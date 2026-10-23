using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class SceneTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();static readonly HudRuntimeContext c=PanelTestData.Context();
    static void Check(bool value){if(!value)throw new Exception("assertion failed");}
    static void Case(string name,Action a){try{a();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static HudScenePresentation Map(HudContentSnapshot s,HudRuntimeContext? current=null,HudSceneOptions? options=null,bool expanded=true)
    {var now=current??c;return HudSceneMapper.Map(now,PanelTestData.Display(now),s,1,1,HudContentPolicy.Default,options??HudSceneOptions.Default,expanded);}
    static bool Covered(HudContentSnapshot s,HudSceneOptions? o=null)=>HudSceneMapper.CountdownCovered(c,s,1,1,HudContentPolicy.Default,o??HudSceneOptions.Default);
    static int Main(string[] args)
    {
        var r=PersonalTestData.Request(c);var s=SceneTestData.Snapshot(c);var interaction=s.Scene.Interaction;
        Case("selected-object-and-real-binding",()=>{var m=Map(s);Check(m.Interaction.Title=="交互 · 安全门"&&m.Interaction.Text=="开启"&&m.Interaction.Secondary=="按键 F"&&m.Interaction.Progress==.42f);});
        Case("missing-binding-does-not-invent-E",()=>Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Key=default}}}).Interaction.Secondary=="按键 未读取"));
        Case("unsupported-progress-is-not-estimated",()=>Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Progress=default}}}).Interaction.Progress==null));
        Case("known-zero-progress",()=>Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Progress=PersonalTestData.Value(new HudQuantity(0,HudUnit.Fraction),interaction.Progress.Stamp)}}}).Interaction.Progress==0));
        Case("over-one-progress-rejected",()=>Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Progress=PersonalTestData.Value(new HudQuantity(1.1,HudUnit.Fraction),interaction.Progress.Stamp)}}}).Interaction.Progress==null));
        Case("wrong-progress-unit-rejected",()=>Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Progress=PersonalTestData.Value(new HudQuantity(2,HudUnit.Seconds),interaction.Progress.Stamp)}}}).Interaction.Progress==null));
        Case("old-target-field-revision-rejected",()=>{var stamp=interaction.Subject.Stamp with{SubjectRevision=2};var target=SceneTestData.Subject(stamp);Check(Map(s with{Scene=s.Scene with{Interaction=interaction with{Subject=target}}}).Interaction.Progress==null);});
        Case("wrong-field-source-rejected",()=>{var field=PersonalTestData.Value(new HudText("旧目标"),interaction.Name.Stamp with{Source=new(777,777)});Check(!Map(s with{Scene=s.Scene with{Interaction=interaction with{Name=field}}}).Interaction.Title.Contains("旧目标"));});
        Case("subject-target-stamp-mismatch-rejected",()=>{var field=PersonalTestData.Value(new HudSceneSubject(new(999,999),default,1),interaction.Subject.Stamp);Check(!Map(s with{Scene=s.Scene with{Interaction=interaction with{Subject=field}}}).Interaction.Active);});
        Case("supply-overrides-generic-interaction",()=>{var supply=SceneTestData.Supply(r);var m=Map(s with{Scene=s.Scene with{Supply=supply}});Check(m.Interaction.Title=="补给 · 医疗包"&&m.Interaction.Text=="接收对象 · 队员甲"&&m.Interaction.Secondary=="剩余 2次");});
        Case("supply-self-is-explicit",()=>Check(Map(s with{Scene=s.Scene with{Supply=SceneTestData.Supply(r,true)}}).Interaction.Text=="接收对象 · 自己"));
        Case("supply-zero-uses-is-known",()=>{var pack=SceneTestData.Supply(r);pack=pack with{Uses=PersonalTestData.Value(new HudQuantity(0,HudUnit.Uses),pack.Uses.Stamp)};Check(Map(s with{Scene=s.Scene with{Supply=pack}}).Interaction.Secondary=="剩余 0次");});
        Case("supply-missing-uses-not-zero",()=>{var pack=SceneTestData.Supply(r) with{Uses=default};Check(Map(s with{Scene=s.Scene with{Supply=pack}}).Interaction.Secondary=="剩余 未读取");});
        Case("supply-foreign-recipient-rejected",()=>{var pack=SceneTestData.Supply(r);pack=pack with{Subject=SceneTestData.Subject(pack.Subject.Stamp,new(999,999))};Check(Map(s with{Scene=s.Scene with{Supply=pack}}).Interaction.Title.StartsWith("交互"));});
        Case("supply-self-flag-must-match-player",()=>{var pack=SceneTestData.Supply(r);pack=pack with{Recipient=PersonalTestData.Value(pack.Recipient.RawValue with{Self=true},pack.Recipient.Stamp)};Check(Map(s with{Scene=s.Scene with{Supply=pack}}).Interaction.Title.StartsWith("交互"));});
        Case("countdown-mirrors-game-text-without-extrapolation",()=>Check(Map(s,c with{NowSeconds=c.NowSeconds+.05}).Objective.Text=="02:18"));
        Case("mission-summary-only-when-expanded",()=>{var data=s with{Scene=s.Scene with{Countdown=default,Scan=default}};Check(Map(data).Objective.Text.Contains("数据立方体")&&!Map(data,expanded:false).Objective.Active);});
        Case("scan-independent-progress",()=>{var data=s with{Scene=s.Scene with{Countdown=default}};Check(Map(data).Objective.Title=="扫描中"&&Map(data).Objective.Progress==.4f);});
        Case("missing-scan-progress-not-zero",()=>{var data=s with{Scene=s.Scene with{Countdown=default,Scan=s.Scene.Scan with{Progress=default}}};Check(Map(data).Objective.Text=="进度未读取"&&Map(data).Objective.Progress==null);});
        Case("stale-action-fields-clear-cards-and-markers",()=>{var m=Map(s,c with{NowSeconds=c.NowSeconds+.11});Check(!m.Interaction.Active&&!m.Objective.Active&&m.Count==0);});
        Case("wrong-context-rejected",()=>Check(!Map(s,c with{ExpectedContext=new(Guid.NewGuid(),1)}).Active));
        Case("wrong-work-epoch-rejected",()=>Check(!Map(s with{Header=s.Header with{WorkEpoch=2}}).Active));
        Case("auxiliary-texture-never-gets-scene-HUD",()=>{var now=c with{Render=c.Render with{Phase=RenderPhase.AuxiliaryActive}};Check(!Map(s,now).Active);});
        Case("first-person-never-gets-scene-HUD",()=>Check(!Map(s,c with{CurrentView=c.CurrentView with{Mode=ThirdPersonMode.FirstPersonAim}}).Active));
        Case("empty-scene-creates-no-fake-cards",()=>{var m=Map(s with{Scene=default});Check(!m.Interaction.Active&&!m.Objective.Active&&m.Count==0);});
        Case("scene-master-switch",()=>Check(!Map(s,options:HudSceneOptions.Default with{Enabled=false}).Active));
        Case("interaction-switch-independent",()=>{var m=Map(s,options:HudSceneOptions.Default with{Interaction=false});Check(!m.Interaction.Active&&m.Objective.Active);});
        Case("supply-switch-independent",()=>{var m=Map(s with{Scene=s.Scene with{Supply=SceneTestData.Supply(r)}},options:HudSceneOptions.Default with{Supply=false});Check(m.Interaction.Title.StartsWith("交互"));});
        Case("mission-switch-keeps-scan",()=>Check(Map(s,options:HudSceneOptions.Default with{Mission=false}).Objective.Title=="扫描中"));
        Case("scan-switch-keeps-countdown",()=>Check(Map(s,options:HudSceneOptions.Default with{Scan=false}).Objective.Title.StartsWith("倒计时")));
        Case("marker-priority-rescue-before-ping-before-contact",()=>{var m=Map(s);Check(m.Count==3&&m.First.Kind==HudSceneMarkerKind.Rescue&&m.Second.Kind==HudSceneMarkerKind.Ping&&m.Third.Kind==HudSceneMarkerKind.Detected);});
        Case("marker-maximum-is-enforced",()=>Check(Map(s,options:HudSceneOptions.Default with{MaximumMarkers=1}).Count==1));
        Case("zero-markers-disables-labels",()=>Check(Map(s,options:HudSceneOptions.Default with{MaximumMarkers=0}).Count==0));
        Case("marker-kind-switches-independent",()=>{var m=Map(s,options:HudSceneOptions.Default with{Rescue=false,Pings=false});Check(m.Count==1&&m.First.Kind==HudSceneMarkerKind.Detected);});
        Case("wrong-dimension-never-revealed",()=>{var marker=SceneTestData.Marker(r,860,HudSceneMarkerKind.Rescue,-6,1,-5,1);Check(Map(s with{Scene=s.Scene with{Marker0=marker}}).Count==2);});
        Case("far-away-markers-filtered",()=>Check(Map(s,options:HudSceneOptions.Default with{MaximumDistance=5}).Count==0));
        Case("behind-camera-contact-hidden",()=>{var data=s with{Scene=s.Scene with{Marker0=default,Marker1=default,Marker2=SceneTestData.Marker(r,999,HudSceneMarkerKind.Detected,0,0,10)}};Check(Map(data).Count==0);});
        Case("contacts-beyond-far-clip-not-shown",()=>{var data=s with{Scene=s.Scene with{Marker0=default,Marker1=default,Marker2=SceneTestData.Marker(r,999,HudSceneMarkerKind.Detected,-200,0,-300)}};Check(Map(data,options:HudSceneOptions.Default with{MaximumDistance=500}).Count==0);});
        Case("behind-camera-rescue-has-direction",()=>{var data=s with{Scene=s.Scene with{Marker0=SceneTestData.Marker(r,999,HudSceneMarkerKind.Rescue,0,0,10),Marker1=default,Marker2=default}};var m=Map(data);Check(m.Count==1&&m.First.Text.Contains("后方"));});
        Case("countdown-covered-only-in-valid-simple-format",()=>Check(Covered(s)&&!Covered(s,HudSceneOptions.Default with{ReplaceCountdown=false})&&!Covered(s,HudSceneOptions.Default with{Mission=false})));
        Case("long-countdown-title-preserves-original",()=>{var timer=SceneTestData.Objective(r,840,"过长的倒计时标题必须继续保留原版","02:18");Check(!Covered(s with{Scene=s.Scene with{Countdown=timer}}));});
        Case("combined-countdown-text-preserves-original",()=>{var timer=SceneTestData.Objective(r,840,"撤离","02:18 附加警告");Check(!Covered(s with{Scene=s.Scene with{Countdown=timer}}));});
        Case("invalid-clock-separators-preserve-original",()=>Check(!Covered(s with{Scene=s.Scene with{Countdown=SceneTestData.Objective(r,840,"撤离","02::18")}})));
        Case("markers-scale-for-4K-reading",()=>{var display=PanelTestData.Display(c);display=display with{Camera=display.Camera with{Viewport=new(0,0,3840,2160)}};var m=HudSceneMapper.Map(c,display,s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);Check(m.First.Bounds.Width==420&&m.First.Bounds.Height==48);});
        Case("rich-countdown-title-preserves-original",()=>Check(!Covered(s with{Scene=s.Scene with{Countdown=SceneTestData.Objective(r,840,"<color=red>撤离</color>","02:18")}})));
        Case("text-control-codes-and-rich-tags-removed",()=>Check(HudSceneMapper.Clean("<color=red>开启</color>\n门\u2028F")=="开启 门 F"));
        Case("bounded-label-input",()=>Check(HudSceneMapper.Clean(new string('界',1000)).Length==160));
        foreach(int height in new[]{720,1080,1440,2160})foreach(bool expanded in new[]{false,true})
        {int h=height;bool e=expanded;Case("viewport-cards-and-budget-"+h+"-"+e,()=>{var display=PanelTestData.Display(c);display=display with{Camera=display.Camera with{Viewport=new(120,90,h*16f/9,h)}};var options=HudPanelOptions.Default with{Layout=HudPanelLayoutOptions.Default with{Expanded=e,Team=true},Theme=HudPanelTheme.Default with{ScanLines=true}};Check(HudPanelProjection.TryPlace(display,options.Layout,1,out var place));var scene=HudSceneMapper.Map(c,display,s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,e);var view=HudPersonalMapper.Map(c,s,1,1,HudContentPolicy.Default,HudPersonalOptions.Default,e) with{Team=HudTeamMapper.Map(c,s,1,1,HudContentPolicy.Default,HudTeamOptions.Default,e),Scene=scene};var list=new HudDrawList();HudPanelDrawing.Build(place.Panel,options,new(HudVisualState.Exploration,0,e,false),view,list);Check(list.Count<256&&Enumerable.Range(0,list.Count).Any(i=>list[i].Region=="scene-interaction-title"));Check(HudSceneDrawing.Layout(place.Panel,display.Camera.Viewport,HudSceneOptions.Default,out var a,out var b)&&!HudSceneDrawing.Overlap(a,place.Panel.Bounds)&&!HudSceneDrawing.Overlap(b,place.Panel.Bounds));foreach(int i in Enumerable.Range(0,list.Count).Where(i=>list[i].Region.StartsWith("scene-"))){var rect=list[i].Rect;float x=rect.X+place.Panel.Bounds.X,y=place.Panel.Bounds.Y+place.Panel.Bounds.Height-rect.Y-rect.Height;Check(x>=120&&x+rect.Width<=120+display.Camera.Viewport.Width+1&&y>=90&&y+rect.Height<=90+h+1);}});}
        Case("insufficient-viewport-retains-original",()=>Check(!HudSceneDrawing.Layout(new(new(0,0,300,300),1,false,false,1),new(0,0,320,240),HudSceneOptions.Default,out _,out _)));
        bool passed=results.All(x=>x.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));foreach(var x in results.Where(x=>!x.Passed))Console.WriteLine("FAIL "+x.Name+": "+x.Error);Console.WriteLine($"Scene rules/drawing: {results.Count}; passed={passed}");return passed?0:1;
    }
}
