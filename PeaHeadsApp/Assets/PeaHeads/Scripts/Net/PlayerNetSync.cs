using Unity.Netcode;
using UnityEngine;

namespace PeaHeads.Net
{
    // Owner writes head lean, pitch and plant state; other machines apply them to their copy of this plant.
    [RequireComponent(typeof(PlayerInput), typeof(PlayerCameraRig), typeof(PlantingController))]
    public class PlayerNetSync : NetworkBehaviour
    {
        [Tooltip("Tinted with the owner's faction colour (Bloom / Blight).")]
        [SerializeField] private Renderer[] _tinted;

        private readonly NetworkVariable<Vector3> _headOffset = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> _pitch = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> _plantState = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> _faceFlags = new(writePerm: NetworkVariableWritePermission.Owner);

        private PlayerInput _input;
        private PlayerCameraRig _rig;
        private PlantingController _planting;
        private PlayerMovement _movement;
        private PlayerHealth _health;
        private SpawnPoint _lastSpawn;
        private PlantFace _plantFace;
        // Owner: the side to respawn on after a death. Set by the host's round-start call, refreshed on team change.
        private int _side;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _rig = GetComponent<PlayerCameraRig>();
            _planting = GetComponent<PlantingController>();
            _movement = GetComponent<PlayerMovement>();
            _health = GetComponent<PlayerHealth>();
            _plantFace = GetComponent<PlantFace>();
        }

        public override void OnNetworkSpawn()
        {
            _input.SetLocal(IsOwner);
            _rig.SetLocal(IsOwner);

            _side = MatchManager.TeamOf(OwnerClientId);
            ApplyTint();
            MatchManager.TeamsChanged += OnTeamsChanged;

            _health.Revived += OnRevived;
            _planting.StateChanged += OnPlantStateChanged;
            if (IsOwner) MoveToSpawn(_side);
            else ApplyRemoteState();
        }

        public override void OnNetworkDespawn()
        {
            _health.Revived -= OnRevived;
            _planting.StateChanged -= OnPlantStateChanged;
            MatchManager.TeamsChanged -= OnTeamsChanged;
        }

        // Digging in / pulling out is heard by everyone nearby; "ready to spit" only by the owner.
        private void OnPlantStateChanged(PlantState state)
        {
            switch (state)
            {
                case PlantState.Planting:
                    GameAudio.PlayAt(Sfx.Plant, transform.position, 0.9f);
                    WorldPolish.PlantBurst(transform.position, false);
                    break;
                case PlantState.Unplanting:
                    GameAudio.PlayAt(Sfx.Unplant, transform.position, 0.9f);
                    WorldPolish.PlantBurst(transform.position, true);
                    break;
                case PlantState.Planted when IsOwner: GameAudio.Play(Sfx.Ready, 0.55f); break;
            }
        }

        private void OnTeamsChanged()
        {
            _side = MatchManager.TeamOf(OwnerClientId);
            ApplyTint();
        }

        // Teams are reshuffled every match, so the head colour follows the current assignment.
        private void ApplyTint()
        {
            Color color = Faction.Color(MatchManager.TeamOf(OwnerClientId));
            foreach (Renderer r in _tinted) r.material.color = color;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                _headOffset.Value = _rig.HeadOffset;
                _pitch.Value = _rig.Pitch;
                _plantState.Value = (byte)_planting.State;
                byte face = FaceFlags.Pack(_input.Current.MouthOpen, _input.Current.ZoomHeld);
                _faceFlags.Value = face;
                ApplyFace(face);
            }
            else ApplyRemoteState();
        }

        private void ApplyRemoteState()
        {
            _rig.ApplyRemote(_headOffset.Value, _pitch.Value);
            _planting.ApplyRemote((PlantState)_plantState.Value);
            ApplyFace(_faceFlags.Value);
        }

        // Dummies and prefabs built before PlantBodyBuilder have no PlantFace.
        private void ApplyFace(byte flags)
        {
            if (!_plantFace) return;
            (bool mouthOpen, bool wink) = FaceFlags.Unpack(flags);
            _plantFace.SetFlags(mouthOpen, wink);
        }

        // Owner only: back to spawn on its team's side, unplanted. Used on revive. Uses the cached side: a revive
        // can arrive before the reshuffled team list, and must not send us back to last round's side.
        public void Respawn() => Respawn(_side);

        // Host → owner at round start: the side comes with the call, so a late team-list update can't misplace us.
        [Rpc(SendTo.Owner)]
        public void RespawnOnSideRpc(byte team)
        {
            _side = team;
            Respawn(team);
        }

        private void Respawn(int team)
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn(team);
        }

        private void OnRevived() => Respawn();

        // Random point on the team's side each time, never the previous one.
        private void MoveToSpawn(int team)
        {
            SpawnPoint spawn = SpawnPoint.GetRandom(team, _lastSpawn);
            if (!spawn) return;
            _lastSpawn = spawn;
            _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
    }
}
