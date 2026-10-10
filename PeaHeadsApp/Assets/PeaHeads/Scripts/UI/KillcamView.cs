using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Killcam: on a kill, the killer's last seconds of view (ViewRecorder) and webcam face (FaceFeed) play side by
    // side — "THE SHOT" | "THE FACE" — ending in slow motion and a freeze on the hit. The killer sees it small in
    // the bottom-right corner; the clip is also sent to the victim (PlayerShooting.SendKillcam), who sees it full
    // screen over the death screen, shrinking to the corner once they respawn. Added by GameMenu.
    public class KillcamView : MonoBehaviour
    {
        public const ulong NoVictim = ulong.MaxValue;
        private const float Before = 2.5f;
        private const float After = 0.25f;
        private const float SlowFrom = -0.6f;
        private const float SlowSpeed = 0.35f;
        private const float FreezeTime = 0.7f;
        private const float FadeTime = 0.4f;
        private const float CompactScale = 0.42f;
        private const string ShareFaceKey = "PeaHeads.KillcamFace";
        private static readonly Vector2 PanelSize = new(530f, 298f);
        private static readonly Vector2 RootSize = new(1100f, 430f);
        private static readonly Color Bad = new(1f, 0.36f, 0.32f);

        private static int _shareFace = -1;

        public static KillcamView Instance { get; private set; }
        // The local player is dead and the killcam owns the middle of the screen (PlayerHUD moves its text aside).
        public static bool FullscreenActive { get; private set; }

        // Whether this player's webcam face goes into the clip sent to the victim.
        public static bool ShareFace
        {
            get
            {
                if (_shareFace < 0) _shareFace = PlayerPrefs.GetInt(ShareFaceKey, 1);
                return _shareFace == 1;
            }
            set
            {
                _shareFace = value ? 1 : 0;
                PlayerPrefs.SetInt(ShareFaceKey, _shareFace);
            }
        }

        private readonly ClipAssembler _assembler = new();
        private int _nextClipId;
        private KillClip _clip;
        private readonly List<Texture2D> _textures = new();
        private Texture2D[] _viewFrames = new Texture2D[0], _faceFrames = new Texture2D[0];
        private float[] _viewTimes = new float[0], _faceTimes = new float[0];
        private float _playStart = float.NegativeInfinity;
        private float _slowAt, _freezeAt, _endAt, _startClipTime;
        private bool _forVictim, _slowPlayed, _freezePlayed;
        private bool _wasDead;
        private float _diedAt = float.NegativeInfinity;
        private float _compact = 1f;

        private RectTransform _canvas, _root;
        private CanvasGroup _group;
        private RawImage _view, _face;
        private Image _flash, _progress, _badge;
        private Text _subtitle, _noWebcam, _waiting;

        private void Awake()
        {
            Instance = this;
            _nextClipId = Random.Range(0, 1 << 20);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            FullscreenActive = false;
            ClearTextures();
            if (_canvas) Destroy(_canvas.gameObject);
        }

        // ---------- Killer side ----------

        // Owner of the shooting plant: one of its peas just knocked out `victim` (NoVictim for dummies).
        public void OnLocalKill(PlayerShooting shooter, ulong victim)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (!nm) return;
            double kill = KillClip.Now;
            int killerTeam = MatchManager.TeamOf(nm.LocalClientId);
            int victimTeam = victim == NoVictim ? 1 - killerTeam : MatchManager.TeamOf(victim);
            List<ClipFrame> face = FaceFeed.Instance && FaceFeed.Instance.isActiveAndEnabled
                ? FaceFeed.Instance.Window(kill, Before, After)
                : new List<ClipFrame>();

            if (!ViewRecorder.Instance) return;
            ViewRecorder.Instance.Freeze(kill, Before, After, view =>
            {
                var clip = new KillClip { KillerTeam = killerTeam, VictimTeam = victimTeam };
                clip.View.AddRange(view);
                clip.Face.AddRange(face);
                Play(clip, false);
                if (victim == NoVictim || victim == nm.LocalClientId || !shooter || !shooter.IsSpawned) return;
                var sent = new KillClip { KillerTeam = killerTeam, VictimTeam = victimTeam };
                sent.View.AddRange(view);
                if (ShareFace) sent.Face.AddRange(face);
                shooter.SendKillcam(victim, _nextClipId++, sent.Serialize());
            });
        }

        // ---------- Victim side ----------

        public void ReceiveChunk(int clipId, int index, int count, byte[] chunk)
        {
            byte[] data = _assembler.Add(clipId, index, count, chunk);
            if (data == null) return;
            try { Play(KillClip.Deserialize(data), true); }
            catch (System.Exception e) { Debug.LogWarning($"[PeaHeads] Bad killcam clip: {e.Message}"); }
        }

        // ---------- Playback ----------

        private void Play(KillClip clip, bool forVictim)
        {
            if (!_canvas) Build();
            ClearTextures();
            _clip = clip;
            _forVictim = forVictim;
            (_viewFrames, _viewTimes) = Decode(clip.View);
            (_faceFrames, _faceTimes) = Decode(clip.Face);

            float first = Mathf.Min(_viewTimes.Length > 0 ? _viewTimes[0] : -Before, _faceTimes.Length > 0 ? _faceTimes[0] : -Before);
            _playStart = Time.unscaledTime;
            _slowAt = _playStart + Mathf.Max(0f, SlowFrom - first);
            _freezeAt = _slowAt + -SlowFrom / SlowSpeed;
            _endAt = _freezeAt + FreezeTime + FadeTime;
            _slowPlayed = _freezePlayed = false;
            _startClipTime = Mathf.Min(first, SlowFrom);

            Color killer = Faction.Color(clip.KillerTeam);
            _subtitle.text = forVictim
                ? $"{Faction.Name(clip.KillerTeam)} spat you out"
                : $"You spat out {Faction.Name(clip.VictimTeam)}";
            _subtitle.color = killer;
            _badge.sprite = clip.KillerTeam == 0 ? UiBuild.Flower : UiBuild.Thorn;
            _badge.color = killer;
            bool hasFace = _faceFrames.Length > 0;
            _face.enabled = hasFace;
            _badge.enabled = _noWebcam.enabled = !hasFace;
            _view.enabled = _viewFrames.Length > 0;
        }

        private float ClipTime(float now)
        {
            if (now < _slowAt) return _startClipTime + (now - _playStart);
            if (now < _freezeAt) return SlowFrom + (now - _slowAt) * SlowSpeed;
            return 0f;
        }

        private void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject local = nm ? nm.LocalClient?.PlayerObject : null;
            PlayerHealth health = local ? local.GetComponent<PlayerHealth>() : null;
            bool dead = health && health.IsDead;
            if (dead && !_wasDead) _diedAt = Time.unscaledTime;
            _wasDead = dead;

            float now = Time.unscaledTime;
            bool playing = _clip != null && now < _endAt;
            bool waiting = dead && !playing && now - _diedAt < (health ? health.RespawnDelay : 0f);
            bool fullscreen = playing && _forVictim && dead;
            FullscreenActive = dead && (fullscreen || waiting) && !StoryIntro.IsPlaying;

            if (!_canvas)
            {
                if (!local) return;
                Build();
            }
            bool show = playing && !StoryIntro.IsPlaying; // stays visible over the scope vignette
            _root.gameObject.SetActive(show);
            _waiting.enabled = waiting && !StoryIntro.IsPlaying;
            if (_waiting.enabled) _waiting.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(now * 5f));
            if (!show)
            {
                if (_clip != null && now >= _endAt) { _clip = null; ClearTextures(); }
                return;
            }

            // Full screen for the dead victim; shrinks into the bottom-right corner otherwise.
            _compact = Mathf.MoveTowards(_compact, fullscreen ? 0f : 1f, Time.unscaledDeltaTime * 3f);
            float scale = Mathf.Lerp(1f, CompactScale, Smooth(_compact));
            Rect screen = _canvas.rect;
            Vector2 corner = new(screen.width * 0.5f - 20f - RootSize.x * CompactScale * 0.5f,
                -screen.height * 0.5f + 20f + RootSize.y * CompactScale * 0.5f);
            _root.localScale = Vector3.one * scale;
            _root.anchoredPosition = Vector2.Lerp(new Vector2(0f, -10f), corner, Smooth(_compact));

            float t = ClipTime(now);
            if (_view.enabled) _view.texture = FrameAt(_viewFrames, _viewTimes, t);
            if (_face.enabled) _face.texture = FrameAt(_faceFrames, _faceTimes, t);
            _progress.fillAmount = Mathf.InverseLerp(_playStart, _freezeAt, now);

            if (now >= _slowAt && !_slowPlayed)
            {
                _slowPlayed = true;
                GameAudio.Play(Sfx.Page, 0.8f);
            }
            if (now >= _freezeAt && !_freezePlayed)
            {
                _freezePlayed = true;
                if (_forVictim) GameAudio.Play(Sfx.Kill, 0.8f);
            }
            float sinceFreeze = now - _freezeAt;
            _flash.color = new Color(1f, 1f, 1f, sinceFreeze >= 0f ? Mathf.Clamp01(1f - sinceFreeze / 0.35f) * 0.8f : 0f);
            _group.alpha = Mathf.Clamp01((_endAt - now) / FadeTime);
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private static Texture FrameAt(Texture2D[] frames, float[] times, float t)
        {
            if (frames.Length == 0) return null;
            int best = 0;
            for (int i = 0; i < times.Length; i++)
                if (times[i] <= t) best = i;
            return frames[best];
        }

        private (Texture2D[], float[]) Decode(List<ClipFrame> frames)
        {
            var textures = new List<Texture2D>();
            var times = new List<float>();
            foreach (ClipFrame frame in frames)
            {
                if (frame.Jpeg == null || frame.Jpeg.Length == 0) continue;
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!texture.LoadImage(frame.Jpeg))
                {
                    Destroy(texture);
                    continue;
                }
                _textures.Add(texture);
                textures.Add(texture);
                times.Add(frame.Time);
            }
            return (textures.ToArray(), times.ToArray());
        }

        private void ClearTextures()
        {
            foreach (Texture2D texture in _textures)
                if (texture) Destroy(texture);
            _textures.Clear();
            _viewFrames = _faceFrames = new Texture2D[0];
            _viewTimes = _faceTimes = new float[0];
        }

        // ---------- Build ----------

        private void Build()
        {
            _canvas = UiBuild.Canvas("KillcamCanvas", 42);
            _root = UiBuild.Node(_canvas, "Killcam", Vector2.zero, RootSize);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            Text title = UiBuild.Label(_root, "KILLCAM", 34, Bad, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 194f), new Vector2(600f, 40f));
            UiBuild.Outline(title, new Color(0f, 0f, 0f, 0.6f), 2f);
            _subtitle = UiBuild.Label(_root, "", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 162f), new Vector2(800f, 30f));

            _view = Panel("THE SHOT", -PanelSize.x * 0.5f - 10f, out _);
            _face = Panel("THE FACE", PanelSize.x * 0.5f + 10f, out RectTransform facePanel);
            _badge = UiBuild.Image(facePanel, UiBuild.Flower, new Vector2(0f, 16f), new Vector2(96f, 96f), Color.white);
            _noWebcam = UiBuild.Label(facePanel, "no webcam", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(300f, 24f));

            Image track = UiBuild.Panel(_root, new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(PanelSize.x * 2f + 20f, 8f),
                new Color(0f, 0f, 0f, 0.5f), 0.25f);
            _progress = UiBuild.Image(track.transform, null, Vector2.zero, Vector2.zero, Bad);
            UiBuild.Stretch(_progress.rectTransform);
            _progress.type = Image.Type.Filled;
            _progress.fillMethod = Image.FillMethod.Horizontal;

            _flash = UiBuild.Image(_root, null, Vector2.zero, Vector2.zero, Color.clear);
            UiBuild.Stretch(_flash.rectTransform);
            _root.gameObject.SetActive(false);

            _waiting = UiBuild.Label(_canvas, "Receiving killcam…", 22, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(600f, 30f));
            _waiting.enabled = false;
        }

        private RawImage Panel(string label, float x, out RectTransform panel)
        {
            Image frame = UiBuild.Panel(_root, new Vector2(0.5f, 0.5f), new Vector2(x, -10f), PanelSize + new Vector2(10f, 10f),
                UiBuild.Glass, 0.6f);
            UiBuild.Outline(frame, new Color(1f, 1f, 1f, 0.25f));
            panel = frame.rectTransform;
            RawImage image = UiBuild.Node(panel, "Frames", Vector2.zero, PanelSize).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            UiBuild.Label(panel, label, 16, Color.white, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(4f, 20f),
                new Vector2(200f, 20f));
            return image;
        }
    }
}
