using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;

static class CameraMotionTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static readonly Dictionary<string,double> metrics=new();
    static void Check(bool value,string why="assertion failed"){if(!value)throw new Exception(why);}
    static void Near(float a,float b,float epsilon=.0001f)=>Check(Math.Abs(a-b)<=epsilon,$"{a} != {b} within {epsilon}");
    static void Near(Vector3 a,Vector3 b,float epsilon=.0001f)=>Check(Vector3.Distance(a,b)<=epsilon,$"{a} != {b}");
    static LogicalView View(int frame,float x=0,float y=0,float eye=1.7f,int generation=1)
        =>new(frame,frame/60f,generation,new(x,y,0),new(x,y+eye,0),Vector3.UnitZ);
    static MotionFrame Step(CameraMotionSolver s,LogicalView v,float dt=1/60f,CameraActivity a=CameraActivity.Exploration,CameraPreset p=CameraPreset.Balanced,MotionSettings? options=null)
        =>s.Step(v,dt,a,p,StyleSettings.Default,options??MotionSettings.Default);
    static void Case(string name,Action test)
    {try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    sealed class World:ICameraWorld
    {
        public int Queries;
        public bool BlockOrigin,BlockShift,BlockTravel;
        public float BackLimit=float.PositiveInfinity;
        public bool Overlap(Vector3 point,float radius){Queries++;return BlockOrigin;}
        public bool Sweep(Vector3 origin,Vector3 direction,float distance,float radius,out float hit)
        {
            Queries++;hit=distance;
            if(BlockShift&&Math.Abs(direction.X)>.9f&&distance<.5f){hit=distance*.5f;return true;}
            if(BlockTravel&&distance<.2f){hit=0;return true;}
            if(direction.Z<-.5f&&BackLimit<distance){hit=BackLimit;return true;}
            return false;
        }
    }
    static RigTarget Rig(Vector3 pivot)=>new(pivot,Vector3.UnitZ,2.25f,.48f,80,CameraActivity.Exploration,0);
    static DisplayView Solve(SpatialCameraSolver s,RigTarget r,World w,int frame=1,float aspect=16f/9)
        =>s.Solve(frame,r,1/60f,frame/60.0,aspect,CameraSettings.Default,SpatialSettings.Default,w);
    static MotionFrame[] Route(int fps)
    {
        var s=new CameraMotionSolver();var samples=new List<MotionFrame>();
        for(int i=0;i<=fps*2;i++)
        {
            float t=(float)i/fps;
            float x=t<=1?2*t:2,root=t>=.5f?.2f:0,eye=1.7f-.6f*Math.Clamp((t-1.2f)/.3f,0,1);
            var result=Step(s,View(i,x,root,eye),1f/fps);
            if(i%(fps/30)==0)samples.Add(result);
        }
        return samples.ToArray();
    }
    static void Main(string[] args)
    {
        Case("initial-pivot-has-no-invented-motion",()=>{
            var f=Step(new(),View(10,7,2));Near(f.Pivot,new(7,3.7f,0));Near(f.Velocity,Vector3.Zero);Check(f.ResetReason==MotionResetReason.Start);
        });
        Case("stationary-camera-has-no-drift",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<600;i++){var f=Step(s,View(i));Near(f.Pivot,new(0,1.7f,0));Near(f.Lead,Vector3.Zero);}
        });
        Case("start-and-stop-are-bounded-and-settle",()=>{
            var s=new CameraMotionSolver();MotionFrame f=default;float peak=0;
            for(int i=0;i<=120;i++){float x=Math.Min(i,60)/30f;f=Step(s,View(i,x));peak=Math.Max(peak,Math.Abs(f.Pivot.X-x));Check(Math.Abs(f.Pivot.X-x)<=.2801f);}
            metrics["maximumDefaultHorizontalErrorMeters"]=peak;metrics["stopErrorAfter1SecondMeters"]=Math.Abs(f.Pivot.X-2);
            Near(f.Pivot.X,2,.002f);Check(f.Velocity.Length()<.001f);
        });
        Case("horizontal-motion-has-positive-route-lead",()=>{
            var s=new CameraMotionSolver();MotionFrame f=default;for(int i=0;i<=120;i++)f=Step(s,View(i,i/30f));
            Check(f.Lead.X>.1f&&f.Lead.X<=.1201f);Near(f.Velocity.X,2,.001f);Check(f.Anchor.X<4);
        });
        Case("step-root-height-is-filtered-and-bounded",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));var first=Step(s,View(1,y:.2f));
            Check(first.Anchor.Y>.0f&&first.Anchor.Y<.2f);Check(.2f-first.Anchor.Y<=.1201f);
            float previous=first.Anchor.Y;for(int i=2;i<=60;i++){var f=Step(s,View(i,y:.2f));Check(f.Anchor.Y>=previous-.00001f&&f.Anchor.Y<=.20001f);previous=f.Anchor.Y;}
            Near(previous,.2f,.001f);
        });
        Case("down-step-does-not-overshoot",()=>{
            var s=new CameraMotionSolver();Step(s,View(0,y:.2f));float previous=.2f;
            for(int i=1;i<=90;i++){var f=Step(s,View(i));Check(f.Anchor.Y>=-.00001f&&f.Anchor.Y<=previous+.00001f&&f.Anchor.Y<=.1201f);previous=f.Anchor.Y;}Near(previous,0,.001f);
        });
        Case("crouch-eye-height-does-not-become-root-speed",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));float previous=1.7f;
            for(int i=1;i<=60;i++){var f=Step(s,View(i,eye:1.1f));Near(f.Anchor,Vector3.Zero);Near(f.Velocity,Vector3.Zero);Check(f.EyeHeight<=previous+.00001f&&f.EyeHeight>=1.09999f);previous=f.EyeHeight;}Near(previous,1.1f,.001f);
        });
        Case("continuous-slope-has-bounded-root-error",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<=120;i++){float y=i/120f;var f=Step(s,View(i,i/60f,y));Check(Math.Abs(f.Anchor.Y-y)<=.1201f);}
        });
        Case("vertical-motion-does-not-create-horizontal-lead",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<60;i++){var f=Step(s,View(i,y:i/120f));Near(f.Lead,Vector3.Zero);Near(f.Velocity,Vector3.Zero);}
        });
        Case("reverse-lead-releases-before-changing-side",()=>{
            var s=new CameraMotionSolver();MotionFrame f=default;for(int i=0;i<=60;i++)f=Step(s,View(i,i/30f));float oldLead=f.Lead.X;
            f=Step(s,View(61,2-1/30f));Check(f.Lead.X>0&&Math.Abs(f.Lead.X-oldLead)<.02f);
            for(int i=62;i<=120;i++)f=Step(s,View(i,2-(i-60)/30f));Check(f.Lead.X<-.01f);
        });
        Case("short-taps-do-not-pump-large-lead",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<120;i++){float x=i%12==1?.03f:0;var f=Step(s,View(i,x));Check(f.Lead.Length()<.04f);}
        });
        Case("shoulder-aim-tightens-follow-and-lead",()=>{
            var s=new CameraMotionSolver();MotionFrame f=default;for(int i=0;i<120;i++)f=Step(s,View(i,i/30f),a:CameraActivity.ShoulderAim);
            Check(Math.Abs(f.Anchor.X-119/30f)<=.0561f);Check(f.Lead.Length()<=.0181f);
        });
        Case("stable-has-no-lead",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<120;i++)Near(Step(s,View(i,i/30f),p:CameraPreset.Stable).Lead,Vector3.Zero);
        });
        Case("lead-can-be-disabled-separately",()=>{
            var s=new CameraMotionSolver();for(int i=0;i<90;i++)Near(Step(s,View(i,i/30f),options:MotionSettings.Default with{MaximumLead=0}).Lead,Vector3.Zero);
        });
        Case("repeat-frame-does-not-integrate-or-consume-sample",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));var once=Step(s,View(1,.03f));
            var repeat=Step(s,View(1,.05f),.1f);Check(repeat==once);
            var a=Step(s,View(2,.06f));var reference=new CameraMotionSolver();Step(reference,View(0));Step(reference,View(1,.03f));var b=Step(reference,View(2,.06f));Near(a.Pivot,b.Pivot);Near(a.Velocity,b.Velocity);
        });
        Case("generation-change-does-not-replay-lag",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));Step(s,View(1,.1f));var f=Step(s,View(2,5,1,generation:2));
            Near(f.Pivot,new(5,2.7f,0));Near(f.Lead,Vector3.Zero);Near(f.Velocity,Vector3.Zero);Check(f.ResetReason==MotionResetReason.Context);
        });
        Case("teleport-discards-velocity",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));var f=Step(s,View(1,10));Near(f.Anchor,new(10,0,0));Near(f.Velocity,Vector3.Zero);Check(f.ResetReason==MotionResetReason.Discontinuity);
        });
        Case("long-frame-and-missing-frame-reset",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));var f=Step(s,View(1,1),.5f);Near(f.Pivot,new(1,1.7f,0));
            f=Step(s,View(8,2));Near(f.Pivot,new(2,1.7f,0));Check(f.ResetReason==MotionResetReason.Discontinuity);
        });
        Case("invalid-time-resets-without-speed-spike",()=>{
            foreach(float dt in new[]{float.NaN,float.PositiveInfinity,-1f}){var s=new CameraMotionSolver();Step(s,View(0));var f=Step(s,View(1,.1f),dt);Check(f.Valid);Near(f.Velocity,Vector3.Zero);}
        });
        Case("invalid-position-is-rejected-and-next-frame-rebinds",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));Check(!Step(s,View(1,float.NaN)).Valid);var f=Step(s,View(2,3));Near(f.Pivot,new(3,1.7f,0));Near(f.Velocity,Vector3.Zero);
        });
        Case("explicit-reset-and-zero-dt-initialize-at-current-body",()=>{
            var s=new CameraMotionSolver();Step(s,View(0));Step(s,View(1,.1f));s.Reset();var f=Step(s,View(2,1),0);Near(f.Pivot,new(1,1.7f,0));Near(f.Lead,Vector3.Zero);
        });
        Case("settings-are-finite-and-total-offset-is-bounded",()=>{
            var options=new MotionSettings(true,float.NaN,0,float.PositiveInfinity,3,5,float.NaN).Sanitized();Check(options==new MotionSettings(true,12,1,.12f,.2f,.2f,6));
            var s=new CameraMotionSolver();for(int i=0;i<180;i++){var f=Step(s,View(i,i/5f),options:options);Check(Math.Abs(f.Pivot.X-i/5f)<=.3501f);Check(ViewMath.Finite(f.Pivot));}
        });
        Case("30-60-120fps-route-agrees",()=>{
            var a=Route(30);var b=Route(60);var c=Route(120);float error=0;
            for(int i=0;i<c.Length;i++)error=Math.Max(error,Math.Max(Vector3.Distance(a[i].Pivot,c[i].Pivot),Vector3.Distance(b[i].Pivot,c[i].Pivot)));
            metrics["routeMaximumDifference30To120FpsMeters"]=error;Check(error<=.015f);
        });
        Case("mouse-forward-and-existing-profile-are-unchanged",()=>{
            var old=new CameraStyleSolver();var current=new CameraStyleSolver();
            for(int i=0;i<180;i++){
                var v=View(i,i/30f) with{Forward=Vector3.Normalize(new Vector3(MathF.Sin(i*.1f),.2f,MathF.Cos(i*.1f)))};
                var a=i>90?CameraActivity.ShoulderAim:CameraActivity.Running;
                var before=old.Step(v,1/60f,a,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Legacy);
                var after=current.Step(v,1/60f,a,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);
                Near(after.Forward,v.Forward);Near(after.Distance,before.Distance);Near(after.Shoulder,before.Shoulder);Near(after.VerticalFov,before.VerticalFov);
            }
        });
        Case("disabled-C1-matches-captured-original-054",()=>{
            using var baseline=JsonDocument.Parse(File.ReadAllText(args[1]));var rows=baseline.RootElement.GetProperty("Samples").EnumerateArray().ToDictionary(r=>r.GetProperty("Frame").GetInt32());
            var s=new CameraStyleSolver();s.Reset(true,.53f);int count=0;
            for(int i=0;i<240;i++){
                float x=Math.Clamp(i-30,0,60)/30f,y=i>=60?.2f:0,eye=i>=120?1.1f:1.7f;
                var a=i>=150&&i<180?CameraActivity.ShoulderAim:i>=90&&i<120?CameraActivity.Running:i>=120?CameraActivity.Crouching:CameraActivity.Exploration;
                if(i==210)s.Reset(true,.53f);
                var r=s.Step(View(i,x,y,eye),1/60f,a,CameraPreset.Balanced,i>=180,StyleSettings.Default,65,MotionSettings.Legacy);
                Check(r.PivotOrigin==null);
                if(!rows.TryGetValue(i,out var row))continue;var p=row.GetProperty("Pivot").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
                Near(r.Pivot,new(p[0],p[1],p[2]));Near(r.Distance,row.GetProperty("Distance").GetSingle());Near(r.Shoulder,row.GetProperty("Shoulder").GetSingle());Near(r.VerticalFov,row.GetProperty("VerticalFov").GetSingle());count++;
            }
            Check(count==12);
        });
        Case("C1-is-restorable-without-stale-lead",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)s.Step(View(i,i/30f),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);
            s.Step(View(60,2),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Legacy);Check(!s.Motion.Valid);
            s.Step(View(61,2),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);Near(s.Motion.Lead,Vector3.Zero);
        });
        Case("invalid-style-input-produces-collision-fallback",()=>{
            var s=new CameraStyleSolver();var r=s.Step(View(0,float.NaN),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);
            Check(!Solve(new(),r,new()).IsThirdPerson);
        });
        Case("collision-rejects-blocked-native-eye-to-pivot-path",()=>{
            var r=Rig(new(.1f,1.8f,0)) with{PivotOrigin=new Vector3(0,1.8f,0)};var d=Solve(new(),r,new(){BlockShift=true});Check(!d.IsThirdPerson&&d.Reason=="pivot-path-blocked");
        });
        Case("collision-rejects-origin-overlap",()=>{
            var r=Rig(new(0,1.8f,0)) with{PivotOrigin=new Vector3(0,1.7f,0)};Check(Solve(new(),r,new(){BlockOrigin=true}).Reason=="pivot-origin-overlap");
        });
        Case("C1-adds-at-most-two-spatial-queries",()=>{
            var before=new World();var after=new World();var r=Rig(new(.1f,1.8f,0));Check(Solve(new(),r,before).IsThirdPerson);Check(Solve(new(),r with{PivotOrigin=new Vector3(0,1.8f,0)},after).IsThirdPerson);
            Check(after.Queries-before.Queries==2);metrics["additionalSpatialQueriesWithPivotShift"]=after.Queries-before.Queries;
        });
        Case("collision-is-final-for-wall-compression-and-wide-aspect",()=>{
            foreach(float aspect in new[]{16f/9,21f/9}){var r=Rig(new(.1f,1.8f,0)) with{PivotOrigin=new Vector3(0,1.8f,0)};var d=Solve(new(),r,new(){BackLimit=.8f},aspect:aspect);Check(d.IsThirdPerson);Check(Vector3.Distance(d.Position,r.Pivot)<=.7751f);}
        });
        Case("transition-path-obstruction-still-falls-back",()=>{
            var s=new SpatialCameraSolver();var world=new World();Check(Solve(s,Rig(new(0,1.8f,0)),world).IsThirdPerson);world.BlockTravel=true;
            var d=Solve(s,Rig(new(.05f,1.8f,0)),world,2);Check(!d.IsThirdPerson&&d.Reason=="transition-path-blocked");
        });
        Case("environment-recovery-keeps-stable-delay",()=>{
            var s=new EnvironmentRecovery();Check(!s.Evaluate(false,0,.38f,0,SpatialSettings.Default));Check(!s.Evaluate(true,1,.38f,.1,SpatialSettings.Default));Check(!s.Evaluate(true,1,.38f,.2,SpatialSettings.Default));Check(s.Evaluate(true,1,.38f,.4,SpatialSettings.Default));
        });
        Case("steady-motion-math-allocates-no-managed-bytes",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<4096;i++)s.Step(View(i,i*.001f),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=4096;i<14096;i++)s.Step(View(i,i*.001f),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;metrics["steady10000StepsAllocatedBytes"]=allocated;Check(allocated==0);
        });
        Case("steady-motion-and-spatial-path-allocate-no-managed-bytes",()=>{
            var style=new CameraStyleSolver();var spatial=new SpatialCameraSolver();var world=new World();
            for(int i=0;i<4096;i++)Solve(spatial,style.Step(View(i),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default),world,i);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=4096;i<14096;i++)Solve(spatial,style.Step(View(i),1/60f,CameraActivity.Exploration,CameraPreset.Balanced,false,StyleSettings.Default,65,MotionSettings.Default),world,i);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;metrics["steady10000MotionAndSpatialStepsAllocatedBytes"]=allocated;Check(allocated==0);
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Scope="Production C1 motion, style and collision; mathematical worlds only, no Unity/GPU/native input execution",Checks=results.Count,Passed=results.All(r=>r.Passed),Metrics=metrics,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
