using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // Afternoon lighting, a generated cloudy skybox, and a haze that builds as the leading team nears the win: from
    // ~30% of the target score the fog takes on the leader's colour (golden Bloom, purple Blight) and thickens, both
    // at full strength by ~90% (18 of 20). Level scores thicken it without colour. The sky's horizon
    // matches the fog so distant hills blend in; the clouds drift slowly. Visual only; runs on every machine from the
    // synced scores. Added to the Match object by MatchManager.
    [RequireComponent(typeof(MatchManager))]
    public class AtmosphereController : MonoBehaviour
    {
        private const int SkyWidth = 1024;
        private const int SkyHeight = 512;

        [Header("Daylight")]
        [Tooltip("Sun height above the horizon, degrees.")]
        [SerializeField, Range(2f, 89f)] private float _sunElevation = 37.2f;
        [SerializeField, Range(-180f, 180f)] private float _sunAzimuth = -50f;
        [SerializeField] private Color _sunColor = new(1f, 0.89f, 0.76f);
        [SerializeField, Range(0f, 3f)] private float _sunIntensity = 1.33f;
        [SerializeField] private Color _ambientSky = new(0.71f, 0.75f, 0.85f);
        [SerializeField] private Color _ambientEquator = new(0.61f, 0.6f, 0.56f);
        [SerializeField] private Color _ambientGround = new(0.31f, 0.29f, 0.25f);

        [Header("Sky")]
        [SerializeField] private Color _zenith = new(0.3f, 0.52f, 0.85f);
        [Tooltip("0 = clear sky, 1 = overcast.")]
        [SerializeField, Range(0f, 1f)] private float _cloudCover = 0.45f;
        [SerializeField, Range(0.1f, 3f)] private float _cloudScale = 1f;
        [Tooltip("Cloud drift, degrees per second around the sky.")]
        [SerializeField, Range(0f, 5f)] private float _cloudDrift = 0.6f;

        [Header("Fog")]
        [SerializeField, Range(0f, 100f)] private float _fogStart = 20f;
        [SerializeField, Range(5f, 200f)] private float _fogEnd = 70f;
        [Tooltip("Fog distances once the leader is close to winning.")]
        [SerializeField, Range(0f, 100f)] private float _closeFogStart = 8f;
        [SerializeField, Range(5f, 200f)] private float _closeFogEnd = 40f;
        [Tooltip("Leader's score ÷ target where the colour and thickening begin.")]
        [SerializeField, Range(0f, 1f)] private float _buildFrom = 0.3f;
        [Tooltip("Leader's score ÷ target where they reach full strength (0.9 = 18 of 20).")]
        [SerializeField, Range(0f, 1f)] private float _fullAt = 0.9f;
        [Tooltip("Level-score haze; also the sky colour at the horizon.")]
        [SerializeField] private Color _neutralFog = new(0.79f, 0.82f, 0.86f);
        [SerializeField] private Color _bloomFog = new(0.95f, 0.85f, 0.5f);
        [SerializeField] private Color _blightFog = new(0.7f, 0.55f, 0.8f);
        [Tooltip("Colour strength at full build-up (1 = full team colour).")]
        [SerializeField, Range(0f, 1f)] private float _maxTint = 1f;
        [Tooltip("How much of the team tint reaches the whole sky (the horizon always follows the fog).")]
        [SerializeField, Range(0f, 1f)] private float _skyTint = 0.5f;
        [Tooltip("How fast the fog eases toward its new colour and thickness.")]
        [SerializeField, Range(0.1f, 10f)] private float _tintSpeed = 1.5f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");

        private MatchManager _match;
        private Material _sky;
        private Color _fog;
        private float _build;

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

            // ponytail: Shader.Find works in the editor; a player build needs the shader referenced by an asset
            // (e.g. a skybox material in the scene) or added to Always Included Shaders.
            Shader panoramic = Shader.Find("Skybox/Panoramic");
            if (panoramic)
            {
                _sky = new Material(panoramic) { mainTexture = BuildSkyTexture() };
                RenderSettings.skybox = _sky;
            }
            _fog = _neutralFog;
            Apply();
        }

        private void OnDestroy()
        {
            if (!_sky) return;
            Destroy(_sky.mainTexture);
            Destroy(_sky);
        }

        private void Update()
        {
            float blend = 1f - Mathf.Exp(-_tintSpeed * Time.deltaTime);
            Targets(out Color color, out float build);
            _fog = Color.Lerp(_fog, color, blend);
            _build = Mathf.Lerp(_build, build, blend);
            RenderSettings.fogStartDistance = Mathf.Lerp(_fogStart, _closeFogStart, _build);
            RenderSettings.fogEndDistance = Mathf.Lerp(_fogEnd, _closeFogEnd, _build);
            Apply();
        }

        // build: 0..1 from how close the leading score is to the target. Colour only when someone is ahead.
        private void Targets(out Color color, out float build)
        {
            color = _neutralFog;
            build = 0f;
            if (!_match || !_match.IsSpawned || _match.TargetScore <= 0) return;
            int bloom = _match.Score(0), blight = _match.Score(1);
            float progress = Mathf.Max(bloom, blight) / (float)_match.TargetScore;
            build = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_buildFrom, _fullAt, progress));
            if (bloom != blight) color = Color.Lerp(_neutralFog, bloom > blight ? _bloomFog : _blightFog, build * _maxTint);
        }

        private void Apply()
        {
            RenderSettings.fogColor = _fog;
            if (_sky)
            {
                // Panoramic tint is ×2: grey 0.5 = untinted. Shift it by the fog's change from neutral.
                Color shift = (_fog - _neutralFog) * _skyTint;
                _sky.SetColor(TintId, new Color(0.5f + shift.r * 0.5f, 0.5f + shift.g * 0.5f, 0.5f + shift.b * 0.5f));
                _sky.SetFloat(RotationId, Mathf.Repeat(Time.time * _cloudDrift, 360f));
            }
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam.cullingMask == 0 || cam.GetComponentInParent<LobbyStage>()) continue;
                if (_sky && cam.clearFlags == CameraClearFlags.SolidColor) cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = _fog;
            }
        }

        // Equirectangular sky: horizon haze (= neutral fog) to zenith blue, and a cloud deck (fractal noise projected
        // onto a plane overhead, so it has no seam and thins out toward the horizon). No sun glow: the sky rotates.
        private Texture2D BuildSkyTexture()
        {
            var pixels = new Color32[SkyWidth * SkyHeight];
            float threshold = Mathf.Lerp(0.75f, 0.35f, _cloudCover);
            for (int y = 0; y < SkyHeight; y++)
            {
                float elevation = (y + 0.5f) / SkyHeight * Mathf.PI - Mathf.PI * 0.5f;
                float up = Mathf.Sin(elevation), flat = Mathf.Cos(elevation);
                for (int x = 0; x < SkyWidth; x++)
                {
                    float azimuth = (x + 0.5f) / SkyWidth * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(azimuth) * flat, up, Mathf.Cos(azimuth) * flat);
                    Color color = up <= 0f ? _neutralFog : Color.Lerp(_neutralFog, _zenith, Mathf.Pow(up, 0.6f));

                    if (up > 0.02f)
                    {
                        float px = dir.x / up * 0.35f * _cloudScale, pz = dir.z / up * 0.35f * _cloudScale;
                        float noise = Fbm(px + 40f, pz + 40f);
                        float cloud = Mathf.SmoothStep(threshold, threshold + 0.25f, noise) * Mathf.SmoothStep(0.02f, 0.3f, up);
                        Color cloudColor = Color.Lerp(new Color(0.82f, 0.84f, 0.88f), Color.white, Mathf.Clamp01(noise * 1.2f));
                        color = Color.Lerp(color, cloudColor, cloud * 0.9f);
                    }
                    pixels[y * SkyWidth + x] = color;
                }
            }
            var texture = new Texture2D(SkyWidth, SkyHeight, TextureFormat.RGBA32, false)
                { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amplitude = 0.5f;
            for (int i = 0; i < 4; i++)
            {
                sum += Mathf.PerlinNoise(x, y) * amplitude;
                x *= 2.1f;
                y *= 2.1f;
                amplitude *= 0.5f;
            }
            return sum / 0.9375f;
        }

        private static Light FindSun()
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) return light;
            return null;
        }
    }
}
