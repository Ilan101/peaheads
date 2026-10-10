using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Local player's HUD (uGUI, built in code): webcam panel top-left, player card bottom-left (faction, hearts,
    // plant state), crosshair with cooldown ring and hit marker, damage / low-health / zoom vignettes, knocked-out
    // overlay and a kill toast, plus the matching feedback sounds. Owner only (PlayerInput's local-only
    // behaviours); hidden while the story plays.
    public class PlayerHUD : MonoBehaviour
    {
        private const float Margin = 16f;
        private const int TrailLength = 6;
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 TopRight = new(1f, 1f);
        private static readonly Vector2 BottomLeft = new(0f, 0f);
        private static readonly Vector2 BottomRight = new(1f, 0f);
        private static readonly Vector2 Centre = new(0.5f, 0.5f);
        private static readonly Vector2 MidLeft = new(0f, 0.5f);
        private static readonly Color Live = new(0.45f, 0.95f, 0.5f);
        private static readonly Color Warn = new(1f, 0.78f, 0.3f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);
        private static readonly Color Dim = new(1f, 1f, 1f, 0.6f);
        private static readonly Color Ink = new(0.06f, 0.08f, 0.06f);
        private static readonly Color HeartRed = new(0.96f, 0.24f, 0.32f);
        private static readonly Color ChipOff = new(1f, 1f, 1f, 0.1f);

        [SerializeField, Range(16f, 96f)] private float _heartSize = 40f;
        [Tooltip("Webcam view width; height follows the tracker's 16:9 rectangle.")]
        [SerializeField, Range(128f, 512f)] private float _trackerWidth = 256f;
        [SerializeField, Range(8f, 64f)] private float _crosshairSize = 22f;
        [SerializeField, Range(0.05f, 1f)] private float _hitMarkerTime = 0.25f;
        [Tooltip("Red screen-edge flash when this player takes damage.")]
        [SerializeField, Range(0.05f, 2f)] private float _damageFlashTime = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _damageFlashAlpha = 0.35f;

        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private HeadTrackingInput _tracking;
        private NetworkObject _networkObject;
        private PlayerShooting _shooting;

        private int _lastHealth;
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
        private Image _zoomVignette, _damageVignette;
        private Outline _trackerOutline, _cardOutline;
        private Image _trackerPanel, _statusPill, _dot, _dotGlow, _mouthChip, _winkChip;
        private Text _statusText, _boxMessage, _mouthText, _winkText;
        private readonly Image[] _trailDots = new Image[TrailLength];
        private Vector2 _boxHalf;
        private RectTransform _card;
        private Image _cardPanel, _factionPill, _barTrack, _barFill;
        private Text _factionText, _stateText, _hintText;
        private Image[] _hearts = new Image[0];
        private Vector2 _cardHome;
        private float _barWidth;
        private RectTransform _crosshair;
        private Image _ring, _cooldown, _centerDot, _marker;
        private Text _blockedText;
        private CanvasGroup _knockedOut;
        private Text _knockedOutTitle, _knockedOutCount;
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
            bool show = !StoryIntro.IsPlaying;
            if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
            if (!show) return;

            Color faction = Faction.Color(Team);
            UpdateTracker(faction);
            UpdateCard(faction);
            UpdateCrosshair(faction);
            UpdateOverlays();
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

        // ---------- Player card ----------

        private void UpdateCard(Color faction)
        {
            _factionPill.color = faction;
            _factionText.text = Faction.Name(Team);
            _cardOutline.effectColor = UiBuild.WithAlpha(faction, 0.5f);

            int hp = _health.Current;
            for (int i = 0; i < _hearts.Length; i++)
            {
                bool full = i < hp;
                float lost = Time.time - _heartLostAt[i];
                float scale = full ? 1f : UiBuild.Pop(lost, 0.35f, 0.6f);
                if (full && hp == 1) scale = 1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 4f)); // last heart beats
                _hearts[i].color = full ? HeartRed : new Color(1f, 1f, 1f, lost < 0.35f ? 0.9f : 0.15f);
                _hearts[i].rectTransform.localScale = Vector3.one * scale;
            }

            float shake = Mathf.Clamp01(1f - (Time.time - _damagedAt) / 0.3f);
            _card.anchoredPosition = _cardHome + Random.insideUnitCircle * (6f * shake);

            float progress = _planting.Progress;
            bool transition = _planting.State is PlantState.Planting or PlantState.Unplanting;
            _barTrack.enabled = _barFill.enabled = transition && !_health.IsDead;
            _hintText.enabled = !_barTrack.enabled;
            if (_health.IsDead)
            {
                SetState("KNOCKED OUT", Bad, "Hang on, you'll sprout again");
                return;
            }
            switch (_planting.State)
            {
                case PlantState.Unplanted:
                    SetState("UPROOTED", Color.white, "WASD move   ·   E plant your roots");
                    break;
                case PlantState.Planting:
                    SetState("PLANTING…", Warn, "");
                    SetBar(progress, Warn);
                    break;
                case PlantState.Planted:
                    bool ready = !_shooting || _shooting.CooldownProgress >= 1f;
                    SetState(ready ? "ROOTED  ·  READY TO SPIT" : "ROOTED  ·  RELOADING",
                        UiBuild.WithAlpha(Live, ready ? 0.75f + 0.25f * Mathf.Sin(Time.time * 5f) : 0.6f),
                        "Open mouth / click: spit   ·   E uproot");
                    break;
                case PlantState.Unplanting:
                    SetState("UPROOTING…", Warn, "");
                    SetBar(1f - progress, Warn);
                    break;
            }
        }

        private void SetState(string state, Color color, string hint)
        {
            _stateText.text = state;
            _stateText.color = color;
            _hintText.text = hint;
        }

        private void SetBar(float fill, Color color)
        {
            _barFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, _barWidth * Mathf.Clamp01(fill)), 8f);
            _barFill.color = color;
        }

        // ---------- Crosshair ----------

        private void UpdateCrosshair(Color faction)
        {
            bool canShoot = _planting.CanShoot && !_health.IsDead;
            float cooldown = _shooting ? _shooting.CooldownProgress : 1f;
            _crosshair.localScale = Vector3.one * Mathf.Lerp(_crosshair.localScale.x, _rig.IsZoomed ? 0.8f : 1f,
                1f - Mathf.Exp(-14f * Time.deltaTime));
            _ring.color = canShoot ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            _centerDot.color = canShoot && cooldown >= 1f ? faction : new Color(1f, 1f, 1f, 0.5f);
            _cooldown.enabled = canShoot && cooldown < 1f;
            _cooldown.fillAmount = cooldown;
            _cooldown.color = faction;

            float marker = Time.time - _markerAt;
            _marker.enabled = marker < _hitMarkerTime;
            if (_marker.enabled)
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

        private void UpdateOverlays()
        {
            float flash = Mathf.Clamp01(1f - (Time.time - _damagedAt) / _damageFlashTime) * _damageFlashAlpha * 2f;
            float lowHealth = _health.Current == 1 ? 0.18f + 0.1f * Mathf.Sin(Time.time * 4f) : 0f;
            _damageVignette.color = UiBuild.WithAlpha(new Color(0.85f, 0.05f, 0.08f), Mathf.Clamp01(Mathf.Max(flash, lowHealth)));
            float zoom = Mathf.MoveTowards(_zoomVignette.color.a, _rig.IsZoomed ? 0.6f : 0f, Time.deltaTime * 4f);
            _zoomVignette.color = new Color(0f, 0f, 0f, zoom);

            bool dead = _health.IsDead;
            _knockedOut.alpha = Mathf.MoveTowards(_knockedOut.alpha, dead ? 1f : 0f, Time.deltaTime * 4f);
            if (dead)
            {
                _knockedOutTitle.color = Faction.Color(1 - Team);
                float left = _health.RespawnDelay - (Time.time - _deadAt);
                _knockedOutCount.text = _health.RespawnDelay > 0f && left > 0f ? $"Sprouting again in {Mathf.CeilToInt(left)}" : "";
            }

            float toast = Time.time - _toastAt;
            _toast.enabled = toast < 1.5f;
            if (!_toast.enabled) return;
            float alpha = toast < 0.12f ? toast / 0.12f : Mathf.Clamp01((1.5f - toast) / 0.5f);
            _toast.color = UiBuild.WithAlpha(_toast.color, alpha);
            _toast.rectTransform.localScale = Vector3.one * UiBuild.Pop(toast, 0.3f, 0.5f);
        }

        // ---------- Build ----------

        private void Build()
        {
            _canvas = UiBuild.Canvas("PlayerHUDCanvas", 40);
            _zoomVignette = FullScreen(_canvas, UiBuild.Vignette, Color.clear);
            _damageVignette = FullScreen(_canvas, UiBuild.Vignette, Color.clear);
            BuildTracker();
            BuildCard();
            BuildCrosshair();

            Image dim = FullScreen(_canvas, null, new Color(0.02f, 0.02f, 0.03f, 0.55f));
            _knockedOut = dim.gameObject.AddComponent<CanvasGroup>();
            _knockedOut.alpha = 0f;
            _knockedOut.blocksRaycasts = false;
            _knockedOutTitle = UiBuild.Label(dim.transform, "KNOCKED OUT", 56, Color.white, TextAnchor.MiddleCenter,
                Centre, new Vector2(0f, 30f), new Vector2(900f, 70f));
            UiBuild.Outline(_knockedOutTitle, new Color(0f, 0f, 0f, 0.6f), 2f);
            _knockedOutCount = UiBuild.Label(dim.transform, "", 24, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -24f), new Vector2(900f, 34f));

            _toast = UiBuild.Label(_canvas, "", 44, Color.white, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 140f),
                new Vector2(900f, 60f));
            UiBuild.Outline(_toast, new Color(0f, 0f, 0f, 0.65f), 2f);
            _toast.enabled = false;
        }

        private void BuildTracker()
        {
            float boxHeight = _trackerWidth * 9f / 16f;
            _boxHalf = new Vector2(_trackerWidth * 0.5f - 9f, boxHeight * 0.5f - 9f);
            _trackerPanel = UiBuild.Panel(_canvas, TopLeft, new Vector2(Margin, -Margin),
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

        private void BuildCard()
        {
            int count = Mathf.Max(1, _health.Max);
            float width = Mathf.Max(310f, 24f + count * (_heartSize + 6f));
            float heartsTop = 44f;
            float stateTop = heartsTop + _heartSize + 8f;
            _barWidth = width - 24f;
            _cardHome = new Vector2(Margin, Margin);
            _cardPanel = UiBuild.Panel(_canvas, BottomLeft, _cardHome, new Vector2(width, stateTop + 52f), UiBuild.Glass);
            _cardOutline = UiBuild.Outline(_cardPanel, Color.clear);
            _card = _cardPanel.rectTransform;

            _factionPill = UiBuild.Panel(_card, TopLeft, new Vector2(12f, -10f), new Vector2(100f, 26f), Color.white, 0.8f);
            _factionText = UiBuild.Label(_factionPill.transform, "", 15, Ink, TextAnchor.MiddleCenter, Centre,
                Vector2.zero, new Vector2(100f, 26f), FontStyle.Bold, false);
            UiBuild.Label(_card, "YOU", 12, Dim, TextAnchor.MiddleLeft, TopLeft, new Vector2(120f, -10f), new Vector2(60f, 26f));

            _hearts = new Image[count];
            _heartLostAt = new float[count];
            for (int i = 0; i < count; i++)
            {
                _heartLostAt[i] = float.NegativeInfinity;
                _hearts[i] = UiBuild.Image(_card, UiBuild.Heart, TopLeft,
                    new Vector2(12f + i * (_heartSize + 6f), -heartsTop), new Vector2(_heartSize, _heartSize), HeartRed);
                Shadow shadow = _hearts[i].gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
                shadow.effectDistance = new Vector2(2f, -2f);
            }

            _stateText = UiBuild.Label(_card, "", 17, Color.white, TextAnchor.MiddleLeft, TopLeft,
                new Vector2(12f, -stateTop), new Vector2(_barWidth, 22f));
            _hintText = UiBuild.Label(_card, "", 12, Dim, TextAnchor.MiddleLeft, TopLeft,
                new Vector2(12f, -stateTop - 26f), new Vector2(_barWidth, 16f), FontStyle.Normal);
            _barTrack = UiBuild.Panel(_card, TopLeft, new Vector2(12f, -stateTop - 30f), new Vector2(_barWidth, 8f),
                new Color(0f, 0f, 0f, 0.45f), 0.25f);
            _barFill = UiBuild.Panel(_barTrack.transform, MidLeft, Vector2.zero, new Vector2(8f, 8f), Warn, 0.25f);
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
            _marker = UiBuild.Image(_crosshair, UiBuild.Cross, Vector2.zero, size * 1.9f, Color.white);
            _marker.enabled = false;
            _blockedText = UiBuild.Label(_canvas, "Plant first!  Press E", 17, Warn, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -46f), new Vector2(400f, 26f));
            _blockedText.enabled = false;
        }

        private static Image FullScreen(Transform parent, Sprite sprite, Color color)
        {
            Image image = UiBuild.Image(parent, sprite, Vector2.zero, Vector2.zero, color);
            UiBuild.Stretch(image.rectTransform);
            return image;
        }
    }
}
