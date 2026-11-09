using System;
using System.Numerics;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public enum ObservationMovementBlock { None,Input,HorizontalSpeed,Unavailable }
    public readonly record struct ObservationMovement(int Frame,float AxisX,float AxisY,Vector3 Velocity,bool Captured=true)
    {
        public const float InputThreshold=.001f,StationarySpeed=.05f;
        public bool Valid=>Captured&&float.IsFinite(AxisX)&&float.IsFinite(AxisY)
            &&float.IsFinite(Velocity.X)&&float.IsFinite(Velocity.Y)&&float.IsFinite(Velocity.Z);
        public bool InputRequested=>Math.Abs(AxisX)>InputThreshold||Math.Abs(AxisY)>InputThreshold;
        // Ground adhesion/gravity can keep native vertical velocity nonzero while standing still.
        public float HorizontalSpeedSquared=>Velocity.X*Velocity.X+Velocity.Z*Velocity.Z;
        public float HorizontalSpeed=>MathF.Sqrt(HorizontalSpeedSquared);
        public ObservationMovementBlock Block=>!Valid?ObservationMovementBlock.Unavailable:InputRequested?ObservationMovementBlock.Input:
            HorizontalSpeedSquared>=StationarySpeed*StationarySpeed?ObservationMovementBlock.HorizontalSpeed:ObservationMovementBlock.None;
        public bool Stationary=>Block==ObservationMovementBlock.None;
    }
}
