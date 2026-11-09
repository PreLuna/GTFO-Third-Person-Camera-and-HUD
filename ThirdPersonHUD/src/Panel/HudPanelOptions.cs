using System;
using System.Numerics;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public enum HudLayoutStyle { Compact, DivisionV3, FourCorner, Radial }
    public enum HudDivisionAnchor { Auto, Shoulder, Centered }
    public enum HudDivisionEmphasis { Ammunition, Health }
    public readonly record struct HudDivisionLayout(float Width, float Scale, HudDivisionAnchor Anchor,
        float ShoulderX, float CenteredX, float Top, float OffsetX, float OffsetY, bool Mirror,
        int HealthSegments, HudDivisionEmphasis Emphasis, float TileSlant)
    {
        public bool FollowBody { get; init; }
        public float WaistHeight { get; init; }
        public float FollowSeconds { get; init; }
        public float TiltDegrees { get; init; }
        public float NumberWidth { get; init; }
        public static HudDivisionLayout Default => new(252,1,HudDivisionAnchor.Auto,860,960,686,0,0,true,20,HudDivisionEmphasis.Ammunition,3)
            {FollowBody=true,WaistHeight=.9f,FollowSeconds=.10f,TiltDegrees=2,NumberWidth=.82f};
        public HudDivisionLayout Sanitized() => new(
            HudPanelLayoutOptions.Clamp(Width,252,420,252),HudPanelLayoutOptions.Clamp(Scale,.75f,1.6f,1),
            Enum.IsDefined(typeof(HudDivisionAnchor),Anchor)?Anchor:HudDivisionAnchor.Auto,
            HudPanelLayoutOptions.Clamp(ShoulderX,0,1920,860),HudPanelLayoutOptions.Clamp(CenteredX,0,1920,960),
            HudPanelLayoutOptions.Clamp(Top,0,1080,686),HudPanelLayoutOptions.Clamp(OffsetX,-1000,1000,0),
            HudPanelLayoutOptions.Clamp(OffsetY,-1000,1000,0),Mirror,Math.Clamp(HealthSegments,1,20),
            Enum.IsDefined(typeof(HudDivisionEmphasis),Emphasis)?Emphasis:HudDivisionEmphasis.Ammunition,
            HudPanelLayoutOptions.Clamp(TileSlant,0,8,3))
            {FollowBody=FollowBody,WaistHeight=HudPanelLayoutOptions.Clamp(WaistHeight,.4f,1.5f,.9f),
                FollowSeconds=HudPanelLayoutOptions.Clamp(FollowSeconds,0,.4f,.10f),TiltDegrees=HudPanelLayoutOptions.Clamp(TiltDegrees,-4,4,2),
                NumberWidth=HudPanelLayoutOptions.Clamp(NumberWidth,.7f,1,.82f)};
    }
    // Shared geometry for the two archived wireframe layouts. They intentionally reuse the
    // validated content/view models but never the Compact or DivisionV3 drawing paths.
    public readonly record struct HudClassicLayout(float Scale, float Margin, float Top, float Bottom,
        float RadialScale, float RadialOffsetX, float RadialOffsetY, bool FollowBody, float WaistHeight,
        float FollowSeconds, float LabelSize, float BodySize, float NumberSize)
    {
        public static HudClassicLayout Default => new(1, 48, 48, 48, 1, 0, 100, true, .9f, .10f, 11, 14, 26);
        public HudClassicLayout Sanitized() => new(
            HudPanelLayoutOptions.Clamp(Scale, .65f, 1.6f, 1),
            HudPanelLayoutOptions.Clamp(Margin, 0, 100, 48),
            HudPanelLayoutOptions.Clamp(Top, 0, 300, 48),
            HudPanelLayoutOptions.Clamp(Bottom, 0, 300, 48),
            HudPanelLayoutOptions.Clamp(RadialScale, .6f, 1.6f, 1),
            HudPanelLayoutOptions.Clamp(RadialOffsetX, -600, 600, 0),
            HudPanelLayoutOptions.Clamp(RadialOffsetY, -600, 600, 100),
            FollowBody,
            HudPanelLayoutOptions.Clamp(WaistHeight, .4f, 1.5f, .9f),
            HudPanelLayoutOptions.Clamp(FollowSeconds, 0, .4f, .10f),
            HudPanelLayoutOptions.Clamp(LabelSize, 9, 18, 11),
            HudPanelLayoutOptions.Clamp(BodySize, 11, 22, 14),
            HudPanelLayoutOptions.Clamp(NumberSize, 18, 48, 26));
    }
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
        public HudLayoutStyle Style { get; init; }
        public HudDivisionLayout Division { get; init; }
        public HudClassicLayout Classic { get; init; }
        public bool Enabled { get; init; }
        public bool Expanded { get; init; }
        public bool Team { get; init; }
        public bool Streamlined { get; init; }
        public bool TeamPage { get; init; }
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
        public static HudPanelLayoutOptions Default => new() { Division=HudDivisionLayout.Default, Classic=HudClassicLayout.Default, Enabled = true, MirrorWithShoulder = true, FixedSide = 1,
            Width = 304, Scale = 1, AnchorHeight = 1.15f, AnchorSide = .8f,
            RightOffset = new(152, -48), LeftOffset = new(-152, -48), ScreenMargin = 20,
            CrosshairRadius = 72, AimScale = .9f, AimOffset = new(20, -24), FollowSeconds = .12f };
        public HudPanelLayoutOptions Sanitized() => this with {
            Style=Enum.IsDefined(typeof(HudLayoutStyle),Style)?Style:HudLayoutStyle.DivisionV3, Division=Division.Sanitized(), Classic=Classic.Sanitized(),
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
        public static HudPanelTheme DivisionV3 => Default with {Base=new(89/255f,200/255f,232/255f),Text=new(236/255f,232/255f,226/255f),
            Attention=new(224/255f,138/255f,46/255f),Danger=new(200/255f,69/255f,47/255f),Background=new(19/255f,23/255f,26/255f),
            BackgroundOpacity=.48f,TextOpacity=1,Glow=false,ScanLines=false};
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
