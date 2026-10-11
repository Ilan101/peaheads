# Lobby (holding room) — design

Approved 2026-10-10. A holding room between the story and the first match: players see their own 3D pea (and up to
three others) facing them, move only their heads, frame and calibrate their webcam, and wait for the host to start.

## Flow

- Start screen → Host/Join → StoryIntro (unchanged, still ends on "YOU ARE BLOOM/BLIGHT") → SPACE on the last slide
  → **Lobby** (instead of the arena).
- Host presses **START** → existing round start (`MatchManager.Restart` minus the reshuffle): respawn on sides, clock
  armed, waits for anyone still in the story as now.
- After that, matches auto-restart as today. The lobby is shown **once per session** (plus for late joiners).
- Late joiner (connects while a match is Playing/Ended): story → lobby with "Next round starts when this match ends"
  (no START) → pulled into the arena at the next round start (`RoundReset`). Their arrival no longer restarts the match.
- Host presses START while a client is still in the story: that client goes straight into the match when the story
  ends (the clock already waits for shielded players).
- Leave (pause menu) → start screen, as now.

## Match state and teams

- `MatchState` gains `Lobby`. A new host session starts in `Lobby`: no clock, no scoring, no auto-restart.
- `MatchManager.StartMatch()` (host only): from `Lobby` → `Restart` without `ShuffleTeams`. START keeps the lobby
  teams so the story card and head colours stay true.
- Client connects:
  - in `Lobby` → `ShuffleTeams` (as now), no restart.
  - in `Playing`/`Ended` → added to the smaller team (pure helper `SmallerTeam(count0, count1)`) and to a host-written
    `NetworkList<ulong> _waiting` (joined mid-round), no reshuffle, no restart. `Restart` clears `_waiting` and
    reshuffles everyone as usual. Disconnects are removed from `_waiting`.
- A player is in the lobby (`LobbyView.IsOpen`, static, like `StoryIntro.IsPlaying`) while their id is in
  `_waiting` (mid-round joiners go there straight away, under the story), or once their story has finished while the
  state is `Lobby`. Otherwise they are in the arena. So START closes the
  lobby everywhere, a mid-round joiner waits until the next `Restart`, and someone still in the story at START goes
  straight to the arena.

## Lobby stage (3D)

- `LobbyStage` (runtime-built, added by GameMenu like Minimap/WorldPolish), placed far outside the arena (a fixed
  offset, e.g. 400 m along +X, out of sight of the arena and minimap bounds).
- 4 slots in a row, ~3 m apart, each a small dirt mound + floor collider; slots face the lobby camera.
- Local lobby camera (not networked), framing all 4 slots head-on, rendering into a RenderTexture. Clear = solid
  colour matching the story slide background. **Mirrored horizontally** by showing the texture in a full-screen
  RawImage with a flipped `uvRect`, so leaning right moves your pea right on screen.
- Slot index = rank of the owner's client id in `MatchManager.LobbyMembers()` (both teams while in `Lobby`, else
  `_waiting`; sorted, max 4). Players still in the story keep their slot reserved (shown as "waiting for a pea…").
- Arena players never see the stage (distance); minimap ignores players inside the lobby bounds.

## Players while in the lobby

- Owner teleports its own Player to its slot (`PlayerNetSync.MoveToLobby(slot)`, owner-auth NetworkTransform carries it),
  unplanted, yaw facing the camera. Re-slots when the lobby membership changes.
- `PlayerInput.LobbyMode`: only `Head`, `MouthOpen`, `ZoomHeld` (face flags: snout flare, eyelid) and
  `RecenterPressed` pass; no move, look, plant or fire. Cursor stays free.
- Shielded (`PlayerHealth.SetShielded(true)`), as during the story.
- Own head/snout render normally while in the lobby (`PlayerCameraRig.SetLocal` shadows-only toggled off), player
  camera off, lobby camera on. PlayerHUD, ScoreHUD, Minimap and killcam hidden while `LobbyView.IsOpen`.
- Everyone sees every lobby pea's real synced head offset, pitch, mouth/wink flags and team tint — no new netcode.
- Leaving the lobby: player camera back on, shadows-only restored, owner respawns on its side (`RespawnOnSideRpc` at
  round start already does this).

## Lobby UI (`LobbyView`, uGUI built in code, story style)

- Same look as StoryIntro: display font, outlines, drifting spores, story palette (Gold/Cream).
- Top: title ("THE GROVE GATHERS") + subtitle "Waiting for peas · N/4".
- Under each slot: "YOU" / "PEA 2..4" label in team colour; empty slots show a dim "waiting for a pea…" outline.
- Bottom-left: framing window — latest face JPEG from `FaceFeed` (5053) with the tracker box + head dot (as PlayerHUD's
  webcam panel); "no face" state when the feed is not live. **CALIBRATE** button → `HeadTrackingInput.Calibrate()`
  (same as C).
- Bottom-centre: **START** (host only, works with 1 player). Clients: "Waiting for host to start…". Late joiner:
  "Next round starts when this match ends".
- Esc opens the existing pause menu over the lobby (Leave, Music, Killcam face).
- Music: story track while in the lobby.

## Player limit

- Max 4 players. Host enables connection approval and rejects a 5th connection; the joiner's status shows
  "Lobby full (4 peas)."

## Testing

EditMode (pure logic only): `LobbyMembers` ranking, `SmallerTeam`, `LobbyView.ShouldOpen`, `LobbyStage.Contains`,
`GameMenu.ScreenFor` with the lobby screen, `SessionController.CanJoin` + lobby-full status.
Everything else verified by playtest: 1-player host START, 2–4 player lobby head motion, mirror, calibrate, late
joiner during a match, 5th player rejected.

## Out of scope

Ready-up toggles, kicking players, choosing teams in the lobby, returning to the lobby after a match.
