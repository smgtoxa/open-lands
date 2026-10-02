// src/game/automap.mjs: magic atlas / automap (lol.cpp).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>automap.mjs initAutomap: defaultLegendData[] entries.</summary>
    public sealed class DefaultLegendData
    {
        public int shapeIndex;
        public bool enable;
        public int y, stringId;
    }

    /// <summary>automap.mjs drawMinimap: { page, x, y, w, h, palette }</summary>
    public sealed class MinimapRegion
    {
        public int page, x, y, w, h;
        public byte[] palette;
    }

    public sealed partial class LandsOfLore
    {
        static readonly int[][] MAP_COORDS =
        {
            new[] { 0, 7, 0, -5 }, new[] { -5, 0, 6, 0 }, new[] { 7, 5, 7, 1 }, new[] { 5, 6, 4, 6 }, new[] { 0, 7, 0, -1 }, new[] { -3, 0, 6, 0 },
            new[] { 6, 7, 6, -3 }, new[] { -3, 5, 6, 5 }, new[] { 1, 5, 1, 1 }, new[] { 3, 1, 3, 1 }, new[] { -1, 6, -1, -8 }, new[] { -7, -1, 5, -1 },
        };

        static readonly (int xo, int yo)[] Automap_NEIGHBOURS = { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0) };

        public List<DefaultLegendData> defaultLegendData;
        public byte[] mapCursorOverlay;
        public byte[] mapOverlay;
        public Shape[] automapShapes;
        public int currentMapLevel;
        public int automapTopLeftX, automapTopLeftY;
        public bool mapUpdateNeeded;
        /// <summary>32 entries of [x, y, stringId, x2, y2, shape] (LEVELn.XXX)</summary>
        public int[][] legendData;
        public bool automapActive;
        public byte[] minimapPalette;
        public byte[] minimapOverlay;

        public void initAutomap()
        {
            var legend = @static.LegendData;
            int entrySize = legend.Length / 12;
            defaultLegendData = new List<DefaultLegendData>();
            for (int i = 0, p = 0; i < 12 && p + entrySize <= legend.Length; i += 1)
            {
                int shapeIndex = legend[p++];
                bool enable = legend[p++] != 0;
                int y = entrySize == 5 ? (sbyte)legend[p++] : i == 10 ? -5 : 0;
                int stringId = legend[p] | (legend[p + 1] << 8);
                p += 2;
                defaultLegendData.Add(new DefaultLegendData { shapeIndex = shapeIndex, enable = enable, y = y, stringId = stringId });
            }
            mapCursorOverlay = @static.MapCursorOvl.Select(v => (byte)v).ToArray();
            mapOverlay = new byte[256];
            automapShapes = new Shape[0];
            currentMapLevel = 0;
            automapTopLeftX = automapTopLeftY = 0;
            mapUpdateNeeded = false;
            legendData = new int[0][];
        }

        public void updateAutoMap(int block)
        {
            if ((flagsTable[31] & 0x10) == 0) return;
            levelBlockProperties[block].flags |= 7;
            int x = block & 0x1f;
            int y = block >> 5;
            foreach (var (xo, yo) in Automap_NEIGHBOURS) updateAutoMapIntern(block, x, y, xo, yo);
        }

        public bool updateAutoMapIntern(int block, int x, int y, int xOffs, int yOffs)
        {
            int[] blockPosTable = { 1, -1, 3, 2, -1, 0, -1, 0, 1, -32, 0, 32 };
            x += xOffs;
            y += yOffs;
            if ((x & 0xffe0) != 0 || (y & 0xffe0) != 0) return false;
            xOffs += 1;
            yOffs += 1;
            int fx = blockPosTable[xOffs];
            int b = block + blockPosTable[6 + xOffs];
            if (fx != -1 && (wllAutomapData[levelBlockProperties[b].walls[fx]] & 0xc0) != 0) return false;
            int fy = blockPosTable[3 + yOffs];
            b = block + blockPosTable[9 + yOffs];
            if (fy != -1 && (wllAutomapData[levelBlockProperties[b].walls[fy]] & 0xc0) != 0) return false;
            b = block + blockPosTable[6 + xOffs] + blockPosTable[9 + yOffs];
            if (fx != -1 && fy != -1 && (wllAutomapData[levelBlockProperties[b].walls[fx]] & 0xc0) != 0 && (wllAutomapData[levelBlockProperties[b].walls[fy]] & 0xc0) != 0) return false;
            levelBlockProperties[b].flags |= 7;
            return true;
        }

        public void loadMapLegendData(int level)
        {
            legendData = Enumerable.Range(0, 32).Select(_ => new[] { 0xffff, 0, 0, 0, 0, 0xffff }).ToArray();
            string file = $"LEVEL{level}.XXX";
            if (!res.exists(file)) return;
            var data = res.get(file);
            int u16(int o) => data[o] | (data[o + 1] << 8);   // DataView.getUint16(o, true)
            int size = Math.Min(data.Length / 12, 32);
            for (int i = 0; i < size; i += 1)
            {
                int p = i * 12;
                legendData[i] = new[] { u16(p + 6), u16(p + 8), u16(p + 10), u16(p), u16(p + 2), u16(p + 4) };
            }
        }

        public async Task displayAutomap()
        {
            automapActive = true;
            snd_playSoundEffect(105, -1);
            gui_toggleButtonDisplayMode(78, 1);
            currentMapLevel = currentLevel;
            var tmpWll = (byte[])wllAutomapData.Clone();
            screen.loadBitmap(res.get("PARCH.CPS"), 2, screen.getPalette(3));
            var shapes = LolShapes.decodeShapeFile(Cps.decodeBitmapData(res.get("AUTOBUT.SHP")).data);
            automapShapes = Js.Slice(shapes, 11, 120);
            screen.generateGrayOverlay(screen.getPalette(3), mapOverlay, 52, 0, 0, 0, 256, false);
            screen.fonts.TryGetValue("9", out var font9);
            screen.fonts.TryGetValue("6", out var font6);
            screen.loadFont("9", res.get("FONT9PN.FNT"));
            screen.loadFont("6", res.get("FONT6PN.FNT"));
            foreach (var l in defaultLegendData) l.enable = false;
            disableSysTimer(2);
            generateTempData();
            resetItems(1);
            disableMonsters();
            bool exitAutomap = false;
            mapUpdateNeeded = false;
            restoreBlockTempData(currentMapLevel);
            loadMapLegendData(currentMapLevel);
            await screen.fadeToBlack(10);
            drawMapPage(2);
            screen.copyPage(2, 0);
            await screen.fadePalette(screen.getPalette(3), 10);
            double delayTimer = getMillis() + 8 * tickLength;
            removeInputTop();
            while (!exitAutomap && !quit)
            {
                if (mapUpdateNeeded)
                {
                    drawMapPage(2);
                    screen.copyPage(2, 0);
                    mapUpdateNeeded = false;
                }
                if (getMillis() >= delayTimer)
                {
                    redrawMapCursor();
                    delayTimer = getMillis() + 8 * tickLength;
                }
                var ev = shiftEvent();
                if (ev != null)
                {
                    exitAutomap = await automapProcessButtons(ev);
                    gui_notifyButtonListChanged();
                    if (ev.type == "key" && ev.key == "c")
                    {
                        for (int i = 0; i < 1024; i += 1) levelBlockProperties[i].flags |= 7;
                        mapUpdateNeeded = true;
                    }
                    else if (ev.type == "key" && ev.key == "Escape") exitAutomap = true;
                }
                await delay(tickLength);
            }
            screen.fonts["9"] = font9;
            screen.fonts["6"] = font6;
            await screen.fadeToBlack(10);
            await loadLevelWallData(currentLevel, false);
            Js.Set(wllAutomapData, tmpWll);
            restoreBlockTempData(currentLevel);
            addLevelItems();
            gui_notifyButtonListChanged();
            enableSysTimer(2);
            automapActive = false;
        }

        public void drawMapPage(int pageNum)
        {
            int xOffset = 0;
            for (int i = 0; i < 2; i += 1)
            {
                screen.loadBitmap(res.get("PARCH.CPS"), pageNum, screen.getPalette(3));
                int cp = screen.curPage;
                screen.curPage = pageNum;
                var of = screen.setFont("9");
                screen.printText(getLangString(@static.MapStringId[currentMapLevel]) ?? "", 236 + xOffset, 8, 1, 0);
                int blX = mapGetStartPosX();
                int bl = (mapGetStartPosY() << 5) + blX;
                int sx = automapTopLeftX;
                int sy = automapTopLeftY;
                for (; bl < 1024; bl += 1)
                {
                    var w = levelBlockProperties[bl].walls;
                    var A = wllAutomapData;
                    if ((levelBlockProperties[bl].flags & 7) == 7 && (A[w[0]] & 0xc0) == 0 && (A[w[2]] & 0xc0) == 0 && (A[w[1]] & 0xc0) == 0 && (A[w[3]] & 0xc0) == 0)
                    {
                        int b0 = calcNewBlockPosition(bl, 0);
                        int b2 = calcNewBlockPosition(bl, 2);
                        int b1 = calcNewBlockPosition(bl, 1);
                        int b3 = calcNewBlockPosition(bl, 3);
                        int w02 = levelBlockProperties[b0].walls[2];
                        int w20 = levelBlockProperties[b2].walls[0];
                        int w13 = levelBlockProperties[b1].walls[3];
                        int w31 = levelBlockProperties[b3].walls[1];
                        int cur = screen.curPage;
                        screen.copyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 7, 6, 0, mapOverlay);
                        drawMapBlockWall(b3, w31, sx, sy, 3);
                        drawMapShape(w31, sx, sy, 3);
                        if ((A[w31] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 1, 6, 0, mapOverlay);
                        drawMapBlockWall(b1, w13, sx, sy, 1);
                        drawMapShape(w13, sx, sy, 1);
                        if ((A[w13] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(cur, sx + 6, sy, cur, sx + 6, sy, 1, 6, 0, mapOverlay);
                        drawMapBlockWall(b0, w02, sx, sy, 0);
                        drawMapShape(w02, sx, sy, 0);
                        if ((A[w02] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(cur, sx, sy, cur, sx, sy, 7, 1, 0, mapOverlay);
                        drawMapBlockWall(b2, w20, sx, sy, 2);
                        drawMapShape(w20, sx, sy, 2);
                        if ((A[w20] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(cur, sx, sy + 5, cur, sx, sy + 5, 7, 1, 0, mapOverlay);
                    }
                    sx += 7;
                    if (bl % 32 == 31)
                    {
                        sx = automapTopLeftX;
                        sy += 6;
                        bl += blX;
                    }
                }
                screen.setFont(of);
                screen.curPage = cp;
                of = screen.setFont("6");
                int tY = 0;
                int startX = mapGetStartPosX();
                int startY = mapGetStartPosY();
                for (int ii = 0; ii < 32; ii += 1)
                {
                    var l = legendData[ii];
                    if (l[0] == 0xffff) break;
                    int cbl = l[0] + (l[1] << 5);
                    if ((levelBlockProperties[cbl].flags & 7) != 7) continue;
                    if (l[2] == 0xffff) continue;
                    printMapText(l[2], 244 + xOffset, (tY << 3) + 22);
                    if (l[5] == 0xffff) { tY += 1; continue; }
                    int cbl2 = l[3] + (l[4] << 5);
                    levelBlockProperties[cbl2].flags |= 7;
                    screen.drawShape(2, automapShapes[l[5] << 2], (l[3] - startX) * 7 + automapTopLeftX - 3, (l[4] - startY) * 6 + automapTopLeftY - 3, 0, 0);
                    screen.drawShape(2, automapShapes[l[5] << 2], 231 + xOffset, (tY << 3) + 19, 0, 0);
                    tY += 1;
                }
                int cp2 = screen.curPage;
                screen.curPage = pageNum;
                foreach (var l in defaultLegendData)
                {
                    if (!l.enable) continue;
                    screen.copyBlockAndApplyOverlay(screen.curPage, 235, (tY << 3) + 21, screen.curPage, 235 + xOffset, (tY << 3) + 21, 7, 6, 0, mapOverlay);
                    screen.drawShape(screen.curPage, automapShapes[l.shapeIndex << 2], 232 + xOffset, (tY << 3) + 18 + l.y, 0, 0);
                    printMapText(l.stringId, 244 + xOffset, (tY << 3) + 22);
                    tY += 1;
                }
                screen.setFont(of);
                screen.curPage = cp2;
            }
            printMapExitButtonText();
        }

        public async Task<bool> automapProcessButtons(InputEvent ev)
        {
            int r = -1;
            if (ev.type == "key" && ev.key == "ArrowRight") r = 0;
            else if (ev.type == "key" && ev.key == "ArrowLeft") r = 1;
            else if (ev.type == "mousedown")
            {
                bool within(int x1, int y1, int x2, int y2) => ev.x >= x1 && ev.x <= x2 && ev.y >= y1 && ev.y <= y2;
                if (within(252, 175, 273, 200)) r = 0;
                else if (within(231, 175, 252, 200)) r = 1;
                else if (within(275, 175, 315, 197)) r = 2;
                printMapExitButtonText();
            }
            else return false;
            if (r == 0) { await automapStep(1); printMapExitButtonText(); }
            else if (r == 1) { await automapStep(-1); printMapExitButtonText(); }
            return r == 2;
        }

        public async Task automapStep(int dir)
        {
            int i = currentMapLevel + dir;
            int guard = 0;
            while ((hasTempDataFlags & (1 << (i - 1))) == 0 && guard++ < 64) i = (i + dir) & 0x1f;
            if (i == currentMapLevel || i < 1) return;
            foreach (var l in defaultLegendData) l.enable = false;
            currentMapLevel = i;
            await loadLevelWallData(i, false);
            restoreBlockTempData(i);
            loadMapLegendData(i);
            mapUpdateNeeded = true;
        }

        // Host minimap: draws the explored blocks around the party with the automap's own parchment,
        // wall/door shapes and cursor into an offscreen page. Returns { page, x, y, w, h, palette }.
        public MinimapRegion drawMinimap(int radius = 6)
        {
            const int PAGE_PARCH = 16;
            const int PAGE_OUT = 18;
            if (minimapPalette == null)
            {
                minimapPalette = new byte[768];
                this.screen.loadBitmap(res.get("PARCH.CPS"), PAGE_PARCH, minimapPalette);
                if (automapShapes.Length == 0) automapShapes = Js.Slice(LolShapes.decodeShapeFile(Cps.decodeBitmapData(res.get("AUTOBUT.SHP")).data), 11, 120);
                minimapOverlay = new byte[256];
                this.screen.generateGrayOverlay(minimapPalette, minimapOverlay, 52, 0, 0, 0, 256, false);
            }
            int cells = radius * 2 + 1;
            int w = cells * 7;
            int h = cells * 6;
            int ox = 8;
            int oy = 8;
            var screen = this.screen;
            int cp = screen.curPage;
            var savedOverlay = mapOverlay;
            mapOverlay = minimapOverlay;
            screen.curPage = PAGE_OUT;
            screen.copyRegion(20, 20, ox, oy, w, h, PAGE_PARCH, PAGE_OUT, true);
            int px = currentBlock & 0x1f;
            int py = currentBlock >> 5;
            var A = wllAutomapData;
            for (int dy = -radius; dy <= radius; dy += 1)
            {
                for (int dx = -radius; dx <= radius; dx += 1)
                {
                    int x = px + dx;
                    int y = py + dy;
                    if (x < 0 || x > 31 || y < 0 || y > 31) continue;
                    int bl = (y << 5) + x;
                    var wl = levelBlockProperties[bl].walls;
                    if ((levelBlockProperties[bl].flags & 7) != 7 || ((A[wl[0]] & 0xc0) != 0 && (A[wl[2]] & 0xc0) != 0 && (A[wl[1]] & 0xc0) != 0 && (A[wl[3]] & 0xc0) != 0)) continue;
                    int sx = ox + (dx + radius) * 7;
                    int sy = oy + (dy + radius) * 6;
                    int b0 = calcNewBlockPosition(bl, 0);
                    int b2 = calcNewBlockPosition(bl, 2);
                    int b1 = calcNewBlockPosition(bl, 1);
                    int b3 = calcNewBlockPosition(bl, 3);
                    int w02 = levelBlockProperties[b0].walls[2];
                    int w20 = levelBlockProperties[b2].walls[0];
                    int w13 = levelBlockProperties[b1].walls[3];
                    int w31 = levelBlockProperties[b3].walls[1];
                    screen.copyBlockAndApplyOverlay(PAGE_OUT, sx, sy, PAGE_OUT, sx, sy, 7, 6, 0, mapOverlay);
                    drawMapBlockWall(b3, w31, sx, sy, 3);
                    drawMapShape(w31, sx, sy, 3);
                    if ((A[w31] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(PAGE_OUT, sx, sy, PAGE_OUT, sx, sy, 1, 6, 0, mapOverlay);
                    drawMapBlockWall(b1, w13, sx, sy, 1);
                    drawMapShape(w13, sx, sy, 1);
                    if ((A[w13] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(PAGE_OUT, sx + 6, sy, PAGE_OUT, sx + 6, sy, 1, 6, 0, mapOverlay);
                    drawMapBlockWall(b0, w02, sx, sy, 0);
                    drawMapShape(w02, sx, sy, 0);
                    if ((A[w02] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(PAGE_OUT, sx, sy, PAGE_OUT, sx, sy, 7, 1, 0, mapOverlay);
                    drawMapBlockWall(b2, w20, sx, sy, 2);
                    drawMapShape(w20, sx, sy, 2);
                    if ((A[w20] & 0xc0) != 0) screen.copyBlockAndApplyOverlay(PAGE_OUT, sx, sy + 5, PAGE_OUT, sx, sy + 5, 7, 1, 0, mapOverlay);
                }
            }
            int cx = ox + radius * 7;
            int cy = oy + radius * 6;
            screen.drawShape(PAGE_OUT, automapShapes[48 + currentDirection], cx - 3, cy - 2, 0, 0);
            screen.curPage = cp;
            mapOverlay = savedOverlay;
            return new MinimapRegion { page = PAGE_OUT, x = ox, y = oy, w = w, h = h, palette = minimapPalette };
        }

        public void redrawMapCursor()
        {
            int sx = mapGetStartPosX();
            int sy = mapGetStartPosY();
            if (currentLevel != currentMapLevel) return;
            int cx = automapTopLeftX + ((currentBlock - sx) % 32) * 7;
            int cy = automapTopLeftY + ((currentBlock - (sy << 5)) / 32) * 6;   // Math.trunc
            screen.fillRect(0, 0, 16, 16, 0, 2);
            screen.drawShape(2, automapShapes[48 + currentDirection], 0, 0, 0, 0);
            screen.copyRegion(cx, cy, cx, cy, 16, 16, 2, 0);
            screen.copyBlockAndApplyOverlay(2, 0, 0, 0, cx - 3, cy - 2, 16, 16, 0, mapCursorOverlay);
            mapCursorOverlay[24] = mapCursorOverlay[1];
            for (int i = 1; i < 24; i += 1) mapCursorOverlay[i] = mapCursorOverlay[i + 1];
        }

        public void drawMapBlockWall(int block, int wall, int x, int y, int direction)
        {
            if (((1 << direction) & levelBlockProperties[block].flags) != 0 || (wllAutomapData[wall] & 0x1f) != 13) return;
            int cp = screen.curPage;
            var M = MAP_COORDS;
            screen.copyBlockAndApplyOverlay(cp, x + M[0][direction], y + M[1][direction], cp, x + M[0][direction], y + M[1][direction], M[2][direction], M[3][direction], 0, mapOverlay);
            screen.copyBlockAndApplyOverlay(cp, x + M[4][direction], y + M[5][direction], cp, x + M[4][direction], y + M[5][direction], M[8][direction], M[9][direction], 0, mapOverlay);
            screen.copyBlockAndApplyOverlay(cp, x + M[6][direction], y + M[7][direction], cp, x + M[6][direction], y + M[7][direction], M[8][direction], M[9][direction], 0, mapOverlay);
        }

        public void drawMapShape(int wall, int x, int y, int direction)
        {
            int l = wllAutomapData[wall] & 0x1f;
            if (l == 0x1f) return;
            screen.drawShape(screen.curPage, automapShapes[(l << 2) + direction], x + MAP_COORDS[10][direction] - 2, y + MAP_COORDS[11][direction] - 2, 0, 0);
            mapIncludeLegendData(l);
        }

        public int mapGetStartPos(bool vertical)
        {
            int at(int a_, int c_) => vertical ? (c_ << 5) + a_ : (a_ << 5) + c_;
            int c = 0;
            int a = 32;
            do
            {
                for (a = 0; a < 32; a += 1) if (levelBlockProperties[at(a, c)].flags != 0) break;
                if (a == 32) c += 1;
            } while (c < 32 && a == 32);
            int d = 31;
            a = 32;
            do
            {
                for (a = 0; a < 32; a += 1) if (levelBlockProperties[at(a, d)].flags != 0) break;
                if (a == 32) d -= 1;
            } while (d > 0 && a == 32);
            if (vertical) automapTopLeftY = d > c ? ((32 - (d - c)) >> 1) * 6 + 4 : 4;
            else automapTopLeftX = d > c ? ((32 - (d - c)) >> 1) * 7 + 5 : 5;
            return d > c ? c : 0;
        }

        public int mapGetStartPosX()
        {
            return mapGetStartPos(false);
        }

        public int mapGetStartPosY()
        {
            return mapGetStartPos(true);
        }

        public void mapIncludeLegendData(int type)
        {
            type &= 0x7f;
            var l = defaultLegendData.Find((d) => d.shapeIndex == type);
            if (l != null) l.enable = true;
        }

        public void printMapText(int stringId, int x, int y)
        {
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.printText(getLangString(stringId) ?? "", x, y, 239, 0);
            screen.curPage = cp;
        }

        public void printMapExitButtonText()
        {
            int cp = screen.curPage;
            screen.curPage = 2;
            var of = screen.setFont("9");
            string exit = getLangString(0x4033);
            screen.fprintString(string.IsNullOrEmpty(exit) ? "Exit" : exit, 295, 182, 172, 0, 5);
            screen.setFont(of);
            screen.curPage = cp;
        }
    }
}
