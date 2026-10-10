using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeaHeads
{
    // Rolling few seconds of the local player's view for the killcam. A small capture camera rides on the player's
    // camera (no HUD, no shadows) and renders into a ring of RenderTextures a few times per second. Collect() gathers
    // the frames around a kill, including the ones still to come, reads them back asynchronously and JPEG-encodes a
    // few per frame so nothing hitches. Added by GameMenu.
    public class ViewRecorder : MonoBehaviour
    {
        [SerializeField, Range(5, 30)] private int _fps = 20;
        [SerializeField, Range(2f, 6f)] private float _keepSeconds = 3.5f;
        [SerializeField] private Vector2Int _size = new(480, 270);
        [SerializeField, Range(10, 100)] private int _jpegQuality = 70;
        [SerializeField, Range(1, 8)] private int _encodesPerFrame = 3;
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
        private readonly Queue<(Job job, float time, byte[] raw, int length)> _toEncode = new();
        private RenderTexture[] _ring;
        private double[] _times;
        private int _next;
        private RenderTexture _target;
        private Camera _capture;
        private Camera _source;
        private Texture2D _encoder;
        private double _nextCapture;
        private bool _rendering;

        private void Awake() => Instance = this;

        private void OnEnable() => StartCoroutine(AfterRender());

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_ring != null)
                foreach (RenderTexture rt in _ring)
                    if (rt) rt.Release();
            if (_target) _target.Release();
            if (_capture) Destroy(_capture.gameObject);
            if (_encoder) Destroy(_encoder);
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

            if (_capture && KillClip.Now >= _nextCapture && !StoryIntro.IsPlaying)
            {
                _nextCapture = KillClip.Now + 1.0 / _fps;
                _capture.fieldOfView = _source.fieldOfView;
                _capture.enabled = true; // renders once this frame; AfterRender stores it and switches it off
                _rendering = true;
            }

            for (int i = 0; i < _encodesPerFrame && _toEncode.Count > 0; i++) Encode(_toEncode.Dequeue());
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
            if (_flipVertical) Graphics.Blit(_target, _ring[_next], new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_target, _ring[_next]);
            double now = KillClip.Now;
            _times[_next] = now;
            foreach (Job job in _jobs)
                if (now >= job.From && now <= job.Until) Request(job, _next);
            _next = (_next + 1) % _ring.Length;
        }

        private void Request(Job job, int index)
        {
            job.Pending++;
            float time = (float)(_times[index] - job.Kill);
            AsyncGPUReadback.Request(_ring[index], 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    job.Pending--;
                    return;
                }
                NativeArray<byte> data = request.GetData<byte>();
                byte[] raw = ArrayPool<byte>.Shared.Rent(data.Length);
                NativeArray<byte>.Copy(data, raw, data.Length);
                _toEncode.Enqueue((job, time, raw, data.Length));
            });
        }

        private void Encode((Job job, float time, byte[] raw, int length) item)
        {
            if (!_encoder) _encoder = new Texture2D(_size.x, _size.y, TextureFormat.RGBA32, false);
            NativeArray<byte> pixels = _encoder.GetRawTextureData<byte>();
            NativeArray<byte>.Copy(item.raw, pixels, Mathf.Min(item.length, pixels.Length));
            item.job.Frames.Add(new ClipFrame(item.time, _encoder.EncodeToJPG(_jpegQuality)));
            ArrayPool<byte>.Shared.Return(item.raw);
            item.job.Pending--;
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
