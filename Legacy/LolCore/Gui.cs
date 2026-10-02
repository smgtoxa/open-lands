// The playfield: everything on the screen that is not the 3D view.
//
// Transliterated from src/game/gui.mjs (gui_drawPlayField, gui_drawInventory, gui_drawScroll,
// gui_drawAllCharPortraitsWithStats, gui_drawCharPortraitWithStats, gui_drawCharFaceShape,
// gui_drawLiveMagicBar, calcCharPortraitXpos, gui_drawMoneyBox, gui_drawCompass, gui_drawBox).
//
// The game draws the whole interface into page 2 and copies it to the screen in one go, so a
// half-drawn frame is never shown; this does the same, because the copy is what page 0 ends up as.
namespace LolCore;

public sealed partial class Gui
{
    private static readonly int[] InventoryX = { 0x6a, 0x7f, 0x94, 0xa9, 0xbe, 0xd3, 0xe8, 0xfd, 0x112 };
    private static readonly int[] MoneyX = { 0x128, 0x134, 0x12b, 0x131, 0x12e };
    private static readonly int[] MoneyY = { 0x73, 0x73, 0x74, 0x74, 0x75 };
    private static readonly byte[] MoneyCols = { 0xd2, 0xd1, 0xd0, 0xd1, 0xd2 };

    private readonly Screen _screen;
    private readonly LevelLoader _loader;
    private readonly SceneShapes _scene;
    private readonly SceneComposer _composer;

    public Gui(LevelLoader loader, SceneComposer composer, SceneShapes scene)
    {
        _loader = loader;
        _composer = composer;
        _scene = scene;
        _screen = loader.Screen;
    }

    /// <summary>PLAYFLD.CPS, the background every other part is drawn onto.</summary>
    public byte[] PlayField;
    public Shape[] GameShapes = Array.Empty<Shape>();
    public Shape[] ItemIconShapes = Array.Empty<Shape>();
    /// <summary>FACE&lt;id&gt;.SHP per party member: 7 faces, then the same 7 again for a wounded one.</summary>
    public readonly Shape[][] FaceShapes = { Array.Empty<Shape>(), Array.Empty<Shape>(), Array.Empty<Shape>(), Array.Empty<Shape>() };
    public byte[] LandsFile;

    /// <summary>
    /// The strings the level is using. A conversation loads its own file part-way through a level
    /// (the loadLangFile opcode), so whatever the loader last loaded wins over what the host set up.
    /// </summary>
    public byte[] LevelLangFile
    {
        get => _loader.LevelLangFile ?? _levelLangFile;
        set => _levelLangFile = value;
    }

    private byte[] _levelLangFile;

    public int SelectedCharacter;
    public int SelectedSpell;
    public bool WeaponsDisabled;
    public int CurrentControlMode;
    public int Lang;
    public int CompassDirection = -1;
    private int _compassDirectionIndex = -1;
    public readonly int[] ActiveCharsXpos = new int[4];

    private int[] AvailableSpells => _loader.AvailableSpells;
    private Character[] Characters => _loader.Characters;
    private ItemBoard Items => _loader.Items;

    public int CountActiveCharacters()
    {
        int n = 0;
        foreach (var c in Characters) if (c.Active) n += 1;
        return n;
    }

    public string LangString(int id) => GameStrings.Get(id, LandsFile, LevelLangFile) ?? "";

    /// <summary>gui_drawPlayField: the whole interface, built on page 2 and shown in one copy.</summary>
    /// <summary>PLAYFLD.CPS, already decompressed. Decoding it per frame cost more than the scene.</summary>
    private byte[] _playFieldPixels;

    public void DrawPlayField()
    {
        if (_playFieldPixels == null)
        {
            _screen.LoadBitmap(PlayField, 2, null);
            _playFieldPixels = (byte[])_screen.Page(2).Clone();
        }
        else Array.Copy(_playFieldPixels, _screen.Page(2), _playFieldPixels.Length);
        if ((_loader.Flags[31] & 0x40) != 0)
        {
            _screen.CopyRegion(112, 32, 288, 0, 32, 32, 2, 2, true);
            CompassDirection = -1;
        }
        if ((_loader.Flags[31] & 0x10) != 0 && GameShapes.Length > 78)
            _screen.DrawShape(2, GameShapes[78], 290, 32, 0, 0);
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        if ((_loader.Flags[31] & 0x20) != 0) DrawScroll();
        else SelectedSpell = 0;
        if ((_loader.Flags[31] & 0x08) != 0) ResetLampStatus();
        UpdateDrawPage2();
        DrawScene(2);
        DrawAllCharPortraitsWithStats();
        DrawInventory();
        DrawMoneyBox(_screen.CurPage);
        _screen.CurPage = cp;
        _screen.CopyPage(2, 0);
        UpdateDrawPage2();
    }

    /// <summary>updateDrawPage2: the view window is kept on its own page for the scroll effects.</summary>
    public void UpdateDrawPage2() => _screen.CopyRegion(112, 0, 112, 0, 176, 120, 0, SceneDrawPage2, true);

    /// <summary>gui_drawScene: the 3D view, unless a box is up over it.</summary>
    public void DrawScene(int pageNum)
    {
        if ((_loader.UpdateFlags & 1) != 0 || WeaponsDisabled || !PartyAwake) return;
        // drawScene: which of the two pages is "the view" changes as the picture is asked for, and
        // the smooth scrolls slide one against the other. Page 0 means "and put it on the screen".
        if (pageNum != 0 && pageNum != SceneDrawPage1)
        {
            (SceneDrawPage1, SceneDrawPage2) = (SceneDrawPage2, SceneDrawPage1);
            UpdateDrawPage2();
        }
        _scene.DrawPass += 1;
        _composer.GenerateBlockDrawingBuffer(_loader.Party.Block, _loader.Party.Direction);
        var window = _composer.DrawVcnBlocks();
        int page = _scene.ScenePage;
        _scene.ScenePage = SceneDrawPage1;
        _screen.CopyBlockToPage(SceneDrawPage1, SceneShapes.SceneXOffset, 0, SceneComposer.SceneWidth, SceneComposer.SceneHeight, window);
        _scene.Draw(_loader.Party.Direction, _loader.Party.PosX, _loader.Party.PosY);
        _scene.ScenePage = page;
        if (pageNum == 0)
        {
            DrawSpecialGuiShape(SceneDrawPage1);
            _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage1, SceneDrawPage2, true);
            _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage1, 0, true);
            (SceneDrawPage1, SceneDrawPage2) = (SceneDrawPage2, SceneDrawPage1);
        }
        _loader.Board.UpdateEnvironmentalSfx(0);
        _loader.Board.SceneUpdateRequired = false;
        DrawCompass();
    }

    public void DrawInventory()
    {
        for (int i = 0; i < 9; i += 1) DrawInventoryItem(i);
    }

    public void DrawInventoryItem(int index)
    {
        int x = InventoryX[index];
        int item = Items.InventoryCurItem + index;
        if (item >= Items.Inventory.Length) item -= Items.Inventory.Length;
        int flag = (item & 1) != 0 ? 0 : 1;
        _screen.DrawShape(_screen.CurPage, GameShapes[4], x, 179, 0, flag);
        if (Items.Inventory[item] != 0)
        {
            var icon = ItemIconShape(Items.Inventory[item]);
            if (icon != null) _screen.DrawShape(_screen.CurPage, icon, x + 1, 180, 0, 0);
        }
    }

    /// <summary>getItemIconShapePtr: an item's icon, with the frame a multi-frame item is showing.</summary>
    public Shape ItemIconShape(int index)
    {
        int prop = Items.InPlay[index] != null ? Items.InPlay[index].ItemPropertyIndex : 0;
        if (prop >= Items.Properties.Length) return null;
        int ix = Items.Properties[prop].ShpIndex;
        if ((Items.Properties[prop].Flags & 0x200) != 0) ix += (Items.InPlay[index].ShpCurFrameFlg & 0x1fff) - 1;
        return ix >= 0 && ix < ItemIconShapes.Length ? ItemIconShapes[ix] : null;
    }

    /// <summary>gui_drawScroll: the spell list, when the party has the scroll.</summary>
    public void DrawScroll()
    {
        _screen.CopyRegion(112, 0, 12, 0, 87, 15, 2, 2, true);
        string of = _screen.SetFont("9");
        int h = 0;
        for (int i = 0; i < LevelLoader.SpellSlots; i += 1) if (AvailableSpells[i] != -1) h += 9;
        if (h == 18) h = 27;
        if (h != 0)
        {
            _screen.CopyRegion(201, 1, 17, 15, 6, h, 2, 2, true);
            _screen.CopyRegion(208, 1, 89, 15, 6, h, 2, 2, true);
            _screen.FillRect(21, 15, 89, h + 15, 206);
        }
        _screen.CopyRegion(112, 16, 12, h + 15, 87, 14, 2, 2, true);
        int y = 15;
        for (int i = 0; i < LevelLoader.SpellSlots; i += 1)
        {
            if (AvailableSpells[i] == -1) continue;
            byte col = (byte)(i == SelectedSpell ? 132 : 1);
            _screen.PrintString(SpellNameOf(AvailableSpells[i]), 24, y, col, 0, 0);
            y += 9;
        }
        _screen.SetFont(of);
    }

    /// <summary>
    /// spellName: the added spells by their own names, the game's own out of the language file, and a
    /// plain "Spell N" for one this build has never heard of - a save may well know one. There is only
    /// one of these on purpose: the port had a raw one beside it, and every drawing site called that.
    /// </summary>
    public string SpellNameOf(int spell)
    {
        var extra = ExtraSpellAt(spell);
        if (extra != null) return extra.Name;
        int code = SpellTable.Field(spell, 0);
        string name = code != 0 ? LangString(code) : null;
        return string.IsNullOrEmpty(name) ? $"Spell {spell}" : name;
    }

    public void DrawAllCharPortraitsWithStats()
    {
        CalcCharPortraitXpos();
        int numChars = CountActiveCharacters();
        for (int i = 0; i < numChars; i += 1) DrawCharPortraitWithStats(i);
    }

    /// <summary>
    /// gui_drawCharPortraitWithStats: the face, the two bars beside it, the weapon panel and the
    /// numbers a fight writes over it. Built on page 6 and copied into place, frame and all.
    /// </summary>
    public void DrawCharPortraitWithStats(int charNum)
    {
        var c = Characters[charNum];
        if (!c.Active || (_loader.UpdateFlags & 2) != 0) return;
        string tmpFid = _screen.SetFont("6");
        int cp = _screen.CurPage;
        _screen.CurPage = 6;
        DrawBox(0, 0, 66, 34, 1, 1, -1);
        DrawCharFaceShape(charNum, 0, 1, _screen.CurPage);
        DrawLiveMagicBar(33, 32, c.MagicPointsCur, 0, c.MagicPointsMax, 5, 32, 162, 1, 0);
        DrawLiveMagicBar(39, 32, c.HitPointsCur, 0, c.HitPointsMax, 5, 32, 154, 1, 1);
        _screen.PrintText(LangString(0x4253), 33, 1, 160, 0);
        _screen.PrintText(LangString(0x4254), 39, 1, 152, 0);
        int spellLevels = 0;
        if (AvailableSpells[SelectedSpell] != -1)
        {
            int spell = AvailableSpells[SelectedSpell];
            for (int i = 0; i < 4; i += 1)
                if (SpellTable.Field(spell, 1 + i) <= c.MagicPointsCur
                    && SpellTable.Field(spell, 5 + i) <= c.HitPointsCur) spellLevels += 1;
        }
        if ((c.Flags & 0x10) != 0)
        {
            _screen.DrawShape(_screen.CurPage, GameShapes[73], 44, 0, 0, 0);
            if (spellLevels < 4) _screen.DrawGridBox(44, (spellLevels << 3) + 1, 22, 32 - (spellLevels << 3), 1);
        }
        else
        {
            int handIndex = 0;
            if (c.Items[0] != 0)
            {
                int prop = Items.InPlay[c.Items[0]].ItemPropertyIndex;
                if (Items.Properties[prop].Might != -1) handIndex = prop;
            }
            var map = StaticData.Table("GameShapeMap");
            handIndex = map[(Items.Properties[handIndex].ShpIndex << 1) + 1];
            if (handIndex == map[1])
            {
                handIndex = c.RaceClassSex - 1;
                if (handIndex < 0) handIndex = 0;
                handIndex += 68;
            }
            _screen.DrawShape(_screen.CurPage, GameShapes[handIndex], 44, 0, 0, 0);
            _screen.DrawShape(_screen.CurPage, GameShapes[72 + c.Field41], 44, 17, 0, 0);
            if (spellLevels == 0) _screen.DrawGridBox(44, 17, 22, 16, 1);
        }
        int f = c.Flags & 0x314c;
        if ((f == 0 && WeaponsDisabled) || (f != 0 && (f != 4 || c.WeaponHit == 0 || (c.WeaponHit != 0 && WeaponsDisabled))))
            _screen.DrawGridBox(44, 0, 22, 34, 1);
        if (c.WeaponHit != 0)
        {
            _screen.DrawShape(_screen.CurPage, GameShapes[34], 44, 0, 0, 0);
            _screen.PrintString(c.WeaponHit.ToString(), 57, 7, 254, 0, 1);
        }
        if (c.DamageSuffered != 0) _screen.PrintString(c.DamageSuffered.ToString(), 17, 28, 254, 0, 1);
        byte col = (byte)(charNum != SelectedCharacter || CountActiveCharacters() == 1 ? 1 : 212);
        _screen.DrawBox(0, 0, 65, 33, col);
        _screen.CopyRegion(0, 0, ActiveCharsXpos[charNum], 143, 66, 34, _screen.CurPage, cp, true);
        _screen.CurPage = cp;
        _screen.SetFont(tmpFid);
    }

    /// <summary>gui_drawCharFaceShape: which of the fourteen faces, and how faded.</summary>
    public void DrawCharFaceShape(int charNum, int x, int y, int pageNum)
    {
        var c = Characters[charNum];
        if (c.CurFaceFrame < 7 && c.TempFaceFrame != 0) c.CurFaceFrame = c.TempFaceFrame;
        if (c.TempFaceFrame == 0 && c.CurFaceFrame > 1 && c.CurFaceFrame < 7) c.CurFaceFrame = c.TempFaceFrame;
        int frm = (c.Flags & 0x1108) != 0 && c.CurFaceFrame < 7 ? 1 : c.CurFaceFrame;
        if (c.HitPointsCur <= c.HitPointsMax >> 1) frm += 14;
        var faces = FaceShapes[charNum];
        if (frm < faces.Length && faces[frm] != null)
            _screen.DrawShape(pageNum, faces[frm], x, y, 0, 0x100,
                new ShapeDrawOptions { FadeTable = _screen.PaletteOverlay2, FadeLevel = (c.Flags & 0x80) != 0 ? 1 : 0 });
        if ((c.Flags & 0x40) != 0) _screen.DrawShape(pageNum, GameShapes[21], x, y, 0, 0);
    }

    /// <summary>gui_drawLiveMagicBar: the vertical bar beside a portrait, and the colour it turns.</summary>
    /// <summary>
    /// The bar under a portrait, at the value the points just changed to. The engine scales both the
    /// value and the maximum through 8192 first, so a bar for 30 points and one for 300 fill alike.
    /// </summary>
    public void DrawPointsBar(int charNum, int type, int newVal, int pointsMax)
    {
        int[][] barData = { new[] { 0x27, 0x9a, 0x98, 0x01, 0x4254 }, new[] { 0x21, 0xa2, 0xa0, 0x00, 0x4253 } };
        if (pointsMax < 1 || charNum < 0 || charNum > 2) return;
        string cf = _screen.SetFont("6");
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        int s = 8192 / pointsMax;
        pointsMax = (s * pointsMax) >> 8;
        newVal = (s * newVal) >> 8;
        var bd = barData[type];
        DrawLiveMagicBar(bd[0] + ActiveCharsXpos[charNum], 175, newVal, 0, pointsMax, 5, 32, bd[1], 1, bd[3]);
        _screen.PrintText(LangString(bd[4]), bd[0] + ActiveCharsXpos[charNum], 144, (byte)bd[2], 0);
        _screen.SetFont(cf);
        _screen.CurPage = cp;
    }

    public void DrawLiveMagicBar(int x, int y, int curPoints, int unk, int maxPoints, int w, int h, int col1, int col2, int flag)
    {
        w -= 1;
        h -= 1;
        if (maxPoints < 1) return;
        int t = curPoints < 1 ? 0 : curPoints;
        curPoints = maxPoints < t ? maxPoints : t;
        int barHeight = curPoints * h / maxPoints;
        if (barHeight < 1 && curPoints > 0) barHeight = 1;
        _screen.DrawClippedLine(x - 1, y - h, x - 1, y, 1);
        if (flag != 0)
        {
            if (maxPoints >> 1 > curPoints) col1 = 144;
            if (maxPoints >> 2 > curPoints) col1 = 132;
        }
        if (barHeight > 0) _screen.FillRect(x, y - barHeight, x + w, y, (byte)col1);
        if (barHeight < h) _screen.FillRect(x, y - h, x + w, y - barHeight, (byte)col2);
        if (unk > 0 && unk < maxPoints) _screen.DrawBox(x, y - barHeight, x + w, y, (byte)(col1 - 2));
    }

    /// <summary>calcCharPortraitXpos: the portraits are spread over what is left of the bar.</summary>
    public void CalcCharPortraitXpos()
    {
        int nc = CountActiveCharacters();
        if (nc == 0) return;
        int t = (235 - nc * 66) / (nc + 1);
        for (int i = 0; i < nc; i += 1) ActiveCharsXpos[i] = i * 66 + t * (i + 1) + 83;
    }

    /// <summary>gui_drawMoneyBox: five stacks of coins and the number beside them.</summary>
    public void DrawMoneyBox(int pageNum)
    {
        int backupPage = _screen.CurPage;
        _screen.CurPage = pageNum;
        _screen.FillRect(292, 97, 316, 118, 252, pageNum);
        for (int i = 0; i < 5; i += 1)
        {
            if (Items.MoneyColumnHeight[i] == 0) continue;
            int h = Items.MoneyColumnHeight[i] - 1;
            for (int k = 0; k < 5; k += 1)
                _screen.DrawClippedLine(MoneyX[i] + k, MoneyY[i], MoneyX[i] + k, MoneyY[i] - h, MoneyCols[k]);
        }
        string backupFont = _screen.SetFont("6");
        _screen.PrintString(Items.Credits.ToString(), 305, 98, 254, 0, 1);
        _screen.SetFont(backupFont);
        _screen.CurPage = backupPage;
        if (pageNum == 6) _screen.CopyRegion(292, 97, 292, 97, 25, 22, 6, 0);
    }

    /// <summary>gui_drawCompass: the needle, drawn twice - once as its own shadow.</summary>
    /// <summary>How far the needle is swinging, and when it may move again.</summary>
    private int _compassStep;
    private double _compassTimer;

    /// <summary>
    /// updateCompass: the needle swings towards the way the party faces rather than jumping to it,
    /// overshooting and settling. A broken compass chases a random heading instead, so it wanders.
    /// </summary>
    public void UpdateCompass()
    {
        if ((_loader.Flags[31] & 0x40) == 0 || (_loader.UpdateFlags & 4) != 0) return;
        if (CompassDirection == -1)
        {
            _compassStep = 0;
            DrawCompass();
            return;
        }
        if (_compassTimer >= _loader.Clock) return;
        if ((_loader.Party.Direction << 6) == CompassDirection && _compassStep == 0) return;
        _compassTimer = _loader.Clock + 3 * LevelLoader.TickLength;
        int dir = _compassStep >= 0 ? 1 : -1;
        if (_compassStep != 0) _compassStep -= ((Math.Abs(_compassStep) >> 4) + 2) * dir;
        int diff = CompassBroken
            ? (sbyte)PresentationRandom(255) - CompassDirection
            : (_loader.Party.Direction << 6) - CompassDirection;
        if (diff <= -128) diff += 256;
        if (diff >= 128) diff -= 256;
        diff >>= 2;
        _compassStep += diff;
        _compassStep = Math.Max(-24, Math.Min(24, _compassStep));
        CompassDirection += _compassStep;
        if (CompassDirection < 0) CompassDirection += 256;
        if (CompassDirection > 255) CompassDirection -= 256;
        if ((((CompassDirection + 3) & 0xfd) >> 6) == _loader.Party.Direction && _compassStep < 2 && Math.Abs(diff) < 4)
        {
            CompassDirection = _loader.Party.Direction << 6;
            _compassStep = 0;
        }
        DrawCompass();
    }

    public void DrawCompass()
    {
        if ((_loader.Flags[31] & 0x40) == 0) return;
        if (CompassDirection == -1)
        {
            _compassDirectionIndex = -1;
            CompassDirection = _loader.Party.Direction << 6;
        }
        int t = ((CompassDirection + 4) >> 3) & 0x1f;
        if (t == _compassDirectionIndex) return;
        _compassDirectionIndex = t;
        var defs = StaticData.Table("CompassNumbers");
        int shapeIndex = defs[t * 4];
        int cx = (sbyte)defs[t * 4 + 1];
        int cy = (sbyte)defs[t * 4 + 2];
        int flags = defs[t * 4 + 3];
        _screen.DrawShape(_screen.CurPage, GameShapes[22 + Lang], 294, 3, 0, 0);
        _screen.DrawShape(_screen.CurPage, GameShapes[25 + shapeIndex], 298 + cx, cy + 9, 0, flags | 0x300,
            new ShapeDrawOptions { FadeTable = _screen.PaletteOverlay1, FadeLevel = 1 });
        _screen.DrawShape(_screen.CurPage, GameShapes[25 + shapeIndex], 299 + cx, cy + 8, 0, flags);
    }

    /// <summary>gui_drawBox: the two-tone frame the portraits sit in.</summary>
    public void DrawBox(int x, int y, int w, int h, byte frameColor1, byte frameColor2, int fillColor)
    {
        w -= 1;
        h -= 1;
        if (fillColor != -1) _screen.FillRect(x + 1, y + 1, x + w - 1, y + h - 1, (byte)fillColor);
        _screen.DrawClippedLine(x + 1, y, x + w, y, frameColor2);
        _screen.DrawClippedLine(x + w, y, x + w, y + h - 1, frameColor2);
        _screen.DrawClippedLine(x, y, x, y + h, frameColor1);
        _screen.DrawClippedLine(x, y + h, x + w, y + h, frameColor1);
    }
}

