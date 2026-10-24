# Store Chase 3D — Player's Manual

A heist in a supermarket. Cats wander the aisles and steal merchandise off
the shelves; you have to get it all back behind the counter before the store
alarm fills. Press **V** (or gamepad **Y**) to switch between the isometric
overhead camera and a first-person mouse-look view — the two aren't just
different ways of looking at the store. A cat can see whether you're looking
straight at it.

The store is bigger than the screen. The camera follows you, so the aisles
scroll past as you move; shelf walls block your view, and a cat two aisles
away that has *heard* you is a different problem from one you can see.

---

## The goal of a round

Five items sit on the shelves (more, in later stores). Three cats wander the
store, and any of them will pick something up and make off with it. Two things
raise the alarm:

- **a cat is carrying something** — and the faster it rises the more cats are
  carrying, so a pile-up is a genuine crisis
- **you are carrying something** — at a much lower rate, so the walk back adds
  pressure without being a second timer

When nothing is out of place, the alarm drains. Get everything behind the
counter before it fills and you've won.

A cat that reaches the counter while carrying something **drops it there** and
the alarm jumps — bolting for the till is a delaying tactic that moves the
goods somewhere inconvenient, not a way out. You still have to collect them.

### The goods have kinds

The three kinds of goods are not just different colours:

- **cans** are cheap (the store barely notices one going missing) — worth 7c
  at the counter
- **bags** are worth the going rate — 10c
- **cartons** are expensive, and the alarm cares twice as much about them — 15c

Each species also has a **taste**. The Shop Cat goes for the cartons and
ignores everything else; a Kitten will take anything but prefers the bags; the
Lounge Lizard only nibbles at tins and only if it has to. The HUD lists what
each cat is after, so knowing which goods to watch is a matter of reading the
panel.

### The store has moods

The alarm meter passes through three stages, and the cats react to them:

- **calm** — the cats go about their business
- **suspicious** — the lighting warms towards red and the cats get braver
- **critical** — the cats stop playing about and go for the goods whatever
  they feel like, which is the moment the store is most likely to be emptied
  out from under you

### The cats talk to each other

One cat seeing you is enough for the others to know. A cat that spots you
raises the alarm, and it carries to any cat within eight cells — but the
further the news travels, the vaguer it gets, so a cat across the store has
only a rough idea of where you were. A cat with a rough idea heads that way
rather than wandering off.

### Camera and being watched

- **Isometric (default)** — angled 3/4 view. Pan with I/J/K/L, tilt with U/O,
  zoom with the mouse wheel. The store scrolls as you move.
- **First-person (V)** — 60° view from your head, mouse-look. In this mode
  cats can tell whether you're staring at them; a Nervous Cat freezes on the
  spot if you're looking straight at it. Overhead, "being watched" means
  "on screen", since the camera looks straight down.

A cat that can see *you* reacts; a cat that is being *watched* (by the
camera) may freeze. The two are deliberately separate.

---

## Power-ups

Three one-use items sit on the shelves each round, and you may carry **one at
a time** (E on the keyboard, B on the gamepad):

- **roller skates** — 8 seconds at ×1.5 speed, the escape tool
- **spray bottle** — panics every cat within range while you walk, for as long
  as you keep moving. It does not make them drop what they're carrying; it's a
  repositioning tool, not a weapon
- **cat treat** — drop it and every cat within earshot makes for it, loot or
  no loot. The cheapest way to empty an aisle. A cat that eats one becomes a
  friend and shadows you instead of stealing
- **floor trap** — set it where you stand; the first cat to step on it is
  stunned 3s and drops whatever it was carrying
- **laser pointer** — a moving dot that wanders the aisles for 6s; cats
  chase it instead of you
- **smoke bomb** (shop only) — panics every cat in earshot for six seconds

You start each round empty-pocketed; what you carried is what you spent.

## Hazards

Two spills and a fire alarm are laid out each round:

- **spills (wet floor)** — walk into one and you *slip*: in the isometric
  grid you can't brake for a beat, in first person you lag into momentum you
  didn't ask for. Slipping is faster for a moment (×1.35), so a slip can be
  an accident or a launch, depending on which way you were pointed. Skates
  double how long it takes to recover. Cats slip too, and a slipping cat
  drops what it was carrying — sometimes in the right direction
- **fire alarm** — stand on it and press Use to pull it. Every cat in the
  store panics (and drops any loot) and the alarm spikes by a fixed 15%. It's
  the one moment where the meter is your fault, so it's priced as a decision:
  trade 15% of the meter for total chaos

## The ball

A throwable distraction, aimed along the camera's ground heading (left click
or F). It works in both camera modes, but only first person aims it
meaningfully — overhead, the camera looks straight down so a throw always
travels the same way.

- A ball that lands on a cat **stuns it for 3 seconds** and makes it drop
  what it was carrying; the cat stays in the game.
- Cats that aren't ignoring balls path to a bouncing ball — but *not* a thief
  in the middle of an escape, which keeps a bolt for the counter honest.

## The characters

Seven species, each with its own speed, senses, and taste:

- **Shop Cat** — the big-box criminal. Whatever is in the cartons.
- **Kitten** — young and twitchy, takes anything but prefers the bags.
- **Lounge Lizard** — asleep on the job; only nibbles at tins, ignores balls.
- **Burglar Cat** — won't shoplift in front of you; watching it actually
  stops it. Once it has something it is gone.
- **Moggy** — slow, hard of hearing, sleeps through the aisles; the ball
  bounces off with no stun.
- **Mouser** — the fastest thing in the store, sharp ears; grab-and-run on
  the light bags.
- **Nervous Cat** — jumpy, freezes when you look straight at it; takes the
  light bags.

## The campaign: shops and coins

A run is a sequence of stores:

- **rounds escalate** — loot grows by one every two rounds (capped at 8),
  cats every three (capped at 5), and the floor plan cycles through four
  stores, ending on the biggest
- **you bank coins** — every secured item is worth its alarm weight × 10 (a
  tin 7c, a bag 10c, a carton 15c), plus a 25c bonus for clearing a store.
  The purse survives losses: a loss costs you the round, not the run
- **the shop** is open on the end screen, nine slots on keys 1–9:
  - consumables (queued, pocketed one per round): roller skates 15c, spray
    bottle 20c, cat treat 18c, smoke bomb 24c
  - permanents (three tiers each, priced at list × (tier + 1)):
    - **fast shoes** — +7% walk speed per tier
    - **calm nerves** — alarm decay ×1.15 per tier
    - **lucky charm** — counter-drop spike ×0.8 per tier
    - **quiet shoes** — footsteps 25% quieter per tier
    - **steady hands** — alarm rises 10% slower while carrying per tier
    - **quiet shoes** — your footsteps 25% quieter per tier
    - **steady hands** — alarm rises 10% slower while you carry goods per tier
- **R advances** on a win and **retries** on a loss

## Controls

Keyboard / mouse and gamepad are both supported and **auto-detected** —
whichever device you use last becomes active.

Keyboard / mouse:
- Arrow keys / WASD: move the player (in first-person, camera-relative: W
  forward, S back, A/D strafe)
- I / J / K / L: pan the camera (up/left/down/right)
- U / O: tilt the camera down/up
- Mouse wheel: zoom in/out
- V: toggle first-person / isometric view
- Mouse: look around in first-person (the pointer is captured)
- Left click / F: throw a ball (aimed along the camera's ground heading)
- E: use — the pocketed power-up, or pull the fire alarm if you're standing
  on one (the alarm wins that priority; it's the bigger button)
- 1–9: buy from the shop, between rounds
- Escape: quit
- R: between rounds — next store on a win, retry on a loss

Gamepad:
- Left stick / D-pad: move
- Right stick: pan the camera; look around in first-person
- Triggers (RT/LT): tilt
- Shoulders (RB/LB): zoom
- Y: toggle first-person / isometric view
- X: throw the ball
- B: use the pocketed power-up (or pull the fire alarm)
- Start: restart / next round
- Back: quit

The shop is currently keyboard-only (six items, six number keys); a gamepad
player can play the whole run but not spend the purse.

## Tips

- Read the HUD: it lists what each cat is after, so you know which goods to
  guard.
- A thief bolting for the counter is a crisis — tackle it or get there first.
- The spray bottle doesn't make cats drop loot; use it to reposition, not to
  disarm.
- The treat is the cheapest way to empty an aisle; a befriended cat shadows
  you instead of stealing.
- First person is a tactic: stare down a Nervous Cat to freeze it, or sneak
  past a Burglar that won't shoplift in front of you.
- Skates double slip recovery time — the boost is a package deal with the risk.

---

*Everything visual is either procedural geometry, a texture generated in code,
or a runtime-loaded OBJ. There are no image or audio assets; all sound is
synthesised at startup.*
