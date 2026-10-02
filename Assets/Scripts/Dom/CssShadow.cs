// box-shadow and outline (CssInfo.Shadows), which USS lacks: painted into a texture and drawn by the
// element's own generateVisualContent as a quad reaching past the box as far as the shadow does (not a
// child: the page reads children by index). Outer shadows are
// painted outside the border box only (CSS clips them there), inset ones inside; each layer is the exact
// Gaussian blur of a rectangle (border-radius is not followed). An element that clips its children
// (overflow: hidden) clips its outer shadow too.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class CssShadow
    {
        sealed class Want { public string shadow, outline, offset; public string Key => $"{shadow}|{outline}|{offset}"; }

        static Dictionary<string, List<int>> _byKey;
        static readonly Dictionary<VisualElement, Want> Wanted = new Dictionary<VisualElement, Want>();
        static readonly Dictionary<VisualElement, (string key, int w, int h)> Painted = new Dictionary<VisualElement, (string, int, int)>();
        static readonly Dictionary<(string, int, int), (Texture2D tex, int pad)> Cache = new Dictionary<(string, int, int), (Texture2D, int)>();

        /// <summary>The cascade for one element (called by the style scan).</summary>
        public static void Match(VisualElement e)
        {
            if (!(e.userData is DomData)) return;
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, List<int>>();
                for (int i = 0; i < CssInfo.Shadows.Length; i += 1)
                {
                    string k = CssLayout.KeyOf(CssInfo.Shadows[i].sel);
                    if (!_byKey.TryGetValue(k, out var l)) _byKey[k] = l = new List<int>();
                    l.Add(i);
                }
            }
            var hits = new List<int>();
            foreach (var c in e.GetClasses()) if (_byKey.TryGetValue("." + c, out var l)) hits.AddRange(l);
            if (Dom.Data(e).id != null && _byKey.TryGetValue("#" + Dom.Data(e).id, out var byId)) hits.AddRange(byId);
            if (hits.Count == 0 && !Wanted.ContainsKey(e)) return;
            hits.Sort();
            Want want = null;
            foreach (int i in hits)
            {
                var (sel, shadow, outline, offset) = CssInfo.Shadows[i];
                if (!Dom.MatchesSelector(e, sel)) continue;
                want = want ?? new Want();
                if (shadow != null) want.shadow = shadow;
                if (outline != null) want.outline = outline;
                if (offset != null) want.offset = offset;
            }
            bool none = want == null || ((want.shadow == null || want.shadow == "none") && (want.outline == null || want.outline == "none" || want.outline.StartsWith("0")));
            if (none) { if (Wanted.Remove(e)) Remove(e); return; }
            Wanted[e] = want;
        }

        /// <summary>textures kept (test runs: --texcache N, a small one fills at once)</summary>
        public static int Limit = 256;
        static readonly Dictionary<VisualElement, (Texture2D tex, int pad)> Drawn = new Dictionary<VisualElement, (Texture2D, int)>();
        static readonly HashSet<VisualElement> Hooked = new HashSet<VisualElement>();

        /// <summary>Once a frame: shadows follow their box's size.</summary>
        public static void Tick()
        {
            foreach (var e in Wanted.Keys.ToList())
            {
                if (e.panel == null) { Wanted.Remove(e); Painted.Remove(e); Drawn.Remove(e); continue; }
                var want = Wanted[e];
                int w = Mathf.RoundToInt(e.layout.width), h = Mathf.RoundToInt(e.layout.height);
                if (w <= 0 || h <= 0 || float.IsNaN(e.layout.width)) continue;
                if (Painted.TryGetValue(e, out var p) && p.key == want.Key && p.w == w && p.h == h) continue;
                Painted[e] = (want.Key, w, h);
                var rs = e.resolvedStyle;
                var radii = new[] { rs.borderTopLeftRadius, rs.borderTopRightRadius, rs.borderBottomRightRadius, rs.borderBottomLeftRadius };
                Drawn[e] = Paint(want, w, h, Mathf.RoundToInt(rs.borderLeftWidth), Mathf.RoundToInt(rs.borderTopWidth), Mathf.RoundToInt(rs.borderRightWidth), Mathf.RoundToInt(rs.borderBottomWidth), radii);
                if (Hooked.Add(e)) e.generateVisualContent += ctx => Draw(e, ctx);
                e.MarkDirtyRepaint();
            }
        }

        static void Remove(VisualElement e)
        {
            Painted.Remove(e);
            if (Drawn.Remove(e)) e.MarkDirtyRepaint();
        }

        /// <summary>The shadow quad, in the element's space (its border box starts at 0, 0).</summary>
        static void Draw(VisualElement e, MeshGenerationContext ctx)
        {
            if (!Drawn.TryGetValue(e, out var d) || d.tex == null) return;
            var r = e.layout;
            float x0 = -d.pad, y0 = -d.pad, x1 = r.width + d.pad, y1 = r.height + d.pad;
            var mesh = ctx.Allocate(4, 6, d.tex);
            var uv = mesh.uvRegion;
            var tint = Color.white;
            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMin, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMax, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMax, uv.yMin) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMin, uv.yMin) });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
        }

        // ---- painting ----
        struct Shadow { public bool inset; public float x, y, blur, spread; public Color color; }

        /// <summary>The shadows of a w x h border box; inset ones fill the padding box inside the borders.</summary>
        static (Texture2D, int) Paint(Want want, int w, int h, int bl, int bt, int br, int bb, float[] radii)
        {
            for (int k = 0; k < 4; k += 1) if (float.IsNaN(radii[k])) radii[k] = 0;
            var key = (want.Key + $"|{bl},{bt},{br},{bb}|{string.Join(",", radii)}", w, h);
            bool round = radii.Any(r => r > 0.01f);
            // the padding box's corners: the border's inner curve
            var inner = new[] { Mathf.Max(0, radii[0] - Mathf.Max(bl, bt)), Mathf.Max(0, radii[1] - Mathf.Max(br, bt)), Mathf.Max(0, radii[2] - Mathf.Max(br, bb)), Mathf.Max(0, radii[3] - Mathf.Max(bl, bb)) };
            if (Cache.TryGetValue(key, out var hit) && hit.tex != null) return hit;
            var layers = Parse(want.shadow);
            // outline: a ring outside the border edge at outline-offset (inside it when negative)
            (float width, float offset, Color color)? outline = null;
            if (want.outline != null && want.outline != "none")
            {
                var parts = Split(want.outline, ' ');
                float ow = parts.Select(Px).FirstOrDefault(v => !float.IsNaN(v));
                var oc = parts.Select(TryColor).FirstOrDefault(c => c.HasValue) ?? Color.white;
                float off = want.offset != null ? Px(want.offset) : 0;
                if (ow > 0) outline = (ow, float.IsNaN(off) ? 0 : off, oc);
            }
            float reach = 0;
            foreach (var l in layers) if (!l.inset) reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.y)) + l.spread + l.blur);
            if (outline.HasValue) reach = Mathf.Max(reach, outline.Value.width + Mathf.Max(0, outline.Value.offset));
            int pad = Mathf.CeilToInt(reach);
            int W = w + pad * 2, H = h + pad * 2;
            if (W * H > 4096 * 4096) return (null, pad);
            var px = new Color[W * H];
            // CSS paints the first shadow on top: from the last to the first
            for (int li = layers.Count - 1; li >= 0; li -= 1)
            {
                var l = layers[li];
                float sigma = l.blur / 2;
                float x0, x1, y0, y1;
                if (!l.inset) { x0 = l.x - l.spread; x1 = w + l.x + l.spread; y0 = l.y - l.spread; y1 = h + l.y + l.spread; }
                else { x0 = bl + l.x + l.spread; x1 = w - br + l.x - l.spread; y0 = bt + l.y + l.spread; y1 = h - bb + l.y - l.spread; }
                var fx = new float[W];
                var fy = new float[H];
                for (int i = 0; i < W; i += 1) fx[i] = Cover(i - pad + 0.5f, x0, x1, sigma);
                for (int j = 0; j < H; j += 1) fy[j] = Cover(j - pad + 0.5f, y0, y1, sigma);
                // rounded corners: the shape's radii grow with the spread (outer) or shrink with it (inset)
                var shapeR = (l.inset ? inner : radii).Select(r => r > 0 ? Mathf.Max(0, r + (l.inset ? -l.spread : l.spread)) : 0).ToArray();
                for (int j = 0; j < H; j += 1)
                {
                    float by = j - pad + 0.5f;
                    for (int i = 0; i < W; i += 1)
                    {
                        float bx = i - pad + 0.5f;
                        bool inside = round ? Sdf(bx, by, 0, 0, w, h, radii) < 0 : bx >= 0 && bx < w && by >= 0 && by < h;
                        bool padding = round ? Sdf(bx, by, bl, bt, w - br, h - bb, inner) < 0 : bx >= bl && bx < w - br && by >= bt && by < h - bb;
                        // separable (exact) for square boxes; the rounded box's signed distance otherwise
                        float cover = round ? Falloff(Sdf(bx, by, x0, y0, x1, y1, shapeR), sigma) : fx[i] * fy[j];
                        float a;
                        if (!l.inset) { if (inside) continue; a = cover; }
                        else { if (!padding) continue; a = 1 - cover; }
                        if (a <= 0.001f) continue;
                        var c = l.color;
                        c.a *= a;
                        px[(H - 1 - j) * W + i] = Over(c, px[(H - 1 - j) * W + i]);
                    }
                }
            }
            if (outline.HasValue)
            {
                var (ow, off, oc) = outline.Value;
                // the ring between the border box grown by offset + width and by offset (corners follow the radii)
                var r0 = radii.Select(r => r > 0 ? Mathf.Max(0, r + off + ow) : 0).ToArray();
                var r1 = radii.Select(r => r > 0 ? Mathf.Max(0, r + off) : 0).ToArray();
                for (int j = 0; j < H; j += 1)
                    for (int i = 0; i < W; i += 1)
                    {
                        float bx = i - pad + 0.5f, by = j - pad + 0.5f;
                        float a = Mathf.Clamp01(0.5f - Sdf(bx, by, -off - ow, -off - ow, w + off + ow, h + off + ow, r0)) * Mathf.Clamp01(0.5f + Sdf(bx, by, -off, -off, w + off, h + off, r1));
                        if (a <= 0.001f) continue;
                        var c = oc; c.a *= a;
                        px[(H - 1 - j) * W + i] = Over(c, px[(H - 1 - j) * W + i]);
                    }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(false, true);
            // full: only the shadows no element shows any more go. Destroying one still drawn left its box painted plain
            // white until a restart (the 3D view's frame, the champion portraits: the "white screen")
            if (Cache.Count > Limit)
            {
                var shown = new HashSet<Texture2D>(Drawn.Values.Select(d => d.tex));
                foreach (var k in Cache.Keys.ToList())
                    if (!shown.Contains(Cache[k].tex)) { if (Cache[k].tex != null) UnityEngine.Object.Destroy(Cache[k].tex); Cache.Remove(k); }
            }
            Cache[key] = (tex, pad);
            return (tex, pad);
        }

        /// <summary>How much of [a, b) a Gaussian of sigma centred at x covers (1 inside a sharp edge).</summary>
        /// <summary>Signed distance from a point to a rounded box (negative inside); radii: tl, tr, br, bl.</summary>
        static float Sdf(float px, float py, float x0, float y0, float x1, float y1, float[] radii)
        {
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = Mathf.Max(0, (x1 - x0) / 2), hy = Mathf.Max(0, (y1 - y0) / 2);
            float r = px < cx ? (py < cy ? radii[0] : radii[3]) : (py < cy ? radii[1] : radii[2]);
            r = Mathf.Min(r, Mathf.Min(hx, hy));
            float qx = Mathf.Abs(px - cx) - (hx - r), qy = Mathf.Abs(py - cy) - (hy - r);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        /// <summary>Coverage at a signed distance: a Gaussian blur's edge (sigma), or an anti-aliased hard edge.</summary>
        static float Falloff(float d, float sigma) => sigma < 0.01f ? Mathf.Clamp01(0.5f - d) : Mathf.Clamp01(0.5f * (1 - Erf(d / (sigma * 1.41421356f))));

        static float Cover(float x, float a, float b, float sigma)
        {
            if (sigma < 0.01f) return x >= a && x < b ? 1 : 0;
            float k = 1 / (sigma * 1.41421356f);
            return Mathf.Clamp01(0.5f * (Erf((x - a) * k) - Erf((x - b) * k)));
        }

        static float Erf(float x)
        {
            // Abramowitz and Stegun 7.1.26
            float s = Mathf.Sign(x); x = Mathf.Abs(x);
            float t = 1 / (1 + 0.3275911f * x);
            float y = 1 - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return s * y;
        }

        static Color Over(Color top, Color under)
        {
            float a = top.a + under.a * (1 - top.a);
            if (a <= 0) return new Color(0, 0, 0, 0);
            return new Color((top.r * top.a + under.r * under.a * (1 - top.a)) / a, (top.g * top.a + under.g * under.a * (1 - top.a)) / a, (top.b * top.a + under.b * under.a * (1 - top.a)) / a, a);
        }

        static List<Shadow> Parse(string value)
        {
            var list = new List<Shadow>();
            if (string.IsNullOrEmpty(value) || value == "none") return list;
            foreach (var layer in Split(value, ','))
            {
                var sh = new Shadow { color = new Color(0, 0, 0, 1) };
                var lengths = new List<float>();
                foreach (var tok in Split(layer.Trim(), ' '))
                {
                    if (tok == "inset") { sh.inset = true; continue; }
                    float v = Px(tok);
                    if (!float.IsNaN(v)) { lengths.Add(v); continue; }
                    var c = TryColor(tok);
                    if (c.HasValue) sh.color = c.Value;
                }
                if (lengths.Count < 2) continue;
                sh.x = lengths[0]; sh.y = lengths[1];
                sh.blur = lengths.Count > 2 ? Mathf.Max(0, lengths[2]) : 0;
                sh.spread = lengths.Count > 3 ? lengths[3] : 0;
                list.Add(sh);
            }
            return list;
        }

        static float Px(string t)
        {
            t = t.Trim();
            if (t == "0") return 0;
            if (t.EndsWith("px") && float.TryParse(t.Substring(0, t.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return float.NaN;
        }

        static Color? TryColor(string t)
        {
            if (t.StartsWith("#") || t.StartsWith("rgb") || t == "black" || t == "white" || t == "transparent" || t == "red") return Css.Color(t);
            return null;
        }

        /// <summary>Splits on a separator outside parentheses.</summary>
        static List<string> Split(string v, char sep)
        {
            var list = new List<string>();
            int depth = 0, from = 0;
            for (int i = 0; i <= v.Length; i += 1)
            {
                char c = i < v.Length ? v[i] : sep;
                if (c == '(') depth += 1;
                else if (c == ')') depth -= 1;
                else if (c == sep && depth == 0) { if (i > from) list.Add(v.Substring(from, i - from).Trim()); from = i + 1; }
            }
            return list.Where(x => x.Length > 0).ToList();
        }
    }
}
