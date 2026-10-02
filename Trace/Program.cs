// The C# engine (Engine/), run the way tools/trace_js.mjs runs the web engine: its real runLoop,
// one tick per scheduler pump, seeded dice, the same steps and the same trace format.
//   dotnet run --project Trace -- "new:0,wait:2,state,key:enter,wait:3,mon"
// Env: LOL_DATA (game DATA folder), LOL_TRACE_OPS=1, LOL_TRACE_UI=1, LOL_SEED.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lol;

string data = Environment.GetEnvironmentVariable("LOL_DATA")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GameData", "DATA"));
if (!File.Exists(Path.Combine(data, "GENERAL.PAK"))) { Console.Error.WriteLine($"no game data at {data}"); return 2; }
bool traceUi = Environment.GetEnvironmentVariable("LOL_TRACE_UI") == "1";
bool traceOps = Environment.GetEnvironmentVariable("LOL_TRACE_OPS") == "1";
uint seed = uint.TryParse(Environment.GetEnvironmentVariable("LOL_SEED"), out var s0) ? s0 : 12345;

var sched = new Scheduler();
long ticksRun = 0;
sched.OnError = e => { if (!(e is QuitException)) Console.WriteLine($"ENGINE ERROR {e}"); };
var res = new Resources("", name =>
{
    var p = Path.Combine(data, name);
    if (!File.Exists(p)) throw new FileNotFoundException(name);
    return Task.FromResult(File.ReadAllBytes(p));
});
var engine = new LandsOfLore(res, sched, log: m => Console.WriteLine($"LOG {m}"));
engine.randomSeed = seed;
engine.presentationSeed = 1;
engine.repairOnLoad = Environment.GetEnvironmentVariable("LOL_REPAIR") == "1";
engine.companionsBench = engine.repairOnLoad;   // the page's load-time repairs (off for parity)
static object Field(object o, string name) =>
    o is System.Collections.Generic.IDictionary<string, object> d ? (d.TryGetValue(name, out var v) ? v : null)
    : o?.GetType().GetProperty(name)?.GetValue(o) ?? o?.GetType().GetField(name)?.GetValue(o);
static string Clean(object t) => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace($"{t}", "[\x01-\x1f]", " "), "\\s+", " ").Trim();
engine.ui = (name, args) =>
{
    if (!traceUi) return;
    if (name == "message") Console.WriteLine($"MSG {Clean(args[0])}");
    else if (name == "dialogue") Console.WriteLine($"SAY {Clean(args[0])}");
    else if (name == "miss") Console.WriteLine($"MISS {Field(args[0], "attacker")} -> {Field(args[0], "target")}");
    else if (name == "damage")
    {
        int att = Convert.ToInt32(Field(args[0], "attacker"));
        var am = (att & 0x8000) != 0 && (att & 0x7fff) < engine.monsters.Length ? engine.monsters[att & 0x7fff] : null;   // -1: a trap (JS reads past the array as undefined)
        Console.WriteLine($"DAMAGE {(Field(args[0], "monster") != null ? "m" + Field(args[0], "monster") : "c" + Field(args[0], "character"))} {Field(args[0], "damage")} by {att}{(am != null ? $" (ps {am.pitScale} ng {am.ngplus})" : "")}");
    }
    else if (name == "kill") Console.WriteLine($"KILL m{Field(args[0], "monster")} by {Field(args[0], "attacker")}");
};
engine.openTalkArchive = name =>
{
    var p = Path.Combine(data, name);
    return Task.FromResult<ITalkArchive>(File.Exists(p) ? new PakTalkArchive(new PakArchive(File.ReadAllBytes(p))) : null);
};
var names = LandsOfLore.OPCODE_NAMES;
if (Environment.GetEnvironmentVariable("LOL_TRACE_TIMERS") == "1") engine.traceTimer = (id, t) => Console.WriteLine($"  TIMER 0x{id:x} @{Math.Round(t / engine.tickLength)}");
if (Environment.GetEnvironmentVariable("LOL_TRACE_SLEEP") == "1") sched.traceSleep = ms => { Console.WriteLine($"  SLEEP {ms:0.##} @{ticksRun} {string.Join(" < ", Environment.StackTrace.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at Lol.LandsOfLore.") || l.StartsWith("at Lol.Screen.")).Take(4).Select(l => l.Split(' ')[1].Replace("Lol.LandsOfLore.", "").Split('(')[0]))}"); };
int dice = 0;
if (Environment.GetEnvironmentVariable("LOL_TRACE_DICE") == "1") engine.traceDice = (t, p, i, r) => Console.WriteLine($"  DICE {t}d{p}+{i} -> {r}");
if (Environment.GetEnvironmentVariable("LOL_TRACE_DICE") == "1") engine.traceRandom = r => Console.WriteLine($"  RND#{++dice} {r:0.#####} {Environment.StackTrace.Split('\n').Skip(3).Take(2).Select(l => l.Trim().Split(' ').ElementAtOrDefault(1)).Aggregate((a, b) => a + " < " + b)}");
if (traceOps)
{
    engine.traceLevelScript = (b, f) => Console.WriteLine($"runLevelScript({b}, 0x{f & 0xffff:x})");
    var ops = engine.opcodes;
    for (int i = 0; i < ops.Length; i += 1)
    {
        var f = ops[i];
        if (f == null) continue;
        string name = i < names.Length && names[i] != "" ? names[i] : $"op{i}";
        ops[i] = async st =>
        {
            int r = await f(st);
            if (name != "updateBlockAnimations") Console.WriteLine($"  op {name}({st.arg(0)},{st.arg(1)},{st.arg(2)}) -> {r}");
            return r;
        };
    }
}

void Run(int ticks)
{
    // clockTicks * tickLength, as the JS virtual clock computes it: a running sum drifts by an ulp
    // and flips `nextRun <= now` comparisons, which moves timers by a tick.
    for (int i = 0; i < ticks; i += 1) { ticksRun += 1; sched.Pump(ticksRun * engine.tickLength); }
}
var keys = new Dictionary<string, string>
{
    ["enter"] = "Enter", ["space"] = " ", ["up"] = "ArrowUp", ["down"] = "ArrowDown", ["left"] = "ArrowLeft",
    ["right"] = "ArrowRight", ["turnl"] = "Home", ["turnr"] = "PageUp", ["esc"] = "Escape",
};


// Navigation, identical in tools/trace_js.mjs: path over passable walls (closed doors with a switch
// count), turn with keys, open the door ahead by clicking its switch, answer boxes with Enter.
bool Passable(int from, int dir)
{
    int to = engine.calcNewBlockPosition(from, dir);
    int wall = engine.levelBlockProperties[to].walls[dir ^ 2];
    int flags = engine.wllWallFlags[wall];
    if ((flags & 1) == 0) return true;
    return (flags & 8) != 0 && engine.wllShapeMap[wall] > 0;
}
List<int> FindPath(int from, int to)
{
    var prev = new Dictionary<int, (int b, int d)?> { [from] = null };
    var queue = new Queue<int>();
    queue.Enqueue(from);
    while (queue.Count > 0)
    {
        int b = queue.Dequeue();
        if (b == to) break;
        for (int d = 0; d < 4; d += 1)
        {
            if (!Passable(b, d)) continue;
            int n = engine.calcNewBlockPosition(b, d);
            if (prev.ContainsKey(n)) continue;
            prev[n] = (b, d);
            queue.Enqueue(n);
        }
    }
    if (!prev.ContainsKey(to)) return null;
    var dirs = new List<int>();
    for (int b = to; prev[b] != null; b = prev[b].Value.b) dirs.Insert(0, prev[b].Value.d);
    return dirs;
}
void PressKey(string key) { sched.Run(() => engine.events.Add(new InputEvent { type = "key", key = key })); Run(8); }
void ClickAt(int x, int y) { sched.Run(() => { engine.pushMouse(x, y, 1); engine.events.Add(new InputEvent { type = "mouseup", x = x, y = y, button = 1 }); }); Run(8); }
bool WaitIdle(int max = 300)
{
    for (int i = 0; i < max; i += 1)
    {
        if ((engine.updateFlags & 3) == 0 && engine.needSceneRestore == 0 && !engine.weaponsDisabled) return true;
        if (engine.needSceneRestore != 0 && i % 40 == 39) sched.Run(() => engine.events.Add(new InputEvent { type = "key", key = "Enter" }));
        Run(1);
    }
    return false;
}
// press - click the decoration on the wall ahead (a button, a lever, a lock), where the game drew it
string PressAhead()
{
    int block = engine.calcNewBlockPosition(engine.currentBlock, engine.currentDirection);
    int wall = engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2];
    for (int l = engine.wllShapeMap[wall]; l > 0; l = engine.levelDecorationProperties[l].next)
    {
        var p = engine.levelDecorationProperties[l];
        if (p.shapeIndex[1] == 0xffff) continue;
        var shape = engine.levelDecorationShapes[p.shapeIndex[1]];
        int x = p.shapeX[1] + engine.clickedShapeXOffs + (shape.width >> 1), y = p.shapeY[1] + engine.clickedShapeYOffs + (shape.height >> 1);
        ClickAt(x, y);
        return $"{x}x{y}";
    }
    ClickAt(200, 60);
    return "200x60";
}

// pick - take what is drawn in the view (a niche, a table, the floor ahead), as a player clicks it: the
// engine hit-tests items by the item buffer it redraws, so find a drawn item pixel and click there; each
// item goes to the first free inventory slot. Up to 8.
int[] SceneItemAt()
{
    int cp = engine.screen.curPage;
    engine.screen.curPage = engine.sceneDrawPage1;
    engine.redrawSceneItem();
    int[] at = null;
    for (int y = 2; y < 118 && at == null; y += 2) for (int x = 114; x < 286 && at == null; x += 2) if (engine.screen.getPagePixel(engine.screen.curPage, x, y) != 0) at = new[] { x, y };
    engine.screen.curPage = cp;
    return at;
}
List<string> PickScene()
{
    var got = new List<string>();
    for (int i = 0; i < 8; i += 1)
    {
        WaitIdle();
        var at = SceneItemAt();
        if (at == null) break;
        ClickAt(at[0], at[1]);
        Run(4);
        if (engine.itemInHand == 0) break;
        got.Add(engine.itemName(engine.itemInHand));
        int slot = Array.IndexOf(engine.inventory, (ushort)0);
        if (slot < 0) break;
        sched.Run(() => engine.queueAsync(async () => { await engine.inventorySlotClick(slot); }));
        Run(6);
    }
    return got;
}

bool OpenDoorAhead()
{
    int block = engine.calcNewBlockPosition(engine.currentBlock, engine.currentDirection);
    int wall = engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2];
    if ((engine.wllWallFlags[wall] & 1) == 0) return true;
    int l = engine.wllShapeMap[wall];
    while (l > 0)
    {
        var p = engine.levelDecorationProperties[l];
        if (p.shapeIndex[1] != 0xffff)
        {
            var shape = engine.levelDecorationShapes[p.shapeIndex[1]];
            ClickAt(p.shapeX[1] + engine.clickedShapeXOffs + (shape.width >> 1), p.shapeY[1] + engine.clickedShapeYOffs + (shape.height >> 1));
            break;
        }
        l = p.next;
    }
    for (int i = 0; i < 200; i += 1)
    {
        Run(1);
        if ((engine.wllWallFlags[engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2]] & 1) == 0) return true;
    }
    return false;
}
// Fight what stands next to the party, as a player would (never a peaceful one - mode 1, the game's own
// test in checkMonsterTypeHostility - such as the gate guard): face it, swing with whoever is ready, until
// nothing has been in reach for a second and a half (or maxSec of game time, or the party is dead).
// The harness keeps the party alive (it tests the game along the walkthrough, not whether a new party
// survives it): full health and magic for every living hero, logged.
void HarnessHeal(string why, bool quiet = false)
{
    foreach (var c in engine.characters) if ((c.flags & 1) != 0 && c.hitPointsCur > 0) { c.hitPointsCur = c.hitPointsMax; c.magicPointsCur = c.magicPointsMax; }
    if (!quiet) Console.WriteLine($"harness heal ({why})");
}
bool Hurt() => engine.characters.Any(c => (c.flags & 1) != 0 && c.hitPointsCur > 0 && c.hitPointsCur < c.hitPointsMax);

int FightNear(double maxSec)
{
    int limit = (int)Math.Round(maxSec * 1000 / engine.tickLength);
    int quiet = 0, swings = 0, heals = 0;
    for (int t = 0; t < limit;)
    {
        if (!engine.characters.Any(c => (c.flags & 1) != 0 && c.hitPointsCur > 0)) break;
        var near = engine.monsters.Where(m => m.properties != null && m.hitPoints > 0 && m.mode != 1 && m.mode < 13 && m.block != 0 && engine.getBlockDistance(engine.currentBlock, m.block) <= 1).ToList();
        // quiet only when nothing is even on its way (within two blocks)
        bool coming = near.Count > 0 || engine.monsters.Any(m => m.properties != null && m.hitPoints > 0 && m.mode != 1 && m.mode < 13 && m.block != 0 && engine.getBlockDistance(engine.currentBlock, m.block) <= 2);
        if (near.Count == 0) { if (coming) quiet = 0; else if (++quiet > 90) break; Run(1); t += 1; continue; }
        quiet = 0;
        if (Hurt()) { HarnessHeal("fight", true); heals += 1; }
        int d = Array.IndexOf(new[] { -32, 1, 32, -1 }, near[0].block - engine.currentBlock);
        if (d >= 0 && d != engine.currentDirection && (engine.updateFlags & 3) == 0) { PressKey(((d - engine.currentDirection + 4) & 3) == 3 ? "q" : "e"); t += 8; continue; }
        if (!new[] { 0, 1, 2, 3 }.Any(c => (engine.characters[c].flags & 1) != 0 && engine.characters[c].hitPointsCur > 0 && engine.uiCanAct(c))) { Run(5); t += 5; continue; }
        sched.Run(() => engine.queueAsync(async () => { await engine.quickAttack(); }));
        swings += 1;
        Run(10); t += 10;
    }
    if (heals != 0) Console.WriteLine($"harness heals in the fight: {heals}");
    return swings;
}

bool GotoBlock(int target, bool fight = false)
{
    int stuck = 0;
    for (int attempt = 0; attempt < (fight ? 400 : 60); attempt += 1)
    {
        if (engine.currentBlock == target) return true;
        if (fight)
        {
            int n = FightNear(120); if (n != 0) Console.WriteLine($"fought at {engine.currentBlock}: {n} swings");
            if (Hurt()) HarnessHeal("walk");
        }
        WaitIdle();
        var dirs = FindPath(engine.currentBlock, target);
        if (dirs == null) { Console.WriteLine($"no path from {engine.currentBlock} to {target}"); return false; }
        int d = dirs[0];
        int diff = (d - engine.currentDirection + 4) & 3;
        if (diff == 1) PressKey("e");
        else if (diff == 3) PressKey("q");
        else if (diff == 2) { PressKey("e"); WaitIdle(); PressKey("e"); }
        WaitIdle();
        if (engine.currentDirection != d) continue;
        if (!OpenDoorAhead()) Console.WriteLine($"door ahead of {engine.currentBlock} did not open");
        int before = engine.currentBlock;
        PressKey("w");
        WaitIdle();
        for (int i = 0; i < 150 && engine.currentBlock == before; i += 1) Run(1);
        if (engine.currentBlock == before)
        {
            Console.WriteLine($"stuck at {before} facing {engine.currentDirection}; flags {engine.updateFlags}");
            if (++stuck > 6) return false;
            Run(30);
        }
    }
    return engine.currentBlock == target;
}

sched.Run(() => sched.Observe(engine.preInit()));
sched.Pump(0);   // no time passes before the first step: the JS clock starts at tick 0 there too
foreach (var step in (args.Length > 0 ? args[0] : "new:0,wait:2,state").Split(','))
{
    int colon = step.IndexOf(':');
    string cmd = colon < 0 ? step : step.Substring(0, colon), arg = colon < 0 ? "" : step.Substring(colon + 1);
    Console.WriteLine($">> {step} (block {engine.currentBlock} dir {engine.currentDirection})");
    switch (cmd)
    {
        case "load":
        {
            var parts = arg.Split('#');
            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(parts[0])).AsObject();
            var state = saved["state"] is System.Text.Json.Nodes.JsonObject st ? st
                : saved["characters"] != null ? saved : saved[parts.Length > 1 ? parts[1] : "auto"]["state"].AsObject();
            engine.playIntro = false;
            sched.Start(() => engine.resumeGame(state));
            if (Environment.GetEnvironmentVariable("LOL_TRACE_SLEEP") == "1") Console.WriteLine($"  started: next due {sched.NextDue}");
            Run(1);
            break;
        }
        case "tp":
        {
            var lb = arg.Split('/');
            int lv = int.Parse(lb[0]);
            int? bl = lb.Length > 1 ? int.Parse(lb[1]) : (int?)null;
            sched.Run(() => engine.queueAsync(() => engine.debugTeleport(lv, bl)));
            Run(1);
            break;
        }
        case "new":
            engine.playIntro = false;
            engine.smoothScrollingEnabled = true;
            sched.Start(() => engine.playNewGame(int.Parse(arg)));
            Run(1);
            break;
        case "wait": Run((int)Math.Round(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * 1000 / engine.tickLength)); break;
        case "key": sched.Run(() => engine.events.Add(new InputEvent { type = "key", key = keys.TryGetValue(arg, out var k) ? k : arg })); Run(6); break;
        case "click": case "rclick":
        {
            var xy = arg.Split('x').Select(int.Parse).ToArray();
            int b = cmd == "click" ? 1 : 2;
            sched.Run(() => { engine.pushMouse(xy[0], xy[1], b); engine.events.Add(new InputEvent { type = "mouseup", x = xy[0], y = xy[1], button = b }); });
            Run(6);
            break;
        }
        case "goto": Console.WriteLine($"goto {arg} -> {(GotoBlock(int.Parse(arg)) ? "true" : "false")} at {engine.currentBlock}"); break;
        case "idle": WaitIdle(); break;
        // walk:B - goto, fighting whatever stands in reach on the way; fight:S - fight what is near for up to S seconds
        case "walk": Console.WriteLine($"walk {arg} -> {(GotoBlock(int.Parse(arg), true) ? "true" : "false")} at {engine.currentBlock}"); break;
        case "heal": HarnessHeal("step"); break;
        // give:PROP - an item of that property into the pack (as the giveItem opcode does); setflag:N - a game flag
        // the story would have set by now (checkpoints start from a teleport, not a played-through game)
        case "give": { int it = engine.makeItem(int.Parse(arg), 0, 0); Console.WriteLine($"give {engine.itemName(it)}({arg}) -> {(engine.addItemToInventory(it) ? 1 : 0)}"); break; }
        // hold:PROP - take the first item of that property from the pack into the hand
        case "hold":
        {
            int slot = Array.FindIndex(engine.inventory, it => it != 0 && engine.itemsInPlay[it].itemPropertyIndex == int.Parse(arg));
            if (slot >= 0) { sched.Run(() => engine.queueAsync(async () => { await engine.inventorySlotClick(slot); })); Run(6); }
            Console.WriteLine($"hold {arg} -> {(engine.itemInHand != 0 ? engine.itemName(engine.itemInHand) : "-")}");
            break;
        }
        // clear - the level's hostile monsters killed as the game kills them (killMonster: their items drop): a
        // checkpoint tests the level's mechanics, and a fight with a party it was not built for is noise there
        case "clear":
            sched.Run(() => engine.queueAsync(async () =>
            {
                int n = 0;
                foreach (var m in engine.monsters) if (m.properties != null && m.hitPoints > 0 && m.mode != 1 && m.mode < 13) { m.hitPoints = 0; engine.killMonster(m); n += 1; }
                Console.WriteLine($"clear {n}");
                await Task.CompletedTask;
            }));
            Run(6);
            break;
        // equip:PROP - the first item of that property into Ak'shel's weapon hand (as "Equip best" puts items on)
        case "equip":
        {
            int slot = Array.FindIndex(engine.inventory, it => it != 0 && engine.itemsInPlay[it].itemPropertyIndex == int.Parse(arg));
            if (slot >= 0) sched.Run(() => engine.queueAsync(async () => Console.WriteLine($"equip {arg} -> {((await engine.uiDropInventoryOn(slot, 0, 0)) != 0 ? 1 : 0)}")));
            Run(10);
            break;
        }
        // prop:NAME - an engine field as it is now (true/false/number)
        case "prop":
        {
            object v = typeof(LandsOfLore).GetField(arg)?.GetValue(engine) ?? typeof(LandsOfLore).GetProperty(arg)?.GetValue(engine);
            Console.WriteLine($"prop {arg} = {(v is bool b ? (b ? "true" : "false") : v?.ToString() ?? "-")}");
            break;
        }
        // pagesum - a checksum of every screen page that exists (what the engine has drawn where)
        case "pagesum":
        {
            var outp = new List<string>();
            for (int n = 0; n < 16; n += 1)
            {
                if (!engine.screen.pages.TryGetValue(n, out var pg) || pg == null) continue;
                uint h = 0; foreach (var v in pg) h = unchecked(h * 31 + v);
                outp.Add($"{n}:{h:x}");
            }
            Console.WriteLine($"pages {string.Join(" ", outp)}");
            break;
        }
        case "setflag": engine.setGameFlag(int.Parse(arg)); Console.WriteLine($"setflag {arg}"); break;
        case "buttons":
        {
            var bs = Enumerable.Range(0, engine.dialogueNumButtons).Select(i => $"{i}:{((engine.dialogueButtonString != null && i < engine.dialogueButtonString.Length ? engine.dialogueButtonString[i] : null) ?? "...").Trim()}");
            Console.WriteLine($"buttons {(engine.dialogueNumButtons > 0 ? string.Join(" | ", bs) : "-")}");
            break;
        }
        case "pick": { var got = PickScene(); Console.WriteLine($"pick {(got.Count > 0 ? string.Join(", ", got) : "-")}"); break; }
        case "press": Console.WriteLine($"press {PressAhead()}"); Run(30); break;
        // take - pick up everything on the floor in reach, as the page's "Take all" does
        case "take":
            sched.Run(() => engine.queueAsync(async () =>
            {
                foreach (var f in engine.uiFloorItems()) { int ok = await engine.uiTakeFloorItem(f.item, f.block); Console.WriteLine($"take {f.name} -> {(ok != 0 ? 1 : 0)}"); if (ok == 0) break; }
            }));
            Run(20);
            break;
        case "fight": Console.WriteLine($"fight -> {FightNear(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture))} swings"); break;
        // face:D - turn (with the turn keys) until facing D (0 N, 1 E, 2 S, 3 W)
        case "face":
        {
            int d = int.Parse(arg);
            for (int i = 0; i < 4 && engine.currentDirection != d; i += 1)
            {
                WaitIdle();
                PressKey(((d - engine.currentDirection + 4) & 3) == 3 ? "q" : "e");
                WaitIdle();
            }
            Console.WriteLine($"face {arg} -> {engine.currentDirection}");
            break;
        }
        case "cflags":
            for (int i = 0; i < 4; i += 1)
            {
                var c = engine.characters[i];
                if ((c.flags & 1) != 0) Console.WriteLine($"char{i} {c.name} flags 0x{c.flags:x} hp {c.hitPointsCur}/{c.hitPointsMax} mp {c.magicPointsCur} ev [{string.Join(",", c.characterUpdateEvents)}] dl [{string.Join(",", c.characterUpdateDelay)}]");
            }
            break;
        case "choose": sched.Run(() => engine.uiChoose(int.Parse(arg))); Run(6); break;
        // next:N - move a scene on N times as a player does: click the first dialogue button if there is one (MORE, OK),
        // else a key press; three seconds after each
        case "next":
            for (int i = 0; i < int.Parse(arg); i += 1)
            {
                if (engine.dialogueNumButtons != 0)
                {
                    // press and release on separate ticks: one in the same tick is not seen as a click
                    int x = engine.dialogueButtonPosX[0] + ((engine.dialogueButtonWidth != 0 ? engine.dialogueButtonWidth : 74) >> 1), y = engine.dialogueButtonPosY[0] + 4;
                    sched.Run(() => engine.pushMouse(x, y, 1)); Run(3);
                    sched.Run(() => engine.events.Add(new InputEvent { type = "mouseup", x = x, y = y, button = 1 })); Run(5);
                }
                // a key only when something waits for one (on the playfield Enter casts the selected spell)
                else if ((engine.updateFlags & 3) != 0 || engine.needSceneRestore != 0 || engine.activeTim.Any(t => t != null)) PressKey("Enter");
                Run((int)Math.Round(3000 / engine.tickLength));
            }
            break;
        case "call":
        {
            // call:method/arg/arg - an engine method as the page calls it (on the engine's action queue)
            var parts = arg.Split('/');
            string name = parts[0];
            var raw = parts.Skip(1).ToArray();
            var method = typeof(LandsOfLore).GetMethods().First(m => m.Name == name && m.GetParameters().Length >= raw.Length
                && m.GetParameters().Skip(raw.Length).All(p => p.IsOptional));
            var ps = method.GetParameters();
            var callArgs = ps.Select((p, i) => i >= raw.Length ? p.DefaultValue
                : p.ParameterType == typeof(bool) ? (object)(raw[i] == "true")
                : p.ParameterType == typeof(string) ? raw[i]
                : Convert.ChangeType(double.Parse(raw[i], System.Globalization.CultureInfo.InvariantCulture), Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType)).ToArray();
            sched.Run(() => engine.queueAsync(async () =>
            {
                object r = method.Invoke(engine, callArgs);
                if (r is Task t)
                {
                    await t;
                    var prop = t.GetType().GetProperty("Result");
                    r = prop != null && t.GetType().IsGenericType ? prop.GetValue(t) : null;
                    if (r != null && r.GetType().Name == "VoidTaskResult") r = null;
                }
                string shown = r == null ? "-" : r is bool bo ? (bo ? "true" : "false") : r is string || r.GetType().IsPrimitive ? Convert.ToString(r, System.Globalization.CultureInfo.InvariantCulture) : "object";
                Console.WriteLine($"call {name} -> {shown}");
            }));
            Run(6);
            break;
        }
        case "mon":
            Console.WriteLine("monsters: " + string.Join(" ", engine.monsters.Select((m, i) => (m, i))
                .Where(t => t.m.properties != null && t.m.hitPoints > 0).Select(t => $"#{t.i} t{t.m.type} b{t.m.block} mode{t.m.mode}")));
            break;
        case "palsum":   // the palette on screen, summed (lighting, fades)
            Console.WriteLine("palsum " + engine.screen.screenPalette.Sum(b => (int)b) + " fade " + engine.screen.fadeFlag);
            break;
        case "monshp":   // the live monsters' drawing: shape index, frames loaded, flags, draw position
            Console.WriteLine("level types: " + string.Join(",", engine.levelMonsterTypes));
            foreach (var m in engine.monsters.Where(m => m.properties != null && m.hitPoints > 0 && m.block != 0))
            {
                int si = m.properties.shapeIndex;
                int loaded = Enumerable.Range(0, 16).Count(k => (si << 4) + k < engine.monsterShapes.Length && engine.monsterShapes[(si << 4) + k] != null);
                var f0 = engine.monsterShapes[si << 4]; var pal0 = engine.monsterPalettes[si << 4];
                Console.WriteLine($"m{m.id} t{m.type} b{m.block} si{si} key{f0?.key} {f0?.width}x{f0?.height} pal{pal0?.Length} colors{f0?.colorCount} frames{loaded} flags0x{m.flags:x} pflags0x{m.properties.flags:x} xy{m.x},{m.y} shift{m.shiftStep} facing{m.facing} drawW{m.drawW} drawSerial{m.drawSerial}/{engine.sceneSerial} props{(m.properties == engine.monsterProperties.ElementAtOrDefault(m.type) ? "same" : "OTHER")}");
            }
            break;
        case "ppm":   // ppm:PATH - the screen (page 0, screen palette) as a PPM picture
        {
            var pg = engine.screen.page(0); var pal = engine.screen.screenPalette;
            using var fs = File.Create(arg);
            var head = System.Text.Encoding.ASCII.GetBytes("P6 320 200 255\n"); fs.Write(head, 0, head.Length);
            foreach (var px in pg) { fs.WriteByte((byte)(pal[px * 3] * 255 / 63)); fs.WriteByte((byte)(pal[px * 3 + 1] * 255 / 63)); fs.WriteByte((byte)(pal[px * 3 + 2] * 255 / 63)); }
            break;
        }
        case "walls":   // walls:B.B - the four wall types of blocks
            Console.WriteLine("walls " + string.Join(" ", arg.Split('.').Select(b => $"{b}=[{string.Join(",", engine.levelBlockProperties[int.Parse(b)].walls)}]")));
            break;
        case "monall":   // every slot: type, block, mode, hit points
            Console.WriteLine("slots: " + string.Join(" ", engine.monsters.Select((m, i) => $"#{i} t{m.type} b{m.block} mode{m.mode} hp{m.hitPoints}")));
            break;
        case "state":
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                level = engine.currentLevel, block = engine.currentBlock, dir = engine.currentDirection, hand = engine.itemInHand,
                flags = engine.updateFlags, hp = engine.characters[0].hitPointsCur, suspend = engine.suspendScript, f73 = (int)engine.flagsTable[73], inv = engine.inventory.Take(9).Select(v => (int)v), act = string.Concat(engine.activeTim.Select(t => t != null ? "1" : "0")),
            }));
            break;
        case "save": File.WriteAllText(arg, engine.saveState().ToJsonString()); break;
        // flags:4.252.11 - game flags (quest progress); items - the hand and the pack, by name and property
        case "flags":
            Console.WriteLine("flags " + string.Join(" ", arg.Split('.').Select(f => $"{f}={(engine.queryGameFlag(int.Parse(f)) != 0 ? 1 : 0)}")));
            break;
        case "items":
        {
            string name(int it) => $"{engine.itemName(it)}({engine.itemsInPlay[it].itemPropertyIndex})";
            var inv = engine.inventory.Select((it, i) => it != 0 ? $"{i}:{name(it)}" : null).Where(x => x != null);
            var worn = engine.characters.Where(c => (c.flags & 1) != 0).Select(c => $"{c.name}[{string.Join(" ", c.items.Select(it => it != 0 ? name(it) : "-"))}]");
            Console.WriteLine($"items hand {(engine.itemInHand != 0 ? name(engine.itemInHand) : "-")} | {string.Join(" ", inv)} | {string.Join(" ", worn)} | crowns {engine.credits}");
            break;
        }
        default: Console.WriteLine($"unknown step {step}"); break;
    }
}
engine.quit = true;
return 0;

sealed class PakTalkArchive : ITalkArchive
{
    readonly PakArchive _pak;
    public PakTalkArchive(PakArchive pak) { _pak = pak; }
    public bool has(string name) => _pak.has(name);
    public Task<byte[]> get(string name) => Task.FromResult(_pak.get(name));
}
