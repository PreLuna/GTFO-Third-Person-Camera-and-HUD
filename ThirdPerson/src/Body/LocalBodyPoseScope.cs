using System;
using System.Collections.Generic;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using N=System.Numerics.Vector3;

namespace PreLuna.ThirdPerson.Body
{
    internal readonly record struct NativeGripSample(bool Valid,bool HasLeft,Vector3 RightPosition,Quaternion RightRotation,Vector3 LeftPosition,Quaternion LeftRotation)
    {
        // Capture before RenderScope moves the camera: these are the authored FPS
        // grip poses after native ADS/recoil/item animation, not the display camera.
        internal static NativeGripSample Capture(ItemEquippable item)
        {
            var right=item?.RightHandGripTrans;var left=item?.LeftHandGripTrans;
            if(right==null||!Finite(right.position)||!Finite(right.rotation))return default;
            bool hasLeft=left!=null&&Finite(left.position)&&Finite(left.rotation);
            return new(true,hasLeft,right.position,right.rotation,hasLeft?left.position:default,hasLeft?left.rotation:Quaternion.identity);
        }
        internal static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        internal static bool Finite(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.000001f;
    }

    // Retarget only displayed arm bones. Native gameplay, Animator state/time,
    // recoil springs, root motion and networking are never updated here.
    internal sealed class LocalBodyPoseScope : IDisposable
    {
        readonly List<(Transform bone,Vector3 position,Quaternion rotation)> original=new(),displayed=new();
        bool suspended,disposed;
        internal string Status {get;private set;}="no-native-grip";
        internal bool RightApplied {get;private set;}
        internal bool LeftApplied {get;private set;}
        internal bool RightClamped {get;private set;}
        internal bool LeftClamped {get;private set;}
        internal bool WasRestored {get;private set;}
        internal float RightGripError {get;private set;}
        internal float LeftGripError {get;private set;}
        readonly record struct Arm(Transform Upper,Transform Lower,Transform Hand)
        {internal bool Valid=>Upper!=null&&Lower!=null&&Hand!=null&&Upper.Pointer!=Lower.Pointer&&Lower.Pointer!=Hand.Pointer&&Lower.IsChildOf(Upper)&&Hand.IsChildOf(Lower);}
        internal LocalBodyPoseScope(LocalPlayerAgent player,NativeGripSample sample)
        {
            if(!sample.Valid)return;
            var animator=player.AnimatorBody;var attach=player.PlayerSyncModel?.ItemAlign;
            if(animator==null||attach==null){Status="body-rig-not-ready";return;}
            if(player.AnimatorArms!=null&&player.AnimatorArms.Pointer==animator.Pointer){Status="shared-native-rig";return;}
            Arm right=default,left=default;
            if(animator.isHuman)
            {
                right=new(animator.GetBoneTransform(HumanBodyBones.RightUpperArm),animator.GetBoneTransform(HumanBodyBones.RightLowerArm),animator.GetBoneTransform(HumanBodyBones.RightHand));
                left=new(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),animator.GetBoneTransform(HumanBodyBones.LeftHand));
            }
            if(!right.Valid||!left.Valid)
            {
                var ik=player.SyncIK;
                var refs=ik?.m_fullbodyBipedIK?.references??ik?.m_bipedIK?.references;
                if(refs!=null)
                {
                    if(!right.Valid)right=new(refs.rightUpperArm,refs.rightForearm,refs.rightHand);
                    if(!left.Valid)left=new(refs.leftUpperArm,refs.leftForearm,refs.leftHand);
                }
                if(!right.Valid&&ik?.m_rightArmSolver!=null)right=new(ik.m_rightArmSolver.bone1?.transform,ik.m_rightArmSolver.bone2?.transform,ik.m_rightArmSolver.bone3?.transform);
                if(!left.Valid&&ik?.m_leftArmSolver!=null)left=new(ik.m_leftArmSolver.bone1?.transform,ik.m_leftArmSolver.bone2?.transform,ik.m_leftArmSolver.bone3?.transform);
            }
            bool Own(Arm arm)=>arm.Valid&&arm.Upper.IsChildOf(animator.transform)
                &&(player.AnimatorArms==null||!arm.Upper.IsChildOf(player.AnimatorArms.transform))
                &&!player.FPSCamera.m_camera.transform.IsChildOf(arm.Upper);
            if(!Own(right)||!attach.IsChildOf(right.Hand)){Status="right-arm-or-attachment-unavailable";return;}
            try
            {
                Save(right.Upper);Save(right.Lower);Save(right.Hand);
                if(sample.HasLeft&&Own(left)){Save(left.Upper);Save(left.Lower);Save(left.Hand);}
                // ItemAlign may have a grip-specific child offset from the hand.
                // Preserve it rather than treating hand-bone origin as weapon grip.
                var handToGrip=Quaternion.Inverse(right.Hand.rotation)*(attach.position-right.Hand.position);
                var gripRotation=Quaternion.Inverse(right.Hand.rotation)*attach.rotation;
                var handRotation=sample.RightRotation*Quaternion.Inverse(gripRotation);
                var handTarget=sample.RightPosition-handRotation*handToGrip;
                RightApplied=Apply(right,handTarget,handRotation,out bool rightClamp);RightClamped=rightClamp;
                if(!RightApplied){Status="right-arm-degenerate";RestoreOriginal();return;}
                RightGripError=(attach.position-sample.RightPosition).magnitude;
                // Reaching limits shift the visual weapon with the solved right
                // grip; keep the left target on that same weapon, not in mid-air.
                var shift=attach.position-sample.RightPosition;
                if(sample.HasLeft&&Own(left))
                {
                    LeftApplied=Apply(left,sample.LeftPosition+shift,sample.LeftRotation,out bool leftClamp);LeftClamped=leftClamp;
                    LeftGripError=(left.Hand.position-(sample.LeftPosition+shift)).magnitude;
                }
                foreach(var pose in original)displayed.Add((pose.bone,pose.bone.localPosition,pose.bone.localRotation));
                Status=LeftApplied?"native-grips-both-arms":"native-grip-right-arm";
            }
            catch{RestoreOriginal();throw;}
        }
        void Save(Transform bone)
        {if(!original.Exists(p=>p.bone.Pointer==bone.Pointer))original.Add((bone,bone.localPosition,bone.localRotation));}
        static N Num(Vector3 v)=>new(v.x,v.y,v.z);
        static Vector3 Unity(N v)=>new(v.X,v.Y,v.Z);
        static bool Apply(Arm arm,Vector3 target,Quaternion rotation,out bool clamped)
        {
            clamped=false;
            if(!arm.Valid||!NativeGripSample.Finite(target)||!NativeGripSample.Finite(rotation))return false;
            if(!TwoBonePoseSolver.Solve(Num(arm.Upper.position),Num(arm.Lower.position),Num(arm.Hand.position),Num(target),out var elbow,out var wrist,out clamped))return false;
            var a=arm.Lower.position-arm.Upper.position;var b=Unity(elbow)-arm.Upper.position;
            arm.Upper.rotation=Quaternion.FromToRotation(a,b)*arm.Upper.rotation;
            a=arm.Hand.position-arm.Lower.position;b=Unity(wrist)-arm.Lower.position;
            arm.Lower.rotation=Quaternion.FromToRotation(a,b)*arm.Lower.rotation;
            arm.Hand.rotation=rotation;
            return true;
        }
        void RestoreOriginal()
        {foreach(var pose in original)if(pose.bone!=null){pose.bone.localPosition=pose.position;pose.bone.localRotation=pose.rotation;}}
        internal void Suspend(){if(disposed||suspended)return;RestoreOriginal();suspended=true;}
        internal void Resume()
        {
            if(disposed||!suspended)return;
            foreach(var pose in displayed)if(pose.bone!=null){pose.bone.localPosition=pose.position;pose.bone.localRotation=pose.rotation;}
            suspended=false;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;RestoreOriginal();WasRestored=true;
            foreach(var pose in original)WasRestored&=pose.bone!=null&&(pose.bone.localPosition-pose.position).sqrMagnitude<.000001f&&Quaternion.Angle(pose.bone.localRotation,pose.rotation)<.02f;
            if(!WasRestored)throw new InvalidOperationException("第三人称手臂显示姿态未恢复");
        }
    }
}
