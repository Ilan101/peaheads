using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // Evening lighting plus a haze that leans toward the leading team: golden when Bloom leads, purple when Blight
    // leads, neutral dusk when level. Tint strength = lead ÷ target score. Every camera's sky follows the fog so the
    // horizon blends. Visual only; runs on every machine from the synced scores. Lives on the Match object.
    [RequireComponent(typeof(MatchManager))]
    public class AtmosphereController : MonoBehaviour
    {
        [Header("Evening")]
        [Tooltip("Sun height above the horizon, degrees.")]
        [SerializeField, Range(2f, 60f)] private float _sunElevation = 12f;
        [SerializeField, Range(-180f, 180f)] private float _sunAzimuth = -50f;
        [SerializeField] private Color _sunColor = new(1f, 0.62f, 0.38f);
        [SerializeField, Range(0f, 3f)] private float _sunIntensity = 1.1f;
        [SerializeField] private Color _ambientSky = new(0.6f, 0.5f, 0.55f);
        [SerializeField] private Color _ambientEquator = new(0.55f, 0.42f, 0.38f);
        [SerializeField] private Color _ambientGround = new(0.22f, 0.18f, 0.16f);

        [Header("Fog")]
        [SerializeField, Range(0f, 100f)] private float _fogStart = 20f;
        [SerializeField, Range(5f, 200f)] private float _fogEnd = 70f;
        [SerializeField] private Color _neutralFog = new(0.85f, 0.6f, 0.5f);
        [SerializeField] private Color _bloomFog = new(0.95f, 0.78f, 0.38f);
        [SerializeField] private Color _blightFog = new(0.55f, 0.36f, 0.62f);
        [Tooltip("Tint at a lead equal to the target score (1 = full team colour).")]
        [SerializeField, Range(0f, 1f)] private float _maxTint = 1f;
        [Tooltip("How fast the fog eases toward its new colour.")]
        [SerializeField, Range(0.1f, 10f)] private float _tintSpeed = 1.5f;

        private MatchManager _match;
        private Color _fog;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void Start()
        {
            Light sun = RenderSettings.sun ? RenderSettings.sun : FindSun();
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(_sunElevation, _sunAzimuth, 0f);
                sun.color = _sunColor;
                sun.intensity = _sunIntensity;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
            _fog = _neutralFog;
            Apply();
        }

        private void Update()
        {
            _fog = Color.Lerp(_fog, TargetFog(), 1f - Mathf.Exp(-_tintSpeed * Time.deltaTime));
            Apply();
        }

        private Color TargetFog()
        {
            if (!_match || !_match.IsSpawned || _match.TargetScore <= 0) return _neutralFog;
            float lead = (_match.Score(0) - _match.Score(1)) / (float)_match.TargetScore;
            float tint = Mathf.Clamp01(Mathf.Abs(lead)) * _maxTint;
            return Color.Lerp(_neutralFog, lead >= 0f ? _bloomFog : _blightFog, tint);
        }

        private void Apply()
        {
            RenderSettings.fogColor = _fog;
            foreach (Camera cam in Camera.allCameras)
                if (cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = _fog;
        }

        private static Light FindSun()
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) return light;
            return null;
        }
    }
}
