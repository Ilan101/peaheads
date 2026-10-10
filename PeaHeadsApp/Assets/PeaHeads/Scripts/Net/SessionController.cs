using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PeaHeads.Net
{
    // Host / join / leave over LAN (direct IP) and the status line the menu shows. No UI of its own.
    public class SessionController : MonoBehaviour
    {
        [SerializeField] private ushort _port = 7777;
        [SerializeField] private string _defaultAddress = "127.0.0.1";
        [SerializeField] private int _maxConnectAttempts = 10;

        public string Address { get; set; }
        public string LocalIps { get; private set; }
        public ushort Port => _port;
        public string Status { get; private set; } = "";

        private bool _wasConnected;
        // Our own Leave(): a disconnect callback arriving afterwards must not replace "Left the match."
        private bool _leaving;

        private void Awake()
        {
            Address = _defaultAddress;
            List<string> ips = GetLanIPv4Addresses();
            LocalIps = ips.Count > 0 ? string.Join(", ", ips) : "no LAN address";
        }

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientDisconnectCallback -= OnClientDisconnect;
            nm.OnClientConnectedCallback -= OnClientConnected;
        }

        public void Host()
        {
            _leaving = false;
            Transport().SetConnectionData("127.0.0.1", _port, "0.0.0.0");
            Status = NetworkManager.Singleton.StartHost() ? "" : $"Host failed. Port {_port} in use?";
        }

        public void Join()
        {
            _leaving = false;
            var transport = Transport();
            transport.MaxConnectAttempts = _maxConnectAttempts;
            transport.SetConnectionData((Address ?? "").Trim(), _port);
            Status = NetworkManager.Singleton.StartClient() ? "" : "Join failed. Check the IP.";
        }

        public void Leave()
        {
            _leaving = true;
            _wasConnected = false;
            NetworkManager.Singleton.Shutdown();
            Status = "Left the match.";
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            Status = "";
            _wasConnected = true;
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsServer && clientId != nm.LocalClientId)
            {
                Status = $"Player {clientId} left.";
                return;
            }
            if (_leaving) return;

            if (!string.IsNullOrEmpty(nm.DisconnectReason)) Status = nm.DisconnectReason;
            else if (_wasConnected) Status = "Host ended the session.";
            else Status = "Disconnected / could not reach host. Same Wi-Fi? Firewall?";
            _wasConnected = false;
        }

        private static UnityTransport Transport() =>
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        private static List<string> GetLanIPv4Addresses()
        {
            var result = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue; // skips VirtualBox/host-only adapters

                foreach (var addr in props.UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(addr.Address.ToString());
            }
            return result;
        }
    }
}
