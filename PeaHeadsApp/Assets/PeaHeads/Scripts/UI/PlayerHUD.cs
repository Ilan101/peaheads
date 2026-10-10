using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI placeholder HUD; swap for a Canvas when the art pass happens.
    public class PlayerHUD : MonoBehaviour
    {
        private const float Margin = 20f;

        [SerializeField, Range(16f, 96f)] private float _heartSize = 40f;
        [Tooltip("Webcam view width; height follows the tracker's 16:9 rectangle.")]
        [SerializeField, Range(128f, 512f)] private float _trackerWidth = 256f;

        private static Texture2D _heart;

        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private HeadTrackingInput _tracking;
        private NetworkObject _networkObject;
        private GUIStyle _style;
        private GUIStyle _factionStyle;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _health = GetComponent<PlayerHealth>();
            _rig = GetComponent<PlayerCameraRig>();
            _tracking = GetComponent<HeadTrackingInput>();
            _networkObject = GetComponent<NetworkObject>();
        }

        private void OnGUI()
        {
            if (StoryIntro.IsPlaying) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.black } };
            _factionStyle ??= new GUIStyle(_style);

            GUI.Label(new Rect(Margin, Margin - 8f, 700f, 30f), TrackingLabel(), _style);
            if (_tracking && _tracking.State == TrackingState.Tracking)
                DrawTrackerView(new Rect(Margin, Margin + 24f, _trackerWidth, _trackerWidth * 9f / 16f), _tracking.Head);

            int team = _networkObject && _networkObject.IsSpawned ? MatchManager.TeamOf(_networkObject.OwnerClientId) : 0;
            float heartsY = Screen.height - Margin - _heartSize;
            _factionStyle.normal.textColor = Faction.Color(team);
            GUI.Label(new Rect(Margin, heartsY - 64f, 600f, 30f), Faction.Name(team), _factionStyle);
            string ready = _planting.CanShoot ? "  -  READY TO SPIT" : "";
            GUI.Label(new Rect(Margin, heartsY - 32f, 600f, 30f), $"{_planting.State}{ready}", _style);
            DrawHearts(Margin, heartsY);

            float size = _rig.IsZoomed ? 10f : 6f;
            GUI.DrawTexture(new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size),
                Texture2D.whiteTexture);
        }

        private string TrackingLabel() => !_tracking ? "Webcam: off (keyboard)" : _tracking.State switch
        {
            TrackingState.Tracking => "Webcam: tracking",
            TrackingState.NoFace => "Webcam: no face (keyboard)",
            _ => "Webcam: off (keyboard) - run head_tracker.py",
        };

        // Mirrors the tracker's active rectangle: centre = neutral, edges = full lean / rise / duck.
        private static void DrawTrackerView(Rect box, Vector2 head)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(box.center.x, box.y, 1f, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.center.y, box.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = box.center.x + head.x * box.width * 0.5f;
            float y = box.center.y - head.y * box.height * 0.5f;
            GUI.DrawTexture(new Rect(x - 5f, y - 5f, 10f, 10f), Texture2D.whiteTexture);
        }

        private void DrawHearts(float x, float y)
        {
            _heart ??= HeartTexture();
            for (int i = 0; i < _health.Max; i++)
            {
                GUI.color = i < _health.Current ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.2f, 0.2f, 0.2f, 0.6f);
                GUI.DrawTexture(new Rect(x + i * (_heartSize + 6f), y, _heartSize, _heartSize), _heart);
            }
            GUI.color = Color.white;
        }

        // White filled heart from the implicit curve (x² + y² − 1)³ − x²y³ ≤ 0; tinted per heart via GUI.color.
        private static Texture2D HeartTexture()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float u = (px + 0.5f) / size * 2.6f - 1.3f;
                float v = (py + 0.5f) / size * 2.7f - 1.3f;
                float a = u * u + v * v - 1f;
                bool inside = a * a * a - u * u * v * v * v <= 0f;
                pixels[py * size + px] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
