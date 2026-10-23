using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Core
{
    // Single input slot + single retained result, not a queue. Computation gets priority over another sample.
    // All mutable state is main-thread-owned; the computer receives only an immutable value sample.
    public sealed class HudDataPipeline : IDisposable
    {
        readonly HudMainThreadExecutor executor;
        readonly IHudDataSource source;
        readonly IHudDataComputer computer;
        readonly IHudPresentation presentation;
        readonly Action collectAction, computeAction, applyAction;
        readonly HudContentPipeline content;
        readonly HudPanelMotion motion = new();
        readonly Action<string> computeError;
        IHudBackgroundExecutor background;
        HudExecutionOptions execution = HudExecutionOptions.Default;
        long modeChanges, accepted, computationErrors, cancelledCompletions, expiredCompletions, minimumSequence = 1, highestReceived;
        double retryAt;
        bool disposed;
        HudWorkOptions workOptions = HudWorkOptions.Default;
        HudLifecycleOptions geometry = HudLifecycleOptions.Default;
        HudRuntimeContext current;
        ThirdPersonDisplayFrame display;
        HudSample input;
        HudPreparedData result, appliedResult;
        HudPlacement placement;
        bool hasInput, hasResult, hasApplied, placed;
        double nextCapture = double.NaN;
        long epoch, sequence, revision = 1, captured, computed, applied, reused, cancelledInputs, cancelledResults,
            expiredInputs, expiredResults, rejected, replaced, lastApplied;
        int maximumPendingInputs;
        int lastCaptureFrame = -1;
        ThirdPersonViewState workView;
        long identityEpoch;
        bool hasWorkIdentity;
        public HudDataPipeline(HudMainThreadExecutor executor, IHudPresentation presentation,
            IHudDataSource source = null, IHudDataComputer computer = null, IHudBackgroundExecutor background = null, Action<string> computeError = null,
            IHudContentSource contentSource = null, Action<string> collectionError = null)
        {
            this.executor = executor; this.presentation = presentation;
            this.source = source ?? new PlaceholderDataSource(); this.computer = computer ?? new PlaceholderDataComputer();
            this.background = background; this.computeError = computeError;
            content = new HudContentPipeline(executor, contentSource, collectionError);
            collectAction = Collect; computeAction = Compute; applyAction = Apply;
        }
        public void Configure(in HudWorkOptions work, in HudLifecycleOptions options)
            => Configure(work, options, HudExecutionOptions.Default);
        public void Configure(in HudWorkOptions work, in HudLifecycleOptions options, in HudExecutionOptions computation)
            => Configure(work, options, computation, HudCollectionOptions.Default);
        public void Configure(in HudWorkOptions work, in HudLifecycleOptions options, in HudExecutionOptions computation, in HudCollectionOptions collection)
        {
            if (!executor.OnMainThread || disposed) return;
            workOptions = work.Sanitized();
            content.Configure(collection, workOptions);
            var next = computation.Sanitized();
            if (next.Mode != execution.Mode) { CancelComputation(); modeChanges++; retryAt = 0; }
            execution = next;
            if (EffectiveMode == HudComputationMode.Background)
                background ??= new HudBackgroundExecutor((IHudBackgroundDataComputer)computer, executor.ComputationClock);
            if (geometry.AnchorHeight != options.AnchorHeight || geometry.AnchorSide != options.AnchorSide
                || geometry.MarkerSizePixels != options.MarkerSizePixels || geometry.Panel != options.Panel)
            {
                revision = checked(revision + 1); CancelComputation();
                if(hasResult)cancelledResults++;
                hasResult=hasApplied=false;result=appliedResult=default;placed=false;placement=default;
            }
            geometry = options;
        }
        public void Cancel()
        {
            if (!executor.OnMainThread) return;
            CancelComputation();
            content.Cancel();
            motion.Reset();
            if (hasResult) cancelledResults++;
            hasInput = hasResult = hasApplied = false; input = default; result = appliedResult = default;
            current = default; display = default; placement = default; placed = false; nextCapture = double.NaN;
            workView = default; hasWorkIdentity = false;
        }
        void CancelComputation()
        {
            if (hasInput) cancelledInputs++;
            hasInput = false; input = default;
            minimumSequence = checked(sequence + 1); background?.Cancel(); nextCapture = double.NaN;
        }
        HudComputationMode EffectiveMode => execution.Mode == HudComputationMode.Background && computer is IHudBackgroundDataComputer
            ? HudComputationMode.Background : HudComputationMode.MainThread;
        HudWorkerSnapshot WorkerSnapshot
        { get { if (background != null && background.TryGetSnapshot(out var snapshot)) return snapshot; return default; } }
        HudValidityPolicy Policy => new(HudValidityPolicy.Default.MaximumDisplayAgeSeconds, workOptions.MaximumDataAgeSeconds);
        HudRuntimeContext Fresh(in HudRuntimeContext context) => context with { NowSeconds = executor.NowSeconds };
        public void Prepare(in HudRuntimeContext context, long workEpoch)
        {
            if (!executor.OnMainThread || disposed) return;
            current = Fresh(context); epoch = workEpoch;
            if (!HudEligibility.EvaluateCurrent(current).Allowed) { Cancel(); return; }
            var view = current.CurrentView;
            if (hasWorkIdentity && (identityEpoch != workEpoch || workView.Context != view.Context || workView.Binding != view.Binding
                || workView.Subject != view.Subject || workView.EligibilityEpoch != view.EligibilityEpoch
                || workView.ConfigurationRevision != view.ConfigurationRevision)) CancelComputation();
            workView = view; identityEpoch = workEpoch; hasWorkIdentity = true;
            // Layout revisions never renew or invalidate personal content; content semantics are unchanged in batch 3.
            content.Prepare(current, workEpoch, 1, workOptions.MaxPreparationStepsPerFrame == 1 && (hasInput || !hasResult));
            if (hasResult && !ValidResult(current, result, workEpoch)) DiscardResult(current);
            if (hasInput && !ValidInput(current, input, workEpoch))
            {
                if (HudEligibility.EvaluateData(current, new(input.Stamp, input.Stamp.CapturedAtSeconds), Policy).Reason == HudDenyReason.ResultExpired) expiredInputs++;
                else cancelledInputs++;
                hasInput = false; input = default; nextCapture = double.NaN;
            }
            var worker = WorkerSnapshot;
            bool attemptedCompute = hasInput || EffectiveMode == HudComputationMode.Background && worker.CompletedMailbox != 0;
            if (attemptedCompute) executor.TryExecute(current.FrameNumber, HudWorkStage.Computation, computeAction);
            current = Fresh(current);
            if (current.NowSeconds < retryAt) return;
            bool queueFull = EffectiveMode == HudComputationMode.Background && WorkerSnapshot.Pending != 0;
            if (!hasInput && !queueFull && lastCaptureFrame != current.FrameNumber && (!double.IsFinite(nextCapture) || current.NowSeconds + 1e-9 >= nextCapture))
                executor.TryExecute(current.FrameNumber, HudWorkStage.Collection, collectAction);
            if (!attemptedCompute && hasInput) executor.TryExecute(current.FrameNumber, HudWorkStage.Computation, computeAction);
        }
        bool ValidInput(in HudRuntimeContext context, in HudSample sample, long workEpoch) => sample.WorkEpoch == workEpoch
            && sample.SettingsRevision == revision && HudEligibility.EvaluateData(context,
                new(sample.Stamp, sample.Stamp.CapturedAtSeconds), Policy).Allowed;
        bool ValidResult(in HudRuntimeContext context, in HudPreparedData value, long workEpoch) => value.WorkEpoch == workEpoch
            && value.SettingsRevision == revision && HudEligibility.EvaluateData(context, value.Stamp, Policy).Allowed;
        void Collect()
        {
            current = Fresh(current);
            var request = new HudSample(new(current.CurrentView, checked(++sequence), current.FrameNumber, current.NowSeconds),
                epoch, revision, geometry.AnchorHeight, geometry.AnchorSide, geometry.MarkerSizePixels) { Panel = geometry.Panel };
            var sampled = source.Capture(request);
            if (sampled != request) throw new InvalidOperationException("HUD 采集端返回了不同身份或参数的样本。");
            input = sampled; hasInput = true; captured++; maximumPendingInputs = 1;
            lastCaptureFrame = current.FrameNumber;
            nextCapture = current.NowSeconds + workOptions.RefreshIntervalSeconds;
        }
        void Compute()
        {
            if (EffectiveMode == HudComputationMode.Background && background.TryTake(out var outcome)) Receive(outcome);
            current = Fresh(current);
            if (!hasInput || current.NowSeconds < retryAt) return;
            if (!ValidInput(current, input, epoch)) { hasInput = false; input = default; expiredInputs++; nextCapture = double.NaN; return; }
            var request = new HudComputationRequest(input, input.Stamp.CapturedAtSeconds + workOptions.MaximumDataAgeSeconds);
            hasInput = false; input = default;
            if (EffectiveMode == HudComputationMode.Background)
            {
                if (!background.TrySubmit(request)) ComputationFailed("Background executor is unavailable.");
            }
            else Receive(HudComputationRunner.Execute(request, computer, executor.ComputationClock));
        }
        void ComputationFailed(string error)
        {
            computationErrors++; CancelComputation();
            retryAt = executor.NowSeconds + execution.ErrorRetryDelaySeconds;
            try { computeError?.Invoke(error); } catch { }
        }
        void Receive(in HudComputationOutcome outcome)
        {
            current = Fresh(current);
            if (outcome.Executed) computed++;
            long incoming = outcome.Input.Stamp.Sequence;
            if (incoming < minimumSequence || incoming > sequence || incoming <= highestReceived || incoming <= lastApplied
                || outcome.Input.Height != geometry.AnchorHeight || outcome.Input.Side != geometry.AnchorSide || outcome.Input.SizePixels != geometry.MarkerSizePixels
                || outcome.Input.Panel != geometry.Panel)
            { rejected++; return; }
            if (!ValidInput(current, outcome.Input, epoch))
            { rejected++; if (current.NowSeconds - outcome.Input.Stamp.CapturedAtSeconds > workOptions.MaximumDataAgeSeconds) expiredCompletions++; return; }
            highestReceived = incoming;
            if (outcome.Cancelled) { cancelledCompletions++; return; }
            if (outcome.Expired) { expiredCompletions++; rejected++; nextCapture = double.NaN; return; }
            if (outcome.Error != null) { ComputationFailed(outcome.Error); return; }
            var completed = new HudPreparedData(new(outcome.Input.Stamp, outcome.CompletedAtSeconds), outcome.Input.WorkEpoch, outcome.Input.SettingsRevision, outcome.Anchor);
            if (!ValidResult(current, completed, epoch) || !HudComputationRunner.ValidAnchor(outcome.Anchor) || outcome.Anchor.Panel != outcome.Input.Panel) { rejected++; return; }
            if (hasResult && result.Stamp.Input.Sequence > lastApplied) replaced++;
            result = completed; hasResult = true; accepted++;
        }
        void DiscardResult(in HudRuntimeContext context)
        {
            if (hasResult)
            {
                if (HudEligibility.EvaluateData(context, result.Stamp, Policy).Reason == HudDenyReason.ResultExpired) expiredResults++;
                else cancelledResults++;
            }
            hasResult = hasApplied = false; result = appliedResult = default; nextCapture = double.NaN;
        }
        public bool TryApply(in HudRuntimeContext context, in ThirdPersonDisplayFrame frame, long workEpoch, out HudPlacement next)
        {
            next = default; if (!executor.OnMainThread || disposed) return false;
            current = Fresh(context); display = frame; epoch = workEpoch; placed = false;
            if (!hasResult) return false;
            if (!ValidResult(current, result, workEpoch)) { DiscardResult(current); return false; }
            bool same = result.Stamp.Input.Sequence == lastApplied;
            bool valid = same ? HudEligibility.EvaluateDisplay(current, display, HudDisplayUsage.ScreenComposition, Policy).Allowed
                : HudEligibility.EvaluateResult(current, display, HudDisplayUsage.ScreenComposition, result.Stamp, lastApplied, Policy).Allowed;
            if (!valid) return false;
            if (!executor.TryExecute(current.FrameNumber, HudWorkStage.Application, applyAction) || !placed) return false;
            next = placement; appliedResult = result; hasApplied = true;
            if (same) reused++; else lastApplied = result.Stamp.Input.Sequence;
            applied++; return true;
        }
        void Apply()
        {
            // Position always uses this render's matrices and body pose, never a sampled/retained pose.
            if (result.Anchor.Panel.Enabled ? !motion.TryPlace(display,result.Anchor,out placement) : !HudProjection.TryPlace(display, result.Anchor, out placement)) return;
            presentation.Show(placement); placed = true;
        }
        public bool IsAppliedDataValid(in HudRuntimeContext context, long workEpoch) => executor.OnMainThread && !disposed && hasApplied
            && ValidResult(Fresh(context), appliedResult, workEpoch);
        public bool TryGetContent(in HudRuntimeContext context, long workEpoch, out HudContentSnapshot snapshot)
        { snapshot = default; return !disposed && content.TryGet(context, workEpoch, out snapshot); }
        public bool TryGetCollectionStatus(out HudCollectionStatus status) => content.TryGetStatus(out status);
        public bool TryGetSnapshot(out HudPerformanceSnapshot snapshot)
        {
            snapshot = default; if (!executor.TryGetSnapshot(out var timing)) return false;
            var worker = WorkerSnapshot;
            snapshot = new(timing, (hasInput ? 1 : 0) + worker.Pending, hasResult ? 1 : 0, maximumPendingInputs, captured, computed,
                applied, reused, cancelledInputs, cancelledResults, expiredInputs, expiredResults, rejected, replaced, lastApplied, revision)
            { Computation = new(execution.Mode, EffectiveMode, computer is IHudBackgroundDataComputer, modeChanges, accepted, computationErrors,
                cancelledCompletions, expiredCompletions, retryAt, worker) };
            return true;
        }
        public void Dispose()
        { if (!executor.OnMainThread || disposed) return; Cancel(); background?.Dispose(); disposed = true; }
    }
}
