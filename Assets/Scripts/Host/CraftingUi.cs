// src/platform/crafting.mjs. C# 9.
// Crafting, as the browser stores it.
//
// The rules - the reagents, the families, the recipes and what a kill leaves - live in the engine
// (src/game/crafting.mjs) so that every build agrees on them. What is here is the pouch's home in
// localStorage, and the thin calls the page's panels were already written against.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Lol;

namespace LolHost
{
    public static class CraftingUi
    {
        // export { DROP_CHANCE, REAGENTS, POTIONS, RECIPE_UNLOCK, RECIPES, FAMILIES, familyOf, dropsFor, recipeHint }
        public const double DROP_CHANCE = LandsOfLore.DROP_CHANCE;
        public static Dictionary<string, Reagent> REAGENTS => LandsOfLore.REAGENTS;
        static string[] REAGENT_KEYS => LandsOfLore.REAGENT_KEYS;
        public static ExtraItemDef[] POTIONS => LandsOfLore.POTIONS;
        public static Dictionary<string, RecipeUnlock> RECIPE_UNLOCK => LandsOfLore.RECIPE_UNLOCK;
        public static Recipe[] RECIPES => LandsOfLore.RECIPES;
        public static Family[] FAMILIES => LandsOfLore.FAMILIES;
        public static Family familyOf(string monsterName = "") => LandsOfLore.familyOf(monsterName);
        public static List<ReagentDrop> dropsFor(string monsterName = "") => LandsOfLore.dropsFor(monsterName);
        public static string recipeHint(Recipe recipe) => LandsOfLore.recipeHint(recipe);

        const string KEY = "lol.craft";

        public static Dictionary<string, int> readPouch()
        {
            JsonNode raw = Store.readJson(KEY, null);
            var @out = new Dictionary<string, int>();
            foreach (var k in REAGENT_KEYS) @out[k] = Math.Max(0, Store.truncOr0(Store.jsNumberOf(raw, k)));
            return @out;
        }

        // Returns true when the pouch is really stored: brewing spends reagents, so a silent failure would
        // hand out a potion for nothing (or take the reagents for nothing).
        public static bool writePouch(Dictionary<string, int> pouch)
        {
            return Store.writeJson(KEY, pouch);
        }

        public static int pouchTotal(Dictionary<string, int> pouch = null)
        {
            pouch = pouch ?? readPouch();
            return REAGENT_KEYS.Aggregate(0, (n, k) => n + pouch[k]);
        }

        public static bool recipeKnown(Recipe recipe, Dictionary<string, int> killsByFamily)
        {
            var need = RECIPE_UNLOCK.TryGetValue(recipe.id, out var u) ? u : null;
            if (need == null) return true;
            return (killsByFamily.TryGetValue(need.family, out int k) ? k : 0) >= need.kills;
        }

        // A kill's reagent, or null: the engine rolls it, this keeps the result.
        public static string rollDrop(LandsOfLore engine, string monsterName = "", Func<double> random = null)
        {
            if (engine == null) return null;
            string key = engine.uiCraftRollDrop(monsterName, random);
            if (key != null) writePouch(engine.uiCraftPouch());
            return key;
        }

        public static Dictionary<string, int> fillPouch(LandsOfLore engine, int each = 2)
        {
            var pouch = engine.uiCraftFillPouch(each);
            writePouch(pouch);
            return pouch;
        }

        public static bool canCraft(Recipe recipe, Dictionary<string, int> pouch = null)
        {
            pouch = pouch ?? readPouch();
            return recipe.needs.All(e => pouch.TryGetValue(e.Key, out int have) && have >= e.Value);
        }

        // Registers everything the port adds to the item table - the potions, the imp's errand trophies and
        // the pit's sigil - in one call.
        public static Dictionary<string, int> registerPotions(LandsOfLore engine)
        {
            return engine.uiCraftRegisterItems(ErrandItems.ERRAND_ITEMS.Concat(ErrandItems.DUNGEON_ITEMS).ToArray());
        }

        // Spends the reagents and puts the brew in the bag. Returns the item, or a reason it failed.
        public static UiResult craft(LandsOfLore engine, Recipe recipe)
        {
            var result = engine.uiCraftBrew(recipe.id, ErrandItems.ERRAND_ITEMS.Concat(ErrandItems.DUNGEON_ITEMS).ToArray());
            if (!string.IsNullOrEmpty(result.error)) return result;
            if (!writePouch(engine.uiCraftPouch()))
            {
                engine.deleteItem(result.item);
                engine.inventory[result.slot] = 0;
                return new UiResult { error = "The pouch cannot be written: this browser refused to store it." };
            }
            return result;
        }
    }
}
