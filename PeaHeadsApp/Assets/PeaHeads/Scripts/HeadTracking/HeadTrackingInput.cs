using UnityEngine;

namespace PeaHeads
{
    public enum TrackingState { Off, NoFace, Tracking }

    // Webcam head position (HeadTrackingReceiver) → proportional lean/duck via PlayerInput's tracker hook. Local
    // player only (listed in PlayerInput's local-only behaviours). Runs before PlayerInput: same-frame response.
    [DefaultExecutionOrder(-110)]
    [RequireComponent(typeof(PlayerInput))]
    public class HeadTrackingInput : MonoBehaviour
    {
        // The tracker sends -1..1: its on-screen box edge = 1 (head_tracker.py BOX_FRAC).
        [Tooltip("Real → in-game movement multiplier for both axes. 2 = lean twice as far as you actually move "
                 + "(capped by PlayerCameraRig max lean / rise / duck).")]
        [SerializeField, Range(0.5f, 6f)] private float _movementMultiplier = 2f;
        [Tooltip("Fraction of the tracker range ignored around neutral.")]
        [SerializeField, Range(0f, 0.3f)] private float _deadZone = 0.03f;
        [Tooltip("Per-axis balance on top of the multiplier. 1 = full lean at the tracker box edge.")]
        [SerializeField, Range(0.5f, 5f)] private float _leanGain = 1f;
        [SerializeField, Range(0.5f, 5f)] private float _verticalGain = 1f;
        [SerializeField] private bool _useMouthFire = true;
        [SerializeField] private bool _useWinkZoom = true;

        public TrackingState State { get; private set; }
        public Vector2 Head { get; private set; }

        private PlayerInput _input;
        private Vector2 _neutral;

        private void Awake() => _input = GetComponent<PlayerInput>();

        private void OnDisable() => SetIdle(TrackingState.Off);

        private void Update()
        {
            HeadTrackingReceiver tracker = HeadTrackingReceiver.Instance;
            if (!tracker || !tracker.isActiveAndEnabled)
            {
                SetIdle(TrackingState.Off);
                return;
            }
            // No call while the face is lost: PlayerInput's tracker data goes stale and the arrow keys take over.
            if (!tracker.FaceVisible)
            {
                SetIdle(TrackingState.NoFace);
                return;
            }

            State = TrackingState.Tracking;
            var raw = new Vector2(tracker.Dx, tracker.Dy);
            if (_input.Current.RecenterPressed) _neutral = raw;
            Head = new Vector2(
                Mathf.Clamp(Shape(Recenter(raw.x, _neutral.x), _deadZone, _leanGain * _movementMultiplier), -1f, 1f),
                Mathf.Clamp(Shape(Recenter(raw.y, _neutral.y), _deadZone, _verticalGain * _movementMultiplier), -1f, 1f));
            _input.SetTrackerInput(Head, _useMouthFire && tracker.Shoot, _useWinkZoom && tracker.Aim);
        }

        private void SetIdle(TrackingState state)
        {
            State = state;
            Head = Vector2.zero;
        }

        // Re-zeroes a -1..1 tracker value around neutral, stretching each side back to full range: the tracker
        // clamps at ±1, so a plain subtraction would leave one side unable to reach full lean.
        public static float Recenter(float value, float neutral)
        {
            float span = value >= neutral ? 1f - neutral : 1f + neutral;
            return span > 0.01f ? (value - neutral) / span : 0f;
        }

        // Proportional past the dead zone, sign kept, not clamped.
        public static float Shape(float value, float deadZone, float gain) =>
            Mathf.Sign(value) * Mathf.Max(0f, Mathf.Abs(value) - deadZone) * gain;
    }
}
