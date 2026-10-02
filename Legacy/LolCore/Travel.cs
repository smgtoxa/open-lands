// Fast travel: the party set out from a camp they have rested in.
//
// Transliterated from the travel half of src/game/camp.mjs. The rules are the engine's - where the
// party may go, and when they may go at all - because a host that draws the panel should not be the
// thing that decides it. The five seconds of casting, and the waiting, belong to the host.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>Where the party have been, as the player's record remembers it.</summary>
    public Meta Meta;

    public sealed class TravelTarget
    {
        public int Level, Block, Direction;
        public string Name = "";
    }

    /// <summary>levelName: the name the atlas gives a level, or "Level n" before its strings load.</summary>
    public string LevelName(int level)
    {
        var mapStringId = StaticData.Table("MapStringId");
        int id = level >= 0 && level < mapStringId.Length ? mapStringId[level] : 0xffff;
        string name = id != 0xffff ? LangString(id) : null;
        return string.IsNullOrEmpty(name) ? $"Level {level}" : name;
    }

    /// <summary>Every level the party could travel to, the one they stand on aside.</summary>
    public List<TravelTarget> TravelTargets()
    {
        var out_ = new List<TravelTarget>();
        if (Meta == null) return out_;
        foreach (var (level, where) in Meta.Visited)
        {
            if (level == _loader.Level) continue;
            out_.Add(new TravelTarget { Level = level, Block = where.Block, Direction = where.Direction, Name = LevelName(level) });
        }
        out_.Sort((a, b) => a.Level.CompareTo(b.Level));
        return out_;
    }

    /// <summary>Whether the party may set out, and - when they may not - what is stopping them.</summary>
    public (bool Ok, string Why) TravelReady(int level)
    {
        if (!InCamp) return (false, "Make camp first: the party can only set out from a camp.");
        if (InCombat()) return (false, "Not while something is attacking the camp.");
        var hurt = Characters.Where(c => c.Active && (c.HitPointsCur < c.HitPointsMax || c.MagicPointsCur < c.MagicPointsMax)).ToArray();
        if (hurt.Length != 0)
            return (false, $"Rest until everyone has recovered ({string.Join(", ", hurt.Select(c => c.Name))} still resting).");
        if (level < 0) return (false, "Choose where to teleport the party.");
        if (!TravelTargets().Any(t => t.Level == level)) return (false, "The party have never been there.");
        return (true, "The spell takes five seconds to cast. Anything that breaks the camp breaks the spell.");
    }

    /// <summary>How long the casting takes; the host draws the waiting.</summary>
    public const double TravelSeconds = 5;

    /// <summary>uiInCombat: something is within reach of the party and still fighting.</summary>
    public bool InCombat()
    {
        if (_loader.Party.Block == 0) return false;
        foreach (var m in _loader.Board.Monsters)
        {
            if (m?.Properties == null || m.HitPoints <= 0 || m.Mode >= 13 || m.Block == 0) continue;
            if (MonsterBoard.GetBlockDistance(_loader.Party.Block, m.Block) <= 1) return true;
        }
        return false;
    }

    /// <summary>
    /// The journey itself: the camp is packed up, the level the party asked for is loaded, and they
    /// arrive where they last stood on it. False when the rules say they may not go.
    /// </summary>
    public bool TravelTo(int level)
    {
        var target = TravelTargets().FirstOrDefault(t => t.Level == level);
        if (target == null || !TravelReady(level).Ok) return false;
        LeaveCamp();
        _loader.LoadNewLevel(level, target.Block, target.Direction);
        return true;
    }
}
