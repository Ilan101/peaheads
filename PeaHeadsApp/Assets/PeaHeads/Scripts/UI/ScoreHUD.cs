using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Match HUD (uGUI, built in code): top-centre scoreboard (faction scores, bars, clock badge), "+N" score pop-ups,
    // "GO!" when the clock starts, last-seconds countdown, mushroom collect bar, rival-in-story note and the end-of-match
    // banner. Lives on the Match object; display and local sounds only. Scoreboard and collect bar hide while scoped in.
    [RequireComponent(typeof(MatchManager))]
    public class ScoreHUD : MonoBehaviour
    {
        private static readonly Vector2 Top = new(0.5f, 1f);
        private static readonly Vector2 Bottom = new(0.5f, 0f);
        private static readonly Vector2 Centre = new(0.5f, 0.5f);
        private static readonly Vector2 MidLeft = new(0f, 0.5f);
        private static readonly Vector2 MidRight = new(1f, 0.5f);
        private static readonly Color Live = new(0.45f, 0.95f, 0.5f);
        private static readonly Color Warn = new(1f, 0.78f, 0.3f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);
        private static readonly Color Dim = new(1f, 1f, 1f, 0.6f);
        private static readonly Color Ink = new(0.06f, 0.08f, 0.06f);

        [SerializeField, Range(400f, 900f)] private float _panelWidth = 580f;
        [SerializeField] private Color _collectColor = new(1f, 0.42f, 0.26f);
        [Tooltip("Ticks and a warning in the last seconds of the clock.")]
        [SerializeField, Range(0, 30)] private int _warningSeconds = 10;

        private MatchManager _match;
        private MushroomSpawner _mushrooms;
        private bool _rivalShielded;
        private float _nextRivalCheck;

        private bool _initialised;
        private readonly int[] _lastScore = new int[2];
        private readonly float[] _scoredAt = { float.NegativeInfinity, float.NegativeInfinity };
        private bool _wasRunning, _wasEnded;
        private int _lastSecond = -1;
        private float _goAt = float.NegativeInfinity, _warnAt = float.NegativeInfinity, _endedAt;

        private RectTransform _canvas;
        private float _barWidth;
        private readonly Text[] _names = new Text[2], _scores = new Text[2], _popups = new Text[2];
        private readonly Image[] _fills = new Image[2];
        private Image _clockRing;
        private Text _clock, _clockSub;
        private RectTransform _scoreboard;
        private Image _shield;
        private Text _shieldText;
        private Image _collectPanel, _collectFill;
        private Text _go, _goSub, _warn;
        private CanvasGroup _banner;
        private Image _bannerTop, _bannerBottom;
        private Text _bannerTitle, _bannerLine, _bannerResult, _bannerScore, _bannerNext;

        private void Awake()
        {
            _match = GetComponent<MatchManager>();
            _mushrooms = GetComponent<MushroomSpawner>();
        }

        private void OnDestroy()
        {
            if (_canvas) Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (!_match.IsSpawned || NetworkManager.Singleton == null)
            {
                if (_canvas) _canvas.gameObject.SetActive(false);
                _initialised = false;
                return;
            }
            if (!_canvas) Build();
            int local = MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId);
            if (!_initialised) Initialise();
            TrackEvents();
            CheckRival();

            bool show = !StoryIntro.IsPlaying;
            if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
            if (!show) return;
            _scoreboard.gameObject.SetActive(!PlayerHUD.Scoped);
            if (!PlayerHUD.Scoped) UpdateScoreboard(local);
            UpdateCollect(local);
            UpdateSplashes(local);
            UpdateBanner(local);
        }

        private void Initialise()
        {
            _initialised = true;
            for (int team = 0; team < 2; team++) _lastScore[team] = _match.Score(team);
            _wasRunning = _match.ClockRunning;
            _wasEnded = _match.IsEnded;
            _lastSecond = -1;
        }

        // Score pop-ups, GO!, countdown ticks and the end timestamp, from state changes.
        private void TrackEvents()
        {
            float now = Time.unscaledTime;
            for (int team = 0; team < 2; team++)
            {
                int score = _match.Score(team);
                if (score > _lastScore[team])
                {
                    _popups[team].text = $"+{score - _lastScore[team]}";
                    _scoredAt[team] = now;
                }
                _lastScore[team] = score;
            }

            bool running = _match.ClockRunning;
            if (running && !_wasRunning)
            {
                _goAt = now;
                GameAudio.Play(Sfx.Go, 0.9f);
            }
            _wasRunning = running;

            bool ended = _match.IsEnded;
            if (ended && !_wasEnded) _endedAt = now;
            _wasEnded = ended;

            int second = Mathf.CeilToInt(_match.TimeRemaining);
            if (running && second != _lastSecond && second > 0 && second <= _warningSeconds)
            {
                GameAudio.Play(second <= 3 ? Sfx.TickFinal : Sfx.Tick, 0.7f);
                if (second == _warningSeconds) _warnAt = now;
            }
            _lastSecond = second;
        }

        private void CheckRival()
        {
            if (Time.unscaledTime < _nextRivalCheck) return;
            _nextRivalCheck = Time.unscaledTime + 0.5f;
            _rivalShielded = false;
            foreach (PlayerNetSync player in FindObjectsByType<PlayerNetSync>(FindObjectsSortMode.None))
                if (!player.IsOwner && player.TryGetComponent(out PlayerHealth health) && health.IsShielded)
                    _rivalShielded = true;
        }

        // ---------- Scoreboard ----------

        private void UpdateScoreboard(int local)
        {
            float now = Time.unscaledTime;
            for (int team = 0; team < 2; team++)
            {
                Color color = Faction.Color(team);
                int score = _match.Score(team);
                _names[team].text = team == local
                    ? (team == 0 ? $"{Faction.Name(team)}  · YOU" : $"YOU ·  {Faction.Name(team)}")
                    : Faction.Name(team);
                _names[team].color = color;
                _scores[team].text = score.ToString();
                _scores[team].rectTransform.localScale = Vector3.one * UiBuild.Pop(now - _scoredAt[team], 0.35f, 0.5f);
                float fill = Mathf.Clamp01(score / (float)_match.TargetScore);
                _fills[team].enabled = fill > 0f;
                _fills[team].rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, _barWidth * fill), 10f);
                _fills[team].color = color;

                float since = now - _scoredAt[team];
                _popups[team].enabled = since < 1.2f;
                if (!_popups[team].enabled) continue;
                _popups[team].color = UiBuild.WithAlpha(color, Mathf.Clamp01((1.2f - since) / 0.4f));
                float x = (team == 0 ? -1f : 1f) * (_panelWidth * 0.5f - 16f - _barWidth * fill);
                _popups[team].rectTransform.anchoredPosition = new Vector2(x, -46f - 22f * since);
                _popups[team].rectTransform.localScale = Vector3.one * UiBuild.Pop(since, 0.3f, 0.6f);
            }

            bool waiting = !_match.ClockRunning && !_match.IsEnded;
            int seconds = Mathf.CeilToInt(_match.TimeRemaining);
            bool warning = _match.ClockRunning && seconds <= _warningSeconds;
            _clock.text = $"{seconds / 60}:{seconds % 60:00}";
            _clock.color = warning ? Bad : Color.white;
            _clock.rectTransform.localScale = Vector3.one * (warning ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(now * Mathf.PI)) : 1f);
            _clockRing.color = warning ? UiBuild.WithAlpha(Bad, 0.9f) : new Color(1f, 1f, 1f, 0.25f);
            // Just the timer; "WAITING" only while the clock waits for the intro story.
            _clockSub.text = waiting ? "WAITING" : "";
            _clock.rectTransform.anchoredPosition = new Vector2(0f, waiting ? 7f : 0f);
            _clockSub.color = waiting ? UiBuild.WithAlpha(Warn, 0.7f + 0.3f * Mathf.Sin(now * 5f)) : Dim;

            // Rival still in the intro story (can't be hit yet): pulsing shield beside their bar.
            bool shield = _rivalShielded && !_match.IsEnded;
            _shield.enabled = _shieldText.enabled = shield;
            if (!shield) return;
            float side = 1 - local == 0 ? -1f : 1f;
            _shield.rectTransform.anchoredPosition = new Vector2(side * (_panelWidth * 0.5f + 24f), 4f);
            _shield.color = UiBuild.WithAlpha(Warn, 0.65f + 0.35f * Mathf.Sin(now * 5f));
            _shieldText.rectTransform.anchoredPosition = new Vector2(side * (_panelWidth * 0.5f + 24f), -22f);
        }

        private void UpdateCollect(int local)
        {
            float progress = _mushrooms ? _mushrooms.CollectProgress(local) : 0f;
            bool show = progress > 0f && !_match.IsEnded && !PlayerHUD.Scoped;
            _collectPanel.gameObject.SetActive(show);
            if (!show) return;
            _collectFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, 230f * progress), 10f);
            _collectFill.color = Color.Lerp(_collectColor, Color.white, 0.25f * Mathf.Sin(Time.unscaledTime * 8f) + 0.25f);
        }

        // ---------- GO! and countdown warning ----------

        private void UpdateSplashes(int local)
        {
            float go = Time.unscaledTime - _goAt;
            _go.enabled = _goSub.enabled = go < 1.8f;
            if (_go.enabled)
            {
                float alpha = go < 0.1f ? go / 0.1f : Mathf.Clamp01((1.8f - go) / 0.6f);
                _go.color = UiBuild.WithAlpha(Faction.Color(local), alpha);
                _goSub.color = UiBuild.WithAlpha(Color.white, alpha);
                _go.rectTransform.localScale = Vector3.one * UiBuild.Pop(go, 0.35f, 0.7f);
            }

            float warn = Time.unscaledTime - _warnAt;
            _warn.enabled = warn < 1.8f;
            if (!_warn.enabled) return;
            _warn.color = UiBuild.WithAlpha(Bad, warn < 0.1f ? warn / 0.1f : Mathf.Clamp01((1.8f - warn) / 0.6f));
            _warn.rectTransform.localScale = Vector3.one * UiBuild.Pop(warn, 0.3f, 0.4f);
        }

        // ---------- End banner ----------

        private void UpdateBanner(int local)
        {
            bool ended = _match.IsEnded;
            _banner.alpha = Mathf.MoveTowards(_banner.alpha, ended ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            _banner.gameObject.SetActive(_banner.alpha > 0f);
            if (!ended) return;

            int winner = _match.Winner;
            bool draw = winner < 0;
            Color color = draw ? Color.white : Faction.Color(winner);
            _bannerTop.color = _bannerBottom.color = color;
            _bannerTitle.text = draw ? "DRAW" : $"{Faction.Name(winner)} WINS";
            _bannerTitle.color = color;
            _bannerTitle.rectTransform.localScale = Vector3.one * UiBuild.Pop(Time.unscaledTime - _endedAt, 0.45f, 0.5f);
            _bannerLine.text = draw ? "Neither side prevails. The Grove hangs in the balance." : Faction.WinLine(winner);
            _bannerResult.text = draw ? "TIED ON POINTS" : winner == local ? "YOU WIN!" : "YOU LOSE";
            _bannerResult.color = draw ? Color.white : winner == local ? Live : Bad;
            _bannerScore.text = $"{Faction.Name(0)}  {_match.Score(0)}   —   {_match.Score(1)}  {Faction.Name(1)}";
            float left = _match.RestartDelay - (Time.unscaledTime - _endedAt);
            _bannerNext.text = left > 0f ? $"Next round in {Mathf.CeilToInt(left)}" : "";
        }

        // ---------- Build ----------

        private void Build()
        {
            _canvas = UiBuild.Canvas("ScoreHUDCanvas", 45);
            float half = _panelWidth * 0.5f;
            _barWidth = half - 78f;

            Image panel = UiBuild.Panel(_canvas, Top, new Vector2(0f, -12f), new Vector2(_panelWidth, 74f), UiBuild.Glass);
            _scoreboard = panel.rectTransform;
            UiBuild.Outline(panel, new Color(1f, 1f, 1f, 0.12f));
            Transform p = panel.transform;
            for (int team = 0; team < 2; team++)
            {
                float side = team == 0 ? -1f : 1f;
                Vector2 outer = team == 0 ? MidLeft : MidRight;
                TextAnchor outerAlign = team == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                TextAnchor innerAlign = team == 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

                _names[team] = UiBuild.Label(p, "", 14, Color.white, outerAlign, outer,
                    new Vector2(-side * 16f, 18f), new Vector2(220f, 20f));
                _scores[team] = UiBuild.Label(p, "0", 34, Color.white, innerAlign, Centre,
                    new Vector2(side * 96f, 8f), new Vector2(80f, 40f));
                UiBuild.Outline(_scores[team], new Color(0f, 0f, 0f, 0.4f));

                Image track = UiBuild.Panel(p, outer, new Vector2(-side * 16f, -20f), new Vector2(_barWidth, 10f),
                    new Color(0f, 0f, 0f, 0.45f), 0.3f);
                _fills[team] = UiBuild.Panel(track.transform, outer, Vector2.zero, new Vector2(10f, 10f), Color.white, 0.3f);
                _popups[team] = UiBuild.Label(p, "", 22, Color.white, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                    new Vector2(80f, 30f));
                UiBuild.Outline(_popups[team], new Color(0f, 0f, 0f, 0.5f));
                _popups[team].enabled = false;
            }

            Image badge = UiBuild.Image(p, UiBuild.Circle, new Vector2(0f, -6f), new Vector2(86f, 86f),
                new Color(0.05f, 0.08f, 0.06f, 0.96f));
            _clockRing = UiBuild.Image(badge.transform, UiBuild.Ring, Vector2.zero, new Vector2(86f, 86f), Color.white);
            _clock = UiBuild.Label(badge.transform, "", 24, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, 7f), new Vector2(86f, 30f));
            _clockSub = UiBuild.Label(badge.transform, "", 9, Dim, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -16f), new Vector2(86f, 14f), FontStyle.Bold, false);

            _shield = UiBuild.Image(p, UiBuild.Shield, Vector2.zero, new Vector2(30f, 30f), Warn);
            _shieldText = UiBuild.Label(p, "IN STORY", 9, Warn, TextAnchor.MiddleCenter, Centre, Vector2.zero,
                new Vector2(70f, 14f));

            _collectPanel = UiBuild.Panel(_canvas, Bottom, new Vector2(0f, 150f), new Vector2(320f, 56f), UiBuild.Glass);
            UiBuild.Outline(_collectPanel, UiBuild.WithAlpha(_collectColor, 0.6f));
            Transform c = _collectPanel.transform;
            UiBuild.Image(c, UiBuild.Glow, MidLeft, new Vector2(2f, 0f), new Vector2(56f, 56f), UiBuild.WithAlpha(_collectColor, 0.5f));
            UiBuild.Image(c, UiBuild.Mushroom, MidLeft, new Vector2(14f, 0f), new Vector2(32f, 32f), _collectColor);
            UiBuild.Label(c, "Drinking the Sporelight…", 14, Color.white, TextAnchor.MiddleLeft, MidLeft,
                new Vector2(62f, 10f), new Vector2(240f, 20f));
            Image collectTrack = UiBuild.Panel(c, MidLeft, new Vector2(62f, -12f), new Vector2(230f, 10f),
                new Color(0f, 0f, 0f, 0.45f), 0.3f);
            _collectFill = UiBuild.Panel(collectTrack.transform, MidLeft, Vector2.zero, new Vector2(10f, 10f), _collectColor, 0.3f);
            _collectPanel.gameObject.SetActive(false);

            _go = UiBuild.Label(_canvas, "GO!", 96, Color.white, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 70f),
                new Vector2(800f, 110f));
            UiBuild.Outline(_go, new Color(0f, 0f, 0f, 0.6f), 3f);
            _goSub = UiBuild.Label(_canvas, "Claim the Grove!", 26, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, 4f), new Vector2(800f, 34f));
            _warn = UiBuild.Label(_canvas, $"{_warningSeconds} SECONDS LEFT!", 36, Bad, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, 150f), new Vector2(800f, 50f));
            UiBuild.Outline(_warn, new Color(0f, 0f, 0f, 0.6f), 2f);
            _go.enabled = _goSub.enabled = _warn.enabled = false;

            BuildBanner();
            UiBuild.Display(_scores[0], _scores[1], _names[0], _names[1], _popups[0], _popups[1], _clock, _go, _goSub, _warn,
                _bannerTitle, _bannerResult);
        }

        private void BuildBanner()
        {
            RectTransform band = UiBuild.Node(_canvas, "EndBanner", Centre, new Vector2(0f, 30f), new Vector2(0f, 230f));
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.sizeDelta = new Vector2(0f, 230f);
            Image back = band.gameObject.AddComponent<Image>();
            back.color = new Color(0.02f, 0.03f, 0.03f, 0.72f);
            back.raycastTarget = false;
            _banner = band.gameObject.AddComponent<CanvasGroup>();
            _banner.alpha = 0f;
            _banner.blocksRaycasts = false;

            _bannerTop = Line(band, 1f);
            _bannerBottom = Line(band, 0f);
            _bannerTitle = UiBuild.Label(band, "", 68, Color.white, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 58f),
                new Vector2(1000f, 80f));
            UiBuild.Outline(_bannerTitle, new Color(0f, 0f, 0f, 0.6f), 2.5f);
            _bannerLine = UiBuild.Label(band, "", 21, Color.white, TextAnchor.MiddleCenter, Centre, new Vector2(0f, 6f),
                new Vector2(1000f, 30f), FontStyle.Italic);
            _bannerResult = UiBuild.Label(band, "", 30, Color.white, TextAnchor.MiddleCenter, Centre,
                new Vector2(0f, -34f), new Vector2(1000f, 40f));
            _bannerScore = UiBuild.Label(band, "", 17, Dim, TextAnchor.MiddleCenter, Centre, new Vector2(0f, -68f),
                new Vector2(1000f, 24f));
            _bannerNext = UiBuild.Label(band, "", 15, Dim, TextAnchor.MiddleCenter, Centre, new Vector2(0f, -94f),
                new Vector2(1000f, 22f), FontStyle.Normal);
            band.gameObject.SetActive(false);
        }

        // Full-width 4-unit stripe on the band's top (y = 1) or bottom (y = 0) edge.
        private static Image Line(RectTransform band, float y)
        {
            RectTransform rect = UiBuild.Node(band, "Stripe", new Vector2(0.5f, y), Vector2.zero, new Vector2(0f, 4f));
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y);
            rect.sizeDelta = new Vector2(0f, 4f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
    }
}
