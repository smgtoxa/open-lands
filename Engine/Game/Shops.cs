// src/game/shops.mjs: selling rules of the merchants, transcribed from their level scripts (LEVELxx.INF)
// so the trade window can quote offers instantly instead of running the shopkeeper's dialogue for every
// item. The rule for both of Victor's shops is: an item with no base price or with item flag 4 is
// refused; his own display pieces fetch the full price ladder; anything else half. Nobody else in the
// game buys goods in general besides the two swamp stalls (trader and Scomish the bowyer): the other
// shopkeepers only react to specific quest items, which still go through their scripts (offer the item
// by hand).
//
// getItemPrice ladder (script opcode): the smallest step of ItemPrices >= base value.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>shops.mjs SELL_RULES entry</summary>
    public sealed class SellRule
    {
        public string name;
        public int[] full;
        public bool half;
        /// <summary>item type -> game flags reset when it is sold (null = none)</summary>
        public Dictionary<int, int[]> onSold;
        /// <summary>0 = none</summary>
        public int fullUnlessFlag;
        public bool fullNeedsFlag8;
        public int[] special;
    }

    /// <summary>shops.mjs sellOffer: { special, reason } | { refused, reason } | { price, full }</summary>
    public sealed class SellOffer
    {
        public bool special, refused;
        public string reason;
        public int price;
        public bool full;
    }

    public sealed partial class LandsOfLore
    {
        public static readonly Dictionary<string, SellRule> SELL_RULES = new Dictionary<string, SellRule>
        {
            { "1:401", new SellRule { name = "Victor", full = new[] { 93, 44, 90, 81 }, half = true, onSold = new Dictionary<int, int[]> { { 81, new[] { 18 } }, { 90, new[] { 19 } } } } }, // Gladstone: selling his mace/dagger back puts it on display again (flags 18/19 cleared)
            { "11:334", new SellRule { name = "The swamp trader", full = new[] { 66, 35, 39 }, fullUnlessFlag = 81, half = true, onSold = new Dictionary<int, int[]> { { 66, new[] { 274 } }, { 35, new[] { 275 } }, { 39, new[] { 276 } } } } }, // Gorkha Swamp: his own pieces at full price (half once the party are "Swamp Heroes", flag 81), the rest half
            { "11:660", new SellRule { name = "Scomish", full = new[] { 124, 52, 70, 149 }, fullUnlessFlag = 81, half = true, onSold = new Dictionary<int, int[]> { { 124, new[] { 279 } }, { 52, new[] { 277 } }, { 70, new[] { 278 } }, { 149, new[] { 280 } } } } }, // the swamp bowyer: same helper as the trader
            { "22:658", new SellRule { name = "Victor", full = new[] { 57, 74, 106, 51 }, fullNeedsFlag8 = true, half = true, special = new[] { 57, 74, 106, 51 } } }, // Yvel: display pieces also flip a shop flag - left to the script
        };

        // What any shopkeeper without a transcribed rule pays: half the ladder, and nothing for what has
        // no price of its own or is flagged as a quest item.
        static readonly SellRule Shops_DEFAULT_RULE = new SellRule { name = "The shopkeeper", full = new int[0], half = true };

        public static SellOffer sellOffer(LandsOfLore engine, int merchant, int item)
        {
            // A shopkeeper with no transcribed rule still buys at the ladder's half price: quoting "ask" for
            // everything and then making the player offer each item by hand is not a shop, and the block a
            // counter happens to sit on differs from one game to the next, so keying the rule to one is no use.
            var rule = SELL_RULES.TryGetValue($"{engine.currentLevel}:{merchant}", out var r) ? r : Shops_DEFAULT_RULE;
            var it = engine.itemsInPlay[item];
            var p = engine.itemProperties[it.itemPropertyIndex];
            int type = it.itemPropertyIndex;
            if (rule.special != null && rule.special.Contains(type)) return new SellOffer { special = true, reason = "A shop piece: offer it by hand, the shopkeeper has something to say." };
            if (p.unkB == 0 || (p.flags & 4) != 0) return new SellOffer { refused = true, reason = $"{rule.name} is not interested in that." };
            int ladder(int @base) { for (int i = 0; i < 46; i += 1) if (engine.@static.ItemPrices[i] >= @base) return engine.@static.ItemPrices[i]; return 0; }
            if (rule.full.Contains(type) && (!rule.fullNeedsFlag8 || (p.flags & 8) != 0) && !(rule.fullUnlessFlag != 0 && engine.queryGameFlag(rule.fullUnlessFlag) != 0)) return new SellOffer { price = ladder(p.unkB), full = true };
            if (rule.half) return new SellOffer { price = ladder(p.unkB >> 1) };
            return new SellOffer { refused = true, reason = $"{rule.name} is not interested in that." };
        }

        // The sale itself, as the script would do it: money in, item gone, shop flags updated.
        public static async Task<SellOffer> sellItem(LandsOfLore engine, int merchant, int slot)
        {
            int item = engine.inventory[slot];
            if (item == 0) return null;
            var offer = sellOffer(engine, merchant, item);
            if (offer.price == 0) return offer;
            var rule = SELL_RULES.TryGetValue($"{engine.currentLevel}:{merchant}", out var r) ? r : Shops_DEFAULT_RULE;
            int type = engine.itemsInPlay[item].itemPropertyIndex;
            string name = engine.itemName(item);
            engine.inventory[slot] = 0;
            engine.deleteItem(item);
            await engine.giveCredits(offer.price, 1);
            if (rule.onSold != null && rule.onSold.TryGetValue(type, out var flags)) foreach (int f in flags) engine.resetGameFlag(f);
            engine.gui_drawInventory();
            engine.uiEmit("message", $"{rule.name} pays {offer.price} crowns for the {name}.", "combat");
            return offer;
        }
    }
}
