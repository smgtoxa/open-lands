// src/game/crafting.mjs
// Crafting: what a slain creature leaves behind, and what the camp's cauldron makes of it.
//
// A port addition. The reagents are not game items - nothing in the original data is touched - so
// they live in a pouch of their own, and the brews are registered as extra item properties at
// start-up. The rules are here rather than in a host so that every build drops the same reagents
// from the same creatures and brews the same potions from them; where the pouch is *kept* is the
// host's business.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lol
{
    /// <summary>crafting.mjs REAGENTS values</summary>
    public sealed class Reagent
    {
        public string name, hint;
    }

    /// <summary>An item the port adds (crafting.mjs POTIONS, camp-store.mjs ERRAND_ITEMS / DUNGEON_ITEMS, the briars):
    /// the defs handed to uiRegisterExtraItems and kept in extraItems.</summary>
    public sealed class ExtraItemDef
    {
        public string id, name;
        public int icon;
        public string art, effect;
        public int amount, price;
        public string use;
        public int skill, seconds;
        public string kind, family;
    }

    /// <summary>crafting.mjs RECIPE_UNLOCK values</summary>
    public sealed class RecipeUnlock
    {
        public string family;
        public int kills;
    }

    /// <summary>crafting.mjs RECIPES</summary>
    public sealed class Recipe
    {
        public string id, name, effect;
        public Dictionary<string, int> needs;
    }

    /// <summary>crafting.mjs FAMILIES</summary>
    public sealed class Family
    {
        public string id, name;
        public Regex match;
        public string[] table;
    }

    /// <summary>crafting.mjs dropsFor entries</summary>
    public sealed class ReagentDrop
    {
        public string key, name;
        public int share;
        public double chance;
    }

    public sealed partial class LandsOfLore
    {
        public const double DROP_CHANCE = 0.1;   // one kill in ten leaves anything at all

        public static readonly Dictionary<string, Reagent> REAGENTS = new Dictionary<string, Reagent>
        {
            ["moss"] = new Reagent { name = "Glowing moss", hint = "Cave-light lichen; the base of every healing brew." },
            ["sap"] = new Reagent { name = "Swamp sap", hint = "Thick Gorkha resin that binds a mixture." },
            ["bone"] = new Reagent { name = "Bone dust", hint = "Ground remains, bitter and drying." },
            ["bile"] = new Reagent { name = "Vial of bile", hint = "Drawn from a beast's gut; a poison fights a poison." },
            ["shard"] = new Reagent { name = "Crystal shard", hint = "Holds a little of the magic that formed it." },
            ["ember"] = new Reagent { name = "Ember gland", hint = "Still warm. Burns what it touches." },
        };

        public static readonly string[] REAGENT_KEYS = REAGENTS.Keys.ToArray();

        // The potions the port adds. They borrow the icons of the herbs that do the same job, because the
        // original graphics have no potion sprite of their own.
        public static readonly ExtraItemDef[] POTIONS =
        {
            new ExtraItemDef { id = "heal", name = "Healing Potion", icon = 217, art = "src/assets/potion-heal.svg", effect = "heal", amount = 25, price = 40, use = "Drink it to close wounds" },
            new ExtraItemDef { id = "mana", name = "Mana Potion", icon = 218, art = "src/assets/potion-mana.svg", effect = "mana", amount = 20, price = 40, use = "Drink it to restore magic" },
            new ExtraItemDef { id = "antidote", name = "Antidote", icon = 216, art = "src/assets/potion-antidote.svg", effect = "cure", amount = 0, price = 30, use = "Drink it to cure poison" },
            // The three enchanted brews double one skill for half a minute, through the engine's own skill
            // modifier (skill 0 fighter, 1 rogue, 2 mage), so everything that reads a skill sees the buff.
            new ExtraItemDef { id = "strength", name = "Strength Potion", icon = 216, art = "src/assets/potion-strength.svg", effect = "skill", skill = 0, seconds = 30, price = 90, use = "Drink it: fighter skill doubled for 30 seconds" },
            new ExtraItemDef { id = "agility", name = "Agility Potion", icon = 216, art = "src/assets/potion-agility.svg", effect = "skill", skill = 1, seconds = 30, price = 90, use = "Drink it: rogue skill doubled for 30 seconds" },
            new ExtraItemDef { id = "arcane", name = "Arcane Potion", icon = 216, art = "src/assets/potion-arcane.svg", effect = "skill", skill = 2, seconds = 30, price = 90, use = "Drink it: mage skill doubled for 30 seconds" },
        };

        // A recipe is learnt by hunting: the party have to have killed enough of the creatures that carry
        // its key reagent before they know what to do with them.
        public static readonly Dictionary<string, RecipeUnlock> RECIPE_UNLOCK = new Dictionary<string, RecipeUnlock>
        {
            ["heal"] = null,
            ["mana"] = null,
            ["antidote"] = new RecipeUnlock { family = "crawling", kills = 3 },
            ["strength"] = new RecipeUnlock { family = "burning", kills = 4 },
            ["agility"] = new RecipeUnlock { family = "crawling", kills = 6 },
            ["arcane"] = new RecipeUnlock { family = "stone", kills = 5 },
        };

        public static readonly Recipe[] RECIPES =
        {
            new Recipe { id = "heal", name = "Healing Potion", effect = "Restores 25 health", needs = new Dictionary<string, int> { ["moss"] = 2, ["sap"] = 1 } },
            new Recipe { id = "mana", name = "Mana Potion", effect = "Restores 20 magic", needs = new Dictionary<string, int> { ["shard"] = 2, ["moss"] = 1 } },
            new Recipe { id = "antidote", name = "Antidote", effect = "Cures poison", needs = new Dictionary<string, int> { ["bile"] = 2, ["bone"] = 1 } },
            new Recipe { id = "strength", name = "Strength Potion", effect = "Fighter skill x2 for 30s", needs = new Dictionary<string, int> { ["ember"] = 2, ["bone"] = 1 } },
            new Recipe { id = "agility", name = "Agility Potion", effect = "Rogue skill x2 for 30s", needs = new Dictionary<string, int> { ["sap"] = 2, ["shard"] = 1 } },
            new Recipe { id = "arcane", name = "Arcane Potion", effect = "Mage skill x2 for 30s", needs = new Dictionary<string, int> { ["shard"] = 2, ["ember"] = 1 } },
        };

        // What a creature leaves follows what it is. The families are named so the bestiary can say where a
        // reagent comes from, and so the imp can ask for a kind of creature rather than a single beast.
        public static readonly Family[] FAMILIES =
        {
            new Family { id = "undead", name = "the undead", match = new Regex("skelet|bone|zombie|wraith|ghost|lich|undead|mantha"),
                table = new[] { "bone", "bone", "bone", "moss", "shard", "bile" } },
            new Family { id = "burning", name = "burning things", match = new Regex("fire|flame|ember|drake|dragon|salamander|imp|demon|dethdisk"),
                table = new[] { "ember", "ember", "shard", "moss", "bone", "bile" } },
            new Family { id = "crawling", name = "swamp crawlers", match = new Regex("swamp|slime|leech|worm|insect|spider|scorpion|snake|toad|frog|hornet"),
                table = new[] { "bile", "bile", "sap", "sap", "moss", "bone" } },
            new Family { id = "stone", name = "things of stone", match = new Regex("golem|stone|rock|crystal|statue|guardian|trez|xeob"),
                table = new[] { "shard", "shard", "shard", "bone", "moss", "ember" } },
            new Family { id = "common", name = "wandering beasts", match = new Regex(".*"),
                table = new[] { "moss", "moss", "sap", "bone", "bile", "shard" } },
        };

        public static Family familyOf(string monsterName = "")
        {
            string n = monsterName.ToLowerInvariant();
            return FAMILIES.FirstOrDefault(f => f.match.IsMatch(n)) ?? FAMILIES[FAMILIES.Length - 1];
        }

        // Everything a creature can leave, most likely first: what the bestiary lists under "Leaves".
        public static List<ReagentDrop> dropsFor(string monsterName = "")
        {
            var table = familyOf(monsterName).table;
            var counts = new Dictionary<string, int>();
            foreach (var k in table) counts[k] = (counts.TryGetValue(k, out int c) ? c : 0) + 1;
            return counts
                .OrderByDescending(e => e.Value)   // stable, like Array.prototype.sort
                // `chance` is per kill: the tenth of kills that leave anything, times this reagent's share.
                .Select(e => new ReagentDrop { key = e.Key, name = REAGENTS[e.Key].name, share = Js.Round((100.0 * e.Value) / table.Length), chance = Js.Round((1000 * DROP_CHANCE * e.Value) / table.Length) / 10.0 })
                .ToList();
        }

        public static string recipeHint(Recipe recipe)
        {
            var need = RECIPE_UNLOCK.TryGetValue(recipe.id, out var u) ? u : null;
            if (need == null) return "";
            var family = FAMILIES.FirstOrDefault(f => f.id == need.family);
            return $"Learnt by hunting: kill {need.kills} of {(family != null ? family.name : need.family)}.";
        }

        // ---- CraftMixin ----
        public Dictionary<string, int> pouch;

        public Dictionary<string, int> initCraft()
        {
            pouch = REAGENT_KEYS.ToDictionary(k => k, k => 0);
            return pouch;
        }

        // The host keeps the pouch between sessions; this takes back what it stored.
        public Dictionary<string, int> uiCraftLoad(JsonObject saved)
        {
            initCraft();
            foreach (var k in REAGENT_KEYS) pouch[k] = Math.Max(0, Store_TruncOr0(Store_Number(saved?[k])));
            return pouch;
        }

        public Dictionary<string, int> uiCraftPouch() => pouch;

        public int uiCraftPouchTotal() => REAGENT_KEYS.Aggregate(0, (n, k) => n + pouch[k]);

        // How many of each family the party have killed, out of the bestiary they have been filling.
        public Dictionary<string, int> uiCraftKillsByFamily()
        {
            var @out = new Dictionary<string, int>();
            foreach (var e in meta != null ? meta.bestiary : new Dictionary<string, BestiaryEntry>())
            {
                string id = familyOf(e.Key).id;
                @out[id] = (@out.TryGetValue(id, out int n) ? n : 0) + e.Value.kills;
            }
            return @out;
        }

        public bool uiCraftKnown(string recipeId)
        {
            var need = RECIPE_UNLOCK.TryGetValue(recipeId, out var u) ? u : null;
            if (need == null) return true;
            return (uiCraftKillsByFamily().TryGetValue(need.family, out int k) ? k : 0) >= need.kills;
        }

        public bool uiCraftCan(string recipeId)
        {
            var recipe = RECIPES.FirstOrDefault(r => r.id == recipeId);
            if (recipe == null) return false;
            return recipe.needs.All(e => pouch[e.Key] >= e.Value);
        }

        // A kill's reagent, or null. The roll is the *presentation* stream: what a monster leaves is a
        // flourish, never something the story depends on.
        public string uiCraftRollDrop(string monsterName = "", Func<double> random = null)
        {
            var roll = random ?? (() => presentationRandom(1000) / 1000.0);
            if (roll() >= DROP_CHANCE) return null;
            var table = familyOf(monsterName).table;
            string key = table[Math.Min(table.Length - 1, Js.Floor(roll() * table.Length))];
            pouch[key] += 1;
            return key;
        }

        // Registers everything the port adds to the item table in one call, because the engine keeps a
        // single block of extra item properties. Idempotent; returns { id: itemProperty }.
        public Dictionary<string, int> uiCraftRegisterItems(IEnumerable<ExtraItemDef> extra = null)
        {
            return uiRegisterExtraItems(POTIONS.Concat(extra ?? Enumerable.Empty<ExtraItemDef>()).ToList()) ?? new Dictionary<string, int>();
        }

        // Spends the reagents and puts the brew in the bag. Returns the item, or a reason it failed.
        public UiResult uiCraftBrew(string recipeId, IEnumerable<ExtraItemDef> extraItems = null)
        {
            var recipe = RECIPES.FirstOrDefault(r => r.id == recipeId);
            if (recipe == null) return new UiResult { error = "No such recipe." };
            if (!uiCraftKnown(recipeId)) return new UiResult { error = "The recipe is not known yet." };
            if (!uiCraftCan(recipeId)) return new UiResult { error = "Not enough reagents." };
            int slot = Array.IndexOf(inventory, (ushort)0);
            if (slot < 0) return new UiResult { error = "Your inventory is full." };
            if (!uiCraftRegisterItems(extraItems).TryGetValue(recipe.id, out int prop)) return new UiResult { error = "The recipe is not ready yet." };
            // makeItem throws when the world is out of item slots; the reagents must not be spent for nothing.
            int item = -1;
            try { item = makeItem(prop, 0, 0); } catch (QuitException) { throw; } catch (Exception) { item = -1; }
            if (item == -1) return new UiResult { error = "The mixture spoils: there is no room in the world for it." };
            foreach (var e in recipe.needs) pouch[e.Key] -= e.Value;
            inventory[slot] = (ushort)item;
            gui_drawInventory();
            return new UiResult { item = item, slot = slot };
        }

        // Testing aid: exactly enough of everything to brew `each` of every recipe.
        public Dictionary<string, int> uiCraftFillPouch(int each = 2)
        {
            foreach (var recipe in RECIPES) foreach (var e in recipe.needs) pouch[e.Key] += e.Value * each;
            return pouch;
        }
    }
}
