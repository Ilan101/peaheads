# Mushroom scoring + match loop — design

**Date:** 2026-10-10 · **Owner:** Person 2 (takes over `MatchManager` from Person 3, who must be told) · **Status:** pending spec review

## Goal

Replace "last plant standing" with a points race. Glowing mushrooms appear around DemoArena; a player collects one by **finishing a plant** on it. Each player (host = Player 1, client = Player 2) has a score bar at the top of the screen. Kill = 3 points, mushroom = 1, first to 20 wins. Then a winner banner shows and the match restarts by itself. Every number is a `[SerializeField]` on a component.

## Decisions

| Question | Decision |
|---|---|
| Teams | 1v1: team 0 = host's player, team 1 = the client's player (same rule as spawn points). |
| Spawning | 10 fixed spots (mostly mirrored). 3 active at a time. A collected mushroom reappears at a random free spot after 5 s. |
| Collecting | Server sees the player in the `Planted` state, alive, within 1.5 m of an active mushroom. Planting takes 1 s, so the player is exposed while collecting. |
| Spot choice | Random among spots that are inactive and have no player within the collect radius, so a mushroom never appears under someone already planted. |
| Kill credit | The pea remembers its shooter's client id. A kill scores only when the victim is a *player* on the other team; dummies give nothing. |
| Match loop | Built now, in `MatchManager`. At the target score: state `Ended`, winner shown on both screens, input frozen, confetti. After 5 s: scores reset, everyone is revived and teleported to spawn, mushrooms reset. |
| Respawn points | Each side has 4 spawn points: the existing `SpawnPoint_0` or `SpawnPoint_1` plus 3 more that ArenaBuilder places, mirrored. Every (re)spawn, including the first one and match restarts, picks a random point on the player's own side, never the one they last used. |
| Decoration | Decorative mushrooms are removed from the arena scatter so they can't be confused with pickups. |

## Tunables (defaults)

| Component | Field | Default |
|---|---|---|
| `MatchManager` | `_targetScore` | 20 |
| | `_killPoints` | 3 |
| | `_mushroomPoints` | 1 |
| | `_restartDelay` (s) | 5 |
| `MushroomSpawner` | `_activeCount` | 3 |
| | `_respawnDelay` (s) | 5 |
| | `_collectRadius` (m) | 1.5 |
| `MushroomSpot` | `_glowPulseSpeed` | 3 (mushrooms are stationary; no bob/spin) |
| `ScoreHUD` | `_team0Color`, `_team1Color` | host green `(0.55, 0.85, 0.25)`, client orange `(0.95, 0.6, 0.2)` (same as `PlayerNetSync`) |

All `[SerializeField, Range(...)]`, as in the earlier slider pass.

## Components

### New — `Scripts/Match/`

**`MatchManager`** (NetworkBehaviour, on the in-scene `Match` object)
- Server-written state: `NetworkVariable<int>` score per team, `NetworkVariable<byte>` state (`Playing`, `Ended`), `NetworkVariable<sbyte>` winner (−1 = none).
- `static int TeamOf(ulong clientId)`: 0 for `NetworkManager.ServerClientId`, otherwise 1.
- `void AddPoints(int team, int points)` (server only; ignored unless `Playing`). When a score reaches `_targetScore`: set the winner, state `Ended`, and start the restart timer.
- Subscribes to `PlayerHealth.Killed` (server). If the victim has `PlayerNetSync` and `TeamOf(victim.OwnerClientId) != TeamOf(attacker)`, it calls `AddPoints(TeamOf(attacker), _killPoints)`.
- On every machine, when the state changes: freezes or unfreezes the local player's `PlayerInput`. On `Ended`: plays the end SFX and spawns confetti (optional prefab field) at the winner's player.
- Restart (server): zero the scores, call `ResetHealth()` on every player, raise the C# event `RoundStarted` (the spawner listens), and send `RoundStartedRpc` (SendTo.Everyone), which calls `PlayerNetSync.Respawn()` on the local player. Then state `Playing`.
- Read API for the HUD: `Score(int team)`, `TargetScore`, `IsEnded`, `Winner`.

**`MushroomSpawner`** (NetworkBehaviour, same `Match` object)
- Finds all `MushroomSpot`s at spawn and orders them by their serialized index, so every machine uses the same order.
- `NetworkVariable<int> _activeMask` (bit i = spot i active; at most 32 spots). On every machine, `OnValueChanged` calls `Show` / `Hide` on each spot. A bit going 1 → 0 also calls `PlayCollect()` (burst + chime) at that spot.
- Server `Update`: for each connected player that is alive, `Planted`, and within `_collectRadius` (XZ distance) of an active spot: clear the bit, call `AddPoints(team, _mushroomPoints)`, and queue a respawn at `Time.time + _respawnDelay`. Due respawns pick a random free spot.
- Initial fill and `RoundStarted`: clear everything, then activate `_activeCount` random free spots.

**`MushroomSpot`** (MonoBehaviour, on the `MushroomPickup` prefab)
- `[SerializeField] int _index` (set by ArenaBuilder), plus references to its visual root, point light and sparkle `ParticleSystem`, and a collect `AudioClip`.
- `Show()` / `Hide()` toggle the visual root and light, and play / stop the sparkles. `PlayCollect()` emits a 25-particle burst and plays the clip.
- `Update` (visual only): pulse the light intensity. The mushroom itself is stationary.

### New — `Scripts/UI/ScoreHUD.cs` (MonoBehaviour, on `Match`)
- IMGUI, matching `PlayerHUD`. It draws only while netcode is running.
- Top of the screen: two bars side by side, Player 1 (green, fills from the left) and Player 2 (orange, fills from the right), each labelled `n / target`. The local player's bar is marked "YOU".
- When `IsEnded`: a big centred "PLAYER 1 WINS" plus "You win!" or "You lose" for the local player, and "Restarting…".

### Changed
- **`PlayerHealth`**: `TakeDamage(int amount, ulong attackerClientId)`. A new server-only `public static event Action<PlayerHealth, ulong> Killed` is raised alongside `Died`. The existing `Died` event is kept.
- **`PeaProjectile`**: `Launch` stores the shooter's `OwnerClientId` (from the owner's `NetworkObject`) and passes it to `TakeDamage`.
- **`PlayerInput`**: `public bool Frozen { get; set; }`. While frozen, `Current` stays default (no move, look, plant or fire).
- **`PlayerNetSync`**: `public void Respawn()` (owner): `_planting.ResetState()` plus `MoveToSpawn()`. `OnRevived` calls it. `MoveToSpawn` uses `SpawnPoint.GetRandom(team, _lastSpawn)` and remembers the result.
- **`SpawnPoint`**: `_index` is renamed to `_team` (`[FormerlySerializedAs("_index")]`, so the existing scene values 0/1 carry over). `static SpawnPoint GetRandom(int team, SpawnPoint avoid = null)` returns a random point of that team other than `avoid`, falling back to `avoid` if it's the only one. `Get(int)` is removed.
- **`ArenaBuilder`**:
  - Builds a `Mushroom` table (5 rows, mirrored to 10 spots). Each spot is a `MushroomPickup` prefab instance under `Environment/Mushrooms` with its `_index` set. Spots are excluded from the decoration scatter.
  - Creates `Prefabs/MushroomPickup.prefab` **only if missing**, so hand tweaks survive. It contains `mushroom_redGroup` scaled to about 0.9 m with emissive glow materials (`Art/Arena/Materials/MushroomGlowCap.mat`, `MushroomGlowStem.mat`), a warm point light (range 3), and a sparkle particle system (URP `ParticlesUnlit` material, slow upward drift).
  - Creates the root `Match` object (NetworkObject + MatchManager + MushroomSpawner + ScoreHUD) **only if missing**. It is never deleted, because it's an in-scene NetworkObject outside `Environment`.
  - Removes `mushroom_redGroup` and `mushroom_tanGroup` from `Decoration`.
  - Places 3 extra spawn points per side under `Environment/SpawnPoints` (`ExtraSpawns` table, team 1 mirrored and facing −z). The decoration scatter avoids all spawn points.

## Spot layout

10 explicit spots (as built), mostly mirrored pairs. (−4, 2) replaces the mirror of (7.5, −6.5), which a hand-placed oak covers. Measured clearance from the nearest cover is ≥ 1.4 m.

(−2.5, −4.5), (2.5, 4.5), (7.5, −6.5), (−4, 2), (−10, −8.5), (10, 8.5), (16, 0), (−16, 0), (−15, 6), (15, −6)

## Spawn layout

Team 0 (host) side; team 1 is the mirror (−x, −z), facing the other way. The existing root `SpawnPoint_0` at (0, −12) and `SpawnPoint_1` at (0, 12) stay. Measured clearance from cover is ≥ 1.59 m.

| x | z |
|---|---|
| −8 | −13 |
| 12 | −14 |
| −17.5 | −10.5 |

## Data flow

```
owner: PlantingController.State --(PlayerNetSync _plantState)--> server copy
server: MushroomSpawner.Update -> Planted & near active spot -> _activeMask bit off -> MatchManager.AddPoints
server: PeaProjectile.Impact -> PlayerHealth.TakeDamage(dmg, shooter) -> Killed(victim, shooter) -> MatchManager.AddPoints
MatchManager scores/state (NetworkVariables) -> ScoreHUD on every machine
score >= target -> state Ended -> all: freeze local input, banner, confetti -> server after delay -> RoundStarted -> reset
```

## Edge cases

- **Both players planted on the same mushroom:** the first in the server's loop gets it; the bit is cleared before the second is checked.
- **Points after the end:** `AddPoints` ignores everything while `Ended`, including a pea already in flight.
- **Collecting while dead:** skipped, because the spawner checks `IsDead`.
- **Solo host (no client):** mushrooms and scoring work, so the host can win alone. That's intended, for testing.
- **Client joins mid-match:** NetworkVariables sync scores, state and active mushrooms on join.
- **Builder re-run:** spots are rebuilt under `Environment`. The `Match` object and the `MushroomPickup` prefab are left alone.

## Verification

- Compile headless, run the builder headless, and check the scene: 10 `MushroomSpot`s, 6 extra `SpawnPoint`s, one `Match` with a non-zero `GlobalObjectIdHash`. EditMode tests still pass. Per CLAUDE.md, no new tests: this is NGO and scene wiring, not pure logic.
- Playtest (human):
  - Solo: mushrooms glow and sparkle, and are visible from across the map. Plant on one and the bar goes up by 1. Points can't be scored mid-plant or by planting nearby (outside 1.5 m). A new one appears about 5 s later.
  - Dying a few times respawns you at different points on your own side.
  - 2P: a kill gives +3 to the shooter. Both screens show the same scores. Reaching 20 shows the banner on both screens, freezes input, then everything resets after 5 s.

## Out of scope

More than 2 players or teams, persistent stats, a mushroom spawn animation, minimap markers, Canvas UI.
