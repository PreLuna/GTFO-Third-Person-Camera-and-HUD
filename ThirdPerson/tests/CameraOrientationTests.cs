using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.Runtime;

// Only the IL2CPP field wrappers are replaced. Tests compile the production adapter and solver.
public class LookCameraController
{
    float yaw,pitch;
    public bool FailPitchOnce;
    public float m_yaw {get=>yaw;set=>yaw=value;}
    public float m_pitch {get=>pitch;set{if(FailPitchOnce){FailPitchOnce=false;throw new Exception("pitch write fault");}pitch=value;}}
    public int NativeInputCalls;
    public void NativeLook(float x,float y,bool inverted=false)
    {NativeInputCalls++;m_yaw+=x*2;m_pitch=Math.Clamp(m_pitch+y*(inverted?-2:2),-79,79);}
}
static class CameraOrientationTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool v,string why="assertion failed"){if(!v)throw new Exception(why);}
    static void Near(float a,float b,float e=.0001f)=>Check(Math.Abs(a-b)<=e,$"{a} != {b}");
    static void Near(Vector3 a,Vector3 b)=>Check(Vector3.Distance(a,b)<.0001f,$"{a} != {b}");
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    sealed class EmptyWorld:IAimWorld
    {public bool Trace(Vector3 o,Vector3 d,float l,out Vector3 p){p=o+d*l;return false;}}
    static CameraInputAdapter Adapter(float yaw=0,float pitch=0)
    {var a=new CameraInputAdapter();Check(a.Orientation.Begin(yaw,pitch));return a;}
    static void Input(CameraInputAdapter a,LookCameraController c,float x,float y,bool invert=false)
    {a.Begin(c,65,out var scope);try{c.NativeLook(x,y,invert);}finally{a.Finish(scope,true);}}
    static void Main(string[] args)
    {
        Case("inactive-adapter-leaves-native-input-and-result-untouched",()=>{
            var a=new CameraInputAdapter();var c=new LookCameraController{m_yaw=15,m_pitch=4};Input(a,c,3,2);
            Near(c.m_yaw,21);Near(c.m_pitch,8);Check(c.NativeInputCalls==1&&!a.Orientation.Active&&!a.Capturing);
        });
        Case("observation-restores-native-yaw-and-pitch",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=12,m_pitch=6};Input(a,c,10,8);
            Near(c.m_yaw,12);Near(c.m_pitch,6);Near(a.Orientation.YawOffset,20);Near(a.Orientation.PitchOffset,16);Check(c.NativeInputCalls==1);
        });
        Case("successive-native-input-calls-accumulate-once",()=>{
            var a=Adapter();var c=new LookCameraController();Input(a,c,4,3);Input(a,c,2,-1);
            Near(a.Orientation.YawOffset,12);Near(a.Orientation.PitchOffset,4);Check(a.Orientation.Samples==2&&c.NativeInputCalls==2);
        });
        Case("native-inversion-and-sensitivity-are-preserved",()=>{
            var a=Adapter();var c=new LookCameraController();Input(a,c,3,4,true);
            Near(a.Orientation.YawOffset,6);Near(a.Orientation.PitchOffset,-8);
        });
        Case("yaw-is-bounded-and-can-immediately-turn-back-from-limit",()=>{
            var a=Adapter();var c=new LookCameraController();Input(a,c,100,0);Near(a.Orientation.YawOffset,65);
            Input(a,c,-5,0);Near(a.Orientation.YawOffset,55);
        });
        Case("pitch-offset-is-bounded-and-native-limit-is-preserved",()=>{
            var a=Adapter();var c=new LookCameraController{m_pitch=70};Input(a,c,0,50);Near(a.Orientation.Pitch(70),79);
            Input(a,c,0,-100);Near(a.Orientation.PitchOffset,-35);Near(c.m_pitch,70);
        });
        Case("yaw-wrap-uses-shortest-angle",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=179};Input(a,c,5,0);Near(a.Orientation.YawOffset,10);Near(a.Orientation.Yaw(179),-171);
        });
        Case("negative-yaw-wrap-is-symmetric",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=-179};Input(a,c,-5,0);Near(a.Orientation.YawOffset,-10);Near(a.Orientation.Yaw(-179),171);
        });
        Case("nested-look-calls-are-captured-by-the-outer-scope-once",()=>{
            var a=Adapter();var c=new LookCameraController();a.Begin(c,65,out var outer);c.NativeLook(2,1);
            a.Begin(c,65,out var inner);c.NativeLook(3,1);a.Finish(inner,true);Check(a.Capturing);a.Finish(outer,true);
            Near(c.m_yaw,0);Near(c.m_pitch,0);Near(a.Orientation.YawOffset,10);Near(a.Orientation.PitchOffset,4);Check(a.Orientation.Samples==1);
        });
        Case("duplicate-finalizer-does-not-restore-over-new-native-input",()=>{
            var a=Adapter();var c=new LookCameraController();a.Begin(c,65,out var s);c.NativeLook(3,2);a.Finish(s,true);
            c.m_yaw=30;a.Finish(s,true);Near(c.m_yaw,30);Check(a.Orientation.Samples==1);
        });
        Case("stale-finalizer-cannot-close-a-later-capture",()=>{
            var a=Adapter();var c=new LookCameraController();a.Begin(c,65,out var old);a.Finish(old,true);
            a.Begin(c,65,out var next);c.NativeLook(4,0);a.Finish(old,true);Check(a.Capturing);a.Finish(next,true);Near(a.Orientation.YawOffset,8);
        });
        Case("native-exception-restores-fields-and-blocks-until-key-release",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=7,m_pitch=-3};a.Begin(c,65,out var s);c.NativeLook(4,1);a.Finish(s,false);
            Near(c.m_yaw,7);Near(c.m_pitch,-3);Check(!a.Capturing&&!a.Orientation.Active&&a.Orientation.BlockedUntilRelease);
            Check(!a.Orientation.Begin(7,-3));a.Orientation.ObserveKey(false);Check(a.Orientation.Begin(7,-3));Near(a.Orientation.YawOffset,0);
        });
        Case("prefix-write-fault-restores-before-propagating",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=9,m_pitch=6};Input(a,c,5,1);c.FailPitchOnce=true;
            bool failed=false;try{a.Begin(c,65,out _);}catch{failed=true;}Check(failed&&!a.Capturing&&!a.Orientation.Active);Near(c.m_yaw,9);Near(c.m_pitch,6);
        });
        Case("restore-attempts-pitch-even-when-completion-is-cancelled",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=2,m_pitch=3};a.Begin(c,65,out var s);c.NativeLook(5,5);
            a.Orientation.Reset(true);a.Finish(s,true);Near(c.m_yaw,2);Near(c.m_pitch,3);Check(!a.Orientation.Active);Near(a.Orientation.YawOffset,0);
        });
        Case("invalid-native-result-restores-before-throwing",()=>{
            var a=Adapter();var c=new LookCameraController{m_yaw=2,m_pitch=3};a.Begin(c,65,out var s);c.m_yaw=float.NaN;
            bool failed=false;try{a.Finish(s,true);}catch{failed=true;}Check(failed&&!a.Capturing);Near(c.m_yaw,2);Near(c.m_pitch,3);
        });
        Case("blocked-hold-cannot-reactivate-without-release",()=>{
            var s=new CameraOrientationSolver();s.Reset(true);s.ObserveKey(true);Check(!s.Begin(0,0));s.ObserveKey(false);Check(s.Begin(0,0));
        });
        Case("reset-discards-observer-history",()=>{
            var s=new CameraOrientationSolver();s.Begin(10,0);s.Capture(45,0,-20,65);s.Reset();s.Begin(-120,30);Near(s.YawOffset,0);Near(s.PitchOffset,0);Near(s.Yaw(-120),-120);
        });
        Case("observer-forward-has-unity-pitch-sign-and-unit-length",()=>{
            var s=new CameraOrientationSolver();s.Begin(0,0);s.Capture(45,0,20,65);var f=s.Forward(0,0);Check(f.X>0&&f.Z>0&&f.Y<0);Near(f.Length(),1);
        });
        Case("handoff-preserves-world-direction-and-wasd-basis",()=>{
            var s=new CameraOrientationSolver();s.Begin(179,-10);s.Capture(36,-10,15,65);var before=s.Forward(179,-10);
            float nativeYaw=s.Yaw(179),nativePitch=s.Pitch(-10);s.Reset(true);Near(s.Forward(nativeYaw,nativePitch),before);
            var flat=Vector3.Normalize(new Vector3(before.X,0,before.Z));var right=Vector3.Cross(Vector3.UnitY,flat);
            Near(Vector3.Dot(flat,right),0);Near(right.Length(),1);Check(Vector3.Dot(flat,new(before.X,0,before.Z))>0);
        });
        Case("bounded-observation-stays-in-native-front-hemisphere",()=>{
            var s=new CameraOrientationSolver();for(int pitch=-79;pitch<=79;pitch++)for(int dp=-35;dp<=35;dp+=5){
                s.Reset();s.Begin(0,pitch);var original=s.Forward(0,pitch);s.Capture(75,pitch,Math.Clamp(pitch+dp,-79,79),75);
                Check(Vector3.Dot(original,s.Forward(0,pitch))>.13f);
            }
        });
        Case("aim-behind-eye-guard-remains-in-force",()=>{
            var w=new EmptyWorld();var result=AimSolver.Solve(new(1,1,1,Vector3.Zero,new(0,0,-2),-Vector3.UnitZ,true),1,1,1,Vector3.Zero,Vector3.UnitZ,250,w);
            Check(!result.Valid&&result.Reason=="near-or-behind-eye");
        });
        Case("aim-after-handoff-follows-current-observer-direction",()=>{
            var s=new CameraOrientationSolver();s.Begin(0,0);s.Capture(65,0,-20,65);var f=s.Forward(0,0);float y=s.Yaw(0),p=s.Pitch(0);s.Reset(true);
            var native=s.Forward(y,p);var result=AimSolver.Solve(new(1,1,1,Vector3.Zero,-f*2,f,true),1,1,1,Vector3.Zero,native,250,new EmptyWorld());
            Check(result.Valid);Near(result.Direction,f);
        });
        Case("invalid-start-is-rejected",()=>{var s=new CameraOrientationSolver();Check(!s.Begin(float.NaN,0)&&!s.Begin(0,float.PositiveInfinity));});
        Case("native-samples-are-frame-rate-independent",()=>{
            foreach(int rate in new[]{30,60,120}){var a=Adapter();var c=new LookCameraController();for(int i=0;i<rate;i++)Input(a,c,20f/rate,-10f/rate);Near(a.Orientation.YawOffset,40,.004f);Near(a.Orientation.PitchOffset,-20,.004f);Check(c.NativeInputCalls==rate);}
        });
        Case("steady-input-scopes-allocate-no-managed-bytes",()=>{
            var a=Adapter();var c=new LookCameraController();for(int i=0;i<4096;i++)Input(a,c,.01f,-.001f);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)Input(a,c,.01f,-.001f);
            Check(GC.GetAllocatedBytesForCurrentThread()-before==0);
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Scope="Production C2a orientation/input scoping with field-wrapper substitutes and existing AimSolver; no game-native input, animation, Unity physics or GPU execution",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
