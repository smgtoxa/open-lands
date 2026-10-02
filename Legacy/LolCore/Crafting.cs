// Crafting: what a slain creature leaves behind, and what the camp's cauldron makes of it.
//
// Transliterated from src/game/crafting.mjs. The reagents are not game items - nothing in the
// original data is touched - so they live in a pouch of their own, and the brews are registered as
// extra item properties. Where the pouch is *kept* between sessions is the host's business; what
// drops, what it takes and what comes out is the engine's.
namespace LolCore;

public sealed class Reagent
{
    public string Key = "", Name = "", Hint = "";
}

public sealed class Potion
{
    public string Id = "", Name = "", Effect = "", Use = "";
    public int Icon, Amount, Price, Skill = -1, Seconds;
    /// <summary>"errand" items are the imp's, and nothing buys them.</summary>
    public string Kind = "";
}

public sealed class Recipe
{
    public string Id = "", Name = "", Effect = "";
    public Dictionary<string, int> Needs = new();
}

public sealed class MonsterFamily
{
    public string Id = "", Name = "";
    public System.Text.RegularExpressions.Regex Match;
    public string[] Table = Array.Empty<string>();
}

public sealed partial class LevelLoader
{
    /// <summary>One kill in ten leaves anything at all.</summary>
    public const double DropChance = 0.1;

    public static readonly Reagent[] Reagents =
    {
        new() { Key = "moss", Name = "Glowing moss", Hint = "Cave-light lichen; the base of every healing brew." },
        new() { Key = "sap", Name = "Swamp sap", Hint = "Thick Gorkha resin that binds a mixture." },
        new() { Key = "bone", Name = "Bone dust", Hint = "Ground remains, bitter and drying." },
        new() { Key = "bile", Name = "Vial of bile", Hint = "Drawn from a beast's gut; a poison fights a poison." },
        new() { Key = "shard", Name = "Crystal shard", Hint = "Holds a little of the magic that formed it." },
        new() { Key = "ember", Name = "Ember gland", Hint = "Still warm. Burns what it touches." },
    };

    /// <summary>The potions the port adds; they borrow the icons of the herbs that do the same job.</summary>
    public static readonly Potion[] Potions =
    {
        new() { Id = "heal", Name = "Healing Potion", Icon = 217, Effect = "heal", Amount = 25, Price = 40, Use = "Drink it to close wounds" },
        new() { Id = "mana", Name = "Mana Potion", Icon = 218, Effect = "mana", Amount = 20, Price = 40, Use = "Drink it to restore magic" },
        new() { Id = "antidote", Name = "Antidote", Icon = 216, Effect = "cure", Price = 30, Use = "Drink it to cure poison" },
        new() { Id = "strength", Name = "Strength Potion", Icon = 216, Effect = "skill", Skill = 0, Seconds = 30, Price = 90, Use = "Drink it: fighter skill doubled for 30 seconds" },
        new() { Id = "agility", Name = "Agility Potion", Icon = 216, Effect = "skill", Skill = 1, Seconds = 30, Price = 90, Use = "Drink it: rogue skill doubled for 30 seconds" },
        new() { Id = "arcane", Name = "Arcane Potion", Icon = 216, Effect = "skill", Skill = 2, Seconds = 30, Price = 90, Use = "Drink it: mage skill doubled for 30 seconds" },
    };

    /// <summary>A recipe is learnt by hunting: so many of the creatures that carry its key reagent.</summary>
    public static readonly Dictionary<string, (string Family, int Kills)> RecipeUnlock = new()
    {
        ["antidote"] = ("crawling", 3),
        ["strength"] = ("burning", 4),
        ["agility"] = ("crawling", 6),
        ["arcane"] = ("stone", 5),
    };

    public static readonly Recipe[] Recipes =
    {
        new() { Id = "heal", Name = "Healing Potion", Effect = "Restores 25 health", Needs = new() { ["moss"] = 2, ["sap"] = 1 } },
        new() { Id = "mana", Name = "Mana Potion", Effect = "Restores 20 magic", Needs = new() { ["shard"] = 2, ["moss"] = 1 } },
        new() { Id = "antidote", Name = "Antidote", Effect = "Cures poison", Needs = new() { ["bile"] = 2, ["bone"] = 1 } },
        new() { Id = "strength", Name = "Strength Potion", Effect = "Fighter skill x2 for 30s", Needs = new() { ["ember"] = 2, ["bone"] = 1 } },
        new() { Id = "agility", Name = "Agility Potion", Effect = "Rogue skill x2 for 30s", Needs = new() { ["sap"] = 2, ["shard"] = 1 } },
        new() { Id = "arcane", Name = "Arcane Potion", Effect = "Mage skill x2 for 30s", Needs = new() { ["shard"] = 2, ["ember"] = 1 } },
    };

    /// <summary>What a creature leaves follows what it is.</summary>
    public static readonly MonsterFamily[] Families =
    {
        new()
        {
            Id = "undead", Name = "the undead",
            Match = new("skelet|bone|zombie|wraith|ghost|lich|undead|mantha"),
            Table = new[] { "bone", "bone", "bone", "moss", "shard", "bile" },
        },
        new()
        {
            Id = "burning", Name = "burning things",
            Match = new("fire|flame|ember|drake|dragon|salamander|imp|demon|dethdisk"),
            Table = new[] { "ember", "ember", "shard", "moss", "bone", "bile" },
        },
        new()
        {
            Id = "crawling", Name = "swamp crawlers",
            Match = new("swamp|slime|leech|worm|insect|spider|scorpion|snake|toad|frog|hornet"),
            Table = new[] { "bile", "bile", "sap", "sap", "moss", "bone" },
        },
        new()
        {
            Id = "stone", Name = "things of stone",
            Match = new("golem|stone|rock|crystal|statue|guardian|trez|xeob"),
            Table = new[] { "shard", "shard", "shard", "bone", "moss", "ember" },
        },
        new()
        {
            Id = "common", Name = "wandering beasts",
            Match = new(".*"),
            Table = new[] { "moss", "moss", "sap", "bone", "bile", "shard" },
        },
    };

    public static MonsterFamily FamilyOf(string monsterName)
    {
        string n = (monsterName ?? "").ToLowerInvariant();
        return Families.FirstOrDefault(f => f.Match.IsMatch(n)) ?? Families[^1];
    }

    /// <summary>Everything a creature can leave, most likely first: the bestiary's "Leaves" list.</summary>
    public static List<(string Key, string Name, int Share, double Chance)> DropsFor(string monsterName)
    {
        var table = FamilyOf(monsterName).Table;
        var counts = new Dictionary<string, int>();
        foreach (string k in table) counts[k] = counts.GetValueOrDefault(k) + 1;
        return counts.OrderByDescending(e => e.Value)
            .Select(e => (
                e.Key,
                Reagents.First(r => r.Key == e.Key).Name,
                (int)JsMath.Round(100.0 * e.Value / table.Length),
                JsMath.Round(1000 * DropChance * e.Value / table.Length) / 10))
            .ToList();
    }

    public static string RecipeHint(string recipeId)
    {
        if (!RecipeUnlock.TryGetValue(recipeId, out var need)) return "";
        var family = Families.FirstOrDefault(f => f.Id == need.Family);
        return $"Learnt by hunting: kill {need.Kills} of {family?.Name ?? need.Family}.";
    }

    /// <summary>The presentation stream a drop is rolled off; the host wires it to the interface's.</summary>
    public Func<double> PresentationRoll;

    /// <summary>The pouch: what the party are carrying to the cauldron.</summary>
    public readonly Dictionary<string, int> Pouch = Reagents.ToDictionary(r => r.Key, _ => 0);

    /// <summary>The player's record, which is where the kills that unlock a recipe are counted.</summary>
    public Meta Meta;

    public int PouchTotal() => Pouch.Values.Sum();

    /// <summary>How many of each family the party have killed, out of the bestiary.</summary>
    public Dictionary<string, int> KillsByFamily()
    {
        var out_ = new Dictionary<string, int>();
        if (Meta == null) return out_;
        foreach (var (name, entry) in Meta.Bestiary)
        {
            string id = FamilyOf(name).Id;
            out_[id] = out_.GetValueOrDefault(id) + entry.Kills;
        }
        return out_;
    }

    public bool RecipeKnown(string recipeId)
    {
        if (!RecipeUnlock.TryGetValue(recipeId, out var need)) return true;
        return KillsByFamily().GetValueOrDefault(need.Family) >= need.Kills;
    }

    public bool CanCraft(string recipeId)
    {
        var recipe = Recipes.FirstOrDefault(r => r.Id == recipeId);
        return recipe != null && recipe.Needs.All(n => Pouch.GetValueOrDefault(n.Key) >= n.Value);
    }

    /// <summary>
    /// A kill's reagent, or null. The roll comes off the presentation stream: what a monster leaves
    /// is a flourish, never something the story depends on.
    /// </summary>
    public string RollDrop(string monsterName, Func<double> random = null)
    {
        var roll = random ?? PresentationRoll;
        if (roll == null) return null;
        if (roll() >= DropChance) return null;
        var table = FamilyOf(monsterName).Table;
        string key = table[Math.Min(table.Length - 1, (int)Math.Floor(roll() * table.Length))];
        Pouch[key] += 1;
        return key;
    }

    /// <summary>Testing aid: exactly enough of everything to brew `each` of every recipe.</summary>
    public Dictionary<string, int> FillPouch(int each = 2)
    {
        foreach (var recipe in Recipes)
            foreach (var (key, n) in recipe.Needs) Pouch[key] += n * each;
        return Pouch;
    }

    /// <summary>Spends the reagents and puts the brew in the bag, or says why it could not.</summary>
    public (int Item, int Slot, string Error) Brew(string recipeId, IEnumerable<Potion> extraItems = null)
    {
        var recipe = Recipes.FirstOrDefault(r => r.Id == recipeId);
        if (recipe == null) return (-1, -1, "No such recipe.");
        if (!RecipeKnown(recipeId)) return (-1, -1, "The recipe is not known yet.");
        if (!CanCraft(recipeId)) return (-1, -1, "Not enough reagents.");
        int slot = Array.IndexOf(Items.Inventory, 0);
        if (slot < 0) return (-1, -1, "Your inventory is full.");
        var props = RegisterExtraItems(Potions.Concat(extraItems ?? Enumerable.Empty<Potion>()).ToArray());
        if (!props.TryGetValue(recipe.Id, out int prop)) return (-1, -1, "The recipe is not ready yet.");
        int item = Items.Make(prop, 0, 0, Level);
        if (item == -1) return (-1, -1, "The mixture spoils: there is no room in the world for it.");
        foreach (var (key, n) in recipe.Needs) Pouch[key] -= n;
        Items.Inventory[slot] = item;
        Gui?.DrawInventory();
        return (item, slot, null);
    }

    /// <summary>What the port adds to the item table, by id; the engine keeps one block of them.</summary>
    public readonly Dictionary<int, Potion> ExtraItems = new();

    private int _extraBase = -1;

    /// <summary>uiRegisterExtraItems: idempotent, and always one block.</summary>
    public Dictionary<string, int> RegisterExtraItems(IReadOnlyList<Potion> defs)
    {
        var out_ = new Dictionary<string, int>(StringComparer.Ordinal);
        if (Items.Properties == null || Items.Properties.Length == 0) return out_;
        if (_extraBase == Items.Properties.Length - defs.Count)
        {
            for (int i = 0; i < defs.Count; i += 1) out_[defs[i].Id] = _extraBase + i;
            return out_;
        }
        _extraBase = Items.Properties.Length;
        ExtraItems.Clear();
        var grown = new ItemProperty[_extraBase + defs.Count];
        Array.Copy(Items.Properties, grown, _extraBase);
        for (int i = 0; i < defs.Count; i += 1)
        {
            var def = defs[i];
            var icon = def.Icon < Items.Properties.Length ? Items.Properties[def.Icon] : null;
            int prop = _extraBase + i;
            grown[prop] = new ItemProperty
            {
                NameStringId = 0, ShpIndex = icon?.ShpIndex ?? 0, Flags = def.Kind == "errand" ? 0 : 1,
                Type = 0, ItemScriptFunc = 0xff, Might = 0, Skill = 0, Protection = 0, UnkB = def.Price,
            };
            ExtraItems[prop] = def;
            out_[def.Id] = prop;
        }
        Items.Properties = grown;
        return out_;
    }

    /// <summary>What an item is, when it is one of the port's own.</summary>
    public Potion ExtraItem(int item)
    {
        var it = item > 0 && item < Items.InPlay.Length ? Items.InPlay[item] : null;
        return it != null ? ExtraItems.GetValueOrDefault(it.ItemPropertyIndex) : null;
    }
}
