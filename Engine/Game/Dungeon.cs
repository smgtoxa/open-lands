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
        // Returns { walls: Uint8Array(1024) (0 open, rock solid), start, exit, cells, deadEnds, dist, rect }.
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

            // Braid: a few extra ways through, so a floor is not one long corridor. Only opens blocks, so the
            // maze stays connected.
            int loops = Js.Round(n * n * 0.12);
            for (int i = 0; i < loops; i += 1)
            {
                int cx = Js.Floor(random() * n);
                int cy = Js.Floor(random() * n);
                int d = Js.Floor(random() * 4);
                int dx = new[] { 0, 1, 0, -1 }[d];
                int dy = new[] { -1, 0, 1, 0 }[d];
                if (cx + dx < 0 || cy + dy < 0 || cx + dx >= n || cy + dy >= n) continue;
                walls[block(cx, cy) + dx + (dy << 5)] = 0;
            }

            // A couple of open chambers, so the wide monsters have somewhere to stand and a floor is not all
            // corridor.
            int rooms = 1 + Js.FloorDiv(n, 5);
            for (int i = 0; i < rooms; i += 1)
            {
                int cx = 1 + Js.Floor(random() * (n - 2));
                int cy = 1 + Js.Floor(random() * (n - 2));
                int centre = block(cx, cy);
                for (int dy = -1; dy <= 1; dy += 1) for (int dx = -1; dx <= 1; dx += 1)
                    {
                        int b = centre + dx + (dy << 5);
                        int bx = b & 31;
                        int by = b >> 5;
                        if (bx <= 0 || by <= 0 || bx >= 31 || by >= 31) continue;
                        walls[b] = 0;
                    }
            }

            int start = block(startCell[0], startCell[1]);
            var dist = new Dictionary<int, int> { [start] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int b = queue.Dequeue();
                for (int d = 0; d < 4; d += 1)
                {
                    int nb = b + STEP[d];
                    if (nb < 0 || nb > 1023 || walls[nb] != 0 || dist.ContainsKey(nb)) continue;
                    dist[nb] = dist[b] + 1;
                    queue.Enqueue(nb);
                }
            }
            var cells = dist.Keys.ToList();
            // walls[b + STEP[d]] off the map is undefined in JS: open
            Func<int, int> wallAt = (i) => i >= 0 && i < 1024 ? walls[i] : 0;
            var deadEnds = cells.Where(b => new[] { 0, 1, 2, 3 }.Count(d => wallAt(b + STEP[d]) == 0) == 1).ToList();
            int exit = start;
            int far = -1;
            foreach (int b in deadEnds.Count != 0 ? deadEnds : cells)
            {
                int d = dist[b];
                if (d > far) { far = d; exit = b; }
            }
            return new GeneratedFloor { walls = walls, start = start, exit = exit, cells = cells, deadEnds = deadEnds, dist = dist, rect = rect };
        }

        // What a floor can ask of the party.
        public static readonly DungeonObjective[] OBJECTIVES =
        {
            new DungeonObjective { id = "clear", label = "Clear the floor" },
            new DungeonObjective { id = "boss", label = "Slay the floor's master" },
            new DungeonObjective { id = "exit", label = "Find the way down" },
            new DungeonObjective { id = "sigil", label = "Take the imp's sigil" },
            new DungeonObjective { id = "shards", label = "Gather the imp's sigils" },
            new DungeonObjective { id = "cull", label = "Thin them out" },
            new DungeonObjective { id = "vault", label = "Open the vault" },
        };

        // How many of a thing an objective asks for.
        public static int objectiveNeed(string objective, int depth)
        {
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
            return Math.Min(24, 6 + 2 * depth);
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
            string objective = OBJECTIVES[Js.Floor(random() * OBJECTIVES.Length)].id;
            int need = objectiveNeed(objective, depth);
            return new FloorPlan { floor = depth, depth = depth, level = level, objective = objective, need = need, seed = $"pit:{depth}:{Js.Floor(random() * 1e9)}", cells = floorCells(depth) };
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
        public async Task<int> uiDungeonEnter(FloorPlan args)
        {
            int level = args.level, depth = args.depth;
            string seed = args.seed, objective = args.objective;
            if (dungeon != null || level == 0 || level == currentLevel) return 0;
            var home = new DungeonHome { level = currentLevel, block = currentBlock, direction = currentDirection };
            int bit = 1 << (level - 1);
            var backup = new DungeonBackup
            {
                temp = lvlTempData != null ? lvlTempData[level - 1] : null,
                hadFlag = (hasTempDataFlags & bit) != 0,
                spawns = monsterSpawns != null && monsterSpawns.TryGetValue(level, out var sp) ? sp : null,
                flags = flagsTable.ToArray(),
                items = new List<DungeonItemRecord>(),
            };
            // The borrowed level's scripts have to be silent for the arrival too, not just afterwards.
            // Loading a level runs its entry function, and that can open a scene window belonging to a map
            // that is about to be written over - which then sits there with needSceneRestore set, and while
            // that is set the party cannot pick anything up, equip anything or open the chest.
            await debugTeleport(level, 528, 0);
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
                home = home, level = level, depth = depth, seed = seed, objective = objective, need = 1, ready = false, backup = backup, plan = plan,
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

        // The things on this floor worth marking on the map: the vault and its levers, the way down, and
        // wherever the floor's own item is lying.
        public List<DungeonMark> uiDungeonMarks()
        {
            var d = dungeon;
            if (d == null) return new List<DungeonMark>();
            var @out = new List<DungeonMark>();
            if (d.vault != null) @out.Add(new DungeonMark { block = d.vault.block, kind = d.vault.open ? "vault (open)" : "vault (shut)" });
            foreach (var lever in d.levers ?? new List<DungeonLever>())
            {
                // a lever is on a wall: mark the corridor block you pull it from
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int nb = lever.block + new[] { -32, 1, 32, -1 }[dir];
                    if (nb >= 0 && nb < 1024 && levelBlockProperties[nb].walls[0] == 0 && levelBlockProperties[nb].walls[1] == 0
                        && levelBlockProperties[nb].walls[2] == 0 && levelBlockProperties[nb].walls[3] == 0)
                    {
                        @out.Add(new DungeonMark { block = nb, kind = lever.pulled ? "lever (pulled)" : "lever" });
                        break;
                    }
                }
            }
            if (d.objective == "exit") @out.Add(new DungeonMark { block = d.exitBlock, kind = "the way down" });
            foreach (int item in d.madeItems ?? new List<int>())
            {
                var it = item >= 0 && item < itemsInPlay.Count() ? itemsInPlay[item] : null;
                if (it != null && it.block != 0 && it.level == currentLevel)
                {
                    string name = itemName(item);
                    @out.Add(new DungeonMark { block = it.block, kind = !string.IsNullOrEmpty(name) ? name : "something" });
                }
            }
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

        // Doors, levers, a sealed vault and a bit of decoration, so a floor is not bare corridor.
        public void uiDungeonDress(GeneratedFloor plan, string seed)
        {
            var d = dungeon;
            if (d == null) return;
            var random = dungeonRng($"{seed}:dress");
            var kit = uiDungeonFurnish();
            Func<int, bool> open = (b) => levelBlockProperties[b].walls[0] == 0 && levelBlockProperties[b].walls[1] == 0
                && levelBlockProperties[b].walls[2] == 0 && levelBlockProperties[b].walls[3] == 0;
            Func<int, bool> solid = (b) => !open(b);
            d.doors = new List<DungeonDoor>();
            d.levers = new List<DungeonLever>();
            d.leversPulled = 0;
            d.vault = null;
            // Doors sit in a straight stretch of corridor: the two faces along it carry the door, the two
            // across it stay rock, which is exactly how the original levels build one.
            var straights = plan.cells.Where(b =>
            {
                bool ns = !solid(b - 32) && !solid(b + 32) && solid(b - 1) && solid(b + 1);
                bool ew = !solid(b - 1) && !solid(b + 1) && solid(b - 32) && solid(b + 32);
                return (ns || ew) && b != plan.start;
            }).ToList();
            Func<int, int, DungeonDoor> hangDoor = (b, type) =>
            {
                var l = levelBlockProperties[b];
                bool ns = !solid(b - 32);
                int rock = uiDungeonRockWall();
                l.walls[ns ? 0 : 1] = (byte)type;
                l.walls[ns ? 2 : 3] = (byte)type;
                l.walls[ns ? 1 : 0] = (byte)rock;
                l.walls[ns ? 3 : 2] = (byte)rock;
                l.direction = 5;
                return new DungeonDoor { block = b, axis = ns ? 0 : 1 };
            };
            // A door is only ever a shortcut, never a gate across the floor: if shutting one would cut any
            // part of the maze off from the start, it is not hung at all. A floor has to be walkable end to
            // end without clicking a single door.
            Func<HashSet<int>, HashSet<int>> reaches = (closed) =>
            {
                var seen = new HashSet<int> { plan.start };
                var queue = new Queue<int>();
                queue.Enqueue(plan.start);
                while (queue.Count > 0)
                {
                    int b = queue.Dequeue();
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        int nb = b + new[] { -32, 1, 32, -1 }[dir];
                        if (nb < 0 || nb > 1023 || seen.Contains(nb) || closed.Contains(nb) || solid(nb)) continue;
                        seen.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
                return seen;
            };
            if (kit.door != 0)
            {
                var shut = new HashSet<int>();
                int wanted = Math.Min(straights.Count, 2 + Js.FloorDiv(d.depth, 2));
                for (int i = 0; i < wanted && straights.Count != 0; i += 1)
                {
                    int ai = Js.Floor(random() * straights.Count);
                    int at = straights[ai];
                    straights.RemoveAt(ai);
                    shut.Add(at);
                    if (reaches(shut).Count < plan.cells.Count - shut.Count) { shut.Remove(at); continue; }
                    d.doors.Add(hangDoor(at, kit.door));
                }
            }
            // The vault: a dead end shut behind a door that only the levers open.
            var ends = plan.deadEnds.Where(b => b != plan.start && !d.doors.Any(x => x.block == b)).ToList();
            if (kit.door != 0 && kit.lever != 0 && ends.Count != 0)
            {
                int vaultRoom = ends[Js.Floor(random() * ends.Count)];
                // the corridor block leading to it carries the door
                int mouth = -1;
                for (int dir = 0; dir < 4; dir += 1) { int nb = vaultRoom + new[] { -32, 1, 32, -1 }[dir]; if (!solid(nb)) { mouth = nb; break; } }
                if (mouth >= 0 && plan.cells.Contains(mouth))
                {
                    var hung = hangDoor(mouth, kit.door);
                    d.vault = new DungeonVault { block = hung.block, axis = hung.axis, room = vaultRoom, open = false };
                    // levers on the rock beside a few corridor blocks
                    // Never inside the vault and never on the block that holds its door: a lever you cannot
                    // reach until the vault is open is a floor that cannot be finished.
                    var @sealed = new HashSet<int> { vaultRoom, mouth };
                    for (int dir = 0; dir < 4; dir += 1) { int nb = vaultRoom + new[] { -32, 1, 32, -1 }[dir]; if (nb != mouth) @sealed.Add(nb); }
                    var spots = plan.cells.Where(b => b != plan.start && !@sealed.Contains(b)).ToList();
                    int wanted = Math.Min(3, 1 + Js.FloorDiv(d.depth, 4) + 1);
                    for (int i = 0; i < wanted && spots.Count != 0; i += 1)
                    {
                        int ai = Js.Floor(random() * spots.Count);
                        int at = spots[ai];
                        spots.RemoveAt(ai);
                        for (int dir = 0; dir < 4; dir += 1)
                        {
                            int nb = at + new[] { -32, 1, 32, -1 }[dir];
                            if (!solid(nb)) continue;
                            levelBlockProperties[nb].walls[dir ^ 2] = (byte)kit.lever;
                            levelBlockProperties[nb].direction = 5;
                            d.levers.Add(new DungeonLever { block = nb, dir = dir ^ 2, pulled = false });
                            break;
                        }
                    }
                }
            }
            // Decoration: a share of the rock the party can actually see gets one of the level's own
            // decorated wall types.
            if (kit.decor.Count != 0)
            {
                foreach (int b in plan.cells)
                {
                    for (int dir = 0; dir < 4; dir += 1)
                    {
                        int nb = b + new[] { -32, 1, 32, -1 }[dir];
                        if (nb < 0 || nb > 1023 || !solid(nb) || random() > 0.08) continue;
                        var l = levelBlockProperties[nb];
                        if (specialWallTypes[l.walls[dir ^ 2]] != 0) continue; // never over a door or a lever
                        l.walls[dir ^ 2] = (byte)kit.decor[Js.Floor(random() * kit.decor.Count)];
                        l.direction = 5;
                    }
                }
            }
            sceneUpdateRequired = true;
        }

        // A lever in the pit: the engine has already flipped its art, this counts it and opens the vault
        // when every one of them is down.
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
                d.vault.open = true;
                ui?.Invoke("message", new object[] { $"Something heavy grinds open: the vault, {uiDungeonBearing(d.vault.block)}.", "system" });
                openCloseDoor(d.vault.block, 1);
            }
            else
            {
                ui?.Invoke("message", new object[] { $"The lever moves. {d.leversPulled} of {d.levers.Count}.", "system" });
            }
            return 1;
        }

        // The vault door stays shut until the levers say otherwise.
        public bool uiDungeonDoorLocked(int block)
        {
            var d = dungeon;
            return d != null && d.vault != null && d.vault.block == block && !d.vault.open;
        }

        // Blocks a monster must not be spawned on: behind the vault door, and the party's own arrival.
        public HashSet<int> uiDungeonSealed()
        {
            var d = dungeon;
            var @out = new HashSet<int>();
            if (d == null) return @out;
            if (d.plan != null) @out.Add(d.plan.start);
            if (d.vault != null)
            {
                @out.Add(d.vault.block);
                @out.Add(d.vault.room);
                for (int dir = 0; dir < 4; dir += 1) @out.Add(d.vault.room + new[] { -32, 1, 32, -1 }[dir]);
            }
            return @out;
        }

        // Fills the floor with monsters of the borrowed level's own kinds.
        public int uiDungeonPopulate(int count, int depth, string seed)
        {
            var plan = dungeon?.plan;
            if (plan == null) return 0;
            var random = dungeonRng($"{seed}:monsters");
            var types = uiDungeonTypes();
            if (types.Count == 0) return 0;
            var @sealed = uiDungeonSealed();
            var spots = plan.cells.Where(b => (plan.dist.TryGetValue(b, out int dd) ? dd : 0) >= 3 && !@sealed.Contains(b)).ToList();
            // Narrow kinds first: a wide monster does not fit a one-block corridor, and a floor that spawns
            // one monster instead of eight is a floor that cannot be finished.
            var bySize = types.OrderBy(t => monsterProperties[t].maxWidth).ToList();   // stable, like Array.prototype.sort
            int placed = 0;
            int tries = 0;
            while (placed < count && tries < Math.Max(60, count * 12) && spots.Count != 0)
            {
                tries += 1;
                int at = Js.Floor(random() * spots.Count);
                int block = spots[at];
                int facing = (Js.Floor(random() * 4) << 1) & 6;
                // Try the kinds in a random order, then fall back to the narrowest that will stand here.
                List<int> order;
                if (random() < 0.5) { order = types.ToList(); Dungeon_v8Sort(order, (x, y) => random() - 0.5); }
                else order = bySize;
                int index = -1;
                foreach (int type in order)
                {
                    index = initMonster(new[] { block, 0x80, 0x80, facing, type, 0, 0, 0, 0, 0, 0 });
                    if (index != -1) break;
                }
                if (index == -1) { spots.RemoveAt(at); continue; } // nothing of this level fits here
                spots.RemoveAt(at); // one to a block: they should be spread through the floor
                var m = monsters[index];
                m.ngplus = depth >= 9 ? Js.FloorDiv(depth - 6, 3) : 0;
                // Everything down here is already hunting: a pit floor is not a museum of standing monsters.
                setMonsterMode(m, 7);
                placed += 1;
            }
            uiDungeonBalance(depth);
            return placed;
        }

        // A floor borrows whatever the level keeps, and a level's monsters are pitched at the party that
        // belongs there - the Keep's guards are not a floor-one fight. So every monster on a floor is
        // measured against the party that walked in: how hard it hits, and how long it stands.
        public void uiDungeonBalance(int depth)
        {
            var heroes = new List<int>();
            for (int c = 0; c < 4; c += 1) if ((characters[c].flags & 1) != 0) heroes.Add(c);
            if (heroes.Count == 0) return;
            double avgHp = heroes.Aggregate(0.0, (a, c) => a + characters[c].hitPointsMax) / heroes.Count;
            // A hit should cost about a twelfth of a hero on floor one, working up to a third deeper down.
            double wantDamage = Math.Max(4, avgHp * Math.Min(0.34, 0.08 + 0.035 * (depth - 1)));
            foreach (var m in monsters)
            {
                if (m.properties == null || m.hitPoints <= 0 || m.mode > 13) continue;
                int id = m.id | 0x8000;
                int swing = Math.Max(1, calcInflictableDamage(id, heroes[0], 1)) + 1; // an average roll
                m.pitScale = Math.Max(0.12, Math.Min(2, wantDamage / swing));
                if (m.dungeonBoss != 0) m.pitScale = Math.Min(2.5, m.pitScale * 1.3);
                // And it should take a few good swings to put down: more of them the deeper the floor. What
                // counts is the damage a swing is *expected* to do, hit chance included - against a kind the
                // party lands one blow in thirty on, sizing by damage alone leaves a monster nothing can kill.
                // Enough samples to measure a chance as low as one in fifty; too few and a kind the party can
                // barely touch reads as "cannot touch at all" and falls through to the ceiling below.
                Func<int, double> landed = (c) =>
                {
                    int n = 0;
                    for (int i = 0; i < 200; i += 1) if (battleHitSkillTest(c, id, 0) != 0) n += 1;
                    return n / 200.0;
                };
                double perSwing = heroes.Select(c => landed(c) * calcInflictableDamage(c, id, 1)).Max();
                double swings = (m.dungeonBoss != 0 ? 12 : 4) + depth * 0.5;
                // What the monster is normally worth is a ceiling, not a floor: those hit points belong to the
                // level the floor borrowed its art from, not to the party that walked in.
                double byBase = m.properties.hitPoints * Math.Min(1.2, 0.2 + 0.08 * depth) * (m.dungeonBoss != 0 ? 2.5 : 1);
                // Nothing the party swings can touch this kind: it still may not become an immovable wall,
                // so size it as if each swing were worth a single point.
                double byParty = (perSwing > 0 ? perSwing : 1) * swings;
                m.hitPoints = Math.Max(8, Js.Round(Math.Min(Math.Min(m.properties.hitPoints * 2.5, byBase), byParty)));
            }
        }

        // One tougher monster at the far end of the floor.
        public int uiDungeonBoss(int depth, string seed)
        {
            var plan = dungeon?.plan;
            if (plan == null) return 0;
            var types = uiDungeonTypes();
            if (types.Count == 0) return 0;
            int type = types.Aggregate(types[0], (best, i) => (monsterProperties[i].hitPoints > monsterProperties[best].hitPoints ? i : best));
            var random = dungeonRng($"{seed}:boss");
            var @sealed = uiDungeonSealed();
            var spots = new[] { plan.exit }.Concat(plan.deadEnds).Concat(plan.cells).Where(b => !@sealed.Contains(b)).ToList();
            foreach (int block in spots)
            {
                int index = initMonster(new[] { block, 0x80, 0x80, (Js.Floor(random() * 4) << 1) & 6, type, 0, 0, 0, 0, 0, 0 });
                if (index == -1) continue; // nothing of this level fits here
                var m = monsters[index];
                m.ngplus = depth >= 9 ? Js.FloorDiv(depth - 6, 3) : 0;
                m.dungeonBoss = 1;
                setMonsterMode(m, 7);
                uiDungeonBalance(depth);
                return index;
            }
            return -1;
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
            bool sigil(int item) => item != 0 && uiExtraItem(item)?.id == "sigil";
            int n = 0;
            for (int i = 0; i < inventory.Length; i += 1) if (sigil(inventory[i])) { deleteItem(inventory[i]); inventory[i] = 0; n += 1; }
            if (sigil(itemInHand)) { int held = itemInHand; await setHandItem(0); deleteItem(held); n += 1; }
            if (n > 0) ui?.Invoke("message", new object[] { n == 1 ? "The imp takes back his sigil." : $"The imp takes back his {n} sigils.", "note" });
        }

        public async Task<int> uiDungeonLeave()
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
            await debugTeleport(d.home.level, d.home.block, d.home.direction);
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
