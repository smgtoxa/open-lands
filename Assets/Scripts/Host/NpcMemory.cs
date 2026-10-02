// src/platform/npcs.mjs. C# 9.
// NPC memory: who you talked to, where, what they said, with a snapshot of their portrait.
//
// Who a dialogue script belongs to is the engine's business (src/game/npcs.mjs); what is here is
// the browser's log of them.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lol;

namespace LolHost
{
    /// <summary>lol.npcs[name]: { name, level, block, lines, thumb, talks, first, last }</summary>
    public sealed class NpcRecord
    {
        public string name;
        public int level, block;
        public List<string> lines;
        public string thumb;
        public int talks;
        public long first;
        public long? last;
    }

    /// <summary>NpcMemory.current: { name, until } while a conversation runs</summary>
    public sealed class NpcCurrent
    {
        public string name;
        public long until;
    }

    public sealed class NpcMemory
    {
        // export { npcName }
        public static string npcName(string file) => LandsOfLore.npcName(file);

        const string KEY = "lol.npcs";

        public Dictionary<string, NpcRecord> npcs;
        public NpcCurrent current;

        static long now() => (long)Web.now();

        static Dictionary<string, NpcRecord> read()
        {
            string raw = Store.storage.getItem(KEY);
            return (raw == null ? null : JsonSerializer.Deserialize<Dictionary<string, NpcRecord>>(raw, Store.json)) ?? new Dictionary<string, NpcRecord>();
        }

        public NpcMemory()
        {
            npcs = new Dictionary<string, NpcRecord>();
            try { npcs = read(); } catch (Exception) { /* ignore */ }
            current = null; // { name, until } while a conversation runs
        }

        public void save() { try { Store.storage.setItem(KEY, Store.stringify(npcs)); } catch (Exception) { /* ignore */ } }

        // A save was loaded: forget this campaign's people and read the ones that belong to it.
        public void reload()
        {
            npcs = new Dictionary<string, NpcRecord>();
            try { npcs = read(); } catch (Exception) { /* ignore */ }
            current = null;
        }

        // A dialogue script started: remember the NPC (portrait snapshot arrives a moment later).
        public NpcRecord begin(string file, int level, int block)
        {
            string name = npcName(file);
            if (Regex.IsMatch(name, "^(Escape|A voice)$")) return null;
            if (!npcs.TryGetValue(name, out var entry) || entry == null) npcs[name] = entry = new NpcRecord { name = name, level = level, block = block, lines = new List<string>(), thumb = "", talks = 0, first = now() };
            entry.level = level; entry.block = block; entry.talks += 1; entry.last = now();
            current = new NpcCurrent { name = name, until = now() + 60000 };
            save();
            return entry;
        }

        public string speaker() { return current != null && now() < current.until ? current.name : ""; }

        public void setThumb(string name, string dataUrl) { if (npcs.TryGetValue(name, out var e) && e != null && !string.IsNullOrEmpty(dataUrl)) { e.thumb = dataUrl; save(); } }

        public void addLine(string name, string text)
        {
            if (!npcs.TryGetValue(name, out var e) || e == null) return;
            if (e.lines.Count == 0 || e.lines[e.lines.Count - 1] != text) e.lines.Add(text);
            if (e.lines.Count > 40) e.lines.RemoveAt(0);
            save();
        }

        public List<NpcRecord> list() { return npcs.Values.OrderByDescending(e => e.last ?? 0).ToList(); }
    }
}
