using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudSceneCard(string Title,string Text,string Secondary,float? Progress)
    { public bool Active=>!string.IsNullOrEmpty(Title); }
    public readonly record struct HudSceneMarkerView(PixelViewport Bounds,string Text,HudSceneMarkerKind Kind,int Priority);
    public readonly record struct HudScenePresentation(bool Active,PixelViewport Viewport,HudSceneOptions Options,
        HudSceneCard Interaction,HudSceneCard Objective,HudSceneMarkerView First,HudSceneMarkerView Second,HudSceneMarkerView Third,int Count)
    {public HudSceneMarkerView this[int i]=>i switch{0=>First,1=>Second,2=>Third,_=>default};}
    public static class HudSceneMapper
    {
        readonly struct Reader
        {
            readonly HudRuntimeContext c;readonly HudContentSnapshot s;readonly long epoch,revision;readonly HudContentPolicy policy;
            public Reader(HudRuntimeContext c,HudContentSnapshot s,long e,long r,HudContentPolicy p){this.c=c;this.s=s;epoch=e;revision=r;policy=p;}
            public bool Subject(HudValue<HudSceneSubject> f,out HudSceneSubject value)=>HudSceneRules.Subject(c,s.Header,f,epoch,revision,policy,out value);
            public bool Read<T>(HudValue<T> f,HudSceneSubject subject,out T value)where T:struct,IHudContentValue=>HudSceneRules.Read(c,s.Header,f,subject,epoch,revision,policy,out value);
            public string Text(HudValue<HudText> f,HudSceneSubject subject,string missing="未读取")=>Read(f,subject,out var t)?Clean(t.Text):missing;
            public float? Progress(HudValue<HudQuantity> f,HudSceneSubject subject)=>Read(f,subject,out var q)&&q.Unit==HudUnit.Fraction&&q.Amount<=1?(float)q.Amount:null;
        }
        public static string Clean(string text,int maximum=160)
        {
            if(string.IsNullOrEmpty(text))return "";var b=new StringBuilder(Math.Min(maximum,text.Length));bool tag=false;
            foreach(char ch in text){if(ch=='<'){tag=true;continue;}if(ch=='>'){tag=false;continue;}if(tag)continue;if(b.Length>=maximum)break;
                if(char.IsControl(ch)||ch=='\u2028'||ch=='\u2029'){if(b.Length>0&&b[b.Length-1]!=' ')b.Append(' ');}else b.Append(ch);}
            return b.ToString().Trim();
        }
        public static HudScenePresentation Map(in HudRuntimeContext current,in ThirdPersonDisplayFrame display,in HudContentSnapshot snapshot,
            long epoch,long revision,in HudContentPolicy policy,in HudSceneOptions raw,bool expanded)
        {
            var o=raw.Sanitized();
            if(!o.Enabled||!HudContentRules.EvaluateDisplay(current,display,snapshot.Header,epoch,revision,policy).Allowed)return default;
            var r=new Reader(current,snapshot,epoch,revision,policy);var s=snapshot.Scene;
            var interaction=default(HudSceneCard);var objective=default(HudSceneCard);
            if(o.Interaction&&r.Subject(s.Interaction.Subject,out var target))
                interaction=new("交互 · "+r.Text(s.Interaction.Name,target,"交互目标"),r.Text(s.Interaction.Action,target,"操作未读取"),"按键 "+r.Text(s.Interaction.Key,target,"未读取"),r.Progress(s.Interaction.Progress,target));
            if(o.Supply&&r.Subject(s.Supply.Subject,out var pack)&&r.Read(s.Supply.Recipient,pack,out var recipient)
                &&recipient.Player.Agent==pack.Related&&recipient.Self==(recipient.Player==current.ExpectedBinding.LocalPlayer))
            {
                string count=r.Read(s.Supply.Uses,pack,out var q)&&q.Unit==HudUnit.Uses?q.Amount.ToString("0",CultureInfo.InvariantCulture)+"次":"未读取";
                interaction=new("补给 · "+r.Text(s.Supply.Item,pack),"接收对象 · "+(recipient.Self?"自己":Clean(recipient.Name,64)),"剩余 "+count,r.Progress(s.Supply.Progress,pack));
            }
            string title="",text="",secondary="";float? progress=null;
            if(o.Mission&&r.Subject(s.Countdown.Subject,out var timer)){title="倒计时 · "+r.Text(s.Countdown.Title,timer);text=r.Text(s.Countdown.Text,timer);}
            if(o.Scan&&r.Subject(s.Scan.Subject,out var scan))
            {if(title.Length==0){title=r.Text(s.Scan.Text,scan);progress=r.Progress(s.Scan.Progress,scan);text=progress.HasValue?HudReadoutFormatting.Percent(progress.Value,0):"进度未读取";}
                else secondary=r.Text(s.Scan.Text,scan)+" "+(r.Progress(s.Scan.Progress,scan) is float pg?HudReadoutFormatting.Percent(pg,0):"进度未读取");}
            if(o.Mission&&expanded&&r.Subject(s.Mission.Subject,out var mission))
            {if(title.Length==0){title="任务 · "+r.Text(s.Mission.Title,mission,"当前目标");text=r.Text(s.Mission.Text,mission);}else secondary+=(secondary.Length>0?" · ":"")+"任务 "+r.Text(s.Mission.Text,mission);}
            if(title.Length>0)objective=new(title,text,secondary,progress);
            HudSceneMarkerView first=default,second=default,third=default;int countVisible=0;
            if(o.Markers&&o.MaximumMarkers>0)
            {
                // Six candidates, three winners; deterministic severity and distance, no history or replay queue.
                var a=default(HudSceneMarkerView);var b=default(HudSceneMarkerView);var d=default(HudSceneMarkerView);
                double da=double.MaxValue,db=double.MaxValue,dd=double.MaxValue;
                for(int i=0;i<6;i++)
                {
                    var field=s.Marker(i);
                    if(!HudContentRules.TryRead(current,snapshot.Header,field,field.Stamp.Source,HudFreshness.Action,epoch,revision,policy,out var marker,out _)
                        ||marker.Identity!=field.Stamp.Source||marker.Dimension!=current.ExpectedBinding.DimensionIndex
                        ||marker.Kind==HudSceneMarkerKind.Rescue&&!o.Rescue||marker.Kind==HudSceneMarkerKind.Ping&&!o.Pings||marker.Kind==HudSceneMarkerKind.Detected&&!o.Detected)continue;
                    double distance=Vector3.Distance(display.BodyPose.Position,marker.Position.ToVector());if(!double.IsFinite(distance)||distance>o.MaximumDistance)continue;
                    if(!Project(display,marker,o,out var box,out var edge))continue;
                    int priority=marker.Kind==HudSceneMarkerKind.Rescue?100:marker.Kind==HudSceneMarkerKind.Ping?70:30;
                    var value=new HudSceneMarkerView(box,(edge?HudAlerts.Direction(marker.Position.ToVector()-display.BodyPose.Position,display.Camera.WorldToView)+" · ":"")+Clean(marker.Label,40)+" · "+Math.Round(distance).ToString("0",CultureInfo.InvariantCulture)+"m",marker.Kind,priority);
                    if(priority>a.Priority||priority==a.Priority&&distance<da){d=b;dd=db;b=a;db=da;a=value;da=distance;}
                    else if(priority>b.Priority||priority==b.Priority&&distance<db){d=b;dd=db;b=value;db=distance;}
                    else if(priority>d.Priority||priority==d.Priority&&distance<dd){d=value;dd=distance;}
                }
                first=a;second=b;third=d;countVisible=Math.Min(o.MaximumMarkers,(a.Priority>0?1:0)+(b.Priority>0?1:0)+(d.Priority>0?1:0));
            }
            return new(true,display.Camera.Viewport,o,interaction,objective,first,second,third,countVisible);
        }
        public static bool CountdownCovered(in HudRuntimeContext current,in HudContentSnapshot snapshot,long epoch,long revision,HudContentPolicy policy,HudSceneOptions raw)
        {
            var o=raw.Sanitized();if(!o.Enabled||!o.Mission||!o.ReplaceCountdown)return false;
            var r=new Reader(current,snapshot,epoch,revision,policy);var timer=snapshot.Scene.Countdown;
            if(!r.Subject(timer.Subject,out var subject)||!r.Read(timer.Title,subject,out var title)||!r.Read(timer.Text,subject,out var text))return false;
            // Preserve rich/combined formats and long titles that would need truncation.
            if(title.Text!=Clean(title.Text)||title.Text.Length>8||text.Text.Length>12||text.Text.Length==0)return false;
            bool colon=false,previousDigit=false;
            foreach(char ch in text.Text){bool digit=ch>='0'&&ch<='9';if(!digit){if((ch!=':'&&ch!='.')||!previousDigit)return false;colon|=ch==':';}previousDigit=digit;}
            return colon&&previousDigit;
        }
        static bool Project(ThirdPersonDisplayFrame display,HudSceneMarker marker,HudSceneOptions o,out PixelViewport box,out bool edge)
        {
            box=default;edge=false;var viewport=display.Camera.Viewport;
            var view=Vector4.Transform(new Vector4(marker.Position.ToVector(),1),display.Camera.WorldToView);
            var clip=Vector4.Transform(view,display.Camera.Projection);
            if(!float.IsFinite(clip.X)||!float.IsFinite(clip.Y)||!float.IsFinite(clip.Z)||!float.IsFinite(clip.W)||Math.Abs(clip.W)<.0001f)return false;
            if(clip.W>0&&(clip.Z/clip.W < -1||clip.Z/clip.W>1))return false;
            float nx=clip.X/Math.Abs(clip.W),ny=clip.Y/Math.Abs(clip.W);
            edge=clip.W<0||Math.Abs(nx)>.85f||Math.Abs(ny)>.85f;
            if(edge&&marker.Kind==HudSceneMarkerKind.Detected)return false;
            if(clip.W<0){nx=nx==0?0:MathF.CopySign(1,nx);ny=-1;}
            float scale=Math.Clamp(viewport.Height/1080f,.75f,2f);
            float width=Math.Min(210*scale,viewport.Width-2*o.ScreenMargin),height=24*scale;
            if(width<120||viewport.Height<2*o.ScreenMargin+height)return false;
            float x=viewport.X+(nx+1)*viewport.Width*.5f-width*.5f,y=viewport.Y+(ny+1)*viewport.Height*.5f+18*scale;
            x=Math.Clamp(x,viewport.X+o.ScreenMargin,viewport.X+viewport.Width-o.ScreenMargin-width);
            y=Math.Clamp(y,viewport.Y+o.ScreenMargin,viewport.Y+viewport.Height-o.ScreenMargin-height);
            // Keep the crosshair clear; detected contacts are optional, rescue/pings retain their original markers.
            float radius=o.MarkerCrosshairRadius*scale;
            var crosshair=new PixelViewport(viewport.X+viewport.Width*.5f-radius,viewport.Y+viewport.Height*.5f-radius,radius*2,radius*2);
            box=new(x,y,width,height);return radius==0||!HudSceneDrawing.Overlap(box,crosshair);
        }
    }
    public static class HudSceneDrawing
    {
        public static bool Overlap(PixelViewport a,PixelViewport b)=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y;
        public static bool Layout(in HudPanelPlacement panel,PixelViewport viewport,HudSceneOptions raw,out PixelViewport interaction,out PixelViewport objective)
        {
            var o=raw.Sanitized();float scale=o.CardScale*Math.Clamp(viewport.Height/1080f,.75f,2);float w=o.CardWidth*scale,h=104*scale;
            interaction=objective=default;
            if(!float.IsFinite(viewport.Width)||!float.IsFinite(viewport.Height)||viewport.Width<2*o.ScreenMargin+w||viewport.Height<2*o.ScreenMargin+2*h)return false;
            float x=Math.Clamp(viewport.X+viewport.Width*.5f-w*.5f+o.InteractionOffsetX*scale,viewport.X+o.ScreenMargin,viewport.X+viewport.Width-o.ScreenMargin-w);
            interaction=new(x,Math.Clamp(viewport.Y+o.InteractionBottom*scale,viewport.Y+o.ScreenMargin,viewport.Y+viewport.Height-o.ScreenMargin-h),w,h);
            if(Overlap(interaction,panel.Bounds))interaction=new(viewport.X+(panel.Bounds.X<viewport.X+viewport.Width*.5f?viewport.Width-o.ScreenMargin-w:o.ScreenMargin),interaction.Y,w,h);
            objective=new(viewport.X+(panel.Bounds.X<viewport.X+viewport.Width*.5f?viewport.Width-o.ScreenMargin-w:o.ScreenMargin),viewport.Y+viewport.Height-o.ScreenMargin-h,w,h);
            return !Overlap(interaction,panel.Bounds)&&!Overlap(objective,panel.Bounds)&&!Overlap(interaction,objective);
        }
        public static void Build(in HudPanelPlacement panel,in HudPanelOptions raw,in HudScenePresentation scene,HudDrawList output)
        {
            if(!scene.Active||!panel.IsValid||!Layout(panel,scene.Viewport,scene.Options,out var interact,out var objective))return;
            var options=raw.Sanitized();var bounds=panel.Bounds;var model=scene;float panelTop=bounds.Y+bounds.Height;var t=options.Theme;
            void Box(PixelViewport r,HudColor color,string region)=>output.Add(new(HudDrawKind.Rectangle,new(r.X-bounds.X,panelTop-r.Y-r.Height,r.Width,r.Height),color,"",0,region));
            void Text(PixelViewport r,string text,float font,HudColor color,string region,bool numeric=false)
            {output.Add(new(HudDrawKind.Text,new(r.X-bounds.X,panelTop-r.Y-r.Height,r.Width,r.Height),color,numeric?text:HudPanelDrawing.Ellipsize(text,r.Width,font),font,region){Numeric=numeric});}
            var accent=t.Base.Brightness(t.Brightness).Opacity(t.TextOpacity);var ink=t.Text.Brightness(t.Brightness).Opacity(t.TextOpacity);
            void Card(PixelViewport box,HudSceneCard card,string region)
            {
                if(!card.Active)return;float s=box.Width/model.Options.CardWidth;
                Box(box,t.Background.Opacity(t.BackgroundOpacity),region+"-background");Box(new(box.X,box.Y,2,box.Height),accent.Opacity(t.DecorationOpacity),region+"-rim");
                Text(new(box.X+12*s,box.Y+box.Height-26*s,box.Width-24*s,22*s),card.Title,options.Typography.LabelSize*s,accent,region+"-title");
                Text(new(box.X+12*s,box.Y+box.Height-52*s,box.Width-24*s,24*s),card.Text,options.Typography.BodySize*s,ink,region+"-text");
                if(!string.IsNullOrEmpty(card.Secondary))Text(new(box.X+12*s,box.Y+box.Height-76*s,box.Width-24*s,22*s),card.Secondary,options.Typography.LabelSize*s,accent,region+"-secondary");
                if(card.Progress is float pg){Box(new(box.X+12*s,box.Y+17*s,box.Width-92*s,2*s),accent.Opacity(.2f),region+"-progress-track");Box(new(box.X+12*s,box.Y+17*s,(box.Width-92*s)*pg,2*s),accent,region+"-progress");
                    Text(new(box.X+box.Width-64*s,box.Y+8*s,52*s,20*s),HudReadoutFormatting.Percent(pg,0),11*s,ink,region+"-percent",true);}
            }
            Card(interact,scene.Interaction,"scene-interaction");Card(objective,scene.Objective,"scene-objective");
            PixelViewport previous=default,beforePrevious=default;
            for(int i=0;i<scene.Count;i++)
            {var marker=scene[i];var r=marker.Bounds;
                if(Overlap(r,panel.Bounds)||scene.Interaction.Active&&Overlap(r,interact)||scene.Objective.Active&&Overlap(r,objective)||i>0&&Overlap(r,previous)||i>1&&Overlap(r,beforePrevious))continue;
                var color=marker.Kind==HudSceneMarkerKind.Rescue?t.Danger.Opacity(t.TextOpacity):accent;
                float scale=r.Height/24;
                Box(r,t.Background.Opacity(t.BackgroundOpacity),"scene-marker-background");Text(new(r.X+4*scale,r.Y,r.Width-8*scale,r.Height),marker.Text,options.Typography.LabelSize*scale,color,"scene-marker-"+marker.Kind);
                beforePrevious=previous;previous=r;
            }
        }
    }
}
