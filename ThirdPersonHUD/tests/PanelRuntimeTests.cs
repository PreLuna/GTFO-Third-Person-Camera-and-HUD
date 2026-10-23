#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;
using PreLuna.ThirdPersonHUD.Runtime;
using U=UnityEngine;

static class PanelRuntimeTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string reason="assertion failed"){if(!ok)throw new Exception(reason);}
    sealed class Source:IHudContentSource
    {public int Calls;public bool TryCapture(in HudContentCaptureRequest r,out HudContentSnapshot s){Calls++;s=PanelTestData.Snapshot(r);return true;}}
    sealed class Fixture:IDisposable
    {
        public BridgeObservation Live;
        public readonly ThirdPersonViewProvider Provider;
        public readonly HudRuntime Runtime;
        public readonly Source Content=new();
        public CameraSnapshot Camera;
        public Fixture()
        {
            U.Object.Created.Clear();U.Object.FailAt=null;U.GUI.Labels.Clear();U.GUI.Draws=0;U.GUI.Drawing=null;U.Camera.current=null;
            U.Font.MissingGlyphs=false;U.Font.RejectedName=null;U.Input.Held=false;U.GUI.color=U.Color.white;U.Event.current=new(){type=U.EventType.Repaint};
            GameStateManager.CurrentStateName=eGameStateName.InLevel;
            var c=PanelTestData.Context();Camera=PanelTestData.Display(c).Camera;
            Live=new(c.ExpectedBinding,c.ExpectedBinding.LocalPlayer,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,100,10);
            Provider=new(()=>Live);ThirdPersonApi.Provider=Provider;Plugin.Instance=new();
            Plugin.Instance.Settings.Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
            Plugin.Instance.Settings.ExecutionOptions=new(HudComputationMode.MainThread,.5);
            Plugin.Instance.Settings.WorkOptions=new(5,3,.2,.5);
            Runtime=new(IntPtr.Zero);Runtime.Initialize(()=>ThirdPersonApi.MonotonicSeconds,contentSource:Content);
        }
        public void Update(){U.Time.frameCount=Live.FrameNumber;ThirdPersonApi.MonotonicSeconds=Live.NowSeconds;Runtime.Update();}
        public void Commit(){Provider.BeginMainAttempt();Check(Provider.PublishMain(Camera,new(Vector3.Zero,Quaternion.Identity)),"publish failed");Provider.MainRestored(true);Provider.NativeMainCompleted(true);}
        public void Next(bool commit=true){Live=Live with{FrameNumber=Live.FrameNumber+1,NowSeconds=Live.NowSeconds+.02};Update();if(commit)Commit();}
        public void Ready(){Update();Commit();for(int i=0;i<8&&!Status.Visible;i++)Next();Runtime.OnGUI();Check(Diagnostics.ActualDraws==1,"formal panel was not drawn: "+Status.Phase);}
        public HudPresentationDiagnostics Diagnostics{get{Runtime.TryGetPresentationDiagnostics(out var d);return d;}}
        public HudLifecycleStatus Status{get{Runtime.TryGetStatus(out var s);return s;}}
        public void Dispose(){Runtime.Stop();Provider.Dispose();ThirdPersonApi.Provider=null!;U.Object.FailAt=null;U.Font.MissingGlyphs=false;U.Font.RejectedName=null;Check(U.Object.Created.All(o=>o.Destroyed),"owned Unity resource leaked");}
    }
    static void Case(string name,Action<Fixture> test)
    {try{using var f=new Fixture();test(f);results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        Case("default-runtime-uses-formal-backend",f=>{f.Ready();Check(f.Diagnostics.AnchorsCreated==0&&f.Diagnostics.TexturesCreated==1&&f.Diagnostics.LiveOwnedReferences==2&&f.Diagnostics.LastDraw.Panel.IsValid);});
        Case("no-resources-before-committed-game-frame",f=>{for(int i=0;i<12;i++)f.Next(false);Check(U.Object.Created.Count==0);});
        Case("initialization-spans-three-main-thread-updates",f=>{f.Update();f.Commit();for(int step=1;step<=3;step++){f.Next();Check(f.Status.InitializationStep==step);}});
        Case("chinese-labels-live-values-and-missing-subfields-reach-production-draw",f=>{f.Ready();Check(U.GUI.Labels.Contains("生命")&&U.GUI.Labels.Contains("弹匣")&&U.GUI.Labels.Contains("—")&&U.GUI.Labels.Contains("80%")&&U.GUI.Labels.Contains("32"));Check(!U.GUI.Labels.Contains("82%")&&!U.GUI.Labels.Contains("180"),"offline example reached runtime");});
        Case("repaints-reuse-font-style-and-texture",f=>{f.Ready();int n=U.Object.Created.Count;for(int i=0;i<40;i++)f.Runtime.OnGUI();Check(U.Object.Created.Count==n&&U.Object.Created.OfType<U.Texture2D>().Single().Uploads==1);});
        Case("layout-and-key-events-never-draw",f=>{f.Ready();long n=f.Diagnostics.ActualDraws;U.Event.current.type=U.EventType.Layout;f.Runtime.OnGUI();U.Event.current.type=U.EventType.KeyDown;f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==n);});
        Case("texture-target-is-excluded",f=>{f.Ready();U.Camera.current=new(){targetTexture=new object()};f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1&&f.Diagnostics.TextureTargetRejections==1);});
        Case("nested-preview-hides-without-cancelling-content",f=>{f.Ready();f.Provider.AuxiliaryBegin(new(801,1));f.Provider.AuxiliaryBegin(new(802,1));f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1&&f.Runtime.TryGetContent(out _));f.Provider.AuxiliaryEnd(new(802,1));f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1);f.Provider.AuxiliaryEnd(new(801,1));f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==2);});
        Case("old-camera-frame-never-draws",f=>{f.Ready();f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+1,NowSeconds=f.Live.NowSeconds+.02};U.Time.frameCount=f.Live.FrameNumber;f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1);});
        Case("first-person-immediately-hides-even-with-zero-budget",f=>{f.Ready();Plugin.Instance.Settings.WorkOptions=Plugin.Instance.Settings.WorkOptions with{FrameBudgetMilliseconds=0};f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Availability=ViewAvailability.TemporaryFallback};f.Next(false);f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1&&!f.Status.Visible);});
        Case("bot-control-never-shows-personal-panel",f=>{f.Ready();f.Live=f.Live with{Source=ViewSource.BotControl,LocalBodyViewActive=false};f.Next(false);f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1&&!f.Runtime.TryGetContent(out _));});
        Case("focus-loss-resets-expanded-state",f=>{f.Ready();U.Input.Held=true;f.Next();f.Next();f.Runtime.OnGUI();Check(f.Diagnostics.LastDraw.Panel.Expanded);f.Live=f.Live with{FocusAllowed=false};f.Next(false);f.Runtime.OnGUI();Check(!f.Status.Visible);});
        Case("hold-expand-and-release-change-layout",f=>{f.Ready();U.Input.Held=true;f.Next();f.Next();f.Runtime.OnGUI();Check(f.Diagnostics.LastDraw.Panel.Expanded);U.Input.Held=false;f.Next();f.Next();f.Runtime.OnGUI();Check(!f.Diagnostics.LastDraw.Panel.Expanded);});
        Case("appearance-only-change-reuses-resources-and-content",f=>{f.Ready();int objects=U.Object.Created.Count,reads=f.Content.Calls;var p=Plugin.Instance.Settings.PanelOptions;Plugin.Instance.Settings.PanelOptions=p with{Theme=p.Theme with{BackgroundOpacity=0,Brightness=.6f}};f.Next();f.Runtime.OnGUI();Check(U.Object.Created.Count==objects&&f.Content.Calls==reads&&f.Runtime.TryGetContent(out _));});
        Case("geometry-change-keeps-content-generation",f=>{f.Ready();Check(f.Runtime.TryGetContent(out var before));int calls=f.Content.Calls;var p=Plugin.Instance.Settings.PanelOptions;Plugin.Instance.Settings.PanelOptions=p with{Layout=p.Layout with{Width=340}};f.Next();Check(f.Runtime.TryGetContent(out var after)&&after.Header==before.Header&&f.Content.Calls==calls);});
        Case("font-change-rebuilds-owned-resources",f=>{f.Ready();var old=U.Object.Created.ToArray();var p=Plugin.Instance.Settings.PanelOptions;Plugin.Instance.Settings.PanelOptions=p with{Typography=p.Typography with{FontName="Another CJK Font"}};f.Next();f.Next();f.Next();f.Runtime.OnGUI();Check(old.All(o=>o.Destroyed)&&f.Diagnostics.ActualDraws==2);});
        Case("font-with-missing-glyphs-falls-back-and-cleans-candidate",f=>{U.Font.RejectedName="Rejected";var p=Plugin.Instance.Settings.PanelOptions;Plugin.Instance.Settings.PanelOptions=p with{Typography=p.Typography with{FontName="Rejected"}};f.Ready();Check(U.Object.Created.OfType<U.Font>().Count()==2&&U.Object.Created.OfType<U.Font>().First().Destroyed);});
        Case("no-chinese-font-fails-closed-and-cleans-resources",f=>{U.Font.MissingGlyphs=true;f.Update();f.Commit();f.Next();f.Next();Check(f.Status.Phase==HudLifecyclePhase.Faulted&&!f.Status.HasResources&&U.Object.Created.All(o=>o.Destroyed));});
        Case("destroyed-font-is-rebuilt",f=>{f.Ready();U.Object.Destroy(U.Object.Created.OfType<U.Font>().Single());f.Next();for(int i=0;i<8&&!f.Status.Visible;i++)f.Next();f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==2&&f.Diagnostics.LiveOwnedReferences==2);});
        Case("label-failure-restores-global-gui-color",f=>{f.Ready();U.GUI.color=new(.2f,.3f,.4f,.5f);U.Object.FailAt="Label";f.Runtime.OnGUI();Check(U.GUI.color.r==.2f&&U.GUI.color.a==.5f&&f.Diagnostics.ActualDraws==1&&!f.Status.HasResources);});
        Case("disable-panel-releases-font-and-texture",f=>{f.Ready();var p=Plugin.Instance.Settings.PanelOptions;Plugin.Instance.Settings.PanelOptions=p with{Layout=p.Layout with{Enabled=false}};f.Next(false);Check(!f.Status.HasResources&&U.Object.Created.All(o=>o.Destroyed));});
        Case("out-of-level-cleans-without-waiting-for-budget",f=>{f.Ready();GameStateManager.CurrentStateName=eGameStateName.Lobby;Plugin.Instance.Settings.WorkOptions=Plugin.Instance.Settings.WorkOptions with{FrameBudgetMilliseconds=0};f.Next(false);Check(!f.Status.HasResources);});
        Case("single-preparation-step-does-not-starve-formal-positioning",f=>{Plugin.Instance.Settings.WorkOptions=new(5,1,0,.25);f.Update();f.Commit();for(int i=0;i<30;i++){f.Next();f.Runtime.OnGUI();}Check(f.Diagnostics.ActualDraws>0);});
        Case("formal-layout-survives-real-background-computation",f=>{Plugin.Instance.Settings.ExecutionOptions=new(HudComputationMode.Background,.5);f.Update();f.Commit();for(int i=0;i<100&&f.Diagnostics.ActualDraws==0;i++){f.Next();Thread.Sleep(1);f.Runtime.OnGUI();}Check(f.Diagnostics.ActualDraws>0&&f.Diagnostics.LastDraw.Panel.IsValid);});
        Case("stop-is-idempotent-and-disconnects-provider",f=>{f.Ready();f.Runtime.Stop();f.Runtime.Stop();f.Commit();f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==1&&U.Object.Created.All(o=>o.Destroyed));});
        bool passed=results.All(r=>r.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,ProductionBackendUsed=true,NativeGameExecuted=false,RuntimeRenderingVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Formal backend/runtime checks: {results.Count}; passed={passed}");return passed?0:1;
    }
}
