using System.Collections.Generic;
using PeaHeads.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads.EditorTools
{
    // Menu PeaHeads > Build Plant Body: writes the peashooter meshes/materials and rebuilds the Gen_* parts of the
    // Player and TargetDummy prefabs. Overwrites hand edits on Gen_* objects (same rule as ArenaBuilder).
    public static class PlantBodyBuilder
    {
        private const string ArtDir = "Assets/PeaHeads/Art/Plant";
        private const string PlayerPath = "Assets/PeaHeads/Prefabs/Player.prefab";
        private const string DummyPath = "Assets/PeaHeads/Prefabs/TargetDummy.prefab";
        private const string GenPrefix = "Gen_";
        private const float HeadScale = 1.08f;
        private const float StemThickness = 0.18f;

        private sealed class Parts
        {
            public Mesh Snout, MouthHole, GroundLeaf, CollarLeaf, TuftLeaf, EyeLid, Sphere;
            public Material PlantGreen, LeafGreen, EyeWhite, Pupil, MouthDark, Dirt;
        }

        [MenuItem("PeaHeads/Build Plant Body")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder("Assets/PeaHeads/Art", "Plant");
            var parts = new Parts
            {
                Snout = SaveMesh(PlantMeshes.Lathe(PlantMeshes.SnoutProfile, 32), "Snout"),
                MouthHole = SaveMesh(PlantMeshes.Disc(PlantMeshes.MouthHoleRadius, 32), "MouthHole"),
                // Short enough not to poke far past cover (tip ~0.62 m from the root, ~0.85 m planted).
                GroundLeaf = SaveMesh(PlantMeshes.Leaf(0.5f, 0.26f, 0.06f, 10), "Leaf"),
                CollarLeaf = SaveMesh(PlantMeshes.Leaf(0.38f, 0.22f, 0.06f, 8), "CollarLeaf"),
                TuftLeaf = SaveMesh(PlantMeshes.Leaf(0.42f, 0.16f, 0.22f, 10), "TuftLeaf"),
                // Eye-local units (eye sphere radius 0.5): a little larger than the eye so it covers the pupil.
                EyeLid = SaveMesh(PlantMeshes.Dome(0.56f, 24, 8), "EyeLid"),
                // Same built-in sphere as the Head (3D Object > Sphere).
                Sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"),
                PlantGreen = SaveMaterial("PlantGreen", new Color(0.42f, 0.76f, 0.29f)),
                LeafGreen = SaveMaterial("LeafGreen", new Color(0.27f, 0.58f, 0.18f)),
                EyeWhite = SaveMaterial("EyeWhite", Color.white),
                Pupil = SaveMaterial("Pupil", new Color(0.05f, 0.05f, 0.05f)),
                MouthDark = SaveMaterial("MouthDark", new Color(0.08f, 0.14f, 0.05f)),
                Dirt = SaveMaterial("Dirt", new Color(0.42f, 0.29f, 0.17f)),
            };
            AssetDatabase.SaveAssets();

            BuildPrefab(PlayerPath, parts, true);
            BuildPrefab(DummyPath, parts, false);
            AssetDatabase.SaveAssets();
            Debug.Log("PlantBodyBuilder: rebuilt Player and TargetDummy bodies.");
        }

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

            // Same Lit shader with emission on, so the glow variant is kept in builds (mushroom caps).
            Material glow = LoadOrCreateMaterial($"{KillSitesDir}/KillSiteGlow.mat", "Universal Render Pipeline/Lit");
            glow.SetColor("_BaseColor", Color.white);
            glow.SetFloat("_Smoothness", 0.5f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", Color.white);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            glow.enableInstancing = true;
            EditorUtility.SetDirty(glow);

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

        private static void BuildPrefab(string path, Parts parts, bool isPlayer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old = new List<GameObject>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(GenPrefix)) old.Add(t.gameObject);
                foreach (GameObject go in old)
                    if (go) Object.DestroyImmediate(go);

                Transform pivot = Find(root.transform, "HeadPivot");
                Transform head = Find(pivot, "Head");
                Transform snout = Find(pivot, "Snout");
                Transform visual = Find(root.transform, "Visual");
                Transform leafBase = Find(visual, "LeafBase");
                Transform stem = Find(visual, "Stem");

                head.localScale = Vector3.one * HeadScale;
                Material headMaterial = head.GetComponent<MeshRenderer>().sharedMaterial;

                snout.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                snout.localScale = Vector3.one;
                snout.GetComponent<MeshFilter>().sharedMesh = parts.Snout;
                snout.GetComponent<MeshRenderer>().sharedMaterial = headMaterial;

                var hidden = new List<Renderer> { head.GetComponent<Renderer>(), snout.GetComponent<Renderer>() };
                MeshRenderer hole = Part("Gen_MouthHole", snout, parts.MouthHole, parts.MouthDark,
                    new Vector3(0f, 0f, PlantMeshes.MouthHoleZ), Quaternion.identity, Vector3.one);
                hidden.Add(hole);

                Eye(pivot, parts, -1f, hidden);
                Transform winkEye = Eye(pivot, parts, 1f, hidden);

                Transform collar = Group("Gen_Collar", pivot, Vector3.zero);
                for (int i = 0; i < 5; i++)
                {
                    // Offset by 36° so no collar leaf sits straight under the snout.
                    Quaternion yaw = Quaternion.Euler(0f, 36f + i * 72f, 0f);
                    hidden.Add(Part($"Gen_CollarLeaf{i}", collar, parts.CollarLeaf, parts.LeafGreen,
                        yaw * new Vector3(0f, -0.42f, 0.2f), yaw * Quaternion.Euler(35f, 0f, 0f), Vector3.one));
                }

                Transform tuft = Group("Gen_Tuft", pivot, new Vector3(0f, 0.38f, -0.32f));
                for (int i = 0; i < 2; i++)
                    hidden.Add(Part($"Gen_TuftLeaf{i}", tuft, parts.TuftLeaf, parts.LeafGreen, Vector3.zero,
                        Quaternion.Euler(-35f, 180f + (i == 0 ? -18f : 18f), 0f), Vector3.one));

                Transform ground = Group("Gen_GroundLeaves", visual, new Vector3(0f, 0.02f, 0f));
                for (int i = 0; i < 5; i++)
                {
                    Quaternion yaw = Quaternion.Euler(0f, i * 72f, 0f);
                    Part($"Gen_GroundLeaf{i}", ground, parts.GroundLeaf, parts.LeafGreen,
                        yaw * new Vector3(0f, 0f, 0.12f), yaw * Quaternion.Euler(-4f, 0f, 0f), Vector3.one);
                }

                leafBase.GetComponent<MeshRenderer>().sharedMaterial = parts.Dirt;
                leafBase.localPosition = new Vector3(0f, 0.06f, 0f);
                leafBase.localScale = new Vector3(0.7f, 0.06f, 0.7f);
                stem.GetComponent<MeshRenderer>().sharedMaterial = parts.PlantGreen;

                if (root.TryGetComponent(out StemBender bender)) SetFloat(bender, "_thickness", StemThickness);
                else stem.localScale = new Vector3(StemThickness, stem.localScale.y, StemThickness);
                if (root.TryGetComponent(out PlantVisuals plantVisuals)) SetObject(plantVisuals, "_groundLeaves", ground);
                if (root.TryGetComponent(out PlayerCameraRig rig)) SetRenderers(rig, "_hiddenInFirstPerson", hidden);

                if (isPlayer)
                {
                    if (!root.TryGetComponent(out PlantFace face)) face = root.AddComponent<PlantFace>();
                    SetObject(face, "_snout", snout);
                    SetObject(face, "_mouthHole", hole.transform);
                    // Right eye only; tinted with the head, swung back into the head while open.
                    MeshRenderer lid = Part("Gen_LidR", winkEye, parts.EyeLid, headMaterial, Vector3.zero,
                        Quaternion.Euler(PlantFace.OpenLidAngle, 0f, 0f), Vector3.one);
                    hidden.Add(lid);
                    if (root.TryGetComponent(out PlayerCameraRig playerRig)) SetRenderers(playerRig, "_hiddenInFirstPerson", hidden);
                    if (root.TryGetComponent(out PlayerNetSync netSync))
                        SetRenderers(netSync, "_tinted", new List<Renderer> { head.GetComponent<Renderer>(), snout.GetComponent<Renderer>(), lid });
                    SetObject(face, "_winkLid", lid.transform);
                    SetFloat(face, "_openHole", PlantFace.DefaultOpenHole);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Eye on the head surface, looking out along its own direction; pupil child pushed forward.
        private static Transform Eye(Transform pivot, Parts parts, float side, List<Renderer> hidden)
        {
            Vector3 direction = new Vector3(0.17f * side, 0.20f, 0.38f).normalized;
            MeshRenderer eye = Part(side < 0f ? "Gen_EyeL" : "Gen_EyeR", pivot, parts.Sphere, parts.EyeWhite,
                direction * 0.50f, Quaternion.LookRotation(direction), Vector3.one * 0.22f);
            MeshRenderer pupil = Part("Gen_Pupil", eye.transform, parts.Sphere, parts.Pupil,
                new Vector3(0f, 0f, 0.40f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.25f));
            hidden.Add(eye);
            hidden.Add(pupil);
            return eye.transform;
        }

        private static MeshRenderer Part(string name, Transform parent, Mesh mesh, Material material,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            return meshRenderer;
        }

        private static Transform Group(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static Transform Find(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (!child) throw new System.InvalidOperationException($"{parent.name}/{name} not found");
            return child;
        }

        // Updates an existing asset in place so its GUID (and every prefab reference) survives rebuilds.
        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{ArtDir}/{name}.asset";
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        private static Material SaveMaterial(string name, Color color)
        {
            string path = $"{ArtDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRenderers(Object target, string field, List<Renderer> values)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
