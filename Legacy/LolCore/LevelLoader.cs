// Loading a level the way the game does it: the wall table comes from LEVEL<n>.WLL, and everything
// else - which tile set the walls are drawn from, the level's special colour and its weight, the
// block map, the door flag fixups - is decided by LEVEL<n>.INI, a script.
//
// This is the first piece of the script VM in C#. It runs the real bytecode through Emc and
// implements the opcodes that INI scripts actually use for graphics; the rest (monsters, items,
// sound) are recorded and answered with a harmless value, because they do not change the picture.
// Transliterated from src/game/scene.mjs loadLevel/loadLevelGraphics/loadBlockProperties and the
// opcode bodies in src/game/script.mjs.
namespace LolCore;

public sealed partial class LevelLoader
{
    private readonly Resources _res;
    private readonly uint _seed;
    private Rng _rng;

    /// <summary>The engine's dice, which the spells roll for their damage.</summary>
    public Rng Dice => _rng;

    /// <summary>The party's block when the level loads: scripts refuse to put a monster on top of it.</summary>
    public int PartyBlock = -1;
    public MonsterBoard Board { get; private set; }

    /// <summary>The item slots and their property table: they outlive a level, so they live here.</summary>
    public readonly ItemBoard Items = new();

    /// <summary>The party. Same reason.</summary>
    private readonly Character[] Party_ = { new(), new(), new(), new() };
    public Character[] Characters => Party_;
    public Decorations Decorations { get; private set; }
    public readonly Shape[] DoorShapes = new Shape[2];

    /// <summary>Seed the dice to match a recorded run (engine.randomSeed in the JavaScript build).</summary>
    public LevelLoader(Resources resources, uint randomSeed = 0)
    {
        _res = resources;
        _seed = randomSeed;
        _rng = new Rng(randomSeed);
    }

    public int Level { get; set; }
    public WallData Walls { get; private set; }
    public BlockMap Map { get; private set; }
    public VcnData Vcn { get; private set; }
    public ushort[] Vmp { get; private set; }
    public readonly Screen Screen = new();

    /// <summary>What loadLevelGraphics was told - the values the parity dump used to hand us.</summary>
    public string BlockDataFile { get; private set; } = "";
    public int SpecialColor { get; private set; }
    public int SpecialColorWeight { get; private set; }
    public string OverridePalFile { get; private set; } = "";
    public string DecorationShapeFile { get; private set; } = "";
    public string DecorationDataFile { get; private set; } = "";

    public readonly byte[] Flags = new byte[256];       // flagsTable: the game's bit flags
    public readonly short[] GlobalScriptVars = new short[24];
    public readonly short[] GlobalScriptVars2 = new short[24];

    /// <summary>
    /// The engine's timers, as far as the port models them: the three scene-animation timers a
    /// level script sets up (a torch guttering, a wall that flickers between two types) and the two
    /// that step the monsters. They count in ticks, so a clock that advances in ticks reproduces
    /// them exactly - which is the whole reason the parity dumps run on a clock of their own.
    /// </summary>
    private sealed class GameTimer
    {
        public int Countdown;
        public double NextRun;   // the engine's tick is 1000/60 ms, so the schedule is fractional
        public bool Enabled;
        public double PauseStart = -1;   // when a dialogue stopped it; its schedule shifts by the wait
    }

    private readonly Dictionary<int, GameTimer> _timers = new();

    /// <summary>The clock the timers run on, in milliseconds.</summary>
    public double Clock { get; private set; }

    private long _ticks;

    /// <summary>The tick a moment belongs to. Timers count in ticks, so their schedule lands on
    /// tick boundaries - otherwise a comparison is decided by a rounding error.</summary>
    private double QuantiseToTick(double moment) => Math.Round(moment / TickLength) * TickLength;

    /// <summary>The engine's tick: 60 per second, as the original counts them.</summary>
    public const double TickLength = 1000.0 / 60.0;

    /// <summary>
    /// A door caught mid-swing: which block, which wall of it, and which way it is moving. The
    /// engine keeps three of these, and the door timer steps them one frame at a time.
    /// </summary>
    private sealed class OpenDoor
    {
        public int Block, State, Wall;
    }

    private readonly OpenDoor[] _openDoors = { new(), new(), new() };

    /// <summary>Sets up the timers a new game starts with (setupTimers, the part the port models).</summary>
    public void SetupTimers()
    {
        _timers[0] = new GameTimer { Countdown = 15, Enabled = true, NextRun = Clock };
        _timers[0x10] = new GameTimer { Countdown = 6, Enabled = true, NextRun = Clock };
        _timers[0x11] = new GameTimer { Countdown = 6, Enabled = true, NextRun = Clock + 3 * TickLength };
        // Timer 3: the character update events - a swing recovering, a poison biting, a potion
        // ending. It runs at 15 ticks and switches itself off when there is nothing left to count.
        _timers[3] = new GameTimer { Countdown = 15, Enabled = false, NextRun = Clock };
        // Timer 4: the eight flying-object slots, a step each.
        _timers[4] = new GameTimer { Countdown = 1, Enabled = true, NextRun = Clock };
        for (int i = 0x50; i <= 0x52; i += 1) _timers[i] = new GameTimer { Countdown = 0, Enabled = false };
        // The four the port never started: health and magic coming back, the faces blinking, the
        // lantern burning down, and the message line fading out.
        _timers[8] = new GameTimer { Countdown = 1200, Enabled = true, NextRun = Clock };
        _timers[9] = new GameTimer { Countdown = 10, Enabled = true, NextRun = Clock };
        _timers[10] = new GameTimer { Countdown = 360, Enabled = true, NextRun = Clock };
        _timers[11] = new GameTimer { Countdown = 360, Enabled = false, NextRun = Clock };
    }

    /// <summary>
    /// What the character update events need from the loader: the timer that counts them, whether a
    /// hero wears a given item, and somewhere to say what happened.
    /// </summary>
    private void WireCharacterEffects()
    {
        Board.EnableEffectTimer = () =>
        {
            if (!_timers.TryGetValue(3, out var t)) _timers[3] = t = new GameTimer { Countdown = 15 };
            t.Enabled = true;
            t.NextRun = Clock;
        };
        Board.DisableEffectTimer = () => { if (_timers.TryGetValue(3, out var t)) t.Enabled = false; };
        Board.WearsItem = ItemEquipped;
        Board.RedrawPortrait = c => Gui?.DrawCharPortraitWithStats(c);
        // The game's own wording, from its string table, on the channel the original prints it on.
        // The potion fading has no string in the table, so it keeps the one sentence the port wrote.
        Board.SayAboutCharacter = (c, channel, stringId) =>
        {
            if (stringId < 0)
            {
                Text?.PrintMessage(channel, $"{Party_[c].Name} feels the potion fade.");
                return;
            }
            // Channel 6 is the party's own voice: the hero says it, and the words are printed as
            // well only when characterSays says they should be.
            if (channel == 6 && Gui != null && !Gui.CharacterSays(stringId, Party_[c].Id, true)) return;
            Text?.PrintMessage(channel, (Gui?.LangString(stringId) ?? "").Replace("%s", Party_[c].Name));
        };
        Board.ShowTemporaryFaceFrame = (c, frame, delay, redraw) => Gui?.SetTemporaryFaceFrame(c, frame, delay, redraw);
        Board.PlaySoundEffect = id => OnSoundEffect?.Invoke(id);
        Board.DrawPointsBar = (c, type, newVal, max) => Gui?.DrawPointsBar(c, type, newVal, max);
        Board.RestoreSwampPalette = () => Gui?.RestoreSwampPalette();
        Board.WakeParty = () => { if (Gui != null) Gui.PartyAwake = true; };
        Board.OnMiss = (attacker, target) => OnCombat?.Invoke("miss", attacker, target, 0);
        Board.OnDamage = (who, damage, attacker, isMonster) =>
            OnCombat?.Invoke(isMonster ? "damage-monster" : "damage-hero", attacker, who, damage);
        Board.OnMonsterSlain = (monster, attacker) => OnCombat?.Invoke("kill", attacker, monster, 0);
        Items.RecordsInUse = () =>
        {
            var used = new HashSet<int>(Items.Inventory) { Gui?.ItemInHand ?? 0 };
            foreach (var ch in Party_) if (ch.Active) foreach (int it in ch.Items) used.Add(it);
            foreach (var m in Board.Monsters)
            {
                int a = m?.AssignedItems ?? 0;
                int guard = 0;
                while (a != 0 && guard++ < 32) { used.Add(a); a = Items.InPlay[a]?.NextAssignedObject ?? 0; }
            }
            for (int b = 0; b < 1024; b += 1)
            {
                int cur = Map.AssignedObjects[b];
                int guard = 0;
                while (cur != 0 && guard++ < 64)
                {
                    if ((cur & 0x8000) == 0) used.Add(cur);
                    cur = Board.Find(cur).NextAssignedObject;
                }
            }
            return used;
        };
        Board.SayLevelGained = (c, stringId) =>
            Text?.PrintMessage(0x8003, (Gui?.LangString(stringId) ?? "").Replace("%s", Party_[c].Name));
        Board.OnEnvironmentalSound = (id, volume) => OnEnvironmentalSound?.Invoke(id, volume);
        Board.UpdateFlagsNow = () => UpdateFlags & 1;
        Board.RunBlockScript = (block, flags, charNum, item) => RunLevelScript(block, flags, charNum, item, 0, 0);
        Board.RunItemScriptFor = (charNum, item, flags, next) => RunItemScript(charNum, item, flags, next, 0);
    }

    /// <summary>
    /// initSpells: which procedure each spell number runs. The gaps are the engine's own - spells 7,
    /// 10, 11, 13, 14, 15 and 17 to 19 have no procedure in the original either.
    /// </summary>
    private void WireSpellProcs()
    {
        if (Gui == null || Board == null) return;
        Gui.Wait = AdvanceClock;
        Gui.OnSoundEffect = id => OnSoundEffect?.Invoke(id);
        Board.OnSpellCast = (c, spell, level) =>
        {
            Gui.ActiveSpellChar = c;
            Gui.ActiveSpellNumber = spell;
            Gui.ActiveSpellLevel = level;
        };
        var procs = Board.SpellProcs;
        procs[0] = (c, l) => { Gui.ProcessMagicSparkFull(c, l); return 1; };
        procs[1] = Gui.CastHeal;
        procs[2] = Gui.CastIce;
        procs[3] = Gui.CastFireball;
        procs[4] = (_, l) => Gui.CastHandOfFate(l);
        procs[5] = Gui.CastMistOfDoom;
        procs[6] = Gui.CastLightning;
        procs[8] = (_, _) => Gui.CastFog();
        procs[9] = (c, _) => Gui.CastSwarm(c);
        procs[12] = (_, _) => Gui.CastVaelansCube();
        procs[16] = (c, _) => Gui.CastGuardian(c);
        procs[20] = Gui.CastHealOnSingleCharacter;
    }

    /// <summary>
    /// testWallInvisibility: a wall with nothing to draw and nothing behind it, which is what lets a
    /// block be marked as showing nothing at all.
    /// </summary>
    private bool TestWallInvisibility(int block, int direction)
    {
        int w = Map.Walls[block, direction];
        return Walls.VmpMap[w] == 0 && Walls.ShapeMap[w] == 0 && (Map.Flags[block] & 0x80) == 0;
    }

    /// <summary>resetBlockProperties: what a spell or a script shut is open again.</summary>
    public void ResetBlockProperties()
    {
        for (int block = 0; block < 1024; block += 1)
        {
            if ((Map.Flags[block] & 0x10) != 0)
            {
                Map.Flags[block] &= 0xef;
                if (TestWallInvisibility(block, 0) && TestWallInvisibility(block, 1)) Map.Flags[block] |= 0x40;
            }
            else if ((Map.Flags[block] & 0x40) != 0) Map.Flags[block] &= 0xbf;
            else if ((Map.Flags[block] & 0x80) != 0) Map.Flags[block] &= 0x7f;
        }
    }

    /// <summary>timerProcessDoors: every door in motion moves one frame.</summary>
    private void TimerProcessDoors()
    {
        foreach (var door in _openDoors)
        {
            if (door.Block == 0) continue;
            Map.Walls[door.Block, door.Wall] = (byte)(Map.Walls[door.Block, door.Wall] + door.State);
            Map.Walls[door.Block, door.Wall ^ 2] = (byte)(Map.Walls[door.Block, door.Wall ^ 2] + door.State);
            int flg = Walls.WallFlags[Map.Walls[door.Block, door.Wall]];
            if ((flg & 0x30) != 0) door.Block = 0;
        }
    }

    /// <summary>processDoorSwitch: a lever or a script asking a door to move; 0 means "the other way".</summary>
    public void ProcessDoorSwitch(int block, int openClose)
    {
        block &= 0x3ff;
        if (block == Party?.Block) return;
        if ((Map.AssignedObjects[block] & 0x8000) != 0) return;
        if (openClose == 0)
        {
            foreach (var door in _openDoors)
            {
                if (door.Block != block) continue;
                openClose = -door.State;
                break;
            }
        }
        if (openClose == 0)
        {
            int first = Map.Walls[block, (Walls.WallFlags[Map.Walls[block, 0]] & 8) != 0 ? 0 : 1];
            openClose = (Walls.WallFlags[first] & 1) != 0 ? 1 : -1;
        }
        OpenCloseDoor(block, openClose);
    }

    /// <summary>
    /// openCloseDoor: start a door moving, or - when the door timer is off - put it straight into
    /// its final state, which is what the engine does when nothing is going to animate it.
    /// </summary>
    public void OpenCloseDoor(int block, int openClose)
    {
        block &= 0x3ff;
        int c = (Walls.WallFlags[Map.Walls[block, 0]] & 8) != 0 ? 0 : 1;
        int v = Map.Walls[block, c];
        int flg = openClose == 1 ? 0x10 : openClose == -1 ? 0x20 : 0;
        if ((Walls.WallFlags[v] & flg) != 0) return;
        int s1 = -1, s2 = -1;
        for (int i = 0; i < 3; i += 1)
        {
            if (_openDoors[i].Block == block) { s1 = i; break; }
            if (_openDoors[i].Block == 0 && s2 == -1) s2 = i;
        }
        bool doorTimer = _timers.TryGetValue(0, out var timer0) && timer0.Enabled;
        if ((s1 != -1 || s2 != -1) && doorTimer)
        {
            if (s1 == -1) s1 = s2;
            _openDoors[s1].Block = block;
            _openDoors[s1].State = openClose;
            _openDoors[s1].Wall = c;
            int back = -openClose == 1 ? 0x10 : -openClose == -1 ? 0x20 : 0;
            if ((Walls.WallFlags[v] & back) != 0)
            {
                Map.Walls[block, c] = (byte)(Map.Walls[block, c] + openClose);
                Map.Walls[block, c ^ 2] = (byte)(Map.Walls[block, c ^ 2] + openClose);
            }
            if (_timers.TryGetValue(0, out var timer)) timer.Enabled = true;
        }
        else
        {
            while ((flg & Walls.WallFlags[v]) == 0) v += openClose;
            Map.Walls[block, c] = (byte)v;
            Map.Walls[block, c ^ 2] = (byte)v;
        }
    }

    /// <summary>completeDoorOperations: every door in motion jumps to where it was going.</summary>
    public void CompleteDoorOperations()
    {
        foreach (var door in _openDoors)
        {
            if (door.Block == 0) continue;
            int v = Map.Walls[door.Block, door.Wall];
            int flg = door.State == 1 ? 0x10 : 0x20;
            while ((flg & Walls.WallFlags[v]) == 0) v += door.State;
            Map.Walls[door.Block, door.Wall] = (byte)v;
            Map.Walls[door.Block, door.Wall ^ 2] = (byte)v;
            door.Block = 0;
        }
    }

    /// <summary>Puts every timer back on the clock, as a run starting from a known moment does.</summary>
    public void ResetTimerSchedule()
    {
        foreach (var (id, timer) in _timers) timer.NextRun = Clock + (id == 0x11 ? 3 * TickLength : 0);
    }

    /// <summary>Runs a given number of engine ticks - the unit the timers actually count in.</summary>
    public void AdvanceTicks(int ticks)
    {
        for (int i = 0; i < ticks; i += 1)
        {
            _ticks += 1;
            Clock = _ticks * TickLength;   // a tick count, not a running sum
            RunDueTimers();
            // Not drained here: the engine queues the work and runs it when the host drains, which
            // is once a step - so a firing near the end of a step is carried out at the step
            // boundary, not the moment it came due.
        }
    }

    /// <summary>
    /// delayUntil: the engine does not wait out the remainder in one go - it waits in steps of at
    /// most 16 ms, and each step costs a whole tick on a clock that only ever holds whole ticks.
    /// Asking for two ticks' worth therefore costs two ticks on most frames and three on some, and
    /// that drift is not incidental: it is how long an animation takes.
    /// </summary>
    public void AdvanceClockTo(double moment)
    {
        for (int guard = 0; Clock < moment && guard < 200000; guard += 1)
            AdvanceClock(Math.Min(16, moment - Clock));
    }

    /// <summary>
    /// engine.mjs advanceClock: whole ticks only, however the caller chopped up the time. A timer
    /// fires once per tick, so the clock has to move in the unit the timers count in - and the
    /// clock is a tick count, never a running sum of fractions, or the two drift apart.
    /// </summary>
    public void AdvanceClock(double ms)
    {
        int ticks = Math.Max(1, (int)Math.Round(Math.Max(0, ms) / TickLength, MidpointRounding.AwayFromZero));
        for (int i = 0; i < ticks; i += 1)
        {
            _ticks += 1;
            Clock = _ticks * TickLength;
            RunDueTimers();
        }
    }

    /// <summary>
    /// Work a timer asked for but did not do itself. The engine queues the monster updates and the
    /// scene-animation scripts rather than running them inside the timer, and re-arms the timer
    /// straight away - so the work lands after the clock has moved on, and the next firing is
    /// measured from when the timer came due, not from when its work finished.
    /// </summary>
    private readonly Queue<Action> _queued = new();

    /// <summary>queueAsync: work the engine puts off until the next drain rather than doing now.</summary>
    public void Queue(Action work) => _queued.Enqueue(work);

    /// <summary>Runs the work the timers queued, as the engine's drain does.</summary>
    public void Drain()
    {
        int guard = 0;
        while (_queued.Count > 0 && guard++ < 10000) _queued.Dequeue()();
    }

    /// <summary>What the timers are doing, for when something that should tick does not.</summary>
    public string TimerReport()
    {
        var parts = new List<string>();
        foreach (var (id, timer) in _timers.OrderBy(t => t.Key))
            parts.Add($"{id:x}:{(timer.Enabled ? "on" : "off")}{(timer.PauseStart >= 0 ? "-paused" : "")}@{timer.NextRun:F0}");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// engine.mjs timersPaused: a harness holding every timer still while it measures one thing.
    /// The game never sets it; the parity checks that are about a spell or a screen do.
    /// </summary>
    public bool TimersPaused;

    private void RunDueTimers()
    {
        if (TimersPaused) return;
        foreach (var (id, timer) in _timers.OrderBy(t => t.Key))
        {
            if (!timer.Enabled || timer.Countdown < 0 || timer.PauseStart >= 0 || timer.NextRun > Clock) continue;
            if (id == 0) TimerProcessDoors();   // doors move inside the timer, as in the engine
            else if (id == 3) Board.TimerSpecialCharacterUpdate();
            else if (id == 4) Board.TimerProcessFlyingObjects();
            else if (id == 8) Gui?.TimerRegeneratePoints();
            else if (id == 9) Gui?.TimerUpdatePortraitAnimations(0);
            else if (id == 10) TimerUpdateLampState();
            else if (id == 11) Gui?.TimerFadeMessageText();
            else if (id == 0x10 || id == 0x11)
            {
                int half = id & 0x0f;
                for (int i = half; i < 30; i += 2)
                {
                    var monster = Board.Monsters[i];
                    _queued.Enqueue(() => Board.UpdateMonster(monster));
                }
            }
            else if (id >= 0x50 && id <= 0x52)
            {
                int func = 0x401 + (id & 0x0f);
                _queued.Enqueue(() => RunLevelScript(func, -1));
            }
            timer.NextRun = QuantiseToTick(Clock + timer.Countdown * TickLength);
        }
    }

    /// <summary>
    /// disableSysTimer(2) / enableSysTimer(2): a dialogue or a cutscene stops the world - the
    /// monsters do not take their turns while it is up, and the timers resume where they left off
    /// rather than firing everything they missed.
    /// </summary>
    /// <summary>timerDisable / timerEnable: one timer, by its id.</summary>
    public void DisableTimer(int id) { if (_timers.TryGetValue(id, out var t)) t.Enabled = false; }

    public void EnableTimer(int id)
    {
        if (!_timers.TryGetValue(id, out var t)) return;
        t.Enabled = true;
        t.NextRun = Clock;
    }

    /// <summary>
    /// engine.mjs sysTimerPaused: the game is held - a panel is up, or a script has the screen. The
    /// interface refuses to cast a spell through it.
    /// </summary>
    public bool SysTimerPaused { get; private set; }

    /// <summary>
    /// uiPauseTimers: the same eight timers stop, but SysTimerPaused stays as it was, so the host's
    /// own windows keep working while the world waits. The host pauses with this, the engine with
    /// PauseSysTimers, and neither undoes the other's hold.
    /// </summary>
    public void PauseTimers(bool pause)
    {
        foreach (int id in new[] { 0x10, 0x11, 3, 4, 8, 9, 10, 11 })
        {
            if (!_timers.TryGetValue(id, out var timer)) continue;
            if (pause) { if (timer.PauseStart < 0) timer.PauseStart = Clock; }
            else if (timer.PauseStart >= 0) { timer.NextRun += Clock - timer.PauseStart; timer.PauseStart = -1; }
        }
    }

    public void PauseSysTimers(bool pause)
    {
        SysTimerPaused = pause;
        foreach (int id in new[] { 0x10, 0x11, 3, 4, 8, 9, 10, 11 })
        {
            if (!_timers.TryGetValue(id, out var timer)) continue;
            if (pause) { if (timer.PauseStart < 0) timer.PauseStart = Clock; }
            else if (timer.PauseStart >= 0) { timer.NextRun += Clock - timer.PauseStart; timer.PauseStart = -1; }
        }
    }

    /// <summary>
    /// How many spells the scroll holds. The parchment is 120 pixels tall: a 15-pixel head, a
    /// 14-pixel foot and 9 pixels a row, so ten rows is what fits. The array used to be 8 while
    /// every loop that drew it stopped at 7, which left an eighth spell bought from the imp in a
    /// slot nothing showed and nothing could select.
    /// </summary>
    public const int SpellSlots = 10;

    /// <summary>
    /// The spell slots the scroll lists, -1 where a spell has not been learned. A new game starts
    /// with spark in the first slot.
    /// </summary>
    public readonly int[] AvailableSpells = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };

    /// <summary>getGlobalVar/setGlobalVar 8: what the interface is doing - 1 means a box is up.</summary>
    public int UpdateFlags;

    /// <summary>
    /// initSceneWindowDialogue: the panel a conversation happens in. The engine saves the ground
    /// it covers, slides it up, and hands the controls to the dialogue until it is dismissed.
    /// </summary>
    public void InitSceneWindowDialogue(int controlMode)
    {
        NeedSceneRestore = true;
        PauseSysTimers(true);   // disableSysTimer(2)
        UpdateFlags |= 3;
        Text?.SetupField(true);
        Text?.ExpandField();
        SetupScreenDims();
        Gui?.DisableControls(controlMode);
    }

    /// <summary>restoreAfterSceneWindowDialogue: the field goes back down and the party may act.</summary>
    public void RestoreAfterSceneWindowDialogue(bool redraw)
    {
        Gui?.EnableControls();
        Text?.SetupField(false);
        UpdateFlags &= 0xffdf;
        Gui?.EnableDefaultPlayfieldButtons();
        // The conversation had its own animations; they go with it, or they keep painting the
        // speaker over the view.
        if (Tim != null) for (int i = 0; i < 6; i += 1) Tim.FreeAnimStruct(i);
        UpdateFlags = 0;
        if (redraw)
        {
            // A speaker's close-up loads its own palette. Without putting the level's back the
            // scene keeps the talker's colours - which is how a lit corridor came out white.
            if (Screen.FadeFlag != 2) Screen.FadeClearSceneWindow(10);
            Gui?.DrawPlayField();
            SetPaletteBrightness(Screen.Palette(0), Brightness, LampEffect);
            Screen.FadeFlag = 0;
        }
        NeedSceneRestore = false;
        PauseSysTimers(false);   // enableSysTimer(2)
    }

    /// <summary>
    /// loadNewLevel: the level being left keeps its state, the party is put where the script said,
    /// and the new level is built from its own scripts.
    /// </summary>
    public void LoadNewLevel(int level, int block, int direction)
    {
        CompleteDoorOperations();
        PartyBlock = block & 0x3ff;
        PartyDirection = direction & 3;
        Party.MoveTo(PartyBlock);
        Party.Direction = PartyDirection;
        Load(level);
    }

    /// <summary>setupScreenDims: the text area is taller when the game is showing text.</summary>
    public void SetupScreenDims()
    {
        bool text = Text?.TextEnabled ?? true;
        Screen.ModifyScreenDim(4, 11, 124, 28, text ? 45 : 9);
        Screen.ModifyScreenDim(5, 85, 123, 233, text ? 54 : 18);
    }

    /// <summary>Set while a dialogue or a special scene is up.</summary>
    public bool NeedSceneRestore;

    /// <summary>The language the game data is in, as the file extension spells it.</summary>
    public string LanguageExt = "ENG";

    /// <summary>The strings the current level (or conversation) brought with it.</summary>
    public byte[] LevelLangFile;

    /// <summary>The cutscene interpreter, when there is one.</summary>
    public TimInterpreter Tim;

    /// <summary>The TIM scripts the level's scripts have loaded, by slot.</summary>
    public readonly TimScript[] ActiveTim = new TimScript[10];

    /// <summary>The text displayer, when there is one: the scripts talk through it.</summary>
    /// <summary>
    /// The message line. Attached after the level loads, like the Gui, so what it needs from the
    /// loader is wired here rather than during the load - where it would be wired to nothing.
    /// </summary>
    public TextDisplayer Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_text != null) _text.EnableFadeTimer = () => EnableTimer(11);
        }
    }

    private TextDisplayer _text;

    /// <summary>The interface, when there is one: a script can enable and disable the controls.</summary>
    /// <summary>
    /// The interface. It is attached after the level is loaded, so the spells are wired here rather
    /// than in Load - a spell whose procedure was never filled in does nothing at all.
    /// </summary>
    public Gui Gui
    {
        get => _gui;
        set { _gui = value; WireSpellProcs(); }
    }

    private Gui _gui;

    /// <summary>The party the scripts move around; the loader makes it, because the scripts need it.</summary>
    public Party Party { get; private set; }
    public int PartyDirection;

    /// <summary>ITEM.INF: the script every item runs when it is picked up, equipped or used.</summary>
    public EmcScript ItemScript { get; private set; }

    /// <summary>LEVEL&lt;n&gt;.INF: the script that runs while the party walks the level.</summary>
    public EmcScript LevelScript { get; private set; }
    public int ScriptDirection;
    public readonly List<int> UnhandledLevelOpcodes = new();

    /// <summary>
    /// What a level looks like when the party leaves it: the walls as the scripts left them and the
    /// monsters as the fight left them. A level is only built from its script once - a revisit
    /// restores this instead, which is also why the script's "first visit" half must not run again.
    /// </summary>
    private sealed class LevelTempData
    {
        public byte[,] Walls;
        public byte[] Flags;
        public Monster[] Monsters;
    }

    private readonly Dictionary<int, LevelTempData> _tempData = new();
    public readonly List<int> UnhandledOpcodes = new();


    /// <summary>
    /// ONETIME.INF, the script that fills the item property table. The engine runs it once at
    /// startup, before any level exists, so the items a level creates have properties to point at.
    /// </summary>
    public void LoadItemProperties()
    {
        var script = EmcScript.Decode(_res.Get("ONETIME.INF"));
        RunFunction(script, 0);
        ItemScript = EmcScript.Decode(_res.Get("ITEM.INF"));
    }

    /// <summary>
    /// runItemScript: what an item does to the character holding it. This is how a sword's might and
    /// a shirt's protection reach the character's own numbers - without it every blow lands for a
    /// different amount.
    /// </summary>
    public void RunItemScript(int charNum, int item, int flags, int next, int reg4)
    {
        int func = item != 0 ? Items.Properties[Items.InPlay[item].ItemPropertyIndex].ItemScriptFunc : 3;
        if (func == 0xff || ItemScript == null) return;
        var state = new EmcState(ItemScript);
        if (!state.Start(func)) return;
        state.Regs[0] = (short)flags;
        state.Regs[1] = (short)charNum;
        state.Regs[2] = (short)item;
        state.Regs[3] = (short)next;
        state.Regs[4] = (short)reg4;
        state.Regs[5] = (short)(item != 0 ? Items.InPlay[item].ItemPropertyIndex : 0);
        state.Regs[6] = (short)Level;
        _calls = 0;
        Run(state);
    }

    /// <summary>
    /// A new game, as far as the scene is concerned: the party's items, then level 1, which is where
    /// the game starts and where the first level items are created. Both matter to a later level:
    /// item slots are handed out in order and never reset, and the wall tables are never cleared.
    /// </summary>
    public void StartNewGame(int charSelection = 0)
    {
        Items.StartNewGame(charSelection, Party_);
        AvailableSpells[0] = 0;   // spark
        SetupScreenDims();        // the text areas the game starts with
        // Every item the character carries runs its script, which is what puts its might and its
        // protection into the character's own numbers.
        for (int i = 0; i < 11; i += 1) if (Party_[0].Items[i] != 0) RunItemScript(0, Party_[0].Items[i], 0x80, 0, 0);
        int party = PartyBlock;
        PartyBlock = 557;   // where the game puts the party on level 1
        _rng = new Rng(0x1EA0);   // the start of the game rolls its own dice, before the seeded run
        // A new game has no history. A host that showed the world before the party existed - a title
        // screen behind the real playfield, say - has already loaded level 1 once, and Load() would
        // then treat this as a *revisit*: it restores that earlier state and skips the first-visit
        // half of the level's script, the half that places the monsters and plays the opening. The
        // gate guard was the visible casualty: no close-up, no stepping aside, and not solid.
        _tempData.Clear();
        Load(1);
        PartyBlock = party;
        _rng = new Rng(_seed);    // the level being compared starts from the recorded seed
    }

    public void Load(int level)
    {
        if (Level != 0) GenerateTempData();   // the level being left keeps its state
        // resetItems: and its floor is written down before the block table goes, or every item the
        // party dropped on it is lost. Level is still the level being left here.
        if (Level != 0 && Board != null && Map != null) Board.ResetItems(Level, true);
        Level = level;
        Flags[73] |= 0x08;
        _res.LoadPak($"L{level:00}.PAK");
        var wllFile = _res.Get($"LEVEL{level}.WLL");
        // One table for the whole game: a new level overwrites the wall types it lists, and the
        // rest keep what the last level that listed them left behind.
        if (Walls == null) Walls = WallData.Decode(wllFile);
        else { Walls.Records.Clear(); WallData.DecodeInto(Walls, wllFile); }
        // loadLevelWallData: the wall set's own decoration files, then every wall type that names a
        // decoration record gets it assigned - in the order the table lists them, which is the order
        // the ids come out in.
        int set = wllFile[0] | (wllFile[1] << 8);
        Decorations = new Decorations(_res);
        Decorations.LoadSet(StaticData.Names("LevelShpList")[set], StaticData.Names("LevelDatList")[set], false);
        foreach (var (wall, raw) in Walls.Records)
            Walls.ShapeMap[wall] = raw > 0 ? (short)Decorations.Assign(raw) : (short)(raw & 0xff);
        Map = new BlockMap();
        Board = new MonsterBoard(Walls, Map, _rng, Items, Party_) { PartyBlock = PartyBlock };
        // Every level gets a new board, so the engine's own hooks are wired here, before the level's
        // scripts run - not once by a host, which lost them at the first staircase: from then on no
        // "monster entered this square" or "monster killed" script ever ran.
        Board.OnMonsterEnteredBlock = (block, id) => RunLevelScript(block, 0x800, -1, id);
        Board.OnMonsterKilled = id => { MonsterKilled?.Invoke(id); RunLevelScript(0x404, -1, id, id); };
        Board.ClipVisibleFrom = (block, dir, index) => ClipVisibleFrom != null && ClipVisibleFrom(block, dir, index);
        WireCharacterEffects();
        WireSpellProcs();   // the board is new: without this no spell did anything after the first level
        Board.OnItemScript = (charNum, item, flags) => RunItemScript(charNum, item, flags, 0, 0);
        Party = new Party(Walls, Map, PartyBlock < 0 ? 0 : PartyBlock, PartyDirection, Board);
        Board.Party = Party;
        // The views (scene composer, automap) follow now, before the level's scripts can use them.
        LevelBuilt?.Invoke();
        // loadLevelWallData also opens the decoration SHP/DAT of the wall set; the shapes are not
        // needed for the wall layer, so only the names are tracked (see UnhandledOpcodes for the
        // rest of the engine that is not here yet).
        bool visited = _tempData.ContainsKey(level);
        LevelWasVisited = visited;
        var script = EmcScript.Decode(_res.Get($"LEVEL{level}.INI"));
        RunFunction(script, 0);
        // A level with no saved state is about to be built from scratch, and its .INI will create its
        // items all over again. Anything still tagged with this level is left from a visit that was
        // thrown away - the Imp's Pit discards one every time it borrows a level - and keeping those
        // records only fills the world's fixed item table until the next level cannot allocate.
        // What the party carries is never tagged with a level, so it is untouched by this.
        if (!visited)
        {
            for (int it = 1; it < Items.InPlay.Length; it += 1)
            {
                var item = Items.InPlay[it];
                if (item == null || item.ItemPropertyIndex == 0 || item.Level != level) continue;
                Items.Delete(it);
            }
        }
        if (!visited) RunFunction(script, 1);   // the "first visit" half: monsters, items, the intro
        else RestoreBlockTempData(level);
        // loadLevel: the level's own script comes next, then the items filed away here, then the
        // block the party stands in is cleared of monsters.
        RunInfScript();
        Board.AddLevelItems(level);
        // What the level designer added, and then what a randomizer seed does with the result.
        ApplyModObjects();
        ApplyRandomizer();
        if (PartyBlock >= 0) DeleteMonstersFromBlock(PartyBlock);
        // The monster layout as the level's script made it, on a first visit only: a later visit was
        // restored from what was saved, which may be a level the party already cleared.
        if (!visited && !MonsterSpawns.ContainsKey(level))
            MonsterSpawns[level] = Board.Monsters.Select(MonsterBoard.CopyMonster).ToArray();
        InvalidateDrawOrder();
    }

    /// <summary>
    /// Every block caches the order its objects are drawn in, keyed by the facing it was last drawn
    /// from. restoreBlockTempData leaves that cache claiming "facing 0, nothing to draw", which hides
    /// a monster standing in front of the party until something else invalidates it - so the caches
    /// are dropped when a level finishes loading. scripts/dump_frame.mjs does the same before it
    /// records, and the two have to agree.
    /// </summary>
    /// <summary>
    /// Bumped whenever something that is drawn has moved or changed. A host redrawing a software
    /// rasterised scene needs to know when it may leave the last one alone.
    /// </summary>
    public int DrawSerial { get; private set; }

    /// <summary>setMusicTrack: which theme the level asked for, and a way for the host to hear it.</summary>
    public int MusicTrack { get; private set; } = -1;

    /// <summary>
    /// timerUpdateLampState: the lantern burns its oil, one unit every six seconds, while it is lit
    /// and the party has it and it has not been switched off.
    /// </summary>
    public void TimerUpdateLampState()
    {
        if ((Flags[31] & 0x08) != 0 && (Flags[31] & 0x04) != 0
            && Brightness != 0 && LampOilStatus != 0 && !LampSwitchedOff) LampOilStatus -= 1;
    }

    /// <summary>getGlobalVar 13: whether the spoken lines are on, which a script asks before waiting.</summary>
    public Func<bool> Speech;

    /// <summary>
    /// monsterSpawns: the monster set each level's own script created when it was first entered, kept
    /// so it can be put back. Only a first visit records one, and nothing else writes to it.
    /// </summary>
    public readonly Dictionary<int, Monster[]> MonsterSpawns = new();

    /// <summary>The save file's copy of it, on the way back in.</summary>
    public void SetMonsterSpawns(int level, Monster[] set) => MonsterSpawns[level] = set;

    /// <summary>setGlobalVar 12: something is draining the party's magic rather than restoring it.</summary>
    public int DrainMagic;

    /// <summary>
    /// uiRespawnMonsters: the level's monsters put back as its script first made them. Returns how
    /// many are standing afterwards, or 0 when the moment is wrong or there is nothing to put back.
    /// </summary>
    public int RespawnMonsters()
    {
        if ((UpdateFlags & 3) != 0 || NeedSceneRestore || SysTimerPaused) return 0;
        // A pit floor is generated over a borrowed level: putting that level's own monsters back
        // would take the floor's master with it, and an objective asking for the master would then
        // read as already done.
        if (InDungeon) return 0;
        int Living() => Board.Monsters.Count(m => m.Properties != null && m.HitPoints > 0 && m.Mode < 13);
        int before = Living();
        // The living ones come off their blocks first, so nothing is left pointing at them.
        foreach (var m in Board.Monsters) if (m != null && m.Block != 0) Board.TakeMonsterOffBlock(m);

        MonsterSpawns.TryGetValue(Level, out var spawn);
        if (spawn != null && spawn.Any(m => m.HitPoints > 0))
        {
            for (int i = 0; i < Board.Monsters.Length && i < spawn.Length; i += 1)
            {
                var m = MonsterBoard.CopyMonster(spawn[i]);
                if (m.Type >= 0 && m.Type < Board.Properties.Length) m.Properties = Board.Properties[m.Type];
                Board.Monsters[i] = m;
                if (m.Block != 0 && m.Mode != 14 && m.HitPoints > 0) Board.AssignMonsterToBlock(m.Block, m.Id | 0x8000);
            }
        }
        else if (RespawnFromScript() == 0) return 0;

        if (PartyBlock >= 0) DeleteMonstersFromBlock(Party?.Block ?? PartyBlock);
        GenerateTempData();   // the fresh set is what a later visit restores
        InvalidateDrawOrder();
        int after = Living();
        OnHostMessage?.Invoke($"Monsters respawned: {after} on this level (was {before}).");
        return after;
    }

    /// <summary>
    /// uiRespawnFromScript: no snapshot to hand, so the level's own spawn script runs again - and
    /// everything it touches except the monsters is put back exactly as it was.
    /// </summary>
    public int RespawnFromScript()
    {
        if (!_res.Exists($"LEVEL{Level}.INI"))
        {
            OnHostMessage?.Invoke("This level has no spawn script.");
            return 0;
        }
        var walls = (byte[,])Map.Walls.Clone();
        var flags = (byte[])Map.Flags.Clone();
        var gameFlags = (byte[])Flags.Clone();
        var items = Items.InPlay.Select(it => it == null ? null : new Item
        {
            NextAssignedObject = it.NextAssignedObject, NextDrawObject = it.NextDrawObject,
            FlyingHeight = it.FlyingHeight, Block = it.Block, X = it.X, Y = it.Y, Level = it.Level,
            ItemPropertyIndex = it.ItemPropertyIndex, ShpCurFrameFlg = it.ShpCurFrameFlg, Wear = it.Wear,
        }).ToArray();

        for (int i = 0; i < Board.Monsters.Length; i += 1) Board.Monsters[i] = new Monster { Id = i };
        RunFunction(EmcScript.Decode(_res.Get($"LEVEL{Level}.INI")), 1);

        Array.Copy(gameFlags, Flags, Flags.Length);
        for (int i = 0; i < items.Length && i < Items.InPlay.Length; i += 1) if (items[i] != null) Items.InPlay[i] = items[i];
        for (int b = 0; b < 1024; b += 1)
        {
            for (int d = 0; d < 4; d += 1) Map.Walls[b, d] = walls[b, d];
            Map.Flags[b] = flags[b];
            Map.AssignedObjects[b] = 0;
            Map.DrawObjects[b] = 0;
        }
        Board.AddLevelItems(Level);   // the level's items back on their blocks, as after a level load
        foreach (var m in Board.Monsters)
            if (m != null && m.Block != 0 && m.Mode != 14 && m.HitPoints > 0)
                Board.AssignMonsterToBlock(m.Block, m.Id | 0x8000);
        MonsterSpawns[Level] = Board.Monsters.Select(MonsterBoard.CopyMonster).ToArray();
        return 1;
    }

    /// <summary>Whether the level that just loaded is one the party had already been in.</summary>
    public bool LevelWasVisited { get; private set; }

    /// <summary>A line for the host's own log, in its own words rather than the game's.</summary>
    public Action<string> OnHostMessage;

    /// <summary>timerSetCountdown: how often a timer comes round, changed while it runs.</summary>
    public void SetTimerCountdown(int id, int countdown)
    {
        if (_timers.TryGetValue(id, out var t)) t.Countdown = countdown;
    }

    /// <summary>blockDoor: the door a script is holding shut.</summary>
    public int BlockDoor;

    /// <summary>The language the game's strings are in.</summary>
    public int Lang;

    public Action<int> OnMusicTrack;

    /// <summary>A new level's walls, block map, board and party exist; its scripts have not run yet.</summary>
    public Action LevelBuilt;

    /// <summary>A monster died (before its level script runs): the host's bestiary, drops, log.</summary>
    public Action<int> MonsterKilled;

    /// <summary>The scene pass's line-of-sight test, for the monsters' ranged attacks.</summary>
    public Func<int, int, int, bool> ClipVisibleFrom;

    /// <summary>A sound a script asked for; the host plays it.</summary>
    public Action<int> OnSoundEffect;

    /// <summary>
    /// What a blow did, for the host's combat log: (what, attacker, target, damage). "miss",
    /// "damage-monster", "damage-hero" and "kill" - the four the browser page builds its log from.
    /// </summary>
    public Action<string, int, int, int> OnCombat;

    /// <summary>
    /// A sound belonging to a place rather than a moment: (sound id, volume 0..240). A host with
    /// nothing wired here hears the game as it was before - every sound at full volume.
    /// </summary>
    public Action<int, int> OnEnvironmentalSound;

    /// <summary>A sound bank a script asked to be loaded.</summary>
    public Action<int> OnLoadSoundFile;

    /// <summary>The game is won: the host plays the finale.</summary>
    public Action OnEndSequence;

    /// <summary>getItemOnPos walks a block's objects from where it last stopped.</summary>
    public int EmcLastItem = -1;

    /// <summary>assignCustomSfx: sounds a level renames for itself.</summary>
    public readonly Dictionary<int, string> CustomSfx = new();

    /// <summary>Opens one of the game's .WSA animations, for the scenes that play one.</summary>
    public WsaPlayer OpenWsa(string name) => OpenWsa(name, 0, Screen.Palette(3));

    /// <summary>
    /// openWsa with the flags the spells pass. Flag 2 decodes straight to the page rather than to an
    /// offscreen buffer, and a spell passes no palette buffer at all, so palette 3 is left alone.
    /// </summary>
    public WsaPlayer OpenWsa(string name, int flags, byte[] palette = null)
    {
        if (!_res.Exists(name)) return null;
        var wsa = new WsaPlayer(Screen) { Name = name.ToUpperInvariant() };
        wsa.Open(_res.Get(name), flags, palette);
        return wsa;
    }

    /// <summary>loadPaletteFile: a .COL from the archive, or black if the game has no such file.</summary>
    public byte[] LoadPaletteFile(string name)
    {
        var pal = new byte[768];
        if (_res.Exists(name)) Array.Copy(_res.Get(name), pal, Math.Min(768, _res.Get(name).Length));
        return pal;
    }

    /// <summary>
    /// The spoken lines. A conversation asks for a voice first and only writes the line out when
    /// there is none or the player wants both - which is why a port without this hook is silent.
    /// </summary>
    public Func<int, int, bool> PlayCharacterSpeech;
    public Action StopSpeech;
    public Action<int, int> QueueSpeech;
    public Func<bool> TextEnabled = () => true;

    public void BumpDrawSerial() => DrawSerial += 1;

    public void InvalidateDrawOrder()
    {
        for (int b = 0; b < 1024; b += 1) Map.Direction[b] = 5;
    }

    /// <summary>
    /// Put a level's saved state in place without having visited it - what loading a saved game
    /// does. The level is then "visited", so loading it restores this instead of building it.
    /// </summary>
    public void SetTempData(int level, byte[,] walls, byte[] flags, Monster[] monsters)
    {
        var full = new Monster[30];
        for (int i = 0; i < 30; i += 1) full[i] = i < monsters.Length ? monsters[i] : new Monster { Id = i, Mode = 0x10 };
        _tempData[level] = new LevelTempData { Walls = walls, Flags = flags, Monsters = full };
    }

    /// <summary>generateTempData: remember the level the party is leaving.</summary>
    public void GenerateTempData()
    {
        if (Map == null || Board == null) return;
        var walls = new byte[1024, 4];
        var flags = new byte[1024];
        for (int b = 0; b < 1024; b += 1)
        {
            for (int i = 0; i < 4; i += 1) walls[b, i] = Map.Walls[b, i];
            flags[b] = Map.Flags[b];
        }
        _tempData[Level] = new LevelTempData
        {
            Walls = walls,
            Flags = flags,
            Monsters = Board.Monsters.Select(Copy).ToArray(),
        };
    }

    private static Monster Copy(Monster m) => new()
    {
        Id = m.Id, Block = m.Block, X = m.X, Y = m.Y, Facing = m.Facing, Direction = m.Direction, Type = m.Type,
        Mode = m.Mode, Flags = m.Flags, HitPoints = m.HitPoints, NumDistAttacks = m.NumDistAttacks,
        DistAttackTick = m.DistAttackTick, FlyingHeight = m.FlyingHeight, CurrentSubFrame = m.CurrentSubFrame,
        ShiftStep = m.ShiftStep, FightCurTick = m.FightCurTick, DamageReceived = m.DamageReceived,
        DestX = m.DestX, DestY = m.DestY, DestDirection = m.DestDirection, Properties = m.Properties,
    };

    /// <summary>Which levels have been visited, as the save file records it.</summary>
    public int HasTempDataFlags
    {
        get
        {
            int flags = 0;
            foreach (int level in _tempData.Keys) if (level >= 1 && level <= 29) flags |= 1 << (level - 1);
            return flags;
        }
    }

    /// <summary>Marks levels as visited without having been there - what a loaded save does.</summary>
    public void SetVisitedLevels(int flags)
    {
        for (int level = 1; level <= 29; level += 1)
        {
            bool visited = (flags & (1 << (level - 1))) != 0;
            if (visited && !_tempData.ContainsKey(level))
                _tempData[level] = new LevelTempData { Walls = new byte[1024, 4], Flags = new byte[1024], Monsters = Array.Empty<Monster>() };
            else if (!visited) _tempData.Remove(level);
        }
    }

    /// <summary>What is remembered of a level right now, so it can be put back untouched: the
    /// Imp's Pit borrows a level, rewrites all 1024 of its blocks, and has to hand it back.</summary>
    public object TempDataFor(int level) => _tempData.GetValueOrDefault(level);

    /// <summary>Puts a borrowed level's memory back - or forgets it again, if it had none.</summary>
    public void RestoreTempData(int level, object temp, bool hadFlag)
    {
        if (hadFlag && temp is LevelTempData data) _tempData[level] = data;
        else _tempData.Remove(level);
    }
    /// <summary>The saved state of every level visited: (level number, what it looked like).</summary>
    public IEnumerable<(int Level, LevelState State)> TempData()
    {
        foreach (var (level, t) in _tempData.OrderBy(e => e.Key))
            yield return (level, new LevelState { Walls = t.Walls, Flags = t.Flags, Monsters = t.Monsters });
    }

    /// <summary>What a save records about a level that was visited.</summary>
    public sealed class LevelState
    {
        public byte[,] Walls;
        public byte[] Flags;
        public Monster[] Monsters;
    }

    /// <summary>
    /// saveState tags the items lying on the current level before writing, because a level only
    /// tags them when the party leaves it - and only the head of a block's chain carries the tag.
    /// </summary>
    public void TagFloorItemsForSave()
    {
        for (int b = 0; b < 1024; b += 1)
        {
            int id = Map.AssignedObjects[b];
            while ((id & 0x8000) != 0) id = Board.Monsters[id & 0x7fff].NextAssignedObject;
            bool head = true;
            var seen = new HashSet<int>();
            for (; id != 0 && !seen.Contains(id); id = Items.InPlay[id].NextAssignedObject, head = false)
            {
                seen.Add(id);
                Items.InPlay[id].Level = head ? Level : -1;
                Items.InPlay[id].Block = b;
            }
        }
    }

    /// <summary>The palette brightness the level script asked for.</summary>
    public int Brightness;

    /// <summary>The lamp's own dimming, or -1 when there is no lamp effect.</summary>
    public int LampEffect = -1;

    /// <summary>
    /// generateBrightnessPalette: the palette the display actually shows is the level's palette
    /// dimmed by the brightness setting - and, where the level has one, by the lamp burning down.
    /// Only the first 128 colours are dimmed; the interface keeps its own.
    /// </summary>
    private static int Sum384(byte[] p) { int t = 0; for (int i = 0; i < 384 && i < p.Length; i += 1) t += p[i]; return t; }

    public void GenerateBrightnessPalette(byte[] src, byte[] dst, int brightness, int modifier)
    {
        Array.Copy(src, dst, Math.Min(src.Length, dst.Length));
        Screen.LoadSpecialColors(dst);
        brightness = (8 - brightness) << 5;
        if (modifier >= 0 && modifier < 8 && (Flags[31] & 0x08) != 0)
        {
            brightness = 256 - ((((modifier & 0xfffe) << 5) * (256 - brightness)) >> 8);
            if (brightness < 0) brightness = 0;
        }
        for (int i = 0; i < 384; i += 1) dst[i] = (byte)(((dst[i] * brightness) >> 8) & 0xff);
    }

    /// <summary>setPaletteBrightness: what the level looks like once it is up, faded into view.</summary>
    public void SetPaletteBrightness(byte[] src, int brightness, int modifier)
    {
        GenerateBrightnessPalette(src, Screen.Palette(1), brightness, modifier);
        Screen.FadePalette(Screen.Palette(1), 5, AdvanceClock);
        Screen.FadeFlag = 0;
    }


    /// <summary>restoreBlockTempData: put a visited level back the way it was left.</summary>
    private void RestoreBlockTempData(int level)
    {
        if (!_tempData.TryGetValue(level, out var t)) return;
        for (int b = 0; b < 1024; b += 1)
        {
            Map.AssignedObjects[b] = 0;
            Map.DrawObjects[b] = 0;
            Map.Direction[b] = 0;
            for (int i = 0; i < 4; i += 1) Map.Walls[b, i] = t.Walls[b, i];
            Map.Flags[b] = t.Flags[b];
        }
        for (int i = 0; i < 30; i += 1)
        {
            var m = Copy(t.Monsters[i]);
            for (int k = 0; k < 4; k += 1) m.EquipmentShapes[k] = t.Monsters[i].EquipmentShapes[k];
            // A monster from a saved game has no properties object of its own: the level script has
            // just rebuilt the table it points into.
            if (m.Properties != null || m.HitPoints > 0 || m.Block != 0)
                m.Properties = m.Type >= 0 && m.Type < Board.Properties.Length ? Board.Properties[m.Type] : null;
            Board.Monsters[i] = m;
            if (m.Block != 0 && m.Mode != 14 && m.HitPoints > 0) Board.AssignMonsterToBlock(m.Block, m.Id | 0x8000);
        }
    }

    /// <summary>runInfScript: load LEVEL&lt;n&gt;.INF and run its entry function.</summary>
    public void RunInfScript()
    {
        LevelScript = _res.Exists($"LEVEL{Level}.INF") ? EmcScript.Decode(_res.Get($"LEVEL{Level}.INF")) : null;
        RunLevelScript(0x400, -1);
    }

    /// <summary>
    /// runLevelScriptCustom: a block's own script function, guarded by the event mask stored in the
    /// word before it. This is what fires when the party steps onto a square, turns, or leaves one.
    /// </summary>
    public void RunLevelScript(int block, int flags, int charNum = -1, int item = 0, int reg3 = 0, int reg4 = 0)
    {
        if (LevelScript == null) return;
        _calls = 0;
        var state = new EmcState(LevelScript);
        if (!state.Start(block)) return;
        state.Regs[0] = (short)flags;
        state.Regs[1] = (short)charNum;
        state.Regs[2] = (short)item;
        state.Regs[3] = (short)reg3;
        state.Regs[4] = (short)reg4;
        state.Regs[5] = (short)block;
        state.Regs[6] = (short)ScriptDirection;
        if ((state.EntryFlags() & flags) == 0) return;
        Run(state);
    }

    /// <summary>
    /// A character swinging at what is in front of them. The damage is not dealt here: the weapon's
    /// own script does it, which is why an unarmed hand and a mace differ without the engine knowing
    /// anything about either.
    /// </summary>
    public void CharacterAttack(int charNum)
    {
        if ((Party_[charNum].Flags & 0x314c) != 0) return;
        int target = Board.GetNearestMonsterFromCharacter(charNum, Party.Block, Party.Direction);
        int s = 0;
        for (int i = 0; i < 4; i += 1)
        {
            if (Party_[charNum].Items[i] == 0) continue;
            RunItemScript(charNum, Party_[charNum].Items[i], 0x400, target, s);
            RunLevelScript(Party.Block, 0x400, charNum, Party_[charNum].Items[i], target, s);
            s -= 10;
        }
        if (s == 0)
        {
            RunItemScript(charNum, 0, 0x400, target, s);
            RunLevelScript(Party.Block, 0x400, charNum, 0, target, s);
        }
        // clickedAttackButton's tail: the hero is swinging, and cannot swing again until the
        // recovery their skill earns them has counted down.
        int recovery = Party_[charNum].WeaponHit != 0 ? 4 : Board.CalcCharacterSkillLevel(charNum, 8) + 4;
        if (ItemEquipped(charNum, 230)) recovery >>= 1;
        Party_[charNum].Flags |= 4;
        Gui?.HighlightPortraitFrame(charNum);
        Party_[charNum].AttackCooldownTotal = recovery;
        Board.SetCharacterUpdateEvent(charNum, 1, recovery, true);
    }

    /// <summary>
    /// moveParty, with the four script events the engine fires around a step and the slide that
    /// carries the view there. Which slide depends on how the step was asked for: 0 is a step
    /// forward, 1 a step back, 2 a sidestep left and 3 a sidestep right.
    /// </summary>
    public bool MoveParty(int direction, int moveKind = 0)
    {
        int opos = Party.Block;
        int npos = Party.CalcNewBlockPosition(opos, direction);
        if (!Party.CheckBlockPassability(npos, direction))
        {
            NotifyBlockNotPassable(moveKind == 0);
            return false;
        }
        ScriptDirection = direction;
        Party.MoveTo(npos);
        SceneDefaultUpdate = 1;
        Flags[73] &= 0xfd;
        RunLevelScript(opos, 4);
        RunLevelScript(npos, 1);
        if ((Flags[73] & 0x02) == 0)
        {
            Gui?.InitTextFading(2, 0);
            if (SceneDefaultUpdate != 0 && Gui != null)
            {
                switch (moveKind)
                {
                    case 0: Gui.MovePartySmoothScrollUp(2); break;
                    case 1: Gui.MovePartySmoothScrollDown(2); break;
                    case 2: Gui.MovePartySmoothScrollLeft(1); break;
                    default: Gui.MovePartySmoothScrollRight(1); break;
                }
            }
            else Gui?.DrawScene(0);
            if (npos == Party.Block)
            {
                RunLevelScript(opos, 8);
                RunLevelScript(npos, 2);
                if (Map.Walls[npos, 0] == 0x1a) for (int i = 0; i < 4; i += 1) Map.Walls[npos, i] = 0;
            }
        }
        Gui?.Automap?.UpdateAutoMap(Party.Block);
        return true;
    }

    /// <summary>notifyBlockNotPassable: the lurch, the message and the thud of walking into a wall.</summary>
    public void NotifyBlockNotPassable(bool withScroll)
    {
        if (withScroll) Gui?.MovePartySmoothScrollBlocked(2);
        Text?.PrintMessage(0x8002, Gui?.LangString(0x403f) ?? "");
        OnSoundEffect?.Invoke(19);
    }

    public void TurnParty(int amount)
    {
        Party.Turn(amount);
        Gui?.InitTextFading(2, 0);
        Gui?.MovePartySmoothScrollTurn(1);
        RunLevelScript(Party.Block, 0x4000);
    }

    private void RunFunction(EmcScript script, int func)
    {
        var state = new EmcState(script);
        if (!state.Start(func)) return;
        _calls = 0;
        Run(state);
    }

    /// <summary>
    /// A script that stopped to wait for the player. Nothing else runs while one is held: the
    /// engine is in a conversation, and the next frame carries it on from the same instruction.
    /// </summary>
    public EmcState PendingScript { get; private set; }

    private void Run(EmcState state)
    {
        // Only the waiting script itself gives up its place. Any other script that happens to run
        // (a click, a monster stepping on a square) must not erase it: in script.mjs the waiting
        // one is an await, and nothing else can end it.
        if (PendingScript == state) PendingScript = null;
        try { Emc.Run(state, SysFunc); }
        catch (ScriptStuckException) { ScriptsCutOff += 1; return; }
        if (state.Yield && state.Running) PendingScript = state;
    }

    /// <summary>One more go at the script that is waiting. The host calls this once a frame.</summary>
    public void ResumeScript()
    {
        var state = PendingScript;
        if (state == null) return;
        _calls = 0;
        Run(state);
    }

    /// <summary>Set to watch what the scripts do: id, name-less, with its first three arguments.</summary>
    public Action<int, int, int, int, int> Trace;

    /// <summary>How many system calls one script run may make before it is cut off as stuck.</summary>
    public const int ScriptCallBudget = 200_000;
    private int _calls;
    private int _scriptCharacterCycle;

    /// <summary>itemEquipped: is this character wearing or holding one of these?</summary>
    public bool ItemEquipped(int charNum, int itemType)
    {
        if (charNum < 0 || charNum > 3) return false;
        var c = Party_[charNum];
        if (!c.Active) return false;
        for (int i = 0; i < 11; i += 1)
        {
            int item = c.Items[i];
            if (item != 0 && Items.InPlay[item].ItemPropertyIndex == itemType) return true;
        }
        return false;
    }

    public int ScriptsCutOff { get; private set; }

    private int SysFunc(int id, EmcState s)
    {
        if (++_calls > ScriptCallBudget) throw new ScriptStuckException();
        int result = SysFuncBody(id, s);
        if (!s.Yield) Trace?.Invoke(id, s.Arg(0), s.Arg(1), s.Arg(2), result);   // a wait is one call, not one per frame
        return result;
    }

    private int SysFuncBody(int id, EmcState s)
    {
        switch (id)
        {
            case 0: // setWallType
                SetWallType(s.Arg(0), s.Arg(1), s.Arg(2));
                return 1;
            case 2: return 1;   // drawScene: the host decides when to draw
            case 18: // getItemPara
            {
                if (s.Arg(0) == 0) return 0;
                var it = Items.InPlay[s.Arg(0)];
                var prop = Items.Properties[it.ItemPropertyIndex];
                return s.Arg(1) switch
                {
                    0 => it.Block, 1 => it.X, 2 => it.Y, 3 => it.Level, 4 => it.ItemPropertyIndex,
                    5 => it.ShpCurFrameFlg, 6 => prop.NameStringId, 8 => prop.ShpIndex, 9 => prop.Type,
                    10 => prop.ItemScriptFunc, 11 => prop.Might, 12 => prop.Skill, 13 => prop.Protection, 14 => prop.UnkB,
                    15 => it.ShpCurFrameFlg & 0x1fff, 16 => prop.Flags, 17 => (prop.Skill << 8) | (prop.Might & 0xff),
                    _ => -1,
                };
            }
            case 64: // battleHitSkillTest
                return Board.BattleHitSkillTest(s.Arg(0), s.Arg(1), s.Arg(2));
            case 65: // inflictDamage
                if (s.Arg(0) == -1) for (int i = 0; i < 4; i += 1) Board.InflictDamage(i, s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
                else Board.InflictDamage(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
                return 1;
            case 132: // updateBlockAnimations2: a wall cycling through a list of types
            {
                int numFrames = s.Arg(3);
                if (numFrames <= 0) return 0;
                int curFrame = s.Arg(2) % numFrames;
                SetWallType(s.Arg(0), s.Arg(1), s.Arg(4 + curFrame));
                return 0;
            }
            case 49: // triggerDoorSwitch
                ProcessDoorSwitch(s.Arg(0), s.Arg(1));
                return 1;
            case 72: // setScriptTimer: a level asking for one of its animation functions to be run
            {
                int timerId = 0x50 + s.Arg(0);
                if (!_timers.TryGetValue(timerId, out var timer)) _timers[timerId] = timer = new GameTimer();
                if (s.Arg(1) != 0)
                {
                    timer.Enabled = true;
                    timer.Countdown = s.Arg(1);
                    timer.NextRun = QuantiseToTick(Clock + timer.Countdown * TickLength);
                }
                else timer.Enabled = false;
                return 1;
            }
            case 74: return 1;   // playAttackSound
            case 85: // giveItemToMonster
            {
                if (s.Arg(0) == -1) return 0;
                var m = Board.Monsters[s.Arg(0) & 0x7fff];
                int item = s.Arg(1);
                if (m.AssignedItems == 0) m.AssignedItems = item;
                else
                {
                    int c = m.AssignedItems;
                    while (Items.InPlay[c].NextAssignedObject != 0) c = Items.InPlay[c].NextAssignedObject;
                    Items.InPlay[c].NextAssignedObject = item;
                }
                Items.InPlay[item].NextAssignedObject = 0;
                return 1;
            }
            case 86: // loadLangFile: a conversation brings its own strings with it
            {
                string name = $"{s.ArgString(0)}.{LanguageExt}";
                if (_res.Exists(name))
                {
                    LevelLangFile = _res.Get(name);
                    if (Gui != null) Gui.LevelLangFile = LevelLangFile;
                }
                return 1;
            }
            case 166: // restoreFadePalette: the level's colours after a speaker's close-up
            {
                var p0 = Screen.Palette(0);
                Array.Copy(Screen.Palette(1), 0, p0, 0, 128 * 3);
                Screen.FadePalette(p0, 10);
                Screen.FadeFlag = 0;
                return 1;
            }
            case 25: // initAnimStruct: the .WSA a conversation plays - the speaker, drawn close up
                return Tim != null && Tim.InitAnimStruct(s.Arg(1), s.ArgString(0), s.Arg(2), s.Arg(3), s.Arg(4), 0, s.Arg(5)) != 0 ? 1 : 0;
            case 27: // freeAnimStruct
                return Tim != null && Tim.FreeAnimStruct(s.Arg(0)) != 0 ? 1 : 0;
            case 35: // setupBackgroundAnimationPart
                Tim?.Animator.SetupPart(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4), s.Arg(5), s.Arg(6), s.Arg(7), s.Arg(8), s.Arg(9));
                return 0;
            case 36: // startBackgroundAnimation
                Tim?.Animator.Start(s.Arg(0), s.Arg(1));
                return 1;
            case 139: // restorePage5: the conversation's animations go when it does
                if (Tim != null) for (int i = 0; i < 6; i += 1) Tim.FreeAnimStruct(i);
                return 1;
            case 107: // setPaletteBrightness
            {
                int old = Brightness;
                Brightness = s.Arg(0);
                if (s.Arg(1) == 1) SetPaletteBrightness(Screen.Palette(0), s.Arg(0), LampEffect);
                return old;
            }
            case 30: // setMusicTrack: the theme loadLevel plays once the level is up
                MusicTrack = s.Arg(0);
                return 1;
            case 39: // fadeToBlack
                Screen.FadeToBlack(s.Arg(0) != 0 ? s.Arg(0) : 10);
                return 1;
            case 40: // fadePalette
                Screen.FadePalette(Screen.Palette(0), 10);
                Screen.FadeFlag = 0;
                return 1;
            case 55: // copyRegion
                Screen.CopyRegion(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4), s.Arg(5), s.Arg(6), s.Arg(7));
                return 1;
            case 58: // fadeSequencePalette
            {
                var p3 = Screen.Palette(3);
                Array.Copy(Screen.Palette(0), 128 * 3, p3, 128 * 3, 768 - 128 * 3);
                Screen.LoadSpecialColors(p3);
                Screen.FadePalette(p3, 10);
                Screen.FadeFlag = 0;
                return 1;
            }
            case 59: // redrawPlayfield
                if (Screen.FadeFlag != 2) Screen.FadeClearSceneWindow(10);
                Gui?.DrawPlayField();
                SetPaletteBrightness(Screen.Palette(0), Brightness, LampEffect);
                return 1;
            case 61: // getNearestMonsterFromCharacter
                return Board.GetNearestMonsterFromCharacter(s.Arg(0), Party.Block, Party.Direction);
            case 71: // checkMoney
                return s.Arg(0) > Items.Credits ? 0 : 1;
            case 83: // getItemInHand
                return Gui?.ItemInHand ?? 0;
            case 84: // checkMagic
                return Board.CheckMagic(s.Arg(0), s.Arg(1), s.Arg(2));
            case 90: // getWallFlags
                return Walls.WallFlags[Map.Walls[s.Arg(0) & 0x3ff, s.Arg(1) & 3]];
            case 97: // healCharacter
                Gui?.IncreaseCharacterHitpoints(s.Arg(0), s.Arg(1), true);
                if (s.Arg(2) != 0) Gui?.DrawCharPortraitWithStats(s.Arg(0));
                return 1;
            case 101: // deleteMonstersFromBlock
                DeleteMonstersFromBlock(s.Arg(0));
                return 1;
            case 110: // checkForCertainPartyMember
                return Party_.Any(c => (c.Flags & 9) != 0 && c.Id == s.Arg(0)) ? 1 : 0;
            case 116: // checkInventoryFull
                return Items.Inventory.Any(i => i != 0) ? 0 : 1;
            case 131: // countSpecificMonsters
            {
                int types = 0, at = 0;
                while (at < 16 && s.Arg(at) != -1) types |= 1 << s.Arg(at++);
                return Board.Monsters.Count(m => (types & (1 << m.Type)) != 0 && m.Mode < 14);
            }
            case 134: // blockDoor
                BlockDoor = s.Arg(0);
                return BlockDoor;
            case 137: // removeLevelItem
                Board.RemoveLevelItem(s.Arg(0), s.Arg(1));
                return 1;
            case 160: // removeInventoryItem
            {
                int type = s.Arg(0);
                for (int i = 0; i < Items.Inventory.Length; i += 1)
                {
                    if (Items.Inventory[i] == 0 || Items.InPlay[Items.Inventory[i]].ItemPropertyIndex != type) continue;
                    Items.Inventory[i] = 0;
                    Gui?.DrawInventory();
                    return 1;
                }
                return 0;
            }
            case 165: // findInventoryItem
            {
                if (s.Arg(0) == 0)
                    for (int i = 0; i < Items.Inventory.Length; i += 1)
                        if (Items.Inventory[i] != 0 && Items.InPlay[Items.Inventory[i]].ItemPropertyIndex == s.Arg(2)) return 0;
                int cur = s.Arg(1), last = cur;
                if (s.Arg(1) == -1) { cur = 0; last = 4; }
                for (; cur < last; cur += 1)
                {
                    if ((Party_[cur].Flags & 1) == 0) continue;
                    for (int i = 0; i < 11; i += 1)
                        if (Party_[cur].Items[i] != 0 && Items.InPlay[Party_[cur].Items[i]].ItemPropertyIndex == s.Arg(2)) return cur;
                }
                return -1;
            }
            case 167: // calcNewBlockPosition
                return Party.CalcNewBlockPosition(s.Arg(0), s.Arg(1));
            case 168: // getSelectedCharacter
                return Gui?.SelectedCharacter ?? 0;
            case 169: // setHandItem
                Gui?.SetHandItem(s.Arg(0));
                return 1;
            case 171: // changeItemTypeOrFlag
            {
                if (s.Arg(0) < 1) return 0;
                var item = Items.InPlay[s.Arg(0)];
                int val = s.Arg(2);
                if (s.Arg(1) == 4) item.ItemPropertyIndex = val;
                else if (s.Arg(1) == 15) item.ShpCurFrameFlg = (item.ShpCurFrameFlg & 0xe000) | (val & 0x1fff);
                else val = -1;
                return val;
            }
            case 172: // placeInventoryItemInHand
            {
                int type = s.Arg(0), i = 0;
                for (; i < Items.Inventory.Length; i += 1)
                    if (Items.Inventory[i] != 0 && Items.InPlay[Items.Inventory[i]].ItemPropertyIndex == type) break;
                if (i == Items.Inventory.Length) return -1;
                Items.InventoryCurItem = i;
                int held = Gui?.ItemInHand ?? 0;
                Gui?.SetHandItem(Items.Inventory[i]);
                Items.Inventory[i] = held;
                if (s.Arg(1) != 0) Gui?.DrawInventory();
                return held;
            }
            case 173: // castSpell
                return Board.CastSpell(s.Arg(0), s.Arg(1), s.Arg(2), Level) ? 1 : 0;
            case 175: // increaseSkill
            {
                var c = Party_[s.Arg(0)];
                int skill = s.Arg(1), before = c.SkillLevels[skill];
                var requirements = StaticData.Table("ExpRequirements");
                Board.IncreaseExperience(s.Arg(0), skill,
                    requirements[Math.Min(before, requirements.Length - 1)] - c.ExperiencePts[skill]);
                return c.SkillLevels[skill] - before;
            }
            case 190: // getLanguage
                return Lang;
            case 37: // o1_hideMouse
            case 38: // o1_showMouse
            case 138: // savePage5
                return 1;
            case 42: // stopBackgroundAnimation
                Tim?.Animator.Stop(s.Arg(0));
                return 1;
            case 163: // getAnimationLastPart
                return Tim?.Animator.ResetLastPart(s.Arg(0)) ?? -1;
            case 33: // checkRectForMousePointer
                return Gui != null && Gui.MouseX >= s.Arg(0) && Gui.MouseX <= s.Arg(2)
                    && Gui.MouseY >= s.Arg(1) && Gui.MouseY <= s.Arg(3) ? 1 : 0;
            case 32: // setDefaultButtonState
                Gui?.EnableDefaultPlayfieldButtons();
                return 1;
            case 31: // setSequenceButtons
                Gui?.EnableSequenceButtons(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
                return 1;
            case 112: // deleteLevelItem
            {
                int item = s.Arg(0);
                if (Items.InPlay[item].Block != 0) Board.RemoveLevelItem(item, Items.InPlay[item].Block);
                Items.Delete(item);
                return 1;
            }
            case 102: // countBlockItems
            {
                int o = Map.AssignedObjects[s.Arg(0) & 0x3ff], found = 0, guard = 0;
                while (o != 0 && guard++ < 512)
                {
                    if ((o & 0x8000) == 0) found += 1;
                    o = (o & 0x8000) != 0 ? Board.Monsters[o & 0x7fff].NextAssignedObject : Items.InPlay[o].NextAssignedObject;
                }
                return found;
            }
            case 150: // findAssignedMonster
            {
                int o = s.Arg(1) == -1
                    ? Map.AssignedObjects[s.Arg(0) & 0x3ff]
                    : (s.Arg(1) & 0x8000) != 0 ? Board.Monsters[s.Arg(1) & 0x7fff].NextAssignedObject : Items.InPlay[s.Arg(1)].NextAssignedObject;
                int guard = 0;
                while (o != 0 && guard++ < 512)
                {
                    if ((o & 0x8000) != 0) return o & 0x7fff;
                    o = Items.InPlay[o].NextAssignedObject;
                }
                return -1;
            }
            case 151: // checkBlockForMonster
            {
                int wanted = s.Arg(1) | 0x8000;
                int o = Map.AssignedObjects[s.Arg(0) & 0x3ff], guard = 0;
                while ((o & 0x8000) != 0 && guard++ < 512)
                {
                    if (wanted == 0xffff || (wanted & 0xffff) == o) return o & 0x7fff;
                    o = Board.Monsters[o & 0x7fff].NextAssignedObject;
                }
                return -1;
            }
            case 189: // getItemPrice
            {
                int want = s.Arg(0);
                if (want < 0)
                {
                    want = -want;
                    return want < 50 ? 50 : (want + 99) / 100 * 100;
                }
                var prices = StaticData.Table("ItemPrices");
                for (int i = 0; i < 46 && i < prices.Length; i += 1) if (prices[i] >= want) return prices[i];
                return 0;
            }
            case 87: // playSoundEffect
                OnSoundEffect?.Invoke(s.Arg(0));
                return 1;
            case 96: // playEnvironmentalSfx
                Board.ProcessEnvironmentalSoundEffect(s.Arg(0), s.Arg(1) == -1 ? PartyBlock : s.Arg(1));
                return 1;
            case 100: // playMusicTrack
                MusicTrack = s.Arg(0);
                OnMusicTrack?.Invoke(s.Arg(0));
                return 1;
            case 41: // loadBitmap
            {
                string name = s.ArgString(0);
                if (!_res.Exists(name)) return 1;
                Screen.LoadBitmap(_res.Get(name), 3, Screen.Palette(3));
                Screen.CopyPage(3, s.Arg(1) != 2 ? s.Arg(1) : 2);
                return 1;
            }
            case 95: // update: the engine's own frame, which the host is already running
                return 1;
            case 99: // loadSoundFile
                OnLoadSoundFile?.Invoke(s.Arg(0));
                return 1;
            case 113: // calcInflictableDamagePerItem
                return Board.CalcInflictableDamagePerItem(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
            case 29: // characterSurpriseFeedback
                for (int i = 0; i < 4; i += 1)
                {
                    if ((Party_[i].Flags & 1) == 0 || Party_[i].Id >= 0) continue;
                    int sid = -Party_[i].Id;
                    int sfx = sid == 1 ? 136 : sid == 5 ? 50 : sid == 8 ? 49 : sid == 9 ? 48 : 0;
                    if (sfx != 0) OnSoundEffect?.Invoke(sfx);
                    return 1;
                }
                return 1;
            case 50: // checkEquippedItemScriptFlags
                for (int i = 0; i < 4; i += 1)
                {
                    if ((Party_[i].Flags & 1) == 0) continue;
                    for (int ii = 0; ii < 4; ii += 1)
                    {
                        int f = Items.Properties[Items.InPlay[Party_[i].Items[ii]].ItemPropertyIndex].ItemScriptFunc;
                        if (f == 0 || f == 2) return 1;
                    }
                }
                return 0;
            case 73: // createHandItem
            {
                if ((Gui?.ItemInHand ?? 0) != 0) return 0;
                int made = Items.MakeItem(s.Arg(0), s.Arg(1), s.Arg(2), Level);
                if (made == 0) return 0;
                Gui?.SetHandItem(made);
                return 1;
            }
            case 93: // releaseMonsterShapes
                return 0;
            case 105: // playEndSequence
                OnEndSequence?.Invoke();
                return 1;
            case 136: // getItemOnPos
            {
                int pX = s.Arg(1); if (pX != -1) pX &= 0xff;
                int pY = s.Arg(2); if (pY != -1) pY &= 0xff;
                int block = s.Arg(3) != 0 || EmcLastItem == -1 ? s.Arg(0) : EmcLastItem;
                EmcLastItem = Map.AssignedObjects[block & 0x3ff];
                int guard = 0;
                while (EmcLastItem != 0 && guard++ < 512)
                {
                    var item = (EmcLastItem & 0x8000) == 0 ? Items.InPlay[EmcLastItem] : null;
                    if (item == null || (pX != -1 && (item.X & 0xff) != pX) || (pY != -1 && (item.Y & 0xff) != pY))
                    {
                        block = EmcLastItem & 0x7fff;
                        EmcLastItem = Map.AssignedObjects[block & 0x3ff];
                        continue;
                    }
                    return EmcLastItem;
                }
                return 0;
            }
            case 153: // calcCoordinatesAddDirectionOffset
            {
                var (ox, oy) = MonsterBoard.CalcCoordinatesAddDirectionOffset(s.Arg(0) & 0xffff, s.Arg(1) & 0xffff, s.Arg(2));
                return s.Arg(3) != 0 ? ox : oy;
            }
            case 159: // drawCharPortrait
                if (s.Arg(0) == -1) Gui?.DrawAllCharPortraitsWithStats(); else Gui?.DrawCharPortraitWithStats(s.Arg(0));
                return 1;
            case 185: // updateDrawPage2
                Gui?.UpdateDrawPage2();
                return 1;
            case 186: // setMouseCursor: the host draws its own pointer
                return 1;
            case 115: // removeCharacterEffects
                Board.RemoveCharacterEffects(Party_[s.Arg(0)], s.Arg(1), s.Arg(2));
                return 1;
            case 158: // paralyzePoisonCharacter
                return Board.ParalyzePoisonCharacter(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
            case 152: // crossFadeRegion
                Screen.CrossFadeRegion(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4), s.Arg(5), s.Arg(6), s.Arg(7),
                    n => Gui?.PresentationRandom(Math.Max(0, n - 1)) ?? 0);
                return 1;
            case 184: // crossFadeScene
                Gui?.DrawScene(2);
                Screen.CrossFadeRegion(112, 0, 112, 0, 176, 120, 2, 0, n => Gui?.PresentationRandom(Math.Max(0, n - 1)) ?? 0);
                Gui?.UpdateDrawPage2();
                return 1;
            case 176: // paletteFlash
                Gui?.PaletteFlash(s.Arg(0), AdvanceClock);
                return 0;
            case 181: // shakeScene
                Gui?.ShakeScene(s.Arg(0), s.Arg(1), s.Arg(2), true, AdvanceClock);
                return 1;
            case 75: // addRemoveCharacter
            {
                int who = s.Arg(0);
                if (who < 0)
                {
                    who = -who;
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((Party_[i].Flags & 1) == 0 || Party_[i].Id != who) continue;
                        Party_[i].Flags &= ~1;
                        Gui?.CalcCharPortraitXpos();
                        if (Gui != null && Gui.SelectedCharacter == i) Gui.SelectedCharacter = 0;
                        break;
                    }
                }
                else Board.AddCharacter(who);
                if (UpdateFlags == 0)
                {
                    Gui?.EnableDefaultPlayfieldButtons();
                    Gui?.DrawPlayField();
                }
                return 1;
            }
            case 127: // suspendMonster
            {
                var m = Board.Monsters[s.Arg(0) & 0x7fff];
                Board.SetMonsterMode(m, 14);
                Board.Place(m, 0, 0);
                return 1;
            }
            case 129: // triggerEventOnMouseButtonClick: the host answers with its own input
                return 1;
            case 148: // assignCustomSfx
            {
                string name = s.ArgString(0);
                int at = s.Arg(1);
                if (string.IsNullOrEmpty(name) || at > 250) return 0;
                var index = StaticData.Table("IngameSfxIndex");
                if (at << 1 >= index.Length) return 0;
                int slot = index[at << 1];
                if (slot == 0xffff) return 0;
                CustomSfx[slot] = name;
                return 0;
            }
            case 164: // assignSpecialGuiShape
                if (Gui == null) return 1;
                if (s.Arg(0) != 0)
                {
                    var record = Decorations.Properties[Walls.ShapeMap[s.Arg(0)]];
                    int at = s.Arg(1);
                    int frame = record != null && at >= 0 && at < record.ShapeIndex.Length ? record.ShapeIndex[at] : 0xffff;
                    Gui.SpecialGuiShape = frame != 0xffff ? Decorations.Shapes[frame] : null;
                    Gui.SpecialGuiShapeX = s.Arg(2);
                    Gui.SpecialGuiShapeY = s.Arg(3);
                    Gui.SpecialGuiShapeMirrorFlag = s.Arg(4);
                }
                else
                {
                    Gui.SpecialGuiShape = null;
                    Gui.SpecialGuiShapeX = Gui.SpecialGuiShapeY = Gui.SpecialGuiShapeMirrorFlag = 0;
                }
                return 1;
            case 26: // playAnimationPart
                Tim?.Animator.SetupPart(s.Arg(0), 0, s.Arg(1), s.Arg(2), 1, -1, 0, 0, -1, 0);
                Tim?.Animator.Start(s.Arg(0), 0);
                return 1;
            case 117: // moveBlockObjects
            {
                int o = Map.AssignedObjects[s.Arg(0) & 0x3ff];
                int result = 0, level = s.Arg(2), destBlock = s.Arg(1);
                bool includeMonsters = s.Arg(3) != 0, runScript = s.Arg(4) != 0, includeItems = s.Arg(5) != 0;
                // The one place the game rewrites its own destination (the Urbish mines shaft).
                if (Level == 21 && level == 21 && destBlock == 0x3e0) { level = 20; destBlock = 0x0247; }
                int guard = 0;
                while (o != 0 && guard++ < 512)
                {
                    int l = o;
                    o = (o & 0x8000) != 0 ? Board.Monsters[o & 0x7fff].NextAssignedObject : Items.InPlay[o].NextAssignedObject;
                    if ((l & 0x8000) != 0)
                    {
                        if (!includeMonsters) continue;
                        var m = Board.Monsters[l & 0x7fff];
                        Board.SetMonsterMode(m, 14);
                        Board.Place(m, 0, 0);
                        result = 1;
                    }
                    else
                    {
                        if ((Items.InPlay[l].ShpCurFrameFlg & 0x4000) == 0 || !includeItems) continue;
                        Board.PlaceMoveLevelItem(l, level, destBlock, Items.InPlay[l].X & 0xff, Items.InPlay[l].Y & 0xff,
                            Items.InPlay[l].FlyingHeight, Level);
                        result = 1;
                        if (!runScript || level != Level) continue;
                        RunLevelScript(destBlock, 0x80, -1, l, 0, 0);
                    }
                }
                return result;
            }
            case 120: // addSpellToScroll
            {
                int spell = s.Arg(0);
                bool assigned = false;
                int slot = 0;
                for (int i = 0; i < SpellSlots; i += 1)
                {
                    if (!assigned && AvailableSpells[i] == -1) { assigned = true; slot = i; }
                    if (AvailableSpells[i] == spell)
                    {
                        Text?.PrintMessage(2, Gui?.LangString(0x42d0) ?? "");
                        return 1;
                    }
                }
                // A full scroll used to fall through with slot still 0 and write over whatever was in
                // the first line, so a spell bought from the imp could vanish when the next one was
                // learnt. Say so instead.
                if (!assigned)
                {
                    Text?.PrintMessage(2, "There is no room left on the scroll.");
                    return 1;
                }
                AvailableSpells[slot] = spell;
                Flags[31] |= 0x20;   // the scroll is on the playfield once there is a spell in it
                Gui?.EnableDefaultPlayfieldButtons();
                return 1;
            }
            case 98: // drawExitButton
            {
                if (Gui == null) return 1;
                int[] printPara = { 0x90, 0x78, 0x0c, 0x9f, 0x80, 0x1e };
                int which = s.Arg(0) & 1;
                int cp = Screen.CurPage;
                Screen.CurPage = 0;
                string oldFont = Screen.SetFont("6");
                int x = printPara[3 * which] << 1;
                int y = printPara[3 * which + 1];
                int offs = printPara[3 * which + 2];
                string label = Gui.LangString(0x4033);
                int w = Screen.TextWidth(label);
                int hButton = Screen.FontHeight + 3;
                Gui.DrawBox(x - offs - w, y - hButton, w + offs, hButton, 136, 251, 252);
                Screen.PrintText(label, x - (offs >> 1) - w, y - hButton + 2, 144, 0);
                Screen.SetFont(oldFont);
                Screen.CurPage = cp;
                return 1;
            }
            case 114: // distanceAttack
            {
                int fX = s.Arg(3), fY = s.Arg(4);
                if ((s.Arg(8) & 0x8000) == 0) fX = fY = 0x80;
                var (ax, ay) = Party.CalcCoordinates(s.Arg(2), fX, fY);
                if (Board.LaunchObject(s.Arg(0), s.Arg(1), ax, ay, s.Arg(5), s.Arg(6) << 1, s.Arg(7), 0x3f)) return 1;
                Items.Delete(s.Arg(1));
                return 0;
            }
            case 174: // pitDrop
                if (s.Arg(0) != 0)
                {
                    Gui?.DrawScene(2);
                    Gui?.PitDropScroll(9, AdvanceClock);
                    Gui?.ShakeScene(30, 4, 0, true, AdvanceClock);
                }
                else
                {
                    int scream = -1;
                    for (int i = 0; i < 4; i += 1)
                    {
                        var c = Party_[i];
                        if ((c.Flags & 1) == 0 || c.Id >= 0) continue;
                        scream = c.Id == -1 ? 54 : c.Id == -5 ? 53 : c.Id == -8 ? 52 : c.Id == -9 ? 51 : scream;
                    }
                    Screen.FillRect(112, 0, 288, 120, 0, 2);
                    if (scream >= 0) OnSoundEffect?.Invoke(scream);
                    Gui?.PitDropScroll(12, AdvanceClock);
                }
                return 1;
            case 182: // gasExplosion
            {
                var (distance, _) = Board.GetSpellTargetBlock(Party.Block, Party.Direction, 3);
                Gui?.ProcessGasExplosion(s.Arg(0), distance, AdvanceClock);
                return 1;
            }
            case 170: // drinkBezelCup
                Gui?.DrinkBezelCup(3 - s.Arg(0), s.Arg(1), OpenWsa, AdvanceClock);
                return 1;
            case 177: // restoreMagicShroud
                Flags[31] |= 0x04;
                Gui?.RestoreMagicShroud();
                Gui?.DrawPlayField();
                return 1;
            case 78: // loadTimScript
                if (Tim != null && ActiveTim[s.Arg(0)] == null)
                    ActiveTim[s.Arg(0)] = Tim.Load($"{s.ArgString(1)}.TIM", TimOpcodes.InGame(this, Gui, null));
                return 1;
            case 79: // runTimScript
            {
                if (Tim == null) return 0;
                var script = ActiveTim[s.Arg(0)];
                if (script == null) return 0;
                // Argument 1 is the engine's "loop": it keeps stepping the script until it ends.
                if (s.Arg(1) == 0) return Tim.ExecStep(script);
                // tim.exec(tim, loop) is awaited in script.mjs: the level script stops here, frame
                // after frame, until the conversation is over - including waiting for the player's
                // answer. Running it to the end inside this call (as this port did) burned the
                // clock without any input, so the guard's "Welcome" was never answered and the
                // moveMonster after it never happened. One step a frame, the opcode repeated.
                int result = Tim.ExecStep(script);
                if (result != -1 && Tim.Running(script))
                {
                    s.YieldAndRepeat();
                    return 0;
                }
                return script.ClickedButton;   // tim.exec returns tim.clickedButton, however it ended
            }
            case 80: // releaseTimScript
                ActiveTim[s.Arg(0)] = null;
                return 1;
            case 89: // stopTimScript
                Tim?.StopAllFuncs(ActiveTim[s.Arg(0)]);
                return 1;
            case 135: // resetTimDialogueState
                Tim?.ResetDialogueState(ActiveTim[s.Arg(0)]);
                return 1;
            case 81: // initSceneWindowDialogue: the text field slides up over the portraits
                InitSceneWindowDialogue(s.Arg(0));
                return 1;
            case 82: // restoreAfterSceneWindowDialogue: and slides away again
                RestoreAfterSceneWindowDialogue(s.Arg(0) != 0);
                return 1;
            case 179: // disableControls
                return Gui?.DisableControls(s.Arg(0)) ?? 0;
            case 180: // enableControls
                return Gui?.EnableControls() ?? 0;
            case 60: // loadNewLevel: a staircase, a trapdoor, the way out of a level
                LoadNewLevel(s.Arg(0), s.Arg(1), s.Arg(2));
                s.Ip = -1;   // the script that asked for it does not continue on the new level
                return 1;
            case 68: // moveMonster: a script walking a monster to a square, facing it on the way
            {
                var m = Board.Monsters[s.Arg(0)];
                if (m.Mode == 1 || m.Mode == 2)
                {
                    var (dx, dy) = Party.CalcCoordinates(s.Arg(1), s.Arg(2), s.Arg(3));
                    m.DestX = dx;
                    m.DestY = dy;
                    m.DestDirection = s.Arg(4) << 1;
                    if (m.X != m.DestX || m.Y != m.DestY)
                        Board.SetMonsterDirection(m, MonsterBoard.CalcMonsterDirection(m.X, m.Y, m.DestX, m.DestY));
                }
                return 1;
            }
            case 91: // changeMonsterStat: a script waking, hurting, moving or turning a monster
            {
                if (s.Arg(0) == -1) return 1;
                var m = Board.Monsters[s.Arg(0) & 0x7fff];
                int d = s.Arg(2);
                switch (s.Arg(1))
                {
                    case 0: Board.SetMonsterMode(m, d); break;
                    case 1: m.HitPoints = d; break;
                    case 2:
                    {
                        var (x, y) = Party.CalcCoordinates(d, m.X & 0xff, m.Y & 0xff);
                        if (Board.WalkMonsterCheckDest(x, y, m, 7) == 0) Board.Place(m, x, y);
                        break;
                    }
                    case 3: Board.SetMonsterDirection(m, d << 1); break;
                    case 6: m.Flags |= d; break;
                }
                return 1;
            }
            case 92: // getMonsterStat
            {
                if (s.Arg(0) == -1) return 0;
                var m = Board.Monsters[s.Arg(0) & 0x7fff];
                return s.Arg(1) switch
                {
                    0 => m.Mode, 1 => m.HitPoints, 2 => m.Block, 3 => m.Facing, 4 => m.Type,
                    5 => m.Properties?.HitPoints ?? 0, 6 => m.Flags, 7 => m.Properties?.Flags ?? 0,
                    8 => m.Properties != null ? Board.AnimType[m.Properties.ShapeIndex] : 0,
                    _ => 0,
                };
            }
            case 133: // checkPartyForItemType: does anyone have one of these?
            {
                int prop = s.Arg(1);
                int last = s.Arg(0) == -1 ? 3 : s.Arg(0) & 3;
                int first = s.Arg(0) == -1 ? 0 : s.Arg(0) & 3;
                for (int i = first; i <= last; i += 1) if (ItemEquipped(i, prop)) return 1;
                return 0;
            }
            case 157: // getNextActiveCharacter: walks the party, one call at a time
                if (s.Arg(0) != 0) _scriptCharacterCycle = 0;
                else _scriptCharacterCycle += 1;
                while (_scriptCharacterCycle < 4)
                {
                    if (Party_[_scriptCharacterCycle].Active) return _scriptCharacterCycle;
                    _scriptCharacterCycle += 1;
                }
                return -1;
            case 103: // characterSkillTest: the best of the party tries, and the dice decide
            {
                int skill = s.Arg(0) & 3;
                int n = Board.CountActiveCharacters();
                int m = 0, c = 0;
                for (int i = 0; i < n; i += 1)
                {
                    int v = Board.Characters[i].SkillModifiers[skill] + Board.Characters[i].SkillLevels[skill] + 25;
                    if (v > m) { m = v; c = i; }
                }
                return _rng.RollDice(1, 100) > m ? -1 : c;
            }
            case 104: // countAllMonsters
                return Board.Monsters.Count(mm => mm.HitPoints > 0 && mm.Mode != 13);
            case 108: // calcInflictableDamage
                return Board.CalcInflictableDamage(s.Arg(0), s.Arg(1), s.Arg(2));
            case 109: // getInflictedDamage
                return _rng.RollDice(2, s.Arg(0));
            case 19: // getCharacterStat
            {
                var c = Party_[s.Arg(0) & 3];
                int d = s.Arg(2);
                return s.Arg(1) switch
                {
                    0 => c.Flags, 1 => c.RaceClassSex, 5 => c.HitPointsCur, 6 => c.HitPointsMax,
                    7 => c.MagicPointsCur, 8 => c.MagicPointsMax, 9 => c.ItemProtection, 10 => c.Items[d & 0xf],
                    11 => c.SkillLevels[d & 3] + c.SkillModifiers[d & 3], 12 => c.ProtectionAgainstItems[d & 7],
                    13 => (d & 0x80) != 0 ? c.ItemsMight[7] : c.ItemsMight[d & 7], 14 => c.SkillModifiers[d & 3], 15 => c.Id,
                    _ => 0,
                };
            }
            case 20: // setCharacterStat
            {
                var c = Party_[s.Arg(0) & 3];
                int d = s.Arg(2);
                int e = s.Arg(3);
                switch (s.Arg(1))
                {
                    case 0: c.Flags = e; break;
                    case 1: c.RaceClassSex = e & 0x0f; break;
                    case 5: Board.SetCharacterMagicOrHitPoints(s.Arg(0), 0, e, 0); break;
                    case 6: c.HitPointsMax = e; break;
                    case 7: Board.SetCharacterMagicOrHitPoints(s.Arg(0), 1, e, 0); break;
                    case 8: c.MagicPointsMax = e; break;
                    case 9: c.ItemProtection = e; break;
                    case 10: c.Items[d & 0xf] = 0; break;
                    case 11: c.SkillLevels[d & 3] = e; break;
                    case 12: c.ProtectionAgainstItems[d & 7] = e; break;
                    case 13: if ((d & 0x80) != 0) c.ItemsMight[7] = e; else c.ItemsMight[d & 7] = e; break;
                    case 14: c.SkillModifiers[d & 3] = e; break;
                }
                return 0;
            }
            case 183: // calcNewBlockPosition: scripts use it to work out where a fall or a slide ends
                return Party.CalcNewBlockPosition(s.Arg(0), s.Arg(1));
            case 4: // moveParty: a script pushing the party around - a slide, a trap, a cutscene
            {
                int mode = s.Arg(0);
                if (mode > 5 && mode < 10) mode = (mode - 6 - (Party?.Direction ?? 0)) & 3;
                switch (mode)
                {
                    case 0: MoveParty(Party.Direction); break;
                    case 1: MoveParty((Party.Direction + 1) & 3); break;
                    case 2: MoveParty(Party.Direction ^ 2); break;
                    case 3: MoveParty((Party.Direction - 1) & 3); break;
                    case 4: TurnParty(-1); break;
                    case 5: TurnParty(1); break;
                    case 10: case 11: case 12: case 13:
                    {
                        int turns = Math.Abs(mode - 10 - Party.Direction);
                        if (turns > 2) turns = (turns ^ 2) * -1;
                        while (turns != 0)
                        {
                            if (turns > 0) { TurnParty(1); turns -= 1; }
                            else { TurnParty(-1); turns += 1; }
                        }
                        break;
                    }
                }
                return 1;
            }
            case 28: // getDirection
                return Party?.Direction ?? 0;
            case 45: // getGlobalScriptVar
                return GlobalScriptVars[s.Arg(0) & 0x1f];
            case 70: // giveTakeMoney
                if (s.Arg(0) >= 0) Items.GiveCredits(s.Arg(0));
                else Items.TakeCredits(-s.Arg(0));
                return 1;
            case 76: // giveItem: into the inventory row, or nowhere at all
            {
                int item = Items.Make(s.Arg(0), s.Arg(1), s.Arg(2), Level);
                if (Items.AddToInventory(item)) return 1;
                Items.Delete(item);
                return 0;
            }
            case 46: // setGlobalScriptVar
                GlobalScriptVars[s.Arg(0) & 0x1f] = (short)s.Arg(1);
                return 1;
            case 47: // getGlobalVar
                return s.Arg(0) switch
                {
                    0 => Party?.Block ?? 0,
                    1 => Party?.Direction ?? 0,
                    2 => Level,
                    3 => Gui?.ItemInHand ?? 0,
                    4 => Brightness,
                    5 => Items.Credits,
                    6 => GlobalScriptVars2[s.Arg(1) & 0xf],
                    8 => UpdateFlags,
                    9 => LampOilStatus,
                    10 => SceneDefaultUpdate,
                    11 => (Gui?.CompassBroken ?? false) ? 1 : 0,
                    12 => DrainMagic,
                    13 => (Speech?.Invoke() ?? false) ? (Text?.TextEnabled ?? true ? 2 : 1) : 0,
                    14 => Tim?.AbortFlag ?? 0,
                    _ => 0,
                };
            case 48: // setGlobalVar
                switch (s.Arg(0))
                {
                    case 0:                                                // a script teleport
                        Party?.MoveTo(s.Arg(2) & 0xffff);
                        if (Party != null) Gui?.Automap?.UpdateAutoMap(Party.Block);
                        break;
                    case 1: if (Party != null) Party.Direction = (s.Arg(2) & 0xffff) & 3; break;
                    case 2: Level = (s.Arg(2) & 0xffff) & 0xff; break;
                    case 3: Gui?.SetHandItem(s.Arg(2) & 0xffff); break;
                    case 4: Brightness = s.Arg(2) & 0xff; break;
                    case 5: Items.Credits = s.Arg(2) & 0xffff; break;
                    case 6: GlobalScriptVars2[s.Arg(1) & 0xf] = (short)(s.Arg(2) & 0xffff); break;
                    case 9: LampOilStatus = s.Arg(2) & 0xff; break;
                    case 10:
                        SceneDefaultUpdate = s.Arg(2) & 0xff;
                        Gui?.ToggleButtonDisplayMode(0, 0);
                        break;
                    case 11: if (Gui != null) Gui.CompassBroken = (s.Arg(1) & 0xff) != 0; break;
                    case 12: DrainMagic = s.Arg(1) & 0xff; break;
                    case 8:
                        UpdateFlags = s.Arg(2) & 0xffff;
                        PauseSysTimers(UpdateFlags == 1);
                        break;
                }
                return 1;
            case 52: // updateBlockAnimations: a torch or a lever changing between two wall types
            {
                int block = s.Arg(0) & 0x3ff;
                int wall = s.Arg(1);
                int current = Map.Walls[block, wall == -1 ? 0 : wall & 3];
                SetWallType(block, wall, current == s.Arg(2) ? s.Arg(3) : s.Arg(2));
                return 0;
            }
            case 34: // clearDialogueField
                Gui?.ClearDialogueField();
                return 1;
            case 57: // fadeClearSceneWindow
                Screen.FadeClearSceneWindow(10);
                return 1;
            case 69: // setupDialogueButtons
                Gui?.SetupDialogueButtons(s.Arg(0), Gui.LangString(s.Arg(1)), Gui.LangString(s.Arg(2)), Gui.LangString(s.Arg(3)));
                return 1;
            case 88: // processDialogue: 0 while the player has not chosen, and the script waits
            {
                int answered = Gui?.ProcessDialogue(false) ?? 1;
                if (answered == 0) s.Yield = true;
                return answered;
            }
            case 106: // stopPortraitSpeechAnim
                Gui?.StopPortraitSpeechAnim();
                return 1;
            case 111: // printMessage
            {
                int Safe(int i) => s.Sp + i < 100 ? s.Arg(i) : 0;
                var args = new object[] { Safe(3), Safe(4), Safe(5), Safe(6), Safe(7), Safe(8), Safe(9) };
                Text?.PrintMessage(Safe(0), Gui == null ? "" : Gui.FormatString(Gui.LangString(Safe(1)), args));
                return 1;
            }
            case 121: // playDialogueText
                Gui?.PrintDialogueText(3, Gui.LangString(s.Arg(0)) ?? "", Array.ConvertAll(s.Stack, v => (int)v), s.Sp + 1);
                return 1;
            case 122: // playDialogueTalkText: the line is spoken, and written out when it is not
            {
                int track = s.Arg(0);
                bool spoke = PlayCharacterSpeech?.Invoke(track, 0) ?? false;
                if (!spoke || TextEnabled())
                    Gui?.PrintDialogueText(4, Gui.LangString(track) ?? "", Array.ConvertAll(s.Stack, v => (int)v), s.Sp + 1);
                return 1;
            }
            case 187: // characterSays
                if (s.Arg(0) == -1) { StopSpeech?.Invoke(); return 1; }
                return PlayCharacterSpeech?.Invoke(s.Arg(0), s.Arg(1)) ?? false ? 1 : 0;
            case 188: // queueSpeech
                if (s.Arg(0) != 0 && s.Arg(1) != 0) QueueSpeech?.Invoke(s.Arg(0) + 1000, s.Arg(1));
                return 1;
            case 94: // playCharacterScriptChat
                StopSpeech?.Invoke();
                Gui?.StopPortraitSpeechAnim();
                PlayCharacterSpeech?.Invoke(s.Arg(1), s.Arg(0));
                Gui?.PrintDialogueText(3, Gui.LangString(s.Arg(2)) ?? "", Array.ConvertAll(s.Stack, v => (int)v), s.Sp + 3);
                return 1;
            case 124: // setNextFunc
                NextScriptFunc = s.Arg(0);
                return 1;
            case 128: // setScriptTextParameter
                ScriptTextParameter = s.Arg(0);

                return 1;
            case 130: // printWindowText
            {
                int dim = s.Arg(0), flg = s.Arg(1);
                Screen.ModifyScreenDim(dim, Screen.Dims[dim].Sx, Screen.Dims[dim].Sy, Screen.Dims[dim].W, Screen.Dims[dim].H);
                if ((flg & 1) != 0) Text?.ClearCurDim();
                if ((flg & 3) != 0) Text?.ResetDimTextPositions(dim);
                Gui?.PrintDialogueText(dim, Gui.LangString(s.Arg(2)) ?? "", Array.ConvertAll(s.Stack, v => (int)v), s.Sp + 3);
                return 1;
            }
            case 140: // initDialogueSequence
                Gui?.InitDialogueSequence(s.Arg(0), s.Arg(1));
                return 1;
            case 141: // restoreAfterDialogueSequence
                Gui?.RestoreAfterDialogueSequence(s.Arg(0));
                return 1;
            case 142: // setSpecialSceneButtons
                Gui?.SetSpecialSceneButtons(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4));
                return 1;
            case 143: // restoreButtonsAfterSpecialScene
                Gui?.SpecialSceneRestoreButtons();
                return 1;
            case 146: // prepareSpecialScene
                Gui?.PrepareSpecialScene(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3), s.Arg(4), s.Arg(5));
                return 1;
            case 147: // restoreAfterSpecialScene
                return Gui?.RestoreAfterSpecialScene(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3)) ?? 0;
            case 154: // resetPortraitsAndDisableSysTimer
                Gui?.ResetPortraitsAndDisableSysTimer();
                return 1;
            case 155: // enableSysTimer
                NeedSceneRestore = false;
                PauseSysTimers(false);
                return 1;
            case 156: // checkNeedSceneRestore
                return NeedSceneRestore ? 1 : 0;
            case 123: // checkMonsterTypeHostility
                for (int i = 0; i < 30; i += 1)
                {
                    if (s.Arg(0) != Board.Monsters[i].Type && s.Arg(0) != -1) continue;
                    return Board.Monsters[i].Mode == 1 ? 0 : 1;
                }
                return 1;
            case 1: // getWallType
                return (sbyte)Map.Walls[s.Arg(0) & 0x3ff, s.Arg(1) & 3];
            case 3: // rollDice
                return _rng.RollDice(s.Arg(0), s.Arg(1));
            case 7: // setGameFlag
                if (s.Arg(1) != 0) Flags[(s.Arg(0) >> 3) & 0xff] |= (byte)(1 << (s.Arg(0) & 7));
                else Flags[(s.Arg(0) >> 3) & 0xff] &= (byte)~(1 << (s.Arg(0) & 7));
                return 1;
            case 8: // testGameFlag
                return s.Arg(0) < 0 ? 0 : (Flags[(s.Arg(0) >> 3) & 0xff] >> (s.Arg(0) & 7)) & 1;
            case 9: // loadLevelGraphics
                LoadLevelGraphics(s.ArgString(0), s.Arg(1), s.Arg(2), s.Arg(5) == -1 ? null : s.ArgString(5));
                return 1;
            case 10: // loadBlockProperties
                LoadBlockProperties(s.ArgString(0));
                return 1;
            case 13: // allocItemPropertiesBuffer
                Items.AllocProperties(s.Arg(0));
                return 1;
            case 14: // setItemProperty
                Items.SetProperty(Enumerable.Range(0, 10).Select(s.Arg).ToArray());
                return 1;
            case 15: // makeItem
                return Items.Make(s.Arg(0), s.Arg(1), s.Arg(2), Level);
            case 16: // placeMoveLevelItem
                Board.PlaceMoveLevelItem(s.Arg(0), s.Arg(1), s.Arg(2), s.Arg(3) & 0xff, s.Arg(4) & 0xff, s.Arg(5), Level);
                return 1;
            case 17: // createLevelItem
            {
                int item = Items.Make(s.Arg(0), s.Arg(1), s.Arg(2), Level);
                if (item == -1) return item;
                Board.PlaceMoveLevelItem(item, s.Arg(3), s.Arg(4), s.Arg(5) & 0xff, s.Arg(6) & 0xff, s.Arg(7), Level);
                return item;
            }
            case 11: // loadMonsterShapes
                Board.LoadShapes(_res.Get(s.ArgString(0)), s.ArgString(0), s.Arg(1), s.Arg(2));
                return 1;
            case 56: // initMonster: 11 arguments off the stack
                return Board.Init(Enumerable.Range(0, 11).Select(s.Arg).ToArray(), Level);
            case 63: // loadMonsterProperties: the whole stat block, 42 arguments
                Board.LoadProperties(Enumerable.Range(0, 42).Select(s.Arg).ToArray());
                return 1;
            case 21: // loadLevelShapes: a second decoration set, keeping the ids already handed out
                DecorationShapeFile = s.ArgString(0);
                DecorationDataFile = s.ArgString(1);
                Decorations.LoadSet(DecorationShapeFile, DecorationDataFile, true);
                return 1;
            case 22: return 1; // closeLevelShapeFile
            case 24: // loadDoorShapes
            {
                var doorFile = Shapes.DecodeContainer(_res.Get(s.ArgString(0)));
                DoorShapes[0] = s.Arg(1) < doorFile.Length ? doorFile[s.Arg(1)] : null;
                DoorShapes[1] = s.Arg(2) < doorFile.Length ? doorFile[s.Arg(2)] : null;
                foreach (var (shape, index) in new[] { (DoorShapes[0], s.Arg(1)), (DoorShapes[1], s.Arg(2)) })
                    if (shape != null) shape.Key = $"{s.ArgString(0)}:{index}";
                for (int i = 0; i < 20; i += 1)
                {
                    Walls.WallFlags[i + 3] |= 7;
                    int t = i % 5;
                    if (t == 4) Walls.WallFlags[i + 3] &= 0xf8;
                    if (t == 3) Walls.WallFlags[i + 3] &= 0xfd;
                }
                if (s.Arg(3) != 0) for (int i = 3; i < 13; i += 1) Walls.WallFlags[i] &= 0xfd;
                if (s.Arg(4) != 0) for (int i = 13; i < 23; i += 1) Walls.WallFlags[i] &= 0xfd;
                return 1;
            }
            case 51: // setDoorState
            {
                int block = s.Arg(0) & 0x3ff;
                if (s.Arg(1) != 0) Map.Flags[block] = (byte)((Map.Flags[block] & 0xef) | 0x20);
                else Map.Flags[block] &= 0xdf;
                return 1;
            }
            case 53: // assignLevelDecorationShape
                return Decorations.Assign(s.Arg(0));
            case 54: // resetBlockShapeAssignment
            {
                short v = (sbyte)s.Arg(0);
                for (int i = 3; i < 8; i += 1) Walls.ShapeMap[i] = v;
                for (int i = 13; i < 18; i += 1) Walls.ShapeMap[i] = v;
                return 1;
            }
            default:
                // Speech, music, palettes, portraits: presentation the scene pass does not need.
                if (!UnhandledOpcodes.Contains(id)) UnhandledOpcodes.Add(id);
                return 1;
        }
    }

    private void SetWallType(int block, int wall, int value)
    {
        DrawSerial += 1;   // a torch or a lever changing is a change to the picture
        block &= 0x3ff;
        if (wall == -1)
        {
            for (int i = 0; i < 4; i += 1) Map.Walls[block, i] = (byte)value;
            if (Walls.Automap[value & 0xff] == 17) Map.Flags[block] = (byte)((Map.Flags[block] & 0xef) | 0x20);
            else Map.Flags[block] &= 0xdf;
        }
        else Map.Walls[block, wall & 3] = (byte)value;
    }

    private void LoadBlockProperties(string cmzFile)
    {
        BlockMap.DecodeInto(Map, _res.Get(cmzFile));
        ApplyModWalls();   // the designer's walls, before any script looks at them
        for (int block = 0; block < 1024; block += 1)
        {
            Map.Direction[block] = 5;
            if (Walls.Automap[Map.Walls[block, 0]] == 17) Map.Flags[block] = 0x20;
        }
    }

    /// <summary>deleteMonstersFromBlock: take every monster in a block off the board.</summary>
    public void DeleteMonstersFromBlock(int block)
    {
        int id = Map.AssignedObjects[block & 0x3ff];
        var seen = new HashSet<int>();
        while (id != 0 && seen.Add(id))
        {
            // findObject: the chain holds items too, and the next link has to come from whichever
            // this one is. Asking the monster table for an item walks off the end of it.
            int next = Board.Find(id).NextAssignedObject;
            if ((id & 0x8000) == 0) { id = next; continue; }
            var monster = Board.Monsters[id & 0x7fff];
            Board.SetMonsterMode(monster, 14);
            Board.Place(monster, 0, 0);
            id = next;
        }
    }

    private void LoadLevelGraphics(string file, int specialColor, int weight, string palFile)
    {
        if (!string.IsNullOrEmpty(file))
        {
            BlockDataFile = file;
            SpecialColor = specialColor;
            SpecialColorWeight = weight;
            OverridePalFile = palFile ?? "";
        }
        if (!_res.Exists($"{BlockDataFile}.VCN")) _res.LoadPak($"{(BlockDataFile == "YVEL1" ? "YVEL" : BlockDataFile)}.PAK");
        Vcn = VcnData.Decode(_res.Get($"{BlockDataFile}.VCN"));
        Vmp = VmpData.Decode(_res.Get($"{BlockDataFile}.VMP"));

        var pal0 = Screen.Palette(0);
        var source = string.IsNullOrEmpty(OverridePalFile) ? Vcn.RawPalette : _res.Get(OverridePalFile);
        Array.Copy(source, pal0, 384);

        Screen.BuildLevelOverlays(pal0, SpecialColor, SpecialColorWeight);
        Screen.LoadTransparencyTables(_res.Get($"LEVEL{Level:00}.TLC"));
    }
}

/// <summary>A script that would not finish: raised by the call budget, caught by the runner.</summary>
public sealed class ScriptStuckException : Exception { }
