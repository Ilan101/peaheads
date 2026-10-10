using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeaHeads
{
    // Intro cutscene played when a session starts: typewriter story slides over small animated drawings, ending on
    // the local player's faction card. Built in code like GameMenu. Space/Enter/click advance, Esc skips to the end.
    public class StoryIntro : MonoBehaviour
    {
        private const float FadeTime = 0.35f;
        private const float GroundY = -120f;
        private const int SporeCount = 32;
        // Pea drawing (scale 1, facing right): face centre and mouth relative to the root at the leaf base.
        private static readonly Vector2 FaceOffset = new(0f, 108f);
        private static readonly Vector2 MouthOffset = new(74f, 102f);
        private static readonly Vector2 Reference = new(1280f, 720f);
        private static readonly Color Gold = new(1f, 0.84f, 0.42f);
        private static readonly Color Rot = new(0.5f, 0.32f, 0.55f);
        private static readonly Color Cream = new(0.95f, 0.9f, 0.8f);
        private static readonly Color Soil = new(0.24f, 0.16f, 0.1f);
        private static readonly Color Skin = new(0.96f, 0.8f, 0.65f);
        private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);

        [SerializeField] private Font _font;
        [SerializeField, Range(10f, 200f)] private float _charsPerSecond = 55f;

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
        }

        private Slide[] _slides;
        private Action _onDone;
        private int _index;
        private int _next = -1;
        private float _slideTime;
        private float _typed;
        private Action<float> _animate;

        private GameObject _canvas;
        private Image _background;
        private CanvasGroup _content;
        private Text _title;
        private Text _body;
        private Text _hint;
        private RectTransform _art;
        private GameObject _skipButton;
        private RectTransform[] _spores;
        private Image[] _sporeImages;
        private float[] _sporeSpeed;
        private Sprite _circle;
        private Sprite _glow;

        private int Last => _slides.Length - 1;

        private void OnDisable() => IsPlaying = false;

        // match may be null (scores then use the defaults from the spec).
        public void Play(int team, MatchManager match, Action onDone)
        {
            if (!_canvas) Build();
            _onDone = onDone;
            _slides = BuildSlides(team, match ? match.TargetScore : 20, match ? match.MushroomPoints : 1,
                match ? match.KillPoints : 3, match ? Mathf.RoundToInt(match.MatchDuration) : 120);
            _canvas.SetActive(true);
            _content.alpha = 0f;
            _background.color = _slides[0].Background;
            IsPlaying = true;
            ShowSlide(0);
        }

        public void Stop()
        {
            IsPlaying = false;
            _onDone = null;
            if (_canvas) _canvas.SetActive(false);
        }

        private void Update()
        {
            if (!IsPlaying) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            float dt = Time.unscaledDeltaTime;

            if (_next >= 0)
            {
                _content.alpha = Mathf.MoveTowards(_content.alpha, 0f, dt / FadeTime);
                if (_content.alpha <= 0f) ShowSlide(_next);
            }
            else _content.alpha = Mathf.MoveTowards(_content.alpha, 1f, dt / FadeTime);

            Slide slide = _slides[_index];
            float blend = 1f - Mathf.Exp(-3f * dt);
            _background.color = Color.Lerp(_background.color, slide.Background, blend);
            UpdateSpores(dt, slide.Spore, blend);

            _slideTime += dt;
            _typed = Mathf.Min(slide.Body.Length, _typed + dt * _charsPerSecond);
            _body.text = slide.Body.Substring(0, (int)_typed);
            _animate?.Invoke(_slideTime);

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                kb.numpadEnterKey.wasPressedThisFrame) Advance();
            else if (kb.escapeKey.wasPressedThisFrame) Skip();
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
            _skipButton.SetActive(index != Last);
            _hint.text = index == Last ? "Click or press Space to begin" : "Click / Space: next        Esc: skip";
        }

        private void Advance()
        {
            if (_next >= 0) return;
            if (_typed < _slides[_index].Body.Length) _typed = _slides[_index].Body.Length;
            else if (_index == Last) Finish();
            else _next = _index + 1;
        }

        private void Skip()
        {
            if (_index != Last) _next = Last;
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

        private Slide[] BuildSlides(int team, int target, int mushroomPoints, int killPoints, int seconds)
        {
            Color mine = Faction.Color(team);
            Color rival = Faction.Color(1 - team);
            return new[]
            {
                new Slide
                {
                    Title = "THE HOLLOW GROVE",
                    Body = "Deep in the forest grow the Sporelights: glowing mushrooms that hold the life of the whole " +
                           "Grove. For a thousand seasons, their light kept everything green.",
                    TitleColor = Gold, Background = new Color(0.05f, 0.12f, 0.14f), Spore = Gold, Draw = DrawGrove,
                },
                new Slide
                {
                    Title = "THE ROT",
                    Body = "Then the Rot crept up from the roots. One by one the Sporelights began to flicker... " +
                           "and the Grove began to die.",
                    TitleColor = new Color(0.78f, 0.55f, 0.88f), Background = new Color(0.11f, 0.06f, 0.12f),
                    Spore = Rot, Draw = DrawRot,
                },
                new Slide
                {
                    Title = "TWO SEEDS, ONE POD",
                    Body = "From a single pod fell two PeaHeads. One drank the last of the light and became BLOOM. " +
                           "The other drank the Rot and became BLIGHT. Only one can claim the Grove.",
                    TitleColor = Cream, Background = new Color(0.08f, 0.1f, 0.11f),
                    Spore = Color.Lerp(Faction.BloomColor, Faction.BlightColor, 0.5f), Draw = DrawSeeds,
                },
                new Slide
                {
                    Title = "PLANT TO FIGHT",
                    Body = "A PeaHead can walk or fight, never both. Press E to plant your roots: only a planted " +
                           "PeaHead can spit peas. Press E again to uproot and run.",
                    TitleColor = mine, Background = new Color(0.06f, 0.12f, 0.08f), Spore = mine,
                    Draw = art => DrawPlanting(art, mine),
                },
                new Slide
                {
                    Title = "A NIMBLE NECK",
                    Body = "Planted, your body can't move, but your neck can. Lean your REAL head to dodge: your " +
                           "PeaHead leans much further. Open your mouth to spit. Close one eye to zoom.\n" +
                           "No webcam? Arrow keys lean, left click spits, right click zooms.",
                    TitleColor = mine, Background = new Color(0.06f, 0.1f, 0.12f), Spore = mine,
                    Draw = art => DrawNeck(art, mine, rival),
                },
                new Slide
                {
                    Title = "CLAIM THE GROVE",
                    Body = $"Plant beside a glowing Sporelight to drink its essence: +{mushroomPoints}. Knock out your " +
                           $"rival with peas: +{killPoints}. First to {target} wins; if the {seconds}-second clock runs out, " +
                           "the most points decides the fate of the Grove.",
                    TitleColor = Gold, Background = new Color(0.07f, 0.1f, 0.13f), Spore = Gold,
                    Draw = art => DrawClaim(art, mine, rival, mushroomPoints, killPoints),
                },
                new Slide
                {
                    Title = $"YOU ARE {Faction.Name(team)}",
                    Body = Faction.Goal(team),
                    TitleColor = mine, Background = Shade(mine, 0.16f), Spore = mine,
                    Draw = art => DrawRole(art, mine, team == 1),
                },
            };
        }

        private Action<float> DrawGrove(RectTransform art)
        {
            Ground(art);
            float[] xs = { -230f, 0f, 230f };
            float[] scales = { 1f, 1.35f, 1.1f };
            var glows = new Image[xs.Length];
            for (int i = 0; i < xs.Length; i++) glows[i] = Sporelight(art, xs[i], Gold, scales[i], out _);
            return t =>
            {
                for (int i = 0; i < glows.Length; i++)
                    glows[i].color = WithAlpha(Gold, 0.4f + 0.2f * Mathf.Sin(t * 2f + i * 2.1f));
            };
        }

        private Action<float> DrawRot(RectTransform art)
        {
            Ground(art);
            var blobs = new RectTransform[7];
            for (int i = 0; i < blobs.Length; i++)
            {
                blobs[i] = Shape(art, new Vector2(-330f + i * 110f, GroundY), new Vector2(130f, 46f),
                    Shade(Rot, 0.7f)).rectTransform;
                blobs[i].localScale = Vector3.zero;
            }
            float[] xs = { -230f, 0f, 230f };
            float[] scales = { 1f, 1.35f, 1.1f };
            var glows = new Image[xs.Length];
            var caps = new Image[xs.Length];
            for (int i = 0; i < xs.Length; i++) glows[i] = Sporelight(art, xs[i], Gold, scales[i], out caps[i]);
            return t =>
            {
                float k = Mathf.Clamp01(t / 4f);
                for (int i = 0; i < blobs.Length; i++)
                    blobs[i].localScale = Vector3.one * Smooth(Mathf.Clamp01(k * 1.6f - Mathf.Abs(i - 3) * 0.15f));
                for (int i = 0; i < caps.Length; i++)
                {
                    Color c = Color.Lerp(Gold, Rot, Mathf.Clamp01(k * 1.5f - i * 0.15f));
                    caps[i].color = c;
                    float flicker = Mathf.PerlinNoise(t * 7f, i * 3.1f);
                    glows[i].color = WithAlpha(c, Mathf.Lerp(0.5f, 0.08f, k) * flicker);
                }
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
            Text caption = Label(art, new Vector2(0f, 120f), new Vector2(600f, 34f), 24, FontStyle.Bold);
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

                caption.text = p < 1.6f ? "WASD: walk" : p < 2.7f ? "E: plant your roots" : "Planted: spit peas!";
                caption.color = Color.Lerp(Color.white, color, 0.4f);
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
            };
        }

        private Action<float> DrawClaim(RectTransform art, Color mine, Color rival, int mushroomPoints, int killPoints)
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
            };
        }

        private Action<float> DrawRole(RectTransform art, Color color, bool faceLeft)
        {
            Ground(art);
            Image glow = Shape(art, new Vector2(0f, -10f), new Vector2(360f, 360f), WithAlpha(color, 0.4f), _glow);
            Pea pea = MakePea(art, color, faceLeft);
            PlacePea(pea, Vector2.zero, 1.3f, faceLeft);
            return t =>
            {
                glow.color = WithAlpha(color, 0.35f + 0.15f * Mathf.Sin(t * 2.5f));
                pea.Head.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 3f) * 6f);
            };
        }

        // ---------- Drawing helpers ----------

        private void Ground(RectTransform art) =>
            Shape(art, new Vector2(0f, GroundY - 10f), new Vector2(820f, 20f), Soil, null);

        // Side-view PeaHead facing right (or left). Root sits at the leaf base; use PlacePea to stand it on the ground.
        private Pea MakePea(RectTransform parent, Color color, bool faceLeft)
        {
            RectTransform root = Node(parent, "Pea", Vector2.zero, Vector2.zero);
            Color dark = Shade(color, 0.65f);
            Shape(root, new Vector2(-26f, 10f), new Vector2(60f, 20f), dark);
            Shape(root, new Vector2(26f, 10f), new Vector2(60f, 20f), dark);
            Shape(root, new Vector2(0f, 58f), new Vector2(16f, 96f), dark, null);
            RectTransform head = Node(root, "Head", Vector2.zero, Vector2.zero);
            RectTransform face = Node(head, "Face", FaceOffset, Vector2.zero);
            Shape(face, new Vector2(50f, -6f), new Vector2(50f, 40f), Shade(color, 0.88f));
            Image skin = Shape(face, Vector2.zero, new Vector2(92f, 92f), color);
            RectTransform mouth = Shape(face, new Vector2(74f, -6f), new Vector2(18f, 18f), Ink).rectTransform;
            Shape(face, new Vector2(14f, 16f), new Vector2(28f, 28f), Color.white);
            Shape(face, new Vector2(19f, 16f), new Vector2(13f, 13f), Color.black);
            var pea = new Pea { Root = root, Head = head, Mouth = mouth, Face = skin };
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

            // The whole background is the "next" button; Skip sits above it and wins the raycast.
            RectTransform backgroundRect = Node(root, "Background", Vector2.zero, Vector2.zero);
            Stretch(backgroundRect);
            _background = backgroundRect.gameObject.AddComponent<Image>();
            var advance = backgroundRect.gameObject.AddComponent<Button>();
            advance.transition = Selectable.Transition.None;
            advance.onClick.AddListener(Advance);

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
            _content.blocksRaycasts = false;
            _title = Label(content, new Vector2(0f, 270f), new Vector2(1100f, 70f), 48, FontStyle.Bold);
            _art = Node(content, "Art", new Vector2(0f, 60f), new Vector2(820f, 300f));
            _body = Label(content, new Vector2(0f, -180f), new Vector2(1000f, 150f), 26, FontStyle.Normal);
            _body.alignment = TextAnchor.UpperCenter;
            _hint = Label(content, new Vector2(0f, -320f), new Vector2(900f, 30f), 18, FontStyle.Normal);
            _hint.color = new Color(1f, 1f, 1f, 0.6f);

            RectTransform skip = Node(root, "Skip", new Vector2(-24f, -20f), new Vector2(140f, 44f));
            skip.anchorMin = skip.anchorMax = skip.pivot = Vector2.one;
            Image skipImage = skip.gameObject.AddComponent<Image>();
            skipImage.color = new Color(0f, 0f, 0f, 0.45f);
            var skipButton = skip.gameObject.AddComponent<Button>();
            skipButton.targetGraphic = skipImage;
            skipButton.onClick.AddListener(Skip);
            Text skipLabel = Label(skip, Vector2.zero, Vector2.zero, 20, FontStyle.Bold);
            Stretch(skipLabel.rectTransform);
            skipLabel.text = "Skip  >>";
            _skipButton = skip.gameObject;

            _canvas.SetActive(false);
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
