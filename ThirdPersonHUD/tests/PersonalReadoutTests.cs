#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class PersonalReadoutTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
    sealed class Fixture
    {
        public HudRuntimeContext Current=PanelTestData.Context();
        public HudContentSnapshot Snapshot;
        public HudPersonalOptions Options=HudPersonalOptions.Default;
        public HudContentPolicy Policy=HudContentPolicy.Default;
        public bool Expanded;
        public Fixture(){Snapshot=PersonalTestData.Snapshot(Current);}
        public HudPanelView Map(long epoch=1,long revision=1)=>HudPersonalMapper.Map(Current,Snapshot,epoch,revision,Policy,Options,Expanded);
        public void Select(HudEquipmentSlot slot){Snapshot=Snapshot with{Personal=Snapshot.Personal with{Selected=PersonalTestData.Value(new HudSelection(slot),Snapshot.Personal.Selected.Stamp)}};}
        public void Next(double hp=.82,double infection=.06,double seconds=.1)
        {Current=Current with{NowSeconds=Current.NowSeconds+seconds,FrameNumber=Current.FrameNumber+1};Snapshot=PersonalTestData.Snapshot(Current,hp:hp,infection:infection,sequence:Snapshot.Header.Origin.Input.Sequence+1);}
        public HudPanelView Feedback(HudPersonalFeedback controller)=>controller.Apply(Current,Snapshot.Header,Map(),Options,Policy);
        public HudDrawCommand[] Draw(HudPanelView? view=null)
        {
            var options=HudPanelOptions.Default with{Layout=HudPanelLayoutOptions.Default with{Expanded=Expanded}};
            Check(HudPanelProjection.TryPlace(PanelTestData.Display(Current),options.Layout,1,out var placed));
            var list=new HudDrawList();HudPanelDrawing.Build(placed.Panel,options,new(HudVisualState.Exploration,0,Expanded,false),view??Map(),list);
            return Enumerable.Range(0,list.Count).Select(i=>list[i]).ToArray();
        }
    }
    static void Case(string name,Action<Fixture> action){try{action(new());results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        Case("real-health-infection-and-ammo-map-to-different-units",f=>{var v=f.Map();Check(v.Health=="82%"&&v.Infection=="6%"&&v.Magazine=="32"&&v.Reserve=="180"&&v.Supply=="74%");});
        Case("known-zero-health-is-not-missing",f=>{f.Snapshot=PersonalTestData.Snapshot(f.Current,hp:0);var v=f.Map();Check(v.Health=="0%"&&v.HealthFraction==0&&v.Personal.Health.Available);});
        Case("tiny-positive-health-is-not-rounded-to-zero",f=>{f.Snapshot=PersonalTestData.Snapshot(f.Current,hp:.000001);Check(f.Map().Health=="<1%");});
        Case("percent-precision-is-configurable",f=>{f.Options=f.Options with{PercentDecimals=1};f.Snapshot=PersonalTestData.Snapshot(f.Current,hp:.8234);Check(f.Map().Health=="82.3%");});
        Case("overcap-health-and-supply-retain-their-readings",f=>{var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Health=PersonalTestData.Value(new HudQuantity(1.4,HudUnit.Fraction),f.Snapshot.Personal.Health.Stamp),Primary=PersonalTestData.Gear(r,HudEquipmentSlot.Primary,"增强步枪",700,32,180,1.6)}};var v=f.Map();Check(v.Health=="140%"&&v.HealthFraction==1&&v.Supply=="160%");});
        Case("zero-infection-is-quiet-until-expanded",f=>{f.Snapshot=PersonalTestData.Snapshot(f.Current,infection:0);Check(f.Map().Infection=="0%"&&!f.Map().Personal.ShowInfection);f.Expanded=true;Check(f.Map().Personal.ShowInfection);});
        Case("out-of-range-infection-is-rejected-independently",f=>{f.Snapshot=PersonalTestData.Snapshot(f.Current,infection:1.1);var v=f.Map();Check(v.Infection=="—"&&v.Personal.Infection.State==HudValueState.Invalid&&v.Health=="82%"&&v.Magazine=="32");});
        Case("wrong-player-health-does-not-poison-valid-equipment",f=>{var h=f.Snapshot.Personal.Health;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Health=PersonalTestData.Value(new HudQuantity(.1,HudUnit.Fraction),h.Stamp with{Source=new(999,99)})}};Check(f.Map().Health=="—"&&f.Map().Magazine=="32");});
        Case("expired-field-has-no-fill-or-current-number",f=>{f.Current=f.Current with{NowSeconds=10.3,FrameNumber=101};var v=f.Map();Check(v.Health=="—"&&v.HealthFraction==null&&v.Personal.Health.State==HudValueState.Expired);Check(!f.Draw(v).Any(c=>c.Region=="health-fill"));});
        Case("new-header-does-not-renew-old-health-field",f=>{var old=f.Snapshot.Personal.Health;f.Next(seconds:.3);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Health=old}};Check(f.Map().Personal.Health.State==HudValueState.Expired);});
        Case("whole-snapshot-expiry-is-marked",f=>{f.Current=f.Current with{NowSeconds=10.6,FrameNumber=101};Check(f.Map().Personal.Health.State==HudValueState.Expired);});
        Case("wrong-work-or-content-version-rejects-all-values",f=>{Check(f.Map(epoch:2).Health=="—"&&f.Map(revision:2).Magazine=="—"&&f.Map(epoch:2).Personal.Health.State==HudValueState.Invalid);});
        Case("rebound-character-rejects-old-content",f=>{var b=f.Current.ExpectedBinding with{CharacterModel=new(909,90)};f.Current=f.Current with{ExpectedBinding=b,CurrentView=f.Current.CurrentView with{Binding=b}};Check(f.Map().Health=="—"&&f.Map().Magazine=="—");});
        Case("secondary-selected-becomes-main-with-primary-summary",f=>{f.Select(HudEquipmentSlot.Secondary);var v=f.Map();Check(v.Weapon=="左轮手枪"&&v.Magazine=="8"&&v.Reserve=="40"&&v.Personal.OtherWeaponLabel=="主武器"&&v.Personal.OtherWeaponValue=="32 / 180");});
        Case("valid-empty-magazine-and-empty-reserve-stay-zero",f=>{var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=PersonalTestData.Gear(r,HudEquipmentSlot.Primary,"空枪",700,0,0,0)}};var v=f.Map();Check(v.Magazine=="0"&&v.Reserve=="0"&&v.Supply=="0%");});
        Case("reserve-failure-keeps-known-magazine",f=>{var g=f.Snapshot.Personal.Primary;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=g with{ReserveRounds=HudValue<HudQuantity>.Missing(HudValueState.Unreadable,g.ReserveRounds.Stamp)}}};Check(f.Map().Magazine=="32"&&f.Map().Reserve=="—"&&f.Map().Personal.FocusStatus=="不可读");});
        Case("unreadable-name-keeps-valid-counts",f=>{var g=f.Snapshot.Personal.Primary;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=g with{Name=HudValue<HudText>.Missing(HudValueState.Unreadable,g.Name.Stamp)}}};Check(f.Map().Magazine=="32"&&f.Map().Weapon.Contains("不可读"));});
        Case("unsupported-ammo-never-becomes-zero",f=>{var g=f.Snapshot.Personal.Primary;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=g with{Magazine=HudValue<HudQuantity>.Missing(HudValueState.Unsupported,g.Magazine.Stamp)}}};Check(f.Map().Magazine=="不支持");});
        Case("old-weapon-field-cannot-attach-to-new-identity",f=>{var g=f.Snapshot.Personal.Primary;var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=PersonalTestData.Gear(r,HudEquipmentSlot.Primary,"新枪",799,12,60) with{Magazine=g.Magazine}}};Check(f.Map().Weapon=="新枪"&&f.Map().Magazine=="—");});
        Case("wrong-slot-identity-is-rejected",f=>{var g=f.Snapshot.Personal.Primary;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=g with{Identity=PersonalTestData.Value(new HudEquipmentIdentity(g.Identity.Stamp.Source,HudEquipmentSlot.Secondary),g.Identity.Stamp)}}};Check(f.Map().Magazine=="—");});
        Case("selection-unreadable-does-not-pretend-primary-is-current",f=>{f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Selected=HudValue<HudSelection>.Missing(HudValueState.Unreadable,f.Snapshot.Personal.Selected.Stamp)}};Check(f.Map().Magazine=="—"&&f.Map().Weapon.Contains("不可读")&&f.Map().Personal.Primary.Value=="32 / 180");});
        Case("missing-primary-is-distinct-from-zero",f=>{f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=PersonalTestData.Missing(PersonalTestData.Request(f.Current),HudEquipmentSlot.Primary)}};Check(f.Map().Magazine=="未装备"&&f.Map().Weapon.Contains("未装备"));});
        Case("tool-selection-uses-resource-and-deployment-units",f=>{f.Select(HudEquipmentSlot.Tool);var v=f.Map();Check(v.Weapon=="自动炮台"&&v.Magazine=="46%"&&v.Reserve=="携带中"&&v.Personal.MainLabel=="工具资源");});
        Case("deployed-sentry-is-still-equipped",f=>{f.Select(HudEquipmentSlot.Tool);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Tool=PersonalTestData.Gear(PersonalTestData.Request(f.Current),HudEquipmentSlot.Tool,"炮台",702,supply:.31,deployed:true)}};Check(f.Map().Magazine=="31%"&&f.Map().Reserve=="已部署");});
        Case("missing-deployed-object-keeps-reliable-deployment-only",f=>{f.Select(HudEquipmentSlot.Tool);var r=PersonalTestData.Request(f.Current);var g=PersonalTestData.Missing(r,HudEquipmentSlot.Tool,HudValueState.Unreadable) with{Deployment=PersonalTestData.Value(new HudDeployment(HudDeploymentKind.Deployed),HudValueStamp.AtCapture(r,r.Origin.View.Binding.LocalPlayer.Agent))};f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Tool=g}};Check(f.Map().Magazine=="—"&&f.Map().Reserve=="已部署"&&f.Map().Health=="82%");});
        Case("tool-with-no-resource-concept-has-no-false-empty-bar",f=>{var g=f.Snapshot.Personal.Tool;f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Tool=g with{SupplyRatio=HudValue<HudQuantity>.Missing(HudValueState.Unsupported,g.SupplyRatio.Stamp)}}};Check(f.Map().Tool=="不支持"&&f.Map().ToolFraction==null);});
        Case("resource-pack-shows-uses-not-items",f=>{f.Select(HudEquipmentSlot.ResourcePack);Check(f.Map().Magazine=="2次"&&f.Map().Personal.MainLabel=="使用次数");});
        Case("consumables-show-integer-items",f=>{f.Select(HudEquipmentSlot.Consumable);Check(f.Map().Magazine=="3个"&&f.Map().Personal.MainLabel=="物品数量");});
        Case("heavy-item-gets-its-own-current-identity",f=>{f.Select(HudEquipmentSlot.Heavy);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Heavy=PersonalTestData.Gear(PersonalTestData.Request(f.Current),HudEquipmentSlot.Heavy,"重型物品",790,count:5)}};Check(f.Map().Magazine=="5个"&&f.Map().Weapon=="重型物品");});
        Case("carry-object-does-not-invent-a-count-of-one",f=>{f.Select(HudEquipmentSlot.Carry);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Carry=PersonalTestData.Gear(PersonalTestData.Request(f.Current),HudEquipmentSlot.Carry,"任务电池",791)}};var v=f.Map();Check(v.Weapon=="任务电池"&&v.Magazine=="搬运中"&&v.Reserve==""&&v.Personal.ItemValue=="搬运中");});
        Case("melee-clears-gun-ammo-labels-and-values",f=>{f.Select(HudEquipmentSlot.Melee);var v=f.Map();Check(v.Weapon=="重锤"&&v.Magazine=="近战"&&v.Reserve==""&&v.Personal.MainLabel!="弹匣");});
        Case("empty-hands-do-not-reuse-last-gun",f=>{f.Select(HudEquipmentSlot.EmptyHands);Check(f.Map().Weapon=="空手"&&f.Map().Magazine=="空手"&&f.Map().Reserve=="");});
        Case("withdrawn-item-is-not-retained-in-summary",f=>{f.Select(HudEquipmentSlot.ResourcePack);Check(f.Map().Item.Contains("医疗包"));var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{ResourcePack=PersonalTestData.Missing(r,HudEquipmentSlot.ResourcePack),Consumable=PersonalTestData.Missing(r,HudEquipmentSlot.Consumable)}};Check(!f.Map().Item.Contains("医疗包")&&f.Map().Personal.Pack.Value=="未携带");});
        Case("unreadable-backpack-is-not-an-empty-backpack",f=>{var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{ResourcePack=PersonalTestData.Missing(r,HudEquipmentSlot.ResourcePack,HudValueState.Unreadable)}};Check(f.Map().Personal.Pack.Value=="不可读"&&f.Map().Health=="82%");});
        Case("automatic-details-require-valid-selection",f=>{f.Select(HudEquipmentSlot.Tool);Check(HudPersonalMapper.WantsDetails(f.Current,f.Snapshot,1,1,f.Policy,f.Options));Check(!HudPersonalMapper.WantsDetails(f.Current,f.Snapshot,2,1,f.Policy,f.Options));f.Options=f.Options with{AutoExpandSelected=false};Check(!HudPersonalMapper.WantsDetails(f.Current,f.Snapshot,1,1,f.Policy,f.Options));});
        Case("fixed-primary-mode-is-explicitly-a-summary",f=>{f.Select(HudEquipmentSlot.Tool);f.Options=f.Options with{FocusSelected=false};Check(f.Map().Weapon=="突击步枪"&&f.Map().Personal.FocusLabel=="主武器摘要");});
        Case("disabled-modules-do-not-draw-their-values",f=>{f.Options=f.Options with{Health=false,Infection=false,Tools=false,Items=false};var c=f.Draw();Check(!c.Any(c=>c.Region.StartsWith("health")||c.Region.StartsWith("infection")||c.Region.StartsWith("tool")||c.Region.StartsWith("item")));});
        Case("disabled-personal-content-keeps-no-numerical-regions",f=>{f.Options=f.Options with{Enabled=false};Check(!f.Draw().Any(c=>c.Kind==HudDrawKind.Text&&c.Text.Any(char.IsDigit)));});
        Case("supply-toggle-does-not-remove-tool-resource",f=>{f.Options=f.Options with{SupplyRatio=false};Check(f.Map().Supply==""&&f.Map().Tool=="46%");});
        Case("maximum-ammo-integers-are-never-ellipsized",f=>{var r=PersonalTestData.Request(f.Current);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=PersonalTestData.Gear(r,HudEquipmentSlot.Primary,"大弹匣",700,int.MaxValue,int.MaxValue)}};var c=f.Draw();Check(c.Single(c=>c.Region=="magazine").Text==int.MaxValue.ToString()&&c.Single(c=>c.Region=="reserve").Text==int.MaxValue.ToString());});
        Case("long-item-name-does-not-consume-the-count-region",f=>{f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{ResourcePack=PersonalTestData.Gear(PersonalTestData.Request(f.Current),HudEquipmentSlot.ResourcePack,new string('长',100),703,count:12)}};var c=f.Draw();Check(c.Single(c=>c.Region=="item").Text=="12次"&&c.Single(c=>c.Region=="item-name").Text.EndsWith("…"));});
        Case("expanded-details-cover-all-personal-slots",f=>{f.Expanded=true;var c=f.Draw();foreach(string role in new[]{"primary","secondary","tool","pack","consumable","heavy","carry"})Check(c.Any(c=>c.Region=="detail-"+role+"-value"),role);});
        Case("personal-drawing-remains-bounded-inside-panel",f=>{f.Expanded=true;var c=f.Draw();Check(c.Length<128);foreach(var command in c)Check(command.Rect.X>=0&&command.Rect.Y>=0&&command.Rect.X+command.Rect.Width<=304.01f&&command.Rect.Y+command.Rect.Height<=HudPanelProjection.ExpandedHeight+.01f,command.Region);});
        Case("first-sample-has-no-fabricated-change",f=>{Check(string.IsNullOrEmpty(f.Feedback(new()).Personal.HealthChange));});
        Case("current-health-bar-is-distinct-from-historical-change",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.62);var commands=f.Draw(f.Feedback(feedback));var fill=commands.Single(c=>c.Region=="health-fill");var trace=commands.Single(c=>c.Region=="health-change-fill");Check(Math.Abs(fill.Rect.Width-276*.62)<.01&&trace.Rect.Y>fill.Rect.Y+fill.Rect.Height);});
        Case("withdrawn-weapon-clears-obsolete-ammo-labels",f=>{f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Primary=PersonalTestData.Missing(PersonalTestData.Request(f.Current),HudEquipmentSlot.Primary)}};Check(f.Map().Personal.MainLabel=="装备状态"&&f.Map().Personal.ReserveLabel==""&&f.Map().Reserve=="");});
        Case("heavy-item-category-label-remains-complete",f=>{f.Select(HudEquipmentSlot.Heavy);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Heavy=PersonalTestData.Gear(PersonalTestData.Request(f.Current),HudEquipmentSlot.Heavy,"重型物品",790,count:2)}};Check(f.Draw().Single(c=>c.Region=="item-label").Text=="重型物品");});
        Case("health-loss-feedback-keeps-current-number",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.62);var v=f.Feedback(feedback);Check(v.Health=="62%"&&v.Personal.HealthChange=="−20%"&&v.Personal.PreviousHealthFraction==.82f);});
        Case("healing-feedback-is-positive",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.9);Check(f.Feedback(feedback).Personal.HealthChange=="+8%");});
        Case("infection-change-to-zero-remains-temporarily-visible",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(infection:0);var v=f.Feedback(feedback);Check(v.Infection=="0%"&&v.Personal.InfectionChange=="−6%"&&v.Personal.ShowInfection);});
        Case("repaints-and-unchanged-samples-do-not-renew-feedback",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.62);f.Feedback(feedback);for(int i=0;i<12;i++){f.Next(hp:.62);f.Feedback(feedback);}Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange));});
        Case("missing-health-clears-feedback-before-recovery",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.62);f.Feedback(feedback);f.Snapshot=f.Snapshot with{Personal=f.Snapshot.Personal with{Health=HudValue<HudQuantity>.Missing(HudValueState.Unreadable,f.Snapshot.Personal.Health.Stamp)}};Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange));f.Next(hp:.4);Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange));});
        Case("long-gap-does-not-present-old-damage-as-new",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Next(hp:.3,seconds:1);Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange));});
        Case("recreated-character-resets-feedback",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);var context=new ContextStamp(Guid.NewGuid(),1);f.Current=f.Current with{ExpectedContext=context,CurrentView=f.Current.CurrentView with{Context=context}};f.Next(hp:.3);Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange));});
        Case("feedback-can-be-disabled-independently",f=>{var feedback=new HudPersonalFeedback();f.Feedback(feedback);f.Options=f.Options with{ValueFeedback=false};f.Next(hp:.3);Check(string.IsNullOrEmpty(f.Feedback(feedback).Personal.HealthChange)&&f.Map().Health=="30%");});
        Case("overflowing-percent-conversion-is-invalid-not-infinity",f=>{f.Snapshot=PersonalTestData.Snapshot(f.Current,hp:double.MaxValue);Check(f.Map().Health=="—"&&f.Map().HealthFraction==null);});
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,NativeGameExecuted=false,RuntimeRenderingVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Personal mapping/feedback/drawing: {results.Count}; passed={passed}");return passed?0:1;
    }
}
