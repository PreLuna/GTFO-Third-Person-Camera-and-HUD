using System;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public readonly record struct TransitionSettings(bool Enabled,float SpeedStart,float SpeedFull,float SpeedResponse,float AimEnterResponse,float AimExitResponse)
    {
        public static TransitionSettings Default=>new(true,1.5f,5.5f,5,20,8);
        public TransitionSettings Sanitized()
        {
            float start=StyleSettings.Safe(SpeedStart,.25f,5,1.5f);
            return new(Enabled,start,Math.Max(start+.5f,StyleSettings.Safe(SpeedFull,1,12,5.5f)),
                StyleSettings.Safe(SpeedResponse,1,20,5),StyleSettings.Safe(AimEnterResponse,8,40,20),StyleSettings.Safe(AimExitResponse,2,20,8));
        }
    }
    public readonly record struct TransitionFrame(int Frame,int Generation,CameraProfile Profile,CameraProfile Target,float Speed,float SpeedBlend,bool LeavingAim);
    public sealed class CameraTransitionSolver
    {
        bool initialized,wasAim,leavingAim;int frame,generation;float speed;CameraProfile current;
        public TransitionFrame Current {get;private set;}
        public void Reset(){initialized=false;speed=0;Current=default;}
        static CameraProfile Mix(CameraProfile a,CameraProfile b,float t)=>new(a.Distance+(b.Distance-a.Distance)*t,a.Shoulder+(b.Shoulder-a.Shoulder)*t,
            a.Height+(b.Height-a.Height)*t,a.VerticalFov+(b.VerticalFov-a.VerticalFov)*t);
        static float Smooth(float x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
        public TransitionFrame Step(LogicalView view,CameraKinematicFrame motion,float dt,CameraActivity activity,CameraPreset preset,bool left,
            StyleSettings style,TransitionSettings options,CameraProfile seed)
        {
            options=options.Sanitized();bool aim=activity==CameraActivity.ShoulderAim;
            bool reset=!initialized||view.Generation!=generation||view.Frame<frame||view.Frame>frame+1||!float.IsFinite(dt)||dt<0||dt>.2f
                ||motion.Reset&&motion.Frame==view.Frame&&frame!=view.Frame;
            if(reset){initialized=true;generation=view.Generation;frame=view.Frame;current=seed;speed=0;wasAim=aim;leavingAim=false;}
            bool advance=!reset&&view.Frame!=frame&&dt>.000001f;
            if(advance)
            {
                speed+=(motion.Speed-speed)*ViewMath.Blend(options.SpeedResponse,dt);
                if(wasAim&&!aim)leavingAim=true;if(aim)leavingAim=false;wasAim=aim;
            }
            float blend=Smooth((speed-options.SpeedStart)/(options.SpeedFull-options.SpeedStart));
            blend*=preset==CameraPreset.Stable?0:preset==CameraPreset.Combat?.5f:1;
            var target=aim?style.Profile(CameraActivity.ShoulderAim,preset):activity==CameraActivity.Crouching?style.Profile(CameraActivity.Crouching,preset):
                Mix(style.Profile(CameraActivity.Exploration,preset),style.Profile(CameraActivity.Running,preset),blend);
            target=target with{Shoulder=left?-target.Shoulder:target.Shoulder};
            if(advance)
            {
                float tune=preset==CameraPreset.Stable?1.5f:preset==CameraPreset.Combat?1.2f:preset==CameraPreset.Traversal?.8f:1;
                float Rate(float rate)=>aim?Math.Max(rate,options.AimEnterResponse):leavingAim?Math.Min(rate,options.AimExitResponse):rate*tune;
                current=new(current.Distance+(target.Distance-current.Distance)*ViewMath.Blend(Rate(StyleSettings.Safe(style.DistanceResponse,1,40,10)),dt),
                    current.Shoulder+(target.Shoulder-current.Shoulder)*ViewMath.Blend(Rate(StyleSettings.Safe(style.ShoulderResponse,1,40,12)),dt),
                    current.Height+(target.Height-current.Height)*ViewMath.Blend(Rate(12),dt),
                    current.VerticalFov+(target.VerticalFov-current.VerticalFov)*ViewMath.Blend(Rate(StyleSettings.Safe(style.FovResponse,1,40,9)),dt));
                if(Math.Abs(current.Distance-target.Distance)<.001f&&Math.Abs(current.VerticalFov-target.VerticalFov)<.01f)leavingAim=false;
                frame=view.Frame;
            }
            return Current=new(view.Frame,generation,current,target,speed,blend,leavingAim);
        }
    }
    public static class CameraComposition
    {
        public static float HorizontalFov(float verticalFov,float aspect)=>float.IsFinite(verticalFov)&&verticalFov>0&&verticalFov<170&&float.IsFinite(aspect)&&aspect>0
            ?2*MathF.Atan(MathF.Tan(verticalFov*MathF.PI/360)*aspect)*180/MathF.PI:0;
        // A projection reference, not a measured mesh bounding box or a game screenshot.
        public static float HeightRatio(float bodyHeight,float distance,float verticalFov)
            =>float.IsFinite(bodyHeight)&&float.IsFinite(distance)&&distance>0&&float.IsFinite(verticalFov)&&verticalFov>0&&verticalFov<170
                ?bodyHeight/(2*distance*MathF.Tan(verticalFov*MathF.PI/360)):0;
    }
}
