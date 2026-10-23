using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

internal static class SceneTestData
{
    internal static HudValueStamp Stamp(HudContentCaptureRequest r,long id,long revision=1)=>HudValueStamp.AtCapture(r,new(id,(int)id)) with{SubjectRevision=revision};
    internal static HudValue<HudSceneSubject> Subject(HudValueStamp s,ObjectIdentity related=default)=>PersonalTestData.Value(new HudSceneSubject(s.Source,related,s.SubjectRevision),s);
    internal static HudInteractionSnapshot Interaction(HudContentCaptureRequest r)
    {var s=Stamp(r,810);return new(Subject(s),PersonalTestData.Value(new HudText("安全门"),s),PersonalTestData.Value(new HudText("开启"),s),PersonalTestData.Value(new HudText("F"),s),PersonalTestData.Value(new HudQuantity(.42,HudUnit.Fraction),s));}
    internal static HudSupplySnapshot Supply(HudContentCaptureRequest r,bool self=false)
    {var s=Stamp(r,820);var player=self?r.Origin.View.Binding.LocalPlayer:new PlayerIdentity(new(3001,3001),1001,1);return new(Subject(s,player.Agent),PersonalTestData.Value(new HudText("医疗包"),s),PersonalTestData.Value(new HudRecipient(player,"队员甲",self),s),PersonalTestData.Value(new HudQuantity(2,HudUnit.Uses),s),PersonalTestData.Value(new HudQuantity(.6,HudUnit.Fraction),s));}
    internal static HudObjectiveSnapshot Objective(HudContentCaptureRequest r,long id,string title,string text)
    {var s=Stamp(r,id);return new(Subject(s),PersonalTestData.Value(new HudText(title),s),PersonalTestData.Value(new HudText(text),s));}
    internal static HudValue<HudSceneMarker> Marker(HudContentCaptureRequest r,long id,HudSceneMarkerKind kind,float x,float y,float z,int dimension=0)
    {var s=Stamp(r,id);return PersonalTestData.Value(new HudSceneMarker(s.Source,kind,new(x,y,z),dimension,kind==HudSceneMarkerKind.Rescue?"救援队员2":kind==HudSceneMarkerKind.Ping?"队员3标记":"已发现目标"),s);}
    internal static HudContentSnapshot Add(HudContentCaptureRequest r,HudContentSnapshot s)
    {
        var scan=Stamp(r,850);
        return s with{Scene=new(Interaction(r),default,Objective(r,830,"当前目标","寻找数据立方体并返回撤离点"),Objective(r,840,"撤离","02:18"),
            new(Subject(scan),PersonalTestData.Value(new HudText("扫描中"),scan),PersonalTestData.Value(new HudQuantity(.4,HudUnit.Fraction),scan)),
            Marker(r,860,HudSceneMarkerKind.Rescue,-6,1,-5),Marker(r,861,HudSceneMarkerKind.Ping,6,3,-7),Marker(r,862,HudSceneMarkerKind.Detected,-4,-2,-6),default,default,default)};
    }
    internal static HudContentSnapshot Snapshot(HudRuntimeContext c)=>Add(PersonalTestData.Request(c),TeamTestData.Snapshot(c));
}
