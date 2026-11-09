using System;
using PreLuna.ThirdPerson.CameraSystem;

namespace PreLuna.ThirdPerson.Runtime
{
    // The verified native methods retain sensitivity, inversion, smoothing and gamepad curves.
    // Only their yaw/pitch destination is scoped; RotationUpdate/recoil is never replayed.
    internal sealed class CameraInputAdapter
    {
        internal readonly CameraOrientationSolver Orientation=new();
        internal bool Capturing=>current!=null;
        LookCameraController current;
        int serial;
        internal struct Capture
        {
            internal LookCameraController Camera;
            internal float Yaw,Pitch,MaximumYaw;
            internal int Serial;
        }
        internal void Begin(LookCameraController camera,float maximumYaw,out Capture capture)
        {
            capture=default;
            // A nested native look call is included by its outer capture, once.
            if(current!=null||!Orientation.Active)return;
            capture=new(){Camera=camera,Yaw=camera.m_yaw,Pitch=camera.m_pitch,MaximumYaw=maximumYaw,Serial=++serial};
            current=camera;
            // A relative yaw destination avoids turning a large >180° native increment backwards
            // through shortest-angle subtraction, and avoids global-yaw wrap at the +/-180 seam.
            try {camera.m_yaw=Orientation.YawOffset;camera.m_pitch=Orientation.Pitch(capture.Pitch);}
            catch {Finish(capture,false);throw;}
        }
        internal void Finish(Capture capture,bool succeeded)
        {
            if(capture.Camera==null||current==null||capture.Serial!=serial)return;
            try
            {
                if(succeeded)Orientation.Capture(capture.Camera.m_yaw,capture.Pitch,capture.Camera.m_pitch,capture.MaximumYaw);
            }
            finally
            {
                try {capture.Camera.m_yaw=capture.Yaw;}
                finally
                {
                    try {capture.Camera.m_pitch=capture.Pitch;}
                    finally {current=null;if(!succeeded)Orientation.Reset(true);}
                }
            }
        }
    }
}
