// src/platform/dungeon-run.mjs. C# 9.
// The Imp's Pit, as the browser stores it: how deep the party have been, and how each run went.
//
// The rules - which level a floor borrows, what it asks for, what it pays - live in the engine
// (src/game/dungeon.mjs) so that every build reads a floor the same way. What is here is the
// record's home in localStorage and the calls the page's panels were written against.
//
// Note: this class is LolHost.DungeonRun; the engine's record type is Lol.DungeonRun (write it in full).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Lol;

namespace LolHost
{
    /// <summary>main.mjs dungeonState(): engine.uiDungeonInfo() plus what the page adds to it (sigils and
    /// where the floor's things are). OBJECTIVES' notes read it.</summary>
    public sealed class PitInfo
    {
        public int depth, level;
        public string objective;
        public int need, exitBlock;
        public bool ready;
        public int monstersLeft;
        public bool bossAlive;
        public int kills, sigilsBefore, levers, leversPulled;
        public bool vaultOpen;
        public bool gateOpen, gateLocked, floorDone;
        public int stairs, secrets, secretsFound;
        // added by the page
        public int sigils;
        public string vaultWhere, exitWhere, leverWhere;

        public PitInfo(DungeonInfo d)
        {
            depth = d.depth; level = d.level; objective = d.objective; need = d.need; exitBlock = d.exitBlock; ready = d.ready;
            monstersLeft = d.monstersLeft; bossAlive = d.bossAlive; kills = d.kills; sigilsBefore = d.sigilsBefore;
            levers = d.levers; leversPulled = d.leversPulled; vaultOpen = d.vaultOpen;
            gateOpen = d.gateOpen; gateLocked = d.gateLocked; floorDone = d.floorDone; stairs = d.stairs; secrets = d.secrets; secretsFound = d.secretsFound;
        }
    }

    /// <summary>An OBJECTIVES entry: the engine's { id, label } plus the line under the map.</summary>
    public sealed class Objective
    {
        public string id, label;
        public Func<PitInfo, int, string> note;
    }

    public static class DungeonRun
    {
        // export { DUNGEON_ITEMS, objectiveNeed, floorPlan, floorSizeName, monsterCount }
        public static ExtraItemDef[] DUNGEON_ITEMS => ErrandItems.DUNGEON_ITEMS;
        public static int objectiveNeed(string objective, int depth) => LandsOfLore.objectiveNeed(objective, depth);
        public static FloorPlan floorPlan(double floor, object visited = null, int currentLevel = 0) => LandsOfLore.floorPlan(floor, visited, currentLevel);
        public static string floorSizeName(int depth) => LandsOfLore.floorSizeName(depth);
        public static int monsterCount(int depth) => LandsOfLore.monsterCount(depth);

        // the line under the objectives: what is left to do, then (the gate open) which way the stairs lie
        static string Way(PitInfo i, string pending) =>
            i.floorDone ? $"beaten · the stairs lie {i.exitWhere}" : i.gateOpen ? $"the gate is open · the way down lies {i.exitWhere}" : pending;
        static string Hidden(PitInfo i) => i.secrets > 0 ? $" · {i.secretsFound}/{i.secrets} hidden passages found" : "";

        static readonly Dictionary<string, Func<PitInfo, int, string>> NOTES = new Dictionary<string, Func<PitInfo, int, string>>
        {
            ["levers"] = (i, need) => Way(i, $"{i.leversPulled}/{i.levers} levers pulled") + Hidden(i),
            ["switch"] = (i, need) => Way(i, "a loose stone works the gate") + Hidden(i),
            ["key"] = (i, need) => Way(i, "the gate is locked: find its key") + Hidden(i),
            ["sigils"] = (i, need) => Way(i, $"{Math.Max(0, i.sigils - i.sigilsBefore)}/{need} sigils") + Hidden(i),
            ["clear"] = (i, need) => i.gateOpen || i.floorDone ? Way(i, "") : $"{i.monstersLeft} left",
            ["boss"] = (i, need) => i.floorDone ? Way(i, "") : i.bossAlive ? "the master still walks" + Hidden(i) : $"the master is dead · the way down lies {i.exitWhere}",
            ["exit"] = (i, need) => (!string.IsNullOrEmpty(i.exitWhere) ? $"it lies {i.exitWhere}" : "somewhere in the dead ends"),
            ["sigil"] = (i, need) => $"{Math.Max(0, i.sigils - i.sigilsBefore)}/{need}",
            ["shards"] = (i, need) => $"{Math.Max(0, i.sigils - i.sigilsBefore)}/{need}",
            ["cull"] = (i, need) => $"{i.kills}/{need}",
            ["vault"] = (i, need) => (i.vaultOpen
                ? $"it stands open {i.vaultWhere} — take what is inside"
                : $"{i.leversPulled}/{i.levers} levers pulled{(!string.IsNullOrEmpty(i.leverWhere) ? $" ({i.leverWhere})" : "")}{(!string.IsNullOrEmpty(i.vaultWhere) ? $" · the vault is {i.vaultWhere}" : "")}"),
        };

        public static readonly Objective[] OBJECTIVES = LandsOfLore.OBJECTIVES
            .Select(o => new Objective { id = o.id, label = o.label, note = NOTES.TryGetValue(o.id, out var n) ? n : ((i, need) => "") }).ToArray();

        const string KEY = "lol.dungeon";

        public static Lol.DungeonRun readRun(LandsOfLore engine)
        {
            if (engine != null && engine.dungeonRun != null) return engine.dungeonRun;
            try
            {
                JsonNode raw = JsonNode.Parse(Store.storage.getItem(KEY) ?? "null");
                if (!Store.truthy(raw)) raw = new JsonObject();
                return new Lol.DungeonRun
                {
                    cleared = Math.Max(0, Store.truncOr0(Store.jsNumberOf(raw, "cleared"))),
                    runs = Math.Max(0, Store.truncOr0(Store.jsNumberOf(raw, "runs"))),
                    best = raw is JsonObject o && o["best"] is JsonObject best ? (JsonObject)best.DeepClone() : new JsonObject(),
                };
            }
            catch (Exception)
            {
                return new Lol.DungeonRun { cleared = 0, runs = 0, best = new JsonObject() };
            }
        }

        public static Lol.DungeonRun loadRun(LandsOfLore engine)
        {
            JsonNode saved = null;
            try { saved = JsonNode.Parse(Store.storage.getItem(KEY) ?? "null"); } catch (Exception) { saved = null; }
            return engine.uiDungeonRunLoad(saved as JsonObject);
        }

        public static void writeRun(Lol.DungeonRun run)
        {
            try { Store.storage.setItem(KEY, Store.stringify(run)); } catch (Exception) { /* private mode */ }
        }

        // Rewards: the engine hands them out, the browser keeps the pouch it filled.
        public static List<string> grantRewards(LandsOfLore engine, int depth, string seed = null)
        {
            seed = seed ?? $"{(long)Web.now()}";
            var got = engine.uiDungeonRewards(depth, seed, ErrandItems.ERRAND_ITEMS.Concat(ErrandItems.DUNGEON_ITEMS).ToArray());
            try { Store.storage.setItem("lol.craft", Store.stringify(engine.uiCraftPouch())); } catch (Exception) { /* private mode */ }
            return got;
        }

        public static int giveItem(LandsOfLore engine, int? prop)
        {
            return engine.uiDungeonGiveItem(prop);
        }
    }
}
