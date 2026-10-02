// src/game/npcs.mjs (NpcsMixin): who the party have spoken to. The dialogue scripts name their
// speakers, and this is what those names mean. A port addition - the original never shows a name -
// and it lives in the engine so that every build calls the same person by the same name.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lol
{
    /// <summary>npcs.mjs meta.npcs[name]: { name, talks, level, block }</summary>
    public sealed class NpcEntry
    {
        public string name;
        public int talks, level, block;
    }

    public sealed partial class LandsOfLore
    {
        static readonly Dictionary<string, string> Npcs_NAMES = new Dictionary<string, string>
        {
            { "GUARD", "Castle guard" }, { "KING", "King Richard" }, { "KINGSIT", "King Richard" }, { "GERIM", "Geron" }, { "GEREM", "Geron" }, { "DRGERIM", "Geron" }, { "VICTOR", "Victor" }, { "DRVICTOR", "Victor" }, { "DARKVIC", "Victor" }, { "YVIC", "Victor" },
            { "NATE", "Nathaniel" }, { "DRNATHAN", "Nathaniel" }, { "PAUL", "Paulson" }, { "PAULSNA", "Paulson" }, { "PAULSNB", "Paulson" }, { "PAULSNC", "Paulson" }, { "DAWN", "Dawn" }, { "DAWNORB", "Dawn" }, { "DAWN_LIZ", "Dawn" }, { "DAWN_SCO", "Dawn" }, { "RESCDWN", "Dawn" },
            { "WILL", "Will" }, { "CONFRONT", "Scotia" }, { "SCOTIA", "Scotia" }, { "DARKCOS", "Scotia" }, { "DARKCOM", "Scotia" }, { "TALAMSCA", "Talamsca" }, { "BOAT", "The boatman" }, { "LYNN", "Lynn" }, { "DOM", "Dominic" }, { "TIM", "Timothy" },
            { "THOMGOG", "Thomgog" }, { "TYRUS", "Tyrus" }, { "INN", "The innkeeper" }, { "BUCK", "Buck" }, { "BUCKBUY", "Buck" }, { "DRARCLE", "The Draracle" }, { "ROLAND", "Roland" }, { "CHIEF", "The chief" }, { "COUNC", "The council" },
            { "FAITH", "Faith" }, { "FLETCH", "Fletcher" }, { "YFLETCH", "Fletcher" }, { "FRANK", "Frank" }, { "BRUFRAN", "Frank" }, { "HAG", "The hag" }, { "HAG_S", "The hag" }, { "JANA", "Jana" }, { "KNOWLE", "Knowles" }, { "KNWLFNT", "Knowles" },
            { "LYLESAD", "Lyle" }, { "LYLESLY", "Lyle" }, { "SADIE", "Sadie" }, { "SADIE_E", "Sadie" }, { "ORIN", "Orin" }, { "DWIGHT", "Dwight" }, { "SMITHY", "The smith" }, { "DEDROEK", "Droek" }, { "DROEK", "Droek" }, { "XEOB", "Xeob" }, { "XEOBCLB", "Xeob" },
            { "BRUCLIFF", "Cliff" }, { "BRUNORM", "Norm" }, { "BRUSAM", "Sam" }, { "MIX", "The alchemist" }, { "CRUCIBLE", "The crucible" }, { "POD", "The pod" }, { "WDR", "The Draracle" }, { "LIZ_BIRD", "Lizard bird" }, { "TAUNT", "Scotia" }, { "WARNING", "A voice" }, { "ESCAPE", "Escape" },
        };

        public static string npcName(string file)
        {
            string stem = Regex.Replace(file.ToUpperInvariant(), @"\d+$", "");
            if (Npcs_NAMES.TryGetValue(stem, out var n1)) return n1;
            if (Npcs_NAMES.TryGetValue(file.ToUpperInvariant(), out var n2)) return n2;
            return (stem.Length > 0 ? stem.Substring(0, 1) : "") + (stem.Length > 1 ? stem.Substring(1).ToLowerInvariant() : "");
        }

        // A conversation started: the person is remembered, with where it happened and how often.
        public NpcEntry uiNpcMet(string file)
        {
            string name = npcName(file);
            if (Regex.IsMatch(name, "^(Escape|A voice)$")) return null;
            // meta.npcs also holds metaNpcMet's per-file counters (numbers); only entries are objects
            var entry = meta.npcs.TryGetValue(name, out var v) ? v as NpcEntry : null;
            if (entry == null) meta.npcs[name] = entry = new NpcEntry { name = name, talks = 0, level = 0, block = 0 };
            entry.talks += 1;
            entry.level = currentLevel;
            entry.block = currentBlock;
            metaCheckAchievements();
            return entry;
        }

        public List<NpcEntry> uiNpcList()
        {
            return meta.npcs.Values.OfType<NpcEntry>().ToList();
        }
    }
}
