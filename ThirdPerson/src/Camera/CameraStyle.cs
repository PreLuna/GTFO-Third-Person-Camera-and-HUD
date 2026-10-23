using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public enum CameraPreset { Balanced, Combat, Traversal, Stable }
    public enum CameraActivity { Exploration, Running, Crouching, ShoulderAim }
    public readonly record struct CameraProfile(float Distance,float Shoulder,float Height,float VerticalFov);
    public readonly record struct StyleSettings(CameraProfile Exploration,CameraProfile Running,CameraProfile Crouching,CameraProfile Aim,
        float PositionResponse,float HeightResponse,float DistanceResponse,float ShoulderResponse,float FovResponse,float MaximumLag,
        bool MovementFeedback,float FeedbackAmplitude)
    {
        public static StyleSettings Default=>new(new(2.25f,.48f,.1f,80),new(2.6f,.42f,.08f,84),new(1.95f,.42f,.04f,78),new(1.35f,.48f,.08f,70),
            18,12,10,12,9,.16f,false,.008f);
        public CameraProfile Profile(CameraActivity activity,CameraPreset preset)
        {
            var p=activity switch{CameraActivity.Running=>Running,CameraActivity.Crouching=>Crouching,CameraActivity.ShoulderAim=>Aim,_=>Exploration};
            // These are authored style choices, not measurements of either reference game.
            if(preset==CameraPreset.Combat&&activity!=CameraActivity.ShoulderAim)p=p with{Distance=p.Distance*.9f};
            if(preset==CameraPreset.Traversal&&activity!=CameraActivity.ShoulderAim)p=p with{Distance=p.Distance*1.12f,VerticalFov=p.VerticalFov+2};
            return new(Safe(p.Distance,.5f,4,2.25f),Safe(Math.Abs(p.Shoulder),0,1,.48f),Safe(p.Height,-.35f,.6f,.1f),Safe(p.VerticalFov,45,110,80));
        }
        public static float Safe(float value,float min,float max,float fallback)=>float.IsFinite(value)?Math.Clamp(value,min,max):fallback;
    }
    public readonly record struct RigTarget(Vector3 Pivot,Vector3 Forward,float Distance,float Shoulder,float VerticalFov,CameraActivity Activity,float Feedback);
    public sealed class CameraStyleSolver
    {
        bool initialized,startClose;
        Vector3 anchor,lastBody;
        float eyeHeight,height,distance,shoulder,fov,movement;
        float closeDistance=.55f;
        double phase;
        public void Reset(bool fromFirstPerson=false,float initialDistance=.55f){initialized=false;startClose=fromFirstPerson;closeDistance=StyleSettings.Safe(initialDistance,.5f,1,.55f);phase=0;movement=0;}
        public RigTarget Step(LogicalView logical,float dt,CameraActivity activity,CameraPreset preset,bool left,StyleSettings settings,float nativeFov)
        {
            var p=settings.Profile(activity,preset);
            float response=StyleSettings.Safe(settings.PositionResponse,1,40,18);
            if(preset==CameraPreset.Stable)response=40;
            if(preset==CameraPreset.Traversal)response=Math.Min(response,12);
            float elapsed=float.IsFinite(dt)?Math.Clamp(dt,0,.2f):0;
            var body=logical.Body;
            float targetEye=Math.Clamp(logical.Eye.Y-body.Y,.65f,2.2f);
            float requestedShoulder=left?-p.Shoulder:p.Shoulder;
            bool reset=!initialized||!ViewMath.Finite(body)||Vector3.DistanceSquared(body,lastBody)>9;
            if(reset)
            {
                anchor=body;eyeHeight=targetEye;height=p.Height;
                distance=startClose?closeDistance:p.Distance;shoulder=startClose?0:requestedShoulder;
                fov=startClose?StyleSettings.Safe(nativeFov,30,130,p.VerticalFov):p.VerticalFov;
                initialized=true;phase=0;movement=0;elapsed=0;
            }
            anchor+=(body-anchor)*ViewMath.Blend(response,elapsed);
            float lag=StyleSettings.Safe(settings.MaximumLag,0,.3f,.16f);
            var horizontal=new Vector3(anchor.X-body.X,0,anchor.Z-body.Z);
            if(horizontal.LengthSquared()>lag*lag)horizontal=ViewMath.Unit(horizontal,Vector3.Zero)*lag;
            anchor=new(body.X+horizontal.X,body.Y,body.Z+horizontal.Z);
            eyeHeight+=(targetEye-eyeHeight)*ViewMath.Blend(StyleSettings.Safe(settings.HeightResponse,1,40,12),elapsed);
            height+=(p.Height-height)*ViewMath.Blend(12,elapsed);
            distance+=(p.Distance-distance)*ViewMath.Blend(StyleSettings.Safe(settings.DistanceResponse,1,40,10),elapsed);
            shoulder+=(requestedShoulder-shoulder)*ViewMath.Blend(StyleSettings.Safe(settings.ShoulderResponse,1,40,12),elapsed);
            fov+=(p.VerticalFov-fov)*ViewMath.Blend(StyleSettings.Safe(settings.FovResponse,1,40,9),elapsed);
            float travel=reset?0:new Vector3(body.X-lastBody.X,0,body.Z-lastBody.Z).Length();
            if(elapsed>0)
            {
                float speed=travel/elapsed;
                movement+=(Math.Clamp(speed/2,0,1)-movement)*ViewMath.Blend(12,elapsed);
                phase=(phase+Math.Min(travel,.5f)*Math.PI*2/1.4)%(Math.PI*2);
            }
            float feedback=settings.MovementFeedback&&preset!=CameraPreset.Stable
                ?(float)Math.Sin(phase)*StyleSettings.Safe(settings.FeedbackAmplitude,0,.02f,.008f)*movement*(activity==CameraActivity.ShoulderAim?.15f:1):0;
            lastBody=body;
            return new(anchor+Vector3.UnitY*(eyeHeight+height+feedback),ViewMath.Unit(logical.Forward,Vector3.UnitZ),distance,shoulder,fov,activity,feedback);
        }
    }
}
