using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class GameMenuTests
    {
        [Test]
        public void ScreenFor_Idle_IsStart() =>
            Assert.AreEqual(MenuScreen.Start, GameMenu.ScreenFor(false, false, false));

        [Test]
        public void ScreenFor_IdleWithStalePauseFlag_IsStart() =>
            Assert.AreEqual(MenuScreen.Start, GameMenu.ScreenFor(false, false, true));

        [Test]
        public void ScreenFor_ActiveNotConnected_IsConnecting() =>
            Assert.AreEqual(MenuScreen.Connecting, GameMenu.ScreenFor(true, false, false));

        [Test]
        public void ScreenFor_ConnectingIgnoresPauseFlag() =>
            Assert.AreEqual(MenuScreen.Connecting, GameMenu.ScreenFor(true, false, true));

        [Test]
        public void ScreenFor_ConnectedClosed_IsNone() =>
            Assert.AreEqual(MenuScreen.None, GameMenu.ScreenFor(true, true, false));

        [Test]
        public void ScreenFor_ConnectedOpen_IsPause() =>
            Assert.AreEqual(MenuScreen.Pause, GameMenu.ScreenFor(true, true, true));

        [Test]
        public void SessionActive_FalseWhileShuttingDown() =>
            Assert.IsFalse(GameMenu.SessionActive(true, true, true));

        [TestCase(true, false, ExpectedResult = true)]
        [TestCase(false, true, ExpectedResult = true)]
        [TestCase(false, false, ExpectedResult = false)]
        public bool SessionActive_ClientOrServer(bool isClient, bool isServer) =>
            GameMenu.SessionActive(isClient, isServer, false);

        [TestCase(true, true, ExpectedResult = true)]
        [TestCase(true, false, ExpectedResult = false)]
        [TestCase(false, true, ExpectedResult = false)]
        [TestCase(false, false, ExpectedResult = false)]
        public bool ShowRestart_OnlyHostWithMatch(bool isHost, bool matchExists) =>
            GameMenu.ShowRestart(isHost, matchExists);

        [Test]
        public void ScreenFor_ConnectedInLobby_IsLobby() =>
            Assert.AreEqual(MenuScreen.Lobby, GameMenu.ScreenFor(true, true, false, true));

        [Test]
        public void ScreenFor_PauseOverLobby_IsPause() =>
            Assert.AreEqual(MenuScreen.Pause, GameMenu.ScreenFor(true, true, true, true));

        [Test]
        public void ScreenFor_IdleIgnoresLobbyFlag() =>
            Assert.AreEqual(MenuScreen.Start, GameMenu.ScreenFor(false, false, false, true));
    }
}
