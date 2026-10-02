// src/formats/cps.mjs: Westwood CPS bitmaps and the LCW (format 80) decoder.
using System;

namespace Lol
{
    /// <summary>cps.mjs decodeBitmapData: { compression, palette, data }</summary>
    public sealed class BitmapData
    {
        public int compression;
        public byte[] palette;
        public byte[] data;
    }

    /// <summary>cps.mjs decodeCps: { width, height, compression, palette, pixels }</summary>
    public sealed class CpsImage
    {
        public int width, height, compression;
        public byte[] palette;
        public byte[] pixels;
    }

    public static class Cps
    {
        const int WIDTH = 320;
        const int HEIGHT = 200;
        const int PIXEL_COUNT = WIDTH * HEIGHT;

        /// <summary>source is a view (JS passes subarrays); a byte[] converts implicitly.</summary>
        public static byte[] decodeLcwBlock(Bytes source, int outputCapacity)
        {
            var output = new byte[outputCapacity];
            int src = 0;
            int dst = 0;

            void needSource(int count)
            {
                if (src + count > source.Length) throw new Exception("Truncated LCW stream");
            }
            void needOutput(int count)
            {
                if (dst + count > output.Length) throw new Exception("LCW output overflow");
            }
            void copyFromOutput(int offset, int count)
            {
                if (offset < 0 || offset >= dst) throw new Exception($"Invalid LCW back-reference: {offset}");
                needOutput(count);
                for (int i = 0; i < count; i += 1) output[dst++] = output[offset + i];
            }

            while (dst < outputCapacity)
            {
                needSource(1);
                int command = source[src++];

                if (command == 0x80) break;
                if ((command & 0x80) == 0)
                {
                    needSource(1);
                    int count = (command >> 4) + 3;
                    int distance = ((command & 0x0f) << 8) | source[src++];
                    if (distance == 0) throw new Exception("Invalid zero-distance LCW copy");
                    copyFromOutput(dst - distance, count);
                }
                else if ((command & 0x40) == 0)
                {
                    int count = command & 0x3f;
                    needSource(count);
                    needOutput(count);
                    Array.Copy(source.Data, source.Offset + src, output, dst, count);
                    src += count;
                    dst += count;
                }
                else if (command == 0xfe)
                {
                    needSource(3);
                    int count = source[src] | (source[src + 1] << 8);
                    byte value = source[src + 2];
                    src += 3;
                    needOutput(count);
                    Js.Fill(output, value, dst, dst + count);
                    dst += count;
                }
                else if (command == 0xff)
                {
                    needSource(4);
                    int count = source[src] | (source[src + 1] << 8);
                    int offset = source[src + 2] | (source[src + 3] << 8);
                    src += 4;
                    copyFromOutput(offset, count);
                }
                else
                {
                    needSource(2);
                    int count = (command & 0x3f) + 3;
                    int offset = source[src] | (source[src + 1] << 8);
                    src += 2;
                    copyFromOutput(offset, count);
                }
            }

            return Js.Slice(output, 0, dst);
        }

        public static byte[] decodeLcw(Bytes source, int outputSize)
        {
            var output = decodeLcwBlock(source, outputSize);
            if (output.Length != outputSize) throw new Exception($"LCW decoded {output.Length} bytes; expected {outputSize}");
            return output;
        }

        public static BitmapData decodeBitmapData(byte[] buffer)
        {
            var reader = new BinaryReader(buffer);
            if (reader.bytes.Length < 10) throw new Exception("Bitmap is too small");

            int storedSize = reader.u16le();
            int compression = reader.u16le();
            int outputSize = reader.u32le();
            int paletteSize = reader.u16le();

            if (storedSize != 0 && storedSize + 2 > reader.bytes.Length) throw new Exception("Truncated bitmap file");

            reader.ensure(paletteSize);
            var palette = Js.Slice(reader.bytes, reader.offset, reader.offset + paletteSize);
            reader.offset += paletteSize;

            var encoded = new Bytes(reader.bytes).Sub(reader.offset);
            byte[] pixels;
            if (compression == 0)
            {
                if (encoded.Length < outputSize) throw new Exception("Truncated raw CPS pixels");
                pixels = encoded.Sub(0, outputSize).ToArray();
            }
            else if (compression == 4)
            {
                pixels = decodeLcw(encoded, outputSize);
            }
            else
            {
                throw new Exception($"Unsupported bitmap compression type: {compression}");
            }

            return new BitmapData { compression = compression, palette = palette, data = pixels };
        }

        public static CpsImage decodeCps(byte[] buffer)
        {
            var bitmap = decodeBitmapData(buffer);
            if (bitmap.data.Length != PIXEL_COUNT)
            {
                throw new Exception($"Unsupported CPS dimensions ({bitmap.data.Length} pixels)");
            }
            if (bitmap.palette.Length != 0 && bitmap.palette.Length != 768)
            {
                throw new Exception($"Unsupported CPS palette size: {bitmap.palette.Length}");
            }

            var palette = new byte[768];
            for (int i = 0; i < bitmap.palette.Length; i += 1)
            {
                int color = bitmap.palette[i] & 0x3f;
                palette[i] = (byte)((color << 2) | (color & 3));
            }

            return new CpsImage { width = WIDTH, height = HEIGHT, compression = bitmap.compression, palette = palette, pixels = bitmap.data };
        }
    }
}
