using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PeaHeads.Net
{
    public class ConnectionUI : MonoBehaviour
    {
        [SerializeField] private ushort _port = 7777;
        [SerializeField] private string _defaultAddress = "127.0.0.1";
        [SerializeField] private int _maxConnectAttempts = 10;

        private string _address;
        private string _status = "";
        private string _localIps;

        private void Start()
        {
            _address = _defaultAddress;
            _localIps = string.Join(", ", GetLanIPv4Addresses());

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

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (nm.IsHost ? nm.ConnectedClientsIds.Count >= 2 : nm.IsConnectedClient) return;

            float scale = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUILayout.BeginArea(new Rect(10, 10, 340, 240), GUI.skin.box);

            if (!nm.IsClient && !nm.IsServer)
            {
                GUILayout.Label($"This PC: {(_localIps.Length > 0 ? _localIps : "no LAN address")}  port {_port}");
                GUILayout.Label("Host IP to join:");
                _address = GUILayout.TextField(_address);
                if (GUILayout.Button("Host")) StartHost();
                if (GUILayout.Button("Join")) StartClient();
            }
            else
            {
                if (nm.IsHost)
                    GUILayout.Label($"Hosting on {_localIps}:{_port}  players: {nm.ConnectedClientsIds.Count}");
                else if (nm.IsConnectedClient)
                    GUILayout.Label($"Connected to {_address}:{_port}");
                else
                    GUILayout.Label($"Connecting to {_address}:{_port}...");

                if (GUILayout.Button("Disconnect")) nm.Shutdown();
            }

            if (_status.Length > 0) GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        private void StartHost()
        {
            var transport = Transport();
            transport.SetConnectionData("127.0.0.1", _port, "0.0.0.0");
            _status = NetworkManager.Singleton.StartHost() ? "" : $"Host failed. Port {_port} in use?";
        }

        private void StartClient()
        {
            var transport = Transport();
            transport.MaxConnectAttempts = _maxConnectAttempts;
            transport.SetConnectionData(_address.Trim(), _port);
            _status = NetworkManager.Singleton.StartClient() ? "" : "Join failed. Check the IP.";
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId) _status = "";
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsServer && clientId != nm.LocalClientId)
            {
                _status = $"Player {clientId} left.";
                return;
            }

            _status = string.IsNullOrEmpty(nm.DisconnectReason)
                ? "Disconnected / could not reach host. Same Wi-Fi? Firewall?"
                : nm.DisconnectReason;
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
