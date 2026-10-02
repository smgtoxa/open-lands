# Lands of Lore: The Throne of Chaos — behavioral reference (port checklist)

Target: DOS CD version (GOG release). Every `- [ ]` item is meant to be testable in the port.

## How to read this

- **Sources** (full list at the end). Short tags used inline:
  - **[CB]** Official Westwood/Virgin *Clue Book* (1994), scanned on archive.org. Map numbers like `#12` refer to its per-level maps. Treat it as the primary source.
  - **[TR]** Tricky (Jeroen Broks) *Complete Walkthrough* v1.2, GameFAQs/SuperCheats.
  - **[DG]** Diana Griffiths walkthrough (1995; maps by Patricia H.), RPG Gamers / The Computer Show.
  - **[UHS]** Universal Hint System hint file.
  - **[AH]** Anonymous "Hints and Tips" (GameFAQs #1877 / Cheatbook).
  - **[WIKI]** Lands of Lore fandom wiki (NPC dialogue transcripts, shop prices).
  - **[TCRF]** The Cutting Room Floor.
  - **[SVM]** ScummVM `engines/kyra` LoL source. It is a reimplementation, so it is evidence of original behavior, not an authority on it.
- **(single source)** means only one source states it. **DISAGREE** marks conflicting sources.
- Directions ("north" etc.) follow the magnet-stone compass. Level ids like "level 16" are the internal numbers in the ScummVM source.
- None of the sources gives grid coordinates. Positions are given as Clue Book map numbers or as step-by-step routes.

### Scope corrections (areas from the request that are not in LoL1)

- [ ] **Not in LoL1:** Castle Gladstone basement, White Tower "swamp", Cimmeria "Hulk's area", frozen/ice caves, Belial, Ruins of Zanthia, Huline ruins and Beast Isle. Belial and the Huline ruins belong to *LoL2: Guardians of Destiny*. Zanthia is from *Kyrandia*. No source mentions any of these for LoL1.
- [ ] **Kieran, Michael and Conrad (with Ak'shel) are the four selectable champions, not companions who join later.** The companions who join in LoL1 are **Timothy, Baccata, Lora (optional) and Paulson**. **Dawn is cut content** and never joins the party.
- [ ] **Real area order:** Gladstone Keep → Northland Forest (+ Thugs' Hideout, Gladstone Marina) → Southland Forest (Grey Eagle Inn, Buck's Skins, Southland Marina) → Roland's Manor → Gladstone Keep (Scotia's attack) → Draracle's Caves L1–L4 → Northland Forest (Gladstone has fallen, Lake Dread) → Opinwood → Gorkha Swamp → Opinwood (Droek's wagon / Dawn) → Urbish Mining Co. + Urbish Mines L1–L4 → Upper Opinwood → Yvel Woods → White Tower L1–L3 + Sub-Level → City of Yvel → White Tower (Elixir) → Yvel Woods (false Dawn) → Opinwood (Droek dying) → Yvel (siege) → Catwalk Caverns L1 (L2 optional) → Dungeons (Xeobs/Knowles) → Castle Cimmeria L1–L3 → ending.

---

## 1. Intro, character selection, start state

### Intro cinematic (CD, voiced)
- [ ] A messenger rides to Gladstone Keep and the portcullis rises. The messenger hands a scroll to Geron, who is with King Richard. [DG]
- [ ] Geron: "My liege, it is as we feared. Scotia has uncovered the temple and will have the Nether Mask soon." Richard: "We must be ready for her. She will come here first." Geron: "...Surely we can arrange a defense against any charade." Richard: "The Mask is not a toy! ... I must destroy her now!" [WIKI]
- [ ] Scotia's camp scene. Scotia: "What can be taking so long? I want it now!" A minion presents the Mask ("Your unholiness!") and she kills him ("You will not make me wait again."). She says "Ha! It's a pretty little package!...", transforms into a young woman ("Ha! I like this!"), then says "...Beware King Richard! You will pay for your lack of vision!" [WIKI][DG]
- [ ] CD only: the main menu has "Lore of the Lands", a slideshow narrated by Patrick Stewart as King Richard. It includes a sepia shot of Roland's Manor intact. [WIKI][TCRF]

### Character selection
- [ ] Richard: "I have need of a champion. Who among you will volunteer to serve me in this matter?" If the player waits too long: "Well? Have you decided?" After a pick: "Excellent! Settle your affairs and attend me in the Throne Room for instructions." [WIKI]
- [ ] Four champions. The three preview stats come from the [SVM] static table `_charPreviews`. The labels (Magic / Protection / Might) are from [WIKI].

| Champion | Race | Magic | Protection | Might | Start equipment | Notes |
|---|---|---|---|---|---|---|
| Ak'shel | Dracoid | 15 | 8 | 5 | Dagger, Shirt, Sandals | Best mage; regains magic faster [CB] |
| Michael | Human | 6 | 10 | 15 | Dagger, Shirt, Sandals | Strongest fighter; learns magic slowly [CB] |
| Kieran | Huline | 8 [SVM] / 6 [WIKI] | 6 [SVM] / 8 [WIKI] | 8 | Dagger, Shirt | Strikes "nearly twice as fast" [CB]. **Cannot wear footwear** [WIKI] |
| Conrad | Human | 10 | 12 | 10 | Dagger, Shirt, Sandals | Jack of all trades [CB] |

- [ ] DISAGREE: [SVM] has Kieran's preview as 8/6/8 and [WIKI] has Magic 6, Protection 8, Might 8. Check against the original selection screen.
- [ ] Voices [WIKI] (single source): Ak'shel is Richard Smith, Kieran is Philip Shelburne, Conrad is Scott Lakin, King Richard (CD) is Patrick Stewart.

### Game start state
- [ ] Party of 1 (the champion). Start map is level 1, Gladstone Keep, facing north. [CB]: "Keep going north (the direction you were facing as the game began)."
- [ ] Spark is known from the start and can be cast **without a spellbook** (spell button, then pick a level) [CB][UHS]. There is no spellbook until Dawn gives the "mystic album" after Richard is poisoned. There is no automap until the Magic Atlas is picked up, and no compass until Philip or Roland's chest.
- [ ] [SVM] `startupNew()`: current level = 1, `giveCredits(41)`, three starting inventory items (item ids 216, 217, 218), spell slot 0 = Spark (single source for the numbers).
- [ ] Money is "silver crowns" (s.c.).

---

## 2. Walkthrough checklist by area

### 2.1 Gladstone Keep (first visit)
Scripted flow:
- [ ] **Keep guards greet the party near the entrance:** "Welcome to Gladstone Keep. King Richard awaits you in the throne room." [WIKI] After the greeting the guards **prevent leaving until you have seen the King** [CB]. If you try to leave first: "[Champion], were you not summoned to see the king? Go back straight north to the throne room." [WIKI]
- [ ] The sources do not describe the guard's movement animation (walk up / step aside). [CB] only says the entrance guards greet you and block the exit. **Near the Throne Room a "second brace of guards" reminds you to see the King. They stand in front of the golden Throne Room doors.** [TR]: "Approach the guards and they'll allow you into the throne room." Verify the exact trigger and animation against the original.
- [ ] Route [TR]: go forward and open the door. Two shops are on the right (weapons, herbs). Take the way left of the fountain. At the wall with the tapestry turn right and follow the corridor to the guards and the throne room.
- [ ] **Throne Room cutscene** [WIKI]: Richard: "I tell you we must march with whatever troops can be gathered locally!" Geron: "Isn't Eric mustering his White Army?..." Richard: "Eric is the finest commander... five day march from here!" Dawn: "As long as we have our own magic..." Richard: "Scotia is not an adversary to be taken lightly! Ah, [Champion]. ... retrieve [the Ruby of Truth] from Roland's estate in the Southland... Here is a key to my private library. Among the books you will find a Magic Atlas..." Geron: "Come by my office before you leave the Keep. I'll give you a Writ..."
- [ ] Receive the **Library Key**.
- [ ] Re-entering the throne room before you have the Ruby: guard asks "Have you found the Ruby of Truth?" Champion: "I have additional questions for his majesty." Guard: "You are not to be admitted without the Ruby." [WIKI]

Map points [CB]:
- [ ] #1 Guard at regular post.
- [ ] #2 Fountain. Sometimes holds a silver crown (random, low chance). Clicking gives flavor text: "The hidden plumbing seems miraculous." / "This must be fed by artesian well." / "What a fine fountain." [CB][WIKI]
- [ ] #3 **Royal Herbarium (Nathaniel)**. Prices [WIKI]: Salve 20, Ginseng 10, Aloe 5. Selling items: "Sorry, donations are not refundable." / "...we only accept coins as donations." Greeting: "Shh! Did you hear something? If Scotia does attack, they say we won't hear a thing!..." Clicking Nathaniel: "They say the Ruby and the Shard are the only means of opposing the Nether Mask."
- [ ] #4 **Royal Armory (Victor)**. Prices [WIKI]: Dagger 10, Mace 40, Rapier 60, Long Sword 125. Victor buys items [CB]. The "Commander's sword" is not for sale [WIKI]. Selling back to the vendor who sold the item pays the purchase price [TR] (single source).
- [ ] #5 Throne Room.
- [ ] #6/#7 Library Key lock opens the library door. #8 **Magic Atlas** on a pedestal in the SE corner of the library [UHS]. Clicking it adds the automap icon.
- [ ] #9 **Geron's office.** You must get the **Royal Writ** here [CB]. Geron on entering: "Oh, it's you. The impending storm attracts all you would-be heroes... Now, I suppose you want your writ." **A crow lands at his window** (this is Scotia) and he says: "Look, even the beasts seek shelter." On leaving: "If you need help, mayhap that rascal Timothy is at the Grey Eagle!" [WIKI]
- [ ] #10 Exit to the Northlands. Guard without the writ: "None shall pass without a writ." With the writ: "You may pass." [WIKI]
- [ ] Suggested buy: the Mace (you can sell the starting dagger) [UHS][DG].
- [ ] Floppy only: a password prompt when re-entering the Keep (copy protection). **The CD/GOG version never asks** [TR][TCRF].

### 2.2 Northland Forest (first time)
- [ ] Monsters: wild boars, thugs [CB].
- [ ] Items [CB]: #1 Swarm + (weathered dagger OR pouch with 5 s.c.) (random). #2 Nest: random aloe/salve/silver/Swarm. #3 Swarm. #4 Aloe in a hollow. #5 Rock. #6 Weathered dagger.
- [ ] #7 **Thug hold-up.** Choices: give in / bluff / defy. Giving in avoids the fight even with only 1 s.c. Bluff means you pay 1 crown and it may fail, and failure means a fight. Defy means a fight [CB]. [TR]: "Refuse and he'll fight."
- [ ] #8 Two thugs at a campfire. Choices: leave / sneak past into the hideout / attack [CB]. If you sneak, you may meet them inside or on the way out [CB].
- [ ] #9 Entrance to the Thugs' Hideout.
- [ ] #10 **Scotia as "Lady Bird"**: "Excuse me lad. Are you with King Richard? I need to get into the Keep, and I have forgotten the password. May I go in with you?" Champion refuses ("...these are dangerous times!"). She says "Timid fool! I will not forget you!", turns into a bird and flies off [CB][WIKI]. She has yellow eyes, which is Scotia's tell [TR].
- [ ] Optional: return to the Keep after this. Geron: "Why are you back here so soon?..." Champion: "I have just seen Scotia! She turned into a bird..." Geron: "...Our security will never let her in!" then "Get out! Do not return without the Ruby." [WIKI]
- [ ] #13 Draracle Caves entrance. It is sealed until Baccata is in the party [CB][UHS].
- [ ] #15 **Gladstone Marina (Lynn).** Hand over the Writ and you get free passage south. **Lynn keeps the Writ** [CB][WIKI]. Dialogue: "Where's your money?" / "I have a Writ from the King!" / "Well, where is it?" / "Well then, hurry and get on the boat!"

### 2.3 Thugs' Hideout
[CB] map; [TR]/[DG]/[UHS] agree:
- [ ] #2 Rock. #3 Lever opens the wall at #4. #5 **Lantern**. #6 Torn shirt + weathered dagger.
- [ ] #7 Hidden button (west wall) opens niche #8, which holds the **Thugs' Key** (for the chest #15).
- [ ] #9–#11 Vault. Plate #9 starts weighed down by a rock. **Take the weight off #9 and put two objects on plate #10** to open passage #11 [CB]. ([UHS]: take the rock from the east plate; two rocks on the west plate.)
- [ ] #12 Pressure plate that only opens #14 **temporarily**, and only the party's weight triggers it. It is a red herring [CB][UHS].
- [ ] #13 Secret button sequence: button on the **south** wall appears → press → button on the **north** wall appears → press → press the button on the **west** wall. Passage #14 opens **permanently** [CB]. ([UHS] order west→...; [TR] "south, north, north". Minor DISAGREE; use [CB].)
- [ ] #15 Chest (Thugs' Key): **lockpicks, salve, Bezel cup, 10 s.c.** [CB][DG]. #16 Pouch with coins.

### 2.4 Southland Forest
- [ ] Monsters: orcs, giant lizards [CB][TR].
- [ ] #1 **Southland Marina (Petricia**, Baccata's sister). First visit: "Did you forget something on the boat?" / "Are you Timothy by chance?" / "...probably down at the inn." Passage north costs **100 s.c. per passenger**. The Writ is not honored. Before Roland the champion says "I cannot return to Gladstone until I find Sir Roland." Declining the fare: "Perhaps you could swim." [CB][WIKI]
- [ ] #2 **Grey Eagle Inn.**
  - Click the **yellow-haired man at the left end of the bar** to get **Timothy** [CB]. Dialogue: "Are you by chance Timothy of Gladstone?" ... "Scotia! She is the most evil woman alive!..." "**I'll get my things together and meet you outside.**" Timothy **joins when you exit the tavern** [CB][WIKI].
  - Door on the right: **Philip**. "This room is occupied." ... "No, I am Philip. Timothy is the yellow haired fellow..." He gives the **Magnet Stone (compass)** only if you don't already have one. It shows on screen after you leave the tavern [CB][WIKI].
  - Timothy does **not** join if Roland's Manor is already cleared [WIKI] (single source).
- [ ] Timothy: Might 20, Protection 20, Fighter 3 / Rogue 2 / Mage 1. Rapier, Leather Jerkin, Buckler, Sandals [CB].
- [ ] #3 Swarm. #5 Rock.
- [ ] #4 **Buck's Skins.** Prices [WIKI]: Boots 10, Buckler 10, Salve 20, Bow 100 (arrows included). Buck does not usually buy things [CB] (WIKI says he takes back his own stock at the same price). Greeting: "hallo! Come in! I am Buck..." Leaving: "So long. Watch out for the Orcs!"
- [ ] This is the **only visit** to the Southlands [CB].
- [ ] #6 Entrance to Roland's Manor.

### 2.5 Roland's Manor
- [ ] Orcs in every room. Orc darts fly out when a door opens. Orcs will open doors and attack if you rest in a room [CB].
- [ ] On arrival with Timothy: "What happened? Someone has attacked!" [WIKI]
- [ ] #2 Rock. #3 **Fireplace: click 3 times** to get the **burned scroll** (Roland's notes; read by right-clicking it on a portrait) [CB].
- [ ] #4 Button opens niche #5: **aloe + two oil flasks** [CB].
- [ ] Clicking sconces: "These scones are firmly attached." Timothy: "I think you mean sconces." [WIKI]
- [ ] #6 **Orc Leader** (in Roland's old library, the right-most room [WIKI]). "Huh, huh! you small. What you?" Options:
  - **Leave**: the orcs throw you out and nothing changes. You never find Roland this way [CB].
  - **Bluff**: random chance, **influenced by Rogue skill** [CB]. Success: "You ignorant cretin! I'm Commander [Champion]!..." and he replies "Good. Me tired. I go now." Failure: "You trick me! You think I stupid?" and you fight him plus two goons [CB][WIKI].
  - **Fight**: same as a failed bluff.
  - He carries the Axe "Dominance" [WIKI].
- [ ] #7 **Touch the wall** to open Roland's hiding place. With Timothy: "This is Roland's hiding place. I hope he is in here." [WIKI] Alone: "Hmm, is that stone loose?" [WIKI]. [TR]/[UHS]: Timothy points it out. [UHS] says Timothy is needed; [CB] does not require him. DISAGREE, but minor.
- [ ] #8 **Roland dies.** "Timothy! The Dark Army passed through here... not an hour ago. They... have stolen the Ruby of Truth." ... "I... am beyond healing..." Timothy: "The war has begun..." Champion: "I vow upon his deathbed..." [WIKI]. [CB]: the Ruby was last seen heading for the Swamps.
- [ ] Roland gives his key. His chest holds: **magnet stone compass (only if you don't have one), a saber, a hoard of silver crowns** [CB]. That money is the fare home.
- [ ] Return trip: Petricia charges 100 s.c. per passenger (200 with Timothy) [UHS].

### 2.6 Gladstone Keep (second visit): Scotia's attack
- [ ] Entrance guard: "Do you have the Ruby of Truth?" / "The Ruby has been stolen! I must see King Richard!" / "Go in at once!" [WIKI]
- [ ] **Throne Room cutscene** [WIKI]: Dawn: "Leave him vile witch!" Scotia: "You cannot stop me! I found the Mask! My warriors have the Ruby..." Dawn: "Postpone your celebration, hag!..." Scotia: "Oh? The inept charmer threatens me??" After being hit by her own reflected magic: "You, Dawn, will be the first to grovel at my feet!" She flies away. [DG]: note the fallen cup (poison).
- [ ] Dawn: "He still lives, but we have no antidote... protective shield." Chant: "**Elvni Mekilno Connaax!**" "This shroud will prevent any further action of the poison..." [WIKI]
- [ ] Council present: Paulson, Geron, Nathaniel, Dawn, Baccata. Paulson: "I'm going down to the Urbish Mines and get to the bottom of all this mask business!" Dawn objects. Paulson: "Enough talk! We've got to do something now!" [WIKI]
- [ ] Clicking the pyramid keys: "Each of the Council has one of the four key orbs needed to open this enchanted shroud." [WIKI]
- [ ] Dawn sends you to the Draracle. **Timothy leaves** ("Farewell [Champion]... Baccata is a good man...") and **Baccata joins** ("Hello, we have not met. I am Baccata, Dawn's apprentice... I am familiar with the path to the Draracle"). Dawn gives the **Mystic Album (spellbook) with Heal** ("Your time has come [Champion], to walk the paths of magic...") [WIKI][CB][TR].
  - Variant with no Timothy: "Baccata will join you." / "Welcome Baccata. Let us find a cure!" / Dawn: "Remember Baccata, you are a guide only. Do not involve yourself in fighting!" [WIKI]
- [ ] Strip Timothy's gear before this scene. Whether his items drop or leave with him is not stated. [TR][DG] advise stripping him.
- [ ] Baccata: Might 20, Protection 20, Fighter 1 / Rogue 1 / Mage 3 [CB]. Start equipment: DISAGREE. [CB] says Staff, Helm, Leather Jerkin, Buckler, Sandals. [WIKI] says Staff, Kite Shield, Helm, Leather Jerkin, Boots. Baccata (Thomgog) has **4 arms: two weapon slots and two shield slots** [TR], and **cannot wear rings** [WIKI].
- [ ] Nathaniel after the poisoning: "All my potions are for naught!" Geron: "How did she get inside the keep? We change our passwords frequently!" [WIKI]
- [ ] Guard after the poisoning: "By Dawn's order, you may not enter without the Elixir." [WIKI] This is the **last chance to shop at Victor's in the Keep** [TR].
- [ ] Leaving: Baccata says "Do you recall seeing the caverns east of the Keep?... Let us bear east and then proceed north as soon as possible." At the cave: "**Connaax vur Talamari!**" and the cave opens [WIKI].

### 2.7 Draracle's Caves (4 levels)
Monsters: Bandits, Ratmen, Scavengers (L1). Cave Dwellers, Ratmen (L2). Flying Spiders, Scavengers (L3). Flying Spiders plus an armor-eating slime (L4) [TR].

**Level 1** [CB]
- [ ] #2 Lever toggles the passage at #4. [TR]: "horned face on the wall — move the horns." [DG]: "gold switch."
- [ ] #3 Lantern, **only if you didn't take the one in the Thugs' Hideout**.
- [ ] #5 Three buttons control the moving pits to the NE. **Order: blue (east wall), then green (south wall), then red (west wall)** [CB]. DISAGREE: [TR] says "any order" and [UHS] says "press all three". Use the [CB] order.
- [ ] #6 Pit. It is closed by the buttons but **re-opens once you pass the corridor near #9**. The first descent to L2 is by falling through it [CB].
- [ ] #7 Pressure plate holds door #8 open while weighted. #9 Lockpicks, ginseng, oil flask. [TR]: bandits come out when you step on the plate.
- [ ] #10 Plate triggers darts from #11. **The trap re-arms every time the weight comes off**, so drop an item on it before you step off [CB]. Lora warns about it if she is present [TR].
- [ ] #12 **A Ratman in this area carries the Emerald Eye** [CB].
- [ ] #13 Crumbly wall (south). Needs the Sledge. Lora comments on weak walls [TR]. [TR]: the first wall needs 2 hits, the second 3 hits (single source).
- [ ] #14 Oil flask, two ginseng. #15 Secret button opens #16. #17 Pit to the main part of L2 (the only way on). #18 Trapdoor from L2.
- [ ] #19–#21 After the Draracle: stairs up from L2. The lever on the north wall (#20) opens #21, the way out.

**Level 2** [CB + addendum card]
- [ ] #1 **Sledge** (east corner). Needed for the crumbly walls. [TR] says it must be equipped.
- [ ] #2 Landing from the L1 #6 pit. **Addendum:** the secret button on the north wall opens the way to the Pod Room. #3 A secret button on the east wall opens the way south to the trapdoor to L1. (The original book text swapped these two.)
- [ ] #4 **Pod Room.** Click each of the 3 pods. Champion: "Hey! There's something alive in there!" Baccata: "Shall we cut this one open?" **Lora** comes out of the right-most pod and joins [CB][WIKI]. Lora: "I will wait for you outside this dreadful chamber." Lora stats: Might 20, Protection 20, Fighter 1 / Rogue 4 / Mage 1, Torn Shirt [CB]. [DG]: characters take damage in the Pod Room [TR].
- [ ] **Lora window:** if you drop to the other part of L2 without entering the Pod Room first, the Pod Room seals and Lora is lost [TR] (see bugs).
- [ ] #6 Spin square.
- [ ] #7 Chest (pick or bash): two stars, oil flask (**destroyed if you bash the chest**), **Sapphire Eye** [CB].
- [ ] #8 Button opens the door west of #9. [TR] also mentions an infinite Cave Dweller spawn near a crossroads here [WIKI] (see bugs).
- [ ] #9 **Dragon Room.** **Emerald Eye in the EAST dragon opens the east route (Emerald Route). Sapphire Eye in the NORTH dragon opens the north route (Sapphire Route). Choosing one seals the other** [CB][TR][DG]. DISAGREE: the [CB] "Special Items" appendix says the reverse (emerald = north, sapphire = east) and calls the Jeweled Dagger the "Sapphire path" item. That appendix contradicts the book's own map text and every walkthrough. [DG] claims you can come back and take the other path. [AH] says "you can use both eyes, but will only be able to complete one side."
- [ ] **Emerald Route L2:** #10 Rock. #11 Leather jerkin. #12 Spin. #13 Chest: 10 s.c., salve, Treant stick. #14 **Pit-trap button: throw an item east over the pit to hit it; the pit moves to #14**, freeing the way to the stairs #16 [CB]. [UHS] warns that the middle pit position can trap you, so cycle it back. #15 Pouch + dagger "Stiletto".
  - #17 **"Dagger in... Dagger out."** Take the note from the east-wall niche. **Dagger in niche (niche vanishes) → press north button → lever appears; pull it → press button → lock appears; pick it → press button → niche appears; put any item in (e.g. the note) → press button → passage west opens** [CB]. [UHS]/[DG] give the same sequence with slightly different wording.
  - #18 Chest (bash; picking rarely works [UHS]): helm, **Freeze scroll, Jeweled Dagger**.
  - #19 The south wall becomes illusory after you enter #20. #20 Spin. #21 Secret switch → niche with 2 ginseng. #22 Lever triggers a fireball trap. #23 Stone-shaped button opens the way south, which closes behind you. #24 Star "Shining". #25 Button opens north. #26/#27 Return stairs after the Draracle.
- [ ] **Sapphire Route L2:** #13 Chest: 10 s.c., salve, Treant stick. #14 Secret passage. #16 Chest: aloe, mace "Bouncer", **Silver Goblet, Freeze scroll** [CB]. #17 **The button triggers a fireball trap; don't press it** [TR][CB]. A rock lies there. #18 Pit: **throw west to hit the button; the pit closes; take the Worn Key** from the north niche [CB]. [TR]: "Throw a random item over the pit and the pit will close."
- [ ] The Silver Goblet heals the whole party to full for a little magic. The Jeweled Dagger is a strong weapon. **Only one is needed, as the offering** [CB].

**Level 3** [CB]
- [ ] Emerald: #3 Secret button → niche with aloe + dagger "Razor". #4 **Iron Key** (opens chest #7 or #15, only one of them). #5 Empty flask. #6 Illusory pit. #7 Chest: coins, rapier "Dicer", scale mail, oil flask. #11 Axe. #12 Saber "Cutter". #14 **Red Lock** ← Red Key from #15. #15 Chest: **Red Key**, aloe, boots, oil flask. #17 **Weight on the plate closes the niche to the west; a dagger put in that niche becomes the dagger "Backbiter"** [CB] (single source). #18 Stairs to L4. #20 Draracle's Lair.
- [ ] Sapphire: #2 **Worn Key lock opens east; the passage closes behind you** [CB]. [TR] adds that Baccata says the pit is an illusion, **or Lora says the wall closed behind you** (random line). #3 Pouch, 3 s.c. #4 Dagger. #6 Secret button → niche with a Freeze scroll. #7 **Illusory pit.** #8 Empty flask (needed for the Elixir). #9 Chest (Iron Key #11): 2 oil, rapier "Ripper", scale mail. #10 Star "Shining". #11 **Iron Key** (opens #9 or #19). #12 Button opens #13. #14 Secret passage (axe behind it [TR]). #16 Saber "Cutter". #18 Red Lock. #19 Chest: **Red Key**, aloe, boots, oil. #21 Dagger → "Backbiter" niche. #22 Stairs to L4.
- [ ] [UHS]: the empty niche on L3 does nothing.

**Level 4** [CB]
- [ ] #2 Chest (pick or bash): crossbow + 10 s.c.
- [ ] #3 Triggers a fireball trap. #4 Loose stone on the west wall opens a passage ([TR]: a "V" switch; [DG]: "crooked brick").
- [ ] #5 Triggers fireballs from the west-wall dragon #6. Chest near it: salve + **Bezel ring** (poison immunity) [CB]. [UHS]: reach the chest by smashing the crumbly wall NE of it instead.
- [ ] #7 Crumbly wall (either side). #8 A button **arms** plate #9: stepping on or off #9 fires from the north.
- [ ] #10 Rock. #11 Crumbly wall north.
- [ ] #12/#13/#14 Plates that fire from W/E/S. **Throw a rock onto the plate from the passage to the south, wait for the fireball, then walk.** These plates react to a **change** of weight, not to weight [UHS].
- [ ] #15 Secret button (west) → niche: **24 s.c. + the items you left in the "dagger in/out" niche** (Emerald Route) [CB].
- [ ] #16 Stairs up to the Lair (NE of L4) [CB].

**Draracle's Lair** (entered on L3)
- [ ] Strip Lora's equipment before entering [TR][DG]. She stays behind.
- [ ] Champion: "Excuse me, we are seeking a cure..." **Jakel**: "Peasants! How dare you bother the master before placing an offering! Go through the archway there, and place your trinket on the altar." [WIKI]
- [ ] Click the archway and a small closet opens [TR]. A wrong item gives Baccata: "I think they want a different offering." Silver Goblet or Jeweled Dagger gives "Splendid!..." [WIKI]
- [ ] Draracle: "Only the Elixir of Tybal can cure... can only be created in an ancient white tower." Baccata: "Could you be a bit more specific?" Jakel: "...present another gift." Draracle: "No! I will answer their query..." Then the **four riddles**: (1) butcher the creature whose flesh has never lived, (2) see the sweetness of your enemy, (3) collected from the deadly depths, (4) powders taken from the heart of your mother. You get the **Riddle Scroll** [WIKI][CB].
- [ ] If Lora is in the party: "Hi! I'm Lora... Where do I go now?" Jakel: "When your companions are finished, you shall wait here." Then the farewell. **Lora leaves** [WIKI][CB].
- [ ] On exit you land on L2. Baccata: "Bad luck!..." Champion: "Let's return to Gladstone..." [WIKI]. A faster exit is now open (L1 #19–#21) [UHS].

### 2.8 Northland Forest: Gladstone has fallen
- [ ] **Orc ambush.** The dead orcs drop a coin sack that the Champion recognizes as Timothy's [WIKI][TR].
- [ ] #11 **Timothy dying on the road** (this happens whether or not he was ever recruited [TR]). Baccata: "Timothy! What happened?" Timothy: "...Gladstone has fallen! Scotia and the Dark Army have stolen King Richard's body!..." "...this arrow is... quite annoying..." "...you must find the Council. Only their four keys can open the Shroud..." Champion: "Shouldn't we assist him somehow?" Baccata: "Death is so often embarrassing. Let us at least leave the poor man with his dignity." [WIKI][CB]
- [ ] The Keep is destroyed and cannot be entered. The Marina is destroyed ("I hope that tart enjoyed the Orcs' visit!") [WIKI][TR]. You can never return to the Northlands after leaving [TR].
- [ ] #14 **Lake Dread / Victor.** Route [TR]: from the caves go south to the trees, one step east, continue south, west at the trees, **first way south** (you hear "Psst" twice), at the end turn east. Victor: "Psst" / "What was that!" / "Psst" / Baccata: "I think there's someone in the bushes there." / "...Dark Soldiers everywhere." ... "Same boat take Dawn just came back..." "...You visit the Yvel city in a few days, look for me, no?" **He gives a Long Sword** ("Here, maybe you like this, no?"). The boat (Dom rows) takes you to Opinwood [WIKI][CB].

### 2.9 Opinwood
- [ ] Monsters: Pentrogs (ranged crossbows; Spark IV is effective), orcs [CB].
- [ ] #1 Lake Dread landing. #2 **Orcs near the drop-off carry the mace "Puma"**.
- [ ] #3/#4/#5 Hollow / stump / nest: random items. #6 Secret passage. #7 Rock. #8 Star "Shining".
- [ ] #9 Chest: salve, bracers, Bezel cup, saber "Gutter". #10 Long sword "Flayer".
- [ ] #11 **Stump that may hold a Green Skull** (for the Larkhon) [CB]. [DG]: "green skull out of the gnarled tree."
- [ ] #12 **Beggar (Frank).** "Spare this poor traveler a few coins?" Options [CB][WIKI]:
  - **Help**: pay **5 s.c.** Later, clicking the Riddle Scroll on him writes the **swamp water** clue onto the scroll ("...the deadly depths could refer to walking through the swamps. Maybe swamp water has magic properties.").
  - **Leave**: "Please! I beseech thee!" / "I've got my own problems."
  - **Attack**: he can't fight back. Baccata: "That was not much of a battle." He drops a single crown [WIKI].
  - He later moves to Bruno's Lodge in Yvel (see 2.16).
  - [TR] (single source): if you did not help him, he **sells** the cloak in Yvel "for a very high price". [CB] only says he gives it if you aided him.
- [ ] #13 **Chest: "Kane" leather jerkin + Lightning scroll** (far NE corner [UHS]).
- [ ] #14 Stump, may hold a Bezel Ring. #15 **Droek's wagon**. #16 To the Swamp. #17 To the Urbish Mines.
- [ ] **Droek (first visit, no Ruby):** Baccata: "Good day, sir. Have you seen a woman by the name of Dawn?" Droek: "E'er I was to know, would I be telling da likes of ye?"... "I'd not trust me own mudder wit'out proof..." You cannot pass [WIKI][CB].
- [ ] The Opinwood route from the Swamp to the Mines and Droek is step-by-step in [TR] §Opinwood II. It includes "go east through 2 illusions" and "the trees are an illusion".

### 2.10 Gorkha Swamp
- [ ] **Never hurt a Gorkha, even by accident.** Killing or attacking one, or a failed theft, makes the **whole tribe hostile**. Merchants then refuse service and the Mask quest is lost [CB].
- [ ] Sinkholes (marked on the map) **kill the party instantly**. **Freeze (any level) freezes them.** The atmosphere changes color and higher levels last longer [CB]. If the thaw happens while you stand on a pit you survive until you step into a pit again [TR]. Gas squares cause sleep (fix by resting) or poison [TR][AH].
- [ ] Monsters: Boglytes (weak to Freeze), Hurzels ("living sticks", weak to Spark/electric) [TR][CB].
- [ ] **Entry sentry:** "Bunda! Identify yourselves!" / "We are from Gladstone." / "If you come in peace, you are welcome. But be warned!..." Its appearance is randomized. It can be killed, which turns the tribe hostile [WIKI].
- [ ] #2 **Swamp water:** click an empty flask on a sinkhole. The swamp must NOT be frozen [TR] (single source).
- [ ] #3 Chest: axe "Slitter", **Duble Ring** (better HP recovery), salve. #4 Staff "Beater". #6 Chest: dagger "Assassin", oil flask.
- [ ] #7 **Gorkha guard:** "Bunda! Remove your weapons before entering the chieftain's compound." Move **all weapons out of the hand slots**. The guard retreats and you may re-equip afterwards [CB][TR].
- [ ] #8 **Chieftain Mathpasha.** "Ah, you come to trade with the Gorkha, no?"... "...beautiful red stone we take from Orcs. What will you trade me for it?" **Any offered item is refused** ("I don't want that thing..."). He then offers the deal: kill the Living Sticks that stole the **Brass Helmet / Ceremonial Mask**. Options [CB][WIKI]:
  - **Accept**: "Good! Bring the helmet as proof."
  - **Steal**: chance scales with Rogue. Success: "Hmm.. that's odd! I know I had that stone here somewhere!" Failure: "Thieves! Guards!..." and the tribe goes to war.
  - **Attack**: "Guards! To arms!" Killing him gives the Ruby, but all merchants stop dealing with you.
- [ ] #9 **The Hurzel carrying the Mask** is in the central area past a cluster of three sinkholes (Freeze needed) [CB][TR][UHS].
- [ ] **Return the Mask:** "...From this day forward you shall be known as Heroes of the Swamps. Take the Ruby..." He also gives the **trident "Mantis"**. **Swamp merchants now give a 50% discount** [CB][WIKI].
- [ ] #10 Dwarvish buckler. #12 Glint mail. #13 Bronze horseshoe (worthless). #15 Bow "Scout". #16 Pouch with 4 s.c.
- [ ] #11 **Idol Ra'Tol the Fortunate:** put an item in her mouth and click the nose. The item turns into a purse. [CB] says the purse is 60 s.c. [WIKI] says it is **always exactly 60 s.c.**, not the item's value. **She stands over a sinkhole, so cast Freeze first.** She won't take important quest items [AH].
- [ ] #14 **Idol Ba'Del the Healer:** item in mouth, click the nose. Heals the party, restores all magic, and keeps the item. Also over a sinkhole [CB].
- [ ] #17 **Grent, blacksmith**: kite shield, trident, great sword, great axe [WIKI]. East of the three-pit field [TR]. Greeting: Baccata "I have plenty, thank you." Champion: "A Thomgog jester, is he not?"
- [ ] #18 **Witch Doctor:** decodes **one** riddle for **100 s.c. (50 as Heroes)**. His clue: "...the only enemy's sweetness that I'd taste in the nectar from a Giant Hornet." He sells **Fireball wands for 300 s.c.** [CB][WIKI].
- [ ] #19 **Scomish, fletcher**: bow, great bow, crossbow. For Heroes he sells "everything at my cost" [WIKI]. Fletchers pay about 2x for ranged weapons [TR] (single source).
- [ ] #20 To Upper Opinwood.

### 2.11 Opinwood again: Dawn at Droek's wagon
- [ ] Holding the Ruby: "I hold the Ruby of Truth, and as you can see, it does not contradict me..." Droek: "Dou art truly [Champion]. Milandy Dawn dwells within..." [WIKI]
- [ ] **Dawn** gives **her Pyramid Key** ("Take my key to Richard's casket..."). Two variants depend on whether Paulson is already in the party [WIKI].
  - Variant with Paulson present: Dawn tries to keep Baccata ("...this violence interferes with your training! You must stay here with me!"). Baccata refuses ("I will not abandon my friends, nor my King!"). Dawn: "Fine!..." [WIKI]
- [ ] **Show the Riddle Scroll:** "...the mother of which he speaks is Mother Earth. There is a mystic reagent called Earth Powder." The clue is written on the scroll [WIKI][CB]. Showing it again: "I can discern no new information from this."
- [ ] **On leaving:** "...take these vials..." (**empty flasks**). **"Perhaps this Vaelan's Cube will help you as well."** She gives the cube only in the Paulson-already-joined case [WIKI][TR].
- [ ] Cube rule: the first cube is in **Paulson's cache if you met Dawn first**, or **given by Dawn if Paulson joined first** [TR][WIKI]. DISAGREE: [CB] says Paulson's cache has the cube "only if you have not found the great maul 'Hammerhead'; otherwise Dawn will have it". This looks like a Clue Book error. Check the original.
- [ ] Returning before the Elixir: "Have you found the Elixir?" / "I am making progress on my spell. Return when you have the Elixir." [WIKI]

### 2.12 Urbish Mining Co. (office level, "level 0") and the Larkhon
- [ ] #1 To Upper Opinwood. #2 To Opinwood. The office is a **shortcut between the two forests** [DG].
- [ ] #3 **Larkhon.** It is released the **first time you open this door**. It is invulnerable to almost everything except acid. The **Green Skull** (right-click on a portrait) casts **Caustic Fog** for 15 acid damage and needs magic points. It takes about 5 uses [CB][AH]. The Larkhon **disarms** you and can knock the skull away. Stolen items come back after the fight [UHS]. Alternatives: many thrown weapons [TR][CB], or leave by one forest exit, loop through the swamp and come in by the other one to get around it [CB].
- [ ] #4/#5 Stairs within the office. #6/#7/#13 Desks/cabinets with random items. #18 Cabinet: salve. #17 Pouch, 2 s.c.
- [ ] #8 **Chest: Silver Key + mace "Bouncer"** (SE, behind the airlock) [CB].
- [ ] #19 **Airlock doors: one must be closed before the other opens** [CB][UHS]. [TR]: go through the south door, close it, open the west door.
- [ ] #10 Lockpicks. #11 Helm. #12 Button on the east wall reveals a passage.
- [ ] #14 **Dwight the clerk** ("At last! You must be the rescue party!"). Take the **Great Helm and mining Pick** from under his desk ("I'll sign that out to you right away."). After the Larkhon is dead he says he'll leave after finishing his reports [CB][WIKI]. He can be killed with no effect [WIKI].
- [ ] #15 Lock must be picked. #16 **Old pump machine**: needs a gear (L4 #23) and coal (L3 #22). Open it, put in the coal, close it, fit the gear, pull the lever. This **drains the flooded stairs on L2 #43** [CB][TR].
- [ ] **Fireball scroll** somewhere in the office. [CB] says it is in a chest. [TR] says the spot is random and it may not appear. [UHS] found it only once ("may have been a random occurrence"). DISAGREE: treat it as a random spawn.

### 2.13 Urbish Mines L1–L4
**Monsters** [TR][CB]: office level: Larkhon, "mine crab". L1: Gimlets ("little guys", poison), Iron Grazers. L2: electrical wisps/Necrosaps, worms. L3: Avian worms (nests), Rocklings. L4: Rocklings, crabs. Rocklings are weak to **Freeze** and blunt weapons [CB][UHS].

**Level 1** [CB]
- [ ] #2 Note to Orin. #3 **Silver Key lock** (key from office #8).
- [ ] #4 **Wheel levers + button #5.** N and S both up or both down: a pit appears, no teleporter ([UHS]: a monster spawns, which you can farm). **N up / S down: teleporter to L2 #36 (secret-wall hub).** **N down / S up: teleporter to office #13.** The button **also opens a pit one square north** [CB][UHS][TR]. [AH]: "right wheel up, left down".
- [ ] #6 Secret passage. #7 **Sign says disarm yourself. There is a rock at #7 (it counts as a weapon); throw it away from the sign to open the secret wall at #17** [CB]. [TR] says to ignore the sign.
- [ ] #8 **Emerald Blade, Mine Key 2, bones**. #9 Stairs to L2 #39. #10 Lock (Mine Key 2) opens the door 1W 1S.
- [ ] #11 Lever on the east wall opens a pit under the party (to L2 #45). #12 Lever on the south wall toggles pit #13.
- [ ] #14 South niche: **Mine Key 2. Taking it opens pit #13. You must throw Mine Key 2 north to close it** [CB]. ([UHS]: once you take the key you must jump down.)
- [ ] #15 Lock (Mine Key 2) opens #16, a secret passage that is **one-way W→E until unlocked**.
- [ ] #18 Mace "Puma" + saber "Wolf". #19/#20 Carrying the #14 key while walking on #19 closes pit #20. #21 **This lever starts down and reverses #20. It breaks after one use.** #22 Mine Key 3. #23 Chest: "Hale" leather jerkin + maul "**Hammerhead**".

**Level 2** [CB]
- [ ] **#36 hub** (arrival by the L1 teleporter): **all 4 surrounding walls are illusions** [TR][DG].
- [ ] #1 Chest: Dwarvish helm, Bezel cup, star "Shooting", 25 s.c. #2 Chest: oil, ginseng, empty flask, bracers. #3 Chest: oil + **Lightning scroll** (north branch).
- [ ] #9/#10 **Weight on the plate opens a niche: Mine Key 4** (south branch). #11 Mine Key 4 lock opens passages NE and SE (west branch).
- [ ] #12 Orin's note to Geof + bones. #13/#14 Buttons open walls east.
- [ ] #15 **Gas smell.** Stand at least 3 squares back ([TR]: 2 back; [DG]: 1–2) and throw a Fireball (spell level II or higher [CB spellbook], or a wand) north. The explosion **destroys wall #16** and releases electrical wisps [CB][TR]. #5 **Mine Key 5** is behind it. #6 Mine Key 5 lock.
- [ ] #17 Plate closes and locks the west door. **It reopens once you hold the Fireball wand from #4** [CB].
- [ ] **Fire-jet parity puzzle** [CB]: plates #18, #20, #22 control jets #19/#21. An **odd** number of those plates pressed turns #19/#21 **off**. Plates #22, #23, #25 control jets #24/#26. An **odd** number turns those **on** (#24/#26 fire when 1 or 3 of #22/#23/#25 are pressed). Plate #20 is also in the second group per the #20 text. [DG]/[UHS]: they just ran through and healed. #4 Niche: **Fireball wand**.
- [ ] #27 **Vault combination: pull the west lever, then the east, then the north.** The passage north opens [CB]. ([AH]: "sides down first then the center". [UHS]: west wheel → east wheel appears → north wheel appears.)
- [ ] #28–#32 **Double-door trap** [CB]: #29 button closes #28. #30 plate + 2 levers. Door #31 opens if #28 is closed, the plate is weighted, **left lever down, right lever up**. Door #28 opens with the plate weighted, **left up, right down**. #32 button closes #31 **and fires arrows from the north**, so throw an item at it from the east. [UHS] gives the opposite order (left up/right down, then press the west button, loot, then reverse, then throw at the east button). The lever states agree, only the narration differs.
- [ ] #33 Button opens a pit under the party (to L3 #31). #37/#38 Secret buttons open north. #39 Up to L1 #9. #40 Landing from L1 #20.
- [ ] #41 **Plate weighted + button activates teleporter #42 to the office** [CB][TR].
- [ ] #43 **Flooded staircase. Passable only after the pump runs.** It leads down to the Shiny Key [CB]. #44 Sword "Flayer". #45 Landing from L1 #11. #46 **Plate opens pits #47** (to L3). #48 Landing for L1 #4.

**Level 3** [CB]
- [ ] #1 Landing from L2 #47.
- [ ] #2 **Spin square + button: the button opens a north niche with the "Shiney" Key** [CB]. [TR] precise sequence: face west, step forward, turn right twice, press the button, turn left, take the key.
- [ ] #3/#19 **Avian worm nests: Fireball each nest to stop spawning.** [UHS]: there are 3 nests ("smell like rotten flesh").
- [ ] #4 Button opens north. #5 **Plate fires the missile at #6 once only.** #7 Bones + **Pick**. #8/#9 Incomplete wall: pick through it. #10 Oil flask. #11 Secret button opens #12.
- [ ] #13 **Teleporter to the office.** #14 Stairs to L4 #25.
- [ ] #16 Niche with the **Silver Key** (landing of L2 pit #54). #17 Keyhole opens #18.
- [ ] #20 Pick this lock (door #32 NE). #21 Note "**PISCATA ROSEA 4 4 5**" is a **red herring** [CB][DG].
- [ ] #22 **Coal seam: use the Pick on the south wall.** [TR] gets 5 coal pieces. Only 1 is needed [DG].
- [ ] #23 **Mine-cart levers:** both down (initial) = locked. **Left up / right down = east path. Right up / left down = west path (to the stairs to L4). Both up = south path (to the coal)** [CB][TR][AH][DG]. [UHS]: 3 of 4 settings go somewhere.
- [ ] #24/#25 **Door to the cart opens when the gold jewel from #28 is placed in the gem hole #25** [CB]. #28 Chest: gold jewel + empty flask.
- [ ] #26 Cart: set the levers, get in, click forward. #27/#29 Buttons open N/E. #30 Secret passage. #31 Landing of the L2 #34 pit.
- [ ] #32 **This door locks behind you unless the secret plate #33 is pressed** [CB]. [TR]: "the door will close behind you (it'll open again soon)".

**Level 4** [CB]
- [ ] #1 **Spin square surrounded by wheel levers. Each lever adds 90° of spin. All levers down stops it** [CB][UHS][AH].
- [ ] #2 Stepping here triggers **rock slide #20**.
- [ ] #3 Button activates the teleporter to #4. #4 **The west-wall button disarms missile hole #8** (or it fires when you walk on #5/#7) [CB][TR].
- [ ] #9 Rock slide: Pick. #10→#13 teleporter. #11 teleporter to office #5. #12 teleporter to L2 #36. (Cluster of 3 [UHS]: east = nowhere, north = office, south = L2.)
- [ ] #14 Keyhole opens the east door. **The key is Paulson's (Mine Key 4 in his cache).**
- [ ] #15 + #16 **Both keyholes: #15 Shiney Key, #16 Rusty Key. Door #17 opens only when both are used** [CB]. [TR]: "rusty key in the red lock, shiny key in the blue lock".
- [ ] #19 **Orin's ghost.** Examine the bones: first a warning, then examine again for the **Rusty Key** [CB][TR]. It is behind a rock pile (pick) [UHS].
- [ ] #21 **Fireball trap that fires constantly, plus a spin square** on the way to Paulson [CB][TR].
- [ ] #18 **Paulson joins** (internally level 16, block 0x225 [SVM]). "Lads! I thought you were lost!" / "We have discovered a recipe..." / "I'll join you then. We'll need this Key." You get **Paulson's Pyramid Key**. If shown the Riddle Scroll he gives a clue [CB special items][TR].
- [ ] Leaving his room: "**Lads, a secret button to the south hides my equipment!**" Facing it: "**Let the button be revealed!**" [WIKI]. **The button appears only when Paulson is in the party** [TR bug letter].
- [ ] #22 **Paulson's cache:** Vaelan's Cube (conditional, see 2.11), Dwarvish boots, chain mail, kite shield, axe "Vixen", **Mine Key 4 (opens #14)**, great helm, two oil flasks, Bezel cup [CB].
- [ ] Paulson: Might 20, Protection 20. DISAGREE on skills: [CB] (OCR) reads Knight 3 / Mage 3 with Rogue garbled, [WIKI] says Fighter 5 / Rogue 2 / Mage 3. He starts with a Leather Jerkin, Buckler, Boots [CB] ([WIKI]: Shirt, Boots).
- [ ] #23 **Water-pump note + Gear** (Hank's / "Hank's bones" [DG]). #24 Teleporter to #3. #25 Up to L3 #14.
- [ ] #26 **A Rockling here drops the Bloodstone** (Elixir ingredient #1). **L3 rocklings don't count** [TR] (single source). [DG]: keep one "red heart".
- [ ] Exit: the north teleporter goes to the office, then go north to Upper Opinwood [CB][TR].

### 2.14 Upper Opinwood
- [ ] Monsters: Giant Hornets (fire works), Molders [TR][CB].
- [ ] #2/#9 Stick. #3/#7 Bird nest. #4/#5/#6 Hollows (+ rock). #16 Rock. #19 Dagger "Assassin".
- [ ] #8/#15 **Hornet nests: use an empty flask to get Hornet Honey** (Elixir ingredient #2). A Swarm is also here [CB].
- [ ] #10 Chest: **Jade necklace** (+1 Rogue), 28 s.c., Bezel cup.
- [ ] #11 Chest: Ebony staff (5 Swarm charges), 10 s.c., **Emerald Blade, Green Skull**.
- [ ] #12 Chest: **Crossbow "Valkyrie"** (fire arrows), Dwarvish chain mail, 23 s.c. A Molder nearby may drop a **worn key** that "might not work" [CB]. #13 Empty chest.
- [ ] #14 **Scotia's barrier.** Scotia appears: "You fellow have become bothersome. Don't press your luck... I have business in the tower now, and I do not wish to be disturbed." Champion: "I wish she would just stay and fight!" She raises a barrier [WIKI]. **Use Vaelan's Cube on it (right-click on a portrait, possibly several times) until it breaks.** This uses up the cube [CB][TR][UHS].
- [ ] #17 To the Urbish Mines. #18 **Yvel Cave** to Yvel Woods.

### 2.15 Yvel Woods
- [ ] Monsters: Great Orcs [CB].
- [ ] #3/#4 **Vulture's Chasm.** The Dark Army always cuts the bridge to Cimmeria. **A Great Orc in this area drops a Vaelan's Cube** [CB][TR]. [TR]: if not, try the small area directly south. [UHS]: keep fighting giant orcs until one drops. Paulson here: "Yep. That's Castle Cimmeria." [WIKI]
- [ ] #1 **Barrier to Yvel City.** Using the cube here **destroys** it. Do the White Tower first, because the L3 ghosts need the cube [CB]. [TR] warns strongly that you need the **Crucible of Faith** before using the cube. DISAGREE on how bad it is: [UHS] says emerald blades or the Valkyrie can beat the ghosts instead.
- [ ] #2 White Tower. #5 Chest: **Shield of Stealth** + arbalest "Equalizer" (**cursed, always misses**). #6 To Yvel City. #7 To Upper Opinwood. #8 Stump.
- [ ] #9 **False Dawn (Scotia).** She appears only **after Geron sends you to find Dawn** (after the Elixir). She asks for Dawn's key. She has **yellow eyes; the real Dawn's are blue** [TR]. Three options [CB]:
  - **Give the key**: she takes it and leaves. It can be found again later in the Cimmeria L3 humanoid-figurine niche (#16) [CB][TR][AH]. **Early releases have a game-breaking bug here where the key is lost** [TCRF].
  - **Argue**: she becomes a Giant Lizard and fights. **At more than 50% damage she changes form and flies away** [CB]. [TR]: in lizard form she can cause an earthquake that disarms you, and she flies off as a crow. [UHS]: an Ace of Oblivion helps.
  - **Fight**: same as arguing.
  - [DG]: "Leave when you encounter her."

### 2.16 White Tower
Monsters: L1 Amazons (weak to Freeze), Archer slugs, Queen Jana, Starks. L2 fireball "birds"/"chickens", Minotaur (side area). L3 Wraiths and Apparitions (pass through walls, interrupt rest) [TR][CB].

**Level 1** [CB]
- [ ] #14 **Slimy grates: close them to stop Archer slugs spawning** [CB][TR].
- [ ] #1 Rapier "Talon". #2 A warrior drops the **Brown Mystic Key** (opens the door near #5). #3 Star "Vega". #4 Empty chest. #5 Grey Mystic Key. #6 Lockpicks, dagger. #7 A warrior drops a **Grey Mystic Key** for #16. #10 Chest: Dwarvish great helm + **Jade necklace**. #11 Button triggers a bolt trap.
- [ ] #16 Mystic lock (Grey key) + #17 **button opens the door after the key is placed** [CB][TR].
- [ ] #8 **Lyle** (prisoner; Baccata: "I recognize him. That is the thief that was banished from Gladstone last year!"). Options [CB][WIKI]:
  - **Help**: he gives crowns. Each repeated "help" gives a different payout and dialogue. The Amber Ring comes on the second help if you first chose "leave".
  - **Kill**: you find a pouch of crowns.
  - **Leave**: he offers the **Amber Ring**. Leaving again brings more pleading. Coming back: "So, you have returned for the ring?"
  - Otherwise the ring is in a niche on the **west wall, revealed by the button on the north wall** of his cell [CB].
  - He promises 10 s.c. but may give less. The third-help text says "...the 5 crowns I promised" [WIKI].
- [ ] #13 **Niche with the note "Ring for Admittance." Put the Amber Ring in and a passage to the left opens** to Jana. Take the ring back afterwards (+5 protection [AH]) [CB][UHS].
- [ ] #9 **Queen Jana.** "So! You dare challenge the power of Jana, Leader of the Amazons!..." She **may** drop the great sword "**Trouble**" ([TR]: sometimes not dropped). Niche: **Brown-Grey Mystic Key** (for L3 #14). **Taking it opens the wall to the west** [CB][TR].
- [ ] #12 **Ivory Key lock** leads to the Sub-Level (#21 stairs). #15/#20 Pickable locks. #18 Mystic lock (key from L2 #2 or #4). #19 Mystic lock (lockpicks or key #2).
- [ ] #22 **Altar DeBlanca** (behind a mystic door, north part of L1 [UHS]). See "Creating the Elixir".

**Level 2** [CB + addendum]
- [ ] #1 Light-blue Mystic Key (for #6). Reached by the eastern stairs from L1 [UHS].
- [ ] #2 **Dark room: close the door, then press the button** to reveal a niche with a Mystic Key [CB][UHS][TR]. With no closed door there is no niche.
- [ ] #11 **Fireball room: the traps fire only when you step onto a square while FACING a hole.** Back in facing east until you hit the wall, turn, back into the south alcove, grab the **Fireball scroll** (#3), turn west, move along the wall to the north niche (#4, **Mystic Key**) [CB][UHS][TR].
- [ ] #5 Brown/grey Mystic Key (for #20), an illusory pit, and a button that closes pit #13. #6/#7 Locks.
- [ ] **Pit room** (#7 area, entered by picking the east lock; the door closes behind you) [TR]. [TR] grid (S = start, `*` illusion, `.` real pit, `#` wall):
  ```
    A B C D
  1 * * * .
  2 . . * .
  3 * * * .
  4 S . . #
  5 # . # #
  ```
  There is a Mystic Key on A1. Pit A2 drops to L1 room #3 (chest with a **Jade Necklace**, and a switch opens the door) [TR]. [UHS] route from the entry: N, E, E, N, N, W, W. Take the key and press the west-wall button, then E, E, S, S, W, S, S and press the south-wall button. This closes the NE pit and opens the secret wall to the other half. [AH] also has an ASCII grid (different notation). DISAGREE in detail: verify against the level data.
- [ ] #13 Pit until button #5. #14 Illusory pit + button that closes the pit south of #23. #15 Helm "Prentis". #16–#19 Plates firing fireballs from #17/#19. #20 Mystic lock opens the west door (key from #5).
- [ ] #21 **Minotaur** (reached by falling through L3 pit #11 [DG][TR]). It drops the great maul "**Thunder**", Dwarvish scale mail and a **Minotaur Horn**. [TR]: the armor often doesn't drop. [UHS]: circle the central column. #22 **The horn is the key; the lock is in the SW corner** and opens the west wall [CB][UHS].
- [ ] [DG] (single source): a long wall with 4 switches. The first three may stop the chicken spawns and the fourth turns on a fireball cannon.

**Level 3** [CB]
- [ ] #23 **Stairs from L2: the ghost "Spirit of Warning" warns you** [CB][TR].
- [ ] **Ghosts:** most spells and swords barely work on them. **Vaelan's Cube kills them instantly** [TR][CB]. **Emerald Blades** work too ([CB][UHS]; [TR] found them too slow). DISAGREE on how well the blades work. The cube **alternates colors**: black absorbs and damages, white restores up to 25 MP [CB][UHS].
- [ ] #1 **Secret switch in the floor** opens a niche with a light-blue/grey Mystic Key (for #10) [CB][TR].
- [ ] #2 Chest: dagger "Fang", salve, 4 s.c. #3 Chest: long sword "Protector". #4 Chest: long sword "**Entropy**" (cursed, strikes the bearer every 7th blow) + 8 s.c.
- [ ] #5 Chest: Dwarvish boots, staff "Tarsal", **Blue Mystic Key** (Sub-Level #3).
- [ ] #11 **Pit: press the near button AND throw an item at the far-wall button; both are needed to close it. The pit drops you into the Minotaur room on L2** [CB][TR].
- [ ] #12/#19 A button reveals the passage. #13 **Ivory Key** (for L1 #12). #14 Lock ← the Mystic Key from Jana's niche. #15/#22 Secret passages.
- [ ] #16/#17 **Weighting the plates at #17 slides block #16 west to reveal chest #8** (wand of Lightning, 2 Bannon's Reserves, **400 s.c.**). **#21 Bow "Gemini": take it BEFORE weighting the plates or it becomes unreachable** [CB].
- [ ] #18 **Faith Door: opens only with the Crucible of Faith in the inventory.** Behind it are **chest #6** (Dwarvish chain mail, rapier "Talon", star "Polaris") **and chest #7** (Galenian plate mail, arbalest, great axe "Reaper"). **Opening one makes the other vanish** [CB][UHS][AH]. [AH]: you need the statue head and the Elixir? DISAGREE: [CB]/[UHS] say only the Crucible.
- [ ] #9 Salve, Bannon's Reserve, saber "Cougar". #20 Pickable lock.

**Sub-Level** [CB]
- [ ] Sign: **"Face your greed."** A scream plays on entry [TR].
- [ ] #1 **Four items roam the floor around a central square. Put one in each of the four niches** (3 on the inner walls, 1 on an outer wall; **not the niche nearest the stairs**) [CB][DG][UHS]. When all four are filled the niches close and this **disables the invisible teleporter #6**, which otherwise sends you near #4 [CB].
- [ ] #2 The four items reappear in this niche (near the stairs) for you to take back [CB][AH].
- [ ] #3 **Blue Mystic lock.** #4 Secret button opens the wall to teleporter #5 (back to the stairs #7).
- [ ] #8 **Old Woman gives the Crucible of Faith**, then vanishes. Floppy asks for the "word of faith" (copy protection); **CD does not** [TR]. [TR]: you take the stone head flying in the air.
- [ ] On the way out, the stairs niche #2 gives back the four "greed" items plus extra equipment [TR][DG]. [DG] mentions a "wand of death" around here (single source; the death stick is otherwise a Knowles reward and in the Catwalk #39 chest).

### 2.17 City of Yvel (first visit)
- [ ] Enter at #28 (from Yvel Woods). No monsters on the first visit [TR].
- [ ] **#1 Sadie** (herbalist, Lora's aunt). Greeting: "You look surprised to see anyone still here in Yvel!..." You show the Riddle Scroll:
  - She understands it only if the **Mother Earth clue** is already on the scroll (from Dawn or Geron) [CB][TR].
  - **Lora was rescued**: **Earth Powder free** ("My niece Lora has written me..."). Otherwise it costs **500 s.c.** [CB]. [TR] did not know the price.
  - A second bottle costs 500 s.c. She also diagnoses and heals for 50 s.c., and sells Salve for 20 [CB][WIKI].
- [ ] #2 Dwarvish plate mail. #3 **Boarded door with loose boards: click several times to pry them off** [CB][UHS][TR]. It is in the SE of the city, on the north half of the south-easternmost N–S wall [UHS].
- [ ] #4 **Hand of Fate scroll + Speckled Key.** Addendum: the Speckled Key opens the lock at #20. Behind #20 is room #21 (plate mail, kite shield, great helm, **Jade Key**). The Jade Key opens #22 [CB addendum][UHS]. [DG]: the Jade-key house is empty.
- [ ] #5 Ace of Dominion (casts Hand of Fate IV once). #9 Ace of Oblivion (Mists of Doom IV once) + oil. #23 Ace of Infinity (restores all magic). #6/#11 Torn shirt. #8 Shirt. #10 Arbalest "Redemption". #12 Sandals. #13 Chest: Silent sandals + Bezel cup. #14 Chest: oil, granite rock, bracers, coins. #16 Aegis helm. #17 Pickable lock. #18 Halberd "Sever". #19 Halberd "Widow". #24 Oil.
- [ ] #7 **Victor's shop.** On entry: "**Hello Commander, I have the.....Oh, it is you!**" (foreshadows his betrayal). Stock: Saber "Dragon's Tooth" 150, Great Axe "Brimstone" 325, **Great Sword "Justice" 560**, Jeweled Dagger 3000 [WIKI]. [TR]: the 3000 dagger cannot be sold back. Directions [TR]: north, east at the wall, first way north.
- [ ] #15 **Fletcher Phillip** (Dracoid): Peregrin great bow 225, Arbalest 300, Crossbow "Swift" 300 [WIKI].
- [ ] #25 **Council of Yvel (Geron).** Show the Riddle Scroll the first time and he gives the Mother Earth clue and sends you to the White Tower [CB][TR]. After the Elixir: "fetch Dawn" [CB]. He will not hand over his key until Dawn is brought [WIKI].
- [ ] #26 **Bruno's Lodge.** Bruno: "Welcome to Bruno's Lodge!" / "Have you seen the new apothecary?... Sadie. What a dish!" Kegs: "Chateau Bruno's Table Red" / "Grisbl Cider" / "Southlandia Ale". "Some peddler named Philip left these samples." [WIKI]
  - **Frank the beggar** (if not killed): "What great fortune!..." gives the **Cape of Concealment / Whisper Cloak** if you helped him [CB][WIKI].
  - **Greph** (mailman) gives a long lore monologue: Warren the 13th heir, the Shard of Truth, William Gadfly, Ian the Cruel, Michel Lordsport and the Ruby, Ludwig ver Grundle... "Scotia... failed the examination to join the ranks of the Talamar. She hates Richard and his little Dawn, too." It repeats on every click [WIKI].
  - **Mark** (Thomgog): "Buy me a draught?" Yes = "Cheers!" (1 s.c.). No = "Put another one on my tab, will ya, Bruno?" [WIKI]
  - After the siege a back door opens here (see 2.20).
- [ ] #27 Secret panel to the Catwalk Caverns (after the siege).

### 2.18 White Tower: creating the Elixir
- [ ] Altar DeBlanca route [TR]: from the entrance go north, west at the end, north, west.
- [ ] **Place the Crucible on the altar's neck/statue, then put in the four ingredients one at a time** [CB][TR][AH]:
  1. Bloodstone ("butcher the creature whose flesh has never lived")
  2. Hornet Honey ("see the sweetness of your enemy")
  3. Swamp Water ("collected from the deadly depths")
  4. Earth Powder ("powders taken from the heart of your mother")
- [ ] **The Elixir mixes automatically when all four are in.** Take the Crucible back (it now holds the Elixir) [UHS][TR].
- [ ] [DG] (single source): items dropped close to the altar got lost.
- [ ] Riddle helpers: Witch Doctor (any one clue, paid; gave the Honey clue), Beggar (Swamp water), Dawn (Earth Powder), Geron (Earth Powder), Paulson (a clue) [CB][AH][UHS][WIKI]. Nobody writes the Bloodstone clue except the paid Witch Doctor; Orin's notes in the mines hint at it [UHS].

### 2.19 After the Elixir: Geron, false Dawn, Droek
Order [CB][TR][DG][UHS]:
- [ ] Yvel Council: **Geron tells you to bring Dawn** [CB]. [TR] route to Geron: from the Yvel entrance go N, W at the wall, N around the corner, W at the wall, N where the open part ends, 1E 1N then E, first N, first W, **second door north**.
- [ ] Leaving toward Upper Opinwood: **the false Dawn event in Yvel Woods #9** (see 2.15).
- [ ] Opinwood #15: **Droek dying** ("..ack.. cough.." / "Where is Dawn?" / "Dawn...ack.. cough..") and **Dawn is gone** (kidnapped) [WIKI][DG].
- [ ] Back to Yvel, Council: **Geron says the militia has fallen and the city is under attack** [CB][DG].

### 2.20 Siege of Yvel
- [ ] Monsters: Great Orcs, Cabal Warriors (they come back once as Ethereal Warriors) [TR][CB].
- [ ] **Kill invaders until you hear the Orc battle horn sound "retreat"** and the champion declares the battle won. After that no new enemies enter, though stragglers may remain [CB][TR].
- [ ] Enemies cannot enter buildings, so you can rest inside [TR] (single source).
- [ ] **Victor's shop closes permanently after the siege** [TR]. Buy what you need before.
- [ ] Council door is **locked**. **Take Geron's note**: a secret route into Cimmeria through Bruno's Lodge [CB][DG].
- [ ] Bruno's Lodge: the back door is now open. Behind it **kill the Great Orcs**, then press the **button on the north wall / open the secret passage north** into the Catwalk Caverns [CB][TR][DG]. Bruno and the patrons are gone [WIKI].

### 2.21 Catwalk Caverns
Monsters: Frendor (Dark Commander) + 2 lieutenants, Cabal Warriors, "sparks" (electric elementals: **Necrosaps**, drain magic, killed by Spark/Lightning; [TR]: only Spark IV) [CB][UHS][TR].

**Level 1** [CB] (91 map references; key ones below)
- [ ] #1 Stairs to Yvel. #2 **Invisible plate (party weight only) closes the wall at #73.**
- [ ] #3 Secret button (S wall) opens the west niche: a salve.
- [ ] #5 **War Room.** Frendor: "Welcome adventurers!... Welcome to the Army of the Dark Path..." Mylek: "I am Mylek, First Envoy..." Baccata: "We shall never join you Mylek..." Paulson: "Throw down your weapons..." Champion: "You cannot hope to sway our convictions..." **Victor**: "Be reasonable, [Champion]. Dark Army too powerful to beat. Join, and become respected like Victor, Imperial Blacksmith." Champion: "Honor means more to me than money and titles, Victor!" Frendor: "Then you shall die!" **Victor escapes** during the fight [WIKI].
- [ ] Frendor drops the **granite Statuette, the Gauntlet of Force (Dark Gauntlet) and Geron's Pyramid Key** [CB][TR]. The Gauntlet works as a weapon (pushes enemies back) and as the **key for the hand-print walls** [CB].
- [ ] #4 **Statuette on the north-wall pedestal opens passage #72** (a one-way E→W area that is removed) [CB][DG].
- [ ] #6→#7, #9→#11, #10→#73, #29→#30, #65→#66, #85→#86: **Gauntlet on the hand impression opens the linked wall or door.** #8/#12: invisible party-weight plates **close the wall behind you** [CB].
- [ ] At the **two hand prints in one square**, the **west print leads to Section 1 and the north print to Section 2**. You may do them in either order [TR][UHS].
- [ ] #13/#14 button toggles a door. #15–#22 **Moving-pit puzzle:** press the E-wall button at #18 (pit #15→#19), the W button at #15 (pit #21→#22), the E button at #21 (pit #22→#16), then the W button at #22 (pit #16→#21). The way is open [CB].
- [ ] #23–#28 Plates that toggle doors (party or items) [CB].
- [ ] #31 Stairs to L2. #32–#36 **Teleporter trap:** click an item onto plate #33 a few times until it is pressed, walk into teleporter #34 (goes to #36), pull the lever at #36 to open door #35 and get your item back. **Ignore every other pit, teleporter and plate** [CB]. [UHS]: the northern plate toggles the north teleporter on step-on and step-off.
- [ ] #37 cancels #38. #38 **Walking past slowly drains magic** (resting restores it) [CB].
- [ ] #39 **Chest (pick): staff "Death Stick", Mists of Doom scroll, ginseng, kite shield "George's", Bannon's Reserve, Yellow Key (for #69)** [CB]. [TR]: yellow key + Mist of Doom scroll.
- [ ] #40–#51 Button- and plate-toggled doors.
- [ ] #52 **Chest that can't be picked or smashed. It opens with the Blue Key from a Cabal warrior wandering the east corridors (#83).** Contents: **Great Axe "Master"** (+1 Rogue), 2 Bannon's Reserves, **Small Key (for #70)**, oil flask, Guardian globe [CB][TR][DG].
- [ ] #60/#61 **Necrosap locks / "Activate and replicate":** push a Necrosap into each of the 3 alcoves with **Hand of Fate I or the Gauntlet**. Three gems light and the niche activates. **Put an item in, press the east-wall button, and the item is duplicated. It works only ONCE per game** [CB][UHS][AH]. [CB] suggests "Justice" or "Valkyrie".
- [ ] #67/#72/#77–#81 **One-way passages** (the directions are on the map) [CB][UHS].
- [ ] #68 **Door needs both locks: #69 Yellow Key and #70 Small Key** [CB][TR]. [DG]: "left-hand lock yellow, right-hand small".
- [ ] #71 **Knowles and Xeobs encounter** (see 2.22). #53 **Knowles teleporter** (side with the Knowles, fight Xeobs). #91 **Xeobs teleporter** (side with the Xeobs, fight Knowles) [CB].
- [ ] #74 Spin square. #82/#84 Teleporter pair.

**Level 2** (optional)
- [ ] #1 Stairs back to L1 #31. #2 Teleporter to L1 #17. #3 **"This button has been deactivated by Scotia's minions!"** [CB]. Nothing useful here [TR][DG]. There is a door with something moving behind it that won't open [DG].

### 2.22 Xeobs vs Knowles: the Dungeons (below Castle Cimmeria)
- [ ] Both races ask you to wipe out the other. **The choice is final** [TR]. Stepping into a race's teleporter = siding with that race = you are sent to fight the other race [CB].
- [ ] **Rewards** [CB]:
  - **Side with the Xeobs (kill the Knowles):** Vaelan's Cube, great sword "**Justice**", the castle key, **+1 Fighter level for every party member**. [TR]: you become "Lord [name]".
  - **Side with the Knowles (kill the Xeobs):** Vaelan's Cube, **Death Stick**, the castle key, **+1 Mage level for everyone**.
  - [UHS] wording: "defeating the Xeobs (north teleporter) → spellcasting; beating the Knowles → fighting". This agrees.
- [ ] DISAGREE on difficulty: [CB] intro says the Knowles are easier, [CB] #71 says the Xeobs are tougher, and [TR] says the Xeobs are easier to kill. Both races eat armor; the Knowles quake weapons out of your hands [TR][WIKI].
- [ ] **You must kill every member of the enemy race**, or your patrons send you back [CB][AH].
- [ ] **Betrayal option:** at your ally's home you can attack them. You get the castle key at once but lose the level bonus [CB][UHS].
- [ ] Dungeon map [CB]:
  - #1 Chest: kite shield "George's", chain mail "Protector".
  - #2 **Plate fires ice bolts from #25 up the corridor. Objects cannot weigh it down.**
  - #3 **Copper Key** (for #23, the Knowles' homeworld teleporter).
  - #4 W-wall button toggles an E-wall niche with a Bezel cup.
  - #5 N-wall button toggles the west wall and a S-wall niche that **devours anything put in it**.
  - #6 Teleporter to the Catwalk Caverns.
  - #7 **Niche with Nathaniel's Key** (Pyramid Key #4).
  - #8/#9/#11 Chests: Galenian plate / trident "Mandible" / steel bracers, Ace of Dominion, 2 Guardian globes...
  - #10 **Gold Key** (for #13, the Xeobs' homeworld teleporter).
  - #13/#23 Homeworld teleporters (see rewards).
  - #14/#16 **Chest: salve + Diamond (needed to free Dawn)**. [TR]: in the centre.
  - #15 **This lever must be thrown before you can throw items through the grates on Cimmeria L1 #6 (for the sword "Doom")** [CB] (single source).
  - #17 Guardian globe (hidden niche; [DG]: a yellow key, no use).
  - #18 **Chest that teleports to #26 the first time you click it**. It holds great helm "Nestor" and crossbow "Elayne".
  - #19 Creatures may drop the key for #32. #21 **Door to the castle stairs: opens with the Xeob or Knowle key.**
  - #24 Bannon's Reserve, Ace of Oblivion, bones. #28/#12 Invisible plate fires spikes. #29/#30 Spike ball trap. #32 Lock: key from #19 or pick it.
- [ ] DISAGREE on where the homes are: [UHS] says the Xeobs are SW (gold key) and the Knowles centre-east (copper key). [AH] says the Xeobs are SE and the Knowles west side. [TR] gives step routes from each home to Nathaniel's key and the exit.
- [ ] Chests here may need high Rogue. [AH]: with low Rogue some "chests" teleported them away (version-dependent).
- [ ] Before going up, collect **Nathaniel's Key, the Diamond and the Cube** [CB].

### 2.23 Castle Cimmeria
**Rule: you cannot kill Scotia until Richard is cured and you have made the Whole Truth** [TR][AH][CB]. Meeting her early on L3 means certain death: she locks you in [TR].

**Level 1** [CB]
- [ ] Monsters: Ethereal (ghost) Cabal warriors (the Cube kills them instantly), flying axes (Hachucks) [TR][CB].
- [ ] The wall in front of the dungeon stairs closes behind you. It can be opened later by a W-wall button [DG].
- [ ] #1 **Crystal ball with Dawn trapped inside.** Examining it: "What an odd location for furniture." / "What strange magics hold her captive?" / "How did Dawn get in such a small ball?" **Use the Diamond** and Dawn is freed: "[Champion]! Paulson! I owe you my internal gratitude... So what of Richard?..." "**I shall gather our forces and return to join you soon.**" **She never joins** (cut content) [CB][WIKI][TR].
- [ ] #2 Button opens the west door. #4 **Airlock pair** (close one to open the other) [CB][UHS].
- [ ] #3 switch toggles wall #5.
- [ ] #6 **Two grate doors. Throw items through them to hit the switches behind.** #7 **Ornamental swords: click to get the great sword "Doom"** (the second-best sword [TR]). This needs dungeon lever #15 first [CB]. [UHS]: it only worked sometimes.
- [ ] #8/#9 Toggle the door south. #10 **Secret button activates teleporter #11 to #21** [CB]. [TR]: "second door south (press the button twice), press the south button, a teleporter appears."
- [ ] #12 **Stepping in from the east raises walls 2W and 1E and traps you.** #13 **Two switches: the WEST one hit twice opens a pit south (the only escape, to the dungeon). The NORTH one makes an east niche appear with the COBRA figurine** [CB]. [TR]: "At the end a pit appears behind you. Press the west button to remove it, then the north button." DISAGREE on whether the west switch opens or removes the pit.
- [ ] #14 **Switch stops the iron-tooth doors #16.** #15 Stepping here starts them again.
- [ ] #17 Jammed door, cannot be opened [CB][UHS]. It hides the **Blood Key / Zephyr ring area** (cut, see §3).
- [ ] #18 **Entrance to the pit room: do NOT flip any tongue switch in the corridor beyond** [CB]. [AH]: "Pull the first 3 levers on your way inside." DISAGREE. [UHS]: the levers only redirect the teleporter.
- [ ] #19/#20 **Switch spawns monsters. Avoid it.**
- [ ] #22 **Switches N, S, N, S make the pit disappear.** #23 Dropping an item removes that pit [CB]. [UHS]: the second of the two pits can't be closed.
- [ ] #24 **Niche: the item vanishes. Hitting the south switch brings the niche back with the item turned into silver coins** [CB].
- [ ] #25 **"Whoa-field" / mapless room.** **The automap is disabled and the compass spins or breaks.** You are teleported to the centre. Spin squares (#32) and pits (#34; a fall drops you to the dungeon). **Weigh down all 4 pressure plates (#31)** to open three secret corridors along the south wall [CB][TR][UHS]. [AH]: the plates are in the 4 corners; do the SE one last.
- [ ] #26 **Dragon figurine alcove:** press the wall button to enter. Press **south, then east**; a west niche opens with the **Dragon figurine**. Leave **without** pressing again. If blocked, put an item in the niche and press the buttons to reset [CB]. [TR]: "button in front of you, then the button left of you."
- [ ] #33 The centre south corridor holds the **stairs to L2**. The map and compass come back [CB][TR]. [TR] exit: from the plate go left, forward (spin), turn right, 1 forward, turn left, forward through the illusion.

**Level 2** [CB]
- [ ] Monsters: Death Disks/"wheel beasts" (rip weapons away), Manthas (stun) [TR][CB].
- [ ] #1 **Stepping here triggers a fireball.** The S-wall switch opens door #2 [TR: "set the switch quickly and move 1W 1N"].
- [ ] #4 **Plate opens a passage east. It can't be weighed down and the wall closes behind you permanently.**
- [ ] #5 **Niche: anything put in disappears (and the niche too).** [DG]: do not use it.
- [ ] #6 Chest (pick): staff "Gustavus", dagger "Riposte" (+25 protection). #7 Deactivated switch. #8 Switch → niche with trident "Plague" (−1 Knight).
- [ ] #9 **Ornate pickable lock** leads to room #10, with an empty niche.
- [ ] #11 Chest (pick): Bracers of Defense, Ace of Infinity, Ace of Oblivion, coins.
- [ ] #12 **Ornate pickable lock → #13 switch → niche with the UNICORN figurine** [CB]. Picking it successfully may need **Rogue level 4 or higher** [DG][TR].
- [ ] #14/#32 Spikes (impassable, hurt you). #15 **Lock (Noir Key) + tongue switch opens the south door, then teleport to the stairs area.** #16/#17 Tongue switches open doors.
- [ ] #18 **Switch teleports you to #39.** #19 Lock ← **Adder Key** (#26). #20/#21 Plates throw spiked balls from W/E. #22 Switch opens a passage 2 west.
- [ ] #23 Chest: Westwood stick (25% one-hit kill, one use), aloe, great helm "Aegis".
- [ ] #24 **Two doors: hitting the switch on one opens the other.**
- [ ] #25/#26 **Adder Key trap:** push button #25, throw an item west, walk west and pick it up, walk north, press the second E-wall button, walk south, **take the Adder Key, put an item on the plate to the west**, leave [CB]. [TR]: "Press W-wall button, go W (trap), press E-wall button in N alcove, take the key from the W niche in the S alcove."
- [ ] #27 Spike ball from the W. #28 **Tongue switch makes a pit appear immediately west. It vanishes when you step east of the switch.** #29 Ice bolt from the W. #30 Fireball from the S.
- [ ] #31 **Niche: Noir Key** (the only niche room on the west side [UHS]).
- [ ] #33 **Two switches: the W-wall one opens the east wall. The S-wall one opens a pit to the dungeon.** [TR]: "IGNORE THE BUTTON ON THE SOUTH WALL! Press the west one."
- [ ] #34 **Ornate lock ← Carrion Key.** After #36 is opened, stepping here fires a spiked ball from the south.
- [ ] #35 **King Richard's chamber** (see below).
- [ ] #36 **Press both switches** to open a passage south.
- [ ] #37 **Throw an item over the pit to the east to hit the switch. A teleporter appears across the (illusory) pit to the south.** Walk to it and it takes you to #38 [CB][UHS][TR].
- [ ] #38 **Niche with the Carrion Key. Put a useless item in the niche to close the pit that traps you** [CB]. [UHS]: the teleporter lands you on a plate, so throwing an item into the teleporter instead closes the east pit. [TR]: leave an item on the plate, or jump into the pit (to L1, mid Whoa-field).
- [ ] #39 Arrival from #18. #40 Stairs to L3. #41 Disappearing wall.
- [ ] Unreachable walled-off chest: **Cloud Ring + Great Bow "Darkness"** (see §3).

**Level 3 (first visit: humanoid figurine)** [CB]
- [ ] **The compass breaks permanently on arrival** [TR] (single source). **Scotia locks you in**: the stairs down can't be used and you leave by pits to L2 [TR]. Scotia's warning line: "You will not cheat me again." [WIKI]
- [ ] **Items thrown on L3 may vanish forever.** The fireball/laser storm saturates the flying-object buffer and thrown items share it [TR]. [SVM] also patches a script bug where items thrown toward the stairs on White Tower L3 (level 21, block 0x3E0) vanished.
- [ ] #14/#15 **"THE SUM MUST BE CORRECT."** The automap shows walls forming "1 + 1 = 3". Press the wall button to move the wall 1 west, walk west, and press again. The map now reads **"1 + 1 = 2"** and wall #15 opens [CB][DG][UHS][AH].
- [ ] #16 **Buttons on both east and west sides open a niche: HUMANOID figurine, plus Dawn's key if you gave it to the false Dawn.** Put an item in the niche and press a button to open the way north out [CB][TR][DG]. [UHS]: the left button shows the niche and the right button lets you out. [AH]: you may need to close the door behind you to get Dawn's key off the floor.
- [ ] #17 Button opens a niche 2 east across a pit. #21 Pit. #22 **Dripping acid (damage).** #19/#20 Wall/switch pairs.
- [ ] Leave by jumping into a pit (west alcove) down to L2 [TR].

**King Richard's chamber (L2 #35)** [CB][UHS][TR][DG][AH]
- [ ] Open with the **Carrion Key**. Richard lies in the **dark-tainted Crystal Casket**.
- [ ] **Light puzzle:** a sunbeam lights the **front-left pedestal**. Place figurines in the order of the reflected beam:
  1. **Cobra, front-left** (the lit one)
  2. **Humanoid, front-right**
  3. **Dragon, back-right**
  4. **Unicorn, back-left**
  - [DG] and [UHS] agree on these positions. [TR] and [AH] give the same order (Cobra → Humanoid → Dragon → Unicorn) with "click the next lit pedestal". [TR]: "place and press exit, then the next platform lights."
- [ ] **Pyramid keys on the four casket corners:**
  - **Dawn: back-left** ("upper left")
  - **Geron: back-right** ("upper right")
  - **Nathaniel: front-left** ("bottom left")
  - **Paulson: front-right** ("bottom right")
  - [TR], [AH] and [UHS] agree. [DG]: "the game will help you put each key in the correct corner."
- [ ] **Apply the Crucible (Elixir) to Richard.** "I can tell by the foul stench that I must be in Cimmeria." / Paulson: "Yes, my liege." / "...What of Scotia?..." / Baccata: "...She has changed form to thwart us..." / "**Take this ring, the Shard of Truth.** ...I am too weak to join your group..." / "Your Highness, how shall you defend yourself?" / "If you could spare a weapon, I would be grateful." Giving one: "Thank you, [Champion]. You are a noble subject." [WIKI]
- [ ] You receive the **Shard of Truth** (a ring, +5 protection when worn) [CB].
- [ ] [SVM] notes invisible text in the casket graphics, left over from a king-present/absent state [TCRF].

**Level 3 (second visit: to Scotia)** [CB]
- [ ] #28/#29 **Fireball from the E / ice bolt from the S** when stepped on. [TR]: going south at the corridor starts a **perpetual laser and fireball mechanism**. **A level-4 Freeze keeps the fireballs out of the small corridor** (Freeze IV creates a temporary ice wall [CB spellbook]).
- [ ] #13 **Button opens the south niche: Gold Key** [CB]. [TR]: "press the north button to open the south niche." [UHS]: the gold key is in a NW room with no trick.
- [ ] #1 **"Leave … here" alcoves.** Leave a **suit of armor, a weapon, a ring or necklace, and a salve or herb**, one per alcove. Then go to the south niche for the **Dull Key**. You can take your items back [CB][DG][UHS][AH]. [TR]: **shooting weapons do not count as the "weapon"** (single source).
- [ ] #6 **Oily Key.** #8 **Death Key.** #9 Chest (Death Key): halberd "Death's Hand", Aegis plate mail, coins. [DG]: the Oily Key opens chest #7? DISAGREE: [CB] says #7 opens with the Dull Key and holds the Silver Key. [DG] uses the Oily Key on one chest and the Death Key on another.
- [ ] #7 **Chest (Dull Key): Silver Key, salve, coins** [CB]. [TR]: "lockpicks won't work." [UHS]: the Dull Key only opens one lock, so try the chests safely.
- [ ] #11 Chest (pick): helm "Talamar", ginseng, coins. #18 Chest (pick): arbalest "Eternity", plate "Bastion", coins.
- [ ] #5/#12/#24 **Switches make walls vanish and reveal a Toadulus monster.** #26 Spin. #27 **Stepping here raises walls N and E. The switches that appear remove them.** #23 **Plate permanently closes the hall south.**
- [ ] #2 **Silver Key lock (left)** + #4 **Gold Key lock (right)**, then the **middle button**. The wall opens [CB][DG][TR]. [TR]: the N-wall button reveals the door, silver key on the lock west of it, gold key on the right. **Past this door there is no way back** [TR].
- [ ] **Combine Ruby + Shard** (click one onto the other in inventory) to make **the Whole Truth** [CB].

### 2.24 Final battle: Scotia (L3 #30)
- [ ] Scotia: "So! You worthless dogs have come to your deaths!" / "It is you who will breathe your last, Scotia!" / "Ha! You brought my Ruby and its Shard to me! That fool Richard actually believes the old tale that they may defeat my Nether Mask. It is folly. They shall give me even more power." [WIKI]
- [ ] Without the Whole Truth she transforms through **Toadulus → Serpentis → Executioner**. The **Executioner (third form) is "virtually unbeatable"** [CB][TR].
- [ ] **Use the Whole Truth (right-click on a character portrait) only while she is mid-transformation, shown by a visible blue glow.** [CB][TR]: it does not work at any other moment. [DG]: use it "as soon as she stops talking". [UHS]: the best chance is the first transformation, and if you miss, "kill" her form to trigger the next one.
- [ ] On success **the Whole Truth vanishes, the Nether Mask is destroyed, and she reverts to her hag form for good** [CB]. Then kill her. Tips: Guardian globes, Death Stick/Wand, Aces [TR][DG][AH].
- [ ] Scotia stats [CB]: Hag: to-hit 100, dodge 50, speed 100, magic 200, AF 50, HP 300, XP 1000. Toadulus: AF 50, HP 250, XP 200. Serpentis: AF 45, HP 350, XP 500. Executioner: AF 60, HP n/a, XP 1000.
- [ ] Death lines: Champion: "Well struck! That looks to be a mortal blow!" / Scotia: "Fool! My death will not save you from our wrath!" [WIKI][DG]

---

## 3. Easter eggs, secrets, cut content, cheats, known original bugs

### Secrets and optional content
- [ ] **Hand of Fate** is hidden behind the loose boarded door in SE Yvel (click the boards repeatedly) [CB][UHS][TR].
- [ ] **Activate-and-replicate** duplicator in the Catwalk Caverns: one use per game [CB][UHS].
- [ ] **Great sword "Doom"**: throw items through the Cimmeria L1 grates. It needs the dungeon lever first [CB].
- [ ] **White Tower Faith Door**: choose one of two chests; the other vanishes [CB].
- [ ] **White Tower L3 sliding block**: 400 s.c. + wand of Lightning. Take bow "Gemini" before the plates [CB].
- [ ] **Backbiter dagger**: a dagger put in the niche next to the plate on Draracle L3 transforms into it [CB] (single source).
- [ ] **Swamp idols**: Ra'Tol (item → 60 s.c.) and Ba'Del (full heal and MP for any item) [CB].
- [ ] **Cimmeria L1 #24 niche**: turns an item into silver coins [CB].
- [ ] **Infinite Cave Dweller spawn** near a Draracle L2 crossroads if not fully cleared; XP farm [WIKI]. **Urbish L1 wheels both up or both down** spawn a fightable monster for practice [UHS].
- [ ] **Undocumented poison cure**: let the poisoned character drop to 0 HP (unconscious, not dead) and heal them; the poison is gone. **If the whole party goes down it is game over** [UHS][DG].
- [ ] **Skills advance only on kills** (the character who lands the killing blow gets the XP), not by use [UHS][CB].
- [ ] Only the **Hand of Fate spell gives no XP** for its damage. It may be an original bug [SVM].

### Jokes / references
- [ ] Throne Room line "You will pay for your lack of vision!" is a *Return of the Jedi* reference [WIKI].
- [ ] TCRF tagline: King Richard is voiced by Patrick Stewart ("Captain Picard was king way before Oblivion") [TCRF].
- [ ] "These scones are firmly attached." / "I think you mean sconces." (Timothy, Roland's Manor) [WIKI].
- [ ] Buck: "Buy the left one for 10 crowns and I'll throw the right." Lynn: "I suppose you'll want to steer the boat too?" Petricia: "We hung three Orcs with that rope last week!" [WIKI]
- [ ] Greph's endless history lecture in Bruno's Lodge, and Champion's escape line "...I have a kingdom to save and a Scotia to kill." [WIKI]
- [ ] **"Nathaniel references"**: none of the sources found any joke or easter egg about Nathaniel. He is the Keep herbalist (Joseph D. Kucan's voice) and a Council member. His Pyramid Key is in a Dungeon niche. Treat "Nathaniel jokes" as unverified.

### Cut / unused content (do not implement as reachable unless you want "restored content") [TCRF][WIKI]
- [ ] **Dawn as a playable companion**: portraits exist and she says "I shall gather our forces and return to join you soon". [TR] claims a savegame or memory hack can add her.
- [ ] **Blood Key**: an unused item. It opens the jammed "fancy lock" on Cimmeria L1 [CB #17 "can not be opened"]. Behind it is a chest with the **Zephyr ring and Bow "Tempest"** [TCRF]. The wiki mentions a separate walled-off chest on Cimmeria L2 with the **Cloud Ring and Great Bow "Darkness"**.
- [ ] Unused items: Great Maul "Pillage", Long Sword "Ares' Breath"/"Life Taker", Axe "Splitter", Great Axe "Meister"/"Executioner", Great Maul "Thor's Fist"/"Armageddon", Great Bow "Tracker", Rapier "Coup D' Grace", Trident "Pestilence", Mesmer's Great Helm, Aegis Kite Shield, Bronze key, Staff "Gainful", Bloody staff, Talba ring, Vortex scroll. (The [CB] weapon tables do list some of these, e.g. Armageddon, Life Taker, Executioner, Tracker. That suggests they were cut late.)
- [ ] Throwable "special" projectile items (Illegal Item, Poison dart, Arrow, Quarrel, Shuriken, Muck, Spit, temp item, Ice Bolt, Spike, **Pain**, Dispell, Poison). "Pain" and "Poison" do heavy damage if thrown [TCRF].
- [ ] Unused spells: **Ice Wall, Vortex** (box shots) [TCRF].
- [ ] Unused music: "Lands of Lore Main Theme" (title screen is silent), and "The Keep" (keep228m). Music slot 0x00FE plays LORE03A track 03 instead of LORE01A track 06 [TCRF].
- [ ] Unused speech: Timothy and Lora have "cannot use" lines for the Vaelan's Cube and the Whole Truth [WIKI][TCRF].
- [ ] Inaccessible map areas on most levels, including a named colored square in Yvel Woods [TCRF].

### Cheats
- [ ] **There are no built-in cheat codes** in any source. The only published "cheat" is a hex patch of `MAIN.EXE` [Cheatbook]:
  - Unlimited life: `5E FC 26 89 7F 39 26 83` → `5E FC 90 90 90 90 26 83`
  - Magic only increases: `5E FC 26 89 7F 3D EB 12` → `5E FC 90 90 90 90 EB 12` and `5E FC 26 01 47 3D 26 83` → `5E FC 90 90 90 90 26 83`
- [ ] Killing the Keep guards (only possible with cheats) **permanently locks the Keep doors, which soft-locks the game** [WIKI].

### Known original-game bugs / quirks (decide per item whether to reproduce)
- [ ] **Lora/Paulson crash** [TR][WIKI][SVM]. On a second fall to Draracle L2 near the Pod Room, the Pod Room passage stays open for 1–2 seconds. If you run in and free Lora after the Draracle, she stays in the party permanently. Then **the original crashes when Paulson (a 4th member) tries to join** (Urbish L4, level 16, block 0x225). ScummVM boots Lora and drops her items with the message "Lora has left the party. Her items are on the floor." Without the fix, Paulson's cache button never appears, so no cube and Upper Opinwood is blocked [TR].
- [ ] **Dawn's key lost in early releases** if given to the false Dawn. Fixed later, so the key is in the Cimmeria L3 niche [TCRF].
- [ ] **Vaelan's Cube self-replicates** on some versions (v1.05 floppy) when used, or when equipped as a weapon [AH][UHS]. [SVM] "WORKAROUND for unpatched early floppy versions: the Vaelan's cube should not be able to be equipped in a weapon slot" (item 264).
- [ ] **Items thrown toward the stairs on White Tower L3 vanish** (script bug, level 21, block 0x3E0) [SVM]. **Thrown items vanish on Cimmeria L3** under the fireball storm [TR].
- [ ] **Large fireballs** from White Tower L2 birds and Cimmeria L1 wraith knights fly through corridors in the original only because of a flags-lookup bug. It gives them width 63 instead of 256 [SVM].
- [ ] **Pixel-edge item pickup**: clicking exactly 1 px from the left, right or bottom border of the view could pick up items and even lock up the game [SVM].
- [ ] **Monster sound id 0xFF** on some White Tower monsters is invalid, but it happened to be harmless in the original [SVM].
- [ ] **Base might not added to melee damage** (`res = 0` instead of the character's might). It may be an original bug [SVM].
- [ ] **Bezel Cup "zombie" effect**: a character turned into a "zombie" on use. Unconfirmed, possibly fixed in v1.11 [AH] (single source).
- [ ] **Fireball scroll in the Urbish office** is random or absent [TR][UHS].
- [ ] **Ra'Tol always pays 60 s.c.** regardless of the item's value (the Clue Book implies full value) [WIKI].
- [ ] **Scotia can be reached before Richard is cured**, and then she cannot be killed. This is by design, but it is a dead end [TR][AH].
- [ ] **Using the 2nd Vaelan's Cube on the Yvel barrier before White Tower L3** makes the ghosts very hard. [TR] calls it unwinnable, [UHS] says emerald blades work.
- [ ] **Timothy/Lora** speak generic lines for items they can never hold [WIKI].
- [ ] Original GUI crashed on non-contiguous save slots (ScummVM fixed this) [ScummVM release notes].

---

## 4. Ending

- [ ] After Scotia dies: cut to **Gladstone**, where the party is honored. Richard: "Congratulations!" / "You have served me well." Geron: "**A luckier adventurer Gladstone never saw!**" Dawn: "It has been a pleasure serving with you." [WIKI][DG]
- [ ] Baccata is made an officer of Gladstone [WIKI] (lore; how it shows on screen is unverified).
- [ ] **Credits roll as a parade of the game's monsters** ("reminisce at the parade of monsters... so you didn't kill all of them!") [DG] (single source).
- [ ] The game ends after the credits. There is no post-game.

---

## 5. Reference data

### Spells [CB]
| Spell | Source | I | II | III | IV |
|---|---|---|---|---|---|
| Spark | known at start | 7 elec, 1 target | 15 | 25 | 60 |
| Heal | Dawn's spellbook | +25 HP | +45 HP | full HP + cure poison | whole party full + cure |
| Fireball | White Tower L2 fireball room (or Urbish office, random) | 1 small, 20 | 2 small, 40 (**can ignite mine gas**) | 3 small, 80 | 2 large, 100 |
| Freeze | Draracle L2 chest (both routes), Sapphire L3 niche | 10 cold, all | 20 | 30 | 55 + **temporary ice wall** |
| Lightning | Opinwood NE chest; Urbish L2 N chest | 18, 1 target | 35 | 50 all | 72 all |
| Hand of Fate | Yvel boarded house | push back | push + 75 | fist 125 | crush 175 |
| Mists of Doom | Catwalk L1 chest #39 | 30 | 70 | 110 | 200 |

- [ ] **Any Freeze level freezes swamp sinkholes. The level only changes how long they stay frozen** [CB].

### Special items (effects) [CB]
- [ ] Bezel Cup: full heal + cure poison, one use per gem. Bezel Ring: poison immunity. Duble Ring: faster HP regen. Jade Necklace and Great Axe "Master": +1 Rogue each.
- [ ] Ruby of Truth / Shard of Truth: each +5 Protection when worn. Combined they make the Whole Truth.
- [ ] Green Skull: Caustic Fog, 15 acid. Ebony Staff: 5 Swarm charges. Swarm: 10 damage (no effect on insects, ethereal or stone creatures).
- [ ] Guardian Globe: summons a Guardian Sword, about 200 damage to everything nearby, hurts magic-only creatures. It is **not consumed if there are no enemies**.
- [ ] Wands: power level = number of charges left. Use them in-hand with right-click; don't equip them.
- [ ] Emerald Blades: good against apparitions, wraiths and ethereal warriors.
- [ ] Vaelan's Cube: black = damage magical beings and dispel barriers (also secret walls [WIKI]). White = +25 MP. It alternates color on each use.

### Pyramid keys (4)
- [ ] **Dawn**: Droek's wagon, Opinwood. **Paulson**: Urbish L4. **Geron**: from Frendor, Catwalk L1. **Nathaniel**: Dungeon niche #7 [CB][UHS].

### Figurines (4)
- [ ] **Cobra**: Cimmeria L1 #13 (NW area, trap room). **Dragon**: L1 Whoa-field alcove #26 (SE). **Unicorn**: L2 #13 behind a pickable ornate lock (east side). **Humanoid**: L3 sum-puzzle niche #16 [CB][UHS][AH].

### Vaelan's Cubes (3–4 obtainable)
- [ ] 1: Paulson's cache or Dawn (see 2.11) → Upper Opinwood barrier.
- [ ] 2: Great Orc at Vulture's Chasm → White Tower L3 ghosts, then the Yvel barrier.
- [ ] 3: Xeob or Knowle reward → Cimmeria ghosts.
- [ ] Extra copies only on buggy versions (self-replication) [CB][TR][WIKI].

### Monster stats [CB] (to-hit / dodge / attack speed / magic ; armor factor / HP / XP)
Rows marked `?` were garbled in the OCR (the [CB] table columns came out interleaved). Check the scan (archive.org PDF, pp. 98–101) for exact values. Rows that could be read reliably (AF / HP / XP):

| Monster | AF | HP | XP |
|---|---|---|---|
| Amazon | 20 | 100 | 200 |
| Amazon Queen (Jana) | 30 | 125 | 300 |
| Apparition | 50 | 90 | 200 |
| Archer Slug | 10 | 34 | 60 |
| Avian Worm | 30 | 55 | 70 |
| Bandit | 12 | 40 | 50 |
| Boar | 5 | 26 | 25 |
| Boglyte | 20 | 95 | 70 |
| Cabal Warrior | 40 | 100 | 175 |
| Cave Dweller | 5 | 30 | 40 |
| Chull | 30 | 83 | 200 |
| Dark Commander | 50 | 150 | 250 |
| Death Disk | 40 | 105 | 300 |
| Ethereal Warrior | 40 | 185 | 300 |
| Flying Spider | 10 | 20 | 25 |
| Giant Lizard | 7 | 40 | 30 |
| Giant Hornet | 20 | 34 | 65 |
| Gimlet | 15 | 30 | 45 |
| Gorkha | 15 | 80 | 85 |
| Great Orc | 40 | 150 | 100 |
| Greater Multipede | 25 | 125 | 100 |
| Hachucks | 10 | 50 | 300 |
| Creeping Horror | 55 | 150 | 200 |
| Hurzel | 25 | 85 | 100 |
| Iron Grazer | 30 | 90 | 90 |
| Keep Guard | 10 | 200 | 200 |
| Knowles | 12 | 125 | 300 |
| Larkhon | 25 | 80 | 125 |
| Magic Mirror | 25 | 60 | 100 |
| Mantha | 40 | 100 | 350 |
| Minotaur | 35 | 155 | 200 |
| Molder | 20 | 200 | 100 |
| Moribund | 25 | 68 | 75 |
| Necrosap | 10 | 1 | 100 |
| Orc | 8 | 50 | 40 |
| Orc Leader | 20 | 50 | 50 |
| Pentrog | 25 | 78 | 90 |
| Ratman | 20 | 35 | 50 |
| Rockling | 40 | 95 | 95 |
| Scavenger | 20 | 75 | 75 |
| Scotia (hag) | 50 | 300 | 1000 |
| Serpentis | 45 | 350 | 500 |
| Stark | 15 | 40 | ? |

- [ ] Keep Guard [WIKI]: to-hit 200, dodge 100, attack speed 200, AF 10, HP 200, loot Halberd.
- [ ] Weaknesses [CB]: Amazons (Freeze); Apparitions (not cold; cutting weapons do half damage); Archer slugs (large blunt); Boars (thrusting); Boglytes (Freeze); Flying spiders (Fireball); Gorkha (impaling, magic-resistant); Hornets (fire); Multipedes (impact); Hachucks (magic-immune); Hurzels (electric); Knowles (only fire/acid get through); Larkhon (acid only); Giant lizards (blunt; they steal weapons); Magic mirror (impact; reflects spells); Manthas (ranged; they stun); Molders (fire; immune to cold/acid); Moribunds (fire/cold/impale); Necrosaps (Spark/Lightning); Orcs (blunt); Great Orcs (not blunt); Pentrogs (fire, cold-resistant); Ratmen (edged; poison wounds); Rocklings (immune to slash/impale, use Freeze/blunt); Scavengers (cold, bashing; fireproof; webs; metal scavengers destroy metal gear); Starks (Freeze; fire does nothing); Trez (fire/thrust); Wraiths (magic-resistant, drain MP); Xeobs (magic-resistant).
- [ ] **Armor-eating monsters** (slimes on Draracle L4, metal scavengers, Knowles/Xeobs acid) destroy gear for good [TR][DG][CB].

---

## 6. Sources

- Westwood/Virgin, *Lands of Lore: The Throne of Chaos Clue Book* (1994), scan and OCR: https://archive.org/details/Lands_of_Lore_Cluebook (text: https://archive.org/download/Lands_of_Lore_Cluebook/Lands_of_Lore_Cluebook_djvu.txt)
- Tricky (Jeroen P. Broks), *Complete Walkthrough* v1.2: https://www.supercheats.com/pc/walkthroughs/landsoflorethethroneofchaos-walkthrough02.txt (also GameFAQs https://gamefaqs.gamespot.com/pc/564639-lands-of-lore-the-throne-of-chaos/faqs/50982). v1.0: https://www.cheatbook.de/wfiles/landsoflorea.htm
- Diana Griffiths walkthrough (1995): https://rpggamers.com/walkthrough/lands-of-lore (mirror: http://www.thecomputershow.com/computershow/walkthroughs/landsoflore1walk.htm)
- Anonymous "Hints and Tips": https://www.cheatbook.de/wfiles/landsoflorethethroneofchaos.htm (GameFAQs faqs/1877)
- UHS hints: https://www.uhs-hints.com/uhsweb/lol.php
- Lands of Lore fandom wiki (NPC dialogue, prices, bugs, cut content), read through the MediaWiki API: https://lands-of-lore.fandom.com/wiki/Walkthrough_(The_Throne_of_Chaos), /wiki/Bugs, /wiki/Cut_Content_(The_Throne_of_Chaos), /wiki/Gladstone_Guards_(The_Throne_of_Chaos), plus the character pages (Victor Glaston, Timothy, Lora, Dawn, Scotia, Geron Arbroath, Richard LeGré, Mathpasha, Lyle, Frank (Beggar), Sadie, Buck, Nathaniel, Droek, Dwight, Petricia, Lynn, Phillip (Dracoid), Bruno, Greph, Jakel, Draracle)
- The Cutting Room Floor (Wayback copy): https://tcrf.net/Lands_of_Lore:_The_Throne_of_Chaos
- ScummVM kyra engine source (LoL): https://github.com/scummvm/scummvm/tree/master/engines/kyra (`engine/lol.cpp`, `engine/items_lol.cpp`, `engine/sprites_lol.cpp`, `script/script_lol.cpp`, `gui/gui_lol.cpp`, `resource/staticres_lol.cpp`)
- Cheatbook hex patch: https://www.cheatbook.de/files/lolttc.htm

### Coverage gaps
- The sources do not describe the guards' animation or movement at game start (whether a guard walks up and steps aside, and what exactly triggers it). Only the dialogue and "blocks exit until you see the King" are documented. Check this in the original (DOSBox) or in the LoL script files (`LEVEL1.INF` / `.EMC`, which ScummVM runs).
- There are no grid coordinates. The Clue Book map numbers need the scanned maps (archive.org PDF).
- The Clue Book monster table's to-hit, dodge, speed and magic columns were garbled by OCR, as were a few item costs and the armor/weapon factor columns.
- These sources are not accessible: the GameFAQs pages and rpgcodex (403/Cloudflare), StrategyWiki (stub page) and any ScummVM bug-tracker entries specific to LoL1 scripts.
