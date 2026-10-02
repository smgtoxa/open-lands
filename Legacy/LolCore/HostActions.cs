// The host's own verbs: the lantern, the marching order, and who acts next.
//
// Transliterated from src/game/scene.mjs (updateLampStatus), src/game/gui.mjs (clickedLamp,
// toggleLantern, refillLantern, nextReadyCharacter, uiCanAct, uiSelectNextAfter, uiAutoSelect,
// quickAttack) and src/game/host-ui.mjs (uiSwapParty, uiRotateParty, uiSelectCharacter,
// uiAttackCooldown).
//
// None of this is in the original game: the DOS build lights the lantern by itself and has no
// marching order. It is what the browser build put on either side of the picture, so the Godot
// host needs it to show the same panels.
namespace LolCore;

public sealed partial class LevelLoader
{
    /// <summary>How much oil the lantern holds, 0..100 per flask (script global 9).</summary>
    public int LampOilStatus;

    /// <summary>The host's switch: the lantern keeps its oil but stops burning.</summary>
    public bool LampSwitchedOff;

    /// <summary>When the flame next flickers.</summary>
    public double LampStatusTimer;
}

public sealed partial class Gui
{
    /// <summary>What a spell costs its caster at that power.</summary>
    public static int SpellMpCost(int spell, int level) => SpellTable.Field(spell, 1 + level);

    /// <summary>And what it costs in blood.</summary>
    public static int SpellHpCost(int spell, int level) => SpellTable.Field(spell, 5 + level);

    /// <summary>updateLampStatus: the flame dims as the oil goes, and flickers while it burns.</summary>
    /// <summary>resetLampStatus: the lamp is lit again, and its light worked out afresh.</summary>
    public void ResetLampStatus()
    {
        _loader.Flags[31] |= 0x04;
        _loader.LampEffect = -1;
        UpdateLampStatus();
    }

    /// <summary>
    /// setLampMode: the lamp put out while something else has the screen. With a lamp in the party
    /// the dark shape is drawn in its corner, and the view goes to its dimmest.
    /// </summary>
    public void SetLampMode(bool lampOn)
    {
        _loader.Flags[31] &= 0xfb;
        if ((_loader.Flags[31] & 0x08) == 0 || !lampOn) return;
        if (GameShapes.Length > 43) _screen.DrawShape(0, GameShapes[43], 291, 56, 0, 0);
        _loader.LampEffect = 8;
    }

    public void UpdateLampStatus()
    {
        if ((_loader.UpdateFlags & 4) != 0 || (_loader.Flags[31] & 0x08) == 0) return;
        int newLampEffect;
        if (_loader.Brightness == 0 || _loader.LampOilStatus == 0 || _loader.LampSwitchedOff)
        {
            newLampEffect = 8;
            if (newLampEffect != _loader.LampEffect && _screen.FadeFlag == 0)
                _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, newLampEffect);
        }
        else
        {
            int oil = Math.Min(_loader.LampOilStatus, 100);
            newLampEffect = (3 - (oil - 1) / 25) << 1;
            if (_loader.LampEffect == -1)
            {
                if (_screen.FadeFlag == 0) _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, newLampEffect);
                _loader.LampStatusTimer = _loader.Clock + (10 + PresentationRoll(1, 30)) * LevelLoader.TickLength;
            }
            else if ((_loader.LampEffect & 0xfe) == (newLampEffect & 0xfe))
            {
                if (_loader.Clock <= _loader.LampStatusTimer) newLampEffect = _loader.LampEffect;
                else
                {
                    newLampEffect = _loader.LampEffect ^ 1;
                    _loader.LampStatusTimer = _loader.Clock + (10 + PresentationRoll(1, 30)) * LevelLoader.TickLength;
                }
            }
            else if (_screen.FadeFlag == 0) _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, newLampEffect);
        }
        if (newLampEffect == _loader.LampEffect) return;
        if (35 + newLampEffect < GameShapes.Length)
            _screen.DrawShape(_screen.CurPage, GameShapes[35 + newLampEffect], 291, 56, 0, 0);
        _loader.LampEffect = newLampEffect;
    }

    /// <summary>The host switch: off keeps the oil, and the view goes dark.</summary>
    public bool ToggleLantern()
    {
        if ((_loader.Flags[31] & 0x08) == 0) return false;
        _loader.LampSwitchedOff = !_loader.LampSwitchedOff;
        _loader.LampEffect = -1;
        UpdateLampStatus();
        return true;
    }

    private bool IsOilFlask(int item) => item != 0 && Items.InPlay[item].ItemPropertyIndex == 248;

    /// <summary>clickedLamp, reached from the host's Refill button: a flask goes in, or it reports.</summary>
    public string RefillLantern()
    {
        if ((_loader.Flags[31] & 0x08) == 0) return "No lantern yet.";
        if ((_loader.UpdateFlags & 3) != 0 || WeaponsDisabled) return "Not just now.";
        if (!IsOilFlask(ItemInHand))
        {
            int slot = Array.FindIndex(Items.Inventory, IsOilFlask);
            if (slot < 0) return "No lamp oil in the inventory.";
            int flask = Items.Inventory[slot];
            Items.Inventory[slot] = ItemInHand;
            SetHandItem(flask);
            DrawInventory();
        }
        if (_loader.LampOilStatus >= 100) return "The lantern is full.";
        Items.Delete(ItemInHand);
        SetHandItem(0);
        _loader.LampOilStatus += 100;
        _loader.LampEffect = -1;
        if (_loader.Brightness != 0) _loader.SetPaletteBrightness(_screen.Palette(0), _loader.Brightness, _loader.LampEffect);
        UpdateLampStatus();
        return "The lantern is filled.";
    }

    /// <summary>uiCanAct: active, alive, and not busy, stunned, paralysed or asleep.</summary>
    public bool CanAct(int c) =>
        c >= 0 && c < 4 && (Characters[c].Flags & 1) != 0 && (Characters[c].Flags & 0x314c) == 0 && Characters[c].HitPointsCur > 0;

    /// <summary>gui_highlightPortraitFrame: the box moves to this hero.</summary>
    public void HighlightPortraitFrame(int c)
    {
        if (c != SelectedCharacter)
        {
            int old = SelectedCharacter;
            SelectedCharacter = c;
            DrawCharPortraitWithStats(old);
        }
        DrawCharPortraitWithStats(c);
    }

    /// <summary>
    /// The hero the player picked by hand, whose turn to be selected holds until they act. -1 when
    /// the selection is the interface's own choice and may move freely.
    /// </summary>
    public int SelectionPinned = -1;

    /// <summary>
    /// uiAutoSelect: the selection never rests on a hero who cannot act. A hero the player picked
    /// keeps it until they have acted; a dead or missing one hands over to anyone still standing.
    /// </summary>
    public void AutoSelect()
    {
        if (AwaitingSpellTarget || (_loader.UpdateFlags & 3) != 0 || WeaponsDisabled
            || _loader.NeedSceneRestore || _loader.SysTimerPaused) return;
        int s = SelectedCharacter;
        if (s < 0 || s > 3) return;
        if (CanAct(s)) return;
        var ch = Characters[s];
        if (SelectionPinned == s && ch.Active) return;
        for (int j = 1; j < 4; j += 1)
        {
            int n = (s + j) % 4;
            if (CanAct(n)) { SelectionPinned = -1; HighlightPortraitFrame(n); return; }
        }
        // Nobody is ready: a dead or missing hero still hands over to any living one.
        if (ch.Active && ch.HitPointsCur > 0) return;
        for (int j = 1; j < 4; j += 1)
        {
            int n = (s + j) % 4;
            if (Characters[n].Active && Characters[n].HitPointsCur > 0)
            {
                SelectionPinned = -1;
                HighlightPortraitFrame(n);
                return;
            }
        }
    }

    /// <summary>uiSelectNextAfter: after a hero acts, the selection moves on.</summary>
    public void SelectNextAfter(int c)
    {
        SelectionPinned = -1;   // they have acted, so the selection may move again
        for (int j = 1; j < 4; j += 1)
        {
            int n = (c + j) % 4;
            if (CanAct(n)) { HighlightPortraitFrame(n); return; }
        }
    }

    /// <summary>
    /// quickAttack: "attack with the next character". Start at the selected hero and take the first
    /// one who can actually swing. Trying only the selected hero meant that whenever they were on
    /// cooldown the button did nothing at all - it moved the selection and swallowed the press,
    /// which reads as a dead button while the same attack from a hero's own card works.
    /// </summary>
    public bool QuickAttack()
    {
        if ((_loader.UpdateFlags & 3) != 0 || WeaponsDisabled) return false;
        int start = SelectedCharacter;
        for (int i = 0; i < 4; i += 1)
        {
            int c = (start + i) % 4;
            if ((Characters[c].Flags & 1) == 0 || Characters[c].HitPointsCur <= 0) continue;
            if (!CanAct(c)) continue;
            if (c != SelectedCharacter) SelectCharacter(c);
            _loader.CharacterAttack(c);
            SelectNextAfter(c);
            return true;
        }
        return false;   // nobody is ready to swing yet
    }

    /// <summary>uiSelectCharacter: a click on a party card.</summary>
    public void SelectCharacter(int c)
    {
        if (c < 0 || c > 3 || (Characters[c].Flags & 1) == 0 || c == SelectedCharacter) return;
        SelectionPinned = c;   // the player's choice holds until this hero acts
        HighlightPortraitFrame(c);
    }

    /// <summary>uiSwapParty: two heroes change places in the line-up.</summary>
    public bool SwapParty(int a, int b)
    {
        if (a == b || (Characters[a].Flags & 1) == 0 || (Characters[b].Flags & 1) == 0) return false;
        if ((_loader.UpdateFlags & 3) != 0 || WeaponsDisabled) return false;
        (Characters[a], Characters[b]) = (Characters[b], Characters[a]);
        (FaceShapes[a], FaceShapes[b]) = (FaceShapes[b], FaceShapes[a]);
        if (SelectedCharacter == a) SelectedCharacter = b;
        else if (SelectedCharacter == b) SelectedCharacter = a;
        CalcCharPortraitXpos();
        DrawAllCharPortraitsWithStats();
        return true;
    }

    /// <summary>uiRotateParty: the hero in front goes to the back.</summary>
    public bool RotateParty()
    {
        var active = Enumerable.Range(0, 4).Where(i => (Characters[i].Flags & 1) != 0).ToArray();
        if (active.Length < 2) return false;
        for (int i = 0; i + 1 < active.Length; i += 1) if (!SwapParty(active[i], active[i + 1])) return false;
        return true;
    }

    /// <summary>
    /// uiEquipBest: for each of the eleven slots, the best thing in the pack that fits it goes on.
    /// "Best" is the same score the browser uses - might plus protection, and nothing usable.
    /// </summary>
    public int EquipBest(int c)
    {
        if ((_loader.UpdateFlags & 3) != 0 || WeaponsDisabled || ItemInHand != 0) return 0;
        if (c < 0 || c > 3 || (Characters[c].Flags & 1) == 0) return 0;
        int Score(int item)
        {
            if (item == 0) return -1;
            var prop = Items.Properties[Items.InPlay[item].ItemPropertyIndex];
            return prop.Type == 0 ? -1 : prop.Might + prop.Protection;
        }
        bool Fits(int item, int slot) =>
            item != 0 && (Items.Properties[Items.InPlay[item].ItemPropertyIndex].Type & (1 << slot)) != 0;

        int changed = 0;
        var used = new HashSet<int>();
        for (int slot = 0; slot < 11; slot += 1)
        {
            int best = -1, bestScore = Score(Characters[c].Items[slot]);
            for (int inv = 0; inv < Items.Inventory.Length; inv += 1)
            {
                int item = Items.Inventory[inv];
                if (item == 0 || used.Contains(inv) || !Fits(item, slot)) continue;
                int s = Score(item);
                if (s > bestScore) { bestScore = s; best = inv; }
            }
            if (best < 0) continue;
            used.Add(best);
            int worn = Characters[c].Items[slot];
            Characters[c].Items[slot] = Items.Inventory[best];
            Items.Inventory[best] = worn;
            if (Characters[c].Items[slot] != 0) _loader.RunItemScript(c, Characters[c].Items[slot], 0x100, 0, 0);
            if (worn != 0) _loader.RunItemScript(c, worn, 0x80, 0, 0);
            changed += 1;
        }
        if (changed == 0) return 0;
        RecalcCharacterStats(c);
        DrawInventory();
        DrawCharPortraitWithStats(c);
        return changed;
    }
}
