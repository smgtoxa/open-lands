// src/platform/saves.mjs. C# 9.
// Save slots in localStorage: "quick" (F5/F9), "auto" (written when the page is left), manual slots
// 1-8 and three rotating checkpoints.
//
// A slot holds two things: the engine snapshot, and the campaign state the port keeps beside it -
// the chest, the pouch, the camp upgrades, the imp's errand, the pit, the journal, the map notes.
// Those used to live only in global browser keys, so loading an older save rolled the engine back
// while the chest kept whatever was put in it, which duplicated items. They now ride in the slot.
//
// The store ("lol.saves") is kept as JSON nodes, not classes, so a slot written by the browser build
// comes back out byte for byte (same keys, same shapes) and moves between builds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace LolHost
{
    /// <summary>What listSaves returns per slot ({ slot, time, level, block, name, thumb }); latestSave's
    /// `{ slot, ...entry }` also carries the entry's state and side.</summary>
    public sealed class SaveInfo
    {
        public string slot;
        public double time;
        public int level, block;
        public string name, thumb;
        public JsonNode state, side;
    }

    /// <summary>writeSave's `extra`: a user-given name and a small screenshot data URL (null = not given).</summary>
    public sealed class SaveExtra
    {
        public string name;
        public string thumb;
    }

    public static class Saves
    {
        const string KEY = "lol.saves";
        const string LEGACY_KEY = "lol.autosave";

        public const int MANUAL_SLOTS = 8;
        public const int SAVE_VERSION = 1; // the engine snapshot format (src/game/savegame.mjs)

        // What belongs to this playthrough and travels inside every slot.
        public static readonly string[] CAMPAIGN_KEYS = { "lol.stash", "lol.craft", "lol.impjob", "lol.campup", "lol.loadouts", "lol.dungeon",
            "lol.visited", "lol.stats", "lol.journal", "lol.notes", "lol.npcs", "lol.itemdb",
            "lol.hotbar", "lol.hotbar.kind", "lol.quickSpell", "lol.quickSpells", "lol.spellPower", "lol.hiddenSpells" };
        // What belongs to the browser, not to a campaign: kept global, still carried by export/import.
        public static readonly string[] PREFERENCE_KEYS = { "lol.settings", "lol.filter", "lol.mods" };

        static bool unreadable = false; // a store that will not parse is never overwritten by the next write

        static JsonObject readAll()
        {
            string raw = safeGet(KEY);
            JsonObject saves = new JsonObject();
            if (raw != null)
            {
                try
                {
                    JsonNode parsed = JsonNode.Parse(raw);
                    // port: only a JSON object is a store (the JS also took an array as one; nothing writes that)
                    saves = parsed is JsonObject o ? jsOrder(o) : new JsonObject();
                    unreadable = false;
                }
                catch (Exception)
                {
                    // Corrupt or half-written: report nothing rather than "no saves", and refuse to write over it.
                    unreadable = true;
                    return new JsonObject();
                }
            }
            else unreadable = false;
            migrateLegacy(saves);
            return saves;
        }

        // The old single autosave key, moved into the slot store exactly once. The new store is written and
        // read back before the old key is touched: losing both is what the first version of this did.
        static void migrateLegacy(JsonObject saves)
        {
            string legacy = null;
            try { legacy = Store.storage.getItem(LEGACY_KEY); } catch (Exception) { return; }
            if (string.IsNullOrEmpty(legacy)) return;
            try
            {
                JsonNode state = JsonNode.Parse(legacy);
                var entry = new JsonObject { ["time"] = now() };
                setIf(entry, "level", get(state, "currentLevel"));
                setIf(entry, "block", get(state, "currentBlock"));
                entry["state"] = state;
                var merged = (JsonObject)saves.DeepClone();
                // Never bury an auto slot under the legacy key. The legacy save carries no timestamp of its
                // own - it is stamped as it is migrated - so comparing times only says which ran first, not
                // which is newer. A slot that exists was written by this build and is therefore the later one.
                if (!Store.truthy(merged["auto"])) merged["auto"] = entry;
                merged = jsOrder(merged);
                string text = Store.stringify(merged);
                if (!Store.writeRaw(KEY, text)) return;  // keep the legacy key: it is still the only copy
                if (safeGet(KEY) != text) return; // it did not really land
                Store.removeKey(LEGACY_KEY);
                // Only once it is stored does the migrated slot count as a save.
                foreach (var kv in entries(merged)) saves[kv.Key] = kv.Value?.DeepClone();
            }
            catch (Exception)
            {
                // Unparsable legacy data: leave it alone, and leave the modern slots readable.
            }
        }

        static string safeGet(string key)
        {
            try { return Store.storage.getItem(key); } catch (Exception) { return null; }
        }

        // Writes the whole store. Returns false (and leaves everything as it was) when storage refuses.
        static bool writeAll(JsonObject saves)
        {
            if (unreadable) return false;
            return Store.writeJson(KEY, jsOrder(saves));
        }

        public static string storageError() { return Store.lastStorageFailure(); }
        public static bool savesUnreadable() { return unreadable; }

        public static List<SaveInfo> listSaves()
        {
            var saves = readAll();
            return entries(saves).Select(kv => new SaveInfo
            {
                slot = kv.Key, time = time(kv.Value), level = Store.truncOr0(Store.jsNumber(get(kv.Value, "level"))),
                block = Store.truncOr0(Store.jsNumber(get(kv.Value, "block"))), name = str(get(kv.Value, "name")), thumb = str(get(kv.Value, "thumb")),
            }).ToList();
        }

        public static JsonObject readSave(string slot)
        {
            var entry = readAll()[slot];
            return Store.truthy(entry) ? get(entry, "state")?.DeepClone() as JsonObject : null;
        }

        // The campaign state stored with a slot, or null for a slot written before this existed.
        public static JsonObject readSide(string slot)
        {
            var entry = readAll()[slot];
            return Store.truthy(entry) && get(entry, "side") is JsonObject side ? (JsonObject)side.DeepClone() : null;
        }

        // Most recent save of any slot, for the title screen's Continue.
        public static SaveInfo latestSave()
        {
            SaveInfo best = null;
            foreach (var kv in entries(readAll()))
            {
                var entry = kv.Value;
                if (best == null || time(entry) > best.time)
                    best = new SaveInfo
                    {
                        slot = kv.Key, time = time(entry), level = Store.truncOr0(Store.jsNumber(get(entry, "level"))),
                        block = Store.truncOr0(Store.jsNumber(get(entry, "block"))), name = str(get(entry, "name")), thumb = str(get(entry, "thumb")),
                        state = get(entry, "state")?.DeepClone(), side = get(entry, "side")?.DeepClone(),
                    };
            }
            return best;
        }

        // What the campaign keys hold right now, to be stored alongside a snapshot.
        public static JsonObject collectSide()
        {
            var side = new JsonObject();
            foreach (var key in CAMPAIGN_KEYS) { string v = safeGet(key); if (v != null) side[key] = v; }
            return side;
        }

        // Puts a slot's campaign state back. Keys the slot does not carry are cleared, so one campaign never
        // leaks into another. Returns false if any write was refused.
        public static bool restoreSide(JsonObject side)
        {
            if (side == null) return true;
            bool ok = true;
            foreach (var key in CAMPAIGN_KEYS)
            {
                if (side[key] is JsonValue v && v.TryGetValue(out string s)) { if (!Store.writeRaw(key, s)) ok = false; }
                else if (!Store.removeKey(key)) ok = false;
            }
            return ok;
        }

        // A new campaign: every campaign key cleared, so nothing the last one earned comes with it. Without
        // this a new game began with the previous one's journal, its chest and its pit already several
        // floors down - the slot carried its own state correctly, but a game that had never been saved yet
        // was still reading whatever the last one left in the browser.
        public static bool clearSide() { return restoreSide(new JsonObject()); }

        // `extra` may carry a user-given name and a small screenshot data URL (kept from the previous save
        // of the slot when not given again).
        public static bool writeSave(string slot, JsonObject state, SaveExtra extra = null)
        {
            extra = extra ?? new SaveExtra();
            var saves = readAll();
            JsonNode old = Store.truthy(saves[slot]) ? saves[slot] : new JsonObject();
            var entry = new JsonObject { ["time"] = now() };
            setIf(entry, "level", state["currentLevel"]);
            setIf(entry, "block", state["currentBlock"]);
            entry["name"] = extra.name != null ? JsonValue.Create(extra.name) : orEmpty(get(old, "name"));
            entry["thumb"] = !string.IsNullOrEmpty(extra.thumb) ? JsonValue.Create(extra.thumb) : orEmpty(get(old, "thumb"));
            entry["state"] = state.Parent == null ? state : state.DeepClone();
            entry["side"] = collectSide();
            saves[slot] = entry;
            return writeAll(saves);
        }

        public static bool renameSave(string slot, string name)
        {
            var saves = readAll();
            if (!Store.truthy(saves[slot])) return false;
            if (saves[slot] is JsonObject entry) entry["name"] = name;
            return writeAll(saves);
        }

        // Export/import: one JSON file with every slot (each with its own campaign state) plus the browser's
        // own preferences.
        public static string exportSaves()
        {
            var extras = new JsonObject();
            foreach (var key in PREFERENCE_KEYS) { string v = safeGet(key); if (v != null) extras[key] = v; }
            return Store.stringify(new JsonObject
            {
                ["format"] = "lol-saves", ["version"] = 1,
                ["exported"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                ["saves"] = readAll(), ["extras"] = extras,
            });
        }

        // `mode`: "merge" keeps existing slots the file does not have, "replace" drops them. Returns the slot count.
        public static int importSaves(string text, string mode = "merge")
        {
            JsonNode data = JsonNode.Parse(text);
            JsonNode fileSaves = Store.truthy(data) ? get(data, "saves") : null;
            // port: `typeof data.saves !== "object"` also let null through (to a TypeError); here it is this error
            if (!Store.truthy(data) || str(get(data, "format")) != "lol-saves" || !(fileSaves is JsonObject || fileSaves is JsonArray)) throw new Exception("Not an Open Lands save file");
            JsonNode version = get(data, "version");
            if (version != null && Store.jsNumber(version) > 1) throw new Exception($"This file was written by a newer version of the game (format {version}).");
            var saves = mode == "replace" ? new JsonObject() : readAll();
            int count = 0;
            var fileEntries = fileSaves is JsonObject fo ? entries(fo) : ((JsonArray)fileSaves).Select((v, i) => new KeyValuePair<string, JsonNode>(i.ToString(CultureInfo.InvariantCulture), v)).ToList();
            foreach (var kv in fileEntries)
            {
                var entry = kv.Value;
                if (!(entry is JsonObject) || !Store.truthy(entry["state"])) continue;
                saves[kv.Key] = entry.DeepClone();
                count += 1;
            }
            if (!writeAll(saves)) throw new Exception(!string.IsNullOrEmpty(Store.lastStorageFailure()) ? Store.lastStorageFailure() : "The saves could not be stored.");
            // A file from before per-slot campaign state carries its extras globally: apply them, but never
            // let them overwrite a slot's own record.
            var legacyExtras = get(data, "extras") as JsonObject ?? new JsonObject();
            foreach (var key in PREFERENCE_KEYS.Concat(CAMPAIGN_KEYS))
                if (legacyExtras[key] is JsonValue v && v.TryGetValue(out string s)) Store.writeRaw(key, s);
            return count;
        }

        // Rotating checkpoints (cp1..cp3): written before level changes and dangerous encounters.
        public const int CHECKPOINTS = 3;
        public static string writeCheckpoint(JsonObject state, string label)
        {
            var saves = readAll();
            var cps = Enumerable.Range(0, CHECKPOINTS).Select(i => $"cp{i + 1}").ToArray();
            string slot = cps.FirstOrDefault(s => !Store.truthy(saves[s]));
            if (slot == null) slot = cps.Aggregate((a, b) => (time(saves[a]) <= time(saves[b]) ? a : b));
            var entry = new JsonObject { ["time"] = now() };
            setIf(entry, "level", state["currentLevel"]);
            setIf(entry, "block", state["currentBlock"]);
            entry["name"] = label; entry["thumb"] = "";
            entry["state"] = state.Parent == null ? state : state.DeepClone();
            entry["side"] = collectSide();
            saves[slot] = entry;
            return writeAll(saves) ? slot : null;
        }

        public static bool deleteSave(string slot)
        {
            var saves = readAll();
            saves.Remove(slot);
            return writeAll(saves);
        }

        // ---- port helpers: the JS object semantics the code above relies on ----

        static long now() => (long)Web.now();

        /// <summary>`obj.key`: undefined (null) on a non-object, a TypeError on null/undefined.</summary>
        static JsonNode get(JsonNode obj, string key)
        {
            if (obj == null) throw new InvalidOperationException($"Cannot read properties of null (reading '{key}')");
            return obj is JsonObject o ? o[key] : null;
        }

        static double time(JsonNode entry)
        {
            var t = get(entry, "time");
            return t == null ? double.NaN : Store.jsNumber(t);
        }

        /// <summary>`v || ""` as a string.</summary>
        static string str(JsonNode v) => !Store.truthy(v) ? "" : v is JsonValue jv && jv.TryGetValue(out string s) ? s : v.ToJsonString();

        /// <summary>`v || ""` as a JSON value.</summary>
        static JsonNode orEmpty(JsonNode v) => Store.truthy(v) ? v.DeepClone() : JsonValue.Create("");

        /// <summary>An undefined value is left out of JSON.stringify's output.</summary>
        static void setIf(JsonObject o, string key, JsonNode v) { if (v != null) o[key] = v.DeepClone(); }

        /// <summary>Object.entries order: array-index keys ascending, then the rest in insertion order.</summary>
        static List<KeyValuePair<string, JsonNode>> entries(JsonObject o)
        {
            var list = o.ToList();
            return list.Where(kv => isIndex(kv.Key)).OrderBy(kv => uint.Parse(kv.Key, CultureInfo.InvariantCulture))
                .Concat(list.Where(kv => !isIndex(kv.Key))).ToList();
        }

        static bool isIndex(string k) =>
            k.Length > 0 && k.Length <= 10 && k.All(c => c >= '0' && c <= '9') && (k == "0" || k[0] != '0')
            && ulong.Parse(k, CultureInfo.InvariantCulture) < uint.MaxValue;

        /// <summary>The object's members moved into a new object in JS key order (what JSON.stringify writes).</summary>
        static JsonObject jsOrder(JsonObject o)
        {
            var list = entries(o);
            o.Clear();
            var r = new JsonObject();
            foreach (var kv in list) r[kv.Key] = kv.Value;
            return r;
        }
    }
}
