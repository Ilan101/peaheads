using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeaHeads
{
    // Intro cutscene played when a session starts: typewriter story slides over small animated drawings, ending on
    // the local player's faction card. Built in code like GameMenu. Space/Enter advance (the last slide continues to the lobby), Esc skips straight to it.
    public class StoryIntro : MonoBehaviour
    {
        private const float FadeTime = 0.35f;
        private const float GroundY = -120f;
        private const int SporeCount = 40;
        private const int SlideCount = 5;
        // Pea drawing (scale 1, facing right): face centre and mouth relative to the root at the leaf base.
        private static readonly Vector2 FaceOffset = new(0f, 108f);
        private static readonly Vector2 MouthOffset = new(74f, 102f);
        private static readonly Vector2 Reference = new(1280f, 720f);
        private static readonly Color Gold = new(1f, 0.84f, 0.42f);
        private static readonly Color Cream = new(0.95f, 0.9f, 0.8f);
        private static readonly Color Soil = new(0.24f, 0.16f, 0.1f);
        private static readonly Color GrassDark = new(0.22f, 0.42f, 0.18f);
        private static readonly Color GrassLight = new(0.36f, 0.6f, 0.26f);
        private static readonly Color Skin = new(0.96f, 0.8f, 0.65f);
        private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);

        [SerializeField] private Font _font;
        [SerializeField, Range(10f, 200f)] private float _charsPerSecond = 60f;

        // Read by the IMGUI HUDs, which would otherwise draw over the cutscene.
        public static bool IsPlaying { get; private set; }

        private struct Slide
        {
            public string Title;
            public string Body;
            public Color TitleColor;
            public Color Background;
            public Color Spore;
            public Func<RectTransform, Action<float>> Draw;
        }

        private sealed class Pea
        {
            public RectTransform Root;
            public RectTransform Head;
            public RectTransform Mouth;
            public Image Face;
            public Image Eye;
            public Image Pupil;
        }

        private Slide[] _slides;
        private Action _onDone;
        private MatchManager _match;
        private int _index;
        private int _next = -1;
        private float _slideTime;
        private float _typed;
        private Action<float> _animate;

        private const int BokehCount = 7;
        private const float SlideShift = 70f;

        private GameObject _canvas;
        private Image _background;
        private CanvasGroup _content;
        private RectTransform _contentRect;
        private CanvasGroup _hintGroup;
        private RectTransform[] _bokeh;
        private Image[] _bokehImages;
        private Text _title;
        private Text _body;
        private RectTransform _art;
        private Image[] _dots;
        private RectTransform[] _spores;
        private Image[] _sporeImages;
        private float[] _sporeSpeed;
        private Sprite _circle;
        private Sprite _glow;

        private int Last => _slides.Length - 1;

        private void OnDisable()
        {
            IsPlaying = false;
            MatchManager.TeamsChanged -= OnTeamsChanged;
        }

        // match may be null (scores then use the defaults from the spec).
        public void Play(int team, MatchManager match, Action onDone)
        {
            if (!_canvas) Build();
            _onDone = onDone;
            _match = match;
            _slides = BuildFor(team);
            MatchManager.TeamsChanged -= OnTeamsChanged;
            MatchManager.TeamsChanged += OnTeamsChanged;
            _canvas.SetActive(true);
            _content.alpha = 0f;
            _background.color = _slides[0].Background;
            IsPlaying = true;
            ShowSlide(0);
        }

        public void Stop()
        {
            IsPlaying = false;
            MatchManager.TeamsChanged -= OnTeamsChanged;
            _onDone = null;
            if (_canvas) _canvas.SetActive(false);
        }

        private Slide[] BuildFor(int team) =>
            BuildSlides(team, _match ? _match.TargetScore : 20, _match ? _match.MushroomPoints : 3,
                _match ? _match.KillPoints : 2);

        // Teams are reshuffled when a client joins, possibly mid-story: retell it for the team we have now.
        private void OnTeamsChanged()
        {
            if (!IsPlaying || NetworkManager.Singleton == null) return;
            Slide[] slides = BuildFor(MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId));
            bool redraw = slides[_index].Title != _slides[_index].Title;
            _slides = slides;
            if (redraw) ShowSlide(_index);
        }

        private void Update()
        {
            if (!IsPlaying) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            float dt = Time.unscaledDeltaTime;

            bool leaving = _next >= 0;
            if (leaving)
            {
                _content.alpha = Mathf.MoveTowards(_content.alpha, 0f, dt / FadeTime);
                if (_content.alpha <= 0f) ShowSlide(_next);
            }
            else _content.alpha = Mathf.MoveTowards(_content.alpha, 1f, dt / FadeTime);
            // Outgoing slide drifts left, the next one arrives from the right.
            float away = 1f - _content.alpha;
            _contentRect.anchoredPosition = new Vector2((leaving ? -1f : 1f) * away * away * SlideShift, 0f);

            Slide slide = _slides[_index];
            float blend = 1f - Mathf.Exp(-3f * dt);
            _background.color = Color.Lerp(_background.color, slide.Background, blend);
            UpdateSpores(dt, slide.Spore, blend);

            _slideTime += dt;
            _typed = Mathf.Min(slide.Body.Length, _typed + dt * _charsPerSecond);
            _body.text = slide.Body.Substring(0, (int)_typed);
            _animate?.Invoke(_slideTime);
            _title.rectTransform.localScale = Vector3.one * UiBuild.Pop(_slideTime, 0.45f, 0.14f);
            // The hint pulses once the text is fully shown: the player decides when to move on.
            bool typed = _typed >= slide.Body.Length;
            _hintGroup.alpha = typed ? 0.7f + 0.3f * Mathf.Sin(_slideTime * 4f) : 0.4f;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                kb.numpadEnterKey.wasPressedThisFrame) Advance();
            else if (kb.escapeKey.wasPressedThisFrame) Finish();
        }

        private void ShowSlide(int index)
        {
            _index = index;
            _next = -1;
            _slideTime = 0f;
            _typed = 0f;
            Slide slide = _slides[index];
            _title.text = slide.Title;
            _title.color = slide.TitleColor;
            _body.text = "";
            for (int i = _art.childCount - 1; i >= 0; i--) Destroy(_art.GetChild(i).gameObject);
            _animate = slide.Draw(_art);
            _hintGroup.alpha = 0.4f;
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i].color = i == index ? slide.TitleColor : new Color(1f, 1f, 1f, 0.25f);
                _dots[i].rectTransform.sizeDelta = i == index ? new Vector2(26f, 10f) : new Vector2(10f, 10f);
            }
            GameAudio.Play(Sfx.Page, 0.7f, 0.05f);
        }

        private void Advance()
        {
            if (_next >= 0) return;
            if (_typed < _slides[_index].Body.Length) _typed = _slides[_index].Body.Length;
            else if (_index == Last) Finish();
            else _next = _index + 1;
        }

        private void Finish()
        {
            Action done = _onDone;
            Stop();
            done?.Invoke();
        }

        private void UpdateSpores(float dt, Color color, float blend)
        {
            Rect area = ((RectTransform)_canvas.transform).rect;
            float time = Time.unscaledTime;
            for (int i = 0; i < BokehCount; i++)
            {
                float a = time * (0.05f + i * 0.012f) + i * 2.1f;
                _bokeh[i].anchoredPosition = new Vector2(Mathf.Sin(a) * area.width * 0.42f, Mathf.Cos(a * 0.8f + i) * area.height * 0.38f);
                _bokehImages[i].color = Color.Lerp(_bokehImages[i].color, WithAlpha(color, 0.07f + 0.03f * (i % 3)), blend);
            }
            for (int i = 0; i < SporeCount; i++)
            {
                Vector2 p = _spores[i].anchoredPosition;
                p.y += _sporeSpeed[i] * dt;
                p.x += Mathf.Sin(time * 0.7f + i * 1.7f) * 12f * dt;
                if (p.y > area.yMax + 20f) p = new Vector2(UnityEngine.Random.Range(area.xMin, area.xMax), area.yMin - 20f);
                _spores[i].anchoredPosition = p;
                float alpha = 0.25f + 0.4f * Mathf.PerlinNoise(time * 0.8f, i * 0.37f);
                _sporeImages[i].color = Color.Lerp(_sporeImages[i].color, WithAlpha(color, alpha), blend);
            }
        }

        // ---------- Story ----------

        private Slide[] BuildSlides(int team, int target, int mushroomPoints, int killPoints)
        {
            Color mine = Faction.Color(team);
            Color rival = Faction.Color(1 - team);
            return new[]
            {
                new Slide
                {
                    Title = "SAVE THE GROVE",
                    Body = "The Rot is draining the Sporelights, the glowing mushrooms that keep the Grove alive.\n" +
                           "Two PeaHeads will decide its fate: BLOOM and BLIGHT.",
                    TitleColor = Gold, Background = new Color(0.07f, 0.09f, 0.11f),
                    Spore = Color.Lerp(Faction.BloomColor, Faction.BlightColor, 0.5f), Draw = DrawSeeds,
                },
                new Slide
                {
                    Title = "PLANT TO SPIT",
                    Body = "You can walk OR fight, never both.\nPlant your roots to spit peas, uproot to move again.",
                    TitleColor = mine, Background = new Color(0.06f, 0.12f, 0.08f), Spore = mine,
                    Draw = art => DrawPlanting(art, mine),
                },
                new Slide
                {
                    Title = "DODGE WITH YOUR REAL HEAD",
                    Body = "The webcam is your controller: your PeaHead copies your real head.\n" +
                           "Lean to dodge peas, open your mouth to spit.",
                    TitleColor = mine, Background = new Color(0.06f, 0.1f, 0.12f), Spore = mine,
                    Draw = art => DrawNeck(art, mine, rival),
                },
                new Slide
                {
                    Title = "CLOSE ONE EYE TO AIM",
                    Body = "Aiming zooms in, and your pea flies faster and dead straight.\n" +
                           "Without aiming, peas start to drop after a short distance.",
                    TitleColor = Gold, Background = new Color(0.08f, 0.09f, 0.12f), Spore = Gold,
                    Draw = art => DrawAim(art, mine, rival),
                },
                new Slide
                {
                    Title = $"YOU ARE {Faction.Name(team)}",
                    Body = $"Score for {Faction.Name(team)} and claim the Grove.\n" +
                           $"First to {target} points wins. Good luck, little pea!",
                    TitleColor = mine, Background = Shade(mine, 0.16f), Spore = mine,
                    Draw = art => DrawClaim(art, mine, rival, mushroomPoints, killPoints, target),
                },
            };
        }

        private Action<float> DrawSeeds(RectTransform art)
        {
            Ground(art);
            RectTransform pod = Shape(art, new Vector2(0f, -80f), new Vector2(150f, 70f),
                Shade(Faction.BloomColor, 0.6f)).rectTransform;
            Image bloomGlow = Shape(art, new Vector2(0f, -10f), new Vector2(260f, 260f), Color.clear, _glow);
            Image blightGlow = Shape(art, new Vector2(0f, -10f), new Vector2(260f, 260f), Color.clear, _glow);
            Pea bloom = MakePea(art, Faction.BloomColor, false);
            Pea blight = MakePea(art, Faction.BlightColor, true);
            Text bloomName = Label(art, new Vector2(-180f, 95f), new Vector2(240f, 34f), 26, FontStyle.Bold);
            Text blightName = Label(art, new Vector2(180f, 95f), new Vector2(240f, 34f), 26, FontStyle.Bold);
            bloomName.text = Faction.Name(0);
            blightName.text = Faction.Name(1);
            return t =>
            {
                float k = Smooth(Mathf.Clamp01((t - 0.6f) / 1.6f));
                pod.localScale = new Vector3(1f + k * 0.4f, 1f - k, 1f);
                float s = Mathf.Lerp(0.25f, 1f, k);
                float bob = Mathf.Sin(t * 3f) * 4f;
                PlacePea(bloom, new Vector2(-180f * k, 0f), s, false);
                PlacePea(blight, new Vector2(180f * k, 0f), s, true);
                bloom.Head.anchoredPosition = new Vector2(0f, bob);
                blight.Head.anchoredPosition = new Vector2(0f, -bob);
                bloomGlow.rectTransform.anchoredPosition = new Vector2(-180f * k, -10f);
                blightGlow.rectTransform.anchoredPosition = new Vector2(180f * k, -10f);
                bloomGlow.color = WithAlpha(Faction.BloomColor, 0.35f * k);
                blightGlow.color = WithAlpha(Faction.BlightColor, 0.35f * k);
                bloomName.color = WithAlpha(Faction.BloomColor, k);
                blightName.color = WithAlpha(Faction.BlightColor, k);
            };
        }

        private Action<float> DrawPlanting(RectTransform art, Color color)
        {
            Ground(art);
            Pea pea = MakePea(art, color, false);
            RectTransform spit = Shape(art, Vector2.zero, new Vector2(22f, 22f), Shade(color, 1.2f)).rectTransform;
            Prompt walkKeys = KeyPrompt(art, -280f, "WALK", "W", "A", "S", "D");
            Prompt plantKey = KeyPrompt(art, 0f, "PLANT", "SPACE");
            Prompt spitKeys = IconPrompt(art, 280f, "OPEN MOUTH = SPIT", MouthIcon);
            return t =>
            {
                float p = t % 5f;
                float walk = Mathf.Clamp01(p / 1.6f);
                float x = Mathf.Lerp(-280f, -140f, walk);
                float bob = p < 1.6f ? Mathf.Abs(Mathf.Sin(p * 9f)) * 10f : 0f;
                float sink = Smooth(Mathf.Clamp01((p - 1.9f) / 0.6f)) * 26f;
                PlacePea(pea, new Vector2(x, bob - sink), 1f, false);

                bool firing = p > 2.7f;
                float shot = firing ? (p - 2.7f) % 0.75f : 0f;
                pea.Mouth.localScale = Vector3.one * (firing && shot < 0.15f ? 1.9f : 1f);
                Vector2 mouth = pea.Root.anchoredPosition + MouthOffset;
                spit.gameObject.SetActive(firing);
                spit.anchoredPosition = mouth + new Vector2(shot * 700f, 0f);

                Light(walkKeys, p < 1.6f, color, p < 1.6f && Mathf.Repeat(p, 0.4f) < 0.2f);
                Light(plantKey, p >= 1.6f && p < 2.7f, color, p >= 1.75f && p < 2f);
                Light(spitKeys, firing, color, firing && shot < 0.15f);
            };
        }

        // Side by side: your real head leans a little, your PeaHead leans a lot, the incoming pea misses.
        private Action<float> DrawNeck(RectTransform art, Color color, Color rival)
        {
            Ground(art);
            RectTransform you = Node(art, "You", new Vector2(-220f, -10f), Vector2.zero);
            Shape(you, new Vector2(0f, -80f), new Vector2(120f, 60f), new Color(0.3f, 0.4f, 0.6f));
            RectTransform face = Shape(you, Vector2.zero, new Vector2(100f, 110f), Skin).rectTransform;
            Shape(face, new Vector2(0f, 44f), new Vector2(104f, 40f), new Color(0.3f, 0.2f, 0.12f));
            Shape(face, new Vector2(-20f, 6f), new Vector2(12f, 12f), Ink);
            RectTransform wink = Shape(face, new Vector2(20f, 6f), new Vector2(12f, 12f), Ink).rectTransform;
            RectTransform mouth = Shape(face, new Vector2(0f, -26f), new Vector2(26f, 8f), Ink).rectTransform;
            Text youLabel = Label(art, new Vector2(-220f, 120f), new Vector2(200f, 30f), 22, FontStyle.Bold);
            youLabel.text = "YOU";
            Text arrow = Label(art, new Vector2(-40f, 10f), new Vector2(120f, 40f), 34, FontStyle.Bold);
            arrow.text = ">>";

            var stem = new RectTransform[7];
            for (int i = 0; i < stem.Length; i++)
                stem[i] = Shape(art, Vector2.zero, new Vector2(18f, 18f), Shade(color, 0.7f)).rectTransform;
            Vector2 baseCentre = new(180f, GroundY + 6f);
            Shape(art, baseCentre + new Vector2(-26f, 0f), new Vector2(60f, 20f), Shade(color, 0.6f));
            Shape(art, baseCentre + new Vector2(26f, 0f), new Vector2(60f, 20f), Shade(color, 0.6f));
            RectTransform head = Node(art, "PeaHead", Vector2.zero, Vector2.zero);
            Shape(head, Vector2.zero, new Vector2(96f, 96f), color);
            Shape(head, new Vector2(-18f, 10f), new Vector2(24f, 24f), Color.white);
            Shape(head, new Vector2(18f, 10f), new Vector2(24f, 24f), Color.white);
            Shape(head, new Vector2(-18f, 10f), new Vector2(11f, 11f), Color.black);
            Shape(head, new Vector2(18f, 10f), new Vector2(11f, 11f), Color.black);
            RectTransform snout = Shape(head, new Vector2(0f, -18f), new Vector2(30f, 30f), Shade(color, 0.85f)).rectTransform;
            Text peaLabel = Label(art, new Vector2(180f, 120f), new Vector2(260f, 30f), 22, FontStyle.Bold);
            peaLabel.text = "YOUR PEAHEAD";
            peaLabel.color = color;

            Image shot = Shape(art, Vector2.zero, new Vector2(30f, 30f), Color.white);
            Vector2 headRest = new(180f, 10f);
            Prompt leanPrompt = IconPrompt(art, -280f, "LEAN = DODGE", LeanIcon);
            Prompt mouthPrompt = IconPrompt(art, 0f, "OPEN MOUTH = SPIT", MouthIcon);
            Prompt winkPrompt = IconPrompt(art, 280f, "CLOSE ONE EYE = AIM", WinkIcon);
            const float speed = 2.2f;
            float period = Mathf.PI / speed; // time between full leans
            return t =>
            {
                float sway = Mathf.Sin(t * speed);
                you.anchoredPosition = new Vector2(-220f + sway * 14f, -10f);
                you.localRotation = Quaternion.Euler(0f, 0f, -sway * 8f);
                Vector2 headPos = headRest + new Vector2(sway * 85f, -Mathf.Abs(sway) * 12f);
                head.anchoredPosition = headPos;
                head.localRotation = Quaternion.Euler(0f, 0f, -sway * 18f);
                for (int i = 0; i < stem.Length; i++)
                {
                    float u = (i + 1f) / (stem.Length + 1f);
                    Vector2 control = new(baseCentre.x, Mathf.Lerp(baseCentre.y, headPos.y, 0.6f));
                    stem[i].anchoredPosition = Bezier(baseCentre, control, headPos + Vector2.down * 30f, u);
                }

                float q = Mathf.Repeat(t - period * 0.5f, period) / period;
                shot.rectTransform.anchoredPosition = headRest;
                shot.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.6f, q * q);
                shot.color = WithAlpha(rival, q < 0.85f ? 1f : (1f - q) / 0.15f);

                float cycle = t % 4f;
                wink.localScale = new Vector3(1f, cycle > 2.6f && cycle < 3.4f ? 0.15f : 1f, 1f);
                bool open = cycle > 1.2f && cycle < 1.7f;
                mouth.sizeDelta = open ? new Vector2(22f, 20f) : new Vector2(26f, 8f);
                snout.localScale = Vector3.one * (open ? 1.25f : 1f);

                bool winking = cycle > 2.6f && cycle < 3.4f;
                Light(leanPrompt, Mathf.Abs(sway) > 0.55f, color, false);
                leanPrompt.Icon.localRotation = Quaternion.Euler(0f, 0f, -sway * 18f);
                Light(mouthPrompt, open, color, open);
                Light(winkPrompt, winking, color, winking);
            };
        }

        // Normal shot drops short of the rival; the aimed one (one eye closed) flies faster and straight into them.
        private Action<float> DrawAim(RectTransform art, Color color, Color rival)
        {
            const float period = 5.2f, fireAt = 0.7f, gravity = 900f, ground = GroundY + 8f;
            Ground(art);
            Pea pea = MakePea(art, color, false);
            PlacePea(pea, new Vector2(-300f, -22f), 1f, false);
            Pea target = MakePea(art, rival, true);
            PlacePea(target, new Vector2(300f, -22f), 1f, true);
            Color targetColor = target.Face.color;
            Vector2 mouth = pea.Root.anchoredPosition + MouthOffset;
            Vector2 hitPoint = target.Root.anchoredPosition + FaceOffset;

            var trail = new Image[10];
            for (int i = 0; i < trail.Length; i++)
                trail[i] = Shape(art, Vector2.zero, new Vector2(12f - i * 0.6f, 12f - i * 0.6f), Color.clear);
            RectTransform shot = Shape(art, Vector2.zero, new Vector2(24f, 24f), Shade(color, 1.2f)).rectTransform;
            RectTransform splat = Shape(art, Vector2.zero, new Vector2(44f, 14f), Shade(color, 0.9f)).rectTransform;
            Image scope = Shape(art, hitPoint, new Vector2(130f, 130f), Color.white, UiBuild.Ring);
            Text tag = Label(art, new Vector2(0f, 128f), new Vector2(760f, 36f), 26, FontStyle.Normal);
            tag.font = UiBuild.DisplayFont;
            Prompt eyePrompt = IconPrompt(art, -280f, "CLOSE ONE EYE = AIM", WinkIcon);
            Prompt mouthPrompt = IconPrompt(art, 0f, "OPEN MOUTH = SPIT", MouthIcon);
            Prompt straightPrompt = IconPrompt(art, 280f, "AIMED: FASTER + STRAIGHT", parent => PeaIcon(parent, color));

            Vector2 Position(bool aimed, float s)
            {
                float speed = aimed ? 640f : 380f;
                float x = speed * s;
                float straight = aimed ? 2000f : 150f;
                float y = x > straight ? -0.5f * gravity * Mathf.Pow((x - straight) / speed, 2f) : 0f;
                return mouth + new Vector2(x, y);
            }

            float ImpactTime(bool aimed) => aimed
                ? (hitPoint.x - mouth.x) / 640f
                : 150f / 380f + Mathf.Sqrt(2f * (mouth.y - ground) / gravity);

            return t =>
            {
                float p = t % period;
                bool aimed = p >= period * 0.5f;
                float since = (aimed ? p - period * 0.5f : p) - fireAt;
                float impact = ImpactTime(aimed);
                bool fired = since >= 0f, landed = since >= impact;

                pea.Eye.rectTransform.localScale = new Vector3(1f, aimed ? 0.15f : 1f, 1f);
                pea.Pupil.enabled = !aimed;
                pea.Mouth.localScale = Vector3.one * (fired && since < 0.15f ? 1.9f : 1f);

                shot.gameObject.SetActive(fired && !landed);
                shot.anchoredPosition = Position(aimed, Mathf.Min(Mathf.Max(since, 0f), impact));
                for (int i = 0; i < trail.Length; i++)
                {
                    float s = Mathf.Min(since, impact) - i * 0.04f;
                    bool visible = fired && s >= 0f && since - impact < 0.35f;
                    trail[i].enabled = visible;
                    if (!visible) continue;
                    trail[i].rectTransform.anchoredPosition = Position(aimed, s);
                    trail[i].color = WithAlpha(Shade(color, 1.2f), 0.55f * (1f - i / (float)trail.Length));
                }

                splat.gameObject.SetActive(!aimed && landed && since - impact < 1f);
                splat.anchoredPosition = new Vector2(Position(false, impact).x, GroundY - 2f);
                float flash = aimed && landed ? Mathf.Clamp01((since - impact) / 0.6f) : 1f;
                target.Face.color = Color.Lerp(Color.white, targetColor, flash);

                scope.enabled = aimed;
                scope.color = WithAlpha(Gold, 0.55f + 0.25f * Mathf.Sin(t * 8f));
                scope.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(t * 4f));
                tag.text = aimed ? "AIMED: FAST AND STRAIGHT" : "NORMAL SHOT: DROPS SHORT";
                tag.color = aimed ? Gold : new Color(1f, 1f, 1f, 0.75f);

                Light(eyePrompt, aimed, color, false);
                Light(mouthPrompt, fired && since < 0.25f, color, fired && since < 0.15f);
                Light(straightPrompt, aimed && fired, Gold, false);
            };
        }

        private Action<float> DrawClaim(RectTransform art, Color mine, Color rival, int mushroomPoints, int killPoints, int winScore)
        {
            Ground(art);
            Image glow = Sporelight(art, -300f, Gold, 1.1f, out _);
            Pea pea = MakePea(art, mine, false);
            PlacePea(pea, new Vector2(-160f, -22f), 1f, false);
            Pea enemy = MakePea(art, rival, true);
            PlacePea(enemy, new Vector2(240f, -22f), 1f, true);
            var motes = new RectTransform[5];
            for (int i = 0; i < motes.Length; i++)
                motes[i] = Shape(art, Vector2.zero, new Vector2(14f, 14f), Gold, _glow).rectTransform;
            RectTransform spit = Shape(art, Vector2.zero, new Vector2(22f, 22f), Shade(mine, 1.2f)).rectTransform;
            Text plusOne = Label(art, Vector2.zero, new Vector2(120f, 40f), 30, FontStyle.Bold);
            plusOne.text = $"+{mushroomPoints}";
            Text plusThree = Label(art, Vector2.zero, new Vector2(120f, 40f), 34, FontStyle.Bold);
            plusThree.text = $"+{killPoints}";
            Color enemyColor = enemy.Face.color;
            Vector2 source = new(-300f, -50f);
            Vector2 sink = pea.Root.anchoredPosition + FaceOffset;
            Vector2 mouth = pea.Root.anchoredPosition + MouthOffset;
            Vector2 target = enemy.Root.anchoredPosition + FaceOffset;
            Prompt sporelight = IconPrompt(art, -280f, $"+{mushroomPoints}  PLANT ON A SPORELIGHT", SporelightIcon);
            Prompt knockout = IconPrompt(art, 0f, $"+{killPoints}  KNOCK OUT YOUR RIVAL", parent => PeaIcon(parent, mine));
            Prompt win = IconPrompt(art, 280f, $"FIRST TO {winScore} WINS", parent => MedalIcon(parent, winScore));
            return t =>
            {
                glow.color = WithAlpha(Gold, 0.4f + 0.2f * Mathf.Sin(t * 3f));
                for (int i = 0; i < motes.Length; i++)
                {
                    float u = Mathf.Repeat(t * 0.7f + i / (float)motes.Length, 1f);
                    motes[i].anchoredPosition = Vector2.Lerp(source, sink, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * 50f;
                }

                float a = Mathf.Repeat(t, 1.5f) / 1.5f;
                plusOne.rectTransform.anchoredPosition = new Vector2(-300f, 30f + a * 60f);
                plusOne.color = WithAlpha(Gold, 1f - a);

                float c = Mathf.Repeat(t, 3f);
                bool flying = c < 0.6f;
                spit.gameObject.SetActive(flying);
                spit.anchoredPosition = Vector2.Lerp(mouth, target, c / 0.6f);
                pea.Mouth.localScale = Vector3.one * (c < 0.12f ? 1.9f : 1f);
                float hit = c >= 0.6f ? Mathf.Clamp01((c - 0.6f) / 1.2f) : 1f;
                enemy.Face.color = Color.Lerp(Color.white, enemyColor, hit);
                plusThree.rectTransform.anchoredPosition = target + new Vector2(0f, 60f + hit * 40f);
                plusThree.color = WithAlpha(mine, c >= 0.6f ? 1f - hit : 0f);

                Light(sporelight, a < 0.35f, Gold, a < 0.12f);
                Light(knockout, c >= 0.6f && c < 1.4f, mine, c >= 0.6f && c < 0.8f);
                Light(win, true, Gold, false);
            };
        }

        // ---------- Control prompts ----------

        // A dark chip under the drawing: keycaps or icons plus a label. Lit = team colour, pressed = keys dip.
        private sealed class Prompt
        {
            public Image Back;
            public Text Label;
            public RectTransform Icon;
            public readonly System.Collections.Generic.List<(Image key, Text text, RectTransform rect, Vector2 rest)> Keys = new();
        }

        private const float PromptY = -180f;
        private static readonly Vector2 PromptSize = new(262f, 58f);

        private Prompt NewPrompt(RectTransform art, float x, string label)
        {
            var prompt = new Prompt { Back = Shape(art, new Vector2(x, PromptY), PromptSize, new Color(0f, 0f, 0f, 0.45f), UiBuild.Rounded) };
            prompt.Back.type = Image.Type.Sliced;
            prompt.Back.pixelsPerUnitMultiplier = 1.3f;
            prompt.Label = Label(prompt.Back.rectTransform, new Vector2(0f, -40f), new Vector2(PromptSize.x + 40f, 22f), 15, FontStyle.Normal);
            prompt.Label.font = UiBuild.DisplayFont;
            prompt.Label.text = label;
            return prompt;
        }

        private Prompt KeyPrompt(RectTransform art, float x, string label, params string[] keys)
        {
            Prompt prompt = NewPrompt(art, x, label);
            float[] widths = new float[keys.Length];
            float total = 0f;
            for (int i = 0; i < keys.Length; i++) total += widths[i] = keys[i].Length > 1 ? 120f : 36f;
            total += (keys.Length - 1) * 8f;
            float left = -total * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                var rest = new Vector2(left + widths[i] * 0.5f, 3f);
                left += widths[i] + 8f;
                Image key = Shape(prompt.Back.rectTransform, rest, new Vector2(widths[i], 36f), new Color(0.92f, 0.92f, 0.88f), UiBuild.Rounded);
                key.type = Image.Type.Sliced;
                key.pixelsPerUnitMultiplier = 2.2f;
                Shadow shadow = key.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
                shadow.effectDistance = new Vector2(0f, -4f);
                Text text = Label(key.rectTransform, Vector2.zero, new Vector2(widths[i], 36f), keys[i].Length > 1 ? 18 : 20, FontStyle.Normal);
                text.font = UiBuild.DisplayFont;
                text.text = keys[i];
                text.GetComponent<Shadow>().enabled = false;
                prompt.Keys.Add((key, text, key.rectTransform, rest));
            }
            return prompt;
        }

        // Icons laid out left to right with a small "or" between them; the first icon is Prompt.Icon.
        private Prompt IconPrompt(RectTransform art, float x, string label, params Func<RectTransform, RectTransform>[] icons)
        {
            Prompt prompt = NewPrompt(art, x, label);
            float spacing = 64f;
            float left = -(icons.Length - 1) * spacing * 0.5f;
            for (int i = 0; i < icons.Length; i++)
            {
                RectTransform holder = Node(prompt.Back.rectTransform, "Icon", new Vector2(left + i * spacing, 0f), Vector2.zero);
                RectTransform icon = icons[i](holder);
                if (i == 0) prompt.Icon = icon;
                if (i == 0) continue;
                Text or = Label(prompt.Back.rectTransform, new Vector2(left + (i - 0.5f) * spacing, 0f), new Vector2(40f, 20f), 13, FontStyle.Italic);
                or.text = "or";
                or.color = new Color(1f, 1f, 1f, 0.6f);
            }
            return prompt;
        }

        private static void Light(Prompt prompt, bool on, Color color, bool pressed)
        {
            prompt.Back.color = on ? new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.85f) : new Color(0f, 0f, 0f, 0.45f);
            prompt.Label.color = on ? Color.Lerp(color, Color.white, 0.35f) : new Color(1f, 1f, 1f, 0.55f);
            prompt.Back.rectTransform.localScale = Vector3.one * (on ? 1.04f : 1f);
            foreach ((Image key, Text text, RectTransform rect, Vector2 rest) in prompt.Keys)
            {
                key.color = on ? Color.Lerp(color, Color.white, 0.25f) : new Color(0.92f, 0.92f, 0.88f);
                text.color = Ink;
                rect.anchoredPosition = rest + (pressed ? new Vector2(0f, -3f) : Vector2.zero);
            }
        }

        // ---------- Icons (each draws into a holder centred on its slot, returns what animates) ----------

        private RectTransform Face(RectTransform parent)
        {
            RectTransform face = Shape(parent, Vector2.zero, new Vector2(38f, 42f), Skin).rectTransform;
            Shape(face, new Vector2(0f, 16f), new Vector2(40f, 16f), new Color(0.3f, 0.2f, 0.12f));
            return face;
        }

        private RectTransform MouthIcon(RectTransform parent)
        {
            RectTransform face = Face(parent);
            Shape(face, new Vector2(-8f, 3f), new Vector2(6f, 6f), Ink);
            Shape(face, new Vector2(8f, 3f), new Vector2(6f, 6f), Ink);
            Shape(face, new Vector2(0f, -10f), new Vector2(13f, 12f), new Color(0.45f, 0.08f, 0.08f));
            return face;
        }

        private RectTransform WinkIcon(RectTransform parent)
        {
            RectTransform face = Face(parent);
            Shape(face, new Vector2(-8f, 3f), new Vector2(6f, 6f), Ink);
            Shape(face, new Vector2(8f, 3f), new Vector2(9f, 2.5f), Ink, null);
            Shape(face, new Vector2(0f, -10f), new Vector2(12f, 3f), Ink, null);
            return face;
        }

        private RectTransform LeanIcon(RectTransform parent)
        {
            Text arrows = Label(parent, new Vector2(0f, 0f), new Vector2(90f, 30f), 22, FontStyle.Bold);
            arrows.text = "<       >";
            arrows.color = new Color(1f, 1f, 1f, 0.8f);
            RectTransform face = Face(parent);
            Shape(face, new Vector2(-8f, 3f), new Vector2(6f, 6f), Ink);
            Shape(face, new Vector2(8f, 3f), new Vector2(6f, 6f), Ink);
            Shape(face, new Vector2(0f, -10f), new Vector2(12f, 3f), Ink, null);
            return face;
        }

        private RectTransform SporelightIcon(RectTransform parent)
        {
            RectTransform root = Node(parent, "Sporelight", new Vector2(0f, -14f), Vector2.zero);
            root.localScale = Vector3.one * 0.45f;
            Image glow = Shape(root, new Vector2(0f, 50f), new Vector2(150f, 150f), WithAlpha(Gold, 0.45f), _glow);
            Shape(root, new Vector2(0f, 22f), new Vector2(20f, 44f), Cream, null);
            Shape(root, new Vector2(0f, 48f), new Vector2(78f, 46f), Gold);
            return glow.rectTransform;
        }

        private RectTransform PeaIcon(RectTransform parent, Color color)
        {
            RectTransform head = Shape(parent, Vector2.zero, new Vector2(34f, 34f), color).rectTransform;
            Shape(head, new Vector2(16f, -2f), new Vector2(16f, 14f), Shade(color, 0.85f));
            Shape(head, new Vector2(5f, 6f), new Vector2(10f, 10f), Color.white);
            Shape(head, new Vector2(7f, 6f), new Vector2(5f, 5f), Color.black);
            return head;
        }

        // Gold medal with the winning score on it.
        private RectTransform MedalIcon(RectTransform parent, int score)
        {
            Image medal = Shape(parent, Vector2.zero, new Vector2(40f, 40f), Gold);
            Shape(medal.rectTransform, Vector2.zero, new Vector2(30f, 30f), Shade(Gold, 0.8f));
            Text number = Label(medal.rectTransform, new Vector2(0f, 1f), new Vector2(40f, 30f), 18, FontStyle.Normal);
            number.font = UiBuild.DisplayFont;
            number.text = score.ToString();
            number.color = Ink;
            number.GetComponent<Shadow>().enabled = false;
            return medal.rectTransform;
        }

        // ---------- Drawing helpers ----------

        // Soil with a grassy rim and tufts (fixed layout so slides don't flicker between visits).
        private void Ground(RectTransform art)
        {
            Image soil = Shape(art, new Vector2(0f, GroundY - 14f), new Vector2(840f, 28f), Soil, UiBuild.Rounded);
            soil.type = Image.Type.Sliced;
            soil.pixelsPerUnitMultiplier = 1.5f;
            Image grass = Shape(art, new Vector2(0f, GroundY - 2f), new Vector2(836f, 7f), GrassDark, UiBuild.Rounded);
            grass.type = Image.Type.Sliced;
            grass.pixelsPerUnitMultiplier = 4f;
            for (int i = 0; i < 26; i++)
            {
                float x = -400f + i * 32f + Mathf.Sin(i * 12.9898f) * 9f;
                float size = 9f + 5f * Mathf.Abs(Mathf.Sin(i * 4.1f));
                Shape(art, new Vector2(x, GroundY + 1f + size * 0.2f), new Vector2(size * 1.3f, size), i % 3 == 0 ? GrassLight : GrassDark);
            }
        }

        // Side-view PeaHead facing right (or left). Root sits at the leaf base; use PlacePea to stand it on the ground.
        private Pea MakePea(RectTransform parent, Color color, bool faceLeft)
        {
            RectTransform root = Node(parent, "Pea", Vector2.zero, Vector2.zero);
            Shape(root, new Vector2(0f, 2f), new Vector2(120f, 18f), new Color(0f, 0f, 0f, 0.3f), _glow);
            Color dark = Shade(color, 0.65f);
            Shape(root, new Vector2(-26f, 10f), new Vector2(60f, 20f), dark);
            Shape(root, new Vector2(26f, 10f), new Vector2(60f, 20f), dark);
            Shape(root, new Vector2(0f, 58f), new Vector2(16f, 96f), dark, null);
            RectTransform head = Node(root, "Head", Vector2.zero, Vector2.zero);
            RectTransform face = Node(head, "Face", FaceOffset, Vector2.zero);
            Shape(face, new Vector2(50f, -6f), new Vector2(50f, 40f), Shade(color, 0.88f));
            Image skin = Shape(face, Vector2.zero, new Vector2(92f, 92f), color);
            RectTransform mouth = Shape(face, new Vector2(74f, -6f), new Vector2(18f, 18f), Ink).rectTransform;
            Image eye = Shape(face, new Vector2(14f, 16f), new Vector2(28f, 28f), Color.white);
            Image pupil = Shape(face, new Vector2(19f, 16f), new Vector2(13f, 13f), Color.black);
            var pea = new Pea { Root = root, Head = head, Mouth = mouth, Face = skin, Eye = eye, Pupil = pupil };
            PlacePea(pea, Vector2.zero, 1f, faceLeft);
            return pea;
        }

        // offset.y = 0 stands the pea on the ground; negative sinks its roots in.
        private static void PlacePea(Pea pea, Vector2 offset, float scale, bool faceLeft)
        {
            pea.Root.anchoredPosition = new Vector2(offset.x, GroundY + offset.y);
            pea.Root.localScale = new Vector3(faceLeft ? -scale : scale, scale, 1f);
        }

        // Mushroom standing on the ground at x. Returns its glow; cap is the coloured top.
        private Image Sporelight(RectTransform parent, float x, Color color, float scale, out Image cap)
        {
            RectTransform root = Node(parent, "Sporelight", new Vector2(x, GroundY), Vector2.zero);
            root.localScale = Vector3.one * scale;
            Image glow = Shape(root, new Vector2(0f, 50f), new Vector2(180f, 180f), WithAlpha(color, 0.5f), _glow);
            Shape(root, new Vector2(0f, 22f), new Vector2(20f, 44f), Cream, null);
            cap = Shape(root, new Vector2(0f, 48f), new Vector2(78f, 46f), color);
            Shape(root, new Vector2(-14f, 56f), new Vector2(11f, 11f), WithAlpha(Color.white, 0.85f));
            Shape(root, new Vector2(13f, 60f), new Vector2(8f, 8f), WithAlpha(Color.white, 0.85f));
            return glow;
        }

        private Image Shape(RectTransform parent, Vector2 pos, Vector2 size, Color color) =>
            Shape(parent, pos, size, color, _circle);

        private static Image Shape(RectTransform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite)
        {
            Image image = Node(parent, "Shape", pos, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Label(RectTransform parent, Vector2 pos, Vector2 size, int fontSize, FontStyle style)
        {
            Text text = Node(parent, "Label", pos, size).gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            Shadow shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private static RectTransform Node(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t) =>
            Vector2.Lerp(Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), t);

        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
        private static Color Shade(Color c, float k) => new(c.r * k, c.g * k, c.b * k, 1f);

        // ---------- Canvas ----------

        private void Build()
        {
            if (!_font) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _circle = MakeCircle(128, false);
            _glow = MakeCircle(128, true);

            _canvas = new GameObject("StoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)_canvas.transform;

            // Draws nothing: keeps a camera on the display while the story covers the screen and the player's camera
            // rests (PlayerCameraRig). On and off with the canvas.
            Camera blank = new GameObject("StoryScreenCamera", typeof(Camera)).GetComponent<Camera>();
            blank.transform.SetParent(root, false);
            blank.cullingMask = 0;
            blank.clearFlags = CameraClearFlags.SolidColor;
            blank.backgroundColor = Color.black;
            blank.depth = -50f;

            // The whole background is the "next" button.
            RectTransform backgroundRect = Node(root, "Background", Vector2.zero, Vector2.zero);
            Stretch(backgroundRect);
            _background = backgroundRect.gameObject.AddComponent<Image>();
            var advance = backgroundRect.gameObject.AddComponent<Button>();
            advance.transition = Selectable.Transition.None;
            advance.onClick.AddListener(Advance);

            _bokeh = new RectTransform[BokehCount];
            _bokehImages = new Image[BokehCount];
            for (int i = 0; i < BokehCount; i++)
            {
                float size = 260f + 90f * (i % 4);
                _bokehImages[i] = Shape(root, Vector2.zero, new Vector2(size, size), Color.clear, _glow);
                _bokeh[i] = _bokehImages[i].rectTransform;
            }

            _spores = new RectTransform[SporeCount];
            _sporeImages = new Image[SporeCount];
            _sporeSpeed = new float[SporeCount];
            for (int i = 0; i < SporeCount; i++)
            {
                var pos = new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f) * Reference.x * 1.4f,
                    UnityEngine.Random.Range(-0.5f, 0.5f) * Reference.y);
                float size = UnityEngine.Random.Range(6f, 16f);
                _sporeImages[i] = Shape(root, pos, new Vector2(size, size), Color.clear, _glow);
                _spores[i] = _sporeImages[i].rectTransform;
                _sporeSpeed[i] = UnityEngine.Random.Range(15f, 45f);
            }

            RectTransform content = Node(root, "Content", Vector2.zero, Vector2.zero);
            Stretch(content);
            _content = content.gameObject.AddComponent<CanvasGroup>();
            _contentRect = content;
            _content.blocksRaycasts = false;
            Image vignette = Shape(root, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f), UiBuild.Vignette);
            Stretch(vignette.rectTransform);
            vignette.transform.SetSiblingIndex(content.GetSiblingIndex());

            _title = Label(content, new Vector2(0f, 268f), new Vector2(1200f, 80f), 64, FontStyle.Normal);
            _title.font = UiBuild.DisplayFont;
            UiBuild.Outline(_title, new Color(0.05f, 0.04f, 0.03f, 0.85f), 2.5f);
            Image glow = Shape(content, new Vector2(0f, 60f), new Vector2(760f, 380f), new Color(1f, 1f, 1f, 0.06f), _glow);
            glow.raycastTarget = false;
            _art = Node(content, "Art", new Vector2(0f, 60f), new Vector2(820f, 300f));
            Image card = Shape(content, new Vector2(0f, -224f), new Vector2(1180f, 84f), new Color(0f, 0f, 0f, 0.38f), UiBuild.Rounded);
            card.type = Image.Type.Sliced;
            _body = Label(content, new Vector2(0f, -224f), new Vector2(1140f, 80f), 26, FontStyle.Bold);
            _body.alignment = TextAnchor.MiddleCenter;
            RectTransform hint = Node(content, "Hint", new Vector2(0f, -297f), new Vector2(520f, 36f));
            _hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.blocksRaycasts = false;
            HintKey(hint, -150f, "SPACE", 96f, "NEXT");
            HintKey(hint, 70f, "ESC", 58f, "SKIP");
            _dots = new Image[SlideCount];
            for (int i = 0; i < SlideCount; i++)
                _dots[i] = Shape(content, new Vector2((i - (SlideCount - 1) * 0.5f) * 34f, -334f), new Vector2(10f, 10f),
                    Color.white, UiBuild.Rounded);
            foreach (Image dot in _dots)
            {
                dot.type = Image.Type.Sliced;
                dot.pixelsPerUnitMultiplier = 3.2f;
            }

            _canvas.SetActive(false);
        }

        // A light keycap with its action written beside it, like the tutorial prompts.
        private void HintKey(RectTransform parent, float x, string key, float width, string action)
        {
            Image cap = Shape(parent, new Vector2(x, 0f), new Vector2(width, 30f), new Color(0.92f, 0.92f, 0.88f), UiBuild.Rounded);
            cap.type = Image.Type.Sliced;
            cap.pixelsPerUnitMultiplier = 2.4f;
            Shadow shadow = cap.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -3f);
            Text text = Label(cap.rectTransform, Vector2.zero, new Vector2(width, 30f), 17, FontStyle.Normal);
            text.font = UiBuild.DisplayFont;
            text.text = key;
            text.color = Ink;
            text.GetComponent<Shadow>().enabled = false;
            Text label = Label(parent, new Vector2(x + width * 0.5f + 44f, 0f), new Vector2(80f, 30f), 18, FontStyle.Normal);
            label.font = UiBuild.DisplayFont;
            label.text = action;
            label.alignment = TextAnchor.MiddleLeft;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Hard-edged disc (anti-aliased rim) or soft radial glow.
        private static Sprite MakeCircle(int size, bool soft)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = soft ? Mathf.Pow(Mathf.Clamp01(1f - d / r), 2f) : Mathf.Clamp01(r - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
