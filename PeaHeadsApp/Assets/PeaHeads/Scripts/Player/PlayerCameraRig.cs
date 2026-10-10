using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // First person. Look = yaw (whole plant) + pitch (head). Head input = bounded positional offset of the
    // head (and the camera inside it), which is what lets you peek around cover without moving the base.
    [RequireComponent(typeof(PlantingController), typeof(PlayerInput))]
    public class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _headPivot;
        [SerializeField] private Camera _camera;
        [Tooltip("Own head meshes: hidden from the local camera but still cast shadows.")]
        [SerializeField] private Renderer[] _hiddenInFirstPerson;
        [SerializeField] private Vector2 _pitchLimits = new(-70f, 70f);

        [Header("Head offset (metres at full input)")]
        [SerializeField, Range(0f, 2f)] private float _maxLean = 1.1f;
        [SerializeField, Range(0f, 1f)] private float _maxRise = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _maxDuck = 0.6f;
        [SerializeField, Range(1f, 30f)] private float _headSmoothing = 16f;
        [Tooltip("Lean stops this far from walls so the camera can't see through them.")]
        [SerializeField, Range(0.05f, 1f)] private float _headCollisionRadius = 0.3f;

        [Header("Zoom")]
        [SerializeField, Range(1f, 4f)] private float _zoomFactor = 1.5f;
        [SerializeField, Range(1f, 30f)] private float _zoomSpeed = 12f;

        public Camera Camera => _camera;
        public bool IsZoomed { get; private set; }
        public Vector3 HeadOffset { get; private set; }
        public float Pitch => _pitch;

        private PlantingController _planting;
        private PlayerInput _input;
        private Vector3 _pivotBase;
        private Vector2 _head;
        private Vector3 _offsetTarget;
        private float _pitch;
        private float _pitchTarget;
        private float _baseFov;
        private bool _isLocal;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _input = GetComponent<PlayerInput>();
            _pivotBase = _headPivot.localPosition;
            _baseFov = _camera.fieldOfView;
        }

        public void SetLocal(bool isLocal)
        {
            _isLocal = isLocal;
            ShadowCastingMode mode = isLocal ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (Renderer r in _hiddenInFirstPerson) r.shadowCastingMode = mode;
        }

        // Remote copies: targets come from the network instead of local input.
        public void ApplyRemote(Vector3 headOffset, float pitch)
        {
            _offsetTarget = headOffset;
            _pitchTarget = pitch;
        }

        private void Update()
        {
            float blend = 1f - Mathf.Exp(-_headSmoothing * Time.deltaTime);
            if (_isLocal)
            {
                PlayerInputFrame input = _input.Current;
                transform.Rotate(0f, input.Look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - input.Look.y, _pitchLimits.x, _pitchLimits.y);
                IsZoomed = input.ZoomHeld;
                float fov = IsZoomed ? _baseFov / _zoomFactor : _baseFov;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_zoomSpeed * Time.deltaTime));
                _head = Vector2.Lerp(_head, input.Head, blend);
                HeadOffset = ClampedOffset();
            }
            else
            {
                // The owner already swept the offset against walls; just follow it.
                _pitch = Mathf.Lerp(_pitch, _pitchTarget, blend);
                HeadOffset = Vector3.Lerp(HeadOffset, _offsetTarget, blend);
            }

            _headPivot.localPosition = RestPosition() + HeadOffset;
            _headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Rest position sinks with the planted body.
        private Vector3 RestPosition() => new(_pivotBase.x, _pivotBase.y * _planting.HeightScale, _pivotBase.z);

        // Lean/duck from rest, swept so it stops at walls.
        private Vector3 ClampedOffset()
        {
            Vector3 offset = new(_head.x * _maxLean, _head.y * (_head.y > 0f ? _maxRise : _maxDuck), 0f);
            float distance = offset.magnitude;
            if (distance < 0.001f) return Vector3.zero;

            Vector3 direction = offset / distance;
            if (PhysicsUtil.CastIgnoring(transform, transform.TransformPoint(RestPosition()), _headCollisionRadius,
                    transform.TransformDirection(direction), distance, out RaycastHit hit))
                distance = hit.distance;
            return direction * distance;
        }
    }
}
