using System;
using System.Numerics;

namespace PreLuna.ThirdPerson.Core
{
    public enum ViewMode { Original, Exploration, ShoulderAim, FirstPersonAim }
    public enum FallbackReason { None, Disabled, NoPlayer, Focus, ControlTaken, UnsupportedState, Attack, MissingModel, Collision, Fault }
    public readonly record struct InputContext(long Player, long Owner, long Item, int Generation, bool CanControl);
    public readonly record struct LogicalView(int Frame, float FixedTime, int Generation, Vector3 Body, Vector3 Eye, Vector3 Forward);
    public readonly record struct DisplayView(int Frame, Vector3 Position, Vector3 Forward, float Radius, bool IsThirdPerson, string Reason);
    public readonly record struct AimSolution(int Frame, float FixedTime, Vector3 OriginalOrigin, Vector3 Intent, bool Adapted);
    public readonly record struct CameraSettings(float Distance, float Shoulder, float Height, float VerticalResponse,
        float ReleaseResponse, float Radius, float Skin, float MinimumDistance, float NearClip)
    {
        public static CameraSettings Default => new(2.25f, .48f, .1f, 12f, 5f, .18f, .025f, .38f, .08f);
        public CameraSettings Sanitized() => new(Clamp(Distance,.5f,4f,2.25f), Clamp(Shoulder,-1f,1f,.48f),
            Clamp(Height,-.35f,.6f,.1f), Clamp(VerticalResponse,1f,40f,12f), Clamp(ReleaseResponse,.5f,30f,5f),
            Clamp(Radius,.08f,.5f,.18f), Clamp(Skin,.005f,.1f,.025f), Clamp(MinimumDistance,.15f,.8f,.38f), Clamp(NearClip,.03f,.2f,.08f));
        static float Clamp(float v,float low,float high,float fallback) => float.IsFinite(v)?Math.Clamp(v,low,high):fallback;
    }
    public interface ICameraWorld
    {
        bool Overlap(Vector3 center,float radius);
        bool Sweep(Vector3 origin,Vector3 direction,float distance,float radius,out float hitDistance);
    }
    public static class ViewMath
    {
        public static bool Finite(Vector3 v) => float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
        public static Vector3 Unit(Vector3 v,Vector3 fallback) => Finite(v)&&v.LengthSquared()>.00001f?Vector3.Normalize(v):fallback;
        public static float Blend(float response,float dt) => 1f-MathF.Exp(-response*Math.Clamp(float.IsFinite(dt)?dt:0,0,.2f));
        // Sphere centered at the lens encloses all four near-plane corners.
        public static float NearRadius(float near,float verticalFov,float aspect)
        {
            float halfHeight=near*MathF.Tan(Math.Clamp(verticalFov,1,170)*MathF.PI/360f);
            return MathF.Sqrt(near*near+halfHeight*halfHeight*(1+aspect*aspect));
        }
    }
}
