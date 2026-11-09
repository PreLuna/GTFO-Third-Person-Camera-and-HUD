using System;
using System.Numerics;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;
using PreLuna.ThirdPerson.Tests;
using static PreLuna.ThirdPerson.Tests.StageTest;

static class SpatialCameraTests
{
    static void Main(string[] args)
    {
        Case("unobstructed-authored-distance-does-not-wait-for-wall-release",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();Solve(s,Rig(distance:.55f,side:0),w);var d=Solve(s,Rig(distance:1,side:0),w,2);Near(d.Position.Z,-1);
        });
        Case("back-wall-compression-is-immediate-and-final-sphere-is-clear",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.BackWall(-1);var d=Solve(s,Rig(),w);Check(d.IsThirdPerson&&!w.Overlap(d.Position,d.Radius));Check(s.Distance<1&&s.Current.DistanceConstrained);
        });
        Case("distance-release-is-delayed-then-bounded",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.BackWall(-1);Solve(s,Rig(),w);float compressed=s.Distance;w.Boxes.Clear();
            Solve(s,Rig(),w,2,.05);Near(s.Distance,compressed);Solve(s,Rig(),w,3,.3);Check(s.Distance>compressed&&s.Distance<Rig().Distance+.1f);
        });
        Case("opening-side-obstacle-does-not-instantly-expand-shoulder",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.Boxes.Add(new(new(.3f,-10,-10),new(.4f,10,10)));Solve(s,Rig(),w);float small=s.CompressedShoulder;
            w.Boxes.Clear();Solve(s,Rig(),w,2,.05);Near(s.CompressedShoulder,small);Solve(s,Rig(),w,3,.3);Check(s.CompressedShoulder>small&&s.CompressedShoulder<.48f);
        });
        Case("near-wall-shoulder-crossing-can-use-a-checked-near-position",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();Check(Solve(s,Rig(),w).IsThirdPerson);
            w.Boxes.Add(new(new(-.04f,0,-2.29f),new(.04f,3,-2.2f)));
            var d=Solve(s,Rig(side:-.48f),w,2);Check(d.IsThirdPerson,d.Reason);Check(s.Current.Retractions>0&&d.Reason=="third-person-retracted");Check(s.Distance<2&&!w.Overlap(d.Position,d.Radius));
        });
        Case("failed-near-route-falls-back-instead-of-crossing-the-obstacle",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();Solve(s,Rig(),w);
            w.Boxes.Add(new(new(-2,0,-1.9f),new(.25f,3,-.2f)));
            var d=Solve(s,Rig(side:-.48f),w,2);Check(!d.IsThirdPerson||!w.Overlap(d.Position,d.Radius));Check(s.Current.Queries<=20);
        });
        Case("moving-door-engulfing-the-old-camera-forces-native-view",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();var old=Solve(s,Rig(),w);w.Boxes.Add(new(old.Position-new Vector3(.1f),old.Position+new Vector3(.1f)));
            var d=Solve(s,Rig(forward:-Vector3.UnitZ),w,2);Check(!d.IsThirdPerson&&d.Reason=="previous-overlap");
        });
        Case("thin-wall-on-eye-to-pivot-path-cannot-be-skipped",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.Boxes.Add(new(new(.15f,0,-1),new(.16f,3,1)));
            var r=Rig(pivot:new(.4f,1.8f,0)) with{LogicalEye=new Vector3(0,1.8f,0)};var d=Solve(s,r,w);Check(!d.IsThirdPerson);
        });
        Case("low-ceiling-validates-full-native-eye-to-pivot-height-shift",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.Boxes.Add(new(new(-10,2,-10),new(10,2.2f,10)));
            var r=Rig(pivot:new(0,1.9f,0)) with{LogicalEye=new Vector3(0,1.8f,0)};var d=Solve(s,r,w);
            Check(d.IsThirdPerson&&d.Reason=="third-person-pivot-compressed"&&!w.Overlap(d.Position,d.Radius));Check(d.ConstrainedPivot.Value.Y<1.9f);
        });
        Case("ceiling-overlapping-the-native-eye-still-forces-native-view",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.Boxes.Add(new(new(-10,1.9f,-10),new(10,2.2f,10)));Check(!Solve(s,Rig(),w).IsThirdPerson);
        });
        foreach(float aspect in new[]{16f/9,21f/9})foreach(float fov in new[]{45f,80f,110f})
            Case("fov-and-aspect-near-plane-envelope-"+aspect+"-"+fov,()=>{
                var s=new SpatialCameraSolver();var w=new BoxWorld();w.BackWall(-1.1f);var d=Solve(s,Rig(fov:fov),w,aspect:aspect);
                Check(d.IsThirdPerson&&!w.Overlap(d.Position,d.Radius));Check(d.Radius>=ViewMath.NearRadius(.08f,fov,aspect)+.025f-.00001f);
            });
        Case("fov-growth-cannot-keep-an-engulfed-old-envelope",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();var old=Solve(s,Rig(fov:45),w);w.Boxes.Add(new(old.Position+new Vector3(.22f,-1,-.1f),old.Position+new Vector3(.3f,1,.1f)));
            var d=Solve(s,Rig(fov:110),w,2,aspect:21f/9);Check(!d.IsThirdPerson||!w.Overlap(d.Position,d.Radius));
        });
        Case("small-obstacle-jitter-does-not-cause-large-pumping",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();float low=10,high=0;
            for(int i=0;i<240;i++){w.BackWall(-1+(i%2==0?.01f:-.01f));var d=Solve(s,Rig(),w,i);Check(d.IsThirdPerson);low=Math.Min(low,s.Distance);high=Math.Max(high,s.Distance);}
            Metrics["obstacleJitterDistanceRangeMeters"]=high-low;Check(high-low<.04f);
        });
        Case("fire-preview-does-not-change-spatial-history-or-diagnostics",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();Solve(s,Rig(),w);var state=s.Current;
            s.Preview(2,Rig(forward:Vector3.UnitX),.1,16f/9,CameraSettings.Default,SpatialSettings.Default,w,SpatialRefinementSettings.Default);Check(s.Current==state);
            var a=Solve(s,Rig(),w,2);var other=new SpatialCameraSolver();Solve(other,Rig(),new(),1);var b=Solve(other,Rig(),new(),2);Near(a.Position,b.Position);
        });
        Case("fresh-clearance-probe-ignores-stale-camera-history-without-committing",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();var old=Solve(s,Rig(),w);var before=s.Current;
            w.Boxes.Add(new(old.Position-new Vector3(.1f),old.Position+new Vector3(.1f)));
            var d=s.Preview(2,Rig(forward:-Vector3.UnitZ),.1,16f/9,CameraSettings.Default,SpatialSettings.Default,w,SpatialRefinementSettings.Default,true);
            Check(d.IsThirdPerson&&s.Current==before);
        });
        Case("recovery-clearance-probe-can-unblock-a-near-eye-restart",()=>{
            var env=new EnvironmentRecovery();var s=new SpatialCameraSolver();var w=new BoxWorld();Check(!env.Evaluate(false,0,.38f,0,SpatialSettings.Default));
            bool recovered=false;for(int i=1;i<=30;i++){
                var r=Rig();var p=s.Preview(i,r,i/60.0,16f/9,CameraSettings.Default,SpatialSettings.Default,w,SpatialRefinementSettings.Default,true);
                if(env.Evaluate(p.IsThirdPerson,(p.Position-r.Pivot).Length(),.38f,i/60.0,SpatialSettings.Default)){recovered=true;break;}
            }Check(recovered);s.Reset();var close=Solve(s,Rig(distance:.53f,side:0),w,31);Check(close.IsThirdPerson&&!env.Blocked);Near(s.Distance,.53f);
        });
        Case("recovery-preview-cannot-clear-the-real-environment-gate",()=>{
            var env=new EnvironmentRecovery();env.Evaluate(false,0,.38f,0,SpatialSettings.Default);Check(!env.Evaluate(true,1,.38f,.1,SpatialSettings.Default,false));
            Check(!env.Evaluate(true,1,.38f,1,SpatialSettings.Default,false));Check(env.Blocked);
        });
        Case("long-frame-does-not-sweep-a-stale-camera-trajectory",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();var old=Solve(s,Rig(),w);w.Boxes.Add(new(old.Position-new Vector3(.1f),old.Position+new Vector3(.1f)));
            Check(Solve(s,Rig(forward:-Vector3.UnitZ),w,2,dt:.3f).IsThirdPerson);
        });
        Case("world-exception-in-preview-restores-the-original-state",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();Solve(s,Rig(),w);var before=s.Current;w.Throw=true;bool threw=false;
            try{s.Preview(2,Rig(),.1,16f/9,CameraSettings.Default,SpatialSettings.Default,w,SpatialRefinementSettings.Default);}catch{threw=true;}Check(threw&&s.Current==before);
        });
        Case("invalid-view-fails-closed",()=>{
            var s=new SpatialCameraSolver();Check(!Solve(s,Rig(fov:float.NaN),new()).IsThirdPerson);Check(!Solve(s,Rig(forward:Vector3.Zero),new()).IsThirdPerson);
        });
        Case("bounded-space-query-budget-for-turn-and-shoulder-route",()=>{
            var s=new SpatialCameraSolver();var w=new BoxWorld();w.Boxes.Add(new(new(-.04f,0,-2.29f),new(.04f,3,-2.2f)));int maximum=0;
            for(int i=0;i<240;i++){float yaw=i*.08f;var r=Rig(new(MathF.Sin(yaw),0,MathF.Cos(yaw)),side:i%60<30?.48f:-.48f);var d=Solve(s,r,w,i);maximum=Math.Max(maximum,s.Current.Queries);if(d.IsThirdPerson)Check(!w.Overlap(d.Position,d.Radius));Check(s.Current.Queries<=20);}
            Metrics["maximumRefinedSolveQueries"]=maximum;
        });
        Finish(args[0],"Production C4 collision/recovery/preview; conservative AABB sphere geometry substitutes, no Unity PhysX or moving-door game execution");
    }
}
