using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.Runtime;

// Environment/native wrappers only. The state transitions are ThirdPersonObservation.cs itself.
namespace Il2CppInterop.Runtime.Attributes { sealed class HideFromIl2CppAttribute:Attribute {} }
class NativeObject { static long next=1; public IntPtr Pointer=new(next++); }
namespace UnityEngine
{
    enum KeyCode { LeftAlt,RightAlt,F6,F8 }
    struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
    }
    static class Input {public static bool Held;public static KeyCode HeldKey;public static bool GetKey(KeyCode key)=>Held&&key==HeldKey;}
    static class Time {public static int frameCount=100;public static float unscaledDeltaTime=1/60f,unscaledTime;}
}
enum InputAction { MoveHorizontal,MoveVertical,Aim,Fire,Use,Reload }
enum eGameEventChatLogType { GameEvent }
static class GuiManager
{
    public static Layer PlayerLayer=new();
    public class Layer {public EventLog m_gameEventLog=new();}
    public class EventLog
    {
        public readonly List<string> Messages=new();public bool Throw;
        public void AddLogItem(string text,eGameEventChatLogType type){if(Throw)throw new Exception("UI unavailable");Messages.Add(text);}
    }
}
static class InputMapper
{
    public static readonly Dictionary<InputAction,float> Axes=new();
    public static readonly HashSet<InputAction> Buttons=new();
    public static readonly Func<InputAction,int,float> GetAxis=(a,_)=>Axes.TryGetValue(a,out float value)?value:0;
    public static readonly Func<InputAction,int,bool> GetButton=(a,_)=>Buttons.Contains(a);
}
class LookCameraController:NativeObject {public float m_yaw,m_pitch;}
class FPSCamera:LookCameraController
{
    public UnityEngine.Vector3 Assigned;
    public int SetCalls,FlatCalls;
    public void SetLookDirection(UnityEngine.Vector3 v)
    {Assigned=v;SetCalls++;m_yaw=MathF.Atan2(v.x,v.z)*180/MathF.PI;m_pitch=-MathF.Asin(v.y)*180/MathF.PI;}
    public void UpdateFlatTransform(){FlatCalls++;}
}
class ItemEquippable:NativeObject {public bool IsReloading;}
class Holder {public bool ItemIsBusy,ItemAimTrigger;}
class Inventory {public ItemEquippable WieldedItem=new();}
namespace Player
{
    class PlayerLocomotion:NativeObject {public UnityEngine.Vector3 Velocity;}
    class LocalPlayerAgent:NativeObject
    {
        public NativeObject Owner=new(),PlayerSyncModel=new();
        public FPSCamera FPSCamera=new();public Inventory Inventory=new();public Holder FPItemHolder=new();
        public PlayerLocomotion Locomotion=new();public int InputFilter=0;
    }
}
namespace PreLuna.ThirdPerson.Runtime
{
    sealed class Entry<T> {public T Value;public Entry(T value){Value=value;}}
    sealed class Settings
    {public Entry<UnityEngine.KeyCode> Observe=new(UnityEngine.KeyCode.RightAlt),Toggle=new(UnityEngine.KeyCode.F6),Snapshot=new(UnityEngine.KeyCode.F8);public Entry<bool> IndependentLook=new(true),Enabled=new(true);public Entry<float> ObserveYaw=new(65);}
    sealed class MockLog {public int Warnings;public void LogInfo(string s){}public void LogWarning(string s){Warnings++;}}
    sealed class Plugin {public static Plugin Instance=new();public Settings Settings=new();public MockLog Log=new();}
    sealed class MockState {public bool Requested=true,Faulted;}
    sealed class MockGesture {public bool WantsAim;}
    sealed class MockAim {public bool Active=true;public MockGesture Gesture=new();}
    readonly record struct HeadObservation(bool Available,bool ControlsBody,bool ReplacesMainView);
    sealed class MockHeadProbe {public HeadObservation Value=new(true,false,false);public HeadObservation Read()=>Value;}
    static class ConvertView
    {public static UnityEngine.Vector3 U(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);public static System.Numerics.Vector3 N(UnityEngine.Vector3 v)=>new(v.x,v.y,v.z);}
    public sealed partial class ThirdPersonRuntime
    {
        internal readonly MockState state=new();internal readonly MockAim aim=new();
        internal MockHeadProbe headCameraProbe=new();internal bool nativeDisplay;
        internal static Player.LocalPlayerAgent LocalValue;
        internal FallbackReason GateResult;internal int AimPolls;
        static Player.LocalPlayerAgent Local()=>LocalValue;
        static long Id(object obj)=>obj is NativeObject n?n.Pointer.ToInt64():0;
        FallbackReason Gate(Player.LocalPlayerAgent local)=>local==null?FallbackReason.NoPlayer:GateResult;
        void PollAim(Player.LocalPlayerAgent local){AimPolls++;MaintainObservation(local);}
        void BeforeLogic(FPSCamera camera){}
        bool FocusAllowed()=>GateResult!=FallbackReason.Focus;
        internal void Feedback()=>TickObservationFeedback(LocalValue);
        internal int KeyPresses=>observationKeyPresses;
        internal int LocalHooks=>localLookHookCalls;
        internal PreLuna.ThirdPerson.CameraSystem.ObservationMovement CurrentMovement=>observationMovement;
        internal PreLuna.ThirdPerson.CameraSystem.ObservationMovement RejectedMovement=>lastRejectedMovement;
        internal void Fresh()=>lastLookRenderFrame=UnityEngine.Time.frameCount;
        internal bool Observing=>lookInput.Orientation.Active;
        internal bool Waiting=>lookInput.Orientation.BlockedUntilRelease;
        internal int Handoffs=>lookHandoffs;
        internal void Maintain()=>MaintainObservation(LocalValue);
        internal void Release()=>ReleaseObservation();
        internal void Delta(float yaw,float pitch)
        {
            var c=LocalValue.FPSCamera;BeginLook(c,out var s);
            c.m_yaw+=yaw;c.m_pitch+=pitch;FinishLook(s,true);
        }
        internal System.Numerics.Vector3 Observer=>lookInput.Orientation.Forward(LocalValue.FPSCamera.m_yaw,LocalValue.FPSCamera.m_pitch);
    }
}
static class CameraObservationTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool v,string why="assertion failed"){if(!v)throw new Exception(why);}
    static void Near(float a,float b)=>Check(Math.Abs(a-b)<.0001f,$"{a} != {b}");
    static (ThirdPersonRuntime,Player.LocalPlayerAgent) Ready()
    {
        Plugin.Instance=new();GuiManager.PlayerLayer=new();UnityEngine.Input.Held=true;UnityEngine.Input.HeldKey=UnityEngine.KeyCode.RightAlt;UnityEngine.Time.frameCount=100;UnityEngine.Time.unscaledDeltaTime=1/60f;UnityEngine.Time.unscaledTime=0;
        InputMapper.Axes.Clear();InputMapper.Buttons.Clear();
        var p=new Player.LocalPlayerAgent();ThirdPersonRuntime.LocalValue=p;var r=new ThirdPersonRuntime();r.Fresh();return(r,p);
    }
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static void Main(string[] args)
    {
        Case("valid-stationary-local-owner-can-observe-without-native-turn",()=>{
            var(r,p)=Ready();r.Delta(30,10);Check(r.Observing);Near(p.FPSCamera.m_yaw,0);Near(p.FPSCamera.m_pitch,0);Check(p.FPSCamera.SetCalls==0);
        });
        Case("stationary-with-native-ground-vertical-velocity-can-enter-and-stay",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,-2,0);r.Delta(30,10);r.Maintain();r.Feedback();
            Check(r.Observing);Near(p.FPSCamera.m_yaw,0);Near(r.CurrentMovement.HorizontalSpeed,0);Near(r.CurrentMovement.Velocity.Y,-2);
            Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Single().Contains("已进入"));
        });
        Case("vertical-component-does-not-change-horizontal-stationary-threshold",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(.049f,-10,0);r.Delta(20,0);Check(r.Observing);
        });
        Case("horizontal-speed-at-the-threshold-still-blocks-entry",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(.05f,-2,0);r.Delta(20,0);Check(!r.Observing);
        });
        Case("diagonal-horizontal-speed-is-not-checked-per-axis",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(.04f,-2,.04f);r.Delta(20,0);Check(!r.Observing);
        });
        Case("movement-input-still-hands-off-after-ground-vertical-entry",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,-2,0);r.Delta(35,0);InputMapper.Axes[InputAction.MoveVertical]=.01f;
            r.BeforeMove(p.Locomotion);Check(!r.Observing&&p.FPSCamera.SetCalls==1&&p.FPSCamera.FlatCalls==1);Near(p.FPSCamera.m_yaw,35);
        });
        Case("input-threshold-is-preserved-instead-of-silently-loosened",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,-2,0);InputMapper.Axes[InputAction.MoveHorizontal]=.0005f;r.Delta(20,0);Check(r.Observing);
            InputMapper.Axes[InputAction.MoveHorizontal]=.0011f;r.BeforeMove(p.Locomotion);Check(!r.Observing);
        });
        Case("unsupported-locomotion-is-still-blocked-with-zero-horizontal-speed",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,4,0);r.GateResult=FallbackReason.UnsupportedState;r.Delta(20,0);Check(!r.Observing);
        });
        Case("input-denial-displays-measured-axes",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,-2,0);InputMapper.Axes[InputAction.MoveHorizontal]=.25f;r.Feedback();
            string m=GuiManager.PlayerLayer.m_gameEventLog.Messages.Single();Check(m.Contains("横轴0.25")&&m.Contains("纵轴0")&&m.Contains("移动输入"));
        });
        Case("horizontal-drift-denial-does-not-blame-a-joystick",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(.2f,-2,0);r.Feedback();string m=GuiManager.PlayerLayer.m_gameEventLog.Messages.Single();
            Check(m.Contains("水平")&&m.Contains("0.2")&&!m.Contains("摇杆"));Near(r.RejectedMovement.Velocity.Y,-2);
        });
        Case("rejected-movement-sample-survives-later-player-loss",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(.2f,-2,0);r.Delta(20,0);ThirdPersonRuntime.LocalValue=null;r.Feedback();
            Check(r.RejectedMovement.Valid&&r.RejectedMovement.Frame==100);Near(r.RejectedMovement.HorizontalSpeed,.2f);
        });
        Case("invalid-native-movement-data-does-not-enable-observation",()=>{
            var(r,p)=Ready();p.Locomotion.Velocity=new(0,float.NaN,0);r.Delta(20,0);r.Feedback();Check(!r.Observing&&!r.CurrentMovement.Valid);
            Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Single().Contains("移动数据暂不可用"));
        });
        Case("invalid-movement-axis-is-rejected",()=>{
            var(r,p)=Ready();InputMapper.Axes[InputAction.MoveHorizontal]=float.NaN;r.Delta(20,0);Check(!r.Observing&&!r.CurrentMovement.Valid);
        });
        Case("native-local-chat-reports-a-press-even-without-look-hook",()=>{
            var(r,p)=Ready();r.Feedback();Check(r.KeyPresses==1&&r.LocalHooks==0&&GuiManager.PlayerLayer.m_gameEventLog.Messages.Single().Contains("已收到"));
            UnityEngine.Time.unscaledTime=.5f;r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Last().Contains("回调"));
            for(int i=0;i<100;i++)r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Count==2);
        });
        Case("native-local-chat-explains-moving-denial",()=>{
            var(r,p)=Ready();InputMapper.Axes[InputAction.MoveVertical]=1;r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Single().Contains("站定"));Check(!r.Observing);
        });
        Case("native-local-chat-reports-entry-and-release",()=>{
            var(r,p)=Ready();r.Delta(20,0);r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Single().Contains("已进入"));
            UnityEngine.Input.Held=false;r.Maintain();r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Count==2&&GuiManager.PlayerLayer.m_gameEventLog.Messages.Last().Contains("已交接"));
        });
        Case("unavailable-chat-does-not-fault-or-disable-observation",()=>{
            var(r,p)=Ready();GuiManager.PlayerLayer.m_gameEventLog.Throw=true;r.Delta(20,0);r.Feedback();r.Feedback();Check(r.Observing&&!r.state.Faulted&&Plugin.Instance.Log.Warnings==1);
            GuiManager.PlayerLayer.m_gameEventLog.Throw=false;UnityEngine.Input.Held=false;r.Maintain();r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Count==1);
        });
        Case("missing-chat-ui-does-not-consume-the-first-notice",()=>{
            var(r,p)=Ready();GuiManager.PlayerLayer=null;r.Feedback();GuiManager.PlayerLayer=new();r.Feedback();Check(GuiManager.PlayerLayer.m_gameEventLog.Messages.Count==1&&r.KeyPresses==1);
        });
        Case("look-callback-counter-is-local-owner-specific",()=>{
            var(r,p)=Ready();r.BeginLook(new FPSCamera(),out var s);r.FinishLook(s,true);Check(r.LocalHooks==0);r.Delta(2,0);Check(r.LocalHooks==1);
        });
        Case("left-Alt-does-not-activate-right-Alt-observation",()=>{
            var(r,p)=Ready();UnityEngine.Input.HeldKey=UnityEngine.KeyCode.LeftAlt;r.Delta(30,10);
            Check(!r.Observing&&r.AimPolls==0);Near(p.FPSCamera.m_yaw,30);Near(p.FPSCamera.m_pitch,10);
        });
        Case("observation-uses-configured-key-instead-of-a-hardcoded-Alt",()=>{
            var(r,p)=Ready();Plugin.Instance.Settings.Observe.Value=UnityEngine.KeyCode.LeftAlt;r.Delta(30,10);Check(!r.Observing);
            UnityEngine.Input.HeldKey=UnityEngine.KeyCode.LeftAlt;r.Delta(5,2);Check(r.Observing);Near(p.FPSCamera.m_yaw,30);Near(p.FPSCamera.m_pitch,10);
        });
        Case("remote-camera-input-is-not-captured",()=>{
            var(r,p)=Ready();var other=new FPSCamera();r.BeginLook(other,out var s);other.m_yaw=30;r.FinishLook(s,true);Check(!r.Observing);Near(other.m_yaw,30);
        });
        Case("disabled-C2-retains-native-input-result",()=>{
            var(r,p)=Ready();Plugin.Instance.Settings.IndependentLook.Value=false;r.Delta(30,10);Check(!r.Observing&&r.AimPolls==0);Near(p.FPSCamera.m_yaw,30);Near(p.FPSCamera.m_pitch,10);
        });
        Case("ordinary-look-without-Alt-does-not-add-aim-polls",()=>{var(r,p)=Ready();UnityEngine.Input.Held=false;r.Delta(30,10);Check(!r.Observing&&r.AimPolls==0);Near(p.FPSCamera.m_yaw,30);Near(p.FPSCamera.m_pitch,10);});
        Case("key-release-hands-off-world-direction-once",()=>{
            var(r,p)=Ready();r.Delta(40,-15);var f=r.Observer;UnityEngine.Input.Held=false;r.Maintain();r.Maintain();
            Check(!r.Observing&&r.Handoffs==1&&p.FPSCamera.SetCalls==1&&p.FPSCamera.FlatCalls==1);Near(p.FPSCamera.Assigned.x,f.X);Near(p.FPSCamera.Assigned.y,f.Y);Near(p.FPSCamera.Assigned.z,f.Z);
        });
        Case("movement-prefix-hands-off-before-native-flat-basis-is-read",()=>{
            var(r,p)=Ready();r.Delta(50,0);InputMapper.Axes[InputAction.MoveVertical]=1;r.BeforeMove(p.Locomotion);
            Check(!r.Observing&&r.Waiting&&p.FPSCamera.FlatCalls==1);Near(p.FPSCamera.m_yaw,50);
        });
        Case("remote-movement-does-not-end-local-observation",()=>{
            var(r,p)=Ready();r.Delta(20,0);InputMapper.Axes[InputAction.MoveHorizontal]=1;r.BeforeMove(new Player.PlayerLocomotion());Check(r.Observing&&p.FPSCamera.SetCalls==0);
        });
        foreach(var action in new[]{InputAction.Aim,InputAction.Fire,InputAction.Use,InputAction.Reload})
            Case("action-hands-off-"+action,()=>{var(r,p)=Ready();r.Delta(25,5);InputMapper.Buttons.Add(action);r.Maintain();Check(!r.Observing&&r.Waiting&&p.FPSCamera.FlatCalls==1);Near(p.FPSCamera.m_yaw,25);});
        Case("shoulder-aim-intent-hands-off-even-after-button-up",()=>{
            var(r,p)=Ready();r.Delta(35,0);r.aim.Gesture.WantsAim=true;r.Maintain();Check(!r.Observing&&r.Handoffs==1);Near(p.FPSCamera.m_yaw,35);
        });
        Case("interruption-requires-release-before-reactivation",()=>{
            var(r,p)=Ready();r.Delta(25,0);InputMapper.Buttons.Add(InputAction.Fire);r.Maintain();InputMapper.Buttons.Clear();r.Delta(5,0);Check(!r.Observing&&r.Waiting);
            UnityEngine.Input.Held=false;r.Maintain();UnityEngine.Input.Held=true;r.Delta(5,0);Check(r.Observing);
        });
        foreach(var gate in new[]{FallbackReason.Focus,FallbackReason.ControlTaken,FallbackReason.UnsupportedState})
            Case("lost-control-discards-without-writing-native-"+gate,()=>{var(r,p)=Ready();r.Delta(30,0);r.GateResult=gate;r.Maintain();Check(!r.Observing&&r.Waiting&&p.FPSCamera.SetCalls==0);Near(p.FPSCamera.m_yaw,0);});
        foreach(int change in new[]{0,1,2,3})
            Case("identity-change-discards-old-observer-"+change,()=>{
                var(r,p)=Ready();r.Delta(30,0);
                if(change==0)ThirdPersonRuntime.LocalValue=new Player.LocalPlayerAgent();
                if(change==1)p.Owner=new NativeObject();if(change==2)p.PlayerSyncModel=new NativeObject();if(change==3)p.Inventory.WieldedItem=new ItemEquippable();
                r.Maintain();Check(!r.Observing&&r.Handoffs==0&&p.FPSCamera.SetCalls==0&&ThirdPersonRuntime.LocalValue.FPSCamera.SetCalls==0);
            });
        foreach(var head in new[]{new HeadObservation(true,true,false),new HeadObservation(true,false,true),new HeadObservation(false,false,false)})
            Case("head-camera-body-preview-or-unknown-discards-"+head,()=>{var(r,p)=Ready();r.Delta(30,0);r.headCameraProbe.Value=head;r.Maintain();Check(!r.Observing&&p.FPSCamera.SetCalls==0);});
        Case("stale-render-does-not-activate-hidden-observation",()=>{var(r,p)=Ready();UnityEngine.Time.frameCount+=2;r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        Case("moving-before-entry-keeps-native-direction",()=>{var(r,p)=Ready();p.Locomotion.Velocity=new(.1f,0,0);r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        Case("native-first-person-display-prevents-hidden-observation",()=>{var(r,p)=Ready();r.nativeDisplay=true;r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        Case("faulted-runtime-does-not-capture-input",()=>{var(r,p)=Ready();r.state.Faulted=true;r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        Case("unsupported-item-does-not-capture-input",()=>{var(r,p)=Ready();r.aim.Active=false;r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        foreach(int busy in new[]{0,1,2})
            Case("reload-busy-or-native-aim-interrupts-observation-"+busy,()=>{
                var(r,p)=Ready();r.Delta(20,0);if(busy==0)p.Inventory.WieldedItem.IsReloading=true;if(busy==1)p.FPItemHolder.ItemIsBusy=true;if(busy==2)p.FPItemHolder.ItemAimTrigger=true;
                r.Maintain();Check(!r.Observing&&r.Waiting&&p.FPSCamera.FlatCalls==1);
            });
        Case("long-frame-discards-stale-observer",()=>{var(r,p)=Ready();r.Delta(20,0);UnityEngine.Time.unscaledDeltaTime=.3f;r.Maintain();Check(!r.Observing&&p.FPSCamera.SetCalls==0);});
        Case("long-frame-does-not-start-new-observation",()=>{var(r,p)=Ready();UnityEngine.Time.unscaledDeltaTime=.3f;r.Delta(20,0);Check(!r.Observing);Near(p.FPSCamera.m_yaw,20);});
        Case("stale-render-discards-without-replaying-old-direction",()=>{var(r,p)=Ready();r.Delta(20,0);UnityEngine.Time.frameCount+=2;r.Maintain();Check(!r.Observing&&p.FPSCamera.SetCalls==0);});
        Case("camera-replacement-does-not-apply-old-offset-to-new-camera",()=>{var(r,p)=Ready();r.Delta(20,0);var old=p.FPSCamera;p.FPSCamera=new();r.Maintain();Check(!r.Observing&&p.FPSCamera.SetCalls==0&&old.SetCalls==0);});
        Case("ordinary-release-hands-off-and-invalidates-render-eligibility",()=>{var(r,p)=Ready();r.Delta(20,0);r.Release();Check(!r.Observing&&r.Handoffs==1);r.Delta(5,0);Check(!r.Observing);});
        Case("control-release-does-not-write-into-a-borrowed-view",()=>{var(r,p)=Ready();r.Delta(20,0);r.GateResult=FallbackReason.ControlTaken;r.Release();Check(!r.Observing&&p.FPSCamera.SetCalls==0);});
        Case("release-during-capture-restores-without-replaying-observation",()=>{
            var(r,p)=Ready();r.Delta(20,0);r.BeginLook(p.FPSCamera,out var s);p.FPSCamera.m_yaw+=10;r.Release();r.FinishLook(s,true);Check(!r.Observing&&p.FPSCamera.SetCalls==0);Near(p.FPSCamera.m_yaw,0);Near(p.FPSCamera.m_pitch,0);
        });
        Case("disabling-during-observation-hands-off-and-stays-disabled",()=>{var(r,p)=Ready();r.Delta(20,0);Plugin.Instance.Settings.IndependentLook.Value=false;r.Maintain();Check(!r.Observing&&r.Handoffs==1);r.Delta(5,0);Check(!r.Observing);});
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Scope="Production C2a observation state transitions; environment, native wrappers and existing Gate/Aim services are substitutes, not game execution",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
