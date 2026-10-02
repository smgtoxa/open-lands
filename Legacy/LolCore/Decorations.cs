// Wall decorations: the torches, doors frames, signs and clutter stuck to the level's walls.
//
// The level's WLL table points at records in a <set>.DAT file, each naming up to ten shapes in a
// <set>.SHP file; the engine assigns them lazily, in encounter order, and that order is part of the
// state (wllShapeMap holds the assigned id, not the file's). Transliterated from
// src/game/scene.mjs loadLevelShpDat/assignLevelDecorationShapes/getLevelDecorationShapes and
// src/formats/lol-shapes.mjs decodeDecorationData.
namespace LolCore;

public sealed class DecorationRecord
{
    public readonly ushort[] ShapeIndex = new ushort[10];
    public readonly byte[] ScaleFlag = new byte[10];
    public readonly short[] ShapeX = new short[10];
    public readonly short[] ShapeY = new short[10];
    public int Next;
    public int Flags;

    public static DecorationRecord[] Decode(byte[] buffer)
    {
        var reader = new BinaryReader(buffer);
        int count = reader.U16Le();
        var records = new DecorationRecord[count];
        for (int i = 0; i < count; i += 1)
        {
            var record = new DecorationRecord();
            for (int k = 0; k < 10; k += 1) record.ShapeIndex[k] = (ushort)reader.U16Le();
            for (int k = 0; k < 10; k += 1) record.ScaleFlag[k] = (byte)reader.U8();
            for (int k = 0; k < 10; k += 1) record.ShapeX[k] = (short)reader.U16Le();
            for (int k = 0; k < 10; k += 1) record.ShapeY[k] = (short)reader.U16Le();
            record.Next = (sbyte)reader.U8();
            record.Flags = reader.U8();
            records[i] = record;
        }
        return records;
    }

    public DecorationRecord Clone()
    {
        var copy = new DecorationRecord { Next = Next, Flags = Flags };
        Array.Copy(ShapeIndex, copy.ShapeIndex, 10);
        Array.Copy(ScaleFlag, copy.ScaleFlag, 10);
        Array.Copy(ShapeX, copy.ShapeX, 10);
        Array.Copy(ShapeY, copy.ShapeY, 10);
        return copy;
    }
}

public sealed class Decorations
{
    private readonly Resources _res;

    private DecorationRecord[] _data = Array.Empty<DecorationRecord>();
    private byte[] _shpFile = Array.Empty<byte>();
    private string _shpName = "";
    private int _shapeCount;
    private readonly int[] _map1 = new int[400];   // file shape -> assigned shape slot
    private readonly int[] _map2 = new int[400];   // file record -> assigned property slot
    private int _mapped = 1;
    private int _shapeIndex = 1;

    /// <summary>The assigned records, indexed the way wllShapeMap points at them.</summary>
    public readonly DecorationRecord[] Properties = new DecorationRecord[400];
    public readonly Shape[] Shapes = new Shape[400];

    public Decorations(Resources resources) => _res = resources;

    /// <summary>loadLevelShpDat: open a wall set's shape and data files.</summary>
    public void LoadSet(string shpFile, string datFile, bool keepAssignments)
    {
        Array.Clear(_map1, 0, _map1.Length);
        Array.Clear(_map2, 0, _map2.Length);
        // Level packs are named after the SHP file, except YVEL1.SHP which ships in YVEL.PAK.
        if (!_res.Exists(shpFile))
        {
            string pak = shpFile.Replace(".SHP", ".PAK", StringComparison.OrdinalIgnoreCase);
            if (pak.Equals("YVEL1.PAK", StringComparison.OrdinalIgnoreCase)) pak = "YVEL.PAK";
            _res.LoadPak(pak);
        }
        _shpFile = _res.Get(shpFile);
        _shpName = shpFile;
        _shapeCount = _shpFile[0] | (_shpFile[1] << 8);
        _data = DecorationRecord.Decode(_res.Get(datFile));
        if (!keepAssignments)
        {
            _mapped = 1;
            _shapeIndex = 1;
        }
    }

    /// <summary>assignLevelDecorationShapes: hand out ids in encounter order, following `next`.</summary>
    public int Assign(int index)
    {
        if (index < 0 || index >= _data.Length) return 0;
        if (_map2[index] != 0) return _map2[index];
        int slot = _mapped++;
        var record = _data[index].Clone();
        Properties[slot] = record;
        for (int i = 0; i < 10; i += 1)
        {
            int shape = record.ShapeIndex[i];
            if (shape == 0xffff) continue;
            if (_map1[shape] != 0) record.ShapeIndex[i] = (ushort)_map1[shape];
            else
            {
                Shapes[_shapeIndex] = DecodeShape(shape);
                _map1[shape] = _shapeIndex;
                record.ShapeIndex[i] = (ushort)_shapeIndex++;
            }
        }
        _map2[index] = slot;
        if (record.Next != 0) record.Next = Assign(record.Next);
        return slot;
    }

    private Shape DecodeShape(int shapeIndex)
    {
        if (_shapeCount <= shapeIndex) return null;
        var f = _shpFile;
        int offset = (f[shapeIndex * 4 + 2] | (f[shapeIndex * 4 + 3] << 8) | (f[shapeIndex * 4 + 4] << 16) | (f[shapeIndex * 4 + 5] << 24)) + 2;
        var shape = LolCore.Shapes.Decode(f, offset);
        shape.Key = $"{_shpName}:{shapeIndex}";
        return shape;
    }
}
