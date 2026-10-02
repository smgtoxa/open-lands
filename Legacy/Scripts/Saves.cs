// lol.saves (src/platform/saves.mjs): one JSON object, slot name -> {time, level, block, name,
// thumb, state}. Slots: quick, auto, "1".."8", cp1..cp3. The same shape the browser stores, so an
// exported browser saves file can be dropped in as saves.json.
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using UnityEngine;

public sealed class Saves
{
    readonly string _path;
    public Saves(string path) { _path = path; }

    public static readonly string[] ManualSlots = { "1", "2", "3", "4", "5", "6", "7", "8" };

    public sealed class Entry { public string Slot, Name, Thumb; public long Time; public int Level, Block; }

    JsonObject Read()
    {
        try
        {
            if (!File.Exists(_path)) return new JsonObject();
            var node = JsonNode.Parse(File.ReadAllText(_path));
            // An export ({format: "lol-saves", saves: {...}}) is accepted as it is.
            if (node is JsonObject o && o["format"] != null && o["saves"] is JsonObject inner) return (JsonObject)JsonNode.Parse(inner.ToJsonString());
            return node as JsonObject ?? new JsonObject();
        }
        catch (Exception e)
        {
            // A file that cannot be parsed is never written over.
            Debug.LogWarning($"saves: {e.Message}");
            _broken = true;
            return new JsonObject();
        }
    }

    bool _broken;

    void WriteAll(JsonObject all)
    {
        if (_broken) throw new IOException("saves.json cannot be read; it is left alone.");
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, all.ToJsonString());
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }

    public Entry Get(string slot)
    {
        var all = Read();
        return all[slot] is JsonObject e ? ToEntry(slot, e) : null;
    }

    static Entry ToEntry(string slot, JsonObject e) => new Entry
    {
        Slot = slot,
        Time = e["time"] is JsonValue t && t.TryGetValue(out long tv) ? tv : e["time"] is JsonValue t2 && t2.TryGetValue(out double td) ? (long)td : 0,
        Level = e["level"] is JsonValue l && l.TryGetValue(out int lv) ? lv : 0,
        Block = e["block"] is JsonValue b && b.TryGetValue(out int bv) ? bv : 0,
        Name = e["name"] is JsonValue n && n.TryGetValue(out string nv) ? nv : "",
        Thumb = e["thumb"] is JsonValue th && th.TryGetValue(out string thv) ? thv : "",
    };

    public Entry[] All() => Read().Where(kv => kv.Value is JsonObject).Select(kv => ToEntry(kv.Key, (JsonObject)kv.Value)).ToArray();

    public Entry Latest() => All().OrderByDescending(e => e.Time).FirstOrDefault();

    public string State(string slot) => Read()[slot] is JsonObject e && e["state"] != null ? e["state"].ToJsonString() : null;

    public void Write(string slot, int level, int block, string state, string thumb)
    {
        var all = Read();
        string name = all[slot] is JsonObject old && old["name"] is JsonValue n && n.TryGetValue(out string s) ? s : "";
        all[slot] = new JsonObject
        {
            ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["level"] = level,
            ["block"] = block,
            ["name"] = name,
            ["thumb"] = thumb,
            ["state"] = JsonNode.Parse(state),
        };
        WriteAll(all);
    }

    public void Rename(string slot, string name)
    {
        var all = Read();
        if (all[slot] is JsonObject e) { e["name"] = name; WriteAll(all); }
    }

    public void Delete(string slot)
    {
        var all = Read();
        if (all.Remove(slot)) WriteAll(all);
    }
}
