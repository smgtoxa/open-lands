// src/main.mjs, section B (lines 660-1324): the journal log, NPC memory, the item database, fast
// travel, spell power and quick spells, the icon renderer, reagents, the bag panel and the quick
// actions. Ported 1:1 (docs/port/HOST.md). C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>A journal line: { level, block, text, cls, t, speaker, hero } (lol.journal).</summary>
    public sealed class JournalEntry
    {
        public int level, block;
        public string text, cls;
        public double t;
        public string speaker;
        public bool hero;
        public string kind;   // Unity build: the message's kind ("system", "warn", "note"...), for the log's filters
    }

    /// <summary>{ slot, level }: a quick spell (lol.quickSpell, lol.quickSpells values).</summary>
    public sealed class QuickSpell
    {
        public int slot, level;
    }

    /// <summary>teleporting: { level, until, timer }</summary>
    public sealed class Teleporting
    {
        public int level;
        public double until;
        public int timer;
    }

    /// <summary>slotCanvases entries: { button, canvas }</summary>
    public sealed class SlotCanvas
    {
        public VisualElement button;
        public CanvasEl canvas;
    }

    /// <summary>updateInventoryPanel's stacks: { slot, item, kind, count, pinned }</summary>
    public sealed class InvStack
    {
        public int slot, item, kind, count;
        public bool pinned;
    }

    /// <summary>The browser's Image for drawArtInto: loads once, `complete` when it has pixels.</summary>
    public sealed class ArtImage
    {
        public Texture2D texture;
        public bool complete;
        public int naturalWidth => texture != null ? texture.width : 0;
    }

    /// <summary>drawIconInto's expandos on a canvas: logicalWidth, logicalHeight, hdWidth.</summary>
    public sealed class IconBox
    {
        public int logicalWidth, logicalHeight, hdWidth = -1;
    }

    public sealed partial class Web
    {
        static readonly JsonSerializerOptions B_json = new JsonSerializerOptions { IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        // --- journal: dialogue lines and notices, by level, kept in the browser ---
        const string JOURNAL_KEY = "lol.journal";
        List<JournalEntry> journal = new List<JournalEntry>();
        VisualElement journalList;
        DomInput journalFilter;
        VisualElement journalClear;

        void init_B01()
        {
            try { journal = JsonSerializer.Deserialize<List<JournalEntry>>(localStorage.getItem(JOURNAL_KEY) ?? "null", B_json) ?? new List<JournalEntry>(); } catch (Exception) { /* ignore */ }
            journalList = Q("#journal-list");
            journalFilter = Q("#journal-filter") as DomInput;
        }

        public void journalAdd(string text, string cls, object kind = null)
        {
            if (engine == null || !playing || kind as string == "combat") return;
            string clean = Regex.Replace(Regex.Replace(text ?? "null", "[\\x01-\\x1f]", " "), "\\s+", " ").Trim();
            if (clean.Length == 0 || (journal.Count != 0 && journal[journal.Count - 1].text == clean)) return;
            string hero = cls == "journal-say" && engine.updateCharNum >= 0 && engine.characters[engine.updateCharNum] != null && (engine.characters[engine.updateCharNum].flags & 1) != 0 ? engine.characters[engine.updateCharNum].name : "";
            string npc = hero.Length == 0 && cls == "journal-say" ? npcs.speaker() : "";
            string speaker = hero.Length != 0 ? hero : npc;
            if (!string.IsNullOrEmpty(npc)) npcs.addLine(npc, clean);
            string kindName = kind is string ks ? ks : kind is IConvertible kc ? new[] { "", "warn", "alert", "note", "info" }.ElementAtOrDefault(Convert.ToInt32(kc, CultureInfo.InvariantCulture)) : null;
            journal.Add(new JournalEntry { level = engine.currentLevel, block = engine.currentBlock, text = clean, cls = cls, t = now(), speaker = speaker, hero = hero.Length != 0, kind = string.IsNullOrEmpty(kindName) ? null : kindName });
            if (journal.Count > 400) journal.RemoveAt(0);
            try { localStorage.setItem(JOURNAL_KEY, JsonSerializer.Serialize(journal, B_json)); } catch (Exception) { /* ignore */ }
        }

        // Unity build: the log reads newest first, sorted into dialogue, events, items and system notices (filter
        // chips over it, system notices hidden at first), repeated lines folded into one with a count, a time on
        // every line and no block numbers. Built a slice a frame (a long log held the game still for seconds).
        int journalRenderToken;
        const int JOURNAL_SLICE = 60;
        static readonly (string key, string label, string icon)[] JOURNAL_KINDS =
            { ("talk", "Dialogue", "talk"), ("events", "Events", "flag"), ("items", "Items", "bag"), ("system", "System", "note") };
        string journalShow = "all";   // a kind, or "all": everything but the system notices
        VisualElement journalChips;
        static readonly Regex JOURNAL_SYSTEM = new Regex(@"^(Quick (save|load)|Game (saved|loaded)|Saved|Loaded|Autosave|Checkpoint|You can't go that way|Nobody is ready|Not with |Saving is only|Nothing to |Not now)|(has no quick spell|already wears the best gear|not enough mana for power|is not ready|cannot cast that now|cannot attack)", RegexOptions.IgnoreCase);
        static readonly Regex JOURNAL_ITEMS = new Regex(@"( taken\.?$|^You find |picked up|stole an item|^Dropped|^Sold |^Bought |credits|crowns|silver coins|into the chest|from the chest|^Equipped)", RegexOptions.IgnoreCase);

        static string journalKind(JournalEntry e)
        {
            if (e.cls == "journal-say") return "talk";
            if (e.kind == "system" || JOURNAL_SYSTEM.IsMatch(e.text ?? "")) return "system";
            if (JOURNAL_ITEMS.IsMatch(e.text ?? "")) return "items";
            return "events";
        }

        bool journalShows(string kind) => journalShow == "all" ? kind != "system" : kind == journalShow;

        // one chip on at a time, like tabs: All (but the system notices) or a single kind
        void journalChipsUpdate()
        {
            if (journalChips == null) return;
            journalChips.ReplaceChildren();
            void chip(string key, string label, string icon, int n)
            {
                var c = Dom.El("button", "itemdb-chip jl-chip" + (journalShow == key ? " on" : ""));
                c.SetAttr("type", "button");
                if (icon != null) c.Append(achIcon(icon, 14));
                c.Append($" {label} ({n})");
                c.On("click", () => { journalShow = key; renderJournal(); });
                journalChips.Append(c);
            }
            chip("all", "All", null, journal.Count(e => journalKind(e) != "system"));
            foreach (var (key, label, icon) in JOURNAL_KINDS) chip(key, label, icon, journal.Count(e => journalKind(e) == key));
            CssLayout.Touch(journalChips);
        }

        public void renderJournal()
        {
            if (journalList == null) return;
            if (journalChips == null && journalFilter?.parent != null)
            {
                journalChips = Dom.El("div", "jl-chips");
                var tools = journalFilter.parent;
                tools.parent.Insert(tools.parent.IndexOf(tools) + 1, journalChips);
            }
            journalChipsUpdate();
            int token = ++journalRenderToken;
            string needle = (journalFilter != null ? journalFilter.value : "").ToLowerInvariant();
            journalList.ReplaceChildren();
            // the plan, newest first: a header where the level changes, a conversation box around dialogue lines
            // close in time, and a repeated line folded into the one before it
            var plan = new List<(JournalEntry entry, string kind, int count, bool levelHead, bool talkHead)>();
            int level = -1;
            double lastSay = 0;
            for (int i = journal.Count - 1; i >= 0; i -= 1)
            {
                var entry = journal[i];
                string kind = journalKind(entry);
                if (!journalShows(kind)) continue;
                if (needle.Length != 0 && !entry.text.ToLowerInvariant().Contains(needle) && !(entry.speaker ?? "").ToLowerInvariant().Contains(needle)) continue;
                bool levelHead = entry.level != level;
                if (levelHead) { level = entry.level; lastSay = 0; }
                if (!levelHead && plan.Count > 0 && plan[plan.Count - 1].entry.text == entry.text && plan[plan.Count - 1].kind == kind)
                {
                    var last = plan[plan.Count - 1];
                    plan[plan.Count - 1] = (last.entry, last.kind, last.count + 1, last.levelHead, last.talkHead);
                    continue;
                }
                bool talkHead = kind == "talk" && (lastSay == 0 || Math.Abs(lastSay - entry.t) > 45000);
                if (kind == "talk") lastSay = entry.t;   // other lines between the words do not end the talk
                plan.Add((entry, kind, 1, levelHead, talkHead));
            }
            if (plan.Count == 0) { journalList.Append(Dom.El("p", "panel-note", journal.Count != 0 ? "Nothing matches. Try another word, or All above." : "Nothing written yet.")); return; }
            List<VisualElement> build(int from, int to)
            {
                var els = new List<VisualElement>();
                for (int i = from; i < to; i += 1) els.AddRange(journalRows(plan[i].entry, plan[i].kind, plan[i].count, plan[i].levelHead, plan[i].talkHead));
                return els;
            }
            int done = Math.Min(plan.Count, JOURNAL_SLICE);
            journalList.Append(build(0, done));
            journalList.SetScrollTop(0);
            if (done == plan.Count) return;
            var loading = Dom.El("h5", "journal-talk", "Loading earlier entries…");
            journalList.Append(loading); CssLayout.Touch(loading);
            void more()
            {
                if (token != journalRenderToken || journalList.panel == null) return;
                int to = Math.Min(plan.Count, done + JOURNAL_SLICE);
                foreach (var el in build(done, to)) { journalList.Insert(journalList.IndexOf(loading), el); CssLayout.Touch(el); }
                done = to;
                if (done < plan.Count) timers.setTimeout(more, 0); else loading.RemoveFromHierarchy();
            }
            timers.setTimeout(more, 0);
        }

        IEnumerable<VisualElement> journalRows(JournalEntry entry, string kind, int count, bool levelHead, bool talkHead)
        {
            string time = entry.t != 0 ? $"{DateTimeOffset.FromUnixTimeMilliseconds((long)entry.t).ToLocalTime():HH:mm}" : "";
            if (levelHead) yield return Dom.El("h4", "jl-level", engine != null ? engine.levelName(entry.level) : $"Level {entry.level}");
            if (talkHead) yield return Dom.El("h5", "journal-talk", $"Conversation{(time.Length != 0 ? $" at {time}" : "")}");
            var line = Dom.El("div", $"jl-row jl-{kind}{(entry.kind == "warn" || entry.kind == "alert" ? " jl-warn" : "")}");
            line.Append(Dom.El("div", "jl-time", time));
            // dialogue: the hero's face or the NPC snapshot; the rest: the kind's icon
            if (kind == "talk" && !string.IsNullOrEmpty(entry.speaker) && engine != null)
            {
                var who = engine.characters.FirstOrDefault((c) => (c.flags & 1) != 0 && c.name == entry.speaker);
                var face = Dom.El(who != null ? "canvas" : "img");
                face.SetClassName("journal-face");
                if (who != null) { int i = Array.IndexOf(engine.characters, who); var shape = engine.characterFaceShapes[i] != null ? engine.characterFaceShapes[i][0] : null; if (shape != null) { var fc = (CanvasEl)face; fc.width = shape.width; fc.height = shape.height; drawIconInto(fc, shape, engine.uiPalette()); } }
                else { if (npcs.npcs.TryGetValue(entry.speaker, out var n) && n != null && !string.IsNullOrEmpty(n.thumb)) setImageSrc(face, n.thumb); else face.SetHidden(true); }
                line.Append(face);
            }
            else
            {
                var icon = achIcon(JOURNAL_KINDS.First(k => k.key == kind).icon, 14);
                icon.AddToClassList("jl-icon");
                line.Append(icon);
            }
            var text = Dom.El("div", "jl-text");
            if (kind == "talk" && !string.IsNullOrEmpty(entry.speaker)) text.Append(Dom.El("b", "jl-who", entry.speaker + "  "), entry.text);
            else text.Append(entry.text);
            line.Append(text);
            if (count > 1) line.Append(Dom.El("div", "jl-count", $"×{count}"));
            yield return line;
        }

        void init_B02()
        {
            if (journalFilter != null) journalFilter.On("input", renderJournal);
            journalClear = Q("#journal-clear");
            if (journalClear != null) journalClear.On("click", () => { journal = new List<JournalEntry>(); try { localStorage.removeItem(JOURNAL_KEY); } catch (Exception) { /* ignore */ } renderJournal(); });
        }

        public void toggleJournal(bool? show = null)
        {
            var modal = Q("#journal-overlay");
            if (modal == null) return;
            bool open = show ?? modal.IsHidden();
            // shown first, then drawn: pages built inside a hidden window were laid out against no size and
            // stayed that way (the bestiary, opened on its own tab, kept squeezed columns until clicked again)
            if (open) modal.SetHidden(false);
            // Only the tab in view is drawn now; the others when first shown (journalTabShown). Drawing all five on
            // every opening froze the game for seconds with a long log and a full item list.
            if (open)
            {
                journalStale.Clear();
                foreach (var t in JOURNAL_PAGES.Keys) journalStale.Add(t);
                var active = Q("#journal-overlay .tab.active");
                string want = active != null && active.Dataset().TryGetValue("tab", out var w) ? w : "quests";
                journalTabShown(want);
            }
            modal.SetHidden(!open);
        }

        // tab -> the page's renderer (Statistics draws the Bestiary and Achievements too)
        Dictionary<string, (string name, Action render)> JOURNAL_PAGES => new Dictionary<string, (string, Action)>
        {
            ["log"] = ("journal", renderJournal), ["stats"] = ("stats", renderStats), ["bestiary"] = ("stats", renderStats),
            ["achievements"] = ("stats", renderStats), ["quests"] = ("quests", renderQuestJournal), ["items"] = ("items", renderItemDb),
            ["npcs"] = ("npcs", renderNpcs), ["guide"] = ("guide", renderGuide), ["spellbook"] = ("spellbook", renderSpellBook),
        };
        readonly HashSet<string> journalStale = new HashSet<string>();

        // Each page is drawn on its own: a single bad entry in one of them must not be able to stop the journal.
        void journalTabShown(string tab)
        {
            if (tab == null || !journalStale.Remove(tab) || !JOURNAL_PAGES.TryGetValue(tab, out var page)) return;
            // the tabs sharing a renderer are drawn with it
            foreach (var kv in JOURNAL_PAGES) if (kv.Value.name == page.name) journalStale.Remove(kv.Key);
            try { page.render(); } catch (Exception error) { log($"Journal ({page.name}): {error.Message}"); }
        }

        // --- NPC memory: dialogue scripts name the speaker; a snapshot of the scene keeps the portrait ---
        NpcMemory npcs;

        void init_B03()
        {
            npcs = new NpcMemory();
        }

        /// <summary>npcStarted({ file }): the engine's payload.</summary>
        public void npcStarted(object payload)
        {
            string file = (string)JsObj.Get(payload, "file");
            if (engine == null || !playing) return;
            engine.uiNpcMet(file);
            var entry = npcs.begin(file, engine.currentLevel, engine.currentBlock);
            if (entry == null) return;
            timers.setTimeout(() =>
            { // the portrait is on screen by now
                try
                {
                    // Whichever canvas is actually being painted. With the HD renderer on, #screen is not it, and
                    // grabbing it gave a blank portrait - which is why the People page had empty frames.
                    var src = (hdCanvas != null && !hdCanvas.IsHidden() ? hdCanvas : Q("#screen")) as CanvasEl;
                    if (src == null || src.width == 0 || src.height == 0) return;
                    var c = (CanvasEl)Dom.El("canvas");
                    c.width = 64; c.height = 48;
                    var ctx = c.getContext("2d");
                    ctx.imageSmoothingEnabled = false;
                    // compact view shows the scene window only; the NPC portrait fills its left part
                    ctx.drawImage(src, 0, 0, Math.Min(src.width, src.width * 0.6f), src.height, 0, 0, 64, 48);
                    // A portrait that came out entirely black is no portrait: keep looking rather than store it.
                    var px = ctx.getImageData(0, 0, 64, 48).data;
                    bool lit = false;
                    for (int i = 0; i < px.Length && !lit; i += 4) lit = px[i] > 8 || px[i + 1] > 8 || px[i + 2] > 8;
                    if (!lit) return;
                    npcs.setThumb(entry.name, c.toDataURL("image/png"));
                }
                catch (Exception) { /* ignore */ }
            }, 900);
        }

        public void renderNpcs()
        {
            var box = Q("#journal-npcs");
            if (box == null || engine == null) return;
            box.ReplaceChildren();
            var list = npcs.list();
            if (list.Count == 0) { box.Append(Dom.El("p", "panel-note", "Nobody met yet.")); return; }
            foreach (var n in list)
            {
                var card = Dom.El("div");
                card.SetClassName("npc-card");
                var img = Dom.El("img");
                img.SetClassName("npc-thumb");
                if (!string.IsNullOrEmpty(n.thumb)) setImageSrc(img, n.thumb); else img.SetHidden(true);
                var body = Dom.El("div");
                var title = Dom.El("b");
                title.SetText(n.name);
                var where = Dom.El("div");
                where.SetClassName("panel-note");
                where.SetText($"{engine.levelName(n.level)}, block {n.block} · {n.talks} talk{(n.talks == 1 ? "" : "s")}");
                var last = Dom.El("div");
                last.SetClassName("npc-last");
                last.SetText(n.lines.Count != 0 ? $"\"{n.lines[n.lines.Count - 1]}\"" : "");
                body.Append(title, where, last);
                if (n.level == engine.currentLevel)
                {
                    var go = Dom.El("button");
                    go.SetAttr("type", "button"); go.SetText("Walk there");
                    int block = n.block;
                    go.On("click", () => { toggleJournal(false); if (!engine.autoWalkTo(block)) gameUi.message("No known way there.", "system"); });
                    body.Append(go);
                }
                card.Append(img, body);
                box.Append(card);
            }
        }

        /// <summary>img.src = url for a data: URL (the NPC snapshots): decoded into the Image element.</summary>
        public static void setImageSrc(VisualElement img, string url)
        {
            if (!(img is Image image) || string.IsNullOrEmpty(url) || !url.StartsWith("data:")) return;
            int comma = url.IndexOf(',');
            if (comma < 0) return;
            try
            {
                var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
                if (tex.LoadImage(Convert.FromBase64String(url.Substring(comma + 1)))) image.image = tex;
            }
            catch (Exception) { /* a broken image */ }
        }

        // --- item database: every item seen (inventory, equipment, floor, hand) with where it was first seen ---
        const string ITEMS_KEY = "lol.itemdb";
        Dictionary<int, ItemDbEntry> itemDb = new Dictionary<int, ItemDbEntry>();
        int itemDbTick = 0;

        void init_B04()
        {
            try { itemDb = JsonSerializer.Deserialize<Dictionary<int, ItemDbEntry>>(localStorage.getItem(ITEMS_KEY) ?? "null", B_json) ?? new Dictionary<int, ItemDbEntry>(); } catch (Exception) { /* ignore */ }
        }

        public void scanItems()
        {
            if (engine == null || !playing || (itemDbTick += 1) % 100 != 0) return;
            if (engine.metaScanItems() == 0) return;
            try { localStorage.setItem(ITEMS_KEY, JsonSerializer.Serialize(itemDb, B_json)); } catch (Exception) { /* ignore */ }
        }

        // Items tab: a filterable grid of icons; clicking one shows its card on the right.
        int? itemDbSel = null;
        string itemDbFilter = "";
        string itemDbKind = "all";
        string itemKind(ItemDbEntry it) { return it.usable ? "usable" : it.might != 0 ? "weapon" : it.protection != 0 || it.slots.Count != 0 ? "armour" : "other"; }
        VisualElement mkEl(string tag, string cls = null, string text = null) { var n = Dom.El(tag); if (!string.IsNullOrEmpty(cls)) n.SetClassName(cls); if (text != null) n.SetText(text); return n; }

        public void renderItemDb() => renderItemDb(null, null);

        // into: where the page is built (the box, or swapCard's scratch element: a tile click swaps the card only)
        void renderItemDb(VisualElement unused, VisualElement into)
        {
            var box = Q("#journal-items");
            if (box == null || engine == null) return;
            into = into ?? box;
            // An entry whose item property this build does not have (a save or a browser record from a build
            // with a different set of added items) is skipped rather than followed into a crash.
            var rows = itemDb
                .Where((kv) => kv.Key >= 0 && kv.Key < engine.itemProperties.Count && engine.itemProperties[kv.Key] != null)
                .OrderBy((kv) => kv.Value.name, StringComparer.CurrentCulture)
                .ToList();
            if (rows.Count == 0) { into.ReplaceChildren(mkEl("p", "panel-note", "No items seen yet.")); return; }
            var palette = engine.uiPalette();
            VisualElement icon(int prop, int size) { var c = (CanvasEl)Dom.El("canvas"); c.width = c.height = 24; c.SetClassName("itemdb-icon"); c.SetStyle("width", size + "px"); c.SetStyle("height", size + "px"); drawIconInto(c, engine.itemIconShapes[engine.itemProperties[prop].shpIndex], palette); return c; }
            var heroes = engine.characters.Where((c) => (c.flags & 1) != 0).ToList();
            string wearers(int prop) => string.Join(", ", heroes.Where((c) => { int @base = engine.@static.CharInvIndex[c.raceClassSex] * 22; var p = engine.itemProperties[prop]; for (int i = 0; i < 11; i += 1) if ((p.type & (1 << i)) != 0 && engine.@static.CharInvDefs[@base + i * 2] != 0xff) return true; return false; }).Select((c) => c.name));
            var shown = rows.Where((kv) => (itemDbKind == "all" || itemKind(kv.Value) == itemDbKind) && (itemDbFilter.Length == 0 || kv.Value.name.ToLowerInvariant().Contains(itemDbFilter))).ToList();
            if (!shown.Any((kv) => kv.Key == itemDbSel)) itemDbSel = shown.Count != 0 ? shown[0].Key : (int?)null;

            var tools = mkEl("div", "itemdb-tools");
            var search = (DomInput)Dom.El("input"); search.SetAttr("type", "search"); search.SetAttr("placeholder", "Search items"); search.value = itemDbFilter; search.SetAttr("aria-label", "Search items");
            // again.setSelectionRange(end, end): the DOM layer has no caret API; focusing is kept.
            search.On("input", () => { itemDbFilter = search.value.Trim().ToLowerInvariant(); renderItemDb(); var again = box.Q("input[type=search]") as DomInput; if (again != null) { again.FocusInput(); } });
            tools.Append(search);
            foreach (var (kind, label) in new[] { ("all", "All"), ("weapon", "Weapons"), ("armour", "Armour"), ("usable", "Usable"), ("other", "Other") })
            {
                var b = mkEl("button", "itemdb-chip" + (itemDbKind == kind ? " on" : ""), label); b.SetAttr("type", "button");
                b.On("click", () => { itemDbKind = kind; renderItemDb(); });
                tools.Append(b);
            }
            var grid = mkEl("div", "itemdb-grid");
            foreach (var kv in shown)
            {
                int prop = kv.Key; var it = kv.Value;
                var tile = mkEl("button", "itemdb-tile" + (prop == itemDbSel ? " on" : "")); tile.SetAttr("type", "button"); tile.SetTitle(it.name);
                tile.Append(icon(prop, 40), mkEl("span", "", it.name));
                tile.On("click", () => { itemDbSel = prop; swapCard(box, renderItemDb, tile); });
                grid.Append(tile);
            }
            var card = mkEl("div", "itemdb-card");
            if (itemDbSel != null)
            {
                var it = itemDb[itemDbSel.Value]; int prop = itemDbSel.Value;
                var head = mkEl("div", "itemdb-head"); head.Append(icon(prop, 96), mkEl("h3", "", it.name));
                var chips = mkEl("div", "itemdb-chips");
                if (!it.usable && it.might != 0) chips.Append(mkEl("span", "chip might", "Might " + it.might));
                if (!it.usable && it.protection != 0) chips.Append(mkEl("span", "chip prot", "Protection " + it.protection));
                string kindName; switch (itemKind(it)) { case "weapon": kindName = "Weapon"; break; case "armour": kindName = "Armour"; break; case "usable": kindName = "Usable"; break; default: kindName = "Item"; break; }
                chips.Append(mkEl("span", "chip", kindName));
                var rowsEl = mkEl("dl", "itemdb-rows");
                void row(string k, string v) { if (string.IsNullOrEmpty(v)) return; rowsEl.Append(mkEl("dt", "", k), mkEl("dd", "", v)); }
                row("Use", engine.itemUse(it.name, it.usable, it.slots.Count != 0));
                row("Fits", string.Join(", ", it.slots));
                if (it.slots.Count != 0) { var w = wearers(prop); row("Who can wear it", w.Length != 0 ? w : "nobody in the party"); }
                row("First seen", engine.levelName(it.level) + ", block " + it.block);
                int have = engine.inventory.Count((i) => i != 0 && engine.itemsInPlay[i].itemPropertyIndex == prop) + engine.characters.Where((c) => (c.flags & 1) != 0).Aggregate(0, (n, c) => n + c.items.Count((i) => i != 0 && engine.itemsInPlay[i].itemPropertyIndex == prop));
                row("Carried", have != 0 ? have.ToString() : "");
                card.Append(head, chips, rowsEl);
            }
            else card.Append(mkEl("p", "panel-note", "Nothing matches."));
            var body = mkEl("div", "itemdb"); body.Append(grid, card);
            // Unity build: a search or a kind chip redraws the results only. The search box being typed in stays
            // (rebuilt on every key, it flashed and lost the caret); the chips just move their highlight.
            var oldTools = into.Children().FirstOrDefault(e => e.ClassListContains("itemdb-tools"));
            var oldBody = into.Children().FirstOrDefault(e => e.ClassListContains("itemdb"));
            if (oldTools != null && oldBody != null)
            {
                var chipsNow = tools.Children().Where(e => e.ClassListContains("itemdb-chip")).ToList();
                var chipsWas = oldTools.Children().Where(e => e.ClassListContains("itemdb-chip")).ToList();
                for (int i = 0; i < chipsWas.Count && i < chipsNow.Count; i += 1) chipsWas[i].ClassToggle("on", chipsNow[i].ClassListContains("on"));
                into.Insert(into.IndexOf(oldBody), body);
                oldBody.RemoveFromHierarchy();
                CssLayout.Touch(body);
            }
            else into.ReplaceChildren(tools, body);
        }

        // --- fast travel: the last position on every level visited, offered in the Map panel ---
        const string VISITED_KEY = "lol.visited";
        Dictionary<int, VisitedEntry> visited = new Dictionary<int, VisitedEntry>();
        DomSelect travelSelect;

        void init_B05()
        {
            try { visited = JsonSerializer.Deserialize<Dictionary<int, VisitedEntry>>(localStorage.getItem(VISITED_KEY) ?? "null", B_json) ?? new Dictionary<int, VisitedEntry>(); } catch (Exception) { /* ignore */ }
            travelSelect = Q("#travel") as DomSelect;
            // names resolve only once the language file is loaded: refresh the list when it is opened
            if (travelSelect != null) foreach (var ev in new[] { "focus", "mousedown" }) travelSelect.On(ev, () => renderTravel());
        }

        public void rememberVisit()
        {
            if (engine == null || !playing || engine.currentBlock == 0) return;
            // A floor of the pit is a real level with a generated map: it must not be remembered as a travel
            // target, autosaved as a level change, or stirred by the re-entry respawn.
            if (engine.uiInDungeon()) return;
            if (lastAutosaveLevel != engine.currentLevel)
            {
                // Walking back into a level you have been to before stirs it up again (Settings -> Monsters
                // return). The wait lets the level change finish: uiRespawnMonsters refuses while the engine's
                // timers are paused, so try once more if the first attempt is turned away.
                if (visited.ContainsKey(engine.currentLevel) && visited[engine.currentLevel] != null && lastAutosaveLevel != -1 && (settings.respawn == "reentry" || settings.respawn == "both"))
                {
                    int level = engine.currentLevel;
                    void stir(bool retry) => timers.setTimeout(() =>
                    {
                        // ...and not once the party has gone down the pit: a floor is borrowed from this very
                        // level, so the level number alone does not say they are still where they were.
                        if (engine == null || !playing || engine.currentLevel != level || engine.uiInCamp() || engine.uiInDungeon()) return;
                        engine.queueAsync(async () => { if ((await engine.uiRespawnMonsters()) == 0 && retry) stir(false); });
                    }, retry ? 1200 : 1000);
                    stir(true);
                }
                // A level change: autosave (the first status of a fresh game/load only arms it).
                if (lastAutosaveLevel != -1 && settings.autosaveLevel) timers.setTimeout(() => saveToSlot("auto", true), 500);
                if (lastAutosaveLevel != -1) timers.setTimeout(() => checkpoint($"Entered level {engine.currentLevel}"), 900);
                lastAutosaveLevel = engine.currentLevel;
            }
            visited.TryGetValue(engine.currentLevel, out var cur);
            // The level's own exits, so the camp's map board can name the ways out that are still unexplored.
            var exits = cur != null ? cur.exits : null;
            try { exits = engine.uiLevelExits().Select((e) => e.level).Distinct().ToList(); } catch (Exception) { /* script not ready */ }
            // What this level's monsters are worth, so the pit can offer its floors gently first.
            int danger = cur != null ? cur.danger : 0;
            try
            {
                foreach (var p in engine.monsterProperties)
                {
                    if (p == null || p.hitPoints == 0 || p.maxWidth == 0) continue;
                    int might = p.itemsMight.Aggregate(0, (a, b) => a + b);
                    danger = Math.Max(danger, p.hitPoints + might * 2);
                }
            }
            catch (Exception) { /* before the level is loaded */ }
            visited[engine.currentLevel] = new VisitedEntry { block = engine.currentBlock, dir = engine.currentDirection, exits = exits ?? new List<int>(), danger = danger };
            try { localStorage.setItem(VISITED_KEY, JsonSerializer.Serialize(visited, B_json)); } catch (Exception) { /* ignore */ }
            if (cur == null) renderTravel();
        }

        // Fast travel belongs to the camp: the party set out from a camp they have rested in, never from
        // the middle of a corridor. "Set out" needs everyone at full health and mana, and takes five
        // seconds of casting that a monster can still interrupt.
        VisualElement travelGo;
        VisualElement travelNote;
        VisualElement teleportBox;
        Teleporting teleporting = null; // { level, until, timer }

        void init_B06()
        {
            travelGo = Q("#travel-go");
            travelNote = Q("#travel-note");
            teleportBox = Q("#teleport-overlay");
        }

        TravelReady travelReady()
        {
            if (engine == null || !playing) return new TravelReady { ok = false, why = "Make camp first: the party can only set out from a camp." };
            int? level = travelSelect != null && travelSelect.value.Length != 0 ? int.Parse(travelSelect.value) : (int?)null;
            return engine.uiTravelReady(level);
        }

        public void updateTravelLock()
        {
            if (travelSelect == null || travelGo == null) return;
            var state = travelReady();
            string key = $"{(state.ok ? "true" : "false")}|{state.why}|{(teleporting != null ? "true" : "false")}";
            if (key == travelLocked) return;
            travelLocked = key;
            travelGo.SetDisabled(!state.ok || teleporting != null);
            travelSelect.SetDisabled(teleporting != null);
            if (travelNote != null) travelNote.SetText(state.why);
        }
        string travelLocked = null;

        // The teleport itself: a five-second cast with the vortex overlay, cancelled if the camp breaks.
        void startTeleport(int level)
        {
            visited.TryGetValue(level, out var target);
            if (target == null || teleporting != null) return;
            const int SECONDS = 5;
            teleporting = new Teleporting { level = level, until = now() + SECONDS * 1000, timer = 0 };
            var fill0 = Q("#teleport-fill");
            if (fill0 != null) fill0.SetStyle("width", "0%");
            Q("#teleport-where").SetText($"Travelling to {engine.levelName(level)}");
            teleportBox.SetHidden(false);
            teleportBox.AddToClassList("on");
            teleporting.timer = timers.setInterval(() =>
            {
                if (!playing || engine == null || !engine.uiInCamp() || engine.uiInCombat()) { cancelTeleport("The casting is broken."); return; }
                double left = Math.Max(0, teleporting.until - now());
                var fill = Q("#teleport-fill");
                if (fill != null) fill.SetStyle("width", $"{Math.Round(100 * (1 - left / (SECONDS * 1000)), MidpointRounding.AwayFromZero)}%");
                if (left != 0) return;
                var go = teleporting; cancelTeleport();
                closeCampSheet();
                leaveCampAndMaybeRespawn(); // put the chamber back before the level changes under it
                engine.queueAsync(() => engine.debugTeleport(go.level, target.block, target.dir ?? 0));
            }, 100);
        }

        void cancelTeleport(string message = null)
        {
            if (teleporting == null) return;
            timers.clearInterval(teleporting.timer);
            teleporting = null;
            teleportBox.SetHidden(true);
            teleportBox.RemoveFromClassList("on");
            travelLocked = null;
            if (!string.IsNullOrEmpty(message)) toast(message, "fx-toast-ach");
        }

        // The camp's map board: levels the party has been to that still have an exit to somewhere it has
        // never been. The exits come from the level scripts, recorded as each level is visited.
        // The route between two levels over the exits recorded on the levels you have walked. Returns the
        // chain of levels, or null when the board knows no way.
        List<int> routeBetween(int from, int to)
        {
            if (from == to) return new List<int> { from };
            var prev = new Dictionary<int, int?> { [from] = null };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count != 0)
            {
                int at = queue.Dequeue();
                var exits = visited.TryGetValue(at, out var v) && v != null && v.exits != null ? v.exits : new List<int>();
                foreach (int next in exits)
                {
                    if (prev.ContainsKey(next)) continue;
                    prev[next] = at;
                    if (next == to)
                    {
                        var chain = new List<int> { to };
                        for (int? step = at; step != null; step = prev[step.Value]) chain.Insert(0, step.Value);
                        return chain;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        public void renderTravelBoard()
        {
            var board = Q("#travel-board");
            if (board == null || engine == null) return;
            string name(int l) => engine != null ? engine.levelName(l) : $"Level {l}";
            var leads = new Dictionary<int, int>(); // target level -> the known level it is reached from
            // Object.entries: integer keys come out in ascending order
            foreach (var kv in visited.OrderBy((kv) => kv.Key))
            {
                foreach (int to in (kv.Value != null ? kv.Value.exits : null) ?? new List<int>()) if (!(visited.TryGetValue(to, out var vt) && vt != null) && !leads.ContainsKey(to)) leads[to] = kv.Key;
            }
            var route = mkEl("p", "panel-note travel-route", "");
            route.SetHidden(true);
            var rows = leads.Select((kv) =>
            {
                int to = kv.Key, from = kv.Value;
                var line = mkEl("button", "travel-lead", $"{name(to)} ← {name(from)}");
                line.SetAttr("type", "button");
                line.SetTitle("Show the way there from where you stand");
                line.On("click", () =>
                {
                    var chain = routeBetween(engine.currentLevel, to);
                    route.SetHidden(false);
                    route.SetText(chain != null
                        ? $"Route: {string.Join(" → ", chain.Select(name))}"
                        : $"No way to {name(to)} from here on the board. Teleport to {name(from)} and walk on.");
                });
                return line;
            }).ToList();
            var children = new List<object> { mkEl("h5", "", "The map board") };
            if (rows.Count != 0) children.AddRange(rows);
            else children.Add(mkEl("p", "panel-note", "Every way out of the places you know leads somewhere you have been."));
            children.Add(route);
            board.ReplaceChildren(children.ToArray());
        }

        public void renderTravel()
        {
            if (travelSelect == null) return;
            var levels = visited.Keys.OrderBy((l) => l).ToList();
            // the names come from the language file: rebuild whenever it can resolve them (see the focus hook)
            // (option.title - "<name> - level l, block b" - has no place on a DomSelect option)
            travelSelect.ClearOptions();
            travelSelect.AddOption("", "choose a place…");
            foreach (int l in levels)
            {
                string name = engine != null ? engine.levelName(l) : $"Level {l}";
                travelSelect.AddOption(l.ToString(), name == $"Level {l}" ? $"Level {l} (block {visited[l].block})" : name);
            }
            travelSelect.parent.SetHidden(levels.Count < 2);
        }

        void init_B07()
        {
            if (travelSelect != null) travelSelect.On("change", () => { travelLocked = null; updateTravelLock(); });
            if (travelGo != null) travelGo.On("click", () =>
            { // `on` is defined further down
                int level = travelSelect != null && int.TryParse(travelSelect.value, out var lv) ? lv : 0;
                if (!travelReady().ok || level == 0 || level == engine.currentLevel) return;
                startTeleport(level);
                travelGo.BlurEl();
            });
            renderTravel();
        }

        // Side panel: minimap note and the spell quick bar (rebuilt only when the relevant state changes).
        VisualElement spellbar;
        VisualElement spellbarNote;
        VisualElement minimapNote;
        string sidebarKey = "";
        const string QUICK_SPELL_KEY = "lol.quickSpell";
        QuickSpell quickSpell = new QuickSpell { slot = -1, level = 0 };
        VisualElement quickSpellName;
        // Last power used per spell slot (remembered across sessions; the star and hotbar use it).
        const string SPELL_POWER_KEY = "lol.spellPower";
        // { [characterId]: { [spellSlot]: level } } - each hero remembers their own power per spell.
        Dictionary<string, Dictionary<int, int>> spellPower = new Dictionary<string, Dictionary<int, int>>();

        void init_B08()
        {
            spellbar = Q("#spellbar");
            spellbarNote = Q("#spellbar-note");
            minimapNote = Q("#minimap-note");
            try { quickSpell = JsonSerializer.Deserialize<QuickSpell>(localStorage.getItem(QUICK_SPELL_KEY) ?? "null", B_json) ?? quickSpell; } catch (Exception) { /* ignore */ }
            quickSpellName = Q("#quick-spell-name");
            try
            {
                var raw = JsonNode.Parse(localStorage.getItem(SPELL_POWER_KEY) ?? "null") as JsonObject ?? new JsonObject();
                if (raw.Any((kv) => kv.Value is JsonValue v && v.GetValueKind() == JsonValueKind.Number)) raw = new JsonObject { ["*"] = raw }; // old flat format -> default for everyone
                spellPower = raw.Deserialize<Dictionary<string, Dictionary<int, int>>>(B_json);
            }
            catch (Exception) { /* ignore */ }
        }

        string powerOwner() => engine != null && engine.characters[engine.selectedCharacter] != null ? engine.characters[engine.selectedCharacter].id.ToString() : "*";

        public int powerFor(int slot, string id = null)
        {
            id ??= powerOwner();
            if (spellPower.TryGetValue(id, out var mine) && mine != null && mine.TryGetValue(slot, out int own)) return own;
            if (spellPower.TryGetValue("*", out var all) && all != null && all.TryGetValue(slot, out int def)) return def;
            return 0;
        }

        public void rememberSpellPower(int slot, int level, string id = null)
        {
            id ??= powerOwner();
            if (!spellPower.TryGetValue(id, out var mine) || mine == null) spellPower[id] = mine = new Dictionary<int, int>();
            mine[slot] = level;
            try { localStorage.setItem(SPELL_POWER_KEY, JsonSerializer.Serialize(spellPower, B_json)); } catch (Exception) { /* ignore */ }
            sidebarKey = "";
        }

        public void setQuickSpell(int slot, int level)
        {
            quickSpell = new QuickSpell { slot = slot, level = level };
            try { localStorage.setItem(QUICK_SPELL_KEY, JsonSerializer.Serialize(quickSpell, B_json)); } catch (Exception) { /* ignore */ }
            sidebarKey = "";
        }

        public string quickSpellLabel()
        {
            if (engine == null) return "no spell assigned";
            quickSpells.TryGetValue(engine.characters[engine.selectedCharacter].id, out var own);
            var q = own ?? quickSpell;
            if (q == null || q.slot < 0 || engine.availableSpells[q.slot] == -1) return "no spell assigned";
            return $"{engine.spellName(engine.availableSpells[q.slot])} {q.level + 1}{(own != null ? " (hero's own)" : "")}";
        }

        VisualElement inventoryGrid;
        CanvasEl handIcon;
        VisualElement handName;
        VisualElement handUse;
        VisualElement handDrop;
        string inventoryKey = "";
        readonly List<SlotCanvas> slotCanvases = new List<SlotCanvas>();

        void init_B09()
        {
            inventoryGrid = Q("#inventory-grid");
            handIcon = Q("#hand-icon") as CanvasEl;
            handName = Q("#hand-name");
            handUse = Q("#hand-use");
            handDrop = Q("#hand-drop");
        }

        // Draws an item icon shape (indexed pixels + colour table) into a small canvas using the live palette.
        // Artwork shipped with the port (the crafted potions). Images are cached and, because they load
        // asynchronously, the canvas is redrawn once the first one arrives.
        readonly Dictionary<string, ArtImage> artCache = new Dictionary<string, ArtImage>();

        void drawArtInto(Ctx2D ctx, CanvasEl canvas, string url)
        {
            if (!artCache.TryGetValue(url, out var image))
            {
                image = new ArtImage();
                artCache[url] = image;
                // new Image().src = url: the browser's art is SVG (src/assets/*.svg), which Unity cannot rasterize;
                // its PNG under Resources/Art (tools/art_png.py) is used, else the image never loads.
                var tex = UnityEngine.Resources.Load<Texture2D>("Art/" + System.IO.Path.GetFileNameWithoutExtension(url));
                if (tex != null)
                {
                    image.texture = tex;
                    image.complete = true;
                    timers.setTimeout(() => { inventoryKey = ""; gameUi.hotbarKey = ""; }); // the "load" event
                }
            }
            if (!image.complete || image.naturalWidth == 0) return;
            int size = Math.Min(canvas.width, canvas.height);
            ctx.drawImage(image.texture, (canvas.width - size) / 2f, (canvas.height - size) / 2f, size, size);
        }

        // canvas.logicalWidth / logicalHeight / hdWidth: expandos kept beside the canvas.
        static readonly ConditionalWeakTable<CanvasEl, IconBox> iconBoxes = new ConditionalWeakTable<CanvasEl, IconBox>();

        // Item, portrait and face icons. The caller sizes the canvas in logical (320x200) pixels and the
        // stylesheet gives it its screen size; here the backing store is grown to 4x and the shape drawn
        // through the same xBR scaler the HD packs are built with. `plain` keeps the 1x pixels (save
        // thumbnails, which are stored as data URLs).
        public void drawIconInto(CanvasEl canvas, Shape shape, byte[] palette, bool plain = false)
        {
            var ex = iconBoxes.GetOrCreateValue(canvas);
            int box = canvas.width; int boxH = canvas.height;
            if (canvas.width == ex.hdWidth) { box = ex.logicalWidth; boxH = ex.logicalHeight; } // still our own 4x buffer
            int scale = plain ? 1 : Xbr.XBR;
            if (canvas.width != box * scale) { canvas.width = box * scale; canvas.height = boxH * scale; }
            ex.logicalWidth = box; ex.logicalHeight = boxH; ex.hdWidth = canvas.width;
            var ctx = canvas.getContext("2d");
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            if (shape == null) return;
            if (!string.IsNullOrEmpty(shape.art)) { drawArtInto(ctx, canvas, shape.art); return; } // an item the port added: real artwork
            var flat = new byte[shape.width * shape.height * 4];
            for (int i = 0; i < shape.pixels.Length; i += 1)
            {
                int raw = shape.pixels[i];
                if (raw == 0) continue;
                int color = (shape.colorTable != null ? shape.colorTable[raw] : raw) * 3;
                flat[i * 4] = (byte)((palette[color] * 255) / 63);
                flat[i * 4 + 1] = (byte)((palette[color + 1] * 255) / 63);
                flat[i * 4 + 2] = (byte)((palette[color + 2] * 255) / 63);
                flat[i * 4 + 3] = 255;
            }
            var image = ctx.createImageData(shape.width * scale, shape.height * scale);
            var data = plain ? flat : Xbr.xbrUpscale4(flat, shape.width, shape.height);
            Array.Copy(data, image.data, data.Length);
            ctx.putImageData(image, ((box - shape.width) >> 1) * scale, ((boxH - shape.height) >> 1) * scale);
        }

        void init_B10()
        {
            // a hotbar slot dropped anywhere on the inventory window is unpinned
            if (inventoryGrid != null)
            {
                var invBox = Q("#inventory-overlay") ?? inventoryGrid;
                invBox.On("dragover", (DomEvent @event) => { if (@event.dataTransfer.HasType("text/x-hotbar")) @event.preventDefault(); });
                invBox.On("drop", (DomEvent @event) => { string f = @event.dataTransfer.getData("text/x-hotbar"); if (f != "") { @event.preventDefault(); gameUi.assign(int.Parse(f), -1); } });
            }
        }

        // Reagents are not game items (they would eat bag slots), so the bag shows them as their own
        // stacked row under the grid whenever crafting is on.
        string reagentKey = "";

        void renderReagents()
        {
            var box = Q("#reagents");
            if (box == null) return;
            var reagentSelect = Q("#inventory-filter") as DomSelect;
            if (reagentSelect != null && reagentSelect.options.Any((o) => o.value == "reagent")) reagentSelect.SetOptionHidden("reagent", !settings.craft);
            box.SetHidden(!settings.craft || (!string.IsNullOrEmpty(invFilter) && invFilter != "reagent"));
            if (!settings.craft) return;
            var pouch = CraftingUi.readPouch();
            string key = $"{string.Join(",", pouch.Values)}|{invSearch}|{invFilter}";
            if (key == reagentKey) return;
            reagentKey = key;
            var list = Q("#reagent-list");
            var have = LandsOfLore.REAGENTS.Where((kv) => pouch[kv.Key] > 0 && (invSearch.Length == 0 || kv.Value.name.ToLowerInvariant().Contains(invSearch))).ToList();
            list.ReplaceChildren(have.Count != 0
                ? have.Select((kv) =>
                {
                    var cell = mkEl("div", "reagent");
                    cell.SetTitle($"{kv.Value.name}: {kv.Value.hint}");
                    // Unity build: each reagent's own picture (Resources/Reagents, CC0 pixel art)
                    var tex = UnityEngine.Resources.Load<Texture2D>("Reagents/" + kv.Key);
                    if (tex != null) { var pic = new VisualElement { pickingMode = PickingMode.Ignore }; pic.AddToClassList("reagent-icon"); pic.style.backgroundImage = tex; cell.Append(pic); }
                    cell.Append(mkEl("span", "reagent-name", kv.Value.name), mkEl("span", "ui-count", pouch[kv.Key].ToString()));
                    return cell;
                }).ToArray()
                : new[] { mkEl("p", "panel-note", invSearch.Length != 0 || invFilter == "reagent" ? "No reagent of that name." : "No reagents yet. Slain monsters sometimes leave one.") });
        }

        // Bag tools: a text search, a kind filter and a sort. The sort reorders the engine's own bag, so
        // the hotbar pins (which are slot numbers) are moved with it.
        string invSearch = "";
        string invFilter = "";
        VisualElement invCount;

        void init_B11()
        {
            invCount = Q("#inventory-count");
            {
                var search = Q("#inventory-search") as DomInput;
                if (search != null) search.On("input", () => { invSearch = search.value.Trim().ToLowerInvariant(); invPage = 0; updateInventoryPanel(); });
                var filter = Q("#inventory-filter") as DomSelect;
                if (filter != null) filter.On("change", () => { invFilter = filter.value; invPage = 0; updateInventoryPanel(); });
                var sort = Q("#inventory-sort") as DomSelect;
                if (sort != null) sort.On("change", () =>
                {
                    string mode = sort.value; sort.value = "";
                    if (string.IsNullOrEmpty(mode) || !playing || engine == null) return;
                    var moved = engine.uiSortInventory(mode);
                    gameUi.hotbar = gameUi.hotbar.Select((slot) => (slot >= 0 && moved[slot] >= 0 ? moved[slot] : slot)).ToArray();
                    gameUi.saveHotbar();
                    inventoryKey = "";
                    updateInventoryPanel();
                });
            }
        }

        // Unity build: "Equip best" says what happened in a notice: what was put on, that nothing better is carried,
        // or why it cannot be done now (it used to do nothing silently while a scene or a held item blocked it)
        void equipBest(int c)
        {
            if (!playing || engine == null) return;
            var ch = engine.characters[c];
            if (engine.itemInHand != 0) { toast("Put down the item in your hand first."); return; }
            if ((engine.updateFlags & 3) != 0 || engine.needSceneRestore != 0 || engine.sysTimerPaused || engine.weaponsDisabled) { toast("Not now: finish the scene or dialogue first."); return; }
            var before = (int[])ch.items.Select(i => (int)i).ToArray().Clone();
            engine.queueAsync(async () =>
            {
                int changed = await engine.uiEquipBest(c);
                var put = new List<string>();
                for (int s = 0; s < before.Length && s < ch.items.Length; s += 1)
                    if (ch.items[s] != before[s] && ch.items[s] != 0) put.Add(engine.itemName(ch.items[s]));
                toast(changed > 0 ? $"{ch.name} puts on {string.Join(", ", put)}." : $"{ch.name} already wears the best gear in the pack.");
            });
        }

        const int INV_PAGE = 48;
        int invPage;
        VisualElement invPager;

        void renderInvPager(int pages)
        {
            if (invPager == null)
            {
                invPager = Dom.El("div", "inv-pager");
                inventoryGrid.parent.Insert(inventoryGrid.parent.IndexOf(inventoryGrid) + 1, invPager);
                // the wheel over the bag turns its pages
                inventoryGrid.RegisterCallback<WheelEvent>(w =>
                {
                    int before = invPage;
                    invPage += w.delta.y > 0 ? 1 : -1;
                    inventoryKey = "";
                    updateInventoryPanel();
                    if (invPage != before) w.StopPropagation();
                });
            }
            invPager.SetHidden(pages <= 1);
            invPager.ReplaceChildren();
            if (pages <= 1) return;
            VisualElement go(string text, int to, bool on)
            {
                var b = Dom.El("button", "inv-page" + (on ? " on" : ""), text);
                b.SetAttr("type", "button");
                b.SetDisabled(to < 0 || to >= pages);
                b.On("click", () => { invPage = to; inventoryKey = ""; updateInventoryPanel(); });
                return b;
            }
            invPager.Append(go("‹", invPage - 1, false));
            for (int i = 0; i < pages; i += 1) invPager.Append(go((i + 1).ToString(), i, i == invPage));
            invPager.Append(go("›", invPage + 1, false));
            CssLayout.Touch(invPager);
        }

        public void updateInventoryPanel()
        {
            if (inventoryGrid == null) return;
            renderReagents();
            bool onlyReagents = settings.craft && invFilter == "reagent";
            inventoryGrid.SetHidden(onlyReagents);
            var inv = engine.inventory;
            string key = $"{string.Join(",", inv)}|{engine.itemInHand}|{string.Join(",", gameUi.hotbar)}|{invSearch}|{invFilter}|{(settings.craft ? CraftingUi.pouchTotal() : 0)}|{invPage}";
            if (key == inventoryKey) return;
            inventoryKey = key;
            var palette = engine.uiPalette();

            // Identical things share one square with a count on it. The square stands for the first slot
            // holding that kind, so using, dropping or selling takes one off the pile and the rest stay.
            var stacks = new List<InvStack>();
            var byKind = new Dictionary<int, InvStack>();
            int free = 0;
            for (int slot = 0; slot < inv.Length; slot += 1)
            {
                int item = inv[slot];
                if (item == 0) { free += 1; continue; }
                int kind = engine.itemsInPlay[item] != null ? engine.itemsInPlay[item].itemPropertyIndex : -slot;
                bool pinned = gameUi.hotbar.Contains(slot);
                // one square per kind (a pinned first one used to stop the rest grouping); only a second pin of the
                // same kind gets a square of its own
                byKind.TryGetValue(kind, out var found);
                if (found != null && !(pinned && found.pinned))
                {
                    found.count += 1;
                    // the pile is shown by its pinned slot, so the hotbar pin points at the square you see
                    if (pinned) { found.slot = slot; found.item = item; found.pinned = true; }
                    continue;
                }
                var stack = new InvStack { slot = slot, item = item, kind = kind, count = 1, pinned = pinned };
                stacks.Add(stack);
                if (found == null) byKind[kind] = stack;
            }

            slotCanvases.Clear();
            inventoryGrid.ReplaceChildren();
            int shown = 0;
            VisualElement cell(InvStack stack)
            {
                var button = Dom.El("button");
                button.SetAttr("type", "button");
                var canvas = (CanvasEl)Dom.El("canvas");
                canvas.SetClassName("slot-icon");
                canvas.width = 24;
                canvas.height = 24;
                button.Append(canvas);
                slotCanvases.Add(new SlotCanvas { button = button, canvas = canvas });
                if (stack == null) { button.SetTitle("Empty"); drawIconInto(canvas, null, palette); return button; }
                int slot = stack.slot, item = stack.item, count = stack.count;
                button.SetDraggable(true);
                button.On("dragstart", (DomEvent @event) => { @event.dataTransfer.setData("text/x-inventory-slot", slot.ToString()); @event.dataTransfer.effectAllowed = "link"; });
                button.On("click", (DomEvent @event) =>
                {
                    if (!playing || engine == null) return;
                    if (inventoryMode == "drop") { if (engine.inventory[slot] != 0) engine.queueAsync(() => engine.uiDropToFloor(slot)); return; }
                    if (inventoryMode == "sell") { int m = engine.uiMerchantAhead(); if (m >= 0 && engine.inventory[slot] != 0) { engine.queueAsync(() => engine.uiSell(slot, m)); toggleInventory(false); } return; }
                    if (@event.shiftKey) { int spare = gameUi.firstFreeHotbarSlot(); if (spare >= 0) gameUi.assign(spare, slot); }
                    else engine.queueAsync(() => engine.inventorySlotClick(slot));
                    button.BlurEl();
                });
                button.On("contextmenu", (DomEvent @event) =>
                {
                    @event.preventDefault();
                    if (!playing || engine == null) return;
                    if (@event.shiftKey) engine.queueAsync(() => engine.uiDropToFloor(slot));
                    else engine.queueAsync(() => engine.uiUseInventorySlot(slot, engine.selectedCharacter));
                });
                drawIconInto(canvas, engine.getItemIconShapePtr(item), palette);
                if (count > 1) button.Append(mkEl("span", "ui-count", count.ToString()));
                bool quick = gameUi.hotbar.Contains(slot);
                button.ClassToggle("quick", quick);
                CssTooltip.MarkItem(button, item, -1, count);
                button.SetTitle($"{engine.itemTooltip(item)}{(count > 1 ? $" ({count} of them)" : "")}{(quick ? " - on the hotbar" : "")}");
                bool match = (string.IsNullOrEmpty(invFilter) || engine.uiItemKind(item) == invFilter) && (invSearch.Length == 0 || (engine.itemName(item) ?? "").ToLowerInvariant().Contains(invSearch));
                button.ClassToggle("filtered-out", !match);
                if (match) shown += count;
                return button;
            }
            // Unity build: a page of 48 squares at a time (the original bag's size), so a big bag keeps squares a hand
            // can hit instead of shrinking them to fit; with a search or a kind picked, only the matches are paged
            bool filtering = invSearch.Length != 0 || !string.IsNullOrEmpty(invFilter);
            bool matches(InvStack st) => (string.IsNullOrEmpty(invFilter) || engine.uiItemKind(st.item) == invFilter) && (invSearch.Length == 0 || (engine.itemName(st.item) ?? "").ToLowerInvariant().Contains(invSearch));
            var entries = (filtering ? stacks.Where(matches) : stacks).Cast<InvStack>().ToList();
            if (!filtering) for (int i = 0; i < free; i += 1) entries.Add(null);
            int pages = Math.Max(1, (entries.Count + INV_PAGE - 1) / INV_PAGE);
            invPage = Math.Max(0, Math.Min(pages - 1, invPage));
            foreach (var entry in entries.Skip(invPage * INV_PAGE).Take(INV_PAGE)) inventoryGrid.Append(cell(entry));
            // a short page keeps full-size squares: the rest of its rows are held by invisible ones
            for (int pad = entries.Skip(invPage * INV_PAGE).Take(INV_PAGE).Count(); pad < INV_PAGE; pad += 1)
            {
                var hold = Dom.El("button");
                hold.style.visibility = Visibility.Hidden;
                hold.pickingMode = PickingMode.Ignore;
                inventoryGrid.Append(hold);
            }
            shown = filtering ? stacks.Where(matches).Sum(st => st.count) : shown;
            renderInvPager(pages);

            if (invCount != null)
            {
                int total = inv.Count((i) => i != 0);
                if (onlyReagents) invCount.SetText($"{CraftingUi.pouchTotal()} reagents");
                else if (invSearch.Length != 0 || !string.IsNullOrEmpty(invFilter)) invCount.SetText($"{shown} of {total}");
                else invCount.SetText($"{total}/{inv.Length} slots · {stacks.Count} kinds");
            }
            int hand = engine.itemInHand;
            drawIconInto(handIcon, hand != 0 ? engine.getItemIconShapePtr(hand) : null, palette);
            handName.SetText(hand != 0 ? engine.itemName(hand) : "Empty hand");
            handUse.SetDisabled(hand == 0);
            handDrop.SetDisabled(hand == 0);
        }

        VisualElement inventoryOverlay;
        public void gameEvent(Lol.InputEvent @event) { if (playing && engine != null) engine.events.Add(@event); }
        public void clickGame(int x, int y) { if (!playing || engine == null) return; engine.pushMouse(x, y, 1); engine.events.Add(new Lol.InputEvent { type = "mouseup", x = x, y = y, button = 1 }); }
        // While a spell waits for its target (Heal), other actions would replace it: ask for the target instead.
        bool targetPending() { if (engine != null && engine.awaitingSpellTarget) { gameUi.message("Choose the hero to cast on first (click a hero card).", "system"); return true; } return false; }
        public void quickAttack() { if (playing && engine != null && !targetPending()) engine.queueAsync(() => engine.quickAttack()); }
        // Per-hero quick spells (by character id, kept across saves); heroes without one use the party default.
        const string QUICK_SPELLS_KEY = "lol.quickSpells";
        Dictionary<int, QuickSpell> quickSpells = new Dictionary<int, QuickSpell>();
        CharScreen charScreen;

        void init_B12()
        {
            inventoryOverlay = Q("#inventory-overlay");
            try { quickSpells = JsonSerializer.Deserialize<Dictionary<int, QuickSpell>>(localStorage.getItem(QUICK_SPELLS_KEY) ?? "null", B_json) ?? new Dictionary<int, QuickSpell>(); } catch (Exception) { /* ignore */ }
        }

        public void setHeroQuickSpell(int id, int slot, int level)
        {
            if (slot < 0) quickSpells.Remove(id); else quickSpells[id] = new QuickSpell { slot = slot, level = level };
            try { localStorage.setItem(QUICK_SPELLS_KEY, JsonSerializer.Serialize(quickSpells, B_json)); } catch (Exception) { /* ignore */ }
            sidebarKey = "";
        }

        QuickSpell heroQuickSpell(int c) => (engine != null && quickSpells.TryGetValue(engine.characters[c].id, out var q) ? q : null) ?? (quickSpell.slot >= 0 ? quickSpell : null);

        // C: the next ready hero casts *their* quick spell, then the selection moves on.
        public void quickCast()
        {
            if (!playing || engine == null || targetPending()) return;
            engine.queueAsync(async () =>
            {
                if ((engine.updateFlags & 3) != 0 || engine.weaponsDisabled || engine.needSceneRestore != 0 || engine.sysTimerPaused) return;
                bool canCast(int i)
                {
                    var q1 = heroQuickSpell(i);
                    if (q1 == null || engine.availableSpells[q1.slot] == -1) return false;
                    var sp = engine.@static.SpellProperties[engine.availableSpells[q1.slot]];
                    return sp.mpRequired[0] <= engine.characters[i].magicPointsCur && sp.hpRequired[0] < engine.characters[i].hitPointsCur; // any power will do (auto power)
                }
                // Strictly the selected hero: no quick spell / no mana / on cooldown passes the selection on.
                int c = engine.selectedCharacter;
                if (!engine.uiCanAct(c)) { engine.uiSelectNextAfter(c); return; }
                if (heroQuickSpell(c) == null) { engine.txt.printMessage(2, $"{engine.characters[c].name} has no quick spell: pick one on the character screen."); return; }
                if (!canCast(c)) { engine.txt.printMessage(2, $"{engine.characters[c].name} cannot cast that now."); engine.uiSelectNextAfter(c); return; }
                var q = heroQuickSpell(c);
                await engine.quickCastSpell(c, q.slot, q.level);
                engine.uiSelectNextAfter(c);
            });
        }

        void init_B13()
        {
            charScreen = new CharScreen(Q("#char-overlay"), (canvas, shape, palette) => drawIconInto(canvas, shape, palette), () => engine, () => (JsonObject)JsonSerializer.SerializeToNode(quickSpells, B_json), (id, slot, level) => setHeroQuickSpell(id, slot, level), (slot, id) => powerFor(slot, id.ToString()));
            charScreen.onEquipBest = equipBest;
            gameUi.onPortrait = (c) => { if (playing && compactActive) charScreen.open(c); };
            gameUi.onEquipBest = equipBest;
            if (charScreen != null) charScreen.onEquipBest = equipBest;
            gameUi.onSwap = (a, b) => { if (playing && engine != null && engine.uiSwapParty(a, b)) gameUi.partyKey = ""; };
            gameUi.getQuickSpell = (c) =>
            {
                if (engine == null || (engine.characters[c].flags & 1) == 0) return null;
                if (!quickSpells.TryGetValue(engine.characters[c].id, out var q) || q == null || engine.availableSpells[q.slot] == -1) return null;
                int spell = engine.availableSpells[q.slot];
                return new GameUi.QuickSpell { spell = spell, level = q.level, name = engine.spellName(spell) };
            };
        }
    }
}
