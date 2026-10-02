// The camp as a real place.
//
// Transliterated from src/game/camp.mjs. A three-by-three chamber is cut out of solid, unreachable
// rock on the level the party is already standing on, and every block it borrows is put back
// exactly as it was when they leave. Nothing is loaded and no script fires, so no story event can
// go off by accident, and the chamber is never marked as seen, so it leaves the automap alone.
namespace LolCore;

public sealed class CampPiece
{
    public int Dir;
    public string Name = "", Label = "";
}

public sealed partial class Gui
{
    private const int CampWallBase = 240;   // wall types 240..243 are unused by every LEVEL<N>.WLL
    private const int CampGrid = 32;

    public static readonly CampPiece[] CampPieces =
    {
        new() { Dir = 0, Name = "travel", Label = "The travelling circle" },
        new() { Dir = 1, Name = "craft", Label = "The cauldron" },
        new() { Dir = 2, Name = "stash", Label = "The stash chest" },
        new() { Dir = 3, Name = "imp", Label = "The imp trader" },
    };

    public sealed class CampRoom
    {
        public int Centre;
        public int[] Mid = new int[4];
        public int[] Corners = Array.Empty<int>();
        public int[] Outside = new int[4];
        public int[] All = Array.Empty<int>();
        public bool Alcove;
    }

    /// <summary>What was borrowed from the level and where the party stood before they pitched it.</summary>
    public sealed class CampReturn
    {
        public int Level, Block, Direction;
        public double Settled;
        public CampRoom Room;
        public (int Block, byte[] Walls, byte Flags, byte Direction)[] Snapshot;
    }

    public CampReturn Camp;
    /// <summary>A warm hearth halves the wait; the host sets it from the camp's upgrades.</summary>
    public double CampHealBoost = 1;
    private readonly Dictionary<int, HashSet<int>> _campSeen = new();

    private BlockMap CampMap => _loader.Map;

    /// <summary>The nine blocks of a chamber centred on `centre`, plus the four outside its walls.
    /// Anything that would wrap around the edge of the map is rejected: those blocks look adjacent
    /// by index but are on the far side of the level, and opening a wall between them is a hole.</summary>
    public CampRoom CampRoomAt(int centre)
    {
        int c = centre & (CampGrid - 1), r = centre >> 5;
        if (c < 2 || c > CampGrid - 3 || r < 2 || r > CampGrid - 3) return null;
        int At(int dc, int dr) => centre + dc + dr * CampGrid;
        var mid = new[] { At(0, -1), At(1, 0), At(0, 1), At(-1, 0) };
        var corners = new[] { At(1, -1), At(1, 1), At(-1, 1), At(-1, -1) };
        var outside = new[] { At(0, -2), At(2, 0), At(0, 2), At(-2, 0) };
        var all = new[] { centre }.Concat(mid).Concat(corners).ToArray();
        if (all.Concat(outside).Distinct().Count() != 13) return null;
        for (int d = 0; d < 4; d += 1)
        {
            if (Party.CalcNewBlockPosition(centre, d) != mid[d]) return null;
            if (Party.CalcNewBlockPosition(mid[d], d) != outside[d]) return null;
        }
        return new CampRoom { Centre = centre, Mid = mid, Corners = corners, Outside = outside, All = all };
    }

    /// <summary>A single block with a door on each wall: the fallback where a level is too crowded.</summary>
    public CampRoom CampAlcoveAt(int centre)
    {
        int c = centre & (CampGrid - 1), r = centre >> 5;
        if (c < 1 || c > CampGrid - 2 || r < 1 || r > CampGrid - 2) return null;
        var outside = new[] { centre - CampGrid, centre + 1, centre + CampGrid, centre - 1 };
        for (int d = 0; d < 4; d += 1) if (Party.CalcNewBlockPosition(centre, d) != outside[d]) return null;
        if (new[] { centre }.Concat(outside).Distinct().Count() != 5) return null;
        return new CampRoom
        {
            Centre = centre, Mid = new[] { centre, centre, centre, centre }, Corners = Array.Empty<int>(),
            Outside = outside, All = new[] { centre }, Alcove = true,
        };
    }

    public bool CampRock(int b)
    {
        if (b < 0 || b > 1023 || b == _loader.Party.Block) return false;
        if ((CampMap.Flags[b] & 7) != 0 || CampMap.AssignedObjects[b] != 0 || CampMap.DrawObjects[b] != 0) return false;
        for (int d = 0; d < 4; d += 1) if (_loader.Party.CheckBlockPassability(b, d)) return false;
        return true;
    }

    /// <summary>Somewhere the camp can be cut. Every block of the chamber, and every block it
    /// touches, has to be solid rock: one soft neighbour on the ring is a way out into the level.</summary>
    public CampRoom FindCampRoom()
    {
        bool Fits(CampRoom room)
        {
            if (room == null) return false;
            var inside = new HashSet<int>(room.All);
            foreach (int b in room.All.Concat(room.Outside)) if (!CampRock(b)) return false;
            foreach (int b in room.All)
                for (int d = 0; d < 4; d += 1)
                {
                    int n = Party.CalcNewBlockPosition(b, d);
                    if (!inside.Contains(n) && !CampRock(n)) return false;
                }
            return true;
        }
        for (int b = 0; b < 1024; b += 1) { var room = CampRoomAt(b); if (Fits(room)) return room; }
        for (int b = 0; b < 1024; b += 1) { var room = CampAlcoveAt(b); if (Fits(room)) return room; }
        return null;
    }

    /// <summary>A door for each wall, in the level's own style, carrying the camp's emblem.</summary>
    public void CampFurnish()
    {
        var walls = _loader.Walls;
        int door = -1;
        for (int w = 1; w < 256 && door < 0; w += 1)
            if ((walls.Automap[w] & 0x1f) == 13 && walls.VmpMap[w] != 0 && (walls.WallFlags[w] & 1) == 0) door = w;
        foreach (var piece in CampPieces)
        {
            int wall = CampWallBase + piece.Dir;
            walls.VmpMap[wall] = door > 0 ? walls.VmpMap[door] : (byte)1;
            walls.ShapeMap[wall] = door > 0 ? walls.ShapeMap[door] : (short)0;
            walls.WallFlags[wall] = 0x0f;      // shut: the party never walks through it
            walls.SpecialTypes[wall] = 1;      // clicking it is what opens the room behind
            walls.Automap[wall] = 0;
        }
    }

    /// <summary>Living monsters within range of the party: you cannot pitch a camp while any of
    /// them is that close - the tent would be found before the first hour passed.</summary>
    public List<Monster> CampThreats(int range = 3)
    {
        var found = new List<Monster>();
        if (_loader.Party.Block == 0) return found;
        foreach (var m in _loader.Board.Monsters)
        {
            if (m?.Properties == null || m.HitPoints <= 0 || m.Mode >= 13 || m.Block == 0) continue;
            if (MonsterBoard.GetBlockDistance(_loader.Party.Block, m.Block) <= range) found.Add(m);
        }
        return found;
    }

    /// <summary>Step into the camp. False when there is nowhere to cut it.</summary>
    public bool EnterCamp()
    {
        if (Camp != null) return true;
        if (CampThreats().Count != 0) return false;   // checked here too: every path in is covered
        var room = FindCampRoom();
        if (room == null) return false;
        CampFurnish();
        // Everything the chamber touches is copied before a single wall is moved, and put back from
        // that copy: the level can never be left changed, whatever happens in between.
        var touched = new HashSet<int>(room.All);
        foreach (int b in room.All) for (int d = 0; d < 4; d += 1) touched.Add(Party.CalcNewBlockPosition(b, d));
        // Anything a camp on this level touched before is scrubbed first: if a restore was ever
        // missed, the old chamber would stay drawn on the automap for the rest of the game.
        if (_campSeen.TryGetValue(_loader.Level, out var before))
            foreach (int b in before) if (!touched.Contains(b)) CampMap.Flags[b] &= unchecked((byte)~7);
        _campSeen[_loader.Level] = new HashSet<int>(touched);

        var snapshot = touched.Select(b => (
            Block: b,
            Walls: new[] { CampMap.Walls[b, 0], CampMap.Walls[b, 1], CampMap.Walls[b, 2], CampMap.Walls[b, 3] },
            Flags: CampMap.Flags[b],
            Direction: CampMap.Direction[b])).ToArray();

        var inside = new HashSet<int>(room.All);
        foreach (int b in room.All)      // open every wall between two blocks of the chamber
            for (int d = 0; d < 4; d += 1)
            {
                int n = Party.CalcNewBlockPosition(b, d);
                if (!inside.Contains(n)) continue;
                CampMap.Walls[b, d] = 0;
                CampMap.Walls[n, d ^ 2] = 0;
            }
        // The wall the party looks at belongs to the block in front of them, on the side facing back.
        foreach (var piece in CampPieces)
            CampMap.Walls[room.Outside[piece.Dir], piece.Dir ^ 2] = (byte)(CampWallBase + piece.Dir);

        Camp = new CampReturn
        {
            Level = _loader.Level,
            Block = _loader.Party.Block,
            Direction = _loader.Party.Direction,
            Settled = _loader.Clock + 2000,
            Room = room,
            Snapshot = snapshot,
        };
        _loader.Party.MoveTo(room.Centre);
        _loader.Party.Direction = 0;
        _loader.PartyDirection = 0;
        _loader.InvalidateDrawOrder();
        DrawScene(0);
        return true;
    }

    /// <summary>Put every borrowed block back and return the party to where they made camp.</summary>
    public bool LeaveCamp()
    {
        var r = Camp;
        if (r == null) return false;
        foreach (var s in r.Snapshot)
        {
            for (int d = 0; d < 4; d += 1) CampMap.Walls[s.Block, d] = s.Walls[d];
            CampMap.Flags[s.Block] = (byte)(s.Flags & ~7);   // what the party saw is forgotten with it
            CampMap.Direction[s.Block] = s.Direction;
        }
        Camp = null;
        _loader.Party.MoveTo(r.Block);
        _loader.Party.Direction = r.Direction;
        _loader.PartyDirection = r.Direction;
        _loader.InvalidateDrawOrder();
        DrawScene(0);
        return true;
    }

    /// <summary>Something hostile has walked up to where the party left their things.</summary>
    public bool CampIntruded()
    {
        var r = Camp;
        if (r == null) return false;
        if (_loader.Clock < r.Settled) return false;   // a moment to sit down first
        foreach (var m in _loader.Board.Monsters)
        {
            if (m?.Properties == null || m.HitPoints <= 0 || m.Mode >= 13 || m.Block == 0) continue;
            int d = MonsterBoard.GetBlockDistance(r.Block, m.Block);
            if (d == 0 || (d == 1 && m.Mode >= 5)) return true;
        }
        return false;
    }

    public bool InCamp => Camp != null;

    /// <summary>The camp door in front of the party and how far off it is. Read from the walls
    /// themselves, exactly the way a click on a wall finds what it landed on, so the two can never
    /// disagree. Returns null when there is no door ahead.</summary>
    public (string Piece, int Distance)? CampView()
    {
        if (Camp == null) return null;
        int dir = _loader.Party.Direction, back = dir ^ 2;
        string Door(int b)
        {
            int wall = CampMap.Walls[b, back];
            if (wall < CampWallBase || wall > CampWallBase + 3) return null;
            return CampPieces.FirstOrDefault(p => p.Dir == wall - CampWallBase)?.Name;
        }
        int ahead = Party.CalcNewBlockPosition(_loader.Party.Block, dir);
        if (ahead == _loader.Party.Block) return null;
        string near = Door(ahead);
        if (near != null) return (near, 1);
        if (_loader.Party.TestWallFlag(ahead, dir, 1)) return null;   // something solid in the way
        int far = Party.CalcNewBlockPosition(ahead, dir);
        if (far == ahead) return null;
        string next = Door(far);
        return next != null ? (next, 2) : null;
    }

    /// <summary>Only what the party can reach out and touch answers a click.</summary>
    public string CampFacing()
    {
        var view = CampView();
        return view is { Distance: 1 } ? view.Value.Piece : null;
    }

    /// <summary>One turn of the camp's healing: health and magic come back while the party stays.
    /// A point a tick is a crawl for a hero with a hundred and sixty hit points, so the pace is
    /// scaled to each pool - what matters is how long a full recovery takes, not how many points.</summary>
    public void CampHeal()
    {
        if (Camp == null) return;
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if (!c.Active || (c.Flags & 8) != 0 || (c.Flags & 0x80) != 0) continue;   // not poison, not the dead
            bool changed = false;
            double pace = 40 / (CampHealBoost <= 0 ? 1 : CampHealBoost);
            if (c.HitPointsCur < c.HitPointsMax)
            {
                int step = Math.Max(1, (int)Math.Ceiling(c.HitPointsMax / pace));
                IncreaseCharacterHitpoints(i, Math.Min(step, c.HitPointsMax - c.HitPointsCur));
                changed = true;
            }
            if (c.MagicPointsCur < c.MagicPointsMax)
            {
                int step = Math.Max(1, (int)Math.Ceiling(c.MagicPointsMax / pace));
                c.MagicPointsCur = Math.Min(c.MagicPointsMax, c.MagicPointsCur + step);
                changed = true;
            }
            if (changed) DrawCharPortraitWithStats(i);
        }
    }

    /// <summary>Sleeping through a stretch of the night: the party wakes mended, but the brews they
    /// drank have long worn off. Returns the hours actually slept.</summary>
    public int CampSleep(int hours)
    {
        if (Camp == null || hours <= 0) return 0;
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if (!c.Active) continue;
            // Potion buffs do not survive half a day: take back what each one added, the same undo
            // the 9/10/11 timer events do when they expire.
            for (int e = 0; e < 5; e += 1)
            {
                int type = c.CharacterUpdateEvents[e];
                if (type < 9 || type > 11) continue;
                int skill = type - 9;
                c.SkillModifiers[skill] -= c.PotionSkillBonus[skill];
                c.PotionSkillBonus[skill] = 0;
                c.CharacterUpdateEvents[e] = 0;
                c.CharacterUpdateDelay[e] = 0;
            }
            if ((c.Flags & 8) != 0 || (c.Flags & 0x80) != 0) continue;
            if (c.HitPointsCur < c.HitPointsMax) IncreaseCharacterHitpoints(i, c.HitPointsMax - c.HitPointsCur);
            c.MagicPointsCur = c.MagicPointsMax;
            DrawCharPortraitWithStats(i);
        }
        PlayTimer += hours * 3600;
        return hours;
    }

    /// <summary>Seconds of play, as the camp counts them: sleeping moves it on by whole hours.</summary>
    public double PlayTimer;

    public bool CampRested()
    {
        foreach (var c in Characters)
            if (c.Active && (c.Flags & 8) == 0 && (c.HitPointsCur < c.HitPointsMax || c.MagicPointsCur < c.MagicPointsMax))
                return false;
        return true;
    }
}
