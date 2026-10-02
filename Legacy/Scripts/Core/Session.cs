// One running game: LolCore wired and driven the way the browser build drives its engine
// (src/game/lol.mjs preInit/startup/runLoop, src/main.mjs runGame). Plain C# with no Unity in it,
// so the Unity host (GameHost) and the headless harness (Harness/) run exactly the same code.
// Keep it C# 9: Unity compiles this file too.
using System;
using System.Collections.Generic;
using System.Linq;
using LolCore;

/// <summary>What the game asks of the sound hardware (sound.mjs).</summary>
public interface ISound
{
    void LoadSoundFile(int track);
    void LoadSoundFileByName(string name);
    int PlayTrack(int track);
    void PlaySoundEffect(int track, int volume);
    void VoicePlay(string name, int volume);
    void PlayVoice(Voc.Sound sound);
    void StopVoice();
    bool VoicePlaying { get; }
}

public sealed class Session
{
    public const int ScreenW = 320, ScreenH = 200;

    public Resources Res { get; private set; }
    public LevelLoader Loader { get; private set; }
    public Gui Gui { get; private set; }
    public Screen Screen { get; private set; }
    public TimInterpreter Tim { get; private set; }
    public Speech Speech { get; private set; }
    public Cinematic Cine { get; private set; }
    public CharSelect CharSelect { get; private set; }
    public Party Party => Loader?.Party;
    public ISound Sound;

    // engine.ui (host-ui.mjs) and the other host hooks
    public Action<string, string> Message = (t, k) => { };
    public Action<int, int> HeroHit = (c, d) => { };
    public Action Died = () => { };
    public Action Revived = () => { };
    public Action OptionsRequested = () => { };
    public Action FinaleRequested = () => { };
    /// <summary>A level was entered (or re-entered): (level, re-entry).</summary>
    public Action<int, bool> LevelEntered = (l, r) => { };

    public bool SpeechOn = true, TextOn = true;
    public bool PartyDown { get; private set; }

    SceneComposer _composer;
    SceneShapes _scenePass;
    int _boundLevel = -1;
    bool _boundOnce;
    bool _redrawWanted = true;
    string _drawKey = "";
    int _campTicks;

    /// <summary>engine.events: what the player did, handed to the engine at its next input pass.</summary>
    readonly Queue<(bool Key, int Code, bool Shift, int X, int Y, int Button)> _events = new Queue<(bool, int, bool, int, int, int)>();

    // ---------------------------------------------------------------- boot

    /// <summary>preInit + startup (lol.mjs), in the order LolCore needs its pieces wired.</summary>
    public void Boot(Func<string, byte[]> read, uint seed)
    {
        Res = new Resources(read);
        Res.LoadPak("GENERAL.PAK");
        Res.LoadPak("STARTUP.PAK");
        var fileList = read("FILEDATA.FDT");
        if (fileList != null) Res.LoadFileList(fileList);

        Loader = new LevelLoader(Res, seed) { PartyBlock = 557, PartyDirection = 0 };
        Screen = Loader.Screen;
        Screen.LoadBitmap(Res.Get("PLAYFLD.CPS"), 3, Screen.Palette(0));
        Loader.LoadItemProperties();
        Loader.SetupTimers();
        // preInit + startup load no level: startupNew (NewGame) or loadState does. Loading level 1
        // here, as the Godot host did, ran its init script before the game began - its dice, flags
        // and the guards' items (which then took item slots 1-4 from the party's own).

        var statics = StaticData.Table("DscBlockIndex").Select(v => (sbyte)v).ToArray();
        var blockMap = StaticData.Table("DscBlockMap").Select(v => (byte)v).ToArray();
        _composer = new SceneComposer(Loader.Walls, Loader.Map, Loader.Vcn, Loader.Vmp, statics, blockMap);
        _scenePass = new SceneShapes(_composer, Loader.Walls, Loader.Map, Loader.Decorations, Screen, Loader.Board)
        {
            ItemShapes = Shapes.DecodeContainer(Res.Get("ITEMSHP.SHP")),
            GameShapes = Shapes.DecodeContainer(Res.Get("GAMESHP.SHP")),
        };
        Loader.ClipVisibleFrom = (block, dir, index) => _scenePass.ClipVisibleFrom(block, dir, index);
        Loader.LevelBuilt = BindLevelData;

        Screen.LoadFont("9", Res.Get("FONT9P.FNT"));
        Screen.LoadFont("6", Res.Get("FONT6P.FNT"));
        Screen.SetFont("9");
        // startup(): the two fade tables the interface draws shapes through.
        var pal = (byte[])Screen.Palette(0).Clone();
        for (int i = 0; i < 3; i += 1) pal[i] = 0x3f;
        for (int i = 2 * 3; i < 128 * 3; i += 1) pal[i] = 0x3f;
        for (int i = 192 * 3; i < 196 * 3; i += 1) pal[i] = 0x3f;
        Screen.GenerateOverlay(pal, Screen.PaletteOverlay1, 1, 96, 254);
        Screen.GenerateOverlay(pal, Screen.PaletteOverlay2, 144, 65, 254);

        Gui = new Gui(Loader, _composer, _scenePass)
        {
            PlayField = Res.Get("PLAYFLD.CPS"),
            GameShapes = _scenePass.GameShapes,
            ItemIconShapes = Shapes.DecodeContainer(Res.Get("ITEMICN.SHP")),
            EffectShapes = SpellShapes("ICE.SHP"),
            FireballShapes = SpellShapes("FIREBALL.SHP"),
            HealShapes = SpellShapes("HEAL.SHP"),
            HealiShapes = SpellShapes("HEALI.SHP"),
            LandsFile = Res.Get("LANDS.ENG"),
            LevelLangFile = LevelLang(Loader.Level),
            LoadBitmapFile = name => Res.Exists(name) ? Res.Get(name) : null,
        };
        Loader.Gui = Gui;

        var text = new TextDisplayer(Screen) { Gui = Gui };
        text.UpdateFlagsSet = bits => Loader.UpdateFlags |= bits;
        text.UpdateFlagsClear = bits => Loader.UpdateFlags &= ~bits;
        Loader.Text = text;
        text.HostAnswersPageBreak = true;
        text.OnMessage = (type, line) => Message(line, (type & 0x7fff) == 2 ? "combat" : MessageKind(type));
        Gui.OnDialogueText = line => Message(line, "say");
        Loader.OnHostMessage = line => Message(line, "system");
        Loader.OnCombat = OnCombat;
        Gui.OnOptions = () => OptionsRequested();

        Speech = new Speech
        {
            Enabled = SpeechOn,
            OpenArchive = name => { var b = read(name); return b != null ? new PakArchive(b) : null; },
            Play = s => Sound?.PlayVoice(s),
            StopHost = () => Sound?.StopVoice(),
            HostPlaying = () => Sound != null && Sound.VoicePlaying,
        };
        Speech.LoadTalkFile(Loader.Level);
        Gui.SpeechPlaying = () => Speech.UpdateCharacterSpeech(Loader.Characters) == 2;
        Loader.PlayCharacterSpeech = (id, speaker) => Speech.PlayCharacterSpeech(id, speaker, Loader.Characters);
        Loader.StopSpeech = () => { Speech.Stop(true); Sound?.StopVoice(); };
        Loader.QueueSpeech = (id, speaker) => { Speech.NextSpeechId = id; Speech.NextSpeaker = speaker; };
        Loader.TextEnabled = () => TextOn;

        Loader.OnSoundEffect = id => Sound?.PlaySoundEffect(id, 255);
        Loader.OnEnvironmentalSound = (id, volume) => Sound?.PlaySoundEffect(id, volume);
        Loader.OnMusicTrack = track => Sound?.PlayTrack(track);
        Loader.OnLoadSoundFile = track => Sound?.LoadSoundFile(track);
        Loader.OnEndSequence = () => FinaleRequested();

        Tim = new TimInterpreter(Screen, Res, () => Loader.Clock, LevelLoader.TickLength)
        {
            Gui = Gui,
            Loader = Loader,
            Wait = ms => Loader.AdvanceClock(ms),
        };
        Tim.PlayVoice = name =>
        {
            if (!Speech.Enabled) return;
            string file = name + ".VOC";
            if (Res.Exists(file)) Sound?.PlayVoice(Voc.Decode(Res.Get(file)));
        };
        Tim.LoadSoundFileByName = name => Sound?.LoadSoundFileByName(name);
        Tim.MusicTrack = track => Sound?.PlayTrack(track);
        Tim.SoundEffect = id => Sound?.PlaySoundEffect(id, -1);
        Tim.Animator.PlaySoundEffect = id => Sound?.PlaySoundEffect(id, -1);
        Loader.Tim = Tim;
        text.Wait = Tim.Wait;
        text.TickLength = LevelLoader.TickLength;

        var automap = new Automap(Screen, Loader.Walls, Loader.Map, text) { CurrentMapLevel = Loader.Level };
        if (Res.Exists("AUTOBUT.SHP"))
            automap.Shapes = Shapes.DecodeFile(Cps.DecodeBitmapData(Res.Get("AUTOBUT.SHP")).Data).Skip(11).Take(109).ToArray();
        automap.LandsFile = Gui.LandsFile;
        automap.LevelLangFile = Gui.LevelLangFile;
        Gui.Automap = automap;
        if (Res.Exists("PARCH.CPS")) Gui.ParchFile = Res.Get("PARCH.CPS");
        Gui.Font9N = Res.Exists("FONT9PN.FNT") ? Res.Get("FONT9PN.FNT") : null;
        Gui.Font6N = Res.Exists("FONT6PN.FNT") ? Res.Get("FONT6PN.FNT") : null;
        Gui.Font9 = Res.Get("FONT9P.FNT");
        Gui.Font6 = Res.Get("FONT6P.FNT");
        Gui.LegendFile = LevelLang;

        // engine.onStartup: the added spells take their rows before anything reads the table.
        Gui.RegisterExtraSpells();

        Cine = new Cinematic(Screen, Res, Tim, () => Loader.Clock, ms => Loader.AdvanceClock(ms), LevelLoader.TickLength)
        {
            SpeechEnabled = SpeechOn,
            TextEnabled = TextOn,
        };
        Res.LoadPak("ENG/INTRO9.PAK");
        CharSelect = new CharSelect(Screen);
        CharSelect.Load(Res);

        Gui.InitDialogue();
        Gui.EnableDefaultPlayfieldButtons();
    }

    Shape[] SpellShapes(string name) => Res.Exists(name) ? Shapes.DecodeContainer(Res.Get(name)) : Array.Empty<Shape>();
    public byte[] LevelLang(int level) => Res.Exists($"LEVEL{level}.ENG") ? Res.Get($"LEVEL{level}.ENG") : null;

    static readonly string[] MessageKinds = { "", "warn", "alert", "note", "info" };
    static string MessageKind(int type) { int t = type & 0x7fff; return t < MessageKinds.Length ? MessageKinds[t] : ""; }

    public string MonsterNameOf(int monster)
    {
        if (monster < 0 || monster >= Loader.Board.Monsters.Length) return "creature";
        var m = Loader.Board.Monsters[monster];
        return m?.Properties == null ? "creature" : Loader.Board.MonsterName(m).ToLowerInvariant();
    }

    /// <summary>engine.ui.miss / damage / kill: the combat lines of the log.</summary>
    void OnCombat(string what, int attacker, int target, int damage)
    {
        string Who(int id) => (id & 0x8000) != 0 ? $"The {MonsterNameOf(id & 0x7fff)}"
            : id >= 0 && id < 4 ? Loader.Characters[id].Name : "Something";
        switch (what)
        {
            case "damage-monster":
                Message($"{Who(attacker)} hits the {MonsterNameOf(target)} for {damage}.", "combat");
                break;
            case "damage-hero":
                Message($"{Loader.Characters[target].Name} takes {damage} damage"
                    + ((attacker & 0x8000) != 0 ? $" from the {MonsterNameOf(attacker & 0x7fff)}" : "") + ".", "combat");
                HeroHit(target, damage);
                break;
            case "kill": Message($"The {MonsterNameOf(target)} falls.", "combat"); break;
            case "miss": Message($"{Who(attacker)} misses.", "combat"); break;
        }
    }

    public void ApplySettings(bool speech, bool text, bool scroll)
    {
        SpeechOn = speech;
        TextOn = text;
        if (Loader == null) return;
        Speech.Enabled = speech;
        Loader.Text.TextEnabled = text;
        Tim.SpeechEnabled = speech;
        Cine.SpeechEnabled = speech;
        Cine.TextEnabled = text;
        Gui.SmoothScrollingEnabled = scroll;
    }

    // ---------------------------------------------------------------- starting and loading

    /// <summary>startupNew: the champion joins and level 1 begins.</summary>
    public void NewGame(int champion)
    {
        Loader.StartNewGame(champion);
        Loader.ResetTimerSchedule();
        LoadFaces();
        _boundOnce = false;
        PartyDown = false;
        _events.Clear();
        RebindLevel();
        Gui.EnableDefaultPlayfieldButtons();
        Gui.DrawPlayField();
    }

    /// <summary>resumeGame: a saveState() snapshot, put back.</summary>
    public void LoadState(string json)
    {
        using (var doc = System.Text.Json.JsonDocument.Parse(json)) SaveGame.Load(Loader, doc.RootElement);
        Gui.RegisterExtraSpells();
        _boundOnce = false;
        PartyDown = false;
        _events.Clear();
        LoadFaces();
        RebindLevel();
        Loader.ResetTimerSchedule();
    }

    public string SaveState()
    {
        Loader.TagFloorItemsForSave();
        return SaveGame.Save(Loader, Gui);
    }

    public bool HasLivingHero => Loader.Characters.Any(c => c.Active && c.HitPointsCur > 0);

    /// <summary>canSaveNow (main.mjs), the engine's half of it.</summary>
    public bool CanSaveNow() =>
        !PartyDown && Loader.UpdateFlags == 0 && !Gui.WeaponsDisabled && !Loader.SysTimerPaused
        && !Gui.InCamp && Loader.Party.Block != 0 && Loader.PendingScript == null && HasLivingHero;

    void LoadFaces()
    {
        for (int i = 0; i < 4; i += 1)
        {
            var c = Loader.Characters[i];
            if (!c.Active) continue;
            string face = $"FACE{Math.Abs(c.Id):00}.SHP";
            if (Res.Exists(face)) Gui.FaceShapes[i] = Shapes.DecodeContainer(Res.Get(face));
        }
    }

    /// <summary>
    /// Load() built a new level's tables: the views follow straight away, before its scripts run
    /// (the init script already updates the automap and may draw).
    /// </summary>
    void BindLevelData()
    {
        _composer.SetLevel(Loader.Walls, Loader.Map);
        _scenePass.SetLevel(Loader.Walls, Loader.Map, Loader.Decorations);
        _scenePass.SetBoard(Loader.Board);
        _scenePass.Level = Loader.Level;
        Gui.LevelLangFile = LevelLang(Loader.Level);
        if (Gui.Automap != null)
        {
            Gui.Automap.SetLevel(Loader.Walls, Loader.Map);
            Gui.Automap.CurrentMapLevel = Loader.Level;
            Gui.Automap.LevelLangFile = Gui.LevelLangFile;
        }
    }

    /// <summary>A level change builds new tables: everything bound to the old ones follows.</summary>
    public void RebindLevel()
    {
        _redrawWanted = true;
        _boundLevel = Loader.Level;
        BindLevelData();
        _scenePass.DoorShapes = Loader.DoorShapes;
        _composer.SetTiles(Loader.Vcn, Loader.Vmp);
        Speech.LoadTalkFile(Loader.Level);
        Loader.InvalidateDrawOrder();
        Gui.EnableDefaultPlayfieldButtons();
        Gui.DrawPlayField();
        Loader.SetPaletteBrightness(Screen.Palette(0), Loader.Brightness, Loader.LampEffect);
        Sound?.PlayTrack(Loader.MusicTrack);   // loadLevel: snd_playTrack(curMusicTheme)
        bool reEntry = _boundOnce && Loader.LevelWasVisited;
        _boundOnce = true;
        LevelEntered(Loader.Level, reEntry);
    }

    // ---------------------------------------------------------------- input

    /// <summary>engine.events.push({type: "key"}): a DOS scan code (KEY_CODES in gui.mjs).</summary>
    public void PushKey(int code, bool shift) => _events.Enqueue((true, code, shift, 0, 0, 0));

    /// <summary>pushMouse + mousedown, in 320x200 page coordinates (button 1 left, 2 right).</summary>
    public void PushClick(int x, int y, int button)
    {
        Gui.MouseX = x;
        Gui.MouseY = y;
        _events.Enqueue((false, 0, false, x, y, button));
    }

    public void PointerAt(int x, int y) { Gui.MouseX = x; Gui.MouseY = y; }

    /// <summary>gui_updateInput (lol.mjs): queued events against the active button list.</summary>
    void UpdateInput()
    {
        while (_events.Count > 0)
        {
            var ev = _events.Dequeue();
            List<GuiButton> buttons;
            if (!ev.Key)
            {
                Gui.MouseX = ev.X;
                Gui.MouseY = ev.Y;
                buttons = Gui.FindButtonsForClick(ev.X, ev.Y, ev.Button);
            }
            else
            {
                var b = Gui.FindButtonForKey(ev.Code, ev.Shift);
                buttons = b != null ? new List<GuiButton> { b } : new List<GuiButton>();
                if (ev.Code == 61 || ev.Code == 43) Loader.StopSpeech?.Invoke();   // space / enter
                else if (b == null && ev.Code == 55) { CycleSpell(); continue; }
            }
            if (buttons.Count == 0) continue;
            var first = buttons[0];
            if (Gui.ActiveMagicMenu != -1 && first.DefIndex != 15 && first.DefIndex != 16)
            {
                Gui.EnableDefaultPlayfieldButtons();
                Loader.Characters[Gui.ActiveMagicMenu].Flags &= unchecked((int)0xffffffef);
                Gui.DrawCharPortraitWithStats(Gui.ActiveMagicMenu);
                Gui.ActiveMagicMenu = -1;
            }
            foreach (var one in buttons)
            {
                int before = Gui.ActiveButtons.Count;
                int result = Gui.Press(one);
                if (result != 0 || (one.Flags & 0x20) != 0 || Gui.ActiveButtons.Count != before) break;
            }
            Loader.Drain();
            // A scene-window box nothing drives any more is the host's to close.
            if (!ev.Key && (Loader.UpdateFlags & 3) != 0 && Gui.DialogueNumButtons == 0 && Loader.PendingScript == null)
            {
                Loader.RestoreAfterSceneWindowDialogue(true);
                Loader.Drain();
            }
            _redrawWanted = true;
        }
    }

    /// <summary>"/" steps to the next spell on the scroll.</summary>
    void CycleSpell()
    {
        if (Gui.WeaponsDisabled || Loader.AvailableSpells.Length < 2 || Loader.AvailableSpells[1] == -1) return;
        Gui.HighlightSelectedSpell(false);
        Gui.SelectedSpell += 1;
        if (Gui.SelectedSpell >= Loader.AvailableSpells.Length || Loader.AvailableSpells[Gui.SelectedSpell] == -1) Gui.SelectedSpell = 0;
        Gui.HighlightSelectedSpell(true);
        Gui.DrawAllCharPortraitsWithStats();
    }

    // ---------------------------------------------------------------- dialogue

    /// <summary>What the prompt under the picture offers (renderPromptInto in game-ui.mjs).</summary>
    public List<string> Choices()
    {
        var list = new List<string>();
        if (Loader.Text.AwaitingPageBreak) { list.Add("More"); return list; }
        for (int i = 0; i < Gui.DialogueNumButtons && i < Gui.DialogueButtonString.Length; i += 1)
        {
            string label = Gui.DialogueButtonString[i];
            list.Add(string.IsNullOrWhiteSpace(label) ? "..." : label.Trim());
        }
        if (list.Count == 0 && (Loader.UpdateFlags & 3) != 0) list.Add("Continue");
        return list.Count == 0 ? null : list;
    }

    /// <summary>uiChoose: answers the conversation the way clicking its button would.</summary>
    public void Choose(int which)
    {
        if (Loader.Text.AwaitingPageBreak)
        {
            Loader.Text.ResumePageBreak();
            Loader.Drain();
        }
        else if (Gui.DialogueNumButtons == 0)
        {
            if ((Loader.UpdateFlags & 3) != 0 && Loader.PendingScript == null) { Loader.RestoreAfterSceneWindowDialogue(true); Loader.Drain(); }
        }
        else if (which >= 0 && which < Gui.DialogueNumButtons)
        {
            Gui.MouseX = Gui.DialogueButtonPosX[which] + Gui.DialogueButtonXoffs + 4;
            Gui.MouseY = Gui.DialogueButtonYoffs + Gui.DialogueButtonPosY[which] + 4;
            Gui.ProcessDialogue(true);
            Loader.Drain();
            if (Loader.PendingScript == null && (Loader.UpdateFlags & 3) != 0)
            {
                Loader.RestoreAfterSceneWindowDialogue(true);
                Loader.Drain();
            }
        }
        _redrawWanted = true;
    }

    /// <summary>skipCutscene: fast-forwards speech and the MORE pages to the next real choice.</summary>
    public bool SkipCutscene()
    {
        if ((Loader.UpdateFlags & 3) == 0 && !Loader.Text.AwaitingPageBreak && Gui.DialogueNumButtons == 0) return false;
        Loader.StopSpeech?.Invoke();
        for (int guard = 0; guard < 50 && Loader.Text.AwaitingPageBreak; guard += 1)
        {
            Loader.Text.ResumePageBreak();
            Loader.Drain();
        }
        _redrawWanted = true;
        return true;
    }

    // ---------------------------------------------------------------- the loop

    /// <summary>One pass of runLoop (lol.mjs): one engine tick.</summary>
    public void Tick()
    {
        if (_boundLevel != Loader.Level) RebindLevel();
        if (Loader.PendingScript != null)
        {
            // A level script waiting on a conversation is an await inside runLoop: the loop itself
            // stands still. Time passes (tim.exec waits a tick at a time) and update() runs, but no
            // timer fires, no new script starts and nobody walks - exactly as the browser build.
            Loader.ResumeScript();
            Loader.Drain();
            Loader.TimersPaused = true;
            Loader.AdvanceTicks(1);
            Loader.TimersPaused = false;
            UpdateInput();
            Loader.Drain();
            EngineUpdate();
            return;
        }
        if (Loader.NextScriptFunc != 0) { Loader.RunNextScriptFunc(); Loader.Drain(); }
        Loader.AdvanceTicks(1);   // timerUpdate
        Loader.Drain();
        Gui.AutoSelect();
        if (Gui.AutoWalking) Gui.AutoWalkStep();
        UpdateInput();
        Loader.Drain();
        EngineUpdate();
        if (Gui.InCamp)
        {
            if (++_campTicks >= 20) { _campTicks = 0; Gui.CampHeal(); }
            if (Gui.CampIntruded()) { Gui.LeaveCamp(); RebindLevel(); Message("Something walks into the camp: the party are on their feet.", "alert"); }
        }
        if (Loader.Board.SceneUpdateRequired) _redrawWanted = true;
        else Loader.Board.UpdateEnvironmentalSfx(0);
        CheckForPartyDeath();
    }

    /// <summary>LoLEngine::update: animation slots, speaking portrait, lamp, compass, message fade.</summary>
    void EngineUpdate()
    {
        if ((Loader.UpdateFlags & 8) == 0)
            for (int i = 0; i < 6; i += 1) Tim.Animator.Update(i);
        if (Gui.PortraitSpeechAnimDue) Gui.UpdatePortraitSpeechAnim();
        Gui.UpdateLampStatus();
        Gui.UpdateCompass();
        if (Sound != null && !Sound.VoicePlaying && Speech.Playing != null) Speech.Finished();
        Speech.UpdateCharacterSpeech(Loader.Characters);
        Gui.FadeTextStep();
    }

    /// <summary>runLoop's catch: a script or button bug must not end the game.</summary>
    public void Recover(Exception e)
    {
        Message($"ERROR: {e.Message}", "system");
        if ((Loader.UpdateFlags & 3) != 0) Loader.RestoreAfterSceneWindowDialogue(true);
        Gui.WeaponsDisabled = false;
        Loader.UpdateFlags = 0;
        _redrawWanted = true;
    }

    void CheckForPartyDeath()
    {
        if (HasLivingHero) { PartyDown = false; return; }
        if (!Loader.Characters.Any(c => c.Active) || PartyDown) return;
        PartyDown = true;
        if (Gui.WeaponsDisabled) Gui.CloseCharSheet();
        Gui.DrawAllCharPortraitsWithStats();
        if ((Loader.Board.PartyDamageFlags & 0x40) != 0)
        {
            // Knocked out rather than killed: they wake up again.
            Screen.FadeToBlack(40, ms => Loader.AdvanceClock(ms));
            for (int i = 0; i < 4; i += 1)
                if (Loader.Characters[i].Active) Gui.IncreaseCharacterHitpoints(i, 1, true);
            Gui.DrawAllCharPortraitsWithStats();
            Screen.FadeToPalette1(40, ms => Loader.AdvanceClock(ms));
            Loader.Board.PartyDamageFlags = -1;
            PartyDown = false;
            _redrawWanted = true;
            Revived();
            return;
        }
        Loader.Board.PartyDamageFlags = -1;
        Died();
    }

    /// <summary>
    /// The playfield, redrawn when something it shows has changed and only while it is what is on
    /// screen (not under the character sheet, the atlas or a conversation box).
    /// </summary>
    public void Draw()
    {
        var chars = Loader.Characters;
        string key = $"{Loader.Level}:{Party.Block}:{Party.Direction}:{Loader.DrawSerial}:{Loader.UpdateFlags}"
            + $":{Gui.ItemInHand}:{Gui.SelectedCharacter}:{Gui.SelectedSpell}:{Loader.Flags[31]}:{Loader.LampEffect}"
            + $":{Loader.Items.InventoryCurItem}:{Loader.Items.Credits}:{Screen.FadeFlag}:{Gui.CompassDirection}"
            + $":{chars[0].HitPointsCur},{chars[1].HitPointsCur},{chars[2].HitPointsCur},{chars[3].HitPointsCur}"
            + $":{chars[0].MagicPointsCur},{chars[1].MagicPointsCur},{chars[2].MagicPointsCur},{chars[3].MagicPointsCur}"
            + $":{chars[0].Flags},{chars[1].Flags},{chars[2].Flags},{chars[3].Flags}"
            + $":{chars[0].CurFaceFrame},{chars[1].CurFaceFrame},{chars[2].CurFaceFrame},{chars[3].CurFaceFrame}"
            + $":{Tim.Animator.Serial}:{Loader.Board.SceneSerial()}";
        bool changed = key != _drawKey;
        _drawKey = key;
        if (!Gui.WeaponsDisabled && !Gui.AutomapActive && (Loader.UpdateFlags & 3) == 0 && (changed || _redrawWanted))
        {
            _redrawWanted = false;
            Gui.DrawPlayField();
        }
    }

    public void Redraw() => _redrawWanted = true;

    /// <summary>compactActive: the page shows only the scene window.</summary>
    public bool CompactView => !Gui.AutomapActive && !Gui.WeaponsDisabled
        && (Loader.UpdateFlags & 4) == 0 && Loader.Party != null && Loader.Party.Block != 0;

    // ---------------------------------------------------------------- actions

    public void QuickAttack() { Gui.QuickAttack(); Loader.Drain(); _redrawWanted = true; }

    /// <summary>quickCastSpell with the selected hero; false when they cannot.</summary>
    public bool CastSpell(int slot, int power)
    {
        int spell = slot < Loader.AvailableSpells.Length ? Loader.AvailableSpells[slot] : -1;
        if (spell < 0) return false;
        int who = Gui.SelectedCharacter;
        if (!Loader.Characters[who].Active) who = Array.FindIndex(Loader.Characters, c => c.Active);
        if (who < 0) return false;
        bool ok = Gui.QuickCastSpell(who, slot, power) != 0;
        Loader.Drain();
        _redrawWanted = true;
        return ok;
    }
}
