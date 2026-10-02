// src/game/gui.mjs: Playfield GUI: drawing and button handling (gui_lol.cpp, gui_rpg.cpp).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>gui.mjs initGui / gui_enableSequenceButtons: sceneWindowButton { x, y, w, h }</summary>
    public sealed class SceneWindowButton
    {
        public int x, y, w, h;
    }

    public sealed partial class LandsOfLore
    {
        static readonly int[] DROP_ITEM_DIR_INDEX = { 0, 1, 2, 3, 1, 3, 0, 2, 3, 2, 1, 0, 2, 0, 3, 1 };
        static readonly int[] INVENTORY_X = { 0x6a, 0x7f, 0x94, 0xa9, 0xbe, 0xd3, 0xe8, 0xfd, 0x112 };

        // DOS scan codes used by the button table, keyed by KeyboardEvent.key.
        // (index initializers: a later key overrides an earlier one, as in the JS object literal)
        public static readonly Dictionary<string, int> KEY_CODES = new Dictionary<string, int>
        {
            [" "] = 61, ["Enter"] = 43, ["ArrowUp"] = 96, ["ArrowRight"] = 102, ["ArrowDown"] = 98, ["ArrowLeft"] = 92, ["Home"] = 91, ["PageUp"] = 101, ["End"] = 93, ["PageDown"] = 103,
            ["F1"] = 112, ["F2"] = 113, ["F3"] = 114, ["F4"] = 115, ["F5"] = 116, ["F6"] = 117, ["Tab"] = 16, ["a"] = 31, ["b"] = 50, ["c"] = 48, ["d"] = 33, ["e"] = 19, ["f"] = 34, ["i"] = 24, ["k"] = 38, ["m"] = 52, ["n"] = 51,
            ["o"] = 25, ["p"] = 26, ["r"] = 20, ["y"] = 22, ["z"] = 46,
            // WASD/QE aliases for the movement keys (original: arrows slide, Home/PageUp turn).
            ["w"] = 96, ["s"] = 98, ["a"] = 92, ["d"] = 102, ["q"] = 91, ["e"] = 101, ["1"] = 2, ["2"] = 3, ["3"] = 4, ["4"] = 5, ["5"] = 6, ["6"] = 7, ["7"] = 8, ["/"] = 55, ["Escape"] = 110, ["-"] = 12, ["+"] = 13, [","] = 53, ["."] = 54,
        };

        // ---- gui.mjs fields ----
        public List<Button> activeButtons;
        public bool buttonListChanged;
        public int activeMagicMenu;
        public int subMenuIndex;
        public SceneWindowButton sceneWindowButton;
        public int lastButtonShape;
        public double buttonPressTimer;
        public int charInventoryUnk;
        public byte[] pageBuffer1;
        public byte[] pageBuffer2;
        public int floatingCursorControl;
        public Func<Button, Task<int>>[] buttonCallbacks;
        public bool awaitingSpellTarget;
        public ActiveSpell spellTargetSpell;
        public bool lampSwitchedOff;
        public bool restInterrupted;

        public void initGui()
        {
            activeButtons = new List<Button>();
            buttonListChanged = false;
            activeMagicMenu = -1;
            subMenuIndex = 0;
            sceneWindowButton = new SceneWindowButton { x = 0, y = 0, w = 0, h = 0 };
            lastButtonShape = 0;
            buttonPressTimer = 0;
            charInventoryUnk = 0;
            pageBuffer1 = null;
            pageBuffer2 = null;
            floatingCursorControl = 0;
            buttonCallbacks = makeButtonCallbacks();
        }

        public Func<Button, Task<int>>[] makeButtonCallbacks()
        {
            var cb = new Func<Button, Task<int>>[95];
            void set(int i, Func<Button, Task<int>> f) { cb[i] = f; }
            set(0, (b) => clickedUpArrow(b)); set(1, (b) => clickedDownArrow(b)); set(2, cb[1]); set(3, (b) => clickedLeftArrow(b));
            set(4, (b) => clickedRightArrow(b)); set(5, (b) => clickedTurnLeftArrow(b)); set(6, (b) => clickedTurnRightArrow(b));
            set(7, (b) => clickedAttackButton(b)); for (int i = 8; i < 11; i += 1) set(i, cb[7]);
            set(11, (b) => clickedMagicButton(b)); for (int i = 12; i < 15; i += 1) set(i, cb[11]);
            set(15, (b) => clickedMagicSubmenu(b)); set(16, (b) => clickedScreen(b));
            set(17, (b) => clickedPortraitLeft(b)); for (int i = 18; i < 25; i += 1) set(i, cb[17]);
            set(25, (b) => clickedLiveMagicBarsLeft(b)); for (int i = 26; i < 29; i += 1) set(i, cb[25]);
            set(29, (b) => clickedPortraitEtcRight(b)); for (int i = 30; i < 33; i += 1) set(i, cb[29]);
            set(33, (b) => clickedCharInventorySlot(b)); for (int i = 34; i < 44; i += 1) set(i, cb[33]);
            set(44, (b) => clickedExitCharInventory()); set(45, (b) => clickedSceneDropItem(b)); for (int i = 46; i < 49; i += 1) set(i, cb[45]);
            set(49, (b) => clickedScenePickupItem(b)); set(50, (b) => clickedInventorySlot(b)); for (int i = 51; i < 60; i += 1) set(i, cb[50]);
            set(60, (b) => clickedInventoryScroll(b)); set(61, cb[60]); set(62, (b) => clickedWall()); set(63, cb[62]);
            set(64, (b) => clickedSequenceWindow()); set(65, cb[0]); set(66, cb[1]); set(67, cb[3]); set(68, cb[4]); set(69, cb[5]); set(70, cb[6]);
            set(71, (b) => clickedScroll(b)); for (int i = 72; i < 81; i += 1) set(i, cb[71]);
            set(81, (b) => clickedSpellTargetCharacter(b)); for (int i = 82; i < 85; i += 1) set(i, cb[81]);
            set(85, (b) => clickedSpellTargetScene()); set(86, (b) => clickedSceneThrowItem()); set(87, cb[86]);
            set(88, (b) => clickedOptions()); set(89, (b) => clickedRestParty()); set(90, (b) => clickedMoneyBox()); set(91, (b) => clickedCompass());
            set(92, (b) => clickedAutomap()); set(93, (b) => clickedLamp()); set(94, (b) => clickedStatusIcon());
            return cb;
        }

        // ---- drawing ----
        public void gui_drawPlayField()
        {
            screen.loadBitmap(res.get("PLAYFLD.CPS"), 2, null);
            if ((flagsTable[31] & 0x40) != 0)
            {
                screen.copyRegion(112, 32, 288, 0, 32, 32, 2, 2, true);
                compassDirection = -1;
            }
            if ((flagsTable[31] & 0x10) != 0) screen.drawShape(2, gameShapes[78], 290, 32, 0, 0);
            int cp = screen.curPage;
            screen.curPage = 2;
            if ((flagsTable[31] & 0x20) != 0) gui_drawScroll();
            else selectedSpell = 0;
            if ((flagsTable[31] & 0x08) != 0) resetLampStatus();
            updateDrawPage2();
            gui_drawScene(2);
            gui_drawAllCharPortraitsWithStats();
            gui_drawInventory();
            gui_drawMoneyBox(screen.curPage);
            screen.curPage = cp;
            screen.copyPage(2, 0);
            updateDrawPage2();
        }

        public void gui_drawInventory()
        {
            if (currentControlMode == 0 || needSceneRestore == 0) for (int i = 0; i < 9; i += 1) gui_drawInventoryItem(i);
        }

        public void gui_drawInventoryItem(int index)
        {
            int x = INVENTORY_X[index];
            int item = inventoryCurItem + index;
            if (item >= inventory.Length) item -= inventory.Length;
            int flag = (item & 1) != 0 ? 0 : 1;
            screen.drawShape(screen.curPage, gameShapes[4], x, 179, 0, flag);
            if (inventory[item] != 0) screen.drawShape(screen.curPage, getItemIconShapePtr(inventory[item]), x + 1, 180, 0, 0);
        }

        public void gui_drawScroll()
        {
            screen.copyRegion(112, 0, 12, 0, 87, 15, 2, 2, true);
            var of = screen.setFont("9");
            int h = 0;
            for (int i = 0; i < SPELL_SLOTS; i += 1) if (availableSpells[i] != -1) h += 9;
            if (h == 18) h = 27;
            if (h != 0)
            {
                screen.copyRegion(201, 1, 17, 15, 6, h, 2, 2, true);
                screen.copyRegion(208, 1, 89, 15, 6, h, 2, 2, true);
                screen.fillRect(21, 15, 89, h + 15, 206);
            }
            screen.copyRegion(112, 16, 12, h + 15, 87, 14, 2, 2, true);
            int y = 15;
            for (int i = 0; i < SPELL_SLOTS; i += 1)
            {
                if (availableSpells[i] == -1) continue;
                int col = i == selectedSpell ? 132 : 1;
                screen.fprintString(spellName(availableSpells[i]), 24, y, col, 0, 0);
                y += 9;
            }
            screen.setFont(of);
        }

        public void gui_highlightSelectedSpell(bool mode)
        {
            int y = 15;
            var of = screen.setFont("9");
            for (int i = 0; i < SPELL_SLOTS; i += 1)
            {
                if (availableSpells[i] == -1) continue;
                int col = mode && i == selectedSpell ? 132 : 1;
                screen.fprintString(spellName(availableSpells[i]), 24, y, col, 0, 0);
                y += 9;
            }
            screen.setFont(of);
        }

        public void gui_displayCharInventory(int charNum)
        {
            int[] inventoryTypes = { 0, 1, 2, 6, 3, 1, 1, 3, 5, 4 };
            int cp = screen.curPage;
            screen.curPage = 2;
            var l = characters[charNum];
            int id = Math.Abs(l.id);
            if (id != lastCharInventory)
            {
                screen.loadBitmap(res.get($"INVENT{inventoryTypes[id]}.CPS"), 2, null);
                screen.copyRegion(0, 0, 112, 0, 208, 120, 2, 6);
            }
            else screen.copyRegion(112, 0, 0, 0, 208, 120, 6, 2);
            screen.copyRegion(80, 143, 80, 143, 232, 35, 0, 2);
            gui_drawAllCharPortraitsWithStats();
            screen.fprintString(l.name, 157, 9, 254, 0, 5);
            gui_printCharInventoryStats(charNum);
            for (int i = 0; i < 11; i += 1) gui_drawCharInventoryItem(i);
            var of = screen.setFont("9");
            screen.fprintString(getLangString(0x4033), 182, 103, 172, 0, 5);
            screen.setFont(of);
            int[] statusFlags = { 0x0080, 0x0000, 0x1000, 0x0002, 0x100, 0x0001, 0x0000, 0x0000 };
            Js.Fill(charStatusFlags, (byte)0xff);
            int x = 0;
            int c = 0;
            for (int i = 0; i < 3; i += 1)
            {
                if ((l.flags & statusFlags[i << 1]) == 0) continue;
                var shp = gameShapes[statusFlags[(i << 1) + 1]];
                screen.drawShape(screen.curPage, shp, 108 + x, 98, 0, 0);
                x += shp.width + 2;
                charStatusFlags[c] = (byte)statusFlags[(i << 1) + 1];
                c += 1;
            }
            for (int i = 0; i < 3; i += 1)
            {
                int b = l.experiencePts[i] - @static.ExpRequirements[l.skillLevels[i] - 1];
                int e = @static.ExpRequirements[l.skillLevels[i]] - @static.ExpRequirements[l.skillLevels[i] - 1];
                while ((e & unchecked((int)0xffff8000)) != 0)
                {
                    e >>= 1;
                    int cc = b;
                    b >>= 1;
                    if (cc != 0 && b == 0) b = 1;
                }
                gui_drawHorizontalBarGraph(154, 64 + i * 10, 34, 5, b, e, 132, 0);
            }
            screen.drawClippedLine(14, 120, 194, 120, 1);
            screen.copyRegion(0, 0, 112, 0, 208, 121, 2, 0);
            screen.copyRegion(80, 143, 80, 143, 232, 35, 2, 0);
            screen.curPage = cp;
        }

        public void gui_printCharInventoryStats(int charNum)
        {
            for (int i = 0; i < 5; i += 1) gui_printCharacterStats(i, 1, calculateCharacterStats(charNum, i));
            charInventoryUnk |= 1 << charNum;
        }

        public void gui_printCharacterStats(int index, int redraw, int value)
        {
            int offs = screen.curPage != 0 ? 0 : 112;
            int y;
            int col;
            if (index < 2)
            {
                y = index * 10 + 22;
                col = 158;
                if (redraw != 0) screen.fprintString(getLangString(0x4014 + index), offs + 108, y, col, 0, 4);
            }
            else
            {
                int s = index - 2;
                y = s * 10 + 62;
                col = (characters[selectedCharacter].flags & (0x200 << s)) != 0 ? 254 : 180;
                if (redraw != 0) screen.fprintString(getLangString(0x4014 + index), offs + 108, y, col, 0, 4);
            }
            if (offs != 0) screen.copyRegion(294, y, 182 + offs, y, 18, 8, 6, screen.curPage, true);
            screen.fprintString(value.ToString(), 200 + offs, y, col, 0, 6);
        }

        public async Task gui_changeCharacterStats(int charNum)
        {
            var tmp = new int[5];
            var inc = new int[5];
            bool prc = false;
            for (int i = 0; i < 5; i += 1)
            {
                tmp[i] = calculateCharacterStats(charNum, i);
                int diff = tmp[i] - charStatsTemp[i];
                inc[i] = diff / 15;   // Math.trunc
                if (diff != 0)
                {
                    prc = true;
                    if (inc[i] == 0) inc[i] = diff < 0 ? -1 : 1;
                }
            }
            if (!prc) return;
            do
            {
                prc = false;
                for (int i = 0; i < 5; i += 1)
                {
                    if (tmp[i] == charStatsTemp[i]) continue;
                    charStatsTemp[i] += inc[i];
                    if ((inc[i] > 0 && tmp[i] < charStatsTemp[i]) || (inc[i] < 0 && tmp[i] > charStatsTemp[i])) charStatsTemp[i] = tmp[i];
                    gui_printCharacterStats(i, 0, charStatsTemp[i]);
                    prc = true;
                }
                await delay(tickLength, true);
            } while (prc);
        }

        public void gui_drawCharInventoryItem(int itemIndex)
        {
            int[] slotShapes = { 0x30, 0x34, 0x30, 0x34, 0x2e, 0x2f, 0x32, 0x33, 0x31, 0x35, 0x35 };
            int @base = @static.CharInvIndex[characters[selectedCharacter].raceClassSex] * 22 + itemIndex * 2;
            int x = @static.CharInvDefs[@base];
            int y = @static.CharInvDefs[@base + 1];
            if (y == 0xff) return;
            if (screen.curPage == 0) x += 112;
            int i = characters[selectedCharacter].items[itemIndex];
            int shapeNum = i != 0 ? (itemIndex < 9 ? 4 : 5) : slotShapes[itemIndex];
            screen.drawShape(screen.curPage, gameShapes[shapeNum], x, y, 0, 0);
            if (itemIndex > 8) { x -= 5; y -= 5; }
            if (i != 0) screen.drawShape(screen.curPage, getItemIconShapePtr(i), x + 1, y + 1, 0, 0);
        }

        public void gui_drawAllCharPortraitsWithStats()
        {
            int numChars = countActiveCharacters();
            for (int i = 0; i < numChars; i += 1) gui_drawCharPortraitWithStats(i);
        }

        public void gui_drawCharPortraitWithStats(int charNum)
        {
            var c = characters[charNum];
            if ((c.flags & 1) == 0 || (updateFlags & 2) != 0) return;
            var tmpFid = screen.setFont("6");
            int cp = screen.curPage;
            screen.curPage = 6;
            gui_drawBox(0, 0, 66, 34, 1, 1, -1);
            gui_drawCharFaceShape(charNum, 0, 1, screen.curPage);
            gui_drawLiveMagicBar(33, 32, c.magicPointsCur, 0, c.magicPointsMax, 5, 32, 162, 1, 0);
            gui_drawLiveMagicBar(39, 32, c.hitPointsCur, 0, c.hitPointsMax, 5, 32, 154, 1, 1);
            screen.printText(getLangString(0x4253), 33, 1, 160, 0);
            screen.printText(getLangString(0x4254), 39, 1, 152, 0);
            int spellLevels = 0;
            if (availableSpells[selectedSpell] != -1)
            {
                var sp = @static.SpellProperties[availableSpells[selectedSpell]];
                for (int i = 0; i < 4; i += 1) if (sp.mpRequired[i] <= c.magicPointsCur && sp.hpRequired[i] <= c.hitPointsCur) spellLevels += 1;
            }
            if ((c.flags & 0x10) != 0)
            {
                screen.drawShape(screen.curPage, gameShapes[73], 44, 0, 0, 0);
                if (spellLevels < 4) screen.drawGridBox(44, (spellLevels << 3) + 1, 22, 32 - (spellLevels << 3), 1);
            }
            else
            {
                int handIndex = 0;
                if (c.items[0] != 0)
                {
                    if (itemProperties[itemsInPlay[c.items[0]].itemPropertyIndex].might != -1) handIndex = itemsInPlay[c.items[0]].itemPropertyIndex;
                }
                handIndex = @static.GameShapeMap[(itemProperties[handIndex].shpIndex << 1) + 1];
                if (handIndex == @static.GameShapeMap[1])
                {
                    handIndex = c.raceClassSex - 1;
                    if (handIndex < 0) handIndex = 0;
                    handIndex += 68;
                }
                screen.drawShape(screen.curPage, gameShapes[handIndex], 44, 0, 0, 0);
                screen.drawShape(screen.curPage, gameShapes[72 + c.field_41], 44, 17, 0, 0);
                if (spellLevels == 0) screen.drawGridBox(44, 17, 22, 16, 1);
            }
            int f = c.flags & 0x314c;
            if ((f == 0 && weaponsDisabled) || (f != 0 && (f != 4 || c.weaponHit == 0 || (c.weaponHit != 0 && weaponsDisabled)))) screen.drawGridBox(44, 0, 22, 34, 1);
            if (c.weaponHit != 0)
            {
                screen.drawShape(screen.curPage, gameShapes[34], 44, 0, 0, 0);
                screen.fprintString(c.weaponHit.ToString(), 57, 7, 254, 0, 1);
            }
            if (c.damageSuffered != 0) screen.fprintString(c.damageSuffered.ToString(), 17, 28, 254, 0, 1);
            int col = charNum != selectedCharacter || countActiveCharacters() == 1 ? 1 : 212;
            screen.drawBox(0, 0, 65, 33, col);
            screen.copyRegion(0, 0, activeCharsXpos[charNum], 143, 66, 34, screen.curPage, cp, true);
            screen.curPage = cp;
            screen.setFont(tmpFid);
        }

        public void gui_drawCharFaceShape(int charNum, int x, int y, int pageNum)
        {
            var c = characters[charNum];
            if (c.curFaceFrame < 7 && c.tempFaceFrame != 0) c.curFaceFrame = c.tempFaceFrame;
            if (c.tempFaceFrame == 0 && c.curFaceFrame > 1 && c.curFaceFrame < 7) c.curFaceFrame = c.tempFaceFrame;
            int frm = (c.flags & 0x1108) != 0 && c.curFaceFrame < 7 ? 1 : c.curFaceFrame;
            if (c.hitPointsCur <= c.hitPointsMax >> 1) frm += 14;
            screen.drawShape(pageNum, characterFaceShapes[charNum][frm], x, y, 0, 0x100, new DrawShapeOpts { fadeTable = screen.paletteOverlay2, fadeLevel = (c.flags & 0x80) != 0 ? 1 : 0 });
            if ((c.flags & 0x40) != 0) screen.drawShape(pageNum, gameShapes[21], x, y, 0, 0);
        }

        public void gui_highlightPortraitFrame(int charNum)
        {
            if (charNum != selectedCharacter)
            {
                int o = selectedCharacter;
                selectedCharacter = charNum;
                gui_drawCharPortraitWithStats(o);
            }
            gui_drawCharPortraitWithStats(charNum);
        }

        public void gui_drawLiveMagicBar(int x, int y, int curPoints, int unk, int maxPoints, int w, int h, int col1, int col2, int flag)
        {
            w -= 1;
            h -= 1;
            if (maxPoints < 1) return;
            int t = curPoints < 1 ? 0 : curPoints;
            curPoints = maxPoints < t ? maxPoints : t;
            int barHeight = (curPoints * h) / maxPoints;   // Math.trunc
            if (barHeight < 1 && curPoints > 0) barHeight = 1;
            screen.drawClippedLine(x - 1, y - h, x - 1, y, 1);
            if (flag != 0)
            {
                t = maxPoints >> 1;
                if (t > curPoints) col1 = 144;
                t = maxPoints >> 2;
                if (t > curPoints) col1 = 132;
            }
            if (barHeight > 0) screen.fillRect(x, y - barHeight, x + w, y, col1);
            if (barHeight < h) screen.fillRect(x, y - h, x + w, y - barHeight, col2);
            if (unk > 0 && unk < maxPoints) screen.drawBox(x, y - barHeight, x + w, y, col1 - 2);
        }

        public void calcCharPortraitXpos()
        {
            int nc = countActiveCharacters();
            if (currentControlMode != 0 && !textEnabled())
            {
                int t = (280 - nc * 33) / (nc + 1);   // Math.trunc
                for (int i = 0; i < nc; i += 1) activeCharsXpos[i] = (short)(i * 33 + t * (i + 1) + 10);
            }
            else
            {
                int t = (235 - nc * 66) / (nc + 1);   // Math.trunc
                for (int i = 0; i < nc; i += 1) activeCharsXpos[i] = (short)(i * 66 + t * (i + 1) + 83);
            }
        }

        public void gui_drawMoneyBox(int pageNum)
        {
            int[] moneyX = { 0x128, 0x134, 0x12b, 0x131, 0x12e };
            int[] moneyY = { 0x73, 0x73, 0x74, 0x74, 0x75 };
            int backupPage = screen.curPage;
            screen.curPage = pageNum;
            screen.fillRect(292, 97, 316, 118, 252, pageNum);
            for (int i = 0; i < 5; i += 1)
            {
                if (moneyColumnHeight[i] == 0) continue;
                int h = moneyColumnHeight[i] - 1;
                int[] cols = { 0xd2, 0xd1, 0xd0, 0xd1, 0xd2 };
                for (int k = 0; k < 5; k += 1) screen.drawClippedLine(moneyX[i] + k, moneyY[i], moneyX[i] + k, moneyY[i] - h, cols[k]);
            }
            var backupFont = screen.setFont("6");
            screen.fprintString(credits.ToString(), 305, 98, 254, 0, 1);
            screen.setFont(backupFont);
            screen.curPage = backupPage;
            if (pageNum == 6) screen.copyRegion(292, 97, 292, 97, 25, 22, 6, 0);
        }

        public void gui_drawCompass()
        {
            if ((flagsTable[31] & 0x40) == 0) return;
            if (compassDirection == -1)
            {
                compassDirectionIndex = -1;
                compassDirection = currentDirection << 6;
            }
            int t = ((compassDirection + 4) >> 3) & 0x1f;
            if (t == compassDirectionIndex) return;
            compassDirectionIndex = t;
            var c = @static.CompassDefs[t];
            int cx = (sbyte)c.x;
            int cy = (sbyte)c.y;
            screen.drawShape(screen.curPage, gameShapes[22 + lang], 294, 3, 0, 0);
            screen.drawShape(screen.curPage, gameShapes[25 + c.shapeIndex], 298 + cx, cy + 9, 0, c.flags | 0x300, new DrawShapeOpts { fadeTable = screen.paletteOverlay1, fadeLevel = 1 });
            screen.drawShape(screen.curPage, gameShapes[25 + c.shapeIndex], 299 + cx, cy + 8, 0, c.flags);
        }

        public void gui_drawBox(int x, int y, int w, int h, int frameColor1, int frameColor2, int fillColor)
        {
            w -= 1;
            h -= 1;
            if (fillColor != -1) screen.fillRect(x + 1, y + 1, x + w - 1, y + h - 1, fillColor);
            screen.drawClippedLine(x + 1, y, x + w, y, frameColor2);
            screen.drawClippedLine(x + w, y, x + w, y + h - 1, frameColor2);
            screen.drawClippedLine(x, y, x, y + h, frameColor1);
            screen.drawClippedLine(x, y + h, x + w, y + h, frameColor1);
        }

        public void gui_drawHorizontalBarGraph(int x, int y, int w, int h, int cur, int max, int col1, int col2)
        {
            if (max < 1) return;
            if (cur < 0) cur = 0;
            int e = Math.Min(cur, max);
            if (--w == 0 || --h == 0) return;
            int t = (e * w) / max;   // Math.trunc
            if (t == 0 && e != 0) t += 1;
            if (t != 0) screen.fillRect(x, y, x + t - 1, y + h, col1);
            if (t < w && col2 != 0) screen.fillRect(x + t, y, x + w - 1, y + h, col2);
        }

        // ---- controls ----
        public int gui_enableControls()
        {
            floatingCursorControl = 0;
            if (currentControlMode == 0) for (int i = 76; i < 85; i += 1) gui_toggleButtonDisplayMode(i, 2);
            gui_toggleFightButtons(false);
            return 1;
        }

        public int gui_disableControls(int controlMode)
        {
            if (currentControlMode != 0) return 0;
            floatingCursorControl = (controlMode & 2) != 0 ? 2 : 1;
            gui_toggleFightButtons(true);
            for (int i = 76; i < 85; i += 1) gui_toggleButtonDisplayMode(i, (controlMode & 2) != 0 && i > 78 ? 2 : 3);
            return 1;
        }

        public void gui_toggleButtonDisplayMode(int shapeIndex, int mode)
        {
            int[] buttonX = { 0x0056, 0x0128, 0x000c, 0x0021, 0x0122, 0x000c, 0x0021, 0x0036, 0x000c, 0x0021, 0x0036 };
            int[] buttonY = { 0x00b4, 0x00b4, 0x00b4, 0x00b4, 0x0020, 0x0084, 0x0084, 0x0084, 0x0096, 0x0096, 0x0096 };
            if (shapeIndex == 78 && (flagsTable[31] & 0x10) == 0) return;
            if (currentControlMode != 0 && needSceneRestore != 0) return;
            if (mode == 0) shapeIndex = lastButtonShape;
            int pageNum = 0;
            int x1 = shapeIndex != 0 ? buttonX[shapeIndex - 74] : 0;
            int y1 = shapeIndex != 0 ? buttonY[shapeIndex - 74] : 0;
            int x2 = 0;
            int y2 = 0;
            switch (mode)
            {
                case 1:
                    mode = 0x100;
                    lastButtonShape = shapeIndex;
                    break;
                case 0:
                    if (lastButtonShape == 0) return;
                    // fall through
                    goto case 2;
                case 2:
                    mode = 0;
                    lastButtonShape = 0;
                    break;
                case 3:
                    mode = 0;
                    lastButtonShape = 0;
                    pageNum = 6;
                    x2 = x1;
                    y2 = y1;
                    x1 = 0;
                    y1 = 0;
                    break;
                default:
                    break;
            }
            var shape = gameShapes[shapeIndex];
            screen.drawShape(pageNum, shape, x1, y1, 0, mode, new DrawShapeOpts { fadeTable = screen.paletteOverlay1, fadeLevel = 1 });
            if (pageNum == 6)
            {
                int cp = screen.curPage;
                screen.curPage = 6;
                screen.drawGridBox(x1, y1, shape.width, shape.height, 1);
                screen.copyRegion(x1, y1, x2, y2, shape.width, shape.height, pageNum, 0, true);
                screen.curPage = cp;
            }
            buttonPressTimer = getMillis() + 6 * tickLength;
        }

        public void gui_toggleFightButtons(bool disable)
        {
            for (int i = 0; i < 3; i += 1)
            {
                if ((characters[i].flags & 1) == 0) continue;
                if (disable) characters[i].flags |= 0x2000;
                else characters[i].flags &= 0xdfff;
                if (disable && !textEnabled())
                {
                    int u = selectedCharacter;
                    selectedCharacter = 99;
                    int f = updateFlags;
                    updateFlags &= 0xfffd;
                    gui_drawCharPortraitWithStats(i);
                    updateFlags = f;
                    selectedCharacter = u;
                }
                else gui_drawCharPortraitWithStats(i);
            }
        }

        public void gui_enableDefaultPlayfieldButtons()
        {
            if (awaitingSpellTarget) { awaitingSpellTarget = false; spellTargetSpell = null; ui?.Invoke("target", new object[] { false }); }
            gui_resetButtonList();
            gui_initButtonsFromList(@static.ButtonList1);
            gui_setFaceFramesControlButtons(7, 44);
            gui_setFaceFramesControlButtons(11, 44);
            gui_setFaceFramesControlButtons(17, 0);
            gui_setFaceFramesControlButtons(29, 0);
            gui_setFaceFramesControlButtons(25, 33);
            if ((flagsTable[31] & 0x20) != 0) gui_initMagicScrollButtons();
        }

        public void gui_enableSequenceButtons(int x, int y, int w, int h, int enableFlags)
        {
            gui_resetButtonList();
            sceneWindowButton = new SceneWindowButton { x = x, y = y, w = w, h = h };
            gui_initButtonsFromList(@static.ButtonList3);
            if ((enableFlags & 1) != 0) gui_initButtonsFromList(@static.ButtonList4);
            if ((enableFlags & 2) != 0) gui_initButtonsFromList(@static.ButtonList5);
        }

        public void gui_specialSceneRestoreButtons()
        {
            if (spsWindowW == 0 && spsWindowH == 0) return;
            gui_enableDefaultPlayfieldButtons();
            spsWindowX = spsWindowY = spsWindowW = spsWindowH = seqTrigger = 0;
        }

        public void gui_enableCharInventoryButtons(int charNum)
        {
            gui_resetButtonList();
            gui_initButtonsFromList(@static.ButtonList2);
            gui_initCharInventorySpecialButtons(charNum);
            gui_setFaceFramesControlButtons(21, 0);
        }

        public void gui_setFaceFramesControlButtons(int index, int xOffs)
        {
            int c = countActiveCharacters();
            for (int i = 0; i < c; i += 1) gui_initButton(index + i, activeCharsXpos[i] + xOffs);
        }

        public void gui_initCharInventorySpecialButtons(int charNum)
        {
            int @base = @static.CharInvIndex[characters[charNum].raceClassSex] * 22;
            for (int i = 0; i < 11; i += 1)
            {
                int x = @static.CharInvDefs[@base + i * 2];
                int y = @static.CharInvDefs[@base + i * 2 + 1];
                if (x != 0xff) gui_initButton(33 + i, x, y, i);
            }
        }

        public void gui_initMagicScrollButtons()
        {
            for (int i = 0; i < SPELL_SLOTS; i += 1)
            {
                if (availableSpells[i] == -1) continue;
                gui_initButton(71 + i, -1, -1, i);
            }
        }

        public void gui_initMagicSubmenu(int charNum)
        {
            gui_resetButtonList();
            subMenuIndex = charNum;
            gui_initButtonsFromList(@static.ButtonList7);
        }

        public void gui_initButtonsFromList(int[] list)
        {
            foreach (int index in list)
            {
                if (index == 0xff) break;
                gui_initButton(index);
            }
        }

        public void gui_resetButtonList()
        {
            // any button-list change ends a pending "cast a spell on who?" prompt
            if (awaitingSpellTarget) { awaitingSpellTarget = false; ui?.Invoke("target", new object[] { false }); }
            activeButtons = new List<Button>();
            buttonList = activeButtons;
            gui_notifyButtonListChanged();
        }

        public void gui_notifyButtonListChanged()
        {
            if (!buttonListChanged && !preserveEvents) removeInputTop();
            buttonListChanged = true;
        }

        public Button gui_initButton(int index, int x = -1, int y = -1, int val = -1)
        {
            var def = @static.ButtonDefs[index];
            var b = new Button
            {
                index = activeButtons.Count + 1, defIndex = index, keyCode = def.keyCode, keyCode2 = def.keyCode2, dimTableIndex = def.screenDim,
                flags = def.buttonFlags, flags2 = 0, arg = val != -1 ? val & 0xff : def.index, x = 0, y = 0, width = 0, height = 0,
            };
            if (index == 15)
            {
                b.x = activeCharsXpos[subMenuIndex] + 44;
                b.arg = subMenuIndex;
                b.y = def.y;
                b.width = def.w - 1;
                b.height = def.h - 1;
            }
            else if (index == 64)
            {
                b.x = sceneWindowButton.x;
                b.y = sceneWindowButton.y;
                b.width = sceneWindowButton.w - 1;
                b.height = sceneWindowButton.h - 1;
            }
            else
            {
                b.x = x != -1 ? x : (short)def.x;
                b.y = y != -1 ? y : (short)def.y;
                b.width = def.w - 1;
                b.height = def.h - 1;
            }
            b.callback = buttonCallbacks[index];
            var dim = screen.getScreenDim(b.dimTableIndex);
            b.absX = (b.x < 0 ? b.x + (dim.w << 3) : b.x) + (dim.sx << 3);
            b.absY = (b.y < 0 ? b.y + dim.h : b.y) + dim.sy;
            activeButtons.Add(b);
            buttonList = activeButtons;
            return b;
        }

        // GUI_LoL::processButtonList reduced to: all hit buttons (in list order) that accept this mouse button.
        public List<Button> findButtonsForClick(int x, int y, int mouseButton)
        {
            var hits = new List<Button>();
            foreach (var b in activeButtons)
            {
                if ((b.flags & 8) != 0) continue;
                if (x < b.absX || y < b.absY || x > b.absX + b.width || y > b.absY + b.height) continue;
                int accepts = mouseButton == 2 ? b.flags & 0x5000 : b.flags & 0x0500;
                if (accepts == 0) continue;
                b.flags2 = mouseButton == 2 ? 0x1080 : 0x1000;
                hits.Add(b);
            }
            return hits;
        }

        public Button findButtonForClick(int x, int y, int mouseButton)
        {
            var hits = findButtonsForClick(x, y, mouseButton);
            return hits.Count > 0 ? hits[0] : null;
        }

        public Button findButtonForKey(int code, bool shifted)
        {
            foreach (var b in activeButtons)
            {
                if ((b.flags & 8) != 0) continue;
                if (b.keyCode == code && !shifted) { b.flags2 = 0x80; return b; }
                if (b.keyCode2 == code + 256 && shifted) { b.flags2 = 0x1080; return b; }
                if (b.keyCode2 == code && !shifted) { b.flags2 = 0x1080; return b; }
            }
            return null;
        }

        // ---- button handlers ----
        public async Task<int> clickedUpArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            await moveParty(currentDirection, (button.flags2 & 0x1080) == 0x1080 ? 1 : 0, 0, 80);
            return 1;
        }

        public async Task<int> clickedDownArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            await moveParty(currentDirection ^ 2, 0, 1, 83);
            return 1;
        }

        public async Task<int> clickedLeftArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            await moveParty((currentDirection - 1) & 3, (button.flags2 & 0x1080) == 0x1080 ? 1 : 0, 2, 82);
            return 1;
        }

        public async Task<int> clickedRightArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            await moveParty((currentDirection + 1) & 3, (button.flags2 & 0x1080) == 0x1080 ? 1 : 0, 3, 84);
            return 1;
        }

        public async Task<int> clickedTurnLeftArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            gui_toggleButtonDisplayMode(79, 1);
            currentDirection = (currentDirection - 1) & 3;
            sceneDefaultUpdate = 1;
            await runLevelScript(currentBlock, 0x4000);
            initTextFading(2, 0);
            if (sceneDefaultUpdate == 0) gui_drawScene(0);
            else await movePartySmoothScrollTurn(1, true);
            gui_toggleButtonDisplayMode(79, 0);
            await runLevelScript(currentBlock, 0x10);
            return 1;
        }

        public async Task<int> clickedTurnRightArrow(Button button)
        {
            if (button.arg != 0 && !floatingCursorsEnabled) return 0;
            gui_toggleButtonDisplayMode(81, 1);
            currentDirection = (currentDirection + 1) & 3;
            sceneDefaultUpdate = 1;
            await runLevelScript(currentBlock, 0x4000);
            initTextFading(2, 0);
            if (sceneDefaultUpdate == 0) gui_drawScene(0);
            else await movePartySmoothScrollTurn(1, false);
            gui_toggleButtonDisplayMode(81, 0);
            await runLevelScript(currentBlock, 0x10);
            return 1;
        }

        public async Task<int> clickedAttackButton(Button button)
        {
            int c = button.arg;
            if ((characters[c].flags & 0x314c) != 0) return 1;
            if (selectionPinned == c) selectionPinned = -1; // acted: the selection may move on
            int bl = calcNewBlockPosition(currentBlock, currentDirection);
            if ((levelBlockProperties[bl].flags & 0x10) != 0)
            {
                await breakIceWall(null, null);
                return 1;
            }
            int target = getNearestMonsterFromCharacter(c);
            int s = 0;
            for (int i = 0; i < 4; i += 1)
            {
                if (characters[c].items[i] == 0) continue;
                await runItemScript(c, characters[c].items[i], 0x400, target, s);
                await runLevelScriptCustom(currentBlock, 0x400, c, characters[c].items[i], target, s);
                s -= 10;
            }
            if (s == 0)
            {
                await runItemScript(c, 0, 0x400, target, s);
                await runLevelScriptCustom(currentBlock, 0x400, c, 0, target, s);
            }
            s = characters[c].weaponHit != 0 ? 4 : calcMonsterSkillLevel(c, 8) + 4;
            if (itemEquipped(c, 230)) s >>= 1;
            characters[c].flags |= 4;
            gui_highlightPortraitFrame(c);
            characters[c].attackCooldownTotal = s; // remembered so the host can show how far along it is
            setCharacterUpdateEvent(c, 1, s, 1);
            return 1;
        }

        public async Task<int> clickedMagicButton(Button button)
        {
            int c = button.arg;
            if ((characters[c].flags & 0x314c) != 0) return 1;
            if (checkMagic(c, availableSpells[selectedSpell], 0) != 0) return 1;
            characters[c].flags ^= 0x10;
            gui_drawCharPortraitWithStats(c);
            gui_initMagicSubmenu(c);
            activeMagicMenu = c;
            return 1;
        }

        public async Task<int> clickedMagicSubmenu(Button button)
        {
            int spellLevel = (mouseY - 144) >> 3;
            int c = button.arg;
            gui_enableDefaultPlayfieldButtons();
            if (checkMagic(c, availableSpells[selectedSpell], spellLevel) != 0)
            {
                characters[c].flags &= 0xffef;
                gui_drawCharPortraitWithStats(c);
            }
            else
            {
                characters[c].flags |= 4;
                characters[c].flags &= 0xffef;
                if (await castSpell(c, availableSpells[selectedSpell], spellLevel) != 0)
                {
                    setCharacterUpdateEvent(c, 1, 8, 1);
                    increaseExperience(c, 2, spellLevel * spellLevel);
                }
                else
                {
                    characters[c].flags &= 0xfffb;
                    gui_drawCharPortraitWithStats(c);
                }
            }
            activeMagicMenu = -1;
            return 1;
        }

        // Cast spell slot `slot` at `spellLevel` for character c, like picking it from the spellbook submenu.
        public async Task<int> quickCastSpell(int c, int slot, int spellLevel)
        {
            if ((updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused) return 0;
            if ((characters[c].flags & 1) == 0 || (characters[c].flags & 0x314c) != 0) return 0;
            if (availableSpells[slot] == -1) return 0;
            if (activeMagicMenu != -1) await clickedScreen(null);   // JS: clickedScreen() with no button
            if (selectedSpell != slot)
            {
                gui_highlightSelectedSpell(false);
                selectedSpell = slot;
                gui_highlightSelectedSpell(true);
                gui_drawAllCharPortraitsWithStats();
            }
            // Auto power: not enough mana (or health) for the chosen power drops to the highest affordable
            // one for this cast; the chosen power stays as it was for the next time.
            var sp = @static.SpellProperties[availableSpells[slot]];
            var ch = characters[c];
            int level = spellLevel;
            while (level > 0 && (sp.mpRequired[level] > ch.magicPointsCur || sp.hpRequired[level] >= ch.hitPointsCur)) level -= 1;
            if (checkMagic(c, availableSpells[slot], level) != 0) return 0;
            if (level != spellLevel) ui?.Invoke("message", new object[] { $"{ch.name}: not enough mana for power {spellLevel + 1}, casting at power {level + 1}.", "system" });
            spellLevel = level;
            characters[c].flags |= 4;
            if (selectionPinned == c) selectionPinned = -1;
            if (await castSpell(c, availableSpells[slot], spellLevel) != 0)
            {
                setCharacterUpdateEvent(c, 1, 8, 1);
                increaseExperience(c, 2, spellLevel * spellLevel);
            }
            else
            {
                characters[c].flags &= 0xfffb;
                gui_drawCharPortraitWithStats(c);
            }
            return 1;
        }

        // Host quick actions: act with the next character in turn that is able to.
        // Starts with the selected hero, then advances the selection to the next active hero.
        public int nextReadyCharacter(string cursorName, Func<int, bool> canAct)
        {
            int start = selectedCharacter;
            for (int i = 0; i < 4; i += 1)
            {
                int c = (start + i) % 4;
                if ((characters[c].flags & 1) == 0 || (characters[c].flags & 0x314c) != 0) continue;
                if (canAct != null && !canAct(c)) continue;
                return c;
            }
            return -1;
        }

        // Can this hero act right now? (active, alive, not busy/stunned/paralysed/asleep: mask 0x314c)
        public bool uiCanAct(int c)
        {
            var ch = characters[c];
            return (ch.flags & 1) != 0 && (ch.flags & 0x314c) == 0 && ch.hitPointsCur > 0;
        }

        // After hero c acted: the pin is released and the selection moves to the next hero who can act.
        public void uiSelectNextAfter(int c)
        {
            selectionPinned = -1;
            for (int j = 1; j < 4; j += 1)
            {
                int n = (c + j) % 4;
                if (uiCanAct(n)) { gui_highlightPortraitFrame(n); return; }
            }
        }

        // Called every frame by the host: a selected hero who cannot act (no turn yet, stunned, dead)
        // hands the selection to the next hero who can. A hero the player clicked stays selected until
        // they act or fall.
        public void uiAutoSelect()
        {
            if (awaitingSpellTarget || (updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused) return;
            int s = selectedCharacter;
            if (uiCanAct(s)) return;
            var ch = characters[s];
            if (selectionPinned == s && (ch.flags & 1) != 0) return; // the player picked this hero: it holds until they act
            for (int j = 1; j < 4; j += 1)
            {
                int n = (s + j) % 4;
                if (uiCanAct(n)) { selectionPinned = -1; gui_highlightPortraitFrame(n); return; }
            }
            // nobody is ready: a dead or missing hero still hands over to any living one
            if ((ch.flags & 1) == 0 || ch.hitPointsCur <= 0) for (int j = 1; j < 4; j += 1)
            {
                int n = (s + j) % 4;
                if ((characters[n].flags & 1) != 0 && characters[n].hitPointsCur > 0) { selectionPinned = -1; gui_highlightPortraitFrame(n); return; }
            }
        }

        public async Task<int> quickAttack()
        {
            if ((updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused) return 0;
            // "Attack with the next character": start at the selected hero and take the first one who can
            // actually swing. Trying only the selected hero meant that whenever they were on cooldown the
            // button did nothing at all - it moved the selection and swallowed the press, which read as a
            // dead button while the same attack from a hero's own card worked.
            int start = selectedCharacter;
            for (int i = 0; i < 4; i += 1)
            {
                int c = (start + i) % 4;
                if ((characters[c].flags & 1) == 0 || characters[c].hitPointsCur <= 0) continue;
                if (!uiCanAct(c)) continue;
                if (c != selectedCharacter) uiSelectCharacter(c);
                await clickedAttackButton(new Button { arg = c });
                uiSelectNextAfter(c);
                return 1;
            }
            // Nobody can swing yet. Say so rather than leaving the player pressing a silent button.
            ui?.Invoke("message", new object[] { "Nobody is ready to attack yet.", "system" });
            return 0;
        }

        public async Task<int> quickCast(int slot, int spellLevel)
        {
            if ((updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused) return 0;
            if (slot < 0 || availableSpells[slot] == -1) return 0;
            int spell = availableSpells[slot];
            var sp = @static.SpellProperties[spell];
            int c = nextReadyCharacter("castCursor", (i) => sp.mpRequired[spellLevel] <= characters[i].magicPointsCur && sp.hpRequired[spellLevel] < characters[i].hitPointsCur);
            if (c < 0)
            {
                txt.printMessage(2, "Nobody can cast that right now.");
                return 0;
            }
            int r = await quickCastSpell(c, slot, spellLevel);
            uiSelectNextAfter(c);
            return r;
        }

        public async Task<int> clickedScreen(Button button)
        {
            characters[activeMagicMenu].flags &= 0xffef;
            gui_drawCharPortraitWithStats(activeMagicMenu);
            activeMagicMenu = -1;
            gui_enableDefaultPlayfieldButtons();
            if ((button.flags2 & 0x80) == 0)
            {
                // re-dispatch the click against the restored button list
                var b = findButtonForClick(mouseX, mouseY, (button.flags2 & 0x1080) != 0 ? 2 : 1);
                if (b != null && b.callback != null) await b.callback(b);
            }
            return 1;
        }

        public async Task<int> clickedPortraitLeft(Button button)
        {
            disableSysTimer(2);
            if (!weaponsDisabled)
            {
                pageBuffer2 = screen.copyRegionToBuffer(2, 0, 0, 320, 200);
                screen.copyPage(0, 2);
                pageBuffer1 = screen.copyRegionToBuffer(2, 0, 0, 320, 200);
                updateFlags |= 0x0c;
                gui_disableControls(1);
            }
            selectedCharacter = button.arg;
            weaponsDisabled = true;
            gui_displayCharInventory(selectedCharacter);
            gui_enableCharInventoryButtons(selectedCharacter);
            return 1;
        }

        public async Task<int> clickedLiveMagicBarsLeft(Button button)
        {
            gui_highlightPortraitFrame(button.arg);
            var c = characters[button.arg];
            txt.printMessage(0, formatString(getLangString(0x4047), c.name, c.hitPointsCur, c.hitPointsMax, c.magicPointsCur, c.magicPointsMax));
            return 1;
        }

        public async Task<int> clickedPortraitEtcRight(Button button)
        {
            if (itemInHand == 0) return 1;
            int flg = itemProperties[itemsInPlay[itemInHand].itemPropertyIndex].flags;
            int c = button.arg;
            if ((flg & 1) != 0)
            {
                if ((characters[c].flags & 8) == 0 || (flg & 0x20) != 0)
                {
                    await runItemScript(c, itemInHand, 0x400, 0, 0);
                    await runLevelScriptCustom(currentBlock, 0x400, c, itemInHand, 0, 0);
                }
                else txt.printMessage(2, Gui_replaceFirst(getLangString(0x402c), "%s", characters[c].name));
                return 1;
            }
            txt.printMessage(2, getLangString((flg & 8) != 0 ? 0x4029 : (flg & 0x10) != 0 ? 0x402a : 0x402b));
            return 1;
        }

        public async Task<int> clickedCharInventorySlot(Button button)
        {
            if (itemInHand != 0)
            {
                int sl = 1 << button.arg;
                var prop = itemProperties[itemsInPlay[itemInHand].itemPropertyIndex];
                if ((sl & prop.type) == 0)
                {
                    bool f = false;
                    for (int i = 0; i < 11; i += 1)
                    {
                        if ((prop.type & (1 << i)) == 0) continue;
                        ExtraItemDef extra = null;
                        if (extraItems != null) extraItems.TryGetValue(itemsInPlay[itemInHand].itemPropertyIndex, out extra);
                        string extraName = extra?.name;
                        txt.printMessage(0, formatString(getLangString(i > 3 ? 0x418a : 0x418b), !string.IsNullOrEmpty(extraName) ? extraName : getLangString(prop.nameStringId), getLangString(@static.InventoryDesc[i])));
                        f = true;
                    }
                    if (!f) txt.printMessage(itemsInPlay[itemInHand].itemPropertyIndex == 231 ? 2 : 0, getLangString(0x418c));
                    return 1;
                }
            }
            else if (characters[selectedCharacter].items[button.arg] == 0)
            {
                txt.printMessage(0, getLangString(@static.InventoryDesc[button.arg] + 8));
                return 1;
            }
            int ih = itemInHand;
            await setHandItem(characters[selectedCharacter].items[button.arg]);
            characters[selectedCharacter].items[button.arg] = (ushort)ih;
            gui_drawCharInventoryItem(button.arg);
            recalcCharacterStats(selectedCharacter);
            if (itemInHand != 0) await runItemScript(selectedCharacter, itemInHand, 0x100, 0, 0);
            if (ih != 0) await runItemScript(selectedCharacter, ih, 0x80, 0, 0);
            gui_drawCharInventoryItem(button.arg);
            gui_drawCharPortraitWithStats(selectedCharacter);
            await gui_changeCharacterStats(selectedCharacter);
            return 1;
        }

        public async Task<int> clickedExitCharInventory()
        {
            updateFlags &= 0xfff3;
            gui_enableDefaultPlayfieldButtons();
            weaponsDisabled = false;
            for (int i = 0; i < 4; i += 1) if ((charInventoryUnk & (1 << i)) != 0) characters[i].flags &= 0xf1ff;
            if (pageBuffer1 != null) screen.copyBlockToPage(2, 0, 0, 320, 200, pageBuffer1);
            int cp = screen.curPage;
            screen.curPage = 2;
            gui_drawAllCharPortraitsWithStats();
            gui_drawInventory();
            screen.curPage = cp;
            screen.copyPage(2, 0);
            gui_enableControls();
            if (pageBuffer2 != null) screen.copyBlockToPage(2, 0, 0, 320, 200, pageBuffer2);
            lastCharInventory = -1;
            updateDrawPage2();
            enableSysTimer(2);
            return 1;
        }

        public async Task<int> clickedSceneDropItem(Button button)
        {
            int[] offsX = { 0x40, 0xc0, 0x40, 0xc0 };
            int[] offsY = { 0x40, 0x40, 0xc0, 0xc0 };
            if ((updateFlags & 1) != 0 || itemInHand == 0) return 0;
            int block = currentBlock;
            if (button.arg > 1)
            {
                block = calcNewBlockPosition(currentBlock, currentDirection);
                int f = wllWallFlags[levelBlockProperties[block].walls[currentDirection ^ 2]];
                if ((f & 0x80) == 0 || (f & 2) != 0) return 1;
            }
            int i = DROP_ITEM_DIR_INDEX[(currentDirection << 2) + button.arg];
            var (x, y) = calcCoordinates(block, offsX[i], offsY[i]);
            await setItemPosition(itemInHand, x, y, 0, 1);
            await setHandItem(0);
            return 1;
        }

        public async Task<int> clickedScenePickupItem(Button button)
        {
            onCall?.Invoke("clickedScenePickupItem", new object[] { button });   // the host's wrapper (main.mjs wrapCount)
            int[] checkX = { 0, 0, 1, 0, -1, -1, 1, 1, -1, 0, 2, 0, -2, -1, 1, 2, 2, 1, -1, -2, -2 };
            int[] checkY = { 0, -1, 0, 1, 0, -1, -1, 1, 1, -2, 0, 2, 0, -2, -2, -1, 1, 2, 2, 1, -1 };
            if ((updateFlags & 1) != 0 || itemInHand != 0) return 0;
            int cp = screen.curPage;
            screen.curPage = sceneDrawPage1;
            redrawSceneItem();
            var dim = screen.getScreenDim(button.dimTableIndex);
            int clipLeft = (dim.sx << 3) + button.x;
            int clipTop = dim.sy + button.y;
            int clipRight = clipLeft + button.width - 1;
            int clipBottom = clipTop + button.height - 1;
            int p = 0;
            for (int i = 0; i < checkX.Length; i += 1)
            {
                int px = Math.Max(clipLeft, Math.Min(clipRight, mouseX + checkX[i]));
                int py = Math.Max(clipTop, Math.Min(clipBottom, mouseY + checkY[i]));
                p = screen.getPagePixel(screen.curPage, px, py);
                if (p != 0) break;
            }
            screen.curPage = cp;
            if (p == 0) return 0;
            int block = p <= 128 ? calcNewBlockPosition(currentBlock, currentDirection) : currentBlock;
            int found = checkSceneForItems(levelBlockProperties[block], p & 0x7f);
            if (found != -1)
            {
                await removeLevelItem(found, block);
                await setHandItem(found);
            }
            sceneUpdateRequired = true;
            return 1;
        }

        public async Task<int> clickedInventorySlot(Button button)
        {
            int slot = inventoryCurItem + button.arg;
            if (slot >= inventory.Length) slot -= inventory.Length;
            int slotItem = inventory[slot];
            int hItem = itemInHand;
            int pi(int i) => itemsInPlay[i].itemPropertyIndex;
            if ((pi(hItem) == 281 || pi(slotItem) == 281) && (pi(hItem) == 220 || pi(slotItem) == 220))
            {
                inventory[slot] = 0;
                gui_drawInventoryItem(button.arg);
                snd_playSoundEffect(99, -1);
                await playWsaInBox("TRUTH.WSA", button.x, button.y - 3, 25, 27, 25, 7);
                deleteItem(slotItem);
                deleteItem(hItem);
                await setHandItem(0);
                inventory[slot] = (ushort)makeItem(280, 0, 0);
            }
            else
            {
                await setHandItem(slotItem);
                inventory[slot] = (ushort)hItem;
            }
            gui_drawInventoryItem(button.arg);
            return 1;
        }

        // Host inventory panel: swap the hand item with an absolute inventory slot (0-47).
        public async Task<int> inventorySlotClick(int slot)
        {
            // no scene guard: like the original inventory strip it also works inside a shop window
            int slotItem = inventory[slot];
            int hItem = itemInHand;
            int pi(int i) => itemsInPlay[i].itemPropertyIndex;
            if ((pi(hItem) == 281 || pi(slotItem) == 281) && (pi(hItem) == 220 || pi(slotItem) == 220))
            {
                // Bezel cup + Ruby of Truth combine, like clickedInventorySlot without the box animation
                inventory[slot] = 0;
                snd_playSoundEffect(99, -1);
                deleteItem(slotItem);
                deleteItem(hItem);
                await setHandItem(0);
                inventory[slot] = (ushort)makeItem(280, 0, 0);
            }
            else
            {
                await setHandItem(slotItem);
                inventory[slot] = (ushort)hItem;
            }
            gui_drawInventory();
            return 1;
        }

        // Host inventory panel: scroll the playfield's quick bar so that `slot` is its first entry.
        public void inventoryShowSlot(int slot)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0) return;
            inventoryCurItem = ((slot % inventory.Length) + inventory.Length) % inventory.Length;
            gui_drawInventory();
        }

        public string itemName(int item)
        {
            if (item == 0 || item < 0 || item >= itemsInPlay.Length || itemsInPlay[item] == null) return "";
            int prop = itemsInPlay[item].itemPropertyIndex;
            if (extraItems != null && extraItems.TryGetValue(prop, out var extra) && extra != null) return extra.name; // a potion the port added
            var p = prop >= 0 && prop < itemProperties.Count ? itemProperties[prop] : null;
            string s = p != null ? getLangString(p.nameStringId) : null;
            return !string.IsNullOrEmpty(s) ? s : "";
        }

        public async Task<int> clickedInventoryScroll(Button button)
        {
            int inc = (sbyte)button.arg;
            int shp = inc == 1 ? 75 : 74;
            if ((button.flags2 & 0x1000) != 0) inc *= 9;
            inventoryCurItem += inc;
            gui_toggleButtonDisplayMode(shp, 1);
            if (inventoryCurItem < 0) inventoryCurItem += inventory.Length;
            if (inventoryCurItem >= inventory.Length) inventoryCurItem -= inventory.Length;
            gui_drawInventory();
            await delay(6 * tickLength, true);
            gui_toggleButtonDisplayMode(shp, 0);
            return 1;
        }

        public async Task<int> clickedWall()
        {
            int block = calcNewBlockPosition(currentBlock, currentDirection);
            int dir = currentDirection ^ 2;
            int type = specialWallTypes[levelBlockProperties[block].walls[dir]];
            switch (type)
            {
                case 1:
                {
                    // `block` is the one in front of the party; the camp's doors are recognised by where the
                    // party is standing, not by that block.
                    string piece = uiInCamp() ? uiCampFacing() : null;
                    if (piece != null) { ui?.Invoke("camp", new object[] { piece }); return 1; } // the camp's own furniture
                    return await clickedWallShape(block, dir);
                }
                case 2:
                {
                    int r = await clickedLeverOn(block, dir);
                    if (r != 0 && uiInDungeon()) uiDungeonLever(block, dir);
                    return r;
                }
                case 3:
                {
                    int r = await clickedLeverOff(block, dir);
                    if (r != 0 && uiInDungeon()) uiDungeonLever(block, dir);
                    return r;
                }
                case 4: return await clickedWallOnlyScript(block);
                case 5:
                    if (uiInDungeon() && uiDungeonDoorLocked(block))
                    {
                        ui?.Invoke("message", new object[] { "The door will not move. Something else has to give first.", "system" });
                        return 1;
                    }
                    return await clickedDoorSwitch(block, dir);
                case 6: return await clickedNiche(block, dir);
                default: return 0;
            }
        }

        public async Task<int> clickedSequenceWindow()
        {
            await runLevelScript(calcNewBlockPosition(currentBlock, currentDirection), 0x40);
            if (seqTrigger == 0 || !(mouseX >= seqWindowX1 && mouseX < seqWindowX2 && mouseY >= seqWindowY1 && mouseY < seqWindowY2))
            {
                seqTrigger = 0;
                removeInputTop();
            }
            return 1;
        }

        public async Task<int> clickedScroll(Button button)
        {
            if (selectedSpell == button.arg) return 1;
            gui_highlightSelectedSpell(false);
            selectedSpell = button.arg;
            gui_highlightSelectedSpell(true);
            gui_drawAllCharPortraitsWithStats();
            return 1;
        }

        public async Task<int> clickedSpellTargetCharacter(Button button)
        {
            int t = button.arg;
            txt.printMessage(0, $"{characters[t].name}.\r");
            if ((@static.SpellProperties[activeSpell.spell].flags & 0xff) == 1)
            {
                activeSpell.target = t;
                await castHealOnSingleCharacter(activeSpell);
            }
            gui_enableDefaultPlayfieldButtons();
            return 1;
        }

        public async Task<int> clickedSpellTargetScene()
        {
            var c = characters[activeSpell.charNum];
            txt.printMessage(0, getLangString(0x4041));
            c.magicPointsCur = Math.Min(c.magicPointsMax, c.magicPointsCur + activeSpell.p.mpRequired[activeSpell.level]);
            c.hitPointsCur = Math.Min(c.hitPointsMax, c.hitPointsCur + activeSpell.p.hpRequired[activeSpell.level]);
            gui_drawCharPortraitWithStats(activeSpell.charNum);
            gui_enableDefaultPlayfieldButtons();
            return 1;
        }

        public async Task<int> clickedSceneThrowItem()
        {
            if ((updateFlags & 1) != 0) return 0;
            int block = calcNewBlockPosition(currentBlock, currentDirection);
            if ((wllWallFlags[levelBlockProperties[block].walls[currentDirection ^ 2]] & 2) != 0 || itemInHand == 0) return 0;
            var (x, y) = calcCoordinates(currentBlock, 0x80, 0x80);
            if (await launchObject(0, itemInHand, x, y, 12, currentDirection << 1, 6, selectedCharacter, 0x3f))
            {
                snd_playSoundEffect(18, -1);
                await setHandItem(0);
            }
            sceneUpdateRequired = true;
            return 1;
        }

        public async Task<int> clickedOptions()
        {
            removeInputTop();
            gui_toggleButtonDisplayMode(76, 1);
            updateFlags |= 4;
            if (weaponsDisabled) await clickedExitCharInventory();
            initTextFading(0, 1);
            stopPortraitSpeechAnim();
            setLampMode(true);
            setMouseCursorToIcon(0);
            disableSysTimer(2);
            gui_toggleButtonDisplayMode(76, 0);
            await gui.runMenu(gui.mainMenu);
            updateFlags &= 0xfffb;
            setMouseCursorToItemInHand();
            resetLampStatus();
            gui_enableDefaultPlayfieldButtons();
            enableSysTimer(2);
            updateDrawPage2();
            gui_drawPlayField();
            return 1;
        }

        public async Task<int> clickedRestParty()
        {
            gui_toggleButtonDisplayMode(77, 1);
            if (weaponsDisabled) await clickedExitCharInventory();
            int tHp = -1;
            int tMp = -1;
            int tHa = -1;
            int needPoisoningFlags = 0;
            int needHealingFlags = 0;
            int needMagicGainFlags = 0;
            for (int i = 0; i < 4; i += 1)
            {
                var c = characters[i];
                if ((c.flags & 1) == 0 || (c.flags & 8) != 0) continue;
                if (c.hitPointsMax > tHp) tHp = c.hitPointsMax;
                if (c.magicPointsMax > tMp) tMp = c.magicPointsMax;
                if ((c.flags & 0x80) != 0)
                {
                    needPoisoningFlags |= 1 << i;
                    if (c.hitPointsCur > tHa) tHa = c.hitPointsCur;
                }
                else if (c.hitPointsCur < c.hitPointsMax) needHealingFlags |= 1 << i;
                if (c.magicPointsCur < c.magicPointsMax) needMagicGainFlags |= 1 << i;
                c.flags |= 0x1000;
            }
            removeInputTop();
            // campMode: the camp is made even when nobody needs rest - the party can still be interrupted
            // there, and it is where fast travel is cast from.
            if (needHealingFlags != 0 || needMagicGainFlags != 0 || campMode)
            {
                screen.fillRect(112, 0, 288, 120, 1);
                gui_drawAllCharPortraitsWithStats();
                txt.printMessage(0x8000, getLangString(0x4057));
                gui_toggleButtonDisplayMode(77, 0);
                // JS float division then Math.trunc (600 / 0 is Infinity, which Math.min clamps)
                int h = (int)Math.Min(30, Math.Truncate(600.0 / tHp));
                int m = (int)Math.Min(30, Math.Truncate(600.0 / tMp));
                int a = tHa > 0 ? (int)Math.Min(15, Math.Truncate(600.0 / tHa)) : 15;
                double delay1 = getMillis() + h * tickLength;
                double delay2 = getMillis() + m * tickLength;
                double delay3 = getMillis() + a * tickLength;
                partyAwake = false;
                updateFlags |= 1;
                for (int i = 0; i < 32; i += 1)
                {
                    timerProcessMonsters(0);
                    timerProcessMonsters(1);
                    timerProcessDoors();
                    timerProcessFlyingObjects();
                    await drainAsync();
                    if (partyAwake) break;
                }
                resetBlockProperties();
                restInterrupted = false;
                do
                {
                    for (int i = 0; i < 8; i += 1)
                    {
                        timerProcessMonsters(0);
                        timerProcessMonsters(1);
                        timerProcessDoors();
                        timerProcessFlyingObjects();
                        await drainAsync();
                        if (partyAwake) break;
                    }
                    if (events.Count != 0)
                    {
                        removeInputTop();
                        break;
                    }
                    if (!partyAwake)
                    {
                        if (getMillis() > delay3)
                        {
                            for (int i = 0; i < 4; i += 1)
                            {
                                if ((needPoisoningFlags & (1 << i)) == 0) continue;
                                inflictDamage(i, 1, 0x8000, 1, 0x80);
                                if ((characters[i].flags & 8) != 0) needPoisoningFlags &= ~(1 << i);
                            }
                            delay3 = getMillis() + a * tickLength;
                        }
                        if (getMillis() > delay1)
                        {
                            for (int i = 0; i < 4; i += 1)
                            {
                                if ((needHealingFlags & (1 << i)) == 0) continue;
                                increaseCharacterHitpoints(i, 1, false);
                                gui_drawCharPortraitWithStats(i);
                                if (characters[i].hitPointsCur == characters[i].hitPointsMax) needHealingFlags &= ~(1 << i);
                            }
                            delay1 = getMillis() + h * tickLength;
                        }
                        if (getMillis() > delay2)
                        {
                            for (int i = 0; i < 4; i += 1)
                            {
                                if ((needMagicGainFlags & (1 << i)) == 0) continue;
                                characters[i].magicPointsCur += 1;
                                gui_drawCharPortraitWithStats(i);
                                if (characters[i].magicPointsCur == characters[i].magicPointsMax) needMagicGainFlags &= ~(1 << i);
                            }
                            delay2 = getMillis() + m * tickLength;
                        }
                    }
                    await delay(tickLength);
                    // campMode: the party stays in the camp after they are healed, until the host says to leave
                    // (an Escape event, handled above) or something wakes them.
                } while (!partyAwake && (needHealingFlags != 0 || needMagicGainFlags != 0 || campMode));
                for (int i = 0; i < 4; i += 1)
                {
                    int frm = 0;
                    int upd = 0;
                    bool setframe = true;
                    if ((characters[i].flags & 0x1000) != 0)
                    {
                        characters[i].flags &= 0xefff;
                        if (partyAwake)
                        {
                            if (characters[i].damageSuffered != 0)
                            {
                                frm = 5;
                                snd_playSoundEffect(characters[i].screamSfx, -1);
                            }
                            else frm = 4;
                            upd = 6;
                        }
                    }
                    else if (characters[i].damageSuffered != 0) setframe = false;
                    else frm = 4;
                    if (setframe) setTemporaryFaceFrame(i, frm, upd, 1);
                }
                updateFlags &= 0xfffe;
                partyAwake = true;
                updateDrawPage2();
                gui_drawScene(0);
                txt.printMessage(0x8000, getLangString(0x4059));
                await screen.fadeToPalette1(40);
            }
            else
            {
                for (int i = 0; i < 4; i += 1) characters[i].flags &= 0xefff;
                if (needPoisoningFlags != 0)
                {
                    setTemporaryFaceFrameForAllCharacters(0, 0, 0);
                    for (int i = 0; i < 4; i += 1) if ((needPoisoningFlags & (1 << i)) != 0) setTemporaryFaceFrame(i, 3, 8, 0);
                    txt.printMessage(0x8000, getLangString(0x405a));
                    gui_drawAllCharPortraitsWithStats();
                }
                else
                {
                    setTemporaryFaceFrameForAllCharacters(2, 4, 1);
                    txt.printMessage(0x8000, getLangString(0x4058));
                }
                gui_toggleButtonDisplayMode(77, 0);
            }
            return 1;
        }

        public async Task<int> clickedMoneyBox()
        {
            txt.printMessage(0, formatString(getLangString(credits == 1 ? 0x402d : 0x402e), credits));
            return 1;
        }

        public async Task<int> clickedCompass()
        {
            if ((flagsTable[31] & 0x40) == 0) return 0;
            if (compassBroken != 0)
            {
                if (characterSays(0x425b, -1, true) != 0) txt.printMessage(4, getLangString(0x425b));
            }
            else txt.printMessage(0, getLangString(0x402f + currentDirection));
            return 1;
        }

        public async Task<int> clickedAutomap()
        {
            if ((flagsTable[31] & 0x10) == 0) return 0;
            removeInputTop();
            await displayAutomap();
            gui_drawPlayField();
            await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
            return 1;
        }

        public async Task<int> clickedLamp()
        {
            if ((flagsTable[31] & 0x08) == 0) return 0;
            if (itemsInPlay[itemInHand].itemPropertyIndex == 248)
            {
                if (lampOilStatus >= 100)
                {
                    txt.printMessage(0, getLangString(0x4061));
                    return 1;
                }
                txt.printMessage(0, getLangString(0x4062));
                deleteItem(itemInHand);
                snd_playSoundEffect(181, -1);
                await setHandItem(0);
                lampOilStatus += 100;
            }
            else
            {
                int s = lampOilStatus >= 100 ? 0x4060 : lampOilStatus == 0 ? 0x405c : lampOilStatus / 33 + 0x405d;
                txt.printMessage(0, Gui_replaceFirst(getLangString(0x405b), "%s", getLangString(s)));
            }
            if (brightness != 0) await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
            return 1;
        }

        // Host lantern controls. The original lantern is always lit while it has oil; the switch is an
        // extra that stops the oil from burning (and darkens the view) until switched back on.
        public bool toggleLantern()
        {
            if ((flagsTable[31] & 0x08) == 0) return false;
            lampSwitchedOff = !lampSwitchedOff;
            lampEffect = -1;
            updateLampStatus();
            return true;
        }

        // Uses an oil flask from the inventory (or the hand) on the lantern.
        public async Task<int> refillLantern()
        {
            if ((flagsTable[31] & 0x08) == 0 || (updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0) return 0;
            bool isOil(int item) => item != 0 && itemsInPlay[item].itemPropertyIndex == 248;
            if (!isOil(itemInHand))
            {
                int slot = Array.FindIndex(inventory, (item) => isOil(item));
                if (slot < 0)
                {
                    txt.printMessage(2, "No lamp oil in the inventory.");
                    return 0;
                }
                int flask = inventory[slot];
                inventory[slot] = (ushort)itemInHand;
                await setHandItem(flask);
                gui_drawInventory();
            }
            return await clickedLamp();
        }

        public async Task<int> clickedStatusIcon()
        {
            int t = Math.Max(0, mouseX - 220);
            t = Math.Min(2, t / 14);   // Math.trunc
            int str = (charStatusFlags[t] + 1) & 0xff;
            if (str == 0 || str > 3) return 1;
            txt.printMessage(0x8002, getLangString(str == 1 ? 0x424c : str == 2 ? 0x424e : 0x424d));
            return 1;
        }

        // printf-style %s/%d substitution used by the message strings.
        public string formatString(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return "";
            int i = 0;
            return Regex.Replace(format, "%[sd]", _ => (i < args.Length ? $"{args[i++]}" : ""));
        }

        /// <summary>JS string.replace(string, string): the first occurrence only.</summary>
        static string Gui_replaceFirst(string s, string find, string with)
        {
            int at = s.IndexOf(find, StringComparison.Ordinal);
            return at < 0 ? s : s.Substring(0, at) + with + s.Substring(at + find.Length);
        }
    }
}
