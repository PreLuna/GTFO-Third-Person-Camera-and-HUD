using System;
using Il2CppInterop.Runtime.Attributes;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        readonly CameraObservationFeedback observationFeedback=new();
        long feedbackPlayer,feedbackOwner;
        bool feedbackKeyCached,feedbackRawHeld,feedbackChatWarned;
        KeyCode feedbackKey,feedbackToggle,feedbackSnapshot;
        string feedbackKeyLabel,feedbackToggleLabel,feedbackSnapshotLabel,lastObservationNotice;
        int observationKeyPresses,observationNotices,lastObservationNoticeFrame=-1;
        ObservationBlockReason feedbackBlock;
        [HideFromIl2Cpp] void ResetObservationFeedback()
        {observationFeedback.Reset();feedbackPlayer=feedbackOwner=0;feedbackRawHeld=false;}
        [HideFromIl2Cpp] static string ObservationKeyLabel(KeyCode key)=>key switch
        {KeyCode.RightAlt=>"右Alt",KeyCode.LeftAlt=>"左Alt",_=>key.ToString()};
        [HideFromIl2Cpp] void TickObservationFeedback(LocalPlayerAgent local)
        {
            try
            {
                if(local==null){ResetObservationFeedback();return;}
                if(feedbackPlayer!=Id(local)||feedbackOwner!=Id(local.Owner))
                {observationFeedback.Reset();feedbackPlayer=Id(local);feedbackOwner=Id(local.Owner);feedbackRawHeld=false;}
                var settings=Plugin.Instance.Settings;
                if(!feedbackKeyCached||feedbackKey!=settings.Observe.Value||feedbackToggle!=settings.Toggle.Value||feedbackSnapshot!=settings.Snapshot.Value)
                {
                    feedbackKey=settings.Observe.Value;feedbackToggle=settings.Toggle.Value;feedbackSnapshot=settings.Snapshot.Value;
                    feedbackKeyLabel=ObservationKeyLabel(feedbackKey);feedbackToggleLabel=ObservationKeyLabel(feedbackToggle);
                    feedbackSnapshotLabel=ObservationKeyLabel(feedbackSnapshot);
                    feedbackKeyCached=true;observationFeedback.Reset();feedbackRawHeld=false;
                }
                bool held=Input.GetKey(feedbackKey);
                if(held&&!feedbackRawHeld)observationKeyPresses++;
                bool needed=held||feedbackRawHeld||lookInput.Orientation.Active||!observationFeedback.Introduced
                    ||lookHandoffs+lookDiscards!=feedbackLastExits;
                feedbackRawHeld=held;
                if(!needed)return;
                feedbackBlock=ObservationBlock(local);
                if(feedbackBlock is ObservationBlockReason.Moving or ObservationBlockReason.MovementUnavailable)lastRejectedMovement=observationMovement;
                if(feedbackBlock==ObservationBlockReason.None&&lookInput.Orientation.BlockedUntilRelease)feedbackBlock=ObservationBlockReason.WaitingRelease;
                var log=GuiManager.PlayerLayer?.m_gameEventLog;if(log==null)return;
                var frame=new ObservationFeedbackFrame(FocusAllowed(),held,lookInput.Orientation.Active,feedbackBlock,
                    Time.frameCount,lastLocalLookHookFrame,lookEntries,lookHandoffs+lookDiscards,lookLastHandedOff,lookExit,feedbackKeyLabel,feedbackToggleLabel,lastLookRejection,feedbackSnapshotLabel,observationMovement);
                string message=observationFeedback.Tick(frame,Time.unscaledTime);
                feedbackLastExits=lookHandoffs+lookDiscards;
                if(message==null)return;
                log.AddLogItem("[第三人称 C2a] "+message,eGameEventChatLogType.GameEvent);
                lastObservationNotice=message;lastObservationNoticeFrame=Time.frameCount;observationNotices++;
                Plugin.Instance.Log.LogInfo("[C2a提示] "+message);
            }
            catch(Exception error)
            {
                if(!feedbackChatWarned){feedbackChatWarned=true;Plugin.Instance?.Log.LogWarning("C2a本地聊天提示暂不可用："+error.GetBaseException().Message);}
            }
        }
        int feedbackLastExits;
        [HideFromIl2Cpp] ObservationBlockReason ObservationBlock(LocalPlayerAgent local)
        {
            var settings=Plugin.Instance.Settings;
            if(!settings.IndependentLook.Value)return ObservationBlockReason.FeatureDisabled;
            if(state.Faulted)return ObservationBlockReason.Fault;
            if(!settings.Enabled.Value||!state.Requested)return ObservationBlockReason.ViewDisabled;
            if(local?.FPSCamera==null)return ObservationBlockReason.NoPlayer;
            var gate=Gate(local);
            if(gate!=FallbackReason.None)return gate switch
            {FallbackReason.Focus=>ObservationBlockReason.Focus,FallbackReason.ControlTaken=>ObservationBlockReason.ControlTaken,
                FallbackReason.NoPlayer=>ObservationBlockReason.NoPlayer,_=>ObservationBlockReason.UnsupportedState};
            if(!OwnObservationView())return ObservationBlockReason.ControlTaken;
            var item=local.Inventory?.WieldedItem;
            if(!aim.Active||item==null)return ObservationBlockReason.UnsupportedItem;
            if(aim.Gesture.WantsAim||local.FPItemHolder?.ItemAimTrigger==true||InputMapper.GetButton.Invoke(InputAction.Aim,local.InputFilter))return ObservationBlockReason.Aiming;
            if(item.IsReloading)return ObservationBlockReason.Reloading;
            if(local.FPItemHolder?.ItemIsBusy!=false)return ObservationBlockReason.Busy;
            if(InputMapper.GetButton.Invoke(InputAction.Fire,local.InputFilter)||InputMapper.GetButton.Invoke(InputAction.Use,local.InputFilter)
                ||InputMapper.GetButton.Invoke(InputAction.Reload,local.InputFilter))return ObservationBlockReason.Action;
            var motion=ReadObservationMovement(local);
            if(!motion.Valid)return ObservationBlockReason.MovementUnavailable;
            if(!motion.Stationary)return ObservationBlockReason.Moving;
            if(nativeDisplay||lastLookRenderFrame<Time.frameCount-1)return ObservationBlockReason.NoDisplay;
            if(!float.IsFinite(Time.unscaledDeltaTime)||Time.unscaledDeltaTime>.2f)return ObservationBlockReason.LongFrame;
            return ObservationBlockReason.None;
        }
    }
}
