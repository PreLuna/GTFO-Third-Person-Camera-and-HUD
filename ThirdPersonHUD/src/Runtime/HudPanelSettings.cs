using System;
using System.Globalization;
using System.Numerics;
using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudPanelSettings
    {
        readonly ConfigEntry<bool> enabled,mirror,decorations,glow,scanLines,shadow,reduce,toggle;
        readonly ConfigEntry<float> width,scale,height,side,fixedSide,rightX,rightY,leftX,leftY,margin,safe,aimScale,aimX,aimY,follow;
        readonly ConfigEntry<float> background,decoration,text,brightness,depth,labelSize,bodySize,numberSize,combat,lowHealth,highInfection,exploration,transition;
        readonly ConfigEntry<string> font,baseColor,textColor,attentionColor,dangerColor,backgroundColor;
        readonly ConfigEntry<KeyCode> expandKey;
        internal HudPanelSettings(ConfigFile c)
        {
            var d=HudPanelOptions.Default;
            ConfigEntry<float> Number(string group,string key,float value,float min,float max,string explanation) => c.Bind(group,key,value,new ConfigDescription(explanation,new AcceptableValueRange<float>(min,max)));
            enabled=c.Bind("Panel","Enabled",true,"启用角色旁全息面板；关闭后隐藏并释放自有资源。只在对局内有效第三人称显示。");
            width=Number("Panel.Layout","Width",d.Layout.Width,260,420,"1080p 基准宽度；高度随摘要或详情模式确定。");
            scale=Number("Panel.Layout","Scale",d.Layout.Scale,.65f,1.6f,"整体缩放；随主相机视口高度调整，并保留最小可读尺寸。");
            height=Number("Panel.Layout","AnchorHeight",d.Layout.AnchorHeight,0,3,"锚点相对本机角色根位置的高度，米。");
            side=Number("Panel.Layout","AnchorSide",d.Layout.AnchorSide,0,2,"沿当次相机右向的侧偏绝对值，米。");
            mirror=c.Bind("Panel.Layout","MirrorWithShoulder",d.Layout.MirrorWithShoulder,"从当次相机与身体相对位置确定左右肩；不改变持枪手。");
            fixedSide=Number("Panel.Layout","FixedSide",d.Layout.FixedSide,-1,1,"关闭跟随肩位时：负数固定左侧，其余固定右侧。");
            rightX=Number("Panel.Layout","RightOffsetX",d.Layout.RightOffset.X,-1000,1000,"右肩额外屏幕横向偏移，基准像素。");
            rightY=Number("Panel.Layout","RightOffsetY",d.Layout.RightOffset.Y,-1000,1000,"右肩额外屏幕纵向偏移，正数向上。");
            leftX=Number("Panel.Layout","LeftOffsetX",d.Layout.LeftOffset.X,-1000,1000,"左肩额外屏幕横向偏移，基准像素。");
            leftY=Number("Panel.Layout","LeftOffsetY",d.Layout.LeftOffset.Y,-1000,1000,"左肩额外屏幕纵向偏移，正数向上。");
            margin=Number("Panel.Layout","ScreenMargin",d.Layout.ScreenMargin,0,100,"相机实际视口边距，像素。");
            safe=Number("Panel.Layout","CrosshairRadius",d.Layout.CrosshairRadius,0,200,"准星周围保留的方形安全区半径，基准像素；0 关闭。");
            aimScale=Number("Panel.Layout","AimScale",d.Layout.AimScale,.7f,1.2f,"肩瞄时缩放倍率。");
            aimX=Number("Panel.Layout","AimOffsetX",d.Layout.AimOffset.X,-1000,1000,"肩瞄时沿肩位方向增加的屏幕偏移。");
            aimY=Number("Panel.Layout","AimOffsetY",d.Layout.AimOffset.Y,-1000,1000,"肩瞄时纵向偏移，正数向上。");
            follow=Number("Panel.Motion","FollowSeconds",d.Layout.FollowSeconds,0,1,"左右肩位偏移的平滑时间；0 立即调整。每次仍使用当前相机投影。");
            transition=Number("Panel.Motion","TransitionSeconds",d.Behavior.TransitionSeconds,0,1,"探索与强调装饰的过渡时间；不延迟失去资格后的隐藏。");
            reduce=c.Bind("Panel.Motion","ReduceMotion",d.Behavior.ReduceMotion,"立即切换强调强度并关闭肩位平滑。");
            background=Number("Panel.Theme","BackgroundOpacity",d.Theme.BackgroundOpacity,0,1,"背景透明度，与文字及装饰分开控制。");
            decoration=Number("Panel.Theme","DecorationOpacity",d.Theme.DecorationOpacity,0,1,"装饰透明度。");
            text=Number("Panel.Theme","TextOpacity",d.Theme.TextOpacity,0,1,"文字透明度。");
            brightness=Number("Panel.Theme","Brightness",d.Theme.Brightness,.2f,2,"文字与装饰亮度。");
            depth=Number("Panel.Theme","DepthPixels",d.Theme.DepthPixels,0,16,"边框分层与轻微透视宽度；文字保持正向。");
            decorations=c.Bind("Panel.Theme","Decorations",d.Theme.Decorations,"启用装饰边框。");
            glow=c.Bind("Panel.Theme","Glow",d.Theme.Glow,"数字使用低透明度偏移光层；不启用全屏后期处理。");
            scanLines=c.Bind("Panel.Theme","ScanLines",d.Theme.ScanLines,"可选稀疏扫描线，默认关闭。");
            baseColor=c.Bind("Panel.Theme","BaseColor",Hex(d.Theme.Base),"基础色，#RRGGBB。");
            textColor=c.Bind("Panel.Theme","TextColor",Hex(d.Theme.Text),"文字色，#RRGGBB。");
            attentionColor=c.Bind("Panel.Theme","AttentionColor",Hex(d.Theme.Attention),"战斗强调色，#RRGGBB。");
            dangerColor=c.Bind("Panel.Theme","DangerColor",Hex(d.Theme.Danger),"危险色，#RRGGBB；同时显示文字状态。");
            backgroundColor=c.Bind("Panel.Theme","BackgroundColor",Hex(d.Theme.Background),"背景色，#RRGGBB。");
            font=c.Bind("Panel.Text","FontName",d.Typography.FontName,"系统动态字体名称；缺少中文标签时尝试微软雅黑/黑体，不打包系统字体。");
            labelSize=Number("Panel.Text","LabelSize",d.Typography.LabelSize,11,18,"标签基准字号。");
            bodySize=Number("Panel.Text","BodySize",d.Typography.BodySize,14,22,"普通读数基准字号。");
            numberSize=Number("Panel.Text","NumberSize",d.Typography.NumberSize,36,64,"当前弹匣基准字号。");
            shadow=c.Bind("Panel.Text","Shadow",d.Typography.Shadow,"文字使用轻微暗影，改善亮背景对比。");
            combat=Number("Panel.State","CombatHoldSeconds",d.Behavior.CombatHoldSeconds,0,10,"同一装备弹药变化或受伤后保持强调的秒数，不推断敌人位置。");
            lowHealth=Number("Panel.State","LowHealth",d.Behavior.LowHealth,0,1,"低生命视觉强调及生命危急提示阈值。");
            highInfection=Number("Panel.State","HighInfection",d.Behavior.HighInfection,0,1,"高感染视觉强调阈值。");
            exploration=Number("Panel.State","ExplorationOpacity",d.Behavior.ExplorationOpacity,.3f,1,"探索时装饰强度；关键文字不随此值减弱。");
            expandKey=c.Bind("Panel.Input","ExpandKey",KeyCode.LeftAlt,"按住展开详情；只读取按键，不拦截游戏输入，可改绑。");
            toggle=c.Bind("Panel.Input","ToggleExpand",false,"false 按住展开，true 按一次切换；失去资格后重置。");
        }
        internal KeyCode ExpandKey=>expandKey.Value;
        internal bool ToggleExpand=>toggle.Value;
        internal HudPanelOptions Read()
        {
            var d=HudPanelOptions.Default;
            return new HudPanelOptions(d.Layout with {Enabled=enabled.Value,Width=width.Value,Scale=scale.Value,AnchorHeight=height.Value,AnchorSide=side.Value,
                MirrorWithShoulder=mirror.Value,FixedSide=fixedSide.Value,RightOffset=new(rightX.Value,rightY.Value),LeftOffset=new(leftX.Value,leftY.Value),
                ScreenMargin=margin.Value,CrosshairRadius=safe.Value,AimScale=aimScale.Value,AimOffset=new(aimX.Value,aimY.Value),FollowSeconds=reduce.Value?0:follow.Value},
                d.Theme with {Base=Color(baseColor.Value,d.Theme.Base),Text=Color(textColor.Value,d.Theme.Text),Attention=Color(attentionColor.Value,d.Theme.Attention),
                    Danger=Color(dangerColor.Value,d.Theme.Danger),Background=Color(backgroundColor.Value,d.Theme.Background),BackgroundOpacity=background.Value,
                    DecorationOpacity=decoration.Value,TextOpacity=text.Value,Brightness=brightness.Value,DepthPixels=depth.Value,Decorations=decorations.Value,Glow=glow.Value,ScanLines=scanLines.Value},
                new(font.Value,labelSize.Value,bodySize.Value,numberSize.Value,shadow.Value),
                new(combat.Value,lowHealth.Value,highInfection.Value,exploration.Value,transition.Value,reduce.Value)).Sanitized();
        }
        static HudColor Color(string raw,HudColor fallback)
        {
            string hex=raw?.Trim().TrimStart('#');
            return hex?.Length==6&&uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var value)
                ?new((value>>16&255)/255f,(value>>8&255)/255f,(value&255)/255f):fallback;
        }
        static string Hex(HudColor color)=>"#"+((int)MathF.Round(color.R*255)).ToString("X2",CultureInfo.InvariantCulture)
            +((int)MathF.Round(color.G*255)).ToString("X2",CultureInfo.InvariantCulture)+((int)MathF.Round(color.B*255)).ToString("X2",CultureInfo.InvariantCulture);
    }
}
