using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPersonHUD.Content
{
    public enum HudPeerState { Unknown, Connected, Synchronizing, Offline }
    public readonly record struct HudConnection(HudPeerState Kind) : IHudContentValue
    { public bool IsValid => Kind is HudPeerState.Connected or HudPeerState.Synchronizing or HudPeerState.Offline; }
    public readonly record struct HudTeamMark(long Sequence, double OccurredAtSeconds) : IHudContentValue
    { public bool IsValid => Sequence > 0 && double.IsFinite(OccurredAtSeconds) && OccurredAtSeconds >= 0; }
    public readonly record struct HudTeamIdentity(long Owner, ulong Lookup, int Slot, ObjectIdentity Agent,
        ObjectIdentity Model, bool IsBot, long Revision) : IHudContentValue
    {
        public bool IsValid => Owner != 0 && Slot >= 0 && Slot < 4 && Revision > 0
            && ((Agent.IsValid && Model.IsValid) || (Agent == default && Model == default));
        public bool Spawned => Agent.IsValid && Model.IsValid;
    }
    public readonly record struct HudTeamRoster(int LocalSlot, int OccupiedMask) : IHudContentValue
    { public bool IsValid => LocalSlot >= 0 && LocalSlot < 4 && OccupiedMask >= 0 && OccupiedMask < 16 && (OccupiedMask & (1 << LocalSlot)) == 0; }
    public readonly record struct HudTeammateSnapshot(int Slot, HudValue<HudTeamIdentity> Identity,
        HudValue<HudText> Name, HudValue<HudConnection> Connection, HudValue<HudQuantity> Health,
        HudValue<HudQuantity> Infection, HudValue<HudLife> Life, HudValue<HudQuantity> PrimaryRatio,
        HudValue<HudQuantity> SecondaryRatio, HudValue<HudQuantity> ToolRatio, HudValue<HudTeamMark> Marked);
    // Four fixed slots, no live objects or unbounded roster/history; the local slot is never rendered.
    public readonly record struct HudTeamSnapshot(HudValue<HudTeamRoster> Roster,
        HudTeammateSnapshot Slot0, HudTeammateSnapshot Slot1, HudTeammateSnapshot Slot2, HudTeammateSnapshot Slot3)
    {
        public HudTeammateSnapshot GetSlot(int slot) => slot switch { 0 => Slot0, 1 => Slot1, 2 => Slot2, 3 => Slot3, _ => default };
    }
    public interface IHudTeamCaptureControl { void SetTeamEnabled(bool enabled); }
    public static class HudTeamRules
    {
        public static bool TryRead<T>(in HudRuntimeContext current, in HudContentHeader header,
            in HudValue<T> field, long memberRevision, long epoch, long revision, in HudContentPolicy policy,
            out T value, out HudValueState state) where T : struct, IHudContentValue
        {
            value = default;
            state = HudContentRules.GetState(current, header, field, current.ExpectedBinding.LocalPlayer.Agent,
                HudFreshness.Live, epoch, revision, policy);
            if (state == HudValueState.Available && (memberRevision <= 0 || field.Stamp.SubjectRevision != memberRevision)) state = HudValueState.Invalid;
            if (state != HudValueState.Available) return false;
            value = field.RawValue; return true;
        }
    }
}
