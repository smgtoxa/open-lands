// src/formats/binary.mjs: BinaryReader, a little-endian cursor over a byte buffer.
using System;
using System.Buffers.Binary;
using System.Text;

namespace Lol
{
    public sealed class BinaryReader
    {
        public byte[] bytes;
        public int offset;

        public BinaryReader(byte[] buffer)
        {
            this.bytes = buffer;
            this.offset = 0;
        }

        /// <summary>JS default `at = this.offset`: null means the cursor.</summary>
        public void ensure(int length, int? at = null)
        {
            int a = at ?? this.offset;
            if (length < 0 || a < 0 || a + length > this.bytes.Length)
            {
                throw new IndexOutOfRangeException($"Read outside buffer at {a} ({length} bytes)");
            }
        }

        public int u8(int? at = null)
        {
            int a = at ?? this.offset;
            this.ensure(1, a);
            int value = this.bytes[a];
            if (a == this.offset) this.offset += 1;
            return value;
        }

        public int u16le(int? at = null)
        {
            int a = at ?? this.offset;
            this.ensure(2, a);
            int value = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(this.bytes, a, 2));
            if (a == this.offset) this.offset += 2;
            return value;
        }

        public int i16le(int? at = null)
        {
            int a = at ?? this.offset;
            this.ensure(2, a);
            int value = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(this.bytes, a, 2));
            if (a == this.offset) this.offset += 2;
            return value;
        }

        /// <summary>getUint32; returned as int (the files never hold offsets past 2^31).</summary>
        public int u32le(int? at = null)
        {
            int a = at ?? this.offset;
            this.ensure(4, a);
            int value = (int)BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(this.bytes, a, 4));
            if (a == this.offset) this.offset += 4;
            return value;
        }

        /// <summary>subarray(start, end): a copy here; no caller writes through it.</summary>
        public byte[] slice(int start, int end)
        {
            this.ensure(end - start, start);
            return Js.Slice(this.bytes, start, end);
        }

        public string asciiZ(int? limit = null)
        {
            int start = this.offset;
            int endLimit = limit == null ? this.bytes.Length : Math.Min(limit.Value, this.bytes.Length);
            while (this.offset < endLimit && this.bytes[this.offset] != 0) this.offset += 1;
            if (this.offset >= endLimit) throw new Exception($"Unterminated string at {start}");
            var value = new StringBuilder();
            for (int i = start; i < this.offset; i += 1) value.Append((char)this.bytes[i]);
            this.offset += 1;
            return value.ToString();
        }
    }
}
