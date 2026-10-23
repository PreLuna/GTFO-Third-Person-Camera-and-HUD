using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudSettings
    {
        readonly ConfigEntry<bool> enabled;
        readonly ConfigEntry<float> delay, retry, height, side, size;
        readonly ConfigEntry<float> budget, refresh, age, logInterval;
        readonly ConfigEntry<int> preparationSteps;
        readonly ConfigEntry<HudComputationMode> computationMode;
        readonly ConfigEntry<float> computationRetry;
        readonly ConfigEntry<bool> collectContent;
        readonly ConfigEntry<float> collectionRetry;
        readonly HudPanelSettings panel;
        readonly HudPersonalSettings personal;
        readonly HudAlertSettings alerts;
        readonly HudReplacementSettings replacement;
        readonly HudTeamSettings team;
        readonly HudSceneSettings scene;
        internal HudSettings(ConfigFile config)
        {
            panel = new HudPanelSettings(config);
            personal = new HudPersonalSettings(config);
            alerts = new HudAlertSettings(config);
            replacement = new HudReplacementSettings(config);
            team = new HudTeamSettings(config);
            scene = new HudSceneSettings(config);
            enabled = config.Bind("Lifecycle", "Enabled", true, "启用 HUD 生命周期。关闭时释放 HUD 自有资源。");
            delay = config.Bind("Lifecycle", "ActivationDelaySeconds", .2f, new ConfigDescription("首次实际第三人称出图后的准备等待时间。", new AcceptableValueRange<float>(0, 3)));
            retry = config.Bind("Lifecycle", "RetryDelaySeconds", 1f, new ConfigDescription("初始化异常后再次准备的最短间隔。", new AcceptableValueRange<float>(.25f, 10)));
            height = config.Bind("Placeholder", "AnchorHeight", 1.2f, new ConfigDescription("占位锚点相对角色根位置的世界高度，米。", new AcceptableValueRange<float>(0, 3)));
            side = config.Bind("Placeholder", "AnchorSide", .65f, new ConfigDescription("沿最终相机右方向的偏移，米；负数在左侧。", new AcceptableValueRange<float>(-2, 2)));
            size = config.Bind("Placeholder", "MarkerSizePixels", 12f, new ConfigDescription("定位方块的像素边长，仅供验证。", new AcceptableValueRange<float>(4, 48)));
            budget = config.Bind("Work", "FrameBudgetMilliseconds", .5f, new ConfigDescription("初始化、采集、计算、应用和绘制共用的每帧软预算；0 暂停这些工作。原子步骤无法中断，超时会记录并停止本帧后续工作。", new AcceptableValueRange<float>(0, 5)));
            preparationSteps = config.Bind("Work", "MaxPreparationStepsPerFrame", 3, new ConfigDescription("每帧最多执行多少个初始化、采集或计算步骤。应用与绘制共用时间预算，不占此步骤数。", new AcceptableValueRange<int>(1, 16)));
            refresh = config.Bind("Work", "RefreshIntervalSeconds", .05f, new ConfigDescription("数据采集间隔，0 表示最多每帧一次；实际间隔不超过内容字段的最短有效期（当前动作/交互默认 0.1 秒）。", new AcceptableValueRange<float>(0, .5f)));
            age = config.Bind("Work", "MaximumDataAgeSeconds", .25f, new ConfigDescription("从采集时刻起算的数据有效期。过期后隐藏并等待新数据。", new AcceptableValueRange<float>(.05f, .5f)));
            logInterval = config.Bind("Performance", "LogIntervalSeconds", 10f, new ConfigDescription("活动期间汇总性能日志的间隔，0 关闭定时日志；卸载时仍记录汇总。", new AcceptableValueRange<float>(0, 300)));
            computationMode = config.Bind("Compute", "ExecutionMode", HudComputationMode.Background, "Background：仅将明确声明后台安全的纯数据计算交给工作线程；MainThread：全部在主线程执行。采集与 Unity 操作始终在主线程。");
            computationRetry = config.Bind("Compute", "ErrorRetryDelaySeconds", .5f, new ConfigDescription("纯计算异常后的冷却时间；保留仍有效的结果与资源。", new AcceptableValueRange<float>(.05f, 10)));
            collectContent = config.Bind("Content", "Enabled", true, "在有效本机第三人称下采集个人内容；关闭后面板显示缺失，不沿用旧读数。");
            collectionRetry = config.Bind("Content", "ErrorRetryDelaySeconds", .25f, new ConfigDescription("整次个人采集失败后的重试间隔；单个字段失败独立标记，不回填旧值。", new AcceptableValueRange<float>(.05f, 10)));
        }
        internal HudLifecycleOptions Read() => new(enabled.Value, delay.Value, retry.Value, height.Value, side.Value, size.Value);
        internal HudWorkOptions ReadWork() => new(budget.Value, preparationSteps.Value, refresh.Value, age.Value);
        internal HudExecutionOptions ReadExecution() => new(computationMode.Value, computationRetry.Value);
        internal HudCollectionOptions ReadCollection() => new(collectContent.Value, collectionRetry.Value);
        internal HudPanelOptions ReadPanel() => panel.Read();
        internal HudPersonalOptions ReadPersonal() => personal.Read();
        internal HudAlertOptions ReadAlerts() => alerts.Read();
        internal HudReplacementOptions ReadReplacement() => replacement.Read();
        internal HudTeamOptions ReadTeam() => team.Read();
        internal HudSceneOptions ReadScene() => scene.Read();
        internal KeyCode RestoreKey => replacement.RestoreKey;
        internal KeyCode ExpandKey => panel.ExpandKey;
        internal bool ToggleExpand => panel.ToggleExpand;
        internal double LogIntervalSeconds => float.IsFinite(logInterval.Value) ? logInterval.Value : 10;
    }
}
