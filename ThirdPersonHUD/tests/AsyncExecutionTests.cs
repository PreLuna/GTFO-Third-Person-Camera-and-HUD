#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Runtime;
using U=UnityEngine;

internal static class AsyncExecutionTests
{
    sealed record TestResult(string Name,bool Passed,string? Error);
    static readonly List<TestResult> results=new();
    static readonly Dictionary<string,object> evidence=new();
    static void Check(bool b,string text="assertion failed"){if(!b)throw new Exception(text);}
    static void Case(string name,Action action){try{action();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static void Until(Func<bool> ready,string reason="worker timeout")
    {var timer=Stopwatch.StartNew();while(!ready()){if(timer.ElapsedMilliseconds>4000)throw new Exception(reason);Thread.Sleep(1);}}
    sealed class Clock
    {long ticks=100000000;public double Now()=>Interlocked.Read(ref ticks)/10000000d;public void Advance(double ms)=>Interlocked.Add(ref ticks,(long)(ms*10000));}
    sealed class Ui : IHudPresentation
    {
        readonly int main=Environment.CurrentManagedThreadId;
        public HudPlacement Last;public int Shows,Releases;
        public int InitializationStep=>3;public bool IsReady=>true;public bool HasResources=>true;public bool IsHealthy=>true;
        public void InitializeNextStep(){Check(Environment.CurrentManagedThreadId==main);}
        public void Bind(ContextStamp c,LocalViewBinding b){Check(Environment.CurrentManagedThreadId==main);}
        public void Show(in HudPlacement p){Check(Environment.CurrentManagedThreadId==main,"Unity port called on worker");Last=p;Shows++;}
        public void Hide(){Check(Environment.CurrentManagedThreadId==main);Last=default;}
        public void Release(){Check(Environment.CurrentManagedThreadId==main);Releases++;}
    }
    sealed class Source : IHudDataSource
    {readonly int main=Environment.CurrentManagedThreadId;public int Calls;public HudSample Capture(in HudSample s){Check(Environment.CurrentManagedThreadId==main,"sampling left main thread");Calls++;return s;}}
    sealed class Computer : IHudBackgroundDataComputer
    {
        readonly int main=Environment.CurrentManagedThreadId;
        public readonly ManualResetEventSlim Entered=new(false),Release=new(false),Left=new(false);
        public volatile bool Block,Cooperative,Fail,Invalid;
        public int Calls,ThreadId;
        public HudAnchorSpec Compute(in HudSample s)=>Compute(s,default);
        public HudAnchorSpec Compute(in HudSample s,in HudComputationCancellation c)
        {
            Interlocked.Increment(ref Calls);Volatile.Write(ref ThreadId,Environment.CurrentManagedThreadId);
            try
            {
                if(Block&&Environment.CurrentManagedThreadId!=main)
                {
                    Entered.Set();var timer=Stopwatch.StartNew();
                    while(!Release.IsSet){if(Cooperative)c.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>5000)throw new Exception("test gate timeout");Thread.Sleep(1);}
                }
                if(Fail)throw new InvalidOperationException("test calculation failed");
                return new(new(0,Invalid?float.NaN:s.Height,0),s.Side,s.SizePixels);
            }
            finally{Left.Set();}
        }
    }
    sealed class Legacy : IHudDataComputer
    {public int ThreadId;public HudAnchorSpec Compute(in HudSample s){ThreadId=Environment.CurrentManagedThreadId;return new(new(0,s.Height,0),s.Side,s.SizePixels);}}
    sealed class Manual : IHudBackgroundExecutor
    {
        public readonly List<HudComputationRequest> Requests=new();public HudComputationOutcome? Output;
        public bool Reject,Disposed;public int Cancels;
        public bool TrySubmit(in HudComputationRequest r){if(Reject||Disposed)return false;Requests.Add(r);return true;}
        public bool TryTake(out HudComputationOutcome o){o=Output??default;bool has=Output.HasValue;Output=null;return has;}
        public void Cancel(){Cancels++;Output=null;}
        public bool TryGetSnapshot(out HudWorkerSnapshot s){s=new(false,Disposed,Disposed,0,0,Output.HasValue?1:0,0,Requests.Count,0,0,0,0,0,0,0,0,0,0);return true;}
        public void Dispose(){Disposed=true;Cancel();}
    }
    sealed class Fixture : IDisposable
    {
        public readonly Clock Clock=new();public readonly Ui Ui=new();public readonly Source Source=new();
        public readonly HudMainThreadExecutor Main;public readonly HudDataPipeline Data;
        public HudWorkOptions Work=new(5,3,0,.25);public HudLifecycleOptions Geometry=HudLifecycleOptions.Default;
        public HudExecutionOptions Execution=new(HudComputationMode.Background,.5);
        public ThirdPersonViewState View;public int Frame=100;public long Epoch=1,Pass=1;
        public Vector3 Body;
        public readonly List<string> Errors=new();
        public Fixture(IHudDataComputer? computer=null,IHudBackgroundExecutor? transport=null)
        {
            var p=new PlayerIdentity(new(100,1),200,0);var b=new LocalViewBinding(p,new(300,2),new(400,3),500,0);
            View=new(1,new(Guid.NewGuid(),1),b,p,1,1,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready);
            Main=new(Clock.Now);Data=new(Main,Ui,Source,computer??new PlaceholderDataComputer(),transport,Errors.Add);
        }
        public HudRuntimeContext Current=>new(true,true,true,View.Context,View.Binding,View,Frame,Clock.Now(),new(new(View.Binding.MainCamera,RenderSurface.MainDisplay,Pass),RenderPhase.MainCommitted));
        public ThirdPersonDisplayFrame Display=>new(View,Current.Render.Identity,Frame,Pass,Clock.Now(),true,
            new(new(new(0,1,3),Quaternion.Identity),Matrix4x4.CreateTranslation(0,-1,-3),Matrix4x4.CreatePerspectiveFieldOfView(1.2f,16f/9,.08f,250),new(0,0,1920,1080)),new(Body,Quaternion.Identity));
        public HudPerformanceSnapshot Perf{get{Data.TryGetSnapshot(out var p);return p;}}
        public void Prepare(){Main.BeginFrame(Frame,Work);Data.Configure(Work,Geometry,Execution);Data.Prepare(Current,Epoch);}
        public bool Apply()=>Data.TryApply(Current,Display,Epoch,out _);
        public void Next(double ms=1){Clock.Advance(ms);Frame++;Pass++;Prepare();}
        public void Ready(){Prepare();Until(()=>Perf.Computation.Worker.CompletedMailbox==1);Next();Check(Apply(),"first result not applied");}
        public HudComputationOutcome Outcome(HudComputationRequest request)=>HudComputationRunner.Execute(request,new PlaceholderDataComputer(),Clock.Now);
        public void Dispose(){Data.Dispose();Until(()=>!Perf.Computation.Worker.Started||Perf.Computation.Worker.Exited,"worker did not exit after dispose");}
    }
    static void F(string name,Action<Fixture,Manual> test)=>Case(name,()=>{var m=new Manual();using var f=new Fixture(transport:m);test(f,m);});
    static void Complete(Fixture f,Manual m,int index=0){m.Output=f.Outcome(m.Requests[index]);f.Next();Check(f.Apply());}
    static HudWorkerSnapshot Snapshot(HudBackgroundExecutor worker){worker.TryGetSnapshot(out var s);return s;}
    static int Main(string[] args)
    {
        Case("execution-options-sanitize",()=>{var o=new HudExecutionOptions((HudComputationMode)99,double.NaN).Sanitized();Check(o.Mode==HudComputationMode.MainThread&&o.ErrorRetryDelaySeconds==.5);});
        Case("background-starts-lazily",()=>{using var f=new Fixture();Check(!f.Perf.Computation.Worker.Started);f.Data.Configure(f.Work,f.Geometry,f.Execution);Check(!f.Perf.Computation.Worker.Started);});
        Case("real-worker-results-equal-main-for-128-inputs",()=>
        {
            var clock=new Clock();var computer=new Computer();using var worker=new HudBackgroundExecutor(computer,clock.Now);
            using var f=new Fixture();
            for(int i=1;i<=128;i++)
            {
                var sample=new HudSample(new(f.View,i,100,clock.Now()),1,1,(i%30)/10f,(i%40-20)/10f,4+i%44);
                var request=new HudComputationRequest(sample,clock.Now()+.25);
                var direct=HudComputationRunner.Execute(request,computer,clock.Now);
                Check(worker.TrySubmit(request));HudComputationOutcome received=default;
                Until(()=>worker.TryTake(out received));Check(received.Input==direct.Input&&received.Anchor==direct.Anchor&&received.Error==null&&received.Executed);
                Check(computer.ThreadId!=Environment.CurrentManagedThreadId);
            }
            evidence["EquivalentInputs"]=128;worker.Dispose();Until(()=>Snapshot(worker).Exited);
        });
        Case("legacy-calculator-stays-on-main-despite-background-config",()=>{var c=new Legacy();using var f=new Fixture(c);f.Prepare();Check(f.Apply()&&c.ThreadId==Environment.CurrentManagedThreadId&&!f.Perf.Computation.Worker.Started&&f.Perf.Computation.EffectiveMode==HudComputationMode.MainThread);});
        Case("opt-in-calculator-runs-on-main-in-main-mode",()=>{var c=new Computer();using var f=new Fixture(c);f.Execution=HudExecutionOptions.Default;f.Prepare();Check(f.Apply()&&c.ThreadId==Environment.CurrentManagedThreadId&&!f.Perf.Computation.Worker.Started);});
        Case("real-pipeline-runs-only-pure-computation-on-worker",()=>{var c=new Computer();using var f=new Fixture(c);f.Ready();Check(c.ThreadId!=Environment.CurrentManagedThreadId&&f.Source.Calls>0&&f.Ui.Shows==1);});
        F("no-placeholder-until-first-result",(f,m)=>{f.Prepare();Check(m.Requests.Count==1&&!f.Apply());});
        F("valid-retained-result-used-while-next-job-is-pending",(f,m)=>{f.Prepare();Complete(f,m);long seq=f.Perf.LastAppliedSequence;for(int i=0;i<30;i++){f.Body=new((i%5)*.01f,0,0);f.Next();Check(f.Apply()&&f.Perf.LastAppliedSequence==seq);}Check(f.Perf.ReusedApplications>=30);});
        F("cached-data-still-uses-current-body-pose",(f,m)=>{f.Prepare();Complete(f,m);f.Body=new(.3f,0,0);f.Next();Check(f.Apply()&&MathF.Abs(f.Ui.Last.WorldAnchor.X-.95f)<.001);});
        F("latest-result-cannot-be-overwritten-by-out-of-order-result",(f,m)=>{f.Prepare();f.Next();f.Next();Check(m.Requests.Count>=3);m.Output=f.Outcome(m.Requests[2]);f.Next();Check(f.Apply());long newest=f.Perf.LastAppliedSequence;m.Output=f.Outcome(m.Requests[0]);f.Next();Check(f.Apply()&&f.Perf.LastAppliedSequence==newest&&f.Perf.RejectedResults>0);});
        F("duplicate-result-is-rejected",(f,m)=>{f.Prepare();Complete(f,m);var duplicate=f.Outcome(m.Requests[0]);m.Output=duplicate;f.Next();Check(f.Apply()&&f.Perf.RejectedResults==1);});
        F("unissued-future-sequence-is-rejected",(f,m)=>{f.Prepare();m.Output=f.Outcome(m.Requests[0]) with{Input=m.Requests[0].Input with{Stamp=m.Requests[0].Input.Stamp with{Sequence=9999}}};f.Next();Check(!f.Apply()&&f.Perf.RejectedResults==1);});
        F("future-completion-time-is-rejected",(f,m)=>{f.Prepare();m.Output=f.Outcome(m.Requests[0]) with{CompletedAtSeconds=f.Clock.Now()+1};f.Next();Check(!f.Apply());});
        F("invalid-anchor-payload-is-rejected",(f,m)=>{f.Prepare();m.Output=f.Outcome(m.Requests[0]) with{Anchor=new(new(float.NaN,0,0),0,12)};f.Next();Check(!f.Apply());});
        F("expired-completion-cannot-renew-age",(f,m)=>{f.Prepare();var old=f.Outcome(m.Requests[0]);f.Clock.Advance(300);m.Output=old;f.Next();Check(!f.Apply()&&f.Perf.Computation.ExpiredCompletions==1);});
        F("retained-result-expires-without-new-completion",(f,m)=>{f.Prepare();Complete(f,m);f.Next(300);Check(!f.Apply()&&f.Perf.ExpiredResults>0);});
        foreach(var reason in new[]{"epoch","eligibility","configuration","camera","subject","hud-config","session","context","model","floor","dimension","player"})
            F("old-completion-rejected-after-change-"+reason,(f,m)=>
            {f.Prepare();var old=f.Outcome(m.Requests[0]);if(reason=="epoch")f.Epoch++;else if(reason=="eligibility")f.View=f.View with{EligibilityEpoch=2};else if(reason=="configuration")f.View=f.View with{ConfigurationRevision=2};else if(reason=="camera")f.View=f.View with{Binding=f.View.Binding with{MainCamera=new(999,99)}};else if(reason=="subject")f.View=f.View with{Subject=new(new(998,98),997,1)};else if(reason=="hud-config")f.Geometry=f.Geometry with{AnchorSide=-.5f};else if(reason=="session")f.View=f.View with{Context=new(Guid.NewGuid(),1)};else if(reason=="context")f.View=f.View with{Context=f.View.Context with{Epoch=2}};else if(reason=="model")f.View=f.View with{Binding=f.View.Binding with{CharacterModel=new(996,96)}};else if(reason=="floor")f.View=f.View with{Binding=f.View.Binding with{FloorHandle=995}};else if(reason=="dimension")f.View=f.View with{Binding=f.View.Binding with{DimensionIndex=1}};else{var b=f.View.Binding with{LocalPlayer=new(new(994,94),993,1)};f.View=f.View with{Binding=b,Subject=b.LocalPlayer};}m.Output=old;f.Next();Check(!f.Apply());});
        F("late-error-from-older-sequence-does-not-disrupt-new-result",(f,m)=>{f.Prepare();f.Next();m.Output=f.Outcome(m.Requests[1]);f.Next();Check(f.Apply());long latest=f.Perf.LastAppliedSequence;m.Output=f.Outcome(m.Requests[0]) with{Error="late failure"};f.Next();Check(f.Apply()&&f.Perf.LastAppliedSequence==latest&&f.Perf.Computation.Errors==0);});
        F("current-computation-error-retains-good-result-and-resources",(f,m)=>{f.Prepare();Complete(f,m);f.Next();m.Output=f.Outcome(m.Requests[1]) with{Error="test failure"};f.Next();Check(f.Apply()&&f.Perf.Computation.Errors==1&&f.Ui.Releases==0&&f.Errors.Count==1);int count=m.Requests.Count;f.Next(100);Check(m.Requests.Count==count);f.Next(501);Check(m.Requests.Count>count);});
        F("cancellation-outcome-is-not-an-error",(f,m)=>{f.Prepare();m.Output=f.Outcome(m.Requests[0]) with{Cancelled=true};f.Next();Check(!f.Apply()&&f.Perf.Computation.CancelledCompletions==1&&f.Errors.Count==0);});
        F("executor-unavailable-uses-cooldown-without-resource-release",(f,m)=>{m.Reject=true;f.Prepare();Check(f.Perf.Computation.Errors==1&&f.Ui.Releases==0);f.Next(10);Check(f.Perf.Computation.Errors==1);m.Reject=false;f.Next(501);Check(m.Requests.Count>0);});
        F("main-mode-merge-has-no-worker-wait",(f,m)=>{f.Prepare();f.Execution=HudExecutionOptions.Default;f.Next();Check(f.Apply()&&f.Perf.Computation.EffectiveMode==HudComputationMode.MainThread&&m.Cancels>0);});
        F("old-background-completion-after-mode-switch-is-rejected",(f,m)=>{f.Prepare();var old=f.Outcome(m.Requests[0]);f.Execution=HudExecutionOptions.Default;f.Next();Check(f.Apply());long seq=f.Perf.LastAppliedSequence;f.Execution=new(HudComputationMode.Background,.5);m.Output=old;f.Next();m.Output=old;f.Next();Check(f.Apply()&&f.Perf.LastAppliedSequence==seq&&f.Perf.RejectedResults>0);});
        F("budget-zero-defers-merge-without-waiting",(f,m)=>{f.Prepare();m.Output=f.Outcome(m.Requests[0]);f.Work=f.Work with{FrameBudgetMilliseconds=0};f.Next();Check(m.Output.HasValue&&!f.Apply());f.Work=f.Work with{FrameBudgetMilliseconds=5};f.Next();Check(f.Apply());});
        F("stale-render-cannot-draw-valid-background-data",(f,m)=>{f.Prepare();Complete(f,m);var display=f.Display;f.Next();Check(!f.Data.TryApply(f.Current,display,f.Epoch,out _));});
        F("one-completion-drained-per-update",(f,m)=>{f.Prepare();Complete(f,m);Check(f.Perf.Computation.AcceptedResults==1);});
        F("third-person-config-change-cancels-old-dispatch",(f,m)=>{f.Prepare();int old=m.Cancels;f.View=f.View with{ConfigurationRevision=2};f.Next();Check(m.Cancels>old&&m.Requests.Last().Input.Stamp.View.ConfigurationRevision==2);});
        F("dispose-cancels-transport-and-rejects-further-work",(f,m)=>{f.Prepare();f.Data.Dispose();int n=m.Requests.Count;f.Next();Check(!f.Apply()&&m.Disposed&&m.Requests.Count==n);});
        Case("real-worker-cooperative-cancel-ends-running-calculation",()=>
        {var clock=new Clock();var c=new Computer{Block=true,Cooperative=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();var request=new HudComputationRequest(new(new(f.View,1,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25);w.TrySubmit(request);Check(c.Entered.Wait(2000));w.Cancel();Until(()=>Snapshot(w).Running==0);Check(!w.TryTake(out _)&&Snapshot(w).Cancelled>=1);w.Dispose();Until(()=>Snapshot(w).Exited);});
        Case("real-config-change-cooperatively-cancels-old-running-job",()=>
        {var c=new Computer{Block=true,Cooperative=true};using var f=new Fixture(c);try{f.Prepare();Check(c.Entered.Wait(2000));f.View=f.View with{ConfigurationRevision=2};f.Next();Until(()=>f.Perf.Computation.Worker.Cancelled>=1);Check(!f.Apply());}finally{c.Release.Set();}});
        Case("real-worker-noncooperative-cancel-and-dispose-never-join",()=>
        {var clock=new Clock();var c=new Computer{Block=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();try{w.TrySubmit(new(new(new(f.View,1,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25));Check(c.Entered.Wait(2000));var time=Stopwatch.StartNew();w.Cancel();w.Dispose();Check(time.ElapsedMilliseconds<500&&Snapshot(w).Running==1&&!w.TryTake(out _));}finally{c.Release.Set();Until(()=>Snapshot(w).Exited);} });
        Case("real-worker-bounded-latest-pending-input-under-10000-submits",()=>
        {var clock=new Clock();var c=new Computer{Block=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();try{for(int i=1;i<=10001;i++){w.TrySubmit(new(new(new(f.View,i,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25));if(i==1)Check(c.Entered.Wait(2000));}var p=Snapshot(w);Check(p.Running==1&&p.Pending==1&&p.ReplacedInputs==9999&&p.ThreadStarts==1);evidence["Submissions10001"]=p;w.Cancel();Check(Snapshot(w).Pending==0);}finally{c.Release.Set();w.Dispose();Until(()=>Snapshot(w).Exited);} });
        Case("real-worker-skips-pending-job-after-deadline",()=>
        {var clock=new Clock();var c=new Computer{Block=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();try{var s=new HudSample(new(f.View,1,100,clock.Now()),1,1,1,.5f,12);w.TrySubmit(new(s,clock.Now()+.25));Check(c.Entered.Wait(2000));w.TrySubmit(new(s with{Stamp=s.Stamp with{Sequence=2}},clock.Now()+.25));clock.Advance(300);c.Release.Set();Until(()=>Snapshot(w).Completed>=2&&Snapshot(w).Pending==0&&Snapshot(w).Running==0);Check(c.Calls==1&&Snapshot(w).Expired==2);HudComputationOutcome o=default;Check(w.TryTake(out o)&&o.Expired&&!o.Executed);}finally{c.Release.Set();w.Dispose();Until(()=>Snapshot(w).Exited);} });
        Case("real-worker-error-isolated-and-next-job-succeeds",()=>
        {var clock=new Clock();var c=new Computer{Fail=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();var s=new HudSample(new(f.View,1,100,clock.Now()),1,1,1,.5f,12);w.TrySubmit(new(s,clock.Now()+.25));HudComputationOutcome bad=default;Until(()=>w.TryTake(out bad));Check(bad.Error!=null&&Snapshot(w).Errors==1);c.Fail=false;w.TrySubmit(new(s with{Stamp=s.Stamp with{Sequence=2}},clock.Now()+.25));HudComputationOutcome good=default;Until(()=>w.TryTake(out good));Check(good.Error==null&&good.Executed&&Snapshot(w).ThreadStarts==1);w.Dispose();Until(()=>Snapshot(w).Exited);});
        Case("real-worker-invalid-output-is-reported-as-calculation-error",()=>
        {var clock=new Clock();var c=new Computer{Invalid=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();w.TrySubmit(new(new(new(f.View,1,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25));HudComputationOutcome bad=default;Until(()=>w.TryTake(out bad));Check(bad.Error!=null&&Snapshot(w).Errors==1);w.Dispose();Until(()=>Snapshot(w).Exited);});
        Case("real-worker-results-mailbox-is-bounded-and-keeps-latest",()=>
        {var clock=new Clock();var c=new Computer();using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();for(int i=1;i<=100;i++){w.TrySubmit(new(new(new(f.View,i,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25));long expected=i;Until(()=>Snapshot(w).Completed>=expected);}Check(Snapshot(w).CompletedMailbox==1&&Snapshot(w).ReplacedCompletions==99);Check(w.TryTake(out var o)&&o.Input.Stamp.Sequence==100);w.Dispose();Until(()=>Snapshot(w).Exited);});
        Case("real-worker-rapid-cancel-reuses-one-thread",()=>
        {var clock=new Clock();var c=new Computer{Block=true};using var w=new HudBackgroundExecutor(c,clock.Now);using var f=new Fixture();try{w.TrySubmit(new(new(new(f.View,1,100,clock.Now()),1,1,1,.5f,12),clock.Now()+.25));Check(c.Entered.Wait(2000));for(int i=2;i<=2001;i++){w.Cancel();w.TrySubmit(new(new(new(f.View,i,100,clock.Now()),i,1,1,.5f,12),clock.Now()+.25));Check(Snapshot(w).Pending==1);}Check(Snapshot(w).ThreadStarts==1);evidence["CancelResubmit2000"]=Snapshot(w);}finally{c.Release.Set();w.Dispose();Until(()=>Snapshot(w).Exited);} });
        Case("main-uses-good-result-during-slow-worker-for-2000-frames",()=>
        {var c=new Computer();using var f=new Fixture(c);try{f.Work=f.Work with{RefreshIntervalSeconds=.1};f.Ready();long old=f.Perf.LastAppliedSequence;c.Block=true;f.Next(100);Check(c.Entered.Wait(2000));f.Next(100);var timer=Stopwatch.StartNew();for(int i=0;i<2000;i++){f.Body=new((i%10)*.001f,0,0);f.Next(.001);Check(f.Apply()&&f.Perf.LastAppliedSequence==old&&f.Perf.PendingInputs<=1);}Check(timer.ElapsedMilliseconds<1000);evidence["Nonblocking2000Frames"]=new{ElapsedMilliseconds=timer.ElapsedMilliseconds,f.Perf.Computation.Worker.Running,f.Perf.PendingInputs,LastAppliedSequence=f.Perf.LastAppliedSequence};}finally{c.Release.Set();} });
        Case("mode-switch-main-result-wins-over-real-late-worker",()=>
        {var c=new Computer{Block=true};using var f=new Fixture(c);try{f.Prepare();Check(c.Entered.Wait(2000));f.Execution=HudExecutionOptions.Default;f.Next();Check(f.Apply());long newest=f.Perf.LastAppliedSequence;c.Release.Set();Until(()=>f.Perf.Computation.Worker.Running==0);f.Execution=new(HudComputationMode.Background,.5);f.Next();Check(f.Apply()&&f.Perf.LastAppliedSequence==newest&&f.Perf.Computation.Worker.ThreadStarts==1);}finally{c.Release.Set();} });
        Case("off-main-cannot-submit-take-reset-or-read-worker",()=>
        {var clock=new Clock();using var w=new HudBackgroundExecutor(new Computer(),clock.Now);Task.Run(()=>{Check(!w.TrySubmit(default)&&!w.TryTake(out _)&&!w.TryGetSnapshot(out _));w.Cancel();w.Dispose();}).GetAwaiter().GetResult();Check(!Snapshot(w).StopRequested);w.Dispose();Check(Snapshot(w).Exited);});
        Case("cancel-after-completion-clears-mailbox",()=>
        {using var f=new Fixture();f.Prepare();Until(()=>f.Perf.Computation.Worker.CompletedMailbox==1);f.Data.Cancel();Check(f.Perf.Computation.Worker.CompletedMailbox==0&&!f.Apply());});
        Case("submit-after-dispose-does-not-start-a-thread",()=>{var clock=new Clock();using var w=new HudBackgroundExecutor(new Computer(),clock.Now);w.Dispose();Check(!w.TrySubmit(default)&&Snapshot(w).ThreadStarts==0&&Snapshot(w).Exited);});
        Case("100-mode-switches-reuse-single-background-thread",()=>
        {using var f=new Fixture();f.Ready();for(int i=0;i<100;i++){f.Execution=HudExecutionOptions.Default;f.Next();Check(f.Apply());f.Execution=new(HudComputationMode.Background,.5);f.Next();Check(f.Apply());}Check(f.Perf.Computation.Worker.ThreadStarts==1&&f.Perf.Computation.ModeChanges==201);evidence["ModeSwitches100"]=f.Perf.Computation;});
        Case("real-stopwatch-worker-timings-are-recorded",()=>
        {static double Now()=>(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;using var f=new Fixture();using var w=new HudBackgroundExecutor(new PlaceholderDataComputer(),Now);for(int i=1;i<=64;i++){double now=Now();w.TrySubmit(new(new(new(f.View,i,100,now),1,1,1,.5f,12),now+.25));HudComputationOutcome o=default;Until(()=>w.TryTake(out o));Check(o.Error==null);}var p=Snapshot(w);Check(p.Executed==64&&p.TotalCalculationMilliseconds>0&&p.MaximumCalculationMilliseconds>0);evidence["StopwatchWorkerTimings"]=p;w.Dispose();Until(()=>Snapshot(w).Exited);});
        Case("production-runtime-background-path-and-stop-clean-resources",()=>RuntimeTest(false));
        Case("production-runtime-main-path-equals-background-placement",()=>RuntimeTest(true));
        Case("production-background-500-view-switches-reuse-thread-and-resources",()=>RuntimeTest(false,500));
        bool passed=results.All(r=>r.Passed);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,RealManagedThreadsExecuted=true,RuntimeRenderingVerified=false,
            Evidence=evidence,Tests=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var t in results.Where(t=>!t.Passed))Console.WriteLine(t.Name+": "+t.Error);
        Console.WriteLine($"Async execution / parity / adversarial results: {results.Count}; passed={passed}");return passed?0:1;
    }
    static void RuntimeTest(bool mainMode,int switches=0)
    {
        U.Object.Created.Clear();U.GUI.Draws=0;U.GUI.Drawing=null;U.Camera.current=null;U.Event.current=new(){type=U.EventType.Repaint};
        var clock=new Clock();using var seed=new Fixture();var view=seed.View;int frame=1;
        var live=new BridgeObservation(view.Binding,view.Subject,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,frame,clock.Now());
        using var provider=new ThirdPersonViewProvider(()=>live with{FrameNumber=frame,NowSeconds=clock.Now()});
        ThirdPersonApi.Provider=provider;GameStateManager.CurrentStateName=eGameStateName.InLevel;Plugin.Instance=new();
        Plugin.Instance.Settings.Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
        Plugin.Instance.Settings.ExecutionOptions=new(mainMode?HudComputationMode.MainThread:HudComputationMode.Background,.5);
        var runtime=new HudRuntime(IntPtr.Zero);runtime.Initialize(clock.Now,legacyMarker:true);HudPerformanceSnapshot last=default;
        try
        {
            Until(()=>
            {
                frame++;clock.Advance(1);U.Time.frameCount=frame;ThirdPersonApi.MonotonicSeconds=clock.Now();runtime.Update();
                provider.BeginMainAttempt();provider.PublishMain(seed.Display.Camera,seed.Display.BodyPose);provider.MainRestored(true);provider.NativeMainCompleted(true);runtime.OnGUI();
                runtime.TryGetPerformance(out last);return U.GUI.Draws>0;
            });
            Check(U.Object.Created.Count==2&&last.Computation.EffectiveMode==(mainMode?HudComputationMode.MainThread:HudComputationMode.Background));
            var rect=U.GUI.LastRect;if(evidence.TryGetValue("RuntimePlacement",out var previous))Check(previous is float[] old&&MathF.Abs(old[0]-rect.x)<.001&&MathF.Abs(old[1]-rect.y)<.001);else evidence["RuntimePlacement"]=new[]{rect.x,rect.y,rect.width,rect.height};
            if(!mainMode)Check(last.Computation.Worker.ThreadStarts==1);
            void RuntimeFrame(bool own)
            {
                live=live with{Source=own?ViewSource.LocalThirdPerson:ViewSource.BotControl,LocalBodyViewActive=own,
                    Availability=own?ViewAvailability.Ready:ViewAvailability.ControlTaken};
                frame++;clock.Advance(1);U.Time.frameCount=frame;ThirdPersonApi.MonotonicSeconds=clock.Now();runtime.Update();
                if(own){provider.BeginMainAttempt();provider.PublishMain(seed.Display.Camera,seed.Display.BodyPose);provider.MainRestored(true);provider.NativeMainCompleted(true);}
                runtime.OnGUI();runtime.TryGetPerformance(out last);Check(last.PendingInputs<=1);
            }
            for(int i=0;i<switches;i++){int before=U.GUI.Draws;RuntimeFrame(false);Check(U.GUI.Draws==before);RuntimeFrame(true);RuntimeFrame(true);}
            Check(U.Object.Created.Count==2);
            if(switches>0){Check(last.Computation.Worker.ThreadStarts==1);evidence["ProductionBackgroundSwitches500"]=new{ObjectsCreated=U.Object.Created.Count,last.PendingInputs,last.Computation.Worker.ThreadStarts,last.CancelledResults};}
        }
        finally
        {
            runtime.Stop();Until(()=>{runtime.TryGetPerformance(out var p);return !p.Computation.Worker.Started||p.Computation.Worker.Exited;});ThirdPersonApi.Provider=null!;
        }
        Check(U.Object.Created.All(o=>o.Destroyed)&&provider.SubscriberErrors==0);
    }
}
