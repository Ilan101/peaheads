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
        private static readonly Color HealGreen = new(0.55f, 1f, 0.55f);
        private static readonly Color DotOff = new(1f, 1f, 1f, 0.25f);

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
        private float _firedAt = float.NegativeInfinity;
        private float _stateAt = float.NegativeInfinity;
        private float _dial;
        private string _lastState;
        private float[] _heartLostAt = new float[0];
        private float[] _heartGainedAt = new float[0];

        // Draw order: HUD 40 < killcam 42 < minimap 50 < scope vignette 51 < crosshair 52, so the scope darkens the
        // minimap and killcam but never the crosshair.
        private RectTransform _canvas, _scopeCanvas, _crosshairCanvas;
        private RectTransform _scopeCircle, _scopeLeft, _scopeRight;
        private Image _scopeImage, _scopeLeftImage, _scopeRightImage;
        private Image _damageVignette;
        private TrackerPanel _tracker;
        private RectTransform _status;
        private Image _badge, _sprout;
        private readonly Image[] _plantDots = new Image[PlantDots];
        private Text _stateText;
        private Image[] _hearts = new Image[0];
        private RectTransform _crosshair;
        private float _crosshairBase = 1f;
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
            _shooting.Fired += OnFired;
        }

        private void OnDisable()
        {
            Scoped = false;
            if (_canvas) SetCanvasesActive(false);
            if (!_shooting) return;
            _shooting.HitConfirmed -= OnHitConfirmed;
            _shooting.FireBlocked -= OnFireBlocked;
            _shooting.Fired -= OnFired;
        }

        // Canvases may already be gone when the scene unloads (quit, leave), so check each one.
        private void OnDestroy()
        {
            if (_canvas) Destroy(_canvas.gameObject);
            if (_scopeCanvas) Destroy(_scopeCanvas.gameObject);
            if (_crosshairCanvas) Destroy(_crosshairCanvas.gameObject);
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

        private void OnFired() => _firedAt = Time.time;

        private void Update()
        {
            TrackHealth();
            _scope = Mathf.MoveTowards(_scope, _rig.IsZoomed ? 1f : 0f, _scopeFadeSpeed * Time.deltaTime);
            bool dead = _health.IsDead;
            Scoped = _scope > 0.5f && !dead;

            bool show = !LobbyView.HidesGame;
            if (_canvas.gameObject.activeSelf != show) SetCanvasesActive(show);
            if (!show) return;

            Color faction = Faction.Color(Team);
            bool status = !Scoped && !dead;
            // Nothing to aim at once the round is decided; the crosshair would sit on the end banner.
            bool aiming = !dead && !(MatchManager.Instance && MatchManager.Instance.IsEnded);
            _tracker.Root.gameObject.SetActive(status);
            _status.gameObject.SetActive(status);
            _crosshair.gameObject.SetActive(aiming);
            if (status)
            {
                _tracker.Refresh(_tracking);
                UpdateStatus();
            }
            if (aiming) UpdateCrosshair(faction);
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
            else if (hp > _lastHealth)
            {
                // A Sporelight grew a heart back.
                for (int i = Mathf.Max(_lastHealth, 0); i < Mathf.Min(hp, _heartGainedAt.Length); i++) _heartGainedAt[i] = Time.time;
                GameAudio.Play(Sfx.Heal, 0.7f);
            }
            _lastHealth = hp;
        }

        // ---------- Bottom-left: badge, plant dial, status line, hearts ----------

        private void UpdateStatus()
        {
            int team = Team;
            _badge.sprite = team == 0 ? UiBuild.Flower : UiBuild.Thorn;
            _badge.color = Faction.Color(team);

            // Smooth fill: the dot at the front fades in as progress passes it.
            _dial = Mathf.MoveTowards(_dial, _planting.Progress, Time.deltaTime * 3f);
            float filled = _dial * PlantDots;
            for (int i = 0; i < PlantDots; i++) _plantDots[i].color = Color.Lerp(DotOff, PlantGreen, Mathf.Clamp01(filled - i));
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
                float gained = Time.time - _heartGainedAt[i];
                float scale = full ? UiBuild.Pop(gained, 0.5f, 0.7f) : UiBuild.Pop(lost, 0.35f, 0.6f);
                if (full && hp == 1 && gained > 0.5f) scale = 1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 4f)); // last heart beats
                Color heart = gained < 0.6f ? Color.Lerp(HealGreen, HeartRed, gained / 0.6f) : HeartRed;
                _hearts[i].color = full ? heart : lost < 0.35f ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0.6f);
                _hearts[i].rectTransform.localScale = Vector3.one * scale;
            }

            float shake = Mathf.Clamp01(1f - (Time.time - _damagedAt) / 0.3f);
            _status.anchoredPosition = new Vector2(Margin, Margin) + Random.insideUnitCircle * (6f * shake);
        }

        private void SetState(string state, Color color)
        {
            if (state != _lastState)
            {
                _lastState = state;
                _stateAt = Time.time;
            }
            _stateText.text = state;
            _stateText.color = Color.Lerp(_stateText.color, color, 1f - Mathf.Exp(-12f * Time.deltaTime));
            _stateText.rectTransform.localScale = Vector3.one * UiBuild.Pop(Time.time - _stateAt, 0.25f, 0.18f);
        }

        // ---------- Crosshair ----------

        private void UpdateCrosshair(Color faction)
        {
            bool canShoot = _planting.CanShoot;
            float marker = Time.time - _markerAt;
            bool showMarker = marker < _hitMarkerTime;
            bool killRed = showMarker && _markerKill;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            // Base size eases to the zoom size; a spit kicks the ring open for a moment.
            _crosshairBase = Mathf.Lerp(_crosshairBase, _rig.IsZoomed ? 1.3f : 1f, k);
            float kick = Mathf.Clamp01(1f - (Time.time - _firedAt) / 0.18f);
            _crosshair.localScale = Vector3.one * (_crosshairBase * (1f + 0.45f * kick * kick));
            Color ring = killRed ? Bad : canShoot ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            Color dot = killRed ? Bad : canShoot ? faction : new Color(1f, 1f, 1f, 0.5f);
            _ring.color = killRed ? ring : Color.Lerp(_ring.color, ring, k);
            _centerDot.color = killRed ? dot : Color.Lerp(_centerDot.color, dot, k);

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

        private void BuildTracker() =>
            _tracker = new TrackerPanel(_canvas, new Vector2(TrackerMargin, -TrackerMargin), _trackerWidth);

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
            _heartGainedAt = new float[count];
            for (int i = 0; i < count; i++)
            {
                _heartLostAt[i] = float.NegativeInfinity;
                _heartGainedAt[i] = float.NegativeInfinity;
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
