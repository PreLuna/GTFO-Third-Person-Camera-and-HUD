#nullable enable
using System;
using System.Threading;

namespace PreLuna.ThirdPersonHUD.Core
{
    // One persistent background worker; lock-free bounded mailboxes for the main thread.
    public sealed class HudBackgroundExecutor : IHudBackgroundExecutor
    {
        sealed class InputBox
        { internal readonly HudComputationRequest Value; internal readonly long Generation; internal InputBox(in HudComputationRequest value, long generation) { Value = value; Generation = generation; } }
        sealed class OutputBox
        { internal readonly HudComputationOutcome Value; internal readonly long Generation; internal OutputBox(in HudComputationOutcome value, long generation) { Value = value; Generation = generation; } }
        readonly int mainThread = Environment.CurrentManagedThreadId;
        readonly IHudBackgroundDataComputer computer;
        readonly Func<double> clock;
        readonly HudCancellationState cancellation = new();
        InputBox? pending;
        OutputBox? completed;
        AutoResetEvent? wake;
        Thread? worker;
        int running, exited;
        long starts, submitted, executed, finished, cancelled, errors, expired, replacedInputs, replacedCompletions, discarded;
        long elapsedTicks, maximumTicks;
        bool OnMainThread => Environment.CurrentManagedThreadId == mainThread;
        public HudBackgroundExecutor(IHudBackgroundDataComputer computer, Func<double> clock)
        { this.computer = computer; this.clock = clock; }
        void Signal() { try { Volatile.Read(ref wake)?.Set(); } catch (ObjectDisposedException) { } }
        public bool TrySubmit(in HudComputationRequest request)
        {
            if (!OnMainThread || Volatile.Read(ref cancellation.Stopped) != 0 || Volatile.Read(ref exited) != 0) return false;
            if (worker == null)
            {
                try
                {
                    wake = new AutoResetEvent(false);
                    var next = new Thread(Run) { IsBackground = true, Name = "PreLuna.ThirdPersonHUD.Compute" };
                    next.Start(); worker = next; Interlocked.Increment(ref starts);
                }
                catch { wake?.Dispose(); wake = null; return false; }
            }
            var box = new InputBox(request, Interlocked.Read(ref cancellation.Generation));
            if (Interlocked.Exchange(ref pending, box) != null) Interlocked.Increment(ref replacedInputs);
            Interlocked.Increment(ref submitted); Signal(); return true;
        }
        public bool TryTake(out HudComputationOutcome outcome)
        {
            outcome = default; if (!OnMainThread) return false;
            var box = Interlocked.Exchange(ref completed, null);
            if (box == null) return false;
            if (box.Generation != Interlocked.Read(ref cancellation.Generation) || Volatile.Read(ref cancellation.Stopped) != 0)
            { Interlocked.Increment(ref discarded); return false; }
            outcome = box.Value; return true;
        }
        public void Cancel()
        {
            if (!OnMainThread) return;
            Interlocked.Increment(ref cancellation.Generation);
            if (Interlocked.Exchange(ref pending, null) != null) Interlocked.Increment(ref cancelled);
            if (Interlocked.Exchange(ref completed, null) != null) Interlocked.Increment(ref discarded);
            // Running code observes the generation; an idle worker need not wake for cancellation.
        }
        void RecordTime(double milliseconds)
        {
            long ticks = (long)(Math.Clamp(milliseconds, 0, 86400000) * 10000);
            Interlocked.Add(ref elapsedTicks, ticks);
            long prior;
            do { prior = Interlocked.Read(ref maximumTicks); if (ticks <= prior) break; }
            while (Interlocked.CompareExchange(ref maximumTicks, ticks, prior) != prior);
        }
        void Run()
        {
            try
            {
                while (Volatile.Read(ref cancellation.Stopped) == 0)
                {
                    var box = Interlocked.Exchange(ref pending, null);
                    if (box == null) { wake!.WaitOne(); continue; }
                    var token = new HudComputationCancellation(cancellation, box.Generation);
                    if (token.IsCancellationRequested) { Interlocked.Increment(ref cancelled); continue; }
                    Volatile.Write(ref running, 1);
                    var outcome = HudComputationRunner.Execute(box.Value, computer, clock, token);
                    Volatile.Write(ref running, 0);
                    if (outcome.Executed) Interlocked.Increment(ref executed);
                    RecordTime(outcome.ElapsedMilliseconds);
                    if (outcome.Cancelled || token.IsCancellationRequested) { Interlocked.Increment(ref cancelled); continue; }
                    if (outcome.Error != null) Interlocked.Increment(ref errors);
                    if (outcome.Expired) Interlocked.Increment(ref expired);
                    Interlocked.Increment(ref finished);
                    if (Interlocked.Exchange(ref completed, new OutputBox(outcome, box.Generation)) != null)
                        Interlocked.Increment(ref replacedCompletions);
                }
            }
            catch { Interlocked.Increment(ref errors); Volatile.Write(ref cancellation.Stopped, 1); }
            finally
            {
                Volatile.Write(ref running, 0); Interlocked.Exchange(ref pending, null); Interlocked.Exchange(ref completed, null);
                Volatile.Write(ref exited, 1); wake?.Dispose();
            }
        }
        public bool TryGetSnapshot(out HudWorkerSnapshot snapshot)
        {
            snapshot = default; if (!OnMainThread) return false;
            snapshot = new(worker != null, Volatile.Read(ref cancellation.Stopped) != 0, Volatile.Read(ref exited) != 0,
                Volatile.Read(ref running), Volatile.Read(ref pending) == null ? 0 : 1, Volatile.Read(ref completed) == null ? 0 : 1,
                Interlocked.Read(ref starts), Interlocked.Read(ref submitted), Interlocked.Read(ref executed), Interlocked.Read(ref finished),
                Interlocked.Read(ref cancelled), Interlocked.Read(ref errors), Interlocked.Read(ref expired), Interlocked.Read(ref replacedInputs),
                Interlocked.Read(ref replacedCompletions), Interlocked.Read(ref discarded), Interlocked.Read(ref elapsedTicks) / 10000d, Interlocked.Read(ref maximumTicks) / 10000d);
            return true;
        }
        public void Dispose()
        {
            if (!OnMainThread || Interlocked.Exchange(ref cancellation.Stopped, 1) != 0) return;
            Cancel(); if (worker == null) Volatile.Write(ref exited, 1); else Signal();
        }
    }
}
