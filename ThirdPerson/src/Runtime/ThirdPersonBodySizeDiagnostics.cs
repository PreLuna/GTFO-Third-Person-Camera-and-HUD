using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Body;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        // F8 only, outside the display scope. Never activate models, advance animation,
        // change scale, or reposition an agent to obtain these measurements.
        [HideFromIl2Cpp] object BodySizeDiagnostics()
        {
            var rows=new List<object>(4);
            try
            {
                var manager=PlayerManager.Current;
                if(manager==null)return new{frame=Time.frameCount,status="no-player-manager",players=rows};
                var local=Local();
                for(int slot=0;slot<4;slot++)
                {
                    try
                    {
                        var agent=manager.GetPlayerAgentInSlot(slot);
                        if(agent==null)continue;
                        if(agent.IsBeingDestroyed||agent.IsBeingDespawned)
                        {rows.Add(new{slot,status="departing"});continue;}
                        var model=agent.PlayerSyncModel;var animator=agent.AnimatorBody;
                        rows.Add(new{slot,status=model==null?"no-model":"sampled",agent=Id(agent),model=Id(model),
                            isLocal=local!=null&&agent.Pointer==local.Pointer,isBot=agent.Owner?.IsBot,
                            scales=BodyScaleSnapshot.Capture(agent.transform,model?.transform,animator?.transform),
                            agentAncestors=SizeAncestors(agent.transform),modelAncestors=SizeAncestors(model?.transform),
                            skeleton=SizeSkeleton(agent),
                            neckAboveAgent=model?.m_neckBone==null?(float?)null:model.m_neckBone.position.y-agent.transform.position.y,
                            modelOffsetFromAgent=model==null?(SizeVector?)null:SizeVector.From(model.transform.position-agent.transform.position),
                            referenceDepthFromLastMain=model==null||lastBodyFrame<0?(float?)null:
                                Vector3.Dot(model.transform.position-ConvertView.U(lastMotionDisplay.Position),ConvertView.U(lastMotionDisplay.Forward))});
                    }
                    catch(Exception error){rows.Add(new{slot,status="unreadable",error=error.GetBaseException().Message});}
                }
            }
            catch(Exception error){return new{frame=Time.frameCount,status="unreadable",error=error.GetBaseException().Message,players=rows};}
            return new{frame=Time.frameCount,status="sampled",scopeActive=scope!=null,lastMainFrame=lastBodyFrame,
                lastMainFrameAge=lastBodyFrame<0?(int?)null:Time.frameCount-lastBodyFrame,players=rows,
                note="Current roster sampled outside main rendering; main-camera depth is a historical reference. Compare standing, stationary poses. Bone lengths measure skeleton size, not outfit bulk or collider size. Missing bones remain null."};
        }
        [HideFromIl2Cpp] static object SizeAncestors(Transform value)
        {
            var rows=new List<object>(8);
            for(int i=0;i<8&&value!=null;i++,value=value.parent)
                rows.Add(new{name=value.name,sample=TransformSizeSample.Capture(value)});
            return new{chain=rows,truncated=value!=null};
        }
        [HideFromIl2Cpp] static object SizeSkeleton(PlayerAgent agent)
        {
            var animator=agent.AnimatorBody;
            if(animator==null)return new{available=false,reason="no-body-animator"};
            var references=agent.SyncIK?.m_fullbodyBipedIK?.references??agent.SyncIK?.m_bipedIK?.references;
            bool Own(Transform bone)=>bone!=null&&bone.IsChildOf(animator.transform)
                &&(agent.AnimatorArms==null||agent.AnimatorArms.transform.Pointer==animator.transform.Pointer||!bone.IsChildOf(agent.AnimatorArms.transform));
            Transform Bone(HumanBodyBones bone,Transform native)
            {
                if(Own(native))return native;
                var humanoid=animator.isHuman?animator.GetBoneTransform(bone):null;
                return Own(humanoid)?humanoid:null;
            }
            float? Length(Transform a,Transform b)=>a==null||b==null?null:(a.position-b.position).magnitude;
            var head=Bone(HumanBodyBones.Head,references?.head);var hips=Bone(HumanBodyBones.Hips,references?.pelvis);
            var leftUpperLeg=Bone(HumanBodyBones.LeftUpperLeg,references?.leftThigh);var leftLowerLeg=Bone(HumanBodyBones.LeftLowerLeg,references?.leftCalf);var leftFoot=Bone(HumanBodyBones.LeftFoot,references?.leftFoot);
            var rightUpperLeg=Bone(HumanBodyBones.RightUpperLeg,references?.rightThigh);var rightLowerLeg=Bone(HumanBodyBones.RightLowerLeg,references?.rightCalf);var rightFoot=Bone(HumanBodyBones.RightFoot,references?.rightFoot);
            var leftUpperArm=Bone(HumanBodyBones.LeftUpperArm,references?.leftUpperArm);var leftLowerArm=Bone(HumanBodyBones.LeftLowerArm,references?.leftForearm);var leftHand=Bone(HumanBodyBones.LeftHand,references?.leftHand);
            return new{available=head!=null||leftUpperLeg!=null||leftUpperArm!=null,nativeReferences=references!=null,humanoid=animator.isHuman,
                animatorEnabled=animator.enabled,active=animator.gameObject.activeInHierarchy,culling=animator.cullingMode.ToString(),
                head=head==null?(SizeVector?)null:SizeVector.From(head.position),hips=hips==null?(SizeVector?)null:SizeVector.From(hips.position),
                leftFoot=leftFoot==null?(SizeVector?)null:SizeVector.From(leftFoot.position),rightFoot=rightFoot==null?(SizeVector?)null:SizeVector.From(rightFoot.position),
                headToHips=Length(head,hips),headToLeftFoot=Length(head,leftFoot),
                leftUpperLegLength=Length(leftUpperLeg,leftLowerLeg),leftLowerLegLength=Length(leftLowerLeg,leftFoot),
                rightUpperLegLength=Length(rightUpperLeg,rightLowerLeg),rightLowerLegLength=Length(rightLowerLeg,rightFoot),
                leftUpperArmLength=Length(leftUpperArm,leftLowerArm),leftLowerArmLength=Length(leftLowerArm,leftHand),
                upperArmSpan=Length(leftUpperArm,Bone(HumanBodyBones.RightUpperArm,references?.rightUpperArm))};
        }
    }
}
