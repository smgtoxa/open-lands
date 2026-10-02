// The part of a level script that talks to the player: message lines, the dialogue field, the
// special scenes a script sets up, and the "run this function next frame" hand-off.
//
// Transliterated from src/game/script.mjs (the printMessage, playDialogueText, printWindowText,
// processDialogue, setNextFunc and special-scene opcodes), src/game/spells.mjs
// (initDialogueSequence, restoreAfterDialogueSequence, resetPortraitsAndDisableSysTimer,
// gui_specialSceneSuspendControls, gui_specialSceneRestoreControls) and src/game/scene.mjs
// (prepareSpecialScene, restoreAfterSpecialScene, setSpecialSceneButtons).
//
// Without this the scripts still run - they just have no voice: every conversation in the game is
// a level script calling these, so a port that leaves them out walks through a silent castle.
namespace LolCore;

public sealed partial class LevelLoader
{
    /// <summary>setNextFunc: the level script asks to be re-entered at this function next frame.</summary>
    public int NextScriptFunc;

    /// <summary>Whether the dialogue field is down over the portraits.</summary>
    public bool DialogueField;

    /// <summary>Whether a script has a special scene up.</summary>
    public bool SpecialSceneFlag;

    /// <summary>sceneDefaultUpdate: whether the view redraws itself between script steps.</summary>
    public int SceneDefaultUpdate;

    /// <summary>The parameter the next piece of script text substitutes.</summary>
    public int ScriptTextParameter;

    /// <summary>
    /// runLoop's hand-off: a script that asked for another function gets it before the next frame.
    /// The host calls this once a frame, as LoLEngine::runLoop does.
    /// </summary>
    public void RunNextScriptFunc()
    {
        if (NextScriptFunc == 0) return;
        int func = NextScriptFunc;
        NextScriptFunc = 0;
        RunLevelScript(func, 2);
    }
}

public sealed partial class Gui
{
    /// <summary>The rectangle button 64 covers while a scene or a sequence owns the screen.</summary>
    public (int X, int Y, int W, int H) SceneWindowButton;

    public int SpsWindowX, SpsWindowY, SpsWindowW, SpsWindowH;
    public int SeqTrigger;

    /// <summary>gui_enableSequenceButtons: the button set a scene or a sequence runs with.</summary>
    public void EnableSequenceButtons(int x, int y, int w, int h, int enableFlags)
    {
        ResetButtonList();
        SceneWindowButton = (x, y, w, h);
        InitButtonsFromList("ButtonList3");
        if ((enableFlags & 1) != 0) InitButtonsFromList("ButtonList4");
        if ((enableFlags & 2) != 0) InitButtonsFromList("ButtonList5");
    }

    /// <summary>setSpecialSceneButtons.</summary>
    public void SetSpecialSceneButtons(int x, int y, int w, int h, int enableFlags)
    {
        EnableSequenceButtons(x, y, w, h, enableFlags);
        SpsWindowX = x; SpsWindowY = y; SpsWindowW = w; SpsWindowH = h;
    }

    /// <summary>gui_specialSceneRestoreButtons.</summary>
    public void SpecialSceneRestoreButtons()
    {
        if (SpsWindowW == 0 && SpsWindowH == 0) return;
        EnableDefaultPlayfieldButtons();
        SpsWindowX = SpsWindowY = SpsWindowW = SpsWindowH = SeqTrigger = 0;
    }

    /// <summary>gui_specialSceneSuspendControls.</summary>
    public void SpecialSceneSuspendControls(int controlMode)
    {
        if (controlMode != 0)
        {
            _loader.UpdateFlags |= 4;
            SetLampMode(false);
        }
        _loader.UpdateFlags |= 1;
        _loader.SpecialSceneFlag = true;
        CurrentControlMode = controlMode;
        CalcCharPortraitXpos();
    }

    /// <summary>gui_specialSceneRestoreControls.</summary>
    public void SpecialSceneRestoreControls(int restoreLamp)
    {
        if (restoreLamp != 0)
        {
            _loader.UpdateFlags &= 0xfffa;
            ResetLampStatus();
        }
        _loader.UpdateFlags &= 0xfffe;
        _loader.SpecialSceneFlag = false;
    }

    /// <summary>
    /// initDialogueSequence: the bottom of the screen becomes a speaking field. With a control mode
    /// it is a box drawn over the portraits; without one the text field slides up instead.
    /// </summary>
    public void InitDialogueSequence(int controlMode, int pageNum)
    {
        if (controlMode != 0)
        {
            int cp = _screen.CurPage;
            _screen.CurPage = pageNum;
            _screen.FillRect(0, 128, 319, 199, 1);
            DrawBox(0, 129, 320, 71, 136, 251, -1);
            DrawBox(1, 130, 318, 69, 136, 251, 252);
            _screen.ModifyScreenDim(5, 8, 131, 306, 66);
            _screen.ModifyScreenDim(4, 1, 133, 38, 60);
            _loader.Text?.ClearDim(4);
            _loader.UpdateFlags |= 2;
            CurrentControlMode = controlMode;
            CalcCharPortraitXpos();
            _screen.CurPage = cp;
        }
        else
        {
            _loader.Text?.SetupField(true);
            _loader.Text?.ExpandField();
            _loader.SetupScreenDims();
            _loader.Text?.ClearDim(4);
        }
        _loader.DialogueField = true;
    }

    /// <summary>restoreAfterDialogueSequence: the field goes away and the portraits come back.</summary>
    public void RestoreAfterDialogueSequence(int controlMode)
    {
        if (!_loader.DialogueField) return;
        StopPortraitSpeechAnim();
        CurrentControlMode = controlMode;
        CalcCharPortraitXpos();
        if (CurrentControlMode != 0)
        {
            _screen.ModifyScreenDim(4, 11, 124, 28, 45);
            _screen.ModifyScreenDim(5, 85, 123, 233, 54);
            _loader.UpdateFlags &= 0xfffd;
        }
        else
        {
            var d = _screen.Dims[5];
            _screen.FillRect(d.Sx << 3, d.Sy, (d.Sx << 3) + (d.W << 3) - 2, d.Sy + d.H - 2, (byte)d.Col2);
            _loader.Text?.ClearDim(4);
            _loader.Text?.SetupField(false);
        }
        _loader.DialogueField = false;
    }

    /// <summary>resetPortraitsAndDisableSysTimer.</summary>
    public void ResetPortraitsAndDisableSysTimer()
    {
        _loader.NeedSceneRestore = true;
        _loader.PauseSysTimers(true);
    }

    /// <summary>prepareSpecialScene: the script takes the screen over.</summary>
    public void PrepareSpecialScene(int fieldType, int hasDialogue, int suspendGui, int allowSceneUpdate, int controlMode, int fadeFlag)
    {
        ResetPortraitsAndDisableSysTimer();
        if (fieldType != 0)
        {
            if (suspendGui != 0) SpecialSceneSuspendControls(1);
            if (allowSceneUpdate == 0) _loader.SceneDefaultUpdate = 0;
            if (hasDialogue != 0) InitDialogueSequence(fieldType, 0);
            if (fadeFlag != 0) { _screen.FadePalette(_screen.Palette(3), 10); _screen.FadeFlag = 0; }
            SetSpecialSceneButtons(0, 0, 320, 130, controlMode);
        }
        else
        {
            if (suspendGui != 0) SpecialSceneSuspendControls(0);
            if (allowSceneUpdate == 0) _loader.SceneDefaultUpdate = 0;
            DisableControls(controlMode);
            if (fadeFlag != 0)
            {
                var p3 = _screen.Palette(3);
                Array.Copy(_screen.Palette(0), 128 * 3, p3, 128 * 3, 768 - 128 * 3);
                _screen.LoadSpecialColors(p3);
                _screen.FadePalette(p3, 10);
                _screen.FadeFlag = 0;
            }
            if (hasDialogue != 0) InitDialogueSequence(fieldType, 0);
            SetSpecialSceneButtons(112, 0, 176, 120, controlMode);
        }
    }

    /// <summary>restoreAfterSpecialScene: the playfield comes back.</summary>
    public int RestoreAfterSpecialScene(int fadeFlag, int redrawPlayField, int releaseTimScripts, int sceneUpdateMode)
    {
        if (!_loader.NeedSceneRestore) return 0;
        _loader.NeedSceneRestore = false;
        _loader.PauseSysTimers(false);
        if (_loader.DialogueField) RestoreAfterDialogueSequence(CurrentControlMode);
        if (_loader.SpecialSceneFlag) SpecialSceneRestoreControls(CurrentControlMode);
        int last = CurrentControlMode;
        CurrentControlMode = 0;
        SpecialSceneRestoreButtons();
        CalcCharPortraitXpos();
        CurrentControlMode = last;
        if (releaseTimScripts != 0 && _loader.Tim != null)
        {
            for (int i = 0; i < 6; i += 1) _loader.Tim.FreeAnimStruct(i);
            for (int i = 0; i < _loader.ActiveTim.Length; i += 1) _loader.ActiveTim[i] = null;
        }
        EnableControls();
        if (fadeFlag != 0)
        {
            if ((_screen.FadeFlag != 1 && _screen.FadeFlag != 2) || (_screen.FadeFlag == 1 && CurrentControlMode != 0))
            {
                if (CurrentControlMode != 0) _screen.FadeToBlack(10);
                else _screen.FadeClearSceneWindow(10);
            }
        }
        CurrentControlMode = 0;
        CalcCharPortraitXpos();
        if (redrawPlayField != 0) DrawPlayField();
        _loader.SceneDefaultUpdate = sceneUpdateMode;
        return 1;
    }

    /// <summary>clearDialogueField: the speaking area is wiped between lines.</summary>
    public void ClearDialogueField()
    {
        if (CurrentControlMode != 0 && !(_loader.Text?.TextEnabled ?? true)) return;
        _screen.CurDimIndex = 5;
        var d = _screen.Dims[5];
        _screen.FillRect(d.Sx << 3, d.Sy, (d.Sx << 3) + (d.W << 3) - 2, d.Sy + d.H - 2, (byte)d.Col2);
        _loader.Text?.ClearDim(4);
        _loader.Text?.ResetDimTextPositions(4);
    }
}
