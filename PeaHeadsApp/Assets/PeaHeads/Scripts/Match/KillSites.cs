using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // Ground patches where plants were cleansed: one puddle of the killer's ground draped over the terrain, plus flowers
    // (Bloom) or glowing purple mushrooms (Blight), placed by downward raycasts so they hug the ground. Same layout on every machine (seeded from the position the
    // host sends). Cleared each round. Added to the Match object by MatchManager.
    public class KillSites : MonoBehaviour
    {
        private const int PuddleSegments = 32;
        private const int PuddleRings = 5;
        private const float PuddleLift = 0.02f;
        private static readonly Color BloomGround = new(0.55f, 0.8f, 0.25f);
        private static readonly Color BlightGround = new(0.22f, 0.1f, 0.25f);
        private static readonly Color StemGreen = new(0.3f, 0.6f, 0.2f);
        private static readonly Color FlowerCentre = new(0.95f, 0.55f, 0.15f);
        private static readonly Color MushroomStem = new(0.82f, 0.74f, 0.9f);
        private static readonly Color MushroomCap = new(0.55f, 0.2f, 0.75f);
        private static readonly Color MushroomGlow = new(0.45f, 0.12f, 0.65f);
        private static readonly Color MushroomGills = new(0.2f, 0.07f, 0.25f);
        private static readonly Color MushroomSpot = new(0.92f, 0.8f, 1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField, Range(0.5f, 4f)] private float _radius = 1.5f;
        [SerializeField, Range(0, 20)] private int _propCount = 8;
        [SerializeField, Range(0.1f, 3f)] private float _growTime = 1f;
        [SerializeField, Range(1, 60)] private int _cap = 20;

        public static KillSites Instance { get; private set; }

        private readonly List<(Vector3 centre, int team)> _sites = new();
        private readonly List<GameObject> _roots = new();
        // Puddle meshes per patch (generated per kill, destroyed with the patch).
        private readonly List<Mesh[]> _rootMeshes = new();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Mesh _leaf;
        private Mesh _dome;
        private Mesh _disc;
        private Mesh _sphere;
        private Mesh _capsule;
        private Material _lit;
        private Material _glow;
        private Material _particle;
        // One material per look, so the patches stay SRP-batcher friendly.
        private readonly Dictionary<(Color colour, float smoothness, Color glow), Material> _materials = new();

        private void Awake()
        {
            Instance = this;
            _lit = Resources.Load<Material>("KillSites/KillSiteLit");
            _glow = Resources.Load<Material>("KillSites/KillSiteGlow");
            var particle = Resources.Load<Material>("KillSites/KillSiteParticle");
            if (particle)
            {
                _particle = new Material(particle);
                _particle.SetTexture(BaseMap, UiSprites.Glow().texture);
            }
            _leaf = PlantMeshes.Leaf(0.22f, 0.1f, 0.05f, 6);
            _dome = PlantMeshes.Dome(1f, 16, 6);
            _disc = PlantMeshes.Disc(1f, 16);
            _sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            _capsule = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx");
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
            bool bloom = killerTeam == 0;
            Color ground = bloom ? BloomGround : BlightGround;
            float gloss = bloom ? 0.15f : 0.75f; // Blight rot looks wet

            // Outer puddle plus a smaller, darker inner one for depth; both spread out from the centre.
            Mesh outer = Puddle(position, _radius, KillSitePlanner.PuddleOutline(PuddleSegments, seed), PuddleLift,
                out Vector3[] outerShape);
            Mesh inner = Puddle(position, _radius * 0.6f, KillSitePlanner.PuddleOutline(PuddleSegments, seed + 2),
                PuddleLift + 0.01f, out Vector3[] innerShape);
            Part(root, outer, ground, Vector3.zero, Quaternion.identity, Vector3.one, false, gloss);
            Part(root, inner, ground * 0.8f, Vector3.zero, Quaternion.identity, Vector3.one, false, gloss);
            StartCoroutine(Spread(outer, outerShape, 0f));
            StartCoroutine(Spread(inner, innerShape, _growTime * 0.25f));

            Vector2[] props = KillSitePlanner.Scatter(_propCount, _radius * 0.8f, seed + 1);
            for (int i = 0; i < props.Length; i++)
            {
                if (!FindGround(position, props[i], out Vector3 point, out Vector3 normal)) continue;
                Transform prop = bloom ? Flower(root) : Mushroom(root, rng);
                prop.localPosition = point - position;
                prop.localRotation = Quaternion.FromToRotation(Vector3.up, normal) *
                                     Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                prop.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
                growing.Add((prop, prop.localScale, props[i].magnitude / _radius * _growTime * 0.5f + 0.15f));
            }

            _sites.Add((position, killerTeam));
            _roots.Add(root.gameObject);
            _rootMeshes.Add(new[] { outer, inner });
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
            foreach (Mesh[] meshes in _rootMeshes)
                foreach (Mesh mesh in meshes) Destroy(mesh);
            _roots.Clear();
            _rootMeshes.Clear();
            _sites.Clear();
        }

        private void RemoveAt(int index)
        {
            if (_roots[index]) Destroy(_roots[index]);
            foreach (Mesh mesh in _rootMeshes[index]) Destroy(mesh);
            _roots.RemoveAt(index);
            _rootMeshes.RemoveAt(index);
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

        // Glowing purple toadstool: lavender stem, domed cap with dark gills and two pale spots. 15-40 cm tall.
        private Transform Mushroom(Transform root, System.Random rng)
        {
            Transform mushroom = Group(root, "Mushroom");
            float height = Mathf.Lerp(0.15f, 0.4f, (float)rng.NextDouble());
            float cap = height * 0.6f;
            Part(mushroom, _capsule, MushroomStem, new Vector3(0f, height * 0.5f, 0f), Quaternion.identity,
                new Vector3(cap * 0.35f, height * 0.5f, cap * 0.35f), true);
            Part(mushroom, _dome, MushroomCap, new Vector3(0f, height, 0f), Quaternion.Euler(-90f, 0f, 0f),
                new Vector3(cap, cap, cap * 0.7f), true, 0.5f, MushroomGlow);
            Part(mushroom, _disc, MushroomGills, new Vector3(0f, height + 0.002f, 0f), Quaternion.Euler(90f, 0f, 0f),
                Vector3.one * cap * 0.98f, false);
            for (int k = 0; k < 2; k++)
            {
                float yaw = (float)rng.NextDouble() * 360f, rise = Mathf.Lerp(35f, 60f, (float)rng.NextDouble());
                Vector3 onCap = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-rise, 0f, 0f) * Vector3.forward;
                onCap = new Vector3(onCap.x * cap, onCap.y * cap * 0.7f, onCap.z * cap);
                Part(mushroom, _sphere, MushroomSpot, new Vector3(0f, height, 0f) + onCap, Quaternion.identity,
                    Vector3.one * cap * 0.22f, false, 0.5f, MushroomGlow * 0.5f);
            }
            return mushroom;
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private Transform Part(Transform parent, Mesh mesh, Color colour, Vector3 localPosition, Quaternion localRotation,
            Vector3 localScale, bool shadows, float smoothness = 0.2f, Color glow = default)
        {
            var go = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            go.transform.localScale = localScale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MaterialFor(colour, smoothness, glow);
            meshRenderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        private Material MaterialFor(Color colour, float smoothness, Color glow)
        {
            if (_materials.TryGetValue((colour, smoothness, glow), out Material material)) return material;
            bool glows = glow != default;
            material = new Material(glows && _glow ? _glow : _lit);
            material.SetColor(BaseColor, colour);
            material.SetFloat(Smoothness, smoothness);
            if (glows)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColor, glow);
            }
            _materials[(colour, smoothness, glow)] = material;
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

        // One draped puddle: a centre vertex plus rings x outline points, each dropped onto the ground by a short ray
        // (death height where there is no valid ground). Vertices are relative to the death point; facing up.
        private Mesh Puddle(Vector3 centre, float radius, float[] outline, float lift, out Vector3[] shape)
        {
            int segments = outline.Length;
            shape = new Vector3[1 + PuddleRings * segments];
            shape[0] = new Vector3(0f, GroundY(centre, Vector2.zero) - centre.y + lift, 0f);
            for (int ring = 1; ring <= PuddleRings; ring++)
            for (int j = 0; j < segments; j++)
            {
                float angle = j * 2f * Mathf.PI / segments;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * outline[j] * ring / PuddleRings);
                shape[1 + (ring - 1) * segments + j] =
                    new Vector3(offset.x, GroundY(centre, offset) - centre.y + lift, offset.y);
            }

            var triangles = new List<int>(segments * 3 * (2 * PuddleRings - 1));
            for (int j = 0; j < segments; j++)
            {
                int next = (j + 1) % segments;
                triangles.Add(0);
                triangles.Add(1 + next);
                triangles.Add(1 + j);
            }
            for (int ring = 1; ring < PuddleRings; ring++)
            for (int j = 0; j < segments; j++)
            {
                int next = (j + 1) % segments;
                int a = 1 + (ring - 1) * segments + j, b = 1 + (ring - 1) * segments + next;
                int c = a + segments, d = b + segments;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }

            var mesh = new Mesh { name = "KillSitePuddle" };
            mesh.MarkDynamic();
            mesh.vertices = shape;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private float GroundY(Vector3 centre, Vector2 offset) =>
            FindGround(centre, offset, out Vector3 point, out _) ? point.y : centre.y;

        // The puddle spreads like liquid: its outline pushes out from the centre (ease-out), heights stay on the ground.
        private IEnumerator Spread(Mesh mesh, Vector3[] shape, float delay)
        {
            var vertices = new Vector3[shape.Length];
            float start = Time.time;
            while (mesh)
            {
                float t = Mathf.Clamp01((Time.time - start - delay) / _growTime);
                float reach = 1f - (1f - t) * (1f - t);
                for (int i = 0; i < shape.Length; i++)
                    vertices[i] = new Vector3(shape[i].x * reach, shape[i].y, shape[i].z * reach);
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                if (t >= 1f) yield break;
                yield return null;
            }
        }
    }
}
