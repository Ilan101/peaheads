using System;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI placeholder HUD; swap for a Canvas when the art pass happens.
    public class PlayerHUD : MonoBehaviour
    {
        private const float Margin = 20f;
        private const float IconSize = 53f;
        private const int PlantDots = 12;

        [SerializeField, Range(16f, 96f)] private float _heartSize = 48f;
        [Tooltip("Webcam view width; height follows the tracker's 16:9 rectangle.")]
        [SerializeField, Range(128f, 512f)] private float _trackerWidth = 256f;
        [SerializeField, Range(8f, 64f)] private float _crosshairSize = 22f;
        [SerializeField, Range(0.05f, 1f)] private float _hitMarkerTime = 0.25f;
        [Tooltip("Red screen flash when this player takes damage.")]
        [SerializeField, Range(0.05f, 2f)] private float _damageFlashTime = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _damageFlashAlpha = 0.35f;
        [Tooltip("Scope vignette darkness at full zoom (0 = off).")]
        [SerializeField, Range(0f, 1f)] private float _scopeVignette = 0.8f;
        [SerializeField, Range(1f, 20f)] private float _scopeFadeSpeed = 8f;
        [Tooltip("Scope circle size relative to the screen height.")]
        [SerializeField, Range(0.8f, 2.5f)] private float _scopeCircleSize = 1.4f;
        [SerializeField] private Color _deathTint = new(0.35f, 0f, 0f, 0.6f);

        private static Texture2D _heart;
        private static Texture2D _ring;
        private static Texture2D _cross;
        private static Texture2D _vignette;
        private static Texture2D _camera;
        private static Texture2D _flower;
        private static Texture2D _thorn;
        private static Texture2D _sprout;
        private static Texture2D _dot;
        private static Texture2D _disc;

        // Local player is scoped in: HUDs hide everything but the crosshair (read by ScoreHUD too).
        public static bool Scoped { get; private set; }

        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private HeadTrackingInput _tracking;
        private NetworkObject _networkObject;
        private PlayerShooting _shooting;
        private int _lastHealth;
        private float _flashUntil;
        private float _markerUntil;
        private bool _markerKill;
        private float _scope;
        private bool _wasDead;
        private float _diedAt;
        private GUIStyle _deathTitle;
        private GUIStyle _deathLine;
        private MatchManager _match;

        private int Team => _networkObject && _networkObject.IsSpawned ? MatchManager.TeamOf(_networkObject.OwnerClientId) : 0;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _health = GetComponent<PlayerHealth>();
            _rig = GetComponent<PlayerCameraRig>();
            _tracking = GetComponent<HeadTrackingInput>();
            _networkObject = GetComponent<NetworkObject>();
            _shooting = GetComponent<PlayerShooting>();
        }

        private void OnEnable()
        {
            _lastHealth = _health.Current;
            if (_shooting) _shooting.HitConfirmed += OnHitConfirmed;
        }

        private void OnDisable()
        {
            Scoped = false;
            if (_shooting) _shooting.HitConfirmed -= OnHitConfirmed;
        }

        private void Update()
        {
            if (_health.Current < _lastHealth) _flashUntil = Time.time + _damageFlashTime;
            _lastHealth = _health.Current;
            _scope = Mathf.MoveTowards(_scope, _rig.IsZoomed ? 1f : 0f, _scopeFadeSpeed * Time.deltaTime);
            bool dead = _health.IsDead;
            if (dead && !_wasDead) _diedAt = Time.time;
            _wasDead = dead;
            Scoped = _scope > 0.5f && !dead;
        }

        private void OnHitConfirmed(bool killed)
        {
            _markerUntil = Time.time + _hitMarkerTime;
            _markerKill = killed;
        }

        private void OnGUI()
        {
            if (StoryIntro.IsPlaying) return;
            if (_health.IsDead)
            {
                DrawDeathScreen();
                return;
            }
            DrawScopeVignette();
            DrawDamageFlash();
            if (!Scoped) DrawStatus();
            DrawCrosshair();
        }

        // Tracker view (or a red camera without webcam), team badge, plant state, hearts. Hidden while scoped in.
        private void DrawStatus()
        {
            if (_tracking && _tracking.State == TrackingState.Tracking)
                DrawTrackerView(new Rect(Margin, Margin, _trackerWidth, _trackerWidth * 9f / 16f), _tracking.Head);
            else DrawWebcamMissing(new Rect(Margin, Margin, IconSize, IconSize));

            int team = Team;
            float heartsY = Screen.height - Margin - _heartSize;
            float iconsY = heartsY - IconSize - 10f;
            DrawTeamBadge(new Rect(Margin, iconsY, IconSize, IconSize), team);
            DrawPlantState(new Rect(Margin + IconSize + 12f, iconsY, IconSize, IconSize));
            DrawHearts(Margin, heartsY);
        }

        // Round scope view: clear centre, dark edges; the strips beside the circle are filled solid.
        private void DrawScopeVignette()
        {
            if (_scope <= 0f || _scopeVignette <= 0f) return;
            _vignette ??= VignetteTexture();
            float side = Screen.height * _scopeCircleSize;
            float x = (Screen.width - side) / 2f;
            GUI.color = new Color(1f, 1f, 1f, _scope * _scopeVignette);
            GUI.DrawTexture(new Rect(x, (Screen.height - side) / 2f, side, side), _vignette);
            GUI.color = new Color(0f, 0f, 0f, _scope * _scopeVignette);
            if (x > 0f) GUI.DrawTexture(new Rect(0f, 0f, x, Screen.height), Texture2D.whiteTexture);
            if (x > 0f) GUI.DrawTexture(new Rect(x + side, 0f, x, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Black, alpha 0 inside ~55% of the radius rising to 1 at the edge.
        private static Texture2D VignetteTexture()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float u = (px + 0.5f) / size * 2f - 1f, v = (py + 0.5f) / size * 2f - 1f;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, Mathf.Sqrt(u * u + v * v)));
                pixels[py * size + px] = new Color32(0, 0, 0, (byte)(alpha * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // Dark red overlay, who got you, respawn countdown. Tint only once the match has ended (its banner says the rest).
        private void DrawDeathScreen()
        {
            GUI.color = _deathTint;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            _match ??= FindFirstObjectByType<MatchManager>();
            if (_match && _match.IsEnded) return;

            _deathTitle ??= new GUIStyle(GUI.skin.label)
                { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _deathLine ??= new GUIStyle(_deathTitle) { fontSize = 26, richText = true };

            int rival = 1 - Team;
            string rivalName = $"<color=#{ColorUtility.ToHtmlStringRGB(Faction.Color(rival))}>{Faction.Name(rival)}</color>";
            float y = Screen.height * 0.36f;
            GUI.Label(new Rect(0f, y, Screen.width, 70f), "SPAT OUT!", _deathTitle);
            GUI.Label(new Rect(0f, y + 72f, Screen.width, 36f), $"{rivalName} got you", _deathLine);
            if (_health.RespawnDelay <= 0f) return;
            int left = Mathf.CeilToInt(Mathf.Max(0f, _health.RespawnDelay - (Time.time - _diedAt)));
            GUI.Label(new Rect(0f, y + 112f, Screen.width, 36f), $"Respawning in {left}...", _deathLine);
        }

        private void DrawDamageFlash()
        {
            float left = _flashUntil - Time.time;
            if (left <= 0f) return;
            GUI.color = new Color(0.85f, 0f, 0f, _damageFlashAlpha * left / _damageFlashTime);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Hollow ring; an X flashes around it when one of our peas lands. On a kill the whole crosshair goes red.
        private void DrawCrosshair()
        {
            _ring ??= MaskTexture((u, v) => { float r = Mathf.Sqrt(u * u + v * v); return r <= 1f && r >= 0.72f; });
            _cross ??= MaskTexture((u, v) =>
                Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) < 0.16f && Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) is > 0.45f and <= 1f);

            Vector2 centre = new(Screen.width / 2f, Screen.height / 2f);
            float size = _rig.IsZoomed ? _crosshairSize * 1.3f : _crosshairSize;
            bool marker = Time.time < _markerUntil;
            // A kill turns the whole crosshair (ring + X) red while the marker shows.
            GUI.color = marker && _markerKill ? new Color(1f, 0.2f, 0.2f) : Color.white;
            GUI.DrawTexture(new Rect(centre.x - size / 2f, centre.y - size / 2f, size, size), _ring);
            if (marker)
            {
                float cross = size * 1.8f;
                GUI.DrawTexture(new Rect(centre.x - cross / 2f, centre.y - cross / 2f, cross, cross), _cross);
            }
            GUI.color = Color.white;
        }

        // Red camera in the corner while the webcam isn't driving the head (keyboard fallback).
        private static void DrawWebcamMissing(Rect rect)
        {
            _camera ??= UiSprites.Camera().texture;
            GUI.color = new Color(0.85f, 0.15f, 0.15f);
            GUI.DrawTexture(rect, _camera);
            GUI.color = Color.white;
        }

        // Bloom = flower, Blight = thorn, in the team colour.
        private static void DrawTeamBadge(Rect rect, int team)
        {
            _flower ??= UiSprites.Flower().texture;
            _thorn ??= UiSprites.Thorn().texture;
            GUI.color = Faction.Color(team);
            GUI.DrawTexture(rect, team == 0 ? _flower : _thorn);
            GUI.color = Color.white;
        }

        // Sprout in a dark disc; a ring of dots fills while planting and empties while unplanting.
        // Grey = can walk, green + full ring = planted, can fire.
        private void DrawPlantState(Rect rect)
        {
            _sprout ??= UiSprites.Sprout().texture;
            _dot ??= UiSprites.Circle(16).texture;
            _disc ??= UiSprites.Circle().texture;
            var green = new Color(0.5f, 0.9f, 0.35f);

            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(rect, _disc);
            int lit = Mathf.RoundToInt(_planting.Progress * PlantDots);
            float radius = rect.width * 0.5f, dot = rect.width * 0.14f;
            for (int i = 0; i < PlantDots; i++)
            {
                float a = Mathf.PI * 0.5f - i * 2f * Mathf.PI / PlantDots;
                Vector2 c = rect.center + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (radius - dot * 0.5f);
                GUI.color = i < lit ? green : new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(new Rect(c.x - dot * 0.5f, c.y - dot * 0.5f, dot, dot), _dot);
            }
            GUI.color = _planting.CanShoot ? green : new Color(0.75f, 0.75f, 0.75f);
            float inset = rect.width * 0.22f;
            GUI.DrawTexture(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f),
                _sprout);
            GUI.color = Color.white;
        }

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
            // Filled heart from the implicit curve (x² + y² − 1)³ − x²y³ ≤ 0.
            _heart ??= MaskTexture((u, v) =>
            {
                float x = u * 1.3f, y = v * 1.35f + 0.05f;
                float a = x * x + y * y - 1f;
                return a * a * a - x * x * y * y * y <= 0f;
            });
            for (int i = 0; i < _health.Max; i++)
            {
                GUI.color = i < _health.Current ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.2f, 0.2f, 0.2f, 0.6f);
                GUI.DrawTexture(new Rect(x + i * (_heartSize + 6f), y, _heartSize, _heartSize), _heart);
            }
            GUI.color = Color.white;
        }

        // White mask texture (tint with GUI.color); inside(u, v) gets u, v in -1..1, v up.
        private static Texture2D MaskTexture(Func<float, float, bool> inside)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float u = (px + 0.5f) / size * 2f - 1f;
                float v = (py + 0.5f) / size * 2f - 1f;
                pixels[py * size + px] = new Color32(255, 255, 255, inside(u, v) ? (byte)255 : (byte)0);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
