// The buttons: where they are, what they answer to, and what happens when one is pressed.
//
// Transliterated from src/game/gui.mjs (gui_initButton, gui_initButtonsFromList, gui_resetButtonList,
// gui_setFaceFramesControlButtons, gui_enableDefaultPlayfieldButtons, findButtonsForClick,
// findButtonForKey, gui_toggleButtonDisplayMode and the movement handlers).
//
// A button is not a widget: it is a rectangle in a screen dim with a key code and an index into the
// engine's own callback table, and the playfield's set is rebuilt whenever the party changes.
namespace LolCore;

/// <summary>One live button, as gui_initButton makes it.</summary>
public sealed class GuiButton
{
    public int Index;          // 1-based position in the active list, the way the engine numbers them
    public int DefIndex;       // which ButtonDefs row it came from
    public int KeyCode, KeyCode2;
    public int DimTableIndex;
    public int Flags, Flags2;
    public int Arg;
    public int X, Y, Width, Height;
    public int AbsX, AbsY;
}

public sealed partial class Gui
{
    private const int ButtonStride = 9;

    public readonly List<GuiButton> ActiveButtons = new();
    public bool FloatingCursorsEnabled;

    private static int ButtonField(int index, int field) => StaticData.Table("ButtonNumbers")[index * ButtonStride + field];

    public void ResetButtonList()
    {
        if (AwaitingSpellTarget)
        {
            AwaitingSpellTarget = false;
            OnSpellTargetEnded?.Invoke();
        }
        ActiveButtons.Clear();
    }

    /// <summary>Raised when a waiting "cast on whom?" is called off.</summary>
    public Action OnSpellTargetEnded;

    /// <summary>gui_initButton: a row of the table becomes a rectangle on the screen.</summary>
    public GuiButton InitButton(int index, int x = -1, int y = -1, int val = -1)
    {
        var b = new GuiButton
        {
            Index = ActiveButtons.Count + 1,
            DefIndex = index,
            Flags = ButtonField(index, 0),
            KeyCode = ButtonField(index, 1),
            KeyCode2 = ButtonField(index, 2),
            Arg = val != -1 ? val & 0xff : ButtonField(index, 7),
            DimTableIndex = ButtonField(index, 8),
        };
        if (index == 15)
        {
            b.X = ActiveCharsXpos[SubMenuIndex] + 44;
            b.Arg = SubMenuIndex;
            b.Y = ButtonField(index, 4);
        }
        else
        {
            b.X = x != -1 ? x : (short)ButtonField(index, 3);
            b.Y = y != -1 ? y : (short)ButtonField(index, 4);
        }
        b.Width = ButtonField(index, 5) - 1;
        b.Height = ButtonField(index, 6) - 1;
        var dim = _screen.Dims[b.DimTableIndex];
        b.AbsX = (b.X < 0 ? b.X + (dim.W << 3) : b.X) + (dim.Sx << 3);
        b.AbsY = (b.Y < 0 ? b.Y + dim.H : b.Y) + dim.Sy;
        ActiveButtons.Add(b);
        return b;
    }

    public int SubMenuIndex;

    public void InitButtonsFromList(string tableName)
    {
        foreach (int index in StaticData.Table(tableName))
        {
            if (index == 255) break;
            InitButton(index);
        }
    }

    /// <summary>gui_setFaceFramesControlButtons: one button per portrait, wherever the portraits sit.</summary>
    public void SetFaceFramesControlButtons(int index, int xOffs)
    {
        int c = CountActiveCharacters();
        for (int i = 0; i < c; i += 1) InitButton(index + i, ActiveCharsXpos[i] + xOffs);
    }

    /// <summary>gui_enableDefaultPlayfieldButtons: the set the player walks the dungeon with.</summary>
    public void EnableDefaultPlayfieldButtons()
    {
        CalcCharPortraitXpos();
        ResetButtonList();
        InitButtonsFromList("ButtonList1");
        SetFaceFramesControlButtons(7, 44);
        SetFaceFramesControlButtons(11, 44);
        SetFaceFramesControlButtons(17, 0);
        SetFaceFramesControlButtons(29, 0);
        SetFaceFramesControlButtons(25, 33);
        if ((_loader.Flags[31] & 0x20) != 0) InitMagicScrollButtons();
    }

    public void InitMagicScrollButtons()
    {
        for (int i = 0; i < LevelLoader.SpellSlots; i += 1)
        {
            if (AvailableSpells[i] == -1) continue;
            InitButton(71 + i, -1, -1, i);
        }
    }

    /// <summary>processButtonList: every button under the pointer that takes this mouse button.</summary>
    public List<GuiButton> FindButtonsForClick(int x, int y, int mouseButton)
    {
        var hits = new List<GuiButton>();
        foreach (var b in ActiveButtons)
        {
            if ((b.Flags & 8) != 0) continue;
            if (x < b.AbsX || y < b.AbsY || x > b.AbsX + b.Width || y > b.AbsY + b.Height) continue;
            int accepts = mouseButton == 2 ? b.Flags & 0x5000 : b.Flags & 0x0500;
            if (accepts == 0) continue;
            b.Flags2 = mouseButton == 2 ? 0x1080 : 0x1000;
            hits.Add(b);
        }
        return hits;
    }

    public GuiButton FindButtonForClick(int x, int y, int mouseButton)
    {
        var hits = FindButtonsForClick(x, y, mouseButton);
        return hits.Count > 0 ? hits[0] : null;
    }

    /// <summary>findButtonForKey: the DOS scan code a button answers to, shifted or not.</summary>
    public GuiButton FindButtonForKey(int code, bool shifted)
    {
        foreach (var b in ActiveButtons)
        {
            if ((b.Flags & 8) != 0) continue;
            if (b.KeyCode == code && !shifted) { b.Flags2 = 0x80; return b; }
            if (b.KeyCode2 == code + 256 && shifted) { b.Flags2 = 0x1080; return b; }
            if (b.KeyCode2 == code && !shifted) { b.Flags2 = 0x1080; return b; }
        }
        return null;
    }

    /// <summary>
    /// The engine's callback table, reduced to what the port carries out: walking, turning, the
    /// portraits and the attack button. A button whose screen is not ported yet answers 0, which is
    /// what the engine's own disabled buttons do.
    /// </summary>
    public int Press(GuiButton b)
    {
        if (b == null) return 0;
        switch (b.DefIndex)
        {
            case 0: case 65: return ClickedUpArrow(b);
            case 1: case 2: case 66: return ClickedDownArrow(b);
            case 3: case 67: return ClickedLeftArrow(b);
            case 4: case 68: return ClickedRightArrow(b);
            case 5: case 69: return ClickedTurnLeftArrow(b);
            case 6: case 70: return ClickedTurnRightArrow(b);
            case 7: case 8: case 9: case 10: return ClickedAttackButton(b);
            case 17: case 18: case 19: case 20:
            case 21: case 22: case 23: case 24: return OpenCharSheet(b.Arg);
            case 44: return CloseCharSheet();
            case 33: case 34: case 35: case 36: case 37: case 38:
            case 39: case 40: case 41: case 42: case 43: return ClickedCharInventorySlot(b);
            case 50: case 51: case 52: case 53: case 54:
            case 55: case 56: case 57: case 58: case 59: return ClickedInventorySlot(b);
            case 60: case 61: return ClickedInventoryScroll(b);
            // 62 and 63 are the scene window: a lever, a door switch, a niche, or a wall with a
            // script behind it. Without them none of those could be used.
            case 62: case 63: return ClickedWall();
            case 64: return ClickedSequenceWindow();
            case 25: case 26: case 27: case 28: return ClickedLiveMagicBarsLeft(b);
            case 29: case 30: case 31: case 32: return ClickedPortraitEtcRight(b);
            case 81: case 82: case 83: case 84: return ClickedSpellTargetCharacter(b);
            case 85: return ClickedSpellTargetScene();
            case 86: case 87: return ClickedSceneThrowItem();
            case 88: OnOptions?.Invoke(); return 1;
            case 93: return ClickedLamp();
            case 90: return ClickedMoneyBox();
            case 91: return ClickedCompass();
            case 94: return ClickedStatusIcon();
            case 45: case 46: case 47: case 48: return ClickedSceneDropItem(b);
            case 49: return ClickedScenePickupItem(b);
            case 92: return OpenAutomap();
            case 89: return ClickedRestParty();
            case 11: case 12: case 13: case 14: return ClickedMagicButton(b);
            case 15: return ClickedMagicSubmenu(b);
            case 16: return ClickedScreen(b);
            case 71: case 72: case 73: case 74: case 75:
            case 76: case 77: case 78: case 79: case 80: return ClickedScroll(b);
            default: return 0;
        }
    }

    private static readonly int[] ButtonShapeX = { 0x0056, 0x0128, 0x000c, 0x0021, 0x0122, 0x000c, 0x0021, 0x0036, 0x000c, 0x0021, 0x0036 };
    private static readonly int[] ButtonShapeY = { 0x00b4, 0x00b4, 0x00b4, 0x00b4, 0x0020, 0x0084, 0x0084, 0x0084, 0x0096, 0x0096, 0x0096 };

    /// <summary>The shape last drawn pressed, so releasing it draws the right one again.</summary>
    private int _lastButtonShape;

    /// <summary>
    /// gui_toggleButtonDisplayMode: how a playfield button looks. 1 presses it, 0 releases the one
    /// that was pressed, 2 draws it plain, and 3 draws it greyed out - which is what disabling the
    /// controls does to all nine of them.
    /// </summary>
    public void ToggleButtonDisplayMode(int shapeIndex, int mode)
    {
        if (shapeIndex == 78 && (_loader.Flags[31] & 0x10) == 0) return;
        if (CurrentControlMode != 0 && _loader.NeedSceneRestore) return;
        if (mode == 0) shapeIndex = _lastButtonShape;
        int pageNum = 0;
        int x1 = shapeIndex != 0 ? ButtonShapeX[shapeIndex - 74] : 0;
        int y1 = shapeIndex != 0 ? ButtonShapeY[shapeIndex - 74] : 0;
        int x2 = 0, y2 = 0;
        switch (mode)
        {
            case 1:
                mode = 0x100;
                _lastButtonShape = shapeIndex;
                break;
            case 0:
                if (_lastButtonShape == 0) return;
                goto case 2;
            case 2:
                mode = 0;
                _lastButtonShape = 0;
                break;
            case 3:
                mode = 0;
                _lastButtonShape = 0;
                pageNum = 6;
                x2 = x1;
                y2 = y1;
                x1 = 0;
                y1 = 0;
                break;
        }
        if (shapeIndex < 0 || shapeIndex >= GameShapes.Length) return;
        var shape = GameShapes[shapeIndex];
        if (shape == null) return;
        _screen.DrawShape(pageNum, shape, x1, y1, 0, mode,
            new ShapeDrawOptions { FadeTable = _screen.PaletteOverlay1, FadeLevel = 1 });
        if (pageNum == 6)
        {
            int cp = _screen.CurPage;
            _screen.CurPage = 6;
            _screen.DrawGridBox(x1, y1, shape.Width, shape.Height, 1);
            _screen.CopyRegion(x1, y1, x2, y2, shape.Width, shape.Height, pageNum, 0, true);
            _screen.CurPage = cp;
        }
    }

    /// <summary>toggleSelectedCharacterFrame: the box around whoever is selected.</summary>
    public void ToggleSelectedCharacterFrame(int mode)
    {
        if (CountActiveCharacters() == 1) return;
        byte col = (byte)(mode != 0 ? 212 : 1);
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        int x = ActiveCharsXpos[SelectedCharacter];
        _screen.DrawBox(x, 143, x + 65, 176, col);
        _screen.CurPage = cp;
    }

    /// <summary>gui_enableControls / gui_disableControls: whether the party may act at all.</summary>
    public int EnableControls()
    {
        if (CurrentControlMode == 0) for (int i = 76; i < 85; i += 1) ToggleButtonDisplayMode(i, 2);
        ToggleFightButtons(false);
        return 1;
    }

    public int DisableControls(int controlMode)
    {
        if (CurrentControlMode != 0) return 0;
        ToggleFightButtons(true);
        for (int i = 76; i < 85; i += 1) ToggleButtonDisplayMode(i, (controlMode & 2) != 0 && i > 78 ? 2 : 3);
        return 1;
    }

    /// <summary>
    /// gui_toggleFightButtons: flag 0x2000 is "this character cannot swing right now", and the
    /// portrait is redrawn with its weapon panel greyed out.
    /// </summary>
    public void ToggleFightButtons(bool disable)
    {
        for (int i = 0; i < 3; i += 1)
        {
            if (!Characters[i].Active) continue;
            if (disable) Characters[i].Flags |= 0x2000;
            else Characters[i].Flags &= unchecked((int)0xffffdfff);
            DrawCharPortraitWithStats(i);
        }
    }

    private bool BlockedByFloatingCursor(GuiButton b) => b.Arg != 0 && !FloatingCursorsEnabled;

    internal int ClickedUpArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.MoveParty(_loader.Party.Direction, 0);
        return 1;
    }

    private int ClickedDownArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.MoveParty(_loader.Party.Direction ^ 2, 1);
        return 1;
    }

    private int ClickedLeftArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.MoveParty((_loader.Party.Direction - 1) & 3, 2);
        return 1;
    }

    private int ClickedRightArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.MoveParty((_loader.Party.Direction + 1) & 3, 3);
        return 1;
    }

    internal int ClickedTurnLeftArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.TurnParty(-1);
        _loader.RunLevelScript(_loader.Party.Block, 0x10);
        return 1;
    }

    internal int ClickedTurnRightArrow(GuiButton b)
    {
        if (BlockedByFloatingCursor(b)) return 0;
        _loader.TurnParty(1);
        _loader.RunLevelScript(_loader.Party.Block, 0x10);
        return 1;
    }

    /// <summary>clickedAttackButton: the character swings with whatever its items say it swings with.</summary>
    private int ClickedAttackButton(GuiButton b)
    {
        if (SelectionPinned == b.Arg) SelectionPinned = -1;   // acted: the selection may move on
        int c = b.Arg;
        if (c < 0 || c > 3 || (Characters[c].Flags & 0x314c) != 0) return 1;
        _loader.CharacterAttack(c);
        return 1;
    }


}
