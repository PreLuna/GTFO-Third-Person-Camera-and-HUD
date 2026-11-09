using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public readonly record struct CameraKinematicFrame(int Frame,int Generation,bool Valid,bool Reset,Vector3 Velocity,float Travel)
    {public float Speed=>Velocity.Length();}
    // Actual root displacement, independent of C1 being enabled and of native ground-gravity velocity.
    public sealed class CameraKinematics
    {
        bool initialized;int frame,generation;Vector3 body;
        public CameraKinematicFrame Current {get;private set;}
        public void Reset(){initialized=false;Current=default;}
        public CameraKinematicFrame Step(LogicalView view,float dt)
        {
            if(!ViewMath.Finite(view.Body)||!ViewMath.Finite(view.Eye)||!ViewMath.Finite(view.Forward)||view.Forward.LengthSquared()<.00001f)
            {Reset();return Current=new(view.Frame,view.Generation,false,true,default,0);}
            bool reset=!initialized||view.Generation!=generation||view.Frame<frame||view.Frame>frame+1
                ||!float.IsFinite(dt)||dt<0||dt>.2f||Vector3.DistanceSquared(view.Body,body)>9;
            if(reset)
            {initialized=true;frame=view.Frame;generation=view.Generation;body=view.Body;return Current=new(frame,generation,true,true,default,0);}
            if(view.Frame==frame||dt<=.000001f)return Current;
            var delta=view.Body-body;delta.Y=0;float travel=delta.Length();var velocity=delta/dt;
            if(velocity.LengthSquared()>144)velocity=Vector3.Normalize(velocity)*12;
            body=view.Body;frame=view.Frame;
            return Current=new(frame,generation,true,false,velocity,Math.Min(travel,12*dt));
        }
    }
}
