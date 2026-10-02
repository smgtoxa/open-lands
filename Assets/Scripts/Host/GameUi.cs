// src/platform/game-ui.mjs, ported 1:1 (docs/port/HOST.md). C# 9.
// New in-page interface: message/dialogue panel, party cards, 10-slot hotbar. It talks to the engine
// through the HostUiMixin (src/game/host-ui.mjs) and reads engine state directly for display.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lol;
using UnityEngine.UIElements;
using LolButton = Lol.Button;
using LolInputEvent = Lol.InputEvent;

namespace LolHost
{
    public sealed class GameUi
    {
        const string HOTBAR_KEY = "lol.hotbar";
        // Hotbar entries: -1 empty, 0-47 an inventory slot, SPELL_BASE + spellSlot * 10 + level a spell.
        public const int SPELL_BASE = 1000;
        static bool isSpell(int v) => v >= SPELL_BASE;
        const int HOTBAR_SIZE = 10;
        const int LOG_LIMIT = 8;
        static readonly string[] MESSAGE_KINDS = { "", "warn", "alert", "note", "info" };

        /// <summary>A log entry: { text, cls }.</summary>
        public sealed class LogEntry { public string text, cls; }
        /// <summary>The last priced item (main sets it from the getItemPrice hook): { price, selling, item, type, at }.</summary>
        public sealed class TradeInfo { public int price; public bool selling; public int item, type; public double at; }
        /// <summary>getQuickSpell's result: { spell, level, name }.</summary>
        public sealed class QuickSpell { public int spell, level; public string name; }
        /// <summary>spellLabel's result: { name, level }.</summary>
        public sealed class SpellLabel { public string name; public int level; }
        public sealed class Slot { public VisualElement button; public CanvasEl canvas; }
        public sealed class Card
        {
            public VisualElement root; public CanvasEl face;
            public VisualElement name, hp, hpText, mp, mpText, stats, skills, status, attack, quick, cooldown, weapon;
            /// <summary>What the skill rows show: they are rebuilt only when it changes (the same DOM as rebuilding
            /// every update, without re-laying-out fresh rows each frame).</summary>
            public string skillsKey, quickKey;
        }

        public readonly VisualElement root;
        public readonly Action<CanvasEl, Shape, byte[]> drawIcon;
        public readonly Func<LandsOfLore> getEngine;
        public readonly Func<bool> isPlaying;
        readonly LocalStorage localStorage;

        public int[] hotbar;
        public int[] hotbarKind;
        public List<LogEntry> log;
        public string[] choiceLabels;
        public string waitLabel;
        public string partyKey;
        public string hotbarKey;
        public VisualElement messages, logBox, prompt, party, bar;
        public string logFilter;
        public List<Slot> slots;
        /// <summary>JS `this.cards = []`, filled 0..3 by the first updateParty: null until then.</summary>
        public Card[] cards;
        public bool targeting;
        public TradeInfo trade;
        /// <summary>"yes" | "no" | "continue" | null</summary>
        public string autoAnswer, lastAutoAnswer;
        public VisualElement mirror;
        public Action onPrompt;
        public Action<int> onPortrait;
        /// <summary>Unity build: "Equip best" on a card (Web.equipBest: the swap, with a notice of what changed)</summary>
        public Action<int> onEquipBest;
        public Action<int, int> onSwap;
        public Func<int, QuickSpell> getQuickSpell;

        public GameUi(VisualElement root, Action<CanvasEl, Shape, byte[]> drawIcon, Func<LandsOfLore> getEngine, Func<bool> isPlaying, LocalStorage storage)
        {
            this.root = root;
            this.drawIcon = drawIcon;
            this.getEngine = getEngine;
            this.isPlaying = isPlaying;
            localStorage = storage;
            hotbar = Enumerable.Repeat(-1, HOTBAR_SIZE).ToArray();
            hotbarKind = Enumerable.Repeat(-1, HOTBAR_SIZE).ToArray(); // what each pin is a pile of, so it can follow it
            readInts($"{HOTBAR_KEY}.kind", hotbarKind);
            readInts(HOTBAR_KEY, hotbar);
            log = new List<LogEntry>();
            choiceLabels = null;
            waitLabel = null;
            partyKey = "";
            hotbarKey = "";
            build();
        }

        /// <summary>try { JSON.parse(localStorage.getItem(key)).forEach((v, i) => { if (i &lt; HOTBAR_SIZE) into[i] = Number.isInteger(v) ? v : -1; }) } catch { }</summary>
        void readInts(string key, int[] into)
        {
            try
            {
                var s = localStorage.getItem(key);
                if (s == null || !(JsonNode.Parse(s) is JsonArray a)) return;
                for (int i = 0; i < a.Count; i += 1)
                {
                    if (i >= HOTBAR_SIZE) continue;
                    into[i] = a[i] is JsonValue v && v.TryGetValue(out double d) && Math.Floor(d) == d && !double.IsInfinity(d) ? (int)d : -1;
                }
            }
            catch (Exception) { /* ignore */ }
        }

        static string json(int[] a) => "[" + string.Join(",", a) + "]";

        /// <summary>JS Number(string): "" is 0, anything unparsable NaN.</summary>
        static double Number(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        static bool isInteger(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d;

        /// <summary>A JS number in a template string.</summary>
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>engine.inventory[s] (undefined, i.e. 0, outside the array).</summary>
        static int inv(LandsOfLore engine, int s) => s >= 0 && s < engine.inventory.Length ? engine.inventory[s] : 0;

        VisualElement el(string tag, string className = null, string text = null) => Dom.El(tag, className, text);

        static VisualElement closestButton(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e is UnityEngine.UIElements.Button || (e.userData is DomData d && d.tag == "button")) return e;
            return null;
        }

        void build()
        {
            var root = this.root;
            root.ReplaceChildren();
            messages = el("div", "ui-messages");
            // log filter tabs: everything, talk, combat, system
            logFilter = "all";
            var filters = el("div", "ui-log-filters");
            foreach (var (key, label) in new[] { ("all", "All"), ("say", "Talk"), ("combat", "Combat"), ("system", "System") })
            {
                var b = el("button", "ui-log-filter" + (key == "all" ? " on" : ""), label); b.SetAttr("type", "button");
                b.On("click", () => { logFilter = key; foreach (var x in filters.Children()) x.ClassToggle("on", x == b); renderLog(); b.BlurEl(); });
                filters.Append(b);
            }
            messages.Append(filters);
            logBox = el("div", "ui-log");
            logBox.SetAttr("aria-live", "polite");
            logBox.SetAttr("role", "log");
            prompt = el("div", "ui-prompt");
            messages.Append(logBox, prompt);
            party = el("div", "ui-party");
            bar = el("div", "ui-hotbar");
            slots = new List<Slot>();
            for (int i0 = 0; i0 < HOTBAR_SIZE; i0 += 1)
            {
                int i = i0;
                var slot = el("button", "ui-slot");
                slot.SetAttr("type", "button");
                var canvas = (CanvasEl)Dom.El("canvas");
                canvas.width = 24;
                canvas.height = 24;
                canvas.SetClassName("slot-icon");
                var key = el("span", "ui-key", ((i + 1) % 10).ToString(CultureInfo.InvariantCulture));
                slot.Append(canvas, key);
                slot.On("click", (DomEvent @event) => { if (@event.shiftKey) assign(i, -1); else slotClick(i); slot.BlurEl(); });
                slot.On("contextmenu", (DomEvent @event) => { @event.preventDefault(); slotUse(i); });
                slot.On("dragover", (DomEvent @event) => { @event.preventDefault(); slot.AddToClassList("drop"); });
                slot.On("dragleave", () => slot.RemoveFromClassList("drop"));
                // A filled slot can be dragged: onto another slot to move it, onto the inventory to unpin it,
                // or (items) onto a hero like any inventory item.
                slot.On("dragstart", (DomEvent @event) =>
                {
                    int v = hotbar[i];
                    if (v < 0) { @event.preventDefault(); return; }
                    @event.dataTransfer.setData("text/x-hotbar", i.ToString(CultureInfo.InvariantCulture));
                    if (isSpell(v)) @event.dataTransfer.setData("text/x-spell", $"{(v - SPELL_BASE) / 10}:{(v - SPELL_BASE) % 10}");
                    else @event.dataTransfer.setData("text/x-inventory-slot", v.ToString(CultureInfo.InvariantCulture));
                    @event.dataTransfer.effectAllowed = "copyMove";
                });
                slot.On("drop", (DomEvent @event) =>
                {
                    @event.preventDefault();
                    slot.RemoveFromClassList("drop");
                    string fromBar = @event.dataTransfer.getData("text/x-hotbar");
                    if (fromBar != "") { int f = (int)Number(fromBar); if (f != i) { int v = hotbar[f]; hotbar[f] = hotbar[i]; assign(i, v); } return; }
                    string spell = @event.dataTransfer.getData("text/x-spell");
                    if (!string.IsNullOrEmpty(spell)) { var p = spell.Split(':').Select(Number).ToArray(); assign(i, (int)(SPELL_BASE + p[0] * 10 + p[1])); return; }
                    double from = Number(@event.dataTransfer.getData("text/x-inventory-slot"));
                    if (isInteger(from)) assign(i, (int)from);
                });
                bar.Append(slot);
                slots.Add(new Slot { button = slot, canvas = canvas });
            }
            root.Append(messages, party, bar);
            cards = new Card[4];
        }

        // ---- engine callbacks (see HostUiMixin) ----
        /// <summary>kind: a class name (string) or a MESSAGE_KINDS index (number).</summary>
        public void message(string text, object kind = null)
        {
            string k = kind is string s ? s : "";
            if (!(kind is string) && kind is IConvertible c) { int n = Convert.ToInt32(c, CultureInfo.InvariantCulture); k = n >= 0 && n < MESSAGE_KINDS.Length ? MESSAGE_KINDS[n] : ""; }
            push(new LogEntry { text = text, cls = $"ui-msg {k}" });
        }

        // Flash a party card and float the damage number over it.
        // "Cast a spell on who?": highlight the cards until a target is picked.
        // A click on a hero card: answers a pending spell prompt, otherwise selects the hero. Returns
        // true when the click went to a spell. The selection is forced through even if the engine's own
        // path bails for any reason.
        public bool pickHero(int c)
        {
            var engine = getEngine();
            if (engine == null || (engine.characters[c].flags & 1) == 0) return false;
            if (engine.uiTargetPending()) { engine.uiCastOn(c); return true; }
            engine.uiSelectCharacter(c);
            if (engine.selectedCharacter != c)
            { // something refused: select anyway and redraw
                engine.uiClearTarget();
                engine.selectionPinned = c;
                engine.selectedCharacter = c;
                engine.gui_drawAllCharPortraitsWithStats();
                partyKey = "";
            }
            return false;
        }

        public void targetMode(bool on)
        {
            var engine = getEngine();
            if (on && engine != null && !engine.uiTargetPending()) on = false; // not a real prompt
            targeting = on;
            party.ClassToggle("targeting", on);
            foreach (var card in cards) { if (card != null) card.root.SetTitle(on ? "Click to cast the spell on this hero" : ""); }
            renderPrompt();
        }

        public void cardHit(int c, int damage)
        {
            var card = c >= 0 && c < cards.Length ? cards[c] : null;
            if (card == null) return;
            card.root.RemoveFromClassList("hit");
            // void card.root.offsetWidth: restarts the CSS animation in a browser; nothing to do here
            card.root.AddToClassList("hit");
            var num = el("span", "fx-float fx-dmg", $"-{damage}");
            card.root.Append(num);
            num.On("animationend", () => num.RemoveFromHierarchy());
        }

        public void dialogue(string text)
        {
            push(new LogEntry { text = text, cls = "ui-say" });
        }

        public void choices(string[] labels)
        {
            choiceLabels = labels;
            renderPrompt();
            if (autoAnswer != null)
            {
                var engine = getEngine();
                string want = autoAnswer; // "yes" | "no" | "continue"
                var re = new Regex(want == "no" ? "^no" : "^yes", RegexOptions.IgnoreCase);
                int idx = labels != null && labels.Length > 0 ? Array.FindIndex(labels, (l) => re.IsMatch(l ?? "")) : -1;
                // setTimeout(fn, 120): the element's scheduler, the handler run through the page's event loop (Dom.Invoke)
                root.schedule.Execute(() => Dom.Invoke(() =>
                {
                    if (!ReferenceEquals(choiceLabels, labels)) return;
                    if (idx >= 0 && want != "continue") { lastAutoAnswer = want; engine.uiChoose(idx); }
                    else if (labels == null || labels.Length <= 1) engine.uiChoose(labels != null && labels.Length > 0 ? 0 : -1); // "OK" / continue
                })).StartingIn(120);
            }
        }

        static ItemInfo withProp(ItemInfo i, int prop) => i == null
            ? new ItemInfo { prop = prop }
            : new ItemInfo { name = i.name, might = i.might, protection = i.protection, slots = i.slots, usable = i.usable, skill = i.skill, prop = prop, condition = i.condition };

        // What is being bought or sold: icon, stats, who can wear it, and the change against what the
        // selected hero has in that slot. Built from the last priced item (see the getItemPrice hook).
        public VisualElement renderTradeCard(LandsOfLore engine, VisualElement box = null)
        {
            box ??= prompt;
            var t = trade;
            if (t == null || Web.now() - t.at > 60000 || !choiceLabels.Any((l) => Regex.IsMatch(l ?? "", "yes", RegexOptions.IgnoreCase))) return null;
            int item = t.item; var info = item != 0 ? engine.itemInfo(item) : null;
            if (info == null && t.type != 0) { item = 0; info = engine.itemInfoForProperty(t.type); }
            if (info == null)
            { // wares offered in conversation: match an item name in the last dialogue line
                var last = Enumerable.Reverse(log).FirstOrDefault((e) => Regex.IsMatch(e.text, "crown|credit", RegexOptions.IgnoreCase));
                if (last != null) { int prop = engine.itemProperties.FindIndex((p) => { string n = engine.getLangString(p.nameStringId); return !string.IsNullOrEmpty(n) && n.Length > 2 && last.text.ToLowerInvariant().Contains(n.ToLowerInvariant()); }); if (prop > 0) { item = 0; info = withProp(engine.itemInfoForProperty(prop), prop); } }
            }
            if (info == null) return null;
            var card = el("div", "trade-card");
            var icon = (CanvasEl)Dom.El("canvas"); icon.width = 24; icon.height = 24; icon.SetClassName("slot-icon");
            var shape = item != 0 ? engine.getItemIconShapePtr(item) : engine.itemIconShapes[engine.itemProperties[info.prop].shpIndex];
            drawIcon(icon, shape, engine.uiPalette());
            var head = el("div", "trade-head"); head.Append(icon, el("b", "", info.name), el("span", "trade-price", $"{(t.selling ? "sells for" : "costs")} {t.price} · you have {engine.credits}"));
            var rows = el("div", "trade-rows");
            void row(string k, string v) { if (!string.IsNullOrEmpty(v)) rows.Append(el("span", "k", k), el("span", "v", v)); }
            if (!info.usable) { row("Might", info.might != 0 ? info.might.ToString(CultureInfo.InvariantCulture) : ""); row("Protection", info.protection != 0 ? info.protection.ToString(CultureInfo.InvariantCulture) : ""); }
            row("Fits", string.Join(", ", info.slots));
            row("Use", engine.itemUse(info.name, info.usable, info.slots.Count != 0));
            string wearers = string.Join(", ", engine.characters.Where((ch, i) => (ch.flags & 1) != 0 && engine.uiSlotsForProperty(info.prop, i).Count != 0).Select((ch) => ch.name));
            if (info.slots.Count != 0) row("Who can wear it", wearers.Length > 0 ? wearers : "nobody in the party");
            // change vs the selected hero's current item in the first matching slot
            int c = engine.selectedCharacter; var slots = engine.uiSlotsForProperty(info.prop, c);
            if (slots.Count != 0 && !info.usable)
            {
                int cur = engine.characters[c].items[slots[0]]; var ci = cur != 0 ? engine.itemInfo(cur) : null;
                string d(int a, int b) { int n = a - b; return n != 0 ? $" ({(n > 0 ? "+" : "")}{n})" : ""; }
                row($"For {engine.characters[c].name}", ci != null ? $"vs {ci.name}: might {info.might}{d(info.might, ci.might)}, prot {info.protection}{d(info.protection, ci.protection)}" : "slot is empty");
            }
            if (box == prompt)
            { // the log panel is small: one summary line instead of the rows
                card.AddToClassList("compact");
                var bits = new List<string>(); for (int i = 0; i < rows.childCount; i += 2) bits.Add($"{rows[i].GetText()}: {rows[i + 1].GetText()}");
                card.Append(head, el("div", "trade-summary", string.Join(" · ", bits)));
            }
            else card.Append(head, rows);
            box.Append(card);
            return card;
        }

        public void waitingFor(bool on, string label = null)
        {
            waitLabel = on ? (string.IsNullOrEmpty(label) ? "More" : label) : null;
            renderPrompt();
        }

        public void push(LogEntry entry)
        {
            string clean = Regex.Replace(Regex.Replace(entry.text ?? "", "[\x01-\x1f]", " "), @"\s+", " ").Trim();
            if (clean.Length == 0) return;
            log.Add(new LogEntry { cls = entry.cls, text = clean });
            if (log.Count > LOG_LIMIT) log.RemoveAt(0);
            renderLog();
        }

        public bool logMatches(LogEntry e)
        {
            if (logFilter == "all") return true;
            if (logFilter == "say") return Regex.IsMatch(e.cls, "ui-say");
            if (logFilter == "combat") return Regex.IsMatch(e.cls, "combat|warn|alert");
            return !Regex.IsMatch(e.cls, "ui-say|combat");
        }

        public void renderLog()
        {
            var shown = log.Where((e) => logMatches(e)).ToList();
            logBox.ReplaceChildren(shown.Select((e, i) => el("div", $"{e.cls}{(i == shown.Count - 1 ? " last" : "")}", e.text)).ToArray<object>());
            logBox.ScrollToBottom();
        }

        public void renderPrompt()
        {
            renderPromptInto(prompt);
            if (mirror != null) renderPromptInto(mirror);
            onPrompt?.Invoke();
        }

        public void renderPromptInto(VisualElement box)
        {
            var engine = getEngine();
            box.ReplaceChildren();
            if (engine == null) return;
            if (targeting && engine.uiTargetPending() && box == prompt)
            { // a spell waits for its hero: explicit buttons as well as the cards
                string name = engine.activeSpell != null ? engine.spellName(engine.activeSpell.spell) : "Spell";
                box.Append(el("span", "ui-target-label", $"{name} on whom?"));
                for (int i0 = 0; i0 < engine.characters.Length; i0 += 1) { int i = i0; var ch = engine.characters[i]; if ((ch.flags & 1) == 0) continue; var b = el("button", "ui-choice", ch.name); b.SetAttr("type", "button"); b.On("click", () => { engine.uiCastOn(i); b.BlurEl(); }); box.Append(b); }
                var cancel = el("button", "ui-choice skip", "Cancel"); cancel.SetAttr("type", "button"); cancel.SetTitle("Give the spell up (the mana comes back)"); cancel.On("click", () => { engine.uiCancelTarget(); cancel.BlurEl(); });
                box.Append(cancel);
                return;
            }
            if (choiceLabels != null && choiceLabels.Length > 0)
            {
                // With an item card the answer belongs inside it, as its footer: buttons hanging under the
                // card's border read as torn-off tabs.
                var card = renderTradeCard(engine, box);
                var answers = card != null && box == prompt ? el("div", "trade-choices") : box; // the trade window hides the card and shows it in its details pane
                for (int i0 = 0; i0 < choiceLabels.Length; i0 += 1)
                {
                    int i = i0; string label = choiceLabels[i];
                    var b = el("button", "ui-choice", $"{i + 1}. {(string.IsNullOrEmpty(label) ? "..." : label)}");
                    b.SetAttr("type", "button");
                    b.On("click", () => { engine.uiChoose(i); b.BlurEl(); });
                    answers.Append(b);
                }
                if (answers != box) card.Append(answers);
            }
            else if (choiceLabels != null && choiceLabels.Length == 0)
            {
                var b = el("button", "ui-choice", "Continue");
                b.SetAttr("type", "button");
                b.On("click", () => { engine.uiChoose(-1); b.BlurEl(); });
                box.Append(b);
            }
            if (waitLabel != null)
            {
                var b = el("button", "ui-choice more", $"{waitLabel} ▶");
                b.SetAttr("type", "button");
                b.On("click", () => { engine.events.Add(new LolInputEvent { type = "key", key = "Enter" }); b.BlurEl(); });
                box.Append(b);
            }
            if ((waitLabel != null || (choiceLabels != null && choiceLabels.Length <= 1)) /* && engine.skipCutscene: always there */ && box == prompt)
            {
                var b = el("button", "ui-choice skip", "Skip ⏭");
                b.SetAttr("type", "button");
                b.SetTitle("Skip the cutscene / dialogue up to the next choice (Esc)");
                b.On("click", () => { engine.skipCutscene(); b.BlurEl(); });
                box.Append(b);
            }
        }

        public void reset()
        {
            log = new List<LogEntry>();
            choiceLabels = null;
            waitLabel = null;
            logBox.ReplaceChildren();
            prompt.ReplaceChildren();
            partyKey = "";
            hotbarKey = "";
        }

        // ---- hotbar ----
        public void assign(int index, int inventorySlot)
        {
            // one inventory slot (or spell) may sit on the bar only once
            for (int i = 0; i < HOTBAR_SIZE; i += 1) if (inventorySlot >= 0 && hotbar[i] == inventorySlot) hotbar[i] = -1;
            hotbar[index] = inventorySlot;
            saveHotbar();
        }

        // A save was loaded: the bar belongs to that campaign, so read it again.
        public void reloadHotbar()
        {
            hotbar = Enumerable.Repeat(-1, HOTBAR_SIZE).ToArray();
            hotbarKind = Enumerable.Repeat(-1, HOTBAR_SIZE).ToArray();
            readInts(HOTBAR_KEY, hotbar);
            readInts($"{HOTBAR_KEY}.kind", hotbarKind);
            hotbarKey = "";
        }

        public void saveHotbar()
        {
            try
            {
                localStorage.setItem(HOTBAR_KEY, json(hotbar));
                localStorage.setItem($"{HOTBAR_KEY}.kind", json(hotbarKind ?? new int[0]));
            }
            catch (Exception) { /* ignore */ }
            hotbarKey = "";
        }

        // A pin points at an inventory slot, but what the player pinned is the *kind* of thing. Throw the
        // stone in that slot and the slot empties - so the pin moves to the next stone instead of going
        // blank and having to be set up again. This runs on every redraw, so it covers throwing,
        // dropping, selling and using alike, not just the one path that used to be patched up.
        public void followStacks(LandsOfLore engine)
        {
            if (hotbarKind == null) hotbarKind = Enumerable.Repeat(-1, HOTBAR_SIZE).ToArray();
            bool moved = false;
            for (int i = 0; i < HOTBAR_SIZE; i += 1)
            {
                int slot = hotbar[i];
                if (slot < 0 || isSpell(slot)) { hotbarKind[i] = -1; continue; }
                int item = inv(engine, slot);
                if (item != 0 && engine.itemsInPlay[item] != null) { hotbarKind[i] = engine.itemsInPlay[item].itemPropertyIndex; continue; }
                int kind = hotbarKind[i];
                if (kind < 0) continue;
                int next = -1;
                for (int j = 0; j < engine.inventory.Length && next < 0; j += 1)
                {
                    int other = engine.inventory[j];
                    if (other == 0 || hotbar.Contains(j)) continue;
                    if (engine.itemsInPlay[other] != null && engine.itemsInPlay[other].itemPropertyIndex == kind) next = j;
                }
                if (next >= 0) { hotbar[i] = next; moved = true; }
                else hotbarKind[i] = -1; // the last one is gone: the slot empties for real
            }
            if (moved) saveHotbar();
        }

        public int firstFreeHotbarSlot()
        {
            return Array.IndexOf(hotbar, -1);
        }

        public void slotClick(int index)
        {
            var engine = getEngine();
            int slot = hotbar[index];
            if (engine == null || !isPlaying()) return;
            if (isSpell(slot)) { castSlot(slot); return; }
            if (slot < 0)
            {
                // empty bar slot with an item in hand: store the hand item in a free inventory slot and pin it here
                if (engine.itemInHand == 0) return;
                int free = Array.FindIndex(engine.inventory, (it) => it == 0);
                if (free < 0) return;
                engine.queueAsync(async () => { await engine.inventorySlotClick(free); });
                assign(index, free);
                return;
            }
            engine.queueAsync(() => engine.inventorySlotClick(slot));
        }

        public void slotUse(int index)
        {
            var engine = getEngine();
            int slot = hotbar[index];
            if (engine == null || !isPlaying() || slot < 0) return;
            if (engine.awaitingSpellTarget) { message("Choose the hero to cast on first (click a hero card).", "system"); return; }
            if (isSpell(slot)) { castSlot(slot); return; }
            int item = inv(engine, slot);
            int prop = item != 0 ? engine.itemsInPlay[item].itemPropertyIndex : -1;
            engine.queueAsync(async () => { await engine.uiUseInventorySlot(slot, engine.selectedCharacter); if (prop >= 0) restack(engine, index, prop); });
        }

        public void castSlot(int value)
        {
            var engine = getEngine();
            if (engine.awaitingSpellTarget) { message("Choose the hero to cast on first (click a hero card).", "system"); return; }
            int spellSlot = (value - SPELL_BASE) / 10;
            int level = (value - SPELL_BASE) % 10;
            if (engine.availableSpells[spellSlot] == -1) return;
            engine.queueAsync(() => engine.quickCastSpell(engine.selectedCharacter, spellSlot, level));
        }

        public SpellLabel spellLabel(LandsOfLore engine, int value)
        {
            int spellSlot = (value - SPELL_BASE) / 10;
            if (spellSlot < 0 || spellSlot >= engine.availableSpells.Length) return null; // undefined
            int spell = engine.availableSpells[spellSlot];
            if (spell == -1) return null;
            string name = engine.spellName(spell);
            return new SpellLabel { name = name, level = ((value - SPELL_BASE) % 10) + 1 };
        }

        // ---- per-frame refresh ----
        public void update()
        {
            var engine = getEngine();
            if (engine == null) return;
            updateHotbar(engine);
            updateParty(engine);
        }

        public int sameKindCount(LandsOfLore engine, int item)
        {
            int prop = engine.itemsInPlay[item].itemPropertyIndex;
            int n = 0;
            foreach (int it in engine.inventory) if (it != 0 && engine.itemsInPlay[it].itemPropertyIndex == prop) n += 1;
            return n;
        }

        // The pinned slot emptied (the potion was used): move the pin to the next item of the same kind.
        public void restack(LandsOfLore engine, int index, int prop)
        {
            int slot = hotbar[index];
            if (slot < 0 || isSpell(slot) || inv(engine, slot) != 0) return;
            for (int i = 0; i < engine.inventory.Length; i += 1)
            {
                int it = engine.inventory[i];
                if (it != 0 && engine.itemsInPlay[it].itemPropertyIndex == prop && !hotbar.Contains(i)) { hotbar[index] = i; hotbarKey = ""; try { localStorage.setItem(HOTBAR_KEY, json(hotbar)); } catch (Exception) { /* ignore */ } return; }
            }
        }

        public void updateHotbar(LandsOfLore engine)
        {
            followStacks(engine);
            string key = $"{string.Join(",", hotbar)}|{string.Join(",", hotbar.Select((s) => s >= 0 && !isSpell(s) ? inv(engine, s) : 0))}|{engine.itemInHand}|{string.Join(",", engine.availableSpells)}";
            if (key == hotbarKey) return;
            hotbarKey = key;
            var palette = engine.uiPalette();
            for (int i = 0; i < slots.Count; i += 1)
            {
                var button = slots[i].button; var canvas = slots[i].canvas;
                int slot = hotbar[i];
                var spell = isSpell(slot) ? spellLabel(engine, slot) : null;
                var label = button.Q(".ui-slot-spell");
                if (spell != null)
                {
                    if (label == null) { label = Dom.El("span", "ui-slot-spell", null, true); button.Append(label); }
                    int spellIndex = engine.availableSpells[(slot - SPELL_BASE) / 10];
                    var extraSpell = engine.extraSpellAt(spellIndex);
                    label.ReplaceChildren(SpellWidget.spellIcon(extraSpell != null ? (object)extraSpell.icon : spellIndex, 22), $"{(spell.name.Length > 7 ? spell.name.Substring(0, 7) : spell.name)} {spell.level}");
                    canvas.SetHidden(true);
                    button.AddToClassList("assigned");
                    button.SetDraggable(true);
                    button.SetTitle($"{spell.name} (power {spell.level})\nClick, right-click or key {(i + 1) % 10}: cast with the selected hero. Shift-click: clear.");
                    continue;
                }
                if (label != null) label.RemoveFromHierarchy();
                canvas.SetHidden(false);
                int item = slot >= 0 ? inv(engine, slot) : 0;
                drawIcon(canvas, item != 0 ? engine.getItemIconShapePtr(item) : null, palette);
                // how many of the same kind the inventory holds (a stack): shown on the slot
                var count = button.Q(".ui-count");
                int same = item != 0 ? sameKindCount(engine, item) : 0;
                if (same > 1) { if (count == null) { count = el("span", "ui-count"); button.Append(count); } count.SetText(same.ToString(CultureInfo.InvariantCulture)); }
                else if (count != null) count.RemoveFromHierarchy();
                button.ClassToggle("assigned", slot >= 0);
                button.SetDraggable(slot >= 0);
                CssTooltip.MarkItem(button, item, -1);
                button.SetTitle(item != 0 ? $"{engine.itemTooltip(item)}\nClick: take / swap with hand. Right-click or key {(i + 1) % 10}: use on the selected hero. Shift-click: clear." : slot >= 0 ? $"Empty (inventory slot {slot + 1})" : "Empty. Drag an item here from the inventory, or click with an item in hand.");
            }
        }

        static readonly JsonSerializerOptions KeyJson = new JsonSerializerOptions { IncludeFields = true };

        public void updateParty(LandsOfLore engine)
        {
            var infos = new[] { 0, 1, 2, 3 }.Select((c) => engine.uiCharacterInfo(c)).ToArray();
            var palette = engine.uiPalette();
            // the palette is part of the key: faces drawn during a fade (frozen party, level scripts) would
            // otherwise stay black until something else on the card changed
            int palSum = 0; for (int i = 0; i < 768; i += 1) palSum = unchecked(palSum * 31 + palette[i]);
            string key = JsonSerializer.Serialize(infos, KeyJson) + $"|{string.Join(",", engine.characters.Select((ch) => $"{ch.curFaceFrame}:{ch.tempFaceFrame}"))}|{engine.itemInHand}|{JsonSerializer.Serialize(engine.characters.Select((ch, i) => getQuickSpell != null ? getQuickSpell(i) : null).ToArray(), KeyJson)}|{palSum}";
            if (key == partyKey) return;
            partyKey = key;
            for (int c = 0; c < infos.Length; c += 1)
            {
                var info = infos[c];
                var card = cards[c];
                if (card == null)
                {
                    card = buildCard(c);
                    cards[c] = card;
                    party.Append(card.root);
                }
                card.root.SetHidden(info == null);
                if (info == null) continue;
                var ch = engine.characters[c];
                int frm = (ch.flags & 0x1108) != 0 && ch.curFaceFrame < 7 ? 1 : ch.curFaceFrame;
                if (ch.hitPointsCur <= ch.hitPointsMax >> 1) frm += 14;
                var faces = engine.characterFaceShapes[c];
                var shape = faces != null && frm >= 0 && frm < faces.Length ? faces[frm] : null;
                if (shape != null)
                {
                    card.face.width = shape.width;
                    card.face.height = shape.height;
                    drawIcon(card.face, shape, palette);
                }
                card.root.ClassToggle("selected", info.selected);
                card.root.ClassToggle("poisoned", info.status.poisoned);
                card.root.ClassToggle("frozen", info.status.paralyzed);
                // the page tints the face with CSS filters (.ui-card.poisoned .ui-face: sepia + hue-rotate, green;
                // .frozen: pale ice blue), which USS cannot draw: the same look as a tint of the face picture
                var tint = info.status.paralyzed ? new UnityEngine.Color(0.70f, 0.88f, 1f) : info.status.poisoned ? new UnityEngine.Color(0.62f, 0.95f, 0.38f) : (UnityEngine.Color?)null;
                var had = card.face.style.unityBackgroundImageTintColor;
                if (tint == null) { if (had.keyword != StyleKeyword.Null) card.face.style.unityBackgroundImageTintColor = StyleKeyword.Null; }
                else if (had.keyword != StyleKeyword.Undefined || had.value != tint.Value) card.face.style.unityBackgroundImageTintColor = tint.Value;
                card.root.ClassToggle("busy", info.status.busy);
                // the name's text node (a "text" label, as Append makes them) before the weapon mark
                if (card.name.childCount > 0 && card.name[0] is Label tn && tn.ClassListContains("text")) tn.text = info.name;
                else { var l = new Label(info.name) { pickingMode = PickingMode.Ignore, enableRichText = false }; l.AddToClassList("text"); card.name.Insert(0, l); }
                // A dulling blade shows a notched-sword mark on the card, with the state in its tooltip.
                var w = info.weapon;
                card.weapon.SetHidden(w == null || string.IsNullOrEmpty(w.condition));
                if (w != null && !string.IsNullOrEmpty(w.condition))
                {
                    card.weapon.SetText(w.left > 0.6 ? "⚔" : w.left > 0.3 ? "⚠" : "☠");
                    card.weapon.SetClassName($"ui-weapon {(w.left > 0.6 ? "worn" : w.left > 0.3 ? "chipped" : "battered")}");
                    card.weapon.SetTitle($"{w.name}: {w.condition} — the imp grinds blades back in the camp");
                }
                card.root.ClassToggle("low-hp", info.hp > 0 && info.hp <= Math.Max(5, info.hpMax * 0.25));
                card.root.ClassToggle("low-mp", info.mpMax > 0 && info.mp <= Math.Max(2, info.mpMax * 0.2));
                card.hp.SetStyle("width", $"{N(Math.Max(0, Math.Min(100, ((double)info.hp / Math.Max(1, info.hpMax)) * 100)))}%");
                card.hpText.SetText($"{info.hp}/{info.hpMax}");
                card.mp.SetStyle("width", $"{N(Math.Max(0, Math.Min(100, ((double)info.mp / Math.Max(1, info.mpMax)) * 100)))}%");
                card.mpText.SetText($"{info.mp}/{info.mpMax}");
                // how far through their swing they are, so you can see who is ready without guessing
                bool ready = info.cooldown == null;
                card.cooldown.ClassToggle("ready", ready);
                card.cooldown[0].SetStyle("width", ready ? "100%" : $"{N(Math.Floor(info.cooldown.Value * 100 + 0.5))}%");
                card.cooldown.SetTitle(ready ? $"{info.name} can strike" : $"{info.name} is recovering from a swing");
                card.stats.SetText($"Might {info.might}  Prot {info.protection}");
                string skillsKey = $"{info.skills.fighter.level},{info.skills.fighter.points},{info.skills.fighter.next}|{info.skills.rogue.level},{info.skills.rogue.points},{info.skills.rogue.next}|{info.skills.mage.level},{info.skills.mage.points},{info.skills.mage.next}";
                if (skillsKey != card.skillsKey)
                {
                card.skillsKey = skillsKey;
                // the rows already shown take the new numbers (rebuilt, their xp wrapped to a second line for a frame
                // whenever a hero gained experience and the card jumped)
                var have = card.skills.Children().Where(r => r.ClassListContains("ui-skill")).ToList();
                if (have.Count == 3)
                {
                    var keys = new[] { "fighter", "rogue", "mage" };
                    for (int r = 0; r < 3; r += 1)
                    {
                        var k = keys[r];
                        var sk = k == "fighter" ? info.skills.fighter : k == "rogue" ? info.skills.rogue : info.skills.mage;
                        int prev = sk.level > 0 ? getEngine().@static.ExpRequirements[Math.Min(sk.level - 1, 10)] : 0;
                        int span = Math.Max(1, sk.next - prev);
                        string @short(int n) => n >= 10000 ? $"{((double)n / 1000).ToString(n % 1000 != 0 ? "F1" : "F0", CultureInfo.InvariantCulture)}k" : n.ToString(CultureInfo.InvariantCulture);
                        var row = have[r];
                        row.Q(className: "ui-skill-name")?.SetText($"{char.ToUpperInvariant(k[0])}{k.Substring(1)} {sk.level}");
                        row.Q(className: "ui-skill-fill")?.SetStyle("width", $"{N(Math.Max(0, Math.Min(100, ((double)(sk.points - prev) / span) * 100)))}%");
                        var xpEl = row.Q(className: "ui-skill-xp");
                        xpEl?.SetText($"{@short(sk.points)}/{@short(sk.next)}");
                        xpEl?.SetTitle($"{sk.points} / {sk.next} experience");
                        row.SetTitle($"{k}: level {sk.level}, {sk.points} xp, next level at {sk.next}");
                    }
                }
                else
                card.skills.ReplaceChildren(new[] { "fighter", "rogue", "mage" }.Select((k) =>
                {
                    var sk = k == "fighter" ? info.skills.fighter : k == "rogue" ? info.skills.rogue : info.skills.mage;
                    var row = el("div", "ui-skill");
                    var label = el("span", "ui-skill-name", $"{char.ToUpperInvariant(k[0])}{k.Substring(1)} {sk.level}");
                    var bar = el("div", "ui-skill-bar");
                    var fill = el("div", "ui-skill-fill");
                    int prev = sk.level > 0 ? getEngine().@static.ExpRequirements[Math.Min(sk.level - 1, 10)] : 0;
                    int span = Math.Max(1, sk.next - prev);
                    fill.SetStyle("width", $"{N(Math.Max(0, Math.Min(100, ((double)(sk.points - prev) / span) * 100)))}%");
                    bar.Append(fill);
                    string @short(int n) => n >= 10000 ? $"{((double)n / 1000).ToString(n % 1000 != 0 ? "F1" : "F0", CultureInfo.InvariantCulture)}k" : n.ToString(CultureInfo.InvariantCulture);
                    var xp = el("span", "ui-skill-xp", $"{@short(sk.points)}/{@short(sk.next)}");
                    xp.SetTitle($"{sk.points} / {sk.next} experience");
                    row.SetTitle($"{k}: level {sk.level}, {sk.points} xp, next level at {sk.next}");
                    row.Append(label, bar, xp);
                    return (object)row;
                }).ToArray());
                }
                var status = new List<string>();
                if (info.status.poisoned) status.Add("poisoned");
                if (info.status.paralyzed) status.Add("paralyzed");
                if (info.status.asleep) status.Add("asleep");
                if (info.weaponHit != 0) status.Add($"hit {info.weaponHit}");
                if (info.damageSuffered != 0) status.Add($"-{info.damageSuffered}");
                card.status.SetText(string.Join(" · ", status));
                card.attack.SetDisabled(info.status.busy);
                // the hero's quick spell (icon + power), set on the character screen
                var q = getQuickSpell != null ? getQuickSpell(c) : null;
                // rebuilt only when it changes (fresh children every tick are laid out a frame late: the card shook)
                string quickKey = q != null ? $"{q.spell}|{q.level}|{q.name}" : "";
                if (quickKey == card.quickKey) continue;
                card.quickKey = quickKey;
                card.quick.ReplaceChildren();
                if (q != null)
                {
                    card.quick.Append(SpellWidget.spellIcon(q.spell, 18), el("span", "ui-quick-power", (q.level + 1).ToString(CultureInfo.InvariantCulture)));
                    card.quick.SetTitle($"Quick spell: {q.name} at power {q.level + 1} (C). Click to change it.");
                    card.quick.SetHidden(false);
                }
                else
                {
                    card.quick.Append(el("span", "ui-quick-none", "no quick spell"));
                    card.quick.SetTitle("Click to choose this hero's quick spell");
                    card.quick.SetHidden(false);
                }
            }
        }

        // Accept an inventory item dragged from the inventory overlay (or the hotbar source slot).
        public void dropTarget(VisualElement el, Action<int> onDrop)
        {
            el.On("dragover", (DomEvent @event) => { if (@event.dataTransfer.HasType("text/x-inventory-slot")) { @event.preventDefault(); el.AddToClassList("drop"); } });
            el.On("dragleave", () => el.RemoveFromClassList("drop"));
            el.On("drop", (DomEvent @event) =>
            {
                @event.preventDefault();
                el.RemoveFromClassList("drop");
                double from = Number(@event.dataTransfer.getData("text/x-inventory-slot"));
                if (isInteger(from)) onDrop((int)from);
            });
        }

        public Card buildCard(int c)
        {
            var engine = getEngine();
            var root = el("div", "ui-card");
            root.On("click", (DomEvent @event) => { if (closestButton(@event.target) == null) pickHero(c); });
            // drag a card onto another to swap the two heroes' places in the line-up
            // (drag handle is the name bar, so plain clicks on the card keep selecting the hero)
            root.On("dragover", (DomEvent @event) => { if (@event.dataTransfer.HasType("text/x-party-slot")) { @event.preventDefault(); root.AddToClassList("drop"); } });
            root.On("dragleave", () => root.RemoveFromClassList("drop"));
            root.On("drop", (DomEvent @event) => { root.RemoveFromClassList("drop"); double from = Number(@event.dataTransfer.getData("text/x-party-slot")); if (isInteger(from) && from != c) { @event.preventDefault(); onSwap?.Invoke((int)from, c); } });
            var face = (CanvasEl)Dom.El("canvas");
            face.SetClassName("ui-face");
            face.SetTitle("Click: character screen. Right-click: use the hand item on this hero (equipment goes into its slot). Shift+1-4 select heroes");
            face.On("click", (DomEvent @event) => { @event.stopPropagation(); bool cast = pickHero(c); if (!cast && onPortrait != null) onPortrait(c); });
            face.On("contextmenu", (DomEvent @event) => { @event.preventDefault(); engine.queueAsync(() => engine.uiUseHandOn(c)); });
            dropTarget(face, (from) => engine.queueAsync(() => engine.uiDropInventoryOn(from, c)));
            var name = el("div", "ui-name");
            name.SetDraggable(true);
            name.SetTitle("Drag the name onto another hero to swap places in the line-up (the hero nearest a monster takes its hits)");
            name.On("dragstart", (DomEvent @event) => { @event.dataTransfer.setData("text/x-party-slot", c.ToString(CultureInfo.InvariantCulture)); @event.dataTransfer.effectAllowed = "move"; });
            var hpBar = el("div", "ui-bar hp");
            var hp = el("div", "ui-fill");
            var hpText = el("span", "ui-bar-text");
            hpBar.Append(hp, hpText);
            var mpBar = el("div", "ui-bar mp");
            var mp = el("div", "ui-fill");
            var mpText = el("span", "ui-bar-text");
            mpBar.Append(mp, mpText);
            var stats = el("div", "ui-stats");
            var skills = el("div", "ui-skills");
            var status = el("div", "ui-status");
            var cooldown = el("div", "ui-cooldown");
            cooldown.Append(el("i"));
            var weapon = el("span", "ui-weapon"); // blade condition, only with weapon wear on
            weapon.SetHidden(true);
            var attack = el("button", "ui-attack", "Attack");
            attack.SetAttr("type", "button");
            attack.On("click", () => { if (engine.awaitingSpellTarget) { engine.uiSelectCharacter(c); return; } engine.queueAsync(() => engine.clickedAttackButton(new LolButton { arg = c })); attack.BlurEl(); });
            var best = el("button", "ui-attack ui-best", "Equip best");
            best.SetAttr("type", "button");
            best.SetTitle("Put the best weapon and armour from the inventory into every slot where it beats what is worn");
            best.On("click", () => { onEquipBest?.Invoke(c); best.BlurEl(); });
            var info = el("div", "ui-card-info");
            var quick = el("button", "ui-quick");
            quick.SetAttr("type", "button");
            quick.On("click", () => { engine.uiSelectCharacter(c); onPortrait?.Invoke(c); });
            var buttons = el("div", "ui-buttons");
            buttons.Append(attack, best, quick);
            name.Append(weapon);
            info.Append(name, hpBar, mpBar, cooldown, stats, skills, status, buttons);
            root.Append(face, info);
            return new Card { root = root, face = face, name = name, hp = hp, hpText = hpText, mp = mp, mpText = mpText, stats = stats, skills = skills, status = status, attack = attack, quick = quick, cooldown = cooldown, weapon = weapon };
        }
    }
}
