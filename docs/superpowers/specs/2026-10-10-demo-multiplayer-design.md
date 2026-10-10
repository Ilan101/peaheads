# DemoArena multiplayer — design

**Date:** 2026-10-10 · **Owner:** Person 2 (player/combat) · **Status:** approved in brainstorming, pending spec review

## Goal

Make the first-person demo (`DemoArena`, `Player` prefab) playable by two players over LAN, with full combat: both players see each other move, look, lean, plant, shoot; peas, hits and HP are host-authoritative. Builds on the netcode pattern proven in Ilan's `NetTest` scene.

## Decisions

| Question | Decision |
|---|---|
| Scope | Full combat over the network. **No match loop** (win/restart = Person 3's `MatchManager`); players respawn after 3 s meanwhile. |
| Solo testing | **One networked scene.** `DemoArena` gets NetworkManager + `ConnectionUI`; solo = press Host. No offline mode. |
| Architecture | **Make existing components network-aware** (not wrapper components, not client-side hits). |
| Input | Implement the previously approved `PlayerInput` single-input-source design as part of this work. |
| NetTest | Left untouched (scene, prefabs, `NetTestSceneBuilder`). |

## Components

### New

**`PlayerInput`** (`Scripts/Player/`, MonoBehaviour, `[DefaultExecutionOrder(-100)]`)
- Only class that reads `Keyboard.current` / `Mouse.current`.
- Fills `public PlayerInputFrame Current` each frame:
  - `Vector2 Move` (WASD, −1..1), `Vector2 Look` (mouse delta × sensitivity, degrees), `Vector2 Head` (−1..1),
  - `bool PlantPressed` (E, one frame), `bool FirePressed` (LMB, one frame), `bool ZoomHeld` (RMB).
- `Head` = arrow keys (tracker source added later via `SetTrackerInput(Vector2 head, bool shoot, bool aim)`: head replaces arrows when tracker data is fresh; `shoot` false→true edge ORs into `FirePressed`; `aim` ORs into `ZoomHeld`).
- Owns cursor lock: locks on enable/click, Esc unlocks, **unlocks in `OnDisable`**. The click that re-locks the cursor does not set `FirePressed`.
- `SetLocal(bool isLocal)`: enables/disables itself and a serialized `_localOnly` list (PlayerCamera GameObject — Camera + AudioListener — and `PlayerHUD`). When not local, `Current` stays default (all zero/false).
- Mouse sensitivity moves here from `PlayerCameraRig`.

**`PlayerNetSync`** (`Scripts/Net/`, NetworkBehaviour)
- Owner-write `NetworkVariable`s: `Vector2 HeadOffset` (normalized lean −1..1, before wall clamp), `float Pitch`, `byte PlantState`.
- `OnNetworkSpawn`: `input.SetLocal(IsOwner)`; `rig.SetLocal(IsOwner)`; head tint by role (host green, client orange); if owner → teleport to spawn point (index 0 if `IsHost`, else 1).
- Owner `Update`: writes current values from rig + planting.
- Non-owner `Update`: `rig.ApplyRemote(HeadOffset, Pitch)`, `planting.ApplyRemote((PlantState)PlantState)`. Smoothing stays in the rig.
- Applies initial values on spawn (late join).

**`SpawnPoint`** (`Scripts/Net/`, MonoBehaviour): marker with `Index` (0/1). Static lookup `SpawnPoint.Get(int index)`.

### Changed

**`PlantingController`**
- Reads `input.Current.PlantPressed` instead of the keyboard.
- Logic moves to `public void Tick(float deltaTime, bool plantPressed)`; `Update` calls it (owner only). Remote: `ApplyRemote(PlantState)` sets state; squash/sink progress still animates locally from state.
- `Update` passes `plantPressed = input.PlantPressed && !health.IsDead`, so a dead plant can't toggle. `ResetState()` → Unplanted, progress 0.

**`PlayerMovement`**: reads `input.Current.Move`; disabled when not local; no movement while dead. CharacterController stays enabled on remote (acts as hit collider) but `Move` is never called there. Adds `Teleport(Vector3, Quaternion)` (disable CC → set → enable).

**`PlayerCameraRig`**
- Reads `Look`, `Head`, `ZoomHeld` from input (no device reads, no cursor code).
- Exposes `Vector2 HeadInput` (smoothed, pre-clamp) and `float Pitch`.
- `ApplyRemote(Vector2 head, float pitch)`: sets the smoothing target / pitch for the remote copy; wall clamp + plant sink still applied locally.
- `SetLocal(bool)`: own head/snout → `ShadowsOnly` **only when local**; remote heads render normally.

**`PlayerShooting`** → NetworkBehaviour
- Owner, on `FirePressed`: local pre-check (`CanShoot`, not dead, cooldown) → compute aim (camera ray → target point → direction from mouth) → `FireRpc(Vector3 mouthPos, Vector3 direction, Vector3 headPos)` with `[Rpc(SendTo.Server)]`.
- Host `FireRpc`: re-check `planting.CanShoot` (synced state), not dead, cooldown → instantiate Pea, `Launch(shooterRoot, headPos)`, `Spawn(destroyWithScene: true)`. Fire SFX played via a client RPC (all clients).

**`PeaProjectile`** → NetworkBehaviour
- Prefab gets `NetworkObject` + `NetworkTransform` (position only, server authority, interpolate).
- `Launch` and per-frame sweep run **only on the server** (`IsServer`); existing head→mouth gap sweep kept.
- Impact (server): `PlayerHealth.TakeDamage` → `NetworkObject.Despawn()`. Lifetime expiry → despawn.

**`PlayerHealth`** → NetworkBehaviour
- `NetworkVariable<int> Current` (server write).
- `TakeDamage(int)` server-only; ignored when dead.
- `OnValueChanged` on all clients: decrease → flash red + hit SFX; 0 → hide renderers/colliders (not CharacterController); back to max → show. Initial value applied on spawn.
- Server respawn timer: `_respawnDelay` (dummies 3 s; **players now 3 s**). On reset, the owner of a player teleports it to its spawn point and resets plant state to Unplanted (`PlantingController.ResetState()`).
- Flash fix: original color cached once in `Awake` (no permanent red on overlapping hits).

**`PlayerHUD`**: unchanged logic, reads synced HP; only enabled for the local player (via `_localOnly`).

### Prefabs & scene

- `Player.prefab`: + `NetworkObject`, `NetworkTransform` (Owner authority; sync position XYZ, rotation Y only; interpolate), `PlayerInput`, `PlayerNetSync`. `PlayerHealth._respawnDelay` = 3.
- `Pea.prefab`: + `NetworkObject`, `NetworkTransform` (position only).
- `TargetDummy.prefab`: + `NetworkObject`.
- `DefaultNetworkPrefabs`: + Player, Pea, TargetDummy (NetTest entries kept).
- `DemoArena.unity`:
  - remove placed Player;
  - add `NetworkManager` (`UnityTransport`, PlayerPrefab = Player, prefab list = DefaultNetworkPrefabs) + `ConnectionUI`;
  - add `SpawnPoint` 0 at (0, 0, −12) facing +Z and `SpawnPoint` 1 at (0, 0, 12) facing −Z;
  - move the dummies off the spawn line so spawn 1 is clear: (−9, 0, 3) stays, others to (−6, 0, 14) and (8, 0, 10);
  - add `OverviewCamera` (Camera only, no AudioListener, depth −1, high angle over the arena). Player camera depth 0 draws over it.
- Applied by a **one-off editor script**, run headless, verified, then deleted.

## Data flow

```
Owner:   devices → PlayerInput → Planting / Movement / CameraRig (immediate, local)
                                   └→ PlayerNetSync vars + NetworkTransform (owner writes)
         PlayerShooting → FireRpc(mouth, dir, head) → HOST
Remote:  NetworkTransform (interpolated) + PlayerNetSync → ApplyRemote (lean, pitch, plant state)
Host:    FireRpc → validate → spawn Pea → server sweep → PlayerHealth.TakeDamage → HP var
All:     HP change → flash/SFX/hide/show; host timer → HP reset → owner teleports to spawn
```

- Local camera/lean/movement never wait on the network.
- Owner is trusted for transform, head pose and plant state (LAN, no anti-cheat). Host is authoritative for peas, hits, HP.
- Known window: a shot fired in the first network tick after `Planted` may be rejected by the host if the synced state still reads `Planting`.

## Edge cases

| Case | Handling |
|---|---|
| Teleport with CharacterController | `PlayerMovement.Teleport` disables CC around the move. |
| Rejoining client gets clientId 2 | Spawn index from `IsHost`, not clientId. |
| Dead plant | No move/plant/fire locally; host rejects `FireRpc` from dead shooter. |
| Client disconnects | NGO despawns their player; their in-flight peas continue. |
| Host disconnects | Client player destroyed; overview camera visible; `ConnectionUI` shows disconnect message; cursor unlocked by `PlayerInput.OnDisable`. |
| Late join | NetworkVariables deliver current values; components apply initial state in `OnNetworkSpawn`. |
| Third player joins | Not blocked (spec = 2 players); would share spawn 1. |
| Host hit detection vs client position | Uses host's interpolated copy; no lag compensation. |
| Play without Host/Join | Overview camera + connect panel only. |

## Testing

**Automated**
- Headless compile: zero errors, zero warnings from `Assets/PeaHeads`.
- EditMode tests (`Assets/PeaHeads/Tests/EditMode/`, `PeaHeads.Tests.asmdef`) for `PlantingController.Tick`: press toggles to Planting; 1.0 s → Planted; press during transition ignored; Planted press → Unplanting → Unplanted; `ApplyRemote` sets state; `ResetState` returns to Unplanted. Run headless via `-runTests -testPlatform EditMode`.
- YAML check: prefab/scene serialized references non-empty.

**Manual (user)**
1. Multiplayer Play Mode (Window > Multiplayer Play Mode, enable Player 2): host in editor, join `127.0.0.1`.
2. Two laptops on the same Wi-Fi.

Checklist: see each other move/turn/lean/plant-sink; own camera only; peas visible both sides and blocked by cover; hits flash + reduce HP on both; 0 HP → hidden → back after 3 s at spawn; dummies work when hosting alone; cursor free after disconnect.

**Review**: code-reviewer agent after implementation; fix CRITICAL/HIGH before reporting.

## Out of scope

Match loop / win screen / restart (Person 3), turret dummy, webcam receiver (Person 1 — only the `SetTrackerInput` hook is added), lag compensation, 2-player cap, Canvas HUD, NetTest changes.
