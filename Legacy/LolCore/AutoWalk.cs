// Walking the party somewhere by itself: the route, the step, and the two things the host asks for
// before it can offer the walk at all - is there a way there, and where is the nearest unexplored
// block. host-ui.mjs findPath, nearestUnexplored, autoWalkTo and autoWalkStep.
//
// The route is breadth-first over blocks the party may actually enter, so the first one found is the
// shortest. Only explored blocks are crossed - the party will not walk through a wall it has never
// seen - but the destination itself may be unexplored, which is what makes "explore" work.
namespace LolCore;

public sealed partial class LevelLoader
{
    /// <summary>
    /// findPath: the shortest way from one block to another, as the directions to walk, or null when
    /// there is none. Explored blocks only, except the destination.
    /// </summary>
    public List<int> FindPath(int from, int to)
    {
        if (from == to) return new List<int>();
        var prev = new short[1024];
        var via = new byte[1024];
        for (int i = 0; i < 1024; i += 1) prev[i] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        prev[from] = (short)from;
        while (queue.Count > 0)
        {
            int block = queue.Dequeue();
            for (int dir = 0; dir < 4; dir += 1)
            {
                int next = Party.CalcNewBlockPosition(block, dir);
                if (prev[next] != -1) continue;
                if ((Map.Flags[next] & 7) != 7 && next != to) continue;
                if (!Party.CheckBlockPassability(next, dir)) continue;
                prev[next] = (short)block;
                via[next] = (byte)dir;
                if (next == to)
                {
                    var path = new List<int>();
                    for (int b = to; b != from; b = prev[b]) path.Insert(0, via[b]);
                    return path;
                }
                queue.Enqueue(next);
            }
        }
        return null;
    }

    /// <summary>nearestUnexplored: the closest block the party may reach and has not seen yet.</summary>
    public int NearestUnexplored()
    {
        var seen = new bool[1024];
        var queue = new Queue<int>();
        int start = Party.Block;
        queue.Enqueue(start);
        seen[start] = true;
        while (queue.Count > 0)
        {
            int block = queue.Dequeue();
            for (int dir = 0; dir < 4; dir += 1)
            {
                int next = Party.CalcNewBlockPosition(block, dir);
                if (seen[next] || !Party.CheckBlockPassability(next, dir)) continue;
                if ((Map.Flags[next] & 7) != 7) return next;
                seen[next] = true;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

}

public sealed partial class Gui
{
    /// <summary>The turns and steps still to take, as directions; empty when nothing is walking.</summary>
    private readonly List<int> _autoWalk = new();

    /// <summary>The party's health when the walk was last checked: losing any of it stops the walk.</summary>
    private int _autoWalkHp;

    /// <summary>Whether the party is walking somewhere on its own.</summary>
    public bool AutoWalking => _autoWalk.Count > 0;

    private int PartyHitPoints()
    {
        int sum = 0;
        foreach (var c in Characters) if (c.Active) sum += c.HitPointsCur;
        return sum;
    }

    /// <summary>autoWalkTo: start walking to a block, or say there is no known way there.</summary>
    public bool AutoWalkTo(int target)
    {
        var path = _loader.FindPath(_loader.Party.Block, target);
        _autoWalk.Clear();
        if (path == null || path.Count == 0) return false;
        _autoWalk.AddRange(path);
        _autoWalkHp = PartyHitPoints();
        return true;
    }

    /// <summary>Stop walking - what the host calls when the player takes over.</summary>
    public void StopAutoWalk() => _autoWalk.Clear();

    /// <summary>
    /// autoWalkStep: one turn or one step, and anything unexpected hands control back. Called once a
    /// frame while AutoWalking.
    /// </summary>
    public void AutoWalkStep()
    {
        if (_autoWalk.Count == 0) return;
        int hp = PartyHitPoints();
        if (_loader.UpdateFlags != 0 || WeaponsDisabled || _loader.NeedSceneRestore
            || _loader.SysTimerPaused || !PartyAwake || hp < _autoWalkHp)
        {
            _autoWalk.Clear();
            return;
        }
        _autoWalkHp = hp;
        int dir = _autoWalk[0];
        var button = new GuiButton { Arg = 0, Flags2 = 0x80 };
        if (dir != _loader.Party.Direction)
        {
            // Turn the short way round, as the arrows do.
            if (((_loader.Party.Direction - dir) & 3) == 1) ClickedTurnLeftArrow(button);
            else ClickedTurnRightArrow(button);
            return;
        }
        int before = _loader.Party.Block;
        bool talking = _loader.PendingScript != null;
        ClickedUpArrow(button);
        // Blocked, or a script stopped us on the way: the player takes it from here.
        if (_loader.Party.Block == before || (_loader.PendingScript != null) != talking)
        {
            _autoWalk.Clear();
            return;
        }
        _autoWalk.RemoveAt(0);
    }
}
