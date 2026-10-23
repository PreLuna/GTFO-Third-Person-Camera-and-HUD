#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;

static class PanelTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string reason="assertion failed"){if(!ok)throw new Exception(reason);}
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static HudPlacement Place(HudPanelOptions? options=null,ThirdPersonDisplayFrame? display=null,float side=1)
    {Check(HudPanelProjection.TryPlace(display??PanelTestData.Display(PanelTestData.Context()),(options??HudPanelOptions.Default).Layout,side,out var p),"projection failed");return p;}
    static HudDrawCommand[] Commands(HudPanelOptions? options=null,HudPanelVisual? state=null,HudPanelView? view=null)
    {var o=options??HudPanelOptions.Default;var list=new HudDrawList();HudPanelDrawing.Build(Place(o).Panel,o,state??new(HudVisualState.Exploration,0,false,false),view??HudPanelView.OfflineExample,list);return Enumerable.Range(0,list.Count).Select(i=>list[i]).ToArray();}
    static void Inside(HudPlacement p,PixelViewport viewport)
    {var b=p.Panel.Bounds;Check(b.X>=viewport.X&&b.Y>=viewport.Y&&b.X+b.Width<=viewport.X+viewport.Width+.01f&&b.Y+b.Height<=viewport.Y+viewport.Height+.01f,"outside viewport");}
    static int Main(string[] args)
    {
        Case("formal-width-independent-of-marker-limit",()=>Check(Place().Panel.Bounds.Width>48));
        Case("layout-nonfinite-values-sanitized",()=>{var o=HudPanelLayoutOptions.Default with{Scale=float.NaN,Width=float.PositiveInfinity,AnchorHeight=float.NaN};var s=o.Sanitized();Check(s.Scale==1&&s.Width==304&&s.AnchorHeight==1.15f);});
        Case("compact-and-expanded-regions-have-distinct-height",()=>{var d=HudPanelOptions.Default;Check(Place(d with{Layout=d.Layout with{Expanded=true}}).Panel.Bounds.Height>Place(d).Panel.Bounds.Height);});
        Case("aiming-scales-panel-and-preserves-expanded-choice",()=>{var d=PanelTestData.Display(PanelTestData.Context());var p=Place(display:d with{View=d.View with{Mode=ThirdPersonMode.ShoulderAim}});Check(p.Panel.Aiming&&p.Panel.Scale<Place().Panel.Scale);});
        Case("left-and-right-layouts-use-current-shoulder",()=>Check(Place(side:-1).PixelCenter.X<Place(side:1).PixelCenter.X));
        Case("configurable-horizontal-offset",()=>{var o=HudPanelOptions.Default;Check(Place(o with{Layout=o.Layout with{RightOffset=new(350,-48)}}).PixelCenter.X>Place().PixelCenter.X);});
        Case("screen-edge-anchor-clamps-whole-panel",()=>{var d=PanelTestData.Display(PanelTestData.Context());d=d with{BodyPose=d.BodyPose with{Position=new(100,0,0)}};Inside(Place(display:d),d.Camera.Viewport);});
        Case("behind-camera-is-not-edge-clamped",()=>{var d=PanelTestData.Display(PanelTestData.Context());d=d with{BodyPose=d.BodyPose with{Position=new(0,0,5)}};Check(!HudPanelProjection.TryPlace(d,HudPanelLayoutOptions.Default,1,out _));});
        Case("nonfinite-camera-is-rejected",()=>{var d=PanelTestData.Display(PanelTestData.Context());var matrix=d.Camera.Projection;matrix.M11=float.NaN;d=d with{Camera=d.Camera with{Projection=matrix}};Check(!HudPanelProjection.TryPlace(d,HudPanelLayoutOptions.Default,1,out _));});
        Case("unreadably-small-viewports-hide",()=>{var d=PanelTestData.Display(PanelTestData.Context());d=d with{Camera=d.Camera with{Viewport=new(0,0,120,90)}};Check(!HudPanelProjection.TryPlace(d,HudPanelLayoutOptions.Default,1,out _));});
        foreach(var v in new[]{new PixelViewport(0,0,1280,720),new(0,0,2560,1440),new(0,0,3440,1440),new(430,180,960,600)})
            Case("viewport-fit-"+v.Width+"x"+v.Height,()=>{var d=PanelTestData.Display(PanelTestData.Context());d=d with{Camera=d.Camera with{Viewport=v}};Inside(Place(display:d),v);});
        Case("crosshair-safe-area-never-covered",()=>{var p=Place();var b=p.Panel.Bounds;var r=HudPanelLayoutOptions.Default.CrosshairRadius*p.Panel.Scale;Check(b.X>=960+r||b.X+b.Width<=960-r||b.Y>=540+r||b.Y+b.Height<=540-r);});
        Case("motion-smooths-offset-not-old-screen-position",()=>{var c=PanelTestData.Context();var d=PanelTestData.Display(c);var a=new HudAnchorSpec(Vector3.Zero,0,12){Panel=HudPanelLayoutOptions.Default};var m=new HudPanelMotion();Check(m.TryPlace(d,a,out var p));var matrix=d.Camera.WorldToView;matrix.M41-=.5f;d=d with{CapturedAtSeconds=10.02,Camera=d.Camera with{WorldToView=matrix}};Check(m.TryPlace(d,a,out var next)&&next.PixelCenter.X<p.PixelCenter.X);});
        Case("shoulder-change-interpolates-and-new-context-resets",()=>{var d=PanelTestData.Display(PanelTestData.Context());var m=new HudPanelMotion();var a=new HudAnchorSpec(Vector3.Zero,0,12){Panel=HudPanelLayoutOptions.Default};m.TryPlace(d,a,out _);d=d with{CapturedAtSeconds=10.02,Camera=d.Camera with{Pose=d.Camera.Pose with{Position=new(-.48f,1,3)}}};Check(m.TryPlace(d,a,out var p)&&p.Panel.Side<1&&p.Panel.Side>-1);d=d with{View=d.View with{Context=new(Guid.NewGuid(),1)}};Check(m.TryPlace(d,a,out p)&&p.Panel.Side==-1);});
        Case("fixed-side-ignores-camera-shoulder",()=>{var d=PanelTestData.Display(PanelTestData.Context());var a=new HudAnchorSpec(Vector3.Zero,0,12){Panel=HudPanelLayoutOptions.Default with{MirrorWithShoulder=false,FixedSide=-1}};Check(new HudPanelMotion().TryPlace(d,a,out var p)&&p.Panel.Side==-1);});
        Case("drawing-commands-fit-panel",()=>{var o=HudPanelOptions.Default;foreach(var c in Commands(o)){Check(c.Rect.X>=0&&c.Rect.Y>=0&&c.Rect.X+c.Rect.Width<=o.Layout.Width+.01f&&c.Rect.Y+c.Rect.Height<=HudPanelProjection.CompactHeight+.01f,c.Region);}});
        Case("expanded-details-are-actually-added",()=>{var o=HudPanelOptions.Default;var commands=Commands(o with{Layout=o.Layout with{Expanded=true}});Check(commands.Any(c=>c.Region=="secondary")&&commands.Any(c=>c.Region=="supply"));});
        Case("temporary-notice-has-a-bounded-independent-region",()=>{var commands=Commands(view:HudPanelView.OfflineExample with{Notice="换弹中"});Check(commands.Single(c=>c.Region=="notice").Text=="换弹中");Check(!commands.Any(c=>c.Region=="action-progress"));});
        Case("empty-view-never-uses-offline-example-numbers",()=>{var commands=Commands(view:HudPanelView.Empty);Check(commands.Where(c=>c.Kind==HudDrawKind.Text).All(c=>!c.Text.Any(char.IsDigit)));Check(!commands.Any(c=>c.Region=="health-fill"||c.Region=="tool-fill"));});
        Case("background-opacity-does-not-fade-labels",()=>{var o=HudPanelOptions.Default;var a=Commands(o);var b=Commands(o with{Theme=o.Theme with{BackgroundOpacity=0}});Check(b.Single(c=>c.Region=="background").Color.A==0&&a.First(c=>c.Region=="health"&&c.Kind==HudDrawKind.Text).Color==b.First(c=>c.Region=="health"&&c.Kind==HudDrawKind.Text).Color);});
        Case("decoration-disable-keeps-core-readings",()=>{var o=HudPanelOptions.Default;Check(Commands(o with{Theme=o.Theme with{Decorations=false,Glow=false}}).Any(c=>c.Region=="magazine"));});
        Case("optional-effects-stay-bounded",()=>{var o=HudPanelOptions.Default;Check(Commands(o with{Theme=o.Theme with{ScanLines=true},Layout=o.Layout with{Expanded=true}}).Length<128);});
        Case("danger-has-word-as-well-as-color",()=>Check(Commands(state:new(HudVisualState.Danger,1,false,false)).Any(c=>c.Region=="state"&&c.Text=="危险")));
        Case("chinese-long-label-is-ellipsized",()=>Check(HudPanelDrawing.Ellipsize("很长很长的中文装备名称以及扩展状态",180,16).EndsWith("…")));
        Case("surrogate-pair-is-not-split",()=>{string t=HudPanelDrawing.Ellipsize("😀😀😀😀😀😀",40,16);for(int i=0;i<t.Length;i++)if(char.IsHighSurrogate(t[i])){Check(i+1<t.Length&&char.IsLowSurrogate(t[++i]));}});
        Case("progress-fill-is-clipped-without-changing-text",()=>{var v=HudPanelView.OfflineExample with{Health="140%",HealthFraction=1.4f};var c=Commands(view:v);Check(c.Single(c=>c.Region=="health-fill").Rect.Width==276&&c.Any(c=>c.Text=="140%"));});
        Case("invalid-fill-does-not-create-nan-geometry",()=>Check(!Commands(view:HudPanelView.Empty with{HealthFraction=float.NaN}).Any(c=>c.Region=="health-fill")));
        Case("hold-expand-releases-and-toggle-does-not-repeat",()=>{var s=default(HudExpandState).Update(true,true,false);Check(s.Expanded&&!s.Update(true,false,false).Expanded);s=default(HudExpandState).Update(true,true,true);Check(s.Expanded&&s.Update(true,true,true).Expanded);s=s.Update(true,false,true).Update(true,true,true);Check(!s.Expanded);Check(!s.Update(false,true,true).Expanded);});
        Case("same-owner-health-drop-triggers-combat",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();v.Update(c,true,PanelTestData.Snapshot(c),HudContentPolicy.Default,HudPanelOptions.Default);c=c with{NowSeconds=10.1,FrameNumber=101};Check(v.Update(c,true,PanelTestData.Snapshot(c,.7),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Combat);});
        Case("low-health-and-high-infection-enter-danger",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();Check(v.Update(c,true,PanelTestData.Snapshot(c,.2),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Danger);Check(v.Update(c,true,PanelTestData.Snapshot(c,.8,infection:.6),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Danger);});
        Case("weapon-change-does-not-invent-firing",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();v.Update(c,true,PanelTestData.Snapshot(c),HudContentPolicy.Default,HudPanelOptions.Default);c=c with{NowSeconds=10.1,FrameNumber=101};Check(v.Update(c,true,PanelTestData.Snapshot(c,clip:8,weapon:701),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Exploration);});
        Case("same-weapon-ammo-change-triggers-emphasis",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();v.Update(c,true,PanelTestData.Snapshot(c),HudContentPolicy.Default,HudPanelOptions.Default);c=c with{NowSeconds=10.1,FrameNumber=101};Check(v.Update(c,true,PanelTestData.Snapshot(c,clip:31),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Combat);});
        Case("expired-values-do-not-keep-danger",()=>{var c=PanelTestData.Context();var s=PanelTestData.Snapshot(c,.2);var v=new HudPanelVisualController();v.Update(c,true,s,HudContentPolicy.Default,HudPanelOptions.Default);c=c with{NowSeconds=10.4,FrameNumber=102};Check(v.Update(c,true,s,HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Exploration);});
        Case("invalid-view-resets-visual-memory",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();v.Update(c,true,PanelTestData.Snapshot(c,.2),HudContentPolicy.Default,HudPanelOptions.Default);c=c with{FocusAllowed=false};Check(v.Update(c,true,PanelTestData.Snapshot(c,.2),HudContentPolicy.Default,HudPanelOptions.Default).State==HudVisualState.Exploration);});
        Case("reduced-motion-is-immediate",()=>{var c=PanelTestData.Context();var v=new HudPanelVisualController();var o=HudPanelOptions.Default with{Behavior=HudPanelBehavior.Default with{ReduceMotion=true}};Check(v.Update(c,true,PanelTestData.Snapshot(c,.2),HudContentPolicy.Default,o).Emphasis==1);});
        Case("malformed-panel-anchor-is-rejected-before-application",()=>Check(!HudComputationRunner.ValidAnchor(new(Vector3.Zero,0,12){Panel=HudPanelLayoutOptions.Default with{Scale=float.NaN}})));
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false,RuntimeRenderingVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Panel layout/visual checks: {results.Count}; passed={passed}");return passed?0:1;
    }
}
