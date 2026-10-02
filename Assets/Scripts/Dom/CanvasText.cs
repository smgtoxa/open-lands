// Canvas fillText / measureText: an OS font's glyphs (Unity's dynamic font atlas, read back to the CPU when
// it changes) blended into a canvas' pixels with the fill colour, at the canvas' text alignment and
// baseline - so text is part of the picture (toDataURL, getImageData) as in a browser.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LolHost
{
    public static class CanvasText
    {
        sealed class Atlas
        {
            public Font font; public Color32[] pixels; public int w, h; public bool dirty = true;
            // glyphs the copy holds: a glyph added to the atlas does not always rebuild it (no event)
            public readonly HashSet<(char, int, FontStyle)> known = new HashSet<(char, int, FontStyle)>();
        }

        static readonly Dictionary<string, Atlas> Fonts = new Dictionary<string, Atlas>();

        static Atlas For(string[] families)
        {
            string key = string.Join(",", families);
            if (Fonts.TryGetValue(key, out var a)) return a;
            a = new Atlas { font = Font.CreateDynamicFontFromOSFont(families, 16) };
            var atlas = a;
            Font.textureRebuilt += f => { if (f == atlas.font) { atlas.dirty = true; atlas.known.Clear(); } };
            Fonts[key] = a;
            return a;
        }

        /// <summary>The glyphs of the text in the atlas, and the atlas' pixels as they are now.</summary>
        static Atlas Prepare(string text, int size, bool bold, string[] families)
        {
            var a = For(families);
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            a.font.RequestCharactersInTexture(text + "Mg", size, style);
            foreach (char c in text + "Mg") if (a.known.Add((c, size, style))) a.dirty = true;
            var tex = a.font.material.mainTexture;
            if (tex == null) return null;
            if (a.dirty || a.pixels == null || a.w != tex.width || a.h != tex.height)
            {
                // the atlas lives on the GPU: copy it through a render texture
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                var old = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                a.pixels = read.GetPixels32();
                a.w = tex.width; a.h = tex.height;
                UnityEngine.Object.Destroy(read);
                a.dirty = false;
            }
            return a;
        }

        public static float Measure(string text, int size, bool bold, string[] families)
        {
            var a = For(families);
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            a.font.RequestCharactersInTexture(text, size, style);
            float w = 0;
            foreach (char c in text) if (a.font.GetCharacterInfo(c, out var ci, size, style)) w += ci.advance;
            return w;
        }

        public static void Draw(Color32[] dst, int W, int H, string text, float x, float y, int size, bool bold, string[] families, Color color,
            string align, string baseline, int clipX0, int clipY0, int clipX1, int clipY1)
        {
            var a = Prepare(text, size, bold, families);
            if (a == null) return;
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            float width = 0;
            foreach (char c in text) if (a.font.GetCharacterInfo(c, out var ci, size, style)) width += ci.advance;
            a.font.GetCharacterInfo('M', out var m, size, style);
            a.font.GetCharacterInfo('g', out var g, size, style);
            float ascent = m.maxY, descent = -g.minY;
            if (align == "center") x -= width / 2;
            else if (align == "right" || align == "end") x -= width;
            // the pen's baseline for the canvas' textBaseline
            float by = baseline == "top" || baseline == "hanging" ? y + ascent : baseline == "middle" ? y + (ascent - descent) / 2 : baseline == "bottom" || baseline == "ideographic" ? y - descent : y;
            float pen = x;
            foreach (char c in text)
            {
                if (!a.font.GetCharacterInfo(c, out var ci, size, style)) continue;
                Glyph(dst, W, H, a, ci, pen, by, color, clipX0, clipY0, clipX1, clipY1);
                pen += ci.advance;
            }
        }

        static void Glyph(Color32[] dst, int W, int H, Atlas a, CharacterInfo ci, float pen, float baseline, Color color, int cx0, int cy0, int cx1, int cy1)
        {
            float gx0 = pen + ci.minX, gx1 = pen + ci.maxX, gy0 = baseline - ci.maxY, gy1 = baseline - ci.minY;
            if (gx1 <= gx0 || gy1 <= gy0) return;
            int x0 = Math.Max(cx0, Mathf.FloorToInt(gx0)), x1 = Math.Min(cx1, Mathf.CeilToInt(gx1));
            int y0 = Math.Max(cy0, Mathf.FloorToInt(gy0)), y1 = Math.Min(cy1, Mathf.CeilToInt(gy1));
            for (int y = y0; y < y1; y += 1)
            {
                float t = (y + 0.5f - gy0) / (gy1 - gy0);
                if (t < 0 || t > 1) continue;
                for (int x = x0; x < x1; x += 1)
                {
                    float s = (x + 0.5f - gx0) / (gx1 - gx0);
                    if (s < 0 || s > 1) continue;
                    // the glyph's corners in the atlas (a glyph may be stored rotated)
                    var uv = Vector2.Lerp(Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, s), Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, s), t);
                    int ax = Mathf.Clamp((int)(uv.x * a.w), 0, a.w - 1), ay = Mathf.Clamp((int)(uv.y * a.h), 0, a.h - 1);
                    float cover = a.pixels[ay * a.w + ax].a / 255f * color.a;
                    if (cover <= 0.004f) continue;
                    int i = y * W + x;
                    if (i < 0 || i >= dst.Length) continue;
                    Color d = dst[i];
                    float oa = cover + d.a * (1 - cover);
                    dst[i] = oa <= 0 ? new Color32(0, 0, 0, 0) : (Color32)new Color((color.r * cover + d.r * d.a * (1 - cover)) / oa, (color.g * cover + d.g * d.a * (1 - cover)) / oa, (color.b * cover + d.b * d.a * (1 - cover)) / oa, oa);
                }
            }
        }
    }
}
