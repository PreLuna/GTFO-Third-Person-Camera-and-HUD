using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Core
{
    public enum HudLifecyclePhase { Dormant, Waiting, Initializing, Ready, Paused, Faulted, Disposed }

    public readonly record struct HudLifecycleOptions(bool Enabled, double ActivationDelaySeconds,
        double RetryDelaySeconds, float AnchorHeight, float AnchorSide, float MarkerSizePixels)
    {
        public HudPanelLayoutOptions Panel { get; init; }
        public static HudLifecycleOptions Default => new(true, .2, 1, 1.2f, .65f, 12);
        public HudLifecycleOptions Sanitized() => new(Enabled,
            double.IsFinite(ActivationDelaySeconds) ? Math.Clamp(ActivationDelaySeconds, 0, 3) : .2,
            double.IsFinite(RetryDelaySeconds) ? Math.Clamp(RetryDelaySeconds, .25, 10) : 1,
            float.IsFinite(AnchorHeight) ? Math.Clamp(AnchorHeight, 0, 3) : 1.2f,
            float.IsFinite(AnchorSide) ? Math.Clamp(AnchorSide, -2, 2) : .65f,
            float.IsFinite(MarkerSizePixels) ? Math.Clamp(MarkerSizePixels, 4, 48) : 12)
            { Panel = Panel.Enabled ? Panel.Sanitized() : default };
    }

    public readonly record struct HudTick(bool InLevel, int FrameNumber, double NowSeconds,
        bool ProviderAvailable, HudRuntimeContext Current);

    // Immutable render packet. It contains no Unity objects or references to previous players.
    public readonly record struct HudPlacement(ThirdPersonViewState View, RenderIdentity Render,
        int FrameNumber, long DisplaySequence, Vector3 WorldAnchor, Vector2 PixelCenter, float SizePixels)
    { public HudPanelPlacement Panel { get; init; } }

    public readonly record struct HudLifecycleStatus(HudLifecyclePhase Phase, bool Visible,
        bool HasResources, int InitializationStep, ContextStamp BoundContext, long WorkEpoch,
        long Bindings, long InitializationSteps, long Pauses, long Releases, long Presentations,
        long Errors, int LastShownFrame, int LastEvidenceFrame);

    // Main-thread resource port. Later render backends can replace the placeholder without changing lifecycle.
    public interface IHudPresentation
    {
        int InitializationStep { get; }
        bool IsReady { get; }
        bool HasResources { get; }
        bool IsHealthy { get; }
        void InitializeNextStep();
        void Bind(ContextStamp context, LocalViewBinding binding);
        void Show(in HudPlacement placement);
        void Hide();
        void Release();
    }
}
