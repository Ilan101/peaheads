using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class LobbyTests
    {
        [TestCase(0, 0, ExpectedResult = 0)]
        [TestCase(1, 0, ExpectedResult = 1)]
        [TestCase(1, 1, ExpectedResult = 0)]
        [TestCase(1, 2, ExpectedResult = 0)]
        [TestCase(2, 1, ExpectedResult = 1)]
        public int SmallerTeam_PicksTheShortSide_BloomOnTies(int bloom, int blight) =>
            MatchManager.SmallerTeam(bloom, blight);

        [Test]
        public void LobbyMembers_InLobby_AreBothTeamsSorted() =>
            CollectionAssert.AreEqual(new ulong[] { 0, 1, 2, 3 },
                MatchManager.LobbyMembers(true, new ulong[] { 3, 0 }, new ulong[] { 2, 1 }, new ulong[] { 9 }));

        [Test]
        public void LobbyMembers_MidRound_AreOnlyTheWaiting() =>
            CollectionAssert.AreEqual(new ulong[] { 4, 7 },
                MatchManager.LobbyMembers(false, new ulong[] { 0 }, new ulong[] { 1 }, new ulong[] { 7, 4 }));

        [Test]
        public void LobbyMembers_CappedAtFourSlots() =>
            Assert.AreEqual(4,
                MatchManager.LobbyMembers(true, new ulong[] { 0, 2, 4 }, new ulong[] { 1, 3 }, new ulong[0]).Count);

        [Test]
        public void Stage_ContainsPointsNearItsOrigin() =>
            Assert.IsTrue(LobbyStage.Contains(LobbyStage.Origin + new UnityEngine.Vector3(5f, 1f, -3f)));

        [Test]
        public void Stage_DoesNotContainTheArena() =>
            Assert.IsFalse(LobbyStage.Contains(UnityEngine.Vector3.zero));

        [TestCase(false, true, false, ExpectedResult = false)]
        [TestCase(true, true, false, ExpectedResult = true)]
        [TestCase(true, false, true, ExpectedResult = true)]
        [TestCase(true, false, false, ExpectedResult = false)]
        public bool ShouldOpen_AfterTheStory_InLobbyOrWaiting(bool storyDone, bool matchIsLobby, bool waiting) =>
            LobbyView.ShouldOpen(storyDone, matchIsLobby, waiting);
    }
}
