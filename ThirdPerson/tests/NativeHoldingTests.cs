using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Gear;
using GameData;
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
        GameDataBlockBase<GearCategoryDataBlock>.Blocks.Clear();GameDataBlockBase<MeleeArchetypeDataBlock>.Blocks.Clear();GameDataBlockBase<MeleeAnimationSetDataBlock>.Blocks.Clear();
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
        internal GlueGun GlueGun()
        {
            var glue=Node("glue",Item.transform.parent).AddComponent<GlueGun>();
            glue.pItemData=new(){slot=InventorySlot.GearClass};
            glue.ItemDataBlock.inventorySlot=InventorySlot.GearClass;
            glue.LeftHandGripAnim="Delivery_Glue_Distance_1";
            glue.RightHandGripAnim="Tool_Grip_3";
            glue.RightHandGripTrans=Node("rightGrip",glue.transform).transform;
            glue.LeftHandGripTrans=Node("leftGrip",glue.transform).transform;
            glue.LeftHandGripTrans.localPosition=new(-.15f,0,.5f);
            var mesh=Node("mesh",glue.transform);mesh.AddComponent<MeshFilter>().sharedMesh=new();mesh.AddComponent<MeshRenderer>();
            return glue;
        }
        internal WorldWeaponView View(){var v=new WorldWeaponView();Check(v.Bind(Item));return v;}
        internal Gear.MeleeWeaponFirstPerson Spear()
        {
            var spear=Node("spear",Item.transform.parent).AddComponent<Gear.MeleeWeaponFirstPerson>();
            spear.pItemData=new(){slot=InventorySlot.GearMelee};
            spear.ItemDataBlock.inventorySlot=InventorySlot.GearMelee;
            spear.ItemDataBlock.persistentID=999;
            spear.LeftHandGripAnim=spear.RightHandGripAnim="Spear_Idle";
            spear.RightHandGripTrans=Node("rightGrip",spear.transform).transform;
            spear.LeftHandGripTrans=Node("leftGrip",spear.transform).transform;
            spear.LeftHandGripTrans.localPosition=new(-.15f,0,.5f);
            var mesh=Node("mesh",spear.transform);mesh.AddComponent<MeshFilter>().sharedMesh=new();mesh.AddComponent<MeshRenderer>();
            return spear;
        }
        internal void NoFpsChanges()=>Check(Fps.PlayCalls==0&&Fps.WeightCalls==0&&NativeAim.Updates==0&&NativeLeft.Updates==0,"live FPS/native IK instance was modified");
    }
    static void Main(string[] args)
    {
        Case("long-range-flashlight-uses-native-aim-offset-and-authored-hands",()=>{
            var f=new Fixture();
            var item=Node("flashlight").AddComponent<ItemEquippable>();
            item.pItemData=new(){slot=InventorySlot.Consumable};
            item.ItemDataBlock=new(){inventorySlot=InventorySlot.Consumable,persistentID=30};
            item.LeftHandGripAnim=item.RightHandGripAnim="FlashlightMedium";
            int left=f.Add(7,"FlashlightMedium"),right=f.Add(8,"FlashlightMedium");
            f.Add(5,"FlashlightMedium");
            using var holding=new NativeBodyHolding();
            Check(NativeBodyHolding.IsLongRangeFlashlight(item));
            Check(!NativeBodyHolding.SupportsThrow(item));
            holding.Tick(f.Player,item,allowEquipment:true);
            Check(holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None));
            Check(f.Body.States[7].shortNameHash==left&&f.Body.States[8].shortNameHash==right);
            Check(f.Body.GetLayerWeight(4)==1&&f.Body.States[4].shortNameHash==Animator.StringToHash("RifleAimOffset"));
            Check(f.Body.GetLayerWeight(5)==0);
            holding.Release();
            Check(f.Body.GetLayerWeight(4)==0&&f.Body.GetLayerWeight(5)==1);
            f.NoFpsChanges();
        });
        Case("consumable-placement-plays-once-and-restores-authored-holding",()=>{
            var f=new Fixture();
            var item=Node("placement").AddComponent<ItemEquippable>();
            item.pItemData=new(){slot=InventorySlot.Consumable};
            item.ItemDataBlock=new(){inventorySlot=InventorySlot.Consumable};
            item.LeftHandGripAnim=item.RightHandGripAnim="Tripmine";
            f.Add(7,"Tripmine");f.Add(8,"Tripmine");
            int idle=f.Add(5,"Tripmine"),place=f.Add(5,"Consumable_Mine_Use");
            using var holding=new NativeBodyHolding();
            holding.Tick(f.Player,item,allowEquipment:true);
            holding.Tick(f.Player,item,allowEquipment:true,placement:true);
            Check(holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None,placement:true));
            Check(!holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None));
            Check(f.Body.States[5].shortNameHash==place);
            int plays=f.Body.PlayCalls;
            Time.frameCount++;
            holding.Tick(f.Player,item,allowEquipment:true,placement:true);
            Check(f.Body.PlayCalls==plays);
            holding.Tick(f.Player,item,allowEquipment:true);
            Check(holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None));
            Check(f.Body.States[5].shortNameHash==idle);
            f.NoFpsChanges();
        });
        foreach(var (slot,grip) in new[]{(InventorySlot.Consumable,"FlashlightMedium"),
            (InventorySlot.ConsumableHeavy,"HeavyItem"),(InventorySlot.GearClass,"mapper_c_fp_arms"),
            (InventorySlot.HackingTool,"hackingtool_idle"),(InventorySlot.InLevelCarry,"Generator_Cell")})
        Case($"equipment-{slot}-keeps-its-own-hand-pose-across-frames",()=>{
            var f=new Fixture();
            var item=Node("equipment").AddComponent<ItemEquippable>();
            item.pItemData=new(){slot=slot};
            item.ItemDataBlock=new(){inventorySlot=slot};
            item.LeftHandGripAnim=item.RightHandGripAnim=grip;
            int left=f.Add(7,grip),right=f.Add(8,grip),upper=f.Add(5,grip);
            using var holding=new NativeBodyHolding();
            holding.Tick(f.Player,item,allowEquipment:true);
            Check(holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None));
            Check(f.Body.States[7].shortNameHash==left&&f.Body.States[8].shortNameHash==right);
            Check(f.Body.States[5].shortNameHash==upper&&f.Body.GetLayerWeight(5)==1);
            int bound=holding.BoundFrame;
            int plays=f.Body.PlayCalls;
            Time.frameCount++;
            holding.Tick(f.Player,item,allowEquipment:true);
            Check(holding.BoundFrame==bound&&f.Body.PlayCalls==plays);
            Check(holding.IsShowingEquipment(item,pPlayerThrowStatus.StatusEnum.None));
            item.pItemData=new(){slot=InventorySlot.Pickup};
            Check(!NativeBodyHolding.SupportsEquipmentHolding(item));
        });
        foreach(var (id,grip,prefix) in new (uint,string,string)[]{
            (114,"GlowStick","Consumable"),(117,"FogRepeller","Fogrepeller")})
        Case($"consumable-{id}-plays-native-throw-phases-and-restores-holding",()=>{
            var f=new Fixture();
            var item=Node("consumable").AddComponent<ItemEquippable>();
            item.pItemData=new(){slot=InventorySlot.Consumable};
            item.ItemDataBlock=new(){persistentID=id,inventorySlot=InventorySlot.Consumable};
            item.LeftHandGripAnim=item.RightHandGripAnim=grip;
            f.Add(7,grip);f.Add(8,grip);
            int charge=f.Add(5,prefix+"_Throw_Charge"),release=f.Add(5,prefix+"_Throw");
            using var holding=new NativeBodyHolding();
            void Tick(pPlayerThrowStatus.StatusEnum action){
                Time.frameCount++;
                holding.Tick(f.Player,item,allowEquipment:true,throwAction:action);
                Check(holding.IsShowingEquipment(item,action));
            }
            Check(NativeBodyHolding.SupportsThrow(item));
            Tick(pPlayerThrowStatus.StatusEnum.None);
            Tick(pPlayerThrowStatus.StatusEnum.Charging);
            Check(f.Body.States[5].shortNameHash==charge&&f.Body.GetLayerWeight(5)==1);
            int plays=f.Body.PlayCalls;
            Tick(pPlayerThrowStatus.StatusEnum.Charging);
            Check(f.Body.PlayCalls==plays);
            Tick(pPlayerThrowStatus.StatusEnum.None);
            Check(f.Body.GetLayerWeight(5)==0);
            Tick(pPlayerThrowStatus.StatusEnum.Throw);
            Check(f.Body.States[5].shortNameHash==release&&f.Body.GetLayerWeight(5)==1);
            Tick(pPlayerThrowStatus.StatusEnum.None);
            holding.Release();
            Check(f.Body.States[5].shortNameHash==Animator.StringToHash("old-5"));
            f.NoFpsChanges();
        });
        foreach(var (id,grip) in new (uint,string)[]{(101,"AmmoPack_idle"),(102,"MediPack_idle"),
            (127,"AmmoPack_idle"),(132,"DisinfectionPack_idle")})
        Case($"resource-pack-{id}-uses-item-grip-and-native-take-give",()=>{
            var f=new Fixture();
            var pack=Node("resource-pack").AddComponent<ResourcePackFirstPerson>();
            pack.pItemData=new(){slot=InventorySlot.ResourcePack};
            pack.ItemDataBlock=new(){persistentID=id,inventorySlot=InventorySlot.ResourcePack};
            pack.LeftHandGripAnim=pack.RightHandGripAnim=grip;
            pack.m_interactApplyResource=new();
            int left=f.Add(7,grip),right=f.Add(8,grip);
            int take=f.Add(5,"ConsumablePack_Take"),give=f.Add(5,"ConsumablePack_Give");
            using var holding=new NativeBodyHolding();
            void Tick(NativeBodyHolding.ResourcePackAction expected) {
                Time.frameCount++;
                var action=NativeBodyHolding.ReadResourcePackAction(pack);
                Check(action==expected);
                Check(!holding.IsShowingResourcePack(pack,action));
                holding.Tick(f.Player,pack,packAction:action);
                Check(holding.IsShowingResourcePack(pack,action));
            }
            Check(NativeBodyHolding.SupportsResourcePack(pack));
            Tick(NativeBodyHolding.ResourcePackAction.Holding);
            Check(f.Body.GetLayerWeight(5)==0);
            Check(f.Body.States[7].shortNameHash==left&&f.Body.States[8].shortNameHash==right);
            pack.m_actionReceiver=new(){IsLocallyOwned=true};
            pack.m_interactApplyResource.TimerIsActive=true;
            Tick(NativeBodyHolding.ResourcePackAction.Take);
            Check(f.Body.States[5].shortNameHash==take&&f.Body.GetLayerWeight(5)==1);
            int plays=f.Body.PlayCalls;
            Tick(NativeBodyHolding.ResourcePackAction.Take);
            Check(f.Body.PlayCalls==plays);
            pack.m_interactApplyResource.TimerIsActive=false;
            Tick(NativeBodyHolding.ResourcePackAction.Holding);
            Check(f.Body.GetLayerWeight(5)==0);
            pack.m_actionReceiver.IsLocallyOwned=false;
            pack.m_interactApplyResource.TimerIsActive=true;
            Tick(NativeBodyHolding.ResourcePackAction.Give);
            Check(f.Body.States[5].shortNameHash==give);
            holding.Tick(f.Player,f.Item);
            Check(!holding.IsShowingResourcePack(pack,NativeBodyHolding.ResourcePackAction.Give));
            Check(f.Body.States[5].shortNameHash==Animator.StringToHash("old-5"));
            f.NoFpsChanges();
        });
        Case("resource-pack-missing-receiver-or-clip-does-not-claim-action",()=>{
            var f=new Fixture();
            var pack=Node("resource-pack").AddComponent<ResourcePackFirstPerson>();
            pack.pItemData=new(){slot=InventorySlot.ResourcePack};
            pack.ItemDataBlock=new(){persistentID=101,inventorySlot=InventorySlot.ResourcePack};
            pack.LeftHandGripAnim=pack.RightHandGripAnim="AmmoPack_idle";
            pack.m_interactApplyResource=new(){TimerIsActive=true};
            f.Add(7,"AmmoPack_idle");f.Add(8,"AmmoPack_idle");
            using var holding=new NativeBodyHolding();
            var action=NativeBodyHolding.ReadResourcePackAction(pack);
            Check(action==NativeBodyHolding.ResourcePackAction.Unknown);
            holding.Tick(f.Player,pack,packAction:action);
            Check(!holding.IsShowingResourcePack(pack,action));
            pack.m_actionReceiver=new(){IsLocallyOwned=true};
            action=NativeBodyHolding.ReadResourcePackAction(pack);
            holding.Tick(f.Player,pack,packAction:action);
            Check(!holding.IsShowingResourcePack(pack,action)&&holding.Status=="resource-pack-action-state-unavailable");
            pack.ItemDataBlock.persistentID=999;
            Check(!NativeBodyHolding.SupportsResourcePack(pack));
            pack.ItemDataBlock.persistentID=101;
            pack.pItemData=new(){slot=InventorySlot.GearSpecial};
            Check(!NativeBodyHolding.SupportsResourcePack(pack));
        });
        Case("melee-shove-uses-native-states-once-and-resumes-holding",()=>{
            var f=new Fixture();var spear=f.Spear();
            spear.ItemDataBlock.publicName="Spear";
            int idle=f.Add(5,"Spear_Idle");
            f.Add(7,"Spear_Idle");f.Add(8,"Spear_Idle");
            int stand=f.Add(5,"Spear_Stand_Shove"),crouch=f.Add(5,"Spear_Crouch_Shove");
            using var holding=new NativeBodyHolding();using var view=new WorldWeaponView();
            Check(view.Bind(spear));
            holding.Tick(f.Player,spear,allowMelee:true,action:NativeBodyHolding.MeleeAction.Shove);
            Check(holding.IsShowingShove(spear)&&f.Body.States[5].shortNameHash==stand);
            holding.Solve(view,new(0,1,100));
            Check(IKSolver.Instances.OfType<IKSolverLimb>().Single()==f.NativeLeft);
            int plays=f.Body.PlayCalls;
            Time.frameCount++;
            Check(!holding.IsShowingShove(spear));
            holding.Tick(f.Player,spear,allowMelee:true,action:NativeBodyHolding.MeleeAction.Shove);
            Check(holding.IsShowingShove(spear)&&f.Body.PlayCalls==plays);
            holding.Tick(f.Player,spear,allowMelee:true);
            Check(!holding.IsShowingShove(spear)&&f.Body.States[5].shortNameHash==idle);
            holding.Solve(view,new(0,1,100));
            Check(IKSolver.Instances.OfType<IKSolverLimb>().Single(s=>s!=f.NativeLeft).Updates==1);
            f.Player.Locomotion.Crouch=.5f;
            Time.frameCount++;
            holding.Tick(f.Player,spear,allowMelee:true,action:NativeBodyHolding.MeleeAction.Shove);
            Check(holding.IsShowingShove(spear)&&f.Body.States[5].shortNameHash==crouch);
            holding.Release();
            Check(!holding.IsShowingShove(spear)&&f.Body.States[5].shortNameHash==Animator.StringToHash("old-5"));
            f.NoFpsChanges();
        });
        Case("missing-native-shove-state-does-not-claim-presentation",()=>{
            var f=new Fixture();var spear=f.Spear();
            spear.ItemDataBlock.publicName="Spear";
            f.Add(5,"Spear_Idle");f.Add(7,"Spear_Idle");f.Add(8,"Spear_Idle");
            using var holding=new NativeBodyHolding();
            holding.Tick(f.Player,spear,allowMelee:true,action:NativeBodyHolding.MeleeAction.Shove);
            Check(!holding.IsShowingShove(spear)&&holding.Status=="native-shove-state-unavailable");
            f.NoFpsChanges();
        });
        Case("melee-uses-equipped-animation-set-through-swing-charge-release-and-shove",()=>{
            var f=new Fixture();var spear=f.Spear();spear.ItemDataBlock.publicName="Spear";
            int idle=f.Add(5,"Spear_Idle");
            f.Add(7,"Spear_Idle");f.Add(8,"Spear_Idle");
            int shove=f.Add(5,"Spear_Crouch_Shove");
            spear.GearIDRange=new(){Category=10};
            GameDataBlockBase<GearCategoryDataBlock>.Blocks[10]=new(){MeleeArchetype=20};
            GameDataBlockBase<MeleeArchetypeDataBlock>.Blocks[20]=new(){MeleeAnimationSet=30};
            MeleeAnimationSetDataBlock.AnimHash Anim(string name){f.Add(5,name);return new(){Name=name};}
            var data=new MeleeAnimationSetDataBlock{
                TPAnimHashAttackLeft=Anim("data-swing"),TPAnimHashAttackLeftCrouch=Anim("data-swing-crouch"),
                TPAnimHashAttackLeftCharge=Anim("data-charge"),TPAnimHashAttackLeftChargeCrouch=Anim("data-charge-crouch"),
                TPAnimHashAttackLeftRelease=Anim("data-release"),TPAnimHashAttackLeftReleaseCrouch=Anim("data-release-crouch"),
                TPAnimHashIdle=Anim("Spear_Idle"),TPAnimHashIdleCrouch=Anim("data-idle-crouch")
            };
            GameDataBlockBase<MeleeAnimationSetDataBlock>.Blocks[30]=data;
            using var holding=new NativeBodyHolding();
            void Apply(eMeleeWeaponState state,int expected) {
                Time.frameCount++;
                var action=NativeBodyHolding.SelectMeleeAction(state);
                Check(!holding.IsShowingMeleeAction(spear,action));
                holding.Tick(f.Player,spear,allowMelee:true,action:action);
                Check(holding.IsShowingMeleeAction(spear,action)&&f.Body.States[5].shortNameHash==expected);
            }
            Apply(eMeleeWeaponState.AttackHitRight,data.TPAnimHashAttackLeft.GetHash());
            int plays=f.Body.PlayCalls;
            Apply(eMeleeWeaponState.AttackMissRight,data.TPAnimHashAttackLeft.GetHash());
            Check(f.Body.PlayCalls==plays);
            Apply(eMeleeWeaponState.AttackMissLeft,data.TPAnimHashAttackLeft.GetHash());
            Check(f.Body.PlayCalls==plays+1);
            Apply(eMeleeWeaponState.Idle,idle);
            Apply(eMeleeWeaponState.AttackChargeUpRight,data.TPAnimHashAttackLeftCharge.GetHash());
            f.Player.Locomotion.Crouch=1;
            Apply(eMeleeWeaponState.AttackChargeUpRight,data.TPAnimHashAttackLeftCharge.GetHash());
            Apply(eMeleeWeaponState.AttackChargeReleaseRight,data.TPAnimHashAttackLeftReleaseCrouch.GetHash());
            plays=f.Body.PlayCalls;
            Apply(eMeleeWeaponState.AttackChargeHitRight,data.TPAnimHashAttackLeftReleaseCrouch.GetHash());
            Check(f.Body.PlayCalls==plays);
            Apply(eMeleeWeaponState.Idle,idle);
            Apply(eMeleeWeaponState.AttackChargeUpLeft,data.TPAnimHashAttackLeftChargeCrouch.GetHash());
            Apply(eMeleeWeaponState.CancelChargeToIdleLeft,data.TPAnimHashIdleCrouch.GetHash());
            Apply(eMeleeWeaponState.AttackChargeUpLeft,data.TPAnimHashAttackLeftChargeCrouch.GetHash());
            Apply(eMeleeWeaponState.Push,shove);
            Apply(eMeleeWeaponState.Idle,idle);
            holding.Release();
            Check(f.Body.States[5].shortNameHash==Animator.StringToHash("old-5"));
            f.NoFpsChanges();
        });
        Case("melee-attack-without-equipped-animation-data-does-not-claim-presentation",()=>{
            var f=new Fixture();var spear=f.Spear();
            int idle=f.Add(5,"Spear_Idle");f.Add(7,"Spear_Idle");f.Add(8,"Spear_Idle");
            var action=NativeBodyHolding.SelectMeleeAction(eMeleeWeaponState.AttackChargeReleaseLeft);
            using var holding=new NativeBodyHolding();
            holding.Tick(f.Player,spear,allowMelee:true,action:action);
            Check(!holding.IsShowingMeleeAction(spear,action)&&f.Body.States[5].shortNameHash==idle);
            Check(holding.Status=="native-melee-animation-set-unavailable");f.NoFpsChanges();
        });
        Case("spear-holding-uses-observed-weights-and-only-left-ik",()=>{
            var f=new Fixture();var spear=f.Spear();
            f.Player.Locomotion.Movement="Spear_Movement";
            f.Add(1,"Spear_Movement");f.Add(5,"Spear_Idle");int hand=f.Add(7,"Spear_Idle");f.Add(8,"Spear_Idle");
            f.Body.Weights[4]=f.Body.Weights[6]=1;
            using var holding=new NativeBodyHolding();using var view=new WorldWeaponView();
            holding.Tick(f.Player,spear);Check(!holding.Active);
            holding.Tick(f.Player,spear,allowMelee:true);
            Check(holding.IsBoundTo(spear)&&holding.Status=="native-spear-holding-layers");
            Check(f.Body.GetLayerWeight(1)==1&&f.Body.GetLayerWeight(4)==0&&f.Body.GetLayerWeight(5)==1&&f.Body.GetLayerWeight(6)==0);
            Check(f.Body.States[7].shortNameHash==hand&&f.Body.States[8].shortNameHash==hand);
            Check(view.Bind(spear));holding.Solve(view,new(0,1,100));
            Check(IKSolver.Instances.OfType<IKSolverAim>().Single()==f.NativeAim);
            var left=IKSolver.Instances.OfType<IKSolverLimb>().Single(s=>s!=f.NativeLeft);
            Check(view.LeftGrip(out var target,out _));Near(left.IKPosition,target);
            f.NoFpsChanges();holding.Release();
            Check(f.Body.GetLayerWeight(4)==1&&f.Body.GetLayerWeight(6)==1);
            Check(f.Body.States[7].shortNameHash==Animator.StringToHash("old-7")&&f.Body.States[8].shortNameHash==Animator.StringToHash("old-8"));
        });
        Case("spear-profile-skips-other-items-and-missing-hand-states",()=>{
            var f=new Fixture();var spear=f.Spear();f.Add(7,"Spear_Idle");
            using var holding=new NativeBodyHolding();
            spear.ThirdPersonFullbodyMovement="Unknown";holding.Tick(f.Player,spear,allowMelee:true);
            Check(!holding.Active&&holding.Status=="unsupported-equipment");
            spear.ThirdPersonFullbodyMovement="Spear";holding.Tick(f.Player,spear,allowMelee:true);
            Check(!holding.Active&&holding.Status=="melee-grip-states-unavailable");
            Check(f.Body.PlayCalls==0&&f.Body.WeightCalls==0);
        });
        Case("sledgehammer-plays-observed-upperbody-state-and-restores-it",()=>{
            var f=new Fixture();var hammer=f.Spear();
            hammer.ItemDataBlock.persistentID=100;
            hammer.ThirdPersonFullbodyMovement="Melee";
            hammer.LeftHandGripAnim=hammer.RightHandGripAnim="SledgeHammer_fp_arms";
            f.Player.Locomotion.Movement="Melee_Movement";
            f.Add(1,"Melee_Movement");f.Add(7,hammer.LeftHandGripAnim);f.Add(8,hammer.RightHandGripAnim);
            using var holding=new NativeBodyHolding();
            holding.Tick(f.Player,hammer,allowMelee:true);
            Check(!holding.Active&&holding.Status=="melee-upperbody-state-unavailable"&&f.Body.PlayCalls==0&&f.Body.WeightCalls==0);
            int upper=f.Add(5,"Sledgehammer_Idle");
            f.Body.Weights[6]=1;
            holding.Tick(f.Player,hammer,allowMelee:true);
            Check(holding.IsBoundTo(hammer)&&holding.Status=="native-sledgehammer-holding-layers");
            Check(f.Body.States[5].shortNameHash==upper&&f.Body.GetLayerWeight(5)==1&&f.Body.GetLayerWeight(6)==0);
            Check(f.Body.GetLayerWeight(4)==0&&f.Body.States[7].shortNameHash==Animator.StringToHash(hammer.LeftHandGripAnim));
            holding.Release();f.NoFpsChanges();
            Check(f.Body.States[5].shortNameHash==Animator.StringToHash("old-5")&&f.Body.GetLayerWeight(6)==1);
        });
        Case("knife-and-bat-share-movement-but-use-their-own-hands-without-left-ik",()=>{
            var f=new Fixture();var item=f.Spear();
            item.ThirdPersonFullbodyMovement="Knife";
            f.Player.Locomotion.Movement="Knife_Movement";f.Add(1,"Knife_Movement");
            using var holding=new NativeBodyHolding();using var view=new WorldWeaponView();Check(view.Bind(item));
            foreach(var pair in new[]{("Knife_Idle","native-knife-holding-layers"),("Bat_Idle","native-bat-holding-layers")})
            {
                item.LeftHandGripAnim=item.RightHandGripAnim=pair.Item1;
                var upper=f.Add(5,pair.Item1);f.Add(7,pair.Item1);f.Add(8,pair.Item1);
                holding.Tick(f.Player,item,allowMelee:true);
                Check(holding.Status==pair.Item2&&f.Body.States[5].shortNameHash==upper);
                holding.Solve(view,new(0,1,100));
                Check(IKSolver.Instances.Count==2);
            }
            f.NoFpsChanges();
        });
        Case("native-firearm-layers-and-finger-data-drive-body-only",()=>{
            var f=new Fixture();using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);
            Check(holding.Active&&f.Body.GetLayerWeight(4)==1&&f.Body.GetLayerWeight(5)==0&&f.Body.GetLayerWeight(7)==1&&f.Body.GetLayerWeight(8)==1);
            Check(f.Body.States[4].shortNameHash==Animator.StringToHash("RifleAimOffset"));f.NoFpsChanges();
        });
        Case("aim-offset-selector-matches-native-item-data-slot",()=>{
            var f=new Fixture();f.Item.ItemDataBlock.inventorySlot=InventorySlot.GearStandard;
            using var holding=new NativeBodyHolding();holding.Tick(f.Player,f.Item);Check(f.Body.States[4].shortNameHash==Animator.StringToHash("PistolAimOffset"));
        });
        Case("glue-gun-reuses-observed-layers-and-independent-ik-with-clean-handoff",()=>{
            var f=new Fixture();var glue=f.GlueGun();
            int left=f.Add(7,glue.LeftHandGripAnim),right=f.Add(8,glue.RightHandGripAnim);
            f.Body.Weights[6]=1;
            using var holding=new NativeBodyHolding();using var view=new WorldWeaponView();
            holding.Tick(f.Player,glue);Check(!holding.Active);
            holding.Tick(f.Player,glue,allowGlueGun:true);
            Check(holding.Active&&holding.Status=="native-glue-gun-holding-layers");
            Check(f.Body.GetLayerWeight(4)==1&&f.Body.GetLayerWeight(5)==0&&f.Body.GetLayerWeight(6)==0);
            Check(f.Body.GetLayerWeight(7)==1&&f.Body.GetLayerWeight(8)==1);
            Check(f.Body.States[7].shortNameHash==left&&f.Body.States[8].shortNameHash==right);
            Check(view.Bind(glue));holding.Solve(view,new(0,1,100));
            var owned=IKSolver.Instances.OfType<IKSolverLimb>().Single(s=>s!=f.NativeLeft);
            Check(view.LeftGrip(out var target,out _));Near(owned.IKPosition,target);
            f.NoFpsChanges();holding.Release();
            Check(f.Body.GetLayerWeight(4)==0&&f.Body.GetLayerWeight(5)==1&&f.Body.GetLayerWeight(6)==1);
            Check(f.Body.States[7].shortNameHash==Animator.StringToHash("old-7")&&f.Body.States[8].shortNameHash==Animator.StringToHash("old-8"));
            holding.Tick(f.Player,f.Item);
            Check(holding.Status=="native-synced-firearm-layers"&&f.Body.GetLayerWeight(6)==1);
        });
        Case("glue-gun-with-missing-hand-state-leaves-body-unchanged",()=>{
            var f=new Fixture();var glue=f.GlueGun();f.Add(7,glue.LeftHandGripAnim);
            using var holding=new NativeBodyHolding();holding.Tick(f.Player,glue,allowGlueGun:true);
            Check(!holding.Active&&holding.Status=="glue-gun-grip-states-unavailable");
            Check(f.Body.PlayCalls==0&&f.Body.WeightCalls==0);f.NoFpsChanges();
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
