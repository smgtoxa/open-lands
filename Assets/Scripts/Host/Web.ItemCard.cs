// Unity build only: an item's tooltip as a card in the manner of the modern RPGs (Diablo II's in particular): the
// item's picture, its name coloured by rarity, what kind of thing it is, its numbers one to a line, what it is for and
// who can carry it. Gear that a hero could wear is shown beside the card of what that hero wears now, each number of
// the new one marked with how far it is above or below it.
using System;
using System.Collections.Generic;
using System.Linq;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        static readonly Color CardGold = new Color32(0xc7, 0xa3, 0x56, 255), CardWhite = new Color32(0xec, 0xe6, 0xd6, 255),
            CardGrey = new Color32(0x9a, 0x92, 0x80, 255), CardBlue = new Color32(0x7f, 0x9c, 0xf0, 255),
            CardUp = new Color32(0x6f, 0xd3, 0x5b, 255), CardDown = new Color32(0xe0, 0x5a, 0x4e, 255), CardLine = new Color32(0x5c, 0x4a, 0x29, 255);

        void init_E20_itemCard() { CssTooltip.ItemCard = itemCard; }

        VisualElement itemCard(int item, int hero, int count, bool worn, string title)
        {
            if (engine == null) return null;
            var info = engine.itemInfo(item);
            if (info == null) return null;
            int c = hero >= 0 ? hero : engine.selectedCharacter;
            var ch = c >= 0 && c < engine.characters.Length && (engine.characters[c].flags & 1) != 0 ? engine.characters[c] : null;
            // what the hero wears where this would go (nothing to compare for a worn item or something to use)
            int equipped = 0;
            if (!worn && ch != null && !info.usable)
            {
                var fits = engine.uiSlotsFor(item, c);
                if (fits.Count > 0 && ch.items[fits[0]] != item) equipped = ch.items[fits[0]];
            }
            var other = equipped != 0 ? engine.itemInfo(equipped) : null;

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            var hints = title.Split('\n').Where(l => l.Contains("Click") || l.Contains("key ") || l.Contains("on the hotbar")).Select(l => l.Trim()).ToList();
            row.Add(card(item, info, other, count, ch, hints, worn ? $"Worn by {ch?.name}" : null));
            if (other != null)
            {
                var gap = new VisualElement(); gap.style.width = 6; row.Add(gap);
                row.Add(card(equipped, other, null, 1, ch, null, $"Equipped: {ch.name}"));
            }
            return row;
        }

        VisualElement card(int item, ItemInfo info, ItemInfo vs, int count, Character hero, List<string> hints, string header)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            var s = box.style;
            s.width = 250; s.alignItems = Align.Center;
            s.paddingLeft = s.paddingRight = 12; s.paddingTop = 8; s.paddingBottom = 10;
            s.backgroundColor = (Color)new Color32(0x09, 0x08, 0x06, 242);
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = (Color)new Color32(0x8a, 0x6d, 0x36, 255);
            Label line(string text, Color color, int size = 13, bool bold = false)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
                l.style.color = color; l.style.fontSize = size; l.style.whiteSpace = WhiteSpace.Normal;
                l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.marginTop = l.style.marginBottom = 0; l.style.paddingTop = l.style.paddingBottom = 1;
                if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
                box.Add(l);
                return l;
            }
            void rule() { var r = new VisualElement(); r.style.height = 1; r.style.alignSelf = Align.Stretch; r.style.marginTop = r.style.marginBottom = 5; r.style.backgroundColor = CardLine; box.Add(r); }
            if (header != null) { line(header.ToUpperInvariant(), CardGrey, 10); }
            // the picture, larger than in the bag
            var shape = engine.getItemIconShapePtr(item);
            if (shape != null)
            {
                var icon = (CanvasEl)Dom.El("canvas");
                icon.width = 24; icon.height = 24;
                drawIconInto(icon, shape, engine.uiPalette());
                // twice the bag's size (the page's canvas rules size the element itself: it is scaled instead)
                var holder = new VisualElement { pickingMode = PickingMode.Ignore };
                holder.style.width = 60; holder.style.height = 60; holder.style.marginTop = 12; holder.style.marginBottom = 4; holder.style.alignItems = Align.Center; holder.style.justifyContent = Justify.Center;
                icon.style.scale = new Scale(new Vector3(1.6f, 1.6f, 1));
                holder.Add(icon);
                box.Add(holder);
            }
            // a named piece ("Cutter") in gold, as a unique; the rest in white
            bool named = info.name.Contains("\"");
            var name = line(info.name + (count > 1 ? $"  x{count}" : ""), named ? CardGold : CardWhite, 16);
            name.AddToClassList("card-name");
            var extra = engine.uiExtraItem(item);   // an item the remake added: its own kind and use
            string kind = extra != null && extra.kind == "errand" ? (extra.family != null ? "Trophy for the imp" : "Pit item") : info.usable ? "Usable" : info.slots.Count > 0 ? string.Join(" or ", info.slots.Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1))) : "Quest item";
            line(kind, CardGrey, 12);
            // the skill a weapon trains (every item carries one, fighter by default: only a weapon's means anything)
            if (!info.usable && (info.might != 0 || info.protection != 0))
            {
                rule();
                void stat(string label, int value, int? than)
                {
                    var r = new VisualElement { pickingMode = PickingMode.Ignore };
                    r.style.flexDirection = FlexDirection.Row; r.style.justifyContent = Justify.Center;
                    var a = new Label($"{label}: {value}") { pickingMode = PickingMode.Ignore };
                    a.style.color = CardWhite; a.style.fontSize = 14; a.style.marginTop = a.style.marginBottom = 0;
                    r.Add(a);
                    if (than != null && than.Value != value)
                    {
                        int d = value - than.Value;
                        var b = new Label($"\u00a0\u00a0{(d > 0 ? "▲ +" : "▼ ")}{d}") { pickingMode = PickingMode.Ignore };
                        b.style.color = d > 0 ? CardUp : CardDown; b.style.fontSize = 14; b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.marginTop = b.style.marginBottom = 0;
                        r.Add(b);
                    }
                    box.Add(r);
                }
                if (info.might != 0 || (vs != null && vs.might != 0)) stat(info.usable ? "Strength" : "Might", info.might, vs?.might);
                if (info.protection != 0 || (vs != null && vs.protection != 0)) stat("Protection", info.protection, vs?.protection);
                if (!string.IsNullOrEmpty(info.skill) && info.might != 0) line($"{char.ToUpperInvariant(info.skill[0]) + info.skill.Substring(1)} skill", CardBlue, 12);
                // weapon wear (a setting): each blow dulls the edge, down to half the damage; the imp grinds it back
                if (engine.weaponWear && info.might != 0)
                {
                    double cond = engine.uiCondition(item);
                    int pct = (int)Math.Round(cond * 100), hits = (int)Math.Round((0.5 + 0.5 * cond) * 100);
                    var col = cond > 0.85 ? CardUp : cond > 0.3 ? CardGold : CardDown;
                    line($"Durability: {pct}%" + (string.IsNullOrEmpty(info.condition) ? "" : $" ({info.condition})"), col, 12);
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.style.width = 150; bar.style.height = 5; bar.style.marginTop = 2; bar.style.marginBottom = 2; bar.style.backgroundColor = CardLine;
                    var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                    fill.style.width = Length.Percent(pct); fill.style.height = Length.Percent(100); fill.style.backgroundColor = col;
                    bar.Add(fill); box.Add(bar);
                    if (hits < 100) line($"Strikes at {hits}% damage. The imp can repair it.", CardGrey, 11);
                }
            }
            string use = !string.IsNullOrEmpty(extra?.use) ? extra.use : engine.itemUse(info.name, info.usable, info.slots.Count > 0);
            if (!string.IsNullOrEmpty(use)) { rule(); line(use, CardGrey, 12).style.unityFontStyleAndWeight = FontStyle.Italic; }
            if (!info.usable && info.slots.Count > 0)
            {
                var who = engine.characters.Select((x, i) => (x, i)).Where(p => (p.x.flags & 1) != 0 && engine.uiSlotsFor(item, p.i).Count > 0).Select(p => p.x.name).ToList();
                line(who.Count > 0 ? $"Can be worn by {string.Join(", ", who)}" : "Nobody in the party can wear it", who.Count > 0 ? CardBlue : CardDown, 12);
            }
            if (hints != null && hints.Count > 0) { rule(); foreach (var h in hints) line(h, CardGrey, 11); }
            return box;
        }
    }
}
