using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.CameraSystem;

static class CameraFeedbackTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool v,string why="assertion failed"){if(!v)throw new Exception(why);}
    static void Case(string n,Action test){try{test();results.Add(new(n,true,null));}catch(Exception e){results.Add(new(n,false,e.ToString()));}}
    static ObservationFeedbackFrame Frame(bool held=false,bool active=false,ObservationBlockReason block=ObservationBlockReason.None,int frame=1,int hook=-1,int entries=0,int exits=0,bool handoff=false,string exit="",string key="右Alt")
        =>new(true,held,active,block,frame,hook,entries,exits,handoff,exit,key,"F6");
    static void Main(string[] args)
    {
        Case("guide-explains-the-visible-effect-and-control-once",()=>{
            var s=new CameraObservationFeedback();string m=s.Tick(Frame(),0);Check(m.Contains("右Alt")&&m.Contains("移动鼠标")&&m.Contains("绕看人物")&&m.Contains("F6"));
            for(int i=0;i<100;i++)Check(s.Tick(Frame(),i)==null);
        });
        Case("press-is-confirmed-even-if-native-input-hook-never-arrives",()=>{
            var s=new CameraObservationFeedback();string m=s.Tick(Frame(true,frame:10),1);Check(m.Contains("已收到")&&!m.Contains("已进入"));
            Check(s.Tick(Frame(true,frame:11),1.1f)==null);m=s.Tick(Frame(true,frame:40),1.5f);Check(m.Contains("回调")&&m.Contains("F8"));
            for(int i=0;i<100;i++)Check(s.Tick(Frame(true,frame:100+i),2+i)==null);
        });
        Case("hook-without-entry-has-a-distinct-message",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,frame:10),1);string m=s.Tick(Frame(true,frame:40,hook:39),1.5f);Check(m.Contains("和观察输入")&&m.Contains("F8"));
        });
        foreach(var reason in Enum.GetValues<ObservationBlockReason>().Where(r=>r!=ObservationBlockReason.None))
            Case("press-reports-block-"+reason,()=>{
                var s=new CameraObservationFeedback();string m=s.Tick(Frame(true,block:reason),0);Check(m.Contains("未进入")&&m.Contains(CameraObservationFeedback.Describe(reason,"F6"))&&m.Contains("再按"));
                Check(s.Tick(Frame(true,block:reason),1)==null);
            });
        Case("entry-confirms-camera-and-character-behavior",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true),0);string m=s.Tick(Frame(true,true,entries:1),.01f);Check(m.Contains("已进入")&&m.Contains("人物保持朝向"));Check(s.Tick(Frame(true,true,entries:1),1)==null);
        });
        Case("release-confirms-native-direction-handoff",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,true,entries:1),0);string m=s.Tick(Frame(entries:1,exits:1,handoff:true,exit:"release-or-action"),1);
            Check(m.Contains("已松开")&&m.Contains("已交接"));Check(s.Tick(Frame(entries:1,exits:1),2)==null);
        });
        Case("collision-exit-does-not-claim-a-handoff",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,true,entries:1),0);string m=s.Tick(Frame(true,entries:1,exits:1,exit:"collision"),1);Check(m.Contains("空间不足")&&m.Contains("已回原生")&&!m.Contains("已交接"));
        });
        Case("brief-entry-and-exit-between-ui-ticks-is-reported",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true),0);string m=s.Tick(Frame(true,entries:1,exits:1,handoff:true,exit:"fire"),1);Check(m.Contains("短暂进入")&&m.Contains("开火"));
        });
        Case("holding-a-denied-key-does-not-spam-chat",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,block:ObservationBlockReason.Moving),0);for(int i=1;i<10000;i++)Check(s.Tick(Frame(true,block:ObservationBlockReason.Moving),i)==null);
        });
        Case("release-and-repress-allows-a-new-denial-message",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,block:ObservationBlockReason.Aiming),0);s.Tick(Frame(),1);Check(s.Tick(Frame(true,block:ObservationBlockReason.Moving),2).Contains("站定"));
        });
        Case("changed-block-after-pending-press-is-explained",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true),0);Check(s.Tick(Frame(true,block:ObservationBlockReason.Reloading),.5f).Contains("换弹"));
        });
        Case("custom-key-label-is-shown",()=>{
            var s=new CameraObservationFeedback();Check(s.Tick(Frame(key:"Mouse4"),0).Contains("Mouse4"));
        });
        Case("movement-feedback-includes-the-actual-input-not-a-guess",()=>{
            var s=new CameraObservationFeedback();var f=Frame(true,block:ObservationBlockReason.Moving) with{Motion=new(1,.1f,-.25f,new(0,-2,0))};
            string m=s.Tick(f,0);Check(m.Contains("横轴0.1")&&m.Contains("纵轴-0.25")&&m.Contains("移动输入"));
        });
        Case("horizontal-speed-feedback-excludes-vertical-gravity",()=>{
            var s=new CameraObservationFeedback();var f=Frame(true,block:ObservationBlockReason.Moving) with{Motion=new(1,0,0,new(.1f,-2,0))};
            string m=s.Tick(f,0);Check(m.Contains("0.1米/秒")&&!m.Contains("2米/秒")&&!m.Contains("摇杆"));
        });
        Case("guide-waits-for-game-focus",()=>{
            var s=new CameraObservationFeedback();Check(s.Tick(Frame() with{CanIntroduce=false},0)==null);Check(s.Tick(Frame(),1)!=null);
        });
        Case("context-reset-reintroduces-the-controls",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(),0);s.Reset();Check(s.Tick(Frame(),1).Contains("C2a操作"));
        });
        Case("reset-does-not-replay-historical-entry-and-exit-counters",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true,true,entries:3,exits:2),0);s.Reset();string m=s.Tick(Frame(entries:3,exits:3),1);Check(m.Contains("C2a操作")&&!m.Contains("短暂"));
        });
        Case("waiting-release-includes-the-original-rejection",()=>{
            var s=new CameraObservationFeedback();string m=s.Tick(Frame(true,block:ObservationBlockReason.WaitingRelease) with{LastRejected=ObservationBlockReason.NoDisplay},0);Check(m.Contains("上次")&&m.Contains("稳定显示"));
        });
        Case("callback-warning-uses-the-configured-snapshot-key",()=>{
            var s=new CameraObservationFeedback();var f=Frame(true) with{Snapshot="F12"};s.Tick(f,0);Check(s.Tick(f,.5f).Contains("F12"));
        });
        Case("disabled-feature-guide-does-not-claim-readiness",()=>{
            var s=new CameraObservationFeedback();Check(s.Tick(Frame(block:ObservationBlockReason.FeatureDisabled),0).Contains("配置中关闭"));
        });
        Case("clock-rewind-does-not-produce-per-frame-notices",()=>{
            var s=new CameraObservationFeedback();s.Tick(Frame(true),10);Check(s.Tick(Frame(true),1)==null);Check(s.Tick(Frame(true),1.1f)==null);Check(s.Tick(Frame(true),1.5f)!=null);
        });
        Case("unchanged-active-feedback-allocates-no-managed-bytes",()=>{
            var s=new CameraObservationFeedback();var f=Frame(true,true,entries:1);for(int i=0;i<4096;i++)s.Tick(f,i);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)s.Tick(f,i);Check(GC.GetAllocatedBytesForCurrentThread()==before);
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Scope="Production feedback state machine, no game UI/input/network execution",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
