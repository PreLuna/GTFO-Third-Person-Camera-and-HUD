using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPerson.Core
{
    // Projects a world point through the display camera snapshot published for the
    // current third-person render. GUI coordinates use a top-left origin, while
    // CameraSnapshot.Viewport follows Unity's bottom-left pixelRect.
    public static class PointerProjection
    {
        public static bool TryProject(in CameraSnapshot camera,in Vector3 world,float screenHeight,out Vector2 screen)
            =>TryProject(camera,world,screenHeight,false,out screen);

        public static bool TryProject(in CameraSnapshot camera,in Vector3 world,float screenHeight,bool clampToViewport,out Vector2 screen)
        {
            screen=default;
            if(!Finite(world)||!float.IsFinite(screenHeight)||screenHeight<=0)return false;
            var rect=camera.Viewport;
            if(!float.IsFinite(rect.X)||!float.IsFinite(rect.Y)||!float.IsFinite(rect.Width)
                ||!float.IsFinite(rect.Height)||rect.Width<=0||rect.Height<=0)return false;
            var view=Vector4.Transform(new Vector4(world,1),camera.WorldToView);
            var clip=Vector4.Transform(view,camera.Projection);
            if(!float.IsFinite(clip.W)||clip.W<=.0001f)return false;
            float x=clip.X/clip.W,y=clip.Y/clip.W;
            if(!float.IsFinite(x)||!float.IsFinite(y))return false;
            if(!clampToViewport&&(x<-1||x>1||y<-1||y>1))return false;
            float pixelX=rect.X+(x+1)*.5f*rect.Width;
            float pixelYBottom=rect.Y+(y+1)*.5f*rect.Height;
            if(clampToViewport)
            {
                pixelX=Math.Clamp(pixelX,rect.X,rect.X+rect.Width);
                pixelYBottom=Math.Clamp(pixelYBottom,rect.Y,rect.Y+rect.Height);
            }
            float pixelY=screenHeight-pixelYBottom;
            if(!float.IsFinite(pixelX)||!float.IsFinite(pixelY))return false;
            screen=new(pixelX,pixelY);
            return true;
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    }
}
