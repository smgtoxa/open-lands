// A per-selector table (CssInfo.Fonts, FontSizes, ...) looked up per element: the selectors are bucketed by
// the class / id their last compound needs (CssLayout.KeyOf), so an element is matched only against the
// rules that could apply to it - not every rule, and not a document-wide query per rule. A rule whose
// ancestors need classes the element's ancestors do not have is passed over without walking up for it.
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class RuleIndex<T>
    {
        readonly Dictionary<string, List<(int order, string sel, T value, string[] above)>> _byKey = new Dictionary<string, List<(int, string, T, string[])>>();
        static readonly HashSet<string> Above = new HashSet<string>();
        static VisualElement _aboveOf;
        static int _aboveFrame = -1;

        public RuleIndex(IEnumerable<(string sel, T value)> rules)
        {
            int order = 0;
            foreach (var (sel, value) in rules)
            {
                string key = CssLayout.KeyOf(sel);
                if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = new List<(int, string, T, string[])>();
                list.Add((order++, sel, value, AboveClasses(sel)));
            }
        }

        /// <summary>The value of the last rule (in cascade order) that matches e.</summary>
        public bool Last(VisualElement e, out T value)
        {
            value = default;
            int best = -1;
            foreach (var c in e.GetClasses()) Check(e, "." + c, ref best, ref value);
            if (e.userData is DomData d && d.id != null) Check(e, "#" + d.id, ref best, ref value);
            Check(e, "", ref best, ref value);
            return best >= 0;
        }

        public bool Any(VisualElement e) => Last(e, out _);

        void Check(VisualElement e, string key, ref int best, ref T value)
        {
            if (!_byKey.TryGetValue(key, out var list)) return;
            foreach (var (order, sel, v, above) in list)
            {
                if (order <= best) continue;
                if (above.Length > 0 && !HasAbove(e, above)) continue;
                if (Dom.MatchesSelector(e, sel)) { best = order; value = v; }
            }
        }

        // the classes the compounds before the last one require (a selector's ancestors): ".a .b > .c" -> a, b
        static string[] AboveClasses(string sel)
        {
            var parts = sel.Replace(">", " ").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var need = new List<string>();
            for (int i = 0; i < parts.Length - 1; i += 1)
            {
                string p = parts[i].Split(':', '[')[0];
                foreach (var cls in p.Split('.').Skip(1)) if (cls.Length > 0) need.Add(cls);
            }
            return need.ToArray();
        }

        // the ancestors' classes, collected once per element (per frame) however many rules ask
        static bool HasAbove(VisualElement e, string[] need)
        {
            if (_aboveOf != e || _aboveFrame != UnityEngine.Time.frameCount)
            {
                Above.Clear();
                for (var p = e.parent; p != null; p = p.parent) foreach (var c in p.GetClasses()) Above.Add(c);
                _aboveOf = e; _aboveFrame = UnityEngine.Time.frameCount;
            }
            foreach (var n in need) if (!Above.Contains(n)) return false;
            return true;
        }
    }
}
