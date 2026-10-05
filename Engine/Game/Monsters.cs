// src/game/monsters.mjs: monsters: shapes, placement, AI and drawing (sprites_lol.cpp).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lol
{
    public sealed partial class LandsOfLore
    {
        static readonly int[] Monsters_STEP_X = { 0, 32, 32, 32, 0, -32, -32, -32 };
        static readonly int[] Monsters_STEP_Y = { -32, -32, 0, 32, 32, 32, 0, -32 };

        // ---- monsters.mjs fields ----
        public Monster[] monsters;
        public MonsterProperty[] monsterProperties;
        public Shape[] monsterShapes;
        public byte[][] monsterPalettes;
        public Shape[] monsterDecorationShapes;
        public byte[] monsterAnimType;
        /// <summary>JS: created on the first loadMonsterShapes, grown by index</summary>
        public List<string> monsterShapeNames;
        public HashSet<int> warnMissingItemProperty;
        /// <summary>host setting (main.mjs: engine.fleeingMonsters = settings.flee)</summary>
        public bool fleeingMonsters;

        /// <summary>JS ToInt32 (what `| 0` / `>>` do to a double): NaN and Infinity give 0.</summary>
        static int Monsters_toInt32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return unchecked((int)(uint)(long)Math.Truncate(v));
        }

        /// <summary>arr[i] where the JS reads past the end and gets undefined.</summary>
        static Shape Monsters_at(Shape[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : null;

        public void initMonsters()
        {
            monsters = new Monster[30];
            for (int i = 0; i < 30; i += 1) monsters[i] = makeEmptyMonster(i);
            monsterProperties = new MonsterProperty[5];
            for (int i = 0; i < 5; i += 1) monsterProperties[i] = makeMonsterProperty();
            monsterShapes = new Shape[48];
            monsterPalettes = new byte[48][];
            monsterDecorationShapes = new Shape[576];
            monsterAnimType = new byte[3];
        }

        public Monster makeEmptyMonster(int id) => new Monster { id = id };

        public MonsterProperty makeMonsterProperty() => new MonsterProperty();

        public void disableMonsters()
        {
            for (int i = 0; i < 30; i += 1) monsters[i] = makeEmptyMonster(i);
        }

        public int getBlockDistance(int block1, int block2)
        {
            int dy = Math.Abs((block2 >> 5) - (block1 >> 5));
            int dx = Math.Abs((block2 & 0x1f) - (block1 & 0x1f));
            if (dx > dy) (dx, dy) = (dy, dx);
            return (dx >> 1) + dy;
        }

        public void loadMonsterShapes(string file, int monsterIndex, int animType)
        {
            releaseMonsterShapes(monsterIndex);
            var bytes = Cps.decodeBitmapData(res.get(file)).data;
            var shapes = LolShapes.decodeShapeFile(bytes);
            for (int i = 0; i < shapes.Length; i += 1) { var shape = shapes[i]; if (shape != null) shape.key = $"{file}:{i}"; }
            if (monsterShapeNames == null) monsterShapeNames = new List<string>();
            while (monsterShapeNames.Count <= monsterIndex) monsterShapeNames.Add(null);
            monsterShapeNames[monsterIndex] = file;
            int pos = monsterIndex << 4;
            for (int i = 0; i < 16; i += 1)
            {
                monsterShapes[pos + i] = shapes[i];
                monsterPalettes[pos + i] = new byte[(shapes[i].colorCount != 0 ? shapes[i].colorCount : 16) << 3];
            }
            for (int i = 0; i < 4; i += 1)
            {
                for (int ii = 0; ii < 16; ii += 1)
                {
                    int @base = monsterIndex * 192 + i * 48 + ii * 3;
                    int s = (i << 4) + ii + 17;
                    monsterDecorationShapes[@base] = Monsters_at(shapes, s);
                    monsterDecorationShapes[@base + 1] = Monsters_at(shapes, s + 1);
                    monsterDecorationShapes[@base + 2] = Monsters_at(shapes, s + 2);
                }
            }
            monsterAnimType[monsterIndex] = (byte)(animType & 0xff);
            // Shape 16 is a palette image: column 0 lists the base colors, columns 1..8 the recolor variants.
            var palShape = Monsters_at(shapes, 16) ?? new Shape { width = 0, height = 0, pixels = new byte[0] }; // some files (APARITIN, TCABAL) have no variants
            var grid = new byte[320 * 200];
            for (int y = 0; y < palShape.height; y += 1)
            {
                for (int x = 0; x < palShape.width; x += 1)
                {
                    int raw = palShape.pixels[y * palShape.width + x];
                    if (raw != 0) grid[y * 320 + x] = palShape.colorTable != null ? palShape.colorTable[raw] : (byte)raw;
                }
            }
            var tmpPal1 = new byte[64];
            for (int i = 0; i < 64; i += 1) tmpPal1[i] = grid[i * 320];
            for (int i = 0; i < 16; i += 1)
            {
                var shape = monsterShapes[pos + i];
                int numCol = shape.colorCount;
                var table = shape.colorTable;
                var tmpPal3 = new int[256];
                Js.Fill(tmpPal3, -1);
                for (int ii = 0; ii < numCol; ii += 1)
                {
                    int cl = Array.IndexOf(tmpPal1, table[ii]);
                    if (cl >= 0) tmpPal3[ii] = cl;
                }
                var @out = monsterPalettes[pos + i];
                for (int ii = 0; ii < 8; ii += 1)
                {
                    var tmpPal2 = (byte[])table.Clone();
                    for (int iii = 0; iii < numCol; iii += 1)
                    {
                        if (tmpPal3[iii] == -1) continue;
                        byte v = grid[tmpPal3[iii] * 320 + ii + 1];
                        if (v != 0) tmpPal2[iii] = v;
                    }
                    Js.Set(@out, Js.Slice(tmpPal2, 0, numCol), ii * numCol);
                }
            }
        }

        public void releaseMonsterShapes(int monsterIndex)
        {
            for (int i = 0; i < 16; i += 1)
            {
                monsterShapes[(monsterIndex << 4) + i] = null;
                monsterPalettes[(monsterIndex << 4) + i] = null;
            }
            for (int i = 0; i < 192; i += 1) monsterDecorationShapes[monsterIndex * 192 + i] = null;
        }

        /// <summary>the monster types this level's scripts set up (loadMonsterProperties), the ones it can draw</summary>
        public readonly HashSet<int> levelMonsterTypes = new HashSet<int>();

        public void loadMonsterProperties(int[] args)
        {
            levelMonsterTypes.Add(args[0]);
            var l = monsterProperties[args[0]];
            l.shapeIndex = args[1] & 0xff;
            int shpWidthMax = 0;
            for (int i = 0; i < 16; i += 1)
            {
                var shape = monsterShapes[(l.shapeIndex << 4) + i];
                int m = shape != null ? shape.width & 0xff : 0;
                if (m > shpWidthMax) shpWidthMax = m;
            }
            l.maxWidth = shpWidthMax;
            l.fightingStats[0] = (ushort)((args[2] << 8) / 100);
            l.fightingStats[1] = 256;
            l.fightingStats[2] = (ushort)((args[3] << 8) / 100);
            l.fightingStats[3] = (ushort)args[4];
            l.fightingStats[4] = (ushort)((args[5] << 8) / 100);
            l.fightingStats[5] = (ushort)((args[6] << 8) / 100);
            l.fightingStats[6] = (ushort)((args[7] << 8) / 100);
            l.fightingStats[7] = (ushort)((args[8] << 8) / 100);
            l.fightingStats[8] = 0;
            for (int i = 0; i < 8; i += 1)
            {
                l.itemsMight[i] = (ushort)args[9 + i];
                l.protectionAgainstItems[i] = (ushort)((args[17 + i] << 8) / 100);
            }
            l.itemProtection = args[25];
            l.hitPoints = args[26];
            l.speedTotalWaitTicks = 1;
            l.flags = args[27];
            l.unk5 = args[29];
            l.numDistAttacks = args[30];
            l.numDistWeapons = args[31];
            for (int i = 0; i < 3; i += 1) l.distWeapons[i] = args[32 + i];
            l.attackSkillChance = args[35];
            l.attackSkillType = args[36];
            l.defenseSkillChance = args[37];
            l.defenseSkillType = args[38];
            for (int i = 0; i < 3; i += 1) l.sounds[i] = args[39 + i];
        }

        public int initMonster(int[] args)
        {
            var (x, y) = calcCoordinates(args[0], args[1], args[2]);
            int w = monsterProperties[args[4]].maxWidth;
            if (checkBlockBeforeObjectPlacement(x, y, w, 7, 7) != 0) return -1;
            for (int i = 0; i < 30; i += 1)
            {
                var old = monsters[i];
                if (old.hitPoints != 0 || old.mode == 13) continue;
                var l = makeEmptyMonster(i);
                monsters[i] = l;
                l.mode = 0;
                l.x = x;
                l.y = y;
                l.facing = args[3];
                l.type = args[4];
                l.properties = monsterProperties[l.type];
                l.direction = l.facing << 1;
                l.hitPoints = (l.properties.hitPoints * @static.MonsterModifiers1[monsterDifficulty]) >> 8;
                if (currentLevel != 12 || l.type != 2) l.hitPoints = (l.hitPoints * (rollDice(1, 128) + 192)) >> 8;
                l.numDistAttacks = l.properties.numDistAttacks;
                l.distAttackTick = rollDice(1, calcMonsterSkillLevel(l.id | 0x8000, 8)) - 1;
                l.flyingHeight = 2;
                l.flags = args[5];
                l.assignedItems = 0;
                setMonsterMode(l, args[6]);
                placeMonster(l, l.x, l.y);
                l.destX = l.x;
                l.destY = l.y;
                l.destDirection = l.direction;
                for (int ii = 0; ii < 4; ii += 1) l.equipmentShapes[ii] = (7 + ii < args.Length ? args[7 + ii] : 0) & 0xff;
                checkSceneUpdateNeed(l.block);
                return i;
            }
            return -1;
        }

        public int deleteMonstersFromBlock(int block)
        {
            int i = levelBlockProperties[block].assignedObjects;
            int cnt = 0;
            while (i != 0)
            {
                int next = findObject(i).nextAssignedObject;
                if ((i & 0x8000) == 0) { i = next; continue; }
                var m = monsters[i & 0x7fff];
                cnt += 1;
                setMonsterMode(m, 14);
                checkSceneUpdateNeed(m.block);
                placeMonster(m, 0, 0);
                i = next;
            }
            return cnt;
        }

        public void setMonsterMode(Monster monster, int mode)
        {
            if (monster.mode == 13 && mode != 14) return;
            if (mode == 7)
            {
                monster.destX = partyPosX;
                monster.destY = partyPosY;
            }
            if (monster.mode == 1 && mode == 7)
            {
                for (int i = 0; i < 30; i += 1)
                {
                    if (monster.mode != 1) continue;
                    monster.mode = mode;
                    monster.fightCurTick = 0;
                    monster.destX = partyPosX;
                    monster.destY = partyPosY;
                    setMonsterDirection(monster, calcMonsterDirection(monster.x, monster.y, monster.destX, monster.destY));
                }
            }
            else
            {
                monster.mode = mode;
                monster.fightCurTick = 0;
                if (mode == 14) monster.hitPoints = 0;
                if (mode == 13 && (monster.flags & 0x20) != 0)
                {
                    monster.mode = 0;
                    monsterDropItems(monster);
                    if (currentLevel != 29) setMonsterMode(monster, 14);
                    queueAsync(() => runLevelScriptCustom(0x404, -1, monster.id, monster.id, 0, 0));
                    checkSceneUpdateNeed(monster.block);
                    if (monster.mode == 14) placeMonster(monster, 0, 0);
                }
            }
        }

        static readonly int[] Monsters_adjustDims = { 0, 13, 9, 3 };

        public bool updateMonsterAdjustBlocks(Monster monster)
        {
            var dims = Monsters_adjustDims;
            if ((monster.properties.flags & 8) != 0) return true;
            int x1 = (monster.x & 0xff00) | 0x80;
            int y1 = (monster.y & 0xff00) | 0x80;
            int dir;
            if ((monster.properties.flags & 1) != 0) dir = monster.direction >> 1;
            else
            {
                dir = calcMonsterDirection(x1, y1, partyPosX, partyPosY);
                if ((monster.properties.flags & 2) != 0 && dir == (monster.direction ^ 4)) return false;
                dir >>= 1;
            }
            var (x2, y2) = calcSpriteRelPosition(x1, y1, partyPosX, partyPosY, dir);
            x2 >>= 8;
            y2 >>= 8;
            if (y2 < 0 || y2 > 3) return false;
            if (Math.Abs(x2) > y2) return false;
            for (int i = 0; i < 18; i += 1)
            {
                visibleBlocks[i] = levelBlockProperties[(monster.block + dscBlockIndex[dir + i]) & 0x3ff];
            }
            Js.Fill(lvlShapeLeftRight, (short)-1);
            var (fx1, fx2) = setLevelShapesDim(x2 + dims[y2], 13);
            Js.Fill(lvlShapeLeftRight, (short)-1);
            return fx1 < fx2;
        }

        public void placeMonster(Monster monster, int x, int y)
        {
            bool cont = true;
            int t = monster.block;
            if (monster.block != 0)
            {
                removeAssignedObjectFromBlock(levelBlockProperties[t], monster.id | 0x8000);
                levelBlockProperties[t].direction = 5;
                checkSceneUpdateNeed(t);
            }
            else cont = false;
            monster.block = calcBlockIndex(x, y);
            if (monster.x != x || monster.y != y)
            {
                monster.x = x;
                monster.y = y;
                monster.currentSubFrame = (monster.currentSubFrame + 1) & 3;
            }
            if (monster.block == 0) return;
            assignObjectToBlock(levelBlockProperties[monster.block], monster.id | 0x8000);
            levelBlockProperties[monster.block].direction = 5;
            checkSceneUpdateNeed(monster.block);
            if (monster.properties.sounds[0] == 0 || monster.properties.sounds[0] == 255 || cont == false) return;
            if (((monster.properties.flags & 0x100) == 0 || (monster.currentSubFrame & 1) == 0) && monster.block == t) return;
            if (monster.block != t) queueAsync(() => runLevelScriptCustom(monster.block, 0x800, -1, monster.id, 0, 0));
            if ((updateFlags & 1) != 0) return;
            snd_processEnvironmentalSoundEffect(monster.properties.sounds[0], monster.block);
        }

        static readonly int[] Monsters_directionTable = { 1, 2, 1, 0, 7, 6, 7, 0, 3, 2, 3, 4, 5, 6, 5, 4 };

        public int calcMonsterDirection(int x1, int y1, int x2, int y2)
        {
            int r = 0;
            int t1 = (short)(y1 - y2);
            if (t1 < 0) { r += 1; t1 = -t1; }
            r <<= 1;
            int t2 = (short)(x2 - x1);
            if (t2 < 0) { r += 1; t2 = -t2; }
            int f = t1 > t2 ? 1 : 0;
            if (t2 >= t1) (t1, t2) = (t2, t1);
            r = (r << 1) | f;
            t1 = (t1 + 1) >> 1;
            f = t1 > t2 ? 1 : 0;
            r = (r << 1) | f;
            return Monsters_directionTable[r];
        }

        public void setMonsterDirection(Monster monster, int dir)
        {
            monster.direction = dir;
            if ((dir & 1) == 0 || monster.direction - (monster.facing << 1) >= 2) monster.facing = monster.direction >> 1;
            checkSceneUpdateNeed(monster.block);
        }

        public void monsterDropItems(Monster monster)
        {
            int a = monster.assignedItems;
            monster.assignedItems = 0;
            // the position has to be taken now: the caller moves the monster off the map right after this,
            // and the queued placement would otherwise drop everything on block 0
            int x = monster.x;
            int y = monster.y;
            while (a != 0)
            {
                int b = a;
                a = itemsInPlay[a].nextAssignedObject;
                queueAsync(() => setItemPosition(b, x, y, 0, 1));
            }
        }

        public int checkBlockBeforeObjectPlacement(int x, int y, int objectWidth, int testFlag, int wallFlag)
        {
            objectLastDirection = 0;
            int x2 = 0;
            int y2 = 0;
            int xOffs = 0;
            int yOffs = 0;
            int flag = 0;
            int r = testBlockPassability(calcBlockIndex(x, y), x, y, objectWidth, testFlag, wallFlag);
            if (r != 0) return r;
            if (checkBlockOccupiedByParty(x, y, testFlag) != 0) return 4;
            if ((x & 0x80) != 0)
            {
                if ((((x & 0xff) + objectWidth) & 0xff00) != 0)
                {
                    xOffs = 1;
                    objectLastDirection = 2;
                    x2 = x + objectWidth;
                    r = testBlockPassability(calcBlockIndex(x2, y), x, y, objectWidth, testFlag, wallFlag);
                    if (r != 0) return r;
                    if (checkBlockOccupiedByParty(x + xOffs, y, testFlag) != 0) return 4;
                    flag = 1;
                }
            }
            else if ((((x & 0xff) - objectWidth) & 0xff00) != 0)
            {
                xOffs = -1;
                objectLastDirection = 6;
                x2 = x - objectWidth;
                r = testBlockPassability(calcBlockIndex(x2, y), x, y, objectWidth, testFlag, wallFlag);
                if (r != 0) return r;
                if (checkBlockOccupiedByParty(x + xOffs, y, testFlag) != 0) return 4;
                flag = 1;
            }
            if ((y & 0x80) != 0)
            {
                if ((((y & 0xff) + objectWidth) & 0xff00) != 0)
                {
                    yOffs = 1;
                    objectLastDirection = 4;
                    y2 = y + objectWidth;
                    r = testBlockPassability(calcBlockIndex(x, y2), x, y, objectWidth, testFlag, wallFlag);
                    if (r != 0) return r;
                    if (checkBlockOccupiedByParty(x, y + yOffs, testFlag) != 0) return 4;
                    flag &= 1;
                }
                else flag = 0;
            }
            else if ((((y & 0xff) - objectWidth) & 0xff00) != 0)
            {
                yOffs = -1;
                objectLastDirection = 0;
                y2 = y - objectWidth;
                r = testBlockPassability(calcBlockIndex(x, y2), x, y, objectWidth, testFlag, wallFlag);
                if (r != 0) return r;
                if (checkBlockOccupiedByParty(x, y + yOffs, testFlag) != 0) return 4;
                flag &= 1;
            }
            else flag = 0;
            if (flag == 0) return 0;
            r = testBlockPassability(calcBlockIndex(x2, y2), x, y, objectWidth, testFlag, wallFlag);
            if (r != 0) return r;
            if (checkBlockOccupiedByParty(x + xOffs, y + yOffs, testFlag) != 0) return 4;
            return 0;
        }

        public int testBlockPassability(int block, int x, int y, int objectWidth, int testFlag, int wallFlag)
        {
            if (block == currentBlock) testFlag &= 0xfffe;
            if ((testFlag & 1) != 0)
            {
                monsterCurBlock = block;
                if (testWallFlag(block, -1, wallFlag)) return 1;
                monsterCurBlock = 0;
            }
            if ((testFlag & 2) == 0) return 0;
            int obj = levelBlockProperties[block].assignedObjects;
            while ((obj & 0x8000) != 0)
            {
                var monster = monsters[obj & 0x7fff];
                if (monster.mode < 13)
                {
                    int r = checkDrawObjectSpace(x, y, monster.x, monster.y);
                    if (objectWidth + monster.properties.maxWidth > r) return 2;
                }
                obj = findObject(obj).nextAssignedObject;
            }
            return 0;
        }

        public int calcMonsterSkillLevel(int id, int a)
        {
            var c = getCharacterOrMonsterStats(id);
            double rd = Math.Truncate((double)(a << 8) / c[4]);   // float division, as in JS (a zero divisor gives Infinity)
            int r;
            if ((id & 0x8000) != 0) r = Monsters_toInt32(rd * @static.MonsterModifiers2[monsterDifficulty]) >> 8;
            else
            {
                r = (int)rd;
                int sk = characters[id].skillLevels[1];
                if (sk > 7) r -= r >> 1;
                else if (sk > 3) r -= r >> 2;
            }
            return r;
        }

        public int checkBlockOccupiedByParty(int x, int y, int testFlag)
        {
            return (testFlag & 4) != 0 && currentBlock == calcBlockIndex(x, y) ? 1 : 0;
        }

        // ---- drawing ----
        public void drawBlockObjects(int blockArrayIndex)
        {
            var l = visibleBlocks[blockArrayIndex];
            int s = l.assignedObjects;
            if (l.direction != currentDirection)
            {
                l.drawObjects = 0;
                l.direction = currentDirection;
                while (s != 0)
                {
                    reassignDrawObjects(currentDirection, s, l, true);
                    s = findObject(s).nextAssignedObject;
                }
            }
            s = l.drawObjects;
            while (s != 0)
            {
                if ((s & 0x8000) != 0)
                {
                    s &= 0x7fff;
                    if (blockArrayIndex < 15) drawMonster(s);
                    s = monsters[s].nextDrawObject;
                }
                else
                {
                    var i = itemsInPlay[s];
                    int fx = (sbyte)@static.SceneItemOffs[s & 7] << 1; // int8 table
                    int fy = (sbyte)@static.SceneItemOffs[(s >> 1) & 7] + 5;
                    if (i.flyingHeight >= 2 && blockArrayIndex >= 15) { s = i.nextDrawObject; continue; }
                    Shape shp = null;
                    int flg = 0;
                    if (i.flyingHeight >= 2) fy -= (i.flyingHeight - 1) * 6;
                    var prop = i.itemPropertyIndex >= 0 && i.itemPropertyIndex < itemProperties.Count ? itemProperties[i.itemPropertyIndex] : null;
                    // An item whose property record is missing cannot be drawn. That happens to a save holding
                    // a crafted item (a potion, the pit's sigil) when the build that loads it has not registered
                    // those extra items yet: skip it rather than take the whole scene down.
                    if (prop == null)
                    {
                        warnMissingItemProperty = warnMissingItemProperty ?? new HashSet<int>();
                        if (!warnMissingItemProperty.Contains(i.itemPropertyIndex))
                        {
                            warnMissingItemProperty.Add(i.itemPropertyIndex);
                            log($"Item property {i.itemPropertyIndex} is not registered; the item is not drawn");
                        }
                        s = i.nextDrawObject;
                        continue;
                    }
                    if ((prop.flags & 0x1000) != 0 && (i.shpCurFrame_flg & 0xc000) == 0)
                    {
                        int shpIndex = (prop.flags & 0x800) != 0 ? 7 : prop.shpIndex;
                        int ii = 0;
                        for (; ii < 8; ii += 1)
                        {
                            if (flyingObjects[ii].enable == 0) continue;
                            if (flyingObjects[ii].item == s) break;
                        }
                        var fis = @static.FlyingObjectShp[shpIndex];
                        if (fis.flipFlags != 0 && ((i.x ^ i.y) & 0x20) != 0) flg |= 0x20;
                        flg |= fis.drawFlags;
                        if (ii != 8)
                        {
                            switch (currentDirection - (flyingObjects[ii].direction >> 1) + 3)
                            {
                                case 1: case 5: shpIndex = fis.shapeFront; break;
                                case 3: shpIndex = fis.shapeBack; break;
                                case 2: case 6: flg |= 0x10; shpIndex = fis.shapeLeft; break;
                                case 0: case 4: shpIndex = fis.shapeLeft; break;
                                default: break;
                            }
                            shp = Monsters_at(thrownShapes, shpIndex);
                        }
                        if (shp != null) fy += shp.height >> 2;
                    }
                    else
                    {
                        shp = (prop.flags & 0x40) != 0 ? Monsters_at(gameShapes, prop.shpIndex) : Monsters_at(itemShapes, @static.GameShapeMap[prop.shpIndex << 1]);
                    }
                    if (shp != null) drawItemOrMonster(shp, null, i.x, i.y, fx, fy, flg, -1, false);
                    s = i.nextDrawObject;
                }
            }
        }

        public void drawMonster(int id)
        {
            var m = monsters[id];
            int flg = @static.MonsterDirFlags[(currentDirection << 2) + m.facing];
            int curFrm = getMonsterCurFrame(m, flg & 0xffef);
            Shape shp;
            if (curFrm == -1)
            {
                shp = monsterShapes[m.properties.shapeIndex << 4];
                calcDrawingLayerParameters(m.x + (sbyte)@static.MonsterShiftOffsets[m.shiftStep << 1], m.y + (sbyte)@static.MonsterShiftOffsets[(m.shiftStep << 1) + 1], shp, false);
            }
            else
            {
                int d = m.flags & 7;
                bool flip = (m.properties.flags & 0x200) != 0;
                flg &= 0x10;
                shp = monsterShapes[(m.properties.shapeIndex << 4) + curFrm];
                if ((m.properties.flags & 0x800) != 0) flg |= 0x20;
                byte[] monsterPalette = null;
                if (d != 0)
                {
                    var pal = monsterPalettes[(m.properties.shapeIndex << 4) + (curFrm & 0x0f)];
                    int n = shp.colorCount;
                    monsterPalette = Js.Slice(pal, n * (d - 1), n * (d - 1) + n);
                }
                int sx = (m.x + (sbyte)@static.MonsterShiftOffsets[m.shiftStep << 1]) & 0xffff;
                int sy = (m.y + (sbyte)@static.MonsterShiftOffsets[(m.shiftStep << 1) + 1]) & 0xffff;
                drawBoost = m.dungeonBoss != 0 ? 384 : 0;
                var brightnessOverlay = drawItemOrMonster(shp, monsterPalette, sx, sy, 0, 0, flg | 1, -1, flip);
                drawBoost = 0;
                // Screen box of the sprite for the host interface (damage numbers, health bars).
                m.drawW = LolShapes.scaledSize(shp.width, dmScaleW);
                m.drawH = LolShapes.scaledSize(shp.height, dmScaleH);
                m.drawX = shpDmX + (m.drawW >> 1);
                m.drawY = shpDmY;
                m.drawSerial = sceneSerial;
                for (int i = 0; i < 4; i += 1)
                {
                    int v = m.equipmentShapes[i] - 1;
                    if (v == -1) break;
                    var shp2 = monsterDecorationShapes[m.properties.shapeIndex * 192 + v * 48 + curFrm * 3];
                    if (shp2 == null) continue;
                    drawDoorOrMonsterEquipment(shp2, null, shpDmX, shpDmY, flg | 1, brightnessOverlay);
                }
            }
            if (m.damageReceived == 0) return;
            int dW = LolShapes.scaledSize(shp.width, dmScaleW) >> 1;
            int dH = LolShapes.scaledSize(shp.height, dmScaleH) >> 1;
            int bloodAmount = m.mode == 13 ? m.fightCurTick << 1 : m.properties.hitPoints / (m.damageReceived & 0x7fff);
            var blood = gameShapes[6];
            int bloodType = m.properties.flags & 0xc000;
            if (bloodType == 0x4000) bloodType = 63;
            else if (bloodType == 0x8000) bloodType = 15;
            else if (bloodType == 0xc000) bloodType = 74;
            else bloodType = 0;
            var tbl = new byte[256];
            for (int i = 0; i < 256; i += 1) tbl[i] = (byte)(i >= 2 && i <= 7 ? (i + bloodType) & 0xff : i);
            dW += m.hitOffsX;
            dH += m.hitOffsY;
            bloodAmount = Math.Max(1, Math.Min(4, bloodAmount));
            int sW = dmScaleW / bloodAmount;
            int sH = dmScaleH / bloodAmount;
            screen.drawShape(sceneDrawPage1, blood, shpDmX + dW, shpDmY + dH, 13, 0x124, new DrawShapeOpts { fadeTable = tbl, fadeLevel = bloodType != 0 ? 1 : 0, scaleW = sW, scaleH = sH });
        }

        public int getMonsterCurFrame(Monster m, int dirFlags)
        {
            int tmp = 0;
            switch (monsterAnimType[m.properties.shapeIndex])
            {
                case 0:
                    if (dirFlags != 0) return m.mode == 13 ? -1 : dirFlags + m.currentSubFrame;
                    if (m.damageReceived != 0) return 12;
                    switch (m.mode - 5)
                    {
                        case 0: return (m.properties.flags & 4) != 0 ? 13 : 0;
                        case 3: return m.fightCurTick + 13;
                        case 6: return 14;
                        case 8: return -1;
                        default: return m.currentSubFrame;
                    }
                case 1:
                    tmp = m.properties.hitPoints;
                    tmp = (tmp * @static.MonsterModifiers1[monsterDifficulty]) >> 8;
                    if (m.hitPoints > tmp >> 1) tmp = 0;
                    else if (m.hitPoints > tmp >> 2) tmp = 4;
                    else tmp = 8;
                    switch (m.mode)
                    {
                        case 8: return m.fightCurTick + tmp;
                        case 11: return 12;
                        case 13: return m.fightCurTick + 12;
                        default: return tmp;
                    }
                case 2:
                    return m.fightCurTick >= 13 ? 13 : m.fightCurTick;
                case 3:
                    switch (m.mode)
                    {
                        case 5: return m.damageReceived != 0 ? 5 : 6;
                        case 8: return m.fightCurTick + 6;
                        case 11: return 5;
                        default: return m.damageReceived != 0 ? 5 : m.currentSubFrame;
                    }
                default:
                    return 0;
            }
        }

        public void reassignDrawObjects(int direction, int itemIndex, LevelBlock l, bool flag)
        {
            if (l.direction != direction)
            {
                l.direction = 5;
                return;
            }
            var newObject = findObject(itemIndex);
            int r = calcObjectPosition(newObject, direction);
            BlockObject prev = null;
            int cur = l.drawObjects;
            while (cur != 0)
            {
                var lastObject = findObject(cur);
                int pos = calcObjectPosition(lastObject, direction);
                if (flag ? pos >= r : pos > r) break;
                prev = lastObject;
                cur = lastObject.nextDrawObject;
            }
            newObject.nextDrawObject = cur;
            if (prev != null) prev.nextDrawObject = itemIndex;
            else l.drawObjects = itemIndex;
        }

        public void redrawSceneItem()
        {
            assignVisibleBlocks(currentBlock, currentDirection);
            screen.fillRect(112, 0, 287, 119, 0, sceneDrawPage1);
            int[] tiles = { 13, 16 };
            for (int i = 0; i < 2; i += 1)
            {
                int tile = tiles[i];
                setLevelShapesDim(tile, 13);
                int s = visibleBlocks[tile].drawObjects;
                int t = (i << 7) + 1;
                while (s != 0)
                {
                    if ((s & 0x8000) != 0) s = monsters[s & 0x7fff].nextDrawObject;
                    else
                    {
                        var item = itemsInPlay[s];
                        if ((item.shpCurFrame_flg & 0x4000) != 0 && checkDrawObjectSpace(item.x, item.y, partyPosX, partyPosY) < 320)
                        {
                            int fx = (sbyte)@static.SceneItemOffs[s & 7] << 1; // int8 table
                            int fy = (sbyte)@static.SceneItemOffs[(s >> 1) & 7] + 5;
                            if (item.flyingHeight > 1) fy -= (item.flyingHeight - 1) * 6;
                            var prop = itemProperties[item.itemPropertyIndex];
                            var shp = (prop.flags & 0x40) != 0 ? Monsters_at(gameShapes, prop.shpIndex) : Monsters_at(itemShapes, @static.GameShapeMap[prop.shpIndex << 1]);
                            drawItemOrMonster(shp, null, item.x, item.y, fx, fy, 0, t, false);
                        }
                        s = item.nextDrawObject;
                        t += 1;
                    }
                }
            }
        }

        // ---- AI ----
        static readonly int[] Monsters_modeFlags = { 1, 0, 1, 3, 3, 0, 0, 3, 4, 1, 0, 0, 4, 0, 0 };

        public async Task updateMonster(Monster monster)
        {
            var flags = Monsters_modeFlags;
            if (monster.mode > 14) return;
            int f = flags[monster.mode];
            if (monster.speedTick++ < monster.properties.speedTotalWaitTicks && (f & 4) == 0) return;
            monster.speedTick = 0;
            if ((monster.properties.flags & 0x40) != 0) monster.hitPoints = Math.Min(monster.properties.hitPoints, monster.hitPoints + rollDice(1, 8));
            if ((monster.flags & 8) != 0)
            {
                monster.destX = partyPosX;
                monster.destY = partyPosY;
            }
            if ((f & 2) != 0)
            {
                if (updateMonsterAdjustBlocks(monster))
                {
                    setMonsterMode(monster, 7);
                    f &= 6;
                }
            }
            if ((f & 1) != 0 && (monster.flags & 0x10) != 0) setMonsterMode(monster, 7);
            // Port addition (Settings -> wounded monsters flee): a badly hurt monster breaks off and runs
            // for its spawn instead of fighting to the death. It comes back once it has healed a little,
            // and never flees while it is dying, stunned or scripted.
            if (fleeingMonsters && dungeon == null && monster.mode < 12 && monster.mode != 11 && monster.properties.hitPoints != 0)
            {
                bool hurt = monster.hitPoints <= monster.properties.hitPoints / 4.0;
                if (hurt && monster.fleeing == 0)
                {
                    // The engine has its own way of running away: flag 8 makes the chase walk pick the
                    // direction *away* from the party, step by step, through the same wall tests as any other
                    // move. Walking it to a far-off destination instead (mode 2) goes down the "monster stuck
                    // in a wall" path in walkMonster, which steps straight through the rock.
                    monster.fleeing = 1;
                    monster.flags |= 8;
                    setMonsterMode(monster, 7);
                    // One that breaks pulls its neighbours with it: anything of its kind within two blocks that
                    // is already hurt gives up the fight too, so a beaten pack falls back together.
                    foreach (var other in monsters)
                    {
                        if (other == monster || other.properties == null || other.hitPoints <= 0 || other.mode >= 12 || other.fleeing != 0) continue;
                        if (other.type != monster.type || getBlockDistance(other.block, monster.block) > 2) continue;
                        if (other.hitPoints > other.properties.hitPoints / 2.0) continue;
                        other.fleeing = 1;
                        other.flags |= 8;
                        setMonsterMode(other, 7);
                    }
                }
                else if (monster.fleeing != 0 && monster.hitPoints > monster.properties.hitPoints / 2.0)
                {
                    // It fights again only once it is properly healed, so one that ran with the pack does not
                    // turn round on the next tick.
                    monster.fleeing = 0;
                    monster.flags &= ~8;
                    setMonsterMode(monster, 7);
                }
            }
            if (monster.mode != 11 && monster.mode != 14)
            {
                if ((random(255) & 3) == 0)
                {
                    monster.shiftStep = (monster.shiftStep + 1) & 0x0f;
                    checkSceneUpdateNeed(monster.block);
                }
            }
            switch (monster.mode)
            {
                case 0:
                case 1:
                    if ((monster.flags & 0x10) != 0)
                    {
                        for (int i = 0; i < 30; i += 1) if (monsters[i].mode == 1) setMonsterMode(monsters[i], 7);
                    }
                    else if (monster.mode == 1) moveMonster(monster);
                    break;
                case 2:
                    moveMonster(monster);
                    break;
                case 3:
                    if (updateMonsterAdjustBlocks(monster)) setMonsterMode(monster, 7);
                    for (int i = 0; i < 4; i += 1) if (calcNewBlockPosition(monster.block, i) == currentBlock) setMonsterMode(monster, 7);
                    break;
                case 4:
                    moveStrayingMonster(monster);
                    break;
                case 5:
                    partyAwake = true;
                    monster.fightCurTick -= 1;
                    if (monster.fightCurTick <= 0 || checkDrawObjectSpace(partyPosX, partyPosY, monster.x, monster.y) > 256 || (monster.flags & 8) != 0) setMonsterMode(monster, 7);
                    else alignMonsterToParty(monster);
                    break;
                case 6:
                    if (--monster.fightCurTick <= 0) setMonsterMode(monster, 7);
                    break;
                case 7:
                    if (!await chasePartyWithDistanceAttacks(monster)) chasePartyWithCloseAttacks(monster);
                    checkSceneUpdateNeed(monster.block);
                    break;
                case 8:
                    if (++monster.fightCurTick > 2)
                    {
                        setMonsterMode(monster, 5);
                        // JS: float division, then `* mod >> 8 << 24 >> 24` (a zero speed gives Infinity -> 0)
                        monster.fightCurTick = (sbyte)(Monsters_toInt32(Math.Truncate(2048.0 / monster.properties.fightingStats[4]) * @static.MonsterModifiers3[monsterDifficulty]) >> 8);
                        monster.attackWait = monster.fightCurTick; // host: cooldown ring on the health bar
                    }
                    checkSceneUpdateNeed(monster.block);
                    break;
                case 9:
                    if (--monster.fightCurTick != 0) chasePartyWithCloseAttacks(monster);
                    else
                    {
                        setMonsterMode(monster, 7);
                        monster.flags &= 0xfff7;
                    }
                    break;
                case 12:
                    checkSceneUpdateNeed(monster.block);
                    if (++monster.fightCurTick > 13) await runLevelScriptCustom(0x404, -1, monster.id, monster.id, 0, 0);
                    break;
                case 13:
                    if (++monster.fightCurTick > 2) killMonster(monster);
                    checkSceneUpdateNeed(monster.block);
                    break;
                case 14:
                    monster.damageReceived = 0;
                    break;
                default:
                    break;
            }
            if (monster.damageReceived != 0)
            {
                if ((monster.damageReceived & 0x8000) != 0) monster.damageReceived &= 0x7fff;
                else monster.damageReceived = 0;
                checkSceneUpdateNeed(monster.block);
            }
            monster.flags &= 0xffef;
        }

        static readonly int[] Monsters_turnPos = { 0, 2, 6, 6, 0, 2, 4, 4, 2, 2, 4, 6, 0, 0, 4, 6, 0 };

        public void moveMonster(Monster monster)
        {
            var turnPos = Monsters_turnPos;
            if (monster.x != monster.destX || monster.y != monster.destY) walkMonster(monster);
            else if (monster.direction != monster.destDirection)
            {
                setMonsterDirection(monster, turnPos[(monster.facing << 2) + (monster.destDirection >> 1)]);
            }
        }

        public void walkMonster(Monster monster)
        {
            if ((monster.properties.flags & 0x400) != 0) return;
            int s = walkMonsterCalcNextStep(monster);
            if (s == -1)
            {
                // The unstick step below walks through whatever is in the way (it exists for a monster that
                // ended up inside a wall). One that is merely cornered while running away stays put.
                if (monster.fleeing != 0) return;
                if (walkMonsterCheckDest(monster.x, monster.y, monster, 4) != 1) return;
                objectLastDirection ^= 4;
                setMonsterDirection(monster, objectLastDirection);
            }
            else
            {
                setMonsterDirection(monster, s);
                if (monster.numDistAttacks != 0)
                {
                    if (getBlockDistance(monster.block, currentBlock) >= 2)
                    {
                        if (checkForPossibleDistanceAttack(monster.block, monster.direction, 3, currentBlock) != 5)
                        {
                            if (monster.distAttackTick != 0) return;
                        }
                    }
                }
            }
            var (fx, fy) = getNextStepCoords(monster.x, monster.y, s == -1 ? objectLastDirection : s);
            placeMonster(monster, fx, fy);
        }

        public async Task<bool> chasePartyWithDistanceAttacks(Monster monster)
        {
            if (monster.numDistAttacks == 0) return false;
            if (monster.distAttackTick > 0)
            {
                monster.distAttackTick -= 1;
                return false;
            }
            int dir = checkForPossibleDistanceAttack(monster.block, monster.facing, 4, currentBlock);
            if (dir == 5) return false;
            int s = 0;
            if ((monster.flags & 0x10) != 0) s = monster.properties.numDistWeapons != 0 ? rollDice(1, monster.properties.numDistWeapons) : 0;
            else
            {
                s = monster.curDistWeapon++;
                if (monster.curDistWeapon >= monster.properties.numDistWeapons) monster.curDistWeapon = 0;
            }
            // JS: distWeapons[3] (a roll of 1..3 can go one past the end) is undefined, which acts as 0 below.
            int flyingObject = s < monster.properties.distWeapons.Length ? monster.properties.distWeapons[s] : 0;
            if ((flyingObject & 0xc000) != 0)
            {
                if (getBlockDistance(monster.block, currentBlock) > 1)
                {
                    int type = (flyingObject & 0x4000) != 0 ? 0 : 1;
                    flyingObject = makeItem(flyingObject & 0x3fff, 0, 0);
                    if (flyingObject != 0)
                    {
                        if (!await launchObject(type, flyingObject, monster.x, monster.y, 12, dir << 1, -1, monster.id | 0x8000, 0x3f)) deleteItem(flyingObject);
                    }
                }
            }
            else if ((flyingObject & 0x2000) == 0)
            {
                if (getBlockDistance(monster.block, currentBlock) > 1) return false;
                if (flyingObject == 1)
                {
                    snd_playSoundEffect(147, -1);
                    await shakeScene(10, 2, 2, 1);
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((characters[i].flags & 1) == 0) continue;
                        int item = removeCharacterItem(i, 15);
                        if (item != 0) await setItemPosition(item, partyPosX, partyPosY, 0, 1);
                        inflictDamage(i, 20, 0xffff, 0, 2);
                    }
                }
                else if (flyingObject == 3)
                {
                    for (int i = 0; i < 30; i += 1) if (getBlockDistance(monster.block, monsters[i].block) < 7) setMonsterMode(monster, 7);
                    txt.printMessage(2, getLangString(0x401a));
                }
                else if (flyingObject == 4)
                {
                    await launchMagicViper();
                }
                else return false;
            }
            if (monster.numDistAttacks != 255) monster.numDistAttacks -= 1;
            monster.distAttackTick = (monster.properties.fightingStats[4] * 8) >> 8;
            return true;
        }

        public void chasePartyWithCloseAttacks(Monster monster)
        {
            if ((monster.flags & 8) == 0)
            {
                int dir = calcMonsterDirection(monster.x & 0xff00, monster.y & 0xff00, partyPosX & 0xff00, partyPosY & 0xff00);
                var (x1, y1) = calcSpriteRelPosition(monster.x, monster.y, partyPosX, partyPosY, dir >> 1);
                if (y1 <= 160 && Math.Abs(x1) <= 80)
                {
                    if (monster.direction == dir && monster.facing == dir >> 1)
                    {
                        int dst = getNearestPartyMemberFromPos(monster.x, monster.y);
                        snd_playSoundEffect(monster.properties.sounds[1], -1);
                        int m = monster.id | 0x8000;
                        int hit = battleHitSkillTest(m, dst, 0);
                        if (hit != 0)
                        {
                            int mx = calcInflictableDamage(m, dst, hit);
                            int dmg = rollDice(2, mx);
                            if (monster.ngplus != 0) dmg = Js.Round(dmg * (1 + 0.25 * monster.ngplus)); // New game+
                            if (monster.pitScale != 0) dmg = Math.Max(1, Js.Round(dmg * monster.pitScale)); // a floor of the imp's pit
                            inflictDamage(dst, dmg, m, 0, 0);
                            applyMonsterAttackSkill(monster, dst, dmg);
                        }
                        setMonsterMode(monster, 8);
                        checkSceneUpdateNeed(monster.block);
                    }
                    else
                    {
                        setMonsterDirection(monster, dir);
                        checkSceneUpdateNeed(monster.block);
                    }
                    return;
                }
            }
            if (monster.x != monster.destX || monster.y != monster.destY) walkMonster(monster);
            else
            {
                setMonsterDirection(monster, monster.destDirection);
                setMonsterMode(monster, rollDice(1, 100) <= 50 ? 4 : 3);
            }
        }

        static readonly int[] Monsters_stepTable1 = { 7, -6, 5, -4, 3, -2, 1, 0 };
        static readonly int[] Monsters_stepTable2 = { -7, 6, -5, 4, -3, 2, -1, 0 };

        public int walkMonsterCalcNextStep(Monster monster)
        {
            var table1 = Monsters_stepTable1;
            var table2 = Monsters_stepTable2;
            if (++monsterStepCounter > 10)
            {
                monsterStepCounter = 0;
                monsterStepMode ^= 1;
            }
            var tbl = monsterStepMode != 0 ? table2 : table1;
            int s = monster.direction;
            int d = calcMonsterDirection(monster.x, monster.y, monster.destX, monster.destY);
            if ((monster.flags & 8) != 0) d ^= 4;
            d = (d - s) & 7;
            if (d >= 5) s = (s - 1) & 7;
            else if (d != 0) s = (s + 1) & 7;
            for (int i = 7; i > -1; i -= 1)
            {
                s = (s + tbl[i]) & 7;
                var (fx, fy) = getNextStepCoords(monster.x, monster.y, s);
                d = walkMonsterCheckDest(fx, fy, monster, 4);
                if (d == 0) return s;
                if (d != 1 || (s & 1) != 0 || (monster.properties.flags & 0x80) == 0) continue;
                int w = levelBlockProperties[monsterCurBlock].walls[(s >> 1) ^ 2];
                if ((wllWallFlags[w] & 0x20) != 0 && specialWallTypes[w] == 5)
                {
                    openCloseDoor(monsterCurBlock, 1);
                    return -1;
                }
                if ((wllWallFlags[w] & 8) != 0) return -1;
            }
            return -1;
        }

        public int checkForPossibleDistanceAttack(int monsterBlock, int direction, int distance, int curBlock)
        {
            if (getBlockDistance(curBlock, monsterBlock) > distance) return 5;
            int dir = calcMonsterDirection(monsterBlock & 0x1f, monsterBlock >> 5, curBlock & 0x1f, curBlock >> 5);
            if ((dir & 1) != 0 || dir != direction << 1) return 5;
            if ((monsterBlock & 0x1f) != (curBlock & 0x1f) && (monsterBlock & 0xffe0) != (curBlock & 0xffe0)) return 5;
            if (distance < 0 || direction > 3) return 5;
            int p = monsterBlock;
            for (int i = 0; i < distance; i += 1)
            {
                p = calcNewBlockPosition(p, direction);
                if (p == curBlock) return direction;
                if ((wllWallFlags[levelBlockProperties[p].walls[direction ^ 2]] & 2) != 0) return 5;
                if ((levelBlockProperties[p].assignedObjects & 0x8000) != 0) return 5;
            }
            return 5;
        }

        public int walkMonsterCheckDest(int x, int y, Monster monster, int unk)
        {
            int m = monster.mode;
            monster.mode = 15;
            int objType = checkBlockBeforeObjectPlacement(x, y, monster.properties.maxWidth, 7, (monster.properties.flags & 0x1000) != 0 ? 32 : unk);
            monster.mode = m;
            return objType;
        }

        public (int, int) getNextStepCoords(int srcX, int srcY, int direction)
        {
            return ((srcX + Monsters_STEP_X[direction]) & 0x1fff, (srcY + Monsters_STEP_Y[direction]) & 0x1fff);
        }

        public void alignMonsterToParty(Monster monster)
        {
            int mdir = monster.direction >> 1;
            int mx = monster.x;
            int my = monster.y;
            bool useY = (mdir & 1) != 0;
            int pos = useY ? my : mx;
            bool centered = (pos & 0x7f) == 0;
            bool posFlag = true;
            if (monster.properties.maxWidth <= 63)
            {
                if (centered)
                {
                    bool r = false;
                    if ((monster.nextAssignedObject & 0x8000) != 0) r = true;
                    else
                    {
                        int id = levelBlockProperties[monster.block].assignedObjects;
                        id = (id & 0x8000) != 0 ? id & 0x7fff : 0xffff;
                        if (id != monster.id) r = true;
                        else
                        {
                            for (int i = 0; i < 3; i += 1)
                            {
                                mdir = (mdir + 1) & 3;
                                id = levelBlockProperties[calcNewBlockPosition(monster.block, mdir)].assignedObjects;
                                id = (id & 0x8000) != 0 ? id & 0x7fff : 0xffff;
                                if (id != 0xffff) { r = true; break; }
                            }
                        }
                    }
                    if (r) posFlag = false;
                }
                else posFlag = false;
            }
            if (centered && posFlag) return;
            if (posFlag) pos = (pos & 0x80) != 0 ? pos - 32 : pos + 32;
            else pos = (pos & 0x80) != 0 ? pos + 32 : pos - 32;
            pos &= 0xffff;
            if (useY) my = pos;
            else mx = pos;
            if (walkMonsterCheckDest(mx, my, monster, 4) != 0) return;
            var (fx, fy) = calcSpriteRelPosition(mx, my, partyPosX, partyPosY, monster.direction >> 1);
            if (fy > 160 || Math.Abs(fx) > 80) return;
            placeMonster(monster, mx, my);
        }

        public void moveStrayingMonster(Monster monster)
        {
            if (monster.fightCurTick != 0)
            {
                int d = (monster.direction - monster.fightCurTick) & 6;
                int id = monster.id;
                for (int i = 0; i < 7; i += 1)
                {
                    var (x, y) = getNextStepCoords(monster.x, monster.y, d);
                    if (walkMonsterCheckDest(x, y, monster, 4) == 0)
                    {
                        placeMonster(monster, x, y);
                        setMonsterDirection(monster, d);
                        if (i == 0 && ++id > 3) monster.fightCurTick = 0;
                        return;
                    }
                    d = (d + monster.fightCurTick) & 6;
                }
                setMonsterMode(monster, 3);
            }
            else
            {
                monster.direction &= 6;
                var (x, y) = getNextStepCoords(monster.x, monster.y, monster.direction);
                if (walkMonsterCheckDest(x, y, monster, 4) == 0) placeMonster(monster, x, y);
                else
                {
                    monster.fightCurTick = random(1) != 0 ? 2 : -2;
                    monster.direction = (monster.direction + monster.fightCurTick) & 6;
                }
            }
        }

        public void killMonster(Monster monster)
        {
            setMonsterMode(monster, 14);
            monsterDropItems(monster);
            checkSceneUpdateNeed(monster.block);
            int w = levelBlockProperties[monster.block].walls[0];
            int f = levelBlockProperties[monster.block].flags;
            if (wllVmpMap[w] == 0 && wllShapeMap[w] == 0 && (f & 0x40) == 0 && (monster.properties.flags & 0x1000) == 0) levelBlockProperties[monster.block].flags |= 0x80;
            placeMonster(monster, 0, 0);
        }
    }
}
