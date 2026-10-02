// The Imp's Pit as a run: which floor comes next, what it asks for, and what it pays.
//
// Transliterated from the run half of src/game/dungeon.mjs. A floor is decided from its number
// alone - which level it borrows, what it asks and what it is worth - so every build reads the
// same pit. How deep the party have been is kept by the host.
namespace LolCore;

public sealed class DungeonRunRecord
{
    public int Cleared, Runs;
    /// <summary>The best a floor has gone, by floor number, as the host chooses to describe it.</summary>
    public readonly Dictionary<int, string> Best = new();
}

public sealed class FloorPlan
{
    public int Floor, Depth, Level, Need, Cells;
    public string Objective = "", Seed = "";
}

public sealed partial class LevelLoader
{
    /// <summary>What a floor can ask of the party.</summary>
    public static readonly (string Id, string Label)[] Objectives =
    {
        ("clear", "Clear the floor"),
        ("boss", "Slay the floor's master"),
        ("exit", "Find the way down"),
        ("sigil", "Take the imp's sigil"),
        ("shards", "Gather the imp's sigils"),
        ("cull", "Thin them out"),
        ("vault", "Open the vault"),
    };

    public static int ObjectiveNeed(string objective, int depth) => objective switch
    {
        "shards" => 2 + depth / 3,
        "cull" => 4 + depth,
        _ => 1,
    };

    public static string FloorSizeName(int depth)
    {
        int cells = DungeonFloor.Cells(depth);
        return cells <= 7 ? "small" : cells <= 9 ? "fair-sized" : cells <= 11 ? "big" : "huge";
    }

    public static int MonsterCount(int depth) => Math.Min(24, 6 + 2 * depth);

    /// <summary>How deep the party have been, and how their runs went.</summary>
    public readonly DungeonRunRecord PitRun = new();

    /// <summary>
    /// Everything about a floor. Floors are ordered by how hard a level's own monsters hit rather
    /// than by its number: walking into the Keep's guards on floor one is what "borrow a level the
    /// party have walked" gets you otherwise.
    /// </summary>
    /// <summary>
    /// What this level's monsters are worth, so the pit can offer its floors gently first. The sum
    /// main.mjs makes when it writes a visit down: over every kind the level actually loaded, its hit
    /// points plus twice everything it hits with, and the worst of them wins.
    /// </summary>
    public int LevelDanger()
    {
        int worst = 0;
        if (Board?.Properties == null) return 0;
        foreach (var p in Board.Properties)
        {
            if (p == null || p.HitPoints == 0 || p.MaxWidth == 0) continue;
            int might = 0;
            foreach (int m in p.ItemsMight) might += m;
            worst = Math.Max(worst, p.HitPoints + might * 2);
        }
        return worst;
    }

    public FloorPlan FloorPlanFor(int floor, IReadOnlyDictionary<int, int> dangerByLevel)
    {
        var candidates = dangerByLevel
            .Where(e => e.Key >= 1 && e.Key <= 29 && e.Key != Level)
            .OrderBy(e => e.Value).ThenBy(e => e.Key)
            .Select(e => e.Key)
            .ToArray();
        if (candidates.Length == 0) return null;
        int depth = Math.Max(1, floor);
        int level = candidates[Math.Min(depth - 1, candidates.Length - 1)];
        var random = DungeonFloor.Rng($"pit:{depth}");
        string objective = Objectives[(int)Math.Floor(random() * Objectives.Length)].Id;
        return new FloorPlan
        {
            Floor = depth, Depth = depth, Level = level, Objective = objective,
            Need = ObjectiveNeed(objective, depth),
            Seed = $"pit:{depth}:{(int)Math.Floor(random() * 1e9)}",
            Cells = DungeonFloor.Cells(depth),
        };
    }

    /// <summary>
    /// What a finished floor pays: credits, experience, reagents and things for the bag. Nothing is
    /// left lying on the floor, so nothing is lost when the floor is thrown away.
    /// </summary>
    public List<string> DungeonRewards(int depth, string seed = "pit", IEnumerable<Potion> extraItems = null)
    {
        var random = DungeonFloor.Rng($"{seed}:reward");
        var got = new List<string>();
        int credits = 40 + 25 * depth + (int)Math.Floor(random() * 40);
        Items.GiveCredits(credits);
        got.Add($"{credits} credits");

        int points = JsMath.RoundToInt((60 + 40 * depth) / 3.0);
        for (int c = 0; c < 4; c += 1)
        {
            var ch = Characters[c];
            if (!ch.Active || (ch.Flags & 8) != 0) continue;
            for (int skill = 0; skill < 3; skill += 1) Board.IncreaseExperience(c, skill, points);
        }
        got.Add($"{points} experience each");

        int rolls = 1 + depth / 4 + (random() < 0.5 ? 1 : 0);
        for (int i = 0; i < rolls; i += 1)
        {
            double roll = random();
            if (roll < 0.45)
            {
                var keys = Pouch.Keys.ToArray();
                string key = keys[(int)Math.Floor(random() * keys.Length)];
                int n = 1 + (int)Math.Floor(random() * 3);
                Pouch[key] += n;
                got.Add($"{Reagents.First(r => r.Key == key).Name} ×{n}");
            }
            else
            {
                var props = RegisterExtraItems(Potions.Concat(extraItems ?? Enumerable.Empty<Potion>()).ToArray());
                var potion = Potions[(int)Math.Floor(random() * Potions.Length)];
                int made = props.TryGetValue(potion.Id, out int prop) ? DungeonGiveItem(prop) : 0;
                if (made != 0) got.Add(potion.Name);
                else
                {
                    Items.GiveCredits(40);   // no room in the bag: he pays instead
                    got.Add("40 credits (no room in the pack)");
                }
            }
        }
        return got;
    }

    /// <summary>Puts one item in the first free bag slot, or nothing when there is no room.</summary>
    public int DungeonGiveItem(int prop)
    {
        int free = Array.IndexOf(Items.Inventory, 0);
        if (free < 0) return 0;
        int spare = 0;
        for (int i = 1; i < 400 && i < Items.InPlay.Length; i += 1) if (Items.InPlay[i].ItemPropertyIndex == 0) spare += 1;
        if (spare < 20) return 0;   // leave the item table room to breathe
        int item = Items.Make(prop, 0, 0, Level);
        if (item == -1) return 0;
        Items.Inventory[free] = item;
        Gui?.DrawInventory();
        return item;
    }
}
