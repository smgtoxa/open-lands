// Level mods and the randomizer: what the port adds on top of the original data when a level loads.
//
// Transliterated from src/game/mods.mjs.
//
// A mod is what the level designer saved - walls replaced, objects removed, items and monsters
// added - and is applied in two places, because the level's own scripts run in between: the walls
// right after the block map is read, and the objects once the scripts have placed theirs.
//
// The randomizer is the other half: on a first visit it shuffles the loose items among the floor
// blocks the party can actually reach and swaps monster types among the kinds the level already
// uses, on its own seeded stream so a seed always produces the same level.
namespace LolCore;

/// <summary>One level's overrides, as the designer saved them.</summary>
public sealed class LevelMod
{
    public Dictionary<int, int[]> Walls = new();
    public List<int> Remove = new();
    public List<(int Block, int Prop)> Items = new();
    public List<(int Block, int Type, int Facing)> Monsters = new();
}

/// <summary>A seed and a New game+ level.</summary>
public sealed class RandomizerConfig
{
    public string Seed = "";
    public int NgPlus;
}

public sealed partial class LevelLoader
{
    /// <summary>The mods to apply, by level number.</summary>
    public readonly Dictionary<int, LevelMod> LevelMods = new();

    /// <summary>The randomizer, when one is on.</summary>
    public RandomizerConfig Randomizer;

    private LevelMod LevelModFor(int level) => LevelMods.TryGetValue(level, out var mod) ? mod : null;

    /// <summary>applyModWalls: the designer's walls, right after the block map is read.</summary>
    public void ApplyModWalls()
    {
        var mod = LevelModFor(Level);
        if (mod == null) return;
        foreach (var (block, walls) in mod.Walls)
        {
            if (block < 0 || block > 1023) continue;
            for (int i = 0; i < 4 && i < walls.Length; i += 1) Map.Walls[block, i] = (byte)(walls[i] & 0xff);
            Map.Flags[block] &= 0xdf;
            if (Walls.Automap[Map.Walls[block, 0]] == 17) Map.Flags[block] |= 0x20;
        }
    }

    /// <summary>applyModObjects: what the designer added, once the level's scripts have run.</summary>
    public void ApplyModObjects()
    {
        var mod = LevelModFor(Level);
        // A revisit restores the level from its own saved state, where these already live.
        if (mod == null || (HasTempDataFlags & (1 << (Level - 1))) != 0) return;

        foreach (int block in mod.Remove)
        {
            if (block < 0 || block > 1023) continue;
            var ids = new List<int>();
            int o = Map.AssignedObjects[block];
            int guard = 0;
            while (o != 0 && guard++ < 64)
            {
                ids.Add(o);
                o = (o & 0x8000) != 0 ? Board.Monsters[o & 0x7fff].NextAssignedObject : Items.InPlay[o].NextAssignedObject;
            }
            foreach (int id in ids)
            {
                if ((id & 0x8000) != 0)
                {
                    var m = Board.Monsters[id & 0x7fff];
                    m.HitPoints = 0;
                    Board.SetMonsterMode(m, 14);
                    Board.Place(m, 0, 0);
                }
                else
                {
                    Board.RemoveLevelItem(id, block);
                    Items.Delete(id);
                }
            }
        }

        foreach (var (block, prop) in mod.Items)
        {
            if (prop < 0 || prop >= Items.Properties.Length) continue;
            int item = Items.Make(prop, 0, 0, Level);
            if (item <= 0) continue;
            var (x, y) = Party.CalcCoordinates(block, 0x80, 0x80);
            Board.SetItemPosition(item, x, y, 0, true);
        }

        foreach (var (block, type, facing) in mod.Monsters)
        {
            if (type < 0 || type >= Board.Properties.Length || Board.Properties[type] == null || Board.Properties[type].HitPoints == 0) continue;
            Board.Init(new[] { block, 0x80, 0x80, facing, type, 0, 0, 0, 0, 0, 0 }, Level);
        }
    }

    /// <summary>
    /// The randomizer's own stream: a hash of the seed and the level number, so every level of a
    /// seed is its own sequence and a seed always produces the same game.
    /// </summary>
    private static Func<double> SeedStream(string seedText)
    {
        uint h = 2166136261;
        foreach (char ch in seedText) { h ^= ch; h = unchecked(h * 16777619); }
        return () =>
        {
            h = unchecked(h + 0x6d2b79f5);
            uint t = h;
            t = unchecked((uint)((t ^ (t >> 15)) * (t | 1)));
            t ^= unchecked(t + (uint)((t ^ (t >> 7)) * (t | 61)));
            return ((t ^ (t >> 14)) & 0xffffffff) / 4294967296.0;
        };
    }

    /// <summary>Items whose place in the world is the story's, not the randomizer's.</summary>
    private static readonly string[] QuestItemWords =
    {
        "bezel", "ruby", "shard", "writ", "atlas", "key", "mask", "cube", "orb", "figurine",
        "emblem", "medallion", "crystal", "scroll of", "elixir", "potion of",
    };

    private bool IsQuestItem(int item)
    {
        if (Gui == null) return false;
        int prop = Items.InPlay[item].ItemPropertyIndex;
        if (prop < 0 || prop >= Items.Properties.Length) return false;
        string name = (GameStrings.Get(Items.Properties[prop].NameStringId, Gui.LandsFile, Gui.LevelLangFile) ?? "").ToLowerInvariant();
        if (name.Length == 0) return false;
        foreach (string word in QuestItemWords)
            if (word == "key" ? System.Text.RegularExpressions.Regex.IsMatch(name, @"key\b") : name.Contains(word)) return true;
        return false;
    }

    /// <summary>applyRandomizer: the loose items shuffled, the monsters swapped, on a first visit.</summary>
    public void ApplyRandomizer()
    {
        var cfg = Randomizer;
        if (cfg == null || (HasTempDataFlags & (1 << (Level - 1))) != 0) return;
        var random = SeedStream($"{cfg.Seed}:{Level}");

        bool Open(int b)
        {
            for (int i = 0; i < 4; i += 1) if (Map.Walls[b, i] != 0) return false;
            return true;
        }

        // The floor the party can actually walk to, so nothing lands behind a wall.
        var reach = new HashSet<int> { Party.Block };
        var queue = new Queue<int>();
        queue.Enqueue(Party.Block);
        while (queue.Count > 0)
        {
            int b = queue.Dequeue();
            for (int d = 0; d < 4; d += 1)
            {
                int n = Party.CalcNewBlockPosition(b, d);
                if (reach.Contains(n) || !Open(n) || Party.TestWallFlag(n, d, 1)) continue;
                reach.Add(n);
                queue.Enqueue(n);
            }
        }
        var floor = reach.Where(b => b != Party.Block).ToList();
        if (floor.Count < 4) return;

        var movable = new List<(int Item, int Block)>();
        foreach (int b in floor)
        {
            int o = Map.AssignedObjects[b];
            int guard = 0;
            while (o != 0 && guard++ < 64)
            {
                int next = (o & 0x8000) != 0 ? Board.Monsters[o & 0x7fff].NextAssignedObject : Items.InPlay[o].NextAssignedObject;
                if ((o & 0x8000) == 0 && !IsQuestItem(o)) movable.Add((o, b));
                o = next;
            }
        }
        foreach (var m in movable) Board.RemoveLevelItem(m.Item, m.Block);
        foreach (var m in movable)
        {
            int target = floor[(int)(random() * floor.Count)];
            var (x, y) = Party.CalcCoordinates(target, 0x80, 0x80);
            Board.SetItemPosition(m.Item, x, y, 0, true);
        }

        var kinds = Enumerable.Range(0, Board.Properties.Length)
            .Where(i => Board.Properties[i] != null && Board.Properties[i].HitPoints != 0).ToList();
        double scaleHp = 1 + 0.5 * cfg.NgPlus;
        var modifiers1 = StaticData.Table("MonsterModifiers1");
        foreach (var mon in Board.Monsters)
        {
            if (mon.Properties == null || mon.HitPoints <= 0 || mon.Mode >= 13) continue;
            if (kinds.Count > 1 && random() < 0.7)
            {
                int type = kinds[(int)(random() * kinds.Count)];
                if (type != mon.Type)
                {
                    mon.Type = type;
                    mon.Properties = Board.Properties[type];
                    mon.HitPoints = (mon.Properties.HitPoints * modifiers1[Board.Difficulty]) >> 8;
                    for (int i = 0; i < 4; i += 1) mon.EquipmentShapes[i] = 0;
                }
            }
            mon.HitPoints = JsMath.RoundToInt(mon.HitPoints * scaleHp);
            mon.NgPlus = cfg.NgPlus;
        }
    }
}
