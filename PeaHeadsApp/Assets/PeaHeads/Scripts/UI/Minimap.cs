using System.Collections.Generic;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeaHeads
{
    // Top-right map of the whole play area, own side at the bottom: a top-down image baked from the level's colliders
    // plus live icons for you (arrow + view cone), your rival, dummies and Sporelights. Local only, reads state that
    // is already synced (transforms, PlayerHealth, MushroomSpot visibility). Built in code; GameMenu adds it.
    public class Minimap : MonoBehaviour
    {
        private const int TextureSize = 160;
        private const float Padding = 8f;
        private const float LegendHeight = 26f;
        private const float RayTop = 60f;
        private static readonly Color MushroomCap = new(1f, 0.3f, 0.22f);
        private static readonly Color DummyColor = new(0.75f, 0.75f, 0.75f);
        private static readonly Color GrassDark = new(0.27f, 0.45f, 0.23f);
        private static readonly Color GrassLight = new(0.35f, 0.55f, 0.28f);
        private static readonly Color StoneDark = new(0.45f, 0.41f, 0.36f);
        private static readonly Color StoneLight = new(0.76f, 0.72f, 0.64f);
        private static readonly Color Outline = new(0.14f, 0.12f, 0.09f);
        private static readonly Color Void = new(0.09f, 0.11f, 0.09f);

        [SerializeField] private Font _font;
        [Tooltip("Half the play area's side in metres (ArenaBuilder PlayHalf + wall).")]
        [SerializeField] private float _worldHalfSize = 20.5f;
        [SerializeField] private Vector2 _worldCentre = Vector2.zero;
        [SerializeField, Range(120f, 360f)] private float _mapSize = 210f;
        [SerializeField, Range(1f, 2.5f)] private float _expandedScale = 1.7f;
        [SerializeField] private Key _toggleKey = Key.M;

        private sealed class PlayerIcon
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public RectTransform Heading;
            public Image Body;
            public Image Rim;
            public Image Arrow;
            public Image Cone;
            public Image Ping;
        }

        private sealed class MushroomIcon
        {
            public MushroomSpot Spot;
            public RectTransform Root;
            public Image Dot;
            public Image Glow;
            public Image Shadow;
            public Image Icon;
            public Image Ping;
            public bool WasShown;
            public float ShownAt = float.NegativeInfinity;
        }

        private readonly Dictionary<PlayerHealth, PlayerIcon> _players = new();
        private readonly List<PlayerHealth> _stale = new();
        private readonly List<MushroomIcon> _mushrooms = new();
        private float _nextScan;
        private int _bakedTeam = -1;
        private float _rotation;
        private bool _expanded;
        private float _scale = 1f;

        private GameObject _canvas;
        private RectTransform _root;
        private RectTransform _map;
        private RectTransform _mushroomLayer;
        private RectTransform _playerLayer;
        private RawImage _terrain;
        private Texture2D _texture;
        private UnityEngine.UI.Outline _frameOutline;
        private Image _legendYou;
        private Image _legendRival;
        private Text _distance;
        private Sprite _circle, _ring, _glow, _arrow, _cone, _mushroom, _rounded;

        private void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject local = nm ? nm.LocalClient?.PlayerObject : null;
            if (!local)
            {
                if (_canvas && _canvas.activeSelf) _canvas.SetActive(false);
                return;
            }
            if (!_canvas) Build();
            // Baked as soon as the local plant exists, so the one-off hitch lands during the story.
            int team = MatchManager.TeamOf(nm.LocalClientId);
            if (team != _bakedTeam) Bake(team);

            bool show = !StoryIntro.IsPlaying;
            if (_canvas.activeSelf != show) _canvas.SetActive(show);
            if (!show) return;

            Keyboard kb = Keyboard.current;
            bool paused = local.TryGetComponent(out PlayerInput input) && input.Paused;
            if (kb != null && !paused && kb[_toggleKey].wasPressedThisFrame) _expanded = !_expanded;
            _scale = Mathf.Lerp(_scale, _expanded ? _expandedScale : 1f, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            _root.localScale = Vector3.one * _scale;

            UpdatePlayers();
            UpdateMushrooms(local.transform.position);
        }

        private void OnDestroy()
        {
            if (_texture) Destroy(_texture);
        }

        // ---------- Live icons ----------

        private void UpdatePlayers()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                    if (!_players.ContainsKey(health)) _players.Add(health, CreatePlayerIcon());
            }

            _stale.Clear();
            float time = Time.unscaledTime;
            foreach (KeyValuePair<PlayerHealth, PlayerIcon> pair in _players)
            {
                PlayerHealth health = pair.Key;
                PlayerIcon icon = pair.Value;
                if (!health)
                {
                    _stale.Add(health);
                    Destroy(icon.Root.gameObject);
                    continue;
                }

                bool isPlayer = health.TryGetComponent(out PlayerNetSync sync);
                bool isLocal = isPlayer && sync.IsOwner;
                Color color = isPlayer ? Faction.Color(MatchManager.TeamOf(health.OwnerClientId)) : DummyColor;

                icon.Root.anchoredPosition = WorldToMap(health.transform.position);
                icon.Heading.localRotation = Quaternion.Euler(0f, 0f, -health.transform.eulerAngles.y);
                icon.Root.gameObject.SetActive(isLocal || !health.IsDead);
                icon.Group.alpha = health.IsDead ? 0.35f : health.IsShielded && !isLocal ? 0.45f : 1f;

                icon.Cone.enabled = isLocal;
                icon.Body.enabled = !isLocal;
                icon.Rim.enabled = !isLocal;
                icon.Arrow.enabled = isPlayer;
                icon.Ping.enabled = isPlayer && !isLocal;

                icon.Arrow.color = color;
                icon.Body.color = color;
                icon.Arrow.rectTransform.sizeDelta = isLocal ? new Vector2(20f, 20f) : new Vector2(9f, 9f);
                icon.Arrow.rectTransform.anchoredPosition = isLocal ? Vector2.zero : new Vector2(0f, 11f);
                icon.Body.rectTransform.sizeDelta = icon.Rim.rectTransform.sizeDelta =
                    isPlayer ? new Vector2(14f, 14f) : new Vector2(9f, 9f);
                if (isLocal) icon.Root.SetAsLastSibling();

                if (!icon.Ping.enabled) continue;
                float t = Mathf.Repeat(time, 1.6f) / 1.6f;
                icon.Ping.rectTransform.localScale = Vector3.one * (0.6f + 2.4f * t);
                icon.Ping.color = WithAlpha(color, (1f - t) * 0.8f);
            }
            foreach (PlayerHealth gone in _stale) _players.Remove(gone);
        }

        private void UpdateMushrooms(Vector3 localPosition)
        {
            float time = Time.unscaledTime;
            float nearest = float.PositiveInfinity;
            MushroomIcon nearestIcon = null;
            foreach (MushroomIcon m in _mushrooms)
            {
                if (!m.Spot) continue;
                bool shown = m.Spot.IsShown;
                if (shown && !m.WasShown) m.ShownAt = time;
                m.WasShown = shown;

                m.Dot.enabled = !shown;
                m.Glow.enabled = m.Shadow.enabled = m.Icon.enabled = shown;
                float since = time - m.ShownAt;
                m.Ping.enabled = shown && since < 1.2f;
                if (!shown) continue;

                Vector3 d = m.Spot.transform.position - localPosition;
                float distance = new Vector2(d.x, d.z).magnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestIcon = m;
                }

                float pop = 1f + 0.8f * Mathf.Exp(-since * 6f);
                m.Icon.rectTransform.localScale = m.Shadow.rectTransform.localScale = Vector3.one * pop;
                m.Glow.color = WithAlpha(MushroomCap, 0.35f + 0.2f * Mathf.Sin(time * 4f + m.Spot.Index));
                m.Glow.rectTransform.localScale = Vector3.one;
                if (m.Ping.enabled)
                {
                    float t = since / 1.2f;
                    m.Ping.rectTransform.localScale = Vector3.one * (0.5f + 2.8f * t);
                    m.Ping.color = WithAlpha(MushroomCap, 1f - t);
                }
            }

            if (nearestIcon != null) nearestIcon.Glow.rectTransform.localScale = Vector3.one * 1.35f;
            _distance.text = nearestIcon != null ? $"Sporelight {nearest:0} m" : "No Sporelights";
        }

        private PlayerIcon CreatePlayerIcon()
        {
            var icon = new PlayerIcon { Root = Node(_playerLayer, "Player", Vector2.zero, Vector2.zero) };
            icon.Group = icon.Root.gameObject.AddComponent<CanvasGroup>();
            icon.Group.blocksRaycasts = false;
            icon.Ping = AddImage(icon.Root, _ring, Vector2.zero, new Vector2(16f, 16f), Color.clear);
            icon.Heading = Node(icon.Root, "Heading", Vector2.zero, Vector2.zero);
            icon.Cone = AddImage(icon.Heading, _cone, Vector2.zero, new Vector2(110f, 110f), new Color(1f, 1f, 0.9f, 0.4f));
            icon.Rim = AddImage(icon.Root, _circle, Vector2.zero, new Vector2(14f, 14f), Color.white);
            icon.Body = AddImage(icon.Root, _circle, Vector2.zero, new Vector2(14f, 14f), Color.white);
            icon.Rim.rectTransform.localScale = Vector3.one * 1.3f;
            icon.Arrow = AddImage(icon.Heading, _arrow, Vector2.zero, new Vector2(20f, 20f), Color.white);
            icon.Arrow.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return icon;
        }

        private MushroomIcon CreateMushroomIcon(MushroomSpot spot)
        {
            var m = new MushroomIcon { Spot = spot };
            m.Root = Node(_mushroomLayer, "Sporelight", WorldToMap(spot.transform.position), Vector2.zero);
            m.Root.localRotation = Quaternion.Euler(0f, 0f, -_rotation); // stays upright on a rotated map
            m.Dot = AddImage(m.Root, _circle, Vector2.zero, new Vector2(5f, 5f), new Color(1f, 0.85f, 0.75f, 0.35f));
            m.Glow = AddImage(m.Root, _glow, Vector2.zero, new Vector2(38f, 38f), MushroomCap);
            m.Ping = AddImage(m.Root, _ring, Vector2.zero, new Vector2(16f, 16f), MushroomCap);
            m.Shadow = AddImage(m.Root, _mushroom, new Vector2(1f, -1.5f), new Vector2(19f, 19f), new Color(0f, 0f, 0f, 0.6f));
            m.Icon = AddImage(m.Root, _mushroom, Vector2.zero, new Vector2(16f, 16f), MushroomCap);
            return m;
        }

        private Vector2 WorldToMap(Vector3 world) =>
            new Vector2(world.x - _worldCentre.x, world.z - _worldCentre.y) * (_mapSize * 0.5f / _worldHalfSize);

        // ---------- Bake ----------

        // Orientation, faction colours, Sporelight icons and the top-down image. Re-run if the local team changes.
        private void Bake(int team)
        {
            _bakedTeam = team;
            bool hasOwn = SpawnPoint.TryGetTeamCentre(team, out Vector3 own);
            bool hasRival = SpawnPoint.TryGetTeamCentre(1 - team, out Vector3 rival);
            Vector2 ownXZ = new Vector2(own.x, own.z) - _worldCentre;
            _rotation = 0f;
            if (hasOwn && ownXZ.sqrMagnitude > 0.01f)
            {
                float angle = -90f - Mathf.Atan2(ownXZ.y, ownXZ.x) * Mathf.Rad2Deg;
                _rotation = Mathf.Round(angle / 90f) * 90f;
            }
            _map.localRotation = Quaternion.Euler(0f, 0f, _rotation);

            _frameOutline.effectColor = WithAlpha(Faction.Color(team), 0.55f);
            _legendYou.color = Faction.Color(team);
            _legendRival.color = Faction.Color(1 - team);

            foreach (MushroomIcon m in _mushrooms) Destroy(m.Root.gameObject);
            _mushrooms.Clear();
            foreach (MushroomSpot spot in FindObjectsByType<MushroomSpot>(FindObjectsSortMode.None))
                _mushrooms.Add(CreateMushroomIcon(spot));

            Vector2 rivalXZ = new Vector2(rival.x, rival.z) - _worldCentre;
            BakeTexture(team, hasOwn && hasRival, ownXZ, rivalXZ);
        }

        private void BakeTexture(int team, bool tint, Vector2 own, Vector2 rival)
        {
            const int n = TextureSize;
            float cell = _worldHalfSize * 2f / n;
            var kind = new byte[n * n]; // 0 nothing, 1 ground, 2 cover
            var height = new float[n * n];
            var hits = new RaycastHit[16];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var origin = new Vector3(_worldCentre.x - _worldHalfSize + (x + 0.5f) * cell, RayTop,
                    _worldCentre.y - _worldHalfSize + (y + 0.5f) * cell);
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, RayTop * 2f, ~0,
                    QueryTriggerInteraction.Ignore);
                float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = hits[i];
                    if (hit.distance >= best || IsDynamic(hit.collider)) continue;
                    best = hit.distance;
                    kind[y * n + x] = hit.collider is TerrainCollider ? (byte)1 : (byte)2;
                    height[y * n + x] = hit.point.y;
                }
            }

            float ground = float.PositiveInfinity;
            for (int i = 0; i < kind.Length; i++)
                if (kind[i] == 1) ground = Mathf.Min(ground, height[i]);
            if (float.IsInfinity(ground)) ground = 0f;

            Vector2 axis = own - rival;
            Vector2 mid = (own + rival) * 0.5f;
            float halfLength = axis.magnitude * 0.5f;
            if (halfLength > 0.01f) axis /= halfLength * 2f;
            Color ownColor = Faction.Color(team), rivalColor = Faction.Color(1 - team);

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                var world = new Vector2(-_worldHalfSize + (x + 0.5f) * cell, -_worldHalfSize + (y + 0.5f) * cell);
                Color c;
                if (kind[i] == 2)
                {
                    c = Color.Lerp(StoneDark, StoneLight, Mathf.Clamp01((height[i] - ground) / 3f));
                    if (Neighbour(kind, x - 1, y) != 2 || Neighbour(kind, x + 1, y) != 2 ||
                        Neighbour(kind, x, y - 1) != 2 || Neighbour(kind, x, y + 1) != 2) c = Outline;
                }
                else if (kind[i] == 1)
                {
                    c = Color.Lerp(GrassDark, GrassLight, Mathf.PerlinNoise(x * 0.07f, y * 0.07f));
                    if (tint && halfLength > 0.01f)
                    {
                        float side = Vector2.Dot(world - mid, axis) / halfLength; // -1 rival end .. +1 own end
                        c = Color.Lerp(c, side > 0f ? ownColor : rivalColor, 0.16f * Mathf.SmoothStep(0f, 1f, Mathf.Abs(side)));
                    }
                    if (Neighbour(kind, x - 2, y + 2) == 2 || Neighbour(kind, x - 1, y + 1) == 2) c *= 0.68f; // drop shadow
                    if (Mathf.Repeat(world.x, 10f) < cell || Mathf.Repeat(world.y, 10f) < cell) c *= 0.9f; // 10 m grid
                }
                else c = Void;
                c.a = 1f;
                pixels[i] = c;
            }

            if (!_texture)
                _texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _texture.SetPixels32(pixels);
            _texture.Apply();
            _terrain.texture = _texture;
        }

        // Players, dummies, peas and pickups move or vanish: keep them out of the baked image.
        private static bool IsDynamic(Collider collider) =>
            collider.GetComponentInParent<NetworkObject>() || collider.GetComponentInParent<MushroomSpot>();

        private static byte Neighbour(byte[] kind, int x, int y) =>
            x < 0 || y < 0 || x >= TextureSize || y >= TextureSize ? (byte)1 : kind[y * TextureSize + x];

        // ---------- Canvas ----------

        private void Build()
        {
            if (!_font) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _circle = UiSprites.Circle();
            _ring = UiSprites.Ring();
            _glow = UiSprites.Glow();
            _arrow = UiSprites.Arrow();
            _cone = UiSprites.Cone();
            _mushroom = UiSprites.Mushroom();
            _rounded = UiSprites.RoundedRect();

            _canvas = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler));
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            float width = _mapSize + Padding * 2f;
            float panelHeight = width + LegendHeight;
            _root = Node(_canvas.transform, "Minimap", new Vector2(-16f, -16f), new Vector2(width, panelHeight));
            _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.one;

            Image frame = AddImage(_root, _rounded, Vector2.zero, Vector2.zero, new Color(0.04f, 0.07f, 0.05f, 0.82f));
            Stretch(frame.rectTransform);
            frame.type = Image.Type.Sliced;
            _frameOutline = frame.gameObject.AddComponent<UnityEngine.UI.Outline>();
            _frameOutline.effectDistance = new Vector2(1.5f, -1.5f);

            float mapY = panelHeight * 0.5f - Padding - _mapSize * 0.5f;
            Image viewport = AddImage(_root, _rounded, new Vector2(0f, mapY), new Vector2(_mapSize, _mapSize), Color.white);
            viewport.type = Image.Type.Sliced;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            viewport.color = Void;

            _map = Node(viewport.rectTransform, "Map", Vector2.zero, new Vector2(_mapSize, _mapSize));
            _terrain = Node(_map, "Terrain", Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            Stretch(_terrain.rectTransform);
            _terrain.raycastTarget = false;
            _mushroomLayer = Node(_map, "Sporelights", Vector2.zero, Vector2.zero);
            _playerLayer = Node(_map, "Players", Vector2.zero, Vector2.zero);

            float legendY = -panelHeight * 0.5f + LegendHeight * 0.5f + 2f;
            float left = -width * 0.5f + Padding + 6f;
            _legendYou = AddImage(_root, _arrow, new Vector2(left, legendY), new Vector2(12f, 12f), Color.white);
            AddLabel(_root, "You", new Vector2(left + 8f, legendY));
            _legendRival = AddImage(_root, _circle, new Vector2(left + 44f, legendY), new Vector2(10f, 10f), Color.white);
            AddLabel(_root, "Rival", new Vector2(left + 52f, legendY));
            AddImage(_root, _mushroom, new Vector2(left + 96f, legendY), new Vector2(13f, 13f), MushroomCap);
            _distance = AddLabel(_root, "", new Vector2(left + 105f, legendY));

            _canvas.SetActive(false);
        }

        private Text AddLabel(RectTransform parent, string value, Vector2 leftEdge)
        {
            RectTransform rect = Node(parent, "Label", leftEdge, new Vector2(140f, 20f));
            rect.pivot = new Vector2(0f, 0.5f);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = 13;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 1f, 1f, 0.9f);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static Image AddImage(RectTransform parent, Sprite sprite, Vector2 pos, Vector2 size, Color color)
        {
            Image image = Node(parent, "Image", pos, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
