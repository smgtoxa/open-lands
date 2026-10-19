# Changelog

## 0.4.0

### The Imp's Pit, made reliable
Every floor is now played through by an automatic tester before it ships: every level of the game as the borrowed
level, floors 1 to 30, every puzzle, two layouts each, a save and load in the middle of the floor, and a run of 40
floors down in a row. Out of 6,760 floors, all can be finished. What it found and what was fixed:
- "Clear the floor" could never be finished: the gate waited for the guards beyond it too. Only what you can reach counts now.
- A key did nothing on a level without doors (its gate is a wall): click the gate with the key in your pack, door or wall.
- The key, the sigils or the switch could end up behind a hidden stone with no mark on it, or nowhere at all on a
  floor whose start side was a few corridors. The gate is placed so both sides are a real part of the floor, and
  what a floor needs always lies where you can walk (or behind a marked stone).
- Doors that only a lever elsewhere could open were used as the pit's doors: they never opened. Only doors you can click are used.
- A switch or lever drawn at the foot of a wall could not be clicked on some levels. Only clickable ones are used.
- Teleport traps could cut off part of a floor, sit beside a hidden stone, or lie on the only way to a lever. They now
  only go where the floor has another way round, and never by anything you have to stand at.
- Shortcut doors could be hung beside the gate, beside a hidden stone, or next to each other, sealing the way. Not any more.
- Monsters could walk into the pit's rock on some levels (and the master with them). The rock holds them now, and a
  level whose only creatures walk through walls gets no master (that floor is cleared instead).
- Monsters spawned facing an impossible direction could crash the game while drawing.
- On boss floors, opening the gate door by hand left the stairs shut after the master died.
- Sigils held in the hand or worn now count. A floor whose puzzle cannot be furnished opens its gate and says so.
- Floor five without a master still gives the Perfect Healing Potion for Timothy.

### Items kept safe
- Leaving the pit could damage your items: the borrowed level's monsters lost their loot, and the records were
  reused for the pit's own things, so an item could end up in your pack and on a floor at once. The game no longer
  reuses anything the party carries, a camp companion wears, or the borrowed level keeps.
- Loading a save repairs items an older version listed twice, or left tagged to a floor while carried.

## 0.3.1

- The character selection has its sound back, as in the original: the intro music, King Richard's greeting, each
  hero introducing themselves when you pick them, and the king's answer to your choice before the game begins.

## 0.3.0

This release is about faithfulness. Every rule of the game was compared with ScummVM's Lands of Lore code and then
with the original DOS game itself (`MAIN.EXE`, `FINALE.EXE`, `LANDS.EXE`, decompiled). Where they differ, Open
Lands now follows the original. 42 differences were fixed. From now on the Unity version is the only one: the web
and Godot versions are discontinued.

### Combat
- Monsters attack with their own skill. Their chance to hit was taken from whichever monster happened to sit in
  the first monster slots, and when that slot was empty the attack failed silently, so a hero could be untouchable.
- The difficulty now changes how well heroes dodge too, not only monsters, as in the original.
- When acid or a monster destroys a worn item, the item's bonuses are taken away properly.
- A boss's death script now sees it where it fell, so its drops and triggers happen in the right place.
- Monster sounds and ranged-attack counts behave as single bytes, as in the original, so a monster that loses its
  last ranged attack really stops shooting.
- Spark draws its randomness from the dice, as in the original; screen shakes use the original's own small
  random generator.

### Saving and loading
- Fixed a save bug that could make a pile of items on the floor loop forever and hang the game: save, drop an
  item on a pile, leave the level and come back.
- Saving finishes moving doors first, so a save never holds a half-open door.
- Monsters on levels you have visited are rescaled to the current difficulty, if you changed it since.
- A level is restored with every monster that was saved on it.

### The world
- Items that fall through a pit or are sent ahead by a script are no longer deleted when you reach their level.
  A quest item could be lost this way.
- A lever changes only the wall you face (it also changed the far side of the block), with the original's sound.
- Walls changed by scripts (secret walls, moving walls) show at once.
- The Gladstone gate guard keeps his post until his greeting has played.
- Walking into a wall drops the moves you had queued, as in the original.
- Clicking a wall decoration stops the current speech instead of playing a sound.

### Timing and controls
- During dialogues, spells, the inventory and level changes, doors and level animations now pause, as in the
  original, and messages keep fading.
- Poison, stun and paralysis no longer keep running during paused scenes.
- The game handles one input per step, so buffered clicks and moves no longer race through without the world
  moving in between.
- A left click on the inventory arrows scrolls by one slot (it scrolled by nine); with the spell-level menu open, a
  left click elsewhere acts as a left click.
- Hero status timers behave as the original's signed byte.
- A script's "react to a click" command handles the click as the original does.

### Party
- Resting heals and poisons at the original's pace.
- A rest broken by a click or key now also does what you clicked.
- HP and MP bars slide to their new value, as in the original.
- HP and MP changes reach the 4th party slot.
- The party's death plays the death music and stops the world behind the death menu.
- A new remark waits for the hero speaking to finish instead of cutting them off.

### Dialogue and text
- Dialogue boxes open fully before their text is printed.
- Long status messages page with MORE instead of being cut short.

### The ending
- The intro and the ending end on their own instead of waiting for a key.
- The ending is complete: the animated credits with monsters stepping out of the doors, the lead hero's final
  picture, the extra line for the hardest difficulty, and the hero's own congratulation voice. As in the
  original, Esc after the picture ends the game and any other key plays the ending again.
- The ending's text, cross-fades, animation frames and music fade work as in the original. A bug that could stop
  the ending part-way was fixed.
- The party's portraits are hidden while the ending plays.

### Sound
- Sound effects share the original's single sample channel with priorities: a quiet effect no longer cuts off an
  important one.
- The Ruby of Truth plays its own sound again.

### Fixes in Open Lands' own features
- Quick-casting a spell with the spell-level menu open no longer crashes.
- The spell-cycle key (/) no longer reads past a full spell scroll.

## 0.2

### New platforms
- macOS build (Apple Silicon and Intel).
- Choosing the game folder and importing saves now work on Linux (through zenity or kdialog) and on macOS.

### The Imp's Pit, reworked
- The pit no longer ends after one floor. Take the stairs down as deep as you like, and climb out whenever you want.
- Every floor is a full maze with a puzzle to solve before its gate opens: pull levers, find a hidden switch, carry
  the gate key, gather sigils, clear the floor, or beat its master.
- Hidden passages: secret stones open alcoves with loot, and some walls hide switches.
- Traps on the floor, guards behind the gate.
- Monsters grow stronger with every floor, from easy near the top to very hard deep down.
- Every 5th floor has a floor master: a real boss, larger and much tougher, with its own health bar. At half health
  it calls guards to help.
- No more open doors you cannot walk through.

### Saving Timothy
- The master of pit floor 5 drops a Perfect Healing Potion, once.
- Carry it to Timothy in the Northland Forest after the Draracle and you can give it to him instead of leaving him to
  die. He then joins the party, at the party's level.
- One chance only: without the potion the scene plays as in the original game.

### Companions
- When a new companion joins a full party (Timothy, Paulson), you choose who travels with you. The others wait in
  the camp, and the imp's Camp tab swaps them back in.

### Errands
- Errands now ask for monsters you can meet where you are (or have already fought), not ones from late in the game.
  The errand text names the monsters.

### Guide
- New article "Saving Timothy"; the Imp's Pit, Errands and Companions articles are updated.

### Fixes
- The pit's gate message no longer appears twice.
- Pit floors with many items no longer fail to fill.
- Errand monster lists are kept in saves.
- A party of one hero shows its Attack, Equip best and quick spell buttons again.
