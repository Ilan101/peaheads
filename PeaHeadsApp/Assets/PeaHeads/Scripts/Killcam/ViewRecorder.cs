using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeaHeads
{
    // Rolling few seconds of the local player's view for the killcam. A small capture camera rides on the player's
    // camera (no HUD, no shadows) and renders into a ring of RenderTextures. Collect() gathers the frames around a
    // kill, including the ones still to come: they're read back asynchronously and JPEG-encoded on worker threads,
    // so a kill never costs the killer a frame. Added by GameMenu.
    public class ViewRecorder : MonoBehaviour
    {
        // Raw frames being read back or encoded at once (allocated up front), and new readbacks started per frame:
        // a kill asks for ~40 frames at once, and doing them all in one frame is a visible hitch.
        private const int RawBuffers = 12;
        private const int ReadbacksPerFrame = 6;

        [SerializeField, Range(5, 30)] private int _fps = 25;
        [SerializeField, Range(2f, 6f)] private float _keepSeconds = 3.5f;
        [SerializeField] private Vector2Int _size = new(480, 270);
        [SerializeField, Range(10, 100)] private int _jpegQuality = 72;
        [Tooltip("Flip captured frames if the replay shows upside down on this graphics API.")]
        [SerializeField] private bool _flipVertical;

        public static ViewRecorder Instance { get; private set; }

        private sealed class Job
        {
            public double Kill, From, Until;
            public Action<List<ClipFrame>> Done;
            public readonly List<ClipFrame> Frames = new();
            public int Pending;
        }

        private readonly List<Job> _jobs = new();
        // Encoded on worker threads, handed back here so jobs and the buffer pool are only touched on the main thread.
        private readonly ConcurrentQueue<(Job job, float time, byte[] jpeg, byte[] raw)> _encoded = new();
        private readonly Stack<byte[]> _rawPool = new();
        private readonly Queue<(Job job, int index, float time)> _queued = new();
        private RenderTexture[] _ring;
        private double[] _times;
        private int _next;
        private RenderTexture _target;
        private Camera _capture;
        private Camera _source;
        private double _nextCapture;
        private bool _rendering;

        private int RawSize => _size.x * _size.y * 4;

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < RawBuffers; i++) _rawPool.Push(new byte[RawSize]);
        }

        private void OnEnable() => StartCoroutine(AfterRender());

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_ring != null)
                foreach (RenderTexture rt in _ring)
                    if (rt) rt.Release();
            if (_target) _target.Release();
            if (_capture) Destroy(_capture.gameObject);
        }

        // Frames in [kill - before, kill + after], times relative to the kill. Calls done on the main thread once the
        // last post-kill frame is captured and everything is encoded.
        public void Collect(double kill, double before, double after, Action<List<ClipFrame>> done)
        {
            var job = new Job { Kill = kill, From = kill - before, Until = kill + after, Done = done };
            _jobs.Add(job);
            if (_ring == null) return;
            foreach (int index in KillClip.Window(_times, kill, before, after)) Request(job, index);
        }

        private void Update()
        {
            Camera source = LocalCamera();
            if (source != _source) Attach(source);

            if (_capture && KillClip.Now >= _nextCapture && !LobbyView.HidesGame)
            {
                _nextCapture = KillClip.Now + 1.0 / _fps;
                _capture.fieldOfView = _source.fieldOfView;
                _capture.enabled = true; // renders once this frame; AfterRender stores it and switches it off
                _rendering = true;
            }

            while (_encoded.TryDequeue(out (Job job, float time, byte[] jpeg, byte[] raw) item))
            {
                if (item.jpeg != null) item.job.Frames.Add(new ClipFrame(item.time, item.jpeg));
                item.job.Pending--;
                _rawPool.Push(item.raw);
            }
            for (int n = 0; n < ReadbacksPerFrame && _queued.Count > 0 && _rawPool.Count > 0; n++)
            {
                (Job job, int index, float time) = _queued.Dequeue();
                ReadBack(job, index, time, _rawPool.Pop());
            }
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                Job job = _jobs[i];
                if (KillClip.Now < job.Until || job.Pending > 0) continue;
                _jobs.RemoveAt(i);
                job.Frames.Sort((a, b) => a.Time.CompareTo(b.Time));
                job.Done(job.Frames);
            }
        }

        private IEnumerator AfterRender()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (!_rendering) continue;
                _rendering = false;
                if (!_capture) continue;
                _capture.enabled = false;
                Store();
            }
        }

        private void Store()
        {
            EnsureRing();
            // Blit leaves the ring texture active; hand the screen back so nothing else reads a 480×270 target.
            RenderTexture previous = RenderTexture.active;
            if (_flipVertical) Graphics.Blit(_target, _ring[_next], new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_target, _ring[_next]);
            RenderTexture.active = previous;
            double now = KillClip.Now;
            _times[_next] = now;
            foreach (Job job in _jobs)
                if (now >= job.From && now <= job.Until) Request(job, _next);
            _next = (_next + 1) % _ring.Length;
        }

        // Queued; Update starts a few readbacks per frame. The ring slot is read long before it's overwritten.
        private void Request(Job job, int index)
        {
            job.Pending++;
            _queued.Enqueue((job, index, (float)(_times[index] - job.Kill)));
        }

        private void ReadBack(Job job, int index, float time, byte[] raw)
        {
            int width = _size.x, height = _size.y, quality = _jpegQuality;
            AsyncGPUReadback.Request(_ring[index], 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    _encoded.Enqueue((job, time, null, raw));
                    return;
                }
                NativeArray<byte> data = request.GetData<byte>();
                NativeArray<byte>.Copy(data, raw, Mathf.Min(data.Length, raw.Length));
                // ImageConversion's array encoders are thread safe: the JPEG work never touches the main thread.
                Task.Run(() =>
                {
                    byte[] jpeg = null;
                    try
                    {
                        jpeg = ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8A8_SRGB, (uint)width,
                            (uint)height, (uint)(width * 4), quality);
                    }
                    catch (Exception) { /* dropped frame */ }
                    _encoded.Enqueue((job, time, jpeg, raw));
                });
            });
        }

        private static Camera LocalCamera()
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject local = nm ? nm.LocalClient?.PlayerObject : null;
            return local && local.TryGetComponent(out PlayerCameraRig rig) ? rig.Camera : null;
        }

        // Capture camera as a child of the player's camera: same view, own small target, no shadows.
        private void Attach(Camera source)
        {
            _source = source;
            if (_capture) Destroy(_capture.gameObject);
            _capture = null;
            if (!source) return;
            if (!_target)
            {
                _target = new RenderTexture(_size.x, _size.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                _target.Create();
            }
            var go = new GameObject("KillcamCapture");
            go.transform.SetParent(source.transform, false);
            _capture = go.AddComponent<Camera>();
            _capture.CopyFrom(source);
            _capture.targetTexture = _target;
            _capture.depth = source.depth - 1f;
            _capture.allowHDR = false; // no post-processing on the capture, so HDR would only add a copy
            _capture.enabled = false;
            UniversalAdditionalCameraData data = _capture.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
        }

        private void EnsureRing()
        {
            if (_ring != null) return;
            int count = Mathf.CeilToInt(_keepSeconds * _fps);
            _ring = new RenderTexture[count];
            _times = new double[count];
            for (int i = 0; i < count; i++)
            {
                _ring[i] = new RenderTexture(_size.x, _size.y, 0, RenderTextureFormat.ARGB32);
                _times[i] = double.NegativeInfinity;
            }
        }
    }
}
