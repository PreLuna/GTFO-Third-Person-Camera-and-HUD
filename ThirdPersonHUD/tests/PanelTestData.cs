using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

internal static class PanelTestData
{
    internal static HudRuntimeContext Context()
    {
        var player=new PlayerIdentity(new(100,1),200,0);
        var binding=new LocalViewBinding(player,new(300,2),new(400,3),500,0);
        var view=new ThirdPersonViewState(1,new(Guid.NewGuid(),1),binding,player,1,1,ViewSource.LocalThirdPerson,ThirdPersonMode.Exploration,ViewAvailability.Ready);
        return new(true,true,true,view.Context,binding,view,100,10,new(new(binding.MainCamera,RenderSurface.MainDisplay,1),RenderPhase.MainCommitted));
    }
    internal static ThirdPersonDisplayFrame Display(HudRuntimeContext c) => new(c.CurrentView,c.Render.Identity,c.FrameNumber,1,c.NowSeconds,true,
        new(new(new(.48f,1,3),Quaternion.Identity),Matrix4x4.CreateTranslation(-.48f,-1,-3),
            Matrix4x4.CreatePerspectiveFieldOfView(1.2f,16f/9,.08f,250),new(0,0,1920,1080)),new(Vector3.Zero,Quaternion.Identity));
    internal static HudContentSnapshot Snapshot(HudRuntimeContext c,double hp=.8,double clip=32,long weapon=700,double infection=.05)
        => Snapshot(new HudContentCaptureRequest(new(c.CurrentView,1,c.FrameNumber,c.NowSeconds),1,1),hp,clip,weapon,infection);
    internal static HudContentSnapshot Snapshot(HudContentCaptureRequest request,double hp=.8,double clip=32,long weapon=700,double infection=.05)
    {
        var stamp=HudValueStamp.AtCapture(request,request.Origin.View.Binding.LocalPlayer.Agent);
        var item=new ObjectIdentity(weapon,(int)weapon);
        var itemStamp=HudValueStamp.AtCapture(request,item);
        var primary=default(HudEquipmentSnapshot) with {Slot=HudEquipmentSlot.Primary,
            Identity=HudValue<HudEquipmentIdentity>.FromValue(new(item,HudEquipmentSlot.Primary),itemStamp),
            Magazine=HudValue<HudQuantity>.FromValue(new(clip,HudUnit.Rounds),itemStamp)};
        var personal=default(HudPersonalSnapshot) with {Health=HudValue<HudQuantity>.FromValue(new(hp,HudUnit.Fraction),stamp),
            Infection=HudValue<HudQuantity>.FromValue(new(infection,HudUnit.Fraction),stamp),
            Selected=HudValue<HudSelection>.FromValue(new(HudEquipmentSlot.Primary),stamp),Primary=primary};
        return new(new(1,new(request.Origin,request.Origin.CapturedAtSeconds),request.WorkEpoch,request.ContentRevision),personal);
    }
}
