// Unity build: the page's title="" tooltips (UI Toolkit shows none at runtime). Hovering an element with a title for a
// moment shows it in a gold-framed card by the mouse: the first line as a heading, comparison lines ("+3 might, -1
// protection vs. ...") with the gains in green and the losses in red.
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class CssTooltip
    {
        static VisualElement _box, _over;
        /// <summary>the item an element shows: (item, hero or -1 for the selected one, count, worn)</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object[]> Items = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object[]>();
        /// <summary>the host's item card (Web.ItemCard): content for an item's tooltip, null for the plain text</summary>
        public static System.Func<int, int, int, bool, string, VisualElement> ItemCard;

        public static void MarkItem(VisualElement e, int item, int hero, int count = 1, bool worn = false)
        {
            Items.Remove(e);
            if (item != 0) Items.Add(e, new object[] { item, hero, count, worn });
        }
        static string _text;
        static float _since;
        const float DELAY = 0.35f;

        public static void Tick(VisualElement root, VisualElement picked, Vector2 mouse)
        {
            if (root == null) return;
            VisualElement owner = null;
            for (var e = picked; e != null; e = e.parent) if (!string.IsNullOrEmpty(e.tooltip)) { owner = e; break; }
            if (owner != _over) { _over = owner; _since = Time.unscaledTime; Hide(); }
            if (owner == null || Input.GetMouseButton(0) || Time.unscaledTime - _since < DELAY) return;
            if (_box == null || _box.parent != root || _text != owner.tooltip) Show(root, owner.tooltip);
            // beside the mouse, kept inside the window
            float w = _box.layout.width, h = _box.layout.height;
            if (float.IsNaN(w)) w = 260; if (float.IsNaN(h)) h = 60;
            float x = mouse.x + 18, y = mouse.y + 20;
            if (x + w > root.layout.width - 4) x = mouse.x - w - 12;
            if (y + h > root.layout.height - 4) y = mouse.y - h - 12;
            _box.style.left = Mathf.Max(4, x); _box.style.top = Mathf.Max(4, y);
            _box.BringToFront();
        }

        static void Hide() { _box?.RemoveFromHierarchy(); _box = null; _text = null; }

        static void Show(VisualElement root, string text)
        {
            Hide();
            _text = text;
            if (ItemCard != null && _over != null && Items.TryGetValue(_over, out var it))
            {
                var card = ItemCard((int)it[0], (int)it[1], (int)it[2], (bool)it[3], text);
                if (card != null)
                {
                    card.pickingMode = PickingMode.Ignore;
                    card.style.position = Position.Absolute;
                    root.Add(card);
                    _box = card;
                    return;
                }
            }
            var box = new VisualElement { pickingMode = PickingMode.Ignore, name = "tooltip" };
            var s = box.style;
            s.position = Position.Absolute; s.maxWidth = 340;
            s.paddingLeft = s.paddingRight = 10; s.paddingTop = s.paddingBottom = 7;
            s.backgroundColor = (Color)new Color32(0x14, 0x10, 0x0a, 245);
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = (Color)new Color32(0xc9, 0xa7, 0x5a, 255);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i += 1)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                var l = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.fontSize = i == 0 && lines.Length > 1 ? 15 : 13;
                l.style.color = i == 0 && lines.Length > 1 ? (Color)new Color32(0xf0, 0xd4, 0x88, 255) : (Color)new Color32(0xe3, 0xd5, 0xae, 255);
                if (i > 0) l.style.marginTop = 3;
                if (Regex.IsMatch(line, @"vs\.|slot empty"))
                {
                    // the comparison: gains green, losses red, piece by piece
                    var row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
                    row.style.marginTop = 6; row.style.paddingTop = 4;
                    row.style.borderTopWidth = 1; row.style.borderTopColor = (Color)new Color32(0x5c, 0x4a, 0x29, 255);
                    foreach (var piece in Regex.Split(line, @"([+-]\d+ (?:might|protection))"))
                    {
                        if (piece.Length == 0) continue;
                        // edge spaces as no-break spaces (a label's own spaces at its ends are dropped)
                        var t = new Label(Regex.Replace(piece, @"^ | $", "\u00a0")) { pickingMode = PickingMode.Ignore, enableRichText = false };
                        t.style.fontSize = 13; t.style.marginLeft = t.style.marginRight = 0; t.style.paddingLeft = t.style.paddingRight = 0;
                        t.style.whiteSpace = WhiteSpace.Normal;
                        t.style.color = piece.StartsWith("+") ? (Color)new Color32(0x7f, 0xd3, 0x6b, 255) : piece.StartsWith("-") ? (Color)new Color32(0xe0, 0x66, 0x5a, 255) : (Color)new Color32(0xe3, 0xd5, 0xae, 255);
                        if (piece.StartsWith("+") || piece.StartsWith("-")) t.style.unityFontStyleAndWeight = FontStyle.Bold;
                        row.Add(t);
                    }
                    box.Add(row);
                    continue;
                }
                l.enableRichText = false;
                l.text = line;
                box.Add(l);
            }
            root.Add(box);
            _box = box;
        }
    }
}
