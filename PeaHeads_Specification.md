# PeaHeads: Game Design & Technical Specification

**Hackathon project | 36 hours | 3-person team | Unity**

## 1. Project overview

**PeaHeads** is a quirky, two-player PvP game inspired by the visual style of *Plants vs. Zombies: Garden Warfare*. Each player controls a small pea-shooting plant with a leafy base, a thin green stem, and an oversized pea-shaped head.

The defining feature is that **the player's real-life head movements, tracked through a webcam, directly control the pea character's head and in-game viewpoint**. Players physically lean and move their heads to peek around cover, dodge incoming projectiles, and line up shots. Opening their mouths fires peas, while closing one eye zooms in for more precise aiming.

Unlike a conventional FPS, players cannot shoot while moving around the map. They must plant themselves in the ground before firing, turning combat into a game of positioning, timing, head movement, and aim.

The goal for the 36-hour hackathon is to create a small, polished, playable demonstration of this unique mechanic rather than a large, content-heavy game.

### Core design pillars

1. **Physical head movement is the wow factor.** Players should immediately understand that their real head movements control their character.
2. **Movement creates tactical decisions.** Players must find cover, plant themselves, peek around obstacles, and dodge incoming peas.
3. **Combat is simple but skill-based.** Large, slow-moving projectiles create opportunities to dodge and reward accurate shots.
4. **The game is quirky and expressive.** Oversized pea heads, exaggerated animations, and colorful environments make it entertaining to watch and play.
5. **The scope is achievable.** Every feature should support the core gameplay loop and fit the team's limited development time.

## 2. Core gameplay loop

A typical encounter should follow this sequence:

1. **Explore:** Move around the forest arena using the keyboard.
2. **Find cover:** Position behind a wide tree, rock, or other obstacle.
3. **Plant:** Activate the planting action and wait for the plant to become stationary.
4. **Peek:** Move your real head to look around the obstacle without exposing yourself more than necessary.
5. **Aim:** Line up a shot using normal mouse aiming or close one eye to activate zoom and a crosshair.
6. **Fire:** Open your mouth to shoot a pea projectile from the character's mouth.
7. **React:** Unplant to reposition or use real-life head movement to dodge an incoming projectile.
8. **Collect:** Plant on top of a glowing mushroom to collect it for a point. Planting takes time, so collecting in the open is a risk.
9. **Repeat:** Find a new angle, outmaneuver the opponent, and race to the target score with kills and mushrooms.

The central tension is that planting allows the player to shoot, but makes them vulnerable to incoming fire. Players must decide when to commit to a shot and when to unplant and reposition.

**Important gameplay rule:** Head movement controls the character's head and viewpoint, not the plant's movement around the arena. The character's body remains in place when planted. Head movement must never function as ordinary walking or strafing.

## 3. Player character

Each player controls a stylized pea-shooting plant consisting of three main parts:

- **Leaf base:** The bottom of the character, attached to the ground. It provides the planted/unplanted state and handles movement around the map.
- **Thin green stem:** Connects the base to the head. It should bend, stretch, or tilt slightly to make the character feel alive.
- **Large pea head:** An oversized green head with a round mouth. This is the primary visual focus and the part controlled by webcam head tracking.

### Visual direction

The intended style is inspired by *Plants vs. Zombies: Garden Warfare*:

- Colorful, stylized 3D environments.
- Exaggerated character proportions.
- Bright greens and saturated natural colors.
- Simple, readable silhouettes.
- Expressive animations and playful sound effects.
- Large, easily visible projectiles.

Use existing free or placeholder assets where practical. Do not spend valuable development time modeling highly detailed characters.

### Character animation

At minimum, the plant should have the following states:

| State | Behavior |
|---|---|
| Moving | The base moves through the world; the stem and head follow. |
| Planting | The plant transitions into a stationary stance. |
| Planted | The base is stationary, and the player can shoot. |
| Unplanting | The plant prepares to move again. |
| Shooting | The mouth opens or animates as a pea is fired. |
| Hit | The plant reacts to damage. |
| Defeated | The plant plays a simple defeat animation or disappears. |

Head tracking should be visible on the character model. When the player leans left, the pea head should lean left in the game. When the player looks upward, the head and viewpoint should look upward.

Avoid making the head movement purely cosmetic: **the in-game viewpoint must also respond so players can actually peek around cover and aim.**

## 4. Controls

The game uses a combination of keyboard, mouse, and webcam input.

### Keyboard and mouse

| Input | Action |
|---|---|
| `W` | Move forward |
| `A` | Move left |
| `S` | Move backward |
| `D` | Move right |
| Mouse movement | Aim and adjust the normal viewing direction |
| `Space` | Plant/unplant |
| `Esc` | Pause or open the menu |

The planting key is `Space` (changed from the originally suggested `E` on 2026-10-10).

### Webcam controls

| Webcam input | In-game action |
|---|---|
| Lean head left | Move the pea head and viewpoint left |
| Lean head right | Move the pea head and viewpoint right |
| Move head upward | Move the pea head and viewpoint upward |
| Move head downward | Move the pea head and viewpoint downward |
| Open mouth | Fire a pea projectile |
| Close one eye | Activate zoom and display a crosshair |

The webcam should track movement relative to a calibrated neutral head position, rather than assuming that every player's natural sitting position is the same.

### Control interaction rules

**Head movement and mouse movement must work together.**

- The mouse controls the normal aiming direction.
- The webcam adds physical head movement to the viewpoint and head pose.
- The player can combine mouse aiming with head movement to aim around cover.
- Head tracking must not cause the player's base to move around the arena.
- Opening the mouth should only fire when the plant is planted.
- Closing one eye should toggle or activate zoom without interfering with shooting.
- The planting action must take approximately one second to complete.

For the first prototype, head movement should have a bounded range. Moving your head farther should not cause unlimited camera movement.

### A note on head movement and the camera

The game needs to distinguish between **turning the camera** and **physically shifting the viewpoint**.

For example, leaning your real head to the right should shift the in-game viewpoint to the right relative to the character's body. This is what allows players to peek around a tree while keeping their base behind it.

Looking to the right, on the other hand, should rotate the viewing direction toward the right.

A good implementation maps horizontal and vertical head displacement to controlled viewpoint offsets, while allowing the mouse to handle conventional aiming. The exact mapping can be tuned during testing.

This distinction is important because rotating the camera alone will not let players peek around cover. The camera needs a small, bounded positional offset, and the character's head model should follow the tracked movement.

## 5. Webcam computer vision specification

The webcam system is the most important technical component of PeaHeads. It should be implemented early so the team can identify tracking problems before building the rest of the game.

### Recommended approach

Use **MediaPipe Face Landmarker or another lightweight face-landmark tracking solution** to detect facial features and estimate head position and orientation.

The computer vision component should provide normalized control signals that Unity can consume.

A practical architecture is:

1. Capture video from the webcam.
2. Detect facial landmarks and head pose.
3. Calculate head movement relative to a calibrated neutral position.
4. Detect mouth-opening and eye-closure gestures.
5. Apply thresholds and smoothing.
6. Send the resulting control values to Unity.
7. Unity translates those values into head animation, camera movement, zoom, and shooting.

For a three-person, 36-hour project, choose the integration path that gets reliable data into Unity the fastest. Avoid building a complicated custom computer vision pipeline.

If using Python for tracking and Unity for the game, connect them through a simple local communication channel, such as a UDP socket. Send only the control values, not the webcam video.

### Head movement detection

At the start of a session, ask the player to look straight ahead and click a calibration button.

Store that neutral position as the baseline.

During gameplay, calculate relative movement:

- **Horizontal displacement:** Left/right head lean.
- **Vertical displacement:** Up/down head movement.
- **Head orientation:** Optional additional information for matching the head model's rotation.

Normalize movement so that different players can use the same controls despite different distances from the webcam.

Use a dead zone to ignore tiny involuntary movements, then smooth the remaining signal to prevent camera jitter.

Suggested behavior:

- Small movement: Little or no in-game movement.
- Moderate movement: Noticeable viewpoint shift.
- Large movement: Maximum permitted viewpoint shift.

Start with a modest range and adjust it through playtesting. The goal is to let players peek around obstacles comfortably without needing exaggerated neck movements.

### Mouth-opening detection

Detect the distance between relevant upper and lower lip landmarks and normalize it against facial dimensions, such as the distance between the eyes.

When the mouth-opening measurement crosses a threshold, register a mouth-open event.

**Do not fire a projectile on every video frame that the mouth remains open.** Instead, use an event-based trigger:

1. Detect the transition from closed to open.
2. Fire one projectile if shooting is permitted.
3. Require the mouth to close again before another shot can be triggered.

This prevents one prolonged mouth opening from emptying the player's entire supply of ammunition.

A short cooldown may also be added to prevent accidental rapid firing.

The threshold should be adjustable, since players naturally have different facial proportions and resting expressions.

### Eye-closure detection

Detect whether the left or right eye is closed using eye landmarks and an eye-aspect measurement.

For the demo:

- Closing one eye activates zoom and a crosshair.
- Opening the eye again disables zoom.
- Both eyes closed should not activate the ability.
- Brief natural blinks should not repeatedly toggle zoom.

Use a small amount of temporal filtering to distinguish a deliberate wink from a single noisy frame.

If eye detection proves unreliable, provide a keyboard fallback for zoom. Webcam gestures should feel responsive, not random.

### Tracking failures and calibration

The system should account for:

- Poor lighting.
- The player's face leaving the camera frame.
- Glasses or facial features that make eye detection less reliable.
- Temporary loss of face landmarks.
- Sudden jumps in tracking.
- Different webcam frame rates.

If the face is temporarily lost, freeze or smoothly return the camera offset toward neutral rather than allowing it to jump unpredictably.

Display a small webcam status indicator so the player knows when tracking is active.

**Hackathon priority:** Reliable left/right head movement is more important than perfect detection of every gesture. If necessary, implement head tracking and mouth-triggered shooting first, then add eye-closure zoom.

## 6. Planting and movement mechanics

Planting is the central constraint that prevents PeaHeads from becoming a conventional FPS.

### Unplanted state

When unplanted:

- The player can move using WASD.
- The player cannot shoot.
- The player can reposition behind cover.
- The player can prepare to plant at a useful angle.

### Planting transition

When the player presses the planting key:

1. The plant begins a planting animation.
2. The character gradually becomes stationary.
3. Movement input is disabled or reduced during the transition.
4. After approximately one second, the plant enters the planted state.
5. Shooting becomes available.

The player should not be able to instantly switch from moving to shooting.

### Planted state

When planted:

- The plant's base is locked in position.
- WASD movement is disabled.
- The player can aim using the mouse.
- Webcam head movement controls the head and viewpoint.
- Mouth opening fires a pea.
- Winking activates zoom.
- The player can unplant to reposition.

### Unplanting transition

When the player presses the planting key again:

1. The plant begins an unplanting animation.
2. Shooting is disabled immediately.
3. After approximately one second, normal movement becomes available.

The team can tune the transition duration during testing, but the delay should be noticeable enough to make planting a commitment.

### Important implementation detail

Do not simply disable every form of camera movement when planted. The body and base should remain stationary, while the head and viewpoint retain their full tracking functionality.

The stem can stretch and bend to support the visual movement of the head.

## 7. Combat system

Combat should be easy to understand and fun to watch. The challenge should come from predicting opponent movement, using cover, and aiming with a combination of mouse and head movement.

### Pea projectiles

Each shot launches a large, green pea from the character's mouth toward the aiming direction.

Projectiles should be:

- Large enough to see clearly.
- Slow enough for opponents to dodge.
- Fast enough that shooting feels responsive.
- Visible against the forest environment.
- Equipped with a simple hit effect and sound.

A pea should travel in a straight line toward the direction the player is aiming when it is fired.

For the prototype, use Unity physics or a simple projectile movement script. Do not implement complicated ballistic physics unless testing shows that it improves the gameplay.

### Suggested starting values

These are prototype values, not final balance requirements.

| Parameter | Suggested starting point |
|---|---|
| Projectile speed | 8–12 world units per second |
| Projectile size | Approximately 0.3–0.5 world units in diameter |
| Planting duration | 1 second |
| Unplanting duration | 1 second |
| Health | 3 hits |
| Zoom | Approximately 1.5× |
| Firing cooldown | Approximately 0.5–0.8 seconds |

Tune these values in Unity based on the arena size and how easy the projectiles are to dodge.

The most important balance goal is that players can recognize incoming peas and react using their head movement.

### Damage and win condition

For the initial demo:

- Each player starts with three health points.
- A projectile hit removes one health point.
- A player who reaches zero health is defeated and respawns at their spawn point after a short delay.
- **Points race** (added 2026-10-10, see `docs/superpowers/specs/2026-10-10-mushroom-scoring-design.md`):
  - A kill gives the shooter **3 points**.
  - Collecting a mushroom gives **1 point**. Glowing mushrooms appear at spots around the map, a few at a time; a player collects one by completing a plant on top of it.
  - The first player to **20 points** wins. Otherwise, when the **120-second** match clock runs out, the player with the most points wins (a tie is a draw). Each player's score is shown as a bar at the top of the screen, with the countdown below it.
  - All values are adjustable on the components.
- When a player wins, both screens show the winner, then the match restarts automatically: scores reset and players respawn.

Avoid implementing complicated health regeneration, weapon upgrades, status effects, or multiple weapon types unless the core game is already complete.

### Collision and hit detection

Use simple colliders for the player and projectile.

The projectile should spawn at the character's mouth, not at the center of the body or the camera.

When a projectile hits a player:

1. Destroy the projectile.
2. Apply damage.
3. Play a hit effect or animation.
4. Update the health display.

The character's head movement and cover interactions must be consistent with hit detection. If the head can lean behind a tree while the base stays exposed, the game should not incorrectly treat the entire character as invulnerable.

## 8. Camera and cover mechanics

Cover is essential because it gives players a reason to lean physically rather than simply stand in an open arena and shoot.

### Camera behavior

The camera should follow the plant while allowing bounded head-tracking offsets.

The camera system must support:

- Normal movement while unplanted.
- Horizontal and vertical viewpoint shifts while planted.
- Mouse-based aiming.
- Zoom mode.
- Collision-aware behavior around nearby geometry.

A third-person camera that shows the full plant could help players appreciate the character's movements, while a first-person-style aiming view could make the webcam mechanic feel more direct.

For the hackathon, **choose whichever camera arrangement is easiest to implement reliably**. A hybrid view could work, but it should not become a major engineering project.

### Cover objects

The demo map should contain:

- Wide tree trunks.
- Large rocks.
- A few low obstacles.
- Open areas for repositioning.
- Clear lines of sight between cover positions.

Cover objects need colliders that block projectiles.

Players should be able to move behind an obstacle, plant, and lean around its edge to shoot. They should also be able to lean back behind the obstacle to avoid incoming fire.

### What makes cover work

Cover should create a clear trade-off:

- Staying fully hidden protects the player but prevents them from attacking.
- Leaning out creates a shooting opportunity but increases exposure.
- Staying exposed too long gives the opponent time to land a shot.
- Unplanting allows repositioning but temporarily sacrifices the ability to shoot.

The prototype should make these situations easy to understand through visual feedback and playtesting.

## 9. Demo map and art direction

### Setting

A small, colorful forest arena with a playful, slightly overgrown atmosphere.

The environment should evoke *Plants vs. Zombies: Garden Warfare*, rather than realistic military shooters or horror games.

### Required map elements

- A compact playable arena.
- Several wide trees.
- Several large rocks.
- At least two distinct cover positions.
- Enough open space to move between cover.
- Clear sightlines that let players spot incoming peas.
- A simple ground plane and background vegetation.

Use free assets, simple primitives, or existing low-poly models wherever possible.

### Level-design priorities

1. **Cover readability:** Players should immediately understand which objects can block incoming peas.
2. **Movement space:** The arena must allow players to reposition without getting stuck.
3. **Fair sightlines:** Neither player should have an overwhelming advantage from the starting position.
4. **Projectile visibility:** Green peas must remain visible against the ground and vegetation.
5. **Compact size:** Players should encounter each other quickly rather than spending most of the demo searching.

Do not build a large open world. One small, well-designed arena is enough.

## 10. Multiplayer requirements

PeaHeads is designed as a **two-player PvP game**. Each player controls their own pea shooter and uses their own webcam to control its head.

Multiplayer is a significant technical risk for a 36-hour hackathon, so the team should validate it early rather than leave it until the end.

### Minimum multiplayer functionality

- Two players can join the same match.
- Each player controls their own character.
- Each player's keyboard and mouse control their own movement and aiming.
- Each player's webcam controls only their own character's head and viewpoint.
- Players can plant, unplant, and fire independently.
- Pea projectiles are visible to both players.
- Hits and health changes are synchronized.
- The match ends when one player reaches the target score.
- Both players see the result of the match.

### Networking recommendation

Use an established Unity multiplayer solution rather than writing a custom networking system.

Possible options include:

- **Photon Fusion:** Worth considering if you want a relatively straightforward approach to networked gameplay.
- **Netcode for GameObjects:** A reasonable option if your team prefers Unity's networking ecosystem and is comfortable configuring it.
- **Another familiar networking solution:** Prefer whatever a team member has already used successfully.

Before committing, confirm that your chosen solution supports your intended two-player setup and can be integrated within the available time.

### What should be synchronized?

At a minimum, synchronize:

- Character position and planted state.
- Character head pose and relevant aiming direction.
- Projectile spawning and movement.
- Player health.
- Match state and round result.

**Webcam processing should happen locally on each player's computer.** Do not stream webcam footage to the other player. Only transmit the resulting control state or character pose.

The local player should receive their own webcam data directly, while the opponent receives the resulting networked character movement.

### Important multiplayer detail

The local player's camera should respond immediately to webcam input. It should not wait for a network round trip.

The remote player's view of that character can use interpolation to smooth out network updates.

For hit detection, start with a simple and consistent system. The host or server should be authoritative about damage if your networking solution supports it. Avoid building a sophisticated lag-compensation system for the prototype.

### Networking fallback

If multiplayer is not working early in development, reduce the scope immediately.

A useful fallback is a single-player practice mode with a stationary or simple AI opponent. This lets you demonstrate the webcam-controlled dodging, aiming, and shooting mechanics even if network integration fails.

However, the two-player experience remains the primary goal.

## 11. Suggested technical architecture

Keep the project modular so team members can work independently without constantly breaking one another's code.

A possible Unity project structure:

```text
Assets/
├── Scripts/
│   ├── Player/
│   │   ├── PlayerMovement.cs
│   │   ├── PlantingController.cs
│   │   ├── HeadMovementController.cs
│   │   ├── PlayerShooting.cs
│   │   └── PlayerHealth.cs
│   │
│   ├── ComputerVision/
│   │   ├── WebcamInputReceiver.cs
│   │   ├── FaceTrackingData.cs
│   │   └── GestureController.cs
│   │
│   ├── Combat/
│   │   ├── PeaProjectile.cs
│   │   └── ProjectileSpawner.cs
│   │
│   ├── Networking/
│   │   ├── NetworkPlayer.cs
│   │   └── MatchManager.cs
│   │
│   └── UI/
│       ├── PlayerHUD.cs
│       └── MatchUI.cs
│
├── Prefabs/
│   ├── Player/
│   └── Projectiles/
│
├── Scenes/
│   ├── MainMenu.unity
│   └── ForestArena.unity
│
├── Materials/
├── Models/
└── Audio/
```

This is a suggested structure, not a requirement to create every file immediately.

### Responsibilities of the main components

**`PlayerMovement.cs`**
- Handles WASD movement.
- Disables movement while planted or planting.
- Handles the transition between planted and unplanted states.

**`PlantingController.cs`**
- Manages planting and unplanting.
- Tracks the current state.
- Enforces transition durations.
- Tells the shooting system whether firing is allowed.

**`HeadMovementController.cs`**
- Receives normalized head movement data.
- Applies bounded head rotation and viewpoint offsets.
- Smooths movement and handles tracking loss.

**`WebcamInputReceiver.cs`**
- Receives computer vision output from the webcam tracking system.
- Exposes normalized values for horizontal movement, vertical movement, mouth state, eye state, and tracking confidence.

**`GestureController.cs`**
- Converts gestures into discrete actions.
- Detects mouth-open transitions.
- Activates and deactivates zoom.
- Prevents repeated actions from noisy tracking.

**`PlayerShooting.cs`**
- Checks whether shooting is permitted.
- Spawns peas from the mouth.
- Enforces firing cooldowns.

**`PeaProjectile.cs`**
- Moves the projectile.
- Detects collisions.
- Applies damage through the appropriate local or networked damage system.

**`PlayerHealth.cs`**
- Tracks health.
- Processes damage.
- Triggers the defeated state.

**`MatchManager.cs`**
- Coordinates match state.
- Detects when a player wins.
- Handles restarting the round.

### One architectural rule

Computer vision should not directly manipulate Unity objects or implement gameplay logic.

Instead, it should output simple control values, such as:

```text
HeadHorizontal: -1.0 to 1.0
HeadVertical:   -1.0 to 1.0
MouthOpen:      true or false
LeftEyeClosed:  true or false
RightEyeClosed: true or false
TrackingValid:  true or false
```

Unity then interprets those values according to the current gameplay state.

This separation will make the system easier to test, debug, and integrate with multiplayer.

## 12. Minimum viable product and feature priorities

For a 36-hour hackathon, the team must distinguish between the features that prove the concept and the features that merely make it more elaborate.

### Tier 1: Must work

These features define PeaHeads and should be treated as non-negotiable for the main demo.

- A controllable pea-shooter character.
- Keyboard movement.
- Planting and unplanting with a noticeable transition.
- Webcam-controlled head movement.
- In-game viewpoint shifts that allow the player to peek around cover.
- Mouth-open shooting.
- Visible pea projectiles.
- Basic projectile collision and damage.
- A small forest arena with functional cover.
- Two-player combat, if multiplayer is the primary demo format.
- A win condition and restart option.

### Tier 2: Important if time permits

- Wink-controlled zoom.
- Crosshair that appears during zoom.
- Character head and stem animations.
- Sound effects for shooting, hitting, planting, and taking damage.
- Health display.
- Tracking status indicator.
- Better projectile impact effects.
- Improved camera smoothing and calibration.
- A simple main menu.

### Tier 3: Skip unless everything else is complete

- Multiple character classes.
- Different weapons.
- Weapon upgrades.
- Complex physics.
- Multiple maps.
- Progression systems.
- Online matchmaking.
- Accounts or persistent player profiles.
- Elaborate menus.
- Highly detailed character models.
- Advanced animations.
- Sophisticated AI.

**Do not sacrifice reliable webcam controls to add more content.** One working arena with responsive head movement is much more impressive than several unfinished levels.

## 13. Recommended 36-hour development schedule

The most important scheduling principle is to validate the two largest risks first: webcam control and multiplayer.

| Time | Goal | Deliverable |
|---|---|---|
| Hours 0–2 | Project setup and technical validation | Unity project runs; webcam tracking produces usable control values; networking approach is selected and tested. |
| Hours 2–6 | Core character and webcam movement | A plant moves with WASD, plants and unplants, and responds to real head movement. |
| Hours 6–10 | Combat prototype | Mouth opening fires a pea; projectiles travel and can hit a target. |
| Hours 10–15 | Multiplayer integration | Two players can join a match and see each other move and shoot. |
| Hours 15–20 | Arena and cover | Forest map with trees and rocks; projectiles collide with obstacles. |
| Hours 20–25 | Complete the gameplay loop | Health, damage, win condition, and restarting work. |
| Hours 25–29 | Integrate remaining controls | Wink zoom, crosshair, feedback, and any missing webcam features. |
| Hours 29–33 | Playtesting and polish | Fix tracking issues, camera problems, projectile bugs, and confusing gameplay. |
| Hours 33–36 | Final build and demo preparation | Test the actual presentation setup, prepare a backup, and rehearse the demonstration. |

This is a target schedule, not a rigid commitment. Networking may take longer than expected, and the team should adapt rather than continue following the schedule at the expense of a playable build.

### The first two hours are critical

By the end of the first two hours, aim to answer these questions:

1. Can you reliably detect left/right and up/down head movement?
2. Can those movements control a Unity camera or character head?
3. Can two computers connect through your chosen networking solution?
4. Can a basic character and projectile be represented over the network?

If a component is proving difficult, simplify it immediately.

## 14. Suggested division of work for three people

Each person should own a reasonably independent part of the project, with agreed interfaces between components.

### Person 1: Computer vision and webcam integration

**Primary responsibility:** Make the webcam mechanic work.

Tasks:
- Integrate the facial landmark tracking library.
- Implement head movement detection.
- Implement mouth-open detection.
- Implement eye-closure detection.
- Add calibration, thresholds, and smoothing.
- Send tracking data to Unity.
- Test under realistic lighting and webcam conditions.

**Success criterion:** A player can move their real head and reliably control the in-game head and viewpoint.

### Person 2: Player controller and combat

**Primary responsibility:** Make the game feel good to play.

Tasks:
- Implement WASD movement.
- Implement planting and unplanting.
- Implement camera behavior and head movement offsets.
- Implement shooting and projectile movement.
- Implement collision, health, and damage.
- Implement zoom and crosshair behavior.
- Tune movement and combat.

**Success criterion:** A player can move, plant, aim, shoot, dodge, and take damage in a functional local prototype.

### Person 3: Multiplayer, environment, and integration

**Primary responsibility:** Make the game playable as a complete experience.

Tasks:
- Set up the networking solution.
- Implement player spawning and match joining.
- Synchronize player states and projectiles.
- Build the forest arena and cover.
- Implement match state, win condition, and restart.
- Integrate the final build and handle presentation setup.

**Success criterion:** Two players can play a complete match in the forest arena.

### Shared responsibilities

Everyone should help test the core mechanic and provide feedback.

Agree on key interfaces early. For example, the computer vision developer should provide normalized head movement and gesture states, not a custom Unity camera controller that conflicts with the player controller developer's work.

Likewise, the networking developer should agree on how player movement, planted state, and projectile spawning will be represented before each system is independently implemented.

**Important:** Person 3 should test networking in the first two hours rather than waiting until hours 10–15. If networking is delayed, it may become the biggest threat to the final demo.

## 15. Definition of done

The project is ready for the hackathon demo when all of the following are true:

### Webcam
- [ ] The player can calibrate a neutral head position.
- [ ] Left/right head movement changes the in-game viewpoint.
- [ ] Up/down head movement changes the in-game viewpoint.
- [ ] The pea head visibly follows the tracked movement.
- [ ] Opening the mouth fires a projectile when shooting is permitted.
- [ ] Closing one eye activates zoom and a crosshair.
- [ ] Small involuntary movements do not cause excessive camera jitter.
- [ ] Tracking loss does not make the camera jump unpredictably.

### Player and combat
- [ ] WASD movement works.
- [ ] Planting and unplanting take approximately one second.
- [ ] The player cannot move normally while planted.
- [ ] The player cannot shoot while unplanted or planting.
- [ ] Peas spawn from the character's mouth.
- [ ] Projectiles can hit opponents and reduce health.
- [ ] Cover blocks incoming projectiles.
- [ ] A round ends when a player's health reaches zero.

### Multiplayer
- [ ] Two players can join the same match.
- [ ] Each player's inputs control only their own character.
- [ ] Both players see the opponent's movement and projectiles.
- [ ] Damage and match results are synchronized.
- [ ] The match can be restarted.

### Presentation
- [ ] The forest arena is playable.
- [ ] Trees and rocks provide useful cover.
- [ ] The game communicates when players are planted and able to shoot.
- [ ] Players can understand the webcam controls without lengthy explanation.
- [ ] The game can be launched and demonstrated reliably on the intended hardware.

If the team runs out of time, prioritize a stable core mechanic and a complete match over cosmetic polish.

## 16. Risks and fallback plans

| Risk | Why it matters | Mitigation |
|---|---|---|
| Webcam tracking is jittery | Makes aiming and dodging frustrating | Add calibration, dead zones, smoothing, and movement limits. |
| Mouth detection repeatedly fires | Makes shooting unreliable | Trigger on mouth-open transitions and require mouth closure before the next shot. |
| Wink detection is unreliable | Makes zoom frustrating | Add temporal filtering and a keyboard fallback. |
| Networking takes too long | Prevents the intended PvP demo | Validate networking immediately; maintain a local practice-mode fallback. |
| Camera peeking does not match cover | Players may appear exposed when they think they're safe | Test with simple obstacles early and make viewpoint offsets and collision behavior consistent. |
| Head movement is uncomfortable | Players may avoid the main mechanic | Keep movement ranges small and calibrate per player. |
| Projectiles are difficult to dodge | Combat feels unfair | Reduce projectile speed, improve visual cues, or increase the arena's available cover. |
| The scope grows too large | The game remains unfinished | Freeze new features after the core loop is playable. |

## 17. Final design summary for the team

**PeaHeads is a two-player, webcam-controlled pea-shooter game in which physical head movement is the defining gameplay mechanic.**

Players move around a small, colorful forest arena using WASD. They must plant themselves before shooting, creating a tactical commitment to a position. While planted, they use their real head movements to shift the in-game head and viewpoint, peek around trees and rocks, and dodge incoming peas. Opening their mouths fires projectiles from the pea character's mouth, while closing one eye activates zoom and a crosshair.

The game should feel playful, colorful, and expressive, with a visual style inspired by *Plants vs. Zombies: Garden Warfare*. Its combat should emphasize readable, slow-moving projectiles, tactical cover, and physical head movement rather than conventional strafing.

The hackathon objective is not to build a full commercial game. It is to deliver a reliable, fun, and memorable demonstration of a simple idea: **you physically move your head to control a pea-headed plant, and that movement directly affects whether you can dodge an opponent's shot or land one yourself.**

The development priority is:

1. Prove webcam head tracking.
2. Prove planting, shooting, and dodging.
3. Validate multiplayer immediately.
4. Build one functional arena.
5. Complete the match loop.
6. Polish and rehearse the demo.

If those elements work well together, PeaHeads will have a clear identity and a compelling hackathon demonstration.
