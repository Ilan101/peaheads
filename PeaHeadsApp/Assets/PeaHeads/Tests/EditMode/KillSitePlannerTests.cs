using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class KillSitePlannerTests
    {
        [TestCase(14, 1.5f)]
        [TestCase(8, 1.2f)]
        public void Scatter_PointsInsideRadiusAndSpreadOut(int count, float radius)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                Vector2[] points = KillSitePlanner.Scatter(count, radius, seed);
                Assert.AreEqual(count, points.Length);
                float minSpacing = 0.5f * radius / Mathf.Sqrt(count);
                for (int i = 0; i < count; i++)
                {
                    Assert.LessOrEqual(points[i].magnitude, radius + 1e-4f, $"seed {seed} point {i}");
                    for (int j = i + 1; j < count; j++)
                        Assert.GreaterOrEqual((points[i] - points[j]).magnitude, minSpacing, $"seed {seed} pair {i},{j}");
                }
            }
        }

        [Test]
        public void Scatter_SameSeedSamePoints() =>
            Assert.AreEqual(KillSitePlanner.Scatter(14, 1.5f, 42), KillSitePlanner.Scatter(14, 1.5f, 42));

        [Test]
        public void Scatter_DifferentSeedDifferentPoints() =>
            Assert.AreNotEqual(KillSitePlanner.Scatter(14, 1.5f, 1), KillSitePlanner.Scatter(14, 1.5f, 2));

        [Test]
        public void Seed_SamePositionSameSeed() =>
            Assert.AreEqual(KillSitePlanner.Seed(new Vector3(3.04f, 0.5f, -7.2f)),
                KillSitePlanner.Seed(new Vector3(3.01f, 0.52f, -7.18f)));

        [Test]
        public void Seed_DifferentPositionsDifferentSeeds() =>
            Assert.AreNotEqual(KillSitePlanner.Seed(new Vector3(3f, 0f, 7f)), KillSitePlanner.Seed(new Vector3(4f, 0f, 7f)));

        private static readonly List<(Vector3 centre, int team)> Sites = new()
        {
            (new Vector3(0f, 0f, 0f), 1),     // other team, inside
            (new Vector3(1f, 5f, 0f), 1),     // other team, inside on XZ despite height
            (new Vector3(0.5f, 0f, 0.5f), 0), // same team, inside
            (new Vector3(4f, 0f, 0f), 1),     // other team, outside
        };

        [Test]
        public void ToReplace_OnlyOtherTeamInsideRadiusOnXZ() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, KillSitePlanner.ToReplace(Sites, Vector3.zero, 0, 1.5f));

        [TestCase(0.08f, 0.02f, 1f, ExpectedResult = true)]    // flat ground under the plant
        [TestCase(0.08f, 3.0f, 1f, ExpectedResult = false)]    // top of a 3 m boundary wall
        [TestCase(0.08f, 1.3f, 1f, ExpectedResult = false)]    // top of a log beside the plant
        [TestCase(1.38f, 1.3f, 1f, ExpectedResult = true)]     // plant died standing on the log
        [TestCase(0.08f, -0.5f, 0.9f, ExpectedResult = true)]  // gentle dip
        [TestCase(0.08f, -1.2f, 1f, ExpectedResult = false)]   // off a ledge
        [TestCase(0.08f, 0.1f, 0.5f, ExpectedResult = false)]  // too steep
        public bool IsGroundHit_OnlyNearDeathHeightAndFlat(float deathY, float hitY, float normalY) =>
            KillSitePlanner.IsGroundHit(deathY, hitY, normalY);

        [Test]
        public void Shade_QuantisedWithinRange()
        {
            var seen = new HashSet<float>();
            for (int i = 0; i <= 100; i++)
            {
                float shade = KillSitePlanner.Shade(i / 100f, 5);
                Assert.That(shade, Is.InRange(0.92f, 1.08f));
                seen.Add(shade);
            }
            Assert.AreEqual(5, seen.Count);
        }

        [TestCase(5, 20, ExpectedResult = 0)]
        [TestCase(19, 20, ExpectedResult = 0)]
        [TestCase(20, 20, ExpectedResult = 1)]
        [TestCase(22, 20, ExpectedResult = 3)]
        public int OverCap_RemovesOldestToFitOneMore(int count, int cap) => KillSitePlanner.OverCap(count, cap);
    }
}
