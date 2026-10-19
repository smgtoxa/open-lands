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

        byte[] outroTextBuffer;

        /// <summary>The ending's own text routine (TIMInterpreter::displayText with a colour): lines at the top of the
        /// screen in the intro font; flags > 0 first puts back what was under the last text, 255 only does that, 0
        /// saves the area first. No text palette.</summary>
        public void cineDisplayTextOutro(int textId, int flags, int color)
        {
            if (!textEnabledFlag && (textId & 0x8000) == 0) return;
            string text = cineTableEntry(textId & 0x7fff) ?? "";
            if (flags > 0 && outroTextBuffer != null) screen.copyBlockToPage(0, 0, 0, 320, 40, outroTextBuffer);
            if (flags == 255) return;
            string cf = screen.setFont("intro");
            var savedColors = screen.textColors.ToArray();
            screen.setTextColor(new byte[] { 0x00, 0xa0, 0xa1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0);
            int spacing = screen.charSpacing;
            screen.charSpacing = 0;
            if (flags == 0) outroTextBuffer = screen.copyRegionToBuffer(0, 0, 0, 320, 40);
            int cp = screen.curPage;
            screen.curPage = 0;
            int y = 0;
            foreach (var line in text.Split('\r'))
            {
                if (line.Length == 0) continue;
                int width = screen.textWidth(line);
                screen.printText(line, flags >= 0 ? (320 - width) >> 1 : 0, y, color, 0x00);
                y += screen.fontHeight() - 4;
            }
            screen.curPage = cp;
            screen.charSpacing = spacing;
            screen.setTextColor(savedColors, 0);
            screen.setFont(cf);
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
            Func<TimScript, Span16, Task<int>> displayText = (tim, p) => { if (outro) cineDisplayTextOutro(p[0], (short)p[1], p[2]); else cineDisplayText(p[0], (short)p[1], 0); return Task.FromResult(1); };
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
                    // the overlay is a 64 KB blend table indexed by (new colour << 8 | old colour), applied 3 times
                    var src = screen.page(4);
                    var dst = screen.page(2);
                    for (int i = 0; i < 3; i += 1)
                    {
                        if (overlay != null && overlay.Length >= 0x10000)
                            for (int k = 0; k < SCREEN_W * SCREEN_H; k += 1) dst[k] = overlay[(src[k] << 8) | dst[k]];
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
                (tim, p) => { snd_fadeOutMusic(); return Task.FromResult(1); }, // fadeOutSound: a fade only, the track is not stopped
                (tim, p) =>
                { // displayAnimFrame
                    int animIndex = tim.wsa[p[0]].anim - 1;
                    var animations = this.tim.animator.animations;
                    var anim = animIndex >= 0 && animIndex < animations.Length ? animations[animIndex] : null;
                    if (anim == null || anim.wsa == null) return Task.FromResult(1);
                    // 0xFFFF keeps the screen as the base for the next frames; a frame is drawn on page 2 and its
                    // rectangle copied to the screen
                    if (p[1] == 0xffff) screen.copyRegion(0, 0, 0, 0, 320, 200, 0, 2, true);
                    else
                    {
                        anim.wsa.displayFrame(p[1], 2, anim.x, anim.y, 0, null, null);
                        screen.copyRegion(anim.x + anim.wsa.xAdd, anim.y + anim.wsa.yAdd, anim.x + anim.wsa.xAdd, anim.y + anim.wsa.yAdd, anim.wsa.width, anim.wsa.height, 2, 0);
                    }
                    return Task.FromResult(1);
                },
                async (tim, p) => { if (!speechEnabledFlag) await delay(p[0] * tickLength); return 1; }, // delayForChat: only without voices
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

        // Ending (FINALE.EXE): the final TIM, the credits, then the lead hero's picture. After the picture the
        // original waits for a key: Esc ends, any other key plays the whole ending again.
        public async Task showOutro(int character = 0)
        {
            onCall?.Invoke("showOutro", new object[] { character });   // the host's wrapper (main.mjs wrapCount)
            outroPlaying = true;
            try
            {
                while (!quit)
                {
                    await runCinematic("LOLFINAL.TIM", "LOLFINAL.DIP", OUTRO_PAKS, true, character);
                    if (quit) return;
                    await showCredits();
                    if (quit || !await showHeroPicture(character)) return;
                }
            }
            finally { outroPlaying = false; }
        }

        /// <summary>The ending is on (a program of its own in the original): the host hides what would be drawn in
        /// its colours, the party's portraits.</summary>
        public bool outroPlaying;

        static readonly string[] OUTRO_HERO_PICTURES = { "KIERAN.CPS", "AK'SHEL.CPS", "MICHAEL.CPS", "CONRAD.CPS" };

        /// <summary>The lead hero's picture (and the hardest difficulty's extra line); true when a key other than
        /// Esc asks for the ending again.</summary>
        async Task<bool> showHeroPicture(int character)
        {
            var pal = screen.getPalette(0);
            Js.Fill(pal, (byte)0);
            string pic = (uint)character < 4 ? OUTRO_HERO_PICTURES[character] : null;
            if (pic != null && res.exists(pic)) screen.loadBitmap(res.get(pic), 0, pal);
            else screen.clearPage(0);
            if (monsterDifficulty == 2 && res.exists("LOLFINAL.DIP"))
            {
                tim.langData = res.get("LOLFINAL.DIP");
                cineDisplayTextOutro(0x8000, 0, 0xdc);
                tim.langData = null;
            }
            await screen.fadePalette(pal, 30);
            events.Clear();
            bool again = false;
            while (!quit)
            {
                var ev = events.Count > 0 ? shiftEvent() : null;
                if (ev != null && (ev.type == "key" || ev.type == "mousedown")) { again = ev.key != "Escape"; break; }
                await delay(tickLength);
            }
            await screen.fadeToBlack(30);
            return again;
        }

        // The ending's animated credits (processCredits): CREDITS.TXT scrolls up over ROOM.CPS while a monster
        // after monster steps out of one of the two doors. The tables are FINALE.EXE's.
        static readonly string[] OUTRO_SHAPE_FILES =
        {
            "AMAZON.SHP", "ARCHRSLG.SHP", "AVIANWRM.SHP", "BANDIT.SHP", "BOAR.SHP", "CABAL.SHP",
            "GUARD.SHP", "HAG.SHP", "HORNET.SHP", "HURZELL.SHP", "IRONGRZR.SHP", "KNOWLES.SHP",
            "LIZARD.SHP", "MANTHA.SHP", "MINOTAUR.SHP", "MORIBUND.SHP", "ORC.SHP", "ORCLDR.SHP",
            "PENTROG.SHP", "RATMAN.SHP", "ROCKLING.SHP", "SCAVNGR.SHP", "STARK.SHP",
            "SWAMPCIT.SHP", "SWAMPMON.SHP", "THUG.SHP", "VIPER.SHP", "XEOB.SHP",
        };
        static readonly int[] OUTRO_FRAMES = { 0, 0, 0, 0, 0, 1, 2, 3, 0, 1, 2, 3, 8, 9, 10, 11, 8, 9, 10, 11, 4, 5, 6, 7 };
        static readonly int[] OUTRO_RIGHT_MONSTER =
        {
            205, 55, 205, 55, 205, 55, 205, 55, 205, 56, 207, 57, 208, 58, 210, 59, 213, 60, 216, 61, 220, 61, 225, 61,
            230, 61, 235, 61, 240, 61, 240, 61, 240, 61, 240, 61, 240, 61, 240, 61, 240, 61, 265, 61, 290, 61, 315, 61,
        };
        static readonly int[] OUTRO_LEFT_MONSTER =
        {
            92, 55, 92, 55, 92, 55, 92, 55, 92, 56, 90, 57, 85, 58, 77, 59, 67, 60, 57, 61, 47, 61, 35, 61,
            35, 61, 35, 61, 35, 61, 35, 61, 35, 61, 35, 61, 35, 61, 35, 61, 35, 61, 10, 61, -20, 61, -45, 61,
        };
        static readonly int[] OUTRO_RIGHT_DOOR =
        {
            200, 41, 200, 29, 200, 17, 200, 5, 200, -7, 200, -7, 200, -7, 200, -7, 200, 5, 200, 17, 200, 29, 200, 41,
            200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41, 200, 41,
        };
        static readonly int[] OUTRO_LEFT_DOOR =
        {
            72, 41, 72, 29, 72, 17, 72, 5, 72, -7, 72, -7, 72, -7, 72, -7, 72, 5, 72, 17, 72, 29, 72, 41,
            72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41, 72, 41,
        };
        static readonly int[] OUTRO_SCALE_X = { 0x50, 0x50, 0x50, 0x50, 0x50, 0x5d, 0x70, 0x85, 0xa0, 0xc0, 0xe2, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100 };
        static readonly int[] OUTRO_SCALE_Y = { 0x4c, 0x4c, 0x4c, 0x4c, 0x4c, 0x59, 0x6b, 0x80, 0x99, 0xb8, 0xd9, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100 };

        sealed class CreditLine { public int x, y, height; public string text; public char code; }

        Shape[] loadOutroShapes(int file)
        {
            var all = LolShapes.decodeShapeFile(Cps.decodeBitmapData(res.get(OUTRO_SHAPE_FILES[file])).data);
            var shapes = new Shape[12];
            for (int i = 0; i < 12; i += 1) { int k = i < 8 ? i : i + 4; shapes[i] = k < all.Length ? all[k] : null; }
            return shapes;
        }

        public async Task showCredits()
        {
            var screen = this.screen;
            if (!res.exists("CREDITS.TXT") || !res.exists("ROOM.CPS")) return;
            snd_stopMusic();
            snd_loadSoundFileByName("LOREFINL");
            snd_playRawTrack(4);
            var pal = screen.getPalette(0);
            screen.loadBitmap(res.get("ROOM.CPS"), 2, pal);
            pal[255 * 3] = pal[255 * 3 + 1] = pal[255 * 3 + 2] = 0;
            await screen.fadeToBlack(30);
            screen.copyRegion(0, 0, 0, 0, 320, 200, 2, 0, true);
            var raw = res.get("CREDITS.TXT");
            var sb = new StringBuilder();
            foreach (byte c in raw) sb.Append((char)c);
            await processCredits(sb.ToString(), 21, 4, 5);
            double end = getMillis() + 120 * tickLength;
            while (getMillis() < end && !quit)
            {
                if (events.Any(ev => ev.type == "key" || ev.type == "mousedown")) break;
                await delay(tickLength);
            }
            events.Clear();
            snd_stopMusic();
            await screen.fadeToBlack(30);
            screen.clearPage(0);
        }

        async Task processCredits(string text, int dimState, int page, int delayTime)
        {
            var screen = this.screen;
            screen.setScreenDim(dimState);
            var dim = screen.curDim;
            screen.clearPage(page);
            screen.clearPage(6);
            var door = res.exists("DOOR.SHP") ? LolShapes.decodeShapeFile(Cps.decodeBitmapData(res.get("DOOR.SHP")).data)[0] : null;
            screen.drawShape(0, door, 0, 0, 22, DRAWSHP.WINREL);
            screen.drawShape(0, door, 0, 0, 23, DRAWSHP.WINREL | DRAWSHP.XFLIP);
            int curShapeFile = 0;
            var shapes = loadOutroShapes(curShapeFile++);
            var monsterPal = res.exists("MONSTERS.PAL") ? res.get("MONSTERS.PAL") : null;
            var pal = screen.getPalette(0);
            if (monsterPal != null) Array.Copy(monsterPal, 0, pal, 88 * 3, 40 * 3);
            await screen.fadePalette(pal, 30);
            var shapeTable = new byte[256];
            for (int i = 0; i < 255; i += 1) shapeTable[i] = (byte)i;   // 255 -> 0
            var lines = new List<CreditLine>();
            int pos = 0;
            int frameCounter = 0, monsterAnimFrame = 0;
            bool needNewShape = false, doorRedraw = true;
            if (res.exists("NEW6P.FNT")) screen.loadFont("new6", res.get("NEW6P.FNT"));   // the ending's own small font
            string savedFont = screen.setFont(res.exists("NEW6P.FNT") ? "new6" : "6");
            int savedSpacing = screen.charSpacing, savedLine = screen.lineSpacing;
            screen.charSpacing = 0;
            screen.lineSpacing = 0;
            events.Clear();
            double waitTimer = getMillis();
            bool pressed = false;
            do
            {
                while (getMillis() < waitTimer && !quit) await delay(tickLength);
                waitTimer = getMillis() + delayTime * tickLength;
                while (lines.Count < 35 && pos < text.Length)
                {
                    var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
                    int y = last == null ? dim.h : last.y + last.height + (last.height >> 3);
                    int stop = text.IndexOfAny(new[] { '\x05', '\r' }, pos);
                    if (stop < 0) stop = text.Length;
                    string body = text.Substring(pos, stop - pos);
                    char code = stop < text.Length ? text[stop] : '\0';
                    pos = stop < text.Length ? stop + 1 : stop;
                    int align = 0;
                    if (body.Length > 0 && (body[0] == '\x03' || body[0] == '\x04')) { align = body[0]; body = body.Substring(1); }
                    if (body.Length > 0 && (body[0] == '\x01' || body[0] == '\x02')) body = body.Substring(1);
                    int h = screen.fontHeight();
                    int x = align == 3 ? 0 : align == 4 ? 300 - screen.textWidth(body) : ((dim.w << 3) - screen.textWidth(body)) / 2;
                    if (last != null && last.code == '\x05') y -= last.height + (last.height >> 3);
                    lines.Add(new CreditLine { x = x, y = y, height = h, text = body, code = code });
                }
                frameCounter += 1;
                if (frameCounter % 3 != 0) screen.copyRegion(0, 0, 0, 0, 320, 200, 6, page, true);
                else if (monsterAnimFrame == 0 && doorRedraw)
                {
                    screen.copyRegion(0, 0, 0, 0, 320, 200, 2, page, true);
                    screen.drawShape(page, door, 0, 0, 22, DRAWSHP.WINREL);
                    screen.drawShape(page, door, 0, 0, 23, DRAWSHP.WINREL | DRAWSHP.XFLIP);
                    frameCounter -= 1;
                    doorRedraw = false;
                }
                else
                {
                    if (monsterAnimFrame == 0) screen.setScreenPalette(pal);
                    screen.copyRegion(0, 0, 0, 0, 320, 200, 2, page, true);
                    var monster = shapes[OUTRO_FRAMES[monsterAnimFrame]];
                    bool right = ((curShapeFile - 1) & 1) != 0;
                    int f2 = monsterAnimFrame * 2;
                    int doorSD = right ? 23 : 22;
                    int doorX = right ? OUTRO_RIGHT_DOOR[f2] : OUTRO_LEFT_DOOR[f2], doorY = right ? OUTRO_RIGHT_DOOR[f2 + 1] : OUTRO_LEFT_DOOR[f2 + 1];
                    int mx = right ? OUTRO_RIGHT_MONSTER[f2] : OUTRO_LEFT_MONSTER[f2], my = right ? OUTRO_RIGHT_MONSTER[f2 + 1] : OUTRO_LEFT_MONSTER[f2 + 1];
                    if (right) screen.drawShape(page, door, 0, 0, 22, DRAWSHP.WINREL);
                    else screen.drawShape(page, door, 0, 0, 23, DRAWSHP.WINREL | DRAWSHP.XFLIP);
                    int doorFlip = doorSD == 22 ? 0 : DRAWSHP.XFLIP;
                    if (monsterAnimFrame >= 8) screen.drawShape(page, door, doorX, doorY, doorSD, doorFlip);
                    int mflip = (!right || monsterAnimFrame < 20) ? 0 : DRAWSHP.XFLIP;
                    screen.drawShape(page, monster, mx, my, 0, DRAWSHP.FADE | DRAWSHP.SCALE | mflip,
                        new DrawShapeOpts { fadeTable = shapeTable, fadeLevel = 1, scaleW = OUTRO_SCALE_X[monsterAnimFrame], scaleH = OUTRO_SCALE_Y[monsterAnimFrame] });
                    if (monsterAnimFrame < 8) screen.drawShape(page, door, doorX, doorY, doorSD, doorFlip);
                    screen.copyRegion(0, 0, 0, 0, 320, 200, page, 6, true);
                    doorRedraw = true;
                    monsterAnimFrame = (monsterAnimFrame + 1) % 24;
                    needNewShape = monsterAnimFrame == 0;
                }
                int cp = screen.curPage;
                screen.curPage = page;
                foreach (var l in lines)
                {
                    if (l.y < dim.h) screen.printText(l.text, (dim.sx << 3) + l.x, dim.sy + l.y, 0xdc, 0);
                    l.y -= 1;
                }
                screen.curPage = cp;
                screen.copyRegion(0, dim.sy, 0, dim.sy, 320, dim.h, page, 0, true);
                if (lines.Count > 0 && lines[0].y < -10) lines.RemoveAt(0);
                if (needNewShape)
                {
                    curShapeFile += 1;
                    if (curShapeFile == 16) curShapeFile += 2;
                    if (curShapeFile == 6) curShapeFile += 2;
                    curShapeFile %= 28;
                    shapes = loadOutroShapes(curShapeFile);
                    if (monsterPal != null) { Array.Copy(monsterPal, curShapeFile * 40 * 3, pal, 88 * 3, 40 * 3); screen.setScreenPalette(pal); }
                    needNewShape = false;
                }
                pressed = events.Any(ev => ev.type == "key" || ev.type == "mousedown");
                events.Clear();
            } while (lines.Count > 0 && !pressed && !quit);
            screen.charSpacing = savedSpacing;
            screen.lineSpacing = savedLine;
            screen.setFont(savedFont);
            screen.setScreenDim(0);
        }
    }
}
