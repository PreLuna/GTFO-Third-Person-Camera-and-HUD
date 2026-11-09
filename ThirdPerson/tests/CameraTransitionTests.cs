using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;
using PreLuna.ThirdPerson.Tests;
using static PreLuna.ThirdPerson.Tests.StageTest;

static class CameraTransitionTests
{
    static (float Distance,float Fov)[] Route(int fps)
    {
        var s=new CameraStyleSolver();var list=new System.Collections.Generic.List<(float,float)>();
        for(int i=0;i<=fps*4;i++)
        {
            float t=(float)i/fps,x=t<2?1.5f*t*t:t<3?6+6*(t-2):12;
            var r=Style(s,View(i,x),1f/fps,t>=2&&t<3?CameraActivity.Running:CameraActivity.Exploration);
            if(i%(fps/30)==0)list.Add((r.Distance,r.VerticalFov));
        }
        return list.ToArray();
    }
    static void Main(string[] args)
    {
        Case("kinematics-uses-planar-root-travel-not-eye-height-or-gravity",()=>{
            var k=new CameraKinematics();k.Step(View(0),1/60f);var f=k.Step(View(1,y:.2f,eye:1.2f),1/60f);Near(f.Speed,0);Near(f.Travel,0);
        });
        Case("kinematics-same-frame-is-not-consumed-twice",()=>{
            var k=new CameraKinematics();k.Step(View(0),1/60f);var f=k.Step(View(1,.03f),1/60f);Check(k.Step(View(1,.06f),1/60f)==f);
        });
        Case("preview-does-not-replace-the-previous-root-sample",()=>{
            var k=new CameraKinematics();k.Step(View(0),1/60f);k.Step(View(1,.03f),0);var f=k.Step(View(1,.03f),1/60f);Near(f.Speed,1.8f);
        });
        Case("stationary-running-flag-does-not-trigger-a-zoom",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));for(int i=1;i<120;i++){var r=Style(s,View(i),activity:i%2==0?CameraActivity.Running:CameraActivity.Exploration);Near(r.Distance,2.25f);Near(r.VerticalFov,80);}
        });
        Case("continuous-speed-drives-distance-and-fov-without-running-flag",()=>{
            var s=new CameraStyleSolver();RigTarget r=default;for(int i=0;i<240;i++)r=Style(s,View(i,i*.1f));
            Check(s.Transition.SpeedBlend>.999f);Near(r.Distance,2.6f,.001f);Near(r.VerticalFov,84,.01f);
        });
        Case("brief-forward-tap-has-a-small-zoom-response",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));float x=0,peak=0;
            for(int i=1;i<90;i++){if(i<4)x+=.05f;var r=Style(s,View(i,x));peak=Math.Max(peak,r.Distance-2.25f);}
            Check(peak<.01f);Metrics["shortTapMaximumDistanceIncreaseMeters"]=peak;
        });
        Case("shoulder-swap-and-interruption-continue-from-current-value",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));float previous=.48f;
            for(int i=1;i<9;i++){var r=Style(s,View(i),left:true);Check(r.Shoulder<previous&&r.Shoulder>-.48f);previous=r.Shoulder;}
            var reverse=Style(s,View(9),left:false);Check(reverse.Shoulder>previous&&reverse.Shoulder<.48f);
        });
        Case("aim-entry-and-exit-use-separate-continuous-channels",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));var first=Style(s,View(1),activity:CameraActivity.ShoulderAim);
            Check(first.Distance<2.25f&&first.Distance>1.35f&&first.VerticalFov<80&&first.VerticalFov>70);
            for(int i=2;i<=30;i++)Style(s,View(i),activity:CameraActivity.ShoulderAim);
            var out1=Style(s,View(31));Check(out1.Distance>1.35f&&out1.Distance<2.25f&&s.Transition.LeavingAim);
            var back=Style(s,View(32),activity:CameraActivity.ShoulderAim);Check(back.Distance<out1.Distance&&back.Distance>1.35f);
        });
        Case("same-frame-fire-or-render-query-does-not-advance-channels",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));var r=Style(s,View(1,.04f),activity:CameraActivity.ShoulderAim);
            for(int i=0;i<12;i++){var repeated=Style(s,View(1,.04f),activity:CameraActivity.ShoulderAim);Near(repeated.Pivot,r.Pivot);Near(repeated.Distance,r.Distance);Near(repeated.VerticalFov,r.VerticalFov);}
        });
        Case("same-frame-target-refresh-does-not-consume-a-second-timestep",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));var r=Style(s,View(1,.04f));var aim=Style(s,View(1,.04f),activity:CameraActivity.ShoulderAim);
            Near(aim.Distance,r.Distance);Near(aim.VerticalFov,r.VerticalFov);Near(s.TargetProfile.Distance,1.35f);
        });
        Case("first-person-return-starts-near-current-native-eye",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<60;i++)Style(s,View(i,i*.1f));s.Reset(true,.53f);
            var r=Style(s,View(100,8));Near(r.Distance,.53f);Near(r.Shoulder,0);Near(r.VerticalFov,65);Near(s.Kinematics.Speed,0);
        });
        Case("long-frame-and-context-reset-do-not-replay-speed",()=>{
            var s=new CameraStyleSolver();for(int i=0;i<90;i++)Style(s,View(i,i*.1f));Style(s,View(90,9),.3f);Near(s.Transition.Speed,0);
            Style(s,View(91,9,generation:2));Near(s.Transition.Speed,0);
        });
        Case("teleport-is-not-treated-as-fast-travel",()=>{
            var s=new CameraStyleSolver();Style(s,View(0));Style(s,View(1,20));Near(s.Kinematics.Speed,0);Near(s.Transition.Speed,0);
        });
        foreach(var preset in Enum.GetValues<CameraPreset>())Case("preset-has-safe-continuous-profiles-"+preset,()=>{
            var s=new CameraStyleSolver();for(int i=0;i<180;i++){var r=Style(s,View(i,i*.08f),preset:preset);Check(r.Distance>=.5f&&r.Distance<=4&&r.VerticalFov>=45&&r.VerticalFov<=110);}
            if(preset==CameraPreset.Stable){Near(s.Transition.SpeedBlend,0);Near(s.CurrentProfile.Distance,2.25f);Near(s.CurrentProfile.VerticalFov,80);}
        });
        Case("30-60-120-fps-speed-route-stays-within-frozen-tolerance",()=>{
            var a=Route(30);float maxDistance=0,maxFov=0;foreach(int fps in new[]{60,120}){var b=Route(fps);for(int i=0;i<a.Length;i++){maxDistance=Math.Max(maxDistance,Math.Abs(a[i].Distance-b[i].Distance));maxFov=Math.Max(maxFov,Math.Abs(a[i].Fov-b[i].Fov));}}
            Metrics["frameRateMaximumDistanceDifferenceMeters"]=maxDistance;Metrics["frameRateMaximumVerticalFovDifferenceDegrees"]=maxFov;Check(maxDistance<=.025f&&maxFov<=.3f);
        });
        Case("vertical-composition-is-invariant-across-aspect-ratios",()=>{
            float ratio=CameraComposition.HeightRatio(1.8f,2.25f,80);Near(ratio,.4767f,.001f);
            Check(CameraComposition.HorizontalFov(80,21f/9)>CameraComposition.HorizontalFov(80,16f/9));Near(CameraComposition.HorizontalFov(80,16f/9),112.3f,.1f);
        });
        Case("configuration-cross-fields-and-non-finite-values-are-sanitized",()=>{
            var t=new TransitionSettings(true,5,1,float.NaN,float.PositiveInfinity,-1).Sanitized();Check(t.SpeedFull>=t.SpeedStart+.5f);Near(t.SpeedResponse,5);Check(t.AimExitResponse>=2);
        });
        Case("all-stages-disabled-match-executed-059-baseline",()=>{
            using var document=JsonDocument.Parse(File.ReadAllText(args[1]));var s=new CameraStyleSolver();int count=0;
            foreach(var sample in document.RootElement.GetProperty("Samples").EnumerateArray()){
                int frame=sample.GetProperty("Frame").GetInt32();float x=sample.GetProperty("X").GetSingle();var activity=(CameraActivity)sample.GetProperty("Activity").GetInt32();
                var preset=(CameraPreset)sample.GetProperty("Preset").GetInt32();bool left=sample.GetProperty("Left").GetBoolean();
                var r=s.Step(View(frame,x),1/60f,activity,preset,left,StyleSettings.Default,65,MotionSettings.Default,
                    TransitionSettings.Default with{Enabled=false},ActionFeedbackSettings.Default with{Enabled=false});
                Near(r.Distance,sample.GetProperty("Distance").GetSingle());Near(r.Shoulder,sample.GetProperty("Shoulder").GetSingle());Near(r.VerticalFov,sample.GetProperty("Fov").GetSingle());
                var p=sample.GetProperty("Pivot").EnumerateArray().Select(v=>v.GetSingle()).ToArray();Near(r.Pivot,new(p[0],p[1],p[2]));count++;
            }Check(count>=12);
        });
        Case("steady-modern-style-and-spatial-path-have-no-managed-allocation",()=>{
            var style=new CameraStyleSolver();var spatial=new SpatialCameraSolver();var world=new BoxWorld();
            for(int i=0;i<4096;i++)Solve(spatial,Style(style,View(i,i*.001f)),world,i);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=4096;i<14096;i++)Solve(spatial,Style(style,View(i,i*.001f)),world,i);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before;Metrics["steady10000ModernPipelineManagedBytes"]=bytes;Check(bytes==0);
        });
        Finish(args[0],"Production C3 transitions/kinematics and combined math pipeline, executed 059 baseline; geometric substitutes only, no game input or Unity rendering");
    }
}
