using System;
using System.Diagnostics;

namespace PreLuna.ThirdPersonHUD.Core
{
    // Synchronous cooperative executor. A granted atomic operation cannot be preempted.
    public sealed class HudMainThreadExecutor
    {
        sealed class Timing
        {
            readonly double[] recent = new double[128];
            long count, errors, deferred;
            double total, last, maximum;
            public void Defer() => deferred++;
            public void Record(double milliseconds, bool failed)
            { last = milliseconds; maximum = Math.Max(maximum, last); total += last; recent[count % recent.Length] = last; count++; if (failed) errors++; }
            public HudTiming Snapshot()
            {
                int length = (int)Math.Min(count, recent.Length);
                Span<double> sorted = stackalloc double[128]; recent.AsSpan(0, length).CopyTo(sorted); sorted = sorted[..length]; sorted.Sort();
                return new(count, errors, deferred, last, count == 0 ? 0 : total / count, maximum,
                    length == 0 ? 0 : sorted[(int)Math.Ceiling(length * .95) - 1]);
            }
        }
        readonly int thread = Environment.CurrentManagedThreadId;
        readonly Func<double> clock;
        readonly Timing[] timings = { new(), new(), new(), new(), new() };
        HudWorkOptions options = HudWorkOptions.Default;
        int frame = -1, preparationSteps;
        double spent, maximumFrame;
        long overruns, invalidClocks;
        bool overrun, executing;
        public bool OnMainThread => Environment.CurrentManagedThreadId == thread;
        public HudMainThreadExecutor(Func<double> clock = null)
        { this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency); }
        public double NowSeconds => OnMainThread ? clock() : double.NaN;
        internal Func<double> ComputationClock => clock;
        public bool BeginFrame(int frameNumber, in HudWorkOptions configuration)
        {
            if (!OnMainThread || executing || frameNumber < 0 || frameNumber < frame) return false;
            options = configuration.Sanitized();
            if (frameNumber != frame) { frame = frameNumber; spent = 0; preparationSteps = 0; overrun = false; }
            return true;
        }
        public bool TryExecute(int frameNumber, HudWorkStage stage, Action operation)
        {
            if (!OnMainThread || executing || (int)stage < 0 || (int)stage >= timings.Length) return false;
            var timing = timings[(int)stage];
            bool preparation = stage is HudWorkStage.Initialization or HudWorkStage.Collection or HudWorkStage.Computation;
            if (frameNumber != frame || frame < 0 || spent >= options.FrameBudgetMilliseconds
                || preparation && preparationSteps >= options.MaxPreparationStepsPerFrame)
            { timing.Defer(); return false; }
            double started = clock();
            if (!double.IsFinite(started) || started < 0) { invalidClocks++; timing.Defer(); return false; }
            if (preparation) preparationSteps++;
            bool failed = true; executing = true;
            try { operation(); failed = false; return true; }
            finally
            {
                double ended = clock(), milliseconds = 0;
                if (!double.IsFinite(ended) || ended < started)
                { invalidClocks++; spent = Math.Max(spent, options.FrameBudgetMilliseconds); }
                else milliseconds = (ended - started) * 1000;
                spent += milliseconds; maximumFrame = Math.Max(maximumFrame, spent);
                if (!overrun && spent > options.FrameBudgetMilliseconds) { overrun = true; overruns++; }
                timing.Record(milliseconds, failed); executing = false;
            }
        }
        public bool TryGetSnapshot(out HudExecutorSnapshot snapshot)
        {
            snapshot = default; if (!OnMainThread) return false;
            snapshot = new(frame, spent, maximumFrame, preparationSteps, overruns, invalidClocks,
                timings[0].Snapshot(), timings[1].Snapshot(), timings[2].Snapshot(), timings[3].Snapshot(), timings[4].Snapshot());
            return true;
        }
    }
}
