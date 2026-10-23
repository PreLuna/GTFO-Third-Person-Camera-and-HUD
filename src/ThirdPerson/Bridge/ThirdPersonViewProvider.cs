#nullable enable
using System;
using System.Collections.Generic;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPerson.Bridge
{
    public readonly record struct BridgeObservation(LocalViewBinding Binding, PlayerIdentity Subject,
        bool InLevel, bool FocusAllowed, bool LocalBodyViewActive, ViewSource Source,
        ThirdPersonMode Mode, ViewAvailability Availability, long ConfigurationRevision,
        int FrameNumber, double NowSeconds);

    // All mutation and reads are main-thread-only. This class is also exercised without Unity.
    public sealed class ThirdPersonViewProvider : IThirdPersonViewProvider, IDisposable
    {
        readonly Func<BridgeObservation> observe;
        readonly Action<Exception>? error;
        readonly int thread = Environment.CurrentManagedThreadId;
        readonly Guid session = Guid.NewGuid();
        readonly List<Action<ThirdPersonViewState>> stateHandlers = new();
        readonly List<Action<ThirdPersonRenderNotice>> renderHandlers = new();
        Action<ThirdPersonViewState>[] stateSubscribers = Array.Empty<Action<ThirdPersonViewState>>();
        Action<ThirdPersonRenderNotice>[] renderSubscribers = Array.Empty<Action<ThirdPersonRenderNotice>>();
        readonly Stack<RenderObservation> auxiliary = new();
        BridgeObservation live;
        ThirdPersonViewState view;
        ThirdPersonDisplayFrame display;
        RenderObservation render;
        LocalViewBinding binding;
        long contextEpoch, eligibilityEpoch = 1, sequence, pass;
        int frame = -1;
        bool hasBinding, hasDisplay, pendingNative, restored, disposed, refreshing;
        public int ApiVersion => ThirdPersonContract.ApiVersion;
        public string ProviderId => ThirdPersonContract.ProviderId;
        public bool OnMainThread => Environment.CurrentManagedThreadId == thread;
        public int SubscriberErrors { get; private set; }
        public long MainPublications { get; private set; }
        public long ScreenCommits { get; private set; }
        public long AuxiliaryRenders { get; private set; }
        public long CancelledMainPasses { get; private set; }

        public ThirdPersonViewProvider(Func<BridgeObservation> observe, Action<Exception>? error = null)
        { this.observe = observe; this.error = error; }

        public event Action<ThirdPersonViewState>? StateChanged
        {
            add { if (OnMainThread && !disposed && value != null) { stateHandlers.Add(value); stateSubscribers = stateHandlers.ToArray(); } }
            remove { if (OnMainThread && value != null) { RemoveLast(stateHandlers, value); stateSubscribers = stateHandlers.ToArray(); } }
        }
        public event Action<ThirdPersonRenderNotice>? RenderChanged
        {
            add { if (OnMainThread && !disposed && value != null) { renderHandlers.Add(value); renderSubscribers = renderHandlers.ToArray(); } }
            remove { if (OnMainThread && value != null) { RemoveLast(renderHandlers, value); renderSubscribers = renderHandlers.ToArray(); } }
        }
        static void RemoveLast<T>(List<Action<T>> handlers, Action<T> handler)
        { int index = handlers.LastIndexOf(handler); if (index >= 0) handlers.RemoveAt(index); }
        void Report(Exception failure) { SubscriberErrors++; try { error?.Invoke(failure); } catch { } }
        void SendState() { var value = view; foreach (var handler in stateSubscribers) try { handler(value); } catch (Exception e) { Report(e); } }
        void SendRender() { var notice = new ThirdPersonRenderNotice(view.Context, view.EligibilityEpoch, frame, render); foreach (var handler in renderSubscribers) try { handler(notice); } catch (Exception e) { Report(e); } }

        public bool Refresh()
        {
            if (!OnMainThread || disposed) return false;
            if (refreshing) return true;
            refreshing = true;
            try
            {
                live = observe();
                if (live.FrameNumber != frame)
                {
                    if (pendingNative) AbortMain();
                    frame = live.FrameNumber; ClearRender();
                }
                if (!hasBinding || live.Binding != binding)
                {
                    binding = live.Binding; hasBinding = true;
                    contextEpoch = checked(contextEpoch + 1);
                    eligibilityEpoch = checked(eligibilityEpoch + 1);
                    ClearRender();
                }
                var availability = live.Availability;
                if (availability == ViewAvailability.Ready)
                    availability = view.Availability == ViewAvailability.Ready && view.Binding == binding
                        ? ViewAvailability.Ready : ViewAvailability.TemporaryFallback;
                SetState(live.Source, live.Mode, availability);
                return true;
            }
            catch (Exception e)
            {
                Report(e); ClearRender();
                SetState(ViewSource.Unknown, ThirdPersonMode.Unknown, ViewAvailability.Faulted);
                return false;
            }
            finally { refreshing = false; }
        }

        void SetState(ViewSource source, ThirdPersonMode mode, ViewAvailability availability)
        {
            bool revoked = view.Source != source || view.Availability != availability
                || view.Subject != live.Subject || view.Context != new ContextStamp(session, contextEpoch);
            if (revoked) { eligibilityEpoch = checked(eligibilityEpoch + 1); ClearRender(); }
            if (view.ConfigurationRevision != live.ConfigurationRevision || view.Mode != mode) ClearRender();
            var next = new ThirdPersonViewState(ApiVersion, new(session, contextEpoch), binding,
                live.Subject, eligibilityEpoch, live.ConfigurationRevision, source, mode, availability);
            if (next == view) return;
            view = next; SendState();
        }

        void ClearRender()
        {
            bool changed = render.Phase != RenderPhase.Unknown;
            if(pendingNative)CancelledMainPasses++;
            hasDisplay = pendingNative = restored = false; display = default; render = default; auxiliary.Clear();
            if (changed) SendRender();
        }

        HudRuntimeContext Context() => new(live.InLevel, live.FocusAllowed, live.LocalBodyViewActive,
            new(session, contextEpoch), binding, view, frame, live.NowSeconds, render);

        public bool TryGetRuntimeContext(out HudRuntimeContext current)
        { current = default; if (!Refresh()) return false; current = Context(); return true; }
        public bool TryGetViewState(out ThirdPersonViewState state)
        { state = default; if (!Refresh()) return false; state = view; return true; }
        public bool TryGetDisplayFrame(in DisplayFrameRequest request, out ThirdPersonDisplayFrame snapshot)
        {
            snapshot = default;
            if (!Refresh() || !hasDisplay || request.FrameNumber != frame) return false;
            if (!HudEligibility.EvaluateDisplay(Context(), display, request.Usage, HudValidityPolicy.Default).Allowed) return false;
            snapshot = display; return true;
        }

        // Called for every real main-camera attempt, even if it later falls back to first person.
        public void BeginMainAttempt()
        { if (!Refresh()) return; if (pendingNative) AbortMain(); ClearRender(); }

        public bool PublishMain(CameraSnapshot camera, WorldPose body)
        {
            if (!Refresh() || live.Availability != ViewAvailability.Ready || !binding.IsValid
                || !live.InLevel || !live.FocusAllowed || !live.LocalBodyViewActive
                || live.Subject != binding.LocalPlayer || live.Source != ViewSource.LocalThirdPerson
                || live.Mode is not (ThirdPersonMode.Exploration or ThirdPersonMode.ShoulderAim)) return false;
            if (auxiliary.Count != 0) { AbortMain(); return false; }
            SetState(live.Source, live.Mode, ViewAvailability.Ready);
            var identity = new RenderIdentity(binding.MainCamera, RenderSurface.MainDisplay, checked(++pass));
            render = new(identity, RenderPhase.MainActive);
            display = new(view, identity, frame, checked(++sequence), live.NowSeconds, true, camera, body);
            hasDisplay = pendingNative = true; restored = false;
            if (!HudEligibility.EvaluateDisplay(Context(), display, HudDisplayUsage.WorldRender, HudValidityPolicy.Default).Allowed)
            { AbortMain(); return false; }
            MainPublications++;
            SendRender(); return hasDisplay;
        }

        public void MainRestored(bool success)
        {
            if (!OnMainThread || disposed || !pendingNative) return;
            if (!success || auxiliary.Count != 0) { AbortMain(); return; }
            restored = true; render = default; SendRender();
        }
        public void NativeMainCompleted(bool success)
        {
            if (!OnMainThread || disposed || !pendingNative) return;
            if (!Refresh() || !pendingNative) return;
            if (!success || !restored || auxiliary.Count != 0 || !hasDisplay) { AbortMain(); return; }
            pendingNative = false; render = new(display.Render, RenderPhase.MainCommitted); ScreenCommits++; SendRender();
        }
        public void AbortMain()
        {
            if (!OnMainThread || disposed) return;
            bool wasReady = view.Availability == ViewAvailability.Ready;
            ClearRender();
            if (wasReady) SetState(live.Source, live.Mode, ViewAvailability.TemporaryFallback);
        }
        public void AuxiliaryBegin(ObjectIdentity camera)
        {
            if (!Refresh() || !camera.IsValid || camera == binding.MainCamera) return;
            auxiliary.Push(render);
            AuxiliaryRenders++;
            render = new(new(camera, RenderSurface.AuxiliaryTexture, checked(++pass)), RenderPhase.AuxiliaryActive);
            SendRender();
        }
        public void AuxiliaryEnd(ObjectIdentity camera)
        {
            if (!OnMainThread || disposed || auxiliary.Count == 0) return;
            if (render.Phase != RenderPhase.AuxiliaryActive || render.Identity.Camera != camera)
            { AbortMain(); return; }
            render = auxiliary.Pop(); SendRender();
        }
        public void ResetContext()
        {
            if (!OnMainThread || disposed) return;
            AbortMain(); hasBinding = false; binding = default;
            contextEpoch = checked(contextEpoch + 1); eligibilityEpoch = checked(eligibilityEpoch + 1);
            live = live with { InLevel = false, Binding = default, Subject = default, Availability = ViewAvailability.OutOfLevel };
            SetState(ViewSource.Unknown, ThirdPersonMode.Unknown, ViewAvailability.OutOfLevel);
        }
        public void Dispose()
        {
            if (!OnMainThread || disposed) return;
            ResetContext(); disposed = true; stateHandlers.Clear(); renderHandlers.Clear();
            stateSubscribers = Array.Empty<Action<ThirdPersonViewState>>(); renderSubscribers = Array.Empty<Action<ThirdPersonRenderNotice>>();
        }
    }
}
