using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Webcam head indicator shared by the HUD and the lobby: "Press C to Calibrate" over a transparent 16:9 box with
    // centre lines and a dot where the webcam puts your head. Red camera instead when the webcam isn't tracking.
    public sealed class TrackerPanel
    {
        private const float LabelHeight = 22f;
        private const float IconSize = 53f;
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);

        public RectTransform Root { get; }

        private readonly Image _box;
        private readonly Image _dot;
        private readonly Image _cameraOff;
        private readonly Text _calibrate;
        private readonly Vector2 _boxHalf;

        // Sits in the top-left corner of parent, offset by pos.
        public TrackerPanel(Transform parent, Vector2 pos, float width)
        {
            float boxHeight = width * 9f / 16f;
            _boxHalf = new Vector2(width * 0.5f - 5f, boxHeight * 0.5f - 5f);
            Root = UiBuild.Node(parent, "Tracker", TopLeft, pos, new Vector2(width, boxHeight + LabelHeight));
            _calibrate = UiBuild.Label(Root, "Press C to Calibrate", 17, Color.white, TextAnchor.MiddleLeft, TopLeft,
                Vector2.zero, new Vector2(width, LabelHeight));
            _box = UiBuild.Image(Root, null, TopLeft, new Vector2(0f, -LabelHeight), new Vector2(width, boxHeight),
                new Color(0f, 0f, 0f, 0.35f));
            UiBuild.Image(_box.transform, null, Vector2.zero, new Vector2(1f, boxHeight), new Color(1f, 1f, 1f, 0.25f));
            UiBuild.Image(_box.transform, null, Vector2.zero, new Vector2(width, 1f), new Color(1f, 1f, 1f, 0.25f));
            _dot = UiBuild.Image(_box.transform, UiBuild.Circle, Vector2.zero, new Vector2(10f, 10f), Color.white);
            _cameraOff = UiBuild.Image(Root, UiSprites.Camera(), TopLeft, Vector2.zero, new Vector2(IconSize, IconSize), Bad);
        }

        public void Refresh(HeadTrackingInput tracking)
        {
            bool live = tracking && tracking.State == TrackingState.Tracking;
            _box.enabled = live;
            _dot.enabled = live;
            _calibrate.enabled = live;
            _cameraOff.enabled = !live;
            if (live) _dot.rectTransform.anchoredPosition = Vector2.Scale(tracking.Head, _boxHalf);
        }
    }
}
