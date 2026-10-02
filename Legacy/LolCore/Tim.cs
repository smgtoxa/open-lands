// TIM: the little interpreter the cutscenes and in-game conversations are written in.
//
// Transliterated from src/game/tim.mjs (decodeTim, TimAnimator, TimInterpreter) - itself a port of
// ScummVM's script_tim.cpp / animator_tim.cpp.
//
// A TIM script is ten independent functions running side by side, each with its own instruction
// pointer and its own "not before this moment" clock. That clock is the wall clock in the original;
// here it is whatever clock the host drives, which is what makes a cutscene reproducible.
namespace LolCore;

public sealed class TimScript
{
    public string Filename = "";
    public int ClickedButton;
    public int DlgFunc = -1;
    public int ProcFunc = -1;
    public int ProcParam;
    public int LolCharacter;
    public ushort[] Avtl = Array.Empty<ushort>();
    public byte[] Text = Array.Empty<byte>();
    public Func<TimScript, int[], int>[] Opcodes;
    public readonly TimFunc[] Func = Enumerable.Range(0, 10).Select(_ => new TimFunc()).ToArray();
    public readonly TimWsaSlot[] Wsa = Enumerable.Range(0, 6).Select(_ => new TimWsaSlot()).ToArray();

    public sealed class TimFunc
    {
        public int Ip = -1;
        public double LastTime, NextTime;
        public int LoopIp = -1;
        public int Avtl = -1;
    }

    public sealed class TimWsaSlot
    {
        public int Anim, X, Y, WsaFlags, Offscreen;
    }
}

/// <summary>The animator: six WSA slots, each stepping through parts of its animation on the clock.</summary>
public sealed class TimAnimator
{
    public sealed class Part
    {
        public int FirstFrame, LastFrame, Cycles, NextPart, PartDelay, FieldA, SfxIndex, SfxFrame;
    }

    public sealed class Animation
    {
        public WsaPlayer Wsa;
        public int X, Y, WsaCopyParams, FrameDelay, Enable, LastPart = -1, CurPart, CurFrame;
        public double NextFrame;
        public int CyclesCompleted, FieldD;
        public Part[] Parts = Enumerable.Range(0, 10).Select(_ => new Part()).ToArray();
    }

    private readonly Screen _screen;
    private readonly Func<double> _millis;
    private readonly double _tickLength;
    public readonly Animation[] Animations = Enumerable.Range(0, 6).Select(_ => new Animation()).ToArray();

    public TimAnimator(Screen screen, Func<double> millis, double tickLength)
    {
        _screen = screen;
        _millis = millis;
        _tickLength = tickLength;
    }

    public void Init(int animIndex, WsaPlayer wsa, int x, int y, int wsaCopyParams, int frameDelay)
    {
        var anim = Animations[animIndex];
        anim.Wsa = wsa;
        anim.X = x;
        anim.Y = y;
        anim.WsaCopyParams = wsaCopyParams;
        anim.FrameDelay = frameDelay;
        anim.Enable = 0;
        anim.LastPart = -1;
    }

    public void Reset(int animIndex, bool clearStruct)
    {
        var anim = Animations[animIndex];
        anim.FieldD = 0;
        anim.Enable = 0;
        anim.Wsa = null;
        if (clearStruct) Animations[animIndex] = new Animation();
    }

    public void DisplayFrame(int animIndex, int page, int frame, int flags = -1)
    {
        var anim = Animations[animIndex];
        if ((anim.WsaCopyParams & 0x4000) != 0) page = 2;
        anim.Wsa?.DisplayFrame(frame, page, anim.X, anim.Y, flags == -1 ? anim.WsaCopyParams & 0xf0ff : flags);
    }

    public void SetupPart(int animIndex, int part, int firstFrame, int lastFrame, int cycles, int nextPart, int partDelay, int f, int sfxIndex, int sfxFrame)
    {
        var p = Animations[animIndex].Parts[part];
        p.FirstFrame = firstFrame;
        p.LastFrame = lastFrame;
        p.Cycles = cycles;
        p.NextPart = nextPart;
        p.PartDelay = partDelay;
        p.FieldA = f;
        p.SfxIndex = sfxIndex;
        p.SfxFrame = sfxFrame;
    }

    public void Start(int animIndex, int part)
    {
        var anim = Animations[animIndex];
        anim.CurPart = part;
        var p = anim.Parts[part];
        anim.Enable = 1;
        anim.NextFrame = _millis() + anim.FrameDelay * _tickLength;
        anim.CurFrame = p.FirstFrame;
        anim.CyclesCompleted = 0;
        anim.Wsa?.DisplayFrame(anim.CurFrame - 1, 0, anim.X, anim.Y);
    }

    public void Stop(int animIndex)
    {
        var anim = Animations[animIndex];
        anim.Enable = 0;
        anim.FieldD = 0;
        if (animIndex == 5) anim.Wsa = null;
    }

    /// <summary>One animation's turn: the next frame, if its moment has come.</summary>
    /// <summary>Bumped every time a slot puts a frame up, so a host knows the picture moved.</summary>
    public int Serial { get; private set; }

    /// <summary>snd_playSoundEffect(index, -1): a part's own sound, played on its frame.</summary>
    public Action<int> PlaySoundEffect;

    public void Update(int animIndex)
    {
        var anim = Animations[animIndex];
        if (anim.Enable == 0 || anim.NextFrame >= _millis()) return;
        Serial += 1;
        var p = anim.Parts[anim.CurPart];
        anim.NextFrame = 0;
        int step;
        if (p.LastFrame >= p.FirstFrame) { step = 1; anim.CurFrame += 1; }
        else { step = -1; anim.CurFrame -= 1; }
        if (anim.CurFrame == p.LastFrame + step)
        {
            anim.CyclesCompleted += 1;
            if (anim.CyclesCompleted > p.Cycles || anim.FieldD != 0)
            {
                anim.LastPart = anim.CurPart;
                if (p.NextPart == -1 || p.NextPart == 0xffff || (anim.FieldD != 0 && p.FieldA != 0))
                {
                    anim.Enable = 0;
                    anim.FieldD = 0;
                    return;
                }
                anim.NextFrame += p.PartDelay * _tickLength;
                anim.CurPart = p.NextPart;
                p = anim.Parts[anim.CurPart];
                anim.CurFrame = p.FirstFrame;
                anim.CyclesCompleted = 0;
            }
            else anim.CurFrame = p.FirstFrame;
        }
        if (p.SfxIndex != -1 && p.SfxIndex != 0xffff && p.SfxFrame == anim.CurFrame) PlaySoundEffect?.Invoke(p.SfxIndex);
        anim.NextFrame += anim.FrameDelay * _tickLength;
        anim.Wsa?.DisplayFrame(anim.CurFrame - 1, 0, anim.X, anim.Y);
        anim.NextFrame += _millis();
        _ = _screen;
    }

    public int ResetLastPart(int animIndex)
    {
        var anim = Animations[animIndex];
        int res = anim.LastPart;
        anim.LastPart = -1;
        return res;
    }
}

public sealed class TimInterpreter
{
    private const int CountFuncs = 10;

    private readonly Screen _screen;
    private readonly Resources _res;
    private readonly Func<double> _millis;
    private readonly double _tickLength;

    public TimScript CurrentTim;
    public int CurrentFunc;
    public bool Finished;
    public int AbortFlag;
    public readonly List<string> VocFiles = Enumerable.Repeat("", 8).ToList();

    /// <summary>The host's sound: snd_loadSoundFileByName, snd_playTrack, snd_playSoundEffect(i, -1).</summary>
    public Action<string> LoadSoundFileByName;
    public Action<int> MusicTrack;
    public Action<int> SoundEffect;

    private void SetVocFile(int index, string name)
    {
        while (VocFiles.Count <= index) VocFiles.Add("");
        VocFiles[index] = name;
    }
    public int DrawPage2;

    /// <summary>Set while the intro or the outro is running: the WSA slots are opened the
    /// cinematic way and the captions come from the .DIP table rather than a level's strings.</summary>
    public bool IntroMode;

    /// <summary>LOLINTRO.DIP / LOLFINAL.DIP: the cinematic's own string table.</summary>
    public byte[] LangData;

    /// <summary>Set by the host: play this voice file (a .VOC beside the speech archives).</summary>
    public Action<string> PlayVoice;

    /// <summary>Where a caption goes while a cinematic is running (Cinematic.DisplayText).</summary>
    public Action<int, int, int> DisplayText;

    /// <summary>Set on the outro's script: its captions are drawn in a different colour.</summary>
    public bool IsLoLOutro;
    public readonly TimAnimator Animator;

    /// <summary>What the interpreter reaches back into: the interface and the party.</summary>
    public Gui Gui;
    public LevelLoader Loader;

    /// <summary>The commands this port carries out, by number, for a run that wants to know.</summary>
    public readonly List<int> UnhandledCommands = new();

    public TimInterpreter(Screen screen, Resources resources, Func<double> millis, double tickLength)
    {
        _screen = screen;
        _res = resources;
        _millis = millis;
        _tickLength = tickLength;
        Animator = new TimAnimator(screen, millis, tickLength);
    }

    /// <summary>decodeTim: the TEXT and AVTL chunks of an IFF-ish container.</summary>
    public static (byte[] Text, ushort[] Avtl) Decode(byte[] bytes)
    {
        int offset = 12;
        byte[] text = null;
        ushort[] avtl = null;
        while (offset + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            int size = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16) | (bytes[offset + 6] << 8) | bytes[offset + 7];
            if (id == "TEXT") text = bytes.Skip(offset + 8).Take(size).ToArray();
            else if (id == "AVTL")
            {
                avtl = new ushort[size >> 1];
                for (int i = 0; i < avtl.Length; i += 1)
                    avtl[i] = (ushort)(bytes[offset + 8 + i * 2] | (bytes[offset + 9 + i * 2] << 8));
            }
            offset += 8 + size + (size & 1);
        }
        if (avtl == null) throw new InvalidDataException("TIM lacks AVTL");
        return (text ?? Array.Empty<byte>(), avtl);
    }

    public TimScript Load(string name, Func<TimScript, int[], int>[] opcodes)
    {
        if (!_res.Exists(name)) return null;
        var (text, avtl) = Decode(_res.Get(name));
        var tim = new TimScript { Filename = name, Avtl = avtl, Text = text, Opcodes = opcodes };
        int num = Math.Min(avtl.Length, CountFuncs);
        for (int i = 0; i < num; i += 1) tim.Func[i].Avtl = avtl[i];
        return tim;
    }

    public string TextString(TimScript tim, int index)
    {
        var text = tim.Text;
        int at = text[index << 1] | (text[(index << 1) + 1] << 8);
        var s = new System.Text.StringBuilder();
        while (at < text.Length && text[at] != 0) s.Append((char)text[at++]);
        return s.ToString();
    }

    /// <summary>
    /// execTim, one pass: every function whose moment has come runs until it yields. The host calls
    /// this on its own clock instead of looping inside, so a cutscene can be stepped.
    /// </summary>
    public int ExecStep(TimScript tim)
    {
        CurrentTim = tim;
        if (tim.Func[0].Ip < 0)
        {
            tim.Func[0].Ip = tim.Func[0].Avtl;
            tim.Func[0].NextTime = tim.Func[0].LastTime = _millis();
        }
        for (CurrentFunc = 0; CurrentFunc < CountFuncs; CurrentFunc += 1)
        {
            var cur = tim.Func[CurrentFunc];
            if (tim.ProcFunc != -1) ExecCommand(28, new[] { tim.ProcParam });
            CheckSpeechProgress();
            bool running = true;
            int cnt = 0;
            while (cur.Ip >= 0 && cur.Ip + 2 < tim.Avtl.Length && cur.NextTime <= _millis() && running)
            {
                if (cnt++ > 0 && tim.ProcFunc != -1) ExecCommand(28, new[] { tim.ProcParam });
                int opcode = (sbyte)tim.Avtl[cur.Ip + 2];
                var param = tim.Avtl.Skip(cur.Ip + 3).Select(v => (int)v).ToArray();
                int result = ExecCommand(opcode, param);
                if (result == -1)
                {
                    running = false;
                    CurrentFunc = 11;
                    return -1;
                }
                if (result == -2) running = false;
                else if (result == -3)
                {
                    tim.ProcFunc = CurrentFunc;
                    tim.DlgFunc = -1;
                }
                else if (result == 22) cur.LoopIp = -1;
                if (cur.Ip >= 0)
                {
                    cur.Ip += tim.Avtl[cur.Ip];
                    cur.LastTime = cur.NextTime;
                    // Past the end of the script tim.mjs reads undefined, and a NaN time never comes
                    // due: the function simply stops. Indexing past the end here threw instead.
                    cur.NextTime = cur.Ip + 2 < tim.Avtl.Length ? cur.NextTime + tim.Avtl[cur.Ip + 1] * _tickLength : double.PositiveInfinity;
                }
            }
        }
        return tim.ClickedButton;
    }

    /// <summary>Whether any function still has somewhere to go.</summary>
    public bool Running(TimScript tim) => tim != null && tim.Func.Any(f => f.Ip >= 0);

    /// <summary>Set to trace what a script does, command by command.</summary>
    public Action<int, int[]> Trace;

    public int ExecCommand(int cmd, int[] param)
    {
        Trace?.Invoke(cmd, param);
        var tim = CurrentTim;
        switch (cmd)
        {
            case 0: return CmdInitFunc0();
            case 1: return CmdStopAllFuncs();
            case 2: return CmdInitWsa(param);
            case 3: return CmdUninitWsa(param);
            case 4: return CmdInitFunc(param);
            case 5: tim.Func[param[0]].Ip = -1; return 1;
            case 6: Animator.DisplayFrame(param[0], DrawPage2, param[1]); return 1;
            case 7:
                if (IntroMode) DisplayText?.Invoke(param[0], (short)param[1], IsLoLOutro ? 0xf2 : 0);
                return 1;
            case 8: SetVocFile(param[1], TextString(tim, param[0])); return 1;
            case 9: SetVocFile(param[0], ""); return 1;
            case 10:                                // playVocFile: a named take, else a sound effect
                if (param[0] >= 0 && param[0] < VocFiles.Count && VocFiles[param[0]].Length != 0)
                    PlayVoice?.Invoke(VocFiles[param[0]]);
                else SoundEffect?.Invoke(param[0]);
                return 1;
            case 12: LoadSoundFileByName?.Invoke(TextString(tim, param[0])); return 1;
            case 13: return 1;
            case 14: MusicTrack?.Invoke(param[0]); return 1;
            case 16: case 17: return 1;
            case 20: return CmdSetLoopIp();
            case 21: return CmdContinueLoop(param);
            case 22: tim.Func[CurrentFunc].LoopIp = -1; return 1;
            case 23: return CmdResetAllRuntimes();
            case 24: return 1;
            case 25: return CmdExecOpcode(param);
            case 26: return CmdInitFuncNow(param);
            case 27: return CmdStopFuncNow(param);
            case 28: return CmdProcessDialogue();
            case 29: return CmdDialogueBox(param);
            case 30: return -1;
            default:
                UnhandledCommands.Add(cmd);
                return 0;
        }
    }

    public void StopAllFuncs(TimScript tim)
    {
        if (tim == null) return;
        foreach (var f in tim.Func) f.Ip = -1;
    }

    /// <summary>advanceToOpcode: skip this function forward to the next command of a given kind.</summary>
    public void AdvanceToOpcode(int opcode)
    {
        var tim = CurrentTim;
        var f = tim.Func[tim.DlgFunc];
        int len = tim.Avtl[f.Ip];
        while ((tim.Avtl[f.Ip + 2] & 0xff) != opcode)
        {
            if ((tim.Avtl[f.Ip + 2] & 0xff) == 1)
            {
                tim.Avtl[f.Ip] = (ushort)len;
                break;
            }
            len = tim.Avtl[f.Ip];
            f.Ip += len;
        }
        f.NextTime = _millis();
    }

    public void ResetDialogueState(TimScript tim)
    {
        if (tim == null) return;
        tim.ProcFunc = 0;
        tim.ProcParam = Gui != null && Gui.DialogueNumButtons != 0 ? Gui.DialogueNumButtons : 1;
        tim.ClickedButton = 0;
        tim.DlgFunc = -1;
    }

    public int FreeAnimStruct(int index)
    {
        Animator.Reset(index, true);
        return 1;
    }

    /// <summary>initAnimStruct: open the .WSA a slot plays, and put its first frame up.</summary>
    public int InitAnimStruct(int index, string filename, int x, int y, int frameDelay, int unused, int wsaFlags)
    {
        _ = unused;
        if (IntroMode) return InitAnimStructCinematic(index, filename, x, y, unused, wsaFlags);
        WsaPlayer wsa = null;
        int wsaOpenFlags = 0;
        if ((wsaFlags & 0x10) != 0) wsaOpenFlags |= 2;
        if ((wsaFlags & 8) != 0) wsaOpenFlags |= 1;
        string file = $"{filename}.WSA";
        if (_res.Exists(file))
        {
            wsa = new WsaPlayer(_screen) { Name = file.ToUpperInvariant() };
            wsa.Open(_res.Get(file), wsaOpenFlags, _screen.Palette(3));
        }
        if ((wsaFlags & 1) != 0)
        {
            if (_screen.FadeFlag != 1) _screen.FadeClearSceneWindow(10, Wait);
            var p0 = _screen.Palette(0);
            var p3 = _screen.Palette(3);
            Array.Copy(p0, 128 * 3, p3, 128 * 3, p0.Length - 128 * 3);
        }
        else if ((wsaFlags & 2) != 0) _screen.FadeToBlack(10, Wait);
        if ((wsaFlags & 3) != 0)
        {
            var p3 = _screen.Palette(3);
            _screen.LoadSpecialColors(p3);
            _screen.FadePalette(p3, 10, Wait);
            _screen.FadeFlag = 0;
        }
        if (wsa != null && (wsaFlags & 7) != 0) wsa.DisplayFrame(0, 0, x, y);
        Animator.Init(index, wsa, x, y, wsaFlags, frameDelay);
        return index + 1;
    }

    /// <summary>initAnimStructCinematic: the intro's and the outro's way of opening a slot.</summary>
    public int InitAnimStructCinematic(int index, string filename, int x, int y, int offscreenBuffer, int wsaFlags)
    {
        int page2 = DrawPage2;
        WsaPlayer wsa = null;
        int wsaOpenFlags = 1;
        if ((wsaFlags & 0x10) != 0) wsaOpenFlags |= 2;
        if (offscreenBuffer == 2) wsaOpenFlags = 1;
        string file = $"{filename}.WSA";
        if (_res.Exists(file))
        {
            wsa = new WsaPlayer(_screen) { Name = file.ToUpperInvariant() };
            wsa.Open(_res.Get(file), wsaOpenFlags, index == 1 ? _screen.Palette(0) : null);
        }
        if (x == -1) x = 0;
        if (y == -1) y = 0;
        if ((wsaFlags & 2) != 0)
        {
            _screen.FadePalette(_screen.Palette(1), 15, Wait);
            _screen.ClearPage(page2);
            if (page2 != 0) _screen.CheckedPageUpdate(8, 4);
        }
        if ((wsaFlags & 4) != 0 || wsa == null)
        {
            string cps = $"{filename}.CPS";
            if (_res.Exists(cps))
            {
                _screen.LoadBitmap(_res.Get(cps), 2, _screen.Palette(0));
                _screen.CopyRegion(0, 0, 0, 0, 320, 200, 2, page2, true);
                if (page2 != 0) _screen.CheckedPageUpdate(8, 4);
            }
            if (wsa != null && (wsaFlags & 4) != 0) wsa.DisplayFrame(0, page2, x, y);
        }
        if ((wsaFlags & 2) != 0) _screen.FadePalette(_screen.Palette(0), 30, Wait);
        Animator.Init(index, wsa, x, y, wsaFlags, 0);
        return index + 1;
    }
    // ---- commands ----
    private int CmdInitFunc0()
    {
        var tim = CurrentTim;
        for (int i = 0; i < 6; i += 1) tim.Wsa[i] = new TimScript.TimWsaSlot();
        tim.Func[0].Ip = tim.Func[0].Avtl;
        tim.Func[0].LastTime = _millis();
        return 1;
    }

    /// <summary>
    /// stopAllFuncs. The engine spins here until the player answers the dialogue; a stepped run
    /// cannot spin, so it stops the script and lets the host keep asking.
    /// </summary>
    private int CmdStopAllFuncs()
    {
        var tim = CurrentTim;
        if (tim.DlgFunc == -1 && tim.ClickedButton == 0)
        {
            tim.ClickedButton = Gui?.ProcessDialogue(false) ?? 1;
            if (tim.ClickedButton == 0) return -2;
        }
        foreach (var f in tim.Func) f.Ip = -1;
        return -1;
    }

    private int CmdInitWsa(int[] param)
    {
        var tim = CurrentTim;
        int index = param[0];
        var slot = tim.Wsa[index];
        slot.X = (short)param[2];
        slot.Y = (short)param[3];
        slot.Offscreen = param[4];
        slot.WsaFlags = param[5];
        string filename = TextString(tim, param[1]);
        slot.Anim = InitAnimStruct(index, filename, slot.X, slot.Y, 10, slot.Offscreen, slot.WsaFlags);
        return 1;
    }

    private int CmdUninitWsa(int[] param)
    {
        var tim = CurrentTim;
        int index = param[0];
        var slot = tim.Wsa[index];
        if (slot.Anim == 0) return 0;
        if (slot.Offscreen != 0)
        {
            Animator.Reset(index, false);
            slot.Anim = 0;
        }
        else
        {
            Animator.Reset(index, true);
            tim.Wsa[index] = new TimScript.TimWsaSlot();
        }
        return 1;
    }

    private int CmdInitFunc(int[] param)
    {
        var tim = CurrentTim;
        int func = param[0];
        if (tim.Func[func].Avtl >= 0) tim.Func[func].Ip = tim.Func[func].Avtl;
        else tim.Func[func].Avtl = tim.Func[func].Ip = tim.Avtl[func];
        return 1;
    }

    /// <summary>
    /// setLoopIp: hold this function here while the line is being spoken. With speech on but
    /// nothing playing - which is the port's case - the engine skips ahead to the matching
    /// "continue" instead, and so does this.
    /// </summary>
    private int CmdSetLoopIp()
    {
        var tim = CurrentTim;
        if (SpeechEnabled)
        {
            if (SpeechPlaying?.Invoke() ?? false) tim.Func[CurrentFunc].LoopIp = tim.Func[CurrentFunc].Ip;
            else AdvanceToOpcodeSafe(21);
        }
        else tim.Func[CurrentFunc].LoopIp = tim.Func[CurrentFunc].Ip;
        return 1;
    }

    /// <summary>
    /// How the host is told that time passed inside a command: the engine's fades and waits move
    /// its clock, and a script's next instruction is due at a moment measured on that clock.
    /// </summary>
    public Action<double> Wait;

    /// <summary>Whether the game is set to speak its lines, and whether one is playing now.</summary>
    public bool SpeechEnabled;
    public Func<bool> SpeechPlaying;

    private void AdvanceToOpcodeSafe(int opcode)
    {
        var tim = CurrentTim;
        tim.DlgFunc = CurrentFunc;
        AdvanceToOpcode(opcode);
        tim.DlgFunc = -1;
    }

    /// <summary>
    /// checkSpeechProgress: a line that has finished playing lets its function move on, which is
    /// how a conversation keeps going when the player says nothing.
    /// </summary>
    private void CheckSpeechProgress()
    {
        var tim = CurrentTim;
        if (!SpeechEnabled || tim.ProcParam <= 1 || tim.Func[CurrentFunc].LoopIp < 0) return;
        if (SpeechPlaying?.Invoke() ?? false) return;
        tim.Func[CurrentFunc].LoopIp = -1;
        tim.DlgFunc = CurrentFunc;
        AdvanceToOpcode(21);
        tim.DlgFunc = -1;
        Animator.Reset(5, false);
    }

    private int CmdContinueLoop(int[] param)
    {
        _ = param;
        var func = CurrentTim.Func[CurrentFunc];
        if (func.LoopIp < 0) return -2;
        func.Ip = func.LoopIp;
        return -2;
    }

    private int CmdResetAllRuntimes()
    {
        foreach (var f in CurrentTim.Func) if (f.Ip >= 0) f.NextTime = _millis();
        return 1;
    }

    private int CmdExecOpcode(int[] param)
    {
        var tim = CurrentTim;
        int opcode = param[0];
        var proc = tim.Opcodes != null && opcode < tim.Opcodes.Length ? tim.Opcodes[opcode] : null;
        if (proc == null)
        {
            UnhandledCommands.Add(1000 + opcode);
            return 0;
        }
        return proc(tim, param.Skip(1).ToArray());
    }

    private int CmdInitFuncNow(int[] param)
    {
        var f = CurrentTim.Func[param[0]];
        f.Ip = f.Avtl;
        f.LastTime = f.NextTime = _millis();
        return 1;
    }

    private int CmdStopFuncNow(int[] param)
    {
        var f = CurrentTim.Func[param[0]];
        f.Ip = -1;
        f.LastTime = f.NextTime = _millis();
        return 1;
    }

    private int CmdProcessDialogue()
    {
        var tim = CurrentTim;
        int res = Gui?.ProcessDialogue(DialoguePressed) ?? 0;
        DialoguePressed = false;
        if (res == 0 || tim.ProcParam == 0) return res;
        tim.Func[tim.ProcFunc].LoopIp = -1;
        tim.DlgFunc = tim.ProcFunc;
        tim.ProcFunc = -1;
        tim.ClickedButton = res;
        Animator.Reset(5, false);
        if (tim.ProcParam != 0) AdvanceToOpcode(21);
        return res;
    }

    /// <summary>Set by the host when the pointer button went down this tick.</summary>
    public bool DialoguePressed;

    private int CmdDialogueBox(int[] param)
    {
        var tim = CurrentTim;
        tim.ProcParam = param[0];
        tim.ClickedButton = 0;
        var strings = new string[3];
        int cnt = 0;
        for (int i = 1; i < 4; i += 1)
        {
            if (param[i] == 0xffff) continue;
            strings[i - 1] = Gui != null ? GameStrings.Get(param[i], Gui.LandsFile, Gui.LevelLangFile) : "";
            cnt += 1;
        }
        Gui?.SetupDialogueButtons(cnt, strings[0], strings[1], strings[2]);
        return -3;
    }
}
