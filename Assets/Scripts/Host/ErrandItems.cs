// src/platform/errand-items.mjs. C# 9.
// Errand trophies and the pit's sigil, with the pictures the page draws them with.
//
// What they are - their ids, names and that nobody buys them - is the engine's
// (src/game/camp-store.mjs), so that every build's imp asks for the same things. The SVG beside
// each one is the browser's alone.
using System.Collections.Generic;
using System.Linq;
using Lol;

namespace LolHost
{
    public static class ErrandItems
    {
        static readonly Dictionary<string, string> ART = new Dictionary<string, string>
        {
            ["bone"] = "src/assets/errand-bone.svg",
            ["heart"] = "src/assets/errand-heart.svg",
            ["chitin"] = "src/assets/errand-chitin.svg",
            ["core"] = "src/assets/errand-core.svg",
            ["fang"] = "src/assets/errand-fang.svg",
            ["sigil"] = "src/assets/pit-sigil.svg",
        };

        // { ...item, art: ART[item.id] }
        static ExtraItemDef[] withArt(IEnumerable<ExtraItemDef> items) => items.Select(item => new ExtraItemDef
        {
            id = item.id, name = item.name, icon = item.icon, art = ART.TryGetValue(item.id, out var a) ? a : null, effect = item.effect,
            amount = item.amount, price = item.price, use = item.use, skill = item.skill, seconds = item.seconds, kind = item.kind, family = item.family,
        }).ToArray();

        public static readonly ExtraItemDef[] ERRAND_ITEMS = withArt(LandsOfLore.ERRAND_ITEMS);
        public static readonly ExtraItemDef[] DUNGEON_ITEMS = withArt(LandsOfLore.DUNGEON_ITEMS);
        public static ExtraItemDef errandItemFor(string familyId) => LandsOfLore.errandItemFor(familyId);
    }
}
