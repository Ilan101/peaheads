# Random even teams — design

**Date:** 2026-10-10 · **Owner:** Ilan · **Status:** approved in brainstorming, pending spec review

## Goal

Teams are no longer "host = Bloom, client = Blight". At every match start/restart the host shuffles all connected players into Bloom and Blight evenly (1 v 1 today, balanced for more), so either player can be either faction from round to round.

## Decisions

| Question | Decision |
|---|---|
| Rule | **Random each match**: shuffle connected players, deal alternately Bloom, Blight, Bloom… (balanced, every player assigned). |
| Source of truth | `MatchManager` holds the assignment in synced lists; `MatchManager.TeamOf(ulong clientId)` keeps its signature and reads them. Unknown id → team 0. |
| Spawn side | Host tells each owner which side to respawn on in a targeted RPC at round start (no race with list sync). Revive uses `TeamOf`. |
| Colours | Head/snout/eyelid tint re-applied whenever the assignment changes. Everything else already looks teams up per use. |
| Story | Starts only once the local player appears in the assignment, so it shows the right faction. |

## Current state

- `MatchManager.TeamOf(clientId) => clientId == ServerClientId ? 0 : 1` — used by score/kills (`OnKilled`), confetti, `MushroomSpawner`, `PlayerNetSync` (tint), `PlayerShooting` (pea + splat colour), `GameMenu` (story), `Minimap`, `PlayerHUD`, `ScoreHUD`.
- `Restart()` (server): resets scores/HP, `RoundStartedRpc()` → every client calls its own `PlayerNetSync.Respawn()` → `MoveToSpawn()` picks `SpawnPoint.GetRandom(IsHost ? 0 : 1)`.
- `OnClientConnected` (server): a client joining calls `RestartNow()`.
- `PlayerNetSync.OnNetworkSpawn`: tints `_tinted` once with `Faction.Color(TeamOf(OwnerClientId))`; owner `MoveToSpawn()`.
- `GameMenu`: plays `StoryIntro` with `TeamOf(LocalClientId)` as soon as connected.

## Components

### 1. Team assignment (pure) — `MatchManager.AssignTeams`

`public static int[] AssignTeams(IReadOnlyList<ulong> clientIds, System.Random rng)` → team per input index. Shuffles indices (Fisher–Yates with `rng`), then the k-th shuffled player gets team `k % 2`. Pure, no Unity/NGO state; tested with seeded `System.Random`.

### 2. `MatchManager` changes

- `private readonly NetworkList<ulong> _team0 = new(); _team1 = new();` (server write).
- `public static MatchManager Instance { get; private set; }` set in `OnNetworkSpawn`, cleared in `OnNetworkDespawn` (if it is this).
- `public static int TeamOf(ulong clientId)` → `Instance` lists: in `_team1` → 1, else 0 (also 0 when no instance).
- `public static bool HasTeam(ulong clientId)` → in either list.
- `public static event Action TeamsChanged` — raised on every machine from both lists' `OnListChanged`.
- Server `ShuffleTeams()`: clears both lists, `AssignTeams(ConnectedClientsIds, _rng)`, fills lists. Called in `OnNetworkSpawn` (host alone) and at the start of `Restart()` (covers a client joining, since that restarts).
- `Restart()`: after `ShuffleTeams()` and resets, replace `RoundStartedRpc()` with, for each connected client's `PlayerNetSync`, `sync.RespawnOnSideRpc((byte)TeamOf(clientId))`. `RoundStartedRpc` is removed (it only respawned).
- `OnClientDisconnectCallback` (server): remove the id from both lists (no reshuffle mid-match).

### 3. `PlayerNetSync` changes

- Tint moved into `ApplyTint()`: `Faction.Color(MatchManager.TeamOf(OwnerClientId))` on `_tinted`. Called in `OnNetworkSpawn` and on `MatchManager.TeamsChanged` (subscribe in `OnNetworkSpawn`, unsubscribe in `OnNetworkDespawn`).
- `[Rpc(SendTo.Owner)] public void RespawnOnSideRpc(byte team)` → `Respawn(team)`.
- `Respawn()` (revive) → `Respawn(MatchManager.TeamOf(OwnerClientId))`; `MoveToSpawn(int team)` uses `SpawnPoint.GetRandom(team, _lastSpawn)`. Initial `OnNetworkSpawn` owner placement uses `TeamOf(OwnerClientId)`; the round-start RPC corrects it after a reshuffle.

### 4. `GameMenu` change

- Story starts when `connected && !_storyStarted && MatchManager.HasTeam(nm.LocalClientId)` (instead of only `connected`).

### 5. Unchanged callers

`OnKilled`, confetti, `MushroomSpawner`, `PlayerShooting`, `Minimap`, `PlayerHUD`, `ScoreHUD` keep calling `TeamOf` and pick up the new meaning with no edits.

## Not changing

Match rules, scoring, faction names/colours/story lines, spawn points, menu flow, UI layout.

## Testing

- EditMode `MatchTeamsTests`:
  - 2 players → one team 0, one team 1 (several seeds).
  - 1, 3, 4, 5 players → team sizes differ by at most 1; every index assigned 0 or 1.
  - Over 200 seeds with 2 players, each player is team 0 at least once and team 1 at least once.
  - `TeamOf` with no `MatchManager` instance → 0 (no exception).
- Manual (two machines, DemoArena), across ≥ 4 restarts (Restart Match / timer / score):
  1. Host is sometimes Blight, client sometimes Bloom.
  2. Head + eyelid colours, peas, splats, score bars, minimap, win banner always match the current team.
  3. Each round both players spawn on their own team's side.
  4. Client joining mid-session: story shows the faction it actually got.
  5. Client leaves: host keeps playing; score attribution still correct.
