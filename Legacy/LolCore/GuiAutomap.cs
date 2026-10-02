// The magic atlas as a screen: opening it, the party's cursor on the parchment, and closing it.
//
// Transliterated from src/game/automap.mjs (displayAutomap, redrawMapCursor) and the part of
// gui.mjs that opens it (clickedAutomap).
//
// The drawing itself lives in Automap.cs and is checked by its own harness; what is here is the
// screen mode around it - the fonts and palette it borrows, the level state it puts back
// afterwards, and the cursor that blinks where the party is standing.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>Set while the atlas is on screen instead of the playfield.</summary>
    public bool AutomapActive;

    /// <summary>The atlas, when the host has built one.</summary>
    public Automap Automap;

    /// <summary>PARCH.CPS, the two narrow fonts the atlas is drawn with, and the normal pair.</summary>
    public byte[] ParchFile, Font9N, Font6N, Font9, Font6;

    private readonly byte[] _mapCursorOverlay = StaticData.Table("MapCursorOvl").Select(v => (byte)v).ToArray();
    private byte[] _wllAutomapBackup;

    /// <summary>clickedAutomap: the atlas opens if the party has it.</summary>
    public int OpenAutomap()
    {
        if ((_loader.Flags[31] & 0x10) == 0) return 0;
        if (Automap == null || ParchFile == null) return 0;
        DisplayAutomap();
        return 1;
    }

    /// <summary>displayAutomap, up to the point where the engine starts waiting for input.</summary>
    public void DisplayAutomap()
    {
        AutomapActive = true;
        ToggleButtonDisplayMode(78, 1);
        Automap.CurrentMapLevel = _loader.Level;
        // The strings the parchment is labelled with are whatever the level is using now.
        Automap.LandsFile = LandsFile;
        Automap.LevelLangFile = LevelLangFile;
        _wllAutomapBackup = (byte[])_loader.Walls.Automap.Clone();

        var palette3 = _screen.Palette(3);
        _screen.LoadBitmap(ParchFile, 2, palette3);
        Screen.GenerateGrayOverlay(palette3, Automap.MapOverlay, 52, 0, 0, 0, 256, false);
        // The atlas borrows the narrow fonts under the same names, exactly as the engine does, and
        // the normal pair is put back when it closes.
        if (Font9N != null) _screen.LoadFont("9", Font9N);
        if (Font6N != null) _screen.LoadFont("6", Font6N);
        foreach (var entry in Automap.DefaultLegend) entry.Enable = false;

        _loader.PauseSysTimers(true);
        _loader.GenerateTempData();
        Automap.LoadMapLegendData(LegendFileFor(Automap.CurrentMapLevel));
        _screen.FadeToBlack(10, _loader.AdvanceClock);
        Automap.DrawMapPage(2, ParchFile, palette3);
        _screen.CopyPage(2, 0);
        _screen.FadePalette(palette3, 10, _loader.AdvanceClock);
        RedrawMapCursor();
    }

    /// <summary>Where the level's own legend entries come from.</summary>
    public Func<int, byte[]> LegendFile;

    private byte[] LegendFileFor(int level) => LegendFile?.Invoke(level);

    /// <summary>
    /// redrawMapCursor: the party's arrow, drawn through a rolling overlay so it pulses. The
    /// overlay is rotated by one entry each time, which is the whole animation.
    /// </summary>
    public void RedrawMapCursor()
    {
        if (Automap == null) return;
        int sx = Automap.MapGetStartPosX();
        int sy = Automap.MapGetStartPosY();
        if (_loader.Level != Automap.CurrentMapLevel) return;
        int cx = Automap.TopLeftX + ((_loader.Party.Block - sx) % 32) * 7;
        int cy = Automap.TopLeftY + (_loader.Party.Block - (sy << 5)) / 32 * 6;
        _screen.FillRect(0, 0, 16, 16, 0, 2);
        int shape = 48 + _loader.Party.Direction;
        if (shape < Automap.Shapes.Length) _screen.DrawShape(2, Automap.Shapes[shape], 0, 0, 0, 0);
        _screen.CopyRegion(cx, cy, cx, cy, 16, 16, 2, 0);
        _screen.CopyBlockAndApplyOverlay(2, 0, 0, 0, cx - 3, cy - 2, 16, 16, 0, _mapCursorOverlay);
        _mapCursorOverlay[24] = _mapCursorOverlay[1];
        for (int i = 1; i < 24; i += 1) _mapCursorOverlay[i] = _mapCursorOverlay[i + 1];
    }

    /// <summary>The atlas closes: the level's own wall data and state come back.</summary>
    public void CloseAutomap()
    {
        if (!AutomapActive) return;
        AutomapActive = false;
        if (Font9 != null) _screen.LoadFont("9", Font9);
        if (Font6 != null) _screen.LoadFont("6", Font6);
        _screen.SetFont("9");
        if (_wllAutomapBackup != null) Array.Copy(_wllAutomapBackup, _loader.Walls.Automap, _wllAutomapBackup.Length);
        _screen.FadeToBlack(10, _loader.AdvanceClock);
        _loader.PauseSysTimers(false);
        EnableDefaultPlayfieldButtons();
        DrawPlayField();
        _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, _loader.LampEffect);
    }
}
