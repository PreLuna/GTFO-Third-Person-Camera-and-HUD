using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudAlertSettings
    {
        readonly ConfigEntry<bool> enabled,actions,hits,states,resources;
        readonly ConfigEntry<int> visible,reserve;
        readonly ConfigEntry<float> tool,seconds;
        internal HudAlertSettings(ConfigFile config)
        {
            var d=HudAlertOptions.Default;
            enabled=config.Bind("Alerts","Enabled",d.Enabled,"启用独立提示区；仍需有效的本机第三人称画面。");
            actions=config.Bind("Alerts","Actions",d.Actions,"显示可靠的装备动作；没有可靠进度时只显示进行中。");
            hits=config.Bind("Alerts","HitFeedback",d.HitFeedback,"显示原生受击反馈及可靠方向；观察点不可用时不推测方向。");
            states=config.Bind("Alerts","StateFeedback",d.StateFeedback,"显示生命危急、感染过高、倒地及数值变化。沿用 Behavior 阈值和 Personal 变化持续时间。");
            resources=config.Bind("Alerts","ResourceWarnings",d.ResourceWarnings,"显示当前有效装备的资源不足/耗尽；缺失、未装备和过期不触发。");
            visible=config.Bind("Alerts","MaximumVisible",d.MaximumVisible,new ConfigDescription("同时显示提示条数；紧急信息优先，同类合并，不排队补播。",new AcceptableValueRange<int>(1,3)));
            reserve=config.Bind("Alerts","LowReserveRounds",d.LowReserveRounds,new ConfigDescription("主/副武器实际备用弹数不高于该值时警告；0 只警告耗尽。",new AcceptableValueRange<int>(0,1000)));
            tool=config.Bind("Alerts","LowToolRatio",d.LowToolRatio,new ConfigDescription("工具资源比例不高于该值时警告；0 只警告耗尽。",new AcceptableValueRange<float>(0,1)));
            seconds=config.Bind("Alerts","HitSeconds",d.HitSeconds,new ConfigDescription("受击提示秒数，从原生事件时刻计算；刷新及被其他提示压住均不延长。",new AcceptableValueRange<float>(.1f,5)));
        }
        internal HudAlertOptions Read()=>new HudAlertOptions{Enabled=enabled.Value,Actions=actions.Value,HitFeedback=hits.Value,StateFeedback=states.Value,
            ResourceWarnings=resources.Value,MaximumVisible=visible.Value,LowReserveRounds=reserve.Value,LowToolRatio=tool.Value,HitSeconds=seconds.Value}.Sanitized();
    }
}
