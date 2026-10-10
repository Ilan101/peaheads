using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    public enum MusicTrack { None, Menu, Story, Game }

    // Crossfades the menu / story / gameplay loops (Audio/Resources/Music, from tools/make_audio.py) to match what's
    // on screen. Quieter while paused and under the end-of-match stinger. GameMenu adds it; runs after GameMenu so
    // the story has already started on the frame a session connects.
    [DefaultExecutionOrder(100)]
    public class MusicPlayer : MonoBehaviour
    {
        private const string MutedKey = "PeaHeads.MusicMuted";

        [SerializeField, Range(0f, 1f)] private float _menuVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float _storyVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float _gameVolume = 0.4f;
        [SerializeField, Range(0.1f, 5f)] private float _fadeTime = 1.5f;
        [SerializeField, Range(0f, 1f)] private float _pausedLevel = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _duckedLevel = 0.25f;

        private static int _muted = -1;

        public static MusicPlayer Instance { get; private set; }

        public static bool Muted
        {
            get
            {
                if (_muted < 0) _muted = PlayerPrefs.GetInt(MutedKey, 0);
                return _muted == 1;
            }
            set
            {
                _muted = value ? 1 : 0;
                PlayerPrefs.SetInt(MutedKey, _muted);
            }
        }

        private readonly Dictionary<MusicTrack, AudioClip> _clips = new();
        private AudioSource _current;
        private AudioSource _previous;
        private MusicTrack _track = MusicTrack.None;
        private float _currentFade;
        private float _previousFade;
        private float _previousVolume;
        private float _level = 1f;
        private float _duckUntil;

        private void Awake()
        {
            Instance = this;
            _current = NewSource();
            _previous = NewSource();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Music drops under a stinger for this long.
        public void Duck(float seconds) => _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + seconds);

        private void Update()
        {
            MusicTrack want = Desired(out bool paused);
            if (want != _track) Switch(want);

            float dt = Time.unscaledDeltaTime;
            float level = Muted ? 0f : paused ? _pausedLevel : Time.unscaledTime < _duckUntil ? _duckedLevel : 1f;
            _level = Mathf.MoveTowards(_level, level, dt / 0.4f);
            _currentFade = Mathf.MoveTowards(_currentFade, 1f, dt / _fadeTime);
            _previousFade = Mathf.MoveTowards(_previousFade, 0f, dt / _fadeTime);
            _current.volume = _currentFade * VolumeOf(_track) * _level;
            _previous.volume = _previousFade * _previousVolume * _level;
            if (_previousFade <= 0f && _previous.isPlaying) _previous.Stop();
        }

        private static MusicTrack Desired(out bool paused)
        {
            paused = false;
            if (StoryIntro.IsPlaying) return MusicTrack.Story;
            NetworkManager nm = NetworkManager.Singleton;
            if (!nm || nm.ShutdownInProgress || !(nm.IsHost || nm.IsConnectedClient)) return MusicTrack.Menu;
            NetworkObject local = nm.LocalClient?.PlayerObject;
            paused = local && local.TryGetComponent(out PlayerInput input) && input.Paused;
            return MusicTrack.Game;
        }

        private void Switch(MusicTrack track)
        {
            (_previous, _current) = (_current, _previous);
            _previousFade = _currentFade;
            _previousVolume = VolumeOf(_track);
            _track = track;
            _currentFade = 0f;
            _current.clip = Clip(track);
            if (!_current.clip) return;
            _current.time = 0f;
            _current.Play();
        }

        private AudioClip Clip(MusicTrack track)
        {
            if (track == MusicTrack.None) return null;
            if (!_clips.TryGetValue(track, out AudioClip clip))
                _clips[track] = clip = Resources.Load<AudioClip>($"Music/{track.ToString().ToLowerInvariant()}");
            return clip;
        }

        private float VolumeOf(MusicTrack track) => track switch
        {
            MusicTrack.Menu => _menuVolume,
            MusicTrack.Story => _storyVolume,
            MusicTrack.Game => _gameVolume,
            _ => 0f,
        };

        private AudioSource NewSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.ignoreListenerPause = true;
            source.volume = 0f;
            return source;
        }
    }
}
