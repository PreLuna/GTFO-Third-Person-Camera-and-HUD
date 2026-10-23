using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudPersonalSettings
    {
        readonly ConfigEntry<bool> enabled,health,infection,weapons,tools,items,supply,focus,automatic,feedback;
        readonly ConfigEntry<int> decimals;
        readonly ConfigEntry<float> seconds;
        internal HudPersonalSettings(ConfigFile config)
        {
            var d=HudPersonalOptions.Default;
            enabled=config.Bind("Personal","Enabled",d.Enabled,"在全息面板显示真实个人读数；不能绕过第三人称显示资格。");
            health=config.Bind("Personal","Health",d.Health,"显示当前生命、生命条与数值变化。");
            infection=config.Bind("Personal","Infection",d.Infection,"感染大于零、变化、不可读或展开时显示；有效零值不冒充缺失。");
            weapons=config.Bind("Personal","Weapons",d.Weapons,"显示枪械、近战和主副武器摘要。");
            tools=config.Bind("Personal","Tools",d.Tools,"显示工具名称、资源与可靠部署状态。");
            items=config.Bind("Personal","Items",d.Items,"显示补给、消耗品、重型物品与搬运对象。");
            supply=config.Bind("Personal","SupplyRatio",d.SupplyRatio,"显示当前枪械或工具的补给资源比例，与弹匣/备用弹数分开。");
            focus=config.Bind("Personal","FocusSelected",d.FocusSelected,"主读数区突出当前手持装备；关闭时明确显示主武器摘要。");
            automatic=config.Bind("Personal","AutoExpandSelected",d.AutoExpandSelected,"选中工具、补给、消耗品或搬运对象时自动展开详情，可靠采集后生效；仍可按键展开。");
            feedback=config.Bind("Personal","ValueFeedback",d.ValueFeedback,"显示同一角色的新生命/感染变化；数据缺失或换身份立即清除。");
            decimals=config.Bind("Personal","PercentDecimals",d.PercentDecimals,new ConfigDescription("百分比小数位；未取整的真实值仍用于条形及状态判断。",new AcceptableValueRange<int>(0,2)));
            seconds=config.Bind("Personal","FeedbackSeconds",d.FeedbackSeconds,new ConfigDescription("生命/感染数值变化显示秒数；重复绘制不延长。",new AcceptableValueRange<float>(.1f,5)));
        }
        internal HudPersonalOptions Read()=>new HudPersonalOptions{Enabled=enabled.Value,Health=health.Value,Infection=infection.Value,Weapons=weapons.Value,
            Tools=tools.Value,Items=items.Value,SupplyRatio=supply.Value,FocusSelected=focus.Value,AutoExpandSelected=automatic.Value,
            ValueFeedback=feedback.Value,PercentDecimals=decimals.Value,FeedbackSeconds=seconds.Value}.Sanitized();
    }
}
