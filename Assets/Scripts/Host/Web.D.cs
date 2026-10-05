// src/main.mjs, section D (the `on` helper .. renderTrade): the camp (station, rest overlay, sheets,
// the imp, the stash chest, the cauldron, sleeping), map notes and markers, shops and the trade window.
// Ported 1:1, see docs/port/HOST.md. C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lol;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>main.mjs sleeping: { hours, started, timer }</summary>
    public sealed class SleepState
    {
        public int hours;
        public double started;
        public int timer;
    }

    /// <summary>main.mjs hintMark: { block, until }</summary>
    public sealed class HintMark
    {
        public int block;
        public double until;
    }

    /// <summary>main.mjs tradeSelected: { item } (an item instance) or { type } (a ware), with its price.</summary>
    public sealed class TradeSelection
    {
        public int item, type, price;
    }

    public sealed partial class Web
    {
        void on(string id, Action<DomEvent> fn) { var el = Q(id); if (el != null) el.On("click", (DomEvent @event) => { fn(@event); el.BlurEl(); }); }
        void on(string id, Action fn) => on(id, (DomEvent @event) => fn());

        void init_D01()
        {
            on("#attack", quickAttack);
            on("#quick-spell", quickCast);
        }

        // Rest overlay: camp picture, per-hero recovery bars, wake-up button, and the reason the rest ended.
        bool restShown = false;
        int restLogAt = 0;
        // Stepping up to one of the camp's doors shows what is behind it, filling the scene window the
        // way the game shows a shop counter when you walk up to one. It is only ever drawn when the party
        // is standing at that wall facing it - never while they walk around the room.
        string campStationKey = "";
        /// <summary>Unity build: StreamingAssets/camp (HostMain), the camp stations' free-licensed pictures</summary>
        public string campArtFolder;
        void renderCampStation(bool camping)
        {
            var layer = Q("#camp-station");
            if (layer == null) return;
            var view = camping ? engine.uiCampView() : null;
            var piece = view != null ? LandsOfLore.CAMP_PIECES.FirstOrDefault((p) => p.name == view.piece) : null;
            var key = piece != null ? $"{piece.name}:{view.distance}" : "";
            if (key == campStationKey) return;
            campStationKey = key;
            layer.SetHidden(piece == null);
            layer.ReplaceChildren();
            if (piece == null) return;
            var button = mkEl("button", "camp-station-art");
            button.SetAttr("type", "button");
            // Across the room it sits inside the far doorway; up close it fills the view.
            if (view.distance == 2) button.AddToClassList("far");
            button.SetTitle(view.distance == 1 ? $"{piece.label} - click to open it" : $"{piece.label} - walk up to it");
            var art = Dom.El("img");
            // Unity build: free-licensed scenes shipped in StreamingAssets/camp (tools/gen_camp_scenes.py); the animated
            // ones are numbered frames, played while the station is in view
            var frames = new List<string>();
            if (campArtFolder != null && System.IO.Directory.Exists(campArtFolder))
            {
                string still = System.IO.Path.Combine(campArtFolder, $"scene-{piece.name}.jpg");
                if (System.IO.File.Exists(still)) frames.Add(still);
                else frames.AddRange(System.IO.Directory.GetFiles(campArtFolder, $"scene-{piece.name}_*.jpg").OrderBy(f => f, StringComparer.Ordinal));
            }
            art.SetAttr("src", frames.Count > 0 ? frames[0] : piece.scene);
            if (frames.Count > 1)
            {
                int frame = 0;
                string mine = key;
                int timer = 0;
                timer = timers.setInterval(() =>
                {
                    if (campStationKey != mine || art.panel == null) { timers.clearInterval(timer); return; }
                    frame = (frame + 1) % frames.Count;
                    art.SetAttr("src", frames[frame]);
                }, piece.name == "imp" ? 180 : 70);
            }
            art.SetAttr("alt", piece.label);
            button.Append(art, mkEl("span", "camp-station-label", piece.label));
            button.On("click", () => openCampSheet(piece.name));
            layer.Append(button);
        }

        string restBarsKey;

        void updateRest()
        {
            var box = Q("#rest-overlay");
            if (box == null || engine == null) return;
            var camping = playing && engine.uiInCamp();
            if (camping && !restShown) { restShown = true; restLogAt = gameUi.log.Count; box.SetHidden(false); }
            if (!camping && restShown)
            {
                restShown = false;
                box.SetHidden(true);
                engine.campMode = false;
                cancelTeleport();
                closeCampSheet();
            }
            renderCampStation(camping);
            if (!camping) return;
            // Something walked up to where the party left their things: the camp breaks.
            if (engine.uiCampIntruded())
            {
                closeCampSheet();
                cancelTeleport();
                engine.uiLeaveCamp();
                toast("Something found the camp. You are back on your feet.", "fx-toast danger");
                gameUi.message("Something found the camp: the party breaks it and takes up arms.", "system");
                return;
            }
            var healed = engine.uiCampRested();
            var title = Q("#rest-title");
            if (title != null) title.SetText(healed ? "Camp · rested" : "Camp · resting…");
            if (!Q("#camp-craft").IsHidden()) renderCraft();
            if (!Q("#camp-imp").IsHidden()) renderImp();
            if (!Q("#camp-stash").IsHidden()) renderStash();
            var bars = Q("#rest-bars");
            // rebuilt only when a value changes (the same rows; fresh rows every frame are laid out a frame late here)
            string barsKey = string.Join("|", engine.characters.Where((c) => (c.flags & 1) != 0).Select((c) => $"{c.name},{c.hitPointsCur},{c.hitPointsMax},{c.magicPointsCur},{c.magicPointsMax}"));
            if (barsKey == restBarsKey && bars.childCount > 0) return;
            restBarsKey = barsKey;
            var resting = engine.characters.Where((c) => (c.flags & 1) != 0).ToList();
            // the same heroes: the rows are updated in place (fresh rows are laid out a frame late: they jumped)
            if (bars.childCount == resting.Count && resting.Select((c, i) => ((VisualElement)bars[i])[0].GetText() == c.name).All((x) => x))
            {
                for (int i = 0; i < resting.Count; i += 1)
                {
                    var c = resting[i]; var row = (VisualElement)bars[i];
                    row[1][0].SetStyle("width", $"{Js.Round((100.0 * c.hitPointsCur) / Math.Max(1, c.hitPointsMax))}%");
                    row[2].SetText($"{c.hitPointsCur}/{c.hitPointsMax}");
                    row[3][0].SetStyle("width", $"{Js.Round((100.0 * c.magicPointsCur) / Math.Max(1, c.magicPointsMax))}%");
                    row[4].SetText($"{c.magicPointsCur}/{c.magicPointsMax}");
                }
                return;
            }
            bars.ReplaceChildren(engine.characters.Where((c) => (c.flags & 1) != 0).Select((c) =>
            {
                var row = mkEl("div", "rest-row");
                var hp = mkEl("div", "rest-bar hp"); hp.Append(mkEl("i")); hp[0].SetStyle("width", $"{Js.Round((100.0 * c.hitPointsCur) / Math.Max(1, c.hitPointsMax))}%");
                var mp = mkEl("div", "rest-bar mp"); mp.Append(mkEl("i")); mp[0].SetStyle("width", $"{Js.Round((100.0 * c.magicPointsCur) / Math.Max(1, c.magicPointsMax))}%");
                row.Append(mkEl("b", "", c.name), hp, mkEl("span", "", $"{c.hitPointsCur}/{c.hitPointsMax}"), mp, mkEl("span", "", $"{c.magicPointsCur}/{c.magicPointsMax}"));
                return row;
            }).ToList());
        }

        // The camp is the picture: the cauldron and the travelling circle are the two things to click,
        // and each opens its own sheet over the scene.
        static readonly Dictionary<string, string[]> CAMP_SHEETS = new Dictionary<string, string[]>
        {
            ["craft"] = new[] { "#camp-craft", "The cauldron" },
            ["travel"] = new[] { "#camp-travel", "The travelling circle" },
            ["imp"] = new[] { "#camp-imp", "The imp trader" },
            ["stash"] = new[] { "#camp-stash", "The stash chest" },
        };
        void openCampSheet(string which)
        {
            var sheet = Q("#camp-sheet");
            if (sheet == null || which == null || !CAMP_SHEETS.ContainsKey(which)) return;
            sheet.SetHidden(false);
            Q(".camp-hud")?.SetHidden(true);   // the sheet takes the scene: the camp's panel waits under it
            Q("#camp-sheet-title").SetText(CAMP_SHEETS[which][1]);
            foreach (var entry in CAMP_SHEETS) Q(entry.Value[0]).SetHidden(entry.Key != which);
            if (which == "craft") renderCraft(true);
            else if (which == "travel") { travelLocked = null; updateTravelLock(); renderTravelBoard(); }
            else if (which == "imp") renderImp(true);
            else if (which == "stash") renderStash(true);
        }
        void closeCampSheet()
        {
            var sheet = Q("#camp-sheet");
            if (sheet != null) sheet.SetHidden(true);
            Q(".camp-hud")?.SetHidden(false);
        }

        void init_D02()
        {
            on("#spot-brew", () => openCampSheet("craft"));
            on("#spot-travel", () => openCampSheet("travel"));
            on("#spot-imp", () => openCampSheet("imp"));
            on("#spot-stash", () => openCampSheet("stash"));
            on("#camp-sheet-close", () => closeCampSheet());
            foreach (var tab in QAll(".imp-tab")) tab.On("click", () => { impTab = tab.GetAttr("data-imp"); renderImp(true); });
            {
                var search = Q("#stash-search") as DomInput;
                if (search != null) search.On("input", () => { stashSearch = search.value.Trim().ToLowerInvariant(); renderStash(true); });
            }
        }

        // The imp trader: reagents over the counter, spells out of his book, and he buys what you do not
        // want to carry. Rebuilt only on a change, so a press always lands on the button it started on.
        PouchApi pouchApi;
        void init_D03()
        {
            pouchApi = new PouchApi { read = CraftingUi.readPouch, write = CraftingUi.writePouch };
        }
        string impTab = "reagents";
        string impKey = "";
        // "He buys": a search, a kind and an order over the pack (Unity build)
        string impSearch = "", impFilter = "", impSort = "";
        VisualElement impTools;
        void makeImpTools(VisualElement list)
        {
            impTools = Dom.El("div", "imp-tools");
            var search = (DomInput)Dom.El("input");
            search.SetAttr("type", "search"); search.SetAttr("placeholder", "Search…");
            search.On("input", () => { impSearch = search.value.Trim().ToLowerInvariant(); renderImp(true); });
            var filter = (DomSelect)Dom.El("select");
            foreach (var (v, t) in new[] { ("", "everything"), ("weapon", "weapons"), ("armour", "armour"), ("usable", "usable"), ("other", "other") }) filter.AddOption(v, t, false, v == "");
            filter.Refresh();
            filter.On("change", () => { impFilter = filter.value; renderImp(true); });
            var sort = (DomSelect)Dom.El("select");
            foreach (var (v, t) in new[] { ("", "as carried"), ("name", "by name"), ("price", "by price") }) sort.AddOption(v, t, false, v == "");
            sort.Refresh();
            sort.On("change", () => { impSort = sort.value; renderImp(true); });
            impTools.Append(search, filter, sort);
            list.parent.Insert(list.parent.IndexOf(list), impTools);
        }
        string impSaid = "";
        static readonly JsonSerializerOptions JsonFieldsD = new JsonSerializerOptions { IncludeFields = true };
        // A redraw after a deal changes a count, a price or a button: the rows already shown are updated in place and
        // only the ones gone are taken out. Rebuilding the whole list drew its new rows unstyled for a frame (a flash,
        // the scroll jumping); a list that changed in kind (another tab, a filter) is still built anew.
        readonly Dictionary<VisualElement, Action> impActions = new Dictionary<VisualElement, Action>();
        void patchList(VisualElement list, params object[] items)
        {
            var fresh = new List<VisualElement>();
            foreach (var c in items) { if (c is VisualElement v) fresh.Add(v); else if (c is IEnumerable<VisualElement> many) fresh.AddRange(many); }
            var old = list.Children().Where(c => c.name != "scrollbar").ToList();
            string key(VisualElement e) => e.GetAttr("data-key");
            // the new rows must be the old ones less some, in the same order, each matching in shape
            var keep = new List<(VisualElement was, VisualElement now)>();
            int at = 0;
            bool patchable = fresh.All(n => key(n) != null) && old.All(o => key(o) != null);
            if (patchable)
                foreach (var n in fresh)
                {
                    while (at < old.Count && (key(old[at]) != key(n) || !sameShape(old[at], n))) at += 1;
                    if (at == old.Count) { patchable = false; break; }
                    keep.Add((old[at], n)); at += 1;
                }
            if (!patchable) { foreach (var o in old) forget(o); list.ReplaceChildren(fresh); return; }
            foreach (var o in old.Except(keep.Select(k => k.was)).ToList()) { forget(o); o.RemoveFromHierarchy(); }
            foreach (var (was, now) in keep) copyInto(was, now);
            CssLayout.KeepScroll(list);
        }
        static bool sameShape(VisualElement a, VisualElement b)
        {
            if (a.GetType() != b.GetType() || a.childCount != b.childCount) return false;
            for (int i = 0; i < a.childCount; i += 1) if (!sameShape(a[i], b[i])) return false;
            return true;
        }
        void copyInto(VisualElement was, VisualElement now)
        {
            if (was is TextElement ta && now is TextElement tb && ta.text != tb.text) ta.text = tb.text;
            if (was.enabledSelf != now.enabledSelf) was.SetEnabled(now.enabledSelf);
            if (impActions.TryGetValue(now, out var act)) { impActions[was] = act; impActions.Remove(now); }
            for (int i = 0; i < was.childCount; i += 1) copyInto(was[i], now[i]);
        }
        void forget(VisualElement e) { impActions.Remove(e); foreach (var c in e.Children()) forget(c); }

        void renderImp(bool force = false)
        {
            var box = Q("#camp-imp");
            if (box == null || engine == null) return;
            var pouch = CraftingUi.readPouch();
            var job = CampStoreUi.readJob(engine);
            var kills = stats.familyKills ?? new Dictionary<string, int>();
            // the credits count up or down a few a frame after a deal (as in the original): the list is redrawn only
            // when what can be afforded changes, not on every step of the count (it was rebuilt every frame and jumped)
            int credits = engine.credits;
            var afford = string.Concat(LandsOfLore.REAGENT_PRICE.Values.Concat(LandsOfLore.EXTRA_SPELLS.Select(d => d.price)).Concat(LandsOfLore.UPGRADES.Select(u => u.price)).Append(engine.uiRepairCost().price).Select(p => credits >= p ? '1' : '0'));
            var note = Q("#imp-note");
            note.SetText(!string.IsNullOrEmpty(impSaid) ? $"{impSaid} You have {credits} crowns." : $"You have {credits} crowns.");
            var key = $"{impTab}|{impSearch}|{impFilter}|{impSort}|{(engine.uiInDungeon() ? "pit" + engine.uiDungeonInfo().depth : "")}|{JsonSerializer.Serialize(DungeonRun.readRun(engine), JsonFieldsD)}|{afford}|{string.Join(",", pouch.Values)}|{string.Join(",", engine.inventory)}|{string.Join(",", engine.availableSpells)}|{(job != null ? job.kind + job.need : "-")}|{string.Join(",", kills.Values)}|{string.Join(",", engine.uiCampReserveNames())}|{string.Join(",", engine.characters.Select(c => c.id))}";
            if (!force && key == impKey) return;
            // the same tab redrawn (a purchase, a sale) stays where it was scrolled to
            bool sameTab = impKey.Split('|')[0] == impTab;
            impKey = key;
            foreach (var tab in box.QAll(".imp-tab")) tab.ClassToggle("on", tab.GetAttr("data-imp") == impTab);
            var list = Q("#imp-list");
            if (!sameTab) list.SetScrollTop(0);   // the same tab redrawn keeps its scroll, as the browser does
            if (impTools == null) makeImpTools(list);
            impTools.SetHidden(impTab != "sell");
            // whatever he last said stays on screen (set above): the redraw that follows a purchase used to wipe it
            VisualElement row(VisualElement icon, string title, string sub, Action action, string label, bool disabled, string key = null)
            {
                var r = mkEl("div", "imp-row");
                r.SetAttr("data-key", key ?? title);
                var text = mkEl("div", "imp-what");
                text.Append(mkEl("b", "", title), mkEl("span", "imp-sub", sub));
                var b = mkEl("button", "imp-do", label);
                b.SetAttr("type", "button");
                b.SetDisabled(disabled);
                impActions[b] = action;
                b.On("click", () => { if (impActions.TryGetValue(b, out var act)) act(); });
                if (icon != null) r.Append(icon);
                r.Append(text, b);
                return r;
            }
            // JS say(result, ok) reads result.error: the C# results are of different types, so the caller passes it.
            void say(string error, string ok) { impSaid = !string.IsNullOrEmpty(error) ? error : ok; impKey = impTab + "|!"; renderImp(true); }
            if (impTab == "reagents")
            {
                patchList(list, LandsOfLore.REAGENTS.Select((e) =>
                {
                    var k = e.Key; var r = e.Value;
                    var line = mkEl("div", "imp-pair");
                    line.SetAttr("data-key", k);
                    line.Append(
                        row(null, r.name, $"{LandsOfLore.REAGENT_PRICE[k]} crowns · you have {pouch[k]}", () => say(CampStoreUi.buyReagent(engine, k, pouchApi).error, $"Bought {r.name.ToLowerInvariant()}."), "Buy", engine.credits < LandsOfLore.REAGENT_PRICE[k]),
                        row(null, "", $"he pays {Math.Max(1, Js.Round(LandsOfLore.REAGENT_PRICE[k] / 2.0))}", () => say(CampStoreUi.sellReagent(engine, k, pouchApi).error, $"Sold {r.name.ToLowerInvariant()}."), "Sell", pouch[k] == 0)
                    );
                    return line;
                }).ToList());
            }
            else if (impTab == "spells")
            {
                var ids = engine.uiRegisterExtraSpells();
                patchList(list, LandsOfLore.EXTRA_SPELLS.Select((def) =>
                {
                    var known = engine.uiKnowsSpell(ids[def.id]);
                    return row(SpellWidget.spellIcon(def.icon, 24), def.name, known ? "already in the spell book" : $"{def.price} crowns · {def.about}",
                        () => say(CampStoreUi.buySpell(engine, def).error, $"{def.name} written into the book."), known ? "Known" : "Buy", known || engine.credits < def.price);
                }).ToList());
            }
            else if (impTab == "jobs")
            {
                // One errand at a time: take it, do it, get paid, take the next.
                if (job == null || string.IsNullOrEmpty(job.kind))
                {
                    patchList(list, row(null, "No errand", $"He has run {(job != null ? job.done : 0)} for you so far.",
                        () => { CampStoreUi.acceptJob(engine, stats.bestiary.Where(kv => kv.Value.kills > 0).Select(kv => kv.Key)); impSaid = "The imp scratches out an errand."; impKey = impTab + "|!"; renderImp(true); renderErrand(); }, "Ask him", false));
                }
                else
                {
                    var have = CampStoreUi.jobProgress(engine, job);
                    var ready = have >= job.need;
                    var kin = !string.IsNullOrEmpty(job.family) ? stats.bestiary.Keys.Where((n) => LandsOfLore.familyOf(n).id == job.family).ToList() : new List<string>();
                    var where = kin.SelectMany((n) => (stats.bestiary[n].levels ?? new List<int> { stats.bestiary[n].level }).Where((l) => l != 0)).Distinct()
                        .Select((l) => engine.levelName(l)).ToList();
                    var old = job.kind == "fetch"; // an errand from before the trophies: it can only be given up
                    var line = row(null, engine.uiJobText(job), $"{have}/{job.need} · pays {CampStoreUi.jobPay(engine, job)} crowns{(old ? " · an old errand: give it up and he will think of another" : "")}{(!string.IsNullOrEmpty(job.hint) ? $" · {job.hint}" : "")}{(kin.Count > 0 ? $" · e.g. {string.Join(", ", kin.Take(4))}" : "")}{(where.Count > 0 ? $" · seen in {string.Join(", ", where.Take(3))}" : "")}",
                        () => { var r = CampStoreUi.claimJob(engine); renderErrand(); say(r.error, $"The imp pays you {CampStoreUi.jobPay(engine, job)} crowns. {impGossip()}"); }, ready ? "Hand them over" : "Not yet", !ready);
                    // Any errand can be handed back, and an old one has to be: nothing pays for it now.
                    var drop = mkEl("button", "imp-do", "Give it up");
                    drop.SetAttr("type", "button");
                    drop.SetTitle("Hand the errand back. Nothing is paid, and he will have another for you.");
                    drop.On("click", () => { CampStoreUi.abandonJob(engine); renderErrand(); say(null, "The imp shrugs and crosses the errand out."); });
                    line.Append(drop);
                    patchList(list, line);
                }
            }
            else if (impTab == "pit")
            {
                // The endless dungeon. Inside a floor he only offers the way back up.
                if (engine.uiInDungeon())
                {
                    var info = engine.uiDungeonInfo();
                    patchList(list, row(null, $"Floor {info.depth}", info.floorDone ? "He can pull you back up. Everything you earned is yours." : "He can pull you back up. This floor stays unbeaten; the ones before it are paid.",
                        () => abandonDungeon(), "Return", false));
                }
                else
                {
                    var record = DungeonRun.readRun(engine);
                    var rows = new List<VisualElement>();
                    for (int floor = 1; floor <= record.cleared + 1; floor += 1)
                    {
                        int fl = floor; // the JS `let` gives each click its own floor
                        var plan = LandsOfLore.floorPlan(floor, visited, engine.currentLevel);
                        if (plan == null) break;
                        var def = LandsOfLore.OBJECTIVES.FirstOrDefault((o) => o.id == plan.objective) ?? LandsOfLore.OBJECTIVES[0];
                        var bestNode = record.best?[floor.ToString(CultureInfo.InvariantCulture)];
                        var beaten = bestNode != null && double.TryParse(bestNode.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bestN) ? (int)bestN : 0;
                        rows.Add(row(null, $"Floor {floor}",
                            $"{LandsOfLore.floorSizeName(plan.depth)} · {def.label.ToLowerInvariant()} · {engine.levelName(plan.level)} stone · {LandsOfLore.monsterCount(plan.depth)} of them{(beaten != 0 ? $" · beaten {beaten}×" : "")} · go as deep as you dare",
                            () => { impSaid = $"Down you go, floor {fl}."; _ = enterDungeon(fl); }, floor > record.cleared ? "Enter" : "Again", false));
                    }
                    patchList(list, rows.Count > 0 ? rows
                        : new List<VisualElement> { mkEl("p", "panel-note", "He has nowhere to send you yet: walk another level first.") });
                }
            }
            else if (impTab == "camp")
            {
                var built = CampStoreUi.readUpgrades(engine);
                var rows = new List<VisualElement>();
                if (settings.wear)
                {
                    var cost = engine.uiRepairCost();
                    var wear = cost.wear; var price = cost.price;
                    rows.Add(row(null, "Grind the blades", wear != 0 ? $"{price} crowns · every weapon back to new" : "nothing is dull",
                        () => say(engine.uiRepairAll().error, "The imp grinds every blade back to an edge."), "Repair", wear == 0 || engine.credits < price));
                }
                // Unity build: the companions waiting in the camp, swapped for one in the party (Companions.cs)
                var waiting = engine.uiCampReserveNames();
                for (int w = 0; w < waiting.Count; w += 1)
                {
                    int wi = w;
                    for (int c = 0; c < 4; c += 1)
                    {
                        var ch = engine.characters[c];
                        if ((ch.flags & 1) == 0 || ch.id <= 0) continue;   // never the champion
                        int slot = c; string who = ch.name, back = waiting[wi];
                        rows.Add(row(null, $"{back} waits in the camp", $"Bring {back} back; {who} waits here instead.",
                            () => { engine.queueAsync(() => { engine.uiSwapCompanion(wi, slot); gameUi.partyKey = ""; impSaid = $"The imp calls {back} over. {who} sits down by the fire."; impKey = impTab + "|!"; renderImp(true); return System.Threading.Tasks.Task.CompletedTask; }); },
                            $"Swap with {who}", false));
                    }
                }
                patchList(list, rows, LandsOfLore.UPGRADES.Select((u) =>
                {
                    var has = built.Contains(u.id);
                    return row(null, u.name, has ? "already built" : $"{u.price} crowns · {u.about}",
                        () => { var r = CampStoreUi.buyUpgrade(engine, u); if (string.IsNullOrEmpty(r.error)) { engine.campHealBoost = CampStoreUi.hasUpgrade(engine, "hearth") ? 2 : 1; stashKey = ""; } say(r.error, $"The imp builds the {u.name.ToLowerInvariant()}."); },
                        has ? "Built" : "Buy", has || engine.credits < u.price);
                }).ToList());
            }
            else
            {
                // one row per kind of thing, as the bag shows it: selling takes one off the pile
                var piles = new List<(int item, int slot, int count, UiResult offer, int kind)>();   // slot: the pile's last one, sold first (the row keeps its place)
                var byKind = new Dictionary<int, int>();
                for (int slot = 0; slot < engine.inventory.Length; slot += 1)
                {
                    int item = engine.inventory[slot];
                    if (item == 0) continue;
                    int kind = engine.itemsInPlay[item] != null ? engine.itemsInPlay[item].itemPropertyIndex : -1 - slot;
                    if (byKind.TryGetValue(kind, out int at)) { var p = piles[at]; p.count += 1; p.slot = slot; p.item = item; piles[at] = p; continue; }
                    byKind[kind] = piles.Count;
                    piles.Add((item, slot, 1, CampStoreUi.impOffer(engine, item), kind));
                }
                var shown = piles.Where(p => (string.IsNullOrEmpty(impFilter) || engine.uiItemKind(p.item) == impFilter)
                    && (impSearch.Length == 0 || Or(engine.itemName(p.item), "").ToLowerInvariant().Contains(impSearch))).ToList();
                if (impSort == "name") shown = shown.OrderBy(p => Or(engine.itemName(p.item), ""), StringComparer.OrdinalIgnoreCase).ToList();
                else if (impSort == "price") shown = shown.OrderByDescending(p => p.offer.refused ? -1 : p.offer.price).ToList();
                patchList(list, shown.Count > 0
                    ? shown.Select((e) =>
                    {
                        var item = e.item; var slot = e.slot; var offer = e.offer;
                        var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon");
                        drawIconInto(icon, engine.getItemIconShapePtr(item), engine.uiPalette());
                        return row(icon, Or(engine.itemName(item), "Unknown") + (e.count > 1 ? $"  x{e.count}" : ""), offer.refused ? offer.reason : $"he pays {offer.price}",
                            () => { var sold = Or(engine.itemName(item), "it"); say(CampStoreUi.sellToImp(engine, slot).error, $"Sold {sold}."); }, "Sell", offer.refused, $"kind{e.kind}");
                    }).ToList()
                    : new List<VisualElement> { mkEl("p", "panel-note", piles.Count > 0 ? "Nothing in the pack matches." : "Your pack is empty.") });
            }
        }

        /// <summary>JS `a || b` for strings.</summary>
        static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;

        // Payment loosens his tongue: he names a way out nobody has walked, or what you left lying about on
        // this level. Both are read from what the game already knows, so he never invents anything.
        string impGossip()
        {
            if (engine == null) return "";
            string name(int l) => engine.levelName(l);
            var leads = new List<(int from, int to)>();
            foreach (var entry in visited) foreach (var to in (entry.Value?.exits ?? new List<int>())) if (!visited.ContainsKey(to) || visited[to] == null) leads.Add((entry.Key, to));
            var dropped = new List<string>();
            for (int b = 0; b < 1024; b += 1)
            {
                if ((engine.levelBlockProperties[b].flags & 7) != 7) continue;
                foreach (var f in engine.uiItemsOnBlock(b)) if (!string.IsNullOrEmpty(f.name)) dropped.Add(f.name);
            }
            var lines = new List<string>();
            if (leads.Count > 0) { var (from, to) = leads[UnityEngine.Random.Range(0, leads.Count)]; lines.Add($"He says nobody has taken the way from {name(from)} to {name(to)}."); }
            if (dropped.Count > 0) lines.Add($"He says {(dropped.Count == 1 ? "something is" : $"{dropped.Count} things are")} still lying about in {name(engine.currentLevel)}: {string.Join(", ", dropped.Distinct().Take(3))}.");
            return lines.Count > 0 ? lines[UnityEngine.Random.Range(0, lines.Count)] : "";
        }

        // The stash chest: 144 slots that stay in the camp.
        string stashSearch = "";
        string stashKey = "";
        void renderStash(bool force = false)
        {
            var box = Q("#camp-stash");
            if (box == null || engine == null) return;
            var chest = CampStoreUi.readStash(engine);
            var key = $"{string.Join(",", engine.inventory)}|{string.Join(",", chest.Select((e) => (e != null ? e.prop : 0)))}|{stashSearch}|{string.Join("|", engine.characters.Select((c) => string.Join("-", c.items)))}";
            if (!force && key == stashKey) return;
            stashKey = key;
            var used = CampStoreUi.stashCount(engine);
            Q("#stash-count").SetText($"{used} of {CampStoreUi.stashSlots(engine)} slots used");
            var note = Q("#stash-note");
            void say(string error, string ok) { note.SetText(!string.IsNullOrEmpty(error) ? error : ok); stashKey = ""; inventoryKey = ""; renderStash(true); }
            bool match(string name) => string.IsNullOrEmpty(stashSearch) || (name ?? "").ToLowerInvariant().Contains(stashSearch);
            VisualElement line(string name, VisualElement icon, Action action, string label)
            {
                var r = mkEl("div", "stash-row");
                if (icon != null) r.Append(icon);
                r.Append(mkEl("span", "stash-name", Or(name, "Unknown")));
                var b = mkEl("button", "stash-do", label);
                b.SetAttr("type", "button");
                b.On("click", action);
                r.Append(b);
                return r;
            }
            var bag = engine.inventory.Select((item, slot) => (item: (int)item, slot)).Where((e) => e.item != 0 && match(engine.itemName(e.item))).ToList();
            Q("#stash-bag").ReplaceChildren(bag.Count > 0
                ? bag.Select((e) =>
                {
                    var item = e.item; var slot = e.slot;
                    var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon");
                    drawIconInto(icon, engine.getItemIconShapePtr(item), engine.uiPalette());
                    var itemName = engine.itemName(item);
                    return line(itemName, icon, () => { var r = CampStoreUi.stashItem(engine, slot); say(r.error, $"Stashed {Or(Or(r.name, itemName), "it")}."); }, "Store →");
                }).ToList()
                : new List<VisualElement> { mkEl("p", "panel-note", "Nothing to store.") });
            ExtraItemDef extraOf(int prop) => engine.extraItems != null && engine.extraItems.TryGetValue(prop, out var x) ? x : null;
            var inChest = chest.Select((entry, slot) => (entry, slot)).Where((e) => e.entry != null).ToList();
            var shown = inChest.Where((e) => match(extraOf(e.entry.prop) != null ? extraOf(e.entry.prop).name : engine.getLangString(e.entry.prop >= 0 && e.entry.prop < engine.itemProperties.Count && engine.itemProperties[e.entry.prop] != null ? engine.itemProperties[e.entry.prop].nameStringId : 0))).ToList();
            Q("#stash-chest").ReplaceChildren(shown.Count > 0
                ? shown.Select((e) =>
                {
                    var entry = e.entry; var slot = e.slot;
                    var info = engine.itemInfoForProperty(entry.prop);
                    var extra = extraOf(entry.prop);
                    var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon");
                    var shape = extra != null && !string.IsNullOrEmpty(extra.art) ? new Shape { width = 1, height = 1, pixels = new byte[0], art = extra.art } : (info != null ? engine.itemIconShapes[engine.itemProperties[entry.prop].shpIndex] : null);
                    drawIconInto(icon, shape, engine.uiPalette());
                    var name = extra != null ? extra.name : Or(info?.name, "Unknown");
                    return line(name, icon, () => say(CampStoreUi.takeFromStash(engine, slot).error, $"Took {name}."), "← Take");
                }).ToList()
                : new List<VisualElement> { mkEl("p", "panel-note", used != 0 ? "Nothing of that name in the chest." : "The chest is empty.") });
            // Loadouts: one remembered set of gear per hero, rebuilt from the pack and the chest.
            var board = Q("#stash-loadouts");
            if (board != null) board.ReplaceChildren(mkEl("h5", "", "Loadouts"), engine.characters.Select((ch, c) =>
            {
                if ((ch.flags & 1) == 0) return null;
                var set = CampStoreUi.readLoadout(engine, c);
                var r = mkEl("div", "stash-row");
                r.Append(mkEl("span", "stash-name", $"{ch.name} · {(set != null ? $"{set.worn.Count} pieces remembered" : "nothing remembered")}"));
                var save = mkEl("button", "stash-do", "Remember");
                save.SetAttr("type", "button");
                save.On("click", () => { var res = CampStoreUi.saveLoadout(engine, c); say(res.error, $"Remembered what {ch.name} wears."); });
                var wear = mkEl("button", "stash-do", "Wear");
                wear.SetAttr("type", "button");
                wear.SetDisabled(set == null);
                wear.On("click", async () =>
                {
                    var res = await CampStoreUi.wearLoadout(engine, c);
                    say(res.error, res.worn != 0 ? $"{ch.name} puts on {res.worn} piece{(res.worn == 1 ? "" : "s")}{(res.missing != 0 ? $", {res.missing} missing" : "")}." : "Nothing to change.");
                });
                r.Append(save, wear);
                return r;
            }).Where((r) => r != null).ToList());
        }

        // Camp crafting: what the pouch holds and what it can be brewed into.
        string craftKey = "";
        void renderCraft(bool force = false)
        {
            var box = Q("#camp-craft");
            if (box == null || engine == null) return;
            // The cauldron can be walked up to whether or not crafting is switched on. Rendering nothing left
            // the player staring at an empty panel, so say what is wrong and where the switch is.
            if (!settings.craft)
            {
                Q("#craft-pouch").ReplaceChildren();
                Q("#craft-list").ReplaceChildren(mkEl("div", "craft-empty",
                    "Crafting is switched off. Turn on “Crafting” in Settings and slain monsters will start leaving reagents for the cauldron."));
                var off = Q("#craft-note");
                if (off != null) off.SetHidden(true);
                craftKey = "";
                return;
            }
            var pouch = CraftingUi.readPouch();
            // Rebuilding the rows every frame would destroy the button between mousedown and mouseup, and
            // the click would never fire. Only redraw when what the cauldron can do actually changed.
            var kills = stats.familyKills ?? new Dictionary<string, int>();
            var key = $"{string.Join(",", pouch.Values)}|{engine.inventory.Count((i) => i != 0)}|{string.Join(",", kills.Values)}";
            if (!force && key == craftKey) return;
            craftKey = key;
            var pouchBox = Q("#craft-pouch");
            var have = LandsOfLore.REAGENTS.Where((e) => pouch[e.Key] > 0).ToList();
            pouchBox.ReplaceChildren(have.Count > 0
                ? have.Select((e) => { var chip = mkEl("span", "craft-chip", $"{e.Value.name} ×{pouch[e.Key]}"); chip.SetTitle(e.Value.hint); return chip; }).ToList()
                : new List<VisualElement> { mkEl("span", "craft-empty", "The pouch is empty. Slain monsters sometimes leave reagents.") });
            var list = Q("#craft-list");
            list.ReplaceChildren(LandsOfLore.RECIPES.Select((recipe) =>
            {
                var row = mkEl("div", "craft-row");
                // A recipe you have not learnt yet shows what to hunt for, not what it needs.
                var known = CraftingUi.recipeKnown(recipe, kills);
                var ready = known && CraftingUi.canCraft(recipe, pouch);
                var needs = known
                    ? string.Join(" · ", recipe.needs.Select((n) => $"{LandsOfLore.REAGENTS[n.Key].name} {pouch[n.Key]}/{n.Value}"))
                    : LandsOfLore.recipeHint(recipe);
                var text = mkEl("div", "craft-what");
                text.Append(mkEl("b", "", known ? recipe.name : "Unknown recipe"), mkEl("span", "craft-effect", known ? recipe.effect : "Not yet learnt"), mkEl("span", "craft-needs", needs));
                var button = mkEl("button", "craft-do", "Brew");
                button.SetAttr("type", "button");
                button.SetDisabled(!ready);
                row.ClassToggle("locked", !known);
                button.On("click", () =>
                {
                    var note = Q("#craft-note");
                    void fail(string why) { if (note != null) { note.SetText(why); note.SetHidden(false); note.RemoveFromClassList("ok"); } gameUi.message(why, "system"); toast(why, "fx-toast-ach"); }
                    UiResult result;
                    try
                    {
                        result = CraftingUi.craft(engine, recipe);
                    }
                    catch (Exception error)
                    { // never fail silently: the player has to see why the cauldron refused
                        log($"Crafting failed: {error.Message}");
                        fail($"The cauldron refuses: {error.Message}");
                        return;
                    }
                    if (!string.IsNullOrEmpty(result.error)) { fail(result.error); return; }
                    gameUi.message($"Brewed {recipe.name.ToLowerInvariant()}. It is in slot {result.slot + 1}.", "system");
                    inventoryKey = "";
                    renderCraft(true);
                    var done = Q("#craft-note"); // renderCraft did not rebuild it, but be safe
                    if (done != null) { done.SetText($"{recipe.name} brewed — it is in bag slot {result.slot + 1}."); done.SetHidden(false); done.AddToClassList("ok"); }
                });
                row.ClassToggle("ready", ready);
                row.Append(text, button);
                return row;
            }).ToList());
        }

        // Sleeping: hours pass in a few seconds, the party wakes mended, and a full night stirs the level
        // up again. Monsters finding the camp cut it short, and only the hours already slept count.
        SleepState sleeping = null; // { hours, until, timer }
        double sleptHours = 0;
        bool sleepRespawnAllowed() { return settings.respawn == "sleep" || settings.respawn == "both"; }
        void sleepNote(string text)
        {
            var note = Q("#sleep-note");
            if (note == null) return;
            note.SetHidden(string.IsNullOrEmpty(text));
            note.SetText(text ?? "");
        }
        const int MS_PER_HOUR = 400; // an in-game hour of sleep takes this long on the clock
        void stopSleep(string message = null)
        {
            if (sleeping == null) return;
            timers.clearInterval(sleeping.timer);
            sleeping = null;
            var bar = Q("#sleep-bar");
            if (bar != null) bar.SetHidden(true);
            if (!string.IsNullOrEmpty(message)) sleepNote(message);
        }
        void startSleep(int hours)
        {
            if (engine == null || !playing || !engine.uiInCamp() || sleeping != null || teleporting != null) return;
            if (engine.uiInCombat()) { sleepNote("Not with something attacking the camp."); return; }
            sleeping = new SleepState { hours = hours, started = now(), timer = 0 };
            var bar = Q("#sleep-bar");
            var fill = Q("#sleep-fill");
            if (bar != null) bar.SetHidden(false);
            if (fill != null) fill.SetStyle("width", "0%");
            sleepNote($"Sleeping {hours} hour{(hours == 1 ? "" : "s")}…");
            sleeping.timer = timers.setInterval(() =>
            {
                if (sleeping == null) return;
                if (!playing || engine == null || !engine.uiInCamp()) { stopSleep("The camp is gone."); return; }
                if (engine.uiInCombat()) { finishSleep(true); return; }
                var spent = now() - sleeping.started;
                var total = hours * MS_PER_HOUR;
                var f = Q("#sleep-fill");
                if (f != null) f.SetStyle("width", $"{Js.Round(Math.Min(100, (100 * spent) / total))}%");
                if (spent >= total) finishSleep(false);
            }, 100);
        }
        void finishSleep(bool broken)
        {
            if (sleeping == null || engine == null) return;
            var asked = sleeping.hours;
            var spent = now() - sleeping.started;
            var hours = broken ? Math.Floor(spent / MS_PER_HOUR) : asked;
            stopSleep();
            var slept = engine.uiCampSleep(hours);
            sleptHours += slept;
            if (broken)
            {
                gameUi.message($"Something breaks the camp after {slept} hour{(slept == 1 ? "" : "s")}.", "system");
                sleepNote($"The camp is broken after {slept} hour{(slept == 1 ? "" : "s")}.");
                return;
            }
            gameUi.message($"The party sleeps {slept} hour{(slept == 1 ? "" : "s")} and wakes rested.", "system");
            sleepNote(sleptHours >= 8 && sleepRespawnAllowed()
                ? $"Slept {sleptHours} hours. The lands have stirred: things have moved back in."
                : $"Slept {sleptHours} hour{(sleptHours == 1 ? "" : "s")}.");
        }
        void init_D04()
        {
            foreach (var button in QAll(".sleep-do"))
            {
                button.On("click", () => startSleep(int.TryParse(button.GetAttr("data-hours"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h != 0 ? h : 1));
            }
        }

        // Leaving the camp is the only safe moment to stir the level: uiRespawnMonsters snapshots the level
        // (generateTempData), and the camp's borrowed walls must be back in place before it does.
        void leaveCampAndMaybeRespawn(bool allowStir = true)
        {
            var stir = allowStir && sleptHours >= 8 && sleepRespawnAllowed() && !engine.uiInDungeon();
            sleptHours = 0;
            stopSleep();
            sleepNote("");
            engine.uiLeaveCamp();
            if (stir) engine.queueAsync(() => engine.uiRespawnMonsters());
        }

        void init_D05()
        {
            on("#rest-wake", () =>
            {
                if (engine == null) return;
                cancelTeleport();
                closeCampSheet();
                leaveCampAndMaybeRespawn();
            });
            on("#rest", async () =>
            {
                if (!playing || engine == null || !engine.partyAwake || engine.uiInCamp()) return;
                // Nothing within three blocks: a camp is not made with monsters in sight.
                var near = engine.uiCampThreats();
                // In the pit that would leave the party with no way out, so the Camp button offers the rope.
                if (near.Count > 0 && engine.uiInDungeon())
                {
                    var @out = await askConfirm("There is no making camp with something this close. Climb back out of the pit? The floor stays unbeaten.", "Climb out?", "Climb out", "Stay");
                    if (@out) abandonDungeon();
                    return;
                }
                if (near.Count > 0)
                {
                    var what = near.Count == 1 ? $"A {engine.monsterName(near[0]).ToLowerInvariant()} is too close" : $"{near.Count} monsters are too close";
                    gameUi.message($"{what} to make camp. Kill them or put some distance between you.", "system");
                    toast("Too close to make camp", "fx-toast danger");
                    return;
                }
                bump("rests");
                sleptHours = 0;
                sleepNote("");
                if (!engine.uiEnterCamp()) { gameUi.message("There is nowhere to pitch a camp here.", "system"); return; }
                engine.uiCampStartHealing();
                gameUi.message("You make camp. Turn around and click the tent, the cauldron, the chest or the imp.", "system");
            });
        }

        // --- map notes and auto markers ---
        const string NOTES_KEY = "lol.notes";
        Dictionary<int, List<MapNote>> notes = new Dictionary<int, List<MapNote>>();
        VisualElement noteList;
        VisualElement mapTools;
        string noteKey = "";
        void init_D06()
        {
            try { notes = JsonSerializer.Deserialize<Dictionary<int, List<MapNote>>>(localStorage.getItem(NOTES_KEY) ?? "null", JsonFieldsD) ?? new Dictionary<int, List<MapNote>>(); } catch (Exception) { /* ignore */ }
            noteList = Q("#note-list");
            mapTools = Q("#map-tools");
        }
        void saveNotes() { try { localStorage.setItem(NOTES_KEY, JsonSerializer.Serialize(notes, JsonFieldsD)); } catch (Exception) { /* ignore */ } minimap.lastKey = ""; noteKey = ""; }
        void renderNotes()
        {
            if (noteList == null || engine == null) return;
            var list = notes.TryGetValue(engine.currentLevel, out var l) && l != null ? l : new List<MapNote>();
            var key = $"{engine.currentLevel}|{string.Join("|", list.Select((n) => n.block + n.text))}";
            if (key == noteKey) return;
            noteKey = key;
            noteList.ReplaceChildren();
            for (int i0 = 0; i0 < list.Count; i0++)
            {
                var note = list[i0]; var i = i0;
                var row = Dom.El("div");
                var text = Dom.El("span");
                text.SetClassName("note-text");
                text.SetText($"{note.block}: {note.text}");
                text.SetTitle("Click to walk there");
                text.On("click", () => { if (playing && !engine.autoWalkTo(note.block)) gameUi.message("No known way there.", "system"); });
                var del = Dom.El("button");
                del.SetAttr("type", "button");
                del.SetText("×");
                del.SetTitle("Remove note");
                del.On("click", () => { list.RemoveAt(i); if (list.Count == 0) notes.Remove(engine.currentLevel); saveNotes(); renderNotes(); });
                row.Append(text, del);
                noteList.Append(row);
            }
        }
        void init_D07()
        {
            on("#note-add", async () =>
            {
                if (!playing || engine == null) return;
                var block = engine.currentBlock;
                var level = engine.currentLevel;
                var text = await askText($"Note for block {block}:", "");
                if (text == null || text.Trim().Length == 0) return;
                if (!notes.TryGetValue(level, out var at) || at == null) notes[level] = at = new List<MapNote>();
                var trimmed = text.Trim();
                at.Add(new MapNote { block = block, text = trimmed.Length > 60 ? trimmed.Substring(0, 60) : trimmed });
                saveNotes();
                renderNotes();
            });
            on("#hint", () =>
            {
                if (!playing || engine == null || !settings.hints) return;
                var hint = engine.uiPuzzleHint();
                if (hint == null) { gameUi.message("No hint here: explore more first.", "system"); return; }
                gameUi.message($"Hint: try the {hint.name.ToLowerInvariant()} at block {hint.block} ({hint.dist} steps away){(hint.door >= 0 ? $" for the closed door at block {hint.door}" : "")}.", "note");
                hintMark = new HintMark { block = hint.block, until = now() + 30000 };
                minimap.lastKey = "";
                if (engine.autoWalkTo(hint.block)) toast("Walking to the hint", "");
            });
            on("#explore-next", () =>
            {
                if (!playing || engine == null) return;
                var target = engine.nearestUnexplored();
                if (target < 0 || !engine.autoWalkTo(target)) gameUi.message("Nothing left to explore from here.", "system");
            });
        }
        // Markers on the minimap: notes (yellow), unexplored passages (cyan), the last visited level exit is the engine door glyph.
        HintMark hintMark = null;
        void init_D08()
        {
            minimap.afterDraw = (map, game) =>
            {
                var ctx = map.ctx;
                if (hintMark != null && now() < hintMark.until) { var at = map.cellCenter(game, hintMark.block); if (at != null) { ctx.strokeStyle = "#ff9f43"; ctx.lineWidth = 2; ctx.beginPath(); ctx.arc((float)at[0], (float)at[1], 5, 0, (float)(Math.PI * 2)); ctx.stroke(); ctx.lineWidth = 1; } }
                foreach (var block in game.uiFrontier())
                {
                    var at = map.cellCenter(game, block);
                    if (at == null) continue;
                    ctx.fillStyle = "#5fe0ff";
                    ctx.fillRect((float)at[0] - 1, (float)at[1] - 1, 2, 2);
                }
                foreach (var note in (notes.TryGetValue(game.currentLevel, out var l) && l != null ? l : new List<MapNote>()))
                {
                    var at = map.cellCenter(game, note.block);
                    if (at == null) continue;
                    ctx.fillStyle = "#ffd400";
                    ctx.beginPath();
                    ctx.arc((float)at[0], (float)at[1], 1.8f, 0, (float)(Math.PI * 2));
                    ctx.fill();
                }
            };
        }

        // Floor items on the current block and the one ahead; click to take one, "Take all" for everything.
        VisualElement lootBox;
        VisualElement lootList;
        string lootKey = "";
        // Shop panel: shown when the party faces a merchant. Counter items can be bought from the list;
        // "Sell…" opens the inventory in sell mode.
        VisualElement shopBox;
        VisualElement shopList;
        string shopKey = "";
        string inventoryMode = "";
        void init_D09()
        {
            lootBox = Q("#loot");
            lootList = Q("#loot-list");
            shopBox = Q("#shop");
            shopList = Q("#shop-list");
        }
        void updateShop()
        {
            var btn = Q("#shop-open");
            if (btn == null || engine == null) return;
            var merchant = compactActive && engine.partyAwake ? engine.uiMerchantAhead() : -1;
            btn.SetHidden(merchant < 0);
        }
        // Trade window: wares (counter items and scripted offers) on the left, the inventory on the
        // right (equipped items are not offered). Prices come from the merchant's script when it names
        // them; the choice buttons and the item card are mirrored here while it is open.
        VisualElement tradeOverlay;
        int tradeMerchant = -1;
        string tradeKey = "";
        int tradeSayCount = -1;
        TradeSelection tradeSelected = null; // { item } (an item instance) or { type } (a ware) shown in the details pane
        void init_D10()
        {
            tradeOverlay = Q("#trade-overlay");
        }
        void renderTradeDetails()
        {
            var box = Q("#trade-details");
            if (box == null || engine == null) return;
            box.ReplaceChildren();
            if (gameUi.choiceLabels != null && gameUi.choiceLabels.Any() && gameUi.trade != null) { gameUi.renderTradeCard(engine, box); if (box.childCount > 0) return; }
            var sel = tradeSelected;
            var info = sel != null ? (sel.item != 0 ? engine.itemInfo(sel.item) : engine.itemInfoForProperty(sel.type)) : null;
            if (info == null) { box.Append(mkEl("p", "panel-note", "Select an item to see its details.")); return; }
            var card = mkEl("div", "trade-card");
            var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon");
            drawIconInto(icon, sel.item != 0 ? engine.getItemIconShapePtr(sel.item) : engine.itemIconShapes[engine.itemProperties[sel.type].shpIndex], engine.uiPalette());
            var head = mkEl("div", "trade-head"); head.Append(icon, mkEl("b", "", info.name), mkEl("span", "trade-price", sel.price != 0 ? $"~{sel.price} cr" : ""));
            var rows = mkEl("div", "trade-rows");
            void row(string k, string v) { if (!string.IsNullOrEmpty(v)) rows.Append(mkEl("span", "k", k), mkEl("span", "v", v)); }
            if (!info.usable) { row("Might", info.might != 0 ? info.might.ToString(CultureInfo.InvariantCulture) : ""); row("Protection", info.protection != 0 ? info.protection.ToString(CultureInfo.InvariantCulture) : ""); }
            row("Fits", string.Join(", ", info.slots));
            row("Use", engine.itemUse(info.name, info.usable, info.slots.Count > 0));
            var prop = info.prop;
            var wearers = string.Join(", ", engine.characters.Where((ch, i) => (ch.flags & 1) != 0 && engine.uiSlotsForProperty(prop, i).Count > 0).Select((ch) => ch.name));
            if (info.slots.Count > 0) row("Who can wear it", Or(wearers, "nobody in the party"));
            var c = engine.selectedCharacter; var slots = engine.uiSlotsForProperty(prop, c);
            if (slots.Count > 0 && !info.usable)
            {
                int cur = engine.characters[c].items[slots[0]]; var ci = cur != 0 ? engine.itemInfo(cur) : null;
                string d(int a, int b) { var n = a - b; return n != 0 ? $" ({(n > 0 ? "+" : "")}{n})" : ""; }
                row($"For {engine.characters[c].name}", ci != null ? $"vs {ci.name}: might {info.might}{d(info.might, ci.might)}, prot {info.protection}{d(info.protection, ci.protection)}" : "slot is empty");
            }
            card.Append(head, rows); box.Append(card);
        }
        void openTrade()
        {
            if (!playing || engine == null) return;
            var m = engine.uiMerchantAhead();
            if (m < 0) { gameUi.message("No merchant here.", "system"); return; }
            tradeMerchant = m; tradeKey = "";
            gameUi.mirror = Q("#trade-prompt");
            gameUi.onPrompt = () => { renderTradeSay(); renderTradeDetails(); };
            tradeSelected = null;
            toggleModal("#trade-overlay", true);
            renderTrade();
            gameUi.renderPrompt();
        }
        void closeTrade() { gameUi.mirror = null; gameUi.onPrompt = null; gameUi.autoAnswer = null; tradeMerchant = -1; basket.Clear(); toggleModal("#trade-overlay", false); }
        void renderTradeSay()
        {
            var say = Q("#trade-say");
            if (say == null) return;
            var lines = gameUi.log.Where((e) => Regex.IsMatch(e.cls ?? "", "ui-say|combat|system")).ToList();
            say.ReplaceChildren(lines.Skip(Math.Max(0, lines.Count - 3)).Select((e) => mkEl("div", e.cls, e.text)).ToList());
        }
        // Sell basket: offers come from the merchant's transcribed rules (src/game/shops.mjs), so they
        // show instantly; "Sell marked" performs the sales directly.
        readonly JsSet<int> basket = new JsSet<int>(); // inventory slots, in the order they were marked (a JS Set)
        bool tradeBusy = false;
        async Task acceptOffers()
        {
            if (tradeBusy || engine == null || tradeMerchant < 0) return;
            tradeBusy = true; tradeKey = "";
            try
            {
                int total = 0;
                foreach (var slot in basket.ToList())
                {
                    var item = engine.inventory[slot]; if (item == 0) { basket.Remove(slot); continue; }
                    var r = await LandsOfLore.sellItem(engine, tradeMerchant, slot);
                    if (r != null && r.price != 0) { total += r.price; basket.Remove(slot); }
                }
                if (total != 0) toast($"Sold for {total} crowns", "fx-toast-ach");
            }
            finally { tradeBusy = false; tradeKey = ""; renderTrade(); }
        }
        void renderTrade()
        {
            if (tradeOverlay == null || tradeOverlay.IsHidden() || engine == null || tradeMerchant < 0) return;
            var inv = engine.inventory.Select((it, slot) => (it: (int)it, slot)).Where((e) => e.it != 0).ToList();
            // the credits count a few a frame after a deal: they are written out, not a reason to rebuild the lists
            Q("#trade-credits").SetText($"{engine.credits} crowns");
            var key = $"{string.Join(",", inv.Select((e) => e.it))}|{engine.itemInHand}|{string.Join(",", engine.uiShopItems(tradeMerchant).Select((i) => i.item))}|{string.Join(",", basket)}|{(tradeBusy ? "true" : "false")}";
            if (key == tradeKey) return;
            tradeKey = key;
            Q("#trade-title").SetText($"Trade · {engine.levelName()}");
            var palette = engine.uiPalette();
            string statLine(ItemInfo info) => string.Join(" · ", new[] { info.might != 0 ? $"might {info.might}" : "", info.protection != 0 ? $"prot {info.protection}" : "", info.slots.Count > 0 ? string.Join("/", info.slots) : (info.usable ? "usable" : "") }.Where((s) => !string.IsNullOrEmpty(s)));
            VisualElement row(VisualElement icon, string name, string stats, string price, Action onClick, string title, TradeSelection sel)
            {
                var b = mkEl("button", "trade-row"); b.SetAttr("type", "button");
                b.Append(icon, mkEl("span", "trade-name", name), mkEl("span", "trade-stats", stats), mkEl("span", "trade-cost", price));
                b.SetTitle(title ?? "");
                b.On("mouseenter", () => { if (!(gameUi.choiceLabels != null && gameUi.choiceLabels.Any())) { tradeSelected = sel; renderTradeDetails(); } });
                b.On("click", () => { tradeSelected = sel; renderTradeDetails(); onClick(); b.BlurEl(); });
                return b;
            }
            tradeOverlay.ClassToggle("trade-busy", tradeBusy);
            var buy = Q("#trade-buy"); buy.ReplaceChildren();
            foreach (var it in engine.uiShopItems(tradeMerchant))
            {
                var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon"); drawIconInto(icon, engine.getItemIconShapePtr(it.item), palette);
                buy.Append(row(icon, it.name, statLine(it.info), $"{it.price} cr", () => engine.queueAsync(() => engine.uiBuy(it.item, it.block)), "Buy: the merchant names the price", new TradeSelection { item = it.item, price = it.price }));
            }
            foreach (var w in engine.uiWares(tradeMerchant))
            {
                var info = engine.itemInfoForProperty(w.type);
                var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon"); drawIconInto(icon, engine.itemIconShapes[engine.itemProperties[w.type].shpIndex], palette);
                buy.Append(row(icon, w.name, statLine(info), w.price != 0 ? $"~{w.price} cr" : "ask", () => engine.queueAsync(() => engine.uiAsk(tradeMerchant, w)), "Buy: the merchant names the price", new TradeSelection { type = w.type, price = w.price }));
            }
            if (buy.childCount == 0) buy.Append(mkEl("p", "panel-note", "Nothing on offer here; some merchants only buy, or sell in conversation."));
            var sell = Q("#trade-sell"); sell.ReplaceChildren();
            foreach (var (it, slot) in inv)
            {
                var info = engine.itemInfo(it);
                var icon = (CanvasEl)Dom.El("canvas"); icon.width = icon.height = 24; icon.SetClassName("slot-icon"); drawIconInto(icon, engine.getItemIconShapePtr(it), palette);
                var o = LandsOfLore.sellOffer(engine, tradeMerchant, it);
                // JS: !o.price && (o.ask || o.special); sellOffer never returns `ask`, so SellOffer has no such field.
                var byHand = o.price == 0 && o.special;
                var cost = o.price != 0 ? $"{o.price} cr" : byHand ? "ask" : "not wanted";
                void click()
                {
                    if (tradeBusy) return;
                    if (byHand)
                    {
                        tradeBusy = true;
                        // Offering by hand is the only way to find out what an unlisted shopkeeper will pay, and
                        // most of them pay nothing - they just talk. Say which happened: the item quietly returning
                        // to the hand with no message is what makes a shop look broken.
                        var purse = engine.credits;
                        engine.queueAsync(async () =>
                        {
                            var res = await engine.uiSell(slot, tradeMerchant);
                            tradeBusy = false; tradeKey = "";
                            if (res == 2)
                            {
                                // He could not be located in his script: the player has to show him the item themselves.
                                gameUi.message($"{engine.itemName(engine.itemInHand)} is in your hand. Close this and click the shopkeeper to offer it.", "system");
                            }
                            else if (engine.credits == purse && engine.itemInHand != 0)
                            {
                                gameUi.message($"{engine.itemName(engine.itemInHand)}: he did not take it. Press B to put it back.", "system");
                            }
                        });
                        return;
                    }
                    if (o.price == 0) return;
                    if (basket.Contains(slot)) basket.Remove(slot); else basket.Add(slot);
                    tradeKey = ""; renderTrade();
                }
                var r = row(icon, info.name, statLine(info), cost, click, o.price != 0 ? $"{(o.full ? "Full price" : "Half price")}: click to mark for sale" : o.reason, new TradeSelection { item = it, price = o.price });
                r.ClassToggle("marked", basket.Contains(slot)); if (o.price == 0 && !byHand) r.AddToClassList("refused");
                sell.Append(r);
            }
            var bar = Q("#trade-basket"); bar.ReplaceChildren();
            var total = basket.Aggregate(0, (n, sl) => n + (engine.inventory[sl] != 0 ? LandsOfLore.sellOffer(engine, tradeMerchant, engine.inventory[sl]).price : 0));
            var accept = mkEl("button", "accept", $"Sell marked{(basket.Count > 0 ? $" ({basket.Count}) +{total} cr" : "")}"); accept.SetAttr("type", "button"); accept.SetDisabled(tradeBusy || total == 0); accept.On("click", () => { _ = acceptOffers(); });
            var all = mkEl("button", "", "Mark all"); all.SetAttr("type", "button"); all.SetDisabled(tradeBusy); all.On("click", () => { foreach (var (it, sl) in inv) if (LandsOfLore.sellOffer(engine, tradeMerchant, it).price != 0) basket.Add(sl); tradeKey = ""; renderTrade(); });
            var clear = mkEl("button", "", "Clear"); clear.SetAttr("type", "button"); clear.SetDisabled(tradeBusy || basket.Count == 0); clear.On("click", () => { basket.Clear(); tradeKey = ""; renderTrade(); });
            bar.Append(accept, all, clear);
            renderTradeDetails();
            if (engine.itemInHand != 0) { var p = mkEl("p", "panel-note", $"In hand: {engine.itemName(engine.itemInHand)}. Stash it (B) to put it back."); sell.Prepend(p); }
            if (inv.Count == 0) sell.Append(mkEl("p", "panel-note", "Your inventory is empty."));
        }
    }
}
