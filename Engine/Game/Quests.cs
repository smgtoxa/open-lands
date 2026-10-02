// src/game/quests.mjs: quest log data. The game has no quest records of its own: objectives are
// implied by dialogue and tracked through game flags, so this table names them and says when each one
// appears and when it is done. Flag numbers were traced with `LOL_TRACE_FLAGS=1 scripts/headless_walk.mjs`.
//
// Conditions: { flag: n } game flag set, { level: n } party on level n (or has been there when
// combined with flags), { party: id } character id in the party, { item: propertyIndex } item in the
// inventory or equipped, { visited: n } level n has been entered, { all: [...] }, { any: [...] },
// { never: true }. `expire` marks an objective done once the party has been to that level (the story
// has moved on, whatever the flags say later). A completed objective stays completed: the ids are kept
// in the save (engine.questDone).
using System.Collections.Generic;
using System.Linq;

namespace Lol
{
    /// <summary>quests.mjs condition: exactly one of the keys is set (null = undefined).</summary>
    public sealed class QuestCond
    {
        public bool never;
        public QuestCond[] all, any;
        public int? flag, level, visited, party, item;
        public string itemName;
    }

    /// <summary>quests.mjs QUESTS entry (expire 0 = none)</summary>
    public sealed class Quest
    {
        public string id;
        public int level;
        public bool main;
        public int expire;
        public string title, detail;
        public QuestCond reveal, done;
    }

    public sealed partial class LandsOfLore
    {
        /// <summary>quests.mjs engine.questItemCache: item name -> property index (-1 = none)</summary>
        public Dictionary<string, int> questItemCache;

        static QuestCond Q_flag(int n) => new QuestCond { flag = n };
        static QuestCond Q_level(int n) => new QuestCond { level = n };
        static QuestCond Q_visited(int n) => new QuestCond { visited = n };
        static QuestCond Q_party(int n) => new QuestCond { party = n };
        static QuestCond Q_item(int n) => new QuestCond { item = n };
        static QuestCond Q_never() => new QuestCond { never = true };
        static QuestCond Q_all(params QuestCond[] c) => new QuestCond { all = c };
        static QuestCond Q_any(params QuestCond[] c) => new QuestCond { any = c };

        public static readonly Quest[] QUESTS =
        {
            // --- Gladstone and the Ruby ---
            new Quest { id = "king", level = 1, expire = 2, title = "Report to King Richard", detail = "The guard says His Majesty awaits you in the throne room of Gladstone Keep.", reveal = Q_level(1), done = Q_flag(4) },
            new Quest { id = "ruby", level = 1, main = true, expire = 5, title = "Retrieve the Ruby of Truth", detail = "King Richard needs the Ruby of Truth against Scotia. It is kept at Roland's estate in the Southland; bring it back to Gladstone.", reveal = Q_flag(4), done = Q_any(Q_visited(4), Q_party(3)) },
            new Quest { id = "atlas", level = 1, expire = 3, title = "Take the Magic Atlas from the King's library", detail = "The King gave you the key to his private library. Among the books is a Magic Atlas: it draws the map of every place you visit.", reveal = Q_flag(4), done = Q_flag(252) },
            new Quest { id = "writ", level = 1, expire = 3, title = "Collect your Writ from Geron", detail = "Geron's office is in the Keep. The Writ identifies you as being on official business for the King.", reveal = Q_flag(4), done = Q_flag(11) },
            new Quest { id = "timothy", level = 1, expire = 5, title = "Find Timothy at the Grey Eagle", detail = "Geron thinks that rascal Timothy, at the Grey Eagle inn, could help on the journey.", reveal = Q_flag(11), done = Q_party(2) },
            new Quest { id = "forest", level = 2, expire = 4, title = "Cross the forest to the boat", detail = "The road south leads through the woods outside Gladstone to the boat landing.", reveal = Q_flag(191), done = Q_flag(192) },
            new Quest { id = "eagle", level = 3, expire = 4, title = "Find the Grey Eagle inn", detail = "Petricia says the Timothy you seek is probably down at the inn.", reveal = Q_flag(192), done = Q_flag(7) },
            new Quest { id = "manor", level = 4, expire = 5, title = "Search Roland's Manor for the Ruby", detail = "Roland's estate lies beyond the Southland. Find the Ruby of Truth inside.", reveal = Q_visited(4), done = Q_party(3) },
            // --- after Scotia's attack on the Keep ---
            new Quest { id = "stolen", level = 4, expire = 5, title = "The Ruby has been stolen: warn King Richard", detail = "Scotia's warriors took the Ruby before you. Return to Gladstone at once.", reveal = Q_visited(4), done = Q_party(3) },
            new Quest { id = "elixir", level = 1, main = true, title = "Find the Elixir to cure King Richard", detail = "Scotia's poison would kill the King; a shroud holds it back for now. Our potions cannot cure him, but legends speak of an Elixir that could.", reveal = Q_party(3), done = Q_flag(310) }, // flag 310: the Elixir was made on the Altar (LEVEL18.INF)
            new Quest { id = "draracle", level = 5, expire = 10, title = "Visit the Draracle in his caves", detail = "Baccata knows the path to the Draracle beneath the Southland. Perhaps he knows how the Elixir is made.", reveal = Q_party(3), done = Q_any(Q_visited(9), Q_visited(10)) },
            // The Elixir of Tybal (traced in LEVEL18.INF / LEVEL19.INF): the Crucible of Faith from the Tower
            // sub-level goes on the Altar de Blanca in Tower Level 1, then the four ingredients are used on it:
            // Bloodstone (273), Swamp Vial (212), Honey (213), Earth Vial (214). Flag 347 = crucible placed,
            // flag 310 = Elixir complete (the Crucible of Faith in hand is the Elixir).
            new Quest { id = "ingredients", level = 9, title = "Gather the Elixir's four ingredients", detail = "The Draracle: \"Ingredients are everywhere, but the Elixir can only be created in an ancient white tower.\" The recipe is a riddle: the flesh that never lived (a Bloodstone), what is gathered in the deadly depths (a Swamp Vial), the sweetness of your enemy (Honey) and powders from the heart of your mother (an Earth Vial). Empty vials hold the liquids.", reveal = Q_any(Q_visited(9), Q_visited(10)), done = Q_any(Q_flag(310), Q_all(Q_item(273), Q_item(212), Q_item(213), Q_item(214))) },
            new Quest { id = "crucible", level = 19, title = "Take the Crucible of Faith from the Tower's sub-level", detail = "\"Take this crucible, for ye art true of heart. Place it upon the Altar de Blanca to receive the ingredients for the Elixir.\"", reveal = Q_visited(18), done = Q_any(Q_item(260), Q_flag(347), Q_flag(310)) },
            new Quest { id = "altar", level = 18, title = "Make the Elixir on the Altar de Blanca", detail = "In Tower Level 1: put the Crucible of Faith on the altar, then use each ingredient on it (Bloodstone, Swamp Vial, Honey, Earth Vial). Only the right ones are accepted. When all four are in, take the Crucible back: that is the Elixir.", reveal = Q_any(Q_item(260), Q_flag(347)), done = Q_flag(310) },
            // --- the road to the Elixir's ingredients, by region (levels 10-26 are keyed on where the party has been) ---
            new Quest { id = "opinwood", level = 10, title = "Explore Opinwood", detail = "The woods beyond the Gladstone road; the Elixir's ingredients are said to be found in these lands.", reveal = Q_visited(10), done = Q_any(Q_visited(11), Q_visited(12)) },
            new Quest { id = "swamp", level = 11, title = "Cross the Gorkha swamp", detail = "The swamp people brew salves from what grows here; watch for the swamp creatures.", reveal = Q_visited(11), done = Q_visited(12) },
            new Quest { id = "urbish", level = 12, title = "Reach the town of Urbish", detail = "Urbish sits at the mouth of its mines. Ask around for what the Elixir still needs.", reveal = Q_visited(12), done = Q_visited(13) },
            new Quest { id = "mines", level = 13, title = "Descend into the Urbish mines", detail = "The mines run four levels deep; the deepest galleries hold what you are looking for.", reveal = Q_visited(13), done = Q_any(Q_visited(17), Q_visited(18)) },
            new Quest { id = "upperwood", level = 17, expire = 22, title = "Cross Upper Opinwood to the White Tower", detail = "Upper Opinwood lies between the mines and the tower.", reveal = Q_visited(17), done = Q_visited(18) },
            new Quest { id = "tower", level = 18, title = "Climb the White Tower", detail = "The tower rises four floors above the forest; its master is not expecting visitors.", reveal = Q_visited(18), done = Q_visited(22) },
            new Quest { id = "yvel", level = 17, title = "Bring the Elixir to Yvel", detail = "By Dawn's order nobody enters Yvel without the Elixir.", reveal = Q_flag(310), done = Q_visited(22) },
            new Quest { id = "shard", level = 22, main = true, title = "Recover the Ruby and find the Shard", detail = "They say the Ruby and the Shard are the only means of opposing the Nether Mask that Scotia found.", reveal = Q_visited(22), done = Q_visited(27) },
            new Quest { id = "catwalk", level = 23, title = "Pass through the Catwalk caverns", detail = "Beyond Yvel the caverns' catwalks lead on toward Scotia's lands.", reveal = Q_visited(23), done = Q_visited(26) },
            new Quest { id = "ruins", level = 26, title = "Escape the dungeons below Castle Cimmeria", detail = "The dungeons lead up into Scotia's castle.", reveal = Q_visited(26), done = Q_visited(27) },
            new Quest { id = "scotia", level = 27, main = true, title = "Defeat Scotia in Castle Cimmeria", detail = "Scotia and the Nether Mask wait in Castle Cimmeria.", reveal = Q_visited(27), done = Q_never() },
        };

        public static bool evalCondition(LandsOfLore engine, QuestCond cond)
        {
            if (cond == null) return false;
            if (cond.never) return false;
            if (cond.all != null) return cond.all.All(c => evalCondition(engine, c));
            if (cond.any != null) return cond.any.Any(c => evalCondition(engine, c));
            if (cond.flag != null) return engine.queryGameFlag(cond.flag.Value) != 0;
            if (cond.level != null) return engine.currentLevel == cond.level;
            if (cond.visited != null) return engine.currentLevel == cond.visited || (engine.hasTempDataFlags & (1 << (cond.visited.Value - 1))) != 0;
            if (cond.party != null) return engine.characters.Any(c => (c.flags & 1) != 0 && c.id == cond.party);
            if (cond.itemName != null)
            {
                if (engine.questItemCache == null) engine.questItemCache = new Dictionary<string, int>();
                if (!engine.questItemCache.ContainsKey(cond.itemName)) engine.questItemCache[cond.itemName] = engine.itemProperties.FindIndex(p => (engine.getLangString(p.nameStringId) ?? "") == cond.itemName);
                int prop = engine.questItemCache[cond.itemName];
                return prop >= 0 && evalCondition(engine, new QuestCond { item = prop });
            }
            if (cond.item != null)
            {
                bool has(int i) => i != 0 && i < engine.itemsInPlay.Length && engine.itemsInPlay[i] != null && engine.itemsInPlay[i].itemPropertyIndex == cond.item;
                return engine.inventory.Any(i => has(i)) || engine.characters.Any(c => (c.flags & 1) != 0 && c.items.Any(i => has(i))) || has(engine.itemInHand);
            }
            return false;
        }

        // "hidden" | "active" | "done" for every quest.
        static bool Quests_visitedBeyond(LandsOfLore engine, int level)
        {
            if (engine.currentLevel >= level) return true;
            for (int l = level; l <= 29; l += 1) if ((engine.hasTempDataFlags & (1 << (l - 1))) != 0) return true;
            return false;
        }

        public static Dictionary<string, string> questStates(LandsOfLore engine)
        {
            if (engine.questDone == null) engine.questDone = new HashSet<string>();
            var states = new Dictionary<string, string>();
            foreach (var q in QUESTS)
            {
                string state = engine.questDone.Contains(q.id) || evalCondition(engine, q.done) || (q.expire != 0 && Quests_visitedBeyond(engine, q.expire)) ? "done" : evalCondition(engine, q.reveal) ? "active" : "hidden";
                if (state == "done") engine.questDone.Add(q.id);
                states[q.id] = state;
            }
            return states;
        }
    }
}
