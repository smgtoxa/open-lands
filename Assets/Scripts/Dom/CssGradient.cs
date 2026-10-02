// CSS gradient backgrounds, which USS lacks: linear-gradient, repeating-linear-gradient and radial-gradient
// (layered with commas, over a plain colour) painted into a texture the size of the element. conic-gradient
// sweeps round its centre.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace LolHost
{
    public static class CssGradient
    {
        static readonly Dictionary<(string, int, int), Texture2D> Cache = new Dictionary<(string, int, int), Texture2D>();
        static readonly Dictionary<(string, int, int), int> Used = new Dictionary<(string, int, int), int>();
        /// <summary>background value -> its CSS mask (the generated tables have no mask column: the host adds them)</summary>
        public static readonly Dictionary<string, string> Masks = new Dictionary<string, string>
        {
            ["conic-gradient(from 0deg, rgba(95, 224, 255, 0), rgba(95, 224, 255, .55), rgba(160, 120, 255, .15), rgba(95, 224, 255, 0) 70%)"] =
                "radial-gradient(circle, transparent 12%, #000 38%, #000 60%, transparent 72%)",
        };

        /// <summary>A painted texture is still shown (an element that did not need painting again this frame).</summary>
        public static void Keep(string value, int w, int h) => Used[(value, Mathf.Clamp(w, 1, 2048), Mathf.Clamp(h, 1, 2048))] = Time.frameCount;

        /// <summary>The texture for a CSS background value at w x h pixels (cached).</summary>
        public static Texture2D Paint(string value, int w, int h)
        {
            w = Mathf.Clamp(w, 1, 2048);
            h = Mathf.Clamp(h, 1, 2048);
            if (Cache.TryGetValue((value, w, h), out var tex) && tex != null) { Used[(value, w, h)] = Time.frameCount; return tex; }
            // full: only the textures nobody asked for in the last few seconds go (a destroyed texture still on an
            // element shows as Unity's magenta: an animated box that keeps changing size filled the cache)
            if (Cache.Count > 256)
                foreach (var old in Cache.Keys.Where(k => !Used.TryGetValue(k, out var f) || Time.frameCount - f > 300).ToList())
                { UnityEngine.Object.Destroy(Cache[old]); Cache.Remove(old); Used.Remove(old); }
            var px = new Color[w * h];
            var layers = Split(value, ',');
            // the last layer is the bottom one
            for (int li = layers.Count - 1; li >= 0; li -= 1) PaintLayer(layers[li].Trim(), px, w, h);
            // CSS mask (the page's mask-image): its alpha multiplies the picture's
            if (Masks.TryGetValue(value, out var mask))
            {
                var mp = new Color[w * h];
                foreach (var layer in Split(mask, ',')) PaintLayer(layer.Trim(), mp, w, h);
                for (int i = 0; i < px.Length; i += 1) px[i].a *= mp[i].a;
            }
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            // texture rows run bottom-up, the page top-down
            var rows = new Color[w * h];
            for (int y = 0; y < h; y += 1) Array.Copy(px, y * w, rows, (h - 1 - y) * w, w);
            tex.SetPixels(rows);
            tex.Apply(false, true);
            Cache[(value, w, h)] = tex;
            Used[(value, w, h)] = Time.frameCount;
            return tex;
        }

        public static bool IsGradient(string value) => value != null && value.Contains("gradient(");

        static void PaintLayer(string layer, Color[] px, int w, int h)
        {
            int open = layer.IndexOf('(');
            string fn = open > 0 ? layer.Substring(0, open).Trim() : "";
            if (!fn.EndsWith("gradient"))
            {
                if (layer.StartsWith("url(")) return;
                var c = Css.Color(layer);
                if (c.a > 0) for (int i = 0; i < px.Length; i += 1) px[i] = Over(c, px[i]);
                return;
            }
            var args = Split(layer.Substring(open + 1, layer.LastIndexOf(')') - open - 1), ',').Select(a => a.Trim()).ToList();
            if (fn == "conic-gradient")
            {
                // the colour sweeps clockwise from the top round the centre; stops in % of the turn (or degrees)
                float from = 0;
                if (args.Count > 0 && args[0].StartsWith("from "))
                {
                    var f = args[0].Substring(5).Trim().Split(' ')[0];
                    if (f.EndsWith("deg")) float.TryParse(f.Substring(0, f.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out from);
                    args = args.Skip(1).ToList();
                }
                var stops = Stops(args.Select(x => System.Text.RegularExpressions.Regex.Replace(x, @"(-?[\d.]+)deg", m => (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 3.6f).ToString(CultureInfo.InvariantCulture) + "%")).ToList(), 100);
                if (stops.Count == 0) return;
                for (int y = 0; y < h; y += 1)
                    for (int x = 0; x < w; x += 1)
                    {
                        float deg = Mathf.Atan2(x + 0.5f - w / 2f, -(y + 0.5f - h / 2f)) * Mathf.Rad2Deg - from;
                        float t = Mod(deg, 360f) / 3.6f;
                        px[y * w + x] = Over(At(stops, t), px[y * w + x]);
                    }
                return;
            }
            bool repeating = fn.StartsWith("repeating-");
            if (fn.EndsWith("radial-gradient")) Radial(args, px, w, h, repeating);
            else Linear(args, px, w, h, repeating);
        }

        // ---- linear ----
        static void Linear(List<string> args, Color[] px, int w, int h, bool repeating)
        {
            float angle = 180;
            if (args.Count > 0 && (args[0].EndsWith("deg") || args[0].StartsWith("to ") || args[0].EndsWith("turn") || args[0].EndsWith("rad")))
            {
                angle = Angle(args[0]);
                args = args.Skip(1).ToList();
            }
            float a = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            float length = Mathf.Abs(w * Mathf.Sin(a)) + Mathf.Abs(h * Mathf.Cos(a));
            var stops = Stops(args, length);
            if (stops.Count == 0) return;
            float period = repeating ? stops[stops.Count - 1].pos - stops[0].pos : 0;
            var center = new Vector2(w / 2f, h / 2f);
            for (int y = 0; y < h; y += 1)
                for (int x = 0; x < w; x += 1)
                {
                    float t = Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - center, dir) + length / 2;
                    if (period > 0) t = stops[0].pos + Mod(t - stops[0].pos, period);
                    px[y * w + x] = Over(At(stops, t), px[y * w + x]);
                }
        }

        static float Angle(string s)
        {
            s = s.Trim();
            if (s.StartsWith("to "))
            {
                var d = s.Substring(3);
                bool top = d.Contains("top"), bottom = d.Contains("bottom"), left = d.Contains("left"), right = d.Contains("right");
                if (top && right) return 45; if (bottom && right) return 135; if (bottom && left) return 225; if (top && left) return 315;
                return top ? 0 : right ? 90 : bottom ? 180 : 270;
            }
            if (s.EndsWith("deg")) return F(s.Substring(0, s.Length - 3));
            if (s.EndsWith("turn")) return F(s.Substring(0, s.Length - 4)) * 360;
            if (s.EndsWith("rad")) return F(s.Substring(0, s.Length - 3)) * Mathf.Rad2Deg;
            return 180;
        }

        // ---- radial ----
        static void Radial(List<string> args, Color[] px, int w, int h, bool repeating)
        {
            bool circle = false;
            var center = new Vector2(w / 2f, h / 2f);
            if (args.Count > 0 && (args[0].Contains("circle") || args[0].Contains("ellipse") || args[0].StartsWith("at ") || args[0].Contains(" at ") || args[0].Contains("closest") || args[0].Contains("farthest")))
            {
                string shape = args[0];
                circle = shape.Contains("circle");
                int at = shape.IndexOf("at ", StringComparison.Ordinal);
                if (at >= 0)
                {
                    var p = shape.Substring(at + 3).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    center = new Vector2(Pos(p.Length > 0 ? p[0] : "50%", w), Pos(p.Length > 1 ? p[1] : "50%", h));
                }
                args = args.Skip(1).ToList();
            }
            // farthest-corner (the default size)
            float dx = Mathf.Max(center.x, w - center.x), dy = Mathf.Max(center.y, h - center.y);
            float rx, ry;
            if (circle) rx = ry = Mathf.Sqrt(dx * dx + dy * dy);
            else { rx = dx * Mathf.Sqrt(2); ry = dy * Mathf.Sqrt(2); }
            var stops = Stops(args, rx);
            if (stops.Count == 0) return;
            float period = repeating ? stops[stops.Count - 1].pos - stops[0].pos : 0;
            for (int y = 0; y < h; y += 1)
                for (int x = 0; x < w; x += 1)
                {
                    float ex = (x + 0.5f - center.x), ey = (y + 0.5f - center.y) * (rx / Mathf.Max(ry, 0.001f));
                    float t = Mathf.Sqrt(ex * ex + ey * ey);
                    if (period > 0) t = stops[0].pos + Mod(t - stops[0].pos, period);
                    px[y * w + x] = Over(At(stops, t), px[y * w + x]);
                }
        }

        static float Pos(string s, float size)
        {
            switch (s) { case "left": case "top": return 0; case "center": return size / 2; case "right": case "bottom": return size; }
            return Len(s, size);
        }

        // ---- colour stops ----
        struct Stop { public Color color; public float pos; }

        static Color? StopColor(string s)
        {
            int paren = s.IndexOf(')');
            string c = s.StartsWith("rgb") || s.StartsWith("hsl") ? s.Substring(0, paren + 1) : s.Split(' ')[0];
            try { return Css.Color(c); } catch (Exception) { return null; }
        }

        /// <summary>"color [pos [pos]]" stops, positions resolved to px along a line of the given length.</summary>
        static List<Stop> Stops(List<string> args, float length)
        {
            var raw = new List<(Color c, float? p)>();
            foreach (var a in args)
            {
                string s = a.Trim();
                int paren = s.StartsWith("rgb") || s.StartsWith("hsl") ? s.IndexOf(')') + 1 : s.IndexOf(' ') < 0 ? s.Length : s.IndexOf(' ');
                var color = Css.Color(s.Substring(0, paren));
                var rest = s.Substring(paren).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (rest.Length == 0) raw.Add((color, null));
                foreach (var r in rest) raw.Add((color, Len(r, length)));
            }
            if (raw.Count == 0) return new List<Stop>();
            var pos = raw.Select(r => r.p).ToArray();
            if (!pos[0].HasValue) pos[0] = 0;
            if (!pos[pos.Length - 1].HasValue) pos[pos.Length - 1] = length;
            // a stop before its predecessor sits on it; missing positions spread evenly
            for (int i = 1; i < pos.Length; i += 1) if (pos[i].HasValue && pos[i] < pos[i - 1]) pos[i] = pos[i - 1];
            for (int i = 1; i < pos.Length; i += 1)
            {
                if (pos[i].HasValue) continue;
                int j = i;
                while (!pos[j].HasValue) j += 1;
                for (int k = i; k < j; k += 1) pos[k] = pos[i - 1] + (pos[j] - pos[i - 1]) * (k - i + 1) / (j - i + 1);
            }
            return raw.Select((r, i) => new Stop { color = r.c, pos = pos[i].Value }).ToList();
        }

        static Color At(List<Stop> stops, float t)
        {
            if (t <= stops[0].pos) return stops[0].color;
            for (int i = 1; i < stops.Count; i += 1)
            {
                if (t > stops[i].pos) continue;
                var a = stops[i - 1];
                var b = stops[i];
                float span = b.pos - a.pos;
                float k = span <= 0 ? 1 : (t - a.pos) / span;
                // premultiplied, as browsers interpolate
                var pa = new Color(a.color.r * a.color.a, a.color.g * a.color.a, a.color.b * a.color.a, a.color.a);
                var pb = new Color(b.color.r * b.color.a, b.color.g * b.color.a, b.color.b * b.color.a, b.color.a);
                var m = Color.Lerp(pa, pb, k);
                return m.a > 0 ? new Color(m.r / m.a, m.g / m.a, m.b / m.a, m.a) : new Color(0, 0, 0, 0);
            }
            return stops[stops.Count - 1].color;
        }

        static Color Over(Color top, Color under)
        {
            float a = top.a + under.a * (1 - top.a);
            if (a <= 0) return new Color(0, 0, 0, 0);
            return new Color((top.r * top.a + under.r * under.a * (1 - top.a)) / a, (top.g * top.a + under.g * under.a * (1 - top.a)) / a, (top.b * top.a + under.b * under.a * (1 - top.a)) / a, a);
        }

        static float Mod(float a, float m) => ((a % m) + m) % m;

        static float Len(string s, float basis)
        {
            s = s.Trim();
            if (s.EndsWith("%")) return F(s.Substring(0, s.Length - 1)) * basis / 100;
            if (s.EndsWith("rem")) return F(s.Substring(0, s.Length - 3)) * 14;
            if (s.EndsWith("em")) return F(s.Substring(0, s.Length - 2)) * 14;
            if (s.EndsWith("px")) return F(s.Substring(0, s.Length - 2));
            return F(s);
        }

        static float F(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Splits on a separator outside parentheses.</summary>
        static List<string> Split(string v, char sep)
        {
            var list = new List<string>();
            int depth = 0, from = 0;
            for (int i = 0; i < v.Length; i += 1)
            {
                if (v[i] == '(') depth += 1;
                else if (v[i] == ')') depth -= 1;
                else if (v[i] == sep && depth == 0) { list.Add(v.Substring(from, i - from)); from = i + 1; }
            }
            list.Add(v.Substring(from));
            return list;
        }
    }
}
