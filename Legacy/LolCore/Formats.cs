// Westwood file formats, transliterated from src/formats/*.mjs of the JavaScript port (which in
// turn follows ScummVM's Kyra engine). Same names, same order of operations: this code is meant to
// be diffable against the JS, not to be idiomatic C#.
namespace LolCore;

/// <summary>Reader for little-endian structures, matching src/formats/binary.mjs.</summary>
public sealed class BinaryReader
{
    public readonly byte[] Bytes;
    public int Offset;

    public BinaryReader(byte[] bytes, int offset = 0) { Bytes = bytes; Offset = offset; }

    public void Ensure(int count)
    {
        if (Offset + count > Bytes.Length) throw new InvalidDataException("Unexpected end of data");
    }

    public byte U8() { Ensure(1); return Bytes[Offset++]; }

    public ushort U16Le() { Ensure(2); ushort v = (ushort)(Bytes[Offset] | (Bytes[Offset + 1] << 8)); Offset += 2; return v; }

    public short I16Le() => (short)U16Le();

    public uint U32Le()
    {
        Ensure(4);
        uint v = (uint)(Bytes[Offset] | (Bytes[Offset + 1] << 8) | (Bytes[Offset + 2] << 16) | (Bytes[Offset + 3] << 24));
        Offset += 4;
        return v;
    }

    public string AsciiZ(int limit)
    {
        var chars = new List<char>();
        while (Offset < limit && Offset < Bytes.Length)
        {
            byte b = Bytes[Offset++];
            if (b == 0) break;
            chars.Add((char)b);
        }
        return new string(chars.ToArray());
    }
}

/// <summary>Westwood PAK archives (src/formats/pak.mjs).</summary>
public sealed class PakArchive
{
    private readonly byte[] _bytes;
    private readonly Dictionary<string, (int Offset, int Size)> _entries = new(StringComparer.Ordinal);

    public PakArchive(byte[] bytes)
    {
        _bytes = bytes;
        var reader = new BinaryReader(bytes);
        if (bytes.Length < 4) throw new InvalidDataException("PAK is too small");
        int firstOffset = (int)reader.U32Le();
        if (firstOffset < 5 || firstOffset > bytes.Length) throw new InvalidDataException($"Invalid first PAK data offset: {firstOffset}");

        int start = firstOffset;
        while (reader.Offset < firstOffset)
        {
            string name = reader.AsciiZ(firstOffset);
            if (name.Length == 0) break;
            if (reader.Offset + 4 > firstOffset) throw new InvalidDataException("PAK index overlaps payload");
            int end = (int)reader.U32Le();
            if (end == 0) end = bytes.Length;
            if (start > end || end > bytes.Length) throw new InvalidDataException($"Invalid PAK range for {name}: {start}-{end}");
            if (end > start) _entries[name.ToUpperInvariant()] = (start, end - start);
            start = end;
            if (end == bytes.Length) break;
        }
        if (_entries.Count == 0) throw new InvalidDataException("PAK contains no files");
    }

    public bool Has(string name) => _entries.ContainsKey(name.ToUpperInvariant());

    public IEnumerable<string> Names => _entries.Keys;

    public byte[] Get(string name)
    {
        if (!_entries.TryGetValue(name.ToUpperInvariant(), out var entry)) throw new FileNotFoundException($"Missing PAK entry: {name}");
        var slice = new byte[entry.Size];
        Array.Copy(_bytes, entry.Offset, slice, 0, entry.Size);
        return slice;
    }
}

/// <summary>LCW ("format 80") decompression and CPS bitmaps (src/formats/cps.mjs).</summary>
public static class Cps
{
    public const int Width = 320;
    public const int Height = 200;

    // `source` is a plain array rather than a span: the local helpers below close over it, and a
    // ref-like type cannot be captured.
    public static byte[] DecodeLcwBlock(byte[] source, int sourceOffset, int sourceLength, int outputCapacity)
    {
        var output = new byte[outputCapacity];
        int src = sourceOffset;
        int end = sourceOffset + sourceLength;
        int dst = 0;

        void NeedSource(int count) { if (src + count > end) throw new InvalidDataException("Truncated LCW stream"); }
        void NeedOutput(int count) { if (dst + count > output.Length) throw new InvalidDataException("LCW output overflow"); }
        void CopyFromOutput(int offset, int count)
        {
            if (offset < 0 || offset >= dst) throw new InvalidDataException($"Invalid LCW back-reference: {offset}");
            NeedOutput(count);
            for (int i = 0; i < count; i += 1) output[dst++] = output[offset + i];
        }

        while (dst < outputCapacity)
        {
            NeedSource(1);
            byte command = source[src++];
            if (command == 0x80) break;

            if ((command & 0x80) == 0)
            {
                NeedSource(1);
                int count = (command >> 4) + 3;
                int distance = ((command & 0x0f) << 8) | source[src++];
                if (distance == 0) throw new InvalidDataException("Invalid zero-distance LCW copy");
                CopyFromOutput(dst - distance, count);
            }
            else if ((command & 0x40) == 0)
            {
                int count = command & 0x3f;
                NeedSource(count);
                NeedOutput(count);
                Array.Copy(source, src, output, dst, count);
                src += count;
                dst += count;
            }
            else if (command == 0xfe)
            {
                NeedSource(3);
                int count = source[src] | (source[src + 1] << 8);
                byte value = source[src + 2];
                src += 3;
                NeedOutput(count);
                output.AsSpan(dst, count).Fill(value);
                dst += count;
            }
            else if (command == 0xff)
            {
                NeedSource(4);
                int count = source[src] | (source[src + 1] << 8);
                int offset = source[src + 2] | (source[src + 3] << 8);
                src += 4;
                CopyFromOutput(offset, count);
            }
            else
            {
                NeedSource(2);
                int count = (command & 0x3f) + 3;
                int offset = source[src] | (source[src + 1] << 8);
                src += 2;
                CopyFromOutput(offset, count);
            }
        }

        if (dst == output.Length) return output;
        var trimmed = new byte[dst];
        Array.Copy(output, trimmed, dst);
        return trimmed;
    }

    public static byte[] DecodeLcw(byte[] source, int sourceOffset, int sourceLength, int outputSize)
    {
        var output = DecodeLcwBlock(source, sourceOffset, sourceLength, outputSize);
        if (output.Length != outputSize) throw new InvalidDataException($"LCW decoded {output.Length} bytes; expected {outputSize}");
        return output;
    }

    public readonly record struct Bitmap(int Compression, byte[] Palette, byte[] Data);

    public static Bitmap DecodeBitmapData(byte[] buffer)
    {
        var reader = new BinaryReader(buffer);
        if (buffer.Length < 10) throw new InvalidDataException("Bitmap is too small");
        int storedSize = reader.U16Le();
        int compression = reader.U16Le();
        int outputSize = (int)reader.U32Le();
        int paletteSize = reader.U16Le();
        if (storedSize != 0 && storedSize + 2 > buffer.Length) throw new InvalidDataException("Truncated bitmap file");

        reader.Ensure(paletteSize);
        var palette = new byte[paletteSize];
        Array.Copy(buffer, reader.Offset, palette, 0, paletteSize);
        reader.Offset += paletteSize;

        int encodedAt = reader.Offset;
        int encodedLength = buffer.Length - encodedAt;
        byte[] pixels;
        if (compression == 0)
        {
            if (encodedLength < outputSize) throw new InvalidDataException("Truncated raw CPS pixels");
            pixels = buffer.AsSpan(encodedAt, outputSize).ToArray();
        }
        else if (compression == 4) pixels = DecodeLcw(buffer, encodedAt, encodedLength, outputSize);
        else throw new InvalidDataException($"Unsupported bitmap compression type: {compression}");

        return new Bitmap(compression, palette, pixels);
    }
}
