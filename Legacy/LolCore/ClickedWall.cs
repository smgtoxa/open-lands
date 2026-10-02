// Clicking on the wall ahead: a lever thrown, a door switch pressed, an item put into a niche, or a
// wall that simply runs a script.
//
// Transliterated from src/game/scene.mjs (clickedShape, clickedWallShape, clickedLeverOn,
// clickedLeverOff, clickedWallOnlyScript, clickedDoorSwitch, clickedNiche) and the clickedWall that
// dispatches them in src/game/gui.mjs.
//
// Without these the levers, the niches and the door switches in the game could not be used at all,
// and several levels cannot be finished without them.
namespace LolCore;

public sealed partial class Gui
{
    // lol.mjs: where a wall decoration lands on the screen, and the script flag a click runs with.
    private const int ClickedShapeXOffs = 136;
    private const int ClickedShapeYOffs = 8;
    private const int ClickedSpecialFlag = 0x40;

    /// <summary>
    /// clickedShape: whether the pointer is over one of the decorations on this wall. The engine
    /// walks the chain and tests each one's second frame, which is the one drawn dead ahead.
    /// </summary>
    private bool ClickedShape(int shapeIndex)
    {
        if (ClickedSpecialFlag != 0x40) return true;
        for (int guard = 0; shapeIndex > 0 && guard < 400; guard += 1)
        {
            var prop = _loader.Decorations.Properties[shapeIndex];
            if (prop == null) break;
            int s = prop.ShapeIndex[1];
            if (s != 0xffff)
            {
                var shape = _loader.Decorations.Shapes[s];
                if (shape != null)
                {
                    int w = shape.Width;
                    int h = shape.Height;
                    int x = prop.ShapeX[1] + ClickedShapeXOffs;
                    int y = prop.ShapeY[1] + ClickedShapeYOffs;
                    if ((prop.Flags & 1) != 0) w <<= 1;
                    if (MouseX >= x - 4 && MouseX < x + w + 8 && MouseY >= y - 4 && MouseY < y + h + 8) return true;
                }
            }
            shapeIndex = prop.Next;
        }
        return false;
    }

    private int WallShapeChain(int block, int direction) => _loader.Walls.ShapeMap[_loader.Map.Walls[block, direction]];

    /// <summary>clickedWallShape: a wall whose decoration answers a click with its own script.</summary>
    private int ClickedWallShape(int block, int direction)
    {
        if (!ClickedShape(WallShapeChain(block, direction))) return 0;
        OnSoundEffect?.Invoke(0x69);
        _loader.RunLevelScript(block, 0x40);
        return 1;
    }

    /// <summary>clickedLeverOn: the lever's shape is the next one along, and the script follows.</summary>
    private int ClickedLeverOn(int block, int direction)
    {
        if (!ClickedShape(WallShapeChain(block, direction))) return 0;
        _loader.Map.Walls[block, direction] += 1;
        _loader.Map.Walls[block, direction ^ 2] += 1;
        OnSoundEffect?.Invoke(0x1c);
        _loader.RunLevelScript(block, ClickedSpecialFlag);
        return 1;
    }

    /// <summary>clickedLeverOff: and back again.</summary>
    private int ClickedLeverOff(int block, int direction)
    {
        if (!ClickedShape(WallShapeChain(block, direction))) return 0;
        _loader.Map.Walls[block, direction] -= 1;
        _loader.Map.Walls[block, direction ^ 2] -= 1;
        OnSoundEffect?.Invoke(0x1d);
        _loader.RunLevelScript(block, ClickedSpecialFlag);
        return 1;
    }

    private int ClickedWallOnlyScript(int block)
    {
        _loader.RunLevelScript(block, ClickedSpecialFlag);
        return 1;
    }

    /// <summary>
    /// clickedDoorSwitch: the switch runs its script, and unless that script claimed the door, the
    /// door itself moves a quarter of a second later.
    /// </summary>
    private int ClickedDoorSwitch(int block, int direction)
    {
        if (!ClickedShape(WallShapeChain(block, direction))) return 0;
        OnSoundEffect?.Invoke(78);
        _loader.BlockDoor = 0;
        _loader.RunLevelScript(block, 0x40);
        if (_loader.BlockDoor == 0)
        {
            Wait?.Invoke(15 * LevelLoader.TickLength);
            _loader.ProcessDoorSwitch(block, 0);
        }
        return 1;
    }

    /// <summary>clickedNiche: what is in hand is put down on the ledge.</summary>
    private int ClickedNiche(int block, int direction)
    {
        if (!ClickedShape(WallShapeChain(block, direction)) || ItemInHand == 0) return 0;
        var (x, y) = MonsterBoard.CalcCoordinatesAddDirectionOffset(0x80, 0xff, _loader.Party.Direction);
        (x, y) = Party.CalcCoordinates(block, x, y);
        _loader.Board.SetItemPosition(ItemInHand, x, y, 8, true);
        SetHandItem(0);
        return 1;
    }

    /// <summary>
    /// clickedWall: which of them the wall ahead is. The type comes from the level's own wall table,
    /// so a lever is a lever because the level says so, not because of where it is.
    /// </summary>
    public int ClickedWall()
    {
        int block = Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);
        int dir = _loader.Party.Direction ^ 2;
        int type = _loader.Walls.SpecialTypes[_loader.Map.Walls[block, dir]];
        switch (type)
        {
            case 1:
                // In the camp the doors are known by where the party stands, not by the block ahead.
                if (InCamp)
                {
                    string piece = CampFacing();
                    if (piece != null) { OnCampPiece?.Invoke(piece); return 1; }
                }
                return ClickedWallShape(block, dir);
            case 2: return ClickedLeverOn(block, dir);
            case 3: return ClickedLeverOff(block, dir);
            case 4: return ClickedWallOnlyScript(block);
            case 5: return ClickedDoorSwitch(block, dir);
            case 6: return ClickedNiche(block, dir);
            default: return 0;
        }
    }

    /// <summary>clickedMoneyBox: how much the party is carrying, in the game's own words.</summary>
    public int ClickedMoneyBox()
    {
        int credits = Items.Credits;
        _loader.Text?.PrintMessage(0, FormatString(LangString(credits == 1 ? 0x402d : 0x402e), credits));
        return 1;
    }

    /// <summary>clickedCompass: which way they are facing, or that the compass is broken.</summary>
    public int ClickedCompass()
    {
        if ((_loader.Flags[31] & 0x40) == 0) return 0;
        if (CompassBroken) _loader.Text?.PrintMessage(4, LangString(0x425b));
        else _loader.Text?.PrintMessage(0, LangString(0x402f + _loader.Party.Direction));
        return 1;
    }

    /// <summary>Set by the level that breaks it: the compass then says so rather than a direction.</summary>
    public bool CompassBroken;

    /// <summary>clickedStatusIcon: what the icon under the pointer is warning about.</summary>
    public int ClickedStatusIcon()
    {
        int t = Math.Max(0, MouseX - 220);
        t = Math.Min(2, t / 14);
        int str = (CharStatusFlags[t] + 1) & 0xff;
        if (str == 0 || str > 3) return 1;
        _loader.Text?.PrintMessage(0x8002, LangString(str == 1 ? 0x424c : str == 2 ? 0x424e : 0x424d));
        return 1;
    }

    /// <summary>clickedLiveMagicBarsLeft: what this hero has left, in the game's own words.</summary>
    public int ClickedLiveMagicBarsLeft(GuiButton b)
    {
        int who = b?.Arg ?? 0;
        if (who < 0 || who > 3) return 1;
        HighlightPortraitFrame(who);
        var c = Characters[who];
        _loader.Text?.PrintMessage(0, FormatString(LangString(0x4047), c.Name,
            c.HitPointsCur, c.HitPointsMax, c.MagicPointsCur, c.MagicPointsMax));
        return 1;
    }

    /// <summary>
    /// clickedPortraitEtcRight: whatever is in hand, used on this hero. This is how a potion gets
    /// drunk; the item's own script decides what happens, and says so when it cannot.
    /// </summary>
    public int ClickedPortraitEtcRight(GuiButton b)
    {
        if (ItemInHand == 0) return 1;
        int who = b?.Arg ?? 0;
        if (who < 0 || who > 3) return 1;
        int flg = Items.Properties[Items.InPlay[ItemInHand].ItemPropertyIndex].Flags;
        if ((flg & 1) != 0)
        {
            if ((Characters[who].Flags & 8) == 0 || (flg & 0x20) != 0)
            {
                _loader.RunItemScript(who, ItemInHand, 0x400, 0, 0);
                _loader.RunLevelScript(_loader.Party.Block, 0x400, who, ItemInHand);
            }
            else _loader.Text?.PrintMessage(2, LangString(0x402c).Replace("%s", Characters[who].Name));
            return 1;
        }
        _loader.Text?.PrintMessage(2, LangString((flg & 8) != 0 ? 0x4029 : (flg & 0x10) != 0 ? 0x402a : 0x402b));
        return 1;
    }

    /// <summary>clickedSpellTargetCharacter: the hero a waiting heal is for.</summary>
    public int ClickedSpellTargetCharacter(GuiButton b)
    {
        int who = b?.Arg ?? 0;
        if (who < 0 || who > 3) return 1;
        _loader.Text?.PrintMessage(0, $"{Characters[who].Name}.\r");
        // Spell flag 1 in the low byte is the one that heals a single hero.
        if ((SpellTable.Field(ActiveSpellNumber, 9) & 0xff) == 1)
            CastHealOnSingleCharacter(who, ActiveSpellLevel);
        AwaitingSpellTarget = false;
        EnableDefaultPlayfieldButtons();
        return 1;
    }

    /// <summary>clickedSpellTargetScene: thought better of it, and the magic comes back.</summary>
    public int ClickedSpellTargetScene()
    {
        var c = Characters[ActiveSpellChar];
        _loader.Text?.PrintMessage(0, LangString(0x4041));
        c.MagicPointsCur = Math.Min(c.MagicPointsMax, c.MagicPointsCur + SpellMpCost(ActiveSpellNumber, ActiveSpellLevel));
        c.HitPointsCur = Math.Min(c.HitPointsMax, c.HitPointsCur + SpellHpCost(ActiveSpellNumber, ActiveSpellLevel));
        DrawCharPortraitWithStats(ActiveSpellChar);
        AwaitingSpellTarget = false;
        EnableDefaultPlayfieldButtons();
        return 1;
    }

    /// <summary>
    /// clickedLamp: a flask of oil in hand goes into the lantern; an empty hand asks it how much it
    /// has left, and it answers in the game's own words.
    /// </summary>
    public int ClickedLamp()
    {
        if ((_loader.Flags[31] & 0x08) == 0) return 0;
        if (ItemInHand != 0 && Items.InPlay[ItemInHand].ItemPropertyIndex == 248)
        {
            if (_loader.LampOilStatus >= 100)
            {
                _loader.Text?.PrintMessage(0, LangString(0x4061));
                return 1;
            }
            _loader.Text?.PrintMessage(0, LangString(0x4062));
            Items.Delete(ItemInHand);
            OnSoundEffect?.Invoke(181);
            SetHandItem(0);
            _loader.LampOilStatus += 100;
        }
        else
        {
            int s = _loader.LampOilStatus >= 100 ? 0x4060
                : _loader.LampOilStatus == 0 ? 0x405c
                : _loader.LampOilStatus / 33 + 0x405d;
            _loader.Text?.PrintMessage(0, LangString(0x405b).Replace("%s", LangString(s)));
        }
        if (_loader.Brightness != 0)
            _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, _loader.LampEffect);
        return 1;
    }

    /// <summary>
    /// clickedOptions: the DOS menu is the host's own panel here, so the button asks for that. The
    /// rest of what the original does around it - pausing the world, stopping a spoken line - the
    /// panel does for itself.
    /// </summary>
    public Action OnOptions;

    /// <summary>clickedSceneThrowItem: whatever is in hand, thrown at what is ahead.</summary>
    public int ClickedSceneThrowItem()
    {
        if ((_loader.UpdateFlags & 1) != 0) return 0;
        int block = Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);
        if ((_loader.Walls.WallFlags[_loader.Map.Walls[block, _loader.Party.Direction ^ 2]] & 2) != 0 || ItemInHand == 0) return 0;
        var (x, y) = Party.CalcCoordinates(_loader.Party.Block, 0x80, 0x80);
        if (_loader.Board.LaunchObject(0, ItemInHand, x, y, 12, _loader.Party.Direction << 1, SelectedCharacter, 0x3f))
        {
            OnSoundEffect?.Invoke(18);
            SetHandItem(0);
        }
        return 1;
    }

    /// <summary>clickedSequenceWindow: a click inside whatever scene has the screen.</summary>
    public int ClickedSequenceWindow()
    {
        _loader.RunLevelScript(Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction), 0x40);
        var (x, y, w, h) = SceneWindowButton;
        bool inside = MouseX >= x && MouseX < x + w && MouseY >= y && MouseY < y + h;
        if (SeqTrigger == 0 || !inside) SeqTrigger = 0;
        return 1;
    }

    /// <summary>Raised when the wall clicked was a piece of the party's camp.</summary>
    public Action<string> OnCampPiece;
}
