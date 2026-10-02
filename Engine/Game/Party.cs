// src/game/party.mjs: characters, combat and timers (lol.cpp / timer_lol.cpp).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        // ---- party.mjs fields ----
        public Shape[][] characterFaceShapes;
        public short[] activeCharsXpos;
        public int[] charStatsTemp;
        public int portraitSpeechAnimMode;
        public int updatePortraitSpeechAnimDuration;
        public int resetPortraitAfterSpeechAnim;
        public double updatePortraitNext;
        public double palUpdateTimer;
        public int monsterStepCounter;
        public int monsterStepMode;
        public bool sysTimerPaused;
        /// <summary>host setting (main.mjs: engine.weaponWear = settings.wear)</summary>
        public bool weaponWear;

        /// <summary>String.prototype.replace with a string pattern: the first match only.</summary>
        static string Party_replace(string s, string pattern, string with)
        {
            int i = s.IndexOf(pattern, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + pattern.Length);
        }

        public void initParty()
        {
            characterFaceShapes = new Shape[][] { new Shape[0], new Shape[0], new Shape[0], new Shape[0] };
            activeCharsXpos = new short[3];
            charStatsTemp = new int[5];
            portraitSpeechAnimMode = 0;
            updatePortraitSpeechAnimDuration = 0;
            resetPortraitAfterSpeechAnim = 0;
            updatePortraitNext = 0;
            palUpdateTimer = 0;
            monsterStepCounter = 0;
            monsterStepMode = 0;
        }

        public void setupTimers()
        {
            addTimer(0, _ => timerProcessDoors(), 15, true);
            addTimer(0x10, id => timerProcessMonsters(id), 6, true);
            addTimer(0x11, id => timerProcessMonsters(id), 6, true);
            timerSetNextRun(0x11, getMillis() + 3 * tickLength);
            addTimer(3, _ => timerSpecialCharacterUpdate(), 15, true);
            addTimer(4, _ => timerProcessFlyingObjects(), 1, true);
            addTimer(0x50, id => timerRunSceneAnimScript(id), 0, false);
            addTimer(0x51, id => timerRunSceneAnimScript(id), 0, false);
            addTimer(0x52, id => timerRunSceneAnimScript(id), 0, false);
            addTimer(8, _ => timerRegeneratePoints(), 1200, true);
            addTimer(9, _ => timerUpdatePortraitAnimations(0), 10, true);
            addTimer(10, _ => timerUpdateLampState(), 360, true);
            addTimer(11, id => timerFadeMessageText(id), 360, false);
        }

        public void enableSysTimer(int sysTimer)
        {
            if (sysTimer != 2) return;
            foreach (int id in new[] { 0x10, 0x11, 3, 4, 8, 9, 10, 11 }) timerPauseSingle(id, false);
            sysTimerPaused = false;
        }

        // Host pause (menus open): the same timers stop, but sysTimerPaused stays clear so the host
        // interface (equipping from the character screen, map notes...) keeps working.
        public void uiPauseTimers(bool pause)
        {
            foreach (int id in new[] { 0x10, 0x11, 3, 4, 8, 9, 10, 11 }) timerPauseSingle(id, pause);
        }

        public void disableSysTimer(int sysTimer)
        {
            if (sysTimer != 2) return;
            foreach (int id in new[] { 0x10, 0x11, 3, 4, 8, 9, 10, 11 }) timerPauseSingle(id, true);
            sysTimerPaused = true;
        }

        public void timerProcessMonsters(int timerNum)
        {
            for (int i = timerNum & 0x0f; i < 30; i += 2)
            {
                int n = i;   // JS `let`: one binding per iteration; the monster is looked up when the task runs
                queueAsync(() => updateMonster(monsters[n]));
            }
        }

        public void timerSpecialCharacterUpdate()
        {
            int eventsLeft = 0;
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0) continue;
                for (int ii = 0; ii < 5; ii += 1)
                {
                    if (c.characterUpdateEvents[ii] == 0) continue;
                    if (--c.characterUpdateDelay[ii] > 0)
                    {
                        if (c.characterUpdateDelay[ii] > eventsLeft) eventsLeft = c.characterUpdateDelay[ii];
                        continue;
                    }
                    switch (c.characterUpdateEvents[ii] - 1)
                    {
                        case 0:
                            if (c.weaponHit != 0)
                            {
                                c.weaponHit = 0;
                                c.characterUpdateDelay[ii] = (byte)calcMonsterSkillLevel(i, 6);
                                if (c.characterUpdateDelay[ii] > eventsLeft) eventsLeft = c.characterUpdateDelay[ii];
                            }
                            else c.flags &= 0xfffb;
                            gui_drawCharPortraitWithStats(i);
                            break;
                        case 1:
                            c.damageSuffered = 0;
                            gui_drawCharPortraitWithStats(i);
                            break;
                        case 2:
                            c.flags &= 0xffbf;
                            gui_drawCharPortraitWithStats(i);
                            break;
                        case 3:
                            eventsLeft = rollDice(1, 2);
                            if (inflictDamage(i, eventsLeft, 0x8000, 0, 0x80) != 0)
                            {
                                txt.printMessage(2, Party_replace(getLangString(0x4022), "%s", c.name));
                                c.characterUpdateDelay[ii] = 10;
                                if (c.characterUpdateDelay[ii] > eventsLeft) eventsLeft = c.characterUpdateDelay[ii];
                            }
                            break;
                        case 4:
                            c.flags &= 0xfeff;
                            txt.printMessage(0, Party_replace(getLangString(0x4027), "%s", c.name));
                            gui_drawCharPortraitWithStats(i);
                            break;
                        case 5:
                            setTemporaryFaceFrame(i, 0, 0, 1);
                            break;
                        case 6:
                            c.flags &= 0xefff;
                            gui_drawCharPortraitWithStats(i);
                            break;
                        case 7:
                            restoreSwampPalette();
                            break;
                        case 8: case 9: case 10:
                        { // a fighter/rogue/mage potion running out
                            int skill = c.characterUpdateEvents[ii] - 9;
                            c.skillModifiers[skill] = (sbyte)(c.skillModifiers[skill] - c.potionSkillBonus[skill]);
                            c.potionSkillBonus[skill] = 0;
                            txt.printMessage(0, $"{c.name} feels the potion fade.");
                            gui_drawCharPortraitWithStats(i);
                            break;
                        }
                        default:
                            break;
                    }
                    if (c.characterUpdateDelay[ii] <= 0) c.characterUpdateEvents[ii] = 0;
                }
            }
            if (eventsLeft != 0) timerEnable(3);
            else timerDisable(3);
        }

        public void timerProcessFlyingObjects()
        {
            for (int i = 0; i < 8; i += 1)
            {
                if (flyingObjects[i].enable == 0) continue;
                int n = i;
                queueAsync(() => updateFlyingObject(flyingObjects[n]));
            }
        }

        public void timerRunSceneAnimScript(int timerNum)
        {
            queueAsync(() => runLevelScript(0x401 + (timerNum & 0x0f), -1));
        }

        public void timerRegeneratePoints()
        {
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0) continue;
                int hInc = (c.flags & 8) != 0 ? 0 : itemEquipped(i, 228) ? 4 : 1;
                int mInc = drainMagic != 0 ? (c.magicPointsMax >> 5) * -1 : (c.flags & 8) != 0 ? 0 : itemEquipped(i, 227) ? c.magicPointsMax / 10 : 1;
                c.magicPointsCur = Math.Max(0, Math.Min(c.magicPointsMax, c.magicPointsCur + mInc));
                if ((c.flags & 0x80) == 0) increaseCharacterHitpoints(i, hInc, false);
                gui_drawCharPortraitWithStats(i);
            }
        }

        public void timerUpdatePortraitAnimations(int skipUpdate)
        {
            if (skipUpdate != 1) skipUpdate = 0;
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0 || (c.flags & 8) != 0 || c.curFaceFrame > 1) continue;
                if (c.curFaceFrame != 1)
                {
                    if (--c.nextAnimUpdateCountdown <= 0 && skipUpdate == 0)
                    {
                        c.curFaceFrame = 1;
                        gui_drawCharPortraitWithStats(i);
                        timerSetCountdown(9, 10);
                    }
                }
                else
                {
                    c.curFaceFrame = 0;
                    gui_drawCharPortraitWithStats(i);
                    c.nextAnimUpdateCountdown = rollDice(1, 12) + 6;
                }
            }
        }

        public void timerUpdateLampState()
        {
            if ((flagsTable[31] & 0x08) != 0 && (flagsTable[31] & 0x04) != 0 && brightness != 0 && lampOilStatus != 0 && !lampSwitchedOff) lampOilStatus -= 1;
        }

        public void timerFadeMessageText(int timerNum)
        {
            timerDisable(timerNum);
            initTextFading(0, 0);
        }

        // ---- characters ----
        public bool addCharacter(int id)
        {
            var S = @static;
            int[][] cdf = { S.CharDefsMan, S.CharDefsMan, S.CharDefsMan, S.CharDefsWoman, S.CharDefsMan, S.CharDefsMan, S.CharDefsWoman, S.CharDefsKieran, S.CharDefsAkshel };
            int numChars = countActiveCharacters();
            if (numChars >= 3 && companionsBench && benchForNewcomer(id) >= 0) numChars = countActiveCharacters();   // Unity port: one waits in the camp
            if (numChars >= 3) return false;
            int defIndex = Array.FindIndex(S.CharacterDefs, d => d.id == id);
            if (defIndex < 0) return false;
            var def = S.CharacterDefs[defIndex];
            var c = makeEmptyCharacter();
            // Object.assign(c, JSON.parse(JSON.stringify(def))), then the arrays copied (Array.from).
            c.flags = def.flags;
            c.name = def.name;
            c.raceClassSex = def.raceClassSex;
            c.id = def.id;
            c.curFaceFrame = def.curFaceFrame;
            c.tempFaceFrame = def.tempFaceFrame;
            c.screamSfx = def.screamSfx;
            c.itemProtection = def.itemProtection;
            c.hitPointsCur = def.hitPointsCur;
            c.hitPointsMax = def.hitPointsMax;
            c.magicPointsCur = def.magicPointsCur;
            c.magicPointsMax = def.magicPointsMax;
            c.field_41 = def.field_41;
            c.damageSuffered = def.damageSuffered;
            c.weaponHit = def.weaponHit;
            c.totalMightModifier = def.totalMightModifier;
            c.totalProtectionModifier = def.totalProtectionModifier;
            c.might = def.might;
            c.protection = def.protection;
            c.nextAnimUpdateCountdown = def.nextAnimUpdateCountdown;
            // JS: plain arrays here; the C# Character keeps the typed arrays of makeEmptyCharacter.
            c.itemsMight = Array.ConvertAll(def.itemsMight, v => (ushort)v);
            c.protectionAgainstItems = Array.ConvertAll(def.protectionAgainstItems, v => (ushort)v);
            c.items = Array.ConvertAll(def.items, v => (ushort)v);
            c.skillLevels = (int[])def.skillLevels.Clone();
            c.skillModifiers = (int[])def.skillModifiers.Clone();
            c.experiencePts = (int[])def.experiencePts.Clone();
            c.characterUpdateEvents = (int[])def.characterUpdateEvents.Clone();
            c.characterUpdateDelay = (int[])def.characterUpdateDelay.Clone();
            c.defaultModifiers = cdf[defIndex];
            characters[numChars] = c;
            loadCharFaceShapes(numChars, id);
            c.nextAnimUpdateCountdown = rollDice(1, 12) + 6;
            for (int i = 0; i < 11; i += 1)
            {
                if (c.items[i] != 0)
                {
                    c.items[i] = (ushort)makeItem(c.items[i], 0, 0);
                    int n = i;
                    queueAsync(() => runItemScript(numChars, c.items[n], 0x80, 0, 0));
                }
            }
            calcCharPortraitXpos();
            if (numChars > 0) setTemporaryFaceFrame(numChars, 2, 6, 0);
            return true;
        }

        public void loadCharFaceShapes(int charNum, int id)
        {
            if (id < 0) id = -id;
            characterFaceShapes[charNum] = loadShapeFile($"FACE{id.ToString().PadLeft(2, '0')}.SHP");
        }

        public void setTemporaryFaceFrame(int charNum, int frame, int updateDelay, int redraw)
        {
            characters[charNum].tempFaceFrame = frame;
            if (frame != 0 || updateDelay != 0) setCharacterUpdateEvent(charNum, 6, updateDelay, 1);
            if (redraw != 0) gui_drawCharPortraitWithStats(charNum);
        }

        public void setTemporaryFaceFrameForAllCharacters(int frame, int updateDelay, int redraw)
        {
            for (int i = 0; i < 4; i += 1) setTemporaryFaceFrame(i, frame, updateDelay, 0);
            if (redraw != 0) gui_drawAllCharPortraitsWithStats();
        }

        public void setCharacterUpdateEvent(int charNum, int updateType, int updateDelay, int overwrite)
        {
            var l = characters[charNum];
            for (int i = 0; i < 5; i += 1)
            {
                if (l.characterUpdateEvents[i] != 0 && (overwrite == 0 || l.characterUpdateEvents[i] != updateType)) continue;
                l.characterUpdateEvents[i] = (byte)updateType;
                l.characterUpdateDelay[i] = (byte)updateDelay;   // Types.cs byte[]; the JS array is a plain one (3600 fits there)
                timerSetNextRun(3, getMillis());
                timerEnable(3);
                break;
            }
        }

        public void setCharFaceFrame(int charNum, int frameNum)
        {
            characters[charNum].curFaceFrame = frameNum;
        }

        public void faceFrameRefresh(int charNum)
        {
            var c = characters[charNum];
            if (c.curFaceFrame == 1) setTemporaryFaceFrame(charNum, 0, 0, 0);
            else if (c.curFaceFrame == 6)
            {
                if (c.tempFaceFrame != 5) setTemporaryFaceFrame(charNum, 0, 0, 0);
                else c.curFaceFrame = 5;
            }
            else c.curFaceFrame = 0;
        }

        public void updatePortraitSpeechAnim()
        {
            int x = 0;
            int y = 0;
            bool redraw = false;
            if (portraitSpeechAnimMode == 0)
            {
                x = activeCharsXpos[updateCharNum];
                y = 144;
                redraw = true;
            }
            else if (portraitSpeechAnimMode == 1)
            {
                if (textEnabled()) { x = 90; y = 130; }
                else { x = activeCharsXpos[updateCharNum]; y = 144; }
            }
            else if (portraitSpeechAnimMode == 2)
            {
                if (textEnabled()) { x = 16; y = 134; }
                else { x = activeCharsXpos[updateCharNum] + 10; y = 145; }
            }
            // Which face frame a talking portrait shows: presentation, and it used to come out of the
            // gameplay dice, which made a conversation shift every roll that followed it.
            int f = presentationRoll(1, 6) - 1;
            if (f == characters[updateCharNum].curFaceFrame) f += 1;
            if (f > 5) f -= 5;
            f += 7;
            if (speechEnabled())
            {
                if (snd_updateCharacterSpeech() == 2)
                {
                    if (resetPortraitAfterSpeechAnim == 2) resetPortraitAfterSpeechAnim = 1;
                    else updatePortraitSpeechAnimDuration = 2;
                }
                else updatePortraitSpeechAnimDuration = 1;
            }
            else if (resetPortraitAfterSpeechAnim == 2) resetPortraitAfterSpeechAnim = 1;
            updatePortraitSpeechAnimDuration -= 1;
            if (updatePortraitSpeechAnimDuration != 0)
            {
                setCharFaceFrame(updateCharNum, f);
                if (redraw) gui_drawCharPortraitWithStats(updateCharNum);
                else gui_drawCharFaceShape(updateCharNum, x, y, 0);
                updatePortraitNext = getMillis() + 10 * tickLength;
            }
            else if (resetPortraitAfterSpeechAnim != 0)
            {
                faceFrameRefresh(updateCharNum);
                if (redraw)
                {
                    gui_drawCharPortraitWithStats(updateCharNum);
                    initTextFading(0, 0);
                }
                else gui_drawCharFaceShape(updateCharNum, x, y, 0);
                updateCharNum = -1;
            }
        }

        public void stopPortraitSpeechAnim()
        {
            if (updateCharNum == -1) return;
            updatePortraitSpeechAnimDuration = 1;
            resetPortraitAfterSpeechAnim = 2;
            updatePortraitSpeechAnim();
            updatePortraitSpeechAnimDuration = 1;
            updateCharNum = -1;
            if (portraitSpeechAnimMode == 0) initTextFading(0, 0);
        }

        public void initTextFading(int textType, int clearField)
        {
            if (textColorFlag == textType || textType == 0)
            {
                fadeText = true;
                palUpdateTimer = getMillis();
            }
            if (clearField == 0) return;
            stopPortraitSpeechAnim();
            if (needSceneRestore != 0) screen.setScreenDim(txt.clearDim(3));
            fadeText = false;
            timerDisable(11);
        }

        public void fadeTextStep()
        {
            if (!fadeText) return;
            if (screen.fadeColor(192, 252, (int)((getMillis() - palUpdateTimer) / tickLength), 60)) return;
            if (needSceneRestore != 0) return;
            screen.setScreenDim(txt.clearDim(3));
            timerDisable(11);
            fadeText = false;
        }

        public void recalcCharacterStats(int charNum)
        {
            for (int i = 0; i < 5; i += 1) charStatsTemp[i] = calculateCharacterStats(charNum, i);
        }

        public int calculateCharacterStats(int charNum, int index)
        {
            var ch = characters[charNum];
            if (index == 0)
            {
                int c = 0;
                for (int i = 0; i < 8; i += 1) c += ch.itemsMight[i];
                if (c != 0) c += ch.might;
                else c = ch.defaultModifiers[8];
                c = (c * ch.defaultModifiers[1]) >> 8;
                c = (c * ch.totalMightModifier) >> 8;
                return c;
            }
            if (index == 1) return calculateProtection(charNum);
            if (index > 4) return -1;
            index -= 2;
            return ch.skillLevels[index] + ch.skillModifiers[index];
        }

        public int calculateProtection(int index)
        {
            int c = 0;
            if ((index & 0x8000) != 0)
            {
                var m = monsters[index & 0x7fff];
                c = (m.properties.itemProtection * m.properties.fightingStats[2]) >> 8;
            }
            else
            {
                var ch = characters[index];
                c = ch.itemProtection + ch.protection;
                c = (c * ch.defaultModifiers[2]) >> 8;
                c = (c * ch.totalProtectionModifier) >> 8;
            }
            return c;
        }

        static readonly int[][] Party_barData = { new[] { 0x27, 0x9a, 0x98, 0x01, 0x4254 }, new[] { 0x21, 0xa2, 0xa0, 0x00, 0x4253 } };

        public void setCharacterMagicOrHitPoints(int charNum, int type, int points, int mode)
        {
            var barData = Party_barData;
            if (charNum > 2) return;
            var c = characters[charNum];
            if ((c.flags & 1) == 0) return;
            int pointsMax = type != 0 ? c.magicPointsMax : c.hitPointsMax;
            int pointsCur = type != 0 ? c.magicPointsCur : c.hitPointsCur;
            int newVal = mode == 2 ? pointsMax + points : mode != 0 ? pointsCur + points : points;
            newVal = Math.Max(0, Math.Min(pointsMax, newVal));
            if (type != 0) c.magicPointsCur = newVal;
            else
            {
                c.hitPointsCur = newVal;
                if (c.hitPointsCur < 1) c.flags |= 8;
            }
            if ((updateFlags & 2) != 0) return;
            // The animated bar (with delays) is drawn synchronously at its final value here.
            var cf = screen.setFont("6");
            int cp = screen.curPage;
            screen.curPage = 0;
            int s = pointsMax != 0 ? 8192 / pointsMax : 0;   // JS: 8192 / 0 is Infinity, and both products below then come out 0
            pointsMax = (s * pointsMax) >> 8;
            newVal = (s * newVal) >> 8;
            var bd = barData[type];
            gui_drawLiveMagicBar(bd[0] + activeCharsXpos[charNum], 175, newVal, 0, pointsMax, 5, 32, bd[1], 1, bd[3]);
            screen.printText(getLangString(bd[4]), bd[0] + activeCharsXpos[charNum], 144, bd[2], 0);
            screen.setFont(cf);
            screen.curPage = cp;
        }

        public void increaseExperience(int charNum, int skill, int points)
        {
            if ((charNum & 0x8000) != 0) return;
            var c = characters[charNum];
            if ((c.flags & 8) != 0) return;
            c.experiencePts[skill] += points;
            while (c.experiencePts[skill] >= @static.ExpRequirements[c.skillLevels[skill]])
            {
                c.skillLevels[skill] += 1;
                c.flags |= 0x200 << skill;
                int inc = 0;
                if (skill == 0)
                {
                    txt.printMessage(0x8003, Party_replace(getLangString(0x4023), "%s", c.name));
                    inc = rollDice(4, 6);
                    c.hitPointsCur += inc;
                    c.hitPointsMax += inc;
                }
                else if (skill == 1)
                {
                    txt.printMessage(0x8003, Party_replace(getLangString(0x4025), "%s", c.name));
                    inc = rollDice(2, 6);
                    c.hitPointsCur += inc;
                    c.hitPointsMax += inc;
                }
                else if (skill == 2)
                {
                    txt.printMessage(0x8003, Party_replace(getLangString(0x4024), "%s", c.name));
                    inc = (c.defaultModifiers[6] * (rollDice(1, 8) + 17)) >> 8;
                    c.magicPointsCur += inc;
                    c.magicPointsMax += inc;
                    inc = rollDice(1, 6);
                    c.hitPointsCur += inc;
                    c.hitPointsMax += inc;
                }
                snd_playSoundEffect(118, -1);
                gui_drawCharPortraitWithStats(charNum);
            }
        }

        public void increaseCharacterHitpoints(int charNum, int points, bool ignoreDeath)
        {
            var c = characters[charNum];
            if (c.hitPointsCur <= 0 && !ignoreDeath) return;
            if (points <= 1) points = 1;
            c.hitPointsCur = Math.Max(1, Math.Min(c.hitPointsMax, c.hitPointsCur + points));
            c.flags &= 0xfff7;
        }

        /// <summary>A monster's fightingStats (Uint16Array) or a character's defaultModifiers, read only: widened to int[].</summary>
        public int[] getCharacterOrMonsterStats(int id)
        {
            return (id & 0x8000) != 0 ? Array.ConvertAll(monsters[id & 0x7fff].properties.fightingStats, v => (int)v) : characters[id].defaultModifiers;
        }

        public ushort[] getCharacterOrMonsterItemsMight(int id)
        {
            return (id & 0x8000) != 0 ? monsters[id & 0x7fff].properties.itemsMight : characters[id].itemsMight;
        }

        public ushort[] getCharacterOrMonsterProtectionAgainstItems(int id)
        {
            return (id & 0x8000) != 0 ? monsters[id & 0x7fff].properties.protectionAgainstItems : characters[id].protectionAgainstItems;
        }

        // ---- combat ----
        public int battleHitSkillTest(int attacker, int target, int skill)
        {
            if (target == -1 || target == 0xffff) return 0;
            if (attacker == -1) return 1;
            if ((target & 0x8000) != 0 && monsters[target & 0x7fff].mode >= 13) return 0;
            int hitChanceModifier;
            int evadeChanceModifier;
            int sk;
            if ((attacker & 0x8000) != 0)
            {
                hitChanceModifier = monsters[target & 0x7fff].properties.fightingStats[0];
                sk = 100 - monsters[target & 0x7fff].properties.skillLevel;
            }
            else
            {
                hitChanceModifier = characters[attacker].defaultModifiers[0];
                int m = characters[attacker].skillModifiers[skill];
                if (skill == 1) m *= 3;
                sk = 100 - (characters[attacker].skillLevels[skill] + m);
            }
            if ((target & 0x8000) != 0)
            {
                evadeChanceModifier = monsters[target & 0x7fff].properties.fightingStats[3];
                evadeChanceModifier = (evadeChanceModifier * @static.MonsterModifiers4[monsterDifficulty]) >> 8;
                monsters[target & 0x7fff].flags |= 0x10;
            }
            else evadeChanceModifier = characters[target].defaultModifiers[3];
            int r = rollDice(1, 100);
            if (r >= sk) return 2;
            double v = Math.Truncate((double)(evadeChanceModifier << 8) / hitChanceModifier);   // float division, as in JS (a zero divisor gives Infinity/NaN)
            if (r < v) { uiEmit("miss", new { attacker, target }); return 0; }
            return 1;
        }

        public int calcInflictableDamage(int attacker, int target, int hitType)
        {
            var s = getCharacterOrMonsterItemsMight(attacker);
            int res = 0;
            for (int i = 0; i < 8; i += 1) res += calcInflictableDamagePerItem(attacker, target, s[i], i, hitType);
            // Port addition (Settings -> weapon wear): a blade dulls as it is used, until it is repaired.
            if (weaponWear && attacker >= 0 && (attacker & 0x8000) == 0) res = uiWearWeapon(attacker, res);
            return res;
        }

        public int calcInflictableDamagePerItem(int attacker, int target, int itemMight, int index, int hitType)
        {
            int dmg = attacker == -1 ? 0x100 : getCharacterOrMonsterStats(attacker)[1];
            var st = getCharacterOrMonsterProtectionAgainstItems(target);
            dmg = (dmg * itemMight) >> 8;
            if (dmg == 0) return 0;
            if ((attacker & 0x8000) == 0)
            {
                dmg = (dmg * characters[attacker].totalMightModifier) >> 8;
                if (dmg == 0) return 0;
            }
            int d = (short)((index & 0x80) != 0 ? st[7] : st[index]);
            int r = (dmg * Math.Abs(d)) >> 8;
            dmg = d < 0 ? -r : r;
            if (hitType == 2 || dmg == 0) return dmg == 1 ? 2 : dmg;
            dmg = (dmg * (256 - Math.Min((calculateProtection(target) << 7) / dmg, 217))) >> 8;
            return dmg < 2 ? 2 : dmg;
        }

        public int inflictDamage(int target, int damage, int attacker, int skill, int flags)
        {
            if ((target & 0x8000) != 0)
            {
                var m = monsters[target & 0x7fff];
                if (m.mode >= 13) return 0;
                if (damage > 0)
                {
                    m.hitPoints -= damage;
                    m.damageReceived = 0x8000 | damage;
                    m.flags |= 0x10;
                    m.hitOffsX = rollDice(1, 24) - 12;
                    m.hitOffsY = rollDice(1, 24) - 12;
                    m.hitPoints = Math.Max(0, Math.Min(m.properties.hitPoints, m.hitPoints));
                    uiEmit("damage", new { monster = m.id, damage, attacker });
                    if ((attacker & 0x8000) == 0) applyMonsterDefenseSkill(m, attacker, flags, skill, damage);
                    snd_queueEnvironmentalSoundEffect(m.properties.sounds[2], m.block);
                    checkSceneUpdateNeed(m.block);
                    if (m.hitPoints <= 0)
                    {
                        m.hitPoints = 0;
                        if ((attacker & 0x8000) == 0) increaseExperience(attacker, skill, m.properties.hitPoints);
                        uiEmit("kill", new { monster = m.id, attacker });
                        setMonsterMode(m, 13);
                    }
                }
                else
                {
                    m.hitPoints -= damage;
                    m.hitPoints = Math.Max(1, Math.Min(m.properties.hitPoints, m.hitPoints));
                }
            }
            else
            {
                if (target > 3)
                {
                    int t = target;
                    int i = Array.FindIndex(characters, ch => ch.id == t);
                    if (i < 0) return 0;
                    target = i;
                }
                var c = characters[target];
                if ((c.flags & 1) == 0 || (c.flags & 8) != 0) return 0;
                if ((c.flags & 0x1000) == 0) snd_playSoundEffect(c.screamSfx, -1);
                setTemporaryFaceFrame(target, 6, 4, 0);
                if (flags == 4 && itemEquipped(target, 229)) damage >>= 2;
                uiEmit("damage", new { character = target, damage, attacker });
                setCharacterMagicOrHitPoints(target, 0, -damage, 1);
                if (c.hitPointsCur <= 0) characterHitpointsZero(target, flags);
                else
                {
                    c.damageSuffered = damage;
                    setCharacterUpdateEvent(target, 2, 4, 1);
                }
                gui_drawCharPortraitWithStats(target);
            }
            if ((attacker & 0x8000) == 0 && attacker != -1)
            {
                if (skill == 0) characters[attacker].weaponHit = damage;
                increaseExperience(attacker, skill, damage);
            }
            return damage;
        }

        public void characterHitpointsZero(int charNum, int flags)
        {
            var c = characters[charNum];
            c.hitPointsCur = 0;
            c.flags |= 8;
            removeCharacterEffects(c, 1, 5);
            partyDamageFlags = flags;
        }

        public void removeCharacterEffects(Character c, int first, int last)
        {
            for (int i = first; i <= last; i += 1)
            {
                switch (i - 1)
                {
                    case 0: c.flags &= 0xfffb; c.weaponHit = 0; break;
                    case 1: c.damageSuffered = 0; break;
                    case 2: c.flags &= 0xffbf; break;
                    case 3: c.flags &= 0xff7f; break;
                    case 4: c.flags &= 0xfeff; break;
                    case 6: c.flags &= 0xefff; break;
                    default: break;
                }
                for (int ii = 0; ii < 5; ii += 1)
                {
                    if (i != c.characterUpdateEvents[ii]) continue;
                    c.characterUpdateEvents[ii] = 0;
                    c.characterUpdateDelay[ii] = 0;
                }
            }
            timerEnable(3);
        }

        public async Task checkForPartyDeath()
        {
            for (int i = 0; i < 4; i += 1)
            {
                if ((characters[i].flags & 1) == 0 || characters[i].hitPointsCur <= 0) continue;
                return;
            }
            if (weaponsDisabled) await clickedExitCharInventory();
            gui_drawAllCharPortraitsWithStats();
            if ((partyDamageFlags & 0x40) != 0)
            {
                await screen.fadeToBlack(40);
                for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0) increaseCharacterHitpoints(i, 1, true);
                gui_drawAllCharPortraitsWithStats();
                await screen.fadeToPalette1(40);
            }
            else
            {
                await screen.fadeClearSceneWindow(10);
                await restoreAfterSpecialScene(0, 1, 1, 0);
                gui_drawAllCharPortraitsWithStats();
                await gui.runMenu(gui.deathMenu);
                setMouseCursorToItemInHand();
                updateFlags &= 0xfffb;
                resetLampStatus();
                gui_enableDefaultPlayfieldButtons();
                enableSysTimer(2);
                updateDrawPage2();
            }
        }

        public void applyMonsterAttackSkill(Monster monster, int target, int damage)
        {
            if (rollDice(1, 100) > monster.properties.attackSkillChance) return;
            int t = 0;
            switch (monster.properties.attackSkillType - 1)
            {
                case 0:
                    t = removeCharacterItem(target, 0x7ff);
                    if (t != 0)
                    {
                        giveItemToMonster(monster, t);
                        if (characterSays(0x4019, characters[target].id, true) != 0) txt.printMessage(6, getLangString(0x4019));
                    }
                    break;
                case 1: paralyzePoisonCharacter(target, 0x80, 0x88, 100, 1); break;
                case 2:
                    t = removeCharacterItem(target, 0x20);
                    if (t != 0)
                    {
                        deleteItem(t);
                        if (characterSays(0x401b, characters[target].id, true) != 0) txt.printMessage(6, getLangString(0x401b));
                    }
                    break;
                case 3:
                    t = removeCharacterItem(target, 0x0f);
                    if (t != 0)
                    {
                        if (characterSays(0x401e, characters[target].id, true) != 0) txt.printMessage(6, Party_replace(getLangString(0x401e), "%s", characters[target].name));
                        int item = t;
                        queueAsync(() => setItemPosition(item, monster.x, monster.y, 0, 1));
                    }
                    break;
                case 5:
                    if (characters[target].magicPointsCur <= 0) return;
                    monster.hitPoints += characters[target].magicPointsCur;
                    characters[target].magicPointsCur = 0;
                    gui_drawCharPortraitWithStats(target);
                    if (characterSays(0x4020, characters[target].id, true) != 0) txt.printMessage(6, Party_replace(getLangString(0x4020), "%s", characters[target].name));
                    break;
                case 7: stunCharacter(target); break;
                case 8:
                    monster.hitPoints = Math.Min(monster.properties.hitPoints, monster.hitPoints + damage);
                    break;
                case 9: paralyzePoisonAllCharacters(0x40, 0x48, 100); break;
                default: break;
            }
        }

        public void applyMonsterDefenseSkill(Monster monster, int attacker, int flags, int skill, int damage)
        {
            if (rollDice(1, 100) > monster.properties.defenseSkillChance) return;
            switch (monster.properties.defenseSkillType - 1)
            {
                case 0:
                case 1:
                    if ((flags & 0x3f) == 2 || skill != 0) return;
                    for (int i = 0; i < 3; i += 1)
                    {
                        int itm = characters[attacker].items[i];
                        if (itm == 0) continue;
                        if ((itemProperties[itemsInPlay[itm].itemPropertyIndex].protection & 0x3f) != flags) continue;
                        removeCharacterItem(attacker, 0x7fff);
                        if (monster.properties.defenseSkillType == 1)
                        {
                            giveItemToMonster(monster, itm);
                            if (characterSays(0x401c, characters[attacker].id, true) != 0) txt.printMessage(6, getLangString(0x401c));
                        }
                        else
                        {
                            deleteItem(itm);
                            if (characterSays(0x401d, characters[attacker].id, true) != 0) txt.printMessage(6, getLangString(0x401d));
                        }
                    }
                    break;
                case 2:
                    if ((flags & 0x80) == 0) return;
                    monster.flags |= 8;
                    monster.direction = calcMonsterDirection(monster.x, monster.y, partyPosX, partyPosY) ^ 4;
                    setMonsterMode(monster, 9);
                    monster.fightCurTick = 30;
                    break;
                case 3:
                    if (flags != 3) return;
                    monster.hitPoints = Math.Min(monster.properties.hitPoints, monster.hitPoints + damage);
                    break;
                case 4:
                    if ((flags & 0x80) == 0) return;
                    monster.hitPoints = Math.Min(monster.properties.hitPoints, monster.hitPoints + damage);
                    break;
                case 5:
                    if ((flags & 0x84) == 0x84) monster.numDistAttacks += 1;
                    break;
                default: break;
            }
        }

        public int removeCharacterItem(int charNum, int itemFlags)
        {
            for (int i = 0; i < 11; i += 1)
            {
                int s = characters[charNum].items[i];
                if (((1 << i) & itemFlags) == 0 || s == 0) continue;
                characters[charNum].items[i] = 0;
                queueAsync(() => runItemScript(charNum, s, 0x100, 0, 0));
                return s;
            }
            return 0;
        }

        public int paralyzePoisonCharacter(int charNum, int typeFlag, int immunityFlags, int hitChance, int redraw)
        {
            var c = characters[charNum];
            if ((c.flags & 1) == 0 || (c.flags & immunityFlags) != 0) return 0;
            if (rollDice(1, 100) > hitChance) return 0;
            int r = 0;
            if (typeFlag == 0x40)
            {
                c.flags |= 0x40;
                setCharacterUpdateEvent(charNum, 3, 3600, 1);
                r = 1;
            }
            else if (typeFlag == 0x80 && !itemEquipped(charNum, 225))
            {
                c.flags |= 0x80;
                setCharacterUpdateEvent(charNum, 4, 10, 1);
                if (characterSays(0x4021, c.id, true) != 0) txt.printMessage(6, Party_replace(getLangString(0x4021), "%s", c.name));
                r = 1;
            }
            else if (typeFlag == 0x1000)
            {
                c.flags |= 0x1000;
                setCharacterUpdateEvent(charNum, 7, 120, 1);
                r = 1;
            }
            if (r != 0 && redraw != 0) gui_drawCharPortraitWithStats(charNum);
            return r;
        }

        public void paralyzePoisonAllCharacters(int typeFlag, int immunityFlags, int hitChance)
        {
            bool r = false;
            for (int i = 0; i < 4; i += 1) if (paralyzePoisonCharacter(i, typeFlag, immunityFlags, hitChance, 0) != 0) r = true;
            if (r) gui_drawAllCharPortraitsWithStats();
        }

        public void stunCharacter(int charNum)
        {
            var c = characters[charNum];
            if ((c.flags & 1) == 0 || (c.flags & 0x108) != 0) return;
            c.flags |= 0x100;
            setCharacterUpdateEvent(charNum, 5, 20, 1);
            gui_drawCharPortraitWithStats(charNum);
            txt.printMessage(6, Party_replace(getLangString(0x4026), "%s", c.name));
        }

        // The swamp thaws: the frozen palette (kept in palette 2 by processMagicIce) swaps back and the
        // screen fades from ice to normal, like LoLEngine::restoreSwampPalette.
        public void restoreSwampPalette()
        {
            flagsTable[52] &= 0xfb;
            if (currentLevel != 11) return;
            var s = screen.getPalette(2);
            var d = screen.getPalette(0);
            var d2 = screen.getPalette(1);
            for (int i = 1; i < 768; i += 1) { byte t = s[i]; s[i] = d[i]; d[i] = t; }
            generateBrightnessPalette(d, d2, brightness, lampEffect);
            screen.loadSpecialColors(s);
            screen.loadSpecialColors(d2);
            queueAsync(() => playSpellAnimation(null, 0, 0, 2, 0, 0, null, false, (byte[])s.Clone(), d2, 40));
        }

        public int getNearestMonsterFromCharacter(int charNum)
        {
            return getNearestMonsterFromCharacterForBlock(calcNewBlockPosition(currentBlock, currentDirection), charNum);
        }

        public int getNearestMonsterFromCharacterForBlock(int block, int charNum)
        {
            int id = 0xffff;
            int minDist = 0x7fff;
            if (block == 0xffff) return id;
            var (cX, cY) = calcCoordinatesForSingleCharacter(charNum);
            int o = levelBlockProperties[block].assignedObjects;
            while ((o & 0x8000) != 0)
            {
                var m = monsters[o & 0x7fff];
                if (m.mode >= 13) { o = m.nextAssignedObject; continue; }
                int d = Math.Abs(cX - m.x) + Math.Abs(cY - m.y);
                if (d < minDist) { minDist = d; id = o; }
                o = m.nextAssignedObject;
            }
            return id;
        }

        public int getNearestMonsterFromPos(int x, int y)
        {
            int id = 0xffff;
            int minDist = 0x7fff;
            for (int i = 0; i < 30; i += 1)
            {
                if (monsters[i].mode > 13) continue;
                int d = Math.Abs(x - monsters[i].x) + Math.Abs(y - monsters[i].y);
                if (d < minDist) { minDist = d; id = 0x8000 | i; }
            }
            return id;
        }

        public int getNearestPartyMemberFromPos(int x, int y)
        {
            int id = 0xffff;
            int minDist = 0x7fff;
            for (int i = 0; i < 4; i += 1)
            {
                if ((characters[i].flags & 1) == 0 || characters[i].hitPointsCur <= 0) continue;
                var (cx, cy) = calcCoordinatesForSingleCharacter(i);
                int d = Math.Abs(x - cx) + Math.Abs(y - cy);
                if (d < minDist) { minDist = d; id = i; }
            }
            return id;
        }

        public void giveItemToMonster(Monster monster, int item)
        {
            if (monster.assignedItems == 0) monster.assignedItems = item;
            else
            {
                int c = monster.assignedItems;
                while (itemsInPlay[c].nextAssignedObject != 0) c = itemsInPlay[c].nextAssignedObject;
                itemsInPlay[c].nextAssignedObject = item;
            }
            itemsInPlay[item].nextAssignedObject = 0;
        }
    }
}
