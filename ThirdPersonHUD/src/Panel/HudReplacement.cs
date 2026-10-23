using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    [Flags] public enum HudOriginalGroup { None=0, Health=1, Infection=2, Ammo=4, Tool=8, Items=16, All=31,
        Team0=32, Team1=64, Team2=128, Team3=256, Team=480, Countdown=512 }
    public readonly record struct HudReplacementOptions(bool Enabled,bool Health,bool Infection,bool Ammo,bool Tool,bool Items)
    {
        public static HudReplacementOptions Default=>new(true,true,true,true,true,true);
        public HudOriginalGroup Groups=>!Enabled?0:(Health?HudOriginalGroup.Health:0)|(Infection?HudOriginalGroup.Infection:0)
            |(Ammo?HudOriginalGroup.Ammo:0)|(Tool?HudOriginalGroup.Tool:0)|(Items?HudOriginalGroup.Items:0);
    }
    // Covers specific original readouts, never their parent HUD, stamina, air, shield, capacity or interactions.
    public static class HudReplacementCoverage
    {
        public static HudOriginalGroup Evaluate(in HudRuntimeContext current,in HudContentSnapshot snapshot,long epoch,long revision,
            in HudContentPolicy policy,in HudPersonalOptions personal,bool expanded)
        {
            if(!personal.Enabled||!HudEligibility.EvaluateCurrent(current).Allowed
                ||!HudContentRules.EvaluateData(current,snapshot.Header,epoch,revision,policy).Allowed)return 0;
            var r=new Reader(current,snapshot,epoch,revision,policy);var p=snapshot.Personal;
            if(!r.Read(p.Life,r.Player,HudFreshness.Live,out var life)||life.Kind!=HudLifeKind.Alive)return 0;
            var result=HudOriginalGroup.None;
            if(personal.Health&&r.Quantity(p.Health,r.Player,HudInformationId.Health))result|=HudOriginalGroup.Health;
            if(personal.Infection&&r.Quantity(p.Infection,r.Player,HudInformationId.Infection))result|=HudOriginalGroup.Infection;
            bool selection=r.Read(p.Selected,r.Player,HudFreshness.Live,out var selected);
            if(personal.Weapons&&selection&&(expanded||personal.FocusSelected&&selected.Slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary)
                &&r.Gear(p.Primary,HudEquipmentSlot.Primary,true)&&r.Gear(p.Secondary,HudEquipmentSlot.Secondary,true))result|=HudOriginalGroup.Ammo;
            if(personal.Tools&&r.Gear(p.Tool,HudEquipmentSlot.Tool,false))result|=HudOriginalGroup.Tool;
            if(personal.Items&&expanded&&r.Gear(p.ResourcePack,HudEquipmentSlot.ResourcePack,true)
                &&r.Gear(p.Consumable,HudEquipmentSlot.Consumable,true)&&r.Gear(p.Heavy,HudEquipmentSlot.Heavy,true))result|=HudOriginalGroup.Items;
            return result;
        }
        readonly struct Reader
        {
            readonly HudRuntimeContext c;readonly HudContentSnapshot s;readonly long e,v;readonly HudContentPolicy p;
            public ObjectIdentity Player=>c.ExpectedBinding.LocalPlayer.Agent;
            public Reader(in HudRuntimeContext c,in HudContentSnapshot s,long e,long v,in HudContentPolicy p){this.c=c;this.s=s;this.e=e;this.v=v;this.p=p;}
            public bool Read<T>(in HudValue<T> field,ObjectIdentity owner,HudFreshness age,out T value)where T:struct,IHudContentValue
                =>HudContentRules.TryRead(c,s.Header,field,owner,age,e,v,p,out value,out _);
            public bool Quantity(in HudValue<HudQuantity> field,ObjectIdentity owner,HudInformationId id)
                =>HudContentRules.TryReadQuantity(c,s.Header,field,owner,id,e,v,p,out _,out _);
            public bool Gear(in HudEquipmentSnapshot gear,HudEquipmentSlot slot,bool allowAbsent)
            {
                if(gear.Slot!=slot)return false;
                if(allowAbsent&&HudContentRules.GetState(c,s.Header,gear.Identity,Player,HudFreshness.Identity,e,v,p)==HudValueState.NotEquipped)return true;
                if(!Read(gear.Identity,gear.Identity.Stamp.Source,HudFreshness.Identity,out var id)||id.Instance!=gear.Identity.Stamp.Source||id.Slot!=slot
                    ||!Read(gear.Name,id.Instance,HudFreshness.Identity,out _))return false;
                return slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary
                    ?Quantity(gear.Magazine,id.Instance,HudInformationId.Magazine)&&Quantity(gear.ReserveRounds,id.Instance,HudInformationId.ReserveRounds)
                    :slot==HudEquipmentSlot.Tool?Quantity(gear.SupplyRatio,id.Instance,HudInformationId.ToolResource)
                    :Quantity(slot==HudEquipmentSlot.ResourcePack?gear.Uses:gear.ItemCount,id.Instance,slot==HudEquipmentSlot.ResourcePack?HudInformationId.ResourcePackUses:HudInformationId.ConsumableCount);
            }
        }
    }
}
