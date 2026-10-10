using UnityEngine;

namespace PeaHeads
{
    // One mushroom pickup location. Visual only; MushroomSpawner decides when it is shown.
    public class MushroomSpot : MonoBehaviour
    {
        [SerializeField] private int _index;
        [SerializeField] private Transform _visual;
        [SerializeField] private Light _glow;
        [SerializeField] private ParticleSystem _sparkles;
        [SerializeField] private AudioClip _collectSfx;
        [SerializeField, Range(0f, 10f)] private float _glowPulseSpeed = 3f;

        public int Index => _index;
        public bool IsShown => _visual && _visual.gameObject.activeSelf;

        private float _glowIntensity;

        private void Awake()
        {
            _glowIntensity = _glow ? _glow.intensity : 0f;
            Hide();
        }

        public void Show()
        {
            _visual.gameObject.SetActive(true);
            if (_glow) _glow.enabled = true;
            if (_sparkles) _sparkles.Play();
        }

        public void Hide()
        {
            _visual.gameObject.SetActive(false);
            if (_glow) _glow.enabled = false;
            if (_sparkles) _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void PlayCollect()
        {
            if (_sparkles) _sparkles.Emit(25);
            if (_collectSfx) AudioSource.PlayClipAtPoint(_collectSfx, transform.position);
        }

        private void Update()
        {
            if (!_glow || !_glow.enabled) return;
            _glow.intensity = _glowIntensity * (0.75f + 0.25f * Mathf.Sin(Time.time * _glowPulseSpeed));
        }
    }
}
