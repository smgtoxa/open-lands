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
LandsOfLore engine = null;
int dice = 0;
var pitEvents = new List<string>();
var names = LandsOfLore.OPCODE_NAMES;
LandsOfLore MakeEngine()
{
    var e = new LandsOfLore(res, sched, log: m => Console.WriteLine($"LOG {m}"));
    e.randomSeed = seed;
    e.presentationSeed = 1;
    e.repairOnLoad = Environment.GetEnvironmentVariable("LOL_REPAIR") == "1";
    e.companionsBench = e.repairOnLoad;   // the page's load-time repairs (off for parity)
    e.ui = (name, args) =>
    {
        if (name == "pit") pitEvents.Add($"{args[0]}");
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
    e.openTalkArchive = name =>
    {
        var p = Path.Combine(data, name);
        return Task.FromResult<ITalkArchive>(File.Exists(p) ? new PakTalkArchive(new PakArchive(File.ReadAllBytes(p))) : null);
    };
    if (Environment.GetEnvironmentVariable("LOL_TRACE_TIMERS") == "1") e.traceTimer = (id, t) => Console.WriteLine($"  TIMER 0x{id:x} @{Math.Round(t / e.tickLength)}");
    if (Environment.GetEnvironmentVariable("LOL_TRACE_DICE") == "1") e.traceDice = (t, p, i, r) => Console.WriteLine($"  DICE {t}d{p}+{i} -> {r}");
    if (Environment.GetEnvironmentVariable("LOL_TRACE_DICE") == "1") e.traceRandom = r => Console.WriteLine($"  RND#{++dice} {r:0.#####} {Environment.StackTrace.Split('\n').Skip(3).Take(2).Select(l => l.Trim().Split(' ').ElementAtOrDefault(1)).Aggregate((a, b) => a + " < " + b)}");
    if (traceOps)
    {
        e.traceLevelScript = (b, f) => Console.WriteLine($"runLevelScript({b}, 0x{f & 0xffff:x})");
        var ops = e.opcodes;
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
    return e;
}
static object Field(object o, string name) =>
    o is System.Collections.Generic.IDictionary<string, object> d ? (d.TryGetValue(name, out var v) ? v : null)
    : o?.GetType().GetProperty(name)?.GetValue(o) ?? o?.GetType().GetField(name)?.GetValue(o);
static string Clean(object t) => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace($"{t}", "[\x01-\x1f]", " "), "\\s+", " ").Trim();
if (Environment.GetEnvironmentVariable("LOL_TRACE_SLEEP") == "1") sched.traceSleep = ms => { Console.WriteLine($"  SLEEP {ms:0.##} @{ticksRun} {string.Join(" < ", Environment.StackTrace.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at Lol.LandsOfLore.") || l.StartsWith("at Lol.Screen.")).Take(4).Select(l => l.Split(' ')[1].Replace("Lol.LandsOfLore.", "").Split('(')[0]))}"); };
engine = MakeEngine();

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
// the game handles one input per main-loop pass (as the original): wait until this one has been taken
void PressKey(string key) { sched.Run(() => engine.events.Add(new InputEvent { type = "key", key = key })); Run(8); for (int i = 0; i < 300 && engine.events.Count > 0; i += 1) Run(1); }
void ClickAt(int x, int y) { sched.Run(() => { engine.pushMouse(x, y, 1); engine.events.Add(new InputEvent { type = "mouseup", x = x, y = y, button = 1 }); }); Run(8); for (int i = 0; i < 300 && engine.events.Count > 0; i += 1) Run(1); }
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
    Console.WriteLine($"  press: no decoration on wall {wall} (shape {engine.wllShapeMap[wall]}): clicking 200x60");
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

// ---- The Imp's Pit, played to the end: a floor that cannot be finished is a bug ----
// The solver walks, turns, clicks and picks up through the game's own input, as the bot below does; only the
// fighting is cut short (a monster in reach dies at once): the question is whether a floor can be finished,
// not whether this party survives it.
bool pitTick = false;
// in the pit test the heroes cannot die: whether a floor can be finished is the question, not whether this party lives
void Tick(int ticks)
{
    for (int i = 0; i < ticks; i += 1)
    {
        Run(1);
        if (!pitTick || engine.dungeon == null) continue;
        foreach (var c in engine.characters) if ((c.flags & 1) != 0) { c.hitPointsMax = Math.Max(c.hitPointsMax, 30000); c.hitPointsCur = c.hitPointsMax; }
        sched.Run(() => engine.uiDungeonTick());
    }
}
string pitWhy = null;
int[] STEPS = { -32, 1, 32, -1 };
PitSecret ToldSecretAt(int b) => engine.dungeon?.secrets?.FirstOrDefault(x => x.block == b && x.told && !x.open);
List<int> PitPath(int from, int to, bool secrets = false)
{
    var traps = new HashSet<int>((engine.dungeon?.traps ?? new List<PitTrap>()).Where(t => t.kind == "teleport").Select(t => t.block));
    var prev = new Dictionary<int, (int b, int d)?> { [from] = null };
    var queue = new Queue<int>();
    queue.Enqueue(from);
    while (queue.Count > 0)
    {
        int b = queue.Dequeue();
        if (b == to) break;
        if (b != from && traps.Contains(b)) continue;
        for (int d = 0; d < 4; d += 1)
        {
            if (!engine.uiPitPassable(b, d) && !(secrets && ToldSecretAt(b + STEPS[d]) != null)) continue;
            int n = b + STEPS[d];
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
bool Hostile(Monster m) => m.properties != null && m.hitPoints > 0 && m.mode < 13 && m.block != 0;
int PitKillNear()
{
    var near = engine.monsters.Where(m => Hostile(m) && engine.getBlockDistance(engine.currentBlock, m.block) <= 1).ToList();
    if (near.Count == 0) return 0;
    sched.Run(() => engine.queueAsync(async () => { foreach (var m in near) if (m.hitPoints > 0) { m.hitPoints = 0; engine.killMonster(m); } await Task.CompletedTask; }));
    Tick(12);
    return near.Count;
}
void PitFace(int d)
{
    for (int i = 0; i < 4 && engine.currentDirection != d; i += 1)
    {
        WaitIdle();
        PressKey(((d - engine.currentDirection + 4) & 3) == 3 ? "q" : "e");
        WaitIdle();
    }
}
bool PitGoto(int target)
{
    int stuck = 0;
    for (int attempt = 0; attempt < 600; attempt += 1)
    {
        if (engine.currentBlock == target) return true;
        HarnessHeal("pit", true);
        PitKillNear();
        WaitIdle();
        var dirs = PitPath(engine.currentBlock, target) ?? PitPath(engine.currentBlock, target, true);
        if (dirs == null) { pitWhy = $"no way from {engine.currentBlock} to {target}"; return false; }
        int d = dirs[0];
        PitFace(d);
        if (engine.currentDirection != d) continue;
        if (ToldSecretAt(engine.currentBlock + STEPS[d]) != null) { ClickAt(200, 50); Tick(10); WaitIdle(); }   // the marked stone slides away
        if (!OpenDoorAhead())
        {
            int db = engine.currentBlock + STEPS[d];
            int w = engine.levelBlockProperties[db].walls[d ^ 2];
            pitWhy = $"the door ahead of {engine.currentBlock} (facing {d}) did not open: wall {w} flags 0x{engine.wllWallFlags[w]:x} type {engine.specialWallTypes[w]} shape {engine.wllShapeMap[w]} walls [{string.Join(",", engine.levelBlockProperties[db].walls)}] objects 0x{engine.levelBlockProperties[db].assignedObjects:x}";
            if (++stuck > 6) return false;
            continue;
        }
        int before = engine.currentBlock;
        if (Environment.GetEnvironmentVariable("LOL_PITSTEP") == "1") Console.WriteLine($"  step {before} -> {before + STEPS[d]} (dir {d}) walls [{string.Join(",", engine.levelBlockProperties[before + STEPS[d]].walls)}] objects 0x{engine.levelBlockProperties[before + STEPS[d]].assignedObjects:x} flags {engine.updateFlags} restore {engine.needSceneRestore}");
        PressKey("w");
        WaitIdle();
        for (int i = 0; i < 150 && engine.currentBlock == before; i += 1) Tick(1);
        if (engine.currentBlock == before)
        {
            PitKillNear();
            if (++stuck > 12) { pitWhy = $"stuck at {before} facing {engine.currentDirection} on the way to {target}"; return false; }
            Tick(30);
        }
    }
    pitWhy = $"never got from {engine.currentBlock} to {target}";
    return engine.currentBlock == target;
}
// stand where a wall's face is worked from: `block` is the wall's block, `face` the face the party looks at
bool PitStandAt(int block, int face)
{
    int stand = block + STEPS[face];
    if (!PitGoto(stand)) return false;
    PitFace(face ^ 2);
    return engine.currentDirection == (face ^ 2);
}
// The floor, drawn: # rock, . open, D door, G gate, @ party, > stairs, L lever (l pulled), S switch, ? sliding stone,
// T teleport, k key, s sigil, i item, m monster, B master
void PitMap(string why)
{
    if (Environment.GetEnvironmentVariable("LOL_PITMAP") != "1") return;
    var d = engine.dungeon;
    if (d == null) return;
    var r = d.plan.rect;
    var marks = new Dictionary<int, char>();
    foreach (var l in d.levers ?? new List<DungeonLever>()) marks[l.block] = l.pulled ? 'l' : 'L';
    if (d.switchWall != null) marks[d.switchWall.block] = 'S';
    foreach (var x in d.secrets) if (!x.open) marks[x.block] = '?';
    foreach (var t in d.traps) marks[t.block] = t.kind == "teleport" ? 'T' : 't';
    foreach (var x in d.doors) marks[x.block] = 'D';
    if (d.gate != null) marks[d.gate.block] = d.gate.open ? 'g' : 'G';
    foreach (int it in d.madeItems) if (engine.itemsInPlay[it].block != 0) marks[engine.itemsInPlay[it].block] = engine.uiExtraItem(it)?.id == "pitkey" ? 'k' : engine.uiExtraItem(it)?.id == "sigil" ? 's' : 'i';
    foreach (var m in engine.monsters) if (m.properties != null && m.hitPoints > 0 && m.block != 0) marks[m.block] = m.dungeonBoss != 0 ? 'B' : 'm';
    marks[d.stairs] = '>';
    marks[engine.currentBlock] = '@';
    var reach = engine.uiPitReach(engine.currentBlock);   // drawn as ',' where the party can walk to now
    foreach (var bm in engine.monsters.Where(m => m.dungeonBoss != 0 && m.hitPoints > 0)) Console.WriteLine($"  master t{bm.type} b{bm.block} xy {bm.x},{bm.y} mode {bm.mode} pflags 0x{bm.properties.flags:x} width {bm.properties.maxWidth} rock {engine.uiDungeonRockWall()} rockflags 0x{engine.wllWallFlags[engine.uiDungeonRockWall()]:x} wall@b [{string.Join(",", engine.levelBlockProperties[bm.block].walls)}]");
    Console.WriteLine($"  map ({why}) start {d.plan.start} stairs {d.stairs} gate {d.gate?.block} party {engine.currentBlock} facing {engine.currentDirection}");
    for (int y = r.y - 1; y <= r.y + r.h; y += 1)
    {
        var row = new System.Text.StringBuilder("  ");
        for (int x = r.x - 1; x <= r.x + r.w; x += 1)
        {
            int b = (y << 5) + x;
            if (marks.TryGetValue(b, out char c)) row.Append(c);
            else row.Append(engine.levelBlockProperties[b].walls.All(w => w == 0) ? (reach.Contains(b) ? ',' : '.') : '#');
        }
        Console.WriteLine(row.ToString());
    }
}
// stand next to `block` on the side the party can reach, facing it
bool PitStandBy(int block)
{
    var reach = engine.uiPitReach(engine.currentBlock);
    for (int d = 0; d < 4; d += 1)
    {
        int stand = block + STEPS[d];
        if (!reach.Contains(stand)) continue;
        if (!PitGoto(stand)) return false;
        PitFace(d ^ 2);
        return engine.currentDirection == (d ^ 2);
    }
    pitWhy = $"nowhere to stand by {block}";
    return false;
}
List<int> PitItems(string id) => engine.dungeon.madeItems.Where(it => engine.itemsInPlay[it].itemPropertyIndex != 0 && engine.uiExtraItem(it)?.id == id && engine.itemsInPlay[it].block != 0
    && !engine.inventory.Contains((ushort)it) && engine.itemInHand != it).ToList();
bool PitTake(int item)
{
    int block = engine.itemsInPlay[item].block;
    if (!PitGoto(block)) return false;
    WaitIdle();
    // a full pack: the party drops something it can spare, as a player would
    if (engine.itemInHand != 0 || Array.IndexOf(engine.inventory, (ushort)0) < 0)
        sched.Run(() => engine.queueAsync(async () =>
        {
            if (engine.itemInHand != 0 && engine.uiExtraItem(engine.itemInHand)?.id != "sigil" && engine.uiExtraItem(engine.itemInHand)?.id != "pitkey") { int h = engine.itemInHand; await engine.setHandItem(0); engine.deleteItem(h); }
            int spare = Array.FindIndex(engine.inventory, it => it != 0 && engine.uiExtraItem(it)?.id != "sigil" && engine.uiExtraItem(it)?.id != "pitkey");
            if (Array.IndexOf(engine.inventory, (ushort)0) < 0 && spare >= 0) { engine.deleteItem(engine.inventory[spare]); engine.inventory[spare] = 0; }
        }));
    Tick(6);
    int got = -1;
    sched.Run(() => engine.queueAsync(async () => { got = await engine.uiTakeFloorItem(item, block); }));
    Tick(20);
    if (got != 1) { pitWhy = $"could not take the {engine.uiExtraItem(item)?.id} at {block}: floor [{string.Join(",", engine.uiFloorItems().Select(f => f.item + "@" + f.block))}] hand {engine.itemInHand} flags {engine.updateFlags} restore {engine.needSceneRestore} paused {engine.sysTimerPaused} item block {engine.itemsInPlay[item].block} level {engine.itemsInPlay[item].level} h {engine.itemsInPlay[item].flyingHeight}"; return false; }
    return true;
}
void PitSaveLoad()
{
    var bad = PackCheck("before the save");
    if (bad != null) Console.WriteLine("  " + bad);
    var st = engine.saveState();
    var old = engine;
    old.quit = true;
    Run(20);
    engine = MakeEngine();
    engine.playIntro = false;
    sched.Start(() => engine.resumeGame(st));
    Run(120);
    WaitIdle();
    var bad2 = PackCheck("after the load");
    if (bad2 != null) Console.WriteLine("  " + bad2);
    if (Environment.GetEnvironmentVariable("LOL_PITSTEP") == "1") Console.WriteLine($"  reload: saved dungeon level {st["dungeon"]?["level"]} stairs {st["dungeon"]?["stairs"]} -> loaded level {engine.dungeon?.level} stairs {engine.dungeon?.stairs} objective {engine.dungeon?.objective} at L{engine.currentLevel} b{engine.currentBlock}");
}
// Plays the floor the party stands on: its puzzle, then the way down. Null when the party stood on the stairs with
// the way open; else what went wrong.
string PitSolve(bool saveLoad)
{
    pitWhy = null;
    var d = engine.dungeon;
    for (int i = 0; i < 200 && !d.ready; i += 1) Tick(1);
    if (!d.ready) return "the floor never became ready";
    string obj = d.objective;
    bool saved = false;
    void Halfway() { if (saveLoad && !saved) { saved = true; PitSaveLoad(); d = engine.dungeon; } }
    switch (obj)
    {
        case "levers":
            if (d.levers.Count == 0) return "a lever floor without levers";
            for (int i = 0; i < d.levers.Count; i += 1)
            {
                var lever = engine.dungeon.levers[i];
                if (lever.pulled) continue;
                if (!PitStandAt(lever.block, lever.dir)) return $"lever {i + 1}/{d.levers.Count}: {pitWhy}";
                PressAhead(); Tick(30); WaitIdle();
                if (!engine.dungeon.levers[i].pulled) return $"lever {i + 1}/{d.levers.Count} at {lever.block} did not move";
                Halfway();
            }
            break;
        case "switch":
        {
            var sw = d.switchWall;
            if (sw == null) return "a switch floor without a switch";
            Halfway();
            if (!PitStandAt(sw.block, sw.dir)) return $"switch: {pitWhy}";
            string at = PressAhead(); Tick(30); WaitIdle();
            if (!engine.dungeon.switchWall.pressed) return $"the switch at {sw.block} did not give (clicked {at}, wall {engine.levelBlockProperties[sw.block].walls[sw.dir]} type {engine.specialWallTypes[engine.levelBlockProperties[sw.block].walls[sw.dir]]})";
            break;
        }
        case "key":
        {
            var keys = PitItems("pitkey");
            if (keys.Count == 0 && engine.dungeon.gate != null && !engine.dungeon.gate.open) return $"no key on the floor: made [{string.Join(",", engine.dungeon.madeItems.Select(it => $"{it}:p{engine.itemsInPlay[it].itemPropertyIndex}:{engine.uiExtraItem(it)?.id}:b{engine.itemsInPlay[it].block}"))}] pitkey prop {engine.pitProp("pitkey")} pack [{string.Join(",", engine.inventory.Select(x => (int)x))}] hand {engine.itemInHand}";
            if (keys.Count != 0)
            {
                if (!PitTake(keys[0])) return $"key: {pitWhy}";
                Halfway();
                if (Array.FindIndex(engine.inventory, it => it != 0 && engine.uiExtraItem(it)?.id == "pitkey") < 0) return "the key is not in the pack after taking it";
                if (!PitStandBy(engine.dungeon.gate.block)) return $"key gate: {pitWhy}";
                if (engine.dungeon.gate.door) PressAhead(); else ClickAt(200, 50);   // a door by its switch; a sealed wall anywhere
                Tick(30); WaitIdle();
            }
            break;
        }
        case "sigils":
        {
            var sigils = PitItems("sigil");
            if (sigils.Count < d.need) return $"{sigils.Count} sigils lie on the floor, {d.need} are asked for";
            foreach (int it in sigils) { if (!PitTake(it)) return $"sigil: {pitWhy}"; Halfway(); }
            Tick(10);
            break;
        }
        case "clear":
            for (int round = 0; round < 60; round += 1)
            {
                var reach = engine.uiPitReach(engine.dungeon.plan.start);
                var foes = engine.monsters.Where(m => Hostile(m) && reach.Contains(m.block)).OrderBy(m => engine.getBlockDistance(engine.currentBlock, m.block)).ToList();
                if (foes.Count == 0) break;
                var m = foes[0];
                if (!PitStandBy(m.block) && engine.getBlockDistance(engine.currentBlock, m.block) > 1) { if (m.hitPoints > 0 && Hostile(m)) return $"clear: {pitWhy}"; }
                PitKillNear();
                if (round == 1) Halfway();
            }
            Tick(10);
            break;
        case "boss":
        {
            Halfway();
            for (int round = 0; round < 10; round += 1)
            {
                var boss = engine.monsters.FirstOrDefault(m => m.dungeonBoss != 0 && m.hitPoints > 0 && m.block != 0);
                if (boss == null) break;
                if (!PitStandBy(boss.block) && engine.getBlockDistance(engine.currentBlock, boss.block) > 1) return $"boss: {pitWhy}";
                PitKillNear();
            }
            break;
        }
        default: return $"unknown objective {obj}";
    }
    d = engine.dungeon;
    if (d.gate != null && !d.gate.open && d.gate.locked) return $"{obj}: the gate stayed shut after the puzzle";
    if (engine.currentBlock == d.stairs)   // standing on them already: step off, and on again
    {
        for (int dir = 0; dir < 4; dir += 1) if (engine.uiPitPassable(d.stairs, dir)) { PitGoto(d.stairs + STEPS[dir]); break; }
    }
    pitEvents.Clear();
    if (!PitGoto(d.stairs)) return $"stairs: {pitWhy}";
    for (int i = 0; i < 200 && !pitEvents.Contains("stairs"); i += 1) Tick(1);   // said once the step has played out
    string blocked = engine.uiDungeonStairsBlocked();
    if (blocked != null) return $"on the stairs: {blocked}";
    if (!pitEvents.Contains("stairs")) return "the stairs did not say so";
    return null;
}
async Task PitEnter(FloorPlan plan, bool descend)
{
    if (descend) await engine.uiDungeonDescend(plan);
    else await engine.uiDungeonEnter(plan);
    if (engine.dungeon != null) await engine.uiDungeonSetup(plan);
}
int pitOk = 0, pitFail = 0;
// the pack must hold each item once, and only items that are records of something carried (not lying anywhere)
HashSet<int> packBaseline = null;   // what the save was already carrying wrong (an older version's doing)
string PackCheck(string when)
{
    var seen = new HashSet<int>();
    foreach (int it in engine.inventory)
    {
        if (it == 0 || (packBaseline != null && packBaseline.Contains(it))) continue;
        if (!seen.Add(it)) return $"{when}: item {it} twice in the pack";
        var rec = engine.itemsInPlay[it];
        if (rec.itemPropertyIndex == 0) return $"{when}: pack holds deleted item {it}";
        if (rec.block != 0) return $"{when}: pack item {it} ({engine.itemName(it)}) also lies on block {rec.block}";
    }
    return null;
}
void PitCase(FloorPlan plan, bool descend, bool saveLoad)
{
    pitTick = true;
    foreach (var c in engine.characters) if ((c.flags & 1) != 0) { c.hitPointsMax = Math.Max(c.hitPointsMax, 30000); c.hitPointsCur = c.hitPointsMax; }
    long t0 = ticksRun;
    sched.Run(() => engine.queueAsync(() => PitEnter(plan, descend)));
    Tick(60);
    WaitIdle();
    string why = PackCheck("before the floor") ?? (engine.dungeon == null ? "could not enter" : PitSolve(saveLoad)) ?? PackCheck("after the floor");
    string what = engine.dungeon != null ? engine.dungeon.objective : plan.objective;
    if (why == null) pitOk += 1; else { pitFail += 1; PitMap(why); Console.WriteLine($"  state: flags {engine.updateFlags} weaponsDisabled {engine.weaponsDisabled} paused {engine.sysTimerPaused} restore {engine.needSceneRestore} hand {engine.itemInHand} flying [{string.Join(" ", engine.flyingObjects.Where(f => f.enable != 0).Select(f => $"{f.item}:{f.attackerId}@{f.x},{f.y}"))}] events {engine.events.Count} party hp [{string.Join(",", engine.characters.Where(c => (c.flags & 1) != 0).Select(c => c.hitPointsCur + "/" + c.flags.ToString("x")))}]"); }
    Console.WriteLine($"PIT L{plan.level} D{plan.depth} {plan.seed} {plan.objective}->{what} {(why == null ? "OK" : "FAIL " + why)} ({(ticksRun - t0) / 60}s)");
}
void PitLeave()
{
    if (engine.dungeon == null) return;
    sched.Run(() => engine.queueAsync(async () => { await engine.uiDungeonLeave(); }));
    Tick(60);
    WaitIdle();
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
        // pittest:L1-L2:D1-D2:DSTEP:SEEDS:SAVE - every objective on every borrowed level and depth, each floor entered
        // from outside, solved and left. pitchain:D1-D2:SAVE - the run as the page plays it: floor after floor down.
        // dupcheck - items listed in two places at once (pack twice, pack and worn, pack and a floor)
        case "dupcheck":
        {
            var where = new Dictionary<int, List<string>>();
            void Add(int it, string w) { if (it == 0) return; if (!where.TryGetValue(it, out var l)) where[it] = l = new List<string>(); l.Add(w); }
            for (int i = 0; i < engine.inventory.Length; i += 1) Add(engine.inventory[i], $"pack{i}");
            foreach (var c in engine.characters) if ((c.flags & 1) != 0) for (int i = 0; i < c.items.Length; i += 1) Add(c.items[i], $"{c.name}{i}");
            Add(engine.itemInHand, "hand");
            foreach (var kv in where) if (kv.Value.Count > 1 || engine.itemsInPlay[kv.Key].block != 0) Console.WriteLine($"dup item {kv.Key} {engine.itemName(kv.Key)} at {string.Join(",", kv.Value)} block {engine.itemsInPlay[kv.Key].block} level {engine.itemsInPlay[kv.Key].level}");
            break;
        }
        case "pittest":
        {
            var a = arg.Split(':');
            int[] R(string x) { var p = x.Split('-').Select(int.Parse).ToArray(); return p.Length == 1 ? new[] { p[0], p[0] } : p; }
            var lv = R(a[0]); var dp = R(a[1]); int dstep = int.Parse(a[2]); int seeds = int.Parse(a[3]); bool sl = a.Length > 4 && a[4] == "1";
            string[] objs = a.Length > 5 ? a[5].Split('+') : new[] { "levers", "switch", "key", "sigils", "clear", "boss" };
            PitLeave();   // a save made inside a floor comes back inside it
            packBaseline = new HashSet<int>(engine.inventory.Select(x => (int)x).GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key));
            for (int l = lv[0]; l <= lv[1]; l += 1)
                for (int depth = dp[0]; depth <= dp[1]; depth += dstep)
                    for (int k = 0; k < seeds; k += 1)
                        foreach (var o2 in objs)
                        {
                            if (l == engine.currentLevel) continue;
                            var plan = new FloorPlan { floor = depth, depth = depth, level = l, objective = o2, need = LandsOfLore.objectiveNeed(o2, depth), seed = $"t:{l}:{depth}:{k}:{o2}", cells = LandsOfLore.floorCells(depth) };
                            PitCase(plan, false, sl && k % 2 == 1);
                            PitLeave();
                        }
            Console.WriteLine($"PIT TOTAL ok {pitOk} fail {pitFail}");
            break;
        }
        case "pitchain":
        {
            var a = arg.Split(':');
            var dp = a[0].Split('-').Select(int.Parse).ToArray();
            int saveEvery = a.Length > 1 ? int.Parse(a[1]) : 0;
            var visited = Enumerable.Range(1, 29).ToList();
            PitLeave();
            packBaseline = new HashSet<int>(engine.inventory.Select(x => (int)x).GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key));
            for (int depth = dp[0]; depth <= dp[1]; depth += 1)
            {
                var plan = LandsOfLore.floorPlan(depth, visited, engine.currentLevel);
                if (plan == null) { Console.WriteLine($"PIT D{depth} no plan"); break; }
                PitCase(plan, engine.dungeon != null, saveEvery > 0 && depth % saveEvery == 0);
            }
            PitLeave();
            Console.WriteLine($"PIT TOTAL ok {pitOk} fail {pitFail}");
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
