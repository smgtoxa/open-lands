// Westwood SHP shapes and the shape rasteriser.
//
// Transliterated from src/formats/lol-shapes.mjs (decodeShapeFile/decodeShape) and
// Screen.drawShape in src/game/screen.mjs, which is ScummVM's Screen_LoL::drawShape.
//
// The rasteriser is where the original look lives: scaling is an 8.8 fixed-point DDA, and the
// colour work is a set of index-to-index lookups (brightness overlays, a two-stage transparency
// table, per-shape colour tables). None of it is colour arithmetic, so none of it can be replaced
// by blending without changing the picture.
namespace LolCore;

public sealed class Shape
{
    public string Key;          // "<file>:<index>", the name the recorder writes
    public int Width;
    public int Height;
    public byte[] Pixels;       // raw run bytes, 0 = transparent
    public byte[] ColorTable;   // compact shapes map their bytes through this
    public int ColorCount;
}

public static class Shapes
{
    public static Shape[] DecodeFile(byte[] buffer)
    {
        var reader = new BinaryReader(buffer);
        int count = reader.U16Le();
        var shapes = new Shape[count];
        for (int i = 0; i < count; i += 1)
        {
            var at = new BinaryReader(buffer, 2 + i * 4);
            int offset = (int)at.U32Le() + 2;
            shapes[i] = Decode(buffer, offset);
        }
        return shapes;
    }

    /// <summary>
    /// Shapes arrive two ways: the level's own decoration file is a bare SHP container, while
    /// everything the engine loads with loadShapeFile is wrapped in the CPS/LCW container first
    /// (see engine.mjs loadShapeFile versus scene.mjs getLevelDecorationShapes). The second word
    /// tells them apart: in a wrapper it is the compression type, 0 or 4.
    /// </summary>
    public static Shape[] DecodeContainer(byte[] bytes)
    {
        int second = bytes.Length >= 4 ? bytes[2] | (bytes[3] << 8) : -1;
        if (second == 0 || second == 4)
        {
            try { return DecodeFile(Cps.DecodeBitmapData(bytes).Data); }
            catch (Exception) { /* not a wrapper after all */ }
        }
        return DecodeFile(bytes);
    }

    public static Shape Decode(byte[] bytes, int offset)
    {
        var reader = new BinaryReader(bytes, offset);
        int flags = reader.U16Le();
        int height = reader.U8();
        int width = reader.U16Le();
        reader.Offset += 1;
        int size = reader.U16Le();
        int frameSize = reader.U16Le();
        if (width == 0 || height == 0 || size < 10) throw new InvalidDataException($"Invalid shape at {offset}");

        int colorCount = (flags & 4) != 0 ? reader.U8() : 16;
        byte[] colorTable = null;
        if ((flags & 1) != 0)
        {
            colorTable = bytes.AsSpan(reader.Offset, colorCount).ToArray();
            reader.Offset += colorCount;
        }

        byte[] rows = bytes.AsSpan(reader.Offset, offset + size - reader.Offset).ToArray();
        if ((flags & 2) == 0) rows = Cps.DecodeLcwBlock(rows, 0, rows.Length, frameSize);

        var pixels = new byte[width * height];
        int src = 0;
        for (int y = 0; y < height; y += 1)
        {
            int x = 0;
            while (x < width)
            {
                if (src >= rows.Length) throw new InvalidDataException($"Truncated shape rows at {offset}");
                byte value = rows[src++];
                if (value != 0)
                {
                    pixels[y * width + x] = value;
                    x += 1;
                }
                else x += rows[src++];
            }
        }
        return new Shape { Width = width, Height = height, Pixels = pixels, ColorTable = colorTable, ColorCount = colorCount };
    }

    public static int ScaledSize(int value, int scale) => (value * scale) >> 8;
}

/// <summary>One recorded shape draw, as the engine's recorder writes it (see Screen.drawShape).</summary>
public sealed class ShapeOp
{
    public string Key;
    public int X, Y, W, H;
    public bool XFlip, YFlip;
    public int[] Clip;          // x1, y1, x2, y2 in page coordinates
    public int Fade;            // how many times to walk the brightness table
    public int Ppc;             // which plot function
    public int Page;
    public int ScaleW = 0x100, ScaleH = 0x100;
    public int FadeTableIndex = -1;
    public byte[] FadeTable;    // when the table was not one of the level overlays
    public byte[] ColorTable;
    public bool Transparency;
    public bool BackgroundFade;
    public int BackgroundFadeIndex = -1;
    public byte[] BackgroundFadeTable;
}

/// <summary>The plot functions and the scaled blit, working on one page of palette indices.</summary>
public sealed class ShapeRasteriser
{
    private readonly byte[][] _levelOverlays;   // eight brightness levels, index -> index
    private readonly byte[] _transparency1;     // 256
    private readonly byte[] _transparency2;     // 20 pages of 256
    private readonly byte[] _backgroundFade;

    public ShapeRasteriser(byte[][] levelOverlays, byte[] transparency1, byte[] transparency2, byte[] backgroundFade = null)
    {
        _levelOverlays = levelOverlays;
        _transparency1 = transparency1;
        _transparency2 = transparency2;
        _backgroundFade = backgroundFade;
    }

    /// <summary>Draws one recorded op into `page` (pageWidth wide), exactly as Screen.drawShape would.</summary>
    public void Draw(byte[] page, int pageWidth, Shape shape, ShapeOp op)
    {
        if (shape == null) return;
        byte[] fadeTable = op.FadeTable ?? (op.FadeTableIndex >= 0 && op.FadeTableIndex < _levelOverlays.Length ? _levelOverlays[op.FadeTableIndex] : null);
        int fadeLevel = fadeTable != null ? op.Fade : 0;
        byte[] colorTable = op.ColorTable ?? shape.ColorTable;
        byte[] backgroundFade = op.BackgroundFadeTable
            ?? (op.BackgroundFadeIndex >= 0 && op.BackgroundFadeIndex < _levelOverlays.Length ? _levelOverlays[op.BackgroundFadeIndex] : _backgroundFade);

        byte Fade(byte cmd)
        {
            for (int i = 0; i < fadeLevel; i += 1) cmd = fadeTable[cmd];
            return cmd;
        }
        byte Transparent(byte cmd, byte under)
        {
            byte offs = _transparency1[cmd];
            return (offs & 0x80) != 0 ? cmd : _transparency2[(offs << 8) | under];
        }

        Action<byte[], int, byte> plot = op.Ppc switch
        {
            0 => (dst, i, cmd) => dst[i] = cmd,
            1 => (dst, i, cmd) => { cmd = Fade(cmd); if (cmd != 0) dst[i] = cmd; },
            3 or 7 => (dst, i, _) => { byte cmd = Fade(dst[i]); if (cmd != 0) dst[i] = cmd; },
            4 => (dst, i, cmd) => dst[i] = colorTable[cmd],
            5 => (dst, i, cmd) => { cmd = Fade(colorTable[cmd]); if (cmd != 0) dst[i] = cmd; },
            16 => (dst, i, cmd) => dst[i] = Transparent(cmd, dst[i]),
            20 => (dst, i, cmd) => dst[i] = Transparent(colorTable[cmd], dst[i]),
            21 => (dst, i, cmd) => { cmd = Fade(Transparent(colorTable[cmd], dst[i])); if (cmd != 0) dst[i] = cmd; },
            33 => (dst, i, cmd) => { if (cmd == 255) dst[i] = backgroundFade[dst[i]]; else { cmd = Fade(cmd); if (cmd != 0) dst[i] = cmd; } },
            37 => (dst, i, cmd) => { byte c = colorTable[cmd]; c = c == 255 ? backgroundFade[dst[i]] : Fade(c); if (c != 0) dst[i] = c; },
            48 => (dst, i, cmd) => dst[i] = Transparent(cmd, dst[i]),
            52 => (dst, i, cmd) => dst[i] = Transparent(colorTable[cmd], dst[i]),
            _ => null,
        };
        if (plot == null) throw new NotSupportedException($"Missing drawShape plotting method type {op.Ppc}");

        int clipX1 = op.Clip[0], clipY1 = op.Clip[1], clipX2 = op.Clip[2], clipY2 = op.Clip[3];
        int x = op.X, y = op.Y;
        int shapeHeight = op.H;
        int scaledW = op.W;
        int scaleH = op.ScaleH;
        int scaleW = op.ScaleW;

        // The recorder writes x/y after the engine applied its own centring and flips, so the blit
        // below starts exactly where the original one did.
        int acc = 0;
        int targetY = y;
        int width = shape.Width;
        for (int sourceY = 0; sourceY < shape.Height; sourceY += 1)
        {
            acc += scaleH;
            for (; acc >= 0x100; acc -= 0x100, targetY += 1)
            {
                int outY = op.YFlip ? y + shapeHeight - 1 - (targetY - y) : targetY;
                if (outY < clipY1 || outY >= clipY2) continue;
                int accX = 0;
                int column = 0;
                int row = sourceY * width;
                int rowStart = outY * pageWidth;
                for (int sourceX = 0; sourceX < width; sourceX += 1)
                {
                    accX += scaleW;
                    byte cmd = shape.Pixels[row + sourceX];
                    for (; accX >= 0x100; accX -= 0x100, column += 1)
                    {
                        if (cmd == 0) continue;
                        int targetX = op.XFlip ? x + scaledW - 1 - column : x + column;
                        if (targetX >= clipX1 && targetX < clipX2) plot(page, rowStart + targetX, cmd);
                    }
                }
            }
        }
    }
}
