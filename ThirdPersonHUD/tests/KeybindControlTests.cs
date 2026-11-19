using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PreLuna.ThirdPersonHUD.Control;
static class HudKeybindControlTests
{
    sealed record Result(string Name, bool Passed, string Error);
    static readonly List<Result> Results = new();
    static void Check(bool v, string message = "assertion failed") { if (!v) throw new Exception(message); }
    static void Case(string name, Action test) { try { test(); Results.Add(new(name,true,null)); } catch(Exception e) { Results.Add(new(name,false,e.ToString())); } }
    sealed class Harness
    {
        internal readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "preluna-keys-" + Guid.NewGuid().ToString("N"), "target.cfg");
        internal readonly Dictionary<string, HudControlValue> Values = new();
        internal readonly HudControlState State;
        internal bool Fail;
        internal Harness()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, "# preserved\n[unknown]\nother = keep\n[Panel.Input]\nExpandKey = F6\n");
            foreach(var d in HudControlState.Definitions) Values[d.Key] = d.DefaultValue;
            State = new HudControlState(Path, k => Values[k], (k,v) => Values[k]=v,
                k => Fail ? throw new IOException("injected save failure") : Values[k].TryGetInteger(out var n) ? n == 0 ? "None" : n == 288 ? "F7" : "F6" : Values[k].ToString(),
                validKey: n => n == 0 || n == 287 || n == 288);
        }
        internal HudControlResult Save(int code) => State.SetPersistent(new HudControlChange(State.ReadSnapshot().Revision,
            new Dictionary<string, HudControlValue> { ["key.visibility"] = HudControlValue.Integer(code) }));
    }
    static int Main(string[] args)
    {
        Case("rebind-and-clear-write-owned-line-and-preserve-unknown-input",()=>{
            var h=new Harness(); Check(h.Save(288).Outcome==HudControlOutcome.Success);
            var text=File.ReadAllText(h.Path);Check(text.Contains("ExpandKey = F7")&&text.Contains("# preserved")&&text.Contains("other = keep"));
            Check(h.Save(0).Outcome==HudControlOutcome.Success);Check(File.ReadAllText(h.Path).Contains("ExpandKey = None"));
            Check(h.State.ReadSnapshot().Effective["key.visibility"].TryGetInteger(out var code)&&code==0);
        });
        Case("invalid-key-rejected-before-runtime-or-disk-change",()=>{
            var h=new Harness();var before=File.ReadAllBytes(h.Path);var old=h.Values["key.visibility"];
            Check(h.Save(286).Outcome==HudControlOutcome.ValidationRejected);
            Check(Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(h.Path))&&h.Values["key.visibility"].ValueEquals(old));
        });
        Case("save-failure-rolls-back-runtime-and-preserves-disk",()=>{
            var h=new Harness();var before=File.ReadAllText(h.Path);var old=h.Values["key.visibility"];h.Fail=true;
            Check(h.Save(288).Outcome==HudControlOutcome.SaveFailed);Check(h.Values["key.visibility"].ValueEquals(old)&&File.ReadAllText(h.Path)==before);
        });
        Case("external-edit-is-rejected-and-runtime-is-restored",()=>{
            var h=new Harness();var old=h.Values["key.visibility"];File.AppendAllText(h.Path,"# external\n");var before=File.ReadAllText(h.Path);
            Check(h.Save(288).Outcome==HudControlOutcome.StateConflict);Check(h.Values["key.visibility"].ValueEquals(old)&&File.ReadAllText(h.Path)==before);
        });
        Case("snapshot-after-runtime-change-uses-current-key",()=>{
            var h=new Harness();h.Values["key.visibility"]=HudControlValue.Integer(288);
            Check(h.State.ReadSnapshot().Effective["key.visibility"].TryGetInteger(out var code)&&code==288);
        });
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in Results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+" "+r.Error);
        return Results.Exists(r=>!r.Passed)?1:0;
    }
}
