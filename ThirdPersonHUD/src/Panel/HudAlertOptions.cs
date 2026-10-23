using System;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudAlertOptions
    {
        public bool Enabled { get; init; }
        public bool Actions { get; init; }
        public bool HitFeedback { get; init; }
        public bool StateFeedback { get; init; }
        public bool ResourceWarnings { get; init; }
        public int MaximumVisible { get; init; }
        public int LowReserveRounds { get; init; }
        public float LowToolRatio { get; init; }
        public float HitSeconds { get; init; }
        public static HudAlertOptions Default => new(){Enabled=true,Actions=true,HitFeedback=true,StateFeedback=true,
            ResourceWarnings=true,MaximumVisible=2,LowReserveRounds=20,LowToolRatio=.2f,HitSeconds=1.2f};
        public HudAlertOptions Sanitized()=>this with{MaximumVisible=Math.Clamp(MaximumVisible,1,3),LowReserveRounds=Math.Clamp(LowReserveRounds,0,1000),
            LowToolRatio=HudPanelLayoutOptions.Clamp(LowToolRatio,0,1,.2f),HitSeconds=HudPanelLayoutOptions.Clamp(HitSeconds,.1f,5,1.2f)};
    }
}
