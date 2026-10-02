// src/platform/camp-store.mjs. C# 9.
// The camp's chest and the imp, as the browser stores them.
//
// The rules - what the chest holds, what the imp pays, what his errands ask for, what his upgrades
// do - live in the engine (src/game/camp-store.mjs) so that every build keeps the same camp. What
// is here is where that state lives between sessions, and the thin calls the page's panels were
// already written against.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lol;

namespace LolHost
{
    /// <summary>main.mjs's `pouchApi = { read: readPouch, write: writePouch }`.</summary>
    public sealed class PouchApi
    {
        public Func<Dictionary<string, int>> read;
        public Func<Dictionary<string, int>, bool> write;
    }

    public static class CampStoreUi
    {
        // export { STASH_SLOTS, REAGENT_PRICE, UPGRADES, ERRAND_ITEMS, DUNGEON_ITEMS, errandItemFor } (the engine's)
        public const int STASH_SLOTS = LandsOfLore.STASH_SLOTS;
        public static Dictionary<string, int> REAGENT_PRICE => LandsOfLore.REAGENT_PRICE;
        public static UpgradeDef[] UPGRADES => LandsOfLore.UPGRADES;
        public static ExtraItemDef[] ERRAND_ITEMS => LandsOfLore.ERRAND_ITEMS;
        public static ExtraItemDef[] DUNGEON_ITEMS => LandsOfLore.DUNGEON_ITEMS;
        public static ExtraItemDef errandItemFor(string familyId) => LandsOfLore.errandItemFor(familyId);

        const string STASH_KEY = "lol.stash";
        const string JOB_KEY = "lol.impjob";
        const string UPGRADE_KEY = "lol.campup";
        const string LOADOUT_KEY = "lol.loadouts";

        static readonly Random rng = new Random();

        // Everything the browser kept, handed to the engine in one call.
        public static CampStoreState loadStore(LandsOfLore engine)
        {
            return engine.uiStoreLoad(new JsonObject
            {
                ["stash"] = Store.readJson(STASH_KEY, null),
                ["job"] = Store.readJson(JOB_KEY, null),
                ["upgrades"] = Store.readJson(UPGRADE_KEY, null),
                ["loadouts"] = Store.readJson(LOADOUT_KEY, null),
            });
        }

        static bool writeStash(LandsOfLore engine)
        {
            return Store.writeJson(STASH_KEY, engine.store.stash);
        }

        static bool writeJob(LandsOfLore engine)
        {
            var job = engine.store.job;
            return job != null ? Store.writeJson(JOB_KEY, JobJson(job)) : Store.removeKey(JOB_KEY);
        }

        // the errand as the JS object literal has it (camp-store.mjs): only its kind's fields, in that order
        static JsonObject JobJson(ImpJob job)
        {
            switch (job.kind)
            {
                case "collect": return new JsonObject { ["kind"] = job.kind, ["family"] = job.family, ["item"] = job.item, ["name"] = job.name, ["need"] = job.need, ["chance"] = job.chance, ["hint"] = job.hint, ["pay"] = job.pay, ["done"] = job.done };
                case "hunt": return new JsonObject { ["kind"] = job.kind, ["family"] = job.family, ["need"] = job.need, ["from"] = job.from, ["pay"] = job.pay, ["done"] = job.done };
                case "fetch": return new JsonObject { ["kind"] = job.kind, ["key"] = job.key, ["need"] = job.need, ["pay"] = job.pay, ["done"] = job.done };   // an older save's errand, kept
                default: return new JsonObject { ["done"] = job.done };
            }
        }

        public static StashEntry[] readStash(LandsOfLore engine)
        {
            return engine != null ? engine.store.stash : new StashEntry[STASH_SLOTS];
        }

        public static int stashCount(LandsOfLore engine)
        {
            return engine != null ? engine.uiStashCount() : 0;
        }

        public static int stashSlots(LandsOfLore engine)
        {
            return engine != null ? engine.uiStashSlots() : 144;
        }

        // The chest is written before the bag gives anything up, and a failed write is reported: an
        // unreported one is what destroyed items in 0.1.0.
        public static UiResult stashItem(LandsOfLore engine, int invSlot)
        {
            return engine.uiStashItem(invSlot, () => writeStash(engine));
        }

        public static UiResult takeFromStash(LandsOfLore engine, int stashSlot)
        {
            return engine.uiTakeFromStash(stashSlot, () => writeStash(engine));
        }

        // ---- the imp trader ----
        public static UiResult buyReagent(LandsOfLore engine, string key, PouchApi pouchApi)
        {
            var result = engine.uiBuyReagent(key);
            if (string.IsNullOrEmpty(result.error)) pouchApi.write(engine.uiCraftPouch());
            return result;
        }

        public static UiResult sellReagent(LandsOfLore engine, string key, PouchApi pouchApi)
        {
            var result = engine.uiSellReagent(key);
            if (string.IsNullOrEmpty(result.error)) pouchApi.write(engine.uiCraftPouch());
            return result;
        }

        public static UiResult impOffer(LandsOfLore engine, int item)
        {
            return engine.uiImpOffer(item);
        }

        public static UiResult sellToImp(LandsOfLore engine, int invSlot)
        {
            return engine.uiSellToImp(invSlot);
        }

        public static UiResult buySpell(LandsOfLore engine, ExtraSpellDef def)
        {
            return engine.uiBuySpellFromImp(def);
        }

        // ---- the imp's errands ----
        public static ImpJob readJob(LandsOfLore engine)
        {
            if (engine != null) return engine.store.job;
            // readJson(JOB_KEY, null), as the engine's ImpJob
            try { return Store.readJson(JOB_KEY, null) is JsonObject o ? System.Text.Json.JsonSerializer.Deserialize<ImpJob>(o, Store.json) : null; }
            catch (Exception) { return null; }
        }

        public static string jobText(ImpJob job)
        {
            return job != null && !string.IsNullOrEmpty(job.kind) ? (job.kind == "collect"
                ? $"Bring the imp {job.need} × {job.name}."
                : $"Kill {job.need} of them.") : "";
        }

        public static int jobProgress(LandsOfLore engine, ImpJob job)
        {
            return engine != null ? engine.uiJobProgress(job) : 0;
        }

        public static int jobPay(LandsOfLore engine, ImpJob job)
        {
            return engine != null ? engine.uiJobPay(job) : 0;
        }

        public static ImpJob acceptJob(LandsOfLore engine)
        {
            var job = engine.uiAcceptJob();
            writeJob(engine);
            return job;
        }

        public static UiResult abandonJob(LandsOfLore engine)
        {
            var result = engine.uiAbandonJob();
            writeJob(engine);
            return result;
        }

        public static UiResult claimJob(LandsOfLore engine)
        {
            var result = engine.uiClaimJob();
            if (string.IsNullOrEmpty(result.error)) writeJob(engine);
            return result;
        }

        /// <summary>`random` defaults to Math.random.</summary>
        public static UiResult rollErrandDrop(LandsOfLore engine, ImpJob job, string monsterFamilyId, Func<double> random = null)
        {
            return engine.uiRollErrandDrop(monsterFamilyId, random ?? rng.NextDouble);
        }

        // ---- camp upgrades ----
        public static List<string> readUpgrades(LandsOfLore engine)
        {
            return engine != null ? engine.store.upgrades.ToList() : new List<string>();
        }

        public static bool hasUpgrade(LandsOfLore engine, string id)
        {
            return engine != null && engine.uiHasUpgrade(id);
        }

        public static UiResult buyUpgrade(LandsOfLore engine, UpgradeDef def)
        {
            var result = engine.uiBuyUpgrade(def);
            if (!string.IsNullOrEmpty(result.error)) return result;
            if (!Store.writeJson(UPGRADE_KEY, engine.store.upgrades))
            {
                engine.store.upgrades = engine.store.upgrades.Where(id => id != def.id).ToList();
                return new UiResult { error = "The imp cannot write it down: this browser refused to store it." };
            }
            return result;
        }

        // ---- loadouts ----
        public static Loadout readLoadout(LandsOfLore engine, int c)
        {
            return engine != null ? engine.uiReadLoadout(c) : null;
        }

        public static UiResult saveLoadout(LandsOfLore engine, int c)
        {
            var result = engine.uiSaveLoadout(c);
            if (!string.IsNullOrEmpty(result.error)) return result;
            if (!Store.writeJson(LOADOUT_KEY, engine.store.loadouts))
            {
                engine.store.loadouts.Remove(c);
                return new UiResult { error = "It cannot be written down: this browser refused to store it." };
            }
            return result;
        }

        public static async Task<UiResult> wearLoadout(LandsOfLore engine, int c)
        {
            var result = await engine.uiWearLoadout(c);
            writeStash(engine);
            return result;
        }
    }
}
