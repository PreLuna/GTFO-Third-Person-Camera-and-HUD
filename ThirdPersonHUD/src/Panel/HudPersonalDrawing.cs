using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public static class HudPersonalDrawing
    {
        static HudDetailRow Row(in HudPersonalPresentation p,int i)=>i switch
        {0=>p.Primary,1=>p.Secondary,2=>p.Tool,3=>p.Pack,4=>p.Consumable,5=>p.Heavy,6=>p.Carry,_=>default};
        static bool Show(in HudPersonalPresentation p,int i)
        {
            var row=Row(p,i);var o=p.Options;
            var slot=i switch{0=>HudEquipmentSlot.Primary,1=>HudEquipmentSlot.Secondary,2=>HudEquipmentSlot.Tool,3=>HudEquipmentSlot.ResourcePack,4=>HudEquipmentSlot.Consumable,5=>HudEquipmentSlot.Heavy,_=>HudEquipmentSlot.Carry};
            return row.Visible&&(i<2?o.Weapons:i==2?o.Tools:o.Items)&&(!p.FocusVisible||p.FocusSlot!=slot);
        }
        public static float MeasureHeight(in HudPanelView view)
        {
            var p=view.Personal;float h=28;
            if(p.Options.Health||p.Options.Infection&&p.ShowInfection)h+=54;
            if(p.FocusVisible)h+=110;
            for(int i=0;i<7;i++)if(Show(p,i))h+=26;
            return h+Math.Min(3,view.Prompts.Count)*22+10;
        }
        public static void Build(in HudPanelPlacement panel,in HudPanelOptions raw,in HudPanelVisual visual,in HudPanelView view,HudDrawList output)
        {
            output.Clear();if(!panel.IsValid)return;
            var o=raw.Sanitized();var t=o.Theme;var f=o.Typography;var p=view.Personal;
            float s=panel.Scale,w=panel.Bounds.Width/s,h=Math.Min(panel.Bounds.Height/s,MeasureHeight(view));
            var accent=(visual.State==HudVisualState.Danger?t.Danger:visual.State==HudVisualState.Combat?t.Attention:t.Base).Brightness(t.Brightness);
            var text=t.Text.Brightness(t.Brightness).Opacity(t.TextOpacity);
            var dim=t.Base.Brightness(t.Brightness).Opacity(t.TextOpacity*.76f);
            var rim=accent.Opacity(t.DecorationOpacity);
            void Box(float x,float y,float width,float height,HudColor c,string region="decoration")=>output.Add(new(HudDrawKind.Rectangle,new(x*s,y*s,width*s,height*s),c,"",0,region));
            void Text(float x,float y,float width,float height,string value,float size,HudColor c,string region,bool numeric=false)
            {
                if(string.IsNullOrEmpty(value))return;
                if(numeric)size=FitSize(value,width,height,size);
                output.Add(new(HudDrawKind.Text,new(x*s,y*s,width*s,height*s),c,numeric?value:HudPanelDrawing.Ellipsize(value,width,size),size*s,region){Numeric=numeric});
            }
            Box(0,0,w,h,t.Background.Opacity(t.BackgroundOpacity),"background");Box(0,0,2,h,rim);
            if(t.Decorations){Box(0,0,28,2,rim);Box(w-28,0,28,2,rim);Box(0,h-1,w*.38f,1,rim);Box(w-1,0,1,h,rim.Opacity(.45f));}
            Text(14,3,92,20,"个人状态",f.LabelSize,dim,"header");
            string notice=p.Life;
            if(!p.Health.Available&&!p.Infection.Available&&!p.FocusVisible)notice="等待数据";
            Text(111,3,w-185,20,notice,f.LabelSize,accent,"notice");
            Text(w-62,3,50,20,visual.State==HudVisualState.Exploration&&panel.Aiming?"肩瞄":visual.Label,f.LabelSize,accent,"state");
            float y=28;
            if(p.Options.Health||p.Options.Infection&&p.ShowInfection)
            {
                if(p.Options.Health)
                {
                    Text(14,y,48,24,"生命",f.LabelSize,dim,"health-label");Text(65,y,90,28,view.Health,f.BodySize,text,"health",true);
                    Box(14,y+34,w-28,3,dim.Opacity(.18f),"health-track");
                    if(view.HealthFraction is float hp&&float.IsFinite(hp))Box(14,y+34,(w-28)*Math.Clamp(hp,0,1),3,accent,"health-fill");
                    if(view.HealthFraction is float current&&p.PreviousHealthFraction is float previous&&!o.Behavior.ReduceMotion)
                    {float a=Math.Min(current,previous),b=Math.Max(current,previous);Box(14+(w-28)*a,y+41,(w-28)*(b-a),1,(previous>current?t.Danger:t.Base).Opacity(p.ChangeAlpha),"health-change-fill");}
                    if(!string.IsNullOrEmpty(p.HealthChange))Text(14,y+38,120,14,p.HealthChange,11,accent.Opacity(p.ChangeAlpha),"health-change",true);
                }
                if(p.Options.Infection&&p.ShowInfection)
                {Text(w-118,y,48,24,"感染",f.LabelSize,dim,"infection-label");Text(w-69,y,56,28,view.Infection,f.BodySize,text,"infection",true);if(!string.IsNullOrEmpty(p.InfectionChange))Text(w-118,y+38,104,14,p.InfectionChange,11,accent.Opacity(p.ChangeAlpha),"infection-change",true);}
                y+=54;
            }
            if(p.FocusVisible)
            {
                Text(14,y,w-28,18,p.FocusLabel,11,dim,"focus-label");
                Text(14,y+18,w-28,24,view.Weapon,f.BodySize,text,"equipment");
                Text(14,y+42,112,44,view.Magazine,f.NumberSize,text,"magazine",true);
                if(!string.IsNullOrEmpty(view.Reserve))Text(151,y+48,w-165,36,view.Reserve,f.BodySize+10,text,"reserve",true);
                Text(14,y+86,120,20,p.MainLabel,f.LabelSize,dim,"magazine-label");
                Text(151,y+86,w-165,20,p.ReserveLabel,f.LabelSize,dim,"reserve-label");
                y+=110;
            }
            for(int i=0;i<7;i++)
            {
                if(!Show(p,i))continue;
                var row=Row(p,i);string region=i switch{0=>"detail-primary",1=>"detail-secondary",2=>"detail-tool",3=>"detail-pack",4=>"detail-consumable",5=>"detail-heavy",_=>"detail-carry"};
                bool value=!string.IsNullOrEmpty(row.Value);
                Text(14,y,80,24,row.Label,f.LabelSize,dim,region+"-label");
                Text(96,y,value?w-200:w-110,24,row.Name,f.LabelSize,text,region+"-name");
                if(value)Text(w-102,y,88,24,row.Value,f.BodySize,text,region+"-value",true);
                y+=26;
            }
            for(int i=0;i<view.Prompts.Count&&i<3;i++)
            {
                var prompt=view.Prompts[i];var color=(prompt.Priority>=85?t.Danger:prompt.Priority>=50?t.Attention:t.Base).Opacity(t.TextOpacity);
                bool progress=prompt.Progress is float pg&&float.IsFinite(pg)&&pg>=0&&pg<=1;
                Text(14,y,w-(progress?80:28),20,prompt.Text,f.LabelSize,color,"prompt-"+prompt.Kind);
                if(progress){Text(w-66,y,52,20,HudReadoutFormatting.Percent(prompt.Progress.Value,0),f.LabelSize,color,"action-progress",true);Box(14,y+20,(w-28)*prompt.Progress.Value,1,color,"action-progress-fill");}
                y+=22;
            }
            HudTeamDrawing.Build(panel,o,view.Team,output);
        }
        public static float FitSize(string text,float width,float height,float requested)
        {
            float units=0;foreach(char c in text)units+=c>255?1:.66f;
            return Math.Max(8,Math.Min(requested,Math.Min((width-6)/Math.Max(.66f,units),height*.85f)));
        }
    }
}
