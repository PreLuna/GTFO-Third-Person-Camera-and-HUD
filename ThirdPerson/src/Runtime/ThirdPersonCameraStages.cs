using System;
using Il2CppInterop.Runtime.Attributes;
using Player;
using PreLuna.ThirdPerson.CameraSystem;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        int featureSignature=int.MinValue;long featurePlayer;bool featureNoticeWarned;
        float lastViewAspect=16f/9;
        [HideFromIl2Cpp] void TickCameraFeatureNotice(LocalPlayerAgent local)
        {
            if(local==null){featurePlayer=0;featureSignature=int.MinValue;return;}
            if(!FocusAllowed())return;
            var c=Plugin.Instance.Settings;
            int signature=(int)c.Preset.Value*16+(c.Transition.Enabled?1:0)+(c.Refinement.Enabled?2:0)
                +(c.ActionFeedback.Enabled?4:0)+(c.MovementFeedback.Value?8:0)+(int)c.CyclePreset.Value*256;
            if(featurePlayer==Id(local)&&featureSignature==signature)return;
            try
            {
                var log=GuiManager.PlayerLayer?.m_gameEventLog;if(log==null)return;
                string name=c.Preset.Value switch{CameraPreset.Combat=>"战斗",CameraPreset.Traversal=>"探索",CameraPreset.Stable=>"舒适",_=>"均衡"};
                bool effects=c.MovementFeedback.Value&&c.Preset.Value!=CameraPreset.Stable;
                log.AddLogItem("[第三人称] 当前"+name+"预设；C3连续缩放="+(c.Transition.Enabled?"开":"关")+"，C4空间避让="+(c.Refinement.Enabled?"开":"关")
                    +"，动作反馈="+(effects?"开":"关")+"；"+c.CyclePreset.Value+"切换预设。",eGameEventChatLogType.GameEvent);
                featurePlayer=Id(local);featureSignature=signature;
            }
            catch(Exception e){if(!featureNoticeWarned){featureNoticeWarned=true;Plugin.Instance.Log.LogWarning("镜头阶段提示暂不可用："+e.GetBaseException().Message);}}
        }
        [HideFromIl2Cpp] object CameraStagesDiagnostics()
        {
            var k=cameraStyle.Kinematics;var f=cameraStyle.ActionFeedback;
            float actual=display.ClearanceDistance??(display.Position-rig.Pivot).Length(),height=Math.Clamp(logical.Eye.Y-logical.Body.Y,.65f,2.2f);
            return new{
                kinematics=new{k.Frame,k.Generation,k.Valid,k.Reset,k.Speed,k.Travel,velocity=k.Velocity.ToString()},
                transition=cameraStyle.Transition,
                actionFeedback=new{f.Frame,f.Generation,f.Enabled,offset=f.Offset.ToString(),f.Gait,inertia=f.Inertia.ToString(),f.AimScale},
                spatial=collisionSolver.Current,
                clearanceProbe=new{lastClearanceProbe.Frame,lastClearanceProbe.IsThirdPerson,lastClearanceProbe.Reason,position=lastClearanceProbe.Position.ToString()},
                composition=new{aspect=lastViewAspect,horizontalFov=CameraComposition.HorizontalFov(rig.VerticalFov,lastViewAspect),
                    requestedHeightRatio=CameraComposition.HeightRatio(height,rig.Distance,rig.VerticalFov),actualHeightRatio=CameraComposition.HeightRatio(height,actual,rig.VerticalFov),
                    referenceOnly=true}
            };
        }
    }
}
