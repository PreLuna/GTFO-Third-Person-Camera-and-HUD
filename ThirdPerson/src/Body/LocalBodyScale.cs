using System;
using UnityEngine;

namespace PreLuna.ThirdPerson.Body
{
    // The observed local FPS visual rig is uniformly 1.2; synced player rigs are 1.
    // Own only that known visual-root correction during third-person presentation.
    // Keep it stable across cameras/skinning; never scale the player or FPS rig.
    internal sealed class LocalBodyScale
    {
        readonly Transform root;
        Vector3 original,applied;
        internal bool Applied {get;private set;}
        internal string Status {get;private set;}="not-prepared";
        internal LocalBodyScale(Transform visualRoot){root=visualRoot;}
        static bool Same(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)<.0001f&&Math.Abs(a.y-b.y)<.0001f&&Math.Abs(a.z-b.z)<.0001f;
        internal bool Prepare(bool enabled)
        {
            if(!enabled){bool changed=Restore();Status="disabled";return changed;}
            if(root==null){Status="missing-root";return false;}
            var current=root.localScale;
            if(Applied)
            {
                if(Same(current,applied))return false;
                // A native reassertion of the captured FPS scale is not a new
                // presentation transition and must not restart skin warmup.
                if(Same(current,original)){root.localScale=applied;Status="normalized-1.2-local-rig";return false;}
                Applied=false;Status="external-scale-preserved";return true;
            }
            if(!Same(current,new(1.2f,1.2f,1.2f))){Status="different-scale-preserved";return false;}
            original=current;applied=new(1,1,1);root.localScale=applied;Applied=true;Status="normalized-1.2-local-rig";return true;
        }
        internal bool Restore()
        {
            if(!Applied)return false;
            Applied=false;
            if(root!=null&&Same(root.localScale,applied))
            {root.localScale=original;Status="restored-fps-scale";return true;}
            Status="external-scale-preserved";return false;
        }
        internal object Diagnostics()=>new{Status,Applied,root=root==null?0:root.Pointer.ToInt64(),
            original=SizeVector.From(original),target=SizeVector.From(applied),current=root==null?(SizeVector?)null:SizeVector.From(root.localScale)};
    }
}
