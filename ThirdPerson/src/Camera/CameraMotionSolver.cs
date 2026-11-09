using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public readonly record struct MotionSettings(bool Enabled,float HorizontalResponse,float RootResponse,float MaximumRootLag,
        float LeadSeconds,float MaximumLead,float LeadResponse)
    {
        public static MotionSettings Default=>new(true,12,8,.12f,.1f,.12f,6);
        public static MotionSettings Legacy=>Default with{Enabled=false};
        public MotionSettings Sanitized()=>new(Enabled,Safe(HorizontalResponse,1,40,12),Safe(RootResponse,1,40,8),
            Safe(MaximumRootLag,0,.25f,.12f),Safe(LeadSeconds,0,.2f,.1f),Safe(MaximumLead,0,.2f,.12f),Safe(LeadResponse,1,30,6));
        static float Safe(float value,float min,float max,float fallback)=>float.IsFinite(value)?Math.Clamp(value,min,max):fallback;
    }
    public enum MotionResetReason { None,Start,Context,Discontinuity,InvalidSample }
    public readonly record struct MotionFrame(int Frame,int Generation,bool Valid,Vector3 Anchor,float EyeHeight,Vector3 Lead,
        Vector3 Velocity,MotionResetReason ResetReason)
    {
        public Vector3 Pivot=>Anchor+Vector3.UnitY*EyeHeight+Lead;
    }

    // C1 owns only translation. It never consumes input, rotates the view, or touches game objects.
    public sealed class CameraMotionSolver
    {
        bool initialized;
        Vector3 lastBody,anchor,lead,velocity;
        float eyeHeight,lastEye;
        int frame,generation;
        public MotionFrame Current {get;private set;}=new(-1,0,false,default,0,default,default,MotionResetReason.Start);
        public void Reset(){initialized=false;Current=new(-1,0,false,default,0,default,default,MotionResetReason.Start);}
        static Vector3 Limit(Vector3 value,float maximum)
        {float length=value.Length();return length>maximum&&length>0?value*(maximum/length):value;}
        // Exact first-order response to a linearly moving target, reducing timestep-dependent follow error.
        static Vector3 Track(Vector3 current,Vector3 from,Vector3 to,float response,float dt)
        {
            float decay=MathF.Exp(-response*dt),average=(1-decay)/(response*dt);
            return current*decay+from*(average-decay)+to*(1-average);
        }
        static float Track(float current,float from,float to,float response,float dt)
            =>Track(new Vector3(current,0,0),new Vector3(from,0,0),new Vector3(to,0,0),response,dt).X;
        public MotionFrame Step(LogicalView view,float dt,CameraActivity activity,CameraPreset preset,StyleSettings style,MotionSettings options)
        {
            options=options.Sanitized();
            if(!ViewMath.Finite(view.Body)||!ViewMath.Finite(view.Eye)||!ViewMath.Finite(view.Forward)||view.Forward.LengthSquared()<.00001f)
            {Reset();return Current=new(view.Frame,view.Generation,false,default,0,default,default,MotionResetReason.InvalidSample);}
            float targetEye=Math.Clamp(view.Eye.Y-view.Body.Y,.65f,2.2f);
            MotionResetReason reason=!initialized?MotionResetReason.Start:view.Generation!=generation?MotionResetReason.Context:
                !float.IsFinite(dt)||dt<0||dt>.2f||view.Frame<frame||view.Frame>frame+1||Vector3.DistanceSquared(view.Body,lastBody)>9
                    ?MotionResetReason.Discontinuity:MotionResetReason.None;
            if(reason!=MotionResetReason.None)
            {
                initialized=true;anchor=lastBody=view.Body;eyeHeight=lastEye=targetEye;lead=velocity=Vector3.Zero;
                frame=view.Frame;generation=view.Generation;
                return Current=new(frame,generation,true,anchor,eyeHeight,lead,velocity,reason);
            }
            // Repeated render/fire queries cannot advance motion or replace the previous-frame sample.
            if(view.Frame==frame||dt<=.000001f)return Current;
            bool stable=preset==CameraPreset.Stable,aim=activity==CameraActivity.ShoulderAim;
            float horizontal=stable?40:Math.Min(StyleSettings.Safe(style.PositionResponse,1,40,18),options.HorizontalResponse);
            if(preset==CameraPreset.Traversal)horizontal=Math.Min(horizontal,10);
            if(aim)horizontal=Math.Max(horizontal,30);
            float lag=StyleSettings.Safe(style.MaximumLag,0,.3f,.16f)*(aim?.35f:1);
            var followed=Track(anchor,lastBody,view.Body,horizontal,dt);
            var offset=Limit(new(followed.X-view.Body.X,0,followed.Z-view.Body.Z),lag);
            float rootResponse=stable?40:aim?Math.Max(24,options.RootResponse):options.RootResponse;
            float rootLag=options.MaximumRootLag*(stable?.25f:aim?.5f:1);
            float root=Track(anchor.Y,lastBody.Y,view.Body.Y,rootResponse,dt);
            anchor=new(view.Body.X+offset.X,Math.Clamp(root,view.Body.Y-rootLag,view.Body.Y+rootLag),view.Body.Z+offset.Z);
            eyeHeight=Track(eyeHeight,lastEye,targetEye,StyleSettings.Safe(style.HeightResponse,1,40,12),dt);
            eyeHeight=Math.Clamp(eyeHeight,.65f,2.2f);
            var raw=(view.Body-lastBody)/dt;raw.Y=0;raw=Limit(raw,12);
            velocity+=(raw-velocity)*ViewMath.Blend(12,dt);
            var desired=velocity.LengthSquared()<.04f?Vector3.Zero:Limit(velocity*options.LeadSeconds,options.MaximumLead);
            desired*=stable?0:aim?.15f:1;
            // Reverse by first releasing the old lead, rather than crossing through a sudden target flip.
            if(Vector3.Dot(lead,desired)<0&&lead.LengthSquared()>.000025f)desired=Vector3.Zero;
            lead+=(desired-lead)*ViewMath.Blend(options.LeadResponse,dt);
            lead=Limit(lead,options.MaximumLead*(stable?0:aim?.15f:1));
            var total=Limit(offset+lead,Math.Min(.35f,lag+options.MaximumLead));
            lead=total-offset;
            lastBody=view.Body;lastEye=targetEye;frame=view.Frame;generation=view.Generation;
            return Current=new(frame,generation,true,anchor,eyeHeight,lead,velocity,MotionResetReason.None);
        }
    }
}
