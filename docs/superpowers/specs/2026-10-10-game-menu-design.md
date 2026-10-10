# Game menu (start + pause) — design

**Date:** 2026-10-10 · **Owner:** Ilan (net/session) with Person 2 · **Status:** approved in brainstorming, pending spec review

## Goal

Replace the IMGUI debug connect panel with a real mouse-driven menu: a start screen to **Host** or **Join**, and an in-game pause menu with **Resume**, **Restart Match** (host only) and **Leave** (both). It should look finished for judges and stay out of the way of the head-tracking controls.

## Decisions

| Question | Decision |
|---|---|
| Input | **Mouse.** Esc opens/closes the pause menu; cursor freed while open, re-locked on close. |
| UI tech | **uGUI Canvas built in code at runtime** (`com.unity.ugui` 2.0, already in the project as a dependency → made direct). Legacy `Text` + built-in `LegacyRuntime.ttf` font, no TextMeshPro (avoids the TMP Essentials import). |
| Scene impact | No hand-built Canvas/prefab. `DemoArena.unity` gets one extra component (`GameMenu`) on the existing `NetworkManager` object; avoids YAML conflicts with `ArenaBuilder`/partners. |
| Pausing | Networked game keeps running. Only the local plant ignores input while the menu is open. |
| Leave as host | Ends the session for everyone (NGO host shutdown). Client is told "Host ended the session." |
| Restart | Host only. Same reset as the automatic post-win restart (scores 0, HP full, all respawn), cancels a pending automatic restart. |

## Current state

- `Scripts/Net/ConnectionUI.cs` (on `NetworkManager` in `DemoArena`; also added by `Editor/NetTestSceneBuilder.cs`): IMGUI panel + logic. `StartHost()` (listen `0.0.0.0:7777`), `StartClient()` (address field, `MaxConnectAttempts`), connect/disconnect callbacks → `_status`, LAN IPv4 list, Disconnect = `nm.Shutdown()`, X button to dismiss while hosting solo; panel hides once 2 players are in.
- `PlayerInput`: Esc unlocks the cursor; a click while unlocked re-locks (and is swallowed). `Frozen` (written by `MatchManager` on match end) zeroes input. `OnEnable` locks the cursor, `OnDisable` unlocks.
- `MatchManager` (Person 3): private `Restart()` called by `Invoke` `_restartDelay` s after a win. Absent in `NetTest`.
- Project input handling = **Input System only** (`activeInputHandler: 1`) → UI needs `InputSystemUIInputModule`, not `StandaloneInputModule`.
- `PlayerHUD` hint text says "Esc free cursor".

## Components

### 1. `SessionController` (rename of `ConnectionUI`, `Scripts/Net/SessionController.cs`)

`git mv` the `.cs` and keep the `.meta` GUID, so the existing scene components in `DemoArena` and `NetTest` become `SessionController` automatically (no re-wiring). Class renamed; `OnGUI` and `_dismissed` removed. Logic unchanged otherwise.

Public API:
- `string Address { get; set; }` — host IP to join (default `_defaultAddress`).
- `string LocalIps { get; }` — LAN IPv4s joined by ", " (or "no LAN address").
- `ushort Port { get; }`.
- `string Status { get; }` — last status / error line ("" when none).
- `void Host()`, `void Join()` — the existing `StartHost`/`StartClient` bodies.
- `void Leave()` — `nm.Shutdown()`, `Status = "Left the match."`.
- Disconnect callback: if the local client was connected and the host went away, `Status = "Host ended the session."`; otherwise existing messages ("Player N left.", disconnect reason, "Disconnected / could not reach host. Same Wi-Fi? Firewall?").

`NetTestSceneBuilder` → `AddComponent<SessionController>()` + `AddComponent<GameMenu>()`.

### 2. `GameMenu` (new, `Scripts/UI/GameMenu.cs`)

MonoBehaviour, same GameObject as `SessionController` (`[RequireComponent(typeof(SessionController))]`).

**Built in `Awake`** (all code, no assets):
- `Canvas` (Screen Space Overlay, sort order 100) + `CanvasScaler` (scale with screen size, reference 1280×720, match 0.5) + `GraphicRaycaster`.
- `EventSystem` + `InputSystemUIInputModule`, only if no `EventSystem` exists yet.
- Full-screen dim backdrop + centred panel. Serialized style: `_panelColor`, `_buttonColor`, `_buttonHoverColor`, `_textColor`, `_accentColor` (title), `_font` (defaults to `LegacyRuntime.ttf` if unset).
- **Start panel:** title "PeaHeads", "This PC: {LocalIps} · port {Port}", label "Host IP", `InputField` bound to `SessionController.Address`, buttons **Host**, **Join**, status line.
- **Connecting panel:** "Connecting to {Address}…", status line, **Cancel** (→ `Leave()`).
- **Pause panel:** title "Paused", info line ("Hosting on {LocalIps}:{Port} · players {n}" or "Connected to {Address}"), buttons **Resume**, **Restart Match** (host + `MatchManager` present only), **Leave**.

**Each frame (`Update`):**
- Esc (`Keyboard.current.escapeKey.wasPressedThisFrame`) toggles `_pauseOpen` while connected; ignored on start/connecting screens.
- `MenuScreen screen = ScreenFor(active, connected, _pauseOpen)`; panels shown/hidden only when `screen` changes. Restart button visibility = `ShowRestart(isHost, matchExists)`.
- Status and info text refreshed.
- Local player's `PlayerInput.Paused = (screen == Pause)`; cursor unlocked + visible while Pause, re-locked when Pause closes (Resume or Esc). Local player = `NetworkManager.LocalClient?.PlayerObject`.
- `_pauseOpen` resets to false whenever the session stops (so the next session doesn't start paused).

**Buttons:** Host → `Host()`; Join → `Join()`; Cancel / Leave → `Leave()` then `_pauseOpen = false`; Resume → `_pauseOpen = false`; Restart Match → `FindFirstObjectByType<MatchManager>()?.RestartNow()` then `_pauseOpen = false`.

**Pure logic (public static, EditMode-tested):**
- `enum MenuScreen { None, Start, Connecting, Pause }`
- `static MenuScreen ScreenFor(bool sessionActive, bool connected, bool pauseOpen)` → not active: `Start`; active but not connected: `Connecting`; connected: `pauseOpen ? Pause : None`. (`sessionActive = nm.IsClient || nm.IsServer`; `connected = nm.IsHost || nm.IsConnectedClient`.)
- `static bool ShowRestart(bool isHost, bool matchExists)` → `isHost && matchExists`.

### 3. `PlayerInput` (changed)

- Add `public bool Paused { get; set; }` (set by `GameMenu`), separate from `Frozen` (owned by `MatchManager`), so neither writer overrides the other.
- Remove the Esc → unlock line (menu owns Esc and the cursor while paused).
- While `Paused`: skip the click-to-relock, `Current = default`, clear `_trackerFirePending`, return. (Same zeroing as `Frozen`.)
- Click-to-relock when not paused stays (e.g. after alt-tab).

### 4. `MatchManager` (changed, Person 3's file — tell them)

- Add `public void RestartNow()`: `if (!IsServer || !IsSpawned) return; CancelInvoke(nameof(Restart)); Restart();`.

### 5. `PlayerHUD` (changed)

- Hint text "Esc free cursor" → "Esc menu".

### 6. Packages / asmdef

- `Packages/manifest.json`: add `"com.unity.ugui": "2.0.0"` (already resolved at that version; makes the dependency explicit).
- `PeaHeads.asmdef`: add reference `"UnityEngine.UI"`.

### 7. Scene (editor, by hand)

- `DemoArena` → `NetworkManager` object: after the rename the old component shows as `SessionController`. **Add Component → `GameMenu`.** Save scene.

## Not changing

`PlayerNetSync`, `PlayerHealth`, `OverviewCamera`, `ScoreHUD`, networking model, match rules. `NetTest` scene keeps working only after regenerating it via **PeaHeads > Build Net Test Scene** (until then it has `SessionController` but no menu).

## Testing

- EditMode `GameMenuTests`: `ScreenFor` for all 5 meaningful input combos (idle; connecting; connected closed; connected open; idle with stale pauseOpen → Start); `ShowRestart` host/client × match/no match.
- Existing suite stays green; compile headless with the editor closed.
- Manual (DemoArena):
  1. Launch → start screen, cursor free; Host → menu disappears, cursor locked, plant controllable.
  2. Esc → pause menu with Resume / Restart Match / Leave; plant ignores WASD/E/clicks; Esc again → closes, cursor locked.
  3. Restart Match mid-match → scores 0, both players respawn, menu closes.
  4. Second machine: start screen → type host IP → Join → "Connecting…" → in game. Its pause menu has **no** Restart Match.
  5. Client Leave → client back to start screen; host sees "Player N left." in its next pause menu status.
  6. Host Leave → host back to start screen; client back to start screen with "Host ended the session."
  7. Join a wrong IP → Connecting → after attempts, back to start screen with the "could not reach host" message; Cancel works mid-connect.
  8. Match ends (target score) while pause menu open → closing the menu does not unfreeze input before the restart.
