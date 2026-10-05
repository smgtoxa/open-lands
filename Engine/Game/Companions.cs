// Unity port: companions waiting in the camp. The party holds three; the original refuses (or, with Lora still along
// after the Draracle, crashes on) a fourth. Here the one who joins takes the place of a companion who goes to wait in
// the camp, and the imp can swap them back. Also: Lora's pod room on Caves Level 2 is never sealed while she is still
// in her pod (the original closed it for good once the party met the Draracle). Host rules: companionsBench on.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        /// <summary>host setting: a full party benches a companion instead of refusing a newcomer</summary>
        public bool companionsBench;
        /// <summary>the companions waiting in the camp, as saved characters</summary>
        public List<JsonObject> campReserve = new List<JsonObject>();
        string lastCmzFile;

        const int LORA = 4;

        /// <summary>The party is full and `id` joins: a companion makes room (Lora first, else the last who joined;
        /// never the champion). Returns the slot freed, -1 when nobody can make room.</summary>
        int benchForNewcomer(int id, int chosen = -1)
        {
            int slot = chosen;
            if (slot < 0) for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0 && characters[i].id == LORA && id != LORA) slot = i;
            if (slot < 0) for (int i = 3; i >= 0; i -= 1) if ((characters[i].flags & 1) != 0 && characters[i].id > 0 && characters[i].id != id) { slot = i; break; }
            if (slot < 0) return -1;
            var c = characters[slot];
            campReserve.Add(SaveGame_toJson(c, CHAR_FIELDS));
            ui?.Invoke("message", new object[] { $"{c.name} goes to wait in the camp. The imp can bring {(c.raceClassSex == 1 ? "her" : "him")} back.", "note" });
            // the party stays packed: the ones after the freed slot move up, the last slot is where addCharacter puts the newcomer
            for (int i = slot; i < 3; i += 1) { characters[i] = characters[i + 1]; characterFaceShapes[i] = characterFaceShapes[i + 1]; }
            characters[3] = makeEmptyCharacter();
            characterFaceShapes[3] = null;
            if (selectedCharacter >= slot && selectedCharacter > 0) selectedCharacter -= 1;
            calcCharPortraitXpos();
            return slot;
        }

        /// <summary>The waiting companion `reserve` (its index) takes party slot `slot`; whoever stood there waits instead.</summary>
        public bool uiSwapCompanion(int reserve, int slot)
        {
            if (reserve < 0 || reserve >= campReserve.Count || slot < 0 || slot > 3) return false;
            var cur = characters[slot];
            if ((cur.flags & 1) == 0 || cur.id <= 0) return false;   // never the champion
            var back = characterFromJson(campReserve[reserve]);
            campReserve[reserve] = SaveGame_toJson(cur, CHAR_FIELDS);
            characters[slot] = back;
            loadCharFaceShapes(slot, back.id);
            calcCharPortraitXpos();
            if (updateFlags == 0) { gui_enableDefaultPlayfieldButtons(); gui_drawPlayField(); }
            ui?.Invoke("message", new object[] { $"{back.name} rejoins the party; {cur.name} waits in the camp.", "note" });
            return true;
        }

        const int TIMOTHY = 2, BACCATA = 3;
        /// <summary>Set in Timothy's dying scene when the party gives him the Perfect Healing Potion.</summary>
        public bool timothyRescued;

        /// <summary>The party is full and `id` joins: the player picks who waits in the camp (never the champion or
        /// Baccata, whom the story needs). One possible choice is made without asking. Returns the slot benched.</summary>
        public async Task<int> pickWhoWaits(int id)
        {
            if (!companionsBench || countActiveCharacters() < 3) return -1;
            var can = new List<int>();
            for (int i = 0; i < 4; i += 1)
                if ((characters[i].flags & 1) != 0 && characters[i].id > 0 && characters[i].id != BACCATA && characters[i].id != id) can.Add(i);
            if (can.Count == 0) return -1;
            int pick = can[0];
            if (can.Count > 1)
            {
                string newcomer = S_nameOf(id);
                ui?.Invoke("message", new object[] { $"The party is full. Who waits in the camp while {newcomer} joins?", "note" });
                var names = can.Take(3).Select(i => characters[i].name).ToList();
                while (names.Count < 3) names.Add(null);
                setupDialogueButtons(Math.Min(3, can.Count), names[0], names[1], names[2]);
                int r = await runDialogue();
                pick = can[Math.Max(0, Math.Min(can.Count - 1, r - 1))];
            }
            return benchForNewcomer(id, pick);
        }

        /// <summary>After a join that sent someone to the camp: the player picks who travels as the third companion -
        /// the newcomer, or one of those waiting (Timothy, Paulson, Lora...); the others wait in the camp.</summary>
        public async Task chooseCompanion(int newcomer)
        {
            if (!companionsBench || campReserve.Count == 0) return;
            int slot = -1;
            for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0 && characters[i].id == newcomer) slot = i;
            if (slot < 0) return;
            var waiting = campReserve.Take(2).Select(j => j["name"]?.GetValue<string>() ?? "?").ToList();
            ui?.Invoke("message", new object[] { "Who travels with you? The others wait in the camp, and the imp can swap them later.", "note" });
            setupDialogueButtons(1 + waiting.Count, characters[slot].name, waiting[0], waiting.Count > 1 ? waiting[1] : null);
            int r = await runDialogue();
            if (r >= 2) uiSwapCompanion(r - 2, slot);
        }

        string S_nameOf(int id) => @static.CharacterDefs.FirstOrDefault(d => d.id == id)?.name ?? "the newcomer";

        /// <summary>A companion joins outside the original's scripts (Timothy, saved): who waits is asked first, and
        /// Timothy is brought up to the party - each skill to the average the others have earned - and healed.</summary>
        public async Task<bool> companionJoins(int id)
        {
            bool full = await pickWhoWaits(id) >= 0;
            if (!addCharacter(id)) return false;
            int slot = -1;
            for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0 && characters[i].id == id) slot = i;
            if (slot < 0) return false;
            var others = characters.Where((c, i) => i != slot && (c.flags & 1) != 0).ToList();
            if (id == TIMOTHY && others.Count != 0)
                for (int skill = 0; skill < 3; skill += 1)
                {
                    int avg = (int)others.Average(c => (double)c.experiencePts[skill]);
                    int gain = avg - characters[slot].experiencePts[skill];
                    if (gain > 0) increaseExperience(slot, skill, gain);
                }
            var t = characters[slot];
            t.hitPointsCur = t.hitPointsMax;
            t.magicPointsCur = t.magicPointsMax;
            calcCharPortraitXpos();
            if (updateFlags == 0) { gui_enableDefaultPlayfieldButtons(); gui_drawPlayField(); }
            if (full) await chooseCompanion(id);
            return true;
        }

        /// <summary>The pit's floor-5 master drops the Perfect Healing Potion while it can still matter: Timothy not met
        /// dying yet, not with the party, and no such potion carried or in the chest.</summary>
        public bool uiPerfectPotionWanted()
        {
            if (queryGameFlag(70) != 0) return false;
            if (characters.Any(c => (c.flags & 1) != 0 && c.id == TIMOTHY) || campReserve.Any(j => j["id"]?.GetValue<int>() == TIMOTHY)) return false;
            return !carriesPerfectPotion(true);
        }

        /// <summary>The Perfect Healing Potion in the bag, the hand or a hero's slots (or, with `stash`, the chest).</summary>
        public bool carriesPerfectPotion(bool stash = false)
        {
            bool isIt(int item) => item != 0 && uiExtraItem(item)?.id == "perfectheal";
            if (isIt(itemInHand) || inventory.Any(i => isIt(i)) || characters.Any(c => (c.flags & 1) != 0 && c.items.Any(i => isIt(i)))) return true;
            if (!stash || store?.stash == null || extraItems == null) return false;
            return store.stash.Any(e => e != null && extraItems.TryGetValue(e.prop, out var def) && def.id == "perfectheal");
        }

        /// <summary>Takes the Perfect Healing Potion out of the party's hands (given to Timothy).</summary>
        void usePerfectPotion()
        {
            bool isIt(int item) => item != 0 && uiExtraItem(item)?.id == "perfectheal";
            if (isIt(itemInHand)) { int it = itemInHand; _ = setHandItem(0); deleteItem(it); return; }
            for (int i = 0; i < inventory.Length; i += 1) if (isIt(inventory[i])) { deleteItem(inventory[i]); inventory[i] = 0; gui_drawInventory(); return; }
            foreach (var c in characters) for (int s = 0; s < c.items.Length; s += 1) if (isIt(c.items[s])) { deleteItem(c.items[s]); c.items[s] = 0; return; }
        }

        /// <summary>The names of the companions waiting in the camp.</summary>
        public List<string> uiCampReserveNames() => campReserve.Select(j => j["name"]?.GetValue<string>() ?? "?").ToList();

        /// <summary>After a level is loaded: Caves Level 2's pod room stays open while Lora is in her pod.</summary>
        void keepPodRoomOpen(int index)
        {
            if (!companionsBench || index != 7) return;
            bool loraFreed = queryGameFlag(56) != 0 || characters.Any(c => (c.flags & 1) != 0 && c.id == LORA) || campReserve.Any(j => j["id"]?.GetValue<int>() == LORA);
            if (loraFreed) return;
            resetGameFlag(588);   // set on meeting the Draracle: the scripts by the button then wall the pod room up
            // a pod room already walled up gets its own walls back (the level's map file)
            const int POD = 98;
            if (levelBlockProperties[POD].walls.All(w => w == 1) && lastCmzFile != null)
            {
                try
                {
                    var data = Cps.decodeBitmapData(res.get(lastCmzFile)).data;
                    int len = data[4] | (data[5] << 8);
                    for (int k = 0; k < 4; k += 1) levelBlockProperties[POD].walls[k] = data[6 + POD * len + k];
                }
                catch (Exception) { /* the map file is not readable: the room stays as it is */ }
            }
        }
    }
}
