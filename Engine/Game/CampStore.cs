// src/game/camp-store.mjs
// The camp's chest and the imp who trades at it.
//
// A port addition, and until now it lived entirely in the browser: the chest, the imp's prices, his
// errands, the upgrades he builds and the sets a hero wears. The rules are here so that every build
// keeps the same chest and pays the same errand; where the chest is *stored* between sessions stays
// the host's business (uiStoreSave / uiStoreLoad).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>What the ui* calls of the camp, the chest, the imp and the cauldron return: the JS returns
    /// an object literal with some of these keys ({ error } or the result's own fields).</summary>
    public sealed class UiResult
    {
        public string error;
        public int slot, item, price, spell, pay, done, count, worn, missing;
        public string name;
        public bool full, refused;
        public string reason;
    }

    /// <summary>camp-store.mjs store.stash entries</summary>
    public sealed class StashEntry
    {
        public int v, prop, frame, flags, wear;
    }

    /// <summary>camp-store.mjs uiMakeJob: the imp's errand (store.job). { done } alone between errands.</summary>
    public sealed class ImpJob
    {
        public string kind, family, item, name;
        /// <summary>Unity build: the kinds of that family the party can find now (named in the errand)</summary>
        public string kinds;
        public int need;
        public double chance;
        public string hint;
        public int pay, done, from;
        /// <summary>an errand from an older save ("fetch")</summary>
        public string key;
    }

    /// <summary>camp-store.mjs UPGRADES</summary>
    public sealed class UpgradeDef
    {
        public string id, name;
        public int price;
        public string about;
    }

    /// <summary>camp-store.mjs TIERS</summary>
    public sealed class ErrandTier
    {
        public double chance;
        public string label;
        public int pay;
    }

    /// <summary>camp-store.mjs uiSaveLoadout: store.loadouts[c]</summary>
    public sealed class Loadout
    {
        public string name;
        public List<LoadoutPiece> worn;
    }

    public sealed class LoadoutPiece
    {
        public int slot, prop, frame;
    }

    /// <summary>camp-store.mjs initCampStore: this.store</summary>
    public sealed class CampStoreState
    {
        public StashEntry[] stash;
        public ImpJob job;
        public List<string> upgrades;
        public Dictionary<int, Loadout> loadouts;
    }

    public sealed partial class LandsOfLore
    {
        public const int STASH_SLOTS = 240;   // the chest at its largest; without the upgrade the first 144
        const int FRAME_MASK = 0x1fff;
        const int RECORD_VERSION = 2;

        // What a host says when it cannot keep the chest; the engine puts everything back and reports it.
        const string STORAGE_ERROR = "The chest will not hold it: this browser refused to store it (no space, or storage is blocked).";

        // What the imp charges for a reagent, and what he pays when you sell one back.
        public static readonly Dictionary<string, int> REAGENT_PRICE = new Dictionary<string, int> { ["moss"] = 12, ["sap"] = 14, ["bone"] = 10, ["bile"] = 16, ["shard"] = 22, ["ember"] = 24 };

        // Trophies: what a creature leaves for the imp's collecting errands. `icon` is the original item whose sprite
        // stands for it in the hand and on the floor (Unity build: a look-alike - a bone, a bloodstone, a horn - not
        // the salve flask they all borrowed). Real items in the bag rather
        // than pouch reagents - reagents are for brewing, these are for him.
        public static readonly ExtraItemDef[] ERRAND_ITEMS =
        {
            new ExtraItemDef { id = "bone", family = "undead", name = "Skeleton Bone", icon = 250, kind = "errand", price = 0, use = "The imp asks for these" },
            new ExtraItemDef { id = "heart", family = "burning", name = "Charred Heart", icon = 273, kind = "errand", price = 0, use = "The imp asks for these" },
            new ExtraItemDef { id = "chitin", family = "crawling", name = "Chitin Plate", icon = 131, kind = "errand", price = 0, use = "The imp asks for these" },
            new ExtraItemDef { id = "core", family = "stone", name = "Golem Core", icon = 98, kind = "errand", price = 0, use = "The imp asks for these" },
            new ExtraItemDef { id = "fang", family = "common", name = "Beast Fang", icon = 172, kind = "errand", price = 0, use = "The imp asks for these" },
        };

        public static readonly ExtraItemDef[] DUNGEON_ITEMS =
        {
            new ExtraItemDef { id = "sigil", name = "Pit Sigil", icon = 224, kind = "errand", price = 0, use = "The imp's mark. On a pit floor that asks for them, holding enough opens the gate to the way down. Worth nothing outside the pit." },
            new ExtraItemDef { id = "pitkey", name = "Gate Key", icon = 171, kind = "errand", price = 0, use = "Opens the locked gate of the pit floor it was found on: hold it and click the gate. Worth nothing outside the pit." },
            new ExtraItemDef { id = "perfectheal", name = "Perfect Healing Potion", icon = 217, art = "src/assets/potion-heal.svg", kind = "errand", price = 0, use = "A quest item: it can save a life that no herb could. Keep it for someone who needs it." },
        };

        public static ExtraItemDef errandItemFor(string familyId)
        {
            return ERRAND_ITEMS.FirstOrDefault(i => i.family == familyId);
        }

        // Things the imp builds into the camp, once each.
        public static readonly UpgradeDef[] UPGRADES =
        {
            new UpgradeDef { id = "hearth", name = "Warm hearth", price = 300, about = "The camp mends the party twice as fast." },
            new UpgradeDef { id = "chest", name = "Deeper chest", price = 450, about = "96 more slots in the stash chest." },
            new UpgradeDef { id = "ledger", name = "Imp's ledger", price = 350, about = "Errands pay half as much again." },
        };

        // How often an errand's trophy drops, and what he pays for the trouble.
        static readonly ErrandTier[] TIERS =
        {
            new ErrandTier { chance = 1, label = "every one of them carries it", pay = 30 },
            new ErrandTier { chance = 0.75, label = "three in four carry it", pay = 45 },
            new ErrandTier { chance = 0.5, label = "one in two carries it", pay = 70 },
        };

        /// <summary>JSON.parse(JSON.stringify(x)) and back: public fields, JS key names, nulls left out.</summary>
        internal static readonly JsonSerializerOptions Store_json = new JsonSerializerOptions { IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        /// <summary>JS Number(v) on a JSON value (undefined -> NaN).</summary>
        internal static double Store_Number(JsonNode v)
        {
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue(out double d)) return d;
                if (jv.TryGetValue(out bool b)) return b ? 1 : 0;
                if (jv.TryGetValue(out string s))
                {
                    s = s.Trim();
                    if (s.Length == 0) return 0;
                    return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ds) ? ds : double.NaN;
                }
            }
            return double.NaN;
        }

        /// <summary>Math.trunc(x) || 0</summary>
        internal static int Store_TruncOr0(double x) => double.IsNaN(x) || double.IsInfinity(x) ? 0 : (int)Math.Truncate(x);

        // ---- CampStoreMixin ----
        public CampStoreState store;

        public CampStoreState initCampStore()
        {
            store = new CampStoreState
            {
                stash = new StashEntry[STASH_SLOTS],
                job = null,
                upgrades = new List<string>(),
                loadouts = new Dictionary<int, Loadout>(),
            };
            return store;
        }

        // The host hands back what it stored; anything it cannot make sense of is dropped rather than
        // guessed at, because a half-read chest is worse than an empty one.
        public CampStoreState uiStoreLoad(JsonObject saved)
        {
            initCampStore();
            if (saved == null) return store;
            if (saved["stash"] is JsonArray stash)
            {
                for (int i = 0; i < Math.Min(STASH_SLOTS, stash.Count); i += 1)
                {
                    var e = stash[i] as JsonObject;
                    if (e == null) continue;
                    // Number.isInteger(e.prop) && e.prop > 0
                    if (!(e["prop"] is JsonValue pv) || !pv.TryGetValue(out double prop) || prop != Math.Floor(prop) || double.IsInfinity(prop) || prop <= 0) continue;
                    store.stash[i] = new StashEntry
                    {
                        v = RECORD_VERSION, prop = (int)prop, frame = Store_TruncOr0(Store_Number(e["frame"])),
                        flags = Store_TruncOr0(Store_Number(e["flags"])), wear = Math.Max(0, Store_TruncOr0(Store_Number(e["wear"]))),
                    };
                }
            }
            if (saved["job"] is JsonObject job) store.job = job.Deserialize<ImpJob>(Store_json);
            if (saved["upgrades"] is JsonArray upgrades) store.upgrades = upgrades.Where(id => id is JsonValue v && v.TryGetValue(out string s) && UPGRADES.Any(u => u.id == s)).Select(id => (string)id).ToList();
            if (saved["loadouts"] is JsonObject loadouts) store.loadouts = loadouts.Deserialize<Dictionary<int, Loadout>>(Store_json);
            return store;
        }

        public JsonNode uiStoreSave()
        {
            return JsonSerializer.SerializeToNode(store, Store_json);
        }

        // ---- the chest ----
        public int uiStashSlots()
        {
            return uiHasUpgrade("chest") ? STASH_SLOTS : 144;
        }

        public int uiStashCount()
        {
            return store.stash.Count(e => e != null);
        }

        // Stashing keeps what an item is, not the live object: the engine recycles item slots, and a
        // chest that outlives a save has to survive that. It is recreated on withdrawal, wear and all.
        public UiResult uiStashItem(int invSlot, Func<bool> commit = null)
        {
            int item = inventory[invSlot];
            if (item == 0) return new UiResult { error = "That slot is empty." };
            int free = Array.IndexOf(Js.Slice(store.stash, 0, uiStashSlots()), null);
            if (free < 0) return new UiResult { error = "The chest is full." };
            var it = itemsInPlay[item];
            string name = itemName(item);   // taken before the item is deleted, or the message names a ghost
            store.stash[free] = new StashEntry
            {
                v = RECORD_VERSION,
                prop = it.itemPropertyIndex,
                frame = it.shpCurFrame_flg & FRAME_MASK,
                flags = it.shpCurFrame_flg & ~FRAME_MASK,
                wear = it.wear,
            };
            // The chest has it before the bag loses it: a host that cannot store the chest says so here,
            // and nothing has happened yet.
            if (commit != null && commit() == false)
            {
                store.stash[free] = null;
                return new UiResult { error = STORAGE_ERROR };
            }
            inventory[invSlot] = 0;
            deleteItem(item);
            gui_drawInventory();
            return new UiResult { slot = free, name = name };
        }

        public int uiFreeItemSlots()
        {
            int n = 0;
            for (int i = 1; i < itemsInPlay.Count(); i += 1) if (itemsInPlay[i].itemPropertyIndex == 0) n += 1;
            return n;
        }

        public UiResult uiTakeFromStash(int stashSlot, Func<bool> commit = null)
        {
            var entry = store.stash[stashSlot];
            if (entry == null) return new UiResult { error = "Nothing there." };
            int free = Array.IndexOf(inventory, (ushort)0);
            if (free < 0) return new UiResult { error = "Your inventory is full." };
            // makeItem's last resort is to destroy a floor item on another level to make room, which no
            // rollback can undo. Keep well clear of that.
            if (uiFreeItemSlots() < 8) return new UiResult { error = "There is no room left in the world for another item." };
            int item = -1;
            try { item = makeItem(entry.prop, entry.frame, 0); } catch (QuitException) { throw; } catch (Exception) { item = -1; }
            if (item == -1) return new UiResult { error = "It will not come out." };
            var it = itemsInPlay[item];
            if (it != null)
            {
                it.shpCurFrame_flg = (it.shpCurFrame_flg & FRAME_MASK) | (entry.flags & ~FRAME_MASK);
                if (entry.wear != 0) it.wear = entry.wear;
            }
            store.stash[stashSlot] = null;
            // And the other way round: the item exists before the chest forgets it, so a refused write
            // destroys the new one rather than leaving two.
            if (commit != null && commit() == false)
            {
                store.stash[stashSlot] = entry;
                deleteItem(item);
                return new UiResult { error = STORAGE_ERROR };
            }
            inventory[free] = (ushort)item;
            gui_drawInventory();
            return new UiResult { item = item, slot = free };
        }

        // ---- the imp trader ----
        public UiResult uiBuyReagent(string key)
        {
            int price = REAGENT_PRICE.TryGetValue(key, out int p) ? p : 0;
            if (price == 0) return new UiResult { error = "He does not deal in that." };
            if (credits < price) return new UiResult { error = $"The imp wants {price} crowns." };
            pouch[key] += 1;
            queueAsync(() => takeCredits(price, 1));
            return new UiResult { price = price, name = REAGENTS[key].name };
        }

        public UiResult uiSellReagent(string key)
        {
            if (!pouch.TryGetValue(key, out int have) || have == 0) return new UiResult { error = "You have none." };
            int price = Math.Max(1, Js.Round(REAGENT_PRICE[key] / 2.0));
            pouch[key] -= 1;
            queueAsync(() => giveCredits(price, 1));
            return new UiResult { price = price, name = REAGENTS[key].name };
        }

        // The imp buys anything with a price, at half the ladder value - he is not generous.
        public UiResult uiImpOffer(int item)
        {
            int propIndex = itemsInPlay[item].itemPropertyIndex;
            var prop = propIndex < itemProperties.Count() ? itemProperties[propIndex] : null;
            if (prop == null) return new UiResult { refused = true, reason = "He turns it over and hands it back." };
            var extra = uiExtraItem(item);
            int @base = extra != null ? extra.price : prop.unkB;
            if (@base == 0 || (prop.flags & 4) != 0) return new UiResult { refused = true, reason = "The imp will not take that." };
            Func<int, int> ladder = (value) => { for (int i = 0; i < 46; i += 1) if (@static.ItemPrices[i] >= value) return @static.ItemPrices[i]; return 0; };
            return new UiResult { price = Math.Max(1, ladder(@base >> 1)) };
        }

        public UiResult uiSellToImp(int invSlot)
        {
            int item = inventory[invSlot];
            if (item == 0) return new UiResult { error = "That slot is empty." };
            var offer = uiImpOffer(item);
            if (offer.refused) return new UiResult { error = offer.reason };
            string name = itemName(item);
            inventory[invSlot] = 0;
            deleteItem(item);
            queueAsync(() => giveCredits(offer.price, 1));
            gui_drawInventory();
            return new UiResult { price = offer.price, name = name };
        }

        public UiResult uiBuySpellFromImp(ExtraSpellDef def)
        {
            var ids = uiRegisterExtraSpells();
            if (!ids.TryGetValue(def.id, out int spell)) return new UiResult { error = "He cannot find that one today." };
            if (uiKnowsSpell(spell)) return new UiResult { error = "The party already knows it." };
            if (credits < def.price) return new UiResult { error = $"The imp wants {def.price} crowns." };
            int slot = uiLearnSpell(spell);
            if (slot < 0) return new UiResult { error = "There is no room left in the spell book." };
            queueAsync(() => takeCredits(def.price, 1));
            return new UiResult { spell = spell, slot = slot, price = def.price };
        }

        // ---- the imp's errands ----
        // He always has exactly one going: fetch him trophies, or thin out a kind of creature. The kill
        // count is measured against a snapshot taken when the job was accepted, so old kills do not pay.
        /// <summary>Unity build: the kinds the party has fought (the host's bestiary), for an errand taken where no
        /// monster lives.</summary>
        public List<string> errandNearby;

        /// <summary>A monster kind's name: its sprite file (ORC.SHP -> Orc), as monsterName gives it.</summary>
        public string uiKindName(int type)
        {
            var p = type >= 0 && type < monsterProperties.Count() ? monsterProperties[type] : null;
            if (p == null || p.hitPoints == 0) return "";
            string file = monsterShapeNames != null ? monsterShapeNames.ElementAtOrDefault(p.shapeIndex) : "";
            if (string.IsNullOrEmpty(file)) return "";
            string b = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(file, @"\.SHP$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase), @"\d+$", "").ToLowerInvariant();
            return b.Length == 0 ? "" : char.ToUpperInvariant(b[0]) + b.Substring(1);
        }

        public ImpJob uiMakeJob(Func<double> random = null)
        {
            random = random ?? (() => _entropy.NextDouble());   // Math.random
            var killsByFamily = uiCraftKillsByFamily();
            int done = store.job != null ? store.job.done : 0;
            int size = 3 + Js.FloorDiv(done, 2);   // he asks for a little more each time
            // Unity build: only creatures the party can find now - this level's own, else the kinds it has already
            // fought (errandNearby, from the host's bestiary) - never a family that lives at the far end of the game
            var nearby = levelMonsterTypes.Select(t => uiKindName(t)).Where(nm => !string.IsNullOrEmpty(nm)).Distinct().ToList();
            if (nearby.Count == 0 && errandNearby != null) nearby = errandNearby.Distinct().ToList();
            var families = nearby.Select(nm => familyOf(nm)).Distinct().ToList();
            var family = families.Count != 0 ? families[Js.Floor(random() * families.Count)] : FAMILIES[Js.Floor(random() * FAMILIES.Length)];
            string kinds = string.Join(", ", nearby.Where(nm => familyOf(nm).id == family.id).Take(3));
            var trophy = errandItemFor(family.id) ?? errandItemFor("common");
            if (random() < 0.7)
            {
                int ti = Math.Min(TIERS.Length - 1, Js.Floor(random() * (1 + Js.FloorDiv(done, 2))));
                var tier = (ti >= 0 && ti < TIERS.Length ? TIERS[ti] : null) ?? TIERS[0];
                int needC = size + 2;
                return new ImpJob { kind = "collect", family = family.id, kinds = kinds, item = trophy.id, name = trophy.name, need = needC, chance = tier.chance, hint = tier.label, pay = needC * tier.pay, done = done };
            }
            int need = size;
            return new ImpJob { kind = "hunt", family = family.id, kinds = kinds, need = need, from = killsByFamily.TryGetValue(family.id, out int k) ? k : 0, pay = need * 25, done = done };
        }

        // `job = this.store.job` defaults: a null argument means "the current errand".
        public string uiJobText(ImpJob job = null)
        {
            job = job ?? store.job;
            if (job == null || string.IsNullOrEmpty(job.kind)) return "";
            var family = FAMILIES.FirstOrDefault(f => f.id == job.family);
            string who = (family != null ? family.name : job.family) + (!string.IsNullOrEmpty(job.kinds) ? $" ({job.kinds})" : "");
            if (job.kind == "collect") return $"Bring the imp {job.need} × {job.name} from {who}.";
            if (job.kind == "fetch") return $"Bring the imp {job.need} × {REAGENTS[job.key].name.ToLowerInvariant()}.";   // an errand from an older save
            return $"Kill {job.need} of {who}.";
        }

        // The trophy's item property in this session, or undefined before the extra items are registered.
        public int? uiTrophyProperty(ImpJob job = null)
        {
            job = job ?? store.job;
            if (job == null || string.IsNullOrEmpty(job.item) || extraItems == null) return null;
            foreach (var e in extraItems) if (e.Value.id == job.item) return e.Key;
            return null;
        }

        public int uiJobProgress(ImpJob job = null)
        {
            job = job ?? store.job;
            if (job == null || string.IsNullOrEmpty(job.kind)) return 0;
            if (job.kind == "collect")
            {
                int? prop = uiTrophyProperty(job);
                return Math.Min(job.need, prop == null ? 0 : uiCountOfProperty(prop.Value));
            }
            if (job.kind == "fetch") return Math.Min(job.need, pouch.TryGetValue(job.key, out int p) ? p : 0);
            return Math.Min(job.need, Math.Max(0, (uiCraftKillsByFamily().TryGetValue(job.family, out int k) ? k : 0) - job.from));
        }

        public int uiJobPay(ImpJob job = null)
        {
            job = job ?? store.job;
            if (job == null || string.IsNullOrEmpty(job.kind)) return 0;
            return Js.Round(job.pay * (uiHasUpgrade("ledger") ? 1.5 : 1));
        }

        public ImpJob uiAcceptJob(Func<double> random = null)
        {
            store.job = uiMakeJob(random);
            return store.job;
        }

        // Hand the errand back. The tally of finished ones is kept; nothing is paid.
        public UiResult uiAbandonJob()
        {
            int done = store.job != null ? store.job.done : 0;
            store.job = new ImpJob { done = done };
            return new UiResult { done = done };
        }

        public UiResult uiClaimJob()
        {
            var job = store.job;
            if (job == null || string.IsNullOrEmpty(job.kind)) return new UiResult { error = "He has nothing for you." };
            if (uiJobProgress(job) < job.need) return new UiResult { error = "Not yet done." };
            if (job.kind == "collect")
            {
                int? prop = uiTrophyProperty(job);
                int left = job.need;
                for (int i = 0; i < inventory.Length && left > 0; i += 1)
                {
                    int item = inventory[i];
                    if (item == 0 || itemsInPlay[item].itemPropertyIndex != prop) continue;
                    inventory[i] = 0;
                    deleteItem(item);
                    left -= 1;
                }
                gui_drawInventory();
            }
            else if (job.kind == "fetch")
            {
                pouch[job.key] -= job.need;
            }
            int pay = uiJobPay(job);
            queueAsync(() => giveCredits(pay, 1));
            store.job = new ImpJob { done = job.done + 1 };
            return new UiResult { pay = pay };
        }

        // A kill for a collecting errand: rolls its chance and puts the trophy in the bag.
        public UiResult uiRollErrandDrop(string monsterFamilyId, Func<double> random = null)
        {
            random = random ?? (() => _entropy.NextDouble());   // Math.random
            var job = store.job;
            if (job == null || job.kind != "collect" || job.family != monsterFamilyId) return null;
            if (uiJobProgress(job) >= job.need) return null;   // he asked for so many
            if (random() >= job.chance) return null;
            int? prop = uiTrophyProperty(job);
            if (prop == null) return null;
            int slot = Array.IndexOf(inventory, (ushort)0);
            if (slot < 0) return new UiResult { full = true, name = job.name };
            int item = -1;
            try { item = makeItem(prop.Value, 0, 0); } catch (QuitException) { throw; } catch (Exception) { return null; }   // the table is full: no trophy today
            if (item == -1) return null;
            inventory[slot] = (ushort)item;
            gui_drawInventory();
            return new UiResult { name = job.name, slot = slot };
        }

        // ---- camp upgrades ----
        public bool uiHasUpgrade(string id)
        {
            return store.upgrades.Contains(id);
        }

        public UiResult uiBuyUpgrade(UpgradeDef def)
        {
            if (uiHasUpgrade(def.id)) return new UiResult { error = "It is already built." };
            if (credits < def.price) return new UiResult { error = $"The imp wants {def.price} crowns." };
            store.upgrades.Add(def.id);
            queueAsync(() => takeCredits(def.price, 1));
            return new UiResult { name = def.name };
        }

        // ---- loadouts ----
        // What a hero wore, by item kind rather than by item: the engine recycles item slots, so a set
        // that outlives a save has to be rebuilt out of the chest and the pack.
        public Loadout uiReadLoadout(int c)
        {
            return store.loadouts.TryGetValue(c, out var l) ? l : null;
        }

        public UiResult uiSaveLoadout(int c)
        {
            var ch = c >= 0 && c < characters.Length ? characters[c] : null;
            if (ch == null || (ch.flags & 1) == 0) return new UiResult { error = "Nobody there." };
            var worn = new List<LoadoutPiece>();
            for (int slot = 0; slot < 11; slot += 1)
            {
                int item = ch.items[slot];
                if (item == 0) continue;
                var it = itemsInPlay[item];
                worn.Add(new LoadoutPiece { slot = slot, prop = it.itemPropertyIndex, frame = it.shpCurFrame_flg & FRAME_MASK });
            }
            if (worn.Count == 0) return new UiResult { error = $"{ch.name} wears nothing to remember." };
            store.loadouts[c] = new Loadout { name = ch.name, worn = worn };
            return new UiResult { count = worn.Count, name = ch.name };
        }

        // Puts the remembered set back on: what is already worn stays, the rest comes out of the pack,
        // and out of the chest when it is not in the pack.
        public async Task<UiResult> uiWearLoadout(int c)
        {
            var set = uiReadLoadout(c);
            var ch = c >= 0 && c < characters.Length ? characters[c] : null;
            if (set == null || ch == null || (ch.flags & 1) == 0) return new UiResult { error = "Nothing remembered for this hero." };
            int worn = 0;
            int missing = 0;
            foreach (var want in set.worn)
            {
                int cur = ch.items[want.slot];
                if (cur != 0 && itemsInPlay[cur].itemPropertyIndex == want.prop) continue;
                int inv = Array.FindIndex(inventory, item => item != 0 && itemsInPlay[item].itemPropertyIndex == want.prop);
                if (inv < 0)
                {
                    int at = Array.FindIndex(store.stash, e => e != null && e.prop == want.prop);
                    if (at < 0) { missing += 1; continue; }
                    var @out = uiTakeFromStash(at);
                    if (@out.error != null) { missing += 1; continue; }
                    inv = @out.slot;
                }
                if (await uiDropInventoryOn(inv, c, want.slot) != 0) worn += 1;
                else missing += 1;
            }
            return new UiResult { worn = worn, missing = missing, name = ch.name };
        }

        // What a creature of this kind is worth to the imp's errand, for the panels that say so.
        public string uiErrandFamilyOf(string monsterName)
        {
            return familyOf(monsterName).id;
        }
    }
}
