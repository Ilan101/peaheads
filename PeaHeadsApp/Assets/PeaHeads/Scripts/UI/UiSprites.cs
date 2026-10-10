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

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
