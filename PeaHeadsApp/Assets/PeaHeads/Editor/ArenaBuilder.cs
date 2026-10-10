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

        [MenuItem("PeaHeads/Build Arena")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var env = ResetEnvironment(scene);
            BuildTerrain(env);
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
