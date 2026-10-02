// What the browser gives the page that Unity does not: localStorage, timers (setTimeout /
// setInterval / requestAnimationFrame), Date.now, and file fetches. The ported page code (Web.*)
// calls these by their JS names. C# 9.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lol;
using UnityEngine;

namespace LolHost
{
    /// <summary>window.localStorage: string keys and values, kept in one JSON file (written atomically).</summary>
    public sealed class LocalStorage
    {
        readonly string _path;
        readonly Dictionary<string, string> _items = new Dictionary<string, string>();
        bool _dirty;

        public LocalStorage(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
                    foreach (var kv in o) if (kv.Value is JsonValue v && v.TryGetValue(out string s)) _items[kv.Key] = s;
            }
            catch (Exception e) { Debug.LogError($"localStorage: {path} is unreadable ({e.Message}); starting empty, the file is kept."); File.Copy(path, path + ".unreadable", true); }
        }

        public string getItem(string key) => _items.TryGetValue(key, out var v) ? v : null;
        public void setItem(string key, string value) { _items[key] = value ?? "null"; _dirty = true; }
        public void removeItem(string key) { if (_items.Remove(key)) _dirty = true; }
        public int length => _items.Count;
        public string key(int i) => i >= 0 && i < _items.Count ? _items.Keys.ElementAt(i) : null;
        public IEnumerable<string> keys => _items.Keys;

        System.Threading.Tasks.Task _writing = System.Threading.Tasks.Task.CompletedTask;
        readonly object _fileLock = new object();

        /// <summary>Writes the file if anything changed (the host calls it every few seconds and on quit). The
        /// periodic write runs off the main thread - building and writing a few megabytes of JSON (the saves) held a
        /// frame for 100 ms and more - from a copy taken here; quit waits for it and writes in place.</summary>
        public void Flush(bool now = false)
        {
            if (now) { try { _writing.Wait(); } catch (Exception) { /* reported where it ran */ } }
            if (!_dirty) return;
            if (!now && !_writing.IsCompleted) return;   // one write at a time: this one waits for the next call
            _dirty = false;
            var copy = new List<KeyValuePair<string, string>>(_items);
            if (now) { Write(copy); return; }
            _writing = System.Threading.Tasks.Task.Run(() =>
            {
                try { Write(copy); }
                catch (Exception e) { _dirty = true; Debug.LogWarning($"localStorage: could not write {_path} ({e.Message}); will try again."); }
            });
        }

        void Write(List<KeyValuePair<string, string>> items)
        {
            lock (_fileLock)
            {
                var o = new JsonObject();
                foreach (var kv in items) o[kv.Key] = kv.Value;
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, o.ToJsonString());
                if (File.Exists(_path)) File.Replace(tmp, _path, null); else File.Move(tmp, _path);
            }
        }
    }

    /// <summary>The browser's timers on the engine's event loop (Scheduler): the same ordering rules.</summary>
    public sealed class Timers
    {
        readonly Scheduler _sched;
        int _next = 1;
        readonly HashSet<int> _cancelled = new HashSet<int>();
        readonly List<(int id, Action fn)> _frames = new List<(int, Action)>();

        public Timers(Scheduler sched) { _sched = sched; }

        public int setTimeout(Action fn, double ms = 0)
        {
            int id = _next++;
            _ = Run(id, fn, ms, false);
            return id;
        }

        public int setInterval(Action fn, double ms)
        {
            int id = _next++;
            _ = Run(id, fn, ms, true);
            return id;
        }

        public void clearTimeout(int id) => _cancelled.Add(id);
        public void clearInterval(int id) => _cancelled.Add(id);

        async Task Run(int id, Action fn, double ms, bool repeat)
        {
            do
            {
                await _sched.Sleep(Math.Max(0, ms));
                if (_cancelled.Remove(id)) return;
                try { fn(); } catch (Exception e) { Debug.LogException(e); }
            } while (repeat && !_cancelled.Contains(id));
            _cancelled.Remove(id);
        }

        /// <summary>requestAnimationFrame: called on the next frame.</summary>
        public int requestAnimationFrame(Action fn) { int id = _next++; _frames.Add((id, fn)); return id; }
        public void cancelAnimationFrame(int id) => _frames.RemoveAll(f => f.id == id);

        public void RunFrame()
        {
            if (_frames.Count == 0) return;
            var due = _frames.ToList();
            _frames.Clear();
            foreach (var (_, fn) in due) _sched.Run(fn);
        }
    }
}
