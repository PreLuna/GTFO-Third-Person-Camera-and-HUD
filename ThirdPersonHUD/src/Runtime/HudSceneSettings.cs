using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudSceneSettings
    {
        readonly ConfigEntry<bool> enabled,interaction,supply,mission,scan,markers,rescue,pings,detected,replace;
        readonly ConfigEntry<int> maximum,candidates;
        readonly ConfigEntry<float> distance,scale,width,offset,bottom,margin,crosshair;
        internal HudSceneSettings(ConfigFile c)
        {
            var d=HudSceneOptions.Default;
            enabled=c.Bind("Scene","Enabled",d.Enabled,"启用交互、任务和已知场景提示；仍仅在有效本机第三人称主画面显示。");
            interaction=c.Bind("Scene","Interaction",d.Interaction,"显示当前选中对象、操作、实际键鼠绑定及可靠长按进度。");
            supply=c.Bind("Scene","Supply",d.Supply,"补给使用期间显示实际接收对象、物品及剩余次数。");
            mission=c.Bind("Scene","Mission",d.Mission,"展开时显示已公布任务摘要，常驻显示游戏当前倒计时。");
            scan=c.Bind("Scene","Scan",d.Scan,"显示游戏当前最近扫描的可靠进度；不推算预计时长。");
            markers=c.Bind("Scene","Markers",d.Markers,"显示有限数量的已知标记，避让准星、面板及交互卡片。");
            rescue=c.Bind("Scene","Rescue",d.Rescue,"倒地队员方向提示。");pings=c.Bind("Scene","Pings",d.Pings,"实际可见的队友标记。");
            detected=c.Bind("Scene","Detected",d.Detected,"仅当前本机扫描器已发现列表，不搜索隐藏敌人。");
            replace=c.Bind("Scene","ReplaceCountdown",d.ReplaceCountdown,"成功完整绘制后替代对应原版倒计时文字；其他合并信息继续保留。");
            maximum=c.Bind("Scene","MaximumMarkers",d.MaximumMarkers,new ConfigDescription("最多显示的场景标签数，0 关闭。",new AcceptableValueRange<int>(0,3)));
            candidates=c.Bind("Scene","CandidateLimit",d.CandidateLimit,new ConfigDescription("每次最多检查多少个扫描器已发现条目。",new AcceptableValueRange<int>(1,32)));
            distance=c.Bind("Scene","MaximumDistance",d.MaximumDistance,new ConfigDescription("场景标签最大距离，米。",new AcceptableValueRange<float>(5,500)));
            scale=c.Bind("Scene","CardScale",d.CardScale,new ConfigDescription("交互/任务卡片缩放。",new AcceptableValueRange<float>(.65f,1.4f)));
            width=c.Bind("Scene","CardWidth",d.CardWidth,new ConfigDescription("卡片逻辑宽度。",new AcceptableValueRange<float>(280,480)));
            offset=c.Bind("Scene","InteractionOffsetX",d.InteractionOffsetX,new ConfigDescription("交互卡片水平偏移；自动限制在视口内并避让个人面板。",new AcceptableValueRange<float>(-600,600)));
            bottom=c.Bind("Scene","InteractionBottom",d.InteractionBottom,new ConfigDescription("交互卡片距底部的逻辑距离。",new AcceptableValueRange<float>(20,400)));
            margin=c.Bind("Scene","ScreenMargin",d.ScreenMargin,new ConfigDescription("卡片与场景标签屏幕边距。",new AcceptableValueRange<float>(8,100)));
            crosshair=c.Bind("Scene","MarkerCrosshairRadius",d.MarkerCrosshairRadius,new ConfigDescription("场景标签避让准星的逻辑半径，0 关闭避让。",new AcceptableValueRange<float>(0,200)));
        }
        internal HudSceneOptions Read()=>new HudSceneOptions{Enabled=enabled.Value,Interaction=interaction.Value,Supply=supply.Value,Mission=mission.Value,
            Scan=scan.Value,Markers=markers.Value,Rescue=rescue.Value,Pings=pings.Value,Detected=detected.Value,ReplaceCountdown=replace.Value,
            MaximumMarkers=maximum.Value,CandidateLimit=candidates.Value,MaximumDistance=distance.Value,CardScale=scale.Value,CardWidth=width.Value,
            InteractionOffsetX=offset.Value,InteractionBottom=bottom.Value,ScreenMargin=margin.Value,MarkerCrosshairRadius=crosshair.Value}.Sanitized();
    }
}
