using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // Full-viewport placement for the two archived wireframe layouts. The classic layouts use
    // screen-space design units (1920x1080 baseline) and never rotate or tilt the whole HUD.
    public static class HudClassicProjection
    {
        // Draft extents around the radial center, in 1920x1080 design pixels.
        const float MissionLeft = 508, MissionTop = 450, TeamRight = 516, EquipmentBottom = 320;
        const float MinimumDesignWidth = 1100;

        public static bool TryPlace(in ThirdPersonDisplayFrame display, in HudPanelLayoutOptions options, float side, out HudPlacement placement)
        {
            placement = default;
            var v = display.Camera.Viewport;
            var o = options.Classic.Sanitized();
            if (!Finite(v.X) || !Finite(v.Y) || !Finite(v.Width) || !Finite(v.Height) || v.Width <= 0 || v.Height <= 0)
                return false;
            float scale = o.Scale * Math.Clamp(v.Height / 1080f, .75f, 2);
            // Keep the four panels from overlapping on unusually narrow viewports.
            scale = Math.Min(scale, v.Width / MinimumDesignWidth);
            if (!float.IsFinite(scale) || scale < .55f) return false;
            bool aim = display.View.Mode == ThirdPersonMode.ShoulderAim;
            var panel = new HudPanelPlacement(new(v.X, v.Y, v.Width, v.Height), scale, options.Expanded, aim, side)
            { Viewport = v, Plane = HudPlaneTransform.Identity };
            if (options.Style == HudLayoutStyle.FourCorner)
            {
                var camera = new Vector2(v.X + v.Width * .5f, v.Y + v.Height * .5f);
                placement = new(display.View, display.Render, display.FrameNumber, display.Sequence,
                    display.BodyPose.Position + Vector3.UnitY * o.WaistHeight, camera, v.Width) { Panel = panel };
                return true;
            }
            if (options.Style != HudLayoutStyle.Radial) return false;

            float cx, cy;
            var world = display.BodyPose.Position + Vector3.UnitY * o.WaistHeight;
            if (o.FollowBody)
            {
                var clip = Vector4.Transform(Vector4.Transform(new Vector4(world, 1), display.Camera.WorldToView), display.Camera.Projection);
                if (!Finite(clip.X) || !Finite(clip.Y) || !Finite(clip.Z) || !Finite(clip.W) || clip.W <= .0001f
                    || clip.Z / clip.W < -1 || clip.Z / clip.W > 1) return false;
                cx = v.X + (clip.X / clip.W + 1) * .5f * v.Width + o.RadialOffsetX * scale;
                // Draft offsets are top-down; convert the +Y offset to bottom-left viewport space.
                cy = v.Y + (clip.Y / clip.W + 1) * .5f * v.Height - o.RadialOffsetY * scale;
            }
            else
            {
                cx = v.X + v.Width * .5f + o.RadialOffsetX * scale;
                cy = v.Y + v.Height * .5f - o.RadialOffsetY * scale;
            }
            float designX = (cx - v.X) / scale;
            float designY = (v.Y + v.Height - cy) / scale;
            ClampCenter(v, o, scale, ref designX, ref designY);
            cx = v.X + designX * scale;
            cy = v.Y + v.Height - designY * scale;
            if (!float.IsFinite(cx) || !float.IsFinite(cy)) return false;
            var origin = new Vector2(cx, cy);
            panel = panel with { StyleOrigin = origin };
            placement = new(display.View, display.Render, display.FrameNumber, display.Sequence, world, origin, v.Width)
            { Panel = panel };
            return true;
        }

        public static bool Safe(Vector2 center, PixelViewport v, in HudPanelLayoutOptions options, float scale)
        {
            var o = options.Classic.Sanitized();
            if (!Finite(center.X) || !Finite(center.Y) || !Finite(scale) || scale <= 0) return false;
            float x = (center.X - v.X) / scale, y = (v.Y + v.Height - center.Y) / scale;
            Bounds(v, o, scale, out float minX, out float maxX, out float minY, out float maxY);
            return x >= minX - .01f && x <= maxX + .01f && y >= minY - .01f && y <= maxY + .01f;
        }

        static void ClampCenter(PixelViewport v, in HudClassicLayout o, float scale, ref float x, ref float y)
        {
            Bounds(v, o, scale, out float minX, out float maxX, out float minY, out float maxY);
            x = Math.Clamp(x, minX, maxX);
            y = Math.Clamp(y, minY, maxY);
        }

        static void Bounds(PixelViewport v, in HudClassicLayout o, float scale,
            out float minX, out float maxX, out float minY, out float maxY)
        {
            float vw = v.Width / scale, vh = v.Height / scale;
            minX = o.Margin + MissionLeft; maxX = vw - o.Margin - TeamRight;
            minY = o.Margin + MissionTop; maxY = vh - o.Margin - EquipmentBottom;
            if (maxX < minX) { float middle = (minX + maxX) * .5f; minX = maxX = middle; }
            if (maxY < minY) { float middle = (minY + maxY) * .5f; minY = maxY = middle; }
        }

        static bool Finite(float value) => float.IsFinite(value);
    }
}
