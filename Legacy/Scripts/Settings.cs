// lol.settings (src/main.mjs): the page's settings, the same names and defaults, kept as JSON in
// the player's data folder instead of localStorage.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using UnityEngine;

public sealed class Settings
{
    public string Voice = "both";          // both | speech | text
    public bool Sfx = true, Music = true, Scroll = true, Minimap = true;
    public int MinimapRadius = 6, Difficulty = 1;
    public string Autosave = "0";           // minutes, "0" = only when leaving
    public bool AutosaveLevel = true;
    public string UiFont = "normal";
    public bool Colorblind, Mods, Hints, Craft, Flee, Wear, Hd, Randomizer;
    public bool Intro = true;
    public string Respawn = "both";         // off | sleep | reentry | both
    public string InventorySize = "96";
    public string Seed = "", NgPlus = "0";
    public Dictionary<string, string> Keys = new Dictionary<string, string>();

    public bool Speech => Voice != "text";
    public bool Text => Voice != "speech";

    /// <summary>DEFAULT_KEYS.</summary>
    public static readonly Dictionary<string, string> DefaultKeys = new Dictionary<string, string>
    {
        ["attack"] = "F", ["cast"] = "C", ["inventory"] = "I", ["stash"] = "B", ["trade"] = "T", ["rotate"] = "O",
        ["journal"] = "J", ["map"] = "M", ["minimap"] = "N", ["rest"] = "R", ["explore"] = "X",
        ["quicksave"] = "F5", ["quickload"] = "F9", ["photo"] = "F12",
    };

    public static readonly Dictionary<string, string> KeyLabels = new Dictionary<string, string>
    {
        ["attack"] = "Attack with the selected hero", ["cast"] = "Quick cast", ["inventory"] = "Inventory",
        ["stash"] = "Stash the item in hand", ["trade"] = "Trade with the merchant ahead", ["rotate"] = "Rotate the marching order",
        ["journal"] = "Journal", ["map"] = "Full map", ["minimap"] = "Show/hide minimap", ["photo"] = "Screenshot (photo mode)",
        ["rest"] = "Rest", ["explore"] = "Walk to nearest unexplored", ["quicksave"] = "Quick save", ["quickload"] = "Quick load",
    };

    public string KeyFor(string action) => Keys.TryGetValue(action, out var k) && !string.IsNullOrEmpty(k) ? k : DefaultKeys[action];

    /// <summary>keyIs, turned round: which action a pressed key is bound to, if any.</summary>
    public string ActionFor(KeyCode key)
    {
        string name = KeyName(key);
        if (name == null) return null;
        foreach (var action in DefaultKeys.Keys)
            if (string.Equals(KeyFor(action), name, StringComparison.OrdinalIgnoreCase)) return action;
        return null;
    }

    /// <summary>A key as the browser names it (event.key, upper-cased for letters).</summary>
    public static string KeyName(KeyCode key)
    {
        if (key >= KeyCode.A && key <= KeyCode.Z) return ((char)('A' + (key - KeyCode.A))).ToString();
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((char)('0' + (key - KeyCode.Alpha0))).ToString();
        if (key >= KeyCode.F1 && key <= KeyCode.F15) return "F" + (1 + key - KeyCode.F1);
        return null;
    }

    static string PathOf => System.IO.Path.Combine(Application.persistentDataPath, "settings.json");

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(PathOf)) return s;
            var o = JsonNode.Parse(File.ReadAllText(PathOf)) as JsonObject;
            if (o == null) return s;
            string Str(string n, string d) => o[n] is JsonValue v && v.TryGetValue(out string x) ? x : d;
            bool Bool(string n, bool d) => o[n] is JsonValue v && v.TryGetValue(out bool x) ? x : d;
            int Int(string n, int d) => o[n] is JsonValue v && v.TryGetValue(out int x) ? x : d;
            s.Voice = Str("voice", s.Voice); s.Sfx = Bool("sfx", s.Sfx); s.Music = Bool("music", s.Music);
            s.Scroll = Bool("scroll", s.Scroll); s.Minimap = Bool("minimap", s.Minimap);
            s.MinimapRadius = Int("minimapRadius", s.MinimapRadius); s.Difficulty = Int("difficulty", s.Difficulty);
            s.Autosave = Str("autosave", s.Autosave); s.AutosaveLevel = Bool("autosaveLevel", s.AutosaveLevel);
            s.UiFont = Str("uiFont", s.UiFont); s.Colorblind = Bool("colorblind", s.Colorblind);
            s.Mods = Bool("mods", s.Mods); s.Intro = Bool("intro", s.Intro); s.Hints = Bool("hints", s.Hints);
            s.Craft = Bool("craft", s.Craft); s.Flee = Bool("flee", s.Flee); s.Wear = Bool("wear", s.Wear);
            s.Hd = Bool("hd", s.Hd); s.Respawn = Str("respawn", s.Respawn); s.InventorySize = Str("inventorySize", s.InventorySize);
            s.Randomizer = Bool("randomizer", s.Randomizer); s.Seed = Str("seed", s.Seed); s.NgPlus = Str("ngplus", s.NgPlus);
            if (o["keys"] is JsonObject keys)
                foreach (var kv in keys) if (kv.Value is JsonValue v && v.TryGetValue(out string k)) s.Keys[kv.Key] = k;
        }
        catch (Exception e) { Debug.LogWarning($"settings: {e.Message}"); }
        return s;
    }

    public void Save()
    {
        var keys = new JsonObject();
        foreach (var kv in Keys) keys[kv.Key] = kv.Value;
        var o = new JsonObject
        {
            ["voice"] = Voice, ["sfx"] = Sfx, ["music"] = Music, ["scroll"] = Scroll, ["minimap"] = Minimap,
            ["minimapRadius"] = MinimapRadius, ["difficulty"] = Difficulty, ["hd"] = Hd, ["autosave"] = Autosave,
            ["autosaveLevel"] = AutosaveLevel, ["keys"] = keys, ["uiFont"] = UiFont, ["colorblind"] = Colorblind,
            ["mods"] = Mods, ["intro"] = Intro, ["hints"] = Hints, ["craft"] = Craft, ["flee"] = Flee, ["wear"] = Wear,
            ["respawn"] = Respawn, ["inventorySize"] = InventorySize, ["randomizer"] = Randomizer, ["seed"] = Seed, ["ngplus"] = NgPlus,
        };
        try { File.WriteAllText(PathOf, o.ToJsonString()); }
        catch (Exception e) { Debug.LogWarning($"settings: {e.Message}"); }
    }
}
