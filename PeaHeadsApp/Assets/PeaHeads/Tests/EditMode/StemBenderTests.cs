using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class StemBenderTests
    {
        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"expected {expected}, got {actual}");

        private static void AssertFinite(Vector3 v) =>
            Assert.IsTrue(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z), $"not finite: {v}");

        [Test]
        public void Bezier_EndpointsMatchBaseAndTip()
        {
            Vector3 p0 = new(0f, 0.16f, 0f), p1 = new(0f, 0.9f, 0f), p2 = new(0.6f, 1.4f, 0f);

            AssertNear(p0, StemBender.Bezier(p0, p1, p2, 0f));
            AssertNear(p2, StemBender.Bezier(p0, p1, p2, 1f));
        }

        [Test]
        public void Bezier_StraightControlGivesStraightLine()
        {
            Vector3 p0 = Vector3.zero, p2 = new(0f, 2f, 0f);

            AssertNear(new Vector3(0f, 1f, 0f), StemBender.Bezier(p0, (p0 + p2) * 0.5f, p2, 0.5f));
        }

        [Test]
        public void SegmentPose_SpansFromTo()
        {
            Vector3 from = new(0f, 1f, 0f), to = new(1f, 1f, 0f);

            StemBender.SegmentPose(from, to, 0.04f, 0.14f, out Vector3 position, out Quaternion rotation,
                out Vector3 scale);

            AssertNear(new Vector3(0.5f, 1f, 0f), position);
            AssertNear(Vector3.right, rotation * Vector3.up);
            Assert.AreEqual(0.52f, scale.y, 1e-4f); // (1 + 0.04) / 2: capsule mesh is 2 units tall
            Assert.AreEqual(0.14f, scale.x, 1e-4f);
            Assert.AreEqual(0.14f, scale.z, 1e-4f);
        }

        [Test]
        public void SegmentPose_ZeroLength_NoNaN()
        {
            Vector3 point = new(0.3f, 1f, 0.2f);

            StemBender.SegmentPose(point, point, 0.04f, 0.14f, out Vector3 position, out Quaternion rotation,
                out Vector3 scale);

            AssertFinite(position);
            AssertFinite(rotation * Vector3.up);
            AssertFinite(scale);
        }

        [Test]
        public void Thickness_AtRestLengthIsBaseThickness()
        {
            Assert.AreEqual(0.14f, StemBender.Thickness(0.14f, 1.2f, 1.2f, 0.5f), 1e-4f);
        }

        [Test]
        public void Thickness_SquashedIsThickerStretchedIsThinner()
        {
            Assert.Greater(StemBender.Thickness(0.14f, 1.2f, 0.9f, 0.5f), 0.14f);
            Assert.Less(StemBender.Thickness(0.14f, 1.2f, 1.6f, 0.5f), 0.14f);
        }

        [Test]
        public void Thickness_IsClampedWhenSquashed()
        {
            Assert.LessOrEqual(StemBender.Thickness(0.14f, 1.2f, 0f, 1f), 0.28f + 1e-4f);
            Assert.GreaterOrEqual(StemBender.Thickness(0.14f, 1.2f, 100f, 1f), 0.07f - 1e-4f);
        }

        [Test]
        public void SpringStep_ConvergesToTarget()
        {
            Vector3 position = Vector3.zero, velocity = Vector3.zero, target = new(0.6f, 1.4f, 0f);

            for (int i = 0; i < 300; i++)
                StemBender.SpringStep(ref position, ref velocity, target, 120f, 14f, 1f / 60f);

            AssertNear(target, position, 1e-3f);
        }

        [Test]
        public void SpringStep_LargeDeltaTime_StaysStable()
        {
            Vector3 position = Vector3.zero, velocity = Vector3.zero, target = new(0.6f, 1.4f, 0f);

            for (int i = 0; i < 300; i++)
            {
                StemBender.SpringStep(ref position, ref velocity, target, 120f, 14f, 0.5f);
                Assert.Less(Vector3.Distance(position, target), 5f, $"diverged at step {i}");
            }

            AssertNear(target, position, 1e-2f);
        }
    }
}
