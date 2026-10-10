using System.Linq;
using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class MatchTeamsTests
    {
        private static ulong[] Ids(int count) => Enumerable.Range(0, count).Select(i => (ulong)i).ToArray();

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void AssignTeams_IsBalancedAndComplete(int players)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                int[] teams = MatchManager.AssignTeams(Ids(players), new System.Random(seed));
                Assert.AreEqual(players, teams.Length);
                Assert.IsTrue(teams.All(t => t == 0 || t == 1), $"seed {seed}");
                int bloom = teams.Count(t => t == 0);
                Assert.LessOrEqual(System.Math.Abs(bloom - (players - bloom)), 1, $"seed {seed}");
            }
        }

        [Test]
        public void AssignTeams_TwoPlayers_OneOnEachTeam()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                int[] teams = MatchManager.AssignTeams(Ids(2), new System.Random(seed));
                Assert.AreNotEqual(teams[0], teams[1], $"seed {seed}");
            }
        }

        [Test]
        public void AssignTeams_TwoPlayers_EitherCanBeBloom()
        {
            bool firstWasBloom = false, firstWasBlight = false;
            for (int seed = 0; seed < 200; seed++)
            {
                int team = MatchManager.AssignTeams(Ids(2), new System.Random(seed))[0];
                firstWasBloom |= team == 0;
                firstWasBlight |= team == 1;
            }
            Assert.IsTrue(firstWasBloom && firstWasBlight);
        }

        [Test]
        public void TeamOf_WithoutMatch_IsBloom()
        {
            Assert.AreEqual(0, MatchManager.TeamOf(5));
            Assert.IsFalse(MatchManager.HasTeam(5));
        }
    }
}
