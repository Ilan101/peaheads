using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PeaHeads.EditorTools
{
    // Rebuilds DemoArena's Environment root: terrain, cover, tree ring, decoration, fog.
    // Re-running overwrites hand edits under Environment; nothing else in the scene is created or deleted.
    public static class ArenaBuilder
    {
        private const string ScenePath = "Assets/PeaHeads/Scenes/DemoArena.unity";
        private const string ArtDir = "Assets/PeaHeads/Art/Arena";
        private const string TerrainDir = ArtDir + "/Terrain";
        private const float TerrainSize = 80f;
        private const float TerrainHeight = 10f;
        private const float PlayHalf = 20f;
        private const int Seed = 42;

        private static readonly Color FogColor = new(0.62f, 0.8f, 0.95f);

        private const string KenneyDir = ArtDir + "/Kenney/";
        private const float Sink = 0.1f;

        // Half the cover. Each row is mirrored through the origin, (x, z, rot) -> (-x, -z, rot + 180), so both
        // spawns get the same cover; a row at the origin is placed once. Heights in metres: tall cover >= 2.3
        // hides a standing head (top ~2.15 m), low cover ~1.1 hides the body only.
        private static readonly (string model, float x, float z, float rot, float height)[] Cover =
        {
            ("rock_tallA", 0f, 0f, 0f, 2.6f),
            ("rock_largeA", 2.5f, -8f, 15f, 2.4f),
            ("log_large", -6f, -5f, 20f, 1.1f),
            ("stump_oldTall", 6f, -3f, 0f, 2.4f),
            ("rock_largeD", -11f, -2f, 40f, 2.5f),
            ("log_stackLarge", 11f, -9f, 90f, 1.2f),
            ("rock_tallB", -5f, -13f, 0f, 2.5f),
            ("stump_round", 14f, 6f, 0f, 1.0f),
            ("rock_largeB", -16f, 12f, 60f, 2.3f),
            ("tree_oak", -15f, -14f, 0f, 6f),
        };

        private static readonly string[] Trees =
            { "tree_pineTallA_detailed", "tree_pineTallB_detailed", "tree_pineRoundC", "tree_oak", "tree_fat" };

        private static readonly (string model, float minHeight, float maxHeight)[] Decoration =
        {
            ("grass_large", 0.5f, 0.8f), ("grass_leafsLarge", 0.5f, 0.8f),
            ("plant_bushLarge", 0.9f, 1.4f), ("plant_bushDetailed", 0.9f, 1.4f),
            ("mushroom_redGroup", 0.4f, 0.6f), ("mushroom_tanGroup", 0.4f, 0.6f),
        };

        [MenuItem("PeaHeads/Build Arena")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var env = ResetEnvironment(scene);
            var terrain = BuildTerrain(env);
            var rng = new System.Random(Seed);
            var cover = PlaceCover(env, terrain);
            BuildBoundary(env);
            PlaceTreeRing(env, terrain, rng);
            ScatterDecoration(env, terrain, cover, rng);
            ApplyRenderSettings();

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
            return placed;
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

        // Grass, bushes and mushrooms inside the play area, kept off paths, spawns and cover. No colliders.
        private static void ScatterDecoration(Transform env, Terrain terrain, List<Vector2> cover, System.Random rng)
        {
            var parent = Group(env, "Decoration");
            var spawns = new[] { new Vector2(0f, -12f), new Vector2(0f, 12f) };
            var placed = new List<Vector2>();
            const float extent = PlayHalf - 1.5f;
            for (int attempt = 0; attempt < 400 && placed.Count < 40; attempt++)
            {
                var p = new Vector2(Range(rng, -extent, extent), Range(rng, -extent, extent));
                var (model, minHeight, maxHeight) = Decoration[rng.Next(Decoration.Length)];
                float rot = Range(rng, 0f, 360f), height = Range(rng, minHeight, maxHeight);

                if (PathDistance(p) < 2f || NearAny(p, spawns, 3f) || NearAny(p, cover, 2.8f) || NearAny(p, placed, 1.5f))
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
            t.localScale *= height / WorldBounds(go).size.y;

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
                Layer("Grass", "aerial_grass_rock"),
                Layer("Path", "forest_ground_04"),
                Layer("ForestFloor", "forest_leaves_02"),
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

        private static TerrainLayer Layer(string name, string texture)
        {
            string dir = ArtDir + "/Textures/";
            string normalPath = dir + texture + "_nor_gl_1k.jpg";
            var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (importer == null) throw new System.IO.FileNotFoundException($"Missing texture {normalPath}");
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            var layer = LoadOrCreate<TerrainLayer>($"{TerrainDir}/{name}.terrainlayer");
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + texture + "_diff_1k.jpg");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            layer.tileSize = new Vector2(4f, 4f);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
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
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 70f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.85f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.2f);

            var sun = GameObject.Find("Directional Light");
            if (sun && sun.TryGetComponent(out Light light))
            {
                light.color = new Color(1f, 0.95f, 0.84f);
                light.intensity = 1.3f;
            }
            else Debug.LogWarning("[PeaHeads] No 'Directional Light' found; sun left unchanged.");
        }
    }
}
