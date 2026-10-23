#nullable enable
using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Core
{
    // Main-thread content cache, sharing the existing executor. Critical data never waits for anchor computation.
    public sealed class HudContentPipeline
    {
        readonly HudMainThreadExecutor executor;
        readonly IHudContentSource? source;
        readonly IHudContentRefreshSource? refresh;
        readonly Action<string>? report;
        readonly Action captureAction;
        HudCollectionOptions options = HudCollectionOptions.Default;
        HudContentPolicy policy = HudContentPolicy.Default;
        HudRuntimeContext current;
        HudContentSnapshot content;
        ThirdPersonViewState identity;
        long workEpoch, revision, sequence, captured, failed, rejected, capturedVersion;
        int lastCaptureFrame = -1;
        double nextCapture = double.NaN, retryAt;
        double interval = .05;
        bool hasContent, hasIdentity;

        public HudContentPipeline(HudMainThreadExecutor executor, IHudContentSource? source, Action<string>? report = null)
        { this.executor = executor; this.source = source; refresh = source as IHudContentRefreshSource; this.report = report; captureAction = Capture; }

        public void Configure(in HudCollectionOptions configuration, in HudWorkOptions work)
        {
            if (!executor.OnMainThread) return;
            var next = configuration.Sanitized();
            var sanitized = work.Sanitized();
            var nextPolicy = HudContentPolicy.Default with { LiveAgeSeconds = sanitized.MaximumDataAgeSeconds };
            if (next.Enabled != options.Enabled || nextPolicy != policy) Cancel();
            options = next; policy = nextPolicy;
            // Interaction/action fields must have another sampling opportunity before their shorter lifetime ends.
            interval = Math.Min(sanitized.RefreshIntervalSeconds, Math.Min(policy.ActionAgeSeconds, Math.Min(policy.LiveAgeSeconds, policy.IdentityAgeSeconds)));
        }
        public void Cancel()
        {
            if (!executor.OnMainThread) return;
            hasContent = hasIdentity = false; content = default; identity = default;
            nextCapture = double.NaN; retryAt = 0;
            try { (source as IHudContentTransientSource)?.ClearTransient(); } catch { /* Cancellation must still finish. */ }
        }
        public void Prepare(in HudRuntimeContext context, long expectedWorkEpoch, long contentRevision, bool deferRoutine = false)
        {
            if (!executor.OnMainThread || source == null || !options.Enabled) return;
            current = context with { NowSeconds = executor.NowSeconds };
            if (!HudEligibility.EvaluateCurrent(current).Allowed) { Cancel(); return; }
            var view = current.CurrentView;
            bool changed = !hasIdentity || expectedWorkEpoch != workEpoch || contentRevision != revision
                || view.Context != identity.Context || view.Binding != identity.Binding || view.Subject != identity.Subject
                || view.EligibilityEpoch != identity.EligibilityEpoch || view.ConfigurationRevision != identity.ConfigurationRevision;
            if (changed) Cancel();
            identity = view; workEpoch = expectedWorkEpoch; revision = contentRevision; hasIdentity = true;
            if (current.NowSeconds < retryAt || lastCaptureFrame == current.FrameNumber) return;
            bool dirty = refresh != null && refresh.ChangeVersion != capturedVersion;
            // With a one-step limit, let initial anchor work progress while a recent content sample is still valid.
            // Dirty events and stale/missing content retain priority.
            if (deferRoutine && !changed && !dirty && hasContent
                && current.NowSeconds - content.Header.Origin.Input.CapturedAtSeconds < policy.LiveAgeSeconds * .5) return;
            if (changed || dirty || !double.IsFinite(nextCapture) || current.NowSeconds + 1e-9 >= nextCapture)
                executor.TryExecute(current.FrameNumber, HudWorkStage.Collection, captureAction);
        }
        void Capture()
        {
            current = current with { NowSeconds = executor.NowSeconds };
            lastCaptureFrame = current.FrameNumber;
            long signal = refresh?.ChangeVersion ?? 0;
            var request = new HudContentCaptureRequest(new(current.CurrentView, checked(++sequence), current.FrameNumber, current.NowSeconds), workEpoch, revision);
            // Invalidate before calling game code: a failed or reentrant read must not expose the old cache.
            hasContent = false; content = default;
            try
            {
                if (!source!.TryCapture(request, out var snapshot)) { Failure("个人数据暂不可用。"); return; }
                var fresh = current with { NowSeconds = executor.NowSeconds };
                if (snapshot.Header.Origin.Input != request.Origin || snapshot.Header.WorkEpoch != workEpoch
                    || snapshot.Header.ContentRevision != revision
                    || !HudContentRules.EvaluateData(fresh, snapshot.Header, workEpoch, revision, policy).Allowed)
                { rejected++; Failure("个人数据来源或有效期不匹配。"); return; }
                content = snapshot; hasContent = true; captured++; capturedVersion = signal;
                nextCapture = fresh.NowSeconds + interval; retryAt = 0;
            }
            catch (Exception error) { Failure(error.GetType().Name + ": " + error.Message); }
        }
        void Failure(string message)
        {
            failed++; hasContent = false; content = default;
            retryAt = executor.NowSeconds + options.ErrorRetryDelaySeconds;
            nextCapture = double.NaN;
            try { report?.Invoke(message); } catch { }
        }
        public bool TryGet(in HudRuntimeContext context, long expectedWorkEpoch, out HudContentSnapshot snapshot)
        {
            snapshot = default;
            if (!executor.OnMainThread || !options.Enabled || !hasContent || expectedWorkEpoch != workEpoch
                || refresh != null && refresh.ChangeVersion != capturedVersion) return false;
            var fresh = context with { NowSeconds = executor.NowSeconds };
            if (!HudContentRules.EvaluateData(fresh, content.Header, workEpoch, revision, policy).Allowed) return false;
            snapshot = content; return true;
        }
        public bool TryGetStatus(out HudCollectionStatus status)
        {
            status = default; if (!executor.OnMainThread) return false;
            status = new(options.Enabled && source != null, hasContent, refresh != null && refresh.ChangeVersion != capturedVersion,
                captured, failed, rejected, sequence, capturedVersion, lastCaptureFrame, nextCapture, retryAt, policy){WorkEpoch=workEpoch,ContentRevision=revision};
            return true;
        }
    }
}
