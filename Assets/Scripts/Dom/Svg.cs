// Inline SVG for the pages: <svg viewBox> with path / circle / rect / line / polygon / g / text / title
// children (and <use href="#symbol">), drawn with UI Toolkit's Painter2D. Children are SvgNode
// elements carrying attributes; the SvgEl redraws when any of them changes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>An SVG child element (path, circle, g...). Not drawn itself: its SvgEl draws it.</summary>
    public sealed class SvgNode : VisualElement
    {
        public SvgNode(string tag)
        {
            Dom.Data(this).tag = tag;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            // its text (a <text>'s content) is data for the SvgEl, which draws it
            style.visibility = Visibility.Hidden;
            AddToClassList("svg-" + tag);
            AddToClassList("tag-" + tag);   // CSS type selectors (path, circle...)
        }
        public string tag => Dom.Data(this).tag;
        public string Attr(string n) => Dom.Data(this).attributes.TryGetValue(n, out var v) ? v : null;
    }

    public sealed class SvgEl : VisualElement
    {
        /// <summary>The page's &lt;symbol&gt;s: id -> (viewBox, markup nodes). Filled by the generated page.</summary>
        public static readonly Dictionary<string, (string viewBox, Func<SvgNode[]> build)> Symbols = new Dictionary<string, (string, Func<SvgNode[]>)>();

        readonly VisualElement _texts = new VisualElement { pickingMode = PickingMode.Ignore };

        public SvgEl()
        {
            AddToClassList("svg");
            generateVisualContent += Draw;
            _texts.style.position = Position.Absolute;
            _texts.style.left = _texts.style.top = _texts.style.right = _texts.style.bottom = 0;
            hierarchy.Add(_texts);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerMoveEvent>(u => { if (_hasTitles) TitleOnHover(u); if (HoverRules) HoverAt(u.localPosition); });
            RegisterCallback<PointerLeaveEvent>(u => { if (HoverRules) HoverAt(null); });
        }

        public static SvgNode Node(string tag, params (string, string)[] attrs)
        {
            var n = new SvgNode(tag);
            foreach (var (k, v) in attrs) n.SetAttr(k, v);
            return n;
        }

        public void UseSymbol(string id)
        {
            id = id.TrimStart('#');
            if (!Symbols.TryGetValue(id, out var s)) return;
            this.SetAttr("viewBox", s.viewBox);
            foreach (var n in s.build()) Add(n);
            MarkDirtyRepaint();
        }

        public void OnAttribute(string name, string value)
        {
            if (name == "width" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) style.width = w;
            else if (name == "height" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) style.height = h;
            MarkDirtyRepaint();
        }

        /// <summary>An attribute of some node inside changed: redraw the svg that holds it.</summary>
        public static void Invalidate(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent) if (p is SvgEl s) { s.MarkDirtyRepaint(); return; }
        }

        // ------------------------------------------------------------ drawing

        (float scale, float ox, float oy, float vx, float vy) Viewport()
        {
            var r = contentRect;
            string vb = this.GetAttr("viewBox") ?? $"0 0 {r.width} {r.height}";
            var v = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
            float vw = v.Length > 2 ? v[2] : r.width, vh = v.Length > 3 ? v[3] : r.height;
            if (vw <= 0 || vh <= 0 || r.width <= 0) return (1, 0, 0, 0, 0);
            float scale = Mathf.Min(r.width / vw, r.height / vh);
            // centred in the content box, which starts inside the padding and border (r.x / r.y)
            return (scale, r.x + (r.width - vw * scale) / 2, r.y + (r.height - vh * scale) / 2, v.Length > 0 ? v[0] : 0, v.Length > 1 ? v[1] : 0);
        }

        bool _hasTitles;

        void Draw(MeshGenerationContext ctx)
        {
            _hasTitles = this.Query<SvgNode>().Where(n => n.tag == "title").ToList().Count > 0;
            // classes may have changed since the last frame: CSS values are looked up again
            CssCache.Clear();
            // nodes that got handlers before they were in this svg
            foreach (var n in this.Query<SvgNode>().ToList())
                if (n.userData is DomData d) foreach (var type in d.handlers.Keys) if (!_watched.Contains(type)) { var t = type; schedule.Execute(() => WatchNodes(t)); }
            var (scale, ox, oy, vx, vy) = Viewport();
            _next.Clear();
            var m = Matrix4x4.TRS(new Vector3(ox - vx * scale, oy - vy * scale, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            var current = resolvedStyle.color;
            foreach (var c in Children()) if (c is SvgNode n) DrawNode(ctx.painter2D, n, m, current, 1f, scale);
            // the tree cannot change while it renders: the <text> labels go in right after
            string key = string.Join("\u0001", _next.Select(l => $"{l.text}|{l.style.left.value.value}|{l.style.top.value.value}|{l.style.fontSize.value.value}|{l.style.color.value}"));
            if (key == _textsKey) return;
            _textsKey = key;
            var labels = _next.ToList();
            schedule.Execute(() => { _texts.Clear(); foreach (var l in labels) _texts.Add(l); });
        }

        // ---- events on the drawn shapes: the <svg> is the one picked box; a hit test finds the node under the
        // pointer, whose page handlers run, bubbling through its groups (as DOM events bubble) ----
        readonly HashSet<string> _watched = new HashSet<string>();

        /// <summary>A node inside got a handler for this event type: the svg listens for it.</summary>
        public void WatchNodes(string type)
        {
            if (!_watched.Add(type)) return;
            pickingMode = PickingMode.Position;
            switch (type)
            {
                case "click": RegisterCallback<ClickEvent>(u => Dispatch(type, DomEvent.From(u, this), u)); break;
                case "contextmenu": RegisterCallback<ContextClickEvent>(u => Dispatch(type, new DomEvent { button = 2, clientX = u.mousePosition.x, clientY = u.mousePosition.y }, u)); break;
                case "mousedown": case "pointerdown": RegisterCallback<PointerDownEvent>(u => Dispatch(type, DomEvent.From(u, this), u)); break;
                case "mouseup": case "pointerup": RegisterCallback<PointerUpEvent>(u => Dispatch(type, DomEvent.From(u, this), u)); break;
                case "mousemove": case "pointermove": RegisterCallback<PointerMoveEvent>(u => Dispatch(type, DomEvent.From(u, this), u)); break;
            }
        }

        void Dispatch(string type, DomEvent ev, EventBase u)
        {
            var hit = HitTest(this.WorldToLocal(new Vector2(ev.clientX, ev.clientY)));
            if (hit == null) return;
            ev.target = hit;
            for (VisualElement n = hit; n != null && n != this; n = n.parent)
            {
                n.Fire(type, ev);
                if (ev.propagationStopped) { u.StopPropagation(); break; }
            }
        }

        /// <summary>The topmost drawn node whose fill (or stroke) covers a point of the element.</summary>
        public SvgNode HitTest(Vector2 local)
        {
            var (scale, ox, oy, vx, vy) = Viewport();
            var m = Matrix4x4.TRS(new Vector3(ox - vx * scale, oy - vy * scale, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            SvgNode found = null;
            foreach (var c in Children()) if (c is SvgNode n) Hit(n, m, local, scale, ref found);
            return found;
        }

        void Hit(SvgNode n, Matrix4x4 m, Vector2 p, float scale, ref SvgNode found)
        {
            if (n.IsHidden() || n.Attr("display") == "none") return;
            m = m * Transform(n.Attr("transform"));
            switch (n.tag)
            {
                case "g": case "a":
                    foreach (var c in n.Children()) if (c is SvgNode k) Hit(k, m, p, scale, ref found);
                    return;
                case "title": case "defs": case "linearGradient": case "radialGradient": case "stop": case "clipPath": case "mask": case "symbol": case "desc": case "text":
                    return;
            }
            if (Inherit(n, "pointer-events") == "none") return;
            string fill = Inherit(n, "fill") ?? "#000", stroke = Inherit(n, "stroke");
            float reach = Mathf.Max(F(Inherit(n, "stroke-width") ?? "1") * scale / 2, 3);
            foreach (var (pts, closed) in Shape(n))
            {
                var q = pts.Select(v => (Vector2)m.MultiplyPoint3x4(v)).ToList();
                if (fill != "none" && q.Count > 2 && Inside(q, p)) { found = n; return; }
                if (stroke != null && stroke != "none")
                    for (int i = 1; i < q.Count; i += 1)
                        if (Distance(p, q[i - 1], q[i]) <= reach) { found = n; return; }
            }
        }

        static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }

        // :hover on drawn shapes (CssInfo.SvgStyles rules with .is-hover): the hovered node and its groups
        static readonly bool HoverRules = CssInfo.SvgStyles.Any(r => r.sel.Contains("is-hover"));
        readonly List<SvgNode> _hovered = new List<SvgNode>();

        void HoverAt(Vector2? local)
        {
            var hit = local.HasValue ? HitTest(local.Value) : null;
            var chain = new List<SvgNode>();
            for (VisualElement n = hit; n is SvgNode sn; n = n.parent) chain.Add(sn);
            if (chain.Count == _hovered.Count && chain.All(_hovered.Contains)) return;
            foreach (var n in _hovered) n.RemoveFromClassList("is-hover");
            foreach (var n in chain) n.AddToClassList("is-hover");
            _hovered.Clear();
            _hovered.AddRange(chain);
            MarkDirtyRepaint();
        }

        /// <summary>The hovered node's &lt;title&gt; as the svg's tooltip (a browser shows it on hover).</summary>
        void TitleOnHover(PointerMoveEvent u)
        {
            var hit = HitTest(u.localPosition);
            string title = null;
            for (VisualElement n = hit; n != null && n != this && title == null; n = n.parent)
                foreach (var c in n.Children()) if (c is SvgNode t && t.tag == "title") { title = t.GetText(); break; }
            if ((tooltip ?? "") != (title ?? "")) tooltip = title ?? "";
        }

        readonly List<Label> _next = new List<Label>();
        string _textsKey = "";

        void DrawNode(Painter2D p, SvgNode n, Matrix4x4 m, Color current, float opacity, float scale)
        {
            if (n.IsHidden() || n.Attr("display") == "none") return;
            m = m * Transform(n.Attr("transform"));
            if (!string.IsNullOrEmpty(n.Attr("css-transform")))
            {
                var o = (n.Attr("transform-origin") ?? "0 0").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
                var origin = new Vector3(o.Length > 0 ? o[0] : 0, o.Length > 1 ? o[1] : 0, 0);
                m = m * Matrix4x4.Translate(origin) * Transform(n.Attr("css-transform").Replace("deg", "")) * Matrix4x4.Translate(-origin);
            }
            if (float.TryParse(n.Attr("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var op)) opacity *= op;
            string fill = Ref(Inherit(n, "fill") ?? "#000"), strokeAttr = Ref(Inherit(n, "stroke"));
            float sw = F(Inherit(n, "stroke-width") ?? "1");
            switch (n.tag)
            {
                case "g":
                    foreach (var c in n.Children()) if (c is SvgNode k) DrawNode(p, k, m, current, opacity, scale);
                    return;
                case "title": case "defs": case "linearGradient": case "radialGradient": case "stop": case "clipPath": case "mask": case "symbol": case "desc":
                    return;
                case "text":
                {
                    var at = m.MultiplyPoint3x4(new Vector3(F(n.Attr("x") ?? "0"), F(n.Attr("y") ?? "0"), 0));
                    float size = F(Inherit(n, "font-size") ?? "12") * m.lossyScale.x;
                    var l = new Label(n.GetText()) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    l.style.position = Position.Absolute;
                    l.style.fontSize = size;
                    l.style.color = Paint(fill, current, opacity);
                    l.style.unityFontStyleAndWeight = (Inherit(n, "font-weight") ?? "") == "bold" ? FontStyle.Bold : FontStyle.Normal;
                    l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
                    string anchor = Inherit(n, "text-anchor") ?? "start";
                    l.RegisterCallback<GeometryChangedEvent>(_ =>
                    {
                        float w = l.resolvedStyle.width;
                        l.style.left = at.x - (anchor == "middle" ? w / 2 : anchor == "end" ? w : 0);
                        l.style.top = at.y - size * 0.85f;
                    });
                    l.style.left = at.x;
                    l.style.top = at.y - size * 0.85f;
                    _next.Add(l);
                    return;
                }
            }
            var subpaths = Shape(n);
            if (subpaths.Count == 0) return;
            if (fill != "none")
            {
                p.fillColor = Paint(fill, current, opacity * F(Inherit(n, "fill-opacity") ?? "1"));
                p.BeginPath();
                foreach (var sp in subpaths) Emit(p, sp, m, true);
                p.Fill(Inherit(n, "fill-rule") == "evenodd" ? FillRule.OddEven : FillRule.NonZero);
            }
            if (strokeAttr != null && strokeAttr != "none")
            {
                p.strokeColor = Paint(strokeAttr, current, opacity * F(Inherit(n, "stroke-opacity") ?? "1"));
                p.lineWidth = Mathf.Max(0.5f, sw * m.lossyScale.x);
                string cap = Inherit(n, "stroke-linecap"), join = Inherit(n, "stroke-linejoin");
                p.lineCap = cap == "round" ? LineCap.Round : LineCap.Butt;
                p.lineJoin = join == "round" ? LineJoin.Round : join == "bevel" ? LineJoin.Bevel : LineJoin.Miter;
                p.BeginPath();
                foreach (var sp in subpaths) Emit(p, sp, m, false);
                p.Stroke();
            }
        }

        static void Emit(Painter2D p, (List<Vector2> pts, bool closed) sp, Matrix4x4 m, bool forFill)
        {
            if (sp.pts.Count == 0) return;
            p.MoveTo(m.MultiplyPoint3x4(sp.pts[0]));
            for (int i = 1; i < sp.pts.Count; i += 1) p.LineTo(m.MultiplyPoint3x4(sp.pts[i]));
            if (sp.closed || forFill) p.ClosePath();
        }

        /// <summary>An inherited SVG property: on each element from the node up to its svg, a CSS rule
        /// (CssInfo.SvgStyles) beats the presentation attribute.</summary>
        static string Inherit(SvgNode n, string name)
        {
            for (VisualElement e = n; e != null; e = e.parent)
            {
                string css = CssValue(e, name);
                if (css != null) return css;
                if (e is SvgNode s && s.Attr(name) is string v) return v;
                if (e is SvgEl svg) return svg.GetAttr(name);
            }
            return null;
        }

        static readonly Dictionary<(VisualElement, string), string> CssCache = new Dictionary<(VisualElement, string), string>();

        static string CssValue(VisualElement e, string prop)
        {
            if (CssCache.TryGetValue((e, prop), out var cached)) return cached;
            string value = null;
            foreach (var (sel, p, v) in CssInfo.SvgStyles) if (p == prop && Dom.MatchesSelector(e, sel)) value = v;
            if (CssCache.Count > 20000) CssCache.Clear();
            CssCache[(e, prop)] = value;
            return value;
        }

        /// <summary>fill / stroke "url(#id)": a gradient in this svg's defs, painted as its middle stop's colour
        /// (Painter2D fills with one colour).</summary>
        string Ref(string paint)
        {
            if (paint == null || !paint.StartsWith("url(")) return paint;
            string id = paint.Substring(4).Trim(')', ' ', '#', '"', '\'');
            SvgNode grad = null;
            void find(VisualElement e) { foreach (var c in e.Children()) { if (grad != null) return; if (c is SvgNode sn && Dom.Data(sn).id == id) grad = sn; else find(c); } }
            find(this);
            if (grad == null) return "none";
            var stops = grad.Children().OfType<SvgNode>().Where(c => c.tag == "stop").ToList();
            if (stops.Count == 0) return "none";
            var mid = stops[stops.Count / 2];
            return mid.Attr("stop-color") ?? "#000";
        }

        static Color Paint(string s, Color current, float opacity)
        {
            var c = s == "currentColor" ? current : Css.Color(s);
            c.a *= opacity;
            return c;
        }

        static float F(string s) => float.TryParse(s?.Trim().TrimEnd('%').Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static Matrix4x4 Transform(string t)
        {
            var m = Matrix4x4.identity;
            if (string.IsNullOrEmpty(t)) return m;
            foreach (var fn in t.Split(')'))
            {
                int p = fn.IndexOf('(');
                if (p < 0) continue;
                string name = fn.Substring(0, p).Trim();
                var a = fn.Substring(p + 1).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
                if (name == "rotate")
                {
                    float cx = a.Length > 2 ? a[1] : 0, cy = a.Length > 2 ? a[2] : 0;
                    m = m * Matrix4x4.Translate(new Vector3(cx, cy, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, a[0])) * Matrix4x4.Translate(new Vector3(-cx, -cy, 0));
                }
                else if (name == "translate") m = m * Matrix4x4.Translate(new Vector3(a[0], a.Length > 1 ? a[1] : 0, 0));
                else if (name == "scale") m = m * Matrix4x4.Scale(new Vector3(a[0], a.Length > 1 ? a[1] : a[0], 1));
            }
            return m;
        }

        // ------------------------------------------------------------ geometry

        static List<(List<Vector2>, bool)> Shape(SvgNode n)
        {
            var list = new List<(List<Vector2>, bool)>();
            switch (n.tag)
            {
                case "path": return PathData(n.Attr("d") ?? "");
                case "circle": case "ellipse":
                {
                    float cx = F(n.Attr("cx")), cy = F(n.Attr("cy"));
                    float rx = F(n.Attr("r") ?? n.Attr("rx")), ry = n.tag == "circle" ? rx : F(n.Attr("ry"));
                    var pts = new List<Vector2>();
                    for (int i = 0; i < 48; i += 1) { float a = i * Mathf.PI * 2 / 48; pts.Add(new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry)); }
                    list.Add((pts, true));
                    return list;
                }
                case "rect":
                {
                    float x = F(n.Attr("x")), y = F(n.Attr("y")), w = F(n.Attr("width")), h = F(n.Attr("height"));
                    list.Add((new List<Vector2> { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) }, true));
                    return list;
                }
                case "line":
                    list.Add((new List<Vector2> { new Vector2(F(n.Attr("x1")), F(n.Attr("y1"))), new Vector2(F(n.Attr("x2")), F(n.Attr("y2"))) }, false));
                    return list;
                case "polygon": case "polyline":
                {
                    var v = (n.Attr("points") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
                    var pts = new List<Vector2>();
                    for (int i = 0; i + 1 < v.Length; i += 2) pts.Add(new Vector2(v[i], v[i + 1]));
                    list.Add((pts, n.tag == "polygon"));
                    return list;
                }
            }
            return list;
        }

        /// <summary>SVG path data (M L H V C S Q T A Z, absolute and relative) flattened to polylines.</summary>
        public static List<(List<Vector2>, bool)> PathData(string d)
        {
            var result = new List<(List<Vector2>, bool)>();
            var tokens = Tokens(d);
            List<Vector2> cur = null;
            Vector2 pos = Vector2.zero, start = Vector2.zero, lastCtrl = Vector2.zero;
            char cmd = ' ', prev = ' ';
            int i = 0;
            float Num() => i < tokens.Count && tokens[i].num ? tokens[i++].value : 0;
            bool HasNum() => i < tokens.Count && tokens[i].num;
            void Line(Vector2 to) { if (cur == null) { cur = new List<Vector2> { pos }; result.Add((cur, false)); } cur.Add(to); pos = to; }
            while (i < tokens.Count)
            {
                if (!tokens[i].num) cmd = tokens[i++].cmd;
                bool rel = char.IsLower(cmd);
                Vector2 o = rel ? pos : Vector2.zero;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        pos = new Vector2(Num(), Num()) + o;
                        start = pos;
                        cur = new List<Vector2> { pos };
                        result.Add((cur, false));
                        cmd = rel ? 'l' : 'L';
                        break;
                    case 'L': Line(new Vector2(Num(), Num()) + o); break;
                    case 'H': Line(new Vector2(Num() + (rel ? pos.x : 0), pos.y)); break;
                    case 'V': Line(new Vector2(pos.x, Num() + (rel ? pos.y : 0))); break;
                    case 'C':
                    {
                        var c1 = new Vector2(Num(), Num()) + o; var c2 = new Vector2(Num(), Num()) + o; var e = new Vector2(Num(), Num()) + o;
                        Cubic(pos, c1, c2, e, Line);
                        lastCtrl = c2;
                        break;
                    }
                    case 'S':
                    {
                        var c1 = "CcSs".IndexOf(prev) >= 0 ? pos * 2 - lastCtrl : pos;
                        var c2 = new Vector2(Num(), Num()) + o; var e = new Vector2(Num(), Num()) + o;
                        Cubic(pos, c1, c2, e, Line);
                        lastCtrl = c2;
                        break;
                    }
                    case 'Q':
                    {
                        var c = new Vector2(Num(), Num()) + o; var e = new Vector2(Num(), Num()) + o;
                        Cubic(pos, pos + (c - pos) * (2f / 3), e + (c - e) * (2f / 3), e, Line);
                        lastCtrl = c;
                        break;
                    }
                    case 'T':
                    {
                        var c = "QqTt".IndexOf(prev) >= 0 ? pos * 2 - lastCtrl : pos;
                        var e = new Vector2(Num(), Num()) + o;
                        Cubic(pos, pos + (c - pos) * (2f / 3), e + (c - e) * (2f / 3), e, Line);
                        lastCtrl = c;
                        break;
                    }
                    case 'A':
                    {
                        float rx = Num(), ry = Num(), rot = Num(), large = Num(), sweep = Num();
                        var e = new Vector2(Num(), Num()) + o;
                        Arc(pos, rx, ry, rot, large != 0, sweep != 0, e, Line);
                        break;
                    }
                    case 'Z':
                        if (cur != null) { result[result.Count - 1] = (cur, true); cur.Add(start); }
                        pos = start;
                        cur = null;
                        break;
                    default: i++; break;
                }
                prev = cmd;
                if (char.ToUpperInvariant(cmd) == 'Z' && HasNum()) cmd = 'L';
            }
            return result;
        }

        static void Cubic(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3, Action<Vector2> line)
        {
            for (int k = 1; k <= 12; k += 1)
            {
                float t = k / 12f, u = 1 - t;
                line(u * u * u * p0 + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p3);
            }
        }

        /// <summary>Elliptical arc (endpoint parameterisation, SVG 1.1 F.6.5) flattened to lines.</summary>
        static void Arc(Vector2 p1, float rx, float ry, float phiDeg, bool large, bool sweep, Vector2 p2, Action<Vector2> line)
        {
            if (rx == 0 || ry == 0) { line(p2); return; }
            rx = Mathf.Abs(rx); ry = Mathf.Abs(ry);
            float phi = phiDeg * Mathf.Deg2Rad, cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
            var d = (p1 - p2) / 2;
            float x1 = cos * d.x + sin * d.y, y1 = -sin * d.x + cos * d.y;
            float lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { rx *= Mathf.Sqrt(lambda); ry *= Mathf.Sqrt(lambda); }
            float num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1, den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            float co = Mathf.Sqrt(Mathf.Max(0, num / den)) * (large == sweep ? -1 : 1);
            float cx1 = co * rx * y1 / ry, cy1 = -co * ry * x1 / rx;
            var c = new Vector2(cos * cx1 - sin * cy1, sin * cx1 + cos * cy1) + (p1 + p2) / 2;
            float a1 = Mathf.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx), a2 = Mathf.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
            float da = a2 - a1;
            if (sweep && da < 0) da += Mathf.PI * 2;
            if (!sweep && da > 0) da -= Mathf.PI * 2;
            int n = Mathf.Max(6, (int)(Mathf.Abs(da) / (Mathf.PI / 18)));
            for (int k = 1; k <= n; k += 1)
            {
                float a = a1 + da * k / n;
                float ex = rx * Mathf.Cos(a), ey = ry * Mathf.Sin(a);
                line(new Vector2(cos * ex - sin * ey, sin * ex + cos * ey) + c);
            }
        }

        static List<(bool num, float value, char cmd)> Tokens(string d)
        {
            var t = new List<(bool, float, char)>();
            int i = 0, arg = 0;
            char cmd = ' ';
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsLetter(c) && c != 'e' && c != 'E') { t.Add((false, 0, c)); cmd = c; arg = 0; i++; continue; }
                // an arc's two flags are single digits and may be packed: "a8 8 0 107 11"
                if ((cmd == 'A' || cmd == 'a') && (arg % 7 == 3 || arg % 7 == 4) && (c == '0' || c == '1'))
                {
                    t.Add((true, c - '0', ' '));
                    arg += 1;
                    i++;
                    continue;
                }
                if (char.IsDigit(c) || c == '-' || c == '.' || c == '+')
                {
                    arg += 1;
                    int s = i++;
                    bool dot = c == '.';
                    while (i < d.Length)
                    {
                        char k = d[i];
                        if (char.IsDigit(k)) { i++; continue; }
                        if (k == '.' && !dot) { dot = true; i++; continue; }
                        if ((k == 'e' || k == 'E') && i + 1 < d.Length) { i += (d[i + 1] == '-' || d[i + 1] == '+') ? 2 : 1; continue; }
                        break;
                    }
                    t.Add((true, float.Parse(d.Substring(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture), ' '));
                    continue;
                }
                i++;
            }
            return t;
        }
    }
}
