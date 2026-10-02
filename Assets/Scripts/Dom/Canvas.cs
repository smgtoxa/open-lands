// <canvas> and the parts of CanvasRenderingContext2D the pages use, rasterised in software into a
// texture. fillText is drawn as positioned labels over the canvas (cleared with the canvas).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class ImageData
    {
        public int width, height;
        public byte[] data;   // RGBA, top-down
        public ImageData(int w, int h) { width = w; height = h; data = new byte[w * h * 4]; }
    }

    public sealed class CanvasGradient
    {
        public readonly List<(float at, Color color)> stops = new List<(float, Color)>();
        public float x0, y0, x1, y1;
        public void addColorStop(float at, string color) => stops.Add((at, Css.Color(color)));
        public Color At(float x, float y)
        {
            if (stops.Count == 0) return Color.clear;
            float dx = x1 - x0, dy = y1 - y0, len = dx * dx + dy * dy;
            float t = len == 0 ? 0 : Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / len);
            var s = stops.OrderBy(v => v.at).ToList();
            if (t <= s[0].at) return s[0].color;
            for (int i = 1; i < s.Count; i += 1)
                if (t <= s[i].at) return Color.Lerp(s[i - 1].color, s[i].color, (t - s[i - 1].at) / Mathf.Max(1e-6f, s[i].at - s[i - 1].at));
            return s[s.Count - 1].color;
        }
    }

    public sealed class CanvasEl : VisualElement
    {
        static readonly List<CanvasEl> Dirty = new List<CanvasEl>();
        int _w = 300, _h = 150;
        internal Color32[] pixels = new Color32[300 * 150];
        Texture2D _tex;
        Ctx2D _ctx;
        internal readonly List<Label> texts = new List<Label>();
        bool _dirty;
        public bool smoothing;

        public CanvasEl()
        {
            AddToClassList("canvas");
            pickingMode = PickingMode.Position;
        }

        public int width { get => _w; set { _w = Math.Max(1, value); Reset(); } }
        public int height { get => _h; set { _h = Math.Max(1, value); Reset(); } }

        public void OnAttribute(string name, string value)
        {
            if (name == "width") width = int.Parse(value, CultureInfo.InvariantCulture);
            else height = int.Parse(value, CultureInfo.InvariantCulture);
        }

        void Reset()
        {
            pixels = new Color32[_w * _h];
            foreach (var t in texts) t.RemoveFromHierarchy();
            texts.Clear();
            MarkDirty();
        }

        public Ctx2D getContext(string kind = "2d") => _ctx ??= new Ctx2D(this);

        internal void MarkDirty()
        {
            if (_dirty) return;
            _dirty = true;
            Dirty.Add(this);
        }

        /// <summary>Uploads every canvas drawn to since the last frame (the host calls this once a frame).</summary>
        public static void FlushAll()
        {
            foreach (var c in Dirty.ToList()) c.Upload();
            Dirty.Clear();
        }

        void Upload()
        {
            _dirty = false;
            if (_tex == null || _tex.width != _w || _tex.height != _h)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            }
            _tex.filterMode = smoothing ? FilterMode.Bilinear : FilterMode.Point;
            var flipped = new Color32[_w * _h];
            for (int y = 0; y < _h; y += 1) Array.Copy(pixels, y * _w, flipped, (_h - 1 - y) * _w, _w);
            _tex.SetPixels32(flipped);
            _tex.Apply(false);
            style.backgroundImage = _tex;
        }

        public Texture2D Texture { get { if (_dirty) { Upload(); Dirty.Remove(this); } return _tex; } }

        /// <summary>canvas.toDataURL("image/png"): the pixels as a PNG data URL.</summary>
        public string toDataURL(string type = "image/png", float quality = 0.9f)
        {
            var t = Texture;
            var bytes = type == "image/jpeg" ? t.EncodeToJPG((int)(quality * 100)) : t.EncodeToPNG();
            return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }

        /// <summary>Canvas pixel -> element position.</summary>
        internal Vector2 ToLocal(float x, float y)
        {
            var r = contentRect;
            return new Vector2(x / _w * r.width, y / _h * r.height);
        }
    }

    public sealed class Ctx2D
    {
        readonly CanvasEl _c;
        public object fillStyle = "#000";
        public string strokeStyle = "#000";
        public float lineWidth = 1;
        public float globalAlpha = 1;
        public string font = "10px sans-serif";
        public string textAlign = "start", textBaseline = "alphabetic";
        public bool imageSmoothingEnabled { get => _c.smoothing; set => _c.smoothing = value; }
        /// <summary>Kept for the pages' sake: drawImage samples nearest-neighbour whatever the quality.</summary>
        public string imageSmoothingQuality = "low";
        /// <summary>"none" or "brightness(v)" (the only filter the pages draw with): scales the colour of every pixel drawn.</summary>
        public string filter { get => _filter; set { _filter = value ?? "none"; _bright = ParseBrightness(_filter); } }
        string _filter = "none";
        float _bright = 1;
        // The current transform, axis-aligned only (setTransform / translate / scale, no rotate or skew):
        // x' = _a * x + _e, y' = _d * y + _f. Path points and drawImage go through it.
        float _a = 1, _d = 1, _e, _f;
        // clip(): a device-space rectangle (the bounding box of the clipped path), [x0, x1) x [y0, y1)
        int _cx0 = int.MinValue, _cy0 = int.MinValue, _cx1 = int.MaxValue, _cy1 = int.MaxValue;
        readonly List<List<Vector2>> _path = new List<List<Vector2>>();
        readonly Stack<(object, string, float, float, string, string, string)> _saved = new Stack<(object, string, float, float, string, string, string)>();
        readonly Stack<(string, float, float, float, float, int, int, int, int)> _savedState = new Stack<(string, float, float, float, float, int, int, int, int)>();

        public Ctx2D(CanvasEl c) { _c = c; }
        public CanvasEl canvas => _c;
        int W => _c.width;
        int H => _c.height;

        public void save()
        {
            _saved.Push((fillStyle, strokeStyle, lineWidth, globalAlpha, font, textAlign, textBaseline));
            _savedState.Push((_filter, _a, _d, _e, _f, _cx0, _cy0, _cx1, _cy1));
        }
        public void restore()
        {
            if (_saved.Count == 0) return;
            (fillStyle, strokeStyle, lineWidth, globalAlpha, font, textAlign, textBaseline) = _saved.Pop();
            string f;
            (f, _a, _d, _e, _f, _cx0, _cy0, _cx1, _cy1) = _savedState.Pop();
            filter = f;
        }

        // ---- transform (axis-aligned: b and c of setTransform must be 0) ----
        public void setTransform(float a, float b, float c, float d, float e, float f) { _a = a; _d = d; _e = e; _f = f; }
        public void translate(float x, float y) { _e += _a * x; _f += _d * y; }
        public void scale(float x, float y) { _a *= x; _d *= y; }
        Vector2 Tx(float x, float y) => new Vector2(_a * x + _e, _d * y + _f);

        /// <summary>clip(): narrows the clip to the bounding box of the current path (the pages clip to rects only).</summary>
        public void clip()
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var sub in _path)
                foreach (var p in sub) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            if (x0 > x1) { x0 = y0 = x1 = y1 = 0; } // an empty path clips everything away
            _cx0 = Math.Max(_cx0, Mathf.RoundToInt(x0)); _cy0 = Math.Max(_cy0, Mathf.RoundToInt(y0));
            _cx1 = Math.Min(_cx1, Mathf.RoundToInt(x1)); _cy1 = Math.Min(_cy1, Mathf.RoundToInt(y1));
        }

        static float ParseBrightness(string f)
        {
            if (!f.StartsWith("brightness(")) return 1;
            return float.TryParse(f.Substring(11).TrimEnd(')'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1;
        }

        Color FillAt(float x, float y)
        {
            var c = fillStyle is CanvasGradient g ? g.At(x, y) : Parsed(fillStyle as string);
            c.a *= globalAlpha;
            return c;
        }

        // the last colour string and its value (a fill runs per pixel)
        string _parsedKey;
        Color _parsed;
        Color Parsed(string css)
        {
            if (!ReferenceEquals(css, _parsedKey) && css != _parsedKey) { _parsedKey = css; _parsed = Css.Color(css); }
            return _parsed;
        }

        void Blend(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || c.a <= 0) return;
            if (x < _cx0 || y < _cy0 || x >= _cx1 || y >= _cy1) return;
            if (_bright != 1) { c.r = Mathf.Min(1, c.r * _bright); c.g = Mathf.Min(1, c.g * _bright); c.b = Mathf.Min(1, c.b * _bright); }
            int i = y * W + x;
            if (c.a >= 1) { _c.pixels[i] = c; return; }
            Color d = _c.pixels[i];
            float a = c.a + d.a * (1 - c.a);
            _c.pixels[i] = a <= 0 ? new Color(0, 0, 0, 0) : new Color((c.r * c.a + d.r * d.a * (1 - c.a)) / a, (c.g * c.a + d.g * d.a * (1 - c.a)) / a, (c.b * c.a + d.b * d.a * (1 - c.a)) / a, a);
        }

        public void clearRect(float x, float y, float w, float h)
        {
            for (int yy = Mathf.Max(0, (int)y); yy < Mathf.Min(H, (int)(y + h)); yy += 1)
                for (int xx = Mathf.Max(0, (int)x); xx < Mathf.Min(W, (int)(x + w)); xx += 1) _c.pixels[yy * W + xx] = new Color32(0, 0, 0, 0);
            if (x <= 0 && y <= 0 && x + w >= W && y + h >= H) ClearTexts();
            _c.MarkDirty();
        }

        void ClearTexts() { }

        public void fillRect(float x, float y, float w, float h)
        {
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            int x0 = Mathf.Max(0, Mathf.RoundToInt(x)), y0 = Mathf.Max(0, Mathf.RoundToInt(y));
            int x1 = Mathf.Min(W, Mathf.RoundToInt(x + w)), y1 = Mathf.Min(H, Mathf.RoundToInt(y + h));
            var solid = fillStyle is string ? FillAt(0, 0) : default;
            if (fillStyle is string && solid.a >= 1 && _bright == 1)
            {
                // an opaque colour: one value copied over the clipped rectangle
                var c32 = (Color32)solid;
                int cx0 = Math.Max(x0, _cx0), cy0 = Math.Max(y0, _cy0), cx1 = Math.Min(x1, _cx1), cy1 = Math.Min(y1, _cy1);
                for (int yy = cy0; yy < cy1; yy += 1)
                {
                    int row = yy * W;
                    for (int xx = cx0; xx < cx1; xx += 1) _c.pixels[row + xx] = c32;
                }
            }
            else
                for (int yy = y0; yy < y1; yy += 1)
                    for (int xx = x0; xx < x1; xx += 1) Blend(xx, yy, FillAt(xx + 0.5f, yy + 0.5f));
            if (x <= 0 && y <= 0 && x + w >= W && y + h >= H && globalAlpha >= 1) ClearTexts();
            _c.MarkDirty();
        }

        public void strokeRect(float x, float y, float w, float h)
        {
            beginPath();
            rect(x, y, w, h);
            stroke();
        }

        // ---- paths ----
        public void beginPath() => _path.Clear();
        public void moveTo(float x, float y) => _path.Add(new List<Vector2> { Tx(x, y) });
        public void lineTo(float x, float y)
        {
            if (_path.Count == 0) { moveTo(x, y); return; }
            _path[_path.Count - 1].Add(Tx(x, y));
        }
        public void closePath()
        {
            if (_path.Count == 0 || _path[_path.Count - 1].Count == 0) return;
            var p = _path[_path.Count - 1];
            p.Add(p[0]);
        }
        public void rect(float x, float y, float w, float h)
        {
            moveTo(x, y); lineTo(x + w, y); lineTo(x + w, y + h); lineTo(x, y + h); closePath();
        }
        public void arc(float cx, float cy, float r, float a0, float a1, bool ccw = false)
        {
            float sweep = a1 - a0;
            if (!ccw && sweep < 0) sweep += Mathf.PI * 2;
            if (ccw && sweep > 0) sweep -= Mathf.PI * 2;
            if (Mathf.Abs(a1 - a0) >= Mathf.PI * 2) sweep = ccw ? -Mathf.PI * 2 : Mathf.PI * 2;
            int n = Mathf.Max(8, (int)(Mathf.Abs(sweep) * Mathf.Max(r, 1) / 2));
            for (int i = 0; i <= n; i += 1)
            {
                float a = a0 + sweep * i / n;
                var pt = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
                if (i == 0 && (_path.Count == 0 || _path[_path.Count - 1].Count == 0)) moveTo(pt.x, pt.y);
                else lineTo(pt.x, pt.y);
            }
        }

        /// <summary>Non-zero fill of the current path (no anti-aliasing).</summary>
        public void fill()
        {
            var edges = new List<(Vector2 a, Vector2 b)>();
            foreach (var sub in _path)
                for (int i = 0; i < sub.Count; i += 1) edges.Add((sub[i], sub[(i + 1) % sub.Count]));
            if (edges.Count == 0) return;
            float minY = edges.Min(e => Mathf.Min(e.a.y, e.b.y)), maxY = edges.Max(e => Mathf.Max(e.a.y, e.b.y));
            for (int y = Mathf.Max(0, Mathf.FloorToInt(minY)); y < Mathf.Min(H, Mathf.CeilToInt(maxY)); y += 1)
            {
                float sy = y + 0.5f;
                var xs = new List<(float x, int dir)>();
                foreach (var (a, b) in edges)
                {
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy))
                        xs.Add((a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x), a.y < b.y ? 1 : -1));
                }
                xs.Sort((p, q) => p.x.CompareTo(q.x));
                int winding = 0;
                for (int i = 0; i < xs.Count - 1; i += 1)
                {
                    winding += xs[i].dir;
                    if (winding == 0) continue;
                    for (int x = Mathf.Max(0, Mathf.RoundToInt(xs[i].x)); x < Mathf.Min(W, Mathf.RoundToInt(xs[i + 1].x)); x += 1)
                        Blend(x, y, FillAt(x + 0.5f, sy));
                }
            }
            _c.MarkDirty();
        }

        public void stroke()
        {
            var c = Css.Color(strokeStyle);
            c.a *= globalAlpha;
            float hw = Mathf.Max(0.5f, lineWidth / 2);
            foreach (var sub in _path)
                for (int i = 0; i + 1 < sub.Count; i += 1) ThickLine(sub[i], sub[i + 1], hw, c);
            _c.MarkDirty();
        }

        void ThickLine(Vector2 a, Vector2 b, float hw, Color c)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - hw), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + hw);
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - hw), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + hw);
            var d = b - a;
            float len2 = Mathf.Max(1e-6f, d.sqrMagnitude);
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y += 1)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x += 1)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / len2);
                    if ((a + d * t - p).sqrMagnitude <= hw * hw) Blend(x, y, c);
                }
        }

        // ---- images ----
        public ImageData createImageData(int w, int h) => new ImageData(w, h);

        public ImageData getImageData(int x, int y, int w, int h)
        {
            var img = new ImageData(w, h);
            for (int yy = 0; yy < h; yy += 1)
                for (int xx = 0; xx < w; xx += 1)
                {
                    int sx = x + xx, sy = y + yy, o = (yy * w + xx) * 4;
                    if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                    var p = _c.pixels[sy * W + sx];
                    img.data[o] = p.r; img.data[o + 1] = p.g; img.data[o + 2] = p.b; img.data[o + 3] = p.a;
                }
            return img;
        }

        public void putImageData(ImageData img, int x, int y)
        {
            for (int yy = 0; yy < img.height; yy += 1)
                for (int xx = 0; xx < img.width; xx += 1)
                {
                    int dx = x + xx, dy = y + yy, o = (yy * img.width + xx) * 4;
                    if (dx < 0 || dy < 0 || dx >= W || dy >= H) continue;
                    _c.pixels[dy * W + dx] = new Color32(img.data[o], img.data[o + 1], img.data[o + 2], img.data[o + 3]);
                }
            _c.MarkDirty();
        }

        /// <summary>drawImage(canvas, dx, dy[, dw, dh]) or drawImage(canvas, sx, sy, sw, sh, dx, dy, dw, dh).</summary>
        public void drawImage(CanvasEl src, float a, float b, float c = -1, float d = -1, float e = -1, float f = -1, float g = -1, float h = -1)
        {
            float sx = 0, sy = 0, sw = src.width, sh = src.height, dx, dy, dw, dh;
            if (e >= 0 || f >= 0) { sx = a; sy = b; sw = c; sh = d; dx = e; dy = f; dw = g; dh = h; }
            else { dx = a; dy = b; dw = c >= 0 ? c : sw; dh = d >= 0 ? d : sh; }
            DrawPixels(src.pixels, src.width, src.height, sx, sy, sw, sh, dx, dy, dw, dh);
        }

        public void drawImage(Texture2D src, float dx, float dy, float dw, float dh) => drawImage(src, 0, 0, src.width, src.height, dx, dy, dw, dh);

        /// <summary>drawImage(image, sx, sy, sw, sh, dx, dy, dw, dh): a part of a picture, scaled.</summary>
        public void drawImage(Texture2D src, float sx, float sy, float sw, float sh, float dx, float dy, float dw, float dh)
        {
            DrawPixels(TopDown(src), src.width, src.height, sx, sy, sw, sh, dx, dy, dw, dh);
        }

        // A picture's pixels top-down, read once per texture (the HD renderer draws the same tile sheet
        // hundreds of times a frame).
        static readonly ConditionalWeakTable<Texture2D, Color32[]> TopDownCache = new ConditionalWeakTable<Texture2D, Color32[]>();
        static Color32[] TopDown(Texture2D src)
        {
            return TopDownCache.GetValue(src, t =>
            {
                var px = t.GetPixels32();
                var top = new Color32[px.Length];
                for (int y = 0; y < t.height; y += 1) Array.Copy(px, y * t.width, top, (t.height - 1 - y) * t.width, t.width);
                return top;
            });
        }

        void DrawPixels(Color32[] px, int pw, int ph, float sx, float sy, float sw, float sh, float dx, float dy, float dw, float dh)
        {
            // the destination through the transform; a negative scale mirrors the picture
            var p0 = Tx(dx, dy); var p1 = Tx(dx + dw, dy + dh);
            float X0 = p0.x, Y0 = p0.y, DW = p1.x - p0.x, DH = p1.y - p0.y;
            if (DW == 0 || DH == 0) return;
            // the covered pixels, inside the canvas and the clip
            int x0 = Math.Max(Math.Max(0, (int)Mathf.Min(p0.x, p1.x)), _cx0), x1 = Math.Min(Math.Min(W, (int)Mathf.Max(p0.x, p1.x)), _cx1);
            int y0 = Math.Max(Math.Max(0, (int)Mathf.Min(p0.y, p1.y)), _cy0), y1 = Math.Min(Math.Min(H, (int)Mathf.Max(p0.y, p1.y)), _cy1);
            if (x0 >= x1 || y0 >= y1) return;
            // the source column of each destination column (nearest), -1 outside the picture
            var cols = new int[x1 - x0];
            for (int x = x0; x < x1; x += 1)
            {
                int ux = (int)(sx + (x - X0 + 0.5f) / DW * sw);
                cols[x - x0] = ux < 0 || ux >= pw ? -1 : ux;
            }
            int alpha = Mathf.Clamp(Mathf.RoundToInt(globalAlpha * 255), 0, 255);
            int bright = Mathf.RoundToInt(_bright * 256);
            var dst = _c.pixels;
            for (int y = y0; y < y1; y += 1)
            {
                int uy = (int)(sy + (y - Y0 + 0.5f) / DH * sh);
                if (uy < 0 || uy >= ph) continue;
                int srow = uy * pw, drow = y * W;
                for (int i = 0; i < cols.Length; i += 1)
                {
                    int ux = cols[i];
                    if (ux < 0) continue;
                    var s = px[srow + ux];
                    int sa = s.a * alpha / 255;
                    if (sa == 0) continue;
                    int r = s.r, g = s.g, b = s.b;
                    if (bright != 256) { r = Math.Min(255, r * bright >> 8); g = Math.Min(255, g * bright >> 8); b = Math.Min(255, b * bright >> 8); }
                    int di = drow + x0 + i;
                    if (sa == 255) { dst[di] = new Color32((byte)r, (byte)g, (byte)b, 255); continue; }
                    var d = dst[di];
                    // source over destination, straight alpha (as Blend does, in integers)
                    int oa = sa + d.a * (255 - sa) / 255;
                    if (oa == 0) { dst[di] = new Color32(0, 0, 0, 0); continue; }
                    int k = d.a * (255 - sa) / 255;
                    dst[di] = new Color32((byte)((r * sa + d.r * k) / oa), (byte)((g * sa + d.g * k) / oa), (byte)((b * sa + d.b * k) / oa), (byte)oa);
                }
            }
            _c.MarkDirty();
        }

        public CanvasGradient createLinearGradient(float x0, float y0, float x1, float y1) => new CanvasGradient { x0 = x0, y0 = y0, x1 = x1, y1 = y1 };

        // ---- text: drawn into the pixels with an OS font (CanvasText), as a browser canvas does ----
        public void fillText(string text, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            var (size, bold) = ParseFont();
            var p = Tx(x, y);
            float scale = Mathf.Abs(_a);
            var color = FillAt(x, y);
            if (_bright != 1) color = new Color(Mathf.Min(1, color.r * _bright), Mathf.Min(1, color.g * _bright), Mathf.Min(1, color.b * _bright), color.a);
            CanvasText.Draw(_c.pixels, W, H, text, p.x, p.y, Mathf.Max(1, Mathf.RoundToInt(size * scale)), bold, FontFamilies(), color, textAlign, textBaseline,
                Math.Max(0, _cx0), Math.Max(0, _cy0), Math.Min(W, _cx1), Math.Min(H, _cy1));
            _c.MarkDirty();
        }

        public float measureText(string text)
        {
            var (size, bold) = ParseFont();
            return CanvasText.Measure(text ?? "", Mathf.Max(1, Mathf.RoundToInt(size)), bold, FontFamilies());
        }

        /// <summary>The family list of ctx.font ("bold 18px Georgia, serif" -> Georgia, Times New Roman).</summary>
        string[] FontFamilies()
        {
            int px = font.IndexOf("px", StringComparison.Ordinal);
            string list = px >= 0 ? font.Substring(px + 2) : "";
            int slash = list.IndexOf(' ');
            if (list.StartsWith("/") && slash > 0) list = list.Substring(slash);
            var names = list.Split(',').Select(f => f.Trim().Trim('"', '\'')).Where(f => f.Length > 0)
                .Select(f => f == "sans-serif" ? "Arial" : f == "serif" ? "Times New Roman" : f == "monospace" ? "Consolas" : f).ToList();
            names.Add("Segoe UI");
            return names.ToArray();
        }

        (float size, bool bold) ParseFont()
        {
            float size = 10;
            foreach (var part in font.Split(' '))
                if (part.EndsWith("px") && float.TryParse(part.Substring(0, part.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) size = s;
            return (size, font.Contains("bold"));
        }
    }
}
