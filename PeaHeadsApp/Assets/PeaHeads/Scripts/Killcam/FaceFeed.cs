using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace PeaHeads
{
    // Last few seconds of webcam JPEGs from head_tracker.py (UDP 5053), timestamped with KillClip.Now, for the
    // killcam. Background thread like HeadTrackingReceiver; turns itself off if the port is taken. Added by GameMenu.
    public class FaceFeed : MonoBehaviour
    {
        [SerializeField] private int _port = 5053;
        [SerializeField, Range(1f, 10f)] private float _keepSeconds = 4f;

        public static FaceFeed Instance { get; private set; }

        // Received a frame within the last second.
        public bool Live => KillClip.Now - _lastFrame < 1.0;

        private readonly object _lock = new();
        private readonly Queue<(double time, byte[] jpeg)> _frames = new();
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private double _lastFrame = double.NegativeInfinity;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            try { _client = new UdpClient(_port); }
            catch (SocketException e)
            {
                Debug.LogWarning($"[PeaHeads] Killcam face feed off: can't listen on UDP {_port} ({e.Message}).");
                enabled = false;
                return;
            }
            _running = true;
            _thread = new Thread(Listen) { IsBackground = true };
            _thread.Start();
        }

        private void OnDisable()
        {
            _running = false;
            _client?.Close();
            _client = null;
        }

        private void Listen()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] jpeg = _client.Receive(ref endpoint);
                    double now = KillClip.Now;
                    lock (_lock)
                    {
                        _frames.Enqueue((now, jpeg));
                        while (_frames.Count > 0 && now - _frames.Peek().time > _keepSeconds) _frames.Dequeue();
                        _lastFrame = now;
                    }
                }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // Frames in [kill - before, kill + after], times made relative to the kill.
        public List<ClipFrame> Window(double kill, double before, double after)
        {
            var result = new List<ClipFrame>();
            lock (_lock)
            {
                foreach ((double time, byte[] jpeg) in _frames)
                    if (time >= kill - before && time <= kill + after) result.Add(new ClipFrame((float)(time - kill), jpeg));
            }
            return result;
        }
    }
}
