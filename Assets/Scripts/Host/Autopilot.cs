// Test driver from the command line (play.sh): --do "sec:action,..." --quit-after sec
//   key:NAME       KeyboardEvent.key to the page's keydown handler ("Enter", " ", "ArrowUp", "i")
//   click:SELECTOR a click on the first element the selector matches ("#new-game", ".save-slot")
//   shot:PATH      a screenshot of the window
//   realkey:NAME   a real key press through UI Toolkit (Event.KeyboardEvent names: up, return, f5, i)
//   clickat:X:Y    a mouse click at a window position (rclickat: right button)
//   cursorat:X:Y   logs the cursor the page shows at a window position
//   tp:L:B:D       debugTeleport(level, block, direction) on the engine's queue
//   inspect:SEL    logs the inline and resolved box styles of the matches
//   record:PATH:SEC the mixed audio output to a WAV
//   dump:PATH      every element's box, the same walk as tools/layout_dump.js (tools/layout_diff.sh)
//   watch:SEL:SEC  for SEC seconds, logs every box under the first match that moves or resizes between frames
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class Autopilot : MonoBehaviour
    {
        readonly List<(float at, string action)> _steps = new List<(float, string)>();
        float _quitAt = -1;
        public Web web;

        public static void Attach(GameObject go, Web web)
        {
            var args = Environment.GetCommandLineArgs();
            int d = Array.IndexOf(args, "--do"), q = Array.IndexOf(args, "--quit-after");
            if (d < 0 && q < 0) return;
            var a = go.AddComponent<Autopilot>();
            a.web = web;
            if (d >= 0 && d + 1 < args.Length)
                foreach (var step in args[d + 1].Split(','))
                {
                    int c = step.IndexOf(':');
                    if (c > 0 && float.TryParse(step.Substring(0, c), NumberStyles.Float, CultureInfo.InvariantCulture, out var at)) a._steps.Add((at, step.Substring(c + 1)));
                    else Debug.Log("autopilot: bad step '" + step + "'");
                }
            if (q >= 0 && q + 1 < args.Length) a._quitAt = float.Parse(args[q + 1], CultureInfo.InvariantCulture);
        }

        // what moves under an element, frame by frame (a box that keeps changing is a layout fight)
        System.Collections.IEnumerator Watch(string sel, float seconds)
        {
            var root = Dom.Q(sel);
            if (root == null) { Debug.Log("autopilot watch: no element " + sel); yield break; }
            var last = new Dictionary<VisualElement, Rect>();
            var changes = new Dictionary<string, int>();
            var rebuilt = new Dictionary<string, int>();
            string Path(VisualElement e) { var parts = new List<string>(); for (var p = e; p != null && p != root.parent; p = p.parent) parts.Insert(0, (p.userData is DomData d ? d.tag : p.GetType().Name) + (p.GetClasses().Any() ? "." + string.Join(".", p.GetClasses().Take(2)) : "") + $"[{(p.parent != null ? p.parent.IndexOf(p) : 0)}]"); return string.Join(">", parts); }
            int frames = 0;
            for (float end = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < end; frames += 1)
            {
                foreach (var e in Enumerable.Prepend(root.Query<VisualElement>().ToList(), root))
                {
                    var r = e.worldBound;
                    // an element first seen after the first frame: its parent was (re)built while watching
                    if (frames > 0 && !last.ContainsKey(e) && e.parent != null && last.ContainsKey(e.parent))
                    {
                        string k = "NEW under " + Path(e.parent);
                        rebuilt[k] = (rebuilt.TryGetValue(k, out var n2) ? n2 : 0) + 1;
                    }
                    if (last.TryGetValue(e, out var was) && (Mathf.Abs(was.x - r.x) > 0.01f || Mathf.Abs(was.y - r.y) > 0.01f || Mathf.Abs(was.width - r.width) > 0.01f || Mathf.Abs(was.height - r.height) > 0.01f))
                    {
                        string k = Path(e);
                        changes[k] = (changes.TryGetValue(k, out var n) ? n : 0) + 1;
                        if (changes[k] <= 3) Debug.Log($"autopilot watch f{frames} {k}: {was.x:0.#},{was.y:0.#} {was.width:0.#}x{was.height:0.#} -> {r.x:0.#},{r.y:0.#} {r.width:0.#}x{r.height:0.#} style w={e.style.width} h={e.style.height} mt={e.style.marginTop} mb={e.style.marginBottom} ml={e.style.marginLeft} mr={e.style.marginRight} pt={e.style.paddingTop} minW={e.style.minWidth} shrink={e.style.flexShrink}");
                    }
                    last[e] = r;
                }
                yield return null;
            }
            Debug.Log($"autopilot watch done: {frames} frames, {changes.Count} boxes changed");
            foreach (var kv in changes.OrderByDescending(kv => kv.Value).Take(20)) Debug.Log($"autopilot watch {kv.Value}x {kv.Key}");
            foreach (var kv in rebuilt.OrderByDescending(kv => kv.Value).Take(20)) Debug.Log($"autopilot watch rebuilt {kv.Value}x {kv.Key}");
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup;
            for (int i = 0; i < _steps.Count; i += 1)
            {
                if (_steps[i].at > t) continue;
                var (at, action) = _steps[i];
                _steps.RemoveAt(i--);
                // a click waits for its element, as Playwright's click does (the web runs use 500ms): up to 1s
                if (action.StartsWith("click:") && !Clickable(action.Substring(6)) && t < at + 1f)
                {
                    if (!_waiting.Contains((at, action))) { _waiting.Add((at, action)); Debug.Log("autopilot waiting " + action); }
                    _pending.Add((at, action));
                    continue;
                }
                Debug.Log($"autopilot {action} (frame {Time.frameCount}, {t:0.00}s)");
                Run(action);
            }
            _steps.AddRange(_pending);   // retried next frame
            _pending.Clear();
            if (_quitAt >= 0 && t >= _quitAt) Application.Quit();
        }

        readonly List<(float, string)> _pending = new List<(float, string)>();
        readonly HashSet<(float, string)> _waiting = new HashSet<(float, string)>();

        static bool Clickable(string sel)
        {
            var el = Dom.Q(sel);
            if (el == null || el.panel == null || el.resolvedStyle.visibility != Visibility.Visible) return false;
            for (var e = el; e != null; e = e.parent) if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return el.worldBound.width > 0 && el.worldBound.height > 0;
        }

        void Run(string action)
        {
            int c = action.IndexOf(':');
            string kind = c < 0 ? action : action.Substring(0, c), arg = c < 0 ? "" : action.Substring(c + 1);
            if (kind == "key") Dom.Invoke(() =>
            {
                var ev = new DomEvent { type = "keydown", key = arg == "Space" ? " " : arg };
                web.documentKeydown(ev);
                Debug.Log($"autopilot key {arg}: block={Engine()?.currentBlock}");
            });
            else if (kind == "click")
            {
                var el = Dom.Q(arg);
                if (el == null) Debug.Log("autopilot: no element " + arg);
                else el.Fire("click", new DomEvent { type = "click", target = el, currentTarget = el });
            }
            else if (kind == "clicktext")   // clicktext:SELECTOR:TEXT - clicks the first match whose text is TEXT (a tab by name)
            {
                int c2 = arg.LastIndexOf(':');
                string sel = arg.Substring(0, c2), text = arg.Substring(c2 + 1);
                var el = Dom.QAll(sel).FirstOrDefault(e => string.Equals(e.GetText()?.Trim(), text, StringComparison.OrdinalIgnoreCase));
                if (el == null) Debug.Log($"autopilot: no {sel} reading {text}");
                else el.Fire("click", new DomEvent { type = "click", target = el, currentTarget = el });
            }
            else if (kind == "realkey")
            {
                // a real key press through UI Toolkit, to the focused element (Event.KeyboardEvent names: up, return, f5, i, [8])
                var evt = Event.KeyboardEvent(arg);
                var target = Dom.document.panel.focusController.focusedElement as VisualElement ?? Dom.document;
                using (var down = KeyDownEvent.GetPooled('\0', evt.keyCode, evt.modifiers)) { down.target = target; target.SendEvent(down); }
                if (evt.keyCode >= KeyCode.A && evt.keyCode <= KeyCode.Z)
                    using (var ch = KeyDownEvent.GetPooled(arg[0], KeyCode.None, evt.modifiers)) { ch.target = target; target.SendEvent(ch); }
                Debug.Log($"autopilot realkey {evt.keyCode} -> {target}");
            }
            else if (kind == "clickat" || kind == "rclickat")
            {
                // a real mouse press and release at window position X:Y (UI Toolkit picks the target)
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var pos = new Vector2(p[0], p[1]);
                int button = kind == "rclickat" ? 1 : 0;
                var panel = Dom.document.panel;
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = pos })) panel.visualTree.SendEvent(move);
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = pos, button = button, clickCount = 1 })) panel.visualTree.SendEvent(down);
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = pos, button = button, clickCount = 1 })) panel.visualTree.SendEvent(up);
            }
            else if (kind == "wheelat")   // wheelat:X:Y:DELTA - a real wheel turn over window position X:Y
            {
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var panel = Dom.document.panel;
                var pos = new Vector2(p[0], p[1]);
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = pos })) panel.visualTree.SendEvent(move);
                using (var w = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = pos, delta = new Vector2(0, p[2]) })) panel.visualTree.SendEvent(w);
                Debug.Log($"autopilot wheel over {panel.Pick(pos)?.GetClasses().FirstOrDefault()}");
            }
            else if (kind == "pressat")   // pressat:X:Y - a real mouse press only (a release comes with releaseat)
            {
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var pos = new Vector2(p[0], p[1]);
                var panel = Dom.document.panel;
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = pos })) panel.visualTree.SendEvent(move);
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = pos, button = 0, clickCount = 1 })) panel.visualTree.SendEvent(down);
            }
            else if (kind == "releaseat")
            {
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var pos = new Vector2(p[0], p[1]);
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = pos, button = 0, clickCount = 1 })) Dom.document.panel.visualTree.SendEvent(up);
            }
            else if (kind == "openselect")   // openselect:SELECTOR - opens a <select>'s menu as Enter on it would
            {
                var el = Dom.QAll(arg).FirstOrDefault();
                var field = el?.Q<DropdownField>();
                if (field == null) Debug.Log("autopilot openselect: no dropdown at " + arg);
                else (el as DomSelect ?? el.GetFirstAncestorOfType<DomSelect>() ?? el.Q<DomSelect>())?.OpenMenu();
            }
            else if (kind == "hoverat")   // hoverat:X:Y - the cursor and tooltips act as if the mouse were there
            {
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                CssCursor.ForcedMouse = new Vector2(p[0], Screen.height - p[1]);
            }
                        else if (kind == "cursorat")
            {
                var p = arg.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var picked = Dom.document.panel.Pick(new Vector2(p[0], p[1]));
                var shown = CssCursor.CursorFor(picked);
                Debug.Log($"autopilot cursor at {arg}: {(shown is string s ? s : "picture")} over {(picked?.userData is DomData d ? d.tag + "#" + d.id + " ." + string.Join(".", picked.GetClasses()) : picked?.ToString())}");
            }
            else if (kind == "tp")
            {
                var p = arg.Split(':').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                Dom.Invoke(() => Engine()?.queueAsync(() => Engine().debugTeleport(p[0], p.Length > 1 ? p[1] : (int?)null, p.Length > 2 ? p[2] : 0)));
            }
            else if (kind == "inspect")
            {
                foreach (var el in Dom.QAll(arg))
                {
                    var st = el.style; var rs = el.resolvedStyle;
                    var wb = el.worldBound;
                    Debug.Log($"inspect {arg} world=({wb.x:0},{wb.y:0},{wb.width:0},{wb.height:0}) [{string.Join(" ", el.GetClasses())}] text='{(el as TextElement)?.text}' font={rs.fontSize} minW={rs.minWidth} ls={rs.letterSpacing} display={rs.display} hidden={el.IsHidden()} layout={el.layout} inline h={st.height} minH={st.minHeight} w={st.width} grow={st.flexGrow} basis={st.flexBasis} mt={st.marginTop} mb={st.marginBottom} pt={st.paddingTop} | resolved h={rs.height} minH={rs.minHeight} dir={rs.flexDirection} wrap={rs.flexWrap} alignC={rs.alignContent} alignI={rs.alignItems} basis={rs.flexBasis} grow={rs.flexGrow} shrink={rs.flexShrink}");
                }
            }
            else if (kind == "scroll")   // scroll:SELECTOR:TOP - el.scrollTop = TOP on the first match
            {
                int c2 = arg.LastIndexOf(':');
                var el = Dom.QAll(arg.Substring(0, c2)).FirstOrDefault();
                if (el != null) Dom.Invoke(() => CssLayout.SetScrollTop(el, float.Parse(arg.Substring(c2 + 1), CultureInfo.InvariantCulture)));
            }
            else if (kind == "watch")
            {
                int c2 = arg.LastIndexOf(':');
                StartCoroutine(Watch(arg.Substring(0, c2), float.Parse(arg.Substring(c2 + 1), CultureInfo.InvariantCulture)));
            }
            else if (kind == "record")
            {
                int c2 = arg.LastIndexOf(':');
                FindObjectOfType<WebAudio>().Record(arg.Substring(0, c2), float.Parse(arg.Substring(c2 + 1), CultureInfo.InvariantCulture));
            }
            else if (kind == "shot") ScreenCapture.CaptureScreenshot(arg);
            else if (kind == "dump") File.WriteAllText(arg, Dump());
        }

        /// <summary>The page's engine (a private field of the page, as window.lolEngine is a debug handle there).</summary>
        Lol.LandsOfLore Engine() => (Lol.LandsOfLore)typeof(Web).GetField("engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(web);

        static bool IsElement(VisualElement e) => e.userData is DomData && !e.ClassListContains("text");

        static string Name(VisualElement e) => Dom.Data(e).tag + string.Concat(e.GetClasses().Where(c => c != "svg" && !c.StartsWith("svg-") && !c.StartsWith("unity-") && !c.StartsWith("tag-") && c != "root" && c != "inline-flow" && c != "is-hover" && !c.StartsWith("input-type-")).OrderBy(c => c, StringComparer.Ordinal).Select(c => "." + c));

        static string Dump()
        {
            var sb = new StringBuilder();
            void walk(VisualElement el, string key)
            {
                // display:none hides the subtree from getBoundingClientRect too
                if (el.resolvedStyle.display == DisplayStyle.None) return;
                var r = el.worldBound;
                if (r.width > 0 || r.height > 0) sb.Append(key).Append(' ').Append(Mathf.RoundToInt(r.x)).Append(' ').Append(Mathf.RoundToInt(r.y)).Append(' ').Append(Mathf.RoundToInt(r.width)).Append(' ').Append(Mathf.RoundToInt(r.height)).Append('\n');
                if (el is SvgEl || el is DomSelect || el is DomInput) return;
                int i = 0;
                foreach (var c in el.Children())
                {
                    if (!IsElement(c)) continue;
                    string id = Dom.Data(c).id;
                    walk(c, !string.IsNullOrEmpty(id) ? "#" + id : $"{key}>{Name(c)}[{i}]");
                    i += 1;
                }
            }
            walk(Dom.document, "body");
            return sb.ToString();
        }
    }
}
