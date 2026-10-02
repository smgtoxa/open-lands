// What the merchants pay for what the party carries.
//
// Transliterated from src/game/shops.mjs, whose rules were read out of the shopkeepers' own level
// scripts (LEVEL<n>.INF) so the trade window can quote a price without running the conversation for
// every item. Anyone not listed here has no transcribed rule: the item is offered by hand and the
// script answers, which is exactly what the browser build does.
namespace LolCore;

public sealed class SellRule
{
    public string Name = "";
    public int[] Full = Array.Empty<int>();
    public bool Half;
    /// <summary>The full price only holds until this flag is set (the swamp stalls, once the party
    /// are Swamp Heroes).</summary>
    public int FullUnlessFlag;
    /// <summary>Victor's Yvel shop pays full only for a piece that carries item flag 8.</summary>
    public bool FullNeedsFlag8;
    /// <summary>Selling one of these back puts it on display again: the flags its script clears.</summary>
    public Dictionary<int, int[]> OnSold = new();
    /// <summary>Shop pieces whose sale also flips a shop flag - left to the script.</summary>
    public int[] Special = Array.Empty<int>();
}

public sealed class SellOffer
{
    public int Price;
    public bool Full, Refused, Ask, Special;
    public string Reason = "";
}

public sealed partial class LevelLoader
{
    /// <summary>The transcribed rules, by "level:merchant block".</summary>
    public static readonly Dictionary<string, SellRule> SellRules = new()
    {
        // Gladstone: selling his mace or dagger back puts it on display again.
        ["1:401"] = new SellRule
        {
            Name = "Victor", Full = new[] { 93, 44, 90, 81 }, Half = true,
            OnSold = new Dictionary<int, int[]> { [81] = new[] { 18 }, [90] = new[] { 19 } },
        },
        // Gorkha Swamp: his own pieces at full price, the rest half - and full stops once flag 81 is up.
        ["11:334"] = new SellRule
        {
            Name = "The swamp trader", Full = new[] { 66, 35, 39 }, FullUnlessFlag = 81, Half = true,
            OnSold = new Dictionary<int, int[]> { [66] = new[] { 274 }, [35] = new[] { 275 }, [39] = new[] { 276 } },
        },
        // The swamp bowyer: the same helper as the trader.
        ["11:660"] = new SellRule
        {
            Name = "Scomish", Full = new[] { 124, 52, 70, 149 }, FullUnlessFlag = 81, Half = true,
            OnSold = new Dictionary<int, int[]>
            {
                [124] = new[] { 279 }, [52] = new[] { 277 }, [70] = new[] { 278 }, [149] = new[] { 280 },
            },
        },
        // Yvel: the display pieces also flip a shop flag, so those go through the script.
        ["22:658"] = new SellRule
        {
            Name = "Victor", Full = new[] { 57, 74, 106, 51 }, FullNeedsFlag8 = true, Half = true,
            Special = new[] { 57, 74, 106, 51 },
        },
    };

    public SellRule SellRuleFor(int merchant) => SellRules.GetValueOrDefault($"{Level}:{merchant}");

    private bool GameFlag(int flag) => flag >= 0 && ((Flags[(flag >> 3) & 0xff] >> (flag & 7)) & 1) != 0;

    private void ClearGameFlag(int flag) => Flags[(flag >> 3) & 0xff] &= (byte)~(1 << (flag & 7));

    /// <summary>getItemPrice: the smallest step of the price ladder that is at least the base value.</summary>
    private static int PriceLadder(int baseValue)
    {
        var prices = StaticData.Table("ItemPrices");
        for (int i = 0; i < 46; i += 1) if (prices[i] >= baseValue) return prices[i];
        return 0;
    }

    /// <summary>What a merchant would pay for an item the party is holding.</summary>
    public SellOffer SellOffer(int merchant, int item)
    {
        var rule = SellRuleFor(merchant);
        // No transcribed rule: this shopkeeper's price is decided in conversation, so do not claim
        // to know it. The item is offered by hand and they answer themselves.
        if (rule == null) return new SellOffer { Ask = true, Reason = "Offer it and see: this shopkeeper's price is decided in conversation." };
        var it = Items.InPlay[item];
        var p = Items.Properties[it.ItemPropertyIndex];
        int type = it.ItemPropertyIndex;
        if (rule.Special.Contains(type))
            return new SellOffer { Special = true, Reason = "A shop piece: offer it by hand, the shopkeeper has something to say." };
        if (p.UnkB == 0 || (p.Flags & 4) != 0)
            return new SellOffer { Refused = true, Reason = $"{rule.Name} is not interested in that." };
        if (rule.Full.Contains(type) && (!rule.FullNeedsFlag8 || (p.Flags & 8) != 0)
            && !(rule.FullUnlessFlag != 0 && GameFlag(rule.FullUnlessFlag)))
            return new SellOffer { Price = PriceLadder(p.UnkB), Full = true };
        if (rule.Half) return new SellOffer { Price = PriceLadder(p.UnkB >> 1) };
        return new SellOffer { Refused = true, Reason = $"{rule.Name} is not interested in that." };
    }

    /// <summary>The sale itself, as the script would do it: money in, item gone, shop flags updated.</summary>
    public SellOffer SellItem(int merchant, int slot)
    {
        int item = Items.Inventory[slot];
        if (item == 0) return null;
        var offer = SellOffer(merchant, item);
        if (offer.Price == 0) return offer;
        var rule = SellRuleFor(merchant);
        int type = Items.InPlay[item].ItemPropertyIndex;
        Items.Inventory[slot] = 0;
        Items.Delete(item);
        Items.GiveCredits(offer.Price);
        if (rule.OnSold.TryGetValue(type, out var flags)) foreach (int f in flags) ClearGameFlag(f);
        Gui?.DrawInventory();
        return offer;
    }
}
