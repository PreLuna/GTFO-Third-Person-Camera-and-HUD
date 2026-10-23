using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using V=System.Numerics.Vector3;

namespace PreLuna.ThirdPerson.Runtime
{
    internal static class ConvertView
    {
        internal static V N(Vector3 v)=>new(v.x,v.y,v.z);
        internal static Vector3 U(V v)=>new(v.X,v.Y,v.Z);
    }
    internal sealed class UnityCameraWorld : ICameraWorld
    {
        readonly Il2CppReferenceArray<Collider> overlaps=new(256);
        readonly Il2CppStructArray<RaycastHit> hits=new(256);
        readonly HashSet<long> own=new();
        internal int Mask;
        internal long RayQueries,OverlapQueries,SweepQueries;
        internal long TotalQueries=>RayQueries+OverlapQueries+SweepQueries;
        internal bool BufferSaturated {get;private set;}
        internal void Bind(GameObject player)
        { own.Clear();foreach(var c in player.GetComponentsInChildren<Collider>(true))if(c!=null)own.Add(c.Pointer.ToInt64()); }
        bool Blocks(Collider c)=>c!=null&&!c.isTrigger&&c.enabled&&!own.Contains(c.Pointer.ToInt64());
        internal bool Ray(V origin,V direction,float distance,out V point)
        {
            RayQueries++;
            int count=Physics.RaycastNonAlloc(ConvertView.U(origin),ConvertView.U(direction),hits,distance,Mask,QueryTriggerInteraction.Ignore);
            point=origin+direction*distance;
            if(count>=hits.Length){BufferSaturated=true;point=origin;return true;}
            float nearest=distance;bool found=false;
            for(int i=0;i<count;i++){var h=hits[i];if(Blocks(h.collider)&&h.distance<nearest){nearest=h.distance;point=ConvertView.N(h.point);found=true;}}
            // Raycast does not report an origin inside a blocker. Fail closed for this view.
            if(Overlap(origin,.015f)){point=origin;return true;}
            return found;
        }
        public bool Overlap(V center,float radius)
        {
            OverlapQueries++;
            int count=Physics.OverlapSphereNonAlloc(ConvertView.U(center),radius,overlaps,Mask,QueryTriggerInteraction.Ignore);
            if(count>=overlaps.Length){BufferSaturated=true;return true;}
            for(int i=0;i<count;i++)if(Blocks(overlaps[i]))return true;
            return false;
        }
        public bool Sweep(V origin,V direction,float distance,float radius,out float hitDistance)
        {
            SweepQueries++;
            int count=Physics.SphereCastNonAlloc(ConvertView.U(origin),radius,ConvertView.U(direction),hits,distance,Mask,QueryTriggerInteraction.Ignore);
            hitDistance=distance;
            if(count>=hits.Length){BufferSaturated=true;hitDistance=0;return true;}
            bool found=false;
            for(int i=0;i<count;i++){var h=hits[i];if(Blocks(h.collider)&&h.distance<hitDistance){found=true;hitDistance=Math.Max(0,h.distance);}}
            return found;
        }
    }
}
