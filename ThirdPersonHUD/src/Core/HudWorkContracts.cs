using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Core
{
    public enum HudWorkStage { Initialization, Collection, Computation, Application, Draw }

    public readonly record struct HudWorkOptions(double FrameBudgetMilliseconds, int MaxPreparationStepsPerFrame,
        double RefreshIntervalSeconds, double MaximumDataAgeSeconds)
    {
        public static HudWorkOptions Default => new(.5, 3, .05, .25);
        public HudWorkOptions Sanitized()
        {
            double age = double.IsFinite(MaximumDataAgeSeconds) ? Math.Clamp(MaximumDataAgeSeconds, .05, .5) : .25;
            return new(double.IsFinite(FrameBudgetMilliseconds) ? Math.Clamp(FrameBudgetMilliseconds, 0, 5) : .5,
                Math.Clamp(MaxPreparationStepsPerFrame, 1, 16),
                double.IsFinite(RefreshIntervalSeconds) ? Math.Clamp(RefreshIntervalSeconds, 0, age) : Math.Min(.05, age), age);
        }
    }

    // Neither sampling nor computation retains a camera, body pose, Unity object or delegate.
    // Only diagnostic parameters are sampled until the HUD's actual information content is chosen.
    public readonly record struct HudSample(HudWorkStamp Stamp, long WorkEpoch, long SettingsRevision,
        float Height, float Side, float SizePixels)
    { public HudPanelLayoutOptions Panel { get; init; } }
    public readonly record struct HudAnchorSpec(Vector3 WorldOffset, float CameraSide, float SizePixels)
    { public HudPanelLayoutOptions Panel { get; init; } }
    public readonly record struct HudPreparedData(HudResultStamp Stamp, long WorkEpoch,
        long SettingsRevision, HudAnchorSpec Anchor);

    public interface IHudDataSource { HudSample Capture(in HudSample request); }
    public interface IHudDataComputer { HudAnchorSpec Compute(in HudSample sample); }
    public sealed class PlaceholderDataSource : IHudDataSource
    { public HudSample Capture(in HudSample request) => request; }
    public sealed class PlaceholderDataComputer : IHudBackgroundDataComputer
    {
        public HudAnchorSpec Compute(in HudSample sample) => Compute(sample, default);
        public HudAnchorSpec Compute(in HudSample sample, in HudComputationCancellation cancellation)
        { cancellation.ThrowIfCancellationRequested(); return new(new Vector3(0, sample.Height, 0), sample.Side, sample.SizePixels) { Panel = sample.Panel }; }
    }

    public readonly record struct HudTiming(long Count, long Errors, long Deferrals, double LastMilliseconds,
        double MeanMilliseconds, double MaximumMilliseconds, double RecentP95Milliseconds);
    public readonly record struct HudExecutorSnapshot(int FrameNumber, double FrameSpentMilliseconds,
        double MaximumFrameMilliseconds, int PreparationStepsThisFrame, long BudgetOverrunFrames,
        long InvalidClockReads, HudTiming Initialization, HudTiming Collection,
        HudTiming Computation, HudTiming Application, HudTiming Draw);
    public readonly record struct HudPerformanceSnapshot(HudExecutorSnapshot Executor, int PendingInputs,
        int RetainedResults, int MaximumPendingInputs, long Captured, long Computed, long Applied,
        long ReusedApplications, long CancelledInputs, long CancelledResults, long ExpiredInputs,
        long ExpiredResults, long RejectedResults, long ReplacedResults, long LastAppliedSequence,
        long SettingsRevision)
    { public HudComputationSnapshot Computation { get; init; } }
}
