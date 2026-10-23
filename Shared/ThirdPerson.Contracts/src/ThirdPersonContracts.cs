#nullable enable
using System;
using System.Numerics;
using System.Reflection;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PreLuna.ThirdPerson.Contracts
{
    public static class ThirdPersonContract
    {
        public const int ApiVersion = 1;
        public const string ProviderId = "PreLuna.ThirdPerson";
    }

    // Opaque comparison keys. Never dereference native handles on a worker thread.
    // Unity instance IDs may be negative; zero is the missing identity.
    public readonly record struct ObjectIdentity(long NativeHandle, int InstanceId)
    {
        public bool IsValid => NativeHandle != 0 && InstanceId != 0;
    }

    public readonly record struct PlayerIdentity(ObjectIdentity Agent, long OwnerHandle, int Slot)
    {
        public bool IsValid => Agent.IsValid && OwnerHandle != 0 && Slot >= 0;
    }

    // A fresh session per provider load; an increasing epoch per binding change.
    public readonly record struct ContextStamp(Guid SessionId, long Epoch)
    {
        public bool IsValid => SessionId != Guid.Empty && Epoch > 0;
    }

    public readonly record struct LocalViewBinding(
        PlayerIdentity LocalPlayer, ObjectIdentity CharacterModel,
        ObjectIdentity MainCamera, long FloorHandle, int DimensionIndex)
    {
        public bool IsValid => LocalPlayer.IsValid && CharacterModel.IsValid
            && MainCamera.IsValid && FloorHandle != 0 && DimensionIndex >= 0;
    }

    public enum ViewSource
    {
        Unknown, LocalFirstPerson, LocalThirdPerson, HeadCameraPreview, BotControl, Spectator
    }

    public enum ThirdPersonMode { Unknown, Exploration, ShoulderAim, FirstPersonAim }

    public enum ViewAvailability
    {
        Unknown, Ready, Disabled, TemporaryFallback, FocusBlocked, ControlTaken,
        MissingPlayer, MissingModel, OutOfLevel, Faulted
    }

    public readonly record struct ThirdPersonViewState(
        int ApiVersion, ContextStamp Context, LocalViewBinding Binding,
        PlayerIdentity Subject, long EligibilityEpoch, long ConfigurationRevision,
        ViewSource Source, ThirdPersonMode Mode, ViewAvailability Availability);

    public enum RenderSurface { Unknown, MainDisplay, AuxiliaryTexture }
    public enum RenderPhase { Unknown, MainActive, AuxiliaryActive, MainCommitted }
    public enum HudDisplayUsage { Unknown, WorldRender, ScreenComposition }

    // PassId increases per main-camera render invocation, including repeated renders in one frame.
    public readonly record struct RenderIdentity(ObjectIdentity Camera, RenderSurface Surface, long PassId);
    public readonly record struct RenderObservation(RenderIdentity Identity, RenderPhase Phase);

    public readonly record struct WorldPose(Vector3 Position, Quaternion Rotation);

    // Pixels, origin at the bottom left, matching Unity Camera.pixelRect.
    public readonly record struct PixelViewport(float X, float Y, float Width, float Height);

    // System.Numerics row-vector convention: transpose Unity's column-vector matrices at the boundary.
    public readonly record struct CameraSnapshot(
        WorldPose Pose, Matrix4x4 WorldToView, Matrix4x4 Projection, PixelViewport Viewport);

    // Value-only snapshot, published only after successful application of the third-person render scope.
    public readonly record struct ThirdPersonDisplayFrame(
        ThirdPersonViewState View, RenderIdentity Render, int FrameNumber,
        long Sequence, double CapturedAtSeconds, bool ThirdPersonApplied,
        CameraSnapshot Camera, WorldPose BodyPose);

    // Captured by the main-thread adapter from live state, never reconstructed from an old display frame.
    public readonly record struct HudRuntimeContext(
        bool InLevel, bool FocusAllowed, bool LocalBodyViewActive,
        ContextStamp ExpectedContext, LocalViewBinding ExpectedBinding,
        ThirdPersonViewState CurrentView, int FrameNumber, double NowSeconds,
        RenderObservation Render);

    // Data work deliberately carries no old camera or anchor pose.
    public readonly record struct HudWorkStamp(
        ThirdPersonViewState View, long Sequence, int SampleFrame, double CapturedAtSeconds);
    public readonly record struct HudResultStamp(HudWorkStamp Input, double CompletedAtSeconds);

    public readonly record struct HudValidityPolicy(
        double MaximumDisplayAgeSeconds, double MaximumDataAgeSeconds)
    {
        public static HudValidityPolicy Default => new(.1, .5);
    }

    public enum HudDenyReason
    {
        Unknown, None, ApiMismatch, InvalidClock, InvalidPolicy, OutOfLevel, FocusBlocked,
        NotLocalBodyView, InvalidIdentity, ContextMismatch, SubjectMismatch,
        SourceNotLocalThirdPerson, ModeNotThirdPerson, ViewUnavailable,
        EligibilityEpochMismatch, ConfigurationMismatch, ViewModeMismatch,
        RenderNotMain, RenderPhaseMismatch, RenderPassMismatch, DisplayFrameMismatch,
        SnapshotFromFuture, SnapshotExpired, InvalidSnapshot, InvalidSequence,
        InvalidResult, ResultFromFuture, ResultExpired, ResultOutOfOrder
    }

    public readonly record struct HudDecision(HudDenyReason Reason)
    {
        public bool Allowed => Reason == HudDenyReason.None;
    }

    public readonly record struct DisplayFrameRequest(int FrameNumber, HudDisplayUsage Usage);
    public readonly record struct ThirdPersonRenderNotice(
        ContextStamp Context, long EligibilityEpoch, int FrameNumber, RenderObservation Render);

    // Main-thread-only provider API. Step 2 supplies the game adapter and enforces thread affinity.
    // Subscribers must be isolated by the provider so HUD exceptions cannot break camera restoration.
    // StateChanged is lifecycle state; RenderChanged describes a particular render invocation.
    public interface IThirdPersonViewProvider
    {
        int ApiVersion { get; }
        string ProviderId { get; }
        bool TryGetViewState(out ThirdPersonViewState state);
        bool TryGetDisplayFrame(in DisplayFrameRequest request, out ThirdPersonDisplayFrame frame);
        event Action<ThirdPersonViewState>? StateChanged;
        event Action<ThirdPersonRenderNotice>? RenderChanged;
    }
}
