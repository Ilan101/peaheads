# PeaHeads — Claude working notes

36h hackathon, 3 people, Unity 6. Two-player LAN PvP. Webcam head tracking drives pea head + viewpoint, mouth-open fires, wink zooms. Full spec: `PeaHeads_Specification.md` (source of truth for design; this file overrides it where they differ).

**I mostly work with Person 2 (player controller + combat).** Others: Person 1 = CV (Python tracker), Person 3 = networking + arena + match loop.

## Locked decisions

| Area | Choice |
|---|---|
| Engine | Unity **6000.3.9f1**, URP, new Input System (`com.unity.inputsystem` 1.18) |
| Netcode | **Netcode for GameObjects + Unity Transport, direct IP over LAN**. Host-authoritative. Add Unity Relay only if venue Wi-Fi blocks peer connections. |
| CV | **Python + MediaPipe Face Landmarker**, one process per machine, sends JSON over **UDP localhost**. No video leaves the machine. |
| Base project | FPS Microgame template already in `PeaHeadsApp/Assets/FPS/`. **Reuse assets selectively, write our code in `Assets/PeaHeads/`.** Do not extend `PlayerCharacterController`/weapon system. |
| Camera | Third-person over-shoulder, close behind head. Head-tracking = bounded **positional** offset of camera pivot + head bone; mouse = yaw/pitch. Zoom = FOV × 1/1.5. |
| Testing | Hackathon override of global rules: **no 80% coverage / strict TDD.** EditMode tests only for pure logic (planting state machine, gesture edge-detection, packet parsing). Everything else verified by playing. |

## Repo layout

```
peaheads/                      outer repo (git@github.com:Ilan101/peaheads.git) — spec, CLAUDE.md, tracker
├── PeaHeads_Specification.md
├── tracker/                   (to create) Python CV — Person 1
└── PeaHeadsApp/               Unity project (its .gitignore excludes Library/Temp/Logs/UserSettings)
```

Single repo. `github.com/cfaso1/PeaHeadsApp` is obsolete (old nested repo, removed).

Unity editor: `~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity`. Batchmode compile/tests only work when editor is closed (`PeaHeadsApp/Temp/UnityLockfile` present = open). Normally: I write scripts, human wires prefabs/scenes in editor and reports console errors.

## Code layout (Unity)

```
Assets/PeaHeads/
├── PeaHeads.asmdef            refs: Unity.InputSystem, Unity.Netcode.Runtime, Unity.Collections
├── Scripts/
│   ├── Tracking/   FaceTrackingData.cs, WebcamInputReceiver.cs, GestureController.cs
│   ├── Player/     PlayerMovement.cs, PlantingController.cs, HeadMovementController.cs,
│   │               PlayerCameraRig.cs, PlayerShooting.cs, PlayerHealth.cs
│   ├── Combat/     PeaProjectile.cs
│   ├── Net/        MatchManager.cs, ConnectionUI.cs
│   └── UI/         PlayerHUD.cs (health, planted state, tracking dot, crosshair)
├── Prefabs/        Player.prefab, Pea.prefab
├── Scenes/         ForestArena.unity (single scene; menu = overlay canvas)
└── Tests/EditMode/ PeaHeads.Tests.asmdef
```

Only make files when needed. One scene, no MainMenu scene (connect panel overlay is enough). Scene file ownership: **Person 3 owns `ForestArena.unity`**; others work in prefabs or their own test scenes (`Scenes/Sandbox_<name>.unity`) to avoid YAML merge conflicts.

## CV ↔ Unity contract (agree first, don't change silently)

UDP to `127.0.0.1:5065`, one JSON datagram per tracker frame (~30 Hz):

```json
{"t": 1728540000.123, "valid": true, "hx": -0.42, "hy": 0.10, "yaw": -12.0, "pitch": 4.0, "roll": 2.0,
 "mouth": 0.63, "mouthOpen": true, "lEye": false, "rEye": false}
```

- `hx`, `hy`: head displacement vs calibrated neutral, normalized by inter-eye distance, dead-zoned, smoothed, clamped **[-1, 1]**. `+hx` = player's right (un-mirror camera image), `+hy` = up.
- `yaw/pitch/roll`: degrees, optional, for head-model rotation only.
- `mouthOpen`, `lEye`, `rEye`: thresholded with hysteresis in Python. `lEye/rEye` true = closed, already temporally filtered (≥ ~150 ms) so blinks don't pass.
- `valid=false` when face lost. Unity eases offsets back to 0.
- Calibration happens in the Python window (key `c`). Unity also can send `"calibrate"` to `127.0.0.1:5066` (nice-to-have).
- CV never touches gameplay. Unity decides what a gesture means given state.

Unity side: `WebcamInputReceiver` reads on background thread (`UdpClient`), stores latest packet under lock, main thread reads in `Update`. Stale > 0.5 s ⇒ treat as `valid=false`. **Keyboard/mouse fallbacks always on**: arrow keys or IJKL = fake head offset, LMB = fire, RMB/Z = zoom. Needed for dev without webcam and as demo backup.

## Gameplay rules to enforce in code

- States: `Unplanted → Planting (1.0 s) → Planted → Unplanting (1.0 s) → Unplanted`. Key `E`. Defeated is terminal until restart.
- WASD only in `Unplanted`. Shooting only in `Planted`. Unplanting disables fire immediately.
- Head tracking never moves the base/root. It moves head bone + camera pivot offset (max ~0.6 m lateral, ~0.3 m vertical, tune).
- Fire on mouth **closed→open edge** only, plus cooldown 0.6 s. Mouth must close before next shot.
- Zoom on exactly one eye closed; both closed = nothing.
- Pea: spawns at mouth socket, straight line along camera aim (raycast from camera center to get target point, then fire from mouth toward it), speed 10 u/s, diameter 0.4, lifetime 4 s. Cover colliders block it.
- Health 3. Hit colliders on **head and base/stem** (leaning head out = head exposed; base still exposed if out of cover).
- Local camera uses local webcam data immediately; never wait on network.

## Networking model (NGO)

- Host = player 1, client = player 2. Connect panel: Host / Join `<IP>`.
- `NetworkObject` + `NetworkTransform` (owner-authoritative, interpolated) on player root; head pose synced via owner-writable `NetworkVariable<Vector3>` (offset) + aim yaw/pitch, interpolated on remote.
- `PlantState` as owner-writable `NetworkVariable<byte>`.
- Fire: owner calls `FireServerRpc(origin, dir)`; server spawns networked `Pea` (`NetworkObject`, server moves it, `NetworkTransform`). Server does collision + damage.
- `Health` = server-write `NetworkVariable<int>`. Server `MatchManager` ends round at 0, `MatchEndClientRpc(winner)`, restart resets health + respawns at spawn points.
- No lag compensation. No prediction beyond owner-auth movement.
- Fallback if netcode isn't working by hour ~12: single-machine practice mode with stationary dummy pea that fires on a timer. Keep gameplay scripts usable without NetworkManager where cheap (check `IsSpawned`/`NetworkManager.Singleton == null`).

## Tuning

Put tunables as `[SerializeField]` on components (inspector-tweakable), defaults from spec §7. No ScriptableObject config layers.

## Priorities (cut from bottom)

1. Webcam → head + camera offset (left/right first).
2. Move / plant / unplant with 1 s transitions.
3. Mouth-fire peas, collision, damage, health.
4. NGO two-player over LAN (validate in **hour 0–2** with a cube).
5. Arena with trees/rocks (ProBuilder + template art).
6. Win + restart.
7. Wink zoom + crosshair, HUD, tracking indicator, SFX (reuse `Assets/FPS/Audio`).
8. Stem bend, hit/defeat anims, polish.

Feature freeze when 1–6 work. Never trade webcam reliability for content.

## Conventions

- C#: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private` over public fields. One `MonoBehaviour` per file, filename = class.
- Minimal comments. No debug `Debug.Log` spam left in commits (use a single `[SerializeField] bool debug`).
- Don't edit `Assets/FPS/` scripts; copy what you need into `Assets/PeaHeads/`.
- Commits: conventional (`feat:`, `fix:`, ...). Commit `.meta` files with their assets.
- Python tracker: single `tracker/tracker.py` + `requirements.txt` (`mediapipe`, `opencv-python`). Run: `python tracker/tracker.py --cam 0`.
