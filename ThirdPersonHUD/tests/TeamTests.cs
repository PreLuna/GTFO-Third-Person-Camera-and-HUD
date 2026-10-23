using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class TeamTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool value){if(!value)throw new Exception("assertion failed");}
    static void Case(string name,Action action){try{action();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static readonly HudRuntimeContext context=PanelTestData.Context();
    static HudRuntimeContext C=>context;
    static HudTeamPresentation Map(HudContentSnapshot s,HudRuntimeContext? c=null,HudTeamOptions? options=null,bool expanded=true)
        =>HudTeamMapper.Map(c??C,s,1,1,HudContentPolicy.Default,options??HudTeamOptions.Default,expanded);
    static int Main(string[] args)
    {
        var c=C;var s=TeamTestData.Snapshot(c);var one=s.Team.Slot1;var r=PersonalTestData.Request(c);
        Case("three-other-slots-only",()=>{var m=Map(s);Check(m.Active&&m.First.Slot==1&&m.Second.Slot==2&&m.Third.Slot==3);});
        Case("human-and-bot-explicit",()=>Check(Map(s).First.Kind=="真人"&&Map(s).Second.Kind=="人机"));
        Case("known-health-and-sync-ratios",()=>{var m=Map(s);Check(m.First.Health=="75%"&&m.First.Primary=="60%"&&m.First.Infection=="10%");});
        Case("zero-health-is-known",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,hp:0))).First.Health=="0%"));
        Case("empty-slot-is-not-offline",()=>{var empty=s with{Team=s.Team with{Roster=PersonalTestData.Value(new HudTeamRoster(0,12),s.Team.Roster.Stamp)}};Check(Map(empty).First.Name=="空位"&&Map(empty).First.Health=="—");});
        Case("missing-roster-does-not-reuse-names",()=>Check(Map(s with{Team=default}).First.Status=="未读取"));
        Case("wrong-roster-local-slot-rejected",()=>Check(Map(s with{Team=s.Team with{Roster=PersonalTestData.Value(new HudTeamRoster(1,12),s.Team.Roster.Stamp)}}).First.Status=="未读取"));
        Case("wrong-peer-slot-rejected",()=>Check(Map(TeamTestData.WithPeer(s,1,one with{Slot=2})).First.Status=="异常"));
        Case("stale-member-field-revision-rejected",()=>{var newer=TeamTestData.Peer(r,1,revision:2);Check(Map(TeamTestData.WithPeer(s,1,newer with{Health=one.Health})).First.Health=="—");});
        Case("wrong-identity-revision-rejected",()=>{var id=one.Identity.RawValue with{Revision=2};Check(Map(TeamTestData.WithPeer(s,1,one with{Identity=PersonalTestData.Value(id,one.Identity.Stamp)})).First.Status=="异常");});
        Case("wrong-health-source-rejected",()=>{var wrong=PersonalTestData.Value(new HudQuantity(.99,HudUnit.Fraction),one.Health.Stamp with{Source=new(9999,9999)});Check(Map(TeamTestData.WithPeer(s,1,one with{Health=wrong})).First.Health=="—");});
        Case("wrong-generation-rejected",()=>{var wrong=PersonalTestData.Value(new HudQuantity(.99,HudUnit.Fraction),one.Health.Stamp with{Generation=one.Health.Stamp.Generation with{WorkEpoch=2}});Check(Map(TeamTestData.WithPeer(s,1,one with{Health=wrong})).First.Health=="—");});
        Case("future-field-rejected",()=>{var wrong=PersonalTestData.Value(new HudQuantity(.99,HudUnit.Fraction),one.Health.Stamp with{CapturedAtSeconds=c.NowSeconds+.1});Check(Map(TeamTestData.WithPeer(s,1,one with{Health=wrong})).First.Health=="—");});
        Case("expired-live-fields-not-renewed",()=>Check(Map(s,c with{NowSeconds=c.NowSeconds+.26}).First.Health=="—"));
        Case("wrong-work-epoch-rejected",()=>Check(Map(s with{Header=s.Header with{WorkEpoch=2}}).Coverage==0));
        Case("offline-clears-health-and-detail",()=>{var m=Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,connection:HudPeerState.Offline)));Check(m.First.Status=="离线"&&m.First.Health=="—"&&!m.First.Covered&&m.First.Primary=="未读取");});
        Case("synchronizing-clears-old-resources",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,connection:HudPeerState.Synchronizing))).First.Status=="同步中"));
        Case("unspawned-agent-cannot-display-health",()=>{var id=one.Identity.RawValue with{Agent=default,Model=default};var m=Map(TeamTestData.WithPeer(s,1,one with{Identity=PersonalTestData.Value(id,one.Identity.Stamp)}));Check(m.First.Status=="同步中"&&m.First.Health=="—");});
        Case("downed-urgent-rescue",()=>{var m=Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,life:HudLifeKind.Downed)));Check(m.First.Status=="倒地"&&m.Prompt.Priority==92&&m.First.Cue=="需要救援"&&!m.First.Covered);});
        Case("dead-has-no-resource-warning",()=>{var m=Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,life:HudLifeKind.Dead,primary:0)));Check(m.First.Status=="死亡"&&m.First.Priority==0);});
        Case("ammo-low-includes-known-zero",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,primary:0))).First.Cue=="弹药不足"));
        Case("tool-low-is-separate",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,tool:.1))).First.Cue=="工具不足"));
        Case("missing-ammo-is-not-low",()=>{var peer=one with{PrimaryRatio=default,SecondaryRatio=default,ToolRatio=default};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Priority==0);});
        Case("wrong-ratio-unit-rejected",()=>{var peer=one with{PrimaryRatio=PersonalTestData.Value(new HudQuantity(5,HudUnit.Rounds),one.PrimaryRatio.Stamp)};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Primary=="异常");});
        Case("infection-over-one-rejected",()=>{var peer=one with{Infection=PersonalTestData.Value(new HudQuantity(1.5,HudUnit.Fraction),one.Infection.Stamp)};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Infection=="异常");});
        Case("unsupported-tool-is-explicit",()=>{var peer=one with{ToolRatio=HudValue<HudQuantity>.Missing(HudValueState.Unsupported,one.ToolRatio.Stamp)};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Tool=="不支持");});
        Case("current-mark-is-a-transient-notification",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,marked:true))).First.Cue=="已标记"));
        Case("old-mark-time-is-not-refreshed",()=>{var peer=one with{Marked=PersonalTestData.Value(new HudTeamMark(1,c.NowSeconds-4),one.Health.Stamp)};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Cue=="");});
        Case("future-mark-notification-rejected",()=>{var peer=one with{Marked=PersonalTestData.Value(new HudTeamMark(1,c.NowSeconds+1),one.Health.Stamp)};Check(Map(TeamTestData.WithPeer(s,1,peer)).First.Cue=="");});
        Case("cooperation-toggle-is-independent",()=>Check(Map(TeamTestData.WithPeer(s,1,TeamTestData.Peer(r,1,life:HudLifeKind.Downed)),options:HudTeamOptions.Default with{Cooperation=false}).First.Cue==""));
        Case("details-share-expand-state",()=>Check(!Map(s,expanded:false).Details&&Map(s).Details));
        Case("details-switch-keeps-summary",()=>{var m=Map(s,options:HudTeamOptions.Default with{Details=false});Check(m.Active&&!m.Details&&m.Coverage==0);});
        Case("team-toggle-disables-model",()=>Check(!Map(s,options:HudTeamOptions.Default with{Enabled=false}).Active));
        Case("combined-personal-all-mask-is-compatible",()=>Check((int)HudOriginalGroup.All==31&&(int)HudOriginalGroup.Team==480));
        Case("only-complete-expanded-members-covered",()=>{Check(Map(s).Coverage==(HudOriginalGroup.Team1|HudOriginalGroup.Team2|HudOriginalGroup.Team3));Check(Map(s,expanded:false).Coverage==0);Check(!Map(TeamTestData.WithPeer(s,1,one with{Infection=default})).First.Covered);});
        Case("long-and-multiline-name-cannot-inject-rows",()=>{var peer=one with{Name=PersonalTestData.Value(new HudText(new string('界',100)+"\n假读数"),one.Name.Stamp)};var name=Map(TeamTestData.WithPeer(s,1,peer)).First.Name;Check(name.Length<=64&&!name.Contains('\n'));});
        Case("team-prompt-competes-with-personal-priorities",()=>{var injured=PersonalTestData.Snapshot(c,hp:.1);injured=TeamTestData.Add(r,injured);injured=TeamTestData.WithPeer(injured,1,TeamTestData.Peer(r,1,life:HudLifeKind.Downed));var view=HudPersonalMapper.Map(c,injured,1,1,HudContentPolicy.Default,HudPersonalOptions.Default,false) with{Team=Map(injured)};view=new HudAlerts().Apply(c,PanelTestData.Display(c),true,injured,1,1,HudContentPolicy.Default,view,HudAlertOptions.Default,HudPanelBehavior.Default);Check(view.Prompts.First.Priority==95&&view.Prompts.Second.Kind==HudPromptKind.Team);});
        for(int local=0;local<4;local++)
        {int index=local;Case("local-slot-"+local+"-excluded",()=>{var binding=c.ExpectedBinding with{LocalPlayer=c.ExpectedBinding.LocalPlayer with{Slot=index}};var current=c with{ExpectedBinding=binding,CurrentView=c.CurrentView with{Binding=binding,Subject=binding.LocalPlayer}};var m=Map(TeamTestData.Snapshot(current),current);Check(Enumerable.Range(0,3).All(i=>m[i].Slot!=index)&&Enumerable.Range(0,3).Select(i=>m[i].Slot).Distinct().Count()==3);});}
        foreach(bool expanded in new[]{false,true})foreach(int height in new[]{720,1080,1440,2160})
        {bool detail=expanded;int h=height;Case("bounded-team-layout-"+h+"-"+detail,()=>{var display=PanelTestData.Display(c);display=display with{Camera=display.Camera with{Viewport=new(0,0,h*16f/9,h)}};var options=HudPanelOptions.Default with{Layout=HudPanelLayoutOptions.Default with{Team=true,Expanded=detail}};Check(HudPanelProjection.TryPlace(display,options.Layout,1,out var place));var view=HudPersonalMapper.Map(c,s,1,1,HudContentPolicy.Default,HudPersonalOptions.Default,detail) with{Team=Map(s,expanded:detail)};var draw=new HudDrawList();HudPanelDrawing.Build(place.Panel,options,new(HudVisualState.Exploration,0,detail,false),view,draw);Check(draw.Count<256&&place.Panel.Bounds.Height<=h-40);Check(Enumerable.Range(0,draw.Count).Where(i=>draw[i].Region.StartsWith("team-")).All(i=>draw[i].Rect.Y+draw[i].Rect.Height<=place.Panel.Bounds.Height+1));});}
        bool passed=results.All(x=>x.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));foreach(var x in results.Where(x=>!x.Passed))Console.WriteLine("FAIL "+x.Name+": "+x.Error);Console.WriteLine($"Team rules/drawing: {results.Count}; passed={passed}");return passed?0:1;
    }
}
