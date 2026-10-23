using System;
using System.Numerics;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Core
{
    public interface IAimWorld
    {
        bool Trace(Vector3 origin,Vector3 direction,float distance,out Vector3 point);
    }
    public readonly record struct AimFrame(int Frame,int Generation,long Item,Vector3 Eye,Vector3 Camera,Vector3 Forward,bool ThirdPerson);
    public readonly record struct ShotAim(Vector3 Origin,Vector3 Target,Vector3 Direction,Vector3 Impact,bool Blocked,bool Valid,string Reason);
    public sealed class ShotRayGate
    {
        readonly HashSet<long> consumed=new();
        readonly long camera;
        readonly Vector3 origin;
        public ShotRayGate(long camera,Vector3 origin){this.camera=camera;this.origin=origin;}
        public bool Accept(long align,long data,Vector3 actualOrigin)=>camera!=0&&align==camera&&data!=0
            &&ViewMath.Finite(actualOrigin)&&Vector3.DistanceSquared(actualOrigin,origin)<=.000001f&&consumed.Add(data);
    }
    public static class AimSolver
    {
        // The render snapshot is never reused after a weapon/context/frame change.
        public static ShotAim Solve(AimFrame view,int frame,int generation,long item,Vector3 eye,Vector3 forward,float range,IAimWorld world)
        {
            forward=ViewMath.Unit(forward,Vector3.UnitZ);
            ShotAim Native(string why)=>new(eye,eye+forward*100,forward,eye+forward*100,false,false,why);
            if(!view.ThirdPerson||view.Frame!=frame||view.Generation!=generation||view.Item!=item)return Native("stale-or-first-person");
            if(!ViewMath.Finite(eye)||!ViewMath.Finite(view.Eye)||Vector3.DistanceSquared(view.Eye,eye)>.000001f||!ViewMath.Finite(view.Camera)||!ViewMath.Finite(view.Forward)||view.Forward.LengthSquared()<.00001f||!float.IsFinite(range)||range<1)return Native("invalid-sample");
            var direction=ViewMath.Unit(view.Forward,forward);
            var target=view.Camera+direction*range;
            if(world.Trace(view.Camera,direction,range,out var visible))target=visible;
            var offset=target-eye;
            if(!ViewMath.Finite(target)||Vector3.Dot(offset,forward)<.05f||offset.LengthSquared()<.01f)return Native("near-or-behind-eye");
            var shot=Vector3.Normalize(offset);
            bool blocked=world.Trace(eye,shot,offset.Length(),out var impact)&&Vector3.DistanceSquared(impact,target)>.0025f;
            // Keep the intended direction: original ballistics, including penetration, resolves obstruction.
            return new(eye,target,shot,blocked?impact:target,blocked,true,blocked?"origin-path-blocked":"clear");
        }
    }
}
