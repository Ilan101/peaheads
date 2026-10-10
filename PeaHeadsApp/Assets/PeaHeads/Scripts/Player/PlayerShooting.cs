using UnityEngine;
using UnityEngine.InputSystem;

namespace PeaHeads
{
    [RequireComponent(typeof(PlantingController), typeof(PlayerCameraRig))]
    public class PlayerShooting : MonoBehaviour
    {
        [SerializeField] private PeaProjectile _peaPrefab;
        [SerializeField] private Transform _mouth;
        [SerializeField] private float _cooldown = 0.6f;
        [SerializeField] private float _maxAimDistance = 100f;
        [SerializeField] private AudioClip _fireSfx;

        private PlantingController _planting;
        private PlayerCameraRig _rig;
        private float _nextFireTime;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _rig = GetComponent<PlayerCameraRig>();
        }

        private void Update()
        {
            if (Mouse.current?.leftButton.wasPressedThisFrame == true && Cursor.lockState == CursorLockMode.Locked)
                TryFire();
        }

        // Entry point for mouse-open gestures too.
        public bool TryFire()
        {
            if (!_planting.CanShoot || Time.time < _nextFireTime) return false;
            _nextFireTime = Time.time + _cooldown;

            // Aim at whatever is under the crosshair, but launch from the mouth.
            Transform cam = _rig.Camera.transform;
            Vector3 target = PhysicsUtil.CastIgnoring(transform, cam.position, 0f, cam.forward, _maxAimDistance,
                out RaycastHit hit, QueryTriggerInteraction.Collide)
                ? hit.point
                : cam.position + cam.forward * _maxAimDistance;
            Vector3 direction = (target - _mouth.position).normalized;

            PeaProjectile pea = Instantiate(_peaPrefab, _mouth.position, Quaternion.LookRotation(direction));
            pea.Launch(transform, cam.position);
            if (_fireSfx) AudioSource.PlayClipAtPoint(_fireSfx, _mouth.position);
            return true;
        }
    }
}
