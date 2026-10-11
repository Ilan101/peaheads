using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Local player's HUD (uGUI, built in code): small transparent head-position box top-left (red camera icon without
    // webcam); bottom-left team badge (flower / thorn), plant dial (sprout + ring of dots that fills while planting)
    // with a status word, and hearts; crosshair with hit marker (all red on a kill); round scope view while zoomed; damage / low-health vignette; "spat
    // out" death screen; kill toast and feedback sounds. Owner only (PlayerInput's local-only behaviours); hidden
    // during the story. While scoped in, the HUD and scoreboard hide (ScoreHUD reads Scoped); crosshair, minimap and killcam stay.
    public class PlayerHUD : MonoBehaviour
    {
        private const float Margin = 20f;
        private const float TrackerMargin = 6f; // head tracker sits tight in the top-left corner
        private const float IconSize = 53f;
        private const int PlantDots = 12;
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 TopRight = new(1f, 1f);
        private static readonly Vector2 BottomLeft = new(0f, 0f);
        private static readonly Vector2 BottomRight = new(1f, 0f);
        private static readonly Vector2 Centre = new(0.5f, 0.5f);
        private static readonly Vector2 MidLeft = new(0f, 0.5f);
        private static readonly Vector2 MidRight = new(1f, 0.5f);
        private static readonly Color PlantGreen = new(0.5f, 0.9f, 0.35f);
        private static readonly Color Warn = new(1f, 0.78f, 0.3f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);
        private static readonly Color HeartRed = new(0.92f, 0.17f, 0.24f);

        [SerializeField, Range(16f, 96f)] private float _heartSize = 48f;
        [Tooltip("Webcam view width; height follows the tracker's 16:9 rectangle.")]
        [SerializeField, Range(128f, 512f)] private float _trackerWidth = 256f;
        [SerializeField, Range(8f, 64f)] private float _crosshairSize = 22f;
        [SerializeField, Range(0.05f, 1f)] private float _hitMarkerTime = 0.25f;
        [Tooltip("Red screen-edge flash when this player takes damage.")]
        [SerializeField, Range(0.05f, 2f)] private float _damageFlashTime = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _damageFlashAlpha = 0.35f;
        [Tooltip("Scope vignette darkness at full zoom (0 = off).")]
        [SerializeField, Range(0f, 1f)] private float _scopeVignette = 0.8f;
        [SerializeField, Range(1f, 20f)] private float _scopeFadeSpeed = 8f;
        [Tooltip("Scope circle size relative to the screen height.")]
        [SerializeField, Range(0.8f, 2.5f)] private float _scopeCircleSize = 1.4f;
        [SerializeField] private Color _deathTint = new(0.35f, 0f, 0f, 0.6f);

        // Local player is scoped in: HUDs hide everything but the crosshair.
        public static bool Scoped { get; private set; }

        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private HeadTrackingInput _tracking;
        private NetworkObject _networkObject;
        private PlayerShooting _shooting;
        private MatchManager _match;

        private int _lastHealth;
        private float _scope;
        private float _damagedAt = float.NegativeInfinity;
        private float _deadAt = float.NegativeInfinity;
        private float _markerAt = float.NegativeInfinity;
        private bool _markerKill;
        private float _blockedAt = float.NegativeInfinity;
        private float _toastAt = float.NegativeInfinity;
        private float[] _heartLostAt = new float[0];

        // Draw order: HUD 40 < killcam 42 < minimap 50 < scope vignette 51 < crosshair 52, so the scope darkens the
        // minimap and killcam but never the crosshair.
        private RectTransform _canvas, _scopeCanvas, _crosshairCanvas;
        private RectTransform _scopeCircle, _scopeLeft, _scopeRight;
        private Image _scopeImage, _scopeLeftImage, _scopeRightImage;
        private Image _damageVignette;
        private RectTransform _tracker;
        private Image _trackerBox, _dot, _cameraOff;
        private Text _calibrateText;
        private Vector2 _boxHalf;
        private RectTransform _status;
        private Image _badge, _sprout;
        private readonly Image[] _plantDots = new Image[PlantDots];
        private Text _stateText;
        private Image[] _hearts = new Image[0];
        private RectTransform _crosshair;
        private Image _ring, _centerDot, _marker;
        private Text _blockedText;
        private CanvasGroup _death;
        private Image _deathImage;
        private Text _deathTitle, _deathBy, _deathCount;
        private Text _toast;

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
            if (!_canvas) Build();
            SetCanvasesActive(true);
            _lastHealth = _health.Current;
            if (!_shooting) return;
            _shooting.HitConfirmed += OnHitConfirmed;
            _shooting.FireBlocked += OnFireBlocked;
        }

        private void OnDisable()
        {
            Scoped = false;
            if (_canvas) SetCanvasesActive(false);
            if (!_shooting) return;
            _shooting.HitConfirmed -= OnHitConfirmed;
            _shooting.FireBlocked -= OnFireBlocked;
        }

        private void OnDestroy()
        {
            if (!_canvas) return;
            Destroy(_canvas.gameObject);
            Destroy(_scopeCanvas.gameObject);
            Destroy(_crosshairCanvas.gameObject);
        }

        private void SetCanvasesActive(bool active)
        {
            _canvas.gameObject.SetActive(active);
            _scopeCanvas.gameObject.SetActive(active);
            _crosshairCanvas.gameObject.SetActive(active);
        }

        private void OnHitConfirmed(bool killed)
        {
            _markerAt = Time.time;
            _rig.AddShake(killed ? 0.5f : 0.15f);
            _markerKill = killed;
            GameAudio.Play(killed ? Sfx.Kill : Sfx.HitMarker, killed ? 0.9f : 0.7f, 0.04f);
            if (!killed) return;
            _toast.text = "KNOCKOUT!";
            _toast.color = Faction.Color(Team);
            _toastAt = Time.time;
        }

        private void OnFireBlocked() => _blockedAt = Time.time;

        private void Update()
        {
            TrackHealth();
            _scope = Mathf.MoveTowards(_scope, _rig.IsZoomed ? 1f : 0f, _scopeFadeSpeed * Time.deltaTime);
            bool dead = _health.IsDead;
            Scoped = _scope > 0.5f && !dead;

            bool show = !StoryIntro.IsPlaying;
            if (_canvas.gameObject.activeSelf != show) SetCanvasesActive(show);
            if (!show) return;

            Color faction = Faction.Color(Team);
            bool status = !Scoped && !dead;
            _tracker.gameObject.SetActive(status);
            _status.gameObject.SetActive(status);
            _crosshair.gameObject.SetActive(!dead);
            if (status)
            {
                UpdateTracker();
                UpdateStatus();
            }
            if (!dead) UpdateCrosshair(faction);
            UpdateOverlays(dead);
        }

        // Runs even while hidden so a hit during the story's last frame still registers.
        private void TrackHealth()
        {
            int hp = _health.Current;
            if (hp < _lastHealth)
            {
                _damagedAt = Time.time;
                _rig.AddShake(0.7f);
                for (int i = Mathf.Max(hp, 0); i < Mathf.Min(_lastHealth, _heartLostAt.Length); i++) _heartLostAt[i] = Time.time;
                if (hp <= 0)
                {
                    _deadAt = Time.time;
                    GameAudio.Play(Sfx.Knockout, 0.9f);
                }
                else GameAudio.Play(Sfx.Hurt, 0.8f, 0.05f);
            }
            else if (hp > 0 && _lastHealth <= 0) GameAudio.Play(Sfx.Respawn, 0.8f);
            _lastHealth = hp;
        }

        // ---------- Webcam panel ----------

        // Box + dot while the webcam tracks; otherwise just a red camera in the corner (keyboard fallback).
        private void UpdateTracker()
        {
            bool live = _tracking && _tracking.State == TrackingState.Tracking;
            _trackerBox.enabled = live;
            _dot.enabled = live;
            _calibrateText.enabled = live;
            _cameraOff.enabled = !live;
            if (live) _dot.rectTransform.anchoredPosition = Vector2.Scale(_tracking.Head, _boxHalf);
        }

        // ---------- Bottom-left: badge, plant dial, status line, hearts ----------

        private void UpdateStatus()
        {
            int team = Team;
            _badge.sprite = team == 0 ? UiBuild.Flower : UiBuild.Thorn;
            _badge.color = Faction.Color(team);

            int lit = Mathf.RoundToInt(_planting.Progress * PlantDots);
            for (int i = 0; i < PlantDots; i++) _plantDots[i].color = i < lit ? PlantGreen : new Color(1f, 1f, 1f, 0.25f);
            bool ready = _planting.CanShoot && (!_shooting || _shooting.CooldownProgress >= 1f);
            _sprout.color = _planting.CanShoot ? PlantGreen : new Color(0.75f, 0.75f, 0.75f);
            _sprout.rectTransform.localScale = Vector3.one * (ready ? 1f + 0.06f * Mathf.Sin(Time.time * 5f) : 1f);

            switch (_planting.State)
            {
                case PlantState.Unplanted: SetState("UPROOTED", Color.white); break;
                case PlantState.Planting: SetState("PLANTING…", Warn); break;
                case PlantState.Planted: SetState("READY TO SPIT", PlantGreen); break;
                case PlantState.Unplanting: SetState("UPROOTING…", Warn); break;
            }

            int hp = _health.Current;
            for (int i = 0; i < _hearts.Length; i++)
            {
                bool full = i < hp;
                float lost = Time.time - _heartLostAt[i];
                float scale = full ? 1f : UiBuild.Pop(lost, 0.35f, 0.6f);
                if (full && hp == 1) scale = 1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 4f)); // last heart beats
                _hearts[i].color = full ? HeartRed : lost < 0.35f ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0.6f);
                _hearts[i].rectTransform.localScale = Vector3.one * scale;
            }

            float shake = Mathf.Clamp01(1f - (Time.time - _damagedAt) / 0.3f);
            _status.anchoredPosition = new Vector2(Margin, Margin) + Random.insideUnitCircle * (6f * shake);
        }

        private void SetState(string state, Color color)
        {
            _stateText.text = state;
            _stateText.color = color;
        }

        // ---------- Crosshair ----------

        private void UpdateCrosshair(Color faction)
        {
            bool canShoot = _planting.CanShoot;
            float marker = Time.time - _markerAt;
            bool showMarker = marker < _hitMarkerTime;
            bool killRed = showMarker && _markerKill;
            _crosshair.localScale = Vector3.one * Mathf.Lerp(_crosshair.localScale.x, _rig.IsZoomed ? 1.3f : 1f,
                1f - Mathf.Exp(-14f * Time.deltaTime));
            _ring.color = killRed ? Bad : canShoot ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            _centerDot.color = killRed ? Bad : canShoot ? faction : new Color(1f, 1f, 1f, 0.5f);

            _marker.enabled = showMarker;
            if (showMarker)
            {
                _marker.color = UiBuild.WithAlpha(_markerKill ? Bad : Color.white, 1f - marker / _hitMarkerTime);
                _marker.rectTransform.localScale = Vector3.one * UiBuild.Pop(marker, _hitMarkerTime, _markerKill ? 0.6f : 0.3f);
            }

            float blocked = Time.time - _blockedAt;
            _blockedText.enabled = blocked < 1.4f;
            if (_blockedText.enabled)
            {
                _blockedText.color = UiBuild.WithAlpha(Warn, Mathf.Clamp01((1.4f - blocked) / 0.4f));
                _blockedText.rectTransform.localScale = Vector3.one * UiBuild.Pop(blocked, 0.2f, 0.25f);
            }
        }

        // ---------- Screen overlays ----------

        private void UpdateOverlays(bool dead)
        {
            // Round scope: a square circle-vignette the height of the screen × size, solid strips beside it.
            Rect screen = _canvas.rect;
            float side = screen.height * _scopeCircleSize;
            float strip = Mathf.Max(0f, (screen.width - side) * 0.5f);
            _scopeCircle.sizeDelta = new Vector2(side, side);
            // Exactly abutting: any overlap doubles the alpha into dark vertical lines.
            _scopeLeft.sizeDelta = _scopeRight.sizeDelta = new Vector2(strip, 0f);
            float scope = dead ? 0f : _scope * _scopeVignette;
            _scopeImage.color = _scopeLeftImage.color = _scopeRightImage.color = new Color(0f, 0f, 0f, scope);
            _scopeImage.enabled = _scopeLeftImage.enabled = _scopeRightImage.enabled = scope > 0f;

            float flash = Mathf.Clamp01(1f - (Time.time - _damagedAt) / _damageFlashTime) * _damageFlashAlpha * 2f;
            float lowHealth = _health.Current == 1 ? 0.18f + 0.1f * Mathf.Sin(Time.time * 4f) : 0f;
            _damageVignette.color = UiBuild.WithAlpha(new Color(0.85f, 0.05f, 0.08f), dead ? 0f : Mathf.Clamp01(Mathf.Max(flash, lowHealth)));

            _death.alpha = Mathf.MoveTowards(_death.alpha, dead ? 1f : 0f, Time.deltaTime * 4f);
            if (dead)
            {
                if (!_match) _match = FindFirstObjectByType<MatchManager>();
                bool ended = _match && _match.IsEnded; // the end banner says the rest
                _deathImage.color = _deathTint;
                _deathTitle.enabled = _deathBy.enabled = _deathCount.enabled = !ended;
                // The victim's killcam takes the middle of the screen: title above it, countdown below.
                bool killcam = KillcamView.FullscreenActive;
                _deathTitle.rectTransform.anchoredPosition = new Vector2(0f, killcam ? 286f : 40f);
                _deathBy.rectTransform.anchoredPosition = new Vector2(0f, killcam ? 246f : -18f);
                _deathCount.rectTransform.anchoredPosition = new Vector2(0f, killcam ? -300f : -58f);
                int rival = 1 - Team;
                _deathBy.text = $"{Faction.Name(rival)} got you";
                _deathBy.color = Faction.Color(rival);
                float left = _health.RespawnDelay - (Time.time - _deadAt);
                _deathCount.text = _health.RespawnDelay > 0f ? $"Respawning in {Mathf.CeilToInt(Mathf.Max(0f, left))}…" : "";
                // Centred normally; below the victim's full-screen killcam while it plays, so nothing overlaps.
                float y = KillcamView.FullscreenActive ? -215f : 40f;
                _deathTitle.rectTransform.anchoredPosition = new Vector2(0f, y);
                _deathBy.rectTransform.anchoredPosition = new Vector2(0f, y - 52f);
                _deathCount.rectTransform.anchoredPosition = new Vector2(0f, y - 88f);
            }

            float toast = Time.time - _toastAt;
            _toast.enabled = toast < 1.5f && !dead;
            if (!_toast.enabled) return;
            float alpha = toast < 0.12f ? toast / 0.12f : Mathf.Clamp01((1.5f - toast) / 0.5f);
            _toast.color = UiBuild.WithAlpha(_toast.color, alpha);
            _toast.rectTransform.localScale = Vector3.one * UiBuild.Pop(toast, 0.3f, 0.5f);
        }

        // ---------- Build ----------

        private void Build()
        {
            _canvas = UiBuild.Canvas("PlayerHUDCanvas", 40);
            _scopeCanvas = UiBuild.Canvas("ScopeCanvas", 51);
            _crosshairCanvas = UiBuild.Canvas("CrosshairCanvas", 52);
            BuildScope();
            _damageVignette = FullScreen(_canvas, UiBuild.Vignette, Color.clear);
            BuildTracker();
            BuildStatus();
            BuildCrosshair();

            _deathImage = FullScreen(_canvas, null, _deathTint);
            _death = _deathImage.gameObject.AddComponent<CanvasGroup>();
            _death.alpha = 0f;
            _death.blocksRaycasts = false;
            _deathTitle = UiBuild.Label(_deathImage.transform, "SPAT OUT!", 58, Color.white, TextAnchor.MiddleCenter,
                Centre, new Vector2(0f, 40f), new Vector2(900f, 70f));
            UiBuild.Outline(_deathTitle, new Color(0f, 0f, 0f, 0.6f), 2f);
            _deathBy = UiBuild.Label(_deathImage.transform, "", 28, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -18f), new Vector2(900f, 36f));
            UiBuild.Outline(_deathBy, new Color(0f, 0f, 0f, 0.5f));
            _deathCount = UiBuild.Label(_deathImage.transform, "", 24, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -58f), new Vector2(900f, 34f));

            _toast = UiBuild.Label(_canvas, "", 44, Color.white, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 140f),
                new Vector2(900f, 60f));
            UiBuild.Outline(_toast, new Color(0f, 0f, 0f, 0.65f), 2f);
            _toast.enabled = false;
            UiBuild.Display(_toast, _deathTitle, _deathBy, _deathCount);
        }

        private void BuildScope()
        {
            _scopeImage = UiBuild.Image(_scopeCanvas, UiBuild.Scope, Vector2.zero, Vector2.one, Color.clear);
            _scopeCircle = _scopeImage.rectTransform;
            _scopeLeftImage = Strip(MidLeft, out _scopeLeft);
            _scopeRightImage = Strip(MidRight, out _scopeRight);
        }

        // Full-height strip on the left or right screen edge.
        private Image Strip(Vector2 anchor, out RectTransform rect)
        {
            Image image = UiBuild.Image(_scopeCanvas, null, anchor, Vector2.zero, Vector2.zero, Color.clear);
            rect = image.rectTransform;
            rect.anchorMin = new Vector2(anchor.x, 0f);
            rect.anchorMax = new Vector2(anchor.x, 1f);
            return image;
        }

        // Transparent box tucked top-left: centre lines + a dot where the webcam puts your head. Red camera instead
        // when the webcam isn't tracking.
        private void BuildTracker()
        {
            float boxHeight = _trackerWidth * 9f / 16f;
            _boxHalf = new Vector2(_trackerWidth * 0.5f - 5f, boxHeight * 0.5f - 5f);
            const float labelHeight = 22f;
            _tracker = UiBuild.Node(_canvas, "Tracker", TopLeft, new Vector2(TrackerMargin, -TrackerMargin),
                new Vector2(_trackerWidth, boxHeight + labelHeight));
            _calibrateText = UiBuild.Label(_tracker, "Press C to Calibrate", 17, Color.white, TextAnchor.MiddleLeft, TopLeft,
                Vector2.zero, new Vector2(_trackerWidth, labelHeight));
            _trackerBox = UiBuild.Image(_tracker, null, TopLeft, new Vector2(0f, -labelHeight),
                new Vector2(_trackerWidth, boxHeight), new Color(0f, 0f, 0f, 0.35f));
            UiBuild.Image(_trackerBox.transform, null, Vector2.zero, new Vector2(1f, boxHeight), new Color(1f, 1f, 1f, 0.25f));
            UiBuild.Image(_trackerBox.transform, null, Vector2.zero, new Vector2(_trackerWidth, 1f), new Color(1f, 1f, 1f, 0.25f));
            _dot = UiBuild.Image(_trackerBox.transform, UiBuild.Circle, Vector2.zero, new Vector2(10f, 10f), Color.white);
            _cameraOff = UiBuild.Image(_tracker, UiSprites.Camera(), TopLeft, Vector2.zero, new Vector2(IconSize, IconSize), Bad);
        }

        // No panel: icons and hearts sit straight on the scene, with shadows for contrast.
        private void BuildStatus()
        {
            int count = Mathf.Max(1, _health.Max);
            float iconsY = _heartSize + 10f;
            _status = UiBuild.Node(_canvas, "Status", BottomLeft, new Vector2(Margin, Margin),
                new Vector2(IconSize * 2f + 260f, iconsY + IconSize));

            _badge = UiBuild.Image(_status, UiBuild.Flower, BottomLeft, new Vector2(0f, iconsY), new Vector2(IconSize, IconSize),
                Color.white);
            AddShadow(_badge);

            Image disc = UiBuild.Image(_status, UiBuild.Circle, BottomLeft, new Vector2(IconSize + 12f, iconsY),
                new Vector2(IconSize, IconSize), new Color(0f, 0f, 0f, 0.4f));
            float radius = IconSize * 0.5f, dot = IconSize * 0.14f;
            for (int i = 0; i < PlantDots; i++)
            {
                float a = Mathf.PI * 0.5f - i * 2f * Mathf.PI / PlantDots;
                _plantDots[i] = UiBuild.Image(disc.transform, UiBuild.Circle,
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius - dot * 0.5f), new Vector2(dot, dot), Color.white);
            }
            _sprout = UiBuild.Image(disc.transform, UiBuild.Sprout, Vector2.zero, new Vector2(IconSize * 0.56f, IconSize * 0.56f),
                Color.white);

            float textX = IconSize * 2f + 24f;
            _stateText = UiBuild.Label(_status, "", 17, Color.white, TextAnchor.MiddleLeft, BottomLeft,
                new Vector2(textX, iconsY + IconSize * 0.5f), new Vector2(240f, 22f));
            _stateText.rectTransform.pivot = new Vector2(0f, 0.5f); // vertically centred on the icons

            _hearts = new Image[count];
            _heartLostAt = new float[count];
            for (int i = 0; i < count; i++)
            {
                _heartLostAt[i] = float.NegativeInfinity;
                _hearts[i] = UiBuild.Image(_status, UiBuild.Heart, BottomLeft, new Vector2(i * (_heartSize + 6f), 0f),
                    new Vector2(_heartSize, _heartSize), HeartRed);
                AddShadow(_hearts[i]);
            }
        }

        private void BuildCrosshair()
        {
            Vector2 size = new(_crosshairSize, _crosshairSize);
            _crosshair = UiBuild.Node(_crosshairCanvas, "Crosshair", Vector2.zero, size);
            _ring = UiBuild.Image(_crosshair, UiBuild.Ring, Vector2.zero, size, Color.white);
            UiBuild.Outline(_ring, new Color(0f, 0f, 0f, 0.35f), 1f);
            _centerDot = UiBuild.Image(_crosshair, UiBuild.Circle, Vector2.zero, new Vector2(4f, 4f), Color.white);
            _marker = UiBuild.Image(_crosshair, UiBuild.Cross, Vector2.zero, size * 1.8f, Color.white);
            _marker.enabled = false;
            _blockedText = UiBuild.Label(_crosshairCanvas, "Plant first!  Press Space", 17, Warn, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -46f), new Vector2(400f, 26f));
            _blockedText.enabled = false;
            UiBuild.Display(_blockedText, _stateText);
        }

        private static void AddShadow(Graphic graphic)
        {
            Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }

        private static Image FullScreen(Transform parent, Sprite sprite, Color color)
        {
            Image image = UiBuild.Image(parent, sprite, Vector2.zero, Vector2.zero, color);
            UiBuild.Stretch(image.rectTransform);
            return image;
        }
    }
}
