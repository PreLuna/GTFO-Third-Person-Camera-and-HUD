using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;

namespace PreLuna.ThirdPerson.Collision
{
    public readonly record struct SpatialRefinementSettings(bool Enabled,float RetractionDistance)
    {
        public static SpatialRefinementSettings Default=>new(true,.6f);
        public SpatialRefinementSettings Sanitized()=>new(Enabled,StyleSettings.Safe(RetractionDistance,.45f,1,.6f));
    }
    public readonly record struct SpatialFrame(int Frame,bool Refined,bool Safe,float Radius,float Distance,float Shoulder,
        int Queries,int Retractions,bool DistanceConstrained,bool ShoulderConstrained,string Reason);
    public sealed partial class SpatialCameraSolver
    {
        bool refinedMode,distanceConstrained,shoulderConstrained;
        int refinedFrame=-1,queries,retractions;
        double refinedTime=double.NaN;
        Vector3 previousPivot;
        float sideClearLimit;
        bool invalidQuery;
        public SpatialFrame Current {get;private set;}
        void ResetRefinement(){refinedMode=distanceConstrained=shoulderConstrained=false;refinedFrame=-1;refinedTime=double.NaN;queries=retractions=0;Current=default;}
        readonly record struct Saved(bool Initialized,Vector3 Previous,float Distance,float Side,float Radius,float Limit,double Clear,double SideClear,
            bool Mode,bool DistanceBlocked,bool SideBlocked,int Frame,double Time,Vector3 Pivot,int Queries,int Retractions,float SideLimit,SpatialFrame Current);
        Saved Save()=>new(initialized,previous,distance,side,previousRadius,clearLimit,clearSince,sideClearSince,refinedMode,distanceConstrained,
            shoulderConstrained,refinedFrame,refinedTime,previousPivot,queries,retractions,sideClearLimit,Current);
        void Restore(Saved s)
        {
            initialized=s.Initialized;previous=s.Previous;distance=s.Distance;side=s.Side;previousRadius=s.Radius;clearLimit=s.Limit;
            clearSince=s.Clear;sideClearSince=s.SideClear;refinedMode=s.Mode;distanceConstrained=s.DistanceBlocked;shoulderConstrained=s.SideBlocked;
            refinedFrame=s.Frame;refinedTime=s.Time;previousPivot=s.Pivot;queries=s.Queries;retractions=s.Retractions;sideClearLimit=s.SideLimit;Current=s.Current;
        }
        public DisplayView Preview(int frame,RigTarget rig,double now,float aspect,CameraSettings settings,SpatialSettings options,ICameraWorld world,
            SpatialRefinementSettings refinement,bool fresh=false)
        {
            var saved=Save();
            try{if(fresh)Reset();return Solve(frame,rig,0,now,aspect,settings,options,world,refinement);}
            finally{Restore(saved);}
        }
        public DisplayView Solve(int frame,RigTarget rig,float dt,double now,float aspect,CameraSettings settings,SpatialSettings options,ICameraWorld world,
            SpatialRefinementSettings? refinement=null)
        {
            bool modern=refinement?.Enabled==true;
            if(initialized&&modern!=refinedMode)Reset();refinedMode=modern;
            try
            {
                if(modern)return SolveRefined(frame,rig,dt,now,aspect,settings,options,world,refinement.Value.Sanitized());
                var result=SolveLegacy(frame,rig,dt,now,aspect,settings,options,world);
                Current=new(frame,false,result.IsThirdPerson,result.Radius,distance,side,0,0,false,false,result.Reason);return result;
            }
            catch{Reset();throw;}
        }
        bool Occupied(ICameraWorld world,Vector3 p,float radius){queries++;return world.Overlap(p,radius);}
        bool Hit(ICameraWorld world,Vector3 p,Vector3 direction,float length,float radius,out float hit)
        {
            queries++;bool blocked=world.Sweep(p,direction,length,radius,out hit);
            if(blocked&&(!float.IsFinite(hit)||hit<0||hit>length+.001f)){hit=0;invalidQuery=true;}
            return blocked;
        }
        DisplayView RefinedFail(int frame,RigTarget rig,float radius,string reason)
        {
            int q=queries,r=retractions;Reset();
            Current=new(frame,true,false,radius,0,0,q,r,false,false,reason);
            return new(frame,rig.Pivot,rig.Forward,radius,false,reason);
        }
        DisplayView SolveRefined(int frame,RigTarget rig,float dt,double now,float aspect,CameraSettings settings,SpatialSettings options,ICameraWorld world,SpatialRefinementSettings refinement)
        {
            settings=settings.Sanitized();options=options.Sanitized();queries=retractions=0;invalidQuery=false;
            if(!ViewMath.Finite(rig.Pivot)||!ViewMath.Finite(rig.Forward)||rig.Forward.LengthSquared()<.00001f
                ||!float.IsFinite(rig.Distance)||rig.Distance<=0||!float.IsFinite(rig.Shoulder)||!float.IsFinite(aspect)||aspect<=0
                ||!float.IsFinite(rig.VerticalFov)||rig.VerticalFov<=0||rig.VerticalFov>=170||!double.IsFinite(now))return RefinedFail(frame,rig,settings.Radius,"invalid-view");
            if(initialized&&(!float.IsFinite(dt)||dt<0||dt>.2f||frame<refinedFrame||frame>refinedFrame+1||now<refinedTime
                ||Vector3.DistanceSquared(rig.Pivot,previousPivot)>9)){Reset();refinedMode=true;}
            float elapsed=frame==refinedFrame?0:float.IsFinite(dt)?Math.Clamp(dt,0,.2f):0;
            float radius=Math.Max(settings.Radius,ViewMath.NearRadius(settings.NearClip,rig.VerticalFov,aspect)+settings.Skin);
            var eye=rig.LogicalEye??rig.PivotOrigin;
            bool pivotCompressed=false;
            if(eye is Vector3 origin)
            {
                if(!ViewMath.Finite(origin))return RefinedFail(frame,rig,radius,"invalid-pivot-origin");
                if(Occupied(world,origin,radius))return RefinedFail(frame,rig,radius,"pivot-origin-overlap");
                var shift=rig.Pivot-origin;float len=shift.Length();
                if(len>.001f&&Hit(world,origin,shift/len,len,radius,out float pivotHit))
                {
                    if(invalidQuery)return RefinedFail(frame,rig,radius,"invalid-query");
                    rig=rig with{Pivot=origin+shift/len*Math.Max(0,pivotHit-settings.Skin)};pivotCompressed=true;
                }
            }
            if(Occupied(world,rig.Pivot,radius))return RefinedFail(frame,rig,radius,"pivot-overlap");
            var forward=Vector3.Normalize(rig.Forward);
            var right=Vector3.Cross(Vector3.UnitY,ViewMath.Unit(new(forward.X,0,forward.Z),Vector3.UnitZ));
            float wanted=Math.Abs(rig.Shoulder),allowed=wanted;
            float lateral=wanted;
            bool sideHit=wanted>.001f&&Hit(world,rig.Pivot,right*Math.Sign(rig.Shoulder),wanted,radius,out lateral);
            if(invalidQuery)return RefinedFail(frame,rig,radius,"invalid-query");
            if(sideHit)allowed=Math.Max(0,lateral-settings.Skin);
            float requested=Math.Sign(rig.Shoulder)*Math.Min(wanted,allowed);
            bool sideBlocked=sideHit&&allowed<wanted-.001f;
            if(!initialized||Math.Sign(side)!=Math.Sign(requested)||Math.Abs(requested)<Math.Abs(side))
            {side=requested;sideClearSince=double.NaN;shoulderConstrained=sideBlocked;}
            else
            {
                shoulderConstrained|=sideBlocked;
                if(!shoulderConstrained)side=requested; // C3 already smooths authored shoulder changes.
                else if(Math.Abs(requested-side)>.005f)
                {
                    if(double.IsNaN(sideClearSince)||Math.Abs(requested)<sideClearLimit-.005f)sideClearSince=now;
                    sideClearLimit=Math.Abs(requested);
                    if(now-sideClearSince+1e-7>=options.ReleaseDelay)side+=(requested-side)*ViewMath.Blend(settings.ReleaseResponse,elapsed);
                    if(!sideBlocked&&Math.Abs(side-requested)<.005f)shoulderConstrained=false;
                }
                else sideClearSince=double.NaN;
            }
            var path=-forward*rig.Distance+right*side;float length=path.Length();
            if(length<settings.MinimumDistance)return RefinedFail(frame,rig,radius,"space-too-narrow");
            var direction=path/length;float limit=length;
            bool backHit=Hit(world,rig.Pivot,direction,length,radius,out float hit);
            if(invalidQuery)return RefinedFail(frame,rig,radius,"invalid-query");
            if(backHit)limit=Math.Max(0,hit-settings.Skin);
            if(limit<settings.MinimumDistance)return RefinedFail(frame,rig,radius,"space-too-narrow");
            float next;
            if(!initialized){next=limit;distanceConstrained=backHit&&limit<length-.001f;clearSince=double.NaN;}
            else if(limit<distance){next=limit;distanceConstrained=distanceConstrained||backHit;clearSince=double.NaN;}
            else if(!distanceConstrained&&!backHit)next=length; // no artificial delay on C3 zoom.
            else
            {
                distanceConstrained=true;next=distance;
                if(limit-distance>options.ReleaseDeadband)
                {
                    if(double.IsNaN(clearSince)||limit<clearLimit-options.ReleaseDeadband)clearSince=now;
                    clearLimit=limit;
                    if(now-clearSince+1e-7>=options.ReleaseDelay)next=distance+(limit-distance)*ViewMath.Blend(settings.ReleaseResponse,elapsed);
                }
                else clearSince=double.NaN;
                if(!backHit&&length-next<options.ReleaseDeadband)distanceConstrained=false;
            }
            next=Math.Min(next,limit);var position=rig.Pivot+direction*next;
            string reason=pivotCompressed?"third-person-pivot-compressed":"third-person";
            if(initialized)
            {
                float travelRadius=Math.Max(radius,previousRadius);
                if(Occupied(world,previous,travelRadius))return RefinedFail(frame,rig,radius,"previous-overlap");
                var travel=position-previous;float travelLength=travel.Length();
                if(travelLength>.001f&&Hit(world,previous,travel/travelLength,travelLength,travelRadius,out _))
                {
                    if(invalidQuery)return RefinedFail(frame,rig,radius,"invalid-query");
                    bool found=false;float near=Math.Min(next,Math.Max(refinement.RetractionDistance,settings.MinimumDistance+.12f)),lastAttempt=float.PositiveInfinity;
                    for(int attempt=0;attempt<3;attempt++)
                    {
                        float shorter=attempt==0?Math.Max(near,next*.65f):attempt==1?Math.Max(near,next*.4f):near;
                        if(shorter>=next-.001f||shorter>=lastAttempt-.001f)continue;lastAttempt=shorter;retractions++;
                        var candidate=rig.Pivot+direction*shorter;var route=candidate-previous;float routeLength=route.Length();
                        if(Hit(world,rig.Pivot,direction,shorter,radius,out _))continue;
                        if(routeLength>.001f&&Hit(world,previous,route/routeLength,routeLength,travelRadius,out _))continue;
                        if(Occupied(world,candidate,radius))continue;
                        next=shorter;position=candidate;found=true;break;
                    }
                    if(!found)return RefinedFail(frame,rig,radius,"transition-path-blocked");
                    distanceConstrained=true;clearSince=double.NaN;reason="third-person-retracted";
                }
            }
            if(invalidQuery)return RefinedFail(frame,rig,radius,"invalid-query");
            if(Occupied(world,position,radius))return RefinedFail(frame,rig,radius,"final-overlap");
            initialized=true;previous=position;distance=next;previousRadius=radius;previousPivot=rig.Pivot;refinedFrame=frame;refinedTime=now;
            Current=new(frame,true,true,radius,next,Vector3.Dot(position-rig.Pivot,right),queries,retractions,distanceConstrained,shoulderConstrained,reason);
            return new(frame,position,forward,radius,true,reason){ClearanceDistance=next,ConstrainedPivot=rig.Pivot};
        }
    }
}
