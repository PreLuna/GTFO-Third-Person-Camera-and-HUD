using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class AlertTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static readonly HudRuntimeContext C=PanelTestData.Context();
    static HudPanelView Show(HudContentSnapshot s,HudRuntimeContext? context=null,HudAlertOptions? options=null,long epoch=1,HudPersonalOptions? modules=null,HudAlerts alerts=null)
    {
        var c=context??C;var p=modules??HudPersonalOptions.Default;
        var view=HudPersonalMapper.Map(c,s,epoch,1,HudContentPolicy.Default,p,false);
        return (alerts??new()).Apply(c,PanelTestData.Display(c),true,s,epoch,1,HudContentPolicy.Default,view,options??HudAlertOptions.Default,HudPanelBehavior.Default);
    }
    static bool Has(HudPanelView v,string text)=>Enumerable.Range(0,v.Prompts.Count).Any(i=>v.Prompts[i].Text==text);
    static HudContentSnapshot Action(HudContentSnapshot s,HudActionKind kind,double? progress=null,long item=700)
    {
        var stamp=s.Personal.Health.Stamp;
        return s with{Personal=s.Personal with{Action=new(PersonalTestData.Value(new HudAction(kind),stamp),progress.HasValue?PersonalTestData.Value(new HudQuantity(progress.Value,HudUnit.Fraction),stamp):default){Equipment=new(item,(int)item)}}};
    }
    static HudContentSnapshot Hit(HudContentSnapshot s,double at=10,Vector3? direction=null)
        =>s with{Personal=s.Personal with{Hit=PersonalTestData.Value(new HudHit(1,at,direction??Vector3.UnitX),s.Personal.Health.Stamp)}};
    static int Main(string[] args)
    {
        Case("healthy-equipped-default-has-no-warning",()=>Check(Show(PersonalTestData.Snapshot(C)).Prompts.Count==0));
        Case("reload-shows-no-invented-progress",()=>{var v=Show(Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading));Check(Has(v,"换弹中")&&v.Prompts.First.Progress==null);});
        Case("charge-and-pack-progress-use-explicit-actions",()=>{var s=Action(PersonalTestData.Snapshot(C,HudEquipmentSlot.Melee),HudActionKind.MeleeCharging,item:710);Check(Has(Show(s),"近战蓄力中"));s=Action(PersonalTestData.Snapshot(C,HudEquipmentSlot.ResourcePack),HudActionKind.UsingItem,.42,703);Check(Show(s).Prompts.First.Progress==.42f);});
        Case("cancel-or-idle-removes-action-immediately",()=>{var a=new HudAlerts();Check(Show(Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading),alerts:a).Prompts.Count==1);Check(Show(Action(PersonalTestData.Snapshot(C),HudActionKind.Idle),alerts:a).Prompts.Count==0);});
        Case("wield-switch-cannot-reuse-old-action",()=>Check(!Has(Show(Action(PersonalTestData.Snapshot(C,HudEquipmentSlot.Secondary),HudActionKind.Reloading)),"换弹中")));
        Case("wrong-action-owner-rejected",()=>{var s=Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading);s=s with{Personal=s.Personal with{Action=s.Personal.Action with{Action=PersonalTestData.Value(new HudAction(HudActionKind.Reloading),s.Personal.Primary.Identity.Stamp)}}};Check(Show(s).Prompts.Count==0);});
        Case("expired-action-not-rejuvenated-by-live-health",()=>{var s=Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading);Check(Show(s,C with{NowSeconds=10.11}).Prompts.Count==0);});
        foreach(double progress in new[]{-1,double.NaN,1.1})Case("invalid-progress-retains-label-"+progress,()=>{var v=Show(Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading,progress));Check(Has(v,"换弹中")&&v.Prompts.First.Progress==null);});
        Case("progress-endpoints-are-real-zero-and-one",()=>{foreach(double p in new[]{0d,1d})Check(Show(Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading,p)).Prompts.First.Progress==(float)p);});
        Case("wrong-progress-unit-does-not-become-percentage",()=>{var s=Action(PersonalTestData.Snapshot(C),HudActionKind.Reloading);s=s with{Personal=s.Personal with{Action=s.Personal.Action with{Progress=PersonalTestData.Value(new HudQuantity(1,HudUnit.Seconds),s.Personal.Health.Stamp)}}};Check(Show(s).Prompts.First.Progress==null);});
        Case("empty-magazine-and-reserve-merged-per-weapon",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Primary=PersonalTestData.Gear(PersonalTestData.Request(C),HudEquipmentSlot.Primary,"枪",700,0,0)}};var v=Show(s);Check(v.Prompts.Count==1&&Has(v,"主武器弹匣空 · 备用耗尽"));});
        Case("reserve-threshold-is-actual-round-count",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Primary=PersonalTestData.Gear(PersonalTestData.Request(C),HudEquipmentSlot.Primary,"枪",700,4,20,.9)}};Check(Has(Show(s),"主武器备用不足"));Check(Show(s,options:HudAlertOptions.Default with{LowReserveRounds=19}).Prompts.Count==0);});
        Case("unknown-reserve-is-not-empty",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Primary=s.Personal.Primary with{ReserveRounds=default}}};Check(Show(s).Prompts.Count==0);});
        Case("unreadable-and-unequipped-are-not-resource-warnings",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Primary=PersonalTestData.Missing(PersonalTestData.Request(C),HudEquipmentSlot.Primary),Tool=PersonalTestData.Missing(PersonalTestData.Request(C),HudEquipmentSlot.Tool,HudValueState.Unreadable)}};Check(Show(s).Prompts.Count==0);});
        Case("wrong-equipment-stamp-does-not-trigger",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Primary=s.Personal.Primary with{Magazine=PersonalTestData.Value(new HudQuantity(0,HudUnit.Rounds),s.Personal.Secondary.Identity.Stamp)}}};Check(Show(s).Prompts.Count==0);});
        Case("tool-threshold-includes-deployed-resource",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Tool=PersonalTestData.Gear(PersonalTestData.Request(C),HudEquipmentSlot.Tool,"炮台",702,supply:.2,deployed:true)}};Check(Has(Show(s),"工具资源不足"));});
        Case("overcap-tool-is-not-low",()=>{var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Tool=PersonalTestData.Gear(PersonalTestData.Request(C),HudEquipmentSlot.Tool,"炮台",702,supply:1.2)}};Check(Show(s).Prompts.Count==0);});
        Case("zero-pack-and-items-are-distinct-warnings",()=>{var s=PersonalTestData.Snapshot(C);var r=PersonalTestData.Request(C);s=s with{Personal=s.Personal with{ResourcePack=PersonalTestData.Gear(r,HudEquipmentSlot.ResourcePack,"包",703),Consumable=PersonalTestData.Gear(r,HudEquipmentSlot.Consumable,"棒",704)}};var v=Show(s);Check(Has(v,"补给包耗尽")&&Has(v,"消耗品耗尽"));});
        Case("refill-or-dropping-removes-old-warning",()=>{var a=new HudAlerts();var s=PersonalTestData.Snapshot(C);s=s with{Personal=s.Personal with{Tool=PersonalTestData.Gear(PersonalTestData.Request(C),HudEquipmentSlot.Tool,"炮台",702,supply:0)}};Check(Has(Show(s,alerts:a),"工具资源耗尽"));Check(Show(PersonalTestData.Snapshot(C),alerts:a).Prompts.Count==0);});
        Case("critical-health-preempts-action",()=>{var s=Action(Hit(PersonalTestData.Snapshot(C,hp:.2)),HudActionKind.Reloading);var v=Show(s);Check(v.Prompts.Count==2&&v.Prompts.First.Kind==HudPromptKind.Health&&v.Prompts.Second.Kind==HudPromptKind.Hit);});
        Case("high-infection-and-downed-are-explicit",()=>{var s=PersonalTestData.Snapshot(C,infection:.8);s=s with{Personal=s.Personal with{Life=PersonalTestData.Value(new HudLife(HudLifeKind.Downed),s.Personal.Health.Stamp)}};var v=Show(s);Check(v.Prompts.First.Text=="已倒地"&&Has(v,"感染过高"));});
        Case("provider-fallback-wins-over-downed",()=>{var c=C with{LocalBodyViewActive=false};Check(Show(Hit(PersonalTestData.Snapshot(C,hp:.1)),c).Prompts.Count==0);});
        Case("hit-expiry-uses-event-time-not-snapshot-time",()=>{var c=C with{NowSeconds=11.3};var s=Hit(PersonalTestData.Snapshot(c),10);Check(!Has(Show(s,c),"受击 · 右侧"));});
        Case("future-hit-is-rejected",()=>Check(Show(Hit(PersonalTestData.Snapshot(C),10.1)).Prompts.Count==0));
        Case("directionless-hit-never-invents-a-side",()=>Check(Has(Show(Hit(PersonalTestData.Snapshot(C),direction:Vector3.Zero)),"受击")));
        Case("hit-direction-is-from-current-camera",()=>{var m=Matrix4x4.CreateScale(1,1,-1);Check(HudAlerts.Direction(Vector3.UnitZ,m)=="前方"&&HudAlerts.Direction(-Vector3.UnitZ,m)=="后方"&&HudAlerts.Direction(Vector3.UnitX,m)=="右侧"&&HudAlerts.Direction(-Vector3.UnitX,m)=="左侧");m=Matrix4x4.CreateRotationY(MathF.PI/2);Check(HudAlerts.Direction(Vector3.UnitZ,m)=="右侧");});
        Case("invalid-or-overhead-direction-has-no-horizontal-side",()=>{Check(HudAlerts.Direction(Vector3.UnitY,Matrix4x4.Identity)=="");Check(HudAlerts.Direction(new(float.NaN,0,0),Matrix4x4.Identity)=="");});
        Case("identity-and-lifecycle-versions-reject-old-prompts",()=>{var s=Hit(PersonalTestData.Snapshot(C));Check(Show(s,epoch:2).Prompts.Count==0);Check(Show(s,C with{CurrentView=C.CurrentView with{EligibilityEpoch=2}}).Prompts.Count==0);});
        Case("module-and-alert-switches-take-effect-immediately",()=>{var s=Action(Hit(PersonalTestData.Snapshot(C,hp:.1)),HudActionKind.Reloading);Check(Show(s,options:HudAlertOptions.Default with{Enabled=false}).Prompts.Count==0);Check(Show(s,options:HudAlertOptions.Default with{HitFeedback=false,StateFeedback=false},modules:HudPersonalOptions.Default with{Weapons=false}).Prompts.Count==0);});
        Case("bounded-rebuild-has-no-backlog-under-many-changes",()=>{var a=new HudAlerts();for(int i=0;i<1000;i++){var s=Action(Hit(PersonalTestData.Snapshot(C,hp:.1,infection:.9)),HudActionKind.Reloading);var v=Show(s,options:HudAlertOptions.Default with{MaximumVisible=1},alerts:a);Check(v.Prompts.Count==1&&v.Prompts.First.Kind==HudPromptKind.Health);}Check(Show(PersonalTestData.Snapshot(C),alerts:a).Prompts.Count==0);});
        Case("suppressed-expired-hit-is-never-replayed",()=>{var a=new HudAlerts();var s=Hit(PersonalTestData.Snapshot(C,hp:.1));Check(Show(s,options:HudAlertOptions.Default with{MaximumVisible=1},alerts:a).Prompts.First.Kind==HudPromptKind.Health);var c=C with{NowSeconds=12};Check(Show(Hit(PersonalTestData.Snapshot(c),10),c,alerts:a).Prompts.Count==0);});
        Case("three-prompts-fit-without-overlapping-readouts",()=>{var o=HudPanelOptions.Default;var s=Action(Hit(PersonalTestData.Snapshot(C,hp:.1,infection:.9)),HudActionKind.Reloading);var v=Show(s,options:HudAlertOptions.Default with{MaximumVisible=3});Check(HudPanelProjection.TryPlace(PanelTestData.Display(C),o.Layout,1,out var p));var list=new HudDrawList();HudPanelDrawing.Build(p.Panel,o,new(HudVisualState.Danger,1,false,false),v,list);Check(list.Count<256);for(int i=0;i<list.Count;i++){var d=list[i];Check(d.Rect.Y+d.Rect.Height<=HudPanelProjection.CompactHeight+.01f,d.Region);if(d.Region.StartsWith("prompt"))Check(d.Rect.Y>=283);}});
        Case("sanitization-caps-visible-count-and-thresholds",()=>{var o=(HudAlertOptions.Default with{MaximumVisible=99,LowReserveRounds=-10,LowToolRatio=float.NaN,HitSeconds=float.PositiveInfinity}).Sanitized();Check(o.MaximumVisible==3&&o.LowReserveRounds==0&&o.LowToolRatio==.2f&&o.HitSeconds==1.2f);});
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Action/feedback/alert checks: {results.Count}; passed={passed}");return passed?0:1;
    }
}
