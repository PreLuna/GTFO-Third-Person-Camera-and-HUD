using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudTeamSettings
    {
        readonly ConfigEntry<bool> enabled, details, cooperation, replace;
        readonly ConfigEntry<float> ammo, tool, mark;
        internal HudTeamSettings(ConfigFile config)
        {
            var d = HudTeamOptions.Default;
            enabled = config.Bind("Team", "Enabled", d.Enabled, "显示其他三名队员的摘要；关闭停止队伍采集并恢复对应原版读数。");
            details = config.Bind("Team", "Details", d.Details, "使用现有展开键显示游戏同步的弹药、工具比例和感染；不推测远端弹匣弹数。");
            cooperation = config.Bind("Team", "Cooperation", d.Cooperation, "突出救援、资源不足及实际收到的队员标记提示。");
            replace = config.Bind("Team", "ReplaceOriginal", d.ReplaceOriginal, "仅在完整覆盖且成功绘制时隐藏对应队员原版信息文本；方向、距离和场景标记保留。");
            ammo = config.Bind("Team", "LowAmmoRatio", d.LowAmmoRatio, new ConfigDescription("主副武器同步备用资源比例警告阈值。",new AcceptableValueRange<float>(0,1)));
            tool = config.Bind("Team", "LowToolRatio", d.LowToolRatio, new ConfigDescription("工具同步库存比例警告阈值；不代表已部署工具的剩余资源。",new AcceptableValueRange<float>(0,1)));
            mark = config.Bind("Team", "MarkSeconds", d.MarkSeconds, new ConfigDescription("实际队员 Ping 事件的通知保留秒数；不延长原版标记。",new AcceptableValueRange<float>(.1f,10)));
        }
        internal HudTeamOptions Read() => new HudTeamOptions(enabled.Value, details.Value, cooperation.Value, replace.Value, ammo.Value, tool.Value, mark.Value).Sanitized();
    }
}
