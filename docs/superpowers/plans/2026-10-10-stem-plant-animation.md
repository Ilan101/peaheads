# Stem Bend + Plant Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The stem bends to follow the head lean, and planting/unplanting animates the leaf base, without anything except face/mouse moving the camera.

**Architecture:** Two new visual-only MonoBehaviours on the `Player` root (`StemBender`, `PlantVisuals`) read transforms and `PlantingController` state that are already synced, so no networking changes. `PlantingController` stops scaling `Visual`, exposes `Progress`, and eases `HeightScale`. Pure maths lives in static methods covered by EditMode tests.

**Tech Stack:** Unity 6000.3.9f1, URP, C#, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-10-10-stem-plant-animation-design.md`

## Global Constraints

- No new networking, no new colliders, no changes to `PlayerCameraRig`, `PlayerInput`, `PlayerNetSync`, `PlayerHealth`, `PlayerShooting`, `PeaProjectile`.
- Head/camera only move smoothly: overshoot goes into stem thickness and leaf base only, never `HeightScale`.
- C# conventions: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private`, one MonoBehaviour per file, minimal comments, namespace `PeaHeads`.
- All paths below are relative to `PeaHeadsApp/Assets/PeaHeads/` unless they start with `docs/` or `PeaHeadsApp/`.
- Commits: conventional, `.meta` files committed with their assets, end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Batch-mode Unity only works with the editor closed: `PeaHeadsApp/Temp/UnityLockfile` must not exist.

**Shared commands** (Git Bash, from repo root `C:\Users\ilans\Documents\hack26`):

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe"
P="$(cygpath -w "$PWD/PeaHeadsApp")"
LOGS="C:/Users/ilans/AppData/Local/Temp/claude/C--Users-ilans/7450c0e0-71c3-496f-b331-7f763fb3c24c/scratchpad"
```

- **Editor closed check:** `ls PeaHeadsApp/Temp/UnityLockfile 2>/dev/null && echo "CLOSE THE EDITOR"` → expected: no output.
- **Compile:** `"$UNITY" -batchmode -nographics -quit -projectPath "$P" -logFile "$LOGS/compile.log"; grep -E "error CS|warning CS" "$LOGS/compile.log" | grep PeaHeads` → expected: no output.
- **EditMode tests:** `"$UNITY" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testResults "$LOGS/tests.xml" -logFile "$LOGS/tests.log"; grep -oE 'result="[A-Za-z]+" total="[0-9]+" passed="[0-9]+" failed="[0-9]+"' "$LOGS/tests.xml" | head -1` (no `-quit`; exit code 0 = all passed).

## Review Focus

1. Head pivot exactly on the stem base, or two curve points coinciding → segment must not get a NaN rotation/scale (no invisible or exploding stem). Test: `SegmentPose_ZeroLength_NoNaN` (Task 2).
2. Stem squashed to near-zero length (planted + head leaning down) → thickness must stay bounded, not balloon to metres. Test: `Thickness_IsClampedWhenSquashed` (Task 2).
3. Frame hitch (big `deltaTime`, e.g. alt-tab or first frame after load) → lag spring must not blow up. Test: `SpringStep_LargeDeltaTime_StaysStable` (Task 2).
4. Respawn/teleport across the map → stem must not stretch across the map for a few frames. Covered by design: spring runs in root-local space. Test: `SpringStep_ConvergesToTarget` + manual check 6 in Task 4.
5. Death and respawn → old straight stem must not reappear next to the new one (`PlayerHealth.SetAlive` re-enables every child renderer). Covered by deactivating the source stem GameObject; manual check 6 in Task 4.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Scripts/Player/PlantingController.cs` | Modify | Drop `Visual` scaling; add `Progress`; eased `HeightScale` |
| `Tests/EditMode/PlantingControllerTests.cs` | Modify | Tests for `Progress` and eased `HeightScale` |
| `Scripts/Player/StemBender.cs` | Create | Builds capsule segments; places them along a Bezier from leaf base to head each frame; static maths |
| `Tests/EditMode/StemBenderTests.cs` | Create | Tests for `Bezier`, `SegmentPose`, `Thickness`, `SpringStep` |
| `Scripts/Player/PlantVisuals.cs` | Create | Leaf base spread/flatten with overshoot curves; optional plant puff |
| `Prefabs/Player.prefab` | Modify (editor, by hand) | Add and wire the two components |
| `CLAUDE.md` | Modify | Update code layout + Player hierarchy |

---

### Task 1: PlantingController — Progress, eased HeightScale, no Visual scaling

**Files:**
- Modify: `Scripts/Player/PlantingController.cs`
- Test: `Tests/EditMode/PlantingControllerTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public float Progress { get; }` (0 = unplanted, 1 = planted, linear over the 1 s transition); `public float HeightScale { get; }` (1 → `_plantedHeight`, SmoothStep-eased). `PlantState State` unchanged.

- [ ] **Step 1: Write the failing tests**

Add to `PlantingControllerTests` (before the closing brace of the class):

```csharp
        [Test]
        public void PlantedReachesFullProgressAndPlantedHeight()
        {
            Plant();

            Assert.AreEqual(1f, _planting.Progress);
            Assert.AreEqual(0.8f, _planting.HeightScale, 1e-4f);
        }

        [Test]
        public void HeightScaleEasesInDuringPlanting()
        {
            _planting.Tick(0f, true);
            _planting.Tick(0.25f, false);

            Assert.AreEqual(0.25f, _planting.Progress, 1e-4f);
            // SmoothStep(0.25) = 0.15625 → Lerp(1, 0.8, 0.15625)
            Assert.AreEqual(0.96875f, _planting.HeightScale, 1e-4f);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: editor closed check, then EditMode tests command.
Expected: compile failure, `PlantingController` does not contain a definition for `Progress` (shown in `$LOGS/tests.log`).

- [ ] **Step 3: Implement**

In `Scripts/Player/PlantingController.cs`:

Replace the using line and the two fields `_visual` / `_plantedSquash` and the `HeightScale` property:

```csharp
using UnityEngine;
using UnityEngine.Serialization;
```

```csharp
        [SerializeField] private float _transitionTime = 1f;
        [Tooltip("Head rest height multiplier when fully planted.")]
        [FormerlySerializedAs("_plantedSquash")]
        [SerializeField] private float _plantedHeight = 0.8f;

        public PlantState State { get; private set; }
        public bool CanMove => State == PlantState.Unplanted;
        public bool CanShoot => State == PlantState.Planted;
        // 0 = unplanted, 1 = planted, linear. Visual components shape it with their own curves.
        public float Progress => _progress;
        // Head/camera rest height (1 = standing). Eased, never overshoots, so the camera can't bounce.
        public float HeightScale => Mathf.Lerp(1f, _plantedHeight, Mathf.SmoothStep(0f, 1f, _progress));
```

In `Tick`, delete the last line:

```csharp
            if (_visual) _visual.localScale = new Vector3(1f, HeightScale, 1f);
```

In `ResetState`, delete:

```csharp
            if (_visual) _visual.localScale = Vector3.one;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: `result="Passed"`, `failed="0"`, total = previous count + 2.

- [ ] **Step 5: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantingController.cs PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/PlantingControllerTests.cs
git commit -m "feat: ease plant height and expose plant progress" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Note: until Task 4 the plant body no longer squashes visually (only the head sinks). Expected, not a bug.

---

### Task 2: StemBender — segmented stem following the head

**Files:**
- Create: `Scripts/Player/StemBender.cs`
- Test: `Tests/EditMode/StemBenderTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces (static, used by tests):
  - `public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)`
  - `public static void SegmentPose(Vector3 from, Vector3 to, float overlap, float thickness, out Vector3 position, out Quaternion rotation, out Vector3 scale)`
  - `public static float Thickness(float thickness, float restLength, float length, float volumeKeep)`
  - `public static void SpringStep(ref Vector3 position, ref Vector3 velocity, Vector3 target, float stiffness, float damping, float deltaTime)`
- Serialized (wired in Task 4): `_sourceStem`, `_baseAnchor`, `_headPivot`, `_headSink`, `_segmentMesh`, `_segments`, `_thickness`, `_overlap`, `_volumeKeep`, `_springStiffness`, `_springDamping`, `_springAmount`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/StemBenderTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class StemBenderTests
    {
        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"expected {expected}, got {actual}");

        private static void AssertFinite(Vector3 v) =>
            Assert.IsTrue(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z), $"not finite: {v}");

        [Test]
        public void Bezier_EndpointsMatchBaseAndTip()
        {
            Vector3 p0 = new(0f, 0.16f, 0f), p1 = new(0f, 0.9f, 0f), p2 = new(0.6f, 1.4f, 0f);

            AssertNear(p0, StemBender.Bezier(p0, p1, p2, 0f));
            AssertNear(p2, StemBender.Bezier(p0, p1, p2, 1f));
        }

        [Test]
        public void Bezier_StraightControlGivesStraightLine()
        {
            Vector3 p0 = Vector3.zero, p2 = new(0f, 2f, 0f);

            AssertNear(new Vector3(0f, 1f, 0f), StemBender.Bezier(p0, (p0 + p2) * 0.5f, p2, 0.5f));
        }

        [Test]
        public void SegmentPose_SpansFromTo()
        {
            Vector3 from = new(0f, 1f, 0f), to = new(1f, 1f, 0f);

            StemBender.SegmentPose(from, to, 0.04f, 0.14f, out Vector3 position, out Quaternion rotation,
                out Vector3 scale);

            AssertNear(new Vector3(0.5f, 1f, 0f), position);
            AssertNear(Vector3.right, rotation * Vector3.up);
            Assert.AreEqual(0.52f, scale.y, 1e-4f); // (1 + 0.04) / 2: capsule mesh is 2 units tall
            Assert.AreEqual(0.14f, scale.x, 1e-4f);
            Assert.AreEqual(0.14f, scale.z, 1e-4f);
        }

        [Test]
        public void SegmentPose_ZeroLength_NoNaN()
        {
            Vector3 point = new(0.3f, 1f, 0.2f);

            StemBender.SegmentPose(point, point, 0.04f, 0.14f, out Vector3 position, out Quaternion rotation,
                out Vector3 scale);

            AssertFinite(position);
            AssertFinite(rotation * Vector3.up);
            AssertFinite(scale);
        }

        [Test]
        public void Thickness_AtRestLengthIsBaseThickness()
        {
            Assert.AreEqual(0.14f, StemBender.Thickness(0.14f, 1.2f, 1.2f, 0.5f), 1e-4f);
        }

        [Test]
        public void Thickness_SquashedIsThickerStretchedIsThinner()
        {
            Assert.Greater(StemBender.Thickness(0.14f, 1.2f, 0.9f, 0.5f), 0.14f);
            Assert.Less(StemBender.Thickness(0.14f, 1.2f, 1.6f, 0.5f), 0.14f);
        }

        [Test]
        public void Thickness_IsClampedWhenSquashed()
        {
            Assert.LessOrEqual(StemBender.Thickness(0.14f, 1.2f, 0f, 1f), 0.28f + 1e-4f);
            Assert.GreaterOrEqual(StemBender.Thickness(0.14f, 1.2f, 100f, 1f), 0.07f - 1e-4f);
        }

        [Test]
        public void SpringStep_ConvergesToTarget()
        {
            Vector3 position = Vector3.zero, velocity = Vector3.zero, target = new(0.6f, 1.4f, 0f);

            for (int i = 0; i < 300; i++)
                StemBender.SpringStep(ref position, ref velocity, target, 120f, 14f, 1f / 60f);

            AssertNear(target, position, 1e-3f);
        }

        [Test]
        public void SpringStep_LargeDeltaTime_StaysStable()
        {
            Vector3 position = Vector3.zero, velocity = Vector3.zero, target = new(0.6f, 1.4f, 0f);

            for (int i = 0; i < 300; i++)
            {
                StemBender.SpringStep(ref position, ref velocity, target, 120f, 14f, 0.5f);
                Assert.Less(Vector3.Distance(position, target), 5f, $"diverged at step {i}");
            }

            AssertNear(target, position, 1e-2f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile failure, the type or namespace name `StemBender` could not be found.

- [ ] **Step 3: Implement**

Create `Scripts/Player/StemBender.cs`:

```csharp
using UnityEngine;

namespace PeaHeads
{
    // Replaces the straight stem with a chain of capsules curving from the leaf base up into the head, so the
    // stem follows the head lean. Visual only: no colliders, reads transforms (already synced), works on
    // remote copies and dummies. Everything runs in root-local space so teleports/respawns can't stretch it.
    public class StemBender : MonoBehaviour
    {
        private const float MinLength = 0.01f;
        private const float MaxThicknessRatio = 2f;
        private const float MaxSpringStep = 1f / 30f;

        [SerializeField] private Transform _sourceStem;
        [Tooltip("Leaf base cylinder: the stem starts at its top centre.")]
        [SerializeField] private Transform _baseAnchor;
        [SerializeField] private Transform _headPivot;
        [Tooltip("Stem ends this far below the head pivot, hidden inside the head.")]
        [SerializeField] private float _headSink = 0.3f;
        [SerializeField] private Mesh _segmentMesh;
        [SerializeField] private int _segments = 7;
        [SerializeField] private float _thickness = 0.14f;
        [Tooltip("Extra length per segment so joints don't gap when bent.")]
        [SerializeField] private float _overlap = 0.04f;
        [Tooltip("0 = constant thickness, 1 = keeps volume (thicker when squashed, thinner when stretched).")]
        [SerializeField] private float _volumeKeep = 0.5f;

        [Header("Lag")]
        [SerializeField] private float _springStiffness = 120f;
        [SerializeField] private float _springDamping = 14f;
        [Tooltip("0 = no lag.")]
        [SerializeField] private float _springAmount = 0.5f;

        private Transform[] _parts;
        private Vector3[] _points;
        private float _restLength;
        private Vector3 _springPosition;
        private Vector3 _springVelocity;

        private void Awake()
        {
            Material material = _sourceStem.GetComponent<MeshRenderer>().sharedMaterial;
            // Deactivated, not renderer-disabled: PlayerHealth.SetAlive re-enables every child renderer.
            _sourceStem.gameObject.SetActive(false);

            _parts = new Transform[_segments];
            _points = new Vector3[_segments + 1];
            for (int i = 0; i < _segments; i++)
            {
                var part = new GameObject($"StemSegment{i}");
                part.transform.SetParent(transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = _segmentMesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = material;
                _parts[i] = part.transform;
            }

            Vector3 tip = LocalTip();
            _restLength = Mathf.Max(MinLength, Vector3.Distance(LocalBase(), tip));
            _springPosition = tip;
        }

        // After PlayerCameraRig.Update has placed the head for this frame.
        private void LateUpdate()
        {
            Vector3 p0 = LocalBase();
            Vector3 p2 = LocalTip();
            SpringStep(ref _springPosition, ref _springVelocity, p2, _springStiffness, _springDamping,
                Time.deltaTime);
            Vector3 p1 = p0 + Vector3.up * (Vector3.Distance(p0, p2) * 0.5f)
                         + (_springPosition - p2) * _springAmount;

            float length = 0f;
            _points[0] = p0;
            for (int i = 1; i <= _segments; i++)
            {
                _points[i] = Bezier(p0, p1, p2, (float)i / _segments);
                length += Vector3.Distance(_points[i - 1], _points[i]);
            }

            float thickness = Thickness(_thickness, _restLength, length, _volumeKeep);
            for (int i = 0; i < _segments; i++)
            {
                SegmentPose(_points[i], _points[i + 1], _overlap, thickness, out Vector3 position,
                    out Quaternion rotation, out Vector3 scale);
                _parts[i].SetLocalPositionAndRotation(position, rotation);
                _parts[i].localScale = scale;
            }
        }

        // Cylinder mesh spans y -1..1, so (0, 1, 0) is the top centre.
        private Vector3 LocalBase() => transform.InverseTransformPoint(_baseAnchor.TransformPoint(Vector3.up));

        private Vector3 LocalTip() => transform.InverseTransformPoint(_headPivot.position) - Vector3.up * _headSink;

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        // Capsule mesh is 1 wide and 2 tall at scale 1.
        public static void SegmentPose(Vector3 from, Vector3 to, float overlap, float thickness,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            Vector3 span = to - from;
            float length = span.magnitude;
            position = (from + to) * 0.5f;
            rotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, span / length) : Quaternion.identity;
            scale = new Vector3(thickness, (length + overlap) * 0.5f, thickness);
        }

        public static float Thickness(float thickness, float restLength, float length, float volumeKeep)
        {
            float ratio = Mathf.Pow(restLength / Mathf.Max(length, MinLength), volumeKeep);
            return thickness * Mathf.Clamp(ratio, 1f / MaxThicknessRatio, MaxThicknessRatio);
        }

        // Semi-implicit Euler; the step is capped so a frame hitch can't make it diverge.
        public static void SpringStep(ref Vector3 position, ref Vector3 velocity, Vector3 target,
            float stiffness, float damping, float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, MaxSpringStep);
            velocity += ((target - position) * stiffness - velocity * damping) * dt;
            position += velocity * dt;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: `result="Passed"`, `failed="0"`, 9 more tests than after Task 1.

- [ ] **Step 5: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/StemBender.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Player/StemBender.cs.meta PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/StemBenderTests.cs PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/StemBenderTests.cs.meta
git commit -m "feat: add StemBender segmented stem that follows the head" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(The batch-mode run generates the `.meta` files. If `git add` reports a missing `.meta`, run the Compile command once and retry.)

---

### Task 3: PlantVisuals — leaf base roots in and pops out

**Files:**
- Create: `Scripts/Player/PlantVisuals.cs`

**Interfaces:**
- Consumes: `PlantingController.State`, `PlantingController.Progress` (Task 1).
- Produces: component with serialized `_leafBase`, `_plantedScale`, `_plantCurve`, `_unplantCurve`, `_plantPuff` (wired in Task 4).

No EditMode test: the only logic is curve evaluation and a scale lerp, verified by playing (Task 4). Compile is the gate.

- [ ] **Step 1: Implement**

Create `Scripts/Player/PlantVisuals.cs`:

```csharp
using UnityEngine;

namespace PeaHeads
{
    // Leaf base roots into the ground when planted (wider, flatter) and pops back out when unplanted.
    // The overshoot lives here, never in the head height, so the camera can't bounce.
    [RequireComponent(typeof(PlantingController))]
    public class PlantVisuals : MonoBehaviour
    {
        [SerializeField] private Transform _leafBase;
        [Tooltip("x = width multiplier, y = height multiplier when fully planted.")]
        [SerializeField] private Vector2 _plantedScale = new(1.35f, 0.6f);
        [Tooltip("Plant progress 0→1 to blend. Peaks above 1 for a 'thunk' into the ground.")]
        [SerializeField] private AnimationCurve _plantCurve =
            new(new Keyframe(0f, 0f), new Keyframe(0.8f, 1.12f), new Keyframe(1f, 1f));
        [Tooltip("Evaluated while progress falls 1→0. Dips below 0 for a 'pop' out of the ground.")]
        [SerializeField] private AnimationCurve _unplantCurve =
            new(new Keyframe(0f, 0f), new Keyframe(0.15f, -0.1f), new Keyframe(1f, 1f));
        [SerializeField] private ParticleSystem _plantPuff;

        private PlantingController _planting;
        private Vector3 _restScale;
        private PlantState _lastState;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _restScale = _leafBase.localScale;
        }

        private void LateUpdate()
        {
            PlantState state = _planting.State;
            bool rooting = state is PlantState.Planting or PlantState.Planted;
            float blend = (rooting ? _plantCurve : _unplantCurve).Evaluate(_planting.Progress);
            Vector3 planted = new(_plantedScale.x, _plantedScale.y, _plantedScale.x);
            _leafBase.localScale = Vector3.Scale(_restScale, Vector3.LerpUnclamped(Vector3.one, planted, blend));

            if (_plantPuff && state == PlantState.Planted && _lastState != PlantState.Planted) _plantPuff.Play();
            _lastState = state;
        }
    }
}
```

- [ ] **Step 2: Compile and run all tests**

Run: Compile command, then EditMode tests command.
Expected: no `error CS`/`warning CS` lines for PeaHeads; tests `result="Passed"`, `failed="0"`.

- [ ] **Step 3: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantVisuals.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantVisuals.cs.meta
git commit -m "feat: add PlantVisuals leaf base rooting animation" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Wire the Player prefab, playtest, update CLAUDE.md

**Files:**
- Modify (Unity editor, by hand): `Prefabs/Player.prefab`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `StemBender`, `PlantVisuals`, `PlantingController._plantedHeight` from Tasks 1–3.

- [ ] **Step 1: Wire the prefab (human, in the Unity editor)**

1. Open Unity Hub → open project `C:\Users\ilans\Documents\hack26\PeaHeadsApp` (Unity 6000.3.9f1).
2. Project window → `Assets/PeaHeads/Prefabs/Player.prefab` → double-click to open Prefab Mode.
3. Select the root `Player`. Inspector → **Add Component** → `StemBender`.
   - **Source Stem**: drag `Visual/Stem` from the Hierarchy.
   - **Base Anchor**: drag `Visual/LeafBase`.
   - **Head Pivot**: drag `HeadPivot`.
   - **Segment Mesh**: click the circle picker → select **Capsule**.
   - Leave the other values at their defaults.
4. Still on `Player`: **Add Component** → `PlantVisuals`.
   - **Leaf Base**: drag `Visual/LeafBase`.
   - **Plant Puff**: leave empty for now.
5. Still on `Player`: in the `PlantingController` component, check **Planted Height** = `0.8` (carried over from the old Planted Squash value).
6. Click **Save** at the top of the Scene view (or Ctrl+S), then exit Prefab Mode.

Success check: Console shows no errors, and the Player prefab has both new components with all references filled in (no "None").

- [ ] **Step 2: Playtest (human), DemoArena**

Open `Assets/PeaHeads/Scenes/DemoArena.unity` → Play → **Host**.

1. Arrow keys left/right/up/down: stem stays attached to the head, bends smoothly, no gaps at joints.
2. Tap left then right quickly: stem lags slightly and settles; head and camera do not wobble.
3. Press E: camera sinks smoothly, no bounce; leaf base spreads with a small overshoot; stem gets a little thicker.
4. Press E again: leaf base pops back; stem thins; camera rises smoothly.
5. Look straight down: stem visible below, nothing clips into the camera.
6. Get hit 3 times by a second player (or a dummy if available) and respawn: stem segments hide and reappear at the spawn without stretching across the map; the old straight stem does not reappear.
7. Second machine joins (Join `<IP>`): the opponent's stem bends and the plant animation plays on both screens.

Report any Console errors and which check failed. Tuning (all on the Player prefab inspector): stem lag = `StemBender` Spring Amount; squash/stretch strength = Volume Keep; overshoot shape = `PlantVisuals` curves.

- [ ] **Step 3: Update CLAUDE.md**

In `CLAUDE.md`, code layout block, replace:

```
│   │               PlantingController (Tick(dt, pressed) state machine, ApplyRemote, ResetState),
```

with:

```
│   │               PlantingController (Tick(dt, pressed) state machine, ApplyRemote, ResetState, Progress,
│   │                 eased HeightScale = head/camera sink),
│   │               StemBender (visual: capsule chain on a Bezier leaf base → head; replaces Visual/Stem at runtime),
│   │               PlantVisuals (visual: leaf base root-in/pop-out curves, optional plant puff),
```

Replace:

```
├── Tests/EditMode/ PlantingControllerTests (headless: Unity -batchmode -runTests -testPlatform EditMode)
```

with:

```
├── Tests/EditMode/ PlantingControllerTests, StemBenderTests (headless: Unity -batchmode -runTests -testPlatform EditMode)
```

In the Player hierarchy line, replace `` `Visual` (LeafBase, Stem; squashed when planted) `` with `` `Visual` (LeafBase, animated by PlantVisuals; Stem, deactivated at runtime and replaced by StemBender segments) ``.

- [ ] **Step 4: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Prefabs/Player.prefab CLAUDE.md
git commit -m "feat: wire stem bend and plant animation into Player prefab" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Optional follow-up (only if time): add a `PlantPuff` ParticleSystem child at the base of the Player prefab and assign it to `PlantVisuals` → Plant Puff; add `StemBender` to `TargetDummy.prefab` with the same wiring.
