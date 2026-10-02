// The text field: the panel that slides up over the portraits when someone speaks.
//
// Transliterated from src/game/text.mjs (setupField, expandField) and the parts of
// src/game/spells.mjs that open and close it (initSceneWindowDialogue,
// restoreAfterSceneWindowDialogue, gui_prepareForSequence, setupScreenDims).
//
// The slide is a loop of one-row blits on the engine's tick. A port that only has to end up in the
// same place runs the same loop without waiting: every blit happens, none of the waiting does.
namespace LolCore;

public sealed partial class TextDisplayer
{
    /// <summary>Whether the game is showing text at all (the speech-only setting turns it off).</summary>
    public bool TextEnabled = true;

    /// <summary>The engine's two scratch buffers: what the field covered, and what it looks like.</summary>
    public byte[] PageBuffer1, PageBuffer2;

    /// <summary>What the field needs to reach back into: the party's flags and the portraits.</summary>
    public Gui Gui;

    /// <summary>setupField: mode 1 saves the ground the field will cover; mode 0 slides it away.</summary>
    public void SetupField(bool mode)
    {
        if (TextEnabled)
        {
            const int y = 142;
            const int h = 37;
            if (mode)
            {
                PageBuffer1 = _screen.CopyRegionToBuffer(3, 0, 0, 320, 40);
                _screen.CopyRegion(80, y, 0, 0, 240, h, 0, 3, true);
                PageBuffer2 = _screen.CopyRegionToBuffer(3, 0, 0, 320, 40);
                _screen.CopyBlockToPage(3, 0, 0, 320, 40, PageBuffer1);
            }
            else
            {
                _screen.CurDimIndex = ClearDim(4);
                int cp = _screen.CurPage;
                _screen.CurPage = 2;
                PageBuffer1 = _screen.CopyRegionToBuffer(3, 0, 0, 320, 40);
                if (PageBuffer2 != null) _screen.CopyBlockToPage(3, 0, 0, 320, 40, PageBuffer2);
                _screen.CopyRegion(0, 0, 80, y, 240, h, 3, _screen.CurPage, true);
                for (int i = 177; i > 141; i -= 1)
                {
                    _screen.CopyRegion(83, i, 83, i - 1, 235, 3, 0, 0, true);
                    _screen.CopyRegion(83, i + 1, 83, i + 1, 235, 1, 2, 0, true);
                    Wait?.Invoke(TickLength);
                }
                _screen.CopyBlockToPage(3, 0, 0, 320, 40, PageBuffer1);
                _screen.CurPage = cp;
                UpdateFlagsClear?.Invoke(0x0002);
            }
        }
        else
        {
            if (!mode) _screen.CurDimIndex = ClearDim(4);
            Gui?.ToggleSelectedCharacterFrame(1);
        }
    }

    /// <summary>expandField: the field slides up into place and the text may start.</summary>
    public void ExpandField()
    {
        if (TextEnabled)
        {
            _screen.CurDimIndex = ClearDim(3);
            var tmp = _screen.CopyRegionToBuffer(3, 0, 0, 320, 10);
            _screen.CopyRegion(83, 140, 0, 0, 235, 3, 0, 2, true);
            for (int i = 140; i < 177; i += 1)
            {
                _screen.CopyRegion(0, 0, 83, i, 235, 3, 2, 0, true);
                Wait?.Invoke(TickLength);
            }
            _screen.CopyBlockToPage(3, 0, 0, 320, 10, tmp);
            UpdateFlagsSet?.Invoke(0x0002);
        }
        else
        {
            ClearDim(3);
            Gui?.ToggleSelectedCharacterFrame(0);
        }
    }

    /// <summary>
    /// How the host is told that time passed: the panel slides one row per tick, and a script
    /// waiting on the clock behind it is due at a moment measured in those ticks.
    /// </summary>
    public Action<double> Wait;

    /// <summary>The engine's tick, in milliseconds.</summary>
    public double TickLength = 1000.0 / 60.0;

    /// <summary>The engine's updateFlags live on the loader; the field only sets and clears bits.</summary>
    public Action<int> UpdateFlagsSet, UpdateFlagsClear;
}
