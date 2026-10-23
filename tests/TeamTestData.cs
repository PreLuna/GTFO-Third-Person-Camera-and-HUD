using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

internal static class TeamTestData
{
    internal static HudTeammateSnapshot Peer(in HudContentCaptureRequest r, int slot, long revision=1, bool bot=false,
        HudPeerState connection=HudPeerState.Connected, HudLifeKind life=HudLifeKind.Alive, double hp=.75,
        double primary=.6, double secondary=.4, double tool=.5, string name="队员", bool marked=false)
    {
        var stamp=HudValueStamp.AtCapture(r,r.Origin.View.Binding.LocalPlayer.Agent) with{SubjectRevision=revision};
        var id=new HudTeamIdentity(1000+slot,(ulong)(2000+slot),slot,new(3000+slot,3000+slot),new(4000+slot,4000+slot),bot,revision);
        return new(slot,PersonalTestData.Value(id,stamp),PersonalTestData.Value(new HudText(name),stamp),PersonalTestData.Value(new HudConnection(connection),stamp),
            PersonalTestData.Value(new HudQuantity(hp,HudUnit.Fraction),stamp),PersonalTestData.Value(new HudQuantity(.1,HudUnit.Fraction),stamp),
            PersonalTestData.Value(new HudLife(life),stamp),PersonalTestData.Value(new HudQuantity(primary,HudUnit.Fraction),stamp),
            PersonalTestData.Value(new HudQuantity(secondary,HudUnit.Fraction),stamp),PersonalTestData.Value(new HudQuantity(tool,HudUnit.Fraction),stamp),
            marked?PersonalTestData.Value(new HudTeamMark(1,r.Origin.CapturedAtSeconds),stamp):default);
    }
    internal static HudContentSnapshot Add(in HudContentCaptureRequest r,in HudContentSnapshot snapshot)
    {
        int local=r.Origin.View.Binding.LocalPlayer.Slot;
        var stamp=HudValueStamp.AtCapture(r,r.Origin.View.Binding.LocalPlayer.Agent);
        return snapshot with{Team=new(PersonalTestData.Value(new HudTeamRoster(local,15&~(1<<local)),stamp),Peer(r,0,name:"队员零"),
            Peer(r,1,name:"队员一"),Peer(r,2,bot:true,name:"队员二"),Peer(r,3,name:"队员三"))};
    }
    internal static HudContentSnapshot Snapshot(HudRuntimeContext c)=>Add(PersonalTestData.Request(c),PersonalTestData.Snapshot(c));
    internal static HudContentSnapshot WithPeer(HudContentSnapshot s,int slot,HudTeammateSnapshot peer)=>s with{Team=slot switch{
        0=>s.Team with{Slot0=peer},1=>s.Team with{Slot1=peer},2=>s.Team with{Slot2=peer},3=>s.Team with{Slot3=peer},_=>s.Team}};
}
