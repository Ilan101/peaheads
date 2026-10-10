# PeaHeads — Claude working notes

36h hackathon (KnightHacks 2026, theme Bloom vs Blight), 3 people, Unity 6. Two-player LAN PvP, teams **BLOOM** (team 0, yellow) and **BLIGHT** (team 1, purple), shuffled evenly by the host at every match start (host is not always Bloom). Webcam head tracking drives pea head + viewpoint, mouth-open fires, wink zooms. Full spec: `PeaHeads_Specification.md` (source of truth for design; this file overrides it where they differ).

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
| CV | **Python + MediaPipe Face Mesh** (`head_tracker.py`, repo root), one process per machine, JSON over **UDP `127.0.0.1:5052`**; mirrored face JPEGs (384×216, every frame, smoothed 16:9 crop around the face) on **UDP `127.0.0.1:5053`** for the killcam. Video stays local **except** the killer's ~3 s killcam clip, sent to the victim after a kill (user decision 2026-10-10; opt out per player with pause-menu "Killcam Face: Off"). |
| Base project | Started from FPS Microgame; template stripped. Kept: nature props, SFX, confetti, project-wide `PeaHeads/Input/InputSystem_Actions.inputactions`. All our stuff in `Assets/PeaHeads/`. NGO pinned **2.13.3** (3.x needs Unity 6000.7). |
| Camera | **First person**, camera inside the head (own head/snout render shadows-only). Head input = bounded **positional** offset of `HeadPivot` (camera rides along), swept so it stops at walls; mouse = yaw (body) / pitch (head). Head sinks with body when planted. Zoom = FOV ÷ 1.5. Webcam lean is proportional (HeadTrackingInput gain). **Head is webcam-only** (no keyboard lean, user decision 2026-10-10: dodging with your real head is the game). Plain light-blue background, no skybox. |
| Testing | Hackathon override of global rules: **no 80% coverage / strict TDD.** EditMode tests only for pure logic (planting state machine, gesture edge-detection, packet parsing). Everything else verified by playing. |

## Repo layout

```
peaheads/                      repo (git@github.com:Ilan101/peaheads.git)
├── PeaHeads_Specification.md
├── head_tracker.py            Python webcam tracker — Person 1
├── tools/make_audio.py        Synthesises all music + SFX (numpy/scipy, deterministic) into Audio/Resources
└── PeaHeadsApp/               Unity project (its .gitignore excludes Library/Temp/Logs/UserSettings)
```

Single repo. `github.com/cfaso1/PeaHeadsApp` is obsolete (old nested repo, removed).

Unity editor: `~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity`. Batchmode compile/tests only work when editor is closed (`PeaHeadsApp/Temp/UnityLockfile` present = open). Normally: I write scripts, human wires prefabs/scenes in editor and reports console errors.

## Code layout (Unity)

```
Assets/PeaHeads/
├── PeaHeads.asmdef             all runtime scripts. Refs: InputSystem, Netcode.Runtime, Networking.Transport, Collections, UnityEngine.UI
├── Editor/         PeaHeads.Editor.asmdef; PlantBodyBuilder (menu PeaHeads > Build Plant Body — Art/Plant meshes/materials +
│                   Gen_* parts in Player/TargetDummy; never hand-edit Gen_*, change constants and rebuild);
│                   NetTestSceneBuilder (menu PeaHeads > Build Net Test Scene —
│                   regenerates NetTest scene + Prefabs/NetTest/*, overwriting hand edits)
│                   ArenaBuilder (menu PeaHeads > Build Arena — rebuilds DemoArena's Environment root: terrain,
│                   mirrored Cover + SingleCover tables, 10 MushroomSpots, 3 extra SpawnPoints per side, tree ring,
│                   Kenney→palette material remap; overwrites hand edits under Environment — port them into the tables.
│                   Creates root `Match` object and Prefabs/MushroomPickup only if missing)
├── Scripts/
│   ├── PhysicsUtil.cs   CastIgnoring(): closest ray/sphere hit skipping a root (own colliders)
│   ├── Player/     PlayerInput (ONLY device reader → PlayerInputFrame; SetLocal; SetTrackerInput; cursor lock),
│   │               PlantingController (Tick(dt, pressed) state machine, ApplyRemote, ResetState, Progress,
│   │                 eased HeightScale = head/camera sink),
│   │               StemBender (visual: capsule chain on a Bezier leaf base → head; replaces Visual/Stem at runtime),
│   │               PlantVisuals (visual: leaf base root-in/pop-out curves, stays grounded; ground leaves spread; optional plant puff),
│   │               PlantMeshes (static lathe/dome/disc/leaf generators, snout + mouth-hole constants),
│   │               PlantFace (+FaceFlags: snout flare + wink eyelid from 2 synced flags; opponent-visible),
│   │               PlayerMovement (CharacterController, owner only, Teleport),
│   │               PlayerCameraRig (first person: yaw/pitch, head offset + wall clamp with max lean/rise/duck sliders, zoom;
│   │                 HeadOffset (metres) synced; SetLocal, ApplyRemote),
│   │               PlayerShooting (NetworkBehaviour: TryFire() owner → FireRpc → host spawns pea),
│   │               PlayerHealth (NetworkBehaviour: server HP var, owner-write Shielded var, flash/SFX/hide on all, 3 s respawn, Died/Revived)
│   ├── Combat/     PeaProjectile (NetworkBehaviour, server-only sweep; Launch() after Spawn sweeps head→mouth first)
│   ├── Match/      AtmosphereController (daylight sun/ambient, generated cloudy skybox that drifts, fog colour + thickness build as the leader nears the win, 30% → full at 90% of the target), MatchManager (in-scene `Match` NetworkObject: scores, kill credit via PlayerHealth.Killed, win,
│   │               auto-restart, fresh match when a client connects; team lists (2× NetworkList) shuffled each match by pure AssignTeams; TeamOf/HasTeam/TeamsChanged read them; per-player RespawnOnSideRpc at round start), Faction (static: team names, colours, story lines; single source for head tint + score bars — Bloom yellow, Blight purple), MushroomSpawner (active-spot bitmask var, server collect check),
│   │               MushroomSpot (stationary pickup visual: glow pulse + sparkles)
│   ├── Killcam/    FaceFeed (UDP 5053 → 5 s ring of timestamped face JPEGs), ViewRecorder (capture camera on the player
│   │                 camera, no HUD/shadows, 20 fps 480×270 RT ring; Collect() includes post-kill frames, AsyncGPUReadback,
│   │                 3 JPEG encodes per frame), KillClip (pure: clip serialise, ≤4 KB chunks,
│   │                 ClipAssembler, frame window; EditMode-tested). Flow: PeaProjectile.Impact → PlayerShooting.HitConfirmRpc
│   │                 (killed, victim id) → KillcamView.OnLocalKill → killer sees only their own face live (−1 s…+1.6 s, slow-mo on the hit) +
│   │                 SendKillcam (view + face, −1.5…+1.5 s) → KillcamChunkRpc → victim sees "THE SHOT" | "THE FACE" full
│   │                 screen over the death screen. Frames decode on demand (one reused texture per panel).
│   ├── Audio/      GameAudio (static: Sfx enum → Resources "Sfx/<name>" clips, variants name_0..n; PlayAt 3D / Play 2D; returns
│   │                 false if missing so inspector clips stay as fallback), MusicPlayer (added by GameMenu: crossfades
│   │                 menu/story/game loops from Resources "Music/", ducks when paused / under end stinger, Muted in PlayerPrefs)
│   ├── UI/         PlayerHUD (uGUI built in code, owner only: webcam panel top-left; bottom-left team badge flower/thorn +
│   │                 plant dial (sprout, ring of progress dots) + status line + hearts; crosshair with cooldown ring + hit
│   │                 marker, red on a kill; round scope view; damage vignette; "SPAT OUT!" death screen; kill toast; sounds;
│   │                 static Scoped hides HUD/ScoreHUD/Minimap while zoomed),
│   │               ScoreHUD (uGUI built in code on Match: scoreboard + clock badge, +N pop-ups, GO!, last-10-s ticks,
│   │                 collect bar, shield icon by the rival bar while they're in the story, end banner),
│   │               UiBuild (shared uGUI helpers + cached UiSprites),
│   │               Minimap (uGUI built in code, added by GameMenu: top-right, whole play area, own side at bottom;
│   │                 terrain/cover image baked once by downward raycasts; icons for you, rival, dummies, Sporelights;
│   │                 nearest Sporelight glows, no key row; M enlarges), UiSprites (procedural icon sprites incl. Flower/Thorn/Sprout/Camera/Shield/Slash),
│   │               StoryIntro (uGUI built in code: 7-slide typewriter cutscene with animated UI drawings, played by
│   │                 GameMenu on connect; ends on "YOU ARE BLOOM/BLIGHT"; static IsPlaying hides the IMGUI HUDs),
│   │               GameMenu (uGUI built in code: start Host/Join, Connecting/Cancel, Esc pause menu —
│   │                 Resume, Restart Match (host, MatchManager.RestartNow), Leave, with a Controls panel on the right; sets PlayerInput.Paused; during the
│   │                 story also PlayerHealth.SetShielded(true) so the player can't be hit)
│   ├── HeadTracking/ HeadTrackingReceiver (Carlos; scene object, UDP 5052 → Instance.FaceVisible/Dx/Dy/Aim/Shoot),
│   │               HeadTrackingInput (local Player: raw input × movement multiplier × per-axis gain → PlayerInput; C recenters)
│   └── Net/        SessionController (Ilan; host / join-by-IP / leave, port 7777, status line; no UI — ex-ConnectionUI, same GUID); PlayerNetSync (owner-write head/pitch/
│                   plant/face-flag vars, random spawn teleport, Respawn(), faction tint of Head+Snout+eyelid, re-applied on TeamsChanged); SpawnPoint (_team 0/1,
│                   GetRandom avoids last point); OverviewCamera; Test/ (NetTest proof)
├── Tests/EditMode/ PlantingControllerTests, StemBenderTests, PlantVisualsTests, HeadTrackingInputTests, GameMenuTests, SessionControllerTests, PlantMeshesTests, PlantFaceTests, FactionTests, MatchTeamsTests, UiSpritesTests (headless: Unity -batchmode -runTests -testPlatform EditMode)
├── Art/Arena/      CC0 Kenney Nature Kit FBX (CREDITS.md); Terrain/ and Materials/ are generated
├── Prefabs/        Player, Pea, TargetDummy, VFX/ (hand-edited); MushroomPickup (generated once, then hand-editable);
│                   NetTest/ (generated)
└── Scenes/         DemoArena (networked: Host/Join = 2P; dummies removed for demo), NetTest (LAN test), ForestArena (todo)
```

Player hierarchy: `Player` (CharacterController + scripts, yaw) → `Visual` (LeafBase = dirt mound, animated by PlantVisuals; Stem, deactivated at runtime and replaced by StemBender segments; Gen_GroundLeaves) and `HeadPivot` (offset by head input, pitched by mouse) → `Head` (trigger collider, hittable, r 0.54), `Snout` (lathe trumpet + Gen_MouthHole), `Mouth` (pea spawn), `PlayerCamera`, Gen_EyeL/EyeR (+Gen_Pupil, Gen_LidR on the right eye), Gen_Collar, Gen_Tuft.
Head input = webcam only; `HeadTrackingInput` calls `PlayerInput.SetTrackerInput(head, shoot, aim)` each frame while a face is tracked. Player prefab ships with `PlayerInput`, HUD and `PlayerCamera` disabled; `PlayerNetSync` enables them on the owner only.

Only make files when needed. One scene, no MainMenu scene (GameMenu start screen overlay is enough). Scene file ownership: **Person 3 owns `ForestArena.unity`**; others work in prefabs or their own test scenes (`Scenes/Sandbox_<name>.unity`) to avoid YAML merge conflicts.

## CV ↔ Unity contract (as implemented in `head_tracker.py` — change only together with Person 1)

UDP to `127.0.0.1:5052`, one JSON datagram per webcam frame:

```json
{"face": true, "dx": 0.12, "dy": -0.05, "aim": false, "shoot": false}
```

- `dx`, `dy`: smoothed nose displacement from the calibrated neutral (pixels), divided by the tracker's 16:9 active box (half-width `BOX_FRAC` = 0.30 of the frame width, frame-relative so camera mode can't shrink it) and **clamped to [-1, 1]** (box edge = 1). Tracker requests MJPG 30 fps (720p YUYV is only ~10 fps). `+dx` = player's right (image is mirrored), `+dy` = up. Unity applies movement multiplier × per-axis gain (`HeadTrackingInput`, no dead zone), then metres (`PlayerCameraRig` max lean/rise/duck).
- `shoot`: true **while** the mouth is open (≥ 2 frames). It's a level, not an event — Unity detects the false→true edge and applies the cooldown.
- `aim`: true while exactly one eye is closed (≥ 3 frames). Unity treats it as zoom held.
- `face: false` → everything zero/false. Unity eases the head back to neutral.
- Calibration: automatic on the first frame with a face; `C` recalibrates (look straight, eyes open, mouth closed); `Q` quits.
- No timestamp: Unity treats no packet for > 0.5 s the same as `face: false`.
- CV never touches gameplay. Unity decides what a gesture means given plant state.
- Face feed (5053): raw JPEG bytes per datagram (< 60 KB), every frame, cropped around the face, before the debug overlay. Unity keeps the last ~5 s.

Open items for Person 1: none. (`requirements.txt` added; `CAP_DSHOW` is now only used on Windows — fixed by Person 2, 2026-10-10.)

Unity side: `HeadTrackingReceiver` (scene object `HeadTracking`, one per machine; background `UdpClient` thread, latest packet under a lock; disables itself if the port is taken) → `HeadTrackingInput` on the local Player (movement multiplier, lean/vertical gain, no dead zone, C recenters, mouth-fire / wink-zoom toggles) → `PlayerInput.SetTrackerInput`. Design: `docs/superpowers/specs/2026-10-10-webcam-lean-design.md`. **No mouse fire/zoom** (LMB only locks the cursor; removed 2026-10-10 for demo mode). **No keyboard head fallback** (removed 2026-10-10 by the user's call); without a face the head eases to neutral.

## Story (StoryIntro)

The Hollow Grove's Sporelights (= the mushrooms) hold the forest's life; the Rot is killing them. One pod drops two PeaHeads: BLOOM drank the light, BLIGHT drank the Rot. Planting = rooting to draw power (why you can only spit while planted); the free neck = head-tracking dodge. Sporelight +1, knockout +3, first to 20 (or most points when the clock runs out) decides the Grove; a draw leaves it in the balance. Text lives in `StoryIntro.BuildSlides` and `Faction`.

## Gameplay rules to enforce in code

- States: `Unplanted → Planting (1.0 s) → Planted → Unplanting (1.0 s) → Unplanted`. Key `Space` (was E). Defeated respawns after 3 s at a random spawn point on the player's side.
- WASD only in `Unplanted`. Shooting only in `Planted`. Unplanting disables fire immediately.
- Head tracking never moves the base/root. It moves head bone + camera pivot offset. Deliberately exaggerated so dodging is easy: max 1.6 m lateral, 0.6 m up / 0.9 m down, no roll (raw position only), movement multiplier 5, smoothing 7 (Player prefab).
- Fire on mouth **closed→open edge** only, plus cooldown 0.6 s. Mouth must close before next shot.
- Zoom on exactly one eye closed; both closed = nothing.
- Pea: spawns at mouth socket, straight line along camera aim (raycast from camera center to get target point, then fire from mouth toward it), speed 10 u/s, diameter 0.4, lifetime 4 s. Cover colliders block it.
- Points race: kill = 2, mushroom = 3 (stay planted within 1.5 m of a glowing mushroom for 2 s, brown progress bar; 3 of 10 spots active, respawn 5 s), first to 20 wins, or most points when the 120 s clock runs out (tie = draw; the clock starts once nobody is watching the intro story) → banner, input frozen, auto-restart after 10 s. Values on MatchManager / MushroomSpawner.
- Health 3. Hit colliders on **head and base/stem** (leaning head out = head exposed; base still exposed if out of cover).
- Local camera uses local webcam data immediately; never wait on network.

## Networking model (NGO)

- Implemented in `DemoArena`: owner-auth `NetworkTransform` on Player, `PlayerNetSync` vars, `PlayerShooting.FireRpc` → host-spawned `PeaProjectile`, server `PlayerHealth` HP, `MatchManager` points race (design: `docs/superpowers/specs/2026-10-10-mushroom-scoring-design.md`). Players respawn after 3 s. Design: `docs/superpowers/specs/2026-10-10-demo-multiplayer-design.md`.
- Host / Join `<IP>` from the GameMenu start screen. Teams are random and even each match (team 0 = Bloom, team 1 = Blight), independent of host/client.
- `NetworkObject` + `NetworkTransform` (owner-authoritative, interpolated) on player root; head pose synced as the owner's final metre offset (`NetworkVariable<Vector3>`, after the wall sweep) + pitch, interpolated on remote, so per-machine lean tuning can't move the hitbox.
- `PlantState` as owner-writable `NetworkVariable<byte>`.
- Fire: owner calls a `[Rpc(SendTo.Server)]` fire method (as in `NetTestPlayer.FireRpc`); server spawns networked `Pea` (`NetworkObject`, server moves it, `NetworkTransform`). Server does collision + damage.
- `Health` = server-write `NetworkVariable<int>`. Server `MatchManager` scores kills (`PlayerHealth.Killed`, attacker = pea shooter) and mushrooms, ends at the target score, restarts after a delay (`ShuffleTeams` + `ResetHealth` + per-player `PlayerNetSync.RespawnOnSideRpc(team)`; revive uses the owner's cached side). Input frozen while ended.
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
7. Wink zoom + crosshair, HUD, tracking indicator, SFX + music (`Assets/PeaHeads/Audio/Resources`, generated by `tools/make_audio.py`; regenerate, don't hand-edit).
8. Stem bend, hit/defeat anims, polish.

Feature freeze when 1–6 work. Never trade webcam reliability for content.

## Conventions

- C#: `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private` over public fields. One `MonoBehaviour` per file, filename = class.
- Minimal comments. No debug `Debug.Log` spam left in commits (use a single `[SerializeField] bool debug`).
- Commits: conventional (`feat:`, `fix:`, ...). Commit `.meta` files with their assets.
- Python tracker: `head_tracker.py` at repo root. Deps in `requirements.txt` (mediapipe pinned 0.10.21 for `mp.solutions`). Setup once: `python3 -m venv .venv && .venv/bin/pip install -r requirements.txt`. Run: `.venv/bin/python head_tracker.py` (webcam 0).
