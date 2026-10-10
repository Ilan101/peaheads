using System.Linq;
using PeaHeads.Net;
using PeaHeads.Net.Test;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PeaHeads.EditorTools
{
    // Generates the hour 0-2 netcode validation scene. Safe to re-run; overwrites its own assets only.
    public static class NetTestSceneBuilder
    {
        private const string Root = "Assets/PeaHeads";
        private const string PrefabDir = Root + "/Prefabs/NetTest";
        private const string ScenePath = Root + "/Scenes/NetTest.unity";
        private const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("PeaHeads/Build Net Test Scene")]
        public static void Build()
        {
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root + "/Prefabs", "NetTest");
            EnsureFolder(Root, "Scenes");

            var peaMat = CreateMaterial("NetTestPea", new Color(0.35f, 0.85f, 0.2f));
            var playerMat = CreateMaterial("NetTestPlayer", Color.white);
            var groundMat = CreateMaterial("NetTestGround", new Color(0.45f, 0.6f, 0.35f));
            var coverMat = CreateMaterial("NetTestCover", new Color(0.5f, 0.45f, 0.4f));

            var pea = BuildPeaPrefab(peaMat);
            var player = BuildPlayerPrefab(playerMat, pea);
            var prefabList = EnsurePrefabList(player, pea);
            BuildScene(player, prefabList, groundMat, coverMat);
            AddSceneToBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log($"[PeaHeads] Net test scene built at {ScenePath}");
        }

        private static GameObject BuildPeaPrefab(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "NetTestPea";
            Object.DestroyImmediate(go.GetComponent<SphereCollider>());
            go.transform.localScale = Vector3.one * 0.4f;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetTestPea>();

            return SavePrefab(go, "NetTestPea");
        }

        private static GameObject BuildPlayerPrefab(Material mat, GameObject peaPrefab)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "NetTestPlayer";
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.5f, 0.5f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.3f);

            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            var player = go.AddComponent<NetTestPlayer>();
            var so = new SerializedObject(player);
            so.FindProperty("_peaPrefab").objectReferenceValue = peaPrefab.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(go, "NetTestPlayer");
        }

        private static NetworkPrefabsList EnsurePrefabList(params GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }

            foreach (var prefab in prefabs)
                if (list.PrefabList.All(p => p.Prefab != prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });

            EditorUtility.SetDirty(list);
            return list;
        }

        private static void BuildScene(GameObject playerPrefab, NetworkPrefabsList prefabList,
            Material groundMat, Material coverMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 16f, -14f), Quaternion.Euler(50f, 0f, 0f));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            CreateCover("Cover_Center", new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 3f), coverMat);
            CreateCover("Cover_A", new Vector3(-3f, 1f, 4f), new Vector3(1f, 2f, 1f), coverMat);
            CreateCover("Cover_B", new Vector3(3f, 1f, -4f), new Vector3(1f, 2f, 1f), coverMat);

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            if (!nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabList))
                nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            nmGo.AddComponent<SessionController>();
            nmGo.AddComponent<GameMenu>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateCover(string name, Vector3 position, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"{PrefabDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
