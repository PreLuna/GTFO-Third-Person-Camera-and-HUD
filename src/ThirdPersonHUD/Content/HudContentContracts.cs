#nullable enable
using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPersonHUD.Content
{
    public static class HudContentContract { public const int SchemaVersion = 1; }

    public enum HudValueState { Unknown, Available, NotEquipped, Unreadable, Unsupported, Invalid, Expired }
    public enum HudUnit { Unknown, Fraction, Rounds, Uses, Items, Seconds, Meters }
    public enum HudFreshness { Unknown, Live, Identity, Action }
    public enum HudEquipmentSlot { Unknown, Melee, Primary, Secondary, Tool, ResourcePack, Consumable, Heavy, Carry, EmptyHands }
    public enum HudLifeKind { Unknown, Alive, Downed, Dead }
    public enum HudActionKind { Unknown, Idle, Reloading, MeleeCharging, UsingItem, Deploying, Carrying }
    public enum HudDeploymentKind { Unknown, Carried, Deployed, Recovering }

    // Every payload is an immutable managed value. No Unity/IL2CPP references cross this boundary.
    public interface IHudContentValue { bool IsValid { get; } }
    public readonly record struct HudQuantity(double Amount, HudUnit Unit) : IHudContentValue
    {
        public bool IsValid => double.IsFinite(Amount) && Amount >= 0 && (Unit switch
        {
            HudUnit.Fraction or HudUnit.Seconds or HudUnit.Meters => true,
            HudUnit.Rounds or HudUnit.Uses or HudUnit.Items => Amount <= int.MaxValue && Amount == Math.Truncate(Amount),
            _ => false
        });
    }
    public readonly record struct HudText(string Text) : IHudContentValue
    { public bool IsValid => !string.IsNullOrWhiteSpace(Text); }
    public readonly record struct HudEquipmentIdentity(ObjectIdentity Instance, HudEquipmentSlot Slot) : IHudContentValue
    { public bool IsValid => Instance.IsValid && (int)Slot >= (int)HudEquipmentSlot.Melee && (int)Slot <= (int)HudEquipmentSlot.Carry; }
    public readonly record struct HudSelection(HudEquipmentSlot Slot) : IHudContentValue
    { public bool IsValid => (int)Slot >= (int)HudEquipmentSlot.Melee && (int)Slot <= (int)HudEquipmentSlot.EmptyHands; }
    public readonly record struct HudLife(HudLifeKind Kind) : IHudContentValue
    { public bool IsValid => Kind is HudLifeKind.Alive or HudLifeKind.Downed or HudLifeKind.Dead; }
    public readonly record struct HudAction(HudActionKind Kind) : IHudContentValue
    { public bool IsValid => (int)Kind >= (int)HudActionKind.Idle && (int)Kind <= (int)HudActionKind.Carrying; }
    public readonly record struct HudDeployment(HudDeploymentKind Kind) : IHudContentValue
    { public bool IsValid => Kind is HudDeploymentKind.Carried or HudDeploymentKind.Deployed or HudDeploymentKind.Recovering; }
    // Latest native hit only. OccurredAtSeconds is never renewed by a later capture.
    public readonly record struct HudHitDirection(float X, float Y, float Z)
    {
        public static implicit operator HudHitDirection(Vector3 value) => new(value.X,value.Y,value.Z);
        public Vector3 ToVector() => new(X,Y,Z);
    }
    public readonly record struct HudHit(long Sequence, double OccurredAtSeconds, HudHitDirection TowardSource) : IHudContentValue
    {
        public bool IsValid => Sequence > 0 && double.IsFinite(OccurredAtSeconds) && OccurredAtSeconds >= 0
            && float.IsFinite(TowardSource.X) && float.IsFinite(TowardSource.Y) && float.IsFinite(TowardSource.Z);
    }

    // Source is the player for personal fields, or the actual equipment instance for item fields.
    // A retained field keeps its original sampling time; copying it cannot renew its lifetime.
    public readonly record struct HudContentGeneration(ContextStamp Context, long EligibilityEpoch,
        long ProviderConfigurationRevision, long WorkEpoch, long ContentRevision)
    {
        public bool IsValid => Context.IsValid && EligibilityEpoch > 0 && ProviderConfigurationRevision > 0 && WorkEpoch > 0 && ContentRevision > 0;
        public static HudContentGeneration From(in HudContentHeader header) => new(header.Origin.Input.View.Context,
            header.Origin.Input.View.EligibilityEpoch, header.Origin.Input.View.ConfigurationRevision, header.WorkEpoch, header.ContentRevision);
    }
    public readonly record struct HudValueStamp(ObjectIdentity Source, int SampleFrame, double CapturedAtSeconds, HudContentGeneration Generation)
    {
        // Local fields use zero; roster fields also bind to a per-member incarnation.
        public long SubjectRevision { get; init; }
        public bool IsValid => Source.IsValid && Generation.IsValid && SampleFrame >= 0 && double.IsFinite(CapturedAtSeconds) && CapturedAtSeconds >= 0;
        public static HudValueStamp AtCapture(in HudContentCaptureRequest request, ObjectIdentity source) => new(source,
            request.Origin.SampleFrame, request.Origin.CapturedAtSeconds, new(request.Origin.View.Context,
                request.Origin.View.EligibilityEpoch, request.Origin.View.ConfigurationRevision, request.WorkEpoch, request.ContentRevision));
    }

    public readonly record struct HudValue<T> where T : struct, IHudContentValue
    {
        readonly T value;
        public HudValueState State { get; }
        public HudValueStamp Stamp { get; }
        internal T RawValue => value;
        HudValue(T value, HudValueState state, HudValueStamp stamp) { this.value = value; State = state; Stamp = stamp; }
        public static HudValue<T> FromValue(T value, HudValueStamp stamp) => value.IsValid && stamp.IsValid
            ? new(value, HudValueState.Available, stamp) : Missing(HudValueState.Invalid, stamp);
        public static HudValue<T> Missing(HudValueState state, HudValueStamp stamp) => new(default,
            state is HudValueState.NotEquipped or HudValueState.Unreadable or HudValueState.Unsupported or HudValueState.Invalid
                ? state : HudValueState.Unknown, stamp);
    }

    // Content revisions are independent of the existing provider configuration revision.
    public readonly record struct HudContentHeader(int SchemaVersion, HudResultStamp Origin, long WorkEpoch, long ContentRevision);
    public readonly record struct HudContentCaptureRequest(HudWorkStamp Origin, long WorkEpoch, long ContentRevision);
    public readonly record struct HudEquipmentSnapshot(HudEquipmentSlot Slot,
        HudValue<HudEquipmentIdentity> Identity, HudValue<HudText> Name,
        HudValue<HudQuantity> Magazine, HudValue<HudQuantity> ReserveRounds, HudValue<HudQuantity> SupplyRatio,
        HudValue<HudQuantity> Uses, HudValue<HudQuantity> ItemCount, HudValue<HudDeployment> Deployment);
    public readonly record struct HudActionSnapshot(HudValue<HudAction> Action, HudValue<HudQuantity> Progress)
    { public ObjectIdentity Equipment { get; init; } }
    public readonly record struct HudPersonalSnapshot(
        HudValue<HudQuantity> Health, HudValue<HudQuantity> Infection, HudValue<HudLife> Life,
        HudValue<HudSelection> Selected,
        HudEquipmentSnapshot Melee, HudEquipmentSnapshot Primary, HudEquipmentSnapshot Secondary,
        HudEquipmentSnapshot Tool, HudEquipmentSnapshot ResourcePack, HudEquipmentSnapshot Consumable,
        HudEquipmentSnapshot Heavy, HudEquipmentSnapshot Carry, HudActionSnapshot Action)
    { public HudValue<HudHit> Hit { get; init; } }
    public readonly record struct HudContentSnapshot(HudContentHeader Header, HudPersonalSnapshot Personal)
    { public HudTeamSnapshot Team { get; init; } public HudSceneSnapshot Scene { get; init; } }

    // Implemented by the main-thread game adapter in batch 2; no live collector is added in batch 1.
    public interface IHudContentSource
    { bool TryCapture(in HudContentCaptureRequest request, out HudContentSnapshot snapshot); }

    // Central defaults, copied/overridden by future runtime configuration rather than embedded in collectors.
    public readonly record struct HudContentPolicy(double MaximumSnapshotAgeSeconds,
        double LiveAgeSeconds, double IdentityAgeSeconds, double ActionAgeSeconds)
    {
        public static HudContentPolicy Default => new(.5, .25, .5, .1);
        public bool IsValid => Positive(MaximumSnapshotAgeSeconds) && Positive(LiveAgeSeconds)
            && Positive(IdentityAgeSeconds) && Positive(ActionAgeSeconds)
            && LiveAgeSeconds <= MaximumSnapshotAgeSeconds && IdentityAgeSeconds <= MaximumSnapshotAgeSeconds
            && ActionAgeSeconds <= MaximumSnapshotAgeSeconds;
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        public double MaximumAge(HudFreshness freshness) => freshness switch
        { HudFreshness.Live => LiveAgeSeconds, HudFreshness.Identity => IdentityAgeSeconds, HudFreshness.Action => ActionAgeSeconds, _ => double.NaN };
    }
}
