// src/game/text.mjs: TextDisplayer_LoL / TextDisplayer_rpg and dialogue buttons (text_lol.cpp, text_rpg.cpp, kyra_rpg.cpp).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>text.mjs TextDisplayer constructor: textDimData[] entries.</summary>
    public sealed class TextDimData
    {
        public int color1, color2, line, column, charSpacing, lineSpacing, shadowColor;
    }

    // TextDisplayer_LoL / TextDisplayer_rpg and dialogue buttons (text_lol.cpp, text_rpg.cpp, kyra_rpg.cpp).
    public sealed class TextDisplayer
    {
        public LandsOfLore vm;
        public Screen screen;
        public TextDimData[] textDimData;
        public int lineCount;
        public int numCharsTotal;
        public int numCharsPrinted;
        public int scriptTextParameter;
        public bool allowPageBreak;
        public int waitButtonSpace;
        public string pageBreakString;

        public TextDisplayer(LandsOfLore vm)
        {
            this.vm = vm;
            this.screen = vm.screen;
            this.textDimData = vm.screen.dims.Select(d => new TextDimData { color1 = d.col1, color2 = d.col2, line = 0, column = 0, charSpacing = 0, lineSpacing = 0, shadowColor = 0 }).ToArray();
            this.lineCount = 0;
            this.numCharsTotal = 0;
            this.numCharsPrinted = 0;
            this.scriptTextParameter = 0;
            this.allowPageBreak = true;
            this.waitButtonSpace = 0;
            this.pageBreakString = "";
        }

        public int clearDim(int dim)
        {
            int res = this.screen.curDimIndex;
            this.screen.setScreenDim(dim);
            this.textDimData[dim].color1 = this.screen.curDim.col1;
            this.textDimData[dim].color2 = this.screen.curDim.col2;
            this.clearCurDim();
            return res;
        }

        public void clearCurDim()
        {
            int d = this.screen.curDimIndex;
            var tmp = this.screen.getScreenDim(d);
            this.screen.fillRect(tmp.sx << 3, tmp.sy, ((tmp.sx + tmp.w) << 3) - 1, tmp.sy + tmp.h - 1, this.textDimData[d].color2);
            this.lineCount = 0;
            this.textDimData[d].column = this.textDimData[d].line = 0;
        }

        public void resetDimTextPositions(int dim)
        {
            this.textDimData[dim].column = this.textDimData[dim].line = 0;
        }

        // Word-wrapping printer; control codes: 1 page break, 2 color2, 6 color1, 9 tab, 13 newline.
        // Implemented as a generator that yields whenever a page break must be shown.
        public IEnumerable<string> displayTextGen(string str, bool allowPageBreak)
        {
            var screen = this.screen;
            var sd = screen.curDim;
            int sdx = screen.curDimIndex;
            var td = this.textDimData[sdx];
            int fontWidth = screen.font().width + screen.charSpacing;
            int fh = screen.fontHeight() + screen.lineSpacing + td.lineSpacing;
            int lines = (sd.h - screen.lineSpacing) / fh;
            int width = sd.w << 3;
            this.numCharsPrinted = 0;
            string current = "";
            int lineWidth = 0;

            IEnumerable<string> scrollIfNeeded(TextDisplayer self)
            {
                while (td.line >= lines)
                {
                    if (lines - self.waitButtonSpace <= self.lineCount && allowPageBreak)
                    {
                        self.lineCount = 0;
                        yield return "pagebreak";
                        self.numCharsPrinted = 0;
                    }
                    int h1 = (sd.h / fh - 1) * fh;
                    int h2 = sd.h - fh;
                    if (h2 != 0) screen.copyRegion(sd.sx << 3, sd.sy + fh, sd.sx << 3, sd.sy, sd.w << 3, h2, screen.curPage, screen.curPage, true);
                    screen.fillRect(sd.sx << 3, sd.sy + h1, ((sd.sx + sd.w) << 3) - 1, sd.sy + sd.h - 1, td.color2);
                    if (td.line != 0) td.line -= 1;
                }
            }

            // Prints one line from `current`; returns true when more text remains to be printed on a new line.
            bool printOneLine()
            {
                int s = current.Length;
                int lw = lineWidth;
                int w = width;
                if (lw + td.column >= w)
                {
                    if (lines - 1 <= this.lineCount && allowPageBreak) w -= 80;
                    w -= td.column;
                    int lineLastCharPos = 0;
                    int strPos = s - 1;
                    bool printFlag = false;
                    while (strPos > 0)
                    {
                        char c = current[strPos];
                        lw -= screen.charWidth(current[strPos]);
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
                        if (td.column != 0 && !printFlag) { s = lw = 0; }
                        else s = lineLastCharPos;
                    }
                }
                string part = current.Substring(0, s);
                int x1 = (sd.sx << 3) + td.column;
                int y = sd.sy + fh * td.line;
                screen.printText(part, x1, y, td.color1, td.color2);
                td.column += lw;
                this.numCharsPrinted += part.Length;
                string rest = current.Substring(s);
                if (s < current.Length && current[s] == ' ') rest = current.Substring(s + 1);
                if (rest.Length > 0 && rest[0] == ' ') rest = rest.Substring(1);
                current = rest;
                lineWidth = screen.textWidth(current) + td.charSpacing * current.Length;
                if (current.Length == 0 && td.column <= width) return false;
                td.column = 0;
                td.line += 1;
                this.lineCount += 1;
                return current.Length > 0;
            }

            var self = this;
            IEnumerable<string> printLine()
            {
                do
                {
                    foreach (var step in scrollIfNeeded(self)) yield return step;
                } while (printOneLine());
            }

            for (int i = 0; i < str.Length; i += 1)
            {
                int c = str[i];
                switch (c)
                {
                    case 1:
                        foreach (var step in printLine()) yield return step;
                        yield return "pagebreak";
                        this.numCharsPrinted = 0;
                        break;
                    case 2:
                        foreach (var step in printLine()) yield return step;
                        td.color2 = ++i < str.Length ? str[i] : 0;   // charCodeAt past the end: NaN
                        break;
                    case 6:
                        foreach (var step in printLine()) yield return step;
                        td.color1 = ++i < str.Length ? str[i] : 0;
                        break;
                    case 9:
                    {
                        foreach (var step in printLine()) yield return step;
                        int dv = td.column / fontWidth;
                        dv = ((dv + 8) & 0xfff8) - 1;
                        if (dv >= width / fontWidth) dv = 0;
                        td.column = fontWidth * dv;
                        break;
                    }
                    case 13:
                        foreach (var step in printLine()) yield return step;
                        this.lineCount += 1;
                        td.column = 0;
                        td.line += 1;
                        break;
                    default:
                        if (c == 0) break;
                        lineWidth += screen.charWidth(c) + td.charSpacing;
                        current += str[i];
                        if (td.column + lineWidth > width) foreach (var step in printLine()) yield return step;
                        break;
                }
            }
            if (current.Length != 0) foreach (var step in printLine()) yield return step;
        }

        public async Task displayText(string str, bool? allowPageBreak = null)
        {
            bool allow = allowPageBreak ?? this.allowPageBreak;
            foreach (var step in this.displayTextGen(str, allow))
            {
                if (step == "pagebreak") await this.textPageBreak();
            }
        }

        // Synchronous variant for short status messages (no page breaks).
        public void displayTextSync(string str)
        {
            foreach (var step in this.displayTextGen(str, false)) if (!string.IsNullOrEmpty(step)) break;
        }

        public async Task textPageBreak()
        {
            var vm = this.vm;
            var screen = this.screen;
            string pbs = vm.getLangString(0x4073);
            this.pageBreakString = !string.IsNullOrEmpty(pbs) ? pbs : "MORE";
            int cp = screen.curPage;
            screen.curPage = 0;
            string cf = screen.setFont("6");
            vm.timerPauseSingle(11, true);
            vm.fadeText = false;
            int resetPortraitAfterSpeechAnim = 0;
            int updatePortraitSpeechAnimDuration = 0;
            if (vm.updateCharNum != -1)
            {
                resetPortraitAfterSpeechAnim = vm.resetPortraitAfterSpeechAnim;
                vm.resetPortraitAfterSpeechAnim = 0;
                updatePortraitSpeechAnimDuration = vm.updatePortraitSpeechAnimDuration;
                if (vm.updatePortraitSpeechAnimDuration > 36) vm.updatePortraitSpeechAnimDuration = 36;
            }
            double speechPartTime = 0;
            if (vm.speechEnabled() && vm.activeVoiceFileTotalTime != 0 && this.numCharsTotal != 0)
            {
                speechPartTime = vm.getMillis() + (int)((double)this.numCharsPrinted * vm.activeVoiceFileTotalTime / this.numCharsTotal);
            }
            int sdx = screen.curDimIndex;
            var dim = screen.getScreenDim(sdx);
            int x = ((dim.sx + dim.w) << 3) - (vm.dialogueButtonWidth + 3);
            int y;
            int w = vm.dialogueButtonWidth;
            if (vm.needSceneRestore != 0 && (vm.updateFlags & 2) != 0)
            {
                if (vm.currentControlMode != 0 || (vm.updateFlags & 2) == 0) y = dim.sy + dim.h - 5;
                else
                {
                    x += 6;
                    y = dim.sy + dim.h - 2;
                }
            }
            else y = dim.sy + dim.h - 10;
            vm.gui_drawBox(x, y, w, 9, 136, 251, 252);
            screen.printText(this.pageBreakString, x + (w >> 1) - (screen.textWidth(this.pageBreakString) >> 1), y + 2, vm.dialogueButtonLabelColor1, 0);
            vm.removeInputTop();
            vm.ui?.Invoke("waiting", new object[] { true, this.pageBreakString });
            bool loop = true;
            bool target = false;
            do
            {
                var ev = await vm.waitForInputEvent(() =>
                {
                    if (vm.fastForward) return new InputEvent { type = "key", key = "Enter" };
                    if (vm.speechEnabled() && speechPartTime != 0 && (vm.getMillis() > speechPartTime || vm.snd_updateCharacterSpeech() != 2)) return new InputEvent { type = "key", key = "Enter" };
                    return null;
                });
                vm.gui_notifyButtonListChanged();
                if (ev.type == "key" && (ev.key == " " || ev.key == "Enter")) loop = false;
                else if (ev.type == "mousedown")
                {
                    if (ev.x >= x && ev.x <= x + w && ev.y >= y && ev.y <= y + 9) target = true;
                }
                else if (ev.type == "mouseup")
                {
                    if (target) loop = false;
                }
            } while (loop && !vm.quit);
            vm.ui?.Invoke("waiting", new object[] { false });
            screen.fillRect(x, y, x + w - 1, y + 8, this.textDimData[sdx].color2);
            this.clearCurDim();
            vm.timerPauseSingle(11, false);
            if (vm.updateCharNum != -1)
            {
                vm.resetPortraitAfterSpeechAnim = resetPortraitAfterSpeechAnim;
                if (updatePortraitSpeechAnimDuration > 36) updatePortraitSpeechAnimDuration -= 36;
                else updatePortraitSpeechAnimDuration >>= 1;
                vm.updatePortraitSpeechAnimDuration = updatePortraitSpeechAnimDuration;
            }
            screen.setFont(cf);
            screen.curPage = cp;
            vm.removeInputTop();
        }

        /// <summary>Callers pass true/false as the JS does; the mode is only tested for truthiness.</summary>
        public Task setupField(bool mode) => setupField(mode ? 1 : 0);

        public async Task setupField(int mode)
        {
            var vm = this.vm;
            var screen = this.screen;
            if (vm.textEnabled())
            {
                const int y = 142;
                const int h = 37;
                if (mode != 0)
                {
                    vm.pageBuffer1 = screen.copyRegionToBuffer(3, 0, 0, 320, 40);
                    screen.copyRegion(80, y, 0, 0, 240, h, 0, 3, true);
                    vm.pageBuffer2 = screen.copyRegionToBuffer(3, 0, 0, 320, 40);
                    screen.copyBlockToPage(3, 0, 0, 320, 40, vm.pageBuffer1);
                }
                else
                {
                    screen.setScreenDim(this.clearDim(4));
                    int cp = screen.curPage;
                    screen.curPage = 2;
                    vm.pageBuffer1 = screen.copyRegionToBuffer(3, 0, 0, 320, 40);
                    screen.copyBlockToPage(3, 0, 0, 320, 40, vm.pageBuffer2);
                    screen.copyRegion(0, 0, 80, y, 240, h, 3, screen.curPage, true);
                    double endTime = vm.getMillis();
                    for (int i = 177; i > 141; i -= 1)
                    {
                        endTime += vm.tickLength;
                        screen.copyRegion(83, i, 83, i - 1, 235, 3, 0, 0, true);
                        screen.copyRegion(83, i + 1, 83, i + 1, 235, 1, 2, 0, true);
                        vm.updateInput();
                        await vm.delayUntil(endTime);
                    }
                    screen.copyBlockToPage(3, 0, 0, 320, 40, vm.pageBuffer1);
                    screen.curPage = cp;
                    vm.updateFlags &= 0xfffd;
                }
            }
            else
            {
                if (mode == 0) screen.setScreenDim(this.clearDim(4));
                vm.toggleSelectedCharacterFrame(1);
            }
        }

        public async Task expandField()
        {
            var vm = this.vm;
            var screen = this.screen;
            if (vm.textEnabled())
            {
                vm.fadeText = false;
                vm.textColorFlag = 0;
                vm.timerDisable(11);
                screen.setScreenDim(this.clearDim(3));
                var tmp = screen.copyRegionToBuffer(3, 0, 0, 320, 10);
                screen.copyRegion(83, 140, 0, 0, 235, 3, 0, 2, true);
                double endTime = vm.getMillis();
                for (int i = 140; i < 177; i += 1)
                {
                    endTime += vm.tickLength;
                    screen.copyRegion(0, 0, 83, i, 235, 3, 2, 0, true);
                    vm.updateInput();
                    await vm.delayUntil(endTime);
                }
                screen.copyBlockToPage(3, 0, 0, 320, 10, tmp);
                vm.updateFlags |= 2;
            }
            else
            {
                this.clearDim(3);
                vm.toggleSelectedCharacterFrame(0);
            }
        }

        public async Task printDialogueText2(int dim, string str, EmcState script, Span16? paramList, int paramIndex)
        {
            var vm = this.vm;
            var screen = this.screen;
            int oldDim;
            if (dim == 3)
            {
                if ((vm.updateFlags & 2) != 0)
                {
                    oldDim = this.clearDim(4);
                    this.textDimData[4].color1 = 254;
                    this.textDimData[4].color2 = screen.curDim.col2;
                }
                else
                {
                    oldDim = this.clearDim(3);
                    this.textDimData[3].color1 = 192;
                    this.textDimData[3].color2 = screen.curDim.col2;
                    screen.copyColor(192, 254);
                    vm.timerEnable(11);
                    vm.textColorFlag = 0;
                    vm.fadeText = false;
                }
            }
            else
            {
                oldDim = screen.curDimIndex;
                screen.setScreenDim(dim);
                this.lineCount = 0;
                this.textDimData[dim].color1 = 254;
                this.textDimData[dim].color2 = screen.curDim.col2;
            }
            int cp = screen.curPage;
            screen.curPage = 0;
            string of = screen.setFont("9");
            string text = this.preprocessString(str, script, paramList, paramIndex);
            this.numCharsTotal = text.Length;
            vm.ui?.Invoke("dialogue", new object[] { text });
            await this.displayText(text);
            screen.setScreenDim(oldDim);
            screen.curPage = cp;
            screen.setFont(of);
            this.lineCount = 0;
            vm.fadeText = false;
        }

        static readonly int[] printMessage_textColors = { 0xfe, 0xa2, 0x84, 0x97, 0x9f };
        static readonly int[] printMessage_soundEffect = { 0x0b, 0x00, 0x2b, 0x1b, 0x00 };

        public void printMessage(int type, string str)
        {
            var vm = this.vm;
            var screen = this.screen;
            var textColors = printMessage_textColors;
            var soundEffect = printMessage_soundEffect;
            if ((type & 4) != 0) type &= ~4;
            else vm.stopPortraitSpeechAnim();
            int index = type & 0x7fff;
            int col = index < textColors.Length ? textColors[index] : 0;   // JS: undefined past the table
            int od = screen.curDimIndex;
            if ((vm.updateFlags & 2) != 0)
            {
                this.clearDim(4);
                this.textDimData[4].color1 = col;
            }
            else
            {
                this.clearDim(3);
                screen.copyColor(192, col);
                this.textDimData[3].color1 = 192;
                vm.timerEnable(11);
            }
            int cp = screen.curPage;
            screen.curPage = 0;
            string of = screen.setFont("9");
            this.displayTextSync(str ?? "");
            vm.ui?.Invoke("message", new object[] { str ?? "", index });
            screen.setFont(of);
            screen.curPage = cp;
            screen.setScreenDim(od);
            this.lineCount = 0;
            if ((type & 0x8000) == 0 && index < soundEffect.Length && soundEffect[index] != 0) vm.snd_playSoundEffect(soundEffect[index], -1);
            vm.textColorFlag = index;
            vm.fadeText = false;
        }

        // Expands %n (character name), %s (lang string), %d/%u/%x (number), %a (script parameter).
        public string preprocessString(string str, EmcState script, Span16? paramList, int paramIndex)
        {
            var vm = this.vm;
            var @out = new StringBuilder();
            // str[i] past the end is undefined in JS: null here
            char? at(int k) => k < str.Length ? str[k] : (char?)null;
            for (int i = 0; i < str.Length;)
            {
                if (str[i] != '%') { @out.Append(str[i++]); continue; }
                i += 1;
                char? para = at(i);
                if (para == null) break;
                if (para == '#')
                {
                    i += 1;
                    para = at(i);
                    if (para == null || !"EGXcdefgsux".Contains(para.Value)) continue;
                }
                else if (para == ' ' || para == '+' || para == '-') i += 1;
                para = at(i);
                if (para == null) break;
                if (para == '0') i += 1;
                else while (para != null && para > '/' && para < ':') para = at(++i);
                para = at(i++);
                int value() => script != null ? script.stack[script.sp + paramIndex] : paramList != null ? paramList.Value[paramIndex] : 0;
                switch (para)
                {
                    case 'a': @out.Append(this.scriptTextParameter.ToString()); break;
                    case 'n': if (script != null || paramList != null) @out.Append(vm.characters[value()].name); break;
                    case 's': if (script != null || paramList != null) @out.Append(vm.getLangString(value()) ?? ""); break;
                    case 'X': case 'd': case 'u': case 'x': if (script != null || paramList != null) @out.Append(value().ToString()); break;
                    default: break;
                }
            }
            return @out.ToString();
        }
    }

    // Dialogue buttons (KyraRpgEngine::drawDialogueButtons / processDialogue / LoLEngine::setupDialogueButtons).
    public sealed partial class LandsOfLore
    {
        public int dialogueNumButtons;
        public string[] dialogueButtonString;
        public int dialogueHighlightedButton;
        public int[] dialogueButtonPosX;
        public int[] dialogueButtonPosY;
        public int dialogueButtonXoffs;
        public int dialogueButtonYoffs;
        public int dialogueButtonWidth;
        public int dialogueButtonLabelColor1;
        public int dialogueButtonLabelColor2;
        public int activeVoiceFileTotalTime;

        public void initDialogue()
        {
            this.dialogueNumButtons = 0;
            this.dialogueButtonString = new string[] { null, null, null };
            this.dialogueHighlightedButton = 0;
            this.dialogueButtonPosX = new[] { 0, 0, 0 };
            this.dialogueButtonPosY = new[] { 0, 0, 0 };
            this.dialogueButtonXoffs = 0;
            this.dialogueButtonYoffs = 0;
            this.dialogueButtonWidth = 74;
            this.dialogueButtonLabelColor1 = 144;
            this.dialogueButtonLabelColor2 = 254;
            this.activeVoiceFileTotalTime = 0;
        }

        public void setupDialogueButtons(int numStr, string s1, string s2, string s3)
        {
            this.screen.setScreenDim(5);
            this.ui?.Invoke("choices", new object[] { numStr == 1 && this.speechEnabled() ? new string[0] : Js.Slice(new[] { s1, s2, s3 }, 0, numStr) });
            if (numStr == 1 && this.speechEnabled())
            {
                this.dialogueNumButtons = 0;
                this.dialogueButtonString = new string[] { null, null, null };
            }
            else
            {
                this.dialogueNumButtons = numStr;
                this.dialogueButtonString = new[] { s1, s2, s3 };
                this.dialogueHighlightedButton = 0;
                var d = this.screen.getScreenDim(5);
                int y = d.sy + d.h - 9;
                this.dialogueButtonPosY = new[] { y, y, y };
                if (numStr == 1)
                {
                    int x = d.sx + d.w - (this.dialogueButtonWidth + 3);
                    this.dialogueButtonPosX = new[] { x, x, x };
                }
                else
                {
                    // numStr 0: JS gets Infinity here (and Infinity >> 1 == 0); no button is ever drawn or hit
                    int xOffs = numStr != 0 ? d.w / numStr : 0;
                    int x0 = d.sx + (xOffs >> 1) - 37;
                    this.dialogueButtonPosX = new[] { x0, x0 + xOffs, x0 + 2 * xOffs };
                }
                this.drawDialogueButtons();
            }
            this.removeInputTop();
        }

        public void drawDialogueButtons()
        {
            int cp = this.screen.curPage;
            this.screen.curPage = 0;
            string of = this.screen.setFont("6");
            for (int i = 0; i < this.dialogueNumButtons; i += 1)
            {
                int x = this.dialogueButtonPosX[i];
                int y = this.dialogueButtonYoffs + this.dialogueButtonPosY[i];
                this.gui_drawBox(x, y, this.dialogueButtonWidth, 9, 136, 251, 252);
                string label = this.dialogueButtonString[i] ?? "";
                this.screen.printText(label, x + (this.dialogueButtonWidth >> 1) - this.screen.textWidth(label) / 2, y + 2,
                    this.dialogueHighlightedButton == i ? this.dialogueButtonLabelColor1 : this.dialogueButtonLabelColor2, 0);
            }
            this.screen.setFont(of);
            this.screen.curPage = cp;
        }

        // Returns 0 while waiting, otherwise the 1-based button pressed.
        public async Task<int> processDialogue()
        {
            int df = this.dialogueHighlightedButton;
            int res = 0;
            for (int i = 0; i < this.dialogueNumButtons; i += 1)
            {
                int x = this.dialogueButtonPosX[i] + this.dialogueButtonXoffs;
                int y = this.dialogueButtonYoffs + this.dialogueButtonPosY[i];
                if (this.mouseX >= x && this.mouseX <= x + this.dialogueButtonWidth && this.mouseY >= y && this.mouseY <= y + 9)
                {
                    this.dialogueHighlightedButton = i;
                    break;
                }
            }
            var ev = this.shiftEvent();
            if (this.fastForward)
            {
                if (this.dialogueNumButtons > 1) this.fastForward = false; // a real choice: hand back control
                else { this.snd_stopSpeech(true); ev = new InputEvent { type = "key", key = "Enter" }; }
            }
            if (this.dialogueNumButtons == 0)
            {
                if (ev != null)
                {
                    this.gui_notifyButtonListChanged();
                    if (ev.type == "key" && (ev.key == " " || ev.key == "Enter")) this.snd_stopSpeech(true);
                }
                if (this.snd_updateCharacterSpeech() != 2)
                {
                    res = 1;
                    this.removeInputTop();
                    this.gui_notifyButtonListChanged();
                }
            }
            else if (ev != null)
            {
                this.gui_notifyButtonListChanged();
                if (ev.type == "mouseup")
                {
                    for (int i = 0; i < this.dialogueNumButtons; i += 1)
                    {
                        int x = this.dialogueButtonPosX[i];
                        int y = this.dialogueButtonYoffs + this.dialogueButtonPosY[i];
                        if (ev.x >= x && ev.x <= x + this.dialogueButtonWidth && ev.y >= y && ev.y <= y + 9)
                        {
                            this.dialogueHighlightedButton = i;
                            res = i + 1;
                            break;
                        }
                    }
                }
                else if (ev.type == "key" && (ev.key == " " || ev.key == "Enter"))
                {
                    this.snd_stopSpeech(true);
                    res = this.dialogueHighlightedButton + 1;
                }
                else if (ev.type == "key" && (ev.key == "ArrowLeft" || ev.key == "ArrowDown"))
                {
                    if (this.dialogueNumButtons > 1 && this.dialogueHighlightedButton > 0) this.dialogueHighlightedButton -= 1;
                }
                else if (ev.type == "key" && (ev.key == "ArrowRight" || ev.key == "ArrowUp"))
                {
                    if (this.dialogueNumButtons > 1 && this.dialogueHighlightedButton < this.dialogueNumButtons - 1) this.dialogueHighlightedButton += 1;
                }
            }
            if (df != this.dialogueHighlightedButton) this.drawDialogueButtons();
            if (res == 0) return 0;
            this.ui?.Invoke("choices", new object[] { null });
            this.stopPortraitSpeechAnim();
            if (!this.textEnabled() && this.currentControlMode != 0)
            {
                this.screen.setScreenDim(5);
                var d = this.screen.getScreenDim(5);
                this.screen.fillRect(d.sx, d.sy + d.h - 9, d.sx + d.w - 1, d.sy + d.h - 1, d.col2);
            }
            else
            {
                // Clears the dialogue text box. When no dialogue box was set up (door animations that end with
                // stopAllFuncs) the current dim is still the scene-shape clip dim 13, whose byte-unit bounds would paint a
                // black bar over the left border; skip the fill in that case.
                var d = this.screen.curDim;
                if (this.screen.curDimIndex != 13) this.screen.fillRect(d.sx, d.sy, d.sx + d.w - 2, d.sy + d.h - 1, d.col2);
                this.txt.clearDim(4);
                this.txt.resetDimTextPositions(4);
            }
            return res;
        }

        // Runs the dialogue loop until a button is chosen; the caller has set up the buttons.
        public async Task<int> runDialogue()
        {
            int res;
            while ((res = await this.processDialogue()) == 0 && !this.quit)
            {
                this.timerUpdate();
                this.update();
                await this.delay(this.tickLength);
            }
            return res;
        }

        public int characterSays(int track, int charId, bool redraw)
        {
            if (charId == 1) charId = this.selectedCharacter;
            if (charId <= 0) charId = 0;
            else
            {
                int i = 0;
                for (; i < 4; i += 1)
                {
                    if (charId != this.characters[i].id || (this.characters[i].flags & 1) == 0) continue;
                    charId = i;
                    break;
                }
                if (i == 4) return 0;
            }
            bool r = this.snd_playCharacterSpeech(track, charId);
            if (r && redraw)
            {
                this.stopPortraitSpeechAnim();
                this.updateCharNum = charId;
                this.portraitSpeechAnimMode = 0;
                this.resetPortraitAfterSpeechAnim = 1;
                this.fadeText = false;
                this.updatePortraitSpeechAnim();
            }
            return r ? (this.textEnabled() ? 1 : 0) : 1;
        }

        public async Task<int> playCharacterScriptChat(int charId, int mode, int restorePortrait, string str, EmcState script, Span16? paramList, int paramIndex)
        {
            int ch = 0;
            bool skipAnim = false;
            if (charId == -1 || (charId & 0x70) == 0) charId = ch = charId == 1 ? (this.selectedCharacter != 0 ? this.characters[this.selectedCharacter].id : 0) : charId;
            else charId ^= 0x70;
            this.stopPortraitSpeechAnim();
            // Which character pipes up is a presentation choice: it changes who speaks, never what happens.
            if (charId < 0) charId = ch = this.presentationRandom(this.countActiveCharacters() - 1);
            else if (charId > 0)
            {
                int i = 0;
                for (; i < 4; i += 1)
                {
                    if (this.characters[i].id != charId || (this.characters[i].flags & 1) == 0) continue;
                    if (charId == ch) ch = i;
                    charId = i;
                    break;
                }
                if (i == 4)
                {
                    if (charId == 8) skipAnim = true;
                    else return 0;
                }
            }
            if (!skipAnim && charId < 3)
            {
                this.updateCharNum = charId;
                this.portraitSpeechAnimMode = mode;
                this.updatePortraitSpeechAnimDuration = str.Length >> 1;
                this.resetPortraitAfterSpeechAnim = restorePortrait;
            }
            if (script != null) this.snd_playCharacterSpeech(script.stack[script.sp + 2], ch);
            else if (paramList != null) this.snd_playCharacterSpeech(paramList.Value[2], ch);
            if (this.textEnabled())
            {
                if (mode == 0) await this.txt.printDialogueText2(3, str, script, paramList, paramIndex);
                else if (mode == 1)
                {
                    this.txt.clearDim(4);
                    this.screen.modifyScreenDim(4, 16, 123, 23, 47);
                    await this.txt.printDialogueText2(4, str, script, paramList, paramIndex);
                    this.screen.modifyScreenDim(4, 11, 123, 28, 47);
                }
                else if (mode == 2)
                {
                    this.txt.clearDim(4);
                    this.screen.modifyScreenDim(4, 9, 133, 30, 60);
                    await this.txt.printDialogueText2(4, str, script, paramList, 3);
                    this.screen.modifyScreenDim(4, 1, 133, 37, 60);
                }
            }
            this.fadeText = false;
            if (!skipAnim && charId < 3) this.updatePortraitSpeechAnim();
            return 1;
        }
    }
}
