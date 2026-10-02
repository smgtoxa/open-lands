// The browser's event loop, for an engine transliterated from JavaScript async code.
//
// JS: `await promise` continues in a microtask; `setTimeout(fn, ms)` runs fn as a macrotask once the
// clock has passed; microtasks always drain before the next macrotask. Here:
//   - awaits capture this SynchronizationContext, so their continuations are Post()ed to a queue
//     that Pump() drains (the microtask queue);
//   - Sleep(ms) is setTimeout: a sleeper woken when the host's clock (Now) reaches its time;
//   - Pump(now) moves the clock and runs everything that is due, in JS order.
// Nothing runs on another thread, and a run is reproducible from the clock the host feeds it.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lol
{
    public sealed class Scheduler : SynchronizationContext
    {
        readonly Queue<(SendOrPostCallback Callback, object State)> _posted = new Queue<(SendOrPostCallback, object)>();
        readonly List<(double At, long Seq, TaskCompletionSource<bool> Done)> _sleepers = new List<(double, long, TaskCompletionSource<bool>)>();
        long _seq;
        int _depth;

        /// <summary>The page's clock in milliseconds (performance.now / Date.now - start).</summary>
        public double Now { get; private set; }

        /// <summary>Uncaught errors of fire-and-forget tasks (JS: unhandled rejections).</summary>
        public Action<Exception> OnError = e => { };

        public override void Post(SendOrPostCallback d, object state) => _posted.Enqueue((d, state));
        public override void Send(SendOrPostCallback d, object state) => d(state);
        public override SynchronizationContext CreateCopy() => this;

        /// <summary>setTimeout(resolve, ms): completes once the clock has moved ms on (0 = next turn).</summary>
        /// <summary>Diagnostics: every setTimeout, with its length.</summary>
        public Action<double> traceSleep;

        public Task Sleep(double ms)
        {
            traceSleep?.Invoke(ms);
            var done = new TaskCompletionSource<bool>();
            _sleepers.Add((Now + Math.Max(0, ms), _seq++, done));
            return done.Task;
        }

        /// <summary>Runs host code inside the loop, so the engine calls it makes await on this loop.</summary>
        public void Run(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            _depth += 1;
            try { action(); DrainPosted(); }
            finally { _depth -= 1; SetSynchronizationContext(previous); }
        }

        public T Run<T>(Func<T> func)
        {
            T result = default;
            Run(() => { result = func(); });
            return result;
        }

        /// <summary>Starts an async call without waiting for it (JS: calling an async function without await).</summary>
        public void Start(Func<Task> call) => Run(() => Observe(call()));

        public void Observe(Task task)
        {
            if (task.IsCompleted) { if (task.IsFaulted) OnError(task.Exception.GetBaseException()); return; }
            task.ContinueWith(t => { if (t.IsFaulted) OnError(t.Exception.GetBaseException()); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>The page's clock moved on: every timer that is due runs, each followed by its microtasks.</summary>
        public void Pump(double now)
        {
            if (now > Now) Now = now;
            // A sleeper set up during this pump waits for the next one, however short its sleep: one
            // step of delay() is one frame, never zero (JS setTimeout(0) also yields a turn).
            long before = _seq;
            Run(() =>
            {
                for (int guard = 0; guard < 100000; guard += 1)
                {
                    int next = -1;
                    for (int i = 0; i < _sleepers.Count; i += 1)
                    {
                        var s = _sleepers[i];
                        if (s.At > Now || s.Seq >= before) continue;
                        if (next < 0 || s.At < _sleepers[next].At || (s.At == _sleepers[next].At && s.Seq < _sleepers[next].Seq)) next = i;
                    }
                    if (next < 0) return;
                    var due = _sleepers[next];
                    _sleepers.RemoveAt(next);
                    due.Done.SetResult(true);
                    DrainPosted();
                }
            });
        }

        void DrainPosted()
        {
            for (int guard = 0; _posted.Count > 0 && guard < 10000000; guard += 1)
            {
                var (callback, state) = _posted.Dequeue();
                callback(state);
            }
        }

        /// <summary>When the next timer is due, or +inf with none waiting.</summary>
        public double NextDue
        {
            get
            {
                double at = double.PositiveInfinity;
                foreach (var s in _sleepers) if (s.At < at) at = s.At;
                return at;
            }
        }
    }
}
