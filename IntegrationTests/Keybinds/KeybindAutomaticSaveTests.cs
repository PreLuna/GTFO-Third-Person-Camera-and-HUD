using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using PreLuna.ThirdPerson.Control;

namespace PreLuna.ThirdPerson.Runtime
{
    internal sealed class Plugin
    {
        internal static Plugin Instance;
        internal ThirdPersonControlState CameraControl;
    }
}

static class KeybindAutomaticSaveTests
{
    enum Key { None=0, F8=289, F9=290, F10=291, RightAlt=307, LeftAlt=308 }
    sealed record Result(string Name, bool Passed, string Error);
    static readonly List<Result> Results=new();
    static string fixtureRoot;
    static void Check(bool value,string message="assertion failed") { if(!value)throw new Exception(message); }
    static void Case(string name,Action test)
    { try{test();Results.Add(new(name,true,null));}catch(Exception e){Results.Add(new(name,false,e.ToString()));} }
    sealed class Fixture
    {
        internal readonly string Path;
        internal readonly ConfigFile Config;
        internal readonly ConfigEntry<bool> Enabled,Diagnostic;
        internal readonly ConfigEntry<Key> Swap,Observe;
        internal readonly ThirdPersonControlState State;
        readonly Dictionary<string,ThirdPersonControlValue> values=new();
        internal Fixture(bool observeSaves=true)
        {
            Path=System.IO.Path.Combine(fixtureRoot,Guid.NewGuid().ToString("N"),"camera.cfg");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            Config=new ConfigFile(Path,false);
            Enabled=Config.Bind("原型","启用",false);
            Diagnostic=Config.Bind("调试","指向覆盖层",false);
            Swap=Config.Bind("按键","左右换肩",Key.F9);
            Observe=Config.Bind("镜头·C2","观察按键",Key.RightAlt);
            foreach(var d in ThirdPersonControlState.Definitions)values[d.Key]=d.DefaultValue;
            State=new ThirdPersonControlState(Path,Read,Write,Serialize,()=>Config.SaveOnConfigSet,v=>Config.SaveOnConfigSet=v,
                code=>Enum.IsDefined(typeof(Key),code));
            if(observeSaves)Config.SettingChanged+=(_,__)=>{if(Config.SaveOnConfigSet)State.ObserveAutomaticSave();};
            PreLuna.ThirdPerson.Runtime.Plugin.Instance=new(){CameraControl=State};
        }
        ThirdPersonControlValue Read(string key)=>key switch
        { "key.swapShoulder"=>ThirdPersonControlValue.Integer((int)Swap.Value),"key.observe"=>ThirdPersonControlValue.Integer((int)Observe.Value),_=>values[key] };
        void Write(string key,ThirdPersonControlValue value)
        {
            if(key=="key.swapShoulder"&&value.TryGetInteger(out var swap))Swap.Value=(Key)swap;
            else if(key=="key.observe"&&value.TryGetInteger(out var observe))Observe.Value=(Key)observe;
            else values[key]=value;
        }
        string Serialize(string key)=>key=="key.swapShoulder"?Swap.GetSerializedValue():key=="key.observe"?Observe.GetSerializedValue():values[key].ToString();
        internal JsonElement Save(string key,int code,string revision=null)
        {
            var payload=JsonSerializer.Serialize(new { expectedRevision=revision??State.ReadSnapshot().Revision,
                values=new Dictionary<string,object>{{key,new {kind=2,integer=code}}} });
            return JsonDocument.Parse(ThirdPersonControlApi.SetPersistentJson(payload)).RootElement.Clone();
        }
        internal void Success(string key,int code) { var r=Save(key,code);Check(r.GetProperty("outcome").GetInt32()==0,r.ToString()); }
    }
    static int Main(string[] args)
    {
        fixtureRoot=args[1];Directory.CreateDirectory(fixtureRoot);
        Case("real-BepInEx-F6-save-then-rebind-both-reported-actions",()=>{
            var f=new Fixture();Check(f.Config.SaveOnConfigSet);f.Enabled.Value=true;
            f.Success("key.swapShoulder",(int)Key.F10);f.Success("key.observe",(int)Key.LeftAlt);
            Check(f.Swap.Value==Key.F10&&f.Observe.Value==Key.LeftAlt);
            var text=File.ReadAllText(f.Path);Check(text.Contains("左右换肩 = F10")&&text.Contains("观察按键 = LeftAlt")&&text.Contains("启用 = true"));
        });
        Case("other-unmanaged-diagnostic-auto-save-also-advances-revision",()=>{
            var f=new Fixture();f.Diagnostic.Value=true;f.Success("key.swapShoulder",(int)Key.F8);
            Check(File.ReadAllText(f.Path).Contains("指向覆盖层 = true"));
        });
        Case("external-edit-after-own-save-still-rejected-with-runtime-rollback",()=>{
            var f=new Fixture();f.Enabled.Value=true;File.AppendAllText(f.Path,"# external edit\n");var before=File.ReadAllText(f.Path);
            var r=f.Save("key.swapShoulder",(int)Key.F10);Check(r.GetProperty("outcome").GetInt32()==3,r.ToString());
            Check(f.Swap.Value==Key.F9&&File.ReadAllText(f.Path)==before);
        });
        Case("auto-save-disabled-event-cannot-adopt-an-external-file",()=>{
            var f=new Fixture();f.Config.SaveOnConfigSet=false;File.AppendAllText(f.Path,"# external edit\n");f.Enabled.Value=true;
            var r=f.Save("key.observe",(int)Key.LeftAlt);Check(r.GetProperty("outcome").GetInt32()==3,r.ToString());
            Check(f.Observe.Value==Key.RightAlt&&!f.Config.SaveOnConfigSet);
        });
        Case("own-save-invalidates-stale-revision",()=>{
            var f=new Fixture();var old=f.State.ReadSnapshot().Revision;f.Enabled.Value=true;
            Check(f.Save("key.swapShoulder",(int)Key.F10,old).GetProperty("outcome").GetInt32()==3);
        });
        Case("None-clear-and-fresh-config-instance-read-back",()=>{
            var f=new Fixture();f.Enabled.Value=true;f.Success("key.swapShoulder",0);f.Success("key.observe",(int)Key.LeftAlt);
            var fresh=new ConfigFile(f.Path,false);Check(fresh.Bind("按键","左右换肩",Key.F9).Value==Key.None);
            Check(fresh.Bind("镜头·C2","观察按键",Key.RightAlt).Value==Key.LeftAlt);
        });
        Case("old-unobserved-save-reproduces-original-conflict",()=>{
            var f=new Fixture(false);f.Enabled.Value=true;
            var r=f.Save("key.swapShoulder",(int)Key.F10);Check(r.GetProperty("outcome").GetInt32()==3&&f.Swap.Value==Key.F9);
        });
        File.WriteAllText(args[0],JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in Results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+" "+r.Error);
        return Results.Any(r=>!r.Passed)?1:0;
    }
}
