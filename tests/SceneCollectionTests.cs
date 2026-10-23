#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Gear;
using Player;
using SNetwork;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Runtime;

static class SceneCollectionTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok){if(!ok)throw new Exception("assertion failed");}
    sealed class Fixture
    {
        public readonly LocalPlayerAgent Local=new(){Owner=new(){NickName="自己"}};public readonly PlayerAgent Peer=new(){Owner=new(){NickName="队员甲"},PlayerSlotIndex=1};
        public readonly PlayerBackpack Bag;public readonly Interact_Pickup_PickupItem Target=new(){m_nameFunc=()=>"数据立方体",InteractionMessage="拾取",TimerIsActive=true,InteractionTimerRel=.42f};
        public readonly HudGameContentSource Source;public HudRuntimeContext Current;public double Now=10;
        public HudContentCaptureRequest Request=>new(new(Current.CurrentView,1,Current.FrameNumber,Now),1,1);
        public Fixture()
        {
            LevelGeneration.Builder.CurrentFloor=new();PlayerBackpackManager.Bags.Clear();PlayerManager.Local=Local;PlayerManager.Current=new();SNet.Slots=new();InputMapper.Current=new();PlayerInteraction.InteractionEnabled=true;
            GuiManager.ThrowPlayerLayer=false;
            Local.Damage=new(){Owner=Local};Local.Inventory=new(){Owner=Local};Local.Interaction=new(){m_owner=Local,m_bestSelectedInteract=Target};Target.m_localPlayerInteractInfo=new(){Agent=Local};Peer.Sync!.m_agent=Peer;
            PlayerManager.Current.Slots[0]=Local;PlayerManager.Current.Slots[1]=Peer;SNet.Slots.Players[0]=Local.Owner;SNet.Slots.Players[1]=Peer.Owner;
            Bag=new(){Owner=Local.Owner};PlayerBackpackManager.Bag=Bag;
            var objectives=new PUI_GameObjectives();objectives.m_progressionObjectiveMap[1]=new(){m_header=new(){text="当前目标"},m_text=new(){text="寻找立方体"}};
            GuiManager.PlayerLayer=new(){m_player=Local,Inventory=new(){m_owner=Local.Owner},m_wardenObjective=objectives,m_objectiveTimer=new(){m_timerText=new(){text="02:18"},m_titleText=new(){text="撤离"}},TimerOnMainScreenVisible=true};GuiManager.Current=new();
            var player=new PlayerIdentity(Key(Local),Local.Owner!.Pointer.ToInt64(),0);var binding=new LocalViewBinding(player,Key(Local.PlayerSyncModel!),Key(Local.FPSCamera!.m_camera!),LevelGeneration.Builder.CurrentFloor.Pointer.ToInt64(),0);
            var view=new ThirdPersonViewState(1,new(Guid.NewGuid(),1),binding,player,1,1,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready);
            Current=new(true,true,true,view.Context,binding,view,100,Now,default);Source=new(()=>Now,()=>Current with{NowSeconds=Now});
        }
        static ObjectIdentity Key(UnityEngine.Object o)=>new(o.Pointer.ToInt64(),o.GetInstanceID());
        public HudContentSnapshot Capture(){Check(Source.TryCapture(Request,out var s));return s;}
        public ResourcePackFirstPerson Supply(PlayerAgent? recipient=null)
        {var pack=new ResourcePackFirstPerson(){Owner=Local,AmmoType=AmmoType.ResourcePackRel,Name="医疗包",m_actionReceiver=recipient??Peer,m_interactApplyResource=new(){TimerIsActive=true,InteractionTimerRel=.6f,m_localPlayerInteractInfo=new(){Agent=Local}}};Local.Inventory!.WieldedSlot=InventorySlot.ResourcePack;Local.Inventory.WieldedItem=pack;Bag.Items[InventorySlot.ResourcePack]=new(){Instance=pack};Bag.AmmoStorage!.Ammo[AmmoType.ResourcePackRel]=new(){AmmoInPack=.4f,AmmoMaxCap=1,CostOfBullet=.2f,NativeCount=2};return pack;}
        public EnemyScanner Scanner()
        {var scanner=new EnemyScanner(){Owner=Local};Local.Inventory!.WieldedItem=scanner;Local.Inventory.WieldedSlot=InventorySlot.GearClass;return scanner;}
        public void Ping(int slot=1)
        {GuiManager.Current!.m_playerPings[slot]=new(){m_playerIndex=slot,m_marker=new(){TrackingTrans=new(){position=new(4,2,-8)}}};}
    }
    static void Case(string name,Action<Fixture> action){try{action(new());results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        Case("actual-selected-target-name-action-key-progress",f=>{var x=f.Capture().Scene.Interaction;Check(x.Subject.State==HudValueState.Available&&x.Name.RawValue.Text=="数据立方体"&&x.Key.RawValue.Text=="E"&&Math.Abs(x.Progress.RawValue.Amount-.42)<.0001);});
        Case("key-rebind-is-read-from-keyboard-map",f=>{InputMapper.Current!.Binding="Mouse4";Check(f.Capture().Scene.Interaction.Key.RawValue.Text=="Mouse4");});
        Case("missing-input-map-is-explicit",f=>{InputMapper.Current=null;Check(f.Capture().Scene.Interaction.Key.State==HudValueState.Unreadable);});
        Case("unselected-target-clears-card",f=>{f.Capture();f.Target.IsSelected=false;Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Unknown);});
        Case("blocked-target-keeps-original-only",f=>{f.Target.IsBlocked=true;Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Unknown);});
        Case("disabled-controller-not-read",f=>{PlayerInteraction.InteractionEnabled=false;Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Unknown);});
        Case("foreign-interaction-controller-rejected",f=>{f.Local.Interaction!.m_owner=new();Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Unknown);});
        Case("foreign-interactor-has-no-hold-progress",f=>{f.Target.m_localPlayerInteractInfo!.Agent=f.Peer;Check(f.Capture().Scene.Interaction.Progress.State==HudValueState.Unsupported);});
        Case("cancelled-hold-clears-progress",f=>{f.Capture();f.Target.TimerIsActive=false;Check(f.Capture().Scene.Interaction.Progress.State==HudValueState.Unsupported);});
        Case("invalid-progress-not-zero",f=>{f.Target.InteractionTimerRel=float.NaN;Check(f.Capture().Scene.Interaction.Progress.State==HudValueState.Invalid);});
        Case("target-change-invalidates-between-intervals",f=>{var old=f.Capture().Scene.Interaction.Subject.RawValue;long version=f.Source.ChangeVersion;f.Local.Interaction!.m_bestSelectedInteract=new Interact_Generic(){m_displayName="新门"};Check(f.Source.ChangeVersion>version&&f.Capture().Scene.Interaction.Subject.RawValue.Revision>old.Revision);});
        Case("mid-read-target-change-is-discarded",f=>{InputMapper.Current!.OnBinding=()=>f.Local.Interaction!.m_bestSelectedInteract=new Interact_Generic();Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Unknown);});
        Case("non-timed-interaction-does-not-fake-progress",f=>{f.Local.Interaction!.m_bestSelectedInteract=new Interact_Generic();Check(f.Capture().Scene.Interaction.Progress.State==HudValueState.Unsupported);});
        Case("real-supply-recipient-and-use-count",f=>{f.Supply();var x=f.Capture().Scene.Supply;Check(x.Recipient.RawValue.Name=="队员甲"&&!x.Recipient.RawValue.Self&&x.Uses.RawValue.Amount==2&&x.Uses.RawValue.Unit==HudUnit.Uses);});
        Case("self-supply-is-explicit",f=>{f.Supply(f.Local);Check(f.Capture().Scene.Supply.Recipient.RawValue.Self);});
        Case("supply-zero-use-is-known",f=>{f.Supply();f.Bag.AmmoStorage!.Ammo[AmmoType.ResourcePackRel].NativeCount=0;Check(f.Capture().Scene.Supply.Uses.State==HudValueState.Available&&f.Capture().Scene.Supply.Uses.RawValue.Amount==0);});
        Case("unwielded-pack-does-not-leak-target",f=>{f.Supply();f.Local.Inventory!.WieldedSlot=InventorySlot.GearMelee;Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("idle-pack-does-not-show-old-recipient",f=>{var pack=f.Supply();pack.m_interactApplyResource!.TimerIsActive=false;Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("foreign-pack-owner-rejected",f=>{var pack=f.Supply();pack.Owner=f.Peer;Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("supply-foreign-interactor-rejected",f=>{var pack=f.Supply();pack.m_interactApplyResource!.m_localPlayerInteractInfo!.Agent=f.Peer;Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("supply-slot-owner-mismatch-rejected",f=>{f.Supply();SNet.Slots!.Players[1]=new();Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("offline-supply-recipient-rejected",f=>{f.Supply();f.Peer.Owner!.HasTimedOut=true;Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("recipient-switch-changes-subject-revision",f=>{var pack=f.Supply();var old=f.Capture().Scene.Supply.Subject.RawValue;pack.m_actionReceiver=f.Local;Check(f.Capture().Scene.Supply.Subject.RawValue.Revision>old.Revision);});
        Case("recipient-changes-during-name-read-discarded",f=>{var pack=f.Supply();int reads=0;pack.OnName=()=>{if(++reads==2)pack.m_actionReceiver=f.Local;};Check(f.Capture().Scene.Supply.Subject.State==HudValueState.Unknown);});
        Case("supply-count-failure-does-not-erase-recipient",f=>{f.Supply();f.Bag.AmmoStorage!.ThrowCount=true;var x=f.Capture().Scene.Supply;Check(x.Recipient.State==HudValueState.Available&&x.Uses.State==HudValueState.Unreadable);});
        Case("mission-uses-current-visible-published-text",f=>Check(f.Capture().Scene.Mission.Text.RawValue.Text=="寻找立方体"));
        Case("previous-objectives-not-promoted-to-current",f=>{GuiManager.PlayerLayer!.m_wardenObjective!.ShowPreviousObjectives=true;Check(f.Capture().Scene.Mission.Subject.State==HudValueState.Unknown);});
        Case("foreign-player-GUI-rejected",f=>{GuiManager.PlayerLayer!.m_player=f.Peer;var s=f.Capture();Check(s.Scene.Mission.Subject.State==HudValueState.Unknown&&s.Scene.Countdown.Subject.State==HudValueState.Unknown&&s.Personal.Health.State==HudValueState.Available);});
        Case("hidden-objectives-not-read",f=>{GuiManager.PlayerLayer!.m_wardenObjective!.gameObject.activeInHierarchy=false;Check(f.Capture().Scene.Mission.Subject.State==HudValueState.Unknown);});
        Case("provided-countdown-text-is-copied",f=>Check(f.Capture().Scene.Countdown.Text.RawValue.Text=="02:18"));
        Case("stopped-countdown-clears",f=>{f.Capture();GuiManager.PlayerLayer!.TimerOnMainScreenVisible=false;Check(f.Capture().Scene.Countdown.Subject.State==HudValueState.Unknown);});
        Case("countdown-rebuild-invalidates-cached-source",f=>{f.Capture();long before=f.Source.ChangeVersion;GuiManager.PlayerLayer!.m_objectiveTimer=new();Check(f.Source.ChangeVersion>before);});
        Case("GUI-exception-does-not-break-interaction-or-personal",f=>{GuiManager.ThrowPlayerLayer=true;var s=f.Capture();Check(s.Scene.Mission.Subject.State==HudValueState.Unknown&&s.Scene.Interaction.Subject.State==HudValueState.Available&&s.Personal.Health.State==HudValueState.Available);});
        Case("bioscan-only-closest-visible-source",f=>{f.Source.ObserveBioscan(new(){m_progressTarget=.7f});Check(Math.Abs(f.Capture().Scene.Scan.Progress.RawValue.Amount-.7)<.0001);});
        Case("hidden-scan-not-observed",f=>{f.Source.ObserveBioscan(new(){m_visible=false});Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("distant-scan-not-observed",f=>{f.Source.ObserveBioscan(new(){m_isClosestToPlayer=false});Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("stopped-scan-clears",f=>{var scan=new ChainedPuzzles.CP_Bioscan_Hud();f.Source.ObserveBioscan(scan);scan.m_visible=false;Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("scan-observation-time-not-renewed-by-capture",f=>{f.Source.ObserveBioscan(new());f.Now+=.11;Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("cancel-clears-scan-reference",f=>{f.Source.ObserveBioscan(new());f.Source.ClearTransient();Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("bioscan-from-auxiliary-scope-ignored",f=>{f.Current=f.Current with{Render=f.Current.Render with{Phase=RenderPhase.AuxiliaryActive}};f.Source.ObserveBioscan(new());f.Current=f.Current with{Render=default};Check(f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        Case("actual-teammate-ping-only",f=>{f.Ping();Check(f.Capture().Scene.Marker0.RawValue.Kind==HudSceneMarkerKind.Ping);});
        Case("hidden-ping-is-not-revealed",f=>{f.Ping();GuiManager.Current!.m_playerPings[1]!.Visible=false;Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("cross-dimension-ping-ignored",f=>{f.Ping();GuiManager.Current!.m_playerPings[1]!.m_marker!.m_isInPlayerDimension=false;Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("foreign-slot-ping-ignored",f=>{f.Ping();GuiManager.Current!.m_playerPings[1]!.m_playerIndex=2;Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("local-ping-not-attributed-to-teammate",f=>{f.Ping(0);Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("downed-rescue-position",f=>{f.Peer.Sync!.m_locomotionData=new(){State=PlayerLocomotion.PLOC_State.Downed};f.Peer.Position=new(6,0,-9);Check(f.Capture().Scene.Marker0.RawValue.Kind==HudSceneMarkerKind.Rescue&&f.Capture().Scene.Marker0.RawValue.Position.X==6);});
        Case("rescue-does-not-require-team-module-enabled",f=>{f.Source.SetTeamEnabled(false);f.Peer.Sync!.m_locomotionData=new(){State=PlayerLocomotion.PLOC_State.Downed};Check(f.Capture().Scene.Marker0.RawValue.Kind==HudSceneMarkerKind.Rescue);});
        Case("other-dimension-rescue-not-projected",f=>{f.Peer.DimensionIndex=1;f.Peer.Sync!.m_locomotionData=new(){State=PlayerLocomotion.PLOC_State.Downed};Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("scanner-only-already-detected-list",f=>{var scanner=f.Scanner();scanner.m_enemiesDetected.Add(new(){Position=new(4,0,-10)});Check(f.Capture().Scene.Marker0.RawValue.Kind==HudSceneMarkerKind.Detected);});
        Case("scanner-foreign-owner-not-used",f=>{var scanner=f.Scanner();scanner.Owner=f.Peer;scanner.m_enemiesDetected.Add(new());Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("dead-and-other-dimension-contacts-filtered",f=>{var scanner=f.Scanner();scanner.m_enemiesDetected.Add(new(){Alive=false});scanner.m_enemiesDetected.Add(new(){DimensionIndex=1});Check(f.Capture().Scene.Marker0.State==HudValueState.Unknown);});
        Case("scanner-candidates-bounded",f=>{var scanner=f.Scanner();for(int i=0;i<100;i++)scanner.m_enemiesDetected.Add(new(){Position=new(i,0,-10)});f.Source.ConfigureScene(HudSceneCollectionOptions.Default with{CandidateLimit=2});var scene=f.Capture().Scene;Check(scene.Marker0.State==HudValueState.Available&&scene.Marker1.State==HudValueState.Available&&scene.Marker2.State==HudValueState.Unknown);});
        Case("scene-disabled-preserves-personal",f=>{f.Source.ConfigureScene(HudSceneCollectionOptions.Default with{Enabled=false});var s=f.Capture();Check(s.Scene==default&&s.Personal.Health.State==HudValueState.Available);});
        Case("off-main-observer-and-config-ignored",f=>{Task.Run(()=>{f.Source.ObserveBioscan(new());f.Source.ConfigureScene(HudSceneCollectionOptions.Default with{Enabled=false});}).GetAwaiter().GetResult();Check(f.Capture().Scene.Interaction.Subject.State==HudValueState.Available&&f.Capture().Scene.Scan.Subject.State==HudValueState.Unknown);});
        bool passed=results.All(x=>x.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,ActualCollectorUsed=true,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));foreach(var x in results.Where(x=>!x.Passed))Console.WriteLine("FAIL "+x.Name+": "+x.Error);Console.WriteLine($"Scene collection: {results.Count}; passed={passed}");return passed?0:1;
    }
}
