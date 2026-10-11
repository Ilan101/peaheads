using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace PeaHeads
{
    // Killcam. The killer sees only their own face (FaceFeed) around the kill: it starts the moment the kill lands,
    // trailing real time by a second so the frames already exist, slows down over the hit and runs on past it.
    // The victim gets the killer's view (ViewRecorder) and face sent over the network (PlayerShooting.SendKillcam)
    // and sees the shot and the face side by side full screen over the death screen, shrinking to the corner once they
    // respawn. Smooth playback: frames are decoded a little ahead of the playhead under a per-frame time budget, and in
    // slow motion consecutive frames crossfade so it glides instead of stepping. Added by GameMenu.
    public class KillcamView : MonoBehaviour
    {
        public const ulong NoVictim = ulong.MaxValue;
        private const string ShareFaceKey = "PeaHeads.KillcamFace";
        private const float FadeTime = 0.4f;
        private const float CompactScale = 0.42f;
        // Victim clip: what the killer records and sends.
        private const float ClipBefore = 1.5f, ClipAfter = 1.5f;
        // Decode this far ahead of the playhead, spending at most this much main-thread time per frame on it.
        private const float Lookahead = 0.6f;
        private const float DecodeBudgetMs = 4f;
        // The victim's clip starts once this much of it is decoded (or all of it).
        private const float Prebuffer = 0.5f;
        private const float SlowZoom = 1.07f;
        private static readonly Timeline VictimTimeline = new(-ClipBefore, ClipAfter, -0.4f, 0.4f, 0.4f);
        private static readonly Timeline KillerTimeline = new(-1f, 1.6f, -0.3f, 0.5f, 0.5f);
        private static readonly Vector2 PanelSize = new(540f, 304f);
        private static readonly Vector2 VictimSize = new(1110f, 440f);
        private static readonly Vector2 KillerPanelSize = new(400f, 225f);
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

        // Clip time (seconds from the kill) for playback time r: normal speed, slow over [slowFrom, slowTo], normal again.
        private readonly struct Timeline
        {
            public readonly float Start, End, SlowFrom, SlowTo, SlowSpeed;

            public Timeline(float start, float end, float slowFrom, float slowTo, float slowSpeed)
            {
                Start = start;
                End = end;
                SlowFrom = slowFrom;
                SlowTo = slowTo;
                SlowSpeed = slowSpeed;
            }

            private float Normal1 => SlowFrom - Start;
            private float Slow => (SlowTo - SlowFrom) / SlowSpeed;
            public float Duration => Normal1 + Slow + (End - SlowTo);

            public float ClipTime(float r)
            {
                if (r < Normal1) return Start + r;
                if (r < Normal1 + Slow) return SlowFrom + (r - Normal1) * SlowSpeed;
                return Mathf.Min(End, SlowTo + (r - Normal1 - Slow));
            }

            // 0 outside the slow section, easing up to 1 in its middle (drives the slow-motion zoom).
            public float SlowAmount(float clipTime)
            {
                if (clipTime <= SlowFrom || clipTime >= SlowTo) return 0f;
                return Mathf.Sin((clipTime - SlowFrom) / (SlowTo - SlowFrom) * Mathf.PI);
            }

            // How much consecutive frames crossfade: fully in slow motion (each frame is held for several screen
            // frames), none at normal speed (fast camera moves would ghost), easing over 0.1 s of clip time between.
            public float Smoothing(float clipTime)
            {
                float outside = Mathf.Max(SlowFrom - clipTime, clipTime - SlowTo, 0f);
                return Mathf.Clamp01(1f - outside / 0.1f);
            }
        }

        // One panel's frames. JPEGs are decoded into pooled textures just ahead of the playhead and dropped behind
        // it; the panel shows the frame at the playhead with the next one crossfaded on top.
        private sealed class Track
        {
            public RawImage Back;
            public RawImage Front;
            private List<ClipFrame> _frames = new();
            private readonly Dictionary<int, Texture2D> _decoded = new();
            private readonly Stack<Texture2D> _pool = new();
            private readonly List<int> _drop = new();

            public bool HasFrames => _frames.Count > 0;

            // New clip: every decoded texture goes back to the pool.
            public void Reset(List<ClipFrame> frames)
            {
                foreach (Texture2D texture in _decoded.Values) _pool.Push(texture);
                _decoded.Clear();
                _frames = frames;
                if (Front) Front.enabled = false;
            }

            // Live clip (killer): the window only grows at the end, so decoded indices stay valid.
            public void Refresh(List<ClipFrame> frames) => _frames = frames;

            // Decodes frames covering [clipTime, clipTime + ahead] in order; false once the budget runs out.
            public bool Prefetch(float clipTime, float ahead, Stopwatch budget)
            {
                int first = Index(clipTime);
                for (int i = first; i < _frames.Count && _frames[i].Time <= clipTime + ahead; i++)
                {
                    if (_decoded.ContainsKey(i)) continue;
                    if (budget.Elapsed.TotalMilliseconds > DecodeBudgetMs) return false;
                    Decode(i);
                }
                return true;
            }

            // Decoded up to clipTime + ahead (or to the end of the clip).
            public bool Buffered(float clipTime, float ahead)
            {
                for (int i = Index(clipTime); i < _frames.Count && _frames[i].Time <= clipTime + ahead; i++)
                    if (!_decoded.ContainsKey(i)) return false;
                return true;
            }

            public void Show(float clipTime, float zoom, float smoothing)
            {
                if (_frames.Count == 0) return;
                int i = Index(clipTime);
                if (!_decoded.ContainsKey(i)) Decode(i); // only if the budget fell behind: never show a blank
                Back.texture = _decoded[i];
                int j = i + 1;
                float blend = 0f;
                if (j < _frames.Count && _decoded.TryGetValue(j, out Texture2D next))
                {
                    float span = _frames[j].Time - _frames[i].Time;
                    float fraction = span > 0.0001f ? Mathf.Clamp01((clipTime - _frames[i].Time) / span) : 0f;
                    blend = Mathf.Lerp(fraction >= 0.5f ? 1f : 0f, fraction, smoothing);
                    Front.texture = next;
                }
                Front.color = new Color(1f, 1f, 1f, blend);
                Front.enabled = blend > 0.001f;
                Rect uv = Zoomed(zoom);
                Back.uvRect = uv;
                Front.uvRect = uv;

                // Behind the playhead: back to the pool.
                _drop.Clear();
                foreach (int index in _decoded.Keys)
                    if (index < i - 1) _drop.Add(index);
                foreach (int index in _drop)
                {
                    _pool.Push(_decoded[index]);
                    _decoded.Remove(index);
                }
            }

            public void Dispose()
            {
                foreach (Texture2D texture in _decoded.Values) Destroy(texture);
                foreach (Texture2D texture in _pool) Destroy(texture);
                _decoded.Clear();
                _pool.Clear();
            }

            // Last frame at or before clipTime (the first one before the clip starts).
            private int Index(float clipTime)
            {
                int best = 0;
                for (int i = 0; i < _frames.Count && _frames[i].Time <= clipTime; i++) best = i;
                return best;
            }

            private void Decode(int index)
            {
                Texture2D texture = _pool.Count > 0
                    ? _pool.Pop()
                    : new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                byte[] jpeg = _frames[index].Jpeg;
                if (jpeg != null && jpeg.Length > 0) texture.LoadImage(jpeg);
                _decoded[index] = texture;
            }

            private static Rect Zoomed(float zoom)
            {
                float size = 1f / zoom;
                return new Rect((1f - size) * 0.5f, (1f - size) * 0.5f, size, size);
            }
        }

        private sealed class Playback
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Timeline Timeline;
            public float StartedAt = float.NegativeInfinity;

            public bool Playing(float now) => now - StartedAt < Timeline.Duration + FadeTime;
            public float ClipTime(float now) => Timeline.ClipTime(Mathf.Max(0f, now - StartedAt));

            // Fade in at the start, out at the end.
            public void Animate(float now)
            {
                float r = now - StartedAt;
                Group.alpha = Mathf.Clamp01(Mathf.Min(r / 0.2f, (Timeline.Duration + FadeTime - r) / FadeTime));
            }
        }

        private readonly ClipAssembler _assembler = new();
        private readonly Stopwatch _budget = new();
        private int _nextClipId;
        private readonly Playback _killer = new() { Timeline = KillerTimeline };
        private readonly Playback _victim = new() { Timeline = VictimTimeline };
        private readonly Track _killerFace = new();
        private readonly Track _victimView = new(), _victimFace = new();
        private double _killAt;
        // A received clip waiting for its first frames to decode.
        private bool _victimBuffering;
        private bool _victimSlowPlayed;
        private bool _wasDead;
        private float _diedAt = float.NegativeInfinity;
        private float _compact = 1f;

        private RectTransform _canvas;
        private Image _victimBadge;

        private void Awake()
        {
            Instance = this;
            _nextClipId = Random.Range(0, 1 << 20);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            FullscreenActive = false;
            _killerFace.Dispose();
            _victimView.Dispose();
            _victimFace.Dispose();
            if (_canvas) Destroy(_canvas.gameObject);
        }

        // ---------- Killer side ----------

        // Owner of the shooting plant: one of its peas just knocked out `victim` (NoVictim for dummies).
        public void OnLocalKill(PlayerShooting shooter, ulong victim)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (!nm) return;
            if (!_canvas) Build();
            double kill = KillClip.Now;
            int killerTeam = MatchManager.TeamOf(nm.LocalClientId);
            int victimTeam = victim == NoVictim ? 1 - killerTeam : MatchManager.TeamOf(victim);

            FaceFeed feed = FaceFeed.Instance && FaceFeed.Instance.isActiveAndEnabled ? FaceFeed.Instance : null;
            if (feed && feed.Live)
            {
                _killAt = kill;
                _killerFace.Reset(feed.Window(kill, -KillerTimeline.Start, KillerTimeline.End));
                _killer.StartedAt = Time.unscaledTime;
            }

            if (victim == NoVictim || victim == nm.LocalClientId || !ViewRecorder.Instance) return;
            ViewRecorder.Instance.Collect(kill, ClipBefore, ClipAfter, view =>
            {
                if (!shooter || !shooter.IsSpawned) return;
                var clip = new KillClip { KillerTeam = killerTeam, VictimTeam = victimTeam };
                clip.View.AddRange(view);
                if (ShareFace && feed) clip.Face.AddRange(feed.Window(kill, ClipBefore, ClipAfter));
                shooter.SendKillcam(victim, _nextClipId++, clip.Serialize());
            });
        }

        // ---------- Victim side ----------

        public void ReceiveChunk(int clipId, int index, int count, byte[] chunk)
        {
            byte[] data = _assembler.Add(clipId, index, count, chunk);
            if (data == null) return;
            try { PlayVictim(KillClip.Deserialize(data)); }
            catch (System.Exception e) { Debug.LogWarning($"[PeaHeads] Bad killcam clip: {e.Message}"); }
        }

        private void PlayVictim(KillClip clip)
        {
            if (!_canvas) Build();
            _victimView.Reset(clip.View);
            _victimFace.Reset(clip.Face);
            _victimBuffering = true;
            _victim.StartedAt = float.NegativeInfinity;
            _victimSlowPlayed = false;
            Color killer = Faction.Color(clip.KillerTeam);
            _victimBadge.sprite = clip.KillerTeam == 0 ? UiBuild.Flower : UiBuild.Thorn;
            _victimBadge.color = killer;
            bool hasFace = _victimFace.HasFrames;
            _victimFace.Back.enabled = hasFace;
            _victimBadge.enabled = !hasFace;
            _victimView.Back.enabled = _victimView.HasFrames;
        }

        // ---------- Update ----------

        private void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject local = nm ? nm.LocalClient?.PlayerObject : null;
            PlayerHealth health = local ? local.GetComponent<PlayerHealth>() : null;
            bool dead = health && health.IsDead;
            if (dead && !_wasDead) _diedAt = Time.unscaledTime;
            _wasDead = dead;

            float now = Time.unscaledTime;
            _budget.Restart();
            if (_victimBuffering) Buffer(now);

            bool story = LobbyView.HidesGame;
            bool victimPlaying = _victim.Playing(now);
            bool waiting = dead && !victimPlaying && now - _diedAt < (health ? health.RespawnDelay : 0f);
            bool fullscreen = victimPlaying && dead;
            FullscreenActive = dead && (fullscreen || waiting) && !story;

            if (!_canvas)
            {
                if (!local) return;
                Build();
            }

            // Both stay visible over the scope vignette.
            UpdateVictim(now, victimPlaying && !story, fullscreen);
            UpdateKiller(now, _killer.Playing(now) && !story && !dead);
        }

        // Decode the opening of a received clip, then start it.
        private void Buffer(float now)
        {
            float start = VictimTimeline.Start;
            _victimView.Prefetch(start, Prebuffer, _budget);
            _victimFace.Prefetch(start, Prebuffer, _budget);
            if (!_victimView.Buffered(start, Prebuffer) || !_victimFace.Buffered(start, Prebuffer)) return;
            _victimBuffering = false;
            _victim.StartedAt = now;
        }

        private void UpdateKiller(float now, bool show)
        {
            _killer.Root.gameObject.SetActive(show);
            if (!show) return;
            // Frames arrive live; the playback trails real time, so the ones it needs are already here.
            FaceFeed feed = FaceFeed.Instance;
            if (feed) _killerFace.Refresh(feed.Window(_killAt, -KillerTimeline.Start, KillerTimeline.End));
            float t = _killer.ClipTime(now);
            _killerFace.Prefetch(t, Lookahead, _budget);
            _killerFace.Show(t, 1f + (SlowZoom - 1f) * KillerTimeline.SlowAmount(t), KillerTimeline.Smoothing(t));
            _killer.Animate(now);
        }

        private void UpdateVictim(float now, bool show, bool fullscreen)
        {
            _victim.Root.gameObject.SetActive(show);
            if (!show) return;

            // Full screen for the dead victim; shrinks into the bottom-right corner once they're back.
            _compact = Mathf.MoveTowards(_compact, fullscreen ? 0f : 1f, Time.unscaledDeltaTime * 3f);
            float k = _compact * _compact * (3f - 2f * _compact);
            Rect screen = _canvas.rect;
            Vector2 corner = new(screen.width * 0.5f - 20f - VictimSize.x * CompactScale * 0.5f,
                -screen.height * 0.5f + 20f + VictimSize.y * CompactScale * 0.5f);
            _victim.Root.localScale = Vector3.one * Mathf.Lerp(1f, CompactScale, k);
            // Full-screen spot: just under the scoreboard; the death text sits below it (PlayerHUD).
            _victim.Root.anchoredPosition = Vector2.Lerp(new Vector2(0f, 0f), corner, k);

            float t = _victim.ClipTime(now);
            _victimView.Prefetch(t, Lookahead, _budget);
            _victimFace.Prefetch(t, Lookahead, _budget);
            float zoom = 1f + (SlowZoom - 1f) * VictimTimeline.SlowAmount(t);
            float smoothing = VictimTimeline.Smoothing(t);
            _victimView.Show(t, zoom, smoothing);
            _victimFace.Show(t, 1f, smoothing);
            if (t >= VictimTimeline.SlowFrom && !_victimSlowPlayed)
            {
                _victimSlowPlayed = true;
                GameAudio.Play(Sfx.Page, 0.8f);
            }
            _victim.Animate(now);
        }

        // ---------- Build ----------

        private void Build()
        {
            _canvas = UiBuild.Canvas("KillcamCanvas", 42);

            // Victim: two panels, title, progress.
            BuildRoot(_victim, VictimSize, 210f);
            Panel(_victim.Root, new Vector2(-PanelSize.x * 0.5f - 10f, -6f), PanelSize, _victimView, out _);
            Panel(_victim.Root, new Vector2(PanelSize.x * 0.5f + 10f, -6f), PanelSize, _victimFace, out RectTransform facePanel);
            _victimBadge = UiBuild.Image(facePanel, UiBuild.Flower, new Vector2(0f, 16f), new Vector2(96f, 96f), Color.white);

            // Killer: one face panel in the bottom-right corner.
            Vector2 killerSize = KillerPanelSize + new Vector2(20f, 90f);
            BuildRoot(_killer, killerSize, killerSize.y * 0.5f - 18f);
            _killer.Root.anchorMin = _killer.Root.anchorMax = _killer.Root.pivot = new Vector2(1f, 0f);
            _killer.Root.anchoredPosition = new Vector2(-20f, 20f);
            Panel(_killer.Root, new Vector2(0f, -4f), KillerPanelSize, _killerFace, out _);
        }

        private void BuildRoot(Playback playback, Vector2 size, float titleY)
        {
            playback.Root = UiBuild.Node(_canvas, "Killcam", Vector2.zero, size);
            playback.Group = playback.Root.gameObject.AddComponent<CanvasGroup>();
            playback.Group.blocksRaycasts = false;
            bool big = size.x > 800f;
            // The only text: "KILL CAM" for both killer and victim.
            Text title = UiBuild.Label(playback.Root, "KILL CAM", big ? 34 : 22, Bad, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, titleY), new Vector2(600f, 40f));
            UiBuild.Outline(title, new Color(0f, 0f, 0f, 0.6f), 2f);
            UiBuild.Display(title);
            playback.Root.gameObject.SetActive(false);
        }

        // Rounded glass frame with two stacked image layers (frame and the next one crossfading over it).
        private static void Panel(RectTransform root, Vector2 pos, Vector2 size, Track track, out RectTransform panel)
        {
            Image frame = UiBuild.Panel(root, new Vector2(0.5f, 0.5f), pos, size + new Vector2(10f, 10f), UiBuild.Glass, 0.6f);
            UiBuild.Rim(frame, new Color(1f, 1f, 1f, 0.3f));
            panel = frame.rectTransform;
            track.Back = UiBuild.Node(panel, "Frames", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            track.Back.raycastTarget = false;
            track.Front = UiBuild.Node(panel, "Next", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            track.Front.raycastTarget = false;
            track.Front.enabled = false;
        }
    }
}
