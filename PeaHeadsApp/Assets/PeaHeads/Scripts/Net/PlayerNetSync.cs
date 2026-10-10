using Unity.Netcode;
using UnityEngine;

namespace PeaHeads.Net
{
    // Owner writes head lean, pitch and plant state; other machines apply them to their copy of this plant.
    [RequireComponent(typeof(PlayerInput), typeof(PlayerCameraRig), typeof(PlantingController))]
    public class PlayerNetSync : NetworkBehaviour
    {
        [SerializeField] private Renderer[] _tinted;
        [SerializeField] private Color _hostColor = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _clientColor = new(0.95f, 0.6f, 0.2f);

        private readonly NetworkVariable<Vector2> _head = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> _pitch = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> _plantState = new(writePerm: NetworkVariableWritePermission.Owner);

        private PlayerInput _input;
        private PlayerCameraRig _rig;
        private PlantingController _planting;
        private PlayerMovement _movement;
        private PlayerHealth _health;
        private SpawnPoint _lastSpawn;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _rig = GetComponent<PlayerCameraRig>();
            _planting = GetComponent<PlantingController>();
            _movement = GetComponent<PlayerMovement>();
            _health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            _input.SetLocal(IsOwner);
            _rig.SetLocal(IsOwner);

            Color color = OwnerClientId == NetworkManager.ServerClientId ? _hostColor : _clientColor;
            foreach (Renderer r in _tinted) r.material.color = color;

            _health.Revived += OnRevived;
            if (IsOwner) MoveToSpawn();
            else ApplyRemoteState();
        }

        public override void OnNetworkDespawn() => _health.Revived -= OnRevived;

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                _head.Value = _rig.HeadInput;
                _pitch.Value = _rig.Pitch;
                _plantState.Value = (byte)_planting.State;
            }
            else ApplyRemoteState();
        }

        private void ApplyRemoteState()
        {
            _rig.ApplyRemote(_head.Value, _pitch.Value);
            _planting.ApplyRemote((PlantState)_plantState.Value);
        }

        // Owner only: back to spawn, unplanted. Used on revive and on match restart.
        public void Respawn()
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn();
        }

        private void OnRevived() => Respawn();

        // Host spawns on team 0's side, any client on team 1's (a rejoining client gets a new id, so ids can't be
        // used). Random point each time, never the previous one.
        private void MoveToSpawn()
        {
            SpawnPoint spawn = SpawnPoint.GetRandom(IsHost ? 0 : 1, _lastSpawn);
            if (!spawn) return;
            _lastSpawn = spawn;
            _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
    }
}
