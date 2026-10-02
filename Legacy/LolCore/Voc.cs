// Creative Voice File: the game's sound effects and its spoken lines.
//
// Transliterated from decodeVoc in src/game/sound.mjs. A .VOC is a 26-byte header followed by typed
// blocks: type 1 carries 8-bit samples with a divisor for the rate, type 9 the same with an
// explicit rate, and type 3 is a silence of so many samples. The game's files use all three.
namespace LolCore;

public static class Voc
{
    public sealed class Sound
    {
        public int SampleRate;
        public float[] Samples = Array.Empty<float>();
    }

    public static Sound Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 26) throw new InvalidDataException("Not a VOC file");
        if (System.Text.Encoding.ASCII.GetString(bytes, 0, 19) != "Creative Voice File")
            throw new InvalidDataException("Not a VOC file");
        int dataOffset = bytes[20] | (bytes[21] << 8);
        int p = dataOffset;
        int sampleRate = 11025;
        var chunks = new List<byte[]>();
        int total = 0;
        while (p < bytes.Length)
        {
            int type = bytes[p++];
            if (type == 0) break;
            if (p + 3 > bytes.Length) break;
            int len = bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16);
            p += 3;
            if (type == 1)
            {
                sampleRate = (int)Math.Round(1000000.0 / (256 - bytes[p]));
                var data = bytes.Skip(p + 2).Take(len - 2).ToArray();
                chunks.Add(data);
                total += data.Length;
            }
            else if (type == 9)
            {
                sampleRate = bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16) | (bytes[p + 3] << 24);
                var data = bytes.Skip(p + 12).Take(len - 12).ToArray();
                chunks.Add(data);
                total += data.Length;
            }
            else if (type == 3)
            {
                int pause = bytes[p] | (bytes[p + 1] << 8);
                var silence = new byte[pause];
                Array.Fill(silence, (byte)0x80);
                chunks.Add(silence);
                total += pause;
            }
            p += len;
        }
        var samples = new float[total];
        int at = 0;
        foreach (var chunk in chunks)
            foreach (byte b in chunk) samples[at++] = (b - 128) / 128f;
        return new Sound { SampleRate = sampleRate, Samples = samples };
    }
}
