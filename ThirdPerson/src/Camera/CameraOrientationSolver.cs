using System;
using System.Numerics;

namespace PreLuna.ThirdPerson.CameraSystem
{
    // C2a: bounded stationary observation. No time integration and no second input sampling.
    public sealed class CameraOrientationSolver
    {
        public bool Active { get; private set; }
        public bool BlockedUntilRelease { get; private set; }
        public float YawOffset { get; private set; }
        public float PitchOffset { get; private set; }
        public int Samples { get; private set; }
        public void ObserveKey(bool held) { if(!held)BlockedUntilRelease=false; }
        public bool Begin(float yaw,float pitch)
        {
            if(BlockedUntilRelease||!float.IsFinite(yaw)||!float.IsFinite(pitch))return false;
            Active=true;return true;
        }
        public void Capture(float processedYawOffset,float basePitch,float observedPitch,float maximumYaw)
        {
            if(!Active)return;
            if(!float.IsFinite(processedYawOffset)||!float.IsFinite(basePitch)||!float.IsFinite(observedPitch))
                throw new InvalidOperationException("Non-finite native look sample");
            maximumYaw=float.IsFinite(maximumYaw)?Math.Clamp(maximumYaw,10,75):65;
            YawOffset=Math.Clamp(processedYawOffset,-maximumYaw,maximumYaw);
            PitchOffset=Math.Clamp(Delta(basePitch,observedPitch),-35,35);
            Samples++;
        }
        public float Yaw(float nativeYaw)=>Wrap(nativeYaw+YawOffset);
        public float Pitch(float nativePitch)=>Math.Clamp(nativePitch+PitchOffset,-85,85);
        public Vector3 Forward(float nativeYaw,float nativePitch)
        {
            float y=Yaw(nativeYaw)*MathF.PI/180,p=Pitch(nativePitch)*MathF.PI/180;
            return new(MathF.Sin(y)*MathF.Cos(p),-MathF.Sin(p),MathF.Cos(y)*MathF.Cos(p));
        }
        public void Reset(bool waitForRelease=false)
        {Active=false;YawOffset=PitchOffset=0;BlockedUntilRelease=waitForRelease;}
        public static float Wrap(float angle)=>((angle+180)%360+360)%360-180;
        public static float Delta(float from,float to)=>Wrap(to-from);
    }
}
