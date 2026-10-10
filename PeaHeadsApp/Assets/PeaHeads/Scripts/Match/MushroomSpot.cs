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
        [SerializeField, Range(0f, 1f)] private float _bobHeight = 0.15f;
        [SerializeField, Range(0f, 10f)] private float _bobSpeed = 2f;
        [SerializeField, Range(0f, 360f)] private float _spinSpeed = 45f;
        [SerializeField, Range(0f, 10f)] private float _glowPulseSpeed = 3f;

        public int Index => _index;

        private float _glowIntensity;
        private Vector3 _visualRest;

        private void Awake()
        {
            _glowIntensity = _glow ? _glow.intensity : 0f;
            _visualRest = _visual.localPosition;
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
            if (!_visual.gameObject.activeSelf) return;
            float t = Time.time;
            _visual.localPosition = _visualRest + Vector3.up * ((Mathf.Sin(t * _bobSpeed) * 0.5f + 0.5f) * _bobHeight);
            _visual.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
            if (_glow) _glow.intensity = _glowIntensity * (0.75f + 0.25f * Mathf.Sin(t * _glowPulseSpeed));
        }
    }
}
