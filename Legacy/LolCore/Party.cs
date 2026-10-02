// Where the party is and where it may go: block coordinates, wall passability, turning and stepping.
//
// Transliterated from the coordinate and movement section of src/game/scene.mjs
// (calcNewBlockPosition, calcCoordinates, testWallFlag, checkBlockPassability, moveParty).
//
// The block grid is 32x32 wrapped into 1024 entries, and a wall is tested on the *destination*
// block's opposite face - `walls[direction ^ 2]` - which is the detail every reimplementation of
// this engine gets wrong first.
namespace LolCore;

public sealed class Party
{
    private static readonly int[] Offsets = { -32, 1, 32, -1 };

    private readonly WallData _walls;
    private readonly BlockMap _map;
    private readonly MonsterBoard _monsters;

    public int Block;
    public int Direction;
    public int PosX;
    public int PosY;

    public Party(WallData walls, BlockMap map, int block, int direction, MonsterBoard monsters = null)
    {
        _walls = walls;
        _map = map;
        _monsters = monsters;
        Block = block;
        Direction = direction;
        (PosX, PosY) = CalcCoordinates(block, 0x80, 0x80);
    }

    public static int CalcNewBlockPosition(int block, int direction) => (block + Offsets[direction & 3]) & 0x3ff;

    public static int CalcBlockIndex(int x, int y) => (((y & 0xff00) >> 3) | (x >> 8)) & 0x3ff;

    public static (int X, int Y) CalcCoordinates(int block, int xOffset, int yOffset)
        => (((block & 0x1f) << 8) | xOffset, ((block & 0xffe0) << 3) | yOffset);

    /// <summary>scene.mjs testWallFlag: direction -1 asks "any face of this block".</summary>
    public bool TestWallFlag(int block, int direction, int flag)
    {
        block &= 0x3ff;
        if ((_map.Flags[block] & 0x10) != 0) return true;
        if (direction != -1) return (_walls.WallFlags[_map.Walls[block, direction ^ 2]] & flag) != 0;
        for (int i = 0; i < 4; i += 1) if ((_walls.WallFlags[_map.Walls[block, i]] & flag) != 0) return true;
        return false;
    }

    /// <summary>checkBlockPassability: a wall, or a monster standing there, refuses the step.</summary>
    public bool CheckBlockPassability(int block, int direction)
    {
        if (TestWallFlag(block, direction, 1)) return false;
        return _monsters == null || !_monsters.BlockHasMonster(block);
    }

    /// <summary>Put the party in a block, fine position and all - what a script teleport does.</summary>
    public void MoveTo(int block)
    {
        Block = block & 0x3ff;
        (PosX, PosY) = CalcCoordinates(Block, 0x80, 0x80);
    }

    public void Turn(int amount)
    {
        Direction = (Direction + amount) & 3;
    }

    /// <summary>moveParty, minus the scripts and the smooth scroll: returns false if something was in the way.</summary>
    public bool Move(int direction)
    {
        int next = CalcNewBlockPosition(Block, direction);
        if (!CheckBlockPassability(next, direction)) return false;
        MoveTo(next);
        return true;
    }

    /// <summary>The four buttons: forward, back, strafe left, strafe right.</summary>
    public bool Step(int relative) => Move((Direction + relative) & 3);
}
