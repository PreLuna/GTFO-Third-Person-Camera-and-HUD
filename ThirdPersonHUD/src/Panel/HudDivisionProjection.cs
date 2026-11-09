using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public static class HudDivisionProjection
    {
        public const float Height=154;
        public static bool Safe(PixelViewport b,PixelViewport v,HudPanelLayoutOptions o,float scale)
        {
            float r=o.CrosshairRadius*scale,cx=v.X+v.Width*.5f,cy=v.Y+v.Height*.5f;
            return b.X>=v.X+o.ScreenMargin-.01f&&b.Y>=v.Y+o.ScreenMargin-.01f&&b.X+b.Width<=v.X+v.Width-o.ScreenMargin+.01f&&b.Y+b.Height<=v.Y+v.Height-o.ScreenMargin+.01f
                &&(r<=0||b.X>=cx+r||b.X+b.Width<=cx-r||b.Y>=cy+r||b.Y+b.Height<=cy-r);
        }
        public static bool TryPlace(in ThirdPersonDisplayFrame display,in HudPanelLayoutOptions options,float side,out HudPlacement placement)
        {
            placement=default;var v=display.Camera.Viewport;var o=options.Division.Sanitized();
            var world=display.BodyPose.Position+Vector3.UnitY*(o.FollowBody?o.WaistHeight:1);
            var clip=Vector4.Transform(Vector4.Transform(new Vector4(world,1),display.Camera.WorldToView),display.Camera.Projection);
            if(!float.IsFinite(clip.X)||!float.IsFinite(clip.Y)||!float.IsFinite(clip.Z)||!float.IsFinite(clip.W)||clip.W<=.0001f
                ||clip.Z/clip.W < -1||clip.Z/clip.W > 1)return false;
            float bodyX=(clip.X/clip.W+1)*.5f;
            bool centered=o.Anchor==HudDivisionAnchor.Centered||o.Anchor==HudDivisionAnchor.Auto&&Math.Abs(bodyX-.5f)<=.035f;
            float scale=o.Scale*Math.Clamp(v.Height/1080f,.75f,2);
            scale=Math.Min(scale,Math.Min((v.Width-2*options.ScreenMargin)/o.Width,(v.Height-2*options.ScreenMargin)/Height));
            if(!float.IsFinite(scale)||scale<.55f)return false;
            float w=o.Width*scale,h=Height*scale,reference=centered?o.CenteredX:o.ShoulderX;
            // Anchor to the committed viewport. Changing HUD composition never changes camera/body poses.
            float x=v.X+v.Width*reference/1920f;
            if(!centered&&o.Mirror&&bodyX>.535f)x=v.X+v.Width*(1-reference/1920f)-w;
            if(o.FollowBody)x+=Math.Clamp((bodyX-(centered?.5f:bodyX>.535f?.58f:.42f))*v.Width,-96*scale,96*scale);
            x+=o.OffsetX*scale;
            // Top remains a useful trim around the 686 reference in body-follow mode.
            float y=o.FollowBody?v.Y+(clip.Y/clip.W+1)*.5f*v.Height-h-(o.Top-686+o.OffsetY)*scale
                :v.Y+v.Height*(1-o.Top/1080f)-h-o.OffsetY*scale;
            float minX=v.X+options.ScreenMargin,maxX=v.X+v.Width-options.ScreenMargin-w;
            float minY=v.Y+options.ScreenMargin,maxY=v.Y+v.Height-options.ScreenMargin-h;
            x=Math.Clamp(x,minX,maxX);y=Math.Clamp(y,minY,maxY);
            float cx=v.X+v.Width*.5f,cy=v.Y+v.Height*.5f,r=options.CrosshairRadius*scale;
            if(r>0&&x<cx+r&&x+w>cx-r&&y<cy+r&&y+h>cy-r)
            {
                if(cy-r-h>=minY)y=cy-r-h;
                else if(cy+r<=maxY)y=cy+r;
                else return false;
            }
            var panel=new HudPanelPlacement(new(x,y,w,h),scale,options.Expanded,display.View.Mode==ThirdPersonMode.ShoulderAim,side)
                {Viewport=v,Plane=HudPlaneTransform.Create(w,h,o.TiltDegrees,o.TileSlant*scale)};
            placement=new(display.View,display.Render,display.FrameNumber,display.Sequence,world,new(x+w*.5f,y+h*.5f),w){Panel=panel};
            return true;
        }
    }
}
