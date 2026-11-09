using System;
using System.Collections.Generic;
using Player;
using RootMotion.FinalIK;
using UnityEngine;

namespace PreLuna.ThirdPerson.Body
{
    // Owned instances of the game's native FinalIK solvers. Read the native
    // body rig/profile, but do not change PlayerSyncIK or any FPS solver.
    internal sealed class NativeBodyIk : IDisposable
    {
        readonly List<Transform> bones=new();
        readonly List<(Transform Bone,Vector3 Position,Quaternion Rotation)> original=new();
        IKSolverAim aim;
        IKSolverLimb left;
        int lastFrame=-1,nextAttempt;
        string aimStatus="not-bound",leftStatus="not-bound";
        float leftError;
        bool Own(LocalPlayerAgent player,Transform bone)
        {
            return bone!=null&&bone.IsChildOf(player.AnimatorBody.transform)
                &&!bone.IsChildOf(player.AnimatorArms.transform)
                &&!player.FPSCamera.m_camera.transform.IsChildOf(bone);
        }
        void Remember(Transform bone)
        {if(bone!=null&&!bones.Exists(b=>b.Pointer==bone.Pointer))bones.Add(bone);}
        void Bind(LocalPlayerAgent player,WorldWeaponView view)
        {
            var root=player.AnimatorBody.transform;
            if(aim==null)
            {
                var source=player.SyncIK?.m_aimSolver;
                var chain=new List<Transform>();
                bool valid=source?.bones!=null&&source.bones.Length>0&&!source.useRotationLimits&&view.AimReference!=null;
                if(valid)foreach(var b in source.bones)
                {if(b==null||!Own(player,b.transform)){valid=false;break;}chain.Add(b.transform);}
                if(valid&&Finite(source.axis)&&source.axis.sqrMagnitude>.000001f)
                {
                    var solver=new IKSolverAim{transform=view.AimReference,axis=source.axis,poleAxis=source.poleAxis,
                        clampWeight=source.clampWeight,clampSmoothing=source.clampSmoothing,tolerance=source.tolerance,
                        maxIterations=Math.Max(1,source.maxIterations),useRotationLimits=false,
                        polePosition=source.polePosition,poleWeight=source.poleTarget==null||Own(player,source.poleTarget)?source.poleWeight:0,
                        poleTarget=Own(player,source.poleTarget)?source.poleTarget:null,IKPositionWeight=1};
                    foreach(var bone in chain)Remember(bone);
                    if(solver.SetChain(chain.ToArray(),root))
                    {
                        for(int i=0;i<chain.Count;i++)solver.bones[i].weight=source.bones[i].weight;
                        aim=solver;aimStatus="native-aim-profile";
                    }
                    else aimStatus="aim-chain-rejected";
                }
                else aimStatus="native-body-aim-chain-unavailable";
            }
            if(left==null)
            {
                var source=player.SyncIK?.m_leftArmSolver;
                Transform upper=source?.bone1?.transform,lower=source?.bone2?.transform,hand=source?.bone3?.transform;
                if(!Own(player,upper)||!Own(player,lower)||!Own(player,hand))
                {
                    var refs=player.SyncIK?.m_fullbodyBipedIK?.references??player.SyncIK?.m_bipedIK?.references;
                    upper=refs?.leftUpperArm;lower=refs?.leftForearm;hand=refs?.leftHand;
                    if((!Own(player,upper)||!Own(player,lower)||!Own(player,hand))&&player.AnimatorBody.isHuman)
                    {
                        upper=player.AnimatorBody.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        lower=player.AnimatorBody.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                        hand=player.AnimatorBody.GetBoneTransform(HumanBodyBones.LeftHand);
                    }
                }
                if(Own(player,upper)&&Own(player,lower)&&Own(player,hand)&&lower.IsChildOf(upper)&&hand.IsChildOf(lower))
                {
                    var solver=new IKSolverLimb{goal=AvatarIKGoal.LeftHand,IKPositionWeight=1,IKRotationWeight=1};
                    if(source!=null)
                    {
                        solver.bendModifier=source.bendModifier;solver.bendModifierWeight=source.bendModifierWeight;
                        solver.maintainRotationWeight=source.maintainRotationWeight;solver.bendNormal=source.bendNormal;
                        solver.bendGoal=Own(player,source.bendGoal)?source.bendGoal:null;
                        if(solver.bendGoal==null&&solver.bendModifier==IKSolverLimb.BendModifier.Goal)solver.bendModifier=IKSolverLimb.BendModifier.Animation;
                    }
                    Remember(upper);Remember(lower);Remember(hand);
                    if(solver.SetChain(upper,lower,hand,root)){left=solver;leftStatus="native-limb-solver";}
                    else leftStatus="left-chain-rejected";
                }
                else leftStatus="native-body-left-chain-unavailable";
            }
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        internal void Solve(LocalPlayerAgent player,WorldWeaponView view,Vector3 target)
        {
            if(lastFrame==Time.frameCount||!Finite(target))return;
            if((aim==null||left==null)&&Time.frameCount>=nextAttempt)
            {Bind(player,view);nextAttempt=Time.frameCount+30;}
            original.Clear();foreach(var bone in bones)if(bone!=null)original.Add((bone,bone.localPosition,bone.localRotation));
            lastFrame=Time.frameCount;
            try
            {
                view.UpdatePose(player);
                if(aim!=null){aim.IKPosition=target;aim.Update();}
                view.UpdatePose(player);
                if(left!=null&&view.LeftGrip(out var position,out var rotation))
                {
                    left.IKPosition=position;left.IKRotation=rotation;left.Update();
                    leftError=(left.bone3.transform.position-position).magnitude;
                    leftStatus="native-left-grip-solved";
                }
                else if(left!=null)leftStatus="item-left-grip-unavailable";
            }
            catch{Restore();throw;}
        }
        void Restore()
        {
            // If Unity has already evaluated a newer animation frame, keep it.
            if(lastFrame==Time.frameCount)foreach(var pose in original)if(pose.Bone!=null)
            {pose.Bone.localPosition=pose.Position;pose.Bone.localRotation=pose.Rotation;}
        }
        internal object Diagnostics()=>new{aim=aimStatus,left=leftStatus,bones=bones.Count,lastFrame,leftGripError=leftError};
        internal void Release(bool restore=true)
        {if(restore)Restore();original.Clear();bones.Clear();aim=null;left=null;lastFrame=-1;nextAttempt=0;aimStatus=leftStatus="released";leftError=0;}
        public void Dispose()=>Release();
    }
}
