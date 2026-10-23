using System;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPersonHUD.Content
{
    public readonly record struct HudSceneSubject(ObjectIdentity Target, ObjectIdentity Related, long Revision) : IHudContentValue
    { public bool IsValid => Target.IsValid && Revision > 0; }
    public readonly record struct HudRecipient(PlayerIdentity Player, string Name, bool Self) : IHudContentValue
    { public bool IsValid => Player.IsValid && !string.IsNullOrWhiteSpace(Name); }
    public readonly record struct HudInteractionSnapshot(HudValue<HudSceneSubject> Subject, HudValue<HudText> Name,
        HudValue<HudText> Action, HudValue<HudText> Key, HudValue<HudQuantity> Progress);
    public readonly record struct HudSupplySnapshot(HudValue<HudSceneSubject> Subject, HudValue<HudText> Item,
        HudValue<HudRecipient> Recipient, HudValue<HudQuantity> Uses, HudValue<HudQuantity> Progress);
    public readonly record struct HudObjectiveSnapshot(HudValue<HudSceneSubject> Subject, HudValue<HudText> Title, HudValue<HudText> Text);
    public readonly record struct HudScanSnapshot(HudValue<HudSceneSubject> Subject, HudValue<HudText> Text, HudValue<HudQuantity> Progress);
    public enum HudSceneMarkerKind { Unknown, Rescue, Ping, Detected }
    public readonly record struct HudWorldPoint(float X, float Y, float Z)
    {
        public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
        public System.Numerics.Vector3 ToVector() => new(X,Y,Z);
    }
    public readonly record struct HudSceneMarker(ObjectIdentity Identity, HudSceneMarkerKind Kind, HudWorldPoint Position,
        int Dimension, string Label) : IHudContentValue
    {
        public bool IsValid => Identity.IsValid && Kind is HudSceneMarkerKind.Rescue or HudSceneMarkerKind.Ping or HudSceneMarkerKind.Detected
            && Position.IsValid && Dimension >= 0 && !string.IsNullOrWhiteSpace(Label);
    }
    public readonly record struct HudSceneSnapshot(HudInteractionSnapshot Interaction, HudSupplySnapshot Supply,
        HudObjectiveSnapshot Mission, HudObjectiveSnapshot Countdown, HudScanSnapshot Scan,
        HudValue<HudSceneMarker> Marker0, HudValue<HudSceneMarker> Marker1, HudValue<HudSceneMarker> Marker2,
        HudValue<HudSceneMarker> Marker3, HudValue<HudSceneMarker> Marker4, HudValue<HudSceneMarker> Marker5)
    {
        public HudValue<HudSceneMarker> Marker(int i) => i switch { 0=>Marker0,1=>Marker1,2=>Marker2,3=>Marker3,4=>Marker4,5=>Marker5,_=>default };
    }
    public readonly record struct HudSceneCollectionOptions(bool Enabled, bool Interaction, bool Supply, bool Mission, bool Scan,
        bool Markers, bool Rescue, bool Pings, bool Detected, int CandidateLimit)
    { public static HudSceneCollectionOptions Default => new(true,true,true,true,true,true,true,true,true,16); }
    public interface IHudSceneCaptureControl { void ConfigureScene(HudSceneCollectionOptions options); }
    public static class HudSceneRules
    {
        public static bool Subject(in HudRuntimeContext c, in HudContentHeader h, HudValue<HudSceneSubject> field,
            long epoch,long revision,HudContentPolicy policy,out HudSceneSubject subject)
        {
            subject=default;
            return HudContentRules.TryRead(c,h,field,field.Stamp.Source,HudFreshness.Action,epoch,revision,policy,out subject,out _)
                && subject.Target==field.Stamp.Source && subject.Revision==field.Stamp.SubjectRevision;
        }
        public static bool Read<T>(in HudRuntimeContext c,in HudContentHeader h,HudValue<T> field,HudSceneSubject subject,
            long epoch,long revision,HudContentPolicy policy,out T value) where T:struct,IHudContentValue
        {
            value=default;
            return subject.IsValid && field.Stamp.SubjectRevision==subject.Revision
                && HudContentRules.TryRead(c,h,field,subject.Target,HudFreshness.Action,epoch,revision,policy,out value,out _);
        }
    }
}
