// Headless walk, the C# twin of the browser build's scripts/headless_walk.mjs:
//
//   dotnet run --project Harness -- "new:0,wait:2,mon,state,key:enter,wait:3,mon,state"
//
// Steps: new:N (new game, champion N) | load:FILE (a saveState JSON) | wait:SECONDS | key:NAME
// (enter space up down left right turnl turnr esc or a scan code) | click:XxY | rclick:XxY |
// choose:N | mon | state | shot:FILE.ppm | save:FILE
// Env: LOL_TRACE_OPS=1 prints every script opcode (names from the JS opcode table), LOL_TRACE_UI=1
// prints the log lines, LOL_DATA points at the game's DATA folder.
using System.Text.Json;
using LolCore;

string data = Environment.GetEnvironmentVariable("LOL_DATA")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GameData", "DATA"));
if (!File.Exists(Path.Combine(data, "GENERAL.PAK"))) { Console.Error.WriteLine($"no game data at {data}"); return 2; }
byte[] Read(string name) { var p = Path.Combine(data, name); return File.Exists(p) ? File.ReadAllBytes(p) : null; }

var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "opcode-names.json")));
var game = new Session();
bool traceUi = Environment.GetEnvironmentVariable("LOL_TRACE_UI") == "1";
game.Message = (t, k) => { if (traceUi) Console.WriteLine($"{(k == "say" ? "SAY" : "MSG")} {t}"); };
game.Died = () => Console.WriteLine("DIED");
game.Boot(Read, 12345);
if (Environment.GetEnvironmentVariable("LOL_TRACE_OPS") == "1")
    game.Loader.Trace = (id, a0, a1, a2, result) =>
    {
        string name = id >= 0 && id < names.Length && names[id] != "" ? names[id] : $"op{id}";
        if (name != "updateBlockAnimations") Console.WriteLine($"  op {name}({a0},{a1},{a2}) -> {result}");
    };

var keys = new Dictionary<string, int>
{
    ["enter"] = 43, ["space"] = 61, ["up"] = 96, ["down"] = 98, ["left"] = 92, ["right"] = 102,
    ["turnl"] = 91, ["turnr"] = 101, ["esc"] = 110,
};
void Ticks(double seconds) { int n = (int)Math.Round(seconds * 1000 / LevelLoader.TickLength); for (int i = 0; i < n; i += 1) { game.Tick(); game.Draw(); } }

foreach (var step in (args.Length > 0 ? args[0] : "new:0,wait:2,state").Split(','))
{
    int colon = step.IndexOf(':');
    string cmd = colon < 0 ? step : step[..colon], arg = colon < 0 ? "" : step[(colon + 1)..];
    Console.WriteLine($">> {step} (block {game.Party?.Block ?? 0} dir {game.Party?.Direction ?? 0})");
    switch (cmd)
    {
        case "new": game.NewGame(int.Parse(arg)); break;
        case "load": game.LoadState(File.ReadAllText(arg)); break;
        case "save": File.WriteAllText(arg, game.SaveState()); break;
        case "wait": Ticks(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)); break;
        case "key":
            int code = keys.TryGetValue(arg, out int k) ? k : int.Parse(arg);
            if ((code == 43 || code == 61) && game.Choices() != null) { game.Loader.StopSpeech?.Invoke(); game.Choose(0); }
            else game.PushKey(code, false);
            Ticks(0.1);
            break;
        case "click": case "rclick":
            var xy = arg.Split('x').Select(int.Parse).ToArray();
            game.PushClick(xy[0], xy[1], cmd == "click" ? 1 : 2);
            Ticks(0.1);
            break;
        case "choose": game.Choose(int.Parse(arg)); Ticks(0.1); break;
        case "mon":
            var ms = game.Loader.Board.Monsters;
            Console.WriteLine("monsters: " + string.Join(" ", Enumerable.Range(0, ms.Length)
                .Where(i => ms[i] != null && ms[i].HitPoints > 0)
                .Select(i => $"#{i} t{ms[i].Type} b{ms[i].Block} mode{ms[i].Mode}")));
            break;
        case "state":
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                level = game.Loader.Level, block = game.Party.Block, dir = game.Party.Direction, hand = game.Gui.ItemInHand,
                flags = game.Loader.UpdateFlags, hp = game.Loader.Characters[0].HitPointsCur,
                inv = game.Loader.Items.Inventory.Take(9),
            }));
            break;
        case "shot":
            // A plain PPM: no image library needed to look at what the engine drew.
            var page = game.Screen.Page(0); var pal = game.Screen.ScreenPalette;
            using (var f = File.Create(arg))
            {
                var head = System.Text.Encoding.ASCII.GetBytes("P6\n320 200\n255\n");
                f.Write(head);
                foreach (byte i in page) { f.WriteByte((byte)(pal[i * 3] * 255 / 63)); f.WriteByte((byte)(pal[i * 3 + 1] * 255 / 63)); f.WriteByte((byte)(pal[i * 3 + 2] * 255 / 63)); }
            }
            break;
        default: Console.WriteLine($"unknown step {step}"); break;
    }
}
return 0;
