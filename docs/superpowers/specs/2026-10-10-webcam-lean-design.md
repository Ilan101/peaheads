# Webcam head lean + stem integration — design

**Date:** 2026-10-10 · **Owner:** Person 2 · **Builds on:** Carlos's `HeadTrackingReceiver` (UDP reader + test scene) and Ilan's `StemBender` / `PlantVisuals` (bendy stem, leaf-base rooting) · **Status:** pending spec review

## Goal

Real head movement from the webcam drives the in-game head: **lean left/right and duck/raise, proportional** to how far the player actually moves. It replaces the arrow keys' fixed full lean whenever the tracker sees a face. How much real movement maps to in-game movement is adjustable, so lean can be exaggerated. Mouth-open fires and a wink zooms (each toggleable). Ilan's stem bends with the lean and his planting animation keeps working, now joined by a dust puff. A small HUD indicator shows whether the webcam is driving the head.

## Update (tracker commit 7f07503)

`head_tracker.py` now sends `dx`/`dy` already scaled to its 16:9 on-screen rectangle (`RANGE_X` = 0.6 face widths, `RANGE_Y` = `RANGE_X` × 9/16) and clamped to −1..1. So `HeadTrackingInput` gains now default to **1** (rectangle edge = full in-game lean; >1 still exaggerates). The HUD tracker view is a 16:9 box (256 px wide by default) in the top-left. HP is shown as hearts in the bottom-left; the controls line is gone.

## How the pieces work today

| Piece | What it does | Gap |
|---|---|---|
| `head_tracker.py` (Person 1) | MediaPipe, one UDP JSON datagram per frame to `127.0.0.1:5052`: `face`, `dx`, `dy` (nose offset from calibrated neutral ÷ face width, smoothed, unclamped; +dx = player's right, +dy = up), `aim`, `shoot`. Auto-calibrates on the first face; **C** recalibrates. | Uses `cv2.CAP_DSHOW`, which is Windows-only, so the webcam won't open on Linux. |
| `HeadTrackingReceiver` (Carlos) | Background-thread `UdpClient`, latest packet under a lock, exposes `Instance.FaceVisible/Dx/Dy/Aim/Shoot` on the main thread. After 0.5 s without packets, everything zeroes. | The file is named `HeadTrackingReciever.cs`, so Unity can't attach it (filename ≠ class). Its debug label sits at (10, 10), on top of the connect panel. It throws if port 5052 is taken (two Unity instances on one machine). It isn't in DemoArena. |
| `PlayerInput.SetTrackerInput(head, shoot, aim)` | Already built for this. While tracker data is fresh, `head` (−1..1) replaces the arrow keys; a shoot false→true edge fires; aim zooms. | Nothing calls it. |
| `PlayerCameraRig` | Moves `HeadPivot` (camera inside) by `Head × _headRange` (0.6 m, 0.3 m), smoothed, swept so it stops at walls. | The range is symmetric up/down and has no sliders. Remote copies multiply the synced −1..1 value by **their own** range, so if tuning differs, the hitbox ends up somewhere other than where the owner sees their head. |
| `StemBender` / `PlantVisuals` (Ilan) | Visual only. The stem is a Bezier capsule chain from the leaf base to `HeadPivot` (with spring lag); the leaf base roots in and pops out using `PlantingController.Progress`. Both read transforms, so they follow any head movement and work on remote copies. | `_plantPuff` is empty. Otherwise nothing is needed: proportional lean bends the stem automatically. |

## Decisions

| Question | Decision |
|---|---|
| Mapping | Linear: `lean = sign(v) × max(0, |v| − deadZone) × gain`, clamped to −1..1 per axis. Then metres = lean × max range. |
| When | Always (walking, planting or planted), same as the arrow keys. |
| Fallback | Keyboard is always on. While the tracker has no fresh face, the arrows lean a fixed amount as today. |
| Neutral | **C** in game makes the current head position the new zero (a Unity-side offset, no Python change). Python's auto-calibration and its **C** still work. |
| Face input | Mouth-open fire and wink zoom are on, each with an inspector toggle. LMB and RMB still work. |
| Head sync | The owner syncs the final **metre offset** (after the wall sweep) instead of the −1..1 value. Every machine then places the head, and the host its hitbox, exactly where the owner sees it, whatever each machine's tuning. |
| Extras | HUD tracking indicator; plant dust puff. Dummies unchanged. |

## Components

### Changed: `Scripts/HeadTracking/HeadTrackingReceiver.cs` (Carlos's, minimal edits)
- **Rename** the file `HeadTrackingReciever.cs` → `HeadTrackingReceiver.cs`, keeping its `.meta` so the GUID is stable.
- Wrap it in `namespace PeaHeads`.
- The debug label only draws when `[SerializeField] private bool _debug` is set (default off).
- `OnEnable`: if binding the port fails (`SocketException`), log one warning and disable the component, so the game falls back to keyboard.
- `OnDestroy` clears `Instance`. Public fields, the test cube and the timeout are unchanged.

### New: `Scripts/HeadTracking/HeadTrackingInput.cs` (MonoBehaviour on the Player prefab, local player only)
- `[DefaultExecutionOrder(-110)]`, so it runs before `PlayerInput` (−100) and the tracker drives the same frame.
- Tunables (sliders):

  | Field | Default | Range |
  |---|---|---|
  | `_deadZone` | 0.03 | 0–0.2 |
  | `_leanGain` | 2.5 | 0.5–10 |
  | `_verticalGain` | 3 | 0.5–10 |
  | `_useMouthFire` | true | |
  | `_useWinkZoom` | true | |

- Each `Update`:
  - If `HeadTrackingReceiver.Instance` is missing or disabled: `State = Off` and return.
  - If `!FaceVisible`: `State = NoFace` and return. With no calls, `PlayerInput`'s tracker data goes stale after 0.5 s and the arrows take over.
  - Otherwise `State = Tracking`, `raw = (Dx, Dy)`. When last frame's `PlayerInput.Current.RecenterPressed` is set, `_neutral = raw`. Then `Head = (Shape(raw.x − _neutral.x, _deadZone, _leanGain), Shape(raw.y − _neutral.y, _deadZone, _verticalGain))`, and call `SetTrackerInput(Head, _useMouthFire && Shoot, _useWinkZoom && Aim)`.
- `public static float Shape(float value, float deadZone, float gain)` is pure, so it's EditMode-tested.
- Read API for the HUD: `State` (`Off`, `NoFace`, `Tracking`) and `Head` (the mapped −1..1 value).
- Enabled only on the owner, via `PlayerInput._localOnlyBehaviours` (same mechanism as the HUD), so remote copies never read this machine's webcam.

### Changed: `PlayerInput`
- `PlayerInputFrame.RecenterPressed`, on the **C** key, true for one frame. Recentering on any input is fine: with no tracker it simply does nothing.

### Changed: `PlayerCameraRig`
- `Vector2 _headRange` is replaced by sliders:

  | Field | Default (m) | Range |
  |---|---|---|
  | `_maxLean` | 0.6 | 0–1.5 |
  | `_maxRise` | 0.2 | 0–1 |
  | `_maxDuck` | 0.4 | 0–1 |

  Vertical offset is `head.y × (head.y > 0 ? _maxRise : _maxDuck)`. The old Vector2 value is dropped; the defaults apply.
- `public Vector3 HeadOffset`: the owner's final local offset from the rest position, after the wall sweep.
- `ApplyRemote(Vector3 headOffset, float pitch)`: remote copies smooth toward rest + offset and **don't sweep again** (the owner already did). Rest still sinks with the remote's synced plant state.
- `HeadInput` is removed (it was only used for sync).

### Changed: `PlayerNetSync`
- `NetworkVariable<Vector2> _head` becomes `NetworkVariable<Vector3> _headOffset` (owner write) ← `_rig.HeadOffset`; remote → `_rig.ApplyRemote(_headOffset.Value, _pitch.Value)`.

### Changed: `PlayerHUD`
- A line under HP: `Webcam: tracking` / `Webcam: no face (keyboard)` / `Webcam: off (keyboard) — run head_tracker.py`.
- While tracking, a 60 × 60 px box with a dot at the current `Head` value, so you can see how far your real lean maps in game.
- The controls hint gains `C recenter`.

### Changed: Player prefab (wired by a temporary editor script, run once headless, then deleted)
- Adds `HeadTrackingInput` to the root and appends it to `PlayerInput._localOnlyBehaviours`.
- Adds child `PlantPuff`: a `ParticleSystem` (burst of 14 brown-green dust particles, 0.6 s, slight outward and upward speed, URP `ParticlesUnlit` material), assigned to `PlantVisuals._plantPuff`. It plays when planting finishes.

### Changed: `ArenaBuilder`
- `EnsureHeadTrackingObject(scene)`: root `HeadTracking` object with `HeadTrackingReceiver`, created **only if missing** (same pattern as `Match`). One per machine, so only the local game binds the port.
- The rebuild also removes the stray duplicate oak at (−6.76, 6.48). It was a root-level copy of one that `SingleCover` already places, and the local edit has moved it under `Environment`.

### Changed: `head_tracker.py` (Person 1's file; one line, tell them)
- Use `cv2.CAP_DSHOW` only on Windows (`sys.platform == "win32"`), otherwise the default backend, so the tracker runs on Linux and macOS. The packet contract is unchanged.

## Data flow

```
webcam → head_tracker.py → UDP :5052 → HeadTrackingReceiver (thread → main) →
HeadTrackingInput (recenter, dead zone, gain) → PlayerInput.SetTrackerInput →
PlayerCameraRig (× max lean/rise/duck, wall sweep) → HeadPivot (+ camera) → StemBender follows
owner: PlayerNetSync._headOffset (metres) → remote/host: HeadPivot = rest + offset → hitbox and stem match
```

## Edge cases

- **No tracker running:** the receiver binds but gets no packets, so `FaceVisible` is false, the state is NoFace, and the keyboard is used. With the receiver missing or disabled, the state is Off.
- **Face lost mid-lean:** the tracker sends `face: false` and Dx/Dy zero; `SetTrackerInput` stops being called, the arrows take over after 0.5 s, and the head eases back via the rig smoothing.
- **Two Unity instances on one PC (Multiplayer Play Mode):** the second can't bind 5052, so that receiver disables itself and that instance uses the keyboard.
- **Match `Ended`:** `PlayerInput.Frozen` already makes `Current` default, so tracker head, fire and zoom stop too. The head eases to rest.
- **Lean into a wall:** the owner's sweep clamps the offset, and remotes receive the clamped offset.
- **Mouth held open:** fires once (edge detection in `PlayerInput`), then the existing cooldown applies.

## Verification

- **EditMode:** a new `HeadTrackingInputTests` for `Shape` (dead zone, sign, gain, no clamp in `Shape`). The existing 3 test files still pass.
- **Headless:**
  - Compile, run the prefab setup script, and run the builder.
  - Checks: the `HeadTracking` root exists (once), the Player prefab has `HeadTrackingInput` in `_localOnlyBehaviours`, `_plantPuff` is assigned, and only one oak sits at (−6.76, 6.48).
- **Playtest (human, webcam):**
  - Run `python head_tracker.py`, then Host.
  - The HUD says "tracking". Leaning a little gives a little in-game lean, leaning more gives more, up to max lean; ducking lowers the camera and stem, standing tall raises it.
  - **C** re-zeroes.
  - Mouth-open fires once per open, and a wink zooms.
  - Close the tracker: the HUD says "off" or "no face" and the arrows work.
  - Two machines: the other player's head and stem lean match what they see, and shots at a leaned-out head hit.
  - Planting finishes with a dust puff.

## Out of scope

Lean rotation (tilting the camera roll with the head), forward/back (face-size) tracking, a Unity → Python recalibrate message, dummy stems, Canvas UI, `requirements.txt` (Person 1).
