#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Runtime;
using U = UnityEngine;

internal static class WorkBudgetTests
{
    sealed record TestResult(string Name, bool Passed, string? Error);
    static readonly List<TestResult> results = new();
    static readonly Dictionary<string,object> stress = new();
    static void Check(bool condition,string message="assertion failed"){if(!condition)throw new Exception(message);}
    static bool Near(double a,double b)=>Math.Abs(a-b)<.00001;
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    sealed class Clock { public double Time=10;public void Spend(double ms)=>Time+=ms/1000; }
    sealed class Presentation : IHudPresentation
    {
        readonly Clock clock;public double InitCost,ApplyCost,DrawCost;public int Creates,Releases,Draws;
        public int InitializationStep{get;private set;}
        public bool HasResources=>InitializationStep>0;
        public bool IsHealthy{get;set;}=true;
        public bool IsReady=>InitializationStep==3&&IsHealthy;
        public HudPlacement Placement;public ContextStamp Bound;public LocalViewBinding Binding;
        public Presentation(Clock clock){this.clock=clock;}
        public void InitializeNextStep(){if(InitializationStep==0)Creates++;InitializationStep++;clock.Spend(InitCost);}
        public void Bind(ContextStamp c,LocalViewBinding b){Bound=c;Binding=b;}
        public void Show(in HudPlacement p){Check(Bound==p.View.Context&&Binding==p.View.Binding,"foreign binding applied");Placement=p;clock.Spend(ApplyCost);}
        public void Hide(){Placement=default;}
        public void Release(){if(HasResources)Releases++;InitializationStep=0;Bound=default;Binding=default;IsHealthy=true;}
        public void Draw(){Draws++;clock.Spend(DrawCost);}
    }
    sealed class Source : IHudDataSource
    {
        readonly Clock clock;public double Cost;public int Calls,Thread;public bool Corrupt,Throw;
        public Source(Clock clock){this.clock=clock;}
        public HudSample Capture(in HudSample input){Thread=Environment.CurrentManagedThreadId;Calls++;clock.Spend(Cost);if(Throw)throw new Exception("capture failed");return Corrupt?input with{WorkEpoch=input.WorkEpoch+1}:input;}
    }
    sealed class Computer : IHudDataComputer
    {
        readonly Clock clock;public double Cost;public int Calls,Thread;public bool Invalid,Throw;
        public Computer(Clock clock){this.clock=clock;}
        public HudAnchorSpec Compute(in HudSample s){Thread=Environment.CurrentManagedThreadId;Calls++;clock.Spend(Cost);if(Throw)throw new Exception("compute failed");return new(new(0,Invalid?float.NaN:s.Height,0),s.Side,s.SizePixels);}
    }
    sealed class Fixture : IDisposable
    {
        public readonly Clock Clock=new();public readonly Presentation Ui;public readonly Source Source;public readonly Computer Computer;
        public readonly HudLifecycle Hud;public readonly ThirdPersonViewProvider Provider;
        public HudLifecycleOptions Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
        public HudWorkOptions Work=HudWorkOptions.Default;
        public BridgeObservation Live;
        public CameraSnapshot Camera=new(new(new(0,1,3),Quaternion.Identity),Matrix4x4.CreateTranslation(0,-1,-3),Matrix4x4.CreatePerspectiveFieldOfView(1.2f,16f/9,.08f,250),new(0,0,1920,1080));
        public WorldPose Body=new(Vector3.Zero,Quaternion.Identity);
        public Fixture()
        {
            var p=new PlayerIdentity(new(100,1),200,0);var b=new LocalViewBinding(p,new(300,2),new(400,3),500,0);
            Live=new(b,p,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,100,10);
            Ui=new(Clock);Source=new(Clock);Computer=new(Clock);Hud=new(Ui,clock:()=>Clock.Time,source:Source,computer:Computer);
            Provider=new(()=>Live with{NowSeconds=Clock.Time});Provider.StateChanged+=s=>Hud.ObserveState(s);
            Provider.RenderChanged+=n=>{if(n.Render.Phase==RenderPhase.MainCommitted&&Provider.TryGetDisplayFrame(new(Live.FrameNumber,HudDisplayUsage.ScreenComposition),out var d))Hud.ObserveCommitted(Current,d);else Hud.HideForRender();};
        }
        public HudRuntimeContext Current{get{Provider.TryGetRuntimeContext(out var c);return c;}}
        public ThirdPersonDisplayFrame Display{get{Provider.TryGetDisplayFrame(new(Live.FrameNumber,HudDisplayUsage.ScreenComposition),out var d);return d;}}
        public HudLifecycleStatus Status{get{Hud.TryGetStatus(out var s);return s;}}
        public HudPerformanceSnapshot Perf{get{Hud.TryGetPerformance(out var p);return p;}}
        public void Tick(bool available=true){Hud.Tick(new(Live.InLevel,Live.FrameNumber,Clock.Time,available,available?Current:default),Options,Work);}
        public void Commit(){Provider.BeginMainAttempt();Check(Provider.PublishMain(Camera,Body));Provider.MainRestored(true);Provider.NativeMainCompleted(true);}
        public void Next(double milliseconds=16,bool commit=true){Clock.Spend(milliseconds);Live=Live with{FrameNumber=Live.FrameNumber+1};Tick();if(commit)Commit();}
        public void Ready(){Tick();Commit();for(int i=0;i<30&&!Status.Visible;i++)Next();Check(Status.Visible,"ready timeout");}
        public bool Draw()=>Hud.TryDraw(Current,Display,Ui.Draw);
        public void Dispose(){Hud.Dispose();Provider.Dispose();Check(Provider.SubscriberErrors==0,"subscriber failed");}
    }
    static void F(string name,Action<Fixture> test)=>Case(name,()=>{using var f=new Fixture();test(f);});
    static HudMainThreadExecutor Executor(Clock c)=>new(()=>c.Time);
    static void RuntimeCase(string name,Action<Fixture,HudRuntime> test)=>F(name,f=>
    {
        U.Object.Created.Clear();U.Object.FailAt=null;U.GUI.Draws=0;U.GUI.Drawing=null;U.Camera.current=null;U.Event.current=new(){type=U.EventType.Repaint};
        GameStateManager.CurrentStateName=eGameStateName.InLevel;ThirdPersonApi.Provider=f.Provider;Plugin.Instance=new();Plugin.Instance.Settings.Options=f.Options;
        var r=new HudRuntime(IntPtr.Zero);r.Initialize(()=>f.Clock.Time,legacyMarker:true);
        try{test(f,r);}finally{r.Stop();ThirdPersonApi.Provider=null!;U.GUI.Drawing=null;}
        Check(U.Object.Created.All(o=>o.Destroyed),"Unity resource leaked");
    });
    static void Update(Fixture f,HudRuntime r){U.Time.frameCount=f.Live.FrameNumber;ThirdPersonApi.MonotonicSeconds=f.Clock.Time;r.Update();}
    static void Next(Fixture f,HudRuntime r,bool commit=true){f.Clock.Spend(16);f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+1};Update(f,r);if(commit)f.Commit();}
    static void Ready(Fixture f,HudRuntime r){Update(f,r);f.Commit();for(int i=0;i<4;i++)Next(f,r);r.OnGUI();Check(U.GUI.Draws==1);}
    static int Main(string[] args)
    {
        Case("atomic-overrun-stops-subsequent-work",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);Check(e.TryExecute(1,HudWorkStage.Collection,()=>c.Spend(.6)));Check(!e.TryExecute(1,HudWorkStage.Computation,()=>throw new Exception()));e.TryGetSnapshot(out var p);Check(Near(p.FrameSpentMilliseconds,.6)&&p.BudgetOverrunFrames==1&&p.Computation.Deferrals==1);});
        Case("same-frame-does-not-renew-budget",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);e.TryExecute(1,HudWorkStage.Application,()=>c.Spend(.6));e.BeginFrame(1,HudWorkOptions.Default);Check(!e.TryExecute(1,HudWorkStage.Draw,()=>throw new Exception()));});
        Case("new-frame-resets-admission-without-clearing-history",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);e.TryExecute(1,HudWorkStage.Collection,()=>c.Spend(.6));e.BeginFrame(2,HudWorkOptions.Default);Check(e.TryExecute(2,HudWorkStage.Computation,()=>c.Spend(.1)));e.TryGetSnapshot(out var p);Check(Near(p.FrameSpentMilliseconds,.1)&&Near(p.MaximumFrameMilliseconds,.6)&&p.Collection.Count==1);});
        Case("preparation-count-does-not-starve-render-lane",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default with{MaxPreparationStepsPerFrame=1});Check(e.TryExecute(1,HudWorkStage.Collection,()=>{}));Check(!e.TryExecute(1,HudWorkStage.Computation,()=>{}));Check(e.TryExecute(1,HudWorkStage.Application,()=>{})&&e.TryExecute(1,HudWorkStage.Draw,()=>{}));});
        foreach(var stage in Enum.GetValues<HudWorkStage>())Case("zero-budget-defers-"+stage,()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default with{FrameBudgetMilliseconds=0});Check(!e.TryExecute(1,stage,()=>throw new Exception()));});
        Case("error-cost-counted-and-executor-recovers",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);try{e.TryExecute(1,HudWorkStage.Computation,()=>{c.Spend(.6);throw new Exception();});}catch{}e.TryGetSnapshot(out var p);Check(p.Computation.Errors==1&&Near(p.Computation.LastMilliseconds,.6));e.BeginFrame(2,HudWorkOptions.Default);Check(e.TryExecute(2,HudWorkStage.Collection,()=>{}));});
        Case("reentrant-run-and-frame-reset-denied",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);e.TryExecute(1,HudWorkStage.Computation,()=>{Check(!e.BeginFrame(2,HudWorkOptions.Default));Check(!e.TryExecute(1,HudWorkStage.Draw,()=>throw new Exception()));});});
        Case("worker-cannot-run-reset-or-read-executor",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);Task.Run(()=>{Check(!e.BeginFrame(2,HudWorkOptions.Default));Check(!e.TryExecute(1,HudWorkStage.Collection,()=>throw new Exception()));Check(!e.TryGetSnapshot(out _));Check(double.IsNaN(e.NowSeconds));}).GetAwaiter().GetResult();});
        Case("invalid-initial-clock-does-not-run",()=>{var e=new HudMainThreadExecutor(()=>double.NaN);e.BeginFrame(1,HudWorkOptions.Default);Check(!e.TryExecute(1,HudWorkStage.Draw,()=>throw new Exception()));e.TryGetSnapshot(out var p);Check(p.InvalidClockReads==1);});
        Case("backwards-clock-exhausts-current-budget",()=>{var c=new Clock();var e=Executor(c);e.BeginFrame(1,HudWorkOptions.Default);e.TryExecute(1,HudWorkStage.Draw,()=>c.Spend(-1));Check(!e.TryExecute(1,HudWorkStage.Collection,()=>{}));e.TryGetSnapshot(out var p);Check(p.InvalidClockReads==1);});
        Case("fixed-window-percentile-and-lifetime-mean",()=>{var c=new Clock();var e=Executor(c);for(int i=1;i<=200;i++){e.BeginFrame(i,HudWorkOptions.Default);int cost=i;e.TryExecute(i,HudWorkStage.Draw,()=>c.Spend(cost));}e.TryGetSnapshot(out var p);Check(p.Draw.Count==200&&Near(p.Draw.MeanMilliseconds,100.5)&&Near(p.Draw.RecentP95Milliseconds,194)&&Near(p.Draw.MaximumMilliseconds,200));});
        Case("work-configuration-sanitizes-extremes",()=>{var w=new HudWorkOptions(double.NaN,0,99,.1).Sanitized();Check(w.FrameBudgetMilliseconds==.5&&w.MaxPreparationStepsPerFrame==1&&w.RefreshIntervalSeconds==.1&&w.MaximumDataAgeSeconds==.1);w=new HudWorkOptions(-1,99,double.NaN,double.NaN).Sanitized();Check(w.FrameBudgetMilliseconds==0&&w.MaxPreparationStepsPerFrame==16&&w.RefreshIntervalSeconds==.05&&w.MaximumDataAgeSeconds==.25);});
        F("no-work-before-eligible-actual-frame",f=>{for(int i=0;i<300;i++)f.Next(16,false);Check(f.Perf.Captured==0&&f.Perf.Executor.Initialization.Count==0);});
        F("all-three-data-phases-use-main-thread",f=>{f.Ready();f.Draw();int t=Environment.CurrentManagedThreadId;Check(f.Source.Thread==t&&f.Computer.Thread==t&&f.Perf.Executor.Application.Count>0&&f.Perf.Executor.Draw.Count==1);});
        F("one-preparation-step-progresses-without-starvation",f=>{f.Work=f.Work with{MaxPreparationStepsPerFrame=1};f.Ready();Check(f.Draw()&&f.Perf.Captured>=1&&f.Perf.Computed>=1&&f.Perf.Executor.PreparationStepsThisFrame==1);});
        F("initialization-shares-budget-with-data-work",f=>{f.Ui.InitCost=.7;f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next();Check(f.Status.InitializationSteps==3&&f.Perf.Captured==0&&!f.Status.Visible&&f.Perf.Executor.BudgetOverrunFrames==3);f.Next();Check(f.Status.Visible&&f.Perf.Captured==1);});
        F("collection-overrun-retains-one-input-then-computes",f=>{f.Source.Cost=.7;f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next();Check(f.Perf.PendingInputs==1&&f.Perf.Computed==0&&!f.Status.Visible);f.Source.Cost=0;f.Next();Check(f.Status.Visible&&f.Perf.Computed==1&&f.Perf.MaximumPendingInputs==1);});
        F("computation-overrun-defers-application-to-next-frame",f=>{f.Computer.Cost=.7;f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next();Check(f.Perf.Computed==1&&f.Perf.RetainedResults==1&&!f.Status.Visible&&f.Perf.Executor.Application.Deferrals>0);f.Computer.Cost=0;f.Next();Check(f.Status.Visible&&f.Perf.Executor.BudgetOverrunFrames==1);});
        F("application-and-drawing-consume-same-budget",f=>{f.Ui.ApplyCost=.4;f.Ui.DrawCost=.2;f.Ready();Check(f.Draw());Check(!f.Draw());Check(Near(f.Perf.Executor.FrameSpentMilliseconds,.6)&&f.Perf.Executor.BudgetOverrunFrames==1&&f.Perf.Executor.Draw.Deferrals==1);});
        F("refresh-throttles-data-while-camera-projection-stays-current",f=>{f.Work=f.Work with{RefreshIntervalSeconds=.1};f.Ready();long captures=f.Perf.Captured;for(int i=0;i<300;i++){f.Body=new(new((i%5)*.02f,0,0),Quaternion.Identity);f.Next(10);Check(f.Status.Visible);Check(MathF.Abs(f.Ui.Placement.WorldAnchor.X-(f.Body.Position.X+.65f))<.001);}Check(f.Perf.Captured-captures<=30&&f.Perf.ReusedApplications>250&&f.Ui.Creates==1);});
        F("refresh-zero-still-samples-at-most-once-per-frame",f=>{f.Work=f.Work with{RefreshIntervalSeconds=0,MaxPreparationStepsPerFrame=16};f.Ready();long captures=f.Perf.Captured;for(int i=0;i<20;i++)f.Tick();Check(f.Perf.Captured==captures);f.Next();Check(f.Perf.Captured==captures+1);});
        F("zero-budget-retains-resources-and-hides",f=>{f.Ready();f.Work=f.Work with{FrameBudgetMilliseconds=0};f.Next();Check(!f.Status.Visible&&f.Ui.Creates==1&&f.Ui.Releases==0);f.Next(300);Check(f.Perf.RetainedResults==0&&f.Perf.ExpiredResults>0);f.Work=HudWorkOptions.Default;f.Next();Check(f.Status.Visible&&f.Ui.Creates==1);});
        F("old-input-expires-before-computation",f=>{f.Work=f.Work with{MaxPreparationStepsPerFrame=1};f.Tick();f.Commit();for(int i=0;i<4;i++)f.Next();Check(f.Perf.PendingInputs==1);f.Work=f.Work with{FrameBudgetMilliseconds=0};f.Next(300);Check(f.Perf.PendingInputs==0&&f.Perf.ExpiredInputs==1&&f.Perf.Computed==0);});
        F("slow-completion-cannot-renew-sample-age",f=>{f.Computer.Cost=300;f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next();Check(f.Perf.Computed==1&&f.Perf.RejectedResults==1&&f.Perf.RetainedResults==0&&!f.Status.Visible);});
        F("data-validity-checked-again-before-drawing",f=>{f.Ready();f.Clock.Spend(260);Check(!f.Draw()&&!f.Status.Visible);});
        F("old-display-remains-forbidden-with-fresh-data",f=>{f.Ready();var display=f.Display;f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+1};f.Clock.Spend(16);Check(!f.Hud.TryDraw(f.Current,display,f.Ui.Draw));});
        F("same-frame-new-pass-reprojects-without-new-data",f=>{f.Ready();long captures=f.Perf.Captured,seq=f.Perf.LastAppliedSequence;var old=f.Display;f.Body=new(new(.2f,0,0),Quaternion.Identity);f.Commit();Check(f.Perf.Captured==captures&&f.Perf.LastAppliedSequence==seq&&MathF.Abs(f.Ui.Placement.WorldAnchor.X-.85f)<.001);Check(!f.Hud.CanDraw(f.Current,old));});
        F("auxiliary-hide-retains-data-without-new-sample",f=>{f.Ready();var p=f.Perf;f.Provider.AuxiliaryBegin(new(800,8));Check(!f.Status.Visible&&f.Perf.RetainedResults==1);f.Provider.AuxiliaryEnd(new(800,8));Check(f.Status.Visible&&f.Perf.Captured==p.Captured&&f.Perf.ReusedApplications==p.ReusedApplications+1);});
        F("apply-uses-new-final-camera-matrices",f=>{f.Ready();float x=f.Ui.Placement.PixelCenter.X;f.Camera=f.Camera with{WorldToView=Matrix4x4.CreateTranslation(-.1f,-1,-3)};f.Commit();Check(f.Ui.Placement.PixelCenter.X<x&&f.Perf.Captured==1);});
        F("geometry-settings-invalidate-retained-data",f=>{f.Ready();long old=f.Perf.SettingsRevision;f.Options=f.Options with{AnchorSide=-.65f};f.Next();Check(f.Status.Visible&&f.Ui.Placement.WorldAnchor.X<0&&f.Perf.SettingsRevision==old+1&&f.Ui.Creates==1);});
        F("third-person-config-version-invalidates-data",f=>{f.Ready();long seq=f.Perf.LastAppliedSequence;f.Live=f.Live with{ConfigurationRevision=2};f.Next();Check(f.Status.Visible&&f.Perf.LastAppliedSequence>seq&&f.Ui.Creates==1);});
        foreach(var part in new[]{"camera","model","dimension","floor"})F("pending-work-revoked-on-rebind-"+part,f=>{f.Ready();long seq=f.Perf.LastAppliedSequence;var b=f.Live.Binding;b=part switch{"camera"=>b with{MainCamera=new(900,9)},"model"=>b with{CharacterModel=new(901,10)},"dimension"=>b with{DimensionIndex=1},_=>b with{FloorHandle=999}};f.Live=f.Live with{Binding=b};f.Commit();Check(!f.Status.Visible&&f.Perf.RetainedResults==0);for(int i=0;i<4;i++)f.Next();Check(f.Status.Visible&&f.Perf.LastAppliedSequence>seq&&f.Ui.Creates==(part=="floor"?2:1));});
        foreach(var source in Enum.GetValues<ViewSource>().Where(s=>s!=ViewSource.LocalThirdPerson))F("source-switch-cancels-data-"+source,f=>{f.Ready();f.Live=f.Live with{Source=source};f.Next(16,false);Check(!f.Status.Visible&&f.Perf.PendingInputs==0&&f.Perf.RetainedResults==0&&f.Ui.Releases==0);});
        F("provider-loss-does-not-release-prepared-resources",f=>{f.Ready();f.Tick(false);Check(!f.Status.Visible&&f.Perf.RetainedResults==0&&f.Ui.Releases==0);f.Next();Check(!f.Status.Visible);f.Next();Check(f.Status.Visible&&f.Ui.Creates==1);});
        foreach(var kind in new[]{"capture","compute","invalid-sample","invalid-result"})F("stage-error-retry-and-resource-policy-"+kind,f=>{f.Source.Throw=kind=="capture";f.Computer.Throw=kind=="compute";f.Source.Corrupt=kind=="invalid-sample";f.Computer.Invalid=kind=="invalid-result";f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next();bool pure=kind is "compute" or "invalid-result";Check(f.Status.Errors==(pure?0:1)&&f.Status.HasResources==pure&&f.Perf.PendingInputs==0&&f.Perf.RetainedResults==0);if(pure)Check(f.Perf.Computation.Errors==1&&f.Ui.Creates==1&&f.Ui.Releases==0);f.Source.Throw=f.Source.Corrupt=f.Computer.Throw=f.Computer.Invalid=false;f.Next(1100);for(int i=0;i<5&&!f.Status.Visible;i++)f.Next();Check(f.Status.Visible);});
        F("geometry-change-cancels-pending-input",f=>{f.Work=f.Work with{MaxPreparationStepsPerFrame=1};f.Tick();f.Commit();for(int i=0;i<4;i++)f.Next();Check(f.Perf.PendingInputs==1);f.Options=f.Options with{AnchorSide=-.5f};f.Next();Check(f.Perf.CancelledInputs==1&&f.Perf.Computed==0);f.Next();Check(f.Status.Visible&&MathF.Abs(f.Ui.Placement.WorldAnchor.X+.5f)<.001);});
        F("outlevel-clears-pending-input-immediately",f=>{f.Work=f.Work with{MaxPreparationStepsPerFrame=1};f.Tick();f.Commit();for(int i=0;i<4;i++)f.Next();f.Provider.ResetContext();Check(!f.Status.HasResources&&f.Perf.PendingInputs==0&&f.Perf.RetainedResults==0);});
        F("work-and-performance-snapshots-reject-worker-calls",f=>{f.Ready();var before=f.Perf;Task.Run(()=>{Check(!f.Hud.TryGetPerformance(out _));f.Hud.Tick(default,f.Options,f.Work);}).GetAwaiter().GetResult();Check(f.Perf==before);});
        F("retained-result-and-pending-input-stay-bounded-over-10000-frames",f=>{f.Work=f.Work with{MaxPreparationStepsPerFrame=1,RefreshIntervalSeconds=0};f.Ready();for(int i=0;i<10000;i++){f.Next();Check(f.Perf.PendingInputs<=1&&f.Perf.RetainedResults<=1&&f.Perf.Executor.PreparationStepsThisFrame<=1);}Check(f.Ui.Creates==1&&f.Ui.Releases==0&&f.Perf.Computed>4000);stress["Continuous10000Frames"]=new{f.Perf.Captured,f.Perf.Computed,f.Perf.MaximumPendingInputs,f.Ui.Creates,f.Ui.Releases};});
        F("2000-switches-cancel-pending-input-without-resource-churn",f=>{f.Ready();f.Work=f.Work with{MaxPreparationStepsPerFrame=1,RefreshIntervalSeconds=0};for(int i=0;i<2000;i++){f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Availability=ViewAvailability.TemporaryFallback};f.Next(16,false);Check(f.Perf.PendingInputs==0&&f.Perf.RetainedResults==0);f.Live=f.Live with{Source=ViewSource.LocalThirdPerson,Availability=ViewAvailability.Ready};f.Next();f.Next();Check(f.Perf.PendingInputs==1);}Check(f.Ui.Creates==1&&f.Ui.Releases==0&&f.Perf.MaximumPendingInputs==1&&f.Perf.CancelledInputs>=1999);stress["RapidSwitches2000"]=new{f.Perf.Captured,f.Perf.CancelledInputs,f.Perf.MaximumPendingInputs,f.Ui.Creates,f.Ui.Releases};});
        RuntimeCase("production-runtime-shares-draw-budget-across-repaints",(f,r)=>{Ready(f,r);U.GUI.Drawing=()=>f.Clock.Spend(.4);r.OnGUI();r.OnGUI();r.OnGUI();r.TryGetPerformance(out var p);Check(p.Executor.Draw.Count==3&&p.Executor.Draw.Deferrals==1&&p.Executor.BudgetOverrunFrames==1);});
        RuntimeCase("production-runtime-500-switches-create-one-anchor-and-texture",(f,r)=>{Ready(f,r);for(int i=0;i<500;i++){f.Live=f.Live with{Source=ViewSource.BotControl,LocalBodyViewActive=false};Next(f,r,false);r.OnGUI();f.Live=f.Live with{Source=ViewSource.LocalThirdPerson,LocalBodyViewActive=true};Next(f,r);Next(f,r);r.OnGUI();}r.TryGetPerformance(out var p);Check(U.Object.Created.Count==2&&U.Object.Created.All(o=>!o.Destroyed)&&p.MaximumPendingInputs==1);stress["ProductionRuntimeSwitches500"]=new{CreatedObjects=U.Object.Created.Count,p.Captured,p.MaximumPendingInputs,p.CancelledResults};});
        RuntimeCase("production-logs-aggregate-on-interval-and-stop",(f,r)=>{Ready(f,r);Check(Plugin.Instance.Log.InformationMessages.Count==0);for(int i=0;i<700;i++)Next(f,r);Check(Plugin.Instance.Log.InformationMessages.Count==2);r.Stop();Check(Plugin.Instance.Log.InformationMessages.Count==4);});
        RuntimeCase("performance-log-period-survives-rapid-view-switches",(f,r)=>{Plugin.Instance.Settings.LogIntervalSeconds=.2;Ready(f,r);for(int i=0;i<12;i++){f.Live=f.Live with{Source=ViewSource.BotControl,LocalBodyViewActive=false};Next(f,r,false);f.Live=f.Live with{Source=ViewSource.LocalThirdPerson,LocalBodyViewActive=true};Next(f,r);Next(f,r);}Check(Plugin.Instance.Log.InformationMessages.Count>=2);});
        RuntimeCase("zero-log-interval-disables-periodic-records",(f,r)=>{Plugin.Instance.Settings.LogIntervalSeconds=0;Ready(f,r);for(int i=0;i<700;i++)Next(f,r);Check(Plugin.Instance.Log.InformationMessages.Count==0);r.Stop();Check(Plugin.Instance.Log.InformationMessages.Count==2);});
        bool passed=results.All(r=>r.Passed);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,RuntimeRenderingVerified=false,Clock="Synthetic monotonic clock; no real-game frame-time claim",Stress=stress,Tests=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var test in results.Where(t=>!t.Passed))Console.WriteLine(test.Name+": "+test.Error);
        Console.WriteLine($"Work budget / pipeline / stress: {results.Count}; passed={passed}");return passed?0:1;
    }
}
