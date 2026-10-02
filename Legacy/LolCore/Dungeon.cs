// The Imp's Pit: an endless run of generated floors (a port addition).
//
// Transliterated from src/game/dungeon.mjs. A floor is not a new level format: it is a level the
// party has already walked, with its whole 32x32 block map rewritten in memory - the same trick
// Camp.cs plays on three blocks, done to all 1024. Everything the borrowed level owns is copied
// out first and put back when the party leaves, so the real level is never changed by a run.
namespace LolCore;

/// <summary>A generated floor: which blocks are rock, where the party lands, and where the way
/// down is. Pure - no engine, no game data - so it can be checked on its own.</summary>
public sealed class DungeonPlan
{
    public byte[] Walls = new byte[1024];   // 0 open, anything else the rock's wall type
    public int Start, Exit;
    public List<int> Cells = new();
    public List<int> DeadEnds = new();
    public Dictionary<int, int> Dist = new();
    public (int X, int Y, int W, int H) Rect;
}

public static class DungeonFloor
{
    /// <summary>The same small seeded PRNG the randomizer uses (mods.mjs dungeonRng).</summary>
    public static Func<double> Rng(string seedText)
    {
        uint h = 2166136261;
        foreach (char ch in seedText) { h ^= ch; h = unchecked(h * 16777619); }
        return () =>
        {
            h = unchecked(h + 0x6d2b79f5);
            uint t = h;
            t = unchecked((uint)((t ^ (t >> 15)) * (t | 1)));
            t ^= unchecked(t + (uint)((t ^ (t >> 7)) * (t | 61)));
            return ((t ^ (t >> 14)) & 0xffffffff) / 4294967296.0;
        };
    }

    /// <summary>Cells across (not blocks): a floor grows with depth.</summary>
    public static int Cells(int depth)
    {
        if (depth <= 3) return 7;
        if (depth <= 8) return 9;
        if (depth <= 15) return 11;
        return 13;
    }

    private static readonly int[] Step = { -32, 1, 32, -1 };
    private static readonly int[] Dx = { 0, 1, 0, -1 };
    private static readonly int[] Dy = { -1, 0, 1, 0 };

    /// <summary>
    /// Generates a floor. The map is written the way the original levels are: a solid block carries
    /// the wall on all four of its faces, an open block carries nothing. Walking is blocked by the
    /// destination block's face, and a monster can only stand where no face of its block is solid -
    /// which is why rock has to live on the rock, not around the corridor.
    /// </summary>
    public static DungeonPlan Generate(string seed, int depth, int rock = 1)
    {
        var random = Rng(seed);
        var plan = new DungeonPlan();
        for (int i = 0; i < 1024; i += 1) plan.Walls[i] = (byte)rock;   // solid rock to begin with
        int n = Cells(depth);
        int w = n * 2 - 1;
        // The maze hugs one side, so the rock left on the other is one unbroken band: the camp needs
        // a 3x3 of rock plus its whole ring, and a band split in two by the maze may not hold one.
        int free = 32 - 2 - w;
        int ox = random() < 0.5 ? 1 : 1 + free;
        int oy = 1 + (int)Math.Floor(random() * (free + 1));
        plan.Rect = (ox, oy, w, w);
        int Block(int cx, int cy) => ((oy + cy * 2) << 5) + ox + cx * 2;

        var seen = new byte[n * n];
        int startX = n / 2, startY = n / 2;
        var stack = new List<(int X, int Y)> { (startX, startY) };
        seen[startY * n + startX] = 1;
        plan.Walls[Block(startX, startY)] = 0;
        while (stack.Count != 0)
        {
            var (cx, cy) = stack[^1];
            var options = new List<(int X, int Y, int Dx, int Dy)>();
            for (int d = 0; d < 4; d += 1)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n || seen[ny * n + nx] != 0) continue;
                options.Add((nx, ny, Dx[d], Dy[d]));
            }
            if (options.Count == 0) { stack.RemoveAt(stack.Count - 1); continue; }
            var pick = options[(int)Math.Floor(random() * options.Count)];
            plan.Walls[Block(cx, cy) + pick.Dx + (pick.Dy << 5)] = 0;   // the block between two cells
            plan.Walls[Block(pick.X, pick.Y)] = 0;
            seen[pick.Y * n + pick.X] = 1;
            stack.Add((pick.X, pick.Y));
        }

        // Braid: a few extra ways through, so a floor is not one long corridor.
        int loops = JsMath.RoundToInt(n * n * 0.12);
        for (int i = 0; i < loops; i += 1)
        {
            int cx = (int)Math.Floor(random() * n);
            int cy = (int)Math.Floor(random() * n);
            int d = (int)Math.Floor(random() * 4);
            if (cx + Dx[d] < 0 || cy + Dy[d] < 0 || cx + Dx[d] >= n || cy + Dy[d] >= n) continue;
            plan.Walls[Block(cx, cy) + Dx[d] + (Dy[d] << 5)] = 0;
        }

        // A couple of open chambers, so the wide monsters have somewhere to stand.
        int rooms = 1 + n / 5;
        for (int i = 0; i < rooms; i += 1)
        {
            int cx = 1 + (int)Math.Floor(random() * (n - 2));
            int cy = 1 + (int)Math.Floor(random() * (n - 2));
            int centre = Block(cx, cy);
            for (int dy = -1; dy <= 1; dy += 1)
                for (int dx = -1; dx <= 1; dx += 1)
                {
                    int b = centre + dx + (dy << 5);
                    int bx = b & 31, by = b >> 5;
                    if (bx <= 0 || by <= 0 || bx >= 31 || by >= 31) continue;
                    plan.Walls[b] = 0;
                }
        }

        plan.Start = Block(startX, startY);
        plan.Dist[plan.Start] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(plan.Start);
        while (queue.Count != 0)
        {
            int b = queue.Dequeue();
            for (int d = 0; d < 4; d += 1)
            {
                int nb = b + Step[d];
                if (nb < 0 || nb > 1023 || plan.Walls[nb] != 0 || plan.Dist.ContainsKey(nb)) continue;
                plan.Dist[nb] = plan.Dist[b] + 1;
                queue.Enqueue(nb);
            }
        }
        plan.Cells = plan.Dist.Keys.ToList();
        plan.DeadEnds = plan.Cells.Where(b =>
        {
            int ways = 0;
            for (int d = 0; d < 4; d += 1) if (plan.Walls[b + Step[d]] == 0) ways += 1;
            return ways == 1;
        }).ToList();
        plan.Exit = plan.Start;
        int far = -1;
        foreach (int b in plan.DeadEnds.Count != 0 ? plan.DeadEnds : plan.Cells)
            if (plan.Dist[b] > far) { far = plan.Dist[b]; plan.Exit = b; }
        return plan;
    }
}

public sealed partial class LevelLoader
{
    /// <summary>A run through the pit: where the party came from, what the floor is, and what was
    /// borrowed from the level it is written over.</summary>
    public sealed class DungeonRun
    {
        public (int Level, int Block, int Direction) Home;
        public int Level, Depth;
        public string Seed = "", Objective = "";
        public int Need = 1, Kills, SigilsBefore, ExitBlock;
        public bool Ready;
        public DungeonPlan Plan;
        public readonly List<(int Block, int Axis)> Doors = new();
        public readonly List<DungeonLever> Levers = new();
        public int LeversPulled;
        public DungeonVault Vault;
        public readonly List<int> MadeItems = new();
        public DungeonBackup Backup;
    }

    public sealed class DungeonLever { public int Block, Dir; public bool Pulled; }
    public sealed class DungeonVault { public int Block, Axis, Room; public bool Open; }

    /// <summary>What the borrowed level owned before the floor was written over it.</summary>
    public sealed class DungeonBackup
    {
        public object Temp;
        public bool HadFlag;
        public byte[] Flags;
        public readonly List<(int Id, int Block, int X, int Y, int FlyingHeight, int ShpCurFrameFlg, int ItemPropertyIndex)> Items = new();
    }

    public DungeonRun Dungeon;

    public bool InDungeon => Dungeon != null;

    private static readonly int[] DungeonStep = { -32, 1, 32, -1 };

    /// <summary>
    /// Which wall types this level actually defines. The engine never clears the wall tables between
    /// levels, so entries left over from the level before are still sitting there - and drawing one
    /// gives a stained-glass window hung in a mine. The level's own LEVEL&lt;N&gt;.WLL is the only
    /// honest list.
    /// </summary>
    public HashSet<int> DungeonWallSet()
    {
        var out_ = new HashSet<int>();
        var file = _res.Exists($"LEVEL{Level}.WLL") ? _res.Get($"LEVEL{Level}.WLL") : null;
        if (file != null)
        {
            int count = (file.Length - 2) / 12;
            for (int i = 0; i < count; i += 1) out_.Add(file[2 + i * 12] | (file[3 + i * 12] << 8));
        }
        if (out_.Count == 0)
            for (int b = 0; b < 1024; b += 1)
                for (int d = 0; d < 4; d += 1) if (Map.Walls[b, d] != 0) out_.Add(Map.Walls[b, d]);
        return out_;
    }

    /// <summary>The borrowed level's own rock: the maze is drawn in that level's art, so nothing
    /// has to be minted the way the camp mints its doors.</summary>
    public int DungeonRockWall()
    {
        var count = new Dictionary<int, int>();
        for (int b = 0; b < 1024; b += 1)
            for (int d = 0; d < 4; d += 1)
            {
                int w = Map.Walls[b, d];
                if (w != 0) count[w] = count.GetValueOrDefault(w) + 1;
            }
        var own = DungeonWallSet();
        bool Usable(int w) => w > 0 && w != 0x1a && own.Contains(w) && Walls.VmpMap[w] != 0
            && (Walls.WallFlags[w] & 1) != 0 && Walls.SpecialTypes[w] == 0;
        int best = 0, most = -1;
        foreach (var (w, n) in count) if (Usable(w) && n > most) { most = n; best = w; }
        if (best != 0) return best;
        for (int w = 1; w < 256; w += 1) if (Usable(w)) return w;
        return 1;
    }

    /// <summary>Writes a generated floor over the level that is loaded now.</summary>
    public void DungeonApply(DungeonPlan plan)
    {
        for (int b = 0; b < 1024; b += 1)
        {
            byte w = plan.Walls[b];   // solid blocks carry the wall on every face, open ones none
            for (int d = 0; d < 4; d += 1) Map.Walls[b, d] = w;
            Map.AssignedObjects[b] = 0;
            Map.DrawObjects[b] = 0;
            Map.Direction[b] = 5;
            // Every flag of the old map goes: what was explored, what was sealed, and the
            // invisible-wall bookkeeping. Only the automap bit the wall type implies is re-derived.
            Map.Flags[b] = (byte)(Walls.Automap[w] == 17 ? 0x20 : 0);
        }
        CompleteDoorOperations();   // no door of the old map is still moving
    }

    /// <summary>The kinds this level can actually draw: a property record whose shapes were never
    /// loaded is a placeholder, and spawning it takes the renderer down on the first visible frame.</summary>
    public List<int> DungeonTypes()
    {
        var out_ = new List<int>();
        for (int i = 0; i < Board.Properties.Length; i += 1)
        {
            var p = Board.Properties[i];
            if (p == null || p.HitPoints == 0 || p.MaxWidth == 0) continue;
            int frames = 0;
            for (int f = 0; f < 16; f += 1) if (Board.Shapes[(p.ShapeIndex << 4) + f] != null) frames += 1;
            if (frames == 16) out_.Add(i);
        }
        return out_;
    }

    private bool DungeonOpen(int b)
    {
        for (int d = 0; d < 4; d += 1) if (Map.Walls[b, d] != 0) return false;
        return true;
    }

    /// <summary>What the borrowed level can furnish a floor with: its own doors, levers and
    /// decorated walls.</summary>
    public (int Door, int Lever, List<int> Decor) DungeonFurnish()
    {
        var own = DungeonWallSet();
        int door = 0;
        for (int w = 1; w < 256 && door == 0; w += 1)
        {
            if (!own.Contains(w)) continue;                        // a leftover from another level
            if (Walls.SpecialTypes[w] != 5) continue;              // a door with a switch to click
            int flags = Walls.WallFlags[w];
            if ((flags & 8) == 0 || (flags & 0x20) == 0) continue;  // a door, and shut
            if ((flags & 1) == 0) continue;                        // solid while it is shut
            door = w;
        }
        // Decoration: only walls that are plain walls in every other respect. A level's "decorated"
        // types also cover windows, archways and gateways, and those look absurd in a corridor -
        // the automap byte is what tells them apart (255 = draws as ordinary wall).
        var decor = new List<int>();
        for (int w = 1; w < 256; w += 1)
        {
            if (!own.Contains(w)) continue;
            if (Walls.SpecialTypes[w] != 1 || Walls.WallFlags[w] != 7 || Walls.Automap[w] != 255) continue;
            if (Walls.VmpMap[w] == 0 || Walls.ShapeMap[w] == 0) continue;
            decor.Add(w);
        }
        // Levers: only the level's own. Minting one from a decoration put a stained-glass window on
        // the wall and called it a lever, which is not the kind of nonsense this floor needs.
        int lever = 0;
        for (int w = 1; w < 255 && lever == 0; w += 1)
        {
            if (!own.Contains(w) || !own.Contains(w + 1)) continue;
            if (Walls.SpecialTypes[w] == 2 && Walls.SpecialTypes[w + 1] == 3 && Walls.VmpMap[w] != 0) lever = w;
        }
        return (door, lever, decor);
    }

    /// <summary>Doors, levers, a sealed vault and a bit of decoration, so a floor is not bare
    /// corridor.</summary>
    public void DungeonDress(DungeonPlan plan, string seed)
    {
        var d = Dungeon;
        if (d == null) return;
        var random = DungeonFloor.Rng($"{seed}:dress");
        var kit = DungeonFurnish();
        bool Solid(int b) => !DungeonOpen(b);
        d.Doors.Clear();
        d.Levers.Clear();
        d.LeversPulled = 0;
        d.Vault = null;

        // Doors sit in a straight stretch of corridor: the two faces along it carry the door, the
        // two across it stay rock, which is how the original levels build one.
        var straights = plan.Cells.Where(b =>
        {
            bool ns = !Solid(b - 32) && !Solid(b + 32) && Solid(b - 1) && Solid(b + 1);
            bool ew = !Solid(b - 1) && !Solid(b + 1) && Solid(b - 32) && Solid(b + 32);
            return (ns || ew) && b != plan.Start;
        }).ToList();

        (int Block, int Axis) HangDoor(int b, int type)
        {
            bool ns = !Solid(b - 32);
            int rock = DungeonRockWall();
            Map.Walls[b, ns ? 0 : 1] = (byte)type;
            Map.Walls[b, ns ? 2 : 3] = (byte)type;
            Map.Walls[b, ns ? 1 : 0] = (byte)rock;
            Map.Walls[b, ns ? 3 : 2] = (byte)rock;
            Map.Direction[b] = 5;
            return (b, ns ? 0 : 1);
        }

        // A door is only ever a shortcut, never a gate across the floor: if shutting one would cut
        // any part of the maze off from the start, it is not hung at all.
        HashSet<int> Reaches(HashSet<int> closed)
        {
            var seen = new HashSet<int> { plan.Start };
            var queue = new Queue<int>();
            queue.Enqueue(plan.Start);
            while (queue.Count != 0)
            {
                int b = queue.Dequeue();
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = b + DungeonStep[dir];
                    if (nb < 0 || nb > 1023 || seen.Contains(nb) || closed.Contains(nb) || Solid(nb)) continue;
                    seen.Add(nb);
                    queue.Enqueue(nb);
                }
            }
            return seen;
        }

        if (kit.Door != 0)
        {
            var shut = new HashSet<int>();
            int wanted = Math.Min(straights.Count, 2 + d.Depth / 2);
            for (int i = 0; i < wanted && straights.Count != 0; i += 1)
            {
                int pick = (int)Math.Floor(random() * straights.Count);
                int at = straights[pick];
                straights.RemoveAt(pick);
                shut.Add(at);
                if (Reaches(shut).Count < plan.Cells.Count - shut.Count) { shut.Remove(at); continue; }
                d.Doors.Add(HangDoor(at, kit.Door));
            }
        }

        // The vault: a dead end shut behind a door that only the levers open.
        var ends = plan.DeadEnds.Where(b => b != plan.Start && !d.Doors.Any(x => x.Block == b)).ToList();
        if (kit.Door != 0 && kit.Lever != 0 && ends.Count != 0)
        {
            int vaultRoom = ends[(int)Math.Floor(random() * ends.Count)];
            int mouth = -1;                        // the corridor block leading to it carries the door
            for (int dir = 0; dir < 4; dir += 1)
            {
                int nb = vaultRoom + DungeonStep[dir];
                if (!Solid(nb)) { mouth = nb; break; }
            }
            if (mouth >= 0 && plan.Cells.Contains(mouth))
            {
                var hung = HangDoor(mouth, kit.Door);
                d.Vault = new DungeonVault { Block = hung.Block, Axis = hung.Axis, Room = vaultRoom, Open = false };
                // Levers on the rock beside a few corridor blocks - never inside the vault and never
                // on the block that holds its door: a lever you cannot reach until the vault is open
                // is a floor that cannot be finished.
                var sealed_ = new HashSet<int> { vaultRoom, mouth };
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = vaultRoom + DungeonStep[dir];
                    if (nb != mouth) sealed_.Add(nb);
                }
                var spots = plan.Cells.Where(b => b != plan.Start && !sealed_.Contains(b)).ToList();
                int wanted = Math.Min(3, 1 + d.Depth / 4 + 1);
                for (int i = 0; i < wanted && spots.Count != 0; i += 1)
                {
                    int pick = (int)Math.Floor(random() * spots.Count);
                    int at = spots[pick];
                    spots.RemoveAt(pick);
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        int nb = at + DungeonStep[dir];
                        if (!Solid(nb)) continue;
                        Map.Walls[nb, dir ^ 2] = (byte)kit.Lever;
                        Map.Direction[nb] = 5;
                        d.Levers.Add(new DungeonLever { Block = nb, Dir = dir ^ 2, Pulled = false });
                        break;
                    }
                }
            }
        }

        // Decoration: a share of the rock the party can actually see gets one of the level's own
        // decorated wall types.
        if (kit.Decor.Count != 0)
        {
            foreach (int b in plan.Cells)
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = b + DungeonStep[dir];
                    if (nb < 0 || nb > 1023 || !Solid(nb) || random() > 0.08) continue;
                    if (Walls.SpecialTypes[Map.Walls[nb, dir ^ 2]] != 0) continue;   // never over a door or a lever
                    Map.Walls[nb, dir ^ 2] = (byte)kit.Decor[(int)Math.Floor(random() * kit.Decor.Count)];
                    Map.Direction[nb] = 5;
                }
        }
        InvalidateDrawOrder();
    }

    /// <summary>A lever in the pit: the engine has already flipped its art, this counts it and
    /// opens the vault when every one of them is down. Returns 1 when it was one of ours.</summary>
    public int DungeonPullLever(int block, int dir)
    {
        var d = Dungeon;
        var lever = d?.Levers.FirstOrDefault(l => l.Block == block && l.Dir == dir);
        if (lever == null) return 0;
        lever.Pulled = !lever.Pulled;
        d.LeversPulled = d.Levers.Count(l => l.Pulled);
        if (d.Vault != null && !d.Vault.Open && d.LeversPulled == d.Levers.Count)
        {
            d.Vault.Open = true;
            OpenCloseDoor(d.Vault.Block, 1);
        }
        return 1;
    }

    /// <summary>The vault door stays shut until the levers say otherwise.</summary>
    public bool DungeonDoorLocked(int block)
        => Dungeon?.Vault != null && Dungeon.Vault.Block == block && !Dungeon.Vault.Open;

    /// <summary>Blocks a monster must not be spawned on: behind the vault door, and the party's
    /// own arrival.</summary>
    public HashSet<int> DungeonSealed()
    {
        var out_ = new HashSet<int>();
        var d = Dungeon;
        if (d == null) return out_;
        if (d.Plan != null) out_.Add(d.Plan.Start);
        if (d.Vault != null)
        {
            out_.Add(d.Vault.Block);
            out_.Add(d.Vault.Room);
            for (int dir = 0; dir < 4; dir += 1) out_.Add(d.Vault.Room + DungeonStep[dir]);
        }
        return out_;
    }

    /// <summary>Fills the floor with monsters of the borrowed level's own kinds.</summary>
    public int DungeonPopulate(int count, int depth, string seed)
    {
        var plan = Dungeon?.Plan;
        if (plan == null) return 0;
        var random = DungeonFloor.Rng($"{seed}:monsters");
        var types = DungeonTypes();
        if (types.Count == 0) return 0;
        var sealed_ = DungeonSealed();
        var spots = plan.Cells.Where(b => plan.Dist.GetValueOrDefault(b) >= 3 && !sealed_.Contains(b)).ToList();
        // Narrow kinds first: a wide monster does not fit a one-block corridor, and a floor that
        // spawns one monster instead of eight is a floor that cannot be finished.
        var bySize = types.OrderBy(t => Board.Properties[t].MaxWidth).ToList();
        int placed = 0, tries = 0;
        while (placed < count && tries < Math.Max(60, count * 12) && spots.Count != 0)
        {
            tries += 1;
            int at = (int)Math.Floor(random() * spots.Count);
            int block = spots[at];
            int facing = ((int)Math.Floor(random() * 4) << 1) & 6;
            var order = random() < 0.5 ? Shuffle(types, random) : bySize;
            int index = -1;
            foreach (int type in order)
            {
                index = Board.Init(new[] { block, 0x80, 0x80, facing, type, 0, 0, 0, 0, 0, 0 }, Level);
                if (index != -1) break;
            }
            if (index == -1) { spots.RemoveAt(at); continue; }   // nothing of this level fits here
            spots.RemoveAt(at);                                  // one to a block
            var m = Board.Monsters[index];
            m.NgPlus = depth >= 9 ? (depth - 6) / 3 : 0;
            // Everything down here is already hunting: a pit floor is not a museum.
            Board.SetMonsterMode(m, 7);
            placed += 1;
        }
        DungeonBalance(depth);
        return placed;
    }

    /// <summary>JavaScript's sort with a random comparator, near enough: the engine only wants the
    /// kinds tried in an unpredictable order, not a particular permutation.</summary>
    private static List<int> Shuffle(List<int> source, Func<double> random)
    {
        var list = source.ToList();
        for (int i = list.Count - 1; i > 0; i -= 1)
        {
            int j = (int)Math.Floor(random() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// A floor borrows whatever the level keeps, and a level's monsters are pitched at the party
    /// that belongs there. So every monster on a floor is measured against the party that walked
    /// in: how hard it hits, and how long it stands.
    /// </summary>
    public void DungeonBalance(int depth)
    {
        var heroes = new List<int>();
        for (int c = 0; c < 4; c += 1) if (Characters[c].Active) heroes.Add(c);
        if (heroes.Count == 0) return;
        double avgHp = heroes.Sum(c => (double)Characters[c].HitPointsMax) / heroes.Count;
        // A hit should cost about a twelfth of a hero on floor one, a third deeper down.
        double wantDamage = Math.Max(4, avgHp * Math.Min(0.34, 0.08 + 0.035 * (depth - 1)));
        foreach (var m in Board.Monsters)
        {
            if (m?.Properties == null || m.HitPoints <= 0 || m.Mode > 13) continue;
            int id = m.Id | 0x8000;
            double swing = Math.Max(1, Board.CalcInflictableDamage(id, heroes[0], 1)) + 1;   // an average roll
            m.PitScale = Math.Max(0.12, Math.Min(2, wantDamage / swing));
            if (m.DungeonBoss) m.PitScale = Math.Min(2.5, m.PitScale * 1.3);
            // And it should take a few good swings to put down: more of them the deeper the floor.
            // What counts is the damage a swing is *expected* to do, hit chance included - against a
            // kind the party lands one blow in thirty on, sizing by damage alone leaves a monster
            // nothing can kill. Enough samples to measure a chance as low as one in fifty.
            double Landed(int c)
            {
                int n = 0;
                for (int i = 0; i < 200; i += 1) if (Board.BattleHitSkillTest(c, id, 0) != 0) n += 1;
                return n / 200.0;
            }
            double perSwing = heroes.Max(c => Landed(c) * Board.CalcInflictableDamage(c, id, 1));
            double swings = (m.DungeonBoss ? 12 : 4) + depth * 0.5;
            // What the monster is normally worth is a ceiling, not a floor: those hit points belong
            // to the level the floor borrowed its art from, not to the party that walked in.
            double byBase = m.Properties.HitPoints * Math.Min(1.2, 0.2 + 0.08 * depth) * (m.DungeonBoss ? 2.5 : 1);
            // Nothing the party swings can touch this kind: it still may not become an immovable
            // wall, so size it as if each swing were worth a single point.
            double byParty = (perSwing > 0 ? perSwing : 1) * swings;
            m.HitPoints = (int)Math.Max(8, JsMath.Round(Math.Min(m.Properties.HitPoints * 2.5, Math.Min(byBase, byParty))));
        }
    }

    /// <summary>One tougher monster at the far end of the floor.</summary>
    public int DungeonBoss(int depth, string seed)
    {
        var plan = Dungeon?.Plan;
        if (plan == null) return 0;
        var types = DungeonTypes();
        if (types.Count == 0) return 0;
        int type = types.Aggregate(types[0], (best, i) => Board.Properties[i].HitPoints > Board.Properties[best].HitPoints ? i : best);
        var random = DungeonFloor.Rng($"{seed}:boss");
        var sealed_ = DungeonSealed();
        var spots = new[] { plan.Exit }.Concat(plan.DeadEnds).Concat(plan.Cells).Where(b => !sealed_.Contains(b)).ToList();
        foreach (int block in spots)
        {
            int index = Board.Init(new[] { block, 0x80, 0x80, ((int)Math.Floor(random() * 4) << 1) & 6, type, 0, 0, 0, 0, 0, 0 }, Level);
            if (index == -1) continue;
            var m = Board.Monsters[index];
            m.NgPlus = depth >= 9 ? (depth - 6) / 3 : 0;
            m.DungeonBoss = true;
            Board.SetMonsterMode(m, 7);
            DungeonBalance(depth);
            return index;
        }
        return -1;
    }

    /// <summary>Drops one of the port's own items on the floor (the sigil an objective asks for).</summary>
    public int DungeonPlaceItem(int prop, int block)
    {
        if (Dungeon == null) return 0;
        int item = Items.Make(prop, 0, 0, Level);
        if (item == -1) return 0;
        var (x, y) = Party.CalcCoordinates(block, 0x80, 0x80);
        Board.SetItemPosition(item, x, y, 0, true);
        Dungeon.MadeItems.Add(item);
        return item;
    }

    /// <summary>Copies the borrowed level out of the way, then writes the floor over it.</summary>
    public int DungeonEnter(int level, int depth, string seed, string objective)
    {
        if (Dungeon != null || level == 0 || level == Level) return 0;
        var home = (Level, Party.Block, Party.Direction);
        var backup = new DungeonBackup
        {
            Temp = TempDataFor(level),
            HadFlag = (HasTempDataFlags & (1 << (level - 1))) != 0,
            Flags = (byte[])Flags.Clone(),
        };
        LoadNewLevel(level, 528, 0);
        // A revisit re-runs the level's .INF entry function; it must not leave quest flags behind.
        Array.Copy(backup.Flags, Flags, Flags.Length);
        // Every block script of the borrowed level is silenced from here on: its teleports, traps
        // and cutscenes belong to a map that no longer exists.
        LevelScript = null;
        // The real floor items are taken out of the way (and remembered) before a corridor is
        // carved over them.
        for (int b = 0; b < 1024; b += 1)
        {
            var chain = new List<int>();
            int cur = Map.AssignedObjects[b];
            int guard = 0;
            while (cur != 0 && guard++ < 64)
            {
                var obj = Board.Find(cur);
                if ((cur & 0x8000) == 0 && Items.InPlay[cur] != null && Items.InPlay[cur].ItemPropertyIndex != 0) chain.Add(cur);
                cur = obj.NextAssignedObject;
            }
            foreach (int id in chain)
            {
                var it = Items.InPlay[id];
                backup.Items.Add((id, b, it.X, it.Y, it.FlyingHeight, it.ShpCurFrameFlg, it.ItemPropertyIndex));
                Board.RemoveLevelItem(id, b);
            }
        }
        for (int i = 0; i < 30; i += 1)
        {
            var m = Board.Monsters[i];
            if (m != null && m.Block != 0) Board.Place(m, 0, 0);
            // Whatever this monster was carrying goes with it. Item records are a fixed table of a
            // few hundred shared by the whole world; dropping a monster without freeing them leaks a
            // handful every time a floor is built, and a few dozen floors later the table is full and
            // the next level to load dies with "Out of item slots".
            int held = m?.AssignedItems ?? 0;
            int guard = 0;
            while (held != 0 && guard++ < 32)
            {
                int next = Items.InPlay[held] != null ? Items.InPlay[held].NextAssignedObject : 0;
                Items.Delete(held);
                held = next;
            }
            Board.Monsters[i] = new Monster { Id = i, Mode = 0x10, Block = 0 };
        }
        var plan = DungeonFloor.Generate(seed, depth, DungeonRockWall());
        Dungeon = new DungeonRun
        {
            Home = home, Level = level, Depth = depth, Seed = seed, Objective = objective,
            Need = 1, Ready = false, Backup = backup, Plan = plan, ExitBlock = plan.Exit,
        };
        DungeonApply(plan);
        DungeonDress(plan, seed);
        Party.MoveTo(plan.Start);
        Party.Direction = 0;
        PartyBlock = plan.Start;
        PartyDirection = 0;
        InvalidateDrawOrder();
        Gui?.DrawScene(0);
        return 1;
    }

    /// <summary>Puts the borrowed level back exactly as it was and returns the party home.</summary>
    public int DungeonLeave()
    {
        var d = Dungeon;
        if (d == null) return 0;
        if (Gui?.Camp != null) Gui.LeaveCamp();
        // Anything the floor made and nobody picked up goes with the floor.
        var carried = new HashSet<int>(Items.Inventory) { Gui?.ItemInHand ?? 0 };
        foreach (var ch in Characters) if (ch.Active) foreach (int it in ch.Items) carried.Add(it);
        foreach (int item in d.MadeItems)
        {
            if (carried.Contains(item) || Items.InPlay[item] == null) continue;
            if (Items.InPlay[item].Block != 0) Board.RemoveLevelItem(item, Items.InPlay[item].Block);
            Items.Delete(item);
        }
        // Everything else still lying on the floor of the pit goes with the pit too. Only what the
        // party carries survives; the real level's own items are put back from the backup below.
        var restored = new HashSet<int>();
        foreach (var rec in d.Backup.Items) restored.Add(rec.Id);
        for (int b = 0; b < 1024; b += 1)
        {
            int cur = Map.AssignedObjects[b];
            int guard = 0;
            var loose = new List<int>();
            while (cur != 0 && guard++ < 64)
            {
                var obj = Board.Find(cur);
                if ((cur & 0x8000) == 0 && Items.InPlay[cur] != null && Items.InPlay[cur].ItemPropertyIndex != 0
                    && !carried.Contains(cur) && !restored.Contains(cur)) loose.Add(cur);
                cur = obj.NextAssignedObject;
            }
            foreach (int id in loose) { Board.RemoveLevelItem(id, b); Items.Delete(id); }
        }
        int level = d.Level;
        Dungeon = null;   // before the teleport: nothing must treat the way home as dungeon ground
        LoadNewLevel(d.Home.Level, d.Home.Block, d.Home.Direction);
        // The teleport snapshotted the floor into the borrowed level's temp data; throw that away.
        RestoreTempData(level, d.Backup.Temp, d.Backup.HadFlag);
        foreach (var rec in d.Backup.Items)
        {
            var it = Items.InPlay[rec.Id];
            if (it == null) continue;
            it.Level = level;
            it.Block = rec.Block;
            it.X = rec.X;
            it.Y = rec.Y;
            it.FlyingHeight = rec.FlyingHeight;
            it.ShpCurFrameFlg = rec.ShpCurFrameFlg;
            it.ItemPropertyIndex = rec.ItemPropertyIndex;
            it.NextAssignedObject = 0;
            it.NextDrawObject = 0;
        }
        InvalidateDrawOrder();
        return 1;
    }
}
