// src/game/scene.mjs (SceneMixin): level loading, block properties, movement, doors and scene drawing
// (scene_lol.cpp / scene_rpg.cpp). JS names are kept.
// (scene.mjs imports DRAWSHP and SCREEN_W from screen.mjs but never uses them.)
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>scene.mjs initScene: openDoorState[] entries</summary>
    public sealed class OpenDoorState
    {
        public int block, wall, state;
    }

    /// <summary>scene.mjs drawScene: hdFrame, the snapshot for the HD renderer</summary>
    public sealed class HdFrame
    {
        public int level;
        public ushort[] tiles;
        public List<DrawShapeRecord> ops;
        public int serial;
        /// <summary>Unity port: the scene window (176x120 at 112,0) as the engine drew it for this frame; the host
        /// shows the HD picture only while the screen still shows this (a scroll, a scene or a spell drawn over the
        /// window means the original is what the player must see)</summary>
        public byte[] window;
    }

    public sealed partial class LandsOfLore
    {
        static readonly int[] VMP_OFFSETS = { 102, 97, 129, 117, 81, 159, 45, 239, 0 };

        // ---- fields (initScene) ----
        public LevelBlock[] levelBlockProperties;
        public byte[] wllVmpMap;
        public sbyte[] wllShapeMap;
        public byte[] wllWallFlags;
        public byte[] specialWallTypes;
        public byte[] wllAutomapData;
        public DecorationProperty[] levelDecorationData;
        /// <summary>JS array written at arbitrary indices (holes read as undefined): see Scene_at / Scene_setAt</summary>
        public List<DecorationProperty> levelDecorationProperties;
        public List<Shape> levelDecorationShapes;
        public List<Shape> levelShapes;
        public int mappedDecorationsCount;
        public int lvlShapeIndex;
        public ushort[] decorationMap1;
        public ushort[] decorationMap2;
        public Shape[] doorShapes;
        public VcnData vcn;
        public ushort[] vmp;
        public int lastSpecialColor;
        public int lastSpecialColorWeight;
        public string lastBlockDataFile;
        public string lastOverridePalFile;
        public byte[] transparencyTable1;
        public byte[] transparencyTable2;
        public ushort[] visibleBlockIndex;
        public LevelBlock[] visibleBlocks;
        public ushort[] blockDrawingBuffer;
        public byte[] sceneWindowBuffer;
        public int sceneDrawPage1;
        public int sceneDrawPage2;
        public int sceneXoffset;
        public int sceneShpDim;
        public short[] lvlShapeLeftRight;
        public short[] lvlShapeTop;
        public short[] lvlShapeBottom;
        public OpenDoorState[] openDoorState;
        public Shape specialGuiShape;
        public int specialGuiShapeX, specialGuiShapeY, specialGuiShapeMirrorFlag;
        public int shpDmX, shpDmY, dmScaleW, dmScaleH;
        public int blockBrightness;
        public int smoothScrollModeNormal;
        public int currentControlMode;
        public bool dialogueField;
        public int specialSceneFlag;
        public int seqWindowX1, seqWindowY1, seqWindowX2, seqWindowY2, seqTrigger;
        public int spsWindowX, spsWindowY, spsWindowW, spsWindowH;
        public double lampStatusTimer;
        public double compassTimer;

        // ---- fields first set outside initScene ----
        public byte[] lvlShpFile;
        public string lvlShpName;
        public int decorationCount;
        public int sceneDrawVarDown, sceneDrawVarRight, sceneDrawVarLeft;
        public int wllProcessFlag;
        /// <summary>monsters drawn in this pass carry it (host health bars)</summary>
        public int sceneSerial;
        /// <summary>host setting (main.mjs: engine.hdRecording)</summary>
        public bool hdRecording;
        public HdFrame hdFrame;
        public int currentFloatingCursor;

        /// <summary>arr[i] of a JS array: undefined (null) past the end or in a hole.</summary>
        static T Scene_at<T>(List<T> a, int i) where T : class => i >= 0 && i < a.Count ? a[i] : null;

        /// <summary>arr[i] = v of a JS array: grows it with holes.</summary>
        static void Scene_setAt<T>(List<T> a, int i, T v) where T : class
        {
            while (a.Count <= i) a.Add(null);
            a[i] = v;
        }

        public void initScene()
        {
            levelBlockProperties = new LevelBlock[1024];
            for (int i = 0; i < 1024; i += 1) levelBlockProperties[i] = new LevelBlock { walls = new byte[4], assignedObjects = 0, drawObjects = 0, direction = 5, flags = 0 };
            wllVmpMap = new byte[256];
            wllShapeMap = new sbyte[256];
            wllWallFlags = new byte[256];
            specialWallTypes = new byte[256];
            wllAutomapData = new byte[256];
            levelDecorationData = new DecorationProperty[0];
            levelDecorationProperties = new List<DecorationProperty>();
            levelDecorationShapes = new List<Shape>();
            levelShapes = new List<Shape>();
            mappedDecorationsCount = 1;
            lvlShapeIndex = 1;
            decorationMap1 = new ushort[2000];
            decorationMap2 = new ushort[560];
            doorShapes = new Shape[] { null, null };
            vcn = null;
            vmp = null;
            lastSpecialColor = 0;
            lastSpecialColorWeight = 0;
            lastBlockDataFile = "";
            lastOverridePalFile = "";
            transparencyTable1 = new byte[256];
            transparencyTable2 = new byte[5120];
            visibleBlockIndex = new ushort[18];
            visibleBlocks = new LevelBlock[18];
            blockDrawingBuffer = new ushort[660];
            sceneWindowBuffer = new byte[176 * 120];
            sceneDrawPage1 = 2;
            sceneDrawPage2 = 6;
            sceneXoffset = 112;
            sceneShpDim = 13;
            lvlShapeLeftRight = new short[36];
            lvlShapeTop = new short[18];
            lvlShapeBottom = new short[18];
            openDoorState = new[] { new OpenDoorState { block = 0, wall = 0, state = 0 }, new OpenDoorState { block = 0, wall = 0, state = 0 }, new OpenDoorState { block = 0, wall = 0, state = 0 } };
            specialGuiShape = null;
            specialGuiShapeX = specialGuiShapeY = specialGuiShapeMirrorFlag = 0;
            shpDmX = shpDmY = dmScaleW = dmScaleH = 0;
            blockBrightness = 0;
            smoothScrollModeNormal = 1;
            currentControlMode = 0;
            dialogueField = false;
            specialSceneFlag = 0;
            seqWindowX1 = seqWindowY1 = seqWindowX2 = seqWindowY2 = seqTrigger = 0;
            spsWindowX = spsWindowY = spsWindowW = spsWindowH = 0;
            lampStatusTimer = 0xffffffff;
            compassTimer = 0;
        }

        // A script that greets the party and only *then* tells an NPC to step out of the way is one-shot:
        // it sets its own "already ran" flag, so it can never issue that order a second time. Leave the
        // level (or save) in the moment between the two and the NPC is stranded in the corridor for good,
        // with no way left in the game to move him. Re-issue the order the script would have given.
        //
        // Each entry is a guarantee the script makes once its flag is up: "no one of mine is still on
        // `block`". Walking them out rather than teleporting them keeps it looking like the script did it.
        // `force` skips the flag test. At level load the flag is the only evidence the script has already
        // had its turn, so it is checked. When the party is actually standing there being refused the way,
        // the flag does not matter: an NPC on that block with nowhere to go is stuck whatever the flag says,
        // and the script's whole purpose is that he is not standing there.
        static readonly (int level, int flag, int block, int dest)[] Scene_strandedGuards =
        {
            // Gladstone Keep gate: block 557's greeting sets flag 110, then sends the guard west off 525.
            (1, 110, 525, 524),
        };

        public void repairStrandedScriptGuards(int index, bool force = false)
        {
            foreach (var g in Scene_strandedGuards)
            {
                if (g.level != index || (!force && queryGameFlag(g.flag) == 0)) continue;
                foreach (var m in monsters)
                {
                    if (m == null || m.hitPoints <= 0 || m.mode != 1 || m.block != g.block) continue;
                    if (m.destX != m.x || m.destY != m.y) continue;  // already walking somewhere: leave him be
                    (m.destX, m.destY) = calcCoordinates(g.dest, 0x80, 0x80);
                }
            }
        }

        // ---- level loading ----
        /// <summary>a level is being loaded (host: no respawn may run on its half-made monster list)</summary>
        public bool levelLoading;

        public async Task loadLevel(int index)
        {
            levelLoading = true;
            try { await loadLevelNow(index); }
            finally { levelLoading = false; }
        }

        async Task loadLevelNow(int index)
        {
            levelMonsterTypes.Clear();
            flagsTable[73] |= 0x08;
            setMouseCursorToIcon(0x85);
            nextScriptFunc = 0;
            snd_stopMusic();
            stopPortraitSpeechAnim();
            levelDecorationShapes = new List<Shape>();
            scriptData = null;
            resetItems(1);
            disableMonsters();
            resetBlockProperties();
            releaseMonsterShapes(0);
            releaseMonsterShapes(1);
            for (int i = 0x50; i < 0x53; i += 1) timerDisable(i);
            currentLevel = index;
            updateFlags = 0;
            setDefaultButtonState();
            await loadTalkFile(index);
            await res.loadPak($"L{index.ToString().PadLeft(2, '0')}.PAK");
            await loadLevelWallData(index, true);
            loadLevelFlag = 1;
            int f = hasTempDataFlags & (1 << (index - 1));
            // A level with no saved state is about to be built from scratch, and its .INI will create its
            // items all over again. Anything still tagged with this level is left from a visit that was
            // thrown away - the Imp's Pit discards one every time it borrows a level - and keeping those
            // records only fills the world's fixed item table until the next level to load cannot allocate.
            // What the party carries is never tagged with a level, so it is untouched by this.
            if (f == 0)
            {
                for (int it = 1; it < itemsInPlay.Length; it += 1)
                {
                    var item = itemsInPlay[it];
                    if (item == null || item.itemPropertyIndex == 0 || item.level != index) continue;
                    deleteItem(it);
                }
            }
            await runInitScript($"LEVEL{index}.INI", f != 0 ? 0 : 1);
            if (f != 0) restoreBlockTempData(index);
            // Unity port: a saved level can hold monsters of another level (the re-entry respawn once ran while a
            // level was still loading and kept the previous level's monsters): their kinds have no pictures here,
            // so they fought unseen. Any living monster of a kind this level never sets up goes.
            if (repairOnLoad && f != 0 && levelMonsterTypes.Count > 0)
                for (int i = 0; i < monsters.Length; i += 1)
                {
                    var m = monsters[i];
                    if (m == null || m.hitPoints <= 0 || m.block == 0 || levelMonsterTypes.Contains(m.type)) continue;
                    placeMonster(m, 0, 0);
                    monsters[i] = makeEmptyMonster(i);
                }
            // Both branches: a level rebuilt from its .INI puts the guard back on his post just as a restore
            // does, and the one-shot script that should move him has already run either way.
            repairStrandedScriptGuards(index);
            keepPodRoomOpen(index);
            await runInfScript($"LEVEL{index}.INF");
            addLevelItems();
            await applyModObjects();
            if (randomizer != null) await applyRandomizer();
            deleteMonstersFromBlock(currentBlock);
            uiCleanPhantomItems(); // leftovers of the old monster-drop bug (JS: if the host defined it; it always is)
            // the level's monster layout as the scripts created it: the host can put it back (Respawn).
            // Only a first visit shows it; on a revisit the saved (possibly cleared) state was restored.
            monsterSpawns = monsterSpawns ?? new Dictionary<int, Monster[]>();
            if (f == 0 && !(monsterSpawns.TryGetValue(index, out var spawned) && spawned != null))
            {
                var list = new Monster[monsters.Length];
                for (int i = 0; i < monsters.Length; i += 1) list[i] = Scene_spreadMonster(monsters[i]);
                monsterSpawns[index] = list;
            }
            screen.generateGrayOverlay(screen.getPalette(0), screen.grayOverlay, 32, 16, 0, 0, 128, true);
            sceneDefaultUpdate = 0;
            if (screen.fadeFlag == 3) await screen.fadeToBlack(10);
            gui_drawPlayField();
            await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
            setMouseCursorToItemInHand();
            snd_playTrack(curMusicTheme);
        }

        /// <summary>JS ({ ...m, equipmentShapes: Array.from(m.equipmentShapes), nextAssignedObject: 0, nextDrawObject: 0 })</summary>
        static Monster Scene_spreadMonster(Monster m)
        {
            var r = m.Spread();
            r.nextAssignedObject = 0;
            r.nextDrawObject = 0;
            r.equipmentShapes = (int[])m.equipmentShapes.Clone();
            return r;
        }

        public async Task loadTalkFile(int index)
        {
            if (index == curTlkFile) return;
            if (curTlkFile > 0 && index > 0) res.unloadPak($"{curTlkFile.ToString().PadLeft(2, '0')}.TLK");
            if (index > 0) curTlkFile = index;
            await loadTalkArchive($"{index.ToString().PadLeft(2, '0')}.TLK"); // JS: if (this.loadTalkArchive), always defined
        }

        public async Task loadLevelWallData(int index, bool mapShapes)
        {
            var file = res.get($"LEVEL{index}.WLL");
            // DataView little-endian reads
            int set = file[0] | (file[1] << 8);
            await loadLevelShpDat(@static.LevelShpList[set], @static.LevelDatList[set], false);
            int count = Js.FloorDiv(file.Length - 2, 12);
            for (int i = 0; i < count; i += 1)
            {
                int d = 2 + i * 12;
                int c = file[d] | (file[d + 1] << 8);
                wllVmpMap[c] = file[d + 2];
                if (mapShapes)
                {
                    int sh = (short)(file[d + 4] | (file[d + 5] << 8));
                    wllShapeMap[c] = (sbyte)(sh > 0 ? assignLevelDecorationShapes(sh) : file[d + 4]);
                }
                specialWallTypes[c] = file[d + 6];
                wllWallFlags[c] = file[d + 8];
                wllAutomapData[c] = file[d + 10];
            }
        }

        public async Task loadLevelShpDat(string shpFile, string datFile, bool flag)
        {
            Js.Fill(decorationMap1, (ushort)0);
            Js.Fill(decorationMap2, (ushort)0);
            // Level packs are named after the SHP file, except YVEL1.SHP which ships in YVEL.PAK.
            string pak = Regex.Replace(Regex.Replace(shpFile, @"\.SHP$", ".PAK", RegexOptions.IgnoreCase), @"^YVEL1\.PAK$", "YVEL.PAK");
            if (!res.exists(shpFile)) await res.loadPak(pak);
            lvlShpFile = res.get(shpFile);
            lvlShpName = shpFile;
            decorationCount = lvlShpFile[0] | (lvlShpFile[1] << 8);
            levelDecorationData = LolShapes.decodeDecorationData(res.get(datFile));
            if (!flag)
            {
                mappedDecorationsCount = 1;
                lvlShapeIndex = 1;
            }
        }

        public int assignLevelDecorationShapes(int index)
        {
            int r = decorationMap2[index];
            if (r != 0) return r;
            int o = mappedDecorationsCount++;
            var src = levelDecorationData[index];
            var prop = new DecorationProperty { shapeIndex = (ushort[])src.shapeIndex.Clone(), scaleFlag = (byte[])src.scaleFlag.Clone(), shapeX = (short[])src.shapeX.Clone(), shapeY = (short[])src.shapeY.Clone(), next = src.next, flags = src.flags };
            Scene_setAt(levelDecorationProperties, o, prop);
            for (int i = 0; i < 10; i += 1)
            {
                int t = prop.shapeIndex[i];
                if (t == 0xffff) continue;
                int pv = decorationMap1[t];
                if (pv != 0) prop.shapeIndex[i] = (ushort)pv;
                else
                {
                    Scene_setAt(levelDecorationShapes, lvlShapeIndex, getLevelDecorationShapes(t));
                    decorationMap1[t] = (ushort)lvlShapeIndex;
                    prop.shapeIndex[i] = (ushort)lvlShapeIndex++;
                }
            }
            decorationMap2[index] = (ushort)o;
            if (prop.next != 0) prop.next = assignLevelDecorationShapes(prop.next);
            return o;
        }

        public Shape getLevelDecorationShapes(int shapeIndex)
        {
            if (decorationCount <= shapeIndex) return null;
            var f = lvlShpFile;
            int offs = (f[shapeIndex * 4 + 2] | (f[shapeIndex * 4 + 3] << 8) | (f[shapeIndex * 4 + 4] << 16) | (f[shapeIndex * 4 + 5] << 24)) + 2;
            var shape = LolShapes.decodeShape(f, offs);
            shape.key = $"{lvlShpName}:{shapeIndex}";
            return shape;
        }

        public void loadBlockProperties(string cmzFile)
        {
            lastCmzFile = cmzFile;
            var data = Cps.decodeBitmapData(res.get(cmzFile)).data;
            int len = data[4] | (data[5] << 8);
            for (int i = 0; i < 1024; i += 1)
            {
                var l = levelBlockProperties[i];
                Js.Set(l.walls, Js.Slice(data, 6 + i * len, 6 + i * len + 4));
                l.assignedObjects = 0;
                l.drawObjects = 0;
                l.flags = 0;
                l.direction = 5;
                if (wllAutomapData[l.walls[0]] == 17)
                {
                    l.flags &= 0xef;
                    l.flags |= 0x20;
                }
            }
            applyModWalls();
        }

        public async Task loadLevelGraphics(string file, int specialColor, int weight, int vcnLen, int vmpLen, string palFile)
        {
            if (!string.IsNullOrEmpty(file))
            {
                lastSpecialColor = specialColor;
                lastSpecialColorWeight = weight;
                lastBlockDataFile = file;
                lastOverridePalFile = palFile ?? "";
            }
            if (!res.exists($"{lastBlockDataFile}.VCN")) await res.loadPak($"{Regex.Replace(lastBlockDataFile, "^YVEL1$", "YVEL")}.PAK");
            vcn = LolSceneFile.decodeVcn(res.get($"{lastBlockDataFile}.VCN"));
            var pal0 = screen.getPalette(0);
            if (!string.IsNullOrEmpty(lastOverridePalFile)) Js.Set(pal0, Js.Slice(res.get(lastOverridePalFile), 0, 384));
            else Js.Set(pal0, Js.Slice(vcn.rawPalette, 0, 384));
            vmp = LolSceneFile.decodeVmp(res.get($"{lastBlockDataFile}.VMP"));
            if (currentLevel == 11)
            { // the swamp keeps its ice palette in slot 2 (LoLEngine::loadLevelGraphics)
                var pal2 = screen.getPalette(2);
                Js.Set(pal2, Js.Slice(loadPaletteFile("SWAMPICE.COL"), 0, 768));
                Js.Set(pal2, Js.Slice(pal0, 128 * 3), 128 * 3);
                if ((flagsTable[52] & 0x04) != 0) for (int i = 1; i < 768; i += 1) { byte t = pal0[i]; pal0[i] = pal2[i]; pal2[i] = t; }
            }

            var tpal = pal0;
            for (int i = 0; i < 7; i += 1)
            {
                int w = 100 - i * lastSpecialColorWeight;
                w = w > 0 ? Js.FloorDiv(w * 255, 100) : 0;
                var overlay = screen.levelOverlays[i];
                screen.generateOverlay(tpal, overlay, lastSpecialColor, w);
                for (int ii = 0; ii < 128; ii += 1) if (overlay[ii] == 255) overlay[ii] = 0;
                for (int ii = 128; ii < 256; ii += 1) overlay[ii] = (byte)ii;
            }
            for (int i = 0; i < 256; i += 1) screen.levelOverlays[7][i] = (byte)i;
            generateBrightnessPalette(pal0, screen.getPalette(1), brightness, lampEffect);
            var tlc = res.get($"LEVEL{currentLevel.ToString().PadLeft(2, '0')}.TLC");
            Js.Set(transparencyTable1, Js.Slice(tlc, 0, 256));
            Js.Set(transparencyTable2, Js.Slice(tlc, 256, 256 + 5120));
        }

        public void generateBrightnessPalette(byte[] src, byte[] dst, int brightness, int modifier)
        {
            Js.Set(dst, src);
            screen.loadSpecialColors(dst);
            brightness = (8 - brightness) << 5;
            if (modifier >= 0 && modifier < 8 && (flagsTable[31] & 0x08) != 0)
            {
                brightness = 256 - ((((modifier & 0xfffe) << 5) * (256 - brightness)) >> 8);
                if (brightness < 0) brightness = 0;
            }
            for (int i = 0; i < 384; i += 1) dst[i] = (byte)(((dst[i] * brightness) >> 8) & 0xff);
        }

        public async Task setPaletteBrightness(byte[] srcPal, int brightness, int modifier)
        {
            generateBrightnessPalette(srcPal, screen.getPalette(1), brightness, modifier);
            await screen.fadePalette(screen.getPalette(1), 5);
            screen.fadeFlag = 0;
        }

        public void resetItems(int flag)
        {
            for (int i = 0; i < 1024; i += 1)
            {
                levelBlockProperties[i].direction = 5;
                int id = levelBlockProperties[i].assignedObjects;
                BlockObject r = null;
                while ((id & 0x8000) != 0)
                {
                    r = findObject(id);
                    id = r.nextAssignedObject;
                }
                if (id == 0) continue;
                var it = itemsInPlay[id];
                it.level = currentLevel;
                it.block = i;
                if (r != null) r.nextAssignedObject = 0;
            }
            if (flag != 0) for (int i = 0; i < 8; i += 1) flyingObjects[i] = makeFlyingObject();
        }

        public void resetBlockProperties()
        {
            for (int i = 0; i < 1024; i += 1)
            {
                var l = levelBlockProperties[i];
                if ((l.flags & 0x10) != 0)
                {
                    l.flags &= 0xef;
                    if (testWallInvisibility(i, 0) && testWallInvisibility(i, 1)) l.flags |= 0x40;
                }
                else if ((l.flags & 0x40) != 0) l.flags &= 0xbf;
                else if ((l.flags & 0x80) != 0) l.flags &= 0x7f;
            }
        }

        public bool testWallFlag(int block, int direction, int flag)
        {
            var l = levelBlockProperties[block];
            if ((l.flags & 0x10) != 0) return true;
            if (direction != -1) return (wllWallFlags[l.walls[direction ^ 2]] & flag) != 0;
            for (int i = 0; i < 4; i += 1) if ((wllWallFlags[l.walls[i]] & flag) != 0) return true;
            return false;
        }

        public bool testWallInvisibility(int block, int direction)
        {
            int w = levelBlockProperties[block].walls[direction];
            return !(wllVmpMap[w] != 0 || wllShapeMap[w] != 0 || (levelBlockProperties[block].flags & 0x80) != 0);
        }

        public void setWallType(int block, int wall, int val)
        {
            var l = levelBlockProperties[block];
            if (wall == -1)
            {
                Js.Fill(l.walls, (byte)val);
                if (wllAutomapData[val] == 17)
                {
                    l.flags &= 0xef;
                    l.flags |= 0x20;
                }
                else l.flags &= 0xdf;
            }
            else l.walls[wall] = (byte)val;
        }

        // ---- coordinates ----
        public int calcNewBlockPosition(int block, int direction)
        {
            int[] offsets = { -32, 1, 32, -1 };
            return (block + offsets[direction]) & 0x3ff;
        }

        public int calcBlockIndex(int x, int y)
        {
            return (((y & 0xff00) >> 3) | (x >> 8)) & 0x3ff;
        }

        public (int, int) calcCoordinates(int block, int xOffs, int yOffs)
        {
            return (((block & 0x1f) << 8) | xOffs, ((block & 0xffe0) << 3) | yOffs);
        }

        public (int, int) calcCoordinatesAddDirectionOffset(int x, int y, int direction)
        {
            if (direction == 0) return (x, y);
            int tx = x;
            int ty = y;
            if ((direction & 1) != 0) (tx, ty) = (ty, tx);
            if (direction != 1) ty = (ty - 256) * -1;
            if (direction != 3) tx = (tx - 256) * -1;
            return (tx & 0xffff, ty & 0xffff);
        }

        public (int, int) calcCoordinatesForSingleCharacter(int charNum)
        {
            int[] xOffsets = { 0x80, 0x00, 0x00, 0x40, 0xc0, 0x00, 0x40, 0x80, 0xc0 };
            int c = countActiveCharacters();
            if (c == 0) return (0, 0);
            c = (c - 1) * 3 + charNum;
            var (x, y) = calcCoordinatesAddDirectionOffset(xOffsets[c], 0x80, currentDirection);
            x |= partyPosX & 0xff00;
            y |= partyPosY & 0xff00;
            return (x, y);
        }

        public (int, int) calcSpriteRelPosition(int x1, int y1, int x2, int y2, int direction)
        {
            int a = x2 - x1;
            int b = y1 - y2;
            if (direction != 0)
            {
                if (direction != 2) (a, b) = (b, a);
                if (direction != 3)
                {
                    a = -a;
                    if (direction != 1) b = -b;
                }
                else b = -b;
            }
            return (a, b);
        }

        public bool checkBlockPassability(int block, int direction)
        {
            if (testWallFlag(block, direction, 1)) return false;
            int d = levelBlockProperties[block].assignedObjects;
            while (d != 0)
            {
                if ((d & 0x8000) != 0) return false;
                d = findObject(d).nextAssignedObject;
            }
            return true;
        }

        public async Task notifyBlockNotPassable(int scrollFlag)
        {
            if (scrollFlag != 0) await movePartySmoothScrollBlocked(2);
            snd_stopSpeech(true);
            txt.printMessage(0x8002, getLangString(0x403f));
            snd_playSoundEffect(19, -1);
        }

        // ---- party movement ----
        public async Task moveParty(int direction, int unk1, int unk2, int buttonShape)
        {
            onCall?.Invoke("moveParty", new object[] { direction, unk1, unk2, buttonShape });   // the host's wrapper (main.mjs wrapCount)
            if (buttonShape != 0) gui_toggleButtonDisplayMode(buttonShape, 1);
            int opos = currentBlock;
            int npos = calcNewBlockPosition(currentBlock, direction);
            if (!checkBlockPassability(npos, direction))
            {
                // The way is shut. If what shuts it is an NPC whose one-shot script can no longer order him
                // aside, re-issue that order now rather than only at level load: whichever path put him back
                // on his post, this is the moment it matters, and he walks off within a second.
                repairStrandedScriptGuards(currentLevel, true);
                await notifyBlockNotPassable(unk2 != 0 ? 0 : 1);
                gui_toggleButtonDisplayMode(buttonShape, 0);
                return;
            }
            scriptDirection = direction;
            currentBlock = npos;
            sceneDefaultUpdate = 1;
            (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
            flagsTable[73] &= 0xfd;
            await runLevelScript(opos, 4);
            await runLevelScript(npos, 1);
            if ((flagsTable[73] & 0x02) == 0)
            {
                initTextFading(2, 0);
                if (sceneDefaultUpdate != 0)
                {
                    if (unk2 == 0) await movePartySmoothScrollUp(2);
                    else if (unk2 == 1) await movePartySmoothScrollDown(2);
                    else if (unk2 == 2) await movePartySmoothScrollLeft(1);
                    else if (unk2 == 3) await movePartySmoothScrollRight(1);
                }
                else gui_drawScene(0);
                gui_toggleButtonDisplayMode(buttonShape, 0);
                if (npos == currentBlock)
                {
                    await runLevelScript(opos, 8);
                    await runLevelScript(npos, 2);
                    if (levelBlockProperties[npos].walls[0] == 0x1a) Js.Fill(levelBlockProperties[npos].walls, (byte)0);
                }
            }
            updateAutoMap(currentBlock);
            if (dungeon != null) await uiDungeonStepped(currentBlock);   // the pit's traps and its way down
        }

        // ---- doors (KyraRpgEngine) ----
        public void processDoorSwitch(int block, int openClose)
        {
            if (block == currentBlock) return;
            if ((levelBlockProperties[block].assignedObjects & 0x8000) != 0) return;
            if (openClose == 0)
            {
                foreach (var s in openDoorState)
                {
                    if (s.block != block) continue;
                    openClose = -s.state;
                    break;
                }
            }
            if (openClose == 0)
            {
                var walls = levelBlockProperties[block].walls;
                openClose = (wllWallFlags[walls[(wllWallFlags[walls[0]] & 8) != 0 ? 0 : 1]] & 1) != 0 ? 1 : -1;
            }
            openCloseDoor(block, openClose);
        }

        public void openCloseDoor(int block, int openClose)
        {
            var walls = levelBlockProperties[block].walls;
            int c = (wllWallFlags[walls[0]] & 8) != 0 ? 0 : 1;
            int v = walls[c];
            int flg = openClose == 1 ? 0x10 : openClose == -1 ? 0x20 : 0;
            if ((wllWallFlags[v] & flg) != 0) return;
            int s1 = -1;
            int s2 = -1;
            for (int i = 0; i < 3; i += 1)
            {
                if (openDoorState[i].block == block) { s1 = i; break; }
                if (openDoorState[i].block == 0 && s2 == -1) s2 = i;
            }
            if (s1 != -1 || s2 != -1)
            {
                if (s1 == -1) s1 = s2;
                openDoorState[s1].block = block;
                openDoorState[s1].state = openClose;
                openDoorState[s1].wall = c;
                flg = -openClose == 1 ? 0x10 : -openClose == -1 ? 0x20 : 0;
                if ((wllWallFlags[v] & flg) != 0)
                {
                    walls[c] = (byte)(walls[c] + openClose);
                    walls[c ^ 2] = (byte)(walls[c ^ 2] + openClose);
                    int snd = openClose == -1 ? 4 : 3;
                    snd_processEnvironmentalSoundEffect(snd + 28, currentBlock);
                    if (!checkSceneUpdateNeed(block)) snd_updateEnvironmentalSfx(0);
                }
                timerEnable(0);
            }
            else
            {
                while ((flg & wllWallFlags[v]) == 0) v += openClose;
                walls[c] = walls[c ^ 2] = (byte)v;
                checkSceneUpdateNeed(block);
            }
        }

        public void completeDoorOperations()
        {
            foreach (var s in openDoorState)
            {
                if (s.block == 0) continue;
                var walls = levelBlockProperties[s.block].walls;
                do
                {
                    walls[s.wall] = (byte)(walls[s.wall] + s.state);
                    walls[s.wall ^ 2] = (byte)(walls[s.wall ^ 2] + s.state);
                } while ((wllWallFlags[walls[s.wall]] & 0x30) == 0);
                s.block = 0;
            }
        }

        public void timerProcessDoors()
        {
            foreach (var s in openDoorState)
            {
                if (s.block == 0) continue;
                var walls = levelBlockProperties[s.block].walls;
                walls[s.wall] = (byte)(walls[s.wall] + s.state);
                walls[s.wall ^ 2] = (byte)(walls[s.wall ^ 2] + s.state);
                int snd = 3;
                int flg = wllWallFlags[walls[s.wall]];
                if ((flg & 0x20) != 0) snd = 5;
                else if (s.state == -1) snd = 4;
                if ((updateFlags & 1) == 0)
                {
                    snd_processEnvironmentalSoundEffect(snd + 28, s.block);
                    if (!checkSceneUpdateNeed(s.block)) snd_updateEnvironmentalSfx(0);
                }
                if ((flg & 0x30) != 0) s.block = 0;
            }
        }

        public bool checkSceneUpdateNeed(int block)
        {
            if (sceneUpdateRequired) return true;
            for (int i = 0; i < 15; i += 1)
            {
                if (visibleBlockIndex[i] == block)
                {
                    sceneUpdateRequired = true;
                    return true;
                }
            }
            if (currentBlock == block) sceneUpdateRequired = true;
            return sceneUpdateRequired;
        }

        // ---- clicked walls ----
        public bool clickedShape(int shapeIndex)
        {
            if (clickedSpecialFlag != 0x40) return true;
            for (; shapeIndex > 0; shapeIndex = levelDecorationProperties[shapeIndex].next)
            {
                var prop = Scene_at(levelDecorationProperties, shapeIndex);
                if (prop == null) break;
                int s = prop.shapeIndex[1];
                if (s == 0xffff) continue;
                var shape = levelDecorationShapes[s];
                int w = shape.width;
                int h = shape.height;
                int x = prop.shapeX[1] + clickedShapeXOffs;
                int y = prop.shapeY[1] + clickedShapeYOffs;
                if ((prop.flags & 1) != 0) w <<= 1;
                if (mouseX >= x - 4 && mouseX < x + w + 8 && mouseY >= y - 4 && mouseY < y + h + 8) return true;
            }
            return false;
        }

        public async Task<int> clickedWallShape(int block, int direction)
        {
            int v = wllShapeMap[levelBlockProperties[block].walls[direction]];
            if (!clickedShape(v)) return 0;
            snd_playSoundEffect(0x69, -1);
            await runLevelScript(block, 0x40);
            return 1;
        }

        public async Task<int> clickedLeverOn(int block, int direction)
        {
            int v = wllShapeMap[levelBlockProperties[block].walls[direction]];
            if (!clickedShape(v)) return 0;
            levelBlockProperties[block].walls[direction] += 1;
            levelBlockProperties[block].walls[direction ^ 2] += 1;
            snd_playSoundEffect(0x1c, -1);
            await runLevelScript(block, clickedSpecialFlag);
            return 1;
        }

        public async Task<int> clickedLeverOff(int block, int direction)
        {
            int v = wllShapeMap[levelBlockProperties[block].walls[direction]];
            if (!clickedShape(v)) return 0;
            levelBlockProperties[block].walls[direction] -= 1;
            levelBlockProperties[block].walls[direction ^ 2] -= 1;
            snd_playSoundEffect(0x1d, -1);
            await runLevelScript(block, clickedSpecialFlag);
            return 1;
        }

        public async Task<int> clickedWallOnlyScript(int block)
        {
            await runLevelScript(block, clickedSpecialFlag);
            return 1;
        }

        public async Task<int> clickedDoorSwitch(int block, int direction)
        {
            int v = wllShapeMap[levelBlockProperties[block].walls[direction]];
            if (!clickedShape(v)) return 0;
            snd_playSoundEffect(78, -1);
            blockDoor = 0;
            await runLevelScript(block, 0x40);
            if (blockDoor == 0)
            {
                await delay(15 * tickLength, true);
                processDoorSwitch(block, 0);
            }
            return 1;
        }

        public async Task<int> clickedNiche(int block, int direction)
        {
            int v = wllShapeMap[levelBlockProperties[block].walls[direction]];
            if (!clickedShape(v) || itemInHand == 0) return 0;
            var (x, y) = calcCoordinatesAddDirectionOffset(0x80, 0xff, currentDirection);
            (x, y) = calcCoordinates(block, x, y);
            _ = setItemPosition(itemInHand, x, y, 8, 1);
            _ = setHandItem(0);
            return 1;
        }

        // ---- scene drawing ----
        public void gui_drawScene(int pageNum)
        {
            if ((updateFlags & 1) == 0 && weaponsDisabled == false && partyAwake && vcn != null) drawScene(pageNum);
        }

        public void drawScene(int pageNum)
        {
            if (pageNum != 0 && pageNum != sceneDrawPage1)
            {
                (sceneDrawPage1, sceneDrawPage2) = (sceneDrawPage2, sceneDrawPage1);
                updateDrawPage2();
            }
            sceneSerial = sceneSerial + 1; // monsters drawn in this pass carry it (host health bars)  (JS: (this.sceneSerial || 0) + 1)
            generateBlockDrawingBuffer();
            drawVcnBlocks();
            if (hdRecording) screen.recorder = new List<DrawShapeRecord>();
            drawSceneShapes();
            if (hdRecording)
            {
                // Snapshot for the HD renderer: the 22x15 tile grid (base + overlay) and the shape draws in order.
                hdFrame = new HdFrame { level = currentLevel, tiles = Js.Slice(blockDrawingBuffer, 0, 660), ops = screen.recorder, serial = (hdFrame != null ? hdFrame.serial : 0) + 1 };
                var drawn = screen.page(sceneDrawPage1);
                hdFrame.window = new byte[176 * 120];
                for (int y = 0; y < 120; y += 1) Buffer.BlockCopy(drawn, y * 320 + 112, hdFrame.window, y * 176, 176);
                screen.recorder = null;
            }
            if (pageNum == 0)
            {
                drawSpecialGuiShape(sceneDrawPage1);
                screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage1, sceneDrawPage2, true);
                screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage1, 0, true);
                (sceneDrawPage1, sceneDrawPage2) = (sceneDrawPage2, sceneDrawPage1);
            }
            snd_updateEnvironmentalSfx(0);
            gui_drawCompass();
            sceneUpdateRequired = false;
        }

        public void updateDrawPage2()
        {
            screen.copyRegion(112, 0, 112, 0, 176, 120, 0, sceneDrawPage2, true);
        }

        public void drawSpecialGuiShape(int pageNum)
        {
            if (specialGuiShape == null) return;
            screen.drawShape(pageNum, specialGuiShape, specialGuiShapeX, specialGuiShapeY, 2, 0);
            if ((specialGuiShapeMirrorFlag & 1) != 0)
            {
                screen.drawShape(pageNum, specialGuiShape, specialGuiShapeX + specialGuiShape.width, specialGuiShapeY, 2, 1);
            }
        }

        public void assignVisibleBlocks(int block, int direction)
        {
            for (int i = 0; i < 18; i += 1)
            {
                int t = (block + dscBlockIndex[direction * 18 + i]) & 0x3ff;
                visibleBlockIndex[i] = (ushort)t;
                visibleBlocks[i] = levelBlockProperties[t];
                lvlShapeLeftRight[i << 1] = lvlShapeLeftRight[(i << 1) + 1] = -1;
            }
        }

        public void generateBlockDrawingBuffer()
        {
            var bm = @static.DscBlockMap;
            sceneDrawVarDown = bm[currentDirection];
            sceneDrawVarRight = bm[currentDirection + 4];
            sceneDrawVarLeft = bm[currentDirection + 8];
            Js.Fill(blockDrawingBuffer, (ushort)0);
            wllProcessFlag = ((currentBlock >> 5) + (currentBlock & 0x1f) + currentDirection) & 1;
            placeTiles(0, 15, 1, -330, 22, 15, wllProcessFlag != 0);
            assignVisibleBlocks(currentBlock, currentDirection);
            int down = sceneDrawVarDown;
            int right = sceneDrawVarRight;
            int left = sceneDrawVarLeft;
            int wall(int block, int side) => visibleBlocks[block].walls[side];
            var flags = wllWallFlags;
            bool hasWall(int v) => v != 0 && (flags[v] & 8) == 0;
            void T(int x, int y, int w, int o, int ww, int h, bool flip = false) => placeTiles(x, y, w, o, ww, h, flip);

            int a = wall(0, right);
            if (a != 0) T(-2, 3, a, VMP_OFFSETS[0], 3, 5);
            a = wall(6, left);
            if (a != 0) T(21, 3, a, VMP_OFFSETS[0], 3, 5, true);
            a = wall(1, right);
            int b = wall(2, down);
            if (hasWall(a) && (flags[b] & 8) == 0) T(2, 3, a, VMP_OFFSETS[0], 3, 5);
            else if (a != 0 && (flags[b] & 8) != 0) T(2, 3, b, VMP_OFFSETS[0], 3, 5);
            a = wall(5, left);
            b = wall(4, down);
            if (hasWall(a) && (flags[b] & 8) == 0) T(17, 3, a, VMP_OFFSETS[0], 3, 5, true);
            else if (a != 0 && (flags[b] & 8) != 0) T(17, 3, b, VMP_OFFSETS[0], 3, 5, true);
            a = wall(2, right);
            if (a != 0) T(8, 3, a, VMP_OFFSETS[1], 1, 5);
            a = wall(4, left);
            if (a != 0) T(13, 3, a, VMP_OFFSETS[1], 1, 5, true);
            foreach (var (block, x) in new[] { (1, -4), (5, 20), (2, 2), (4, 14), (3, 8) })
            {
                a = wall(block, down);
                if ((block == 3 && a != 0) || (block != 3 && hasWall(a))) T(x, 3, a, VMP_OFFSETS[2], 6, 5);
            }
            a = wall(7, right);
            if (a != 0) T(0, 3, a, VMP_OFFSETS[3], 2, 6);
            a = wall(11, left);
            if (a != 0) T(20, 3, a, VMP_OFFSETS[3], 2, 6, true);
            a = wall(8, right);
            if (a != 0) T(6, 2, a, VMP_OFFSETS[4], 2, 8);
            a = wall(10, left);
            if (a != 0) T(14, 2, a, VMP_OFFSETS[4], 2, 8, true);
            foreach (var (block, x) in new[] { (8, -4), (10, 16), (9, 6) })
            {
                a = wall(block, down);
                if ((block == 9 && a != 0) || (block != 9 && hasWall(a))) T(x, 2, a, VMP_OFFSETS[5], 10, 8);
            }
            a = wall(12, right);
            if (a != 0) T(3, 1, a, VMP_OFFSETS[6], 3, 12);
            a = wall(14, left);
            if (a != 0) T(16, 1, a, VMP_OFFSETS[6], 3, 12, true);
            foreach (var (block, x) in new[] { (12, -13), (14, 19), (13, 3) })
            {
                a = wall(block, down);
                if ((block == 13 && a != 0) || (block != 13 && (flags[a] & 8) == 0)) T(x, 1, a, VMP_OFFSETS[7], 16, 12);
            }
            a = wall(15, right);
            b = wall(17, left);
            if (a != 0) T(0, 0, a, VMP_OFFSETS[8], 3, 15);
            if (b != 0) T(19, 0, b, VMP_OFFSETS[8], 3, 15, true);
        }

        public void placeTiles(int startX, int startY, int wall, int offset, int width, int height, bool flip = false)
        {
            int mapIndex = wllVmpMap[wall];
            if (mapIndex == 0) return;
            int sourceStart = (mapIndex - 1) * 431 + offset + 330;
            var drawing = blockDrawingBuffer;
            for (int y = 0; y < height; y += 1)
            {
                for (int x = 0; x < width; x += 1)
                {
                    int targetX = startX + x;
                    int sourceX = flip ? width - 1 - x : x;
                    int si = sourceStart + y * width + sourceX;
                    int tile = si >= 0 && si < vmp.Length ? vmp[si] : 0; // past the end: undefined
                    if (targetX < 0 || targetX >= 22 || tile == 0) continue;
                    if (flip) tile ^= 0x4000;
                    drawing[(startY + y) * 22 + targetX] = (ushort)tile;
                }
            }
        }

        public void drawVcnBlocks()
        {
            var @out = sceneWindowBuffer;
            var drawing = blockDrawingBuffer;
            var vcn = this.vcn;
            for (int cell = 0; cell < 330; cell += 1)
            {
                int tile = drawing[cell];
                int overlay = 0;
                if ((tile & 0x8000) != 0)
                {
                    overlay = tile & 0x7fff;
                    tile = 0;
                }
                if (tile == 0) tile = drawing[cell + 330];
                drawVcnTile(@out, cell, tile, false);
                if (overlay != 0) drawVcnTile(@out, cell, overlay, true);
            }
            screen.copyBlockToPage(sceneDrawPage1, sceneXoffset, 0, 176, 120, @out);
        }

        public void drawVcnTile(byte[] output, int cell, int encodedTile, bool transparent)
        {
            bool flipped = (encodedTile & 0x4000) != 0;
            int tile = encodedTile & 0x3fff;
            var vcn = this.vcn;
            if (tile >= vcn.tileCount) return;
            int source = tile * 32;
            int targetX = (cell % 22) * 8;
            int targetY = Js.FloorDiv(cell, 22) * 8;
            int shift = vcn.shifts[tile];
            for (int y = 0; y < 8; y += 1)
            {
                for (int x = 0; x < 8; x += 1)
                {
                    int sourceX = flipped ? 7 - x : x;
                    int packed = vcn.tiles[source + y * 4 + (sourceX >> 1)];
                    int nibble = (sourceX & 1) != 0 ? packed & 0x0f : packed >> 4;
                    byte color = vcn.colorTable[nibble | shift];
                    if (!transparent || color != 0) output[(targetY + y) * 176 + targetX + x] = color;
                }
            }
        }

        public (int, int) setLevelShapesDim(int index, int dim)
        {
            int x1;
            int x2;
            if (lvlShapeLeftRight[index << 1] == -1)
            {
                x1 = 0;
                x2 = 22;
                int y1 = 0;
                int y2 = 120;
                int m = index * 18;
                var dim1 = @static.DscDimData1;
                var dim2 = @static.DscDimData2;
                for (int i = 0; i < 18; i += 1)
                {
                    int d = visibleBlocks[i].walls[sceneDrawVarDown];
                    int a = wllWallFlags[d];
                    if ((a & 8) != 0)
                    {
                        int t = dim2[(m + i) << 1];
                        if (t > x1)
                        {
                            x1 = t;
                            if ((a & 0x10) == 0) (y1, y2) = setDoorShapeDim(index, -1);
                        }
                        t = dim2[((m + i) << 1) + 1];
                        if (t < x2)
                        {
                            x2 = t;
                            if ((a & 0x10) == 0) (y1, y2) = setDoorShapeDim(index, -1);
                        }
                    }
                    else
                    {
                        int t = (sbyte)dim1[m + i];
                        if (wllVmpMap[d] == 0 || t == -40) continue;
                        if (t == -41) { x1 = 22; x2 = 0; break; }
                        if (t > 0 && x2 > t) x2 = t;
                        if (t < 0 && x1 < -t) x1 = -t;
                    }
                    if (x2 < x1) break;
                }
                x1 += sceneXoffset >> 3;
                x2 += sceneXoffset >> 3;
                lvlShapeTop[index] = (short)y1;
                lvlShapeBottom[index] = (short)y2;
                lvlShapeLeftRight[index << 1] = (short)x1;
                lvlShapeLeftRight[(index << 1) + 1] = (short)x2;
            }
            else
            {
                x1 = lvlShapeLeftRight[index << 1];
                x2 = lvlShapeLeftRight[(index << 1) + 1];
            }
            screen.modifyScreenDim(dim, x1, 0, x2 - x1, 120);
            return (x1, x2);
        }

        public (int, int) setDoorShapeDim(int index, int dim)
        {
            int a = @static.DscDimMap[index];
            if (dim == -1 && a != 3) a += 1;
            int y1 = @static.DscDoorFrameY1[a];
            int y2 = @static.DscDoorFrameY2[a];
            if (dim != -1)
            {
                var cDim = screen.getScreenDim(dim);
                screen.modifyScreenDim(dim, cDim.sx, y1, cDim.w, y2 - y1);
            }
            return (y1, y2);
        }

        public void drawSceneShapes()
        {
            for (int i = 0; i < 18; i += 1)
            {
                int t = @static.DscTileIndex[i];
                int s = visibleBlocks[t].walls[sceneDrawVarDown];
                var (x1, x2) = setLevelShapesDim(t, sceneShpDim);
                if (x2 <= x1) continue;
                drawDecorations(t);
                int w = wllWallFlags[s];
                if (t == 16) w |= 0x80;
                drawBlockEffects(t, 0);
                if (visibleBlocks[t].assignedObjects != 0 && (w & 0x80) != 0) drawBlockObjects(t);
                drawBlockEffects(t, 1);
                if ((w & 8) == 0) continue;
                int v = 20 * (s - (s < 23 ? @static.DscDoorScaleOffs[s] : 0));
                if (v > 80) v = 80;
                setDoorShapeDim(t, sceneShpDim);
                drawDoor(doorShapes[s < 23 ? @static.DscDoorShapeIndex[s] : 0], null, t, 10, 0, -v, 2);
                setLevelShapesDim(t, sceneShpDim);
            }
        }

        public void drawDecorations(int index)
        {
            var S = @static;
            for (int i = 1; i >= 0; i -= 1)
            {
                int s = index * 2 + i;
                int scaleW = S.DscScaleWidthData[s];
                int scaleH = S.DscScaleHeightData[s];
                int ix = (sbyte)S.DscShapeIndex[s];
                int shpIx = Math.Abs(ix);
                int ovlIndex = S.DscOvlIndex[4 + S.DscDimMap[index] * 5] + 2;
                if (ovlIndex > 7) ovlIndex = 7;
                if (scaleW == 0 || scaleH == 0) continue;
                int d = (currentDirection + (sbyte)S.DscWalls[s]) & 3;
                int l = wllShapeMap[visibleBlocks[index].walls[d]];
                while (l > 0)
                {
                    var prop = levelDecorationProperties[l];
                    if ((prop.flags & 8) != 0 && index != 3 && index != 9 && index != 13)
                    {
                        l = prop.next;
                        continue;
                    }
                    if (S.DscOvlMap[shpIx] == 1 && ((prop.flags & 2) != 0 || ((prop.flags & 4) != 0 && wllProcessFlag != 0))) ix = -ix;
                    int xOffs = 0;
                    int yOffs = 0;
                    byte[] ovl = null;
                    if ((prop.scaleFlag[shpIx] & 1) != 0)
                    {
                        xOffs = prop.shapeX[shpIx];
                        yOffs = prop.shapeY[shpIx];
                        shpIx = S.DscOvlMap[shpIx];
                        ovl = screen.levelOverlays[ovlIndex];
                    }
                    else if (prop.shapeIndex[shpIx] != 0xffff)
                    {
                        scaleW = scaleH = 0x100;
                        ovl = screen.levelOverlays[7];
                    }
                    if (prop.shapeIndex[shpIx] != 0xffff)
                    {
                        var shape = Scene_at(levelDecorationShapes, prop.shapeIndex[shpIx]);
                        if (shape != null)
                        {
                            int dscX = (short)S.DscX[s];
                            int x;
                            int flags;
                            if (ix < 0)
                            {
                                x = dscX + xOffs + ((prop.shapeX[shpIx] * scaleW) >> 8);
                                if (ix == (sbyte)S.DscShapeIndex[s]) x = dscX - ((prop.shapeX[shpIx] * scaleW) >> 8) - LolShapes.scaledSize(shape.width, scaleW) - xOffs;
                                flags = 0x105;
                            }
                            else
                            {
                                x = dscX + xOffs + ((prop.shapeX[shpIx] * scaleW) >> 8);
                                flags = 0x104;
                            }
                            int y = S.BaseDscY[s] + yOffs + ((prop.shapeY[shpIx] * scaleH) >> 8);
                            var opts = new DrawShapeOpts { fadeTable = ovl, fadeLevel = 1, scaleW = scaleW, scaleH = scaleH };
                            screen.drawShape(sceneDrawPage1, shape, x + 112, y, sceneShpDim, flags, opts);
                            if ((prop.flags & 1) != 0 && shpIx < 4)
                            {
                                x += LolShapes.scaledSize(shape.width, scaleW);
                                flags ^= 1;
                                screen.drawShape(sceneDrawPage1, shape, x + 112, y, sceneShpDim, flags, opts);
                            }
                        }
                    }
                    l = prop.next;
                    shpIx = Math.Abs((int)(sbyte)S.DscShapeIndex[s]);
                }
            }
        }

        public void drawBlockEffects(int index, int type)
        {
            int[] yOffs = { 0xff, 0xff, 0x80, 0x80 };
            int flg = visibleBlocks[index].flags;
            if ((flg & 0xf0) == 0) return;
            type = type == 0 ? 2 : 0;
            for (int i = 0; i < 2; i += 1, type += 1)
            {
                if (((0x10 << type) & flg) == 0) continue;
                var (x, y) = calcCoordinatesAddDirectionOffset(0x80, yOffs[type], currentDirection);
                int drawFlag = type == 3 ? 0x80 : 0x20;
                var ovl = type == 3 ? screen.grayOverlay : null;
                x |= (visibleBlockIndex[index] & 0x1f) << 8;
                y |= (visibleBlockIndex[index] & 0xffe0) << 3;
                drawItemOrMonster(effectShapes[type], ovl, x, y, 0, type == 1 ? -20 : 0, drawFlag, -1, false);
            }
        }

        public void drawDoor(Shape shape, byte[] doorPalette, int index, int unk2, int w, int h, int flags)
        {
            if (shape == null) return;
            var S = @static;
            int c = S.DscDoorY2[(currentDirection << 5) + unk2];
            int r = Js.FloorDiv(c, 5) + 5 * S.DscDimMap[index];
            int d = S.DscOvlIndex[r];
            int t = (index << 5) + c;
            shpDmY = (short)S.DscDoorY[t] + 120;
            int u = 0;
            if ((flags & 2) != 0)
            {
                int dimW = S.DscDimMap[index];
                dmScaleW = S.DscDoorScale[dimW << 1];
                dmScaleH = S.DscDoorScale[(dimW << 1) + 1];
                u = S.DscDoor4[dimW];
            }
            d += 2;
            if (dmScaleW == 0 || dmScaleH == 0) return;
            int s = LolShapes.scaledSize(shape.height, dmScaleH) >> 1;
            if (w != 0) w = (w * dmScaleW) >> 8;
            if (h != 0) h = (h * dmScaleH) >> 8;
            shpDmX = (short)S.DscDoorX[t] + w + 200;
            shpDmY = shpDmY + 4 - s + h - u;
            if (d > 7) d = 7;
            var brightnessOverlay = screen.levelOverlays[d];
            shpDmX -= LolShapes.scaledSize(shape.width, dmScaleW) >> 1;
            shpDmY -= s;
            drawDoorOrMonsterEquipment(shape, doorPalette, shpDmX, shpDmY, flags, brightnessOverlay);
        }

        public void drawDoorOrMonsterEquipment(Shape shape, byte[] objectPalette, int x, int y, int flags, byte[] brightnessOverlay)
        {
            int flg = 0;
            if ((flags & 0x10) != 0) flg |= 1;
            if ((flags & 0x20) != 0) flg |= 0x1000;
            if ((flags & 0x40) != 0) flg |= 2;
            var opts = new DrawShapeOpts { fadeTable = brightnessOverlay, fadeLevel = 1, scaleW = dmScaleW, scaleH = dmScaleH, colorTable = objectPalette,
                transparency1 = transparencyTable1, transparency2 = transparencyTable2 };
            flg |= objectPalette != null ? 0x8104 : 0x104;
            screen.drawShape(sceneDrawPage1, shape, x, y, 13, flg, opts);
        }

        public byte[] drawItemOrMonster(Shape shape, byte[] monsterPalette, int x, int y, int fineX, int fineY, int flags, int tblValue, bool vflip)
        {
            byte[] ovl2 = null;
            byte[] brightnessOverlay = null;
            if ((flags & 0x80) != 0)
            {
                flags &= 0xff7f;
                ovl2 = monsterPalette;
                monsterPalette = null;
            }
            else ovl2 = screen.levelOverlays[4];
            int r = calcDrawingLayerParameters(x, y, shape, vflip);
            if (drawBoost > 0 && dmScaleH > 0)
            {
                // the pit's master, drawn larger: the same ground under its feet, taller above it
                int ground = shpDmY + (LolShapes.scaledSize(shape.height, dmScaleH) >> 1);
                dmScaleW = Math.Min(0x200, (dmScaleW * drawBoost) >> 8);
                dmScaleH = Math.Min(0x200, (dmScaleH * drawBoost) >> 8);
                shpDmY = ground - (LolShapes.scaledSize(shape.height, dmScaleH) >> 1);
            }
            if (tblValue == -1)
            {
                r = 7 - (r / 3 - 1); // Math.trunc(r / 3)
                r = Math.Max(0, Math.Min(7, r));
                brightnessOverlay = screen.levelOverlays[r];
            }
            else
            {
                var tmpOvl = new byte[16];
                Js.Fill(tmpOvl, (byte)tblValue);
                tmpOvl[0] = 0;
                monsterPalette = tmpOvl;
                brightnessOverlay = screen.levelOverlays[7];
            }
            int flg = (flags & 0x10) != 0 ? 1 : 0;
            if ((flags & 0x20) != 0) flg |= 0x1000;
            if ((flags & 0x40) != 0) flg |= 2;
            if (currentLevel == 22)
            {
                if (brightnessOverlay != null) brightnessOverlay[255] = 0;
            }
            else flg |= 0x2000;
            shpDmX += (dmScaleW * fineX) >> 8;
            shpDmY += (dmScaleH * fineY) >> 8;
            int dH = LolShapes.scaledSize(shape.height, dmScaleH) >> 1;
            var opts = new DrawShapeOpts { fadeTable = brightnessOverlay, fadeLevel = (flg & 0x1000) != 0 ? 0 : 1, scaleW = dmScaleW, scaleH = dmScaleH, colorTable = monsterPalette,
                transparency1 = transparencyTable1, transparency2 = transparencyTable2, backgroundFade = ovl2 };
            flg |= monsterPalette != null ? 0x8124 : 0x124;
            screen.drawShape(sceneDrawPage1, shape, shpDmX, shpDmY, 13, flg, opts);
            shpDmX -= LolShapes.scaledSize(shape.width, dmScaleW) >> 1;
            shpDmY -= dH;
            return brightnessOverlay;
        }

        /// <summary>Unity build: a scale (256 = as is) for the next sprite drawn: the pit's master, larger.</summary>
        public int drawBoost;

        public int calcDrawingLayerParameters(int x1, int y1, Shape shape, bool vflip)
        {
            var (x, y) = calcSpriteRelPosition(partyPosX, partyPosY, x1, y1, currentDirection);
            if (y < 0)
            {
                dmScaleW = dmScaleH = shpDmX = shpDmY = 0;
                return 0;
            }
            int l = y >> 5;
            // past the scale tables (an object further off than the view draws, e.g. a thrown item in flight): JS reads
            // undefined there and draws nothing; C# threw, which broke the draw - and with it the throw
            if (l >= @static.MonsterScaleY.Length || l >= @static.MonsterScaleX.Length)
            {
                dmScaleW = dmScaleH = shpDmX = shpDmY = 0;
                return l;
            }
            shpDmY = @static.MonsterScaleY[l];
            shpDmX = ((@static.MonsterScaleX[l] * x) >> 8) + 200;
            int wh = shpDmY - 56;
            dmScaleW = dmScaleH = shpDmY > 120 ? 0x100 : wh >= 0 && wh < @static.MonsterScaleWH.Length ? @static.MonsterScaleWH[wh] : 0;
            if (vflip) shpDmY = ((120 - shpDmY) >> 1) + (LolShapes.scaledSize(shape.height, dmScaleH) >> 1);
            else shpDmY -= LolShapes.scaledSize(shape.height, dmScaleH) >> 1;
            return l;
        }

        // ---- scene transitions ----
        public async Task movePartySmoothScrollBlocked(int speed)
        {
            if (!smoothScrollingEnabled || needSceneRestore != 0) return;
            screen.backupSceneWindow(sceneDrawPage2 == 2 ? 2 : 6, 6);
            var S = @static;
            double delayTimer = getMillis();
            for (int i = 0; i < 2; i += 1)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollZoomStepTop(6, 2, S.ScrollXTop[i], S.ScrollYTop[i]);
                screen.smoothScrollZoomStepBottom(6, 2, S.ScrollXBottom[i], S.ScrollYBottom[i]);
                screen.restoreSceneWindow(2, 0);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            delayTimer = getMillis();
            for (int i = 2; i != 0; i -= 1)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollZoomStepTop(6, 2, S.ScrollXTop[i], S.ScrollYTop[i]);
                screen.smoothScrollZoomStepBottom(6, 2, S.ScrollXBottom[i], S.ScrollYBottom[i]);
                screen.restoreSceneWindow(2, 0);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            if (sceneDefaultUpdate != 2) screen.restoreSceneWindow(6, 0);
            updateDrawPage2();
        }

        public async Task movePartySmoothScrollUp(int speed)
        {
            if (!smoothScrollingEnabled || needSceneRestore != 0) return;
            int d;
            if (sceneDrawPage2 == 2)
            {
                d = smoothScrollDrawSpecialGuiShape(6);
                gui_drawScene(6);
                screen.backupSceneWindow(6, 12);
                screen.backupSceneWindow(2, 6);
            }
            else
            {
                d = smoothScrollDrawSpecialGuiShape(2);
                gui_drawScene(2);
                screen.backupSceneWindow(2, 12);
                screen.backupSceneWindow(6, 6);
            }
            var S = @static;
            double delayTimer = getMillis();
            for (int i = 0; i < 5; i += 1)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollZoomStepTop(6, 2, S.ScrollXTop[i], S.ScrollYTop[i]);
                screen.smoothScrollZoomStepBottom(6, 2, S.ScrollXBottom[i], S.ScrollYBottom[i]);
                if (d != 0) screen.copyGuiShapeToSurface(14, 2);
                screen.restoreSceneWindow(2, 0);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            if (d != 0) screen.copyGuiShapeToSurface(14, 12);
            if (sceneDefaultUpdate != 2) screen.restoreSceneWindow(12, 0);
            updateDrawPage2();
        }

        public async Task movePartySmoothScrollDown(int speed)
        {
            if (!smoothScrollingEnabled) return;
            int d = smoothScrollDrawSpecialGuiShape(2);
            gui_drawScene(2);
            screen.backupSceneWindow(2, 6);
            var S = @static;
            double delayTimer = getMillis();
            for (int i = 4; i >= 0; i -= 1)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollZoomStepTop(6, 2, S.ScrollXTop[i], S.ScrollYTop[i]);
                screen.smoothScrollZoomStepBottom(6, 2, S.ScrollXBottom[i], S.ScrollYBottom[i]);
                if (d != 0) screen.copyGuiShapeToSurface(14, 2);
                screen.restoreSceneWindow(2, 0);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            if (d != 0) screen.copyGuiShapeToSurface(14, 12);
            if (sceneDefaultUpdate != 2) screen.restoreSceneWindow(6, 0);
            updateDrawPage2();
        }

        public async Task movePartySmoothScrollLeft(int speed)
        {
            if (!smoothScrollingEnabled) return;
            speed <<= 1;
            gui_drawScene(sceneDrawPage1);
            double delayTimer = getMillis();
            for (int i = 88, d = 88; i > 22; i -= 22, d += 22)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollHorizontalStep(sceneDrawPage2, 66, d, i);
                screen.copyRegion(112 + i, 0, 112, 0, d, 120, sceneDrawPage1, sceneDrawPage2, true);
                screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage2, 0, true);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            if (sceneDefaultUpdate != 2) screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage1, 0, true);
            (sceneDrawPage1, sceneDrawPage2) = (sceneDrawPage2, sceneDrawPage1);
        }

        public async Task movePartySmoothScrollRight(int speed)
        {
            if (!smoothScrollingEnabled) return;
            speed <<= 1;
            gui_drawScene(sceneDrawPage1);
            double delayTimer = getMillis() + speed * tickLength;
            screen.copyRegion(112, 0, 222, 0, 66, 120, sceneDrawPage1, sceneDrawPage2, true);
            screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage2, 0, true);
            fadeTextStep();
            await delayUntil(delayTimer);
            delayTimer += speed * tickLength;
            screen.smoothScrollHorizontalStep(sceneDrawPage2, 22, 0, 66);
            screen.copyRegion(112, 0, 200, 0, 88, 120, sceneDrawPage1, sceneDrawPage2, true);
            screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage2, 0, true);
            fadeTextStep();
            await delayUntil(delayTimer);
            delayTimer += speed * tickLength;
            screen.smoothScrollHorizontalStep(sceneDrawPage2, 44, 0, 22);
            screen.copyRegion(112, 0, 178, 0, 110, 120, sceneDrawPage1, sceneDrawPage2, true);
            screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage2, 0, true);
            fadeTextStep();
            await delayUntil(delayTimer);
            if (sceneDefaultUpdate != 2) screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage1, 0, true);
            (sceneDrawPage1, sceneDrawPage2) = (sceneDrawPage2, sceneDrawPage1);
        }

        public async Task movePartySmoothScrollTurn(int speed, bool left)
        {
            if (!smoothScrollingEnabled) return;
            speed <<= 1;
            int d = smoothScrollDrawSpecialGuiShape(sceneDrawPage1);
            gui_drawScene(sceneDrawPage1);
            int dp = sceneDrawPage2 == 2 ? sceneDrawPage2 : sceneDrawPage1;
            int[] steps = left ? new[] { 1, 2, 3 } : new[] { 3, 2, 1 };
            var (p1, p2) = left ? (sceneDrawPage1, sceneDrawPage2) : (sceneDrawPage2, sceneDrawPage1);
            double delayTimer = getMillis();
            foreach (int step in steps)
            {
                delayTimer += speed * tickLength;
                screen.smoothScrollTurnStep(step, p1, p2, dp);
                if (d != 0) screen.copyGuiShapeToSurface(14, dp);
                screen.restoreSceneWindow(dp, 0);
                fadeTextStep();
                await delayUntil(delayTimer);
            }
            if (sceneDefaultUpdate != 2)
            {
                drawSpecialGuiShape(sceneDrawPage1);
                screen.copyRegion(112, 0, 112, 0, 176, 120, sceneDrawPage1, 0, true);
            }
        }

        public int smoothScrollDrawSpecialGuiShape(int pageNum)
        {
            if (specialGuiShape == null) return 0;
            screen.clearGuiShapeMemory(pageNum);
            screen.drawShape(pageNum, specialGuiShape, specialGuiShapeX, specialGuiShapeY, 2, 0);
            screen.copyGuiShapeFromSceneBackupBuffer(pageNum, 14);
            return 1;
        }

        public async Task shakeScene(int duration, int width, int height, int restore)
        {
            screen.copyRegion(112, 0, 112, 0, 176, 120, 0, 6, true);
            double endTime = getMillis() + duration * tickLength;
            double delayTimer = getMillis();
            while (endTime > getMillis())
            {
                delayTimer += 2 * tickLength;
                int s1 = width != 0 ? (presentationRandom(255) % (width << 1)) - width : 0;
                int s2 = height != 0 ? (presentationRandom(255) % (height << 1)) - height : 0;
                var (x1, x2, w) = s1 >= 0 ? (112, 112 + s1, 176 - s1) : (112 - s1, 112, 176 + s1);
                var (y1, y2, h) = s2 >= 0 ? (0, s2, 120 - s2) : (-s2, 0, 120 + s2);
                screen.copyRegion(x1, y1, x2, y2, w, h, 6, 0, true);
                await delayUntil(delayTimer);
            }
            if (restore != 0)
            {
                screen.copyRegion(112, 0, 112, 0, 176, 120, 6, 0, true);
                updateDrawPage2();
            }
        }

        // ---- special scenes ----
        public async Task prepareSpecialScene(int fieldType, int hasDialogue, int suspendGui, int allowSceneUpdate, int controlMode, int fadeFlag)
        {
            resetPortraitsAndDisableSysTimer();
            if (fieldType != 0)
            {
                if (suspendGui != 0) gui_specialSceneSuspendControls(1);
                if (allowSceneUpdate == 0) sceneDefaultUpdate = 0;
                if (hasDialogue != 0) initDialogueSequence(fieldType, 0);
                if (fadeFlag != 0)
                {
                    await screen.fadePalette(screen.getPalette(3), 10);
                    screen.fadeFlag = 0;
                }
                setSpecialSceneButtons(0, 0, 320, 130, controlMode);
            }
            else
            {
                if (suspendGui != 0) gui_specialSceneSuspendControls(0);
                if (allowSceneUpdate == 0) sceneDefaultUpdate = 0;
                gui_disableControls(controlMode);
                if (fadeFlag != 0)
                {
                    var p3 = screen.getPalette(3);
                    Js.Set(p3, Js.Slice(screen.getPalette(0), 128 * 3), 128 * 3);
                    screen.loadSpecialColors(p3);
                    await screen.fadePalette(p3, 10);
                    screen.fadeFlag = 0;
                }
                if (hasDialogue != 0) initDialogueSequence(fieldType, 0);
                setSpecialSceneButtons(112, 0, 176, 120, controlMode);
            }
        }

        public async Task<int> restoreAfterSpecialScene(int fadeFlag, int redrawPlayField, int releaseTimScripts, int sceneUpdateMode)
        {
            if (needSceneRestore == 0) return 0;
            needSceneRestore = 0;
            enableSysTimer(2);
            if (dialogueField) restoreAfterDialogueSequence(currentControlMode);
            if (specialSceneFlag != 0) gui_specialSceneRestoreControls(currentControlMode);
            int l = currentControlMode;
            currentControlMode = 0;
            gui_specialSceneRestoreButtons();
            calcCharPortraitXpos();
            currentControlMode = l;
            if (releaseTimScripts != 0)
            {
                for (int i = 0; i < 6; i += 1) tim.freeAnimStruct(i);
                for (int i = 0; i < 10; i += 1) activeTim[i] = null;
            }
            gui_enableControls();
            if (fadeFlag != 0)
            {
                if ((screen.fadeFlag != 1 && screen.fadeFlag != 2) || (screen.fadeFlag == 1 && currentControlMode != 0))
                {
                    if (currentControlMode != 0) await screen.fadeToBlack(10);
                    else await screen.fadeClearSceneWindow(10);
                }
                currentControlMode = 0;
                calcCharPortraitXpos();
                if (redrawPlayField != 0) gui_drawPlayField();
                await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
            }
            else
            {
                currentControlMode = 0;
                calcCharPortraitXpos();
                if (redrawPlayField != 0) gui_drawPlayField();
            }
            sceneDefaultUpdate = sceneUpdateMode;
            return 1;
        }

        public void setSequenceButtons(int x, int y, int w, int h, int enableFlags)
        {
            gui_enableSequenceButtons(x, y, w, h, enableFlags);
            seqWindowX1 = x;
            seqWindowY1 = y;
            seqWindowX2 = x + w;
            seqWindowY2 = y + h;
            currentFloatingCursor = -1;
            if (w == 320)
            {
                setLampMode(false); // JS: setLampMode(0)
                lampStatusSuspended = true;
            }
        }

        public void setSpecialSceneButtons(int x, int y, int w, int h, int enableFlags)
        {
            gui_enableSequenceButtons(x, y, w, h, enableFlags);
            spsWindowX = x;
            spsWindowY = y;
            spsWindowW = w;
            spsWindowH = h;
        }

        public void setDefaultButtonState()
        {
            gui_enableDefaultPlayfieldButtons();
            seqWindowX1 = seqWindowY1 = seqWindowX2 = seqWindowY2 = seqTrigger = 0;
            if (lampStatusSuspended) resetLampStatus();
            lampStatusSuspended = false;
        }

        // ---- lamp & compass ----
        public void resetLampStatus()
        {
            flagsTable[31] |= 0x04;
            lampEffect = -1;
            updateLampStatus();
        }

        public void setLampMode(bool lampOn)
        {
            flagsTable[31] &= 0xfb;
            if ((flagsTable[31] & 0x08) == 0 || !lampOn) return;
            screen.drawShape(0, gameShapes[43], 291, 56, 0, 0);
            lampEffect = 8;
        }

        public void updateLampStatus()
        {
            int newLampEffect = 0;
            if ((updateFlags & 4) != 0 || (flagsTable[31] & 0x08) == 0) return;
            // resumeGame leaves fadeFlag 3 ("fade in") and nothing clears it until a scene or a level changes:
            // the lamp below never touched the palette, so after loading a save the lantern lit nothing. The
            // game's page asks for this (repairOnLoad); the parity tracers keep the web engine's behaviour.
            if (repairOnLoad && screen.fadeFlag == 3) screen.fadeFlag = 0;
            if (brightness == 0 || lampOilStatus == 0 || lampSwitchedOff)
            {
                newLampEffect = 8;
                if (newLampEffect != lampEffect && screen.fadeFlag == 0) _ = setPaletteBrightness(screen.getPalette(0), brightness, newLampEffect);
            }
            else
            {
                int tmpOilStatus = lampOilStatus < 100 ? lampOilStatus : 100;
                newLampEffect = (3 - (tmpOilStatus - 1) / 25) << 1; // Math.trunc
                if (lampEffect == -1)
                {
                    if (screen.fadeFlag == 0) _ = setPaletteBrightness(screen.getPalette(0), brightness, newLampEffect);
                    lampStatusTimer = getMillis() + (10 + presentationRoll(1, 30)) * tickLength;
                }
                else if ((lampEffect & 0xfe) == (newLampEffect & 0xfe))
                {
                    if (getMillis() <= lampStatusTimer) newLampEffect = lampEffect;
                    else
                    {
                        newLampEffect = lampEffect ^ 1;
                        lampStatusTimer = getMillis() + (10 + presentationRoll(1, 30)) * tickLength;
                    }
                }
                else if (screen.fadeFlag == 0) _ = setPaletteBrightness(screen.getPalette(0), brightness, newLampEffect);
            }
            if (newLampEffect == lampEffect) return;
            screen.drawShape(screen.curPage, gameShapes[35 + newLampEffect], 291, 56, 0, 0);
            lampEffect = newLampEffect;
        }

        public void updateCompass()
        {
            if ((flagsTable[31] & 0x40) == 0 || (updateFlags & 4) != 0) return;
            if (compassDirection == -1)
            {
                compassStep = 0;
                gui_drawCompass();
                return;
            }
            if (compassTimer >= getMillis()) return;
            if ((currentDirection << 6) == compassDirection && compassStep == 0) return;
            compassTimer = getMillis() + 3 * tickLength;
            int dir = compassStep >= 0 ? 1 : -1;
            if (compassStep != 0) compassStep -= ((Math.Abs(compassStep) >> 4) + 2) * dir;
            int diff = compassBroken != 0 ? (sbyte)presentationRandom(255) - compassDirection : (currentDirection << 6) - compassDirection;
            if (diff <= -128) diff += 256;
            if (diff >= 128) diff -= 256;
            diff >>= 2;
            compassStep += diff;
            compassStep = Math.Max(-24, Math.Min(24, compassStep));
            compassDirection += compassStep;
            if (compassDirection < 0) compassDirection += 256;
            if (compassDirection > 255) compassDirection -= 256;
            if ((((compassDirection + 3) & 0xfd) >> 6) == currentDirection && compassStep < 2 && Math.Abs(diff) < 4)
            {
                compassDirection = currentDirection << 6;
                compassStep = 0;
            }
            gui_drawCompass();
        }
    }
}
