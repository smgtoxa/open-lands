// The screen: indexed pages, 6-bit VGA palettes, and the index-remap tables the rasteriser reads.
//
// Transliterated from src/game/screen.mjs (ScummVM's Screen / Screen_LoL) and the overlay build in
// scene.mjs loadLevelGraphics. The brightness overlays are the reason this file exists: they are
// nearest-colour searches over the level palette, and if a port gets them wrong the walls fade to
// slightly the wrong colours in a way nobody can eyeball.
namespace LolCore;

/// <summary>Screen_LoL's drawShape flags.</summary>
public static class DrawShp
{
    public const int XFlip = 0x01, YFlip = 0x02, Scale = 0x04, WinRel = 0x10, Center = 0x20,
        Fade = 0x100, Compact = 0x400, Transparent = 0x1000, BackgroundFade = 0x2000, Color = 0x8000;
}

/// <summary>One screen dimension: an 8-pixel-granular x, a pixel y, and a size (Screen::_screenDimTable).</summary>
public sealed class ScreenDim
{
    public int Sx, Sy, W, H, Col1, Col2;
}

/// <summary>
/// A Westwood DOS bitmap font: a glyph is a run of 4-bit colour indices, with the blank rows above
/// and below stored as counts rather than pixels. Transliterated from DosFont in
/// src/game/screen.mjs; in-game text goes through this, not through any font the host provides,
/// because the word wrap divides by these glyph widths.
/// </summary>
public sealed class DosFont
{
    private readonly byte[] _data;
    private readonly int[] _bitmapOffsets;
    private readonly int _widthTable;
    private readonly int _heightTable;

    public readonly int Width;
    public readonly int Height;
    public readonly int NumGlyphs;

    public DosFont(byte[] bytes)
    {
        int U16(int at) => bytes[at] | (bytes[at + 1] << 8);
        if (U16(2) != 0x0500) throw new InvalidDataException("Invalid DOS font");
        int desc = U16(4);
        _data = bytes;
        Width = bytes[desc + 5];
        Height = bytes[desc + 4];
        NumGlyphs = bytes[desc + 3] + 1;
        int offsets = U16(6);
        _bitmapOffsets = new int[NumGlyphs];
        for (int i = 0; i < NumGlyphs; i += 1) _bitmapOffsets[i] = U16(offsets + i * 2);
        _widthTable = U16(8);
        _heightTable = U16(12);
    }

    public int CharWidth(int c) => c < NumGlyphs ? _data[_widthTable + c] : 0;

    public void DrawChar(int c, byte[] page, int x, int y, byte[] colorMap)
    {
        if (c >= NumGlyphs || _bitmapOffsets[c] == 0) return;
        int width = _data[_widthTable + c];
        if (width == 0) return;
        int src = _bitmapOffsets[c];
        int h1 = _data[_heightTable + c * 2];
        int h2 = _data[_heightTable + c * 2 + 1];
        int h0 = Height - (h1 + h2);
        int row = y * Screen.Width + x;

        void Fill(int rows)
        {
            byte color = colorMap[0];
            for (int r = 0; r < rows; r += 1, row += Screen.Width)
                if (color != 0) for (int i = 0; i < width; i += 1) page[row + i] = color;
        }

        Fill(h1);
        for (int r = 0; r < h2; r += 1, row += Screen.Width)
        {
            int b = 0;
            for (int i = 0; i < width; i += 1)
            {
                byte color;
                if ((i & 1) != 0) color = colorMap[b >> 4];
                else
                {
                    b = _data[src++];
                    color = colorMap[b & 0x0f];
                }
                if (color != 0) page[row + i] = color;
            }
        }
        Fill(h0);
    }
}

public sealed partial class Screen
{
    public const int Width = 320;
    public const int Height = 200;
    /// <summary>Pages 0..7 are the games; the cinematics compose on page 8.</summary>
    public const int PageCount = 16;

    // Screen::_screenDimTable, as in src/game/screen.mjs DIM_TABLE.
    private static readonly int[][] DimTable =
    {
        new[]{0x00,0x00,0x28,0xc8,0xc7,0xcf}, new[]{0x08,0x48,0x18,0x38,0xfe,0x01}, new[]{0x0e,0x00,0x16,0x78,0xfe,0x01},
        new[]{0x0b,0x7b,0x1c,0x12,0xfe,0xfc}, new[]{0x0b,0x7b,0x1c,0x2d,0xfe,0xfc}, new[]{0x55,0x7b,0xe9,0x37,0xfe,0xfc},
        new[]{0x0b,0x8c,0x10,0x2b,0x3d,0x01}, new[]{0x04,0x59,0x20,0x3c,0x00,0x00}, new[]{0x05,0x6e,0x1e,0x0c,0xfe,0x01},
        new[]{0x07,0x19,0x1a,0x97,0x00,0x00}, new[]{0x03,0x1e,0x22,0x8c,0x00,0x00}, new[]{0x02,0x48,0x24,0x34,0x00,0x00},
        new[]{0x00,0x00,0x00,0x00,0x00,0x00}, new[]{0x0e,0x00,0x16,0x78,0xfe,0x01}, new[]{0x0d,0xa2,0x18,0x0c,0xfe,0x01},
        new[]{0x0f,0x06,0x14,0x6e,0x01,0x00}, new[]{0x1a,0xbe,0x0a,0x07,0xfe,0x01}, new[]{0x07,0x21,0x1a,0x85,0x00,0x00},
        new[]{0x03,0x32,0x22,0x62,0x00,0x00}, new[]{0x0b,0x8c,0x10,0x33,0x3d,0x01}, new[]{0x0b,0x8c,0x10,0x23,0x3d,0x01},
        new[]{0x01,0x20,0x26,0x80,0xdc,0xfd}, new[]{0x09,0x29,0x08,0x2c,0x00,0x00}, new[]{0x19,0x29,0x08,0x2c,0x00,0x00},
        new[]{0x01,0x02,0x26,0x14,0x00,0x0f},
    };

    public readonly ScreenDim[] Dims = DimTable.Select(d => new ScreenDim { Sx = d[0], Sy = d[1], W = d[2], H = d[3], Col1 = d[4], Col2 = d[5] }).ToArray();

    /// <summary>Set to a list to capture the draws, the way the JavaScript recorder does.</summary>
    public List<ShapeOp> Recorder;

    private readonly Dictionary<string, DosFont> _fonts = new();
    private string _currentFont;
    private readonly byte[] _textColors = new byte[16];

    public int CurPage;
    public int CurDimIndex;
    public int CharSpacing;
    public int LineSpacing;
    public int TextMarginRight = Width;

    public void LoadFont(string id, byte[] bytes)
    {
        _fonts[id] = new DosFont(bytes);
        _currentFont ??= id;
    }

    public string SetFont(string id)
    {
        var previous = _currentFont;
        _currentFont = id;
        return previous;
    }

    public DosFont Font => _fonts[_currentFont];

    public bool HasFont(string id) => _fonts.ContainsKey(id);

    public int FontHeight => Font.Height + LineSpacing;

    /// <summary>
    /// The palette the display is showing, which is not always the one being faded towards: a
    /// cinematic walks getPalette(0) towards its target a step per frame and pushes each step out
    /// here. Everything else in the game shows palette 0 directly.
    /// </summary>
    public readonly byte[] ScreenPalette = new byte[768];

    public void SetScreenPalette(byte[] pal) => Array.Copy(pal, ScreenPalette, Math.Min(768, pal.Length));

    /// <summary>Screen::setTextColor - the whole 16-entry map a glyph is drawn through.</summary>
    public void SetTextColor(byte[] map) => Array.Copy(map, _textColors, Math.Min(_textColors.Length, map.Length));

    public byte[] TextColors => (byte[])_textColors.Clone();

    /// <summary>Screen::checkedPageUpdate: copies the pixels that differ between two pages onto page 0.</summary>
    public void CheckedPageUpdate(int srcPage, int dstPage)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        var out_ = Page(0);
        for (int i = 0; i < src.Length; i += 1)
        {
            if (src[i] == dst[i]) continue;
            dst[i] = src[i];
            out_[i] = src[i];
        }
    }

    public int CharWidth(int c) => Font.CharWidth(c) + CharSpacing;

    public int TextWidth(string str)
    {
        int cur = 0, max = 0;
        foreach (char ch in str)
        {
            if (ch == 13)
            {
                if (cur > max) max = cur;
                else cur = 0;
            }
            else cur += CharWidth(ch);
        }
        return Math.Max(cur, max);
    }

    /// <summary>Screen::printText, with the same wrap-at-the-margin behaviour.</summary>
    public void PrintText(string str, int x, int y, byte color1, byte color2)
    {
        _textColors[0] = color2;
        _textColors[1] = color1;
        if (x < 0) x = 0;
        else if (x >= Width) return;
        if (y < 0) y = 0;
        else if (y >= Height) return;
        int xStart = x;
        var font = Font;
        var page = Page(CurPage);
        foreach (char ch in str)
        {
            int c = ch;
            if (c == 13)
            {
                x = xStart;
                y += font.Height + LineSpacing;
                continue;
            }
            int width = CharWidth(c);
            if (x + width > TextMarginRight)
            {
                x = xStart;
                y += font.Height + LineSpacing;
                if (y >= Height) break;
            }
            if (x + width <= Width && y + font.Height <= Height) font.DrawChar(c, page, x, y, _textColors);
            x += width;
        }
    }

    /// <summary>Screen_LoL::fprintString: 1 centres, 2 right-aligns, 4 and 8 draw a shadow first.</summary>
    public void PrintString(string str, int x, int y, byte col1, byte col2, int flags)
    {
        if ((flags & 1) != 0) x -= TextWidth(str) >> 1;
        if ((flags & 2) != 0) x -= TextWidth(str);
        if ((flags & 4) != 0)
        {
            PrintText(str, x - 1, y, 1, col2);
            PrintText(str, x, y + 1, 1, col2);
        }
        if ((flags & 8) != 0)
        {
            PrintText(str, x - 1, y, 227, col2);
            PrintText(str, x, y + 1, 227, col2);
        }
        PrintText(str, x, y, col1, col2);
    }

    private ShapeRasteriser _rasteriser;
    public ShapeRasteriser Rasteriser => _rasteriser ??= new ShapeRasteriser(LevelOverlays, TransparencyTable1, TransparencyTable2);

    public void ModifyScreenDim(int index, int sx, int sy, int w, int h)
    {
        var dim = Dims[index];
        dim.Sx = sx;
        dim.Sy = sy;
        dim.W = w;
        dim.H = h;
        CurDimIndex = index;
    }

    public static int ScaledSize(int value, int scale) => (value * scale) >> 8;

    /// <summary>
    /// Screen_LoL::drawShape: works out the clip rectangle, the scaled size and the flips, then
    /// hands the blit to the rasteriser. Straight from src/game/screen.mjs drawShape.
    /// </summary>
    public void DrawShape(int pageNum, Shape shape, int x, int y, int dimIndex, int flags, ShapeDrawOptions opts = null)
    {
        if (shape == null) return;
        opts ??= new ShapeDrawOptions();
        if (shape.ColorTable != null) flags |= DrawShp.Compact;
        var colorTable = (flags & DrawShp.Color) != 0 ? opts.ColorTable : shape.ColorTable;
        byte[] fadeTable = null;
        int fadeLevel = 0;
        if ((flags & DrawShp.Fade) != 0)
        {
            fadeTable = opts.FadeTable;
            fadeLevel = opts.FadeLevel;
            if (fadeLevel == 0) flags &= ~DrawShp.Fade;
        }
        bool transparent = (flags & DrawShp.Transparent) != 0;
        var backgroundFade = (flags & DrawShp.BackgroundFade) != 0 ? opts.BackgroundFade : null;
        int scaleW = (flags & DrawShp.Scale) != 0 ? opts.ScaleW : 0x100;
        int scaleH = (flags & DrawShp.Scale) != 0 ? opts.ScaleH : 0x100;

        var dim = Dims[dimIndex];
        if ((flags & DrawShp.WinRel) == 0) x -= dim.Sx << 3;
        int clipX1 = dim.Sx << 3;
        int clipX2 = clipX1 + (dim.W << 3);
        int clipY1 = dim.Sy;
        int clipY2 = clipY1 + dim.H;
        if ((flags & DrawShp.WinRel) != 0) y += clipY1;
        x += clipX1;

        int shapeHeight = shape.Height;
        int scaledW = shape.Width;
        if ((flags & DrawShp.Scale) != 0)
        {
            shapeHeight = ScaledSize(shape.Height, scaleH);
            scaledW = ScaledSize(shape.Width, scaleW);
            if (shapeHeight == 0 || scaledW == 0) return;
        }
        if ((flags & DrawShp.Center) != 0)
        {
            x -= scaledW >> 1;
            y -= shapeHeight >> 1;
        }
        bool yFlip = (flags & DrawShp.YFlip) != 0;
        bool xFlip = (flags & DrawShp.XFlip) != 0;
        if (yFlip) y = clipY2 - (y - clipY1) - shapeHeight;

        var op = new ShapeOp
        {
            Key = shape.Key, X = x, Y = y, W = scaledW, H = shapeHeight, XFlip = xFlip, YFlip = yFlip,
            Clip = new[] { clipX1, clipY1, clipX2, clipY2 }, Fade = fadeLevel, Ppc = (flags >> 8) & 0x3f, Page = pageNum,
            ScaleW = scaleW, ScaleH = scaleH, FadeTable = fadeLevel != 0 ? fadeTable : null, ColorTable = colorTable,
            Transparency = transparent, BackgroundFade = backgroundFade != null, BackgroundFadeTable = backgroundFade,
        };
        Recorder?.Add(op);
        Rasteriser.Draw(Page(pageNum), Width, shape, op);
    }

    private readonly byte[][] _pages = new byte[PageCount][];
    private readonly byte[][] _palettes = { new byte[768], new byte[768], new byte[768], new byte[768] };

    /// <summary>Eight brightness levels, index to index. 7 is "no change".</summary>
    public readonly byte[][] LevelOverlays = new byte[8][];
    public readonly byte[] TransparencyTable1 = new byte[256];
    public readonly byte[] TransparencyTable2 = new byte[5120];

    public Screen()
    {
        for (int i = 0; i < LevelOverlays.Length; i += 1) LevelOverlays[i] = new byte[256];
    }

    // VGA page mapping, as in Screen::_pageMapping: odd pages share the even one below them.
    public byte[] Page(int number)
    {
        number &= ~1;
        return _pages[number] ??= new byte[Width * Height];
    }

    public byte[] Palette(int number) => _palettes[number];

    public void ClearPage(int number) { var p = Page(number); Array.Clear(p, 0, p.Length); }

    /// <summary>Screen::fillRect - corners, both ends inclusive, as the original takes them.</summary>
    public void FillRect(int x1, int y1, int x2, int y2, byte color, int page = -1)
    {
        var dst = Page(page == -1 ? CurPage : page);
        for (int y = y1; y <= y2; y += 1)
        {
            if (y < 0 || y >= Height) continue;
            int row = y * Width;
            for (int x = x1; x <= x2; x += 1) if (x >= 0 && x < Width) dst[row + x] = color;
        }
    }

    /// <summary>
    /// Screen::copyWsaRect - an offscreen WSA frame blitted with clipping to a dim, through one of
    /// the plot functions: straight copy, the transparency lookup, skip-zero, or both.
    /// </summary>
    public void CopyWsaRect(int x, int y, int w, int h, int dimState, int plotFunc, byte[] src, int unk1, byte[] table1, byte[] table2)
    {
        _ = unk1;
        var dst = Page(CurPage);
        var dim = Dims[dimState];
        int dimX1 = dim.Sx << 3;
        int dimX2 = dimX1 + (dim.W << 3);
        int dimY1 = dim.Sy;
        int dimY2 = dimY1 + dim.H;
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
            int d = (y + row) * Width + x;
            for (int i = 0; i < w; i += 1, d += 1)
            {
                if (srcPos >= src.Length || d < 0 || d >= dst.Length) { srcPos += 1; continue; }
                int v = src[srcPos++];
                switch (plotFunc)
                {
                    case 0: dst[d] = (byte)v; break;
                    case 1:
                    {
                        int t = table1[v];
                        if (t != 0xff) v = table2[dst[d] + (t << 8)];
                        dst[d] = (byte)v;
                        break;
                    }
                    case 4: if (v != 0) dst[d] = (byte)v; break;
                    case 5:
                        if (v != 0)
                        {
                            int t = table1[v];
                            if (t != 0xff) v = table2[dst[d] + (t << 8)];
                            dst[d] = (byte)v;
                        }
                        break;
                }
            }
            srcPos += srcAdd;
        }
    }

    /// <summary>
    /// Screen::fadePalette. The engine walks the live palette towards the target one step at a
    /// time and waits between steps, so a fade costs *time* - and a script whose next instruction
    /// is due at a given moment cares about exactly how much. The walk is reproduced here for its
    /// duration and its end state; the host is told how long it took through the wait callback.
    /// </summary>
    public void FadePalette(byte[] target, int delay, Action<double> wait = null)
    {
        var live = ScreenPalette;
        int maxDiff = 0;
        for (int i = 0; i < 768 && i < target.Length; i += 1) maxDiff = Math.Max(maxDiff, Math.Abs(target[i] - live[i]));
        int delayInc = delay << 8;
        if (maxDiff != 0) delayInc = Math.Min(delayInc / maxDiff, 0x7fff);
        int step = delayInc;
        int diff;
        for (diff = 1; diff <= maxDiff; diff += 1)
        {
            if (delayInc >= 256) break;
            delayInc += step;
        }
        int delayAcc = 0;
        double waited = 0;
        while (true)
        {
            delayAcc += delayInc;
            bool refreshed = false;
            for (int i = 0; i < 768 && i < target.Length; i += 1)
            {
                int c1 = target[i];
                int c2 = live[i];
                if (c1 == c2) continue;
                refreshed = true;
                c2 = c1 > c2 ? Math.Min(c1, c2 + diff) : Math.Max(c1, c2 - diff);
                live[i] = (byte)c2;
            }
            if (!refreshed) break;
            waited += (delayAcc >> 8) * 1000.0 / 60.0;
            delayAcc &= 0xff;
        }
        if (waited > 0) wait?.Invoke(waited);
    }

    /// <summary>Screen::loadSpecialColors - the four entries the interface keeps for itself.</summary>
    public void LoadSpecialColors(byte[] dst) => Array.Copy(ScreenPalette, 192 * 3, dst, 192 * 3, 4 * 3);

    public void FadeToBlack(int delay, Action<double> wait = null)
    {
        FadePalette(new byte[768], delay, wait);
        FadeFlag = 2;
    }

    /// <summary>
    /// Screen::fadeClearSceneWindow: the view fades out and is left black - border column included,
    /// because the engine's fillRect takes its corners inclusively.
    /// </summary>
    /// <summary>fadeToPalette1: back up from black to the palette the level is lit by.</summary>
    public void FadeToPalette1(int delay, Action<double> wait = null)
    {
        LoadSpecialColors(Palette(1));
        FadePalette(Palette(1), delay, wait);
        FadeFlag = 0;
    }

    public void FadeClearSceneWindow(int delay = 10, Action<double> wait = null)
    {
        if (FadeFlag == 1) return;
        var target = (byte[])Palette(0).Clone();
        Array.Clear(target, 0, 128 * 3);
        LoadSpecialColors(target);
        FadePalette(target, delay, wait);
        FillRect(112, 0, 288, 120, 0);
        FadeFlag = 1;
    }

    /// <summary>Screen::fadeFlag - which fade the screen is in the middle of.</summary>
    public int FadeFlag;

    /// <summary>Screen::copyRegionToBuffer - a rectangle of a page, lifted out as its own block.</summary>
    public byte[] CopyRegionToBuffer(int pageNum, int x, int y, int w, int h)
    {
        var outBuf = new byte[w * h];
        var src = Page(pageNum);
        for (int row = 0; row < h; row += 1)
            Array.Copy(src, (y + row) * Width + x, outBuf, row * w, w);
        return outBuf;
    }

    /// <summary>Screen::drawClippedLine - horizontal or vertical only, which is all the game draws.</summary>
    public void DrawClippedLine(int x1, int y1, int x2, int y2, byte color)
    {
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);
        x1 = Math.Max(0, x1); y1 = Math.Max(0, y1);
        x2 = Math.Min(Width - 1, x2); y2 = Math.Min(Height - 1, y2);
        var dst = Page(CurPage);
        if (x1 == x2) { for (int y = y1; y <= y2; y += 1) dst[y * Width + x1] = color; }
        else { for (int x = x1; x <= x2; x += 1) dst[y1 * Width + x] = color; }
    }

    public void DrawBox(int x1, int y1, int x2, int y2, byte color)
    {
        DrawClippedLine(x1, y1, x2, y1, color);
        DrawClippedLine(x1, y1, x1, y2, color);
        DrawClippedLine(x2, y1, x2, y2, color);
        DrawClippedLine(x1, y2, x2, y2, color);
    }

    /// <summary>
    /// Screen_LoL::drawGridBox - every other pixel, in a checker that starts on the parity of
    /// (x + y). It is how the game greys out a portrait's weapon panel.
    /// </summary>
    public void DrawGridBox(int x, int y, int w, int h, byte col)
    {
        if (w <= 0 || x >= Width || h <= 0 || y >= Height) return;
        if (x < 0) { x = 0; w += x; if (w < 0) return; }
        if (x + w > Width) w = Width - x;
        if (y < 0) { y = 0; h += y; if (h < 0) return; }
        if (y + h > Height) h = Height - y;
        var dst = Page(CurPage);
        for (int row = 0; row < h; row += 1)
        {
            int start = (y + row) * Width + x;
            for (int i = ((y + row + x) & 1) != 0 ? 1 : 0; i < w; i += 2) dst[start + i] = col;
        }
    }

    /// <summary>The two overlays the interface fades shapes with (startup in lol.mjs).</summary>
    public readonly byte[] PaletteOverlay1 = new byte[256];
    public readonly byte[] PaletteOverlay2 = new byte[256];

    /// <summary>Screen_LoL::copyColor - one palette entry copied over another, which is how the
    /// message line fades its text.</summary>
    /// <summary>
    /// Screen_LoL::fadeColor: one palette entry walked towards another. True while it is still on
    /// the way, which is what the message text's fade counts on to know when it is done.
    /// </summary>
    public bool FadeColor(int dstColorIndex, int srcColorIndex, int elapsedTicks, int totalTicks)
    {
        int dst = dstColorIndex * 3;
        int src = srcColorIndex * 3;
        var p1 = Palette(1);
        bool more = false;
        var tmp = new byte[3];
        for (int i = 0; i < 3; i += 1)
        {
            int outV;
            if (elapsedTicks < totalTicks && totalTicks != 0)
            {
                int srcV = ScreenPalette[src + i] & 0x3f;
                int dstV = ScreenPalette[dst + i] & 0x3f;
                outV = srcV - dstV;
                if (outV != 0) more = true;
                outV = dstV + ((((outV << 8) / totalTicks) * elapsedTicks) >> 8);
            }
            else
            {
                p1[dst + i] = ScreenPalette[src + i];
                outV = ScreenPalette[src + i];
                more = false;
            }
            tmp[i] = (byte)(outV & 0xff);
        }
        Array.Copy(tmp, 0, ScreenPalette, dst, 3);
        return more;
    }

    public void CopyColor(int dstColorIndex, int srcColorIndex)
    {
        var pal = _palettes[0];
        Array.Copy(pal, srcColorIndex * 3, pal, dstColorIndex * 3, 3);
    }

    public void CopyPalette(int dst, int src) => Array.Copy(_palettes[src], _palettes[dst], _palettes[src].Length);

    /// <summary>Screen::copyRegion - a blit between pages, skipping index 0 unless noCheck.</summary>
    public void CopyRegion(int srcX, int srcY, int dstX, int dstY, int w, int h, int srcPage, int dstPage, bool noCheck = false)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        for (int y = 0; y < h; y += 1)
        {
            int sy = srcY + y, dy = dstY + y;
            if (sy < 0 || sy >= Height || dy < 0 || dy >= Height) continue;
            for (int x = 0; x < w; x += 1)
            {
                int sx = srcX + x, dx = dstX + x;
                if (sx < 0 || sx >= Width || dx < 0 || dx >= Width) continue;
                byte value = src[sy * Width + sx];
                if (!noCheck && value == 0) continue;
                dst[dy * Width + dx] = value;
            }
        }
    }

    public void CopyPage(int src, int dst) => Array.Copy(Page(src), Page(dst), Width * Height);

    /// <summary>
    /// crossFadeRegion: the destination takes the source a scattered pixel at a time, which is how
    /// the game dissolves one picture into another. The scatter is the engine's own shuffle.
    /// </summary>
    public void CrossFadeRegion(int x1, int y1, int x2, int y2, int w, int h, int srcPage, int dstPage, Func<int, int> random = null)
    {
        if (w <= 0 || h <= 0) return;
        var wB = new int[w];
        var hB = new int[h];
        for (int i = 0; i < w; i += 1) wB[i] = i;
        for (int i = 0; i < h; i += 1) hB[i] = i;
        random ??= n => 0;
        for (int i = 0; i < w; i += 1) { int j = random(w); (wB[i], wB[j]) = (wB[j], wB[i]); }
        for (int i = 0; i < h; i += 1) { int j = random(h); (hB[i], hB[j]) = (hB[j], hB[i]); }
        var src = Page(srcPage);
        var dst = Page(dstPage);
        for (int i = 0; i < h; i += 1)
        {
            int iH = i;
            for (int ii = 0; ii < w; ii += 1)
            {
                int sy = y1 + hB[iH], sx = x1 + wB[ii];
                int dy = y2 + hB[iH], dx = x2 + wB[ii];
                if (sy >= 0 && sy < Height && sx >= 0 && sx < Width && dy >= 0 && dy < Height && dx >= 0 && dx < Width)
                    dst[dy * Width + dx] = src[sy * Width + sx];
                if (++iH >= h) iH = 0;
            }
        }
    }

    /// <summary>Screen::loadBitmap - a CPS image straight onto a page, with its palette.</summary>
    public byte[] LoadBitmap(byte[] bytes, int pageNum, byte[] palette)
    {
        var bitmap = Cps.DecodeBitmapData(bytes);
        if (bitmap.Data.Length != Width * Height) throw new InvalidDataException("Bitmap is not 320x200");
        Array.Copy(bitmap.Data, Page(pageNum), Width * Height);
        if (palette != null && bitmap.Palette is { Length: 768 }) Array.Copy(bitmap.Palette, palette, 768);
        return bitmap.Palette;
    }

    public static int FindLeastDifferentColor(byte[] entry, int entryAt, byte[] pal, int firstColor, int numColors, bool skipSpecialColors)
    {
        int m = 0x7fff, r = 0x101;
        for (int i = 0; i < numColors; i += 1)
        {
            if (skipSpecialColors && i >= 0xc0 && i <= 0xc3) continue;
            int c = 0;
            for (int k = 0; k < 3; k += 1)
            {
                int v = entry[entryAt + k] - pal[(i + firstColor) * 3 + k];
                c += v * v;
            }
            if (c <= m) { m = c; r = i; }
        }
        return r;
    }

    /// <summary>Screen_LoL::generateGrayOverlay - the parchment map's washed-out palette.</summary>
    public static void GenerateGrayOverlay(byte[] srcPal, byte[] overlay, int factor, int addR, int addG, int addB, int lastColor, bool skipSpecialColors)
    {
        var tmp = new byte[lastColor * 3];
        int[] adds = { addR, addG, addB };
        for (int i = 0; i < lastColor; i += 1)
            for (int k = 0; k < 3; k += 1)
            {
                int v = ((srcPal[3 * i + k] & 0x3f) * factor) / 0x40 + adds[k];
                tmp[3 * i + k] = (byte)(v > 0x3f ? 0x3f : v & 0xff);
            }
        for (int i = 0; i < lastColor; i += 1) overlay[i] = (byte)FindLeastDifferentColor(tmp, 3 * i, srcPal, 0, lastColor, skipSpecialColors);
    }

    /// <summary>Screen_LoL::calcBounds - clip a rectangle to a screen dimension.</summary>
    private static (int Na, int Nb, int X, int Y, int W, int H)? CalcBounds(ScreenDim dim, int x, int y, int w, int h)
    {
        int iw = dim.W << 3;
        int ih = dim.H;
        int na = 0;
        if (x + w <= 0 || y + h <= 0 || x >= iw || y >= ih) return null;
        if (x < 0) { na = -x; w += x; x = 0; }
        if (x + w > iw) w = iw - x;
        int nb = 0;
        if (y < 0) { nb = -y; h += y; y = 0; }
        if (y + h > ih) h = ih - y;
        return (na, nb, x, y, w, h);
    }

    /// <summary>Screen_LoL::copyBlockAndApplyOverlay - the map draws itself with this.</summary>
    public void CopyBlockAndApplyOverlay(int page1, int x1, int y1, int page2, int x2, int y2, int w, int h, int dim, byte[] ovl)
    {
        if (w == 0 || h == 0 || ovl == null) return;
        var cdim = Dims[dim];
        var b = CalcBounds(cdim, x2, y2, w, h);
        if (b == null) return;
        var (na, nb, bx, by, bw, bh) = b.Value;
        var src = Page(page1);
        var dst = Page(page2);
        for (int i = 0; i < bh; i += 1)
        {
            int sRow = (y1 + nb + i) * Width + x1 + na;
            int dRow = (by + cdim.Sy + i) * Width + bx + (cdim.Sx << 3);
            for (int ii = 0; ii < bw; ii += 1)
            {
                byte pv = ovl[src[sRow + ii]];
                if (pv != 0) dst[dRow + ii] = pv;
            }
        }
    }

    /// <summary>Screen::copyBlockToPage - drops a raw w*h buffer onto a page.</summary>
    public void CopyBlockToPage(int page, int x, int y, int w, int h, byte[] block)
    {
        var dst = Page(page);
        for (int row = 0; row < h; row += 1)
        {
            int ty = y + row;
            if (ty < 0 || ty >= Height) continue;
            for (int col = 0; col < w; col += 1)
            {
                int tx = x + col;
                if (tx >= 0 && tx < Width) dst[ty * Width + tx] = block[row * w + col];
            }
        }
    }

    /// <summary>
    /// Screen_v2::generateOverlay (the LoL 256-colour variant): every colour is blended toward
    /// `opColor` by `weight`, then replaced by the nearest colour that already exists in the
    /// palette. Straight transliteration - the search order decides ties, so it must not be
    /// "improved".
    /// </summary>
    public static byte[] GenerateOverlay(byte[] pal, byte[] buffer, int opColor, int weight, int maxColor = 127)
    {
        weight = Math.Min(weight, 255) >> 1;
        var op = new[] { pal[opColor * 3], pal[opColor * 3 + 1], pal[opColor * 3 + 2] };
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

    /// <summary>
    /// The eight distance overlays a level uses, built exactly as scene.mjs loadLevelGraphics does:
    /// seven fades toward the level's special colour plus an identity table, with the 128..255 range
    /// left alone (those are the interface colours) and 255 treated as transparent.
    /// </summary>
    public void BuildLevelOverlays(byte[] palette0, int specialColor, int specialColorWeight)
    {
        for (int i = 0; i < 7; i += 1)
        {
            int w = 100 - i * specialColorWeight;
            w = w > 0 ? (w * 255) / 100 : 0;
            var overlay = LevelOverlays[i];
            GenerateOverlay(palette0, overlay, specialColor, w);
            for (int ii = 0; ii < 128; ii += 1) if (overlay[ii] == 255) overlay[ii] = 0;
            for (int ii = 128; ii < 256; ii += 1) overlay[ii] = (byte)ii;
        }
        for (int i = 0; i < 256; i += 1) LevelOverlays[7][i] = (byte)i;
    }

    /// <summary>LEVEL&lt;nn&gt;.TLC: 256 bytes of "which blend page", then 20 pages of 256 blends.</summary>
    public void LoadTransparencyTables(byte[] tlc)
    {
        Array.Copy(tlc, 0, TransparencyTable1, 0, 256);
        Array.Copy(tlc, 256, TransparencyTable2, 0, Math.Min(5120, tlc.Length - 256));
    }

    /// <summary>
    /// Screen::applyOverlaySpecial: the source page's non-zero pixels do not replace the destination,
    /// they push what is already there through an overlay table. The heal glow is drawn with this.
    /// </summary>
    public void ApplyOverlaySpecial(int page1, int x1, int y1, int page2, int x2, int y2, int w, int h,
                                    int dim, int flag, byte[] ovl)
    {
        if (w == 0 || h == 0 || ovl == null) return;
        var cdim = Dims[dim];
        var bounds = CalcBounds(cdim, x2, y2, w, h);
        if (bounds == null) return;
        var b = bounds.Value;
        var src = Page(page1);
        var dst = Page(page2);
        for (int i = 0; i < b.H; i += 1)
        {
            int s = (y1 + b.Nb + i) * Width + x1 + b.Na;
            int d = (b.Y + cdim.Sy + i) * Width + b.X + (cdim.Sx << 3);
            if (flag != 0) d += i >> 1;
            for (int ii = 0; ii < b.W; ii += 1) if (src[s + ii] != 0) dst[d + ii] = ovl[dst[d + ii]];
        }
    }

    /// <summary>Expands a 6-bit VGA palette to 8-bit RGB, the way the host does when presenting.</summary>
    public static byte[] ToRgb888(byte[] palette6)
    {
        var rgb = new byte[768];
        for (int i = 0; i < 768; i += 1) rgb[i] = (byte)(palette6[i] * 255 / 63);
        return rgb;
    }
}

/// <summary>The optional tables a drawShape call carries (screen.mjs drawShape `opts`).</summary>
public sealed class ShapeDrawOptions
{
    public byte[] FadeTable;
    public int FadeLevel;
    public byte[] ColorTable;
    public int ScaleW = 0x100;
    public int ScaleH = 0x100;
    public byte[] BackgroundFade;
}
