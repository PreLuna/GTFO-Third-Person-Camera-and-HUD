using System;
using System.Numerics;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudPersonalOptions
    {
        public bool Enabled { get; init; }
        public bool Health { get; init; }
        public bool Infection { get; init; }
        public bool Weapons { get; init; }
        public bool Tools { get; init; }
        public bool Items { get; init; }
        public bool SupplyRatio { get; init; }
        public bool FocusSelected { get; init; }
        public bool AutoExpandSelected { get; init; }
        public bool ValueFeedback { get; init; }
        public int PercentDecimals { get; init; }
        public float FeedbackSeconds { get; init; }
        public static HudPersonalOptions Default => new(){Enabled=true,Health=true,Infection=true,Weapons=true,Tools=true,Items=true,
            SupplyRatio=true,FocusSelected=true,AutoExpandSelected=true,ValueFeedback=true,FeedbackSeconds=1};
        public HudPersonalOptions Sanitized()=>this with{PercentDecimals=Math.Clamp(PercentDecimals,0,2),FeedbackSeconds=HudPanelLayoutOptions.Clamp(FeedbackSeconds,.1f,5,1)};
    }
    public readonly record struct HudColor(float R, float G, float B, float A = 1)
    {
        public HudColor Sanitized() => new(Unit(R), Unit(G), Unit(B), Unit(A));
        public HudColor Opacity(float alpha) => this with { A = Math.Clamp(A * alpha, 0, 1) };
        public HudColor Brightness(float value) => new(Math.Clamp(R * value, 0, 1), Math.Clamp(G * value, 0, 1), Math.Clamp(B * value, 0, 1), A);
        static float Unit(float v) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
    }
    public readonly record struct HudPanelLayoutOptions
    {
        public bool Enabled { get; init; }
        public bool Expanded { get; init; }
        public bool Team { get; init; }
        public bool MirrorWithShoulder { get; init; }
        public float FixedSide { get; init; }
        public float Width { get; init; }
        public float Scale { get; init; }
        public float AnchorHeight { get; init; }
        public float AnchorSide { get; init; }
        public Vector2 RightOffset { get; init; }
        public Vector2 LeftOffset { get; init; }
        public float ScreenMargin { get; init; }
        public float CrosshairRadius { get; init; }
        public float AimScale { get; init; }
        public Vector2 AimOffset { get; init; }
        public float FollowSeconds { get; init; }
        public static HudPanelLayoutOptions Default => new() { Enabled = true, MirrorWithShoulder = true, FixedSide = 1,
            Width = 304, Scale = 1, AnchorHeight = 1.15f, AnchorSide = .8f,
            RightOffset = new(152, -48), LeftOffset = new(-152, -48), ScreenMargin = 20,
            CrosshairRadius = 72, AimScale = .9f, AimOffset = new(20, -24), FollowSeconds = .12f };
        public HudPanelLayoutOptions Sanitized() => this with {
            FixedSide = FixedSide < 0 ? -1 : 1, Width = Clamp(Width, 260, 420, 304), Scale = Clamp(Scale, .65f, 1.6f, 1),
            AnchorHeight = Clamp(AnchorHeight, 0, 3, 1.15f), AnchorSide = Clamp(AnchorSide, 0, 2, .8f),
            RightOffset = Vector(RightOffset), LeftOffset = Vector(LeftOffset), AimOffset = Vector(AimOffset),
            ScreenMargin = Clamp(ScreenMargin, 0, 100, 20), CrosshairRadius = Clamp(CrosshairRadius, 0, 200, 72),
            AimScale = Clamp(AimScale, .7f, 1.2f, .9f), FollowSeconds = Clamp(FollowSeconds, 0, 1, .12f) };
        internal static float Clamp(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        static Vector2 Vector(Vector2 v) => new(Clamp(v.X, -1000, 1000, 0), Clamp(v.Y, -1000, 1000, 0));
    }
    public readonly record struct HudPanelTheme
    {
        public HudColor Base { get; init; }
        public HudColor Text { get; init; }
        public HudColor Attention { get; init; }
        public HudColor Danger { get; init; }
        public HudColor Background { get; init; }
        public float BackgroundOpacity { get; init; }
        public float DecorationOpacity { get; init; }
        public float TextOpacity { get; init; }
        public float Brightness { get; init; }
        public float DepthPixels { get; init; }
        public bool Decorations { get; init; }
        public bool Glow { get; init; }
        public bool ScanLines { get; init; }
        public static HudPanelTheme Default => new() { Base = new(135/255f,229/255f,240/255f), Text = new(237/255f,249/255f,1),
            Attention = new(1,176/255f,84/255f), Danger = new(1,92/255f,92/255f), Background = new(6/255f,17/255f,23/255f),
            BackgroundOpacity = .24f, DecorationOpacity = .58f, TextOpacity = .96f, Brightness = 1,
            DepthPixels = 7, Decorations = true, Glow = true };
        public HudPanelTheme Sanitized() => this with { Base=Base.Sanitized(), Text=Text.Sanitized(), Attention=Attention.Sanitized(),
            Danger=Danger.Sanitized(), Background=Background.Sanitized(),
            BackgroundOpacity=HudPanelLayoutOptions.Clamp(BackgroundOpacity,0,1,.24f), DecorationOpacity=HudPanelLayoutOptions.Clamp(DecorationOpacity,0,1,.58f),
            TextOpacity=HudPanelLayoutOptions.Clamp(TextOpacity,0,1,.96f), Brightness=HudPanelLayoutOptions.Clamp(Brightness,.2f,2,1),
            DepthPixels=HudPanelLayoutOptions.Clamp(DepthPixels,0,16,7) };
    }
    public readonly record struct HudPanelTypography(string FontName, float LabelSize, float BodySize, float NumberSize, bool Shadow)
    {
        public static HudPanelTypography Default => new("Microsoft YaHei", 13, 16, 52, true);
        public HudPanelTypography Sanitized() => new(string.IsNullOrWhiteSpace(FontName) ? "Microsoft YaHei" : FontName.Trim(),
            HudPanelLayoutOptions.Clamp(LabelSize,11,18,13),HudPanelLayoutOptions.Clamp(BodySize,14,22,16),
            HudPanelLayoutOptions.Clamp(NumberSize,36,64,52),Shadow);
    }
    public readonly record struct HudPanelBehavior(float CombatHoldSeconds, float LowHealth, float HighInfection,
        float ExplorationOpacity, float TransitionSeconds, bool ReduceMotion)
    {
        public static HudPanelBehavior Default => new(2.5f,.25f,.5f,.72f,.16f,false);
        public HudPanelBehavior Sanitized() => new(HudPanelLayoutOptions.Clamp(CombatHoldSeconds,0,10,2.5f),
            HudPanelLayoutOptions.Clamp(LowHealth,0,1,.25f),HudPanelLayoutOptions.Clamp(HighInfection,0,1,.5f),
            HudPanelLayoutOptions.Clamp(ExplorationOpacity,.3f,1,.72f),HudPanelLayoutOptions.Clamp(TransitionSeconds,0,1,.16f),ReduceMotion);
    }
    public readonly record struct HudPanelOptions(HudPanelLayoutOptions Layout, HudPanelTheme Theme, HudPanelTypography Typography, HudPanelBehavior Behavior)
    {
        public static HudPanelOptions Default => new(HudPanelLayoutOptions.Default,HudPanelTheme.Default,HudPanelTypography.Default,HudPanelBehavior.Default);
        public HudPanelOptions Sanitized() => new(Layout.Sanitized(),Theme.Sanitized(),Typography.Sanitized(),Behavior.Sanitized());
    }
}
