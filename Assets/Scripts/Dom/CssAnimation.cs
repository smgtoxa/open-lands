// CSS animations (CssInfo.Animations + CssInfo.Keyframes): opacity, transform (rotate / scale / translate),
// margin-top (drawn as a vertical translate) and background colour, with the timing functions the page uses;
// animationend fires when a finite animation ends. box-shadow / filter steps are not drawn.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class CssAnimation
    {
        sealed class Run
        {
            public string value, name, timing = "ease", direction = "normal", fill = "none";
            public float duration = 0, delay = 0, iterations = 1;
            public double start;
            public bool ended;
            public Color baseBackground;
            public readonly HashSet<string> touched = new HashSet<string>();
        }

        static readonly Dictionary<VisualElement, Run> Runs = new Dictionary<VisualElement, Run>();
        static readonly HashSet<VisualElement> Pending = new HashSet<VisualElement>();
        static readonly HashSet<VisualElement> Ran = new HashSet<VisualElement>();

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>A class or the tree changed around the element: its animation is looked at next frame.</summary>
        public static void Touch(VisualElement e) { if (e != null) Pending.Add(e); }

        /// <summary>Did an animation run (or start) on the element? (the page's animationend fallback asks)</summary>
        public static bool HasRun(VisualElement e) => Ran.Contains(e);

        /// <summary>The cascade: the last animation whose selector matches (null: none).</summary>
        static RuleIndex<string> _idx;

        public static void Match(VisualElement e)
        {
            _idx = _idx ?? new RuleIndex<string>(CssInfo.Animations.Select(a => (a.sel, a.value)));
            _idx.Last(e, out var value);
            if (value == "none") value = null;
            Runs.TryGetValue(e, out var run);
            if (value == null)
            {
                if (run != null) { Runs.Remove(e); Clear(e, run); }
                return;
            }
            if (run != null && run.value == value) return;
            if (run != null) Clear(e, run);
            run = Parse(value);
            if (run == null || !CssInfo.Keyframes.ContainsKey(run.name)) return;
            run.start = Now;
            run.baseBackground = e.resolvedStyle.backgroundColor;
            Runs[e] = run;
            Ran.Add(e);
        }

        public static void Tick(VisualElement root)
        {
            if (Pending.Count > 0)
            {
                foreach (var e in Pending.ToList()) if (e.panel != null) Match(e);
                Pending.RemoveWhere(e => e.panel != null);
                if (Pending.Count > 2000) Pending.Clear();
            }
            foreach (var kv in Runs.ToList())
            {
                var e = kv.Key;
                var run = kv.Value;
                if (e.panel == null) { Runs.Remove(e); Ran.Remove(e); continue; }
                if (run.ended) continue;
                double t = Now - run.start - run.delay;
                if (t < 0) continue;
                double total = run.duration * run.iterations;
                bool done = !float.IsInfinity(run.iterations) && t >= total;
                double local = run.duration <= 0 ? 1 : done ? (run.iterations % 1 == 0 ? 1 : (t % run.duration) / run.duration) : (t % run.duration) / run.duration;
                int cycle = run.duration <= 0 ? 0 : (int)Math.Floor(Math.Min(t, total - 1e-6) / run.duration);
                if (run.direction == "alternate" && cycle % 2 == 1) local = 1 - local;
                else if (run.direction == "reverse") local = 1 - local;
                Apply(e, run, (float)local);
                if (done)
                {
                    run.ended = true;
                    if (run.fill != "forwards" && run.fill != "both") Clear(e, run);
                    e.Fire("animationend", new DomEvent { type = "animationend", target = e, currentTarget = e });
                }
            }
        }

        static Run Parse(string value)
        {
            var run = new Run { value = value };
            bool gotDuration = false;
            foreach (var part in SplitTop(value))
            {
                string p = part.Trim();
                if (p.EndsWith("ms") && float.TryParse(p.Substring(0, p.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                { if (!gotDuration) { run.duration = ms / 1000; gotDuration = true; } else run.delay = ms / 1000; }
                else if (p.EndsWith("s") && float.TryParse(p.Substring(0, p.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                { if (!gotDuration) { run.duration = sec; gotDuration = true; } else run.delay = sec; }
                else if (p == "infinite") run.iterations = float.PositiveInfinity;
                else if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) run.iterations = n;
                else if (p == "linear" || p.StartsWith("ease") || p.StartsWith("steps(") || p.StartsWith("cubic-bezier(")) run.timing = p;
                else if (p == "alternate" || p == "reverse" || p == "alternate-reverse" || p == "normal") run.direction = p;
                else if (p == "forwards" || p == "backwards" || p == "both") run.fill = p;
                else run.name = p;
            }
            return run.name == null ? null : run;
        }

        static IEnumerable<string> SplitTop(string v)
        {
            int depth = 0, from = 0;
            for (int i = 0; i <= v.Length; i += 1)
            {
                char c = i < v.Length ? v[i] : ' ';
                if (c == '(') depth += 1;
                else if (c == ')') depth -= 1;
                else if (c == ' ' && depth == 0) { if (i > from) yield return v.Substring(from, i - from); from = i + 1; }
            }
        }

        // ---- timing ----
        static float Ease(string timing, float x)
        {
            switch (timing)
            {
                case "linear": return x;
                case "ease": return Bezier(0.25f, 0.1f, 0.25f, 1f, x);
                case "ease-in": return Bezier(0.42f, 0f, 1f, 1f, x);
                case "ease-out": return Bezier(0f, 0f, 0.58f, 1f, x);
                case "ease-in-out": return Bezier(0.42f, 0f, 0.58f, 1f, x);
            }
            if (timing.StartsWith("steps("))
            {
                var a = timing.Substring(6).TrimEnd(')').Split(',');
                int n = int.Parse(a[0].Trim(), CultureInfo.InvariantCulture);
                bool start = a.Length > 1 && a[1].Trim().StartsWith("start");
                float s = (float)Math.Floor(x * n + (start ? 1 : 0)) / n;
                return Mathf.Clamp01(s);
            }
            if (timing.StartsWith("cubic-bezier("))
            {
                var a = timing.Substring(13).TrimEnd(')').Split(',').Select(t => float.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return Bezier(a[0], a[1], a[2], a[3], x);
            }
            return x;
        }

        static float Bezier(float x1, float y1, float x2, float y2, float x)
        {
            // solve x(t) = x by bisection, then y(t)
            float lo = 0, hi = 1, t = x;
            for (int i = 0; i < 20; i += 1)
            {
                t = (lo + hi) / 2;
                float bx = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
                if (bx < x) lo = t; else hi = t;
            }
            return 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
        }

        // ---- keyframes ----
        static void Apply(VisualElement e, Run run, float progress)
        {
            var frames = CssInfo.Keyframes[run.name];
            foreach (var prop in new[] { "opacity", "margin-top", "transform", "background" })
            {
                // the frames that set the property, with the element's own value at 0% / 100% when they do not
                var steps = new List<(float at, string value)>();
                foreach (var (at, decls) in frames)
                {
                    string v = Decl(decls, prop) ?? (prop == "background" ? Decl(decls, "background-color") : null);
                    if (v != null) steps.Add((at, v));
                }
                if (steps.Count == 0) continue;
                steps.Sort((a, b) => a.at.CompareTo(b.at));
                if (steps[0].at > 0) steps.Insert(0, (0, null));
                if (steps[steps.Count - 1].at < 1) steps.Add((1, null));
                int k = 1;
                while (k < steps.Count - 1 && steps[k].at < progress) k += 1;
                var a = steps[k - 1];
                var b = steps[k];
                float span = b.at - a.at;
                float x = span <= 0 ? 1 : Mathf.Clamp01((progress - a.at) / span);
                // the timing function applies per keyframe interval
                x = Ease(run.timing, x);
                Set(e, run, prop, a.value, b.value, x);
            }
        }

        static string Decl(string decls, string prop)
        {
            foreach (var d in decls.Split(';'))
            {
                int c = d.IndexOf(':');
                if (c > 0 && d.Substring(0, c).Trim() == prop) return d.Substring(c + 1).Trim();
            }
            return null;
        }

        static void Set(VisualElement e, Run run, string prop, string from, string to, float x)
        {
            run.touched.Add(prop);
            switch (prop)
            {
                case "opacity":
                {
                    float a = from == null ? 1 : F(from), b = to == null ? 1 : F(to);
                    e.style.opacity = Mathf.Lerp(a, b, x);
                    break;
                }
                case "margin-top":
                {
                    float a = from == null ? 0 : F(from.Replace("px", "")), b = to == null ? 0 : F(to.Replace("px", ""));
                    e.style.translate = new Translate(0, Mathf.Lerp(a, b, x));
                    break;
                }
                case "background":
                {
                    var a = from == null ? run.baseBackground : Css.Color(from);
                    var b = to == null ? run.baseBackground : Css.Color(to);
                    e.style.backgroundColor = Color.Lerp(a, b, x);
                    break;
                }
                case "transform":
                {
                    var ta = Transform(from);
                    var tb = Transform(to);
                    e.style.rotate = new Rotate(new Angle(Mathf.Lerp(ta.rot, tb.rot, x), AngleUnit.Degree));
                    float s = Mathf.Lerp(ta.scale, tb.scale, x);
                    e.style.scale = new Scale(new Vector3(s, s, 1));
                    break;
                }
            }
        }

        static (float rot, float scale) Transform(string v)
        {
            float rot = 0, scale = 1;
            if (v == null) return (rot, scale);
            var r = System.Text.RegularExpressions.Regex.Match(v, @"rotate\((-?[\d.]+)deg\)");
            if (r.Success) rot = F(r.Groups[1].Value);
            var s = System.Text.RegularExpressions.Regex.Match(v, @"scale\((-?[\d.]+)");
            if (s.Success) scale = F(s.Groups[1].Value);
            return (rot, scale);
        }

        /// <summary>Hands the animated properties back to the stylesheet.</summary>
        static void Clear(VisualElement e, Run run)
        {
            if (run.touched.Contains("opacity")) e.style.opacity = StyleKeyword.Null;
            if (run.touched.Contains("margin-top")) e.style.translate = StyleKeyword.Null;
            if (run.touched.Contains("transform")) { e.style.rotate = StyleKeyword.Null; e.style.scale = StyleKeyword.Null; }
            if (run.touched.Contains("background")) e.style.backgroundColor = StyleKeyword.Null;
            run.touched.Clear();
        }

        static float F(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
