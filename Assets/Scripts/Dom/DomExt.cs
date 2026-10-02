// DOM element API as extension methods on VisualElement (see Dom.cs). Port rule of thumb:
//   el.hidden = x              -> el.SetHidden(x)            el.hidden -> el.IsHidden()
//   el.textContent = s          -> el.SetText(s)              el.textContent -> el.GetText()
//   el.title = s                -> el.SetTitle(s)
//   el.classList.toggle(c, on)  -> el.ClassToggle(c, on)      add/remove/contains: AddToClassList / RemoveFromClassList / ClassListContains
//   el.append(a, b) / replaceChildren(a, b) -> el.Append(a, b) / el.ReplaceChildren(a, b)   (strings become text)
//   el.setAttribute(k, v) / getAttribute / removeAttribute -> SetAttr / GetAttr / RemoveAttr
//   el.dataset.x                -> el.Dataset()["x"]
//   el.disabled = x             -> el.SetDisabled(x)
//   el.style.width = "40%"      -> el.SetStyle("width", "40%")
//   el.addEventListener(t, fn)  -> el.On(t, fn)   (click, contextmenu, mousedown/up/move, pointer*, mouseenter, wheel,
//                                                 input, change, keydown, dragstart/dragover/dragleave/drop, animationend)
//   el.querySelector(s) / querySelectorAll(s) -> el.Q(s) / el.QAll(s)
//   el.remove()                 -> el.RemoveFromHierarchy()
//   el.getBoundingClientRect()  -> el.worldBound
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class DomExt
    {
        public static VisualElement Q(this VisualElement e, string selector) => Dom.Q(e, selector);
        public static List<VisualElement> QAll(this VisualElement e, string selector) => Dom.QAll(e, selector);

        // ---- hidden ----
        public static bool IsHidden(this VisualElement e) => e != null && Dom.Data(e).hidden;

        public static void SetHidden(this VisualElement e, bool hidden)
        {
            if (e == null) return;
            var d = Dom.Data(e);
            bool was = d.hidden || e.ClassListContains("is-hidden");
            d.hidden = hidden;
            e.style.display = hidden ? DisplayStyle.None : StyleKeyword.Null;
            e.EnableInClassList("is-hidden", hidden);
            if (e is SvgNode) SvgEl.Invalidate(e);
            // shown: styled now (fonts, line heights, flow) - a panel coming out of hiding was left to the next scan
            // and settled a few pixels up to half a second later (the camp's panel, the stash, the map's side)
            if (!hidden && was) CssLayout.Touch(e);
        }

        // ---- text ----
        public static void SetText(this VisualElement e, string text)
        {
            if (e == null) return;
            text = text ?? "";
            if (e is TextElement te) { Dom.ShowText(te, text); return; }
            if (e is DomInput di) { di.value = text; return; }
            // the same one text: kept, not made anew - a fresh label lacks the line-height the layout gives it
            // until its next pass, so a line set every tick (a card's "Might 12 Prot 8") jumped by 2px
            if (text.Length > 0 && e.childCount == 1 && e[0] is Label only && only.ClassListContains("text")) { Dom.ShowText(only, text); return; }
            e.Clear();
            if (text.Length > 0)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
                l.AddToClassList("text");
                e.Add(l);
            }
        }

        public static string GetText(this VisualElement e)
        {
            if (e == null) return "";
            if (e is TextElement te) return Dom.RawText(te);
            if (e is DomInput di) return di.value;
            return string.Concat(e.Children().Select(GetText));
        }

        public static void SetTitle(this VisualElement e, string title) { if (e != null) e.tooltip = title ?? ""; }
        public static string GetTitle(this VisualElement e) => e?.tooltip ?? "";

        // ---- classes ----
        public static bool ClassToggle(this VisualElement e, string cls, bool? force = null)
        {
            if (e == null) return false;
            bool on = force ?? !e.ClassListContains(cls);
            e.EnableInClassList(cls, on);
            CssLayout.Touch(e);
            return on;
        }

        public static void SetClassName(this VisualElement e, string classes)
        {
            var d = Dom.Data(e);
            foreach (var c in e.GetClasses().ToList()) if (!c.StartsWith("tag-") && c != "is-hidden") e.RemoveFromClassList(c);
            foreach (var c in (classes ?? "").Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            CssLayout.Touch(e);
        }

        // ---- children ----
        /// <summary>Tags whose content is inline in CSS: once they hold elements, the content flows in a row.</summary>
        static readonly HashSet<string> InlineContent = new HashSet<string> { "p", "li", "span", "label", "a", "b", "i", "em", "strong", "small", "td", "th", "h1", "h2", "h3", "h4", "h5", "summary", "kbd", "button" };

        /// <summary>A text element that gets element children: its own text becomes the first child, and the
        /// children flow in a row (the .inline-flow default, which the page's own display rules override).</summary>
        public static void MakeInlineContainer(VisualElement e)
        {
            if (e.ClassListContains("inline-flow") || !InlineContent.Contains(Dom.Data(e).tag)) return;
            e.AddToClassList("inline-flow");
            if (e is TextElement te && !(e is Button) && te.text.Length > 0)
            {
                var l = new Label(Dom.RawText(te)) { pickingMode = PickingMode.Ignore, enableRichText = false };
                l.AddToClassList("text");
                te.text = "";
                e.Insert(0, l);
            }
        }

        public static VisualElement Append(this VisualElement e, params object[] children)
        {
            foreach (var c in children)
            {
                if (c == null) continue;
                if (c is VisualElement || c is IEnumerable<VisualElement>) MakeInlineContainer(e);
                if (c is VisualElement added) CssLayout.Touch(added);
                if (c is VisualElement v) e.Add(v);
                else if (c is IEnumerable<VisualElement> many) foreach (var m in many.ToList()) { e.Add(m); CssLayout.Touch(m); }
                else
                {
                    var l = new Label(c.ToString()) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    l.AddToClassList("text");
                    e.Add(l);
                }
            }
            return e;
        }

        // ---- scrolling (overflow: auto elements; Dom/CssLayout.cs) ----
        public static float ScrollTop(this VisualElement e) => CssLayout.ScrollTop(e);
        public static void SetScrollTop(this VisualElement e, float v) => CssLayout.SetScrollTop(e, v);
        public static float ScrollHeight(this VisualElement e) => CssLayout.ScrollHeight(e);
        /// <summary>el.scrollTop = el.scrollHeight: the end, wherever it is once the new content is laid out</summary>
        public static void ScrollToBottom(this VisualElement e) => CssLayout.SetScrollTop(e, float.MaxValue);

        public static VisualElement ReplaceChildren(this VisualElement e, params object[] children)
        {
            e.Clear();
            e.Append(children);
            CssLayout.KeepScroll(e);
            return e;
        }

        public static void Prepend(this VisualElement e, VisualElement child) => e.Insert(0, child);

        // ---- attributes ----
        public static void SetAttr(this VisualElement e, string name, string value)
        {
            var d = Dom.Data(e);
            if (name == "title") { e.tooltip = value; return; }
            if (name == "id") { if (e is SvgNode) Dom.Data(e).id = value; else Dom.Register(value, e); return; }   // svg ids (gradients) stay local
            if (name == "src" && e is Image img) { d.attributes["src"] = value; DomImages.SetSrc(img, value ?? ""); return; }
            if (name == "class") { e.SetClassName(value); return; }
            if (name == "hidden") { e.SetHidden(true); return; }
            if (name == "disabled") { e.SetEnabled(false); return; }
            if (name.StartsWith("data-")) { d.dataset[Dom.DataKey(name)] = value; return; }
            d.attributes[name] = value;
            if (e is SvgEl svg) svg.OnAttribute(name, value);
            else if (e is DomInput di) di.OnAttribute(name, value);
            else if (e is CanvasEl ce && (name == "width" || name == "height")) ce.OnAttribute(name, value);
            else if (e.parent is SvgEl || e is SvgNode) SvgEl.Invalidate(e);
        }

        public static string GetAttr(this VisualElement e, string name)
        {
            var d = Dom.Data(e);
            if (name == "title") return e.tooltip;
            if (name.StartsWith("data-")) return d.dataset.TryGetValue(Dom.DataKey(name), out var v) ? v : null;
            return d.attributes.TryGetValue(name, out var a) ? a : null;
        }

        public static void RemoveAttr(this VisualElement e, string name)
        {
            var d = Dom.Data(e);
            if (name == "hidden") { e.SetHidden(false); return; }
            if (name == "disabled") { e.SetEnabled(true); return; }
            if (name.StartsWith("data-")) { d.dataset.Remove(Dom.DataKey(name)); return; }
            d.attributes.Remove(name);
        }

        public static Dictionary<string, string> Dataset(this VisualElement e) => Dom.Data(e).dataset;

        public static void SetDisabled(this VisualElement e, bool disabled) { if (e != null) e.SetEnabled(!disabled); }
        public static bool IsDisabled(this VisualElement e) => e != null && !e.enabledSelf;

        public static void SetDraggable(this VisualElement e, bool draggable) { Dom.Data(e).draggable = draggable; DragDrop.Watch(e); }

        /// <summary>The JS expando properties a page hangs on an element (el.foo = ...).</summary>
        public static T Extra<T>(this VisualElement e) where T : class => Dom.Data(e).extra as T;
        public static void SetExtra(this VisualElement e, object value) => Dom.Data(e).extra = value;

        // ---- style ----
        public static void SetStyle(this VisualElement e, string prop, string value)
        {
            if (e == null) return;
            if (e is SvgNode sn)
            {
                // CSS on an SVG element: the renderer applies transform (around transform-origin) and opacity.
                if (prop == "transform") sn.SetAttr("css-transform", value ?? "");
                else if (prop == "opacity") sn.SetAttr("opacity", string.IsNullOrEmpty(value) ? "1" : value);
                else if (prop == "transformOrigin") sn.SetAttr("transform-origin", value);
                else if (prop == "display") sn.SetHidden(value == "none");
                SvgEl.Invalidate(sn);
                return;
            }
            var s = e.style;
            bool empty = string.IsNullOrEmpty(value);
            switch (prop)
            {
                case "width": s.width = empty ? StyleKeyword.Null : Len(value); break;
                case "height": s.height = empty ? StyleKeyword.Null : Len(value); break;
                case "left": s.left = empty ? StyleKeyword.Null : Len(value); break;
                case "top": s.top = empty ? StyleKeyword.Null : Len(value); break;
                case "right": s.right = empty ? StyleKeyword.Null : Len(value); break;
                case "bottom": s.bottom = empty ? StyleKeyword.Null : Len(value); break;
                case "opacity": s.opacity = empty ? StyleKeyword.Null : float.Parse(value, CultureInfo.InvariantCulture); break;
                case "color": s.color = empty ? StyleKeyword.Null : new StyleColor(Css.Color(value)); break;
                case "background": case "backgroundColor":
                    if (prop == "background") CssLayout.SetInlineBackground(e, empty ? null : value);
                    if (empty) s.backgroundColor = StyleKeyword.Null;
                    else if (value.StartsWith("url(")) { }
                    else s.backgroundColor = new StyleColor(Css.Color(value.Contains("gradient(") ? value : value.Split(' ')[0]));
                    break;
                case "borderColor": s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = empty ? StyleKeyword.Null : new StyleColor(Css.Color(value)); break;
                case "display": s.display = value == "none" ? DisplayStyle.None : empty ? StyleKeyword.Null : DisplayStyle.Flex; break;
                case "transform":
                    if (empty) { s.rotate = StyleKeyword.Null; s.scale = StyleKeyword.Null; s.translate = StyleKeyword.Null; break; }
                    Css.ApplyTransform(e, value);
                    break;
                case "aspectRatio": SetAspectRatio(e, value); break;
                case "cursor": case "filter": case "transition": case "animation": case "pointerEvents":
                    // filter: brightness(v) tints the element's picture (a canvas' pixels); other filters are not drawn
                    if (prop == "filter")
                    {
                        float v = 1;
                        if (!empty && value.StartsWith("brightness(")) float.TryParse(value.Substring(11).TrimEnd(')'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                        s.unityBackgroundImageTintColor = empty || v == 1 ? StyleKeyword.Null : new StyleColor(new Color(v, v, v, 1));
                    }
                    if (prop == "pointerEvents") e.pickingMode = value == "none" ? PickingMode.Ignore : PickingMode.Position;
                    if (prop == "cursor") CssCursor.SetInline(e, empty ? null : value);
                    break;
                default: break;
            }
        }

        /// <summary>
        /// CSS aspect-ratio ("w / h"), which USS lacks: the element's height follows its width.
        /// </summary>
        public static void SetAspectRatio(VisualElement e, string value)
        {
            var d = Dom.Data(e);
            bool first = !d.attributes.ContainsKey("aspect-ratio");
            d.attributes["aspect-ratio"] = value ?? "";
            if (first) e.RegisterCallback<GeometryChangedEvent>(_ => ApplyAspect(e));
            ApplyAspect(e);
        }

        static void ApplyAspect(VisualElement e)
        {
            var parts = (Dom.Data(e).attributes.TryGetValue("aspect-ratio", out var v) ? v : "").Split('/');
            if (parts.Length != 2 || !float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || w <= 0) return;
            float width = e.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;
            float want = Mathf.Round(width * h / w);
            if (Mathf.Abs(e.resolvedStyle.height - want) > 0.5f) e.style.height = want;
        }

        public static StyleLength Len(string v)
        {
            v = v.Trim();
            if (v.EndsWith("%")) return new StyleLength(Length.Percent(float.Parse(v.TrimEnd('%'), CultureInfo.InvariantCulture)));
            if (v.EndsWith("px")) v = v.Substring(0, v.Length - 2);
            return new StyleLength(float.Parse(v, CultureInfo.InvariantCulture));
        }

        // ---- focus ----
        public static void FocusEl(this VisualElement e) { if (e == null) return; e.focusable = true; e.Focus(); }
        public static void BlurEl(this VisualElement e) { e?.Blur(); }

        // ---- events ----
        public static void On(this VisualElement e, string type, Action<DomEvent> handler)
        {
            var d = Dom.Data(e);
            if (!d.handlers.TryGetValue(type, out var list))
            {
                d.handlers[type] = list = new List<Action<DomEvent>>();
                Wire(e, type);
            }
            list.Add(handler);
        }

        public static void On(this VisualElement e, string type, Action handler) => e.On(type, ev => handler());

        public static void Fire(this VisualElement e, string type, DomEvent ev)
        {
            if (e == null || !(e.userData is DomData d) || !d.handlers.TryGetValue(type, out var list)) return;
            ev.type = type;
            ev.currentTarget = e;
            Dom.Invoke(() => { foreach (var h in list.ToList()) h(ev); });
            if (type == "click" || type == "contextmenu" || type == "keydown" || type == "mousedown" || type == "pointerdown" || type == "pointerup" || type == "drop")
                CssLayout.Flush(Dom.document);
        }

        static void Wire(VisualElement e, string type)
        {
            if (e is SvgNode)
            {
                // drawn, not laid out: the svg that holds it hit-tests for it
                for (var p = e.parent; p != null; p = p.parent) if (p is SvgEl svg) { svg.WatchNodes(type); break; }
                return;
            }
            switch (type)
            {
                case "click":
                    e.RegisterCallback<ClickEvent>(u => { var ev = DomEvent.From(u, e); e.Fire("click", ev); ev.Apply(u); });
                    break;
                case "contextmenu":
                    e.RegisterCallback<PointerUpEvent>(u => { if (u.button != 1) return; var ev = DomEvent.From(u, e); ev.button = 2; e.Fire("contextmenu", ev); ev.Apply(u); });
                    break;
                case "mousedown": case "pointerdown":
                    e.RegisterCallback<PointerDownEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); ev.Apply(u); });
                    break;
                case "mouseup": case "pointerup":
                    e.RegisterCallback<PointerUpEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); ev.Apply(u); });
                    break;
                case "mousemove": case "pointermove":
                    e.RegisterCallback<PointerMoveEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); ev.Apply(u); });
                    break;
                case "pointercancel":
                    e.RegisterCallback<PointerCancelEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); });
                    break;
                case "mouseenter":
                    e.RegisterCallback<PointerEnterEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); });
                    break;
                case "mouseleave":
                    e.RegisterCallback<PointerLeaveEvent>(u => { var ev = DomEvent.From(u, e); e.Fire(type, ev); });
                    break;
                case "wheel":
                    e.RegisterCallback<WheelEvent>(u =>
                    {
                        // Chrome reports 100 px per wheel notch; Unity reports 3 lines
                        var local = e.WorldToLocal(u.mousePosition);
                        var ev = new DomEvent { type = "wheel", target = u.target as VisualElement ?? e, deltaY = u.delta.y * 100f / 3f, clientX = u.mousePosition.x, clientY = u.mousePosition.y, offsetX = local.x, offsetY = local.y, shiftKey = u.shiftKey, ctrlKey = u.ctrlKey, altKey = u.altKey };
                        e.Fire("wheel", ev);
                        if (ev.defaultPrevented) u.StopPropagation();
                    });
                    break;
                case "keydown":
                    e.focusable = true;
                    e.RegisterCallback<KeyDownEvent>(u => { if (u.keyCode == KeyCode.None && u.character == 0) return; var ev = DomEvent.FromKey(u, e); e.Fire("keydown", ev); ev.Apply(u); });
                    break;
                case "input": case "change":
                    // DomInput / DomSelect raise these themselves.
                    break;
                case "dragstart": case "dragover": case "dragleave": case "drop":
                    DragDrop.Watch(e);
                    break;
                case "animationend":
                    // Dom/CssAnimation.cs fires it; an element no animation rule reached still gets it, after 2s
                    e.schedule.Execute(() => { if (!CssAnimation.HasRun(e)) e.Fire("animationend", new DomEvent { target = e }); }).StartingIn(2000);
                    break;
            }
        }
    }
}
