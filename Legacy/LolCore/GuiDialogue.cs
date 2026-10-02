// Conversations: the text a character speaks, and the choices under it.
//
// Transliterated from src/game/text.mjs (printDialogueText2, preprocessString, the DialogueMixin:
// setupDialogueButtons, drawDialogueButtons, processDialogue) and the parts of gui.mjs that draw
// the buttons' frames.
//
// A dialogue button is not a GuiButton: the engine keeps its own three, hit-tests them against the
// pointer itself, and answers with the 1-based number of the one that was pressed.
namespace LolCore;

public sealed partial class Gui
{
    public int DialogueNumButtons;
    public readonly string[] DialogueButtonString = { null, null, null };
    public int DialogueHighlightedButton;
    public readonly int[] DialogueButtonPosX = new int[3];
    public readonly int[] DialogueButtonPosY = new int[3];
    public int DialogueButtonXoffs, DialogueButtonYoffs;
    public int DialogueButtonWidth = 74;
    public byte DialogueButtonLabelColor1 = 144, DialogueButtonLabelColor2 = 254;

    public void InitDialogue()
    {
        DialogueNumButtons = 0;
        for (int i = 0; i < 3; i += 1) { DialogueButtonString[i] = null; DialogueButtonPosX[i] = DialogueButtonPosY[i] = 0; }
        DialogueHighlightedButton = 0;
        DialogueButtonXoffs = DialogueButtonYoffs = 0;
        DialogueButtonWidth = 74;
        DialogueButtonLabelColor1 = 144;
        DialogueButtonLabelColor2 = 254;
    }

    /// <summary>setupDialogueButtons: one, two or three choices, spread across the text window.</summary>
    public void SetupDialogueButtons(int numStr, string s1, string s2, string s3)
    {
        _screen.CurDimIndex = 5;
        DialogueNumButtons = numStr;
        DialogueButtonString[0] = s1;
        DialogueButtonString[1] = s2;
        DialogueButtonString[2] = s3;
        DialogueHighlightedButton = 0;
        var d = _screen.Dims[5];
        int y = d.Sy + d.H - 9;
        for (int i = 0; i < 3; i += 1) DialogueButtonPosY[i] = y;
        if (numStr == 1)
        {
            int x = d.Sx + d.W - (DialogueButtonWidth + 3);
            for (int i = 0; i < 3; i += 1) DialogueButtonPosX[i] = x;
        }
        else
        {
            int xOffs = numStr == 0 ? d.W : d.W / numStr;
            int x0 = d.Sx + (xOffs >> 1) - 37;
            DialogueButtonPosX[0] = x0;
            DialogueButtonPosX[1] = x0 + xOffs;
            DialogueButtonPosX[2] = x0 + 2 * xOffs;
        }
        DrawDialogueButtons();
    }

    public void DrawDialogueButtons()
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        string of = _screen.SetFont("6");
        for (int i = 0; i < DialogueNumButtons; i += 1)
        {
            int x = DialogueButtonPosX[i];
            int y = DialogueButtonYoffs + DialogueButtonPosY[i];
            DrawBox(x, y, DialogueButtonWidth, 9, 136, 251, 252);
            string label = DialogueButtonString[i] ?? "";
            _screen.PrintText(label, x + (DialogueButtonWidth >> 1) - _screen.TextWidth(label) / 2, y + 2,
                DialogueHighlightedButton == i ? DialogueButtonLabelColor1 : DialogueButtonLabelColor2, 0);
        }
        _screen.SetFont(of);
        _screen.CurPage = cp;
    }

    /// <summary>
    /// processDialogue: which choice the pointer is over, and - when the button is pressed - which
    /// one was chosen, 1-based, or 0 while the game is still waiting.
    /// </summary>
    /// <summary>Set by the host: whether a spoken line is still playing.</summary>
    public Func<bool> SpeechPlaying;

    public int ProcessDialogue(bool pressed)
    {
        int highlighted = DialogueHighlightedButton;
        int result = 0;
        if (DialogueNumButtons == 0 && (SpeechPlaying == null || !SpeechPlaying())) result = 1;
        for (int i = 0; i < DialogueNumButtons; i += 1)
        {
            int x = DialogueButtonPosX[i] + DialogueButtonXoffs;
            int y = DialogueButtonYoffs + DialogueButtonPosY[i];
            if (MouseX >= x && MouseX <= x + DialogueButtonWidth && MouseY >= y && MouseY <= y + 9)
            {
                DialogueHighlightedButton = i;
                if (pressed) result = i + 1;
                break;
            }
        }
        if (highlighted != DialogueHighlightedButton) DrawDialogueButtons();
        if (result == 0) return 0;
        StopPortraitSpeechAnim();
        DialogueNumButtons = 0;
        DialogueButtonString[0] = DialogueButtonString[1] = DialogueButtonString[2] = null;
        // The box the conversation was in is wiped, and the message area starts again from the top.
        // Dim 13 is the scene's own clip rectangle: filling that would paint over the view's border,
        // which is why the engine skips it.
        var text = _loader.Text;
        if (_screen.CurDimIndex != 13)
        {
            var d = _screen.Dims[_screen.CurDimIndex];
            _screen.FillRect(d.Sx, d.Sy, d.Sx + d.W - 2, d.Sy + d.H - 1, (byte)d.Col2);
        }
        if (text != null)
        {
            text.ClearDim(4);
            text.ResetDimTextPositions(4);
        }
        return result;
    }

    /// <summary>
    /// printDialogueText2: a line of speech into the text window. Which dim it lands in depends on
    /// whether the field is up: the panel over the portraits, or the one-line message area.
    /// </summary>
    /// <summary>The host listens in, so what is said also lands in its own log.</summary>
    public Action<string> OnDialogueText;

    public void PrintDialogueText(int dim, string str, int[] paramList = null, int paramIndex = 0)
    {
        str = PreprocessString(str, paramList, paramIndex);
        if (!string.IsNullOrWhiteSpace(str)) OnDialogueText?.Invoke(str);
        var text = _loader.Text;
        if (text == null) return;
        int oldDim;
        if (dim == 3)
        {
            if ((_loader.UpdateFlags & 2) != 0)
            {
                oldDim = text.ClearDim(4);
                text.DimData[4].Color1 = 254;
                text.DimData[4].Color2 = (byte)_screen.Dims[_screen.CurDimIndex].Col2;
            }
            else
            {
                oldDim = text.ClearDim(3);
                text.DimData[3].Color1 = 192;
                text.DimData[3].Color2 = (byte)_screen.Dims[_screen.CurDimIndex].Col2;
                _screen.CopyColor(192, 254);
                text.EnableFadeTimer?.Invoke();
            }
        }
        else
        {
            oldDim = _screen.CurDimIndex;
            _screen.CurDimIndex = dim;
            text.DimData[dim].Color1 = 254;
            text.DimData[dim].Color2 = (byte)_screen.Dims[dim].Col2;
        }
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        string of = _screen.SetFont("9");
        text.DisplayText(str ?? "");
        _screen.CurDimIndex = oldDim;
        _screen.CurPage = cp;
        _screen.SetFont(of);
    }

    /// <summary>
    /// preprocessString: the engine's own %-codes. %n is a character's name, %s another string from
    /// the table, %d a number - and which one comes from the script's parameters, not from the text.
    /// </summary>
    public string PreprocessString(string str, int[] paramList, int paramIndex)
    {
        if (string.IsNullOrEmpty(str) || str.IndexOf('%') < 0) return str;
        var outText = new System.Text.StringBuilder();
        int Value() => paramList != null && paramIndex < paramList.Length ? paramList[paramIndex] : 0;
        for (int i = 0; i < str.Length; )
        {
            if (str[i] != '%') { outText.Append(str[i++]); continue; }
            i += 1;
            if (i >= str.Length) break;
            char para = str[i];
            if (para == '#')
            {
                i += 1;
                if (i >= str.Length) break;
                para = str[i];
                if ("EGXcdefgsux".IndexOf(para) < 0) continue;
            }
            else if (para == ' ' || para == '+' || para == '-') i += 1;
            if (i >= str.Length) break;
            para = str[i];
            if (para == '0') i += 1;
            else while (i < str.Length && str[i] > '/' && str[i] < ':') i += 1;
            if (i >= str.Length) break;
            para = str[i++];
            switch (para)
            {
                case 'a': outText.Append(_loader.ScriptTextParameter); break;   // setScriptTextParameter
                case 'n':
                    if (paramList != null)
                    {
                        int who = Value();
                        if (who >= 0 && who < Characters.Length) outText.Append(Characters[who].Name ?? "");
                    }
                    break;
                case 's':
                    if (paramList != null) outText.Append(GameStrings.Get(Value(), LandsFile, LevelLangFile) ?? "");
                    break;
                case 'X': case 'd': case 'u': case 'x':
                    if (paramList != null) outText.Append(Value());
                    break;
            }
        }
        return outText.ToString();
    }
}
