// The camp's chest and the imp who trades at it.
//
// Transliterated from src/game/camp-store.mjs. The ordering rules are the point of this file: the
// chest is written before the bag gives anything up, and a new item exists before the chest forgets
// it, so a host that cannot keep its storage loses nothing either way. Where the chest is *kept* is
// the host's business - it says so by returning false from the commit it passes in.
namespace LolCore;

public sealed class StashRecord
{
    public int Prop, Frame, Flags, Wear;
}

public sealed class ImpJob
{
    /// <summary>"collect" (bring trophies) or "hunt" (thin out a kind); null when he has none.</summary>
    public string Kind;
    public string Family = "", Item = "", Name = "", Hint = "";
    public int Need, From, Pay, Done;
    public double Chance = 1;
}

public sealed class CampUpgrade
{
    public string Id = "", Name = "", About = "";
    public int Price;
}

public sealed class Loadout
{
    public string Name = "";
    public List<(int Slot, int Prop, int Frame)> Worn = new();
}

public sealed partial class LevelLoader
{
    public const int StashSlots = 240;   // the chest at its largest; without the upgrade the first 144
    private const int FrameMask = 0x1fff;

    /// <summary>What a host says when it cannot keep the chest.</summary>
    public const string StorageError = "The chest will not hold it: this browser refused to store it (no space, or storage is blocked).";

    public static readonly Dictionary<string, int> ReagentPrice = new()
    {
        ["moss"] = 12, ["sap"] = 14, ["bone"] = 10, ["bile"] = 16, ["shard"] = 22, ["ember"] = 24,
    };

    /// <summary>Trophies: what a creature leaves for the imp's collecting errands.</summary>
    public static readonly Potion[] ErrandItems =
    {
        new() { Id = "bone", Name = "Skeleton Bone", Icon = 216, Kind = "errand", Use = "The imp asks for these" },
        new() { Id = "heart", Name = "Charred Heart", Icon = 216, Kind = "errand", Use = "The imp asks for these" },
        new() { Id = "chitin", Name = "Chitin Plate", Icon = 216, Kind = "errand", Use = "The imp asks for these" },
        new() { Id = "core", Name = "Golem Core", Icon = 216, Kind = "errand", Use = "The imp asks for these" },
        new() { Id = "fang", Name = "Beast Fang", Icon = 216, Kind = "errand", Use = "The imp asks for these" },
    };

    /// <summary>Which trophy comes from which family.</summary>
    private static readonly Dictionary<string, string> TrophyFamily = new()
    {
        ["undead"] = "bone", ["burning"] = "heart", ["crawling"] = "chitin", ["stone"] = "core", ["common"] = "fang",
    };

    public static readonly Potion[] DungeonItems =
    {
        new() { Id = "sigil", Name = "Pit Sigil", Icon = 216, Kind = "errand", Use = "The imp's mark, only worth anything down in the pit." },
    };

    public static Potion ErrandItemFor(string familyId)
        => ErrandItems.FirstOrDefault(i => i.Id == TrophyFamily.GetValueOrDefault(familyId, "fang"));

    /// <summary>Things the imp builds into the camp, once each.</summary>
    public static readonly CampUpgrade[] Upgrades =
    {
        new() { Id = "hearth", Name = "Warm hearth", Price = 300, About = "The camp mends the party twice as fast." },
        new() { Id = "chest", Name = "Deeper chest", Price = 450, About = "96 more slots in the stash chest." },
        new() { Id = "ledger", Name = "Imp's ledger", Price = 350, About = "Errands pay half as much again." },
    };

    /// <summary>How often an errand's trophy drops, and what he pays for the trouble.</summary>
    private static readonly (double Chance, string Label, int Pay)[] Tiers =
    {
        (1, "every one of them carries it", 30),
        (0.75, "three in four carry it", 45),
        (0.5, "one in two carries it", 70),
    };

    public readonly StashRecord[] Stash = new StashRecord[StashSlots];
    public ImpJob Job;
    public readonly List<string> BuiltUpgrades = new();
    public readonly Dictionary<int, Loadout> Loadouts = new();

    public int StashSlotCount() => HasUpgrade("chest") ? StashSlots : 144;

    public int StashCount() => Stash.Count(e => e != null);

    public bool HasUpgrade(string id) => BuiltUpgrades.Contains(id);

    /// <summary>
    /// Stashing keeps what an item is, not the live object: the engine recycles item slots. The
    /// chest has it before the bag loses it.
    /// </summary>
    public (int Slot, string Name, string Error) StashItem(int invSlot, Func<bool> commit = null)
    {
        int item = Items.Inventory[invSlot];
        if (item == 0) return (-1, null, "That slot is empty.");
        int free = Array.FindIndex(Stash, 0, StashSlotCount(), e => e == null);
        if (free < 0) return (-1, null, "The chest is full.");
        var it = Items.InPlay[item];
        string name = ItemName(item);
        Stash[free] = new StashRecord
        {
            Prop = it.ItemPropertyIndex,
            Frame = it.ShpCurFrameFlg & FrameMask,
            Flags = it.ShpCurFrameFlg & ~FrameMask,
            Wear = it.Wear,
        };
        if (commit != null && !commit())
        {
            Stash[free] = null;
            return (-1, null, StorageError);
        }
        Items.Inventory[invSlot] = 0;
        Items.Delete(item);
        Gui?.DrawInventory();
        return (free, name, null);
    }

    /// <summary>
    /// uiTakeFloorItem: the item leaves the floor, goes through the hand - which is what runs its
    /// pick-up script - and lands in the pack. Refused while a panel or a conversation is up.
    /// </summary>
    public bool TakeFloorItem(int item, int block)
    {
        if ((UpdateFlags & 3) != 0 || NeedSceneRestore || Gui == null || Gui.WeaponsDisabled || Gui.ItemInHand != 0) return false;
        if (!FloorItems().Any(f => f.Item == item && f.Block == block)) return false;
        Board.RemoveLevelItem(item, block);
        Gui.SetHandItem(item);
        if (Gui.ItemInHand == item)
        {
            int slot = Array.IndexOf(Items.Inventory, 0);
            if (slot >= 0)
            {
                Items.Inventory[slot] = item;
                Gui.SetHandItem(0);
                Gui.DrawInventory();
            }
        }
        InvalidateDrawOrder();
        return true;
    }

    /// <summary>uiFloorItems: what is lying here, and what is lying just ahead.</summary>
    public List<(int Item, string Name, int Block, bool Ahead)> FloorItems()
    {
        var out_ = new List<(int, string, int, bool)>();
        int here = Party.Block;
        int ahead = Party.CalcNewBlockPosition(here, Party.Direction);
        var blocks = new List<(int Block, bool Ahead)> { (here, false) };
        if (!Party.TestWallFlag(ahead, Party.Direction, 1) && !Party.TestWallFlag(here, Party.Direction, 1))
            blocks.Add((ahead, true));
        foreach (var (block, isAhead) in blocks)
        {
            int cur = Map.DrawObjects[block] != 0 ? Map.DrawObjects[block] : Map.AssignedObjects[block];
            int guard = 0;
            while (cur != 0 && guard++ < 64)
            {
                var obj = Board.Find(cur);
                if ((cur & 0x8000) == 0 && Items.InPlay[cur] != null && Items.InPlay[cur].ItemPropertyIndex != 0)
                    out_.Add((cur, ItemName(cur), block, isAhead));
                cur = obj.NextDrawObject != 0 ? obj.NextDrawObject : obj.NextAssignedObject;
            }
        }
        return out_;
    }

    /// <summary>itemName: what the game calls an item, or what the port calls one of its own.</summary>
    public string ItemName(int item)
    {
        if (item <= 0 || item >= Items.InPlay.Length) return "";
        var extra = ExtraItem(item);
        if (extra != null) return extra.Name;
        var prop = Items.Properties[Items.InPlay[item].ItemPropertyIndex];
        return prop == null ? "" : GameStrings.Get(prop.NameStringId, Gui?.LandsFile, Gui?.LevelLangFile) ?? "";
    }

    public int FreeItemSlots()
    {
        int n = 0;
        for (int i = 1; i < Items.InPlay.Length; i += 1) if (Items.InPlay[i].ItemPropertyIndex == 0) n += 1;
        return n;
    }

    public (int Item, int Slot, string Error) TakeFromStash(int stashSlot, Func<bool> commit = null)
    {
        var entry = stashSlot >= 0 && stashSlot < Stash.Length ? Stash[stashSlot] : null;
        if (entry == null) return (-1, -1, "Nothing there.");
        int free = Array.IndexOf(Items.Inventory, 0);
        if (free < 0) return (-1, -1, "Your inventory is full.");
        // makeItem's last resort is to destroy a floor item on another level to make room, which no
        // rollback can undo. Keep well clear of that.
        if (FreeItemSlots() < 8) return (-1, -1, "There is no room left in the world for another item.");
        int item = Items.Make(entry.Prop, entry.Frame, 0, Level);
        if (item == -1) return (-1, -1, "It will not come out.");
        var it = Items.InPlay[item];
        it.ShpCurFrameFlg = (it.ShpCurFrameFlg & FrameMask) | (entry.Flags & ~FrameMask);
        if (entry.Wear != 0) it.Wear = entry.Wear;
        Stash[stashSlot] = null;
        if (commit != null && !commit())
        {
            Stash[stashSlot] = entry;
            Items.Delete(item);
            return (-1, -1, StorageError);
        }
        Items.Inventory[free] = item;
        Gui?.DrawInventory();
        return (item, free, null);
    }

    // ---- the imp trader ----
    public (int Price, string Name, string Error) BuyReagent(string key)
    {
        if (!ReagentPrice.TryGetValue(key, out int price)) return (0, null, "He does not deal in that.");
        if (Items.Credits < price) return (0, null, $"The imp wants {price} credits.");
        Pouch[key] += 1;
        Items.TakeCredits(price);
        return (price, Reagents.First(r => r.Key == key).Name, null);
    }

    public (int Price, string Name, string Error) SellReagent(string key)
    {
        if (Pouch.GetValueOrDefault(key) == 0) return (0, null, "You have none.");
        int price = Math.Max(1, JsMath.RoundToInt(ReagentPrice[key] / 2.0));
        Pouch[key] -= 1;
        Items.GiveCredits(price);
        return (price, Reagents.First(r => r.Key == key).Name, null);
    }

    /// <summary>The imp buys anything with a price, at half the ladder value - he is not generous.</summary>
    public (int Price, string Reason) ImpOffer(int item)
    {
        var prop = Items.Properties[Items.InPlay[item].ItemPropertyIndex];
        if (prop == null) return (0, "He turns it over and hands it back.");
        var extra = ExtraItem(item);
        int basis = extra != null ? extra.Price : prop.UnkB;
        if (basis == 0 || (prop.Flags & 4) != 0) return (0, "The imp will not take that.");
        var prices = StaticData.Table("ItemPrices");
        int Ladder(int value)
        {
            for (int i = 0; i < 46; i += 1) if (prices[i] >= value) return prices[i];
            return 0;
        }
        return (Math.Max(1, Ladder(basis >> 1)), null);
    }

    public (int Price, string Name, string Error) SellToImp(int invSlot)
    {
        int item = Items.Inventory[invSlot];
        if (item == 0) return (0, null, "That slot is empty.");
        var (price, reason) = ImpOffer(item);
        if (price == 0) return (0, null, reason);
        string name = ItemName(item);
        Items.Inventory[invSlot] = 0;
        Items.Delete(item);
        Items.GiveCredits(price);
        Gui?.DrawInventory();
        return (price, name, null);
    }

    public (int Spell, int Slot, string Error) BuySpellFromImp(string id, int price)
    {
        var ids = Gui?.RegisterExtraSpells();
        if (ids == null || !ids.TryGetValue(id, out int spell)) return (-1, -1, "He cannot find that one today.");
        if (Gui.KnowsSpell(spell)) return (-1, -1, "The party already knows it.");
        if (Items.Credits < price) return (-1, -1, $"The imp wants {price} credits.");
        int slot = Gui.LearnSpell(spell);
        if (slot < 0) return (-1, -1, "There is no room left in the spell book.");
        Items.TakeCredits(price);
        return (spell, slot, null);
    }

    // ---- the imp's errands ----
    public ImpJob MakeJob(Func<double> random)
    {
        var killsByFamily = KillsByFamily();
        int done = Job?.Done ?? 0;
        int size = 3 + done / 2;   // he asks for a little more each time
        var family = Families[(int)Math.Floor(random() * Families.Length)];
        var trophy = ErrandItemFor(family.Id) ?? ErrandItemFor("common");
        if (random() < 0.7)
        {
            var tier = Tiers[Math.Min(Tiers.Length - 1, (int)Math.Floor(random() * (1 + done / 2)))];
            int need = size + 2;
            return new ImpJob
            {
                Kind = "collect", Family = family.Id, Item = trophy.Id, Name = trophy.Name,
                Need = need, Chance = tier.Chance, Hint = tier.Label, Pay = need * tier.Pay, Done = done,
            };
        }
        return new ImpJob
        {
            Kind = "hunt", Family = family.Id, Need = size,
            From = killsByFamily.GetValueOrDefault(family.Id), Pay = size * 25, Done = done,
        };
    }

    public string JobText(ImpJob job = null)
    {
        job ??= Job;
        if (job?.Kind == null) return "";
        var family = Families.FirstOrDefault(f => f.Id == job.Family);
        string who = family?.Name ?? job.Family;
        if (job.Kind == "collect") return $"Bring the imp {job.Need} × {job.Name} from {who}.";
        return $"Kill {job.Need} of {who}.";
    }

    /// <summary>The trophy's item property in this session, or -1 before the extra items exist.</summary>
    public int TrophyProperty(ImpJob job = null)
    {
        job ??= Job;
        if (job?.Item == null) return -1;
        foreach (var (prop, def) in ExtraItems) if (def.Id == job.Item) return prop;
        return -1;
    }

    public int CountOfProperty(int prop)
    {
        int n = 0;
        foreach (int item in Items.Inventory) if (item != 0 && Items.InPlay[item].ItemPropertyIndex == prop) n += 1;
        foreach (var c in Characters)
        {
            if (!c.Active) continue;
            foreach (int item in c.Items) if (item != 0 && Items.InPlay[item].ItemPropertyIndex == prop) n += 1;
        }
        return n;
    }

    public int JobProgress(ImpJob job = null)
    {
        job ??= Job;
        if (job?.Kind == null) return 0;
        if (job.Kind == "collect")
        {
            int prop = TrophyProperty(job);
            return Math.Min(job.Need, prop < 0 ? 0 : CountOfProperty(prop));
        }
        return Math.Min(job.Need, Math.Max(0, KillsByFamily().GetValueOrDefault(job.Family) - job.From));
    }

    public int JobPay(ImpJob job = null)
    {
        job ??= Job;
        return job == null ? 0 : JsMath.RoundToInt(job.Pay * (HasUpgrade("ledger") ? 1.5 : 1));
    }

    public ImpJob AcceptJob(Func<double> random)
    {
        Job = MakeJob(random);
        return Job;
    }

    /// <summary>Hand the errand back: the tally of finished ones is kept, nothing is paid.</summary>
    public int AbandonJob()
    {
        int done = Job?.Done ?? 0;
        Job = new ImpJob { Done = done };
        return done;
    }

    public (int Pay, string Error) ClaimJob()
    {
        var job = Job;
        if (job?.Kind == null) return (0, "He has nothing for you.");
        if (JobProgress(job) < job.Need) return (0, "Not yet done.");
        if (job.Kind == "collect")
        {
            int prop = TrophyProperty(job);
            int left = job.Need;
            for (int i = 0; i < Items.Inventory.Length && left > 0; i += 1)
            {
                int item = Items.Inventory[i];
                if (item == 0 || Items.InPlay[item].ItemPropertyIndex != prop) continue;
                Items.Inventory[i] = 0;
                Items.Delete(item);
                left -= 1;
            }
            Gui?.DrawInventory();
        }
        int pay = JobPay(job);
        Items.GiveCredits(pay);
        Job = new ImpJob { Done = job.Done + 1 };
        return (pay, null);
    }

    /// <summary>A kill for a collecting errand: rolls its chance and puts the trophy in the bag.</summary>
    public (string Name, int Slot, bool Full) RollErrandDrop(string monsterFamilyId, Func<double> random)
    {
        var job = Job;
        if (job?.Kind != "collect" || job.Family != monsterFamilyId) return (null, -1, false);
        if (JobProgress(job) >= job.Need) return (null, -1, false);   // he asked for so many
        if (random() >= job.Chance) return (null, -1, false);
        int prop = TrophyProperty(job);
        if (prop < 0) return (null, -1, false);
        int slot = Array.IndexOf(Items.Inventory, 0);
        if (slot < 0) return (job.Name, -1, true);
        int item = Items.Make(prop, 0, 0, Level);
        if (item == -1) return (null, -1, false);
        Items.Inventory[slot] = item;
        Gui?.DrawInventory();
        return (job.Name, slot, false);
    }

    // ---- camp upgrades ----
    public (string Name, string Error) BuyUpgrade(CampUpgrade def, Func<bool> commit = null)
    {
        if (HasUpgrade(def.Id)) return (null, "It is already built.");
        if (Items.Credits < def.Price) return (null, $"The imp wants {def.Price} credits.");
        BuiltUpgrades.Add(def.Id);
        if (commit != null && !commit())
        {
            BuiltUpgrades.Remove(def.Id);
            return (null, "The imp cannot write it down: this browser refused to store it.");
        }
        Items.TakeCredits(def.Price);
        return (def.Name, null);
    }

    // ---- loadouts ----
    public Loadout ReadLoadout(int c) => Loadouts.GetValueOrDefault(c);

    public (int Count, string Name, string Error) SaveLoadout(int c)
    {
        var ch = Characters[c];
        if (!ch.Active) return (0, null, "Nobody there.");
        var worn = new List<(int, int, int)>();
        for (int slot = 0; slot < 11; slot += 1)
        {
            int item = ch.Items[slot];
            if (item == 0) continue;
            var it = Items.InPlay[item];
            worn.Add((slot, it.ItemPropertyIndex, it.ShpCurFrameFlg & FrameMask));
        }
        if (worn.Count == 0) return (0, null, $"{ch.Name} wears nothing to remember.");
        Loadouts[c] = new Loadout { Name = ch.Name, Worn = worn };
        return (worn.Count, ch.Name, null);
    }
}
