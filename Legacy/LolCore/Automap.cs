// The magic atlas: the parchment map the player opens, drawn from the blocks they have walked.
//
// Transliterated from src/game/automap.mjs (updateAutoMap, loadMapLegendData, drawMapPage,
// drawMapBlockWall, drawMapShape, mapGetStartPos). It is a second renderer with its own rules: the
// parchment is a CPS bitmap, every explored block is stamped through a grey overlay, and the walls
// are drawn as little runs read out of a twelve-entry coordinate table.
namespace LolCore;

public sealed class Automap
{
    private static readonly int[][] MapCoords =
    {
        new[]{0, 7, 0, -5}, new[]{-5, 0, 6, 0}, new[]{7, 5, 7, 1}, new[]{5, 6, 4, 6}, new[]{0, 7, 0, -1}, new[]{-3, 0, 6, 0},
        new[]{6, 7, 6, -3}, new[]{-3, 5, 6, 5}, new[]{1, 5, 1, 1}, new[]{3, 1, 3, 1}, new[]{-1, 6, -1, -8}, new[]{-7, -1, 5, -1},
    };

    private readonly Screen _screen;
    private WallData _walls;
    private BlockMap _map;
    private readonly TextDisplayer _text;

    public readonly byte[] MapOverlay = new byte[256];
    public Shape[] Shapes = Array.Empty<Shape>();
    public byte[] LandsFile;
    public byte[] LevelLangFile;
    public int CurrentMapLevel;
    public int TopLeftX, TopLeftY;

    /// <summary>The per-level legend: block, string, shape - read from LEVEL&lt;n&gt;.XXX.</summary>
    public int[][] LegendData = Enumerable.Range(0, 32).Select(_ => new[] { 0xffff, 0, 0, 0, 0, 0xffff }).ToArray();

    public sealed class DefaultLegendEntry
    {
        public int ShapeIndex;
        public bool Enable;
        public int Y;
        public int StringId;
    }

    public readonly List<DefaultLegendEntry> DefaultLegend = new();

    /// <summary>Points the atlas at the level that is loaded now.</summary>
    public void SetLevel(WallData walls, BlockMap map)
    {
        _walls = walls;
        _map = map;
    }

    public Automap(Screen screen, WallData walls, BlockMap map, TextDisplayer text)
    {
        _screen = screen;
        _walls = walls;
        _map = map;
        _text = text;

        var legend = StaticData.Table("LegendData");
        int entrySize = legend.Length / 12;
        for (int i = 0, p = 0; i < 12 && p + entrySize <= legend.Length; i += 1)
        {
            int shapeIndex = legend[p++];
            bool enable = legend[p++] != 0;
            int y = entrySize == 5 ? (sbyte)legend[p++] : i == 10 ? -5 : 0;
            int stringId = legend[p] | (legend[p + 1] << 8);
            p += 2;
            DefaultLegend.Add(new DefaultLegendEntry { ShapeIndex = shapeIndex, Enable = enable, Y = y, StringId = stringId });
        }
    }

    /// <summary>updateAutoMap: walking into a block reveals it and the open ground around it.</summary>
    public void UpdateAutoMap(int block)
    {
        _map.Flags[block] |= 7;
        int x = block & 0x1f;
        int y = block >> 5;
        int[][] around = { new[]{-1, -1}, new[]{1, -1}, new[]{-1, 1}, new[]{1, 1}, new[]{0, -1}, new[]{0, 1}, new[]{-1, 0}, new[]{1, 0} };
        foreach (var o in around) UpdateAutoMapIntern(block, x, y, o[0], o[1]);
    }

    private bool UpdateAutoMapIntern(int block, int x, int y, int xOffs, int yOffs)
    {
        int[] blockPosTable = { 1, -1, 3, 2, -1, 0, -1, 0, 1, -32, 0, 32 };
        x += xOffs;
        y += yOffs;
        if ((x & 0xffe0) != 0 || (y & 0xffe0) != 0) return false;
        xOffs += 1;
        yOffs += 1;
        int fx = blockPosTable[xOffs];
        int b = (block + blockPosTable[6 + xOffs]) & 0x3ff;
        if (fx != -1 && (_walls.Automap[_map.Walls[b, fx]] & 0xc0) != 0) return false;
        int fy = blockPosTable[3 + yOffs];
        b = (block + blockPosTable[9 + yOffs]) & 0x3ff;
        if (fy != -1 && (_walls.Automap[_map.Walls[b, fy]] & 0xc0) != 0) return false;
        b = (block + blockPosTable[6 + xOffs] + blockPosTable[9 + yOffs]) & 0x3ff;
        if (fx != -1 && fy != -1 && (_walls.Automap[_map.Walls[b, fx]] & 0xc0) != 0 && (_walls.Automap[_map.Walls[b, fy]] & 0xc0) != 0) return false;
        _map.Flags[b] |= 7;
        return true;
    }

    /// <summary>loadMapLegendData: the named places on this level, from LEVEL&lt;n&gt;.XXX.</summary>
    public void LoadMapLegendData(byte[] data)
    {
        LegendData = Enumerable.Range(0, 32).Select(_ => new[] { 0xffff, 0, 0, 0, 0, 0xffff }).ToArray();
        if (data == null) return;
        int U16(int at) => data[at] | (data[at + 1] << 8);
        int size = Math.Min(data.Length / 12, 32);
        for (int i = 0; i < size; i += 1)
        {
            int p = i * 12;
            LegendData[i] = new[] { U16(p + 6), U16(p + 8), U16(p + 10), U16(p), U16(p + 2), U16(p + 4) };
        }
    }

    private int MapGetStartPos(bool vertical)
    {
        int At(int a, int c) => vertical ? (c << 5) + a : (a << 5) + c;
        int c = 0, a = 32;
        do
        {
            for (a = 0; a < 32; a += 1) if (_map.Flags[At(a, c)] != 0) break;
            if (a == 32) c += 1;
        } while (c < 32 && a == 32);
        int d = 31;
        a = 32;
        do
        {
            for (a = 0; a < 32; a += 1) if (_map.Flags[At(a, d)] != 0) break;
            if (a == 32) d -= 1;
        } while (d > 0 && a == 32);
        if (vertical) TopLeftY = d > c ? ((32 - (d - c)) >> 1) * 6 + 4 : 4;
        else TopLeftX = d > c ? ((32 - (d - c)) >> 1) * 7 + 5 : 5;
        return d > c ? c : 0;
    }

    public int MapGetStartPosX() => MapGetStartPos(false);

    public int MapGetStartPosY() => MapGetStartPos(true);

    private void DrawMapBlockWall(int block, int wall, int x, int y, int direction)
    {
        if (((1 << direction) & _map.Flags[block]) != 0 || (_walls.Automap[wall] & 0x1f) != 13) return;
        int cp = _screen.CurPage;
        var m = MapCoords;
        _screen.CopyBlockAndApplyOverlay(cp, x + m[0][direction], y + m[1][direction], cp, x + m[0][direction], y + m[1][direction], m[2][direction], m[3][direction], 0, MapOverlay);
        _screen.CopyBlockAndApplyOverlay(cp, x + m[4][direction], y + m[5][direction], cp, x + m[4][direction], y + m[5][direction], m[8][direction], m[9][direction], 0, MapOverlay);
        _screen.CopyBlockAndApplyOverlay(cp, x + m[6][direction], y + m[7][direction], cp, x + m[6][direction], y + m[7][direction], m[8][direction], m[9][direction], 0, MapOverlay);
    }

    private void DrawMapShape(int wall, int x, int y, int direction)
    {
        int l = _walls.Automap[wall] & 0x1f;
        if (l == 0x1f) return;
        int index = (l << 2) + direction;
        if (index < Shapes.Length) _screen.DrawShape(_screen.CurPage, Shapes[index], x + MapCoords[10][direction] - 2, y + MapCoords[11][direction] - 2, 0, 0);
        MapIncludeLegendData(l);
    }

    private void MapIncludeLegendData(int index)
    {
        foreach (var entry in DefaultLegend) if (entry.ShapeIndex == index) entry.Enable = true;
    }

    private void PrintMapText(int stringId, int x, int y, int page)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = page;
        _screen.PrintText(GameStrings.Get(stringId, LandsFile, LevelLangFile) ?? "", x, y, 239, 0);
        _screen.CurPage = cp;
    }

    /// <summary>drawMapPage: the whole parchment - title, blocks, walls, legend and exit button.</summary>
    private byte[] _minimapPalette;
    private byte[] _minimapOverlay;
    private const int MinimapParchPage = 10;
    private const int MinimapOutPage = 12;

    /// <summary>
    /// drawMinimap: the explored cells around the party, on a page of their own. Returns where it
    /// landed and the palette it is drawn in; the host blits that region wherever it likes.
    /// </summary>
    public (int Page, int X, int Y, int W, int H, byte[] Palette) DrawMinimap(int radius, byte[] parch, int partyBlock, int partyDirection)
    {
        if (_minimapPalette == null)
        {
            _minimapPalette = new byte[768];
            _screen.LoadBitmap(parch, MinimapParchPage, _minimapPalette);
            _minimapOverlay = new byte[256];
            Screen.GenerateGrayOverlay(_minimapPalette, _minimapOverlay, 52, 0, 0, 0, 256, false);
        }
        int cells = radius * 2 + 1;
        int w = cells * 7;
        int h = cells * 6;
        const int ox = 8, oy = 8;
        int cp = _screen.CurPage;
        var savedOverlay = (byte[])MapOverlay.Clone();
        Array.Copy(_minimapOverlay, MapOverlay, MapOverlay.Length);
        _screen.CurPage = MinimapOutPage;
        _screen.CopyRegion(20, 20, ox, oy, w, h, MinimapParchPage, MinimapOutPage, true);

        int px = partyBlock & 0x1f;
        int py = partyBlock >> 5;
        var a = _walls.Automap;
        for (int dy = -radius; dy <= radius; dy += 1)
            for (int dx = -radius; dx <= radius; dx += 1)
            {
                int x = px + dx, y = py + dy;
                if (x < 0 || x > 31 || y < 0 || y > 31) continue;
                int bl = (y << 5) + x;
                int w0 = _map.Walls[bl, 0], w1 = _map.Walls[bl, 1], w2 = _map.Walls[bl, 2], w3 = _map.Walls[bl, 3];
                if ((_map.Flags[bl] & 7) != 7
                    || ((a[w0] & 0xc0) != 0 && (a[w2] & 0xc0) != 0 && (a[w1] & 0xc0) != 0 && (a[w3] & 0xc0) != 0)) continue;
                int sx = ox + (dx + radius) * 7;
                int sy = oy + (dy + radius) * 6;
                int b0 = Party.CalcNewBlockPosition(bl, 0);
                int b2 = Party.CalcNewBlockPosition(bl, 2);
                int b1 = Party.CalcNewBlockPosition(bl, 1);
                int b3 = Party.CalcNewBlockPosition(bl, 3);
                int w02 = _map.Walls[b0, 2];
                int w20 = _map.Walls[b2, 0];
                int w13 = _map.Walls[b1, 3];
                int w31 = _map.Walls[b3, 1];
                _screen.CopyBlockAndApplyOverlay(MinimapOutPage, sx, sy, MinimapOutPage, sx, sy, 7, 6, 0, MapOverlay);
                DrawMapBlockWall(b3, w31, sx, sy, 3);
                DrawMapShape(w31, sx, sy, 3);
                if ((a[w31] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(MinimapOutPage, sx, sy, MinimapOutPage, sx, sy, 1, 6, 0, MapOverlay);
                DrawMapBlockWall(b1, w13, sx, sy, 1);
                DrawMapShape(w13, sx, sy, 1);
                if ((a[w13] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(MinimapOutPage, sx + 6, sy, MinimapOutPage, sx + 6, sy, 1, 6, 0, MapOverlay);
                DrawMapBlockWall(b0, w02, sx, sy, 0);
                DrawMapShape(w02, sx, sy, 0);
                if ((a[w02] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(MinimapOutPage, sx, sy, MinimapOutPage, sx, sy, 7, 1, 0, MapOverlay);
                DrawMapBlockWall(b2, w20, sx, sy, 2);
                DrawMapShape(w20, sx, sy, 2);
                if ((a[w20] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(MinimapOutPage, sx, sy + 5, MinimapOutPage, sx, sy + 5, 7, 1, 0, MapOverlay);
            }

        int cx = ox + radius * 7;
        int cy = oy + radius * 6;
        int shape = 48 + partyDirection;
        if (shape < Shapes.Length && Shapes[shape] != null) _screen.DrawShape(MinimapOutPage, Shapes[shape], cx - 3, cy - 2, 0, 0);
        _screen.CurPage = cp;
        Array.Copy(savedOverlay, MapOverlay, MapOverlay.Length);
        return (MinimapOutPage, ox, oy, w, h, _minimapPalette);
    }

    public void DrawMapPage(int pageNum, byte[] parch, byte[] palette3)
    {
        const int xOffset = 0;
        var mapStringId = StaticData.Table("MapStringId");
        for (int pass = 0; pass < 2; pass += 1)
        {
            _screen.LoadBitmap(parch, pageNum, palette3);
            int cp = _screen.CurPage;
            _screen.CurPage = pageNum;
            var of = _screen.SetFont("9");
            _screen.PrintText(GameStrings.Get(mapStringId[CurrentMapLevel], LandsFile, LevelLangFile) ?? "", 236 + xOffset, 8, 1, 0);
            int blX = MapGetStartPosX();
            int bl = (MapGetStartPosY() << 5) + blX;
            int sx = TopLeftX;
            int sy = TopLeftY;
            var a = _walls.Automap;
            for (; bl < 1024; bl += 1)
            {
                int w0 = _map.Walls[bl, 0], w1 = _map.Walls[bl, 1], w2 = _map.Walls[bl, 2], w3 = _map.Walls[bl, 3];
                if ((_map.Flags[bl] & 7) == 7 && (a[w0] & 0xc0) == 0 && (a[w2] & 0xc0) == 0 && (a[w1] & 0xc0) == 0 && (a[w3] & 0xc0) == 0)
                {
                    int b0 = Party.CalcNewBlockPosition(bl, 0);
                    int b2 = Party.CalcNewBlockPosition(bl, 2);
                    int b1 = Party.CalcNewBlockPosition(bl, 1);
                    int b3 = Party.CalcNewBlockPosition(bl, 3);
                    int w02 = _map.Walls[b0, 2];
                    int w20 = _map.Walls[b2, 0];
                    int w13 = _map.Walls[b1, 3];
                    int w31 = _map.Walls[b3, 1];
                    int cur = _screen.CurPage;
                    _screen.CopyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 7, 6, 0, MapOverlay);
                    DrawMapBlockWall(b3, w31, sx, sy, 3);
                    DrawMapShape(w31, sx, sy, 3);
                    if ((a[w31] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 1, 6, 0, MapOverlay);
                    DrawMapBlockWall(b1, w13, sx, sy, 1);
                    DrawMapShape(w13, sx, sy, 1);
                    if ((a[w13] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(cur, sx + 6, sy, cur, sx + 6, sy, 1, 6, 0, MapOverlay);
                    DrawMapBlockWall(b0, w02, sx, sy, 0);
                    DrawMapShape(w02, sx, sy, 0);
                    if ((a[w02] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 7, 1, 0, MapOverlay);
                    DrawMapBlockWall(b2, w20, sx, sy, 2);
                    DrawMapShape(w20, sx, sy, 2);
                    if ((a[w20] & 0xc0) != 0) _screen.CopyBlockAndApplyOverlay(cur, sx, sy + 5, cur, sx, sy + 5, 7, 1, 0, MapOverlay);
                }
                sx += 7;
                if (bl % 32 == 31)
                {
                    sx = TopLeftX;
                    sy += 6;
                    bl += blX;
                }
            }
            _screen.SetFont(of);
            _screen.CurPage = cp;
            of = _screen.SetFont("6");
            int tY = 0;
            int startX = MapGetStartPosX();
            int startY = MapGetStartPosY();
            for (int ii = 0; ii < 32; ii += 1)
            {
                var l = LegendData[ii];
                if (l[0] == 0xffff) break;
                int cbl = (l[0] + (l[1] << 5)) & 0x3ff;
                if ((_map.Flags[cbl] & 7) != 7) continue;
                if (l[2] == 0xffff) continue;
                PrintMapText(l[2], 244 + xOffset, (tY << 3) + 22, 2);
                if (l[5] == 0xffff) { tY += 1; continue; }
                int cbl2 = (l[3] + (l[4] << 5)) & 0x3ff;
                _map.Flags[cbl2] |= 7;
                int shape = l[5] << 2;
                if (shape < Shapes.Length)
                {
                    _screen.DrawShape(2, Shapes[shape], (l[3] - startX) * 7 + TopLeftX - 3, (l[4] - startY) * 6 + TopLeftY - 3, 0, 0);
                    _screen.DrawShape(2, Shapes[shape], 231 + xOffset, (tY << 3) + 19, 0, 0);
                }
                tY += 1;
            }
            int cp2 = _screen.CurPage;
            _screen.CurPage = pageNum;
            foreach (var l in DefaultLegend)
            {
                if (!l.Enable) continue;
                _screen.CopyBlockAndApplyOverlay(_screen.CurPage, 235, (tY << 3) + 21, _screen.CurPage, 235 + xOffset, (tY << 3) + 21, 7, 6, 0, MapOverlay);
                if ((l.ShapeIndex << 2) < Shapes.Length) _screen.DrawShape(_screen.CurPage, Shapes[l.ShapeIndex << 2], 232 + xOffset, (tY << 3) + 18 + l.Y, 0, 0);
                PrintMapText(l.StringId, 244 + xOffset, (tY << 3) + 22, 2);
                tY += 1;
            }
            _screen.SetFont(of);
            _screen.CurPage = cp2;
        }
        PrintMapExitButtonText();
    }

    private void PrintMapExitButtonText()
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        var of = _screen.SetFont("9");
        _screen.PrintString(GameStrings.Get(0x4033, LandsFile, LevelLangFile) ?? "Exit", 295, 182, 172, 0, 5);
        _screen.SetFont(of);
        _screen.CurPage = cp;
    }
}
