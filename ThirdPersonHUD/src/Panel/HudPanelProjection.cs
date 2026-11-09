using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // Pixel bounds use the provider's bottom-left origin. Drawing backends convert only at their boundary.
    public readonly record struct HudPanelPlacement(PixelViewport Bounds, float Scale, bool Expanded, bool Aiming, float Side)
    { public PixelViewport Viewport { get; init; }
        public HudPlaneTransform Plane { get; init; }
        // Character-anchored radial center in absolute bottom-left viewport pixels. Full-viewport
        // classic layouts leave it at zero; only Radial reads it.
        public System.Numerics.Vector2 StyleOrigin { get; init; }
        public bool IsValid => Bounds.Width > 0 && Bounds.Height > 0 && float.IsFinite(Scale) && Scale >= .55f; }

    public sealed class HudPanelMotion
    {
        ThirdPersonViewState identity;
        double at = double.NaN;
        float side;
        Vector2 divisionOrigin, radialOrigin;
        HudPanelLayoutOptions previousOptions;
        bool divisionValid, radialValid;
        public void Reset() { identity = default; at = double.NaN; side = 0; divisionValid=false; radialValid=false; }
        public bool TryPlace(in ThirdPersonDisplayFrame display, in HudAnchorSpec anchor, out HudPlacement placement)
        {
            var options = anchor.Panel.Sanitized();
            var right = Vector3.Transform(Vector3.UnitX, display.Camera.Pose.Rotation);
            float shoulder = Vector3.Dot(display.Camera.Pose.Position-display.BodyPose.Position,right);
            float target = options.MirrorWithShoulder ? (Math.Abs(shoulder) < .05f ? (side == 0 ? options.FixedSide : MathF.Sign(side)) : MathF.Sign(shoulder)) : options.FixedSide;
            bool reset = !double.IsFinite(at) || display.CapturedAtSeconds < at || display.CapturedAtSeconds-at > .5
                || identity.Context != display.View.Context || identity.Binding != display.View.Binding
                || identity.EligibilityEpoch != display.View.EligibilityEpoch;
            float dt = reset ? 0 : (float)(display.CapturedAtSeconds-at);
            side = reset || options.FollowSeconds <= 0 ? target : side+(target-side)*(1-MathF.Exp(-dt/options.FollowSeconds));
            at=display.CapturedAtSeconds; identity=display.View;
            if(!HudPanelProjection.TryPlace(display,options,side,out placement)){divisionValid=false;radialValid=false;return false;}
            if(options.Style==HudLayoutStyle.DivisionV3)
            {
                radialValid=false;
                var b=placement.Panel.Bounds;var next=new Vector2(b.X,b.Y);
                if(!reset&&divisionValid&&options==previousOptions&&options.Division.FollowSeconds>0)
                {
                    var smooth=Vector2.Lerp(divisionOrigin,next,1-MathF.Exp(-dt/options.Division.FollowSeconds));
                    var candidate=new PixelViewport(smooth.X,smooth.Y,b.Width,b.Height);
                    if(HudDivisionProjection.Safe(candidate,display.Camera.Viewport,options,placement.Panel.Scale))next=smooth;
                }
                divisionOrigin=next;divisionValid=true;
                placement=placement with{Panel=placement.Panel with{Bounds=new(next.X,next.Y,b.Width,b.Height)},PixelCenter=new(next.X+b.Width*.5f,next.Y+b.Height*.5f)};
            }
            else if(options.Style==HudLayoutStyle.Radial)
            {
                divisionValid=false;
                var next=placement.Panel.StyleOrigin;
                if(!reset&&radialValid&&options==previousOptions&&options.Classic.FollowSeconds>0)
                {
                    var smooth=Vector2.Lerp(radialOrigin,next,1-MathF.Exp(-dt/options.Classic.FollowSeconds));
                    if(HudClassicProjection.Safe(smooth,display.Camera.Viewport,options,placement.Panel.Scale))next=smooth;
                }
                radialOrigin=next;radialValid=true;
                placement=placement with{Panel=placement.Panel with{StyleOrigin=next},PixelCenter=next};
            }
            else {divisionValid=false;radialValid=false;}
            previousOptions=options;
            return true;
        }
    }
    public static class HudPanelProjection
    {
        public const float CompactHeight = 356, ExpandedHeight = 568;
        public const float PersonalMaximumHeight = 480;
        public static bool TryPlace(in ThirdPersonDisplayFrame display, in HudPanelLayoutOptions raw, float sideBlend, out HudPlacement placement)
        {
            placement=default;
            var o=raw.Sanitized(); var rect=display.Camera.Viewport;
            if(!o.Enabled || !float.IsFinite(sideBlend) || !Finite(rect.X) || !Finite(rect.Y) || !Finite(rect.Width) || !Finite(rect.Height)
                || rect.Width<=0 || rect.Height<=0) return false;
            if(o.Style==HudLayoutStyle.DivisionV3)return HudDivisionProjection.TryPlace(display,o,sideBlend,out placement);
            if(o.Style is HudLayoutStyle.FourCorner or HudLayoutStyle.Radial)return HudClassicProjection.TryPlace(display,o,sideBlend,out placement);
            sideBlend=Math.Clamp(sideBlend,-1,1);
            var right=Vector3.Transform(Vector3.UnitX,display.Camera.Pose.Rotation);
            var world=display.BodyPose.Position+Vector3.UnitY*o.AnchorHeight+right*(o.AnchorSide*sideBlend);
            var clip=Vector4.Transform(Vector4.Transform(new Vector4(world,1),display.Camera.WorldToView),display.Camera.Projection);
            if(!Finite(clip.X)||!Finite(clip.Y)||!Finite(clip.Z)||!Finite(clip.W)||clip.W<=.0001f) return false;
            var ndc=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
            // An off-screen anchor may be clamped, but a point behind the camera/clip depth may not.
            if(!Finite(ndc.X)||!Finite(ndc.Y)||!Finite(ndc.Z)||ndc.Z < -1 || ndc.Z > 1) return false;
            bool aim=display.View.Mode==ThirdPersonMode.ShoulderAim;
            bool expanded=o.Expanded;
            float scale=o.Scale*Math.Clamp(rect.Height/1080f,.75f,2f)*(aim?o.AimScale:1);
            float baseHeight=o.TeamPage?HudTeamDrawing.ExpandedHeight:o.Streamlined?PersonalMaximumHeight:expanded?ExpandedHeight:CompactHeight;
            if(o.Team&&!o.TeamPage)baseHeight+=expanded?HudTeamDrawing.ExpandedHeight:HudTeamDrawing.CompactHeight;
            scale=Math.Min(scale,Math.Min((rect.Width-2*o.ScreenMargin)/o.Width,(rect.Height-2*o.ScreenMargin)/baseHeight));
            if(!Finite(scale)||scale<.55f) return false;
            float w=o.Width*scale,h=baseHeight*scale;
            var offset=Vector2.Lerp(o.LeftOffset,o.RightOffset,(sideBlend+1)*.5f);
            if(aim) offset+=new Vector2(o.AimOffset.X*sideBlend,o.AimOffset.Y);
            var point=new Vector2(rect.X+(ndc.X+1)*.5f*rect.Width,rect.Y+(ndc.Y+1)*.5f*rect.Height)+offset*scale;
            float minX=rect.X+o.ScreenMargin,maxX=rect.X+rect.Width-o.ScreenMargin-w;
            float minY=rect.Y+o.ScreenMargin,maxY=rect.Y+rect.Height-o.ScreenMargin-h;
            float x=Math.Clamp(point.X-w*.5f,minX,maxX),y=Math.Clamp(point.Y-h*.5f,minY,maxY);
            float cx=rect.X+rect.Width*.5f,cy=rect.Y+rect.Height*.5f,r=o.CrosshairRadius*scale;
            if(r>0 && x<cx+r && x+w>cx-r && y<cy+r && y+h>cy-r)
            {
                float preferred=sideBlend>=0?cx+r:cx-r-w, other=sideBlend>=0?cx-r-w:cx+r;
                if(preferred>=minX && preferred<=maxX) x=preferred;
                else if(other>=minX && other<=maxX) x=other;
                else if(cy-r-h>=minY) y=cy-r-h;
                else if(cy+r<=maxY) y=cy+r;
                else return false;
            }
            var panel=new HudPanelPlacement(new(x,y,w,h),scale,expanded,aim,sideBlend);
            placement=new(display.View,display.Render,display.FrameNumber,display.Sequence,world,new(x+w*.5f,y+h*.5f),w){Panel=panel};
            return true;
        }
        static bool Finite(float v)=>float.IsFinite(v);
    }
}
