using UnityEngine;
using UnityEngine.InputSystem;

namespace PeaHeads
{
    [RequireComponent(typeof(CharacterController), typeof(PlantingController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float _speed = 5f;

        private CharacterController _controller;
        private PlantingController _planting;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _planting = GetComponent<PlantingController>();
        }

        private void Update()
        {
            Vector3 input = Vector3.zero;
            Keyboard kb = Keyboard.current;
            if (kb != null && _planting.CanMove)
            {
                input.x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                input.z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            }

            Vector3 velocity = transform.TransformDirection(Vector3.ClampMagnitude(input, 1f)) * _speed;
            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Physics.gravity.y * Time.deltaTime;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }
    }
}
