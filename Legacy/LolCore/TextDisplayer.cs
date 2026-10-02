// The text displayer: word wrap, scrolling and the control codes, inside a screen dimension.
//
// Transliterated from src/game/text.mjs (TextDisplayer_rpg / TextDisplayer_LoL). The page-break
// path is the one piece left out here - it waits for the player, which a port needs its interface
// for; everything a message line does is here.
//
// The wrap is the reason this cannot be handed to a host text renderer: it measures with the DOS
// font's own per-glyph widths, breaks on the last space that fits, and scrolls the dimension by one
// line height when it runs out of room.
namespace LolCore;

public sealed class TextDimData
{
    public byte Color1, Color2;
    public int Line, Column;
    public int CharSpacing, LineSpacing;
}

public sealed partial class TextDisplayer
{
    /// <summary>textColorFlag: which colour the message line was last printed in.</summary>
    public int TextColorFlag;

    private readonly Screen _screen;
    public readonly TextDimData[] DimData;

    public int LineCount;
    public int NumCharsPrinted;
    public int WaitButtonSpace;

    /// <summary>
    /// Whether a long line is allowed to stop and ask. The engine's own flag: the status line and
    /// displayTextSync print straight through, a conversation waits.
    /// </summary>
    public bool AllowPageBreak = true;

    /// <summary>
    /// Set while a line has stopped part-way with the MORE button up. The host clears it by calling
    /// ResumePageBreak when the player presses or clicks, and the rest of the line is then printed.
    /// </summary>
    public bool AwaitingPageBreak { get; private set; }

    /// <summary>What the prompt says, from the game's own strings.</summary>
    public string PageBreakString = "MORE";

    private bool _breakWanted;
    private string _pending;
    private int _pendingAt;
    private int _breakX, _breakY, _breakW;

    /// <summary>Asked for when the prompt goes up or comes down, so a host can say so too.</summary>
    public Action<bool, string> OnWaitingForPage;

    /// <summary>
    /// Whether anything is going to answer the prompt. The game is, a frame at a time. The parity
    /// harnesses are not - and they do not need to, because the engine they are compared against
    /// answers its own prompt from an Enter pump within a tick, so the recorded screen is the one
    /// with the line already finished. Left false, the rest of the line is printed at once.
    /// </summary>
    public bool HostAnswersPageBreak;

    /// <summary>Messages that would have stopped for a page break; the port has no button to wait on.</summary>
    public int PageBreaks;

    public TextDisplayer(Screen screen)
    {
        _screen = screen;
        DimData = screen.Dims.Select(d => new TextDimData { Color1 = (byte)d.Col1, Color2 = (byte)d.Col2 }).ToArray();
    }

    public int ClearDim(int dim)
    {
        int previous = _screen.CurDimIndex;
        _screen.CurDimIndex = dim;
        DimData[dim].Color1 = (byte)_screen.Dims[dim].Col1;
        DimData[dim].Color2 = (byte)_screen.Dims[dim].Col2;
        ClearCurDim();
        return previous;
    }

    public void ClearCurDim()
    {
        int d = _screen.CurDimIndex;
        var dim = _screen.Dims[d];
        _screen.FillRect(dim.Sx << 3, dim.Sy, ((dim.Sx + dim.W) << 3) - 1, dim.Sy + dim.H - 1, DimData[d].Color2, _screen.CurPage);
        LineCount = 0;
        DimData[d].Column = DimData[d].Line = 0;
    }

    public void ResetDimTextPositions(int dim) => DimData[dim].Column = DimData[dim].Line = 0;

    /// <summary>
    /// displayText: control codes are 1 page break, 2 background colour, 6 text colour, 9 tab,
    /// 13 newline; anything else is a character that may push the line over the edge.
    /// </summary>
    public void DisplayText(string str)
    {
        var sd = _screen.Dims[_screen.CurDimIndex];
        var td = DimData[_screen.CurDimIndex];
        int fontWidth = _screen.Font.Width + _screen.CharSpacing;
        int fh = _screen.Font.Height + _screen.LineSpacing + td.LineSpacing;
        int lines = (sd.H - _screen.LineSpacing) / fh;
        int width = sd.W << 3;
        NumCharsPrinted = 0;
        string current = "";
        int lineWidth = 0;

        void ScrollIfNeeded()
        {
            while (td.Line >= lines)
            {
                if (lines - WaitButtonSpace <= LineCount && AllowPageBreak)
                {
                    LineCount = 0;
                    PageBreaks += 1;
                    NumCharsPrinted = 0;
                    _breakWanted = true;
                }
                int h1 = (sd.H / fh - 1) * fh;
                int h2 = sd.H - fh;
                if (h2 != 0) _screen.CopyRegion(sd.Sx << 3, sd.Sy + fh, sd.Sx << 3, sd.Sy, sd.W << 3, h2, _screen.CurPage, _screen.CurPage, true);
                _screen.FillRect(sd.Sx << 3, sd.Sy + h1, ((sd.Sx + sd.W) << 3) - 1, sd.Sy + sd.H - 1, td.Color2, _screen.CurPage);
                if (td.Line != 0) td.Line -= 1;
            }
        }

        bool PrintOneLine()
        {
            int s = current.Length;
            int lw = lineWidth;
            int w = width;
            if (lw + td.Column >= w)
            {
                // The last line of a box leaves room for the MORE button, so it breaks earlier than
                // the others. Without this the port wrapped 80 pixels wider than the engine.
                if (lines - 1 <= LineCount && AllowPageBreak) w -= 80;
                w -= td.Column;
                int lineLastCharPos = 0;
                int strPos = s - 1;
                bool printFlag = false;
                while (strPos > 0)
                {
                    char c = current[strPos];
                    lw -= _screen.CharWidth(current[strPos]);
                    if (lineLastCharPos == 0 && lw <= w) lineLastCharPos = strPos;
                    if (lineLastCharPos != 0 && c == ' ')
                    {
                        s = strPos;
                        printFlag = false;
                        break;
                    }
                    strPos -= 1;
                }
                if (strPos == 0)
                {
                    if (td.Column != 0 && !printFlag) { s = 0; lw = 0; }
                    else s = lineLastCharPos;
                }
            }
            string part = current[..Math.Clamp(s, 0, current.Length)];
            int x1 = (sd.Sx << 3) + td.Column;
            int y = sd.Sy + fh * td.Line;
            _screen.PrintText(part, x1, y, td.Color1, td.Color2);
            td.Column += lw;
            NumCharsPrinted += part.Length;
            string rest = current[Math.Clamp(s, 0, current.Length)..];
            if (s < current.Length && current[s] == ' ') rest = current[(s + 1)..];
            if (rest.Length > 0 && rest[0] == ' ') rest = rest[1..];
            current = rest;
            lineWidth = _screen.TextWidth(current) + td.CharSpacing * current.Length;
            if (current.Length == 0 && td.Column <= width) return false;
            td.Column = 0;
            td.Line += 1;
            LineCount += 1;
            return current.Length > 0;
        }

        void PrintLine()
        {
            do { ScrollIfNeeded(); } while (PrintOneLine());
        }

        for (int i = 0; i < str.Length; i += 1)
        {
            if (_breakWanted)
            {
                // Everything from here waits for the player. PrintLine already drew what fitted.
                _breakWanted = false;
                _pending = str;
                _pendingAt = i;
                AwaitingPageBreak = true;
                DrawPageBreakButton();
                return;
            }
            int c = str[i];
            switch (c)
            {
                case 1:
                    PrintLine();
                    PageBreaks += 1;
                    NumCharsPrinted = 0;
                    break;
                case 2:
                    PrintLine();
                    td.Color2 = (byte)str[++i];
                    break;
                case 6:
                    PrintLine();
                    td.Color1 = (byte)str[++i];
                    break;
                case 9:
                {
                    PrintLine();
                    int dv = td.Column / fontWidth;
                    dv = ((dv + 8) & 0xfff8) - 1;
                    if (dv >= width / fontWidth) dv = 0;
                    td.Column = fontWidth * dv;
                    break;
                }
                case 13:
                    PrintLine();
                    LineCount += 1;
                    td.Column = 0;
                    td.Line += 1;
                    break;
                default:
                    if (c == 0) break;
                    lineWidth += _screen.CharWidth(c) + td.CharSpacing;
                    current += str[i];
                    if (td.Column + lineWidth > width) PrintLine();
                    break;
            }
        }
        if (current.Length != 0) PrintLine();
        if (!_breakWanted) return;
        _breakWanted = false;
        _pending = "";
        _pendingAt = 0;
        AwaitingPageBreak = true;
        DrawPageBreakButton();
    }

    /// <summary>
    /// textPageBreak, the drawing half: the button in the corner of the text box. The waiting half is
    /// the host's, because this engine is stepped a frame at a time rather than blocking.
    /// </summary>
    private void DrawPageBreakButton()
    {
        var dim = _screen.Dims[_screen.CurDimIndex];
        string fromGame = Gui?.LangString(0x4073);
        if (!string.IsNullOrEmpty(fromGame)) PageBreakString = fromGame;
        string label = PageBreakString;
        int w = Gui?.DialogueButtonWidth ?? 74;
        int x = ((dim.Sx + dim.W) << 3) - (w + 3);
        int y = dim.Sy + dim.H - 10;
        _breakX = x;
        _breakY = y;
        _breakW = w;
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        string of = _screen.SetFont("6");
        Gui?.DrawBox(x, y, w, 9, 136, 251, 252);
        _screen.PrintText(label, x + (w >> 1) - (_screen.TextWidth(label) >> 1), y + 2,
                          Gui?.DialogueButtonLabelColor1 ?? 144, 0);
        _screen.SetFont(of);
        _screen.CurPage = cp;
        OnWaitingForPage?.Invoke(true, label);
        if (!HostAnswersPageBreak) ResumePageBreak();
    }

    /// <summary>
    /// The player has said go on: the button is wiped and the rest of the line printed - which may
    /// stop again at the next break, exactly as the engine does with a long enough line.
    /// </summary>
    public void ResumePageBreak()
    {
        if (!AwaitingPageBreak) return;
        AwaitingPageBreak = false;
        OnWaitingForPage?.Invoke(false, null);
        var td = DimData[_screen.CurDimIndex];
        _screen.FillRect(_breakX, _breakY, _breakX + _breakW - 1, _breakY + 8, td.Color2, 0);
        ClearCurDim();
        string rest = _pending ?? "";
        int at = _pendingAt;
        _pending = null;
        _pendingAt = 0;
        if (at < rest.Length) DisplayText(rest.Substring(at));
    }

    /// <summary>Whether the pointer is over the prompt.</summary>
    public bool PageBreakButtonAt(int x, int y) =>
        AwaitingPageBreak && x >= _breakX && x <= _breakX + _breakW && y >= _breakY && y <= _breakY + 9;

    /// <summary>
    /// printMessage: the status line under the view. The colour comes from the message type, and
    /// the line is cleared and redrawn on the live page.
    /// </summary>
    /// <summary>The host listens in, so the same line lands in the log under the picture.</summary>
    public Action<int, string> OnMessage;

    /// <summary>timerEnable(11): the line has been put up, so start counting until it fades.</summary>
    public Action EnableFadeTimer;

    public void PrintMessage(int type, string str, bool inDialogue = false)
    {
        OnMessage?.Invoke(type, str);
        byte[] textColors = { 0xfe, 0xa2, 0x84, 0x97, 0x9f };
        if ((type & 4) != 0) type &= ~4;
        int index = type & 0x7fff;
        byte col = textColors[index % textColors.Length];
        int od = _screen.CurDimIndex;
        if (inDialogue)
        {
            ClearDim(4);
            DimData[4].Color1 = col;
        }
        else
        {
            ClearDim(3);
            _screen.CopyColor(192, col);
            DimData[3].Color1 = 192;
            EnableFadeTimer?.Invoke();
        }
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        var of = _screen.SetFont("9");
        bool pageBreaks = AllowPageBreak;
        AllowPageBreak = false;
        DisplayText(str ?? "");
        AllowPageBreak = pageBreaks;
        _screen.SetFont(of);
        _screen.CurPage = cp;
        _screen.CurDimIndex = od;
    }
}

