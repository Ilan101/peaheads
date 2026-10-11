using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    [RequireComponent(typeof(PlantingController), typeof(PlayerCameraRig), typeof(PlayerInput))]
    public class PlayerShooting : NetworkBehaviour
    {
        // Host accepts shots slightly early so network jitter doesn't eat legit ones.
        private const float HostCooldownTolerance = 0.8f;
        // Reject fire origins further than this from the shooter (bad/forged RPC data).
        private const float MaxOriginDistance = 3f;
        private const int KillcamChunkSize = 4000;
        private const int KillcamChunksPerFrame = 12;

        [SerializeField] private PeaProjectile _peaPrefab;
        [SerializeField] private Transform _mouth;
        [SerializeField, Range(0f, 2f)] private float _cooldown = 0.6f;
        [SerializeField, Range(10f, 300f)] private float _maxAimDistance = 100f;
        [SerializeField] private AudioClip _fireSfx;
        [Tooltip("Pea break burst played where a pea hits, on every machine.")]
        [SerializeField] private ParticleSystem _splatPrefab;
        [SerializeField, Range(0.2f, 5f)] private float _splatLifetime = 1.5f;

        // Owner only: one of this player's peas damaged something (true = it was a kill).
        public event Action<bool> HitConfirmed;
        // Owner only: fire was pressed while not planted (HUD hint).
        public event Action FireBlocked;

        // 0 right after a shot, 1 when the next one is allowed.
        public float CooldownProgress =>
            _cooldown <= 0f ? 1f : Mathf.Clamp01(1f - (_nextFireTime - Time.time) / _cooldown);

        private PlantingController _planting;
        private PlayerCameraRig _rig;
        private PlayerInput _input;
        private PlayerHealth _health;
        private float _nextFireTime;
        private float _hostNextFireTime;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _rig = GetComponent<PlayerCameraRig>();
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            PlayerInputFrame input = _input.Current;
            if (input.FirePressed && !TryFire(input.ZoomHeld) && !_planting.CanShoot && !_health.IsDead)
                FireBlocked?.Invoke();
        }

        // Owner only. Aim at whatever is under the crosshair, but launch from the mouth.
        private bool TryFire(bool zoomed)
        {
            if (!CanFire(_nextFireTime)) return false;
            _nextFireTime = Time.time + _cooldown;

            Transform cam = _rig.Camera.transform;
            Vector3 target = PhysicsUtil.CastIgnoring(transform, cam.position, 0f, cam.forward, _maxAimDistance,
                out RaycastHit hit, QueryTriggerInteraction.Collide)
                ? hit.point
                : cam.position + cam.forward * _maxAimDistance;
            FireRpc(_mouth.position, (target - _mouth.position).normalized, cam.position, zoomed);
            PlayFireSfx(_mouth.position); // the shooter hears it now; everyone else via PlayFireSfxRpc
            _rig.AddShake(0.25f);
            return true;
        }

        private bool CanFire(float nextFireTime) =>
            _planting.CanShoot && !_health.IsDead && Time.time >= nextFireTime;

        // Owner is trusted for plant state (host's synced copy lags, which would eat the first shot after planting).
        // Comparisons are written so NaN fails them.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FireRpc(Vector3 origin, Vector3 direction, Vector3 sweepFrom, bool zoomed)
        {
            if (_health.IsDead || Time.time < _hostNextFireTime) return;
            if (!(direction.sqrMagnitude >= 0.5f)) return;
            if (!IsNearShooter(origin) || !IsNearShooter(sweepFrom)) return;
            _hostNextFireTime = Time.time + _cooldown * HostCooldownTolerance;

            PeaProjectile pea = Instantiate(_peaPrefab, origin, Quaternion.LookRotation(direction.normalized));
            pea.SetTeam(MatchManager.TeamOf(OwnerClientId));
            pea.NetworkObject.Spawn(destroyWithScene: true);
            pea.Launch(transform, sweepFrom, zoomed);
            PlayFireSfxRpc(origin);
        }

        private bool IsNearShooter(Vector3 point) =>
            (point - transform.position).sqrMagnitude <= MaxOriginDistance * MaxOriginDistance;

        // Server, from PeaProjectile.Impact.
        public void ReportImpact(Vector3 point, Vector3 normal, bool damaged, bool killed, ulong victim)
        {
            SplatRpc(point, normal);
            if (damaged) HitConfirmRpc(killed, victim);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SplatRpc(Vector3 point, Vector3 normal)
        {
            GameAudio.PlayAt(Sfx.Splat, point, 0.9f, 0.12f);
            if (!_splatPrefab) return;
            Quaternion facing = normal.sqrMagnitude > 0.01f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            ParticleSystem splat = Instantiate(_splatPrefab, point, facing);
            // Same colour as the pea (shooter's faction); restart so the burst uses it.
            splat.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Color team = Faction.Color(MatchManager.TeamOf(OwnerClientId));
            ParticleSystem.MainModule main = splat.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(team.r * 0.75f, team.g * 0.75f, team.b * 0.75f), team);
            splat.Play();
            Destroy(splat.gameObject, _splatLifetime);
        }

        [Rpc(SendTo.Owner)]
        private void HitConfirmRpc(bool killed, ulong victim)
        {
            HitConfirmed?.Invoke(killed);
            if (killed && KillcamView.Instance) KillcamView.Instance.OnLocalKill(this, victim);
        }

        // Owner: stream a killcam clip to the victim, a few chunks per frame.
        public void SendKillcam(ulong victim, int clipId, byte[] data) => StartCoroutine(SendChunks(victim, clipId, data));

        private IEnumerator SendChunks(ulong victim, int clipId, byte[] data)
        {
            var chunks = KillClip.Split(data, KillcamChunkSize);
            for (int i = 0; i < chunks.Count; i++)
            {
                if (!IsSpawned) yield break;
                KillcamChunkRpc(clipId, i, chunks.Count, chunks[i], RpcTarget.Single(victim, RpcTargetUse.Temp));
                if (i % KillcamChunksPerFrame == KillcamChunksPerFrame - 1) yield return null;
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void KillcamChunkRpc(int clipId, int index, int count, byte[] chunk, RpcParams rpcParams = default)
        {
            if (KillcamView.Instance) KillcamView.Instance.ReceiveChunk(clipId, index, count, chunk);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayFireSfxRpc(Vector3 position)
        {
            if (!IsOwner) PlayFireSfx(position);
        }

        // Bubble pop (generated); the inspector clip is only a fallback.
        private void PlayFireSfx(Vector3 position)
        {
            if (!GameAudio.PlayAt(Sfx.Pop, position, 1f, 0.1f) && _fireSfx) AudioSource.PlayClipAtPoint(_fireSfx, position);
        }
    }
}
