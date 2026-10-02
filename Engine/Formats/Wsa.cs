// src/formats/wsa.mjs: WSA v2 movie decoder (every frame decoded up front).
using System;
using System.Collections.Generic;

namespace Lol
{
    public static class WsaFile
    {
        public static void applyXorDelta(byte[] pixels, byte[] delta)
        {
            int source = 0;
            int target = 0;

            void need(int count)
            {
                if (source + count > delta.Length) throw new Exception("Truncated WSA delta");
            }
            void fit(int count)
            {
                if (target + count > pixels.Length) throw new Exception("WSA delta exceeds frame");
            }
            void xorLiteral(int count)
            {
                need(count);
                fit(count);
                for (int i = 0; i < count; i += 1) pixels[target++] ^= delta[source++];
            }
            void xorRun(int count)
            {
                need(1);
                fit(count);
                byte value = delta[source++];
                for (int i = 0; i < count; i += 1) pixels[target++] ^= value;
            }

            while (source < delta.Length)
            {
                int command = delta[source++];
                if (command == 0)
                {
                    need(2);
                    int count = delta[source++];
                    xorRun(count);
                }
                else if (command < 0x80)
                {
                    xorLiteral(command);
                }
                else if (command > 0x80)
                {
                    int count = command & 0x7f;
                    fit(count);
                    target += count;
                }
                else
                {
                    need(2);
                    int count = delta[source] | (delta[source + 1] << 8);
                    source += 2;
                    if (count == 0) break;
                    if ((count & 0x8000) == 0)
                    {
                        fit(count);
                        target += count;
                    }
                    else if ((count & 0x4000) == 0)
                    {
                        xorLiteral(count & 0x3fff);
                    }
                    else
                    {
                        xorRun(count & 0x3fff);
                    }
                }
            }
        }
    }

    public sealed class WsaMovie
    {
        public int frameCount, x, y, width, height, deltaCapacity, flags;
        public List<int> offsets;
        public byte[] palette;
        public List<byte[]> frames;

        public WsaMovie(byte[] buffer)
        {
            var reader = new BinaryReader(buffer);
            this.frameCount = reader.u16le() & 0x7fff;
            this.x = reader.i16le();
            this.y = reader.i16le();
            this.width = reader.u16le();
            this.height = reader.u16le();
            this.deltaCapacity = reader.u16le();
            this.flags = reader.u16le();

            if (this.frameCount == 0 || this.width == 0 || this.height == 0 || this.deltaCapacity == 0)
            {
                throw new Exception("Invalid WSA v2 header");
            }

            this.offsets = new List<int>();
            for (int i = 0; i < this.frameCount + 2; i += 1) this.offsets.Add(reader.u32le());

            this.palette = null;
            if ((this.flags & 1) != 0)
            {
                reader.ensure(768);
                this.palette = new byte[768];
                for (int i = 0; i < 768; i += 1)
                {
                    int color = reader.bytes[reader.offset + i] & 0x3f;
                    this.palette[i] = (byte)((color << 2) | (color & 3));
                }
                reader.offset += 768;
            }

            int baseOffset = this.offsets[0] != 0 ? this.offsets[0] : this.offsets[1];
            if (baseOffset == 0) throw new Exception("WSA has no frame data");
            int payloadStart = reader.offset;
            var pixels = new byte[this.width * this.height];
            this.frames = new List<byte[]>();

            for (int i = 0; i < this.frameCount; i += 1)
            {
                if (this.offsets[i] == 0 || this.offsets[i + 1] == 0) throw new Exception($"Missing WSA frame {i}");
                int start = payloadStart + this.offsets[i] - baseOffset;
                int end = payloadStart + this.offsets[i + 1] - baseOffset;
                if (start < payloadStart || end < start || end > reader.bytes.Length)
                {
                    throw new Exception($"Invalid WSA frame range {i}");
                }
                var delta = Cps.decodeLcwBlock(new Bytes(reader.bytes, start, end - start), this.deltaCapacity);
                WsaFile.applyXorDelta(pixels, delta);
                this.frames.Add((byte[])pixels.Clone());
            }
        }

        public byte[] frame(int index)
        {
            if (index < 0 || index >= this.frames.Count)
            {
                throw new IndexOutOfRangeException($"Invalid WSA frame: {index}");
            }
            return this.frames[index];
        }
    }
}
