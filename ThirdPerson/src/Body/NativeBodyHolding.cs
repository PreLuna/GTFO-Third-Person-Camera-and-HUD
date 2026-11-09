using System;
using System.Collections.Generic;
using Player;
using UnityEngine;
using Il2CppInterop.Runtime;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    // Body-only equivalent of the firearm branch of the native synced inventory.
    // Never call DoEquipItem/PlayAnimationsForWieldedItem on the live inventory:
    // those also change FPS animators, real equipment and gameplay IK targets.
    internal sealed class NativeBodyHolding : IDisposable
    {
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
        string movementName,aimName;
        string leftName,rightName;
        float savedAt,lastFire;
        int lastClip;
        internal bool Active=>animator!=null;
        internal int BoundFrame {get;private set;}=-1;
        internal string Status {get;private set;}="not-bound";
        int bindings,recoilEvents;
        internal void Tick(LocalPlayerAgent local,ItemEquippable item)
        {
            var gun=item?.TryCast<Gear.BulletWeapon>();
            if(local==null||gun==null||item.ItemDataBlock==null||local.Locomotion==null)
            {Release();Status="unsupported-equipment";return;}
            if(item.IsReloading)
            {Release();Status="native-equipment-busy";return;}
            var body=local.AnimatorBody;
            if(body==null||local.AnimatorArms==null||body.Pointer==local.AnimatorArms.Pointer||body.transform.IsChildOf(local.AnimatorArms.transform))
            {Release();Status="body-and-fps-animators-not-separated";return;}
            if(body.runtimeAnimatorController==null){Release();Status="native-controller-not-ready";return;}
            if(item.pItemData.slot is not (InventorySlot.GearStandard or InventorySlot.GearSpecial))
            {Release();Status="unsupported-equipment-slot";return;}
            string move=local.Locomotion.GetAnimationForWielded(PlayerLocomotion.ePlayerAnimation.Movement);
            // Original PlayAnimationsForWieldedItem selects this using the item
            // data slot (native +0x7c), not a guessed weapon/model name.
            string aim=item.ItemDataBlock.inventorySlot==InventorySlot.GearStandard?"PistolAimOffset":"RifleAimOffset";
            if(Active&&(player.Pointer!=local.Pointer||animator.Pointer!=body.Pointer||controllerId!=body.runtimeAnimatorController.Pointer.ToInt64()
                ||itemId!=item.Pointer.ToInt64()||movementName!=move||aimName!=aim||leftName!=item.LeftHandGripAnim||rightName!=item.RightHandGripAnim))Release();
            if(!Active)
            {
                movementLayer=PlayerAgent.ANIM_LAYER_FULL_BODY;aimLayer=PlayerAgent.ANIM_LAYER_3RD_AIM_OFFSET;
                upperLayer=PlayerAgent.ANIM_LAYER_3RD_UPPERBODY;leftLayer=PlayerAgent.ANIM_LAYER_LEFT_HAND;
                rightLayer=PlayerAgent.ANIM_LAYER_RIGHT_HAND;recoilLayer=PlayerAgent.ANIM_LAYER_3RD_ADDITIVE;
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                    if(layer<0||layer>=body.layerCount){Status="native-animation-layers-unavailable";return;}
                int mh=Animator.StringToHash(move??""),ah=Animator.StringToHash(aim);
                if(string.IsNullOrWhiteSpace(move)||!body.HasState(movementLayer,mh)||!body.HasState(aimLayer,ah))
                {Status="native-holding-states-unavailable";return;}
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                    if(body.IsInTransition(layer)){Status="waiting-for-native-animation-transition";return;}
                player=local;itemId=item.Pointer.ToInt64();animator=body;savedAt=Time.time;
                controllerId=body.runtimeAnimatorController.Pointer.ToInt64();leftName=item.LeftHandGripAnim;rightName=item.RightHandGripAnim;
                movementName=move;aimName=aim;movementHash=mh;aimHash=ah;
                leftHash=State(body,leftLayer,item.LeftHandGripAnim);rightHash=State(body,rightLayer,item.RightHandGripAnim);
                // Exact string literals from BulletWeaponSynced and its rifle /
                // shotgun overrides; independent from the inventory-slot aim selector.
                recoilHash=State(body,recoilLayer,item.TryCast<Gear.Shotgun>()!=null||item.TryCast<Gear.RifleWeapon>()!=null?"Rifle_Recoil":"Pistol_Recoil");
                foreach(int layer in new[]{movementLayer,aimLayer,upperLayer,leftLayer,rightLayer,recoilLayer})
                {
                    var state=body.GetCurrentAnimatorStateInfo(layer);
                    saved.Add(new(layer,body.GetLayerWeight(layer),state.fullPathHash,state.normalizedTime,state.length,state.speed*state.speedMultiplier));
                }
                lastFire=gun.m_lastFireTime;lastClip=gun.m_clip;BoundFrame=Time.frameCount;bindings++;
                try
                {
                    PlayIfDifferent(movementLayer,movementHash);PlayIfDifferent(aimLayer,aimHash);
                    if(leftHash!=0)PlayIfDifferent(leftLayer,leftHash);
                    if(rightHash!=0)PlayIfDifferent(rightLayer,rightHash);
                }
                catch{Release();throw;}
            }
            // These are the synced firearm weights. Leave FPS arms and body
            // locomotion parameters to their native update paths.
            Weight(movementLayer,1);Weight(aimLayer,1);Weight(upperLayer,0);
            if(leftHash!=0)Weight(leftLayer,1);
            if(rightHash!=0)Weight(rightLayer,1);
            float fired=gun.m_lastFireTime;int clip=gun.m_clip;
            if(float.IsFinite(fired)&&fired>lastFire&&lastClip>0&&clip<=lastClip&&recoilHash!=0)
            {Weight(recoilLayer,1);played.Add(recoilLayer);animator.Play(recoilHash,recoilLayer,0);recoilEvents++;}
            lastFire=fired;lastClip=clip;Status="native-synced-firearm-layers";
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
            if(!Active)return;
            ik.Solve(player,view,aimTarget);
        }
        internal object Diagnostics()=>new{status=Status,active=Active,boundFrame=BoundFrame,bindings,movement=movementName,aimOffset=aimName,
            leftFingerState=leftHash!=0,rightFingerState=rightHash!=0,recoilState=recoilHash!=0,recoilEvents,
            bodyAnimator=animator==null?0:animator.Pointer.ToInt64(),fpsAnimator=player?.AnimatorArms==null?0:player.AnimatorArms.Pointer.ToInt64(),ik=ik.Diagnostics()};
        internal void Release()
        {
            using var cleanup=new RestoreJournal();
            var old=animator;var layers=saved.ToArray();var changedStates=new HashSet<int>(played);var changedWeights=new HashSet<int>(weighted);float elapsed=Math.Max(0,Time.time-savedAt);
            bool sameController=old!=null&&old.runtimeAnimatorController!=null&&old.runtimeAnimatorController.Pointer.ToInt64()==controllerId;
            animator=null;player=null;itemId=0;BoundFrame=-1;saved.Clear();
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
