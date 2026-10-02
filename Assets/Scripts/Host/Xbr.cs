// src/platform/xbr.mjs, ported 1:1 (docs/port/HOST.md). C# 9.
// xBR 4x for pixel art: an edge-directed scaler in the xBR family (the classic "xBR-lv2"
// interpolation rules), operating on RGBA. Keeps the original artwork - it only decides, per
// output subpixel, which of the neighbouring source pixels it belongs to - so nothing is invented.
// Usage: xbr4(rgba, w, h) -> Uint8Array of w*4 x h*4 RGBA.
using System;

namespace LolHost
{
    public static class Xbr
    {
        static readonly double[] wgtY = { 0.299, 0.587, 0.114 };

        static double yuvDist(byte[] a, byte[] b, int ai, int bi)
        {
            double ar = a[ai], ag = a[ai + 1], ab = a[ai + 2], aa = a[ai + 3];
            double br = b[bi], bg = b[bi + 1], bb = b[bi + 2], ba = b[bi + 3];
            double ay = wgtY[0] * ar + wgtY[1] * ag + wgtY[2] * ab;
            double by = wgtY[0] * br + wgtY[1] * bg + wgtY[2] * bb;
            double au = 0.492 * (ab - ay), bu = 0.492 * (bb - by);
            double av = 0.877 * (ar - ay), bv = 0.877 * (br - by);
            return 48 * Math.Abs(ay - by) + 7 * Math.Abs(au - bu) + 6 * Math.Abs(av - bv) + 64 * Math.Abs(aa - ba);
        }

        /// <summary>JS Math.round (halves go up).</summary>
        static double round(double v) => Math.Floor(v + 0.5);

        static void mix(byte[] @out, int o, byte[] a, int ai, byte[] b, int bi, double t)
        {
            for (int k = 0; k < 4; k += 1) @out[o + k] = (byte)round(a[ai + k] * (1 - t) + b[bi + k] * t);
        }

        public static byte[] xbr4(byte[] src, int w, int h)
        {
            const int S = 4;
            var dst = new byte[w * S * h * S * 4];
            int at(int x, int y) => (Math.Min(h - 1, Math.Max(0, y)) * w + Math.Min(w - 1, Math.Max(0, x))) * 4;
            double d(int a, int b) => yuvDist(src, src, a, b);
            var block = new int[4];
            for (int y = 0; y < h; y += 1)
            {
                for (int x = 0; x < w; x += 1)
                {
                    // 4x4 neighbourhood, xBR naming: A1..D0 around the centre E
                    int A1 = at(x - 1, y - 2), B1 = at(x, y - 2), C1 = at(x + 1, y - 2),
                        A0 = at(x - 2, y - 1), A = at(x - 1, y - 1), B = at(x, y - 1), C = at(x + 1, y - 1), C4 = at(x + 2, y - 1),
                        D0 = at(x - 2, y), D = at(x - 1, y), E = at(x, y), F = at(x + 1, y), F4 = at(x + 2, y),
                        G0 = at(x - 2, y + 1), G = at(x - 1, y + 1), H = at(x, y + 1), I = at(x + 1, y + 1), I4 = at(x + 2, y + 1),
                        G5 = at(x - 1, y + 2), H5 = at(x, y + 2), I5 = at(x + 1, y + 2);
                    // per-corner blend weights, as in the xBR-lv2 rules
                    var corners = new[]
                    {
                        new[] { E, I, H, F, G, C, D, B, F4, I4, H5, I5 },   // bottom-right
                        new[] { E, C, F, B, I, A, H, D, B1, C1, F4, C4 },   // top-right
                        new[] { E, A, B, D, C, G, F, H, D0, A0, B1, A1 },   // top-left
                        new[] { E, G, D, H, A, I, B, F, H5, G5, D0, G0 },   // bottom-left
                    };
                    for (int n = 0; n < 4; n += 1)
                    {
                        var k = corners[n];
                        int e = k[0], i = k[1], hN = k[2], f = k[3], g = k[4], c = k[5], dN = k[6], b = k[7], f4 = k[8], i4 = k[9], h5 = k[10], i5 = k[11];
                        bool ex = d(e, hN) > 0 || d(e, f) > 0;
                        int blend = 0;
                        if (ex)
                        {
                            double e1 = d(e, c) + d(e, g) + d(i, h5) + d(i, f4) + 4 * d(hN, f);
                            double e2 = d(hN, dN) + d(hN, i5) + d(f, i4) + d(f, b) + 4 * d(e, i);
                            if (e1 < e2) blend = e1 * 2 <= e2 ? 2 : 1;
                        }
                        block[n] = blend;
                    }
                    // write the 4x4 output pixels of this source pixel
                    int @base = (y * S) * (w * S) + x * S;
                    for (int sy = 0; sy < S; sy += 1)
                    {
                        for (int sx = 0; sx < S; sx += 1)
                        {
                            int o = (@base + sy * w * S + sx) * 4;
                            // which corner does this subpixel belong to, and how far into it?
                            double fx = (sx + 0.5) / S - 0.5;   // -0.375 .. 0.375
                            double fy = (sy + 0.5) / S - 0.5;
                            int corner = fy >= 0 ? (fx >= 0 ? 0 : 3) : (fx >= 0 ? 1 : 2);
                            int blend = block[corner];
                            int hN = corners[corner][2], f = corners[corner][3];
                            double rad = Math.Abs(fx) + Math.Abs(fy); // 0 centre .. 0.75 outer corner
                            if (blend == 0 || rad < 0.4) { for (int k = 0; k < 4; k += 1) dst[o + k] = src[E + k]; continue; }
                            int neighbour = Math.Abs(fx) > Math.Abs(fy) ? f : hN;
                            double t = blend == 2 ? (rad >= 0.65 ? 0.75 : 0.35) : (rad >= 0.65 ? 0.4 : 0.15);
                            mix(dst, o, src, E, src, neighbour, t);
                        }
                    }
                }
            }
            return dst;
        }

        // Transparent pixels carry no colour, so blending against them would pull black into every edge.
        // Give them the colour of an opaque neighbour first (alpha stays 0, so only the mix changes).
        static byte[] bleedColour(byte[] src, int w, int h)
        {
            var @out = (byte[])src.Clone();
            for (int y = 0; y < h; y += 1)
            {
                for (int x = 0; x < w; x += 1)
                {
                    int o = (y * w + x) * 4;
                    if (src[o + 3] != 0) continue;
                    int r = 0; int g = 0; int b = 0; int n = 0;
                    for (int dy = -1; dy <= 1; dy += 1) for (int dx = -1; dx <= 1; dx += 1)
                    {
                        int nx = x + dx; int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int p = (ny * w + nx) * 4;
                        if (src[p + 3] == 0) continue;
                        r += src[p]; g += src[p + 1]; b += src[p + 2]; n += 1;
                    }
                    if (n != 0) { @out[o] = (byte)round((double)r / n); @out[o + 1] = (byte)round((double)g / n); @out[o + 2] = (byte)round((double)b / n); }
                }
            }
            return @out;
        }

        // What the HD passes call: 4x xBR that does not darken the edges of a cut-out shape.
        public static byte[] xbrUpscale4(byte[] rgba, int w, int h)
        {
            return xbr4(bleedColour(rgba, w, h), w, h);
        }

        // The HD packs and the UI icons are both built at 4x.
        public const int XBR = 4;
    }
}
