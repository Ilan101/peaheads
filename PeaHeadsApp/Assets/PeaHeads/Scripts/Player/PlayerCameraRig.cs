using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // First person. Mouse = yaw (whole plant) + pitch (head). Head input = bounded positional offset of the
    // head (and the camera inside it), which is what lets you peek around cover without moving the base.
    [RequireComponent(typeof(PlantingController))]
    public class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _headPivot;
        [SerializeField] private Camera _camera;
        [Tooltip("Own head meshes: hidden from the local camera but still cast shadows.")]
        [SerializeField] private Renderer[] _hiddenInFirstPerson;
        [SerializeField] private float _mouseSensitivity = 0.1f;
        [SerializeField] private Vector2 _pitchLimits = new(-70f, 70f);

        [Header("Head offset")]
        [SerializeField] private Vector2 _headRange = new(0.6f, 0.3f);
        [SerializeField] private float _headSmoothing = 10f;
        [Tooltip("Lean stops this far from walls so the camera can't see through them.")]
        [SerializeField] private float _headCollisionRadius = 0.3f;

        [Header("Zoom")]
        [SerializeField] private float _zoomFactor = 1.5f;
        [SerializeField] private float _zoomSpeed = 12f;

        public Camera Camera => _camera;
        public bool IsZoomed { get; private set; }

        private PlantingController _planting;
        private Vector3 _pivotBase;
        private Vector2 _head;
        private float _pitch;
        private float _baseFov;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _pivotBase = _headPivot.localPosition;
            _baseFov = _camera.fieldOfView;
            foreach (Renderer r in _hiddenInFirstPerson) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        private void Start() => SetCursorLocked(true);

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * _mouseSensitivity;
                transform.Rotate(0f, delta.x, 0f);
                _pitch = Mathf.Clamp(_pitch - delta.y, _pitchLimits.x, _pitchLimits.y);
            }

            // ponytail: arrow keys stand in for webcam head offset; tracker input replaces this target.
            Vector2 target = new(Axis(kb.leftArrowKey, kb.rightArrowKey), Axis(kb.downArrowKey, kb.upArrowKey));
            _head = Vector2.Lerp(_head, target, 1f - Mathf.Exp(-_headSmoothing * Time.deltaTime));
            _headPivot.localPosition = ClampedHeadPosition();
            _headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            IsZoomed = locked && mouse.rightButton.isPressed;
            float fov = IsZoomed ? _baseFov / _zoomFactor : _baseFov;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_zoomSpeed * Time.deltaTime));
        }

        // Rest position sinks with the planted body; the lean is swept from there and stops at walls.
        private Vector3 ClampedHeadPosition()
        {
            Vector3 rest = new(_pivotBase.x, _pivotBase.y * _planting.HeightScale, _pivotBase.z);
            Vector3 offset = new(_head.x * _headRange.x, _head.y * _headRange.y, 0f);
            float distance = offset.magnitude;
            if (distance < 0.001f) return rest;

            Vector3 worldRest = transform.TransformPoint(rest);
            Vector3 worldDirection = transform.TransformDirection(offset / distance);
            if (PhysicsUtil.CastIgnoring(transform, worldRest, _headCollisionRadius, worldDirection, distance,
                    out RaycastHit hit))
                distance = hit.distance;
            return rest + offset.normalized * distance;
        }

        private static float Axis(KeyControl negative, KeyControl positive) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
