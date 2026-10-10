using UnityEngine;
using UnityEngine.InputSystem;

namespace PeaHeads
{
    public enum PlantState { Unplanted, Planting, Planted, Unplanting }

    public class PlantingController : MonoBehaviour
    {
        [SerializeField] private float _transitionTime = 1f;
        [SerializeField] private Transform _visual;
        [SerializeField] private float _plantedSquash = 0.8f;

        public PlantState State { get; private set; }
        public bool CanMove => State == PlantState.Unplanted;
        public bool CanShoot => State == PlantState.Planted;
        // Current vertical squash of the body (1 = standing). The head rides down with it.
        public float HeightScale => Mathf.Lerp(1f, _plantedSquash, _progress);

        private float _timer;
        private float _progress;

        private void Update()
        {
            if (Keyboard.current?.eKey.wasPressedThisFrame == true) Toggle();

            if (State is PlantState.Planting or PlantState.Unplanting)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) State = State == PlantState.Planting ? PlantState.Planted : PlantState.Unplanted;
            }

            float target = State is PlantState.Planting or PlantState.Planted ? 1f : 0f;
            _progress = Mathf.MoveTowards(_progress, target, Time.deltaTime / _transitionTime);
            if (_visual) _visual.localScale = new Vector3(1f, HeightScale, 1f);
        }

        // Presses during a transition are ignored so planting is a real commitment.
        public void Toggle()
        {
            if (State == PlantState.Unplanted) Begin(PlantState.Planting);
            else if (State == PlantState.Planted) Begin(PlantState.Unplanting);
        }

        private void Begin(PlantState state)
        {
            State = state;
            _timer = _transitionTime;
        }
    }
}
