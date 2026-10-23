using System;

namespace PreLuna.ThirdPersonHUD.Content
{
    [Flags]
    public enum HudContentChange { None = 0, Health = 1, Ammo = 2, Equipment = 4, Reload = 8, Items = 16 }
    public interface IHudContentRefreshSource { long ChangeVersion { get; } }
    // Optional bounded event storage is cleared with the owning content session, including work-version changes.
    public interface IHudContentTransientSource { void ClearTransient(); }
    public readonly record struct HudCollectionOptions(bool Enabled, double ErrorRetryDelaySeconds)
    {
        public static HudCollectionOptions Default => new(true, .25);
        public HudCollectionOptions Sanitized() => new(Enabled,
            double.IsFinite(ErrorRetryDelaySeconds) ? Math.Clamp(ErrorRetryDelaySeconds, .05, 10) : .25);
    }
    public readonly record struct HudCollectionStatus(bool Enabled, bool HasContent, bool RefreshPending,
        long Captured, long Failed, long Rejected, long LastSequence, long CapturedChangeVersion,
        int LastCaptureFrame, double NextCaptureAtSeconds, double RetryAtSeconds, HudContentPolicy Policy)
    { public long WorkEpoch { get; init; } public long ContentRevision { get; init; } }
}
