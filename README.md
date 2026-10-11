# PeaHeads

**A multiplayer shooter where your face is the controller.**

You play a peashooter. Your webcam tracks your head: lean in your chair to peek around cover and dodge peas, open your mouth to spit, close one eye to aim. Built in 36 hours at **KnightHacks 2026** for the theme **Bloom vs Blight**.

---

## The idea

Most shooters are about how fast your hands are. We wanted one where your whole head is in the game.

A peashooter was the perfect character for that: it's a plant, so it can't really run, and it fights by spitting. So we made those the rules:

- **You can walk or you can shoot, never both.** To fire you have to *plant* yourself in the ground, which means you're stuck in one place. The skill becomes picking good cover and then peeking out from it with your real head.
- **Your head is the only thing that moves while you're planted.** Leaning left in your chair leans your pea left. Ducking ducks it. That's how you aim around a rock and how you dodge incoming peas.
- **Your face does the shooting.** Open your mouth and you spit a pea. Close one eye to zoom in for a long, straight shot.

The theme gave us two teams: **Bloom**, trying to heal the forest, and **Blight**, trying to rot it. Every knockout leaves a mark: the ground where a plant falls turns into the winning team's flowers or purple rot, so you can see the fight play out across the arena.

## How a match plays

- Two teams, **Bloom** (yellow) and **Blight** (purple). Teams are shuffled and kept even every match. Up to **4 players** on the same network.
- Score points by:
  - **knocking out** an opponent: **2 points**;
  - staying planted on a glowing **Sporelight mushroom** for 2 seconds: **3 points**, plus a heart back.
- First team to **20 points** wins, or whoever is ahead when the **2-minute clock** runs out.
- Each plant has **3 hearts** and respawns on its team's side 3 seconds after being knocked out.

## Features

**Face controls**
- Head tracking with MediaPipe Face Mesh. Lean, rise and duck move your pea's head and your camera.
- Open your mouth to spit (one pea per mouth-open).
- Close one eye to aim: you zoom in, and the pea flies faster and stays straight. Peas fired without aiming start to drop after a short distance.
- Press **C** to recalibrate to wherever you're sitting.
- Head, spitting and aiming are webcam-only on purpose; the keyboard is just for walking (WASD), planting (Space) and the menu.

**Your opponent sees your face**
- Your peashooter copies your expressions live on the other players' screens. Its snout flares when you open your mouth, and an eyelid drops when you wink.

**Kill cam**
- When you knock someone out, they see a short replay of your view **next to your real webcam face** at the moment you spat.
- The killer sees their own face in the corner.

**Cleanse and kill sites**
- A knocked-out plant is "cleansed": it turns the killer's colour and bursts into petals or rot dust.
- A puddle of the killer's team spreads across the ground where it fell:
  - **Bloom:** bright moss with yellow flowers.
  - **Blight:** glossy rot with glowing purple mushrooms.
- A new knockout takes over the other team's puddles, so by the end of a match the arena shows who won where.
- The fog and sky slowly take on the colour of the team that's winning.

**Around the game**
- A short story intro that also teaches the controls. Players can't be hit while watching it.
- A lobby where everyone stands on a stage and can see their pea copying them before the host presses **START**.
- Pause menu with controls, restart and leave. A minimap, score bars and clock, hit markers, scoped view and death screen.
- Music and sound effects (generated, see below).

## Controls

| Input | Action |
|---|---|
| Lean your head | Peek / dodge |
| Open your mouth | Spit a pea |
| Close one eye | Aim: zoom in, fast straight pea |
| Space | Plant / uproot |
| WASD | Move (only while uprooted) |
| Mouse | Look |
| C | Recalibrate your head position |
| Esc | Menu |

---

## Getting started

### Requirements
- **Unity 6000.3.9f1** (URP). Netcode for GameObjects 2.13.3 and the other packages come in through the project's manifest.
- **Python 3.9–3.12** (MediaPipe 0.10.21 doesn't support 3.13+) and a **webcam** — required, the game is played with your face.
- Two or more computers **on the same network** to play multiplayer. Some venue and campus Wi-Fi networks block devices from talking to each other; if joining fails, use a phone hotspot.

### 1. Set up the face tracker (once per computer)
From the repo root:

```bash
# Windows
py -3.11 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt

# macOS / Linux
python3.11 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
```

### 2. Run the tracker
```bash
.venv\Scripts\python head_tracker.py      # Windows
.venv/bin/python head_tracker.py          # macOS / Linux
```
A "Head Tracker" window opens with your camera. Look straight at the screen with your mouth closed; it calibrates on the first frame it sees your face. **C** recalibrates, **Q** quits.

### 3. Run the game
1. Open `PeaHeadsApp/` in Unity 6000.3.9f1.
2. Open the scene `Assets/PeaHeads/Scenes/DemoArena.unity` and press Play (or make a build).
3. One player clicks **Host**. The start screen shows their LAN IP.
4. Everyone else types that IP and clicks **Join**.
5. Watch the story (Space for next, Esc to skip), calibrate in the lobby with **C**, and the host presses **START**.

Hosting alone works too, so you can try the face controls and walk the arena on your own.

---

## How it works

```
 webcam ──► head_tracker.py (Python, MediaPipe)
               │  UDP 127.0.0.1:5052  head offset, mouth open, wink   (JSON, every camera frame)
               │  UDP 127.0.0.1:5053  small face images               (kill cam only)
               ▼
            Unity game  ◄──── LAN, UDP port 7777 (Netcode for GameObjects) ────►  other players' games
```

**Face tracking.**
- `head_tracker.py` finds the face landmarks with MediaPipe Face Mesh and compares your nose position with your calibrated neutral to get the lean.
- It measures how open your mouth is and how open each eye is, then sends a small JSON message to Unity on localhost.
- It never makes gameplay decisions; Unity decides what a gesture means (for example, a mouth-open only fires while you're planted).

**Input.** `PlayerInput` is the only script that reads devices. It merges webcam, keyboard and mouse into one input frame that movement, the camera, planting and shooting all read.

**Networking.** The game uses Netcode for GameObjects over direct IP.
- **The host is authoritative** for everything that could be cheated: peas, hits, health, score, teams and mushrooms.
- **Each player owns their own movement, head pose and expressions**, so your own controls never wait for the network.
- **NetworkVariables** carry continuous state: health, score, plant state, head pose, face flags, team lists.
- **RPCs** carry one-off events: fire, splat, cleanse, kill cam chunks, "respawn on side X".

**Generated content.** A lot of the game is made by code rather than imported:
- **The peashooter body** (trumpet snout, eyes, eyelid, collar and tail leaves, ground leaves) comes from our own mesh generators. An editor tool builds it into the player prefab.
- **The kill-site puddles, flowers and mushrooms** are generated at runtime. Each puddle's random shape is seeded from the death position, so every player sees the identical puddle without it being sent over the network.
- **The UI icons** are drawn from simple shape functions.
- **All music and sound effects** are synthesised by `tools/make_audio.py` (numpy/scipy, no samples).

**Privacy.** Your camera video stays on your computer. The only exception is the ~3-second kill cam clip of your face, sent only to the player you just knocked out.

## Project structure

```
head_tracker.py              Python webcam tracker (MediaPipe Face Mesh)
requirements.txt             tracker dependencies
tools/make_audio.py          generates all music and sound effects
PeaHeadsApp/                 Unity project
  Assets/PeaHeads/
    Scripts/
      HeadTracking/          UDP receiver + head/face → game input
      Player/                input, camera rig, movement, planting, shooting, health,
                             plant body meshes, stem, face expressions
      Combat/                pea projectile
      Net/                   host/join/leave, per-player sync, spawn points
      Match/                 match rules, teams, mushrooms, atmosphere, kill sites
      Killcam/               view/face recording and clip packing
      Lobby/                 lobby stage
      UI/                    menus, story, lobby view, HUD, score, minimap, kill cam view
      Audio/                 sound effects and music player
    Editor/                  builders: plant body, kill-site materials, arena
    Tests/EditMode/          automated tests for the pure game logic
    Scenes/DemoArena.unity   the game
docs/superpowers/            design specs and implementation plans we wrote while building
```

## Development notes

- **Tests:** 130+ EditMode tests cover the pure logic: planting, team shuffling, menu states, disconnect messages, mesh generation, puddle maths, face flags, icons, kill cam packing, lobby slots. Run them from the Unity Test Runner, or headless with the editor closed:
  ```bash
  Unity -batchmode -nographics -projectPath PeaHeadsApp -runTests -testPlatform EditMode -testResults results.xml
  ```
- **Generated assets, don't hand-edit:**
  - **PeaHeads > Build Plant Body** regenerates the peashooter parts (`Gen_*` objects in the prefabs).
  - **PeaHeads > Build Kill Site Materials** regenerates the puddle materials.
  - **PeaHeads > Build Arena** rebuilds the arena.
  - `python tools/make_audio.py` regenerates the audio.

## Credits

- Built by **Ilan Szabo**, **Charles Faso** and **Carlos Rincon** at KnightHacks 2026.
- Arena trees, rocks and props: [Kenney Nature Kit](https://kenney.nl/assets/nature-kit) by Kenney (CC0).
- Font: Chewy by Sideshow (licence included in `Assets/PeaHeads/Resources/Fonts/`).
- Started from Unity's FPS Microgame template, stripped down to a few props and effects.
- Face tracking: [MediaPipe](https://developers.google.com/mediapipe) Face Mesh; [OpenCV](https://opencv.org/) for the camera.
- Multiplayer: Unity [Netcode for GameObjects](https://docs-multiplayer.unity3d.com/).
