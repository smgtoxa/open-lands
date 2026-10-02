// The briar sprite the Wall of Thorns leaves on the floor.
//
// Transliterated from briarShape/nearestIndex in src/game/extra-spells.mjs. The original art has no
// briars, so the sprite is drawn here against the level's own palette - every colour is the nearest
// entry in that palette, which is why it looks like it belongs wherever it is cast.
namespace LolCore;

public static class ExtraSpellArt
{
    private static int NearestIndex(byte[] palette, int r, int g, int b, Dictionary<int, int> cache)
    {
        int key = (r << 16) | (g << 8) | b;
        if (cache.TryGetValue(key, out int hit)) return hit;
        int pr = r >> 2, pg = g >> 2, pb = b >> 2;
        int best = 1;
        int bestDist = int.MaxValue;
        for (int i = 1; i < 256; i += 1)
        {
            int dr = palette[i * 3] - pr, dg = palette[i * 3 + 1] - pg, db = palette[i * 3 + 2] - pb;
            int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
            if (d >= bestDist) continue;
            bestDist = d;
            best = i;
        }
        cache[key] = best;
        return best;
    }

    /// <summary>A 30x22 tangle of stems and thorns, in whatever greens the level's palette has.</summary>
    public static Shape BriarShape(byte[] palette)
    {
        const int w = 30, h = 22;
        var pixels = new byte[w * h];
        var cache = new Dictionary<int, int>();
        int Ix(int r, int g, int b) => NearestIndex(palette, r, g, b, cache);
        void Dot(double xd, double yd, int c)
        {
            int x = (int)Math.Round(xd), y = (int)Math.Round(yd);
            if (x >= 0 && y >= 0 && x < w && y < h) pixels[y * w + x] = (byte)c;
        }
        void Line(double x0, double y0, double x1, double y1, int c)
        {
            double n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= n; i += 1) Dot(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, c);
        }
        int dark = Ix(20, 44, 18), mid = Ix(38, 74, 30), lit = Ix(74, 112, 48), thorn = Ix(150, 150, 120);
        for (int i = 0; i < 7; i += 1)   // a tangle of stems leaning out of the floor
        {
            int x0 = 3 + i * 4;
            int top = 6 + ((i * 5) % 7);
            Line(x0, h - 2, x0 + (i % 2 != 0 ? 3 : -3), top, i % 3 != 0 ? mid : dark);
            Line(x0 + (i % 2 != 0 ? 3 : -3), top, x0 + (i % 2 != 0 ? 6 : -1), top + 4, dark);
            Dot(x0 + (i % 2 != 0 ? 4 : -4), top - 1, thorn);   // the spikes catch the light
            Dot(x0 + (i % 2 != 0 ? 2 : -2), top + 3, thorn);
        }
        for (int i = 0; i < 5; i += 1) Line(2 + i * 6, h - 2, 6 + i * 6, h - 6, lit);
        for (int x = 1; x < w - 1; x += 1) Dot(x, h - 1, dark);
        return new Shape { Width = w, Height = h, Pixels = pixels, ColorTable = null, ColorCount = 256, Key = "camp:briars" };
    }
}
