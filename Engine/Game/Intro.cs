// src/game/intro.mjs
// Intro and outro cinematics (sequences_lol.cpp showIntro/showOutro): TIM scripts LOLINTRO.TIM and
// LOLFINAL.TIM with their own opcode tables, text from the .DIP tables, WSA animations composed on
// page 8 and copied to the screen through checkedPageUpdate, palette fades driven per frame.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        static readonly int[] TEXT_PAL = { 0x00, 0x00, 0x00, 0x64, 0x64, 0x64, 0x61, 0x51, 0x30, 0x29, 0x48, 0x64, 0x00, 0x4b, 0x3b, 0x64, 0x1e, 0x1e };
        static readonly string[] INTRO_PAKS = { "INTRO1.PAK", "INTRO2.PAK", "INTRO3.PAK", "INTRO4.PAK", "INTRO5.PAK", "INTRO6.PAK", "INTRO7.PAK", "INTRO8.PAK", "INTROVOC.PAK" };
        static readonly string[] OUTRO_PAKS = { "FINALE.PAK", "FINALE1.PAK", "FINALE2.PAK" };

        // ---- IntroMixin ----
        public bool cineSkip;
        public bool cineTextShown;
        public byte[] cineTextBuffer;
        /// <summary>the mouse cursor shape (spells.mjs setMouseCursor sets it; the host draws it)</summary>
        public Shape cursorShape;

        public void initIntro()
        {
            cineSkip = false;
        }

        public string cineString(TimScript tim, int index)
        {
            var t = tim.text;
            int at = t[index * 2] | (t[index * 2 + 1] << 8);
            var s = new StringBuilder();
            while (at < t.Length && t[at] != 0) s.Append((char)t[at++]);
            return s.ToString();
        }

        public string cineTableEntry(int index)
        {
            var d = tim.langData;
            if (d == null) return "";
            int at = d[index * 2] | (d[index * 2 + 1] << 8);
            var s = new StringBuilder();
            while (at < d.Length && d[at] != 0) s.Append((char)d[at++]);
            return s.ToString();
        }

        // Screen::getFadeParams + fadePalStep, driven from the cinematic loop.
        public void cineFadeParams(byte[] pal, int delay)
        {
            int maxDiff = 0;
            for (int i = 0; i < 768; i += 1) maxDiff = Math.Max(maxDiff, Math.Abs(pal[i] - screen.screenPalette[i]));
            int delayInc = (delay << 8) & 0x7fff;
            if (maxDiff != 0) delayInc = delayInc / maxDiff;
            int step = delayInc;
            int diff = 1;
            for (; diff <= maxDiff; diff += 1)
            {
                if (delayInc >= 512) break;
                delayInc += step;
            }
            tim.palDelayInc = delayInc;
            tim.palDiff = diff;
            tim.palDelayAcc = 0;
        }

        public bool cineFadeStep(byte[] pal, int diff)
        {
            var sp = screen.screenPalette;
            bool changed = false;
            for (int i = 0; i < 768; i += 1)
            {
                int c1 = pal[i];
                int c2 = sp[i];
                if (c1 == c2) continue;
                changed = true;
                if (c1 > c2) c2 = Math.Min(c1, c2 + diff);
                else c2 = Math.Max(c1, c2 - diff);
                sp[i] = (byte)c2;
            }
            screen.paletteDirty = true;
            return changed;
        }

        public void cineSetupTextPalette(int index, int fadePalette)
        {
            var pal = screen.getPalette(0);
            for (int i = 0; i < 15; i += 1)
            {
                int p = (240 + i) * 3;
                for (int c = 0; c < 3; c += 1) pal[p + c] = (byte)((((15 - i) << 2) * TEXT_PAL[index * 3 + c]) / 100);
            }
            if (fadePalette == 0 && tim.palDiff == 0) screen.setScreenPalette(pal);
            else cineFadeParams(pal, fadePalette);
        }

        // TIMInterpreter::displayText: a line of intro text (voice file in $...$), centred at y=160 (or 188 for flags < 0).
        public void cineDisplayText(int textId, int flags, int color)
        {
            var screen = this.screen;
            string text = cineTableEntry(textId & 0x7fff);
            if (cineTextShown)
            {
                screen.copyBlockToPage(0, 0, 160, 320, 40, cineTextBuffer);
                cineTextShown = false;
            }
            if (string.IsNullOrEmpty(text)) return;
            if (text[0] == '$')
            {
                int end = text.IndexOf('$', 1);
                if (end > 0)
                {
                    string voice = text.Substring(1, end - 1);
                    if (speechEnabledFlag && voice.Length != 0) snd_voicePlay(voice, 255);
                    text = text.Substring(end + 1);
                }
            }
            cineSetupTextPalette(flags < 0 ? 1 : flags, 0);
            string cf = screen.setFont(flags < 0 ? "8" : "intro");
            var savedColors = screen.textColors.ToArray();
            if (flags < 0) screen.setTextColor(new byte[] { 0x00, 0xf0, 0xfe, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0);
            else screen.setTextColor(new byte[] { 0x00, (byte)(color != 0 ? color : 0xf0), 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa, 0, 0, 0, 0 }, 0);
            cineTextBuffer = screen.copyRegionToBuffer(0, 0, 160, 320, 40);
            cineTextShown = true;
            int cp = screen.curPage;
            screen.curPage = 0;
            var lines = text.Split('\r');
            int y = flags < 0 ? 188 : 160;
            foreach (var line in lines)
            {
                if (!textEnabledFlag) break;
                int width = screen.textWidth(line);
                screen.printText(line, (320 - width) >> 1, y, flags < 0 ? 0xf0 : color != 0 ? color : 0xf0, 0x00);
                y += screen.fontHeight() - 4;
            }
            screen.curPage = cp;
            screen.setTextColor(savedColors, 0);
            screen.setFont(cf);
        }

        // Screen_v2::wsaFrameAnimationStep: scaled copy of a WSA frame (intro zooms).
        public void wsaFrameAnimationStep(int x1, int y1, int x2, int y2, int w1, int h1, int w2, int h2, int srcPage, int dstPage)
        {
            if (w1 == 0 || h1 == 0 || w2 == 0 || h2 == 0) return;
            var src = screen.page(srcPage);
            var dst = screen.page(dstPage);
            var row = new byte[w2];
            int last = -1;
            for (int yy = 0; yy < h2; yy += 1)
            {
                int t = (yy * h1) / h2;
                if (t != last)
                {
                    last = t;
                    int s = (y1 + t) * 320 + x1;
                    for (int xx = 0; xx < w2; xx += 1)
                    {
                        int at = s + (xx * w1) / w2;
                        row[xx] = at >= 0 && at < src.Length ? src[at] : (byte)0;   // undefined stored into a Uint8Array is 0
                    }
                }
                int dy = y2 + yy;
                if (dy < 0 || dy >= 200) continue;
                for (int xx = 0; xx < w2; xx += 1) { int dx = x2 + xx; if (dx >= 0 && dx < 320) dst[dy * 320 + dx] = row[xx]; }
            }
            if (dstPage == 0) screen.dirty = true;
        }

        public Func<TimScript, Span16, Task<int>>[] cineOpcodes(bool outro)
        {
            // shared
            Func<TimScript, Span16, Task<int>> setupPaletteFade = (tim, p) => { cineFadeParams(screen.getPalette(0), p[0]); return Task.FromResult(1); };
            Func<TimScript, Span16, Task<int>> loadPalette = (tim, p) =>
            {
                string name = cineString(tim, p[0]);
                if (res.exists(name)) Js.Set(screen.getPalette(0), Js.Slice(res.get(name), 0, 768));
                return Task.FromResult(1);
            };
            Func<TimScript, Span16, Task<int>> setupPaletteFadeEx = (tim, p) => { screen.copyPalette(0, 1); cineFadeParams(screen.getPalette(0), p[0]); return Task.FromResult(1); };
            Func<TimScript, Span16, Task<int>> processWsaFrame = (tim, p) =>
            {
                int animIndex = tim.wsa[p[0]].anim - 1;
                var animations = this.tim.animator.animations;
                var anim = animIndex >= 0 && animIndex < animations.Length ? animations[animIndex] : null;
                if (anim == null || anim.wsa == null) return Task.FromResult(1);
                int factor = Math.Max(0, (int)(short)p[4]);
                int w1 = anim.wsa.width; int h1 = anim.wsa.height;
                int w2 = (w1 * factor) / 100; int h2 = (h1 * factor) / 100;
                this.tim.animator.displayFrame(animIndex, 2, p[1]);
                wsaFrameAnimationStep(anim.x, anim.y, (short)p[2], (short)p[3], w1, h1, w2, h2, 2, 8);
                checkedPageUpdate(8, 4);
                return Task.FromResult(1);
            };
            Func<TimScript, Span16, Task<int>> displayText = (tim, p) => { cineDisplayText(p[0], (short)p[1], outro ? p[2] : 0); return Task.FromResult(1); };
            if (!outro) return new[] { setupPaletteFade, null, loadPalette, setupPaletteFadeEx, processWsaFrame, displayText, null, null };
            return new Func<TimScript, Span16, Task<int>>[]
            {
                setupPaletteFade, null, loadPalette, setupPaletteFadeEx,
                null,
                async (tim, p) =>
                { // fadeInScene: new backdrop blended in through an overlay table
                    string sceneFile = cineString(tim, p[0]);
                    string overlayFile = cineString(tim, p[1]);
                    screen.copyRegion(0, 0, 0, 0, 320, 200, 0, 2, true);
                    if (res.exists($"{sceneFile}.CPS")) screen.loadBitmap(res.get($"{sceneFile}.CPS"), 4, screen.getPalette(0));
                    var overlay = res.exists(overlayFile) ? res.get(overlayFile) : null;
                    for (int i = 0; i < 3; i += 1)
                    {
                        if (overlay != null) screen.copyBlockAndApplyOverlay(4, 0, 0, 2, 0, 0, 320, 200, 0, Js.Slice(overlay, i * 256, i * 256 + 256));
                        screen.copyRegion(0, 0, 0, 0, 320, 200, 2, 0, true);
                        await delay(10 * tickLength);
                    }
                    screen.copyRegion(0, 0, 0, 0, 320, 200, 4, 0, true);
                    return 1;
                },
                (tim, p) => Task.FromResult(1), (tim, p) => Task.FromResult(1),
                async (tim, p) =>
                { // fadeInPalette
                    string name = cineString(tim, p[0]);
                    var pal = new byte[768];
                    if (res.exists(name)) screen.loadBitmap(res.get(name), 2, pal);
                    await screen.fadePalette(pal, p[1]);
                    return 1;
                },
                null, null,
                (tim, p) => { snd_stopMusic(); return Task.FromResult(1); }, // fadeOutSound
                (tim, p) =>
                { // displayAnimFrame
                    int animIndex = tim.wsa[p[0]].anim - 1;
                    var animations = this.tim.animator.animations;
                    var anim = animIndex >= 0 && animIndex < animations.Length ? animations[animIndex] : null;
                    if (anim != null && anim.wsa != null) anim.wsa.displayFrame(p[1], 0, anim.x, anim.y, 0, null, null);
                    return Task.FromResult(1);
                },
                async (tim, p) => { await delay(p[0] * tickLength); return 1; }, // delayForChat
                displayText, null,
            };
        }

        public async Task<bool> runCinematic(string timName, string dipName, string[] paks, bool outro, int character = 0)
        {
            var screen = this.screen;
            string lang = languageExt;
            foreach (var pak in paks) { try { await res.loadPak($"{lang}/{pak}"); } catch (QuitException) { throw; } catch (Exception) { log($"cinematic: {pak} missing"); } }
            try { await res.loadPak($"{lang}/STARTUP.PAK"); } catch (QuitException) { throw; } catch (Exception) { /* fonts and TIM may live elsewhere */ }
            if (!res.exists(timName)) { log($"cinematic {timName} not found"); return false; }
            var tim = this.tim;
            tim.introMode = true;
            tim.drawPage2 = outro ? 0 : 8;
            tim.finished = false;
            tim.palDiff = 0; tim.palDelayInc = 0; tim.palDelayAcc = 0;
            tim.langData = res.exists(dipName) ? res.get(dipName) : null;
            cineTextShown = false;
            cineSkip = false;
            Js.Fill(screen.getPalette(0), (byte)0);
            screen.setScreenPalette(screen.getPalette(0));
            foreach (int page in new[] { 0, 2, 4, 8 }) screen.clearPage(page);
            if (res.exists("NEW8P.FNT")) screen.loadFont("8", res.get("NEW8P.FNT"));
            if (res.exists("INTRO.FNT")) screen.loadFont("intro", res.get("INTRO.FNT"));
            string savedFont = screen.setFont(res.exists("NEW8P.FNT") ? "8" : "9");
            var script = tim.load(timName, cineOpcodes(outro));
            script.isLoLOutro = outro;
            script.lolCharacter = character;
            events.Clear();
            double palNext = 0;
            var cursor = cursorShape;
            cursorShape = null;
            try
            {
                while (!tim.finished && !quit && !cineSkip)
                {
                    // any key or click skips
                    while (events.Count > 0) { var ev = shiftEvent(); if (ev.type == "key" || ev.type == "mousedown") cineSkip = true; }
                    if (cineSkip) break;
                    await tim.exec(script, false);
                    if (!outro) checkedPageUpdate(8, 4);
                    if (tim.palDiff != 0 && palNext < getMillis())
                    {
                        tim.palDelayAcc += tim.palDelayInc;
                        palNext = getMillis() + (tim.palDelayAcc >> 8) * tickLength;
                        tim.palDelayAcc &= 0xff;
                        if (!cineFadeStep(screen.getPalette(0), tim.palDiff)) { screen.setScreenPalette(screen.getPalette(0)); tim.palDiff = 0; }
                    }
                    await delay(10);
                }
            }
            finally
            {
                snd_stopSpeech(false);
                snd_stopMusic();
                for (int i = 0; i < 6; i += 1) tim.animator.reset(i, true);
                tim.langData = null;
                tim.introMode = false;
                tim.drawPage2 = 0;
                tim.finished = false;
                tim.currentTim = null;
                screen.setFont(savedFont);
                cursorShape = cursor;
                events.Clear();
            }
            await screen.fadeToBlack(20);
            return true;
        }

        public async Task<bool> showIntro()
        {
            return await runCinematic("LOLINTRO.TIM", "LOLINTRO.DIP", INTRO_PAKS, false);
        }

        // Ending: the final TIM, then the scrolling credits.
        public async Task showOutro(int character = 0)
        {
            onCall?.Invoke("showOutro", new object[] { character });   // the host's wrapper (main.mjs wrapCount)
            await runCinematic("LOLFINAL.TIM", "LOLFINAL.DIP", OUTRO_PAKS, true, character);
            await showCredits();
        }

        public async Task showCredits()
        {
            var screen = this.screen;
            if (!res.exists("CREDITS.TXT")) return;
            var raw = res.get("CREDITS.TXT");
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i += 1) sb.Append((char)raw[i]);
            string text = sb.ToString();
            // Entries end with \x05 (next entry stays on the same line) or \r (new line); a leading \x03 aligns
            // left, \x04 right, otherwise centred; \x01/\x02 are style markers.
            string cf = screen.setFont("6");
            int lh = screen.fontHeight();
            int gap = lh >> 3;
            var entries = new List<(string text, int x, int y)>();
            int y = 200;
            bool sameLine = false;
            foreach (var part in Regex.Split(text, "(?<=[\\x05\\r])"))
            {
                if (part.Length == 0) continue;
                char code = part[part.Length - 1];
                string body = Regex.Replace(part, "[\\x05\\r]\\z", "");
                int align = 0;
                if (body.Length > 0 && (body[0] == '\x03' || body[0] == '\x04')) { align = body[0]; body = body.Substring(1); }
                body = Regex.Replace(Regex.Replace(body, "^[\\x01\\x02]", ""), "[\\x01-\\x1f]", "");
                if (!sameLine && entries.Count != 0) y += lh + gap;
                int width = screen.textWidth(body);
                int x = align == 3 ? 0 : align == 4 ? 300 - width : (320 - width) >> 1;
                if (body.Length != 0) entries.Add((body, x, y));
                sameLine = code == '\x05';
            }
            int total = y + lh + 40;
            screen.clearPage(0);
            var pal = screen.getPalette(0);
            Js.Fill(pal, (byte)0);
            pal[0xf0 * 3] = 0x3f; pal[0xf0 * 3 + 1] = 0x3a; pal[0xf0 * 3 + 2] = 0x28;
            screen.setScreenPalette(pal);
            events.Clear();
            int cp = screen.curPage;
            screen.curPage = 0;
            for (int scroll = 0; scroll < total && !quit; scroll += 1)
            {
                if (events.Any(ev => ev.type == "key" || ev.type == "mousedown")) break;
                screen.clearPage(0);
                foreach (var e in entries)
                {
                    int yy = e.y - scroll;
                    if (yy < 0 || yy > 200 - lh) continue;
                    screen.printText(e.text, e.x, yy, 0xf0, 0);
                }
                await delay(tickLength);
            }
            screen.curPage = cp;
            screen.setFont(cf);
            events.Clear();
            await screen.fadeToBlack(20);
        }
    }
}
