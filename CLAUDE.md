# PeaHeads — Claude working notes

36h hackathon, 3 people, Unity 6. Two-player LAN PvP. Webcam head tracking drives pea head + viewpoint, mouth-open fires, wink zooms. Full spec: `PeaHeads_Specification.md` (source of truth for design; this file overrides it where they differ).

**I mostly work with Person 2 (player controller + combat).** Person 2 also built `MatchManager` (2026-10-10, with mushroom scoring); Person 3 must be told. Others: Person 1 = CV (Python tracker), Person 3 = networking + arena + match loop.

## Workflow

- Feature or multi-file work: `superpowers:brainstorming` → spec → `superpowers:writing-plans` → implement → **code-reviewer** agent. Get the user's OK on the design before writing code.
- Small single-file fixes: just do them.
- Before claiming done: compile headless (editor closed) and say plainly what was not playtested.

## Locked decisions

| Area | Choice |
|---|---|
| Engine | Unity **6000.3.9f1**, URP, new Input System (`com.unity.inputsystem` 1.18) |
| Netcode | **Netcode for GameObjects + Unity Transport, direct IP over LAN**. Host-authoritative. Add Unity Relay only if venue Wi-Fi blocks peer connections. |
| CV | **Python + MediaPipe Face Mesh** (`head_tracker.py`, repo root), one process per machine, JSON over **UDP `127.0.0.1:5052`**. No video leaves the machine. |
| Base project | Started from FPS Microgame; template stripped. Kept: nature props, SFX, confetti, project-wide `PeaHeads/Input/InputSystem_Actions.inputactions`. All our stuff in `Assets/PeaHeads/`. NGO pinned **2.13.3** (3.x needs Unity 6000.7). |
| Camera | **First person**, camera inside the head (own head/snout render shadows-only). Head input = bounded **positional** offset of `HeadPivot` (camera rides along), swept so it stops at walls; mouse = yaw (body) / pitch (head). Head sinks with body when planted. Zoom = FOV ÷ 1.5. Plain light-blue background, no skybox. |
| Testing | Hackathon override of global rules: **no 80% coverage / strict TDD.** EditMode tests only for pure logic (planting state machine, gesture edge-detection, packet parsing). Everything else verified by playing. |

## Repo layout

```
peaheads/                      repo (git@github.com:Ilan101/peaheads.git)
├── PeaHeads_Specification.md
├── head_tracker.py            Python webcam tracker — Person 1
└── PeaHeadsApp/               Unity project (its .gitignore excludes Library/Temp/Logs/UserSettings)
```

Single repo. `github.com/cfaso1/PeaHeadsApp` is obsolete (old nested repo, removed).

Unity editor: `~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity`. Batchmode compile/tests only work when editor is closed (`PeaHeadsApp/Temp/UnityLockfile` present = open). Normally: I write scripts, human wires prefabs/scenes in editor and reports console errors.

## Code layout (Unity)

```
Assets/PeaHeads/
├── PeaHeads.asmdef             all runtime scripts. Refs: InputSystem, Netcode.Runtime, Networking.Transport, Collections
├── Editor/         PeaHeads.Editor.asmdef; NetTestSceneBuilder (menu PeaHeads > Build Net Test Scene —
│                   regenerates NetTest scene + Prefabs/NetTest/*, overwriting hand edits)
│                   ArenaBuilder (menu PeaHeads > Build Arena — rebuilds DemoArena's Environment root: terrain,
│                   mirrored Cover + SingleCover tables, 10 MushroomSpots, 3 extra SpawnPoints per side, tree ring,
│                   Kenney→palette material remap; overwrites hand edits under Environment — port them into the tables.
│                   Creates root `Match` object and Prefabs/MushroomPickup only if missing)
├── Scripts/
│   ├── PhysicsUtil.cs   CastIgnoring(): closest ray/sphere hit skipping a root (own colliders)
│   ├── Player/     PlayerInput (ONLY device reader → PlayerInputFrame; SetLocal; SetTrackerInput; cursor lock),
│   │               PlantingController (Tick(dt, pressed) state machine, ApplyRemote, ResetState),
│   │               PlayerMovement (CharacterController, owner only, Teleport),
│   │               PlayerCameraRig (first person: yaw/pitch, head offset + wall clamp, zoom; SetLocal, ApplyRemote),
│   │               PlayerShooting (NetworkBehaviour: TryFire() owner → FireRpc → host spawns pea),
│   │               PlayerHealth (NetworkBehaviour: server HP var, flash/SFX/hide on all, 3 s respawn, Died/Revived)
│   ├── Combat/     PeaProjectile (NetworkBehaviour, server-only sweep; Launch() after Spawn sweeps head→mouth first)
│   ├── Match/      MatchManager (in-scene `Match` NetworkObject: scores, kill credit via PlayerHealth.Killed, win,
│   │               auto-restart; TeamOf), MushroomSpawner (active-spot bitmask var, server collect check),
│   │               MushroomSpot (stationary pickup visual: glow pulse + sparkles)
│   ├── UI/         PlayerHUD (IMGUI placeholder: state, HP, crosshair), ScoreHUD (IMGUI score bars + win banner)
│   ├── Tracking/   (todo) WebcamInputReceiver, GestureController
│   └── Net/        ConnectionUI (Ilan; IMGUI host / join-by-IP, port 7777); PlayerNetSync (owner-write head/pitch/
│                   plant vars, random spawn teleport, Respawn(), host/client tint); SpawnPoint (_team 0/1,
│                   GetRandom avoids last point); OverviewCamera; Test/ (NetTest proof)
├── Tests/EditMode/ PlantingControllerTests (headless: Unity -batchmode -runTests -testPlatform EditMode)
├── Art/Arena/      CC0 Kenney Nature Kit FBX (CREDITS.md); Terrain/ and Materials/ are generated
├── Prefabs/        Player, Pea, TargetDummy, VFX/ (hand-edited); MushroomPickup (generated once, then hand-editable);
│                   NetTest/ (generated)
└── Scenes/         DemoArena (networked: Host alone = solo + dummies, Host/Join = 2P), NetTest (LAN test), ForestArena (todo)
```

Player hierarchy: `Player` (CharacterController + scripts, yaw) → `Visual` (LeafBase, Stem; squashed when planted) and `HeadPivot` (offset by head input, pitched by mouse) → `Head` (trigger collider, hittable), `Snout`, `Mouth` (pea spawn), `PlayerCamera`.
Head input = arrow keys in `PlayerInput`; the webcam receiver calls `PlayerInput.SetTrackerInput(head, shoot, aim)` (main thread). Player prefab ships with `PlayerInput`, HUD and `PlayerCamera` disabled; `PlayerNetSync` enables them on the owner only.

Only make files when needed. One scene, no MainMenu scene (connect panel overlay is enough). Scene file ownership: **Person 3 owns `ForestArena.unity`**; others work in prefabs or their own test scenes (`Scenes/Sandbox_<name>.unity`) to avoid YAML merge conflicts.

## CV ↔ Unity contract (as implemented in `head_tracker.py` — change only together with Person 1)

UDP to `127.0.0.1:5052`, one JSON datagram per webcam frame:

```json
{"face": true, "dx": 0.12, "dy": -0.05, "aim": false, "shoot": false}
```

- `dx`, `dy`: smoothed nose displacement from the calibrated neutral, divided by face width. `+dx` = player's right (image is mirrored), `+dy` = up. **Not clamped, no dead zone** — Unity applies dead zone, scale and clamp to [-1, 1].
- `shoot`: true **while** the mouth is open (≥ 2 frames). It's a level, not an event — Unity detects the false→true edge and applies the cooldown.
- `aim`: true while exactly one eye is closed (≥ 3 frames). Unity treats it as zoom held.
- `face: false` → everything zero/false. Unity eases the head back to neutral.
- Calibration: automatic on the first frame with a face; `C` recalibrates (look straight, eyes open, mouth closed); `Q` quits.
- No timestamp: Unity treats no packet for > 0.5 s the same as `face: false`.
- CV never touches gameplay. Unity decides what a gesture means given plant state.

Open items for Person 1: `cv2.VideoCapture(0, cv2.CAP_DSHOW)` uses a Windows-only backend (use the default backend on Linux/macOS); no `requirements.txt` yet.

Unity side (todo): `WebcamInputReceiver` reads on a background thread (`UdpClient`), stores the latest packet under a lock, main thread reads it in `Update`. **Keyboard/mouse fallbacks always on**: arrow keys = head offset, LMB = fire, RMB = zoom. Needed for dev without a webcam and as demo backup.

## Gameplay rules to enforce in code

- States: `Unplanted → Planting (1.0 s) → Planted → Unplanting (1.0 s) → Unplanted`. Key `E`. Defeated respawns after 3 s at a random spawn point on the player's side.
- WASD only in `Unplanted`. Shooting only in `Planted`. Unplanting disables fire immediately.
- Head tracking never moves the base/root. It moves head bone + camera pivot offset (max ~0.6 m lateral, ~0.3 m vertical, tune).
- Fire on mouth **closed→open edge** only, plus cooldown 0.6 s. Mouth must close before next shot.
- Zoom on exactly one eye closed; both closed = nothing.
- Pea: spawns at mouth socket, straight line along camera aim (raycast from camera center to get target point, then fire from mouth toward it), speed 10 u/s, diameter 0.4, lifetime 4 s. Cover colliders block it.
- Points race: kill = 3, mushroom = 1 (finish planting within 1.5 m of a glowing mushroom; 3 of 10 spots active, respawn 5 s), first to 20 wins → banner, input frozen, auto-restart after 5 s. Values on MatchManager / MushroomSpawner.
- Health 3. Hit colliders on **head and base/stem** (leaning head out = head exposed; base still exposed if out of cover).
- Local camera uses local webcam data immediately; never wait on network.

## Networking model (NGO)

- Implemented in `DemoArena`: owner-auth `NetworkTransform` on Player, `PlayerNetSync` vars, `PlayerShooting.FireRpc` → host-spawned `PeaProjectile`, server `PlayerHealth` HP, `MatchManager` points race (design: `docs/superpowers/specs/2026-10-10-mushroom-scoring-design.md`). Players respawn after 3 s. Design: `docs/superpowers/specs/2026-10-10-demo-multiplayer-design.md`.
- Host = player 1, client = player 2. Connect panel: Host / Join `<IP>`.
- `NetworkObject` + `NetworkTransform` (owner-authoritative, interpolated) on player root; head pose synced via owner-writable `NetworkVariable<Vector3>` (offset) + aim yaw/pitch, interpolated on remote.
- `PlantState` as owner-writable `NetworkVariable<byte>`.
- Fire: owner calls a `[Rpc(SendTo.Server)]` fire method (as in `NetTestPlayer.FireRpc`); server spawns networked `Pea` (`NetworkObject`, server moves it, `NetworkTransform`). Server does collision + damage.
- `Health` = server-write `NetworkVariable<int>`. Server `MatchManager` scores kills (`PlayerHealth.Killed`, attacker = pea shooter) and mushrooms, ends at the target score, restarts after a delay (`ResetHealth` + `RoundStartedRpc` → `PlayerNetSync.Respawn`). Input frozen while ended.
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
7. Wink zoom + crosshair, HUD, tracking indicator, SFX (`Assets/PeaHeads/Audio`).
8. Stem bend, hit/defeat anims, polish.

Feature freeze when 1–6 work. Never trade webcam reliability for content.

## Conventions

- C#: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private` over public fields. One `MonoBehaviour` per file, filename = class.
- Minimal comments. No debug `Debug.Log` spam left in commits (use a single `[SerializeField] bool debug`).
- Commits: conventional (`feat:`, `fix:`, ...). Commit `.meta` files with their assets.
- Python tracker: `head_tracker.py` at repo root. Deps: `mediapipe`, `opencv-python`. Run: `python head_tracker.py` (webcam 0).
