using System;

namespace PreLuna.ThirdPerson.CameraSystem
{
    public enum ObservationBlockReason
    {
        None,ViewDisabled,FeatureDisabled,Fault,NoPlayer,Focus,ControlTaken,UnsupportedState,
        UnsupportedItem,Aiming,Reloading,Busy,Action,Moving,NoDisplay,LongFrame,WaitingRelease,MovementUnavailable
    }
    public readonly record struct ObservationFeedbackFrame(bool CanIntroduce,bool Held,bool Active,
        ObservationBlockReason Block,int Frame,int LastHookFrame,int Entries,int Exits,
        bool HandedOff,string Exit,string Key,string Toggle,ObservationBlockReason LastRejected=ObservationBlockReason.None,string Snapshot="F8",ObservationMovement Motion=default);
    // Local UI messages only. Input capture/render callbacks never write chat directly.
    public sealed class CameraObservationFeedback
    {
        bool held,active,warned,sampled;
        int entries,exits,pressFrame;
        float pressedAt;
        public bool Introduced {get;private set;}
        public void Reset(){Introduced=held=active=warned=sampled=false;entries=exits=pressFrame=0;pressedAt=0;}
        public string Tick(ObservationFeedbackFrame f,float now)
        {
            bool pressed=f.Held&&!held,entered=f.Active&&!active,left=!f.Active&&active;
            bool brief=sampled&&f.Entries>entries&&f.Exits>exits&&!f.Active;
            sampled=true;
            held=f.Held;active=f.Active;entries=f.Entries;exits=f.Exits;
            if(!f.Held)warned=false;
            if(pressed){pressFrame=f.Frame;pressedAt=now;warned=false;}
            if(entered)
            {Introduced=true;warned=true;return "已进入绕看：按住"+f.Key+"并移动鼠标，镜头绕看、人物保持朝向；松开或开始动作时交接。";}
            if(left||brief)
            {
                Introduced=true;warned=true;
                return (brief?"绕看已短暂进入后退出：":"已退出绕看：")+ExitReason(f)+"；"+(f.HandedOff?"已交接当前观察朝向。":"已回原生视角。")+"重新进入请松开"+f.Key+"再按。";
            }
            if(pressed&&!f.Active)
            {
                Introduced=true;
                if(f.Block!=ObservationBlockReason.None){warned=true;return Denied(f);}
                return "已收到"+f.Key+"：请按住并移动鼠标，正在等待进入绕看。";
            }
            if(f.Held&&!f.Active&&!warned)
            {
                if(now<pressedAt)pressedAt=now;
                if(float.IsFinite(now)&&now-pressedAt>=.35f)
                {
                    warned=true;
                    if(f.Block!=ObservationBlockReason.None)return Denied(f);
                    return f.LastHookFrame<pressFrame
                        ?"已收到"+f.Key+"，但尚未收到本机观察输入回调；请按"+f.Snapshot+"保存诊断。"
                        :"已收到"+f.Key+"和观察输入，但尚未进入绕看；请按"+f.Snapshot+"保存诊断。";
                }
            }
            if(!Introduced&&f.CanIntroduce&&!f.Held)
            {
                Introduced=true;
                if(f.Block==ObservationBlockReason.FeatureDisabled)return "C2a站定观察已在配置中关闭；启用“镜头·C2／启用站定观察”后再试。";
                return "C2a操作："+f.Toggle+"开关第三人称；持枪站定，按住"+f.Key+"并移动鼠标可绕看人物，松开后交接朝向。";
            }
            return null;
        }
        static string Denied(ObservationFeedbackFrame f)=>"未进入绕看："+
            (f.Block==ObservationBlockReason.WaitingRelease&&f.LastRejected!=ObservationBlockReason.None&&f.LastRejected!=ObservationBlockReason.WaitingRelease
                ?"上次未进入原因："+Describe(f.LastRejected,f.Toggle)+"，需要先松开观察键":BlockedDescription(f))+
            "。请松开"+f.Key+"，满足条件后再按。";
        static string BlockedDescription(ObservationFeedbackFrame f)
        {
            if(f.Block!=ObservationBlockReason.Moving||!f.Motion.Valid)return Describe(f.Block,f.Toggle);
            var culture=System.Globalization.CultureInfo.InvariantCulture;
            if(f.Motion.InputRequested)return "检测到移动输入（横轴"+f.Motion.AxisX.ToString("0.###",culture)+"、纵轴"+f.Motion.AxisY.ToString("0.###",culture)+"），请松开方向键或回正移动摇杆后站定";
            return "水平移动尚未停稳（"+f.Motion.HorizontalSpeed.ToString("0.###",culture)+"米/秒），请站定";
        }
        public static string Describe(ObservationBlockReason reason,string toggle)=>reason switch
        {
            ObservationBlockReason.ViewDisabled=>"第三人称未开启，请按"+toggle,
            ObservationBlockReason.FeatureDisabled=>"站定观察已在配置中关闭",
            ObservationBlockReason.Fault=>"相机已因异常停用，请按F8保存诊断",
            ObservationBlockReason.NoPlayer=>"本机角色或相机尚未就绪",
            ObservationBlockReason.Focus=>"游戏失焦或菜单／聊天输入尚未关闭",
            ObservationBlockReason.ControlTaken=>"其他相机或微操接管正在占用视角",
            ObservationBlockReason.UnsupportedState=>"当前人物状态不支持绕看",
            ObservationBlockReason.UnsupportedItem=>"请先持标准／特殊槽枪械，工具和近战暂不支持",
            ObservationBlockReason.Aiming=>"正在瞄准，请先退出肩射／开镜",
            ObservationBlockReason.Reloading=>"正在换弹",
            ObservationBlockReason.Busy=>"装备正在忙碌或尚未就绪",
            ObservationBlockReason.Action=>"正在开火或交互",
            ObservationBlockReason.Moving=>"尚未满足站定条件，请松开方向键后站定",
            ObservationBlockReason.MovementUnavailable=>"移动数据暂不可用，请保存诊断",
            ObservationBlockReason.NoDisplay=>"当前未能稳定显示第三人称，可能正在回退或等待模型就绪",
            ObservationBlockReason.LongFrame=>"当前画面停顿，观察状态已重置",
            ObservationBlockReason.WaitingRelease=>"上一次进入被打断，需要先松开观察键",
            _=>"观察尚未就绪"
        };
        static string ExitReason(ObservationFeedbackFrame f)=>f.Exit switch
        {
            "collision"=>"空间不足",
            "fire"=>"开始开火",
            "move"=>"开始移动",
            "context-change"=>"角色或场景变化",
            "invalid-aim"=>"瞄准路径不可用",
            "native-reload"=>"开始换弹",
            _=>!f.Held?"已松开观察键":f.Block!=ObservationBlockReason.None&&f.Block!=ObservationBlockReason.WaitingRelease?Describe(f.Block,f.Toggle):"动作或状态发生变化"
        };
    }
}
