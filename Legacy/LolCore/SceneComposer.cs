// The wall compositor: turns the party's position and direction into the 22x15 grid of 8x8 tiles
// that makes the 3D view, then unpacks those tiles into a 176x120 buffer of palette indices.
//
// Transliterated from generateBlockDrawingBuffer / placeTiles / drawVcnBlocks / drawVcnTile in
// src/game/scene.mjs. The order of the stamps is the depth order: far walls first, near walls last.
namespace LolCore;

public sealed class SceneComposer
{
    public const int SceneWidth = 176;
    public const int SceneHeight = 120;
    private const int Columns = 22;
    private const int Rows = 15;

    private static readonly ushort[] VmpOffsets = { 102, 97, 129, 117, 81, 159, 45, 239, 0 };

    private WallData _walls;
    private BlockMap _map;
    private VcnData _vcn;
    private ushort[] _vmp;
    private readonly sbyte[] _dscBlockIndex; // 4 directions * 18 visible blocks
    private readonly byte[] _dscBlockMap;    // down/right/left side per direction

    public readonly ushort[] BlockDrawingBuffer = new ushort[660]; // base layer + overlay layer
    public readonly int[] VisibleBlockIndex = new int[18];

    /// <summary>sceneDrawVarDown: which wall face of a visible block faces the party.</summary>
    public int Down => _down;
    public int WllProcessFlag;

    private int _down, _right, _left;

    public SceneComposer(WallData walls, BlockMap map, VcnData vcn, ushort[] vmp, sbyte[] dscBlockIndex, byte[] dscBlockMap)
    {
        _walls = walls;
        _map = map;
        _vcn = vcn;
        _vmp = vmp;
        _dscBlockIndex = dscBlockIndex;
        _dscBlockMap = dscBlockMap;
    }

    private byte Wall(int visibleBlock, int side) => _map.Walls[VisibleBlockIndex[visibleBlock], side];

    private bool HasWall(int v) => v != 0 && (_walls.WallFlags[v] & 8) == 0;

    public void AssignVisibleBlocks(int block, int direction)
    {
        for (int i = 0; i < 18; i += 1) VisibleBlockIndex[i] = (block + _dscBlockIndex[direction * 18 + i]) & 0x3ff;
    }

    /// <summary>
    /// A level script can swap the tile set under the party - falling through a floor in Yvel does
    /// exactly that - so the compositor has to be told, or it keeps drawing the old level.
    /// </summary>
    public void SetTiles(VcnData vcn, ushort[] vmp)
    {
        _vcn = vcn;
        _vmp = vmp;
    }

    /// <summary>
    /// A level change replaces the wall table and the block map, so whatever draws from them has to
    /// be told: the objects the composer was built with belong to the level that was left.
    /// </summary>
    public void SetLevel(WallData walls, BlockMap map)
    {
        _walls = walls;
        _map = map;
    }

    public void GenerateBlockDrawingBuffer(int currentBlock, int direction)
    {
        _down = _dscBlockMap[direction];
        _right = _dscBlockMap[direction + 4];
        _left = _dscBlockMap[direction + 8];
        Array.Clear(BlockDrawingBuffer, 0, BlockDrawingBuffer.Length);
        WllProcessFlag = ((currentBlock >> 5) + (currentBlock & 0x1f) + direction) & 1;
        PlaceTiles(0, 15, 1, -330, 22, 15, WllProcessFlag != 0);
        AssignVisibleBlocks(currentBlock, direction);

        void T(int x, int y, int wall, int offset, int width, int height, bool flip = false) => PlaceTiles(x, y, wall, offset, width, height, flip);

        int a = Wall(0, _right);
        if (a != 0) T(-2, 3, a, VmpOffsets[0], 3, 5);
        a = Wall(6, _left);
        if (a != 0) T(21, 3, a, VmpOffsets[0], 3, 5, true);

        a = Wall(1, _right);
        int b = Wall(2, _down);
        if (HasWall(a) && (_walls.WallFlags[b] & 8) == 0) T(2, 3, a, VmpOffsets[0], 3, 5);
        else if (a != 0 && (_walls.WallFlags[b] & 8) != 0) T(2, 3, b, VmpOffsets[0], 3, 5);

        a = Wall(5, _left);
        b = Wall(4, _down);
        if (HasWall(a) && (_walls.WallFlags[b] & 8) == 0) T(17, 3, a, VmpOffsets[0], 3, 5, true);
        else if (a != 0 && (_walls.WallFlags[b] & 8) != 0) T(17, 3, b, VmpOffsets[0], 3, 5, true);

        a = Wall(2, _right);
        if (a != 0) T(8, 3, a, VmpOffsets[1], 1, 5);
        a = Wall(4, _left);
        if (a != 0) T(13, 3, a, VmpOffsets[1], 1, 5, true);

        foreach (var (block, x) in new[] { (1, -4), (5, 20), (2, 2), (4, 14), (3, 8) })
        {
            a = Wall(block, _down);
            if ((block == 3 && a != 0) || (block != 3 && HasWall(a))) T(x, 3, a, VmpOffsets[2], 6, 5);
        }

        a = Wall(7, _right);
        if (a != 0) T(0, 3, a, VmpOffsets[3], 2, 6);
        a = Wall(11, _left);
        if (a != 0) T(20, 3, a, VmpOffsets[3], 2, 6, true);

        a = Wall(8, _right);
        if (a != 0) T(6, 2, a, VmpOffsets[4], 2, 8);
        a = Wall(10, _left);
        if (a != 0) T(14, 2, a, VmpOffsets[4], 2, 8, true);

        foreach (var (block, x) in new[] { (8, -4), (10, 16), (9, 6) })
        {
            a = Wall(block, _down);
            if ((block == 9 && a != 0) || (block != 9 && HasWall(a))) T(x, 2, a, VmpOffsets[5], 10, 8);
        }

        a = Wall(12, _right);
        if (a != 0) T(3, 1, a, VmpOffsets[6], 3, 12);
        a = Wall(14, _left);
        if (a != 0) T(16, 1, a, VmpOffsets[6], 3, 12, true);

        foreach (var (block, x) in new[] { (12, -13), (14, 19), (13, 3) })
        {
            a = Wall(block, _down);
            if ((block == 13 && a != 0) || (block != 13 && (_walls.WallFlags[a] & 8) == 0)) T(x, 1, a, VmpOffsets[7], 16, 12);
        }

        a = Wall(15, _right);
        b = Wall(17, _left);
        if (a != 0) T(0, 0, a, VmpOffsets[8], 3, 15);
        if (b != 0) T(19, 0, b, VmpOffsets[8], 3, 15, true);
    }

    private void PlaceTiles(int startX, int startY, int wall, int offset, int width, int height, bool flip)
    {
        int mapIndex = _walls.VmpMap[wall];
        if (mapIndex == 0) return;
        int sourceStart = (mapIndex - 1) * 431 + offset + 330;
        for (int y = 0; y < height; y += 1)
        {
            for (int x = 0; x < width; x += 1)
            {
                int targetX = startX + x;
                int sourceX = flip ? width - 1 - x : x;
                int at = sourceStart + y * width + sourceX;
                if (at < 0 || at >= _vmp.Length) continue;
                int tile = _vmp[at];
                if (targetX < 0 || targetX >= Columns || tile == 0) continue;
                if (flip) tile ^= 0x4000;
                BlockDrawingBuffer[(startY + y) * Columns + targetX] = (ushort)tile;
            }
        }
    }

    /// <summary>
    /// The engine's sceneWindowBuffer: one buffer, kept between frames. It matters - a tile index
    /// past the end of the VCN file draws nothing at all, so those pixels are whatever the last
    /// frame left there. Starting from a fresh buffer would clear them to black instead.
    /// </summary>
    private readonly byte[] _window = new byte[SceneWidth * SceneHeight];

    /// <summary>Unpacks the tile grid into 176x120 palette indices - the wall layer of the view.</summary>
    public byte[] DrawVcnBlocks()
    {
        var output = _window;
        for (int cell = 0; cell < 330; cell += 1)
        {
            int tile = BlockDrawingBuffer[cell];
            int overlay = 0;
            if ((tile & 0x8000) != 0)
            {
                overlay = tile & 0x7fff;
                tile = 0;
            }
            if (tile == 0) tile = BlockDrawingBuffer[cell + 330];
            DrawVcnTile(output, cell, tile, false);
            if (overlay != 0) DrawVcnTile(output, cell, overlay, true);
        }
        return output;
    }

    private void DrawVcnTile(byte[] output, int cell, int encodedTile, bool transparent)
    {
        bool flipped = (encodedTile & 0x4000) != 0;
        int tile = encodedTile & 0x3fff;
        if (tile >= _vcn.TileCount) return;
        int source = tile * 32;
        int targetX = (cell % Columns) * 8;
        int targetY = (cell / Columns) * 8;
        int shift = _vcn.Shifts[tile];
        for (int y = 0; y < 8; y += 1)
        {
            for (int x = 0; x < 8; x += 1)
            {
                int sourceX = flipped ? 7 - x : x;
                byte packed = _vcn.Tiles[source + y * 4 + (sourceX >> 1)];
                int nibble = (sourceX & 1) != 0 ? packed & 0x0f : packed >> 4;
                byte color = _vcn.ColorTable[nibble | shift];
                if (!transparent || color != 0) output[(targetY + y) * SceneWidth + targetX + x] = color;
            }
        }
    }
}
