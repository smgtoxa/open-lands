// Level data: the wall table, the block map, and the two files that make the 3D view - VCN (8x8
// tiles) and VMP (which tiles to stamp for each wall slot). Transliterated from
// src/formats/lol-scene.mjs and the loaders in src/game/scene.mjs.
namespace LolCore;

/// <summary>LEVEL&lt;n&gt;.WLL: what every wall type looks like and how it behaves.</summary>
public sealed class WallData
{
    public int DecorationSet;
    public readonly byte[] VmpMap = new byte[256];
    public readonly short[] ShapeMap = new short[256];
    public readonly byte[] WallFlags = new byte[256];
    public readonly byte[] SpecialTypes = new byte[256];
    public readonly byte[] Automap = new byte[256];
    /// <summary>
    /// Every record in file order, as (wall type, raw decoration value). A wall type can appear more
    /// than once - the later record wins - and the order is what decides which decoration gets which
    /// id, so the list is kept rather than a set.
    /// </summary>
    public readonly List<(int Wall, short Shape)> Records = new();

    public static WallData Decode(byte[] buffer)
    {
        var data = new WallData();
        DecodeInto(data, buffer);
        return data;
    }

    /// <summary>
    /// The engine keeps one wall table for the whole game: a level's .WLL overwrites the entries it
    /// lists and leaves the rest alone, so a wall type a level never mentions still has whatever the
    /// last level that did mention it put there. Levels rely on that.
    /// </summary>
    public static void DecodeInto(WallData data, byte[] buffer)
    {
        var reader = new BinaryReader(buffer);
        data.DecorationSet = reader.U16Le();
        if ((buffer.Length - 2) % 12 != 0) throw new InvalidDataException("Invalid WLL record table");
        while (reader.Offset < buffer.Length)
        {
            int wall = reader.U16Le();
            if (wall > 255) throw new InvalidDataException($"Invalid wall index: {wall}");
            data.VmpMap[wall] = reader.U8();
            reader.U8();
            data.ShapeMap[wall] = reader.I16Le();
            data.Records.Add((wall, data.ShapeMap[wall]));
            data.SpecialTypes[wall] = reader.U8();
            reader.U8();
            data.WallFlags[wall] = reader.U8();
            reader.U8();
            data.Automap[wall] = reader.U8();
            reader.U8();
        }
    }
}

/// <summary>LEVEL&lt;n&gt;.CMZ: 1024 blocks, each with four wall indices (N, E, S, W).</summary>
public sealed class BlockMap
{
    public readonly byte[,] Walls = new byte[1024, 4];
    public readonly byte[] Flags = new byte[1024];
    public readonly int[] AssignedObjects = new int[1024];  // monsters (0x8000 | id) and, later, items
    public readonly int[] DrawObjects = new int[1024];
    public readonly byte[] Direction = new byte[1024];

    public static BlockMap Decode(byte[] cmz)
    {
        var map = new BlockMap();
        DecodeInto(map, cmz);
        return map;
    }

    /// <summary>loadBlockProperties: the walls come from the file, everything else starts clean.</summary>
    public static void DecodeInto(BlockMap map, byte[] cmz)
    {
        var data = Cps.DecodeBitmapData(cmz).Data;
        int stride = data[4] | (data[5] << 8);
        for (int block = 0; block < 1024; block += 1)
        {
            int at = 6 + block * stride;
            for (int side = 0; side < 4; side += 1) map.Walls[block, side] = data[at + side];
            map.AssignedObjects[block] = 0;
            map.DrawObjects[block] = 0;
            map.Flags[block] = 0;
            map.Direction[block] = 5;
        }
    }
}

/// <summary>&lt;set&gt;.VCN: the 8x8 tiles the walls are built from, as 4-bit indices.</summary>
public sealed class VcnData
{
    public int TileCount;
    public byte[] Shifts;
    public byte[] ColorTable;   // 128 entries
    public byte[] Palette;      // 768, expanded to 8 bit
    public byte[] RawPalette;   // 384, as stored (6 bit)
    public byte[] Tiles;        // TileCount * 32

    public static VcnData Decode(byte[] buffer)
    {
        var data = Cps.DecodeBitmapData(buffer).Data;
        var reader = new BinaryReader(data);
        var vcn = new VcnData { TileCount = reader.U16Le() };
        reader.Ensure(vcn.TileCount + 128 + 384 + vcn.TileCount * 32);

        vcn.Shifts = data.AsSpan(reader.Offset, vcn.TileCount).ToArray();
        reader.Offset += vcn.TileCount;
        vcn.ColorTable = data.AsSpan(reader.Offset, 128).ToArray();
        reader.Offset += 128;

        vcn.RawPalette = data.AsSpan(reader.Offset, 384).ToArray();
        vcn.Palette = new byte[768];
        for (int i = 0; i < 384; i += 1)
        {
            int color = data[reader.Offset + i] & 0x3f;
            vcn.Palette[i] = (byte)((color << 2) | (color & 3));
        }
        reader.Offset += 384;

        vcn.Tiles = data.AsSpan(reader.Offset, vcn.TileCount * 32).ToArray();
        return vcn;
    }
}

/// <summary>&lt;set&gt;.VMP: for each wall type, the rectangle of tile ids to stamp per view slot.</summary>
public static class VmpData
{
    public static ushort[] Decode(byte[] buffer)
    {
        var data = Cps.DecodeBitmapData(buffer).Data;
        var reader = new BinaryReader(data);
        int count = reader.U16Le();
        reader.Ensure(count * 2);
        var words = new ushort[count];
        for (int i = 0; i < count; i += 1) words[i] = reader.U16Le();
        return words;
    }
}
