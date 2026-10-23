#nullable enable
using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPersonHUD.Content
{
    public enum HudInformationId
    {
        Unknown, Health, Infection, LifeState, EquipmentIdentity, EquipmentName, SelectedSlot,
        Magazine, ReserveRounds, SupplyRatio, ToolResource, ToolDeployment, ResourcePackUses,
        ConsumableCount, CarryObject, Action, ActionProgress, DamageFeedback, ResourceWarning,
        TeamIdentity, TeamHealth, TeamDowned, TeamResources, Interaction, InteractionKey,
        InteractionProgress, SupplyRecipient, Mission, ScanProgress, Countdown, SceneMarker
    }
    public enum HudVisibility { Unknown, Internal, Persistent, Conditional, Expanded }
    public enum HudPriority { Unknown, Detail, Summary, Primary, Urgent }
    public readonly record struct HudInformationRule(HudInformationId Id, string Label, HudUnit Unit,
        HudVisibility Visibility, HudPriority Priority, HudFreshness Freshness, bool EnabledByDefault, int FirstBatch)
    {
        public bool IsValid => (int)Id > (int)HudInformationId.Unknown && (int)Id <= (int)HudInformationId.SceneMarker
            && !string.IsNullOrWhiteSpace(Label) && Enum.IsDefined(typeof(HudUnit), Unit)
            && Visibility is HudVisibility.Internal or HudVisibility.Persistent or HudVisibility.Conditional or HudVisibility.Expanded
            && (int)Priority >= (int)HudPriority.Detail && (int)Priority <= (int)HudPriority.Urgent
            && Freshness is HudFreshness.Live or HudFreshness.Identity or HudFreshness.Action
            && FirstBatch is >= 1 and <= 9;
    }

    // Product defaults only. Immutable rule values can be copied with user overrides later;
    // labels, priority and visibility never grant rendering eligibility or read game objects.
    public static class HudInformationCatalog
    {
        static HudInformationRule R(HudInformationId id, string label, HudUnit unit, HudVisibility visibility,
            HudPriority priority, HudFreshness freshness, int batch) => new(id, label, unit, visibility, priority, freshness, true, batch);
        public static IReadOnlyList<HudInformationRule> Defaults { get; } = Array.AsReadOnly(new[]
        {
            R(HudInformationId.Health, "生命", HudUnit.Fraction, HudVisibility.Persistent, HudPriority.Primary, HudFreshness.Live, 4),
            R(HudInformationId.Infection, "感染", HudUnit.Fraction, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Live, 4),
            R(HudInformationId.LifeState, "角色状态", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Urgent, HudFreshness.Live, 4),
            R(HudInformationId.EquipmentIdentity, "装备身份", HudUnit.Unknown, HudVisibility.Internal, HudPriority.Detail, HudFreshness.Identity, 4),
            R(HudInformationId.EquipmentName, "装备", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Identity, 4),
            R(HudInformationId.SelectedSlot, "当前手持", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Live, 4),
            R(HudInformationId.Magazine, "弹匣", HudUnit.Rounds, HudVisibility.Persistent, HudPriority.Primary, HudFreshness.Live, 4),
            R(HudInformationId.ReserveRounds, "备用", HudUnit.Rounds, HudVisibility.Persistent, HudPriority.Primary, HudFreshness.Live, 4),
            R(HudInformationId.SupplyRatio, "补给比例", HudUnit.Fraction, HudVisibility.Expanded, HudPriority.Detail, HudFreshness.Live, 4),
            R(HudInformationId.ToolResource, "工具", HudUnit.Fraction, HudVisibility.Persistent, HudPriority.Summary, HudFreshness.Live, 4),
            R(HudInformationId.ToolDeployment, "部署状态", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Live, 4),
            R(HudInformationId.ResourcePackUses, "补给次数", HudUnit.Uses, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Live, 4),
            R(HudInformationId.ConsumableCount, "物品数量", HudUnit.Items, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Live, 4),
            R(HudInformationId.CarryObject, "搬运", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Identity, 4),
            R(HudInformationId.Action, "当前动作", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Action, 5),
            R(HudInformationId.ActionProgress, "动作进度", HudUnit.Fraction, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Action, 5),
            R(HudInformationId.DamageFeedback, "状态变化", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Urgent, HudFreshness.Action, 5),
            R(HudInformationId.ResourceWarning, "资源警告", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Urgent, HudFreshness.Live, 5),
            R(HudInformationId.TeamIdentity, "队员", HudUnit.Unknown, HudVisibility.Persistent, HudPriority.Summary, HudFreshness.Identity, 7),
            R(HudInformationId.TeamHealth, "队员生命", HudUnit.Fraction, HudVisibility.Persistent, HudPriority.Summary, HudFreshness.Live, 7),
            R(HudInformationId.TeamDowned, "队员倒地", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Urgent, HudFreshness.Live, 7),
            R(HudInformationId.TeamResources, "队员资源", HudUnit.Fraction, HudVisibility.Expanded, HudPriority.Detail, HudFreshness.Live, 7),
            R(HudInformationId.Interaction, "交互", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Action, 8),
            R(HudInformationId.InteractionKey, "操作键", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Action, 8),
            R(HudInformationId.InteractionProgress, "交互进度", HudUnit.Fraction, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Action, 8),
            R(HudInformationId.SupplyRecipient, "补给对象", HudUnit.Unknown, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Action, 8),
            R(HudInformationId.Mission, "任务", HudUnit.Unknown, HudVisibility.Expanded, HudPriority.Detail, HudFreshness.Identity, 8),
            R(HudInformationId.ScanProgress, "扫描进度", HudUnit.Fraction, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Live, 8),
            R(HudInformationId.Countdown, "倒计时", HudUnit.Seconds, HudVisibility.Conditional, HudPriority.Primary, HudFreshness.Live, 8),
            R(HudInformationId.SceneMarker, "场景标记", HudUnit.Meters, HudVisibility.Conditional, HudPriority.Summary, HudFreshness.Action, 8)
        });
        public static HudInformationRule GetDefault(HudInformationId id)
        { foreach (var rule in Defaults) if (rule.Id == id) return rule; return default; }
    }
}
