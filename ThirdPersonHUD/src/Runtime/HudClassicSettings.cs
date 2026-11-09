using System;
using System.Globalization;
using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // Shared visual/geometry settings for the archived FourCorner and Radial wireframe layouts.
    // DivisionV3 and Compact keep their own groups and values; selecting a classic style never resets them.
    internal sealed class HudClassicSettings
    {
        readonly ConfigEntry<float> scale, margin, top, bottom, radialScale, radialX, radialY, waist, follow;
        readonly ConfigEntry<float> label, body, number, background;
        readonly ConfigEntry<bool> followBody;
        readonly ConfigEntry<string> accent, text, team, danger;

        internal HudClassicSettings(ConfigFile config)
        {
            var d = HudClassicLayout.Default;
            ConfigEntry<float> N(string group, string key, float value, float min, float max, string explanation)
                => config.Bind(group, key, value, new ConfigDescription(explanation, new AcceptableValueRange<float>(min, max)));
            scale = N("Classic.Layout", "Scale", d.Scale, .65f, 1.6f, "四角/环绕布局整体缩放；1080p 基准为 1。");
            margin = N("Classic.Layout", "Margin", d.Margin, 0, 100, "四角布局屏幕安全边距，1080p 像素；环绕布局也用它限制圆心和标签。");
            top = N("Classic.Layout", "Top", d.Top, 0, 300, "四角布局顶部面板的 1080p 基准 y，向下为正。");
            bottom = N("Classic.Layout", "Bottom", d.Bottom, 0, 300, "四角布局底部面板距底边的 1080p 基准距离。");
            radialScale = N("Classic.Layout", "RadialScale", d.RadialScale, .6f, 1.6f, "环绕布局生命/感染弧的半径倍率；默认 1 对应 170px。");
            radialX = N("Classic.Layout", "RadialOffsetX", d.RadialOffsetX, -600, 600, "环绕圆心相对角色腰部投影的横向偏移，1080p 像素，向右为正。");
            radialY = N("Classic.Layout", "RadialOffsetY", d.RadialOffsetY, -600, 600, "环绕圆心相对角色腰部投影的纵向偏移，1080p 像素，向下为正。");
            followBody = config.Bind("Classic.Layout", "FollowBody", d.FollowBody, "环绕圆心跟随当前主相机投影的角色腰部参考点；关闭则使用视口中心。");
            waist = N("Classic.Layout", "WaistHeight", d.WaistHeight, .4f, 1.5f, "环绕圆心参考点相对角色根的世界高度，米；不是骨骼采样。");
            follow = N("Classic.Layout", "FollowSeconds", d.FollowSeconds, 0, .4f, "环绕圆心的短平滑；失效、切换或超出安全区时立即重置。0 关闭。");
            label = N("Classic.Text", "LabelSize", d.LabelSize, 9, 18, "四角/环绕标签字号；线框中的 7–9px 仅作参考。");
            body = N("Classic.Text", "BodySize", d.BodySize, 11, 22, "正文/普通读数字号。");
            number = N("Classic.Text", "NumberSize", d.NumberSize, 18, 48, "主数值字号；过长时按实际字体测量缩小。");
            background = N("Classic.Theme", "BackgroundOpacity", .72f, 0, 1, "四角/环绕小面板背景透明度；不绘制全屏遮罩。");
            accent = config.Bind("Classic.Theme", "AccentColor", "#E08A2E", "强调色，#RRGGBB。");
            text = config.Bind("Classic.Theme", "TextColor", "#ECE8E2", "文字与生命弧色，#RRGGBB。");
            team = config.Bind("Classic.Theme", "TeamColor", "#59C8E8", "队友/存活状态色，#RRGGBB。");
            danger = config.Bind("Classic.Theme", "DangerColor", "#C8452F", "危险/倒地色，#RRGGBB。");
        }

        internal HudPanelOptions Read(HudPanelOptions compact, HudLayoutStyle style)
        {
            var classic = new HudClassicLayout(scale.Value, margin.Value, top.Value, bottom.Value, radialScale.Value,
                radialX.Value, radialY.Value, followBody.Value, waist.Value, follow.Value, label.Value, body.Value, number.Value).Sanitized();
            var theme = HudPanelTheme.DivisionV3;
            theme = theme with
            {
                Base = Color(team.Value, theme.Base), Text = Color(text.Value, theme.Text),
                Attention = Color(accent.Value, theme.Attention), Danger = Color(danger.Value, theme.Danger),
                BackgroundOpacity = background.Value, Glow = false, ScanLines = false
            };
            return (compact with
            {
                Layout = compact.Layout with { Style = style, Classic = classic },
                Theme = theme
            }).Sanitized();
        }

        static HudColor Color(string raw, HudColor fallback)
        {
            string hex = raw?.Trim().TrimStart('#');
            return hex?.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? new((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f)
                : fallback;
        }
    }
}
