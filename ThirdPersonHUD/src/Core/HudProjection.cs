using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Core
{
    public static class HudProjection
    {
        public static bool TryPlace(in ThirdPersonDisplayFrame display, in HudLifecycleOptions options,
            out HudPlacement placement)
        {
            var settings = options.Sanitized();
            return TryPlace(display, new HudAnchorSpec(Vector3.UnitY * settings.AnchorHeight, settings.AnchorSide, settings.MarkerSizePixels) { Panel = settings.Panel }, out placement);
        }
        public static bool TryPlace(in ThirdPersonDisplayFrame display, in HudAnchorSpec spec, out HudPlacement placement)
        {
            placement = default;
            if(spec.Panel.Enabled)
            {
                var rightAxis=Vector3.Transform(Vector3.UnitX,display.Camera.Pose.Rotation);
                float shoulder=Vector3.Dot(display.Camera.Pose.Position-display.BodyPose.Position,rightAxis);
                float side=spec.Panel.MirrorWithShoulder&&Math.Abs(shoulder)>=.05f?MathF.Sign(shoulder):spec.Panel.FixedSide;
                return HudPanelProjection.TryPlace(display,spec.Panel,side,out placement);
            }
            var right = Vector3.Transform(Vector3.UnitX, display.Camera.Pose.Rotation);
            var anchor = display.BodyPose.Position + spec.WorldOffset + right * spec.CameraSide;
            if (!float.IsFinite(spec.SizePixels) || spec.SizePixels < 4 || spec.SizePixels > 48) return false;
            if (!Finite(anchor)) return false;
            var view = Vector4.Transform(new Vector4(anchor, 1), display.Camera.WorldToView);
            var clip = Vector4.Transform(view, display.Camera.Projection);
            if (!float.IsFinite(clip.W) || clip.W <= .0001f) return false;
            var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            if (!Finite(ndc) || ndc.X < -1 || ndc.X > 1 || ndc.Y < -1 || ndc.Y > 1 || ndc.Z < -1 || ndc.Z > 1) return false;
            var rect = display.Camera.Viewport;
            if (!float.IsFinite(rect.X) || !float.IsFinite(rect.Y) || !float.IsFinite(rect.Width)
                || !float.IsFinite(rect.Height) || rect.Width <= 0 || rect.Height <= 0) return false;
            var point = new Vector2(rect.X + (ndc.X + 1) * .5f * rect.Width,
                rect.Y + (ndc.Y + 1) * .5f * rect.Height);
            float half = spec.SizePixels * .5f;
            if (point.X - half < rect.X || point.X + half > rect.X + rect.Width
                || point.Y - half < rect.Y || point.Y + half > rect.Y + rect.Height) return false;
            placement = new(display.View, display.Render, display.FrameNumber, display.Sequence,
                anchor, point, spec.SizePixels);
            return true;
        }
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
