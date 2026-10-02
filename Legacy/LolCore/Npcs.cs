// Who the party have spoken to: the dialogue scripts name their speakers, and this is what those
// names mean.
//
// Transliterated from src/game/npcs.mjs. A port addition - the original never shows a name - kept
// in the engine so that every build calls the same person by the same name.
namespace LolCore;

public sealed partial class LevelLoader
{
    private static readonly Dictionary<string, string> NpcNames = new(StringComparer.Ordinal)
    {
        ["GUARD"] = "Castle guard",
        ["KING"] = "King Richard",
        ["KINGSIT"] = "King Richard",
        ["GERIM"] = "Geron",
        ["GEREM"] = "Geron",
        ["DRGERIM"] = "Geron",
        ["VICTOR"] = "Victor",
        ["DRVICTOR"] = "Victor",
        ["DARKVIC"] = "Victor",
        ["YVIC"] = "Victor",
        ["NATE"] = "Nathaniel",
        ["DRNATHAN"] = "Nathaniel",
        ["PAUL"] = "Paulson",
        ["PAULSNA"] = "Paulson",
        ["PAULSNB"] = "Paulson",
        ["PAULSNC"] = "Paulson",
        ["DAWN"] = "Dawn",
        ["DAWNORB"] = "Dawn",
        ["DAWN_LIZ"] = "Dawn",
        ["DAWN_SCO"] = "Dawn",
        ["RESCDWN"] = "Dawn",
        ["WILL"] = "Will",
        ["CONFRONT"] = "Scotia",
        ["SCOTIA"] = "Scotia",
        ["DARKCOS"] = "Scotia",
        ["DARKCOM"] = "Scotia",
        ["TALAMSCA"] = "Talamsca",
        ["BOAT"] = "The boatman",
        ["LYNN"] = "Lynn",
        ["DOM"] = "Dominic",
        ["TIM"] = "Timothy",
        ["THOMGOG"] = "Thomgog",
        ["TYRUS"] = "Tyrus",
        ["INN"] = "The innkeeper",
        ["BUCK"] = "Buck",
        ["BUCKBUY"] = "Buck",
        ["DRARCLE"] = "The Draracle",
        ["ROLAND"] = "Roland",
        ["CHIEF"] = "The chief",
        ["COUNC"] = "The council",
        ["FAITH"] = "Faith",
        ["FLETCH"] = "Fletcher",
        ["YFLETCH"] = "Fletcher",
        ["FRANK"] = "Frank",
        ["BRUFRAN"] = "Frank",
        ["HAG"] = "The hag",
        ["HAG_S"] = "The hag",
        ["JANA"] = "Jana",
        ["KNOWLE"] = "Knowles",
        ["KNWLFNT"] = "Knowles",
        ["LYLESAD"] = "Lyle",
        ["LYLESLY"] = "Lyle",
        ["SADIE"] = "Sadie",
        ["SADIE_E"] = "Sadie",
        ["ORIN"] = "Orin",
        ["DWIGHT"] = "Dwight",
        ["SMITHY"] = "The smith",
        ["DEDROEK"] = "Droek",
        ["DROEK"] = "Droek",
        ["XEOB"] = "Xeob",
        ["XEOBCLB"] = "Xeob",
        ["BRUCLIFF"] = "Cliff",
        ["BRUNORM"] = "Norm",
        ["BRUSAM"] = "Sam",
        ["MIX"] = "The alchemist",
        ["CRUCIBLE"] = "The crucible",
        ["POD"] = "The pod",
        ["WDR"] = "The Draracle",
        ["LIZ_BIRD"] = "Lizard bird",
        ["TAUNT"] = "Scotia",
        ["WARNING"] = "A voice",
        ["ESCAPE"] = "Escape",
    };

    /// <summary>npcName: GUARD1.TIM is the castle guard; an unknown script is named after itself.</summary>
    public static string NpcName(string file)
    {
        string upper = (file ?? "").ToUpperInvariant();
        int dot = upper.IndexOf(".", StringComparison.Ordinal);
        if (dot >= 0) upper = upper[..dot];
        string stem = upper.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (NpcNames.TryGetValue(stem, out string name)) return name;
        if (NpcNames.TryGetValue(upper, out name)) return name;
        if (stem.Length == 0) return "";
        return stem[0] + stem[1..].ToLowerInvariant();
    }

    /// <summary>A conversation started: the person is remembered, with where and how often.</summary>
    public (string Name, int Talks) NpcMet(string file)
    {
        string name = NpcName(file);
        if (name is "Escape" or "A voice") return (null, 0);
        int talks = Meta?.NpcMet(name) ?? 0;
        return (name, talks);
    }
}
