using System;
using System.Numerics;

namespace PreLuna.ThirdPerson.Core
{
    // Position-only analytical IK. Does not evaluate an Animator or a native IK solver.
    public static class TwoBonePoseSolver
    {
        public static bool Solve(Vector3 shoulder,Vector3 elbow,Vector3 wrist,Vector3 target,
            out Vector3 nextElbow,out Vector3 nextWrist,out bool clamped)
        {
            nextElbow=elbow;nextWrist=wrist;clamped=false;
            if(!ViewMath.Finite(shoulder)||!ViewMath.Finite(elbow)||!ViewMath.Finite(wrist)||!ViewMath.Finite(target))return false;
            float a=Vector3.Distance(shoulder,elbow),b=Vector3.Distance(elbow,wrist);
            if(a<.001f||b<.001f)return false;
            var delta=target-shoulder;float distance=delta.Length();
            var direction=distance>.00001f?delta/distance:ViewMath.Unit(wrist-shoulder,Vector3.UnitZ);
            float low=Math.Abs(a-b)+.0001f,high=a+b-.0001f;
            float reach=Math.Clamp(distance,low,high);clamped=Math.Abs(reach-distance)>.0001f;
            var pole=elbow-shoulder;pole-=direction*Vector3.Dot(pole,direction);
            if(pole.LengthSquared()<.000001f)
            {
                var normal=Vector3.Cross(elbow-shoulder,wrist-elbow);
                pole=Vector3.Cross(direction,normal);
                if(pole.LengthSquared()<.000001f)pole=Vector3.Cross(direction,Math.Abs(direction.Y)<.9f?Vector3.UnitY:Vector3.UnitX);
            }
            pole=Vector3.Normalize(pole);
            float along=(a*a-b*b+reach*reach)/(2*reach);
            float height=MathF.Sqrt(Math.Max(0,a*a-along*along));
            nextElbow=shoulder+direction*along+pole*height;nextWrist=shoulder+direction*reach;
            return ViewMath.Finite(nextElbow)&&ViewMath.Finite(nextWrist);
        }
    }
}
