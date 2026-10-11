using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeaHeads
{
    // Visual polish layered on top of the scene and AtmosphereController (which owns sky, fog and sun): a global
    // post-processing volume (bloom, grading, vignette) with SMAA on the game cameras, drifting spores over the arena,
    // light beams + ground rings on active Sporelights, glowing peas with trails, and dirt bursts for planting.
    // Watches the frame time too: on a GPU that can't keep up (integrated laptop graphics) it steps quality down.
    // Visual only, local. Added by GameMenu.
    public class WorldPolish : MonoBehaviour
    {
        private static readonly Color SporelightGlow = new(1f, 0.55f, 0.3f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainColor = Shader.PropertyToID("_Color");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [Header("Post-processing")]
        [SerializeField, Range(0f, 3f)] private float _bloomIntensity = 0.9f;
        [SerializeField, Range(0f, 2f)] private float _bloomThreshold = 0.95f;
        [SerializeField, Range(-50f, 50f)] private float _contrast = 10f;
        [SerializeField, Range(-50f, 50f)] private float _saturation = 14f;
        [SerializeField, Range(0f, 0.6f)] private float _vignette = 0.2f;

        [Header("Arena")]
        [SerializeField] private Vector3 _arenaCentre = new(0f, 2.5f, 0f);
        [SerializeField] private Vector3 _arenaSize = new(42f, 5f, 42f);
        [SerializeField, Range(0, 400)] private int _spores = 180;
        [SerializeField, Range(0f, 15f)] private float _beamHeight = 8f;

        [Header("Performance")]
        [Tooltip("While the median arena frame takes longer than this (ms), quality steps down one tier: cheaper bloom, " +
                 "then FXAA and 85% then 75% render scale with FSR upscaling. It never steps back up.")]
        [SerializeField, Range(10f, 50f)] private float _targetFrameMs = 21f;

        private static WorldPolish _instance;
        private static Material _particleBase;
        private static Texture2D _softDot, _beamTexture, _ringTexture;

        private readonly List<(MushroomSpot spot, Transform beam, Transform ring, Material beamMat, Material ringMat)> _beacons = new();
        private ParticleSystem _dust;
        private float _nextCameraCheck;

        private const int MaxTier = 3;
        private const int SampleFrames = 90;
        private readonly List<float> _frameMs = new(SampleFrames);
        private Bloom _bloom;
        private int _tier;
        private float _measureAfter;
        private AntialiasingMode _antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        private AntialiasingQuality _antialiasingQuality = AntialiasingQuality.High;
        // Render scale lives on the pipeline asset: changed in memory only (never marked dirty, so never saved) and put
        // back in OnDestroy, which also runs when play mode stops.
        private UniversalRenderPipelineAsset _pipeline;
        private float _baseRenderScale = 1f;
        private UpscalingFilterSelection _baseUpscaling;

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (!_pipeline) return;
            _pipeline.renderScale = _baseRenderScale;
            _pipeline.upscalingFilter = _baseUpscaling;
        }

        private void Start()
        {
            _pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_pipeline)
            {
                _baseRenderScale = _pipeline.renderScale;
                _baseUpscaling = _pipeline.upscalingFilter;
            }
            _measureAfter = Time.unscaledTime + 3f; // loading hitches aren't the GPU's fault
            BuildVolume();
            BuildSpores();
            BuildBeacons();
            _dust = MakeSystem("PlantDust", 300);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextCameraCheck)
            {
                _nextCameraCheck = Time.unscaledTime + 0.5f;
                EnablePostProcessing();
            }
            UpdateBeacons();
            MeasureFrames();
        }

        // ---------- Performance ----------

        // Median of 90 arena frames (the story and lobby cover the arena and cost little, so they don't count); one
        // tier down each time it's over target, judged again a second after the change has settled.
        private void MeasureFrames()
        {
            if (_tier >= MaxTier) return;
            if (LobbyView.HidesGame || Time.unscaledTime < _measureAfter)
            {
                _frameMs.Clear();
                return;
            }
            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (_frameMs.Count < SampleFrames) return;
            _frameMs.Sort();
            float median = _frameMs[SampleFrames / 2];
            _frameMs.Clear();
            if (median <= _targetFrameMs) return;
            SetTier(_tier + 1);
            _measureAfter = Time.unscaledTime + 1f;
        }

        // 0 full quality; 1 quarter-resolution bloom, medium SMAA; 2 FXAA, 85% render scale; 3 75%.
        private void SetTier(int tier)
        {
            _tier = tier;
            if (_bloom)
            {
                _bloom.highQualityFiltering.value = tier == 0;
                _bloom.downscale.value = tier == 0 ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter;
            }
            _antialiasing = tier >= 2 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            _antialiasingQuality = tier == 0 ? AntialiasingQuality.High : AntialiasingQuality.Medium;
            EnablePostProcessing();
            if (!_pipeline) return;
            _pipeline.renderScale = tier >= 3 ? 0.75f : tier == 2 ? 0.85f : _baseRenderScale;
            _pipeline.upscalingFilter = tier >= 2 ? UpscalingFilterSelection.FSR : _baseUpscaling;
        }

        // ---------- Post-processing ----------

        private void BuildVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = _bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = _bloomIntensity;
            bloom.threshold.value = _bloomThreshold;
            bloom.scatter.value = 0.65f;
            bloom.highQualityFiltering.value = true;
            bloom.downscale.overrideState = true;
            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.value = TonemappingMode.Neutral;
            ColorAdjustments grading = profile.Add<ColorAdjustments>(true);
            grading.contrast.value = _contrast;
            grading.saturation.value = _saturation;
            grading.postExposure.value = 0.05f;
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = _vignette;
            vignette.smoothness.value = 0.45f;

            var go = new GameObject("PostProcessing");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = profile;
        }

        // Screen cameras only (render-texture cameras like the killcam capture stay cheap, blank ones draw nothing).
        private void EnablePostProcessing()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam.targetTexture || cam.cullingMask == 0) continue;
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                if (data.renderPostProcessing && data.antialiasing == _antialiasing &&
                    data.antialiasingQuality == _antialiasingQuality) continue;
                data.renderPostProcessing = true;
                data.antialiasing = _antialiasing;
                data.antialiasingQuality = _antialiasingQuality;
            }
        }

        // ---------- Spores ----------

        private void BuildSpores()
        {
            if (_spores <= 0) return;
            ParticleSystem system = MakeSystem("ArenaSpores", _spores);
            system.transform.position = _arenaCentre;
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.55f, 0.9f), new Color(0.7f, 1f, 0.6f, 0.8f));
            main.prewarm = true;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = _spores / 8f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _arenaSize;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            ParticleSystem.ColorOverLifetimeModule fadeInOut = system.colorOverLifetime;
            fadeInOut.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fadeInOut.color = gradient;
            system.GetComponent<ParticleSystemRenderer>().material = GlowMaterial(SoftDot, new Color(1.6f, 1.6f, 1.4f, 1f));
            system.Play();
        }

        // ---------- Sporelight beacons ----------

        private void BuildBeacons()
        {
            foreach (MushroomSpot spot in FindObjectsByType<MushroomSpot>(FindObjectsSortMode.None))
            {
                Material beamMat = GlowMaterial(BeamTexture, SporelightGlow * 1.8f);
                Material ringMat = GlowMaterial(RingTexture, SporelightGlow * 2f);
                Transform beam = Quad("SporelightBeam", spot.transform, beamMat);
                beam.localScale = new Vector3(1.1f, _beamHeight, 1f);
                beam.localPosition = new Vector3(0f, _beamHeight * 0.5f, 0f);
                Transform ring = Quad("SporelightRing", spot.transform, ringMat);
                ring.localPosition = new Vector3(0f, 0.04f, 0f);
                ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ring.localScale = new Vector3(3.2f, 3.2f, 1f);
                _beacons.Add((spot, beam, ring, beamMat, ringMat));
            }
        }

        private void UpdateBeacons()
        {
            Camera cam = Camera.main ? Camera.main : null;
            float t = Time.time;
            foreach ((MushroomSpot spot, Transform beam, Transform ring, Material beamMat, Material ringMat) in _beacons)
            {
                if (!spot) continue;
                bool shown = spot.IsShown;
                if (beam.gameObject.activeSelf != shown)
                {
                    beam.gameObject.SetActive(shown);
                    ring.gameObject.SetActive(shown);
                }
                if (!shown) continue;
                // Face the camera around the vertical axis only, so the beam stays upright.
                if (cam)
                {
                    Vector3 to = cam.transform.position - beam.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) beam.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
                }
                float pulse = 0.75f + 0.25f * Mathf.Sin(t * 2.5f + spot.Index);
                // Thin out as the camera walks into the shaft, so collecting doesn't fill the screen with glow.
                float near = 1f;
                if (cam)
                {
                    Vector3 offset = cam.transform.position - beam.position;
                    offset.y = 0f;
                    near = Mathf.Lerp(0.12f, 1f, Mathf.InverseLerp(1.2f, 4.5f, offset.magnitude));
                }
                SetColor(beamMat, SporelightGlow * (1.8f * pulse * near));
                SetColor(ringMat, SporelightGlow * (2f * pulse));
                ring.localRotation = Quaternion.Euler(90f, t * 25f, 0f);
            }
        }

        // ---------- Peas and planting ----------

        // Called by PeaProjectile when it spawns: team-coloured glow and a short light trail.
        public static void DecoratePea(GameObject pea, Renderer renderer, Color color)
        {
            if (renderer)
            {
                Material material = renderer.material;
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColor, color * 0.9f);
            }
            if (pea.GetComponent<TrailRenderer>()) return;
            var trail = pea.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = pea.transform.lossyScale.x * 0.9f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.material = GlowMaterial(SoftDot, color * 1.6f);
        }

        // Dirt clods and a few leaves at the base when a PeaHead digs in or pulls out.
        public static void PlantBurst(Vector3 position, bool uproot)
        {
            if (!_instance || !_instance._dust) return;
            var soil = new Color(0.36f, 0.25f, 0.16f);
            var leaf = new Color(0.45f, 0.75f, 0.3f);
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < 26; i++)
            {
                bool isLeaf = i % 5 == 0;
                Vector2 dir = Random.insideUnitCircle.normalized;
                emit.position = position + new Vector3(dir.x, 0.05f, dir.y) * Random.Range(0.1f, 0.4f);
                emit.velocity = new Vector3(dir.x * Random.Range(0.6f, 1.8f), Random.Range(uproot ? 2.2f : 1.2f, uproot ? 3.6f : 2.4f),
                    dir.y * Random.Range(0.6f, 1.8f));
                emit.startSize = isLeaf ? Random.Range(0.1f, 0.16f) : Random.Range(0.06f, 0.13f);
                emit.startLifetime = Random.Range(0.5f, 0.9f);
                emit.startColor = isLeaf ? leaf : soil * Random.Range(0.8f, 1.2f);
                _instance._dust.Emit(emit, 1);
            }
        }

        // ---------- Helpers ----------

        private ParticleSystem MakeSystem(string name, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = name == "PlantDust" ? 1f : 0f;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = name != "PlantDust";
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (name == "PlantDust") renderer.material = GlowMaterial(SoftDot, Color.white);
            if (name == "PlantDust") system.Play();
            return system;
        }

        private static Transform Quad(string name, Transform parent, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            quad.SetActive(false);
            return quad.transform;
        }

        // Unlit, alpha-blended material: URP's particle material borrowed from the scene's mushroom sparkles (so the
        // shader is in builds), else the always-included UI shader.
        private static Material GlowMaterial(Texture texture, Color color)
        {
            if (!_particleBase)
            {
                foreach (ParticleSystemRenderer r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
                    if (r.sharedMaterial && r.sharedMaterial.shader.name.Contains("Particles"))
                    {
                        _particleBase = r.sharedMaterial;
                        break;
                    }
            }
            Material material = _particleBase ? new Material(_particleBase) : new Material(Shader.Find("UI/Default"));
            if (material.HasProperty(BaseMap)) material.SetTexture(BaseMap, texture);
            if (material.HasProperty(MainTex)) material.SetTexture(MainTex, texture);
            SetColor(material, color);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            if (material.HasProperty(MainColor)) material.SetColor(MainColor, color);
        }

        private static Texture2D SoftDot => _softDot ? _softDot : _softDot = UiSprites.Glow(64).texture;

        // Vertical light shaft: bright at the base, fading up and towards the sides.
        private static Texture2D BeamTexture => _beamTexture ? _beamTexture : _beamTexture = MakeTexture(32, 128, (u, v) =>
            Mathf.Pow(1f - v, 1.6f) * Mathf.Pow(1f - Mathf.Abs(u * 2f - 1f), 1.8f));

        // Collect-area ring on the ground: soft rim plus a faint fill.
        private static Texture2D RingTexture => _ringTexture ? _ringTexture : _ringTexture = MakeTexture(128, 128, (u, v) =>
        {
            float r = new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude;
            float rim = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.06f, 2f));
            float dashes = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(Mathf.Atan2(v - 0.5f, u - 0.5f) * 12f));
            return r > 1f ? 0f : rim * dashes + 0.12f * (1f - r);
        });

        private static Texture2D MakeTexture(int width, int height, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(255, 255, 255,
                    (byte)(Mathf.Clamp01(alpha((x + 0.5f) / width, (y + 0.5f) / height)) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
