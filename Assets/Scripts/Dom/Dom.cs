// A small DOM over UI Toolkit, so the browser build's page code (src/main.mjs, src/platform/*.mjs)
// can be transliterated line for line: document.querySelector, createElement, textContent, hidden,
// classList, addEventListener, dataset, replaceChildren... map onto VisualElements here.
// C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>What a DOM node carries that a VisualElement does not: tag, attributes, dataset, handlers.</summary>
    public sealed class DomData
    {
        public string tag = "div";
        public string id;
        public readonly Dictionary<string, string> attributes = new Dictionary<string, string>();
        public readonly Dictionary<string, string> dataset = new Dictionary<string, string>();
        public readonly Dictionary<string, List<Action<DomEvent>>> handlers = new Dictionary<string, List<Action<DomEvent>>>();
        public bool draggable;
        public bool hidden;
        public DisplayStyle shownDisplay = DisplayStyle.Flex;
        public string value = "";
        public bool isChecked;
        public object extra;   // per-element state for a port (the JS expando properties)
    }

    public static class Dom
    {
        /// <summary>document: the page root (the UIDocument's root).</summary>
        public static VisualElement document;
        static readonly Dictionary<string, VisualElement> ById = new Dictionary<string, VisualElement>();

        public static DomData Data(VisualElement e)
        {
            if (e.userData is DomData d) return d;
            d = new DomData();
            e.userData = d;
            return d;
        }

        /// <summary>document.createElement(tag) with an optional class name and text (the pages' `el` helper).</summary>
        public static VisualElement El(string tag, string className = null, string text = null) => El(tag, className, text, false);

        /// <summary>container: a text tag (label, p, span...) that holds elements, not only text.</summary>
        public static VisualElement El(string tag, string className, string text, bool container)
        {
            VisualElement e;
            switch (container ? "div" : tag)
            {
                case "button":
                    e = new Button { focusable = false };
                    break;
                case "span": case "p": case "b": case "i": case "h1": case "h2": case "h3": case "h4": case "h5": case "kbd":
                case "strong": case "em": case "small": case "label": case "li": case "td": case "th": case "summary": case "a": case "code":
                    e = new Label();
                    break;
                case "canvas":
                    e = new CanvasEl();
                    break;
                case "img":
                    e = new Image { scaleMode = ScaleMode.ScaleToFit };
                    break;
                case "input":
                    e = new DomInput();
                    break;
                case "select":
                    e = new DomSelect();
                    break;
                case "textarea":
                    e = new TextField { multiline = true };
                    break;
                case "svg":
                    e = new SvgEl();
                    break;
                default:
                    e = new VisualElement();
                    break;
            }
            var d = Data(e);
            d.tag = tag;
            e.AddToClassList("tag-" + tag);
            if (!string.IsNullOrEmpty(className)) foreach (var c in className.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            if (text != null) e.SetText(text);
            if (e is TextElement te) te.enableRichText = false;
            if (container) DomExt.MakeInlineContainer(e);
            return e;
        }

        public static void Register(string id, VisualElement e)
        {
            Data(e).id = id;
            e.name = id;
            ById[id] = e;
        }

        /// <summary>document.querySelector: "#id", ".class", "tag", "tag.class", "#id .class", ".a .b", "[data-x]", "a > b".</summary>
        public static VisualElement Q(string selector) => Q(document, selector);

        public static VisualElement Q(VisualElement root, string selector)
        {
            if (root == null) return null;
            if (root == document && selector.StartsWith("#") && selector.IndexOfAny(new[] { ' ', '.', '[', '>' , ':' }) < 0)
                return ById.TryGetValue(selector.Substring(1), out var byId) && byId.panel != null ? byId : (ById.TryGetValue(selector.Substring(1), out byId) ? byId : null);
            return QAll(root, selector).FirstOrDefault();
        }

        public static List<VisualElement> QAll(string selector) => QAll(document, selector);

        public static List<VisualElement> QAll(VisualElement root, string selector)
        {
            var result = new List<VisualElement>();
            if (root == null) return result;
            foreach (var alternative in selector.Split(','))
            {
                var parts = alternative.Trim().Replace(">", " > ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var e in Descendants(root))
                    if (!result.Contains(e) && MatchesChain(e, parts, parts.Length - 1, root)) result.Add(e);
            }
            return result;
        }

        static IEnumerable<VisualElement> Descendants(VisualElement root)
        {
            foreach (var c in root.Children())
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        }

        /// <summary>Does the element match a full selector ("a b", "a > b") within the document?</summary>
        public static bool MatchesSelector(VisualElement e, string selector)
        {
            // parsed once per selector: the stylesheet tables ask the same few hundred selectors all the time
            if (!ParsedSelectors.TryGetValue(selector, out var parts))
            {
                parts = selector.Trim().Replace(">", " > ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (ParsedSelectors.Count > 5000) ParsedSelectors.Clear();
                ParsedSelectors[selector] = parts;
            }
            return parts.Length > 0 && MatchesChain(e, parts, parts.Length - 1, document ?? e);
        }

        static readonly Dictionary<string, string[]> ParsedSelectors = new Dictionary<string, string[]>();

        /// <summary>text-transform: uppercase (CssInfo.Uppercase), which USS cannot do.</summary>
        static RuleIndex<bool> _upperIdx;

        public static bool IsUppercase(VisualElement e)
        {
            _upperIdx = _upperIdx ?? new RuleIndex<bool>(CssInfo.Uppercase.Select(sel => (sel, true)));
            for (var p = e; p != null; p = p.parent)
                if (_upperIdx.Any(p)) return true;
            return false;
        }

        /// <summary>Re-applies upper-casing to every text under root (after a subtree is attached).</summary>
        public static void ApplyUppercase(VisualElement root)
        {
            foreach (var e in Descendants(root).Prepend(root))
                if (e is TextElement te && te.text.Length > 0 && !te.ClassListContains("inline-run") && !(te.parent is TextField) && !(te.parent?.parent is TextField)) ShowText(te, RawText(te));
        }

        /// <summary>The text as the page set it (textContent): what the element shows may be upper-cased or
        /// carry ::before / ::after content.</summary>
        sealed class Shown { public string raw, shown; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, Shown> Raw = new System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, Shown>();

        /// <summary>::before / ::after content of a text element, as rich-text runs (Dom/CssLayout.cs).</summary>
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, string[]> Decorations = new System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, string[]>();

        /// <summary>Lines broken between words by the host (CssLayout) where a word is wider than the box: the
        /// raw text they were made for, and the text with its line breaks.</summary>
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, string[]> WordBreaks = new System.Runtime.CompilerServices.ConditionalWeakTable<TextElement, string[]>();

        public static string RawText(TextElement te) => Raw.TryGetValue(te, out var r) && te.text == r.shown ? r.raw : te.text;

        // text set while detached: whether an ancestor upper-cases it is known only once it is attached
        static readonly HashSet<TextElement> _upperPending = new HashSet<TextElement>();

        /// <summary>Re-applies upper-casing to the text set while detached that is attached now.</summary>
        public static void ApplyUppercasePending()
        {
            if (_upperPending.Count == 0) return;
            foreach (var te in _upperPending.Where(t => t.panel != null).ToList())
            {
                _upperPending.Remove(te);
                if (te.text.Length > 0 && !te.ClassListContains("inline-run") && !(te.parent is TextField) && !(te.parent?.parent is TextField)) ShowText(te, RawText(te));
            }
            if (_upperPending.Count > 20000) _upperPending.Clear();   // ponytail: never attached; dropped wholesale
        }

        public static void ShowText(TextElement te, string text)
        {
            if (te.panel == null) _upperPending.Add(te);
            string shown = WordBreaks.TryGetValue(te, out var wb) && wb[0] == text ? wb[1] : text;
            shown = te.panel != null && IsUppercase(te) ? shown.ToUpperInvariant() : shown;
            if (Decorations.TryGetValue(te, out var deco) && (deco[0].Length > 0 || deco[1].Length > 0))
            {
                te.enableRichText = true;
                shown = deco[0] + "<noparse>" + shown + "</noparse>" + deco[1];
            }
            else if (te.enableRichText) te.enableRichText = false;
            Raw.Remove(te);
            Raw.Add(te, new Shown { raw = text, shown = shown });
            if (te.text != shown) te.text = shown;
            // a part of a merged inline text run: the run is redrawn
            if (te.parent != null && te.parent.ClassListContains("inline-flow")) CssLayout.Touch(te);
        }

        /// <summary>How event handlers run: the host routes them through the engine's event loop.</summary>
        public static Action<Action> Invoke = a => a();

        static bool MatchesChain(VisualElement e, string[] parts, int i, VisualElement root)
        {
            if (!Matches(e, parts[i])) return false;
            if (i == 0) return true;
            if (parts[i - 1] == ">")
            {
                var p = e.parent;
                return p != null && p != root.parent && i >= 2 && MatchesChain(p, parts, i - 2, root);
            }
            for (var p = e.parent; p != null && p != root.parent; p = p.parent)
                if (MatchesChain(p, parts, i - 1, root)) return true;
            return false;
        }

        /// <summary>One compound selector: tag, #id, .class..., [attr], [attr=value], :not([hidden]), :checked.</summary>
        public static bool Matches(VisualElement e, string compound)
        {
            var c = Compound.Of(compound);
            var d = e.userData as DomData;
            if (c.tag != null && (d == null || d.tag != c.tag)) return false;
            if (c.id != null && (d == null || d.id != c.id)) return false;
            foreach (var n in c.classes) if (!e.ClassListContains(n)) return false;
            foreach (var body in c.attrs) if (!AttrMatches(e, d, body)) return false;
            foreach (var inner in c.nots) if (Matches(e, inner)) return false;
            foreach (var pseudo in c.pseudos)
            {
                if (pseudo == "checked" && !(d != null && d.isChecked)) return false;
                if (pseudo == "disabled" && e.enabledSelf) return false;
                if (pseudo == "enabled" && !e.enabledSelf) return false;
                if (pseudo == "hover" && !e.ClassListContains("is-hover")) return false;   // set by CssCursor
                if (pseudo == "focus" && e.focusController?.focusedElement != e) return false;
                if (pseudo == "active") return false;
            }
            return true;
        }

        /// <summary>A compound selector taken apart once (the tables match the same ones over and over).</summary>
        sealed class Compound
        {
            public string tag, id;
            public string[] classes, attrs, nots, pseudos;
            static readonly Dictionary<string, Compound> Cache = new Dictionary<string, Compound>();

            public static Compound Of(string compound)
            {
                if (Cache.TryGetValue(compound, out var c)) return c;
                c = new Compound();
                var classes = new List<string>(); var attrs = new List<string>(); var nots = new List<string>(); var pseudos = new List<string>();
                int i = 0, start = 0;
                while (i < compound.Length && (char.IsLetterOrDigit(compound[i]) || compound[i] == '-' || compound[i] == '*')) i++;
                if (i > start) { string tag = compound.Substring(start, i - start); if (tag != "*") c.tag = tag; }
                while (i < compound.Length)
                {
                    char ch = compound[i];
                    if (ch == '#' || ch == '.')
                    {
                        int s = ++i;
                        while (i < compound.Length && (char.IsLetterOrDigit(compound[i]) || compound[i] == '-' || compound[i] == '_')) i++;
                        string n = compound.Substring(s, i - s);
                        if (ch == '#') c.id = n; else classes.Add(n);
                    }
                    else if (ch == '[')
                    {
                        int end = compound.IndexOf(']', i);
                        attrs.Add(compound.Substring(i + 1, end - i - 1));
                        i = end + 1;
                    }
                    else if (ch == ':')
                    {
                        if (compound.Substring(i).StartsWith(":not("))
                        {
                            int end = compound.IndexOf(')', i);
                            nots.Add(compound.Substring(i + 5, end - i - 5));
                            i = end + 1;
                        }
                        else
                        {
                            int s = ++i;
                            while (i < compound.Length && (char.IsLetterOrDigit(compound[i]) || compound[i] == '-')) i++;
                            pseudos.Add(compound.Substring(s, i - s));
                        }
                    }
                    else i++;
                }
                c.classes = classes.ToArray(); c.attrs = attrs.ToArray(); c.nots = nots.ToArray(); c.pseudos = pseudos.ToArray();
                if (Cache.Count > 5000) Cache.Clear();
                Cache[compound] = c;
                return c;
            }
        }

        static bool AttrMatches(VisualElement e, DomData d, string body)
        {
            string name = body, want = null;
            int eq = body.IndexOf('=');
            if (eq >= 0) { name = body.Substring(0, eq); want = body.Substring(eq + 1).Trim('"', '\''); }
            string have = null;
            if (name == "hidden") have = d != null && d.hidden ? "" : null;
            else if (name.StartsWith("data-") && d != null) have = d.dataset.TryGetValue(DataKey(name), out var v) ? v : null;
            else if (d != null && d.attributes.TryGetValue(name, out var a)) have = a;
            if (have == null) return false;
            return want == null || have == want;
        }

        /// <summary>data-foo-bar -> fooBar (dataset naming).</summary>
        public static string DataKey(string attr)
        {
            var s = attr.Substring(5);
            var parts = s.Split('-');
            return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : p));
        }

        /// <summary>An `<svg><use href="#i-name"/></svg>` icon: the symbol drawn in the element's color.</summary>
        public static VisualElement Icon(string symbol)
        {
            var svg = new SvgEl();
            Data(svg).tag = "svg";
            svg.AddToClassList("tag-svg");
            svg.UseSymbol(symbol);
            return svg;
        }
    }
}
