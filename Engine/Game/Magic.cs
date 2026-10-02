// src/game/magic.mjs: attack spells and magical effects (LoLEngine::processMagic* in lol.cpp).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        static readonly int[][] MIST_ANIM = { new[] { 0, 7, 7, 13, 155 }, new[] { 0, 16, 16, 17, 155 }, new[] { 0, 24, 24, 24, 174 }, new[] { 0, 19, 19, 19, 174 }, new[] { 0, 16, 16, 17, 175 } };

        // const int16 = (v) => (v << 16) >> 16;  -> (short)v

        /// <summary>processMagicFireball's per-fireball state</summary>
        sealed class FireballState
        {
            public bool active;
            public int destX, destY, tblIndex, progress, step;
            public bool finalize;
            public int finProgress;
        }

        public int lightningCurSfx, lightningDiv, lightningFirstSfx, lightningSfxFrame;
        public int swarmSpellStatus;

        // Screen_v2::timedPaletteFadeStep: interpolates palette 1 between pal1 and pal2; returns true while still fading.
        public bool timedPaletteFadeStep(byte[] pal1, byte[] pal2, double elapsed, double total)
        {
            var p1 = screen.getPalette(1);
            var @out = new byte[768];
            bool res = false;
            for (int i = 0; i < 768; i += 1)
            {
                if (elapsed < total)
                {
                    int d = (pal2[i] & 0x3f) - (pal1[i] & 0x3f);
                    if (d != 0) res = true;
                    int val = ((int)(Math.Truncate((d << 8) / total) * elapsed)) >> 8;
                    @out[i] = (byte)(((pal1[i] & 0x3f) + (sbyte)val) & 0xff);
                }
                else
                {
                    @out[i] = p1[i] = (byte)(pal2[i] & 0x3f);
                    res = false;
                }
            }
            screen.setScreenPalette(@out);
            return res;
        }

        // Screen::checkedPageUpdate: copies the pixels that differ between two pages onto page 0.
        public void checkedPageUpdate(int srcPage, int dstPage)
        {
            var src = screen.page(srcPage);
            var dst = screen.page(dstPage);
            var @out = screen.page(0);
            for (int i = 0; i < src.Length; i += 1) if (src[i] != dst[i]) { dst[i] = src[i]; @out[i] = src[i]; }
            screen.dirty = true;
        }

        public byte[] loadPaletteFile(string name)
        {
            var pal = new byte[768];
            if (res.exists(name)) Js.Set(pal, Js.Slice(res.get(name), 0, 768));
            return pal;
        }

        public async Task<int> castIce(ActiveSpell a) { await processMagicIce(a.charNum, a.level); return 1; }
        public async Task<int> castFireball(ActiveSpell a) { await processMagicFireball(a.charNum, a.level); return 1; }
        public async Task<int> castHandOfFate(ActiveSpell a) { await processMagicHandOfFate(a.level); return 1; }
        public async Task<int> castMistOfDoom(ActiveSpell a) { await processMagicMistOfDoom(a.charNum, a.level); return 1; }
        public async Task<int> castLightning(ActiveSpell a) { await processMagicLightning(a.charNum, a.level); return 1; }
        public async Task<int> castFog() { await processMagicFog(); return 1; }
        public async Task<int> castSwarm(ActiveSpell a) { await processMagicSwarm(a.charNum, 10); return 1; }
        public async Task<int> castVaelansCube() { return await processMagicVaelansCube(); }
        public async Task<int> castGuardian(ActiveSpell a) { return await processMagicGuardian(a.charNum); }

        public async Task<int> processMagicIce(int charNum, int spellLevel)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            disableSysTimer(2);
            gui_drawScene(0);
            screen.copyPage(0, 12);
            if (currentLevel == 11 && (flagsTable[52] & 0x04) == 0)
            {
                var sc = screen.getPalette(0);
                var dc = screen.getPalette(2);
                for (int i = 1; i < 768; i += 1) { byte t = sc[i]; sc[i] = dc[i]; dc[i] = t; }
                flagsTable[52] |= 0x04;
                int[] freezeTimes = { 20, 28, 40, 60 };
                setCharacterUpdateEvent(charNum, 8, freezeTimes[spellLevel], 1);
            }
            var s = (byte[])screen.getPalette(1).Clone();
            var swampCol = loadPaletteFile("SWAMPICE.COL");
            var tpal = (byte[])s.Clone();
            Js.Set(swampCol, Js.Slice(s, 128 * 3), 128 * 3);
            for (int i = 1; i < 128; i += 1)
            {
                tpal[i * 3] = 0;
                int v = (s[i * 3] + s[i * 3 + 1] + s[i * 3 + 2]) / 3;
                tpal[i * 3 + 1] = (byte)v;
                tpal[i * 3 + 2] = (byte)Math.Min(v << 1, 0x3f);
            }
            generateBrightnessPalette(tpal, tpal, brightness, lampEffect);
            generateBrightnessPalette(swampCol, swampCol, brightness, lampEffect);
            swampCol[0] = swampCol[1] = swampCol[2] = tpal[0] = tpal[1] = tpal[2] = 0;
            generateBrightnessPalette(screen.getPalette(0), s, brightness, lampEffect);
            int sX = 112;
            int sY = 0;
            WsaPlayer mov = null;
            if (spellLevel == 0) sX = 0;
            if (spellLevel == 1 || spellLevel == 2) mov = openWsa("SNOW.WSA", 1);
            if (spellLevel == 3) { mov = openWsa("ICE.WSA", 1); sX = 136; sY = 12; }
            snd_playSoundEffect(71, -1);
            await playSpellAnimation(null, 0, 0, 2, 0, 0, null, false, s, tpal, 40);
            timedPaletteFadeStep(s, tpal, getMillis(), tickLength);
            if (mov != null)
            {
                bool r = true;
                if (spellLevel > 2)
                {
                    levelBlockProperties[calcNewBlockPosition(currentBlock, currentDirection)].flags |= 0x10;
                    snd_playSoundEffect(165, -1);
                    r = false;
                }
                await playSpellAnimation(mov, 0, mov.numFrames, 2, sX, sY, null, r);
            }
            int[] snowDamage = { 10, 20, 30, 55 };
            int[] iceDamageMax = { 1, 2, 15, 20, 35 };
            int[] iceDamageMin = { 10, 10, 3, 4, 4 };
            int[] iceDamageAdd = { 5, 10, 30, 10, 10 };
            bool breakWall = false;
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            if (spellLevel < 3) inflictMagicalDamageForBlock(ahead, charNum, snowDamage[spellLevel], 3);
            else
            {
                int o = levelBlockProperties[ahead].assignedObjects;
                while ((o & 0x8000) != 0)
                {
                    int might = rollDice(iceDamageMin[spellLevel], iceDamageMax[spellLevel]) + iceDamageAdd[spellLevel];
                    int dmg = calcInflictableDamagePerItem(charNum, 0, might, 3, 2);
                    var m = monsters[o & 0x7fff];
                    if (m.hitPoints <= dmg)
                    {
                        increaseExperience(charNum, 2, m.hitPoints);
                        // Shattering is still a kill: the page counts kills, drops, errands and the pit's cull
                        // objective from this event, and it has to be emitted while the monster is still on its block.
                        ui?.Invoke("kill", new object[] { new Dictionary<string, object> { ["monster"] = m.id, ["attacker"] = charNum } });
                        o = m.nextAssignedObject;
                        if ((m.flags & 0x20) != 0)
                        {
                            m.mode = 0;
                            monsterDropItems(m);
                            if (currentLevel != 29) setMonsterMode(m, 14);
                            await runLevelScriptCustom(0x404, -1, o, o, 0, 0);
                            checkSceneUpdateNeed(m.block);
                            if (m.mode != 14) placeMonster(m, 0, 0);
                        }
                        else killMonster(m);
                    }
                    else
                    {
                        breakWall = true;
                        inflictDamage(o, dmg, charNum, 2, 3);
                        m.damageReceived = 0;
                        o = m.nextAssignedObject;
                    }
                    if ((m.flags & 0x20) != 0) break;
                }
            }
            updateDrawPage2();
            gui_drawScene(0);
            enableSysTimer(2);
            if (currentLevel != 11) generateBrightnessPalette(screen.getPalette(0), swampCol, brightness, lampEffect);
            await playSpellAnimation(null, 0, 0, 2, 0, 0, null, false, tpal, swampCol, 40);
            timedPaletteFadeStep(tpal, swampCol, getMillis(), tickLength);
            if (breakWall) await breakIceWall(tpal, swampCol);
            screen.curPage = cp;
            return 1;
        }

        public async Task<int> processMagicFireball(int charNum, int spellLevel)
        {
            var screen = this.screen;
            int fbCnt = new[] { 4, 5, 6, 5 }[spellLevel];
            int d = spellLevel == 3 ? 0 : 1;
            int drawPage1 = 2;
            int drawPage2 = 4;
            int bl = currentBlock;
            int fireballItem = makeItem(9, 0, 0);
            int i = 0;
            for (; i < 3; i += 1)
            {
                await runLevelScriptCustom(bl, 0x200, -1, fireballItem, 0, 0);
                int o = levelBlockProperties[bl].assignedObjects;
                if ((o & 0x8000) != 0 || (wllWallFlags[levelBlockProperties[bl].walls[currentDirection ^ 2]] & 7) != 0)
                {
                    while ((o & 0x8000) != 0)
                    {
                        int[] fireballDamage = { 20, 40, 80, 100 };
                        int dmg = calcInflictableDamagePerItem(charNum, o, fireballDamage[spellLevel], 4, 1);
                        var m = monsters[o & 0x7fff];
                        o = m.nextAssignedObject;
                        envSfxUseQueue = true;
                        inflictDamage(m.id | 0x8000, dmg, charNum, 2, 4);
                        envSfxUseQueue = false;
                    }
                    break;
                }
                bl = calcNewBlockPosition(bl, currentDirection);
            }
            d = Math.Min(d + i, 3);
            deleteItem(fireballItem);
            snd_playSoundEffect(69, -1);
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            int fireBallWH = (d << 4) * -1;
            int numFireballs = fbCnt > 3 ? fbCnt - 3 : 1;
            var states = new List<FireballState>();
            for (int k = 0; k < numFireballs; k += 1) states.Add(new FireballState { active = true, destX = 200, destY = 60, tblIndex = ((k * 50) % 255) + 200, progress = 1000, step = 10, finalize = false, finProgress = 0 });
            screen.copyPage(12, drawPage1);
            var coords = @static.FireballCoords;
            Shape fbShape(int idx) => fireballShapes[idx];
            for (i = 0; i < numFireballs;)
            {
                screen.curPage = drawPage1;
                double ctime = getMillis();
                for (int ii = 0; ii < Math.Min(fbCnt, 3); ii += 1)
                {
                    var fb = ii < states.Count ? states[ii] : null;
                    if (fb == null || !fb.active) continue;
                    int[] finShpIndex1 = { 5, 6, 7, 7, 6, 5 };
                    int[] finShpIndex2 = { -1, 1, 2, 3, 4, -1 };
                    var shp = fb.finalize ? fbShape(finShpIndex1[fb.finProgress]) : fbShape(0);
                    (int, int, int, int) place(Shape shape)
                    {
                        int pfX = (((fb.progress * (short)coords[fb.tblIndex & 0xff]) >> 16) + fb.destX) - ((fb.progress / 8 + shape.width + fireBallWH) >> 1);
                        int pfY = (((fb.progress * (short)coords[(fb.tblIndex + 64) & 0xff]) >> 16) + fb.destY) - ((fb.progress / 8 + shape.height + fireBallWH) >> 1);
                        int psW = ((fb.progress / 8 + shape.width + fireBallWH) << 8) / shape.width;
                        int psH = ((fb.progress / 8 + shape.height + fireBallWH) << 8) / shape.height;
                        return (pfX, pfY, psW, psH);
                    }
                    var (fX, fY, sW, sH) = place(shp);
                    if (fb.finalize)
                    {
                        screen.drawShape(screen.curPage, shp, fX, fY, 0, 0x1004, new DrawShapeOpts { transparency1 = transparencyTable1, transparency2 = transparencyTable2, scaleW = sW, scaleH = sH });
                        if (finShpIndex2[fb.finProgress] != -1)
                        {
                            shp = fbShape(finShpIndex2[fb.finProgress]);
                            (fX, fY, sW, sH) = place(shp);
                            screen.drawShape(screen.curPage, shp, fX, fY, 0, 4, new DrawShapeOpts { scaleW = sW, scaleH = sH });
                        }
                    }
                    else screen.drawShape(screen.curPage, shp, fX, fY, 0, 0x1004, new DrawShapeOpts { transparency1 = transparencyTable1, transparency2 = transparencyTable2, scaleW = sW, scaleH = sH });
                    if (fb.finalize)
                    {
                        if (++fb.finProgress >= 6) { fb.active = false; i += 1; }
                    }
                    else
                    {
                        fb.step = fb.step < 40 ? fb.step + 2 : 40;
                        if (fb.progress < fb.step)
                        {
                            if (ii < 1) { fb.progress = fb.step = fb.finProgress = 0; fb.finalize = true; }
                            else { fb.active = false; i += 1; }
                            int[] fireballSfx = { 98, 167, 167, 168 };
                            snd_playSoundEffect(fireballSfx[d], -1);
                        }
                        else fb.progress -= fb.step;
                    }
                }
                double del = tickLength - (getMillis() - ctime);
                if (del > 0) await delay(del);
                checkedPageUpdate(drawPage1, drawPage2);
                present();
                (drawPage1, drawPage2) = (drawPage2, drawPage1);
                screen.copyPage(12, drawPage1);
            }
            screen.curPage = cp;
            screen.copyPage(12, 0);
            updateDrawPage2();
            snd_playQueuedEffects();
            await runLevelScriptCustom(bl, 0x20, charNum, 3, 0, 0);
            return 1;
        }

        public async Task<int> processMagicHandOfFate(int spellLevel)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            var mov = openWsa("HAND.WSA", 1);
            int[] frames = { 17, 26, 11, 16, 27, 35, 27, 35, 0, 75 };
            snd_playSoundEffect(173, -1);
            await playSpellAnimation(mov, 0, 10, 3, 112, 0, null, false);
            snd_playSoundEffect(151, -1);
            await playSpellAnimation(mov, frames[spellLevel * 2], frames[spellLevel * 2 + 1], 3, 112, 0, null, false);
            snd_playSoundEffect(18, -1);
            await playSpellAnimation(mov, 10, 0, 3, 112, 0, null, false);
            screen.curPage = cp;
            screen.copyPage(12, 2);
            gui_drawScene(2);
            if (spellLevel < 2)
            {
                int b1 = calcNewBlockPosition(currentBlock, currentDirection);
                int b2 = calcNewBlockPosition(b1, currentDirection);
                if (!testWallFlag(b2, 0, 4) && (levelBlockProperties[b2].assignedObjects & 0x8000) == 0)
                {
                    checkSceneUpdateNeed(b1);
                    int dir = currentDirection << 1;
                    int o = levelBlockProperties[b1].assignedObjects;
                    while ((o & 0x8000) != 0)
                    {
                        int o2 = o;
                        var m = monsters[o & 0x7fff];
                        o = findObject(o).nextAssignedObject;
                        var (nX, nY) = getNextStepCoords(m.x, m.y, dir);
                        for (int k = 0; k < 7; k += 1) (nX, nY) = getNextStepCoords(nX, nY, dir);
                        placeMonster(m, nX, nY);
                        await runLevelScriptCustom(b2, 0x800, -1, o2, 0, 0);
                    }
                }
            }
            else
            {
                int b1 = calcNewBlockPosition(currentBlock, currentDirection);
                checkSceneUpdateNeed(b1);
                int[] damage = { 75, 125, 175 };
                int o = levelBlockProperties[b1].assignedObjects;
                while ((o & 0x8000) != 0)
                {
                    int t = o;
                    o = findObject(o).nextAssignedObject;
                    int dmg = calcInflictableDamagePerItem(-1, t, damage[spellLevel - 2], 0x80, 1);
                    inflictDamage(t, dmg, 0xffff, 3, 0x80);
                }
            }
            if (currentLevel == 29) screen.copyPage(12, 2);
            screen.copyPage(2, 0);
            gui_drawScene(2);
            updateDrawPage2();
            return 1;
        }

        public async Task<int> processMagicMistOfDoom(int charNum, int spellLevel)
        {
            var screen = this.screen;
            int[] mistDamage = { 30, 70, 110, 200 };
            envSfxUseQueue = true;
            inflictMagicalDamageForBlock(calcNewBlockPosition(currentBlock, currentDirection), charNum, mistDamage[spellLevel], 0x80);
            envSfxUseQueue = false;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 2);
            gui_drawScene(2);
            screen.copyPage(2, 12);
            snd_playSoundEffect(155, -1);
            var mov = openWsa($"MISTS{spellLevel + 1}.WSA", 1);
            var md = MIST_ANIM[spellLevel];
            snd_playSoundEffect(md[4], -1);
            await playSpellAnimation(mov, md[0], md[1], 7, 112, 0, null, false);
            await playSpellAnimation(mov, md[2], md[3], 14, 112, 0, null, false);
            screen.curPage = cp;
            screen.copyPage(12, 0);
            updateDrawPage2();
            snd_playQueuedEffects();
            return 1;
        }

        public async Task<int> processMagicLightning(int charNum, int spellLevel)
        {
            var screen = this.screen;
            screen.copyPage(0, 2);
            gui_drawScene(2);
            screen.copyPage(2, 12);
            var L = @static.LightningDefs;
            lightningCurSfx = L[(spellLevel << 2) + 2] | (L[(spellLevel << 2) + 3] << 8);
            lightningDiv = L[(spellLevel << 2) + 1];
            lightningFirstSfx = 0;
            lightningSfxFrame = 0;
            var mov = openWsa($"LITNING{spellLevel + 1}.WSA", 1);
            for (int i = 0; i < 4; i += 1) await playSpellAnimation(mov, 0, L[spellLevel << 2], 3, 93, 0, (m, x, y) => callbackProcessMagicLightning(), false);
            screen.setScreenPalette(screen.getPalette(1));
            screen.copyPage(12, 2);
            screen.copyPage(12, 0);
            updateDrawPage2();
            int[] lightningDamage = { 18, 35, 50, 72 };
            inflictMagicalDamageForBlock(calcNewBlockPosition(currentBlock, currentDirection), charNum, lightningDamage[spellLevel], 5);
            sceneUpdateRequired = true;
            gui_drawScene(0);
            return 1;
        }

        public async Task<int> processMagicFog()
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            var mov = openWsa("FOG.WSA", 0);
            int numFrames = mov.numFrames;
            snd_playSoundEffect(145, -1);
            for (int curFrame = 0; curFrame < numFrames; curFrame += 1)
            {
                double delayTimer = getMillis() + 3 * tickLength;
                screen.copyPage(12, 2);
                mov.displayFrame(curFrame % numFrames, 2, 112, 0, 0x5000, transparencyTable1, transparencyTable2);
                screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                await delayUntil(delayTimer);
            }
            screen.copyPage(12, 2);
            screen.curPage = cp;
            updateDrawPage2();
            int o = levelBlockProperties[calcNewBlockPosition(currentBlock, currentDirection)].assignedObjects;
            while ((o & 0x8000) != 0)
            {
                inflictMagicalDamage(o, -1, 15, 6, 0);
                o = monsters[o & 0x7fff].nextAssignedObject;
            }
            gui_drawScene(0);
            return 1;
        }

        public async Task<int> processMagicSwarm(int charNum, int damage)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            snd_playSoundEffect(74, -1);
            var destIds = new List<int>();
            var destModes = new List<int>();
            var destTicks = new List<int>();
            int o = levelBlockProperties[calcNewBlockPosition(currentBlock, currentDirection)].assignedObjects;
            while ((o & 0x8000) != 0)
            {
                o &= 0x7fff;
                if (monsters[o].mode != 13)
                {
                    destIds.Add(o);
                    if ((monsters[o].flags & 0x2000) == 0)
                    {
                        envSfxUseQueue = true;
                        inflictMagicalDamage(o | 0x8000, charNum, damage, 0, 0);
                        envSfxUseQueue = false;
                        monsters[o].flags &= 0xffef;
                    }
                }
                o = monsters[o].nextAssignedObject;
            }
            foreach (int id in destIds)
            {
                destModes.Add(monsters[id].mode);
                destTicks.Add(monsters[id].fightCurTick);
                monsters[id].mode = 8;
                monsters[id].fightCurTick = 0;
            }
            gui_drawScene(screen.curPage);
            screen.copyRegion(112, 0, 112, 0, 176, 120, screen.curPage, 7);
            for (int k = 0; k < destIds.Count; k += 1) { int id = destIds[k]; monsters[id].mode = destModes[k]; monsters[id].fightCurTick = destTicks[k]; }
            var mov = openWsa("SWARM.WSA", 0);
            swarmSpellStatus = 0;
            await playSpellAnimation(mov, 0, 37, 2, 0, 0, null, false);
            await playSpellAnimation(mov, 38, 41, 8, 0, 0, (m, x, y) => callbackProcessMagicSwarm(), false);
            screen.copyPage(12, 0);
            updateDrawPage2();
            snd_playQueuedEffects();
            screen.curPage = cp;
            return 1;
        }

        public async Task<int> processMagicVaelansCube()
        {
            var screen = this.screen;
            var sp1 = screen.getPalette(1);
            var tmpPal1 = (byte[])sp1.Clone();
            var tmpPal2 = (byte[])sp1.Clone();
            for (int i = 0; i < 128; i += 1)
            {
                tmpPal2[i * 3] = (byte)Math.Min(sp1[i * 3] + 16, 60);
                tmpPal2[i * 3 + 1] = sp1[i * 3 + 1];
                tmpPal2[i * 3 + 2] = (byte)Math.Min(sp1[i * 3 + 2] + 19, 60);
            }
            snd_playSoundEffect(146, -1);
            double ctime = getMillis();
            double endTime = ctime + 70 * tickLength;
            while (getMillis() < endTime)
            {
                timedPaletteFadeStep(tmpPal1, tmpPal2, getMillis() - ctime, 70 * tickLength);
                await delay(tickLength);
            }
            int bl = calcNewBlockPosition(currentBlock, currentDirection);
            int s = levelBlockProperties[bl].walls[currentDirection ^ 2];
            int flg = wllWallFlags[s];
            int res = s == 47 && (currentLevel == 17 || currentLevel == 24) ? 1 : 0;
            if ((wllVmpMap[s] == 1 || wllVmpMap[s] == 2) && (flg & 1) == 0 && currentLevel != 22)
            {
                Js.Fill(levelBlockProperties[bl].walls, (byte)0);
                gui_drawScene(0);
                res = 1;
            }
            int o = levelBlockProperties[bl].assignedObjects;
            while ((o & 0x8000) != 0)
            {
                var m = monsters[o & 0x7fff];
                if (m.properties != null && (m.properties.flags & 0x1000) != 0)
                {
                    inflictDamage(o, 100, 0xffff, 0, 0x80);
                    res = 1;
                }
                o = m.nextAssignedObject;
            }
            ctime = getMillis();
            endTime = ctime + 70 * tickLength;
            while (getMillis() < endTime)
            {
                timedPaletteFadeStep(tmpPal2, tmpPal1, getMillis() - ctime, 70 * tickLength);
                await delay(tickLength);
            }
            return res;
        }

        public async Task<int> processMagicGuardian(int charNum)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 2);
            screen.copyPage(2, 12);
            var mov = openWsa("GUARDIAN.WSA", 0);
            snd_playSoundEffect(156, -1);
            await playSpellAnimation(mov, 0, 37, 2, 112, 0, null, false);
            screen.copyPage(2, 12);
            int bl = calcNewBlockPosition(currentBlock, currentDirection);
            int res = (levelBlockProperties[bl].assignedObjects & 0x8000) != 0 ? 1 : 0;
            inflictMagicalDamageForBlock(bl, charNum, 200, 0x80);
            screen.copyPage(12, 2);
            updateDrawPage2();
            gui_drawScene(2);
            screen.copyPage(2, 12);
            snd_playSoundEffect(176, -1);
            await playSpellAnimation(mov, 38, 48, 8, 112, 0, null, false);
            screen.curPage = cp;
            gui_drawPlayField();
            updateDrawPage2();
            return res;
        }

        public void callbackProcessMagicSwarm()
        {
            if (swarmSpellStatus != 0) screen.copyRegion(112, 0, 112, 0, 176, 120, 6, screen.curPage);
            swarmSpellStatus ^= 1;
        }

        public void callbackProcessMagicLightning()
        {
            if (lightningDiv == 2) queueAsync(() => shakeScene(1, 2, 3, 0));
            var p1 = screen.getPalette(1);
            if (lightningSfxFrame % lightningDiv != 0) screen.setScreenPalette(p1);
            else
            {
                var tpal = (byte[])p1.Clone();
                for (int i = 6; i < 384; i += 1)
                {
                    int v = (tpal[i] * 120) / 64;
                    tpal[i] = (byte)(v < 64 ? v : 63);
                }
                screen.setScreenPalette(tpal);
            }
            if (lightningDiv == 2)
            {
                if (lightningFirstSfx == 0)
                {
                    snd_playSoundEffect(lightningCurSfx, -1);
                    lightningFirstSfx = 1;
                }
            }
            else if ((lightningSfxFrame & 7) == 0) snd_playSoundEffect(lightningCurSfx, -1);
            lightningSfxFrame += 1;
        }

        public async Task launchMagicViper()
        {
            partyAwake = true;
            int d = 0;
            for (int b = currentBlock; d < 3; d += 1)
            {
                if ((levelBlockProperties[b].assignedObjects & 0x8000) != 0) break;
                b = calcNewBlockPosition(b, currentDirection);
                if ((wllWallFlags[levelBlockProperties[b].walls[currentDirection ^ 2]] & 7) != 0) break;
            }
            var screen = this.screen;
            screen.copyPage(0, 12);
            snd_playSoundEffect(148, -1);
            var mov = openWsa("VIPER.WSA", 1);
            int numFrames = mov.numFrames;
            int[] viperAnimData = { 15, 25, 20, 10, 25, 20, 5, 25, 20, 0, 25, 20 };
            var v = Js.Slice(viperAnimData, d * 3, d * 3 + 3);
            int frm = v[0];
            for (bool running = true; running;)
            {
                double etime = getMillis() + 5 * tickLength;
                screen.copyPage(12, 2);
                if (frm == v[2]) snd_playSoundEffect(172, -1);
                mov.displayFrame(frm++ % numFrames, 2, 112, 0, 0x5000, transparencyTable1, transparencyTable2);
                screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                await delayUntil(etime);
                if (frm > v[1]) running = false;
            }
            screen.copyPage(12, 0);
            screen.copyPage(12, 2);
            int t = rollDice(1, 4);
            for (int i = 0; i < 4; i += 1)
            {
                if ((characters[i].flags & 1) == 0) { t %= 4; continue; }
                inflictDamage(t, currentLevel + 10, 0x8000, 2, 0x86);
            }
        }

        // Screen_LoL::generateFadeTable: numTabs palettes interpolating src1 (default: screen palette) -> src2.
        public List<byte[]> generateFadeTable(byte[] src1, byte[] src2, int numTabs)
        {
            var p2 = src1 ?? screen.screenPalette;
            var @out = new List<byte[]>();
            var delta = new sbyte[768];
            for (int i = 0; i < 768; i += 1) delta[i] = (sbyte)(src2[i] - p2[i]);
            @out.Add((byte[])p2.Clone());
            int t = 0;
            int d = 256 / numTabs;
            for (int i = 1; i < numTabs - 1; i += 1)
            {
                t += d;
                var pal = new byte[768];
                for (int ii = 0; ii < 768; ii += 1) pal[ii] = (byte)((((delta[ii] * t) >> 8) + (sbyte)p2[ii]) & 0xff);
                @out.Add(pal);
            }
            @out.Add((byte[])src2.Clone());
            return @out;
        }

        // olol_restoreMagicShroud: the DARKLITE cutscene that lifts the shroud.
        public async Task restoreMagicShroud()
        {
            if (!res.exists("DARKLITE.WSA")) return;
            var mov = openWsa("DARKLITE.WSA", 2);
            var pal1 = loadPaletteFile("LITEPAL1.COL");
            var tab1 = generateFadeTable(null, pal1, 21);
            var pal2 = loadPaletteFile("LITEPAL2.COL");
            var pal3 = loadPaletteFile("LITEPAL3.COL");
            var tab2 = generateFadeTable(pal2, pal3, 4);
            for (int i = 0; i < 21; i += 1)
            {
                double etime = getMillis() + 20 * tickLength;
                mov.displayFrame(i, 0, 0, 0, 0, null, null);
                screen.setScreenPalette(tab1[i]);
                if (new[] { 2, 5, 8, 11, 13, 15, 17, 19 }.Contains(i)) snd_playSoundEffect(95, -1);
                await delayUntil(etime);
            }
            snd_playSoundEffect(91, -1);
            await screen.fadePalette(pal2, 300);
            int k = 0;
            for (int i = 22; i < 38; i += 1)
            {
                double etime = getMillis() + 12 * tickLength;
                mov.displayFrame(i, 0, 0, 0, 0, null, null);
                if (i == 22 || i == 24 || i == 28 || i == 32)
                {
                    snd_playSoundEffect(131, -1);
                    screen.setScreenPalette(tab2[k++]);
                }
                await delayUntil(etime);
            }
        }

        public async Task breakIceWall(byte[] pal1, byte[] pal2)
        {
            var screen = this.screen;
            int bl = calcNewBlockPosition(currentBlock, currentDirection);
            levelBlockProperties[bl].flags &= 0xef;
            screen.copyPage(0, 2);
            gui_drawScene(2);
            screen.copyPage(2, 10);
            var mov = openWsa("SHATTER.WSA", 1);
            snd_playSoundEffect(166, -1);
            await playSpellAnimation(mov, 0, mov.numFrames, 1, 58, 0, null, true, pal1, pal2, 20);
            screen.copyPage(10, 0);
            updateDrawPage2();
            gui_drawScene(0);
        }
    }
}
