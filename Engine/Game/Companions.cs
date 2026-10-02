// Unity port: companions waiting in the camp. The party holds three; the original refuses (or, with Lora still along
// after the Draracle, crashes on) a fourth. Here the one who joins takes the place of a companion who goes to wait in
// the camp, and the imp can swap them back. Also: Lora's pod room on Caves Level 2 is never sealed while she is still
// in her pod (the original closed it for good once the party met the Draracle). Host rules: companionsBench on.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

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
        int benchForNewcomer(int id)
        {
            int slot = -1;
            for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0 && characters[i].id == LORA && id != LORA) slot = i;
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
