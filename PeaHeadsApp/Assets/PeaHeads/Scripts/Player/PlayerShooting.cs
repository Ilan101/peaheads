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

        [SerializeField] private PeaProjectile _peaPrefab;
        [SerializeField] private Transform _mouth;
        [SerializeField] private float _cooldown = 0.6f;
        [SerializeField] private float _maxAimDistance = 100f;
        [SerializeField] private AudioClip _fireSfx;

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
            if (IsSpawned && IsOwner && _input.Current.FirePressed) TryFire();
        }

        // Owner only. Aim at whatever is under the crosshair, but launch from the mouth.
        public bool TryFire()
        {
            if (!CanFire(_nextFireTime)) return false;
            _nextFireTime = Time.time + _cooldown;

            Transform cam = _rig.Camera.transform;
            Vector3 target = PhysicsUtil.CastIgnoring(transform, cam.position, 0f, cam.forward, _maxAimDistance,
                out RaycastHit hit, QueryTriggerInteraction.Collide)
                ? hit.point
                : cam.position + cam.forward * _maxAimDistance;
            FireRpc(_mouth.position, (target - _mouth.position).normalized, cam.position);
            return true;
        }

        private bool CanFire(float nextFireTime) =>
            _planting.CanShoot && !_health.IsDead && Time.time >= nextFireTime;

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector3 origin, Vector3 direction, Vector3 sweepFrom)
        {
            if (!CanFire(_hostNextFireTime)) return;
            if (direction.sqrMagnitude < 0.5f) return;
            if ((origin - transform.position).sqrMagnitude > MaxOriginDistance * MaxOriginDistance) return;
            if ((sweepFrom - transform.position).sqrMagnitude > MaxOriginDistance * MaxOriginDistance) return;
            _hostNextFireTime = Time.time + _cooldown * HostCooldownTolerance;

            PeaProjectile pea = Instantiate(_peaPrefab, origin, Quaternion.LookRotation(direction.normalized));
            pea.NetworkObject.Spawn(destroyWithScene: true);
            pea.Launch(transform, sweepFrom);
            PlayFireSfxRpc(origin);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayFireSfxRpc(Vector3 position)
        {
            if (_fireSfx) AudioSource.PlayClipAtPoint(_fireSfx, position);
        }
    }
}
