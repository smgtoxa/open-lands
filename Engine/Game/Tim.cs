// src/game/tim.mjs: TIM script interpreter and animator for in-game dialogues/cutscenes (script_tim.cpp, animator_tim.cpp).
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>tim.mjs decodeTim: { text, avtl }</summary>
    public sealed class TimFile
    {
        public byte[] text;
        public ushort[] avtl;
    }

    /// <summary>tim.mjs makeAnimation: parts[] entries</summary>
    public sealed class TimAnimPart
    {
        public int firstFrame, lastFrame, cycles, nextPart, partDelay, field_A, sfxIndex, sfxFrame;
    }

    /// <summary>tim.mjs makeAnimation</summary>
    public sealed class TimAnimation
    {
        public WsaPlayer wsa;
        public int x, y, wsaCopyParams, frameDelay, enable, lastPart = -1, curPart, curFrame;
        public double nextFrame;
        public int cyclesCompleted, field_D;
        public TimAnimPart[] parts;
    }

    /// <summary>tim.mjs load: tim.func[] entries</summary>
    public sealed class TimFunc
    {
        public int ip = -1;
        public double lastTime, nextTime;
        public int loopIp = -1, avtl = -1;
    }

    /// <summary>tim.mjs load / cmd_initFunc0: tim.wsa[] entries</summary>
    public sealed class TimWsa
    {
        public int anim, x, y, wsaFlags, offscreen;
    }

    /// <summary>tim.mjs load: the loaded script (`tim`)</summary>
    public sealed class TimScript
    {
        public string filename;
        public int clickedButton, dlgFunc = -1, procFunc = -1, procParam;
        public ushort[] avtl;
        public byte[] text;
        public Func<TimScript, Span16, Task<int>>[] opcodes;
        public TimFunc[] func;
        public TimWsa[] wsa;
        public bool isLoLOutro;
        public int lolCharacter;
    }

    public sealed partial class LandsOfLore
    {
        public static TimFile decodeTim(byte[] bytes)
        {
            int offset = 12;
            byte[] text = null;
            ushort[] avtl = null;
            while (offset + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, offset, 4);
                // view.getUint32(offset + 4): big-endian
                int size = (int)((uint)(bytes[offset + 4] << 24) | (uint)(bytes[offset + 5] << 16) | (uint)(bytes[offset + 6] << 8) | bytes[offset + 7]);
                var data = Js.Slice(bytes, offset + 8, offset + 8 + size);
                if (id == "TEXT") text = data;
                else if (id == "AVTL")
                {
                    avtl = new ushort[size >> 1];
                    for (int i = 0; i < avtl.Length; i += 1) avtl[i] = BitConverter.ToUInt16(bytes, offset + 8 + i * 2);
                }
                offset += 8 + size + (size & 1);
            }
            if (avtl == null) throw new Exception("TIM lacks AVTL");
            return new TimFile { text = text ?? new byte[0], avtl = avtl };
        }

        internal static void advanceToOpcodeSafe(TimInterpreter interp, int opcode)
        {
            var tim = interp.currentTim;
            tim.dlgFunc = interp.currentFunc;
            interp.advanceToOpcode(opcode);
            tim.dlgFunc = -1;
        }
    }

    public sealed class TimAnimator
    {
        public LandsOfLore vm;
        public Screen screen;
        public TimAnimation[] animations;

        public TimAnimator(LandsOfLore vm)
        {
            this.vm = vm;
            this.screen = vm.screen;
            this.animations = new TimAnimation[TimInterpreter.WSA_SLOTS];
            for (int i = 0; i < this.animations.Length; i += 1) this.animations[i] = this.makeAnimation();
        }

        public TimAnimation makeAnimation()
        {
            var parts = new TimAnimPart[TimInterpreter.ANIM_PARTS];
            for (int i = 0; i < parts.Length; i += 1) parts[i] = new TimAnimPart { firstFrame = 0, lastFrame = 0, cycles = 0, nextPart = 0, partDelay = 0, field_A = 0, sfxIndex = 0, sfxFrame = 0 };
            return new TimAnimation { wsa = null, x = 0, y = 0, wsaCopyParams = 0, frameDelay = 0, enable = 0, lastPart = -1, curPart = 0, curFrame = 0, nextFrame = 0, cyclesCompleted = 0, field_D = 0, parts = parts };
        }

        public void init(int animIndex, WsaPlayer wsa, int x, int y, int wsaCopyParams, int frameDelay)
        {
            var anim = this.animations[animIndex];
            anim.wsa = wsa;
            anim.x = x;
            anim.y = y;
            anim.wsaCopyParams = wsaCopyParams;
            anim.frameDelay = frameDelay;
            anim.enable = 0;
            anim.lastPart = -1;
        }

        public void reset(int animIndex, bool clearStruct)
        {
            var anim = this.animations[animIndex];
            anim.field_D = 0;
            anim.enable = 0;
            anim.wsa = null;
            if (clearStruct) this.animations[animIndex] = this.makeAnimation();
        }

        public void displayFrame(int animIndex, int page, int frame, int flags = -1)
        {
            var anim = this.animations[animIndex];
            if ((anim.wsaCopyParams & 0x4000) != 0) page = 2;
            if (anim.wsa != null) anim.wsa.displayFrame(frame, page, anim.x, anim.y, flags == -1 ? anim.wsaCopyParams & 0xf0ff : flags, null, null);
        }

        public void setupPart(int animIndex, int part, int firstFrame, int lastFrame, int cycles, int nextPart, int partDelay, int f, int sfxIndex, int sfxFrame)
        {
            var p = this.animations[animIndex].parts[part];
            p.firstFrame = firstFrame; p.lastFrame = lastFrame; p.cycles = cycles; p.nextPart = nextPart; p.partDelay = partDelay; p.field_A = f; p.sfxIndex = sfxIndex; p.sfxFrame = sfxFrame;
        }

        public void start(int animIndex, int part)
        {
            var anim = this.animations[animIndex];
            anim.curPart = part;
            var p = anim.parts[part];
            anim.enable = 1;
            anim.nextFrame = this.vm.getMillis() + anim.frameDelay * this.vm.tickLength;
            anim.curFrame = p.firstFrame;
            anim.cyclesCompleted = 0;
            if (anim.wsa != null) anim.wsa.displayFrame(anim.curFrame - 1, 0, anim.x, anim.y, 0, null, null);
        }

        public void stop(int animIndex)
        {
            var anim = this.animations[animIndex];
            anim.enable = 0;
            anim.field_D = 0;
            if (animIndex == 5) anim.wsa = null;
        }

        public void update(int animIndex)
        {
            var anim = this.animations[animIndex];
            if (anim.enable == 0 || anim.nextFrame >= this.vm.getMillis()) return;
            var p = anim.parts[anim.curPart];
            anim.nextFrame = 0;
            int step;
            if (p.lastFrame >= p.firstFrame) { step = 1; anim.curFrame += 1; }
            else { step = -1; anim.curFrame -= 1; }
            if (anim.curFrame == p.lastFrame + step)
            {
                anim.cyclesCompleted += 1;
                if (anim.cyclesCompleted > p.cycles || anim.field_D != 0)
                {
                    anim.lastPart = anim.curPart;
                    if (p.nextPart == -1 || p.nextPart == 0xffff || (anim.field_D != 0 && p.field_A != 0))
                    {
                        anim.enable = 0;
                        anim.field_D = 0;
                        return;
                    }
                    anim.nextFrame += p.partDelay * this.vm.tickLength;
                    anim.curPart = p.nextPart;
                    p = anim.parts[anim.curPart];
                    anim.curFrame = p.firstFrame;
                    anim.cyclesCompleted = 0;
                }
                else anim.curFrame = p.firstFrame;
            }
            if (p.sfxIndex != -1 && p.sfxIndex != 0xffff && p.sfxFrame == anim.curFrame) this.vm.snd_playSoundEffect(p.sfxIndex, -1);
            anim.nextFrame += anim.frameDelay * this.vm.tickLength;
            if (anim.wsa != null) anim.wsa.displayFrame(anim.curFrame - 1, 0, anim.x, anim.y, 0, null, null);
            anim.nextFrame += this.vm.getMillis();
        }

        public async Task playPart(int animIndex, int firstFrame, int lastFrame, int delay)
        {
            var anim = this.animations[animIndex];
            if (anim.wsa == null) return;
            int step = lastFrame >= firstFrame ? 1 : -1;
            for (int i = firstFrame; i != lastFrame + step; i += step)
            {
                double next = this.vm.getMillis() + delay * this.vm.tickLength;
                if ((anim.wsaCopyParams & 0x4000) != 0)
                {
                    this.screen.copyRegion(112, 0, 112, 0, 176, 120, 6, 2);
                    anim.wsa.displayFrame(i - 1, 2, anim.x, anim.y, (anim.wsaCopyParams & 0x1000) != 0 ? 0x5000 : 0x4000, this.vm.transparencyTable1, this.vm.transparencyTable2);
                    this.screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0);
                }
                else anim.wsa.displayFrame(i - 1, 0, anim.x, anim.y, 0, null, null);
                double del = next - this.vm.getMillis();
                if (del > 0 && !this.vm.fastForward) await this.vm.delay(del, true);
                else if (this.vm.fastForward) await this.vm.delay(0);
            }
        }

        public int resetLastPart(int animIndex)
        {
            var anim = this.animations[animIndex];
            int res = anim.lastPart;
            anim.lastPart = -1;
            return res;
        }
    }

    public sealed class TimInterpreter
    {
        internal const int COUNT_FUNCS = 10;
        internal const int WSA_SLOTS = 6;
        internal const int ANIM_PARTS = 10;

        public LandsOfLore vm;
        public Screen screen;
        public TimAnimator animator;
        public TimScript currentTim;
        public int currentFunc;
        public bool finished;
        public int abortFlag;
        public string[] vocFiles;
        public int drawPage2;
        public Func<Span16, Task<int>>[] commands;

        // Set from outside in the JS (intro.mjs runCinematic / cineFadeParams); declared here since C# needs them.
        public bool introMode;
        public int palDiff, palDelayInc, palDelayAcc;
        public byte[] langData;

        public TimInterpreter(LandsOfLore vm)
        {
            this.vm = vm;
            this.screen = vm.screen;
            this.animator = new TimAnimator(vm);
            this.currentTim = null;
            this.currentFunc = 0;
            this.finished = false;
            this.abortFlag = 0;
            this.vocFiles = new[] { "", "", "", "", "", "", "", "" };
            this.drawPage2 = 0;
            Func<Span16, Task<int>> one = p => Task.FromResult(1);
            this.commands = new Func<Span16, Task<int>>[]
            {
                p => Task.FromResult(this.cmd_initFunc0()), p => this.cmd_stopAllFuncs(), p => this.cmd_initWSA(p), p => Task.FromResult(this.cmd_uninitWSA(p)),
                p => Task.FromResult(this.cmd_initFunc(p)), p => Task.FromResult(this.cmd_stopFunc(p)), p => Task.FromResult(this.cmd_wsaDisplayFrame(p)), p => Task.FromResult(this.cmd_displayText(p)),
                p => Task.FromResult(this.cmd_loadVocFile(p)), p => Task.FromResult(this.cmd_unloadVocFile(p)), p => Task.FromResult(this.cmd_playVocFile(p)), null,
                p => Task.FromResult(this.cmd_loadSoundFile(p)), one, p => Task.FromResult(this.cmd_playMusicTrack(p)), null,
                one, one, null, null,
                p => Task.FromResult(this.cmd_setLoopIp()), p => Task.FromResult(this.cmd_continueLoop(p)), p => Task.FromResult(this.cmd_resetLoopIp()), p => Task.FromResult(this.cmd_resetAllRuntimes()),
                one, p => this.cmd_execOpcode(p), p => Task.FromResult(this.cmd_initFuncNow(p)), p => Task.FromResult(this.cmd_stopFuncNow(p)),
                p => this.cmd_processDialogue(), p => Task.FromResult(this.cmd_dialogueBox(p)), p => Task.FromResult(-1),
            };
        }

        static readonly Regex load_noNpc = new Regex("^(AUTOMAP|COMPASS|SPELLBK|BARRIER|UNBAR|SMSHWL|LOLINTRO|LOLFINAL)", RegexOptions.IgnoreCase);
        static readonly Regex load_timExt = new Regex(@"\.TIM$", RegexOptions.IgnoreCase);

        public TimScript load(string name, Func<TimScript, Span16, Task<int>>[] opcodes)
        {
            if (!this.vm.res.exists(name)) return null;
            // A dialogue/cutscene script starting: the host keeps an NPC memory from it.
            if (!load_noNpc.IsMatch(name)) this.vm.ui?.Invoke("npc", new object[] { new Dictionary<string, object> { ["file"] = load_timExt.Replace(name, "") } });
            var decoded = LandsOfLore.decodeTim(this.vm.res.get(name));
            var text = decoded.text;
            var avtl = decoded.avtl;
            var func = new TimFunc[COUNT_FUNCS];
            for (int i = 0; i < func.Length; i += 1) func[i] = new TimFunc { ip = -1, lastTime = 0, nextTime = 0, loopIp = -1, avtl = -1 };
            var wsa = new TimWsa[WSA_SLOTS];
            for (int i = 0; i < wsa.Length; i += 1) wsa[i] = new TimWsa { anim = 0, x = 0, y = 0, wsaFlags = 0, offscreen = 0 };
            var tim = new TimScript
            {
                filename = name, clickedButton = 0, dlgFunc = -1, procFunc = -1, procParam = 0, avtl = avtl, text = text, opcodes = opcodes,
                func = func,
                wsa = wsa,
                isLoLOutro = false, lolCharacter = 0,
            };
            int num = Math.Min(avtl.Length, COUNT_FUNCS);
            for (int i = 0; i < num; i += 1) tim.func[i].avtl = avtl[i];
            return tim;
        }

        public string textString(TimScript tim, int index)
        {
            var text = tim.text;
            int at = text[index << 1] | (text[(index << 1) + 1] << 8);
            var s = new StringBuilder();
            while (at < text.Length && text[at] != 0) s.Append((char)text[at++]);
            return s.ToString();
        }

        /// <summary>loop: JS truthiness (runTimScript passes a script number, the intro passes false).</summary>
        public async Task<int> exec(TimScript tim, bool loop)
        {
            if (tim == null) return 0;
            var outer = this.currentTim;
            try
            {
                return await this.execTim(tim, loop);
            }
            finally
            {
                this.currentTim = outer;
                if (outer == null) this.vm.fastForward = false;
            }
        }

        public async Task<int> execTim(TimScript tim, bool loop)
        {
            this.currentTim = tim;
            if (tim.func[0].ip < 0)
            {
                tim.func[0].ip = tim.func[0].avtl;
                tim.func[0].nextTime = tim.func[0].lastTime = this.vm.getMillis();
            }
            do
            {
                this.vm.update();
                for (this.currentFunc = 0; this.currentFunc < COUNT_FUNCS; this.currentFunc += 1)
                {
                    var cur = tim.func[this.currentFunc];
                    if (tim.procFunc != -1) await this.execCommand(28, new Span16(new[] { (ushort)tim.procParam }, 0));
                    this.vm.update();
                    this.checkSpeechProgress();
                    bool running = true;
                    int cnt = 0;
                    if (this.vm.fastForward && cur.nextTime > this.vm.getMillis()) cur.nextTime = this.vm.getMillis();
                    while (cur.ip >= 0 && cur.nextTime <= this.vm.getMillis() && running)
                    {
                        if (cnt++ > 0)
                        {
                            if (tim.procFunc != -1) await this.execCommand(28, new Span16(new[] { (ushort)tim.procParam }, 0));
                            this.vm.update();
                        }
                        int opcode = (sbyte)tim.avtl[cur.ip + 2];
                        var param = new Span16(tim.avtl, cur.ip + 3);
                        int result = await this.execCommand(opcode, param);
                        if (result == -1)
                        {
                            loop = false;
                            running = false;
                            this.currentFunc = 11;
                            break;
                        }
                        else if (result == -2) running = false;
                        else if (result == -3)
                        {
                            tim.procFunc = this.currentFunc;
                            tim.dlgFunc = -1;
                        }
                        else if (result == 22) cur.loopIp = -1;
                        if (cur.ip >= 0)
                        {
                            cur.ip += tim.avtl[cur.ip];
                            cur.lastTime = cur.nextTime;
                            cur.nextTime += tim.avtl[cur.ip + 1] * this.vm.tickLength;
                        }
                    }
                }
                if (loop) await this.vm.delay(this.vm.tickLength);
            } while (loop && !this.vm.quit);
            return tim.clickedButton;
        }

        public async Task<int> execCommand(int cmd, Span16 param)
        {
            var tim = this.currentTim;
            var proc = cmd >= 0 && cmd < this.commands.Length ? this.commands[cmd] : null;   // JS: undefined out of range
            if (proc == null)
            {
                this.vm.log($"Unimplemented TIM command {cmd} in {tim.filename}");
                return 0;
            }
            return await proc(param);
        }

        public void stopAllFuncs(TimScript tim)
        {
            if (tim == null) return;
            foreach (var f in tim.func) f.ip = -1;
        }

        public TimScript unload(TimScript tim)
        {
            return null;
        }

        public void advanceToOpcode(int opcode)
        {
            var tim = this.currentTim;
            var f = tim.func[tim.dlgFunc];
            int len = tim.avtl[f.ip];
            while ((tim.avtl[f.ip + 2] & 0xff) != opcode)
            {
                if ((tim.avtl[f.ip + 2] & 0xff) == 1)
                {
                    tim.avtl[f.ip] = (ushort)len;
                    break;
                }
                len = tim.avtl[f.ip];
                f.ip += len;
            }
            f.nextTime = this.vm.getMillis();
        }

        public void resetDialogueState(TimScript tim)
        {
            if (tim == null) return;
            tim.procFunc = 0;
            tim.procParam = this.vm.dialogueNumButtons != 0 ? this.vm.dialogueNumButtons : 1;
            tim.clickedButton = 0;
            tim.dlgFunc = -1;
        }

        public void checkSpeechProgress()
        {
            var tim = this.currentTim;
            if (this.vm.speechEnabled() && tim.procParam > 1 && tim.func[this.currentFunc].loopIp >= 0)
            {
                if (this.vm.snd_updateCharacterSpeech() != 2)
                {
                    tim.func[this.currentFunc].loopIp = -1;
                    tim.dlgFunc = this.currentFunc;
                    this.advanceToOpcode(21);
                    tim.dlgFunc = -1;
                    this.animator.reset(5, false);
                }
            }
        }

        public int freeAnimStruct(int index)
        {
            this.animator.reset(index, true);
            return 1;
        }

        public async Task<int> initAnimStruct(int index, string filename, int x, int y, int frameDelay, int unused, int wsaFlags)
        {
            if (this.introMode) return await this.initAnimStructCinematic(index, filename, x, y, unused, wsaFlags);
            var vm = this.vm;
            var screen = this.screen;
            WsaPlayer wsa = null;
            int wsaOpenFlags = 0;
            if ((wsaFlags & 0x10) != 0) wsaOpenFlags |= 2;
            if ((wsaFlags & 8) != 0) wsaOpenFlags |= 1;
            string file = $"{filename}.WSA";
            if (vm.res.exists(file))
            {
                wsa = new WsaPlayer(screen);
                wsa.name = file.ToUpperInvariant();
                wsa.open(vm.res.get(file), wsaOpenFlags, screen.getPalette(3));
            }
            if ((wsaFlags & 1) != 0)
            {
                if (screen.fadeFlag != 1) await screen.fadeClearSceneWindow(10);
                Js.Set(screen.getPalette(3), Js.Slice(screen.getPalette(0), 128 * 3), 128 * 3);
            }
            else if ((wsaFlags & 2) != 0) await screen.fadeToBlack(10);
            if (wsa != null && (wsaFlags & 7) != 0) wsa.displayFrame(0, 0, x, y, 0, null, null);
            if ((wsaFlags & 3) != 0)
            {
                screen.loadSpecialColors(screen.getPalette(3));
                await screen.fadePalette(screen.getPalette(3), 10);
                screen.fadeFlag = 0;
            }
            this.animator.init(index, wsa, x, y, wsaFlags, frameDelay);
            return index + 1;
        }

        // TIMInterpreter::initAnimStruct (the generic one used by the intro/outro): draws on page 8 (intro)
        // or 0 (outro); flags 2 fade to black first, flags 4 load the backdrop .CPS and show frame 0.
        public async Task<int> initAnimStructCinematic(int index, string filename, int x, int y, int offscreenBuffer, int wsaFlags)
        {
            var vm = this.vm;
            var screen = this.screen;
            int page2 = this.drawPage2;
            WsaPlayer wsa = null;
            int wsaOpenFlags = 1;
            if ((wsaFlags & 0x10) != 0) wsaOpenFlags |= 2;
            if (offscreenBuffer == 2) wsaOpenFlags = 1;
            string file = $"{filename}.WSA";
            if (vm.res.exists(file))
            {
                wsa = new WsaPlayer(screen);
                wsa.open(vm.res.get(file), wsaOpenFlags, index == 1 ? screen.getPalette(0) : null);
            }
            if (x == -1) x = 0;
            if (y == -1) y = 0;
            if ((wsaFlags & 2) != 0)
            {
                await screen.fadePalette(screen.getPalette(1), 15);
                screen.clearPage(page2);
                if (page2 != 0) vm.checkedPageUpdate(8, 4);
            }
            if ((wsaFlags & 4) != 0 || wsa == null)
            {
                string cps = $"{filename}.CPS";
                if (vm.res.exists(cps))
                {
                    screen.loadBitmap(vm.res.get(cps), 2, screen.getPalette(0));
                    screen.copyRegion(0, 0, 0, 0, 320, 200, 2, page2, true);
                    if (page2 != 0) vm.checkedPageUpdate(8, 4);
                }
                if (wsa != null && (wsaFlags & 4) != 0) wsa.displayFrame(0, page2, x, y, 0, null, null);
            }
            if ((wsaFlags & 2) != 0) await screen.fadePalette(screen.getPalette(0), 30);
            this.animator.init(index, wsa, x, y, wsaFlags, 0);
            return index + 1;
        }

        // ---- commands ----
        public int cmd_initFunc0()
        {
            var tim = this.currentTim;
            for (int i = 0; i < WSA_SLOTS; i += 1) tim.wsa[i] = new TimWsa { anim = 0, x = 0, y = 0, wsaFlags = 0, offscreen = 0 };
            tim.func[0].ip = tim.func[0].avtl;
            tim.func[0].lastTime = this.vm.getMillis();
            return 1;
        }

        public async Task<int> cmd_stopAllFuncs()
        {
            var tim = this.currentTim;
            while (tim.dlgFunc == -1 && tim.clickedButton == 0 && !this.vm.quit)
            {
                this.vm.update();
                tim.clickedButton = await this.vm.processDialogue();
                if (tim.clickedButton == 0) await this.vm.delay(this.vm.tickLength);
            }
            foreach (var f in tim.func) f.ip = -1;
            return -1;
        }

        public int cmd_stopCurFunc()
        {
            if (this.currentFunc < COUNT_FUNCS) this.currentTim.func[this.currentFunc].ip = -1;
            if (this.currentFunc == 0) this.finished = true;
            return -2;
        }

        public async Task<int> cmd_initWSA(Span16 param)
        {
            var tim = this.currentTim;
            int index = param[0];
            var slot = tim.wsa[index];
            slot.x = (short)param[2];
            slot.y = (short)param[3];
            slot.offscreen = param[4];
            slot.wsaFlags = param[5];
            string filename = this.textString(tim, param[1]);
            slot.anim = await this.initAnimStruct(index, filename, slot.x, slot.y, 10, slot.offscreen, slot.wsaFlags);
            return 1;
        }

        public int cmd_uninitWSA(Span16 param)
        {
            var tim = this.currentTim;
            int index = param[0];
            var slot = tim.wsa[index];
            if (slot.anim == 0) return 0;
            if (slot.offscreen != 0)
            {
                this.animator.reset(index, false);
                slot.anim = 0;
            }
            else
            {
                this.animator.reset(index, true);
                tim.wsa[index] = new TimWsa { anim = 0, x = 0, y = 0, wsaFlags = 0, offscreen = 0 };
            }
            return 1;
        }

        public int cmd_initFunc(Span16 param)
        {
            var tim = this.currentTim;
            int func = param[0];
            if (tim.func[func].avtl >= 0) tim.func[func].ip = tim.func[func].avtl;
            else tim.func[func].avtl = tim.func[func].ip = tim.avtl[func];
            return 1;
        }

        public int cmd_stopFunc(Span16 param)
        {
            this.currentTim.func[param[0]].ip = -1;
            return 1;
        }

        public int cmd_wsaDisplayFrame(Span16 param)
        {
            this.animator.displayFrame(param[0], this.drawPage2, param[1]);
            return 1;
        }

        public int cmd_displayText(Span16 param)
        {
            // JS also tests `this.vm.cineDisplayText` (a method, always there)
            if (this.introMode) this.vm.cineDisplayText(param[0], (short)param[1], this.currentTim.isLoLOutro ? 0xf2 : 0);
            return 1;
        }

        public int cmd_loadVocFile(Span16 param)
        {
            string name = this.textString(this.currentTim, param[0]);
            SetVocFile(param[1], name.Length > 4 ? name.Substring(0, name.Length - 4) : "");   // slice(0, -4)
            return 1;
        }

        public int cmd_unloadVocFile(Span16 param)
        {
            SetVocFile(param[0], "");
            return 1;
        }

        // JS's vocFiles is an array that grows when a slot past its end is written (the outro loads its
        // voices from slot 8 on); a fixed C# array threw there and the ending cinematic never ran
        void SetVocFile(int index, string name)
        {
            if (index >= this.vocFiles.Length)
            {
                var grown = new string[index + 1];
                Array.Copy(this.vocFiles, grown, this.vocFiles.Length);
                for (int i = this.vocFiles.Length; i < index; i += 1) grown[i] = null;   // JS: holes (undefined)
                this.vocFiles = grown;
            }
            this.vocFiles[index] = name;
        }

        public int cmd_playVocFile(Span16 param)
        {
            int index = param[0];
            int volume = param[1] * 255 / 100;
            if (index < this.vocFiles.Length && !string.IsNullOrEmpty(this.vocFiles[index])) this.vm.snd_voicePlay(this.vocFiles[index], volume);
            else this.vm.snd_playSoundEffect(index, -1);
            return 1;
        }

        public int cmd_loadSoundFile(Span16 param)
        {
            this.vm.snd_loadSoundFileByName(this.textString(this.currentTim, param[0]));
            return 1;
        }

        public int cmd_playMusicTrack(Span16 param)
        {
            this.vm.snd_playTrack(param[0]);
            return 1;
        }

        public int cmd_setLoopIp()
        {
            var tim = this.currentTim;
            if (this.vm.speechEnabled())
            {
                if (this.vm.snd_updateCharacterSpeech() == 2) tim.func[this.currentFunc].loopIp = tim.func[this.currentFunc].ip;
                else LandsOfLore.advanceToOpcodeSafe(this, 21);
            }
            else tim.func[this.currentFunc].loopIp = tim.func[this.currentFunc].ip;
            return 1;
        }

        public int cmd_continueLoop(Span16 param)
        {
            var func = this.currentTim.func[this.currentFunc];
            if (func.loopIp < 0) return -2;
            func.ip = func.loopIp;
            if (this.vm.snd_updateCharacterSpeech() != 2)
            {
                int factor = param[0];
                if (factor != 0)
                {
                    // Pacing jitter for a line of speech: presentation, not a roll the game turns on.
                    int random = this.vm.presentationRandom(0x8000);
                    func.nextTime += (int)((double)random * factor / 0x8000) * this.vm.tickLength;
                }
            }
            return -2;
        }

        public int cmd_resetLoopIp()
        {
            this.currentTim.func[this.currentFunc].loopIp = -1;
            return 1;
        }

        public int cmd_resetAllRuntimes()
        {
            foreach (var f in this.currentTim.func) if (f.ip >= 0) f.nextTime = this.vm.getMillis();
            return 1;
        }

        public async Task<int> cmd_execOpcode(Span16 param)
        {
            var tim = this.currentTim;
            int opcode = param[0];
            var proc = tim.opcodes != null && opcode < tim.opcodes.Length ? tim.opcodes[opcode] : null;   // JS: undefined out of range
            if (proc == null)
            {
                this.vm.log($"Unimplemented TIM opcode {opcode} in {tim.filename}");
                return 0;
            }
            return await proc(tim, new Span16(param.Data, param.Offset + 1));
        }

        public int cmd_initFuncNow(Span16 param)
        {
            var f = this.currentTim.func[param[0]];
            f.ip = f.avtl;
            f.lastTime = f.nextTime = this.vm.getMillis();
            return 1;
        }

        public int cmd_stopFuncNow(Span16 param)
        {
            var f = this.currentTim.func[param[0]];
            f.ip = -1;
            f.lastTime = f.nextTime = this.vm.getMillis();
            return 1;
        }

        public async Task<int> cmd_processDialogue()
        {
            var tim = this.currentTim;
            int res = await this.vm.processDialogue();
            if (res == 0 || tim.procParam == 0) return res;
            this.vm.snd_stopSpeech(false);
            tim.func[tim.procFunc].loopIp = -1;
            tim.dlgFunc = tim.procFunc;
            tim.procFunc = -1;
            tim.clickedButton = res;
            this.animator.reset(5, false);
            if (tim.procParam != 0) this.advanceToOpcode(21);
            return res;
        }

        public int cmd_dialogueBox(Span16 param)
        {
            var tim = this.currentTim;
            tim.procParam = param[0];
            tim.clickedButton = 0;
            var tmpStr = new string[] { null, null, null };
            int cnt = 0;
            for (int i = 1; i < 4; i += 1)
            {
                if (param[i] != 0xffff)
                {
                    tmpStr[i - 1] = this.vm.getLangString(param[i]);
                    cnt += 1;
                }
            }
            this.vm.setupDialogueButtons(cnt, tmpStr[0], tmpStr[1], tmpStr[2]);
            this.vm.gui_notifyButtonListChanged();
            return -3;
        }
    }
}
