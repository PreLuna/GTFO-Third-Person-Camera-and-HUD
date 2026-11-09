using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Body;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.Runtime;
using UnityEngine;
using V=System.Numerics.Vector3;

static class BodyPoseTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string why="assertion failed"){if(!ok)throw new Exception(why);}
    static void Same(Vector3 a,Vector3 b){Check((a-b).magnitude<.0005f,"position mismatch");}
    static void Same(Quaternion a,Quaternion b){Check(Quaternion.Angle(a,b)<.02f,"rotation mismatch");}
    static void Case(string name,Action a){try{a();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static Transform Bone(string name,Transform parent,Vector3 position){var o=new GameObject(name);o.transform.SetParent(parent,false);o.transform.localPosition=position;return o.transform;}
    sealed class Fixture
    {
        internal readonly Player.LocalPlayerAgent Player;
        internal readonly Transform RU,RL,RH,LU,LL,LH;
        internal readonly ItemEquippable Item;
        internal readonly Transform[] Bones;
        internal Fixture(bool linked=false)
        {
            Player=new GameObject("player").AddComponent<Player.LocalPlayerAgent>();
            Player.FPSCamera=new GameObject("fps").AddComponent<FPSCamera>();
            var camera=new GameObject("main").AddComponent<Camera>();camera.transform.position=new(0,1.8f,0);
            Player.FPSCamera.m_camera=camera;Player.FPSCamera.m_cullingCamera=camera.gameObject.AddComponent<CullingSystem.C_Camera>();
            var model=new GameObject("body");model.transform.SetParent(linked?camera.transform:Player.transform,false);model.transform.position=Vector3.zero;
            Player.PlayerSyncModel=model.AddComponent<PlayerSyncModelData>();Player.AnimatorBody=model.AddComponent<Animator>();Player.AnimatorBody.isHuman=true;
            Player.AnimatorArms=new GameObject("fpsArms").AddComponent<Animator>();
            RU=Bone("RU",model.transform,new(.22f,1.4f,0));RL=Bone("RL",RU,new(.1f,-.2f,.2f));RH=Bone("RH",RL,new(.02f,-.05f,.295f));
            LU=Bone("LU",model.transform,new(-.22f,1.4f,0));LL=Bone("LL",LU,new(-.12f,-.2f,.2f));LH=Bone("LH",LL,new(.05f,.05f,.25f));
            Bones=new[]{RU,RL,RH,LU,LL,LH};
            var keys=new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand};
            for(int i=0;i<6;i++)Player.AnimatorBody.Bones[keys[i]]=Bones[i];
            Player.PlayerSyncModel.ItemAlign=Bone("ItemAlign",RH,new(.02f,0,.015f));
            Player.PlayerSyncModel.ItemAlign.localRotation=Quaternion.Euler(8,12,-3);
            Item=new GameObject("nativeGun").AddComponent<ItemEquippable>();Item.transform.SetParent(camera.transform,false);
            Item.RightHandGripTrans=Bone("rightGrip",Item.transform,Vector3.zero);Item.RightHandGripTrans.SetPositionAndRotation(new(.32f,1.35f,.45f),Quaternion.Euler(-10,5,3));
            Item.LeftHandGripTrans=Bone("leftGrip",Item.transform,Vector3.zero);Item.LeftHandGripTrans.SetPositionAndRotation(new(.04f,1.35f,.35f),Quaternion.Euler(5,8,10));
        }
        internal NativeGripSample Sample()=>NativeGripSample.Capture(Item);
        internal (Vector3 p,Quaternion q)[] Snapshot()=>Bones.Select(b=>(b.localPosition,b.localRotation)).ToArray();
        internal void Restored((Vector3 p,Quaternion q)[] before){for(int i=0;i<Bones.Length;i++){Same(Bones[i].localPosition,before[i].p);Same(Bones[i].localRotation,before[i].q);}}
        internal RenderScope CameraScope()=>new(Player,new(1,new V(1,2.3f,-2),V.UnitZ,.18f,true,"test"),CameraSettings.Default);
        internal void GenericRefs()
        {
            Player.AnimatorBody.isHuman=false;Player.SyncIK=new(){m_fullbodyBipedIK=new(){references=new(){rightUpperArm=RU,rightForearm=RL,rightHand=RH,leftUpperArm=LU,leftForearm=LL,leftHand=LH}}};
        }
    }
    static void Main(string[] args)
    {
        Case("native-authored-grips-drive-both-arms-and-grip-offset",()=>{
            var f=new Fixture();var before=f.Snapshot();var sample=f.Sample();
            using(var pose=new LocalBodyPoseScope(f.Player,sample)){
                Check(pose.RightApplied&&pose.LeftApplied&&!pose.RightClamped&&!pose.LeftClamped,pose.Status);
                Same(f.Player.PlayerSyncModel.ItemAlign.position,sample.RightPosition);Same(f.Player.PlayerSyncModel.ItemAlign.rotation,sample.RightRotation);Same(f.LH.position,sample.LeftPosition);
            }f.Restored(before);
        });
        Case("ads-and-recoil-samples-produce-different-display-poses",()=>{
            var f=new Fixture();var before=f.Snapshot();Vector3 first;
            using(var pose=new LocalBodyPoseScope(f.Player,f.Sample()))first=f.RH.position;
            f.Item.RightHandGripTrans.position+=new Vector3(0,.06f,-.04f);f.Item.RightHandGripTrans.rotation=Quaternion.Euler(-18,5,3);
            using(var pose=new LocalBodyPoseScope(f.Player,f.Sample())){Check((f.RH.position-first).magnitude>.03f);Check(pose.RightApplied);}
            f.Restored(before);
        });
        Case("capture-before-camera-move-preserves-authored-world-target",()=>{
            var f=new Fixture(true);var sample=f.Sample();var before=f.Snapshot();
            using(var camera=f.CameraScope())using(var pose=new LocalBodyPoseScope(f.Player,sample)){
                Same(f.Player.PlayerSyncModel.ItemAlign.position,sample.RightPosition);Check((f.Item.RightHandGripTrans.position-sample.RightPosition).magnitude>1);
            }f.Restored(before);
        });
        Case("auxiliary-camera-and-main-resume-restore-complete-bone-pose",()=>{
            var f=new Fixture(true);var original=f.Snapshot();var sample=f.Sample();
            using(var camera=f.CameraScope())using(var pose=new LocalBodyPoseScope(f.Player,sample)){
                var displayed=f.Player.PlayerSyncModel.ItemAlign.position;
                for(int i=0;i<3;i++){pose.Suspend();camera.Suspend();f.Restored(original);camera.Resume();pose.Resume();Same(f.Player.PlayerSyncModel.ItemAlign.position,displayed);}
            }f.Restored(original);
        });
        Case("generic-biped-references-work-without-humanoid-avatar",()=>{
            var f=new Fixture();f.GenericRefs();using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(pose.RightApplied&&pose.LeftApplied,pose.Status);
        });
        Case("generic-trigonometric-bone-references-work",()=>{
            var f=new Fixture();f.Player.AnimatorBody.isHuman=false;f.Player.SyncIK=new(){m_rightArmSolver=new(){bone1=new(){transform=f.RU},bone2=new(){transform=f.RL},bone3=new(){transform=f.RH}},m_leftArmSolver=new(){bone1=new(){transform=f.LU},bone2=new(){transform=f.LL},bone3=new(){transform=f.LH}}};
            using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(pose.RightApplied&&pose.LeftApplied,pose.Status);
        });
        Case("missing-native-grip-leaves-body-unchanged",()=>{
            var f=new Fixture();var before=f.Snapshot();f.Item.RightHandGripTrans=null;using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(!pose.RightApplied);f.Restored(before);
        });
        Case("missing-avatar-bones-preserve-native-body",()=>{
            var f=new Fixture();var before=f.Snapshot();f.Player.AnimatorBody.Bones.Clear();using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(!pose.RightApplied);f.Restored(before);
        });
        Case("first-person-source-rig-is-never-retargeted",()=>{
            var f=new Fixture();var before=f.Snapshot();f.Player.AnimatorArms=f.Player.AnimatorBody;using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(pose.Status=="shared-native-rig"&&!pose.RightApplied);f.Restored(before);
        });
        Case("unreachable-grip-clamps-without-changing-bone-lengths",()=>{
            var f=new Fixture();var a=(f.RL.position-f.RU.position).magnitude;var b=(f.RH.position-f.RL.position).magnitude;f.Item.RightHandGripTrans.position=new(30,20,30);
            using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(pose.RightApplied&&pose.RightClamped);Check(Math.Abs((f.RL.position-f.RU.position).magnitude-a)<.0005f&&Math.Abs((f.RH.position-f.RL.position).magnitude-b)<.0005f);
        });
        Case("bone-write-failure-rolls-back-already-applied-upper-arm",()=>{
            var f=new Fixture();var before=f.Snapshot();f.RL.ThrowOnRotationWrite=true;bool failed=false;
            try{using var pose=new LocalBodyPoseScope(f.Player,f.Sample());}catch(InvalidOperationException){failed=true;}Check(failed);f.Restored(before);
        });
        Case("missing-left-grip-keeps-left-arm-native",()=>{
            var f=new Fixture();f.Item.LeftHandGripTrans=null;var q=f.LU.rotation;using var pose=new LocalBodyPoseScope(f.Player,f.Sample());Check(pose.RightApplied&&!pose.LeftApplied);Same(q,f.LU.rotation);
        });
        Case("nan-and-zero-quaternion-grips-are-rejected",()=>{
            var f=new Fixture();f.Item.RightHandGripTrans.position=new(float.NaN,0,0);Check(!f.Sample().Valid);
            f.Item.RightHandGripTrans.position=Vector3.zero;f.Item.RightHandGripTrans.rotation=default;Check(!f.Sample().Valid);
        });
        Case("solver-collinear-and-zero-distance-targets-remain-finite",()=>{
            foreach(var target in new[]{V.Zero,new V(0,0,.5f),new V(0,1,0),new V(0,0,-.5f)}){
                Check(TwoBonePoseSolver.Solve(V.Zero,new V(0,0,.3f),new V(0,0,.6f),target,out var e,out var w,out _));Check(ViewMath.Finite(e)&&ViewMath.Finite(w));
                Check(Math.Abs(e.Length()-.3f)<.0005f&&Math.Abs(V.Distance(e,w)-.3f)<.0005f);
            }
        });
        Case("solver-invalid-and-degenerate-bones-are-rejected",()=>{
            Check(!TwoBonePoseSolver.Solve(V.Zero,V.Zero,V.UnitZ,V.UnitY,out _,out _,out _));Check(!TwoBonePoseSolver.Solve(V.Zero,V.UnitX,V.UnitZ,new(float.NaN,0,0),out _,out _,out _));
        });
        var path=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,JsonSerializer.Serialize(new{Scope="Production arm retargeting with controlled native/Unity stand-ins; no GPU/gameplay proof",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
