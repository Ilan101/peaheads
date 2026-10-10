using UnityEngine;

namespace PeaHeads
{
    // Leaf base roots into the ground when planted (wider, flatter) and pops back out when unplanted.
    // The overshoot lives here, never in the head height, so the camera can't bounce.
    [RequireComponent(typeof(PlantingController))]
    public class PlantVisuals : MonoBehaviour
    {
        [SerializeField] private Transform _leafBase;
        [Tooltip("Optional. Spread outward (x/z) with the same curve as the leaf base when planting.")]
        [SerializeField] private Transform _groundLeaves;
        [Tooltip("x = width multiplier, y = height multiplier when fully planted.")]
        [SerializeField] private Vector2 _plantedScale = new(1.35f, 0.6f);
        [Tooltip("Plant progress 0→1 to blend. Peaks above 1 for a 'thunk' into the ground.")]
        [SerializeField] private AnimationCurve _plantCurve =
            new(new Keyframe(0f, 0f), new Keyframe(0.8f, 1.12f), new Keyframe(1f, 1f));
        [Tooltip("Evaluated while progress falls 1→0. Dips below 0 for a 'pop' out of the ground.")]
        [SerializeField] private AnimationCurve _unplantCurve =
            new(new Keyframe(0f, 0f), new Keyframe(0.15f, -0.1f), new Keyframe(1f, 1f));
        [SerializeField] private ParticleSystem _plantPuff;

        private PlantingController _planting;
        private Vector3 _restScale;
        private Vector3 _restPosition;
        private Vector3 _groundRest;
        private PlantState _lastState;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _restScale = _leafBase.localScale;
            _restPosition = _leafBase.localPosition;
            if (_groundLeaves) _groundRest = _groundLeaves.localScale;
        }

        private void LateUpdate()
        {
            PlantState state = _planting.State;
            bool rooting = state is PlantState.Planting or PlantState.Planted;
            float blend = (rooting ? _plantCurve : _unplantCurve).Evaluate(_planting.Progress);
            Vector3 planted = new(_plantedScale.x, _plantedScale.y, _plantedScale.x);
            Vector3 scale = Vector3.Scale(_restScale, Vector3.LerpUnclamped(Vector3.one, planted, blend));
            _leafBase.localScale = scale;
            _leafBase.localPosition = new Vector3(_restPosition.x,
                GroundedY(_restPosition.y, _restScale.y, scale.y), _restPosition.z);
            if (_groundLeaves)
            {
                float spread = Mathf.LerpUnclamped(1f, _plantedScale.x, blend);
                _groundLeaves.localScale = new Vector3(_groundRest.x * spread, _groundRest.y, _groundRest.z * spread);
            }

            if (_plantPuff && state == PlantState.Planted && _lastState != PlantState.Planted) _plantPuff.Play();
            _lastState = state;
        }

        // Cylinder half-height equals its y scale, so this keeps the bottom where it was at rest.
        public static float GroundedY(float restY, float restHalfHeight, float halfHeight) =>
            restY - restHalfHeight + halfHeight;
    }
}
