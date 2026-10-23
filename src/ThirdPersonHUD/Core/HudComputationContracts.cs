#nullable enable
using System;
using System.Threading;

namespace PreLuna.ThirdPersonHUD.Core
{
    public enum HudComputationMode { MainThread, Background }
    public readonly record struct HudExecutionOptions(HudComputationMode Mode, double ErrorRetryDelaySeconds)
    {
        // Existing API callers remain on the main thread; the plugin explicitly opts into Background.
        public static HudExecutionOptions Default => new(HudComputationMode.MainThread, .5);
        public HudExecutionOptions Sanitized() => new(Mode is HudComputationMode.MainThread or HudComputationMode.Background ? Mode : HudComputationMode.MainThread,
            double.IsFinite(ErrorRetryDelaySeconds) ? Math.Clamp(ErrorRetryDelaySeconds, .05, 10) : .5);
    }

    internal sealed class HudCancellationState { internal long Generation = 1; internal int Stopped; }
    public readonly struct HudComputationCancellation
    {
        readonly HudCancellationState? state;
        readonly long generation;
        internal HudComputationCancellation(HudCancellationState state, long generation) { this.state = state; this.generation = generation; }
        public bool IsCancellationRequested => state != null && (Volatile.Read(ref state.Stopped) != 0 || Interlocked.Read(ref state.Generation) != generation);
        public void ThrowIfCancellationRequested() { if (IsCancellationRequested) throw new OperationCanceledException(); }
    }

    // Explicit opt-in: implementers must be pure, reentrant and thread safe, including during a mode switch.
    // Do not access Unity/IL2CPP objects, providers, logging or mutable main-thread state here.
    public interface IHudBackgroundDataComputer : IHudDataComputer
    { HudAnchorSpec Compute(in HudSample sample, in HudComputationCancellation cancellation); }

    public readonly record struct HudComputationRequest(HudSample Input, double ExpiresAtSeconds);
    public readonly record struct HudComputationOutcome(HudSample Input, HudAnchorSpec Anchor, double CompletedAtSeconds,
        double ElapsedMilliseconds, bool Executed, bool Cancelled, bool Expired, string? Error);
    public readonly record struct HudWorkerSnapshot(bool Started, bool StopRequested, bool Exited, int Running, int Pending,
        int CompletedMailbox, long ThreadStarts, long Submitted, long Executed, long Completed, long Cancelled,
        long Errors, long Expired, long ReplacedInputs, long ReplacedCompletions, long DiscardedGenerations,
        double TotalCalculationMilliseconds, double MaximumCalculationMilliseconds);
    public readonly record struct HudComputationSnapshot(HudComputationMode RequestedMode, HudComputationMode EffectiveMode,
        bool SupportsBackground, long ModeChanges, long AcceptedResults, long Errors, long CancelledCompletions,
        long ExpiredCompletions, double RetryAtSeconds, HudWorkerSnapshot Worker);

    // Main-thread mailbox API. No method waits for calculation or thread termination.
    public interface IHudBackgroundExecutor : IDisposable
    {
        bool TrySubmit(in HudComputationRequest request);
        bool TryTake(out HudComputationOutcome outcome);
        void Cancel();
        bool TryGetSnapshot(out HudWorkerSnapshot snapshot);
    }
}
