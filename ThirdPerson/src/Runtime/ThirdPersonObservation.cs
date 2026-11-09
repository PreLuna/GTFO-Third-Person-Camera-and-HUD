using Il2CppInterop.Runtime.Attributes;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        readonly CameraInputAdapter lookInput=new();
        long lookPlayer,lookOwner,lookModel,lookItem,lookCamera;
        int lastLookRenderFrame=-1,lookHandoffs,lookDiscards;
        string lookExit="not-started";
        int lookHookCalls,localLookHookCalls,lastLocalLookHookFrame=-1,lookEntries,lastLookRejectFrame=-1;
        bool lookLastHandedOff;
        ObservationBlockReason lastLookRejection;
        ObservationMovement observationMovement,lastRejectedMovement;
        [HideFromIl2Cpp] bool ObservationOwner(LocalPlayerAgent local)
            =>local!=null&&lookPlayer==Id(local)&&lookOwner==Id(local.Owner)&&lookModel==Id(local.PlayerSyncModel)&&lookItem==Id(local.Inventory?.WieldedItem)&&lookCamera==Id(local.FPSCamera);
        [HideFromIl2Cpp] bool OwnObservationView()
        {
            var head=headCameraProbe?.Read();
            return head==null||head.Value.Available&&!head.Value.ControlsBody&&!head.Value.ReplacesMainView;
        }
        [HideFromIl2Cpp] ObservationMovement ReadObservationMovement(LocalPlayerAgent local)
            =>observationMovement=new(Time.frameCount,InputMapper.GetAxis.Invoke(InputAction.MoveHorizontal,local.InputFilter),
                InputMapper.GetAxis.Invoke(InputAction.MoveVertical,local.InputFilter),ConvertView.N(local.Locomotion.Velocity));
        [HideFromIl2Cpp] bool MoveRequested(LocalPlayerAgent local)=>ReadObservationMovement(local).InputRequested;
        [HideFromIl2Cpp] bool ObservationIdle(LocalPlayerAgent local)
        {
            var item=local.Inventory?.WieldedItem;
            return aim.Active&&!aim.Gesture.WantsAim&&item!=null&&!item.IsReloading
                &&local.FPItemHolder?.ItemIsBusy==false&&local.FPItemHolder.ItemAimTrigger==false
                &&!InputMapper.GetButton.Invoke(InputAction.Aim,local.InputFilter)
                &&!InputMapper.GetButton.Invoke(InputAction.Fire,local.InputFilter)
                &&!InputMapper.GetButton.Invoke(InputAction.Use,local.InputFilter)
                &&!InputMapper.GetButton.Invoke(InputAction.Reload,local.InputFilter)
                &&ReadObservationMovement(local).Stationary;
        }
        [HideFromIl2Cpp] void MaintainObservation(LocalPlayerAgent local)
        {
            if(lookInput.Capturing)return;
            bool held=Input.GetKey(Plugin.Instance.Settings.Observe.Value);
            lookInput.Orientation.ObserveKey(held);
            if(!lookInput.Orientation.Active)return;
            if(!ObservationOwner(local)||Gate(local)!=FallbackReason.None||!OwnObservationView())
            {EndObservation(local,false,"ownership-or-focus");return;}
            if(!float.IsFinite(Time.unscaledDeltaTime)||Time.unscaledDeltaTime>.2f||lastLookRenderFrame<Time.frameCount-1)
            {EndObservation(local,false,"discontinuity");return;}
            if(!held||!Plugin.Instance.Settings.IndependentLook.Value||!Plugin.Instance.Settings.Enabled.Value
                ||!ObservationIdle(local))
                EndObservation(local,true,"release-or-action");
        }
        [HideFromIl2Cpp] void EndObservation(LocalPlayerAgent local,bool handoff,string why)
        {
            bool active=lookInput.Orientation.Active;
            var direction=active&&!lookInput.Capturing&&handoff&&ObservationOwner(local)?lookInput.Orientation.Forward(local.FPSCamera.m_yaw,local.FPSCamera.m_pitch):default;
            // Clear first: faults and subsequent callbacks cannot replay an old observer offset.
            lookInput.Orientation.Reset(Input.GetKey(Plugin.Instance.Settings.Observe.Value));
            lookPlayer=lookOwner=lookModel=lookItem=lookCamera=0;
            if(!active)return;
            lookExit=why;
            lookLastHandedOff=false;
            if(direction.LengthSquared()>.9f)
            {
                local.FPSCamera.SetLookDirection(ConvertView.U(direction));
                local.FPSCamera.UpdateFlatTransform();
                lookHandoffs++;lookLastHandedOff=true;
            }
            else lookDiscards++;
        }
        [HideFromIl2Cpp] internal void BeginLook(LookCameraController camera,out CameraInputAdapter.Capture capture)
        {
            capture=default;
            lookHookCalls++;
            var local=Local();
            if(camera==null||local?.FPSCamera?.Pointer!=camera.Pointer||lookInput.Capturing)return;
            localLookHookCalls++;lastLocalLookHookFrame=Time.frameCount;
            var settings=Plugin.Instance.Settings;
            bool held=Input.GetKey(settings.Observe.Value);
            lookInput.Orientation.ObserveKey(held);
            // Ordinary C1 look and the disabled comparison do not introduce extra Aim polling.
            if(!lookInput.Orientation.Active&&(!settings.IndependentLook.Value||!held||!state.Requested||state.Faulted||!settings.Enabled.Value))return;
            BeforeLogic(local.FPSCamera);
            PollAim(local);
            MaintainObservation(local);
            var blocked=ObservationBlock(local);
            if(!held||blocked!=ObservationBlockReason.None)
            {if(held){lastLookRejection=blocked;lastLookRejectFrame=Time.frameCount;
                if(blocked is ObservationBlockReason.Moving or ObservationBlockReason.MovementUnavailable)lastRejectedMovement=observationMovement;
                lookInput.Orientation.Reset(true);}return;}
            if(!lookInput.Orientation.Active)
            {
                if(!lookInput.Orientation.Begin(camera.m_yaw,camera.m_pitch))
                {if(lastLookRejection==ObservationBlockReason.None)lastLookRejection=ObservationBlockReason.WaitingRelease;lastLookRejectFrame=Time.frameCount;return;}
                lookEntries++;lastLookRejection=ObservationBlockReason.None;
                lookPlayer=Id(local);lookOwner=Id(local.Owner);lookModel=Id(local.PlayerSyncModel);lookItem=Id(local.Inventory?.WieldedItem);lookCamera=Id(local.FPSCamera);
            }
            lookInput.Begin(camera,settings.ObserveYaw.Value,out capture);
        }
        [HideFromIl2Cpp] internal void FinishLook(CameraInputAdapter.Capture capture,bool succeeded)
            =>lookInput.Finish(capture,succeeded);
        [HideFromIl2Cpp] internal void BeforeMove(PlayerLocomotion locomotion)
        {
            if(!lookInput.Orientation.Active)return;
            var local=Local();
            if(local?.Locomotion?.Pointer!=locomotion.Pointer)return;
            MaintainObservation(local);
            // This prefix runs before the native method reads FlatForward/FlatRight.
            if(lookInput.Orientation.Active&&MoveRequested(local))EndObservation(local,true,"move");
        }
        [HideFromIl2Cpp] void ReleaseObservation()
        {
            var local=Local();
            EndObservation(local,ObservationOwner(local)&&Gate(local)==FallbackReason.None&&OwnObservationView(),"release");
            lastLookRenderFrame=-1;
        }
    }
}
