# Peashooter Look Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the primitive plant with a procedural cartoon peashooter (option B · Chunky), team-coloured heads (purple host / yellow client), and live mouth/wink expressions visible to the opponent.

**Architecture:** Pure mesh generators (`PlantMeshes`) feed an editor builder (`PlantBodyBuilder`) that writes mesh/material assets and rebuilds `Gen_*` children in the Player and TargetDummy prefabs. A runtime `PlantFace` animates snout flare and eye squint from two flags that `PlayerNetSync` syncs as one owner-writable byte. `TeamColors` is the single source of team colours.

**Tech Stack:** Unity 6000.3.9f1, URP Lit, Netcode for GameObjects 2.13.3, NUnit EditMode tests, Unity Editor API (`PrefabUtility`, `AssetDatabase`, `SerializedObject`).

**Spec:** `docs/superpowers/specs/2026-10-10-peashooter-look-design.md`

## Global Constraints

- Original geometry/materials only; no ripped game models or textures.
- Head radius **0.54 m** (Head scale 1.08), stem thickness **0.18**, snout rim ends at z ≈ 0.75 (= `Mouth`); `Mouth` is never moved.
- Team colours: host **(0.61, 0.35, 0.82)** purple, client **(0.96, 0.83, 0.19)** yellow, head + snout only; everything else pea-green.
- Generated objects are named `Gen_*` and are deleted and rebuilt on every builder run.
- C# conventions: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private`, one MonoBehaviour per file, minimal comments. Runtime code in namespace `PeaHeads` (net code in `PeaHeads.Net`), editor code in `PeaHeads.EditorTools`.
- Commits: conventional, `.meta` files with assets, end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Batch-mode Unity needs the editor closed (`PeaHeadsApp/Temp/UnityLockfile` absent).

**Shared commands** (Git Bash, repo root `C:\Users\ilans\Documents\hack26`):

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe"
P="$(cygpath -w "$PWD/PeaHeadsApp")"
LOGS="C:/Users/ilans/AppData/Local/Temp/claude/C--Users-ilans/7450c0e0-71c3-496f-b331-7f763fb3c24c/scratchpad"
```

- **EditMode tests (also compiles):** `rm -f "$LOGS/tests.xml"; "$UNITY" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testResults "$LOGS/tests.xml" -logFile "$LOGS/tests.log"; grep -E "error CS|warning CS" "$LOGS/tests.log" | grep PeaHeads | sort -u; grep -oE 'result="[A-Za-z]+" total="[0-9]+" passed="[0-9]+" failed="[0-9]+"' "$LOGS/tests.xml" | head -1`
- **Run builder:** `"$UNITY" -batchmode -nographics -quit -projectPath "$P" -executeMethod PeaHeads.EditorTools.PlantBodyBuilder.Build -logFile "$LOGS/build.log"; grep -E "PlantBodyBuilder|Exception|error" "$LOGS/build.log" | head`

## Review Focus

1. Running the builder twice → no duplicated parts (old `Gen_*` deleted first). Pinned by Task 4 Step 4 (run twice, counts unchanged).
2. Own collar/tuft/eyes visible in first person, blocking the view → every head part goes into `_hiddenInFirstPerson`. Pinned by Task 4 Step 5 (count of hidden renderers = 14) + manual check 3.
3. Snout flare pushing the dark mouth disc outside the rim → `_openHole` 1.3 keeps disc radius 0.19·1.22·1.3 ≈ 0.30 < rim 0.29·1.22 ≈ 0.35. Pinned by `PlantFaceTests.Targets_MouthOpen_HoleStaysInsideRim` (Task 2).
4. A prefab without `PlantFace` (TargetDummy, or Player before the builder ran) → no NullReferenceException in `PlayerNetSync`. Null-guarded in Task 3; manual check 8.
5. A client joining while the opponent already has the mouth open → face shows the current flags immediately (`ApplyRemoteState` runs in `OnNetworkSpawn`). Manual check 6.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Scripts/Player/PlantMeshes.cs` | Create | `Lathe`, `Disc`, `Leaf`, `SnoutProfile` |
| `Tests/EditMode/PlantMeshesTests.cs` | Create | Mesh generator tests |
| `Scripts/Player/PlantFace.cs` | Create | `FaceFlags` (static) + `PlantFace` (MonoBehaviour) |
| `Tests/EditMode/PlantFaceTests.cs` | Create | `Targets`, `FaceFlags` tests |
| `Scripts/Net/TeamColors.cs` | Create | Team colour constants |
| `Tests/EditMode/TeamColorsTests.cs` | Create | `TeamColors.Of` test |
| `Scripts/Player/PlayerInput.cs` | Modify | `PlayerInputFrame.MouthOpen` |
| `Scripts/Net/PlayerNetSync.cs` | Modify | `TeamColors`, `_faceFlags` sync |
| `Scripts/UI/ScoreHUD.cs` | Modify | `TeamColors` |
| `Scripts/Player/PlantVisuals.cs` | Modify | `_groundLeaves` spread |
| `Editor/PlantBodyBuilder.cs` | Create | Menu builder |
| `Art/Plant/*` | Generated | Meshes + materials |
| `Prefabs/Player.prefab`, `Prefabs/TargetDummy.prefab` | Generated changes | `Gen_*` parts, wiring |
| `CLAUDE.md` | Modify | Code layout + builder rule |

(All under `PeaHeadsApp/Assets/PeaHeads/` except `CLAUDE.md`.)

---

### Task 1: PlantMeshes

**Files:**
- Create: `Scripts/Player/PlantMeshes.cs`
- Test: `Tests/EditMode/PlantMeshesTests.cs`

**Interfaces:**
- Produces: `public static class PlantMeshes` with `Mesh Lathe(Vector2[] profile, int segments)`, `Mesh Disc(float radius, int segments)`, `Mesh Leaf(float length, float width, float curl, int segments)`, `static readonly Vector2[] SnoutProfile`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/PlantMeshesTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantMeshesTests
    {
        private static readonly Vector2[] Cylinder = { new(0.5f, 0f), new(0.5f, 1f) };

        private static void AssertNormalsFiniteAndUnit(Mesh mesh)
        {
            foreach (Vector3 n in mesh.normals)
            {
                Assert.IsTrue(float.IsFinite(n.x) && float.IsFinite(n.y) && float.IsFinite(n.z), $"not finite: {n}");
                Assert.AreEqual(1f, n.magnitude, 1e-3f);
            }
        }

        [Test]
        public void Lathe_VertexCountIsProfileTimesRing() =>
            Assert.AreEqual(2 * 17, PlantMeshes.Lathe(Cylinder, 16).vertexCount);

        [Test]
        public void Lathe_RadiusMatchesProfile()
        {
            Bounds bounds = PlantMeshes.Lathe(Cylinder, 16).bounds;
            Assert.AreEqual(0.5f, bounds.extents.x, 1e-3f);
            Assert.AreEqual(0.5f, bounds.extents.y, 1e-3f);
            Assert.AreEqual(1f, bounds.size.z, 1e-3f);
        }

        [Test]
        public void Lathe_NormalsPointOutward()
        {
            Mesh mesh = PlantMeshes.Lathe(Cylinder, 16);
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 radial = new Vector3(vertices[i].x, vertices[i].y, 0f).normalized;
                Assert.Greater(Vector3.Dot(normals[i], radial), 0.9f, $"vertex {i}");
            }
        }

        [Test]
        public void SnoutLathe_NormalsFiniteAndRimAtMouth()
        {
            Mesh mesh = PlantMeshes.Lathe(PlantMeshes.SnoutProfile, 32);
            AssertNormalsFiniteAndUnit(mesh);
            Assert.AreEqual(0.76f, mesh.bounds.max.z, 1e-3f);
        }

        [Test]
        public void Disc_FacesPlusZ()
        {
            Mesh mesh = PlantMeshes.Disc(0.2f, 24);
            foreach (Vector3 n in mesh.normals) Assert.Greater(n.z, 0.99f);
            Assert.AreEqual(0.2f, mesh.bounds.extents.x, 1e-3f);
        }

        [Test]
        public void Leaf_IsDoubleSided()
        {
            Mesh mesh = PlantMeshes.Leaf(0.75f, 0.3f, 0.08f, 10);
            Vector3[] normals = mesh.normals;
            int side = normals.Length / 2;
            for (int i = 0; i < side; i++)
                Assert.Less(Vector3.Dot(normals[i], normals[i + side]), -0.99f, $"vertex {i}");
            Assert.Greater(normals[side / 2].y, 0f, "front face points up");
        }

        [Test]
        public void Leaf_SizeMatchesLengthWidthAndCurl()
        {
            Mesh mesh = PlantMeshes.Leaf(0.75f, 0.3f, 0.08f, 10);
            AssertNormalsFiniteAndUnit(mesh);
            Bounds bounds = mesh.bounds;
            Assert.AreEqual(0.75f, bounds.size.z, 1e-3f);
            Assert.AreEqual(0.3f, bounds.size.x, 0.01f);
            Assert.AreEqual(0.08f, bounds.max.y, 0.02f);
            Assert.AreEqual(0f, bounds.min.z, 1e-3f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile error `The name 'PlantMeshes' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `Scripts/Player/PlantMeshes.cs`:

```csharp
using UnityEngine;

namespace PeaHeads
{
    // Pure mesh generators for the peashooter body; used by the editor PlantBodyBuilder.
    public static class PlantMeshes
    {
        // Option B snout, head-local metres (radius, z): starts inside the head, flares to a rim at the Mouth (z 0.75),
        // then turns back inward so the opening has an inner wall.
        public static readonly Vector2[] SnoutProfile =
        {
            new(0.20f, 0.38f), new(0.17f, 0.52f), new(0.19f, 0.62f), new(0.27f, 0.70f),
            new(0.29f, 0.74f), new(0.25f, 0.76f), new(0.20f, 0.70f),
        };

        // Spins the (radius, z) profile around +Z. Ends stay open.
        public static Mesh Lathe(Vector2[] profile, int segments)
        {
            int ring = segments + 1;
            var vertices = new Vector3[profile.Length * ring];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j < ring; j++)
            {
                float angle = 2f * Mathf.PI * j / segments;
                vertices[i * ring + j] = new Vector3(Mathf.Cos(angle) * profile[i].x,
                    Mathf.Sin(angle) * profile[i].x, profile[i].y);
                uvs[i * ring + j] = new Vector2((float)j / segments, (float)i / (profile.Length - 1));
            }

            var triangles = new int[(profile.Length - 1) * segments * 6];
            int t = 0;
            for (int i = 0; i < profile.Length - 1; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * ring + j, b = a + ring, c = a + 1, d = b + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
            }
            return Build("Lathe", vertices, uvs, triangles);
        }

        // Flat disc in the XY plane facing +Z.
        public static Mesh Disc(float radius, int segments)
        {
            var vertices = new Vector3[segments + 2];
            var uvs = new Vector2[vertices.Length];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int j = 0; j <= segments; j++)
            {
                float angle = 2f * Mathf.PI * j / segments;
                vertices[j + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                uvs[j + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle));
            }

            var triangles = new int[segments * 3];
            for (int j = 0; j < segments; j++)
            {
                triangles[j * 3] = 0;
                triangles[j * 3 + 1] = j + 1;
                triangles[j * 3 + 2] = j + 2;
            }
            return Build("Disc", vertices, uvs, triangles);
        }

        // Tapered leaf from the origin along +Z, face up (+Y), tip lifted by `curl`, slight centre crease.
        // Front and back faces use separate vertices so both sides light correctly.
        public static Mesh Leaf(float length, float width, float curl, int segments)
        {
            int rows = segments + 1;
            int side = rows * 3;
            var vertices = new Vector3[side * 2];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < rows; i++)
            {
                float t = (float)i / segments;
                float half = 0.5f * width * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.12f, 1f, t));
                float y = curl * t * t;
                for (int k = 0; k < 3; k++)
                {
                    float crease = k == 1 ? half * 0.15f : 0f;
                    var vertex = new Vector3((k - 1) * half, y + crease, t * length);
                    vertices[i * 3 + k] = vertex;
                    vertices[side + i * 3 + k] = vertex;
                    uvs[i * 3 + k] = uvs[side + i * 3 + k] = new Vector2(k * 0.5f, t);
                }
            }

            var triangles = new int[segments * 2 * 12];
            int n = 0;
            for (int i = 0; i < segments; i++)
            for (int k = 0; k < 2; k++)
            {
                int a = i * 3 + k, b = a + 3, c = a + 1, d = b + 1;
                triangles[n++] = a; triangles[n++] = b; triangles[n++] = c;
                triangles[n++] = c; triangles[n++] = b; triangles[n++] = d;
                triangles[n++] = side + a; triangles[n++] = side + c; triangles[n++] = side + b;
                triangles[n++] = side + c; triangles[n++] = side + d; triangles[n++] = side + b;
            }
            return Build("Leaf", vertices, uvs, triangles);
        }

        private static Mesh Build(string name, Vector3[] vertices, Vector2[] uvs, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: `result="Passed"`, `failed="0"`, 7 more tests than before. If `Lathe_NormalsPointOutward`, `Disc_FacesPlusZ` or the front-face check in `Leaf_IsDoubleSided` fails, the triangle winding is reversed — swap the 2nd/3rd index of each triangle in that generator (systematic-debugging; do not loosen the test).

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Scripts/Player/PlantMeshes.cs $S/Scripts/Player/PlantMeshes.cs.meta $S/Tests/EditMode/PlantMeshesTests.cs $S/Tests/EditMode/PlantMeshesTests.cs.meta
git commit -m "feat: add procedural peashooter mesh generators" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: PlantFace + FaceFlags

**Files:**
- Create: `Scripts/Player/PlantFace.cs`
- Test: `Tests/EditMode/PlantFaceTests.cs`

**Interfaces:**
- Produces: `public static class FaceFlags { byte Pack(bool mouthOpen, bool wink); (bool mouthOpen, bool wink) Unpack(byte value); }`; `public class PlantFace : MonoBehaviour` with `void SetFlags(bool mouthOpen, bool wink)`, `static Vector3 Targets(bool mouthOpen, bool wink, float openFlare, float openHole, float winkSquint)`; serialized `_snout`, `_mouthHole`, `_winkEye` (Transforms), `_openFlare` 1.22, `_openHole` 1.3, `_winkSquint` 0.15, `_smoothing` 14.

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/PlantFaceTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantFaceTests
    {
        private static Vector3 Targets(bool mouth, bool wink) => PlantFace.Targets(mouth, wink, 1.22f, 1.3f, 0.15f);

        [Test]
        public void Targets_Neutral_IsRest() => Assert.AreEqual(Vector3.one, Targets(false, false));

        [Test]
        public void Targets_MouthOpen_FlaresSnoutAndHole() =>
            Assert.AreEqual(new Vector3(1.22f, 1.3f, 1f), Targets(true, false));

        [Test]
        public void Targets_Wink_SquintsEyeOnly() =>
            Assert.AreEqual(new Vector3(1f, 1f, 0.15f), Targets(false, true));

        [Test]
        public void Targets_Both() => Assert.AreEqual(new Vector3(1.22f, 1.3f, 0.15f), Targets(true, true));

        [Test]
        public void Targets_MouthOpen_HoleStaysInsideRim()
        {
            Vector3 open = Targets(true, false);
            const float holeRadius = 0.19f, rimRadius = 0.29f;
            Assert.Less(holeRadius * open.x * open.y, rimRadius * open.x);
        }

        [TestCase(false, false, ExpectedResult = 0)]
        [TestCase(true, false, ExpectedResult = 1)]
        [TestCase(false, true, ExpectedResult = 2)]
        [TestCase(true, true, ExpectedResult = 3)]
        public int FaceFlags_PackBitLayout(bool mouth, bool wink) => FaceFlags.Pack(mouth, wink);

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FaceFlags_RoundTrip(bool mouth, bool wink) =>
            Assert.AreEqual((mouth, wink), FaceFlags.Unpack(FaceFlags.Pack(mouth, wink)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile errors `'PlantFace' could not be found` / `'FaceFlags' does not exist`.

- [ ] **Step 3: Implement**

Create `Scripts/Player/PlantFace.cs`:

```csharp
using UnityEngine;

namespace PeaHeads
{
    // Mouth open / wink packed into one byte for PlayerNetSync.
    public static class FaceFlags
    {
        public static byte Pack(bool mouthOpen, bool wink) => (byte)((mouthOpen ? 1 : 0) | (wink ? 2 : 0));

        public static (bool mouthOpen, bool wink) Unpack(byte value) => ((value & 1) != 0, (value & 2) != 0);
    }

    // What the opponent sees: the snout flares while the mouth is open, one eye squints on a wink.
    public class PlantFace : MonoBehaviour
    {
        [SerializeField] private Transform _snout;
        [SerializeField] private Transform _mouthHole;
        [SerializeField] private Transform _winkEye;
        [SerializeField, Range(1f, 2f)] private float _openFlare = 1.22f;
        [Tooltip("Mouth hole scale on top of the flare; keep hole radius below the rim.")]
        [SerializeField, Range(1f, 2f)] private float _openHole = 1.3f;
        [SerializeField, Range(0.05f, 1f)] private float _winkSquint = 0.15f;
        [SerializeField, Range(1f, 40f)] private float _smoothing = 14f;

        private Vector3 _current = Vector3.one;
        private Vector3 _target = Vector3.one;
        private Vector3 _snoutRest;
        private Vector3 _holeRest;
        private Vector3 _eyeRest;

        private void Awake()
        {
            if (_snout) _snoutRest = _snout.localScale;
            if (_mouthHole) _holeRest = _mouthHole.localScale;
            if (_winkEye) _eyeRest = _winkEye.localScale;
        }

        public void SetFlags(bool mouthOpen, bool wink) =>
            _target = Targets(mouthOpen, wink, _openFlare, _openHole, _winkSquint);

        private void Update()
        {
            _current = Vector3.Lerp(_current, _target, 1f - Mathf.Exp(-_smoothing * Time.deltaTime));
            if (_snout) _snout.localScale = new Vector3(_snoutRest.x * _current.x, _snoutRest.y * _current.x, _snoutRest.z);
            if (_mouthHole)
                _mouthHole.localScale = new Vector3(_holeRest.x * _current.y, _holeRest.y * _current.y, _holeRest.z);
            if (_winkEye) _winkEye.localScale = new Vector3(_eyeRest.x, _eyeRest.y * _current.z, _eyeRest.z);
        }

        // x = snout flare, y = mouth-hole scale, z = wink-eye height.
        public static Vector3 Targets(bool mouthOpen, bool wink, float openFlare, float openHole, float winkSquint) =>
            new(mouthOpen ? openFlare : 1f, mouthOpen ? openHole : 1f, wink ? winkSquint : 1f);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: `result="Passed"`, `failed="0"`, 13 more tests than after Task 1.

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Scripts/Player/PlantFace.cs $S/Scripts/Player/PlantFace.cs.meta $S/Tests/EditMode/PlantFaceTests.cs $S/Tests/EditMode/PlantFaceTests.cs.meta
git commit -m "feat: add PlantFace expressions and FaceFlags packing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Team colours, MouthOpen input, face sync, ground-leaf spread

**Files:**
- Create: `Scripts/Net/TeamColors.cs`, `Tests/EditMode/TeamColorsTests.cs`
- Modify: `Scripts/Player/PlayerInput.cs`, `Scripts/Net/PlayerNetSync.cs`, `Scripts/UI/ScoreHUD.cs`, `Scripts/Player/PlantVisuals.cs`

**Interfaces:**
- Consumes: `FaceFlags`, `PlantFace.SetFlags` (Task 2); `MatchManager.TeamOf(ulong)` (existing).
- Produces: `public static class TeamColors { Color Host; Color Client; Color Of(int team); }` (namespace `PeaHeads`); `PlayerInputFrame.MouthOpen`; `PlantVisuals._groundLeaves` (serialized Transform, wired by Task 4).

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/TeamColorsTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class TeamColorsTests
    {
        [Test]
        public void Of_HostIsPurple() => Assert.AreEqual(new Color(0.61f, 0.35f, 0.82f), TeamColors.Of(0));

        [Test]
        public void Of_ClientIsYellow() => Assert.AreEqual(new Color(0.96f, 0.83f, 0.19f), TeamColors.Of(1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile error `The name 'TeamColors' does not exist in the current context`.

- [ ] **Step 3: Implement `TeamColors`**

Create `Scripts/Net/TeamColors.cs`:

```csharp
using UnityEngine;

namespace PeaHeads
{
    // Team 0 = host, team 1 = client. Heads and score bars both read from here so they can't drift apart.
    public static class TeamColors
    {
        public static readonly Color Host = new(0.61f, 0.35f, 0.82f);
        public static readonly Color Client = new(0.96f, 0.83f, 0.19f);

        public static Color Of(int team) => team == 0 ? Host : Client;
    }
}
```

- [ ] **Step 4: `PlayerInput.MouthOpen`**

In `Scripts/Player/PlayerInput.cs`, struct `PlayerInputFrame`: after `public bool ZoomHeld;` add

```csharp
        // Level, not edge: true while the mouth is open (webcam) or LMB is held. Drives the opponent-visible snout.
        public bool MouthOpen;
```

In `Update`, inside `Current = new PlayerInputFrame { ... }`, after the `ZoomHeld = ...` line add

```csharp
                MouthOpen = locked && (mouse.leftButton.isPressed || (trackerFresh && _trackerShootPrevious)),
```

- [ ] **Step 5: `PlayerNetSync` — team colours + face sync**

In `Scripts/Net/PlayerNetSync.cs`:

Delete the two fields:

```csharp
        [SerializeField] private Color _hostColor = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _clientColor = new(0.95f, 0.6f, 0.2f);
```

After `_plantState` add

```csharp
        private readonly NetworkVariable<byte> _faceFlags = new(writePerm: NetworkVariableWritePermission.Owner);
```

After `private SpawnPoint _lastSpawn;` add `private PlantFace _plantFace;` and in `Awake` add `_plantFace = GetComponent<PlantFace>();`.

In `OnNetworkSpawn` replace

```csharp
            Color color = OwnerClientId == NetworkManager.ServerClientId ? _hostColor : _clientColor;
```

with

```csharp
            Color color = TeamColors.Of(MatchManager.TeamOf(OwnerClientId));
```

In `Update`, inside `if (IsOwner) { ... }` after `_plantState.Value = ...;` add

```csharp
                byte face = FaceFlags.Pack(_input.Current.MouthOpen, _input.Current.ZoomHeld);
                _faceFlags.Value = face;
                ApplyFace(face);
```

In `ApplyRemoteState` add as the last line `ApplyFace(_faceFlags.Value);` and add the method

```csharp
        // Dummies and prefabs built before PlantBodyBuilder have no PlantFace.
        private void ApplyFace(byte flags)
        {
            if (!_plantFace) return;
            (bool mouthOpen, bool wink) = FaceFlags.Unpack(flags);
            _plantFace.SetFlags(mouthOpen, wink);
        }
```

- [ ] **Step 6: `ScoreHUD` uses `TeamColors`**

In `Scripts/UI/ScoreHUD.cs` delete

```csharp
        [SerializeField] private Color _team0Color = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _team1Color = new(0.95f, 0.6f, 0.2f);
```

and replace `GUI.color = team == 0 ? _team0Color : _team1Color;` with `GUI.color = TeamColors.Of(team);`.

- [ ] **Step 7: `PlantVisuals` spreads the ground leaves**

In `Scripts/Player/PlantVisuals.cs`: after `[SerializeField] private Transform _leafBase;` add

```csharp
        [Tooltip("Optional. Spread outward (x/z) with the same curve as the leaf base when planting.")]
        [SerializeField] private Transform _groundLeaves;
```

After `private Vector3 _restPosition;` add `private Vector3 _groundRest;`. In `Awake` add `if (_groundLeaves) _groundRest = _groundLeaves.localScale;`. In `LateUpdate`, after the `_leafBase.localPosition = ...;` statement add

```csharp
            if (_groundLeaves)
            {
                float spread = Mathf.LerpUnclamped(1f, _plantedScale.x, blend);
                _groundLeaves.localScale = new Vector3(_groundRest.x * spread, _groundRest.y, _groundRest.z * spread);
            }
```

- [ ] **Step 8: Run tests**

Run: EditMode tests command.
Expected: no `error CS`/`warning CS` for PeaHeads; `result="Passed"`, `failed="0"`, 2 more tests than after Task 2.

- [ ] **Step 9: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Scripts/Net/TeamColors.cs $S/Scripts/Net/TeamColors.cs.meta $S/Tests/EditMode/TeamColorsTests.cs $S/Tests/EditMode/TeamColorsTests.cs.meta $S/Scripts/Player/PlayerInput.cs $S/Scripts/Net/PlayerNetSync.cs $S/Scripts/UI/ScoreHUD.cs $S/Scripts/Player/PlantVisuals.cs
git commit -m "feat: purple/yellow team colours, synced face flags, ground-leaf spread" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: PlantBodyBuilder + generated prefabs

**Files:**
- Create: `Editor/PlantBodyBuilder.cs`
- Generated: `Art/Plant/{Snout,MouthHole,Leaf,CollarLeaf,TuftLeaf}.asset`, `Art/Plant/{PlantGreen,LeafGreen,EyeWhite,Pupil,MouthDark,Dirt}.mat` (+ `.meta`), changes to `Prefabs/Player.prefab`, `Prefabs/TargetDummy.prefab`

**Interfaces:**
- Consumes: `PlantMeshes` (Task 1), `PlantFace` (Task 2), `PlantVisuals._groundLeaves` (Task 3); existing serialized fields `StemBender._thickness`, `PlayerCameraRig._hiddenInFirstPerson`.
- Produces: menu `PeaHeads/Build Plant Body`, `public static void PlantBodyBuilder.Build()`.

- [ ] **Step 1: Write the builder**

Create `Editor/PlantBodyBuilder.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
            public Mesh Snout, MouthHole, GroundLeaf, CollarLeaf, TuftLeaf, Sphere;
            public Material PlantGreen, LeafGreen, EyeWhite, Pupil, MouthDark, Dirt;
        }

        [MenuItem("PeaHeads/Build Plant Body")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder("Assets/PeaHeads/Art", "Plant");
            var parts = new Parts
            {
                Snout = SaveMesh(PlantMeshes.Lathe(PlantMeshes.SnoutProfile, 32), "Snout"),
                MouthHole = SaveMesh(PlantMeshes.Disc(0.19f, 32), "MouthHole"),
                GroundLeaf = SaveMesh(PlantMeshes.Leaf(0.75f, 0.3f, 0.08f, 10), "Leaf"),
                CollarLeaf = SaveMesh(PlantMeshes.Leaf(0.38f, 0.22f, 0.06f, 8), "CollarLeaf"),
                TuftLeaf = SaveMesh(PlantMeshes.Leaf(0.42f, 0.16f, 0.22f, 10), "TuftLeaf"),
                Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
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
                    new Vector3(0f, 0f, 0.70f), Quaternion.identity, Vector3.one);
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
                    SetObject(face, "_winkEye", winkEye);
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
                new Vector3(0f, 0f, 0.42f), Quaternion.identity, Vector3.one * 0.5f);
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
```

- [ ] **Step 2: Compile + tests**

Run: EditMode tests command.
Expected: no `error CS`/`warning CS` for PeaHeads; `result="Passed"`, `failed="0"`, same total as after Task 3.

- [ ] **Step 3: Run the builder (editor closed)**

Run: Run builder command.
Expected: `PlantBodyBuilder: rebuilt Player and TargetDummy bodies.` and no `Exception`.

- [ ] **Step 4: Verify parts, then run again for idempotency**

```bash
S=PeaHeadsApp/Assets/PeaHeads/Prefabs
for f in Player TargetDummy; do echo "$f: $(grep -c 'm_Name: Gen_' $S/$f.prefab)"; done
```

Expected: `Player: 20`, `TargetDummy: 20` (MouthHole 1, EyeL/EyeR 2, Pupil 2, Collar 1 + 5 leaves, Tuft 1 + 2 leaves, GroundLeaves 1 + 5 leaves). Run the builder command again and re-run the count: still 20 each.

- [ ] **Step 5: Verify wiring**

```bash
S=PeaHeadsApp/Assets/PeaHeads/Prefabs
awk '/_hiddenInFirstPerson:/{on=1;next} on&&/^  - /{n++;next} on{print n; exit}' $S/Player.prefab
grep -n "_groundLeaves\|_snout:\|_mouthHole:\|_winkEye:\|_thickness" $S/Player.prefab
ls PeaHeadsApp/Assets/PeaHeads/Art/Plant/
```

Expected: `14` hidden renderers (head, snout, mouth hole, 2 eyes, 2 pupils, 5 collar leaves, 2 tuft leaves); `_groundLeaves`, `_snout`, `_mouthHole`, `_winkEye` have non-zero fileIDs; `_thickness: 0.18`; 5 `.asset` + 6 `.mat` files with `.meta`.

- [ ] **Step 6: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Editor/PlantBodyBuilder.cs $S/Editor/PlantBodyBuilder.cs.meta $S/Art/Plant $S/Art/Plant.meta $S/Prefabs/Player.prefab $S/Prefabs/TargetDummy.prefab
git commit -m "feat: add PlantBodyBuilder and generate the peashooter body" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Visual check, playtest, CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Look and playtest (human, Unity editor)** — the 8 manual checks from the spec:

1. Open `Prefabs/Player.prefab`: plant looks like mockup B; nothing floats; stem enters the head; snout tip at `Mouth`. If a part is off, the fix is a constant in `PlantBodyBuilder` + **PeaHeads > Build Plant Body** (never hand-edit `Gen_*`).
2. DemoArena, two machines: host head purple, client head yellow on both screens; score bars match.
3. First person: own head parts invisible (shadow visible); looking down shows stem + ground leaves, not the collar.
4. Lean: collar, eyes, tuft move with the head; stem still bends into it.
5. Plant: ground leaves spread with the dirt mound; unplant pops back.
6. Opponent opens mouth / holds LMB → snout flares; winks / holds RMB → right eye squints; their pause menu open → neutral. Join while the opponent holds LMB → flared immediately.
7. Hits on the bigger head register; pea still leaves from the snout tip.
8. TargetDummy shows the same body with its own head material; no Console errors.

- [ ] **Step 2: Update CLAUDE.md**

- `Scripts/Player/` line: add `PlantMeshes (static lathe/disc/leaf generators), PlantFace (+FaceFlags: snout flare + wink squint from 2 synced flags)`.
- `Net/` line: add `TeamColors (host purple / client yellow; heads + score bars)` and to `PlayerNetSync` `face flags byte`.
- `Editor/` line: add `PlantBodyBuilder (menu PeaHeads > Build Plant Body — Art/Plant meshes/materials + Gen_* parts in Player/TargetDummy; overwrites hand edits under Gen_*)`.
- Player hierarchy sentence: mention `Gen_*` parts (eyes, mouth hole, collar, tuft under HeadPivot; ground leaves under Visual), `LeafBase` = dirt mound, `Snout` = lathe trumpet.
- `Tests/EditMode/` line: add `PlantMeshesTests, PlantFaceTests, TeamColorsTests`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document peashooter body builder and face sync" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
