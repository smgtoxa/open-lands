// src/game/dungeon.mjs
// The Imp's Pit: an endless run of generated floors (a port addition).
//
// A floor is not a new level format. It is a level the party has already walked, with its whole
// 32x32 block map rewritten in memory - the same trick src/game/camp.mjs plays on three blocks,
// done to all 1024. Everything the borrowed level owns (its remembered walls and monsters, its
// spawn set, its floor items, its quest flags) is copied out first and put back when the party
// leaves, so the real level is never changed by a run through the pit.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>dungeon.mjs generateFloor: rect</summary>
    public sealed class FloorRect
    {
        public int x, y, w, h;
    }

    /// <summary>dungeon.mjs generateFloor: { walls, start, exit, cells, deadEnds, dist, rect }</summary>
    public sealed class GeneratedFloor
    {
        public byte[] walls;
        public int start, exit;
        public List<int> cells, deadEnds;
        /// <summary>a Map: its keys are the cells, in the order the search reached them</summary>
        public Dictionary<int, int> dist;
        public FloorRect rect;
        // Unity build: the gate between the start side and the stairs side (a straight corridor block every way to
        // the stairs passes through; -1 when the floor is too small for one), the blocks beyond it, and the alcoves
        // hidden behind sliding walls
        public int gate = -1;
        public HashSet<int> regionB = new HashSet<int>();
        public List<PitAlcove> alcoves = new List<PitAlcove>();
    }

    /// <summary>A dead-end branch walled off by a stone that slides away when clicked.</summary>
    public sealed class PitAlcove
    {
        public int secret;
        public List<int> cells = new List<int>();
    }

    /// <summary>The gate: a door (or, where the level has none, a sealed wall) that the floor's puzzle opens.</summary>
    public sealed class PitGate
    {
        public int block, axis;
        public bool open, door, locked;
    }

    public sealed class PitSecret
    {
        public int block;
        public bool open;
    }

    public sealed class PitSwitch
    {
        public int block, dir;
        public bool pressed;
    }

    /// <summary>A trap in the floor: "teleport" moves the party to `to`, "spin" turns it about.</summary>
    public sealed class PitTrap
    {
        public int block, to;
        public string kind;
    }

    /// <summary>dungeon.mjs OBJECTIVES</summary>
    public sealed class DungeonObjective
    {
        public string id, label;
    }

    /// <summary>dungeon.mjs floorPlan (also what uiDungeonEnter takes)</summary>
    public sealed class FloorPlan
    {
        public int floor, depth, level;
        public string objective;
        public int need;
        public string seed;
        public int cells;
    }

    /// <summary>dungeon.mjs initDungeonRun: this.dungeonRun</summary>
    public sealed class DungeonRun
    {
        public int cleared, runs;
        public JsonObject best;
    }

    /// <summary>dungeon.mjs uiDungeonEnter: dungeon.home</summary>
    public sealed class DungeonHome
    {
        public int level, block, direction;
    }

    /// <summary>dungeon.mjs uiDungeonEnter: dungeon.backup.items entries</summary>
    public sealed class DungeonItemRecord
    {
        public int id, block, x, y, flyingHeight, shpCurFrame_flg, itemPropertyIndex;
    }

    /// <summary>dungeon.mjs uiDungeonEnter: dungeon.backup</summary>
    public sealed class DungeonBackup
    {
        public LvlTempData temp;
        public bool hadFlag;
        public Monster[] spawns;
        public byte[] flags;
        public List<DungeonItemRecord> items;
    }

    /// <summary>dungeon.mjs uiDungeonDress: hangDoor</summary>
    public sealed class DungeonDoor
    {
        public int block, axis;
    }

    /// <summary>dungeon.mjs uiDungeonDress: dungeon.levers entries</summary>
    public sealed class DungeonLever
    {
        public int block, dir;
        public bool pulled;
    }

    /// <summary>dungeon.mjs uiDungeonDress: dungeon.vault ({ ...hung, room, open })</summary>
    public sealed class DungeonVault
    {
        public int block, axis, room;
        public bool open;
    }

    /// <summary>dungeon.mjs uiDungeonEnter / uiDungeonLoad: this.dungeon</summary>
    public sealed class DungeonState
    {
        public DungeonHome home;
        public int level, depth;
        public string seed, objective;
        public int need;
        public bool ready;
        public DungeonBackup backup;
        public GeneratedFloor plan;
        public List<int> madeItems;
        public int exitBlock, kills, sigilsBefore;
        public List<DungeonDoor> doors;
        public List<DungeonLever> levers;
        public int leversPulled;
        public DungeonVault vault;
        // Unity build: the gate, its puzzle and the floor's hidden things
        public PitGate gate;
        public List<PitSecret> secrets = new List<PitSecret>();
        public PitSwitch switchWall;
        public List<PitTrap> traps = new List<PitTrap>();
        public int stairs;
        public bool floorDone;
        public int bossMaxHp, bossBlock;
        public bool bossEnraged;
        public int clueWall, switchType;
        public List<int> spots = new List<int>();
    }

    /// <summary>dungeon.mjs uiDungeonInfo</summary>
    public sealed class DungeonInfo
    {
        public int depth, level;
        public string objective;
        public int need, exitBlock;
        public bool ready;
        public int monstersLeft;
        public bool bossAlive;
        public int kills, sigilsBefore, levers, leversPulled;
        public bool vaultOpen;
        public bool gateOpen, gateLocked, floorDone;
        public int stairs, secretsFound, secrets;
    }

    /// <summary>dungeon.mjs uiDungeonMarks entries</summary>
    public sealed class DungeonMark
    {
        public int block;
        public string kind;
    }

    /// <summary>dungeon.mjs uiDungeonFurnish</summary>
    public sealed class DungeonKit
    {
        public int door, lever;
        public List<int> decor;
    }

    public sealed partial class LandsOfLore
    {
        // A small seeded PRNG, the same one the randomizer uses (mods.mjs).
        public static Func<double> dungeonRng(string seedText)
        {
            uint h = 2166136261;
            foreach (char ch in seedText) { h ^= ch; h = (uint)Js.Imul((int)h, 16777619); }
            return () =>
            {
                h = unchecked(h + 0x6d2b79f5);
                uint t = h;
                t = (uint)Js.Imul((int)(t ^ (t >> 15)), (int)(t | 1));
                t ^= unchecked(t + (uint)Js.Imul((int)(t ^ (t >> 7)), (int)(t | 61)));
                return (t ^ (t >> 14)) / 4294967296.0;
            };
        }

        // Cells across (not blocks): a floor grows with depth. 7 cells = 13 blocks, 13 cells = 25 blocks.
        public static int floorCells(int depth)
        {
            if (depth <= 3) return 7;
            if (depth <= 8) return 9;
            if (depth <= 15) return 11;
            return 13;
        }

        static readonly int[] STEP = { -32, 1, 32, -1 }; // block offset per direction (N, E, S, W)

        // Generates a floor. Pure: no engine, no globals, so the tests can check it without game data.
        //
        // The map is written the way the original levels are: a solid block carries the wall on all four of
        // its faces, an open block carries nothing. Walking is blocked by the *destination* block's face
        // (testWallFlag), and a monster can only stand where no face of its block is solid - which is why
        // rock has to live on the rock, not around the corridor.
        //
        // Unity build: the maze is cut in two by a gate - a straight corridor block that every way to the stairs
        // passes through - so the far side (the stairs, and the master's lair) is earned by solving the floor.
        // Loops and chambers are only ever added within one side. A few dead-end branches are walled off by a
        // stone that slides away when clicked: the floor's hidden alcoves.
        public static GeneratedFloor generateFloor(string seed, int depth, int rock = 1)
        {
            var random = dungeonRng(seed);
            var walls = new byte[1024];
            Js.Fill(walls, (byte)rock); // everything is solid rock to begin with
            int n = floorCells(depth);
            int w = n * 2 - 1; // blocks across
            // The maze hugs one side, so the rock left on the other is one unbroken band: the camp needs a
            // 3x3 of rock plus its whole ring, and a band split in two by the maze may not hold one.
            int free = 32 - 2 - w;
            int ox = random() < 0.5 ? 1 : 1 + free;
            int oy = 1 + Js.Floor(random() * (free + 1));
            var rect = new FloorRect { x = ox, y = oy, w = w, h = w };
            Func<int, int, int> block = (cx, cy) => ((rect.y + cy * 2) << 5) + rect.x + cx * 2;
            Func<int, int> wallAt = (i) => i >= 0 && i < 1024 ? walls[i] : 0;
            Func<int, bool> open = (i) => i >= 0 && i < 1024 && walls[i] == 0;
            Func<int, int> degree = (b) => new[] { 0, 1, 2, 3 }.Count(d => open(b + STEP[d]));

            // a perfect maze first: one way between any two cells
            var seen = new byte[n * n];
            int[] startCell = { Js.FloorDiv(n, 2), Js.FloorDiv(n, 2) };
            var stack = new List<int[]> { startCell };
            seen[startCell[1] * n + startCell[0]] = 1;
            walls[block(startCell[0], startCell[1])] = 0;
            while (stack.Count > 0)
            {
                int cx = stack[stack.Count - 1][0], cy = stack[stack.Count - 1][1];
                var options = new List<int[]>();
                for (int d = 0; d < 4; d += 1)
                {
                    int dx = new[] { 0, 1, 0, -1 }[d];
                    int dy = new[] { -1, 0, 1, 0 }[d];
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n || seen[ny * n + nx] != 0) continue;
                    options.Add(new[] { nx, ny, dx, dy });
                }
                if (options.Count == 0) { stack.RemoveAt(stack.Count - 1); continue; }
                var o = options[Js.Floor(random() * options.Count)];
                walls[block(cx, cy) + o[2] + (o[3] << 5)] = 0; // the block between the two cells
                walls[block(o[0], o[1])] = 0;
                seen[o[1] * n + o[0]] = 1;
                stack.Add(new[] { o[0], o[1] });
            }
            int start = block(startCell[0], startCell[1]);

            Func<int, int, Dictionary<int, int>> distances = (from, skip) =>
            {
                var dist0 = new Dictionary<int, int> { [from] = 0 };
                var q = new Queue<int>();
                q.Enqueue(from);
                while (q.Count > 0)
                {
                    int b = q.Dequeue();
                    for (int d = 0; d < 4; d += 1)
                    {
                        int nb = b + STEP[d];
                        if (!open(nb) || nb == skip || dist0.ContainsKey(nb)) continue;
                        dist0[nb] = dist0[b] + 1;
                        q.Enqueue(nb);
                    }
                }
                return dist0;
            };

            // the gate: a corridor block (between two cells) on the one way to the farthest dead end, well along it
            var treeDist = distances(start, -1);
            int far = start, farD = -1;
            foreach (var kv in treeDist) if (degree(kv.Key) == 1 && kv.Value > farD) { farD = kv.Value; far = kv.Key; }
            var path = new List<int>();
            for (int b = far; b != start;)
            {
                path.Add(b);
                int next = b;
                for (int d = 0; d < 4; d += 1) { int nb = b + STEP[d]; if (treeDist.TryGetValue(nb, out int nd) && nd == treeDist[b] - 1) { next = nb; break; } }
                if (next == b) break;
                b = next;
            }
            Func<int, bool> isLink = (b) => (((b & 31) - rect.x) & 1) != (((b >> 5) - rect.y) & 1);
            var gateChoices = path.Where(b => isLink(b) && treeDist[b] >= Math.Max(2, farD * 0.45) && treeDist[b] <= farD * 0.8).ToList();
            if (gateChoices.Count == 0) gateChoices = path.Where(b => isLink(b) && treeDist[b] >= 2 && b != far).ToList();
            int gate = gateChoices.Count != 0 ? gateChoices[Js.Floor(random() * gateChoices.Count)] : -1;
            var sideB = gate >= 0 ? new HashSet<int>(distances(far, gate).Keys) : new HashSet<int>();
            Func<int, int> side = (b) => sideB.Contains(b) ? 1 : 0;

            // Braid: a few extra ways through, so a floor is not one long corridor - but only within one side, so
            // the gate stays the one way across.
            int loops = Js.Round(n * n * 0.12);
            for (int i = 0; i < loops; i += 1)
            {
                int cx = Js.Floor(random() * n);
                int cy = Js.Floor(random() * n);
                int d = Js.Floor(random() * 4);
                int dx = new[] { 0, 1, 0, -1 }[d];
                int dy = new[] { -1, 0, 1, 0 }[d];
                if (cx + dx < 0 || cy + dy < 0 || cx + dx >= n || cy + dy >= n) continue;
                int a0 = block(cx, cy), b0 = block(cx + dx, cy + dy), link = a0 + dx + (dy << 5);
                if (link == gate || side(a0) != side(b0)) continue;
                walls[link] = 0;
                if (side(a0) == 1) sideB.Add(link);
            }

            // A couple of open chambers, so the wide monsters have somewhere to stand and a floor is not all
            // corridor (each one wholly on one side, and never around the gate).
            int rooms = 1 + Js.FloorDiv(n, 5);
            for (int i = 0; i < rooms; i += 1)
            {
                int cx = 1 + Js.Floor(random() * (n - 2));
                int cy = 1 + Js.Floor(random() * (n - 2));
                int centre = block(cx, cy);
                int sd = side(centre);
                bool ok = true;
                for (int d = 0; d < 4 && ok; d += 1)
                {
                    int dx = new[] { 0, 1, 0, -1 }[d], dy = new[] { -1, 0, 1, 0 }[d];
                    if (side(block(cx + dx, cy + dy)) != sd || centre + dx + (dy << 5) == gate) ok = false;
                }
                if (!ok) continue;
                for (int dy = -1; dy <= 1; dy += 1) for (int dx = -1; dx <= 1; dx += 1)
                    {
                        int b = centre + dx + (dy << 5);
                        int bx = b & 31;
                        int by = b >> 5;
                        if (bx <= 0 || by <= 0 || bx >= 31 || by >= 31 || b == gate) continue;
                        walls[b] = 0;
                        if (sd == 1) sideB.Add(b);
                    }
            }
            // the gate keeps rock on both of its sides: it is a door in a straight corridor and nothing else
            if (gate >= 0)
            {
                bool ns = (((gate >> 5) - rect.y) & 1) == 1;   // a link between two cells one above the other: a north-south corridor
                if (ns) { walls[gate - 1] = (byte)rock; walls[gate + 1] = (byte)rock; }
                else { walls[gate - 32] = (byte)rock; walls[gate + 32] = (byte)rock; }
                sideB = new HashSet<int>(distances(far, gate).Keys);
                if (sideB.Contains(start)) { gate = -1; sideB.Clear(); }   // never: but a floor must not lock its own start away
            }

            // Hidden alcoves: dead-end branches on the start side, walled off where they leave the corridor.
            var alcoves = new List<PitAlcove>();
            int wantAlcoves = Math.Min(4, 1 + Js.FloorDiv(depth, 4));
            var ends = distances(start, gate).Keys.Where(b => b != start && degree(b) == 1 && !sideB.Contains(b)).ToList();
            pitShuffle(ends, random);
            var taken = new HashSet<int>();
            foreach (int e in ends)
            {
                if (alcoves.Count >= wantAlcoves) break;
                var branch = new List<int> { e };
                int prev = e, cur = -1;
                for (int d = 0; d < 4; d += 1) if (open(e + STEP[d])) cur = e + STEP[d];
                while (cur >= 0 && degree(cur) == 2 && cur != start && cur != gate && branch.Count < 6)
                {
                    branch.Add(cur);
                    int next = -1;
                    for (int d = 0; d < 4; d += 1) { int nb = cur + STEP[d]; if (open(nb) && nb != prev) next = nb; }
                    prev = cur; cur = next;
                }
                if (branch.Count < 2 || branch.Any(taken.Contains)) continue;
                int secretBlock = branch[branch.Count - 1];
                // the sliding stone sits in a straight stretch: rock on both of its sides
                bool straight = (open(secretBlock - 32) && open(secretBlock + 32) && !open(secretBlock - 1) && !open(secretBlock + 1))
                    || (open(secretBlock - 1) && open(secretBlock + 1) && !open(secretBlock - 32) && !open(secretBlock + 32));
                if (!straight)
                {
                    if (branch.Count < 3) continue;
                    branch.RemoveAt(branch.Count - 1);
                    secretBlock = branch[branch.Count - 1];
                    straight = (open(secretBlock - 32) && open(secretBlock + 32) && !open(secretBlock - 1) && !open(secretBlock + 1))
                        || (open(secretBlock - 1) && open(secretBlock + 1) && !open(secretBlock - 32) && !open(secretBlock + 32));
                    if (!straight) continue;
                }
                var alc = new PitAlcove { secret = secretBlock, cells = branch.Take(branch.Count - 1).ToList() };
                foreach (int b in branch) taken.Add(b);
                alcoves.Add(alc);
            }
            foreach (var alc in alcoves) walls[alc.secret] = (byte)rock;

            // what the party can walk with the gate open, the stairs at the far end of the far side
            var dist = distances(start, -1);
            var cells = dist.Keys.ToList();
            var deadEnds = cells.Where(b => new[] { 0, 1, 2, 3 }.Count(d => wallAt(b + STEP[d]) == 0) == 1).ToList();
            int exit = start;
            int farthest = -1;
            foreach (int b in gate >= 0 ? cells.Where(sideB.Contains) : (deadEnds.Count != 0 ? deadEnds : cells))
            {
                int d = dist[b];
                if (d > farthest) { farthest = d; exit = b; }
            }
            return new GeneratedFloor { walls = walls, start = start, exit = exit, cells = cells, deadEnds = deadEnds, dist = dist, rect = rect, gate = gate, regionB = sideB, alcoves = alcoves };
        }

        // What a floor can ask of the party: the puzzle that opens its gate. (Unity build: the first six; the
        // rest are the older floors' objectives, kept so a save made on one still reads.)
        public static readonly DungeonObjective[] OBJECTIVES =
        {
            new DungeonObjective { id = "levers", label = "Pull the levers" },
            new DungeonObjective { id = "switch", label = "Find the hidden switch" },
            new DungeonObjective { id = "key", label = "Find the gate's key" },
            new DungeonObjective { id = "sigils", label = "Gather the imp's sigils" },
            new DungeonObjective { id = "clear", label = "Clear the floor" },
            new DungeonObjective { id = "boss", label = "Slay the floor's master" },
            new DungeonObjective { id = "exit", label = "Find the way down" },
            new DungeonObjective { id = "sigil", label = "Take the imp's sigil" },
            new DungeonObjective { id = "shards", label = "Gather the imp's sigils" },
            new DungeonObjective { id = "cull", label = "Thin them out" },
            new DungeonObjective { id = "vault", label = "Open the vault" },
        };
        public const int PIT_PUZZLES = 6;

        // How many of a thing an objective asks for.
        public static int objectiveNeed(string objective, int depth)
        {
            if (objective == "sigils") return Math.Min(6, 2 + Js.FloorDiv(depth, 4));
            if (objective == "shards") return 2 + Js.FloorDiv(depth, 3);
            if (objective == "cull") return 4 + depth;
            return 1;
        }

        public static string floorSizeName(int depth)
        {
            int cells = floorCells(depth);
            return cells <= 7 ? "small" : cells <= 9 ? "fair-sized" : cells <= 11 ? "big" : "huge";
        }

        public static int monsterCount(int depth)
        {
            return Math.Min(22, 5 + 2 * depth);
        }

        // Everything about a floor, decided from the floor number alone so it reads the same everywhere.
        // Floors are ordered by how hard a level's own monsters hit rather than by its number: walking into
        // the Keep's guards on floor one is what "borrow a level you have walked" gets you otherwise.
        // `visited`: a list of level numbers, or meta.visited (level -> { danger }).
        public static FloorPlan floorPlan(double floor, object visited = null, int currentLevel = 0)
        {
            visited = visited ?? new Dictionary<int, VisitedEntry>();
            var levels = visited is IEnumerable<int> list
                ? list.Select(l => (l, danger: 0)).ToList()
                : ((IDictionary<int, VisitedEntry>)visited).Select(e => (l: e.Key, danger: e.Value != null ? e.Value.danger : 0)).ToList();
            var candidates = levels
                .Where(e => e.l >= 1 && e.l <= 29 && e.l != currentLevel)
                .OrderBy(e => e.danger).ThenBy(e => e.l)
                .Select(e => e.l)
                .ToList();
            if (candidates.Count == 0) return null;
            int depth = Math.Max(1, double.IsNaN(floor) || (int)Math.Truncate(floor) == 0 ? 1 : (int)Math.Truncate(floor));
            int level = candidates[Math.Min(depth - 1, candidates.Count - 1)];
            var random = dungeonRng($"pit:{depth}");
            // every fifth floor belongs to a master; the others to one of the gate's puzzles
            string objective = depth % 5 == 0 ? "boss" : OBJECTIVES[Js.Floor(random() * (PIT_PUZZLES - 1))].id;
            int need = objectiveNeed(objective, depth);
            return new FloorPlan { floor = depth, depth = depth, level = level, objective = objective, need = need, seed = $"pit:{depth}:{Js.Floor(random() * 1e9)}", cells = floorCells(depth) };
        }

        /// <summary>Unity build: a seeded shuffle (Fisher-Yates) for lists of any length.</summary>
        static void pitShuffle<T>(List<T> a, Func<double> random)
        {
            for (int i = a.Count - 1; i > 0; i -= 1)
            {
                int j = Js.Floor(random() * (i + 1));
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        /// <summary>Array.prototype.sort as V8 runs it (TimSort; below 64 elements one run finished by binary
        /// insertion), so a comparator that rolls dice is called exactly as often, in the same order.</summary>
        static void Dungeon_v8Sort<T>(List<T> a, Func<T, T, double> compare)
        {
            int length = a.Count;
            if (length < 2) return;
            // ponytail: no run merging; a list of 64+ would need V8's MergeCollapse/galloping (monster kinds per level are a handful)
            if (length >= 64) throw new NotSupportedException("Dungeon_v8Sort: 64 or more elements");
            int low = 0;
            int high = length;
            // CountAndMakeRun(0, length)
            int runLength = 1;
            if (low + 1 != high)
            {
                runLength = 2;
                double order = compare(a[low + 1], a[low]);
                bool isDescending = order < 0;
                T previous = a[low + 1];
                for (int idx = low + 2; idx < high; idx += 1)
                {
                    T current = a[idx];
                    order = compare(current, previous);
                    if (isDescending) { if (order >= 0) break; }
                    else { if (order < 0) break; }
                    previous = current;
                    runLength += 1;
                }
                if (isDescending) a.Reverse(low, runLength);
            }
            // BinaryInsertionSort(low, low + runLength, high): minRun is the whole list below 64
            if (runLength < length)
            {
                for (int start = low == low + runLength ? low + 1 : low + runLength; start < high; start += 1)
                {
                    int left = low;
                    int right = start;
                    T pivot = a[start];
                    while (left < right)
                    {
                        int mid = left + ((right - left) >> 1);
                        double order = compare(pivot, a[mid]);
                        if (order < 0) right = mid;
                        else left = mid + 1;
                    }
                    for (int p = start; p > left; p -= 1) a[p] = a[p - 1];
                    a[left] = pivot;
                }
            }
        }

        // ---- DungeonMixin ----
        public DungeonRun dungeonRun;
        public DungeonState dungeon;

        public DungeonRun initDungeonRun()
        {
            dungeonRun = new DungeonRun { cleared = 0, runs = 0, best = new JsonObject() };
            return dungeonRun;
        }

        public DungeonRun uiDungeonRunLoad(JsonObject saved)
        {
            initDungeonRun();
            if (saved == null) return dungeonRun;
            dungeonRun.cleared = Math.Max(0, Store_TruncOr0(Store_Number(saved["cleared"])));
            dungeonRun.runs = Math.Max(0, Store_TruncOr0(Store_Number(saved["runs"])));
            if (saved["best"] is JsonObject best) dungeonRun.best = (JsonObject)best.DeepClone();
            return dungeonRun;
        }

        // The floor the party may go down to next, and what it will ask of them.
        public FloorPlan uiDungeonFloorPlan(double floor)
        {
            return floorPlan(floor, meta != null ? meta.visited : new Dictionary<int, VisitedEntry>(), currentLevel);
        }

        // What a finished floor pays: credits, experience, reagents and things for the bag. Nothing is
        // left lying on the floor, so nothing is lost when it is thrown away.
        public List<string> uiDungeonRewards(int depth, string seed = "pit", IEnumerable<ExtraItemDef> extraItems = null)
        {
            var random = dungeonRng($"{seed}:reward");
            var got = new List<string>();
            int credits = 40 + 25 * depth + Js.Floor(random() * 40);
            queueAsync(() => giveCredits(credits, 1));
            got.Add($"{credits} crowns");

            int points = Js.Round((60 + 40 * depth) / 3.0);
            for (int c = 0; c < 4; c += 1)
            {
                var ch = characters[c];
                if (ch == null || (ch.flags & 1) == 0 || (ch.flags & 8) != 0) continue;
                for (int skill = 0; skill < 3; skill += 1) increaseExperience(c, skill, points);
            }
            got.Add($"{points} experience each");

            int rolls = 1 + Js.FloorDiv(depth, 4) + (random() < 0.5 ? 1 : 0);
            for (int i = 0; i < rolls; i += 1)
            {
                double roll = random();
                if (roll < 0.45)
                {
                    var keys = pouch.Keys.ToList();
                    string key = keys[Js.Floor(random() * keys.Count)];
                    int n = 1 + Js.Floor(random() * 3);
                    pouch[key] += n;
                    got.Add($"{REAGENTS[key].name} ×{n}");
                }
                else
                {
                    var props = uiCraftRegisterItems(extraItems);
                    var potion = POTIONS[Js.Floor(random() * POTIONS.Length)];
                    int made = uiDungeonGiveItem(props.TryGetValue(potion.id, out int p) ? p : (int?)null);
                    if (made != 0) got.Add(potion.name);
                    else
                    {
                        queueAsync(() => giveCredits(40, 1));   // no room in the bag: he pays instead
                        got.Add("40 crowns (no room in the pack)");
                    }
                }
            }
            return got;
        }

        // Puts one item in the first free bag slot, or nothing when there is no room - in the bag, or in
        // the engine's item table, which every level shares.
        public int uiDungeonGiveItem(int? prop)
        {
            if (prop == null) return 0;
            int free = Array.IndexOf(inventory, (ushort)0);
            if (free < 0) return 0;
            int spare = 0;
            for (int i = 1; i < 400; i += 1) if (itemsInPlay[i].itemPropertyIndex == 0) spare += 1;
            if (spare < 20) return 0;   // leave the item table room to breathe
            int item = -1;
            try { item = makeItem(prop.Value, 0, 0); } catch (QuitException) { throw; } catch (Exception) { return 0; }
            if (item == -1) return 0;
            inventory[free] = (ushort)item;
            gui_drawInventory();
            return item;
        }

        public bool uiInDungeon() { return dungeon != null; }

        public DungeonInfo uiDungeonInfo()
        {
            var d = dungeon;
            if (d == null) return null;
            return new DungeonInfo
            {
                depth = d.depth, level = d.level, objective = d.objective, need = d.need != 0 ? d.need : 1, exitBlock = d.exitBlock, ready = d.ready,
                monstersLeft = monsters.Count(m => m.properties != null && m.hitPoints > 0 && m.mode < 13),
                bossAlive = monsters.Any(m => m.dungeonBoss != 0 && m.hitPoints > 0),
                kills = d.kills, sigilsBefore = d.sigilsBefore,
                levers = (d.levers ?? new List<DungeonLever>()).Count, leversPulled = d.leversPulled, vaultOpen = d.vault != null && d.vault.open,
                gateOpen = d.gate == null || d.gate.open, gateLocked = d.gate != null && d.gate.locked, floorDone = d.floorDone, stairs = d.stairs,
                secrets = (d.secrets ?? new List<PitSecret>()).Count, secretsFound = (d.secrets ?? new List<PitSecret>()).Count(x => x.open),
            };
        }

        // Which wall types this level actually defines. The engine never clears the wall tables between
        // levels (nothing in the original data reads an undefined type), so entries left over from the
        // level before are still sitting there - and drawing one gives a stained-glass window hung in a
        // mine. The level's own LEVEL<N>.WLL is the only honest list.
        public HashSet<int> uiDungeonWallSet()
        {
            var @out = new HashSet<int>();
            try
            {
                var file = res.get($"LEVEL{currentLevel}.WLL");
                int count = Js.FloorDiv(file.Length - 2, 12);
                for (int i = 0; i < count; i += 1) @out.Add(file[2 + i * 12] | (file[2 + i * 12 + 1] << 8));
            }
            catch (QuitException) { throw; }
            catch (Exception) { /* no file: fall back to whatever the map uses */ }
            if (@out.Count == 0) foreach (var l in levelBlockProperties) foreach (int w in l.walls) if (w != 0) @out.Add(w);
            return @out;
        }

        // The borrowed level's own rock: the maze is drawn in that level's art, so nothing has to be
        // minted the way the camp mints its doors.
        public int uiDungeonRockWall()
        {
            var count = new Dictionary<int, int>();   // a Map: iterated in insertion order
            foreach (var l in levelBlockProperties) foreach (int w in l.walls) if (w != 0) count[w] = (count.TryGetValue(w, out int c) ? c : 0) + 1;
            var own = uiDungeonWallSet();
            Func<int, bool> usable = (w) => w > 0 && w != 0x1a && own.Contains(w) && wllVmpMap[w] != 0 && (wllWallFlags[w] & 1) != 0 && specialWallTypes[w] == 0;
            int best = 0;
            int most = -1;
            foreach (var e in count) if (usable(e.Key) && e.Value > most) { most = e.Value; best = e.Key; }
            if (best != 0) return best;
            for (int w = 1; w < 256; w += 1) if (usable(w)) return w;
            return 1;
        }

        // Copies the borrowed level out of the way, then writes the floor over it.
        // What a level owns that a floor written over it must give back: its remembered state, its spawn set, and
        // the quest flags as they were before its entry function ran.
        DungeonBackup pitBackupOf(int level)
        {
            int bit = 1 << (level - 1);
            return new DungeonBackup
            {
                temp = lvlTempData != null ? lvlTempData[level - 1] : null,
                hadFlag = (hasTempDataFlags & bit) != 0,
                spawns = monsterSpawns != null && monsterSpawns.TryGetValue(level, out var sp) ? sp : null,
                flags = flagsTable.ToArray(),
                items = new List<DungeonItemRecord>(),
            };
        }

        // `homeFrom` / `backupFrom`: going deeper - the party is already standing in the next level (uiDungeonDescend),
        // and keeps the way home of the run's first floor.
        public async Task<int> uiDungeonEnter(FloorPlan args, DungeonHome homeFrom = null, DungeonBackup backupFrom = null)
        {
            int level = args.level, depth = args.depth;
            string seed = args.seed, objective = args.objective;
            if (dungeon != null || level == 0 || (homeFrom == null && level == currentLevel)) return 0;
            var home = homeFrom ?? new DungeonHome { level = currentLevel, block = currentBlock, direction = currentDirection };
            var backup = backupFrom ?? pitBackupOf(level);
            // The borrowed level's scripts have to be silent for the arrival too, not just afterwards.
            // Loading a level runs its entry function, and that can open a scene window belonging to a map
            // that is about to be written over - which then sits there with needSceneRestore set, and while
            // that is set the party cannot pick anything up, equip anything or open the chest.
            if (currentLevel != level) await debugTeleport(level, 528, 0);
            // A revisit re-runs the level's .INF entry function; it must not leave quest flags behind.
            Js.Set(flagsTable, backup.flags);
            // Every block script of the borrowed level is silenced from here on too: its teleports, traps
            // and cutscenes belong to a map that no longer exists.
            scriptData = null;
            // The real floor items are taken out of the way (and remembered) before a corridor is carved
            // over them.
            for (int b = 0; b < 1024; b += 1)
            {
                var chain = new List<int>();
                int cur = levelBlockProperties[b].assignedObjects;
                int guard = 0;
                while (cur != 0 && guard++ < 64)
                {
                    var obj = findObject(cur);
                    if ((cur & 0x8000) == 0 && cur < itemsInPlay.Count() && itemsInPlay[cur] != null && itemsInPlay[cur].itemPropertyIndex != 0) chain.Add(cur);
                    cur = obj.nextAssignedObject;
                }
                foreach (int id in chain)
                {
                    var it = itemsInPlay[id];
                    backup.items.Add(new DungeonItemRecord { id = id, block = b, x = it.x, y = it.y, flyingHeight = it.flyingHeight, shpCurFrame_flg = it.shpCurFrame_flg, itemPropertyIndex = it.itemPropertyIndex });
                    await removeLevelItem(id, b);
                }
            }
            for (int i = 0; i < 30; i += 1)
            {
                var m = monsters[i];
                if (m != null && m.block != 0) placeMonster(m, 0, 0);
                // Whatever this monster was carrying goes with it. The item records are a fixed table of a few
                // hundred entries shared by the whole world; dropping a monster without freeing them leaks a
                // handful every time a floor is built, and a few dozen floors later the table is full and the
                // next level to load dies with "Out of item slots".
                int held = m != null ? m.assignedItems : 0;
                int guard = 0;
                while (held != 0 && guard++ < 32)
                {
                    int next = held < itemsInPlay.Count() && itemsInPlay[held] != null ? itemsInPlay[held].nextAssignedObject : 0;
                    deleteItem(held);
                    held = next;
                }
                monsters[i] = makeEmptyMonster(i);
            }
            var plan = generateFloor(seed, depth, uiDungeonRockWall());
            uiDungeonApply(plan);
            dungeon = new DungeonState
            {
                home = home, level = level, depth = depth, seed = seed, objective = objective, need = args.need != 0 ? args.need : 1, ready = false, backup = backup, plan = plan,
                madeItems = new List<int>(), exitBlock = plan.exit, kills = 0, sigilsBefore = 0,
            };
            uiDungeonDress(plan, seed);
            currentBlock = plan.start;
            currentDirection = 0;
            (partyPosX, partyPosY) = calcCoordinates(plan.start, 0x80, 0x80);
            sceneDefaultUpdate = 1;
            sceneUpdateRequired = true;
            gui_drawScene(0);
            return 1;
        }

        // Writes a generated floor over the level that is loaded now.
        public void uiDungeonApply(GeneratedFloor plan)
        {
            for (int b = 0; b < 1024; b += 1)
            {
                var l = levelBlockProperties[b];
                Js.Fill(l.walls, plan.walls[b]); // solid blocks carry the wall on every face; open blocks carry none
                l.assignedObjects = 0;
                l.drawObjects = 0;
                l.direction = 5;
                // Every flag of the old map goes: what was explored, what was sealed (0x10 would make a block
                // impassable from every side), and the invisible-wall bookkeeping. Only the automap bit the
                // wall type implies is re-derived.
                l.flags = wllAutomapData[l.walls[0]] == 17 ? 0x20 : 0;
            }
            if (openDoorState != null) foreach (var door in openDoorState) door.block = 0; // no door of the old map is still moving
        }

        // The kinds this level can actually draw: a property record whose shapes were never loaded is a
        // placeholder, and spawning it takes the renderer down on the first frame it is visible.
        public List<int> uiDungeonTypes()
        {
            var @out = new List<int>();
            for (int i = 0; i < monsterProperties.Count(); i += 1)
            {
                var p = monsterProperties[i];
                if (p == null || p.hitPoints == 0 || p.maxWidth == 0) continue;
                int frames = 0;
                for (int f = 0; f < 16; f += 1) if (monsterShapes.ElementAtOrDefault((p.shapeIndex << 4) + f) != null) frames += 1;
                if (frames == 16) @out.Add(i);
            }
            return @out;
        }

        // The things on this floor worth marking on the map: the gate, the way down once the gate is open, the levers
        // already pulled. Nothing hidden is marked: finding it is the floor's puzzle.
        public List<DungeonMark> uiDungeonMarks()
        {
            var d = dungeon;
            if (d == null) return new List<DungeonMark>();
            var @out = new List<DungeonMark>();
            if (d.vault != null) @out.Add(new DungeonMark { block = d.vault.block, kind = d.vault.open ? "vault (open)" : "vault (shut)" });
            if (d.gate != null) @out.Add(new DungeonMark { block = d.gate.block, kind = d.gate.open ? "the gate (open)" : "the gate (shut)" });
            if (d.stairs != 0 && (d.gate == null || d.gate.open)) @out.Add(new DungeonMark { block = d.stairs, kind = "the way down" });
            foreach (var lever in (d.levers ?? new List<DungeonLever>()).Where(l => l.pulled))
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = lever.block + STEP[dir];
                    if (nb >= 0 && nb < 1024 && levelBlockProperties[nb].walls.All(w => w == 0)) { @out.Add(new DungeonMark { block = nb, kind = "lever (pulled)" }); break; }
                }
            if (d.objective == "exit") @out.Add(new DungeonMark { block = d.exitBlock, kind = "the way down" });
            return @out;
        }

        // Where something is, in plain words, from where the party stands.
        public string uiDungeonBearing(int? block)
        {
            if (currentBlock == 0 || block == null) return "";
            int dx = (block.Value & 31) - (currentBlock & 31);
            int dy = (block.Value >> 5) - (currentBlock >> 5);
            if (dx == 0 && dy == 0) return "right here";
            string ns = dy < 0 ? "north" : dy > 0 ? "south" : "";
            string ew = dx > 0 ? "east" : dx < 0 ? "west" : "";
            int steps = Math.Abs(dx) + Math.Abs(dy);
            return $"{steps} block{(steps == 1 ? "" : "s")} {ns}{ew}";
        }

        // What the borrowed level can furnish a floor with: its own doors, levers and decorated walls.
        // A level without levers gets a pair minted from one of its decorated walls, the way the camp
        // mints its doors (indices nothing in a LEVEL<N>.WLL ever uses).
        public DungeonKit uiDungeonFurnish()
        {
            Func<int, int> flags = (w) => wllWallFlags[w];
            var own = uiDungeonWallSet();
            int door = 0;
            for (int w = 1; w < 256 && door == 0; w += 1)
            {
                if (!own.Contains(w)) continue;                                  // a leftover from another level
                if (specialWallTypes[w] != 5) continue;                          // a door with a switch to click
                if ((flags(w) & 8) == 0 || (flags(w) & 0x20) == 0) continue;     // a door, and shut
                if ((flags(w) & 1) == 0) continue;                               // solid while it is shut
                door = w;
            }
            // Decoration: only walls that are plain walls in every other respect. A level's "decorated"
            // types also cover windows, archways and gateways, and those look absurd hung in a corridor -
            // the automap byte is what tells them apart (255 = draws as ordinary wall).
            var decor = new List<int>();
            for (int w = 1; w < 256; w += 1)
            {
                if (!own.Contains(w)) continue;
                if (specialWallTypes[w] != 1 || flags(w) != 7 || wllAutomapData[w] != 255) continue;
                if (wllVmpMap[w] == 0 || wllShapeMap[w] == 0) continue;
                decor.Add(w);
            }
            // Levers: only the level's own. Minting one from a decoration put a stained-glass window on the
            // wall and called it a lever, which is exactly the kind of nonsense this floor does not need.
            int lever = 0;
            for (int w = 1; w < 255 && lever == 0; w += 1)
            {
                if (!own.Contains(w) || !own.Contains(w + 1)) continue;
                if (specialWallTypes[w] == 2 && specialWallTypes[w + 1] == 3 && wllVmpMap[w] != 0) lever = w;
            }
            return new DungeonKit { door = door, lever = lever, decor = decor };
        }

        // Unity build: dresses a floor the way the original levels are built. The gate across the maze, the puzzle
        // that opens it (levers, a hidden switch, a key, the floor's sigils, its monsters or its master), stones
        // that slide away to hidden alcoves, shortcut doors, decoration, and deeper down the traps the original
        // levels are full of: floors that throw the party somewhere else, and floors that turn it about.
        public void uiDungeonDress(GeneratedFloor plan, string seed)
        {
            var d = dungeon;
            if (d == null) return;
            var random = dungeonRng($"{seed}:dress");
            var kit = uiDungeonFurnish();
            int rock = uiDungeonRockWall();
            Func<int, bool> inMap = (b) => b >= 0 && b < 1024;
            Func<int, bool> open = (b) => inMap(b) && levelBlockProperties[b].walls[0] == 0 && levelBlockProperties[b].walls[1] == 0
                && levelBlockProperties[b].walls[2] == 0 && levelBlockProperties[b].walls[3] == 0;
            Func<int, bool> solid = (b) => !open(b);
            d.doors = new List<DungeonDoor>();
            d.levers = new List<DungeonLever>();
            d.leversPulled = 0;
            d.vault = null;
            d.secrets = new List<PitSecret>();
            d.traps = new List<PitTrap>();
            d.switchWall = null;
            d.gate = null;
            d.stairs = plan.exit;
            var alcoveCells = new HashSet<int>(plan.alcoves.SelectMany(x => x.cells));
            var secretBlocks = new HashSet<int>(plan.alcoves.Select(x => x.secret));
            var sideA = plan.cells.Where(b => !plan.regionB.Contains(b)).ToList();
            Func<int, int, DungeonDoor> hangDoor = (b, type) =>
            {
                var l = levelBlockProperties[b];
                bool ns = !solid(b - 32) || !solid(b + 32);
                l.walls[ns ? 0 : 1] = (byte)type;
                l.walls[ns ? 2 : 3] = (byte)type;
                l.walls[ns ? 1 : 0] = (byte)rock;
                l.walls[ns ? 3 : 2] = (byte)rock;
                l.direction = 5;
                return new DungeonDoor { block = b, axis = ns ? 0 : 1 };
            };
            // a puzzle this level cannot furnish is swapped for one it can
            string puzzle = d.objective;
            if (puzzle == "levers" && kit.lever == 0) puzzle = kit.decor.Count != 0 ? "switch" : "clear";
            if (puzzle == "switch" && kit.decor.Count == 0) puzzle = kit.lever != 0 ? "levers" : "clear";
            d.objective = puzzle;
            // decoration: one type of the level's decorated walls marks the sliding stones (the floor's tell, as the
            // original's cracked and odd-coloured walls are); the switch hides among the others
            d.clueWall = kit.decor.Count != 0 ? kit.decor[0] : 0;
            var pool = kit.decor.Count > 1 ? kit.decor.Skip(1).ToList() : new List<int>();
            d.switchType = pool.Count != 0 ? pool[Js.Floor(random() * pool.Count)] : d.clueWall;

            // the gate
            if (plan.gate >= 0)
            {
                if (kit.door != 0)
                {
                    var hung = hangDoor(plan.gate, kit.door);
                    d.gate = new PitGate { block = hung.block, axis = hung.axis, door = true };
                }
                else
                {
                    Js.Fill(levelBlockProperties[plan.gate].walls, (byte)rock);   // a sealed wall, where the level has no doors
                    d.gate = new PitGate { block = plan.gate, door = false };
                }
                d.gate.locked = puzzle != "boss";   // the master's gate stands unbarred: the master is the lock
                if (!d.gate.locked && !d.gate.door) { Js.Fill(levelBlockProperties[plan.gate].walls, (byte)0); d.gate.open = true; }
            }

            // the sliding stones, each with the tell on the face the corridor sees (deeper down, only some of them)
            foreach (var alc in plan.alcoves)
            {
                d.secrets.Add(new PitSecret { block = alc.secret });
                if (d.clueWall == 0 || (d.depth >= 6 && random() > 0.6)) continue;
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = alc.secret + STEP[dir];
                    if (!open(nb) || alcoveCells.Contains(nb)) continue;
                    levelBlockProperties[alc.secret].walls[dir] = (byte)d.clueWall;
                    levelBlockProperties[alc.secret].direction = 5;
                }
            }

            // where the floor's hidden things go: the alcoves first, then the far dead ends of the start side
            var hiding = plan.alcoves.Select(x => x.cells[0]).ToList();
            hiding.AddRange(plan.deadEnds.Where(b => b != plan.start && !plan.regionB.Contains(b)).OrderByDescending(b => plan.dist.TryGetValue(b, out int dd) ? dd : 0));
            d.spots = hiding.Distinct().ToList();

            // rock faces a puzzle piece can be set into: beside the start side's corridors (or an alcove's), never on
            // a sliding stone, the gate or a door
            Func<int, int, bool> faceFree = (b, dir) =>
            {
                int nb = b + STEP[dir];
                if (!inMap(nb) || open(nb) || secretBlocks.Contains(nb) || nb == plan.gate) return false;
                return specialWallTypes[levelBlockProperties[nb].walls[dir ^ 2]] == 0 && levelBlockProperties[nb].walls[dir ^ 2] != d.clueWall;
            };
            if (puzzle == "levers")
            {
                int wanted = Math.Min(4, 2 + Js.FloorDiv(d.depth, 5));
                var where = new List<int>();
                // deeper down, one of them waits in an alcove
                if (d.depth >= 4 && plan.alcoves.Count != 0) where.Add(plan.alcoves[Js.Floor(random() * plan.alcoves.Count)].cells[0]);
                var spread = sideA.Where(b => b != plan.start && (plan.dist.TryGetValue(b, out int dd) ? dd : 0) >= 3).ToList();
                pitShuffle(spread, random);
                where.AddRange(spread);
                foreach (int at in where)
                {
                    if (d.levers.Count >= wanted) break;
                    if (d.levers.Any(l => Math.Abs((l.block & 31) - (at & 31)) + Math.Abs((l.block >> 5) - (at >> 5)) < 4)) continue;   // spread them out
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        if (!faceFree(at, dir)) continue;
                        int nb = at + STEP[dir];
                        levelBlockProperties[nb].walls[dir ^ 2] = (byte)kit.lever;
                        levelBlockProperties[nb].direction = 5;
                        d.levers.Add(new DungeonLever { block = nb, dir = dir ^ 2, pulled = false });
                        break;
                    }
                }
                if (d.levers.Count == 0) { puzzle = d.objective = "clear"; }
            }
            if (puzzle == "switch")
            {
                // set into the end wall of a dead end (deeper down, possibly an alcove's)
                var ends = (d.depth >= 6 ? hiding : hiding.Where(b => !alcoveCells.Contains(b))).ToList();
                if (ends.Count == 0) ends = hiding;
                foreach (int at in ends.Skip(Js.Floor(random() * Math.Min(3, ends.Count))).Concat(ends))
                {
                    // the end wall: the face across from the one way in
                    int dir = -1;
                    for (int x = 0; x < 4 && dir < 0; x += 1) if (faceFree(at, x) && open(at + STEP[x ^ 2])) dir = x;
                    for (int x = 0; x < 4 && dir < 0; x += 1) if (faceFree(at, x)) dir = x;
                    if (dir < 0) continue;
                    int nb = at + STEP[dir];
                    levelBlockProperties[nb].walls[dir ^ 2] = (byte)d.switchType;
                    levelBlockProperties[nb].direction = 5;
                    d.switchWall = new PitSwitch { block = nb, dir = dir ^ 2 };
                    break;
                }
                if (d.switchWall == null) { puzzle = d.objective = "clear"; }
            }
            if (d.gate == null && puzzle != "clear" && puzzle != "boss" && puzzle != "sigils" && puzzle != "key") puzzle = d.objective = "clear";

            // shortcut doors: straight corridor blocks whose door, shut, cuts nothing off
            Func<HashSet<int>, int> reaches = (closed) =>
            {
                var seen = new HashSet<int> { plan.start };
                var queue = new Queue<int>();
                queue.Enqueue(plan.start);
                while (queue.Count > 0)
                {
                    int b = queue.Dequeue();
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        int nb = b + STEP[dir];
                        if (!inMap(nb) || seen.Contains(nb) || closed.Contains(nb) || solid(nb)) continue;
                        seen.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
                return seen.Count;
            };
            if (kit.door != 0)
            {
                var straights = plan.cells.Where(b =>
                {
                    bool ns = open(b - 32) && open(b + 32) && solid(b - 1) && solid(b + 1);
                    bool ew = open(b - 1) && open(b + 1) && solid(b - 32) && solid(b + 32);
                    return (ns || ew) && b != plan.start && b != plan.exit && b != plan.gate;
                }).ToList();
                int baseline = reaches(new HashSet<int>());
                var shut = new HashSet<int>();
                int wanted = Math.Min(straights.Count, 2 + Js.FloorDiv(d.depth, 2));
                for (int i = 0; i < wanted && straights.Count != 0; i += 1)
                {
                    int ai = Js.Floor(random() * straights.Count);
                    int at = straights[ai];
                    straights.RemoveAt(ai);
                    shut.Add(at);
                    if (reaches(shut) < baseline - shut.Count) { shut.Remove(at); continue; }
                    d.doors.Add(hangDoor(at, kit.door));
                }
            }

            // traps, from floor six: floors that throw the party elsewhere; from floor ten: floors that turn it about
            var trapSpots = sideA.Where(b => b != plan.start && new[] { 0, 1, 2, 3 }.Count(x => open(b + STEP[x])) == 2
                && (plan.gate < 0 || Math.Abs((b & 31) - (plan.gate & 31)) + Math.Abs((b >> 5) - (plan.gate >> 5)) > 1)
                && !d.doors.Any(x => x.block == b)).ToList();
            pitShuffle(trapSpots, random);
            int teleports = d.depth >= 6 ? Math.Min(3, 1 + Js.FloorDiv(d.depth - 6, 5)) : 0;
            int spins = d.depth >= 10 ? Math.Min(3, 1 + Js.FloorDiv(d.depth - 10, 6)) : 0;
            foreach (int at in trapSpots)
            {
                if (teleports == 0 && spins == 0) break;
                if (teleports > 0)
                {
                    int from = plan.dist.TryGetValue(at, out int fd) ? fd : 0;
                    var targets = sideA.Where(b => b != plan.start && Math.Abs((plan.dist.TryGetValue(b, out int td) ? td : 0) - from) >= 6 && !trapSpots.Take(0).Contains(b)).ToList();
                    if (targets.Count == 0) continue;
                    d.traps.Add(new PitTrap { block = at, kind = "teleport", to = targets[Js.Floor(random() * targets.Count)] });
                    teleports -= 1;
                }
                else { d.traps.Add(new PitTrap { block = at, kind = "spin" }); spins -= 1; }
            }

            // Decoration: a share of the rock the party can actually see gets one of the level's own decorated wall
            // types (never the tell of the sliding stones).
            if (pool.Count != 0)
            {
                foreach (int b in plan.cells)
                {
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        int nb = b + STEP[dir];
                        if (!inMap(nb) || !solid(nb) || secretBlocks.Contains(nb) || nb == plan.gate || random() > 0.08) continue;
                        var l = levelBlockProperties[nb];
                        if (specialWallTypes[l.walls[dir ^ 2]] != 0 || l.walls[dir ^ 2] == d.clueWall) continue; // never over a door, a lever or a tell
                        if (d.switchWall != null && nb == d.switchWall.block && (dir ^ 2) == d.switchWall.dir) continue;
                        l.walls[dir ^ 2] = (byte)pool[Js.Floor(random() * pool.Count)];
                        l.direction = 5;
                    }
                }
            }
            sceneUpdateRequired = true;
        }

        // A lever in the pit: the engine has already flipped its art, this counts it and opens the gate when every
        // one of them is down.
        public int uiDungeonLever(int block, int dir)
        {
            var d = dungeon;
            if (d == null || d.levers == null) return 0;
            var lever = d.levers.FirstOrDefault(l => l.block == block && l.dir == dir);
            if (lever == null) return 0;
            lever.pulled = !lever.pulled;
            d.leversPulled = d.levers.Count(l => l.pulled);
            if (d.vault != null && !d.vault.open && d.leversPulled == d.levers.Count)
            {
                d.vault.open = true;   // a floor from an older save
                ui?.Invoke("message", new object[] { $"Something heavy grinds open: the vault, {uiDungeonBearing(d.vault.block)}.", "system" });
                openCloseDoor(d.vault.block, 1);
            }
            else if (d.objective == "levers" && d.gate != null && !d.gate.open && d.leversPulled == d.levers.Count) uiDungeonOpenGate("Every lever is down. Far off, something heavy grinds open.");
            else ui?.Invoke("message", new object[] { $"The lever moves. {d.leversPulled} of {d.levers.Count}.", "system" });
            return 1;
        }

        // Opens the gate (the puzzle is solved): the door swings open, or the sealed wall sinks away.
        public void uiDungeonOpenGate(string why = null)
        {
            var d = dungeon;
            if (d == null || d.gate == null || d.gate.open) return;
            d.gate.open = true;
            d.gate.locked = false;
            if (d.gate.door) openCloseDoor(d.gate.block, 1);
            else { Js.Fill(levelBlockProperties[d.gate.block].walls, (byte)0); sceneUpdateRequired = true; }
            snd_playSoundEffect(78, -1);
            ui?.Invoke("message", new object[] { (why ?? "The gate opens.") + $" The way down lies beyond it, {uiDungeonBearing(d.stairs)}.", "system" });
        }

        // The gate stays shut until its puzzle is solved; the key's gate opens to its key in the hand.
        public bool uiDungeonDoorLocked(int block)
        {
            var d = dungeon;
            if (d == null) return false;
            if (d.vault != null && d.vault.block == block && !d.vault.open) return true;
            if (d.gate == null || d.gate.block != block || !d.gate.locked) return false;
            if (d.objective == "key" && itemInHand != 0 && uiExtraItem(itemInHand)?.id == "pitkey")
            {
                int key = itemInHand;
                _ = setHandItem(0);
                deleteItem(key);
                uiDungeonOpenGate("The key turns in the lock.");
                return true;
            }
            string why = d.objective switch
            {
                "levers" => $"The gate will not move. {d.leversPulled} of {d.levers.Count} levers are down.",
                "switch" => "The gate will not move. Somewhere on this floor a stone is loose.",
                "key" => "The gate is locked. Its key lies somewhere on this floor.",
                "sigils" => "The gate will not move. It wants the imp's sigils.",
                "clear" => "The gate will not move while anything on this floor still breathes.",
                _ => "The gate will not move.",
            };
            ui?.Invoke("message", new object[] { why, "system" });
            return true;
        }

        // A click on a wall in the pit: a sliding stone or the hidden switch. True when the click was the pit's.
        public bool uiDungeonWallClick(int block, int dir)
        {
            var d = dungeon;
            if (d == null) return false;
            var secret = d.secrets?.FirstOrDefault(x => x.block == block && !x.open);
            if (secret != null)
            {
                secret.open = true;
                Js.Fill(levelBlockProperties[block].walls, (byte)0);
                levelBlockProperties[block].flags &= 0xef;
                snd_playSoundEffect(78, -1);
                sceneUpdateRequired = true;
                gui_drawScene(0);
                ui?.Invoke("message", new object[] { "The stone gives under your hand and slides away: a hidden passage.", "system" });
                return true;
            }
            var sw = d.switchWall;
            if (sw != null && !sw.pressed && sw.block == block && sw.dir == dir)
            {
                sw.pressed = true;
                uiDungeonOpenGate("A stone sinks into the wall with a click.");
                return true;
            }
            return false;
        }

        // The party has stepped onto a block of the floor: its traps, and the way down.
        public async Task uiDungeonStepped(int block)
        {
            var d = dungeon;
            if (d == null) return;
            var trap = d.traps?.FirstOrDefault(t => t.block == block);
            if (trap != null && trap.kind == "teleport" && trap.to != 0)
            {
                snd_playSoundEffect(77, -1);
                currentBlock = trap.to;
                (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
                currentDirection = (currentDirection + 1 + Js.Floor(_entropy.NextDouble() * 3)) & 3;
                sceneUpdateRequired = true;
                gui_drawScene(0);
                updateAutoMap(currentBlock);
                ui?.Invoke("message", new object[] { "The floor shimmers under your feet, and the corridor around you is a different one.", "system" });
                return;
            }
            if (trap != null && trap.kind == "spin")
            {
                currentDirection = (currentDirection + 1 + Js.Floor(_entropy.NextDouble() * 3)) & 3;
                sceneUpdateRequired = true;
                gui_drawScene(0);
                return;
            }
            if (block == d.stairs) ui?.Invoke("pit", new object[] { "stairs" });
        }

        // Blocks a monster must not be spawned on: the party's own arrival and the blocks around it, and the old
        // vault of a floor from an older save.
        public HashSet<int> uiDungeonSealed()
        {
            var d = dungeon;
            var @out = new HashSet<int>();
            if (d == null) return @out;
            if (d.plan != null) { @out.Add(d.plan.start); for (int dir = 0; dir < 4; dir += 1) @out.Add(d.plan.start + STEP[dir]); }
            if (d.gate != null) @out.Add(d.gate.block);
            if (d.vault != null)
            {
                @out.Add(d.vault.block);
                @out.Add(d.vault.room);
                for (int dir = 0; dir < 4; dir += 1) @out.Add(d.vault.room + new[] { -32, 1, 32, -1 }[dir]);
            }
            return @out;
        }

        // Fills the floor with monsters of the borrowed level's own kinds: most on the start side, a few guarding
        // the way down.
        public int uiDungeonPopulate(int count, int depth, string seed)
        {
            var plan = dungeon?.plan;
            if (plan == null) return 0;
            var random = dungeonRng($"{seed}:monsters");
            var types = uiDungeonTypes();
            if (types.Count == 0) return 0;
            var @sealed = uiDungeonSealed();
            var spotsB = plan.cells.Where(b => plan.regionB.Contains(b) && !@sealed.Contains(b)).ToList();
            var spotsA = plan.cells.Where(b => !plan.regionB.Contains(b) && (plan.dist.TryGetValue(b, out int dd) ? dd : 0) >= 3 && !@sealed.Contains(b)).ToList();
            int guards = spotsB.Count == 0 ? 0 : Math.Min(spotsB.Count, 1 + Js.FloorDiv(depth, 4));
            // Narrow kinds first: a wide monster does not fit a one-block corridor, and a floor that spawns
            // one monster instead of eight is a floor that cannot be finished.
            var bySize = types.OrderBy(t => monsterProperties[t].maxWidth).ToList();   // stable, like Array.prototype.sort
            int placed = 0;
            int Place(List<int> spots, int want)
            {
                int put = 0, tries = 0;
                while (put < want && tries < Math.Max(60, want * 12) && spots.Count != 0)
                {
                    tries += 1;
                    int at = Js.Floor(random() * spots.Count);
                    int block = spots[at];
                    int facing = (Js.Floor(random() * 4) << 1) & 6;
                    List<int> order;
                    if (random() < 0.5) { order = types.ToList(); Dungeon_v8Sort(order, (x, y) => random() - 0.5); }
                    else order = bySize;
                    int index = -1;
                    foreach (int type in order)
                    {
                        index = initMonster(new[] { block, 0x80, 0x80, facing, type, 0, 0, 0, 0, 0, 0 });
                        if (index != -1) break;
                    }
                    spots.RemoveAt(at); // one to a block: they should be spread through the floor
                    if (index == -1) continue;
                    var m = monsters[index];
                    m.ngplus = depth >= 9 ? Js.FloorDiv(depth - 6, 3) : 0;
                    setMonsterMode(m, 7); // everything down here is already hunting
                    put += 1;
                }
                return put;
            }
            placed += Place(spotsB, guards);
            placed += Place(spotsA, count - placed);
            uiDungeonBalance(depth);
            return placed;
        }

        // How hard a floor's monsters are, against the party that walked in: a hit costs a twentieth of a hero on
        // floor one, a fifth by floor ten, more than a third from floor twenty; and it takes ever more good swings to
        // put one down. There is no top: the pit gets harder for as long as the party keeps going down.
        public static double pitHitShare(int depth) => 0.05 + 0.42 * (1 - Math.Exp(-(depth - 1) / 11.0));
        public static double pitSwings(int depth) => 3 + 0.8 * (depth - 1);

        public void uiDungeonBalance(int depth) => uiDungeonBalance(depth, null);

        public void uiDungeonBalance(int depth, HashSet<Monster> only)
        {
            var heroes = new List<int>();
            for (int c = 0; c < 4; c += 1) if ((characters[c].flags & 1) != 0) heroes.Add(c);
            if (heroes.Count == 0) return;
            double avgHp = heroes.Aggregate(0.0, (a, c) => a + characters[c].hitPointsMax) / heroes.Count;
            double wantDamage = Math.Max(3, avgHp * pitHitShare(depth));
            foreach (var m in monsters)
            {
                if (m.properties == null || m.hitPoints <= 0 || m.mode > 13) continue;
                if (only != null && !only.Contains(m)) continue;
                if (m.dungeonBoss != 0 && dungeon != null && dungeon.bossMaxHp != 0) continue;   // the master is sized once, when it rises
                int id = m.id | 0x8000;
                int swing = Math.Max(1, calcInflictableDamage(id, heroes[0], 1)) + 1; // an average roll
                double share = m.dungeonBoss != 0 ? 1.6 : 1;
                m.pitScale = Math.Max(0.1, Math.Min(m.dungeonBoss != 0 ? 4 : 3, wantDamage * share / swing));
                // What counts is the damage a swing is *expected* to do, hit chance included - against a kind the
                // party lands one blow in thirty on, sizing by damage alone leaves a monster nothing can kill.
                Func<int, double> landed = (c) =>
                {
                    int n = 0;
                    for (int i = 0; i < 200; i += 1) if (battleHitSkillTest(c, id, 0) != 0) n += 1;
                    return n / 200.0;
                };
                double perSwing = heroes.Select(c => landed(c) * calcInflictableDamage(c, id, 1)).Max();
                double swings = pitSwings(depth) * (m.dungeonBoss != 0 ? 6 : 1) * (heroes.Count >= 3 ? 1.3 : 1);
                // Nothing the party swings can touch this kind: it still may not become an immovable wall, so size
                // it as if each swing were worth a single point.
                m.hitPoints = Math.Max(m.dungeonBoss != 0 ? 40 : 6, Js.Round((perSwing > 0 ? perSwing : 1) * swings));
                m.pitMaxHp = m.hitPoints;
                if (m.dungeonBoss != 0 && dungeon != null) dungeon.bossMaxHp = m.hitPoints;
            }
        }

        // The floor's master: the strongest kind the level keeps, at the far end of the far side, guarding the way
        // down - drawn half as large again, hitting harder and standing far longer than anything else down here.
        public int uiDungeonBoss(int depth, string seed)
        {
            var plan = dungeon?.plan;
            if (plan == null) return 0;
            var types = uiDungeonTypes();
            if (types.Count == 0) return 0;
            int type = types.Aggregate(types[0], (best, i) => (monsterProperties[i].hitPoints > monsterProperties[best].hitPoints ? i : best));
            var random = dungeonRng($"{seed}:boss");
            var @sealed = uiDungeonSealed();
            var lair = plan.cells.Where(b => plan.regionB.Count == 0 || plan.regionB.Contains(b)).OrderBy(b => Math.Abs((b & 31) - (plan.exit & 31)) + Math.Abs((b >> 5) - (plan.exit >> 5))).ToList();
            foreach (int block in lair.Concat(plan.cells))
            {
                if (@sealed.Contains(block)) continue;
                int index = initMonster(new[] { block, 0x80, 0x80, (Js.Floor(random() * 4) << 1) & 6, type, 0, 0, 0, 0, 0, 0 });
                if (index == -1) continue; // nothing of this level fits here
                var m = monsters[index];
                m.ngplus = Js.FloorDiv(depth, 3);
                m.dungeonBoss = 1;
                dungeon.bossMaxHp = 0;
                dungeon.bossEnraged = false;
                setMonsterMode(m, 7);
                uiDungeonBalance(depth);
                return index;
            }
            return -1;
        }

        // Called every frame in the pit: the master, brought to half its strength, flies into a rage and calls its
        // guards - two of the floor's own kinds, right beside it.
        public void uiDungeonBossTick()
        {
            var d = dungeon;
            if (d == null || d.bossMaxHp == 0) return;
            var boss = monsters.FirstOrDefault(m => m.dungeonBoss != 0 && m.hitPoints > 0 && m.properties != null);
            if (boss != null && boss.block != 0) d.bossBlock = boss.block;   // where it falls is where it drops its prize
            if (d.bossEnraged || boss == null || boss.hitPoints * 2 > d.bossMaxHp) return;
            d.bossEnraged = true;
            boss.pitScale = boss.pitScale * 1.35;
            var types = uiDungeonTypes().Where(t => t != boss.type).ToList();
            if (types.Count == 0) types = uiDungeonTypes();
            int called = 0;
            var fresh = new HashSet<Monster>();
            foreach (int dir in new[] { 0, 1, 2, 3 })
            {
                if (called >= 2 || types.Count == 0) break;
                int at = boss.block + STEP[dir];
                if (at < 0 || at > 1023 || at == currentBlock || levelBlockProperties[at].walls.Any(w => w != 0)) continue;
                int index = initMonster(new[] { at, 0x80, 0x80, boss.facing, types[called % types.Count], 0, 0, 0, 0, 0, 0 });
                if (index == -1) continue;
                monsters[index].ngplus = boss.ngplus;
                setMonsterMode(monsters[index], 7);
                fresh.Add(monsters[index]);
                called += 1;
            }
            uiDungeonBalance(d.depth, fresh);
            ui?.Invoke("message", new object[] { called > 0 ? "The master roars in fury, and its guards come running!" : "The master roars in fury!", "combat" });
        }

        // Drops one of the port's own items in the floor (the sigil an objective asks for).
        public async Task<int> uiDungeonPlaceItem(int? prop, int block)
        {
            if (dungeon == null || prop == null) return 0;
            int item = -1;
            try { item = makeItem(prop.Value, 0, 0); } catch (QuitException) { throw; } catch (Exception) { return 0; }
            if (item == -1) return 0;
            var (x, y) = calcCoordinates(block, 0x80, 0x80);
            await setItemPosition(item, x, y, 0, 1);
            dungeon.madeItems.Add(item);
            return item;
        }

        // Puts the borrowed level back exactly as it was and returns the party home.
        // Unity build (host rules, companionsBench): the imp's sigils belong to the pit. Leaving it - or loading a save
        // outside it that still carries some - he takes back the ones the party holds. The parity tracers keep them.
        async Task takeBackSigils()
        {
            bool sigil(int item) => item != 0 && (uiExtraItem(item)?.id == "sigil" || uiExtraItem(item)?.id == "pitkey");
            int n = 0;
            for (int i = 0; i < inventory.Length; i += 1) if (sigil(inventory[i])) { deleteItem(inventory[i]); inventory[i] = 0; n += 1; }
            if (sigil(itemInHand)) { int held = itemInHand; await setHandItem(0); deleteItem(held); n += 1; }
            if (n > 0) ui?.Invoke("message", new object[] { n == 1 ? "The imp takes back his sigil." : $"The imp takes back his {n} sigils.", "note" });
        }

        public async Task<int> uiDungeonLeave()
        {
            var d = dungeon;
            if (d == null) return 0;
            return await uiDungeonLeaveTo(d.home.level, d.home.block, d.home.direction);
        }

        // Down the stairs: this floor is put back and the next one written over the next level, in one step - the
        // party never sees home in between.
        public async Task<int> uiDungeonDescend(FloorPlan next)
        {
            var d = dungeon;
            if (d == null || next == null || next.level == 0 || next.level == d.level) return 0;
            var home = d.home;
            var backup = pitBackupOf(next.level);   // before anything touches the level
            await uiDungeonLeaveTo(next.level, 528, 0);
            return await uiDungeonEnter(next, home, backup);
        }

        public async Task<int> uiDungeonLeaveTo(int toLevel, int toBlock, int toDirection)
        {
            var d = dungeon;
            if (d == null) return 0;
            if (campReturn != null) uiLeaveCamp();
            if (companionsBench) await takeBackSigils();
            // Anything the floor made and nobody picked up goes with the floor.
            var carried = new HashSet<int> { itemInHand };
            foreach (int it in inventory) carried.Add(it);
            foreach (var ch in characters) if ((ch.flags & 1) != 0) foreach (int it in ch.items) carried.Add(it);
            foreach (int item in d.madeItems)
            {
                if (carried.Contains(item) || item < 0 || item >= itemsInPlay.Count() || itemsInPlay[item] == null) continue;
                if (itemsInPlay[item].block != 0) await removeLevelItem(item, itemsInPlay[item].block);
                deleteItem(item);
            }
            // A monster that died a moment ago drops its load through the async queue. Let that land before
            // the sweep below, or the drop arrives after the real level is back and is both a leaked record
            // and a stray item lying in a level it never belonged to.
            await drainAsync();
            // Everything still lying on the floor of the pit goes with the pit. Only what the party carries
            // survives, and the real level's own items are put back from the backup below.
            var keep = new HashSet<int> { itemInHand };
            foreach (int it in inventory) keep.Add(it);
            foreach (var ch in characters) if ((ch.flags & 1) != 0) foreach (int it in ch.items) keep.Add(it);
            var restored = new HashSet<int>(d.backup.items.Select(r => r.id));
            for (int b = 0; b < 1024; b += 1)
            {
                int cur = levelBlockProperties[b].assignedObjects;
                int guard = 0;
                var loose = new List<int>();
                while (cur != 0 && guard++ < 64)
                {
                    var obj = findObject(cur);
                    if ((cur & 0x8000) == 0 && cur < itemsInPlay.Count() && itemsInPlay[cur] != null && itemsInPlay[cur].itemPropertyIndex != 0 && !keep.Contains(cur) && !restored.Contains(cur)) loose.Add(cur);
                    cur = obj.nextAssignedObject;
                }
                foreach (int id in loose) { await removeLevelItem(id, b); deleteItem(id); }
            }
            dungeon = null; // before the teleport: nothing must treat the way home as dungeon ground
            await debugTeleport(toLevel, toBlock, toDirection);
            // The teleport snapshotted the floor into the borrowed level's temp data; throw that away.
            int bit = 1 << (d.level - 1);
            if (lvlTempData != null) lvlTempData[d.level - 1] = d.backup.temp;
            hasTempDataFlags = d.backup.hadFlag ? (hasTempDataFlags | bit) : (hasTempDataFlags & ~bit);
            if (monsterSpawns != null)
            {
                if (d.backup.spawns == null) monsterSpawns.Remove(d.level);
                else monsterSpawns[d.level] = d.backup.spawns;
            }
            foreach (var rec in d.backup.items)
            {
                var it = rec.id < itemsInPlay.Count() ? itemsInPlay[rec.id] : null;
                if (it == null) continue;
                it.level = d.level;
                it.block = rec.block;
                it.x = rec.x;
                it.y = rec.y;
                it.flyingHeight = rec.flyingHeight;
                it.shpCurFrame_flg = rec.shpCurFrame_flg;
                it.itemPropertyIndex = rec.itemPropertyIndex;
                it.nextAssignedObject = 0;
                it.nextDrawObject = 0;
            }
            sceneUpdateRequired = true;
            return 1;
        }

        /// <summary>JSON.stringify's view of a record: public fields by their JS names, nulls kept.</summary>
        static readonly JsonSerializerOptions Dungeon_json = new JsonSerializerOptions { IncludeFields = true };

        // Save/load: the floor itself rides along in the level temp data, this is the rest of it.
        public JsonObject uiDungeonSave()
        {
            var d = dungeon;
            if (d == null) return null;
            return new JsonObject
            {
                ["home"] = JsonSerializer.SerializeToNode(d.home, Dungeon_json), ["level"] = d.level, ["depth"] = d.depth, ["seed"] = d.seed, ["objective"] = d.objective, ["need"] = d.need,
                ["exitBlock"] = d.exitBlock, ["kills"] = d.kills, ["sigilsBefore"] = d.sigilsBefore, ["ready"] = d.ready, ["madeItems"] = JsonSerializer.SerializeToNode(d.madeItems.ToList(), Dungeon_json),
                ["doors"] = JsonSerializer.SerializeToNode(d.doors, Dungeon_json), ["levers"] = JsonSerializer.SerializeToNode(d.levers, Dungeon_json), ["vault"] = JsonSerializer.SerializeToNode(d.vault, Dungeon_json),
                ["gate"] = JsonSerializer.SerializeToNode(d.gate, Dungeon_json), ["secrets"] = JsonSerializer.SerializeToNode(d.secrets, Dungeon_json),
                ["switchWall"] = JsonSerializer.SerializeToNode(d.switchWall, Dungeon_json), ["traps"] = JsonSerializer.SerializeToNode(d.traps, Dungeon_json),
                ["stairs"] = d.stairs, ["floorDone"] = d.floorDone, ["bossMaxHp"] = d.bossMaxHp, ["bossEnraged"] = d.bossEnraged, ["clueWall"] = d.clueWall, ["switchType"] = d.switchType,
                ["spots"] = JsonSerializer.SerializeToNode(d.spots, Dungeon_json),
                ["backup"] = new JsonObject
                {
                    ["hadFlag"] = d.backup.hadFlag, ["items"] = JsonSerializer.SerializeToNode(d.backup.items, Dungeon_json), ["flags"] = new JsonArray(d.backup.flags.Select(f => (JsonNode)(int)f).ToArray()),
                    ["temp"] = d.backup.temp != null ? plainTemp(d.backup.temp) : null, ["spawns"] = d.backup.spawns != null ? JsonSerializer.SerializeToNode(d.backup.spawns, Dungeon_json) : null,
                },
            };
        }

        public int uiDungeonLoad(JsonObject saved)
        {
            if (saved == null) { dungeon = null; return 0; }
            var backup = (JsonObject)saved["backup"];
            var levers = saved["levers"]?.Deserialize<List<DungeonLever>>(Dungeon_json) ?? new List<DungeonLever>();
            dungeon = new DungeonState
            {
                home = saved["home"]?.Deserialize<DungeonHome>(Dungeon_json), level = Dungeon_int(saved["level"]), depth = Dungeon_int(saved["depth"]), seed = (string)saved["seed"], objective = (string)saved["objective"],
                need = Dungeon_int(saved["need"]) != 0 ? Dungeon_int(saved["need"]) : 1,
                exitBlock = Dungeon_int(saved["exitBlock"]), kills = Dungeon_int(saved["kills"]), sigilsBefore = Dungeon_int(saved["sigilsBefore"]),
                ready = !(saved["ready"] is JsonValue rv && rv.TryGetValue(out bool r) && r == false),
                madeItems = saved["madeItems"]?.Deserialize<List<int>>(Dungeon_json) ?? new List<int>(),
                doors = saved["doors"]?.Deserialize<List<DungeonDoor>>(Dungeon_json) ?? new List<DungeonDoor>(), levers = levers, vault = saved["vault"]?.Deserialize<DungeonVault>(Dungeon_json),
                leversPulled = levers.Count(l => l.pulled),
                gate = saved["gate"]?.Deserialize<PitGate>(Dungeon_json),
                secrets = saved["secrets"]?.Deserialize<List<PitSecret>>(Dungeon_json) ?? new List<PitSecret>(),
                switchWall = saved["switchWall"]?.Deserialize<PitSwitch>(Dungeon_json),
                traps = saved["traps"]?.Deserialize<List<PitTrap>>(Dungeon_json) ?? new List<PitTrap>(),
                stairs = Dungeon_int(saved["stairs"]) != 0 ? Dungeon_int(saved["stairs"]) : Dungeon_int(saved["exitBlock"]),
                floorDone = saved["floorDone"] is JsonValue fv && fv.TryGetValue(out bool fd) && fd,
                bossMaxHp = Dungeon_int(saved["bossMaxHp"]),
                bossEnraged = saved["bossEnraged"] is JsonValue ev && ev.TryGetValue(out bool en) && en,
                clueWall = Dungeon_int(saved["clueWall"]), switchType = Dungeon_int(saved["switchType"]),
                spots = saved["spots"]?.Deserialize<List<int>>(Dungeon_json) ?? new List<int>(),
                plan = generateFloor((string)saved["seed"], Dungeon_int(saved["depth"]), uiDungeonRockWall()),
                backup = new DungeonBackup
                {
                    hadFlag = backup["hadFlag"] is JsonValue hv && hv.TryGetValue(out bool h) && h,
                    items = backup["items"]?.Deserialize<List<DungeonItemRecord>>(Dungeon_json) ?? new List<DungeonItemRecord>(),
                    flags = (backup["flags"] as JsonArray ?? new JsonArray()).Select(f => (byte)Dungeon_int(f)).ToArray(),
                    temp = backup["temp"] is JsonObject t ? reviveTemp(t, this) : null,
                    spawns = backup["spawns"]?.Deserialize<Monster[]>(Dungeon_json),
                },
            };
            scriptData = null; // the borrowed level's scripts stay silent after a load, too
            return 1;
        }

        /// <summary>a saved number, or 0 (`x || 0`)</summary>
        static int Dungeon_int(JsonNode v) => Store_TruncOr0(Store_Number(v));

        // The level temp data holds typed arrays; the save file holds plain ones.
        static JsonObject plainTemp(LvlTempData t)
        {
            return new JsonObject
            {
                ["walls"] = new JsonArray(t.walls.Select(w => (JsonNode)new JsonArray(w.Select(b => (JsonNode)(int)b).ToArray())).ToArray()),
                ["flags"] = JsonSerializer.SerializeToNode(t.flags.ToArray(), Dungeon_json),
                ["monsters"] = new JsonArray(t.monsters.Select(m =>
                {
                    // ({ properties, ...m }) => ({ ...m, equipmentShapes: Array.from(m.equipmentShapes) })
                    var o = JsonSerializer.SerializeToNode(m, Dungeon_json).AsObject();
                    o.Remove("properties");
                    return (JsonNode)o;
                }).ToArray()),
                ["flyingObjects"] = JsonSerializer.SerializeToNode(t.flyingObjects, Dungeon_json),
                ["monsterDifficulty"] = t.monsterDifficulty,
            };
        }

        static LvlTempData reviveTemp(JsonObject t, LandsOfLore engine)
        {
            return new LvlTempData
            {
                walls = ((JsonArray)t["walls"]).Select(w => ((JsonArray)w).Select(b => (byte)Dungeon_int(b)).ToArray()).ToArray(),
                flags = t["flags"].Deserialize<int[]>(Dungeon_json),
                monsters = ((JsonArray)t["monsters"]).Select(mj =>
                {
                    var m = mj.Deserialize<Monster>(Dungeon_json);
                    m.equipmentShapes = m.equipmentShapes.ToArray();
                    m.properties = engine.monsterProperties[m.type];
                    return m;
                }).ToArray(),
                flyingObjects = t["flyingObjects"].Deserialize<FlyingObject[]>(Dungeon_json),
                monsterDifficulty = Dungeon_int(t["monsterDifficulty"]),
            };
        }
    }
}
