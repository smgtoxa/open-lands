// src/formats/lol-scene.mjs: level map/wall/VCN/VMP decoders and a standalone scene renderer (LolScene).
using System;

namespace Lol
{
    /// <summary>lol-scene.mjs decodeLevelMap: { width, height, blocks }</summary>
    public sealed class LevelMap
    {
        public int width, height;
        public LevelMapBlock[] blocks;
    }

    /// <summary>lol-scene.mjs decodeLevelMap blocks[i]: { walls }</summary>
    public sealed class LevelMapBlock
    {
        public byte[] walls;
    }

    /// <summary>lol-scene.mjs decodeWallData: { decorationSet, vmpMap, shapeMap, wallFlags, specialTypes, automap }</summary>
    public sealed class WallData
    {
        public int decorationSet;
        public byte[] vmpMap;
        public short[] shapeMap;
        public byte[] wallFlags;
        public byte[] specialTypes;
        public byte[] automap;
    }

    /// <summary>lol-scene.mjs decodeVcn: { tileCount, shifts, colorTable, palette, rawPalette, tiles }</summary>
    public sealed class VcnData
    {
        public int tileCount;
        public byte[] shifts, colorTable, palette, rawPalette, tiles;
    }

    /// <summary>LolScene.door: { block, wall, step }</summary>
    public sealed class SceneDoor
    {
        public int block, wall, step;
    }

    /// <summary>LolScene.decorations: { shapes, records, door, overlays }</summary>
    public sealed class SceneDecorations
    {
        public Shape[] shapes;
        public DecorationProperty[] records;
        public Shape door;
        public byte[][] overlays;
    }

    public static class LolSceneFile
    {
        public static LevelMap decodeLevelMap(byte[] buffer)
        {
            var data = Cps.decodeBitmapData(buffer).data;
            var reader = new BinaryReader(data);
            int width = reader.u16le();
            int height = reader.u16le();
            int recordSize = reader.u16le();
            if (width != 32 || height != 32 || recordSize < 4)
            {
                throw new Exception($"Unsupported level map geometry: {width}x{height}x{recordSize}");
            }
            reader.ensure(width * height * recordSize);
            var blocks = new LevelMapBlock[width * height];
            for (int i = 0; i < blocks.Length; i += 1)
            {
                int offset = reader.offset + i * recordSize;
                blocks[i] = new LevelMapBlock { walls = Js.Slice(data, offset, offset + 4) };
            }
            return new LevelMap { width = width, height = height, blocks = blocks };
        }

        public static WallData decodeWallData(byte[] buffer)
        {
            var reader = new BinaryReader(buffer);
            int decorationSet = reader.u16le();
            if ((reader.bytes.Length - 2) % 12 != 0) throw new Exception("Invalid WLL record table");

            var vmpMap = new byte[256];
            var shapeMap = new short[256];
            var wallFlags = new byte[256];
            var specialTypes = new byte[256];
            var automap = new byte[256];
            while (reader.offset < reader.bytes.Length)
            {
                int wall = reader.u16le();
                if (wall > 255) throw new Exception($"Invalid wall index: {wall}");
                vmpMap[wall] = (byte)reader.u8();
                reader.u8();
                shapeMap[wall] = (short)reader.i16le();
                specialTypes[wall] = (byte)reader.u8();
                reader.u8();
                wallFlags[wall] = (byte)reader.u8();
                reader.u8();
                automap[wall] = (byte)reader.u8();
                reader.u8();
            }
            return new WallData { decorationSet = decorationSet, vmpMap = vmpMap, shapeMap = shapeMap, wallFlags = wallFlags, specialTypes = specialTypes, automap = automap };
        }

        public static VcnData decodeVcn(byte[] buffer)
        {
            var data = Cps.decodeBitmapData(buffer).data;
            var reader = new BinaryReader(data);
            int tileCount = reader.u16le();
            reader.ensure(tileCount + 128 + 384 + tileCount * 32);
            var shifts = Js.Slice(data, reader.offset, reader.offset + tileCount);
            reader.offset += tileCount;
            var colorTable = Js.Slice(data, reader.offset, reader.offset + 128);
            reader.offset += 128;
            var rawPalette = Js.Slice(data, reader.offset, reader.offset + 384);
            var palette = new byte[768];
            for (int i = 0; i < 384; i += 1)
            {
                int color = data[reader.offset + i] & 0x3f;
                palette[i] = (byte)((color << 2) | (color & 3));
            }
            reader.offset += 384;
            var tiles = Js.Slice(data, reader.offset, reader.offset + tileCount * 32);
            return new VcnData { tileCount = tileCount, shifts = shifts, colorTable = colorTable, palette = palette, rawPalette = rawPalette, tiles = tiles };
        }

        public static ushort[] decodeVmp(byte[] buffer)
        {
            var data = Cps.decodeBitmapData(buffer).data;
            var reader = new BinaryReader(data);
            int count = reader.u16le();
            reader.ensure(count * 2);
            var words = new ushort[count];
            for (int i = 0; i < count; i += 1) words[i] = (ushort)reader.u16le();
            return words;
        }
    }

    public sealed class LolScene
    {
        static readonly byte[] BLOCK_MAP = new byte[]
        {
            2, 3, 0, 1, 1, 2, 3, 0, 3, 0, 1, 2,
        };
        static readonly sbyte[] BLOCK_INDEX = new sbyte[]
        {
            -99, -98, -97, -96, -95, -94, -93, -66, -65, -64, -63, -62, -33, -32, -31, -1, 0, 1, -93, -61, -29, 3, 35, 67,
            99, -62, -30, 2, 34, 66, -31, 1, 33, -32, 0, 32, 99, 98, 97, 96, 95, 94, 93, 66, 65, 64, 63, 62,
            33, 32, 31, 1, 0, -1, 93, 61, 29, -3, -35, -67, -99, 62, 30, -2, -34, -66, 31, -1, -33, 32, 0, -32,
        };
        static readonly ushort[] VMP_OFFSETS = new ushort[]
        {
            102, 97, 129, 117, 81, 159, 45, 239, 0,
        };
        static readonly short[] MOVE_OFFSETS = new short[]
        {
            -32, 1, 32, -1,
        };
        // Scene shape tables from ScummVM's LoL CD static resources (kLoLDsc*).
        static readonly byte[] TILE_INDEX = new byte[]
        {
            0, 6, 1, 5, 2, 4, 3, 7, 11, 8, 10, 9, 12, 14, 13, 15, 17, 16,
        };
        static readonly byte[] DIM_MAP = new byte[]
        {
            0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3,
        };
        static readonly byte[] OVL_INDEX = new byte[]
        {
            0, 0, 1, 1, 1, 2, 2, 3, 3, 3, 4, 4, 5, 5, 5, 6, 6, 7, 7, 7,
        };
        static readonly byte[] OVL_MAP = new byte[]
        {
            0, 1, 1, 1, 4, 5, 5, 5, 5, 4,
        };
        static readonly sbyte[] SHAPE_WALLS = new sbyte[]
        {
            -1, -1, 2, 1, 2, 1, 2, -1, 2, 3, 2, 3, -1, -1, 2, 1, 2, 1, 2, -1, 2, 3, 2, 3,
            2, 1, 2, -1, 2, 3, -1, 1, 2, -1, -1, 3,
        };
        static readonly sbyte[] SHAPE_INDEX = new sbyte[]
        {
            3, 9, 3, 8, 3, 7, 3, 3, 3, -7, 3, -8, 3, -9, 2, 9, 2, 6, 2, 2, 2, -6, 2, -9,
            1, 5, 1, 1, 1, -5, 0, 4, 0, 0, 0, -4,
        };
        static readonly ushort[] SHAPE_SCALE_W = new ushort[]
        {
            0x60, 0, 0x60, 0xce, 0x60, 0x55, 0x60, 0, 0x60, 0x55, 0x60, 0xce, 0x60, 0, 0xa0, 0xae, 0xa0, 0xaa, 0xa0, 0, 0xa0, 0xaa, 0xa0, 0xae,
            0x100, 0x100, 0x100, 0, 0x100, 0x100, 0, 0x100, 0x100, 0, 0, 0x100,
        };
        static readonly ushort[] SHAPE_SCALE_H = new ushort[]
        {
            0x6a, 0, 0x6a, 0x6a, 0x6a, 0x6a, 0x6a, 0, 0x6a, 0x6a, 0x6a, 0x6a, 0x6a, 0, 0xaa, 0x56, 0xaa, 0xaa, 0xaa, 0, 0xaa, 0xaa, 0xaa, 0x56,
            0x100, 0x100, 0x100, 0, 0x100, 0x100, 0, 0x100, 0x100, 0, 0, 0x100,
        };
        static readonly short[] SHAPE_X = new short[]
        {
            -80, -32, -32, 16, 16, 64, 64, 0, 112, 112, 160, 160, 208, 208, -122, 0, -32, 48, 48, 0, 128, 128, 208, 176,
            -104, 24, 24, 0, 152, 152, 0, 0, 0, 0, 176, 176,
        };
        static readonly byte[] SHAPE_Y = new byte[]
        {
            27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 20, 28, 20, 20, 20, 20, 20, 20, 20, 28,
            8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0,
        };
        // Per (drawn block, visible block) horizontal clip in 8-pixel columns: -41 hides
        // the block, -40 is neutral, otherwise a right (positive) or left (negative) bound.
        static readonly sbyte[] DIM1 = new sbyte[]
        {
            -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -40, -41, -40, -40, 2, -40, -40, -40,
            -40, -2, -41, -40, -40, -40, -41, 3, -40, -3, -40, -40, -40, -2, -40, 8, -40, -40, -40, -2, -8, 6, -40, -40,
            -6, 3, -40, -3, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -6, -41, 16, -40, -6, -41, 16, -40, -40, -40,
            -40, -40, -40, -14, -40, 20, -40, -40, -40, -16, 14, 20, -40, -19, 16, -40, -40, 19, -40, -40, -40, -40, -20, -40,
            -40, -40, -40, -40, -41, 20, -40, -19, -41, -40, -40, 19, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41, -41,
            -41, -41, -41, -41, -40, -41, -40, -40, -40, -40, -40, -40, -40, -40, -41, -40, -40, -40, -41, -40, -40, -41, -40, -40,
            -40, -40, -40, -40, -40, -40, -40, -40, -40, 6, -40, -40, -6, 3, -40, -3, -40, -40, -40, -40, -40, -40, -40, -40,
            -40, -40, -6, -40, 16, -40, -6, -41, 16, -3, -40, 19, -40, -40, -40, -40, -40, -40, -40, -40, -40, -16, -40, -40,
            -40, -19, 16, -40, -40, 19, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -41, -40, -40, -40, -41, -40, -40, -41,
            -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, 3, -40, -3, -40, -40, -40, -40, -40, -40, -40, -40,
            -40, -40, -40, -40, -40, -40, -3, -40, 19, -3, -40, 19, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40,
            -40, -19, -40, -40, -40, 19, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40,
            -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40,
            -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40, -40,
        };
        // Same for door walls: left and right bound pairs.
        static readonly byte[] DIM2 = new byte[]
        {
            22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0,
            22, 0, 22, 0, 22, 0, 22, 0, 0, 22, 22, 0, 0, 22, 0, 22, 0, 2, 0, 22, 0, 22, 0, 22,
            0, 22, 2, 22, 0, 4, 0, 22, 0, 22, 0, 22, 22, 0, 0, 3, 0, 22, 3, 22, 0, 22, 0, 22,
            0, 22, 2, 22, 0, 22, 0, 8, 0, 22, 0, 22, 0, 22, 0, 2, 22, 0, 0, 6, 0, 22, 0, 22,
            6, 22, 0, 3, 0, 22, 3, 0, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 6, 22, 8, 14, 0, 16, 0, 22, 3, 22, 6, 16, 0, 19, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 14, 22, 0, 22, 0, 20, 0, 22, 0, 22, 0, 22, 16, 22, 22, 0, 0, 20,
            0, 22, 19, 22, 0, 16, 0, 22, 0, 22, 0, 19, 0, 22, 0, 22, 0, 22, 0, 22, 20, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 18, 22, 20, 22, 0, 22, 19, 22, 22, 0, 0, 22, 0, 22, 0, 19,
            22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0,
            22, 0, 22, 0, 22, 0, 22, 0, 0, 22, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0,
            22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 0, 22, 22, 0,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 6, 0, 22, 0, 22,
            6, 22, 0, 3, 0, 22, 3, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 6, 22, 0, 22, 0, 16, 0, 22, 3, 22, 7, 15, 0, 19, 3, 22, 0, 22, 0, 19,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 16, 22, 0, 22, 0, 22,
            0, 22, 19, 22, 0, 16, 0, 22, 0, 22, 0, 19, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0,
            22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 0, 22, 22, 0,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 3, 0, 22, 3, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 3, 22, 0, 22, 0, 19, 3, 22, 0, 22, 0, 19,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 19, 22, 0, 22, 0, 22, 0, 22, 0, 19, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
            0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22, 0, 22,
        };
        static readonly byte[] DOOR_SCALE_OFFSET = new byte[]
        {
            0, 0, 0, 3, 3, 3, 3, 3, 8, 8, 8, 8, 8, 13, 13, 13, 13, 13, 18, 18, 18, 18, 18,
        };
        static readonly byte[] DOOR_FRAME_Y1 = new byte[]
        {
            0x1e, 0x18, 0x10, 0,
        };
        static readonly byte[] DOOR_FRAME_Y2 = new byte[]
        {
            0x3b, 0x47, 0x56, 0x78,
        };
        static readonly ushort[] DOOR_SCALE = new ushort[]
        {
            0x75, 0x6a, 0xb9, 0xb2, 0x100, 0x100, 0, 0,
        };
        static readonly ushort[] DOOR_LIFT = new ushort[]
        {
            5, 2, 8, 0x787c,
        };
        // kLoLDscDoorX/Y column 10, the only column LoL uses for doors.
        static readonly short[] DOOR_X = new short[]
        {
            -120, -80, -40, 0, 40, 80, 120, -118, -59, 0, 59, 118, -98, 0, 98, -128, 0, 128,
        };
        static readonly short[] DOOR_Y = new short[]
        {
            -61, -61, -61, -61, -61, -61, -61, -50, -50, -50, -50, -50, -30, -30, -30, 0, 0, 0,
        };

        public LevelMap map;
        public WallData walls;
        public VcnData vcn;
        public ushort[] vmp;
        public int block;
        public int direction;
        public SceneDecorations decorations;
        public SceneDoor door;

        public LolScene(LevelMap map, WallData walls, VcnData vcn, ushort[] vmp)
        {
            this.map = map;
            this.walls = walls;
            this.vcn = vcn;
            this.vmp = vmp;
            this.block = 557;
            this.direction = 0;
            this.decorations = null;
            this.door = null;
        }

        // KyraRpgEngine::processDoorSwitch + openCloseDoor for the door ahead: closed
        // doors open, open doors close. Returns true when a door starts moving.
        // ponytail: one moving door at a time; ScummVM tracks three.
        public bool useDoor()
        {
            var flags = this.walls.wallFlags;
            int block = (this.block + MOVE_OFFSETS[this.direction]) & 0x3ff;
            var walls = this.map.blocks[block].walls;
            int wall = (flags[walls[0]] & 8) != 0 ? 0 : 1;
            if ((flags[walls[wall]] & 8) == 0 || this.door != null) return false;
            int step = (flags[walls[wall]] & 1) != 0 ? 1 : -1;
            if ((flags[walls[wall]] & (step == 1 ? 0x10 : 0x20)) != 0) return false;
            this.door = new SceneDoor { block = block, wall = wall, step = step };
            if ((flags[walls[wall]] & (step == 1 ? 0x20 : 0x10)) != 0) this.stepDoor();
            return true;
        }

        // KyraRpgEngine::timerProcessDoors: one animation step every 15 ticks.
        // Returns true while the door is still moving.
        public bool stepDoor()
        {
            if (this.door == null) return false;
            int block = this.door.block, wall = this.door.wall, step = this.door.step;
            var walls = this.map.blocks[block].walls;
            walls[wall] = (byte)(walls[wall] + step);
            walls[wall ^ 2] = (byte)(walls[wall ^ 2] + step);
            if ((this.walls.wallFlags[walls[wall]] & 0x30) != 0) this.door = null;
            return this.door != null;
        }

        // shapes: decoded level SHP; records: decoded level DAT; door: one door shape;
        // overlays: eight 256-entry brightness tables (see generateLevelOverlays).
        public void setDecorations(Shape[] shapes, DecorationProperty[] records, Shape door, byte[][] overlays)
        {
            this.decorations = new SceneDecorations { shapes = shapes, records = records, door = door, overlays = overlays };
        }

        public void turn(int amount)
        {
            this.direction = (this.direction + amount + 4) & 3;
        }

        public bool move(int relativeDirection)
        {
            int direction = (this.direction + relativeDirection + 4) & 3;
            int destination = (this.block + MOVE_OFFSETS[direction]) & 0x3ff;
            int wall = this.map.blocks[destination].walls[direction ^ 2];
            if ((this.walls.wallFlags[wall] & 1) != 0) return false;
            this.block = destination;
            return true;
        }

        public byte[] render()
        {
            var drawing = new ushort[660];
            bool processFlip = (((this.block >> 5) + (this.block & 0x1f) + this.direction) & 1) != 0;
            this._placeTiles(drawing, 0, 15, 1, -330, 22, 15, processFlip);

            var visible = new LevelMapBlock[18];
            for (int i = 0; i < 18; i += 1)
            {
                visible[i] = this.map.blocks[(this.block + BLOCK_INDEX[this.direction * 18 + i]) & 0x3ff];
            }
            int down = BLOCK_MAP[this.direction];
            int right = BLOCK_MAP[this.direction + 4];
            int left = BLOCK_MAP[this.direction + 8];
            int wall(int block, int side) => visible[block].walls[side];
            bool hasWall(int value) => value != 0 && (this.walls.wallFlags[value] & 8) == 0;

            int a = wall(0, right);
            if (a != 0) this._placeTiles(drawing, -2, 3, a, VMP_OFFSETS[0], 3, 5);
            a = wall(6, left);
            if (a != 0) this._placeTiles(drawing, 21, 3, a, VMP_OFFSETS[0], 3, 5, true);

            a = wall(1, right);
            int b = wall(2, down);
            if (hasWall(a) && (this.walls.wallFlags[b] & 8) == 0) this._placeTiles(drawing, 2, 3, a, VMP_OFFSETS[0], 3, 5);
            else if (a != 0 && (this.walls.wallFlags[b] & 8) != 0) this._placeTiles(drawing, 2, 3, b, VMP_OFFSETS[0], 3, 5);
            a = wall(5, left);
            b = wall(4, down);
            if (hasWall(a) && (this.walls.wallFlags[b] & 8) == 0) this._placeTiles(drawing, 17, 3, a, VMP_OFFSETS[0], 3, 5, true);
            else if (a != 0 && (this.walls.wallFlags[b] & 8) != 0) this._placeTiles(drawing, 17, 3, b, VMP_OFFSETS[0], 3, 5, true);

            a = wall(2, right);
            if (a != 0) this._placeTiles(drawing, 8, 3, a, VMP_OFFSETS[1], 1, 5);
            a = wall(4, left);
            if (a != 0) this._placeTiles(drawing, 13, 3, a, VMP_OFFSETS[1], 1, 5, true);
            foreach (var (block, x) in new[] { (1, -4), (5, 20), (2, 2), (4, 14), (3, 8) })
            {
                a = wall(block, down);
                if ((block == 3 && a != 0) || (block != 3 && hasWall(a))) this._placeTiles(drawing, x, 3, a, VMP_OFFSETS[2], 6, 5);
            }

            a = wall(7, right);
            if (a != 0) this._placeTiles(drawing, 0, 3, a, VMP_OFFSETS[3], 2, 6);
            a = wall(11, left);
            if (a != 0) this._placeTiles(drawing, 20, 3, a, VMP_OFFSETS[3], 2, 6, true);
            a = wall(8, right);
            if (a != 0) this._placeTiles(drawing, 6, 2, a, VMP_OFFSETS[4], 2, 8);
            a = wall(10, left);
            if (a != 0) this._placeTiles(drawing, 14, 2, a, VMP_OFFSETS[4], 2, 8, true);
            foreach (var (block, x) in new[] { (8, -4), (10, 16), (9, 6) })
            {
                a = wall(block, down);
                if ((block == 9 && a != 0) || (block != 9 && hasWall(a))) this._placeTiles(drawing, x, 2, a, VMP_OFFSETS[5], 10, 8);
            }

            a = wall(12, right);
            if (a != 0) this._placeTiles(drawing, 3, 1, a, VMP_OFFSETS[6], 3, 12);
            a = wall(14, left);
            if (a != 0) this._placeTiles(drawing, 16, 1, a, VMP_OFFSETS[6], 3, 12, true);
            foreach (var (block, x) in new[] { (12, -13), (14, 19), (13, 3) })
            {
                a = wall(block, down);
                if ((block == 13 && a != 0) || (block != 13 && (this.walls.wallFlags[a] & 8) == 0))
                {
                    this._placeTiles(drawing, x, 1, a, VMP_OFFSETS[7], 16, 12);
                }
            }

            a = wall(15, right);
            b = wall(17, left);
            if (a != 0) this._placeTiles(drawing, 0, 0, a, VMP_OFFSETS[8], 3, 15);
            if (b != 0) this._placeTiles(drawing, 19, 0, b, VMP_OFFSETS[8], 3, 15, true);
            var output = this._drawTiles(drawing);
            if (this.decorations != null) this._drawSceneShapes(output, visible, down, processFlip);
            return output;
        }

        // LoLEngine::drawSceneShapes: decorations then door for each block, far to near.
        public void _drawSceneShapes(byte[] output, LevelMapBlock[] visible, int down, bool processFlip)
        {
            var flags = this.walls.wallFlags;
            for (int i = 0; i < 18; i += 1)
            {
                int index = TILE_INDEX[i];
                var clip = this._shapeClip(index, visible, down);
                if (clip.x2 <= clip.x1) continue;
                this._drawDecorations(output, index, visible, clip, processFlip);

                int wall = visible[index].walls[down];
                if ((flags[wall] & 8) == 0) continue;
                int lift = Math.Min(80, 20 * (wall - (wall < 23 ? DOOR_SCALE_OFFSET[wall] : 0)));
                int dim = DIM_MAP[index];
                this._drawDoor(output, index, -lift, new ShapeClip { x1 = clip.x1, x2 = clip.x2, y1 = DOOR_FRAME_Y1[dim], y2 = DOOR_FRAME_Y2[dim] });
            }
        }

        // KyraRpgEngine::setLevelShapesDim: horizontal clip for block `index` given the
        // facing walls of every visible block. Returned in scene pixels.
        public ShapeClip _shapeClip(int index, LevelMapBlock[] visible, int down)
        {
            int x1 = 0;
            int x2 = 22;
            for (int i = 0; i < 18 && x2 >= x1; i += 1)
            {
                int wall = visible[i].walls[down];
                int m = index * 18 + i;
                if ((this.walls.wallFlags[wall] & 8) != 0)
                {
                    x1 = Math.Max(x1, DIM2[m * 2]);
                    x2 = Math.Min(x2, DIM2[m * 2 + 1]);
                    continue;
                }
                int t = DIM1[m];
                if (this.walls.vmpMap[wall] == 0 || t == -40) continue;
                if (t == -41) return new ShapeClip { x1 = 22, x2 = 0, y1 = 0, y2 = 120 };
                if (t > 0 && x2 > t) x2 = t;
                if (t < 0 && x1 < -t) x1 = -t;
            }
            return new ShapeClip { x1 = x1 * 8, x2 = x2 * 8, y1 = 0, y2 = 120 };
        }

        // LoLEngine::drawDecorations for one visible block.
        public void _drawDecorations(byte[] output, int index, LevelMapBlock[] visible, ShapeClip clip, bool processFlip)
        {
            var shapes = this.decorations.shapes;
            var records = this.decorations.records;
            var overlays = this.decorations.overlays;
            for (int i = 1; i >= 0; i -= 1)
            {
                int s = index * 2 + i;
                int scaleW = SHAPE_SCALE_W[s];
                int scaleH = SHAPE_SCALE_H[s];
                if (scaleW == 0 || scaleH == 0) continue;
                int ix = SHAPE_INDEX[s];
                int shapeSlot = Math.Abs(ix);
                int ovlIndex = Math.Min(7, OVL_INDEX[4 + DIM_MAP[index] * 5] + 2);
                int side = (this.direction + SHAPE_WALLS[s]) & 3;
                int l = this.walls.shapeMap[visible[index].walls[side]];

                while (l > 0)
                {
                    var record = l < records.Length ? records[l] : null;
                    if (record == null) throw new Exception($"Decoration record outside resource: {l}");
                    if ((record.flags & 8) != 0 && index != 3 && index != 9 && index != 13)
                    {
                        l = record.next;
                        continue;
                    }
                    if (OVL_MAP[shapeSlot] == 1 && ((record.flags & 2) != 0 || ((record.flags & 4) != 0 && processFlip))) ix = -ix;

                    int xOffset = 0;
                    int yOffset = 0;
                    var overlay = overlays[7];
                    if ((record.scaleFlag[shapeSlot] & 1) != 0)
                    {
                        xOffset = record.shapeX[shapeSlot];
                        yOffset = record.shapeY[shapeSlot];
                        shapeSlot = OVL_MAP[shapeSlot];
                        overlay = overlays[ovlIndex];
                    }
                    else if (record.shapeIndex[shapeSlot] != 0xffff)
                    {
                        scaleW = scaleH = 0x100;
                    }

                    int shapeIndex = record.shapeIndex[shapeSlot];
                    if (shapeIndex != 0xffff)
                    {
                        var shape = shapeIndex < shapes.Length ? shapes[shapeIndex] : null;
                        if (shape == null) throw new Exception($"Decoration shape outside resource: {shapeIndex}");
                        int offsetX = LolShapes.scaledSize(record.shapeX[shapeSlot], scaleW);
                        bool flip = ix < 0;
                        int x = SHAPE_X[s] + xOffset + offsetX;
                        if (ix < 0 && ix == SHAPE_INDEX[s]) x = SHAPE_X[s] - offsetX - LolShapes.scaledSize(shape.width, scaleW) - xOffset;
                        int y = SHAPE_Y[s] + yOffset + LolShapes.scaledSize(record.shapeY[shapeSlot], scaleH);
                        LolShapes.drawShape(output, 176, shape, x, y, clip, flip, overlay, scaleW, scaleH);
                        if ((record.flags & 1) != 0 && shapeSlot < 4)
                        {
                            x += LolShapes.scaledSize(shape.width, scaleW);
                            flip = !flip;
                            LolShapes.drawShape(output, 176, shape, x, y, clip, flip, overlay, scaleW, scaleH);
                        }
                    }
                    l = record.next;
                    shapeSlot = Math.Abs((int)SHAPE_INDEX[s]);
                }
            }
        }

        // LoLEngine::drawDoor with the fixed arguments drawSceneShapes passes.
        public void _drawDoor(byte[] output, int index, int lift, ShapeClip clip)
        {
            var door = this.decorations.door;
            var overlays = this.decorations.overlays;
            int dim = DIM_MAP[index];
            int scaleW = DOOR_SCALE[dim * 2];
            int scaleH = DOOR_SCALE[dim * 2 + 1];
            if (scaleW == 0 || scaleH == 0) return;
            var overlay = overlays[Math.Min(7, OVL_INDEX[2 + dim * 5] + 2)];
            int half = LolShapes.scaledSize(door.height, scaleH) >> 1;
            int x = DOOR_X[index] + 88 - (LolShapes.scaledSize(door.width, scaleW) >> 1);
            int y = DOOR_Y[index] + 124 - half + LolShapes.scaledSize(lift, scaleH) - DOOR_LIFT[dim] - half;
            LolShapes.drawShape(output, 176, door, x, y, clip, false, overlay, scaleW, scaleH);
        }

        public void _placeTiles(ushort[] drawing, int startX, int startY, int wall, int offset, int width, int height, bool flip = false)
        {
            int mapIndex = this.walls.vmpMap[wall];
            if (mapIndex == 0) return;
            int sourceStart = (mapIndex - 1) * 431 + offset + 330;
            for (int y = 0; y < height; y += 1)
            {
                for (int x = 0; x < width; x += 1)
                {
                    int targetX = startX + x;
                    int sourceX = flip ? width - 1 - x : x;
                    int source = sourceStart + y * width + sourceX;
                    if (source < 0 || source >= this.vmp.Length) throw new Exception("VMP mapping outside resource");
                    int tile = this.vmp[source];
                    if (targetX < 0 || targetX >= 22 || tile == 0) continue;
                    if (flip) tile ^= 0x4000;
                    drawing[(startY + y) * 22 + targetX] = (ushort)tile;
                }
            }
        }

        public byte[] _drawTiles(ushort[] drawing)
        {
            var output = new byte[176 * 120];
            for (int cell = 0; cell < 330; cell += 1)
            {
                int tile = drawing[cell];
                int overlay = 0;
                if ((tile & 0x8000) != 0)
                {
                    overlay = tile & 0x7fff;
                    tile = 0;
                }
                bool floor = tile == 0;
                if (floor) tile = drawing[cell + 330];
                this._drawTile(output, cell, tile, false);
                if (overlay != 0) this._drawTile(output, cell, overlay, true);
            }
            return output;
        }

        public void _drawTile(byte[] output, int cell, int encodedTile, bool transparent)
        {
            bool flipped = (encodedTile & 0x4000) != 0;
            int tile = encodedTile & 0x3fff;
            if (tile >= this.vcn.tileCount) throw new Exception($"VCN tile outside resource: {tile}");
            int source = tile * 32;
            int targetX = (cell % 22) * 8;
            int targetY = Js.FloorDiv(cell, 22) * 8;
            int shift = this.vcn.shifts[tile];
            for (int y = 0; y < 8; y += 1)
            {
                for (int x = 0; x < 8; x += 1)
                {
                    int sourceX = flipped ? 7 - x : x;
                    int packed = this.vcn.tiles[source + y * 4 + (sourceX >> 1)];
                    int nibble = (sourceX & 1) != 0 ? packed & 0x0f : packed >> 4;
                    byte color = this.vcn.colorTable[nibble | shift];
                    if (!transparent || color != 0) output[(targetY + y) * 176 + targetX + x] = color;
                }
            }
        }
    }
}
