using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public sealed class CameraSolver
    {
        bool initialized;
        float height;
        Vector3 lastBody;
        public void Reset() { initialized=false; }
        public Vector3 Pivot(LogicalView logical,float dt,CameraSettings settings)
        {
            float target=Math.Clamp(logical.Eye.Y-logical.Body.Y,.65f,2.2f);
            if(!initialized||Vector3.DistanceSquared(lastBody,logical.Body)>9f) { height=target; initialized=true; }
            else height+=(target-height)*ViewMath.Blend(settings.VerticalResponse,dt);
            lastBody=logical.Body;
            return logical.Body+Vector3.UnitY*(height+settings.Height);
        }
        public Vector3 Desired(Vector3 pivot,Vector3 forward,CameraSettings settings)
        {
            forward=ViewMath.Unit(forward,Vector3.UnitZ);
            var flat=ViewMath.Unit(new Vector3(forward.X,0,forward.Z),Vector3.UnitZ);
            var right=Vector3.Cross(Vector3.UnitY,flat);
            return pivot+right*settings.Shoulder-forward*settings.Distance;
        }
    }
}
