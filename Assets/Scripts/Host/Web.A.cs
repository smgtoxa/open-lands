// src/main.mjs, section A (lines 1-659): imports, constants, the display filter, the saves panel and
// its prompts, log/status/archives, the title screen, character selection, the docks, the host pause
// and presentEngine (the per-present hook). Ported 1:1, see docs/port/HOST.md. C# 9.
//
// The JS imports, and where each name lives in the port:
//   PakArchive, decodeCps (Cps.decodeCps), WsaMovie                     -> Lol (Engine/Formats)
//   IndexedScreen, Minimap, CharScreen, FullMap, NpcMemory, GameUi      -> LolHost classes of the same name
//   RemotePak                                                           -> not needed (files are read from the data folder)
//   FilterScreen                                                        -> FilterScreen (Unity shader passes)
//   HdPack, HdScene                                                     -> not ported yet (hdScene stays null)
//   QUESTS, questStates, sellOffer, sellItem, EXTRA_SPELLS, OPCODE_NAMES, CAMP_PIECES
//                                                                       -> Lol.LandsOfLore statics
//   createSpellSlider, spellIcon                                        -> SpellWidget (static)
//   REAGENTS, RECIPES, RECIPE_UNLOCK, readPouch, writePouch, pouchTotal, rollDrop, canCraft, craft, registerPotions,
//   fillPouch, FAMILIES, familyOf, dropsFor, recipeKnown, recipeHint   -> CraftingUi (static)
//   readRun, writeRun, loadRun, floorPlan, floorSizeName, monsterCount, OBJECTIVES, grantRewards -> DungeonRun (static)
//   loadStore, REAGENT_PRICE, readStash, stashItem, takeFromStash, stashCount, stashSlots, buyReagent, sellReagent,
//   impOffer, sellToImp, buySpell, readJob, acceptJob, claimJob, jobText, jobProgress, jobPay, rollErrandDrop,
//   abandonJob, UPGRADES, readUpgrades, hasUpgrade, buyUpgrade, readLoadout, saveLoadout, wearLoadout -> CampStoreUi (static)
//   CHECKPOINTS, MANUAL_SLOTS, SAVE_VERSION, clearSide, deleteSave, exportSaves, importSaves, latestSave, listSaves,
//   readSave, readSide, renameSave, restoreSide, savesUnreadable, storageError, writeCheckpoint, writeSave -> Saves (static)
//   xbrUpscale4, XBR                                                    -> Xbr (static)
//   LandsOfLore, Resources                                              -> Lol
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>{ name, x, stats } of the character selection.</summary>
    public sealed class CharacterChoice
    {
        public string name;
        public int x;
        public int[] stats;
    }

    /// <summary>The title/character-selection pictures: { title, characterScreen, portraitAtlas, king }.</summary>
    public sealed class PageAssets
    {
        public CpsImage title, characterScreen, portraitAtlas;
        public WsaMovie king;
    }

    /// <summary>{ label, until }</summary>
    public sealed class PendingCheckpoint
    {
        public string label;
        public double until;
    }

    /// <summary>A dock: { id, rect } plus canvas, ctx, image.</summary>
    public sealed class Dock
    {
        public string id;
        public int[] rect;
        public CanvasEl canvas;
        public Ctx2D ctx;
        public ImageData image;
    }

    public sealed partial class Web
    {
        const int GREEN_SKULL = 266; // item property: a thrown poison bomb, offered as a spell in the bar
        VisualElement status;
        VisualElement logElement;
        VisualElement controls;
        VisualElement reload;
        VisualElement teleportForm;
        int lastAutosaveLevel = -1; // level of the last "changing level" autosave check
        IndexedScreen screen;
        Minimap minimap;

        // Display filter: the 2D canvas keeps composing the frame; when a filter is on, the frame is
        // presented through the WebGL overlay canvas instead.
        const string FILTER_KEY = "lol.filter";
        DomSelect filterSelect;
        CanvasEl fxCanvas;
        FilterScreen filterScreen = null;
        string filterMode = "off";
        Action baseRender;

        void init_A01()
        {
            status = Q("#status");
            logElement = Q("#log");
            controls = Q("#game-controls");
            reload = Q("#reload");
            teleportForm = Q("#teleport");
            screen = new IndexedScreen((CanvasEl)Q("#screen"));
            minimap = new Minimap((CanvasEl)Q("#minimap"));
            filterSelect = Q("#filter") as DomSelect;
            fxCanvas = Q("#screen-fx") as CanvasEl;
            baseRender = screen.render;
            screen.render = screenRender;
            if (filterSelect != null) filterSelect.On("change", () => setFilter(filterSelect.value));
            try { setFilter(localStorage.getItem(FILTER_KEY) ?? "off", false); } catch (Exception) { /* no storage */ }
            // ?nominimap (URL query parameter): skipped in the port.
        }

        void screenRender()
        {
            if (filterMode != "off" && filterScreen != null)
            {
                try
                {
                    filterScreen.resize(screen.canvas.width, screen.canvas.height); // the view switches size
                    filterScreen.draw(screen.pixels, screen.palette);
                    return;
                }
                catch (Exception error)
                {
                    // Stop drawing through the filter, but never write the fallback to the setting: one bad
                    // frame used to lose the player's choice for good.
                    log($"Display filter failed: {error.Message}");
                    setFilter("off", false);
                }
            }
            baseRender();
        }

        void setFilter(string mode, bool remember = true)
        {
            string failed = "";
            if (mode != "off" && filterScreen == null && fxCanvas != null)
            {
                try
                {
                    filterScreen = new FilterScreen(fxCanvas, screen.canvas.width, screen.canvas.height);
                }
                catch (Exception error)
                {
                    failed = error.Message;
                }
            }
            filterMode = filterScreen != null ? mode : "off";
            if (filterScreen != null) filterScreen.setMode(filterMode);
            if (fxCanvas != null) fxCanvas.SetHidden(filterMode == "off");
            // The choice is kept even when WebGL is unavailable right now: overwriting it with "off" would
            // silently throw the setting away for good. Only the running mode falls back.
            if (filterSelect != null) filterSelect.value = failed != "" ? mode : filterMode;
            if (remember) try { localStorage.setItem(FILTER_KEY, mode); } catch (Exception) { /* private mode */ }
            if (failed != "")
            {
                log($"Display filter unavailable: {failed}");
                toast($"Display filter needs WebGL, which this browser refused ({failed}).", "fx-toast-ach");
            }
            else if (mode != "off" && settings != null && (settings.hd || settings.textured))
            {
                toast("HD assets draw the scene themselves, so the filter only shows outside the 3D view. Turn HD assets off to see it.", "fx-toast-ach");
            }
            screen.render();
        }

        readonly CharacterChoice[] characters =
        {
            new CharacterChoice { name = "Ak'shel", x = 96, stats = new[] { 15, 8, 5 } },
            new CharacterChoice { name = "Michael", x = 154, stats = new[] { 6, 10, 15 } },
            new CharacterChoice { name = "Kieran", x = 212, stats = new[] { 8, 6, 8 } },
            new CharacterChoice { name = "Conrad", x = 271, stats = new[] { 10, 12, 10 } },
        };
        readonly int[][] atlasPositions =
        {
            new[] { 111, 0 }, new[] { 143, 0 }, new[] { 175, 0 }, new[] { 207, 0 }, new[] { 239, 0 },
            new[] { 111, 32 }, new[] { 143, 32 }, new[] { 175, 32 }, new[] { 207, 32 },
        };

        PageAssets assets;
        int animationTimer; // 0: undefined
        int selectedCharacter = -1;
        int animationStep = 0;
        LandsOfLore engine;
        bool playing = false;
        string dataRoot = "";

        // Saving is only allowed in normal play (not inside a dialogue, menu, map or inventory screen).
        bool canSaveNow()
        {
            // Never while the camp is cut into the level: its opened walls would be written into the save.
            // Nor while the engine still owes itself deferred work: a member who has just joined gets their
            // equipment bonuses from queued item scripts, and a save written first keeps them at zero for good.
            return engine != null && playing && !engine.quit && engine.updateFlags == 0 && engine.asyncQueue.Count == 0 && !engine.weaponsDisabled && engine.needSceneRestore == 0 && !engine.sysTimerPaused && engine.currentBlock != 0
                && !engine.uiInCamp()
                && engine.characters.Any((c) => (c.flags & 1) != 0 && c.hitPointsCur > 0) && Q("#death-overlay").IsHidden();
        }

        // Small screenshot of the scene for the save list.
        string saveThumbnail()
        {
            try
            {
                var src = (CanvasEl)Q("#screen");
                var c = (CanvasEl)Dom.El("canvas");
                c.width = 88;
                c.height = 60;
                c.getContext("2d").drawImage(src, 0, 0, c.width, c.height);
                return c.toDataURL("image/jpeg", 0.6f);
            }
            catch (Exception)
            {
                return "";
            }
        }

        // In-page replacement for window.prompt (Electron does not implement prompt()). Resolves null on cancel.
        Task<string> askText(string label, string initial = "")
        {
            var overlay = Q("#prompt-overlay");
            var form = Q("#prompt-form");
            var input = Q("#prompt-input") as DomInput;
            if (overlay == null || form == null || input == null) return Task.FromResult<string>(null); // window.prompt: none in Unity, so a cancel
            var promise = new TaskCompletionSource<string>();
            Q("#prompt-label").SetText(label);
            input.value = initial;
            overlay.SetHidden(false);
            input.FocusInput(); // input.focus(); input.select(): the text field selects all on focus
            var cancel = Q("#prompt-cancel");
            void done(string value) { overlay.SetHidden(true); A_setOn(form, "submit", null); A_setOn(cancel, "click", null); A_setOn(input, "keydown", null); promise.TrySetResult(value); }
            A_setOn(form, "submit", (evt) => { evt.preventDefault(); done(input.value); });
            A_setOn(cancel, "click", (evt) => done(null));
            A_setOn(input, "keydown", (evt) => { evt.stopPropagation(); if (evt.key == "Escape") { evt.preventDefault(); done(null); } });
            return promise.Task;
        }

        // In-page replacement for window.confirm, in the game's own frame. Enter answers yes, Esc no.
        Task<bool> askConfirm(string text, string title = "Are you sure?", string ok = "Yes", string cancel = "No")
        {
            var overlay = Q("#confirm-overlay");
            var form = Q("#confirm-form");
            if (overlay == null || form == null) return Task.FromResult(false); // window.confirm: none in Unity, so a no
            var promise = new TaskCompletionSource<bool>();
            Q("#confirm-title").SetText(title);
            Q("#confirm-text").SetText(text);
            var okButton = Q("#confirm-ok");
            var cancelButton = Q("#confirm-cancel");
            okButton[0].SetText($"{ok} ");
            cancelButton[0].SetText($"{cancel} ");
            overlay.SetHidden(false);
            okButton.FocusEl();
            void done(bool value)
            {
                overlay.SetHidden(true);
                A_setOn(form, "submit", null); A_setOn(cancelButton, "click", null); A_setOn(form, "keydown", null);
                promise.TrySetResult(value);
            }
            A_setOn(form, "submit", (evt) => { evt.preventDefault(); done(true); });
            A_setOn(cancelButton, "click", (evt) => done(false));
            A_setOn(form, "keydown", (evt) => { evt.stopPropagation(); if (evt.key == "Escape") { evt.preventDefault(); done(false); } });
            return promise.Task;
        }

        // Checkpoint: silent rotating save with a label (never while a dialogue or fight blocks saving).
        PendingCheckpoint pendingCheckpoint = null; // { label, until } retried until the game allows saving
        bool checkpoint(string label)
        {
            if (!canSaveNow()) { pendingCheckpoint = new PendingCheckpoint { label = label, until = now() + 30000 }; return false; }
            pendingCheckpoint = null;
            try
            {
                if (Saves.writeCheckpoint(engine.saveState(), label) == null) return false;
                renderSaveSlots();
                return true;
            }
            catch (Exception) { return false; }
        }

        void init_A02()
        {
            timers.setInterval(() => { if (pendingCheckpoint != null && (now() > pendingCheckpoint.until ? (pendingCheckpoint = null) != null : canSaveNow())) checkpoint(pendingCheckpoint.label); }, 1000);
        }

        bool saveToSlot(string slot, bool quiet = false, JsonObject extra = null)
        {
            if (!quiet && stats != null) bump("saves");
            if (!canSaveNow())
            {
                if (!quiet && engine != null) engine.txt.printMessage(2, "Cannot save right now.");
                return false;
            }
            try
            {
                // { thumb: saveThumbnail(), ...extra }
                var meta = new SaveExtra { thumb = saveThumbnail() };
                if (extra != null && extra.ContainsKey("thumb")) meta.thumb = (string)extra["thumb"];
                if (extra != null && extra.ContainsKey("name")) meta.name = (string)extra["name"];
                if (!Saves.writeSave(slot, engine.saveState(), meta))
                {
                    saveFailed(Saves.savesUnreadable()
                        ? "The saves in this browser cannot be read, so nothing was written over them. Export what you can and clear the site data."
                        : $"The game could not be saved: {(string.IsNullOrEmpty(Saves.storageError()) ? "this browser refused to store it" : Saves.storageError())}.");
                    return false;
                }
                if (!quiet) engine.txt.printMessage(0, slot == "quick" ? "Quick save." : $"Saved to slot {slot}.");
                renderSaveSlots();
                return true;
            }
            catch (Exception error)
            {
                log($"Save failed: {error.Message}");
                saveFailed($"The game could not be saved: {error.Message}");
                return false;
            }
        }

        static readonly JsonSerializerOptions A_json = new JsonSerializerOptions { IncludeFields = true };

        // A save carries the campaign's own state (chest, pouch, journal, map notes, the pit...). After it
        // has been written back into the browser keys, every copy this module is holding has to be read
        // again, or the game would show the previous campaign's things.
        void reloadBrowserState()
        {
            // const read = (key, fallback) => ...: the fallback is given as JSON, read into the variable's own type.
            void read<T>(ref T target, string key, string fallback)
            {
                try { var v = JsonSerializer.Deserialize<T>(localStorage.getItem(key) ?? "null", A_json); target = v == null ? JsonSerializer.Deserialize<T>(fallback, A_json) : v; }
                catch (Exception) { target = JsonSerializer.Deserialize<T>(fallback, A_json); }
            }
            read(ref journal, JOURNAL_KEY, "[]");
            read(ref itemDb, ITEMS_KEY, "{}");
            read(ref visited, VISITED_KEY, "{}");
            read(ref notes, NOTES_KEY, "{}");
            read(ref quickSpell, QUICK_SPELL_KEY, "{\"slot\":-1,\"level\":0}");
            read(ref quickSpells, QUICK_SPELLS_KEY, "{}");
            read(ref hiddenSpells, HIDDEN_SPELLS_KEY, "{}");
            read(ref spellPower, SPELL_POWER_KEY, "{}");
            JsonObject storedStats = null;
            read(ref storedStats, STATS_KEY, "{}");
            read(ref achUnlocked, ACH_KEY, "[]");
            if (engine != null && engine.meta != null)
            {
                engine.initMeta();
                engine.uiCraftLoad((JsonObject)JsonSerializer.SerializeToNode(CraftingUi.readPouch()));
                CampStoreUi.loadStore(engine);
                DungeonRun.loadRun(engine);
                engine.metaLoad(new JsonObject
                {
                    ["stats"] = storedStats.DeepClone(),
                    ["bestiary"] = storedStats["bestiary"]?.DeepClone(),
                    ["visited"] = JsonSerializer.SerializeToNode(visited, A_json),
                    ["itemDb"] = JsonSerializer.SerializeToNode(itemDb, A_json),
                    ["notes"] = JsonSerializer.SerializeToNode(notes, A_json),
                    ["unlocked"] = JsonSerializer.SerializeToNode(achUnlocked, A_json),
                });
                stats = WebStats.OfEngine(engine, stats);
                visited = engine.meta.visited;
                itemDb = engine.meta.itemDb;
                notes = engine.meta.notes;
            }
            else
            {
                // { kills: 0, damageDealt: 0, damageTaken: 0, seconds: 0, bestiary: {}, ...storedStats }
                stats = WebStats.Read(storedStats.ToJsonString());
            }
            npcs.reload();
            gameUi.reloadHotbar();
            dungeonRun = null; // the run itself comes back with the engine snapshot
            // every cached render key, so nothing keeps drawing the old campaign
            inventoryKey = ""; impKey = ""; craftKey = ""; stashKey = ""; reagentKey = "";
            dungeonKey = ""; errandKey = ""; sidebarKey = ""; itemDbTick = 0; questStateCache = null;
            travelLocked = null;
            renderTravel();
            renderErrand();
            renderHunt();
            renderPinned();
            renderSaveSlots();
        }

        // A save that did not happen has to be said out loud, whatever was going on at the time.
        void saveFailed(string message)
        {
            log(message);
            if (engine != null && playing) gameUi.message(message, "system");
            toast("Save failed", "fx-toast danger");
        }

        bool loadFromSlot(string slot)
        {
            var state = Saves.readSave(slot);
            if (state == null) return false;
            if (state["version"] is JsonValue vv && vv.TryGetValue(out double version) && version != 0 && version > Saves.SAVE_VERSION)
            {
                saveFailed($"That save was written by a newer version of the game (format {version.ToString(CultureInfo.InvariantCulture)}).");
                return false;
            }
            // The campaign state that belongs to this slot goes back before the engine starts, so the chest,
            // the pouch and the journal match the moment the snapshot was taken. A slot from an older build
            // carries none, and then whatever is in the browser is left exactly as it is.
            var side = Saves.readSide(slot);
            if (side != null)
            {
                Saves.restoreSide(side);
                reloadBrowserState();
            }
            if (engine != null && playing)
            {
                engine.quit = true; // stop this engine; a fresh one resumes the save
                timers.setTimeout(() => { _ = runGame((game) => game.resumeGame(state)); }, 100);
            }
            else _ = runGame((game) => game.resumeGame(state));
            return true;
        }

        VisualElement saveList;
        void init_A03()
        {
            saveList = Q("#save-slots");
        }

        static DateTime A_localTime(double ms) => DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime;

        void renderSaveSlots() => renderSaveSlots(saveList);
        void renderSaveSlots(VisualElement target, bool loadOnly = false)
        {
            if (target == null) return;
            var saves = new Dictionary<string, SaveInfo>();
            foreach (var entry in Saves.listSaves()) saves[entry.slot] = entry;
            string describe(SaveInfo entry) => entry != null ? $"Level {entry.level}, {A_localTime(entry.time).ToString(CultureInfo.CurrentCulture)}" : "empty";
            target.ReplaceChildren();
            var slots = new List<string> { "quick", "auto" };
            slots.AddRange(Enumerable.Range(0, Saves.MANUAL_SLOTS).Select((i) => (i + 1).ToString(CultureInfo.InvariantCulture)));
            slots.AddRange(Enumerable.Range(0, Saves.CHECKPOINTS).Select((i) => $"cp{i + 1}"));
            foreach (var slot in slots)
            {
                saves.TryGetValue(slot, out var entry);
                if (loadOnly && entry == null) continue;
                var row = Dom.El("div");
                row.SetClassName(loadOnly ? "save-row load-only" : "save-row");
                var label = Dom.El("span");
                label.SetClassName("save-label");
                bool isCp = slot.StartsWith("cp");
                if (isCp && entry == null) continue; // checkpoints only show once written
                label.SetText(slot == "quick" ? "Quick" : slot == "auto" ? "Auto" : isCp ? $"CP{slot.Substring(2)}" : $"#{slot}");
                label.SetTitle($"{(slot == "quick" ? "Quick save (F5 / F9)" : slot == "auto" ? "Written when you leave the page" : isCp ? "Automatic checkpoint (before level changes and dangerous encounters)" : $"Manual slot {slot}")}: {describe(entry)}");
                // Always a grid cell (a hidden element would shift the row's columns).
                var thumb = Dom.El(entry != null && !string.IsNullOrEmpty(entry.thumb) ? "img" : "span");
                thumb.SetClassName("save-thumb");
                if (entry != null && !string.IsNullOrEmpty(entry.thumb)) { thumb.SetAttr("alt", ""); thumb.SetAttr("src", entry.thumb); }
                var info = Dom.El("span");
                info.SetClassName("save-info");
                // Seconds as well as minutes: two saves written a moment apart were otherwise the same line of
                // text, with nothing to tell the player which was which.
                info.SetText(entry != null ? $"{(!string.IsNullOrEmpty(entry.name) ? $"{entry.name} · " : "")}L{entry.level} {A_localTime(entry.time).ToString("T", CultureInfo.CurrentCulture)}" : "-");
                if (entry != null && slot != "quick" && slot != "auto")
                {
                    info.SetTitle("Click to name this save");
                    info.AddToClassList("save-nameable");   // the stylesheet underlines it, so it reads as clickable
                    info.SetStyle("cursor", "text");
                    info.On("click", async () =>
                    {
                        var name = await askText("Name for this save:", entry.name ?? "");
                        if (name != null) { string trimmed = name.Trim(); Saves.renameSave(slot, trimmed.Length > 40 ? trimmed.Substring(0, 40) : trimmed); renderSaveSlots(); }
                    });
                }
                row.Append(label, thumb, info);
                void fill() { var s = Dom.El("span"); row.Append(s); }
                if (slot == "auto" && !loadOnly) fill(); // keep the button columns aligned
                if (slot != "auto" && !isCp && !loadOnly)
                {
                    var save = Dom.El("button");
                    save.SetAttr("type", "button");
                    save.SetText("Save");
                    save.On("click", () => { saveToSlot(slot); save.BlurEl(); });
                    row.Append(save);
                }
                var load = Dom.El("button");
                load.SetAttr("type", "button");
                load.SetText("Load");
                load.SetDisabled(entry == null);
                load.On("click", () => loadFromSlot(slot));
                row.Append(load);
                if (slot != "quick" && slot != "auto" && !loadOnly)
                {
                    var del = Dom.El("button");
                    del.SetAttr("type", "button");
                    del.SetText("X");
                    del.SetTitle("Delete");
                    del.SetDisabled(entry == null);
                    del.On("click", () => { Saves.deleteSave(slot); renderSaveSlots(); });
                    row.Append(del);
                }
                else if (!loadOnly) fill();
                target.Append(row);
            }
            if (loadOnly && target.childCount == 0) target.SetText("No saved games.");
        }

        readonly HashSet<VisualElement> deathReleaseHooked = new HashSet<VisualElement>();
        readonly Dictionary<VisualElement, Action> deathRelease = new Dictionary<VisualElement, Action>();
        Action deathResolve;

        // Death screen: the engine calls deathHook instead of its own "You have been defeated" menu.
        Task showDeathScreen()
        {
            bump("deaths");
            var promise = new TaskCompletionSource<bool>();
            void resolve() => promise.TrySetResult(true);
            var overlay = Q("#death-overlay");
            foreach (var m in QAll(".modal")) m.SetHidden(true);
            charScreen.c = -1;
            var latest = Saves.latestSave();
            var loadLast = Q("#death-load-last");
            loadLast.SetDisabled(latest == null);
            var cps = Saves.listSaves().Where((s) => s.slot.StartsWith("cp")).OrderByDescending((s) => s.time).ToList();
            var cpBtn = Q("#death-checkpoint");
            cpBtn.SetDisabled(cps.Count == 0);
            cpBtn.SetText(cps.Count > 0 ? $"Load last checkpoint ({(string.IsNullOrEmpty(cps[0].name) ? "checkpoint" : cps[0].name)})" : "No checkpoint yet");
            A_setOn(cpBtn, "click", (evt) => { if (overlay.IsHidden()) return; overlay.SetHidden(true); loadFromSlot(cps[0].slot); resolve(); });
            loadLast.SetText(latest != null ? $"Load last save ({(!string.IsNullOrEmpty(latest.name) ? latest.name : (latest.slot == "quick" ? "quick" : latest.slot == "auto" ? "auto" : $"#{latest.slot}"))}, level {latest.level})" : "No saved game");
            var names = engine.characters.Where((c) => (c.flags & 1) != 0).Select((c) => c.name).ToList();
            Q("#death-text").SetText($"{string.Join(", ", names)} {(names.Count > 1 ? "have" : "has")} fallen on level {engine.currentLevel}.");
            var slots = Q("#death-slots");
            slots.SetHidden(true);
            Action<DomEvent> done(Action fn) => (evt) => { if (overlay.IsHidden()) return; overlay.SetHidden(true); fn(); resolve(); };
            A_setOn(loadLast, "click", done(() => loadFromSlot(latest.slot)));
            A_setOn(Q("#death-load"), "click", (evt) => { slots.SetHidden(!slots.IsHidden()); if (!slots.IsHidden()) { renderSaveSlots(slots, true); foreach (var b in slots.QAll("button")) b.On("click", () => { overlay.SetHidden(true); resolve(); }); } });
            A_setOn(Q("#death-exit"), "click", done(() => { }));
            // a press released over the button counts too: the screen comes up while the game is still dying and the
            // click (press and release on the same button) was sometimes not put together, leaving the screen stuck
            foreach (var (id, act) in new (string, Action)[] { ("#death-exit", () => { }), ("#death-load-last", () => { if (latest != null) loadFromSlot(latest.slot); }), ("#death-checkpoint", () => { if (cps.Count > 0) loadFromSlot(cps[0].slot); }) })
            {
                var b = Q(id);
                if (b == null || !deathReleaseHooked.Add(b)) { if (b != null) deathRelease[b] = act; continue; }
                deathRelease[b] = act;
                b.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0 || overlay.IsHidden() || b.enabledInHierarchy == false) return;
                    if (deathRelease.TryGetValue(b, out var f)) { overlay.SetHidden(true); f(); deathResolve?.Invoke(); }
                });
            }
            deathResolve = resolve;
            overlay.SetHidden(false);
            return promise.Task;
        }

        void log(string message)
        {
            logElement.SetText(logElement.GetText() + $"{message}\n");
            UnityEngine.Debug.Log("page log: " + message);   // the browser's console, for Player.log
        }

        void setStatus(string message, bool error = false)
        {
            status.SetText(message);
            status.ClassToggle("error", error);
        }

        // async function fetchBytes(path): Web.cs (the data folder instead of fetch).

        async Task<PakArchive> loadArchive(string path)
        {
            var bytes = await fetchBytes(path);
            var archive = new PakArchive(bytes);
            log($"{path.Split('/').Last()}: {archive.list().Count} entries");
            return archive;
        }

        void clearControls()
        {
            controls.ReplaceChildren();
        }

        void stopAnimation()
        {
            if (animationTimer != 0) timers.clearInterval(animationTimer);
            animationTimer = 0;
        }

        VisualElement createButton(string label, string className, Action action)
        {
            var button = Dom.El("button");
            button.SetAttr("type", "button");
            button.SetClassName($"game-button {className}");
            button.SetText(label);
            button.On("click", action);
            controls.Append(button);
            return button;
        }

        void showTitle()
        {
            stopAnimation();
            compactActive = false;
            setFrameSize(320, 200);
            playing = false;
            lastAutosaveLevel = -1;
            questStateCache = null;
            if (questPanel != null) questPanel.SetHidden(true);
            selectedCharacter = -1;
            clearControls();
            screen.draw(assets.title.pixels, assets.title.palette);
            var latest = Saves.latestSave();
            if (latest != null) createButton("Continue", "title-button continue-button", () => loadFromSlot(latest.slot));
            createButton("New game", "title-button newgame-button", showCharacterSelection);
            createButton("Settings", "title-button", () => toggleModal("#settings-overlay", true));   // Unity build: settings before a game
            createButton("Intro", "title-button intro-button", () => { _ = runGame(async (game) => { await game.preInit(); await game.showIntro(); return "restart"; }); });
            renderSaveSlots();
            setStatus(latest != null ? "Continue or start a new game" : "Choose New Game");
        }

        void renderCharacterSelection()
        {
            screen.draw(assets.characterScreen.pixels, assets.characterScreen.palette);
            int[] kingFrames = { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1 };
            int kingFrame = kingFrames[animationStep % kingFrames.Length];
            screen.blit(assets.king.frame(kingFrame), assets.king.width, assets.king.height, 113, 0);

            for (int index = 0; index < characters.Length; index += 1)
            {
                int atlasIndex = index + (animationStep % 12 == 0 ? 5 : 0);
                int sourceX = atlasPositions[atlasIndex][0], sourceY = atlasPositions[atlasIndex][1];
                screen.copyRegion(
                    assets.portraitAtlas.pixels,
                    assets.portraitAtlas.width,
                    sourceX,
                    sourceY,
                    characters[index].x,
                    127,
                    32,
                    32
                );
            }
            animationStep += 1;
        }

        static string A_num(double v) => v.ToString("0.################", CultureInfo.InvariantCulture);

        void showCharacterSelection()
        {
            stopAnimation();
            playing = false;
            selectedCharacter = -1;
            clearControls();
            animationStep = 0;
            renderCharacterSelection();

            for (int index = 0; index < characters.Length; index += 1)
            {
                var character = characters[index];
                int i = index;
                var button = createButton(character.name, "character-button", () => confirmCharacter(i));
                button.SetStyle("left", $"{A_num(character.x / 3.2)}%");
                button.SetAttr("aria-label", $"Choose {character.name}");

                var label = Dom.El("div");
                label.SetClassName("character-label");
                label.SetStyle("left", $"{A_num((character.x - 13) / 3.2)}%");
                label.SetText($"{character.name} · {string.Join("/", character.stats)}");
                controls.Append(label);
            }

            animationTimer = timers.setInterval(renderCharacterSelection, 140);
            setStatus("Choose your champion");
        }

        void confirmCharacter(int index)
        {
            selectedCharacter = index;
            var buttons = controls.QAll(".character-button");
            for (int buttonIndex = 0; buttonIndex < buttons.Count; buttonIndex += 1)
                buttons[buttonIndex].ClassToggle("selected", buttonIndex == index);
            var existingPanel = controls.Q(".confirm-panel");
            if (existingPanel != null) existingPanel.RemoveFromHierarchy();

            var character = characters[index];
            var panel = Dom.El("div");
            panel.SetClassName("confirm-panel");
            var message = Dom.El("span");
            message.SetText($"{character.name}: Might {character.stats[0]}, Protection {character.stats[1]}, Magic {character.stats[2]}. Accept?");
            panel.Append(message);

            var accept = Dom.El("button");
            accept.SetAttr("type", "button");
            accept.SetClassName("game-button");
            accept.SetText("Yes");
            accept.On("click", () => enterPlayfield());
            panel.Append(accept);

            var reject = Dom.El("button");
            reject.SetAttr("type", "button");
            reject.SetClassName("game-button");
            reject.SetText("No");
            reject.On("click", () =>
            {
                selectedCharacter = -1;
                panel.RemoveFromHierarchy();
                foreach (var button in controls.QAll(".character-button")) button.RemoveFromClassList("selected");
            });
            panel.Append(reject);
            controls.Append(panel);
            accept.FocusEl();
        }

        readonly string[] directions = { "North", "East", "South", "West" };
        string lastStatus = "";

        VisualElement gameUiRoot;
        GameUi gameUi;
        void init_A04()
        {
            gameUiRoot = Q("#game-ui");
            gameUi = new GameUi(gameUiRoot, (canvas, shape, palette) => drawIconInto(canvas, shape, palette), () => engine, () => playing, localStorage);
        }

        // Compact layout: the game canvas shows just the scene window of the original 320x200 playfield.
        // The message line, the party portraits and the quick-access inventory bar are "docked": copied from
        // the same page into their own canvases under the screen, with mouse events mapped back to the
        // playfield, so the original drawing and behaviour stay intact. Full-screen pages (the map, the
        // death menu) show the whole page.
        static readonly int[] SCENE = { 112, 0, 176, 120 };
        readonly Dock[] DOCKS =
        {
            new Dock { id = "#dock-compass", rect = new[] { 290, 0, 30, 30 } },
            new Dock { id = "#dock-lantern", rect = new[] { 289, 55, 31, 41 } },
        };
        bool compactActive = false;
        bool compactMode()
        {
            return playing && engine != null && !engine.automapActive && engine.gui.activeMenu == null && ((engine.updateFlags & 4) == 0 || engine.weaponsDisabled) && engine.currentBlock != 0;
        }
        void setFrameSize(int w, int h)
        {
            if (screen.canvas.width == w && screen.canvas.height == h) return;
            screen.resize(w, h);
            screen.canvas.parent.SetStyle("aspectRatio", $"{w} / {h}");
            if (filterScreen != null) filterScreen.resize(w, h);
        }
        byte[] cropFrame(byte[] page, int[] rect)
        {
            int sx = rect[0], sy = rect[1], w = rect[2], h = rect[3];
            var @out = new byte[w * h];
            for (int y = 0; y < h; y += 1) Array.Copy(page, (sy + y) * 320 + sx, @out, y * w, w);
            return @out;
        }
        // Maps a point on a canvas of the given source rect back to playfield coordinates.
        int[] toSource(CanvasEl canvas, int[] rect, DomEvent @event)
        {
            var box = canvas.worldBound;
            int x = (int)Math.Floor(((@event.clientX - box.xMin) / box.width) * rect[2]);
            int y = (int)Math.Floor(((@event.clientY - box.yMin) / box.height) * rect[3]);
            return new[] { rect[0] + Math.Min(rect[2] - 1, Math.Max(0, x)), rect[1] + Math.Min(rect[3] - 1, Math.Max(0, y)) };
        }
        void init_A05()
        {
            foreach (var dock in DOCKS)
            {
                dock.canvas = Q(dock.id) as CanvasEl;
                if (dock.canvas == null) continue;
                dock.canvas.width = dock.rect[2];
                dock.canvas.height = dock.rect[3];
                dock.ctx = dock.canvas.getContext("2d");
                dock.image = dock.ctx.createImageData(dock.rect[2], dock.rect[3]);
                dock.canvas.On("contextmenu", (evt) => evt.preventDefault());
                dock.canvas.On("mousemove", (evt) => { if (playing && engine != null) { var p = toSource(dock.canvas, dock.rect, evt); engine.pushMouse(p[0], p[1]); } });
                dock.canvas.On("mousedown", (evt) => { if (!playing || engine == null) return; evt.preventDefault(); var p = toSource(dock.canvas, dock.rect, evt); engine.pushMouse(p[0], p[1], evt.button == 2 ? 2 : 1); });
                dock.canvas.On("mouseup", (evt) =>
                {
                    if (!playing || engine == null) return;
                    var p = toSource(dock.canvas, dock.rect, evt);
                    int x = p[0], y = p[1];
                    engine.pushMouse(x, y);
                    engine.events.Add(new Lol.InputEvent { type = "mouseup", x = x, y = y, button = evt.button == 2 ? 2 : 1 });
                });
            }
        }
        void drawDocks(byte[] page, byte[] palette, bool show)
        {
            foreach (var dock in DOCKS)
            {
                if (dock.canvas == null) continue;
                // The compass/lantern column is blank stone until the party owns them: hide the docks then.
                bool visible = show && !((dock.id == "#dock-compass" || dock.id == "#dock-lantern") && engine.weaponsDisabled);
                if (dock.id == "#dock-compass" && (engine.flagsTable[31] & 0x40) == 0) visible = false;
                if (dock.id == "#dock-lantern" && (engine.flagsTable[31] & 0x08) == 0) visible = false;
                dock.canvas.SetHidden(!visible);
                if (!visible) continue;
                int sx = dock.rect[0], sy = dock.rect[1], w = dock.rect[2], h = dock.rect[3];
                var data = dock.image.data;
                for (int y = 0, o = 0; y < h; y += 1)
                {
                    int row = (sy + y) * 320 + sx;
                    for (int x = 0; x < w; x += 1, o += 4)
                    {
                        int c = page[row + x] * 3;
                        data[o] = palette[c];
                        data[o + 1] = palette[c + 1];
                        data[o + 2] = palette[c + 2];
                        data[o + 3] = 255;
                    }
                }
                dock.ctx.putImageData(dock.image, 0, 0);
            }
        }

        // Like the original options menu: monsters and clocks stop while a host window (menu, journal,
        // map, character screen...) is open. Only our own pause is undone, never one the engine holds.
        bool hostPaused = false;
        void syncPause()
        {
            if (!playing) { hostPaused = false; return; }
            bool want = Q(".modal:not([hidden]):not(.death-modal)") != null;
            if (want && !hostPaused && !engine.sysTimerPaused) { engine.uiPauseTimers(true); hostPaused = true; }
            else if (!want && hostPaused) { hostPaused = false; if (!engine.sysTimerPaused) engine.uiPauseTimers(false); }
        }

        // The engine presents after every step of a fade or an animation, up to a dozen times a frame, and only
        // the last is ever seen: each present keeps its picture (a copy), and the page is drawn from it once
        // per frame (flushPresent). Drawing it every time cost ~2ms a go - cinematics fell under 30 FPS.
        Lol.Screen presentScreen;
        byte[] presentPixels;
        readonly byte[] presentPalette = new byte[768];
        bool presentDue;

        void presentEngine(Lol.Screen gameScreen)
        {
            if (!playing || engine == null || gameScreen != engine.screen) return;   // a stopped (or replaced) game draws nothing
            var page = gameScreen.page(0);
            if (presentPixels == null || presentPixels.Length != page.Length) presentPixels = new byte[page.Length];
            Buffer.BlockCopy(page, 0, presentPixels, 0, page.Length);
            for (int i = 0; i < 768; i += 1) presentPalette[i] = (byte)((gameScreen.screenPalette[i] * 255) / 63);
            presentScreen = gameScreen;
            presentDue = true;
        }

        /// <summary>Once a frame, after the engine's turn: the page shows the last picture presented.</summary>
        public void flushPresent()
        {
            if (!presentDue) return;
            presentDue = false;
            double perfAt = Perf.Now;
            try { presentEngineNow(presentScreen); } finally { Perf.Took("present", perfAt); }
        }

        void presentEngineNow(Lol.Screen gameScreen)
        {
            double presentStart = Perf.Now;
            syncPause();
            if (playing && compactActive) engine.uiAutoSelect();
            var palette = (byte[])presentPalette.Clone();
            var full = (byte[])presentPixels.Clone();
            compactActive = compactMode();
            // In the page interface the system pointer is used; the game's own cursor is only drawn on the
            // full 320x200 screens (title, map, death menu). An item in hand becomes a CSS cursor image.
            if (compactActive) updateCssCursor(); else { screen.canvas.SetStyle("cursor", ""); cssCursorKey = ""; if (engine.cursorShape != null) drawCursor(full, engine.cursorShape, engine.mouseX - engine.cursorHotX, engine.mouseY - engine.cursorHotY); }
            if (compactActive)
            {
                setFrameSize(SCENE[2], SCENE[3]);
                screen.draw(cropFrame(full, SCENE), palette);
            }
            else
            {
                setFrameSize(320, 200);
                screen.draw(full, palette);
            }
            double pt = Perf.Now; Perf.Took("p.screen", presentStart);
            drawDocks(full, palette, compactActive);
            Perf.Took("p.docks", pt); pt = Perf.Now;
            presentHd();
            presentCine();
            Perf.Took("p.hd", pt); pt = Perf.Now;
            gameUi.update();
            Perf.Took("p.gameui", pt); pt = Perf.Now;
            charScreen.update();
            updateMonsterBars();
            updateRestButton();
            updateLoot();
            scanItems();
            Perf.Took("p.misc", pt); pt = Perf.Now;
            updateCredits();
            updateQuests();
            renderNotes();
            Perf.Took("p.quests", pt); pt = Perf.Now;
            if (mapTools != null) mapTools.SetHidden(minimap.canvas.IsHidden());
            var hintBtn = Q("#hint"); if (hintBtn != null) hintBtn.SetHidden(!settings.hints);
            var stashBtn = Q("#stash"); if (stashBtn != null) stashBtn.SetHidden(engine.itemInHand == 0);
            var fx = Q("#effect-timer");
            if (fx != null) { var list = engine.uiEffectTimers(); fx.SetHidden(list.Count == 0); fx.SetText(string.Join(" · ", list.Select((t) => $"{t.name}: {t.seconds}s"))); }
            var mz = Q("#minimap-zoom"); if (mz != null) mz.SetHidden(minimap.canvas.IsHidden());
            minimap.draw(engine);
            Perf.Took("p.minimap", pt); pt = Perf.Now;
            updateSidebar();
            updateTravelLock();
            Perf.Took("p.sidebar", pt); pt = Perf.Now;
            // The crafted potions are extra item properties: register them as soon as there is a game, so a
            // potion carried in a loaded save knows its own name and effect.
            if (engine.itemProperties != null && engine.itemProperties.Count > 0 && engine.extraItems == null) CraftingUi.registerPotions(engine);
            string key = $"{engine.currentLevel}:{engine.currentBlock}:{engine.currentDirection}";
            if (key != lastStatus)
            {
                lastStatus = key;
                string where = engine.uiInDungeon() ? $"The Imp's Pit · floor {engine.uiDungeonInfo().depth}" : engine.levelName();
                setStatus($"{where} · {directions[engine.currentDirection]}");
                status.SetTitle($"Level {engine.currentLevel}, block {engine.currentBlock}");
                rememberVisit();
            }
            renderErrand();
            renderDungeon();
        }

        // ---- port helpers (not in the JS) ----

        // el.onclick / form.onsubmit / el.onkeydown = fn: a replaceable handler (null clears it).
        readonly Dictionary<(VisualElement, string), Action<DomEvent>> A_onProps = new Dictionary<(VisualElement, string), Action<DomEvent>>();
        void A_setOn(VisualElement el, string type, Action<DomEvent> fn)
        {
            var k = (el, type);
            bool wired = A_onProps.ContainsKey(k);
            A_onProps[k] = fn;
            if (wired) return;
            void fire(DomEvent ev) { if (A_onProps.TryGetValue(k, out var f) && f != null) f(ev); }
            if (type != "submit") { el.On(type, fire); return; }
            // A form submits from its submit button, or on Enter inside it (implicit submission).
            foreach (var b in el.QAll("button[type=submit]")) b.On("click", (evt) => fire(new DomEvent { type = "submit", target = el, currentTarget = el }));
            el.RegisterCallback<KeyDownEvent>((u) =>
            {
                if (u.keyCode != KeyCode.Return && u.keyCode != KeyCode.KeypadEnter) return;
                u.StopPropagation();
                Dom.Invoke(() => fire(new DomEvent { type = "submit", target = el, currentTarget = el }));
            }, TrickleDown.TrickleDown);
        }
    }
}
