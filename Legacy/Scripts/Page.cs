// The page around the game: index.html + src/platform/game-ui.mjs, rebuilt in UI Toolkit. The
// canvas shows only the scene window while playing; everything else is drawn here, as in the browser.
using System;
using System.Collections.Generic;
using System.Linq;
using LolCore;
using UnityEngine;
using UnityEngine.UIElements;
using Screen = LolCore.Screen;
using Resources = LolCore.Resources;

public sealed class Page : MonoBehaviour
{
    public GameHost Host;
    UIDocument _doc;
    VisualElement _root, _shell, _left, _right, _stage, _screen, _stageColumn, _gameUi, _titleActions, _modalLayer, _toastStack;
    Label _status;
    Button _continue;
    readonly List<Label> _championLabels = new List<Label>();

    // --------------------------------------------------------------- building

    void EnsureDocument()
    {
        if (_doc != null) return;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
        settings.clearColor = true;
        settings.colorClearValue = new Color32(8, 11, 12, 255);
        var go = new GameObject("Page");
        go.transform.SetParent(transform);
        _doc = go.AddComponent<UIDocument>();
        _doc.panelSettings = settings;
        _root = _doc.rootVisualElement;
        _root.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("lands"));
        _root.AddToClassList("root");
        var ui = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Noto Sans", "Arial" }, 14);
        _root.style.unityFontDefinition = FontDefinition.FromFont(ui);
        _root.focusable = true;
        _root.pickingMode = PickingMode.Position;
        _root.RegisterCallback<GeometryChangedEvent>(_ => Layout());
    }

    public void ShowNoData(string where)
    {
        EnsureDocument();
        _root.Clear();
        var box = El("modal-box");
        box.Add(Text("Lands of Lore", "modal-h2"));
        box.Add(Text($"The game files were not found. Put the imported game folder (the one holding DATA/GENERAL.PAK) at:\n{where}\nor start with --data <folder>.", "panel-note"));
        _root.Add(box);
    }

    public void Build()
    {
        EnsureDocument();
        _root.Clear();
        _shell = El("shell");
        _root.Add(_shell);

        var mast = El("masthead");
        var h1 = Text("Lands of Lore", "h1");
        h1.style.unityFontDefinition = FontDefinition.FromFont(Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman", "Noto Serif" }, 32));
        _status = Text("", "status");
        mast.Add(h1);
        mast.Add(_status);
        _shell.Add(mast);

        var play = El("play");
        _shell.Add(play);
        _left = El("sidebar");
        _stageColumn = El("stage-column");
        _right = El("sidebar");
        play.Add(_left);
        play.Add(_stageColumn);
        play.Add(_right);

        BuildLeft();
        BuildStage();
        BuildGameUi();
        BuildRight();

        _modalLayer = new VisualElement { pickingMode = PickingMode.Ignore };
        _modalLayer.style.position = Position.Absolute;
        _modalLayer.style.left = _modalLayer.style.right = _modalLayer.style.top = _modalLayer.style.bottom = 0;
        _root.Add(_modalLayer);
        _toastStack = El("toast-stack");
        _toastStack.pickingMode = PickingMode.Ignore;
        _root.Add(_toastStack);
        ApplySettings();
        Layout();
    }

    static VisualElement El(string cls)
    {
        var e = new VisualElement();
        foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        return e;
    }

    static Label Text(string text, string cls)
    {
        var l = new Label(text);
        foreach (var c in cls.Split(' ')) if (c.Length > 0) l.AddToClassList(c);
        return l;
    }

    static Button Btn(string text, string cls, Action click)
    {
        var b = new Button(click) { text = text };
        foreach (var c in cls.Split(' ')) if (c.Length > 0) b.AddToClassList(c);
        b.focusable = false;
        return b;
    }

    /// <summary>.ibtn: an icon (index.html's SVG symbol, rendered by tools/render_icons.py) over a caption.</summary>
    static Button IconBtn(string icon, string label, string tip, Action click, string extra = "")
    {
        var b = new Button(click) { tooltip = tip, focusable = false };
        b.AddToClassList("ibtn");
        if (extra.Length > 0) b.AddToClassList(extra);
        b.Add(Icon(icon, "ibtn-icon"));
        b.Add(Text(label, "ibtn-label"));
        return b;
    }

    static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();

    public static Texture2D IconTexture(string name)
    {
        if (!IconCache.TryGetValue(name, out var t)) IconCache[name] = t = UnityEngine.Resources.Load<Texture2D>("Icons/" + name);
        return t;
    }

    static VisualElement Icon(string name, string cls)
    {
        var e = El(cls);
        e.style.backgroundImage = IconTexture(name);
        e.pickingMode = PickingMode.Ignore;
        return e;
    }

    /// <summary>spellIcon: the spell's own icon and colour (spell-widget.mjs THEMES).</summary>
    static string SpellIconName(int spell, string name)
    {
        switch (name)
        {
            case "Drain": return "spell-drain";
            case "Wall of Thorns": return "spell-thorns";
            case "Viper": return "spell-viper";
            case "Backstab": return "spell-backstab";
        }
        return spell >= 0 && spell <= 9 ? $"spell-{spell}" : "i-cast";
    }

    static VisualElement Panel(string title)
    {
        var p = El("panel");
        if (title != null) p.Add(Text(title.ToUpperInvariant(), "panel-h2"));
        return p;
    }

    // --------------------------------------------------------------- left: map, objectives, compass

    Label _mapNote, _facing, _lanternNote;
    VisualElement _questPanel, _questList, _oilLevel, _compass, _lantern, _rose, _flame;

    void BuildLeft()
    {
        var map = Panel("Map");
        _mapNote = Text("Find the magic map to see your surroundings.", "panel-note");
        map.Add(_mapNote);
        _left.Add(map);

        _questPanel = Panel("Objectives");
        _questList = new VisualElement();
        _questPanel.Add(_questList);
        _questPanel.Add(Text("Completed objectives are listed in the Journal.", "panel-note"));
        _left.Add(_questPanel);

        var nav = Panel("Compass & lantern");
        var row = El("nav-row");
        _compass = El("compass");
        _compass.style.backgroundImage = IconTexture("compass-dial");
        _rose = Icon("compass-rose", "layer");
        _compass.Add(_rose);
        _facing = Text("-", "nav-text");
        _lantern = El("lantern");
        _lantern.style.backgroundImage = IconTexture("lantern-body");
        _flame = Icon("lantern-flame", "layer");
        // transform-origin: 30px 66px of the 60x100 box
        _flame.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(66));
        _lantern.Add(_flame);
        row.Add(_compass);
        row.Add(_facing);
        row.Add(_lantern);
        nav.Add(row);
        var oil = El("oil");
        _oilLevel = El("oil-level");
        oil.Add(_oilLevel);
        nav.Add(oil);
        var buttons = El("two-buttons");
        buttons.Add(IconBtn("i-lamp", "On/off", "Switch the lantern on or off", () => { if (Playing) { Host.Gui.ToggleLantern(); Host.Redraw(); } }));
        buttons.Add(IconBtn("i-drop", "Refill", "Refill the lantern with lamp oil from the inventory", () =>
        {
            if (!Playing) return;
            string said = Host.Gui.RefillLantern();
            if (!string.IsNullOrEmpty(said)) Message(said, "system");
            Host.Loader.Drain();
            Host.Redraw();
        }));
        nav.Add(buttons);
        _lanternNote = Text("No lantern yet.", "panel-note");
        nav.Add(_lanternNote);
        _left.Add(nav);
    }

    // --------------------------------------------------------------- stage

    void BuildStage()
    {
        _stage = El("stage");
        _screen = El("screen");
        _stage.Add(_screen);
        _stageColumn.Add(_stage);
        _screen.RegisterCallback<PointerDownEvent>(e =>
        {
            var p = ToEngine(e.localPosition);
            if (p.x >= 0) Host.OnSceneClick(p.x, p.y, e.button == 1 ? 2 : 1);
            _root.Focus();
        });
        _screen.RegisterCallback<PointerMoveEvent>(e =>
        {
            var p = ToEngine(e.localPosition);
            if (p.x >= 0) Host.OnScenePointer(p.x, p.y);
        });

        _titleActions = El("title-actions");
        _titleActions.pickingMode = PickingMode.Ignore;
        _continue = Btn("CONTINUE", "title-button", () =>
        {
            var latest = Host.Saves.Latest();
            if (latest != null) Host.LoadFrom(latest.Slot);
        });
        _continue.style.bottom = Length.Percent(20);
        var newGame = Btn("NEW GAME", "title-button", () => Host.NewGame());
        newGame.style.bottom = Length.Percent(8);
        var intro = Btn("INTRO", "title-button intro-button", () => Host.PlayIntro());
        intro.style.bottom = Length.Percent(4);
        _titleActions.Add(_continue);
        _titleActions.Add(newGame);
        _titleActions.Add(intro);
        foreach (var champion in CharSelect.Champions)
        {
            var label = Text($"{champion.Name} · {champion.Might}/{champion.Protection}/{champion.Magic}", "character-label");
            label.style.left = Length.Percent((champion.X - 13) / 3.2f);
            label.pickingMode = PickingMode.Ignore;
            _championLabels.Add(label);
            _stage.Add(label);
        }
        _stage.Add(_titleActions);
    }

    /// <summary>canvasPosition: a point on the picture in the page's 320x200 coordinates.</summary>
    Vector2Int ToEngine(Vector2 local)
    {
        var frame = Host.Frame;
        float w = _screen.resolvedStyle.width, h = _screen.resolvedStyle.height;
        if (frame == null || w <= 0 || h <= 0) return new Vector2Int(-1, -1);
        int x = (int)(local.x / w * frame.width), y = (int)(local.y / h * frame.height);
        if (x < 0 || y < 0 || x >= frame.width || y >= frame.height) return new Vector2Int(-1, -1);
        return Host.Compact ? new Vector2Int(x + GameHost.SceneWindow.x, y + GameHost.SceneWindow.y) : new Vector2Int(x, y);
    }

    // --------------------------------------------------------------- game-ui: log, prompt, cards, hotbar

    const int LogLimit = 8;
    readonly List<(string Text, string Kind)> _log = new List<(string, string)>();
    string _logFilter = "all";
    ScrollView _logBox;
    VisualElement _prompt, _party;
    readonly List<Button> _filterButtons = new List<Button>();
    string _promptKey = "";
    readonly Card[] _cards = new Card[4];
    readonly Slot[] _slots = new Slot[10];
    public readonly int[] Hotbar = Enumerable.Repeat(-1, 10).ToArray();
    const int SpellBase = 1000;

    sealed class Card
    {
        public VisualElement Root, Face, Hp, Mp, Cooldown, Skills;
        public Label Name, HpText, MpText, Stats, Status;
        public Button Attack, Best, Quick;
        public float HitUntil;
    }

    sealed class Slot { public Button Root; public VisualElement Icon; public Label Count, Spell; }

    void BuildGameUi()
    {
        _gameUi = El("game-ui");
        _stageColumn.Add(_gameUi);
        var messages = El("ui-messages");
        var filters = El("ui-log-filters");
        foreach (var (key, label) in new[] { ("all", "All"), ("say", "Talk"), ("combat", "Combat"), ("system", "System") })
        {
            Button b = null;
            b = Btn(label, "ui-log-filter" + (key == "all" ? " on" : ""), () =>
            {
                _logFilter = key;
                foreach (var x in _filterButtons) x.EnableInClassList("on", x == b);
                RenderLog();
            });
            _filterButtons.Add(b);
            filters.Add(b);
        }
        messages.Add(filters);
        _logBox = new ScrollView(ScrollViewMode.Vertical);
        _logBox.AddToClassList("ui-log");
        _logBox.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        messages.Add(_logBox);
        _prompt = El("ui-prompt");
        messages.Add(_prompt);
        _gameUi.Add(messages);

        _party = El("ui-party");
        for (int c = 0; c < 4; c += 1) _party.Add((_cards[c] = BuildCard(c)).Root);
        _gameUi.Add(_party);

        var bar = El("ui-hotbar");
        for (int i = 0; i < 10; i += 1)
        {
            int index = i;
            var s = new Slot { Root = Btn("", "ui-slot", () => HotbarClick(index)) };
            s.Icon = El("ui-slot-icon");
            s.Root.Add(s.Icon);
            s.Root.Add(Text(((i + 1) % 10).ToString(), "ui-key"));
            s.Count = Text("", "ui-count");
            s.Spell = Text("", "ui-slot-spell");
            s.Root.Add(s.Count);
            s.Root.Add(s.Spell);
            s.Root.RegisterCallback<PointerUpEvent>(e => { if (e.button == 1) UseHotbar(index); });
            _slots[i] = s;
            bar.Add(s.Root);
        }
        _gameUi.Add(bar);
        LoadHotbar();
    }

    Card BuildCard(int c)
    {
        var k = new Card { Root = El("ui-card") };
        k.Root.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) PickHero(c); });
        k.Face = El("ui-face");
        k.Face.RegisterCallback<PointerDownEvent>(e =>
        {
            e.StopPropagation();
            if (!Playing) return;
            if (e.button == 1) { UseHandOn(c); return; }
            if (!PickHero(c)) OpenCharSheet(c);
        });
        k.Root.Add(k.Face);
        var info = El("ui-card-info");
        k.Name = Text("", "ui-name");
        info.Add(k.Name);
        var hp = El("ui-bar hp");
        k.Hp = El("ui-fill");
        k.HpText = Text("", "ui-bar-text");
        hp.Add(k.Hp);
        hp.Add(k.HpText);
        var mp = El("ui-bar mp");
        k.Mp = El("ui-fill");
        k.MpText = Text("", "ui-bar-text");
        mp.Add(k.Mp);
        mp.Add(k.MpText);
        info.Add(hp);
        info.Add(mp);
        var cd = El("ui-cooldown");
        k.Cooldown = El("ui-cooldown-fill");
        cd.Add(k.Cooldown);
        info.Add(cd);
        k.Stats = Text("", "ui-stats");
        info.Add(k.Stats);
        k.Skills = new VisualElement();
        info.Add(k.Skills);
        k.Status = Text("", "ui-status");
        info.Add(k.Status);
        var buttons = El("ui-buttons");
        k.Attack = Btn("Attack", "ui-attack", () => Attack(c));
        k.Best = Btn("Equip best", "ui-attack ui-best", () => { if (!Playing) return; Host.Gui.EquipBest(c); Host.Loader.Drain(); Host.Redraw(); });
        k.Best.tooltip = "Put the best weapon and armour from the inventory into every slot where it beats what is worn";
        k.Quick = Btn("", "ui-attack", () => { if (!Playing) return; Host.Gui.SelectCharacter(c); OpenCharSheet(c); });
        k.Quick.Add(Text("no quick spell", "ui-quick-none"));
        buttons.Add(k.Attack);
        buttons.Add(k.Best);
        buttons.Add(k.Quick);
        info.Add(buttons);
        k.Root.Add(info);
        return k;
    }

    // engine.ui.message / dialogue
    public void Message(string text, string kind)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clean = System.Text.RegularExpressions.Regex.Replace(text, @"[\x01-\x1f]", " ");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
        if (clean.Length == 0) return;
        _log.Add((clean, kind ?? ""));
        if (_log.Count > LogLimit) _log.RemoveAt(0);
        RenderLog();
    }

    bool LogMatches(string kind) =>
        _logFilter == "all" ? true
        : _logFilter == "say" ? kind == "say"
        : _logFilter == "combat" ? kind == "combat" || kind == "warn" || kind == "alert"
        : kind != "say" && kind != "combat";

    void RenderLog()
    {
        if (_logBox == null) return;
        _logBox.Clear();
        var shown = _log.Where(e => LogMatches(e.Kind)).ToList();
        for (int i = 0; i < shown.Count; i += 1)
        {
            var line = Text(shown[i].Text, "ui-log-line");
            if (shown[i].Kind == "say") line.AddToClassList("ui-say");
            else if (shown[i].Kind.Length > 0) line.AddToClassList(shown[i].Kind);
            if (i == shown.Count - 1) line.AddToClassList("last");
            _logBox.Add(line);
        }
        _logBox.schedule.Execute(() => _logBox.scrollOffset = new Vector2(0, float.MaxValue));
    }

    /// <summary>renderPromptInto: the answers, a More for a page break, Continue, and Skip.</summary>
    void RenderPrompt()
    {
        var choices = Host.Choices();
        bool more = Playing && Host.Loader.Text.AwaitingPageBreak;
        string key = choices == null ? "" : string.Join("|", choices) + more;
        if (key == _promptKey) return;
        _promptKey = key;
        _prompt.Clear();
        if (choices == null) return;
        if (more)
            _prompt.Add(Btn("More ▶", "ui-choice more", () => Host.Choose(0)));
        else
            for (int i = 0; i < choices.Count; i += 1)
            {
                int index = i;
                string label = choices.Count == 1 && choices[0] == "Continue" ? "Continue" : $"{i + 1}. {choices[i]}";
                _prompt.Add(Btn(label, "ui-choice", () => Host.Choose(index)));
            }
        if (more || choices.Count <= 1)
        {
            var skip = Btn("Skip ⏭", "ui-choice", () => Host.SkipCutscene());
            skip.tooltip = "Skip the cutscene / dialogue up to the next choice (Esc)";
            _prompt.Add(skip);
        }
    }

    bool PickHero(int c)
    {
        if (!Playing || !Host.Loader.Characters[c].Active) return false;
        Host.Gui.SelectCharacter(c);
        Host.Redraw();
        return false;
    }

    public void SelectHero(int c) => PickHero(c);

    void Attack(int c)
    {
        if (!Playing) return;
        Host.Gui.Press(new GuiButton { DefIndex = 7, Arg = c });
        Host.Loader.Drain();
        Host.Redraw();
    }

    void UseHandOn(int c)
    {
        if (!Playing) return;
        Host.Gui.ClickedPortraitEtcRight(new GuiButton { Arg = c });
        Host.Loader.Drain();
        Host.Redraw();
    }

    /// <summary>The character screen: the engine's own sheet for now (the page's comes later).</summary>
    void OpenCharSheet(int c)
    {
        if (!Playing) return;
        Host.Gui.OpenCharSheet(c);
        Host.Loader.Drain();
    }

    public void FlashCard(int c, int damage)
    {
        if (c < 0 || c >= 4 || _cards[c] == null) return;
        _cards[c].HitUntil = Time.time + 0.5f;
        _cards[c].Root.AddToClassList("hit");
    }

    // hotbar -------------------------------------------------------------

    string HotbarPath => System.IO.Path.Combine(Application.persistentDataPath, "hotbar.json");

    void LoadHotbar()
    {
        try
        {
            if (!System.IO.File.Exists(HotbarPath)) return;
            var arr = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(HotbarPath)).RootElement;
            int i = 0;
            foreach (var v in arr.EnumerateArray()) { if (i < 10) Hotbar[i] = v.GetInt32(); i += 1; }
        }
        catch (Exception e) { Debug.LogWarning($"hotbar: {e.Message}"); }
    }

    void SaveHotbar()
    {
        try { System.IO.File.WriteAllText(HotbarPath, "[" + string.Join(",", Hotbar) + "]"); }
        catch (Exception e) { Debug.LogWarning($"hotbar: {e.Message}"); }
    }

    void Assign(int index, int value)
    {
        for (int i = 0; i < 10; i += 1) if (value >= 0 && Hotbar[i] == value) Hotbar[i] = -1;
        Hotbar[index] = value;
        SaveHotbar();
    }

    void HotbarClick(int index)
    {
        if (!Playing) return;
        int v = Hotbar[index];
        if (v >= SpellBase) { Host.CastSpell((v - SpellBase) / 10, (v - SpellBase) % 10); return; }
        if (v < 0) return;
        SwapHand(v);
    }

    public void UseHotbar(int index)
    {
        if (!Playing) return;
        int v = Hotbar[index];
        if (v >= SpellBase) { Host.CastSpell((v - SpellBase) / 10, (v - SpellBase) % 10); return; }
        if (v < 0) return;
        Host.Gui.UseInventorySlot(v, Host.Gui.SelectedCharacter);
        Host.Loader.Drain();
        Host.Redraw();
    }

    void SwapHand(int slot)
    {
        Host.Gui.InventorySlotClick(slot);
        Host.Loader.Drain();
        Host.Redraw();
    }

    // --------------------------------------------------------------- inventory window (#inventory-overlay)

    VisualElement _inventory, _invGrid, _handIcon;
    Label _handName, _invCount;
    Button _handUse, _handDrop;
    string _invKey = "";

    public bool InventoryOpen => _inventory != null && _inventory.style.display == DisplayStyle.Flex;

    public void ToggleInventory(bool? show = null)
    {
        bool open = show ?? !InventoryOpen;
        if (open && !Playing) return;
        if (_inventory == null) BuildInventory();
        _inventory.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        _invKey = "";
    }

    void BuildInventory()
    {
        _inventory = El("inventory-overlay");
        var head = El("inventory-head");
        head.Add(Text("INVENTORY", "panel-h2"));
        var hand = El("hand");
        _handIcon = El("hand-icon");
        _handName = Text("Empty hand", "hand-name");
        _handUse = Btn("Use", "btn small", () => { UseHandOn(Host.Gui.SelectedCharacter); _invKey = ""; });
        _handUse.tooltip = "Use the item in hand on the selected character";
        _handDrop = Btn("Drop", "btn small", () =>
        {
            if (!Playing) return;
            Host.Gui.ClickedSceneDropItem(new GuiButton { Arg = 3 });
            Host.Loader.Drain();
            Host.Redraw();
            _invKey = "";
        });
        _handDrop.tooltip = "Drop the item in hand ahead";
        hand.Add(_handIcon);
        hand.Add(_handName);
        hand.Add(_handUse);
        hand.Add(_handDrop);
        head.Add(hand);
        _invCount = Text("", "inventory-count");
        head.Add(_invCount);
        head.Add(Btn("Close", "btn small", () => ToggleInventory(false)));
        _inventory.Add(head);
        _invGrid = El("inventory-grid");
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.Add(_invGrid);
        _inventory.Add(scroll);
        _inventory.Add(Text("Click a slot to take or place the hand item. Shift-click pins it to the first free hotbar slot. Right-click uses it on the selected hero; Shift+right-click drops it on the floor.", "panel-note"));
        _inventory.style.display = DisplayStyle.None;
        _root.Insert(_root.IndexOf(_modalLayer), _inventory);
    }

    void UpdateInventory()
    {
        if (!InventoryOpen) return;
        var L = Host.Loader;
        var G = Host.Gui;
        var inv = L.Items.Inventory;
        string key = string.Join(",", inv) + "|" + G.ItemInHand + "|" + string.Join(",", Hotbar) + "|" + PaletteSum();
        if (key == _invKey) return;
        _invKey = key;
        var pal = Host.Screen.ScreenPalette;

        // Identical things share one square with a count; a slot pinned to the hotbar keeps its own.
        var stacks = new List<(int Slot, int Item, int Count)>();
        var byKind = new Dictionary<int, int>();
        int free = 0;
        for (int slot = 0; slot < inv.Length; slot += 1)
        {
            int item = inv[slot];
            if (item == 0) { free += 1; continue; }
            int kind = L.Items.InPlay[item] != null ? L.Items.InPlay[item].ItemPropertyIndex : -slot - 1;
            bool pinned = Array.IndexOf(Hotbar, slot) >= 0;
            if (!pinned && byKind.TryGetValue(kind, out int at) && Array.IndexOf(Hotbar, stacks[at].Slot) < 0)
            {
                stacks[at] = (stacks[at].Slot, stacks[at].Item, stacks[at].Count + 1);
                continue;
            }
            if (!byKind.ContainsKey(kind)) byKind[kind] = stacks.Count;
            stacks.Add((slot, item, 1));
        }
        _invGrid.Clear();
        int n = 0;
        foreach (var st in stacks)
        {
            int slot = st.Slot;
            var cell = Btn("", "inv-slot", null);
            cell.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!Playing) return;
                if (e.button == 0)
                {
                    if (e.shiftKey) { int spare = Array.IndexOf(Hotbar, -1); if (spare >= 0) Assign(spare, slot); }
                    else { Host.Gui.InventorySlotClick(slot); Host.Loader.Drain(); Host.Redraw(); }
                }
                else if (e.button == 1)
                {
                    if (e.shiftKey) { var said = Host.Gui.DropToFloor(slot); if (said != null) Message(said, "system"); }
                    else Host.Gui.UseInventorySlot(slot, Host.Gui.SelectedCharacter);
                    Host.Loader.Drain();
                    Host.Redraw();
                }
                _invKey = "";
            });
            var icon = El("inv-icon");
            var shape = Host.Gui.ItemIconShape(st.Item);
            if (shape != null) icon.style.backgroundImage = Icons.Draw(shape, pal, "inv" + n);
            cell.Add(icon);
            if (st.Count > 1) cell.Add(Text(st.Count.ToString(), "ui-count"));
            if (Array.IndexOf(Hotbar, slot) >= 0) cell.AddToClassList("quick");
            cell.tooltip = ItemTooltip(st.Item) + (st.Count > 1 ? $" ({st.Count} of them)" : "");
            _invGrid.Add(cell);
            n += 1;
        }
        for (int i = 0; i < free; i += 1) _invGrid.Add(Btn("", "inv-slot", null));
        _invCount.text = $"{inv.Count(i => i != 0)}/{inv.Length} slots · {stacks.Count} kinds";
        int hand = G.ItemInHand;
        var hs = hand != 0 ? G.ItemIconShape(hand) : null;
        _handIcon.style.backgroundImage = hs != null ? Icons.Draw(hs, pal, "hand") : null;
        _handName.text = hand != 0 ? L.ItemName(hand) : "Empty hand";
        _handUse.SetEnabled(hand != 0);
        _handDrop.SetEnabled(hand != 0);
    }

    string ItemTooltip(int item) => Host.Loader.ItemName(item);

    // --------------------------------------------------------------- the pointer: the item in hand

    int _cursorItem = -1;

    /// <summary>The item in hand is the mouse pointer, as the page makes it the CSS cursor.</summary>
    void UpdateCursor()
    {
        int item = Playing ? Host.Gui.ItemInHand : 0;
        if (item == _cursorItem) return;
        _cursorItem = item;
        var shape = item != 0 ? Host.Gui.ItemIconShape(item) : null;
        if (shape == null) { UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        var tex = Icons.Draw(shape, Host.Screen.ScreenPalette, "cursor", 3);
        UnityEngine.Cursor.SetCursor(tex, new Vector2(tex.width / 2, tex.height / 2), CursorMode.ForceSoftware);
    }

    // --------------------------------------------------------------- right: actions, spells, toolbar

    VisualElement _spellbar;
    Label _credits, _spellNote;
    string _spellKey = "";

    void BuildRight()
    {
        var actions = Panel("Actions");
        var row = El("icon-row");
        row.Add(IconBtn("i-sword", "Attack", "Attack with the next character (F)", () => Host.QuickAttack(), "action-button"));
        row.Add(IconBtn("i-cast", "Cast", "Each ready hero casts their own quick spell in turn (C)", QuickCast, "action-button"));
        row.Add(IconBtn("i-bag", "Items", "Open the inventory (I)", () => ToggleInventory()));
        row.Add(IconBtn("i-rotate", "Order", "Rotate the marching order: the first hero goes to the back (O)", RotateParty));
        row.Add(IconBtn("i-rest", "Camp", "Make camp: rest, heal and travel (R)", Camp));
        row.Add(IconBtn("i-menu", "Menu", "Game menu (Esc)", ToggleMenu));
        actions.Add(row);
        _credits = Text("0 credits", "credits");
        actions.Add(_credits);
        var pad = El("pad");
        foreach (var (glyph, key, tip) in new[]
        {
            ("↶", KeyCode.Home, "Turn left (Q)"), ("▲", KeyCode.UpArrow, "Forward (W)"), ("↷", KeyCode.PageUp, "Turn right (E)"),
            ("◀", KeyCode.LeftArrow, "Slide left (A)"), ("▼", KeyCode.DownArrow, "Back (S)"), ("▶", KeyCode.RightArrow, "Slide right (D)"),
        })
        {
            var b = Btn(glyph, "pad-btn", () => Host.PressPad(key));
            b.tooltip = tip;
            pad.Add(b);
        }
        actions.Add(pad);
        _right.Add(actions);

        var spells = Panel("Spells");
        _spellbar = new VisualElement();
        spells.Add(_spellbar);
        _spellNote = Text("No spells known.", "panel-note");
        spells.Add(_spellNote);
        _right.Add(spells);

        var tools = El("panel");
        var trow = El("toolbar-row");
        trow.Add(IconBtn("i-menu", "Menu", "Game menu (Esc)", ToggleMenu));
        trow.Add(IconBtn("i-book", "Journal", "Quest journal (J)", () => Toast("The journal is not ported yet.")));
        trow.Add(IconBtn("i-gear", "Settings", "Settings", ToggleSettings));
        trow.Add(IconBtn("i-full", "Screen", "Full screen / windowed (F11)", ToggleFullScreen));
        tools.Add(trow);
        _right.Add(tools);
    }

    void QuickCast()
    {
        if (!Playing) return;
        Host.CastSpell(Host.Gui.SelectedSpell, 0);
    }

    void RotateParty()
    {
        if (!Playing) return;
        Toast(Host.Gui.RotateParty() ? "The line-up turns." : "Not just now.");
        Host.Redraw();
    }

    void Camp()
    {
        if (!Playing) return;
        if (Host.Gui.InCamp) { Host.Gui.LeaveCamp(); Host.Redraw(); return; }
        if (Host.Gui.CampThreats().Count != 0) { Toast("Not with something that close."); return; }
        if (!Host.Gui.EnterCamp()) Toast("There is nowhere here to pitch a camp.");
        Host.Redraw();
    }

    /// <summary>Settings › Keys actions (DEFAULT_KEYS). True when the key was taken.</summary>
    public bool HostAction(string action)
    {
        switch (action)
        {
            case "attack": Host.QuickAttack(); return true;
            case "inventory": ToggleInventory(); return true;
            case "stash": { var said = Host.Gui.StashHand(); if (said != null) Message(said, "system"); Host.Redraw(); return true; }
            case "cast": QuickCast(); return true;
            case "rotate": RotateParty(); return true;
            case "rest": Camp(); return true;
            case "quicksave": Host.SaveTo("quick"); return true;
            case "quickload": Host.LoadFrom("quick"); return true;
            case "minimap": Host.Settings.Minimap = !Host.Settings.Minimap; Host.Settings.Save(); return true;
            case "photo": Photo(); return true;
            default: return false;   // not ported yet: the key goes to the engine
        }
    }

    void Photo()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, $"lands-of-lore-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        ScreenCapture.CaptureScreenshot(path);
        Toast($"Screenshot saved to {path}");
    }

    public void ToggleFullScreen() => UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen;

    // --------------------------------------------------------------- modes

    bool Playing => Host.State == GameHost.Mode.Playing;

    public void ShowTitle(bool canContinue)
    {
        _continue.style.display = canContinue ? DisplayStyle.Flex : DisplayStyle.None;
        _titleActions.style.display = DisplayStyle.Flex;
        foreach (var l in _championLabels) l.style.display = DisplayStyle.None;
        _gameUi.style.display = DisplayStyle.None;
        CloseModals();
        _status.text = canContinue ? "CONTINUE OR START A NEW GAME" : "START A NEW GAME";
        Layout();
    }

    public void ShowChoosing()
    {
        _titleActions.style.display = DisplayStyle.None;
        foreach (var l in _championLabels) l.style.display = DisplayStyle.Flex;
        _gameUi.style.display = DisplayStyle.None;
        _status.text = "CHOOSE YOUR CHAMPION";
        Layout();
    }

    public void ShowCinematic()
    {
        _titleActions.style.display = DisplayStyle.None;
        foreach (var l in _championLabels) l.style.display = DisplayStyle.None;
        _gameUi.style.display = DisplayStyle.None;
        _status.text = "ANY KEY SKIPS";
        Layout();
    }

    public void ShowPlaying()
    {
        _titleActions.style.display = DisplayStyle.None;
        foreach (var l in _championLabels) l.style.display = DisplayStyle.None;
        _gameUi.style.display = DisplayStyle.Flex;
        CloseModals();
        _log.Clear();
        RenderLog();
        _promptKey = "?";
        Layout();
    }

    // --------------------------------------------------------------- modals

    VisualElement _menu, _settings, _death;
    public bool ModalOpen => _menu != null || _settings != null || _death != null;

    void CloseModals()
    {
        foreach (var m in new[] { _menu, _settings, _death }) m?.RemoveFromHierarchy();
        _menu = _settings = _death = null;
        UpdateModalPicking();
    }

    void UpdateModalPicking() => _modalLayer.pickingMode = ModalOpen ? PickingMode.Position : PickingMode.Ignore;

    public bool CloseTopOverlay()
    {
        if (InventoryOpen && _settings == null && _menu == null) { ToggleInventory(false); return true; }
        if (_settings != null) { _settings.RemoveFromHierarchy(); _settings = null; UpdateModalPicking(); return true; }
        if (_menu != null) { _menu.RemoveFromHierarchy(); _menu = null; UpdateModalPicking(); return true; }
        return false;
    }

    VisualElement Modal(string title, Action close, string closeLabel, out VisualElement box, string cls = "")
    {
        var modal = El("modal " + cls);
        box = El("modal-box");
        var head = El("modal-head");
        head.Add(Text(title, "modal-h2"));
        if (close != null) head.Add(Btn(closeLabel, "btn", close));
        box.Add(head);
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        box.Add(scroll);
        modal.Add(box);
        _modalLayer.Add(modal);
        box = scroll.contentContainer;
        return modal;
    }

    public void ToggleMenu()
    {
        if (_menu != null) { CloseTopOverlay(); return; }
        if (Host.State != GameHost.Mode.Playing && Host.State != GameHost.Mode.Title) return;
        _menu = Modal("Lands of Lore", () => CloseTopOverlay(), "Resume game", out var box);
        box.Add(Text("SAVED GAMES", "modal-h3"));
        var slots = new VisualElement();
        box.Add(slots);
        RenderSaves(slots);
        box.Add(Text("F5 quick save, F9 quick load. Saving is only possible during normal play.", "panel-note"));
        var actions = El("menu-actions");
        actions.Add(Btn("Game & audio settings", "btn", ToggleSettings));
        actions.Add(Btn("Exit game", "btn", () => { CloseModals(); Host.Quit(); }));
        box.Add(actions);
        UpdateModalPicking();
    }

    void RenderSaves(VisualElement into)
    {
        into.Clear();
        var all = Host.Saves.All().ToDictionary(e => e.Slot);
        bool canSave = Host.CanSaveNow();
        foreach (var slot in new[] { "quick", "auto", "cp1", "cp2", "cp3" }.Concat(Saves.ManualSlots))
        {
            all.TryGetValue(slot, out var entry);
            bool manual = Saves.ManualSlots.Contains(slot);
            if (!manual && entry == null) continue;
            var row = El("save-row");
            row.Add(Text(slot == "quick" ? "Quick" : slot == "auto" ? "Auto" : slot.StartsWith("cp") ? slot.ToUpperInvariant() : $"Slot {slot}", "save-label"));
            var thumb = El("save-thumb");
            if (entry != null && !string.IsNullOrEmpty(entry.Thumb)) thumb.style.backgroundImage = Thumb(entry.Thumb);
            row.Add(thumb);
            string info = entry == null ? "empty"
                : $"{(string.IsNullOrEmpty(entry.Name) ? "" : entry.Name + " · ")}{Host.Gui.LevelName(entry.Level)} · {DateTimeOffset.FromUnixTimeMilliseconds(entry.Time).LocalDateTime:g}";
            row.Add(Text(info, "save-info"));
            string s = slot;
            var load = Btn("Load", "save-btn", () => { if (Host.LoadFrom(s)) CloseModals(); });
            load.SetEnabled(entry != null);
            row.Add(load);
            if (manual)
            {
                var save = Btn("Save", "save-btn", () => { if (Host.SaveTo(s)) RenderSaves(into); });
                save.SetEnabled(canSave);
                row.Add(save);
                var del = Btn("✕", "save-btn", () => { Host.Saves.Delete(s); RenderSaves(into); });
                del.style.width = 28;
                del.SetEnabled(entry != null);
                row.Add(del);
            }
            into.Add(row);
        }
    }

    readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();

    Texture2D Thumb(string dataUrl)
    {
        if (_thumbs.TryGetValue(dataUrl, out var t)) return t;
        try
        {
            int comma = dataUrl.IndexOf(',');
            var bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
            t = new Texture2D(2, 2);
            t.LoadImage(bytes);
        }
        catch { t = null; }
        _thumbs[dataUrl] = t;
        return t;
    }

    void ToggleSettings()
    {
        if (_settings != null) { CloseTopOverlay(); return; }
        var s = Host.Settings;
        _settings = Modal("Settings", () => CloseTopOverlay(), "Close", out var box);
        void Changed() { s.Save(); Host.ApplySettings(); }
        void Check(string label, bool value, Action<bool> set)
        {
            var row = El("setting");
            row.Add(Text(label, "setting-label"));
            var t = new Toggle { value = value, focusable = false };
            t.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); });
            row.Add(t);
            box.Add(row);
        }
        void Choice(string label, string[] values, string[] names, string value, Action<string> set)
        {
            var row = El("setting");
            row.Add(Text(label, "setting-label"));
            int at = Math.Max(0, Array.IndexOf(values, value));
            var d = new DropdownField(names.ToList(), at) { focusable = false };
            d.RegisterValueChangedCallback(e => { set(values[Array.IndexOf(names, e.newValue)]); Changed(); });
            row.Add(d);
            box.Add(row);
        }
        Choice("Dialogue", new[] { "both", "speech", "text" }, new[] { "Speech and text", "Speech only", "Text only" }, s.Voice, v => s.Voice = v);
        Check("Sound effects", s.Sfx, v => s.Sfx = v);
        Check("Music (AdLib)", s.Music, v => s.Music = v);
        Choice("Autosave", new[] { "0", "5", "10", "20" }, new[] { "only when leaving the game", "every 5 minutes", "every 10 minutes", "every 20 minutes" }, s.Autosave, v => s.Autosave = v);
        Check("Autosave when changing level", s.AutosaveLevel, v => s.AutosaveLevel = v);
        box.Add(Text("ACCESSIBILITY", "modal-h3"));
        Choice("Interface text", new[] { "normal", "large" }, new[] { "normal", "large" }, s.UiFont, v => s.UiFont = v);
        Check("Colour-blind friendly bars (blue/orange)", s.Colorblind, v => s.Colorblind = v);
        Check("Smooth scrolling", s.Scroll, v => s.Scroll = v);
        Check("Show minimap", s.Minimap, v => s.Minimap = v);
        Check("Play the intro before a new game (any key skips it)", s.Intro, v => s.Intro = v);
        Choice("Monsters return", new[] { "off", "sleep", "reentry", "both" }, new[] { "never on their own", "after sleeping eight hours in camp", "when you walk back into a level", "both" }, s.Respawn, v => s.Respawn = v);
        UpdateModalPicking();
    }

    public void ShowDeath()
    {
        CloseModals();
        _death = Modal("", null, null, out var box, "death");
        box.parent.parent.AddToClassList("death-box");
        box.Add(Text("The party has fallen", "death-h2"));
        box.Add(Text("Load a saved game, or go back to the title.", "death-text"));
        var actions = El("menu-actions");
        var latest = Host.Saves.Latest();
        var load = Btn("Load last save", "btn", () => { if (latest != null && Host.LoadFrom(latest.Slot)) CloseModals(); });
        load.SetEnabled(latest != null);
        actions.Add(load);
        actions.Add(Btn("Exit to the title", "btn", () => { CloseModals(); Host.ShowTitle(); }));
        box.Add(actions);
        UpdateModalPicking();
    }

    public void Toast(string text)
    {
        if (_toastStack == null) { Debug.Log(text); return; }
        var t = Text(text, "toast");
        _toastStack.Add(t);
        t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(3000);
    }

    // --------------------------------------------------------------- settings

    public void ApplySettings()
    {
        if (_root == null) return;
        _root.EnableInClassList("colorblind", Host.Settings.Colorblind);
        _root.style.fontSize = Host.Settings.UiFont == "large" ? 18 : 14;
    }

    // --------------------------------------------------------------- layout

    /// <summary>.shell width and the stage's shape: the web page's formula, in C#.</summary>
    void Layout()
    {
        if (_shell == null || Host.Frame == null) return;
        float W = _root.resolvedStyle.width, H = _root.resolvedStyle.height;
        if (float.IsNaN(W) || W <= 0) return;
        float shell = Math.Min(W - 32, Math.Max(0, H - 500) * 1.47f + 216 * 2 + 32);
        shell = Math.Max(shell, Math.Min(W - 32, 900));
        _shell.style.width = shell;
        float stageW = shell - (216 + 16) * 2;
        float h = stageW * Host.Frame.height / Host.Frame.width;
        _stage.style.width = stageW;
        _stage.style.height = h;
        // .ui-slot { aspect-ratio: 1 }: ten across the stage's width, 4px apart.
        float slot = (stageW - 36) / 10f;
        foreach (var s in _slots) if (s != null) s.Root.style.height = slot;
    }

    // --------------------------------------------------------------- per-frame refresh

    int _lastFrameW, _lastFrameH;
    float _slowAt;

    public void Refresh()
    {
        if (_screen == null || Host.Frame == null) return;
        _screen.style.backgroundImage = Host.Frame;
        if (Host.Frame.width != _lastFrameW || Host.Frame.height != _lastFrameH)
        {
            _lastFrameW = Host.Frame.width;
            _lastFrameH = Host.Frame.height;
            Layout();
        }
        foreach (var k in _cards) if (k != null && k.HitUntil > 0 && Time.time > k.HitUntil) { k.HitUntil = 0; k.Root.RemoveFromClassList("hit"); }
        UpdateCursor();
        if (!Playing) { if (InventoryOpen) ToggleInventory(false); return; }
        RenderPrompt();
        UpdateInventory();
        UpdateParty();
        UpdateHotbar();
        if (Time.time < _slowAt) return;
        _slowAt = Time.time + 0.25f;
        UpdateStatus();
        UpdateSidebars();
        UpdateSpells();
    }

    static readonly string[] Facings = { "NORTH", "EAST", "SOUTH", "WEST" };

    void UpdateStatus()
    {
        _status.text = $"{Host.Gui.LevelName(Host.Loader.Level).ToUpperInvariant()} · {Facings[Host.Party.Direction & 3]}";
    }

    int _questTick;

    void UpdateSidebars()
    {
        var L = Host.Loader;
        bool hasCompass = (L.Flags[31] & 0x40) != 0, hasLantern = (L.Flags[31] & 0x08) != 0, hasMap = (L.Flags[31] & 0x10) != 0;
        _mapNote.text = hasMap ? "The minimap is not ported yet." : "Find the magic map to see your surroundings.";
        _compass.EnableInClassList("missing", !hasCompass);
        _facing.text = hasCompass ? Facings[Host.Party.Direction & 3] : $"(NO COMPASS) {Facings[Host.Party.Direction & 3]}";
        _lantern.EnableInClassList("missing", !hasLantern);
        int oil = Math.Max(0, Math.Min(100, L.LampOilStatus));
        // The rose turns so the facing is on top; the flame shrinks with the oil and goes out when off.
        _rose.style.rotate = new Rotate(new Angle(-(Host.Party.Direction & 3) * 90f, AngleUnit.Degree));
        float f = hasLantern && !L.LampSwitchedOff ? 0.35f + 0.65f * oil / 100f : 0;
        _flame.style.scale = new Scale(new Vector3(f, f, 1));
        _flame.style.opacity = f > 0 ? 1 : 0;
        _oilLevel.style.width = Length.Percent(hasLantern ? oil : 0);
        _oilLevel.EnableInClassList("off", L.LampSwitchedOff);
        _lanternNote.text = !hasLantern ? "No lantern yet." : L.LampSwitchedOff ? "The lantern is off." : $"Oil {oil}%.";
        _credits.text = $"{L.Items.Credits} credits";

        // questStates, every few refreshes (it walks every flag condition).
        if (_questTick++ % 8 != 0) return;
        _questList.Clear();
        var states = new QuestLog(L, Host.Gui).States();
        int shown = 0;
        foreach (var q in QuestLog.All)
        {
            if (!states.TryGetValue(q.Id, out var st) || st != QuestLog.Active) continue;
            var row = El("quest");
            row.Add(Text("◆", q.Main ? "quest-mark main" : "quest-mark"));
            var t = Text(q.Title, "quest-text");
            t.tooltip = q.Detail;
            row.Add(t);
            _questList.Add(row);
            shown += 1;
        }
        _questPanel.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void UpdateSpells()
    {
        var L = Host.Loader;
        var G = Host.Gui;
        string key = string.Join(",", L.AvailableSpells) + "|" + G.SelectedSpell;
        if (key == _spellKey) return;
        _spellKey = key;
        _spellbar.Clear();
        int count = 0;
        for (int slot = 0; slot < L.AvailableSpells.Length; slot += 1)
        {
            int spell = L.AvailableSpells[slot];
            if (spell < 0) continue;
            count += 1;
            int s = slot;
            var row = new Button(() => { G.SelectedSpell = s; Host.CastSpell(s, 0); _spellKey = ""; }) { focusable = false };
            row.AddToClassList("spell-row");
            if (slot == G.SelectedSpell) row.AddToClassList("selected");
            row.Add(Icon(SpellIconName(spell, G.SpellNameOf(spell)), "spell-icon"));
            row.Add(Text(G.SpellNameOf(spell), "spell-name"));
            row.Add(Text("1", "spell-level"));
            row.tooltip = $"Cast {G.SpellNameOf(spell)} with the selected hero";
            _spellbar.Add(row);
        }
        _spellNote.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    string _partyKey = "";

    void UpdateParty()
    {
        var L = Host.Loader;
        var G = Host.Gui;
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < 4; c += 1)
        {
            var ch = L.Characters[c];
            sb.Append($"{ch.Flags}:{ch.HitPointsCur}/{ch.HitPointsMax}:{ch.MagicPointsCur}/{ch.MagicPointsMax}:{ch.CurFaceFrame}:{ch.DamageSuffered}:{ch.WeaponHit}:{string.Join(",", ch.ExperiencePts)}:{string.Join(",", ch.SkillLevels)};");
        }
        sb.Append(G.SelectedCharacter).Append(PaletteSum());
        string key = sb.ToString();
        if (key == _partyKey) return;
        _partyKey = key;
        var exp = StaticData.Table("ExpRequirements");
        for (int c = 0; c < 4; c += 1)
        {
            var ch = L.Characters[c];
            var k = _cards[c];
            k.Root.style.display = ch.Active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ch.Active) continue;
            int frm = (ch.Flags & 0x1108) != 0 && ch.CurFaceFrame < 7 ? 1 : ch.CurFaceFrame;
            if (ch.HitPointsCur <= ch.HitPointsMax >> 1) frm += 14;
            var faces = G.FaceShapes[c];
            if (faces != null && frm < faces.Length && faces[frm] != null) k.Face.style.backgroundImage = Icons.Draw(faces[frm], Host.Screen.ScreenPalette, "face" + c);
            k.Root.EnableInClassList("selected", c == G.SelectedCharacter);
            k.Name.text = ch.Name;
            k.Hp.style.width = Length.Percent(Mathf.Clamp(ch.HitPointsCur * 100f / Math.Max(1, ch.HitPointsMax), 0, 100));
            k.HpText.text = $"{ch.HitPointsCur}/{ch.HitPointsMax}";
            k.Mp.style.width = Length.Percent(Mathf.Clamp(ch.MagicPointsCur * 100f / Math.Max(1, ch.MagicPointsMax), 0, 100));
            k.MpText.text = $"{ch.MagicPointsCur}/{ch.MagicPointsMax}";
            k.Stats.text = $"Might {G.CalculateCharacterStats(c, 0)}  Prot {G.CalculateCharacterStats(c, 1)}";
            k.Skills.Clear();
            string[] names = { "Fighter", "Rogue", "Mage" };
            for (int s = 0; s < 3; s += 1)
            {
                int level = ch.SkillLevels[s] + ch.SkillModifiers[s];
                int next = exp[Math.Min(level, exp.Length - 1)];
                int prev = level > 0 ? exp[Math.Min(level - 1, 10)] : 0;
                int points = ch.ExperiencePts[s];
                var row = El("ui-skill");
                row.Add(Text($"{names[s]} {level}", "ui-skill-name"));
                var bar = El("ui-skill-bar");
                var fill = El("ui-skill-fill");
                fill.style.width = Length.Percent(Mathf.Clamp((points - prev) * 100f / Math.Max(1, next - prev), 0, 100));
                bar.Add(fill);
                row.Add(bar);
                row.Add(Text($"{Short(points)}/{Short(next)}", "ui-skill-xp"));
                k.Skills.Add(row);
            }
            var status = new List<string>();
            if ((ch.Flags & 0x80) != 0) status.Add("poisoned");
            if ((ch.Flags & 0x40) != 0) status.Add("paralyzed");
            if (!G.PartyAwake) status.Add("asleep");
            if (ch.WeaponHit != 0) status.Add($"hit {ch.WeaponHit}");
            if (ch.DamageSuffered != 0) status.Add($"-{ch.DamageSuffered}");
            k.Status.text = string.Join(" · ", status);
            k.Attack.SetEnabled((ch.Flags & 0x314c) == 0);
        }
    }

    static string Short(int n) => n >= 10000 ? (n % 1000 != 0 ? $"{n / 1000.0:0.0}k" : $"{n / 1000}k") : n.ToString();

    int PaletteSum()
    {
        var p = Host.Screen.ScreenPalette;
        int sum = 0;
        for (int i = 0; i < 768; i += 1) sum = sum * 31 + p[i];
        return sum;
    }

    string _hotbarKey = "";

    void UpdateHotbar()
    {
        var L = Host.Loader;
        var inv = L.Items.Inventory;
        string key = string.Join(",", Hotbar) + "|" + string.Join(",", Hotbar.Select(s => s >= 0 && s < SpellBase && s < inv.Length ? inv[s] : 0))
            + "|" + string.Join(",", L.AvailableSpells) + "|" + PaletteSum();
        if (key == _hotbarKey) return;
        _hotbarKey = key;
        for (int i = 0; i < 10; i += 1)
        {
            var s = _slots[i];
            int v = Hotbar[i];
            s.Root.EnableInClassList("assigned", v >= 0);
            s.Count.text = "";
            s.Spell.text = "";
            s.Icon.style.backgroundImage = null;
            if (v >= SpellBase)
            {
                int spell = L.AvailableSpells[(v - SpellBase) / 10];
                if (spell >= 0) s.Spell.text = $"{Host.Gui.SpellNameOf(spell)} {(v - SpellBase) % 10 + 1}";
                continue;
            }
            if (v < 0 || v >= inv.Length || inv[v] == 0) continue;
            var icon = Host.Gui.ItemIconShape(inv[v]);
            if (icon != null) s.Icon.style.backgroundImage = Icons.Draw(icon, Host.Screen.ScreenPalette, "hotbar" + i);
        }
    }

    // --------------------------------------------------------------- input

    void OnGUI()
    {
        var e = Event.current;
        if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None || Host == null) return;
        Host.OnKey(e.keyCode, e.shift);
    }
}
