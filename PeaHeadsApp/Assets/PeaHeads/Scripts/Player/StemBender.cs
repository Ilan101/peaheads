using UnityEngine;

namespace PeaHeads
{
    // Replaces the straight stem with a chain of capsules curving from the leaf base up into the head, so the
    // stem follows the head lean. Visual only: no colliders, reads transforms (already synced), works on
    // remote copies and dummies. Everything runs in root-local space so teleports/respawns can't stretch it.
    public class StemBender : MonoBehaviour
    {
        private const float MinLength = 0.01f;
        private const float MaxThicknessRatio = 2f;
        private const float MaxSpringStep = 1f / 30f;

        [SerializeField] private Transform _sourceStem;
        [Tooltip("Leaf base cylinder: the stem starts at its top centre.")]
        [SerializeField] private Transform _baseAnchor;
        [SerializeField] private Transform _headPivot;
        [Tooltip("Stem ends this far below the head pivot, hidden inside the head.")]
        [SerializeField] private float _headSink = 0.3f;
        [SerializeField] private Mesh _segmentMesh;
        [SerializeField] private int _segments = 7;
        [SerializeField] private float _thickness = 0.14f;
        [Tooltip("Extra length per segment so joints don't gap when bent.")]
        [SerializeField] private float _overlap = 0.04f;
        [Tooltip("0 = constant thickness, 1 = keeps volume (thicker when squashed, thinner when stretched).")]
        [SerializeField] private float _volumeKeep = 0.5f;

        [Header("Lag")]
        [SerializeField] private float _springStiffness = 120f;
        [SerializeField] private float _springDamping = 14f;
        [Tooltip("0 = no lag.")]
        [SerializeField] private float _springAmount = 0.5f;

        private Transform[] _parts;
        private Vector3[] _points;
        private float _restLength;
        private Vector3 _springPosition;
        private Vector3 _springVelocity;

        private void Awake()
        {
            Material material = _sourceStem.GetComponent<MeshRenderer>().sharedMaterial;
            // Deactivated, not renderer-disabled: PlayerHealth.SetAlive re-enables every child renderer.
            _sourceStem.gameObject.SetActive(false);

            _parts = new Transform[_segments];
            _points = new Vector3[_segments + 1];
            for (int i = 0; i < _segments; i++)
            {
                var part = new GameObject($"StemSegment{i}");
                part.transform.SetParent(transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = _segmentMesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = material;
                _parts[i] = part.transform;
            }

            Vector3 tip = LocalTip();
            _restLength = Mathf.Max(MinLength, Vector3.Distance(LocalBase(), tip));
            _springPosition = tip;
        }

        // After PlayerCameraRig.Update has placed the head for this frame.
        private void LateUpdate()
        {
            Vector3 p0 = LocalBase();
            Vector3 p2 = LocalTip();
            SpringStep(ref _springPosition, ref _springVelocity, p2, _springStiffness, _springDamping,
                Time.deltaTime);
            Vector3 p1 = p0 + Vector3.up * (Vector3.Distance(p0, p2) * 0.5f)
                         + (_springPosition - p2) * _springAmount;

            float length = 0f;
            _points[0] = p0;
            for (int i = 1; i <= _segments; i++)
            {
                _points[i] = Bezier(p0, p1, p2, (float)i / _segments);
                length += Vector3.Distance(_points[i - 1], _points[i]);
            }

            float thickness = Thickness(_thickness, _restLength, length, _volumeKeep);
            for (int i = 0; i < _segments; i++)
            {
                SegmentPose(_points[i], _points[i + 1], _overlap, thickness, out Vector3 position,
                    out Quaternion rotation, out Vector3 scale);
                _parts[i].SetLocalPositionAndRotation(position, rotation);
                _parts[i].localScale = scale;
            }
        }

        // Cylinder mesh spans y -1..1, so (0, 1, 0) is the top centre.
        private Vector3 LocalBase() => transform.InverseTransformPoint(_baseAnchor.TransformPoint(Vector3.up));

        private Vector3 LocalTip() => transform.InverseTransformPoint(_headPivot.position) - Vector3.up * _headSink;

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        // Capsule mesh is 1 wide and 2 tall at scale 1.
        public static void SegmentPose(Vector3 from, Vector3 to, float overlap, float thickness,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            Vector3 span = to - from;
            float length = span.magnitude;
            position = (from + to) * 0.5f;
            rotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, span / length) : Quaternion.identity;
            scale = new Vector3(thickness, (length + overlap) * 0.5f, thickness);
        }

        public static float Thickness(float thickness, float restLength, float length, float volumeKeep)
        {
            float ratio = Mathf.Pow(restLength / Mathf.Max(length, MinLength), volumeKeep);
            return thickness * Mathf.Clamp(ratio, 1f / MaxThicknessRatio, MaxThicknessRatio);
        }

        // Semi-implicit Euler; the step is capped so a frame hitch can't make it diverge.
        public static void SpringStep(ref Vector3 position, ref Vector3 velocity, Vector3 target,
            float stiffness, float damping, float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, MaxSpringStep);
            velocity += ((target - position) * stiffness - velocity * damping) * dt;
            position += velocity * dt;
        }
    }
}
