// Everything drawn on top of the walls: the level's decorations and its doors.
//
// Transliterated from src/game/scene.mjs drawSceneShapes / drawDecorations / setLevelShapesDim /
// setDoorShapeDim / drawDoor, which are ScummVM's LoLEngine equivalents. The clip rectangle is
// computed the original way - by walking the 18 visible blocks and narrowing a left/right pair -
// because it is what decides whether a torch on a side wall is visible at all.
//
// Monsters, items and spell effects also go through here in the original; they arrive with the
// stages that give them shapes.
namespace LolCore;

public sealed class SceneShapes
{
    private readonly SceneComposer _composer;
    private WallData _walls;
    private BlockMap _map;
    private Decorations _decorations;
    private readonly Screen _screen;
    private MonsterBoard _board;

    private readonly int[] _dscTileIndex = StaticData.Table("DscTileIndex");
    private readonly int[] _dscDimMap = StaticData.Table("DscDimMap");
    private readonly int[] _dscDimData1 = StaticData.Table("DscDimData1");
    private readonly int[] _dscDimData2 = StaticData.Table("DscDimData2");
    private readonly int[] _dscOvlIndex = StaticData.Table("DscOvlIndex");
    private readonly int[] _dscOvlMap = StaticData.Table("DscOvlMap");
    private readonly int[] _dscWalls = StaticData.Table("DscWalls");
    private readonly int[] _dscShapeIndex = StaticData.Table("DscShapeIndex");
    private readonly int[] _dscScaleWidth = StaticData.Table("DscScaleWidthData");
    private readonly int[] _dscScaleHeight = StaticData.Table("DscScaleHeightData");
    private readonly int[] _dscX = StaticData.Table("DscX");
    private readonly int[] _baseDscY = StaticData.Table("BaseDscY");
    private readonly int[] _dscDoorShapeIndex = StaticData.Table("DscDoorShapeIndex");
    private readonly int[] _dscDoorScaleOffs = StaticData.Table("DscDoorScaleOffs");
    private readonly int[] _dscDoorFrameY1 = StaticData.Table("DscDoorFrameY1");
    private readonly int[] _dscDoorFrameY2 = StaticData.Table("DscDoorFrameY2");
    private readonly int[] _dscDoorY2 = StaticData.Table("DscDoorY2");
    private readonly int[] _dscDoorY = StaticData.Table("DscDoorY");
    private readonly int[] _dscDoorX = StaticData.Table("DscDoorX");
    private readonly int[] _dscDoorScale = StaticData.Table("DscDoorScale");
    private readonly int[] _dscDoor4 = StaticData.Table("DscDoor4");
    private readonly int[] _dscBlockIndex = StaticData.Table("DscBlockIndex");
    private readonly int[] _monsterDirFlags = StaticData.Table("MonsterDirFlags");
    private readonly int[] _monsterShiftOffsets = StaticData.Table("MonsterShiftOffsets");
    private readonly int[] _monsterScaleX = StaticData.Table("MonsterScaleX");
    private readonly int[] _monsterScaleY = StaticData.Table("MonsterScaleY");
    private readonly int[] _monsterScaleWH = StaticData.Table("MonsterScaleWH");
    private readonly int[] _monsterModifiers1 = StaticData.Table("MonsterModifiers1");
    private readonly int[] _sceneItemOffs = StaticData.Table("SceneItemOffs");
    private readonly int[] _gameShapeMap = StaticData.Table("GameShapeMap");

    /// <summary>ITEMSHP.SHP and GAMESHP.SHP: what items lying on the floor are drawn with.</summary>
    public Shape[] ItemShapes = Array.Empty<Shape>();
    public Shape[] GameShapes = Array.Empty<Shape>();

    private int _shpDmX, _shpDmY;
    private int _partyX, _partyY;

    /// <summary>The party's fine position and the level number: drawing a sprite needs both.</summary>
    public int Level;
    public int Difficulty = 1;

    private readonly int[] _shapeLeftRight = new int[36];
    private int _dmScaleW, _dmScaleH;
    private int _direction;

    /// <summary>
    /// sceneSerial: which drawing pass this is. A monster carries the pass it was last drawn in, so
    /// a host can tell whether what it is about to draw over is still on the screen.
    /// </summary>
    public int DrawPass;

    public const int SceneShpDim = 13;
    public const int SceneXOffset = 112;
    public int ScenePage = 2;            // sceneDrawPage1
    public Shape[] DoorShapes = new Shape[2];

    public SceneShapes(SceneComposer composer, WallData walls, BlockMap map, Decorations decorations, Screen screen, MonsterBoard board = null)
    {
        _board = board;
        _composer = composer;
        _walls = walls;
        _map = map;
        _decorations = decorations;
        _screen = screen;
    }

    /// <summary>Every level builds its own monster board; the view has to draw from the current one.</summary>
    public void SetBoard(MonsterBoard board) => _board = board;

    /// <summary>The new level's wall table, block map and decorations, after a level change.</summary>
    public void SetLevel(WallData walls, BlockMap map, Decorations decorations)
    {
        _walls = walls;
        _map = map;
        _decorations = decorations;
    }

    private int VisibleWall(int visibleBlock, int side) => _map.Walls[_composer.VisibleBlockIndex[visibleBlock], side];

    public void Draw(int direction, int partyX = 0, int partyY = 0)
    {
        _direction = direction;
        _partyX = partyX;
        _partyY = partyY;
        for (int i = 0; i < 36; i += 1) _shapeLeftRight[i] = -1;

        for (int i = 0; i < 18; i += 1)
        {
            int t = _dscTileIndex[i];
            int s = VisibleWall(t, _composer.Down);
            var (x1, x2) = SetLevelShapesDim(t, SceneShpDim);
            if (x2 <= x1) continue;
            DrawDecorations(t);
            int w = _walls.WallFlags[s];
            if (t == 16) w |= 0x80;
            // drawBlockEffects (spell clouds and the like) still belongs between these two.
            if (_board != null && _map.AssignedObjects[_composer.VisibleBlockIndex[t]] != 0 && (w & 0x80) != 0) DrawBlockObjects(t);
            if ((w & 8) == 0) continue;
            int v = 20 * (s - (s < 23 ? _dscDoorScaleOffs[s] : 0));
            if (v > 80) v = 80;
            SetDoorShapeDim(t, SceneShpDim);
            DrawDoor(DoorShapes[s < 23 ? _dscDoorShapeIndex[s] : 0], t, 10, 0, -v, 2);
            SetLevelShapesDim(t, SceneShpDim);
        }
    }

    /// <summary>
    /// updateMonsterAdjustBlocks borrows the scene's clip machinery: it builds a view from a
    /// monster's own block, asks how wide slot `index` comes out, and treats an empty clip as "not
    /// visible". Note the engine indexes dscBlockIndex by `dir + i`, not by `dir * 18 + i`; that is
    /// how the original reads it, quirk and all.
    /// </summary>
    public bool ClipVisibleFrom(int block, int direction, int index)
    {
        if (index < 0 || index > 17) return false;
        var saved = (int[])_composer.VisibleBlockIndex.Clone();
        for (int i = 0; i < 18; i += 1) _composer.VisibleBlockIndex[i] = (block + (sbyte)_dscBlockIndex[direction + i]) & 0x3ff;
        for (int i = 0; i < 36; i += 1) _shapeLeftRight[i] = -1;
        var (x1, x2) = SetLevelShapesDim(index, SceneShpDim);
        for (int i = 0; i < 36; i += 1) _shapeLeftRight[i] = -1;
        Array.Copy(saved, _composer.VisibleBlockIndex, 18);
        return x1 < x2;
    }

    private (int X1, int X2) SetLevelShapesDim(int index, int dim)
    {
        int x1, x2;
        if (_shapeLeftRight[index << 1] == -1)
        {
            x1 = 0;
            x2 = 22;
            int m = index * 18;
            for (int i = 0; i < 18; i += 1)
            {
                int d = VisibleWall(i, _composer.Down);
                int a = _walls.WallFlags[d];
                if ((a & 8) != 0)
                {
                    int t = _dscDimData2[(m + i) << 1];
                    if (t > x1)
                    {
                        x1 = t;
                        if ((a & 0x10) == 0) SetDoorShapeDim(index, -1);
                    }
                    t = _dscDimData2[((m + i) << 1) + 1];
                    if (t < x2)
                    {
                        x2 = t;
                        if ((a & 0x10) == 0) SetDoorShapeDim(index, -1);
                    }
                }
                else
                {
                    int t = (sbyte)_dscDimData1[m + i];
                    if (_walls.VmpMap[d] == 0 || t == -40) continue;
                    if (t == -41) { x1 = 22; x2 = 0; break; }
                    if (t > 0 && x2 > t) x2 = t;
                    if (t < 0 && x1 < -t) x1 = -t;
                }
                if (x2 < x1) break;
            }
            x1 += SceneXOffset >> 3;
            x2 += SceneXOffset >> 3;
            _shapeLeftRight[index << 1] = x1;
            _shapeLeftRight[(index << 1) + 1] = x2;
        }
        else
        {
            x1 = _shapeLeftRight[index << 1];
            x2 = _shapeLeftRight[(index << 1) + 1];
        }
        _screen.ModifyScreenDim(dim, x1, 0, x2 - x1, 120);
        return (x1, x2);
    }

    private (int Y1, int Y2) SetDoorShapeDim(int index, int dim)
    {
        int a = _dscDimMap[index];
        if (dim == -1 && a != 3) a += 1;
        int y1 = _dscDoorFrameY1[a];
        int y2 = _dscDoorFrameY2[a];
        if (dim != -1)
        {
            var current = _screen.Dims[dim];
            _screen.ModifyScreenDim(dim, current.Sx, y1, current.W, y2 - y1);
        }
        return (y1, y2);
    }

    private void DrawDecorations(int index)
    {
        for (int i = 1; i >= 0; i -= 1)
        {
            int s = index * 2 + i;
            int scaleW = _dscScaleWidth[s];
            int scaleH = _dscScaleHeight[s];
            int ix = (sbyte)_dscShapeIndex[s];
            int shpIx = Math.Abs(ix);
            int ovlIndex = _dscOvlIndex[4 + _dscDimMap[index] * 5] + 2;
            if (ovlIndex > 7) ovlIndex = 7;
            if (scaleW == 0 || scaleH == 0) continue;
            int d = (_direction + (sbyte)_dscWalls[s]) & 3;
            int l = _walls.ShapeMap[VisibleWall(index, d)];
            while (l > 0)
            {
                var prop = _decorations.Properties[l];
                if (prop == null) break;
                if ((prop.Flags & 8) != 0 && index != 3 && index != 9 && index != 13)
                {
                    l = prop.Next;
                    continue;
                }
                if (_dscOvlMap[shpIx] == 1 && ((prop.Flags & 2) != 0 || ((prop.Flags & 4) != 0 && _composer.WllProcessFlag != 0))) ix = -ix;
                int xOffs = 0, yOffs = 0;
                byte[] ovl = null;
                if ((prop.ScaleFlag[shpIx] & 1) != 0)
                {
                    xOffs = prop.ShapeX[shpIx];
                    yOffs = prop.ShapeY[shpIx];
                    shpIx = _dscOvlMap[shpIx];
                    ovl = _screen.LevelOverlays[ovlIndex];
                }
                else if (prop.ShapeIndex[shpIx] != 0xffff)
                {
                    scaleW = scaleH = 0x100;
                    ovl = _screen.LevelOverlays[7];
                }
                if (prop.ShapeIndex[shpIx] != 0xffff)
                {
                    var shape = _decorations.Shapes[prop.ShapeIndex[shpIx]];
                    if (shape != null)
                    {
                        int dscX = (short)_dscX[s];
                        int x;
                        int flags;
                        if (ix < 0)
                        {
                            x = dscX + xOffs + ((prop.ShapeX[shpIx] * scaleW) >> 8);
                            if (ix == (sbyte)_dscShapeIndex[s]) x = dscX - ((prop.ShapeX[shpIx] * scaleW) >> 8) - Screen.ScaledSize(shape.Width, scaleW) - xOffs;
                            flags = 0x105;
                        }
                        else
                        {
                            x = dscX + xOffs + ((prop.ShapeX[shpIx] * scaleW) >> 8);
                            flags = 0x104;
                        }
                        int y = _baseDscY[s] + yOffs + ((prop.ShapeY[shpIx] * scaleH) >> 8);
                        var opts = new ShapeDrawOptions { FadeTable = ovl, FadeLevel = 1, ScaleW = scaleW, ScaleH = scaleH };
                        _screen.DrawShape(ScenePage, shape, x + SceneXOffset, y, SceneShpDim, flags, opts);
                        if ((prop.Flags & 1) != 0 && shpIx < 4)
                        {
                            x += Screen.ScaledSize(shape.Width, scaleW);
                            flags ^= 1;
                            _screen.DrawShape(ScenePage, shape, x + SceneXOffset, y, SceneShpDim, flags, opts);
                        }
                    }
                }
                l = prop.Next;
                shpIx = Math.Abs((sbyte)_dscShapeIndex[s]);
            }
        }
    }

    // ---- objects standing in a block ----
    // drawBlockObjects, for monsters. Items also live in these chains; they arrive with the item
    // stage, and until then a block that holds one is drawn without it.
    private void DrawBlockObjects(int blockArrayIndex)
    {
        int block = _composer.VisibleBlockIndex[blockArrayIndex];
        int s = _map.AssignedObjects[block];
        if (_map.Direction[block] != _direction)
        {
            _map.DrawObjects[block] = 0;
            _map.Direction[block] = (byte)_direction;
            while (s != 0)
            {
                ReassignDrawObjects(block, s);
                s = Find(s).NextAssignedObject;
            }
        }
        s = _map.DrawObjects[block];
        while (s != 0)
        {
            if ((s & 0x8000) != 0)
            {
                int id = s & 0x7fff;
                if (blockArrayIndex < 15) DrawMonster(id);
                s = _board.Monsters[id].NextDrawObject;
                continue;
            }
            var item = _board.Items.InPlay[s];
            int fx = (sbyte)_sceneItemOffs[s & 7] << 1;
            int fy = (sbyte)_sceneItemOffs[(s >> 1) & 7] + 5;
            if (item.FlyingHeight >= 2 && blockArrayIndex >= 15) { s = item.NextDrawObject; continue; }
            if (item.FlyingHeight >= 2) fy -= (item.FlyingHeight - 1) * 6;
            var prop = _board.Items.Properties[item.ItemPropertyIndex];
            Shape shape = null;
            // An item in flight is drawn from the flight list instead; nothing flies in a still
            // scene, and launchObject is a later stage, so such an item is simply not drawn - which
            // is what the original does too when the search finds no flight for it.
            if ((prop.Flags & 0x1000) == 0 || (item.ShpCurFrameFlg & 0xc000) != 0)
                shape = (prop.Flags & 0x40) != 0
                    ? (prop.ShpIndex < GameShapes.Length ? GameShapes[prop.ShpIndex] : null)
                    : (_gameShapeMap[prop.ShpIndex << 1] < ItemShapes.Length ? ItemShapes[_gameShapeMap[prop.ShpIndex << 1]] : null);
            if (shape != null) DrawItemOrMonster(shape, null, item.X, item.Y, fx, fy, 0, false);
            s = item.NextDrawObject;
        }
    }

    private IBlockObject Find(int id) => _board.Find(id);

    /// <summary>calcObjectPosition: how far back in the view an object sits, for the draw order.</summary>
    private int ObjectPosition(IBlockObject obj)
    {
        var (_, y) = CalcSpriteRelPosition(_partyX, _partyY, obj.X, obj.Y, _direction);
        if (y < 0) y = 0;
        return (obj.FlyingHeight << 12) | (4095 - y);
    }

    private void ReassignDrawObjects(int block, int id)
    {
        if (_map.Direction[block] != _direction)
        {
            _map.Direction[block] = 5;
            return;
        }
        var newObject = Find(id);
        int r = ObjectPosition(newObject);
        IBlockObject previous = null;
        int cur = _map.DrawObjects[block];
        while (cur != 0)
        {
            var last = Find(cur);
            if (ObjectPosition(last) >= r) break;
            previous = last;
            cur = last.NextDrawObject;
        }
        newObject.NextDrawObject = cur;
        if (previous != null) previous.NextDrawObject = id;
        else _map.DrawObjects[block] = id;
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

    /// <summary>calcDrawingLayerParameters: where a sprite lands on screen and how big it is.</summary>
    private int CalcDrawingLayerParameters(int x1, int y1, Shape shape, bool vflip)
    {
        var (x, y) = CalcSpriteRelPosition(_partyX, _partyY, x1, y1, _direction);
        if (y < 0)
        {
            _dmScaleW = _dmScaleH = _shpDmX = _shpDmY = 0;
            return 0;
        }
        int l = y >> 5;
        _shpDmY = _monsterScaleY[l];
        _shpDmX = ((_monsterScaleX[l] * x) >> 8) + 200;
        _dmScaleW = _dmScaleH = _shpDmY > 120 ? 0x100 : _monsterScaleWH[_shpDmY - 56];
        if (vflip) _shpDmY = ((120 - _shpDmY) >> 1) + (Screen.ScaledSize(shape.Height, _dmScaleH) >> 1);
        else _shpDmY -= Screen.ScaledSize(shape.Height, _dmScaleH) >> 1;
        return l;
    }

    /// <summary>drawItemOrMonster: the brightness comes from the distance, the colours from the palette.</summary>
    /// <summary>
    /// drawItemOrMonster. tblValue is the engine's trick for "draw this shape as one flat colour":
    /// redrawSceneItem paints every item in its own index so a click can read the pixel under the
    /// pointer and know which item was clicked.
    /// </summary>
    private byte[] DrawItemOrMonster(Shape shape, byte[] objectPalette, int x, int y, int fineX, int fineY, int flags, bool vflip, int tblValue = -1)
    {
        byte[] backdrop;
        if ((flags & 0x80) != 0)
        {
            flags &= 0xff7f;
            backdrop = objectPalette;
            objectPalette = null;
        }
        else backdrop = _screen.LevelOverlays[4];

        int r = CalcDrawingLayerParameters(x, y, shape, vflip);
        byte[] brightness;
        if (tblValue == -1)
        {
            r = 7 - (r / 3 - 1);
            r = Math.Max(0, Math.Min(7, r));
            brightness = _screen.LevelOverlays[r];
        }
        else
        {
            var flat = new byte[16];
            for (int i = 1; i < 16; i += 1) flat[i] = (byte)tblValue;
            objectPalette = flat;
            brightness = _screen.LevelOverlays[7];
        }

        int flg = (flags & 0x10) != 0 ? 1 : 0;
        if ((flags & 0x20) != 0) flg |= 0x1000;
        if ((flags & 0x40) != 0) flg |= 2;
        if (Level == 22) { if (brightness != null) brightness[255] = 0; }
        else flg |= 0x2000;
        _shpDmX += (_dmScaleW * fineX) >> 8;
        _shpDmY += (_dmScaleH * fineY) >> 8;
        int dH = Screen.ScaledSize(shape.Height, _dmScaleH) >> 1;
        var opts = new ShapeDrawOptions
        {
            FadeTable = brightness, FadeLevel = (flg & 0x1000) != 0 ? 0 : 1,
            ScaleW = _dmScaleW, ScaleH = _dmScaleH, ColorTable = objectPalette, BackgroundFade = backdrop,
        };
        flg |= objectPalette != null ? 0x8124 : 0x124;
        _screen.DrawShape(ScenePage, shape, _shpDmX, _shpDmY, SceneShpDim, flg, opts);
        _shpDmX -= Screen.ScaledSize(shape.Width, _dmScaleW) >> 1;
        _shpDmY -= dH;
        return brightness;
    }

    /// <summary>
    /// redrawSceneItem: the two squares the party can reach, with every item drawn as a flat index.
    /// The pixel under the pointer is then the item's number, which is how a click picks one up.
    /// </summary>
    public void RedrawSceneItem(int block, int direction, int partyX, int partyY)
    {
        _composer.AssignVisibleBlocks(block, direction);
        _partyX = partyX;
        _partyY = partyY;
        _screen.FillRect(SceneXOffset, 0, SceneXOffset + SceneComposer.SceneWidth - 1, SceneComposer.SceneHeight - 1, 0, ScenePage);
        for (int i = 0; i < 36; i += 1) _shapeLeftRight[i] = -1;
        int[] tiles = { 13, 16 };
        for (int i = 0; i < 2; i += 1)
        {
            int tile = tiles[i];
            SetLevelShapesDim(tile, SceneShpDim);
            int s = _map.DrawObjects[_composer.VisibleBlockIndex[tile]];
            int t = (i << 7) + 1;
            while (s != 0)
            {
                if ((s & 0x8000) != 0) { s = _board.Monsters[s & 0x7fff].NextDrawObject; continue; }
                var item = _board.Items.InPlay[s];
                if ((item.ShpCurFrameFlg & 0x4000) != 0 && MonsterBoard.CheckDrawObjectSpace(item.X, item.Y, _partyX, _partyY) < 320)
                {
                    int fx = (sbyte)StaticData.Table("SceneItemOffs")[s & 7] << 1;
                    int fy = (sbyte)StaticData.Table("SceneItemOffs")[(s >> 1) & 7] + 5;
                    if (item.FlyingHeight > 1) fy -= (item.FlyingHeight - 1) * 6;
                    var prop = _board.Items.Properties[item.ItemPropertyIndex];
                    var shp = (prop.Flags & 0x40) != 0
                        ? (prop.ShpIndex < GameShapes.Length ? GameShapes[prop.ShpIndex] : null)
                        : ItemShapes[_gameShapeMap[prop.ShpIndex << 1]];
                    if (shp != null) DrawItemOrMonster(shp, null, item.X, item.Y, fx, fy, 0, false, t);
                }
                s = item.NextDrawObject;
                t += 1;
            }
        }
    }

    private void DrawMonster(int id)
    {
        var m = _board.Monsters[id];
        int flg = _monsterDirFlags[(_direction << 2) + m.Facing];
        int curFrm = GetMonsterCurFrame(m, flg & 0xffef);
        Shape shape;
        if (curFrm == -1)
        {
            shape = _board.Shapes[m.Properties.ShapeIndex << 4];
            if (shape != null)
                CalcDrawingLayerParameters(m.X + (sbyte)_monsterShiftOffsets[m.ShiftStep << 1], m.Y + (sbyte)_monsterShiftOffsets[(m.ShiftStep << 1) + 1], shape, false);
            DrawMonsterBlood(m, shape);
            return;     // nothing else is drawn for mode 13, but it does bleed
        }

        int d = m.Flags & 7;
        bool flip = (m.Properties.Flags & 0x200) != 0;
        flg &= 0x10;
        shape = _board.Shapes[(m.Properties.ShapeIndex << 4) + curFrm];
        if (shape == null) return;
        if ((m.Properties.Flags & 0x800) != 0) flg |= 0x20;
        byte[] palette = null;
        if (d != 0)
        {
            var pal = _board.Palettes[(m.Properties.ShapeIndex << 4) + (curFrm & 0x0f)];
            int n = shape.ColorCount;
            palette = pal.AsSpan(n * (d - 1), n).ToArray();
        }
        int sx = (m.X + (sbyte)_monsterShiftOffsets[m.ShiftStep << 1]) & 0xffff;
        int sy = (m.Y + (sbyte)_monsterShiftOffsets[(m.ShiftStep << 1) + 1]) & 0xffff;
        var brightness = DrawItemOrMonster(shape, palette, sx, sy, 0, 0, flg | 1, flip);
        // Where it landed, for whatever the host wants to put over it.
        m.DrawW = Screen.ScaledSize(shape.Width, _dmScaleW);
        m.DrawH = Screen.ScaledSize(shape.Height, _dmScaleH);
        m.DrawX = _shpDmX + (m.DrawW >> 1);
        m.DrawY = _shpDmY;
        m.DrawPass = DrawPass;

        for (int i = 0; i < 4; i += 1)
        {
            int v = m.EquipmentShapes[i] - 1;
            if (v == -1) break;
            var equipment = _board.DecorationShapes[m.Properties.ShapeIndex * 192 + v * 48 + curFrm * 3];
            if (equipment == null) continue;
            // drawDoorOrMonsterEquipment, without a palette
            int eflg = 0x104;
            if ((flg & 0x10) != 0) eflg |= 1;
            if ((flg & 0x20) != 0) eflg |= 0x1000;
            if ((flg & 0x40) != 0) eflg |= 2;
            _screen.DrawShape(ScenePage, equipment, _shpDmX, _shpDmY, SceneShpDim, eflg,
                new ShapeDrawOptions { FadeTable = brightness, FadeLevel = 1, ScaleW = _dmScaleW, ScaleH = _dmScaleH });
        }
        DrawMonsterBlood(m, shape);
    }

    /// <summary>
    /// drawMonsterBlood: how much there is depends on how badly it is hurt, and what colour on the
    /// creature - green, blue or the ordinary sort. The offsets were rolled when the blow landed, so
    /// the splash does not sit in the same place twice.
    /// </summary>
    private void DrawMonsterBlood(Monster m, Shape shape)
    {
        if (m.DamageReceived == 0 || shape == null) return;
        var blood = GameShapes.Length > 6 ? GameShapes[6] : null;
        if (blood == null) return;
        int dW = Screen.ScaledSize(shape.Width, _dmScaleW) >> 1;
        int dH = Screen.ScaledSize(shape.Height, _dmScaleH) >> 1;
        int bloodAmount = m.Mode == 13
            ? m.FightCurTick << 1
            : m.Properties.HitPoints / Math.Max(1, m.DamageReceived & 0x7fff);
        int bloodType = m.Properties.Flags & 0xc000;
        bloodType = bloodType switch { 0x4000 => 63, 0x8000 => 15, 0xc000 => 74, _ => 0 };
        var table = new byte[256];
        for (int i = 0; i < 256; i += 1) table[i] = (byte)(i >= 2 && i <= 7 ? (i + bloodType) & 0xff : i);
        dW += m.HitOffsX;
        dH += m.HitOffsY;
        bloodAmount = Math.Max(1, Math.Min(4, bloodAmount));
        int sW = _dmScaleW / bloodAmount;
        int sH = _dmScaleH / bloodAmount;
        _screen.DrawShape(ScenePage, blood, _shpDmX + dW, _shpDmY + dH, SceneShpDim, 0x124,
            new ShapeDrawOptions { FadeTable = table, FadeLevel = bloodType != 0 ? 1 : 0, ScaleW = sW, ScaleH = sH });
    }

    /// <summary>getMonsterCurFrame: which of the sixteen frames this monster shows right now.</summary>
    private int GetMonsterCurFrame(Monster m, int dirFlags)
    {
        int tmp;
        switch (_board.AnimType[m.Properties.ShapeIndex])
        {
            case 0:
                if (dirFlags != 0) return m.Mode == 13 ? -1 : dirFlags + m.CurrentSubFrame;
                if (m.DamageReceived != 0) return 12;
                switch (m.Mode - 5)
                {
                    case 0: return (m.Properties.Flags & 4) != 0 ? 13 : 0;
                    case 3: return m.FightCurTick + 13;
                    case 6: return 14;
                    case 8: return -1;
                    default: return m.CurrentSubFrame;
                }
            case 1:
                tmp = (m.Properties.HitPoints * _monsterModifiers1[Difficulty]) >> 8;
                if (m.HitPoints > tmp >> 1) tmp = 0;
                else if (m.HitPoints > tmp >> 2) tmp = 4;
                else tmp = 8;
                return m.Mode switch
                {
                    8 => m.FightCurTick + tmp,
                    11 => 12,
                    13 => m.FightCurTick + 12,
                    _ => tmp,
                };
            case 2:
                return m.FightCurTick >= 13 ? 13 : m.FightCurTick;
            case 3:
                return m.Mode switch
                {
                    5 => m.DamageReceived != 0 ? 5 : 6,
                    8 => m.FightCurTick + 6,
                    11 => 5,
                    _ => m.DamageReceived != 0 ? 5 : m.CurrentSubFrame,
                };
            default:
                return 0;
        }
    }

    private void DrawDoor(Shape shape, int index, int unk2, int w, int h, int flags)
    {
        if (shape == null) return;
        int c = _dscDoorY2[(_direction << 5) + unk2];
        int r = c / 5 + 5 * _dscDimMap[index];
        int d = _dscOvlIndex[r];
        int t = (index << 5) + c;
        int shpDmY = (short)_dscDoorY[t] + 120;
        int u = 0;
        if ((flags & 2) != 0)
        {
            int dimW = _dscDimMap[index];
            _dmScaleW = _dscDoorScale[dimW << 1];
            _dmScaleH = _dscDoorScale[(dimW << 1) + 1];
            u = _dscDoor4[dimW];
        }
        d += 2;
        if (_dmScaleW == 0 || _dmScaleH == 0) return;
        int s = Screen.ScaledSize(shape.Height, _dmScaleH) >> 1;
        if (w != 0) w = (w * _dmScaleW) >> 8;
        if (h != 0) h = (h * _dmScaleH) >> 8;
        int shpDmX = (short)_dscDoorX[t] + w + 200;
        shpDmY = shpDmY + 4 - s + h - u;
        if (d > 7) d = 7;
        var brightness = _screen.LevelOverlays[d];
        shpDmX -= Screen.ScaledSize(shape.Width, _dmScaleW) >> 1;
        shpDmY -= s;

        // drawDoorOrMonsterEquipment, for a door: no palette, no transparency
        int flg = 0x104;
        if ((flags & 0x10) != 0) flg |= 1;
        if ((flags & 0x20) != 0) flg |= 0x1000;
        if ((flags & 0x40) != 0) flg |= 2;
        var opts = new ShapeDrawOptions { FadeTable = brightness, FadeLevel = 1, ScaleW = _dmScaleW, ScaleH = _dmScaleH };
        _screen.DrawShape(ScenePage, shape, shpDmX, shpDmY, SceneShpDim, flg, opts);
    }
}
