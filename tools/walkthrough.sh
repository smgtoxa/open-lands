#!/bin/bash
# The campaign, walked the way the walkthrough ($LANDS/private/walkthrough.txt) plays it,
# one segment per stretch of the game. Each segment runs on both engines (tools/diff.sh: the web engine
# is the reference) and must then show what the walkthrough says happens there (the "expect" patterns,
# looked for in the web engine's trace). A segment ends in a save the next one starts from.
#   bash tools/walkthrough.sh [segment...]     (default: all, in order)
# Blocks come from tools/level_survey.mjs LEVEL. Scenes wait for a click or a key, as in the original
# (TIM stopAllFuncs); a scene's exit button is at 280x114.
set -u
cd "$(dirname "$0")/.."
W=Shots/walk; mkdir -p "$W"
declare -A STEPS EXPECT
ORDER=()
seg() { ORDER+=("$1"); STEPS[$1]=$2; EXPECT[$1]=$3; }
enters() { local s=""; for i in $(seq 1 "$1"); do s="$s,key:enter,wait:3"; done; echo "${s#,}"; }

# ---- Gladstone Keep (level 1): the King, the library and the Atlas, Geron's writ, out of the gate ----
seg gladstone "new:0,wait:2,key:enter,wait:2,goto:273,wait:3,idle,wait:3,idle,wait:3,idle,wait:5,flags:4,items,\
goto:362,face:2,call:inventorySlotClick/3,wait:1,click:200x60,wait:3,idle,\
goto:392,face:1,click:200x60,wait:4,idle,key:enter,wait:2,flags:252,\
goto:427,face:3,key:up,wait:3,$(enters 8),click:280x114,wait:4,idle,flags:11,items,\
goto:621,wait:3,idle,wait:3,idle,state,cflags,items,save:$W/gladstone.json" \
"Please retrieve it from Roland's estate|Library key\(185\)|Library key taken|flags 252=1|Writ of Passage\(244\)|Timothy is at the Grey Eagle|flags 11=1|\"level\":2,\"block\":106"

# ---- Northland Forest (level 2): Scotia on the road, the marina takes the writ, the boat south ----
seg northland "load:$W/gladstone.json,wait:2,goto:360,wait:3,$(enters 4),\
call:inventorySlotClick/3,wait:1,click:250x40,wait:3,$(enters 6),wait:5,idle,state,flags:25,cflags,items,save:$W/northland.json" \
"Timid fool! I will not forget you|I have a Writ from the King|hurry and get on the boat|flags 25=1|\"level\":3,\"block\":526"

# ---- Southland Forest (level 3): the Grey Eagle (Timothy; Philip's magnet stone), the road to the manor.
# The harness keeps the party at full health while it fights ("harness heal" lines in the trace): the walk tests
# the game along the walkthrough, not whether this party could survive it.
seg southland "load:$W/northland.json,wait:2,goto:688,face:1,wait:1,click:189x58,wait:5,key:enter,wait:3,\
click:148x90,wait:3,$(enters 4),click:152x63,wait:3,$(enters 4),click:303x79,wait:3,$(enters 4),click:24x78,wait:3,$(enters 4),\
cflags,walk:772,wait:4,idle,wait:3,idle,state,cflags,items,save:$W/southland.json" \
"This looks cozy|Are you by chance Timothy of Gladstone|Take this magnet stone|I am pleased to join you|char1 Timothy|What happened\? Someone has attacked|\"level\":4"

# ---- Roland's Manor (level 4): the fireplace's burnt scroll, the button and its niche, the Orc Leader
# (ATTACK), the button to Roland's hiding place, Roland's death, his key, and his chest (one thing a click:
# coins instead of the compass Philip already gave, coins, the saber, then empty); out to the forest ----
seg manor "load:$W/southland.json,wait:2,walk:38,face:0,$(for i in 1 2 3; do printf 'click:202x77,wait:2,key:enter,wait:2,'; done)\
call:inventorySlotClick/3,wait:1,walk:227,face:2,press,wait:2,walk:228,face:2,pick,\
walk:76,wait:3,buttons,choose:0,wait:2,fight:120,walk:75,fight:60,face:3,press,wait:3,walk:73,wait:3,$(enters 8),idle,\
walk:41,take,face:0,call:inventorySlotClick/7,wait:1,press,wait:3,press,wait:3,press,wait:3,press,wait:3,call:inventorySlotClick/7,wait:1,press,wait:3,\
call:uiEquipBest/0,wait:1,call:uiEquipBest/1,wait:1,flags:29.39,items,walk:199,wait:4,idle,state,cflags,items,save:$W/manor.json" \
"Burnt Scroll taken|pick Oil Flask, Oil Flask, Aloe|0:ATTACK|This is Roland's hiding place|They have stolen the Ruby of Truth|Roland's key taken|If I don't take these coins|Saber taken|Hmm,. . . empty|\"level\":3,\"block\":804"

# ---- back north (levels 3, 2, 1): the Southland marina (100 crowns a head), the gate guards, Scotia's attack
# on the King in the throne room; the court talks, Timothy stays, Baccata joins, Dawn's magic album ----
seg return "load:$W/manor.json,wait:2,walk:526,face:0,key:up,wait:4,key:enter,wait:3,click:235x78,wait:3,key:enter,wait:3,\
buttons,choose:0,wait:3,buttons,choose:0,wait:3,choose:0,wait:3,key:enter,wait:3,key:enter,wait:4,idle,state,items,\
walk:74,wait:4,idle,walk:273,wait:3,$(for i in $(seq 1 12); do printf 'click:200x60,wait:2,key:enter,wait:2,next:1,'; done)\
$(for xy in 24x54 287x68 285x48 109x44 115x73 190x19 192x31 24x54; do printf "click:$xy,wait:3,next:2,"; done)\
click:290x121,wait:4,next:4,idle,state,cflags,items,flags:41,save:$W/return.json" \
"Sorry, 100 crowns per passenger|0:YES|Hurry. The boat is just leaving|crowns 241|The Ruby has been stolen! I must see King Richard|I found the Mask|Connaax|I am Baccata, Dawn's apprentice|legends speak of an Elixir|Baccata will join you in Timothy's stead|This mystic album|char1 Baccata|flags 41=1"

# ======== checkpoints (from here on): each teleports in from the return save, clears the level's hostile
# monsters (clear: killMonster, so their items drop), gives what the walkthrough says the party has by then
# (give:PROP, props from tools/item_names.mjs) and plays the level's walkthrough events ========

# ---- Caves Level 1 (level 6): the lever, the pit buttons (blue, green, red), the plate that holds door 165
# open under a dagger, the floor items, the crumbly wall broken by an equipped sledge, the pit down ----
seg caves1 "load:$W/return.json,wait:2,tp:6/452,wait:3,next:2,idle,clear,face:1,press,wait:2,next:2,\
walk:257,face:1,press,wait:2,face:2,press,wait:2,face:3,press,wait:2,flags:64,\
walk:132,wait:2,call:uiDropToFloor/7,wait:2,walk:133,walk:197,walk:168,take,\
give:59,equip:59,walk:232,face:2,call:quickAttack,wait:3,next:1,state,cflags,items" \
"Clever! The wall has moved|We just needed the right combination|triggerDoorSwitch\\(165,1|Dagger dropped|Lockpicks taken|Oil Flask taken|Ginseng taken|setWallType\\(264,0,64\\)|Now we are getting somewhere"
# the open pits drop the party to Caves Level 2 (as the walkthrough's first way down), a little hurt
seg caves1pit "load:$W/return.json,wait:2,tp:6/452,wait:3,next:2,idle,clear,face:1,press,wait:2,next:2,cflags,walk:132,wait:2,state,cflags" \
"Gadzooks|op pitDrop|DAMAGE c0 [0-9]+ by -32768|\"level\":7"

# ---- Caves Level 2 (level 7): the Emerald Eye (from Level 1's Rat Man) in the east dragon opens the east
# route; the sledge lies where the walkthrough says ----
seg caves2 "load:$W/return.json,wait:2,tp:7/294,wait:3,next:2,idle,clear,give:222,hold:222,face:1,press,wait:3,next:2,\
walk:296,state,tp:7/197,wait:3,idle,walk:196,take,state,items" \
"Emerald Eye taken|runLevelScript\\(295, 0x40\\)|Splendid!|setWallType\\(295,-1,0\\)|walk 296 -> true|Sledge taken"

# ---- Caves Level 3 (level 8): the Draracle's lair - he wants an offering; through the archway, the
# Jewelled Dagger on the altar (exit button 0), then he tells of the Elixir of Tybal and its four
# ingredients and hands over the Riddle Scroll ----
seg draracle "load:$W/return.json,wait:2,tp:8/253,wait:3,next:2,idle,clear,give:51,face:1,key:up,wait:5,next:4,\
click:103x70,wait:4,next:2,hold:51,click:199x45,wait:6,click:280x114,wait:4,next:20,state,flags:60.67.309,items,save:$W/draracle.json" \
"How dare you speak before placing an offering|place your trinket on the altar|op deleteHandItem|Let us go now and get the Elixir|Only the Elixir of Tybal|ancient white tower|You must obtain four special trophies|powders taken from the heart of your mother|Riddle Scroll taken|flags 60=1 67=1 309=1"

# ---- Lake Dread (level 2) after the Draracle: Victor, hiding in the bushes, gives a long sword and sends the
# party by boat to Opinwood (level 10), where Dawn met "some chap in a wagon" ----
seg victor "load:$W/draracle.json,wait:2,tp:2/269,wait:3,idle,clear,walk:301,wait:4,next:10,idle,state,items" \
"Ak'shel! Baccata! Shh! Dark Soldiers everywhere|Boat waits at shore|Long sword taken|She met some chap in a wagon|\"level\":10"

# ---- Gorkha Swamp (level 11): an empty flask on a swamp pit gives the Swamp Vial (the "deadly depths"
# ingredient); Ra'Tol the idol takes an item in her mouth and, nose pressed, pays 60 crowns ----
seg swamp "load:$W/draracle.json,wait:2,tp:11/130,wait:3,next:2,idle,clear,give:211,hold:211,face:1,press,wait:3,next:2,items,\
call:inventorySlotClick/10,wait:1,tp:11/282,wait:3,idle,face:3,hold:44,click:199x77,wait:3,next:2,click:201x54,wait:3,next:3,\
click:199x77,wait:3,next:2,items" \
"Empty flask taken|Swamp Vial taken|setWallType\\(281,1,74\\)|setWallType\\(281,1,76\\)|crowns 301"

# ---- Opinwood (level 10): Droek's wagon (entered from the north) - the Ruby of Truth proves Ak'shel; Dawn
# gives her key to Richard's casket, and, shown the Riddle Scroll, solves the "mother" riddle (Earth Powder).
# (The walkthrough's two empty flasks from Dawn did not turn up by any click tried: open question.) ----
seg dawn "load:$W/draracle.json,wait:2,tp:10/524,wait:3,next:2,idle,clear,give:220,face:2,key:up,wait:5,next:16,\
call:inventorySlotClick/10,wait:1,hold:241,click:233x55,wait:3,next:6,items,flags:73.78" \
"Have you seen a woman by the name of Dawn|I hold the Ruby of Truth|Dou art truly Ak'shel|Take my key to Richard's casket|Dawn's Key taken|mother of which he speaks is Mother Earth|flags 73=1 78=1"

# ---- Upper Opinwood (level 17): an empty flask on a hornet's nest gives Honey (the "sweetness of your enemy") ----
seg honey "load:$W/draracle.json,wait:2,tp:17/444,wait:3,next:2,idle,clear,give:211,hold:211,face:0,press,wait:3,next:2,items" \
"Empty flask taken|Honey taken|filled with honey"

# ---- Tower Level 1 (level 18): the Altar de Blanca (walk in from the south) - the Crucible of Faith on the
# altar, then the four ingredients on it, each riddle answered; taking the crucible back is the Elixir ----
seg elixir "load:$W/draracle.json,wait:2,tp:18/104,wait:3,next:2,idle,clear,give:260,give:273,give:212,give:213,give:214,face:0,key:up,wait:5,next:3,\
hold:260,click:192x89,wait:4,next:3,hold:273,click:201x73,wait:3,next:3,hold:212,click:201x73,wait:3,next:3,\
hold:213,click:201x73,wait:3,next:3,hold:214,click:201x73,wait:3,next:8,click:201x73,wait:3,next:4,flags:347.310,items,save:$W/elixir.json" \
"certainly ready to make the Elixir|butchered the creature whose flesh has never lived|gathered while traversing the deadly depths|seen the sweetness of your enemy|taken powders from the heart of your mother|We have the Elixir|flags 347=1 310=1|hand Crucible of Faith\\(260\\)"

# ---- City of Yvel (level 22): with the Elixir, the council - Geron will give his key to Dawn ----
seg yvel "load:$W/elixir.json,wait:2,tp:22/177,wait:4,next:2,idle,clear,face:0,key:up,wait:5,next:16,state,flags:586" \
"Are you not patriots|Where is Dawn|Go find Dawn! I'll give her my key|flags 586=1"

# ---- Castle Level 3 (level 29): Scotia struck down (flag 578, as her death leaves it) - the level's
# function 1028 has the lead hero speak and plays the ending sequence; the game is over ----
seg ending "load:$W/elixir.json,wait:2,tp:29,wait:4,next:2,idle,clear,setflag:578,call:runLevelScript/1028/65535,wait:10,\
$(for i in $(seq 1 30); do printf 'key:enter,wait:4,click:160x100,wait:2,'; done)prop:gameFinished,prop:quit" \
"testGameFlag\\(578,0,0\\) -> 1|That looks to be a mortal blow|op fadeToBlack|prop gameFinished = true|prop quit = true"

fail=0
for name in ${@:-${ORDER[@]}}; do
  r=$(bash tools/diff.sh "${STEPS[$name]}" 2>&1 | grep -E "^SAME|^@@|^ERROR" | head -1)
  cp Shots/diff/js.txt "$W/$name.js.txt"; cp Shots/diff/cs.txt "$W/$name.cs.txt"
  missing=()
  IFS='|' read -ra pats <<< "${EXPECT[$name]}"
  for p in "${pats[@]}"; do grep -qE "$p" "$W/$name.js.txt" || missing+=("$p"); done
  printf '%-12s %s%s\n' "$name" "${r:-ERROR}" "$([ ${#missing[@]} -gt 0 ] && echo "; walkthrough NOT met: ${missing[*]}")"
  [[ "$r" == SAME* && ${#missing[@]} -eq 0 ]] || fail=1
done
exit $fail
