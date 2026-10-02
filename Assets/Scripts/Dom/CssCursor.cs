// The mouse cursor, as a browser picks it: the element under the pointer and its ancestors - an inline
// style.cursor (the page's item-in-hand picture) first, then the stylesheet (CssInfo.Cursors, inherited).
// pointer / help / crosshair / text use the Windows system cursors (Resources/Cursors); none hides it.
// It also keeps .is-hover on the hovered element and its ancestors, which :hover rules the host
// emulates (box-shadow, svg paint) match.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class CssCursor
    {
        static readonly Dictionary<VisualElement, string> Inline = new Dictionary<VisualElement, string>();
        static readonly Dictionary<VisualElement, (Texture2D tex, Vector2 hot)> InlinePicture = new Dictionary<VisualElement, (Texture2D, Vector2)>();
        static readonly Dictionary<VisualElement, string> RuleOf = new Dictionary<VisualElement, string>();
        static readonly Dictionary<string, (Texture2D tex, Vector2 hot)> SystemCursors = new Dictionary<string, (Texture2D, Vector2)>();
        static readonly HashSet<VisualElement> Hovered = new HashSet<VisualElement>();
        static object _shown = "";
        /// <summary>test runs (Autopilot hoverat): the mouse there instead of the real one (screen pixels, y up)</summary>
        public static Vector2? ForcedMouse;
        static int _frame;

        /// <summary>el.style.cursor = value ("" clears it).</summary>
        public static void SetInline(VisualElement e, string value)
        {
            if (string.IsNullOrEmpty(value)) Inline.Remove(e); else Inline[e] = value;
            InlinePicture.Remove(e);
        }

        /// <summary>el.style.cursor = url(picture) hot, auto - a picture the page drew (null clears it).</summary>
        public static void SetInlinePicture(VisualElement e, Texture2D tex, Vector2 hot)
        {
            if (tex == null) InlinePicture.Remove(e); else InlinePicture[e] = (tex, hot);
            Inline.Remove(e);
        }

        public static void Tick(VisualElement root)
        {
            if (root?.panel == null) return;
            if (_frame++ % 20 == 0) RuleOf.Clear();   // classes change: the rules are looked up again now and then
            var mouse = ForcedMouse ?? (Vector2)Input.mousePosition;
            var pos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
            var picked = root.panel.Pick(pos);
            Hover(picked);
            Show(CursorFor(picked));
            CssTooltip.Tick(root, picked, pos);
        }

        /// <summary>The cursor over an element: a picture (Texture2D, hot spot) or a CSS keyword.</summary>
        public static object CursorFor(VisualElement picked)
        {
            for (var e = picked; e != null; e = e.parent)
            {
                if (InlinePicture.TryGetValue(e, out var pic)) return pic;
                if (Inline.TryGetValue(e, out var v) && v != "auto") return v;
                string rule = Rule(e);
                if (rule != null && rule != "auto") return rule;
            }
            return "default";
        }

        static string Rule(VisualElement e)
        {
            if (!(e.userData is DomData)) return null;
            if (RuleOf.TryGetValue(e, out var cached)) return cached;
            string value = null;
            foreach (var (sel, v) in CssInfo.Cursors) if (Dom.MatchesSelector(e, sel)) value = v;
            RuleOf[e] = value;
            return value;
        }

        static void Show(object want)
        {
            if (Equals(want, _shown)) return;
            _shown = want;
            UnityEngine.Cursor.visible = !(want is string n && n == "none");
            if (want is System.ValueTuple<Texture2D, Vector2> pic) { UnityEngine.Cursor.SetCursor(pic.Item1, pic.Item2, CursorMode.Auto); return; }
            string name = want as string;
            // the game's own pixel cursors (Resources/Cursors/game-*.png, tools/gen_hud_art.py): a steel arrow, a gold
            // one over what can be clicked, a sword over a monster in reach, a gauntlet over what can be taken
            string game = name == "default" || name == "auto" ? "game-default" : name == "pointer" ? "game-pointer"
                : name == "attack" ? "game-attack" : name == "grab" ? "game-grab" : name == "crosshair" ? "game-crosshair" : null;
            if (game != null)
            {
                if (!SystemCursors.TryGetValue(game, out var gc))
                {
                    var tex = UnityEngine.Resources.Load<Texture2D>("Cursors/" + game);
                    var hot = game == "game-grab" ? new Vector2(16, 14) : game == "game-attack" ? new Vector2(2, 2) : game == "game-crosshair" ? new Vector2(16, 16) : new Vector2(1, 1);
                    SystemCursors[game] = gc = (tex, hot);
                }
                if (gc.tex != null) { UnityEngine.Cursor.SetCursor(gc.tex, gc.hot, CursorMode.Auto); return; }
            }
            if (name == "pointer" || name == "help" || name == "crosshair" || name == "text")
            {
                if (!SystemCursors.TryGetValue(name, out var sys))
                {
                    var tex = UnityEngine.Resources.Load<Texture2D>("Cursors/" + name);
                    var hot = name == "pointer" ? new Vector2(6, 0) : name == "help" ? Vector2.zero : new Vector2(15, 16);
                    SystemCursors[name] = sys = (tex, hot);
                }
                if (sys.tex != null) { UnityEngine.Cursor.SetCursor(sys.tex, sys.hot, CursorMode.Auto); return; }
            }
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>.is-hover on the hovered element and its ancestors (the :hover chain).</summary>
        static void Hover(VisualElement picked)
        {
            var chain = new HashSet<VisualElement>();
            for (var e = picked; e != null; e = e.parent) if (e.userData is DomData) chain.Add(e);
            // only the :hover rules the host emulates (shadows) are looked at again for these boxes
            foreach (var e in Hovered) if (!chain.Contains(e)) { e.RemoveFromClassList("is-hover"); CssShadow.Match(e); }
            foreach (var e in chain) if (!Hovered.Contains(e)) { e.AddToClassList("is-hover"); CssShadow.Match(e); }
            Hovered.Clear();
            Hovered.UnionWith(chain);
        }
    }
}
