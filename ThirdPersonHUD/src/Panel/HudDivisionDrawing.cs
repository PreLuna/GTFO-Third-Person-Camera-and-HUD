using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public static class HudDivisionDrawing
    {
        public static HudOriginalGroup FilterCoverage(HudOriginalGroup groups,in HudPanelView view)
        {
            var p=view.Personal;
            if(!p.FocusVisible||p.FocusSlot is not (HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary))groups&=~HudOriginalGroup.Ammo;
            if(!(p.FocusVisible&&p.FocusSlot==HudEquipmentSlot.Tool)&&!p.Tool.Visible)groups&=~HudOriginalGroup.Tool;
            return groups;
        }
        public static void Build(in HudPanelPlacement panel,in HudPanelOptions raw,in HudPanelVisual visual,in HudPanelView view,HudDrawList output)
        {
            output.Clear();if(!panel.IsValid)return;
            var o=raw.Sanitized();var d=o.Layout.Division;var t=o.Theme;var f=o.Typography;var p=view.Personal;
            float s=panel.Scale,w=panel.Bounds.Width/s,k=w/252f;
            var ink=t.Text.Opacity(t.TextOpacity);var dim=ink.Opacity(.70f);
            var accent=visual.State==HudVisualState.Danger?t.Danger:t.Attention;
            var back=t.Background.Opacity(t.BackgroundOpacity*.6f);
            void Box(float x,float y,float width,float height,HudColor color,string region)=>output.Add(new(HudDrawKind.Rectangle,new(x*s,y*s,width*s,height*s),color,"",0,region){CorePlane=true});
            void Text(float x,float y,float width,float height,string text,float size,HudColor color,string region,bool numeric=false)
            {
                if(string.IsNullOrEmpty(text))return;
                bool ascii=true;foreach(char ch in text)if(ch>127){ascii=false;break;}
                output.Add(new(HudDrawKind.Text,new(x*s,y*s,width*s,height*s),color,numeric?text:HudPanelDrawing.Ellipsize(text,width,size),size*s,region)
                    {Numeric=numeric,CorePlane=true,NumberWidth=numeric&&ascii?d.NumberWidth:1});
            }
            void Icon(HudEquipmentSlot slot,float x,float y,float size,HudColor color)
            {
                // Original, category-level silhouettes; never infer the exact item model from its name.
                float u=size/20;
                void R(float px,float py,float ww,float hh)=>Box(x+px*u,y+py*u,ww*u,hh*u,color,"v3-icon-"+slot);
                switch(slot)
                {
                    case HudEquipmentSlot.Primary:case HudEquipmentSlot.Secondary:R(1,6,16,4);R(16,7,4,2);R(4,10,3,7);R(10,10,3,4);R(1,10,3,3);break;
                    case HudEquipmentSlot.Melee:R(3,2,14,6);R(9,8,3,12);break;
                    case HudEquipmentSlot.Tool:R(2,3,15,5);R(8,8,3,7);R(4,15,12,2);R(2,17,3,3);R(15,17,3,3);break;
                    case HudEquipmentSlot.ResourcePack:R(3,4,14,13);R(6,1,8,2);R(5,7,10,2);R(5,11,10,2);break;
                    case HudEquipmentSlot.Consumable:R(7,1,6,3);R(5,5,10,14);R(7,8,6,2);break;
                    case HudEquipmentSlot.Heavy:R(2,4,16,14);R(5,1,10,2);R(5,7,2,8);R(13,7,2,8);break;
                    case HudEquipmentSlot.Carry:R(2,3,16,2);R(2,5,2,13);R(16,5,2,13);R(4,16,12,2);R(9,5,2,11);break;
                    default:R(4,8,12,2);R(9,3,2,12);break;
                }
            }
            if(p.Options.Enabled&&p.Options.Health)
            {
                float bar=w-56,gap=2,segment=(bar-gap*(d.HealthSegments-1))/d.HealthSegments;
                for(int i=0;i<d.HealthSegments;i++)
                {
                    float x=i*(segment+gap);Box(x,0,segment,11,ink.Opacity(.18f),"v3-health-track");
                    if(view.HealthFraction is float hp&&float.IsFinite(hp))
                    {float fill=Math.Clamp(hp*d.HealthSegments-i,0,1);if(fill>0)Box(x,0,segment*fill,11,visual.State==HudVisualState.Danger?t.Danger:ink,"v3-health-fill");}
                }
                Text(w-50,0,50,18,view.Health,d.Emphasis==HudDivisionEmphasis.Health?18:f.LabelSize,ink,"v3-health-value",true);
                if(p.PreviousHealthFraction is float old&&view.HealthFraction is float current&&old>current&&p.ChangeAlpha>0)
                {float from=Math.Clamp(current,0,1)*bar,to=Math.Clamp(old,0,1)*bar;Box(from,13,Math.Max(0,to-from),1,t.Danger.Opacity(p.ChangeAlpha),"v3-health-loss");}
            }
            if(p.Options.Enabled&&p.Options.Infection&&p.ShowInfection&&(!p.Infection.Available||p.Infection.Amount>0||!string.IsNullOrEmpty(p.InfectionChange)))
            {
                Box(0,17,w-86,2,accent.Opacity(.16f),"v3-infection-track");
                if(p.Infection.Available)Box(0,17,(w-86)*Math.Clamp((float)p.Infection.Amount.Value,0,1),2,accent,"v3-infection-fill");
                Text(w-80,14,80,18,"感染 "+view.Infection,f.LabelSize,dim,"v3-infection-value");
            }
            bool firearm=p.FocusVisible&&p.FocusSlot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary;
            bool quantity=p.FocusVisible&&(firearm||p.FocusSlot is HudEquipmentSlot.Tool or HudEquipmentSlot.ResourcePack or HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy);
            if(p.FocusVisible)
            {
                Box(0,30,w,quantity?82:60,back,"v3-core-material");
                if(quantity)
                {
                    Box(0,34,2,44,accent,"v3-focus-rim");
                    Text(8,26,76*k,51,view.Magazine,d.Emphasis==HudDivisionEmphasis.Ammunition?f.NumberSize:26,ink,"v3-focus-value",true);
                    if(firearm)
                    {
                        Box(8,80,2,9,dim,"v3-ammo-icon");Box(13,80,2,9,dim,"v3-ammo-icon");
                        Text(21,73,64*k,22,view.Reserve,17,dim,"v3-reserve-value",true);
                        Box(87*k,34,1,54,ink.Opacity(.13f),"v3-column-separator");
                        var other=p.FocusSlot==HudEquipmentSlot.Secondary?p.Primary:p.Secondary;
                        if(other.Visible)
                        {
                            Icon(p.FocusSlot==HudEquipmentSlot.Secondary?HudEquipmentSlot.Primary:HudEquipmentSlot.Secondary,96*k,34,19,dim);
                            Text(119*k,32,32*k,19,p.FocusSlot==HudEquipmentSlot.Secondary?"主":"副",f.LabelSize,dim,"v3-other-label");
                            // Split only the mapper's known display delimiter; no numeric conversion or invented count.
                            int slash=other.Value?.IndexOf(" / ",StringComparison.Ordinal)??-1;
                            if(slash>=0)
                            {Text(96*k,51,55*k,25,other.Value.Substring(0,slash),21,ink,"v3-other-ammo",true);Text(96*k,76,55*k,18,other.Value.Substring(slash+3),f.LabelSize,dim,"v3-other-reserve",true);}
                            else Text(94*k,55,57*k,35,other.Value,f.LabelSize,dim,"v3-other-ammo",true);
                        }
                    }
                    else Icon(p.FocusSlot,98*k,39,26,accent);
                    Text(4,94,w-8,20,view.Weapon+(string.IsNullOrEmpty(p.FocusStatus)?"":" · "+p.FocusStatus),f.LabelSize,dim,"v3-equipment-name");
                }
                else
                {
                    Icon(p.FocusSlot,8,37,25,accent);
                    Text(43,33,112*k,26,p.FocusSlot==HudEquipmentSlot.Carry?"持有中":view.Magazine,f.BodySize,ink,"v3-focus-value");
                    Text(4,67,w-8,22,view.Weapon+(string.IsNullOrEmpty(p.FocusStatus)?"":" · "+p.FocusStatus),f.BodySize,dim,"v3-equipment-name");
                }
            }
            if(p.Options.Enabled&&p.Options.Tools&&p.Tool.Visible&&p.FocusSlot!=HudEquipmentSlot.Tool)
            {
                if(firearm)Box(157*k,34,1,54,ink.Opacity(.13f),"v3-column-separator");
                Icon(HudEquipmentSlot.Tool,170*k,34,18,accent);
                Text(193*k,32,56*k,20,"工具",f.LabelSize,dim,"v3-tool-label");
                Text(170*k,53,78*k,28,view.Tool,21,ink,"v3-tool-value",true);
                if(quantity)Text(170*k,79,78*k,18,p.ToolName,f.LabelSize,dim,"v3-tool-name");
            }
            int item=0;float itemY=quantity?116:94;
            void Item(HudDetailRow row,HudEquipmentSlot slot)
            {
                if(!p.Options.Enabled||!p.Options.Items||!row.Visible||p.FocusVisible&&p.FocusSlot==slot)return;
                float cell=(w-12)/4,x=item++*(cell+4);
                Box(x,itemY,cell,37,back,"v3-item-tile");
                Icon(slot,x+3,itemY+2,16,accent);
                // Names remain below the glyphs, so generic category icons never pretend to identify a specific item.
                Text(x+2,itemY+20,cell-4,17,row.Name,f.LabelSize,dim,"v3-item-name");
                if(slot!=HudEquipmentSlot.Carry&&!string.IsNullOrEmpty(row.Value))Text(x+22,itemY,cell-24,21,row.Value,f.LabelSize,ink,"v3-item-value",true);
            }
            Item(p.Pack,HudEquipmentSlot.ResourcePack);Item(p.Consumable,HudEquipmentSlot.Consumable);Item(p.Heavy,HudEquipmentSlot.Heavy);Item(p.Carry,HudEquipmentSlot.Carry);
            BuildPeripheral(panel,o,view,output);
        }
        static void BuildPeripheral(HudPanelPlacement panel,HudPanelOptions o,HudPanelView view,HudDrawList output)
        {
            var v=panel.Viewport;if(v.Width<=0||v.Height<=0)return;
            var t=o.Theme;var f=o.Typography;var scene=view.Scene;
            float s=Math.Clamp(v.Height/1080f,.75f,2),top=panel.Bounds.Y+panel.Bounds.Height;
            var ink=t.Text.Opacity(t.TextOpacity);var accent=t.Attention.Opacity(t.TextOpacity);var team=t.Base.Opacity(t.TextOpacity);
            PixelViewport At(float x,float y,float width,float height)=>new(v.X+x*s,v.Y+v.Height-(y+height)*s,width*s,height*s);
            void Box(PixelViewport r,HudColor color,string region)=>output.Add(new(HudDrawKind.Rectangle,new(r.X-panel.Bounds.X,top-r.Y-r.Height,r.Width,r.Height),color,"",0,region));
            void Text(PixelViewport r,string text,float size,HudColor color,string region,bool numeric=false)
            {if(!string.IsNullOrEmpty(text))output.Add(new(HudDrawKind.Text,new(r.X-panel.Bounds.X,top-r.Y-r.Height,r.Width,r.Height),color,numeric?text:HudPanelDrawing.Ellipsize(text,r.Width,size*s),size*s,region){Numeric=numeric});}
            bool Fits(PixelViewport r)=>r.X>=v.X&&r.Y>=v.Y&&r.X+r.Width<=v.X+v.Width&&r.Y+r.Height<=v.Y+v.Height&&!HudSceneDrawing.Overlap(r,panel.Bounds);
            var mission=At(48,40,340,66);
            var scan=At((v.Width/s-300)/2,42,300,36);
            var countdown=At((v.Width/s-300)/2,86,300,26);
            if(scene.Active&&scene.Mission.Active&&Fits(mission))
            {
                Text(At(48,40,340,22),scene.Mission.Title,f.BodySize,accent,"v3-mission-title");
                Text(At(48,64,340,42),scene.Mission.Text,f.BodySize,ink,"v3-mission-text");
            }
            if(scene.Active&&scene.Scan.Active&&Fits(scan))
            {
                float x=(v.Width/s-300)/2;
                Text(At(x,42,248,20),scene.Scan.Title,f.LabelSize,ink,"v3-scan-title");
                if(scene.Scan.Progress is float pg)
                {
                    Text(At(x+250,42,50,20),HudReadoutFormatting.Percent(pg,0),f.LabelSize,accent,"v3-scan-value",true);
                    Box(At(x,66,300,3),ink.Opacity(.2f),"v3-scan-track");Box(At(x,66,300*Math.Clamp(pg,0,1),3),accent,"v3-scan-progress");
                }
            }
            if(scene.Active&&scene.Countdown.Active&&Fits(countdown))
                Text(countdown,scene.Countdown.Title+"  "+scene.Countdown.Text,f.BodySize,accent,"v3-countdown");
            PixelViewport interaction=default;
            if(scene.Active&&scene.Interaction.Active&&HudSceneDrawing.Layout(panel,v,scene.Options,out interaction,out _))
            {
                var card=scene.Interaction;float x=interaction.X,y=interaction.Y;
                Box(interaction,t.Background.Opacity(t.BackgroundOpacity),"v3-interaction-background");
                Text(new(x+8*s,y+70*s,interaction.Width-16*s,22*s),card.Title,f.LabelSize,accent,"v3-interaction-title");
                Text(new(x+8*s,y+45*s,interaction.Width-16*s,24*s),card.Text,f.BodySize,ink,"v3-interaction-text");
                Text(new(x+8*s,y+22*s,interaction.Width-16*s,22*s),card.Secondary,f.LabelSize,ink,"v3-interaction-key");
                if(card.Progress is float pg){Box(new(x+8*s,y+10*s,interaction.Width-16*s,2*s),ink.Opacity(.2f),"v3-interaction-track");Box(new(x+8*s,y+10*s,(interaction.Width-16*s)*Math.Clamp(pg,0,1),2*s),accent,"v3-interaction-progress");}
            }
            // Current validated events, bounded to three. Carrying never produces a progress bar here.
            var notifications=At(48,v.Height/s-180,340,80);
            if(Fits(notifications))for(int i=0;i<view.Prompts.Count&&i<3;i++)
                Text(At(48,v.Height/s-180+i*25,340,24),view.Prompts[i].Text,f.BodySize,view.Prompts[i].Priority>=80?t.Danger:accent,"v3-notification");
            var teamArea=At(v.Width/s-268,v.Height/s-218,220,174);
            if(o.Layout.Team&&view.Team.Active&&Fits(teamArea))
            {
                int visible=0;
                for(int i=0;i<3;i++)
                {
                    var r=view.Team[i];if(r.Name=="空位"||string.IsNullOrEmpty(r.Kind)||!r.HealthFraction.HasValue&&string.IsNullOrEmpty(r.Status))continue;
                    float x=v.Width/s-268,y=v.Height/s-218+visible++*54;
                    Text(At(x,y,148,21),r.Name,f.BodySize,team,"v3-team-name");Text(At(x+152,y,68,21),r.Health,f.BodySize,ink,"v3-team-health",true);
                    Box(At(x,y+24,220,2),team.Opacity(.22f),"v3-team-track");
                    if(r.HealthFraction is float hp)Box(At(x,y+24,220*Math.Clamp(hp,0,1),2),team,"v3-team-fill");
                    string status=string.IsNullOrEmpty(r.Cue)?r.Status:r.Cue;
                    Text(At(x,y+29,220,19),status,f.LabelSize,r.Priority>=80?t.Danger:team,"v3-team-status");
                }
            }
            PixelViewport previous=default,before=default;
            for(int i=0;scene.Active&&i<scene.Count&&i<3;i++)
            {
                var r=scene[i];var b=r.Bounds;
                if(!Fits(b)||scene.Mission.Active&&HudSceneDrawing.Overlap(b,mission)||scene.Scan.Active&&HudSceneDrawing.Overlap(b,scan)
                    ||scene.Countdown.Active&&HudSceneDrawing.Overlap(b,countdown)||scene.Interaction.Active&&HudSceneDrawing.Overlap(b,interaction)
                    ||o.Layout.Team&&HudSceneDrawing.Overlap(b,teamArea)||view.Prompts.Count>0&&HudSceneDrawing.Overlap(b,notifications)
                    ||i>0&&HudSceneDrawing.Overlap(b,previous)||i>1&&HudSceneDrawing.Overlap(b,before))continue;
                Text(b,r.Text,f.LabelSize,r.Kind==HudSceneMarkerKind.Rescue?t.Danger:team,"v3-marker");before=previous;previous=b;
            }
        }
    }
}
