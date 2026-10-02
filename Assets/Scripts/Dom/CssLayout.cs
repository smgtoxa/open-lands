// The CSS that USS cannot express, applied at run time from CssInfo (generated from styles.css by
// tools/gen_uss.mjs): flex/grid gap, grid tracks / spans / rows / display: contents (grids are wrapped flex
// rows; each child gets its track's width, gaps become margins on top of the child's own), inline flow,
// line-height, letter-spacing widths, word-boundary wrapping, aspect-ratio, min()/max()/calc() sizes, fixed
// positioning, pointer-events, scrolling, ::before / ::after, gradient and url() backgrounds, OS fonts.
// Rules arrive in cascade order (the generator sorts them by CSS specificity).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class CssLayout
    {
        public sealed class Rule
        {
            public readonly string sel, display, dir, wrap, columns, gridColumn, gridRow, width, height, rows, level, textAlign, key;
            public readonly bool minWidth;
            public readonly float colGap, rowGap, aspect;
            public readonly int order;

            public Rule(string sel, string display, string dir, string wrap, float colGap, float rowGap, string columns, string gridColumn,
                string gridRow, float aspect, string width, string height, string rows, string level, string textAlign, bool minWidth, int order)
            {
                this.sel = sel; this.display = display; this.dir = dir; this.wrap = wrap;
                this.colGap = colGap; this.rowGap = rowGap; this.columns = columns; this.gridColumn = gridColumn;
                this.gridRow = gridRow; this.aspect = aspect; this.width = width; this.height = height; this.rows = rows; this.level = level; this.textAlign = textAlign; this.minWidth = minWidth; this.order = order;
                key = KeyOf(sel);
            }
        }

        /// <summary>The rightmost compound's first class (or id): only elements carrying it are tested.</summary>
        internal static string KeyOf(string sel)
        {
            var last = sel.Replace(">", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Last();
            int dot = last.IndexOf('.'), hash = last.IndexOf('#');
            return dot >= 0 ? "." + last.Substring(dot + 1).Split('.', ':', '[')[0] : hash >= 0 ? "#" + last.Substring(hash + 1).Split('.', ':', '[')[0] : "";
        }

        sealed class State
        {
            public string display, dir, wrap, columns, gridColumn, gridRow, width, height, rows, level, textAlign;
            public bool minWidth;
            public int order;
            public float colGap = -1, rowGap = -1, aspect = -1;
            public bool Lays => display == "grid" || (display == "flex" && (colGap > 0 || rowGap > 0));
        }

        static Dictionary<string, List<(int order, Rule rule)>> _byKey;
        static readonly Dictionary<VisualElement, State> _states = new Dictionary<VisualElement, State>();
        static readonly HashSet<VisualElement> _laidOut = new HashSet<VisualElement>();
        static readonly Dictionary<VisualElement, float[]> _baseMargins = new Dictionary<VisualElement, float[]>();
        static readonly Dictionary<VisualElement, float[]> _gapAdd = new Dictionary<VisualElement, float[]>();
        /// <summary>line-height: half the extra leading per text element (negative when tighter than the font's).</summary>
        static readonly Dictionary<VisualElement, float> _leading = new Dictionary<VisualElement, float>();
        static readonly Dictionary<VisualElement, float[]> _basePadding = new Dictionary<VisualElement, float[]>();
        static readonly Dictionary<VisualElement, float> _spacing = new Dictionary<VisualElement, float>();
        static Dictionary<string, List<(float value, bool px, string sel)>> _lhByKey;
        static int _frame;

        /// <summary>Once a frame: re-matches the rules every few frames, lays out the containers every frame.</summary>
        /// <summary>After a click or a key: the page's handler may have built new elements (a list redrawn, a window
        /// filled); they get their emulated styles now, before the frame is drawn, instead of on the next Tick - they
        /// showed for a frame unstyled (the character screen flashed on every spell clicked).</summary>
        public static void Flush(VisualElement root)
        {
            if (root == null || _byKey == null || _dirty.Count == 0) return;
            Dom.ApplyUppercasePending();
            bool addedTable = _dirty.Any(e => e.panel != null && (e.ClassListContains("tag-table") || e.Q(className: "tag-table") != null));
            VisitDirty();
            if (addedTable) { Tables(root); MinWidths(root); }
            ApplyCalcs(root);
            foreach (var kv in _states)
            {
                if (kv.Key.panel == null || !Displayed(kv.Key)) continue;
                Aspect(kv.Key, kv.Value);
                if (kv.Value.Lays) { Apply(kv.Key, kv.Value); _laidOut.Add(kv.Key); }
            }
            foreach (var e in _scrollWanted.Keys.ToList()) ApplyScroll(e);
        }

        public static void Tick(VisualElement root)
        {
            if (root == null) return;
            // text-transform: uppercase for text set while detached (SetText cannot see the ancestors then):
            // every frame and before layout, or a rebuilt label shows lower case, then flips to upper case and
            // rewraps up to half a second later - every modal that redraws its content shook with it
            Dom.ApplyUppercasePending();
            Media(root);
            Perf.Mark("css.media");
            // the periodic passes, each on its own frame of ten (one frame carrying them all would stutter)
            // (the first frames run them all: the page is styled from the first frame shown)
            bool start = _frame < 10;
            if (start) { Scan(root); ScanContinue(root, double.MaxValue); }
            else
            {
                // a safety net now (what changes is styled as it changes): every 30th frame, in 1.5 ms slices - every
                // 10th frame in 3 ms slices made a 15-20 ms frame several times a second and missed the 60 Hz refresh
                if (!ScanBusy && _frame % 30 == 0) Scan(root);
                if (ScanBusy) ScanContinue(root, 1.5);
            }
            Perf.Mark("css.scan");
            if (start || _frame % 30 == 14) { Fonts(root); Perf.Mark("css.fonts"); }
            if (start || _frame % 30 == 22) { Tables(root); MinWidths(root); Perf.Mark("css.tables"); }
            // something was added this frame: tables and min-widths too, not on the next tenth frame (a rebuilt
            // table - the bestiary's details - showed a frame of unaligned cells)
            // (only when a table came with it: the whole-page pass on every addition made loading the log crawl)
            bool addedTable = _dirty.Any(e => e.panel != null && (e.ClassListContains("tag-table") || e.Q(className: "tag-table") != null));
            // the frame after something new was styled: the whole page's passes in one go (calc sizes, collected
            // boxes, tables, min-widths), not spread over the next ten-odd frames where they moved it a few px late
            if (Revisit() && !start)
            {
                if (!ScanBusy) Scan(root);
                ScanContinue(root, double.MaxValue);
                Tables(root); MinWidths(root);
                Perf.Mark("css.settle");
            }
            VisitDirty();
            // a table that came this frame is measured now and once more on the next frame (its cells have their
            // size only after a layout pass; left to the periodic pass the columns snapped half a second later)
            if (_tablesNext && !start) { Tables(root); MinWidths(root); }
            _tablesNext = addedTable;
            if (addedTable && !start && _frame % 30 != 22) { Tables(root); MinWidths(root); }
            Perf.Mark("css.dirty");
            ApplyCalcs(root);
            Perf.Mark("css.calcs");
            CssAnimation.Tick(root);
            Perf.Mark("css.anim");
            CssShadow.Tick();
            Perf.Mark("css.shadow");
            // (upper case after an ancestor's class change: the scan re-shows the text of every element it finds
            // changed - see Visit - instead of a whole-page pass every two seconds)
            _frame += 1;
            foreach (var kv in _states)
            {
                if (kv.Key.panel == null || !Displayed(kv.Key)) continue;
                Aspect(kv.Key, kv.Value);
                if (kv.Value.Lays) { Apply(kv.Key, kv.Value); _laidOut.Add(kv.Key); }
                else if (_laidOut.Remove(kv.Key)) Reset(kv.Key);
            }
            Perf.Mark("css.containers");
            foreach (var e in _scrollWanted.Keys.ToList()) ApplyScroll(e);
            ScrollBars(root);
            if (_frame % 30 == 7) Order(root);
            foreach (var kv in _gradient) PaintGradient(kv.Key, kv.Value);
            Perf.Mark("css.tail");
        }

        static void Scan(VisualElement root)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, List<(int, Rule)>>();
                var all = CssInfo.Layouts.Concat(HostLayouts).ToArray();
                for (int i = 0; i < all.Length; i += 1)
                {
                    var r = all[i];
                    if (!_byKey.TryGetValue(r.key, out var list)) _byKey[r.key] = list = new List<(int, Rule)>();
                    list.Add((i, r));
                }
            }
            foreach (var gone in _states.Keys.Where(e => e.panel == null).ToList()) { _states.Remove(gone); _laidOut.Remove(gone); _geometryHooked.Remove(gone); _firstDone.Remove(gone); }
            if (_lhByKey == null)
            {
                _lhByKey = new Dictionary<string, List<(float, bool, string)>>();
                foreach (var (sel, value, px) in CssInfo.LineHeights)
                {
                    string k = sel == ".root" ? ".root" : KeyOf(sel);
                    if (!_lhByKey.TryGetValue(k, out var list)) _lhByKey[k] = list = new List<(float, bool, string)>();
                    list.Add((value, px, sel));
                }
            }
            // a thirtieth of the elements is matched again on each scan (what the fingerprint cannot see - a sibling's
            // class for "+" / "~" selectors - is picked up within a few seconds), and the elements gone are dropped;
            // clearing everything at once made a 30-40 ms frame every five seconds
            int phase = (_frame / 30) % 30;
            _refresh.Clear();
            foreach (var e in _sig.Keys) if (e.panel == null || (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e) & 0x7fffffff) % 30 == phase) _refresh.Add(e);
            foreach (var e in _refresh) { _sig.Remove(e); _collected.Remove(e); _fontSig.Remove(e); }
            if (phase == 0) _gridSized.RemoveWhere(e => e.panel == null);
            Perf.Mark("scan.prep");
            foreach (var gone in _baseMargins.Keys.Where(e => e.panel == null).ToList()) { _baseMargins.Remove(gone); _gapAdd.Remove(gone); }
            if (_frame % 300 == 0) _clips.Clear();   // classes change: looked up again now and then
            foreach (var gone in _collapsed.Keys.Where(e => e.panel == null).ToList()) { _collapsed.Remove(gone); _escTop.Remove(gone); _escBottom.Remove(gone); }
            foreach (var gone in _leading.Keys.Where(e => e.panel == null).ToList()) { _leading.Remove(gone); _basePadding.Remove(gone); _spacing.Remove(gone); _wordMin.Remove(gone); }
            // one walk for everything matched per element; what the walk finds for the scrollables, the fixed
            // boxes and the calc() sizes is acted on after it
            _collect = true;
            _found.Clear(); _fixedFound.Clear(); _scrollFound.Clear();
            _scrollFound.Add(root);
            _walk.Clear(); _flow.Clear();
            _walk.Push((root, 1.35f, false, 0, -1));
            Perf.Mark("scan.gone");
        }

        // The walk and then the inline-flow pass go on across frames, a few milliseconds a frame: done whole in one
        // frame they came to 10-20 ms together, and a frame of 20 ms is under 60 FPS.
        static readonly Stack<(VisualElement e, float lh, bool px, int psig, int index)> _walk = new Stack<(VisualElement, float, bool, int, int)>();
        static readonly Stack<(VisualElement e, string align)> _flow = new Stack<(VisualElement, string)>();
        static bool ScanBusy => _walk.Count > 0 || _flow.Count > 0;
        static readonly List<VisualElement> _kids = new List<VisualElement>();

        static void ScanContinue(VisualElement root, double budgetMs)
        {
            long until = budgetMs >= double.MaxValue ? long.MaxValue : System.Diagnostics.Stopwatch.GetTimestamp() + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000);
            if (_walk.Count > 0)
            {
                try
                {
                    while (_walk.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() < until)
                    {
                        var (e, lh, px, psig, index) = _walk.Pop();
                        if (e.panel == null) continue;
                        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        var (clh, cpx, sig) = VisitOne(e, lh, px, psig, false, index);
                        double took = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        if (took > 4 && SlowLog) UnityEngine.Debug.Log($"SLOWVISIT {took:0.0}ms {e.GetType().Name} [{string.Join(" ", e.GetClasses())}] kids={e.childCount}");
                        _kids.Clear(); _kids.AddRange(e.Children());
                        for (int i = _kids.Count - 1; i >= 0; i -= 1) _walk.Push((_kids[i], clh, cpx, sig, i));
                    }
                }
                catch (Exception) { _walk.Clear(); throw; }
                Perf.Mark("scan.visit");
                if (_walk.Count > 0) return;
                _collect = false;
                foreach (var e in _fixedFound) if (e.panel != null && e.parent != root) root.Add(e);
                Scrollables(_scrollFound);
                CalcTargets(_found);
                Perf.Mark("scan.found");
                _flow.Push((root, "left"));
            }
            while (_flow.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() < until)
            {
                var (e, align) = _flow.Pop();
                if (e.panel == null) continue;
                InlineFlow(e, align, false);
                string inner = _alignOf.TryGetValue(e, out var a) ? a : align;
                _kids.Clear(); _kids.AddRange(e.Children());
                for (int i = _kids.Count - 1; i >= 0; i -= 1) if (!_kids[i].ClassListContains("inline-run")) _flow.Push((_kids[i], inner));
            }
            Perf.Mark("scan.inline");
        }

        static readonly List<(int order, Rule rule)> _candidates = new List<(int, Rule)>();
        static readonly Dictionary<VisualElement, (float lh, bool px)> _lhOf = new Dictionary<VisualElement, (float, bool)>();
        static readonly HashSet<VisualElement> _dirty = new HashSet<VisualElement>();

        /// <summary>An element was added or its classes changed: it (and its subtree) is styled next frame, not
        /// at the next full scan.</summary>
        public static void Touch(VisualElement e)
        {
            if (e == null) return;
            _dirty.Add(e);
            CssAnimation.Touch(e);
        }

        // Elements styled by VisitDirty are styled again, all of them, on the next frame: their line spacing is worked
        // out from the font size, which the panel resolves for a new element only after it was first styled. Left to
        // the periodic scan, the text settled 2-3 px up to half a second later (a window opening jumped).
        static HashSet<VisualElement> _revisitNext = new HashSet<VisualElement>(), _revisitNow = new HashSet<VisualElement>();

        static void VisitAll(VisualElement e, float lh, bool lhPx, int parentSig, int index = -1)
        {
            var (clh, cpx, sig) = VisitOne(e, lh, lhPx, parentSig, true, index);
            int k = 0;
            foreach (var c in e.Children()) VisitAll(c, clh, cpx, sig, k++);
        }

        // the steady trickle of new elements while playing (a line in the log, a toast, a damage number) styles itself
        // and needs no whole-page pass: that pass cost up to 40 ms and the frame rate dropped below 60
        static readonly bool SlowLog = Environment.GetCommandLineArgs().Contains("--perf");

        static bool _tablesNext;

        static bool Shown(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
            {
                var d = p.style.display.keyword == StyleKeyword.Undefined ? p.style.display.value : p.resolvedStyle.display;
                if (d == DisplayStyle.None || p.ClassListContains("is-hidden")) return false;
            }
            return true;
        }

        static bool Trickle(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.ClassListContains("ui-log") || p.ClassListContains("fx-layer") || p.ClassListContains("journal-list") || p.ClassListContains("ui-party") || p.ClassListContains("spellbar") || p.ClassListContains("ui-hotbar")) return true;
            return false;
        }

        static bool Revisit()
        {
            // (not what is inside a hidden window, nor the text runs the inline flow makes itself: the pass makes
            // those, and they set it off again on the next frame - it ran twenty times a second)
            var trigger = _revisitNow.FirstOrDefault(e => e.panel != null && !(e is Label) && !e.ClassListContains("inline-run") && Shown(e) && !Trickle(e));
            bool any = trigger != null;
            if (any && SlowLog) { var path = new List<string>(); for (var q = trigger; q != null && path.Count < 4; q = q.parent) path.Add(q.GetType().Name + "." + string.Join(".", q.GetClasses().Take(2))); UnityEngine.Debug.Log("SETTLE by " + string.Join(" < ", path)); }
            if (_revisitNow.Count > 0 && _lhByKey != null)
                foreach (var e in _revisitNow)
                {
                    if (e.panel == null) continue;
                    var inherited = e.parent != null && _lhOf.TryGetValue(e.parent, out var p) ? p : (1.35f, false);
                    VisitAll(e, inherited.Item1, inherited.Item2, e.parent != null && _sig.TryGetValue(e.parent, out var ps) ? ps : 0);
                }
            var t = _revisitNow; _revisitNow = _revisitNext; _revisitNext = t; _revisitNext.Clear();
            return any;
        }

        static void VisitDirty()
        {
            if (_dirty.Count == 0 || _byKey == null || _lhByKey == null) return;
            var list = _dirty.Where(e => e.panel != null).ToList();
            _dirty.RemoveWhere(e => e.panel != null);
            if (_dirty.Count > 5000) _dirty.Clear();
            // each new subtree styled and flowed; each parent's own flow once (not its whole subtree once per
            // child added: a slice of 60 log entries went over the whole log 60 times)
            var hosts = new HashSet<VisualElement>();
            foreach (var e in list)
            {
                if (e.panel == null) continue;
                var inherited = e.parent != null && _lhOf.TryGetValue(e.parent, out var p) ? p : (1.35f, false);
                Visit(e, inherited.Item1, inherited.Item2);
                if (_revisitNext.Count < 2000) _revisitNext.Add(e);
                // its fonts now too, not on the next tenth frame: a heading shown in the plain font for a few frames
                // and then in the page's was a different width (the bestiary's "ORC" broke into "OR / C" and back)
                if (_fontIdx != null) FontsOf(e);
                if (e.parent != null) hosts.Add(e.parent);
            }
            foreach (var host in hosts) if (host.panel != null) InlineFlow(host, host.parent != null && _alignOf.TryGetValue(host.parent, out var al) ? al : "left", false);
            foreach (var e in list) if (e.panel != null && !hosts.Contains(e)) InlineFlow(e, e.parent != null && _alignOf.TryGetValue(e.parent, out var al) ? al : "left");
        }

        static bool _collect;
        static readonly Dictionary<(VisualElement, string), string> _found = new Dictionary<(VisualElement, string), string>();
        static readonly List<VisualElement> _fixedFound = new List<VisualElement>(), _scrollFound = new List<VisualElement>();
        static RuleIndex<bool> _fixedIdx, _scrollIdx;
        static Dictionary<string, RuleIndex<string>> _calcIdx;

        // the full scan's per-element lookups: position: fixed, overflow: auto, calc() sizes (last rule wins;
        // a plain size, "", hands the property back to the stylesheet)
        sealed class Collected { public bool fix, scroll; public List<(string prop, string value)> calcs; }
        static readonly Dictionary<VisualElement, Collected> _collected = new Dictionary<VisualElement, Collected>();

        static void Collect(VisualElement e, bool same)
        {
            _fixedIdx = _fixedIdx ?? new RuleIndex<bool>(CssInfo.Fixed.Select(x => (x, true)));
            _scrollIdx = _scrollIdx ?? new RuleIndex<bool>(CssInfo.Scrollable.Concat(HostScrollable).Select(x => (x, true)));
            _calcIdx = _calcIdx ?? CssInfo.Calc.Concat(HostCalcs).GroupBy(c => c.prop).ToDictionary(g => g.Key, g => new RuleIndex<string>(g.Select(c => (c.sel, c.value))));
            if (!same || !_collected.TryGetValue(e, out var got))
            {
                got = new Collected { fix = _fixedIdx.Any(e), scroll = _scrollIdx.Any(e) };
                foreach (var kv in _calcIdx) if (kv.Value.Last(e, out var v) && v.Length > 0) (got.calcs = got.calcs ?? new List<(string, string)>()).Add((kv.Key, v));
                _collected[e] = got;
            }
            if (got.fix) _fixedFound.Add(e);
            if (got.scroll) _scrollFound.Add(e);
            if (got.calcs != null) foreach (var (prop, v) in got.calcs) _found[(e, prop)] = v;
        }

        // What the rules can see of an element, folded with its parent's: an element whose fingerprint has not
        // changed since the last scan matches the same rules, so the scan reuses what it found then. (The scan
        // matched every rule against every element every tenth frame: ~30 ms, a stutter under 60 FPS.)
        static readonly Dictionary<VisualElement, int> _sig = new Dictionary<VisualElement, int>();
        static readonly List<VisualElement> _refresh = new List<VisualElement>();

        static int Signature(VisualElement e, int parentSig, int index = -1)
        {
            unchecked
            {
                int h = parentSig * 31 + 17;
                foreach (var c in e.GetClasses()) h = h * 31 + c.GetHashCode();
                if (e.userData is DomData d)
                {
                    h = h * 31 + (d.tag?.GetHashCode() ?? 0);
                    h = h * 31 + (d.id?.GetHashCode() ?? 0);
                    foreach (var kv in d.attributes) h = h * 31 + kv.Key.GetHashCode() * 7 + (kv.Value?.GetHashCode() ?? 0);
                    foreach (var kv in d.dataset) h = h * 31 + kv.Key.GetHashCode() * 11 + (kv.Value?.GetHashCode() ?? 0);
                    h = h * 31 + (d.hidden ? 1 : 0) + (d.isChecked ? 2 : 0);
                }
                // the place among its siblings (for :first-child and the like); the walk passes it down - asking the
                // parent for it searched the siblings, over and over for a long list
                var parent = e.parent;
                if (parent != null) { h = h * 31 + (index >= 0 ? index : parent.IndexOf(e)); h = h * 31 + parent.childCount; }
                return h;
            }
        }

        static void Visit(VisualElement e, float lh, bool lhPx) => Visit(e, lh, lhPx, e.parent != null && _sig.TryGetValue(e.parent, out var ps) ? ps : 0, true);

        static void Visit(VisualElement e, float lh, bool lhPx, int parentSig, bool force, int index = -1)
        {
            var (clh, cpx, sig) = VisitOne(e, lh, lhPx, parentSig, force, index);
            int k = 0;
            foreach (var c in e.Children()) Visit(c, clh, cpx, sig, false, k++);
        }

        /// <summary>One element of the walk: its styles; returns what its children inherit.</summary>
        static (float lh, bool px, int sig) VisitOne(VisualElement e, float lh, bool lhPx, int parentSig, bool force, int index)
        {
            int sig = Signature(e, parentSig, index);
            bool same = !force && _sig.TryGetValue(e, out var was) && was == sig;
            if (!same && e.ClassListContains("tag-table")) _tables.Add(e);
            _sig[e] = sig;
            if (_collect) Collect(e, same);
            if (same && _lhOf.TryGetValue(e, out var known)) { lh = known.lh; lhPx = known.px; }
            else
                foreach (var c in e.GetClasses())
                    if (_lhByKey.TryGetValue("." + c, out var lhs))
                        foreach (var (value, px, sel) in lhs)
                            if (Dom.MatchesSelector(e, sel)) { lh = value; lhPx = px; }
            if (e is TextElement te && !(e.parent is TextField) && !(e.parent?.parent is TextField)) { Leading(te, lh, lhPx); if (!te.ClassListContains("inline-run")) LongestWord(te); }
            if (!same && !force && e is TextElement ut && ut.text.Length > 0 && !ut.ClassListContains("inline-run") && !(ut.parent is TextField) && !(ut.parent?.parent is TextField)) Dom.ShowText(ut, Dom.RawText(ut));
            if (same)
            {
                _lhOf[e] = (lh, lhPx);
                return (lh, lhPx, sig);
            }
            Pseudo(e);
            PointerEvents(e);
            Background(e);
            CssAnimation.Match(e);
            CssShadow.Match(e);
            _candidates.Clear();
            foreach (var c in e.GetClasses()) if (_byKey.TryGetValue("." + c, out var l)) _candidates.AddRange(l);
            if (e.userData is DomData d && d.id != null && _byKey.TryGetValue("#" + d.id, out var byId)) _candidates.AddRange(byId);
            if (_byKey.TryGetValue("", out var universal)) _candidates.AddRange(universal);   // "> *" and friends
            State st = null;
            // only the matching rules need the cascade order
            _candidates.RemoveAll(x => !Dom.MatchesSelector(e, x.rule.sel));
            if (_candidates.Count > 1) _candidates.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var (_, r) in _candidates)
            {
                st = st ?? new State();
                if (r.display != null) st.display = r.display;
                if (r.dir != null) st.dir = r.dir;
                if (r.wrap != null) st.wrap = r.wrap;
                if (r.colGap >= 0) st.colGap = r.colGap;
                if (r.rowGap >= 0) st.rowGap = r.rowGap;
                if (r.columns != null) st.columns = r.columns;
                if (r.gridColumn != null) st.gridColumn = r.gridColumn;
                if (r.gridRow != null) st.gridRow = r.gridRow;
                if (r.aspect >= 0) st.aspect = r.aspect;
                if (r.width != null) st.width = r.width;
                if (r.height != null) st.height = r.height;
                if (r.rows != null) st.rows = r.rows;
                if (r.level != null) st.level = r.level;
                if (r.textAlign != null) st.textAlign = r.textAlign;
                if (r.minWidth) st.minWidth = true;
                if (r.order != int.MinValue) st.order = r.order;
            }
            if (st != null)
            {
                _states[e] = st;
                // laid out as soon as UI Toolkit has sized the box (in the same frame: a re-rendered card never
                // shows unstyled), and again on each later size change
                if (_geometryHooked.Add(e)) e.RegisterCallback<GeometryChangedEvent>(_ => OnGeometry(e));
            }
            else _states.Remove(e);
            _lhOf[e] = (lh, lhPx);
            return (lh, lhPx, sig);
        }

        static readonly Dictionary<string, FontDefinition> _fonts = new Dictionary<string, FontDefinition>();

        /// <summary>font-family (CssInfo.Fonts): OS fonts, which USS cannot name.</summary>
        static RuleIndex<string[]> _fontIdx;
        static RuleIndex<bool> _sizeIdx;

        static void Fonts(VisualElement root)
        {
            _fontIdx = _fontIdx ?? new RuleIndex<string[]>(CssInfo.Fonts.Concat(HostFonts).Select(f => (f.sel, f.families)));
            _sizeIdx = _sizeIdx ?? new RuleIndex<bool>(CssInfo.FontSizes.Select(f => (f.sel, f.inherit)));
            _inheritSize.RemoveWhere(e => e.panel == null);
            FontsOf(root);
        }

        // the last matching rule wins; "inherit" leaves the element to its parent's font; font-size: inherit
        // (USS cannot say it) is the parent's size, inline
        // an element the scan found unchanged (same fingerprint as when its font was last set) keeps it; only a
        // font-size: inherit element follows its parent every time
        static readonly Dictionary<VisualElement, int> _fontSig = new Dictionary<VisualElement, int>();

        static void FontsOf(VisualElement e)
        {
            int sig = _sig.TryGetValue(e, out var sg) ? sg : 0;
            if (sig != 0 && _fontSig.TryGetValue(e, out var had) && had == sig)
            {
                if (_inheritSize.Contains(e) && e.parent != null)
                {
                    float size = e.parent.resolvedStyle.fontSize;
                    if (Math.Abs(e.style.fontSize.value.value - size) > 0.01f) e.style.fontSize = size;
                }
                foreach (var c in e.Children()) FontsOf(c);
                return;
            }
            if (sig != 0) _fontSig[e] = sig;
            if (_fontIdx.Last(e, out var families))
            {
                if (families[0] != "inherit")
                {
                    string key = string.Join(",", families);
                    if (!_fonts.TryGetValue(key, out var f)) _fonts[key] = f = OsFont(families);
                    if (e.style.unityFontDefinition.value != f) e.style.unityFontDefinition = f;
                }
                else if (e.style.unityFontDefinition.keyword != StyleKeyword.Null) e.style.unityFontDefinition = StyleKeyword.Null;
            }
            if (_sizeIdx.Last(e, out var inherit) && inherit && e.parent != null)
            {
                float size = e.parent.resolvedStyle.fontSize;
                if (_inheritSize.Add(e) || Math.Abs(e.style.fontSize.value.value - size) > 0.01f) e.style.fontSize = size;
            }
            else if (_inheritSize.Remove(e)) e.style.fontSize = StyleKeyword.Null;
            foreach (var c in e.Children()) FontsOf(c);
        }

        static readonly HashSet<VisualElement> _inheritSize = new HashSet<VisualElement>();

        /// <summary>font-family rules of the host's own stylesheet (page-fantasy.uss), after the page's; "res:PATH"
        /// is a font in Resources.</summary>
        public static readonly List<(string sel, string[] families)> HostFonts = new List<(string, string[])>();
        /// <summary>The host's own calc() sizes, after the page's (later wins; "" hands the property back to the stylesheet).</summary>
        public static readonly List<(string sel, string prop, string value)> HostCalcs = new List<(string, string, string)>();
        /// <summary>The host's own layout rules (display, grid, gaps...), after the page's: later wins.</summary>
        public static readonly List<Rule> HostLayouts = new List<Rule>();
        /// <summary>The host's own backgrounds, after the page's: "" leaves the stylesheet's picture (a theme's stone
        /// button) instead of the page's gradient, which is painted as an inline picture and would cover it.</summary>
        public static readonly List<(string sel, string value)> HostBackgrounds = new List<(string, string)>();
        /// <summary>Boxes the host makes scroll (overflow: auto), besides the page's.</summary>
        public static readonly List<string> HostScrollable = new List<string>();

        /// <summary>A class on the root changed what matches everywhere (a theme): everything is matched again.</summary>
        public static void Restyle() { _sig.Clear(); _collected.Clear(); _fontSig.Clear(); }

        static void MinWidths(VisualElement root)
        {
            var seen = new HashSet<VisualElement>();
            foreach (var (sel, first) in CssInfo.MinWidthPct)
                if (first > 0)
                foreach (var e in Dom.QAll(root, sel))
                {
                    if (e.parent == null || !seen.Add(e)) continue;
                    // the cascade: the last min-width that matches wins
                    float pct = -1;
                    foreach (var (s2, p2) in CssInfo.MinWidthPct) if (Dom.MatchesSelector(e, s2)) pct = p2;
                    if (pct <= 0) { if (e.style.minWidth.keyword != StyleKeyword.Null) e.style.minWidth = StyleKeyword.Null; continue; }
                    var ps = e.parent.resolvedStyle;
                    float w = (ps.width - ps.paddingLeft - ps.paddingRight - ps.borderLeftWidth - ps.borderRightWidth) * pct / 100;
                    if (float.IsNaN(w)) continue;
                    var cur = e.style.minWidth;
                    Set(ref cur, w, v => e.style.minWidth = v);
                }
        }

        static HashSet<string> _installed;

        /// <summary>The first installed family of a font-family list, falling back (per glyph) to the symbol
        /// and emoji fonts the way a browser does.</summary>
        public static FontDefinition OsFont(string[] families)
        {
            _installed = _installed ?? new HashSet<string>(UnityEngine.Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            UnityEngine.TextCore.Text.FontAsset Make(string family) => _installed.Contains(family) ? WithFaces(family) : null;
            UnityEngine.TextCore.Text.FontAsset main = null;
            if (families[0].StartsWith("res:"))
            {
                var res = UnityEngine.Resources.Load<UnityEngine.Font>(families[0].Substring(4));
                main = res != null ? UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(res) : null;
                if (main == null) return OsFont(families.Skip(1).ToArray());
            }
            else
            foreach (var f in families) if ((main = Make(f)) != null) break;
            if (main == null) return FontDefinition.FromFont(UnityEngine.Font.CreateDynamicFontFromOSFont(families, 14));
            main.fallbackFontAssetTable = new List<UnityEngine.TextCore.Text.FontAsset>();
            foreach (var f in new[] { "Segoe UI", "Segoe UI Symbol", "Arial", "Noto Sans Symbols 2", "DejaVu Sans" })
            {
                if (families.Contains(f)) continue;
                var fb = Make(f);
                if (fb != null) main.fallbackFontAssetTable.Add(fb);
            }
            return FontDefinition.FromSDFFont(main);
        }

        // the family's own bold / italic / semibold faces in the weight table (as a browser picks them), not the
        // regular face made bold or slanted by the text engine - which is wider, and not what the page shows
        static UnityEngine.TextCore.Text.FontAsset WithFaces(string family)
        {
            var font = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Regular");
            if (font == null) return null;
            UnityEngine.TextCore.Text.FontAsset Face(string style)
            {
                try { return UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, style); } catch (Exception) { return null; }
            }
            var table = font.fontWeightTable;
            if (table == null || table.Length < 10) return font;
            var bold = Face("Bold");
            if (bold != null) table[7].regularTypeface = bold;
            var italic = Face("Italic");
            if (italic != null) table[4].italicTypeface = italic;
            var boldItalic = Face("Bold Italic");
            if (boldItalic != null) table[7].italicTypeface = boldItalic;
            var semibold = Face("Semibold");
            if (semibold != null) table[6].regularTypeface = semibold;
            return font;
        }

        /// <summary>aspect-ratio, and a canvas / img whose height is auto: the height follows the width.</summary>
        static void Aspect(VisualElement e, State st)
        {
            if (st.height == "set")
            {
                // width: auto + height + aspect-ratio: the width follows the height
                if (st.aspect <= 0 || st.width == "set") return;
                float hh = e.resolvedStyle.height;
                if (float.IsNaN(hh) || hh <= 0) return;
                var cw = e.style.width;
                Set(ref cw, (float)Math.Round(hh * st.aspect, 2), v => e.style.width = v);
                return;
            }
            float ratio = st.aspect > 0 ? st.aspect : 0;
            if (ratio == 0 && st.width == "set")
            {
                if (e is CanvasEl c && c.height > 0) ratio = (float)c.width / c.height;
                else if (e is Image img && img.image != null && img.image.height > 0) ratio = (float)img.image.width / img.image.height;
                else if (e is SvgEl svg)
                {
                    // an svg sized by its width: the viewBox's shape
                    var vb = (svg.GetAttr("viewBox") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (vb.Length == 4 && float.TryParse(vb[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw) && float.TryParse(vb[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh) && vh > 0) ratio = vw / vh;
                }
            }
            if (ratio <= 0) return;
            float w = e.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            float h = (float)Math.Round(w / ratio, 2);
            var cur = e.style.height;
            Set(ref cur, h, v => e.style.height = v);
        }

        // ---- overflow: auto / scroll: the content moves up by scrollTop (the first child's top margin) ----
        static readonly Dictionary<VisualElement, float> _scrollWanted = new Dictionary<VisualElement, float>();
        static readonly Dictionary<VisualElement, float> _scrollTop = new Dictionary<VisualElement, float>();
        static readonly Dictionary<VisualElement, float> _scrolledChild = new Dictionary<VisualElement, float>();
        static readonly Dictionary<VisualElement, VisualElement> _scrollFirst = new Dictionary<VisualElement, VisualElement>();
        static readonly HashSet<VisualElement> _wheel = new HashSet<VisualElement>();

        // the page itself scrolls when it is taller than the window (fixed boxes, moved under the root, stay:
        // only the first child - the page's content - moves); targets: the root and the overflow: auto boxes
        static void Scrollables(List<VisualElement> targets)
        {
            foreach (var e in targets)
                {
                    if (!_wheel.Add(e)) continue;
                    e.RegisterCallback<WheelEvent>(w =>
                    {
                        float max = ScrollHeight(e) - ClientHeight(e);
                        if (max <= 0) return;   // nothing to scroll: the wheel goes on to the parent
                        SetScrollTop(e, ScrollTop(e) + w.delta.y * 20);
                        w.StopPropagation();
                    });
                }
            foreach (var gone in _wheel.Where(e => e.panel == null).ToList()) { _wheel.Remove(gone); _scrollHeld.Remove(gone); _scrollWanted.Remove(gone); _scrollTop.Remove(gone); _scrollFirst.Remove(gone); }
        }

        // ---- scroll bars: a box that scrolls shows where it is and scrolls without a wheel (the browser's own bar) ----
        // the bar is the box's last child, absolute on its right edge: the scroll moves only the in-flow content
        static readonly Dictionary<VisualElement, (VisualElement track, VisualElement thumb)> _bars = new Dictionary<VisualElement, (VisualElement, VisualElement)>();

        static void ScrollBars(VisualElement root)
        {
            foreach (var e in _wheel)
            {
                if (e == root) continue;
                _bars.TryGetValue(e, out var bar);
                bool show = e.panel != null && Displayed(e);
                float client = show ? ClientHeight(e) : 0, max = show ? ScrollHeight(e) - client : 0;
                if (!show || float.IsNaN(max) || max <= 1 || client < 40)
                {
                    if (bar.track != null && bar.track.style.display != DisplayStyle.None) bar.track.style.display = DisplayStyle.None;
                    if (_barRoom.TryGetValue(e, out var had)) { e.style.paddingRight = had; _barRoom.Remove(e); }
                    continue;
                }
                if (bar.track == null) _bars[e] = bar = MakeBar(e);
                if (bar.track.parent != e) e.Add(bar.track);   // the page replaced the box's children
                if (bar.track.style.display != DisplayStyle.Flex) bar.track.style.display = DisplayStyle.Flex;
                // room for the bar beside the content (as the browser's bar takes its width)
                if (!_barRoom.ContainsKey(e)) { _barRoom[e] = e.style.paddingRight; e.style.paddingRight = Num(e.resolvedStyle.paddingRight) + 12; }
                float h = client - 8, th = Math.Max(24, h * client / (client + max));
                float top = (h - th) * Math.Min(ScrollTop(e), max) / max;
                if (Math.Abs(bar.thumb.style.height.value.value - th) > 0.5f) bar.thumb.style.height = th;
                if (Math.Abs(bar.thumb.style.top.value.value - top) > 0.5f) bar.thumb.style.top = top;
            }
            if (_frame % 60 == 0) foreach (var gone in _bars.Keys.Where(e => e.panel == null && !_wheel.Contains(e)).ToList()) { _bars.Remove(gone); _barRoom.Remove(gone); }
        }

        static readonly Dictionary<VisualElement, StyleLength> _barRoom = new Dictionary<VisualElement, StyleLength>();

        static (VisualElement, VisualElement) MakeBar(VisualElement e)
        {
            var gold = new UnityEngine.Color(0.66f, 0.53f, 0.25f);
            var track = new VisualElement { name = "scrollbar" };
            var ts = track.style;
            ts.position = Position.Absolute; ts.top = 4; ts.bottom = 4; ts.right = 2; ts.width = 8;
            ts.backgroundColor = new UnityEngine.Color(gold.r, gold.g, gold.b, 0.12f);
            ts.borderTopLeftRadius = ts.borderTopRightRadius = ts.borderBottomLeftRadius = ts.borderBottomRightRadius = 4;
            var thumb = new VisualElement();
            var hs = thumb.style;
            hs.position = Position.Absolute; hs.left = 0; hs.right = 0; hs.top = 0; hs.height = 24;
            hs.backgroundColor = gold;
            hs.borderTopLeftRadius = hs.borderTopRightRadius = hs.borderBottomLeftRadius = hs.borderBottomRightRadius = 4;
            track.Add(thumb);
            thumb.RegisterCallback<PointerEnterEvent>(_ => thumb.style.backgroundColor = new UnityEngine.Color(0.9f, 0.75f, 0.4f));
            thumb.RegisterCallback<PointerLeaveEvent>(_ => { if (!thumb.HasPointerCapture(0)) thumb.style.backgroundColor = gold; });
            // the track: a page up or down, towards the click
            track.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.target != track) return;
                float page = ClientHeight(e) * 0.9f;
                SetScrollTop(e, ScrollTop(e) + (ev.localPosition.y < thumb.layout.y ? -page : page));
                ev.StopPropagation();
            });
            // the press is the bar's alone: no click on the page under it
            track.RegisterCallback<PointerUpEvent>(ev => ev.StopPropagation());
            track.RegisterCallback<ClickEvent>(ev => ev.StopPropagation());
            // the thumb: dragged, the content follows
            float startY = 0, startTop = 0;
            thumb.RegisterCallback<PointerDownEvent>(ev =>
            {
                startY = ev.position.y; startTop = ScrollTop(e);
                thumb.CapturePointer(ev.pointerId);
                ev.StopPropagation();
            });
            thumb.RegisterCallback<PointerMoveEvent>(ev =>
            {
                if (!thumb.HasPointerCapture(ev.pointerId)) return;
                float client = ClientHeight(e), max = ScrollHeight(e) - client, room = track.layout.height - thumb.layout.height;
                if (room > 0 && max > 0) SetScrollTop(e, Math.Max(0, Math.Min(max, startTop + (ev.position.y - startY) * max / room)));
                ev.StopPropagation();
            });
            thumb.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (thumb.HasPointerCapture(ev.pointerId)) thumb.ReleasePointer(ev.pointerId);
                thumb.style.backgroundColor = gold;
                ev.StopPropagation();
            });
            return (track, thumb);
        }

        public static float ScrollTop(VisualElement e) => _scrollTop.TryGetValue(e, out var v) ? v : 0;

        /// <summary>el.scrollTop = v (clamped to the content once it is laid out, as the browser does).</summary>
        public static void SetScrollTop(VisualElement e, float v)
        {
            // "scrollTop = scrollHeight" before the new content is laid out (layout is a frame late here):
            // the bottom, wherever it ends up; but 0 is the top (an emptied box measures 0 high: its new content
            // was pinned to the bottom)
            _scrollWanted[e] = v > 0 && v >= ScrollHeight(e) - 1 ? float.MaxValue : Math.Max(0, v);
            ApplyScroll(e);
        }

        /// <summary>el.scrollHeight: the content's height, scrolled or not.</summary>
        public static float ScrollHeight(VisualElement e)
        {
            if (_scrollWanted.TryGetValue(e, out var w) && w == float.MaxValue)
            {
                float h = ContentHeight(e);
                return !float.IsNaN(h) ? h : _contentHeight.TryGetValue(e, out var known) ? known : 0;
            }
            float bottom = 0;
            foreach (var c in e.Children())
                if (c.resolvedStyle.display != DisplayStyle.None && c.resolvedStyle.position != Position.Absolute)
                    bottom = Math.Max(bottom, Num(c.layout.yMax) + Num(c.resolvedStyle.marginBottom));
            // a grid's tiles are shifted by translate, which leaves their layout where it was
            return bottom + (Wraps(e) ? 0 : ScrollTop(e)) + Num(e.resolvedStyle.paddingBottom) - Num(e.resolvedStyle.borderTopWidth);
        }

        static bool Wraps(VisualElement e) => e.resolvedStyle.flexWrap != Wrap.NoWrap && e.resolvedStyle.flexDirection == FlexDirection.Row;

        /// <summary>The in-flow children's heights with their margins and the padding: NaN until laid out.</summary>
        static float ContentHeight(VisualElement e)
        {
            float sum = Num(e.resolvedStyle.paddingTop) + Num(e.resolvedStyle.paddingBottom);
            foreach (var c in e.Children())
            {
                if (c.resolvedStyle.display == DisplayStyle.None || c.resolvedStyle.position == Position.Absolute) continue;
                if (float.IsNaN(c.layout.height)) return float.NaN;
                sum += c.layout.height + Num(c.resolvedStyle.marginTop) + Num(c.resolvedStyle.marginBottom);
            }
            return sum;
        }

        static float ClientHeight(VisualElement e) => Num(e.layout.height) - Num(e.resolvedStyle.borderTopWidth) - Num(e.resolvedStyle.borderBottomWidth);

        static readonly Dictionary<VisualElement, float> _contentHeight = new Dictionary<VisualElement, float>();
        static readonly Dictionary<VisualElement, StyleLength> _scrollHeld = new Dictionary<VisualElement, StyleLength>();

        /// <summary>A scrolled box's children were replaced: its new first child takes the scroll at once.</summary>
        public static void KeepScroll(VisualElement e) { if (_scrollWanted.ContainsKey(e)) ApplyScroll(e); }

        static void ApplyScroll(VisualElement e)
        {
            if (!_scrollWanted.TryGetValue(e, out var want)) return;
            if (want == float.MaxValue)
            {
                // the bottom: content pinned to the end once it overflows - no measuring of children that the
                // page may have just rebuilt (layout runs a frame late here)
                if (_scrollFirst.TryGetValue(e, out var pinned) && pinned != null) { _scrolledChild.Remove(pinned); Commit(pinned); _scrollFirst.Remove(e); }
                float content = ContentHeight(e);
                if (!float.IsNaN(content)) _contentHeight[e] = content;
                bool overflows = _contentHeight.TryGetValue(e, out var ch) && ch > ClientHeight(e) + 0.5f;
                var j = overflows ? Justify.FlexEnd : Justify.FlexStart;
                if (e.resolvedStyle.justifyContent != j || e.style.justifyContent.keyword != StyleKeyword.Undefined) e.style.justifyContent = j;
                _scrollTop[e] = overflows ? ch - ClientHeight(e) : 0;
                return;
            }
            if (e.style.justifyContent.keyword == StyleKeyword.Undefined) e.style.justifyContent = StyleKeyword.Null;
            float max = Math.Max(0, ScrollHeight(e) - ClientHeight(e));
            // children the page just put in (a list redrawn after a click) are not laid out yet: they measure
            // nothing, which clamped the scroll to the top for a frame and then back to a wrong place; the box
            // stays where it was until they are, as the browser keeps scrollTop over replaceChildren
            bool fresh = e.Children().Any(c => c.style.display != DisplayStyle.None && float.IsNaN(c.layout.height));
            float top = fresh ? Math.Min(want, ScrollTop(e)) : Math.Min(want, max);
            if (Wraps(e))
            {
                // a grid (rows of tiles): pulling up the first child moved one tile only, and holding the box's
                // height stretched every tile to it; the tiles are all shifted instead (layout stays as it is)
                _scrollTop[e] = top;
                foreach (var c in e.Children())
                {
                    if (c.resolvedStyle.position == Position.Absolute) continue;   // the scroll bar
                    float now = c.style.translate.keyword == StyleKeyword.Undefined ? -c.style.translate.value.y.value : 0;
                    if (Math.Abs(now - top) > 0.01f) c.style.translate = top > 0 ? new Translate(0, -top) : new StyleTranslate(StyleKeyword.Null);
                }
                return;
            }
            var first = e.Children().FirstOrDefault(c => c.resolvedStyle.display != DisplayStyle.None && c.resolvedStyle.position != Position.Absolute);
            if (_scrollFirst.TryGetValue(e, out var old) && old != first && old != null) { _scrolledChild.Remove(old); Commit(old); }
            _scrollTop[e] = top;
            // the content is scrolled by pulling its first child up, which shortens a box sized by its content
            // (a modal's box, up to its max-height): it keeps the height it had unscrolled, as in the browser -
            // it shrank and, centred, jumped about while the wheel turned
            if (top > 0 && !_scrollHeld.ContainsKey(e)) { _scrollHeld[e] = e.style.minHeight; e.style.minHeight = Num(e.layout.height); }
            else if (top <= 0 && _scrollHeld.TryGetValue(e, out var heldMin)) { e.style.minHeight = heldMin; _scrollHeld.Remove(e); }
            if (first == null) return;
            _scrollFirst[e] = first;
            if (top <= 0) { if (_scrolledChild.Remove(first)) Commit(first); return; }   // at the top: its own margins again
            if (_scrolledChild.TryGetValue(first, out var had) && Math.Abs(had - top) < 0.5f) return;
            _scrolledChild[first] = top;
            Commit(first);
        }

        /// <summary>position: fixed (CssInfo.Fixed): the element moves under the root, whose box is the viewport;
        /// last in the root, it draws over the page like the page's z-index puts it.</summary>
        // ---- inline formatting: a block whose children are all inline-level flows them in a row ----
        static readonly HashSet<string> InlineTags = new HashSet<string> { "span", "a", "b", "i", "em", "strong", "small", "kbd", "label", "img", "button", "input", "select", "textarea", "svg", "canvas", "code", "abbr", "sub", "sup" };
        static readonly HashSet<VisualElement> _inlineFlow = new HashSet<VisualElement>();

        static bool InlineLevel(VisualElement c)
        {
            if (!(c.userData is DomData d)) return c.ClassListContains("text") || c.ClassListContains("ps-before") || c.ClassListContains("ps-after") || c.ClassListContains("inline-run");
            if (_states.TryGetValue(c, out var st) && st.level != null) return st.level == "inline";
            return InlineTags.Contains(d.tag);
        }

        static readonly Dictionary<VisualElement, string> _alignOf = new Dictionary<VisualElement, string>();
        static readonly HashSet<VisualElement> _shrunk = new HashSet<VisualElement>();
        static readonly HashSet<string> Replaced = new HashSet<string> { "button", "input", "select", "textarea", "img", "canvas", "svg" };

        static void InlineFlow(VisualElement e, string align, bool deep = true)
        {
            _states.TryGetValue(e, out var st);
            if (st?.textAlign != null) align = st.textAlign;
            _alignOf[e] = align;
            // block flow: a button / input / picture on its own line is as wide as its content (Yoga's column
            // would stretch it), placed by text-align
            bool block = e.userData is DomData && (st == null || st.display == null || st.display == "block") && !e.ClassListContains("inline-flow")
                && !(e is SvgEl) && !(e is DomInput) && !(e is DomSelect);
            foreach (var c in e.Children())
            {
                bool shrink = block && c.userData is DomData cd && Replaced.Contains(cd.tag) && c.resolvedStyle.position != Position.Absolute
                    && !(_states.TryGetValue(c, out var cst) && cst.width == "set");
                if (shrink)
                {
                    var a = align == "center" ? Align.Center : align == "right" || align == "end" ? Align.FlexEnd : Align.FlexStart;
                    if (c.style.alignSelf != a) c.style.alignSelf = a;
                    _shrunk.Add(c);
                }
                else if (_shrunk.Remove(c)) c.style.alignSelf = StyleKeyword.Null;
            }
            if (e.userData is DomData d && !(e is SvgEl) && !(e is DomInput) && !(e is DomSelect))
            {
                bool flows = (st == null || (st.display != "flex" && st.display != "grid" && st.display != "contents"))
                    && e.childCount > 0 && e.Children().All(c => c.resolvedStyle.position == Position.Absolute || InlineLevel(c))
                    && e.Children().Count(c => (c.userData is DomData || c.ClassListContains("text")) && !c.ClassListContains("inline-run")) >= 2
                    && e.Children().Any(c => c.userData is DomData);   // one child flows the same as a block, and Yoga measures it better
                if (flows)
                {
                    _inlineFlow.Add(e);
                    if (!e.ClassListContains("inline-flow")) e.AddToClassList("inline-flow");
                    var j = align == "center" ? Justify.Center : align == "right" || align == "end" ? Justify.FlexEnd : Justify.FlexStart;
                    if (e.style.justifyContent != j) e.style.justifyContent = j;
                }
                else if (_inlineFlow.Remove(e)) { e.RemoveFromClassList("inline-flow"); e.style.justifyContent = StyleKeyword.Null; }
                // a child the stylesheet makes a block (.journal-quests .q small) breaks the line: the row is a
                // column, whoever marked it inline (Append does, for mixed text), and its text is not merged into
                // one run - a quest's title and its description came out as one line
                else if (e.ClassListContains("inline-flow") && e.Children().Any(c => c.userData is DomData && _states.TryGetValue(c, out var cs) && cs.level == "block"))
                { e.RemoveFromClassList("inline-flow"); e.style.justifyContent = StyleKeyword.Null; }
                MergeTextRuns(e, align);
            }
            CollapseMargins(e, st);
            if (!deep) return;
            foreach (var c in e.Children()) if (!c.ClassListContains("inline-run")) InlineFlow(c, align);
        }

        // ---- margin collapsing (CSS block flow; Yoga adds margins): two adjacent siblings are the larger margin
        // apart, and a first / last child's margin passes through a parent with no border or padding on that side
        // (its own margin becomes the larger of the two). The lower sibling's margin-top gives up what the upper
        // one's bottom has; a passed-through margin is 0 on the child and joins the parent's.
        // ponytail: positive margins only (negative ones still add); an empty box's own top/bottom do not join ----
        sealed class Collapse { public float baseTop, baseBottom, top = float.NaN, bottom = float.NaN; public bool sheetTop, sheetBottom; }
        static readonly Dictionary<VisualElement, Collapse> _collapsed = new Dictionary<VisualElement, Collapse>();
        static readonly Dictionary<VisualElement, float> _escTop = new Dictionary<VisualElement, float>(), _escBottom = new Dictionary<VisualElement, float>();

        static bool BlockContainer(VisualElement e)
        {
            if (!(e.userData is DomData) || e is SvgEl || e is DomInput || e is DomSelect || _inlineFlow.Contains(e)) return false;
            _states.TryGetValue(e, out var st);
            return (st == null || st.display == null || st.display == "block") && e.resolvedStyle.flexDirection == FlexDirection.Column;
        }

        // overflow other than visible (CssInfo.Overflows, or inline), cached per element
        static readonly Dictionary<VisualElement, bool> _clips = new Dictionary<VisualElement, bool>();
        static RuleIndex<bool> _overflowIdx;
        static bool Clips(VisualElement e)
        {
            if (e.style.overflow.keyword == StyleKeyword.Undefined) return e.style.overflow.value != Overflow.Visible;
            if (_clips.TryGetValue(e, out var v)) return v;
            _overflowIdx = _overflowIdx ?? new RuleIndex<bool>(CssInfo.Overflows.Select(x => (x, true)));
            return _clips[e] = _overflowIdx.Any(e);
        }

        static bool BlockLevel(VisualElement c) =>
            c.userData is DomData && !InlineLevel(c) && c.resolvedStyle.position != Position.Absolute && c.resolvedStyle.display != DisplayStyle.None;

        // the page's margin on a side: ours overridden inline -> the one it had; else what the sheet (or page) gives
        static float BaseMargin(VisualElement c, Collapse k, bool top)
        {
            var inline = top ? c.style.marginTop : c.style.marginBottom;
            float mine = top ? k.top : k.bottom;
            bool ours = !float.IsNaN(mine) && inline.keyword == StyleKeyword.Undefined && Math.Abs(inline.value.value - mine) < 0.01f;
            if (!ours)
            {
                float v = Num(top ? c.resolvedStyle.marginTop : c.resolvedStyle.marginBottom);
                bool sheet = inline.keyword != StyleKeyword.Undefined;
                if (top) { k.baseTop = v; k.sheetTop = sheet; k.top = float.NaN; } else { k.baseBottom = v; k.sheetBottom = sheet; k.bottom = float.NaN; }
            }
            return top ? k.baseTop : k.baseBottom;
        }

        static void SetMargin(VisualElement c, Collapse k, bool top, float applied)
        {
            float b = top ? k.baseTop : k.baseBottom;
            bool sheet = top ? k.sheetTop : k.sheetBottom;
            if (Math.Abs(applied - b) < 0.01f)
            {
                // back to the page's own margin
                if (!float.IsNaN(top ? k.top : k.bottom))
                {
                    if (top) { if (sheet) c.style.marginTop = StyleKeyword.Null; else c.style.marginTop = b; k.top = float.NaN; }
                    else { if (sheet) c.style.marginBottom = StyleKeyword.Null; else c.style.marginBottom = b; k.bottom = float.NaN; }
                }
                return;
            }
            if (top) { if (Math.Abs(k.top - applied) > 0.01f || float.IsNaN(k.top)) { c.style.marginTop = applied; k.top = applied; } }
            else if (Math.Abs(k.bottom - applied) > 0.01f || float.IsNaN(k.bottom)) { c.style.marginBottom = applied; k.bottom = applied; }
        }

        static void CollapseMargins(VisualElement e, State st)
        {
            bool block = BlockContainer(e);
            var ers = e.resolvedStyle;
            // e passes its children's margins on when it is itself in block flow and opens no new formatting context
            bool through = block && e.parent != null && BlockContainer(e.parent) && BlockLevel(e) && !Clips(e);
            bool passTop = through && Num(ers.paddingTop) < 0.01f && Num(ers.borderTopWidth) < 0.01f;
            bool passBottom = through && Num(ers.paddingBottom) < 0.01f && Num(ers.borderBottomWidth) < 0.01f && (st == null || st.height == null);
            float escTop = 0, escBottom = 0;
            float prevBottom = -1;   // the upper block sibling's margin-bottom as laid out; -1: none (or inline content between)
            bool first = true;
            VisualElement last = null;
            foreach (var c in e.Children())
            {
                var rs = c.resolvedStyle;
                if (rs.display == DisplayStyle.None || rs.position == Position.Absolute) continue;
                if (!block || !BlockLevel(c))
                {
                    if (_collapsed.TryGetValue(c, out var old)) { BaseMargin(c, old, true); BaseMargin(c, old, false); SetMargin(c, old, true, old.baseTop); SetMargin(c, old, false, old.baseBottom); _collapsed.Remove(c); }
                    if (block) { prevBottom = -1; first = false; last = null; }
                    continue;
                }
                // a scrolled box's first child: its top margin is the scroll offset (Commit), not a margin to
                // collapse - taken for one, it was put back to 0 every scan and the wheel only ever scrolled a frame
                if (_scrolledChild.ContainsKey(c)) { prevBottom = Num(rs.marginBottom); first = false; last = null; continue; }
                if (!_collapsed.TryGetValue(c, out var k)) _collapsed[c] = k = new Collapse();
                float top = Math.Max(BaseMargin(c, k, true), _escTop.TryGetValue(c, out var et) ? et : 0);
                float bottom = Math.Max(BaseMargin(c, k, false), _escBottom.TryGetValue(c, out var eb) ? eb : 0);
                float applied = top;
                if (prevBottom > 0 && top > 0) applied = Math.Max(0, top - prevBottom);
                if (first && passTop && top > 0) { escTop = top; applied = 0; }
                SetMargin(c, k, true, applied);
                SetMargin(c, k, false, bottom);
                prevBottom = bottom;
                first = false;
                last = c;
            }
            // the last block child's bottom passes through (set to 0 on it; the parent's bottom takes it)
            if (last != null && passBottom && _collapsed.TryGetValue(last, out var lk))
            {
                escBottom = Math.Max(lk.baseBottom, _escBottom.TryGetValue(last, out var lb) ? lb : 0);
                if (escBottom > 0) SetMargin(last, lk, false, 0); else escBottom = 0;
            }
            if (escTop > 0) _escTop[e] = escTop; else _escTop.Remove(e);
            if (escBottom > 0) _escBottom[e] = escBottom; else _escBottom.Remove(e);
        }

        // ---- inline text: plain text boxes side by side wrap as one paragraph (CSS inline formatting), which
        // flex items cannot - so they are drawn as one rich-text label; the elements stay (hidden) for the page ----
        static readonly Dictionary<VisualElement, string> _runOf = new Dictionary<VisualElement, string>();
        static readonly HashSet<VisualElement> _mergedChild = new HashSet<VisualElement>();

        static bool PlainText(VisualElement c)
        {
            if (c.ClassListContains("inline-run")) return true;
            if (!(c is TextElement) || c is Button || c.Children().Any(k => !k.ClassListContains("inline-run"))) return false;
            if (c.userData is DomData d && d.handlers.Count > 0) return false;
            var rs = c.resolvedStyle;
            return rs.borderLeftWidth + rs.borderRightWidth + rs.borderTopWidth + rs.borderBottomWidth < 0.5f && rs.backgroundColor.a < 0.01f
                && rs.paddingLeft + rs.paddingRight < 0.5f && (rs.position != Position.Absolute || _mergedChild.Contains(c));
        }

        static string Hex(UnityEngine.Color c) => "#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(c);

        static void MergeTextRuns(VisualElement e, string align)
        {
            var kids = e.Children().Where(c => c.resolvedStyle.display != DisplayStyle.None).ToList();
            var parts = kids.Where(c => !c.ClassListContains("inline-run")).ToList();
            bool merge = e.ClassListContains("inline-flow") && parts.Count >= 2 && kids.All(PlainText);
            var run = e.Children().FirstOrDefault(c => c.ClassListContains("inline-run")) as Label;
            if (!merge)
            {
                if (run == null) return;
                run.RemoveFromHierarchy();
                _runOf.Remove(e);
                foreach (var c in e.Children()) if (_mergedChild.Remove(c)) { c.style.position = StyleKeyword.Null; c.style.visibility = StyleKeyword.Null; }
                return;
            }
            var own = e.resolvedStyle;
            var sb = new System.Text.StringBuilder();
            foreach (TextElement te in parts)
            {
                var rs = te.resolvedStyle;
                string text = te.enableRichText ? te.text : "<noparse>" + te.text.Replace("</noparse>", "") + "</noparse>";
                bool bold = (rs.unityFontStyleAndWeight == UnityEngine.FontStyle.Bold || rs.unityFontStyleAndWeight == UnityEngine.FontStyle.BoldAndItalic) && own.unityFontStyleAndWeight != rs.unityFontStyleAndWeight;
                bool italic = (rs.unityFontStyleAndWeight == UnityEngine.FontStyle.Italic || rs.unityFontStyleAndWeight == UnityEngine.FontStyle.BoldAndItalic) && own.unityFontStyleAndWeight != rs.unityFontStyleAndWeight;
                if (Math.Abs(rs.fontSize - own.fontSize) > 0.1f) text = $"<size={rs.fontSize.ToString(CultureInfo.InvariantCulture)}px>{text}</size>";
                if (rs.color != own.color) text = $"<color={Hex(rs.color)}>{text}</color>";
                if (bold) text = $"<b>{text}</b>";
                if (italic) text = $"<i>{text}</i>";
                sb.Append(text);
            }
            string value = sb.ToString();
            if (run == null)
            {
                run = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
                run.AddToClassList("inline-run");
                run.style.flexGrow = 1;
                run.style.flexShrink = 1;
                run.style.minWidth = 0;
                run.style.whiteSpace = WhiteSpace.Normal;
                e.Add(run);   // last: the page reads its own children by index (el.firstChild, el.children[0])
            }
            run.style.unityTextAlign = align == "center" ? UnityEngine.TextAnchor.UpperCenter : align == "right" || align == "end" ? UnityEngine.TextAnchor.UpperRight : UnityEngine.TextAnchor.UpperLeft;
            foreach (var c in parts)
                if (_mergedChild.Add(c)) { c.style.position = Position.Absolute; c.style.visibility = Visibility.Hidden; }
            if (_runOf.TryGetValue(e, out var had) && had == value) return;
            _runOf[e] = value;
            run.text = value;
        }

        // ---- gradient backgrounds (CssInfo.Backgrounds, inline style.background) ----
        static readonly Dictionary<VisualElement, string> _gradient = new Dictionary<VisualElement, string>();
        static readonly Dictionary<VisualElement, string> _inlineBackground = new Dictionary<VisualElement, string>();
        static readonly Dictionary<VisualElement, (string, int, int)> _painted = new Dictionary<VisualElement, (string, int, int)>();
        static RuleIndex<string> _bgIdx;

        /// <summary>el.style.background = value from the page's code: a gradient, or null / plain to clear it.</summary>
        public static void SetInlineBackground(VisualElement e, string value)
        {
            _inlineBackground[e] = value ?? "";
            Background(e);
        }

        static void Background(VisualElement e)
        {
            string value = null;
            if (_inlineBackground.TryGetValue(e, out var inline)) value = inline;
            else
            {
                _bgIdx = _bgIdx ?? new RuleIndex<string>(CssInfo.Backgrounds.Concat(HostBackgrounds).Select(b => (b.sel, b.value)));
                _bgIdx.Last(e, out value);
            }
            if (CssGradient.IsGradient(value)) { _gradient[e] = value; _urlBackground.Remove(e); }
            else if (value != null && value.Contains("url("))
            {
                _gradient.Remove(e); _painted.Remove(e);
                if (_urlBackground.TryGetValue(e, out var had) && had == value) return;
                _urlBackground[e] = value;
                UrlBackground(e, value);
            }
            else
            {
                if (_gradient.Remove(e)) { _painted.Remove(e); e.style.backgroundImage = StyleKeyword.Null; }
                if (_urlBackground.Remove(e)) e.style.backgroundImage = StyleKeyword.Null;
            }
        }

        static readonly Dictionary<VisualElement, string> _urlBackground = new Dictionary<VisualElement, string>();

        /// <summary>background: [color] url(...) [position/size] - the picture, sized cover / contain / stretched.</summary>
        static void UrlBackground(VisualElement e, string value)
        {
            var m = System.Text.RegularExpressions.Regex.Match(value, @"url\(\s*[""']?([^""')]+)[""']?\s*\)");
            var tex = m.Success ? DomImages.Texture(m.Groups[1].Value) : null;
            if (tex == null) { e.style.backgroundImage = StyleKeyword.Null; return; }
            e.style.backgroundImage = UnityEngine.UIElements.Background.FromTexture2D(tex);
            if (value.Contains("cover")) e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            else if (value.Contains("contain")) e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            if (value.Contains("no-repeat")) e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            if (value.Contains("center"))
            {
                e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            }
        }

        static void PaintGradient(VisualElement e, string value)
        {
            if (e.panel == null) return;
            int w = (int)Math.Round(Num(e.layout.width)), h = (int)Math.Round(Num(e.layout.height));
            if (w <= 0 || h <= 0) return;
            // a gradient is smooth: painted at most 256 px across and stretched (a 1300 px vortex took half a second)
            float k = Math.Min(1f, 256f / Math.Max(w, h));
            w = Math.Max(1, (int)Math.Round(w * k)); h = Math.Max(1, (int)Math.Round(h * k));
            if (_painted.TryGetValue(e, out var p) && p == (value, w, h)) { CssGradient.Keep(value, w, h); return; }
            _painted[e] = (value, w, h);
            e.style.backgroundImage = UnityEngine.UIElements.Background.FromTexture2D(CssGradient.Paint(value, w, h));
            e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }

        // ---- pointer-events (inherited; CssInfo.PointerEvents) ----
        static readonly HashSet<VisualElement> _ignored = new HashSet<VisualElement>();

        static RuleIndex<string> _pointerIdx;

        static void PointerEvents(VisualElement e)
        {
            if (!(e.userData is DomData)) return;
            _pointerIdx = _pointerIdx ?? new RuleIndex<string>(CssInfo.PointerEvents.Select(p => (p.sel, p.value)));
            _pointerIdx.Last(e, out var own);
            bool none = own != null ? own == "none" : e.parent != null && _ignored.Contains(e.parent);
            if (none) { if (_ignored.Add(e)) e.pickingMode = PickingMode.Ignore; }
            else if (_ignored.Remove(e)) e.pickingMode = PickingMode.Position;
        }

        // ---- min() / max() / calc() sizes with % (CssInfo.Calc) ----
        static List<(VisualElement e, string prop, string value)> _calcs = new List<(VisualElement, string, string)>();

        static void CalcTargets(Dictionary<(VisualElement, string), string> found)
        {
            foreach (var (e, prop) in _calcs.Select(c => (c.e, c.prop)).Where(k => !found.ContainsKey(k)).ToList()) ClearSize(e, prop);
            _calcs = found.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
        }

        static void ClearSize(VisualElement e, string prop)
        {
            switch (prop)
            {
                case "width": e.style.width = StyleKeyword.Null; break;
                case "height": e.style.height = StyleKeyword.Null; break;
                case "min-width": e.style.minWidth = StyleKeyword.Null; break;
                case "max-width": e.style.maxWidth = StyleKeyword.Null; break;
                case "min-height": e.style.minHeight = StyleKeyword.Null; break;
                case "max-height": e.style.maxHeight = StyleKeyword.Null; break;
            }
        }

        static void ApplyCalcs(VisualElement root)
        {
            // the viewport is the window (the panel), not the page's root box - that is at least 1000px tall
            // (min-height: 100vh baked at 1000), so vh ran past a smaller window
            var view = root.panel?.visualTree.layout ?? root.layout;
            float vw = view.width > 0 ? view.width : root.resolvedStyle.width, vh = view.height > 0 ? view.height : root.resolvedStyle.height;
            foreach (var (e, prop, value) in _calcs)
            {
                if (e.panel == null || e.parent == null) continue;
                var ps = e.parent.resolvedStyle;
                bool horizontal = !prop.EndsWith("height");
                float basis = horizontal ? ps.width - ps.paddingLeft - ps.paddingRight - ps.borderLeftWidth - ps.borderRightWidth
                                         : ps.height - ps.paddingTop - ps.paddingBottom - ps.borderTopWidth - ps.borderBottomWidth;
                float v;
                try { v = new CssExpr(value, basis, vw, vh).Value(); } catch (Exception) { continue; }
                if (float.IsNaN(v)) continue;
                v = (float)Math.Round(v, 2);
                var st = e.style;
                switch (prop)
                {
                    case "width": { var c = st.width; Set(ref c, v, x => st.width = x); break; }
                    case "height": { var c = st.height; Set(ref c, v, x => st.height = x); break; }
                    case "min-width": { var c = st.minWidth; Set(ref c, v, x => st.minWidth = x); break; }
                    case "max-width": { var c = st.maxWidth; Set(ref c, v, x => st.maxWidth = x); break; }
                    case "min-height": { var c = st.minHeight; Set(ref c, v, x => st.minHeight = x); break; }
                    case "max-height": { var c = st.maxHeight; Set(ref c, v, x => st.maxHeight = x); break; }
                }
            }
        }

        /// <summary>A CSS length expression: numbers with px / % / vw / vh / em (14px), + - * /, calc / min / max / clamp.</summary>
        sealed class CssExpr
        {
            readonly string s;
            readonly float pct, vw, vh;
            int i;

            public CssExpr(string s, float pct, float vw, float vh) { this.s = s; this.pct = pct; this.vw = vw; this.vh = vh; }

            public float Value() { i = 0; float v = Sum(); Skip(); if (i != s.Length) throw new FormatException(s); return v; }

            void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i += 1; }

            float Sum()
            {
                float v = Product();
                for (Skip(); i < s.Length && (s[i] == '+' || s[i] == '-'); Skip()) { char op = s[i++]; float r = Product(); v = op == '+' ? v + r : v - r; }
                return v;
            }

            float Product()
            {
                float v = Atom();
                for (Skip(); i < s.Length && (s[i] == '*' || s[i] == '/'); Skip()) { char op = s[i++]; float r = Atom(); v = op == '*' ? v * r : v / r; }
                return v;
            }

            float Atom()
            {
                Skip();
                if (s[i] == '(') { i += 1; float v = Sum(); Skip(); i += 1; return v; }
                if (char.IsLetter(s[i]))
                {
                    int start = i;
                    while (i < s.Length && char.IsLetter(s[i])) i += 1;
                    string fn = s.Substring(start, i - start);
                    Skip(); i += 1;   // (
                    var args = new List<float> { Sum() };
                    for (Skip(); s[i] == ','; Skip()) { i += 1; args.Add(Sum()); }
                    i += 1;   // )
                    switch (fn)
                    {
                        case "min": return args.Min();
                        case "max": return args.Max();
                        case "clamp": return Math.Min(Math.Max(args[0], args[1]), args[2]);
                        default: return args[0];   // calc
                    }
                }
                int n = i;
                if (s[i] == '-' || s[i] == '+') i += 1;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i += 1;
                float num = float.Parse(s.Substring(n, i - n), CultureInfo.InvariantCulture);
                int u = i;
                while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '%')) i += 1;
                switch (s.Substring(u, i - u))
                {
                    case "%": return num * pct / 100;
                    case "vw": return num * vw / 100;
                    case "vh": return num * vh / 100;
                    case "em": case "rem": return num * 14;
                    default: return num;
                }
            }
        }

        // ---- ::before / ::after (CssInfo.Pseudo) ----
        static Dictionary<string, List<int>> _pseudoByKey;

        static void Pseudo(VisualElement e)
        {
            if (!(e.userData is DomData) || e.ClassListContains("text")) return;
            if (_pseudoByKey == null)
            {
                _pseudoByKey = new Dictionary<string, List<int>>();
                for (int i = 0; i < CssInfo.Pseudo.Length; i += 1)
                {
                    string k = KeyOf(CssInfo.Pseudo[i].sel);
                    if (!_pseudoByKey.TryGetValue(k, out var l)) _pseudoByKey[k] = l = new List<int>();
                    l.Add(i);
                }
            }
            string[] content = new string[2], color = new string[2];
            float[] size = { -1, -1 };
            bool any = false;
            var hits = new List<int>();
            foreach (var c in e.GetClasses()) if (_pseudoByKey.TryGetValue("." + c, out var l)) hits.AddRange(l);
            hits.Sort();
            foreach (int i in hits)
            {
                var p = CssInfo.Pseudo[i];
                if (!Dom.MatchesSelector(e, p.sel)) continue;
                int w = p.which == "before" ? 0 : 1;
                any = true;
                if (p.content != null) content[w] = p.content;
                if (p.color != null) color[w] = p.color;
                if (p.size > 0) size[w] = p.size;
            }
            for (int w = 0; w < 2; w += 1) if (content[w] == "\u0000none") content[w] = null;
            if (e is TextElement te && !e.Children().Any(c => c.userData is DomData))
            {
                // a text element: the content joins its text as rich-text runs
                string Run(int w) => content[w] == null ? "" : $"{(color[w] != null ? $"<color={color[w]}>" : "")}{(size[w] > 0 ? $"<size={size[w].ToString(CultureInfo.InvariantCulture)}px>" : "")}<noparse>{content[w]}</noparse>{(size[w] > 0 ? "</size>" : "")}{(color[w] != null ? "</color>" : "")}";
                var deco = new[] { any ? Run(0) : "", any ? Run(1) : "" };
                bool had = Dom.Decorations.TryGetValue(te, out var old);
                if (had && old[0] == deco[0] && old[1] == deco[1]) return;
                if (!had && deco[0].Length == 0 && deco[1].Length == 0) return;
                Dom.Decorations.Remove(te);
                Dom.Decorations.Add(te, deco);
                Dom.ShowText(te, Dom.RawText(te));
                return;
            }
            // a container: a label child, styled by the page's rules for "> .ps-before" / "> .ps-after"
            for (int w = 0; w < 2; w += 1)
            {
                string cls = w == 0 ? "ps-before" : "ps-after";
                var label = e.Children().FirstOrDefault(c => c.ClassListContains(cls)) as Label;
                if (content[w] == null) { label?.RemoveFromHierarchy(); continue; }
                if (label == null)
                {
                    label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                    label.AddToClassList(cls);
                    if (w == 0) e.Insert(0, label); else e.Add(label);
                }
                if (label.text != content[w]) label.text = content[w];
            }
        }

        // ---- @media (max-width: N): .mq-maxN on the root while the window is that narrow ----
        static float _mediaWidth = -1;

        static void Media(VisualElement root)
        {
            float w = root.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0 || Math.Abs(w - _mediaWidth) < 0.5f) return;
            _mediaWidth = w;
            bool changed = false;
            foreach (int max in CssInfo.MediaMaxWidths)
            {
                bool on = w <= max;
                if (root.ClassListContains("mq-max" + max) != on) { root.EnableInClassList("mq-max" + max, on); changed = true; }
            }
            if (changed) _frame = 0;   // the rules that match changed: rescan now
        }

        // ---- order: flex items by order, then source order (the page's own child order is kept aside) ----
        static readonly Dictionary<VisualElement, List<VisualElement>> _sourceOrder = new Dictionary<VisualElement, List<VisualElement>>();

        static int OrderOf(VisualElement e) => _states.TryGetValue(e, out var st) ? st.order : 0;

        static void Order(VisualElement root)
        {
            var parents = new HashSet<VisualElement>(_sourceOrder.Keys);
            foreach (var kv in _states) if (kv.Value.order != 0 && kv.Key.parent != null) parents.Add(kv.Key.parent);
            foreach (var p in parents)
            {
                if (p.panel == null) { _sourceOrder.Remove(p); continue; }
                var kids = p.Children().ToList();
                // children added or removed since: the current list is the page's order
                if (!_sourceOrder.TryGetValue(p, out var source) || source.Count != kids.Count || kids.Any(k => !source.Contains(k))) _sourceOrder[p] = source = kids;
                var want = source.Select((k, i) => (k, i)).OrderBy(t => OrderOf(t.k)).ThenBy(t => t.i).Select(t => t.k).ToList();
                bool same = true;
                for (int i = 0; i < want.Count && same; i += 1) same = kids[i] == want[i];
                if (!same) foreach (var k in want) k.BringToFront();
                if (want.All(k => OrderOf(k) == 0)) _sourceOrder.Remove(p);   // back in source order: forget it
            }
        }

        static readonly HashSet<VisualElement> _geometryHooked = new HashSet<VisualElement>();
        static readonly HashSet<VisualElement> _firstDone = new HashSet<VisualElement>();

        static void OnGeometry(VisualElement e)
        {
            // only the first layout: later passes belong to Tick (re-applying on every size change would chase
            // the sizes this layout itself changes)
            if (_laidOut.Contains(e) || _firstDone.Contains(e)) return;
            if (!_states.TryGetValue(e, out var st) || e.panel == null || !Displayed(e)) return;
            _firstDone.Add(e);
            Aspect(e, st);
            if (st.Lays) { Apply(e, st); _laidOut.Add(e); }
        }

        /// <summary>Is the element rendered (no display: none on it or above)? Hidden boxes have no computed
        /// style to measure (their margins read 0).</summary>
        static bool Displayed(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        static List<VisualElement> Items(VisualElement c)
        {
            var list = new List<VisualElement>();
            foreach (var ch in c.Children())
            {
                // shown or hidden by the page this frame (SetHidden sets display inline): known now, not once the panel
                // has resolved its style a frame or more later - the neighbours' gaps jumped (the guide's list, 6px)
                var display = ch.style.display.keyword == StyleKeyword.Undefined ? ch.style.display.value : ch.resolvedStyle.display;
                if (display != DisplayStyle.None && ch.resolvedStyle.position != Position.Absolute) list.Add(ch);
            }
            return list;
        }

        /// <summary>The child's own margins (from the stylesheet), before any gap was added.</summary>
        static float[] Base(VisualElement e)
        {
            if (_baseMargins.TryGetValue(e, out var m)) return m;
            var s = e.resolvedStyle;
            m = new[] { Num(s.marginLeft), Num(s.marginTop), Num(s.marginRight), Num(s.marginBottom) };
            _baseMargins[e] = m;
            return m;
        }

        static float Num(float v) => float.IsNaN(v) ? 0 : v;

        // ---- CSS breaks lines between words only: a wrapping text box is never narrower than its longest word ----
        static readonly Dictionary<VisualElement, float> _wordMin = new Dictionary<VisualElement, float>();
        static readonly HashSet<VisualElement> _tight = new HashSet<VisualElement>();
        static readonly Dictionary<(string, object, float), float> _wordWidth = new Dictionary<(string, object, float), float>();

        static void LongestWord(TextElement te)
        {
            var rs = te.resolvedStyle;
            bool ours = _wordMin.TryGetValue(te, out var had);
            // the page's own min-width (0 included: "may shrink past the word") wins: then a word wider than the
            // box overflows it, as in CSS, instead of Unity breaking inside the word
            if (_states.TryGetValue(te, out var st) && st.minWidth)
            {
                if (ours) { _wordMin.Remove(te); te.style.minWidth = StyleKeyword.Null; }
                BreakBetweenWords(te);
                return;
            }
            if (string.IsNullOrEmpty(te.text) || rs.whiteSpace == WhiteSpace.NoWrap || (!ours && te.style.minWidth.keyword != StyleKeyword.Null))
            {
                if (ours) { _wordMin.Remove(te); te.style.minWidth = StyleKeyword.Null; }
                return;
            }
            object font = (object)rs.unityFontDefinition.fontAsset ?? rs.unityFontDefinition.font ?? (object)rs.unityFont ?? "";
            float widest = 0;
            foreach (var word in te.text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.StartsWith("<")) continue;   // a rich-text tag (::before / ::after runs)
                var key = (word, font, rs.fontSize);
                if (!_wordWidth.TryGetValue(key, out var w))
                {
                    w = te.MeasureTextSize(word, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    if (_wordWidth.Count > 20000) _wordWidth.Clear();
                    _wordWidth[key] = w;
                }
                widest = Math.Max(widest, w);
            }
            float min = (float)Math.Ceiling(widest + 1 + rs.paddingLeft + rs.paddingRight + rs.borderLeftWidth + rs.borderRightWidth);
            // a flex item shrinks (CSS flex-shrink: 1) down to its longest word; the UA sheet keeps Yoga's
            // items at 0, so text in a flex row the page did not set gets it here
            var parent = te.parent;
            bool shrinks = parent != null && parent.resolvedStyle.flexDirection == FlexDirection.Row && !_inlineFlow.Contains(parent) && !(parent.userData is DomData pd && pd.tag == "tr")
                && !(_states.TryGetValue(parent, out var ps) && (ps.display == "grid" || ps.display == "contents")) && parent.resolvedStyle.flexWrap == Wrap.NoWrap
                && !(_shrinkIdx = _shrinkIdx ?? new RuleIndex<bool>(CssInfo.ShrinkSet.Select(x => (x, true)))).Any(te);
            if (shrinks) { if (_shrinkText.Add(te)) te.style.flexShrink = 1; }
            else if (_shrinkText.Remove(te)) te.style.flexShrink = StyleKeyword.Null;
            if (ours && Math.Abs(had - min) < 0.5f) return;
            _wordMin[te] = min;
            te.style.minWidth = min;
        }

        static readonly HashSet<TextElement> _shrinkText = new HashSet<TextElement>();
        static RuleIndex<bool> _shrinkIdx;

        static readonly HashSet<TextElement> _broken = new HashSet<TextElement>();

        /// <summary>Greedy line breaking between words only (CSS), for a box too narrow for some word: the text
        /// gets its own line breaks and Unity's wrapping is turned off.</summary>
        static void BreakBetweenWords(TextElement te)
        {
            string raw = Dom.RawText(te);
            var rs = te.resolvedStyle;
            float width = Num(te.layout.width) - rs.paddingLeft - rs.paddingRight - rs.borderLeftWidth - rs.borderRightWidth;
            var words = raw.Split(' ');
            if (string.IsNullOrEmpty(raw) || raw.Contains("\n") || width <= 0 || float.IsNaN(width) || _wordMin.ContainsKey(te)) { Unbreak(te, raw); return; }
            float Measure(string t) => te.MeasureTextSize(t, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
            bool tooWide = words.Any(w => Measure(w) > width + 0.5f);
            if (!tooWide) { Unbreak(te, raw); return; }
            var lines = new List<string>();
            string cur = "";
            foreach (var w in words)
            {
                string next = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && Measure(next) > width + 0.5f) { lines.Add(cur); cur = w; }
                else cur = next;
            }
            if (cur.Length > 0) lines.Add(cur);
            string broken = string.Join("\n", lines);
            if (Dom.WordBreaks.TryGetValue(te, out var had) && had[0] == raw && had[1] == broken) return;
            Dom.WordBreaks.Remove(te);
            Dom.WordBreaks.Add(te, new[] { raw, broken });
            _broken.Add(te);
            te.style.whiteSpace = WhiteSpace.NoWrap;
            Dom.ShowText(te, raw);
        }

        static void Unbreak(TextElement te, string raw)
        {
            if (!_broken.Remove(te)) return;
            Dom.WordBreaks.Remove(te);
            te.style.whiteSpace = StyleKeyword.Null;
            Dom.ShowText(te, raw);
        }

        static readonly Dictionary<(object, float), float> _lineOf = new Dictionary<(object, float), float>();

        /// <summary>line-height, which USS lacks: the difference to the font's own line height, split above and
        /// below each line - padding when the page's lines are taller, negative margins when tighter.</summary>
        static void Leading(TextElement te, float lh, bool px)
        {
            float adj = 0;
            if (!string.IsNullOrEmpty(te.text))
            {
                var rs = te.resolvedStyle;
                float size = rs.fontSize;
                var key = ((object)rs.unityFontDefinition.fontAsset ?? rs.unityFontDefinition.font ?? (object)rs.unityFont ?? "", size);
                if (!_lineOf.TryGetValue(key, out var line))
                {
                    line = te.MeasureTextSize("Ag", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;
                    if (float.IsNaN(line) || line <= 0) return;
                    _lineOf[key] = line;
                }
                float want = px ? lh : lh * size;
                int lines = Math.Max(1, (int)Math.Round(Num(te.contentRect.height) / line));
                adj = lines * (want - line) / 2;
            }
            if (Math.Abs(adj) < 0.25f) adj = 0;
            // Unity draws letter-spacing but measures without it: an auto-sized text box gets the width the
            // spaced text needs (a box sized by the page is left alone)
            float spacing = te.resolvedStyle.letterSpacing;
            float wide = 0;
            if (!string.IsNullOrEmpty(te.text) && !float.IsNaN(spacing) && Math.Abs(spacing) > 0.01f)
            {
                var rs = te.resolvedStyle;
                float natural = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                float box = natural + rs.paddingLeft + rs.paddingRight + rs.borderLeftWidth + rs.borderRightWidth;
                bool ours = _spacing.TryGetValue(te, out var w0) && w0 > 0 && te.style.width.keyword == StyleKeyword.Undefined;
                if (ours || Math.Abs(te.layout.width - box) < 1.5f) wide = (float)Math.Ceiling(box + spacing * te.text.Length);
            }
            _leading.TryGetValue(te, out var had);
            _spacing.TryGetValue(te, out var hadWide);
            if (adj == had && wide == hadWide && (adj != 0 || wide != 0 || !_leading.ContainsKey(te))) return;
            _leading[te] = adj;
            _spacing[te] = wide;
            if (!_basePadding.TryGetValue(te, out var bp))
            {
                var r = te.resolvedStyle;
                _basePadding[te] = bp = new[] { Num(r.paddingTop), Num(r.paddingBottom), Num(r.paddingRight) };
            }
            if (adj > 0) { te.style.paddingTop = bp[0] + adj; te.style.paddingBottom = bp[1] + adj; }
            else if (te.style.paddingTop.keyword != StyleKeyword.Null) { te.style.paddingTop = StyleKeyword.Null; te.style.paddingBottom = StyleKeyword.Null; }
            if (wide > 0) te.style.width = wide;
            else if (hadWide > 0) te.style.width = StyleKeyword.Null;
            // a one-line box made narrower (negative spacing) would wrap: Unity breaks lines without the spacing
            if (wide > 0 && spacing < 0) { if (_tight.Add(te)) te.style.whiteSpace = WhiteSpace.NoWrap; }
            else if (_tight.Remove(te)) te.style.whiteSpace = StyleKeyword.Null;
            Commit(te);
        }

        static void Set(ref StyleLength cur, float v, Action<StyleLength> set)
        {
            if (cur.keyword == StyleKeyword.Undefined && Math.Abs(cur.value.value - v) < 0.01f) return;
            set(v);
        }

        /// <summary>Adds to the child's own margins. A side with nothing to add is left to the stylesheet, so
        /// margin: auto keeps working there.</summary>
        static void Margins(VisualElement e, float left, float top, float right, float bottom)
        {
            _gapAdd[e] = new[] { left, top, right, bottom };
            Commit(e);
        }

        static void Commit(VisualElement e)
        {
            var b = Base(e);
            var s = e.style;
            var a = _gapAdd.TryGetValue(e, out var g) ? g : new float[4];
            float lead = _leading.TryGetValue(e, out var l) ? Math.Min(0, l) : 0;
            float shift = _scrolledChild.TryGetValue(e, out var sh) ? -sh : 0;
            float left = a[0], top = a[1] + lead + shift, right = a[2], bottom = a[3] + lead;
            var ml = s.marginLeft; if (left != 0 || ml.keyword != StyleKeyword.Null) Set(ref ml, b[0] + left, v => s.marginLeft = v);
            var mt = s.marginTop; if (top != 0 || mt.keyword != StyleKeyword.Null) Set(ref mt, b[1] + top, v => s.marginTop = v);
            var mr = s.marginRight; if (right != 0 || mr.keyword != StyleKeyword.Null) Set(ref mr, b[2] + right, v => s.marginRight = v);
            var mb = s.marginBottom; if (bottom != 0 || mb.keyword != StyleKeyword.Null) Set(ref mb, b[3] + bottom, v => s.marginBottom = v);
        }

        static void Reset(VisualElement c)
        {
            foreach (var ch in c.Children())
            {
                if (!_baseMargins.ContainsKey(ch)) continue;
                ch.style.marginLeft = ch.style.marginTop = ch.style.marginRight = ch.style.marginBottom = StyleKeyword.Null;
                ch.style.width = StyleKeyword.Null;
                ch.style.flexGrow = ch.style.flexShrink = StyleKeyword.Null;
                _baseMargins.Remove(ch);
                _gapAdd.Remove(ch);
                if (_leading.ContainsKey(ch)) Commit(ch);
            }
        }

        // cells the grid sized, so a child that leaves the flow gives the size back
        static readonly HashSet<VisualElement> _gridSized = new HashSet<VisualElement>();

        static void Apply(VisualElement c, State st)
        {
            var items = Items(c);
            // a child out of the flow now (absolute - often only from its second frame - or hidden) keeps no gap
            // margin or cell width from when it was an item: the camp's far station, absolute at 37%, stayed pushed
            // right by the margin it had as a grid cell on its first frame
            if (c.childCount != items.Count)
            {
                var inFlow = new HashSet<VisualElement>(items);
                foreach (var ch in c.Children())
                {
                    if (inFlow.Contains(ch)) continue;
                    if (_gapAdd.Remove(ch)) Commit(ch);
                    if (_gridSized.Remove(ch)) ch.style.width = StyleKeyword.Null;
                }
            }
            if (items.Count == 0) return;
            float colGap = Math.Max(0, st.colGap), rowGap = Math.Max(0, st.rowGap);
            if (st.display != "grid")
            {
                bool row = st.dir == null || st.dir.StartsWith("row");
                bool wrap = st.wrap == "wrap";
                // Yoga stretches the items of every wrapped line to the container's whole height (CSS: to the
                // line's); the items keep their own height instead
                if (wrap && c.resolvedStyle.alignItems == Align.Stretch && c.style.alignItems != Align.FlexStart) c.style.alignItems = Align.FlexStart;
                // wrapped lines, as laid out last frame: an item starts a new line when it lies wholly past the
                // current line (items of one line overlap across it, whatever their alignment); gaps go between
                // lines and between items of a line
                var line = new int[items.Count];
                if (wrap)
                {
                    float lineEnd = float.MinValue;
                    int current = -1;
                    for (int i = 0; i < items.Count; i += 1)
                    {
                        var lb = items[i].layout;
                        float start = row ? lb.y : lb.x, end = row ? lb.yMax : lb.xMax;
                        if (current < 0 || start >= lineEnd - 0.5f) { current += 1; lineEnd = end; }
                        else lineEnd = Math.Max(lineEnd, end);
                        line[i] = current;
                    }
                }
                int lastLine = items.Count > 0 ? line[items.Count - 1] : 0;
                for (int i = 0; i < items.Count; i += 1)
                {
                    var e = items[i];
                    bool lineEnds = i == items.Count - 1 || line[i + 1] != line[i];
                    float cross = wrap && line[i] != lastLine ? (row ? rowGap : colGap) : 0;
                    if (row) Margins(e, 0, 0, lineEnds ? 0 : colGap, cross);
                    else Margins(e, 0, 0, cross, lineEnds ? 0 : rowGap);
                }
                return;
            }
            var s = c.resolvedStyle;
            float width = s.width - s.paddingLeft - s.paddingRight - s.borderLeftWidth - s.borderRightWidth;
            // a grid added this frame is not laid out yet: as a block it is as wide as its parent's content (a row
            // rebuilt every tick - the camp's bars - would otherwise never get its columns)
            if ((float.IsNaN(width) || width <= 0) && c.parent != null && !float.IsNaN(c.parent.contentRect.width) && c.parent.contentRect.width > 0
                && (c.parent.resolvedStyle.flexDirection == FlexDirection.Column || c.parent.resolvedStyle.flexDirection == FlexDirection.ColumnReverse))
                width = c.parent.contentRect.width - Num(s.marginLeft) - Num(s.marginRight) - Num(s.paddingLeft) - Num(s.paddingRight) - Num(s.borderLeftWidth) - Num(s.borderRightWidth);
            if (float.IsNaN(width) || width <= 0) return;
            // cells in column order: a display: contents item contributes its children
            var cells = new List<VisualElement>();
            foreach (var it in items) if (_states.TryGetValue(it, out var ist) && ist.display == "contents") cells.AddRange(Items(it)); else cells.Add(it);
            var tracks = Tracks(st.columns ?? "1fr", width, colGap, cells);
            int n = tracks.Length;
            // place: (child, first column, span)
            var places = new List<(VisualElement e, int col, int span, int row)>();
            int at = 0, rowNo = 0;
            // cells taken by an item spanning rows (grid-row: 1 / span 2): later rows flow around them
            var taken = new HashSet<(int row, int col)>();
            foreach (var e in items)
            {
                while (at < n && taken.Contains((rowNo, at))) { at += 1; if (at >= n) { at = 0; rowNo += 1; } }
                string gc = _states.TryGetValue(e, out var cs) ? cs.gridColumn : null;
                int start = -1, span = 1;
                // display: contents - its children are the cells: it takes a whole row and lays them on the tracks
                if (cs?.display == "contents") gc = "1 / -1";
                if (gc != null)
                {
                    var p = gc.Split('/').Select(x => x.Trim()).ToArray();
                    if (p.Length == 2 && p[1] == "-1") { start = 0; span = n; }
                    else if (p[0].StartsWith("span")) span = Math.Min(n, int.Parse(p[0].Substring(4).Trim(), CultureInfo.InvariantCulture));
                    else if (int.TryParse(p[0], out var sc)) start = Math.Min(n - 1, sc - 1);
                }
                if (start >= 0 && start < at) { at = 0; rowNo += 1; }
                if (start < 0) start = at;
                if (start + span > n) { if (at > 0) rowNo += 1; start = 0; at = 0; span = Math.Min(span, n); }
                places.Add((e, start, span, rowNo));
                if (cs?.gridRow != null)
                {
                    int rows = 1;
                    var gr = cs.gridRow.Split('/').Select(x => x.Trim()).ToArray();
                    string spanPart = gr.FirstOrDefault(x => x.StartsWith("span"));
                    if (spanPart != null) int.TryParse(spanPart.Substring(4).Trim(), out rows);
                    else if (gr.Length == 2 && int.TryParse(gr[0], out var r0) && int.TryParse(gr[1], out var r1)) rows = Math.Max(1, r1 - r0);
                    for (int rr = 1; rr < rows; rr += 1) for (int k = start; k < start + span; k += 1) taken.Add((rowNo + rr, k));
                }
                at = start + span;
                if (at >= n) { at = 0; rowNo += 1; }
            }
            int lastRow = places[places.Count - 1].row;
            // rows an item spans past the last placed row still exist in CSS (and bring their gaps)
            int spannedPast = 0;
            foreach (var t in taken) spannedPast = Math.Max(spannedPast, t.row - lastRow);
            // grid-auto-rows / grid-template-rows of fr: the rows share the grid's height
            float rowHeight = -1;
            if (st.rows != null && st.rows.Trim().Split(' ').All(t => t == "1fr" || t.StartsWith("repeat(")) && st.rows.Contains("fr"))
            {
                float height = s.height - s.paddingTop - s.paddingBottom - s.borderTopWidth - s.borderBottomWidth;
                if (!float.IsNaN(height) && height > 0) rowHeight = Math.Max(0, (height - rowGap * lastRow) / (lastRow + 1));
            }
            for (int i = 0; i < places.Count; i += 1)
            {
                var (e, col, span, r) = places[i];
                float w = colGap * (span - 1);
                for (int k = col; k < col + span; k += 1) w += tracks[k];
                // a gap before a child that skips columns (grid-column: 2 on a fresh row)
                int prevEnd = i > 0 && places[i - 1].row == r ? places[i - 1].col + places[i - 1].span : 0;
                float left = 0;
                for (int k = prevEnd; k < col; k += 1) left += tracks[k] + colGap;
                // the next child in the same row starts after a gap; a row that ends early is filled with
                // margin so the next child wraps
                bool rowEnds = i == places.Count - 1 || places[i + 1].row != r;
                float right = colGap;
                if (rowEnds) { right = 0; for (int k = col + span; k < n; k += 1) right += colGap + tracks[k]; }
                var es = e.style;
                // the grid's width is the rounded layout (up to half a pixel more than Yoga's own): the row's last
                // cell gives up a pixel so the row never wraps (a card's info dropped under its portrait, and
                // flipped back and forth as the page's sizes moved by fractions - the cards "shook")
                float slack = rowEnds ? 1f : 0.02f;
                if (_states.TryGetValue(e, out var ecs) && ecs.display == "contents") Contents(e, tracks, colGap);
                // justify-self: the item keeps its own width inside its area, the rest is margin
                string js = JustifyOf(e);
                if (js == null) { var cw = es.width; Set(ref cw, Math.Max(0, w - slack), v => es.width = v); _gridSized.Add(e); }
                else
                {
                    // fit-content: its max-content width, at most the area
                    float own = Math.Min(Math.Max(0, w - slack), MaxContent(e));
                    var cw = es.width; Set(ref cw, own, v => es.width = v);
                    float free = Math.Max(0, w - own);
                    float before = js == "center" ? free / 2 : js == "end" || js == "flex-end" || js == "right" ? free : 0;
                    left += before;
                    right += free - before;
                }
                if (rowHeight >= 0) { var ch = es.height; Set(ref ch, (float)Math.Floor(rowHeight * 100) / 100, v => es.height = v); }
                if (es.flexGrow != 0) es.flexGrow = 0;
                if (es.flexShrink != 0) es.flexShrink = 0;
                Margins(e, left, 0, right, r == lastRow ? rowGap * spannedPast : rowGap);
            }
        }

        static RuleIndex<string> _justifyIdx;

        static string JustifyOf(VisualElement e)
        {
            _justifyIdx = _justifyIdx ?? new RuleIndex<string>(CssInfo.JustifySelf.Select(x => (x.sel, x.value)));
            _justifyIdx.Last(e, out var v);
            return v == "stretch" || v == "normal" ? null : v;
        }

        static void Contents(VisualElement e, float[] tracks, float colGap)
        {
            if (e.style.flexDirection != FlexDirection.Row) e.style.flexDirection = FlexDirection.Row;
            if (e.style.alignItems != Align.Center) e.style.alignItems = Align.Center;
            var cells = Items(e);
            for (int k = 0; k < cells.Count; k += 1)
            {
                var c = cells[k];
                bool last = k == cells.Count - 1;
                float w = 0;
                if (last) for (int t = k; t < tracks.Length; t += 1) w += tracks[t] + (t > k ? colGap : 0);
                else if (k < tracks.Length) w = tracks[k];
                // the cell box is the track; a child with its own width (an input) keeps it, at the track's start
                var box = c.resolvedStyle;
                var cw = c.style.width;
                if (!_states.TryGetValue(c, out var st) || st.width != "set") Set(ref cw, Math.Max(0, w - 0.02f), v => c.style.width = v);
                float own = st?.width == "set" ? Num(box.width) : w;
                Margins(c, 0, 0, last ? 0 : colGap + (w - own), 0);
                if (c.style.flexShrink != 0) c.style.flexShrink = 0;
            }
        }

        static float[] Tracks(string template, float width, float gap, List<VisualElement> items)
        {
            var tokens = Expand(Split(template), width, gap);
            int n = Math.Max(1, tokens.Count);
            var w = new float[n];
            var fr = new float[n];
            var auto = new bool[n];
            float used = gap * (n - 1), frSum = 0;
            for (int i = 0; i < tokens.Count; i += 1)
            {
                string t = tokens[i];
                string min = null;
                if (t.StartsWith("minmax("))
                {
                    var a = Split(t.Substring(7, t.Length - 8).Replace(",", " "));
                    min = a[0];
                    t = a.Count > 1 ? a[1] : a[0];
                }
                if (t.EndsWith("fr")) { fr[i] = Float(t.Substring(0, t.Length - 2)); frSum += fr[i]; if (min != null && min.EndsWith("px")) w[i] = Float(min.Substring(0, min.Length - 2)); }
                else if (t.EndsWith("px")) w[i] = Float(t.Substring(0, t.Length - 2));
                else if (t.EndsWith("%")) w[i] = width * Float(t.Substring(0, t.Length - 1)) / 100;
                else auto[i] = true;
            }
            // auto / max-content tracks: the widest child in that column, as laid out last frame
            for (int i = 0; i < items.Count && n > 0; i += 1)
            {
                int col = i % n;
                if (!auto[col]) continue;
                w[col] = Math.Max(w[col], MaxContent(items[i]) + Base(items[i])[0] + Base(items[i])[2]);
            }
            for (int i = 0; i < n; i += 1) if (fr[i] == 0) used += w[i];
            float left = Math.Max(0, width - used);
            if (frSum > 0)
            {
                // fr tracks share what is left; one held at its minmax() minimum drops out and the rest share
                // again (CSS "find the size of an fr")
                var mins = (float[])w.Clone();
                var flexible = new bool[n];
                for (int i = 0; i < n; i += 1) flexible[i] = fr[i] > 0;
                for (int pass = 0; pass < n; pass += 1)
                {
                    float space = left, flex = 0;
                    for (int i = 0; i < n; i += 1) { if (fr[i] <= 0) continue; if (flexible[i]) flex += fr[i]; else space -= mins[i]; }
                    float unit = flex > 0 ? Math.Max(0, space) / Math.Max(1, flex) : 0;
                    bool changed = false;
                    for (int i = 0; i < n; i += 1)
                        if (flexible[i] && unit * fr[i] < mins[i]) { flexible[i] = false; changed = true; }
                    if (changed) continue;
                    for (int i = 0; i < n; i += 1) if (fr[i] > 0) w[i] = flexible[i] ? unit * fr[i] : mins[i];
                    break;
                }
            }
            return w;
        }

        /// <summary>max-content width: a text box's unwrapped text, otherwise its last laid-out width.</summary>
        // ---- tables (CSS automatic table layout): a row per <tr>, each column as wide as its widest cell; a
        // table wider than that (width: 100%) shares the rest by those widths; one without a width fits them ----
        // the tables the scan has passed (a query of the whole page for them was most of this pass)
        static readonly HashSet<VisualElement> _tables = new HashSet<VisualElement>();

        static void Tables(VisualElement root)
        {
            _tables.RemoveWhere(t => t.panel == null);
            foreach (var t in _tables)
            {
                if (t.resolvedStyle.display == DisplayStyle.None) continue;
                var rows = t.Children().Where(r => r.userData is DomData d && d.tag == "tr").ToList();
                if (rows.Count == 0) continue;
                int n = rows.Max(r => r.childCount);
                var widest = new float[n];
                foreach (var r in rows)
                {
                    if (r.style.flexDirection != FlexDirection.Row) r.style.flexDirection = FlexDirection.Row;
                    int i = 0;
                    foreach (var cell in r.Children()) widest[i] = Math.Max(widest[i++], MaxContent(cell));
                }
                float sum = widest.Sum();
                bool sized = _states.TryGetValue(t, out var st) && st.width == "set";
                // a table that fills its box but is not laid out yet (just shown): its width reads 0 and its columns came
                // out narrow, then wide again on the next pass - it is sized once it has a width
                if (sized && !(Num(t.contentRect.width) > 1)) continue;
                if (!sized && t.style.alignSelf != Align.FlexStart) t.style.alignSelf = Align.FlexStart;
                float free = sized ? Math.Max(0, Num(t.contentRect.width) - sum) : 0;
                foreach (var r in rows)
                {
                    int i = 0;
                    foreach (var cell in r.Children())
                    {
                        // a short row's last cell spans the columns left
                        int to = i == r.childCount - 1 ? n : i + 1;
                        float w = 0;
                        for (int k = i; k < to; k += 1) w += widest[k] + (sum > 0 ? free * widest[k] / sum : free / n);
                        var cw = cell.style.width; Set(ref cw, (float)Math.Floor(w * 100) / 100, v => cell.style.width = v);
                        if (cell.style.flexShrink != 0) cell.style.flexShrink = 0;
                        i += 1;
                    }
                }
            }
        }

        static float MaxContent(VisualElement e)
        {
            if (e is TextElement te && !string.IsNullOrEmpty(te.text) && !e.Children().Any(c => c.userData is DomData))
            {
                var rs = te.resolvedStyle;
                float text = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                float spacing = float.IsNaN(rs.letterSpacing) ? 0 : Math.Max(0, rs.letterSpacing) * te.text.Length;
                float min = rs.minWidth.keyword == StyleKeyword.Undefined && !float.IsNaN(rs.minWidth.value) ? rs.minWidth.value : 0;
                return (float)Math.Ceiling(Math.Max(min, text + 1 + spacing + rs.paddingLeft + rs.paddingRight + rs.borderLeftWidth + rs.borderRightWidth));
            }
            // a box sized by the page, or a picture: its own width; a container: its content's, side by side in a
            // row, the widest in a column (margins count: gaps are margins here)
            bool sized = _states.TryGetValue(e, out var st) && st.width == "set";
            var kids = e.Children().Where(c => c.resolvedStyle.display != DisplayStyle.None && c.resolvedStyle.position != Position.Absolute).ToList();
            if (sized || kids.Count == 0 || e is SvgEl || e is CanvasEl || e is Image || e is DomInput || e is DomSelect) return Num(e.layout.width);
            var es = e.resolvedStyle;
            bool row = es.flexDirection == FlexDirection.Row || es.flexDirection == FlexDirection.RowReverse;
            float content = 0;
            foreach (var c in kids)
            {
                float w = MaxContent(c) + Num(c.resolvedStyle.marginLeft) + Num(c.resolvedStyle.marginRight);
                content = row ? content + w : Math.Max(content, w);
            }
            return (float)Math.Ceiling(content + Num(es.paddingLeft) + Num(es.paddingRight) + Num(es.borderLeftWidth) + Num(es.borderRightWidth));
        }

        static List<string> Expand(List<string> tokens, float width, float gap)
        {
            var result = new List<string>();
            foreach (var t in tokens)
            {
                if (!t.StartsWith("repeat(")) { result.Add(t); continue; }
                string inner = t.Substring(7, t.Length - 8);
                int comma = inner.IndexOf(',');
                string count = inner.Substring(0, comma).Trim();
                var body = Split(inner.Substring(comma + 1));
                int times;
                if (count == "auto-fill" || count == "auto-fit")
                {
                    // as many minimum-width tracks as fit
                    string first = body[0].StartsWith("minmax(") ? Split(body[0].Substring(7, body[0].Length - 8).Replace(",", " "))[0] : body[0];
                    float min = first.EndsWith("px") ? Float(first.Substring(0, first.Length - 2)) : 100;
                    times = Math.Max(1, (int)Math.Floor((width + gap) / (min * body.Count + gap)));
                }
                else times = int.Parse(count, CultureInfo.InvariantCulture);
                for (int k = 0; k < times; k += 1) result.AddRange(body);
            }
            return result;
        }

        /// <summary>Splits on top-level whitespace ("minmax(70px, 1fr) 1fr" -> two tokens).</summary>
        static List<string> Split(string v)
        {
            var list = new List<string>();
            int depth = 0, from = 0;
            v = v.Trim();
            for (int i = 0; i <= v.Length; i += 1)
            {
                char ch = i < v.Length ? v[i] : ' ';
                if (ch == '(') depth += 1;
                else if (ch == ')') depth -= 1;
                else if (char.IsWhiteSpace(ch) && depth == 0)
                {
                    if (i > from) list.Add(v.Substring(from, i - from).Replace(", ", ","));
                    from = i + 1;
                }
            }
            return list;
        }

        static float Float(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }
}
