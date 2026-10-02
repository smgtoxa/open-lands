// src/main.mjs lines 2798-3655 (section E): inventory mode, loot, chest, credits, the rest button,
// map notes and the full map, modals, photo mode, full screen, the HD hooks (not ported yet), key
// bindings, settings, the lantern and spell sidebars, the item-in-hand cursor, runGame and start,
// and the page's last listeners. Ported 1:1 (docs/port/HOST.md). C# 9.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>main.mjs `let settings = { ... }`: the page's settings, stored under lol.settings (same JSON keys).</summary>
    public sealed class WebSettings
    {
        public string voice = "both";
        public bool sfx = true;
        public bool music = true;
        public bool scroll = true;
        public bool minimap = true;
        public int minimapRadius = 6;
        public int difficulty = 1;
        public bool hd = false;
        public string autosave = "0";
        public bool autosaveLevel = true;
        public Dictionary<string, string> keys = new Dictionary<string, string>();
        public string uiFont = "normal";
        public bool colorblind = false;
        public bool mods = false;
        public bool intro = true;
        public bool hints = false;
        public bool craft = false;
        public bool flee = false;
        public bool wear = false;
        public string respawn = "both";
        public string inventorySize = "96";
        public bool randomizer = false;
        public string seed = "";
        public string ngplus = "0";
        public bool fps = false; // Unity build only: the frame-rate counter in the corner
        public bool fantasy = true; // Unity build only: the framed fantasy interface (page-fantasy.uss)
        public bool textured = false; // Unity build only: the textured world packs (tools/textured, GameData/textured)

        /// <summary>settings[name]</summary>
        public object Get(string name) => typeof(WebSettings).GetField(name)?.GetValue(this);

        /// <summary>settings[name] = value (a checkbox's bool or a field's string, as the inputs give them).</summary>
        public void Set(string name, object value)
        {
            var f = typeof(WebSettings).GetField(name);
            if (f == null) return;
            var v = Convert(f.FieldType, value is bool b ? JsonValue.Create(b) : JsonValue.Create(value?.ToString()));
            if (v != null) f.SetValue(this, v);
        }

        /// <summary>{ ...settings, ...JSON.parse(stored) }: every known key the stored object has (a browser save
        /// may hold numbers as strings after a select changed them, so values are read leniently).</summary>
        public void Merge(JsonObject stored)
        {
            if (stored == null) return;
            foreach (var kv in stored)
            {
                var f = typeof(WebSettings).GetField(kv.Key);
                if (f == null) continue;
                var v = Convert(f.FieldType, kv.Value);
                if (v != null) f.SetValue(this, v);
            }
        }

        static object Convert(Type t, JsonNode n)
        {
            if (n == null) return null;
            if (t == typeof(Dictionary<string, string>))
                return n is JsonObject o ? o.Where(e => e.Value is JsonValue).ToDictionary(e => e.Key, e => Str(e.Value)) : null;
            if (!(n is JsonValue j)) return null;
            var kind = j.GetValueKind();
            if (t == typeof(bool))
                return kind == JsonValueKind.True ? true : kind == JsonValueKind.False ? false
                    : kind == JsonValueKind.Number ? j.GetValue<double>() != 0 : kind == JsonValueKind.String ? (object)(j.GetValue<string>() != "") : null;
            if (t == typeof(string)) return Str(j);
            if (t == typeof(int))
            {
                double d = kind == JsonValueKind.Number ? j.GetValue<double>()
                    : kind == JsonValueKind.String && double.TryParse(j.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : double.NaN;
                return double.IsNaN(d) ? null : (object)(int)d;
            }
            return null;
        }

        static string Str(JsonNode n)
        {
            var j = (JsonValue)n;
            switch (j.GetValueKind())
            {
                case JsonValueKind.String: return j.GetValue<string>();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                case JsonValueKind.Number: return j.GetValue<double>().ToString(CultureInfo.InvariantCulture);
                default: return "";
            }
        }
    }

    /// <summary>cineOps values: { x, y, w, h, page }</summary>
    public sealed class CineOp
    {
        public int x, y, w, h, page;
    }

    /// <summary>facingWeakness(): { name, taken }</summary>
    public sealed class FacingWeakness
    {
        public string name;
        public List<DamageTakenEntry> taken;
    }

    /// <summary>What openTalkArchive returns: a TLK archive read whole from the data folder (the browser streamed it with RemotePak).</summary>
    public sealed class PakTalkArchive : ITalkArchive
    {
        readonly PakArchive pak;
        public PakTalkArchive(PakArchive pak) { this.pak = pak; }
        public bool has(string name) => pak.has(name);
        public Task<byte[]> get(string name) => Task.FromResult(pak.get(name));
    }

    public sealed partial class Web
    {
        static object Arg(object[] args, int i) => args != null && i < args.Length ? args[i] : null;
        static void click(VisualElement e) => e.Fire("click", new DomEvent { target = e });
        static string num(double v) => v.ToString(CultureInfo.InvariantCulture);

        void updateInventoryMode()
        {
            var mode = Q("#inventory-mode");
            if (mode != null)
            {
                mode.SetHidden(string.IsNullOrEmpty(inventoryMode));
                mode.SetText(inventoryMode == "sell" ? "Selling: click the item to offer" : inventoryMode == "drop" ? "Dropping: click the items to drop on the floor" : "");
            }
            var b = Q("#inventory-drop-mode");
            if (b != null) b.ClassToggle("on", inventoryMode == "drop");
        }

        void init_E01()
        {
            on("#inventory-drop-mode", _ => { inventoryMode = inventoryMode == "drop" ? "" : "drop"; updateInventoryMode(); });
            on("#shop-open", _ => openTrade());
        }

        void updateLoot()
        {
            updateShop();
            updateChest();
            updateRest();
            renderTrade();
            if (tradeOverlay != null && !tradeOverlay.IsHidden()) { if (engine != null && engine.uiMerchantAhead() < 0) closeTrade(); else if (gameUi.log.Count != tradeSayCount) { tradeSayCount = gameUi.log.Count; renderTradeSay(); } }
            if (lootBox == null || engine == null) return;
            bool show = compactActive && engine.updateFlags == 0 && engine.needSceneRestore == 0 && engine.partyAwake;
            var items = show ? engine.uiFloorItems() : new List<FloorItem>();
            string key = string.Join(",", items.Select(f => $"{f.item}:{f.block}")) + (engine.itemInHand != 0 ? "|h" : "");
            if (key == lootKey) return;
            lootKey = key;
            lootBox.SetHidden(items.Count == 0);
            lootList.ReplaceChildren();
            var palette = engine.uiPalette();
            foreach (var f in items)
            {
                var b = Dom.El("button");
                b.SetAttr("type", "button");
                // A Coin Purse has no icon of its own (its item record points at a sword's; the original only ever
                // shows purses on the floor, never as an icon): the floor list shows the page's coin instead.
                // (The web page draws the sword here too.)
                VisualElement iconEl;
                if (engine.itemsInPlay[f.item].itemPropertyIndex == 243) { var coin = (SvgEl)Dom.El("svg"); coin.SetClassName("slot-icon"); coin.UseSymbol("#i-coin"); iconEl = coin; }
                else { var icon = (CanvasEl)Dom.El("canvas"); icon.width = 24; icon.height = 24; icon.SetClassName("slot-icon"); drawIconInto(icon, engine.getItemIconShapePtr(f.item), palette); iconEl = icon; }
                b.Append(iconEl, string.IsNullOrEmpty(f.name) ? "item" : f.name);
                if (f.ahead) { var tag = Dom.El("span"); tag.SetClassName("ahead"); tag.SetText("ahead"); b.Append(tag); }
                b.SetTitle($"{engine.itemTooltip(f.item)}\nClick to take it into the inventory."); CssTooltip.MarkItem(b, f.item, -1);
                b.SetDisabled(engine.itemInHand != 0);
                b.On("click", () => { engine.queueAsync(() => engine.uiTakeFloorItem(f.item, f.block)); b.BlurEl(); });
                lootList.Append(b);
            }
        }

        // chest ahead: a Search button (the same click as on the chest's lid)
        string chestKey = "";

        void updateChest()
        {
            var box = Q("#chest");
            if (box == null || engine == null) return;
            var chest = compactActive && engine.partyAwake ? engine.uiChestAhead() : null;
            string key = chest != null ? $"{chest.block}:{chest.wall}:{(engine.itemInHand != 0 ? "h" : "")}" : "";
            if (key == chestKey) return;
            chestKey = key;
            box.SetHidden(chest == null);
            if (chest != null)
            {
                Q("#chest-label").SetText(chest.locked ? "Locked chest ahead" : "Chest ahead");
                var b = Q("#chest-search");
                b.Q("span").SetText(chest.locked ? (engine.itemInHand != 0 ? "Use on lock" : "Search") : "Search");
                b.SetTitle(chest.locked ? "Locked: hold lockpicks (rogue skill) or its key and click" : "Take what is inside");
            }
        }

        // Credits in the Actions panel and shop prices as the scripts compute them.
        VisualElement creditsEl;
        int creditsShown = -1;
        VisualElement restButton;

        void init_E02()
        {
            on("#chest-search", _ => { if (playing && engine != null) engine.queueAsync(() => engine.uiSearchChest()); });
            on("#rotate", _ => { if (playing && engine != null && engine.uiRotateParty()) gameUi.partyKey = ""; });
            on("#loot-all", _ => { if (playing && engine != null) engine.queueAsync(async () => { foreach (var f in engine.uiFloorItems()) if (await engine.uiTakeFloorItem(f.item, f.block) == 0) break; }); });
            creditsEl = Q("#credits");
        }

        void updateCredits()
        {
            if (creditsEl == null || engine == null) return;
            if (engine.credits == creditsShown) return;
            creditsShown = engine.credits;
            creditsEl.SetText($"{engine.credits} crowns");
        }

        void init_E03()
        {
            restButton = Q("#rest");
        }

        void updateRestButton()
        {
            if (restButton == null || engine == null) return;
            var caption = restButton.Q("span");
            if (engine.partyAwake)
            {
                if (restButton.Dataset().ContainsKey("resting")) { restButton.Dataset().Remove("resting"); if (caption != null) caption.SetText("Camp"); restButton.SetStyle("background", ""); restButton.SetTitle("Make camp: rest, heal and travel (R)"); }
                return;
            }
            int hp = 0; int hpMax = 0; int mp = 0; int mpMax = 0;
            foreach (var c in engine.characters) if ((c.flags & 1) != 0 && (c.flags & 8) == 0) { hp += c.hitPointsCur; hpMax += c.hitPointsMax; mp += c.magicPointsCur; mpMax += c.magicPointsMax; }
            int pct = Js.Round((100.0 * (hp + mp)) / Math.Max(1, hpMax + mpMax));
            restButton.Dataset()["resting"] = "1";
            if (caption != null) caption.SetText(pct >= 100 ? "Camp" : $"{pct}%");
            restButton.SetTitle($"In camp: health {hp}/{hpMax}, magic {mp}/{mpMax}. Leave with \"Exit the camp\".");
            restButton.SetStyle("background", $"linear-gradient(90deg, #2f5a2a {pct}%, #1c1710 0)");
        }

        VisualElement savesFile;

        void init_E04()
        {
            on("#lantern-toggle", _ => { if (playing && engine != null) { engine.toggleLantern(); lanternKey = ""; } });
            on("#lantern-refill", _ => { if (playing && engine != null) engine.queueAsync(() => engine.refillLantern()); });
            on("#options", _ => toggleModal("#menu-overlay", true));
            on("#map-open", _ => { if (engine != null && playing) openFullMap(); });
            on("#menu-open", _ => toggleModal("#menu-overlay", true));
            on("#menu-settings", _ => { toggleModal("#menu-overlay", false); toggleModal("#settings-overlay", true); });
            // Export / import of all saves as a JSON file (works in the browser and the desktop app alike).
            on("#saves-export", _ =>
            {
                if (engine != null && playing && canSaveNow()) saveToSlot("auto", true); // include the current position
                // the download: into Downloads, as the browser saves it
                FileDialog.Download($"lands-of-lore-saves-{DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json", System.Text.Encoding.UTF8.GetBytes(Saves.exportSaves()));
            });
            savesFile = Q("#saves-file");
            // if (window.lolDesktop) the "Game data" row shows: this player is the desktop app
            var dataRow = Q("#setting-data-row");
            if (dataRow != null) dataRow.SetHidden(false);
            on("#setting-import-data", _ => SetupPage.Show(sched, leavePage));
            // savesFile.click(): the system "Open" dialog stands for the browser's file picker
            on("#saves-import", ev => { if (savesFile != null) _ = savesFileChange(); });
            on("#menu-exit", _ => { toggleModal("#menu-overlay", false); if (engine != null && playing) engine.restartRequested = true; });
            foreach (var button in QAll(".pad button")) button.On("click", () => { gameEvent(new Lol.InputEvent { type = "key", key = button.Dataset().TryGetValue("key", out var k) ? k : null }); button.BlurEl(); });
        }

        /// <summary>savesFile.addEventListener("change", async () => { ... })</summary>
        async Task savesFileChange()
        {
            string file = FileDialog.Open("Import saves", "Saves (*.json)", "*.json");
            if (file == null) return;
            try
            {
                string text = File.ReadAllText(file);
                bool replace = Saves.listSaves().Count() > 0 && await askConfirm("Replace the saves already in this browser? Answering no keeps them and only adds or overwrites the slots from the file.", title: "Import saves", ok: "Replace all", cancel: "Merge");
                int count = Saves.importSaves(text, replace ? "replace" : "merge");
                renderSaveSlots();
                reloadBrowserState();
                // window.alert: a toast here
                toast($"Imported {count} save slot{(count == 1 ? "" : "s")}. Each slot carries its own chest, journal and notes; reload the page to apply imported settings.");
            }
            catch (Exception error)
            {
                toast($"Import failed: {error.Message}");
            }
        }

        // Minimap: left-click walks to an explored cell (click-to-move), right-click opens the full map.
        // The page's own full map (the original automap screen is no longer used).
        async Task addNoteAt(int block)
        {
            if (!playing || engine == null) return;
            int level = engine.currentLevel;
            string text = await askText($"Note for block {block}:", "");
            if (text == null || text.Trim() == "") return;
            if (!notes.TryGetValue(level, out var list) || list == null) notes[level] = list = new List<MapNote>();
            string t = text.Trim();
            list.Add(new MapNote { block = block, text = t.Length > 60 ? t.Substring(0, 60) : t });
            saveNotes();
            renderNotes();
        }

        FullMap fullMap;

        void init_E05()
        {
            fullMap = new FullMap(
                root: Q("#map-overlay"), getEngine: () => engine,
                getNotes: (level) => (engine != null && notes.TryGetValue(level, out var l) ? l : null) ?? new List<MapNote>(),
                addNote: addNoteAt,
                walkTo: (block) => { if (playing && block != engine.currentBlock && !engine.autoWalkTo(block)) gameUi.message("No known way there.", "system"); },
                explore: () => click(Q("#explore-next")),
                getVisited: () => visited,
                getHints: () => settings.hints,
                travelTo: (level) => { if (visited.TryGetValue(level, out var target) && target != null && engine != null && playing && engine.updateFlags == 0 && !engine.uiInCombat()) engine.queueAsync(() => engine.debugTeleport(level, target.block, target.dir ?? 0)); });
            minimap.renderer = (ctx, w, h, cell, offset) => fullMap.render(ctx, w, h, cell, offset);
            if (minimap.canvas != null)
            {
                minimap.canvas.On("contextmenu", (ev) => { ev.preventDefault(); openFullMap(); });
                // Hovering a cell names what lies there, so the green dots stop being "something".
                minimap.canvas.On("mousemove", (ev) =>
                {
                    if (engine == null || !playing) { minimap.canvas.SetTitle(""); return; }
                    int block = minimap.blockAt(engine, ev);
                    bool known = block >= 0 && (block == engine.currentBlock || (engine.levelBlockProperties[block].flags & 7) == 7);
                    var items = known ? engine.uiItemsOnBlock(block).Select(f => f.name).Where(n => !string.IsNullOrEmpty(n)).ToList() : new List<string>();
                    minimap.canvas.SetTitle(items.Count > 0 ? string.Join(", ", items) : "Click a cell to walk there, right-click for the full map, wheel to zoom");
                });
                minimap.canvas.On("click", (ev) =>
                {
                    if (engine == null || !playing || (engine.flagsTable[31] & 0x10) == 0) return;
                    int block = minimap.blockAt(engine, ev);
                    if (block == engine.currentBlock) { openFullMap(); return; }
                    if (block < 0 || !engine.autoWalkTo(block)) gameUi.message("No known way there.", "system");
                });
            }
        }

        void openFullMap() { if (engine == null || !playing) return; if ((engine.flagsTable[31] & 0x10) == 0) { gameUi.message("You have no map yet.", "system"); return; } if (fullMap.isOpen) fullMap.close(); else fullMap.open(); }

        void toggleModal(string id, bool? show = null)
        {
            var modal = Q(id);
            if (modal == null) return;
            modal.SetHidden(show == null ? !modal.IsHidden() : !show.Value);
        }

        void init_E06()
        {
            on("#settings-open", _ => toggleModal("#settings-overlay", true));
            on("#setting-respawn", _ => { if (playing && engine != null && !engine.uiInDungeon()) engine.queueAsync(() => engine.uiRespawnMonsters()); });
            on("#setting-recover", _ => { if (playing && engine != null) engine.queueAsync(() => engine.uiRecoverLostLoot()); });
            on("#setting-restore-skull", _ =>
            {
                if (!playing || engine == null) return;
                if (engine.uiRestoreGreenSkull() == 0) gameUi.message("The party already carries a Green Skull.", "system");
                inventoryKey = ""; sidebarKey = "";
            });
            on("#share-card", _ => shareCard());
        }

        // Full screen: the desktop app switches the window; in a browser the Fullscreen API on the page.
        // Photo mode (F12): the scene at 4x (through the HD renderer when it is on), saved as a PNG.
        void photo()
        {
            if (engine == null || !playing) return;
            bump("photos");
            var src = hdCanvas != null && !hdCanvas.IsHidden() ? hdCanvas : screen.canvas;
            var c = (CanvasEl)Dom.El("canvas");
            int scale = src == screen.canvas ? 4 : 1;
            c.width = src.width * scale; c.height = src.height * scale;
            var ctx = c.getContext("2d");
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(src, 0, 0, c.width, c.height);
            // a.download: into Downloads, as the browser saves it
            FileDialog.Download($"lands-of-lore-L{engine.currentLevel}-{engine.currentBlock}-{num(now())}.png", c.Texture.EncodeToPNG());
            toast("Screenshot saved");
        }

        void toggleFullScreen()
        {
            // window.lolDesktop: not this host. document.fullscreenElement / requestFullscreen -> the Unity window.
            if (UnityEngine.Screen.fullScreen) UnityEngine.Screen.fullScreen = false;
            else UnityEngine.Screen.fullScreen = true;
        }

        void init_E07()
        {
            on("#fullscreen", _ => toggleFullScreen());
            on("#setting-fullscreen", _ => toggleFullScreen());
            // if (window.lolDesktop) { #setting-data-row shown }: no desktop bridge here, the row stays hidden.
            on("#setting-import-data", _ => { /* window.lolDesktop only: /setup.html */ });
            // document.addEventListener("keydown", F11): see documentKeydown.
            on("#journal-open", _ => toggleJournal());
            on("#debug-open", _ => toggleModal("#debug-overlay", true));
            on("#debug-reagents", _ =>
            {
                var pouch = CraftingUi.fillPouch(engine, 2);
                reagentKey = ""; craftKey = ""; inventoryKey = "";
                renderReagents(); renderCraft(true);
                string what = string.Join(", ", CraftingUi.REAGENTS.Select(e => $"{e.Value.name} {pouch[e.Key]}"));
                gameUi.message($"Reagent pouch filled: {what}.", "system");
                toast("Reagent pouch filled: two of every potion.", "fx-toast-ach");
            });
            foreach (var button in QAll(".modal-close")) button.On("click", () => { string close = button.Dataset().TryGetValue("close", out var v) ? v : null; if (close == "trade-overlay") closeTrade(); else toggleModal($"#{close}", false); });
            foreach (var modal in QAll(".modal")) modal.On("click", (ev) => { if (ev.target == modal) modal.SetHidden(true); });
        }

        // HD asset packs: one per level, loaded on demand from private/hd/level<N>/ (hdFolder here:
        // --hd PATH or GameData/hd, see HostMain.FindHd; null = no HD files, every pack fails to load).
        CanvasEl hdCanvas;
        HdScene hdScene;
        readonly Dictionary<string, HdPack> hdPacks = new Dictionary<string, HdPack>();
        /// <summary>Unity build: the textured world packs (the HD pack format, made by tools/textured from the
        /// player's own game data: GameData/textured, or "textured" beside the imported game).</summary>
        public string texturedFolder;
        /// <summary>the scene is drawn from packs: HD assets, or the textured world (which wins when both are on)</summary>
        bool packsOn => settings.hd || settings.textured;

        HdPack hdPackFor(int level)
        {
            string folder = settings.textured ? texturedFolder : hdFolder;
            string key = $"{folder}|{level}";
            if (!hdPacks.ContainsKey(key))
            {
                var pack = new HdPack(folder != null ? Path.Combine(folder, $"level{level}") : null, sched);
                hdPacks[key] = pack;
                async Task loaded() { bool ok = await pack.load(); if (ok && engine != null) engine.sceneUpdateRequired = true; else if (!ok) log($"No {(settings.textured ? "textured" : "HD")} pack for level {level}"); }
                sched.Observe(loaded());
            }
            return hdPacks[key];
        }

        // HD cutscene pictures: the first frame of every dialogue/cutscene WSA has an HD version in
        // private/hd/cinema/<NAME>.png; while the game shows that WSA (any frame), the HD picture is drawn
        // over it at 4x. The talking-head lip animation is not reproduced: the portrait stays still.
        CanvasEl cineCanvas;
        readonly Dictionary<string, CineOp> cineOps = new Dictionary<string, CineOp>(); // WSA name -> { x, y, w, h, page }
        readonly Dictionary<string, object> cineImages = new Dictionary<string, object>(); // name -> Image | null | Task<byte[]> (loading: JS undefined)

        void init_E08()
        {
            hdCanvas = Q("#screen-hd") as CanvasEl;
            hdScene = hdCanvas != null ? new HdScene(hdCanvas) : null;
            if (hdScene != null) hdScene.palette = () => engine?.screen.screenPalette;
            cineCanvas = Q("#screen-cine") as CanvasEl;
        }

        Texture2D cineImage(string name)
        {
            if (!cineImages.ContainsKey(name))
            {
                // img.src = private/hd/cinema/<name without .WSA>.png; onload / onerror land in decode below
                string file = hdFolder != null ? Path.Combine(hdFolder, "cinema", System.Text.RegularExpressions.Regex.Replace(name, @"\.WSA$", "") + ".png") : null;
                cineImages[name] = file != null ? HdImages.read(file) : null;
            }
            if (cineImages[name] is Task<byte[]> loading) cineImages[name] = loading.IsCompleted ? HdImages.decode(loading) : (object)loading;
            return cineImages[name] as Texture2D;
        }

        void presentCine()
        {
            if (cineCanvas == null || engine == null) return;
            bool inScene = engine.tim.currentTim != null || (engine.updateFlags & 3) != 0 || engine.needSceneRestore != 0 || engine.activeTim.Any(t => t != null);
            if (!inScene) cineOps.Clear();
            if (!settings.hd || cineOps.Count == 0) { cineCanvas.SetHidden(true); return; }
            int s = 4;
            int ox = compactActive ? 112 : 0; int ow = compactActive ? 176 : 320; int oh = compactActive ? 120 : 200;
            if (cineCanvas.width != ow * s || cineCanvas.height != oh * s) { cineCanvas.width = ow * s; cineCanvas.height = oh * s; }
            var ctx = cineCanvas.getContext("2d");
            ctx.clearRect(0, 0, cineCanvas.width, cineCanvas.height);
            int drawn = 0;
            foreach (var entry in cineOps)
            {
                var op = entry.Value;
                if (op.page != 0 && op.page != 2) continue;
                var img = cineImage(entry.Key);
                if (img == null) continue;
                ctx.drawImage(img, (op.x - ox) * s, op.y * s, op.w * s, op.h * s);
                drawn += 1;
            }
            cineCanvas.SetHidden(drawn == 0);
        }

        // How dark the scene currently is: the live screen palette against the level's working palette.
        double sceneBrightness()
        {
            if (engine == null || engine.screen == null) return 1;
            var live = engine.screen.screenPalette;
            var bas = engine.screen.getPalette(0);
            double a = 0; double b = 0;
            for (int i = 3; i < 384; i += 1) { a += live[i]; b += bas[i]; }
            if (b == 0) return 1;
            return Math.Max(0.2, Math.Min(1, a / b));
        }

        // The window still shows the 3D view of the recorded frame (not a scroll, a scene or a spell drawn over it):
        // fewer than 4% of its pixels differ from what the frame drew.
        bool sceneShowsFrame(HdFrame frame)
        {
            if (frame?.window == null || presentPixels == null || presentPixels.Length < 320 * 120) return true;
            int differ = 0, limit = 176 * 120 * 4 / 100;
            for (int y = 0; y < 120; y += 1)
            {
                int row = y * 320 + 112, w = y * 176;
                for (int x = 0; x < 176; x += 1)
                    if (presentPixels[row + x] != frame.window[w + x] && ++differ > limit) return false;
            }
            return true;
        }

        void presentHd()
        {
            if (hdScene == null) return;
            bool usable = packsOn && compactActive && sceneShowsFrame(engine.hdFrame) && !engine.weaponsDisabled && engine.updateFlags == 0 && engine.needSceneRestore == 0 && !engine.sysTimerPaused;
            if (!usable) { hdCanvas.SetHidden(true); return; }
            hdScene.pack = hdPackFor(engine.currentLevel);
            bool drawn = hdScene.draw(engine.hdFrame, true);
            hdCanvas.SetHidden(!drawn);
            // HD art is not palette-lit: match the screen palette instead (lantern, fades, spell flashes)
            double lit = sceneBrightness();
            string filter = lit < 0.995 ? $"brightness({lit.ToString("F3", CultureInfo.InvariantCulture)})" : "";
            // style.filter: SetStyle maps brightness() onto the canvas picture's tint (setting it again is cheap)
            hdCanvas.SetStyle("filter", filter);
            if (cineCanvas != null) cineCanvas.SetStyle("filter", filter);
            if (drawn && engine.cursorShape != null && !compactActive) drawHdCursor();
        }

        // The game cursor lives in the 320x200 frame, which the HD canvas covers: draw it on top as well.
        string cursorCacheKey;
        readonly CanvasEl cursorCacheCanvas = (CanvasEl)Dom.El("canvas");

        void drawHdCursor()
        {
            var shape = engine.cursorShape;
            int s = hdScene.pack.manifest.scale;
            string key = $"{(!string.IsNullOrEmpty(shape.key) ? shape.key : shape.width + "x" + shape.height)}|{engine.itemInHand}";
            if (cursorCacheKey != key)
            {
                cursorCacheKey = key;
                cursorCacheCanvas.width = shape.width;
                cursorCacheCanvas.height = shape.height;
                drawIconInto(cursorCacheCanvas, shape, engine.screen.screenPalette);
            }
            var ctx = hdScene.ctx;
            ctx.save();
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(cursorCacheCanvas, (engine.mouseX - engine.cursorHotX - 112) * s, (engine.mouseY - engine.cursorHotY) * s, shape.width * s, shape.height * s);
            ctx.restore();
        }

        // --- key bindings (Settings > Keys) ---
        static readonly Dictionary<string, string> DEFAULT_KEYS = new Dictionary<string, string> { ["attack"] = "F", ["cast"] = "C", ["inventory"] = "I", ["stash"] = "B", ["trade"] = "T", ["rotate"] = "O", ["journal"] = "J", ["map"] = "M", ["minimap"] = "N", ["rest"] = "R", ["explore"] = "X", ["quicksave"] = "F5", ["quickload"] = "F9", ["photo"] = "F12" };
        static readonly Dictionary<string, string> KEY_LABELS = new Dictionary<string, string> { ["attack"] = "Attack with the selected hero", ["cast"] = "Quick cast", ["inventory"] = "Inventory", ["stash"] = "Stash the item in hand", ["trade"] = "Trade with the merchant ahead", ["rotate"] = "Rotate the marching order", ["journal"] = "Journal", ["map"] = "Full map", ["minimap"] = "Show/hide minimap", ["photo"] = "Screenshot (photo mode)", ["rest"] = "Rest", ["explore"] = "Walk to nearest unexplored", ["quicksave"] = "Quick save", ["quickload"] = "Quick load" };

        bool keyIs(DomEvent @event, string action)
        {
            string want = settings.keys != null && settings.keys.TryGetValue(action, out var k) && !string.IsNullOrEmpty(k) ? k : DEFAULT_KEYS[action];
            return @event.key.Length == 1 ? @event.key.ToUpperInvariant() == want.ToUpperInvariant() : @event.key == want;
        }

        void renderKeyBindings()
        {
            var box = Q("#setting-keys");
            if (box == null) return;
            box.ReplaceChildren();
            foreach (var entry in KEY_LABELS)
            {
                string action = entry.Key, label = entry.Value;
                var row = Dom.El("label", "keybind", null, true);
                var input = (DomInput)Dom.El("input");
                input.SetAttr("type", "text");
                input.SetAttr("readonly", "");
                input.value = settings.keys != null && settings.keys.TryGetValue(action, out var k) && !string.IsNullOrEmpty(k) ? k : DEFAULT_KEYS[action];
                input.SetTitle("Click, then press the new key");
                input.On("keydown", (ev) =>
                {
                    ev.preventDefault();
                    ev.stopPropagation();
                    if (ev.key == "Escape") { input.BlurEl(); return; }
                    string key = ev.key.Length == 1 ? ev.key.ToUpperInvariant() : ev.key;
                    settings.keys = new Dictionary<string, string>(settings.keys ?? new Dictionary<string, string>()) { [action] = key };
                    input.value = key;
                    applySettings();
                    input.BlurEl();
                });
                row.Append(label, input);
                box.Append(row);
            }
            var reset = Dom.El("button");
            reset.SetAttr("type", "button");
            reset.SetText("Reset keys");
            reset.On("click", () => { settings.keys = new Dictionary<string, string>(); applySettings(); renderKeyBindings(); });
            // a row of its own like the keys above (its button as wide as theirs)
            var resetRow = Dom.El("label", "keybind", null, true);
            resetRow.Append(Dom.El("span", null, "Every key back to its default"), reset);
            box.Append(resetRow);
        }

        // --- settings (applied to every engine instance) ---
        const string SETTINGS_KEY = "lol.settings";
        public WebSettings settings = new WebSettings();
        Dictionary<string, VisualElement> settingInputs;

        void init_E09()
        {
            try { settings.Merge(JsonNode.Parse(localStorage.getItem(SETTINGS_KEY) ?? "null") as JsonObject); } catch (Exception) { /* ignore */ }
            // the page has no FPS or interface setting: their rows go in under "Show minimap"
            var minimapRow = Q("#setting-minimap")?.parent;
            if (minimapRow != null)
                foreach (var (id, text) in new[] { ("setting-textured", " Textured world (photo materials over the original walls, floors, doors and decorations; creatures unchanged)"), ("setting-fantasy", " Fantasy interface"), ("setting-fps", " Show FPS counter") })
                {
                    var input = Dom.El("input"); input.SetAttr("id", id); input.SetAttr("type", "checkbox");
                    var row = Dom.El("label"); row.SetClassName("setting"); row.Append(input, text);
                    minimapRow.parent.Insert(minimapRow.parent.IndexOf(minimapRow) + 1, row); CssLayout.Touch(row);
                }
            settingsTabs();
            settingInputs = new Dictionary<string, VisualElement> { ["hd"] = Q("#setting-hd"), ["voice"] = Q("#setting-voice"), ["sfx"] = Q("#setting-sfx"), ["music"] = Q("#setting-music"), ["autosave"] = Q("#setting-autosave"), ["autosaveLevel"] = Q("#setting-autosave-level"), ["uiFont"] = Q("#setting-font"), ["mods"] = Q("#setting-mods"), ["craft"] = Q("#setting-craft"), ["flee"] = Q("#setting-flee"), ["wear"] = Q("#setting-wear"), ["respawn"] = Q("#setting-respawn-mode"), ["intro"] = Q("#setting-intro"), ["hints"] = Q("#setting-hints"), ["inventorySize"] = Q("#setting-inventory"), ["randomizer"] = Q("#setting-randomizer"), ["seed"] = Q("#setting-seed"), ["ngplus"] = Q("#setting-ngplus"), ["colorblind"] = Q("#setting-colorblind"), ["scroll"] = Q("#setting-scroll"), ["minimap"] = Q("#setting-minimap"), ["difficulty"] = Q("#setting-difficulty"), ["fps"] = Q("#setting-fps"), ["fantasy"] = Q("#setting-fantasy"), ["textured"] = Q("#setting-textured") };
        }

        // Unity build only: the settings in tabs (the page has one long list under three headings); a row no tab
        // names stays below the tabs
        void settingsTabs()
        {
            var box = Q("#settings-overlay .modal-box");
            var head = Q("#settings-overlay .modal-head");
            var keys = Q("#setting-keys");
            if (box == null || head == null) return;
            var keysNote = keys != null && box.IndexOf(keys) + 1 < box.childCount ? box.ElementAt(box.IndexOf(keys) + 1) : null;
            foreach (var h in box.Children().Where(c => c.ClassListContains("tag-h3")).ToList()) h.RemoveFromHierarchy();
            VisualElement rowOf(string sel)
            {
                var e = Q(sel);
                while (e != null && e != box && !e.ClassListContains("setting")) e = e.parent;
                return e == box ? null : e;
            }
            var groups = new (string name, string[] rows)[]
            {
                ("Graphics", new[] { "#filter", "#setting-fullscreen", "#setting-textured", "#setting-hd", "#setting-fantasy", "#setting-scroll", "#setting-minimap", "#setting-fps" }),
                ("Sound", new[] { "#setting-voice", "#setting-sfx", "#setting-music" }),
                ("Gameplay", new[] { "#setting-difficulty", "#setting-autosave", "#setting-autosave-level", "#setting-intro", "#setting-inventory", "#setting-hints", "#setting-respawn-mode", "#setting-flee", "#setting-wear", "#setting-craft" }),
                ("Controls", new string[0]),
                ("Accessibility", new[] { "#setting-font", "#setting-colorblind" }),
                ("Advanced", new[] { "#setting-randomizer", "#setting-seed", "#setting-ngplus", "#setting-mods", "#setting-recover", "#setting-restore-skull", "#setting-respawn", "#setting-data-row" }),
            };
            box.AddToClassList("settings-box");
            var bar = Dom.El("div", "tabs");
            int at = box.IndexOf(head) + 1;
            box.Insert(at++, bar);
            var tabs = new List<VisualElement>();
            var pages = new List<VisualElement>();
            foreach (var (name, rows) in groups)
            {
                var page = Dom.El("section", "tab-page");
                foreach (var sel in rows) { var row = rowOf(sel); if (row != null) page.Append(row); }
                if (name == "Controls" && keys != null) { page.Append(keys); if (keysNote != null && keysNote.ClassListContains("panel-note")) page.Append(keysNote); }
                page.SetHidden(pages.Count > 0);
                box.Insert(at++, page);
                var tab = Dom.El("button", "tab" + (tabs.Count == 0 ? " active" : ""), name);
                tab.SetAttr("type", "button");
                bar.Append(tab);
                tabs.Add(tab); pages.Add(page);
            }
            for (int i = 0; i < tabs.Count; i += 1)
            {
                int me = i;
                tabs[i].On("click", () =>
                {
                    for (int j = 0; j < tabs.Count; j += 1) { tabs[j].ClassToggle("active", j == me); pages[j].SetHidden(j != me); }
                });
            }
            // every row the same: its words on the left, its control in a column of one width on the right
            foreach (var page in pages)
                foreach (var row in page.Children().Where(c => c.ClassListContains("setting")).ToList())
                {
                    bool control(VisualElement c) => c.userData is DomData d && (d.tag == "input" || d.tag == "select" || d.tag == "button");
                    var controls = row.Children().Where(control).ToList();
                    if (controls.Count == 0) continue;
                    string words = string.Concat(row.Children().Where(c => !control(c)).Select(c => c.GetText())).Trim();
                    // the page's link to its level designer (a web page): the words alone
                    words = words.Replace("Use level mods from the level designer", "Use level mods made in the level designer");
                    var text = Dom.El("div", "set-text", words);
                    var ctl = Dom.El("div", "set-ctl");
                    foreach (var c in controls) ctl.Append(c);
                    row.ReplaceChildren(text, ctl);
                    CssLayout.Touch(row);
                }
            // "Settings are remembered in this browser": the page's note, not this build's
            foreach (var note in box.Children().Where(c => c.ClassListContains("panel-note")).ToList()) note.SetHidden(true);
            CssLayout.Touch(box);
        }

        void applySettings()
        {
            try { localStorage.setItem(SETTINGS_KEY, Store.stringify(settings)); } catch (Exception) { /* ignore */ }
            minimap.visible = settings.minimap;
            minimap.lastKey = "";
            Dom.document.ClassToggle("ui-large", settings.uiFont == "large");
            Dom.document.ClassToggle("colorblind", settings.colorblind);
            showFps(settings.fps);
            if (Dom.document.ClassListContains("theme-fantasy") != settings.fantasy) { Dom.document.ClassToggle("theme-fantasy", settings.fantasy); CssLayout.Restyle(); }
            if (minimap.radius != settings.minimapRadius) minimap.setRadius(settings.minimapRadius != 0 ? settings.minimapRadius : 6);
            if (engine == null) return;
            engine.speechEnabledFlag = settings.voice != "text";
            engine.textEnabledFlag = settings.voice != "speech";
            engine.sfxEnabled = settings.sfx;
            if (engine.musicEnabled != settings.music)
            {
                if (!settings.music) engine.snd_stopMusic();
                engine.musicEnabled = settings.music;
                if (settings.music && engine.curMusicTheme >= 250) engine.snd_playTrack(engine.curMusicTheme);
            }
            engine.adlibPost(new AdlibMessage { type = "volume", music = settings.music ? engine.musicVolume : 0, sfx = 255 });
            engine.smoothScrollingEnabled = settings.scroll;
            engine.setInventorySize(jsNumberOr(settings.inventorySize, 48));
            engine.monsterDifficulty = settings.difficulty;
            engine.hdRecording = packsOn;
            engine.fleeingMonsters = settings.flee;
            engine.weaponWear = settings.wear;
            engine.campHealBoost = CampStoreUi.hasUpgrade(engine, "hearth") ? 2 : 1;
            try { engine.levelMods = settings.mods ? JsonSerializer.Deserialize<Dictionary<int, LevelMod>>(localStorage.getItem("lol.mods") ?? "null", Store.json) ?? new Dictionary<int, LevelMod>() : new Dictionary<int, LevelMod>(); } catch (Exception) { engine.levelMods = new Dictionary<int, LevelMod>(); }
            engine.randomizer = settings.randomizer ? new RandomizerConfig { seed = string.IsNullOrEmpty(settings.seed) ? "lore" : settings.seed, ngplus = jsNumberOr(settings.ngplus, 0) } : null;
            if (packsOn) engine.sceneUpdateRequired = true;
            if (!packsOn && hdCanvas != null) hdCanvas.SetHidden(true);
            updateTexturedBuild();
        }

        // frames per second, averaged over half a second, in the top-left corner over everything
        Label fpsLabel;
        int fpsFrames;
        float fpsSince;
        void showFps(bool on)
        {
            if (fpsLabel == null)
            {
                if (!on) return;
                fpsLabel = new Label { pickingMode = PickingMode.Ignore };
                var st = fpsLabel.style;
                st.position = Position.Absolute; st.left = 4; st.top = 4; st.paddingLeft = st.paddingRight = 4;
                st.backgroundColor = new Color(0, 0, 0, .6f); st.color = new Color(.5f, 1f, .5f); st.fontSize = 12;
                fpsLabel.schedule.Execute(() =>
                {
                    float now = Time.realtimeSinceStartup;
                    fpsLabel.text = $"{Mathf.RoundToInt((Time.frameCount - fpsFrames) / Mathf.Max(0.001f, now - fpsSince))} FPS";
                    fpsFrames = Time.frameCount; fpsSince = now;
                    fpsLabel.BringToFront();
                }).Every(500);
                Dom.document.Add(fpsLabel);
            }
            fpsLabel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Number(s) || fallback, truncated to an int.</summary>
        static int jsNumberOr(string s, int fallback) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d != 0 && !double.IsNaN(d) ? (int)d : fallback;

        static string jsString(object v) => v is bool b ? (b ? "true" : "false") : v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString() ?? "undefined";

        void init_E10()
        {
            foreach (var entry in settingInputs)
            {
                string name = entry.Key; var input = entry.Value;
                if (input == null) continue;
                bool checkbox = input.GetAttr("type") == "checkbox";
                if (checkbox) ((DomInput)input).@checked = (bool)settings.Get(name);
                else if (input is DomSelect sel) sel.value = jsString(settings.Get(name));
                else ((DomInput)input).value = jsString(settings.Get(name));
                input.On("change", () => { settings.Set(name, checkbox ? (object)((DomInput)input).@checked : input is DomSelect s2 ? s2.value : ((DomInput)input).value); applySettings(); });
            }
            applySettings();
            renderHunt(); // after the settings exist: it reads settings.craft
            renderKeyBindings();
            on("#minimap-in", _ => minimap.setRadius(minimap.radius - 1)); // fewer cells = closer
            on("#minimap-out", _ => minimap.setRadius(minimap.radius + 1));
            minimap.onRadius = (radius) => { settings.minimapRadius = radius; try { localStorage.setItem(SETTINGS_KEY, Store.stringify(settings)); } catch (Exception) { /* ignore */ } };
        }

        void toggleInventory(bool? show = null)
        {
            if (inventoryOverlay == null) return;
            bool open = show ?? inventoryOverlay.IsHidden();
            if (open && !playing) return;
            inventoryOverlay.SetHidden(!open);
            if (!open) inventoryMode = "";
            updateInventoryMode();
            if (open) fitInventory();
        }

        // the page keeps the bottom 12vh free for the hotbar; here the hotbar can sit higher than that, so the
        // window ends just above wherever the hotbar is
        bool inventoryFitHooked;
        void fitInventory()
        {
            var bar = gameUi?.bar;
            if (bar == null || inventoryOverlay.parent == null) return;
            if (!inventoryFitHooked) { inventoryFitHooked = true; bar.RegisterCallback<GeometryChangedEvent>(_ => fitInventory()); inventoryOverlay.parent.RegisterCallback<GeometryChangedEvent>(_ => fitInventory()); }
            if (inventoryOverlay.IsHidden() || bar.worldBound.height <= 0) return;
            float bottom = Mathf.Max(120f, inventoryOverlay.parent.worldBound.yMax - bar.worldBound.yMin + 8f);
            if (Mathf.Abs(inventoryOverlay.style.bottom.value.value - bottom) > 0.5f) inventoryOverlay.style.bottom = bottom;
        }

        VisualElement inventoryOpen;
        VisualElement inventoryClose;
        VisualElement facingEl;
        VisualElement oilEl;
        VisualElement lanternNote;
        string lanternKey = "";

        void init_E11()
        {
            inventoryOpen = Q("#inventory-open");
            inventoryClose = Q("#inventory-close");
            if (inventoryOpen != null) inventoryOpen.On("click", () => toggleInventory(true));
            on("#stash", _ => { if (playing && engine != null) engine.uiStashHand(); });
            if (inventoryClose != null) inventoryClose.On("click", () => toggleInventory(false));
            if (handUse != null) handUse.On("click", () => { if (playing && engine != null) engine.queueAsync(() => engine.clickedPortraitEtcRight(new Lol.Button { arg = engine.selectedCharacter })); handUse.BlurEl(); });
            if (handDrop != null) handDrop.On("click", () => { if (playing && engine != null) engine.queueAsync(() => engine.clickedSceneDropItem(new Lol.Button { arg = 3 })); handDrop.BlurEl(); });

            facingEl = Q("#facing");
            oilEl = Q("#oil-level");
            lanternNote = Q("#lantern-note");
        }

        void updateLanternPanel()
        {
            bool hasLamp = (engine.flagsTable[31] & 0x08) != 0;
            // the compass is in the key too: picking it up changes nothing else, and the panel kept "(no compass)"
            bool hasCompass = (engine.flagsTable[31] & 0x40) != 0;
            string key = $"{engine.currentDirection}|{(hasLamp ? "true" : "false")}|{engine.lampOilStatus}|{(engine.lampSwitchedOff ? "true" : "false")}|{hasCompass}";
            if (key == lanternKey) return;
            lanternKey = key;
            if (facingEl != null) facingEl.SetText((hasCompass ? "" : "(no compass) ") + directions[engine.currentDirection]);
            var rose = Q("#compass-rose");
            var compass = Q("#compass");
            if (rose != null) rose.SetAttr("transform", $"rotate({-engine.currentDirection * 90} 50 50)"); // facing direction on top
            if (compass != null) compass.ClassToggle("missing", !hasCompass);
            var lantern = Q("#lantern");
            var flame = Q("#lantern-flame");
            if (lantern != null) { lantern.ClassToggle("missing", !hasLamp); lantern.ClassToggle("off", engine.lampSwitchedOff || engine.lampOilStatus <= 0); }
            if (flame != null) { double k = hasLamp ? 0.4 + 0.6 * Math.Min(1, Math.Max(0, engine.lampOilStatus / 100.0)) : 0.4; flame.SetStyle("transform", $"scale({num(k)})"); flame.SetStyle("opacity", engine.lampSwitchedOff || (hasLamp && engine.lampOilStatus <= 0) ? "0" : "1"); } // .lantern.off #lantern-flame: USS does not reach svg children
            if (oilEl != null)
            {
                oilEl.SetStyle("width", $"{(hasLamp ? Math.Min(100, engine.lampOilStatus) : 0)}%");
                oilEl.ClassToggle("off", engine.lampSwitchedOff);
            }
            if (lanternNote != null) lanternNote.SetText(!hasLamp ? "No lantern yet." : engine.lampSwitchedOff ? "Lantern is off." : engine.lampOilStatus <= 0 ? "The lantern is out of oil." : $"Oil: {Math.Min(100, engine.lampOilStatus)}%");
            foreach (var id in new[] { "#lantern-toggle", "#lantern-refill" }) { var b = Q(id); if (b != null) b.SetDisabled(!hasLamp); }
        }

        int openSpellSlider = -1; // the one spell whose power slider is unfolded, or -1
        // Which damage class each spell deals, so the bar can say what the thing in front of you dislikes.
        // (3 ice, 4 fire, 5 lightning, 6 acid, 7 magic - the same table monsterInfo describes.)
        static readonly Dictionary<int, int> SPELL_DAMAGE_CLASS = new Dictionary<int, int> { [0] = 5, [2] = 3, [3] = 4, [4] = 7, [5] = 7, [6] = 5, [8] = 6, [9] = 6 };

        FacingWeakness facingWeakness()
        {
            if (engine == null) return null;
            var m = engine.uiMonsterAhead();
            if (m == null) return null;
            string name = engine.monsterName(m);
            if (!stats.bestiary.TryGetValue(name, out var known) || known == null || known.kills == 0) return null; // you have to have killed one to know its hide
            var info = engine.monsterInfo(m);
            return info != null ? new FacingWeakness { name = name, taken = info.damageTaken } : null;
        }

        VisualElement spellPop;

        void updateSidebar()
        {
            if (spellbar == null) return;
            updateInventoryPanel();
            updateLanternPanel();
            var c = engine.characters[engine.selectedCharacter] ?? engine.characters[0];
            var spells = engine.availableSpells.Select(s => (int)s).ToArray(); // every slot, including the eighth the original scroll has no room for
            // what mana and health decide here is how high each spell can be cast (and the skull at all): that, not the
            // exact points, is the key - mana ticking back rebuilt the list, an open power slider in a drag with it
            var affords = string.Concat(spells.Where(sp => sp != -1).Select(sp =>
            {
                var pr = engine.@static.SpellProperties[sp];
                int m = -1;
                for (int level = 0; level < 4; level += 1) if (pr.mpRequired[level] <= c.magicPointsCur && pr.hpRequired[level] < c.hitPointsCur) m = level;
                return (char)('1' + m);
            })) + (engine.@static.SpellProperties[8] != null && c.magicPointsCur >= engine.@static.SpellProperties[8].mpRequired[3] ? "s" : "");
            string key = $"{string.Join(",", spells)}|{engine.selectedSpell}|{affords}|{engine.flagsTable[31] & 0x10}|{(minimap.canvas.IsHidden() ? "true" : "false")}|{quickSpell.slot}:{quickSpell.level}|{Store.stringify(spellPower)}|{engine.selectedCharacter}|{Store.stringify(quickSpells)}|{engine.uiCountOfProperty(GREEN_SKULL)}|{openSpellSlider}|{facingWeakness()?.name ?? ""}|{JsonSerializer.Serialize(hiddenSpells)}";
            if (key == sidebarKey) return;
            sidebarKey = key;
            if (minimapNote != null) minimapNote.SetHidden(!minimap.canvas.IsHidden());
            var facing = facingWeakness();
            spellbar.ReplaceChildren();
            spellbar.EnableInClassList("spell-icons", hudOn);
            var shownSlots = hudOn ? panelSpells(c.id) : null;   // the in-game interface: the hero's chosen five
            VisualElement powerPop = null;
            int count = 0;
            for (int slotIndex = 0; slotIndex < spells.Length; slotIndex += 1)
            {
                int spell = spells[slotIndex], slot = slotIndex;
                if (spell == -1 || (shownSlots != null && !shownSlots.Contains(slot))) continue;
                count += 1;
                var props = engine.@static.SpellProperties[spell];
                var extra = engine.extraSpellAt(spell);
                object art = extra != null ? (object)extra.icon : spell;
                var row = Dom.El("div");
                row.SetClassName($"spell{(slot == engine.selectedSpell ? " selected" : "")}{(slot == quickSpell.slot ? " quick" : "")}");
                string spellName = engine.spellName(spell);
                // affordable powers for the selected hero
                int max = -1;
                for (int level = 0; level < 4; level += 1) if (props.mpRequired[level] <= c.magicPointsCur && props.hpRequired[level] < c.hitPointsCur) max = level;
                var cast = Dom.El("button");
                cast.SetAttr("type", "button");
                cast.SetClassName("spell-cast");
                cast.Append(SpellWidget.spellIcon(art, hudOn ? 30 : 22));
                var name = Dom.El("span");
                name.SetClassName("spell-name");
                name.SetText(spellName);
                cast.Append(name);
                cast.SetDisabled(max < 0);
                int current = Math.Min(powerFor(slot), Math.Max(0, max));
                cast.SetTitle($"Cast {spellName} at power {current + 1} (MP {props.mpRequired[current]}). Right-click for the power slider.");
                cast.On("click", () =>
                {
                    if (!playing || engine == null) return;
                    int level = Math.Min(powerFor(slot), Math.Max(0, max));
                    engine.queueAsync(() => engine.quickCastSpell(engine.selectedCharacter, slot, level));
                    cast.BlurEl();
                });
                // The power lives in a badge on the row; the slider only unfolds for the spell you ask about,
                // so a full spell book still fits on screen.
                var power = mkEl("span", "spell-power", (current + 1).ToString(CultureInfo.InvariantCulture));
                power.SetTitle($"Power {current + 1} of 4 - click to change");
                if (hudOn)
                {
                    // Unity build, the in-game interface: an icon square; four pips under it - lit: the power it
                    // casts at, dim: powers the hero can afford, dark: out of reach
                    power.SetText("");
                    power.AddToClassList("spell-pips");
                    for (int p = 0; p < 4; p += 1) power.Append(mkEl("i", "pip" + (p <= current ? " on" : p <= max ? " can" : "")));
                    cast.SetTitle($"{spellName}: power {current + 1} of 4, MP {props.mpRequired[current]}. Click to cast, right-click to choose the power.");
                }
                cast.Append(power);
                // what this creature makes of that kind of damage, once the party has killed one before
                if (facing != null)
                {
                    int? cls = extra != null ? 7 : SPELL_DAMAGE_CLASS.TryGetValue(spell, out var dc) ? dc : (int?)null;
                    var taken = cls != null && facing.taken != null && cls.Value < facing.taken.Count ? facing.taken[cls.Value] : null;
                    if (taken != null)
                    {
                        bool strong = taken.pct >= 125;
                        bool weak = taken.pct <= 75;
                        row.ClassToggle("weak-to", strong);
                        row.ClassToggle("resisted", weak);
                        if (strong || weak) cast.SetTitle(cast.GetTitle() + $"\n{facing.name} takes {taken.pct}% from {taken.kind}.");
                    }
                }
                row.Append(cast);
                void openSlider() { openSpellSlider = openSpellSlider == slot ? -1 : slot; sidebarKey = ""; updateSidebar(); }
                power.On("click", (ev) => { ev.preventDefault(); ev.stopPropagation(); openSlider(); });
                row.On("contextmenu", (ev) => { ev.preventDefault(); openSlider(); });
                if (openSpellSlider == slot)
                {
                    var slider = SpellWidget.createSpellSlider(spell: art, level: current, max: max, title: $"{spellName} power: MP {string.Join(" / ", props.mpRequired)}", onChange: (level) =>
                    {
                        rememberSpellPower(slot, level);
                        if (hudOn) { openSpellSlider = -1; sidebarKey = ""; updateSidebar(); }   // picked: the picker closes
                    });
                    if (hudOn)
                    {
                        // under the icons, across the panel: the spell's name, the power picker, the mana each power costs
                        powerPop = mkEl("div", "spell-pop");
                        powerPop.Append(mkEl("span", "spell-pop-name", $"{spellName}: MP {string.Join(" / ", props.mpRequired)}"), slider.root);
                    }
                    else row.Append(slider.root);
                }
                row.SetDraggable(true);
                row.SetTitle("Drag onto a hotbar slot to cast it from there (at the last power used)");
                row.On("dragstart", (ev) => { ev.dataTransfer.setData("text/x-spell", $"{slot}:{powerFor(slot)}"); ev.dataTransfer.effectAllowed = "link"; });
                spellbar.Append(row);
            }
            // The Green Skull is a thrown poison bomb: it gets a spell row of its own while the party carries
            // one. Its power is fixed, so the slider is drawn at full and takes no input.
            int skulls = engine.uiCountOfProperty(GREEN_SKULL);
            if (skulls != 0)
            {
                count += 1;
                var row = Dom.El("div");
                row.SetClassName("spell spell-item");
                var cast = Dom.El("button");
                cast.SetAttr("type", "button");
                cast.SetClassName("spell-cast");
                cast.Append(SpellWidget.spellIcon(5, hudOn ? 30 : 22)); // Mist of Doom: the skull
                var name = Dom.El("span");
                name.SetClassName("spell-name");
                name.SetText("Poison Cloud");
                cast.Append(name);
                var skullSpell = engine.@static.SpellProperties[8];
                int skullMp = skullSpell != null ? skullSpell.mpRequired[3] : 0;
                cast.SetDisabled(c == null || c.magicPointsCur < skullMp);
                cast.SetTitle($"The Green Skull: a cloud of poison ahead, always at full strength (MP {skullMp}). The skull is not used up.");
                var skullPower = mkEl("span", "spell-power fixed", "4");
                skullPower.SetTitle("Always cast at full strength");
                if (hudOn)
                {
                    skullPower.SetText("");
                    skullPower.AddToClassList("spell-pips");
                    for (int p = 0; p < 4; p += 1) skullPower.Append(mkEl("i", "pip on"));
                    cast.SetTitle("Poison Cloud (the Green Skull): always full strength, MP " + skullMp + ". The skull is not used up.");
                }
                cast.Append(skullPower);
                cast.On("click", () =>
                {
                    if (!playing || engine == null) return;
                    int slot = engine.uiInventorySlotOfProperty(GREEN_SKULL);
                    if (slot >= 0) engine.uiCastFromItem(slot, engine.selectedCharacter);
                    cast.BlurEl();
                });
                row.Append(cast);
                spellbar.Append(row);
            }
            // the picker floats over the bottom of the Spells box (in the list it scrolled out of sight)
            spellPop?.RemoveFromHierarchy();
            spellPop = powerPop;
            if (powerPop != null) spellbar.parent.Append(powerPop);
            if (spellbarNote != null) spellbarNote.SetHidden(count > 0);
            if (quickSpellName != null) quickSpellName.SetText(quickSpellLabel());
        }

        string cssCursorKey = "";

        // screen.canvas.style.cursor = `url(picture) hot, auto` / "default" (Dom/CssCursor.cs shows it over the canvas)
        void setCanvasCursor(Texture2D texture, Vector2 hot)
        {
            if (texture != null) CssCursor.SetInlinePicture(screen.canvas, texture, hot);
            else screen.canvas.SetStyle("cursor", "default");
        }

        void updateCssCursor()
        {
            var shape = engine.cursorShape;
            string key = (shape != null ? $"{shape.key ?? ""}:{shape.width}x{shape.height}:{engine.cursorHotX},{engine.cursorHotY}:{engine.itemInHand}:{engine.flagsTable[31] & 0x02}" : "") + ":" + sceneCursor;
            if (key == cssCursorKey) return;
            cssCursorKey = key;
            bool arrow = shape == null || (engine.itemInHand == 0 && (engine.flagsTable[31] & 0x02) == 0); // plain pointer: nothing in hand, no special icon
            if (arrow) { CssCursor.SetInline(screen.canvas, sceneCursor); return; }   // Unity build: attack / grab / default
            int scale = 2;
            int cw = shape.width * scale, ch = shape.height * scale;
            // an item the remake added is held as its own picture (the engine's shape is a borrowed original sprite)
            var art = engine.itemInHand != 0 ? engine.getItemIconShapePtr(engine.itemInHand)?.art : null;
            var artTex = art != null ? UnityEngine.Resources.Load<Texture2D>("Art/" + System.IO.Path.GetFileNameWithoutExtension(art)) : null;
            if (artTex != null)
            {
                int size = Math.Max(cw, ch);
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
                var was = RenderTexture.active;
                Graphics.Blit(artTex, rt);
                RenderTexture.active = rt;
                var held = new Texture2D(size, size, TextureFormat.RGBA32, false);
                held.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                held.Apply();
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
                setCanvasCursor(held, new Vector2(size / 2f, size / 2f));
                return;
            }
            var palette = engine.uiPalette();
            // the small canvas drawn at 2x without smoothing: each pixel becomes a 2x2 block (Texture2D rows go bottom-up)
            var pixels = new Color32[cw * ch];
            for (int i = 0; i < shape.pixels.Length; i += 1)
            {
                int raw = shape.pixels[i];
                if (raw == 0) continue;
                int color = (shape.colorTable != null ? shape.colorTable[raw] : raw) * 3;
                var c32 = new Color32((byte)Math.Round(palette[color] * 255 / 63.0), (byte)Math.Round(palette[color + 1] * 255 / 63.0), (byte)Math.Round(palette[color + 2] * 255 / 63.0), 255);
                int sx = i % shape.width, sy = i / shape.width;
                for (int dy = 0; dy < scale; dy += 1)
                    for (int dx = 0; dx < scale; dx += 1)
                        pixels[(ch - 1 - (sy * scale + dy)) * cw + sx * scale + dx] = c32;
            }
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(pixels);
            tex.Apply();
            setCanvasCursor(tex, new Vector2(engine.cursorHotX * scale, engine.cursorHotY * scale));
        }

        void drawCursor(byte[] frame, Shape shape, int x, int y)
        {
            for (int sy = 0; sy < shape.height; sy += 1)
            {
                int ty = y + sy;
                if (ty < 0 || ty >= 200) continue;
                for (int sx = 0; sx < shape.width; sx += 1)
                {
                    int tx = x + sx;
                    if (tx < 0 || tx >= 320) continue;
                    int raw = shape.pixels[sy * shape.width + sx];
                    if (raw == 0) continue;
                    frame[ty * 320 + tx] = shape.colorTable != null ? shape.colorTable[raw] : (byte)raw;
                }
            }
        }

        int[] canvasPosition(DomEvent @event)
        {
            var rect = screen.canvas.worldBound;
            int x = (int)Math.Floor(((@event.clientX - rect.xMin) / rect.width) * screen.canvas.width);
            int y = (int)Math.Floor(((@event.clientY - rect.yMin) / rect.height) * screen.canvas.height);
            return compactActive ? new[] { x + SCENE[0], y + SCENE[1] } : new[] { x, y };
        }

        // Playtest from the level designer: /?level=N&block=B&dir=D&mods=1 starts a new game there.
        bool playtestFromUrl()
        {
            // URL query parameters do not exist in this host: never a playtest.
            return false;
        }

        void enterPlayfield()
        {
            if (selectedCharacter < 0) return;
            int character = selectedCharacter;
            // A new game is a new campaign: the journal, the bestiary, the people met, the chest, the imp's
            // errand and how deep the pit has been beaten all start over. Only the browser's own preferences
            // (settings, filter, level mods) are left alone.
            Saves.clearSide();
            reloadBrowserState();
            _ = runGame((game) => { game.playIntro = settings.intro != false; return game.playNewGame(character); });
        }

        async Task runGame(Func<LandsOfLore, Task<string>> start)
        {
            stopAnimation();
            clearControls();
            playing = true;
            screen.canvas.AddToClassList("in-game");
            // Every load builds a fresh engine, so the one being replaced has to hand back its share of the
            // page's audio first: otherwise its music keeps playing under the new game's and each load piles
            // another voice on top.
            if (engine != null) { try { engine.snd_dispose(); } catch (Exception error) { log($"audio teardown: {error.Message}"); } }
            var resources = new Lol.Resources(dataRoot, fetchBytes);
            engine = new LandsOfLore(resources, sched, log, presentEngine); engine.repairOnLoad = true; engine.companionsBench = true;
            engine.audioHost = audio;
            // window.lolEngine / lolFullMap / lolGameUi / lolCineOps: debugging handles, skipped.
            engine.screen.onWsaFrame = (wsa, frame, page, x, y) => { var w = (WsaPlayer)wsa; if (!string.IsNullOrEmpty(w.name) && (page == 0 || page == 2)) cineOps[w.name] = new CineOp { x = x, y = y, w = w.width, h = w.height, page = page }; };
            engine.uiCraftLoad(JsonSerializer.SerializeToNode(CraftingUi.readPouch(), Store.json) as JsonObject);
            CampStoreUi.loadStore(engine);
            DungeonRun.loadRun(engine);
            engine.metaLoad(new JsonObject
            {
                ["stats"] = stats.ToJson(), ["bestiary"] = stats.ToJson()["bestiary"]?.DeepClone(),
                ["visited"] = JsonSerializer.SerializeToNode(visited, Store.json), ["itemDb"] = JsonSerializer.SerializeToNode(itemDb, Store.json), ["notes"] = JsonSerializer.SerializeToNode(notes, Store.json),
                ["npcs"] = JsonSerializer.SerializeToNode(npcs.list().ToDictionary(n => n.name, n => (object)n.talks), Store.json),
                ["unlocked"] = JsonSerializer.SerializeToNode(achUnlocked, Store.json),
            });
            // stats = engine.meta.stats; Object.defineProperty(stats, "bestiary", { get: () => engine.meta.bestiary });
            stats = WebStats.OfEngine(engine, stats);
            visited = engine.meta.visited;
            itemDb = engine.meta.itemDb;
            notes = engine.meta.notes;
            gameUi.reset();
            engine.ui = (name, args) =>
            {
                switch (name)
                {
                    case "message": { var t = (string)Arg(args, 0); var k = Arg(args, 1); gameUi.message(t, k); journalAdd(t, "journal-msg", k); break; }   // k: a kind name or printMessage's colour index
                    case "dialogue": { var t = (string)Arg(args, 0); gameUi.dialogue(t); journalAdd(t, "journal-say"); break; }
                    case "choices": gameUi.choices((string[])Arg(args, 0)); break;
                    case "waiting": gameUi.waitingFor((bool)Arg(args, 0), (string)Arg(args, 1)); break;
                    case "miss": combatMiss(Arg(args, 0)); break;
                    case "damage": combatDamage(Arg(args, 0)); break;
                    case "kill": combatKill(Arg(args, 0)); break;
                    case "npc": npcStarted(Arg(args, 0)); break;
                    case "target": { bool on = (bool)Arg(args, 0); gameUi.targetMode(on); if (on) toast("Cast on whom? Click a hero", ""); break; }
                    case "camp": openCampSheet((string)Arg(args, 0)); break;
                }
            };
            if (gameUiRoot != null) gameUiRoot.SetHidden(false);
            // engine.clickedOptions = async () => { toggleModal("#menu-overlay", true); return 1; }: C# cannot
            // replace the method, so the Options button's callback (88, the only caller) is replaced.
            engine.buttonCallbacks[88] = async (b) => { toggleModal("#menu-overlay", true); return 1; }; // host menu instead of the original
            engine.deathHook = showDeathScreen;
            // Trade helper: whenever a shop script prices something, say what it is worth.
            int priceOp = -1;
            for (int i = 0; i < engine.opcodes.Length; i += 1) if (engine.opcodes[i] != null && i < LandsOfLore.OPCODE_NAMES.Length && LandsOfLore.OPCODE_NAMES[i] == "getItemPrice") { priceOp = i; break; }
            if (priceOp >= 0)
            {
                var orig = engine.opcodes[priceOp];
                engine.opcodes[priceOp] = async (s) =>
                {
                    int value = s.arg(0);
                    int price = await orig(s);
                    string item = engine.itemInHand != 0 ? engine.itemName(engine.itemInHand) : "";
                    gameUi.trade = new GameUi.TradeInfo { price = price, selling = value < 0 || engine.itemInHand != 0, item = engine.itemInHand != 0 ? engine.itemInHand : engine.lastPickedItem, type = engine.itemInHand != 0 ? 0 : engine.lastAskedType, at = now() };
                    engine.lastAskedType = 0; engine.lastPickedItem = 0;
                    gameUi.message(value < 0 ? $"Trade: {(!string.IsNullOrEmpty(item) ? $"{item} " : "")}sells for {price} crowns (you have {engine.credits})." : $"Trade: {(!string.IsNullOrEmpty(item) ? $"{item} " : "")}costs {price} crowns (you have {engine.credits}).", "combat");
                    bump("trades");
                    return price;
                };
            }
            // achievement counters on engine actions (wrapCount: the engine calls onCall first, with the arguments)
            engine.onCall = (name, args) =>
            {
                switch (name)
                {
                    case "castSpell":
                    {
                        int type = Convert.ToInt32(args[1]); int level = Convert.ToInt32(args[2]);
                        bump("spells"); if (type == 1) bump("spellHeal"); if (type == 3) bump("spellFire"); if (type == 0 || type == 6) bump("spellBolt"); if (type == 2) bump("spellIce"); if (level >= 3) bump("power4");
                        break;
                    }
                    case "moveParty": bump("steps"); break;
                    case "clickedScenePickupItem": bump("loot"); break;
                    case "uiTakeFloorItem": bump("loot"); break;
                    case "showOutro": bump("finished"); break;
                }
            };
            applySettings();
            // URL parameters ?nospeech / ?nosound: command-line --nospeech / --nosound here.
            var cmd = System.Environment.GetCommandLineArgs();
            if (Array.IndexOf(cmd, "--nospeech") >= 0) engine.speechEnabledFlag = false;
            if (Array.IndexOf(cmd, "--nosound") >= 0) engine.audioHost = null;
            engine.openTalkArchive = async (name) =>
            {
                try
                {
                    return new PakTalkArchive(new PakArchive(await fetchBytes($"{dataRoot}{name}")));
                }
                catch (Exception error)
                {
                    log($"Speech archive {name} unavailable: {error.Message}");
                    return null;
                }
            };
            // The potions are extra entries in the item table: they have to exist before the engine touches
            // a saved bag, which happens well before the first status frame.
            engine.onStartup = () =>
            {
                try { CraftingUi.registerPotions(engine); } catch (Exception error) { log($"Crafted items: {error.Message}"); }
                try { engine.uiRegisterExtraSpells(); } catch (Exception error) { log($"Extra spells: {error.Message}"); }
            };
            engine.saveHook = () => saveToSlot("quick", true);
            engine.loadHook = () => { loadFromSlot("quick"); };
            lastStatus = "";
            setStatus("Loading level 1");
            try
            {
                if (teleportForm != null) teleportForm.Q("button").SetDisabled(false);
                string result;
                try { result = await start(engine); }
                catch (Exception error) when (error.Message == "quit") { result = "quit"; }
                log($"Engine stopped: {result}");
                // the stopped game's last picture must not be shown over the title drawn next (its present was
                // still due: the title came up under the old game's screen, its palette left behind)
                presentDue = false;
                foreach (var bar in monsterBars.Values) bar.RemoveFromHierarchy();   // and its monsters' bars
                monsterBars.Clear();
                if (teleportForm != null) teleportForm.Q("button").SetDisabled(true);
                toggleInventory(false);
                playing = false;
                compactActive = false;
                setFrameSize(320, 200);
                drawDocks(null, null, false);
                if (gameUiRoot != null) gameUiRoot.SetHidden(true);
                screen.canvas.RemoveFromClassList("in-game");
                minimap.canvas.SetHidden(true);
                if (result == "restart" || (result == "quit" && engine.gameFinished)) showTitle();
            }
            catch (Exception error)
            {
                // console.error(error)
                log($"ERROR: {error.Message}");
                setStatus("Engine error", true);
                status.SetTitle($"{error.Message}\n{string.Join("\n", (error.StackTrace ?? "").Split('\n').Take(4))}");
                gameUi.message($"Engine error: {error.Message}", "system");
                toast($"Engine error: {error.Message}", "fx-toast danger");
            }
        }

        /// <summary>indexed-screen.mjs drawFallback() (IndexedScreen.cs does not have it).</summary>
        static void drawFallback(IndexedScreen s)
        {
            int width = s.canvas.width, height = s.canvas.height;
            var pixels = new byte[width * height];
            var palette = new byte[768];
            new byte[] { 8, 11, 12, 189, 166, 99, 191, 106, 58, 80, 96, 75 }.CopyTo(palette, 0);
            for (int y = 0; y < height; y += 1)
            {
                for (int x = 0; x < width; x += 1)
                {
                    bool border = x < 4 || y < 4 || x >= width - 4 || y >= height - 4;
                    pixels[y * width + x] = (byte)(border ? 1 : ((x + y) % 31 == 0 ? 3 : 0));
                }
            }
            s.draw(pixels, palette);
        }

        public async Task start()
        {
            stopAnimation();
            playing = false;
            clearControls();
            logElement.SetText("");
            drawFallback(screen);
            setStatus("Reading private assets");

            try
            {
                // fetch("/private/game/import-manifest.json"): the import writes it beside the DATA folder.
                string manifestPath = Path.Combine(Path.GetDirectoryName(dataFolder.TrimEnd('/', '\\')) ?? "", "import-manifest.json");
                if (!File.Exists(manifestPath)) throw new Exception("Private assets have not been imported");
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
                string manifestRoot = (string)manifest["dataRoot"];
                string root = $"/private/game/{(!string.IsNullOrEmpty(manifestRoot) ? $"{manifestRoot}/" : "")}"; // fetchBytes maps it onto the data folder
                log($"Volume: {(string)manifest["volumeId"]}");
                log($"Imported files: {(manifest["files"] as JsonArray)?.Count ?? 0}");
                log($"Game root: {(!string.IsNullOrEmpty(manifestRoot) ? manifestRoot : ".")}");

                dataRoot = root;
                var loads = await Task.WhenAll(loadArchive($"{root}STARTUP.PAK"), loadArchive($"{root}ENG/INTRO9.PAK"));
                var startup = loads[0]; var intro = loads[1];
                assets = new PageAssets
                {
                    title = Cps.decodeCps(startup.get("TITLE.CPS")),
                    characterScreen = Cps.decodeCps(intro.get("CHAR.CPS")),
                    portraitAtlas = Cps.decodeCps(intro.get("BACKGRND.CPS")),
                    king = new WsaMovie(intro.get("CHARGEN.WSA")),
                };
                log("Decoded title, character screen and portrait atlas");
                log($"Decoded CHARGEN.WSA: {assets.king.frameCount} frames, {assets.king.width}x{assets.king.height}");
                showTitle();
                playtestFromUrl();
            }
            catch (Exception error)
            {
                // console.error(error)
                log($"ERROR: {error.Message}");
                log("Run: make import");
                setStatus("Asset load failed", true);
            }
        }

        double lastAutosaveAt;

        void init_E12()
        {
            reload.On("click", () => { _ = start(); });
            if (teleportForm != null)
            {
                teleportForm.On("submit", (ev) =>
                {
                    ev.preventDefault();
                    if (!playing || engine == null) return;
                    int level = (int)((DomInput)Q("#teleport-level")).valueAsNumber;
                    string blockValue = ((DomInput)Q("#teleport-block")).value;
                    int? block = blockValue == "" ? (int?)null : (int)double.Parse(blockValue, CultureInfo.InvariantCulture);
                    var modStart = block == null && engine.levelMods != null && engine.levelMods.TryGetValue(level, out var mod) && mod != null ? mod.start : null;
                    engine.queueAsync(() => engine.debugTeleport(level, modStart != null ? modStart.block : block, modStart != null ? modStart.dir : 0));
                    screen.canvas.FocusEl();
                });
                // The DOM layer has no form submission: the form's button submits it.
                var submit = teleportForm.Q("button");
                if (submit != null) submit.On("click", () => teleportForm.Fire("submit", new DomEvent { target = teleportForm }));
            }
            // window.addEventListener("pagehide", ...): see pagehide().
            lastAutosaveAt = now();
            timers.setInterval(() => { double minutes = double.TryParse(settings.autosave, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : double.NaN; if (minutes > 0 && playing && engine != null && now() - lastAutosaveAt >= minutes * 60000 && saveToSlot("auto", true)) lastAutosaveAt = now(); }, 30000);
            // document.addEventListener("keydown", ...): see documentKeydown.
            screen.canvas.On("contextmenu", (ev) => ev.preventDefault());
            screen.canvas.On("mousemove", (ev) =>
            {
                if (!playing || engine == null) return;
                var pos = canvasPosition(ev);
                if (pos == null) return;
                int x = pos[0], y = pos[1];
                engine.pushMouse(x, y);
                sceneHover(x, y);
            });
            screen.canvas.On("mousedown", (ev) =>
            {
                if (!playing || engine == null) return;
                ev.preventDefault();
                var pos = canvasPosition(ev);
                if (pos == null) return;
                int x = pos[0], y = pos[1];
                if (sceneClickTaken = sceneClick(x, y, ev.button)) return;   // Unity build: click-to-attack
                engine.pushMouse(x, y, ev.button == 2 ? 2 : 1);
            });
            screen.canvas.On("mouseup", (ev) =>
            {
                if (!playing || engine == null) return;
                var pos = canvasPosition(ev);
                if (pos == null) return;
                int x = pos[0], y = pos[1];
                engine.pushMouse(x, y);
                if (sceneClickTaken) { sceneClickTaken = false; return; }
                engine.events.Add(new Lol.InputEvent { type = "mouseup", x = x, y = y, button = ev.button == 2 ? 2 : 1 });
            });
            // start(): HostMain calls it once Init() has run.
        }

        /// <summary>window.addEventListener("pagehide", ...)</summary>
        public void pagehide() => saveToSlot("auto", true);

        /// <summary>Navigating away (window.location.href): pagehide, then the page and its game stop.</summary>
        void leavePage()
        {
            if (playing) pagehide();
            playing = false;
            if (engine != null) { engine.snd_stopMusic(); engine.quit = true; }
            audio.AdlibPost(new Lol.AdlibMessage { type = "stopAll" });
        }

        /// <summary>The page's two document keydown listeners, in the order they were added.</summary>
        public void documentKeydown(DomEvent @event)
        {
            // document.addEventListener("keydown", (event) => { if (event.key === "F11" && !window.lolDesktop) { ... } });
            if (@event.key == "F11") { @event.preventDefault(); toggleFullScreen(); }

            if (!playing || engine == null) return;
            if (@event.target != null && (Dom.Data(@event.target).tag == "input" || Dom.Data(@event.target).tag == "textarea" || Dom.Data(@event.target).tag == "select")) return; // typing in a field is not a hotkey
            if (!Q("#death-overlay").IsHidden()) return; // the death screen must be answered
            if (@event.key == "Escape" && QAll(".modal").Any(m => !m.IsHidden())) { if (tradeOverlay != null && !tradeOverlay.IsHidden()) closeTrade(); foreach (var m in QAll(".modal")) m.SetHidden(true); charScreen.c = -1; return; }
            if ((@event.key == "p" || @event.key == "P") && compactActive) { if (charScreen.isOpen) charScreen.close(); else charScreen.open(engine.selectedCharacter); return; }
            if (@event.key == "Escape" && !Q("#camp-sheet").IsHidden()) { closeCampSheet(); return; }
            if (@event.key == "Escape" && inventoryOverlay != null && !inventoryOverlay.IsHidden()) { toggleInventory(false); return; }
            if (@event.key == "Escape" && compactActive)
            {
                // a special scene window (chest, shop counter...) that will not close: Esc clicks its exit button
                // (press and release on separate ticks); restoring directly leaves the view stale (updateFlags 3)
                if (engine.needSceneRestore != 0 && engine.tim.currentTim == null && !(gameUi.choiceLabels != null && gameUi.choiceLabels.Any())) { engine.pushMouse(276, 117, 1); timers.setTimeout(() => engine.events.Add(new Lol.InputEvent { type = "mouseup", x = 276, y = 117, button = 1 }), 60); return; }
                if (!engine.skipCutscene()) toggleModal("#menu-overlay", true); return;
            }
            if (@event.key.Length == 1 && @event.key[0] >= '0' && @event.key[0] <= '9' && compactActive && !@event.ctrlKey && !@event.altKey)
            {
                gameUi.slotUse((@event.key[0] - '0' + 9) % 10); // 1..0: hotbar
                return;
            }
            if (@event.key.Length == 1 && "!@#$".IndexOf(@event.key[0]) >= 0 && compactActive) { gameUi.pickHero("!@#$".IndexOf(@event.key[0])); return; } // Shift+1..4: select hero
            if (keyIs(@event, "attack")) { quickAttack(); return; }
            if (keyIs(@event, "cast")) { quickCast(); return; }
            if (keyIs(@event, "quicksave")) { @event.preventDefault(); saveToSlot("quick"); return; }
            if (keyIs(@event, "quickload")) { @event.preventDefault(); loadFromSlot("quick"); return; }
            if (keyIs(@event, "rest")) { click(restButton); return; }
            if (keyIs(@event, "explore")) { click(Q("#explore-next")); return; }
            if (keyIs(@event, "inventory"))
            {
                toggleInventory();
                return;
            }
            if (@event.key == "Escape" && inventoryOverlay != null && !inventoryOverlay.IsHidden())
            {
                toggleInventory(false);
                return;
            }
            if (keyIs(@event, "journal")) { toggleJournal(); return; }
            if (keyIs(@event, "map")) { openFullMap(); return; }
            if (keyIs(@event, "stash")) { engine.uiStashHand(); return; }
            if (keyIs(@event, "rotate")) { if (engine.uiRotateParty()) gameUi.partyKey = ""; return; }
            if (keyIs(@event, "trade")) { openTrade(); return; }
            if (keyIs(@event, "photo")) { @event.preventDefault(); photo(); return; }
            if (keyIs(@event, "minimap"))
            {
                minimap.visible = !minimap.visible;
                minimap.lastKey = "";
                return;
            }
            if (@event.key.StartsWith("Arrow") || @event.key == " " || @event.key == "Home" || @event.key == "PageUp" || @event.key == "End" || @event.key == "PageDown") @event.preventDefault();
            engine.events.Add(new Lol.InputEvent { type = "key", key = @event.key, shift = @event.shiftKey });
        }
    }
}
