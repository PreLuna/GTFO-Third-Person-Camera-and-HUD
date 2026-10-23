#nullable enable
using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Core
{
    public sealed class HudLifecycle : IDisposable
    {
        readonly IHudPresentation presentation;
        readonly Action<Exception>? report;
        readonly HudMainThreadExecutor executor;
        readonly HudDataPipeline data;
        readonly Action initializeAction;
        readonly int mainThread = Environment.CurrentManagedThreadId;
        HudLifecycleOptions options = HudLifecycleOptions.Default;
        HudLifecyclePhase phase;
        ContextStamp boundContext, evidenceContext;
        LocalViewBinding boundBinding, evidenceBinding;
        long evidenceEligibility;
        double waitingSince = double.NaN, evidenceAt = double.NaN, retryAt;
        int evidenceFrame = -1, lastInitializationFrame = -1, lastShownFrame = -1;
        long workEpoch = 1, bindings, steps, pauses, releases, presentations, errors;
        bool visible, disposed;
        HudPlacement placed;
        public bool OnMainThread => Environment.CurrentManagedThreadId == mainThread;
        public HudLifecycle(IHudPresentation presentation, Action<Exception>? report = null, Func<double>? clock = null,
            IHudDataSource? source = null, IHudDataComputer? computer = null, IHudBackgroundExecutor? background = null, Action<string>? computeError = null,
            IHudContentSource? contentSource = null, Action<string>? collectionError = null)
        {
            this.presentation = presentation; this.report = report;
            executor = new HudMainThreadExecutor(clock); data = new HudDataPipeline(executor, presentation, source, computer, background, computeError, contentSource, collectionError);
            initializeAction = InitializeOneStep;
        }
        void InitializeOneStep() { presentation.InitializeNextStep(); steps++; }
        public bool TryGetPerformance(out HudPerformanceSnapshot snapshot) => data.TryGetSnapshot(out snapshot);
        public bool TryGetContent(in HudRuntimeContext current, out HudContentSnapshot snapshot)
        { snapshot = default; return OnMainThread && !disposed && data.TryGetContent(current, workEpoch, out snapshot); }
        public bool TryGetCollectionStatus(out HudCollectionStatus status) => data.TryGetCollectionStatus(out status);

        public bool TryGetStatus(out HudLifecycleStatus status)
        {
            status = default; if (!OnMainThread) return false;
            status = new(phase, visible, presentation.HasResources, presentation.InitializationStep, boundContext,
                workEpoch, bindings, steps, pauses, releases, presentations, errors, lastShownFrame, evidenceFrame);
            return true;
        }

        public void Tick(in HudTick tick, in HudLifecycleOptions configuration)
            => Tick(tick, configuration, HudWorkOptions.Default);
        public void Tick(in HudTick tick, in HudLifecycleOptions configuration, in HudWorkOptions workOptions)
            => Tick(tick, configuration, workOptions, HudExecutionOptions.Default);
        public void Tick(in HudTick tick, in HudLifecycleOptions configuration, in HudWorkOptions workOptions, in HudExecutionOptions execution)
            => Tick(tick, configuration, workOptions, execution, HudCollectionOptions.Default);
        public void Tick(in HudTick tick, in HudLifecycleOptions configuration, in HudWorkOptions workOptions, in HudExecutionOptions execution, in HudCollectionOptions collection)
        {
            if (!OnMainThread || disposed) return;
            options = configuration.Sanitized();
            try
            {
                executor.BeginFrame(tick.FrameNumber, workOptions);
                data.Configure(workOptions, options, execution, collection);
                if (!tick.InLevel || !options.Enabled)
                { ReleaseOwned(true); phase = HudLifecyclePhase.Dormant; return; }
                if (!double.IsFinite(tick.NowSeconds) || tick.NowSeconds < 0 || tick.FrameNumber < 0)
                { Pause(); return; }
                if (lastShownFrame != tick.FrameNumber) Hide();
                if (!tick.ProviderAvailable || tick.Current.FrameNumber != tick.FrameNumber
                    || !HudEligibility.EvaluateCurrent(tick.Current).Allowed)
                { Pause(); return; }
                if (phase == HudLifecyclePhase.Faulted && tick.NowSeconds < retryAt) return;
                var current = tick.Current;
                if (boundContext != current.ExpectedContext || boundBinding != current.ExpectedBinding)
                {
                    Hide(); data.Cancel(); workEpoch++;
                    bool newWorld = boundContext.IsValid && (boundContext.SessionId != current.ExpectedContext.SessionId
                        || boundBinding.FloorHandle != current.ExpectedBinding.FloorHandle);
                    if (newWorld) ReleaseOwned(false);
                    boundContext = current.ExpectedContext; boundBinding = current.ExpectedBinding;
                    presentation.Bind(boundContext, boundBinding); bindings++;
                }
                if (!presentation.IsHealthy)
                { ReleaseOwned(false); presentation.Bind(boundContext, boundBinding); waitingSince = tick.NowSeconds; workEpoch++; }
                if (!HasFreshEvidence(current, tick.FrameNumber, tick.NowSeconds))
                { Pause(); return; }
                if (presentation.IsReady) { phase = HudLifecyclePhase.Ready; data.Prepare(current, workEpoch); return; }
                if (!double.IsFinite(waitingSince)) waitingSince = tick.NowSeconds;
                if (tick.NowSeconds - waitingSince + 1e-9 < options.ActivationDelaySeconds)
                { phase = HudLifecyclePhase.Waiting; return; }
                phase = HudLifecyclePhase.Initializing;
                // Each initialization step is atomic, admitted by the shared frame budget.
                if (lastInitializationFrame == tick.FrameNumber) return;
                if (!executor.TryExecute(tick.FrameNumber, HudWorkStage.Initialization, initializeAction)) return;
                lastInitializationFrame = tick.FrameNumber;
                if (presentation.IsReady) { phase = HudLifecyclePhase.Ready; data.Prepare(current, workEpoch); }
            }
            catch (Exception error) { Fail(error, tick.NowSeconds); }
        }

        public void ObserveState(in ThirdPersonViewState state)
        {
            if (!OnMainThread || disposed) return;
            try
            {
                if (state.Availability == ViewAvailability.OutOfLevel)
                { ReleaseOwned(true); phase = HudLifecyclePhase.Dormant; return; }
                if (state.ApiVersion != ThirdPersonContract.ApiVersion || state.Source != ViewSource.LocalThirdPerson
                    || state.Availability != ViewAvailability.Ready || state.Subject != state.Binding.LocalPlayer
                    || state.Mode is not (ThirdPersonMode.Exploration or ThirdPersonMode.ShoulderAim))
                { Pause(); return; }
                if (boundContext.IsValid && (state.Context != boundContext || state.Binding != boundBinding))
                { Hide(); data.Cancel(); workEpoch++; ResetEvidence(); phase = HudLifecyclePhase.Paused; }
            }
            catch (Exception error) { Fail(error, double.IsFinite(evidenceAt) ? evidenceAt : 0); }
        }

        public void ObserveCommitted(in HudRuntimeContext current, in ThirdPersonDisplayFrame display)
        {
            if (!OnMainThread || disposed) return;
            try
            {
                if (!options.Enabled || !HudEligibility.EvaluateDisplay(current, display,
                    HudDisplayUsage.ScreenComposition, HudValidityPolicy.Default).Allowed)
                { Hide(); return; }
                if (evidenceContext != display.View.Context || evidenceBinding != display.View.Binding
                    || evidenceEligibility != display.View.EligibilityEpoch)
                    waitingSince = current.NowSeconds;
                evidenceContext = display.View.Context; evidenceBinding = display.View.Binding;
                evidenceEligibility = display.View.EligibilityEpoch; evidenceFrame = display.FrameNumber;
                evidenceAt = current.NowSeconds;
                if (!presentation.IsReady || boundContext != display.View.Context || boundBinding != display.View.Binding)
                { Hide(); return; }
                if (!data.TryApply(current, display, workEpoch, out var next)) { Hide(); return; }
                placed = next; visible = true; phase = HudLifecyclePhase.Ready;
                lastShownFrame = current.FrameNumber; presentations++;
            }
            catch (Exception error) { Fail(error, current.NowSeconds); }
        }

        public bool CanDraw(in HudRuntimeContext current, in ThirdPersonDisplayFrame display)
        {
            if (!OnMainThread || disposed || !visible || !options.Enabled || phase != HudLifecyclePhase.Ready) return false;
            return placed.FrameNumber == current.FrameNumber && placed.DisplaySequence == display.Sequence
                && placed.View == display.View && placed.Render == display.Render
                && boundContext == current.ExpectedContext && boundBinding == current.ExpectedBinding
                && data.IsAppliedDataValid(current, workEpoch)
                && HudEligibility.EvaluateDisplay(current, display, HudDisplayUsage.ScreenComposition, HudValidityPolicy.Default).Allowed;
        }

        public bool TryDraw(in HudRuntimeContext current, in ThirdPersonDisplayFrame display, Action draw)
        {
            if (!CanDraw(current, display)) { HideForRender(); return false; }
            try
            {
                if (executor.TryExecute(current.FrameNumber, HudWorkStage.Draw, draw)) return true;
                Hide(); return false;
            }
            catch (Exception error) { Fail(error, current.NowSeconds); return false; }
        }

        public void HideForRender()
        {
            if (!OnMainThread || disposed) return;
            try { Hide(); } catch (Exception error) { Fail(error, double.IsFinite(evidenceAt) ? evidenceAt : 0); }
        }
        public void ReportPresentationFailure(Exception error, double now)
        { if (OnMainThread && !disposed) Fail(error, now); }
        bool HasFreshEvidence(in HudRuntimeContext current, int frame, double now) =>
            evidenceContext == current.ExpectedContext && evidenceBinding == current.ExpectedBinding
            && evidenceEligibility == current.CurrentView.EligibilityEpoch && evidenceFrame >= frame - 1 && evidenceFrame <= frame
            && double.IsFinite(evidenceAt) && evidenceAt <= now && now - evidenceAt <= .5;
        void Hide() { visible = false; placed = default; presentation.Hide(); }
        void ResetEvidence()
        { evidenceContext = default; evidenceBinding = default; evidenceEligibility = 0; evidenceFrame = -1; evidenceAt = waitingSince = double.NaN; }
        void Pause()
        {
            Hide(); data.Cancel();
            if (phase is not (HudLifecyclePhase.Paused or HudLifecyclePhase.Dormant)) { pauses++; workEpoch++; }
            ResetEvidence();
            if (phase != HudLifecyclePhase.Faulted) phase = presentation.HasResources ? HudLifecyclePhase.Paused : HudLifecyclePhase.Dormant;
        }
        void ReleaseOwned(bool clearBinding)
        {
            bool hadResources = presentation.HasResources;
            visible = false; placed = default; data.Cancel();
            try { presentation.Hide(); }
            finally
            {
                try { presentation.Release(); }
                finally
                {
                    if (hadResources) { releases++; workEpoch++; }
                    if (clearBinding) { boundContext = default; boundBinding = default; ResetEvidence(); }
                }
            }
        }
        void Fail(Exception error, double now)
        {
            errors++; workEpoch++; visible = false; placed = default;
            try { ReleaseOwned(true); } catch { }
            ResetEvidence(); phase = HudLifecyclePhase.Faulted;
            retryAt = (double.IsFinite(now) && now >= 0 ? now : 0) + options.RetryDelaySeconds;
            try { report?.Invoke(error); } catch { }
        }
        public void Dispose()
        {
            if (!OnMainThread || disposed) return;
            try { ReleaseOwned(true); } catch (Exception error) { errors++; try { report?.Invoke(error); } catch { } }
            finally { try { data.Dispose(); } finally { disposed = true; phase = HudLifecyclePhase.Disposed; } }
        }
    }
}
