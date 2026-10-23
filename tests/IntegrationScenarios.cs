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
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Runtime;
using U=UnityEngine;

static class IntegrationScenarios
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool b,string why="assertion failed"){if(!b)throw new Exception(why);}
    sealed class Fixture:IDisposable
    {
        public BridgeObservation Live;
        public readonly ThirdPersonViewProvider Provider;
        public readonly HudRuntime Runtime;
        public CameraSnapshot Camera=new(new(new(0,1,3),Quaternion.Identity),Matrix4x4.CreateTranslation(0,-1,-3),Matrix4x4.CreatePerspectiveFieldOfView(1.2f,16f/9,.08f,250),new(0,0,1920,1080));
        public WorldPose Body=new(Vector3.Zero,Quaternion.Identity);
        public Fixture(HudComputationMode mode)
        {
            U.Object.Created.Clear();U.Object.FailAt=null;U.GUI.Draws=0;U.Camera.current=null;U.Event.current=new(){type=U.EventType.Repaint};
            var p=new PlayerIdentity(new(100,1),200,0);var b=new LocalViewBinding(p,new(300,2),new(400,3),500,0);
            Live=new(b,p,true,true,true,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready,1,100,10);
            Provider=new(()=>Live);ThirdPersonApi.Provider=Provider;Plugin.Instance=new();
            Plugin.Instance.Settings.Options=HudLifecycleOptions.Default with{ActivationDelaySeconds=0};
            Plugin.Instance.Settings.WorkOptions=new(5,3,0,.25);Plugin.Instance.Settings.ExecutionOptions=new(mode,.5);
            GameStateManager.CurrentStateName=eGameStateName.InLevel;
            Runtime=new(IntPtr.Zero);Runtime.Initialize(()=>ThirdPersonApi.MonotonicSeconds,legacyMarker:true);
        }
        public HudPresentationDiagnostics Diagnostics{get{Runtime.TryGetPresentationDiagnostics(out var d);return d;}}
        public HudPerformanceSnapshot Perf{get{Runtime.TryGetPerformance(out var p);return p;}}
        public void Frame(bool commit=true)
        {
            Live=Live with{FrameNumber=Live.FrameNumber+1,NowSeconds=Live.NowSeconds+.01};
            U.Time.frameCount=Live.FrameNumber;ThirdPersonApi.MonotonicSeconds=Live.NowSeconds;Runtime.Update();
            if(commit){Provider.BeginMainAttempt();Provider.PublishMain(Camera,Body);Provider.MainRestored(true);Provider.NativeMainCompleted(true);}
            Runtime.OnGUI();Thread.Sleep(1);
        }
        public void Ready(){long draws=Diagnostics.ActualDraws;for(int i=0;i<100&&Diagnostics.ActualDraws==draws;i++)Frame();Check(Diagnostics.ActualDraws>draws,"no fresh draw");}
        public void Pause(ViewSource source,ThirdPersonMode mode,ViewAvailability availability)
        {Live=Live with{Source=source,Mode=mode,Availability=availability};long n=Diagnostics.ActualDraws;for(int i=0;i<3;i++)Frame(false);Check(Diagnostics.ActualDraws==n&&Diagnostics.LiveOwnedReferences==2,"pause drew or lost resources");}
        public void Resume(){Live=Live with{Source=ViewSource.LocalThirdPerson,Mode=ThirdPersonMode.Exploration,Availability=ViewAvailability.Ready};Ready();}
        public void Dispose(){Runtime.Stop();Provider.Dispose();ThirdPersonApi.Provider=null!;Check(U.Object.Created.All(x=>x.Destroyed),"resource leak");}
    }
    static void Case(string name,HudComputationMode mode,Action<Fixture> action)
    {try{using(var f=new Fixture(mode)){f.Ready();action(f);}results.Add(new(mode+"/"+name,true,null));}catch(Exception e){results.Add(new(mode+"/"+name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        foreach(var mode in Enum.GetValues<HudComputationMode>())
        {
            Case("actual-draw-diagnostics",mode,f=>{var d=f.Diagnostics;Check(d.Instance!=Guid.Empty&&d.ActualDraws==U.GUI.Draws&&d.AnchorsCreated==1&&d.TexturesCreated==1&&d.LiveOwnedReferences==2);Check(d.LastDraw.View.Subject==f.Live.Binding.LocalPlayer);});
            Case("texture-target-reject-count",mode,f=>{long n=f.Diagnostics.ActualDraws;U.Camera.current=new(){targetTexture=new object()};f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==n&&f.Diagnostics.TextureTargetRejections==1);U.Camera.current=null;});
            Case("draw-failure-not-counted",mode,f=>{long n=f.Diagnostics.ActualDraws;U.Object.FailAt="DrawTexture";f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==n);});
            Case("shoulder-camera-and-aim-use-fresh-pose",mode,f=>{long creates=f.Diagnostics.AnchorsCreated;f.Body=new(new(.3f,0,0),Quaternion.Identity);f.Camera=f.Camera with{Pose=f.Camera.Pose with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.2f)}};f.Live=f.Live with{Mode=ThirdPersonMode.ShoulderAim};f.Ready();Check(f.Diagnostics.LastDraw.FrameNumber==f.Live.FrameNumber&&f.Diagnostics.AnchorsCreated==creates&&f.Diagnostics.LastDraw.WorldAnchor.X>.8f);});
            Case("aim-fallback-control-200-transitions-reuse",mode,f=>{for(int i=0;i<200;i++){f.Pause(i%2==0?ViewSource.LocalFirstPerson:ViewSource.BotControl,ThirdPersonMode.FirstPersonAim,ViewAvailability.TemporaryFallback);f.Resume();}var d=f.Diagnostics;Check(d.AnchorsCreated==1&&d.TexturesCreated==1&&d.DestroyRequests==0);Check(f.Perf.Computation.Worker.ThreadStarts<=(mode==HudComputationMode.Background?1:0));});
            Case("nested-bot-preview-draws-only-after-unwind",mode,f=>{long n=f.Diagnostics.ActualDraws;f.Provider.AuxiliaryBegin(new(801,1));f.Provider.AuxiliaryBegin(new(802,1));f.Runtime.OnGUI();f.Provider.AuxiliaryEnd(new(802,1));f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==n);f.Provider.AuxiliaryEnd(new(801,1));f.Runtime.OnGUI();Check(f.Diagnostics.ActualDraws==n+1&&f.Diagnostics.LastDraw.Render.Surface==RenderSurface.MainDisplay);});
            Case("character-rebuild-rebinds-current-identity",mode,f=>{var old=f.Diagnostics.LastDraw;f.Live=f.Live with{Binding=f.Live.Binding with{CharacterModel=new(930,12),MainCamera=new(940,13)}};f.Ready();Check(f.Diagnostics.AnchorsCreated==1&&f.Diagnostics.LastDraw.View.Binding.CharacterModel!=old.View.Binding.CharacterModel&&f.Diagnostics.LastDraw.Render.Camera==f.Live.Binding.MainCamera);});
            Case("floor-change-cleans-before-new-draw",mode,f=>{var old=f.Diagnostics.LastDraw;f.Live=f.Live with{Binding=f.Live.Binding with{FloorHandle=901}};f.Ready();Check(f.Diagnostics.AnchorsCreated==2&&f.Diagnostics.TexturesCreated==2&&f.Diagnostics.DestroyRequests==2&&f.Diagnostics.LiveOwnedReferences==2&&f.Diagnostics.LastDraw.View.Binding.FloorHandle!=old.View.Binding.FloorHandle);});
            Case("leave-level-cleans-and-cannot-redraw",mode,f=>{long n=f.Diagnostics.ActualDraws;GameStateManager.CurrentStateName=eGameStateName.Lobby;f.Live=f.Live with{InLevel=false};for(int i=0;i<10;i++)f.Frame(false);Check(f.Diagnostics.ActualDraws==n&&f.Diagnostics.LiveOwnedReferences==0&&f.Diagnostics.DestroyRequests==2);});
        }
        bool passed=results.All(x=>x.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(x=>!x.Passed))Console.WriteLine(r.Name+": "+r.Error);Console.WriteLine($"{(passed?"PASS":"FAIL")} {results.Count} combined scenarios");return passed?0:1;
    }
}

