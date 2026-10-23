using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public static class HudPersonalDrawing
    {
        public static void Build(in HudPanelPlacement panel,in HudPanelOptions raw,in HudPanelVisual visual,in HudPanelView view,HudDrawList output)
        {
            output.Clear();if(!panel.IsValid)return;
            var o=raw.Sanitized();var t=o.Theme;var f=o.Typography;var p=view.Personal;var modules=p.Options;
            float s=panel.Scale,w=panel.Bounds.Width/s,h=panel.Bounds.Height/s;
            var accent=(visual.State==HudVisualState.Danger?t.Danger:visual.State==HudVisualState.Combat?t.Attention:t.Base).Brightness(t.Brightness);
            var text=t.Text.Brightness(t.Brightness).Opacity(t.TextOpacity);
            var dim=t.Base.Brightness(t.Brightness).Opacity(t.TextOpacity*.76f);
            var rim=accent.Opacity(t.DecorationOpacity*(o.Behavior.ExplorationOpacity+(1-o.Behavior.ExplorationOpacity)*visual.Emphasis));
            void Box(float x,float y,float width,float height,HudColor c,string region="decoration")=>output.Add(new(HudDrawKind.Rectangle,new(x*s,y*s,width*s,height*s),c,"",0,region));
            void Text(float x,float y,float width,float height,string value,float size,HudColor c,string region,bool numeric=false)
            {
                value=string.IsNullOrEmpty(value)?"—":value;
                if(numeric)size=FitSize(value,width,height,size);
                output.Add(new(HudDrawKind.Text,new(x*s,y*s,width*s,height*s),c,numeric?value:HudPanelDrawing.Ellipsize(value,width,size),size*s,region){Numeric=numeric});
            }
            void Detail(float y,in HudDetailRow row,bool enabled,string region)
            {
                if(!enabled)return;
                Text(14,y,64,24,row.Label,f.LabelSize,dim,region+"-label");
                Text(81,y,w-215,24,row.Name,f.LabelSize,text,region+"-name");
                Text(w-130,y,116,24,row.Value,f.BodySize,text,region+"-value",true);
            }
            Box(0,0,w,h,t.Background.Opacity(t.BackgroundOpacity),"background");Box(0,0,2,h,rim);
            if(t.Decorations)
            {
                Box(t.DepthPixels,1,w-t.DepthPixels,1,rim.Opacity(.45f));Box(w-1,t.DepthPixels,1,h-t.DepthPixels,rim.Opacity(.45f));
                Box(0,0,28,2,rim);Box(w-28,0,28,2,rim);Box(0,h-1,w*.38f,1,rim);Box(w-18,h-1,18,1,rim);Box(w-1,h-18,1,18,rim);
                for(int i=0;i<4;i++)Box(w-2-t.DepthPixels+i*t.DepthPixels/4,2+i*2,Math.Max(1,t.DepthPixels/4),1,rim.Opacity(.5f));
            }
            if(t.ScanLines&&t.Decorations)for(float y=25;y<h-4;y+=9)Box(4,y,w-8,.5f,rim.Opacity(.055f));
            Text(14,3,100,20,"个人状态",f.LabelSize,dim,"header");
            string notice=!string.IsNullOrEmpty(p.Life)?p.Life:!string.IsNullOrEmpty(p.FocusStatus)?p.FocusStatus
                :modules.SupplyRatio&&!string.IsNullOrEmpty(view.Supply)&&view.Supply!="—"?(modules.FocusSelected&&p.Selected==HudEquipmentSlot.Tool?"工具 ":"补给 ")+view.Supply:"";
            if(!string.IsNullOrEmpty(notice))Text(114,3,w-188,20,notice,f.LabelSize,accent.Opacity(t.TextOpacity),"notice");
            Text(w-62,3,50,20,visual.State==HudVisualState.Exploration&&panel.Aiming?"肩瞄":visual.Label,f.LabelSize,accent.Opacity(t.TextOpacity),"state");
            if(modules.Health)
            {
                Box(14,33,10,2,text,"health-icon");Box(18,29,2,10,text,"health-icon");
                Text(31,25,45,24,"生命",f.LabelSize,dim,"health-label");Text(79,23,92,28,view.Health,f.BodySize,text,"health",true);
                if(!p.Health.Available)Text(14,46,130,14,HudReadoutFormatting.State(p.Health.State),11,dim,"health-status");
                else if(!string.IsNullOrEmpty(p.HealthChange))Text(14,46,130,14,p.HealthChange,11,accent.Opacity(t.TextOpacity*p.ChangeAlpha),"health-change",true);
                Box(14,64,w-28,3,dim.Opacity(.18f),"health-track");
                if(view.HealthFraction is float hp&&float.IsFinite(hp))
                {
                    Box(14,64,(w-28)*Math.Clamp(hp,0,1),3,accent.Opacity(.9f),"health-fill");
                    if(!o.Behavior.ReduceMotion&&p.PreviousHealthFraction is float old)
                    {float a=Math.Min(old,hp),b=Math.Max(old,hp);Box(14+(w-28)*a,69,(w-28)*(b-a),1,(old>hp?t.Danger:t.Base).Opacity(.6f*p.ChangeAlpha),"health-change-fill");}
                }
            }
            if(modules.Infection&&p.ShowInfection)
            {
                Text(w-118,26,48,24,"感染",f.LabelSize,dim,"infection-label");Text(w-69,23,56,28,view.Infection,f.BodySize,accent.Opacity(t.TextOpacity),"infection",true);
                if(!p.Infection.Available)Text(w-118,46,104,14,HudReadoutFormatting.State(p.Infection.State),11,dim,"infection-status");
                else if(!string.IsNullOrEmpty(p.InfectionChange))Text(w-118,46,104,14,p.InfectionChange,11,accent.Opacity(t.TextOpacity*p.ChangeAlpha),"infection-change",true);
            }
            if(p.FocusVisible)
            {
                Text(14,79,66,22,p.FocusLabel,Math.Min(11,f.LabelSize),dim,"focus-label");Text(83,77,w-97,26,view.Weapon,f.BodySize,text,"equipment");
                if(t.Glow)Text(16,102,112,64,view.Magazine,f.NumberSize,accent.Opacity(.1f),"glow",true);
                Text(14,100,112,64,view.Magazine,f.NumberSize,text,"magazine",true);
                if(!string.IsNullOrEmpty(view.Reserve)){Box(135,119,1,33,rim.Opacity(.55f));Text(151,118,w-165,36,view.Reserve,f.BodySize+10,text,"reserve",true);}
                Text(17,166,115,20,p.MainLabel,f.LabelSize,dim,"magazine-label");
                if(!string.IsNullOrEmpty(p.ReserveLabel))Text(152,166,w-166,20,p.ReserveLabel,f.LabelSize,dim,"reserve-label");
            }
            if(modules.Tools)
            {
                Text(14,196,40,24,"工具",f.LabelSize,dim,"tool-label");Text(59,196,w-163,24,p.ToolName,f.LabelSize,text,"tool-name");
                Text(w-97,196,83,24,view.Tool,f.BodySize,text,"tool",true);
                Box(59,220,w-73,2,dim.Opacity(.18f),"tool-track");
                if(view.ToolFraction is float tf&&float.IsFinite(tf))Box(59,220,(w-73)*Math.Clamp(tf,0,1),2,accent.Opacity(.75f),"tool-fill");
            }
            if(modules.Weapons)
            {
                Text(14,225,55,24,p.OtherWeaponLabel,f.LabelSize,dim,"other-weapon-label");
                Text(74,225,w-224,24,p.OtherWeaponName,f.LabelSize,text,"other-weapon-name");
                Text(w-144,225,130,24,p.OtherWeaponValue,f.BodySize,text,"other-weapon",true);
            }
            if(modules.Items){Text(14,253,62,24,p.ItemLabel,f.LabelSize,dim,"item-label");Text(83,251,w-185,26,p.ItemName,f.LabelSize,text,"item-name");Text(w-96,251,82,26,p.ItemValue,f.BodySize,text,"item",true);}
            for(int i=0;i<view.Prompts.Count&&i<3;i++)
            {
                var prompt=view.Prompts[i];float y=283+i*22;
                var color=(prompt.Priority>=85?t.Danger:prompt.Priority>=50?t.Attention:t.Base).Brightness(t.Brightness).Opacity(t.TextOpacity);
                Box(14,y+6,2,10,color,"prompt-marker");
                bool progress=prompt.Progress is float pg&&float.IsFinite(pg)&&pg>=0&&pg<=1;
                Text(23,y,w-(progress?90:37),20,prompt.Text,f.LabelSize,color,"prompt-"+prompt.Kind);
                if(progress)
                {
                    Text(w-66,y,52,20,HudReadoutFormatting.Percent(prompt.Progress.Value,0),f.LabelSize,color,"action-progress",true);
                    Box(23,y+20,w-37,1,dim.Opacity(.25f),"action-progress-track");
                    Box(23,y+20,(w-37)*prompt.Progress.Value,1,color,"action-progress-fill");
                }
            }
            if(panel.Expanded)
            {
                Box(14,360,w-28,1,rim.Opacity(.6f));
                Detail(368,p.Primary,modules.Weapons,"detail-primary");Detail(396,p.Secondary,modules.Weapons,"detail-secondary");
                Detail(424,p.Tool,modules.Tools,"detail-tool");Detail(452,p.Pack,modules.Items,"detail-pack");
                Detail(480,p.Consumable,modules.Items,"detail-consumable");Detail(508,p.Heavy,modules.Items,"detail-heavy");Detail(536,p.Carry,modules.Items,"detail-carry");
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
