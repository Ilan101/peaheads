using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantMeshesTests
    {
        private static readonly Vector2[] Cylinder = { new(0.5f, 0f), new(0.5f, 1f) };

        private static void AssertNormalsFiniteAndUnit(Mesh mesh)
        {
            foreach (Vector3 n in mesh.normals)
            {
                Assert.IsTrue(float.IsFinite(n.x) && float.IsFinite(n.y) && float.IsFinite(n.z), $"not finite: {n}");
                Assert.AreEqual(1f, n.magnitude, 1e-3f);
            }
        }

        [Test]
        public void Lathe_VertexCountIsProfileTimesRing() =>
            Assert.AreEqual(2 * 17, PlantMeshes.Lathe(Cylinder, 16).vertexCount);

        [Test]
        public void Lathe_RadiusMatchesProfile()
        {
            Bounds bounds = PlantMeshes.Lathe(Cylinder, 16).bounds;
            Assert.AreEqual(0.5f, bounds.extents.x, 1e-3f);
            Assert.AreEqual(0.5f, bounds.extents.y, 1e-3f);
            Assert.AreEqual(1f, bounds.size.z, 1e-3f);
        }

        [Test]
        public void Lathe_NormalsPointOutward()
        {
            Mesh mesh = PlantMeshes.Lathe(Cylinder, 16);
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 radial = new Vector3(vertices[i].x, vertices[i].y, 0f).normalized;
                Assert.Greater(Vector3.Dot(normals[i], radial), 0.9f, $"vertex {i}");
            }
        }

        [Test]
        public void SnoutLathe_NormalsFiniteAndRimAtMouth()
        {
            Mesh mesh = PlantMeshes.Lathe(PlantMeshes.SnoutProfile, 32);
            AssertNormalsFiniteAndUnit(mesh);
            Assert.AreEqual(0.76f, mesh.bounds.max.z, 1e-3f);
        }

        [Test]
        public void Disc_FacesPlusZ()
        {
            Mesh mesh = PlantMeshes.Disc(0.2f, 24);
            foreach (Vector3 n in mesh.normals) Assert.Greater(n.z, 0.99f);
            Assert.AreEqual(0.2f, mesh.bounds.extents.x, 1e-3f);
        }

        [Test]
        public void Leaf_IsDoubleSided()
        {
            Mesh mesh = PlantMeshes.Leaf(0.75f, 0.3f, 0.08f, 10);
            Vector3[] normals = mesh.normals;
            int side = normals.Length / 2;
            for (int i = 0; i < side; i++)
                Assert.Less(Vector3.Dot(normals[i], normals[i + side]), -0.99f, $"vertex {i}");
            Assert.Greater(normals[side / 2].y, 0f, "front face points up");
        }

        [Test]
        public void Leaf_SizeMatchesLengthWidthAndCurl()
        {
            Mesh mesh = PlantMeshes.Leaf(0.75f, 0.3f, 0.08f, 10);
            AssertNormalsFiniteAndUnit(mesh);
            Bounds bounds = mesh.bounds;
            Assert.AreEqual(0.75f, bounds.size.z, 1e-3f);
            Assert.AreEqual(0.3f, bounds.size.x, 0.01f);
            Assert.AreEqual(0.08f, bounds.max.y, 0.02f);
            Assert.AreEqual(0f, bounds.min.z, 1e-3f);
        }
    }
}
