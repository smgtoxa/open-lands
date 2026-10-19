# Bug report: C# engine compared with ScummVM and the original game

**Status: all findings below were fixed in 0.3.0 (L9 was not a bug). See CHANGELOG.md.**
Date: 2026-10-18. Reference: ScummVM `engines/kyra` at commit 3f4203f, the closest thing to the original's code (we never
decompiled the original game). C#: `Engine/Game` at commit 5bfb828.

## How this was done
- Six audits went function by function: combat and monsters, magic, items, characters and timers, scripts, TIM and
  text, scene, movement and GUI, and save/load, sound and the ending.
- New port features were left out when they are switched off: the Imp's Pit, the camp, crafting, the companion
  bench, extra spells, mods, the host interface and the new settings.
- Almost every finding is also in the web JS engine (`~/lands/src/game`). They came over in the 1:1 port, so the
  parity tests (C# against JS) cannot catch them.

## Checked against the original game (2026-10-18)
Every finding below was then checked against the original DOS program, decompiled with Ghidra (GOG CD
`MAIN.EXE`, with its Borland overlays turned into normal code by a rewriter script). The ending was checked in
`FINALE.EXE` and the intro in `LANDS.EXE`, both taken out of `GAME.DAT`. The rule is: **the original wins**, not ScummVM.
The scratch work (decompiles, tools, the address map) is in the session scratchpad under `re/`.

**Result: 37 of 38 are real bugs in our code. 33 should be fixed to match ScummVM, which matches the original
there. 4 need a different fix than ScummVM's. L9 is not a bug. 5 new differences turned up.**

| Verdict | Findings |
|---|---|
| Original = ScummVM: our code is wrong, fix as the report says | H1, H2, H3, H4, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11, M12, M14, M15, M16 (one input event per pass), L1, L3, L4, L6, L7, L8, L11, L12 (lightning part), L13, L14, L15, L16, L17, L18 |
| Original differs from both: fix to the original, not ScummVM | **M13**: skip the wait when speech is on, but keep our tick-based length (ScummVM's milliseconds are its own bug). **L2**: `characterUpdateDelay` is a signed byte (`sbyte`): values of 128 and up fire at once. **L5**: `triggerEventOnMouseButtonClick` re-posts the input it just read when its argument is nonzero, and empties the input queue when it is 0; there is no 65/199/201 mapping. **L10**: Spark uses the dice generator (`rand`); screen shake uses a separate Westwood LFSR generator (seed at DS:0x3842), not our "presentation" generator. |
| Original = our code: not a bug | **L9**: protection is signed, so the `d<0` branch is real. One edge case: the original reads a negative damage as unsigned on its second multiply, giving garbage, which only happens if data ever holds negative protection. **L12 swarm part**: the CD version does not rebuild the transparency table either. |
| Not original | M16's play timer: the original keeps no play time, so it is optional. |

New differences from both ScummVM and our code:
- **N1** (Medium): the evade chance is multiplied by MonsterModifiers4 (difficulty) for **every** target, heroes
  too. ScummVM and we apply it only to monster targets. Original `battleHitSkillTest` at 1740:010a.
- **N2** (Medium): `setCharacterMagicOrHitPoints` has no `if (charNum > 2) return`, so HP/MP changes reach the 4th
  party slot. ScummVM (`lol.cpp:1468`) and we (`Party.cs:486`) skip it.
- **N3** (Low): rest healing intervals are `600/maxHP + 1` and `600/maxMP + 1` (cap 30); poison is
  `(600/curHP + 1) >> 1` (cap 15). ScummVM and we have no +1 and no >>1.
- **N4** (Low): bumping into a wall (`notifyBlockNotPassable`) empties the input queue first, so buffered moves are dropped.
- **N5** (Low): after the hero's final picture the ending waits for a key: Esc quits, and any other key replays
  the whole ending.

Codes: **✔** = I re-read both sides myself. *Confirmed / Likely / Unsure* = the auditor's confidence.

## High

| # | Bug | Where (ScummVM / C#) | Effect in game |
|---|---|---|---|
| H1 ✔ | Monster melee hit test reads the **target's** monster slot instead of the attacker's | `lol.cpp:3458-3460` / `Party.cs:588-592` | Every monster attack on the party uses the hit chance and skill of whatever monster sits in slot 0-3, not the attacker's. If that slot is empty (it has no properties), the attack throws, the error is swallowed, and the hero in that position cannot be hit in melee (*Likely*). |
| H2 ✔ | Saving tags floor items with their level **in the running game** | `saveload_lol.cpp:337-345` / `SaveGame.cs:236-251`, `Items.cs:144` `assignBlockItem`, `Items.cs:161` `addLevelItems` | Save, then drop an item on a block that already holds items, leave the level and come back: that block's item list loops on itself (A→…→A). Anything that walks the list can hang. Autosaves and checkpoints make it easy to hit. *Likely* (traced by reading, not reproduced). |
| H3 ✔ | Loading a level never visited before **deletes items tagged for it** | `scene_lol.cpp:64-76` / `Scene.cs:226-234` | The Imp's Pit added this cleanup, and it is not behind any option. Items that fell through a pit or that a script moved to a later level vanish on arrival. A lost quest item could block the game. |
| H4 | Unequip script runs **later** (`queueAsync`), after the item is destroyed | `lol.cpp:3853-3866` / `Party.cs:867-878` | When acid or a monster skill destroys a worn item, its "remove" script runs on an empty item. The hero can keep the bonuses of gear that no longer exists. *Likely*. |

## Medium

| # | Bug | Where (ScummVM / C#) | Effect in game |
|---|---|---|---|
| M1 ✔ | Wrong list of timers paused by `disableSysTimer` | `staticres_lol.cpp:790` / `Party.cs:66,74,80` | Missing: 0 (doors) and 0x50-0x52 (scene animation scripts). Extra: 11 (message fade). During dialogues, spells, the inventory and level changes, doors keep moving and level animations keep running, while messages stop fading. |
| M2 | `timerEnable`/`timerDisable` overwrite the pause bit | `timer.cpp:199-243` / `Engine.cs:339-355` | Timer 3 un-pauses itself during paused sequences, so poison ticks, and stun or paralysis can wear off. *Confirmed*. |
| M3 | Party death skips setup | `lol.cpp:3689-3711` / `Party.cs:756-768` | No death music (track 325) and no `disableSysTimer`: monsters and timers run behind the death menu. *Confirmed*. |
| M4 ✔ | Lever click changes **both** faces of the wall | `scene_rpg.cpp:635-664` / `Scene.cs:790-810` | The far side of a lever block turns into the next wall type. It also plays sound 28 instead of 30 and never asks for a redraw. |
| M5 ✔ | Left clicks carry the right-click value (`flags2` 0x1000 instead of 0x100) | `gui_lol.cpp:1977-1993` / `Gui.cs:715` | A left click on the inventory arrows scrolls 9 slots instead of 1. With the spell-level menu open, a left click elsewhere acts as a right click. |
| M6 | `setWallType` never asks for a redraw | `scene_lol.cpp:1240-1255` / `Scene.cs:508-521` | Walls changed by scripts (secret walls, animations) stay as they were until something else redraws. *Confirmed*. |
| M7 | Monster death script (0x404) runs after the monster is removed | `sprites_lol.cpp:198-207` / `Monsters.cs:267-275` | Boss or special death scripts see block 0 and position 0, so drops and triggers can land in the wrong place. *Likely*. |
| M8 | Save does not finish moving doors first (`completeDoorOperations`) | `saveload_lol.cpp:343` / `SaveGame.cs:235` | A save made while a door moves loads with the door stuck half-open. *Confirmed* missing, effect *Likely*. |
| M9 | Monster HP not rescaled when the difficulty changed since the last visit | `saveload_lol.cpp:537-571` / `Spells.cs:773-805` | After a difficulty change, monsters on visited levels keep their old HP. *Confirmed*. |
| M10 | Gladstone gate guard is moved aside when you bump into him (`force`) | none / `Scene.cs:612`, `170-188` | A possible sequence break: he moves even if his greeting never played. Not behind an option. *Likely*. |
| M11 | Intro and ending cinematics may never end on their own (`cmd_stopCurFunc` not wired) | `script_tim.cpp:42-79` / `Tim.cs:271`, `Intro.cs:273` | Possibly replays until a key or click is pressed. *Likely*: needs an in-game test. |
| M12 | Ending incomplete | `sequences_lol.cpp:1173-1207`, `script_lol.cpp:1419` / `Intro.cs:314-378` | Missing: the hero's final picture, the hardest-difficulty text, the CONGRAT voice and the real credits (`processCredits`). *Confirmed*. |
| M13 | Ending opcode `delayForChat` waits with speech on (the length is right: see the check against the original) | `script_lol.cpp:2664` / `Intro.cs:238` | The ending drifts out of sync with voice and music. *Confirmed*. |
| M14 | New speech cuts off the line already playing | `sound_lol.cpp:94-98` / `Sound.cs:522` | Overlapping hero remarks get cut off. ScummVM waits for the current line to end. *Confirmed*. |
| M15 | Dialogue field setup runs later (`queueAsync`) | `lol.cpp:1734-1761` / `Spells.cs:196,226` | Text can print into the old box size and then be overdrawn. *Likely*. |
| M16 | Input loop handles all queued events in one frame | `lol.cpp:932-975` / `Engine.cs:636-724` | Buffered moves and clicks run back to back with no timer tick between them. `updatePlayTimer` is never called, so play time is not counted. *Confirmed*. |

## Low

- **L1** Rest loop: an extra delay each step changes the monster/heal step ratio, so the odds of an ambush differ. The input that wakes the party is dropped. `gui_lol.cpp:1640-1715` / `Gui.cs clickedRestParty`.
- **L2** `characterUpdateDelay` is int, not uint8: 0 then -1 clears instantly, where ScummVM gets about 255 ticks (faces after interrupted rest). `Types.cs:84`.
- **L3** Monster `sounds[]` and `numDistAttacks` lose their 8-bit wrap: a sound of -1 is played, and defense skill 6 gives infinite ranged attacks instead of stopping them. `Monsters.cs:145-185`, `Party.cs:861`.
- **L4** `setNextFunc` is cleared before the function runs, so a self-rescheduling script function runs every frame. `lol.cpp:949` / `Engine.cs:688`. *Unsure*.
- **L5** `olol_triggerEventOnMouseButtonClick` maps codes 199/201 and keys to Enter. `Script.cs:743`.
- **L6** `olol_loadBitmap` copies page 3 to page 2 when the argument is 2; ScummVM copies when it is not 2. `Script.cs:304-310`.
- **L7** Wall-decoration click plays sound 105 instead of stopping speech. `scene_rpg.cpp:624` / `Scene.cs:781`.
- **L8** Ruby of Truth merge plays a remapped sound instead of raw AdLib effect 99. `Gui.cs:1193,1220`.
- **L9** Protection value read as signed (`(short)`) where ScummVM reads it unsigned. A deliberate JS choice and probably closer to the original. `Party.cs:633`.
- **L10** Spark and screen shake use a separate random generator, so combat rolls don't replay like the original's. `Spells.cs:455`.
- **L11** HP/MP bars jump instead of animating. Spells start a little sooner and timers don't run during the bar. `Party.cs:500-511`.
- **L12** Lightning: the shake runs in the background and the sound/flash counter resets each cast. Swarm: the transparency table is not rebuilt. `Magic.cs:383,429-470,562`.
- **L13** `printMessage` stops at control char 1, with no MORE page breaks. `Text.cs:427-453`.
- **L14** `getGlobalVar(13)` returns 0/1/2 instead of the speech volume. `Script.cs:323`. *Unsure*.
- **L15** Load clears character flags 0x2000 and 4 and `weaponHit`, and does not set flag 73 bit 8 again. `SaveGame.cs:323-329,430-447`.
- **L16** Restored levels skip monsters saved mid-death. `Spells.cs:797-803`. *Unsure*.
- **L17** Ending visuals: `fadeInScene` blend, `displayAnimFrame` pages, `fadeOutSound`. `Intro.cs:204-237`.
- **L18** No sound-effect priority or channel limit. Missing `checkFloatingPointerRegions`, so cursor shapes can go stale.

## Port features (not original behaviour, found on the way)
- **P1** `quickCastSpell` calls `clickedScreen(null)`, which crashes with a null exception when the magic submenu is open. `Gui.cs:879/1013`.
- **P2** With extra spells filling all 10 scroll slots, the `/` spell-cycle key reads past the end. `Engine.cs:657`.

## Not checked
- Tables ScummVM loads from kyra.dat (monster modifiers, experience table, music map, sfx index): their source is not in the checkout.
- Pure drawing routines (block and VCN drawing, smooth-scroll variants) were checked only at the top level.

## Checked and equivalent (short)
- All 192 EMC script opcodes, the TIM opcodes and commands, and every spell function and damage table.
- Monster AI and movement (`updateMonster`, `moveMonster`, `walkMonster`, distance attacks), flying objects, and items (`makeItem`, `deleteItem`, pick up, drop, throw).
- Doors, passability, the automap, timer intervals, experience and level-up, and every saved character, item and global field.
