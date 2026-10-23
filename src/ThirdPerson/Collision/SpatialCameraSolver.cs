using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;

namespace PreLuna.ThirdPerson.Collision
{
    public readonly record struct SpatialSettings(float ReleaseDelay,float ReleaseDeadband,float RecoveryDelay,float RecoveryDistance)
    {
        public static SpatialSettings Default=>new(.18f,.04f,.28f,.6f);
        public SpatialSettings Sanitized()=>new(StyleSettings.Safe(ReleaseDelay,0,1,.18f),StyleSettings.Safe(ReleaseDeadband,.005f,.15f,.04f),
            StyleSettings.Safe(RecoveryDelay,.1f,1,.28f),StyleSettings.Safe(RecoveryDistance,.4f,1,.6f));
    }
    // Holds only environmental eligibility. The user's current aim intent stays in AimGesture.
    public sealed class EnvironmentRecovery
    {
        bool blocked;
        double safeSince=double.NaN,lastTime=double.NaN;
        public bool Blocked=>blocked;
        public void Reset(){blocked=false;safeSince=double.NaN;lastTime=double.NaN;}
        public bool Evaluate(bool safe,float distance,float minimum,double now,SpatialSettings options)
        {
            options=options.Sanitized();
            if(!double.IsFinite(now)){blocked=true;safeSince=double.NaN;return false;}
            if(double.IsFinite(lastTime)&&now<lastTime){blocked=true;safeSince=double.NaN;}
            lastTime=now;
            if(!safe){blocked=true;safeSince=double.NaN;return false;}
            if(!blocked)return true;
            if(!float.IsFinite(distance)||distance<Math.Max(options.RecoveryDistance,minimum+.12f)){safeSince=double.NaN;return false;}
            if(double.IsNaN(safeSince))safeSince=now;
            if(now-safeSince+1e-7<options.RecoveryDelay)return false;
            blocked=false;safeSince=double.NaN;return true;
        }
    }
    public sealed class SpatialCameraSolver
    {
        bool initialized;
        Vector3 previous;
        float distance,side,previousRadius,clearLimit;
        double clearSince=double.NaN,sideClearSince=double.NaN;
        public float CompressedShoulder=>side;
        public float Distance=>distance;
        public void Reset(){initialized=false;distance=0;side=0;clearSince=double.NaN;sideClearSince=double.NaN;}
        public DisplayView Solve(int frame,RigTarget rig,float dt,double now,float aspect,CameraSettings settings,SpatialSettings options,ICameraWorld world)
        {
            options=options.Sanitized();settings=settings.Sanitized();
            float radius=Math.Max(settings.Radius,ViewMath.NearRadius(settings.NearClip,rig.VerticalFov,aspect)+settings.Skin);
            DisplayView Fail(string reason){Reset();return new(frame,rig.Pivot,rig.Forward,radius,false,reason);}
            if(!ViewMath.Finite(rig.Pivot)||!ViewMath.Finite(rig.Forward)||!float.IsFinite(rig.Distance)||!float.IsFinite(rig.Shoulder)
                ||!float.IsFinite(aspect)||aspect<=0||!float.IsFinite(rig.VerticalFov)||!double.IsFinite(now))return Fail("invalid-view");
            if(world.Overlap(rig.Pivot,radius))return Fail("pivot-overlap");
            var forward=ViewMath.Unit(rig.Forward,Vector3.UnitZ);
            var right=Vector3.Cross(Vector3.UnitY,ViewMath.Unit(new(forward.X,0,forward.Z),Vector3.UnitZ));
            float allowedSide=Math.Abs(rig.Shoulder);
            if(allowedSide>.001f&&world.Sweep(rig.Pivot,right*Math.Sign(rig.Shoulder),allowedSide,radius,out float lateralHit))
                allowedSide=Math.Max(0,lateralHit-settings.Skin);
            float desiredSide=Math.Sign(rig.Shoulder)*Math.Min(Math.Abs(rig.Shoulder),allowedSide);
            bool compress=!initialized||Math.Abs(desiredSide)<Math.Abs(side)||Math.Sign(side)!=Math.Sign(desiredSide);
            if(compress){side=desiredSide;sideClearSince=double.NaN;}
            else if(Math.Abs(desiredSide-side)>.005f)
            {
                if(double.IsNaN(sideClearSince))sideClearSince=now;
                if(now-sideClearSince>=options.ReleaseDelay)side+=(desiredSide-side)*ViewMath.Blend(settings.ReleaseResponse,dt);
            }
            var path=-forward*rig.Distance+right*side;
            float length=path.Length();if(length<.001f)return Fail("empty-path");
            var direction=path/length;
            float limit=length;
            if(world.Sweep(rig.Pivot,direction,length,radius,out float hit))limit=Math.Max(0,hit-settings.Skin);
            if(limit<settings.MinimumDistance)return Fail("space-too-narrow");
            float next;
            if(!initialized||limit<distance){next=limit;clearSince=double.NaN;}
            else
            {
                if(double.IsNaN(clearSince)||Math.Abs(limit-clearLimit)>options.ReleaseDeadband){clearSince=now;clearLimit=limit;}
                next=distance;
                if(now-clearSince+1e-7>=options.ReleaseDelay&&limit-distance>options.ReleaseDeadband)
                    next=distance+(limit-distance)*ViewMath.Blend(settings.ReleaseResponse,dt);
            }
            var position=rig.Pivot+direction*next;
            if(initialized)
            {
                // A moving door can engulf the old camera. Do not sweep out of an overlap.
                float travelRadius=Math.Max(radius,previousRadius);
                if(world.Overlap(previous,travelRadius))return Fail("previous-overlap");
                var travel=position-previous;float travelLength=travel.Length();
                if(travelLength>.001f&&world.Sweep(previous,travel/travelLength,travelLength,travelRadius,out _))
                    return Fail("transition-path-blocked");
            }
            if(world.Overlap(position,radius))return Fail("final-overlap");
            initialized=true;distance=next;previous=position;previousRadius=radius;
            return new(frame,position,forward,radius,true,"third-person");
        }
    }
}
