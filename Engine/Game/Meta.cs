// src/game/meta.mjs
// What the game remembers about the player rather than about the world: how much they have killed,
// what they have met, what they have seen and what they have earned.
//
// This is a port addition and it used to live entirely in the browser host, which meant a second
// build could not have it without reimplementing every rule. The rules are here now; the host keeps
// what it is good at - drawing the panels and writing the state to storage - and this keeps the
// counters, the bestiary, the item database and the achievement tests, so that any build running
// this engine earns the same achievements from the same play.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lol
{
    /// <summary>meta.mjs metaBestiaryMeet: meta.bestiary[name]</summary>
    public sealed class BestiaryEntry
    {
        public int kills, hpMax, level;
        public List<int> levels;
        // copied from monsterInfo (host-ui.mjs)
        public int might, hitChance, evade, protection;
        public bool ranged, poison, steals;
        public List<string> traits;
        public string danger;
        public List<string> weak, resist, heals;
        public List<DamageTakenEntry> damageTaken;
    }

    /// <summary>meta.mjs metaRememberVisit: meta.visited[level] (the host also writes dir/exits/danger)</summary>
    public sealed class VisitedEntry
    {
        public int block, direction;
        public int? dir;
        public List<int> exits;
        public int danger;
    }

    /// <summary>meta.mjs metaScanItems: meta.itemDb[prop]</summary>
    public sealed class ItemDbEntry
    {
        public string name;
        public int might, protection;
        public List<string> slots;
        public bool usable;
        public int level, block;
    }

    /// <summary>meta.mjs metaNoteAdd: meta.notes[level][]</summary>
    public sealed class MapNote
    {
        public int block;
        public string text;
    }

    /// <summary>meta.mjs initMeta: this.meta</summary>
    public sealed class MetaState
    {
        public Dictionary<string, double> stats;
        public Dictionary<string, BestiaryEntry> bestiary;   // monster name -> what has been learned about it
        public Dictionary<int, VisitedEntry> visited;        // level -> where the party last stood on it
        public Dictionary<int, ItemDbEntry> itemDb;          // item property index -> what the item is
        public Dictionary<int, List<MapNote>> notes;         // level -> [{ block, text }]
        /// <summary>conversation file -> how many times it was started (an int here; npcs.mjs keeps its
        /// { name, talks, level, block } records in the same table, so the values are either)</summary>
        public Dictionary<string, object> npcs;
        public List<string> unlocked;                        // achievement names, in the order they were earned
    }

    /// <summary>meta.mjs metaView: what the achievement tests read.</summary>
    public sealed class MetaView
    {
        public Dictionary<string, double> stats;
        public Func<int> knownSpells, visitedCount, noteCount, npcCount, kindsKilled, itemKinds, credits, heroCount, bestSkill, questsDone;
        public Func<int, int> unlockedCount;
    }

    /// <summary>meta.mjs metaAchievements entries</summary>
    public sealed class AchievementState
    {
        public string name, description, icon;
        public bool unlocked;
    }

    public sealed partial class LandsOfLore
    {
        static readonly string[] STAT_KEYS =
        {
            "kills", "damageDealt", "damageTaken", "seconds", "maxKind", "maxHit", "closeCalls", "deaths",
            "spells", "spellHeal", "spellFire", "spellBolt", "spellIce", "power4", "steps", "loot", "trades",
            "rests", "saves", "photos", "pitRuns", "finished",
        };

        // [name, description, test, icon]. The test reads only the meta state and the engine, so a build
        // with neither a DOM nor a localStorage earns exactly what the browser build earns.
        public static readonly (string name, string description, Func<MetaView, bool> test, string icon)[] ACHIEVEMENTS =
        {
            ("First blood", "Slay a monster", (m) => m.stats["kills"] >= 1, "skull"),
            ("Slayer", "Slay 250 monsters", (m) => m.stats["kills"] >= 250, "sword"),
            ("Exterminator", "Slay 1,000 monsters", (m) => m.stats["kills"] >= 1000, "axe"),
            ("Nemesis", "Slay 50 of one kind", (m) => m.stats["maxKind"] >= 50, "target"),
            ("Juggernaut", "Deal 25,000 damage", (m) => m.stats["damageDealt"] >= 25000, "hammer"),
            ("Overkill", "Deal 150 damage in one hit", (m) => m.stats["maxHit"] >= 150, "burst"),
            ("Iron skin", "Take 10,000 damage", (m) => m.stats["damageTaken"] >= 10000, "shield"),
            ("Nine lives", "Survive 9 close calls (5 hit points or less)", (m) => m.stats["closeCalls"] >= 9, "heart"),
            ("Phoenix", "Come back from 10 defeats", (m) => m.stats["deaths"] >= 10, "flame"),
            ("Apprentice", "Cast a spell", (m) => m.stats["spells"] >= 1, "wand"),
            ("Archmage", "Cast 500 spells", (m) => m.stats["spells"] >= 500, "hat"),
            ("Healer", "Cast Heal 25 times", (m) => m.stats["spellHeal"] >= 25, "cross"),
            ("Pyromancer", "Cast Fireball 50 times", (m) => m.stats["spellFire"] >= 50, "sun"),
            ("Storm caller", "Cast Spark or Lightning 50 times", (m) => m.stats["spellBolt"] >= 50, "bolt"),
            ("Frostbite", "Cast Freeze 25 times", (m) => m.stats["spellIce"] >= 25, "snow"),
            ("Full power", "Cast a spell at power 4", (m) => m.stats["power4"] >= 1, "star"),
            ("Grimoire", "Know 8 spells", (m) => m.knownSpells() >= 8, "book"),
            ("Explorer", "Visit 5 levels", (m) => m.visitedCount() >= 5, "compass"),
            ("Cartographer", "Visit 25 levels", (m) => m.visitedCount() >= 25, "map"),
            ("Marathon", "Walk 25,000 steps", (m) => m.stats["steps"] >= 25000, "boot"),
            ("Scribe", "Leave 25 map notes", (m) => m.noteCount() >= 25, "note"),
            ("Diplomat", "Talk to 20 people", (m) => m.npcCount() >= 20, "talk"),
            ("Zoologist", "Slay 40 kinds of monster", (m) => m.kindsKilled() >= 40, "eye"),
            ("Hoarder", "See 75 kinds of item", (m) => m.itemKinds() >= 75, "bag"),
            ("Magpie", "Pick up 500 items from the floor", (m) => m.stats["loot"] >= 500, "hand"),
            ("Wealthy", "Hold 2,500 crowns", (m) => m.credits() >= 2500, "coin"),
            ("Merchant", "Trade 10 times", (m) => m.stats["trades"] >= 10, "chest"),
            // Three is the whole party: addCharacter refuses a fourth, exactly as the original game does.
            ("Full house", "Have three heroes in the party", (m) => m.heroCount() >= 3, "crown"),
            ("Master", "A hero reaches level 8 in any skill", (m) => m.bestSkill() >= 8, "gem"),
            ("Sleepyhead", "Rest 50 times", (m) => m.stats["rests"] >= 50, "bed"),
            ("Dedicated", "Play for 25 hours", (m) => m.stats["seconds"] >= 90000, "hourglass"),
            ("Cautious", "Save 50 times", (m) => m.stats["saves"] >= 50, "save"),
            ("Shutterbug", "Take 10 photos", (m) => m.stats["photos"] >= 10, "camera"),
            ("Storyteller", "Complete 15 objectives", (m) => m.questsDone() >= 15, "scroll"),
            ("Throne of Chaos", "Finish the game", (m) => m.stats["finished"] >= 1, "trophy"),
            // The last one counts the others, so it never counts itself.
            ("Achievement hunter", "Unlock 20 achievements", (m) => m.unlockedCount(ACHIEVEMENTS.Length - 1) >= 20, "flag"),
        };

        // ---- host storage (the browser's localStorage) ----
        public Func<string, string> storageGet;
        public Action<string, string> storageSet;

        // ---- MetaMixin ----
        public MetaState meta;

        public MetaState initMeta()
        {
            meta = new MetaState
            {
                stats = STAT_KEYS.ToDictionary(k => k, k => 0.0),
                bestiary = new Dictionary<string, BestiaryEntry>(),   // monster name -> what has been learned about it
                visited = new Dictionary<int, VisitedEntry>(),        // level -> where the party last stood on it
                itemDb = new Dictionary<int, ItemDbEntry>(),          // item property index -> what the item is
                notes = new Dictionary<int, List<MapNote>>(),         // level -> [{ block, text }]
                npcs = new Dictionary<string, object>(),              // conversation file -> how many times it was started
                unlocked = new List<string>(),                        // achievement names, in the order they were earned
            };
            return meta;
        }

        // The host keeps the state between sessions; this takes back what it stored, keys and all.
        public MetaState metaLoad(JsonObject saved)
        {
            if (saved == null) return meta;
            var m = meta;
            foreach (var key in STAT_KEYS)
                if ((saved["stats"] as JsonObject)?[key] is JsonValue v && v.GetValueKind() == JsonValueKind.Number) m.stats[key] = v.GetValue<double>();
            // for (const field of ["bestiary", "visited", "itemDb", "notes", "npcs"]): a shallow copy of each object
            if (saved["bestiary"] is JsonObject bestiary) m.bestiary = bestiary.Deserialize<Dictionary<string, BestiaryEntry>>(Store_json);
            if (saved["visited"] is JsonObject visited) m.visited = visited.Deserialize<Dictionary<int, VisitedEntry>>(Store_json);
            if (saved["itemDb"] is JsonObject itemDb) m.itemDb = itemDb.Deserialize<Dictionary<int, ItemDbEntry>>(Store_json);
            if (saved["notes"] is JsonObject notes) m.notes = notes.Deserialize<Dictionary<int, List<MapNote>>>(Store_json);
            if (saved["npcs"] is JsonObject npcs)
            {
                // a count (metaNpcMet) stays a number; an object is an npcs.mjs NpcEntry
                m.npcs = new Dictionary<string, object>();
                foreach (var e in npcs)
                    m.npcs[e.Key] = e.Value is JsonValue nv && nv.GetValueKind() == JsonValueKind.Number ? nv.GetValue<int>()
                        : e.Value is JsonObject no ? no.Deserialize<NpcEntry>(Store_json) : (object)e.Value?.DeepClone();
            }
            if (saved["unlocked"] is JsonArray unlocked) m.unlocked = unlocked.Select(u => (string)u).ToList();
            return m;
        }

        public JsonNode metaSave()
        {
            return JsonSerializer.SerializeToNode(meta, Store_json);
        }

        public List<string> metaBump(string key, double n = 1)
        {
            meta.stats[key] = (meta.stats.TryGetValue(key, out double v) ? v : 0) + n;
            return metaCheckAchievements();
        }

        public List<string> metaPeak(string key, double value)
        {
            if (value <= (meta.stats.TryGetValue(key, out double v) ? v : 0)) return new List<string>();
            meta.stats[key] = value;
            return metaCheckAchievements();
        }

        // What the party has learned about a kind of monster by meeting it.
        public BestiaryEntry metaBestiaryMeet(Monster monster)
        {
            var info = monsterInfo(monster);
            string name = info != null ? info.name : "Monster";
            var m = meta.bestiary;
            if (!m.TryGetValue(name, out var entry)) entry = m[name] = new BestiaryEntry { kills = 0, hpMax = 0, level = currentLevel, levels = new List<int>() };
            entry.levels = entry.levels ?? new List<int>();
            if (!entry.levels.Contains(currentLevel)) entry.levels.Add(currentLevel);
            if (info != null)
            {
                entry.hpMax = Math.Max(entry.hpMax, info.hpMax);
                // ["might", "hitChance", "evade", "protection", "ranged", "poison", "steals", "traits", "danger", "weak", "resist", "heals", "damageTaken"]:
                // monsterInfo defines every one of them
                entry.might = info.might;
                entry.hitChance = info.hitChance;
                entry.evade = info.evade;
                entry.protection = info.protection;
                entry.ranged = info.ranged;
                entry.poison = info.poison;
                entry.steals = info.steals;
                entry.traits = info.traits;
                entry.danger = info.danger;
                entry.weak = info.weak;
                entry.resist = info.resist;
                entry.heals = info.heals;
                entry.damageTaken = info.damageTaken;
            }
            return entry;
        }

        public List<string> metaKilled(Monster monster)
        {
            var entry = metaBestiaryMeet(monster);
            entry.kills += 1;
            meta.stats["kills"] += 1;
            metaPeak("maxKind", entry.kills);
            return metaCheckAchievements();
        }

        // Everything the party is carrying, wearing, holding or standing on, recorded once per kind.
        public int metaScanItems()
        {
            var seen = new List<int>();
            foreach (int i in inventory) if (i != 0) seen.Add(i);
            foreach (var c in characters) if ((c.flags & 1) != 0) foreach (int i in c.items) if (i != 0) seen.Add(i);
            if (itemInHand != 0) seen.Add(itemInHand);
            /* if (this.uiFloorItems): always there */
            foreach (var f in uiFloorItems()) seen.Add(f.item);
            int added = 0;
            foreach (int item in seen)
            {
                int prop = itemsInPlay[item].itemPropertyIndex;
                if (meta.itemDb.TryGetValue(prop, out var known) && known != null) continue;
                var info = itemInfo(item);
                if (info == null || string.IsNullOrEmpty(info.name)) continue;
                meta.itemDb[prop] = new ItemDbEntry
                {
                    name = info.name, might = info.might, protection = info.protection, slots = info.slots,
                    usable = info.usable, level = currentLevel, block = currentBlock,
                };
                added += 1;
            }
            if (added != 0) metaCheckAchievements();
            return added;
        }

        // Where the party last stood on a level, which is what fast travel offers.
        public bool metaRememberVisit()
        {
            if (currentBlock == 0) return false;
            if (uiInDungeon()) return false;   // a pit floor is not a place to return to
            meta.visited[currentLevel] = new VisitedEntry { block = currentBlock, direction = currentDirection };
            metaCheckAchievements();
            return true;
        }

        public List<MapNote> metaNoteAdd(int level, int block, string text)
        {
            if (!meta.notes.TryGetValue(level, out var list)) list = meta.notes[level] = new List<MapNote>();
            list.Add(new MapNote { block = block, text = text });
            metaCheckAchievements();
            return list;
        }

        public int metaNpcMet(string file)
        {
            // (npcs[file] || 0) + 1; an npcs.mjs record under the same key would concatenate to a string in JS
            meta.npcs[file] = (meta.npcs.TryGetValue(file, out var v) && v is int n ? n : 0) + 1;
            metaCheckAchievements();
            return (int)meta.npcs[file];
        }

        // ---- what the achievement tests read ----
        public MetaView metaView()
        {
            var engine = this;
            return new MetaView
            {
                stats = meta.stats,
                knownSpells = () => engine.availableSpells.Count(v => v != -1),
                visitedCount = () => engine.meta.visited.Count,
                noteCount = () => engine.meta.notes.Values.Aggregate(0, (n, list) => n + list.Count),
                npcCount = () => engine.meta.npcs.Count,
                kindsKilled = () => engine.meta.bestiary.Values.Count(b => b.kills > 0),
                itemKinds = () => engine.meta.itemDb.Count,
                credits = () => engine.credits | 0,
                heroCount = () => engine.characters.Count(c => (c.flags & 1) != 0),
                bestSkill = () => Math.Max(0, engine.characters.Where(c => (c.flags & 1) != 0).SelectMany(c => c.skillLevels.Select(s => (int)s)).DefaultIfEmpty(0).Max()),
                // questDone is a Set (quests.mjs, and the save round-trips it as one): Object.keys of a Set is
                // always empty, which made this read zero however many objectives were finished.
                questsDone = () => (engine.questDone != null ? engine.questDone.Count : 0),
                unlockedCount = (limit) =>
                {
                    var view = engine.metaView();
                    int n = 0;
                    for (int i = 0; i < limit && i < ACHIEVEMENTS.Length; i += 1)
                    {
                        try { if (ACHIEVEMENTS[i].test(view)) n += 1; } catch (Exception) { /* a test that cannot run has not been earned */ }
                    }
                    return n;
                },
            };
        }

        // Which achievements are earned now. Returns the ones earned since the last check, so the host
        // can say so; the full list is kept in meta.unlocked, in the order they were earned.
        public List<string> metaCheckAchievements()
        {
            var view = metaView();
            var fresh = new List<string>();
            foreach (var (name, _, test, _) in ACHIEVEMENTS)
            {
                if (meta.unlocked.Contains(name)) continue;
                bool earned = false;
                try { earned = test(view); } catch (Exception) { earned = false; }
                if (!earned) continue;
                meta.unlocked.Add(name);
                fresh.Add(name);
            }
            return fresh;
        }

        // The whole list, for the panel that shows it.
        public List<AchievementState> metaAchievements()
        {
            var view = metaView();
            return ACHIEVEMENTS.Select(a =>
            {
                bool earned = meta.unlocked.Contains(a.name);
                if (!earned) { try { earned = a.test(view); } catch (Exception) { earned = false; } }
                return new AchievementState { name = a.name, description = a.description, icon = a.icon, unlocked = earned };
            }).ToList();
        }
    }
}
