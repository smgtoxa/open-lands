// src/game/mods.mjs (ModsMixin): level mods made with the level designer (editor.html): per-level
// overrides applied on top of the original data when a level loads. A mod is { walls: { "<block>":
// [n, e, s, w] }, items: [{ block, prop }], monsters: [{ block, type, facing }], start: { block, dir } };
// `engine.levelMods[level]`.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>mods.mjs mod.items[]: { block, prop }</summary>
    public sealed class ModItem
    {
        public int block, prop;
    }

    /// <summary>mods.mjs mod.monsters[]: { block, type, facing }</summary>
    public sealed class ModMonster
    {
        public int block, type, facing;
    }

    /// <summary>mods.mjs mod.start: { block, dir }</summary>
    public sealed class ModStart
    {
        public int block, dir;
    }

    /// <summary>mods.mjs levelMods[level] (any key may be null)</summary>
    public sealed class LevelMod
    {
        public Dictionary<string, int[]> walls;
        public ModItem[] items;
        public ModMonster[] monsters;
        public ModStart start;
        /// <summary>original objects the designer removed: their blocks</summary>
        public int[] remove;
    }

    /// <summary>engine.randomizer = { seed, ngplus } (set by the host)</summary>
    public sealed class RandomizerConfig
    {
        public string seed;
        public int ngplus;
    }

    public sealed partial class LandsOfLore
    {
        public Dictionary<int, LevelMod> levelMods;
        public RandomizerConfig randomizer;

        public void initMods()
        {
            levelMods = new Dictionary<int, LevelMod>();
        }

        public LevelMod levelMod() => levelMod(currentLevel);

        public LevelMod levelMod(int level)
        {
            return levelMods != null && levelMods.TryGetValue(level, out var mod) && mod != null ? mod : null;
        }

        // Wall overrides: right after the CMZ map is read (scripts may look at walls afterwards).
        public void applyModWalls()
        {
            var mod = levelMod();
            if (mod == null || mod.walls == null) return;
            foreach (var entry in mod.walls)
            {
                // Number(block): anything else is NaN and finds no block
                if (!int.TryParse(entry.Key, out int block) || block < 0 || block >= levelBlockProperties.Length) continue;
                var l = levelBlockProperties[block];
                var walls = entry.Value;
                for (int i = 0; i < 4; i += 1) l.walls[i] = (byte)((i < walls.Length ? walls[i] : 0) & 0xff);
                l.flags &= 0xdf;
                if (wllAutomapData[l.walls[0]] == 17) l.flags |= 0x20;
            }
        }

        // Extra items and monsters: once the level's own scripts have placed theirs.
        public async Task applyModObjects()
        {
            var mod = levelMod();
            // Revisits restore the level from its temp data, where the mod objects already live.
            if (mod == null || (hasTempDataFlags & (1 << (currentLevel - 1))) != 0) return;
            // Original objects the designer removed from these blocks.
            foreach (int block in mod.remove ?? new int[0])
            {
                if (block < 0 || block >= levelBlockProperties.Length) continue;
                var l = levelBlockProperties[block];
                var list = new List<int>();
                int o = l.assignedObjects; int guard = 0;
                while (o != 0 && guard++ < 64) { list.Add(o); o = findObject(o).nextAssignedObject; }
                foreach (int id in list)
                {
                    if ((id & 0x8000) != 0) { var m = monsters[id & 0x7fff]; m.hitPoints = 0; setMonsterMode(m, 14); removeAssignedObjectFromBlock(l, id); removeDrawObjectFromBlock(l, id); }
                    else { await removeLevelItem(id, block); deleteItem(id); }
                }
            }
            foreach (var it in mod.items ?? new ModItem[0])
            {
                if (it.prop < 0 || it.prop >= itemProperties.Count || itemProperties[it.prop] == null) continue;
                int item = makeItem(it.prop, 0, 0);
                if (item == 0) continue;
                var (x, y) = calcCoordinates(it.block, 0x80, 0x80);
                await setItemPosition(item, x, y, 0, 1);
            }
            foreach (var m in mod.monsters ?? new ModMonster[0])
            {
                var mp = monsterProperties.ElementAtOrDefault(m.type);
                if (mp == null || mp.hitPoints == 0) continue;
                initMonster(new[] { m.block, 0x80, 0x80, m.facing, m.type, 0, 0, 0, 0, 0, 0 });
            }
        }

        // ---- Randomizer / New game+ (see docs/RANDOMIZER.md) ----
        // engine.randomizer = { seed, ngplus }: on the first visit of a level, items lying on open floor are
        // shuffled among reachable floor blocks and monsters swap types among the level's own kinds; New
        // game+ scales monster hit points and damage. Quest items and anything in niches/chests stay put.
        static readonly Regex QUEST_ITEM = new Regex(@"bezel|ruby|shard|writ|atlas|key\b|mask|cube|orb|figurine|emblem|medallion|crystal|scroll of|elixir|potion of", RegexOptions.IgnoreCase);

        static Func<double> Mods_rng(string seedText)
        {
            uint h = 2166136261;
            foreach (char ch in seedText) { h ^= ch; h = unchecked(h * 16777619u); }
            return () =>
            {
                unchecked
                {
                    h = h + 0x6d2b79f5u; uint t = h;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        public async Task applyRandomizer()
        {
            var cfg = randomizer;
            if (cfg == null || (hasTempDataFlags & (1 << (currentLevel - 1))) != 0) return;
            var random = Mods_rng($"{cfg.seed}:{currentLevel}");
            bool open(int b) => levelBlockProperties[b].walls.All(w => w == 0);
            // reachable open floor from the party (BFS through passable walls)
            var reach = new HashSet<int> { currentBlock };
            var reachOrder = new List<int> { currentBlock }; // a JS Set iterates in insertion order
            var queue = new Queue<int>(); queue.Enqueue(currentBlock);
            while (queue.Count > 0)
            {
                int b = queue.Dequeue();
                for (int d = 0; d < 4; d += 1)
                {
                    int n = calcNewBlockPosition(b, d);
                    if (reach.Contains(n) || !open(n) || testWallFlag(n, d, 1)) continue;
                    reach.Add(n); reachOrder.Add(n); queue.Enqueue(n);
                }
            }
            var floor = reachOrder.Where(b => b != currentBlock).ToList();
            if (floor.Count < 4) return;
            // shuffle floor items (only ones lying on open floor, not quest items)
            var movable = new List<(int item, int block)>();
            foreach (int b in floor)
            {
                int o = levelBlockProperties[b].assignedObjects; int guard = 0;
                while (o != 0 && guard++ < 64)
                {
                    int next = findObject(o).nextAssignedObject;
                    if ((o & 0x8000) == 0)
                    {
                        string name = itemName(o) ?? "";
                        if (!QUEST_ITEM.IsMatch(name)) movable.Add((o, b));
                    }
                    o = next;
                }
            }
            foreach (var m in movable) await removeLevelItem(m.item, m.block);
            foreach (var m in movable)
            {
                int target = floor[Js.Floor(random() * floor.Count)];
                var (x, y) = calcCoordinates(target, 0x80, 0x80);
                await setItemPosition(m.item, x, y, 0, 1);
            }
            // monsters: swap types among the level's kinds, scale for New game+
            var kinds = monsterProperties.Select((p, i) => p.hitPoints != 0 ? i : -1).Where(i => i >= 0).ToList();
            double scaleHp = 1 + 0.5 * cfg.ngplus;
            foreach (var mon in monsters)
            {
                if (mon.properties == null || mon.hitPoints <= 0 || mon.mode >= 13) continue;
                if (kinds.Count > 1 && random() < 0.7)
                {
                    int type = kinds[Js.Floor(random() * kinds.Count)];
                    if (type != mon.type)
                    {
                        mon.type = type;
                        mon.properties = monsterProperties[type];
                        mon.hitPoints = (mon.properties.hitPoints * @static.MonsterModifiers1[monsterDifficulty]) >> 8;
                        for (int i = 0; i < 4; i += 1) mon.equipmentShapes[i] = 0;
                    }
                }
                mon.hitPoints = Js.Round(mon.hitPoints * scaleHp);
                mon.ngplus = cfg.ngplus;
                checkSceneUpdateNeed(mon.block);
            }
            sceneUpdateRequired = true;
        }
    }
}
