using System;
using UnityEngine;

namespace PeaHeads
{
    // Procedural white sprites for code-built UI (tint with Image.color). Shapes are coverage functions over
    // p in [-1, 1]² (y up), supersampled 4×4 for smooth edges.
    public static class UiSprites
    {
        private const int Samples = 4;

        public static Sprite Circle(int size = 64) => Make(size, p => p.magnitude <= 1f ? 1f : 0f);

        public static Sprite Ring(int size = 64, float thickness = 0.14f) =>
            Make(size, p => Mathf.Abs(p.magnitude - (1f - thickness)) <= thickness ? 1f : 0f);

        public static Sprite Glow(int size = 64) =>
            Make(size, p => Mathf.Pow(Mathf.Clamp01(1f - p.magnitude), 2f), 1);

        // Chevron pointing up (+y).
        public static Sprite Arrow(int size = 64)
        {
            Vector2 tip = new(0f, 0.95f), left = new(-0.75f, -0.8f), right = new(0.75f, -0.8f), notch = new(0f, -0.35f);
            return Make(size, p => InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right) ? 1f : 0f);
        }

        // Soft view wedge from the centre, opening upwards, fading with distance.
        public static Sprite Cone(int size = 128, float degrees = 70f)
        {
            float half = degrees * 0.5f;
            return Make(size, p =>
            {
                float r = p.magnitude;
                if (r > 1f || r < 0.001f) return 0f;
                float angle = Mathf.Abs(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg);
                float edge = Mathf.Clamp01((half - angle) / 6f);
                return edge * (1f - r);
            }, 1);
        }

        // Toadstool: dome cap with spot holes on a short stem.
        public static Sprite Mushroom(int size = 64) => Make(size, p =>
        {
            bool cap = p.y >= -0.05f && (p.x * p.x) / 0.81f + ((p.y + 0.05f) * (p.y + 0.05f)) / 0.64f <= 1f;
            bool stem = Mathf.Abs(p.x) <= 0.3f && p.y >= -0.85f && p.y < -0.05f;
            bool spot = (p - new Vector2(-0.38f, 0.22f)).magnitude < 0.14f ||
                        (p - new Vector2(0.3f, 0.35f)).magnitude < 0.12f ||
                        (p - new Vector2(-0.02f, 0.5f)).magnitude < 0.1f;
            return (cap && !spot) || stem ? 1f : 0f;
        });

        // Filled heart from (x² + y² − 1)³ − x²y³ ≤ 0.
        public static Sprite Heart(int size = 64) => Make(size, p =>
        {
            float x = p.x * 1.3f, y = p.y * 1.35f + 0.05f;
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f ? 1f : 0f;
        });

        // Diagonal X with an open centre (hit marker).
        public static Sprite Cross(int size = 64) => Make(size, p =>
            Mathf.Abs(Mathf.Abs(p.x) - Mathf.Abs(p.y)) < 0.16f &&
            Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) is > 0.42f and <= 1f ? 1f : 0f);

        // Screen-edge vignette: clear centre, dense corners. Stretch it over the screen.
        public static Sprite Vignette(int size = 128) => Make(size, p =>
        {
            float r = Mathf.Max(0f, p.magnitude - 0.5f) / 0.9f;
            return Mathf.Clamp01(r * r * 1.8f);
        }, 1);

        // Bloom badge: five round petals around a centre.
        public static Sprite Flower(int size = 64) => Make(size, p =>
        {
            if (p.magnitude <= 0.32f) return 1f;
            for (int k = 0; k < 5; k++)
            {
                float a = (90f + 72f * k) * Mathf.Deg2Rad;
                if ((p - new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.55f).magnitude <= 0.36f) return 1f;
            }
            return 0f;
        });

        // Blight badge: eight sharp spikes around a core.
        public static Sprite Thorn(int size = 64) => Make(size, p =>
        {
            float spike = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(4f * Mathf.Atan2(p.y, p.x))), 4f);
            return p.magnitude <= 0.35f + 0.6f * spike ? 1f : 0f;
        });

        // Plant-state icon: stem with two leaves opening upwards.
        public static Sprite Sprout(int size = 64) => Make(size, p =>
        {
            bool stem = Mathf.Abs(p.x) <= 0.09f && p.y >= -0.85f && p.y <= 0.15f;
            return stem || InLeaf(p, new Vector2(-0.38f, 0.32f), new Vector2(-0.866f, 0.5f)) ||
                   InLeaf(p, new Vector2(0.38f, 0.32f), new Vector2(0.866f, 0.5f)) ? 1f : 0f;
        });

        // Webcam-status icon: camera body with a lens hole and a viewfinder cone on the right.
        public static Sprite Camera(int size = 64)
        {
            Vector2 apex = new(0.38f, 0f), top = new(0.95f, 0.45f), bottom = new(0.95f, -0.45f), lens = new(-0.27f, 0f);
            return Make(size, p =>
            {
                bool body = p.x >= -0.92f && p.x <= 0.38f && Mathf.Abs(p.y) <= 0.48f && (p - lens).magnitude > 0.24f;
                return body || InTriangle(p, apex, top, bottom) ? 1f : 0f;
            });
        }

        // "Can't be hit" icon: flat-topped shield tapering to a point.
        public static Sprite Shield(int size = 64) => Make(size, p =>
        {
            if (p.y > 0.85f || p.y < -0.95f) return 0f;
            float halfWidth = p.y >= 0f ? 0.75f : 0.75f * (p.y + 0.95f) / 0.95f;
            return Mathf.Abs(p.x) <= halfWidth ? 1f : 0f;
        });

        // Diagonal strike-through from top-left to bottom-right (overlay for "off").
        public static Sprite Slash(int size = 64) =>
            Make(size, p => Mathf.Abs(p.x + p.y) <= 0.22f && p.magnitude <= 0.95f ? 1f : 0f);

        // Round scope view: clear inside ~55% of the radius, solid at the edge and beyond (stretch to a square).
        public static Sprite Scope(int size = 128) =>
            Make(size, p => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, p.magnitude)), 1);

        // 9-sliced rounded rectangle; radius in pixels of the source texture.
        public static Sprite RoundedRect(int size = 64, int radius = 16)
        {
            float r = radius / (size * 0.5f);
            Sprite sprite = Make(size, p =>
            {
                Vector2 q = new(Mathf.Max(Mathf.Abs(p.x) - (1f - r), 0f), Mathf.Max(Mathf.Abs(p.y) - (1f - r), 0f));
                return q.magnitude <= r ? 1f : 0f;
            }, Samples, new Vector4(radius, radius, radius, radius));
            return sprite;
        }

        private static Sprite Make(int size, Func<Vector2, float> coverage, int samples = Samples,
            Vector4 border = default)
        {
            var pixels = new Color32[size * size];
            float step = 1f / samples;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    float u = (x + (sx + 0.5f) * step) / size * 2f - 1f;
                    float v = (y + (sy + 0.5f) * step) / size * 2f - 1f;
                    sum += coverage(new Vector2(u, v));
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(sum / (samples * samples)) * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        // Ellipse leaf centred at c, long axis along unit vector axis.
        private static bool InLeaf(Vector2 p, Vector2 c, Vector2 axis)
        {
            Vector2 d = p - c;
            float along = Vector2.Dot(d, axis) / 0.36f;
            float across = (d.x * -axis.y + d.y * axis.x) / 0.17f;
            return along * along + across * across <= 1f;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
