using UnityEngine;

namespace PeaHeads
{
    // Pure mesh generators for the peashooter body; used by the editor PlantBodyBuilder.
    public static class PlantMeshes
    {
        // Option B snout, head-local metres (radius, z): starts inside the head, flares to a rim at the Mouth (z 0.75),
        // then turns back inward so the opening has an inner wall.
        public static readonly Vector2[] SnoutProfile =
        {
            new(0.20f, 0.38f), new(0.17f, 0.52f), new(0.19f, 0.62f), new(0.27f, 0.70f),
            new(0.29f, 0.74f), new(0.25f, 0.76f), new(0.20f, 0.70f),
        };

        // Dark disc inside the snout: reaches the inner wall so no green ring shows around it.
        public const float MouthHoleZ = 0.73f;
        public const float MouthHoleRadius = 0.23f;

        // SnoutProfile index of the rim (largest z): before it is the outer wall, after it the inner wall.
        private const int RimIndex = 5;

        public static float OuterRadiusAt(float z) => RadiusAt(0, RimIndex, z);

        public static float InnerRadiusAt(float z) => RadiusAt(RimIndex, SnoutProfile.Length - 1, z);

        private static float RadiusAt(int from, int to, float z)
        {
            for (int i = from; i < to; i++)
            {
                Vector2 a = SnoutProfile[i], b = SnoutProfile[i + 1];
                if ((z - a.y) * (z - b.y) <= 0f) return Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, z));
            }
            throw new System.ArgumentOutOfRangeException(nameof(z), z, "outside this snout wall");
        }

        // Spins the (radius, z) profile around +Z. Ends stay open.
        public static Mesh Lathe(Vector2[] profile, int segments)
        {
            int ring = segments + 1;
            var vertices = new Vector3[profile.Length * ring];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j < ring; j++)
            {
                float angle = 2f * Mathf.PI * j / segments;
                vertices[i * ring + j] = new Vector3(Mathf.Cos(angle) * profile[i].x,
                    Mathf.Sin(angle) * profile[i].x, profile[i].y);
                uvs[i * ring + j] = new Vector2((float)j / segments, (float)i / (profile.Length - 1));
            }

            var triangles = new int[(profile.Length - 1) * segments * 6];
            int t = 0;
            for (int i = 0; i < profile.Length - 1; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * ring + j, b = a + ring, c = a + 1, d = b + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
            }
            return Build("Lathe", vertices, uvs, triangles);
        }

        // Hemisphere cap facing +Z (rim in the XY plane, apex at z = radius). Used as the eyelid.
        public static Mesh Dome(float radius, int segments, int rings)
        {
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float angle = 0.5f * Mathf.PI * (1f - (float)i / rings);
                profile[i] = new Vector2(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius);
            }
            Mesh mesh = Lathe(profile, segments);
            mesh.name = "Dome";

            // The apex ring collapses to one point, so RecalculateNormals leaves the seam copy with only zero-area
            // triangles (zero normal). The true apex normal is +Z.
            Vector3[] normals = mesh.normals;
            for (int j = rings * (segments + 1); j < normals.Length; j++) normals[j] = Vector3.forward;
            mesh.normals = normals;
            return mesh;
        }

        // Flat disc in the XY plane facing +Z.
        public static Mesh Disc(float radius, int segments)
        {
            var vertices = new Vector3[segments + 2];
            var uvs = new Vector2[vertices.Length];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int j = 0; j <= segments; j++)
            {
                float angle = 2f * Mathf.PI * j / segments;
                vertices[j + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                uvs[j + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle));
            }

            var triangles = new int[segments * 3];
            for (int j = 0; j < segments; j++)
            {
                triangles[j * 3] = 0;
                triangles[j * 3 + 1] = j + 1;
                triangles[j * 3 + 2] = j + 2;
            }
            return Build("Disc", vertices, uvs, triangles);
        }

        // Tapered leaf from the origin along +Z, face up (+Y), tip lifted by `curl`, slight centre crease.
        // Front and back faces use separate vertices so both sides light correctly.
        public static Mesh Leaf(float length, float width, float curl, int segments)
        {
            int rows = segments + 1;
            int side = rows * 3;
            var vertices = new Vector3[side * 2];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < rows; i++)
            {
                float t = (float)i / segments;
                float half = 0.5f * width * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.12f, 1f, t));
                float y = curl * t * t;
                for (int k = 0; k < 3; k++)
                {
                    float crease = k == 1 ? half * 0.15f : 0f;
                    var vertex = new Vector3((k - 1) * half, y + crease, t * length);
                    vertices[i * 3 + k] = vertex;
                    vertices[side + i * 3 + k] = vertex;
                    uvs[i * 3 + k] = uvs[side + i * 3 + k] = new Vector2(k * 0.5f, t);
                }
            }

            var triangles = new int[segments * 2 * 12];
            int n = 0;
            for (int i = 0; i < segments; i++)
            for (int k = 0; k < 2; k++)
            {
                int a = i * 3 + k, b = a + 3, c = a + 1, d = b + 1;
                triangles[n++] = a; triangles[n++] = b; triangles[n++] = c;
                triangles[n++] = c; triangles[n++] = b; triangles[n++] = d;
                triangles[n++] = side + a; triangles[n++] = side + c; triangles[n++] = side + b;
                triangles[n++] = side + c; triangles[n++] = side + d; triangles[n++] = side + b;
            }
            return Build("Leaf", vertices, uvs, triangles);
        }

        private static Mesh Build(string name, Vector3[] vertices, Vector2[] uvs, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
