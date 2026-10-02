// src/formats/lol-shapes.mjs: SHP shapes, KEEP.DAT decorations, level overlays, scaled shape drawing.
using System;

namespace Lol
{
    /// <summary>lol-shapes.mjs drawShape clip: { x1, x2, y1, y2 }</summary>
    public sealed class ShapeClip
    {
        public int x1, x2, y1, y2;
    }

    public static class LolShapes
    {
        // Westwood SHP container: u16 count, u32 offsets (relative to byte 2), then shapes.
        // Shape header: u16 flags, u8 height, u16 width, u8 unused, u16 total size,
        // u16 unpacked row size, optional 16-entry color table (flags & 1), then
        // LCW-packed (unless flags & 2) run-length rows: non-zero byte = pixel,
        // zero byte + count = transparent run.
        public static Shape[] decodeShapeFile(byte[] buffer)
        {
            var reader = new BinaryReader(buffer);
            int count = reader.u16le();
            var shapes = new Shape[count];
            for (int i = 0; i < count; i += 1)
            {
                shapes[i] = decodeShape(reader.bytes, reader.u32le(2 + i * 4) + 2);
            }
            return shapes;
        }

        public static Shape decodeShape(byte[] bytes, int offset)
        {
            var reader = new BinaryReader(bytes);
            reader.offset = offset;
            int flags = reader.u16le();
            int height = reader.u8();
            int width = reader.u16le();
            reader.offset += 1;
            int size = reader.u16le();
            int frameSize = reader.u16le();
            if (width == 0 || height == 0 || size < 10) throw new Exception($"Invalid shape at {offset}");
            int colorCount = (flags & 4) != 0 ? reader.u8() : 16;
            byte[] colorTable = null;
            if ((flags & 1) != 0)
            {
                colorTable = reader.slice(reader.offset, reader.offset + colorCount);
                reader.offset += colorCount;
            }
            var rows = reader.slice(reader.offset, offset + size);
            if ((flags & 2) == 0) rows = Cps.decodeLcwBlock(rows, frameSize);

            var pixels = new byte[width * height];
            int src = 0;
            for (int y = 0; y < height; y += 1)
            {
                int x = 0;
                while (x < width)
                {
                    if (src >= rows.Length) throw new Exception($"Truncated shape rows at {offset}");
                    int value = rows[src++];
                    if (value != 0)
                    {
                        pixels[y * width + x] = (byte)value;
                        x += 1;
                    }
                    else
                    {
                        // JS: rows[src] past the end is undefined, x becomes NaN and the row ends.
                        if (src >= rows.Length) { src++; break; }
                        x += rows[src++];
                    }
                }
            }
            // pixels hold raw run bytes (0 = transparent); compact shapes map them through colorTable.
            return new Shape { width = width, height = height, pixels = pixels, colorTable = colorTable, colorCount = colorCount };
        }

        // KEEP.DAT: u16 count, then 72-byte LevelDecorationProperty records.
        public static DecorationProperty[] decodeDecorationData(byte[] buffer)
        {
            var reader = new BinaryReader(buffer);
            int count = reader.u16le();
            reader.ensure(count * 72);
            var records = new DecorationProperty[count];
            for (int i = 0; i < count; i += 1)
            {
                var shapeIndex = new ushort[10];
                var scaleFlag = new byte[10];
                var shapeX = new short[10];
                var shapeY = new short[10];
                for (int k = 0; k < 10; k += 1) shapeIndex[k] = (ushort)reader.u16le();
                for (int k = 0; k < 10; k += 1) scaleFlag[k] = (byte)reader.u8();
                for (int k = 0; k < 10; k += 1) shapeX[k] = (short)reader.i16le();
                for (int k = 0; k < 10; k += 1) shapeY[k] = (short)reader.i16le();
                int next = (sbyte)reader.u8();
                int flags = reader.u8();
                records[i] = new DecorationProperty { shapeIndex = shapeIndex, scaleFlag = scaleFlag, shapeX = shapeX, shapeY = shapeY, next = next, flags = flags };
            }
            return records;
        }

        // Screen_v2::generateOverlay for LoL 256-color mode: maps each of the 128 level
        // colors to the nearest palette entry after blending toward `specialColor`.
        public static byte[][] generateLevelOverlays(byte[] palette8, int specialColor, int weightStep)
        {
            var pal = new int[palette8.Length];
            for (int n = 0; n < palette8.Length; n += 1) pal[n] = palette8[n] >> 2;
            var overlays = new byte[8][];
            for (int level = 0; level < 7; level += 1)
            {
                int weight = 100 - level * weightStep;
                weight = weight > 0 ? Js.FloorDiv(weight * 255, 100) : 0;
                weight = Math.Min(weight, 255) >> 1;
                var overlay = new byte[256];
                for (int i = 128; i < 256; i += 1) overlay[i] = (byte)i;
                var target = new int[3];
                for (int c = 0; c < 3; c += 1) target[c] = pal[specialColor * 3 + c];
                for (int i = 1; i < 128; i += 1)
                {
                    var blended = new int[3];
                    for (int c = 0; c < 3; c += 1) blended[c] = (pal[i * 3 + c] - (((pal[i * 3 + c] - target[c]) * weight) >> 7)) & 0xff;
                    int best = 0x7fff;
                    int index = specialColor;
                    for (int candidate = 1; candidate <= 127; candidate += 1)
                    {
                        if (candidate == i) continue;
                        int sum = 0;
                        for (int c = 0; c < 3; c += 1)
                        {
                            int diff = pal[candidate * 3 + c] - blended[c];
                            sum += diff * diff;
                        }
                        if (sum == 0)
                        {
                            index = candidate;
                            break;
                        }
                        if (sum <= best)
                        {
                            best = sum;
                            index = candidate;
                        }
                    }
                    overlay[i] = (byte)index;
                }
                overlays[level] = overlay;
            }
            overlays[7] = new byte[256];
            for (int i = 0; i < 256; i += 1) overlays[7][i] = (byte)i;
            return overlays;
        }

        public static int scaledSize(int value, int scale)
        {
            return (value * scale) >> 8;
        }

        // Screen::drawShape subset used by LoL scenes: 8.8 fixed-point DDA scaling,
        // horizontal flip, rectangular clip, and plot type 1 (overlay lookup, 0 = skip).
        public static void drawShape(byte[] output, int outputWidth, Shape shape, int x, int y, ShapeClip clip, bool flip, byte[] overlay, int scaleW, int scaleH)
        {
            int scaledW = scaledSize(shape.width, scaleW);
            int scaledH = scaledSize(shape.height, scaleH);
            if (scaledW == 0 || scaledH == 0) return;
            int acc = 0;
            int targetY = y;
            for (int sourceY = 0; sourceY < shape.height; sourceY += 1)
            {
                acc += scaleH;
                for (; acc >= 0x100; acc -= 0x100, targetY += 1)
                {
                    if (targetY < clip.y1 || targetY >= clip.y2) continue;
                    int accX = 0;
                    int column = 0;
                    int row = sourceY * shape.width;
                    for (int sourceX = 0; sourceX < shape.width; sourceX += 1)
                    {
                        accX += scaleW;
                        int raw = shape.pixels[row + sourceX];
                        // JS: a colorTable index past the table is undefined, and so is the color (skipped).
                        int color = shape.colorTable != null ? (raw < shape.colorTable.Length ? overlay[shape.colorTable[raw]] : 0) : overlay[raw];
                        for (; accX >= 0x100; accX -= 0x100, column += 1)
                        {
                            int targetX = flip ? x + scaledW - 1 - column : x + column;
                            if (color != 0 && targetX >= clip.x1 && targetX < clip.x2) output[targetY * outputWidth + targetX] = (byte)color;
                        }
                    }
                }
            }
        }
    }
}
