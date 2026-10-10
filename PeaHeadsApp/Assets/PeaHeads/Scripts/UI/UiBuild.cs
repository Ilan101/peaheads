using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Helpers for the code-built HUDs: canvas, rounded "glass" panels, labels, and shared procedural sprites.
    // Positions are in 1280×720 reference units (canvases scale with the screen).
    public static class UiBuild
    {
        public static readonly Color Glass = new(0.04f, 0.07f, 0.05f, 0.78f);

        private static Font _font;
        private static Sprite _rounded, _circle, _ring, _glow, _heart, _cross, _vignette, _mushroom, _flower, _thorn, _sprout, _scope, _shield;

        public static Font Font => _font ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Sprite Rounded => _rounded ? _rounded : _rounded = UiSprites.RoundedRect();
        public static Sprite Circle => _circle ? _circle : _circle = UiSprites.Circle();
        public static Sprite Ring => _ring ? _ring : _ring = UiSprites.Ring(96, 0.12f);
        public static Sprite Glow => _glow ? _glow : _glow = UiSprites.Glow();
        public static Sprite Heart => _heart ? _heart : _heart = UiSprites.Heart();
        public static Sprite Cross => _cross ? _cross : _cross = UiSprites.Cross();
        public static Sprite Vignette => _vignette ? _vignette : _vignette = UiSprites.Vignette();
        public static Sprite Mushroom => _mushroom ? _mushroom : _mushroom = UiSprites.Mushroom();
        public static Sprite Flower => _flower ? _flower : _flower = UiSprites.Flower();
        public static Sprite Thorn => _thorn ? _thorn : _thorn = UiSprites.Thorn();
        public static Sprite Sprout => _sprout ? _sprout : _sprout = UiSprites.Sprout();
        public static Sprite Scope => _scope ? _scope : _scope = UiSprites.Scope();
        public static Sprite Shield => _shield ? _shield : _shield = UiSprites.Shield();

        // Overlay canvas with no raycaster: HUDs never take clicks.
        public static RectTransform Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        // anchor doubles as pivot: (0,1) = top-left corner, (0.5,0.5) = centre, ...
        public static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Node(Transform parent, string name, Vector2 pos, Vector2 size) =>
            Node(parent, name, new Vector2(0.5f, 0.5f), pos, size);

        public static Image Image(Transform parent, Sprite sprite, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            Image image = Node(parent, sprite ? sprite.name : "Image", anchor, pos, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image Image(Transform parent, Sprite sprite, Vector2 pos, Vector2 size, Color color) =>
            Image(parent, sprite, new Vector2(0.5f, 0.5f), pos, size, color);

        // Rounded panel; radius scales down with pixelsPerUnit (2 = half the 16-unit corner).
        public static Image Panel(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, float radiusScale = 1f)
        {
            Image image = Image(parent, Rounded, anchor, pos, size, color);
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / radiusScale;
            return image;
        }

        public static Text Label(Transform parent, string value, int fontSize, Color color, TextAnchor alignment,
            Vector2 anchor, Vector2 pos, Vector2 size, FontStyle style = FontStyle.Bold, bool shadow = true)
        {
            Text text = Node(parent, "Label", anchor, pos, size).gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            if (shadow)
            {
                Shadow s = text.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.7f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return text;
        }

        public static Outline Outline(Graphic graphic, Color color, float distance = 1.5f)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            return outline;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        // Scale for a "pop": 1 + amount right after the event, easing back to 1 over duration (1 outside it).
        public static float Pop(float elapsed, float duration, float amount) =>
            elapsed < 0f || elapsed > duration ? 1f : 1f + amount * (1f - elapsed / duration) * (1f - elapsed / duration);
    }
}
