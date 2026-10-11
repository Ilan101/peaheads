using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // Ground patches where plants were cleansed: the killer's ground blobs plus flowers (Bloom) or rot (Blight),
    // placed by downward raycasts so they hug the ground. Same layout on every machine (seeded from the position the
    // host sends). Cleared each round. Added to the Match object by MatchManager.
    public class KillSites : MonoBehaviour
    {
        private const int BlobVariants = 6;
        private static readonly Color BloomGround = new(0.55f, 0.8f, 0.25f);
        private static readonly Color BlightGround = new(0.22f, 0.1f, 0.25f);
        private static readonly Color StemGreen = new(0.3f, 0.6f, 0.2f);
        private static readonly Color FlowerCentre = new(0.95f, 0.55f, 0.15f);
        private static readonly Color RotPurple = new(0.35f, 0.12f, 0.4f);
        private static readonly Color WiltBrown = new(0.45f, 0.35f, 0.18f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField, Range(0.5f, 4f)] private float _radius = 1.5f;
        [SerializeField, Range(4, 32)] private int _blobCount = 14;
        [SerializeField, Range(0, 20)] private int _propCount = 8;
        [SerializeField, Range(0.1f, 3f)] private float _growTime = 1f;
        [SerializeField, Range(1, 60)] private int _cap = 20;

        public static KillSites Instance { get; private set; }

        private readonly List<(Vector3 centre, int team)> _sites = new();
        private readonly List<GameObject> _roots = new();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Mesh[] _blobs;
        private Mesh _leaf;
        private Mesh _sphere;
        private Mesh _capsule;
        private Material _lit;
        private Material _particle;
        // One material per colour (blob shades are quantised), so the patches stay SRP-batcher friendly.
        private readonly Dictionary<Color, Material> _materials = new();

        private void Awake()
        {
            Instance = this;
            _lit = Resources.Load<Material>("KillSites/KillSiteLit");
            var particle = Resources.Load<Material>("KillSites/KillSiteParticle");
            if (particle)
            {
                _particle = new Material(particle);
                _particle.SetTexture(BaseMap, UiSprites.Glow().texture);
            }
            _leaf = PlantMeshes.Leaf(0.22f, 0.1f, 0.05f, 6);
            _sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            _capsule = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx");
            var rng = new System.Random(1234);
            _blobs = new Mesh[BlobVariants];
            for (int i = 0; i < BlobVariants; i++) _blobs[i] = BlobMesh(rng);
            MatchManager.RoundReset += Clear;
        }

        private void OnDestroy()
        {
            MatchManager.RoundReset -= Clear;
            if (Instance == this) Instance = null;
            foreach (Material material in _materials.Values) Destroy(material);
        }

        // Burst + patch after `delay` (the dying plant's tint). Owned here, not by the plant, so a plant that despawns
        // mid-tint still leaves the same patch on every machine; Clear() cancels pending ones.
        public void Schedule(float delay, Vector3 position, int killerTeam) =>
            StartCoroutine(CleanseAfter(delay, position, killerTeam));

        private IEnumerator CleanseAfter(float delay, Vector3 position, int killerTeam)
        {
            yield return new WaitForSeconds(delay);
            Burst(position + Vector3.up, killerTeam);
            Cleanse(position, killerTeam);
        }

        // A patch of the killer's ground at `position`; takes over the other team's overlapping patches.
        public void Cleanse(Vector3 position, int killerTeam)
        {
            if (!_lit) return; // materials not built yet (PeaHeads > Build Kill Site Materials)
            List<int> replaced = KillSitePlanner.ToReplace(_sites, position, killerTeam, _radius);
            for (int i = replaced.Count - 1; i >= 0; i--) RemoveAt(replaced[i]);
            for (int n = KillSitePlanner.OverCap(_sites.Count, _cap); n > 0; n--) RemoveAt(0);

            int seed = KillSitePlanner.Seed(position);
            var rng = new System.Random(seed);
            var root = new GameObject(killerTeam == 0 ? "BloomSite" : "BlightSite").transform;
            root.SetParent(transform, true);
            root.SetPositionAndRotation(position, Quaternion.identity);
            var growing = new List<(Transform part, Vector3 scale, float delay)>();
            Color ground = killerTeam == 0 ? BloomGround : BlightGround;

            Vector2[] blobs = KillSitePlanner.Scatter(_blobCount, _radius, seed);
            for (int i = 0; i < blobs.Length; i++)
            {
                if (!FindGround(position, blobs[i], out Vector3 point, out Vector3 normal)) continue;
                Color colour = ground * KillSitePlanner.Shade((float)rng.NextDouble(), 5);
                colour.a = 1f;
                float size = Mathf.Lerp(0.7f, 1f, (float)rng.NextDouble());
                Transform blob = Part(root, _blobs[rng.Next(BlobVariants)], colour,
                    point + normal * (0.01f + 0.002f * i) - position,
                    Quaternion.FromToRotation(Vector3.forward, normal), Vector3.one * size, false);
                growing.Add((blob, blob.localScale, blobs[i].magnitude / _radius * _growTime * 0.5f));
            }

            Vector2[] props = KillSitePlanner.Scatter(_propCount, _radius * 0.8f, seed + 1);
            for (int i = 0; i < props.Length; i++)
            {
                if (!FindGround(position, props[i], out Vector3 point, out Vector3 normal)) continue;
                Transform prop = killerTeam == 0 ? Flower(root) : i % 2 == 0 ? Tendril(root) : Wilt(root);
                prop.localPosition = point - position;
                prop.localRotation = Quaternion.FromToRotation(Vector3.up, normal) *
                                     Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                prop.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
                growing.Add((prop, prop.localScale, props[i].magnitude / _radius * _growTime * 0.5f + 0.15f));
            }

            _sites.Add((position, killerTeam));
            _roots.Add(root.gameObject);
            StartCoroutine(Grow(growing));
        }

        // Petals (Bloom) or rot dust (Blight) bursting from the cleansed plant.
        public void Burst(Vector3 position, int killerTeam)
        {
            if (!_particle) return;
            bool bloom = killerTeam == 0;
            var go = new GameObject("CleanseBurst");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = bloom ? 0.12f : 0.18f;
            main.gravityModifier = bloom ? 0.3f : -0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            Color team = Faction.Color(killerTeam);
            main.startColor = bloom
                ? new ParticleSystem.MinMaxGradient(team, Color.white)
                : new ParticleSystem.MinMaxGradient(team, Color.black);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _particle;
            ps.Play();
            Destroy(go, 2f);
        }

        public void Clear()
        {
            StopAllCoroutines(); // pending scheduled patches and growth
            foreach (GameObject root in _roots)
                if (root) Destroy(root);
            _roots.Clear();
            _sites.Clear();
        }

        private void RemoveAt(int index)
        {
            if (_roots[index]) Destroy(_roots[index]);
            _roots.RemoveAt(index);
            _sites.RemoveAt(index);
        }

        // Closest non-player surface under the point that is flat enough to grow on.
        private bool FindGround(Vector3 centre, Vector2 offset, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = Vector3.up;
            // From 1 m above the death point: starts inside nearby logs/rocks (rays miss colliders they start in) and
            // below the boundary wall tops; hits far from the death height are rejected.
            Vector3 from = centre + new Vector3(offset.x, 1f, offset.y);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider.GetComponentInParent<PlayerHealth>()) continue;
                if (!KillSitePlanner.IsGroundHit(centre.y, hit.point.y, hit.normal.y) || hit.distance >= best) continue;
                best = hit.distance;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }
            return found;
        }

        private Transform Flower(Transform root)
        {
            Transform flower = Group(root, "Flower");
            Part(flower, _capsule, StemGreen, new Vector3(0f, 0.15f, 0f), Quaternion.identity,
                new Vector3(0.03f, 0.15f, 0.03f), true);
            Color petal = Faction.Color(0);
            for (int k = 0; k < 5; k++)
                Part(flower, _leaf, petal, new Vector3(0f, 0.31f, 0f),
                    Quaternion.Euler(0f, k * 72f, 0f) * Quaternion.Euler(-20f, 0f, 0f), Vector3.one, false);
            Part(flower, _sphere, FlowerCentre, new Vector3(0f, 0.32f, 0f), Quaternion.identity, Vector3.one * 0.07f, false);
            return flower;
        }

        private Transform Tendril(Transform root)
        {
            Transform tendril = Group(root, "Tendril");
            Part(tendril, _leaf, RotPurple, Vector3.zero, Quaternion.Euler(-80f, 0f, 0f), new Vector3(1f, 1f, 1.6f), true);
            Part(tendril, _leaf, RotPurple * 0.8f, Vector3.zero, Quaternion.Euler(-65f, 120f, 0f),
                new Vector3(0.8f, 0.8f, 1.2f), true);
            return tendril;
        }

        private Transform Wilt(Transform root)
        {
            Transform wilt = Group(root, "Wilt");
            for (int k = 0; k < 3; k++)
                Part(wilt, _leaf, WiltBrown, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(15f, k * 120f, 0f), Vector3.one, true);
            return wilt;
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private Transform Part(Transform parent, Mesh mesh, Color colour, Vector3 localPosition, Quaternion localRotation,
            Vector3 localScale, bool shadows)
        {
            var go = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            go.transform.localScale = localScale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MaterialFor(colour);
            meshRenderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        private Material MaterialFor(Color colour)
        {
            if (_materials.TryGetValue(colour, out Material material)) return material;
            material = new Material(_lit);
            material.SetColor(BaseColor, colour);
            _materials[colour] = material;
            return material;
        }

        // Outward ripple: each part scales 0 → full after its delay. Parts removed mid-growth are skipped.
        private IEnumerator Grow(List<(Transform part, Vector3 scale, float delay)> parts)
        {
            float end = 0f;
            foreach (var p in parts)
            {
                p.part.localScale = Vector3.zero;
                end = Mathf.Max(end, p.delay);
            }
            end += _growTime;
            float start = Time.time;
            while (Time.time - start < end)
            {
                float t = Time.time - start;
                foreach (var p in parts)
                    if (p.part) p.part.localScale = p.scale * Mathf.SmoothStep(0f, 1f, (t - p.delay) / _growTime);
                yield return null;
            }
            foreach (var p in parts)
                if (p.part) p.part.localScale = p.scale;
        }

        // Irregular ten-sided disc facing +Z (radius 0.35–0.5 m).
        private static Mesh BlobMesh(System.Random rng)
        {
            const int sides = 10;
            var vertices = new Vector3[sides + 1];
            vertices[0] = Vector3.zero;
            for (int j = 0; j < sides; j++)
            {
                float angle = j * 2f * Mathf.PI / sides;
                float r = Mathf.Lerp(0.35f, 0.5f, (float)rng.NextDouble());
                vertices[j + 1] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
            }
            var triangles = new int[sides * 3];
            for (int j = 0; j < sides; j++)
            {
                triangles[j * 3] = 0;
                triangles[j * 3 + 1] = j + 1;
                triangles[j * 3 + 2] = j + 2 > sides ? 1 : j + 2;
            }
            var mesh = new Mesh { name = "KillSiteBlob", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
