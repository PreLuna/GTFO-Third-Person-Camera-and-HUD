using System;
using Player;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudOriginalGamePort : IHudOriginalHudPort
    {
        sealed class Target : IHudOriginalTarget
        {
            readonly Renderer renderer;
            public Target(Renderer renderer){this.renderer=renderer;Identity=Key(renderer);}
            public ObjectIdentity Identity{get;}
            public bool Alive=>renderer!=null&&Key(renderer)==Identity;
            public bool Hidden{get=>renderer.forceRenderingOff;set=>renderer.forceRenderingOff=value;}
        }
        readonly Target[] cache=new Target[80];
        static readonly InventorySlot[] itemSlots={InventorySlot.ResourcePack,InventorySlot.Consumable,InventorySlot.ConsumableHeavy};
        readonly int thread=Environment.CurrentManagedThreadId;
        static ObjectIdentity Key(UnityEngine.Object value)=>value==null?default:new(value.Pointer.ToInt64(),value.GetInstanceID());
        public bool TryGetTargets(HudOriginalGroup group,in LocalViewBinding binding,IHudOriginalTarget[] output,out int count)
        {
            count=0;if(Environment.CurrentManagedThreadId!=thread)return false;
            var local=PlayerManager.GetLocalPlayerAgent();var layer=GuiManager.PlayerLayer;var inv=layer?.Inventory;
            if(local==null||local.Owner==null||Key(local)!=binding.LocalPlayer.Agent||local.Owner.Pointer.ToInt64()!=binding.LocalPlayer.OwnerHandle
                ||local.PlayerSlotIndex!=binding.LocalPlayer.Slot||Key(local.PlayerSyncModel)!=binding.CharacterModel
                ||Key(local.FPSCamera?.m_camera)!=binding.MainCamera||(int)local.DimensionIndex!=binding.DimensionIndex
                ||LevelGeneration.Builder.CurrentFloor==null||LevelGeneration.Builder.CurrentFloor.Pointer.ToInt64()!=binding.FloorHandle
                ||layer==null||inv==null||inv.m_owner==null
                ||inv.m_owner.Pointer.ToInt64()!=binding.LocalPlayer.OwnerHandle
                ||layer.m_player!=null&&Key(layer.m_player)!=binding.LocalPlayer.Agent)return false;
            int baseIndex=group switch{HudOriginalGroup.Health=>0,HudOriginalGroup.Infection=>8,HudOriginalGroup.Ammo=>16,HudOriginalGroup.Tool=>24,HudOriginalGroup.Items=>32,
                HudOriginalGroup.Team0=>40,HudOriginalGroup.Team1=>48,HudOriginalGroup.Team2=>56,HudOriginalGroup.Team3=>64,HudOriginalGroup.Countdown=>72,_=>-1};
            if(baseIndex<0)return false;int used=0;
            void Add(Renderer renderer)
            {
                if(renderer==null||used>=8||used>=output.Length)throw new InvalidOperationException("Original HUD component unavailable.");
                int at=baseIndex+used;var key=Key(renderer);
                if(cache[at]==null||cache[at].Identity!=key)cache[at]=new Target(renderer);
                output[used++]=cache[at];
            }
            PUI_InventoryItem Slot(InventorySlot slot)
            {if(inv.m_inventorySlots==null||!inv.m_inventorySlots.TryGetValue(slot,out var item)||item==null)throw new InvalidOperationException("Original inventory slot unavailable.");return item;}
            void Ammo(InventorySlot slot)
            {
                var item=Slot(slot);
                // Only this field holds absolute clip/reserve. Percentage, infinite and capacity stay original.
                if(item.m_selected_ammoClip==null)throw new InvalidOperationException("Original ammo text unavailable.");
                if(item.m_selected_ammoClip.gameObject.activeInHierarchy&&(!item.ShowAmmoClip||!item.ShowAmmoPack||item.ShowAmmoInfinite))
                    throw new InvalidOperationException("Unsupported original ammo presentation.");
                Add(item.m_selected_ammoClip.renderer);
            }
            try
            {
                if(group==HudOriginalGroup.Health)
                {var s=layer.m_playerStatus;if(s==null)return false;Add(s.m_health1);Add(s.m_health2);Add(s.m_healthText?.renderer);}
                else if(group==HudOriginalGroup.Infection)
                {var s=layer.m_playerStatus;if(s==null)return false;Add(s.m_infection1);Add(s.m_infection2);Add(s.m_infectionText?.renderer);}
                else if(group==HudOriginalGroup.Ammo){Ammo(InventorySlot.GearStandard);Ammo(InventorySlot.GearSpecial);}
                else if(group==HudOriginalGroup.Tool)
                {
                    var item=Slot(InventorySlot.GearClass);
                    if(!item.ShowAmmoTotalRel||item.ShowAmmoInfinite||item.IsDeployed)return false;
                    Add(item.m_selected_ammoTotal?.renderer);Add(item.m_slim_ammoTotal?.renderer);
                }
                else if(group==HudOriginalGroup.Items)
                {
                    foreach(var slot in itemSlots)
                    {
                        var item=Slot(slot);var text=item.m_selected_ammoClip;if(text==null)return false;
                        if(!text.gameObject.activeInHierarchy)continue;
                        if(item.ShowAmmoClip||!item.ShowAmmoPack||item.ShowAmmoTotalRel||item.ShowAmmoInfinite)return false;
                        Add(text.renderer);
                    }
                }
                else if(group==HudOriginalGroup.Countdown)
                {
                    var timer=layer.m_objectiveTimer;
                    if(!layer.TimerOnMainScreenVisible||timer==null||!timer.Visible||timer.m_timerText==null||timer.m_titleText==null
                        ||!timer.m_timerText.gameObject.activeInHierarchy||!timer.m_titleText.gameObject.activeInHierarchy)return false;
                    Add(timer.m_timerText.renderer);Add(timer.m_titleText.renderer);
                }
                else if((group&HudOriginalGroup.Team)!=0)
                {
                    int slot=(baseIndex-40)/8;
                    if(slot==binding.LocalPlayer.Slot||PlayerManager.Current==null||SNetwork.SNet.Slots==null)return false;
                    var agent=PlayerManager.Current.GetPlayerAgentInSlot(slot);var owner=SNetwork.SNet.Slots.GetPlayerInSlot(slot);
                    var nav=agent?.NavMarker;
                    if(agent==null||agent.Owner==null||owner==null||agent.PlayerSlotIndex!=slot||agent.Owner.Pointer!=owner.Pointer
                        ||nav==null||Key(nav.Player)!=Key(agent)||nav.m_beingDestroyed||nav.ExtraInfoVisible)return false;
                    var marker=nav.m_marker;var label=marker?.m_playerName;
                    // Native UpdateName combines extra information with the name. Never hide an unhandled combined text.
                    if(label==null||!NameOnly(label.text,owner.NickName))return false;
                    Add(label.renderer);
                }
                count=used;return used>0;
            }
            catch{count=0;return false;}
        }
        internal static bool NameOnly(string text,string name)
        {
            if(string.IsNullOrWhiteSpace(name)||text==null||name.IndexOfAny(new[]{'<','>','\r','\n'})>=0)return false;
            // Only the native colour wrapper is accepted. Arbitrary rich text/icons/numbers stay original.
            text=text.Trim();
            if(text.StartsWith("<color=",StringComparison.Ordinal)&&text.EndsWith("</color>",StringComparison.Ordinal))
            {int end=text.IndexOf('>');if(end<0)return false;text=text.Substring(end+1,text.Length-end-9);}
            return string.Equals(text,name,StringComparison.Ordinal);
        }
    }
}
