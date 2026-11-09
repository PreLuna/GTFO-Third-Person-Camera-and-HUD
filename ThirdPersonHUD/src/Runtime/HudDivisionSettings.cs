using System;
using System.Globalization;
using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // Separate namespaces retain all existing Compact values when moving between styles.
    internal sealed class HudDivisionSettings
    {
        readonly ConfigEntry<HudLayoutStyle> style;
        readonly ConfigEntry<KeyCode> cycleKey;
        readonly ConfigEntry<HudDivisionAnchor> anchor;
        readonly ConfigEntry<HudDivisionEmphasis> emphasis;
        readonly ConfigEntry<float> width,scale,shoulderX,centeredX,top,x,y,slant,background,label,body,number;
        readonly ConfigEntry<int> segments;
        readonly ConfigEntry<bool> mirror;
        readonly ConfigEntry<bool> followBody;
        readonly ConfigEntry<float> waistHeight,followSeconds,tilt,numberWidth;
        readonly ConfigEntry<string> accent,text,team,danger;
        internal HudDivisionSettings(ConfigFile c)
        {
            var d=HudDivisionLayout.Default;
            ConfigEntry<float> N(string group,string key,float value,float min,float max,string explanation)=>c.Bind(group,key,value,new ConfigDescription(explanation,new AcceptableValueRange<float>(min,max)));
            style=c.Bind("Presentation","Style",HudLayoutStyle.DivisionV3,"DivisionV3：腰胯附近橙白信息簇；FourCorner：四角任务/队伍/个人/装备面板；Radial：角色环绕生命弧、肩线标签与脚下装备条；Compact：保留0.11.4竖向精简版。只加载一份HUD。Insert总开关；End切换队友信息或Compact队伍页。");
            cycleKey=c.Bind("Presentation","CycleStyleKey",KeyCode.F5,"在有效本机第三人称下按此键循环 DivisionV3→FourCorner→Radial→Compact；配置文件 Style 决定下次启动样式，本键切换只保留到退出游戏。设为None关闭。");
            anchor=c.Bind("DivisionV3.Layout","Anchor",d.Anchor,"Auto：按当前主相机投影判断人物居中或越肩；Shoulder/Centered：固定使用对应基准锚点。不修改镜头。");
            width=N("DivisionV3.Layout","Width",d.Width,252,420,"1080p核心簇宽度，像素。");
            scale=N("DivisionV3.Layout","Scale",d.Scale,.75f,1.6f,"核心簇缩放。外围区域按实际视口高度独立缩放。");
            shoulderX=N("DivisionV3.Layout","ShoulderX",d.ShoulderX,0,1920,"1920宽基准的越肩核心左端x，默认860。");
            centeredX=N("DivisionV3.Layout","CenteredX",d.CenteredX,0,1920,"1920宽基准的人物居中核心左端x，默认960。");
            top=N("DivisionV3.Layout","Top",d.Top,0,1080,"1080高基准的核心顶端，向下为正，默认686。");
            followBody=c.Bind("DivisionV3.Motion","FollowBody",d.FollowBody,"按当前主相机投影跟随人物腰部参考点。关闭可对照固定屏幕锚点；不修改镜头或人物。");
            waistHeight=N("DivisionV3.Motion","WaistHeight",d.WaistHeight,.4f,1.5f,"腰部参考点相对角色根的世界高度，米。不是骨骼或姿态采集。");
            followSeconds=N("DivisionV3.Motion","FollowSeconds",d.FollowSeconds,0,.4f,"核心位置的短平滑；失效/切换/超出安全区时立即重置。0关闭。");
            tilt=N("DivisionV3.Plane","TiltDegrees",d.TiltDegrees,-4,4,"整簇的轻微倾斜，所有核心文字/图标/条共同变换。0关闭倾斜。");
            numberWidth=N("DivisionV3.Plane","NumberWidth",d.NumberWidth,.7f,1,"纯ASCII数值的字宽比例，中文单位保持原字宽。");
            x=N("DivisionV3.Layout","OffsetX",0,-1000,1000,"核心额外横向偏移，向右为正。");
            y=N("DivisionV3.Layout","OffsetY",0,-1000,1000,"核心额外纵向偏移，向下为正。");
            mirror=c.Bind("DivisionV3.Layout","Mirror",true,"右侧人物构图时镜像越肩锚点；居中锚点不镜像。");
            segments=c.Bind("DivisionV3.Layout","HealthSegments",10,new ConfigDescription("生命条分段数，部分段按真实小数填充。",new AcceptableValueRange<int>(1,20)));
            emphasis=c.Bind("DivisionV3.Layout","Emphasis",d.Emphasis,"Ammunition：突出当前弹匣/装备；Health：突出生命。真实读数不随分段取整。");
            slant=N("DivisionV3.Layout","TileSlant",d.TileSlant,0,8,"整簇平面的轻微斜切，像素；与TiltDegrees共同作用，文字/条/图标一致。");
            background=N("DivisionV3.Theme","BackgroundOpacity",.48f,0,1,"小数值板背景透明度，不绘制整个大外框。");
            accent=c.Bind("DivisionV3.Theme","AccentColor","#E08A2E","强调色，#RRGGBB。");
            text=c.Bind("DivisionV3.Theme","TextColor","#ECE8E2","文字/生命条色，#RRGGBB。");
            team=c.Bind("DivisionV3.Theme","TeamColor","#59C8E8","队友色，#RRGGBB。");
            danger=c.Bind("DivisionV3.Theme","DangerColor","#C8452F","危险色，#RRGGBB。");
            label=N("DivisionV3.Text","LabelSize",11,11,18,"标签字号；不沿用线框7–9像素小字。");
            body=N("DivisionV3.Text","BodySize",14,14,22,"正文/普通读数字号。");
            number=N("DivisionV3.Text","NumberSize",38,36,64,"主数值字号，数字过长时按实际字体测量缩小。");
        }
        internal HudLayoutStyle Style
        {
            get
            {
                var s=runtimeStyle??style.Value;
                return Enum.IsDefined(typeof(HudLayoutStyle),s)?s:HudLayoutStyle.DivisionV3;
            }
        }
        HudLayoutStyle? runtimeStyle;
        internal KeyCode CycleStyleKey=>cycleKey.Value;
        internal HudLayoutStyle CycleStyle()
        {
            var order=new[]{HudLayoutStyle.DivisionV3,HudLayoutStyle.FourCorner,HudLayoutStyle.Radial,HudLayoutStyle.Compact};
            int index=Array.IndexOf(order,Style);
            runtimeStyle=order[(index+1)%order.Length];
            return runtimeStyle.Value;
        }
        internal HudPanelOptions Read(HudPanelOptions compact)
        {
            var s=Style;
            if(s is HudLayoutStyle.Compact or HudLayoutStyle.FourCorner or HudLayoutStyle.Radial)return compact with{Layout=compact.Layout with{Style=s}};
            var d=new HudDivisionLayout(width.Value,scale.Value,anchor.Value,shoulderX.Value,centeredX.Value,top.Value,x.Value,y.Value,mirror.Value,segments.Value,emphasis.Value,slant.Value)
                {FollowBody=followBody.Value,WaistHeight=waistHeight.Value,FollowSeconds=compact.Behavior.ReduceMotion?0:followSeconds.Value,TiltDegrees=tilt.Value,NumberWidth=numberWidth.Value};
            var theme=HudPanelTheme.DivisionV3;
            return (compact with{Layout=compact.Layout with{Style=s,Division=d},
                Theme=theme with{Base=Color(team.Value,theme.Base),Text=Color(text.Value,theme.Text),
                    Attention=Color(accent.Value,theme.Attention),Danger=Color(danger.Value,theme.Danger),BackgroundOpacity=background.Value},
                Typography=compact.Typography with{LabelSize=label.Value,BodySize=body.Value,NumberSize=number.Value}}).Sanitized();
        }
        static HudColor Color(string raw,HudColor fallback)
        {
            string hex=raw?.Trim().TrimStart('#');
            return hex?.Length==6&&uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var n)?new((n>>16&255)/255f,(n>>8&255)/255f,(n&255)/255f):fallback;
        }
    }
}
