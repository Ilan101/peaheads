using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Local player's HUD (uGUI, built in code): webcam panel top-left; bottom-left team badge (flower / thorn), plant
    // dial (sprout + ring of dots that fills while planting) with a status line, and hearts; crosshair with cooldown
    // ring and hit marker (all red on a kill); round scope view while zoomed; damage / low-health vignette; "spat
    // out" death screen; kill toast and feedback sounds. Owner only (PlayerInput's local-only behaviours); hidden
    // during the story. While scoped in, everything but the crosshair hides (ScoreHUD and Minimap read Scoped).
    public class PlayerHUD : MonoBehaviour
    {
        private const float Margin = 20f;
        private const float IconSize = 53f;
        private const int PlantDots = 12;
        private const int TrailLength = 6;
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 TopRight = new(1f, 1f);
        private static readonly Vector2 BottomLeft = new(0f, 0f);
        private static readonly Vector2 BottomRight = new(1f, 0f);
        private static readonly Vector2 Centre = new(0.5f, 0.5f);
        private static readonly Vector2 MidLeft = new(0f, 0.5f);
        private static readonly Vector2 MidRight = new(1f, 0.5f);
        private static readonly Color Live = new(0.45f, 0.95f, 0.5f);
        private static readonly Color PlantGreen = new(0.5f, 0.9f, 0.35f);
        private static readonly Color Warn = new(1f, 0.78f, 0.3f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);
        private static readonly Color Dim = new(1f, 1f, 1f, 0.6f);
        private static readonly Color Ink = new(0.06f, 0.08f, 0.06f);
        private static readonly Color HeartRed = new(0.92f, 0.17f, 0.24f);
        private static readonly Color ChipOff = new(1f, 1f, 1f, 0.1f);

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
        private float _mouthAt = float.NegativeInfinity, _winkAt = float.NegativeInfinity;
        private bool _mouthWas, _winkWas;
        private float _nextTrail;
        private float[] _heartLostAt = new float[0];
        private readonly Vector2[] _trail = new Vector2[TrailLength];

        private RectTransform _canvas;
        private RectTransform _scopeCircle, _scopeLeft, _scopeRight;
        private Image _scopeImage, _scopeLeftImage, _scopeRightImage;
        private Image _damageVignette;
        private Outline _trackerOutline;
        private Image _trackerPanel, _statusPill, _dot, _dotGlow, _mouthChip, _winkChip;
        private Text _statusText, _boxMessage, _mouthText, _winkText;
        private readonly Image[] _trailDots = new Image[TrailLength];
        private Vector2 _boxHalf;
        private RectTransform _status;
        private Image _badge, _sprout;
        private readonly Image[] _plantDots = new Image[PlantDots];
        private Text _stateText, _hintText;
        private Image[] _hearts = new Image[0];
        private RectTransform _crosshair;
        private Image _ring, _cooldown, _centerDot, _marker;
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
            _canvas.gameObject.SetActive(true);
            _lastHealth = _health.Current;
            if (!_shooting) return;
            _shooting.HitConfirmed += OnHitConfirmed;
            _shooting.FireBlocked += OnFireBlocked;
        }

        private void OnDisable()
        {
            Scoped = false;
            if (_canvas) _canvas.gameObject.SetActive(false);
            if (!_shooting) return;
            _shooting.HitConfirmed -= OnHitConfirmed;
            _shooting.FireBlocked -= OnFireBlocked;
        }

        private void OnDestroy()
        {
            if (_canvas) Destroy(_canvas.gameObject);
        }

        private void OnHitConfirmed(bool killed)
        {
            _markerAt = Time.time;
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
            if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
            if (!show) return;

            Color faction = Faction.Color(Team);
            bool status = !Scoped && !dead;
            _trackerPanel.gameObject.SetActive(status);
            _status.gameObject.SetActive(status);
            _crosshair.gameObject.SetActive(!dead);
            if (status)
            {
                UpdateTracker(faction);
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

        private void UpdateTracker(Color faction)
        {
            TrackingState state = _tracking ? _tracking.State : TrackingState.Off;
            bool live = state == TrackingState.Tracking;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
            (string label, Color color) = state switch
            {
                TrackingState.Tracking => ("LIVE", Live),
                TrackingState.NoFace => ("NO FACE", Warn),
                _ => ("OFF", Bad),
            };
            _statusText.text = label;
            _statusPill.color = live ? color : UiBuild.WithAlpha(color, pulse);
            _trackerOutline.effectColor = UiBuild.WithAlpha(live ? faction : color, 0.55f);

            Vector2 head = live ? _tracking.Head : Vector2.zero;
            Vector2 pos = Vector2.Scale(head, _boxHalf);
            if (Time.time >= _nextTrail)
            {
                _nextTrail = Time.time + 1f / 30f;
                for (int i = TrailLength - 1; i > 0; i--) _trail[i] = _trail[i - 1];
                _trail[0] = pos;
            }
            _dot.enabled = _dotGlow.enabled = live;
            _dot.rectTransform.anchoredPosition = _dotGlow.rectTransform.anchoredPosition = pos;
            _dot.color = faction;
            _dotGlow.color = UiBuild.WithAlpha(faction, 0.55f);
            for (int i = 0; i < TrailLength; i++)
            {
                _trailDots[i].enabled = live;
                _trailDots[i].rectTransform.anchoredPosition = _trail[i];
                _trailDots[i].color = UiBuild.WithAlpha(faction, 0.35f * (1f - (i + 1f) / (TrailLength + 1f)));
            }
            _boxMessage.text = state switch
            {
                TrackingState.NoFace => "Look at the camera",
                TrackingState.Off => "Start head_tracker.py\nto lean and dodge",
                _ => "",
            };
            _boxMessage.color = UiBuild.WithAlpha(color, pulse);

            HeadTrackingReceiver tracker = HeadTrackingReceiver.Instance;
            bool mouth = live && tracker && tracker.Shoot;
            bool wink = live && tracker && tracker.Aim;
            if (mouth && !_mouthWas) _mouthAt = Time.time;
            if (wink && !_winkWas) _winkAt = Time.time;
            _mouthWas = mouth;
            _winkWas = wink;
            SetChip(_mouthChip, _mouthText, mouth, _mouthAt);
            SetChip(_winkChip, _winkText, wink, _winkAt);
        }

        private static void SetChip(Image chip, Text text, bool on, float since)
        {
            chip.color = on ? Live : ChipOff;
            text.color = on ? Ink : Dim;
            chip.rectTransform.localScale = Vector3.one * (on ? UiBuild.Pop(Time.time - since, 0.2f, 0.25f) : 1f);
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
                case PlantState.Unplanted: SetState("UPROOTED", Color.white, "WASD move  ·  E plant"); break;
                case PlantState.Planting: SetState("PLANTING…", Warn, "Digging in your roots"); break;
                case PlantState.Planted:
                    SetState(ready ? "READY TO SPIT" : "RELOADING", ready ? PlantGreen : Dim, "Open mouth / click  ·  E uproot");
                    break;
                case PlantState.Unplanting: SetState("UPROOTING…", Warn, "Pulling your roots out"); break;
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

        private void SetState(string state, Color color, string hint)
        {
            _stateText.text = state;
            _stateText.color = color;
            _hintText.text = hint;
        }

        // ---------- Crosshair ----------

        private void UpdateCrosshair(Color faction)
        {
            bool canShoot = _planting.CanShoot;
            float cooldown = _shooting ? _shooting.CooldownProgress : 1f;
            float marker = Time.time - _markerAt;
            bool showMarker = marker < _hitMarkerTime;
            bool killRed = showMarker && _markerKill;
            _crosshair.localScale = Vector3.one * Mathf.Lerp(_crosshair.localScale.x, _rig.IsZoomed ? 1.3f : 1f,
                1f - Mathf.Exp(-14f * Time.deltaTime));
            _ring.color = killRed ? Bad : canShoot ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            _centerDot.color = killRed ? Bad : canShoot && cooldown >= 1f ? faction : new Color(1f, 1f, 1f, 0.5f);
            _cooldown.enabled = canShoot && cooldown < 1f;
            _cooldown.fillAmount = cooldown;
            _cooldown.color = faction;

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
            _scopeLeft.sizeDelta = _scopeRight.sizeDelta = new Vector2(strip + 2f, 0f);
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
        }

        private void BuildScope()
        {
            _scopeImage = UiBuild.Image(_canvas, UiBuild.Scope, Vector2.zero, Vector2.one, Color.clear);
            _scopeCircle = _scopeImage.rectTransform;
            _scopeLeftImage = Strip(MidLeft, out _scopeLeft);
            _scopeRightImage = Strip(MidRight, out _scopeRight);
        }

        // Full-height strip on the left or right screen edge.
        private Image Strip(Vector2 anchor, out RectTransform rect)
        {
            Image image = UiBuild.Image(_canvas, null, anchor, Vector2.zero, Vector2.zero, Color.clear);
            rect = image.rectTransform;
            rect.anchorMin = new Vector2(anchor.x, 0f);
            rect.anchorMax = new Vector2(anchor.x, 1f);
            return image;
        }

        private void BuildTracker()
        {
            float boxHeight = _trackerWidth * 9f / 16f;
            _boxHalf = new Vector2(_trackerWidth * 0.5f - 9f, boxHeight * 0.5f - 9f);
            _trackerPanel = UiBuild.Panel(_canvas, TopLeft, new Vector2(16f, -16f),
                new Vector2(_trackerWidth + 20f, boxHeight + 72f), UiBuild.Glass);
            _trackerOutline = UiBuild.Outline(_trackerPanel, Color.clear);
            Transform panel = _trackerPanel.transform;
            UiBuild.Label(panel, "HEAD TRACKING", 12, Dim, TextAnchor.MiddleLeft, TopLeft, new Vector2(10f, -6f),
                new Vector2(160f, 20f));
            _statusPill = UiBuild.Panel(panel, TopRight, new Vector2(-10f, -6f), new Vector2(84f, 20f), Live, 0.6f);
            _statusText = UiBuild.Label(_statusPill.transform, "", 11, Ink, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                new Vector2(84f, 20f), FontStyle.Bold, false);

            Image box = UiBuild.Panel(panel, TopLeft, new Vector2(10f, -30f), new Vector2(_trackerWidth, boxHeight),
                new Color(0f, 0f, 0f, 0.4f), 0.6f);
            UiBuild.Image(box.transform, null, Vector2.zero, new Vector2(1f, boxHeight - 10f), new Color(1f, 1f, 1f, 0.12f));
            UiBuild.Image(box.transform, null, Vector2.zero, new Vector2(_trackerWidth - 10f, 1f), new Color(1f, 1f, 1f, 0.12f));
            for (int i = TrailLength - 1; i >= 0; i--)
                _trailDots[i] = UiBuild.Image(box.transform, UiBuild.Circle, Vector2.zero, new Vector2(9f, 9f), Color.clear);
            _dotGlow = UiBuild.Image(box.transform, UiBuild.Glow, Vector2.zero, new Vector2(40f, 40f), Color.clear);
            _dot = UiBuild.Image(box.transform, UiBuild.Circle, Vector2.zero, new Vector2(15f, 15f), Color.white);
            UiBuild.Outline(_dot, new Color(1f, 1f, 1f, 0.9f), 1.2f);
            _boxMessage = UiBuild.Label(box.transform, "", 15, Warn, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                new Vector2(_trackerWidth, 44f));

            _mouthChip = UiBuild.Panel(panel, BottomLeft, new Vector2(10f, 10f), new Vector2(98f, 24f), ChipOff, 0.7f);
            _mouthText = UiBuild.Label(_mouthChip.transform, "MOUTH · SPIT", 11, Dim, TextAnchor.MiddleCenter, Centre,
                Vector2.zero, new Vector2(98f, 24f), FontStyle.Bold, false);
            _winkChip = UiBuild.Panel(panel, BottomLeft, new Vector2(114f, 10f), new Vector2(98f, 24f), ChipOff, 0.7f);
            _winkText = UiBuild.Label(_winkChip.transform, "WINK · ZOOM", 11, Dim, TextAnchor.MiddleCenter, Centre,
                Vector2.zero, new Vector2(98f, 24f), FontStyle.Bold, false);
            UiBuild.Label(panel, "C recenter", 11, Dim, TextAnchor.MiddleRight, BottomRight, new Vector2(-10f, 10f),
                new Vector2(80f, 24f), FontStyle.Normal);
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
            _stateText.rectTransform.pivot = new Vector2(0f, 0f);
            _hintText = UiBuild.Label(_status, "", 12, Dim, TextAnchor.MiddleLeft, BottomLeft,
                new Vector2(textX, iconsY + IconSize * 0.5f - 18f), new Vector2(240f, 16f), FontStyle.Normal);

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
            _crosshair = UiBuild.Node(_canvas, "Crosshair", Vector2.zero, size);
            _ring = UiBuild.Image(_crosshair, UiBuild.Ring, Vector2.zero, size, Color.white);
            UiBuild.Outline(_ring, new Color(0f, 0f, 0f, 0.35f), 1f);
            _cooldown = UiBuild.Image(_crosshair, UiBuild.Ring, Vector2.zero, size * 1.35f, Color.white);
            _cooldown.type = Image.Type.Filled;
            _cooldown.fillMethod = Image.FillMethod.Radial360;
            _cooldown.fillOrigin = (int)Image.Origin360.Top;
            _cooldown.fillClockwise = true;
            _centerDot = UiBuild.Image(_crosshair, UiBuild.Circle, Vector2.zero, new Vector2(4f, 4f), Color.white);
            _marker = UiBuild.Image(_crosshair, UiBuild.Cross, Vector2.zero, size * 1.8f, Color.white);
            _marker.enabled = false;
            _blockedText = UiBuild.Label(_canvas, "Plant first!  Press E", 17, Warn, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -46f), new Vector2(400f, 26f));
            _blockedText.enabled = false;
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
