# Cleanse + kill sites — design

**Date:** 2026-10-11 · **Owner:** Ilan · **Status:** approved in brainstorming, pending spec review

## Goal

When a plant dies it is "cleansed" by the killer's faction: it takes on the killer's colour and bursts (Bloom: yellow petals; Blight: purple rot dust), and a ~3 m patch of ground where it died turns into the killer's look (Bloom: bright grass + flowers; Blight: purple-black rot + tendrils + wilted grass). Patches stay all match, a new kill takes over the other team's overlapping patches (tug of war), and everything clears at the next match start. Purely visual; no gameplay effect.

## Decisions

| Question | Decision |
|---|---|
| Death look | Dissolve into the killer's colour (~0.3 s tint) then a particle burst. Colliders still switch off instantly. |
| Ground | **Separate patch objects** (approach 2): 12–16 small irregular ground blobs + 6–10 props, each placed by a downward raycast so it follows slopes; no terrain / ArenaBuilder / render-pipeline changes. |
| Overlap | New patch removes the other team's patches whose centres are within its radius; same-team patches stack. |
| Lifetime | Until the match restarts (all patches cleared); cap 20 patches, oldest removed first. |
| Sync | Host sends one RPC per death (position + killer team). Layout is seeded from the position, so every machine builds identical patches. |
| Dummies | Dummy kills also cleanse and leave a patch. |

## Current state

- `PlayerHealth.TakeDamage(amount, attacker)` (server): on death fires `Died`, static `Killed(victim, attacker)`, schedules `ResetHealth` after `_respawnDelay`.
- `OnHealthChanged` (all machines) → `SetAlive(false)` immediately disables all child renderers and non-CharacterController colliders; revival re-enables them and fires `Revived`.
- `MatchManager.Restart()` (server) reshuffles teams, resets scores/HP, sends `RespawnOnSideRpc`; no client-side "new round" signal exists (state may stay `Playing` across a join-restart).
- `PlantBodyBuilder` (editor) writes generated materials; URP Lit is the project shader.

## Components

### 1. `KillSitePlanner` (new, `Scripts/Match/KillSitePlanner.cs`, static, pure)

- `public static int Seed(Vector3 position)` — deterministic from position rounded to 0.1 m (same on every machine).
- `public static Vector2[] Scatter(int count, float radius, int seed)` — golden-angle spiral (`r = radius·√((i+0.5)/count)`, angle `i·137.5°` + seeded rotation) with seeded jitter ≤ 15 % of spacing; every point inside `radius`.
- `public static List<int> ToReplace(IReadOnlyList<(Vector3 centre, int team)> existing, Vector3 centre, int team, float radius)` — indices of patches of the **other** team with horizontal distance (XZ) < `radius`.
- `public static int OverCap(int count, int cap)` — number of oldest patches to remove before adding one (`max(0, count + 1 − cap)`).

### 2. `KillSites` (new, `Scripts/Match/KillSites.cs`, MonoBehaviour)

Added to the `Match` object by `MatchManager.OnNetworkSpawn` if missing (like `AtmosphereController`). `public static KillSites Instance`.

Serialized: `_radius = 1.5f` (patch radius, ~3 m across), `_blobCount = 14`, `_propCount = 8`, `_growTime = 1f`, `_cap = 20`.

- `public void Cleanse(Vector3 position, int killerTeam)`: removes `ToReplace` patches and `OverCap` oldest, then builds a patch:
  - root GameObject at `position`;
  - blobs: for each `Scatter(_blobCount, _radius, seed)` point, raycast down from 3 m above (6 m length, ignore triggers and players), keep hits with `normal.y ≥ 0.7`; irregular disc mesh (`PlantMeshes.Disc`-like, 10 sides with seeded radius jitter, radius 0.35–0.5 m) aligned to the normal, raised 0.01 m + 0.002 m × index (no z-fighting); colour = team ground colour with ±8 % seeded lightness;
  - props: `Scatter(_propCount, _radius * 0.8f, seed + 1)`, same raycast; Bloom → flower (green stem capsule + 5 `PlantMeshes.Leaf` yellow petals + orange centre sphere, 0.25–0.4 m tall); Blight → alternating rot tendril (curled dark-purple `Leaf`, upright) and wilted grass (brown `Leaf`, drooping);
  - growth: each child scales 0 → 1 over `_growTime`, delayed by its distance from the centre (outward ripple).
- `public void Clear()`: destroys all patches.
- Colours: `Faction.Color(team)`-based — Bloom ground `(0.55, 0.8, 0.25)`, Blight ground `(0.22, 0.1, 0.25)`; applied with `MaterialPropertyBlock` (`_BaseColor`) on one shared Lit material.
- Clears on `MatchManager.RoundReset` (§4).

### 3. `PlayerHealth` changes (teammates' file)

- In `TakeDamage`, on death (server): `CleansedRpc((byte)MatchManager.TeamOf(attackerClientId))`.
- `[Rpc(SendTo.ClientsAndHost)] private void CleansedRpc(byte killerTeam)` → starts `Cleanse(killerTeam)` coroutine on every machine:
  - tints every child renderer toward `Faction.Color(killerTeam)` over `CleanseTime = 0.3 s` via `MaterialPropertyBlock` (`_BaseColor`);
  - then spawns the burst (§5) at the plant's centre (y + 1 m) and calls `KillSites.Instance?.Cleanse(transform.position, killerTeam)`.
- `SetAlive(false)`: colliders off immediately (unchanged); renderers off after `CleanseTime + 0.05 s` (coroutine), unless revived first. `SetAlive(true)`: clears the property blocks (restores own colours) and re-enables renderers.

### 4. `MatchManager` changes

- `private readonly NetworkVariable<int> _round = new();` incremented at the end of `Restart()` (server).
- `public static event Action RoundReset` raised on every machine from `_round.OnValueChanged` (subscribed in `OnNetworkSpawn`, removed in `OnNetworkDespawn`).
- `OnNetworkSpawn` (all): add `KillSites` to the Match object if missing.

### 5. Burst + materials

- Burst: a `ParticleSystem` built in code (cached template per team): 40 particles, sphere shape r 0.4, speed 2–4, lifetime 0.8–1.4 s; Bloom petals: yellow → white, size 0.12, gravity 0.3 (drift down); Blight dust: purple → black, size 0.18, gravity −0.1 (rises), fade out. Destroyed after 2 s.
- Materials: `PlantBodyBuilder` writes `Assets/PeaHeads/Resources/KillSites/KillSiteLit.mat` (URP Lit, smoothness 0.2) and `KillSiteParticle.mat` (URP Particles/Unlit, alpha blended) so the shaders ship in builds; `KillSites` loads them with `Resources.Load`.

## Not changing

Terrain, ArenaBuilder, render pipeline settings, scoring, respawn timing, killcam, death screen.

## Testing

- EditMode `KillSitePlannerTests`:
  - `Scatter`: count correct; every point within radius; minimum pairwise distance ≥ 0.5 × (radius / √count); same seed → identical points; different seed → different.
  - `Seed`: same position → same seed; positions 1 m apart → different seeds.
  - `ToReplace`: other team inside radius → included; same team inside → excluded; other team outside → excluded; uses XZ distance only.
  - `OverCap`: 0 below cap, 1 at cap.
- Compile headless; run `PlantBodyBuilder` to create the two materials.
- Manual (two machines, DemoArena):
  1. Kill as Bloom: victim flashes yellow, bursts into petals; flower patch grows on both screens, same layout.
  2. Kill as Blight: purple tint, rot-dust burst, rot patch.
  3. Kill on an opposing patch: old patch vanishes, new one replaces it.
  4. Death on a slope / next to cover: blobs hug the ground, none float or poke through cover.
  5. Respawned plant has its own colours back (no leftover tint).
  6. Restart Match / clock end / client join: all patches cleared.
  7. 20+ kills: oldest patches disappear, no frame drops.
  8. Dummy kill leaves a patch.
