// Monsters as objects on the board: their properties, where a level's script puts them, and the
// per-block object chains that decide what the party can walk into.
//
// Transliterated from src/game/monsters.mjs (loadMonsterProperties, initMonster, placeMonster,
// testBlockPassability, checkBlockBeforeObjectPlacement) and the list helpers in src/game/items.mjs.
//
// Only what placement needs is here: the AI, the fighting and the drawing are later stages. A
// monster still takes part in passability, which is the point - the walk harness could not match
// the engine until this existed.
namespace LolCore;

public sealed class MonsterProperty
{
    public int ShapeIndex;
    public int MaxWidth;
    public readonly int[] FightingStats = new int[9];
    public int HitPoints;
    public int Flags;
    public int NumDistAttacks;
    public int SpeedTotalWaitTicks = 1;
    public int SkillLevel;
    public int DefenseSkillChance, DefenseSkillType;
    public int NumDistWeapons;
    public readonly int[] DistWeapons = new int[3];
    public int ItemProtection;
    public int AttackSkillChance, AttackSkillType;
    public readonly int[] ItemsMight = new int[8];
    public readonly int[] ProtectionAgainstItems = new int[8];
    public readonly int[] Sounds = new int[3];
}

public sealed class Monster : IBlockObject
{
    public int Id;
    public int Block;
    public int X { get; set; }
    public int Y { get; set; }
    public int Facing, Direction;
    public int Type;
    public int Mode = 0x10;
    public int Flags;
    public int HitPoints;
    public int SpeedTick;
    public int NumDistAttacks;
    public int DistAttackTick;
    public int CurDistWeapon;
    /// <summary>New game+ level: what this monster was scaled for.</summary>
    public int NgPlus;
    /// <summary>The Imp's Pit scales what a monster hits for to the party that walked in.</summary>
    public double PitScale;
    /// <summary>The one tougher monster at the far end of a pit floor.</summary>
    public bool DungeonBoss;
    public int AssignedItems;
    public int FlyingHeight { get; set; }
    public int CurrentSubFrame;
    public int NextAssignedObject { get; set; }
    public int NextDrawObject { get; set; }
    public int ShiftStep;
    public int FightCurTick;
    public int DamageReceived;
    public int HitOffsX, HitOffsY;

    /// <summary>
    /// The box this monster was last drawn in, and which drawing pass that was. A host showing
    /// something over a monster - a damage number, a health bar - needs both: the box to place it,
    /// and the pass to know the monster was actually on the screen that time round.
    /// </summary>
    public int DrawX, DrawY, DrawW, DrawH, DrawPass;
    public int DestX, DestY, DestDirection;
    public readonly int[] EquipmentShapes = new int[4];
    public MonsterProperty Properties;
}

/// <summary>The engine's own RNG once it has been seeded, so a port rolls the same dice.</summary>
public sealed class Rng
{
    private uint _state;
    public Rng(uint seed) => _state = seed;

    public double NextDouble()
    {
        _state = _state * 1103515245 + 12345;
        return ((_state >> 16) & 0x7fff) / (double)0x8000;
    }

    /// <summary>engine.mjs rollDice.</summary>
    public int RollDice(int times, int pips, int inc = 0)
    {
        if (times <= 0 || pips <= 0) return inc;
        int total = 0;
        while (times-- > 0) total += 1 + (int)(NextDouble() * pips);
        return total + inc;
    }
}

public sealed partial class MonsterBoard
{
    private static readonly int[] Modifiers1 = { 102, 256, 384 };
    private static readonly int[] Modifiers2 = { 256, 256, 192 };

    private readonly WallData _walls;
    private readonly BlockMap _map;
    private readonly Rng _rng;

    public readonly Monster[] Monsters = new Monster[30];
    public readonly ItemBoard Items;
    public readonly MonsterProperty[] Properties = new MonsterProperty[5];   // as many as the engine keeps
    public readonly Shape[] Shapes = new Shape[48];
    public readonly byte[][] Palettes = new byte[48][];              // per frame: 8 recolour variants
    public readonly Shape[] DecorationShapes = new Shape[3 * 192];   // the monster's equipment
    public readonly int[] AnimType = new int[3];
    public int Difficulty = 1;

    /// <summary>Where the party is standing; a block it occupies refuses a monster. Once the party
    /// is walking it is the party's own block that counts, not the one the level was loaded at.</summary>
    public int PartyBlock { get => Party?.Block ?? _partyBlock; set => _partyBlock = value; }

    /// <summary>
    /// partyDamageFlags: what kind of blow last put a hero down, or -1 when the loop has dealt with
    /// it. Bit 0x40 is the one that lets the party wake up again instead of the game ending.
    /// </summary>
    public int PartyDamageFlags = -1;
    private int _partyBlock = -1;

    public MonsterBoard(WallData walls, BlockMap map, Rng rng, ItemBoard items = null, Character[] party = null)
    {
        if (party != null) Characters = party;
        _walls = walls;
        _map = map;
        _rng = rng;
        Items = items ?? new ItemBoard();
        for (int i = 0; i < Monsters.Length; i += 1) Monsters[i] = new Monster { Id = i };
        for (int i = 0; i < Properties.Length; i += 1) Properties[i] = new MonsterProperty();
    }

    public static int CalcBlockIndex(int x, int y) => (((y & 0xff00) >> 3) | (x >> 8)) & 0x3ff;

    public static int CheckDrawObjectSpace(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    /// <summary>
    /// loadMonsterShapes: 16 animation frames, the equipment shapes behind them, and the recolour
    /// palettes. Shape 16 of the file is a palette image - column 0 lists the base colours and
    /// columns 1..8 the variants - which is how one skeleton file makes eight differently dressed
    /// skeletons.
    /// </summary>
    public void LoadShapes(byte[] file, string name, int monsterIndex, int animType)
    {
        if (monsterIndex >= 0 && monsterIndex < ShapeNames.Length) ShapeNames[monsterIndex] = name;
        var shapes = LolCore.Shapes.DecodeFile(Cps.DecodeBitmapData(file).Data);
        for (int i = 0; i < shapes.Length; i += 1) if (shapes[i] != null) shapes[i].Key = $"{name}:{i}";
        int pos = monsterIndex << 4;
        for (int i = 0; i < 16; i += 1)
        {
            Shapes[pos + i] = i < shapes.Length ? shapes[i] : null;
            Palettes[pos + i] = new byte[(Shapes[pos + i]?.ColorCount ?? 16) << 3];
        }
        for (int i = 0; i < 4; i += 1)
        {
            for (int ii = 0; ii < 16; ii += 1)
            {
                int at = monsterIndex * 192 + i * 48 + ii * 3;
                int source = (i << 4) + ii + 17;
                for (int k = 0; k < 3; k += 1) DecorationShapes[at + k] = source + k < shapes.Length ? shapes[source + k] : null;
            }
        }
        AnimType[monsterIndex] = animType & 0xff;

        var palShape = shapes.Length > 16 ? shapes[16] : null;
        var grid = new byte[320 * 200];
        if (palShape != null)
        {
            for (int y = 0; y < palShape.Height; y += 1)
            {
                for (int x = 0; x < palShape.Width; x += 1)
                {
                    byte raw = palShape.Pixels[y * palShape.Width + x];
                    if (raw != 0) grid[y * 320 + x] = palShape.ColorTable != null ? palShape.ColorTable[raw] : raw;
                }
            }
        }
        var baseColors = new byte[64];
        for (int i = 0; i < 64; i += 1) baseColors[i] = grid[i * 320];
        for (int i = 0; i < 16; i += 1)
        {
            var shape = Shapes[pos + i];
            if (shape?.ColorTable == null) continue;
            int numCol = shape.ColorCount;
            var table = shape.ColorTable;
            var rows = new int[256];
            Array.Fill(rows, -1);
            for (int ii = 0; ii < numCol; ii += 1)
            {
                int cl = Array.IndexOf(baseColors, table[ii]);
                if (cl >= 0) rows[ii] = cl;
            }
            var output = Palettes[pos + i];
            for (int ii = 0; ii < 8; ii += 1)
            {
                var variant = table.ToArray();
                for (int iii = 0; iii < numCol; iii += 1)
                {
                    if (rows[iii] == -1) continue;
                    byte v = grid[rows[iii] * 320 + ii + 1];
                    if (v != 0) variant[iii] = v;
                }
                Array.Copy(variant, 0, output, ii * numCol, numCol);
            }
        }
    }

    /// <summary>loadMonsterProperties: the script hands over the whole stat block as arguments.</summary>
    public void LoadProperties(int[] args)
    {
        var p = Properties[args[0]];
        p.ShapeIndex = args[1] & 0xff;
        int widest = 0;
        for (int i = 0; i < 16; i += 1)
        {
            var shape = Shapes[(p.ShapeIndex << 4) + i];
            int width = shape != null ? shape.Width & 0xff : 0;
            if (width > widest) widest = width;
        }
        p.MaxWidth = widest;
        p.FightingStats[0] = (args[2] << 8) / 100;
        p.FightingStats[1] = 256;
        p.FightingStats[2] = (args[3] << 8) / 100;
        p.FightingStats[3] = args[4];
        p.FightingStats[4] = (args[5] << 8) / 100;
        p.FightingStats[5] = (args[6] << 8) / 100;
        p.FightingStats[6] = (args[7] << 8) / 100;
        p.FightingStats[7] = (args[8] << 8) / 100;
        p.FightingStats[8] = 0;
        for (int i = 0; i < 8; i += 1)
        {
            p.ItemsMight[i] = args[9 + i];
            p.ProtectionAgainstItems[i] = (args[17 + i] << 8) / 100;
        }
        p.ItemProtection = args[25];
        p.HitPoints = args[26];
        p.Flags = args[27];
        p.AttackSkillChance = args.Length > 35 ? args[35] : 0;
        p.AttackSkillType = args.Length > 36 ? args[36] : 0;
        p.DefenseSkillChance = args.Length > 37 ? args[37] : 0;
        p.DefenseSkillType = args.Length > 38 ? args[38] : 0;
        p.NumDistAttacks = args[30];
        p.NumDistWeapons = args.Length > 31 ? args[31] : 0;
        for (int i = 0; i < 3; i += 1) p.DistWeapons[i] = args.Length > 32 + i ? args[32 + i] : 0;
        for (int i = 0; i < 3; i += 1) p.Sounds[i] = args.Length > 39 + i ? args[39 + i] : 0;
    }

    /// <summary>initMonster: place one monster, or -1 when it does not fit.</summary>
    public int Init(int[] args, int currentLevel)
    {
        var (x, y) = Party.CalcCoordinates(args[0], args[1], args[2]);
        int width = Properties[args[4]].MaxWidth;
        if (CheckBlockBeforeObjectPlacement(x, y, width, 7, 7) != 0) return -1;
        for (int i = 0; i < 30; i += 1)
        {
            var old = Monsters[i];
            if (old.HitPoints != 0 || old.Mode == 13) continue;
            var monster = new Monster { Id = i };
            Monsters[i] = monster;
            monster.Mode = 0;
            monster.X = x;
            monster.Y = y;
            monster.Facing = args[3];
            monster.Type = args[4];
            monster.Properties = Properties[monster.Type];
            monster.Direction = monster.Facing << 1;
            monster.HitPoints = (monster.Properties.HitPoints * Modifiers1[Difficulty]) >> 8;
            if (currentLevel != 12 || monster.Type != 2) monster.HitPoints = (monster.HitPoints * (_rng.RollDice(1, 128) + 192)) >> 8;
            monster.NumDistAttacks = monster.Properties.NumDistAttacks;
            monster.DistAttackTick = _rng.RollDice(1, CalcMonsterSkillLevel(monster, 8)) - 1;
            monster.FlyingHeight = 2;
            monster.Flags = args[5];
            monster.Mode = args[6]; // setMonsterMode, without the death and script paths
            Place(monster, monster.X, monster.Y);
            monster.DestX = monster.X;
            monster.DestY = monster.Y;
            monster.DestDirection = monster.Direction;
            for (int ii = 0; ii < 4; ii += 1) monster.EquipmentShapes[ii] = args.Length > 7 + ii ? args[7 + ii] & 0xff : 0;
            return i;
        }
        return -1;
    }

    private int CalcMonsterSkillLevel(Monster monster, int a)
    {
        int r = (a << 8) / monster.Properties.FightingStats[4];
        return (r * Modifiers2[Difficulty]) >> 8;
    }

    /// <summary>placeMonster, minus the sounds and the script it triggers on entering a block.</summary>
    /// <summary>Raised when a monster walks into a new block: (block, monster id).</summary>
    public Action<int, int> OnMonsterEnteredBlock;

    /// <summary>
    /// What the drawn scene depends on, for a host deciding whether to draw it again: every monster
    /// that can be seen, and every object in the air. This is what checkSceneUpdateNeed reports in
    /// the JavaScript engine, asked the other way round.
    /// </summary>
    public int SceneSerial()
    {
        int h = 17;
        foreach (var m in Monsters)
        {
            if (m.Mode > 13 && m.Block == 0) continue;
            h = h * 31 + m.Block;
            h = h * 31 + m.X;
            h = h * 31 + m.Y;
            h = h * 31 + m.Mode;
            h = h * 31 + m.CurrentSubFrame;
            h = h * 31 + m.Direction;
            h = h * 31 + m.DamageReceived;
        }
        foreach (var f in FlyingObjects)
        {
            if (f.Enable == 0) continue;
            h = h * 31 + f.X;
            h = h * 31 + f.Y;
            h = h * 31 + f.FlyingHeight;
        }
        return h;
    }

    public void Place(Monster monster, int x, int y)
    {
        int previousBlock = monster.Block;
        if (monster.Block != 0)
        {
            RemoveAssignedObject(monster.Block, monster.Id | 0x8000);
            _map.Direction[monster.Block] = 5;
        }
        monster.Block = CalcBlockIndex(x, y);
        if (monster.X != x || monster.Y != y)
        {
            monster.X = x;
            monster.Y = y;
            monster.CurrentSubFrame = (monster.CurrentSubFrame + 1) & 3;
        }
        if (monster.Block == 0) return;
        AssignObject(monster.Block, monster.Id | 0x8000);
        _map.Direction[monster.Block] = 5;
        SceneUpdateRequired = true;
        // Entering a block is a script event in its own right (that is how a trap knows).
        if (monster.Block != previousBlock) OnMonsterEnteredBlock?.Invoke(monster.Block, monster.Id);
        // And it is a sound: its own, attenuated by the distance and the walls between.
        int ownSound = monster.Properties?.Sounds[0] ?? 0;
        if (ownSound == 0 || ownSound == 255 || previousBlock == 0) return;
        if (((monster.Properties.Flags & 0x100) == 0 || (monster.CurrentSubFrame & 1) == 0)
            && monster.Block == previousBlock) return;
        if ((UpdateFlagsNow?.Invoke() ?? 0) != 0) return;
        ProcessEnvironmentalSoundEffect(ownSound, monster.Block);
    }

    // ---- the per-block object chains (items.mjs) ----
    /// <summary>findObject: the high bit picks the list.</summary>
    public IBlockObject Find(int id) => (id & 0x8000) != 0 ? Monsters[id & 0x7fff] : Items.InPlay[id];

    /// <summary>
    /// resetItems: the floor of the level being left, written down. Each block's chain is walked
    /// past its monsters to the first item on it, which is stamped with where it lies and cut loose
    /// from the monsters that are about to be discarded. Only that first item carries the stamp -
    /// the rest of the block's pile hangs off its own NextAssignedObject and travels with it.
    /// </summary>
    public void ResetItems(int currentLevel, bool resetFlying)
    {
        for (int block = 0; block < 1024; block += 1)
        {
            _map.Direction[block] = 5;
            int id = _map.AssignedObjects[block];
            int lastMonster = 0;
            while ((id & 0x8000) != 0)
            {
                lastMonster = id;
                id = Find(id).NextAssignedObject;
            }
            if (id == 0) continue;
            Items.InPlay[id].Level = currentLevel;
            Items.InPlay[id].Block = block;
            if (lastMonster != 0) Find(lastMonster).NextAssignedObject = 0;
        }
        if (resetFlying) for (int i = 0; i < FlyingObjects.Length; i += 1) FlyingObjects[i] = new FlyingObject();
    }

    /// <summary>
    /// A monster copied for keeping, or copied back. The chain links are deliberately not carried:
    /// they belong to the block table it is going into, not to the monster.
    /// </summary>
    public static Monster CopyMonster(Monster src)
    {
        var m = new Monster
        {
            Id = src.Id, Block = src.Block, X = src.X, Y = src.Y, Facing = src.Facing,
            Direction = src.Direction, Type = src.Type, Mode = src.Mode, Flags = src.Flags,
            HitPoints = src.HitPoints, SpeedTick = src.SpeedTick, NumDistAttacks = src.NumDistAttacks,
            DistAttackTick = src.DistAttackTick, CurDistWeapon = src.CurDistWeapon, NgPlus = src.NgPlus,
            PitScale = src.PitScale, DungeonBoss = src.DungeonBoss, AssignedItems = src.AssignedItems,
            FlyingHeight = src.FlyingHeight, CurrentSubFrame = src.CurrentSubFrame,
            ShiftStep = src.ShiftStep, FightCurTick = src.FightCurTick, DamageReceived = src.DamageReceived,
            HitOffsX = src.HitOffsX, HitOffsY = src.HitOffsY,
            DestX = src.DestX, DestY = src.DestY, DestDirection = src.DestDirection,
            Properties = src.Properties,
        };
        Array.Copy(src.EquipmentShapes, m.EquipmentShapes, m.EquipmentShapes.Length);
        return m;
    }

    /// <summary>
    /// placeMonster(m, 0, 0): the monster comes off whatever block it stands on, which is how the
    /// living set is cleared away before another is put down.
    /// </summary>
    public void TakeMonsterOffBlock(Monster monster)
    {
        if (monster.Block != 0)
        {
            RemoveAssignedObject(monster.Block, monster.Id | 0x8000);
            _map.Direction[monster.Block] = 5;
        }
        monster.Block = 0;
        monster.X = 0;
        monster.Y = 0;
    }

    public void AssignMonsterToBlock(int block, int id) => AssignObject(block, id);

    private void AssignObject(int block, int id)
    {
        Find(id).NextAssignedObject = _map.AssignedObjects[block];
        _map.AssignedObjects[block] = id;
    }

    /// <summary>assignItemToBlock: an item goes in after the monsters, never twice.</summary>
    public void AssignItemToBlock(int block, int id)
    {
        int cur = _map.AssignedObjects[block];
        for (int guard = 0; cur != 0 && guard < 64; guard += 1)
        {
            if (cur == id) { RemoveAssignedObject(block, id); break; }
            cur = Find(cur).NextAssignedObject;
        }
        var item = Items.InPlay[id];
        if ((_map.AssignedObjects[block] & 0x8000) != 0)
        {
            var previous = Find(_map.AssignedObjects[block]);
            while ((previous.NextAssignedObject & 0x8000) != 0) previous = Find(previous.NextAssignedObject);
            item.NextAssignedObject = previous.NextAssignedObject;
            item.Level = -1;
            previous.NextAssignedObject = id;
            return;
        }
        item.NextAssignedObject = _map.AssignedObjects[block];
        item.Level = -1;
        _map.AssignedObjects[block] = id;
    }

    /// <summary>assignBlockItem: the same, but keeping the item's own tail (addLevelItems uses it).</summary>
    public void AssignBlockItem(int block, int id)
    {
        IBlockObject previous = null;
        int index = _map.AssignedObjects[block];
        while ((index & 0x8000) != 0)
        {
            previous = Find(index);
            index = previous.NextAssignedObject;
        }
        var item = Items.InPlay[id];
        item.Level = -1;
        if (index == id) return;
        if (previous != null) previous.NextAssignedObject = id;
        else _map.AssignedObjects[block] = id;
        IBlockObject last = item;
        while (last.NextAssignedObject != 0) last = Find(last.NextAssignedObject);
        last.NextAssignedObject = index;
    }

    /// <summary>setItemPosition: put an item on the floor of the block its coordinates fall in.</summary>
    public void SetItemPosition(int id, int x, int y, int flyingHeight, bool moveable)
    {
        if (flyingHeight == 0)
        {
            x = (x & 0xffc0) | 0x40;
            y = (y & 0xffc0) | 0x40;
        }
        int block = CalcBlockIndex(x, y);
        var item = Items.InPlay[id];
        item.X = x;
        item.Y = y;
        item.Block = block;
        item.FlyingHeight = flyingHeight;
        if (moveable) item.ShpCurFrameFlg |= 0x4000;
        else item.ShpCurFrameFlg &= 0xbfff;
        AssignItemToBlock(block, id);
        _map.Direction[block] = 5;   // the draw order is rebuilt on the next frame
    }

    /// <summary>placeMoveLevelItem: drop an item on this level, or file it away on another one.</summary>
    public void PlaceMoveLevelItem(int id, int level, int block, int xOffs, int yOffs, int flyingHeight, int currentLevel)
    {
        var item = Items.InPlay[id];
        (item.X, item.Y) = Party.CalcCoordinates(block, xOffs, yOffs);
        if (item.Block != 0)
        {
            RemoveAssignedObject(item.Block, id);
            RemoveDrawObject(item.Block, id);
            item.Block = 0;
            item.Level = 0;
        }
        if (currentLevel == level) SetItemPosition(id, item.X, item.Y, flyingHeight, true);
        else
        {
            item.Level = level;
            item.Block = block;
            item.FlyingHeight = flyingHeight;
            item.ShpCurFrameFlg |= 0x4000;
        }
    }

    /// <summary>addLevelItems: everything filed away on this level comes back onto the board.</summary>
    public void AddLevelItems(int currentLevel)
    {
        for (int i = 0; i < 400; i += 1)
        {
            if (Items.InPlay[i].Level != currentLevel) continue;
            AssignBlockItem(Items.InPlay[i].Block, i);
            _map.Direction[Items.InPlay[i].Block] = 5;
            Items.InPlay[i].NextDrawObject = 0;
        }
    }

    private void RemoveDrawObject(int block, int id)
    {
        if (_map.DrawObjects[block] == id)
        {
            _map.DrawObjects[block] = Find(id).NextDrawObject;
            Find(id).NextDrawObject = 0;
            return;
        }
        int cur = _map.DrawObjects[block];
        while (cur != 0)
        {
            var obj = Find(cur);
            if (obj.NextDrawObject == id)
            {
                obj.NextDrawObject = Find(id).NextDrawObject;
                Find(id).NextDrawObject = 0;
                return;
            }
            cur = obj.NextDrawObject;
        }
    }

    public void RemoveAssignedObject(int block, int id)
    {
        if (_map.AssignedObjects[block] == id)
        {
            _map.AssignedObjects[block] = Find(id).NextAssignedObject;
            Find(id).NextAssignedObject = 0;
            return;
        }
        int cur = _map.AssignedObjects[block];
        while (cur != 0)
        {
            var obj = Find(cur);
            if (obj.NextAssignedObject == id)
            {
                obj.NextAssignedObject = Find(id).NextAssignedObject;
                Find(id).NextAssignedObject = 0;
                return;
            }
            cur = obj.NextAssignedObject;
        }
    }

    /// <summary>Does a block hold a live monster? That is what refuses the party a step.</summary>
    public bool BlockHasMonster(int block)
    {
        int obj = _map.AssignedObjects[block & 0x3ff];
        while (obj != 0)
        {
            if ((obj & 0x8000) != 0) return true;
            obj = Find(obj).NextAssignedObject;
        }
        return false;
    }

    // ---- placement tests ----
    public int TestBlockPassability(int block, int x, int y, int objectWidth, int testFlag, int wallFlag)
    {
        if (block == PartyBlock) testFlag &= 0xfffe;
        if ((testFlag & 1) != 0)
        {
            _monsterCurBlock = block;   // walkMonsterCalcNextStep reads which block stopped the step
            if (TestWallFlag(block, -1, wallFlag)) return 1;
            _monsterCurBlock = 0;
        }
        if ((testFlag & 2) == 0) return 0;
        int obj = _map.AssignedObjects[block];
        while ((obj & 0x8000) != 0)
        {
            var monster = Monsters[obj & 0x7fff];
            if (monster.Mode < 13)
            {
                int room = CheckDrawObjectSpace(x, y, monster.X, monster.Y);
                if (objectWidth + monster.Properties.MaxWidth > room) return 2;
            }
            obj = Find(obj).NextAssignedObject;
        }
        return 0;
    }

    public bool TestWallFlag(int block, int direction, int flag)
    {
        block &= 0x3ff;
        if ((_map.Flags[block] & 0x10) != 0) return true;
        if (direction != -1) return (_walls.WallFlags[_map.Walls[block, direction ^ 2]] & flag) != 0;
        for (int i = 0; i < 4; i += 1) if ((_walls.WallFlags[_map.Walls[block, i]] & flag) != 0) return true;
        return false;
    }

    private int CheckBlockOccupiedByParty(int x, int y, int testFlag)
        => (testFlag & 4) != 0 && PartyBlock == CalcBlockIndex(x, y) ? 1 : 0;

    /// <summary>removeLevelItem: an item leaves the floor - out of both chains, and off the level.</summary>
    public void RemoveLevelItem(int item, int block)
    {
        RemoveAssignedObject(block, item);
        RemoveDrawObject(block, item);
        OnItemRemoved?.Invoke(block, item);
        Items.InPlay[item].Block = 0;
        Items.InPlay[item].Level = 0;
    }

    /// <summary>Raised when an item is taken off the floor: the block runs its script.</summary>
    public Action<int, int> OnItemRemoved;

    /// <summary>checkSceneForItems: the nth item in a block's draw chain, as the pixel colour counts them.</summary>
    public int CheckSceneForItems(int block, int color)
    {
        int cur = _map.DrawObjects[block & 0x3ff];
        while (cur != 0)
        {
            if ((cur & 0x8000) == 0 && --color == 0) return cur;
            cur = (cur & 0x8000) != 0 ? Monsters[cur & 0x7fff].NextDrawObject : Items.InPlay[cur].NextDrawObject;
        }
        return -1;
    }

    public int CheckBlockBeforeObjectPlacement(int x, int y, int objectWidth, int testFlag, int wallFlag)
    {
        int x2 = 0, y2 = 0, xOffs = 0, yOffs = 0, flag = 0;
        _objectLastDirection = 0;
        int r = TestBlockPassability(CalcBlockIndex(x, y), x, y, objectWidth, testFlag, wallFlag);
        if (r != 0) return r;
        if (CheckBlockOccupiedByParty(x, y, testFlag) != 0) return 4;
        if ((x & 0x80) != 0)
        {
            if ((((x & 0xff) + objectWidth) & 0xff00) != 0)
            {
                xOffs = 1;
                _objectLastDirection = 2;
                x2 = x + objectWidth;
                r = TestBlockPassability(CalcBlockIndex(x2, y), x, y, objectWidth, testFlag, wallFlag);
                if (r != 0) return r;
                if (CheckBlockOccupiedByParty(x + xOffs, y, testFlag) != 0) return 4;
                flag = 1;
            }
        }
        else if ((((x & 0xff) - objectWidth) & 0xff00) != 0)
        {
            xOffs = -1;
            _objectLastDirection = 6;
            x2 = x - objectWidth;
            r = TestBlockPassability(CalcBlockIndex(x2, y), x, y, objectWidth, testFlag, wallFlag);
            if (r != 0) return r;
            if (CheckBlockOccupiedByParty(x + xOffs, y, testFlag) != 0) return 4;
            flag = 1;
        }
        if ((y & 0x80) != 0)
        {
            if ((((y & 0xff) + objectWidth) & 0xff00) != 0)
            {
                yOffs = 1;
                _objectLastDirection = 4;
                y2 = y + objectWidth;
                r = TestBlockPassability(CalcBlockIndex(x, y2), x, y, objectWidth, testFlag, wallFlag);
                if (r != 0) return r;
                if (CheckBlockOccupiedByParty(x, y + yOffs, testFlag) != 0) return 4;
                flag &= 1;
            }
            else flag = 0;
        }
        else if ((((y & 0xff) - objectWidth) & 0xff00) != 0)
        {
            yOffs = -1;
            _objectLastDirection = 0;
            y2 = y - objectWidth;
            r = TestBlockPassability(CalcBlockIndex(x, y2), x, y, objectWidth, testFlag, wallFlag);
            if (r != 0) return r;
            if (CheckBlockOccupiedByParty(x, y + yOffs, testFlag) != 0) return 4;
            flag &= 1;
        }
        else flag = 0;
        if (flag == 0) return 0;
        r = TestBlockPassability(CalcBlockIndex(x2, y2), x, y, objectWidth, testFlag, wallFlag);
        if (r != 0) return r;
        if (CheckBlockOccupiedByParty(x + xOffs, y + yOffs, testFlag) != 0) return 4;
        return 0;
    }
}
