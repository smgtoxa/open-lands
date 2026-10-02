// src/formats/pak.mjs: Westwood PAK archive (name/offset index, then payloads).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lol
{
    /// <summary>pak.mjs entries: { name, offset, size }</summary>
    public sealed class PakEntry
    {
        public string name;
        public int offset, size;
    }

    public sealed class PakArchive
    {
        public byte[] bytes;
        public Dictionary<string, PakEntry> entries;

        public PakArchive(byte[] buffer)
        {
            this.bytes = buffer;
            this.entries = new Dictionary<string, PakEntry>();
            this.parse();
        }

        public void parse()
        {
            var reader = new BinaryReader(this.bytes);
            if (this.bytes.Length < 4) throw new Exception("PAK is too small");

            int firstOffset = reader.u32le();
            if (firstOffset < 5 || firstOffset > this.bytes.Length)
            {
                throw new Exception($"Invalid first PAK data offset: {firstOffset}");
            }

            int start = firstOffset;
            while (reader.offset < firstOffset)
            {
                string name = reader.asciiZ(firstOffset);
                if (name.Length == 0) break;
                if (reader.offset + 4 > firstOffset) throw new Exception("PAK index overlaps payload");

                int end = reader.u32le();
                if (end == 0) end = this.bytes.Length;
                if (start > end || end > this.bytes.Length)
                {
                    throw new Exception($"Invalid PAK range for {name}: {start}-{end}");
                }

                if (end > start)
                {
                    this.entries[name.ToUpperInvariant()] = new PakEntry { name = name, offset = start, size = end - start };
                }
                start = end;
                if (end == this.bytes.Length) break;
            }

            if (this.entries.Count == 0) throw new Exception("PAK contains no files");
        }

        public bool has(string name)
        {
            return this.entries.ContainsKey(name.ToUpperInvariant());
        }

        public List<PakEntry> list()
        {
            return this.entries.Values.ToList();
        }

        public byte[] get(string name)
        {
            if (!this.entries.TryGetValue(name.ToUpperInvariant(), out var entry)) throw new Exception($"Missing PAK entry: {name}");
            return Js.Slice(this.bytes, entry.offset, entry.offset + entry.size);
        }
    }
}
