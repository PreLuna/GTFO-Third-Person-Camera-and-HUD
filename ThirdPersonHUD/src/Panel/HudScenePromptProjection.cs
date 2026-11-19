using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // 1b prompt geometry. PixelViewport uses the provider's bottom-left origin;
    // the drawing backend converts to GUI top-left at its boundary.
    public static class HudPromptProjection
    {
        public static bool TryProject(in ThirdPersonDisplayFrame display,in HudWorldPoint world,out Vector2 point)
        {
            point=default;
            var worldVector=world.ToVector();
            if(!world.IsValid)return false;
            var rect=display.Camera.Viewport;
            if(!Finite(rect.X)||!Finite(rect.Y)||!Finite(rect.Width)||!Finite(rect.Height)||rect.Width<=0||rect.Height<=0)return false;
            var view=Vector4.Transform(new Vector4(worldVector,1),display.Camera.WorldToView);
            var clip=Vector4.Transform(view,display.Camera.Projection);
            if(!Finite(clip.X)||!Finite(clip.Y)||!Finite(clip.Z)||!Finite(clip.W)||clip.W<=.0001f)return false;
            float x=clip.X/clip.W,y=clip.Y/clip.W,z=clip.Z/clip.W;
            if(!Finite(x)||!Finite(y)||!Finite(z)||z<-1||z>1||Math.Abs(x)>1||Math.Abs(y)>1)return false;
            point=new(rect.X+(x+1)*.5f*rect.Width,rect.Y+(y+1)*.5f*rect.Height);
            return Finite(point.X)&&Finite(point.Y);
        }

        public static bool TryPlace(in ThirdPersonDisplayFrame display,in HudScenePrompt prompt,float scale,
            in PixelViewport panel,out PixelViewport bounds)
        {
            bounds=default;
            if(!prompt.Active||!Finite(scale)||scale<=0||!TryProject(display,prompt.Position,out var anchor))return false;
            var viewport=display.Camera.Viewport;
            float margin=Math.Max(8f,12f*scale),width=Math.Min(420f*scale,viewport.Width-2*margin),height=Math.Max(22f,30f*scale);
            if(width<120f||height<=0||viewport.Width<2*margin+width||viewport.Height<2*margin+height)return false;
            float minX=viewport.X+margin,maxX=viewport.X+viewport.Width-margin-width;
            float minY=viewport.Y+margin,maxY=viewport.Y+viewport.Height-margin-height;
            float x=Math.Clamp(anchor.X-width*.5f,minX,maxX);
            float below=anchor.Y-height-Math.Max(8f,14f*scale),above=anchor.Y+Math.Max(8f,14f*scale);
            var candidates=new[]{
                new PixelViewport(x,Math.Clamp(below,minY,maxY),width,height),
                new PixelViewport(x,Math.Clamp(above,minY,maxY),width,height),
                new PixelViewport(Math.Clamp(panel.X-width-Math.Max(8f,14f*scale),minX,maxX),Math.Clamp(anchor.Y-height*.5f,minY,maxY),width,height),
                new PixelViewport(Math.Clamp(panel.X+panel.Width+Math.Max(8f,14f*scale),minX,maxX),Math.Clamp(anchor.Y-height*.5f,minY,maxY),width,height)
            };
            foreach(var candidate in candidates)
            {
                if(!HudSceneDrawing.Overlap(candidate,panel)){bounds=candidate;return true;}
            }
            return false;
        }
        static bool Finite(float value)=>float.IsFinite(value);
    }
}
