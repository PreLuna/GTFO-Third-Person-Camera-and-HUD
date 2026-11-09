using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class DivisionHudTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool value,string message="assertion failed"){if(!value)throw new Exception(message);}
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static HudPanelOptions Options=>HudPanelOptions.Default with{Layout=HudPanelLayoutOptions.Default with{Style=HudLayoutStyle.DivisionV3,Expanded=true},Theme=HudPanelTheme.DivisionV3,
        Typography=HudPanelTypography.Default with{LabelSize=11,BodySize=14,NumberSize=38}};
    static HudPanelPlacement Place(HudPanelOptions? options=null,ThirdPersonDisplayFrame? display=null)
    {Check(HudPanelProjection.TryPlace(display??PanelTestData.Display(PanelTestData.Context()),(options??Options).Layout,1,out var p));return p.Panel;}
    static HudPanelView Map(HudContentSnapshot? snapshot=null,HudEquipmentSlot selected=HudEquipmentSlot.Primary)
    {
        var s=snapshot??PersonalTestData.Snapshot(Current,selected);
        return HudPersonalMapper.Map(Current,s,1,1,HudContentPolicy.Default,HudPersonalOptions.Default,true);
    }
    static readonly HudRuntimeContext Current=PanelTestData.Context();
    static HudDrawCommand[] Draw(HudPanelView? view=null,HudPanelOptions? options=null)
    {
        var o=options??Options;var list=new HudDrawList();HudPanelDrawing.Build(Place(o),o,new(HudVisualState.Exploration,0,true,false),view??Map(),list);
        return Enumerable.Range(0,list.Count).Select(i=>list[i]).ToArray();
    }
    static int Main(string[] args)
    {
        Case("shoulder-reference-860-686",()=>{var p=Place(Options with{Layout=Options.Layout with{Division=HudDivisionLayout.Default with{Anchor=HudDivisionAnchor.Shoulder,FollowBody=false}}});Check(Math.Abs(p.Bounds.X-860)<.01&&Math.Abs(1080-p.Bounds.Y-p.Bounds.Height-686)<.01);});
        Case("centered-reference-960-686",()=>{var p=Place(Options with{Layout=Options.Layout with{Division=HudDivisionLayout.Default with{Anchor=HudDivisionAnchor.Centered,FollowBody=false}}});Check(Math.Abs(p.Bounds.X-960)<.01);});
        Case("auto-observes-body-projection",()=>{var d=PanelTestData.Display(Current);var m=d.Camera.WorldToView;m.M41=0;d=d with{Camera=d.Camera with{WorldToView=m}};Check(Place(display:d).Bounds.X==960&&Place().Bounds.X<960);});
        Case("mirror-right-side-composition",()=>{var d=PanelTestData.Display(Current);var m=d.Camera.WorldToView;m.M41=.48f;d=d with{Camera=d.Camera with{WorldToView=m}};var left=Place();var right=Place(display:d);Check(Math.Abs(left.Bounds.X+right.Bounds.X+right.Bounds.Width-1920)<.01);});
        foreach(var v in new[]{new PixelViewport(0,0,1280,720),new(0,0,3440,1440),new(230,90,960,600)})
            Case("viewport-origin-and-fit-"+v.Width,()=>{var d=PanelTestData.Display(Current);d=d with{Camera=d.Camera with{Viewport=v}};var p=Place(display:d);Check(p.Bounds.X>=v.X&&p.Bounds.Y>=v.Y&&p.Bounds.X+p.Bounds.Width<=v.X+v.Width&&p.Bounds.Y+p.Bounds.Height<=v.Y+v.Height&&p.Viewport==v);});
        Case("crosshair-safe-when-manually-offset",()=>{var p=Place(Options with{Layout=Options.Layout with{Division=HudDivisionLayout.Default with{Top=480}}});Check(p.Bounds.Y+p.Bounds.Height<=540-72||p.Bounds.Y>=540+72);});
        Case("behind-camera-rejected",()=>{var d=PanelTestData.Display(Current);d=d with{BodyPose=d.BodyPose with{Position=new(0,0,5)}};Check(!HudPanelProjection.TryPlace(d,Options.Layout,1,out _));});
        Case("malformed-options-sanitized",()=>{var d=(HudDivisionLayout.Default with{Top=float.NaN,Width=float.NaN,Scale=float.NaN,HealthSegments=100}).Sanitized();Check(d.Top==686&&d.Width==252&&d.Scale==1&&d.HealthSegments==20);});
        Case("thick-health-segments-with-partial-current-fill",()=>{var c=Draw();Check(c.Count(c=>c.Region=="v3-health-track")==20&&c.Count(c=>c.Region=="v3-health-fill")==17&&c.Last(c=>c.Region=="v3-health-fill").Rect.Width<c.First(c=>c.Region=="v3-health-fill").Rect.Width);});
        Case("overcap-preserves-text-and-clamps-only-bar",()=>{var c=Draw(Map(PersonalTestData.Snapshot(Current,hp:1.4)));Check(c.Any(c=>c.Text=="140%")&&c.Count(c=>c.Region=="v3-health-fill")==20);});
        Case("zero-health-and-ammo-retained",()=>{var s=PersonalTestData.Snapshot(Current,hp:0);s=s with{Personal=s.Personal with{Primary=PersonalTestData.Gear(PersonalTestData.Request(Current),HudEquipmentSlot.Primary,"空枪",700,0,0)}};var c=Draw(Map(s));Check(c.Any(c=>c.Region=="v3-health-value"&&c.Text=="0%")&&c.Any(c=>c.Region=="v3-focus-value"&&c.Text=="0")&&!c.Any(c=>c.Region=="v3-health-fill"));});
        Case("absent-heavy-and-carry-have-no-placeholder",()=>{var c=Draw();Check(c.Count(c=>c.Region=="v3-item-tile")==2&&!c.Any(c=>c.Text=="重型"||c.Text=="携带"));});
        Case("unknown-item-is-hidden",()=>{var s=PersonalTestData.Snapshot(Current);s=s with{Personal=s.Personal with{Consumable=PersonalTestData.Missing(PersonalTestData.Request(Current),HudEquipmentSlot.Consumable,HudValueState.Unreadable)}};Check(Draw(Map(s)).Count(c=>c.Region=="v3-item-tile")==1);});
        Case("occupied-zero-item-retained",()=>{var s=PersonalTestData.Snapshot(Current);s=s with{Personal=s.Personal with{Consumable=PersonalTestData.Gear(PersonalTestData.Request(Current),HudEquipmentSlot.Consumable,"荧光棒",704,count:0)}};Check(Draw(Map(s)).Any(c=>c.Region=="v3-item-value"&&c.Text=="0个"));});
        Case("selected-item-not-repeated",()=>{var c=Draw(Map(selected:HudEquipmentSlot.ResourcePack));Check(c.Count(c=>c.Text=="医疗包")==1&&c.Count(c=>c.Region=="v3-item-tile")==1&&c.Any(c=>c.Region=="v3-focus-value"&&c.Text=="2次"));});
        Case("selected-tool-not-repeated",()=>{var c=Draw(Map(selected:HudEquipmentSlot.Tool));Check(c.Any(c=>c.Region=="v3-focus-value"&&c.Text=="46%")&&!c.Any(c=>c.Region=="v3-tool-label"));});
        Case("secondary-is-current-magazine",()=>Check(Draw(Map(selected:HudEquipmentSlot.Secondary)).Any(c=>c.Region=="v3-focus-value"&&c.Text=="8")));
        Case("carry-is-state-never-fraction-or-timer",()=>{var s=PersonalTestData.Snapshot(Current,HudEquipmentSlot.Carry);s=s with{Personal=s.Personal with{Carry=PersonalTestData.Gear(PersonalTestData.Request(Current),HudEquipmentSlot.Carry,"数据立方体",790)}};var c=Draw(Map(s));Check(c.Any(c=>c.Text=="持有中")&&c.Any(c=>c.Text=="数据立方体")&&!c.Any(c=>c.Region.Contains("carry-progress")||c.Text.Contains("62%"))&&(HudDivisionDrawing.FilterCoverage(HudOriginalGroup.All,Map(s))&HudOriginalGroup.Ammo)==0);});
        foreach(var slot in new[]{HudEquipmentSlot.Melee,HudEquipmentSlot.EmptyHands})Case("non-gun-state-"+slot,()=>{var c=Draw(Map(selected:slot));Check(!c.Any(c=>c.Region=="v3-reserve-value"));});
        Case("other-ammo-is-protected-from-long-name",()=>{var s=PersonalTestData.Snapshot(Current);s=s with{Personal=s.Personal with{Secondary=PersonalTestData.Gear(PersonalTestData.Request(Current),HudEquipmentSlot.Secondary,new string('长',100),701,8,40)}};var c=Draw(Map(s));Check(c.Single(c=>c.Region=="v3-other-ammo").Text=="8"&&c.Single(c=>c.Region=="v3-other-reserve").Text=="40");});
        Case("separate-task-scan-and-real-countdown",()=>{var d=PanelTestData.Display(Current);var s=SceneTestData.Add(PersonalTestData.Request(Current),PersonalTestData.Snapshot(Current));var scene=HudSceneMapper.Map(Current,d,s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);var c=Draw(Map(s) with{Scene=scene});Check(c.Any(c=>c.Region=="v3-mission-text")&&c.Any(c=>c.Region=="v3-scan-value"&&c.Text=="40%")&&c.Any(c=>c.Region=="v3-countdown"&&c.Text.Contains("02:18"))&&c.Any(c=>c.Text=="按键 F"));});
        Case("scan-without-timer-cannot-invent-seconds",()=>{var d=PanelTestData.Display(Current);var s=SceneTestData.Add(PersonalTestData.Request(Current),PersonalTestData.Snapshot(Current));s=s with{Scene=s.Scene with{Countdown=default}};var scene=HudSceneMapper.Map(Current,d,s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);Check(!Draw(Map(s) with{Scene=scene}).Any(c=>c.Region=="v3-countdown"||c.Text.Contains("秒")));});
        Case("unknown-data-never-uses-example-numbers",()=>Check(!Draw(HudPersonalMapper.Unavailable(HudPersonalOptions.Default,HudValueState.Unreadable)).Any(c=>c.Kind==HudDrawKind.Text&&c.Text.Any(char.IsDigit))));
        Case("no-enclosing-panel-or-synthetic-counters",()=>Check(!Draw().Any(c=>c.Region=="background"||c.Text.Contains("1/3")||c.Text.Contains("拾取+"))));
        Case("health-emphasis-does-not-alter-current-values",()=>{var c=Draw(options:Options with{Layout=Options.Layout with{Division=HudDivisionLayout.Default with{Emphasis=HudDivisionEmphasis.Health}}});Check(c.Single(c=>c.Region=="v3-health-value").FontSize>Draw().Single(c=>c.Region=="v3-health-value").FontSize&&c.Single(c=>c.Region=="v3-focus-value").Text=="32");});
        Case("maximal-module-count-and-finite-bounds",()=>{var s=SceneTestData.Snapshot(Current);var scene=HudSceneMapper.Map(Current,PanelTestData.Display(Current),s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);var team=HudTeamMapper.Map(Current,s,1,1,HudContentPolicy.Default,HudTeamOptions.Default,true);var c=Draw(Map(s) with{Scene=scene,Team=team,Prompts=new(new(HudPromptKind.Health,"生命危急",95),new(HudPromptKind.Tool,"工具不足",45),new(HudPromptKind.Hit,"受击",90),3)},Options with{Layout=Options.Layout with{Team=true,Division=HudDivisionLayout.Default with{HealthSegments=20}}});Check(c.Length<256&&c.All(c=>float.IsFinite(c.Rect.X)&&float.IsFinite(c.Rect.Y)&&c.Rect.Width>=0&&c.Rect.Height>=0));});
        Case("life-uses-small-text-without-orange-life-card",()=>{var c=Draw();Check(!c.Any(c=>c.Region=="v3-health-tile")&&c.Single(c=>c.Region=="v3-health-value").FontSize<c.Single(c=>c.Region=="v3-focus-value").FontSize&&c.First(c=>c.Region=="v3-health-track").Rect.Height==11);});
        Case("melee-state-shrinks-without-ammo-placeholder",()=>{var c=Draw(Map(selected:HudEquipmentSlot.Melee));Check(!c.Any(c=>c.Region=="v3-reserve-value"||c.Region=="v3-other-ammo")&&c.Any(c=>c.Region=="v3-icon-Melee")&&c.Single(c=>c.Region=="v3-focus-value").FontSize<20);});
        Case("zero-infection-does-not-leave-a-quiet-label",()=>Check(!Draw(Map(PersonalTestData.Snapshot(Current,infection:0))).Any(c=>c.Region.StartsWith("v3-infection"))));
        Case("plane-transforms-entire-core-but-not-scene",()=>{var s=SceneTestData.Snapshot(Current);var scene=HudSceneMapper.Map(Current,PanelTestData.Display(Current),s,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);var c=Draw(Map(s) with{Scene=scene});Check(c.Where(c=>c.Region.StartsWith("v3-health")||c.Region.StartsWith("v3-icon")||c.Region=="v3-focus-value").All(c=>c.CorePlane)&&c.Where(c=>c.Region=="v3-mission-text"||c.Region.StartsWith("v3-scan")).All(c=>!c.CorePlane));});
        Case("ascii-numbers-condensed-and-chinese-units-kept",()=>{Check(Draw().Single(c=>c.Region=="v3-focus-value").NumberWidth==.82f&&Draw(Map(selected:HudEquipmentSlot.ResourcePack)).Single(c=>c.Region=="v3-focus-value").NumberWidth==1);});
        foreach(float angle in new[]{-4f,0,4f})Case("plane-corners-stay-in-safe-bounds-"+angle,()=>{var p=HudPlaneTransform.Create(252,154,angle,8);foreach(var point in new[]{Vector2.Zero,new(252,0),new(0,154),new(252,154)}){var q=p.Apply(point);Check(q.X>=-.001&&q.Y>=-.001&&q.X<=252.001&&q.Y<=154.001);}});
        Case("projected-waist-follows-camera-pitch",()=>{var d=PanelTestData.Display(Current);var a=Place(display:d);var m=d.Camera.WorldToView;m.M42=-1.8f;d=d with{Camera=d.Camera with{WorldToView=m}};var b=Place(display:d);Check(b.Bounds.Y<a.Bounds.Y&&!float.IsNaN(b.Bounds.Y));var fixedOptions=Options with{Layout=Options.Layout with{Division=HudDivisionLayout.Default with{FollowBody=false}}};Check(Place(fixedOptions,d).Bounds.Y==Place(fixedOptions).Bounds.Y);});
        Case("follow-motion-is-bounded-and-identity-reset-is-immediate",()=>{var d=PanelTestData.Display(Current);var motion=new HudPanelMotion();var anchor=new PreLuna.ThirdPersonHUD.Core.HudAnchorSpec(Vector3.Zero,0,12){Panel=Options.Layout};Check(motion.TryPlace(d,anchor,out var a));var m=d.Camera.WorldToView;m.M42=-1.8f;d=d with{CapturedAtSeconds=d.CapturedAtSeconds+.02,Camera=d.Camera with{WorldToView=m}};Check(motion.TryPlace(d,anchor,out var b));var target=Place(display:d);Check(b.Panel.Bounds.Y>a.Panel.Bounds.Y-1000&&b.Panel.Bounds.Y>target.Bounds.Y&&b.Panel.Bounds.Y<a.Panel.Bounds.Y&&HudDivisionProjection.Safe(b.Panel.Bounds,d.Camera.Viewport,Options.Layout,b.Panel.Scale));d=d with{View=d.View with{EligibilityEpoch=d.View.EligibilityEpoch+1}};Check(motion.TryPlace(d,anchor,out var reset)&&reset.Panel.Bounds.Y==target.Bounds.Y);});
        // Offline renderer consumes exactly these production drawing commands, never a separate design mock.
        var previews=new List<object>();
        foreach(var slot in new[]{HudEquipmentSlot.Primary,HudEquipmentSlot.Tool,HudEquipmentSlot.Carry,HudEquipmentSlot.Melee})
        {
            var snap=PersonalTestData.Snapshot(Current,slot);if(slot==HudEquipmentSlot.Carry)snap=snap with{Personal=snap.Personal with{Carry=PersonalTestData.Gear(PersonalTestData.Request(Current),slot,"数据立方体",790)}};
            var scene=HudSceneMapper.Map(Current,PanelTestData.Display(Current),SceneTestData.Add(PersonalTestData.Request(Current),snap),1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);
            previews.Add(new{Slot=slot.ToString(),Panel=Place(),Commands=Draw(Map(snap) with{Scene=scene})});
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]),"DivisionHudPreview.json"),JsonSerializer.Serialize(previews));
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,GameplayTested=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Division HUD layout/content: {results.Count}; passed={passed}");return passed?0:1;
    }
}
