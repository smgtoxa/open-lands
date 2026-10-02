// src/game/engine.mjs (LolEngine) + src/game/lol.mjs (LandsOfLore): the core, the timers, the input
// queue, the strings, and the main loop. JS names are kept; the mixins are the other partial files.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>engine.mjs Resources: the PAK search list, first archive wins.</summary>
    public sealed class Resources
    {
        public string root;
        public Func<string, Task<byte[]>> fetchBytes;
        public readonly List<(string name, PakArchive archive)> paks = new List<(string, PakArchive)>();

        public Resources(string root, Func<string, Task<byte[]>> fetchBytes)
        {
            this.root = root;
            this.fetchBytes = fetchBytes;
        }

        public async Task loadPak(string name)
        {
            if (paks.Any(p => p.name == name)) return;
            var bytes = await fetchBytes(root + name);
            if (paks.Any(p => p.name == name)) return;
            paks.Add((name, new PakArchive(bytes)));
        }

        /// <summary>FILEDATA.FDT lists every archive of the CD version; load all .PAK entries (TLK speech is streamed separately).</summary>
        public async Task<List<string>> loadFileList(byte[] bytes, Func<string, bool> skip = null)
        {
            skip = skip ?? (n => n.EndsWith(".TLK") || n == "MUSIC.PAK" || n == "DRIVERS.PAK");
            var names = new List<string>();
            for (int p = 0; p + 4 <= bytes.Length; p += 20)
            {
                int offset = (int)BitConverter.ToUInt32(bytes, p);
                if (offset == 0) break;
                var name = new StringBuilder();
                for (int i = 0; i < 12 && offset + i < bytes.Length && bytes[offset + i] != 0; i += 1) name.Append((char)bytes[offset + i]);
                string n = name.ToString().ToUpperInvariant();
                if (n.EndsWith(".PAK") && !skip(n)) names.Add(n);
            }
            foreach (var n in names)
            {
                try { await loadPak(n); }
                catch (Exception e) { throw new Exception($"{n}: {e.Message}"); }
            }
            return names;
        }

        public void unloadPak(string name) => paks.RemoveAll(p => p.name == name);

        public bool exists(string name) => paks.Any(p => p.archive.has(name));

        public byte[] get(string name)
        {
            foreach (var pak in paks) if (pak.archive.has(name)) return pak.archive.get(name);
            throw new Exception($"Missing resource: {name}");
        }
    }

    public sealed partial class LandsOfLore
    {
        /// <summary>How many spells the scroll holds (engine.mjs SPELL_SLOTS).</summary>
        public const int SPELL_SLOTS = 10;
        const double TICK_LENGTH = 1000.0 / 60;

        // ---- host wiring ----
        public readonly Scheduler sched;
        public Resources res;
        public Action<string> log;
        public Action<Screen> presentHook;
        /// <summary>
        /// The page wraps a few engine methods to count achievements (main.mjs wrapCount: castSpell,
        /// moveParty, clickedScenePickupItem, uiTakeFloorItem, showOutro). C# cannot replace a method,
        /// so those methods call this first with their arguments.
        /// </summary>
        public Action<string, object[]> onCall;
        /// <summary>engine.ui (host-ui.mjs uiEmit): the page's handlers, by event name.</summary>
        public Action<string, object[]> ui;

        public double tickLength;
        public double startTime;
        public bool quit;
        public Screen screen;
        public StaticData @static;

        // input
        public int mouseX, mouseY, mouseDown;
        public List<InputEvent> events = new List<InputEvent>();
        public List<Button> buttonList = new List<Button>();
        public bool preserveEvents;

        // timers
        public readonly Dictionary<int, Timer> timers = new Dictionary<int, Timer>();
        readonly List<int> _timerOrder = new List<int>();
        public bool timersPaused;

        // clock (engine.mjs: virtualClock / clockBase / clockTicks / clockHeld are set by harnesses)
        public double? virtualClock;
        public double clockBase;
        public int clockTicks;
        public int clockHeld;

        // dice (randomSeed / presentationSeed: set to make a run reproducible)
        public uint? randomSeed;
        public uint? presentationSeed;
        readonly Random _entropy = new Random();

        // global game state (LoLEngine constructor defaults)
        public byte[] flagsTable = new byte[100];
        public short[] globalScriptVars = new short[24];
        public ushort[] globalScriptVars2 = new ushort[24];
        public int currentLevel = 1;
        public int currentBlock;
        public int currentDirection;
        public int partyPosX, partyPosY;
        public int scriptDirection;
        public int nextScriptFunc;
        public bool suspendScript;
        public int updateFlags;
        public bool sceneUpdateRequired;
        public int sceneDefaultUpdate;
        public int brightness;
        public int lampEffect;
        public int lampOilStatus;
        public bool lampStatusSuspended;
        public int credits;
        public int itemInHand;
        /// <summary>the original 48; the host may grow it (setInventorySize)</summary>
        public ushort[] inventory = new ushort[48];
        public int inventoryCurItem;
        public int selectedCharacter;
        public int charSelection = -1;
        public int updateCharNum = -1;
        public Character[] characters;
        public byte[] charStatusFlags = new byte[4];
        public int selectedSpell;
        public sbyte[] availableSpells;
        /// <summary>hero the player selected by hand (see uiAutoSelect)</summary>
        public int selectionPinned = -1;
        public ActiveSpell activeSpell = new ActiveSpell();
        public bool weaponsDisabled;
        public bool partyAwake = true;
        /// <summary>set by the host: keeps the party in the camp once everyone is healed</summary>
        public bool campMode;
        public int compassDirection = -1;
        public int compassDirectionIndex = -1;
        public int compassStep;
        public int compassBroken;
        public int drainMagic;
        public int monsterDifficulty = 1;
        public bool smoothScrollingEnabled = true;
        public int hasTempDataFlags;
        public int lastMouseRegion = -1;
        public int curTlkFile = -1;
        public List<string> stringBuffers = new List<string>();
        public byte[] levelLangFile;
        public byte[] landsFile;
        public EmcScript itemScript;
        public EmcScript scriptData;
        public int partyDamageFlags = -1;
        public int monsterCurBlock;
        public int objectLastDirection;
        public int textColorFlag;
        public bool fadeText;
        public int loadLevelFlag;
        public int needSceneRestore;
        public int blockDoor;
        public double playTimer;

        // lol.mjs
        public int lang;
        public string languageExt = "ENG";
        public sbyte[] dscBlockIndex;
        public int clickedShapeXOffs = 136;
        public int clickedShapeYOffs = 8;
        public int clickedSpecialFlag = 0x40;
        public bool floatingCursorsEnabled;
        public List<Func<Task>> asyncQueue = new List<Func<Task>>();
        public TextDisplayer txt;
        public TimInterpreter tim;
        public Gui gui;
        public bool playIntro;
        public bool restartRequested;
        /// <summary>the host registers the items it adds, before anything draws</summary>
        public Action onStartup;
        public HashSet<int> unknownOpcodes;
        public Shape[] itemIconShapes, gameShapes, itemShapes, thrownShapes, effectShapes, fireballShapes, healShapes, healiShapes;
        public string[] opcodeNames = new string[0];

        public LandsOfLore(Resources resources, Scheduler scheduler, Action<string> log = null, Action<Screen> presentHook = null)
        {
            sched = scheduler;
            res = resources;
            this.log = log ?? (m => { });
            this.presentHook = presentHook;
            tickLength = TICK_LENGTH;
            startTime = sched.Now;
            quit = false;
            screen = new Screen(ms => delay(ms), () => present(), () => getMillis());
            @static = new StaticData();
            characters = Enumerable.Range(0, 4).Select(_ => makeEmptyCharacter()).ToArray();
            availableSpells = Enumerable.Repeat((sbyte)-1, SPELL_SLOTS).ToArray();

            // lol.mjs constructor
            lang = 0;
            languageExt = "ENG";
            dscBlockIndex = @static.DscBlockIndex.Select(v => (sbyte)v).ToArray();
            initScene();
            initItems();
            initMonsters();
            initParty();
            initGui();
            initDialogue();
            initSound();
            initHostUi();
            initMods();
            initIntro();
            initMeta();
            initCraft();
            initCampStore();
            initDungeonRun();
            initSpells();
            initAutomap();
            initScript();
            txt = new TextDisplayer(this);
            tim = new TimInterpreter(this);
            gui = new Gui(this);
        }

        public Character makeEmptyCharacter() => new Character();

        // ---- time & loop ----
        public double getMillis() => virtualClock ?? (sched.Now - startTime);

        public T holdClock<T>(Func<T> run)
        {
            clockHeld += 1;
            try { return run(); } finally { clockHeld -= 1; }
        }

        /// <summary>Whole ticks only (see engine.mjs): a timer fires at most once per pass.</summary>
        public void advanceClock(double ms)
        {
            if (virtualClock == null || clockHeld != 0) return;
            int ticks = Math.Max(1, Js.Round(Math.Max(0, ms) / tickLength));
            for (int i = 0; i < ticks; i += 1)
            {
                clockTicks += 1;
                virtualClock = clockBase + clockTicks * tickLength;
                timerUpdate();
            }
        }

        public double quantiseToTick(double moment)
        {
            if (virtualClock == null) return moment;
            return clockBase + Js.RoundD((moment - clockBase) / tickLength) * tickLength;
        }

        public void present() => presentHook?.Invoke(screen);

        public async Task delay(double ms, bool update = false)
        {
            if (quit) throw new QuitException();
            if (virtualClock != null)
            {
                advanceClock(Math.Max(0, ms));
                if (update) this.update();
                present();
                await sched.Sleep(0);
                return;
            }
            double end = getMillis() + ms;
            do
            {
                if (update)
                {
                    timerUpdate();
                    this.update();
                }
                present();
                await sched.Sleep(Math.Min(16, Math.Max(0, end - getMillis())));
            } while (getMillis() < end);
        }

        public async Task delayUntil(double timestamp)
        {
            while (getMillis() < timestamp) await delay(Math.Min(16, timestamp - getMillis()), true);
        }

        // ---- timers (TimerManager) ----
        public void addTimer(int id, Action<int> func, int countdown, bool enabled)
        {
            if (!timers.ContainsKey(id)) _timerOrder.Add(id);
            timers[id] = new Timer { id = id, func = func, countdown = countdown, enabled = enabled ? 1 : 0 };
        }

        /// <summary>Diagnostics: each timer as it fires (the harness's LOL_TRACE_TIMERS).</summary>
        public Action<int, double> traceTimer;

        public void timerUpdate()
        {
            if (timersPaused || clockHeld != 0) return;
            double now = getMillis();
            foreach (int id in _timerOrder.ToArray())
            {
                var timer = timers[id];
                if (timer.enabled == 1 && timer.countdown >= 0 && timer.nextRun <= now)
                {
                    traceTimer?.Invoke(timer.id, now);
                    timer.func(timer.id);
                    double cur = getMillis();
                    timer.lastUpdate = cur;
                    timer.nextRun = quantiseToTick(cur + timer.countdown * tickLength);
                }
            }
        }

        public void timerSetCountdown(int id, int countdown)
        {
            if (!timers.TryGetValue(id, out var timer)) return;
            timer.countdown = countdown;
            if (countdown >= 0)
            {
                double cur = getMillis();
                timer.lastUpdate = cur;
                timer.nextRun = quantiseToTick(cur + countdown * tickLength);
            }
        }

        public void timerSetNextRun(int id, double nextRun) { if (timers.TryGetValue(id, out var t)) t.nextRun = nextRun; }
        public double timerGetNextRun(int id) => timers.TryGetValue(id, out var t) ? t.nextRun : 0;
        public int timerGetDelay(int id) => timers.TryGetValue(id, out var t) ? t.countdown : 0;
        public void timerEnable(int id) { if (timers.TryGetValue(id, out var t)) t.enabled = 1; }
        public void timerDisable(int id) { if (timers.TryGetValue(id, out var t)) t.enabled = 0; }
        public bool timerIsEnabled(int id) => timers.TryGetValue(id, out var t) && t.enabled == 1;

        public void timerPauseSingle(int id, bool pause)
        {
            if (!timers.TryGetValue(id, out var timer)) return;
            if (pause)
            {
                timer.pauseStart = getMillis();
                timer.enabled |= 2;
            }
            else if ((timer.enabled & 2) != 0)
            {
                timer.nextRun += getMillis() - timer.pauseStart;
                timer.enabled &= ~2;
            }
        }

        // ---- input ----
        public void pushMouse(int x, int y, int? down = null)
        {
            mouseX = x;
            mouseY = y;
            if (down != null) events.Add(new InputEvent { type = down != 0 ? "mousedown" : "mouseup", x = x, y = y, button = down.Value });
        }

        public void pushKey(string key) => events.Add(new InputEvent { type = "key", key = key });

        public (int x, int y) getMousePos() => (mouseX, mouseY);

        public void removeInputTop() => events.Clear();

        public void updateInput() { }

        /// <summary>events.shift()</summary>
        public InputEvent shiftEvent()
        {
            if (events.Count == 0) return null;
            var e = events[0];
            events.RemoveAt(0);
            return e;
        }

        // ---- strings (LoLEngine::getLangString) ----
        public string getLangString(int id)
        {
            if (id == 0xffff) return null;
            var buffer = (id & 0x4000) != 0 ? landsFile : levelLangFile;
            if (buffer == null) return null;
            int realId = id & 0x3fff;
            if ((realId << 1) + 1 >= buffer.Length) return null;
            int offset = buffer[realId << 1] | (buffer[(realId << 1) + 1] << 8);
            return decodeString2(decodeString1(buffer, offset));
        }

        // ---- misc helpers ----
        /// <summary>Diagnostics: every gameplay die rolled (the harness's LOL_TRACE_DICE).</summary>
        public Action<double> traceRandom;

        public double randomFloat()
        {
            if (randomSeed == null) return _entropy.NextDouble();
            randomSeed = unchecked(randomSeed.Value * 1103515245u + 12345u);
            double r = ((randomSeed.Value >> 16) & 0x7fff) / (double)0x8000;
            traceRandom?.Invoke(r);
            return r;
        }

        public double presentationFloat()
        {
            if (presentationSeed == null) presentationSeed = (uint)(_entropy.NextDouble() * 0xffffffff);
            presentationSeed = unchecked(presentationSeed.Value * 1103515245u + 12345u);
            return ((presentationSeed.Value >> 16) & 0x7fff) / (double)0x8000;
        }

        public int presentationRandom(int range) => Js.Floor(presentationFloat() * (range + 1));

        public int presentationRoll(int times, int pips, int inc = 0)
        {
            if (times <= 0 || pips <= 0) return inc;
            int r = 0;
            while (times-- > 0) r += 1 + Js.Floor(presentationFloat() * pips);
            return r + inc;
        }

        /// <summary>Diagnostics: rollDice(times, pips, inc) -> result.</summary>
        public Action<int, int, int, int> traceDice;

        public int rollDice(int times, int pips, int inc = 0)
        {
            int t0 = times;
            if (times <= 0 || pips <= 0) { traceDice?.Invoke(t0, pips, inc, inc); return inc; }
            int r = 0;
            while (times-- > 0) r += 1 + Js.Floor(randomFloat() * pips);
            traceDice?.Invoke(t0, pips, inc, r + inc);
            return r + inc;
        }

        public int random(int range) => Js.Floor(randomFloat() * (range + 1));

        public int setGameFlag(int flag) { flagsTable[flag >> 3] |= (byte)(1 << (flag & 7)); return 1; }
        public int resetGameFlag(int flag) { flagsTable[flag >> 3] &= (byte)~(1 << (flag & 7)); return 0; }
        public int queryGameFlag(int flag) => (flagsTable[flag >> 3] >> (flag & 7)) & 1;

        public int countActiveCharacters()
        {
            int count = 0;
            for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0) count += 1;
            return count;
        }

        /// <summary>Shape file loaders (Screen::loadBitmap + makeShapeCopy pattern).</summary>
        public Shape[] loadShapeFile(string name)
        {
            var shapes = LolShapes.decodeShapeFile(Cps.decodeBitmapData(res.get(name)).data);
            for (int i = 0; i < shapes.Length; i += 1) if (shapes[i] != null) shapes[i].key = $"{name}:{i}";   // HD asset lookup key
            return shapes;
        }

        public Shape[] loadRawShapeFile(string name) => LolShapes.decodeShapeFile(res.get(name));

        // ---- EMC ----
        public EmcScript loadScript(string name) => Emc.decodeEmc(res.get(name));

        public async Task<EmcState> runScriptFunction(EmcScript script, int func, Action<EmcState> setup = null)
        {
            var state = new EmcState(script);
            if (!state.start(func)) return state;
            setup?.Invoke(state);
            await Emc.runEmc(state, opcodes, id => warnOpcode(id));
            return state;
        }

        public void warnOpcode(int id)
        {
            unknownOpcodes = unknownOpcodes ?? new HashSet<int>();
            if (unknownOpcodes.Contains(id)) return;
            unknownOpcodes.Add(id);
            log($"Unimplemented script opcode 0x{id:x} ({(id < opcodeNames.Length ? opcodeNames[id] : "?")})");
        }

        // Util::decodeString1 / decodeString2 on a zero-terminated buffer.
        static readonly int[] DECODE1 = { 0x20, 0x65, 0x74, 0x61, 0x69, 0x6e, 0x6f, 0x73, 0x72, 0x6c, 0x68, 0x63, 0x64, 0x75, 0x70, 0x6d };
        static readonly int[] DECODE2 =
        {
            0x74, 0x61, 0x73, 0x69, 0x6f, 0x20, 0x77, 0x62, 0x20, 0x72, 0x6e, 0x73, 0x64, 0x61, 0x6c, 0x6d, 0x68, 0x20, 0x69, 0x65, 0x6f, 0x72,
            0x61, 0x73, 0x6e, 0x72, 0x74, 0x6c, 0x63, 0x20, 0x73, 0x79, 0x6e, 0x73, 0x74, 0x63, 0x6c, 0x6f, 0x65, 0x72, 0x20, 0x64, 0x74, 0x67,
            0x65, 0x73, 0x69, 0x6f, 0x6e, 0x72, 0x20, 0x75, 0x66, 0x6d, 0x73, 0x77, 0x20, 0x74, 0x65, 0x70, 0x2e, 0x69, 0x63, 0x61, 0x65, 0x20,
            0x6f, 0x69, 0x61, 0x64, 0x75, 0x72, 0x20, 0x6c, 0x61, 0x65, 0x69, 0x79, 0x6f, 0x64, 0x65, 0x69, 0x61, 0x20, 0x6f, 0x74, 0x72, 0x75,
            0x65, 0x74, 0x6f, 0x61, 0x6b, 0x68, 0x6c, 0x72, 0x20, 0x65, 0x69, 0x75, 0x2c, 0x2e, 0x6f, 0x61, 0x6e, 0x73, 0x72, 0x63, 0x74, 0x6c,
            0x61, 0x69, 0x6c, 0x65, 0x6f, 0x69, 0x72, 0x61, 0x74, 0x70, 0x65, 0x61, 0x6f, 0x69, 0x70, 0x20, 0x62, 0x6d,
        };

        public static string decodeString1(byte[] buffer, int offset)
        {
            var sb = new StringBuilder();
            for (int i = offset; i < buffer.Length && buffer[i] != 0; i += 1)
            {
                int c = buffer[i];
                if ((c & 0x80) != 0)
                {
                    c &= 0x7f;
                    sb.Append((char)DECODE1[(c & 0x78) >> 3]);
                    c = DECODE2[c];
                }
                sb.Append((char)c);
            }
            return sb.ToString();
        }

        public static string decodeString2(string src)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < src.Length; i += 1)
            {
                if (src[i] == 0x1b)
                {
                    i += 1;
                    sb.Append((char)(((i < src.Length ? src[i] : 0) + 0x7f) & 0xff));
                }
                else sb.Append(src[i]);
            }
            return sb.ToString();
        }

        // ================================================================== lol.mjs

        public void queueAsync(Func<Task> fn) => asyncQueue.Add(fn);

        public async Task drainAsync()
        {
            while (asyncQueue.Count > 0)
            {
                var fn = asyncQueue[0];
                asyncQueue.RemoveAt(0);
                try { await fn(); }
                catch (QuitException) { throw; }
                catch (Exception error) { log($"async task failed: {error.Message}"); }
            }
        }

        public async Task<InputEvent> waitForInputEvent(Func<InputEvent> poll = null)
        {
            while (!quit)
            {
                if (events.Count > 0) return shiftEvent();
                if (poll != null)
                {
                    var ev = poll();
                    if (ev != null) return ev;
                }
                update();
                timerUpdate();
                await drainAsync();
                await delay(tickLength);
            }
            return new InputEvent { type = "key", key = "Escape" };
        }

        // LoLEngine::preInit + startup + startupNew
        public async Task preInit()
        {
            await res.loadPak("GENERAL.PAK");
            await res.loadPak("STARTUP.PAK");
            await res.loadFileList(await res.fetchBytes($"{res.root}FILEDATA.FDT"));
            if (res.exists("FONT9P.FNT")) screen.loadFont("9", res.get("FONT9P.FNT"));
            if (res.exists("FONT6P.FNT")) screen.loadFont("6", res.get("FONT6P.FNT"));
            screen.setFont("9");
            await loadTalkFile(0);
            landsFile = res.get($"LANDS.{languageExt}");
            itemIconShapes = loadShapeFile("ITEMICN.SHP");
            gameShapes = loadShapeFile("GAMESHP.SHP");
        }

        public async Task startup()
        {
            screen.clearPage(0);
            var pal = screen.getPalette(0);
            screen.loadBitmap(res.get("PLAYFLD.CPS"), 3, pal);
            screen.copyPalette(1, 0);
            Js.Fill(pal, (byte)0x3f, 0, 3);
            Js.Fill(pal, (byte)0x3f, 2 * 3, 128 * 3);
            Js.Fill(pal, (byte)0x3f, 192 * 3, 196 * 3);
            screen.generateOverlay(pal, screen.paletteOverlay1, 1, 96, 254);
            screen.generateOverlay(pal, screen.paletteOverlay2, 144, 65, 254);
            screen.copyPalette(0, 1);
            Js.Fill(screen.getPalette(1), (byte)0);
            Js.Fill(screen.getPalette(2), (byte)0);
            setMouseCursor(0, 0, itemIconShapes[0x85]);
            itemShapes = loadShapeFile("ITEMSHP.SHP");
            thrownShapes = loadShapeFile("THROWN.SHP");
            effectShapes = loadShapeFile("ICE.SHP");
            fireballShapes = loadShapeFile("FIREBALL.SHP");
            healShapes = loadShapeFile("HEAL.SHP");
            healiShapes = loadShapeFile("HEALI.SHP");
            initItems();
            await runInitScript("ONETIME.INF", 0);
            itemScript = loadScript("ITEM.INF");
            setMouseCursorToItemInHand();
        }

        public async Task startupNew(int charSelection)
        {
            selectedSpell = 0;
            compassStep = 0;
            compassDirection = compassDirectionIndex = -1;
            lastMouseRegion = -1;
            currentLevel = 1;
            await giveCredits(41, 0);
            inventory[0] = (ushort)makeItem(216, 0, 0);
            inventory[1] = (ushort)makeItem(217, 0, 0);
            inventory[2] = (ushort)makeItem(218, 0, 0);
            availableSpells[0] = 0;
            setupScreenDims();
            Js.Fill(globalScriptVars2, (ushort)0x100);
            int[] selectIds = { -9, -1, -8, -5 };
            this.charSelection = charSelection;
            addCharacter(selectIds[charSelection]);
            await drainAsync();
            gui_enableDefaultPlayfieldButtons();
            await loadLevel(currentLevel);
        }

        // LoLEngine::update
        public void update()
        {
            for (int i = 0; i < 6; i += 1) if ((updateFlags & 8) == 0) tim.animator.update(i);
            if (updateCharNum != -1 && getMillis() > updatePortraitNext) updatePortraitSpeechAnim();
            if ((flagsTable[31] & 0x08) != 0 || (updateFlags & 4) == 0) updateLampStatus();
            if ((flagsTable[31] & 0x40) != 0 && (updateFlags & 4) == 0 && (compassDirection == -1 || currentDirection << 6 != compassDirection || compassStep != 0)) updateCompass();
            snd_updateCharacterSpeech();
            fadeTextStep();
            present();
        }

        // gui_updateInput: dispatch queued events against the active button list.
        public async Task gui_updateInput()
        {
            while (events.Count > 0 && !quit)
            {
                var ev = shiftEvent();
                if ((updateFlags & 3) != 0 || weaponsDisabled)
                {
                    // Input restricted to the current button list (dialogue/inventory); no scene keys.
                }
                var buttons = new List<Button>();
                if (ev.type == "mousedown") buttons = findButtonsForClick(ev.x, ev.y, ev.button);
                else if (ev.type == "key")
                {
                    string k = ev.key.Length == 1 ? ev.key.ToLowerInvariant() : ev.key;
                    var button = KEY_CODES.TryGetValue(k, out int code) ? findButtonForKey(code, ev.shift) : null;
                    if (button != null) buttons = new List<Button> { button };
                    if (ev.key == " " || ev.key == "Enter") snd_stopSpeech(true);
                    else if (ev.key == "/" && button == null)
                    {
                        if (weaponsDisabled || availableSpells[1] == -1) continue;
                        gui_highlightSelectedSpell(false);
                        if (availableSpells[++selectedSpell] == -1) selectedSpell = 0;
                        gui_highlightSelectedSpell(true);
                        gui_drawAllCharPortraitsWithStats();
                    }
                }
                else continue;
                var first = buttons.Count > 0 ? buttons[0] : null;
                if (first != null && activeMagicMenu != -1 && first.defIndex != 15 && first.defIndex != 16)
                {
                    gui_enableDefaultPlayfieldButtons();
                    characters[activeMagicMenu].flags &= 0xffef;
                    gui_drawCharPortraitWithStats(activeMagicMenu);
                    activeMagicMenu = -1;
                }
                buttonListChanged = false;
                foreach (var b in buttons)
                {
                    if (b.callback == null) continue;
                    int result = await b.callback(b);
                    if (result != 0 || (b.flags & 0x20) != 0 || buttonListChanged) break;
                }
            }
        }

        public async Task<string> runLoop()
        {
            flagsTable[73] |= 0x08;
            enableSysTimer(2);
            while (!quit)
            {
                if (restartRequested) return "restart";
                if (nextScriptFunc != 0)
                {
                    int f = nextScriptFunc;
                    nextScriptFunc = 0;
                    await runLevelScript(f, 2);
                }
                try
                {
                    timerUpdate();
                    await drainAsync();
                    if (autoWalk != null) await autoWalkStep();
                    await gui_updateInput();
                    await drainAsync();
                }
                catch (QuitException) { throw; }
                catch (Exception error)
                {
                    // A script or button handler bug must not end the game; report it and keep running.
                    log($"ERROR: {error}");
                    // Undo what an aborted script sequence would normally restore itself.
                    enableSysTimer(2);
                    needSceneRestore = 0;
                    if (dialogueField) restoreAfterDialogueSequence(0);
                    if ((updateFlags & 3) != 0) _ = restoreAfterSceneWindowDialogue(1);   // not awaited in lol.mjs either
                    gui_enableControls();
                    weaponsDisabled = false;
                    updateFlags = 0;
                }
                update();
                if (sceneUpdateRequired) gui_drawScene(0);
                else snd_updateEnvironmentalSfx(0);
                if (partyDamageFlags != -1)
                {
                    await checkForPartyDeath();
                    partyDamageFlags = -1;
                }
                await delay(tickLength);
            }
            return "quit";
        }

        // Resume from a saveState() snapshot.
        public async Task<string> resumeGame(System.Text.Json.Nodes.JsonObject save)
        {
            await preInit();
            setupTimers();
            await startup();
            onStartup?.Invoke();   // the host registers the items it adds, before anything draws
            setupScreenDims();
            gui_enableDefaultPlayfieldButtons();
            await loadState(save);
            screen.fadeFlag = 3;
            sceneUpdateRequired = true;
            return await runLoop();
        }

        public async Task<string> playNewGame(int charSelection)
        {
            await preInit();
            if (playIntro) { try { await showIntro(); } catch (QuitException) { throw; } catch (Exception error) { log($"intro: {error.Message}"); } }
            setupTimers();
            await startup();
            onStartup?.Invoke();
            await startupNew(charSelection);
            screen.fadeFlag = 3;
            sceneUpdateRequired = true;
            return await runLoop();
        }
    }

    /// <summary>JS `throw new Error("quit")` from delay(): the host ended the game.</summary>
    public sealed class QuitException : Exception
    {
        public QuitException() : base("quit") { }
    }
}
