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
    }
}
