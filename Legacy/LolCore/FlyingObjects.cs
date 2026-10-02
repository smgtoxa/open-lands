// Things in the air: a thrown dagger, a fired bolt, a dropped rock.
//
// Transliterated from src/game/items.mjs (launchObject, endObjectFlight, processObjectFlight,
// updateObjectFlightPosition, objectFlightProcessHits, updateFlyingObject) and the
// timerProcessFlyingObjects half of src/game/party.mjs.
//
// Eight slots, stepped by their own timer. Without them a script's distanceAttack had nothing to
// launch, so every ranged attack in the game simply did not happen.
namespace LolCore;

public sealed class FlyingObject
{
    public int Enable;
    public int ObjectType;
    public int Item;
    public int X, Y;
    public int FlyingHeight;
    public int Direction;
    public int Distance;
    public int AttackerId;
    public int Flags;
    public int WallFlags;
    public int C;
}

public sealed partial class MonsterBoard
{
    public readonly FlyingObject[] FlyingObjects =
        { new(), new(), new(), new(), new(), new(), new(), new() };

    /// <summary>Asked for when something lands on the party while they are resting.</summary>
    public Action WakeParty;

    /// <summary>Asked for when an object in flight needs a level or item script run.</summary>
    public Action<int, int, int, int> RunBlockScript;      // block, flags, charNum, item
    public Action<int, int, int, int> RunItemScriptFor;    // charNum, item, flags, next

    /// <summary>getNearestMonsterFromPos.</summary>
    public int GetNearestMonsterFromPos(int x, int y)
    {
        int id = 0xffff, minDist = 0x7fff;
        for (int i = 0; i < 30; i += 1)
        {
            if (Monsters[i].Mode > 13) continue;
            int d = Math.Abs(x - Monsters[i].X) + Math.Abs(y - Monsters[i].Y);
            if (d < minDist) { minDist = d; id = 0x8000 | i; }
        }
        return id;
    }

    /// <summary>launchObject: the slot the new object takes, evicting the most distant if need be.</summary>
    public bool LaunchObject(int objectType, int item, int startX, int startY, int flyingHeight,
                             int direction, int attackerId, int c)
    {
        int space = CheckDrawObjectSpace(Party.PosX, Party.PosY, startX, startY);
        int slot = -1, i = 0;
        for (; i < 8; i += 1)
        {
            var other = FlyingObjects[i];
            if (other.Enable == 0) { space = -1; break; }
            int otherSpace = CheckDrawObjectSpace(Party.PosX, Party.PosY, other.X, other.Y);
            if (otherSpace > space) { space = otherSpace; slot = i; }
        }
        if (space != -1 && slot != -1)
        {
            i = slot;
            EndObjectFlight(FlyingObjects[i], startX, startY, 8);
        }
        if (i == 8) return false;

        var t = FlyingObjects[i];
        t.Enable = 1;
        t.ObjectType = objectType;
        t.Item = item;
        t.X = startX;
        t.Y = startY;
        t.FlyingHeight = flyingHeight;
        t.Direction = direction;
        t.Distance = 255;
        t.AttackerId = attackerId;
        t.Flags = 7;
        t.WallFlags = 2;
        t.C = c;
        if (attackerId != -1)
        {
            if ((attackerId & 0x8000) != 0) t.Flags &= 0xfd;
            else
            {
                t.Flags &= 0xfb;
                IncreaseExperience(attackerId, 1, 2);
            }
        }
        UpdateObjectFlightPosition(t);
        return true;
    }

    private void UpdateObjectFlightPosition(FlyingObject t)
    {
        if (t.ObjectType == 0) SetItemPosition(t.Item, t.X, t.Y, t.FlyingHeight, t.FlyingHeight == 0);
        else if (t.ObjectType == 1)
        {
            if (t.FlyingHeight == 0) Items.Delete(t.Item);
            else SetItemPosition(t.Item, t.X, t.Y, t.FlyingHeight, false);
        }
    }

    private void ProcessObjectFlight(FlyingObject t, int x, int y)
    {
        int block = CalcBlockIndex(t.X, t.Y);
        RemoveAssignedObject(block, t.Item);
        RemoveDrawObject(block, t.Item);
        t.X = x;
        t.Y = y;
        UpdateObjectFlightPosition(t);
    }

    /// <summary>objectFlightProcessHits: what the thing it struck makes of it.</summary>
    private void ObjectFlightProcessHits(FlyingObject t, int x, int y, int collisionType)
    {
        var it = Items.InPlay[t.Item];
        if (collisionType == 1)
        {
            RunBlockScript?.Invoke(Party.CalcNewBlockPosition(it.Block, t.Direction >> 1), 0x8000, -1, t.Item);
        }
        else if (collisionType == 2)
        {
            if ((Items.Properties[it.ItemPropertyIndex].Flags & 0x4000) != 0)
            {
                int obj = _map.AssignedObjects[it.Block & 0x3ff];
                for (int guard = 0; (obj & 0x8000) != 0 && guard < 64; guard += 1)
                {
                    RunItemScriptFor?.Invoke(t.AttackerId, t.Item, 0x8000, obj);
                    obj = Monsters[obj & 0x7fff].NextAssignedObject;
                }
            }
            else RunItemScriptFor?.Invoke(t.AttackerId, t.Item, 0x8000, GetNearestMonsterFromPos(x, y));
        }
        else if (collisionType == 4)
        {
            WakeParty?.Invoke();   // a bolt landing on the party ends their rest
            if ((Items.Properties[it.ItemPropertyIndex].Flags & 0x4000) != 0)
            {
                for (int i = 0; i < 4; i += 1)
                    if ((Characters[i].Flags & 1) != 0) RunItemScriptFor?.Invoke(t.AttackerId, t.Item, 0x8000, i);
            }
            else RunItemScriptFor?.Invoke(t.AttackerId, t.Item, 0x8000, GetNearestPartyMemberFromPos(x, y));
        }
    }

    private void EndObjectFlight(FlyingObject t, int x, int y, int collisionType)
    {
        int cx = x, cy = y;
        int block = CalcBlockIndex(t.X, t.Y);
        RemoveAssignedObject(block, t.Item);
        RemoveDrawObject(block, t.Item);
        if (collisionType == 1) { cx = t.X; cy = t.Y; }
        if (t.ObjectType == 0 || t.ObjectType == 1)
        {
            ObjectFlightProcessHits(t, cx, cy, collisionType);
            t.X = (cx & 0xffc0) | 0x40;
            t.Y = (cy & 0xffc0) | 0x40;
            t.FlyingHeight = 0;
            UpdateObjectFlightPosition(t);
        }
        t.Enable = 0;
    }

    /// <summary>updateFlyingObject: one step of flight, and what stops it.</summary>
    public void UpdateFlyingObject(FlyingObject t)
    {
        var (x, y) = GetNextStepCoords(t.X, t.Y, t.Direction);
        int collisionType = CheckBlockBeforeObjectPlacement(x, y, 63, t.Flags, t.WallFlags);
        if (collisionType != 0) EndObjectFlight(t, x, y, collisionType);
        else if (--t.Distance != 0) ProcessObjectFlight(t, x, y);
        else EndObjectFlight(t, x, y, 8);
    }

    /// <summary>timerProcessFlyingObjects: every slot in the air moves.</summary>
    public void TimerProcessFlyingObjects()
    {
        for (int i = 0; i < 8; i += 1)
        {
            if (FlyingObjects[i].Enable == 0) continue;
            UpdateFlyingObject(FlyingObjects[i]);
        }
    }
}
