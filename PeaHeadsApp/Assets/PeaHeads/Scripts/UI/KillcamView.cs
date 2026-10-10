using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PeaHeads
{
    // Killcam. The killer sees only their own face (FaceFeed) around the kill: it starts the moment the kill lands,
    // trailing real time by a second so the frames already exist, slows down over the hit and runs on past it.
    // The victim gets the killer's view (ViewRecorder) and face sent over the network (PlayerShooting.SendKillcam)
    // and sees "THE SHOT" | "THE FACE" full screen over the death screen, shrinking to the corner once they respawn.
    // Frames are decoded one at a time as they're shown, so playback never hitches. Added by GameMenu.
    public class KillcamView : MonoBehaviour
    {
        public const ulong NoVictim = ulong.MaxValue;
        private const string ShareFaceKey = "PeaHeads.KillcamFace";
        private const float FadeTime = 0.4f;
        private const float CompactScale = 0.42f;
        // Victim clip: what the killer records and sends.
        private const float ClipBefore = 1.5f, ClipAfter = 1.5f;
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
        }

        // One panel's frames, decoded into a single reused texture only when the shown frame changes.
        private sealed class Track
        {
            public RawImage Image;
            public List<ClipFrame> Frames = new();
            private Texture2D _texture;
            private ClipFrame _shown;

            public bool HasFrames => Frames.Count > 0;

            public void Show(float clipTime)
            {
                if (Frames.Count == 0) return;
                int best = 0;
                for (int i = 0; i < Frames.Count; i++)
                    if (Frames[i].Time <= clipTime) best = i;
                ClipFrame frame = Frames[best];
                if (frame.Jpeg == _shown.Jpeg) return;
                _shown = frame;
                if (!_texture) _texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
                if (frame.Jpeg != null && frame.Jpeg.Length > 0 && _texture.LoadImage(frame.Jpeg)) Image.texture = _texture;
            }

            public void Reset(List<ClipFrame> frames)
            {
                Frames = frames;
                _shown = default;
            }

            public void Dispose()
            {
                if (_texture) Destroy(_texture);
            }
        }

        private sealed class Playback
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Flash;
            public Image Progress;
            public Text Subtitle;
            public Timeline Timeline;
            public float StartedAt = float.NegativeInfinity;

            public bool Playing(float now) => now - StartedAt < Timeline.Duration + FadeTime;
            public float ClipTime(float now) => Timeline.ClipTime(now - StartedAt);

            // Flash on the hit, progress bar, fade at the end.
            public void Animate(float now)
            {
                float r = now - StartedAt;
                float sinceHit = r - (Timeline.SlowFrom - Timeline.Start) - -Timeline.SlowFrom / Timeline.SlowSpeed;
                Flash.color = new Color(1f, 1f, 1f, sinceHit >= 0f ? Mathf.Clamp01(1f - sinceHit / 0.35f) * 0.75f : 0f);
                Progress.fillAmount = Mathf.Clamp01(r / Timeline.Duration);
                Group.alpha = Mathf.Clamp01((Timeline.Duration + FadeTime - r) / FadeTime);
            }
        }

        private readonly ClipAssembler _assembler = new();
        private int _nextClipId;
        private readonly Playback _killer = new() { Timeline = KillerTimeline };
        private readonly Playback _victim = new() { Timeline = VictimTimeline };
        private readonly Track _killerFace = new();
        private readonly Track _victimView = new(), _victimFace = new();
        private double _killAt;
        private bool _victimSlowPlayed;
        private bool _wasDead;
        private float _diedAt = float.NegativeInfinity;
        private float _compact = 1f;

        private RectTransform _canvas;
        private Image _victimBadge;
        private Text _victimNoWebcam, _waiting;

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
                _killerFace.Reset(new List<ClipFrame>());
                _killer.StartedAt = Time.unscaledTime;
                _killer.Subtitle.text = $"You spat out {Faction.Name(victimTeam)}";
                _killer.Subtitle.color = Faction.Color(killerTeam);
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
            _victim.StartedAt = Time.unscaledTime;
            _victimSlowPlayed = false;
            Color killer = Faction.Color(clip.KillerTeam);
            _victim.Subtitle.text = $"{Faction.Name(clip.KillerTeam)} spat you out";
            _victim.Subtitle.color = killer;
            _victimBadge.sprite = clip.KillerTeam == 0 ? UiBuild.Flower : UiBuild.Thorn;
            _victimBadge.color = killer;
            bool hasFace = _victimFace.HasFrames;
            _victimFace.Image.enabled = hasFace;
            _victimBadge.enabled = _victimNoWebcam.enabled = !hasFace;
            _victimView.Image.enabled = _victimView.HasFrames;
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
            bool story = StoryIntro.IsPlaying;
            bool victimPlaying = _victim.Playing(now);
            bool waiting = dead && !victimPlaying && now - _diedAt < (health ? health.RespawnDelay : 0f);
            bool fullscreen = victimPlaying && dead;
            FullscreenActive = dead && (fullscreen || waiting) && !story;

            if (!_canvas)
            {
                if (!local) return;
                Build();
            }
            _waiting.enabled = waiting && !story;
            if (_waiting.enabled) _waiting.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(now * 5f));

            // Both stay visible over the scope vignette.
            UpdateVictim(now, victimPlaying && !story, fullscreen);
            UpdateKiller(now, _killer.Playing(now) && !story && !dead);
        }

        private void UpdateKiller(float now, bool show)
        {
            _killer.Root.gameObject.SetActive(show);
            if (!show) return;
            // Frames arrive live; the playback trails real time, so the ones it needs are already here.
            FaceFeed feed = FaceFeed.Instance;
            if (feed) _killerFace.Frames = feed.Window(_killAt, -KillerTimeline.Start, KillerTimeline.End);
            _killerFace.Show(_killer.ClipTime(now));
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
            _victim.Root.anchoredPosition = Vector2.Lerp(new Vector2(0f, -10f), corner, k);

            float t = _victim.ClipTime(now);
            _victimView.Show(t);
            _victimFace.Show(t);
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
            _victimView.Image = Panel(_victim.Root, "THE SHOT", new Vector2(-PanelSize.x * 0.5f - 10f, -6f), PanelSize, out _);
            _victimFace.Image = Panel(_victim.Root, "THE FACE", new Vector2(PanelSize.x * 0.5f + 10f, -6f), PanelSize,
                out RectTransform facePanel);
            _victimBadge = UiBuild.Image(facePanel, UiBuild.Flower, new Vector2(0f, 16f), new Vector2(96f, 96f), Color.white);
            _victimNoWebcam = UiBuild.Label(facePanel, "no webcam", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(300f, 24f));
            BuildProgress(_victim, new Vector2(0f, -186f), PanelSize.x * 2f + 20f);

            // Killer: one face panel in the bottom-right corner.
            Vector2 killerSize = KillerPanelSize + new Vector2(20f, 90f);
            BuildRoot(_killer, killerSize, killerSize.y * 0.5f - 18f);
            _killer.Root.anchorMin = _killer.Root.anchorMax = _killer.Root.pivot = new Vector2(1f, 0f);
            _killer.Root.anchoredPosition = new Vector2(-20f, 20f);
            _killerFace.Image = Panel(_killer.Root, "YOUR REACTION", new Vector2(0f, -4f), KillerPanelSize, out _);
            BuildProgress(_killer, new Vector2(0f, -killerSize.y * 0.5f + 14f), KillerPanelSize.x);

            _waiting = UiBuild.Label(_canvas, "Receiving killcam…", 22, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 30f));
            _waiting.enabled = false;
        }

        private void BuildRoot(Playback playback, Vector2 size, float titleY)
        {
            playback.Root = UiBuild.Node(_canvas, "Killcam", Vector2.zero, size);
            playback.Group = playback.Root.gameObject.AddComponent<CanvasGroup>();
            playback.Group.blocksRaycasts = false;
            bool big = size.x > 800f;
            Text title = UiBuild.Label(playback.Root, "KILLCAM", big ? 34 : 22, Bad, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, titleY), new Vector2(600f, 40f));
            UiBuild.Outline(title, new Color(0f, 0f, 0f, 0.6f), 2f);
            playback.Subtitle = UiBuild.Label(playback.Root, "", big ? 22 : 15, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, titleY - (big ? 32f : 22f)), new Vector2(800f, 30f));
            playback.Root.gameObject.SetActive(false);
        }

        private static void BuildProgress(Playback playback, Vector2 pos, float width)
        {
            Image track = UiBuild.Panel(playback.Root, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 6f),
                new Color(0f, 0f, 0f, 0.5f), 0.2f);
            playback.Progress = UiBuild.Image(track.transform, null, Vector2.zero, Vector2.zero, Bad);
            UiBuild.Stretch(playback.Progress.rectTransform);
            playback.Progress.type = Image.Type.Filled;
            playback.Progress.fillMethod = Image.FillMethod.Horizontal;
            playback.Flash = UiBuild.Image(playback.Root, null, Vector2.zero, Vector2.zero, Color.clear);
            UiBuild.Stretch(playback.Flash.rectTransform);
        }

        private static RawImage Panel(RectTransform root, string label, Vector2 pos, Vector2 size, out RectTransform panel)
        {
            Image frame = UiBuild.Panel(root, new Vector2(0.5f, 0.5f), pos, size + new Vector2(10f, 10f), UiBuild.Glass, 0.6f);
            UiBuild.Outline(frame, new Color(1f, 1f, 1f, 0.25f));
            panel = frame.rectTransform;
            RawImage image = UiBuild.Node(panel, "Frames", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            UiBuild.Label(panel, label, 15, Color.white, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(4f, 20f),
                new Vector2(260f, 20f));
            return image;
        }
    }
}
