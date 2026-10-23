using System;
using Il2CppInterop.Runtime;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    internal sealed class AimController
    {
        internal readonly AimGesture Gesture=new();
        LocalPlayerAgent owner;
        FirstPersonItemHolder holder;
        long itemId;
        int generation;
        int aimPressedFrame=-1;
        internal bool Active {get;private set;}
        internal string Equipment {get;private set;}="none";
        internal static bool Supported(ItemEquippable item)
        {
            if(item==null)return false;
            string type=item.GetIl2CppType().FullName;
            return type is "Gear.BulletWeapon" or "Gear.RifleWeapon" or "Gear.Shotgun"
                && item.TryCast<Gear.BulletWeapon>()!=null&&item.ItemFPSData?.canAim==true
                &&!item.ItemFPSData.onlyStartAimOnPressed;
        }
        internal void Poll(LocalPlayerAgent local,bool permitted,float window)
        {
            var item=local?.Inventory?.WieldedItem;
            bool supported=permitted&&Supported(item)&&local.FPItemHolder!=null;
            Equipment=item==null?"none":item.GetIl2CppType().FullName;
            if(!supported){Cancel();return;}
            long next=item.Pointer.ToInt64();
            if(owner?.Pointer!=local.Pointer||holder?.Pointer!=local.FPItemHolder.Pointer||itemId!=next)
            {Cancel();owner=local;holder=local.FPItemHolder;itemId=next;generation++;}
            Active=true;
            bool wasAiming=Gesture.WantsAim;
            Gesture.Sample(new(local.Pointer.ToInt64(),local.Owner.Pointer.ToInt64(),next,generation,true),
                InputMapper.GetButton.Invoke(InputAction.Aim,local.InputFilter),Time.unscaledTime,window);
            if(!wasAiming&&Gesture.WantsAim)aimPressedFrame=Time.frameCount;
        }
        internal bool GetAim(ItemEquippable item,bool pressed,out bool value)
        {
            value=false;
            if(!Active||item?.Pointer.ToInt64()!=itemId||item.Owner?.Pointer!=owner?.Pointer)return false;
            value=!holder.ItemIsBusy&&Gesture.WantsAim&&(!pressed||aimPressedFrame==Time.frameCount);
            return true;
        }
        // UpdateAimInput only computes the trigger/toggle latch in this verified native build.
        // Replacing that calculation avoids consuming the same click in the game's toggle path.
        internal bool Apply(FirstPersonItemHolder target)
        {
            if(!Active||target?.Pointer!=holder?.Pointer)return false;
            bool allowed=holder.ExtItemAimAllowed&&holder.WieldedItem?.AimingAllowed==true;
            bool want=allowed&&(holder.ForceAimTrigger||(holder.WieldedItem.ItemFPSData?.canAim==true&&Gesture.WantsAim));
            holder.ItemAimTrigger=want;
            PlayerLocomotion.AimToggleLock=want;
            return true;
        }
        internal void Cancel()
        {
            // This adapter owns only its own ADS latch, never the game's toggle setting.
            var oldHolder=holder;bool owned=Active;
            Active=false;owner=null;holder=null;itemId=0;aimPressedFrame=-1;Gesture.Cancel();
            if(!owned)return;
            using var recovery=new RestoreJournal();
            recovery.Add(()=>PlayerLocomotion.AimToggleLock=false);
            recovery.Add(()=>{if(oldHolder!=null)oldHolder.ItemAimTrigger=false;});
        }
    }

    internal sealed class UnityAimWorld : IAimWorld
    {
        readonly UnityCameraWorld world=new();
        internal long TotalQueries=>world.TotalQueries;
        long boundPlayer,boundItem,boundModel;
        internal void Bind(LocalPlayerAgent player)
        {
            long item=player.Inventory?.WieldedItem?.Pointer.ToInt64()??0,model=player.PlayerSyncModel?.Pointer.ToInt64()??0;
            if(boundPlayer!=player.Pointer.ToInt64()||boundItem!=item||boundModel!=model)
            {world.Bind(player.gameObject);boundPlayer=player.Pointer.ToInt64();boundItem=item;boundModel=model;}
            world.Mask=LayerManager.MASK_BULLETWEAPON_RAY;
        }
        public bool Trace(System.Numerics.Vector3 origin,System.Numerics.Vector3 direction,float distance,out System.Numerics.Vector3 point)
            =>world.Ray(origin,direction,distance,out point);
    }

    // Scoped by Fire, not by render callbacks or a global replacement ray.
    internal sealed class FireAimScope : IDisposable
    {
        [ThreadStatic] internal static FireAimScope Current;
        readonly FireAimScope previous;
        readonly ShotRayGate gate;
        readonly Vector3 target;
        bool disposed;
        internal FireAimScope(long camera,Vector3 origin,Vector3 target)
        {gate=new(camera,ConvertView.N(origin));this.target=target;previous=Current;Current=this;}
        internal void Adapt(Transform align,Weapon.WeaponHitData data,Vector3 actualOrigin)
        {
            if(disposed||align==null||data==null||!gate.Accept(align.Pointer.ToInt64(),data.Pointer.ToInt64(),ConvertView.N(actualOrigin)))return;
            var direction=target-actualOrigin;
            if(direction.sqrMagnitude>.01f)data.fireDir=direction.normalized;
            // Original CastWeaponRay applies every pellet offset/random spread afterwards.
            // Continuation rays sharing this hit data are not redirected a second time.
        }
        public void Dispose(){if(disposed)return;disposed=true;if(Current!=this)throw new InvalidOperationException("开火上下文恢复顺序错误");Current=previous;}
    }
}

