using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class HeadTrackingReceiver : MonoBehaviour
{
    [Serializable]
    class Packet
    {
        public bool face;
        public float dx, dy;
        public bool aim, shoot;
    }

    public static HeadTrackingReceiver Instance { get; private set; }

    [Header("Network")]
    public int port = 5052;
    public float lostTimeout = 0.5f;   // seconds without packets = tracking lost

    [Header("Latest values (read these from other scripts)")]
    public bool FaceVisible;
    public float Dx;       // + = you leaned right
    public float Dy;       // + = you moved up
    public bool Aim;       // one eye closed
    public bool Shoot;     // mouth open

    [Header("Test cube (optional)")]
    public Transform testHead;
    public float sensitivity = 3f;
    public float followSpeed = 15f;

    UdpClient client;
    Thread thread;
    volatile bool running;
    readonly object lockObj = new object();
    Packet latest;
    float lastPacketTime = -999f;

    void Awake() { Instance = this; }

    void OnEnable()
    {
        running = true;
        client = new UdpClient(port);
        thread = new Thread(Listen) { IsBackground = true };
        thread.Start();
    }

    void Listen()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                string json = Encoding.UTF8.GetString(client.Receive(ref ep));
                var p = JsonUtility.FromJson<Packet>(json);
                lock (lockObj) { latest = p; }
            }
            catch (SocketException) { break; }       // socket closed
            catch (ObjectDisposedException) { break; }
            catch (Exception e) { Debug.LogWarning("Bad packet: " + e.Message); }
        }
    }

    void Update()
    {
        Packet p;
        lock (lockObj) { p = latest; latest = null; }

        if (p != null) lastPacketTime = Time.time;

        bool alive = Time.time - lastPacketTime < lostTimeout;
        if (p != null && alive)
        {
            FaceVisible = p.face;
            Dx = p.dx; Dy = p.dy;
            Aim = p.aim; Shoot = p.shoot;
        }
        else if (!alive)
        {
            FaceVisible = false; Dx = 0; Dy = 0; Aim = false; Shoot = false;
        }

        if (testHead != null)
        {
            var target = new Vector3(Dx * sensitivity, Dy * sensitivity, 0f);
            testHead.localPosition = Vector3.Lerp(testHead.localPosition, target, followSpeed * Time.deltaTime);

            var r = testHead.GetComponent<Renderer>();
            if (r != null) r.material.color = Shoot ? Color.red : (Aim ? Color.yellow : Color.white);
        }
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 25),
            $"face:{FaceVisible}  dx:{Dx:F2}  dy:{Dy:F2}  aim:{Aim}  shoot:{Shoot}");
    }

    void OnDisable()
    {
        running = false;
        client?.Close();
        client = null;
    }
}