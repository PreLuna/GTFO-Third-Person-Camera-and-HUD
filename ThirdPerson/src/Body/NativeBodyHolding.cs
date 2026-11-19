using System;
using System.Collections.Generic;
using GameData;
using Gear;
using Player;
using UnityEngine;
using Il2CppInterop.Runtime;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    // Body-only holding pose for supported equipment.
    // Never call DoEquipItem/PlayAnimationsForWieldedItem on the live inventory:
    // those also change FPS animators, real equipment and gameplay IK targets.
    internal sealed class NativeBodyHolding : IDisposable
    {
        internal enum MeleeAction { None, Idle, SwingLeft, SwingRight, Charge, Release, Cancel, Shove }
        internal enum ResourcePackAction { None, Holding, Take, Give, Unknown }
        enum MeleePose { None, Spear, Sledgehammer, Knife, Bat }
        readonly record struct Layer(int Index,float Weight,int State,float Time,float Length,float Speed);
        readonly List<Layer> saved=new();
        readonly HashSet<int> played=new(),weighted=new();
        readonly NativeBodyIk ik=new();
        Animator animator;
        LocalPlayerAgent player;
        long itemId;
        long controllerId;
        int movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer;
        int movementHash,aimHash,leftHash,rightHash,recoilHash;
        int upperIdleHash,shoveHash,shoveFrame=-1,shovePlays;
        MeleeAnimationSetDataBlock meleeAnimations;
        MeleeAction shownAttack;
        int attackHash,attackFrame=-1,attackPlays,meleeFrame=-1;
        MeleeAction lastAttackAction;
        string lastAttackState,lastAttackResult;
        ResourcePackAction shownPack, lastPackAction;
        int packFrame=-1,packPlays;
        string lastPackState,lastPackResult;
        pPlayerThrowStatus.StatusEnum shownThrow;
        bool shownPlacement;
        int consumableFrame=-1;
        string throwState;
        string lastShoveState,lastShoveResult;
        string movementName,aimName;
        string leftName,rightName;
        float savedAt,lastFire;
        int lastClip;
        bool leftGripOnly;
        bool solveGrip = true;
        internal bool Active=>animator!=null;
        internal bool IsBoundTo(ItemEquippable item)=>Active&&item!=null&&itemId==item.Pointer.ToInt64();
        internal bool IsShowingShove(ItemEquippable item)=>IsBoundTo(item)&&shoveFrame==Time.frameCount;
        internal bool IsShowingResourcePack(ItemEquippable item,ResourcePackAction action)
            =>IsBoundTo(item)&&packFrame==Time.frameCount&&shownPack==action;
        internal bool IsShowingMeleeAction(ItemEquippable item,MeleeAction action)=>action switch
        {
            MeleeAction.Shove=>IsShowingShove(item),
            MeleeAction.Idle=>IsBoundTo(item)&&meleeFrame==Time.frameCount&&shownAttack==MeleeAction.None&&shoveHash==0,
            _=>IsBoundTo(item)&&attackFrame==Time.frameCount&&shownAttack==action
        };
        internal int BoundFrame {get;private set;}=-1;
        internal string Status {get;private set;}="not-bound";
        int bindings,recoilEvents;
        internal static bool SupportsMelee(ItemEquippable item)
            => GetMeleePose(item)!=MeleePose.None;

        internal static bool SupportsResourcePack(ItemEquippable item)
            =>item?.ItemDataBlock is { persistentID: 101 or 102 or 127 or 132, inventorySlot: InventorySlot.ResourcePack }
                &&item.pItemData.slot==InventorySlot.ResourcePack&&item.TryCast<ResourcePackFirstPerson>()!=null;

        internal static bool SupportsEquipmentHolding(ItemEquippable item)
            =>item?.ItemDataBlock!=null&&item.ItemDataBlock.inventorySlot==item.pItemData.slot
                &&item.pItemData.slot is InventorySlot.Consumable or InventorySlot.ConsumableHeavy
                    or InventorySlot.HackingTool or InventorySlot.GearClass or InventorySlot.InLevelCarry;

        internal static bool SupportsThrow(ItemEquippable item)
            =>SupportsEquipmentHolding(item)&&item.pItemData.slot==InventorySlot.Consumable
                &&item.ItemDataBlock.persistentID is 113 or 114 or 115 or 117 or 130 or 136 or 167 or 174;

        internal static bool IsLongRangeFlashlight(ItemEquippable item)
            =>item?.ItemDataBlock is { persistentID: 30, inventorySlot: InventorySlot.Consumable }
                &&item.pItemData.slot==InventorySlot.Consumable;

        internal bool IsShowingEquipment(ItemEquippable item,pPlayerThrowStatus.StatusEnum action,bool placement=false)
            =>IsBoundTo(item)&&consumableFrame==Time.frameCount&&shownThrow==action&&shownPlacement==placement;

        internal static ResourcePackAction ReadResourcePackAction(ItemEquippable item)
        {
            var pack=item?.TryCast<ResourcePackFirstPerson>();
            var timer=pack?.m_interactApplyResource;
            if(timer==null)return ResourcePackAction.Unknown;
            if(!timer.TimerIsActive)return ResourcePackAction.Holding;
            var receiver=pack.m_actionReceiver;
            return receiver==null?ResourcePackAction.Unknown:receiver.IsLocallyOwned?ResourcePackAction.Take:ResourcePackAction.Give;
        }

        internal static MeleeAction SelectMeleeAction(eMeleeWeaponState state)=>state switch
        {
            eMeleeWeaponState.Idle=>MeleeAction.Idle,
            eMeleeWeaponState.AttackMissLeft or eMeleeWeaponState.AttackHitLeft=>MeleeAction.SwingLeft,
            eMeleeWeaponState.AttackMissRight or eMeleeWeaponState.AttackHitRight=>MeleeAction.SwingRight,
            eMeleeWeaponState.AttackChargeUpLeft or eMeleeWeaponState.AttackChargeUpRight=>MeleeAction.Charge,
            eMeleeWeaponState.AttackChargeReleaseLeft or eMeleeWeaponState.AttackChargeReleaseRight
                or eMeleeWeaponState.AttackChargeHitLeft or eMeleeWeaponState.AttackChargeHitRight=>MeleeAction.Release,
            eMeleeWeaponState.CancelChargeToIdleLeft or eMeleeWeaponState.CancelChargeToIdleRight=>MeleeAction.Cancel,
            eMeleeWeaponState.Push=>MeleeAction.Shove,
            _=>MeleeAction.None
        };

        static MeleePose GetMeleePose(ItemEquippable item)
        {
            if(item?.TryCast<Gear.MeleeWeaponFirstPerson>()==null||item.pItemData.slot!=InventorySlot.GearMelee
                ||item.ItemDataBlock?.inventorySlot!=InventorySlot.GearMelee)return MeleePose.None;
            return (item.ThirdPersonFullbodyMovement.ToString(),item.LeftHandGripAnim,item.RightHandGripAnim) switch
            {
                ("Spear","Spear_Idle","Spear_Idle")=>MeleePose.Spear,
                ("Melee","SledgeHammer_fp_arms","SledgeHammer_fp_arms")=>MeleePose.Sledgehammer,
                ("Knife","Knife_Idle","Knife_Idle")=>MeleePose.Knife,
                ("Knife","Bat_Idle","Bat_Idle")=>MeleePose.Bat,
                _=>MeleePose.None
            };
        }

        internal void Tick(LocalPlayerAgent local,ItemEquippable item,bool allowGlueGun=false,bool allowMelee=false,
            MeleeAction action=MeleeAction.None,ResourcePackAction packAction=ResourcePackAction.None,
            bool allowEquipment=false,pPlayerThrowStatus.StatusEnum throwAction=pPlayerThrowStatus.StatusEnum.None,bool placement=false)
        {
            var gun=item?.TryCast<Gear.BulletWeapon>();
            bool glue=allowGlueGun&&item?.TryCast<GlueGun>()!=null&&item.pItemData.slot==InventorySlot.GearClass
                &&item.ItemDataBlock?.inventorySlot==InventorySlot.GearClass;
            var pose=allowMelee?GetMeleePose(item):MeleePose.None;
            bool melee=pose!=MeleePose.None;
            bool pack=packAction!=ResourcePackAction.None&&SupportsResourcePack(item);
            bool consumable=allowEquipment&&SupportsEquipmentHolding(item);
            bool flashlight=consumable&&IsLongRangeFlashlight(item);
            if(local==null||(!glue&&!melee&&!pack&&!consumable&&gun==null)||item.ItemDataBlock==null||local.Locomotion==null)
            {Release();Status="unsupported-equipment";return;}
            if(item.IsReloading)
            {Release();Status="native-equipment-busy";return;}
            var body=local.AnimatorBody;
            if(body==null||local.AnimatorArms==null||body.Pointer==local.AnimatorArms.Pointer||body.transform.IsChildOf(local.AnimatorArms.transform))
            {Release();Status="body-and-fps-animators-not-separated";return;}
            if(body.runtimeAnimatorController==null){Release();Status="native-controller-not-ready";return;}
            if(!glue&&!melee&&!pack&&!consumable&&item.pItemData.slot is not (InventorySlot.GearStandard or InventorySlot.GearSpecial))
            {Release();Status="unsupported-equipment-slot";return;}
            string move=local.Locomotion.GetAnimationForWielded(PlayerLocomotion.ePlayerAnimation.Movement);
            // Original PlayAnimationsForWieldedItem selects this using the item
            // data slot (native +0x7c), not a guessed weapon/model name.
            string aim=melee||pack||consumable&&!flashlight?null:!glue&&item.ItemDataBlock.inventorySlot==InventorySlot.GearStandard?"PistolAimOffset":"RifleAimOffset";
            if(Active&&(player.Pointer!=local.Pointer||animator.Pointer!=body.Pointer||controllerId!=body.runtimeAnimatorController.Pointer.ToInt64()
                ||itemId!=item.Pointer.ToInt64()||movementName!=move||aimName!=aim||leftName!=item.LeftHandGripAnim||rightName!=item.RightHandGripAnim))Release();
            if(!Active)
            {
                movementLayer=PlayerAgent.ANIM_LAYER_FULL_BODY;aimLayer=PlayerAgent.ANIM_LAYER_3RD_AIM_OFFSET;
                upperLayer=PlayerAgent.ANIM_LAYER_3RD_UPPERBODY;leftLayer=PlayerAgent.ANIM_LAYER_LEFT_HAND;
                rightLayer=PlayerAgent.ANIM_LAYER_RIGHT_HAND;recoilLayer=PlayerAgent.ANIM_LAYER_3RD_ADDITIVE;
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                    if(layer<0||layer>=body.layerCount){Status="native-animation-layers-unavailable";return;}
                int mh=State(body,movementLayer,move),ah=State(body,aimLayer,aim);
                if((!consumable&&mh==0)||(aim!=null&&ah==0))
                {Status="native-holding-states-unavailable";return;}
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                    if(body.IsInTransition(layer)){Status="waiting-for-native-animation-transition";return;}
                int leftState=State(body,leftLayer,item.LeftHandGripAnim),rightState=State(body,rightLayer,item.RightHandGripAnim);
                if((glue||melee||pack||consumable)&&(leftState==0||rightState==0))
                {Status=consumable?"consumable-grip-states-unavailable":pack?"resource-pack-grip-states-unavailable":melee?"melee-grip-states-unavailable":"glue-gun-grip-states-unavailable";return;}
                // The paired native samples use the hand state on the upper body,
                // except for the sledgehammer's distinct body state.
                string upperName=pose==MeleePose.Sledgehammer?"Sledgehammer_Idle":item.RightHandGripAnim;
                // Native non-melee equip uses RifleAimOffset for the flashlight's slot.
                int upperState=melee||consumable&&!flashlight?State(body,upperLayer,upperName):0;
                if(melee&&upperState==0){Status="melee-upperbody-state-unavailable";return;}
                player=local;itemId=item.Pointer.ToInt64();animator=body;savedAt=Time.time;
                leftGripOnly=melee||pack||consumable;
                solveGrip=consumable?item.pItemData.slot==InventorySlot.InLevelCarry:pose is not (MeleePose.Knife or MeleePose.Bat);
                controllerId=body.runtimeAnimatorController.Pointer.ToInt64();leftName=item.LeftHandGripAnim;rightName=item.RightHandGripAnim;
                movementName=move;aimName=aim;movementHash=mh;aimHash=ah;
                upperIdleHash=upperState;
                leftHash=leftState;rightHash=rightState;
                // Exact string literals from BulletWeaponSynced and its rifle /
                // shotgun overrides; independent from the inventory-slot aim selector.
                recoilHash=gun==null?0:State(body,recoilLayer,item.TryCast<Gear.Shotgun>()!=null||item.TryCast<Gear.RifleWeapon>()!=null?"Rifle_Recoil":"Pistol_Recoil");
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                {
                    var state=body.GetCurrentAnimatorStateInfo(layer);
                    saved.Add(new(layer,body.GetLayerWeight(layer),state.fullPathHash,state.normalizedTime,state.length,state.speed*state.speedMultiplier));
                }
                lastFire=gun?.m_lastFireTime??0;lastClip=gun?.m_clip??0;BoundFrame=Time.frameCount;bindings++;
                try
                {
                    if(movementHash!=0)PlayIfDifferent(movementLayer,movementHash);
                    if(aim!=null)PlayIfDifferent(aimLayer,aimHash);
                    if(upperState!=0)PlayIfDifferent(upperLayer,upperState);
                    if(leftHash!=0)PlayIfDifferent(leftLayer,leftHash);
                    if(rightHash!=0)PlayIfDifferent(rightLayer,rightHash);
                }
                catch{Release();throw;}
            }
            Weight(movementLayer,1);Weight(aimLayer,aim==null?0:1);
            if(leftHash!=0)Weight(leftLayer,1);
            if(rightHash!=0)Weight(rightLayer,1);
            if(consumable)
            {
                Weight(recoilLayer,0);
                TickConsumable(item,throwAction,placement);
                return;
            }
            if(pack)
            {
                Weight(recoilLayer,0);
                TickResourcePack(packAction);
                return;
            }
            Weight(upperLayer,melee?1:0);
            if(glue||melee)
            {
                Weight(recoilLayer,0);
                if(melee)meleeFrame=Time.frameCount;
                if(melee&&action==MeleeAction.Shove)
                {
                    shownAttack=MeleeAction.None;
                    attackHash=0;
                    attackFrame=-1;
                    PlayShove(item,local.Locomotion.GetCrouch());
                    return;
                }
                bool wasShoving=shoveHash!=0;
                if(shoveHash!=0)
                {
                    shoveHash=0;
                    shoveFrame=-1;
                }
                if(melee&&action is not (MeleeAction.None or MeleeAction.Idle))
                {
                    PlayMeleeAttack(item,action,local.Locomotion.GetCrouch()>.5f);
                    return;
                }
                if(shownAttack!=MeleeAction.None)
                {
                    if(attackHash!=upperIdleHash)animator.CrossFadeInFixedTime(upperIdleHash,.1f,upperLayer,0,0);
                    shownAttack=MeleeAction.None;
                    attackHash=0;
                    attackFrame=-1;
                }
                else if(wasShoving)PlayIfDifferent(upperLayer,upperIdleHash);
                Status=pose switch {
                    MeleePose.Sledgehammer=>"native-sledgehammer-holding-layers",
                    MeleePose.Spear=>"native-spear-holding-layers",
                    MeleePose.Knife=>"native-knife-holding-layers",
                    MeleePose.Bat=>"native-bat-holding-layers",
                    _=>"native-glue-gun-holding-layers"
                };
                return;
            }
            float fired=gun.m_lastFireTime;int clip=gun.m_clip;
            if(float.IsFinite(fired)&&fired>lastFire&&lastClip>0&&clip<=lastClip&&recoilHash!=0)
            {Weight(recoilLayer,1);played.Add(recoilLayer);animator.Play(recoilHash,recoilLayer,0);recoilEvents++;}
            lastFire=fired;lastClip=clip;Status="native-synced-firearm-layers";
        }
        void TickConsumable(ItemEquippable item,pPlayerThrowStatus.StatusEnum action,bool placement)
        {
            bool active=placement||action!=pPlayerThrowStatus.StatusEnum.None;
            if(active&&(shownThrow!=action||shownPlacement!=placement))
            {
                // PlayerInventorySynced.SyncThrowState uses one shared sequence,
                // except the Fog Repeller, which supplies the alternate prefix.
                string prefix=item.ItemDataBlock.persistentID==117?"Fogrepeller":"Consumable";
                throwState=placement?"Consumable_Mine_Use":prefix+(action==pPlayerThrowStatus.StatusEnum.Charging?"_Throw_Charge":"_Throw");
                int hash=State(animator,upperLayer,throwState);
                if(hash==0){Status=placement?"consumable-placement-state-unavailable":"consumable-throw-state-unavailable";return;}
                played.Add(upperLayer);
                animator.Play(hash,upperLayer,0);
            }
            if(!active&&(shownPlacement||shownThrow!=pPlayerThrowStatus.StatusEnum.None)&&upperIdleHash!=0)
                PlayIfDifferent(upperLayer,upperIdleHash);
            Weight(upperLayer,active||upperIdleHash!=0?1:0);
            shownThrow=action;
            shownPlacement=placement;
            consumableFrame=Time.frameCount;
            Status=placement?"native-consumable-placement":active?"native-consumable-throw":"native-consumable-holding";
        }
        void TickResourcePack(ResourcePackAction action)
        {
            if(action==ResourcePackAction.Unknown)
            {
                Status=lastPackResult="resource-pack-interaction-unavailable";
                return;
            }
            if(action==ResourcePackAction.Holding)
            {
                Weight(upperLayer,0);
                Status="native-resource-pack-holding";
            }
            else
            {
                if(shownPack!=action)
                {
                    // Same upper-body states used by SyncGenericInteract in 34873.
                    lastPackAction=action;
                    lastPackState=action==ResourcePackAction.Take?"ConsumablePack_Take":"ConsumablePack_Give";
                    int hash=State(animator,upperLayer,lastPackState);
                    if(hash==0)
                    {
                        Status=lastPackResult="resource-pack-action-state-unavailable";
                        return;
                    }
                    ik.Release();
                    played.Add(upperLayer);
                    animator.Play(hash,upperLayer,0);
                    packPlays++;
                }
                Weight(upperLayer,1);
                Status=lastPackResult="native-resource-pack-action";
            }
            shownPack=action;
            packFrame=Time.frameCount;
        }
        void PlayMeleeAttack(ItemEquippable item,MeleeAction action,bool crouched)
        {
            if(shownAttack!=action)
            {
                lastAttackAction=action;
                if(meleeAnimations==null)
                {
                    uint categoryId=item.GearIDRange?.GetCompID(eGearComponent.Category)??0;
                    var category=categoryId==0?null:GameDataBlockBase<GearCategoryDataBlock>.GetBlock(categoryId);
                    uint archetypeId=category?.MeleeArchetype??0;
                    var archetype=archetypeId==0?null:GameDataBlockBase<MeleeArchetypeDataBlock>.GetBlock(archetypeId);
                    uint setId=archetype?.MeleeAnimationSet??0;
                    if(setId!=0)meleeAnimations=GameDataBlockBase<MeleeAnimationSetDataBlock>.GetBlock(setId);
                }
                if(meleeAnimations==null)
                {
                    lastAttackState=null;
                    Status=lastAttackResult="native-melee-animation-set-unavailable";
                    return;
                }
                // The native synced body uses the same swing on either side.
                // Latch stance at each action transition, as the native receiver does.
                var state=action switch
                {
                    MeleeAction.SwingLeft or MeleeAction.SwingRight=>crouched?meleeAnimations.TPAnimHashAttackLeftCrouch:meleeAnimations.TPAnimHashAttackLeft,
                    MeleeAction.Charge=>crouched?meleeAnimations.TPAnimHashAttackLeftChargeCrouch:meleeAnimations.TPAnimHashAttackLeftCharge,
                    MeleeAction.Release=>crouched?meleeAnimations.TPAnimHashAttackLeftReleaseCrouch:meleeAnimations.TPAnimHashAttackLeftRelease,
                    _=>crouched?meleeAnimations.TPAnimHashIdleCrouch:meleeAnimations.TPAnimHashIdle
                };
                lastAttackState=state?.Name;
                int hash=state?.GetHash()??0;
                if(hash==0||!animator.HasState(upperLayer,hash))
                {
                    Status=lastAttackResult="native-melee-attack-state-unavailable";
                    return;
                }
                played.Add(upperLayer);
                animator.CrossFadeInFixedTime(hash,.1f,upperLayer,0,0);
                shownAttack=action;
                attackHash=hash;
                attackPlays++;
            }
            attackFrame=Time.frameCount;
            Status=lastAttackResult="native-melee-attack";
        }
        void PlayShove(ItemEquippable item,float crouch)
        {
            if(shoveHash==0)
            {
                // Same state selector as the MeleeShove branch of SyncGenericInteract
                // in revision 34873. Play only its body animation on the local rig.
                string name=item.ItemDataBlock.GetName();
                string stance=crouch>.3f?"Crouch":"Stand";
                lastShoveState=string.IsNullOrEmpty(name)?null:name+"_"+stance+"_Shove";
                int hash=State(animator,upperLayer,lastShoveState);
                if(hash==0)
                {
                    Status=lastShoveResult="native-shove-state-unavailable";
                    return;
                }
                ik.Release();
                played.Add(upperLayer);
                animator.Play(hash,upperLayer,0);
                shoveHash=hash;
                shovePlays++;
            }
            shoveFrame=Time.frameCount;
            Status=lastShoveResult="native-melee-shove";
        }
        static int State(Animator a,int layer,string name)
        {if(string.IsNullOrWhiteSpace(name))return 0;int hash=Animator.StringToHash(name);return a.HasState(layer,hash)?hash:0;}
        void PlayIfDifferent(int layer,int hash)
        {
            var state=animator.GetCurrentAnimatorStateInfo(layer);
            if(state.shortNameHash!=hash&&state.fullPathHash!=hash){played.Add(layer);animator.Play(hash,layer,0);}
        }
        void Weight(int layer,float value)
        {if(Math.Abs(animator.GetLayerWeight(layer)-value)>.00001f){weighted.Add(layer);animator.SetLayerWeight(layer,value);}}
        internal void Solve(WorldWeaponView view,Vector3 aimTarget)
        {
            if(!Active||!solveGrip||shoveHash!=0||shownPack is ResourcePackAction.Take or ResourcePackAction.Give)return;
            ik.Solve(player,view,aimTarget,useAim:!leftGripOnly);
        }
        internal object Diagnostics()=>new{status=Status,active=Active,boundFrame=BoundFrame,bindings,movement=movementName,aimOffset=aimName,
            leftGripAnimation=leftName,rightGripAnimation=rightName,leftFingerState=leftHash!=0,rightFingerState=rightHash!=0,recoilState=recoilHash!=0,recoilEvents,
            shove=ShoveDiagnostics(),
            bodyAnimator=animator==null?0:animator.Pointer.ToInt64(),fpsAnimator=player?.AnimatorArms==null?0:player.AnimatorArms.Pointer.ToInt64(),ik=ik.Diagnostics()};
        internal object ShoveDiagnostics()=>new{state=lastShoveState,result=lastShoveResult,plays=shovePlays,active=shoveHash!=0,frame=shoveFrame};
        internal object MeleeAttackDiagnostics()=>new{action=lastAttackAction.ToString(),state=lastAttackState,result=lastAttackResult,plays=attackPlays,active=shownAttack!=MeleeAction.None,frame=attackFrame};
        internal object ResourcePackDiagnostics()=>new{action=shownPack.ToString(),lastAction=lastPackAction.ToString(),state=lastPackState,result=lastPackResult,plays=packPlays,frame=packFrame};
        internal object ConsumableDiagnostics()=>new{action=shownPlacement?"PlaceTripmine":shownThrow.ToString(),state=throwState,frame=consumableFrame,status=Status};
        internal void Release()
        {
            using var cleanup=new RestoreJournal();
            var old=animator;var layers=saved.ToArray();var changedStates=new HashSet<int>(played);var changedWeights=new HashSet<int>(weighted);float elapsed=Math.Max(0,Time.time-savedAt);
            bool sameController=old!=null&&old.runtimeAnimatorController!=null&&old.runtimeAnimatorController.Pointer.ToInt64()==controllerId;
            animator=null;player=null;itemId=0;BoundFrame=-1;shoveFrame=-1;shoveHash=0;saved.Clear();
            meleeAnimations=null;shownAttack=MeleeAction.None;attackHash=0;attackFrame=meleeFrame=-1;
            shownPack=ResourcePackAction.None;packFrame=-1;
            shownThrow=pPlayerThrowStatus.StatusEnum.None;shownPlacement=false;consumableFrame=-1;
            played.Clear();weighted.Clear();
            foreach(var layer in layers)cleanup.Add(()=>
            {
                if(old==null||!sameController||layer.Index>=old.layerCount)return;
                // Only roll back a state actually replaced by this view. Never
                // restart the native movement state just for a camera toggle.
                var current=old.GetCurrentAnimatorStateInfo(layer.Index);
                if(changedStates.Contains(layer.Index)&&layer.State!=0&&current.fullPathHash!=layer.State&&old.HasState(layer.Index,layer.State))
                    old.Play(layer.State,layer.Index,layer.Time+(layer.Length>0?elapsed*layer.Speed/layer.Length:0));
                if(changedWeights.Contains(layer.Index))old.SetLayerWeight(layer.Index,layer.Weight);
            });
            cleanup.Add(()=>ik.Release(sameController));
            Status="released";
        }
        public void Dispose()=>Release();
    }
}
