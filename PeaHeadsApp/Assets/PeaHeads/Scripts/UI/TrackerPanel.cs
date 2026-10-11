using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Webcam head indicator shared by the HUD and the lobby: "Press C to Calibrate" over a rounded, see-through 16:9 box
    // with centre lines and a glowing dot where the webcam puts your head (eased, so webcam jitter doesn't show).
    // A gently pulsing red camera instead when the webcam isn't tracking.
    public sealed class TrackerPanel
    {
        private const float LabelHeight = 22f;
        private const float IconSize = 53f;
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);

        public RectTransform Root { get; }

        private readonly Image _box;
        private readonly Image _dot;
        private readonly Image _dotGlow;
        private readonly Image _cameraOff;
        private readonly Text _calibrate;
        private readonly Vector2 _boxHalf;
        private Vector2 _shown;

        // Sits in the top-left corner of parent, offset by pos.
        public TrackerPanel(Transform parent, Vector2 pos, float width)
        {
            float boxHeight = width * 9f / 16f;
            _boxHalf = new Vector2(width * 0.5f - 7f, boxHeight * 0.5f - 7f);
            Root = UiBuild.Node(parent, "Tracker", TopLeft, pos, new Vector2(width, boxHeight + LabelHeight));
            _calibrate = UiBuild.Label(Root, "Press C to Calibrate", 17, Color.white, TextAnchor.MiddleLeft, TopLeft,
                new Vector2(4f, 0f), new Vector2(width, LabelHeight));
            UiBuild.Display(_calibrate);
            _box = UiBuild.Panel(Root, TopLeft, new Vector2(0f, -LabelHeight), new Vector2(width, boxHeight),
                new Color(0f, 0f, 0f, 0.32f), 0.6f);
            UiBuild.Rim(_box, new Color(1f, 1f, 1f, 0.3f));
            UiBuild.Image(_box.transform, null, Vector2.zero, new Vector2(1f, boxHeight - 12f), new Color(1f, 1f, 1f, 0.22f));
            UiBuild.Image(_box.transform, null, Vector2.zero, new Vector2(width - 12f, 1f), new Color(1f, 1f, 1f, 0.22f));
            _dotGlow = UiBuild.Image(_box.transform, UiBuild.Glow, Vector2.zero, new Vector2(34f, 34f), new Color(1f, 1f, 1f, 0.45f));
            _dot = UiBuild.Image(_box.transform, UiBuild.Circle, Vector2.zero, new Vector2(11f, 11f), Color.white);
            _cameraOff = UiBuild.Image(Root, UiSprites.Camera(), TopLeft, Vector2.zero, new Vector2(IconSize, IconSize), Bad);
        }

        public void Refresh(HeadTrackingInput tracking)
        {
            bool live = tracking && tracking.State == TrackingState.Tracking;
            if (_box.gameObject.activeSelf != live) _box.gameObject.SetActive(live); // with its lines, rim and dot
            _calibrate.enabled = live;
            _cameraOff.enabled = !live;
            if (!live)
            {
                _cameraOff.color = UiBuild.WithAlpha(Bad, 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f));
                return;
            }
            Vector2 target = Vector2.Scale(tracking.Head, _boxHalf);
            _shown = Vector2.Lerp(_shown, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            _dot.rectTransform.anchoredPosition = _shown;
            _dotGlow.rectTransform.anchoredPosition = _shown;
        }
    }
}
