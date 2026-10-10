# Mushroom Scoring + Match Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Points race in DemoArena, with randomized respawn points. Planting on a glowing mushroom gives 1 point and a kill gives 3. First to 20 wins, then a banner shows and the match restarts automatically.

**Architecture:** One in-scene NetworkObject `Match` holds `MatchManager` (server scores/state, kill credit, restart), `MushroomSpawner` (server-picked active spots as a bitmask NetworkVariable; server-side collect check against synced plant state) and `ScoreHUD` (IMGUI bars + banner). Mushroom spots are non-networked `MushroomSpot` prefab instances that ArenaBuilder places under `Environment`. Kill credit flows `PeaProjectile` → `PlayerHealth.TakeDamage(dmg, attacker)` → static `PlayerHealth.Killed` → `MatchManager`.

**Tech Stack:** Unity 6000.3.9f1, URP 17.3, Netcode for GameObjects 2.13.3, IMGUI, UnityEditor scripting.

Spec: `docs/superpowers/specs/2026-10-10-mushroom-scoring-design.md`.

## Global Constraints

- Team 0 = host's player (`NetworkManager.ServerClientId`), team 1 = the client's player.
- Defaults: target 20, kill 3, mushroom 1, restart delay 5 s, 3 active mushrooms, respawn 5 s, collect radius 1.5 m. All `[SerializeField, Range(...)]`.
- Collect = server sees the player alive, `PlantState.Planted`, XZ distance ≤ radius to an active spot.
- (Re)spawns pick a random `SpawnPoint` of the player's team (index = team), never the last one used.
- Kills score only when the victim has `PlayerNetSync` and is on the other team (dummies give nothing).
- `Match` root object is created by ArenaBuilder **only if missing** and never deleted. `Prefabs/MushroomPickup.prefab` is created **only if missing**.
- Conventions (CLAUDE.md): `_camelCase` private fields, `[SerializeField] private`, one MonoBehaviour per file, minimal comments, no `Debug.Log` spam.
- Testing (CLAUDE.md hackathon override): no new tests (NGO/scene wiring). Verify by headless compile + builder run + scene checks, then human playtest. Existing EditMode tests must still pass.
- Headless Unity only with the editor closed (`PeaHeadsApp/Temp/UnityLockfile` absent).
- Commits: conventional, explicit `git add` paths, end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Shell variables:

```bash
REPO=/home/charlie/Documents/CS_Projects/KHIX/peaheads
APP=$REPO/PeaHeadsApp
PH=$APP/Assets/PeaHeads
UNITY=~/Unity/Hub/Editor/6000.3.9f1/Editor/Unity
LOG=/tmp/claude-1000/-home-charlie-Documents-CS-Projects-KHIX-peaheads/ea11c8e5-c500-4269-ae99-e432253422ec/scratchpad
compile() { test -e $APP/Temp/UnityLockfile && { echo "CLOSE THE EDITOR"; return 1; }
  $UNITY -batchmode -nographics -quit -projectPath $APP -logFile $LOG/compile.log; echo exit=$?
  grep -E "error CS" $LOG/compile.log | head; }
```

---

### Task 1: Player hooks: kill credit, input freeze, randomized respawn

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Combat/PeaProjectile.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs`
- Modify: `PeaHeadsApp/Assets/PeaHeads/Scripts/Net/SpawnPoint.cs`

**Interfaces:**
- Produces: `SpawnPoint.GetRandom(int team, SpawnPoint avoid = null)` (serialized field `_team`, used by Task 4); `PlayerHealth.TakeDamage(int amount, ulong attackerClientId)`; `public static event Action<PlayerHealth, ulong> PlayerHealth.Killed` (server only: victim, attacker client id); `PlayerInput.Frozen` (`bool` get/set); `PlayerNetSync.Respawn()` (owner only).

- [ ] **Step 1: `PlayerHealth`: attacker-aware damage + static `Killed`**

Replace

```csharp
        public event Action<PlayerHealth> Died;
        public event Action Revived;
```

with

```csharp
        public event Action<PlayerHealth> Died;
        public event Action Revived;
        // Server only: victim, attacker client id.
        public static event Action<PlayerHealth, ulong> Killed;
```

and replace

```csharp
        public void TakeDamage(int amount)
        {
            if (!IsServer || IsDead) return;
            _current.Value = Mathf.Max(0, _current.Value - amount);
            if (!IsDead) return;

            Died?.Invoke(this);
```

with

```csharp
        public void TakeDamage(int amount, ulong attackerClientId)
        {
            if (!IsServer || IsDead) return;
            _current.Value = Mathf.Max(0, _current.Value - amount);
            if (!IsDead) return;

            Died?.Invoke(this);
            Killed?.Invoke(this, attackerClientId);
```

- [ ] **Step 2: `PeaProjectile`: remember the shooter**

Replace

```csharp
        private Transform _owner;
        private float _despawnTime;
```

with

```csharp
        private Transform _owner;
        private ulong _attacker;
        private float _despawnTime;
```

In `Launch`, replace

```csharp
            _owner = owner;
            _despawnTime = Time.time + _lifetime;
```

with

```csharp
            _owner = owner;
            _attacker = owner.GetComponent<NetworkObject>().OwnerClientId;
            _despawnTime = Time.time + _lifetime;
```

In `Impact`, replace `if (health) health.TakeDamage(_damage);` with `if (health) health.TakeDamage(_damage, _attacker);`.

- [ ] **Step 3: `PlayerInput`: `Frozen`**

After `public PlayerInputFrame Current { get; private set; }` add:

```csharp
        // Set by MatchManager while a match has ended: input reads as nothing.
        public bool Frozen { get; set; }
```

In `Update`, after `bool trackerFresh = Time.time - _trackerTime < _trackerTimeout;` add:

```csharp
            if (Frozen)
            {
                Current = default;
                _trackerFirePending = false;
                return;
            }
```

- [ ] **Step 4: `PlayerNetSync`: public `Respawn`**

Replace

```csharp
        private void OnRevived()
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn();
        }
```

with

```csharp
        // Owner only: back to spawn, unplanted. Used on revive and on match restart.
        public void Respawn()
        {
            if (!IsOwner) return;
            _planting.ResetState();
            MoveToSpawn();
        }

        private void OnRevived() => Respawn();
```

- [ ] **Step 5: `SpawnPoint`: team + random pick**

Replace the whole file `Scripts/Net/SpawnPoint.cs` with:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PeaHeads.Net
{
    // Team 0 = host, 1 = client. Players (re)spawn at a random point of their team.
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> All = new();

        [FormerlySerializedAs("_index")]
        [SerializeField, Range(0, 1)] private int _team;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        // Never returns `avoid` unless it is the team's only point.
        public static SpawnPoint GetRandom(int team, SpawnPoint avoid = null)
        {
            List<SpawnPoint> candidates = All.FindAll(point => point._team == team && point != avoid);
            if (candidates.Count == 0) candidates = All.FindAll(point => point._team == team);
            return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
        }
    }
}
```

- [ ] **Step 6: `PlayerNetSync.MoveToSpawn` uses it**

Add the field after `private PlayerHealth _health;`:

```csharp
        private SpawnPoint _lastSpawn;
```

Replace

```csharp
        // Host always spawn 0, any client spawn 1 (a rejoining client gets a new id, so ids can't be used).
        private void MoveToSpawn()
        {
            SpawnPoint spawn = SpawnPoint.Get(IsHost ? 0 : 1);
            if (spawn) _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
```

with

```csharp
        // Host spawns on team 0's side, any client on team 1's (a rejoining client gets a new id, so ids can't be
        // used). Random point each time, never the previous one.
        private void MoveToSpawn()
        {
            SpawnPoint spawn = SpawnPoint.GetRandom(IsHost ? 0 : 1, _lastSpawn);
            if (!spawn) return;
            _lastSpawn = spawn;
            _movement.Teleport(spawn.transform.position, spawn.transform.rotation);
        }
```

- [ ] **Step 7: Compile**

Run: `compile`
Expected: `exit=0`, no `error CS`. Also `grep -rn "SpawnPoint.Get(" $PH` → no hits.

- [ ] **Step 8: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerHealth.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Combat/PeaProjectile.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Player/PlayerInput.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Net/PlayerNetSync.cs PeaHeadsApp/Assets/PeaHeads/Scripts/Net/SpawnPoint.cs
git commit -m "feat: kill attribution, input freeze, random team spawn points for the match loop

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: MatchManager + ScoreHUD

**Files:**
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Match/MatchManager.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/UI/ScoreHUD.cs`

**Interfaces:**
- Consumes: Task 1 `PlayerHealth.Killed`, `PlayerHealth.ResetHealth()`, `PlayerInput.Frozen`, `PlayerNetSync.Respawn()`.
- Produces: `MatchManager.TeamOf(ulong) : int` (static); `AddPoints(int team, int points)` (server); `event Action RoundStarted` (server); `int Score(int team)`, `int TargetScore`, `int MushroomPoints`, `bool IsEnded`, `int Winner` (−1 none).

- [ ] **Step 1: Create `Scripts/Match/MatchManager.cs`**

```csharp
using System;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    public enum MatchState : byte { Playing, Ended }

    // Host keeps score (kills + mushrooms), ends the match at the target score and restarts it after a delay.
    public class MatchManager : NetworkBehaviour
    {
        [SerializeField, Range(1, 100)] private int _targetScore = 20;
        [SerializeField, Range(0, 10)] private int _killPoints = 3;
        [SerializeField, Range(0, 10)] private int _mushroomPoints = 1;
        [SerializeField, Range(1f, 30f)] private float _restartDelay = 5f;
        [SerializeField] private GameObject _confettiPrefab;
        [SerializeField] private AudioClip _endSfx;

        private readonly NetworkVariable<int> _score0 = new();
        private readonly NetworkVariable<int> _score1 = new();
        // Declared before _state so clients already have the winner when the state callback runs.
        private readonly NetworkVariable<sbyte> _winner = new(-1);
        private readonly NetworkVariable<byte> _state = new();

        // Server only.
        public event Action RoundStarted;

        public int TargetScore => _targetScore;
        public int MushroomPoints => _mushroomPoints;
        public bool IsEnded => _state.Value == (byte)MatchState.Ended;
        public int Winner => _winner.Value;
        public int Score(int team) => team == 0 ? _score0.Value : _score1.Value;

        public static int TeamOf(ulong clientId) => clientId == NetworkManager.ServerClientId ? 0 : 1;

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += OnStateChanged;
            if (!IsServer) return;
            // In-scene object: values survive a previous host session, so start clean.
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            _state.Value = (byte)MatchState.Playing;
            PlayerHealth.Killed += OnKilled;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            PlayerHealth.Killed -= OnKilled;
            CancelInvoke();
        }

        public void AddPoints(int team, int points)
        {
            if (!IsServer || IsEnded || points <= 0) return;
            NetworkVariable<int> score = team == 0 ? _score0 : _score1;
            score.Value = Mathf.Min(_targetScore, score.Value + points);
            if (score.Value < _targetScore) return;

            _winner.Value = (sbyte)team;
            _state.Value = (byte)MatchState.Ended;
            Invoke(nameof(Restart), _restartDelay);
        }

        private void OnKilled(PlayerHealth victim, ulong attacker)
        {
            if (!victim.TryGetComponent(out PlayerNetSync _)) return; // dummies don't score
            int team = TeamOf(attacker);
            if (team != TeamOf(victim.OwnerClientId)) AddPoints(team, _killPoints);
        }

        private void Restart()
        {
            if (!IsSpawned) return;
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerHealth health))
                    health.ResetHealth();
            RoundStarted?.Invoke();
            RoundStartedRpc();
            _state.Value = (byte)MatchState.Playing;
        }

        [Rpc(SendTo.Everyone)]
        private void RoundStartedRpc()
        {
            NetworkObject player = NetworkManager.LocalClient?.PlayerObject;
            if (player && player.TryGetComponent(out PlayerNetSync sync)) sync.Respawn();
        }

        private void OnStateChanged(byte previous, byte current)
        {
            bool ended = current == (byte)MatchState.Ended;
            NetworkObject local = NetworkManager.LocalClient?.PlayerObject;
            if (local && local.TryGetComponent(out PlayerInput input)) input.Frozen = ended;
            if (!ended) return;

            if (_endSfx && local) AudioSource.PlayClipAtPoint(_endSfx, local.transform.position);
            if (!_confettiPrefab) return;
            foreach (PlayerNetSync player in FindObjectsByType<PlayerNetSync>(FindObjectsSortMode.None))
                if (TeamOf(player.OwnerClientId) == _winner.Value)
                    Destroy(Instantiate(_confettiPrefab, player.transform.position, Quaternion.identity), _restartDelay);
        }
    }
}
```

- [ ] **Step 2: Create `Scripts/UI/ScoreHUD.cs`**

```csharp
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI score bars + winner banner, like PlayerHUD; swap for a Canvas with the UI pass.
    [RequireComponent(typeof(MatchManager))]
    public class ScoreHUD : MonoBehaviour
    {
        [SerializeField] private Color _team0Color = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _team1Color = new(0.95f, 0.6f, 0.2f);
        [SerializeField, Range(100f, 600f)] private float _barWidth = 320f;

        private MatchManager _match;
        private GUIStyle _label;
        private GUIStyle _banner;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void OnGUI()
        {
            if (!_match.IsSpawned) return;
            _label ??= new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _banner ??= new GUIStyle(_label) { fontSize = 48 };

            int localTeam = MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId);
            float mid = Screen.width / 2f;
            DrawBar(new Rect(mid - _barWidth - 10f, 12f, _barWidth, 26f), 0, false, localTeam);
            DrawBar(new Rect(mid + 10f, 12f, _barWidth, 26f), 1, true, localTeam);

            if (!_match.IsEnded) return;
            string winner = _match.Winner == 0 ? "PLAYER 1" : "PLAYER 2";
            string result = _match.Winner == localTeam ? "You win!" : "You lose";
            float y = Screen.height * 0.3f;
            GUI.Label(new Rect(0f, y, Screen.width, 60f), $"{winner} WINS", _banner);
            GUI.Label(new Rect(0f, y + 64f, Screen.width, 30f), $"{result}   Restarting...", _label);
        }

        private void DrawBar(Rect rect, int team, bool fillFromRight, int localTeam)
        {
            int score = _match.Score(team);
            float fill = Mathf.Clamp01(score / (float)_match.TargetScore);
            float width = rect.width * fill;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = team == 0 ? _team0Color : _team1Color;
            GUI.DrawTexture(new Rect(fillFromRight ? rect.xMax - width : rect.x, rect.y, width, rect.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            string you = team == localTeam ? "  (YOU)" : "";
            GUI.Label(rect, $"P{team + 1}  {score} / {_match.TargetScore}{you}", _label);
        }
    }
}
```

- [ ] **Step 3: Compile**

Run: `compile`
Expected: `exit=0`, no `error CS`.

- [ ] **Step 4: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/Match PeaHeadsApp/Assets/PeaHeads/Scripts/Match.meta PeaHeadsApp/Assets/PeaHeads/Scripts/UI/ScoreHUD.cs PeaHeadsApp/Assets/PeaHeads/Scripts/UI/ScoreHUD.cs.meta
git commit -m "feat: MatchManager scores kills to a target and restarts; ScoreHUD bars and banner

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: MushroomSpot + MushroomSpawner

**Files:**
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Match/MushroomSpot.cs`
- Create: `PeaHeadsApp/Assets/PeaHeads/Scripts/Match/MushroomSpawner.cs`

**Interfaces:**
- Consumes: Task 2 `MatchManager` (`AddPoints`, `MushroomPoints`, `IsEnded`, `RoundStarted`, `TeamOf`); `PlantingController.State`, `PlayerHealth.IsDead`.
- Produces: `MushroomSpot` with serialized fields `_index`, `_visual`, `_glow`, `_sparkles`, `_collectSfx` (Task 4 sets them by name via `SerializedObject`); `int Index`, `Show()`, `Hide()`, `PlayCollect()`.

- [ ] **Step 1: Create `Scripts/Match/MushroomSpot.cs`**

```csharp
using UnityEngine;

namespace PeaHeads
{
    // One mushroom pickup location. Visual only; MushroomSpawner decides when it is shown.
    public class MushroomSpot : MonoBehaviour
    {
        [SerializeField] private int _index;
        [SerializeField] private Transform _visual;
        [SerializeField] private Light _glow;
        [SerializeField] private ParticleSystem _sparkles;
        [SerializeField] private AudioClip _collectSfx;
        [SerializeField, Range(0f, 1f)] private float _bobHeight = 0.15f;
        [SerializeField, Range(0f, 10f)] private float _bobSpeed = 2f;
        [SerializeField, Range(0f, 360f)] private float _spinSpeed = 45f;
        [SerializeField, Range(0f, 10f)] private float _glowPulseSpeed = 3f;

        public int Index => _index;

        private float _glowIntensity;
        private Vector3 _visualRest;

        private void Awake()
        {
            _glowIntensity = _glow ? _glow.intensity : 0f;
            _visualRest = _visual.localPosition;
            Hide();
        }

        public void Show()
        {
            _visual.gameObject.SetActive(true);
            if (_glow) _glow.enabled = true;
            if (_sparkles) _sparkles.Play();
        }

        public void Hide()
        {
            _visual.gameObject.SetActive(false);
            if (_glow) _glow.enabled = false;
            if (_sparkles) _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void PlayCollect()
        {
            if (_sparkles) _sparkles.Emit(25);
            if (_collectSfx) AudioSource.PlayClipAtPoint(_collectSfx, transform.position);
        }

        private void Update()
        {
            if (!_visual.gameObject.activeSelf) return;
            float t = Time.time;
            _visual.localPosition = _visualRest + Vector3.up * ((Mathf.Sin(t * _bobSpeed) * 0.5f + 0.5f) * _bobHeight);
            _visual.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
            if (_glow) _glow.intensity = _glowIntensity * (0.75f + 0.25f * Mathf.Sin(t * _glowPulseSpeed));
        }
    }
}
```

- [ ] **Step 2: Create `Scripts/Match/MushroomSpawner.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host keeps a few mushrooms active at random spots; a player who finishes planting on one scores it.
    [RequireComponent(typeof(MatchManager))]
    public class MushroomSpawner : NetworkBehaviour
    {
        private const int MaxSpots = 32; // one bit each in _activeMask

        [SerializeField, Range(1, 10)] private int _activeCount = 3;
        [SerializeField, Range(0f, 30f)] private float _respawnDelay = 5f;
        [SerializeField, Range(0.5f, 4f)] private float _collectRadius = 1.5f;

        private readonly NetworkVariable<int> _activeMask = new();
        private readonly List<float> _respawnTimes = new();
        private MushroomSpot[] _spots = new MushroomSpot[0];
        private MatchManager _match;

        private void Awake() => _match = GetComponent<MatchManager>();

        public override void OnNetworkSpawn()
        {
            _spots = FindObjectsByType<MushroomSpot>(FindObjectsSortMode.None)
                .OrderBy(s => s.Index).Take(MaxSpots).ToArray();
            _activeMask.OnValueChanged += OnMaskChanged;
            ApplyMask(0, _activeMask.Value, false);
            if (!IsServer) return;
            _match.RoundStarted += ResetField;
            ResetField();
        }

        public override void OnNetworkDespawn()
        {
            _activeMask.OnValueChanged -= OnMaskChanged;
            _match.RoundStarted -= ResetField;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _match.IsEnded) return;
            for (int i = _respawnTimes.Count - 1; i >= 0; i--)
                if (Time.time >= _respawnTimes[i] && TryActivateRandom()) _respawnTimes.RemoveAt(i);
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList) TryCollect(client.PlayerObject);
        }

        private void TryCollect(NetworkObject player)
        {
            if (!player || !player.TryGetComponent(out PlantingController planting)) return;
            if (planting.State != PlantState.Planted) return;
            if (player.TryGetComponent(out PlayerHealth health) && health.IsDead) return;

            for (int i = 0; i < _spots.Length; i++)
            {
                if (!IsActive(i) || !IsNear(i, player.transform.position)) continue;
                _activeMask.Value &= ~(1 << i);
                _respawnTimes.Add(Time.time + _respawnDelay);
                _match.AddPoints(MatchManager.TeamOf(player.OwnerClientId), _match.MushroomPoints);
                return;
            }
        }

        private void ResetField()
        {
            _respawnTimes.Clear();
            _activeMask.Value = 0;
            for (int i = 0; i < _activeCount; i++) TryActivateRandom();
        }

        // Free = inactive and nobody standing on it, so a mushroom never appears under a planted player.
        private bool TryActivateRandom()
        {
            var free = new List<int>();
            for (int i = 0; i < _spots.Length; i++)
                if (!IsActive(i) && !AnyPlayerNear(i)) free.Add(i);
            if (free.Count == 0) return false;
            _activeMask.Value |= 1 << free[Random.Range(0, free.Count)];
            return true;
        }

        private bool AnyPlayerNear(int spot)
        {
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && IsNear(spot, client.PlayerObject.transform.position)) return true;
            return false;
        }

        private bool IsActive(int spot) => (_activeMask.Value & (1 << spot)) != 0;

        private bool IsNear(int spot, Vector3 position)
        {
            Vector3 d = position - _spots[spot].transform.position;
            return d.x * d.x + d.z * d.z <= _collectRadius * _collectRadius;
        }

        private void OnMaskChanged(int previous, int current) => ApplyMask(previous, current, true);

        private void ApplyMask(int previous, int current, bool effects)
        {
            for (int i = 0; i < _spots.Length; i++)
            {
                bool was = (previous & (1 << i)) != 0, now = (current & (1 << i)) != 0;
                if (now && !was) _spots[i].Show();
                if (now || !was) continue;
                _spots[i].Hide();
                if (effects) _spots[i].PlayCollect();
            }
        }
    }
}
```

- [ ] **Step 3: Compile**

Run: `compile`
Expected: `exit=0`, no `error CS`.

- [ ] **Step 4: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Scripts/Match
git commit -m "feat: MushroomSpawner keeps random mushrooms active and scores planted collectors

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: ArenaBuilder: pickup prefab, mushroom spots, spawn points, Match object

**Files:**
- Modify: `PeaHeadsApp/Assets/PeaHeads/Editor/ArenaBuilder.cs`
- Generated by running it: `Prefabs/MushroomPickup.prefab`, `Art/Arena/Materials/MushroomGlowCap.mat`, `MushroomGlowStem.mat`; `Scenes/DemoArena.unity` (spots under `Environment/Mushrooms`, root `Match`)

**Interfaces:**
- Consumes: Task 1 `SpawnPoint` (`_team`, namespace `PeaHeads.Net`); Task 2/3 types `MatchManager`, `MushroomSpawner`, `ScoreHUD`, `MushroomSpot` (serialized field names `_index`, `_visual`, `_glow`, `_sparkles`, `_collectSfx`, `_confettiPrefab`, `_endSfx`); existing builder helpers `Group`, `WorldBounds`, `LoadOrCreate`, `KenneyDir`, `MaterialDir`.

- [ ] **Step 1: Usings**

Add after `using System.Collections.Generic;`:

```csharp
using System.Linq;
using PeaHeads.Net;
using Unity.Netcode;
```

- [ ] **Step 2: Constants + spot table (after the `Decoration` table)**

```csharp
        private const string PickupPrefabPath = "Assets/PeaHeads/Prefabs/MushroomPickup.prefab";
        private const string ParticlesMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
        private const string SfxDir = "Assets/PeaHeads/Audio/SFX/UI/";

        // Half the mushroom spots, mirrored like Cover. Clear of cover footprints (>= 1.5 m), spawns, dummies.
        private static readonly Vector2[] MushroomSpots =
            { new(-2.5f, -4.5f), new(7.5f, -6.5f), new(-10f, -8.5f), new(16f, 0f), new(-15f, 6f) };

        // Extra team-0 spawn points (team 1 mirrored, facing -z). The scene's root SpawnPoint_0/1 stay too.
        private static readonly Vector2[] ExtraSpawns = { new(-8f, -13f), new(12f, -14f), new(-16f, -10f) };
```

- [ ] **Step 3: Remove decorative mushrooms**

Replace

```csharp
            ("plant_bushLarge", 0.9f, 1.4f), ("plant_bushDetailed", 0.9f, 1.4f),
            ("mushroom_redGroup", 0.4f, 0.6f), ("mushroom_tanGroup", 0.4f, 0.6f),
```

with

```csharp
            ("plant_bushLarge", 0.9f, 1.4f), ("plant_bushDetailed", 0.9f, 1.4f),
            // No mushrooms here: they would look like pickups.
```

- [ ] **Step 4: Wire into `Build()`**

Replace

```csharp
            var cover = PlaceCover(env, terrain);
            BuildBoundary(env);
            PlaceTreeRing(env, terrain, rng);
            ScatterDecoration(env, terrain, cover, rng);
            ApplyRenderSettings();
```

with

```csharp
            var blocked = PlaceCover(env, terrain);
            blocked.AddRange(PlaceMushrooms(env, terrain));
            blocked.AddRange(PlaceSpawnPoints(env));
            BuildBoundary(env);
            PlaceTreeRing(env, terrain, rng);
            ScatterDecoration(env, terrain, blocked, rng);
            ApplyRenderSettings();
            EnsureMatchObject(scene);
```

- [ ] **Step 5: Add methods after `PlaceCover`**

```csharp
        private static List<Vector2> PlaceMushrooms(Transform env, Terrain terrain)
        {
            var prefab = EnsurePickupPrefab();
            var parent = Group(env, "Mushrooms");
            var placed = new List<Vector2>();
            foreach (Vector2 half in MushroomSpots)
            foreach (Vector2 xz in new[] { half, -half })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.name = $"Mushroom_{placed.Count}";
                var ground = new Vector3(xz.x, 0f, xz.y);
                ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
                go.transform.position = ground;

                var spot = new SerializedObject(go.GetComponent<MushroomSpot>());
                spot.FindProperty("_index").intValue = placed.Count;
                spot.ApplyModifiedPropertiesWithoutUndo();
                placed.Add(xz);
            }
            return placed;
        }

        // Returns every spawn position (root SpawnPoint_0/1 included) so decoration keeps clear of them.
        private static List<Vector2> PlaceSpawnPoints(Transform env)
        {
            var parent = Group(env, "SpawnPoints");
            var placed = new List<Vector2> { new(0f, -12f), new(0f, 12f) };
            foreach (Vector2 half in ExtraSpawns)
            for (int team = 0; team < 2; team++)
            {
                Vector2 xz = team == 0 ? half : -half;
                var go = new GameObject($"SpawnPoint_T{team}_{placed.Count}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(new Vector3(xz.x, 0f, xz.y),
                    Quaternion.Euler(0f, team == 0 ? 0f : 180f, 0f));
                var point = new SerializedObject(go.AddComponent<SpawnPoint>());
                point.FindProperty("_team").intValue = team;
                point.ApplyModifiedPropertiesWithoutUndo();
                placed.Add(xz);
            }
            return placed;
        }

        // Created once; hand edits to the prefab survive later builds.
        private static GameObject EnsurePickupPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
            if (existing) return existing;

            var root = new GameObject("MushroomPickup");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(KenneyDir + "mushroom_redGroup.fbx"), visual);
            model.transform.localScale *= 0.9f / WorldBounds(model).size.y;
            Bounds bounds = WorldBounds(model);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var cap = GlowMaterial("MushroomGlowCap", new Color(1f, 0.25f, 0.2f));
            var stem = GlowMaterial("MushroomGlowStem", new Color(1f, 0.92f, 0.75f));
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name == "colorRed" ? cap : stem).ToArray();

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.3f);
            light.range = 3f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;

            var sparkles = BuildSparkles(root.transform);

            var spot = new SerializedObject(root.AddComponent<MushroomSpot>());
            spot.FindProperty("_visual").objectReferenceValue = visual;
            spot.FindProperty("_glow").objectReferenceValue = light;
            spot.FindProperty("_sparkles").objectReferenceValue = sparkles;
            spot.FindProperty("_collectSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "Notification_Chime.wav");
            spot.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static ParticleSystem BuildSparkles(Transform parent)
        {
            var ps = new GameObject("Sparkles").AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 1.6f;
            main.startSpeed = 0.4f;
            main.startSize = 0.08f;
            main.startColor = new Color(1f, 0.85f, 0.4f);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 100;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var material = AssetDatabase.LoadAssetAtPath<Material>(ParticlesMaterialPath);
            if (!material) Debug.LogWarning($"[PeaHeads] {ParticlesMaterialPath} not found; sparkles have no material.");
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        private static Material GlowMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.5f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // In-scene NetworkObject outside Environment: created once, never deleted, so its network id stays stable.
        private static void EnsureMatchObject(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(r => r.name == "Match")) return;
            var go = new GameObject("Match");
            go.AddComponent<NetworkObject>();
            var match = new SerializedObject(go.AddComponent<MatchManager>());
            go.AddComponent<MushroomSpawner>();
            go.AddComponent<ScoreHUD>();
            match.FindProperty("_confettiPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PeaHeads/Prefabs/VFX/ConfettiCelebration.prefab");
            match.FindProperty("_endSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "Notification_End.wav");
            match.ApplyModifiedPropertiesWithoutUndo();
        }
```

- [ ] **Step 6: Decoration: drop the hard-coded spawn check (spawns are now in `blocked`)**

In `ScatterDecoration`, delete the line `var spawns = new[] { new Vector2(0f, -12f), new Vector2(0f, 12f) };` and replace

```csharp
                if (PathDistance(p) < 2f || NearAny(p, spawns, 3f) || NearAny(p, cover, 2.8f) || NearAny(p, placed, 1.5f))
```

with

```csharp
                if (PathDistance(p) < 2f || NearAny(p, cover, 2.8f) || NearAny(p, placed, 1.5f))
```

and update its comment to `// Grass and bushes inside the play area, kept off paths, spawns, mushrooms and cover. No colliders.`

- [ ] **Step 6b: Update the class comment**

Replace `// Rebuilds DemoArena's Environment root: terrain, cover, tree ring, decoration, lighting.` with
`// Rebuilds DemoArena's Environment root: terrain, cover, mushroom spots, tree ring, decoration, lighting.`
and append to the second comment line: ` The root Match object and MushroomPickup prefab are created only if missing.`

- [ ] **Step 7: Run builder headless**

```bash
test -e $APP/Temp/UnityLockfile && echo "CLOSE THE EDITOR" || \
$UNITY -batchmode -nographics -quit -projectPath $APP -executeMethod PeaHeads.EditorTools.ArenaBuilder.Build -logFile $LOG/build.log; echo exit=$?
grep -E "error CS|Exception|\[PeaHeads\]" $LOG/build.log | head
```
Expected: `exit=0`, `Arena built`, no errors or warnings.

- [ ] **Step 8: Check scene and prefab**

```bash
S=$PH/Scenes/DemoArena.unity
grep -c "m_Name: Mushroom_" $S                       # expect 10
grep -c "m_Name: Match$" $S                          # expect 1
grep -c "m_Name: SpawnPoint_T" $S                    # expect 6 (plus root SpawnPoint_0/1)
grep -c "_team: 1" $S                                # >= 1: SpawnPoint_1 kept its value via FormerlySerializedAs (after re-save)
grep -c "m_Name: mushroom_" $S                       # decorative mushrooms gone: only inside pickups (prefab), expect 0 in scene
awk '/m_Name: Match$/{f=1} f&&/GlobalObjectIdHash:/{print; exit}' $S   # expect a non-zero hash
ls $PH/Prefabs/MushroomPickup.prefab $PH/Art/Arena/Materials/MushroomGlow*.mat
```
If the `Match` hash prints `0` or nothing: NGO assigns it in `OnValidate`. Open DemoArena in the editor and save once (the hash appears), then re-check. Note this for the user.

- [ ] **Step 9: Re-run builder (idempotency): `Match` must not duplicate**

Repeat Step 7, then `grep -c "m_Name: Match$" $S`. Expected `1`.

- [ ] **Step 10: EditMode tests**

```bash
$UNITY -batchmode -nographics -projectPath $APP -runTests -testPlatform EditMode -testResults $LOG/results.xml -logFile $LOG/test.log; echo exit=$?
grep -o '<test-run[^>]*' $LOG/results.xml | grep -oE '(passed|failed)="[0-9]+"'
```
Expected: `failed="0"`.

- [ ] **Step 11: Commit**

```bash
cd $REPO && git add PeaHeadsApp/Assets/PeaHeads/Editor/ArenaBuilder.cs PeaHeadsApp/Assets/PeaHeads/Scenes/DemoArena.unity \
  PeaHeadsApp/Assets/PeaHeads/Prefabs/MushroomPickup.prefab PeaHeadsApp/Assets/PeaHeads/Prefabs/MushroomPickup.prefab.meta \
  PeaHeadsApp/Assets/PeaHeads/Art/Arena/Materials
git commit -m "feat: ArenaBuilder places glowing mushroom pickups and the Match object

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Docs + code review + handoff

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: CLAUDE.md**
  - "Gameplay rules": replace `Health 3. Hit colliders on ...` paragraph's neighbour with a new bullet:
    `- Points race: kill = 3, mushroom = 1 (finish planting within 1.5 m of a glowing mushroom; 3 active of 10 mirrored spots, respawn 5 s), first to 20 wins → banner, input frozen, auto-restart after 5 s. Values on MatchManager / MushroomSpawner.`
  - States line: replace `Defeated is terminal until restart.` with `Defeated respawns after 3 s.`
  - "Networking model": replace `Server MatchManager ends round at 0, MatchEndClientRpc(winner), restart resets health + respawns at spawn points.` with `Server MatchManager (in-scene Match object) scores kills (PlayerHealth.Killed) and mushrooms, ends at the target, restarts after a delay (ResetHealth + RoundStartedRpc → PlayerNetSync.Respawn).`
  - Code layout: add `│   ├── Match/      MatchManager (scores, win, restart; TeamOf), MushroomSpawner (active-spot bitmask, server collect check), MushroomSpot (pickup visual)`, add `ScoreHUD` to the `UI/` line, remove `MatchManager (todo)` from the `Net/` line, add `MushroomPickup (generated once by ArenaBuilder)` to `Prefabs/`.
  - Code layout `Net/` line: `SpawnPoint` → `SpawnPoint (team 0/1; GetRandom avoids last point)`; mention `Environment/SpawnPoints` (3 extra per side) in the ArenaBuilder line.
  - Note under "I mostly work with Person 2": `MatchManager was built by Person 2 (2026-10-10) with the mushroom scoring feature; Person 3 informed.`

- [ ] **Step 2: Code review**: dispatch the **code-reviewer** agent on the diff since the spec commit (CLAUDE.md workflow). Fix CRITICAL/HIGH, re-compile, commit fixes.

- [ ] **Step 3: Commit docs**

```bash
cd $REPO && git add CLAUDE.md && git commit -m "docs: document mushroom scoring and MatchManager

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Human playtest checklist** (report to user; not automatable)
  - Solo host:
    - 3 mushrooms glow, sparkle and bob, and are visible from across the map.
    - Planting on one gives +1 and plays the chime and a burst. Another appears about 5 s later somewhere free.
    - Planting nearby (more than 1.5 m away), or pressing E and unplanting before 1 s, gives nothing.
  - Collecting until 20 shows the banner, freezes input and plays confetti; about 5 s later scores reset and you're back at spawn.
  - Two machines:
    - Bars match on both screens, and "(YOU)" is on the right bar.
    - A kill gives the shooter +3. Killing a dummy gives nothing.
    - A client joining mid-match sees the current scores and mushrooms.
    - Dying repeatedly respawns you at different points on your own side, facing the arena.
  - Tell **Person 3** that MatchManager exists, so they don't build another.
