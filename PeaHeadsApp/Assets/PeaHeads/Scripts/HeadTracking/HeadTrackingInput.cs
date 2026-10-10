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
        [Tooltip("Real head movement (in face widths) ignored around neutral.")]
        [SerializeField, Range(0f, 0.2f)] private float _deadZone = 0.03f;
        [Tooltip("Exaggeration. Full in-game lean is reached at deadZone + 1/gain face widths of real lean.")]
        [SerializeField, Range(0.5f, 10f)] private float _leanGain = 3.5f;
        [SerializeField, Range(0.5f, 10f)] private float _verticalGain = 4f;
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
                Mathf.Clamp(Shape(raw.x - _neutral.x, _deadZone, _leanGain), -1f, 1f),
                Mathf.Clamp(Shape(raw.y - _neutral.y, _deadZone, _verticalGain), -1f, 1f));
            _input.SetTrackerInput(Head, _useMouthFire && tracker.Shoot, _useWinkZoom && tracker.Aim);
        }

        private void SetIdle(TrackingState state)
        {
            State = state;
            Head = Vector2.zero;
        }

        // Proportional past the dead zone, sign kept, not clamped.
        public static float Shape(float value, float deadZone, float gain) =>
            Mathf.Sign(value) * Mathf.Max(0f, Mathf.Abs(value) - deadZone) * gain;
    }
}
