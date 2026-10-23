using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudReplacementSettings
    {
        readonly ConfigEntry<bool> enabled,health,infection,ammo,tool,items;
        readonly ConfigEntry<KeyCode> restoreKey;
        internal HudReplacementSettings(ConfigFile config)
        {
            var d=HudReplacementOptions.Default;
            enabled=config.Bind("OriginalHUD","Enabled",d.Enabled,"成功绘制且数据完整时替代对应原版读数；关闭立即恢复本模组的隐藏状态。");
            health=config.Bind("OriginalHUD","Health",d.Health,"替代生命条及百分比；保留空气、护盾、耐力、强化剂和未替代的原版动画。");
            infection=config.Bind("OriginalHUD","Infection",d.Infection,"替代感染条及百分比；数据失效自动恢复。");
            ammo=config.Bind("OriginalHUD","Ammo",d.Ammo,"替代主副武器绝对弹数；保留原版补给比例、容量和无限弹药标识。");
            tool=config.Bind("OriginalHUD","Tool",d.Tool,"替代可靠携带工具的资源百分比；原版工具弹匣及特殊状态继续保留。");
            items=config.Bind("OriginalHUD","Items",d.Items,"仅展开且三类物品数据完整时替代可靠的使用次数/数量文本；特殊格式保留原版。");
            restoreKey=config.Bind("OriginalHUD","RestoreKey",KeyCode.Pause,"一键恢复原版并锁定暂停替代；再次按下允许重新校验后替代，可改绑。");
        }
        internal HudReplacementOptions Read()=>new(enabled.Value,health.Value,infection.Value,ammo.Value,tool.Value,items.Value);
        internal KeyCode RestoreKey=>restoreKey.Value;
    }
}
