using Unity.Netcode.Components;
using UnityEngine;

namespace PeaHeads
{
    // Owner moves the plant with a CharacterController: quick acceleration into a run, a short glide to a stop
    // (planting brakes harder). Speed01 tells the camera and the step sounds how fast we're going; on remote copies it
    // is measured from the synced position.
    [RequireComponent(typeof(CharacterController), typeof(PlantingController), typeof(PlayerInput))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField, Range(0f, 20f)] private float _speed = 5f;
        [Tooltip("m/s² towards the input direction.")]
        [SerializeField, Range(1f, 120f)] private float _acceleration = 32f;
        [Tooltip("m/s² when letting go of the keys.")]
        [SerializeField, Range(1f, 120f)] private float _deceleration = 26f;
        [Tooltip("m/s² while rooted or knocked out: planting stops you almost at once.")]
        [SerializeField, Range(1f, 200f)] private float _brake = 70f;

        // 0 = standing still, 1 = full speed (horizontal).
        public float Speed01 { get; private set; }

        private CharacterController _controller;
        private PlantingController _planting;
        private PlayerInput _input;
        private PlayerHealth _health;
        private float _verticalVelocity;
        private Vector3 _planar;
        private Vector3 _lastPosition;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _planting = GetComponent<PlantingController>();
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
            _lastPosition = transform.position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            // Remote copies are positioned by NetworkTransform; their speed is measured.
            if (!_input.enabled)
            {
                Vector3 moved = transform.position - _lastPosition;
                moved.y = 0f;
                float measured = dt > 0f && _speed > 0f ? Mathf.Clamp01(moved.magnitude / dt / _speed) : 0f;
                Speed01 = Mathf.Lerp(Speed01, measured, 1f - Mathf.Exp(-10f * dt));
                _lastPosition = transform.position;
                return;
            }

            bool free = _planting.CanMove && !_health.IsDead;
            Vector2 move = free ? _input.Current.Move : Vector2.zero;
            Vector3 wish = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(move.x, 0f, move.y), 1f)) * _speed;
            float rate = !free ? _brake : wish.sqrMagnitude > 0.01f ? _acceleration : _deceleration;
            _planar = Vector3.MoveTowards(_planar, wish, rate * dt);

            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Physics.gravity.y * dt;
            Vector3 velocity = _planar;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * dt);

            // Walls stop the glide too: keep only the speed the controller actually managed.
            Vector3 actual = _controller.velocity;
            actual.y = 0f;
            if (dt > 0f && actual.sqrMagnitude < _planar.sqrMagnitude * 0.81f) _planar = actual;
            Speed01 = _speed > 0f ? Mathf.Clamp01(_planar.magnitude / _speed) : 0f;
            _lastPosition = transform.position;
        }

        // CharacterController overrides direct position writes, so it's disabled around the move.
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false;
            if (TryGetComponent(out NetworkTransform netTransform) && netTransform.IsSpawned)
                netTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            _verticalVelocity = 0f;
            _planar = Vector3.zero;
            Speed01 = 0f;
            _lastPosition = position;
        }
    }
}
