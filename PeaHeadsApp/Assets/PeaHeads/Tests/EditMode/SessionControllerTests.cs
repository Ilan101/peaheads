using NUnit.Framework;
using PeaHeads.Net;
using Unity.Netcode;

namespace PeaHeads.Tests
{
    public class SessionControllerTests
    {
        private const NetworkTransport.DisconnectEvents Remote = NetworkTransport.DisconnectEvents.ClosedByRemote;

        [Test]
        public void OwnLeave_KeepsStatus_EvenWhenHostSeesRemoteClientDrop() =>
            Assert.IsNull(SessionController.DisconnectStatus(true, true, 1, true, Remote));

        [Test]
        public void OwnLeave_KeepsStatus_OnLocalDisconnect() =>
            Assert.IsNull(SessionController.DisconnectStatus(true, false, 0, true, Remote));

        [Test]
        public void HostSeesRemoteClientLeave() =>
            Assert.AreEqual("Player 1 left.", SessionController.DisconnectStatus(false, true, 1, true, Remote));

        [Test]
        public void ConnectedClient_HostClosed_SaysHostEnded() =>
            Assert.AreEqual("Host ended the session.",
                SessionController.DisconnectStatus(false, false, 1, true, Remote));

        [Test]
        public void ConnectedClient_Timeout_SaysLostConnection() =>
            Assert.AreEqual("Lost connection to the host.",
                SessionController.DisconnectStatus(false, false, 1, true,
                    NetworkTransport.DisconnectEvents.ProtocolTimeout));

        [TestCase(NetworkTransport.DisconnectEvents.MaxConnectionAttempts)]
        [TestCase(NetworkTransport.DisconnectEvents.ProtocolTimeout)]
        [TestCase(NetworkTransport.DisconnectEvents.ClosedByRemote)]
        public void NeverConnected_SaysCouldNotReachHost(NetworkTransport.DisconnectEvents disconnectEvent) =>
            Assert.AreEqual("Could not reach host. Same Wi-Fi? Firewall?",
                SessionController.DisconnectStatus(false, false, 1, false, disconnectEvent));

        [Test]
        public void RejectedAsFull_SaysLobbyFull() =>
            Assert.AreEqual("Lobby full (4 peas).",
                SessionController.DisconnectStatus(false, false, 0, false, Remote, SessionController.LobbyFullReason));

        [TestCase(0, ExpectedResult = true)]
        [TestCase(3, ExpectedResult = true)]
        [TestCase(4, ExpectedResult = false)]
        [TestCase(5, ExpectedResult = false)]
        public bool CanJoin_UpToFourPlayers(int connected) => SessionController.CanJoin(connected);
    }
}
