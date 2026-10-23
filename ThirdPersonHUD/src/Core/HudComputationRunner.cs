#nullable enable
using System;

namespace PreLuna.ThirdPersonHUD.Core
{
    public static class HudComputationRunner
    {
        public static bool ValidAnchor(in HudAnchorSpec anchor) => float.IsFinite(anchor.WorldOffset.X) && float.IsFinite(anchor.WorldOffset.Y)
            && float.IsFinite(anchor.WorldOffset.Z) && float.IsFinite(anchor.CameraSide) && float.IsFinite(anchor.SizePixels)
            && (anchor.Panel.Enabled ? anchor.Panel == anchor.Panel.Sanitized() : anchor.SizePixels >= 4 && anchor.SizePixels <= 48);
        public static HudComputationOutcome Execute(in HudComputationRequest request, IHudDataComputer computer,
            Func<double> clock, in HudComputationCancellation cancellation = default)
        {
            double started = double.NaN, ended = double.NaN;
            bool executed = false, cancelled = false, expired = false;
            string? error = null; HudAnchorSpec anchor = default;
            try
            {
                started = clock();
                if (!double.IsFinite(started) || started < request.Input.Stamp.CapturedAtSeconds
                    || !double.IsFinite(request.ExpiresAtSeconds) || request.ExpiresAtSeconds < request.Input.Stamp.CapturedAtSeconds)
                    throw new InvalidOperationException("Invalid computation clock or deadline.");
                cancellation.ThrowIfCancellationRequested();
                if (started > request.ExpiresAtSeconds) expired = true;
                else
                {
                    executed = true;
                    anchor = computer is IHudBackgroundDataComputer cooperative ? cooperative.Compute(request.Input, cancellation) : computer.Compute(request.Input);
                    cancellation.ThrowIfCancellationRequested();
                    if (!ValidAnchor(anchor))
                        throw new InvalidOperationException("Invalid computed HUD anchor.");
                }
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception failure)
            {
                error = failure.GetType().Name + ": " + failure.Message;
                if (error.Length > 1024) error = error[..1024];
            }
            try { ended = clock(); } catch { error ??= "Computation clock failed."; }
            if (!double.IsFinite(ended) || !double.IsFinite(started) || ended < started) error ??= "Invalid completion clock.";
            if (cancellation.IsCancellationRequested) { cancelled = true; error = null; }
            if (double.IsFinite(ended) && ended > request.ExpiresAtSeconds) expired = true;
            double elapsed = double.IsFinite(ended) && double.IsFinite(started) && ended >= started ? (ended - started) * 1000 : 0;
            return new(request.Input, anchor, ended, elapsed, executed, cancelled, expired, error);
        }
    }
}
