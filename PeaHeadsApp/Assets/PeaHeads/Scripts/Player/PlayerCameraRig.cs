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
        public Vector2 HeadInput => _head;
        public float Pitch => _pitch;

        private PlantingController _planting;
        private PlayerInput _input;
        private Vector3 _pivotBase;
        private Vector2 _head;
        private Vector2 _headTarget;
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
        public void ApplyRemote(Vector2 head, float pitch)
        {
            _headTarget = head;
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
                _headTarget = input.Head;
                IsZoomed = input.ZoomHeld;
                float fov = IsZoomed ? _baseFov / _zoomFactor : _baseFov;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_zoomSpeed * Time.deltaTime));
            }
            else
            {
                _pitch = Mathf.Lerp(_pitch, _pitchTarget, blend);
            }

            _head = Vector2.Lerp(_head, _headTarget, blend);
            _headPivot.localPosition = ClampedHeadPosition();
            _headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
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
    }
}
