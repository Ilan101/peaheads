using System.Collections.Generic;
using System.Linq;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PeaHeads.EditorTools
{
    // Rebuilds DemoArena's Environment root: terrain, cover, mushroom spots, spawn points, tree ring, decoration,
    // lighting. Re-running overwrites hand edits under Environment. The root Match and HeadTracking objects and the
    // MushroomPickup prefab are created only if missing; nothing else outside Environment is touched.
    public static class ArenaBuilder
    {
        private const string ScenePath = "Assets/PeaHeads/Scenes/DemoArena.unity";
        private const string ArtDir = "Assets/PeaHeads/Art/Arena";
        private const string TerrainDir = ArtDir + "/Terrain";
        private const float TerrainSize = 80f;
        private const float TerrainHeight = 10f;
        private const float PlayHalf = 20f;
        private const int Seed = 42;

        private const string KenneyDir = ArtDir + "/Kenney/";
        private const string MaterialDir = ArtDir + "/Materials";
        private const float Sink = 0.1f;
        private const float MaxCoverWidth = 4.5f; // Kenney's "large" rocks are 8-14 m wide at cover height

        // Kenney's own colours (orange rocks with teal tops, rocks and stumps the same brown) are swapped by
        // material name for these flat, matte URP materials.
        private static readonly (string name, Color color)[] Palette =
        {
            ("dirt", new Color(0.58f, 0.6f, 0.64f)),          // rock stone
            ("_defaultMat", new Color(0.86f, 0.86f, 0.83f)),  // rock highlights, mushroom stems
            ("grass", new Color(0.38f, 0.8f, 0.22f)),         // rock moss, grass tufts, bushes
            ("leafsGreen", new Color(0.33f, 0.74f, 0.2f)),
            ("leafsDark", new Color(0.16f, 0.55f, 0.25f)),
            ("woodBark", new Color(0.6f, 0.4f, 0.22f)),       // oak
            ("woodBarkDark", new Color(0.42f, 0.27f, 0.15f)),
            ("woodDark", new Color(0.45f, 0.29f, 0.16f)),
            ("woodInner", new Color(0.92f, 0.76f, 0.52f)),
            ("colorRed", new Color(0.9f, 0.18f, 0.15f)),
            ("colorTan", new Color(0.9f, 0.75f, 0.5f)),
        };

        // Half the cover. Each row is mirrored through the origin, (x, z, rot) -> (-x, -z, rot + 180), so both
        // spawns get the same cover; a row at the origin is placed once. Heights in metres: tall cover >= 2.3
        // hides a standing head (top ~2.15 m); a planted head tops out at ~1.8 m; stumps/logs under that hide
        // the body only.
        private static readonly (string model, float x, float z, float rot, float height)[] Cover =
        {
            ("rock_tallA", 0f, 0f, 0f, 3.2f),
            ("rock_largeA", 2.5f, -8f, 15f, 3f),
            ("log_large", -6f, -5f, 20f, 1.3f),
            ("stump_oldTall", 6f, -3f, 0f, 2.23f),
            ("log_stackLarge", 11f, -9f, 90f, 1.46f),
            ("rock_tallB", -5f, -13f, 0f, 2.6f),
            ("stump_round", 15f, 8f, 0f, 1.55f),
            ("rock_largeB", -17f, 13.5f, 60f, 2.6f),
            ("tree_oak", -15f, -14f, 0f, 6f),
            ("tree_oak", -8f, -17f, 0f, 6f),
            ("tree_fat", -17f, 3f, 0f, 5.5f),
            ("tree_pineRoundC", 3f, -18f, 0f, 6f),
            ("tree_pineTallA_detailed", 12f, -4f, 0f, 7f),
        };

        // Hand-tuned in the editor (2026-10-10), placed once each, not mirrored.
        private static readonly (string model, float x, float z, float rot, float height)[] SingleCover =
        {
            ("rock_largeD", -11f, -2f, 40f, 3f),
            ("rock_largeD", 11.64f, 3.96f, 220f, 3f),
            ("tree_pineTallB_detailed", -9f, 15f, 180f, 7f),
            ("tree_oak", -6.76f, 6.48f, 0f, 6f),
            ("tree_oak", 4.93f, 8.25f, 0f, 6f),
            ("tree_oak", 8.68f, -12.85f, 0f, 6f),
        };

        private static readonly string[] Trees =
            { "tree_pineTallA_detailed", "tree_pineTallB_detailed", "tree_pineRoundC", "tree_oak", "tree_fat" };

        private static readonly (string model, float minHeight, float maxHeight)[] Decoration =
        {
            ("grass_large", 0.5f, 0.8f), ("grass_leafsLarge", 0.5f, 0.8f),
            ("plant_bushLarge", 0.9f, 1.4f), ("plant_bushDetailed", 0.9f, 1.4f),
            // No mushrooms here: they would look like pickups.
        };

        private const string PickupPrefabPath = "Assets/PeaHeads/Prefabs/MushroomPickup.prefab";
        private const string ParticlesMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
        private const string SfxDir = "Assets/PeaHeads/Audio/SFX/UI/";

        // Mushroom pickup spots, clear of cover footprints (>= 1.5 m), spawns and dummies. Mostly mirrored pairs;
        // (-4, 2) replaces (-7.5, 6.5), which the hand-placed oak covers.
        private static readonly Vector2[] MushroomSpots =
        {
            new(-2.5f, -4.5f), new(2.5f, 4.5f), new(7.5f, -6.5f), new(-4f, 2f), new(-10f, -8.5f),
            new(10f, 8.5f), new(16f, 0f), new(-16f, 0f), new(-15f, 6f), new(15f, -6f),
        };

        // Extra team-0 spawn points (team 1 mirrored, facing -z). The scene's root SpawnPoint_0/1 stay too.
        private static readonly Vector2[] ExtraSpawns = { new(-8f, -13f), new(12f, -14f), new(-17.5f, -10.5f) };

        [MenuItem("PeaHeads/Build Arena")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            RemapKenneyMaterials();
            var env = ResetEnvironment(scene);
            var terrain = BuildTerrain(env);
            var rng = new System.Random(Seed);
            var blocked = PlaceCover(env, terrain);
            blocked.AddRange(PlaceMushrooms(env, terrain));
            blocked.AddRange(PlaceSpawnPoints(env));
            BuildBoundary(env);
            PlaceTreeRing(env, terrain, rng);
            ScatterDecoration(env, terrain, blocked, rng);
            ApplyRenderSettings();
            EnsureMatchObject(scene);
            EnsureHeadTrackingObject(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PeaHeads] Arena built in {ScenePath}");
        }

        private static Transform ResetEnvironment(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Environment") Object.DestroyImmediate(root);
            return new GameObject("Environment").transform;
        }

        private static List<Vector2> PlaceCover(Transform env, Terrain terrain)
        {
            var parent = Group(env, "Cover");
            var placed = new List<Vector2>();
            foreach (var (model, x, z, rot, height) in Cover)
            {
                Place(model, parent, terrain, new Vector2(x, z), rot, height, true);
                placed.Add(new Vector2(x, z));
                if (x == 0f && z == 0f) continue;
                Place(model, parent, terrain, new Vector2(-x, -z), rot + 180f, height, true);
                placed.Add(new Vector2(-x, -z));
            }
            foreach (var (model, x, z, rot, height) in SingleCover)
            {
                Place(model, parent, terrain, new Vector2(x, z), rot, height, true);
                placed.Add(new Vector2(x, z));
            }
            return placed;
        }

        private static List<Vector2> PlaceMushrooms(Transform env, Terrain terrain)
        {
            var prefab = EnsurePickupPrefab();
            var parent = Group(env, "Mushrooms");
            var placed = new List<Vector2>();
            foreach (Vector2 xz in MushroomSpots)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.name = $"Mushroom_{placed.Count}";
                var ground = new Vector3(xz.x, 0f, xz.y);
                ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
                go.transform.position = ground;

                var spot = new SerializedObject(go.GetComponent<MushroomSpot>());
                spot.FindProperty("_index").intValue = placed.Count;
                spot.ApplyModifiedPropertiesWithoutUndo();
                placed.Add(xz);
            }
            return placed;
        }

        // Returns every spawn position (root SpawnPoint_0/1 included) so decoration keeps clear of them.
        private static List<Vector2> PlaceSpawnPoints(Transform env)
        {
            var parent = Group(env, "SpawnPoints");
            var placed = new List<Vector2> { new(0f, -12f), new(0f, 12f) };
            foreach (Vector2 half in ExtraSpawns)
            for (int team = 0; team < 2; team++)
            {
                Vector2 xz = team == 0 ? half : -half;
                var go = new GameObject($"SpawnPoint_T{team}_{placed.Count}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(new Vector3(xz.x, 0f, xz.y),
                    Quaternion.Euler(0f, team == 0 ? 0f : 180f, 0f));
                var point = new SerializedObject(go.AddComponent<SpawnPoint>());
                point.FindProperty("_team").intValue = team;
                point.ApplyModifiedPropertiesWithoutUndo();
                placed.Add(xz);
            }
            return placed;
        }

        // Created once; hand edits to the prefab survive later builds.
        private static GameObject EnsurePickupPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
            if (existing) return existing;

            var root = new GameObject("MushroomPickup");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(KenneyDir + "mushroom_redGroup.fbx"), visual);
            model.transform.localScale *= 0.9f / WorldBounds(model).size.y;
            Bounds bounds = WorldBounds(model);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var cap = GlowMaterial("MushroomGlowCap", new Color(1f, 0.25f, 0.2f));
            var stem = GlowMaterial("MushroomGlowStem", new Color(1f, 0.92f, 0.75f));
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name == "colorRed" ? cap : stem).ToArray();

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.3f);
            light.range = 3f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;

            var sparkles = BuildSparkles(root.transform);

            var spot = new SerializedObject(root.AddComponent<MushroomSpot>());
            spot.FindProperty("_visual").objectReferenceValue = visual;
            spot.FindProperty("_glow").objectReferenceValue = light;
            spot.FindProperty("_sparkles").objectReferenceValue = sparkles;
            spot.FindProperty("_collectSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "Notification_Chime.wav");
            spot.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static ParticleSystem BuildSparkles(Transform parent)
        {
            var ps = new GameObject("Sparkles").AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 1.6f;
            main.startSpeed = 0.4f;
            main.startSize = 0.08f;
            main.startColor = new Color(1f, 0.85f, 0.4f);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 100;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var material = AssetDatabase.LoadAssetAtPath<Material>(ParticlesMaterialPath);
            if (!material) Debug.LogWarning($"[PeaHeads] {ParticlesMaterialPath} not found; sparkles have no material.");
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        private static Material GlowMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.5f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // In-scene NetworkObject outside Environment: created once, never deleted, so its network id stays stable.
        private static void EnsureMatchObject(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(r => r.name == "Match")) return;
            var go = new GameObject("Match");
            go.AddComponent<NetworkObject>();
            var match = new SerializedObject(go.AddComponent<MatchManager>());
            go.AddComponent<MushroomSpawner>();
            go.AddComponent<ScoreHUD>();
            match.FindProperty("_confettiPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PeaHeads/Prefabs/VFX/ConfettiCelebration.prefab");
            match.FindProperty("_endSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "Notification_End.wav");
            match.ApplyModifiedPropertiesWithoutUndo();
        }

        // One webcam receiver per machine, as a scene object: on the Player prefab, remote copies would fight over
        // the UDP port. Created once, never deleted.
        private static void EnsureHeadTrackingObject(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(r => r.name == "HeadTracking")) return;
            new GameObject("HeadTracking").AddComponent<HeadTrackingReceiver>();
        }

        // Invisible walls on the play-area edge; the tree ring is what players see.
        private static void BuildBoundary(Transform env)
        {
            var parent = Group(env, "Boundary");
            const float height = 3f, thickness = 0.5f, length = PlayHalf * 2f + thickness * 2f;
            const float offset = PlayHalf + thickness / 2f;
            Wall(parent, "Wall_N", new Vector3(0f, height / 2f, offset), new Vector3(length, height, thickness));
            Wall(parent, "Wall_S", new Vector3(0f, height / 2f, -offset), new Vector3(length, height, thickness));
            Wall(parent, "Wall_E", new Vector3(offset, height / 2f, 0f), new Vector3(thickness, height, length));
            Wall(parent, "Wall_W", new Vector3(-offset, height / 2f, 0f), new Vector3(thickness, height, length));
        }

        private static void Wall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        // Jittered grid of trees in the band just outside the play area. Every random draw happens whether or
        // not the cell is kept, so the sequence (and layout) only changes if the grid does.
        private static void PlaceTreeRing(Transform env, Terrain terrain, System.Random rng)
        {
            var parent = Group(env, "TreeRing");
            const float spacing = 3.5f, reach = 36f;
            for (float z = -reach; z <= reach; z += spacing)
            for (float x = -reach; x <= reach; x += spacing)
            {
                var p = new Vector2(x + Range(rng, -1.2f, 1.2f), z + Range(rng, -1.2f, 1.2f));
                bool keep = rng.NextDouble() < 0.55;
                string model = Trees[rng.Next(Trees.Length)];
                float rot = Range(rng, 0f, 360f), height = Range(rng, 5f, 9f);

                float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if (!keep || edge < PlayHalf + 2f || edge > reach) continue;
                Place(model, parent, terrain, p, rot, height, false);
            }
        }

        // Grass and bushes inside the play area, kept off paths, spawns, mushrooms and cover. No colliders.
        private static void ScatterDecoration(Transform env, Terrain terrain, List<Vector2> cover, System.Random rng)
        {
            var parent = Group(env, "Decoration");
            var placed = new List<Vector2>();
            const float extent = PlayHalf - 1.5f;
            for (int attempt = 0; attempt < 400 && placed.Count < 40; attempt++)
            {
                var p = new Vector2(Range(rng, -extent, extent), Range(rng, -extent, extent));
                var (model, minHeight, maxHeight) = Decoration[rng.Next(Decoration.Length)];
                float rot = Range(rng, 0f, 360f), height = Range(rng, minHeight, maxHeight);

                if (PathDistance(p) < 2f || NearAny(p, cover, 2.8f) || NearAny(p, placed, 1.5f))
                    continue;
                Place(model, parent, terrain, p, rot, height, false);
                placed.Add(p);
            }
        }

        // Instantiates a Kenney model scaled so it is `height` metres tall, centred on xz, sitting on the terrain.
        private static void Place(string model, Transform parent, Terrain terrain, Vector2 xz, float yRot,
            float height, bool solid)
        {
            string path = KenneyDir + model + ".fbx";
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                throw new System.IO.FileNotFoundException($"Missing Kenney model {path}");
            if (!importer.isReadable)
            {
                importer.isReadable = true; // MeshColliders bake from it
                importer.SaveAndReimport();
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            var t = go.transform;
            t.rotation = Quaternion.Euler(0f, yRot, 0f) * t.rotation;
            t.localScale *= (height + Sink) / WorldBounds(go).size.y;
            if (solid) CapFootprint(go);

            var ground = new Vector3(xz.x, 0f, xz.y);
            ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y - Sink;
            Bounds b = WorldBounds(go);
            t.position += new Vector3(ground.x - b.center.x, ground.y - b.min.y, ground.z - b.center.z);

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                mf.gameObject.isStatic = true;
                if (solid) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            go.isStatic = true;
        }

        // Squashes the prop horizontally (height unchanged) if its footprint is wider than MaxCoverWidth.
        private static void CapFootprint(GameObject go)
        {
            Vector3 size = WorldBounds(go).size;
            float factor = MaxCoverWidth / Mathf.Max(size.x, size.z);
            if (factor >= 1f) return;

            var t = go.transform;
            Vector3 localUp = Quaternion.Inverse(t.rotation) * Vector3.up;
            Vector3 squash = Vector3.one * factor;
            if (Mathf.Abs(localUp.x) > 0.9f) squash.x = 1f;
            else if (Mathf.Abs(localUp.y) > 0.9f) squash.y = 1f;
            else squash.z = 1f;
            t.localScale = Vector3.Scale(t.localScale, squash);
        }

        // From mesh bounds + transforms (always current), not Renderer.bounds (can lag behind transform edits).
        private static Bounds WorldBounds(GameObject go)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) throw new System.InvalidOperationException($"{go.name} has no meshes");
            var b = new Bounds(filters[0].transform.TransformPoint(filters[0].sharedMesh.bounds.center), Vector3.zero);
            foreach (var mf in filters)
            {
                Bounds mesh = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    b.Encapsulate(mf.transform.TransformPoint(mesh.center + Vector3.Scale(mesh.extents, sign)));
                }
            }
            return b;
        }

        private static Transform Group(Transform env, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(env, false);
            return t;
        }

        private static bool NearAny(Vector2 p, IEnumerable<Vector2> points, float radius)
        {
            foreach (var q in points)
                if ((p - q).sqrMagnitude < radius * radius) return true;
            return false;
        }

        private static float Range(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        private static Terrain BuildTerrain(Transform env)
        {
            if (!AssetDatabase.IsValidFolder(TerrainDir)) AssetDatabase.CreateFolder(ArtDir, "Terrain");
            var layers = new[]
            {
                Layer("Grass", new Color(0.47f, 0.78f, 0.3f)),
                Layer("Path", new Color(0.85f, 0.7f, 0.45f)),
                Layer("ForestFloor", new Color(0.32f, 0.6f, 0.24f)),
            };

            var data = LoadOrCreate<TerrainData>(TerrainDir + "/ArenaTerrain.asset");
            data.heightmapResolution = 129;
            data.alphamapResolution = 128;
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            data.terrainLayers = layers;
            data.SetHeights(0, 0, Heights(data.heightmapResolution));
            data.SetAlphamaps(0, 0, Splat(data.alphamapResolution));
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(env, false);
            go.transform.position = new Vector3(-TerrainSize / 2f, 0f, -TerrainSize / 2f);
            go.isStatic = true;

            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            return terrain;
        }

        // Flat clearing inside the play area, rising into noisy hills beyond it.
        private static float[,] Heights(int res)
        {
            var heights = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector2 p = ToWorld(x / (res - 1f), z / (res - 1f));
                float beyond = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (PlayHalf + 1f);
                if (beyond <= 0f) continue;
                float hill = 3f + 3f * Mathf.PerlinNoise(p.x * 0.08f + 100f, p.y * 0.08f + 100f);
                heights[z, x] = Mathf.SmoothStep(0f, 1f, beyond / 12f) * hill / TerrainHeight;
            }
            return heights;
        }

        // Layers: 0 grass, 1 dirt path, 2 forest floor (outside the play area).
        private static float[,,] Splat(int res)
        {
            var alpha = new float[res, res, 3];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector2 p = ToWorld((x + 0.5f) / res, (z + 0.5f) / res);
                float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlayHalf, PlayHalf + 2f, edge));
                float width = 1.1f + 0.5f * Mathf.PerlinNoise(p.x * 0.3f, p.y * 0.3f);
                float path = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width, width + 0.6f, PathDistance(p))))
                             * (1f - forest);
                alpha[z, x, 0] = 1f - path - forest;
                alpha[z, x, 1] = path;
                alpha[z, x, 2] = forest;
            }
            return alpha;
        }

        // Distance to the dirt paths: spawn to spawn along x = 0, and a cross path along z = 0, both wiggling.
        private static float PathDistance(Vector2 p)
        {
            var alongZ = new Vector2(p.x - 0.8f * Mathf.Sin(p.y * 0.35f), p.y);
            var alongX = new Vector2(p.x, p.y - 0.8f * Mathf.Sin(p.x * 0.3f));
            return Mathf.Min(SegmentDistance(alongZ, new Vector2(0f, -15f), new Vector2(0f, 15f)),
                             SegmentDistance(alongX, new Vector2(-15f, 0f), new Vector2(15f, 0f)));
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // u, v in 0..1 across the terrain -> world XZ.
        private static Vector2 ToWorld(float u, float v) =>
            new(u * TerrainSize - TerrainSize / 2f, v * TerrainSize - TerrainSize / 2f);

        // Flat-colour layer, to match the low-poly props.
        private static TerrainLayer Layer(string name, Color color)
        {
            var layer = LoadOrCreate<TerrainLayer>($"{TerrainDir}/{name}.terrainlayer");
            layer.diffuseTexture = SolidTexture(name, color);
            layer.normalMapTexture = null;
            layer.tileSize = new Vector2(4f, 4f);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Texture2D SolidTexture(string name, Color color)
        {
            string path = $"{TerrainDir}/{name}.png";
            var pixels = new Color[16];
            System.Array.Fill(pixels, color);
            var tex = new Texture2D(4, 4);
            tex.SetPixels(pixels);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void RemapKenneyMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder(ArtDir, "Materials");
            var palette = new Dictionary<string, Material>();
            foreach (var (name, color) in Palette) palette[name] = PaletteMaterial(name, color);

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtDir + "/Kenney" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var current = importer.GetExternalObjectMap();
                bool changed = false;

                // Palette assets share the Kenney names, so this works before and after the first remap.
                var used = new HashSet<string>();
                foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m) used.Add(m.name);

                foreach (string name in used)
                {
                    if (!palette.TryGetValue(name, out var mat))
                    {
                        Debug.LogWarning($"[PeaHeads] {path}: material '{name}' has no palette entry; Kenney colour kept.");
                        continue;
                    }
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                    if (current.TryGetValue(id, out var existing) && existing == mat) continue;
                    importer.AddRemap(id, mat);
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        private static Material PaletteMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material TerrainMaterial()
        {
            string path = TerrainDir + "/ArenaTerrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // Reusing existing assets keeps their GUIDs stable, so reruns don't churn references.
        private static T LoadOrCreate<T>(string path) where T : Object, new()
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = new T();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void ApplyRenderSettings()
        {
            // Neutral ambient: a blue sky term tints every upward face.
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.85f, 0.85f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.65f, 0.65f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.42f, 0.36f);

            var sun = GameObject.Find("Directional Light");
            if (sun && sun.TryGetComponent(out Light light))
            {
                light.color = new Color(1f, 0.97f, 0.9f);
                light.intensity = 1.4f;
            }
            else Debug.LogWarning("[PeaHeads] No 'Directional Light' found; sun left unchanged.");
        }
    }
}
