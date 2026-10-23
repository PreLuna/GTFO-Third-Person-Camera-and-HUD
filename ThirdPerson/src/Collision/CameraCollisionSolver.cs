using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Collision
{
    public sealed class CameraCollisionSolver
    {
        bool initialized;
        float distance;
        Vector3 previous;
        public void Reset() { initialized=false; distance=0; }
        public DisplayView Solve(int frame,Vector3 pivot,Vector3 desired,Vector3 forward,float dt,float fov,float aspect,CameraSettings settings,ICameraWorld world)
        {
            float radius=Math.Max(settings.Radius,ViewMath.NearRadius(settings.NearClip,fov,aspect)+settings.Skin);
            DisplayView Fail(string why) { Reset();return new(frame,pivot,forward,radius,false,why); }
            if(!ViewMath.Finite(pivot)||!ViewMath.Finite(desired)||!ViewMath.Finite(forward)||!float.IsFinite(aspect)||aspect<=0||!float.IsFinite(fov)) return Fail("invalid-view");
            if(world.Overlap(pivot,radius)) return Fail("pivot-overlap");
            var path=desired-pivot;float length=path.Length();
            if(length<.001f) return Fail("empty-path");
            var direction=path/length;float limit=length;
            if(world.Sweep(pivot,direction,length,radius,out float hit)) limit=Math.Max(0,hit-settings.Skin);
            if(limit<settings.MinimumDistance) return Fail("space-too-narrow");
            // Retraction is immediate; only release is damped. New frame orientation
            // is always checked from the pivot, never interpolated through a corner.
            float next=!initialized?limit:Math.Min(limit,distance+(limit-distance)*ViewMath.Blend(settings.ReleaseResponse,dt));
            Vector3 position=pivot+direction*next;
            if(initialized)
            {
                var travel=position-previous;float travelLength=travel.Length();
                if(travelLength>.001f&&world.Sweep(previous,travel/travelLength,travelLength,radius,out _))
                {
                    // Teleports/rapid turns use the independently verified pivot path.
                    // No temporal lerp through the blocked segment is allowed.
                    next=limit;position=pivot+direction*next;
                }
            }
            if(world.Overlap(position,radius)) return Fail("final-overlap");
            initialized=true;distance=next;previous=position;
            return new(frame,position,ViewMath.Unit(forward,Vector3.UnitZ),radius,true,"third-person");
        }
    }
}
