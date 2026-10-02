// src/platform/char-screen.mjs
// Character screen: a modern take on the original's inventory/stats page. Opened from a portrait;
// shows everything about one hero and lets you equip from the inventory, use the hand item and pick
// the hero's own quick spell (the C key casts each hero's own choice in turn).
// C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lol;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class CharScreen
    {
        // Where each equipment slot sits around the figure (percent of the doll box), by slot index: worn things in
        // the left column (head to feet), held things and rings in the right, a second pair of hands under the feet.
        // (The page puts them on the body, over a figure its square viewBox shrinks to a third of the box.)
        static readonly Dictionary<int, int[]> DOLL = new Dictionary<int, int[]>
        {
            [4] = new[] { 13, 11 }, [6] = new[] { 13, 30 }, [5] = new[] { 13, 49 }, [7] = new[] { 13, 68 }, [8] = new[] { 13, 87 },
            [0] = new[] { 87, 20 }, [1] = new[] { 87, 39 }, [9] = new[] { 87, 58 }, [10] = new[] { 87, 77 },
            [2] = new[] { 38, 93 }, [3] = new[] { 62, 93 },
        };
        // A four-armed hero (a Thomgog: Baccata): both pairs of hands beside their arms, the upper pair on the right
        // as for everyone, the lower pair on the left beside the lower arms; the worn things fill the left column above.
        static readonly Dictionary<int, int[]> DOLL4 = new Dictionary<int, int[]>
        {
            [4] = new[] { 13, 9 }, [6] = new[] { 13, 25 }, [5] = new[] { 13, 41 }, [7] = new[] { 87, 58 }, [8] = new[] { 13, 89 },
            [0] = new[] { 87, 22 }, [1] = new[] { 87, 40 }, [9] = new[] { 87, 74 }, [10] = new[] { 87, 89 },
            [2] = new[] { 13, 57 }, [3] = new[] { 13, 73 },
        };
        const string BODY = "M18.5 15h7l1 2.5 5.5 1Q36 19.5 36.8 23.5L38.3 39 39 52Q39.2 55.5 36.6 55.5 34.2 55.5 34 52.5L32.6 40 31.5 29 30.6 43 31.8 59H28.4L27.8 91.5 31 94.5Q32 97 29.5 97H23.2L22.8 61H21.2L20.8 97H14.5Q12 97 13 94.5L16.2 91.5 15.6 59H12.2L13.4 43 12.5 29 11.4 40 10 52.5Q9.8 55.5 7.4 55.5 4.8 55.5 5 52L5.7 39 7.2 23.5Q8 19.5 12 18.5L17.5 17.5z";
        // a four-armed figure: the upper pair raised, the lower pair hanging from the ribs, clear of each other
        static readonly string[] BODY4 =
        {
            "M18.5 15h7l1 2.5Q31 18 32 19.5L31.5 29 30.6 43 31.8 59H12.2L13.4 43 12.5 29 12 19.5Q13 18 17.5 17.5z",
            "M28.4 59L27.8 91.5 31 94.5Q32 97 29.5 97H23.2L22.8 61H21.2L20.8 97H14.5Q12 97 13 94.5L16.2 91.5 15.6 59z",
            "M13 21Q8 20 5.5 15L2.6 6.5Q2.2 3.2 4.6 2.8 6.8 2.6 7.4 5.2L9.8 12 13.6 15.5z",
            "M31 21Q36 20 38.5 15L41.4 6.5Q41.8 3.2 39.4 2.8 37.2 2.6 36.6 5.2L34.2 12 30.4 15.5z",
            "M12.8 29Q8.4 30 6.8 35.5L4.4 52.5Q4.2 56 6.8 56 9.2 56 9.4 53L11.2 40.5 13.2 36z",
            "M31.2 29Q35.6 30 37.2 35.5L39.6 52.5Q39.8 56 37.2 56 34.8 56 34.6 53L32.8 40.5 30.8 36z",
        };
        bool fourArms;
        /// <summary>Unity build: Web.equipBest (the swap with a notice of what changed)</summary>
        public Action<int> onEquipBest;
        /// <summary>Unity build, the in-game interface: whether a spell (hero id, slot) is in the Spells panel, and the toggle.</summary>
        public Func<int, int, bool> spellShown;
        bool spellsOnly;
        public Action<int, int> toggleSpellShown;
        static readonly (string, string)[] SKILLS = { ("fighter", "Fighter"), ("rogue", "Rogue"), ("mage", "Mage") };

        /// <summary>bar(kind): { bar, fill, text }</summary>
        public sealed class Bar
        {
            public VisualElement bar, fill, text;
        }

        static readonly JsonSerializerOptions KeyJson = new JsonSerializerOptions { IncludeFields = true };

        public readonly VisualElement root;
        public readonly Action<CanvasEl, Shape, byte[]> drawIcon;
        public readonly Func<LandsOfLore> getEngine;
        /// <summary>character id (as a JSON key) -> { slot, level }</summary>
        public readonly Func<JsonObject> quickSpells;
        public readonly Action<int, int, int> setQuickSpell;
        public readonly Func<int, int, int> powerFor; // (slot, characterId) -> remembered power
        public int c;
        public string key;
        int faceFrame = -1;
        public VisualElement title, prev, next, stats, skills, status, equip, best, use, spells, picker;
        public CanvasEl face;
        public Bar hp, mp;

        public CharScreen(VisualElement root, Action<CanvasEl, Shape, byte[]> drawIcon, Func<LandsOfLore> getEngine, Func<JsonObject> quickSpells, Action<int, int, int> setQuickSpell, Func<int, int, int> powerFor)
        {
            this.root = root;
            this.drawIcon = drawIcon;
            this.getEngine = getEngine;
            // A function, not the table itself: the host replaces this object wholesale when a profile
            // loads, and a screen holding the old one shows a hero with no quick spell - so the slider fell
            // back to the remembered power and read 4 whatever the player had actually chosen.
            this.quickSpells = quickSpells;
            this.setQuickSpell = setQuickSpell;
            this.powerFor = powerFor; // (slot, characterId) -> remembered power
            this.c = -1;
            this.key = ""; this.faceFrame = -1;
            this.build();
        }

        VisualElement el(string tag, string cls = null, string text = null) => Dom.El(tag, cls, text);

        void build()
        {
            var box = this.el("div", "modal-box char-box");
            var head = this.el("div", "modal-head");
            this.title = this.el("h2", "", "Hero");
            var nav = this.el("div", "char-nav");
            this.prev = this.el("button", "", "◀ Previous");
            this.next = this.el("button", "", "Next ▶");
            var close = this.el("button", "modal-close", "Close");
            foreach (var b in new[] { this.prev, this.next, close }) b.SetAttr("type", "button");
            this.prev.On("click", () => this.step(-1));
            this.next.On("click", () => this.step(1));
            close.On("click", () => this.close());
            nav.Append(this.prev, this.next, close);
            head.Append(this.title, nav);
            var body = this.el("div", "char-body");
            // left: portrait, bars, stats, skills
            var left = this.el("div", "char-left");
            this.face = (CanvasEl)Dom.El("canvas");
            this.face.AddToClassList("char-face");
            this.face.SetTitle("Right-click: use the hand item on this hero");
            this.face.On("contextmenu", (DomEvent @event) => { @event.preventDefault(); var e = this.getEngine(); e.queueAsync(() => e.uiUseHandOn(this.c)); });
            this.hp = this.bar("hp"); this.mp = this.bar("mp");
            this.stats = this.el("table", "char-stats");
            this.skills = this.el("div", "char-skills");
            this.status = this.el("div", "char-status");
            left.Append(this.face, this.hp.bar, this.mp.bar, this.stats, this.skills, this.status);
            // middle: equipment
            var mid = this.el("div", "char-mid");
            mid.Append(this.el("h3", "", "Equipment"));
            this.equip = this.el("div", "paperdoll");
            // innerHTML: <svg class="doll" viewBox="0 0 100 100" aria-hidden="true"><ellipse .../><path .../></svg>
            var doll = Dom.El("svg");
            doll.SetAttr("class", "doll");
            // the box's own proportions (the doll is 50% x 88% of a 1 : 1.3 box), so the figure fills it
            doll.SetAttr("viewBox", "0 0 44 100");
            doll.SetAttr("aria-hidden", "true");
            this.equip.ReplaceChildren(Doll(false));
            mid.Append(this.equip);
            var actions = this.el("div", "actions");
            this.best = this.el("button", "", "Equip best from inventory");
            this.best.SetAttr("type", "button");
            this.best.On("click", () => { if (onEquipBest != null) onEquipBest(this.c); else { var e = this.getEngine(); e.queueAsync(() => e.uiEquipBest(this.c)); } });
            this.use = this.el("button", "", "Use hand item");
            this.use.SetAttr("type", "button");
            this.use.On("click", () => { var e = this.getEngine(); e.queueAsync(() => e.uiUseHandOn(this.c)); });
            actions.Append(this.best, this.use);
            mid.Append(actions, this.el("p", "panel-note", "Click a slot to pick a fitting item from the inventory (or to take the worn item off); with an item in hand, clicking swaps it in. Items can also be dragged from the inventory (I) onto a slot."));
            // right: quick spell
            var right = this.el("div", "char-right");
            right.Append(this.el("h3", "", "Quick spell"));
            this.spells = this.el("div", "char-spells");
            right.Append(this.spells, this.el("p", "panel-note", "Pressing C (or Cast) goes through the heroes in turn and each casts the spell chosen here. ★ marks the hero's choice; ✓ marks the spells in the hero's Spells panel (up to 5)."));
            body.Append(left, mid, right);
            box.Append(head, body);
            this.root.Append(box);
            this.root.On("click", (DomEvent @event) => { if (@event.target == this.root) this.close(); });
        }

        static VisualElement Doll(bool fourArms)
        {
            var doll = Dom.El("svg");
            doll.SetAttr("class", "doll");
            // the box's own proportions (the doll is 50% x 88% of a 1 : 1.3 box), so the figure fills it
            doll.SetAttr("viewBox", "0 0 44 100");
            doll.SetAttr("aria-hidden", "true");
            doll.Append(SvgEl.Node("ellipse", ("cx", "22"), ("cy", "8.5"), ("rx", "5"), ("ry", "6")));
            if (fourArms) foreach (var part in BODY4) doll.Append(SvgEl.Node("path", ("d", part)));
            else doll.Append(SvgEl.Node("path", ("d", BODY)));
            doll.Append(SvgEl.Node("path", ("d", "M13.6 43.5H30.4M17 21.5L22 30 27 21.5M22 30V43.5"), ("fill", "none")));
            return doll;
        }

        // Slot picker: the inventory filtered to what fits this slot; click an item to equip it.
        public void openPicker(EquipmentSlot eq)
        {
            var engine = this.getEngine();
            this.closePicker();
            var pop = this.el("div", "slot-picker");
            var head = this.el("div", "slot-picker-head");
            head.Append(this.el("b", "", $"{Regex.Replace(eq.label, "\\.$", "")}: {(eq.item != 0 ? eq.name : "empty")}"));
            var close = this.el("button", "", "×"); close.SetAttr("type", "button"); close.SetTitle("Close");
            close.On("click", () => this.closePicker());
            head.Append(close);
            pop.Append(head);
            var list = this.el("div", "slot-picker-list");
            var palette = engine.uiPalette();
            var items = engine.uiItemsForSlot(this.c, eq.slot);
            if (eq.item != 0)
            {
                var off = this.el("button", "slot-picker-item unequip", "Take off (to inventory)");
                off.SetAttr("type", "button");
                off.On("click", () => { engine.queueAsync(() => engine.uiUnequip(this.c, eq.slot)); this.closePicker(); });
                list.Append(off);
            }
            if (items.Count == 0) list.Append(this.el("p", "panel-note", "Nothing in the inventory fits here."));
            foreach (var entry in items)
            {
                int inv = entry.inv, item = entry.item;
                var b = this.el("button", "slot-picker-item");
                b.SetAttr("type", "button");
                var icon = (CanvasEl)Dom.El("canvas");
                icon.width = 28; icon.height = 28; icon.AddToClassList("slot-icon");
                this.drawIcon(icon, engine.getItemIconShapePtr(item), palette);
                var info = engine.itemInfo(item);
                var worn = eq.item != 0 ? engine.itemInfo(eq.item) : null;
                string diff = worn != null ? string.Join(", ", new[]
                {
                    info.might - worn.might != 0 ? $"{(info.might - worn.might > 0 ? "+" : "")}{info.might - worn.might} might" : "",
                    info.protection - worn.protection != 0 ? $"{(info.protection - worn.protection > 0 ? "+" : "")}{info.protection - worn.protection} prot" : "",
                }.Where(s => s.Length > 0)) : "";
                var text = Dom.El("span", "slot-picker-text", null, true);
                text.Append(this.el("span", "slot-picker-name", info.name), this.el("small", "", $"{(info.might != 0 ? $"might {info.might} " : "")}{(info.protection != 0 ? $"prot {info.protection}" : "")}{(diff.Length > 0 ? $" ({diff})" : "")}"));
                b.Append(icon, text);
                b.SetTitle(engine.itemTooltip(item, this.c)); CssTooltip.MarkItem(b, item, this.c);
                b.On("click", () => { engine.queueAsync(() => engine.uiDropInventoryOn(inv, this.c, eq.slot)); this.closePicker(); this.key = ""; this.faceFrame = -1; });
                list.Append(b);
            }
            pop.Append(list);
            this.equip.Append(pop);
            this.picker = pop;
        }

        public void closePicker() { if (this.picker != null) { this.picker.RemoveFromHierarchy(); this.picker = null; } }

        Bar bar(string kind)
        {
            var bar = this.el("div", $"ui-bar {kind} char-bar");
            var fill = this.el("div", "ui-fill");
            var text = this.el("span", "ui-bar-text");
            bar.Append(fill, text);
            return new Bar { bar = bar, fill = fill, text = text };
        }

        public void open(int c)
        {
            var engine = this.getEngine();
            if (engine == null || (engine.characters[c].flags & 1) == 0) return;
            this.c = c;
            this.key = ""; this.faceFrame = -1;
            this.root.SetHidden(false);
            this.update(true);
        }

        public void close() { this.closePicker(); this.root.SetHidden(true); this.c = -1; }
        public bool isOpen => !this.root.IsHidden();

        public void step(int dir)
        {
            var engine = this.getEngine();
            for (int j = 1; j <= 4; j += 1)
            {
                int n = (this.c + dir * j + 8) % 4;
                if ((engine.characters[n].flags & 1) != 0) { this.open(n); engine.uiSelectCharacter(n); return; }
            }
        }

        public void update(bool force = false)
        {
            var engine = this.getEngine();
            if (engine == null || this.root.IsHidden() || this.c < 0) return;
            var info = engine.uiCharacterInfo(this.c);
            if (info == null) { this.close(); return; }
            var ch = engine.characters[this.c];
            var table = this.quickSpells();
            JsonNode qs = table != null && table.TryGetPropertyValue(ch.id.ToString(CultureInfo.InvariantCulture), out var own) ? own : null;
            // the face animates on its own: a blink redraws the face, not the whole screen
            var palette = engine.uiPalette();
            var faces = this.c < engine.characterFaceShapes.Length ? engine.characterFaceShapes[this.c] : null;
            var shape = faces != null ? faces[ch.curFaceFrame < 7 ? ch.curFaceFrame : 0] : null;
            if (shape != null && (force || ch.curFaceFrame != this.faceFrame)) { this.faceFrame = ch.curFaceFrame; this.face.width = shape.width; this.face.height = shape.height; this.drawIcon(this.face, shape, palette); }
            // health and magic tick up while the screen is open: their bars are set on every call, and the rest
            // (stats, gear, the spell sliders - one could be in a drag) is rebuilt only when something else changes
            this.hp.fill.SetStyle("width", $"{JsRound((100.0 * info.hp) / Math.Max(1, info.hpMax))}%");
            this.hp.text.SetText($"Health {info.hp} / {info.hpMax}");
            this.mp.fill.SetStyle("width", $"{JsRound((100.0 * info.mp) / Math.Max(1, info.mpMax))}%");
            this.mp.text.SetText($"Magic {info.mp} / {info.mpMax}");
            int hpNow = info.hp, mpNow = info.mp;
            var cooldownNow = info.cooldown;
            info.hp = info.mp = 0;
            info.cooldown = null;   // the swing's recovery: it changes every frame after an attack and the screen does not show it
            string key = JsonSerializer.Serialize(new object[] { info, engine.itemInHand, engine.availableSpells.Select(s => (int)s).ToArray(), qs, new[] { 0, 1, 2, 3, 4, 5, 6 }.Select(s => this.powerFor(s, ch.id)).ToArray() }, KeyJson);
            info.hp = hpNow; info.mp = mpNow; info.cooldown = cooldownNow;
            if (!force && key == this.key) return;
            this.key = key;
            bool onlySpells = this.spellsOnly;
            this.spellsOnly = false;
            // a spell clicked: only the spell list is redrawn (the whole screen was, and flashed)
            if (!onlySpells)
            {
                this.title.SetText(info.name);
                this.stats.ReplaceChildren(new (string, string)[] { ("Might", S(info.might)), ("Protection", S(info.protection)), ("Last hit dealt", info.weaponHit != 0 ? S(info.weaponHit) : "-"), ("Last damage taken", info.damageSuffered != 0 ? S(info.damageSuffered) : "-"), ("Crowns (party)", S(engine.credits)) }
                    .Select(kv => { var tr = this.el("tr"); tr.Append(this.el("th", "", kv.Item1), this.el("td", "", kv.Item2)); return tr; }).ToList());
                this.skills.ReplaceChildren(SKILLS.Select(kl =>
                {
                    var (k, label) = kl;
                    var sk = k == "fighter" ? info.skills.fighter : k == "rogue" ? info.skills.rogue : info.skills.mage;
                    var row = this.el("div", "char-skill");
                    var bar = this.el("div", "ui-bar xp");
                    var fill = this.el("div", "ui-fill");
                    int prevNeed = sk.level > 1 ? engine.@static.ExpRequirements[sk.level - 1] : 0;
                    fill.SetStyle("width", $"{Math.Max(0, Math.Min(100, JsRound((100.0 * (sk.points - prevNeed)) / Math.Max(1, sk.next - prevNeed))))}%");
                    bar.Append(fill);
                    row.Append(this.el("span", "char-skill-name", $"{label} {sk.level}"), bar, this.el("span", "char-skill-xp", $"{sk.points} / {sk.next} xp"));
                    return row;
                }).ToList());
                var st = new List<string>();
                if (info.status.poisoned) st.Add("Poisoned");
                if (info.status.paralyzed) st.Add("Paralyzed");
                if (info.status.asleep) st.Add("Asleep");
                if (info.status.attacking) st.Add("Attacking");
                if ((ch.flags & 8) != 0) st.Add("Unconscious");
                this.status.SetText(st.Count > 0 ? $"Status: {string.Join(", ", st)}" : "Status: fine");
                // equipment
                foreach (var old in this.equip.QAll(".char-slot")) old.RemoveFromHierarchy();
                // a second pair of hands (slots 2 and 3): the four-armed figure, and the slots placed for it
                bool four = info.equipment.Any(e => e.slot == 2 || e.slot == 3);
                if (four != this.fourArms)
                {
                    this.fourArms = four;
                    var oldDoll = this.equip.Q(className: "doll");
                    var doll = Doll(four);
                    if (oldDoll != null) { this.equip.Insert(this.equip.IndexOf(oldDoll), doll); oldDoll.RemoveFromHierarchy(); }
                    else this.equip.Insert(0, doll);
                    CssLayout.Touch(doll);
                }
                var layout = four ? DOLL4 : DOLL;
                this.equip.Append(info.equipment.Select(eq =>
                {
                    var slot = this.el("button", "char-slot");
                    slot.SetAttr("type", "button");
                    var pos = layout.TryGetValue(eq.slot, out var p) ? p : new[] { 50, 50 };
                    slot.SetStyle("left", $"{pos[0]}%");
                    slot.SetStyle("top", $"{pos[1]}%");
                    var icon = (CanvasEl)Dom.El("canvas");
                    icon.width = 32; icon.height = 32; icon.AddToClassList("slot-icon");
                    this.drawIcon(icon, eq.item != 0 ? engine.getItemIconShapePtr(eq.item) : null, palette);
                    var label = this.el("span", "char-slot-label", Regex.Replace(Regex.Replace(eq.label, "\\.$", ""), " hand$", "", RegexOptions.IgnoreCase));
                    var name = this.el("span", "char-slot-name", eq.item != 0 ? eq.name : "—");
                    slot.Append(icon, label, name);
                    if (eq.item != 0) slot.AddToClassList("filled");
                    slot.SetTitle($"{eq.label}: {(eq.item != 0 ? engine.itemTooltip(eq.item, this.c) : "empty")}"); CssTooltip.MarkItem(slot, eq.item, this.c, worn: true);
                    slot.On("click", () => { if (engine.itemInHand != 0) engine.queueAsync(() => engine.uiEquipSlot(this.c, eq.slot)); else this.openPicker(eq); });
                    slot.On("dragover", (DomEvent @event) => { if (@event.dataTransfer.HasType("text/x-inventory-slot")) { @event.preventDefault(); slot.AddToClassList("drop"); } });
                    slot.On("dragleave", () => slot.RemoveFromClassList("drop"));
                    slot.On("drop", (DomEvent @event) =>
                    {
                        @event.preventDefault(); slot.RemoveFromClassList("drop");
                        double from = JsNumber(@event.dataTransfer.getData("text/x-inventory-slot"));
                        if (IsInteger(from)) engine.queueAsync(() => engine.uiDropInventoryOn((int)from, this.c, eq.slot));
                    });
                    return slot;
                }).ToList());
                this.use.SetDisabled(engine.itemInHand == 0);
            }
            // quick spell per hero: the rows are built once for a hero and the spells known, then only refreshed (a
            // rebuilt list drew a frame before its spacing was applied and the screen flickered on every click)
            renderSpells(engine, ch, info, qs);
        }

        string spellRowsKey;
        readonly List<(int slot, VisualElement row, VisualElement show, VisualElement label, SpellSlider slider, int max)> spellRows =
            new List<(int, VisualElement, VisualElement, VisualElement, SpellSlider, int)>();
        VisualElement spellNone;

        void renderSpells(LandsOfLore engine, Character ch, CharacterInfo info, JsonNode qs)
        {
            var available = engine.availableSpells.Select(s => (int)s).ToArray();
            bool showToggles = spellShown != null && Dom.document.ClassListContains("theme-fantasy");
            string rowsKey = $"{ch.id}|{string.Join(",", available)}|{info.mpMax}|{showToggles}";
            JsonNode currentQs() { var t = this.quickSpells(); return t != null && t.TryGetPropertyValue(ch.id.ToString(CultureInfo.InvariantCulture), out var own) ? own : null; }
            if (rowsKey != spellRowsKey)
            {
                spellRowsKey = rowsKey;
                spellRows.Clear();
                this.spells.Clear();
                for (int slot = 0; slot < available.Length; slot += 1)
                {
                    int spell = available[slot];
                    if (spell == -1) continue;
                    var props = spell >= 0 && spell < engine.@static.SpellProperties.Length ? engine.@static.SpellProperties[spell] : null;
                    if (props == null) continue; // a spell this build no longer knows
                    int thisSlot = slot;
                    var row = this.el("div", "char-spell");
                    // the spells the port adds have no entry in the language file: ask the engine for the name
                    // and for the icon key, or they all come out as whatever string id 0 happens to be
                    var extra = engine.extraSpellAt(spell);
                    object art = extra != null ? (object)extra.icon : spell;
                    string name = engine.spellName(spell);
                    var pick = this.el("button", "spell-pick");
                    pick.SetAttr("type", "button");
                    pick.Append(SpellWidget.spellIcon(art, 22));
                    pick.SetTitle($"Make {name} this hero's quick spell");
                    int maxLevel = -1;
                    for (int level = 0; level < 4; level += 1) if (props.mpRequired[level] <= info.mpMax) maxLevel = level;
                    int maxFor = Math.Max(0, maxLevel);
                    pick.On("click", () =>
                    {
                        var q = currentQs();
                        bool chosen = q != null && (int)q["slot"] == thisSlot;
                        this.setQuickSpell(ch.id, thisSlot, chosen ? (int)q["level"] : Math.Min(this.powerFor(thisSlot, ch.id), maxFor));
                        this.spellsOnly = true; this.key = ""; this.update();
                    });
                    var label = this.el("span", "spell-name", name);
                    var slider = SpellWidget.createSpellSlider(art, Math.Min(this.powerFor(thisSlot, ch.id), maxFor), maxLevel,
                        (int level) => { this.setQuickSpell(ch.id, thisSlot, level); this.spellsOnly = true; this.key = ""; this.update(); },
                        $"{name}: MP {string.Join(" / ", props.mpRequired)} (this hero has {info.mpMax})");
                    row.Append(pick, label, slider.root);
                    VisualElement show = null;
                    if (showToggles)
                    {
                        show = this.el("button", "spell-show");
                        show.SetAttr("type", "button");
                        show.On("click", () => { toggleSpellShown?.Invoke(ch.id, thisSlot); this.spellsOnly = true; this.key = ""; this.update(); });
                        row.Insert(0, show);
                    }
                    this.spells.Append(row);
                    spellRows.Add((thisSlot, row, show, label, slider, maxLevel));
                }
                if (spellRows.Count == 0) this.spells.Append(this.el("p", "panel-note", "No spells known yet."));
                spellNone = this.el("button", "char-spell-none");
                spellNone.SetAttr("type", "button");
                spellNone.On("click", () => { this.setQuickSpell(ch.id, -1, 0); this.spellsOnly = true; this.key = ""; this.update(); });
                this.spells.Append(spellNone);
            }
            // the state, in place
            foreach (var (slot, row, show, label, slider, max) in spellRows)
            {
                bool chosen = qs != null && (int)qs["slot"] == slot;
                row.ClassToggle("chosen", chosen);
                string name = engine.spellName(available[slot]);
                label.SetText($"{(chosen ? "★ " : "")}{name}");
                int level = chosen ? (int)qs["level"] : Math.Min(this.powerFor(slot, ch.id), Math.Max(0, max));
                if (slider.level != level) slider.set(level, max);
                if (show != null)
                {
                    bool shown = spellShown(ch.id, slot);
                    show.ClassToggle("on", shown);
                    show.SetText(shown ? "✓" : "");
                    show.SetTitle(shown ? $"In {ch.name}'s Spells panel: click to take it off" : $"Not in {ch.name}'s Spells panel: click to put it there (up to 5)");
                }
            }
            spellNone.SetText(qs != null ? "Clear this hero's quick spell" : "No quick spell chosen (uses the party default)");
            spellNone.SetDisabled(qs == null);
        }

        // ---- JS helpers ----
        /// <summary>Math.round (half up).</summary>
        static int JsRound(double v) => (int)Math.Floor(v + 0.5);
        static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
        /// <summary>Number(s): "" -> 0, not a number -> NaN.</summary>
        static double JsNumber(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }
        static bool IsInteger(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Floor(v) == v;
    }
}
