# Cleanse + Kill Sites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A dying plant tints to the killer's faction colour and bursts, and a ~3 m patch of the killer's ground (Bloom flowers / Blight rot) grows where it died, taking over the other team's patches, cleared each round.

**Architecture:** Pure layout maths in `KillSitePlanner` (seeded from the death position) feed a `KillSites` MonoBehaviour on the Match object that builds patch objects by downward raycasts. The host sends one `CleansedRpc(killerTeam, position)` from `PlayerHealth` on death; every machine tints, bursts and builds the identical patch. A synced round counter in `MatchManager` raises `RoundReset` to clear patches.

**Tech Stack:** Unity 6000.3.9f1 URP (Lit, Particles/Unlit), Netcode for GameObjects 2.13.3 RPCs, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-10-11-cleanse-kill-sites-design.md`

## Global Constraints

- Purely visual: colliders still switch off the instant HP hits 0; scoring, respawn timing, killcam unchanged.
- No changes to Terrain, `ArenaBuilder`, or render-pipeline assets.
- Patch: radius 1.5 m, 14 blobs, 8 props, grow 1 s, cap 20 (oldest removed); new patch removes the **other** team's patches whose centres are within its radius (XZ).
- Colours: Bloom ground `(0.55, 0.8, 0.25)`, Blight ground `(0.22, 0.1, 0.25)`; tint target `Faction.Color(killerTeam)`; tint time 0.3 s; renderers hide 0.35 s after death.
- The death position comes from the host inside the RPC (so every machine seeds identically).
- Materials live in `Assets/PeaHeads/Resources/KillSites/` (`KillSiteLit.mat`, `KillSiteParticle.mat`), created by the editor menu `PeaHeads/Build Kill Site Materials`.
- C# conventions: `PascalCase`, `_camelCase` private fields, `[SerializeField, Range]` tunables, minimal comments. Conventional commits ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Batch-mode Unity needs the editor closed.

**Shared commands** (Git Bash, repo root):

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe"
P="$(cygpath -w "$PWD/PeaHeadsApp")"
LOGS="$HOME/AppData/Local/Temp/peaheads-logs"; mkdir -p "$LOGS"
```

- **EditMode tests:** `rm -f "$LOGS/tests.xml"; "$UNITY" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testResults "$(cygpath -w "$LOGS")\\tests.xml" -logFile "$(cygpath -w "$LOGS")\\tests.log"; grep -E "error CS|warning CS" "$LOGS/tests.log" | grep PeaHeads | sort -u; grep -oE 'result="[A-Za-z]+" total="[0-9]+" passed="[0-9]+" failed="[0-9]+"' "$LOGS/tests.xml" | head -1`
- **Build materials:** `"$UNITY" -batchmode -nographics -quit -projectPath "$P" -executeMethod PeaHeads.EditorTools.PlantBodyBuilder.BuildKillSiteMaterials -logFile "$(cygpath -w "$LOGS")\\mats.log"; grep -E "KillSite|Exception" "$LOGS/mats.log" | head`

## Review Focus

1. Remote copies interpolate positions, so each machine's `transform.position` of the dead plant differs slightly → patches must still be identical. Pinned by sending the host's position in the RPC (Task 3) + manual check 1.
2. Respawned plant must get its own colours back (no leftover killer tint, incl. the team-tinted head). Task 3 clears property blocks on revive; manual check 5.
3. Round reset while a patch is still growing (restart right after a kill) → no MissingReferenceException. Grow coroutine skips destroyed parts (Task 2); manual check 6.
4. Materials not built yet (fresh clone before running the menu) → no patches and no exceptions, game still plays. `KillSites` guards null materials (Task 2).
5. Death on a steep slope, a cover top or next to a wall → blobs only on surfaces with normal.y ≥ 0.7, none floating. Raycast filter (Task 2); manual check 4.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Scripts/Match/KillSitePlanner.cs` | Create | Pure: `Seed`, `Scatter`, `ToReplace`, `OverCap` |
| `Tests/EditMode/KillSitePlannerTests.cs` | Create | Planner tests |
| `Editor/PlantBodyBuilder.cs` | Modify | Menu `PeaHeads/Build Kill Site Materials` |
| `Resources/KillSites/KillSiteLit.mat`, `KillSiteParticle.mat` | Generated | Shared materials |
| `Scripts/Match/KillSites.cs` | Create | Builds/removes patches, burst particles, clears on `RoundReset` |
| `Scripts/Match/MatchManager.cs` | Modify | `_round` var, `RoundReset` event, adds `KillSites` |
| `Scripts/Player/PlayerHealth.cs` | Modify | `CleansedRpc`, tint, delayed hide, tint clear on revive |
| `CLAUDE.md` | Modify | Docs |

(Paths under `PeaHeadsApp/Assets/PeaHeads/`.)

---

### Task 1: KillSitePlanner

**Files:**
- Create: `Scripts/Match/KillSitePlanner.cs`
- Test: `Tests/EditMode/KillSitePlannerTests.cs`

**Interfaces:**
- Produces: `public static class KillSitePlanner` with `int Seed(Vector3 position)`, `Vector2[] Scatter(int count, float radius, int seed)`, `List<int> ToReplace(IReadOnlyList<(Vector3 centre, int team)> existing, Vector3 centre, int team, float radius)`, `int OverCap(int count, int cap)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/KillSitePlannerTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class KillSitePlannerTests
    {
        [TestCase(14, 1.5f)]
        [TestCase(8, 1.2f)]
        public void Scatter_PointsInsideRadiusAndSpreadOut(int count, float radius)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                Vector2[] points = KillSitePlanner.Scatter(count, radius, seed);
                Assert.AreEqual(count, points.Length);
                float minSpacing = 0.5f * radius / Mathf.Sqrt(count);
                for (int i = 0; i < count; i++)
                {
                    Assert.LessOrEqual(points[i].magnitude, radius + 1e-4f, $"seed {seed} point {i}");
                    for (int j = i + 1; j < count; j++)
                        Assert.GreaterOrEqual((points[i] - points[j]).magnitude, minSpacing, $"seed {seed} pair {i},{j}");
                }
            }
        }

        [Test]
        public void Scatter_SameSeedSamePoints() =>
            Assert.AreEqual(KillSitePlanner.Scatter(14, 1.5f, 42), KillSitePlanner.Scatter(14, 1.5f, 42));

        [Test]
        public void Scatter_DifferentSeedDifferentPoints() =>
            Assert.AreNotEqual(KillSitePlanner.Scatter(14, 1.5f, 1), KillSitePlanner.Scatter(14, 1.5f, 2));

        [Test]
        public void Seed_SamePositionSameSeed() =>
            Assert.AreEqual(KillSitePlanner.Seed(new Vector3(3.04f, 0.5f, -7.2f)),
                KillSitePlanner.Seed(new Vector3(3.01f, 0.52f, -7.18f)));

        [Test]
        public void Seed_DifferentPositionsDifferentSeeds() =>
            Assert.AreNotEqual(KillSitePlanner.Seed(new Vector3(3f, 0f, 7f)), KillSitePlanner.Seed(new Vector3(4f, 0f, 7f)));

        private static readonly List<(Vector3 centre, int team)> Sites = new()
        {
            (new Vector3(0f, 0f, 0f), 1),     // other team, inside
            (new Vector3(1f, 5f, 0f), 1),     // other team, inside on XZ despite height
            (new Vector3(0.5f, 0f, 0.5f), 0), // same team, inside
            (new Vector3(4f, 0f, 0f), 1),     // other team, outside
        };

        [Test]
        public void ToReplace_OnlyOtherTeamInsideRadiusOnXZ() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, KillSitePlanner.ToReplace(Sites, Vector3.zero, 0, 1.5f));

        [TestCase(5, 20, ExpectedResult = 0)]
        [TestCase(19, 20, ExpectedResult = 0)]
        [TestCase(20, 20, ExpectedResult = 1)]
        [TestCase(22, 20, ExpectedResult = 3)]
        public int OverCap_RemovesOldestToFitOneMore(int count, int cap) => KillSitePlanner.OverCap(count, cap);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile error `The name 'KillSitePlanner' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `Scripts/Match/KillSitePlanner.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PeaHeads
{
    // Pure layout maths for kill-site patches. Seeded from the death position so every machine builds the same patch.
    public static class KillSitePlanner
    {
        private const float GoldenAngle = 137.50776f;

        // Position rounded to 0.1 m, hashed.
        public static int Seed(Vector3 position)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(position.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(position.y * 10f);
                hash = hash * 31 + Mathf.RoundToInt(position.z * 10f);
                return hash;
            }
        }

        // Sunflower spiral with a seeded rotation and a little seeded jitter: even, natural-looking spread.
        public static Vector2[] Scatter(int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            float rotation = (float)rng.NextDouble() * 360f;
            float spacing = radius / Mathf.Sqrt(Mathf.Max(1, count));
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float r = radius * Mathf.Sqrt((i + 0.5f) / count);
                float angle = (rotation + i * GoldenAngle) * Mathf.Deg2Rad;
                Vector2 p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                var jitter = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                p += jitter * (0.15f * spacing);
                if (p.magnitude > radius) p = p.normalized * radius;
                points[i] = p;
            }
            return points;
        }

        // Other team's patches whose centre is within `radius` of the new one (horizontal distance), ascending.
        public static List<int> ToReplace(IReadOnlyList<(Vector3 centre, int team)> existing, Vector3 centre, int team,
            float radius)
        {
            var result = new List<int>();
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i].team == team) continue;
                var offset = new Vector2(existing[i].centre.x - centre.x, existing[i].centre.z - centre.z);
                if (offset.magnitude < radius) result.Add(i);
            }
            return result;
        }

        // Oldest patches to drop so one more fits under the cap.
        public static int OverCap(int count, int cap) => Mathf.Max(0, count + 1 - cap);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: `result="Passed"`, `failed="0"`, 11 more tests than before.

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Scripts/Match/KillSitePlanner.cs $S/Scripts/Match/KillSitePlanner.cs.meta $S/Tests/EditMode/KillSitePlannerTests.cs $S/Tests/EditMode/KillSitePlannerTests.cs.meta
git commit -m "feat: kill-site patch layout maths" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Materials, KillSites, round reset

**Files:**
- Modify: `Editor/PlantBodyBuilder.cs`, `Scripts/Match/MatchManager.cs`
- Create: `Scripts/Match/KillSites.cs`
- Generated: `Resources/KillSites/KillSiteLit.mat`, `Resources/KillSites/KillSiteParticle.mat` (+ folder `.meta`)

**Interfaces:**
- Consumes: `KillSitePlanner` (Task 1), `PlantMeshes.Leaf`, `UiSprites.Glow`, `Faction.Color`.
- Produces: `KillSites.Instance`, `void Cleanse(Vector3 position, int killerTeam)`, `void Burst(Vector3 position, int killerTeam)`, `void Clear()`; `MatchManager.RoundReset` (static `Action`).

No new EditMode test: scene/physics/rendering code; verified by compile, the materials build, and Task 4 playtest.

- [ ] **Step 1: Materials menu in `PlantBodyBuilder`**

Add `using UnityEngine.Rendering;` to the usings. Inside the class, after `Build()`, add:

```csharp
        private const string KillSitesDir = "Assets/PeaHeads/Resources/KillSites";

        // Shared materials for KillSites (loaded via Resources so the shaders ship in builds).
        [MenuItem("PeaHeads/Build Kill Site Materials")]
        public static void BuildKillSiteMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PeaHeads/Resources")) AssetDatabase.CreateFolder("Assets/PeaHeads", "Resources");
            if (!AssetDatabase.IsValidFolder(KillSitesDir)) AssetDatabase.CreateFolder("Assets/PeaHeads/Resources", "KillSites");

            Material lit = LoadOrCreateMaterial($"{KillSitesDir}/KillSiteLit.mat", "Universal Render Pipeline/Lit");
            lit.SetColor("_BaseColor", Color.white);
            lit.SetFloat("_Smoothness", 0.2f);
            lit.enableInstancing = true;

            Material particle = LoadOrCreateMaterial($"{KillSitesDir}/KillSiteParticle.mat",
                "Universal Render Pipeline/Particles/Unlit");
            particle.SetFloat("_Surface", 1f);
            particle.SetFloat("_Blend", 0f);
            particle.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            particle.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            particle.SetFloat("_ZWrite", 0f);
            particle.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            particle.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(lit);
            EditorUtility.SetDirty(particle);
            AssetDatabase.SaveAssets();
            Debug.Log("PlantBodyBuilder: KillSite materials written.");
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
```

- [ ] **Step 2: `MatchManager` — round counter, `RoundReset`, add `KillSites`**

In `Scripts/Match/MatchManager.cs`:

After `private readonly NetworkVariable<byte> _state = new();` add:

```csharp
        // Bumped by the host at every restart so every machine knows a new round began (state can stay Playing).
        private readonly NetworkVariable<int> _round = new();
```

After `public static event Action TeamsChanged;` add:

```csharp
        // Raised on every machine when a new round starts (kill-site patches clear).
        public static event Action RoundReset;
```

In `Awake`, after the `AtmosphereController` line add:

```csharp
            if (!TryGetComponent(out KillSites _)) gameObject.AddComponent<KillSites>();
```

In `OnNetworkSpawn`, after `_state.OnValueChanged += OnStateChanged;` add `_round.OnValueChanged += OnRoundChanged;`. In `OnNetworkDespawn`, after `_state.OnValueChanged -= OnStateChanged;` add `_round.OnValueChanged -= OnRoundChanged;`.

In `Restart()`, directly after the line `_state.Value = (byte)MatchState.Playing;` add `_round.Value++;`.

Add the handler after `OnTeamsChanged`:

```csharp
        private void OnRoundChanged(int previous, int current) => RoundReset?.Invoke();
```

- [ ] **Step 3: Create `Scripts/Match/KillSites.cs`**

```csharp
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
        private MaterialPropertyBlock _block;

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
            _block = new MaterialPropertyBlock();
            MatchManager.RoundReset += Clear;
        }

        private void OnDestroy()
        {
            MatchManager.RoundReset -= Clear;
            if (Instance == this) Instance = null;
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
                Color colour = ground * Mathf.Lerp(0.92f, 1.08f, (float)rng.NextDouble());
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
            Vector3 from = centre + new Vector3(offset.x, 3f, offset.y);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider.GetComponentInParent<PlayerHealth>()) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }
            return found && normal.y >= 0.7f;
        }

        private Transform Flower(Transform root)
        {
            Transform flower = Group(root, "Flower");
            Part(flower, _capsule, StemGreen, new Vector3(0f, 0.15f, 0f), Quaternion.identity,
                new Vector3(0.03f, 0.15f, 0.03f), true);
            Color petal = Faction.Color(0);
            for (int k = 0; k < 5; k++)
                Part(flower, _leaf, petal, new Vector3(0f, 0.31f, 0f),
                    Quaternion.Euler(0f, k * 72f, 0f) * Quaternion.Euler(-20f, 0f, 0f), Vector3.one, true);
            Part(flower, _sphere, FlowerCentre, new Vector3(0f, 0.32f, 0f), Quaternion.identity, Vector3.one * 0.07f, true);
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
            meshRenderer.sharedMaterial = _lit;
            meshRenderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _block.SetColor(BaseColor, colour);
            meshRenderer.SetPropertyBlock(_block);
            return go.transform;
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
```

- [ ] **Step 4: Compile + tests, then build the materials (editor closed)**

Run: EditMode tests command → expected no `error CS`/`warning CS`, all passed (same total as after Task 1).
Run: Build materials command → expected `PlantBodyBuilder: KillSite materials written.`, no `Exception`; `ls PeaHeadsApp/Assets/PeaHeads/Resources/KillSites` shows `KillSiteLit.mat`, `KillSiteParticle.mat` (+ `.meta`).

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Editor/PlantBodyBuilder.cs $S/Scripts/Match/MatchManager.cs $S/Scripts/Match/KillSites.cs $S/Scripts/Match/KillSites.cs.meta $S/Resources/KillSites $S/Resources/KillSites.meta
git commit -m "feat: KillSites patches, burst, materials and round reset" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: PlayerHealth cleanse

**Files:**
- Modify: `Scripts/Player/PlayerHealth.cs`

**Interfaces:**
- Consumes: `KillSites.Instance.Burst/Cleanse` (Task 2), `MatchManager.TeamOf`, `Faction.Color`.
- Produces: `public const float CleanseTime = 0.3f`; `CleansedRpc(byte killerTeam, Vector3 position)`.

No new EditMode test: NGO RPC + renderer code; verified by compile and Task 4.

- [ ] **Step 1: Fields**

After `private Color _flashOriginal;` add:

```csharp
        // Death tint to the killer's colour; renderers hide right after it (colliders go off immediately).
        public const float CleanseTime = 0.3f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Coroutine _hide;
```

- [ ] **Step 2: Send the cleanse from the host**

In `TakeDamage`, after `Killed?.Invoke(this, attackerClientId);` add:

```csharp
            // Host position: every machine seeds the same patch layout from it.
            CleansedRpc((byte)MatchManager.TeamOf(attackerClientId), transform.position);
```

- [ ] **Step 3: RPC, tint, delayed hide, tint reset**

Replace the whole `SetAlive` method with:

```csharp
        private void SetAlive(bool alive)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c is not CharacterController) c.enabled = alive;

            if (_hide != null)
            {
                StopCoroutine(_hide);
                _hide = null;
            }
            if (alive)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);
                ShowRenderers(true);
            }
            else if (isActiveAndEnabled) _hide = StartCoroutine(HideAfterCleanse());
            else ShowRenderers(false);
        }

        private IEnumerator HideAfterCleanse()
        {
            yield return new WaitForSeconds(CleanseTime + 0.05f);
            ShowRenderers(false);
            _hide = null;
        }

        private void ShowRenderers(bool visible)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CleansedRpc(byte killerTeam, Vector3 position) => StartCoroutine(Cleanse(killerTeam, position));

        // Tint every visible part toward the killer's colour, then burst and leave the killer's ground behind.
        private IEnumerator Cleanse(int killerTeam, Vector3 position)
        {
            Color target = Faction.Color(killerTeam);
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            var from = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                from[i] = material && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.clear;
            }

            var block = new MaterialPropertyBlock();
            for (float t = 0f; t < CleanseTime; t += Time.deltaTime)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!renderers[i] || from[i] == Color.clear) continue;
                    renderers[i].GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(from[i], target, t / CleanseTime));
                    renderers[i].SetPropertyBlock(block);
                }
                yield return null;
            }

            KillSites sites = KillSites.Instance;
            if (!sites) yield break;
            sites.Burst(position + Vector3.up, killerTeam);
            sites.Cleanse(position, killerTeam);
        }
```

- [ ] **Step 4: Compile + tests**

Run: EditMode tests command.
Expected: no `error CS`/`warning CS` for PeaHeads; all passed, same total as after Task 2.

- [ ] **Step 5: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs
git commit -m "feat: dying plants are cleansed into the killer's colour and leave kill sites" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Playtest + CLAUDE.md

- [ ] **Step 1: Playtest (human, two machines, DemoArena)** — the spec's 8 manual checks:

1. Kill as Bloom: victim flashes yellow, bursts into petals; flower patch grows on both screens with the same layout.
2. Kill as Blight: purple tint, rot-dust burst, rot patch with tendrils and wilted grass.
3. Kill on an opposing patch: old patch vanishes, new one replaces it.
4. Death on a slope / next to cover: blobs hug the ground, none float or poke through cover.
5. Respawned plant has its own colours back (head team colour, green body).
6. Restart Match / clock end / client join: all patches cleared, including one still growing.
7. 20+ kills: oldest patches disappear, no frame drops.
8. Dummy kill leaves a patch.

- [ ] **Step 2: CLAUDE.md**

- `Match/` entry: add `KillSitePlanner (pure: seeded scatter, takeover, cap), KillSites (added by MatchManager: cleanse burst + killer-ground patches by raycast, takeover, cap 20, cleared on RoundReset; materials from Resources/KillSites via PeaHeads > Build Kill Site Materials)`; MatchManager: `_round counter → static RoundReset`.
- `PlayerHealth` mention: `on death host sends CleansedRpc(killerTeam, position): 0.3 s tint to killer colour, renderers hide 0.35 s after death, tint cleared on revive`.
- `Tests/EditMode/` line: add `KillSitePlannerTests`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document cleanse and kill sites" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
