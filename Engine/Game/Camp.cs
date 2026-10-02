// src/game/camp.mjs
// The camp as a real place. A three-by-three chamber is opened in solid, unreachable rock on the
// level the party is already standing on, and every block it borrows is put back exactly as it was
// when they leave. Nothing is loaded, no level changes and no script fires, so no story event can
// go off by accident, and the chamber is never marked as seen, so it leaves the automap alone.
//
// Each of the four walls carries a door with an emblem on it saying what is behind it, and
// clicking one opens it - the same way the game's own shopkeepers are approached. The door itself
// is the level's own door; only the emblem is the port's, drawn here in the level's palette.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lol
{
    /// <summary>camp.mjs CAMP_PIECES</summary>
    public sealed class CampPiece
    {
        public int dir;
        public string name, label, scene;
    }

    /// <summary>camp.mjs uiCampRoom / uiCampAlcove</summary>
    public sealed class CampRoom
    {
        public int centre;
        public int[] mid, corners, outside, all;
        public bool alcove;
    }

    /// <summary>camp.mjs uiEnterCamp: campReturn.snapshot entries</summary>
    public sealed class CampSnapshot
    {
        public int block;
        public byte[] walls;
        public int flags, direction;
    }

    /// <summary>camp.mjs uiEnterCamp: this.campReturn</summary>
    public sealed class CampReturn
    {
        public int level, block, direction;
        public double settled;
        public CampRoom room;
        public List<CampSnapshot> snapshot;
    }

    /// <summary>camp.mjs uiCampView</summary>
    public sealed class CampView
    {
        public string piece;
        public int distance;
    }

    /// <summary>camp.mjs uiTravelTargets entries</summary>
    public sealed class TravelTarget
    {
        public int level;
        public string name;
        public int block, direction;
    }

    /// <summary>camp.mjs uiTravelReady</summary>
    public sealed class TravelReady
    {
        public bool ok;
        public string why;
    }

    public sealed partial class LandsOfLore
    {
        const int WALL_BASE = 240; // wall types 240..243 are unused by every LEVEL<N>.WLL in the game
        const int GRID = 32; // blocks are laid out 32 x 32

        public static readonly CampPiece[] CAMP_PIECES =
        {
            new CampPiece { dir = 0, name = "travel", label = "The travelling circle", scene = "src/assets/camp/scene-travel.png" },
            new CampPiece { dir = 1, name = "craft", label = "The cauldron", scene = "src/assets/camp/scene-cauldron.png" },
            new CampPiece { dir = 2, name = "stash", label = "The stash chest", scene = "src/assets/camp/scene-chest.png" },
            new CampPiece { dir = 3, name = "imp", label = "The imp trader", scene = "src/assets/camp/scene-imp.png" },
        };

        static int Camp_col(int b) => b & (GRID - 1);
        static int Camp_row(int b) => b >> 5;

        // ---- CampMixin ----
        public CampReturn campReturn;
        public Dictionary<int, List<int>> campSeen;
        public bool campHealTimer;
        /// <summary>set by the host (the hearth upgrade): 2, or 1</summary>
        public int campHealBoost;

        // The nine blocks of a chamber centred on `centre`, plus the four blocks outside its walls.
        // Rejects anything that would wrap around the edge of the map: those blocks look adjacent by
        // index but are on the far side of the level, and opening a wall between them is a hole.
        public CampRoom uiCampRoom(int centre)
        {
            int c = Camp_col(centre); int r = Camp_row(centre);
            if (c < 2 || c > GRID - 3 || r < 2 || r > GRID - 3) return null;
            Func<int, int, int> at = (dc, dr) => centre + dc + dr * GRID;
            int[] mid = { at(0, -1), at(1, 0), at(0, 1), at(-1, 0) };
            int[] corners = { at(1, -1), at(1, 1), at(-1, 1), at(-1, -1) };
            int[] outside = { at(0, -2), at(2, 0), at(0, 2), at(-2, 0) };
            int[] all = new[] { centre }.Concat(mid).Concat(corners).ToArray();
            if (new HashSet<int>(all.Concat(outside)).Count != 13) return null;
            for (int d = 0; d < 4; d += 1)
            {
                if (calcNewBlockPosition(centre, d) != mid[d]) return null;
                if (calcNewBlockPosition(mid[d], d) != outside[d]) return null;
            }
            return new CampRoom { centre = centre, mid = mid, corners = corners, outside = outside, all = all };
        }

        // A single block with a door on each wall: the fallback where a level is too crowded.
        public CampRoom uiCampAlcove(int centre)
        {
            int c = Camp_col(centre); int r = Camp_row(centre);
            if (c < 1 || c > GRID - 2 || r < 1 || r > GRID - 2) return null;
            int[] outside = { centre - GRID, centre + 1, centre + GRID, centre - 1 };
            for (int d = 0; d < 4; d += 1) if (calcNewBlockPosition(centre, d) != outside[d]) return null;
            if (new HashSet<int>(new[] { centre }.Concat(outside)).Count != 5) return null;
            return new CampRoom { centre = centre, mid = new[] { centre, centre, centre, centre }, corners = new int[0], outside = outside, all = new[] { centre }, alcove = true };
        }

        public bool uiCampRock(int b)
        {
            if (b < 0 || b > 1023 || b == currentBlock) return false;
            var l = levelBlockProperties[b];
            if ((l.flags & 7) != 0 || l.assignedObjects != 0 || l.drawObjects != 0) return false;
            for (int d = 0; d < 4; d += 1) if (checkBlockPassability(b, d)) return false;
            return true;
        }

        // Somewhere the camp can be cut. Every block of the chamber, and every block it touches, has to
        // be solid rock: one soft neighbour anywhere on the ring is a way out into the real level.
        public CampRoom uiFindCampRoom()
        {
            Func<CampRoom, bool> fits = (room) =>
            {
                if (room == null) return false;
                var inside = new HashSet<int>(room.all);
                foreach (int b in room.all.Concat(room.outside)) if (!uiCampRock(b)) return false;
                foreach (int b in room.all)
                {
                    for (int d = 0; d < 4; d += 1)
                    {
                        int n = calcNewBlockPosition(b, d);
                        if (!inside.Contains(n) && !uiCampRock(n)) return false;
                    }
                }
                return true;
            };
            for (int b = 0; b < 1024; b += 1) { var room = uiCampRoom(b); if (fits(room)) return room; }
            for (int b = 0; b < 1024; b += 1) { var room = uiCampAlcove(b); if (fits(room)) return room; }
            return null;
        }

        // A door for each wall, in the level's own style, with the camp's emblem on it. The emblem
        // replaces the door's own ornament at every view distance, so it shrinks with the wall.
        public void uiCampFurnish()
        {
            int door = -1;
            for (int w = 1; w < 256 && door < 0; w += 1)
            {
                if ((wllAutomapData[w] & 0x1f) == 13 && wllVmpMap[w] != 0 && (wllWallFlags[w] & 1) == 0) door = w;
            }
            foreach (var piece in CAMP_PIECES)
            {
                int wall = WALL_BASE + piece.dir;
                wllVmpMap[wall] = door > 0 ? wllVmpMap[door] : (byte)1;
                wllShapeMap[wall] = door > 0 ? wllShapeMap[door] : (sbyte)0;
                wllWallFlags[wall] = 0x0f; // shut: the party never walks through it
                specialWallTypes[wall] = 1; // clicking it is what opens the room behind
                wllAutomapData[wall] = 0;
            }
        }

        // Step into the camp. Returns false when there is nowhere to cut it.
        // Living monsters within three blocks of the party. You cannot pitch a camp while any of them is
        // that close: the tent would be found before the first hour passed.
        public List<Monster> uiCampThreats(int range = 3)
        {
            if (currentBlock == 0) return new List<Monster>();
            return monsters.Where(m => m.properties != null && m.hitPoints > 0 && m.mode < 13 && m.block != 0
                && getBlockDistance(currentBlock, m.block) <= range).ToList();
        }

        public bool uiEnterCamp()
        {
            if (campReturn != null) return true;
            if (uiCampThreats().Count != 0) return false; // checked here too: every path in is covered
            var room = uiFindCampRoom();
            if (room == null) return false;
            uiCampFurnish();
            // Everything the chamber touches is copied before a single wall is moved, and put back from
            // that copy: the level can never be left changed, whatever happens in between.
            var touched = new List<int>();   // a Set: insertion order matters below
            var touchedSet = new HashSet<int>();
            Action<int> touch = b => { if (touchedSet.Add(b)) touched.Add(b); };
            foreach (int b in room.all) touch(b);
            foreach (int b in room.all) for (int d = 0; d < 4; d += 1) touch(calcNewBlockPosition(b, d));
            // Anything a camp on this level touched before is scrubbed first: if a restore was ever missed,
            // the old chamber would otherwise stay drawn on the automap for the rest of the game.
            campSeen = campSeen ?? new Dictionary<int, List<int>>();
            foreach (int b in campSeen.TryGetValue(currentLevel, out var seenBefore) ? seenBefore : new List<int>())
            {
                if (!touchedSet.Contains(b)) levelBlockProperties[b].flags &= ~7;
            }
            campSeen[currentLevel] = touched.ToList();
            var snapshot = touched.Select(b =>
            {
                var l = levelBlockProperties[b];
                return new CampSnapshot { block = b, walls = l.walls.ToArray(), flags = l.flags, direction = l.direction };
            }).ToList();
            var inside = new HashSet<int>(room.all);
            foreach (int b in room.all)
            { // open every wall between two blocks of the chamber
                for (int d = 0; d < 4; d += 1)
                {
                    int n = calcNewBlockPosition(b, d);
                    if (inside.Contains(n)) { levelBlockProperties[b].walls[d] = 0; levelBlockProperties[n].walls[d ^ 2] = 0; }
                }
            }
            // The wall the party looks at belongs to the block in front of them, on the side facing back.
            foreach (var piece in CAMP_PIECES) levelBlockProperties[room.outside[piece.dir]].walls[piece.dir ^ 2] = (byte)(WALL_BASE + piece.dir);
            campReturn = new CampReturn
            {
                level = currentLevel,
                block = currentBlock,
                direction = currentDirection,
                settled = getMillis() + 2000,
                room = room,
                snapshot = snapshot,
            };
            currentBlock = room.centre;
            currentDirection = 0;
            (partyPosX, partyPosY) = calcCoordinates(room.centre, 0x80, 0x80);
            sceneDefaultUpdate = 1;
            sceneUpdateRequired = true;
            gui_drawScene(0);
            return true;
        }

        // Put every borrowed block back from the copy and return the party to where they made camp.
        public bool uiLeaveCamp()
        {
            var r = campReturn;
            if (r == null) return false;
            foreach (var s in r.snapshot)
            {
                var l = levelBlockProperties[s.block];
                Js.Set(l.walls, s.walls);
                l.flags = s.flags & ~7; // whatever the party saw of the chamber is forgotten with it
                l.direction = s.direction;
            }
            campReturn = null;
            currentBlock = r.block;
            currentDirection = r.direction;
            (partyPosX, partyPosY) = calcCoordinates(r.block, 0x80, 0x80);
            sceneDefaultUpdate = 1;
            sceneUpdateRequired = true;
            gui_drawScene(0);
            return true;
        }

        // Something hostile has walked up to where the party left their things: the camp breaks and they
        // are back on their feet, exactly where they were.
        public bool uiCampIntruded()
        {
            var r = campReturn;
            if (r == null) return false;
            if (getMillis() < r.settled) return false; // a moment to sit down first
            return monsters.Any(m =>
            {
                if (m.properties == null || m.hitPoints <= 0 || m.mode >= 13 || m.block == 0) return false;
                int d = getBlockDistance(r.block, m.block);
                return d == 0 || (d == 1 && m.mode >= 5);
            });
        }

        public bool uiInCamp()
        {
            return campReturn != null;
        }

        // The camp door in front of the party and how far off it is: right in front of them, or across
        // the room with an open block between. Read from the walls themselves, exactly the way
        // clickedWall finds what a click landed on, so the two can never disagree.
        public CampView uiCampView()
        {
            if (campReturn == null) return null;
            int dir = currentDirection;
            int back = dir ^ 2;
            Func<int, string> door = (b) =>
            {
                int wall = levelBlockProperties[b].walls[back];
                if (wall < WALL_BASE || wall > WALL_BASE + 3) return null;
                var piece = CAMP_PIECES.FirstOrDefault(p => p.dir == wall - WALL_BASE);
                return piece != null ? piece.name : null;
            };
            int ahead = calcNewBlockPosition(currentBlock, dir);
            if (ahead == currentBlock) return null;
            string near = door(ahead);
            if (!string.IsNullOrEmpty(near)) return new CampView { piece = near, distance = 1 };
            if (testWallFlag(ahead, dir, 1)) return null; // something solid in the way
            int far = calcNewBlockPosition(ahead, dir);
            if (far == ahead) return null;
            string next = door(far);
            return !string.IsNullOrEmpty(next) ? new CampView { piece = next, distance = 2 } : null;
        }

        // Only what the party can reach out and touch answers a click.
        public string uiCampFacing()
        {
            var view = uiCampView();
            return view != null && view.distance == 1 ? view.piece : null;
        }

        // Resting: health and magic come back while the party stays in the camp. Its own timer (id 13 is
        // unused by the game), so they are awake and the chamber stays on screen.
        public void uiCampStartHealing()
        {
            if (campHealTimer) return;
            campHealTimer = true;
            addTimer(13, _ => uiCampHeal(), 20, true);
        }

        public void uiCampHeal()
        {
            if (campReturn == null) return;
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0 || (c.flags & 8) != 0 || (c.flags & 0x80) != 0) continue; // the camp cannot mend poison
                // A point a tick is a crawl for a hero with a hundred and sixty hit points. The original
                // rest scales its pace to the largest pool in the party for the same reason: what matters is
                // how long a full recovery takes, not how many points go by.
                bool changed = false;
                double pace = 40.0 / (campHealBoost != 0 ? campHealBoost : 1); // a warm hearth (camp upgrade) halves the wait
                if (c.hitPointsCur < c.hitPointsMax)
                {
                    int step = Math.Max(1, Js.Ceil(c.hitPointsMax / pace));
                    increaseCharacterHitpoints(i, Math.Min(step, c.hitPointsMax - c.hitPointsCur), false);
                    changed = true;
                }
                if (c.magicPointsCur < c.magicPointsMax)
                {
                    int step = Math.Max(1, Js.Ceil(c.magicPointsMax / pace));
                    c.magicPointsCur = Math.Min(c.magicPointsMax, c.magicPointsCur + step);
                    changed = true;
                }
                if (changed) gui_drawCharPortraitWithStats(i);
            }
        }

        // Sleeping through a stretch of the night: the slow healing tick is skipped and the party wakes
        // mended, but the brews they drank have long worn off. Returns the hours actually slept.
        public double uiCampSleep(double hours)
        {
            if (campReturn == null || !(hours > 0)) return 0;
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0) continue;
                // Potion buffs do not survive half a day: take back what each one added (the same undo the
                // 9/10/11 timer events do when they expire).
                for (int e = 0; e < 5; e += 1)
                {
                    int type = c.characterUpdateEvents[e];
                    if (type < 9 || type > 11) continue;
                    int skill = type - 9;
                    c.skillModifiers[skill] -= (sbyte)c.potionSkillBonus[skill];
                    c.potionSkillBonus[skill] = 0;
                    c.characterUpdateEvents[e] = 0;
                    c.characterUpdateDelay[e] = 0;
                }
                if ((c.flags & 8) != 0 || (c.flags & 0x80) != 0) continue; // the camp cannot mend poison, or the dead
                if (c.hitPointsCur < c.hitPointsMax) increaseCharacterHitpoints(i, c.hitPointsMax - c.hitPointsCur, false);
                c.magicPointsCur = c.magicPointsMax;
                gui_drawCharPortraitWithStats(i);
            }
            playTimer += hours * 3600;
            return hours;
        }

        // Where the party could travel to: every level they have been to, as the meta record remembers
        // them, without the one they are standing on.
        public List<TravelTarget> uiTravelTargets()
        {
            var @out = new List<TravelTarget>();
            foreach (var e in meta != null ? meta.visited : new Dictionary<int, VisitedEntry>())
            {
                int n = e.Key;
                var where = e.Value;
                if (where == null || n == currentLevel) continue;
                @out.Add(new TravelTarget { level = n, name = levelName(n), block = where.block, direction = where.dir != null ? where.dir.Value : where.direction });
            }
            return @out.OrderBy(t => t.level).ToList();
        }

        // Whether the party may set out, and why not when they may not. The party travel from a camp they
        // have rested in - never from the middle of a corridor - and the casting takes five seconds that
        // anything breaking the camp also breaks.
        public TravelReady uiTravelReady(int? level)
        {
            if (!uiInCamp()) return new TravelReady { ok = false, why = "Make camp first: the party can only set out from a camp." };
            if (uiInCombat()) return new TravelReady { ok = false, why = "Not while something is attacking the camp." };
            var hurt = characters.Where(c => (c.flags & 1) != 0 && (c.hitPointsCur < c.hitPointsMax || c.magicPointsCur < c.magicPointsMax)).ToList();
            if (hurt.Count != 0) return new TravelReady { ok = false, why = $"Rest until everyone has recovered ({string.Join(", ", hurt.Select(c => c.name))} still resting)." };
            if (level == null) return new TravelReady { ok = false, why = "Choose where to teleport the party." };
            if (!uiTravelTargets().Any(t => t.level == level)) return new TravelReady { ok = false, why = "The party have never been there." };
            return new TravelReady { ok = true, why = "The spell takes five seconds to cast. Anything that breaks the camp breaks the spell." };
        }

        // How long the casting takes, in seconds; the host draws the waiting.
        public int uiTravelSeconds()
        {
            return 5;
        }

        public bool uiCampRested()
        {
            return !characters.Any(c => (c.flags & 1) != 0 && (c.flags & 8) == 0 && (c.hitPointsCur < c.hitPointsMax || c.magicPointsCur < c.magicPointsMax));
        }
    }
}
