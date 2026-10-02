// src/main.mjs, the page: this partial class is the module. Its sections live in Web.*.cs files
// (ported 1:1, see docs/port/HOST.md); this file holds what the browser provided (timers, storage,
// fetch, Date.now) and the order the module's top-level code runs in.
// C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        // ---- the browser ----
        public readonly Scheduler sched;
        public readonly Timers timers;
        public readonly LocalStorage localStorage;
        public readonly WebAudio audio;
        /// <summary>The game's DATA folder (the browser's /private/game/DATA/).</summary>
        public string dataFolder;
        /// <summary>The HD assets folder (the browser's /private/hd/), null when there is none.</summary>
        public string hdFolder;
        public readonly double startedAt = Time.realtimeSinceStartupAsDouble * 1000;

        /// <summary>Date.now(): milliseconds (wall clock, for saves and "time ago").</summary>
        public static double now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public Web(Scheduler sched, WebAudio audio, string dataFolder, string storageFile)
        {
            this.sched = sched;
            this.audio = audio;
            this.dataFolder = dataFolder;
            timers = new Timers(sched);
            localStorage = new LocalStorage(storageFile);
            Store.storage = localStorage;
        }

        /// <summary>fetch(path) + arrayBuffer(): a file of the game (path relative to the data root, or absolute under it).</summary>
        public Task<byte[]> fetchBytes(string path)
        {
            string rel = path.StartsWith(dataRoot ?? "\u0000") ? path.Substring(dataRoot.Length) : path;
            string full = Path.Combine(dataFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return Task.FromException<byte[]>(new FileNotFoundException($"{path}: HTTP 404"));
            return Task.FromResult(File.ReadAllBytes(full));
        }

        public static VisualElement Q(string selector) => Dom.Q(selector);
        public static List<VisualElement> QAll(string selector) => Dom.QAll(selector);

        /// <summary>The page's module code, run in file order once the DOM exists (import time in the browser).</summary>
        public void Init()
        {
            foreach (var m in typeof(Web).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                         .Where(m => m.Name.StartsWith("init_") && m.GetParameters().Length == 0)
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
                m.Invoke(this, null);
        }

        /// <summary>Once a frame: the page's requestAnimationFrame callbacks.</summary>
        public void Frame() { timers.RunFrame(); Dom.Invoke(flushPresent); Dom.Invoke(hudFrame); }
    }

    /// <summary>Reads a field of a JS-shaped payload the engine emits (anonymous object or dictionary).</summary>
    public static class JsObj
    {
        public static object Get(object o, string name)
        {
            if (o == null) return null;
            if (o is IDictionary<string, object> d) return d.TryGetValue(name, out var v) ? v : null;
            var t = o.GetType();
            return t.GetProperty(name)?.GetValue(o) ?? t.GetField(name)?.GetValue(o);
        }

        public static int Int(object o, string name, int fallback = 0)
        {
            var v = Get(o, name);
            return v == null ? fallback : Convert.ToInt32(v);
        }

        public static bool Has(object o, string name) => Get(o, name) != null;
    }

    /// <summary>A JS Set: unique values, iterated in insertion order.</summary>
    public sealed class JsSet<T> : IEnumerable<T>
    {
        readonly List<T> _items = new List<T>();
        public int Count => _items.Count;
        public bool Contains(T v) => _items.Contains(v);
        public void Add(T v) { if (!_items.Contains(v)) _items.Add(v); }
        public bool Remove(T v) => _items.Remove(v);
        public void Clear() => _items.Clear();
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
