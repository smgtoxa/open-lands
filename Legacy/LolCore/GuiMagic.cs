// Casting: the spell scroll on the left, and the little power menu a character's magic button opens.
//
// Transliterated from src/game/gui.mjs (clickedScroll, clickedMagicButton, clickedMagicSubmenu,
// clickedScreen, gui_initMagicSubmenu, gui_highlightSelectedSpell).
//
// The power menu is two buttons: the four-row strip over the portrait, where the row under the
// pointer is the power, and the rest of the screen, which cancels.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>Which character has the power menu open, or -1.</summary>
    public int ActiveMagicMenu = -1;

    /// <summary>clickedScroll: picking a spell from the scroll.</summary>
    public int ClickedScroll(GuiButton b)
    {
        if (SelectedSpell == b.Arg) return 1;
        HighlightSelectedSpell(false);
        SelectedSpell = b.Arg;
        HighlightSelectedSpell(true);
        DrawAllCharPortraitsWithStats();
        return 1;
    }

    /// <summary>
    /// quickCastSpell: pick the spell, drop to a power the hero can pay for, cast it, and then take
    /// the recovery and the experience that casting earns. 0 when nothing was cast.
    /// </summary>
    public int QuickCastSpell(int c, int slot, int spellLevel)
    {
        if ((_loader.UpdateFlags & 3) != 0 || WeaponsDisabled || _loader.NeedSceneRestore || _loader.SysTimerPaused) return 0;
        if (c < 0 || c > 3) return 0;
        var ch = Characters[c];
        if (!ch.Active || (ch.Flags & 0x314c) != 0) return 0;
        if (slot < 0 || slot >= AvailableSpells.Length || AvailableSpells[slot] == -1) return 0;
        if (ActiveMagicMenu != -1) ClickedScreen(null);
        if (SelectedSpell != slot)
        {
            HighlightSelectedSpell(false);
            SelectedSpell = slot;
            HighlightSelectedSpell(true);
            DrawAllCharPortraitsWithStats();
        }
        int spell = AvailableSpells[slot];
        // The chosen power is kept for next time; this cast drops to the highest one they can pay for.
        int level = spellLevel;
        while (level > 0 && (SpellMpCost(spell, level) > ch.MagicPointsCur || SpellHpCost(spell, level) >= ch.HitPointsCur)) level -= 1;
        if (_loader.Board.CheckMagic(c, spell, level) != 0)
        {
            // checkMagic in the original says why: out of magic in the hero's own voice, or too weak.
            if (SpellMpCost(spell, level) > ch.MagicPointsCur)
            {
                if (CharacterSays(0x4043, ch.Id, true))
                    _loader.Text?.PrintMessage(6, LangString(0x4043).Replace("%s", ch.Name));
            }
            else _loader.Text?.PrintMessage(2, LangString(0x4179).Replace("%s", ch.Name));
            return 0;
        }
        if (level != spellLevel)
            _loader.Text?.PrintMessage(0, $"{ch.Name}: not enough magic for power {spellLevel + 1}, casting at power {level + 1}.");
        ch.Flags |= 4;
        bool cast = ExtraSpellBase >= 0 && spell >= ExtraSpellBase
            ? CastExtraSpell(spell, c, level)
            : _loader.Board.CastSpell(c, spell, level, _loader.Level);
        if (cast)
        {
            _loader.Board.SetCharacterUpdateEvent(c, 1, 8, true);
            _loader.Board.IncreaseExperience(c, 2, level * level);
        }
        else
        {
            ch.Flags &= unchecked((int)0xfffffffb);
            DrawCharPortraitWithStats(c);
        }
        return cast ? 1 : 0;
    }

    /// <summary>gui_highlightSelectedSpell: the chosen spell is the one in a different colour.</summary>
    public void HighlightSelectedSpell(bool on)
    {
        int y = 15;
        string of = _screen.SetFont("9");
        for (int i = 0; i < LevelLoader.SpellSlots; i += 1)
        {
            if (AvailableSpells[i] == -1) continue;
            byte col = (byte)(on && i == SelectedSpell ? 132 : 1);
            _screen.PrintString(SpellNameOf(AvailableSpells[i]), 24, y, col, 0, 0);
            y += 9;
        }
        _screen.SetFont(of);
    }

    /// <summary>clickedMagicButton: the power menu opens over the character's portrait.</summary>
    public int ClickedMagicButton(GuiButton b)
    {
        int c = b.Arg;
        if (c < 0 || c > 3) return 1;
        if ((Characters[c].Flags & 0x314c) != 0) return 1;
        if (_loader.Board.CheckMagic(c, AvailableSpells[SelectedSpell], 0) != 0) return 1;
        Characters[c].Flags ^= 0x10;
        DrawCharPortraitWithStats(c);
        InitMagicSubmenu(c);
        ActiveMagicMenu = c;
        return 1;
    }

    public void InitMagicSubmenu(int charNum)
    {
        ResetButtonList();
        SubMenuIndex = charNum;
        InitButtonsFromList("ButtonList7");
    }

    /// <summary>
    /// clickedMagicSubmenu: which of the four rows the pointer is on decides the power, and the
    /// spell is cast at it - or the menu simply closes if the character cannot pay.
    /// </summary>
    public int ClickedMagicSubmenu(GuiButton b)
    {
        int spellLevel = (MouseY - 144) >> 3;
        int c = b.Arg;
        EnableDefaultPlayfieldButtons();
        if (c < 0 || c > 3) { ActiveMagicMenu = -1; return 1; }
        if (_loader.Board.CheckMagic(c, AvailableSpells[SelectedSpell], spellLevel) != 0)
        {
            Characters[c].Flags &= unchecked((int)0xffffffef);
            DrawCharPortraitWithStats(c);
        }
        else
        {
            Characters[c].Flags |= 4;
            if (SelectionPinned == c) SelectionPinned = -1;   // acted: the selection may move on
            Characters[c].Flags &= unchecked((int)0xffffffef);
            if (_loader.Board.CastSpell(c, AvailableSpells[SelectedSpell], spellLevel, _loader.Level))
            {
                _loader.Board.IncreaseExperience(c, 2, spellLevel * spellLevel);
            }
            else
            {
                Characters[c].Flags &= unchecked((int)0xfffffffb);
                DrawCharPortraitWithStats(c);
            }
        }
        ActiveMagicMenu = -1;
        return 1;
    }

    /// <summary>clickedScreen: anywhere else closes the power menu, and the click is dispatched again.</summary>
    public int ClickedScreen(GuiButton b)
    {
        if (ActiveMagicMenu >= 0)
        {
            Characters[ActiveMagicMenu].Flags &= unchecked((int)0xffffffef);
            DrawCharPortraitWithStats(ActiveMagicMenu);
        }
        ActiveMagicMenu = -1;
        EnableDefaultPlayfieldButtons();
        if ((b.Flags2 & 0x80) == 0)
        {
            var again = FindButtonForClick(MouseX, MouseY, (b.Flags2 & 0x1080) != 0 ? 2 : 1);
            if (again != null) Press(again);
        }
        return 1;
    }
}
