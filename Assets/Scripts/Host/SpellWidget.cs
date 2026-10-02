// src/platform/spell-widget.mjs, ported 1:1 (docs/port/HOST.md). C# 9.
// Spell icons and the power slider: every spell gets its own colour, icon and thumb shape. The
// slider (an SVG) takes clicks and drags; powers the hero cannot afford are greyed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>A THEMES entry: { name, color, glow, thumb, track, icon, stroke }.</summary>
    public sealed class SpellThemeDef
    {
        public string name, color, glow, thumb, track, icon;
        public bool stroke;
    }

    /// <summary>createSpellSlider's result: { root, set(level, max), get level }.</summary>
    public sealed class SpellSlider
    {
        public SvgEl root;
        internal Action<int, int?> setFn;
        internal Func<int> getLevel;
        public void set(int l = 0, int? m = null) => setFn(l, m);
        public int level => getLevel();
    }

    public static class SpellWidget
    {
        static SpellThemeDef T(string name, string color, string glow, string thumb, string track, string icon, bool stroke = false) =>
            new SpellThemeDef { name = name, color = color, glow = glow, thumb = thumb, track = track, icon = icon, stroke = stroke };

        public static readonly Dictionary<int, SpellThemeDef> THEMES = new Dictionary<int, SpellThemeDef>
        {
            [0] = T("Spark", "#7fd7ff", "#e6f7ff", "spark", "zigzag",
                "M13 2L4 14h6l-1 8 9-12h-6z"),
            [1] = T("Heal", "#8be08b", "#e6ffe6", "round", "smooth",
                "M10 3h4v7h7v4h-7v7h-4v-7H3v-4h7z"),
            [2] = T("Freeze", "#9fd6ff", "#ffffff", "hex", "crystal",
                "M12 2v20M4 7l16 10M4 17l16-10M12 2l-3 3M12 2l3 3M12 22l-3-3M12 22l3-3M4 7l4 1M4 7l1 4M20 17l-4-1M20 17l-1-4M4 17l4-1M4 17l1-4M20 7l-4 1M20 7l-1 4", true),
            [3] = T("Fireball", "#ff8a3c", "#ffe08a", "flame", "flame",
                "M12 2c1 4 5 6 5 11a5 5 0 01-10 0c0-2 1-3 2-5 0 2 1 3 2 3 1-2 0-6 1-9z"),
            [4] = T("Hand of Fate", "#d9a4ff", "#f6e8ff", "diamond", "smooth",
                "M7 11V5a1.5 1.5 0 013 0v5m0-6a1.5 1.5 0 013 0v6m0-5a1.5 1.5 0 013 0v6m0-3a1.5 1.5 0 013 0v5a7 7 0 01-7 7h-1a6 6 0 01-5-2.7L3 13a1.6 1.6 0 012.6-1.8L7 13", true),
            [5] = T("Mist of Doom", "#a8e8b0", "#d7ffd9", "skull", "mist",
                "M12 3a7 7 0 00-7 7c0 3 2 4 2 6v2h10v-2c0-2 2-3 2-6a7 7 0 00-7-7zm-3 8a1.5 1.5 0 110 3 1.5 1.5 0 010-3zm6 0a1.5 1.5 0 110 3 1.5 1.5 0 010-3zM9 19h6v2H9z"),
            [6] = T("Lightning", "#ffe36b", "#ffffff", "bolt", "zigzag",
                "M14 2L5 13h6l-2 9 10-13h-6z"),
            [7] = T("Vortex", "#7fb0ff", "#e0eaff", "round", "smooth",
                "M12 4a8 8 0 017 5 5 5 0 00-6 2 3 3 0 01-1 6 8 8 0 01-8-6 5 5 0 006-2 3 3 0 012-5z", true),
            [8] = T("Caustic fog", "#c6e05f", "#f2ffb0", "cloud", "mist",
                "M7 18a4 4 0 010-8 5 5 0 019.6-1A4 4 0 0117 18z", true),
            [9] = T("Caustic fog", "#c6e05f", "#f2ffb0", "cloud", "mist",
                "M7 18a4 4 0 010-8 5 5 0 019.6-1A4 4 0 0117 18z", true),
        };
        static readonly SpellThemeDef DEFAULT = T("Spell", "#cab66e", "#fff", "round", "smooth", "M12 2l1.8 5.2L19 9l-5.2 1.8L12 16l-1.8-5.2L5 9l5.2-1.8z");

        // The spells the port adds are keyed by name, since their index depends on how many the game has.
        public static readonly Dictionary<string, SpellThemeDef> EXTRA_THEMES = new Dictionary<string, SpellThemeDef>
        {
            ["vortex"] = T("Vortex", "#7fb0ff", "#e0eaff", "round", "smooth",
                "M12 4a8 8 0 017 5 5 5 0 00-6 2 3 3 0 01-1 6 8 8 0 01-8-6 5 5 0 006-2 3 3 0 012-5z", true),
            ["drain"] = T("Drain", "#c05bd6", "#f3d9ff", "diamond", "mist",
                "M12 3c3 4 6 6.5 6 10a6 6 0 01-12 0c0-3.5 3-6 6-10zm0 6a3 3 0 00-3 3", true),
            ["thorns"] = T("Wall of Thorns", "#8fc45a", "#e6ffc9", "spark", "zigzag",
                "M4 21c2-5 5-8 8-9M20 21c-2-5-5-8-8-9M12 12V3M9 7l3-4 3 4M6 15l-2-3M18 15l2-3", true),
            ["viper"] = T("Viper", "#8fd66f", "#e6ffd9", "spark", "mist",
                "M4 18c6 0 4-6 10-6s4-6 6-6M20 6l-3 2 3 2", true),
            ["backstab"] = T("Backstab", "#e0b45a", "#fff0c9", "bolt", "smooth",
                "M3 21l6-6M7 17l10-12 3 3-12 10zM14 5l5 5", true),
        };

        /// <summary>spell: a THEMES index (number) or an EXTRA_THEMES key (string).</summary>
        public static SpellThemeDef spellTheme(object spell)
        {
            SpellThemeDef t = null;
            if (spell is string s) EXTRA_THEMES.TryGetValue(s, out t);
            else if (spell is IConvertible c) THEMES.TryGetValue(Convert.ToInt32(c, CultureInfo.InvariantCulture), out t);
            return t ?? DEFAULT;
        }

        /// <summary>A JS number in a template string.</summary>
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

        static SvgNode el(string tag, params (string, string)[] attrs) => SvgEl.Node(tag, attrs);

        // Spell icon as an inline SVG element (24x24 viewbox).
        public static SvgEl spellIcon(object spell, double size = 20)
        {
            var t = spellTheme(spell);
            var svg = (SvgEl)Dom.El("svg");
            svg.SetAttr("viewBox", "0 0 24 24"); svg.SetAttr("width", N(size)); svg.SetAttr("height", N(size)); svg.SetAttr("class", "spell-icon");
            svg.Add(t.stroke
                ? el("path", ("d", t.icon), ("fill", "none"), ("stroke", t.color), ("stroke-width", "2"), ("stroke-linecap", "round"), ("stroke-linejoin", "round"))
                : el("path", ("d", t.icon), ("fill", t.color)));
            return svg;
        }

        const double W = 120; const double H = 26; const double X0 = 7; const double X1 = W - 7;

        static string thumbPath(string kind, double cx, double cy, double r)
        {
            switch (kind)
            {
                case "spark": case "bolt": return $"M{N(cx - r)} {N(cy)} L{N(cx - r / 3)} {N(cy - r / 3)} L{N(cx - r / 4)} {N(cy - r)} L{N(cx + r / 2)} {N(cy - r / 4)} L{N(cx + r)} {N(cy)} L{N(cx + r / 3)} {N(cy + r / 3)} L{N(cx + r / 4)} {N(cy + r)} L{N(cx - r / 2)} {N(cy + r / 4)} Z";
                case "hex": return $"M{N(cx)} {N(cy - r)} L{N(cx + r * .87)} {N(cy - r / 2)} L{N(cx + r * .87)} {N(cy + r / 2)} L{N(cx)} {N(cy + r)} L{N(cx - r * .87)} {N(cy + r / 2)} L{N(cx - r * .87)} {N(cy - r / 2)} Z";
                case "flame": return $"M{N(cx)} {N(cy - r * 1.2)} C{N(cx + r)} {N(cy - r / 3)} {N(cx + r)} {N(cy + r / 4)} {N(cx)} {N(cy + r)} C{N(cx - r)} {N(cy + r / 4)} {N(cx - r)} {N(cy - r / 3)} {N(cx)} {N(cy - r * 1.2)} Z";
                case "diamond": return $"M{N(cx)} {N(cy - r)} L{N(cx + r)} {N(cy)} L{N(cx)} {N(cy + r)} L{N(cx - r)} {N(cy)} Z";
                case "skull": return $"M{N(cx)} {N(cy - r)} a{N(r)} {N(r)} 0 0 1 {N(r)} {N(r)} v{N(r * .6)} h{N(-r * 2)} v{N(-r * .6)} a{N(r)} {N(r)} 0 0 1 {N(r)} {N(-r)} Z";
                case "cloud": return $"M{N(cx - r)} {N(cy + r / 2)} a{N(r * .7)} {N(r * .7)} 0 0 1 {N(r * .3)} {N(-r * 1.2)} a{N(r * .8)} {N(r * .8)} 0 0 1 {N(r * 1.4)} {N(0)} a{N(r * .7)} {N(r * .7)} 0 0 1 {N(r * .3)} {N(r * 1.2)} Z";
                default: return $"M{N(cx)} {N(cy)} m{N(-r)} 0 a{N(r)} {N(r)} 0 1 0 {N(r * 2)} 0 a{N(r)} {N(r)} 0 1 0 {N(-r * 2)} 0";
            }
        }

        static string trackPath(string kind, double y)
        {
            var pts = new List<string>();
            const int n = 24;
            for (int i = 0; i <= n; i += 1)
            {
                double x = X0 + ((X1 - X0) * i) / n;
                double dy = 0;
                if (kind == "zigzag") dy = i % 2 != 0 ? -3 : 3;
                else if (kind == "flame") dy = Math.Sin(i * 1.3) * 2.5 - 1;
                else if (kind == "mist") dy = Math.Sin(i * 0.8) * 3;
                else if (kind == "crystal") dy = i % 4 == 2 ? -3 : i % 4 == 0 ? 2 : 0;
                pts.Add($"{(i != 0 ? "L" : "M")}{N(x)} {N(y + dy)}");
            }
            return string.Join(" ", pts);
        }

        /// <summary>path.getTotalLength(): the length of the flattened path.</summary>
        static double getTotalLength(string d)
        {
            double total = 0;
            foreach (var (pts, _) in SvgEl.PathData(d))
                for (int i = 1; i < pts.Count; i += 1) total += Vector2.Distance(pts[i - 1], pts[i]);
            return total;
        }

        /// <summary>The first `length` units of a polyline path: what stroke-dasharray "length gap" draws.</summary>
        static string dashPrefix(string d, double length)
        {
            var parts = new List<string>();
            double left = length;
            foreach (var (pts, _) in SvgEl.PathData(d))
            {
                if (pts.Count == 0 || left <= 0) break;
                parts.Add($"M{N(pts[0].x)} {N(pts[0].y)}");
                for (int i = 1; i < pts.Count && left > 0; i += 1)
                {
                    double seg = Vector2.Distance(pts[i - 1], pts[i]);
                    var to = seg <= left ? pts[i] : Vector2.Lerp(pts[i - 1], pts[i], (float)(left / seg));
                    parts.Add($"L{N(to.x)} {N(to.y)}");
                    left -= seg;
                }
            }
            return string.Join(" ", parts);
        }

        // createSpellSlider({ spell, level, max, onChange }) -> { root, set(level, max) }
        // `locked`: the power is fixed (the Green Skull is always thrown at full strength). The slider is
        // still drawn, so the row reads like every other spell, but it takes no input.
        public static SpellSlider createSpellSlider(object spell, int level = 0, int max = 3, Action<int> onChange = null, string title = null, bool locked = false)
        {
            var t = spellTheme(spell);
            var svg = (SvgEl)Dom.El("svg");
            svg.SetAttr("viewBox", $"0 0 {N(W)} {N(H)}"); svg.SetAttr("class", "spell-slider"); svg.SetAttr("role", "slider");
            svg.SetAttr("aria-valuemin", "1"); svg.SetAttr("aria-valuemax", "4"); svg.SetAttr("tabindex", "0");
            if (title != null) { var tt = el("title"); tt.SetText(title); svg.Add(tt); svg.SetTitle(title); } // the browser shows an svg <title> as its tooltip
            const double cy = H / 2;
            var @base = el("path", ("d", trackPath(t.track, cy)), ("fill", "none"), ("stroke", "#2a2a24"), ("stroke-width", "5"), ("stroke-linecap", "round"));
            var fill = el("path", ("d", trackPath(t.track, cy)), ("fill", "none"), ("stroke", t.color), ("stroke-width", "3"), ("stroke-linecap", "round"));
            svg.Add(@base); svg.Add(fill);
            var stops = new List<(SvgNode dot, SvgNode label)>();
            for (int i = 0; i < 4; i += 1)
            {
                double x = X0 + ((X1 - X0) * i) / 3;
                var dot = el("circle", ("cx", N(x)), ("cy", N(cy)), ("r", "3.2"), ("fill", "#0b0d0e"), ("stroke", t.color), ("stroke-width", "1.2"));
                var label = el("text", ("x", N(x)), ("y", N(H - 1)), ("text-anchor", "middle"), ("font-size", "7"), ("fill", "#8a8375"));
                label.SetText((i + 1).ToString(CultureInfo.InvariantCulture));
                svg.Add(dot); svg.Add(label);
                stops.Add((dot, label));
            }
            var glow = el("path", ("fill", t.glow), ("opacity", "0.35"));
            var thumb = el("path", ("fill", t.color), ("stroke", "#000"), ("stroke-width", "0.8"));
            svg.Add(glow); svg.Add(thumb);
            int stateLevel = level, stateMax = max;
            double xOf(int l) => X0 + ((X1 - X0) * l) / 3;
            string track = trackPath(t.track, cy);
            void render()
            {
                double x = xOf(stateLevel);
                thumb.SetAttr("d", thumbPath(t.thumb, x, cy, 6));
                glow.SetAttr("d", thumbPath(t.thumb, x, cy, 9));
                // fill up to the thumb along the actual (zigzag/wavy) track length
                double total = getTotalLength(track);
                double dash = stateLevel == 3 ? total + 10 : (total * (x - X0)) / (X1 - X0);
                fill.SetAttr("stroke-dasharray", $"{N(dash)} {N(total + 400)}");
                // the SVG renderer has no dashes: draw the dash (the track's first `dash` units) as the path itself
                fill.SetAttr("d", dashPrefix(track, dash));
                for (int i = 0; i < stops.Count; i += 1)
                {
                    var s = stops[i];
                    bool ok = i <= stateMax;
                    s.dot.SetAttr("opacity", ok ? "1" : "0.25");
                    s.label.SetAttr("fill", i == stateLevel ? t.color : ok ? "#b9ad8f" : "#4a463c");
                }
                svg.SetAttr("aria-valuenow", (stateLevel + 1).ToString(CultureInfo.InvariantCulture));
            }
            int pick(DomEvent @event)
            {
                var rect = svg.worldBound;
                double x = ((@event.clientX - rect.xMin) / rect.width) * W;
                int l = (int)Math.Max(0, Math.Min(3, Math.Floor(((x - X0) / (X1 - X0)) * 3 + 0.5)));
                return Math.Min(l, Math.Max(0, stateMax));
            }
            bool dragging = false;
            void apply(int l, bool done) { if (l != stateLevel) { stateLevel = l; render(); } if (done && onChange != null) onChange(stateLevel); }
            if (locked)
            {
                svg.AddToClassList("locked");
                svg.SetAttr("aria-readonly", "true");
                svg.RemoveAttr("tabindex");
                render();
                return new SpellSlider { root = svg, setFn = (l, m) => { }, getLevel = () => stateLevel };
            }
            // setPointerCapture(event.pointerId): UI Toolkit's capture of the mouse pointer (released on up/cancel, as the browser does)
            svg.On("pointerdown", (DomEvent @event) => { @event.preventDefault(); dragging = true; svg.CapturePointer(PointerId.mousePointerId); apply(pick(@event), false); });
            svg.On("pointermove", (DomEvent @event) => { if (dragging) apply(pick(@event), false); });
            svg.On("pointerup", (DomEvent @event) => { svg.ReleasePointer(PointerId.mousePointerId); if (!dragging) return; dragging = false; apply(pick(@event), true); });
            svg.On("pointercancel", () => { svg.ReleasePointer(PointerId.mousePointerId); dragging = false; });
            svg.On("keydown", (DomEvent @event) =>
            {
                if (@event.key == "ArrowLeft" || @event.key == "ArrowRight") { @event.preventDefault(); @event.stopPropagation(); apply(Math.Max(0, Math.Min(Math.Max(0, stateMax), stateLevel + (@event.key == "ArrowRight" ? 1 : -1))), true); }
            });
            render();
            return new SpellSlider
            {
                root = svg,
                setFn = (l, m) => { if (m != null) stateMax = m.Value; stateLevel = Math.Max(0, Math.Min(3, l)); render(); },
                getLevel = () => stateLevel,
            };
        }
    }
}
