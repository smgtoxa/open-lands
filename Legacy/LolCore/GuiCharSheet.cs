// The character sheet: the screen a portrait click opens, with the party member's kit on it.
//
// Transliterated from src/game/gui.mjs (gui_displayCharInventory, gui_printCharInventoryStats,
// gui_printCharacterStats, gui_drawCharInventoryItem, gui_drawHorizontalBarGraph,
// gui_enableCharInventoryButtons, gui_initCharInventorySpecialButtons, clickedPortraitLeft,
// clickedExitCharInventory) and src/game/party.mjs (calculateCharacterStats).
//
// The sheet is drawn onto the same pages as the playfield: the engine keeps the playfield in a
// buffer, draws the sheet over it, and puts the buffer back when the sheet is closed.
namespace LolCore;

public sealed partial class Gui
{
    private static readonly int[] InventoryTypes = { 0, 1, 2, 6, 3, 1, 1, 3, 5, 4 };
    private static readonly int[] SlotShapes = { 0x30, 0x34, 0x30, 0x34, 0x2e, 0x2f, 0x32, 0x33, 0x31, 0x35, 0x35 };

    /// <summary>Whose sheet is on screen, by absolute character id, so a redraw can reuse the page.</summary>
    private int _lastCharInventory = -1;

    /// <summary>The playfield, kept while the sheet covers it.</summary>
    private byte[] _pageBuffer1, _pageBuffer2;

    /// <summary>Set while the party may not act: the sheet is up.</summary>
    public bool WeaponsDisabledBySheet;

    /// <summary>INVENT&lt;n&gt;.CPS by inventory type - the sheet's own background.</summary>
    public Func<string, byte[]> LoadBitmapFile;

    public readonly int[] CharStatusFlags = new int[3];

    /// <summary>calculateCharacterStats: might, protection, and the three skills.</summary>
    public int CalculateCharacterStats(int charNum, int index)
    {
        var ch = Characters[charNum];
        if (index == 0)
        {
            int c = 0;
            for (int i = 0; i < 8; i += 1) c += ch.ItemsMight[i];
            if (c != 0) c += ch.Might;
            else c = ch.DefaultModifiers[8];
            c = (c * ch.DefaultModifiers[1]) >> 8;
            c = (c * ch.TotalMightModifier) >> 8;
            return c;
        }
        if (index == 1) return _loader.Board.CalculateProtection(charNum);
        if (index > 4) return -1;
        index -= 2;
        return ch.SkillLevels[index] + ch.SkillModifiers[index];
    }

    /// <summary>clickedPortraitLeft: the sheet opens over the playfield, which is kept to put back.</summary>
    public int OpenCharSheet(int charNum)
    {
        _loader.PauseSysTimers(true);
        if (!WeaponsDisabled)
        {
            _pageBuffer2 = _screen.CopyRegionToBuffer(2, 0, 0, 320, 200);
            _screen.CopyPage(0, 2);
            _pageBuffer1 = _screen.CopyRegionToBuffer(2, 0, 0, 320, 200);
            _loader.UpdateFlags |= 0x0c;
            DisableControls(1);
        }
        SelectedCharacter = charNum;
        WeaponsDisabled = true;
        DisplayCharInventory(charNum);
        EnableCharInventoryButtons(charNum);
        return 1;
    }

    /// <summary>clickedExitCharInventory: the playfield comes back exactly as it was.</summary>
    public int CloseCharSheet()
    {
        _loader.UpdateFlags &= 0xfff3;
        EnableDefaultPlayfieldButtons();
        WeaponsDisabled = false;
        // A sheet that was looked at clears the "this skill went up" highlight on that character.
        for (int i = 0; i < 4; i += 1) if ((_charInventoryUnk & (1 << i)) != 0) Characters[i].Flags &= unchecked((int)0xfffff1ff);
        if (_pageBuffer1 != null) _screen.CopyBlockToPage(2, 0, 0, 320, 200, _pageBuffer1);
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        DrawAllCharPortraitsWithStats();
        DrawInventory();
        _screen.CurPage = cp;
        _screen.CopyPage(2, 0);
        EnableControls();
        if (_pageBuffer2 != null) _screen.CopyBlockToPage(2, 0, 0, 320, 200, _pageBuffer2);
        _lastCharInventory = -1;
        UpdateDrawPage2();
        _loader.PauseSysTimers(false);
        return 1;
    }

    /// <summary>Which characters' sheets have been opened since the last close.</summary>
    private int _charInventoryUnk;

    /// <summary>gui_displayCharInventory: the sheet itself.</summary>
    public void DisplayCharInventory(int charNum)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        var l = Characters[charNum];
        int id = Math.Abs(l.Id);
        if (id != _lastCharInventory)
        {
            var bitmap = LoadBitmapFile?.Invoke($"INVENT{InventoryTypes[id]}.CPS");
            if (bitmap != null) _screen.LoadBitmap(bitmap, 2, null);
            _screen.CopyRegion(0, 0, 112, 0, 208, 120, 2, 6);
        }
        else _screen.CopyRegion(112, 0, 0, 0, 208, 120, 6, 2);
        _screen.CopyRegion(80, 143, 80, 143, 232, 35, 0, 2);
        DrawAllCharPortraitsWithStats();
        _screen.PrintString(l.Name, 157, 9, 254, 0, 5);
        PrintCharInventoryStats(charNum);
        for (int i = 0; i < 11; i += 1) DrawCharInventoryItem(i);
        string of = _screen.SetFont("9");
        _screen.PrintString(LangString(0x4033), 182, 103, 172, 0, 5);
        _screen.SetFont(of);

        // The status icons: poisoned, paralysed, and the rest, in the order the engine lists them.
        int[] statusFlags = { 0x0080, 0x0000, 0x1000, 0x0002, 0x0100, 0x0001, 0x0000, 0x0000 };
        for (int i = 0; i < 3; i += 1) CharStatusFlags[i] = 0xff;
        int x = 0, c = 0;
        for (int i = 0; i < 3; i += 1)
        {
            if ((l.Flags & statusFlags[i << 1]) == 0) continue;
            var shp = GameShapes[statusFlags[(i << 1) + 1]];
            _screen.DrawShape(_screen.CurPage, shp, 108 + x, 98, 0, 0);
            x += shp.Width + 2;
            CharStatusFlags[c] = statusFlags[(i << 1) + 1];
            c += 1;
        }

        // The three experience bars, scaled down until the numbers fit in 15 bits.
        var expRequirements = StaticData.Table("ExpRequirements");
        for (int i = 0; i < 3; i += 1)
        {
            int b = l.ExperiencePts[i] - expRequirements[l.SkillLevels[i] - 1];
            int e = expRequirements[l.SkillLevels[i]] - expRequirements[l.SkillLevels[i] - 1];
            while ((e & unchecked((int)0xffff8000)) != 0)
            {
                e >>= 1;
                int cc = b;
                b >>= 1;
                if (cc != 0 && b == 0) b = 1;
            }
            DrawHorizontalBarGraph(154, 64 + i * 10, 34, 5, b, e, 132, 0);
        }

        _screen.DrawClippedLine(14, 120, 194, 120, 1);
        _screen.CopyRegion(0, 0, 112, 0, 208, 121, 2, 0);
        _screen.CopyRegion(80, 143, 80, 143, 232, 35, 2, 0);
        _screen.CurPage = cp;
        _lastCharInventory = id;
    }

    public void PrintCharInventoryStats(int charNum)
    {
        for (int i = 0; i < 5; i += 1) PrintCharacterStats(i, true, CalculateCharacterStats(charNum, i));
        _charInventoryUnk |= 1 << charNum;
    }

    /// <summary>gui_printCharacterStats: one line of the sheet, label and number.</summary>
    public void PrintCharacterStats(int index, bool redraw, int value)
    {
        int offs = _screen.CurPage != 0 ? 0 : 112;
        int y;
        byte col;
        if (index < 2)
        {
            y = index * 10 + 22;
            col = 158;
            if (redraw) _screen.PrintString(LangString(0x4014 + index), offs + 108, y, col, 0, 4);
        }
        else
        {
            int s = index - 2;
            y = s * 10 + 62;
            col = (byte)((Characters[SelectedCharacter].Flags & (0x200 << s)) != 0 ? 254 : 180);
            if (redraw) _screen.PrintString(LangString(0x4014 + index), offs + 108, y, col, 0, 4);
        }
        if (offs != 0) _screen.CopyRegion(294, y, 182 + offs, y, 18, 8, 6, _screen.CurPage, true);
        _screen.PrintString(value.ToString(), 200 + offs, y, col, 0, 6);
    }

    /// <summary>gui_drawCharInventoryItem: a slot, and whatever is in it.</summary>
    public void DrawCharInventoryItem(int itemIndex)
    {
        var defs = StaticData.Table("CharInvDefs");
        var index = StaticData.Table("CharInvIndex");
        int at = index[Characters[SelectedCharacter].RaceClassSex] * 22 + itemIndex * 2;
        int x = defs[at];
        int y = defs[at + 1];
        if (y == 0xff) return;
        if (_screen.CurPage == 0) x += 112;
        int i = Characters[SelectedCharacter].Items[itemIndex];
        int shapeNum = i != 0 ? (itemIndex < 9 ? 4 : 5) : SlotShapes[itemIndex];
        _screen.DrawShape(_screen.CurPage, GameShapes[shapeNum], x, y, 0, 0);
        if (itemIndex > 8) { x -= 5; y -= 5; }
        if (i != 0)
        {
            var icon = ItemIconShape(i);
            if (icon != null) _screen.DrawShape(_screen.CurPage, icon, x + 1, y + 1, 0, 0);
        }
    }

    /// <summary>gui_drawHorizontalBarGraph: the experience bars.</summary>
    public void DrawHorizontalBarGraph(int x, int y, int w, int h, int cur, int max, int col1, int col2)
    {
        if (max < 1) return;
        if (cur < 0) cur = 0;
        int e = Math.Min(cur, max);
        w -= 1;
        h -= 1;
        if (w == 0 || h == 0) return;
        int t = e * w / max;
        if (t == 0 && e != 0) t += 1;
        if (t != 0) _screen.FillRect(x, y, x + t - 1, y + h, (byte)col1);
        if (t < w && col2 != 0) _screen.FillRect(x + t, y, x + w - 1, y + h, (byte)col2);
    }

    /// <summary>gui_enableCharInventoryButtons: the sheet's own buttons.</summary>
    public void EnableCharInventoryButtons(int charNum)
    {
        ResetButtonList();
        InitButtonsFromList("ButtonList2");
        InitCharInventorySpecialButtons(charNum);
        SetFaceFramesControlButtons(21, 0);
    }

    public void InitCharInventorySpecialButtons(int charNum)
    {
        var defs = StaticData.Table("CharInvDefs");
        var index = StaticData.Table("CharInvIndex");
        int at = index[Characters[charNum].RaceClassSex] * 22;
        for (int i = 0; i < 11; i += 1)
        {
            int x = defs[at + i * 2];
            int y = defs[at + i * 2 + 1];
            if (x != 0xff) InitButton(33 + i, x, y, i);
        }
    }
}
