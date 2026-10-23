using System;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public enum HudDrawKind { Rectangle, Text }
    public readonly record struct HudDrawCommand(HudDrawKind Kind, PixelViewport Rect, HudColor Color, string Text, float FontSize, string Region)
    { public bool Numeric { get; init; } }
    public sealed class HudDrawList
    {
        readonly HudDrawCommand[] commands = new HudDrawCommand[256];
        public int Count { get; private set; }
        public HudDrawCommand this[int index] => index>=0&&index<Count?commands[index]:throw new ArgumentOutOfRangeException(nameof(index));
        public void Clear() { Array.Clear(commands,0,Count);Count=0; }
        public void Add(in HudDrawCommand command)
        {
            var r=command.Rect;
            if(!float.IsFinite(r.X)||!float.IsFinite(r.Y)||!float.IsFinite(r.Width)||!float.IsFinite(r.Height)||r.Width<0||r.Height<0)
                throw new InvalidOperationException("Invalid panel drawing bounds.");
            if(Count==commands.Length) throw new InvalidOperationException("Panel drawing capacity exceeded.");
            commands[Count++]=command;
        }
    }
    // A render model, separate from the collector; personal mapping also carries independent field states.
    public readonly record struct HudPanelView(string Health,string Infection,string Weapon,string Magazine,string Reserve,
        string Tool,string Item,string Secondary,string Supply,float? HealthFraction,float? ToolFraction)
    {
        public string Notice { get; init; }
        public HudPersonalPresentation Personal { get; init; }
        public HudTeamPresentation Team { get; init; }
        public HudScenePresentation Scene { get; init; }
        public HudPromptSet Prompts { get; init; }
        public static HudPanelView Empty => new("—","—","当前装备","—","—","—","—","—","—",null,null);
        public static HudPanelView OfflineExample => new("82%","6%","突击步枪","32","180","46%","医疗包 · 2次","8 / 40","74%",.82f,.46f);
    }
    public static class HudPanelDrawing
    {
        // Used for runtime glyph validation and the offline font check. No borrowed game assets.
        public const string RequiredGlyphs="个人状态探索战斗危险肩瞄详情生命感染当前装备弹匣备用工具携带主副武器补给比例展开按住未读取不可异常已过期支持使用次数物品数量部署回收中近战空手消耗重型搬运摘要倒地死亡资源换蓄力受击左右侧前后方危急过高不足尽包队伍员真人机离线同步位需要救援标记库存交互对象操作键剩余接收自己任务倒计时扫描往发现目标进度秒:m−+—…%0123456789/·<>E";
        public static void Build(in HudPanelPlacement panel,in HudPanelOptions raw,in HudPanelVisual visual,in HudPanelView view,HudDrawList output)
        {
            if(view.Personal.Active){HudPersonalDrawing.Build(panel,raw,visual,view,output);HudSceneDrawing.Build(panel,raw,view.Scene,output);return;}
            output.Clear(); if(!panel.IsValid)return;
            var o=raw.Sanitized();var t=o.Theme;var f=o.Typography;
            float s=panel.Scale,w=panel.Bounds.Width/s,h=panel.Bounds.Height/s;
            float subtle=o.Behavior.ExplorationOpacity+(1-o.Behavior.ExplorationOpacity)*Math.Clamp(visual.Emphasis,0,1);
            var accent=(visual.State==HudVisualState.Danger?t.Danger:visual.State==HudVisualState.Combat?t.Attention:t.Base).Brightness(t.Brightness);
            var text=t.Text.Brightness(t.Brightness).Opacity(t.TextOpacity);
            var dim=t.Base.Brightness(t.Brightness).Opacity(t.TextOpacity*.76f);
            var decoration=accent.Opacity(t.DecorationOpacity*subtle);
            var background=t.Background.Opacity(t.BackgroundOpacity);
            void Box(float x,float y,float width,float height,HudColor color,string region="decoration") => output.Add(new(HudDrawKind.Rectangle,new(x*s,y*s,width*s,height*s),color,"",0,region));
            void Text(float x,float y,float width,float height,string value,float size,HudColor color,string region) => output.Add(new(HudDrawKind.Text,
                new(x*s,y*s,width*s,height*s),color,Ellipsize(value,width,size),size*s,region));
            Box(0,0,w,h,background,"background");
            Box(0,0,2,h,decoration);
            if(t.Decorations)
            {
                float d=t.DepthPixels;
                Box(d,1,w-d,1,decoration.Opacity(.45f));Box(w-1,d,1,h-d,decoration.Opacity(.45f));
                Box(0,h-1,w*.38f,1,decoration);Box(w-28,0,28,2,decoration);Box(0,0,28,2,decoration);
                Box(w-1,h-18,1,18,decoration);Box(w-18,h-1,18,1,decoration);
                // Bounded stepped perspective only on the translucent rim; labels stay square to the screen.
                for(int i=0;i<4;i++) Box(w-2-d+i*d/4,2+i*2,Math.Max(1,d/4),1,decoration.Opacity(.5f));
            }
            if(t.ScanLines&&t.Decorations)for(float y=25;y<h-4;y+=9)Box(4,y,w-8,.5f,decoration.Opacity(.055f));
            Text(14,3,100,20,"个人状态",f.LabelSize,dim,"header");
            if(!string.IsNullOrEmpty(view.Notice))Text(114,3,w-188,20,view.Notice,f.LabelSize,accent.Opacity(t.TextOpacity),"notice");
            string state=visual.State==HudVisualState.Exploration&&panel.Aiming?"肩瞄":visual.Label;
            Text(w-62,3,50,20,state,f.LabelSize,accent.Opacity(t.TextOpacity),"state");
            // Cross icon is a primitive, so it never depends on a symbol font.
            Box(14,33,10,2,text,"health-icon");Box(18,29,2,10,text,"health-icon");
            Text(31,26,76,24,"生命",f.LabelSize,dim,"health");Text(78,24,80,28,view.Health,f.BodySize,text,"health");
            Text(w-118,27,46,24,"感染",f.LabelSize,dim,"infection");Text(w-69,24,58,28,view.Infection,f.BodySize,accent.Opacity(t.TextOpacity),"infection");
            Box(14,53,w-28,3,dim.Opacity(.18f),"health-track");
            if(view.HealthFraction is float hp&&float.IsFinite(hp))Box(14,53,(w-28)*Math.Clamp(hp,0,1),3,accent.Opacity(.9f),"health-fill");
            Text(14,66,w-28,26,view.Weapon,f.BodySize,text,"equipment");
            if(t.Glow)Text(16,87,112,61,view.Magazine,f.NumberSize,accent.Opacity(.10f),"glow");
            Text(14,85,112,64,view.Magazine,f.NumberSize,text,"magazine");
            Box(135,105,1,33,decoration.Opacity(.55f));
            Text(151,106,w-165,36,view.Reserve,f.BodySize+10,text,"reserve");
            Text(17,145,105,20,"弹匣",f.LabelSize,dim,"magazine-label");Text(152,145,w-166,20,"备用",f.LabelSize,dim,"reserve-label");
            Box(14,174,12,2,decoration,"tool-icon");Box(18,170,4,10,decoration,"tool-icon");
            Text(32,166,64,24,"工具",f.LabelSize,dim,"tool");Text(85,165,88,24,view.Tool,f.BodySize,text,"tool");
            Box(w-104,173,90,3,dim.Opacity(.18f),"tool-track");
            if(view.ToolFraction is float tool&&float.IsFinite(tool))Box(w-104,173,90*Math.Clamp(tool,0,1),3,accent.Opacity(.75f),"tool-fill");
            Text(14,191,48,21,"携带",f.LabelSize,dim,"item");Text(65,189,w-79,24,view.Item,f.BodySize,text,"item");
            if(panel.Expanded)
            {
                Box(14,220,w-28,1,decoration.Opacity(.6f));
                Text(14,230,70,23,"副武器",f.LabelSize,dim,"secondary");Text(107,228,w-121,26,view.Secondary,f.BodySize,text,"secondary");
                Text(14,263,86,22,"补给比例",f.LabelSize,dim,"supply");Text(107,260,w-121,27,view.Supply,f.BodySize,text,"supply");
            }
            HudTeamDrawing.Build(panel,o,view.Team,output);
            HudSceneDrawing.Build(panel,o,view.Scene,output);
        }
        public static string Ellipsize(string value,float width,float fontSize)
        {
            if(string.IsNullOrEmpty(value))return "—";
            float budget=Math.Max(0,width-6)/Math.Max(1,fontSize),used=0;
            for(int i=0;i<value.Length;i++)
            {
                used+=value[i]>255?1:.59f;
                if(used>budget)
                {
                    int cut=Math.Max(0,i-1);if(cut>0&&char.IsHighSurrogate(value[cut-1]))cut--;
                    return value.Substring(0,cut)+"…";
                }
            }
            return value;
        }
    }
}
