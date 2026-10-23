using System;

namespace PreLuna.ThirdPerson.Core
{
    public static class BodyOcclusionPolicy
    {
        public static (float Opacity,bool Hidden) Evaluate(float distance,float near,float far,bool previouslyHidden)
        {
            near=float.IsFinite(near)?Math.Clamp(near,.05f,.5f):.12f;
            far=float.IsFinite(far)?Math.Clamp(far,near+.05f,1):Math.Max(.4f,near+.05f);
            if(!float.IsFinite(distance))return (0,true);
            return (Math.Clamp((distance-near)/(far-near),0,1),distance<(previouslyHidden?far+.04f:far));
        }
    }
}
