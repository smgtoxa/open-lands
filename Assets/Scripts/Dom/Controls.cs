// <input>, <select> and CSS value helpers for the DOM layer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class Css
    {
        static readonly Dictionary<string, UnityEngine.Color> Named = new Dictionary<string, UnityEngine.Color>
        {
            ["black"] = UnityEngine.Color.black, ["white"] = UnityEngine.Color.white, ["red"] = UnityEngine.Color.red, ["transparent"] = new UnityEngine.Color(0, 0, 0, 0),
            ["none"] = new UnityEngine.Color(0, 0, 0, 0), ["currentColor"] = UnityEngine.Color.white,
        };

        /// <summary>#rgb, #rrggbb, #rrggbbaa, rgb(), rgba(), named.</summary>
        public static UnityEngine.Color Color(string s)
        {
            if (string.IsNullOrEmpty(s)) return new UnityEngine.Color(0, 0, 0, 0);
            s = s.Trim();
            if (Named.TryGetValue(s, out var n)) return n;
            // ponytail: gradients paint as their first colour stop; real gradients need a mesh/texture.
            if (s.Contains("gradient("))
            {
                var stop = System.Text.RegularExpressions.Regex.Match(s, @"#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)");
                return stop.Success ? Color(stop.Value) : new UnityEngine.Color(0, 0, 0, 0);
            }
            if (s.StartsWith("#"))
            {
                string h = s.Substring(1);
                if (h.Length == 3 || h.Length == 4) h = string.Concat(h.Select(c => new string(c, 2)));
                int v(int i) => int.Parse(h.Substring(i, 2), NumberStyles.HexNumber);
                return new Color32((byte)v(0), (byte)v(2), (byte)v(4), h.Length >= 8 ? (byte)v(6) : (byte)255);
            }
            if (s.StartsWith("rgb"))
            {
                var parts = s.Substring(s.IndexOf('(') + 1).TrimEnd(')').Split(',').Select(p => p.Trim()).ToArray();
                float c(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture) / 255f;
                float a = parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 1f;
                return new UnityEngine.Color(c(0), c(1), c(2), a);
            }
            return UnityEngine.Color.magenta;
        }

        /// <summary>transform: rotate(Ndeg) scale(x[,y]) translate(x, y) - what the pages set.</summary>
        public static void ApplyTransform(VisualElement e, string value)
        {
            foreach (var fn in value.Split(')'))
            {
                var t = fn.Trim();
                if (t.Length == 0) continue;
                int p = t.IndexOf('(');
                if (p < 0) continue;
                string name = t.Substring(0, p).Trim();
                var args = t.Substring(p + 1).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                float num(string a) => float.Parse(new string(a.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray()), CultureInfo.InvariantCulture);
                if (name == "rotate") e.style.rotate = new Rotate(new Angle(num(args[0]), AngleUnit.Degree));
                else if (name == "scale") { float x = num(args[0]), y = args.Length > 1 ? num(args[1]) : x; e.style.scale = new Scale(new Vector3(x, y, 1)); }
                else if (name == "translate" || name == "translateX")
                    e.style.translate = new Translate(DomExt.Len(args[0]).value, args.Length > 1 ? DomExt.Len(args[1]).value : 0);
                else if (name == "translateY") e.style.translate = new Translate(0, DomExt.Len(args[0]).value);
            }
        }
    }

    /// <summary>&lt;input&gt;: text/search, checkbox, range, number, file (inert) - by its type attribute.</summary>
    public sealed class DomInput : VisualElement
    {
        string _type = "text";
        VisualElement _control;
        public float min = 0, max = 100, step = 1;
        public string placeholder = "";

        public DomInput() { Build(); }

        public void OnAttribute(string name, string value)
        {
            if (name == "type") { _type = value; Build(); }
            else if (name == "min") { min = float.Parse(value, CultureInfo.InvariantCulture); Build(); }
            else if (name == "max") { max = float.Parse(value, CultureInfo.InvariantCulture); Build(); }
            else if (name == "step") step = float.Parse(value, CultureInfo.InvariantCulture);
            else if (name == "placeholder") { placeholder = value; if (_control is TextField tf) tf.textEdition.placeholder = value; }
            else if (name == "value") value_ = value;
            else if (name == "maxlength" && _control is TextField t2) t2.maxLength = int.Parse(value);
            else if (name == "readonly" && _control is TextField t3) t3.isReadOnly = true;
        }

        void Build()
        {
            Clear();
            foreach (var c in GetClasses().Where(c => c.StartsWith("input-type-")).ToList()) RemoveFromClassList(c);
            AddToClassList("input-type-" + _type);   // the browser's look per type (page.uss, UA section)
            string v = Dom.Data(this).value;
            switch (_type)
            {
                case "checkbox":
                {
                    var t = new Toggle { value = Dom.Data(this).isChecked };
                    t.RegisterValueChangedCallback(e => { Dom.Data(this).isChecked = e.newValue; this.Fire("input", new DomEvent { target = this }); this.Fire("change", new DomEvent { target = this }); });
                    _control = t;
                    break;
                }
                case "range":
                {
                    var s = new Slider(min, max) { value = float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : min };
                    s.RegisterValueChangedCallback(e => { Dom.Data(this).value = e.newValue.ToString(CultureInfo.InvariantCulture); this.Fire("input", new DomEvent { target = this }); });
                    s.RegisterCallback<PointerUpEvent>(e => this.Fire("change", new DomEvent { target = this }));
                    _control = s;
                    break;
                }
                case "file":
                    _control = new VisualElement();
                    break;
                default:
                {
                    var tf = new TextField { value = v ?? "" };
                    tf.textEdition.placeholder = placeholder;
                    tf.RegisterValueChangedCallback(e => { Dom.Data(this).value = e.newValue; this.Fire("input", new DomEvent { target = this }); });
                    tf.RegisterCallback<FocusOutEvent>(e => this.Fire("change", new DomEvent { target = this }));
                    tf.RegisterCallback<KeyDownEvent>(e =>
                    {
                        var ev = DomEvent.FromKey(e, this);
                        this.Fire("keydown", ev);
                        if (ev.defaultPrevented) { e.StopPropagation(); }
                    }, TrickleDown.TrickleDown);
                    _control = tf;
                    break;
                }
            }
            _control.AddToClassList("input-" + _type);
            Add(_control);
        }

        string value_ { set { Dom.Data(this).value = value; if (_control is TextField tf) tf.SetValueWithoutNotify(value); else if (_control is Slider s && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) s.SetValueWithoutNotify(f); } }

        public string value
        {
            get => _control is TextField tf ? tf.value : Dom.Data(this).value;
            set => value_ = value ?? "";
        }

        public bool @checked
        {
            get => Dom.Data(this).isChecked;
            set { Dom.Data(this).isChecked = value; if (_control is Toggle t) t.SetValueWithoutNotify(value); }
        }

        public double valueAsNumber => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

        public void FocusInput() { _control.Focus(); }
    }

    /// <summary>&lt;select&gt; with &lt;option value&gt;s (hidden options are left out of the list).</summary>
    public sealed class DomSelect : VisualElement
    {
        public sealed class Option { public string value, text; public bool hidden; }
        public readonly List<Option> options = new List<Option>();
        public static VisualElement Opening;
        readonly DropdownField _field = new DropdownField();
        string _value = "";

        public DomSelect()
        {
            _field.RegisterValueChangedCallback(e =>
            {
                var o = Visible().FirstOrDefault(x => x.text == e.newValue);
                if (o == null) return;
                _value = o.value;
                this.Fire("input", new DomEvent { target = this });
                this.Fire("change", new DomEvent { target = this });
            });
            _field.focusable = false;
            // our own menu, not Unity's: its GenericDropdownMenu needs theme rules this panel does not have, and
            // placed itself (over the field, cut short) whatever the host did
            _field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                Opening = _field;
                e.StopImmediatePropagation();
                if (_menu != null) { CloseMenu(); return; }
                // the page's own mousedown / focus come first (the travelling circle fills in its place names then)
                this.Fire("mousedown", new DomEvent { type = "mousedown", target = this });
                this.Fire("focus", new DomEvent { type = "focus", target = this });
                OpenMenu();
            }, TrickleDown.TrickleDown);
            _field.RegisterCallback<NavigationSubmitEvent>(e => { e.StopImmediatePropagation(); OpenMenu(); }, TrickleDown.TrickleDown);
            Add(_field);
            _field.RegisterCallback<GeometryChangedEvent>(_ => SizeToOptions());
        }

        /// <summary>A browser select is as wide as its widest option.</summary>
        void SizeToOptions()
        {
            var text = _field.Q<TextElement>(className: "unity-base-popup-field__text");
            if (text == null || text.panel == null) return;
            // a select given a width of its own keeps it: its choices shorten instead (and its arrow stays in)
            if (ClassListContains("fixed-width") || (parent != null && parent.ClassListContains("set-ctl")))
            {
                if (text.style.minWidth.keyword != StyleKeyword.Null) text.style.minWidth = StyleKeyword.Null;
                return;
            }
            float w = 0;
            foreach (var o in Visible()) w = Math.Max(w, text.MeasureTextSize(o.text ?? "", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x);
            w = (float)Math.Ceiling(w);
            if (Math.Abs(text.resolvedStyle.minWidth.value - w) > 0.5f) text.style.minWidth = w;
        }

        IEnumerable<Option> Visible() => options.Where(o => !o.hidden);

        public void AddOption(string value, string text, bool selected = false, bool hidden = false)
        {
            options.Add(new Option { value = value, text = text, hidden = hidden });
            if (selected || options.Count == 1) _value = value;
            Refresh();
        }

        public void ClearOptions() { options.Clear(); _value = ""; Refresh(); }

        public void SetOptionHidden(string value, bool hidden)
        {
            foreach (var o in options) if (o.value == value) o.hidden = hidden;
            Refresh();
        }

        // The same options are not handed to the field again: pages rebuild their lists on mousedown/focus (the
        // travelling circle's destinations), and new choices under an opening menu closed it at once.
        public void Refresh()
        {
            var choices = Visible().Select(o => o.text).ToList();
            if (_field.choices == null || !_field.choices.SequenceEqual(choices)) _field.choices = choices;
            var cur = options.FirstOrDefault(o => o.value == _value);
            string text = cur?.text ?? "";
            if (_field.value != text) _field.SetValueWithoutNotify(text);
            SizeToOptions();
        }

        public string value
        {
            get => _value;
            set { _value = value ?? ""; Refresh(); }
        }

        public int selectedIndex => options.FindIndex(o => o.value == _value);

        // ---- the menu ----
        VisualElement _menu, _menuRoot;
        EventCallback<PointerDownEvent> _outside;
        static readonly UnityEngine.Color MenuBg = new UnityEngine.Color32(0x16, 0x12, 0x0b, 250), MenuLine = new UnityEngine.Color32(0xc9, 0xa7, 0x5a, 255),
            MenuText = new UnityEngine.Color32(0xe8, 0xd4, 0x9c, 255), MenuHover = new UnityEngine.Color32(0x4a, 0x3a, 0x1e, 255), MenuOn = new UnityEngine.Color32(0xff, 0xd8, 0x77, 255), MenuCur = new UnityEngine.Color32(0x2c, 0x22, 0x12, 255);

        public bool MenuOpen => _menu != null;

        public void OpenMenu()
        {
            CloseMenu();
            var root = panel?.visualTree;
            if (root == null) return;
            var list = Visible().ToList();
            if (list.Count == 0) return;
            const float row = 26;
            var at = _field.worldBound;
            float below = root.layout.height - at.yMax - 8, above = at.yMin - 8;
            float want = list.Count * row + 6;
            bool down = below >= want || below >= above;
            float height = Math.Min(want, Math.Max(row * 3, down ? below : above));
            _menuRoot = root;
            var menu = new VisualElement { name = "dom-select-menu", pickingMode = PickingMode.Position };
            var ms = menu.style;
            ms.position = Position.Absolute;
            ms.left = at.xMin; ms.minWidth = at.width; ms.height = height;
            ms.top = down ? at.yMax + 2 : at.yMin - height - 2;
            ms.backgroundColor = MenuBg;
            ms.borderTopWidth = ms.borderBottomWidth = ms.borderLeftWidth = ms.borderRightWidth = 1;
            ms.borderTopColor = ms.borderBottomColor = ms.borderLeftColor = ms.borderRightColor = MenuLine;
            ms.paddingTop = ms.paddingBottom = 3;
            ms.overflow = Overflow.Hidden;
            var inner = new VisualElement();
            inner.style.flexDirection = FlexDirection.Column;
            menu.Add(inner);
            int current = list.FindIndex(o => o.value == _value);
            for (int i = 0; i < list.Count; i += 1)
            {
                int index = i;
                var item = new Label(list[i].text) { enableRichText = false };
                var st = item.style;
                st.height = row; st.paddingLeft = 12; st.paddingRight = 16; st.marginTop = st.marginBottom = 0;
                st.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft; st.whiteSpace = WhiteSpace.NoWrap; st.fontSize = 14;
                st.color = i == current ? MenuOn : MenuText;
                if (i == current) st.backgroundColor = MenuCur;
                item.RegisterCallback<PointerEnterEvent>(_ => item.style.backgroundColor = MenuHover);
                item.RegisterCallback<PointerLeaveEvent>(_ => item.style.backgroundColor = index == current ? new StyleColor(MenuCur) : new StyleColor(StyleKeyword.Null));
                item.RegisterCallback<PointerUpEvent>(e => { e.StopPropagation(); CloseMenu(); ChooseVisible(index); });
                inner.Add(item);
            }
            // a long list scrolls with the wheel, the current choice in view
            float maxScroll = Math.Max(0, want - height), scroll = Math.Max(0, Math.Min(maxScroll, current * row - height / 2 + row));
            inner.style.translate = new Translate(0, -scroll);
            menu.RegisterCallback<WheelEvent>(e =>
            {
                scroll = Math.Max(0, Math.Min(maxScroll, scroll + e.delta.y * 20));
                inner.style.translate = new Translate(0, -scroll);
                e.StopPropagation();
            });
            root.Add(menu);
            menu.BringToFront();
            _menu = menu;
            // a press anywhere else closes it
            _outside = e =>
            {
                if (_menu == null) return;
                var t = e.target as VisualElement;
                for (var p = t; p != null; p = p.parent) if (p == _menu || p == _field) return;
                CloseMenu();
            };
            root.RegisterCallback(_outside, TrickleDown.TrickleDown);
        }

        public void CloseMenu()
        {
            if (_menuRoot != null && _outside != null) _menuRoot.UnregisterCallback(_outside, TrickleDown.TrickleDown);
            _menu?.RemoveFromHierarchy();
            _menu = null; _outside = null;
        }

        /// <summary>The visible option at `index` chosen, as picking it in the menu does (input and change fire).</summary>
        public void ChooseVisible(int index)
        {
            var list = Visible().ToList();
            if (index < 0 || index >= list.Count) return;
            _field.value = list[index].text;   // the value-changed callback sets _value and fires the page's events
        }
    }
}
