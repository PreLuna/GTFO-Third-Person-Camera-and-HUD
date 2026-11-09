using UnityEngine;

namespace PreLuna.ThirdPerson.Body
{
    // Numeric copies only: the historical render sample must not retain Unity objects.
    internal readonly record struct SizeVector(float X,float Y,float Z)
    {
        internal static SizeVector From(Vector3 value)=>new(value.x,value.y,value.z);
    }
    internal readonly record struct TransformSizeSample(long Identity,long Parent,SizeVector Position,SizeVector LocalScale,SizeVector WorldScale)
    {
        internal static TransformSizeSample Capture(Transform value)=>value==null?default:
            new(value.Pointer.ToInt64(),value.parent==null?0:value.parent.Pointer.ToInt64(),SizeVector.From(value.position),
                SizeVector.From(value.localScale),SizeVector.From(value.lossyScale));
    }
    internal readonly record struct BodyScaleSnapshot(int Frame,TransformSizeSample Agent,TransformSizeSample Model,TransformSizeSample Animator)
    {
        internal static BodyScaleSnapshot Capture(Transform agent,Transform model,Transform animator)=>
            new(Time.frameCount,TransformSizeSample.Capture(agent),TransformSizeSample.Capture(model),TransformSizeSample.Capture(animator));
    }
}
