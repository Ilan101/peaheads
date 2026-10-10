# DemoArena Multiplayer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the first-person `DemoArena` playable by two players over LAN with host-authoritative combat.

**Architecture:** Existing player components become network-aware. A new `PlayerInput` is the only device reader and is enabled only on the owner's plant; `PlayerNetSync` shares head lean, pitch and plant state through owner-write NetworkVariables; firing goes through a server RPC, the host spawns and moves networked peas and owns HP. Spec: `docs/superpowers/specs/2026-10-10-demo-multiplayer-design.md`.

**Tech Stack:** Unity 6000.3.9f1, URP, Input System 1.18, Netcode for GameObjects 2.13.3, Unity Transport 2.7.4, Unity Test Framework 1.6 (NUnit, EditMode).

## Global Constraints

- Unity **6000.3.9f1**; NGO **2.13.3** (do not upgrade — 3.x needs Unity 6000.7).
- All runtime scripts live under `PeaHeadsApp/Assets/PeaHeads/` and compile into `PeaHeads.asmdef` (already references `Unity.InputSystem`, `Unity.Netcode.Runtime`, `Unity.Networking.Transport`, `Unity.Collections`). Editor scripts go in `Assets/PeaHeads/Editor/` (`PeaHeads.Editor.asmdef`).
- Namespaces: gameplay = `PeaHeads`; networking helpers in `Scripts/Net/` = `PeaHeads.Net`; tests = `PeaHeads.Tests`.
- C# style: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private`. Minimal comments. No `Debug.Log` left in.
- **Keep existing serialized field names** (`_headPivot`, `_camera`, `_hiddenInFirstPerson`, `_mouth`, `_peaPrefab`, `_flashRenderer`, `_hitSfx`, `_fireSfx`, `_respawnDelay`, `_visual`, …) so prefab wiring survives.
- Do not modify Ilan's files: `Scripts/Net/ConnectionUI.cs`, `Scripts/Net/Test/*`, `Editor/NetTestSceneBuilder.cs`, `Scenes/NetTest.unity`, `Prefabs/NetTest/*`.
- `PeaHeads.PlayerInput` shares its name with `UnityEngine.InputSystem.PlayerInput`. Never add `using UnityEngine.InputSystem;` to a file that references `PlayerInput` outside namespace `PeaHeads` (inside it, the enclosing namespace wins).
- Batch-mode Unity only works with the **Editor closed**. Check first: `pgrep -x Unity` must print nothing.
- Between tasks 2–5 the Player prefab is temporarily inconsistent (scripts expect components added in Task 6). Only compile is verified until Task 6; do not playtest before Task 6.

**Shared commands** (used by every task; run from repo root `/home/charlie/Documents/CS_Projects/KHIX/peaheads`):

```bash
UNITY=~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity
P=$PWD/PeaHeadsApp
LOGS=/tmp/claude-1000/-home-charlie-Documents-CS-Projects-KHIX-peaheads/c358b064-f427-46b5-a0e6-276f7da5b7e0/scratchpad
```

- **Compile:** `"$UNITY" -batchmode -nographics -quit -projectPath "$P" -logFile $LOGS/compile.log; grep -E "error CS|warning CS" $LOGS/compile.log | grep PeaHeads` → expected: no output.
- **EditMode tests:** `"$UNITY" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testResults $LOGS/tests.xml -logFile $LOGS/tests.log; grep -oE 'result="[A-Za-z]+" total="[0-9]+" passed="[0-9]+" failed="[0-9]+"' $LOGS/tests.xml | head -1` (no `-quit`; exit code 0 = all passed).

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Scripts/Player/PlayerInput.cs` | Create | Only device reader; `PlayerInputFrame`; cursor lock; `SetLocal`; tracker hook |
| `Scripts/Player/PlantingController.cs` | Modify | `Tick` logic, `ApplyRemote`, `ResetState`, reads input |
| `Scripts/Player/PlayerMovement.cs` | Modify | Reads input, owner-only, `Teleport` |
| `Scripts/Player/PlayerCameraRig.cs` | Modify | Reads input, `SetLocal`, `ApplyRemote`, exposes head/pitch |
| `Scripts/Player/PlayerHealth.cs` | Modify | NetworkBehaviour, server HP, client effects, respawn, `Revived` |
| `Scripts/Player/PlayerShooting.cs` | Modify | NetworkBehaviour, `FireRpc`, SFX broadcast |
| `Scripts/Combat/PeaProjectile.cs` | Modify | NetworkBehaviour, server-only movement/hits |
| `Scripts/UI/PlayerHUD.cs` | Modify | Move status label below the connect panel |
| `Scripts/Net/PlayerNetSync.cs` | Create | Owner-write head/pitch/plant vars, spawn setup, tint, respawn teleport |
| `Scripts/Net/SpawnPoint.cs` | Create | Spawn marker + static lookup |
| `Scripts/Net/OverviewCamera.cs` | Create | Pre-connect camera that hides while a local player exists |
| `Tests/EditMode/PeaHeads.Tests.asmdef` | Create | Test assembly |
| `Tests/EditMode/PlantingControllerTests.cs` | Create | State machine tests |
| `Editor/MultiplayerDemoSetup.cs` | Create → run → **delete** | One-off prefab + scene rewiring |
| `Prefabs/Player.prefab`, `Pea.prefab`, `TargetDummy.prefab`, `Scenes/DemoArena.unity`, `DefaultNetworkPrefabs.asset` | Modified by setup script | Network components, scene objects |
| `CLAUDE.md` | Modify | Document the result |

(All `Scripts/…`, `Tests/…`, `Editor/…`, `Prefabs/…`, `Scenes/…` paths are under `PeaHeadsApp/Assets/PeaHeads/`.)

---

### Task 1: PlayerInput + PlantingController Tick (TDD)

**Files:**
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/OverviewCamera.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/PeaHeads.Tests.asmdef`
- Create: `PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/PlantingControllerTests.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantingController.cs` (whole file)

**Interfaces:**
- Produces:
  - `struct PlayerInputFrame { Vector2 Move; Vector2 Look; Vector2 Head; bool PlantPressed; bool FirePressed; bool ZoomHeld; }`
  - `PlayerInput : MonoBehaviour` — `PlayerInputFrame Current { get; }`, `void SetLocal(bool isLocal)`, `void SetTrackerInput(Vector2 head, bool shoot, bool aim)`
  - `enum PlantState : byte { Unplanted, Planting, Planted, Unplanting }`
  - `PlantingController` — `PlantState State`, `bool CanMove`, `bool CanShoot`, `float HeightScale`, `void Tick(float deltaTime, bool plantPressed)`, `void ApplyRemote(PlantState state)`, `void ResetState()`
  - `OverviewCamera.SetVisible(bool visible)` (static; `PlayerInput` calls it, so it is created in this task, Step 3b)
- Consumes: `PlayerHealth.IsDead` (already exists).

- [ ] **Step 1: Write the test assembly definition**

`PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/PeaHeads.Tests.asmdef`:
```json
{
    "name": "PeaHeads.Tests",
    "rootNamespace": "PeaHeads.Tests",
    "references": [
        "PeaHeads",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the failing tests**

`PeaHeadsApp/Assets/PeaHeads/Tests/EditMode/PlantingControllerTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantingControllerTests
    {
        private GameObject _go;
        private PlantingController _planting;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Plant");
            _planting = _go.AddComponent<PlantingController>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private void Plant()
        {
            _planting.Tick(0f, true);
            _planting.Tick(1.01f, false);
        }

        [Test]
        public void StartsUnplantedAndCanMove()
        {
            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.IsTrue(_planting.CanMove);
            Assert.IsFalse(_planting.CanShoot);
        }

        [Test]
        public void PressStartsPlantingAndBlocksMoveAndShoot()
        {
            _planting.Tick(0.016f, true);

            Assert.AreEqual(PlantState.Planting, _planting.State);
            Assert.IsFalse(_planting.CanMove);
            Assert.IsFalse(_planting.CanShoot);
        }

        [Test]
        public void PlantingCompletesAfterOneSecond()
        {
            _planting.Tick(0f, true);
            _planting.Tick(0.99f, false);
            Assert.AreEqual(PlantState.Planting, _planting.State);

            _planting.Tick(0.02f, false);
            Assert.AreEqual(PlantState.Planted, _planting.State);
            Assert.IsTrue(_planting.CanShoot);
        }

        [Test]
        public void PressDuringTransitionIsIgnored()
        {
            _planting.Tick(0f, true);
            _planting.Tick(0.5f, true);
            Assert.AreEqual(PlantState.Planting, _planting.State);

            _planting.Tick(0.51f, false);
            Assert.AreEqual(PlantState.Planted, _planting.State);
        }

        [Test]
        public void UnplantingDisablesShootingThenReturnsToUnplanted()
        {
            Plant();

            _planting.Tick(0f, true);
            Assert.AreEqual(PlantState.Unplanting, _planting.State);
            Assert.IsFalse(_planting.CanShoot);

            _planting.Tick(1.01f, false);
            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.IsTrue(_planting.CanMove);
        }

        [Test]
        public void ApplyRemoteSetsState()
        {
            _planting.ApplyRemote(PlantState.Planted);

            Assert.AreEqual(PlantState.Planted, _planting.State);
            Assert.IsTrue(_planting.CanShoot);
        }

        [Test]
        public void ResetStateReturnsToStandingUnplanted()
        {
            Plant();

            _planting.ResetState();

            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.AreEqual(1f, _planting.HeightScale);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run the **EditMode tests** shared command.
Expected: test run fails to compile — `tests.log` contains `error CS1061: 'PlantingController' does not contain a definition for 'Tick'` (and for `ApplyRemote`, `ResetState`).

- [ ] **Step 3b: Create PlayerInput and OverviewCamera**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs`:
```csharp
using PeaHeads.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PeaHeads
{
    public struct PlayerInputFrame
    {
        public Vector2 Move;
        public Vector2 Look;
        public Vector2 Head;
        public bool PlantPressed;
        public bool FirePressed;
        public bool ZoomHeld;
    }

    // The only reader of keyboard/mouse (and later the webcam). Enabled only on the local player's plant.
    [DefaultExecutionOrder(-100)]
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField] private float _mouseSensitivity = 0.1f;
        [SerializeField] private float _trackerTimeout = 0.5f;
        [Tooltip("Switched on/off with local ownership: own camera, HUD.")]
        [SerializeField] private GameObject[] _localOnlyObjects = new GameObject[0];
        [SerializeField] private Behaviour[] _localOnlyBehaviours = new Behaviour[0];

        public PlayerInputFrame Current { get; private set; }

        private Vector2 _trackerHead;
        private bool _trackerAim;
        private bool _trackerShootPrevious;
        private bool _trackerFirePending;
        private float _trackerTime = float.NegativeInfinity;

        public void SetLocal(bool isLocal)
        {
            foreach (GameObject go in _localOnlyObjects) go.SetActive(isLocal);
            foreach (Behaviour behaviour in _localOnlyBehaviours) behaviour.enabled = isLocal;
            enabled = isLocal;
            if (!isLocal) Current = default;
        }

        // Webcam receiver calls this on the main thread once per packet. head is -1..1.
        public void SetTrackerInput(Vector2 head, bool shoot, bool aim)
        {
            _trackerHead = new Vector2(Mathf.Clamp(head.x, -1f, 1f), Mathf.Clamp(head.y, -1f, 1f));
            _trackerAim = aim;
            if (shoot && !_trackerShootPrevious) _trackerFirePending = true;
            _trackerShootPrevious = shoot;
            _trackerTime = Time.time;
        }

        private void OnEnable()
        {
            SetCursorLocked(true);
            OverviewCamera.SetVisible(false);
        }

        private void OnDisable()
        {
            Current = default;
            SetCursorLocked(false);
            OverviewCamera.SetVisible(true);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                Current = default;
                return;
            }

            bool click = mouse.leftButton.wasPressedThisFrame;
            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (click && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
                click = false;
            }
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool trackerFresh = Time.time - _trackerTime < _trackerTimeout;

            Current = new PlayerInputFrame
            {
                Move = new Vector2(Axis(kb.aKey, kb.dKey), Axis(kb.sKey, kb.wKey)),
                Look = locked ? mouse.delta.ReadValue() * _mouseSensitivity : Vector2.zero,
                Head = trackerFresh
                    ? _trackerHead
                    : new Vector2(Axis(kb.leftArrowKey, kb.rightArrowKey), Axis(kb.downArrowKey, kb.upArrowKey)),
                PlantPressed = kb.eKey.wasPressedThisFrame,
                FirePressed = locked && (click || _trackerFirePending),
                ZoomHeld = locked && (mouse.rightButton.isPressed || (trackerFresh && _trackerAim)),
            };
            _trackerFirePending = false;
        }

        private static float Axis(KeyControl negative, KeyControl positive) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
```

`PeaHeadsApp/Assets/PeaHeads/Scripts/Net/OverviewCamera.cs`:
```csharp
using UnityEngine;

namespace PeaHeads.Net
{
    // Scene camera for the connect screen; hidden while a local player's own camera is active.
    public class OverviewCamera : MonoBehaviour
    {
        private static OverviewCamera _instance;

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void SetVisible(bool visible)
        {
            if (_instance) _instance.gameObject.SetActive(visible);
        }
    }
}
```

- [ ] **Step 4: Rewrite PlantingController**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantingController.cs` (replace whole file):
```csharp
using UnityEngine;

namespace PeaHeads
{
    public enum PlantState : byte { Unplanted, Planting, Planted, Unplanting }

    public class PlantingController : MonoBehaviour
    {
        [SerializeField] private float _transitionTime = 1f;
        [SerializeField] private Transform _visual;
        [SerializeField] private float _plantedSquash = 0.8f;

        public PlantState State { get; private set; }
        public bool CanMove => State == PlantState.Unplanted;
        public bool CanShoot => State == PlantState.Planted;
        // Current vertical squash of the body (1 = standing). The head rides down with it.
        public float HeightScale => Mathf.Lerp(1f, _plantedSquash, _progress);

        private PlayerInput _input;
        private PlayerHealth _health;
        private float _timer;
        private float _progress;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        // Remote copies get no input, so this only advances the squash animation for them.
        private void Update() => Tick(Time.deltaTime, _input.Current.PlantPressed && !_health.IsDead);

        public void Tick(float deltaTime, bool plantPressed)
        {
            if (plantPressed) Toggle();

            if (State is PlantState.Planting or PlantState.Unplanting)
            {
                _timer -= deltaTime;
                if (_timer <= 0f) State = State == PlantState.Planting ? PlantState.Planted : PlantState.Unplanted;
            }

            float target = State is PlantState.Planting or PlantState.Planted ? 1f : 0f;
            _progress = Mathf.MoveTowards(_progress, target, deltaTime / _transitionTime);
            if (_visual) _visual.localScale = new Vector3(1f, HeightScale, 1f);
        }

        public void ApplyRemote(PlantState state)
        {
            if (state == State) return;
            State = state;
            _timer = _transitionTime;
        }

        public void ResetState()
        {
            State = PlantState.Unplanted;
            _timer = 0f;
            _progress = 0f;
            if (_visual) _visual.localScale = Vector3.one;
        }

        // Presses during a transition are ignored so planting is a real commitment.
        private void Toggle()
        {
            if (State == PlantState.Unplanted) Begin(PlantState.Planting);
            else if (State == PlantState.Planted) Begin(PlantState.Unplanting);
        }

        private void Begin(PlantState state)
        {
            State = state;
            _timer = _transitionTime;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the **EditMode tests** shared command.
Expected: `result="Passed" total="7" passed="7" failed="0"`.

- [ ] **Step 6: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs* PeaHeadsApp/Assets/PeaHeads/Scripts/Net/OverviewCamera.cs* PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlantingController.cs PeaHeadsApp/Assets/PeaHeads/Tests*
git commit -m "feat: add PlayerInput and testable plant state machine"
```

---

### Task 2: Movement and camera rig read PlayerInput

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerMovement.cs` (whole file)
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerCameraRig.cs` (whole file)
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/UI/PlayerHUD.cs:25`

**Interfaces:**
- Consumes: `PlayerInput.Current`, `PlayerInput.enabled`, `PlantingController.CanMove`, `PlantingController.HeightScale`, `PlayerHealth.IsDead` (exists today).
- Produces:
  - `PlayerMovement.Teleport(Vector3 position, Quaternion rotation)`
  - `PlayerCameraRig` — `Camera Camera`, `bool IsZoomed`, `Vector2 HeadInput`, `float Pitch`, `void SetLocal(bool isLocal)`, `void ApplyRemote(Vector2 head, float pitch)`

- [ ] **Step 1: Rewrite PlayerMovement**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerMovement.cs`:
```csharp
using Unity.Netcode.Components;
using UnityEngine;

namespace PeaHeads
{
    [RequireComponent(typeof(CharacterController), typeof(PlantingController), typeof(PlayerInput))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float _speed = 5f;

        private CharacterController _controller;
        private PlantingController _planting;
        private PlayerInput _input;
        private PlayerHealth _health;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _planting = GetComponent<PlantingController>();
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            // Remote copies are positioned by NetworkTransform.
            if (!_input.enabled) return;

            Vector2 move = _planting.CanMove && !_health.IsDead ? _input.Current.Move : Vector2.zero;
            Vector3 velocity = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(move.x, 0f, move.y), 1f)) * _speed;
            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Physics.gravity.y * Time.deltaTime;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        // CharacterController overrides direct position writes, so it's disabled around the move.
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false;
            if (TryGetComponent(out NetworkTransform netTransform) && netTransform.IsSpawned)
                netTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }
    }
}
```

- [ ] **Step 2: Rewrite PlayerCameraRig**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerCameraRig.cs`:
```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace PeaHeads
{
    // First person. Look = yaw (whole plant) + pitch (head). Head input = bounded positional offset of the
    // head (and the camera inside it), which is what lets you peek around cover without moving the base.
    [RequireComponent(typeof(PlantingController), typeof(PlayerInput))]
    public class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _headPivot;
        [SerializeField] private Camera _camera;
        [Tooltip("Own head meshes: hidden from the local camera but still cast shadows.")]
        [SerializeField] private Renderer[] _hiddenInFirstPerson;
        [SerializeField] private Vector2 _pitchLimits = new(-70f, 70f);

        [Header("Head offset")]
        [SerializeField] private Vector2 _headRange = new(0.6f, 0.3f);
        [SerializeField] private float _headSmoothing = 10f;
        [Tooltip("Lean stops this far from walls so the camera can't see through them.")]
        [SerializeField] private float _headCollisionRadius = 0.3f;

        [Header("Zoom")]
        [SerializeField] private float _zoomFactor = 1.5f;
        [SerializeField] private float _zoomSpeed = 12f;

        public Camera Camera => _camera;
        public bool IsZoomed { get; private set; }
        public Vector2 HeadInput => _head;
        public float Pitch => _pitch;

        private PlantingController _planting;
        private PlayerInput _input;
        private Vector3 _pivotBase;
        private Vector2 _head;
        private Vector2 _headTarget;
        private float _pitch;
        private float _pitchTarget;
        private float _baseFov;
        private bool _isLocal;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _input = GetComponent<PlayerInput>();
            _pivotBase = _headPivot.localPosition;
            _baseFov = _camera.fieldOfView;
        }

        public void SetLocal(bool isLocal)
        {
            _isLocal = isLocal;
            ShadowCastingMode mode = isLocal ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (Renderer r in _hiddenInFirstPerson) r.shadowCastingMode = mode;
        }

        // Remote copies: targets come from the network instead of local input.
        public void ApplyRemote(Vector2 head, float pitch)
        {
            _headTarget = head;
            _pitchTarget = pitch;
        }

        private void Update()
        {
            float blend = 1f - Mathf.Exp(-_headSmoothing * Time.deltaTime);
            if (_isLocal)
            {
                PlayerInputFrame input = _input.Current;
                transform.Rotate(0f, input.Look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - input.Look.y, _pitchLimits.x, _pitchLimits.y);
                _headTarget = input.Head;
                IsZoomed = input.ZoomHeld;
                float fov = IsZoomed ? _baseFov / _zoomFactor : _baseFov;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_zoomSpeed * Time.deltaTime));
            }
            else
            {
                _pitch = Mathf.Lerp(_pitch, _pitchTarget, blend);
            }

            _head = Vector2.Lerp(_head, _headTarget, blend);
            _headPivot.localPosition = ClampedHeadPosition();
            _headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Rest position sinks with the planted body; the lean is swept from there and stops at walls.
        private Vector3 ClampedHeadPosition()
        {
            Vector3 rest = new(_pivotBase.x, _pivotBase.y * _planting.HeightScale, _pivotBase.z);
            Vector3 offset = new(_head.x * _headRange.x, _head.y * _headRange.y, 0f);
            float distance = offset.magnitude;
            if (distance < 0.001f) return rest;

            Vector3 worldRest = transform.TransformPoint(rest);
            Vector3 worldDirection = transform.TransformDirection(offset / distance);
            if (PhysicsUtil.CastIgnoring(transform, worldRest, _headCollisionRadius, worldDirection, distance,
                    out RaycastHit hit))
                distance = hit.distance;
            return rest + offset.normalized * distance;
        }
    }
}
```

- [ ] **Step 3: Move the HUD status label below the connect panel**

In `PeaHeadsApp/Assets/PeaHeads/Scripts/UI/PlayerHUD.cs` replace:
```csharp
            GUI.Label(new Rect(20, 20, 600, 30), $"HP {_health.Current}    {_planting.State}{ready}", _style);
```
with:
```csharp
            GUI.Label(new Rect(20, 270, 600, 30), $"HP {_health.Current}    {_planting.State}{ready}", _style);
```

- [ ] **Step 4: Compile**

Run the **Compile** shared command. Expected: no output. Then run **EditMode tests**: expected `passed="7" failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerMovement.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerCameraRig.cs PeaHeadsApp/Assets/PeaHeads/Scripts/UI/PlayerHUD.cs
git commit -m "refactor: movement and camera rig read PlayerInput, support remote copies"
```

---

### Task 3: Networked PlayerHealth

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs` (whole file)

**Interfaces:**
- Produces: `PlayerHealth : NetworkBehaviour` — `int Current`, `bool IsDead`, `event Action<PlayerHealth> Died` (server), `event Action Revived` (all instances), `void TakeDamage(int amount)` (server only), `void ResetHealth()` (server only).
- Consumes: nothing new.

- [ ] **Step 1: Rewrite PlayerHealth**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs`:
```csharp
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host owns the HP value; every instance reacts to changes (flash, sound, hide/show).
    public class PlayerHealth : NetworkBehaviour
    {
        private const int DefaultMaxHealth = 3;

        [SerializeField] private int _maxHealth = DefaultMaxHealth;
        [Tooltip("> 0: host restores HP after this many seconds.")]
        [SerializeField] private float _respawnDelay;
        [SerializeField] private Renderer _flashRenderer;
        [SerializeField] private AudioClip _hitSfx;

        private readonly NetworkVariable<int> _current = new(DefaultMaxHealth);

        public int Current => _current.Value;
        public bool IsDead => _current.Value <= 0;
        public event Action<PlayerHealth> Died;
        public event Action Revived;

        private bool _flashing;
        private Color _flashOriginal;

        public override void OnNetworkSpawn()
        {
            if (IsServer) _current.Value = _maxHealth;
            _current.OnValueChanged += OnHealthChanged;
            SetAlive(!IsDead);
        }

        public override void OnNetworkDespawn() => _current.OnValueChanged -= OnHealthChanged;

        public void TakeDamage(int amount)
        {
            if (!IsServer || IsDead) return;
            _current.Value = Mathf.Max(0, _current.Value - amount);
            if (!IsDead) return;

            Died?.Invoke(this);
            if (_respawnDelay > 0f) Invoke(nameof(ResetHealth), _respawnDelay);
        }

        public void ResetHealth()
        {
            if (IsServer) _current.Value = _maxHealth;
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous)
            {
                if (_hitSfx) AudioSource.PlayClipAtPoint(_hitSfx, transform.position);
                if (_flashRenderer) StartCoroutine(Flash());
            }

            if (current <= 0) SetAlive(false);
            else if (previous <= 0)
            {
                SetAlive(true);
                Revived?.Invoke();
            }
        }

        private void SetAlive(bool alive)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = alive;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c is not CharacterController) c.enabled = alive;
        }

        // Original color captured only by the first of overlapping flashes, so red never sticks.
        private IEnumerator Flash()
        {
            Material material = _flashRenderer.material;
            if (!_flashing) _flashOriginal = material.color;
            _flashing = true;
            material.color = Color.red;
            yield return new WaitForSeconds(0.12f);
            material.color = _flashOriginal;
            _flashing = false;
        }
    }
}
```

- [ ] **Step 2: Compile**

Run **Compile**. Expected: no output. (`PlayerShooting`/`PeaProjectile` still call `TakeDamage` locally — they compile unchanged.)

- [ ] **Step 3: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs
git commit -m "feat: host-authoritative PlayerHealth with synced HP and respawn"
```

---

### Task 4: Networked shooting and peas

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Combat/PeaProjectile.cs` (whole file)
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerShooting.cs` (whole file)

**Interfaces:**
- Consumes: `PlayerInput.Current.FirePressed`, `PlantingController.CanShoot`, `PlayerHealth.IsDead`, `PlayerHealth.TakeDamage`, `PlayerCameraRig.Camera`, `PhysicsUtil.CastIgnoring`.
- Produces: `PeaProjectile : NetworkBehaviour` — `void Launch(Transform owner, Vector3 sweepFrom)` (server, call after `Spawn`); `PlayerShooting : NetworkBehaviour` — `bool TryFire()` (owner).

- [ ] **Step 1: Rewrite PeaProjectile**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Combat/PeaProjectile.cs`:
```csharp
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host moves the pea and resolves hits; clients only see it through NetworkTransform.
    public class PeaProjectile : NetworkBehaviour
    {
        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _radius = 0.2f;
        [SerializeField] private float _lifetime = 4f;
        [SerializeField] private int _damage = 1;

        private Transform _owner;
        private float _despawnTime;
        private bool _done;

        // Server only, right after Spawn(). sweepFrom is a point inside the shooter (head centre): the gap from
        // there to the spawn point is checked first, so a mouth poking through a wall can't fire through it.
        public void Launch(Transform owner, Vector3 sweepFrom)
        {
            _owner = owner;
            _despawnTime = Time.time + _lifetime;

            Vector3 gap = transform.position - sweepFrom;
            float length = gap.magnitude;
            if (length > 0.001f && Sweep(sweepFrom, gap / length, length, out RaycastHit hit)) Impact(hit);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _done) return;
            if (Time.time >= _despawnTime)
            {
                Finish();
                return;
            }

            float step = _speed * Time.deltaTime;
            if (Sweep(transform.position, transform.forward, step, out RaycastHit hit))
            {
                Impact(hit);
                return;
            }
            transform.position += transform.forward * step;
        }

        private bool Sweep(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) =>
            PhysicsUtil.CastIgnoring(_owner, origin, _radius, direction, distance, out hit,
                QueryTriggerInteraction.Collide);

        private void Impact(RaycastHit hit)
        {
            PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
            if (health) health.TakeDamage(_damage);
            Finish();
        }

        private void Finish()
        {
            _done = true;
            NetworkObject.Despawn();
        }
    }
}
```

- [ ] **Step 2: Rewrite PlayerShooting**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerShooting.cs`:
```csharp
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    [RequireComponent(typeof(PlantingController), typeof(PlayerCameraRig), typeof(PlayerInput))]
    public class PlayerShooting : NetworkBehaviour
    {
        // Host accepts shots slightly early so network jitter doesn't eat legit ones.
        private const float HostCooldownTolerance = 0.8f;
        // Reject fire origins further than this from the shooter (bad/forged RPC data).
        private const float MaxOriginDistance = 3f;

        [SerializeField] private PeaProjectile _peaPrefab;
        [SerializeField] private Transform _mouth;
        [SerializeField] private float _cooldown = 0.6f;
        [SerializeField] private float _maxAimDistance = 100f;
        [SerializeField] private AudioClip _fireSfx;

        private PlantingController _planting;
        private PlayerCameraRig _rig;
        private PlayerInput _input;
        private PlayerHealth _health;
        private float _nextFireTime;
        private float _hostNextFireTime;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _rig = GetComponent<PlayerCameraRig>();
            _input = GetComponent<PlayerInput>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (IsSpawned && IsOwner && _input.Current.FirePressed) TryFire();
        }

        // Owner only. Aim at whatever is under the crosshair, but launch from the mouth.
        public bool TryFire()
        {
            if (!CanFire(_nextFireTime)) return false;
            _nextFireTime = Time.time + _cooldown;

            Transform cam = _rig.Camera.transform;
            Vector3 target = PhysicsUtil.CastIgnoring(transform, cam.position, 0f, cam.forward, _maxAimDistance,
                out RaycastHit hit, QueryTriggerInteraction.Collide)
                ? hit.point
                : cam.position + cam.forward * _maxAimDistance;
            FireRpc(_mouth.position, (target - _mouth.position).normalized, cam.position);
            return true;
        }

        private bool CanFire(float nextFireTime) =>
            _planting.CanShoot && !_health.IsDead && Time.time >= nextFireTime;

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector3 origin, Vector3 direction, Vector3 sweepFrom)
        {
            if (!CanFire(_hostNextFireTime)) return;
            if (direction.sqrMagnitude < 0.5f) return;
            if ((origin - transform.position).sqrMagnitude > MaxOriginDistance * MaxOriginDistance) return;
            if ((sweepFrom - transform.position).sqrMagnitude > MaxOriginDistance * MaxOriginDistance) return;
            _hostNextFireTime = Time.time + _cooldown * HostCooldownTolerance;

            PeaProjectile pea = Instantiate(_peaPrefab, origin, Quaternion.LookRotation(direction.normalized));
            pea.NetworkObject.Spawn(destroyWithScene: true);
            pea.Launch(transform, sweepFrom);
            PlayFireSfxRpc(origin);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayFireSfxRpc(Vector3 position)
        {
            if (_fireSfx) AudioSource.PlayClipAtPoint(_fireSfx, position);
        }
    }
}
```

- [ ] **Step 3: Compile**

Run **Compile**. Expected: no output.

- [ ] **Step 4: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Combat/PeaProjectile.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerShooting.cs
git commit -m "feat: fire via server RPC, host spawns and moves networked peas"
```

---

### Task 5: PlayerNetSync and SpawnPoint

**Files:**
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/SpawnPoint.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs`

**Interfaces:**
- Consumes: `PlayerInput.SetLocal`, `PlayerCameraRig.SetLocal/ApplyRemote/HeadInput/Pitch`, `PlantingController.State/ApplyRemote/ResetState`, `PlayerMovement.Teleport`, `PlayerHealth.Revived`.
- Produces: `SpawnPoint.Get(int index) → SpawnPoint` (null if missing); `PlayerNetSync : NetworkBehaviour` with serialized `_tinted` (Renderer[]), `_hostColor`, `_clientColor`.

- [ ] **Step 1: Create SpawnPoint**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Net/SpawnPoint.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PeaHeads.Net
{
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> All = new();

        [SerializeField] private int _index;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static SpawnPoint Get(int index) => All.Find(point => point._index == index);
    }
}
```

- [ ] **Step 2: Create PlayerNetSync**

`PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs`:
```csharp
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads.Net
{
    // Owner writes head lean, pitch and plant state; other machines apply them to their copy of this plant.
    [RequireComponent(typeof(PlayerInput), typeof(PlayerCameraRig), typeof(PlantingController))]
    public class PlayerNetSync : NetworkBehaviour
    {
        [SerializeField] private Renderer[] _tinted;
        [SerializeField] private Color _hostColor = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _clientColor = new(0.95f, 0.6f, 0.2f);

        private readonly NetworkVariable<Vector2> _head = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> _pitch = new(writePerm: NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> _plantState = new(writePerm: NetworkVariableWritePermission.Owner);

        private PlayerInput _input;
        private PlayerCameraRig _rig;
        private PlantingController _planting;
        private PlayerMovement _movement;
        private PlayerHealth _health;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _rig = GetComponent<PlayerCameraRig>();
            _planting = GetComponent<PlantingController>();
            _movement = GetComponent<PlayerMovement>();
            _health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            _input.SetLocal(IsOwner);
            _rig.SetLocal(IsOwner);

            Color color = OwnerClientId == NetworkManager.ServerClientId ? _hostColor : _clientColor;
            foreach (Renderer r in _tinted) r.material.color = color;

            _health.Revived += OnRevived;
            if (IsOwner) MoveToSpawn();
            else ApplyRemoteState();
        }

        public override void OnNetworkDespawn() => _health.Revived -= OnRevived;

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                _head.Value = _rig.HeadInput;
                _pitch.Value = _rig.Pitch;
                _plantState.Value = (byte)_planting.State;
            }
            else ApplyRemoteState();
        }

        private void ApplyRemoteState()
        {
            _rig.ApplyRemote(_head.Value, _pitch.Value);
            _planting.ApplyRemote((PlantState)_plantState.Value);
        }

        private void OnRevived()
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn();
        }

        // Host always spawn 0, any client spawn 1 (a rejoining client gets a new id, so ids can't be used).
        private void MoveToSpawn()
        {
            SpawnPoint spawn = SpawnPoint.Get(IsHost ? 0 : 1);
            if (spawn) _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
    }
}
```

- [ ] **Step 3: Compile**

Run **Compile**. Expected: no output. Run **EditMode tests**: `passed="7" failed="0"`.

- [ ] **Step 4: Commit**

```bash
git add PeaHeadsApp/Assets/PeaHeads/Scripts/Net/SpawnPoint.cs* PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs*
git commit -m "feat: sync head pose and plant state, spawn points"
```

---

### Task 6: Rewire prefabs and DemoArena (one-off editor script)

**Files:**
- Create → run → delete: `PeaHeadsApp/Assets/PeaHeads/Editor/MultiplayerDemoSetup.cs`
- Modified by the script: `Prefabs/Player.prefab`, `Prefabs/Pea.prefab`, `Prefabs/TargetDummy.prefab`, `Scenes/DemoArena.unity`, `PeaHeadsApp/Assets/DefaultNetworkPrefabs.asset`

**Interfaces:**
- Consumes: all components from Tasks 1–5; Ilan's `PeaHeads.Net.ConnectionUI`.
- Produces: networked prefabs and scene per spec "Prefabs & scene".

- [ ] **Step 1: Write the setup script**

`PeaHeadsApp/Assets/PeaHeads/Editor/MultiplayerDemoSetup.cs`:
```csharp
using System.Linq;
using PeaHeads.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PeaHeads.EditorTools
{
    // One-off: run once headless, verify, then delete this file.
    public static class MultiplayerDemoSetup
    {
        private const string Root = "Assets/PeaHeads";
        private const string PlayerPath = Root + "/Prefabs/Player.prefab";
        private const string PeaPath = Root + "/Prefabs/Pea.prefab";
        private const string DummyPath = Root + "/Prefabs/TargetDummy.prefab";
        private const string ScenePath = Root + "/Scenes/DemoArena.unity";
        private const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        public static void Run()
        {
            SetupPlayer();
            SetupPea();
            using (var dummy = new PrefabUtility.EditPrefabContentsScope(DummyPath))
                Ensure<NetworkObject>(dummy.prefabContentsRoot);
            RegisterPrefabs(PlayerPath, PeaPath, DummyPath);
            SetupScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[PeaHeads] Multiplayer demo setup done");
        }

        private static void SetupPlayer()
        {
            using var scope = new PrefabUtility.EditPrefabContentsScope(PlayerPath);
            GameObject root = scope.prefabContentsRoot;

            Ensure<NetworkObject>(root);
            NetworkTransform nt = Ensure<NetworkTransform>(root);
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            GameObject cam = root.transform.Find("HeadPivot/PlayerCamera").gameObject;
            PlayerHUD hud = root.GetComponent<PlayerHUD>();
            PlayerInput input = Ensure<PlayerInput>(root);
            var inputSo = new SerializedObject(input);
            SetArray(inputSo.FindProperty("_localOnlyObjects"), cam);
            SetArray(inputSo.FindProperty("_localOnlyBehaviours"), hud);
            inputSo.ApplyModifiedPropertiesWithoutUndo();
            input.enabled = false;
            hud.enabled = false;
            cam.SetActive(false);

            Renderer head = root.transform.Find("HeadPivot/Head").GetComponent<Renderer>();
            Renderer snout = root.transform.Find("HeadPivot/Snout").GetComponent<Renderer>();
            PlayerNetSync sync = Ensure<PlayerNetSync>(root);
            var syncSo = new SerializedObject(sync);
            SetArray(syncSo.FindProperty("_tinted"), head, snout);
            syncSo.ApplyModifiedPropertiesWithoutUndo();

            var healthSo = new SerializedObject(root.GetComponent<PlayerHealth>());
            healthSo.FindProperty("_respawnDelay").floatValue = 3f;
            healthSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetupPea()
        {
            using var scope = new PrefabUtility.EditPrefabContentsScope(PeaPath);
            GameObject root = scope.prefabContentsRoot;
            Ensure<NetworkObject>(root);
            NetworkTransform nt = Ensure<NetworkTransform>(root);
            nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        }

        private static void RegisterPrefabs(params string[] paths)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (list.PrefabList.All(p => p.Prefab != prefab)) list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
        }

        private static void SetupScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == "Player") Object.DestroyImmediate(go);
                else if (go.name == "TargetDummy") MoveDummy(go.transform);
            }

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (!nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
                nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            nmGo.AddComponent<ConnectionUI>();

            CreateSpawnPoint(0, new Vector3(0f, 0f, -12f), 0f);
            CreateSpawnPoint(1, new Vector3(0f, 0f, 12f), 180f);

            var overview = new GameObject("OverviewCamera", typeof(Camera), typeof(AudioListener), typeof(OverviewCamera));
            overview.transform.SetPositionAndRotation(new Vector3(0f, 22f, -22f), Quaternion.Euler(45f, 0f, 0f));
            Camera camera = overview.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.8f, 0.95f);

            EditorSceneManager.SaveScene(scene);
        }

        // Clear the spawn-1 line: dummies near (-3,12) and (5,11) move; the one at (-9,3) stays.
        private static void MoveDummy(Transform dummy)
        {
            Vector3 p = dummy.position;
            if (Vector3.Distance(p, new Vector3(-3f, 0f, 12f)) < 0.5f) dummy.position = new Vector3(-6f, 0f, 14f);
            else if (Vector3.Distance(p, new Vector3(5f, 0f, 11f)) < 0.5f) dummy.position = new Vector3(8f, 0f, 10f);
        }

        private static void CreateSpawnPoint(int index, Vector3 position, float yaw)
        {
            var go = new GameObject($"SpawnPoint_{index}");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var so = new SerializedObject(go.AddComponent<SpawnPoint>());
            so.FindProperty("_index").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Ensure<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        private static void SetArray(SerializedProperty property, params Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
```

- [ ] **Step 2: Run it headless**

```bash
pgrep -x Unity && echo "CLOSE THE EDITOR" || "$UNITY" -batchmode -nographics -quit -projectPath "$P" -executeMethod PeaHeads.EditorTools.MultiplayerDemoSetup.Run -logFile $LOGS/setup.log
grep -E "error CS|Exception|\[PeaHeads\]" $LOGS/setup.log | sort -u
```
Expected: only `[PeaHeads] Multiplayer demo setup done`.

- [ ] **Step 3: Verify the YAML**

```bash
A=PeaHeadsApp/Assets
grep -cE "AuthorityMode: 1" $A/PeaHeads/Prefabs/Player.prefab            # expect 1 (Owner)
grep -A3 "_localOnlyObjects" $A/PeaHeads/Prefabs/Player.prefab           # expect 1 non-zero fileID
grep -A3 "_tinted" $A/PeaHeads/Prefabs/Player.prefab                      # expect 2 non-zero fileIDs
grep "_respawnDelay" $A/PeaHeads/Prefabs/Player.prefab                    # expect 3
grep -c "NetworkObject\|GlobalObjectIdHash" $A/PeaHeads/Prefabs/Pea.prefab $A/PeaHeads/Prefabs/TargetDummy.prefab  # expect >0 each
grep -cE "^    Prefab: \{fileID" $A/DefaultNetworkPrefabs.asset          # expect 5 (2 NetTest + 3 ours)
grep -E "m_Name: (NetworkManager|SpawnPoint_0|SpawnPoint_1|OverviewCamera)$" $A/PeaHeads/Scenes/DemoArena.unity  # expect all 4
grep -c "m_Name: Player$" $A/PeaHeads/Scenes/DemoArena.unity              # expect 0
```
If any expectation fails, fix the script and re-run Step 2 before continuing.

- [ ] **Step 4: Delete the setup script and confirm compile**

```bash
rm PeaHeadsApp/Assets/PeaHeads/Editor/MultiplayerDemoSetup.cs PeaHeadsApp/Assets/PeaHeads/Editor/MultiplayerDemoSetup.cs.meta
```
Run **Compile** (expect no output) and **EditMode tests** (expect `passed="7" failed="0"`).

- [ ] **Step 5: Commit**

```bash
git add -A PeaHeadsApp/Assets/PeaHeads/Prefabs PeaHeadsApp/Assets/PeaHeads/Scenes/DemoArena.unity PeaHeadsApp/Assets/DefaultNetworkPrefabs.asset PeaHeadsApp/Assets/PeaHeads/Editor
git status --short   # expect nothing else unexpected (e.g. no NetTest changes)
git commit -m "feat: network the Player/Pea/TargetDummy prefabs and DemoArena scene"
```

---

### Task 7: Review, docs, handoff

**Files:**
- Modify: `CLAUDE.md` (Code layout + Networking model sections)

- [ ] **Step 1: Code review**

Dispatch the **code-reviewer** agent on `git diff 3f8f85c..HEAD -- PeaHeadsApp/Assets/PeaHeads` with the spec path. Fix every CRITICAL/HIGH finding (one commit per fix: `fix: …`), re-run **Compile** and **EditMode tests** after each.

- [ ] **Step 2: Update CLAUDE.md**

In the Code layout block:
- `Player/` line: add `PlayerInput (only device reader, SetLocal, SetTrackerInput)`.
- `Net/` line: replace with `ConnectionUI (Ilan); PlayerNetSync (owner-write head/pitch/plant vars, spawn, tint); SpawnPoint; OverviewCamera; Test/ (NetTest proof)`.
- Add `Tests/EditMode/  PlantingControllerTests (run headless: -runTests -testPlatform EditMode)`.
- Scenes line: `DemoArena (networked: Host alone for solo, Host/Join for 2P), NetTest, ForestArena (todo)`.
- Replace "Head input currently = arrow keys in `PlayerCameraRig`" with "Head input = arrow keys in `PlayerInput`; tracker calls `PlayerInput.SetTrackerInput(head, shoot, aim)`".

In Networking model, replace the first bullet with: "Implemented in `DemoArena`: owner-auth `NetworkTransform` on Player, `PlayerNetSync` vars, `PlayerShooting.FireRpc` → host-spawned `PeaProjectile`, server `PlayerHealth` HP. No match loop yet — players respawn after 3 s."

```bash
git add CLAUDE.md && git commit -m "docs: document networked DemoArena"
```

- [ ] **Step 3: Hand off for manual testing**

Report to the user: what was verified (compile, 7 EditMode tests, YAML checks, review fixes) and that nothing was playtested. Give the manual checklist from the spec (Multiplayer Play Mode first, then two laptops).
