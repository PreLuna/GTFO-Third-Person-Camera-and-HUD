#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;
using PreLuna.ThirdPersonHUD.Runtime;
using U=UnityEngine;

static class PersonalRuntimeTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
    sealed class Source:IHudContentSource,IHudContentRefreshSource,IHudTeamCaptureControl,IHudSceneCaptureControl
    {
        public long ChangeVersion{get;private set;}=1;
        public int Calls,Count=2;
        public double Health=.82,Infection=.06;
        public HudEquipmentSlot Selected=HudEquipmentSlot.Primary;
        public bool Fail,DropPack;
        public bool TeamEnabled;
        public void SetTeamEnabled(bool enabled)=>TeamEnabled=enabled;
        public HudSceneCollectionOptions SceneConfiguration;
        public void ConfigureScene(HudSceneCollectionOptions options)=>SceneConfiguration=options;
        public Func<HudContentSnapshot,HudContentSnapshot>? Modify;
        public void Dirty()=>ChangeVersion++;
        public bool TryCapture(in HudContentCaptureRequest r,out HudContentSnapshot s)
        {
            Calls++;s=default;if(Fail)return false;
            s=PersonalTestData.Snapshot(r,Selected,Health,Infection);
            s=s with{Personal=s.Personal with{ResourcePack=DropPack?PersonalTestData.Missing(r,HudEquipmentSlot.ResourcePack):PersonalTestData.Gear(r,HudEquipmentSlot.ResourcePack,"医疗包",703,count:Count)}};
            if(Modify!=null)s=Modify(s);return true;
        }
    }
    sealed class Fixture:IDisposable
    {
        public BridgeObservation Live;
        public readonly ThirdPersonViewProvider Provider;
        public readonly HudRuntime Runtime;
        public readonly Source Content=new();
        public readonly OriginalTestPort Original=new();
        readonly CameraSnapshot camera;
        public Fixture()
        {
            U.Object.Created.Clear();U.Object.FailAt=null;U.GUI.Labels.Clear();U.GUI.Draws=0;U.GUI.Drawing=null;U.Camera.current=null;
            U.Font.MissingGlyphs=false;U.Font.RejectedName=null;U.Input.Held=false;U.Input.RestorePressed=false;U.Event.current=new(){type=U.EventType.Repaint};
            GameStateManager.CurrentStateName=eGameStateName.InLevel;
            var c=PanelTestData.Context();camera=PanelTestData.Display(c).Camera;
            Live=new(c.ExpectedBinding,c.ExpectedBinding.LocalPlayer,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,100,10);
            Provider=new(()=>Live);ThirdPersonApi.Provider=Provider;Plugin.Instance=new();
            Plugin.Instance.Settings.Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
            Plugin.Instance.Settings.WorkOptions=new(5,3,.05,.25);Plugin.Instance.Settings.ExecutionOptions=new(HudComputationMode.MainThread,.5);
            Runtime=new(IntPtr.Zero);Runtime.Initialize(()=>ThirdPersonApi.MonotonicSeconds,contentSource:Content,originalPort:Original);
        }
        public void Update(){U.Time.frameCount=Live.FrameNumber;ThirdPersonApi.MonotonicSeconds=Live.NowSeconds;Runtime.Update();}
        public void Commit(){Provider.BeginMainAttempt();Check(Provider.PublishMain(camera,new(Vector3.Zero,Quaternion.Identity)));Provider.MainRestored(true);Provider.NativeMainCompleted(true);}
        public void Next(bool commit=true,double seconds=.02){Live=Live with{FrameNumber=Live.FrameNumber+1,NowSeconds=Live.NowSeconds+seconds};Update();if(commit)Commit();}
        public void Draw(){U.GUI.Labels.Clear();Runtime.OnGUI();}
        public bool Has(string text)=>U.GUI.Labels.Contains(text);
        public void Ready(){Update();Commit();for(int i=0;i<8&&!Status.Visible;i++)Next();Draw();Check(Has("82%")&&Has("32"),"live content did not reach formal drawing");}
        public HudLifecycleStatus Status{get{Runtime.TryGetStatus(out var s);return s;}}
        public HudPresentationDiagnostics Diagnostics{get{Runtime.TryGetPresentationDiagnostics(out var d);return d;}}
        public void Dispose(){Runtime.Stop();Provider.Dispose();ThirdPersonApi.Provider=null!;U.GUI.Drawing=null;Check(U.Object.Created.All(o=>o.Destroyed),"Unity resource leaked");}
    }
    static void Case(string name,Action<Fixture> action){try{using var f=new Fixture();action(f);results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        void Team(Fixture f){f.Content.Modify=s=>TeamTestData.Add(new(s.Header.Origin.Input,s.Header.WorkEpoch,s.Header.ContentRevision),s);}
        void Scene(Fixture f){f.Content.Modify=s=>SceneTestData.Add(new(s.Header.Origin.Input,s.Header.WorkEpoch,s.Header.ContentRevision),s);}
        Case("scene-cards-use-production-drawing",f=>{Scene(f);f.Ready();Check(f.Has("交互 · 安全门")&&f.Has("按键 F")&&f.Has("02:18")&&f.Has("42%")&&f.Original.Countdown.Value);});
        Case("scene-supply-recipient-reaches-labels",f=>{Scene(f);f.Ready();f.Content.Modify=s=>{var r=new HudContentCaptureRequest(s.Header.Origin.Input,s.Header.WorkEpoch,s.Header.ContentRevision);s=SceneTestData.Add(r,s);return s with{Scene=s.Scene with{Supply=SceneTestData.Supply(r)}};};f.Content.Dirty();f.Next();f.Draw();Check(f.Has("补给 · 医疗包")&&f.Has("接收对象 · 队员甲")&&f.Has("剩余 2次"));});
        Case("scene-cancel-clears-old-object-and-progress",f=>{Scene(f);f.Ready();f.Content.Modify=s=>s;f.Content.Dirty();f.Next();f.Draw();Check(!f.Has("交互 · 安全门")&&!f.Has("42%")&&!f.Original.Countdown.Value);});
        Case("scene-disable-stops-module-and-restores-original",f=>{Scene(f);f.Ready();Plugin.Instance.Settings.SceneOptions=HudSceneOptions.Default with{Enabled=false};f.Next();f.Draw();Check(!f.Content.SceneConfiguration.Enabled&&!f.Has("02:18")&&f.Has("82%")&&!f.Original.Countdown.Value);});
        Case("scene-countdown-replacement-toggle",f=>{Scene(f);f.Ready();Plugin.Instance.Settings.SceneOptions=HudSceneOptions.Default with{ReplaceCountdown=false};f.Next();f.Draw();Check(f.Has("02:18")&&!f.Original.Countdown.Value);});
        Case("scene-original-master-still-applies",f=>{Scene(f);f.Ready();Plugin.Instance.Settings.ReplacementOptions=HudReplacementOptions.Default with{Enabled=false};f.Next();f.Draw();Check(f.Has("02:18")&&!f.Original.Countdown.Value);});
        Case("scene-auxiliary-isolation-keeps-lease",f=>{Scene(f);f.Ready();int writes=f.Original.Countdown.Writes;f.Provider.AuxiliaryBegin(new(903,1));f.Draw();Check(U.GUI.Labels.Count==0&&f.Original.Countdown.Value&&f.Original.Countdown.Writes==writes);f.Provider.AuxiliaryEnd(new(903,1));f.Draw();Check(f.Has("02:18")&&f.Original.Countdown.Writes==writes);});
        Case("scene-first-person-clears-and-restores",f=>{Scene(f);f.Ready();f.Live=f.Live with{Mode=ThirdPersonMode.FirstPersonAim,Source=ViewSource.LocalFirstPerson,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Draw();Check(U.GUI.Labels.Count==0&&!f.Original.Countdown.Value);});
        Case("scene-stale-countdown-restores-only-that-group",f=>{Scene(f);f.Ready();Check(f.Runtime.TryGetContent(out var old));f.Content.Modify=s=>{s=SceneTestData.Add(new(s.Header.Origin.Input,s.Header.WorkEpoch,s.Header.ContentRevision),s);return s with{Scene=s.Scene with{Countdown=old.Scene.Countdown}};};f.Content.Dirty();f.Next(seconds:.2);f.Draw();Check(!f.Original.Countdown.Value&&f.Has("82%")&&!f.Has("02:18"));});
        Case("scene-draw-failure-restores-countdown",f=>{Scene(f);f.Ready();U.Object.FailAt="Label";U.Object.FailAt="DrawTexture";f.Draw();Check(!f.Original.Countdown.Value);});
        Case("scene-pause-key-restores-countdown",f=>{Scene(f);f.Ready();U.Input.RestorePressed=true;f.Next();f.Draw();Check(!f.Original.Countdown.Value&&f.Has("02:18"));});
        Case("scene-stop-restores-countdown",f=>{Scene(f);f.Ready();f.Runtime.Stop();Check(!f.Original.Countdown.Value);});
        Case("team-summary-reaches-production-labels",f=>{Team(f);f.Ready();Check(f.Has("队员一")&&f.Has("队员二")&&f.Has("队员三")&&f.Has("人机")&&f.Has("75%"));Check(!f.Has("主武器 60%"));});
        Case("team-details-share-existing-key",f=>{Team(f);f.Ready();U.Input.Held=true;f.Next();f.Draw();Check(f.Has("主武器 60%")&&f.Has("副武器 40%"));Check(!f.Original.TeamTargets[0].Value&&f.Original.TeamTargets.Skip(1).All(t=>t.Value));});
        Case("team-readout-only-in-third-person",f=>{Team(f);f.Ready();f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Mode=ThirdPersonMode.FirstPersonAim,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Draw();Check(U.GUI.Labels.Count==0&&f.Original.TeamTargets.All(t=>!t.Value));});
        Case("team-control-source-isolated",f=>{Team(f);f.Ready();f.Live=f.Live with{Source=ViewSource.BotControl,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Draw();Check(!f.Has("队员一")&&U.GUI.Labels.Count==0);});
        Case("team-auxiliary-preview-does-not-toggle-original",f=>{Team(f);U.Input.Held=true;f.Ready();int writes=f.Original.TeamTargets[1].Writes;f.Provider.AuxiliaryBegin(new(901,1));f.Draw();Check(U.GUI.Labels.Count==0&&f.Original.TeamTargets[1].Value&&f.Original.TeamTargets[1].Writes==writes);f.Provider.AuxiliaryEnd(new(901,1));f.Draw();Check(f.Has("队员一")&&f.Original.TeamTargets[1].Writes==writes);});
        Case("team-disable-restores-and-stops-collection-module",f=>{Team(f);U.Input.Held=true;f.Ready();Plugin.Instance.Settings.TeamOptions=HudTeamOptions.Default with{Enabled=false};f.Next();f.Draw();Check(!f.Content.TeamEnabled&&!f.Has("队员一")&&f.Has("82%")&&f.Original.TeamTargets.All(t=>!t.Value));});
        Case("team-replacement-switch-preserves-new-summary",f=>{Team(f);U.Input.Held=true;f.Ready();Plugin.Instance.Settings.TeamOptions=HudTeamOptions.Default with{ReplaceOriginal=false};f.Next();f.Draw();Check(f.Has("队员一")&&f.Original.TeamTargets.All(t=>!t.Value));});
        Case("team-only-one-expired-member-restores",f=>{Team(f);U.Input.Held=true;f.Ready();Check(f.Runtime.TryGetContent(out var old));f.Content.Modify=s=>{s=TeamTestData.Add(new(s.Header.Origin.Input,s.Header.WorkEpoch,s.Header.ContentRevision),s);return TeamTestData.WithPeer(s,1,s.Team.Slot1 with{Health=old.Team.Slot1.Health});};f.Content.Dirty();f.Next(seconds:.3);f.Draw();Check(!f.Original.TeamTargets[1].Value&&f.Original.TeamTargets[2].Value&&f.Original.TeamTargets[3].Value);});
        Case("team-failure-restores-original-and-clears-names",f=>{Team(f);U.Input.Held=true;f.Ready();f.Content.Fail=true;f.Content.Dirty();f.Next();f.Draw();Check(!f.Has("队员一")&&f.Original.TeamTargets.All(t=>!t.Value));});
        Case("team-restore-key-holds-personal-and-team",f=>{Team(f);U.Input.Held=true;f.Ready();U.Input.RestorePressed=true;f.Next();f.Draw();Check(f.Original.TeamTargets.All(t=>!t.Value)&&f.Original.Targets.All(t=>!t.Value)&&f.Has("队员一"));});
        Case("team-stop-releases-owned-targets",f=>{Team(f);U.Input.Held=true;f.Ready();f.Runtime.Stop();Check(f.Original.TeamTargets.All(t=>!t.Value));});
        Case("accepted-personal-values-reach-production-labels",f=>{f.Ready();Check(f.Has("6%")&&f.Has("180")&&f.Has("46%")&&f.Has("2次")&&f.Has("8 / 40"));});
        Case("secondary-switch-replaces-main-ammo-and-label",f=>{f.Ready();f.Content.Selected=HudEquipmentSlot.Secondary;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("8")&&f.Has("40")&&f.Has("左轮手枪")&&!f.Has("32"));});
        Case("tool-selection-focuses-resource-and-automatically-expands",f=>{f.Ready();f.Content.Selected=HudEquipmentSlot.Tool;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("工具资源")&&f.Has("46%")&&!f.Has("32"));f.Next();f.Draw();Check(f.Diagnostics.LastDraw.Panel.Expanded&&f.Has("3个"));});
        Case("automatic-expansion-can-be-disabled",f=>{f.Ready();Plugin.Instance.Settings.PersonalOptions=HudPersonalOptions.Default with{AutoExpandSelected=false};f.Content.Selected=HudEquipmentSlot.Tool;f.Content.Dirty();f.Next();f.Next();f.Draw();Check(!f.Diagnostics.LastDraw.Panel.Expanded&&f.Has("工具资源"));});
        Case("pack-zero-is-shown-as-zero-uses",f=>{f.Ready();f.Content.Selected=HudEquipmentSlot.ResourcePack;f.Content.Count=0;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("0次")&&f.Has("使用次数")&&!f.Has("2次"));});
        Case("consumable-selection-is-counted-in-items",f=>{f.Ready();f.Content.Selected=HudEquipmentSlot.Consumable;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("3个")&&f.Has("物品数量")&&!f.Has("32"));});
        Case("dropping-pack-clears-old-name-and-count",f=>{f.Ready();f.Content.Selected=HudEquipmentSlot.EmptyHands;f.Content.DropPack=true;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("空手")&&!f.Has("医疗包")&&!f.Has("2次"));});
        Case("source-failure-clears-visible-values-without-destroying-panel",f=>{f.Ready();f.Content.Fail=true;f.Content.Dirty();f.Next();f.Draw();Check(!f.Has("82%")&&!f.Has("32")&&f.Has("—")&&f.Diagnostics.LiveOwnedReferences==2);});
        Case("same-frame-dirty-signal-invalidates-old-visible-values",f=>{f.Ready();int calls=f.Content.Calls;f.Content.Dirty();f.Draw();Check(!f.Has("32")&&!f.Has("82%")&&f.Has("—")&&f.Content.Calls==calls);});
        Case("old-health-field-does-not-hide-new-weapon-values",f=>{f.Ready();Check(f.Runtime.TryGetContent(out var old));f.Content.Modify=s=>s with{Personal=s.Personal with{Health=old.Personal.Health}};f.Content.Dirty();f.Next(seconds:.3);f.Draw();Check(!f.Has("82%")&&f.Has("已过期")&&f.Has("32"));});
        Case("tool-failure-is-independent-of-health-and-ammo",f=>{f.Ready();f.Content.Modify=s=>s with{Personal=s.Personal with{Tool=s.Personal.Tool with{SupplyRatio=HudValue<HudQuantity>.Missing(HudValueState.Unreadable,s.Personal.Tool.SupplyRatio.Stamp)}}};f.Content.Dirty();f.Next();f.Draw();Check(f.Has("82%")&&f.Has("32")&&!f.Has("46%"));});
        Case("health-change-feedback-shows-current-value",f=>{f.Ready();f.Content.Health=.62;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("62%")&&f.Has("−20%")&&!f.Has("82%"));});
        Case("focus-or-first-person-exit-stops-all-new-readouts",f=>{f.Ready();f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Draw();Check(U.GUI.Labels.Count==0&&!f.Status.Visible);});
        Case("bot-preview-retains-content-but-does-not-draw-it",f=>{f.Ready();f.Provider.AuxiliaryBegin(new(801,1));f.Draw();Check(U.GUI.Labels.Count==0&&f.Runtime.TryGetContent(out _));f.Provider.AuxiliaryEnd(new(801,1));f.Draw();Check(f.Has("82%")&&f.Has("32"));});
        Case("budget-zero-does-not-run-formatting-or-ui",f=>{f.Ready();int calls=f.Content.Calls;Plugin.Instance.Settings.WorkOptions=Plugin.Instance.Settings.WorkOptions with{FrameBudgetMilliseconds=0};f.Content.Dirty();f.Next();f.Draw();Check(U.GUI.Labels.Count==0&&f.Content.Calls==calls);});
        Case("cosmetic-and-module-options-do-not-resample-game",f=>{f.Ready();int calls=f.Content.Calls;Plugin.Instance.Settings.PersonalOptions=HudPersonalOptions.Default with{Health=false,AutoExpandSelected=false};f.Next();f.Draw();Check(f.Has("32")&&!f.Has("82%")&&f.Content.Calls==calls);});
        Case("repaints-neither-collect-nor-create-unity-resources",f=>{f.Ready();int calls=f.Content.Calls,objects=U.Object.Created.Count;for(int i=0;i<30;i++)f.Draw();Check(f.Content.Calls==calls&&U.Object.Created.Count==objects);});
        Case("recreated-body-cannot-reuse-old-personal-readout",f=>{f.Ready();f.Content.Health=.41;f.Live=f.Live with{Binding=f.Live.Binding with{CharacterModel=new(909,90)}};for(int i=0;i<5;i++){f.Next();f.Draw();Check(!f.Has("82%"));}Check(f.Has("41%")&&f.Diagnostics.LiveOwnedReferences==2);});
        Case("draw-budget-includes-mapping-and-all-labels",f=>{f.Ready();U.GUI.Drawing=()=>ThirdPersonApi.MonotonicSeconds+=.001;f.Draw();long draws=f.Diagnostics.ActualDraws;f.Draw();Check(U.GUI.Labels.Count==0&&f.Diagnostics.ActualDraws==draws);f.Runtime.TryGetPerformance(out var perf);Check(perf.Executor.Draw.LastMilliseconds>=5&&perf.Executor.BudgetOverrunFrames>0);});
        Case("reliable-action-reaches-production-prompt-region",f=>{f.Ready();f.Content.Modify=s=>s with{Personal=s.Personal with{Action=new(PersonalTestData.Value(new HudAction(HudActionKind.Reloading),s.Personal.Health.Stamp),default){Equipment=s.Personal.Primary.Identity.RawValue.Instance}}};f.Content.Dirty();f.Next();f.Draw();Check(f.Has("换弹中")&&f.Has("32"));f.Content.Modify=null;f.Content.Dirty();f.Next();f.Draw();Check(!f.Has("换弹中"));});
        Case("resource-warning-clears-as-soon-as-data-dirty",f=>{f.Ready();f.Content.Count=0;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("补给包耗尽"));f.Content.Dirty();f.Draw();Check(!f.Has("补给包耗尽"));});
        Case("alerts-can-be-disabled-without-losing-readouts",f=>{f.Ready();f.Content.Count=0;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("补给包耗尽"));Plugin.Instance.Settings.AlertOptions=HudAlertOptions.Default with{Enabled=false};f.Next();f.Draw();Check(!f.Has("补给包耗尽")&&f.Has("32")&&f.Has("82%"));});
        Case("native-hit-lifetime-is-not-renewed-by-new-snapshots",f=>{f.Ready();double at=ThirdPersonApi.MonotonicSeconds;f.Content.Modify=s=>s with{Personal=s.Personal with{Hit=PersonalTestData.Value(new HudHit(1,at,Vector3.Zero),s.Personal.Health.Stamp)}};f.Content.Dirty();f.Next();f.Draw();Check(f.Has("受击"));f.Content.Dirty();f.Next(seconds:1.3);f.Draw();Check(!f.Has("受击"));});
        Case("auxiliary-preview-hides-prompts-and-retains-local-session",f=>{f.Ready();f.Content.Count=0;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("补给包耗尽"));long epoch=f.Status.WorkEpoch;f.Provider.AuxiliaryBegin(new(801,1));f.Draw();Check(U.GUI.Labels.Count==0&&f.Status.WorkEpoch==epoch);f.Provider.AuxiliaryEnd(new(801,1));f.Draw();Check(f.Has("补给包耗尽")&&f.Status.WorkEpoch==epoch);});
        Case("fallback-clears-prompts-even-when-critical",f=>{f.Ready();f.Content.Health=.1;f.Content.Dirty();f.Next();f.Draw();Check(f.Has("生命危急"));f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Draw();Check(U.GUI.Labels.Count==0);});
        Case("original-readouts-hide-only-after-successful-draw",f=>{f.Update();f.Commit();Check(f.Original.Targets.All(t=>!t.Value));f.Ready();Check(f.Original.Targets[0].Value&&f.Original.Targets[2].Value&&!f.Original.Targets[4].Value);});
        Case("ordinary-update-does-not-toggle-original-every-frame",f=>{f.Ready();int n=f.Original.Targets[0].Writes;for(int i=0;i<10;i++){f.Next();f.Draw();}Check(f.Original.Targets[0].Writes==n);});
        Case("partial-health-failure-restores-only-original-health",f=>{f.Ready();f.Content.Modify=s=>s with{Personal=s.Personal with{Health=default}};f.Content.Dirty();f.Next();f.Draw();Check(!f.Original.Targets[0].Value&&f.Original.Targets[2].Value);});
        Case("draw-exception-immediately-restores-all-original",f=>{f.Ready();U.Object.FailAt="Label";f.Draw();Check(f.Original.Targets.All(t=>!t.Value));});
        Case("draw-budget-deferral-restores-original",f=>{f.Ready();Plugin.Instance.Settings.WorkOptions=Plugin.Instance.Settings.WorkOptions with{FrameBudgetMilliseconds=0};f.Next();Check(f.Original.Targets.All(t=>!t.Value));});
        Case("real-control-transition-restores-original-before-repaint",f=>{f.Ready();f.Live=f.Live with{Source=ViewSource.BotControl,Availability=ViewAvailability.ControlTaken};f.Next(false);Check(f.Original.Targets.All(t=>!t.Value));});
        Case("auxiliary-preview-does-not-toggle-original",f=>{f.Ready();var before=f.Original.Targets.Select(t=>t.Writes).ToArray();f.Provider.AuxiliaryBegin(new(801,1));f.Draw();f.Provider.AuxiliaryEnd(new(801,1));f.Draw();Check(f.Original.Targets.Select(t=>t.Writes).SequenceEqual(before));});
        Case("manual-restore-latches-until-resumed",f=>{f.Ready();Check(f.Runtime.RestoreOriginal());for(int i=0;i<4;i++){f.Next();f.Draw();}Check(f.Original.Targets.All(t=>!t.Value)&&f.Has("32"));Check(f.Runtime.RestoreOriginal(resume:true));f.Next();f.Draw();Check(f.Original.Targets[0].Value);});
        Case("rebindable-restore-key-toggles-latched-restoration",f=>{f.Ready();U.Input.RestorePressed=true;f.Next();f.Draw();Check(f.Original.Targets.All(t=>!t.Value));U.Input.RestorePressed=true;f.Next();f.Draw();Check(f.Original.Targets[0].Value);});
        Case("stalled-repaint-watchdog-restores-original",f=>{f.Ready();f.Next();f.Next();Check(f.Original.Targets.All(t=>!t.Value));});
        Case("group-config-preserves-other-groups",f=>{f.Ready();Plugin.Instance.Settings.ReplacementOptions=HudReplacementOptions.Default with{Ammo=false};f.Next();f.Draw();Check(!f.Original.Targets[2].Value&&f.Original.Targets[0].Value);});
        Case("shutdown-restores-owned-original-widgets",f=>{f.Ready();f.Runtime.Stop();Check(f.Original.Targets.All(t=>!t.Value));});
        Case("disabled-runtime-component-restores-original",f=>{f.Ready();f.Runtime.OnDisable();Check(f.Original.Targets.All(t=>!t.Value));});
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,ProductionRuntimeAndRendererUsed=true,NativeGameExecuted=false,RuntimeRenderingVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Personal runtime wiring: {results.Count}; passed={passed}");return passed?0:1;
    }
}
