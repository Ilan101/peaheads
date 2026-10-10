# Webcam Head Lean Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Webcam head position drives a proportional, tunable lean/duck of the in-game head (stem follows). Mouth-open fires, a wink zooms, **C** recenters, a HUD shows tracking state, and planting ends with a dust puff.

**Architecture:** Carlos's `HeadTrackingReceiver` (scene object, one per machine) reads UDP. A new `HeadTrackingInput` on the local player maps `Dx/Dy` (dead zone × gain) into the existing `PlayerInput.SetTrackerInput` hook. `PlayerCameraRig` turns −1..1 into metres with separate max lean/rise/duck, and the owner syncs the final metre offset so every machine (and the host's hitbox) agrees. Ilan's `StemBender`/`PlantVisuals` already follow `HeadPivot` and plant progress, so they need no changes.

**Tech Stack:** Unity 6000.3.9f1, URP 17.3, NGO 2.13.3, new Input System, NUnit EditMode tests, Python/OpenCV tracker.

Spec: `docs/superpowers/specs/2026-10-10-webcam-lean-design.md`.

## Global Constraints

- UDP contract with `head_tracker.py` unchanged (`face, dx, dy, aim, shoot` on `127.0.0.1:5052`).
- Keyboard/mouse fallbacks always work (arrows lean, LMB fire, RMB zoom).
- Defaults:

  | Field | Default |
  |---|---|
  | dead zone | 0.03 |
  | lean gain | 2.5 |
  | vertical gain | 3 |
  | max lean | 0.6 m |
  | max rise | 0.2 m |
  | max duck | 0.4 m |
  | mouth fire / wink zoom | on |

  All `[SerializeField, Range(...)]`.
- Lean is allowed in every plant state.
- Conventions: `_camelCase` private fields, minimal comments, one MonoBehaviour per file (filename = class), no `Debug.Log` spam (one warning on port failure is fine).
- Headless Unity only with the editor closed. Commits: conventional, explicit `git add` paths, end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

```bash
REPO=/home/charlie/Documents/CS_Projects/KHIX/peaheads
APP=$REPO/PeaHeadsApp
PH=$APP/Assets/PeaHeads
UNITY=~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity
LOG=/tmp/claude-1000/-home-charlie-Documents-CS-Projects-KHIX-peaheads/ea11c8e5-c500-4269-ae99-e432253422ec/scratchpad
compile() { test -e $APP/Temp/UnityLockfile && { echo "CLOSE THE EDITOR"; return 1; }
  $UNITY -batchmode -nographics -quit -projectPath $APP -logFile $LOG/compile.log; echo exit=$?; grep "error CS" $LOG/compile.log | head; }
tests() { $UNITY -batchmode -nographics -projectPath $APP -runTests -testPlatform EditMode -testResults $LOG/results.xml -logFile $LOG/test.log; echo exit=$?
  grep -o '<test-run[^>]*' $LOG/results.xml | grep -oE '(total|passed|failed)="[0-9]+"'; grep "error CS" $LOG/test.log | head -3; }
```

---

### Task 1: Make the receiver usable + tracker runs on Linux

**Files:**
- Rename: `PeaHeadsApp/Assets/PeaHeads/Scripts/HeadTracking/HeadTrackingReciever.cs` (+ `.meta`) → `HeadTrackingReceiver.cs` (+ `.meta`)
- Modify: that file
- Modify: `head_tracker.py:1,14`

**Interfaces:**
- Produces: `PeaHeads.HeadTrackingReceiver` with `static Instance`, public fields `FaceVisible`, `Dx`, `Dy`, `Aim`, `Shoot` (unchanged names).

- [ ] **Step 1: Rename keeping the GUID**

```bash
cd $PH/Scripts/HeadTracking && git mv HeadTrackingReciever.cs HeadTrackingReceiver.cs && git mv HeadTrackingReciever.cs.meta HeadTrackingReceiver.cs.meta
```

- [ ] **Step 2: Namespace, debug flag, port failure, Instance cleanup**

Wrap the whole class in `namespace PeaHeads { ... }` (indent the class body one level). Then:

After `public float followSpeed = 15f;` add:

```csharp

    [Header("Debug")]
    [SerializeField] private bool _debug;
```

Replace

```csharp
    void OnEnable()
    {
        running = true;
        client = new UdpClient(port);
        thread = new Thread(Listen) { IsBackground = true };
        thread.Start();
    }
```

with

```csharp
    void OnEnable()
    {
        try { client = new UdpClient(port); }
        catch (SocketException e)
        {
            // e.g. a second Unity instance on this PC already listens: that one keeps the webcam, this one uses keys.
            Debug.LogWarning($"[PeaHeads] Head tracking off: can't listen on UDP {port} ({e.Message}).");
            enabled = false;
            return;
        }
        running = true;
        thread = new Thread(Listen) { IsBackground = true };
        thread.Start();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
```

In `OnGUI`, make the first line `if (!_debug) return;`.

- [ ] **Step 3: `head_tracker.py` default camera backend off Windows**

Line 1 becomes `import cv2, socket, json, math, sys`; line 14 becomes:

```python
cap = cv2.VideoCapture(0, cv2.CAP_DSHOW) if sys.platform == "win32" else cv2.VideoCapture(0)  # DSHOW is Windows-only
```

Run `python3 -m py_compile head_tracker.py`. Expected: no output.

- [ ] **Step 4: Compile**: `compile` → `exit=0`, no `error CS`.

- [ ] **Step 5: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/HeadTracking head_tracker.py
git commit -m "fix: make HeadTrackingReceiver attachable, quiet and port-safe; tracker camera backend on Linux

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: HeadTrackingInput bridge + recenter key (TDD for the mapping)

**Files:**
- Create: `PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/HeadTrackingInputTests.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/HeadTracking/HeadTrackingInput.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs`

**Interfaces:**
- Consumes: Task 1 `HeadTrackingReceiver.Instance`; existing `PlayerInput.SetTrackerInput(Vector2, bool, bool)`, `PlayerInput.Current`.
- Produces: `enum TrackingState { Off, NoFace, Tracking }`; `HeadTrackingInput.State`, `HeadTrackingInput.Head` (`Vector2`, −1..1); `static float HeadTrackingInput.Shape(float value, float deadZone, float gain)`; `PlayerInputFrame.RecenterPressed`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class HeadTrackingInputTests
    {
        [Test]
        public void InsideDeadZoneGivesZero() => Assert.AreEqual(0f, HeadTrackingInput.Shape(0.02f, 0.03f, 2.5f));

        [Test]
        public void ScalesDistancePastDeadZone() =>
            Assert.AreEqual(0.25f, HeadTrackingInput.Shape(0.13f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void KeepsSign() => Assert.AreEqual(-0.25f, HeadTrackingInput.Shape(-0.13f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void DoesNotClamp() => Assert.AreEqual(4.925f, HeadTrackingInput.Shape(2f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void ZeroStaysZero() => Assert.AreEqual(0f, HeadTrackingInput.Shape(0f, 0f, 3f));
    }
}
```

- [ ] **Step 2: Run, expect failure**: `tests` → test log has `error CS0103`/`CS0246` for `HeadTrackingInput` (compile failure = RED).

- [ ] **Step 3: `PlayerInput`: recenter key**

In `PlayerInputFrame`, after `public bool ZoomHeld;` add `public bool RecenterPressed;`.
In `Update`'s frame initializer, after the `ZoomHeld = ...,` line add:

```csharp
                RecenterPressed = kb.cKey.wasPressedThisFrame,
```

- [ ] **Step 4: Create `Scripts/HeadTracking/HeadTrackingInput.cs`**

```csharp
using UnityEngine;

namespace PeaHeads
{
    public enum TrackingState { Off, NoFace, Tracking }

    // Webcam head position (HeadTrackingReceiver) → proportional lean/duck via PlayerInput's tracker hook. Local
    // player only (listed in PlayerInput's local-only behaviours). Runs before PlayerInput: same-frame response.
    [DefaultExecutionOrder(-110)]
    [RequireComponent(typeof(PlayerInput))]
    public class HeadTrackingInput : MonoBehaviour
    {
        [Tooltip("Real head movement (in face widths) ignored around neutral.")]
        [SerializeField, Range(0f, 0.2f)] private float _deadZone = 0.03f;
        [Tooltip("Exaggeration. Full in-game lean is reached at deadZone + 1/gain face widths of real lean.")]
        [SerializeField, Range(0.5f, 10f)] private float _leanGain = 2.5f;
        [SerializeField, Range(0.5f, 10f)] private float _verticalGain = 3f;
        [SerializeField] private bool _useMouthFire = true;
        [SerializeField] private bool _useWinkZoom = true;

        public TrackingState State { get; private set; }
        public Vector2 Head { get; private set; }

        private PlayerInput _input;
        private Vector2 _neutral;

        private void Awake() => _input = GetComponent<PlayerInput>();

        private void OnDisable() => SetIdle(TrackingState.Off);

        private void Update()
        {
            HeadTrackingReceiver tracker = HeadTrackingReceiver.Instance;
            if (!tracker || !tracker.isActiveAndEnabled)
            {
                SetIdle(TrackingState.Off);
                return;
            }
            // No call while the face is lost: PlayerInput's tracker data goes stale and the arrow keys take over.
            if (!tracker.FaceVisible)
            {
                SetIdle(TrackingState.NoFace);
                return;
            }

            State = TrackingState.Tracking;
            var raw = new Vector2(tracker.Dx, tracker.Dy);
            if (_input.Current.RecenterPressed) _neutral = raw;
            Head = new Vector2(
                Mathf.Clamp(Shape(raw.x - _neutral.x, _deadZone, _leanGain), -1f, 1f),
                Mathf.Clamp(Shape(raw.y - _neutral.y, _deadZone, _verticalGain), -1f, 1f));
            _input.SetTrackerInput(Head, _useMouthFire && tracker.Shoot, _useWinkZoom && tracker.Aim);
        }

        private void SetIdle(TrackingState state)
        {
            State = state;
            Head = Vector2.zero;
        }

        // Proportional past the dead zone, sign kept, not clamped.
        public static float Shape(float value, float deadZone, float gain) =>
            Mathf.Sign(value) * Mathf.Max(0f, Mathf.Abs(value) - deadZone) * gain;
    }
}
```

- [ ] **Step 5: Run tests, expect pass**: `tests` → `failed="0"`, total = previous + 5.

- [ ] **Step 6: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/HeadTracking/HeadTrackingInput.cs PeaHeadsApp/Assets/PeaHeads/Scripts/HeadTracking/HeadTrackingInput.cs.meta \
  PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/HeadTrackingInputTests.cs PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/HeadTrackingInputTests.cs.meta \
  PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs
git commit -m "feat: HeadTrackingInput maps webcam head offset to proportional lean; C recenters

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Rig limits in metres + sync the metre offset

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerCameraRig.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs`

**Interfaces:**
- Produces: `PlayerCameraRig.HeadOffset` (`Vector3`, local, owner's final offset from rest); `PlayerCameraRig.ApplyRemote(Vector3 headOffset, float pitch)`. Removes `PlayerCameraRig.HeadInput`.

- [ ] **Step 1: Rig fields**

Replace

```csharp
        [Header("Head offset")]
        [SerializeField] private Vector2 _headRange = new(0.6f, 0.3f);
```

with

```csharp
        [Header("Head offset (metres at full input)")]
        [SerializeField, Range(0f, 1.5f)] private float _maxLean = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _maxRise = 0.2f;
        [SerializeField, Range(0f, 1f)] private float _maxDuck = 0.4f;
```

Replace `public Vector2 HeadInput => _head;` with `public Vector3 HeadOffset { get; private set; }`.

Replace `private Vector2 _headTarget;` with:

```csharp
        private Vector2 _headTarget;
        private Vector3 _offsetTarget;
```

- [ ] **Step 2: `ApplyRemote` takes the metre offset**

Replace

```csharp
        public void ApplyRemote(Vector2 head, float pitch)
        {
            _headTarget = head;
            _pitchTarget = pitch;
        }
```

with

```csharp
        public void ApplyRemote(Vector3 headOffset, float pitch)
        {
            _offsetTarget = headOffset;
            _pitchTarget = pitch;
        }
```

- [ ] **Step 3: `Update` + position helpers**

Replace from `            else` (the remote branch) through the end of `ClampedHeadPosition()` with:

```csharp
            else
            {
                _pitch = Mathf.Lerp(_pitch, _pitchTarget, blend);
            }

            if (_isLocal)
            {
                _head = Vector2.Lerp(_head, _headTarget, blend);
                HeadOffset = ClampedOffset();
            }
            // Remote: the owner already swept against walls; just follow its offset.
            else HeadOffset = Vector3.Lerp(HeadOffset, _offsetTarget, blend);

            _headPivot.localPosition = RestPosition() + HeadOffset;
            _headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Rest position sinks with the planted body.
        private Vector3 RestPosition() => new(_pivotBase.x, _pivotBase.y * _planting.HeightScale, _pivotBase.z);

        // Lean/duck from rest, swept so it stops at walls.
        private Vector3 ClampedOffset()
        {
            Vector3 offset = new(_head.x * _maxLean, _head.y * (_head.y > 0f ? _maxRise : _maxDuck), 0f);
            float distance = offset.magnitude;
            if (distance < 0.001f) return Vector3.zero;

            Vector3 direction = offset / distance;
            if (PhysicsUtil.CastIgnoring(transform, transform.TransformPoint(RestPosition()), _headCollisionRadius,
                    transform.TransformDirection(direction), distance, out RaycastHit hit))
                distance = hit.distance;
            return direction * distance;
        }
    }
}
```

(The original lines being replaced are the `else { _pitch = ...; }` block, `_head = Vector2.Lerp(...)`, the two `_headPivot` lines, the closing `}` of `Update`, and the whole `ClampedHeadPosition` method plus the class/namespace closing braces.)

- [ ] **Step 4: `PlayerNetSync`**

Replace `private readonly NetworkVariable<Vector2> _head = new(writePerm: NetworkVariableWritePermission.Owner);` with
`private readonly NetworkVariable<Vector3> _headOffset = new(writePerm: NetworkVariableWritePermission.Owner);`

Replace `_head.Value = _rig.HeadInput;` with `_headOffset.Value = _rig.HeadOffset;` and
`_rig.ApplyRemote(_head.Value, _pitch.Value);` with `_rig.ApplyRemote(_headOffset.Value, _pitch.Value);`.

- [ ] **Step 5: Compile + no leftovers**

Run: `compile`, then `grep -rn "HeadInput\b\|_headRange" $PH/Scripts`.
Expected: `exit=0`, no `error CS`, grep prints nothing.

- [ ] **Step 6: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerCameraRig.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs
git commit -m "feat: max lean/rise/duck sliders; sync head offset in metres so tuning can't desync hitboxes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: HUD tracking indicator

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/UI/PlayerHUD.cs`

**Interfaces:**
- Consumes: Task 2 `HeadTrackingInput.State`, `.Head`, `TrackingState`.

- [ ] **Step 1: Field + Awake**

After `private PlayerCameraRig _rig;` add `private HeadTrackingInput _tracking;`. In `Awake` add `_tracking = GetComponent<HeadTrackingInput>();`.

- [ ] **Step 2: Draw**

After the `GUI.Label(new Rect(20, 270, ...` HP line add:

```csharp
            GUI.Label(new Rect(20, 300, 700, 30), TrackingLabel(), _style);
            if (_tracking && _tracking.State == TrackingState.Tracking) DrawHeadDot(new Rect(20, 334, 60, 60), _tracking.Head);
```

In the controls hint string, replace `Arrows lean head` with `Arrows lean head   C recenter`.

Add methods before the class's closing brace:

```csharp
        private string TrackingLabel() => !_tracking ? "Webcam: off (keyboard)" : _tracking.State switch
        {
            TrackingState.Tracking => "Webcam: tracking",
            TrackingState.NoFace => "Webcam: no face (keyboard)",
            _ => "Webcam: off (keyboard) - run head_tracker.py",
        };

        // Where the webcam currently puts the head: centre = neutral, edges = full lean / rise / duck.
        private static void DrawHeadDot(Rect box, Vector2 head)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = box.center.x + head.x * box.width * 0.5f;
            float y = box.center.y - head.y * box.height * 0.5f;
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
        }
```

- [ ] **Step 3: Compile**: `compile` → `exit=0`, no `error CS`.

- [ ] **Step 4: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/UI/PlayerHUD.cs
git commit -m "feat: HUD shows webcam tracking state and head position

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Wire prefab + scene (temporary setup script, builder)

**Files:**
- Create then delete: `PeaHeadsApp/Assets/PeaHeads/Editor/PlayerPrefabSetup.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Editor/ArenaBuilder.cs`
- Modified by running: `Prefabs/Player.prefab`, `Scenes/DemoArena.unity`

**Interfaces:**
- Consumes: `HeadTrackingInput` (Task 2), `HeadTrackingReceiver` (Task 1), `PlantVisuals._plantPuff`, `PlayerInput._localOnlyBehaviours` (serialized names).

- [ ] **Step 1: Create the temporary `Editor/PlayerPrefabSetup.cs`**

```csharp
using UnityEditor;
using UnityEngine;

namespace PeaHeads.EditorTools
{
    // TEMPORARY: adds HeadTrackingInput (local-only) and a PlantPuff to the Player prefab. Idempotent. Delete after use.
    public static class PlayerPrefabSetup
    {
        private const string PlayerPath = "Assets/PeaHeads/Prefabs/Player.prefab";
        private const string ParticlesMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        public static void Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                if (!root.TryGetComponent(out HeadTrackingInput tracking)) tracking = root.AddComponent<HeadTrackingInput>();

                var input = new SerializedObject(root.GetComponent<PlayerInput>());
                SerializedProperty list = input.FindProperty("_localOnlyBehaviours");
                bool listed = false;
                for (int i = 0; i < list.arraySize; i++)
                    listed |= list.GetArrayElementAtIndex(i).objectReferenceValue == tracking;
                if (!listed)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = tracking;
                    input.ApplyModifiedPropertiesWithoutUndo();
                }

                Transform puff = root.transform.Find("PlantPuff");
                if (!puff) puff = BuildPuff(root.transform);
                var visuals = new SerializedObject(root.GetComponent<PlantVisuals>());
                visuals.FindProperty("_plantPuff").objectReferenceValue = puff.GetComponent<ParticleSystem>();
                visuals.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[PeaHeads] Player prefab setup done");
        }

        private static Transform BuildPuff(Transform parent)
        {
            var ps = new GameObject("PlantPuff").AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.42f, 0.25f), new Color(0.45f, 0.55f, 0.3f));
            main.gravityModifier = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 75f;
            shape.radius = 0.5f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(ParticlesMaterialPath);
            return ps.transform;
        }
    }
}
```

- [ ] **Step 2: Run it**

```bash
test -e $APP/Temp/UnityLockfile && echo "CLOSE THE EDITOR" || \
$UNITY -batchmode -nographics -quit -projectPath $APP -executeMethod PeaHeads.EditorTools.PlayerPrefabSetup.Apply -logFile $LOG/setup.log; echo exit=$?
grep -E "error CS|Exception|\[PeaHeads\]" $LOG/setup.log | head
grep -c "m_Name: PlantPuff" $PH/Prefabs/Player.prefab            # expect 1
grep -c "PeaHeads.HeadTrackingInput" $PH/Prefabs/Player.prefab   # expect 1
grep -A1 "_plantPuff:" $PH/Prefabs/Player.prefab | head -1       # expect non-zero fileID
```

- [ ] **Step 3: Re-run once (idempotency)**: repeat Step 2; counts stay `1`.

- [ ] **Step 4: Delete the setup script**: `rm $PH/Editor/PlayerPrefabSetup.cs $PH/Editor/PlayerPrefabSetup.cs.meta`

- [ ] **Step 5: ArenaBuilder: ensure the scene receiver**

After `EnsureMatchObject(scene);` in `Build()` add `EnsureHeadTrackingObject(scene);`. After the `EnsureMatchObject` method add:

```csharp
        // One webcam receiver per machine, as a scene object: on the Player prefab, remote copies would fight over
        // the UDP port. Created once, never deleted.
        private static void EnsureHeadTrackingObject(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(r => r.name == "HeadTracking")) return;
            new GameObject("HeadTracking").AddComponent<HeadTrackingReceiver>();
        }
```

Update the class comment's "created only if missing" sentence to mention `HeadTracking`.

- [ ] **Step 6: Run the builder; check**

```bash
$UNITY -batchmode -nographics -quit -projectPath $APP -executeMethod PeaHeads.EditorTools.ArenaBuilder.Build -logFile $LOG/build.log; echo exit=$?
grep -E "error CS|Exception|\[PeaHeads\]" $LOG/build.log | head
S=$PH/Scenes/DemoArena.unity
grep -c "m_Name: HeadTracking$" $S     # expect 1
grep -c "m_Name: Match$" $S            # expect 1
```

Then count oaks at (−6.76, 6.48), expecting exactly 1. The stray root copy was moved under `Environment` by the local edit, so the rebuild removes it:

```bash
python3 - <<'EOF'
import re
s=open('/home/charlie/Documents/CS_Projects/KHIX/peaheads/PeaHeadsApp/Assets/PeaHeads/Scenes/DemoArena.unity').read()
n=0
for b in re.split(r'\n--- ',s):
    if b.startswith('!u!1001') and 'a76f0fda2711c7220b83498d325158bd' in b.split('m_SourcePrefab')[-1]:
        v=dict(re.findall(r'propertyPath: (m_LocalPosition\.[xz])\n\s+value: (\S*)',b))
        n+= abs(float(v.get('m_LocalPosition.x',0))+6.76)<0.05 and abs(float(v.get('m_LocalPosition.z',0))-6.48)<0.05
print('oaks at (-6.76,6.48):', n)
EOF
```

- [ ] **Step 7: Tests**: `tests` → `failed="0"`.

- [ ] **Step 8: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Editor/ArenaBuilder.cs PeaHeadsApp/Assets/PeaHeads/Prefabs/Player.prefab PeaHeadsApp/Assets/PeaHeads/Scenes/DemoArena.unity
git status --short   # PlayerPrefabSetup must not appear
git commit -m "feat: wire webcam lean into Player prefab and DemoArena; plant dust puff

Adds HeadTrackingInput (local-only) and a PlantPuff burst to the Player prefab
and a HeadTracking receiver object to DemoArena. The rebuild also drops a
duplicate oak left at the scene root.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Docs, review, handoff

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: CLAUDE.md**
  - CV ↔ Unity contract: replace the `Unity side (todo): WebcamInputReceiver ...` paragraph with: `Unity side: HeadTrackingReceiver (scene object "HeadTracking", one per machine; background UdpClient thread; disables itself if the port is taken) → HeadTrackingInput on the local Player (dead zone + lean/vertical gain, C recenters, mouth-fire / wink-zoom toggles) → PlayerInput.SetTrackerInput. Keyboard/mouse fallbacks always on: arrows = head offset, LMB = fire, RMB = zoom.`
  - Open items for Person 1: replace the `CAP_DSHOW` sentence with `CAP_DSHOW now only on Windows (fixed by Person 2, 2026-10-10).`
  - Code layout: replace `│   ├── Tracking/   (todo) WebcamInputReceiver, GestureController` with `│   ├── HeadTracking/ HeadTrackingReceiver (Carlos; UDP 5052 → Instance.FaceVisible/Dx/Dy/Aim/Shoot), HeadTrackingInput (→ PlayerInput, tunable gain)`; in the `PlayerCameraRig` line mention `max lean/rise/duck sliders, HeadOffset synced in metres`; add `HeadTrackingInputTests` to the Tests line.
  - Camera row in "Locked decisions": append `Webcam lean is proportional (HeadTrackingInput gain), keys give full lean.`
  - Head-input sentence under the Player hierarchy: replace `the webcam receiver calls PlayerInput.SetTrackerInput(head, shoot, aim) (main thread)` with `HeadTrackingInput calls PlayerInput.SetTrackerInput(head, shoot, aim) each frame while a face is tracked`.
  - Networking model: replace `head pose synced via owner-writable NetworkVariable<Vector3> (offset) + aim yaw/pitch` with `head pose synced as the owner's final metre offset (NetworkVariable<Vector3>, after wall sweep) + pitch`.

- [ ] **Step 2: Code review**: dispatch the **code-reviewer** agent on `git diff <spec commit>..HEAD -- '*.cs' head_tracker.py`. Fix CRITICAL/HIGH, re-run `compile` + `tests`, commit fixes.

- [ ] **Step 3: Commit docs**

```bash
cd $REPO && git add CLAUDE.md && git commit -m "docs: document webcam lean pipeline

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Playtest checklist (human, with webcam; report to user)**
  1. `python head_tracker.py` (look straight on start), then Play → Host. The HUD says `Webcam: tracking` and the dot moves with your head.
  2. Lean a little and the camera leans a little; lean more and it goes further, up to max lean. Duck and the camera and stem lower; sit tall and they rise.
  3. Press C while sitting naturally: the dot recentres.
  4. Open your mouth while planted: one pea per open. A wink zooms.
  5. Quit the tracker: the HUD falls back to `no face`/`off` and the arrows lean again.
  6. Tune on `Player` → `HeadTrackingInput` (gain, dead zone) and `PlayerCameraRig` (max lean/rise/duck). Copy the values to the prefab (Play-mode edits are lost).
  7. Two machines: the opponent's head and stem lean where they see it, and shots at a leaned-out head register.
  8. Planting ends with a dust puff.
- [ ] **Step 5: Tell Person 1** about the `head_tracker.py` one-liner and **Carlos** about the receiver rename/namespace.
