using System;
using System.Numerics;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // Bounded affine approximation of a tilted HUD plane. Every core mark uses the same transform.
    // The four corners stay inside the logical placement bounds; scene/team overlays stay screen-aligned.
    public readonly record struct HudPlaneTransform(float A,float B,float C,float D,float X,float Y)
    {
        public static HudPlaneTransform Identity=>new(1,0,0,1,0,0);
        public static HudPlaneTransform Create(float width,float height,float degrees,float slant)
        {
            if(width<=0||height<=0)return Identity;
            float tilt=MathF.Tan(degrees*MathF.PI/180),edge=Math.Clamp(slant,0,Math.Min(8,width*.04f));
            return new(1-edge/width,-edge/height,tilt,1-Math.Abs(tilt)*width/height,edge,tilt<0?-tilt*width:0);
        }
        public Vector2 Apply(Vector2 point)=>new(A*point.X+B*point.Y+X,C*point.X+D*point.Y+Y);
    }
}
