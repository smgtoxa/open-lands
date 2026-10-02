// The Unity side of the game: the Session (Core/Session.cs) run at 60 ticks a second, its page 0
// drawn into a texture, the page (Page.cs) around it, the keyboard, the saves and the screens
// before and after play (title, champion, cinematics). Everything the engine does is in Session,
// shared with the headless harness.
using System;
using System.IO;
using LolCore;
using UnityEngine;
using Screen = LolCore.Screen;

public sealed class GameHost : MonoBehaviour
{
    public const int ScreenW = 320, ScreenH = 200;
    // SCENE in src/main.mjs: the part of the page shown while playing; the rest is the page's own.
    public static readonly RectInt SceneWindow = new RectInt(112, 0, 176, 120);

    public Session Game { get; private set; }
    public LevelLoader Loader => Game.Loader;
    public Gui Gui => Game.Gui;
    public Screen Screen => Game.Screen;
    public Party Party => Game.Party;
    public string DataRoot { get; private set; }
    public Page Page { get; private set; }
    public AudioOut Audio { get; private set; }
    public Settings Settings { get; } = Settings.Load();
    public Saves Saves { get; private set; }

    public enum Mode { NoData, Title, Choosing, Cinematic, Playing }
    public Mode State { get; private set; } = Mode.NoData;

    /// <summary>The frame as the page shows it: the scene window while playing, else the whole page.</summary>
    public Texture2D Frame { get; private set; }
    public bool Compact { get; private set; }
    public bool PartyDown => Game.PartyDown;

    int _charStep;
    float _charNext;
    double _tickAcc;
    bool _hostPaused;
    float _autosaveAt;
    readonly Color32[] _pixels = new Color32[ScreenW * ScreenH];
    readonly Color32[] _palette = new Color32[256];

    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;
        Audio = gameObject.AddComponent<AudioOut>();
        Page = gameObject.AddComponent<Page>();
        Page.Host = this;
        gameObject.AddComponent<Autopilot>().Host = this;
    }

    void Start()
    {
        DataRoot = FindDataRoot();
        if (DataRoot == null)
        {
            Page.ShowNoData(Path.Combine(Application.persistentDataPath, "game"));
            return;
        }
        Game = Fresh();
        Audio.Init(Game.Res);
        Audio.IntroMode = () => Game.Tim.IntroMode;
        Saves = new Saves(Path.Combine(Application.persistentDataPath, "saves.json"));
        Frame = new Texture2D(ScreenW, ScreenH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        ApplySettings();
        Page.Build();
        ShowTitle();
    }

    /// <summary>runGame (main.mjs): every game - new or loaded - gets an engine of its own, so
    /// nothing of the last one (items, flags, monsters, dice) leaks into it.</summary>
    Session Fresh()
    {
        var game = new Session
        {
            Sound = Audio,
            Message = (text, kind) => Page.Message(text, kind),
            HeroHit = (c, damage) => Page.FlashCard(c, damage),
            Died = () => Page.ShowDeath(),
            Revived = () => Page.Toast("The party come round."),
            OptionsRequested = () => Page.ToggleMenu(),
            FinaleRequested = PlayFinale,
            LevelEntered = OnLevelEntered,
        };
        game.ApplySettings(Settings.Speech, Settings.Text, Settings.Scroll);
        game.Boot(ReadData, (uint)Environment.TickCount);
        return game;
    }

    static string FindDataRoot()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--data");
        string beside = Path.GetDirectoryName(Application.dataPath);
        var candidates = new[]
        {
            at >= 0 && at + 1 < args.Length ? args[at + 1] : null,
            Path.Combine(Application.persistentDataPath, "game", "DATA"),
            Path.Combine(beside, "DATA"),
            Path.Combine(beside, "GameData", "DATA"),                          // the project folder, in the editor
            Path.GetFullPath(Path.Combine(beside, "..", "..", "GameData", "DATA")),   // Build/Windows inside the project
        };
        foreach (string c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "GENERAL.PAK"))) return c;
        return null;
    }

    byte[] ReadData(string name)
    {
        string path = Path.Combine(DataRoot, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void ApplySettings()
    {
        if (Loader == null) return;
        Game.ApplySettings(Settings.Speech, Settings.Text, Settings.Scroll);
        Audio.SetMusicEnabled(Settings.Music, State == Mode.Playing ? Loader.MusicTrack : 0);
        Audio.SfxEnabled = Settings.Sfx;
        Page?.ApplySettings();
    }

    void OnLevelEntered(int level, bool reEntry)
    {
        if (State == Mode.Playing && Settings.AutosaveLevel) Autosave();
        if (reEntry && (Settings.Respawn == "reentry" || Settings.Respawn == "both")) Loader.RespawnMonsters();
    }

    // ---------------------------------------------------------------- screens

    /// <summary>showTitle: TITLE.CPS with Continue / New game / Intro under it.</summary>
    public void ShowTitle()
    {
        State = Mode.Title;
        if (Game.Res.Exists("TITLE.CPS")) Screen.LoadBitmap(Game.Res.Get("TITLE.CPS"), 0, Screen.Palette(0));
        Screen.SetScreenPalette(Screen.Palette(0));
        Page.ShowTitle(Saves.Latest() != null);
        Present();
    }

    public void NewGame()
    {
        if (Settings.Intro) { PlayIntro(then: Mode.Choosing); return; }
        State = Mode.Choosing;
        _charStep = 0;
        Page.ShowChoosing();
    }

    Mode _afterCinematic = Mode.Title;

    public void PlayIntro(Mode then = Mode.Title)
    {
        if (!Game.Cine.Begin("LOLINTRO.TIM", "LOLINTRO.DIP", Cinematic.IntroPaks, outro: false))
        {
            if (then == Mode.Choosing) { State = Mode.Choosing; Page.ShowChoosing(); }
            return;
        }
        Game.Cine.Skip = false;
        _afterCinematic = then;
        State = Mode.Cinematic;
        Page.ShowCinematic();
    }

    void PlayFinale()
    {
        if (!Game.Cine.Begin("LOLFINAL.TIM", "LOLFINAL.DIP", Cinematic.OutroPaks, outro: true)) return;
        Game.Cine.Skip = false;
        _afterCinematic = Mode.Title;
        State = Mode.Cinematic;
        Page.ShowCinematic();
    }

    public void ChooseChampion(int index)
    {
        Game = Fresh();
        Audio.Rebind(Game.Res);
        Game.NewGame(index);
        StartPlaying();
    }

    void StartPlaying()
    {
        State = Mode.Playing;
        _tickAcc = 0;
        Page.ShowPlaying();
    }

    public void Quit()
    {
        if (State == Mode.Playing) Autosave();
        ShowTitle();
    }

    // ---------------------------------------------------------------- saves

    public bool CanSaveNow() => State == Mode.Playing && Game.CanSaveNow();

    public bool SaveTo(string slot, bool quiet = false)
    {
        if (!CanSaveNow()) { if (!quiet) Page.Toast("Saving is only possible during normal play."); return false; }
        try { Saves.Write(slot, Loader.Level, Party.Block, Game.SaveState(), Thumbnail()); }
        catch (Exception e) { Page.Toast($"Not saved: {e.Message}"); return false; }
        if (!quiet) Page.Toast(slot == "quick" ? "Quick save." : $"Saved to slot {slot}.");
        return true;
    }

    public void Autosave() { try { SaveTo("auto", quiet: true); } catch (Exception e) { Debug.LogWarning($"autosave: {e.Message}"); } }

    public bool LoadFrom(string slot)
    {
        var state = Saves.State(slot);
        if (state == null) { Page.Toast("That slot is empty."); return false; }
        try
        {
            Game = Fresh();
            Audio.Rebind(Game.Res);
            Game.LoadState(state);
            if (!Game.HasLivingHero) { Page.Toast("That save has no party in it."); return false; }
            StartPlaying();
            Page.Toast(slot == "quick" ? "Loaded the quick save." : $"Loaded {slot}.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Page.Toast($"That save will not load: {e.Message}");
            return false;
        }
    }

    /// <summary>The 88x60 JPEG the saves list shows, as the browser writes it.</summary>
    string Thumbnail()
    {
        var shot = new Texture2D(SceneWindow.width, SceneWindow.height, TextureFormat.RGB24, false);
        var page = Screen.Page(0);
        var pal = Screen.ScreenPalette;
        var px = new Color32[SceneWindow.width * SceneWindow.height];
        for (int y = 0; y < SceneWindow.height; y += 1)
            for (int x = 0; x < SceneWindow.width; x += 1)
            {
                int c = page[(SceneWindow.y + y) * ScreenW + SceneWindow.x + x] * 3;
                px[(SceneWindow.height - 1 - y) * SceneWindow.width + x] = new Color32((byte)(pal[c] * 255 / 63), (byte)(pal[c + 1] * 255 / 63), (byte)(pal[c + 2] * 255 / 63), 255);
            }
        shot.SetPixels32(px);
        shot.Apply();
        var rt = RenderTexture.GetTemporary(88, 60);
        Graphics.Blit(shot, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var small = new Texture2D(88, 60, TextureFormat.RGB24, false);
        small.ReadPixels(new Rect(0, 0, 88, 60), 0, 0);
        small.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        string data = "data:image/jpeg;base64," + Convert.ToBase64String(small.EncodeToJPG(60));
        Destroy(shot);
        Destroy(small);
        return data;
    }

    void OnApplicationQuit() { if (State == Mode.Playing) Autosave(); }

    // ---------------------------------------------------------------- input

    /// <summary>KEY_CODES (src/game/gui.mjs): DOS scan codes the button table answers to.</summary>
    public static int ScanCode(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Space: return 61;
            case KeyCode.Return: case KeyCode.KeypadEnter: return 43;
            case KeyCode.UpArrow: case KeyCode.W: return 96;
            case KeyCode.RightArrow: case KeyCode.D: return 102;
            case KeyCode.DownArrow: case KeyCode.S: return 98;
            case KeyCode.LeftArrow: case KeyCode.A: return 92;
            case KeyCode.Home: case KeyCode.Q: return 91;
            case KeyCode.PageUp: case KeyCode.E: return 101;
            case KeyCode.End: return 93;
            case KeyCode.PageDown: return 103;
            case KeyCode.F1: return 112;
            case KeyCode.F2: return 113;
            case KeyCode.F3: return 114;
            case KeyCode.F4: return 115;
            case KeyCode.F6: return 117;
            case KeyCode.Tab: return 16;
            case KeyCode.K: return 38;
            case KeyCode.Y: return 22;
            case KeyCode.Z: return 46;
            case KeyCode.P: return 26;
            case KeyCode.Slash: return 55;
            case KeyCode.Escape: return 110;
            case KeyCode.Minus: return 12;
            case KeyCode.Equals: return 13;
            case KeyCode.Comma: return 53;
            case KeyCode.Period: return 54;
        }
        if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha7) return 2 + (key - KeyCode.Alpha1);
        return -1;
    }

    /// <summary>The page's keydown handler (main.mjs): host hotkeys first, the rest to the engine.</summary>
    public void OnKey(KeyCode key, bool shift)
    {
        if (State == Mode.Cinematic) { Game.Cine.Skip = true; return; }
        if (State == Mode.Choosing)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha4) ChooseChampion(key - KeyCode.Alpha1);
            return;
        }
        if (State != Mode.Playing) return;

        if (key == KeyCode.Escape)
        {
            if (Page.CloseTopOverlay()) return;
            if (Gui.AutoWalking) { Gui.StopAutoWalk(); return; }
            if (Gui.AutomapActive) { Gui.CloseAutomap(); Gui.DrawPlayField(); return; }
            if (Game.SkipCutscene()) return;
            Page.ToggleMenu();
            return;
        }
        if (Page.ModalOpen) return;
        if (key == KeyCode.F5) { SaveTo("quick"); return; }
        if (key == KeyCode.F9) { LoadFrom("quick"); return; }
        if (key == KeyCode.F11) { Page.ToggleFullScreen(); return; }
        string action = Settings.ActionFor(key);
        if (action != null && Page.HostAction(action)) return;

        if (Compact)
        {
            // Shift+1..4 picks a hero; 1-0 use the hotbar.
            if (shift && key >= KeyCode.Alpha1 && key <= KeyCode.Alpha4) { Page.SelectHero(key - KeyCode.Alpha1); return; }
            int slot = key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9 ? key - KeyCode.Alpha1 : key == KeyCode.Alpha0 ? 9 : -1;
            if (slot >= 0) { Page.UseHotbar(slot); return; }
        }

        // The prompt's buttons answer Space and Enter; a conversation can hold them open with
        // updateFlags 0, so the buttons decide, not the flags.
        if ((key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter) && Game.Choices() != null)
        {
            Loader.StopSpeech?.Invoke();
            Game.Choose(0);
            return;
        }
        if (IsMoveKey(key)) Gui.StopAutoWalk();
        int code = ScanCode(key);
        if (code >= 0) Game.PushKey(code, shift);
    }

    static bool IsMoveKey(KeyCode k) =>
        k == KeyCode.UpArrow || k == KeyCode.DownArrow || k == KeyCode.LeftArrow || k == KeyCode.RightArrow
        || k == KeyCode.W || k == KeyCode.A || k == KeyCode.S || k == KeyCode.D || k == KeyCode.Q || k == KeyCode.E
        || k == KeyCode.Home || k == KeyCode.PageUp;

    public void PressPad(KeyCode key) => OnKey(key, false);

    /// <summary>A click on the picture, in 320x200 engine coordinates (button 1 left, 2 right).</summary>
    public void OnSceneClick(int x, int y, int button)
    {
        if (State == Mode.Cinematic) { Game.Cine.Skip = true; return; }
        if (State == Mode.Choosing)
        {
            for (int i = 0; i < CharSelect.Champions.Length; i += 1)
            {
                var c = CharSelect.Champions[i];
                if (x >= c.X && x < c.X + 32 && y >= 127 && y < 159) { ChooseChampion(i); return; }
            }
            return;
        }
        if (State == Mode.Playing) Game.PushClick(x, y, button);
    }

    public void OnScenePointer(int x, int y) { if (State == Mode.Playing) Game.PointerAt(x, y); }

    // pass-throughs for the page
    public System.Collections.Generic.List<string> Choices() => State == Mode.Playing ? Game.Choices() : null;
    public void Choose(int which) { if (State == Mode.Playing) Game.Choose(which); }
    public bool SkipCutscene() => State == Mode.Playing && Game.SkipCutscene();
    public void Redraw() => Game.Redraw();
    public void QuickAttack() { if (State == Mode.Playing) Game.QuickAttack(); }

    public void CastSpell(int slot, int power)
    {
        if (State != Mode.Playing) return;
        if (!Game.CastSpell(slot, power)) Page.Toast($"{Loader.Characters[Gui.SelectedCharacter].Name} cannot cast that.");
    }

    // ---------------------------------------------------------------- loop

    void Update()
    {
        if (Loader == null) return;
        switch (State)
        {
            case Mode.Cinematic:
                if (!Game.Cine.Step() || Game.Cine.Skip)
                {
                    Game.Cine.End();
                    Game.Redraw();
                    if (_afterCinematic == Mode.Choosing) { State = Mode.Choosing; _charStep = 0; Page.ShowChoosing(); }
                    else ShowTitle();
                }
                Present();
                break;
            case Mode.Title:
                Present();
                break;
            case Mode.Choosing:
                _charNext -= Time.deltaTime;
                if (_charNext <= 0) { _charNext = 0.14f; _charStep += 1; }
                Game.CharSelect.Draw(_charStep);
                Screen.SetScreenPalette(Screen.Palette(0));
                Present();
                break;
            case Mode.Playing:
                SyncPause();
                _tickAcc += Time.deltaTime * 1000.0;
                // Whole ticks, as the engine counts them; a stall does not turn into a burst.
                if (_tickAcc > LevelLoader.TickLength * 6) _tickAcc = LevelLoader.TickLength * 6;
                while (_tickAcc >= LevelLoader.TickLength && State == Mode.Playing)
                {
                    _tickAcc -= LevelLoader.TickLength;
                    try { Game.Tick(); }
                    catch (Exception e) { Debug.LogException(e); Game.Recover(e); }
                }
                Game.Draw();
                Present();
                AutosaveTimer();
                break;
        }
        Page.Refresh();
    }

    void AutosaveTimer()
    {
        if (!int.TryParse(Settings.Autosave, out int minutes) || minutes <= 0) return;
        if (_autosaveAt <= 0) _autosaveAt = Time.time + minutes * 60;
        if (Time.time < _autosaveAt) return;
        _autosaveAt = Time.time + minutes * 60;
        Autosave();
    }

    /// <summary>syncPause: the world waits while one of the page's windows is open.</summary>
    void SyncPause()
    {
        bool want = Page.ModalOpen;
        if (want && !_hostPaused && !Loader.SysTimerPaused) { Loader.PauseTimers(true); _hostPaused = true; }
        else if (!want && _hostPaused) { _hostPaused = false; if (!Loader.SysTimerPaused) Loader.PauseTimers(false); }
    }

    /// <summary>presentEngine: page 0 through the palette into the texture, cropped while playing.</summary>
    void Present()
    {
        bool compact = State == Mode.Playing && Game.CompactView;
        int w = compact ? SceneWindow.width : ScreenW, h = compact ? SceneWindow.height : ScreenH;
        if (Frame.width != w || Frame.height != h) Frame.Reinitialize(w, h);
        Compact = compact;

        var pal6 = Screen.ScreenPalette;
        for (int i = 0; i < 256; i += 1)
            _palette[i] = new Color32((byte)(pal6[i * 3] * 255 / 63), (byte)(pal6[i * 3 + 1] * 255 / 63), (byte)(pal6[i * 3 + 2] * 255 / 63), 255);
        var page = Screen.Page(0);
        int ox = compact ? SceneWindow.x : 0, oy = compact ? SceneWindow.y : 0;
        // Texture rows run bottom-up; the page runs top-down.
        for (int y = 0; y < h; y += 1)
        {
            int src = (oy + y) * ScreenW + ox, dst = (h - 1 - y) * w;
            for (int x = 0; x < w; x += 1) _pixels[dst + x] = _palette[page[src + x]];
        }
        Frame.SetPixels32(0, 0, w, h, _pixels);
        Frame.Apply(false);
    }
}
