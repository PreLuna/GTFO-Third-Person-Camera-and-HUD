using System;
using System.IO;
using System.Text.Json;
using PreLuna.ThirdPerson.Core;

static class PresentationRecoveryTests
{
    static void Check(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    static void Main(string[] args)
    {
        var world=new PresentationWorld(new(1,2,3,0,4),5,6,7,8,9,10,11);
        var binding=new PresentationBinding(world,12);
        var recovery=new PresentationRecovery();

        Check(!recovery.Observe(binding,10,0),"first frame must wait");
        Check(!recovery.Observe(binding,10,.09f),"a second callback is not a new frame");
        Check(!recovery.Observe(binding,11,.1f),"two frames must wait even after 75 ms");
        Check(recovery.Observe(binding,12,.11f),"three continuous frames after 75 ms may bind");

        recovery.Reset();
        Check(!recovery.Observe(binding,13,.12f),"release discards previous recovery even for the same identity");
        Check(!recovery.Observe(binding,15,.24f),"skipped frame restarts recovery");
        Check(!recovery.Observe(binding,16,.25f),"second frame after a gap");
        Check(!recovery.Observe(binding,17,.26f),"three fast frames still need the time window");
        Check(recovery.Observe(binding,18,.33f),"eligible after the time window");

        Check(!recovery.Observe(binding with { Item=13 },19,.34f),"item swap restarts recovery");
        Check(!recovery.Observe(binding,20,.42f),"switching back also restarts recovery");
        Check(!recovery.Observe(binding with { World=world with { Camera=14 } },21,.5f),"camera swap restarts recovery");
        var nextWorld=world with { Context=world.Context with { Floor=15,Model=16 } };
        Check(!recovery.Observe(binding with { World=nextWorld },22,.6f),"floor/model change restarts recovery");

        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=13}));
        Console.WriteLine("Presentation recovery: 13 checks passed");
    }
}
