// --perf: where a frame's time goes (Update's parts, and the frame as a whole, render included), logged every
// 5 seconds as average milliseconds per frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LolHost
{
    public static class Perf
    {
        static readonly bool On = Environment.GetCommandLineArgs().Contains("--perf");
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly Dictionary<string, double> Sum = new Dictionary<string, double>();
        static readonly Dictionary<string, double> Max = new Dictionary<string, double>();   // the worst single frame
        static double _mark, _frameStart = -1, _logAt = 5000;
        static int _frames, _slow;
        static readonly int[] Hist = new int[10];
        static readonly Dictionary<string, double> ThisFrame = new Dictionary<string, double>();
        static readonly Dictionary<string, int> Blame = new Dictionary<string, int>();   // the parts over 3 ms in frames over 15 ms   // frames by 5 ms: 0-5, 5-10, ... 45+

        public static void Begin()
        {
            if (!On) return;
            double now = Clock.Elapsed.TotalMilliseconds;
            if (_frameStart >= 0) { Add("frame", now - _frameStart); _frames += 1; double ft = now - _frameStart; if (ft > 17.5) _slow += 1;
                if (ft > 15) { double known = 0; foreach (var kv in ThisFrame) { known += kv.Value; if (kv.Value > 3) Blame[kv.Key] = (Blame.TryGetValue(kv.Key, out var b) ? b : 0) + 1; } if (ft - known > 3) Blame["(render/other)"] = (Blame.TryGetValue("(render/other)", out var o) ? o : 0) + 1; }
                ThisFrame.Clear(); Hist[Math.Min(9, (int)(ft / 5))] += 1; }
            _frameStart = _mark = now;
        }

        public static void Mark(string part)
        {
            if (!On) return;
            double now = Clock.Elapsed.TotalMilliseconds;
            Add(part, now - _mark);
            _mark = now;
        }

        public static void End()
        {
            if (!On || _frameStart < _logAt || _frames == 0) return;
            _logAt = _frameStart + 5000;
            UnityEngine.Debug.Log("perf ms/frame avg/max: " + string.Join(" ", Sum.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value / _frames:0.0}/{(Max.TryGetValue(kv.Key, out var m) ? m : 0):0.0}")) + $" ({_frames} frames, {_slow} over 17.5 ms; by 5 ms: {string.Join(",", Hist)})");
            Array.Clear(Hist, 0, Hist.Length);
            UnityEngine.Debug.Log("perf slow frames blame: " + string.Join(" ", Blame.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
            Blame.Clear();
            Sum.Clear(); Max.Clear();
            _frames = 0; _slow = 0;
        }

        /// <summary>A part timed on its own (it runs inside another): total ms and calls per frame.</summary>
        public static double Now => On ? Clock.Elapsed.TotalMilliseconds : 0;
        public static void Took(string part, double since)
        {
            if (!On) return;
            Add(part + ".ms", Clock.Elapsed.TotalMilliseconds - since);
            Add(part + ".calls", 1);
        }

        static void Add(string part, double ms)
        {
            if (part != "frame" && !part.EndsWith(".calls")) ThisFrame[part] = (ThisFrame.TryGetValue(part, out var tf) ? tf : 0) + ms;
            Sum[part] = (Sum.TryGetValue(part, out var v) ? v : 0) + ms;
            if (!Max.TryGetValue(part, out var m) || ms > m) Max[part] = ms;
        }
    }
}
