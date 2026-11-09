using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Player;
using RootMotion.FinalIK;
using UnityEngine;
using PreLuna.ThirdPerson.Body;
using PreLuna.ThirdPerson.Core;

static class NativeHoldingTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool value,string why="assertion failed"){if(!value)throw new Exception(why);}
    static void Near(Vector3 a,Vector3 b)=>Check((a-b).magnitude<.0001f,"position mismatch");
    static void Same(Quaternion a,Quaternion b)=>Check(Quaternion.Angle(a,b)<.02f,"rotation mismatch");
    static GameObject Node(string name,Transform parent=null){var o=new GameObject(name);o.transform.SetParent(parent,false);return o;}
    static void Case(string name,Action test)
    {
        Time.frameCount=1;Time.time=Time.unscaledTime=0;IKSolver.Instances.Clear();IKSolver.FailNewLimb=false;
        try{test();foreach(OwnedResource r in Enum.GetValues<OwnedResource>())Check(ResourceLedger.Live(r)==0,"resource leak "+r);results.Add(new(name,true,null));}
        catch(Exception e){results.Add(new(name,false,e.ToString()));}
    }
    sealed class Fixture
    {
        internal readonly LocalPlayerAgent Player;
        internal readonly Gear.BulletWeapon Item;
        internal readonly Animator Body,Fps;
        internal readonly Transform Spine,LeftHand;
        internal readonly IKSolverAim NativeAim;
        internal readonly IKSolverLimb NativeLeft;
        internal Fixture(bool rifle=false,bool pistol=false)
        {
            Player=Node("player").AddComponent<LocalPlayerAgent>();
            Player.FPSCamera=Node("FPS",Player.transform).AddComponent<FPSCamera>();
            Player.FPSCamera.m_camera=Player.FPSCamera.gameObject.AddComponent<Camera>();
            var model=Node("nativeBody",Player.transform);Player.PlayerSyncModel=model.AddComponent<PlayerSyncModelData>();
            Body=model.AddComponent<Animator>();Body.AllowPlayback=true;Body.isHuman=true;Player.AnimatorBody=Body;
            Fps=Node("fpsArms",Player.FPSCamera.transform).AddComponent<Animator>();Player.AnimatorArms=Fps;
            Player.FPItemHolder=new();Spine=Node("spine",model.transform).transform;Spine.localPosition=new(0,1,0);
            Transform Bone(string name,Transform parent,Vector3 p){var t=Node(name,parent).transform;t.localPosition=p;return t;}
            var ru=Bone("rightUpper",Spine,new(.2f,.3f,0));var rl=Bone("rightLower",ru,new(.4f,0,0));var rh=Bone("rightHand",rl,new(.3f,0,0));
            var lu=Bone("leftUpper",Spine,new(-.2f,.3f,0));var ll=Bone("leftLower",lu,new(-.4f,0,0));LeftHand=Bone("leftHand",ll,new(-.3f,0,0));
            Body.Bones[HumanBodyBones.RightUpperArm]=ru;Body.Bones[HumanBodyBones.RightLowerArm]=rl;Body.Bones[HumanBodyBones.RightHand]=rh;
            Body.Bones[HumanBodyBones.LeftUpperArm]=lu;Body.Bones[HumanBodyBones.LeftLowerArm]=ll;Body.Bones[HumanBodyBones.LeftHand]=LeftHand;
            Player.PlayerSyncModel.ItemAlign=Node("ItemAlign",rh).transform;
            Player.PlayerSyncModel.ItemAlign.localRotation=Quaternion.Euler(0,23,0);
            NativeAim=new(){bones=new[]{new Bone{transform=Spine,weight=.4f},new Bone{transform=ru,weight=.6f}},axis=new(0,0,1),clampWeight=.3f};
            NativeLeft=new(){bone1=new(){transform=lu},bone2=new(){transform=ll},bone3=new(){transform=LeftHand}};
            Player.SyncIK=new(){m_aimSolver=NativeAim,m_leftArmSolver=NativeLeft};
            var itemNode=Node("realFPSWeapon",Player.FPSCamera.transform);
            Item=rifle?itemNode.AddComponent<Gear.RifleWeapon>():pistol?itemNode.AddComponent<Gear.BulletWeapon>():itemNode.AddComponent<Gear.Shotgun>();
            Item.transform.localPosition=new(.1f,1.5f,.6f);Item.transform.localRotation=Quaternion.Euler(4,12,0);
            Item.RightHandGripTrans=Bone("rightGrip",Item.transform,new(.1f,.05f,0));
            Item.LeftHandGripTrans=Bone("leftGrip",Item.transform,new(-.15f,0,.5f));
            var mesh=Node("mesh",Item.transform);mesh.AddComponent<MeshFilter>().sharedMesh=new();mesh.AddComponent<MeshRenderer>();
            for(int i=0;i<9;i++){
                int hash=Add(i,"old-"+i);Body.States[i]=new(){fullPathHash=hash,shortNameHash=hash,normalizedTime=.4f,length=1,speed=1,speedMultiplier=1};
                Body.Weights[i]=i==1||i==5?1:0;
            }
            foreach(var pair in new[]{(1,"Rifle_Movement"),(1,"Pistol_Movement"),(4,"RifleAimOffset"),(4,"PistolAimOffset"),(7,"LeftGrip"),(8,"RightGrip"),(6,"Pistol_Recoil"),(6,"Rifle_Recoil")})Add(pair.Item1,pair.Item2);
            int move=Animator.StringToHash("Rifle_Movement");Body.States[1]=new(){fullPathHash=move,shortNameHash=move,normalizedTime=.25f,length=1,speed=1,speedMultiplier=1};
        }
        internal int Add(int layer,string name){int hash=Animator.StringToHash(name);Body.AvailableStates.Add((layer,hash));return hash;}
        internal WorldWeaponView View(){var v=new WorldWeaponView();Check(v.Bind(Item));return v;}
        internal void NoFpsChanges()=>Check(Fps.PlayCalls==0&&Fps.WeightCalls==0&&NativeAim.Updates==0&&NativeLeft.Updates==0,"live FPS/native IK instance was modified");
    }
    static void Main(string[] args)
    {
        Case("native-firearm-layers-and-finger-data-drive-body-only",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);
            Check(holding.Active&&f.Body.GetLayerWeight(4)==1&&f.Body.GetLayerWeight(5)==0&&f.Body.GetLayerWeight(7)==1&&f.Body.GetLayerWeight(8)==1);
            Check(f.Body.States[4].shortNameHash==Animator.StringToHash("RifleAimOffset"));f.NoFpsChanges();
        });
        Case("aim-offset-selector-matches-native-item-data-slot",()=>{
            var f=new Fixture();f.Item.ItemDataBlock.inventorySlot=InventorySlot.GearStandard;
            using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(f.Body.States[4].shortNameHash==Animator.StringToHash("PistolAimOffset"));
        });
        Case("movement-and-hand-states-come-from-native-equipment-data",()=>{
            var f=new Fixture();f.Player.Locomotion.Movement="CustomMovement";f.Add(1,"CustomMovement");f.Item.LeftHandGripAnim="CustomHand";f.Add(7,"CustomHand");
            using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(f.Body.States[1].shortNameHash==Animator.StringToHash("CustomMovement")&&f.Body.States[7].shortNameHash==Animator.StringToHash("CustomHand"));
        });
        Case("steady-ticks-do-not-replay-animations-or-reset-bind-frame",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);int plays=f.Body.PlayCalls,weights=f.Body.WeightCalls,frame=holding.BoundFrame;
            for(int i=0;i<120;i++){Time.frameCount++;Time.time+=1f/60;holding.Tick(f.Player,f.Item);}
            Check(f.Body.PlayCalls==plays&&f.Body.WeightCalls==weights&&holding.BoundFrame==frame);f.NoFpsChanges();
        });
        Case("release-restores-owned-layers-without-rewinding-native-movement",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);
            var movement=f.Body.States[1];movement.normalizedTime=.9f;f.Body.States[1]=movement;Time.time=2;holding.Release();
            Check(f.Body.GetLayerWeight(4)==0&&f.Body.GetLayerWeight(5)==1&&f.Body.States[1].normalizedTime==.9f);
            Check(f.Body.States[7].fullPathHash==Animator.StringToHash("old-7")&&Math.Abs(f.Body.States[7].normalizedTime-2.4f)<.0001f);f.NoFpsChanges();
        });
        Case("unowned-native-additive-change-survives-release",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);int damage=f.Add(6,"nativeDamage");
            f.Body.States[6]=new(){fullPathHash=damage,shortNameHash=damage};f.Body.Weights[6]=.6f;holding.Release();Check(f.Body.States[6].fullPathHash==damage&&f.Body.Weights[6]==.6f);
        });
        Case("shared-fps-body-animator-is-rejected",()=>{
            var f=new Fixture();f.Player.AnimatorArms=f.Body;using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.PlayCalls==0&&f.Body.WeightCalls==0);
        });
        Case("body-under-fps-animator-is-rejected",()=>{
            var f=new Fixture();f.Body.transform.SetParent(f.Fps.transform,true);using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.PlayCalls==0);
        });
        Case("missing-native-state-does-not-write-any-animation",()=>{
            var f=new Fixture();f.Body.AvailableStates.Remove((4,Animator.StringToHash("RifleAimOffset")));using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.PlayCalls==0&&f.Body.WeightCalls==0);
        });
        Case("native-transition-defers-binding-and-recovers",()=>{
            var f=new Fixture();f.Body.Transitions.Add(5);using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(!holding.Active);f.Body.Transitions.Clear();holding.Tick(f.Player,f.Item);Check(holding.Active);
        });
        Case("incomplete-controller-and-layer-layout-defer-safely",()=>{
            var f=new Fixture();f.Body.layerCount=6;using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(!holding.Active);f.Body.layerCount=9;f.Body.runtimeAnimatorController=null;holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.PlayCalls==0);
        });
        Case("reload-restores-original-body-and-does-not-replay-old-fire",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);f.Item.IsReloading=true;holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.Weights[5]==1);
            f.Item.IsReloading=false;f.Item.m_lastFireTime=10;f.Item.m_clip=6;holding.Tick(f.Player,f.Item);Check(holding.Active&&f.Body.GetLayerWeight(6)==0);
        });
        Case("non-firearm-and-non-gear-slot-keep-native-flow",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();var tool=Node("tool").AddComponent<ItemEquippable>();holding.Tick(f.Player,tool);Check(!holding.Active);
            f.Item.pItemData=new(){slot=InventorySlot.GearMelee};holding.Tick(f.Player,f.Item);Check(!holding.Active&&f.Body.PlayCalls==0);
        });
        Case("observed-native-shot-plays-body-recoil-once-without-ammo-write",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);int before=f.Body.PlayCalls;
            f.Item.m_lastFireTime=1;f.Item.m_clip=9;holding.Tick(f.Player,f.Item);Check(f.Body.PlayCalls==before+1&&f.Body.States[6].shortNameHash==Animator.StringToHash("Rifle_Recoil"));
            holding.Tick(f.Player,f.Item);Check(f.Body.PlayCalls==before+1&&f.Item.m_clip==9&&f.Item.m_lastFireTime==1);f.NoFpsChanges();
        });
        Case("empty-clip-and-pre-entry-fire-do-not-play-recoil",()=>{
            var f=new Fixture();f.Item.m_lastFireTime=4;f.Item.m_clip=0;using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);int before=f.Body.PlayCalls;
            f.Item.m_lastFireTime=5;holding.Tick(f.Player,f.Item);Check(f.Body.PlayCalls==before);
        });
        Case("rifle-override-and-base-firearm-use-native-recoil-states",()=>{
            foreach(bool rifle in new[]{true,false}){
                var f=new Fixture(rifle:rifle,pistol:!rifle);using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);
                f.Item.m_lastFireTime=2;f.Item.m_clip=9;holding.Tick(f.Player,f.Item);
                Check(f.Body.States[6].shortNameHash==Animator.StringToHash(rifle?"Rifle_Recoil":"Pistol_Recoil"));f.NoFpsChanges();
            }
        });
        Case("missing-finger-state-is-optional-and-late-data-rebinds",()=>{
            var f=new Fixture();f.Item.LeftHandGripAnim=null;using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(holding.Active&&f.Body.Weights[7]==0);
            Time.frameCount++;f.Item.LeftHandGripAnim="LeftGrip";holding.Tick(f.Player,f.Item);Check(holding.BoundFrame==Time.frameCount&&f.Body.Weights[7]==1);
        });
        Case("native-owned-solvers-use-body-weapon-target-not-fps-grip",()=>{
            var f=new Fixture();using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);
            var position=f.Item.transform.position;var rotation=f.Item.transform.rotation;var parent=f.Item.transform.parent;var sourceAxis=f.NativeAim.axis;
            holding.Solve(view,new(3,1,10));Check(view.AimReference.parent==f.Player.PlayerSyncModel.ItemAlign);
            var owned=IKSolver.Instances.OfType<IKSolverLimb>().Single(s=>s!=f.NativeLeft);Check(view.LeftGrip(out var target,out var targetRotation));Near(owned.IKPosition,target);Same(owned.IKRotation,targetRotation);
            Check((owned.IKPosition-f.Item.LeftHandGripTrans.position).magnitude>.1f);Near(f.Item.transform.position,position);Same(f.Item.transform.rotation,rotation);Check(f.Item.transform.parent==parent);
            Near(f.NativeAim.axis,sourceAxis);f.NoFpsChanges();
        });
        Case("native-aim-profile-and-bone-weights-are-copied",()=>{
            var f=new Fixture();using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);holding.Solve(view,new(1,2,20));
            var solver=IKSolver.Instances.OfType<IKSolverAim>().Single(s=>s!=f.NativeAim);Check(solver.transform==view.AimReference&&solver.clampWeight==.3f&&solver.bones[0].weight==.4f&&solver.bones[1].weight==.6f);f.NoFpsChanges();
        });
        Case("owned-solvers-run-once-per-native-frame",()=>{
            var f=new Fixture();using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);holding.Solve(view,new(1,2,20));holding.Solve(view,new(1,2,20));
            var solver=IKSolver.Instances.OfType<IKSolverAim>().Single(s=>s!=f.NativeAim);Check(solver.Updates==1);Time.frameCount++;holding.Solve(view,new(1,2,20));Check(solver.Updates==2);
        });
        Case("fps-owned-aim-chain-is-not-solved",()=>{
            var f=new Fixture();f.NativeAim.bones=new[]{new Bone{transform=f.Fps.transform}};using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);holding.Solve(view,new(1,2,20));
            Check(IKSolver.Instances.OfType<IKSolverAim>().Count()==1);f.NoFpsChanges();
        });
        Case("native-ik-failure-restores-body-pose",()=>{
            var f=new Fixture();using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);var spine=f.Spine.localRotation;var hand=f.LeftHand.localRotation;
            IKSolver.FailNewLimb=true;bool failed=false;try{holding.Solve(view,new(1,2,20));}catch{failed=true;}
            Check(failed);Same(f.Spine.localRotation,spine);Same(f.LeftHand.localRotation,hand);f.NoFpsChanges();
        });
        Case("release-does-not-overwrite-a-newer-native-animation-pose",()=>{
            var f=new Fixture();using var view=f.View();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);holding.Solve(view,new(1,2,20));
            Time.frameCount++;var native=Quaternion.Euler(0,39,0);f.Spine.localRotation=native;holding.Release();Same(f.Spine.localRotation,native);
        });
        Case("controller-change-does-not-restore-old-controller-state",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);f.Body.runtimeAnimatorController=new();f.Body.Weights[4]=.7f;f.Body.Weights[5]=.3f;
            holding.Release();Check(f.Body.Weights[4]==.7f&&f.Body.Weights[5]==.3f);
        });
        Case("animation-setup-failure-restores-previous-layers",()=>{
            var f=new Fixture();f.Body.ThrowOnLayer=7;using var holding=new NativeBodyHolding();bool failed=false;try{holding.Tick(f.Player,f.Item);}catch{failed=true;}
            Check(failed&&!holding.Active&&f.Body.Weights[5]==1&&f.Body.States[4].fullPathHash==Animator.StringToHash("old-4"));f.NoFpsChanges();
        });
        var output=args.Length>0?args[0]:"NativeHoldingTests.json";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output,JsonSerializer.Serialize(new{Scope="Production body animation and native-IK adapter with stand-ins; no Unity/native IK/GPU execution",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
