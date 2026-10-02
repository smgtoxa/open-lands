// The intro and the outro (sequences_lol.cpp showIntro/showOutro).
//
// Transliterated from src/game/intro.mjs. Both are TIM scripts - LOLINTRO.TIM and LOLFINAL.TIM -
// run with their own opcode tables: captions come from a .DIP string table, the animations are
// composed on page 8 and copied to the screen through checkedPageUpdate, and the palette is walked
// towards its target a step per frame rather than faded in one go.
//
// The run loop is stepped by the host rather than looping inside, so a cinematic can be driven on
// a virtual clock and compared frame for frame with the JavaScript engine.
namespace LolCore;

public sealed class Cinematic
{
    private static readonly int[] TextPal =
    {
        0x00, 0x00, 0x00, 0x64, 0x64, 0x64, 0x61, 0x51, 0x30,
        0x29, 0x48, 0x64, 0x00, 0x4b, 0x3b, 0x64, 0x1e, 0x1e,
    };

    public static readonly string[] IntroPaks =
    {
        "INTRO1.PAK", "INTRO2.PAK", "INTRO3.PAK", "INTRO4.PAK",
        "INTRO5.PAK", "INTRO6.PAK", "INTRO7.PAK", "INTRO8.PAK", "INTROVOC.PAK",
    };

    public static readonly string[] OutroPaks = { "FINALE.PAK", "FINALE1.PAK", "FINALE2.PAK" };

    private readonly Screen _screen;
    private readonly Resources _res;
    private readonly TimInterpreter _tim;
    private readonly Func<double> _millis;
    private readonly Action<double> _advance;
    private readonly double _tickLength;

    public Cinematic(Screen screen, Resources resources, TimInterpreter tim, Func<double> millis, Action<double> advance, double tickLength)
    {
        _screen = screen;
        _res = resources;
        _tim = tim;
        _millis = millis;
        _advance = advance;
        _tickLength = tickLength;
    }

    /// <summary>Set by a key or a click: the cinematic stops at the next step.</summary>
    public bool Skip;

    /// <summary>Whether the speech in a caption is played. Text is drawn either way.</summary>
    public bool SpeechEnabled;
    public bool TextEnabled = true;

    /// <summary>The voice file a caption asked for, most recent last: the host plays them.</summary>
    public readonly List<string> Voices = new();

    // The fade the loop is in the middle of: how big a step to take, and how long to wait for it.
    private int _palDiff, _palDelayInc, _palDelayAcc;
    private double _palNext;
    private bool _outro;
    private byte[] _textBuffer;
    private bool _textShown;
    private TimScript _script;
    private string _savedFont;

    public TimScript Script => _script;
    public int PalDiff => _palDiff;

    /// <summary>The cinematic's own strings, as the TIM's TEXT chunk spells them.</summary>
    private string CineString(TimScript tim, int index) => _tim.TextString(tim, index);

    /// <summary>A line of the .DIP table the cinematic was loaded with.</summary>
    public string TableEntry(int index)
    {
        var d = _tim.LangData;
        if (d == null) return "";
        int at = d[index * 2] | (d[index * 2 + 1] << 8);
        var s = new System.Text.StringBuilder();
        while (at < d.Length && d[at] != 0) s.Append((char)d[at++]);
        return s.ToString();
    }

    /// <summary>Screen::getFadeParams: how big a step towards `pal` each frame takes.</summary>
    public void FadeParams(byte[] pal, int delay)
    {
        int maxDiff = 0;
        for (int i = 0; i < 768; i += 1) maxDiff = Math.Max(maxDiff, Math.Abs(pal[i] - _screen.ScreenPalette[i]));
        int delayInc = (delay << 8) & 0x7fff;
        if (maxDiff != 0) delayInc /= maxDiff;
        int step = delayInc;
        int diff = 1;
        for (; diff <= maxDiff; diff += 1)
        {
            if (delayInc >= 512) break;
            delayInc += step;
        }
        _palDelayInc = delayInc;
        _palDiff = diff;
        _palDelayAcc = 0;
    }

    /// <summary>Screen::fadePalStep: one step of the walk. False when there is nowhere left to go.</summary>
    public bool FadeStep(byte[] pal, int diff)
    {
        var sp = _screen.ScreenPalette;
        bool changed = false;
        for (int i = 0; i < 768; i += 1)
        {
            int c1 = pal[i], c2 = sp[i];
            if (c1 == c2) continue;
            changed = true;
            c2 = c1 > c2 ? Math.Min(c1, c2 + diff) : Math.Max(c1, c2 - diff);
            sp[i] = (byte)c2;
        }
        return changed;
    }

    /// <summary>The fifteen entries a caption is drawn in, dimmed from the colour the script asked for.</summary>
    public void SetupTextPalette(int index, int fadePalette)
    {
        var pal = _screen.Palette(0);
        for (int i = 0; i < 15; i += 1)
        {
            int p = (240 + i) * 3;
            for (int c = 0; c < 3; c += 1) pal[p + c] = (byte)((((15 - i) << 2) * TextPal[index * 3 + c]) / 100);
        }
        if (fadePalette == 0 && _palDiff == 0) _screen.SetScreenPalette(pal);
        else FadeParams(pal, fadePalette);
    }

    /// <summary>
    /// TIMInterpreter::displayText: one line of cinematic text, centred at y=160 (188 for the outro's
    /// own font). A line that starts with $VOICE$ names the spoken take of itself.
    /// </summary>
    public void DisplayText(int textId, int flags, int color)
    {
        string text = TableEntry(textId & 0x7fff);
        if (_textShown)
        {
            _screen.CopyBlockToPage(0, 0, 160, 320, 40, _textBuffer);
            _textShown = false;
        }
        if (string.IsNullOrEmpty(text)) return;
        if (text[0] == '$')
        {
            int end = text.IndexOf('$', 1);
            if (end > 0)
            {
                string voice = text.Substring(1, end - 1);
                if (SpeechEnabled && voice.Length != 0) Voices.Add(voice);
                text = text[(end + 1)..];
            }
        }
        SetupTextPalette(flags < 0 ? 1 : flags, 0);
        string cf = _screen.SetFont(flags < 0 ? "8" : "intro");
        var savedColors = _screen.TextColors;
        if (flags < 0) _screen.SetTextColor(new byte[] { 0x00, 0xf0, 0xfe, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        else _screen.SetTextColor(new byte[]
        {
            0x00, (byte)(color != 0 ? color : 0xf0), 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6,
            0xf7, 0xf8, 0xf9, 0xfa, 0, 0, 0, 0,
        });
        _textBuffer = _screen.CopyRegionToBuffer(0, 0, 160, 320, 40);
        _textShown = true;
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        int y = flags < 0 ? 188 : 160;
        foreach (string line in text.Split('\r'))
        {
            if (!TextEnabled) break;
            int width = _screen.TextWidth(line);
            _screen.PrintText(line, (320 - width) >> 1, y, (byte)(flags < 0 ? 0xf0 : color != 0 ? color : 0xf0), 0x00);
            y += _screen.FontHeight - 4;
        }
        _screen.CurPage = cp;
        _screen.SetTextColor(savedColors);
        _screen.SetFont(cf);
    }

    /// <summary>Screen_v2::wsaFrameAnimationStep: a scaled copy of a WSA frame (the intro's zooms).</summary>
    public void WsaFrameAnimationStep(int x1, int y1, int x2, int y2, int w1, int h1, int w2, int h2, int srcPage, int dstPage)
    {
        if (w1 == 0 || h1 == 0 || w2 == 0 || h2 == 0) return;
        var src = _screen.Page(srcPage);
        var dst = _screen.Page(dstPage);
        var row = new byte[w2];
        int last = -1;
        for (int yy = 0; yy < h2; yy += 1)
        {
            int t = yy * h1 / h2;
            if (t != last)
            {
                last = t;
                int s = (y1 + t) * 320 + x1;
                for (int xx = 0; xx < w2; xx += 1) row[xx] = src[s + xx * w1 / w2];
            }
            int dy = y2 + yy;
            if (dy < 0 || dy >= 200) continue;
            for (int xx = 0; xx < w2; xx += 1)
            {
                int dx = x2 + xx;
                if (dx >= 0 && dx < 320) dst[dy * 320 + dx] = row[xx];
            }
        }
    }

    /// <summary>The opcode table a cinematic's TIM is run with - a different one for each end of
    /// the game.</summary>
    public Func<TimScript, int[], int>[] Opcodes(bool outro)
    {
        int SetupPaletteFade(TimScript tim, int[] p) { FadeParams(_screen.Palette(0), p[0]); return 1; }
        int LoadPalette(TimScript tim, int[] p)
        {
            string name = CineString(tim, p[0]);
            if (_res.Exists(name)) Array.Copy(_res.Get(name), _screen.Palette(0), 768);
            return 1;
        }
        int SetupPaletteFadeEx(TimScript tim, int[] p)
        {
            _screen.CopyPalette(0, 1);
            FadeParams(_screen.Palette(0), p[0]);
            return 1;
        }
        int ProcessWsaFrame(TimScript tim, int[] p)
        {
            int animIndex = tim.Wsa[p[0]].Anim - 1;
            if (animIndex < 0) return 1;
            var anim = _tim.Animator.Animations[animIndex];
            if (anim?.Wsa == null) return 1;
            int factor = Math.Max(0, (int)(short)p[4]);
            int w1 = anim.Wsa.Width, h1 = anim.Wsa.Height;
            int w2 = w1 * factor / 100, h2 = h1 * factor / 100;
            _tim.Animator.DisplayFrame(animIndex, 2, p[1]);
            WsaFrameAnimationStep(anim.X, anim.Y, (short)p[2], (short)p[3], w1, h1, w2, h2, 2, 8);
            _screen.CheckedPageUpdate(8, 4);
            return 1;
        }
        int DisplayTextOp(TimScript tim, int[] p) { DisplayText(p[0], (short)p[1], outro ? p[2] : 0); return 1; }

        if (!outro)
            return new Func<TimScript, int[], int>[]
            {
                SetupPaletteFade, null, LoadPalette, SetupPaletteFadeEx, ProcessWsaFrame, DisplayTextOp, null, null,
            };

        return new Func<TimScript, int[], int>[]
        {
            SetupPaletteFade, null, LoadPalette, SetupPaletteFadeEx,
            null,
            (tim, p) =>            // fadeInScene: a new backdrop blended in through an overlay table
            {
                string sceneFile = CineString(tim, p[0]);
                string overlayFile = CineString(tim, p[1]);
                _screen.CopyRegion(0, 0, 0, 0, 320, 200, 0, 2, true);
                if (_res.Exists($"{sceneFile}.CPS")) _screen.LoadBitmap(_res.Get($"{sceneFile}.CPS"), 4, _screen.Palette(0));
                var overlay = _res.Exists(overlayFile) ? _res.Get(overlayFile) : null;
                for (int i = 0; i < 3; i += 1)
                {
                    if (overlay != null)
                        _screen.CopyBlockAndApplyOverlay(4, 0, 0, 2, 0, 0, 320, 200, 0, overlay.Skip(i * 256).Take(256).ToArray());
                    _screen.CopyRegion(0, 0, 0, 0, 320, 200, 2, 0, true);
                    _advance(10 * _tickLength);
                }
                _screen.CopyRegion(0, 0, 0, 0, 320, 200, 4, 0, true);
                return 1;
            },
            (tim, p) => 1, (tim, p) => 1,
            (tim, p) =>            // fadeInPalette
            {
                string name = CineString(tim, p[0]);
                var pal = new byte[768];
                if (_res.Exists(name)) _screen.LoadBitmap(_res.Get(name), 2, pal);
                _screen.FadePalette(pal, p[1], _advance);
                return 1;
            },
            null, null,
            (tim, p) => 1,         // fadeOutSound: the host's, not the screen's
            (tim, p) =>            // displayAnimFrame
            {
                int animIndex = tim.Wsa[p[0]].Anim - 1;
                if (animIndex < 0) return 1;
                var anim = _tim.Animator.Animations[animIndex];
                anim?.Wsa?.DisplayFrame(p[1], 0, anim.X, anim.Y);
                return 1;
            },
            (tim, p) => { _advance(p[0] * _tickLength); return 1; },   // delayForChat
            DisplayTextOp, null,
        };
    }

    /// <summary>
    /// Opens a cinematic: its PAKs, its fonts, its string table and its script. The run itself is
    /// the host's, one Step() at a time, so it can be skipped, paced or recorded.
    /// </summary>
    public bool Begin(string timName, string dipName, IEnumerable<string> paks, bool outro, string languageExt = "ENG", int character = 0)
    {
        foreach (string pak in paks) _res.LoadPak($"{languageExt}/{pak}");
        _res.LoadPak($"{languageExt}/STARTUP.PAK");
        if (!_res.Exists(timName)) return false;
        _outro = outro;
        _tim.IntroMode = true;
        _tim.DrawPage2 = outro ? 0 : 8;
        _tim.Finished = false;
        _tim.IsLoLOutro = outro;
        _tim.DisplayText = DisplayText;
        _tim.LangData = _res.Exists(dipName) ? _res.Get(dipName) : null;
        _palDiff = _palDelayInc = _palDelayAcc = 0;
        _palNext = 0;
        _textShown = false;
        Skip = false;
        Voices.Clear();
        Array.Clear(_screen.Palette(0), 0, 768);
        _screen.SetScreenPalette(_screen.Palette(0));
        foreach (int page in new[] { 0, 2, 4, 8 }) _screen.ClearPage(page);
        if (_res.Exists("NEW8P.FNT")) _screen.LoadFont("8", _res.Get("NEW8P.FNT"));
        if (_res.Exists("INTRO.FNT")) _screen.LoadFont("intro", _res.Get("INTRO.FNT"));
        _savedFont = _screen.SetFont(_screen.HasFont("8") ? "8" : "9");
        _script = _tim.Load(timName, Opcodes(outro));
        if (_script == null) return false;
        _script.LolCharacter = character;
        return true;
    }

    /// <summary>One pass of the cinematic loop: the script, the page copy and one palette step.
    /// False once the script has run out or the player has skipped it.</summary>
    public bool Step()
    {
        if (_script == null || _tim.Finished || Skip) return false;
        _tim.ExecStep(_script);
        if (!_outro) _screen.CheckedPageUpdate(8, 4);
        if (_palDiff != 0 && _palNext < _millis())
        {
            _palDelayAcc += _palDelayInc;
            _palNext = _millis() + (_palDelayAcc >> 8) * _tickLength;
            _palDelayAcc &= 0xff;
            if (!FadeStep(_screen.Palette(0), _palDiff))
            {
                _screen.SetScreenPalette(_screen.Palette(0));
                _palDiff = 0;
            }
        }
        _advance(10);
        return !_tim.Finished && !Skip;
    }

    /// <summary>Puts back what the cinematic borrowed, and fades what is left to black.</summary>
    public void End()
    {
        for (int i = 0; i < 6; i += 1) _tim.Animator.Reset(i, true);
        _tim.LangData = null;
        _tim.IntroMode = false;
        _tim.IsLoLOutro = false;
        _tim.DisplayText = null;
        _tim.DrawPage2 = 0;
        _tim.Finished = false;
        _tim.CurrentTim = null;
        if (_savedFont != null) _screen.SetFont(_savedFont);
        _script = null;
        _screen.FadeToBlack(20, _advance);
    }

    /// <summary>One line of the credits and where it sits on the roll.</summary>
    public sealed class CreditLine
    {
        public string Text = "";
        public int X, Y;
    }

    /// <summary>
    /// CREDITS.TXT, laid out: an entry ends with \x05 (the next one stays on the same line) or \r
    /// (a new line); a leading \x03 aligns it left, \x04 right, anything else centres it.
    /// </summary>
    public (List<CreditLine> Lines, int Total, int LineHeight) LayOutCredits()
    {
        var lines = new List<CreditLine>();
        if (!_res.Exists("CREDITS.TXT")) return (lines, 0, 0);
        var raw = _res.Get("CREDITS.TXT");
        var text = new System.Text.StringBuilder(raw.Length);
        foreach (byte b in raw) text.Append((char)b);
        string cf = _screen.SetFont("6");
        int lh = _screen.FontHeight;
        int gap = lh >> 3;
        int y = 200;
        bool sameLine = false;
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (char ch in text.ToString())
        {
            current.Append(ch);
            if (ch != '\x05' && ch != '\r') continue;
            parts.Add(current.ToString());
            current.Clear();
        }
        if (current.Length != 0) parts.Add(current.ToString());
        foreach (string part in parts)
        {
            if (part.Length == 0) continue;
            char code = part[^1];
            string body = code is '\x05' or '\r' ? part[..^1] : part;
            int align = 0;
            if (body.Length != 0 && (body[0] == '\x03' || body[0] == '\x04'))
            {
                align = body[0];
                body = body[1..];
            }
            if (body.Length != 0 && (body[0] == '\x01' || body[0] == '\x02')) body = body[1..];
            body = new string(body.Where(c => c < 0x01 || c > 0x1f).ToArray());
            if (!sameLine && lines.Count != 0) y += lh + gap;
            int width = _screen.TextWidth(body);
            int x = align == 3 ? 0 : align == 4 ? 300 - width : (320 - width) >> 1;
            if (body.Length != 0) lines.Add(new CreditLine { Text = body, X = x, Y = y });
            sameLine = code == '\x05';
        }
        _screen.SetFont(cf);
        return (lines, y + lh + 40, lh);
    }

    /// <summary>One frame of the credit roll, scrolled by `scroll` pixels.</summary>
    public void DrawCredits(List<CreditLine> lines, int lineHeight, int scroll)
    {
        string cf = _screen.SetFont("6");
        int cp = _screen.CurPage;
        _screen.CurPage = 0;
        _screen.ClearPage(0);
        foreach (var e in lines)
        {
            int yy = e.Y - scroll;
            if (yy < 0 || yy > 200 - lineHeight) continue;
            _screen.PrintText(e.Text, e.X, yy, 0xf0, 0);
        }
        _screen.CurPage = cp;
        _screen.SetFont(cf);
    }

    /// <summary>The palette the credit roll is shown in: black, with one warm colour for the text.</summary>
    public void SetupCreditsPalette()
    {
        var pal = _screen.Palette(0);
        Array.Clear(pal, 0, 768);
        pal[0xf0 * 3] = 0x3f;
        pal[0xf0 * 3 + 1] = 0x3a;
        pal[0xf0 * 3 + 2] = 0x28;
        _screen.SetScreenPalette(pal);
    }
}
