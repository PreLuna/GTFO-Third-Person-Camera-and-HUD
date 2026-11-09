using System;
using System.Numerics;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Tests;
using static PreLuna.ThirdPerson.Tests.StageTest;

static class CameraActionTests
{
    static readonly StyleSettings Enabled=StyleSettings.Default with{MovementFeedback=true};
    static void Main(string[] args)
    {
        Case("default-master-switch-keeps-all-extra-feedback-zero",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<120;i++){Style(s,View(i,i*.04f));Near(s.ActionFeedback.Offset,Vector3.Zero);}
        });
        Case("standing-without-root-travel-has-no-time-driven-drift",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<1200;i++){Style(s,View(i),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);}
        });
        Case("gait-is-driven-by-real-distance-and-remains-in-budget",()=>{
            var s=new CameraStyleSolver();float peak=0;for(int i=0;i<240;i++){Style(s,View(i,i*.04f),style:Enabled);float len=s.ActionFeedback.Offset.Length();peak=Math.Max(peak,len);Check(len<=.00801f);}
            Check(peak>.001f);Metrics["defaultMovingFeedbackPeakMeters"]=peak;
        });
        Case("start-stop-channel-can-be-tested-without-gait",()=>{
            var s=new CameraStyleSolver();var options=ActionFeedbackSettings.Default with{GaitStrength=0};float peak=0;
            for(int i=0;i<120;i++){float x=i<60?i*.04f:2.36f;Style(s,View(i,x),style:Enabled,effects:options);peak=Math.Max(peak,Math.Abs(s.ActionFeedback.Offset.X));Near(s.ActionFeedback.Gait,0);}
            Check(peak>.0001f);
        });
        Case("gait-channel-can-be-tested-without-start-stop",()=>{
            var s=new CameraStyleSolver();var options=ActionFeedbackSettings.Default with{StartStopStrength=0};
            for(int i=0;i<120;i++){Style(s,View(i,i*.04f),style:Enabled,effects:options);Near(s.ActionFeedback.Inertia,Vector3.Zero);Near(s.ActionFeedback.Offset.X,0);}
        });
        Case("stopping-settles-to-exact-zero-without-phase-replay",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<120;i++)Style(s,View(i,i*.04f),style:Enabled);
            for(int i=120;i<360;i++)Style(s,View(i,4.76f),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero,1e-7f);
        });
        Case("same-frame-queries-do-not-replay-feedback-or-acceleration",()=>{
            var s=new CameraStyleSolver();Style(s,View(0),style:Enabled);Style(s,View(1,.04f),style:Enabled);var before=s.ActionFeedback;
            for(int i=0;i<12;i++)Style(s,View(1,.04f),style:Enabled);Check(s.ActionFeedback==before);
        });
        Case("shoulder-aim-immediately-limits-feedback-to-fifteen-percent",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<40;i++)Style(s,View(i,i*.04f),style:Enabled);float full=s.ActionFeedback.Offset.Length();
            Style(s,View(39,39*.04f),activity:CameraActivity.ShoulderAim,style:Enabled);Check(s.ActionFeedback.Offset.Length()<=full*.1501f+.000001f);
        });
        Case("Stable-zeros-feedback-even-when-master-is-on",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)Style(s,View(i,i*.04f),style:Enabled);Style(s,View(60,2.4f),preset:CameraPreset.Stable,style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);
        });
        Case("master-disable-clears-hidden-inertia-and-phase",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)Style(s,View(i,i*.04f),style:Enabled);Style(s,View(60,2.36f));Near(s.ActionFeedback.Offset,Vector3.Zero);
            Style(s,View(61,2.36f),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);
        });
        Case("zero-amplitude-completely-disables-the-effects",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)Style(s,View(i,i*.04f),style:Enabled with{FeedbackAmplitude=0});Near(s.ActionFeedback.Offset,Vector3.Zero);
        });
        Case("teleport-and-context-change-do-not-create-a-start-stop-kick",()=>{
            var s=new CameraStyleSolver();Style(s,View(0),style:Enabled);Style(s,View(1,20),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);
            Style(s,View(2,20,generation:2),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);
        });
        Case("first-person-return-never-resumes-the-old-gait-phase",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)Style(s,View(i,i*.04f),style:Enabled);s.Reset(true);Style(s,View(100,8),style:Enabled);Near(s.ActionFeedback.Offset,Vector3.Zero);
        });
        foreach(var preset in Enum.GetValues<CameraPreset>())Case("feedback-presets-share-the-same-bound-"+preset,()=>{
            var s=new CameraStyleSolver();for(int i=0;i<180;i++){Style(s,View(i,i<90?i*.06f:(180-i)*.06f),preset:preset,style:Enabled);Check(s.ActionFeedback.Offset.Length()<=.00801f);}
        });
        Case("feedback-is-part-of-the-pivot-before-final-wall-collision",()=>{
            var style=new CameraStyleSolver();var spatial=new PreLuna.ThirdPerson.Collision.SpatialCameraSolver();var w=new BoxWorld();w.BackWall(-1);
            for(int i=0;i<120;i++){var r=Style(style,View(i,i*.002f),style:Enabled);var d=Solve(spatial,r,w,i);if(d.IsThirdPerson)Check(!w.Overlap(d.Position,d.Radius));}
        });
        Case("feedback-does-not-change-observation-direction-or-authored-FOV",()=>{
            var a=new CameraStyleSolver();var b=new CameraStyleSolver();for(int i=0;i<120;i++){var no=Style(a,View(i,i*.04f));var yes=Style(b,View(i,i*.04f),style:Enabled);Near(no.Forward,yes.Forward);Near(no.VerticalFov,yes.VerticalFov);Near(no.Distance,yes.Distance);}
        });
        Case("30-60-120-fps-feedback-route-is-consistent",()=>{
            Vector3[] Route(int fps){var s=new CameraStyleSolver();var list=new System.Collections.Generic.List<Vector3>();for(int i=0;i<=fps*3;i++){float t=(float)i/fps;Style(s,View(i,Math.Min(t,1)*2),1f/fps,style:Enabled);if(i%(fps/30)==0)list.Add(s.ActionFeedback.Offset);}return list.ToArray();}
            var a=Route(30);float difference=0;foreach(int fps in new[]{60,120}){var b=Route(fps);for(int i=0;i<a.Length;i++)difference=Math.Max(difference,Vector3.Distance(a[i],b[i]));}
            Metrics["frameRateMaximumFeedbackDifferenceMeters"]=difference;Check(difference<=.001f);
        });
        Case("moving-feedback-math-allocates-no-managed-bytes",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<4096;i++)Style(s,View(i,i*.01f),style:Enabled);long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=4096;i<14096;i++)Style(s,View(i,i*.01f),style:Enabled);Check(GC.GetAllocatedBytesForCurrentThread()==before);
        });
        Finish(args[0],"Production C5 gait/start-stop/presets and pre-collision composition; no native animations, physical footsteps, recoil or Unity rendering execution");
    }
}
