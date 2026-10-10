using UnityEngine;

namespace PeaHeads
{
    public enum PlantState : byte { Unplanted, Planting, Planted, Unplanting }

    public class PlantingController : MonoBehaviour
    {
        [Tooltip("Seconds to plant.")]
        [SerializeField, Range(0.1f, 3f)] private float _plantTime = 1f;
        [Tooltip("Seconds to unplant.")]
        [SerializeField, Range(0.1f, 3f)] private float _unplantTime = 1f;
        [SerializeField] private Transform _visual;
        [SerializeField, Range(0.3f, 1f)] private float _plantedSquash = 0.8f;

        public PlantState State { get; private set; }
        public bool CanMove => State == PlantState.Unplanted;
        public bool CanShoot => State == PlantState.Planted;
        // Current vertical squash of the body (1 = standing). The head rides down with it.
        public float HeightScale => Mathf.Lerp(1f, _plantedSquash, _progress);

        private PlayerInput _input;
        private PlayerHealth _health;
        private float _timer;
        private float _progress;
        private PlantState? _lastRemote;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        // Remote copies get no input, so this only advances the squash animation for them.
        private void Update() => Tick(Time.deltaTime, _input.Current.PlantPressed && !_health.IsDead);

        public void Tick(float deltaTime, bool plantPressed)
        {
            if (plantPressed) Toggle();

            if (State is PlantState.Planting or PlantState.Unplanting)
            {
                _timer -= deltaTime;
                if (_timer <= 0f) State = State == PlantState.Planting ? PlantState.Planted : PlantState.Unplanted;
            }

            float target = State is PlantState.Planting or PlantState.Planted ? 1f : 0f;
            _progress = Mathf.MoveTowards(_progress, target, deltaTime / (target > 0f ? _plantTime : _unplantTime));
            if (_visual) _visual.localScale = new Vector3(1f, HeightScale, 1f);
        }

        // Reacts only to new network values, so a stale repeat can't undo a transition finished locally.
        public void ApplyRemote(PlantState state)
        {
            if (state == _lastRemote) return;
            _lastRemote = state;
            State = state;
            _timer = Duration(state);
        }

        public void ResetState()
        {
            State = PlantState.Unplanted;
            _timer = 0f;
            _progress = 0f;
            if (_visual) _visual.localScale = Vector3.one;
        }

        // Presses during a transition are ignored so planting is a real commitment.
        private void Toggle()
        {
            if (State == PlantState.Unplanted) Begin(PlantState.Planting);
            else if (State == PlantState.Planted) Begin(PlantState.Unplanting);
        }

        private void Begin(PlantState state)
        {
            State = state;
            _timer = Duration(state);
        }

        private float Duration(PlantState state) => state == PlantState.Unplanting ? _unplantTime : _plantTime;
    }
}
