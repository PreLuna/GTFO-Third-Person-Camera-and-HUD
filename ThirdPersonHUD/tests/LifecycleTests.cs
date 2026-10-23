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

internal static class LifecycleTests
{
    sealed record Result(string Name, bool Passed, string? Error);
    static readonly List<Result> results = new();
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    static void Case(string name, Action test)
    { try { test(); results.Add(new(name,true,null)); } catch(Exception e) { results.Add(new(name,false,e.ToString())); } }
    sealed class FakePresentation : IHudPresentation
    {
        public int InitializationStep {get; private set;}
        public bool HasResources => InitializationStep > 0;
        public bool IsHealthy {get;set;} = true;
        public bool IsReady => InitializationStep == 3 && IsHealthy;
        public bool Visible; public int Releases, Binds; public string? ThrowAt;
        public HudPlacement Placement;
        public void InitializeNextStep(){if(ThrowAt=="init"){ThrowAt=null;throw new Exception("init");} InitializationStep++;}
        public void Bind(ContextStamp c,LocalViewBinding b){Binds++;Visible=false;}
        public void Show(in HudPlacement p){if(ThrowAt=="show"){ThrowAt=null;throw new Exception("show");}Placement=p;Visible=true;}
        public void Hide(){Visible=false;}
        public void Release(){if(HasResources)Releases++;InitializationStep=0;IsHealthy=true;Visible=false;}
    }
    sealed class Fixture : IDisposable
    {
        public BridgeObservation Live;
        public ThirdPersonViewProvider Provider;
        public readonly FakePresentation Backend = new();
        public readonly HudLifecycle Hud;
        public HudLifecycleOptions Options = HudLifecycleOptions.Default;
        public CameraSnapshot Camera = new(new(new(0,1,3),Quaternion.Identity),Matrix4x4.CreateTranslation(0,-1,-3),
            Matrix4x4.CreatePerspectiveFieldOfView(1.2f,16f/9,.08f,250),new(0,0,1920,1080));
        public WorldPose Body = new(Vector3.Zero,Quaternion.Identity);
        public Fixture()
        {
            var player=new PlayerIdentity(new(100,1),200,0);
            var binding=new LocalViewBinding(player,new(300,2),new(400,3),500,0);
            Live=new(binding,player,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,100,10);
            Hud=new(Backend, clock:()=>Live.NowSeconds);Provider=new(()=>Live);Provider.StateChanged+=s=>Hud.ObserveState(s);
            Provider.RenderChanged+=n=>{if(n.Render.Phase==RenderPhase.MainCommitted&&Provider.TryGetDisplayFrame(new(Live.FrameNumber,HudDisplayUsage.ScreenComposition),out var d)&&Provider.TryGetRuntimeContext(out var c))Hud.ObserveCommitted(c,d);else Hud.HideForRender();};
        }
        public HudLifecycleStatus Status {get{Hud.TryGetStatus(out var s);return s;}}
        public HudRuntimeContext Current {get{Provider.TryGetRuntimeContext(out var c);return c;}}
        public ThirdPersonDisplayFrame Display {get{Provider.TryGetDisplayFrame(new(Live.FrameNumber,HudDisplayUsage.ScreenComposition),out var d);return d;}}
        public void Tick(bool available=true){var c=available?Current:default;Hud.Tick(new(Live.InLevel,Live.FrameNumber,Live.NowSeconds,available,c),Options);}
        public void Commit(){Provider.BeginMainAttempt();Check(Provider.PublishMain(Camera,Body),"publish failed");Provider.MainRestored(true);Provider.NativeMainCompleted(true);}
        public void Next(double delta=.02,bool commit=true){Live=Live with{FrameNumber=Live.FrameNumber+1,NowSeconds=Live.NowSeconds+delta};Tick();if(commit)Commit();}
        public void Ready(){Options=Options with{ActivationDelaySeconds=0};Tick();Commit();Next();Next();Next();Check(Status.Visible&&Backend.InitializationStep==3,"not ready");}
        public void Dispose(){Hud.Dispose();Provider.Dispose();}
    }
    static void CoreCase(string name,Action<Fixture> test)=>Case(name,()=>{using var f=new Fixture();test(f);});
    static void RuntimeCase(string name,Action<Fixture,HudRuntime> test)=>Case(name,()=>
    {
        using var f=new Fixture();
        U.Object.Created.Clear();U.Object.FailAt=null;U.GUI.Draws=0;U.Camera.current=null;U.Event.current=new(){type=U.EventType.Repaint};
        GameStateManager.CurrentStateName=eGameStateName.InLevel;
        ThirdPersonApi.Provider=f.Provider;Plugin.Instance=new();Plugin.Instance.Settings.Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
        var runtime=new HudRuntime(IntPtr.Zero);runtime.Initialize(()=>ThirdPersonApi.MonotonicSeconds,legacyMarker:true);
        try{test(f,runtime);}
        finally{runtime.Stop();ThirdPersonApi.Provider=null!;U.Object.FailAt=null;}
        Check(U.Object.Created.All(o=>o.Destroyed),"owned Unity resource leaked");
        Check(f.Provider.SubscriberErrors==0,"provider subscriber exception");
    });
    static void RuntimeUpdate(Fixture f,HudRuntime r)
    {U.Time.frameCount=f.Live.FrameNumber;ThirdPersonApi.MonotonicSeconds=f.Live.NowSeconds;r.Update();}
    static void RuntimeNext(Fixture f,HudRuntime r,bool commit=true)
    {f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+1,NowSeconds=f.Live.NowSeconds+.02};RuntimeUpdate(f,r);if(commit)f.Commit();}
    static void RuntimeReady(Fixture f,HudRuntime r)
    {RuntimeUpdate(f,r);f.Commit();RuntimeNext(f,r);RuntimeNext(f,r);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==1,"runtime marker not drawn");}
    static int Main(string[] args)
    {
        CoreCase("no-initialization-before-actual-third-person-frame",f=>{for(int i=0;i<20;i++)f.Next(.02,false);Check(f.Status.InitializationSteps==0&&!f.Status.HasResources);});
        CoreCase("delay-starts-at-first-committed-frame",f=>{f.Tick();f.Commit();for(int i=0;i<3;i++)f.Next(.05);Check(f.Status.InitializationSteps==0);f.Next(.05);Check(f.Status.InitializationSteps==1);});
        CoreCase("only-one-init-step-per-frame",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();f.Commit();f.Next();for(int i=0;i<20;i++)f.Tick();Check(f.Status.InitializationSteps==1);});
        CoreCase("three-steps-across-three-updates",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();f.Commit();for(int i=1;i<=3;i++){f.Next();Check(f.Status.InitializationSteps==i&&f.Status.Visible==(i==3));}});
        CoreCase("render-callbacks-never-initialize",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();for(int i=0;i<30;i++)f.Commit();Check(f.Status.InitializationSteps==0);});
        CoreCase("ready-marker-has-current-render-stamps",f=>{f.Ready();Check(f.Hud.CanDraw(f.Current,f.Display));Check(f.Backend.Placement.DisplaySequence==f.Display.Sequence);});
        CoreCase("new-frame-hides-old-marker",f=>{f.Ready();f.Next(.02,false);Check(!f.Status.Visible&&!f.Hud.CanDraw(f.Current,f.Display));});
        CoreCase("main-active-is-not-screen-display",f=>{f.Ready();f.Provider.BeginMainAttempt();f.Provider.PublishMain(f.Camera,f.Body);Check(!f.Status.Visible);});
        CoreCase("main-restored-before-native-end-remains-hidden",f=>{f.Ready();f.Provider.BeginMainAttempt();f.Provider.PublishMain(f.Camera,f.Body);f.Provider.MainRestored(true);Check(!f.Status.Visible);f.Provider.NativeMainCompleted(true);Check(f.Status.Visible);});
        foreach(var source in Enum.GetValues<ViewSource>().Where(s=>s!=ViewSource.LocalThirdPerson))
            CoreCase("source-revoked-"+source,f=>{f.Ready();f.Live=f.Live with{Source=source};_=f.Current;Check(!f.Status.Visible&&f.Status.HasResources);});
        foreach(var availability in Enum.GetValues<ViewAvailability>().Where(a=>a!=ViewAvailability.Ready&&a!=ViewAvailability.OutOfLevel))
            CoreCase("pause-keeps-ready-resources-"+availability,f=>{f.Ready();f.Live=f.Live with{Availability=availability};_=f.Current;Check(!f.Status.Visible&&f.Status.HasResources&&f.Backend.Releases==0);});
        CoreCase("first-person-aim-reuses-without-reinitializing",f=>{f.Ready();f.Live=f.Live with{Source=ViewSource.LocalFirstPerson,Mode=ThirdPersonMode.FirstPersonAim,Availability=ViewAvailability.TemporaryFallback};f.Tick();f.Live=f.Live with{Source=ViewSource.LocalThirdPerson,Mode=ThirdPersonMode.Exploration,Availability=ViewAvailability.Ready};f.Next();Check(!f.Status.Visible);f.Next();Check(f.Status.Visible&&f.Status.InitializationSteps==3&&f.Backend.Releases==0);});
        CoreCase("half-initialized-resources-survive-pause",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();f.Commit();f.Next();f.Live=f.Live with{FocusAllowed=false};f.Tick();Check(f.Backend.InitializationStep==1);f.Live=f.Live with{FocusAllowed=true};f.Commit();f.Next();f.Next();Check(f.Status.Visible&&f.Status.InitializationSteps==3&&f.Backend.Releases==0);});
        CoreCase("partial-resume-restarts-delay",f=>{f.Tick();f.Commit();f.Next(.21);Check(f.Backend.InitializationStep==1);f.Live=f.Live with{FocusAllowed=false};f.Tick();f.Live=f.Live with{FocusAllowed=true};f.Commit();f.Next(.05);Check(f.Backend.InitializationStep==1);f.Next(.16);Check(f.Backend.InitializationStep==2);});
        CoreCase("auxiliary-pass-hides-and-restores-without-pause",f=>{f.Ready();var before=f.Status;f.Provider.AuxiliaryBegin(new(800,8));Check(!f.Status.Visible);f.Provider.AuxiliaryEnd(new(800,8));Check(f.Status.Visible&&f.Status.Pauses==before.Pauses&&f.Status.WorkEpoch==before.WorkEpoch);});
        CoreCase("nested-preview-does-not-show-before-stack-unwinds",f=>{f.Ready();f.Provider.AuxiliaryBegin(new(800,8));f.Provider.AuxiliaryBegin(new(801,9));f.Provider.AuxiliaryEnd(new(801,9));Check(!f.Status.Visible);f.Provider.AuxiliaryEnd(new(800,8));Check(f.Status.Visible);});
        CoreCase("failed-native-render-cannot-show",f=>{f.Ready();f.Provider.BeginMainAttempt();f.Provider.PublishMain(f.Camera,f.Body);f.Provider.MainRestored(true);f.Provider.NativeMainCompleted(false);Check(!f.Status.Visible&&f.Status.HasResources);});
        CoreCase("lost-provider-pauses-with-resources",f=>{f.Ready();f.Tick(false);Check(!f.Status.Visible&&f.Status.HasResources);f.Next();Check(!f.Status.Visible);f.Next();Check(f.Status.Visible&&f.Backend.Releases==0);});
        foreach(var part in new[]{"model","camera","player","dimension","floor"})
            CoreCase("rebind-"+part,f=>
            {f.Ready();var old=f.Display;var b=f.Live.Binding;b=part switch{"model"=>b with{CharacterModel=new(901,10)},"camera"=>b with{MainCamera=new(902,11)},"player"=>b with{LocalPlayer=b.LocalPlayer with{Agent=new(903,12)}},"dimension"=>b with{DimensionIndex=1},_=>b with{FloorHandle=900}};
                f.Live=f.Live with{Binding=b,Subject=b.LocalPlayer};f.Commit();Check(!f.Status.Visible&&!f.Hud.CanDraw(f.Current,old));f.Next();if(part=="floor"){f.Next();f.Next();Check(f.Backend.Releases==1&&f.Status.InitializationSteps==6);}else Check(f.Backend.Releases==0&&f.Status.InitializationSteps==3);Check(f.Status.Visible&&f.Backend.Binds>=2);});
        CoreCase("provider-session-reload-rebuilds-owned-resources",f=>{f.Ready();var c=f.Current;var view=c.CurrentView with{Context=new(Guid.NewGuid(),1)};c=c with{ExpectedContext=view.Context,CurrentView=view};f.Hud.Tick(new(true,c.FrameNumber,c.NowSeconds,true,c),f.Options);Check(f.Backend.Releases==1&&!f.Status.Visible&&!f.Status.HasResources);});
        CoreCase("outlevel-notification-cleans-immediately",f=>{f.Ready();f.Provider.ResetContext();Check(!f.Status.HasResources&&f.Status.Phase==HudLifecyclePhase.Dormant);});
        CoreCase("outlevel-update-cleans-with-no-provider",f=>{f.Ready();f.Live=f.Live with{InLevel=false};f.Tick(false);Check(!f.Status.HasResources&&f.Backend.Releases==1);});
        CoreCase("disable-releases-and-enable-rebuilds",f=>{f.Ready();f.Options=f.Options with{Enabled=false};f.Tick();Check(!f.Status.HasResources);f.Options=f.Options with{Enabled=true};f.Tick();f.Commit();f.Next();f.Next();f.Next();Check(f.Status.Visible&&f.Status.InitializationSteps==6);});
        CoreCase("destroyed-resource-forces-clean-reinitialization",f=>{f.Ready();f.Backend.IsHealthy=false;f.Next();f.Next();f.Next();Check(f.Status.Visible&&f.Status.InitializationSteps==6&&f.Backend.Releases==1);});
        CoreCase("init-error-cleans-and-obeys-retry-delay",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();f.Commit();f.Backend.ThrowAt="init";f.Next();Check(f.Status.Phase==HudLifecyclePhase.Faulted&&f.Status.Errors==1);for(int i=0;i<10;i++)f.Next(.05);Check(f.Status.InitializationSteps==0);f.Next(.51);f.Next();f.Next();f.Next();Check(f.Status.Visible&&f.Status.InitializationSteps==3);});
        CoreCase("show-error-cleans-rebinds-and-recovers",f=>{f.Ready();f.Backend.ThrowAt="show";f.Next();Check(f.Status.Phase==HudLifecyclePhase.Faulted&&!f.Status.HasResources);f.Next(1.01);f.Next();f.Next();f.Next();Check(f.Status.Visible&&f.Status.Errors==1);});
        CoreCase("evidence-older-than-half-second-cannot-start-work",f=>{f.Options=f.Options with{ActivationDelaySeconds=0};f.Tick();f.Commit();f.Next(.6,false);Check(f.Status.InitializationSteps==0);});
        CoreCase("missing-more-than-one-render-frame-pauses-preparation",f=>{f.Tick();f.Commit();f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+3,NowSeconds=f.Live.NowSeconds+.1};f.Tick();Check(f.Status.InitializationSteps==0&&f.Status.LastEvidenceFrame==-1);});
        CoreCase("expired-display-cannot-draw",f=>{f.Ready();var d=f.Display;f.Live=f.Live with{NowSeconds=f.Live.NowSeconds+.2};Check(!f.Hud.CanDraw(f.Current,d));});
        CoreCase("old-pass-cannot-draw-after-same-frame-new-pass",f=>{f.Ready();var d=f.Display;f.Commit();Check(!f.Hud.CanDraw(f.Current,d)&&f.Hud.CanDraw(f.Current,f.Display));});
        CoreCase("foreign-subject-hides-immediately",f=>{f.Ready();f.Live=f.Live with{Subject=new(new(999,99),998,1)};_=f.Current;Check(!f.Status.Visible);});
        CoreCase("repeated-dispose-is-idempotent",f=>{f.Ready();f.Hud.Dispose();f.Hud.Dispose();f.Tick();f.Commit();Check(f.Status.Phase==HudLifecyclePhase.Disposed&&f.Backend.Releases==1&&!f.Status.HasResources);});
        CoreCase("worker-thread-cannot-mutate-or-read-resources",f=>{f.Ready();var before=f.Status;var c=f.Current;var d=f.Display;Task.Run(()=>{Check(!f.Hud.TryGetStatus(out _));f.Hud.Tick(new(false,0,0,false,default),f.Options);f.Hud.ObserveState(default);f.Hud.ObserveCommitted(c,d);f.Hud.HideForRender();f.Hud.Dispose();Check(!f.Hud.CanDraw(c,d));}).GetAwaiter().GetResult();Check(f.Status==before);});
        Case("configuration-sanitizes-nonfinite-and-range",()=>{var o=new HudLifecycleOptions(true,double.NaN,-5,float.NaN,99,0).Sanitized();Check(o.ActivationDelaySeconds==.2&&o.RetryDelaySeconds==.25&&o.AnchorHeight==1.2f&&o.AnchorSide==2&&o.MarkerSizePixels==4);});
        CoreCase("projection-is-relative-to-body-and-final-camera-right",f=>{f.Ready();Check(Vector3.Distance(f.Backend.Placement.WorldAnchor,new(.65f,1.2f,0))<.0001);Check(f.Backend.Placement.PixelCenter.X>960&&f.Backend.Placement.PixelCenter.Y>540);});
        CoreCase("viewport-origin-is-included",f=>{f.Ready();var d=f.Display;HudProjection.TryPlace(d,f.Options,out var a);d=d with{Camera=d.Camera with{Viewport=new(100,200,1920,1080)}};Check(HudProjection.TryPlace(d,f.Options,out var b));Check(Vector2.Distance(b.PixelCenter,a.PixelCenter+new Vector2(100,200))<.01);});
        CoreCase("rotated-camera-changes-side-offset",f=>{f.Ready();var d=f.Display;d=d with{Camera=d.Camera with{Pose=d.Camera.Pose with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2)}}};Check(HudProjection.TryPlace(d,f.Options,out var p));Check(MathF.Abs(p.WorldAnchor.X)<.001&&p.WorldAnchor.Z<-.64);});
        foreach(var position in new[]{new Vector3(0,0,10),new Vector3(100,0,0),new Vector3(0,100,0),new Vector3(float.NaN,0,0),new Vector3(0,0,-1000)})
            CoreCase("projection-rejects-offscreen-or-invalid-"+position,f=>{f.Ready();var d=f.Display with{BodyPose=new(position,Quaternion.Identity)};Check(!HudProjection.TryPlace(d,f.Options,out _));});
        CoreCase("projection-does-not-reuse-last-valid-position",f=>{f.Ready();f.Body=new(new(100,0,0),Quaternion.Identity);f.Next();Check(!f.Status.Visible&&f.Status.HasResources);});
        RuntimeCase("production-runtime-builds-three-steps-and-draws",(f,r)=>{RuntimeReady(f,r);Check(U.Object.Created.Count==2);Check(U.Object.Created.OfType<U.GameObject>().All(o=>!o.active));Check(U.Object.Created.OfType<U.Texture2D>().Single().Uploads==1);});
        RuntimeCase("production-marker-gui-y-flips-from-bottom-left",(f,r)=>{RuntimeReady(f,r);HudProjection.TryPlace(f.Display,Plugin.Instance.Settings.Read(),out var p);Check(MathF.Abs(U.GUI.LastRect.y-(1080-p.PixelCenter.Y-p.SizePixels/2))<.001);});
        RuntimeCase("production-onGUI-does-not-allocate-or-reinitialize",(f,r)=>{RuntimeReady(f,r);int objects=U.Object.Created.Count;for(int i=0;i<20;i++)r.OnGUI();Check(U.Object.Created.Count==objects&&U.Object.Created.OfType<U.Texture2D>().Single().Uploads==1);});
        RuntimeCase("production-layout-and-key-events-do-not-draw",(f,r)=>{RuntimeReady(f,r);U.Event.current.type=U.EventType.Layout;r.OnGUI();U.Event.current.type=U.EventType.KeyDown;r.OnGUI();Check(U.GUI.Draws==1);});
        RuntimeCase("production-preview-hides-and-restores",(f,r)=>{RuntimeReady(f,r);f.Provider.AuxiliaryBegin(new(800,8));r.OnGUI();Check(U.GUI.Draws==1);f.Provider.AuxiliaryEnd(new(800,8));r.OnGUI();Check(U.GUI.Draws==2);});
        RuntimeCase("production-texture-target-camera-never-draws",(f,r)=>{RuntimeReady(f,r);U.Camera.current=new(){targetTexture=new object()};r.OnGUI();Check(U.GUI.Draws==1);});
        RuntimeCase("production-old-frame-never-draws",(f,r)=>{RuntimeReady(f,r);f.Live=f.Live with{FrameNumber=f.Live.FrameNumber+1,NowSeconds=f.Live.NowSeconds+.02};U.Time.frameCount=f.Live.FrameNumber;r.OnGUI();Check(U.GUI.Draws==1);});
        RuntimeCase("production-control-hides-and-ready-reuses",(f,r)=>{RuntimeReady(f,r);f.Live=f.Live with{Source=ViewSource.BotControl,LocalBodyViewActive=false};RuntimeNext(f,r,false);r.OnGUI();Check(U.GUI.Draws==1&&U.Object.Created.All(o=>!o.Destroyed));f.Live=f.Live with{Source=ViewSource.LocalThirdPerson,LocalBodyViewActive=true};RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==1);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==2&&U.Object.Created.Count==2);});
        RuntimeCase("production-provider-disconnect-removes-subscriptions",(f,r)=>{RuntimeReady(f,r);ThirdPersonApi.Provider=null!;RuntimeNext(f,r,false);f.Commit();r.TryGetStatus(out var s);Check(!s.Visible);ThirdPersonApi.Provider=f.Provider;RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==1);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==2&&U.Object.Created.Count==2);});
        RuntimeCase("production-camera-rebind-reuses-resources-after-fresh-frame",(f,r)=>{RuntimeReady(f,r);var binding=f.Live.Binding with{MainCamera=new(999,99)};f.Live=f.Live with{Binding=binding};f.Commit();r.OnGUI();Check(U.GUI.Draws==1);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==2&&U.Object.Created.Count==2);});
        RuntimeCase("production-provider-new-session-rebuilds-and-unsubscribes-old",(f,r)=>{RuntimeReady(f,r);var old=f.Provider;f.Provider=new(()=>f.Live);ThirdPersonApi.Provider=f.Provider;RuntimeNext(f,r);old.Dispose();RuntimeNext(f,r);RuntimeNext(f,r);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==2&&U.Object.Created.Count==4&&U.Object.Created.Take(2).All(o=>o.Destroyed));});
        RuntimeCase("production-exit-without-provider-cleans",(f,r)=>{RuntimeReady(f,r);ThirdPersonApi.Provider=null!;GameStateManager.CurrentStateName=eGameStateName.Lobby;RuntimeNext(f,r,false);Check(U.Object.Created.All(o=>o.Destroyed));});
        RuntimeCase("production-resetcontext-cleans-before-update",(f,r)=>{RuntimeReady(f,r);f.Provider.ResetContext();Check(U.Object.Created.All(o=>o.Destroyed));});
        RuntimeCase("production-stop-unsubscribes-and-is-idempotent",(f,r)=>{RuntimeReady(f,r);r.Stop();r.Stop();int drawn=U.GUI.Draws;f.Commit();r.OnGUI();Check(U.GUI.Draws==drawn&&U.Object.Created.All(o=>o.Destroyed));});
        RuntimeCase("production-destroyed-anchor-rebuilds",(f,r)=>{RuntimeReady(f,r);U.Object.Destroy(U.Object.Created.OfType<U.GameObject>().Single());RuntimeNext(f,r);RuntimeNext(f,r);RuntimeNext(f,r);r.OnGUI();Check(U.GUI.Draws==2&&U.Object.Created.Count==4);});
        foreach(var failure in new[]{"SetActive","SetPixel","Apply","DrawTexture"})
            RuntimeCase("production-resource-failure-cleans-and-recovers-"+failure,(f,r)=>
            {if(failure=="DrawTexture")RuntimeReady(f,r);else{RuntimeUpdate(f,r);f.Commit();}U.Object.FailAt=failure;
                if(failure=="DrawTexture")r.OnGUI();else for(int i=0;i<3;i++)RuntimeNext(f,r);
                r.TryGetStatus(out var fault);Check(fault.Errors==1&&!fault.HasResources);int before=U.GUI.Draws;f.Live=f.Live with{NowSeconds=f.Live.NowSeconds+1.1};
                for(int i=0;i<4;i++)RuntimeNext(f,r);r.OnGUI();r.TryGetStatus(out var recovered);Check(recovered.Visible&&recovered.InitializationStep==3&&U.GUI.Draws==before+1);});
        RuntimeCase("production-GUI-color-is-restored-even-on-failure",(f,r)=>{RuntimeReady(f,r);U.GUI.color=new(){r=.2f,g=.3f,b=.4f,a=.5f};U.Object.FailAt="DrawTexture";r.OnGUI();Check(U.GUI.color.r==.2f&&U.GUI.color.a==.5f);});
        bool passed=results.All(r=>r.Passed);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,RuntimeRenderingVerified=false,Tests=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var result in results.Where(r=>!r.Passed))Console.WriteLine(result.Name+": "+result.Error);
        Console.WriteLine($"HUD lifecycle / production callback simulation: {results.Count}; passed={passed}");return passed?0:1;
    }
}
