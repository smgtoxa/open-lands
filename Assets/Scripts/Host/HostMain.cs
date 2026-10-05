// The Unity entry point: builds the page (index.html -> PageMarkup, styles.css -> page.uss), finds the
// game data, and runs the page's event loop every frame: the engine's scheduler is the browser's
// event loop, pumped with real time.
using System;
using System.IO;
using System.Linq;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class HostMain : MonoBehaviour
    {
        /// <summary>The page's panel (the in-game interface scales it with the window: Web.Hud.cs).</summary>
        public static PanelSettings Panel;

        public static HostMain Instance;
        UIDocument _doc;
        Scheduler _sched;
        Web _web;
        WebAudio _audio;
        double _t0;
        float _flushAt;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            // --novsync: frames as fast as they go (--perf then shows what a frame really costs)
            if (Environment.GetCommandLineArgs().Contains("--novsync")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
            _sched = new Scheduler();
            _sched.OnError = e => { if (!(e is QuitException)) Debug.LogException(e); };
            _audio = gameObject.AddComponent<WebAudio>();
            _audio.sched = _sched;
            _t0 = Time.realtimeSinceStartupAsDouble * 1000;
        }

        void Start()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            Panel = settings;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            settings.clearColor = true;
            settings.colorClearValue = new Color32(8, 11, 12, 255);
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            var root = _doc.rootVisualElement;
            root.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("page"));
            var overrides = UnityEngine.Resources.Load<StyleSheet>("page-overrides");
            if (overrides != null) root.styleSheets.Add(overrides);
            // the fantasy interface (Settings): its sheet, and its Cinzel headings (fonts USS cannot name for the layout)
            root.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("page-fantasy"));
            // the in-game interface over it (stone frames, the screen's layout: Host/Web.Hud.cs)
            root.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("page-hud"));
            foreach (var sel in new[] { ".st-hero-name", ".st-big", ".st-value", ".jl-level" })
                CssLayout.HostFonts.Add((".theme-fantasy " + sel, new[] { "res:Fonts/Cinzel-Bold", "Georgia" }));
            // its screen fills the window (the page's shell is sized from the window's height); its boxes scroll
            CssLayout.HostCalcs.Add((".theme-fantasy .shell", "width", ""));
            CssLayout.HostCalcs.Add((".theme-fantasy .shell", "min-width", ""));
            CssLayout.HostScrollable.Add(".theme-fantasy #quest-list");
            // the character screen's spell rows: one line (the page's three-column grid wrapped the power slider away)
            CssLayout.HostLayouts.Add(new CssLayout.Rule(".theme-fantasy .char-spell", "flex", "row", "nowrap", 6, 6, null, null, null, -1, null, null, null, "block", null, false, int.MinValue));
            // the stone buttons and fields win over the page's painted gradients
            foreach (var sel in new[] { ".tag-button", ".ibtn", ".tag-select", ".input-type-text", ".input-type-search", ".imp-row", ".craft-row", ".stash-row", ".trade-row", ".jl-chip", ".itemdb-chip", ".inv-page", ".imp-tab", ".tab" })
                CssLayout.HostBackgrounds.Add((".theme-fantasy " + sel, ""));
            // pixel fonts: a blackletter for titles and names, a pixel sans for buttons, tabs and labels; long text
            // (the log, the guide, tooltips) keeps the readable font
            foreach (var sel in new[] { ".tag-h1", ".ui-name", ".guide-title", ".sb-title", ".card-name", ".game-button" })
                CssLayout.HostFonts.Add((".theme-fantasy " + sel, new[] { "res:Fonts/Jacquard12-Regular", "Georgia" }));
            foreach (var sel in new[] { ".panel .tag-h2", ".modal-head .tag-h2", ".inventory-head .tag-h2", ".modal-box .tag-h3", ".st-title", ".sb-sub", ".sb-hrow > *", ".sb-item-name", ".sb-school", ".tag-button", ".ibtn", ".tab", ".ui-log-filter", ".ui-choice", ".status", ".ui-quick", ".ui-key", ".ui-count", ".mon-master" })
                CssLayout.HostFonts.Add((".theme-fantasy " + sel, new[] { "res:Fonts/PixelifySans", "Segoe UI" }));
            // the world map's names are lettered in Cinzel in either interface
            CssLayout.HostFonts.Add((".wm-name", new[] { "res:Fonts/Cinzel-Bold", "Georgia" }));
            root.AddToClassList("root");
            root.style.unityFontDefinition = CssLayout.OsFont(new[] { "Segoe UI", "Noto Sans", "Arial" });
            Dom.document = root;
            Dom.Invoke = a => _sched.Run(a);
            PageMarkup.Build(root);
            // one Menu button (the Actions panel had a second one, beside the one next to Journal and Settings)
            Dom.QAll(root, "#options").ToList().ForEach(b => b.RemoveFromHierarchy());
            // this build's name (the page is the web's, which names the original game)
            foreach (var sel in new[] { ".masthead .tag-h1", "#menu-overlay .modal-head .tag-h2" })
                foreach (var h in Dom.QAll(root, sel)) h.SetText("Open Lands");
            Dom.ApplyUppercase(root);
            MoveFromOldName();
            string data = FindData();
            // --storage FILE: a separate localStorage (test runs leave the player's saves alone)
            var args = Environment.GetCommandLineArgs();
            int tc = Array.IndexOf(args, "--texcache");
            if (tc >= 0 && tc + 1 < args.Length && int.TryParse(args[tc + 1], out var lim)) CssShadow.Limit = lim;
            int st = Array.IndexOf(args, "--storage");
            string storage = st >= 0 && st + 1 < args.Length ? args[st + 1] : Path.Combine(Application.persistentDataPath, "localStorage.json");
            _web = new Web(_sched, _audio, data, storage);
            _web.hdFolder = FindHd(data);
            _web.texturedFolder = Path.GetFullPath(Path.Combine(data, "..", "textured"));
            _web.campArtFolder = Path.Combine(Application.streamingAssetsPath, "camp");
            bool hasData = File.Exists(Path.Combine(data, "GENERAL.PAK"));
            _sched.Run(() =>
            {
                _web.Init();
                // desktop/main.js loadGame(): the game page with data, else the "Game data" page
                if (hasData) _sched.Observe(_web.start());
                else SetupPage.Show(_sched);
            });
            Autopilot.Attach(gameObject, _web);
            PlayerBot.Attach(gameObject, _web);
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root.focusable = true;
            root.Focus();
        }

        /// <summary>The game data: --data, the imported folder, GameData beside the build / project.</summary>
        /// <summary>The imported game (SetupPage): its manifest names the folder with the PAK files.</summary>
        static string ImportedData()
        {
            try
            {
                var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(SetupPage.DataDir, "import-manifest.json")));
                string root = (string)manifest["dataRoot"] ?? "";
                return Path.Combine(SetupPage.DataDir, root.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception) { return null; }
        }

        static string FindData()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--data");
            // --data is the game, whatever is (or is not) in it
            if (at >= 0 && at + 1 < args.Length) return args[at + 1];
            string beside = Path.GetDirectoryName(Application.dataPath);
            foreach (var c in new[]
            {
                at >= 0 && at + 1 < args.Length ? args[at + 1] : null,
                ImportedData(),
                Path.Combine(beside, "DATA"),
                Path.Combine(beside, "GameData", "DATA"),
                Path.GetFullPath(Path.Combine(beside, "..", "..", "GameData", "DATA")),
            })
                if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "GENERAL.PAK"))) return c;
            return Path.Combine(Application.persistentDataPath, "game", "DATA");
        }

        /// <summary>The build was called "Lands of Lore", and Unity keeps a product's files in a folder of its name:
        /// on the first start as "Open Lands" the saves and settings are copied over (the old folder is left as it was)
        /// and the imported game moves (300 MB, not doubled).</summary>
        static void MoveFromOldName()
        {
            try
            {
                string now = Application.persistentDataPath;
                string old = Path.Combine(Path.GetDirectoryName(now), "Lands of Lore");
                if (!Directory.Exists(old) || File.Exists(Path.Combine(now, "localStorage.json"))) return;
                Directory.CreateDirectory(now);
                foreach (var name in new[] { "localStorage.json", "saves.json" })
                    if (File.Exists(Path.Combine(old, name))) File.Copy(Path.Combine(old, name), Path.Combine(now, name));
                if (Directory.Exists(Path.Combine(old, "game")) && !Directory.Exists(Path.Combine(now, "game")))
                    Directory.Move(Path.Combine(old, "game"), Path.Combine(now, "game"));
                Debug.Log($"Moved the saves from {old}");
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The HD assets (the browser's private/hd/): --hd, else GameData/hd beside DATA, else none.</summary>
        static string FindHd(string data)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--hd");
            string c = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(data, "..", "hd"));
            if (Directory.Exists(c)) return c;
            Debug.Log($"No HD assets at {c}");
            return null;
        }

        /// <summary>document.addEventListener("keydown"): the page's key handler.</summary>
        void OnKey(KeyDownEvent k)
        {
            if (k.keyCode == KeyCode.None && k.character == 0) return;
            if (k.keyCode == KeyCode.None) return;   // the character half of a key press: the keyCode half carries it
            var ev = DomEvent.FromKey(k, _doc.rootVisualElement);
            ev.target = k.target as VisualElement;
            // typing in a field is not a hotkey: the key lands on the text element deep inside the field
            if (InField(ev.target) || InField(_doc.rootVisualElement.panel?.focusController?.focusedElement as VisualElement)) return;
            Dom.Invoke(() => _web.documentKeydown(ev));
            if (ev.defaultPrevented) k.StopPropagation();
        }

        static bool InField(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e is TextField) return true;   // text and search inputs (a checkbox or slider keeps the hotkeys)
            return false;
        }

        // The <select> menus (UI Toolkit's GenericDropdownMenu) open at the panel's own root, beside the page: the page's
        // stylesheet does not reach them and the panel's theme is empty, so a menu was laid out as a zero-height strip
        // below the window and no dropdown could be used. An open menu gets its layout and colours inline.
        VisualElement _styledMenu;
        void StyleOpenMenu()
        {
            var tree = _doc != null ? _doc.rootVisualElement?.panel?.visualTree : null;
            if (tree == null) return;
            VisualElement menu = null;
            foreach (var c in tree.Children()) if (c.ClassListContains("unity-base-dropdown")) { menu = c; break; }
            if (menu == null) { _styledMenu = null; return; }
            var outerNow = menu.Q(className: "unity-base-dropdown__container-outer");
            if (outerNow != null)
            {
                // placed every frame: the menu moves itself again once it has a size
                var anchor = DomSelect.Opening?.worldBound ?? new Rect(0, 0, 200, 20);
                int count = menu.Query(className: "unity-base-dropdown__item").ToList().Count;
                float height = count * 21 + 6, below = tree.layout.height - anchor.yMax;
                float top = below >= height || below >= anchor.y ? anchor.yMax : Mathf.Max(0, anchor.y - height);
                if (outerNow.style.left.value.value != anchor.x) outerNow.style.left = anchor.x;
                if (outerNow.style.top.value.value != top) outerNow.style.top = top;
                outerNow.style.minWidth = anchor.width;
                outerNow.style.maxHeight = Mathf.Max(below, anchor.y);
            }
            if (menu == _styledMenu) return;
            _styledMenu = menu;
            var s = menu.style;
            s.position = Position.Absolute; s.left = 0; s.top = 0; s.right = 0; s.bottom = 0;
            var outer = menu.Q(className: "unity-base-dropdown__container-outer");
            if (outer != null)
            {
                outer.style.position = Position.Absolute;
                outer.style.right = StyleKeyword.Auto; outer.style.bottom = StyleKeyword.Auto;
                outer.style.backgroundColor = (Color)new Color32(0x14, 0x12, 0x0d, 255);
                outer.style.borderTopWidth = outer.style.borderBottomWidth = outer.style.borderLeftWidth = outer.style.borderRightWidth = 1;
                Color brass = new Color32(0xca, 0xb6, 0x6e, 255);
                outer.style.borderTopColor = outer.style.borderBottomColor = outer.style.borderLeftColor = outer.style.borderRightColor = brass;
                outer.style.paddingTop = outer.style.paddingBottom = 2;
            }
            // the scroll list inside: a plain column (its theme rules are missing too: the items lay on each other)
            foreach (var part in menu.Query(className: "unity-scroll-view__content-viewport").ToList()) { part.style.overflow = Overflow.Hidden; part.style.flexGrow = 1; }
            foreach (var part in menu.Query(className: "unity-scroll-view__content-container").ToList()) { part.style.position = Position.Relative; part.style.flexDirection = FlexDirection.Column; }
            foreach (var part in menu.Query(className: "unity-scroller").ToList()) part.style.display = DisplayStyle.None;
            var items = menu.Query(className: "unity-base-dropdown__item").ToList();
            var owner = DomSelect.Opening?.parent as DomSelect;
            foreach (var item in items)
            {
                // choosing: the menu's own choice follows theme state this panel does not have, so a release over
                // an item picks it here and closes the menu
                int index = items.IndexOf(item);
                item.RegisterCallback<PointerUpEvent>(e => { e.StopPropagation(); owner?.ChooseVisible(index); menu.RemoveFromHierarchy(); }, TrickleDown.TrickleDown);
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.paddingLeft = item.style.paddingRight = 10;
                item.style.paddingTop = item.style.paddingBottom = 3;
                item.style.color = (Color)new Color32(0xe3, 0xce, 0x92, 255);
                item.style.fontSize = 13;
                item.style.whiteSpace = WhiteSpace.NoWrap;
                foreach (var t in item.Query<TextElement>().ToList()) t.style.whiteSpace = WhiteSpace.NoWrap;
                item.RegisterCallback<PointerEnterEvent>(_ => item.style.backgroundColor = (Color)new Color32(0x3a, 0x2f, 0x1c, 255));
                item.RegisterCallback<PointerLeaveEvent>(_ => item.style.backgroundColor = StyleKeyword.Null);
            }
            foreach (var mark in menu.Query(className: "unity-base-dropdown__checkmark").ToList()) { mark.style.width = 0; mark.style.height = 0; }
        }

        void Update()
        {
            StyleOpenMenu();
            Perf.Begin();
            _sched.Pump(Time.realtimeSinceStartupAsDouble * 1000 - _t0);
            Perf.Mark("engine");
            _web?.Frame();
            Perf.Mark("web");
            CssLayout.Tick(Dom.document);
            Perf.Mark("css");
            CssCursor.Tick(Dom.document);
            Perf.Mark("cursor");
            _audio.DeliverEnded();
            CanvasEl.FlushAll();
            Perf.Mark("canvas");
            if (Time.time >= _flushAt) { _flushAt = Time.time + 2; _web?.localStorage.Flush(); }
            Perf.Mark("storage");
            Perf.End();
        }

        void OnApplicationQuit()
        {
            if (_web == null) return;
            _sched.Run(() => _web.pagehide());
            _web.localStorage.Flush(true);   // waits for a write in flight, then writes what is left
        }
    }
}
