// src/game/savegame.mjs: game state snapshot/restore (LoLEngine::saveGameStateIntern / loadGameState) as plain JSON.
// Level state lives in the per-level temp data like ScummVM does, so a snapshot covers every visited level.
// The JSON has exactly the keys (and key order) the browser writes, so saves move between the two builds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        /// <summary>lvlTempData is a sparse JS array indexed by level - 1 (levels 1..29); a fixed array here.</summary>
        public const int LVL_TEMP_SLOTS = 32;

        /// <summary>per-level block/monster state, index level - 1 (null: JS undefined)</summary>
        public LvlTempData[] lvlTempData;
        /// <summary>{ [level]: monsters } as scene.mjs first spawned them (null: JS undefined)</summary>
        public Dictionary<int, Monster[]> monsterSpawns;
        /// <summary>quests.mjs: ids of finished quests (null: JS undefined)</summary>
        public HashSet<string> questDone;

        static readonly string[] CHAR_FIELDS = { "flags", "name", "raceClassSex", "id", "curFaceFrame", "tempFaceFrame", "screamSfx", "itemProtection", "hitPointsCur",
            "hitPointsMax", "magicPointsCur", "magicPointsMax", "field_41", "damageSuffered", "weaponHit", "totalMightModifier", "totalProtectionModifier",
            "might", "protection", "nextAnimUpdateCountdown", "itemsMight", "protectionAgainstItems", "items", "skillLevels", "skillModifiers",
            "experiencePts", "characterUpdateEvents", "characterUpdateDelay", "potionSkillBonus" };

        // Own-key order of the JS objects (makeEmptyMonster without `properties`, makeEmptyItem, makeFlyingObject):
        // JSON.stringify writes them in this order.
        static readonly string[] SaveGame_MONSTER_KEYS = { "nextAssignedObject", "nextDrawObject", "flyingHeight", "block", "x", "y", "destDirection", "shiftStep", "destX", "destY",
            "hitOffsX", "hitOffsY", "currentSubFrame", "mode", "fightCurTick", "id", "direction", "facing", "flags", "damageReceived",
            "hitPoints", "speedTick", "type", "numDistAttacks", "curDistWeapon", "distAttackTick", "assignedItems", "equipmentShapes" };
        // Keys other modules add to a monster object as they go (monsters.mjs drawing and pacing, the pit,
        // NG+). JSON.stringify writes them whenever the object has them - a level borrowed by a pit floor
        // keeps its pitScale in the save - so they round-trip here: written when set, read when present.
        static readonly string[] SaveGame_MONSTER_EXTRA_KEYS = { "fleeing", "drawW", "drawH", "drawX", "drawY", "drawSerial", "attackWait", "ngplus", "dungeonBoss", "pitScale", "pitMaxHp" };
        static readonly string[] SaveGame_ITEM_KEYS = { "nextAssignedObject", "nextDrawObject", "flyingHeight", "block", "x", "y", "level", "itemPropertyIndex", "shpCurFrame_flg" };
        static readonly string[] SaveGame_FLYING_KEYS = { "enable", "objectType", "attackerId", "item", "x", "y", "flyingHeight", "direction", "distance", "field_D", "c", "flags", "wallFlags" };

        // ---- JSON helpers (JS values <-> JsonNode) ----

        /// <summary>A JSON number as JS reads it into integer code (undefined/null -> 0).</summary>
        static int SaveGame_int(JsonNode n)
        {
            if (n == null) return 0;
            var v = n.AsValue();
            if (v.TryGetValue<int>(out int i)) return i;
            if (v.TryGetValue<bool>(out bool b)) return b ? 1 : 0;
            return (int)SaveGame_double(n);
        }

        static double SaveGame_double(JsonNode n)
        {
            if (n == null) return 0;
            var v = n.AsValue();
            if (v.TryGetValue<double>(out double d)) return d;
            if (v.TryGetValue<int>(out int i)) return i;
            if (v.TryGetValue<long>(out long l)) return l;
            return double.Parse(n.ToJsonString(), CultureInfo.InvariantCulture);
        }

        /// <summary>JS truthiness of a JSON value.</summary>
        static bool SaveGame_truthy(JsonNode n)
        {
            if (n == null) return false;
            if (n is JsonValue v)
            {
                if (v.TryGetValue<bool>(out bool b)) return b;
                if (v.TryGetValue<string>(out string s)) return s.Length > 0;
                double d = SaveGame_double(n);
                return d != 0 && !double.IsNaN(d);
            }
            return true;
        }

        /// <summary>plain(value): a typed array becomes a plain number array; numbers and strings stay.</summary>
        static JsonNode SaveGame_plain(object value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return JsonValue.Create(s);
                case int i: return JsonValue.Create(i);
                case double d: return JsonValue.Create(d);
                case bool b: return JsonValue.Create(b);
                case Array a:
                    var arr = new JsonArray();
                    foreach (var e in a) arr.Add(JsonValue.Create(Convert.ToInt32(e)));
                    return arr;
                default: throw new ArgumentException($"plain: {value.GetType()}");
            }
        }

        /// <summary>typedArray.set(plainArray): stores wrap like the typed array.</summary>
        static void SaveGame_set(Array dst, JsonNode src)
        {
            var a = src.AsArray();
            for (int i = 0; i < a.Count; i += 1)
            {
                int v = SaveGame_int(a[i]);
                switch (dst)
                {
                    case byte[] u8: u8[i] = (byte)v; break;
                    case sbyte[] i8: i8[i] = (sbyte)v; break;
                    case ushort[] u16: u16[i] = (ushort)v; break;
                    case short[] i16: i16[i] = (short)v; break;
                    case int[] i32: i32[i] = v; break;
                    default: throw new ArgumentException($"set: {dst.GetType()}");
                }
            }
        }

        static int[] SaveGame_ints(JsonNode src) => src.AsArray().Select(SaveGame_int).ToArray();

        /// <summary>An object's listed fields as a JSON object ({ ...o } / ({ properties, ...m }) => m).</summary>
        static JsonObject SaveGame_toJson(object o, string[] keys)
        {
            var r = new JsonObject();
            var t = o.GetType();
            foreach (var k in keys) r[k] = SaveGame_plain(t.GetField(k).GetValue(o));
            if (o is Monster)
                foreach (var k in SaveGame_MONSTER_EXTRA_KEYS)
                {
                    var v = t.GetField(k).GetValue(o);
                    if (v is int i && i != 0) r[k] = i;
                    else if (v is double d && d != 0) r[k] = d;
                }
            return r;
        }

        /// <summary>{ ...json } onto a fresh record: every key the JSON has and the record knows.</summary>
        static T SaveGame_fromJson<T>(JsonNode json, string[] keys) where T : new()
        {
            var r = new T();
            var o = json.AsObject();
            foreach (var k in keys)
            {
                if (!o.TryGetPropertyValue(k, out var v) || v == null) continue;
                var f = typeof(T).GetField(k);
                if (f.FieldType == typeof(int)) f.SetValue(r, SaveGame_int(v));
                else if (f.FieldType == typeof(int[])) f.SetValue(r, SaveGame_ints(v));
            }
            if (r is Monster)
                foreach (var k in SaveGame_MONSTER_EXTRA_KEYS)
                {
                    if (!o.TryGetPropertyValue(k, out var v) || v == null || v.GetValueKind() != System.Text.Json.JsonValueKind.Number) continue;
                    var f = typeof(T).GetField(k);
                    if (f.FieldType == typeof(double)) f.SetValue(r, v.GetValue<double>());
                    else f.SetValue(r, SaveGame_int(v));
                }
            return r;
        }

        static JsonObject SaveGame_intKeyed<T>(IEnumerable<KeyValuePair<int, T>> entries, Func<T, JsonNode> map)
        {
            // Object.entries lists integer keys in ascending order.
            var r = new JsonObject();
            foreach (var e in entries.OrderBy(e => e.Key)) r[e.Key.ToString(CultureInfo.InvariantCulture)] = map(e.Value);
            return r;
        }

        // Debug helper: jump to another level (same path as the loadNewLevel opcode). Without a block it picks an
        // open floor block near the map centre.
        public async Task debugTeleport(int level, int? block = null, int direction = 0)
        {
            if (level < 1 || level > 29) return;
            await res.loadPak($"L{level.ToString().PadLeft(2, '0')}.PAK");
            if (block == null)
            {
                var cmz = res.get($"LEVEL{level}.CMZ");
                var data = Cps.decodeBitmapData(cmz).data;
                int len = data[4] | (data[5] << 8);
                int wall(int b, int side) => data[6 + b * len + side];
                bool open(int b) => wall(b, 0) == 0 && wall(b, 1) == 0 && wall(b, 2) == 0 && wall(b, 3) == 0;
                bool passable(int w) => w == 0 || (w >= 3 && w <= 22); // open or any door state
                // Largest connected floor area, then the block nearest the map centre inside it.
                var comp = new short[1024];
                Js.Fill(comp, (short)-1);
                var sizes = new List<int>();
                for (int b = 0; b < 1024; b += 1)
                {
                    if (!open(b) || comp[b] >= 0) continue;
                    int id = sizes.Count;
                    var q = new List<int> { b };
                    comp[b] = (short)id;
                    int n = 0;
                    while (q.Count > 0)
                    {
                        int c = q[q.Count - 1];
                        q.RemoveAt(q.Count - 1);
                        n += 1;
                        int x = c & 31;
                        int y = c >> 5;
                        foreach (var (nx, ny, side) in new[] { (x, y - 1, 2), (x + 1, y, 3), (x, y + 1, 0), (x - 1, y, 1) })
                        {
                            if (nx < 0 || nx > 31 || ny < 0 || ny > 31) continue;
                            int nb = (ny << 5) + nx;
                            if (comp[nb] >= 0 || !open(nb) || !passable(wall(nb, side))) continue;
                            comp[nb] = (short)id;
                            q.Add(nb);
                        }
                    }
                    sizes.Add(n);
                }
                int largest = sizes.IndexOf(Math.Max(0, sizes.Count > 0 ? sizes.Max() : 0));
                int best = -1;
                double bestDist = double.PositiveInfinity;
                for (int b = 0; b < 1024; b += 1)
                {
                    if (comp[b] != largest) continue;
                    int dist = ((b & 31) - 16) * ((b & 31) - 16) + ((b >> 5) - 16) * ((b >> 5) - 16);
                    if (dist < bestDist) { bestDist = dist; best = b; }
                }
                block = best >= 0 ? best : 0;
            }
            await screen.fadeClearSceneWindow(10);
            screen.fillRect(112, 0, 288, 120, 0);
            disableSysTimer(2);
            completeDoorOperations();
            generateTempData();
            currentBlock = block.Value;
            currentDirection = direction & 3;
            (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
            await loadLevel(level);
            enableSysTimer(2);
            sceneUpdateRequired = true;
        }

        // Returns a JSON-serializable snapshot of the running game.
        public JsonObject saveState()
        {
            generateTempData();
            // Items lying on this level only get their level tag when leaving it (resetItems); tag them now so
            // addLevelItems() finds them again after loading.
            // Only the head of a block's item chain carries the level tag; the rest hang off its nextAssignedObject.
            for (int i = 0; i < 1024; i += 1)
            {
                int id = levelBlockProperties[i].assignedObjects;
                while ((id & 0x8000) != 0) id = findObject(id).nextAssignedObject;
                bool head = true;
                var seen = new HashSet<int>();
                for (; id != 0 && !seen.Contains(id); id = itemsInPlay[id].nextAssignedObject, head = false)
                {
                    seen.Add(id);
                    itemsInPlay[id].level = head ? currentLevel : -1;
                    itemsInPlay[id].block = i;
                }
            }
            var characters = new JsonArray(this.characters.Select(c => (JsonNode)SaveGame_toJson(c, CHAR_FIELDS)).ToArray());
            var spawns = SaveGame_intKeyed(monsterSpawns ?? new Dictionary<int, Monster[]>(),
                list => new JsonArray(list.Select(m => (JsonNode)SaveGame_toJson(m, SaveGame_MONSTER_KEYS)).ToArray()));
            var levels = new JsonObject();
            for (int i = 0; i < 29; i += 1)
            {
                var t = lvlTempData != null && i < lvlTempData.Length ? lvlTempData[i] : null;
                if ((hasTempDataFlags & (1 << i)) == 0 || t == null) continue;
                levels[i.ToString(CultureInfo.InvariantCulture)] = new JsonObject
                {
                    ["walls"] = new JsonArray(t.walls.Select(w => SaveGame_plain(w)).ToArray()),
                    ["flags"] = SaveGame_plain(t.flags),
                    ["monsters"] = new JsonArray(t.monsters.Select(m => (JsonNode)SaveGame_toJson(m, SaveGame_MONSTER_KEYS)).ToArray()),
                    ["flyingObjects"] = new JsonArray(t.flyingObjects.Select(f => (JsonNode)SaveGame_toJson(f, SaveGame_FLYING_KEYS)).ToArray()),
                    ["monsterDifficulty"] = t.monsterDifficulty,
                };
            }
            return new JsonObject
            {
                ["version"] = 1,
                ["characters"] = characters,
                ["currentBlock"] = currentBlock, ["partyPosX"] = partyPosX, ["partyPosY"] = partyPosY, ["updateFlags"] = 0,
                ["scriptDirection"] = scriptDirection, ["selectedSpell"] = selectedSpell, ["sceneDefaultUpdate"] = sceneDefaultUpdate,
                ["compassBroken"] = compassBroken, ["drainMagic"] = drainMagic, ["currentDirection"] = currentDirection,
                ["compassDirection"] = compassDirection, ["selectedCharacter"] = selectedCharacter, ["currentLevel"] = currentLevel,
                ["inventory"] = SaveGame_plain(inventory), ["inventoryCurItem"] = inventoryCurItem, ["itemInHand"] = itemInHand, ["lastMouseRegion"] = lastMouseRegion,
                ["flagsTable"] = SaveGame_plain(flagsTable), ["globalScriptVars"] = SaveGame_plain(globalScriptVars), ["brightness"] = brightness,
                ["lampOilStatus"] = lampOilStatus, ["lampEffect"] = lampEffect, ["credits"] = credits, ["globalScriptVars2"] = SaveGame_plain(globalScriptVars2),
                ["availableSpells"] = SaveGame_plain(availableSpells), ["hasTempDataFlags"] = hasTempDataFlags, ["playTimer"] = playTimer,
                ["itemsInPlay"] = new JsonArray(itemsInPlay.Select(it => (JsonNode)SaveGame_toJson(it, SaveGame_ITEM_KEYS)).ToArray()), ["levels"] = levels, ["monsterSpawns"] = spawns,
                ["charSelection"] = charSelection,
                ["lampSwitchedOff"] = lampSwitchedOff,
                ["questDone"] = new JsonArray((questDone ?? new HashSet<string>()).Select(q => (JsonNode)JsonValue.Create(q)).ToArray()),
                ["dungeon"] = uiDungeonSave(), // a run through the imp's pit
                // Unity port: briars of Wall of Thorns still growing (without them they would never wither)
                ["campReserve"] = campReserve != null && campReserve.Count > 0 ? new JsonArray(campReserve.Select(j => (JsonNode)j.DeepClone()).ToArray()) : null,
                ["thornBlocks"] = thornBlocks != null && thornBlocks.Count > 0 ? JsonSerializer.SerializeToNode(thornBlocks, new JsonSerializerOptions { IncludeFields = true }) : null,
            };
        }

        // Restores a snapshot; the engine must have run preInit/setupTimers/startup first.
        /// <summary>The typed array each int[] Character field is in makeEmptyCharacter: 8 = Uint8Array, -8 = Int8Array.</summary>
        static readonly Dictionary<string, int> SaveGame_typedAs = new Dictionary<string, int>
        {
            ["skillLevels"] = 8, ["skillModifiers"] = -8, ["characterUpdateEvents"] = 8, ["characterUpdateDelay"] = 8,
        };

        /// <summary>A saved character back (loadState's reading; Companions.cs reads the camp's the same way).</summary>
        Character characterFromJson(JsonObject saved, int[][] cdf = null)
        {
            var S = @static;
            cdf = cdf ?? new[] { S.CharDefsMan, S.CharDefsWoman, S.CharDefsKieran, S.CharDefsMan, S.CharDefsAkshel };
            var c = makeEmptyCharacter();
            foreach (var k in CHAR_FIELDS)
            {
                if (!saved.TryGetPropertyValue(k, out var v) || v == null) continue; // a save written before this field existed
                var f = typeof(Character).GetField(k);
                if (f.FieldType.IsArray)
                {
                    var arr = (Array)f.GetValue(c);
                    SaveGame_set(arr, v);
                    // makeEmptyCharacter's typed arrays take the values here (c[k].set(v)), so they
                    // wrap: a saved poison delay of 3600 comes back as 16. The C# fields are int[] for
                    // addCharacter's plain arrays; wrap them the way the Uint8Array / Int8Array would.
                    if (arr is int[] ints && SaveGame_typedAs.TryGetValue(k, out var bits))
                        for (int n = 0; n < ints.Length; n += 1) ints[n] = bits == 8 ? ints[n] & 0xff : (sbyte)ints[n];
                }
                else if (f.FieldType == typeof(string)) f.SetValue(c, v.GetValue<string>());
                else f.SetValue(c, SaveGame_int(v));
            }
            if ((c.flags & 1) != 0)
            {
                c.defaultModifiers = cdf[c.raceClassSex];
                // Transient combat state must not outlive its timer event (a save taken while a script had the
                // timers paused would otherwise keep the attack button locked forever).
                c.flags &= ~0x2000;
                if (!c.characterUpdateEvents.Any(e => e == 1)) { c.flags &= ~4; c.weaponHit = 0; }
            }
            return c;
        }

        public async Task loadState(JsonObject save)
        {
            var S = @static;
            int[][] cdf = { S.CharDefsMan, S.CharDefsWoman, S.CharDefsKieran, S.CharDefsMan, S.CharDefsAkshel };
            for (int i = 0; i < 4; i += 1)
            {
                var c = characterFromJson(save["characters"][i].AsObject(), cdf);
                if ((c.flags & 1) != 0) loadCharFaceShapes(i, c.id);
                characters[i] = c;
            }
            // Unity port: the companions waiting in the camp (Companions.cs)
            campReserve = save["campReserve"] is JsonArray cr ? cr.Select(n => n.AsObject().DeepClone().AsObject()).ToList() : new List<JsonObject>();
            currentBlock = SaveGame_int(save["currentBlock"]);
            partyPosX = SaveGame_int(save["partyPosX"]);
            partyPosY = SaveGame_int(save["partyPosY"]);
            updateFlags = SaveGame_int(save["updateFlags"]);
            scriptDirection = SaveGame_int(save["scriptDirection"]);
            selectedSpell = SaveGame_int(save["selectedSpell"]);
            sceneDefaultUpdate = SaveGame_int(save["sceneDefaultUpdate"]);
            compassBroken = SaveGame_int(save["compassBroken"]);
            drainMagic = SaveGame_int(save["drainMagic"]);
            currentDirection = SaveGame_int(save["currentDirection"]);
            compassDirection = SaveGame_int(save["compassDirection"]);
            selectedCharacter = SaveGame_int(save["selectedCharacter"]);
            currentLevel = SaveGame_int(save["currentLevel"]);
            inventoryCurItem = SaveGame_int(save["inventoryCurItem"]);
            itemInHand = SaveGame_int(save["itemInHand"]);
            lastMouseRegion = SaveGame_int(save["lastMouseRegion"]);
            brightness = SaveGame_int(save["brightness"]);
            lampOilStatus = SaveGame_int(save["lampOilStatus"]);
            lampEffect = SaveGame_int(save["lampEffect"]);
            hasTempDataFlags = SaveGame_int(save["hasTempDataFlags"]);
            playTimer = SaveGame_double(save["playTimer"]);
            charSelection = SaveGame_int(save["charSelection"]);
            lampSwitchedOff = SaveGame_truthy(save["lampSwitchedOff"]);
            questDone = new HashSet<string>(save["questDone"] is JsonArray qd ? qd.Select(q => q.GetValue<string>()) : Enumerable.Empty<string>());
            var inv = save["inventory"].AsArray();
            if (inv.Count > inventory.Length) setInventorySize(inv.Count);
            Js.Fill(inventory, (ushort)0); SaveGame_set(inventory, inv);
            SaveGame_set(flagsTable, save["flagsTable"]);
            SaveGame_set(globalScriptVars, save["globalScriptVars"]);
            SaveGame_set(globalScriptVars2, save["globalScriptVars2"]);
            SaveGame_set(availableSpells, save["availableSpells"]);
            // A spell the save knows but this build does not (an index from another set of extra spells)
            // is forgotten here. Left in place it is a hole in SpellProperties, and the first thing that
            // reads a spell's mana cost takes the engine loop down with it.
            for (int i = 0; i < availableSpells.Length; i += 1)
            {
                int spell = availableSpells[i];
                if (spell >= 0 && (spell >= @static.SpellProperties.Length || @static.SpellProperties[spell] == null)) availableSpells[i] = -1;
            }
            itemsInPlay = save["itemsInPlay"].AsArray().Select(it => SaveGame_fromJson<Item>(it, SaveGame_ITEM_KEYS)).ToArray();
            // Normalise floor-item chains: only chain heads may carry a level tag, and chains must not loop
            // (older autosaves tagged every member, which produced a self-referencing list on load).
            for (int i = 1; i < 400; i += 1)
            {
                var head = itemsInPlay[i];
                if (head.level < 1 || head.level > 29) continue;
                var seen = new HashSet<int> { i };
                var prev = head;
                for (int id = head.nextAssignedObject; id != 0; id = itemsInPlay[id].nextAssignedObject)
                {
                    if (seen.Contains(id) || (id & 0x8000) != 0) { prev.nextAssignedObject = 0; break; }
                    seen.Add(id);
                    var it = itemsInPlay[id];
                    if (it.level == head.level) it.level = -1;
                    prev = it;
                }
            }
            foreach (var l in levelBlockProperties)
            {
                l.assignedObjects = l.drawObjects = 0;
                l.direction = 5;
            }
            monsterSpawns = new Dictionary<int, Monster[]>();
            if (save["monsterSpawns"] is JsonObject savedSpawns)
            {
                foreach (var e in savedSpawns)
                    monsterSpawns[int.Parse(e.Key, CultureInfo.InvariantCulture)] = e.Value.AsArray().Select(m => SaveGame_fromJson<Monster>(m, SaveGame_MONSTER_KEYS)).ToArray();
            }
            lvlTempData = new LvlTempData[LVL_TEMP_SLOTS];
            foreach (var e in save["levels"].AsObject())
            {
                var t = e.Value.AsObject();
                lvlTempData[int.Parse(e.Key, CultureInfo.InvariantCulture)] = new LvlTempData
                {
                    walls = t["walls"].AsArray().Select(w => SaveGame_ints(w).Select(v => (byte)v).ToArray()).ToArray(),
                    flags = SaveGame_ints(t["flags"]),
                    monsters = t["monsters"].AsArray().Select(mj =>
                    {
                        var m = SaveGame_fromJson<Monster>(mj, SaveGame_MONSTER_KEYS);
                        m.properties = monsterProperties[m.type];
                        return m;
                    }).ToArray(),
                    flyingObjects = t["flyingObjects"].AsArray().Select(f => SaveGame_fromJson<FlyingObject>(f, SaveGame_FLYING_KEYS)).ToArray(),
                    monsterDifficulty = SaveGame_int(t["monsterDifficulty"]),
                };
            }
            calcCharPortraitXpos();
            Js.Fill(moneyColumnHeight, (byte)0);
            int credits = SaveGame_int(save["credits"]);
            this.credits = 0;
            await giveCredits(credits, 0);
            await setHandItem(itemInHand);
            curTlkFile = -1;
            await loadLevel(currentLevel);
            if (repairOnLoad) uiRepairEquipment(); // the game's page asks for it; the parity tracers do not
            // The pit: the floor itself came back with the level temp data; this restores the run around it.
            // It has to follow loadLevel, which would otherwise re-attach the borrowed level's script.
            uiDungeonLoad(save["dungeon"] as JsonObject);
            if (companionsBench && dungeon == null) await takeBackSigils();   // a sigil carried out of the pit before this rule
            try { thornRestore = save["thornBlocks"] is JsonArray tb ? JsonSerializer.Deserialize<List<ThornBlock>>(tb.ToJsonString(), new JsonSerializerOptions { IncludeFields = true }) : null; }
            catch (Exception) { thornRestore = null; }
            if (thornBlocks != null) thornBlocks.Clear();
            gui_drawPlayField();
            timerSpecialCharacterUpdate();
        }
    }
}
