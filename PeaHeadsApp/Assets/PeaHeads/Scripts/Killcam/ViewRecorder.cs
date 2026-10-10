using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // Rolling few seconds of the local screen, downscaled into a ring of small RenderTextures, so a kill can be
    // replayed. Records only while the local plant exists and the story isn't playing. Freeze() reads the frames
    // around a kill back from the GPU and JPEG-encodes them. Added by GameMenu.
    public class ViewRecorder : MonoBehaviour
    {
        [SerializeField, Range(4, 30)] private int _fps = 10;
        [SerializeField, Range(1f, 6f)] private float _keepSeconds = 3.5f;
        [SerializeField] private Vector2Int _size = new(256, 144);
        [SerializeField, Range(10, 100)] private int _jpegQuality = 45;

        public static ViewRecorder Instance { get; private set; }

        private RenderTexture[] _ring;
        private double[] _times;
        private int _next;
        private RenderTexture _screen;
        private double _nextCapture;

        private void Awake() => Instance = this;

        private void OnEnable() => StartCoroutine(Record());

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_ring != null)
                foreach (RenderTexture rt in _ring)
                    if (rt) rt.Release();
            if (_screen) _screen.Release();
        }

        private IEnumerator Record()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (KillClip.Now < _nextCapture || !ShouldRecord()) continue;
                _nextCapture = KillClip.Now + 1.0 / _fps;
                Capture();
            }
        }

        private static bool ShouldRecord()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm && nm.LocalClient?.PlayerObject && !StoryIntro.IsPlaying;
        }

        private void Capture()
        {
            if (_ring == null)
            {
                int count = Mathf.CeilToInt(_keepSeconds * _fps);
                _ring = new RenderTexture[count];
                _times = new double[count];
                for (int i = 0; i < count; i++)
                {
                    _ring[i] = new RenderTexture(_size.x, _size.y, 0, RenderTextureFormat.ARGB32);
                    _times[i] = double.NegativeInfinity;
                }
            }
            if (!_screen || _screen.width != Screen.width || _screen.height != Screen.height)
            {
                if (_screen) _screen.Release();
                _screen = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screen);
            // The capture is upside down where UVs start at the top (D3D, Metal, Vulkan).
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_screen, _ring[_next], new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_screen, _ring[_next]);
            _times[_next] = KillClip.Now;
            _next = (_next + 1) % _ring.Length;
        }

        // JPEG frames in [kill - before, kill + after] (times relative to the kill), delivered on the main thread.
        public void Freeze(double kill, double before, double after, Action<List<ClipFrame>> done)
        {
            if (_ring == null)
            {
                done(new List<ClipFrame>());
                return;
            }
            List<int> picked = KillClip.Window(_times, kill, before, after);
            var frames = new ClipFrame[picked.Count];
            int pending = picked.Count;
            if (pending == 0)
            {
                done(new List<ClipFrame>());
                return;
            }
            for (int k = 0; k < picked.Count; k++)
            {
                int slot = k;
                float time = (float)(_times[picked[k]] - kill);
                AsyncGPUReadback.Request(_ring[picked[k]], 0, TextureFormat.RGBA32, request =>
                {
                    byte[] jpeg = Array.Empty<byte>();
                    if (!request.hasError)
                    {
                        var texture = new Texture2D(_size.x, _size.y, TextureFormat.RGBA32, false);
                        texture.LoadRawTextureData(request.GetData<byte>());
                        texture.Apply();
                        jpeg = texture.EncodeToJPG(_jpegQuality);
                        Destroy(texture);
                    }
                    frames[slot] = new ClipFrame(time, jpeg);
                    if (--pending == 0) done(new List<ClipFrame>(frames));
                });
            }
        }
    }
}
