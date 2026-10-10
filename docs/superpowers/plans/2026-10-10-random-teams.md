# Random Even Teams Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At every match start the host shuffles connected players evenly into Bloom/Blight; everything team-coloured, spawn sides and the story follow the assignment.

**Architecture:** `MatchManager` owns two synced `NetworkList<ulong>` team lists filled by a pure `AssignTeams` shuffle; the static `TeamOf(clientId)` keeps its signature and reads them. `PlayerNetSync` re-tints on a `TeamsChanged` event and respawns on the side the host sends in a targeted RPC. `GameMenu` delays the story until the local player has a team.

**Tech Stack:** Unity 6000.3.9f1, Netcode for GameObjects 2.13.3 (`NetworkList`, `[Rpc(SendTo.Owner)]`), NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-10-10-random-teams-design.md`

## Global Constraints

- `MatchManager.TeamOf(ulong clientId)` keeps its exact signature; unknown id or no `MatchManager` → 0.
- Rule: shuffle connected players (Fisher–Yates), k-th shuffled player gets team `k % 2`; reshuffle at `OnNetworkSpawn` (server) and at every `Restart()`.
- No reshuffle when a player leaves mid-match (only remove their id).
- `NetworkList`s are allocated in `Awake`, not in field initializers (avoids native-memory leak warnings on the in-scene object).
- Use `System.Random` explicitly (never `UnityEngine.Random`) for the shuffle.
- C# conventions: `PascalCase`, `_camelCase` private fields, minimal comments. Commits conventional, `.meta` with assets, end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Batch-mode Unity needs the editor closed (`PeaHeadsApp/Temp/UnityLockfile` absent).

**Shared command** (Git Bash, repo root): **EditMode tests** — `bash /tmp/run-tests.sh` (if missing, recreate from the peashooter-look plan's shared commands: batchmode `-runTests -testPlatform EditMode`, then grep `error CS|warning CS` and the `result=… total=… passed=… failed=…` summary from `tests.xml`).

## Review Focus

1. Scene without a `MatchManager` (e.g. NetTest) → the story must still start (not wait forever for a team). Task 2 gates on "no MatchManager in scene OR HasTeam"; manual check 4.
2. Client connects → it is first placed by the stale list, then must end on its own side once the round-start RPC arrives. Manual check 3.
3. Static `TeamsChanged` subscribers after a player despawns (leave / disconnect) → no MissingReferenceException. Unsubscribed in `OnNetworkDespawn` (Task 2); manual check 5.
4. Player leaves mid-match → remaining player keeps their team and score attribution; no reshuffle until next round. Task 1 disconnect handler; manual check 5.
5. Restart while the match already ended (Restart Match button) → reshuffle + respawn, no double respawn from the removed `RoundStartedRpc`. Task 2 removes it; manual check 1.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Scripts/Match/MatchManager.cs` | Modify | Team lists, `AssignTeams`, `TeamOf`/`HasTeam`/`TeamsChanged`/`Instance`, shuffle on spawn/restart, per-player respawn RPC calls, disconnect cleanup |
| `Tests/EditMode/MatchTeamsTests.cs` | Create | `AssignTeams` + `TeamOf` fallback tests |
| `Scripts/Net/PlayerNetSync.cs` | Modify | `ApplyTint` on `TeamsChanged`, `RespawnOnSideRpc`, team-based `MoveToSpawn` |
| `Scripts/UI/GameMenu.cs` | Modify | Story waits for the local team |
| `CLAUDE.md` | Modify | Team model docs |

(Paths under `PeaHeadsApp/Assets/PeaHeads/`.)

---

### Task 1: Team assignment in MatchManager

**Files:**
- Modify: `Scripts/Match/MatchManager.cs`
- Test: `Tests/EditMode/MatchTeamsTests.cs`

**Interfaces:**
- Produces: `public static int[] AssignTeams(IReadOnlyList<ulong> clientIds, System.Random rng)`; `public static int TeamOf(ulong clientId)`; `public static bool HasTeam(ulong clientId)`; `public static MatchManager Instance { get; private set; }`; `public static event Action TeamsChanged`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/MatchTeamsTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class MatchTeamsTests
    {
        private static ulong[] Ids(int count) => Enumerable.Range(0, count).Select(i => (ulong)i).ToArray();

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void AssignTeams_IsBalancedAndComplete(int players)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                int[] teams = MatchManager.AssignTeams(Ids(players), new System.Random(seed));
                Assert.AreEqual(players, teams.Length);
                Assert.IsTrue(teams.All(t => t == 0 || t == 1), $"seed {seed}");
                int bloom = teams.Count(t => t == 0);
                Assert.LessOrEqual(System.Math.Abs(bloom - (players - bloom)), 1, $"seed {seed}");
            }
        }

        [Test]
        public void AssignTeams_TwoPlayers_OneOnEachTeam()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                int[] teams = MatchManager.AssignTeams(Ids(2), new System.Random(seed));
                Assert.AreNotEqual(teams[0], teams[1], $"seed {seed}");
            }
        }

        [Test]
        public void AssignTeams_TwoPlayers_EitherCanBeBloom()
        {
            bool firstWasBloom = false, firstWasBlight = false;
            for (int seed = 0; seed < 200; seed++)
            {
                int team = MatchManager.AssignTeams(Ids(2), new System.Random(seed))[0];
                firstWasBloom |= team == 0;
                firstWasBlight |= team == 1;
            }
            Assert.IsTrue(firstWasBloom && firstWasBlight);
        }

        [Test]
        public void TeamOf_WithoutMatch_IsBloom()
        {
            Assert.AreEqual(0, MatchManager.TeamOf(5));
            Assert.IsFalse(MatchManager.HasTeam(5));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: EditMode tests command.
Expected: compile errors `'MatchManager' does not contain a definition for 'AssignTeams'` / `'HasTeam'`.

- [ ] **Step 3: Implement**

In `Scripts/Match/MatchManager.cs`:

Add `using System.Collections.Generic;` after `using System;`.

After the `_state` field add:

```csharp
        // Client ids per team, shuffled by the host at every match start. Allocated in Awake (native memory).
        private NetworkList<ulong> _team0;
        private NetworkList<ulong> _team1;
        private readonly System.Random _rng = new();

        public static MatchManager Instance { get; private set; }
        // Raised on every machine when the team lists change.
        public static event Action TeamsChanged;
```

Replace

```csharp
        public static int TeamOf(ulong clientId) => clientId == NetworkManager.ServerClientId ? 0 : 1;
```

with

```csharp
        // 0 = Bloom, 1 = Blight. Unknown ids (and scenes without a match) count as Bloom.
        public static int TeamOf(ulong clientId) => Instance && Instance._team1.Contains(clientId) ? 1 : 0;

        public static bool HasTeam(ulong clientId) =>
            Instance && (Instance._team0.Contains(clientId) || Instance._team1.Contains(clientId));

        // Shuffle, then deal alternately: team sizes differ by at most one and nobody is tied to a team.
        public static int[] AssignTeams(IReadOnlyList<ulong> clientIds, System.Random rng)
        {
            int count = clientIds.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var teams = new int[count];
            for (int k = 0; k < count; k++) teams[order[k]] = k % 2;
            return teams;
        }

        private void Awake()
        {
            _team0 = new NetworkList<ulong>();
            _team1 = new NetworkList<ulong>();
        }
```

In `OnNetworkSpawn`, replace its first two lines (`_state.OnValueChanged += OnStateChanged;` and `if (!IsServer) return;`) with

```csharp
            Instance = this;
            _team0.OnListChanged += OnTeamsChanged;
            _team1.OnListChanged += OnTeamsChanged;
            _state.OnValueChanged += OnStateChanged;
            if (!IsServer) return;
            ShuffleTeams();
```

(the rest of the server block unchanged), and after `NetworkManager.OnClientConnectedCallback += OnClientConnected;` add `NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;`.

In `OnNetworkDespawn` add at the top

```csharp
            if (Instance == this) Instance = null;
            _team0.OnListChanged -= OnTeamsChanged;
            _team1.OnListChanged -= OnTeamsChanged;
```

and change `if (NetworkManager) NetworkManager.OnClientConnectedCallback -= OnClientConnected;` to

```csharp
            if (NetworkManager)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
```

Add these methods after `OnClientConnected`:

```csharp
        // A leaver's slot is dropped; the remaining players keep their teams until the next round.
        private void OnClientDisconnected(ulong clientId)
        {
            _team0.Remove(clientId);
            _team1.Remove(clientId);
        }

        private void ShuffleTeams()
        {
            IReadOnlyList<ulong> ids = NetworkManager.ConnectedClientsIds;
            int[] teams = AssignTeams(ids, _rng);
            _team0.Clear();
            _team1.Clear();
            for (int i = 0; i < ids.Count; i++) (teams[i] == 0 ? _team0 : _team1).Add(ids[i]);
        }

        private void OnTeamsChanged(NetworkListEvent<ulong> _) => TeamsChanged?.Invoke();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: EditMode tests command.
Expected: no `error CS`/`warning CS` for PeaHeads; `result="Passed"`, `failed="0"`, 9 more tests than before (5 cases + 3 + 1).

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads
git add $S/Scripts/Match/MatchManager.cs $S/Tests/EditMode/MatchTeamsTests.cs $S/Tests/EditMode/MatchTeamsTests.cs.meta
git commit -m "feat: host shuffles players into even teams each match" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Respawn sides, re-tint, story gate

**Files:**
- Modify: `Scripts/Match/MatchManager.cs`, `Scripts/Net/PlayerNetSync.cs`, `Scripts/UI/GameMenu.cs`

**Interfaces:**
- Consumes: `MatchManager.TeamOf`, `HasTeam`, `TeamsChanged`, `ShuffleTeams` (Task 1).
- Produces: `PlayerNetSync.RespawnOnSideRpc(byte team)` (`[Rpc(SendTo.Owner)]`).

No new EditMode test: NGO RPC / scene code, verified by compile and the manual checks in Task 3.

- [ ] **Step 1: `PlayerNetSync` — tint on change, respawn on a given side**

In `Scripts/Net/PlayerNetSync.cs`, in `OnNetworkSpawn` replace

```csharp
            Color color = Faction.Color(MatchManager.TeamOf(OwnerClientId));
            foreach (Renderer r in _tinted) r.material.color = color;

            _health.Revived += OnRevived;
            if (IsOwner) MoveToSpawn();
```

with

```csharp
            ApplyTint();
            MatchManager.TeamsChanged += ApplyTint;

            _health.Revived += OnRevived;
            if (IsOwner) MoveToSpawn(MatchManager.TeamOf(OwnerClientId));
```

Replace `public override void OnNetworkDespawn() => _health.Revived -= OnRevived;` with

```csharp
        public override void OnNetworkDespawn()
        {
            _health.Revived -= OnRevived;
            MatchManager.TeamsChanged -= ApplyTint;
        }

        // Teams are reshuffled every match, so the head colour follows the current assignment.
        private void ApplyTint()
        {
            Color color = Faction.Color(MatchManager.TeamOf(OwnerClientId));
            foreach (Renderer r in _tinted) r.material.color = color;
        }
```

Replace the `Respawn()` method and the `MoveToSpawn()` method (with its comment) by

```csharp
        // Owner only: back to spawn on its team's side, unplanted. Used on revive.
        public void Respawn() => Respawn(MatchManager.TeamOf(OwnerClientId));

        // Host → owner at round start: the side comes with the call, so a late team-list update can't misplace us.
        [Rpc(SendTo.Owner)]
        public void RespawnOnSideRpc(byte team) => Respawn(team);

        private void Respawn(int team)
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn(team);
        }

        // Random point on the team's side each time, never the previous one.
        private void MoveToSpawn(int team)
        {
            SpawnPoint spawn = SpawnPoint.GetRandom(team, _lastSpawn);
            if (!spawn) return;
            _lastSpawn = spawn;
            _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
```

- [ ] **Step 2: `MatchManager.Restart` — reshuffle and send sides**

In `Restart()`, after `if (!IsSpawned) return;` add `ShuffleTeams();`. Replace the line `RoundStartedRpc();` with

```csharp
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerNetSync sync))
                    sync.RespawnOnSideRpc((byte)TeamOf(client.ClientId));
```

Delete the whole `RoundStartedRpc` method (attribute, signature and body).

- [ ] **Step 3: `GameMenu` — story waits for the local team**

In `Scripts/UI/GameMenu.cs` replace

```csharp
            else if (connected && !_storyStarted)
```

with

```csharp
            // Teams are reshuffled when a client joins; wait for ours so the story tells the right side.
            else if (connected && !_storyStarted &&
                     (!FindFirstObjectByType<MatchManager>() || MatchManager.HasTeam(nm.LocalClientId)))
```

- [ ] **Step 4: Compile + tests**

Run: EditMode tests command.
Expected: no `error CS`/`warning CS` for PeaHeads; same total as after Task 1, all passed. Also `grep -rn "RoundStartedRpc" PeaHeadsApp/Assets` → no output.

- [ ] **Step 5: Commit**

```bash
S=PeaHeadsApp/Assets/PeaHeads/Scripts
git add $S/Match/MatchManager.cs $S/Net/PlayerNetSync.cs $S/UI/GameMenu.cs
git commit -m "feat: respawn on the assigned side, re-tint on team change, story waits for team" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Playtest + CLAUDE.md

- [ ] **Step 1: Playtest (human, two machines, DemoArena), across ≥ 4 rounds (Restart Match / clock / score):**

1. Host is sometimes Blight, client sometimes Bloom; Restart Match always reshuffles.
2. Head + eyelid colours, peas, splats, score bars, minimap, win banner and confetti always match the current team.
3. Each round both players spawn on their own team's side (a brief wrong-side placement on join is corrected within a moment).
4. Client joining: its story shows the faction it actually got. NetTest scene still connects and plays.
5. Client leaves mid-match: host keeps playing, no errors in the Console; rejoin starts a fresh, reshuffled match.

- [ ] **Step 2: CLAUDE.md**

- In the `Match/` entry: `MatchManager` — add `team lists (NetworkList ×2) shuffled each match by AssignTeams; TeamOf/HasTeam/TeamsChanged read them`.
- Wherever CLAUDE.md says host = team 0 / Bloom and client = team 1 / Blight (search `host`, `Bloom`, `team 0`), replace with: teams are random and even each match; team 0 = Bloom, team 1 = Blight.
- `PlayerNetSync` entry: `RespawnOnSideRpc (host sends side at round start); tint follows TeamsChanged`.
- `Tests/EditMode/` line: add `MatchTeamsTests`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document random even teams" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
