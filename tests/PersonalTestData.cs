using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

internal static class PersonalTestData
{
    internal static HudValue<T> Value<T>(T value,HudValueStamp stamp)where T:struct,IHudContentValue=>HudValue<T>.FromValue(value,stamp);
    internal static HudEquipmentSnapshot Missing(HudContentCaptureRequest r,HudEquipmentSlot slot,HudValueState state=HudValueState.NotEquipped)
    {
        var stamp=HudValueStamp.AtCapture(r,r.Origin.View.Binding.LocalPlayer.Agent);
        return new(slot,HudValue<HudEquipmentIdentity>.Missing(state,stamp),HudValue<HudText>.Missing(state,stamp),
            HudValue<HudQuantity>.Missing(state,stamp),HudValue<HudQuantity>.Missing(state,stamp),HudValue<HudQuantity>.Missing(state,stamp),
            HudValue<HudQuantity>.Missing(state,stamp),HudValue<HudQuantity>.Missing(state,stamp),HudValue<HudDeployment>.Missing(state,stamp));
    }
    internal static HudEquipmentSnapshot Gear(HudContentCaptureRequest r,HudEquipmentSlot slot,string name,long identity,
        int magazine=0,int reserve=0,double supply=.46,int count=0,bool deployed=false)
    {
        var item=new ObjectIdentity(identity,(int)identity);var stamp=HudValueStamp.AtCapture(r,item);
        var unsupported=HudValue<HudQuantity>.Missing(HudValueState.Unsupported,stamp);
        bool gun=slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary;
        return new(slot,Value(new HudEquipmentIdentity(item,slot),stamp),Value(new HudText(name),stamp),
            gun?Value(new HudQuantity(magazine,HudUnit.Rounds),stamp):unsupported,gun?Value(new HudQuantity(reserve,HudUnit.Rounds),stamp):unsupported,
            gun||slot==HudEquipmentSlot.Tool?Value(new HudQuantity(supply,HudUnit.Fraction),stamp):unsupported,
            slot==HudEquipmentSlot.ResourcePack?Value(new HudQuantity(count,HudUnit.Uses),stamp):unsupported,
            slot is HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy?Value(new HudQuantity(count,HudUnit.Items),stamp):unsupported,
            slot==HudEquipmentSlot.Tool?Value(new HudDeployment(deployed?HudDeploymentKind.Deployed:HudDeploymentKind.Carried),stamp):HudValue<HudDeployment>.Missing(HudValueState.Unsupported,stamp));
    }
    internal static HudContentCaptureRequest Request(HudRuntimeContext c,long sequence=1)=>new(new(c.CurrentView,sequence,c.FrameNumber,c.NowSeconds),1,1);
    internal static HudContentSnapshot Snapshot(HudRuntimeContext c,HudEquipmentSlot selected=HudEquipmentSlot.Primary,double hp=.82,double infection=.06,long sequence=1)
        =>Snapshot(Request(c,sequence),selected,hp,infection);
    internal static HudContentSnapshot Snapshot(HudContentCaptureRequest r,HudEquipmentSlot selected=HudEquipmentSlot.Primary,double hp=.82,double infection=.06)
    {
        var stamp=HudValueStamp.AtCapture(r,r.Origin.View.Binding.LocalPlayer.Agent);
        var personal=new HudPersonalSnapshot(Value(new HudQuantity(hp,HudUnit.Fraction),stamp),Value(new HudQuantity(infection,HudUnit.Fraction),stamp),
            Value(new HudLife(HudLifeKind.Alive),stamp),Value(new HudSelection(selected),stamp),Gear(r,HudEquipmentSlot.Melee,"重锤",710),
            Gear(r,HudEquipmentSlot.Primary,"突击步枪",700,32,180,.74),Gear(r,HudEquipmentSlot.Secondary,"左轮手枪",701,8,40,.62),
            Gear(r,HudEquipmentSlot.Tool,"自动炮台",702),Gear(r,HudEquipmentSlot.ResourcePack,"医疗包",703,count:2),
            Gear(r,HudEquipmentSlot.Consumable,"荧光棒",704,count:3),Missing(r,HudEquipmentSlot.Heavy),Missing(r,HudEquipmentSlot.Carry),default);
        return new(new(1,new(r.Origin,r.Origin.CapturedAtSeconds),r.WorkEpoch,r.ContentRevision),personal);
    }
}
