using System;
using System.Diagnostics;
using Il2CppInterop.Runtime.Attributes;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using Player;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NQuaternion = System.Numerics.Quaternion;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        ThirdPersonViewProvider hudProvider;
        OptionalHeadCameraProbe headCameraProbe;
        bool hudLevelCleared;
        long hudClearedFloor;
        [HideFromIl2Cpp] internal ThirdPersonViewProvider HudProvider => hudProvider;
        [HideFromIl2Cpp] internal void InitializeHudBridge()
        {
            headCameraProbe = new OptionalHeadCameraProbe(() =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (assembly.GetName().Name == "PreLuna.HeadCamera") return assembly.GetType("PreLuna.HeadCamera.HeadCameraApi");
                return null;
            }, () => RefreshHudBridge());
            hudProvider = new ThirdPersonViewProvider(ReadHudObservation,
                error => Plugin.Instance?.Log.LogWarning("HUD 桥接订阅者/读取异常：" + error.Message));
            hudProvider.Refresh();
        }
        [HideFromIl2Cpp] internal void InvalidateHeadCameraProbe() => headCameraProbe?.Invalidate();
        [HideFromIl2Cpp] void RefreshHudBridge() => hudProvider?.Refresh();
        [HideFromIl2Cpp] BridgeObservation ReadHudObservation()
        {
            bool inLevel = GameStateManager.CurrentStateName == eGameStateName.InLevel;
            long floor = inLevel ? Id(LevelGeneration.Builder.CurrentFloor) : 0;
            if (!inLevel || hudLevelCleared && floor != hudClearedFloor) hudLevelCleared = false;
            var revision = Plugin.Instance?.HudConfigurationRevision ?? 1;
            var time = ThirdPersonApi.MonotonicSeconds;
            if (stopped || !inLevel || hudLevelCleared)
                return new(default, default, false, false, false, ViewSource.Unknown, ThirdPersonMode.Unknown,
                    ViewAvailability.OutOfLevel, revision, Time.frameCount, time);
            var local = Local();
            var binding = local == null ? default : new LocalViewBinding(
                PlayerKey(local), ObjectKey(local.PlayerSyncModel), ObjectKey(local.FPSCamera?.m_camera), floor, (int)local.DimensionIndex);
            var head = headCameraProbe?.Read() ?? new HeadCameraObservation(true, false, false, false, default);
            bool control = ControlTaken() || head.ControlsBody;
            bool ownView = head.Available && !control && !head.ReplacesMainView;
            var source = control ? ViewSource.BotControl : head.ReplacesMainView ? ViewSource.HeadCameraPreview : ViewSource.LocalThirdPerson;
            var subject = control || head.ReplacesMainView ? head.Subject : binding.LocalPlayer;
            bool focus = FocusAllowed();
            var gate = Gate(local);
            var availability = !head.Available ? ViewAvailability.Faulted : !ownView ? ViewAvailability.ControlTaken
                : state.Faulted ? ViewAvailability.Faulted : !Plugin.Instance.Settings.Enabled.Value ? ViewAvailability.Disabled
                : local == null || !binding.IsValid ? ViewAvailability.MissingPlayer
                : !focus ? ViewAvailability.FocusBlocked : gate == Core.FallbackReason.MissingModel ? ViewAvailability.MissingModel
                : gate != Core.FallbackReason.None ? ViewAvailability.TemporaryFallback : ViewAvailability.Ready;
            var mode = aim.Active && aim.Gesture.Mode == Core.ViewMode.FirstPersonAim ? ThirdPersonMode.FirstPersonAim
                : aim.Active && aim.Gesture.WantsAim ? ThirdPersonMode.ShoulderAim : ThirdPersonMode.Exploration;
            if (ownView && mode == ThirdPersonMode.FirstPersonAim)
            { source = ViewSource.LocalFirstPerson; availability = ViewAvailability.TemporaryFallback; }
            if (!ownView) mode = ThirdPersonMode.Unknown;
            return new(binding, subject, true, focus, ownView, source, mode, availability,
                revision, Time.frameCount, time);
        }
        [HideFromIl2Cpp] static ObjectIdentity ObjectKey(UnityEngine.Object value) =>
            value == null ? default : new(value.Pointer.ToInt64(), value.GetInstanceID());
        [HideFromIl2Cpp] static PlayerIdentity PlayerKey(PlayerAgent value) =>
            value == null || value.Owner == null ? default : new(ObjectKey(value), Id(value.Owner), value.PlayerSlotIndex);
        [HideFromIl2Cpp] static WorldPose Pose(Transform transform) =>
            new(ConvertView.N(transform.position), new NQuaternion(transform.rotation.x, transform.rotation.y, transform.rotation.z, transform.rotation.w));
        [HideFromIl2Cpp] static NMatrix Matrix(UnityEngine.Matrix4x4 value) => NMatrix.Transpose(new NMatrix(
            value.m00, value.m01, value.m02, value.m03, value.m10, value.m11, value.m12, value.m13,
            value.m20, value.m21, value.m22, value.m23, value.m30, value.m31, value.m32, value.m33));
        [HideFromIl2Cpp] void PublishHudFrame(LocalPlayerAgent local)
        {
            try
            {
            var camera = local.FPSCamera.m_camera;
            if (camera.targetTexture != null || headCameraProbe?.IsCaptureCamera(ObjectKey(camera)) == true)
            { hudProvider?.AbortMain(); return; }
            var rect = camera.pixelRect;
            var snapshot = new CameraSnapshot(Pose(camera.transform), Matrix(camera.worldToCameraMatrix),
                Matrix(camera.projectionMatrix), new(rect.x, rect.y, rect.width, rect.height));
            hudProvider?.PublishMain(snapshot, Pose(local.transform));
            }
            catch(Exception error)
            {hudProvider?.AbortMain();Plugin.Instance?.Log.LogWarning("HUD 相机快照暂不可用："+error.Message);}
        }
        [HideFromIl2Cpp] void ResetHudContext()
        {
            hudLevelCleared = true; hudClearedFloor = Id(LevelGeneration.Builder.CurrentFloor);
            hudProvider?.ResetContext();
        }
        [HideFromIl2Cpp] void StopHudBridge()
        { hudProvider?.Dispose(); headCameraProbe?.Dispose(); }
        [HideFromIl2Cpp] object HudBridgeDiagnostics()
        {
            if(hudProvider==null||!hudProvider.TryGetRuntimeContext(out var current))return new{available=false};
            bool world=hudProvider.TryGetDisplayFrame(new(Time.frameCount,HudDisplayUsage.WorldRender),out _);
            bool screen=hudProvider.TryGetDisplayFrame(new(Time.frameCount,HudDisplayUsage.ScreenComposition),out _);
            return new{available=true,apiVersion=ThirdPersonApi.ApiVersion,bridgeVersion=ThirdPersonApi.HudBridgeVersion,
                source=current.CurrentView.Source.ToString(),availability=current.CurrentView.Availability.ToString(),
                mode=current.CurrentView.Mode.ToString(),session=current.ExpectedContext.SessionId,
                contextEpoch=current.ExpectedContext.Epoch,eligibilityEpoch=current.CurrentView.EligibilityEpoch,
                configurationRevision=current.CurrentView.ConfigurationRevision,localPlayer=current.ExpectedBinding.LocalPlayer,
                subject=current.CurrentView.Subject,mainCamera=current.ExpectedBinding.MainCamera,
                frame=current.FrameNumber,phase=current.Render.Phase.ToString(),pass=current.Render.Identity.PassId,
                worldEligible=world,screenEligible=screen,headCamera=headCameraProbe?.Status,
                subscriberErrors=hudProvider.SubscriberErrors,mainPublications=hudProvider.MainPublications,
                screenCommits=hudProvider.ScreenCommits,auxiliaryRenders=hudProvider.AuxiliaryRenders,
                cancelledMainPasses=hudProvider.CancelledMainPasses};
        }
    }
}

namespace PreLuna.ThirdPerson
{
    // Public managed entry point. Calls involving Unity state are rejected off the main thread.
    public static class ThirdPersonApi
    {
        public const int ApiVersion = ThirdPersonContract.ApiVersion;
        public const int HudBridgeVersion = 1;
        public static double MonotonicSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public static bool TryGetProvider(out IThirdPersonViewProvider provider)
        {
            provider = null;
            var value = Runtime.Plugin.Instance?.Runtime?.HudProvider;
            if (value == null || !value.OnMainThread || !value.TryGetViewState(out _)) return false;
            provider = value; return true;
        }
        public static bool TryGetRuntimeContext(out HudRuntimeContext context)
        {
            context = default;
            var value = Runtime.Plugin.Instance?.Runtime?.HudProvider;
            return value != null && value.TryGetRuntimeContext(out context);
        }
    }
}
