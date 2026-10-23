using System;
using System.Numerics;

namespace PreLuna.ThirdPerson.Contracts
{
    // Pure validation: no game objects, clocks, mutable globals or thread scheduling.
    public static class HudEligibility
    {
        public static HudDecision EvaluateCurrent(in HudRuntimeContext current)
        {
            if (current.CurrentView.ApiVersion != ThirdPersonContract.ApiVersion) return Deny(HudDenyReason.ApiMismatch);
            if (current.FrameNumber < 0 || !TimeValid(current.NowSeconds)) return Deny(HudDenyReason.InvalidClock);
            if (!current.InLevel) return Deny(HudDenyReason.OutOfLevel);
            if (!current.FocusAllowed) return Deny(HudDenyReason.FocusBlocked);
            if (!current.LocalBodyViewActive) return Deny(HudDenyReason.NotLocalBodyView);
            if (!current.ExpectedContext.IsValid || !current.ExpectedBinding.IsValid) return Deny(HudDenyReason.InvalidIdentity);
            return ValidateView(current, current.CurrentView);
        }

        public static HudDecision EvaluateDisplay(in HudRuntimeContext current,
            in ThirdPersonDisplayFrame display, HudDisplayUsage usage, in HudValidityPolicy policy)
        {
            var decision = EvaluateCurrent(current);
            if (!decision.Allowed) return decision;
            if (!PolicyValid(policy)) return Deny(HudDenyReason.InvalidPolicy);
            decision = ValidateView(current, display.View);
            if (!decision.Allowed) return decision;
            if (display.View.Mode != current.CurrentView.Mode) return Deny(HudDenyReason.ViewModeMismatch);
            if (!display.ThirdPersonApplied) return Deny(HudDenyReason.InvalidSnapshot);
            if (display.Sequence <= 0) return Deny(HudDenyReason.InvalidSequence);
            if (display.FrameNumber != current.FrameNumber) return Deny(HudDenyReason.DisplayFrameMismatch);
            if (!TimeValid(display.CapturedAtSeconds)) return Deny(HudDenyReason.InvalidSnapshot);
            if (display.CapturedAtSeconds > current.NowSeconds) return Deny(HudDenyReason.SnapshotFromFuture);
            if (current.NowSeconds - display.CapturedAtSeconds > policy.MaximumDisplayAgeSeconds)
                return Deny(HudDenyReason.SnapshotExpired);

            if (!MainRender(current.Render.Identity, current.ExpectedBinding.MainCamera)
                || !MainRender(display.Render, current.ExpectedBinding.MainCamera)) return Deny(HudDenyReason.RenderNotMain);
            if (current.Render.Identity != display.Render) return Deny(HudDenyReason.RenderPassMismatch);
            bool phaseMatches = usage switch
            {
                HudDisplayUsage.WorldRender => current.Render.Phase == RenderPhase.MainActive,
                HudDisplayUsage.ScreenComposition => current.Render.Phase == RenderPhase.MainCommitted,
                _ => false
            };
            if (!phaseMatches) return Deny(HudDenyReason.RenderPhaseMismatch);
            if (!CameraValid(display.Camera) || !PoseValid(display.BodyPose)) return Deny(HudDenyReason.InvalidSnapshot);
            return Allow();
        }

        // Revalidate retained data on every use, even if no new worker result has arrived.
        public static HudDecision EvaluateData(in HudRuntimeContext current,
            in HudResultStamp result, in HudValidityPolicy policy)
        {
            var decision = EvaluateCurrent(current);
            if (!decision.Allowed) return decision;
            if (!PolicyValid(policy)) return Deny(HudDenyReason.InvalidPolicy);
            decision = ValidateView(current, result.Input.View);
            if (!decision.Allowed) return decision;
            if (result.Input.Sequence <= 0) return Deny(HudDenyReason.InvalidSequence);
            if (result.Input.SampleFrame < 0 || !TimeValid(result.Input.CapturedAtSeconds)
                || !TimeValid(result.CompletedAtSeconds)
                || result.CompletedAtSeconds < result.Input.CapturedAtSeconds) return Deny(HudDenyReason.InvalidResult);
            if (result.Input.SampleFrame > current.FrameNumber || result.Input.CapturedAtSeconds > current.NowSeconds
                || result.CompletedAtSeconds > current.NowSeconds) return Deny(HudDenyReason.ResultFromFuture);
            // Age is measured from sampling, not task completion; a slow job cannot renew old data.
            if (current.NowSeconds - result.Input.CapturedAtSeconds > policy.MaximumDataAgeSeconds)
                return Deny(HudDenyReason.ResultExpired);
            return Allow();
        }

        public static HudDecision EvaluateResult(in HudRuntimeContext current,
            in ThirdPersonDisplayFrame display, HudDisplayUsage usage, in HudResultStamp result,
            long lastAppliedSequence, in HudValidityPolicy policy)
        {
            var decision = EvaluateDisplay(current, display, usage, policy);
            if (!decision.Allowed) return decision;
            decision = EvaluateData(current, result, policy);
            if (!decision.Allowed) return decision;
            if (lastAppliedSequence < 0) return Deny(HudDenyReason.InvalidSequence);
            if (result.Input.Sequence <= lastAppliedSequence) return Deny(HudDenyReason.ResultOutOfOrder);
            return Allow();
        }

        static HudDecision ValidateView(in HudRuntimeContext current, in ThirdPersonViewState view)
        {
            if (view.ApiVersion != ThirdPersonContract.ApiVersion) return Deny(HudDenyReason.ApiMismatch);
            if (!view.Context.IsValid || !view.Binding.IsValid || !view.Subject.IsValid
                || view.EligibilityEpoch <= 0 || view.ConfigurationRevision <= 0) return Deny(HudDenyReason.InvalidIdentity);
            if (view.Context != current.ExpectedContext || view.Binding != current.ExpectedBinding)
                return Deny(HudDenyReason.ContextMismatch);
            if (view.Subject != current.ExpectedBinding.LocalPlayer) return Deny(HudDenyReason.SubjectMismatch);
            if (view.Source != ViewSource.LocalThirdPerson) return Deny(HudDenyReason.SourceNotLocalThirdPerson);
            if (view.Mode is not (ThirdPersonMode.Exploration or ThirdPersonMode.ShoulderAim)) return Deny(HudDenyReason.ModeNotThirdPerson);
            if (view.Availability != ViewAvailability.Ready) return Deny(HudDenyReason.ViewUnavailable);
            if (view.EligibilityEpoch != current.CurrentView.EligibilityEpoch) return Deny(HudDenyReason.EligibilityEpochMismatch);
            if (view.ConfigurationRevision != current.CurrentView.ConfigurationRevision) return Deny(HudDenyReason.ConfigurationMismatch);
            return Allow();
        }

        static bool MainRender(RenderIdentity render, ObjectIdentity expected) =>
            render.Camera.IsValid && render.Camera == expected && render.Surface == RenderSurface.MainDisplay && render.PassId > 0;

        static bool TimeValid(double value) => double.IsFinite(value) && value >= 0;
        static bool PolicyValid(HudValidityPolicy policy) => double.IsFinite(policy.MaximumDisplayAgeSeconds)
            && double.IsFinite(policy.MaximumDataAgeSeconds) && policy.MaximumDisplayAgeSeconds > 0 && policy.MaximumDataAgeSeconds > 0;
        static HudDecision Deny(HudDenyReason reason) => new(reason);
        static HudDecision Allow() => new(HudDenyReason.None);

        static bool PoseValid(WorldPose pose) => VectorValid(pose.Position)
            && float.IsFinite(pose.Rotation.X) && float.IsFinite(pose.Rotation.Y)
            && float.IsFinite(pose.Rotation.Z) && float.IsFinite(pose.Rotation.W)
            && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .01f;

        static bool CameraValid(CameraSnapshot camera) => PoseValid(camera.Pose)
            && float.IsFinite(camera.Viewport.X) && float.IsFinite(camera.Viewport.Y)
            && float.IsFinite(camera.Viewport.Width) && float.IsFinite(camera.Viewport.Height)
            && camera.Viewport.Width > 0 && camera.Viewport.Height > 0
            && Invertible(camera.WorldToView) && Invertible(camera.Projection);

        static bool VectorValid(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        static bool Invertible(Matrix4x4 matrix) => MatrixValid(matrix)
            && Matrix4x4.Invert(matrix, out var inverse) && MatrixValid(inverse);
        static bool MatrixValid(Matrix4x4 m) => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
            && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
            && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
            && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
    }
}
