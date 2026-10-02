// Monster behaviour: the mode machine the engine steps twice a second, and the walking that comes
// out of it. Transliterated from src/game/monsters.mjs (updateMonster, moveMonster, walkMonster,
// walkMonsterCalcNextStep, moveStrayingMonster, alignMonsterToParty, setMonsterMode and friends).
//
// The fighting half is not here: an attack needs the party - its characters, their armour and the
// dice rolled against them - and the party is a later stage. A monster that reaches the party and
// swings is where this port stops being able to follow the engine, and the watch harness says so.
namespace LolCore;

public sealed partial class MonsterBoard
{
    private static readonly int[] StepX = { 0, 32, 32, 32, 0, -32, -32, -32 };
    private static readonly int[] StepY = { -32, -32, 0, 32, 32, 32, 0, -32 };
    private static readonly int[] ModeFlags = { 1, 0, 1, 3, 3, 0, 0, 3, 4, 1, 0, 0, 4, 0, 0 };
    private static readonly int[] TurnPos = { 0, 2, 6, 6, 0, 2, 4, 4, 2, 2, 4, 6, 0, 0, 4, 6, 0 };
    private static readonly int[] DirectionTable = { 1, 2, 1, 0, 7, 6, 7, 0, 3, 2, 3, 4, 5, 6, 5, 4 };
    private static readonly int[] Modifiers3 = { 320, 256, 128 };

    private int _stepCounter;
    private int _stepMode;
    private int _objectLastDirection;
    private int _monsterCurBlock;

    /// <summary>The party: monsters chase it, so the AI needs where it stands.</summary>
    public Party Party;

    /// <summary>Set when a monster would have attacked the party - the part this port cannot do.</summary>
    public int AttacksAttempted;

    /// <summary>
    /// Asks the scene pass whether a block is inside the view built from (block, direction) - the
    /// question updateMonsterAdjustBlocks answers by borrowing the scene's clip machinery.
    /// </summary>
    public Func<int, int, int, bool> ClipVisibleFrom;

    /// <summary>timerProcessMonsters: the engine updates the odd and even halves on two timers.</summary>
    public void ProcessMonsters(int timerNum)
    {
        for (int i = timerNum & 0x0f; i < 30; i += 2) UpdateMonster(Monsters[i]);
    }

    public void UpdateMonster(Monster monster)
    {
        if (monster.Mode > 14 || monster.Properties == null) return;
        int f = ModeFlags[monster.Mode];
        if (monster.SpeedTick++ < monster.Properties.SpeedTotalWaitTicks && (f & 4) == 0) return;
        monster.SpeedTick = 0;
        if ((monster.Properties.Flags & 0x40) != 0)
            monster.HitPoints = Math.Min(monster.Properties.HitPoints, monster.HitPoints + _rng.RollDice(1, 8));
        if ((monster.Flags & 8) != 0)
        {
            monster.DestX = Party.PosX;
            monster.DestY = Party.PosY;
        }
        if ((f & 2) != 0 && UpdateMonsterAdjustBlocks(monster))
        {
            SetMonsterMode(monster, 7);
            f &= 6;
        }
        if ((f & 1) != 0 && (monster.Flags & 0x10) != 0) SetMonsterMode(monster, 7);

        if (monster.Mode != 11 && monster.Mode != 14 && (Random(255) & 3) == 0) monster.ShiftStep = (monster.ShiftStep + 1) & 0x0f;

        switch (monster.Mode)
        {
            case 0:
            case 1:
                if ((monster.Flags & 0x10) != 0)
                {
                    for (int i = 0; i < 30; i += 1) if (Monsters[i].Mode == 1) SetMonsterMode(Monsters[i], 7);
                }
                else if (monster.Mode == 1) MoveMonster(monster);
                break;
            case 2:
                MoveMonster(monster);
                break;
            case 3:
                if (UpdateMonsterAdjustBlocks(monster)) SetMonsterMode(monster, 7);
                for (int i = 0; i < 4; i += 1) if (Party.CalcNewBlockPosition(monster.Block, i) == Party.Block) SetMonsterMode(monster, 7);
                break;
            case 4:
                MoveStrayingMonster(monster);
                break;
            case 5:
                monster.FightCurTick -= 1;
                if (monster.FightCurTick <= 0 || CheckDrawObjectSpace(Party.PosX, Party.PosY, monster.X, monster.Y) > 256 || (monster.Flags & 8) != 0)
                    SetMonsterMode(monster, 7);
                else AlignMonsterToParty(monster);
                break;
            case 6:
                if (--monster.FightCurTick <= 0) SetMonsterMode(monster, 7);
                break;
            case 7:
                if (!ChasePartyWithDistanceAttacks(monster)) ChasePartyWithCloseAttacks(monster);
                break;
            case 8:
                if (++monster.FightCurTick > 2)
                {
                    SetMonsterMode(monster, 5);
                    // A zero here is Infinity in the engine, and the shift below makes that 0.
                    int swing = monster.Properties.FightingStats[4];
                    monster.FightCurTick = (sbyte)(swing == 0 ? 0 : (((8 << 8) / swing) * Modifiers3[Difficulty]) >> 8);
                }
                break;
            case 9:
                if (--monster.FightCurTick != 0) ChasePartyWithCloseAttacks(monster);
                else
                {
                    SetMonsterMode(monster, 7);
                    monster.Flags &= 0xfff7;
                }
                break;
            case 12:
                monster.FightCurTick += 1;
                break;
            case 13:
                if (++monster.FightCurTick > 2) KillMonster(monster);
                break;
            case 14:
                monster.DamageReceived = 0;
                break;
        }

        if (monster.DamageReceived != 0)
        {
            if ((monster.DamageReceived & 0x8000) != 0) monster.DamageReceived &= 0x7fff;
            else monster.DamageReceived = 0;
        }
        monster.Flags &= 0xffef;
    }

    /// <summary>engine.random(range): the same draw the JavaScript build makes.</summary>
    private int Random(int range) => (int)(_rng.NextDouble() * (range + 1));

    public void SetMonsterMode(Monster monster, int mode)
    {
        if (monster.Mode == 13 && mode != 14) return;
        if (mode == 7)
        {
            monster.DestX = Party.PosX;
            monster.DestY = Party.PosY;
        }
        if (monster.Mode == 1 && mode == 7)
        {
            for (int i = 0; i < 30; i += 1)
            {
                if (monster.Mode != 1) continue;
                monster.Mode = mode;
                monster.FightCurTick = 0;
                monster.DestX = Party.PosX;
                monster.DestY = Party.PosY;
                SetMonsterDirection(monster, CalcMonsterDirection(monster.X, monster.Y, monster.DestX, monster.DestY));
            }
            return;
        }
        monster.Mode = mode;
        monster.FightCurTick = 0;
        if (mode == 14) monster.HitPoints = 0;
        if (mode == 13 && (monster.Flags & 0x20) != 0)
        {
            monster.Mode = 0;
            MonsterDropItems(monster);
            SetMonsterMode(monster, 14);
            OnMonsterKilled?.Invoke(monster.Id);
            if (monster.Mode == 14) Place(monster, 0, 0);
        }
    }

    public void KillMonster(Monster monster)
    {
        SetMonsterMode(monster, 14);
        MonsterDropItems(monster);
        int w = _map.Walls[monster.Block, 0];
        int flags = _map.Flags[monster.Block];
        if (_walls.VmpMap[w] == 0 && _walls.ShapeMap[w] == 0 && (flags & 0x40) == 0 && (monster.Properties.Flags & 0x1000) == 0)
            _map.Flags[monster.Block] |= 0x80;
        Place(monster, 0, 0);
    }

    public static int CalcMonsterDirection(int x1, int y1, int x2, int y2)
    {
        int r = 0;
        int t1 = (short)(y1 - y2);
        if (t1 < 0) { r += 1; t1 = -t1; }
        r <<= 1;
        int t2 = (short)(x2 - x1);
        if (t2 < 0) { r += 1; t2 = -t2; }
        int f = t1 > t2 ? 1 : 0;
        if (t2 >= t1) (t1, t2) = (t2, t1);
        r = (r << 1) | f;
        t1 = (t1 + 1) >> 1;
        f = t1 > t2 ? 1 : 0;
        r = (r << 1) | f;
        return DirectionTable[r];
    }

    public void SetMonsterDirection(Monster monster, int dir)
    {
        monster.Direction = dir;
        if ((dir & 1) == 0 || monster.Direction - (monster.Facing << 1) >= 2) monster.Facing = monster.Direction >> 1;
    }

    private void MoveMonster(Monster monster)
    {
        if (monster.X != monster.DestX || monster.Y != monster.DestY) WalkMonster(monster);
        else if (monster.Direction != monster.DestDirection)
            SetMonsterDirection(monster, TurnPos[(monster.Facing << 2) + (monster.DestDirection >> 1)]);
    }

    private void WalkMonster(Monster monster)
    {
        if ((monster.Properties.Flags & 0x400) != 0) return;
        int s = WalkMonsterCalcNextStep(monster);
        if (s == -1)
        {
            if (WalkMonsterCheckDest(monster.X, monster.Y, monster, 4) != 1) return;
            _objectLastDirection ^= 4;
            SetMonsterDirection(monster, _objectLastDirection);
        }
        else
        {
            SetMonsterDirection(monster, s);
            if (monster.NumDistAttacks != 0
                && GetBlockDistance(monster.Block, Party.Block) >= 2
                && CheckForPossibleDistanceAttack(monster.Block, monster.Direction, 3, Party.Block) != 5
                && monster.DistAttackTick != 0) return;
        }
        var (fx, fy) = GetNextStepCoords(monster.X, monster.Y, s == -1 ? _objectLastDirection : s);
        Place(monster, fx, fy);
    }

    private int WalkMonsterCalcNextStep(Monster monster)
    {
        int[] table1 = { 7, -6, 5, -4, 3, -2, 1, 0 };
        int[] table2 = { -7, 6, -5, 4, -3, 2, -1, 0 };
        if (++_stepCounter > 10)
        {
            _stepCounter = 0;
            _stepMode ^= 1;
        }
        var tbl = _stepMode != 0 ? table2 : table1;
        int s = monster.Direction;
        int d = CalcMonsterDirection(monster.X, monster.Y, monster.DestX, monster.DestY);
        if ((monster.Flags & 8) != 0) d ^= 4;
        d = (d - s) & 7;
        if (d >= 5) s = (s - 1) & 7;
        else if (d != 0) s = (s + 1) & 7;
        for (int i = 7; i > -1; i -= 1)
        {
            s = (s + tbl[i]) & 7;
            var (fx, fy) = GetNextStepCoords(monster.X, monster.Y, s);
            d = WalkMonsterCheckDest(fx, fy, monster, 4);
            if (d == 0) return s;
            if (d != 1 || (s & 1) != 0 || (monster.Properties.Flags & 0x80) == 0) continue;
            int w = _map.Walls[_monsterCurBlock, (s >> 1) ^ 2];
            if ((_walls.WallFlags[w] & 0x20) != 0 && _walls.SpecialTypes[w] == 5) return -1;   // a door it would open
            if ((_walls.WallFlags[w] & 8) != 0) return -1;
        }
        return -1;
    }

    public int CheckForPossibleDistanceAttack(int monsterBlock, int direction, int distance, int curBlock)
    {
        if (GetBlockDistance(curBlock, monsterBlock) > distance) return 5;
        int dir = CalcMonsterDirection(monsterBlock & 0x1f, monsterBlock >> 5, curBlock & 0x1f, curBlock >> 5);
        if ((dir & 1) != 0 || dir != direction << 1) return 5;
        if ((monsterBlock & 0x1f) != (curBlock & 0x1f) && (monsterBlock & 0xffe0) != (curBlock & 0xffe0)) return 5;
        if (distance < 0 || direction > 3) return 5;
        int p = monsterBlock;
        for (int i = 0; i < distance; i += 1)
        {
            p = Party.CalcNewBlockPosition(p, direction);
            if (p == curBlock) return direction;
            if ((_walls.WallFlags[_map.Walls[p, direction ^ 2]] & 2) != 0) return 5;
            if ((_map.AssignedObjects[p] & 0x8000) != 0) return 5;
        }
        return 5;
    }

    public int WalkMonsterCheckDest(int x, int y, Monster monster, int unk)
    {
        int m = monster.Mode;
        monster.Mode = 15;
        int objType = CheckBlockBeforeObjectPlacement(x, y, monster.Properties.MaxWidth, 7, (monster.Properties.Flags & 0x1000) != 0 ? 32 : unk);
        monster.Mode = m;
        return objType;
    }

    internal static (int X, int Y) GetNextStepCoords(int srcX, int srcY, int direction)
        => ((srcX + StepX[direction & 7]) & 0x1fff, (srcY + StepY[direction & 7]) & 0x1fff);

    public static int GetBlockDistance(int block1, int block2)
    {
        int dy = Math.Abs((block2 >> 5) - (block1 >> 5));
        int dx = Math.Abs((block2 & 0x1f) - (block1 & 0x1f));
        if (dx > dy) (dx, dy) = (dy, dx);
        return (dx >> 1) + dy;
    }

    /// <summary>Distance attacks need flying objects and the party; the tick it costs is not faked.</summary>
    private bool ChasePartyWithDistanceAttacks(Monster monster)
    {
        if (monster.NumDistAttacks == 0) return false;
        if (monster.DistAttackTick > 0)
        {
            monster.DistAttackTick -= 1;
            return false;
        }
        int dir = CheckForPossibleDistanceAttack(monster.Block, monster.Facing, 4, Party.Block);
        if (dir == 5) return false;
        int s;
        if ((monster.Flags & 0x10) != 0) s = monster.Properties.NumDistWeapons != 0 ? _rng.RollDice(1, monster.Properties.NumDistWeapons) : 0;
        else
        {
            s = monster.CurDistWeapon++;
            if (monster.CurDistWeapon >= monster.Properties.NumDistWeapons) monster.CurDistWeapon = 0;
        }
        int weapon = monster.Properties.DistWeapons[s & 3];
        if ((weapon & 0xc000) != 0)
        {
            // Something thrown: that is the flying-object stage, and it would consume an item slot.
            if (GetBlockDistance(monster.Block, Party.Block) > 1) ThrownAttacksSkipped += 1;
        }
        else if ((weapon & 0x2000) == 0)
        {
            if (GetBlockDistance(monster.Block, Party.Block) > 1) return false;
            if (weapon == 1)
            {
                // the ground heaves: everyone drops what they are holding and takes a flat 20
                for (int i = 0; i < 4; i += 1)
                {
                    if (!Characters[i].Active) continue;
                    int item = RemoveCharacterItem(i, 15);
                    // It lands at the party's feet, and that matters to more than the picture: a
                    // monster deciding where to stand reads the block's object chain.
                    if (item != 0) { SetItemPosition(item, Party.PosX, Party.PosY, 0, true); DroppedItems += 1; }
                    InflictDamage(i, 20, 0xffff, 0, 2);
                }
            }
            else if (weapon == 3)
            {
                for (int i = 0; i < 30; i += 1) if (GetBlockDistance(monster.Block, Monsters[i].Block) < 7) SetMonsterMode(monster, 7);
            }
            else if (weapon == 4) ThrownAttacksSkipped += 1;   // the magic viper is a spell effect
            else return false;
        }
        if (monster.NumDistAttacks != 255) monster.NumDistAttacks -= 1;
        monster.DistAttackTick = (monster.Properties.FightingStats[4] * 8) >> 8;
        AttacksAttempted += 1;
        return true;
    }

    /// <summary>Distance attacks that would have thrown something, and items knocked loose.</summary>
    public int ThrownAttacksSkipped;
    public int DroppedItems;

    private void ChasePartyWithCloseAttacks(Monster monster)
    {
        if ((monster.Flags & 8) == 0)
        {
            int dir = CalcMonsterDirection(monster.X & 0xff00, monster.Y & 0xff00, Party.PosX & 0xff00, Party.PosY & 0xff00);
            var (x1, y1) = CalcSpriteRelPosition(monster.X, monster.Y, Party.PosX, Party.PosY, dir >> 1);
            if (y1 <= 160 && Math.Abs(x1) <= 80)
            {
                if (monster.Direction == dir && monster.Facing == dir >> 1)
                {
                    int dst = GetNearestPartyMemberFromPos(monster.X, monster.Y);
                    int m = monster.Id | 0x8000;
                    int hit = BattleHitSkillTest(m, dst, 0);
                    if (hit != 0)
                    {
                        int max = CalcInflictableDamage(m, dst, hit);
                        int dmg = _rng.RollDice(2, max);
                        if (monster.NgPlus != 0) dmg = JsMath.RoundToInt(dmg * (1 + 0.25 * monster.NgPlus));   // New game+
                        if (monster.PitScale != 0) dmg = Math.Max(1, JsMath.RoundToInt(dmg * monster.PitScale));   // a floor of the imp's pit
                        InflictDamage(dst, dmg, m, 0, 0);
                        ApplyMonsterAttackSkill(monster, dst, dmg);
                    }
                    AttacksAttempted += 1;
                    SetMonsterMode(monster, 8);
                }
                else SetMonsterDirection(monster, dir);
                return;
            }
        }
        if (monster.X != monster.DestX || monster.Y != monster.DestY) WalkMonster(monster);
        else
        {
            SetMonsterDirection(monster, monster.DestDirection);
            SetMonsterMode(monster, _rng.RollDice(1, 100) <= 50 ? 4 : 3);
        }
    }

    /// <summary>
    /// applyMonsterAttackSkill: the nastier things a monster can do on a hit - stealing, poison,
    /// draining. The roll is made either way, because it is part of the dice stream; what follows
    /// it needs the inventory and the spell system, so it is counted instead of done.
    /// </summary>
    private void ApplyMonsterAttackSkill(Monster monster, int target, int damage)
    {
        if (_rng.RollDice(1, 100) > monster.Properties.AttackSkillChance) return;
        AttackSkillsAttempted += 1;
    }

    /// <summary>Attack skills the port counted but did not carry out.</summary>
    public int AttackSkillsAttempted;

    /// <summary>Raised when a monster dies, so the host can run the level's "monster killed" script.</summary>
    public Action<int> OnMonsterKilled;

    /// <summary>monsterDropItems: what it was carrying falls where it stood.</summary>
    public void MonsterDropItems(Monster monster)
    {
        int a = monster.AssignedItems;
        monster.AssignedItems = 0;
        // Taken now: the caller moves the monster off the map straight after, and the drop would
        // otherwise land on block 0.
        int x = monster.X;
        int y = monster.Y;
        while (a != 0)
        {
            int b = a;
            a = Items.InPlay[a].NextAssignedObject;
            SetItemPosition(b, x, y, 0, true);
        }
    }

    private static (int X, int Y) CalcSpriteRelPosition(int x1, int y1, int x2, int y2, int direction)
    {
        int a = x2 - x1;
        int b = y1 - y2;
        if (direction != 0)
        {
            if (direction != 2) (a, b) = (b, a);
            if (direction != 3)
            {
                a = -a;
                if (direction != 1) b = -b;
            }
            else b = -b;
        }
        return (a, b);
    }

    private void AlignMonsterToParty(Monster monster)
    {
        int mdir = monster.Direction >> 1;
        int mx = monster.X;
        int my = monster.Y;
        bool useY = (mdir & 1) != 0;
        int pos = useY ? my : mx;
        bool centered = (pos & 0x7f) == 0;
        bool posFlag = true;
        if (monster.Properties.MaxWidth <= 63)
        {
            if (centered)
            {
                bool r = false;
                if ((monster.NextAssignedObject & 0x8000) != 0) r = true;
                else
                {
                    int id = _map.AssignedObjects[monster.Block];
                    id = (id & 0x8000) != 0 ? id & 0x7fff : 0xffff;
                    if (id != monster.Id) r = true;
                    else
                    {
                        for (int i = 0; i < 3; i += 1)
                        {
                            mdir = (mdir + 1) & 3;
                            id = _map.AssignedObjects[Party.CalcNewBlockPosition(monster.Block, mdir)];
                            id = (id & 0x8000) != 0 ? id & 0x7fff : 0xffff;
                            if (id != 0xffff) { r = true; break; }
                        }
                    }
                }
                if (r) posFlag = false;
            }
            else posFlag = false;
        }
        if (centered && posFlag) return;
        pos = posFlag
            ? ((pos & 0x80) != 0 ? pos - 32 : pos + 32)
            : ((pos & 0x80) != 0 ? pos + 32 : pos - 32);
        pos &= 0xffff;
        if (useY) my = pos;
        else mx = pos;
        if (WalkMonsterCheckDest(mx, my, monster, 4) != 0) return;
        var (fx, fy) = CalcSpriteRelPosition(mx, my, Party.PosX, Party.PosY, monster.Direction >> 1);
        if (fy > 160 || Math.Abs(fx) > 80) return;
        Place(monster, mx, my);
    }

    private void MoveStrayingMonster(Monster monster)
    {
        if (monster.FightCurTick != 0)
        {
            int d = (monster.Direction - monster.FightCurTick) & 6;
            int id = monster.Id;
            for (int i = 0; i < 7; i += 1)
            {
                var (x, y) = GetNextStepCoords(monster.X, monster.Y, d);
                if (WalkMonsterCheckDest(x, y, monster, 4) == 0)
                {
                    Place(monster, x, y);
                    SetMonsterDirection(monster, d);
                    if (i == 0 && ++id > 3) monster.FightCurTick = 0;
                    return;
                }
                d = (d + monster.FightCurTick) & 6;
            }
            SetMonsterMode(monster, 3);
        }
        else
        {
            monster.Direction &= 6;
            var (x, y) = GetNextStepCoords(monster.X, monster.Y, monster.Direction);
            if (WalkMonsterCheckDest(x, y, monster, 4) == 0) Place(monster, x, y);
            else
            {
                monster.FightCurTick = Random(1) != 0 ? 2 : -2;
                monster.Direction = (monster.Direction + monster.FightCurTick) & 6;
            }
        }
    }

    /// <summary>
    /// updateMonsterAdjustBlocks: is this monster in the party's view? The engine answers it by
    /// building the view from the monster's own block and asking the scene pass for the clip.
    /// </summary>
    private bool UpdateMonsterAdjustBlocks(Monster monster)
    {
        if ((monster.Properties.Flags & 8) != 0) return true;
        int x1 = (monster.X & 0xff00) | 0x80;
        int y1 = (monster.Y & 0xff00) | 0x80;
        int dir;
        if ((monster.Properties.Flags & 1) != 0) dir = monster.Direction >> 1;
        else
        {
            dir = CalcMonsterDirection(x1, y1, Party.PosX, Party.PosY);
            if ((monster.Properties.Flags & 2) != 0 && dir == (monster.Direction ^ 4)) return false;
            dir >>= 1;
        }
        var (x2, y2) = CalcSpriteRelPosition(x1, y1, Party.PosX, Party.PosY, dir);
        x2 >>= 8;
        y2 >>= 8;
        if (y2 < 0 || y2 > 3) return false;
        if (Math.Abs(x2) > y2) return false;
        int[] dims = { 0, 13, 9, 3 };
        return ClipVisibleFrom != null && ClipVisibleFrom(monster.Block, dir, x2 + dims[y2]);
    }
}
