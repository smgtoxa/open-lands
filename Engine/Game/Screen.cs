// src/game/screen.mjs
// Port of ScummVM Kyra Screen / Screen_LoL essentials: 320x200 indexed pages,
// 6-bit palettes, DOS fonts, screen dims, and the full drawShape plotter.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Lol.LandsOfLore;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        public const int SCREEN_W = 320;
        public const int SCREEN_H = 200;

        public static class DRAWSHP
        {
            public const int XFLIP = 0x01, YFLIP = 0x02, SCALE = 0x04, WINREL = 0x10, CENTER = 0x20, FADE = 0x100, PREDATOR = 0x200,
                COMPACT = 0x400, PRIORITY = 0x800, TRANSPARENT = 0x1000, BCKGRNDFADE = 0x2000, MORPH = 0x4000, COLOR = 0x8000;
        }
    }

    /// <summary>screen.mjs dims entries: { sx, sy, w, h, col1, col2 }</summary>
    public sealed class ScreenDim
    {
        public int sx, sy, w, h, col1, col2;
    }

    /// <summary>screen.mjs Screen constructor(sys): { delay(ms) -> Promise, present(), getMillis() }</summary>
    public sealed class ScreenSys
    {
        public Func<double, Task> delay;
        public Action present;
        public Func<double> getMillis;
    }

    /// <summary>screen.mjs drawShape opts: colorTable, fadeTable, fadeLevel, transparency1, transparency2, backgroundFade, scaleW, scaleH, morph.</summary>
    public sealed class DrawShapeOpts
    {
        public byte[] colorTable;
        public byte[] fadeTable;
        public int fadeLevel;
        public byte[] transparency1;
        public byte[] transparency2;
        public byte[] backgroundFade;
        public int scaleW, scaleH; // JS undefined -> 0 (scaledSize gives 0 either way)
        public object morph;
    }

    /// <summary>screen.mjs drawShape: this.recorder entries (HD recording).</summary>
    public sealed class DrawShapeRecord
    {
        public string key;
        public int x, y, w, h;
        public bool xFlip, yFlip;
        public int[] clip;
        public int fade, ppc, page, srcW, srcH, scaleW, scaleH;
        public int fadeTableIndex;
        public int[] fadeTable;
        public int[] colorTable;
        public bool transparency;
        public bool backgroundFade;
        public int backgroundFadeIndex;
        public int[] backgroundFadeTable;
        /// <summary>Unity port: the screen pixels this draw painted and the colour each ended up (the host draws a
        /// sprite its HD pack lacks from these: the creatures of the textured world)</summary>
        public List<int> painted;
        public byte[] colors;
    }

    public sealed class DosFont
    {
        public byte[] data;
        public int width, height, numGlyphs;
        public ushort[] bitmapOffsets;
        public Bytes widthTable;
        public Bytes heightTable;

        static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);

        public DosFont(byte[] bytes)
        {
            if (U16(bytes, 2) != 0x0500) throw new Exception("Invalid DOS font");
            int desc = U16(bytes, 4);
            this.data = bytes;
            this.width = bytes[desc + 5];
            this.height = bytes[desc + 4];
            this.numGlyphs = bytes[desc + 3] + 1;
            int offsets = U16(bytes, 6);
            this.bitmapOffsets = new ushort[this.numGlyphs];
            for (int i = 0; i < this.numGlyphs; i += 1) this.bitmapOffsets[i] = (ushort)U16(bytes, offsets + i * 2);
            this.widthTable = new Bytes(bytes).Sub(U16(bytes, 8));
            this.heightTable = new Bytes(bytes).Sub(U16(bytes, 12));
        }

        public int charWidth(int c)
        {
            return c < this.numGlyphs ? this.widthTable[c] : 0;
        }

        public void drawChar(int c, byte[] page, int x, int y, byte[] colorMap)
        {
            if (c >= this.numGlyphs || this.bitmapOffsets[c] == 0) return;
            int width = this.widthTable[c];
            if (width == 0) return;
            int src = this.bitmapOffsets[c];
            int h1 = this.heightTable[c * 2];
            int h2 = this.heightTable[c * 2 + 1];
            int h0 = this.height - (h1 + h2);
            int row = y * SCREEN_W + x;
            void fill(int rows)
            {
                byte color = colorMap[0];
                for (int r = 0; r < rows; r += 1, row += SCREEN_W)
                {
                    if (color != 0) Js.Fill(page, color, row, row + width);
                }
            }
            fill(h1);
            for (int r = 0; r < h2; r += 1, row += SCREEN_W)
            {
                int b = 0;
                for (int i = 0; i < width; i += 1)
                {
                    byte color;
                    if ((i & 1) != 0) color = colorMap[b >> 4];
                    else
                    {
                        b = this.data[src++];
                        color = colorMap[b & 0x0f];
                    }
                    if (color != 0) page[row + i] = color;
                }
            }
            fill(h0);
        }
    }

    public sealed class Screen
    {
        // Screen_LoL::_screenDimTable256C: sx, sy, w, h (sx/w in 8-pixel columns), col1, col2.
        static readonly int[][] DIM_TABLE =
        {
            new[] { 0x00, 0x00, 0x28, 0xc8, 0xc7, 0xcf }, new[] { 0x08, 0x48, 0x18, 0x38, 0xfe, 0x01 }, new[] { 0x0e, 0x00, 0x16, 0x78, 0xfe, 0x01 },
            new[] { 0x0b, 0x7b, 0x1c, 0x12, 0xfe, 0xfc }, new[] { 0x0b, 0x7b, 0x1c, 0x2d, 0xfe, 0xfc }, new[] { 0x55, 0x7b, 0xe9, 0x37, 0xfe, 0xfc },
            new[] { 0x0b, 0x8c, 0x10, 0x2b, 0x3d, 0x01 }, new[] { 0x04, 0x59, 0x20, 0x3c, 0x00, 0x00 }, new[] { 0x05, 0x6e, 0x1e, 0x0c, 0xfe, 0x01 },
            new[] { 0x07, 0x19, 0x1a, 0x97, 0x00, 0x00 }, new[] { 0x03, 0x1e, 0x22, 0x8c, 0x00, 0x00 }, new[] { 0x02, 0x48, 0x24, 0x34, 0x00, 0x00 },
            new[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, new[] { 0x0e, 0x00, 0x16, 0x78, 0xfe, 0x01 }, new[] { 0x0d, 0xa2, 0x18, 0x0c, 0xfe, 0x01 },
            new[] { 0x0f, 0x06, 0x14, 0x6e, 0x01, 0x00 }, new[] { 0x1a, 0xbe, 0x0a, 0x07, 0xfe, 0x01 }, new[] { 0x07, 0x21, 0x1a, 0x85, 0x00, 0x00 },
            new[] { 0x03, 0x32, 0x22, 0x62, 0x00, 0x00 }, new[] { 0x0b, 0x8c, 0x10, 0x33, 0x3d, 0x01 }, new[] { 0x0b, 0x8c, 0x10, 0x23, 0x3d, 0x01 },
            new[] { 0x01, 0x20, 0x26, 0x80, 0xdc, 0xfd }, new[] { 0x09, 0x29, 0x08, 0x2c, 0x00, 0x00 }, new[] { 0x19, 0x29, 0x08, 0x2c, 0x00, 0x00 },
            new[] { 0x01, 0x02, 0x26, 0x14, 0x00, 0x0f },
        };

        static readonly Random _entropy = new Random();

        public ScreenSys sys;
        public Dictionary<int, byte[]> pages; // JS sparse array
        public byte[][] palettes;
        public byte[] screenPalette;
        public int curPage;
        public Dictionary<string, DosFont> fonts;
        public string currentFont;
        public byte[] textColors;
        public int charSpacing;
        public int lineSpacing;
        public int textMarginRight;
        public ScreenDim[] dims;
        public ScreenDim curDim;
        public int curDimIndex;
        public bool dirty;
        public bool paletteDirty;
        public byte[] paletteOverlay1;
        public byte[] paletteOverlay2;
        public byte[] grayOverlay;
        public byte[][] levelOverlays;
        public int fadeFlag;
        /// <summary>set by the engine (scene.mjs) while recording an HD frame; null otherwise.</summary>
        public List<DrawShapeRecord> recorder;
        /// <summary>host hook read by wsa.mjs (not set in screen.mjs): (wsa, frameNum, pageNum, x, y).</summary>
        public Action<object, int, int, int, int> onWsaFrame;
        /// <summary>JS Math.random (crossFadeRegion); the host may replace it to make a run reproducible.</summary>
        public Func<double> mathRandom = () => _entropy.NextDouble();

        // sys: { delay(ms) -> Promise, present() } supplied by the engine.
        public Screen(Func<double, Task> delay, Action present, Func<double> getMillis)
        {
            this.sys = new ScreenSys { delay = delay, present = present, getMillis = getMillis };
            this.pages = new Dictionary<int, byte[]>();
            this.palettes = new[] { new byte[768], new byte[768], new byte[768], new byte[768] };
            this.screenPalette = new byte[768];
            this.curPage = 0;
            this.fonts = new Dictionary<string, DosFont>();
            this.currentFont = null;
            this.textColors = new byte[16];
            this.charSpacing = 0;
            this.lineSpacing = 0;
            this.textMarginRight = SCREEN_W;
            this.dims = Array.ConvertAll(DIM_TABLE, d => new ScreenDim { sx = d[0], sy = d[1], w = d[2], h = d[3], col1 = d[4], col2 = d[5] });
            this.curDim = this.dims[0];
            this.curDimIndex = 0;
            this.dirty = true;
            this.paletteDirty = true;
            // Screen_LoL: brightness/level overlays
            this.paletteOverlay1 = new byte[256];
            this.paletteOverlay2 = new byte[256];
            this.grayOverlay = new byte[256];
            this.levelOverlays = new byte[8][];
            for (int i = 0; i < 8; i += 1) this.levelOverlays[i] = new byte[256];
            this.fadeFlag = 2;
        }

        // VGA mode: odd and even page numbers share one buffer (Screen::_pageMapping), so page 1 == page 0, 3 == 2, ...
        public byte[] page(int num)
        {
            num &= ~1;
            if (!this.pages.TryGetValue(num, out var p) || p == null) this.pages[num] = p = new byte[SCREEN_W * SCREEN_H];
            return p;
        }

        public byte[] getPalette(int num)
        {
            return this.palettes[num];
        }

        public void setScreenPalette(byte[] pal)
        {
            Js.Set(this.screenPalette, pal);
            this.paletteDirty = true;
        }

        public void copyPalette(int dst, int src)
        {
            Js.Set(this.palettes[dst], this.palettes[src]);
        }

        public void setScreenDim(int index)
        {
            this.curDim = this.dims[index];
            this.curDimIndex = index;
        }

        public ScreenDim getScreenDim(int index)
        {
            return this.dims[index];
        }

        public void modifyScreenDim(int index, int sx, int sy, int w, int h)
        {
            var dim = this.dims[index];
            dim.sx = sx;
            dim.sy = sy;
            dim.w = w;
            dim.h = h;
            this.setScreenDim(index);
        }

        public void clearPage(int pageNum)
        {
            Js.Fill(this.page(pageNum), (byte)0);
            if (pageNum == 0) this.dirty = true;
        }

        public void clearCurPage()
        {
            this.clearPage(this.curPage);
        }

        public void copyPage(int src, int dst)
        {
            Js.Set(this.page(dst), this.page(src));
            if (dst == 0) this.dirty = true;
        }

        // Screen::copyRegion; transparent unless noCheck (CR_NO_P_CHECK).
        public void copyRegion(int x1, int y1, int x2, int y2, int w, int h, int srcPage, int dstPage, bool noCheck = false)
        {
            if (x2 < 0)
            {
                if (x2 <= -w) return;
                w += x2;
                x1 -= x2;
                x2 = 0;
            }
            else if (x2 + w >= SCREEN_W)
            {
                if (x2 > SCREEN_W) return;
                w = SCREEN_W - x2;
            }
            if (y2 < 0)
            {
                if (y2 <= -h) return;
                h += y2;
                y1 -= y2;
                y2 = 0;
            }
            else if (y2 + h >= SCREEN_H)
            {
                if (y2 > SCREEN_H) return;
                h = SCREEN_H - y2;
            }
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            if (src == dst && x1 == x2 && y1 == y2) return;
            for (int row = 0; row < h; row += 1)
            {
                int s = (y1 + row) * SCREEN_W + x1;
                int d = (y2 + row) * SCREEN_W + x2;
                if (noCheck) Js.Set(dst, Js.Slice(src, s, s + w), d);
                else for (int i = 0; i < w; i += 1) if (src[s + i] != 0) dst[d + i] = src[s + i];
            }
            if (dstPage == 0) this.dirty = true;
        }

        public byte[] copyRegionToBuffer(int pageNum, int x, int y, int w, int h)
        {
            var @out = new byte[w * h];
            var src = this.page(pageNum);
            for (int row = 0; row < h; row += 1) Js.Set(@out, Js.Slice(src, (y + row) * SCREEN_W + x, (y + row) * SCREEN_W + x + w), row * w);
            return @out;
        }

        public void copyBlockToPage(int pageNum, int x, int y, int w, int h, byte[] buffer)
        {
            var dst = this.page(pageNum);
            for (int row = 0; row < h; row += 1)
            {
                int ty = y + row;
                if (ty < 0 || ty >= SCREEN_H) continue;
                for (int i = 0; i < w; i += 1)
                {
                    int tx = x + i;
                    if (tx >= 0 && tx < SCREEN_W) dst[ty * SCREEN_W + tx] = buffer[row * w + i];
                }
            }
            if (pageNum == 0) this.dirty = true;
        }

        public void fillRect(int x1, int y1, int x2, int y2, int color, int pageNum = -1, bool xored = false)
        {
            var dst = this.page(pageNum == -1 ? this.curPage : pageNum);
            for (int y = y1; y <= y2; y += 1)
            {
                int row = y * SCREEN_W;
                if (xored) for (int x = x1; x <= x2; x += 1) dst[row + x] ^= (byte)color;
                else Js.Fill(dst, (byte)color, row + x1, row + x2 + 1);
            }
            if (pageNum == 0 || (pageNum == -1 && this.curPage == 0)) this.dirty = true;
        }

        public void setPagePixel(int pageNum, int x, int y, int color)
        {
            this.page(pageNum)[y * SCREEN_W + x] = (byte)color;
            if (pageNum == 0) this.dirty = true;
        }

        public int getPagePixel(int pageNum, int x, int y)
        {
            return this.page(pageNum)[y * SCREEN_W + x];
        }

        public void drawClippedLine(int x1, int y1, int x2, int y2, int color)
        {
            if (x1 > x2) (x1, x2) = (x2, x1);
            if (y1 > y2) (y1, y2) = (y2, y1);
            x1 = Math.Max(0, x1); y1 = Math.Max(0, y1); x2 = Math.Min(SCREEN_W - 1, x2); y2 = Math.Min(SCREEN_H - 1, y2);
            if (x1 == x2) for (int y = y1; y <= y2; y += 1) this.setPagePixel(this.curPage, x1, y, color);
            else for (int x = x1; x <= x2; x += 1) this.setPagePixel(this.curPage, x, y1, color);
        }

        public void drawBox(int x1, int y1, int x2, int y2, int color)
        {
            this.drawClippedLine(x1, y1, x2, y1, color);
            this.drawClippedLine(x1, y1, x1, y2, color);
            this.drawClippedLine(x2, y1, x2, y2, color);
            this.drawClippedLine(x1, y2, x2, y2, color);
        }

        public void drawShadedBox(int x1, int y1, int x2, int y2, int color1, int color2)
        {
            this.fillRect(x1, y1, x2, y1 + 1, color1);
            this.fillRect(x2 - 1, y1, x2, y2, color1);
            this.drawClippedLine(x1, y1, x1, y2, color2);
            this.drawClippedLine(x1 + 1, y1 + 1, x1 + 1, y2 - 1, color2);
            this.drawClippedLine(x1, y2 - 1, x2 - 1, y2 - 1, color2);
            this.drawClippedLine(x1, y2, x2, y2, color2);
        }

        // Screen::copyWsaRect: blit an offscreen WSA frame with clipping to a dim and a plot function.
        public void copyWsaRect(int x, int y, int w, int h, int dimState, int plotFunc, byte[] src, int unk1, byte[] table1, byte[] table2)
        {
            var dst = this.page(this.curPage);
            var dim = this.dims[dimState];
            int dimX1 = dim.sx << 3;
            int dimX2 = dimX1 + (dim.w << 3);
            int dimY1 = dim.sy;
            int dimY2 = dimY1 + dim.h;
            int srcPos = 0;
            int temp = y - dimY1;
            if (temp < 0)
            {
                if ((temp += h) <= 0) return;
                int nh = temp;
                y += h - nh;
                srcPos += (h - nh) * w;
                h = nh;
            }
            temp = dimY2 - y;
            if (temp <= 0) return;
            if (temp < h) h = temp;
            int srcOffset = 0;
            temp = x - dimX1;
            if (temp < 0)
            {
                srcOffset = -temp;
                x += srcOffset;
                w -= srcOffset;
            }
            int srcAdd = 0;
            temp = dimX2 - x;
            if (temp <= 0) return;
            if (temp < w)
            {
                srcAdd = w - temp;
                w = temp;
            }
            for (int row = 0; row < h; row += 1)
            {
                srcPos += srcOffset;
                int d = (y + row) * SCREEN_W + x;
                for (int i = 0; i < w; i += 1, d += 1)
                {
                    int v = src[srcPos++];
                    if (plotFunc == 0) dst[d] = (byte)v;
                    else if (plotFunc == 1)
                    {
                        int t = table1[v];
                        if (t != 0xff) v = table2[dst[d] + (t << 8)];
                        dst[d] = (byte)v;
                    }
                    else if (plotFunc == 4)
                    {
                        if (v != 0) dst[d] = (byte)v;
                    }
                    else if (plotFunc == 5)
                    {
                        if (v != 0)
                        {
                            int t = table1[v];
                            if (t != 0xff) v = table2[dst[d] + (t << 8)];
                            dst[d] = (byte)v;
                        }
                    }
                }
                srcPos += srcAdd;
            }
            if (this.curPage == 0) this.dirty = true;
        }

        // Screen_LoL::copyColor / fadeColor: palette entry animation used by fading message text.
        public void copyColor(int dstColorIndex, int srcColorIndex)
        {
            Js.Set(this.screenPalette, Js.Slice(this.screenPalette, srcColorIndex * 3, srcColorIndex * 3 + 3), dstColorIndex * 3);
            this.paletteDirty = true;
        }

        public bool fadeColor(int dstColorIndex, int srcColorIndex, int elapsedTicks, int totalTicks)
        {
            int dst = dstColorIndex * 3;
            int src = srcColorIndex * 3;
            var p = this.palettes[1];
            bool res = false;
            var tmp = new byte[] { 0, 0, 0 };
            for (int i = 0; i < 3; i += 1)
            {
                int outV;
                if (elapsedTicks < totalTicks)
                {
                    int srcV = this.screenPalette[src + i] & 0x3f;
                    int dstV = this.screenPalette[dst + i] & 0x3f;
                    outV = srcV - dstV;
                    if (outV != 0) res = true;
                    outV = dstV + (((int)((double)(outV << 8) / totalTicks) * elapsedTicks) >> 8);
                }
                else
                {
                    p[dst + i] = (byte)(outV = this.screenPalette[src + i]);
                    res = false;
                }
                tmp[i] = (byte)(outV & 0xff);
            }
            Js.Set(this.screenPalette, tmp, dst);
            this.paletteDirty = true;
            return res;
        }

        // Screen_LoL::drawGridBox
        public void drawGridBox(int x, int y, int w, int h, int col)
        {
            if (w <= 0 || x >= SCREEN_W || h <= 0 || y >= SCREEN_H) return;
            if (x < 0) { x = 0; w += x; if (w < 0) return; }
            if (x + w > SCREEN_W) w = SCREEN_W - x;
            if (y < 0) { y = 0; h += y; if (h < 0) return; }
            if (y + h > SCREEN_H) h = SCREEN_H - y;
            var dst = this.page(this.curPage);
            for (int row = 0; row < h; row += 1)
            {
                int start = (y + row) * SCREEN_W + x;
                for (int i = ((y + row + x) & 1) != 0 ? 1 : 0; i < w; i += 2) dst[start + i] = (byte)col;
            }
            if (this.curPage == 0) this.dirty = true;
        }

        // Bitmap (CPS) into a page; returns the file palette if present.
        public byte[] loadBitmap(byte[] bytes, int pageNum, byte[] palette)
        {
            var bitmap = Cps.decodeBitmapData(bytes);
            if (bitmap.data.Length != SCREEN_W * SCREEN_H) throw new Exception("Bitmap is not 320x200");
            Js.Set(this.page(pageNum), bitmap.data);
            if (pageNum == 0) this.dirty = true;
            if (palette != null && bitmap.palette.Length == 768) Js.Set(palette, bitmap.palette);
            return bitmap.palette;
        }

        // Fonts
        public void loadFont(string id, byte[] bytes)
        {
            this.fonts[id] = new DosFont(bytes);
            if (string.IsNullOrEmpty(this.currentFont)) this.currentFont = id;
        }

        public string setFont(string id)
        {
            var prev = this.currentFont;
            this.currentFont = id;
            return prev;
        }

        public DosFont font()
        {
            // JS fonts[missing] is undefined
            return this.currentFont != null && this.fonts.TryGetValue(this.currentFont, out var f) ? f : null;
        }

        public int fontHeight()
        {
            return this.font().height;
        }

        public int charWidth(int c)
        {
            return this.font().charWidth(c) + this.charSpacing;
        }

        public int textWidth(string str)
        {
            int cur = 0;
            int max = 0;
            for (int i = 0; i < str.Length; i += 1)
            {
                int c = str[i];
                if (c == 13)
                {
                    if (cur > max) max = cur;
                    else cur = 0;
                }
                else cur += this.charWidth(c);
            }
            return Math.Max(cur, max);
        }

        public void setTextColor(byte[] colors, int a)
        {
            Js.Set(this.textColors, colors, a);
        }

        public void printText(string str, int x, int y, int color1, int color2)
        {
            this.textColors[0] = (byte)color2;
            this.textColors[1] = (byte)color1;
            if (x < 0) x = 0;
            else if (x >= SCREEN_W) return;
            if (y < 0) y = 0;
            else if (y >= SCREEN_H) return;
            int xStart = x;
            var font = this.font();
            var page = this.page(this.curPage);
            for (int i = 0; i < str.Length; i += 1)
            {
                int c = str[i];
                if (c == 13)
                {
                    x = xStart;
                    y += font.height + this.lineSpacing;
                    continue;
                }
                int width = this.charWidth(c);
                if (x + width > this.textMarginRight)
                {
                    x = xStart;
                    y += font.height + this.lineSpacing;
                    if (y >= SCREEN_H) break;
                }
                if (x + width <= SCREEN_W && y + font.height <= SCREEN_H) font.drawChar(c, page, x, y, this.textColors);
                x += width;
            }
            if (this.curPage == 0) this.dirty = true;
        }

        // Screen_LoL::fprintString: flags 1 = center, 2 = right align, 4 = shadow.
        public void fprintString(string str, int x, int y, int col1, int col2, int flags)
        {
            if ((flags & 1) != 0) x -= this.textWidth(str) >> 1;
            if ((flags & 2) != 0) x -= this.textWidth(str);
            if ((flags & 4) != 0)
            {
                this.printText(str, x - 1, y, 1, col2);
                this.printText(str, x, y + 1, 1, col2);
            }
            if ((flags & 8) != 0)
            {
                this.printText(str, x - 1, y, 227, col2);
                this.printText(str, x, y + 1, 227, col2);
            }
            this.printText(str, x, y, col1, col2);
        }

        // Screen::drawShape. shape = { width, height, pixels, colorTable }.
        // opts: colorTable, fadeTable, fadeLevel, transparency1, transparency2, backgroundFade, scaleW, scaleH, morph.
        public void drawShape(int pageNum, Shape shape, int x, int y, int sd, int flags, DrawShapeOpts opts = null)
        {
            opts = opts ?? new DrawShapeOpts();
            if (shape == null) return;
            if (shape.colorTable != null) flags |= DRAWSHP.COMPACT;
            byte[] colorTable = (flags & DRAWSHP.COLOR) != 0 ? opts.colorTable : shape.colorTable;
            byte[] fadeTable = null;
            int fadeLevel = 0;
            if ((flags & DRAWSHP.FADE) != 0)
            {
                fadeTable = opts.fadeTable;
                fadeLevel = opts.fadeLevel | 0;
                if (fadeLevel == 0) flags &= ~DRAWSHP.FADE;
            }
            byte[] t1 = (flags & DRAWSHP.TRANSPARENT) != 0 ? opts.transparency1 : null;
            byte[] t2 = (flags & DRAWSHP.TRANSPARENT) != 0 ? opts.transparency2 : null;
            byte[] backgroundFade = (flags & DRAWSHP.BCKGRNDFADE) != 0 ? opts.backgroundFade : null;
            int scaleW = (flags & DRAWSHP.SCALE) != 0 ? opts.scaleW : 0x100;
            int scaleH = (flags & DRAWSHP.SCALE) != 0 ? opts.scaleH : 0x100;

            int ppc = (flags >> 8) & 0x3f;
            int fade(int cmd)
            {
                for (int i = 0; i < fadeLevel; i += 1) cmd = fadeTable[cmd];
                return cmd;
            }
            int transparent(int cmd, int under)
            {
                int offs = t1[cmd];
                return (offs & 0x80) != 0 ? cmd : t2[(offs << 8) | under];
            }
            Action<byte[], int, int> plot;
            switch (ppc)
            {
                case 0: plot = (dst, i, cmd) => { dst[i] = (byte)cmd; }; break;
                case 1: plot = (dst, i, cmd) => { cmd = fade(cmd); if (cmd != 0) dst[i] = (byte)cmd; }; break;
                case 3: case 7: plot = (dst, i, _) => { int cmd = fade(dst[i]); if (cmd != 0) dst[i] = (byte)cmd; }; break;
                case 4: plot = (dst, i, cmd) => { dst[i] = colorTable[cmd]; }; break;
                case 5: plot = (dst, i, cmd) => { cmd = fade(colorTable[cmd]); if (cmd != 0) dst[i] = (byte)cmd; }; break;
                case 16: plot = (dst, i, cmd) => { dst[i] = (byte)transparent(cmd, dst[i]); }; break;
                case 20: plot = (dst, i, cmd) => { dst[i] = (byte)transparent(colorTable[cmd], dst[i]); }; break;
                case 21: plot = (dst, i, cmd) => { cmd = fade(transparent(colorTable[cmd], dst[i])); if (cmd != 0) dst[i] = (byte)cmd; }; break;
                case 33: plot = (dst, i, cmd) => { if (cmd == 255) dst[i] = backgroundFade[dst[i]]; else { cmd = fade(cmd); if (cmd != 0) dst[i] = (byte)cmd; } }; break;
                case 37: plot = (dst, i, cmd) => { cmd = colorTable[cmd]; cmd = cmd == 255 ? backgroundFade[dst[i]] : fade(cmd); if (cmd != 0) dst[i] = (byte)cmd; }; break;
                case 48: plot = (dst, i, cmd) => { dst[i] = (byte)transparent(cmd, dst[i]); }; break;
                case 52: plot = (dst, i, cmd) => { dst[i] = (byte)transparent(colorTable[cmd], dst[i]); }; break;
                default:
                    Console.Error.WriteLine($"Missing drawShape plotting method type {ppc}"); // console.warn
                    return;
            }

            var dim = this.dims[sd];
            var dstPage = this.page(pageNum);
            if ((flags & DRAWSHP.WINREL) == 0) x -= dim.sx << 3;
            int clipX1 = dim.sx << 3;
            int clipX2 = clipX1 + (dim.w << 3);
            int clipY1 = dim.sy;
            int clipY2 = clipY1 + dim.h;
            if ((flags & DRAWSHP.WINREL) != 0) y += clipY1;
            x += clipX1;

            int shapeHeight = shape.height;
            int scaledW = shape.width;
            if ((flags & DRAWSHP.SCALE) != 0)
            {
                shapeHeight = LolShapes.scaledSize(shape.height, scaleH);
                scaledW = LolShapes.scaledSize(shape.width, scaleW);
                if (shapeHeight == 0 || scaledW == 0) return;
            }
            if ((flags & DRAWSHP.CENTER) != 0)
            {
                x -= scaledW >> 1;
                y -= shapeHeight >> 1;
            }
            bool yFlip = (flags & DRAWSHP.YFLIP) != 0;
            bool xFlip = (flags & DRAWSHP.XFLIP) != 0;
            if (yFlip) y = clipY2 - (y - clipY1) - shapeHeight;
            DrawShapeRecord rec = null;
            if (this.recorder != null)
            {
                // Everything a second implementation needs to repeat this draw exactly: which brightness
                // table was used (they are the level overlays, by index), the shape's own colour table, and
                // whether the transparency and background-fade tables were in play.
                var overlays = this.levelOverlays ?? new byte[0][];
                int overlayIndexOf(byte[] table)
                {
                    if (table == null) return -1;
                    for (int i = 0; i < overlays.Length; i += 1) if (overlays[i] == table) return i;
                    return -1;
                }
                int fadeTableIndex = overlayIndexOf(fadeTable);
                int backgroundFadeIndex = overlayIndexOf(backgroundFade);
                int[] from(byte[] a) => Array.ConvertAll(a, v => (int)v); // Array.from
                rec = new DrawShapeRecord
                {
                    key = shape.key, x = x, y = y, w = scaledW, h = shapeHeight, xFlip = xFlip, yFlip = yFlip,
                    clip = new[] { clipX1, clipY1, clipX2, clipY2 }, fade = fadeLevel, ppc = ppc, page = pageNum,
                    srcW = shape.width, srcH = shape.height, scaleW = scaleW, scaleH = scaleH,
                    fadeTableIndex = fadeTableIndex,
                    fadeTable = fadeLevel != 0 && fadeTableIndex < 0 && fadeTable != null ? from(fadeTable) : null,
                    colorTable = colorTable != null ? from(colorTable) : null,
                    transparency = t1 != null,
                    backgroundFade = backgroundFade != null,
                    backgroundFadeIndex = backgroundFadeIndex,
                    backgroundFadeTable = backgroundFade != null && backgroundFadeIndex < 0 ? from(backgroundFade) : null,
                    painted = new List<int>(),
                };
                this.recorder.Add(rec);
            }

            int acc = 0;
            int targetY = y;
            int width = shape.width;
            for (int sourceY = 0; sourceY < shape.height; sourceY += 1)
            {
                acc += scaleH;
                for (; acc >= 0x100; acc -= 0x100, targetY += 1)
                {
                    int outY = yFlip ? y + shapeHeight - 1 - (targetY - y) : targetY;
                    if (outY < clipY1 || outY >= clipY2) continue;
                    int accX = 0;
                    int column = 0;
                    int row = sourceY * width;
                    int rowStart = outY * SCREEN_W;
                    for (int sourceX = 0; sourceX < width; sourceX += 1)
                    {
                        accX += scaleW;
                        int cmd = shape.pixels[row + sourceX];
                        for (; accX >= 0x100; accX -= 0x100, column += 1)
                        {
                            if (cmd == 0) continue;
                            int targetX = xFlip ? x + scaledW - 1 - column : x + column;
                            if (targetX >= clipX1 && targetX < clipX2) { plot(dstPage, rowStart + targetX, cmd); rec?.painted.Add(rowStart + targetX); }
                        }
                    }
                }
            }
            if (rec != null)
            {
                rec.colors = new byte[rec.painted.Count];
                for (int i = 0; i < rec.colors.Length; i += 1) rec.colors[i] = dstPage[rec.painted[i]];
            }
            if (pageNum == 0) this.dirty = true;
        }

        // Screen_v2::generateOverlay (LoL 256-color variant).
        public byte[] generateOverlay(byte[] pal, byte[] buffer, int opColor, int weight, int maxColor = 127)
        {
            weight = Math.Min(weight, 255) >> 1;
            var op = new int[] { pal[opColor * 3], pal[opColor * 3 + 1], pal[opColor * 3 + 2] };
            buffer[0] = 0;
            for (int i = 1; i < 256; i += 1)
            {
                var cur = new int[3];
                for (int c = 0; c < 3; c += 1) cur[c] = (pal[i * 3 + c] - (((pal[i * 3 + c] - op[c]) * weight) >> 7)) & 0xff;
                int best = 0x7fff;
                int index = opColor;
                for (int candidate = 1; candidate <= maxColor; candidate += 1)
                {
                    if (candidate == i) continue;
                    int sum = 0;
                    for (int c = 0; c < 3; c += 1)
                    {
                        int diff = pal[candidate * 3 + c] - cur[c];
                        sum += diff * diff;
                    }
                    if (sum == 0) { index = candidate; break; }
                    if (sum <= best) { best = sum; index = candidate; }
                }
                buffer[i] = (byte)index;
            }
            return buffer;
        }

        public int findLeastDifferentColor(Bytes entry, byte[] pal, int firstColor, int numColors, bool skipSpecialColors = false)
        {
            int m = 0x7fff;
            int r = 0x101;
            for (int i = 0; i < numColors; i += 1)
            {
                if (skipSpecialColors && i >= 0xc0 && i <= 0xc3) continue;
                int c = 0;
                for (int k = 0; k < 3; k += 1)
                {
                    int v = entry[k] - pal[(i + firstColor) * 3 + k];
                    c += v * v;
                }
                if (c <= m) { m = c; r = i; }
            }
            return r;
        }

        // Screen_LoL::generateGrayOverlay
        public void generateGrayOverlay(byte[] srcPal, byte[] overlay, int factor, int addR, int addG, int addB, int lastColor, bool skipSpecialColors)
        {
            var tmp = new byte[lastColor * 3];
            for (int i = 0; i < lastColor; i += 1)
            {
                foreach (var (k, add) in new[] { (0, addR), (1, addG), (2, addB) })
                {
                    int v = (int)(((srcPal[3 * i + k] & 0x3f) * factor) / (double)0x40) + add;
                    tmp[3 * i + k] = (byte)(v > 0x3f ? 0x3f : v & 0xff);
                }
            }
            // overlay[i] = 0x101 wraps like the Uint8Array store
            for (int i = 0; i < lastColor; i += 1) overlay[i] = (byte)this.findLeastDifferentColor(new Bytes(tmp).Sub(3 * i, 3 * i + 3), srcPal, 0, lastColor, skipSpecialColors);
        }

        public void loadSpecialColors(byte[] dst)
        {
            Js.Set(dst, Js.Slice(this.screenPalette, 192 * 3, 196 * 3), 192 * 3);
        }

        // Screen::fadePalette (Screen_v2::getFadeParams variant), driven by sys.delay.
        public async Task fadePalette(byte[] pal, int delay)
        {
            this.sys.present();
            int maxDiff = 0;
            for (int i = 0; i < 768; i += 1) maxDiff = Math.Max(maxDiff, Math.Abs(pal[i] - this.screenPalette[i]));
            int delayInc = delay << 8;
            if (maxDiff != 0) delayInc = Math.Min((int)((double)delayInc / maxDiff), 0x7fff);
            int step = delayInc;
            int diff;
            for (diff = 1; diff <= maxDiff; diff += 1)
            {
                if (delayInc >= 256) break;
                delayInc += step;
            }
            int delayAcc = 0;
            while (true)
            {
                delayAcc += delayInc;
                bool refreshed = false;
                var next = (byte[])this.screenPalette.Clone();
                for (int i = 0; i < 768; i += 1)
                {
                    int c1 = pal[i];
                    int c2 = next[i];
                    if (c1 == c2) continue;
                    refreshed = true;
                    if (c1 > c2) c2 = Math.Min(c1, c2 + diff);
                    else c2 = Math.Max(c1, c2 - diff);
                    next[i] = (byte)c2;
                }
                if (!refreshed) break;
                this.setScreenPalette(next);
                this.sys.present();
                await this.sys.delay(((delayAcc >> 8) * 1000) / 60.0);
                delayAcc &= 0xff;
            }
        }

        public async Task fadeToBlack(int delay)
        {
            await this.fadePalette(new byte[768], delay);
            this.fadeFlag = 2;
        }

        public async Task fadeToPalette1(int delay)
        {
            this.loadSpecialColors(this.palettes[1]);
            await this.fadePalette(this.palettes[1], delay);
            this.fadeFlag = 0;
        }

        public async Task fadeClearSceneWindow(int delay)
        {
            if (this.fadeFlag == 1) return;
            var tpal = (byte[])this.palettes[0].Clone();
            Js.Fill(tpal, (byte)0, 0, 128 * 3);
            this.loadSpecialColors(tpal);
            await this.fadePalette(tpal, delay);
            this.fillRect(112, 0, 288, 120, 0);
            this.fadeFlag = 1;
        }

        public void backupSceneWindow(int srcPage, int dstPage)
        {
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            for (int h = 0; h < 120; h += 1) Js.Set(dst, Js.Slice(src, h * SCREEN_W + 112, h * SCREEN_W + 288), 0xa500 + h * 176);
        }

        public void restoreSceneWindow(int srcPage, int dstPage)
        {
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            for (int h = 0; h < 120; h += 1) Js.Set(dst, Js.Slice(src, 0xa500 + h * 176, 0xa500 + h * 176 + 176), h * SCREEN_W + 112);
            if (dstPage == 0) this.dirty = true;
        }

        /// <summary>_calcBounds result: { na, nb, x, y, w, h }</summary>
        public sealed class Bounds
        {
            public int na, nb, x, y, w, h;
        }

        public Bounds _calcBounds(ScreenDim dim, int x, int y, int w, int h)
        {
            // Screen_LoL::calcBounds: clip (x, y, w, h) to the dim; returns [ok, skipLeft, x, y, w, h].
            int iw = dim.w << 3;
            int ih = dim.h;
            int na = 0;
            if (x + w <= 0 || y + h <= 0 || x >= iw || y >= ih) return null;
            if (x < 0) { na = -x; w += x; x = 0; }
            if (x + w > iw) w = iw - x;
            int nb = 0;
            if (y < 0) { nb = -y; h += y; y = 0; }
            if (y + h > ih) h = ih - y;
            return new Bounds { na = na, nb = nb, x = x, y = y, w = w, h = h };
        }

        public void applyOverlay(int x, int y, int w, int h, int pageNum, byte[] overlay)
        {
            var dst = this.page(pageNum);
            for (int yy = 0; yy < h; yy += 1)
            {
                int row = (y + yy) * SCREEN_W + x;
                for (int xx = 0; xx < w; xx += 1) dst[row + xx] = overlay[dst[row + xx]];
            }
            if (pageNum == 0) this.dirty = true;
        }

        // Screen::crossFadeRegion: copies the region pixel by pixel in a shuffled order, one row-slice per 3ms.
        public async Task crossFadeRegion(int x1, int y1, int x2, int y2, int w, int h, int srcPage, int dstPage)
        {
            var wB = new int[w];
            for (int i = 0; i < w; i += 1) wB[i] = i;
            var hB = new int[h];
            for (int i = 0; i < h; i += 1) hB[i] = i;
            for (int i = 0; i < w; i += 1) { int j = Js.Floor(this.mathRandom() * w); (wB[i], wB[j]) = (wB[j], wB[i]); }
            for (int i = 0; i < h; i += 1) { int j = Js.Floor(this.mathRandom() * h); (hB[i], hB[j]) = (hB[j], hB[i]); }
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            for (int i = 0; i < h; i += 1)
            {
                int iH = i;
                double end = this.sys.getMillis() + 3;
                for (int ii = 0; ii < w; ii += 1)
                {
                    dst[(y2 + hB[iH]) * SCREEN_W + x2 + wB[ii]] = src[(y1 + hB[iH]) * SCREEN_W + x1 + wB[ii]];
                    if (++iH >= h) iH = 0;
                }
                if (dstPage == 0) this.dirty = true;
                // JS `await this.sys.present()`: present() is sync, awaiting its undefined still yields a microtask
                if ((i % 10) == 0) { this.sys.present(); await Task.Yield(); }
                double cur = this.sys.getMillis();
                if (end > cur) await this.sys.delay(end - cur);
            }
            this.sys.present(); await Task.Yield();
        }

        public void copyBlockAndApplyOverlay(int page1, int x1, int y1, int page2, int x2, int y2, int w, int h, int dim, byte[] ovl)
        {
            if (w == 0 || h == 0 || ovl == null) return;
            var cdim = this.dims[dim];
            var b = this._calcBounds(cdim, x2, y2, w, h);
            if (b == null) return;
            var src = this.page(page1);
            var dst = this.page(page2);
            for (int i = 0; i < b.h; i += 1)
            {
                int s = (y1 + b.nb + i) * SCREEN_W + x1 + b.na;
                int d = (b.y + cdim.sy + i) * SCREEN_W + b.x + (cdim.sx << 3);
                for (int ii = 0; ii < b.w; ii += 1)
                {
                    byte p = ovl[src[s + ii]];
                    if (p != 0) dst[d + ii] = p;
                }
            }
            if (page2 == 0) this.dirty = true;
        }

        public void applyOverlaySpecial(int page1, int x1, int y1, int page2, int x2, int y2, int w, int h, int dim, int flag, byte[] ovl)
        {
            if (w == 0 || h == 0 || ovl == null) return;
            var cdim = this.dims[dim];
            var b = this._calcBounds(cdim, x2, y2, w, h);
            if (b == null) return;
            var src = this.page(page1);
            var dst = this.page(page2);
            for (int i = 0; i < b.h; i += 1)
            {
                int s = (y1 + b.nb + i) * SCREEN_W + x1 + b.na;
                int d = (b.y + cdim.sy + i) * SCREEN_W + b.x + (cdim.sx << 3);
                if (flag != 0) d += i >> 1;
                for (int ii = 0; ii < b.w; ii += 1) if (src[s + ii] != 0) dst[d + ii] = ovl[dst[d + ii]];
            }
            if (page2 == 0) this.dirty = true;
        }
        // ---- Screen_LoL smooth scrolling helpers (pointer arithmetic ported 1:1) ----
        public void _zoomStep(int srcPage, int dstPage, int srcStart, int dstStart, int x, int y, int baseHeight)
        {
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            int s = srcStart;
            int d = dstStart;
            x <<= 1;
            int width = 176 - x;
            int scaleX = ((int)((double)((x + 1) << 8) / width) + 0x100) & 0xffff;
            int cntW = scaleX >> 8;
            scaleX = (scaleX << 8) & 0xffff;
            width -= 1;
            int widthCnt = width;
            int height = baseHeight - y;
            int scaleY = ((int)((double)((y + 1) << 8) / height) + 0x100) & 0xffff;
            scaleY = (scaleY << 8) & 0xffff;
            uint scaleYc = 0;
            while (height != 0)
            {
                uint scaleXc = 0;
                do
                {
                    scaleXc = scaleXc + (uint)scaleX;
                    int numbytes = cntW + (int)(scaleXc >> 16);
                    scaleXc &= 0xffff;
                    // Typed arrays: a read past the end is undefined (fills 0), a write past it is dropped.
                    Js.Fill(dst, s < src.Length ? src[s] : (byte)0, d, d + numbytes);
                    s++;
                    d += numbytes;
                } while (--widthCnt != 0);
                if (d < dst.Length) dst[d] = s < src.Length ? src[s] : (byte)0;
                d++;
                s++;
                widthCnt = width;
                s += x;
                scaleYc = scaleYc + (uint)scaleY;
                if ((scaleYc >> 16) != 0)
                {
                    scaleYc = 0;
                    s -= 176;
                    continue;
                }
                height -= 1;
            }
        }

        public void smoothScrollZoomStepTop(int srcPage, int dstPage, int x, int y)
        {
            this._zoomStep(srcPage, dstPage, 0xa500 + y * 176 + x, 0xa500, x, y, 46);
        }

        public void smoothScrollZoomStepBottom(int srcPage, int dstPage, int x, int y)
        {
            this._zoomStep(srcPage, dstPage, 0xc4a0 + x, 0xc4a0, x, y, 74);
        }

        public void smoothScrollHorizontalStep(int pageNum, int srcX, int dstX, int w)
        {
            var pg = this.page(pageNum);
            int d = 0;
            int s = 112 + srcX;
            int w2 = srcX + w - dstX;
            int pitchS = 320 + w2 - (w << 1);
            int pitchD = 320 - w;
            for (int h = 0; h < 120; h += 1)
            {
                for (int i = 0; i < w; i += 1) pg[d++] = pg[s++];
                d -= w;
                s -= w2;
                for (int i = 0; i < w; i += 1) pg[s++] = pg[d++];
                s += pitchS;
                d += pitchD;
            }
        }

        public void smoothScrollTurnStep(int step, int srcPage1, int srcPage2, int dstPage)
        {
            var p1 = this.page(srcPage1);
            var p2 = this.page(srcPage2);
            var dst = this.page(dstPage);
            int s;
            int d;
            if (step == 1)
            {
                s = 273; d = 0xa500;
                for (int i = 0; i < 120; i += 1)
                {
                    byte a = p1[s++];
                    dst[d++] = a; dst[d++] = a;
                    for (int ii = 0; ii < 14; ii += 1) { a = p1[s++]; dst[d++] = a; dst[d++] = a; dst[d++] = a; }
                    s += 305; d += 132;
                }
                s = 112; d = 0xa52c;
                for (int i = 0; i < 120; i += 1)
                {
                    for (int ii = 0; ii < 33; ii += 1) { dst[d++] = p2[s++]; dst[d++] = p2[s++]; byte a = p2[s++]; dst[d++] = a; dst[d++] = a; }
                    s += 221; d += 44;
                }
            }
            else if (step == 2)
            {
                s = 244; d = 0xa500;
                var src = p1;
                for (int k = 0; k < 2; k += 1)
                {
                    for (int i = 0; i < 120; i += 1)
                    {
                        for (int ii = 0; ii < 44; ii += 1) { byte a = src[s++]; dst[d++] = a; dst[d++] = a; }
                        s += 276; d += 88;
                    }
                    src = p2; s = 112; d = 0xa558;
                }
            }
            else
            {
                s = 189; d = 0xa500;
                for (int i = 0; i < 120; i += 1)
                {
                    for (int ii = 0; ii < 33; ii += 1) { dst[d++] = p1[s++]; dst[d++] = p1[s++]; byte a = p1[s++]; dst[d++] = a; dst[d++] = a; }
                    s += 221; d += 44;
                }
                s = 112; d = 0xa584;
                for (int i = 0; i < 120; i += 1)
                {
                    for (int ii = 0; ii < 14; ii += 1) { byte a2 = p2[s++]; dst[d++] = a2; dst[d++] = a2; dst[d++] = a2; }
                    byte a = p2[s++]; dst[d++] = a; dst[d++] = a;
                    s += 305; d += 132;
                }
            }
        }

        public void clearGuiShapeMemory(int pageNum)
        {
            var dst = this.page(pageNum);
            for (int i = 0; i < 23; i += 1) Js.Fill(dst, (byte)0, 0x79b0 + i * 320, 0x79b0 + i * 320 + 176);
        }

        public void copyGuiShapeFromSceneBackupBuffer(int srcPage, int dstPage)
        {
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            int s = 0x79c3;
            int d = 0;
            for (int i = 0; i < 23; i += 1)
            {
                int len = 0;
                int v = 0;
                do { v = src[s++]; len += 1; } while (v == 0);
                dst[d++] = (byte)len;
                len = 69 - len;
                Js.Set(dst, Js.Slice(src, s, s + len), d);
                s += len + 251;
                d += len;
            }
        }

        public void copyGuiShapeToSurface(int srcPage, int dstPage)
        {
            var src = this.page(srcPage);
            var dst = this.page(dstPage);
            int s = 0;
            int d = 0xe7c3;
            for (int i = 0; i < 23; i += 1)
            {
                int v = src[s++];
                int len = 69 - v;
                d += v;
                Js.Set(dst, Js.Slice(src, s, s + len), d);
                s += len - 1;
                d += len;
                for (int ii = 0; ii < len; ii += 1) dst[d++] = src[s--];
                s += len + 1;
                d += v + 38;
            }
            if (dstPage == 0) this.dirty = true;
        }
    }
}
