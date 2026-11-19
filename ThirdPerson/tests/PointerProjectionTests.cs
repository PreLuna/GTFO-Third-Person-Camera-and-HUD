using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPerson.Core;

internal static class PointerProjectionTests
{
    sealed record TestResult(string Name,bool Passed,string? Error);
    static readonly List<TestResult> results=new();
    static void Check(bool condition,string message="assertion failed")
    { if(!condition)throw new InvalidOperationException(message); }
    static void Case(string name,Action action)
    {
        try{action();results.Add(new(name,true,null));}
        catch(Exception error){results.Add(new(name,false,error.Message));}
    }
    static CameraSnapshot Camera(PixelViewport viewport)
        =>new(new(Vector3.Zero,Quaternion.Identity),
            Matrix4x4.Identity,
            new Matrix4x4(
                1,0,0,0,
                0,1,0,0,
                0,0,0,1,
                0,0,0,0),
            viewport);
    static bool Near(Vector2 a,Vector2 b,float tolerance=.01f)
        =>MathF.Abs(a.X-b.X)<=tolerance&&MathF.Abs(a.Y-b.Y)<=tolerance;

    static int Main(string[] args)
    {
        var camera=Camera(new(0,0,1920,1080));
        Case("center-maps-to-screen-center",()=>{
            Check(PointerProjection.TryProject(camera,new(0,0,10),1080,out var point));
            Check(Near(point,new(960,540)),point.ToString());
        });
        Case("right-world-point-maps-right-of-center",()=>{
            Check(PointerProjection.TryProject(camera,new(1,0,10),1080,out var point));
            Check(point.X>960&&MathF.Abs(point.Y-540)<.01f,point.ToString());
        });
        Case("up-world-point-maps-above-center-in-gui",()=>{
            Check(PointerProjection.TryProject(camera,new(0,1,10),1080,out var point));
            Check(point.Y<540&&MathF.Abs(point.X-960)<.01f,point.ToString());
        });
        Case("behind-camera-is-rejected",()=>{
            Check(!PointerProjection.TryProject(camera,new(0,0,-5),1080,out _));
        });
        Case("outside-ndc-is-rejected",()=>{
            Check(!PointerProjection.TryProject(camera,new(20,0,10),1080,out _));
        });
        Case("clamp-to-viewport-edge",()=>{
            Check(PointerProjection.TryProject(camera,new(20,0,10),1080,true,out var point));
            Check(Near(point,new(1920,540)),point.ToString());
            Check(!PointerProjection.TryProject(camera,new(0,0,-5),1080,true,out _));
        });
        Case("viewport-offset-and-flip",()=>{
            var viewportCamera=Camera(new(100,50,800,600));
            Check(PointerProjection.TryProject(viewportCamera,new(0,0,10),1080,out var point));
            Check(Near(point,new(500,730)),point.ToString());
        });
        Case("unity-column-projection-convention",()=>{
            var unityElements=new Matrix4x4(
                1,0,0,0,
                0,1,0,0,
                0,0,-1.002f,-.2002f,
                0,0,-1,0);
            var projectionCamera=new CameraSnapshot(new(Vector3.Zero,Quaternion.Identity),
                Matrix4x4.Identity,Matrix4x4.Transpose(unityElements),new(0,0,1920,1080));
            Check(PointerProjection.TryProject(projectionCamera,new(.5f,.5f,-2),1080,out var point));
            Check(Near(point,new(1200,405)),point.ToString());
        });
        Case("invalid-screen-height-is-rejected",()=>{
            Check(!PointerProjection.TryProject(camera,Vector3.Zero,0,out _));
            Check(!PointerProjection.TryProject(camera,Vector3.Zero,float.NaN,out _));
        });
        Case("invalid-viewport-is-rejected",()=>{
            Check(!PointerProjection.TryProject(Camera(new(0,0,0,1080)),new(0,0,10),1080,out _));
            Check(!PointerProjection.TryProject(Camera(new(0,0,1920,-1)),new(0,0,10),1080,out _));
        });
        Case("non-finite-world-point-is-rejected",()=>{
            Check(!PointerProjection.TryProject(camera,new(float.NaN,0,0),1080,out _));
            Check(!PointerProjection.TryProject(camera,new(0,float.PositiveInfinity,0),1080,out _));
        });
        bool passed=results.All(r=>r.Passed);
        var report=new{SchemaVersion=1,Passed=passed,Count=results.Count,Results=results,
            RuntimeWired=false,RuntimeVerified=false,InstalledToGame=false};
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var result in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+result.Name+": "+result.Error);
        Console.WriteLine($"Pointer projection checks: {results.Count}; passed={passed}");
        return passed?0:1;
    }
}

