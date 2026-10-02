// The screen effects a level script can ask for: the view shaking, the palette flashing, one
// picture dissolving into another.
//
// Transliterated from src/game/scene.mjs (shakeScene), src/game/spells.mjs
// (generateFlashPalette) and the paletteFlash opcode in src/game/script.mjs. They run on the
// engine's clock like everything else here, so a host stepping the clock sees them play out.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>generateFlashPalette: the colours pushed toward or away from white.</summary>
    public static void GenerateFlashPalette(byte[] src, byte[] dst, int colorFlags)
    {
        Array.Copy(src, 0, dst, 0, 6);
        for (int i = 2; i < 128; i += 1)
        {
            for (int ii = 0; ii < 3; ii += 1)
            {
                int t = src[i * 3 + ii] & 0x3f;
                if ((colorFlags & (1 << ii)) != 0) t += (0x3f - t) >> 1;
                else t -= t >> 1;
                dst[i * 3 + ii] = (byte)t;
            }
        }
        Array.Copy(src, 128 * 3, dst, 128 * 3, src.Length - 128 * 3);
    }

    /// <summary>paletteFlash: two ticks of a changed palette, then back.</summary>
    public void PaletteFlash(int colorFlags, Action<double> wait)
    {
        var p1 = _screen.Palette(1);
        var p2 = _screen.Palette(3);
        GenerateFlashPalette(p1, p2, colorFlags);
        _screen.LoadSpecialColors(p1);
        _screen.LoadSpecialColors(p2);
        _screen.SetScreenPalette(p2);
        wait?.Invoke(2 * LevelLoader.TickLength);
        _screen.SetScreenPalette(p1);
    }

    /// <summary>shakeScene: the view jitters about its own copy for a while.</summary>
    public void ShakeScene(int duration, int width, int height, bool restore, Action<double> wait)
    {
        _screen.CopyRegion(112, 0, 112, 0, 176, 120, 0, 6, true);
        for (int step = 0; step < duration; step += 2)
        {
            int s1 = width != 0 ? PresentationRandom(255) % (width << 1) - width : 0;
            int s2 = height != 0 ? PresentationRandom(255) % (height << 1) - height : 0;
            (int x1, int x2, int w) = s1 >= 0 ? (112, 112 + s1, 176 - s1) : (112 - s1, 112, 176 + s1);
            (int y1, int y2, int h) = s2 >= 0 ? (0, s2, 120 - s2) : (-s2, 0, 120 + s2);
            _screen.CopyRegion(x1, y1, x2, y2, w, h, 6, 0, true);
            wait?.Invoke(2 * LevelLoader.TickLength);
        }
        if (restore)
        {
            _screen.CopyRegion(112, 0, 112, 0, 176, 120, 6, 0, true);
            UpdateDrawPage2();
        }
    }
}
