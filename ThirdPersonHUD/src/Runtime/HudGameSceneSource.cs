#nullable enable
using System;
using ChainedPuzzles;
using Gear;
using Player;
using SNetwork;
using Il2CppInterop.Runtime;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed partial class HudGameContentSource : IHudSceneCaptureControl
    {
        HudSceneCollectionOptions sceneOptions=HudSceneCollectionOptions.Default;
        ObjectIdentity interactionKey,supplyKey,recipientKey,countdownKey;
        long interactionRevision=1,supplyRevision=1;
        CP_Bioscan_Hud? scanHud;
        ThirdPersonViewState scanView;
        double scanSeenAt=double.NegativeInfinity;
        readonly HudValue<HudSceneMarker>[] sceneMarkers=new HudValue<HudSceneMarker>[6];
        public void ConfigureScene(HudSceneCollectionOptions options)
        {
            if(!OnMainThread)return;
            options=options with{CandidateLimit=Math.Clamp(options.CandidateLimit,1,32)};
            if(options==sceneOptions)return;
            sceneOptions=options;ClearSceneTransient();changeVersion++;
        }
        void ClearSceneTransient(){scanHud=null;scanView=default;scanSeenAt=double.NegativeInfinity;Array.Clear(sceneMarkers,0,sceneMarkers.Length);}
        bool SceneLocal(out LocalPlayerAgent? local)
        {
            local=null;var c=readContext();
            if(!sceneOptions.Enabled||!c.HasValue||!HudEligibility.EvaluateCurrent(c.Value).Allowed||c.Value.Render.Phase==RenderPhase.AuxiliaryActive)return false;
            local=PlayerManager.GetLocalPlayerAgent()?.TryCast<LocalPlayerAgent>();
            return local!=null&&Binding(local)==c.Value.ExpectedBinding;
        }
        void ObserveScene()
        {
            try
            {
                if(!SceneLocal(out var local)||local==null)return;
                var interaction=local.Interaction;
                var selected=interaction?.m_bestSelectedInteract?.TryCast<Interact_Base>();
                var key=interaction!=null&&Key(interaction.m_owner)==Key(local)&&PlayerInteraction.InteractionEnabled&&selected!=null&&selected.IsSelected&&selected.IsActive&&!selected.IsBlocked?Key(selected):default;
                var pack=local.Inventory?.WieldedItem?.TryCast<ResourcePackFirstPerson>();
                var item=pack!=null&&PlayerKey(pack.Owner)==PlayerKey(local)?Key(pack):default;
                var receiver=pack?.m_actionReceiver?.TryCast<PlayerAgent>();var other=receiver!=null?Key(receiver):default;
                if(key!=interactionKey){interactionKey=key;interactionRevision=checked(interactionRevision+1);changeVersion++;}
                if(item!=supplyKey||other!=recipientKey){supplyKey=item;recipientKey=other;supplyRevision=checked(supplyRevision+1);changeVersion++;}
                ObserveCountdown(local);
            }
            catch { if(interactionKey.IsValid||supplyKey.IsValid){interactionKey=supplyKey=recipientKey=default;interactionRevision++;supplyRevision++;changeVersion++;} }
        }
        void ObserveCountdown(LocalPlayerAgent local)
        {
            ObjectIdentity next=default;
            try{var layer=GuiManager.PlayerLayer;var timer=layer?.m_objectiveTimer;
                if(sceneOptions.Mission&&layer!=null&&Key(layer.m_player)==Key(local)&&layer.TimerOnMainScreenVisible&&timer!=null&&timer.Visible)next=Key(timer);}
            catch{}
            if(next!=countdownKey){countdownKey=next;changeVersion++;}
        }
        internal void ObserveBioscan(CP_Bioscan_Hud hud)
        {
            if(!OnMainThread||!sceneOptions.Scan)return;
            try
            {
                if(hud==null||!hud.m_visible||!hud.m_isClosestToPlayer||!SceneLocal(out _))return;
                var c=readContext();if(!c.HasValue)return;
                if(Key(scanHud)!=Key(hud)){scanHud=hud;changeVersion++;}
                scanView=c.Value.CurrentView;scanSeenAt=clock();
            }
            catch { }
        }
        HudSceneSnapshot CaptureScene(in HudContentCaptureRequest request,LocalPlayerAgent local,PlayerBackpack? bag)
        {
            if(!sceneOptions.Enabled)return default;
            ObserveScene();
            var interaction=sceneOptions.Interaction?CaptureInteraction(request,local):default;
            var supply=sceneOptions.Supply?CaptureSupply(request,local,bag):default;
            var mission=default(HudObjectiveSnapshot);var countdown=default(HudObjectiveSnapshot);
            if(sceneOptions.Mission)CaptureObjectives(request,local,out mission,out countdown);
            var scan=sceneOptions.Scan?CaptureScan(request):default;
            CaptureMarkers(request,local);
            return new(interaction,supply,mission,countdown,scan,sceneMarkers[0],sceneMarkers[1],sceneMarkers[2],sceneMarkers[3],sceneMarkers[4],sceneMarkers[5]);
        }
        HudValueStamp SceneStamp(in HudContentCaptureRequest request,ObjectIdentity key,long revision)
            =>HudValueStamp.AtCapture(request,key) with{SubjectRevision=revision};
        HudInteractionSnapshot CaptureInteraction(in HudContentCaptureRequest request,LocalPlayerAgent local)
        {
            try
            {
                var controller=local.Interaction;var target=controller?.m_bestSelectedInteract?.TryCast<Interact_Base>();
                if(controller==null||Key(controller.m_owner)!=Key(local)||!PlayerInteraction.InteractionEnabled||target==null||!target.IsSelected||!target.IsActive||target.IsBlocked||Key(target)!=interactionKey)return default;
                var stamp=SceneStamp(request,Key(target),interactionRevision);var subject=Known(new HudSceneSubject(Key(target),default,interactionRevision),stamp);
                var action=Missing<HudText>(HudValueState.Unsupported,stamp);var name=action;
                var progress=Missing<HudQuantity>(HudValueState.Unsupported,stamp);
                var timed=target.TryCast<Interact_Timed>();
                if(timed!=null)
                {
                    action=Known(new HudText(timed.InteractionMessage),stamp);
                    if(timed.TimerIsActive&&Key(timed.m_localPlayerInteractInfo?.Agent)==Key(local))
                        progress=HudNumberRules.TryProgress(timed.InteractionTimerRel,out var q)?Known(q,stamp):Missing<HudQuantity>(HudValueState.Invalid,stamp);
                }
                var pickup=target.TryCast<Interact_Pickup_PickupItem>();
                if(pickup?.m_nameFunc!=null)name=Known(new HudText(pickup.m_nameFunc.Invoke()),stamp);
                var generic=target.TryCast<Interact_Generic>();if(generic!=null){name=Known(new HudText(generic.m_displayName),stamp);action=Known(new HudText("交互"),stamp);}
                var revive=target.TryCast<Interact_Revive>();if(revive!=null){name=Known(new HudText(revive.m_displayName),stamp);action=Known(new HudText("救援"),stamp);}
                var key=Missing<HudText>(HudValueState.Unreadable,stamp);
                var mapper=InputMapper.Current;
                if(mapper?.m_currentKeyMouseMap!=null)key=Known(new HudText(mapper.DoGetBindingName(target.InputAction,mapper.m_currentKeyMouseMap)),stamp);
                if(Key(controller.m_bestSelectedInteract?.TryCast<Interact_Base>())!=stamp.Source||!target.IsSelected||!target.IsActive||target.IsBlocked)return default;
                return new(subject,name,action,key,progress);
            }
            catch{return default;}
        }
        HudSupplySnapshot CaptureSupply(in HudContentCaptureRequest request,LocalPlayerAgent local,PlayerBackpack? bag)
        {
            try
            {
                var pack=local.Inventory?.WieldedItem?.TryCast<ResourcePackFirstPerson>();
                if(pack==null||Key(pack)!=supplyKey||PlayerKey(pack.Owner)!=PlayerKey(local)||local.Inventory?.WieldedSlot!=InventorySlot.ResourcePack)return default;
                var timer=pack.m_interactApplyResource;var receiver=pack.m_actionReceiver?.TryCast<PlayerAgent>();
                if(timer==null||!timer.TimerIsActive||Key(timer.m_localPlayerInteractInfo?.Agent)!=Key(local)||receiver==null||Key(receiver)!=recipientKey)return default;
                var recipient=PlayerKey(receiver);if(!recipient.IsValid||receiver.IsBeingDestroyed||receiver.IsBeingDespawned)return default;
                if(recipient!=PlayerKey(local)&&(ReadPeer(recipient.Slot).State!=HudPeerState.Connected||SNet.Slots==null||Id(SNet.Slots.GetPlayerInSlot(recipient.Slot))!=recipient.OwnerHandle||PlayerManager.Current==null||Key(PlayerManager.Current.GetPlayerAgentInSlot(recipient.Slot))!=recipient.Agent))return default;
                var stamp=SceneStamp(request,Key(pack),supplyRevision);
                var uses=Missing<HudQuantity>(HudValueState.Unreadable,stamp);
                try{if(bag?.AmmoStorage!=null&&Id(bag.Owner)==Id(local.Owner)&&HudNumberRules.TryCount(bag.AmmoStorage.GetBulletsInPack(pack.AmmoType),HudUnit.Uses,out var q))uses=Known(q,stamp);}catch{}
                var progress=HudNumberRules.TryProgress(timer.InteractionTimerRel,out var pg)?Known(pg,stamp):Missing<HudQuantity>(HudValueState.Invalid,stamp);
                var row=new HudSupplySnapshot(Known(new HudSceneSubject(Key(pack),recipient.Agent,supplyRevision),stamp),Known(new HudText(pack.PublicName),stamp),
                    Known(new HudRecipient(recipient,receiver.Owner!.NickName,recipient==PlayerKey(local)),stamp),uses,progress);
                return Key(pack.m_actionReceiver?.TryCast<PlayerAgent>())==recipient.Agent&&PlayerKey(receiver)==recipient&&Key(local.Inventory?.WieldedItem)==stamp.Source?row:default;
            }
            catch{return default;}
        }
        void CaptureObjectives(in HudContentCaptureRequest request,LocalPlayerAgent local,out HudObjectiveSnapshot mission,out HudObjectiveSnapshot countdown)
        {
            mission=countdown=default;PlayerGuiLayer? layer;
            try{layer=GuiManager.PlayerLayer;if(layer==null||Key(layer.m_player)!=Key(local))return;}catch{return;}
            try
            {
                var objectives=layer.m_wardenObjective;
                if(objectives!=null&&objectives.gameObject.activeInHierarchy&&objectives.m_isCurrentLayer&&!objectives.ShowPreviousObjectives
                    &&objectives.m_progressionObjectiveMap!=null&&objectives.m_progressionObjectiveMap.TryGetValue(PUI_GameObjectives.MAIN_OBJECTIVE_ID,out var main)&&main!=null&&main.gameObject.activeInHierarchy)
                {var stamp=SceneStamp(request,Key(objectives),1);mission=new(Known(new HudSceneSubject(stamp.Source,Key(main),1),stamp),Known(new HudText(main.m_header?.text??"任务"),stamp),Known(new HudText(main.m_text?.text??""),stamp));}
            }
            catch{}
            try
            {
                var timer=layer.m_objectiveTimer;
                if(layer.TimerOnMainScreenVisible&&timer!=null&&timer.Visible&&timer.m_timerText!=null&&timer.m_timerText.gameObject.activeInHierarchy)
                {var stamp=SceneStamp(request,Key(timer),1);countdown=new(Known(new HudSceneSubject(stamp.Source,default,1),stamp),Known(new HudText(timer.m_titleText?.text??"倒计时"),stamp),Known(new HudText(timer.m_timerText.text),stamp));}
            }
            catch{}
        }
        HudScanSnapshot CaptureScan(in HudContentCaptureRequest request)
        {
            try
            {
                var view=request.Origin.View;var hud=scanHud;
                if(hud==null||!hud.m_visible||!hud.m_isClosestToPlayer||scanSeenAt>clock()||clock()-scanSeenAt>.1||scanView.Context!=view.Context||scanView.Binding!=view.Binding||scanView.EligibilityEpoch!=view.EligibilityEpoch||scanView.ConfigurationRevision!=view.ConfigurationRevision)return default;
                var stamp=SceneStamp(request,Key(hud),1);
                var progress=HudNumberRules.TryProgress(hud.m_progressTarget,out var q)?Known(q,stamp):Missing<HudQuantity>(HudValueState.Invalid,stamp);
                return new(Known(new HudSceneSubject(stamp.Source,default,1),stamp),Known(new HudText(hud.m_localPlayerInScan?"扫描中":"前往扫描"),stamp),progress);
            }
            catch{return default;}
        }
        void CaptureMarkers(in HudContentCaptureRequest request,LocalPlayerAgent local)
        {
            Array.Clear(sceneMarkers,0,sceneMarkers.Length);if(!sceneOptions.Markers)return;
            int count=0;var r=request;
            void Add(UnityEngine.Object identity,HudSceneMarkerKind kind,UnityEngine.Vector3 pos,string label)
            {
                if(count==sceneMarkers.Length)return;var key=Key(identity);
                for(int i=0;i<count;i++)if(sceneMarkers[i].RawValue.Identity==key)return;
                var value=new HudSceneMarker(key,kind,new(pos.x,pos.y,pos.z),(int)local.DimensionIndex,label);
                if(value.IsValid)sceneMarkers[count++]=Known(value,HudValueStamp.AtCapture(r,key));
            }
            if(sceneOptions.Rescue)for(int slot=0;slot<4;slot++)
            {
                try
                {
                    if(slot==local.PlayerSlotIndex||ReadPeer(slot).State!=HudPeerState.Connected)continue;
                    var peer=PlayerManager.Current?.GetPlayerAgentInSlot(slot);if(peer==null||peer.DimensionIndex!=local.DimensionIndex)continue;
                    var state=peer.IsLocallyOwned?peer.Locomotion?.m_currentStateEnum:peer.Sync!=null&&Key(peer.Sync.m_agent)==Key(peer)?peer.Sync.m_locomotionData.State:null;
                    if(state==PlayerLocomotion.PLOC_State.Downed)Add(peer,HudSceneMarkerKind.Rescue,peer.Position,"救援队员"+(slot+1));
                }catch{}
            }
            if(sceneOptions.Pings)try
            {
                var pings=GuiManager.Current?.m_playerPings;
                if(pings!=null)for(int slot=0;slot<Math.Min(4,pings.Length);slot++)
                {
                    var ping=pings[slot];var marker=ping?.m_marker;
                    if(slot==local.PlayerSlotIndex||ping==null||ping.m_playerIndex!=slot||SNet.Slots?.GetPlayerInSlot(slot)==null||!ping.NavMarkerIsVisible()||marker==null||marker.TrackingTrans==null||!marker.IsVisible||!marker.m_isInPlayerDimension)continue;
                    Add(marker,HudSceneMarkerKind.Ping,marker.TrackingTrans.position,"队员"+(slot+1)+"标记");
                }
            }catch{}
            if(sceneOptions.Detected)try
            {
                var scanner=local.Inventory?.WieldedItem?.TryCast<EnemyScanner>();
                if(scanner!=null&&PlayerKey(scanner.Owner)==PlayerKey(local)&&scanner.m_enemiesDetected!=null)
                    for(int i=0;i<Math.Min(sceneOptions.CandidateLimit,scanner.m_enemiesDetected.Count)&&count<sceneMarkers.Length;i++)
                    {var enemy=scanner.m_enemiesDetected[i];if(enemy!=null&&enemy.Alive&&enemy.DimensionIndex==local.DimensionIndex)Add(enemy,HudSceneMarkerKind.Detected,enemy.Position,"已发现目标");}
            }catch{}
        }
    }
}
