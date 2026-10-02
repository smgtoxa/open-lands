// DOM events over UI Toolkit events, and HTML5 drag-and-drop emulated with pointer events.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class DataTransfer
    {
        public readonly Dictionary<string, string> data = new Dictionary<string, string>();
        public string effectAllowed = "all", dropEffect = "none";
        public void setData(string type, string value) => data[type] = value;
        public string getData(string type) => data.TryGetValue(type, out var v) ? v : "";
        public IEnumerable<string> types => data.Keys;
        public bool HasType(string type) => data.ContainsKey(type);
    }

    public sealed class DomEvent
    {
        public string type;
        /// <summary>MouseEvent.button: 0 left, 1 middle, 2 right.</summary>
        public int button;
        public bool shiftKey, ctrlKey, altKey, metaKey;
        /// <summary>clientX/Y: panel coordinates; offsetX/Y: relative to the element the handler is on.</summary>
        public float clientX, clientY, offsetX, offsetY;
        public float deltaY;
        /// <summary>KeyboardEvent.key: "Enter", " ", "ArrowUp", "a", "F5"...</summary>
        public string key;
        public VisualElement target, currentTarget;
        public DataTransfer dataTransfer;
        public bool defaultPrevented, propagationStopped;

        public void preventDefault() => defaultPrevented = true;
        public void stopPropagation() => propagationStopped = true;

        internal void Apply(EventBase u)
        {
            if (propagationStopped) u.StopPropagation();
        }

        static int WebButton(int unity) => unity == 1 ? 2 : unity == 2 ? 1 : 0;

        internal static DomEvent From(IPointerEvent p, VisualElement on)
        {
            var local = on.WorldToLocal(p.position);
            return new DomEvent
            {
                button = WebButton(p.button), shiftKey = p.shiftKey, ctrlKey = p.ctrlKey, altKey = p.altKey,
                clientX = p.position.x, clientY = p.position.y, offsetX = local.x, offsetY = local.y,
                target = (p as EventBase)?.target as VisualElement ?? on,
            };
        }

        internal static DomEvent From(ClickEvent c, VisualElement on)
        {
            var local = on.WorldToLocal(c.position);
            return new DomEvent
            {
                button = 0, shiftKey = c.shiftKey, ctrlKey = c.ctrlKey, altKey = c.altKey,
                clientX = c.position.x, clientY = c.position.y, offsetX = local.x, offsetY = local.y,
                target = c.target as VisualElement ?? on,
            };
        }

        internal static DomEvent FromKey(KeyDownEvent k, VisualElement on) => new DomEvent
        {
            key = KeyName(k.keyCode, k.character, k.shiftKey), shiftKey = k.shiftKey, ctrlKey = k.ctrlKey, altKey = k.altKey,
            target = on,
        };

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetKeyState(int key);
        static bool NumLock() => (GetKeyState(0x90) & 1) != 0;
#else
        static bool NumLock() => true;
#endif

        /// <summary>KeyboardEvent.key for a Unity key (letters lower case unless shifted, as a browser reports them).</summary>
        public static string KeyName(KeyCode code, char character, bool shift)
        {
            switch (code)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Space: return " ";
                case KeyCode.Escape: return "Escape";
                case KeyCode.UpArrow: return "ArrowUp";
                case KeyCode.DownArrow: return "ArrowDown";
                case KeyCode.LeftArrow: return "ArrowLeft";
                case KeyCode.RightArrow: return "ArrowRight";
                case KeyCode.Home: return "Home";
                case KeyCode.End: return "End";
                case KeyCode.PageUp: return "PageUp";
                case KeyCode.PageDown: return "PageDown";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Delete: return "Delete";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Control";
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
            }
            if (code >= KeyCode.F1 && code <= KeyCode.F15) return "F" + (1 + code - KeyCode.F1);
            if (character != 0 && !char.IsControl(character)) return character.ToString();
            if (code >= KeyCode.A && code <= KeyCode.Z) { char c = (char)('a' + (code - KeyCode.A)); return shift ? char.ToUpperInvariant(c).ToString() : c.ToString(); }
            if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9)
            {
                // as a browser reports the keypad: digits with Num Lock on, the navigation keys with it off
                const string nav = "Insert,End,ArrowDown,PageDown,ArrowLeft,Clear,ArrowRight,Home,ArrowUp,PageUp";
                int n = code - KeyCode.Keypad0;
                return NumLock() ? n.ToString() : nav.Split(',')[n];
            }
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9)
            {
                const string shifted = ")!@#$%^&*(";
                int n = code - KeyCode.Alpha0;
                return shift ? shifted[n].ToString() : n.ToString();
            }
            switch (code)
            {
                case KeyCode.Slash: return shift ? "?" : "/";
                case KeyCode.Minus: return shift ? "_" : "-";
                case KeyCode.Equals: return shift ? "+" : "=";
                case KeyCode.Comma: return shift ? "<" : ",";
                case KeyCode.Period: return shift ? ">" : ".";
                case KeyCode.KeypadPlus: return "+";
                case KeyCode.KeypadMinus: return "-";
                case KeyCode.KeypadMultiply: return "*";
                case KeyCode.KeypadDivide: return "/";
                case KeyCode.KeypadPeriod: return NumLock() ? "." : "Delete";
            }
            return code.ToString();
        }
    }

    /// <summary>
    /// HTML5 drag and drop: press on a draggable element and move; dragstart fills the dataTransfer,
    /// dragover handlers that call preventDefault() mark a drop target, releasing there fires drop.
    /// </summary>
    public static class DragDrop
    {
        static readonly HashSet<VisualElement> Watched = new HashSet<VisualElement>();
        static VisualElement _source, _over;
        static Vector2 _start;
        static DataTransfer _transfer;
        static bool _dragging;
        static VisualElement _root;
        static Label _ghost;

        public static void Watch(VisualElement e)
        {
            if (!Watched.Add(e)) return;
            e.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            EnsureRoot();
        }

        static void EnsureRoot()
        {
            if (_root != null || Dom.document == null) return;
            _root = Dom.document;
            _root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
        }

        static void Cancel()
        {
            if (_dragging) _over?.Fire("dragleave", new DomEvent { target = _over, dataTransfer = _transfer });
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
            _source = null;
            _over = null;
            _dragging = false;
        }

        static void OnDown(PointerDownEvent u)
        {
            EnsureRoot();
            Cancel();   // a new press: whatever drag was left over is over
            if (u.button != 0) return;
            var e = u.currentTarget as VisualElement;
            if (e == null || !Dom.Data(e).draggable) return;
            _source = e;
            _start = u.position;
            _dragging = false;
        }

        static void OnMove(PointerMoveEvent u)
        {
            if (_source == null) return;
            // the button is up but the release never reached us (let go outside the window, or over an element that
            // had the pointer captured): the drag is over - its ghost used to stay stuck to the cursor
            if ((u.pressedButtons & 1) == 0) { Cancel(); return; }
            if (!_dragging)
            {
                if ((u.position - (Vector3)_start).sqrMagnitude < 25) return;
                _dragging = true;
                _transfer = new DataTransfer();
                var ev = new DomEvent { target = _source, dataTransfer = _transfer, clientX = u.position.x, clientY = u.position.y };
                _source.Fire("dragstart", ev);
                if (ev.defaultPrevented || _transfer.data.Count == 0) { _source = null; _dragging = false; return; }
                _ghost = new Label("✥") { pickingMode = PickingMode.Ignore };
                _ghost.AddToClassList("drag-ghost");
                _root.Add(_ghost);
            }
            if (_ghost != null) { _ghost.style.left = u.position.x + 8; _ghost.style.top = u.position.y + 8; }
            var target = Target(u.position);
            if (target != _over)
            {
                _over?.Fire("dragleave", new DomEvent { target = _over, dataTransfer = _transfer });
                _over = target;
            }
            if (_over != null)
            {
                var ev = new DomEvent { target = _over, dataTransfer = _transfer, clientX = u.position.x, clientY = u.position.y };
                _over.Fire("dragover", ev);
                _transfer.dropEffect = ev.defaultPrevented ? "move" : "none";
            }
        }

        static void OnUp(PointerUpEvent u)
        {
            if (_source == null) return;
            if (_dragging)
            {
                if (_over != null && _transfer.dropEffect != "none")
                {
                    var ev = new DomEvent { target = _over, dataTransfer = _transfer, clientX = u.position.x, clientY = u.position.y };
                    _over.Fire("drop", ev);
                }
                _over?.Fire("dragleave", new DomEvent { target = _over, dataTransfer = _transfer });
                _source.Fire("dragend", new DomEvent { target = _source, dataTransfer = _transfer });
                u.StopPropagation();   // a drop is not also a click
            }
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
            _source = null;
            _over = null;
            _dragging = false;
        }

        /// <summary>The nearest element under the pointer (or an ancestor) that listens for dragover.</summary>
        static VisualElement Target(Vector2 position)
        {
            var picked = _root.panel?.Pick(position);
            for (var e = picked; e != null; e = e.parent)
                if (e.userData is DomData d && d.handlers.ContainsKey("dragover")) return e;
            return null;
        }
    }
}
