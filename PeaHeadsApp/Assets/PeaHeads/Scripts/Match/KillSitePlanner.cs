using System.Collections.Generic;
using UnityEngine;

namespace PeaHeads
{
    // Pure layout maths for kill-site patches. Seeded from the death position so every machine builds the same patch.
    public static class KillSitePlanner
    {
        private const float GoldenAngle = 137.50776f;

        // Position rounded to 0.1 m, hashed.
        public static int Seed(Vector3 position)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(position.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(position.y * 10f);
                hash = hash * 31 + Mathf.RoundToInt(position.z * 10f);
                return hash;
            }
        }

        // Sunflower spiral with a seeded rotation and a little seeded jitter: even, natural-looking spread.
        public static Vector2[] Scatter(int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            float rotation = (float)rng.NextDouble() * 360f;
            float spacing = radius / Mathf.Sqrt(Mathf.Max(1, count));
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float r = radius * Mathf.Sqrt((i + 0.5f) / count);
                float angle = (rotation + i * GoldenAngle) * Mathf.Deg2Rad;
                Vector2 p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                var jitter = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                p += jitter * (0.15f * spacing);
                if (p.magnitude > radius) p = p.normalized * radius;
                points[i] = p;
            }
            return points;
        }

        // Other team's patches whose centre is within `radius` of the new one (horizontal distance), ascending.
        public static List<int> ToReplace(IReadOnlyList<(Vector3 centre, int team)> existing, Vector3 centre, int team,
            float radius)
        {
            var result = new List<int>();
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i].team == team) continue;
                var offset = new Vector2(existing[i].centre.x - centre.x, existing[i].centre.z - centre.z);
                if (offset.magnitude < radius) result.Add(i);
            }
            return result;
        }

        // A raycast hit belongs to the ground the plant died on: near its height (not a wall top, cover top or a drop
        // off a ledge) and flat enough to grow on.
        public static bool IsGroundHit(float deathY, float hitY, float normalY) =>
            Mathf.Abs(hitY - deathY) <= 0.75f && normalY >= 0.7f;

        // Lightness multiplier in [0.92, 1.08], snapped to `steps` values so patches share a few cached materials.
        public static float Shade(float random01, int steps)
        {
            int step = Mathf.Min(steps - 1, Mathf.FloorToInt(random01 * steps));
            return Mathf.Lerp(0.92f, 1.08f, step / (float)(steps - 1));
        }

        // Oldest patches to drop so one more fits under the cap.
        public static int OverCap(int count, int cap) => Mathf.Max(0, count + 1 - cap);
    }
}
