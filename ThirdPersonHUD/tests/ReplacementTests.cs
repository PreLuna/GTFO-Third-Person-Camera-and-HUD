using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;

static class ReplacementTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static readonly HudRuntimeContext c=PanelTestData.Context();
    static void Check(bool b,string why="assertion failed"){if(!b)throw new Exception(why);}
    static void Case(string n,Action action){try{action();results.Add(new(n,true,null));}catch(Exception e){results.Add(new(n,false,e.ToString()));}}
    static HudOriginalGroup Coverage(HudContentSnapshot s,HudRuntimeContext? context=null,HudPersonalOptions? options=null,bool expanded=false,long epoch=1)
        =>HudReplacementCoverage.Evaluate(context??c,s,epoch,1,HudContentPolicy.Default,options??HudPersonalOptions.Default,expanded);
    static void Apply(HudOriginalHud h,HudOriginalGroup groups=HudOriginalGroup.All,HudRuntimeContext? context=null,bool drawn=true)
    {var current=context??c;h.Apply(current,PanelTestData.Display(current),drawn,groups);}
    static int Main(string[] args)
    {
        Case("compact-gun-hides-only-covered-groups",()=>Check(Coverage(PersonalTestData.Snapshot(c))==(HudOriginalGroup.Health|HudOriginalGroup.Infection|HudOriginalGroup.Ammo|HudOriginalGroup.Tool)));
        Case("expanded-items-are-covered",()=>Check(Coverage(PersonalTestData.Snapshot(c),expanded:true)==HudOriginalGroup.All));
        Case("compact-tool-preserves-weapons-that-are-not-displayed",()=>{var m=Coverage(PersonalTestData.Snapshot(c,HudEquipmentSlot.Tool));Check((m&HudOriginalGroup.Ammo)==0);});
        Case("expanded-tool-can-cover-gun-summaries",()=>Check((Coverage(PersonalTestData.Snapshot(c,HudEquipmentSlot.Tool),expanded:true)&HudOriginalGroup.Ammo)!=0));
        Case("one-missing-gun-restores-entire-ammo-group",()=>{var s=PersonalTestData.Snapshot(c);s=s with{Personal=s.Personal with{Secondary=s.Personal.Secondary with{ReserveRounds=default}}};Check((Coverage(s)&HudOriginalGroup.Ammo)==0&&(Coverage(s)&HudOriginalGroup.Health)!=0);});
        Case("partial-item-count-keeps-original-items",()=>{var s=PersonalTestData.Snapshot(c);s=s with{Personal=s.Personal with{Consumable=s.Personal.Consumable with{ItemCount=default}}};Check((Coverage(s,expanded:true)&HudOriginalGroup.Items)==0);});
        Case("foreign-item-count-cannot-cover-original",()=>{var s=PersonalTestData.Snapshot(c);s=s with{Personal=s.Personal with{ResourcePack=s.Personal.ResourcePack with{Uses=PersonalTestData.Value(new HudQuantity(2,HudUnit.Uses),s.Personal.Primary.Identity.Stamp)}}};Check((Coverage(s,expanded:true)&HudOriginalGroup.Items)==0);});
        Case("not-equipped-is-explicitly-covered-absence",()=>{var s=PersonalTestData.Snapshot(c);s=s with{Personal=s.Personal with{Secondary=PersonalTestData.Missing(PersonalTestData.Request(c),HudEquipmentSlot.Secondary)}};Check((Coverage(s)&HudOriginalGroup.Ammo)!=0);});
        Case("unknown-and-unreadable-are-never-covered-absence",()=>{var s=PersonalTestData.Snapshot(c);s=s with{Personal=s.Personal with{Heavy=PersonalTestData.Missing(PersonalTestData.Request(c),HudEquipmentSlot.Heavy,HudValueState.Unreadable)}};Check((Coverage(s,expanded:true)&HudOriginalGroup.Items)==0);});
        Case("zero-health-remains-current-but-downed-restores-all",()=>{var s=PersonalTestData.Snapshot(c,hp:0);Check((Coverage(s)&HudOriginalGroup.Health)!=0);s=s with{Personal=s.Personal with{Life=PersonalTestData.Value(new HudLife(HudLifeKind.Downed),s.Personal.Health.Stamp)}};Check(Coverage(s)==0);});
        Case("expired-health-restores-only-health",()=>{var s=PersonalTestData.Snapshot(c);var later=c with{NowSeconds=10.3};var fresh=PersonalTestData.Snapshot(later);fresh=fresh with{Personal=fresh.Personal with{Health=s.Personal.Health}};Check((Coverage(fresh,later)&HudOriginalGroup.Health)==0&&(Coverage(fresh,later)&HudOriginalGroup.Ammo)!=0);});
        Case("old-lifecycle-version-and-fallback-cover-nothing",()=>{var s=PersonalTestData.Snapshot(c);Check(Coverage(s,epoch:2)==0&&Coverage(s,c with{LocalBodyViewActive=false})==0);});
        Case("module-switches-restore-their-corresponding-groups",()=>{var m=Coverage(PersonalTestData.Snapshot(c),options:HudPersonalOptions.Default with{Health=false,Tools=false,Items=false},expanded:true);Check(m==(HudOriginalGroup.Infection|HudOriginalGroup.Ammo));});
        Case("disabled-personal-panel-covers-nothing",()=>Check(Coverage(PersonalTestData.Snapshot(c),options:HudPersonalOptions.Default with{Enabled=false})==0));
        Case("successful-render-is-required-before-any-write",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h,drawn:false);Check(p.Calls==0&&h.Status.OwnedTargets==0);});
        Case("hide-restore-only-actual-changes",()=>{var p=new OriginalTestPort();p.Targets[0].Value=true;var h=new HudOriginalHud(p);Apply(h);Check(h.Status.OwnedTargets==4);h.RestoreAll();Check(p.Targets[0].Value&&p.Targets[0].Writes==0&&p.Targets.Skip(1).All(t=>!t.Value));});
        Case("same-frame-repaint-does-not-repeat-visibility-writes",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);for(int i=0;i<1000;i++)Apply(h);Check(h.Status.Writes==5&&p.Targets.All(t=>t.Writes==1));});
        Case("independent-group-switch-restores-only-that-group",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);h.RetainOnly(HudOriginalGroup.All&~HudOriginalGroup.Tool);Check(!p.Targets[3].Value&&p.Targets[0].Value);});
        Case("foreign-visible-write-yields-until-explicit-reset",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Targets[0].Value=false;Apply(h);for(int i=0;i<20;i++)Apply(h);Check(!p.Targets[0].Value&&(h.Status.Conflicted&HudOriginalGroup.Health)!=0);h.ResetConflicts();Apply(h);Check(p.Targets[0].Value);});
        Case("widget-rebuild-restores-old-instance-before-hiding-new",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);var old=p.Targets[0];p.Targets[0]=new(){Identity=new(99999,111)};Apply(h);Check(!old.Value&&p.Targets[0].Value);});
        Case("destroyed-target-is-not-written-during-restore",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Targets[0].Alive=false;h.RestoreAll();Check(p.Targets[0].Writes==1&&h.Status.OwnedTargets==0);});
        Case("missing-targets-restore-just-the-unavailable-group",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Missing=HudOriginalGroup.Ammo;Apply(h);Check(!p.Targets[2].Value&&p.Targets[0].Value);});
        Case("port-exception-restores-all-owned-targets",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Throw=true;Apply(h);Check(h.Status.OwnedTargets==0&&h.Status.Errors>0);});
        Case("mutation-then-exception-is-rolled-back",()=>{var p=new OriginalTestPort();p.Targets[2].ThrowAfter=true;var h=new HudOriginalHud(p);Apply(h);Check(p.Targets.All(t=>!t.Value)&&h.Status.OwnedTargets==0);});
        Case("failed-restore-retains-ownership-for-retry",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Targets[0].ThrowBefore=true;h.RestoreAll();Check(h.Status.PendingRestores==1);h.RetainOnly(0);Check(h.Status.PendingRestores==0&&p.Targets.All(t=>!t.Value));});
        Case("failed-read-retains-restoration-until-property-recovers",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);p.Targets[0].ThrowRead=true;Apply(h);Check(h.Status.PendingRestores==1);p.Targets[0].ThrowRead=false;h.RetainOnly(0);Check(h.Status.OwnedTargets==0);});
        Case("auxiliary-display-cannot-acquire-a-hide-lease",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);var aux=c with{Render=new(new(new(999,3),RenderSurface.AuxiliaryTexture,2),RenderPhase.AuxiliaryActive)};Apply(h,context:aux);Check(h.Status.OwnedTargets==0);});
        Case("first-person-restores-immediately",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Apply(h);Apply(h,context:c with{LocalBodyViewActive=false});Check(h.Status.OwnedTargets==0);});
        Case("off-main-hiding-and-restoring-do-not-touch-ui",()=>{var p=new OriginalTestPort();var h=new HudOriginalHud(p);Task.Run(()=>Apply(h)).Wait();Check(p.Calls==0);Apply(h);Task.Run(()=>h.RestoreAll()).Wait();Check(h.Status.OwnedTargets==5);h.RestoreAll();});
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine(r.Name+": "+r.Error);Console.WriteLine($"Replacement coverage/ownership: {results.Count}; passed={passed}");return passed?0:1;
    }
}
