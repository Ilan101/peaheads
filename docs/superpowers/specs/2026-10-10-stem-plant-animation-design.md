# Stem bend + plant/unplant animation — design

**Date:** 2026-10-10 · **Owner:** Person 2 (player/combat) · **Status:** approved in brainstorming, pending spec review

## Goal

Make the plant body react to what the player's face does: the stem bends to follow the head lean, and planting/unplanting reads as rooting into / popping out of the ground. Nothing here may move the camera in a way the face or mouse didn't cause — attention stays on head movement.

## Decisions

| Question | Decision |
|---|---|
| Scope | **Stem bend + plant/unplant only.** No head tilt, fire recoil, hit wobble, walk sway or defeat wilt. |
| Technique | Procedural (code-driven), no Animator. Everything derives from values already synced (`HeadPivot` position, plant state), so **no new networking**. |
| Stem | Chain of capsule segments along a curve (not a rebuilt tube mesh). |
| Camera rule | Head/camera only move smoothly. Overshoot/bounce goes only into stem thickness and leaf base, never into `HeightScale`. |

## Current state (Player prefab)

- `Visual` → `LeafBase` (cylinder, pos y 0.08, scale 1.2/0.08/1.2), `Stem` (cylinder, pos y 0.78, scale 0.14/0.65/0.14, y 0.13–1.43). No collider on `Stem`.
- `HeadPivot` at y 1.7 → `Head` (sphere, scale 0.9 → radius 0.45, trigger collider), `Snout`, `Mouth`, `PlayerCamera`.
- Body hits land on the `CharacterController` capsule (height 1.5, radius 0.45). Not changed.
- `PlantingController` scales `Visual` y to `Lerp(1, 0.8, progress)` (linear); `PlayerCameraRig` multiplies the head rest height by `HeightScale`.
- Leaning moves `HeadPivot` up to 0.6 m sideways / 0.3 m vertically; the stem stays put, so the head detaches from it.

## Changes

### 1. `StemBender` (new, `Scripts/Player/StemBender.cs`)

MonoBehaviour on the player root.

Serialized:
- `Transform _sourceStem` — the old `Stem`; its `MeshRenderer.sharedMaterial` is reused, then its GameObject is deactivated (deactivated, not renderer-disabled, because `PlayerHealth.SetAlive` re-enables every child renderer).
- `Transform _baseAnchor` — `LeafBase`; the stem starts at its top centre (`LeafBase.TransformPoint(0, 1, 0)`, cylinder mesh spans y −1..1).
- `Transform _headPivot`; `float _headSink = 0.3f` — stem ends this far below the pivot, inside the head sphere so the joint is hidden.
- `Mesh _segmentMesh` — Unity built-in Capsule.
- `int _segments = 7`, `float _thickness = 0.14f`, `float _overlap = 0.04f` (extra length per segment so joints don't gap when bent).
- Not serialized: rest length, the straight `p0`→`p2` distance measured once in `Awake` (used for squash/stretch).
- `float _volumeKeep = 0.5f` — 0 = constant thickness, 1 = full volume preservation.
- `float _springStiffness = 120f`, `float _springDamping = 14f`, `float _springAmount = 0.5f` (0 = no lag).

Behaviour:
- `Awake`: create `_segments` child GameObjects of the player root (`MeshFilter` + `MeshRenderer`, shadows on, no collider), deactivate `_sourceStem`. Being children of the root, `PlayerHealth.SetAlive` hides/shows them with the rest of the plant.
- `LateUpdate` (runs after `PlayerCameraRig.Update` placed `HeadPivot`):
  - `p0` = base anchor top, `p2` = `_headPivot.position − root.up × _headSink` (pivot position only; pitch rotation ignored).
  - Control point `p1` = `p0 + root.up × (|p2 − p0| × 0.5)` so the stem leaves the leaf vertically, plus spring offset × `_springAmount`.
  - Spring: a damped spring tracks `p2`; offset = `springPos − p2` (lags fast head moves, settles to 0). Visual only.
  - Sample quadratic Bezier at `t = i / _segments`; segment `i` spans point `i`→`i+1`: position = midpoint, rotation = `FromToRotation(up, dir)`, scale y = `(len + _overlap) / 2` (capsule mesh is 2 tall), scale x/z = `_thickness × pow(_restLength / curveLength, _volumeKeep)`.
- Works identically on owner, remote copy and `TargetDummy` (only reads transforms).
- Pure maths (`Bezier`, segment transform) lives in static methods so an EditMode test can call them.

### 2. `PlantingController` (changed)

- **Removed:** `_visual`, `_plantedSquash` fields, and the `_visual.localScale` writes in `Tick` and `ResetState`.
- **Added:** `[SerializeField] float _plantedHeight = 0.8f` (same value, renamed for what it now means: head rest height scale when planted). `public float Progress => _progress` (0 = unplanted, 1 = planted, linear).
- **Changed:** `HeightScale => Mathf.Lerp(1f, _plantedHeight, Mathf.SmoothStep(0f, 1f, _progress))` — same range and duration, eased in/out, no overshoot.
- State machine, `ApplyRemote`, `ResetState` semantics unchanged.

### 3. `PlantVisuals` (new, `Scripts/Player/PlantVisuals.cs`)

MonoBehaviour on the player root, requires `PlantingController`.

Serialized:
- `Transform _leafBase`.
- `Vector2 _plantedScale = (1.35, 0.6)` — multipliers on the leaf base's rest x/z and y scale when fully planted (wider, flatter).
- `AnimationCurve _plantCurve` — progress 0→1 to blend 0→1, default overshoots to ~1.12 at 0.8 then settles to 1 ("thunk").
- `AnimationCurve _unplantCurve` — evaluated at `Progress` while it falls 1→0; maps 1→1 and 0→0, default dips to ~−0.1 at progress 0.15 so the leaf briefly pulls in past rest ("pop").
- Optional `ParticleSystem _plantPuff` — played once when `State` becomes `Planted`.

Behaviour (`LateUpdate`):
- Rest scale captured in `Awake`.
- Curve by direction: `Planting`/`Planted` → `_plantCurve.Evaluate(Progress)`, `Unplanting`/`Unplanted` → `_unplantCurve.Evaluate(Progress)`.
- `leafBase.localScale = Vector3.Scale(rest, LerpUnclamped(one, (px, py, px), blend))`.
- Puff: compare `State` with last frame's; play on transition into `Planted`.

### 4. Player prefab (editor, done by hand)

- Add `StemBender` to `Player`; assign `Stem`, `LeafBase`, `HeadPivot`, built-in Capsule mesh.
- Add `PlantVisuals` to `Player`; assign `LeafBase`.
- `PlantingController`: `_plantedHeight` = 0.8 (the old `_visual` reference disappears).
- Optional: child `PlantPuff` ParticleSystem at the base, assigned to `PlantVisuals`.
- `TargetDummy`: optionally add `StemBender` too (it has the same hierarchy).

### 5. Tests

- `PlantingControllerTests` keep passing (they only use `Tick`, `State`, `HeightScale` at rest).
- New `StemBenderTests` (EditMode): Bezier endpoints equal `p0`/`p2`; first segment's bottom end ≈ `p0`, last segment's top end ≈ `p2` within overlap tolerance.
- Compile headless (editor closed) before calling it done.

## Not changing

`PlayerCameraRig`, `PlayerInput`, `PlayerNetSync`, `PlayerHealth`, `PlayerShooting`, `PeaProjectile`, colliders, hit detection, networking.

## Verification (manual play, DemoArena, Host alone and Host/Join)

1. Lean left/right/up/down with arrow keys: stem stays connected to the head, bends smoothly, no gaps at joints.
2. Fast lean flick: stem lags slightly and settles; head and camera do not wobble.
3. Plant (E): head/camera sink smoothly with no bounce; leaf base spreads with a small overshoot; stem thickens.
4. Unplant: leaf base pops back; stem thins; camera rises smoothly.
5. Remote view (second machine): opponent's stem bends and planting animates the same way.
6. Die and respawn: stem segments hide and reappear; the old stem does not reappear.
7. Look straight down in first person: stem visible below, nothing clips into the camera.
