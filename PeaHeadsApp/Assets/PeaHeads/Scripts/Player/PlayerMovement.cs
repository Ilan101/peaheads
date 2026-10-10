using Unity.Netcode.Components;
using UnityEngine;

namespace PeaHeads
{
    [RequireComponent(typeof(CharacterController), typeof(PlantingController), typeof(PlayerInput))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField, Range(0f, 20f)] private float _speed = 5f;

        private CharacterController _controller;
        private PlantingController _planting;
        private PlayerInput _input;
        private PlayerHealth _health;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _planting = GetComponent<PlantingController>();
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            // Remote copies are positioned by NetworkTransform.
            if (!_input.enabled) return;

            Vector2 move = _planting.CanMove && !_health.IsDead ? _input.Current.Move : Vector2.zero;
            Vector3 velocity = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(move.x, 0f, move.y), 1f)) * _speed;
            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Physics.gravity.y * Time.deltaTime;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
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
        }
    }
}
