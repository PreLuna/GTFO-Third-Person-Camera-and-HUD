using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public readonly record struct ActionFeedbackSettings(bool Enabled,float GaitStrength,float StartStopStrength,float StrideLength,float Response)
    {
        public static ActionFeedbackSettings Default=>new(true,1,.5f,1.4f,12);
        public ActionFeedbackSettings Sanitized()=>new(Enabled,StyleSettings.Safe(GaitStrength,0,1,1),StyleSettings.Safe(StartStopStrength,0,1,.5f),
            StyleSettings.Safe(StrideLength,.8f,2.5f,1.4f),StyleSettings.Safe(Response,4,30,12));
    }
    public readonly record struct ActionFeedbackFrame(int Frame,int Generation,Vector3 Offset,float Gait,Vector3 Inertia,float AimScale,bool Enabled);
    public sealed class CameraActionFeedbackSolver
    {
        bool initialized;int frame,generation;Vector3 velocity,inertia;float weight,aimScale=1;double phase;
        public ActionFeedbackFrame Current {get;private set;}
        public void Reset(){initialized=false;velocity=inertia=default;weight=0;aimScale=1;phase=0;Current=default;}
        static Vector3 Limit(Vector3 v,float maximum){float len=v.Length();return len>maximum&&len>0?v*(maximum/len):v;}
        public ActionFeedbackFrame Step(LogicalView view,CameraKinematicFrame motion,float dt,CameraActivity activity,CameraPreset preset,StyleSettings style,ActionFeedbackSettings options)
        {
            options=options.Sanitized();float amplitude=StyleSettings.Safe(style.FeedbackAmplitude,0,.02f,.008f);
            if(!options.Enabled||!style.MovementFeedback||preset==CameraPreset.Stable||amplitude==0||!motion.Valid)
            {Reset();return Current=new(view.Frame,view.Generation,default,0,default,1,false);}
            bool aim=activity==CameraActivity.ShoulderAim;
            bool reset=!initialized||view.Generation!=generation||view.Frame<frame||view.Frame>frame+1||!float.IsFinite(dt)||dt<0||dt>.2f
                ||motion.Reset&&motion.Frame==view.Frame&&frame!=view.Frame;
            if(reset){Reset();initialized=true;frame=view.Frame;generation=view.Generation;velocity=motion.Velocity;aimScale=aim?.15f:1;}
            if(!reset&&view.Frame!=frame&&dt>.000001f)
            {
                var nextVelocity=velocity+(motion.Velocity-velocity)*ViewMath.Blend(8,dt);
                var acceleration=Limit((nextVelocity-velocity)/dt,12);
                var desired=-acceleration*(amplitude*options.StartStopStrength/12);
                inertia+=(desired-inertia)*ViewMath.Blend(options.Response,dt);
                float moving=motion.Travel>.000001f?Math.Clamp(motion.Speed/2,0,1):0;
                weight+=(moving-weight)*ViewMath.Blend(options.Response,dt);
                if(motion.Travel>0)phase=(phase+motion.Travel/options.StrideLength*Math.PI*2)%(Math.PI*2);
                aimScale+=( (aim?.15f:1)-aimScale)*ViewMath.Blend(aim?24:6,dt);
                if(moving==0&&weight<.0001f&&inertia.LengthSquared()<1e-10f){weight=0;inertia=default;}
                velocity=nextVelocity;frame=view.Frame;
            }
            float gait=(float)Math.Sin(phase)*amplitude*options.GaitStrength*weight;
            float gaitTune=preset==CameraPreset.Combat?.35f:preset==CameraPreset.Traversal?.8f:.65f;
            float inertiaTune=preset==CameraPreset.Combat?.35f:preset==CameraPreset.Traversal?1:.7f;
            float scale=aim?Math.Min(.15f,aimScale):aimScale;
            var offset=Limit(Vector3.UnitY*(gait*gaitTune)+inertia*inertiaTune,amplitude)*scale;
            return Current=new(view.Frame,generation,offset,gait*scale,inertia*scale,scale,true);
        }
    }
}
