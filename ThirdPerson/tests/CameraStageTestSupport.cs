using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;

namespace PreLuna.ThirdPerson.Tests
{
    static class StageTest
    {
        sealed record Result(string Name,bool Passed,string Error);
        static readonly List<Result> results=new();
        internal static readonly Dictionary<string,double> Metrics=new();
        internal static void Check(bool value,string why="assertion failed"){if(!value)throw new Exception(why);}
        internal static void Near(float a,float b,float e=.0001f)=>Check(Math.Abs(a-b)<=e,$"{a} != {b} (epsilon {e})");
        internal static void Near(Vector3 a,Vector3 b,float e=.0001f)=>Check(Vector3.Distance(a,b)<=e,$"{a} != {b}");
        internal static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
        internal static LogicalView View(int frame,float x=0,float z=0,float y=0,int generation=1,float eye=1.7f,Vector3? forward=null)
            =>new(frame,frame/60f,generation,new(x,y,z),new(x,y+eye,z),forward??Vector3.UnitZ);
        internal static RigTarget Rig(Vector3? forward=null,float distance=2.25f,float side=.48f,float fov=80,Vector3? pivot=null)
            =>new(pivot??new(0,1.8f,0),forward??Vector3.UnitZ,distance,side,fov,CameraActivity.Exploration,0){LogicalEye=pivot??new Vector3(0,1.8f,0)};
        internal static RigTarget Style(CameraStyleSolver s,LogicalView v,float dt=1/60f,CameraActivity activity=CameraActivity.Exploration,
            CameraPreset preset=CameraPreset.Balanced,bool left=false,StyleSettings? style=null,TransitionSettings? transitions=null,ActionFeedbackSettings? effects=null)
            =>s.Step(v,dt,activity,preset,left,style??StyleSettings.Default,65,MotionSettings.Default,transitions??TransitionSettings.Default,effects??ActionFeedbackSettings.Default);
        internal static DisplayView Solve(SpatialCameraSolver s,RigTarget rig,BoxWorld w,int frame=1,double? now=null,float dt=1/60f,float aspect=16f/9)
            =>s.Solve(frame,rig,dt,now??frame/60.0,aspect,CameraSettings.Default,SpatialSettings.Default,w,SpatialRefinementSettings.Default);
        internal static void Finish(string path,string scope)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path,JsonSerializer.Serialize(new{Scope=scope,Checks=results.Count,Passed=results.All(r=>r.Passed),Metrics,Results=results},new JsonSerializerOptions{WriteIndented=true}));
            foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));if(results.Any(r=>!r.Passed))Environment.Exit(1);
        }
    }
    readonly record struct Box(Vector3 Min,Vector3 Max);
    // Conservative sphere-vs-expanded-AABB casts. These are geometry substitutes, not Unity PhysX.
    sealed class BoxWorld:ICameraWorld
    {
        internal readonly List<Box> Boxes=new();
        internal int Queries;internal bool Throw;
        public bool Overlap(Vector3 p,float radius)
        {
            Queries++;if(Throw)throw new Exception("world unavailable");
            foreach(var b in Boxes){var q=Vector3.Clamp(p,b.Min,b.Max);if(Vector3.DistanceSquared(p,q)<radius*radius-1e-7f)return true;}return false;
        }
        public bool Sweep(Vector3 p,Vector3 direction,float distance,float radius,out float hit)
        {
            Queries++;if(Throw)throw new Exception("world unavailable");hit=distance;bool found=false;
            foreach(var b in Boxes)
            {
                Vector3 min=b.Min-new Vector3(radius),max=b.Max+new Vector3(radius);float near=0,far=distance;
                bool slab(float position,float delta,float low,float high)
                {
                    if(Math.Abs(delta)<1e-7f)return position>=low&&position<=high;
                    float a=(low-position)/delta,c=(high-position)/delta;if(a>c)(a,c)=(c,a);near=Math.Max(near,a);far=Math.Min(far,c);return near<=far;
                }
                if(slab(p.X,direction.X,min.X,max.X)&&slab(p.Y,direction.Y,min.Y,max.Y)&&slab(p.Z,direction.Z,min.Z,max.Z)&&near<=distance&&far>=0)
                {found=true;hit=Math.Min(hit,Math.Max(0,near));}
            }
            return found;
        }
        internal void BackWall(float z){Boxes.Clear();Boxes.Add(new(new(-10,-10,z-.1f),new(10,10,z)));}
    }
}
