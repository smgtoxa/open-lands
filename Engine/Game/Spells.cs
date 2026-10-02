// src/game/spells.mjs: dialogue sequences, spells and misc effects from lol.cpp.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>spells.mjs generateTempData: lvlTempData[level - 1] = { walls, flags, monsters, flyingObjects, monsterDifficulty }</summary>
    public sealed class LvlTempData
    {
        public byte[][] walls;
        public int[] flags;
        public Monster[] monsters;
        public FlyingObject[] flyingObjects;
        public int monsterDifficulty;
    }

    public sealed partial class LandsOfLore
    {
        public byte[] healOverlay;
        /// <summary>indexed by spell; extra-spells.mjs assigns entries past the end (plain JS array)</summary>
        public List<Func<ActiveSpell, Task<int>>> spellProcs;
        public int cursorHotX, cursorHotY;
        /// <summary>host hook (main.mjs): the cursor shape changed</summary>
        public Action<Shape, int, int> onCursorChanged;

        /// <summary>{ ...m, equipmentShapes: m.equipmentShapes.slice() }</summary>
        public static Monster Spells_cloneMonster(Monster m)
        {
            var r = m.Spread();   // every key the object has, as the JS spread copies them
            r.equipmentShapes = (int[])m.equipmentShapes.Clone();
            return r;
        }

        /// <summary>{ ...f }</summary>
        public static FlyingObject Spells_cloneFlyingObject(FlyingObject f) => f.Spread();

        /// <summary>String.prototype.replace with a string pattern: the first occurrence only.</summary>
        static string Spells_replace(string s, string pattern, string replacement)
        {
            int i = s.IndexOf(pattern, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + replacement + s.Substring(i + pattern.Length);
        }

        public void initSpells()
        {
            healOverlay = null;
            spellProcs = new List<Func<ActiveSpell, Task<int>>>
            {
                (a) => castSpark(a), (a) => castHeal(a), (a) => castIce(a), (a) => castFireball(a),
                (a) => castHandOfFate(a), (a) => castMistOfDoom(a), (a) => castLightning(a), null,
                (a) => castFog(), (a) => castSwarm(a), null, null, (a) => castVaelansCube(), null, null, null,
                (a) => castGuardian(a), null, null, null, (a) => castHealOnSingleCharacter(a),
            };
        }

        public WsaPlayer openWsa(string name, int flags = 0, byte[] palette = null)
        {
            var wsa = new WsaPlayer(screen);
            wsa.name = name.ToUpperInvariant();
            if (!res.exists(name)) throw new Exception($"Missing {name}");
            wsa.open(res.get(name), flags, palette);
            return wsa;
        }

        // ---- scene window dialogue helpers (lol.cpp) ----
        public void setupScreenDims()
        {
            if (textEnabled())
            {
                screen.modifyScreenDim(4, 11, 124, 28, 45);
                screen.modifyScreenDim(5, 85, 123, 233, 54);
            }
            else
            {
                screen.modifyScreenDim(4, 11, 124, 28, 9);
                screen.modifyScreenDim(5, 85, 123, 233, 18);
            }
        }

        public async Task initSceneWindowDialogue(int controlMode)
        {
            resetPortraitsAndDisableSysTimer();
            gui_prepareForSequence(112, 0, 176, 120, controlMode);
            updateFlags |= 3;
            await txt.setupField(true);
            await txt.expandField();
            setupScreenDims();
            gui_disableControls(controlMode);
        }

        public void toggleSelectedCharacterFrame(int mode)
        {
            if (countActiveCharacters() == 1) return;
            int col = mode != 0 ? 212 : 1;
            int cp = screen.curPage;
            screen.curPage = 0;
            int x = activeCharsXpos[selectedCharacter];
            screen.drawBox(x, 143, x + 65, 176, col);
            screen.curPage = cp;
        }

        public void gui_prepareForSequence(int x, int y, int w, int h, int buttonFlags)
        {
            setSequenceButtons(x, y, w, h, buttonFlags);
            seqWindowX1 = x;
            seqWindowY1 = y;
            seqWindowX2 = x + w;
            seqWindowY2 = y + h;
            setMouseCursorToItemInHand();
            lastMouseRegion = -1;
            if (w == 320)
            {
                setLampMode(false);
                lampStatusSuspended = true;
            }
        }

        public void gui_specialSceneSuspendControls(int controlMode)
        {
            if (controlMode != 0)
            {
                updateFlags |= 4;
                setLampMode(false);
            }
            updateFlags |= 1;
            specialSceneFlag = 1;
            currentControlMode = controlMode;
            calcCharPortraitXpos();
        }

        public void gui_specialSceneRestoreControls(int restoreLamp)
        {
            if (restoreLamp != 0)
            {
                updateFlags &= 0xfffa;
                resetLampStatus();
            }
            updateFlags &= 0xfffe;
            specialSceneFlag = 0;
        }

        public async Task restoreAfterSceneWindowDialogue(int redraw)
        {
            ui?.Invoke("choices", new object[] { null });
            gui_enableControls();
            await txt.setupField(false);
            updateFlags &= 0xffdf;
            setDefaultButtonState();
            for (int i = 0; i < 6; i += 1) tim.freeAnimStruct(i);
            updateFlags = 0;
            if (redraw != 0)
            {
                if (screen.fadeFlag != 2) await screen.fadeClearSceneWindow(10);
                gui_drawPlayField();
                await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
                screen.fadeFlag = 0;
            }
            needSceneRestore = 0;
            enableSysTimer(2);
        }

        public void initDialogueSequence(int controlMode, int pageNum)
        {
            if (controlMode != 0)
            {
                timerDisable(11);
                fadeText = false;
                int cp = screen.curPage;
                screen.curPage = pageNum;
                screen.fillRect(0, 128, 319, 199, 1);
                gui_drawBox(0, 129, 320, 71, 136, 251, -1);
                gui_drawBox(1, 130, 318, 69, 136, 251, 252);
                screen.modifyScreenDim(5, 8, 131, 306, 66);
                screen.modifyScreenDim(4, 1, 133, 38, 60);
                txt.clearDim(4);
                updateFlags |= 2;
                currentControlMode = controlMode;
                calcCharPortraitXpos();
                if (!textEnabled() && (controlMode & 2) == 0)
                {
                    int nc = countActiveCharacters();
                    for (int i = 0; i < nc; i += 1)
                    {
                        portraitSpeechAnimMode = 2;
                        updateCharNum = i;
                        screen.drawShape(0, gameShapes[88], activeCharsXpos[updateCharNum] + 8, 142, 0, 0);
                        stopPortraitSpeechAnim();
                    }
                }
                screen.curPage = cp;
            }
            else
            {
                queueAsync(async () =>
                {
                    await txt.setupField(true);
                    await txt.expandField();
                    setupScreenDims();
                    txt.clearDim(4);
                });
            }
            currentControlMode = controlMode;
            dialogueField = true;
        }

        public void restoreAfterDialogueSequence(int controlMode)
        {
            if (!dialogueField) return;
            ui?.Invoke("choices", new object[] { null });
            stopPortraitSpeechAnim();
            currentControlMode = controlMode;
            calcCharPortraitXpos();
            if (currentControlMode != 0)
            {
                screen.modifyScreenDim(4, 11, 124, 28, 45);
                screen.modifyScreenDim(5, 85, 123, 233, 54);
                updateFlags &= 0xfffd;
            }
            else
            {
                var d = screen.getScreenDim(5);
                screen.fillRect(d.sx, d.sy, d.sx + d.w - 2, d.sy + d.h - 2, d.col2);
                txt.clearDim(4);
                queueAsync(() => txt.setupField(false));
            }
            dialogueField = false;
        }

        public void resetPortraitsAndDisableSysTimer()
        {
            needSceneRestore = 1;
            if (!textEnabled() || (currentControlMode & 2) == 0) timerUpdatePortraitAnimations(1);
            disableSysTimer(2);
        }

        public void generateFlashPalette(byte[] src, byte[] dst, int colorFlags)
        {
            Js.Set(dst, Js.Slice(src, 0, 6), 0);
            for (int i = 2; i < 128; i += 1)
            {
                for (int ii = 0; ii < 3; ii += 1)
                {
                    int t = src[i * 3 + ii] & 0x3f;
                    if ((colorFlags & (1 << ii)) != 0) t += (0x3f - t) >> 1;
                    else t -= t >> 1;
                    dst[i * 3 + ii] = (byte)t;
                }
            }
            Js.Set(dst, Js.Slice(src, 128 * 3), 128 * 3);
        }

        // ---- cursor ----
        public void setMouseCursor(int hotX, int hotY, Shape shape)
        {
            cursorShape = shape;
            cursorHotX = hotX;
            cursorHotY = hotY;
            onCursorChanged?.Invoke(shape, hotX, hotY);
        }

        public void setMouseCursorToIcon(int icon)
        {
            flagsTable[31] |= 0x02;
            int i = itemProperties.Count != 0 ? itemProperties[itemsInPlay[itemInHand].itemPropertyIndex].shpIndex : -1;
            if (i == icon) return;
            setMouseCursor(0, 0, itemIconShapes[icon]);
        }

        public void setMouseCursorToItemInHand()
        {
            flagsTable[31] &= 0xfd;
            int o = itemInHand == 0 ? 0 : 10;
            setMouseCursor(o, o, getItemIconShapePtr(itemInHand));
        }

        public Shape getItemIconShapePtr(int index)
        {
            int prop = index >= 0 && index < itemsInPlay.Length && itemsInPlay[index] != null ? itemsInPlay[index].itemPropertyIndex : 0;
            if (prop < 0 || prop >= itemProperties.Count || itemProperties[prop] == null) return null;
            int ix = itemProperties[prop].shpIndex;
            if ((itemProperties[prop].flags & 0x200) != 0) ix += (itemsInPlay[index].shpCurFrame_flg & 0x1fff) - 1;
            var shape = ix >= 0 && ix < itemIconShapes.Length ? itemIconShapes[ix] : null;
            // Items the port added carry their own artwork. The engine keeps drawing the borrowed shape
            // into the 320x200 screen (it only reads the pixel fields); the host draws `art` instead.
            var extra = extraItems != null && extraItems.TryGetValue(prop, out var e) ? e : null;
            return extra != null && extra.art != null && shape != null
                ? new Shape { width = shape.width, height = shape.height, pixels = shape.pixels, colorTable = shape.colorTable, colorCount = shape.colorCount, key = shape.key, art = extra.art }
                : shape;
        }

        // ---- spells ----
        public async Task<int> castSpell(int charNum, int spellType, int spellLevel)
        {
            onCall?.Invoke("castSpell", new object[] { charNum, spellType, spellLevel });   // the host's wrapper (main.mjs wrapCount)
            var sp = @static.SpellProperties[spellType];
            activeSpell = new ActiveSpell { charNum = charNum, spell = spellType, p = sp, level = Math.Abs(spellLevel), target = 0 };
            if ((sp.flags & 0x100) != 0 && testWallFlag(calcNewBlockPosition(currentBlock, currentDirection), currentDirection, 1))
            {
                txt.printMessage(2, getLangString(0x4257));
                return 0;
            }
            var proc = spellType < spellProcs.Count ? spellProcs[spellType] : null;
            if (charNum < 0)
            {
                activeSpell.charNum = charNum * -1 - 1;
                if (proc != null) return await proc(activeSpell);
            }
            else
            {
                if (sp.mpRequired[spellLevel] > characters[charNum].magicPointsCur) return 0;
                if (sp.hpRequired[spellLevel] >= characters[charNum].hitPointsCur) return 0;
                setCharacterMagicOrHitPoints(charNum, 1, -sp.mpRequired[spellLevel], 1);
                setCharacterMagicOrHitPoints(charNum, 0, -sp.hpRequired[spellLevel], 1);
                gui_drawCharPortraitWithStats(charNum);
                if (proc != null) await proc(activeSpell);
            }
            return 1;
        }

        public async Task<int> castSpark(ActiveSpell a)
        {
            await processMagicSpark(a.charNum, a.level);
            return 1;
        }

        public async Task<int> castHeal(ActiveSpell a)
        {
            if (a.level < 3) processMagicHealSelectTarget();
            else await processMagicHeal(-1, a.level);
            return 1;
        }

        public async Task<int> castHealOnSingleCharacter(ActiveSpell a)
        {
            await processMagicHeal(a.target, a.level);
            return 1;
        }

        public int checkMagic(int charNum, int spellNum, int spellLevel)
        {
            var sp = @static.SpellProperties[spellNum];
            var c = characters[charNum];
            if (sp.mpRequired[spellLevel] > c.magicPointsCur)
            {
                if (characterSays(0x4043, c.id, true) != 0) txt.printMessage(6, Spells_replace(getLangString(0x4043), "%s", c.name));
                return 1;
            }
            if (sp.hpRequired[spellLevel] >= c.hitPointsCur)
            {
                txt.printMessage(2, Spells_replace(getLangString(0x4179), "%s", c.name));
                return 1;
            }
            return 0;
        }

        public (int, int) getSpellTargetBlock(int currentBlock, int direction, int maxDistance)
        {
            int targetBlock = 0xffff;
            int c = calcNewBlockPosition(currentBlock, direction);
            int i = 0;
            for (; i < maxDistance; i += 1)
            {
                if ((levelBlockProperties[currentBlock].assignedObjects & 0x8000) != 0) return (i, currentBlock);
                if ((wllWallFlags[levelBlockProperties[c].walls[direction ^ 2]] & 7) != 0) return (i, c);
                currentBlock = c;
                c = calcNewBlockPosition(currentBlock, direction);
            }
            return (i, targetBlock);
        }

        public void inflictMagicalDamage(int target, int attacker, int damage, int index, int hitType)
        {
            hitType = hitType != 0 ? 1 : 2;
            damage = calcInflictableDamagePerItem(attacker, target, damage, index, hitType);
            inflictDamage(target, damage, attacker, 2, index);
        }

        public void inflictMagicalDamageForBlock(int block, int attacker, int damage, int index)
        {
            int o = levelBlockProperties[block].assignedObjects;
            while ((o & 0x8000) != 0)
            {
                inflictDamage(o, calcInflictableDamagePerItem(attacker, o, damage, index, 2), attacker, 2, index);
                if ((monsters[o & 0x7fff].flags & 0x20) != 0 && currentLevel != 22) break;
                o = monsters[o & 0x7fff].nextAssignedObject;
            }
        }

        // pal1/pal2/fadeDelay: optional palette fade running alongside the animation (fadeDelay in ticks).
        public async Task playSpellAnimation(WsaPlayer mov, int firstFrame, int lastFrame, int frameDelay, int x, int y, Action<WsaPlayer, int, int> callback, bool restoreScreen, byte[] pal1 = null, byte[] pal2 = null, int fadeDelay = 0)
        {
            int w = mov != null ? mov.width : 0;
            int h = mov != null ? mov.height : 0;
            if (x < 0) w += x;
            if (y < 0) h += y;
            double startTime = getMillis();
            int dir = lastFrame >= firstFrame ? 1 : -1;
            int curFrame = firstFrame;
            bool fin = false;
            while (!fin)
            {
                double delayTimer = getMillis() + tickLength * frameDelay;
                if (mov != null || callback != null) screen.copyPage(12, 2);
                callback?.Invoke(mov, x, y);
                if (mov != null) mov.displayFrame(curFrame % mov.numFrames, 2, x, y, 0x5000, transparencyTable1, transparencyTable2);
                if (mov != null || callback != null) screen.copyRegion(x, y, x, y, w, h, 2, 0, true);
                if (pal1 != null && pal2 != null)
                {
                    double del = Math.Max(0, delayTimer - getMillis());
                    do
                    {
                        double step = Math.Min(del, tickLength);
                        if (!timedPaletteFadeStep(pal1, pal2, getMillis() - startTime, tickLength * fadeDelay) && mov == null) return;
                        if (del != 0) { await delay(step); del -= step; } else await delay(0);
                    } while (del > 0);
                }
                else await delayUntil(delayTimer);
                if (mov == null) continue;
                curFrame += dir;
                if ((dir > 0 && curFrame >= lastFrame) || (dir < 0 && curFrame < lastFrame)) fin = true;
            }
            if (restoreScreen && mov != null)
            {
                screen.copyPage(12, 2);
                screen.copyRegion(x, y, x, y, w, h, 2, 0, true);
            }
        }

        public async Task<int> processMagicSpark(int charNum, int spellLevel)
        {
            screen.copyPage(0, 12);
            var mov = openWsa("SPARK1.WSA");
            snd_playSoundEffect(72, -1);
            await playSpellAnimation(mov, 0, 7, 4, activeCharsXpos[charNum] - 2, 138, null, false);
            screen.copyPage(12, 0);
            var (dist, targetBlock) = getSpellTargetBlock(currentBlock, currentDirection, 4);
            int target = getNearestMonsterFromCharacterForBlock(targetBlock, charNum);
            int[] dmg = { 7, 15, 25, 60 };
            if (target != 0xffff)
            {
                inflictMagicalDamage(target, charNum, dmg[spellLevel], 5, 0);
                updateDrawPage2();
                gui_drawScene(0);
                screen.copyPage(0, 12);
            }
            mov = openWsa("SPARK2.WSA");
            int numFrames = mov.numFrames;
            var wX = new List<int>();
            var wY = new List<int>();
            var wFrames = new List<int>();
            for (int i = 0; i < 6; i += 1)
            {
                wX.Add((presentationRandom(0x7fff) % 64) + ((176 - mov.width) >> 1) + 80);
                wY.Add((presentationRandom(0x7fff) % 32) + ((120 - mov.height) >> 1) - 16);
                wFrames.Add(i << 1);
            }
            for (int i = 0, d = (spellLevel << 1) + 12; i < d; i += 1)
            {
                double delayTimer = getMillis() + 4 * tickLength;
                screen.copyPage(12, 2);
                for (int ii = 0; ii <= spellLevel; ii += 1)
                {
                    if (wFrames[ii] >= i || wFrames[ii] + 13 <= i) continue;
                    if (i - wFrames[ii] == 1) snd_playSoundEffect(162, -1);
                    mov.displayFrame((i - wFrames[ii] + (dist << 4)) % numFrames, 2, wX[ii], wY[ii], 0x5000, transparencyTable1, transparencyTable2);
                    screen.copyRegion(wX[ii], wY[ii], wX[ii], wY[ii], mov.width, mov.height, 2, 0, true);
                }
                if (i < d - 1) await delayUntil(delayTimer);
            }
            screen.copyPage(12, 2);
            updateDrawPage2();
            sceneUpdateRequired = true;
            return 1;
        }

        public int processMagicHealSelectTarget()
        {
            txt.printMessage(0, getLangString(0x4040));
            gui_resetButtonList();
            gui_setFaceFramesControlButtons(81, 0);
            gui_initButtonsFromList(@static.ButtonList8);
            awaitingSpellTarget = true; // the host highlights the party cards: click one to pick the target
            spellTargetSpell = activeSpell; // exactly this cast; anything else makes the prompt stale
            ui?.Invoke("target", new object[] { true });
            return 1;
        }

        public async Task<int> processMagicHeal(int charNum, int spellLevel)
        {
            if (healOverlay == null)
            {
                healOverlay = new byte[256];
                screen.generateGrayOverlay(screen.getPalette(1), healOverlay, 52, 22, 20, 0, 256, true);
            }
            var HF = @static.HealShapeFrames;
            int[] healShpFrames;
            int[] healiShpFrames;
            bool curePoison = false;
            int points;
            if (spellLevel == 0) { points = 25; healShpFrames = Js.Slice(HF, 0, 16); healiShpFrames = Js.Slice(HF, 32, 48); }
            else if (spellLevel == 1) { points = 45; healShpFrames = Js.Slice(HF, 16, 32); healiShpFrames = Js.Slice(HF, 48, 64); }
            else if (spellLevel > 3) { curePoison = true; points = spellLevel; healShpFrames = Js.Slice(HF, 16, 32); healiShpFrames = Js.Slice(HF, 64, 80); }
            else { curePoison = true; points = 10000; healShpFrames = Js.Slice(HF, 16, 32); healiShpFrames = Js.Slice(HF, 64, 80); }
            int ch = 0;
            int n = 4;
            if (charNum != -1) { ch = charNum; n = charNum + 1; }
            int[] pX = { 0, 0, 0, 0 };
            const int pY = 138;
            int[] diff = { 0, 0, 0, 0 };
            int[] pts = { 0, 0, 0, 0 };
            for (int c = ch; c < n; c += 1)
            {
                if ((characters[c].flags & 1) == 0) continue;
                pX[c] = activeCharsXpos[c] - 6;
                characters[c].damageSuffered = 0;
                int dmg = characters[c].hitPointsMax - characters[c].hitPointsCur;
                diff[c] = dmg < points ? dmg : points;
                screen.copyRegion(pX[c], pY, c * 77, 32, 77, 44, 0, 2, true);
            }
            int cp = screen.curPage;
            screen.curPage = 2;
            snd_playSoundEffect(68, -1);
            for (int i = 0; i < 16; i += 1)
            {
                double delayTimer = getMillis() + 4 * tickLength;
                for (int c = ch; c < n; c += 1)
                {
                    if ((characters[c].flags & 1) == 0) continue;
                    screen.copyRegion(c * 77, 32, pX[c], pY, 77, 44, 2, 2, true);
                    pts[c] &= 0xff;
                    pts[c] += (diff[c] << 8) / 16;
                    increaseCharacterHitpoints(c, pts[c] >> 8, true);
                    gui_drawCharPortraitWithStats(c);
                    screen.drawShape(2, healShapes[healShpFrames[i]], pX[c], pY, 0, 0x1000, new DrawShapeOpts { transparency1 = transparencyTable1, transparency2 = transparencyTable2 });
                    screen.fillRect(0, 0, 31, 31, 0);
                    screen.drawShape(screen.curPage, healiShapes[healiShpFrames[i]], 0, 0, 0, 0);
                    screen.applyOverlaySpecial(screen.curPage, 0, 0, 2, pX[c] + 7, pY + 6, 32, 32, 0, 0, healOverlay);
                    screen.copyRegion(pX[c], pY, pX[c], pY, 77, 44, 2, 0, true);
                }
                await delayUntil(delayTimer);
            }
            for (int c = ch; c < n; c += 1)
            {
                if ((characters[c].flags & 1) == 0) continue;
                screen.copyRegion(c * 77, 32, pX[c], pY, 77, 44, 2, 2, true);
                if (curePoison) removeCharacterEffects(characters[c], 4, 4);
                gui_drawCharPortraitWithStats(c);
                screen.copyRegion(pX[c], pY, pX[c], pY, 77, 44, 2, 0, true);
            }
            screen.curPage = cp;
            updateDrawPage2();
            return 1;
        }

        public async Task drinkBezelCup(int numUses, int charNum)
        {
            int cp = screen.curPage;
            screen.curPage = 2;
            snd_playSoundEffect(73, -1);
            var mov = openWsa("BEZEL.WSA");
            int x = activeCharsXpos[charNum] - 11;
            const int y = 124;
            int w = mov.width;
            int h = mov.height;
            screen.copyRegion(x, y, 0, 0, w, h, 0, 2, true);
            int[] bezelAnimData = { 0, 26, 20, 27, 61, 55, 62, 92, 86, 93, 131, 125 };
            int frm = bezelAnimData[numUses * 3];
            int hpDiff = characters[charNum].hitPointsMax - characters[charNum].hitPointsCur;
            int step = 0;
            do
            {
                step = (step & 0xff) + (hpDiff * 256) / bezelAnimData[numUses * 3 + 1];
                increaseCharacterHitpoints(charNum, step >> 8, true);
                gui_drawCharPortraitWithStats(charNum);
                double etime = getMillis() + 4 * tickLength;
                screen.copyRegion(0, 0, x, y, w, h, 2, 2, true);
                mov.displayFrame(frm, 2, x, y, 0x5000, transparencyTable1, transparencyTable2);
                screen.copyRegion(x, y, x, y, w, h, 2, 0, true);
                await delayUntil(etime);
            } while (++frm < bezelAnimData[numUses * 3 + 1]);
            characters[charNum].hitPointsCur = characters[charNum].hitPointsMax;
            screen.copyRegion(0, 0, x, y, w, h, 2, 2, true);
            removeCharacterEffects(characters[charNum], 4, 4);
            gui_drawCharPortraitWithStats(charNum);
            screen.copyRegion(x, y, x, y, w, h, 2, 0, true);
            screen.curPage = cp;
        }

        public async Task addSpellToScroll(int spell, int charNum)
        {
            bool assigned = false;
            int slot = 0;
            for (int i = 0; i < SPELL_SLOTS; i += 1)
            {
                if (!assigned && availableSpells[i] == -1)
                {
                    assigned = true;
                    slot = i;
                }
                if (availableSpells[i] == spell)
                {
                    txt.printMessage(2, getLangString(0x42d0));
                    return;
                }
            }
            // A full scroll used to fall through with slot still 0 and write over whatever was in the first
            // line, so a spell bought from the imp could vanish when the next one was learnt. Say so instead.
            if (!assigned)
            {
                txt.printMessage(2, "There is no room left on the scroll.");
                return;
            }
            if (spell > 1) await transferSpellToScollAnimation(charNum, spell, slot - 1);
            availableSpells[slot] = (sbyte)spell;
            gui_enableDefaultPlayfieldButtons();
        }

        public async Task transferSpellToScollAnimation(int charNum, int spell, int slot)
        {
            var S = @static;
            int cX = 16 + activeCharsXpos[charNum];
            if (slot != 1)
            {
                screen.loadBitmap(res.get("PLAYFLD.CPS"), 3, null);
                screen.copyRegion(8, 0, 216, 0, 96, 120, 3, 3, true);
                screen.copyPage(3, 10);
                for (int i = 0; i < 9; i += 1)
                {
                    int h = (slot + 1) * 9 + i + 1;
                    double delayTimer = getMillis() + tickLength;
                    screen.copyPage(10, 3);
                    screen.copyRegion(216, 0, 8, 0, 96, 120, 3, 3, true);
                    screen.copyRegion(112, 0, 12, 0, 87, 15, 2, 2, true);
                    screen.copyRegion(201, 1, 17, 15, 6, h, 2, 2, true);
                    screen.copyRegion(208, 1, 89, 15, 6, h, 2, 2, true);
                    int cp = screen.curPage;
                    screen.curPage = 2;
                    screen.fillRect(21, 15, 89, h + 15, 206);
                    screen.copyRegion(112, 16, 12, h + 15, 87, 14, 2, 2, true);
                    int y = 15;
                    var of = screen.setFont("9");
                    for (int ii = 0; ii < SPELL_SLOTS; ii += 1)
                    {
                        if (availableSpells[ii] == -1) continue;
                        screen.fprintString(spellName(availableSpells[ii]), 24, y, ii == selectedSpell ? 132 : 1, 0, 0);
                        y += 9;
                    }
                    screen.setFont(of);
                    screen.curPage = cp;
                    screen.copyRegion(8, 0, 8, 0, 96, 120, 3, 0, true);
                    await delayUntil(delayTimer);
                }
            }
            screen.copyPage(0, 12);
            int vX = S.SpellbookCoords[slot << 1] + 32;
            int vY = S.SpellbookCoords[(slot << 1) + 1] + 5;
            string wsaFile = $"WRITE{spell}{(lang == 1 ? "F" : lang == 0 ? "E" : "G")}.WSA";
            snd_playSoundEffect(S.SpellbookAnim[(spell << 2) + 3], -1);
            snd_playSoundEffect(95, -1);
            var mov = openWsa("GETSPELL.WSA");
            snd_playSoundEffect(128, -1);
            await playSpellAnimation(mov, 0, 25, 5, activeCharsXpos[charNum], 148, null, true);
            snd_playSoundEffect(128, -1);
            await playSpellAnimation(mov, 26, 52, 5, activeCharsXpos[charNum], 148, null, true);
            for (int i = 16; i > 0; i -= 1)
            {
                double delayTimer = getMillis() + tickLength;
                screen.copyPage(12, 2);
                int wsaX = vX + ((((cX - vX) << 8) / 16 * i) >> 8) - 16;
                int wsaY = vY + ((((160 - vY) << 8) / 16 * i) >> 8) - 16;
                mov.displayFrame(51, 2, wsaX, wsaY, 0x5000, transparencyTable1, transparencyTable2);
                screen.copyRegion(wsaX, wsaY, wsaX, wsaY, mov.width + 48, mov.height + 48, 2, 0, true);
                await delayUntil(delayTimer);
            }
            mov = openWsa("SPELLEXP.WSA");
            snd_playSoundEffect(168, -1);
            await playSpellAnimation(mov, 0, 8, 3, vX - 44, vY - 38, null, true);
            mov = openWsa("WRITING.WSA");
            await playSpellAnimation(mov, 0, 6, 5, S.SpellbookCoords[slot << 1], S.SpellbookCoords[(slot << 1) + 1], null, false);
            if (res.exists(wsaFile))
            {
                mov = openWsa(wsaFile);
                snd_playSoundEffect(S.SpellbookAnim[(spell << 2) + 3], -1);
                await playSpellAnimation(mov, S.SpellbookAnim[(spell << 2) + 1], S.SpellbookAnim[(spell << 2) + 2], S.SpellbookAnim[spell << 2], S.SpellbookCoords[slot << 1], S.SpellbookCoords[(slot << 1) + 1], null, false);
            }
            gui_drawScene(2);
            updateDrawPage2();
        }

        public async Task processGasExplosion(int soundId)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            int[] sounds = { 0x62, 0xa7, 0xa7, 0xa8 };
            snd_playSoundEffect(sounds[soundId], -1);
            var (dist, _) = getSpellTargetBlock(currentBlock, currentDirection, 3);
            if (dist != 0)
            {
                var mov = openWsa($"GASEXP{dist}.WSA", 1);
                await playSpellAnimation(mov, 0, 6, 1, ((176 - mov.width) >> 1) + 112, (120 - mov.height) >> 1, null, false);
            }
            else
            {
                var p2 = screen.getPalette(3);
                Js.Set(p2, screen.getPalette(1));
                for (int i = 1; i < 128; i += 1) p2[i * 3] = 0x3f;
                double ctime = getMillis();
                while (timedPaletteFadeStep(screen.getPalette(0), p2, getMillis() - ctime, 10)) await delay(tickLength);
                ctime = getMillis();
                while (timedPaletteFadeStep(p2, screen.getPalette(0), getMillis() - ctime, 50)) await delay(tickLength);
            }
            screen.copyPage(12, 2);
            screen.curPage = cp;
            updateDrawPage2();
            sceneUpdateRequired = true;
            gui_drawScene(0);
        }

        public async Task pitDropScroll(int numSteps)
        {
            screen.copyRegion(112, 0, 0, 0, 176, 120, 0, 6, true);
            double etime = getMillis();
            for (int i = 0; i < numSteps; i += 1)
            {
                etime += tickLength;
                int ys = ((30720 / numSteps) * i) >> 8;
                screen.copyRegion(0, ys, 112, 0, 176, 120 - ys, 6, 0, true);
                screen.copyRegion(112, 0, 112, 120 - ys, 176, ys, 2, 0, true);
                await delayUntil(etime);
            }
            etime += tickLength;
            screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
            await delayUntil(etime);
            updateDrawPage2();
        }

        // Plays a WSA into a small box on page 0 (ruby of truth merge).
        public async Task playWsaInBox(string name, int x, int y, int w, int h, int frames, int delayTicks)
        {
            if (!res.exists(name)) return;
            var wsa = openWsa(name);
            screen.copyRegion(x, y, x, y, w, h, 0, 2);
            for (int i = 0; i < frames; i += 1)
            {
                double delayTimer = getMillis() + delayTicks * tickLength;
                screen.copyRegion(x, y, 0, 0, w, h, 2, 2);
                wsa.displayFrame(i, 2, 0, 0, 0x4000, null, null);
                screen.copyRegion(0, 0, x, y, w, h, 2, 0);
                await delayUntil(delayTimer);
            }
        }

        // ---- temp data for level changes (block state persistence) ----
        public void generateTempData()
        {
            int l = currentLevel - 1;
            lvlTempData = lvlTempData ?? new LvlTempData[LVL_TEMP_SLOTS];
            lvlTempData[l] = new LvlTempData
            {
                walls = levelBlockProperties.Select(b => (byte[])b.walls.Clone()).ToArray(),
                flags = levelBlockProperties.Select(b => b.flags).ToArray(),
                monsters = monsters.Select(m => Spells_cloneMonster(m)).ToArray(),
                flyingObjects = flyingObjects.Select(f => Spells_cloneFlyingObject(f)).ToArray(),
                monsterDifficulty = monsterDifficulty,
            };
            hasTempDataFlags |= 1 << l;
        }

        public void restoreBlockTempData(int index)
        {
            var t = lvlTempData != null && index - 1 >= 0 && index - 1 < lvlTempData.Length ? lvlTempData[index - 1] : null;
            if (t == null) return;
            for (int i = 0; i < 1024; i += 1)
            {
                var l = levelBlockProperties[i];
                // ScummVM memsets the whole block table here: stale object lists would otherwise loop.
                l.assignedObjects = 0;
                l.drawObjects = 0;
                // 5, not 0: this field caches which facing the block's draw order was built for, and the
                // drawing pass rebuilds that order only when it does not match. Clearing it to 0 claims the
                // (now empty) order is already correct for facing north, so a monster restored into the block
                // is never added to it - it blocks the party's way while nothing is drawn. 5 is the engine's
                // own "no order yet" value, which forces the rebuild.
                l.direction = 5;
                Js.Set(l.walls, t.walls[i]);
                l.flags = t.flags[i];
            }
            for (int i = 0; i < 30; i += 1)
            {
                var m = Spells_cloneMonster(t.monsters[i]);
                m.nextAssignedObject = 0;
                m.nextDrawObject = 0;
                if (m.properties != null) m.properties = monsterProperties[m.type];
                monsters[i] = m;
                if (m.block != 0 && m.mode != 14 && m.hitPoints > 0) assignObjectToBlock(levelBlockProperties[m.block], m.id | 0x8000);
            }
            flyingObjects = t.flyingObjects.Select(f => Spells_cloneFlyingObject(f)).ToArray();
        }
    }
}
