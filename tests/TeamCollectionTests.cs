#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Player;
using SNetwork;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Runtime;

static class TeamCollectionTests
{
    sealed record Result(string Name,bool Passed,string? Error);
    static readonly List<Result> results=new();
    static void Check(bool ok){if(!ok)throw new Exception("assertion failed");}
    sealed class Fixture
    {
        public readonly LocalPlayerAgent Local=new(){Owner=new()}; public readonly PlayerAgent Peer=new(){Owner=new(){NickName="队员甲"},PlayerSlotIndex=1};
        public readonly PlayerBackpack Bag,PeerBag;public readonly HudGameContentSource Source;public HudRuntimeContext Current;public double Now=10;
        public HudContentCaptureRequest Request=>new(new(Current.CurrentView,1,Current.FrameNumber,Now),1,1);
        public Fixture()
        {
            LevelGeneration.Builder.CurrentFloor=new();PlayerBackpackManager.Bags.Clear();PlayerManager.Local=Local;PlayerManager.Current=new();SNet.Slots=new();
            Local.Damage=new(){Owner=Local};Local.Inventory=new(){Owner=Local};Peer.Damage=new(){Owner=Peer};
            Peer.Sync!.m_agent=Peer;
            PlayerManager.Current.Slots[0]=Local;PlayerManager.Current.Slots[1]=Peer;SNet.Slots.Players[0]=Local.Owner;SNet.Slots.Players[1]=Peer.Owner;
            Bag=new(){Owner=Local.Owner};PeerBag=new(){Owner=Peer.Owner};PlayerBackpackManager.Bag=Bag;PlayerBackpackManager.Bags[Peer.Owner!.Pointer.ToInt64()]=PeerBag;
            PeerBag.AmmoStorage!.Ammo[AmmoType.Standard]=new(){RelInPack=.6f};PeerBag.AmmoStorage.Ammo[AmmoType.Special]=new(){RelInPack=.4f};PeerBag.AmmoStorage.Ammo[AmmoType.Class]=new(){RelInPack=.5f};
            Peer.NavMarker=new(){Player=Peer,m_marker=new()};
            var player=new PlayerIdentity(Key(Local),Local.Owner!.Pointer.ToInt64(),0);var binding=new LocalViewBinding(player,Key(Local.PlayerSyncModel!),Key(Local.FPSCamera!.m_camera!),LevelGeneration.Builder.CurrentFloor.Pointer.ToInt64(),0);
            var view=new ThirdPersonViewState(1,new(Guid.NewGuid(),1),binding,player,1,1,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready);
            Current=new(true,true,true,view.Context,binding,view,100,Now,default);Source=new(()=>Now,()=>Current with{NowSeconds=Now});
        }
        static ObjectIdentity Key(UnityEngine.Object o)=>new(o.Pointer.ToInt64(),o.GetInstanceID());
        public HudContentSnapshot Capture(){Check(Source.TryCapture(Request,out var s));return s;}
        public HudTeammateSnapshot Row()=>Capture().Team.Slot1;
    }
    static void Case(string name,Action<Fixture> action){try{action(new());results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        Case("actual-peer-identity-and-ratios",f=>{var row=f.Row();Check(row.Identity.RawValue.Owner==f.Peer.Owner!.Pointer.ToInt64()&&row.Name.RawValue.Text=="队员甲"&&Math.Abs(row.PrimaryRatio.RawValue.Amount-.6)<.0001);});
        Case("other-empty-slots-are-explicit",f=>{var s=f.Capture();Check(s.Team.Slot2.Identity.State==HudValueState.NotEquipped&&s.Team.Roster.RawValue.OccupiedMask==2);});
        Case("local-slot-not-collected-as-peer",f=>Check(f.Capture().Team.Slot0.Identity.State==HudValueState.Unknown));
        Case("bot-with-no-network-connection-is-connected",f=>{f.Peer.Owner!.IsBot=true;f.Peer.Owner.ConnectionStatus=ConnectionStatus.NotConnected;Check(f.Row().Connection.RawValue.Kind==HudPeerState.Connected&&f.Row().Identity.RawValue.IsBot);});
        Case("offline-keeps-name-not-old-health",f=>{f.Capture();f.Peer.Owner!.HasTimedOut=true;var row=f.Row();Check(row.Connection.RawValue.Kind==HudPeerState.Offline&&row.Health.State==HudValueState.Unreadable);});
        Case("out-of-sync-does-not-publish-resources",f=>{f.Peer.Owner!.IsOutOfSync=true;Check(f.Row().Connection.RawValue.Kind==HudPeerState.Synchronizing&&f.Row().ToolRatio.State==HudValueState.Unreadable);});
        Case("missing-agent-keeps-connection-identity",f=>{PlayerManager.Current!.Slots[1]=null;var row=f.Row();Check(row.Identity.State==HudValueState.Available&&!row.Identity.RawValue.Spawned&&row.Connection.RawValue.Kind==HudPeerState.Synchronizing);});
        Case("owner-agent-mismatch-invalidates-only-peer",f=>{f.Peer.Owner=new();var s=f.Capture();Check(s.Team.Slot1.Identity.State==HudValueState.Unreadable&&s.Personal.Health.State==HudValueState.Available&&s.Team.Slot2.Identity.State==HudValueState.NotEquipped);});
        Case("same-slot-model-rebuild-changes-member-revision",f=>{var old=f.Row();long version=f.Source.ChangeVersion;f.Peer.PlayerSyncModel=new();Check(f.Source.ChangeVersion>version&&f.Row().Identity.RawValue.Revision>old.Identity.RawValue.Revision);});
        Case("same-slot-owner-replacement-changes-revision",f=>{var old=f.Row();var owner=new SNet_Player(){NickName="新队员",Lookup=888};f.Peer.Owner=owner;SNet.Slots!.Players[1]=owner;var row=f.Row();Check(row.Identity.RawValue.Revision>old.Identity.RawValue.Revision&&row.Name.RawValue.Text=="新队员");});
        Case("lookup-reuse-detected",f=>{var old=f.Row();f.Peer.Owner!.Lookup=999;Check(f.Row().Identity.RawValue.Revision>old.Identity.RawValue.Revision);});
        Case("bot-human-transition-clears-incarnation",f=>{var old=f.Row();f.Peer.Owner!.IsBot=true;Check(f.Row().Identity.RawValue.Revision>old.Identity.RawValue.Revision);});
        Case("disconnect-and-reconnect-dirty-between-captures",f=>{f.Row();long version=f.Source.ChangeVersion;f.Peer.Owner!.ConnectionStatus=ConnectionStatus.NotConnected;Check(f.Source.ChangeVersion>version);version=f.Source.ChangeVersion;f.Peer.Owner.ConnectionStatus=ConnectionStatus.ConnectedDirect;Check(f.Source.ChangeVersion>version);});
        Case("damage-owner-rejected",f=>{f.Peer.Damage!.Owner=f.Local;Check(f.Row().Health.State==HudValueState.Unreadable);});
        Case("peer-health-failure-is-independent",f=>{f.Peer.Damage!.ThrowHealth=true;var row=f.Row();Check(row.Health.State==HudValueState.Unreadable&&row.Infection.State==HudValueState.Available&&row.PrimaryRatio.State==HudValueState.Available);});
        Case("zero-peer-health-not-missing",f=>{f.Peer.Damage!.CurrentHealth=0;Check(f.Row().Health.State==HudValueState.Available&&f.Row().Health.RawValue.Amount==0);});
        Case("remote-down-state-uses-sync",f=>{f.Peer.Sync!.m_locomotionData=new(){State=PlayerLocomotion.PLOC_State.Downed};Check(f.Row().Life.RawValue.Kind==HudLifeKind.Downed);});
        Case("master-owned-bot-down-state-uses-live-locomotion",f=>{f.Peer.IsLocallyOwned=true;f.Peer.Owner!.IsBot=true;f.Peer.Locomotion!.m_currentStateEnum=PlayerLocomotion.PLOC_State.Downed;Check(f.Row().Life.RawValue.Kind==HudLifeKind.Downed);});
        Case("master-owned-bot-without-locomotion-is-unreadable",f=>{f.Peer.IsLocallyOwned=true;f.Peer.Locomotion=null;Check(f.Row().Life.State==HudValueState.Unreadable);});
        Case("foreign-sync-owner-cannot-supply-life-state",f=>{f.Peer.Sync!.m_agent=f.Local;Check(f.Row().Life.State==HudValueState.Unreadable);});
        Case("missing-sync-does-not-guess-alive",f=>{f.Peer.Sync=null;Check(f.Row().Life.State==HudValueState.Unreadable);});
        Case("wrong-backpack-owner-rejected",f=>{f.PeerBag.Owner=f.Local.Owner;Check(f.Row().PrimaryRatio.State==HudValueState.Unreadable);});
        Case("missing-storage-does-not-erase-health",f=>{f.PeerBag.AmmoStorage=null;var row=f.Row();Check(row.Health.State==HudValueState.Available&&row.ToolRatio.State==HudValueState.Unreadable);});
        Case("invalid-resource-ratio-not-zero",f=>{f.PeerBag.AmmoStorage!.Ammo[AmmoType.Standard].RelInPack=float.NaN;Check(f.Row().PrimaryRatio.State==HudValueState.Invalid&&f.Row().ToolRatio.State==HudValueState.Available);});
        Case("does-not-read-remote-absolute-count-getter",f=>{f.PeerBag.AmmoStorage!.ThrowCount=true;Check(f.Row().PrimaryRatio.State==HudValueState.Available&&f.PeerBag.AmmoStorage.CountReads==0);});
        Case("actual-peer-marker-only",f=>{f.Capture();f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());Check(f.Row().Marked.State==HudValueState.Available);});
        Case("foreign-marker-ignored",f=>{f.Capture();f.Source.NotifyTeamPing(1234567);Check(f.Row().Marked.State==HudValueState.Unknown);});
        Case("first-person-ping-does-not-leak-on-resume",f=>{f.Capture();var valid=f.Current;f.Current=f.Current with{CurrentView=f.Current.CurrentView with{Mode=ThirdPersonMode.FirstPersonAim}};f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());f.Current=valid;Check(f.Row().Marked.State==HudValueState.Unknown);});
        Case("mark-event-time-not-renewed",f=>{f.Capture();f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());f.Now=10.1;Check(f.Row().Marked.RawValue.OccurredAtSeconds==10);});
        Case("model-rebuild-clears-mark",f=>{f.Capture();f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());f.Peer.PlayerSyncModel=new();Check(f.Row().Marked.State==HudValueState.Unknown);});
        Case("cancellation-clears-team-events",f=>{f.Capture();f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());f.Source.ClearTransient();Check(f.Row().Marked.State==HudValueState.Unknown);});
        Case("team-disabled-preserves-personal-and-no-roster",f=>{f.Source.SetTeamEnabled(false);var s=f.Capture();Check(s.Team.Roster.State==HudValueState.Unknown&&s.Personal.Health.State==HudValueState.Available);});
        Case("missing-manager-does-not-break-personal",f=>{PlayerManager.Current=null;var s=f.Capture();Check(s.Team.Roster.State==HudValueState.Unknown&&s.Personal.Health.State==HudValueState.Available);});
        Case("off-main-signals-and-capture-ignored",f=>{long version=f.Source.ChangeVersion;Task.Run(()=>{f.Source.SetTeamEnabled(false);f.Source.NotifyTeamPing(f.Peer.NavMarker!.m_marker!.Pointer.ToInt64());Check(!f.Source.TryCapture(f.Request,out _));}).GetAwaiter().GetResult();Check(f.Source.ChangeVersion==version&&f.Row().Marked.State==HudValueState.Unknown);});
        Case("first-person-rejects-team-and-personal",f=>{f.Current=f.Current with{CurrentView=f.Current.CurrentView with{Mode=ThirdPersonMode.FirstPersonAim}};Check(!f.Source.TryCapture(f.Request,out _));});
        bool passed=results.All(x=>x.Passed);File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,ActualCollectorUsed=true,NativeGameExecuted=false},new JsonSerializerOptions{WriteIndented=true}));foreach(var x in results.Where(x=>!x.Passed))Console.WriteLine("FAIL "+x.Name+": "+x.Error);Console.WriteLine($"Team collection: {results.Count}; passed={passed}");return passed?0:1;
    }
}
