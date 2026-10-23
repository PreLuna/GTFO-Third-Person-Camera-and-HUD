namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudTeamOptions(bool Enabled, bool Details, bool Cooperation, bool ReplaceOriginal,
        float LowAmmoRatio, float LowToolRatio, float MarkSeconds)
    {
        public static HudTeamOptions Default => new(true, true, true, true, .2f, .2f, 3);
        public HudTeamOptions Sanitized() => this with { LowAmmoRatio = HudPanelLayoutOptions.Clamp(LowAmmoRatio, 0, 1, .2f),
            LowToolRatio = HudPanelLayoutOptions.Clamp(LowToolRatio, 0, 1, .2f), MarkSeconds = HudPanelLayoutOptions.Clamp(MarkSeconds, .1f, 10, 3) };
    }
}
