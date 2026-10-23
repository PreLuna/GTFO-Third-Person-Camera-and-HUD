#nullable enable
using System;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPersonHUD.Content
{
    public enum HudContentDenyReason { Unknown, None, SchemaMismatch, WorkEpochMismatch, ContentRevisionMismatch, InvalidPolicy, SharedEligibility }
    public readonly record struct HudContentDecision(HudContentDenyReason Reason, HudDenyReason SharedReason)
    { public bool Allowed => Reason == HudContentDenyReason.None && SharedReason == HudDenyReason.None; }

    public static class HudContentRules
    {
        static HudContentDecision Deny(HudContentDenyReason reason) => new(reason, HudDenyReason.Unknown);
        static HudContentDecision Shared(HudDecision decision) => new(
            decision.Allowed ? HudContentDenyReason.None : HudContentDenyReason.SharedEligibility, decision.Reason);

        // Valid data is not permission to draw. Use EvaluateDisplay/Result for the current render.
        public static HudContentDecision EvaluateData(in HudRuntimeContext current, in HudContentHeader header,
            long workEpoch, long contentRevision, in HudContentPolicy policy)
        {
            if (header.SchemaVersion != HudContentContract.SchemaVersion) return Deny(HudContentDenyReason.SchemaMismatch);
            if (!policy.IsValid) return Deny(HudContentDenyReason.InvalidPolicy);
            if (workEpoch <= 0 || header.WorkEpoch != workEpoch) return Deny(HudContentDenyReason.WorkEpochMismatch);
            if (contentRevision <= 0 || header.ContentRevision != contentRevision) return Deny(HudContentDenyReason.ContentRevisionMismatch);
            return Shared(HudEligibility.EvaluateData(current, header.Origin,
                new(HudValidityPolicy.Default.MaximumDisplayAgeSeconds, policy.MaximumSnapshotAgeSeconds)));
        }

        // Reuse of a retained, still-valid content snapshot; always use this frame's camera packet.
        public static HudContentDecision EvaluateDisplay(in HudRuntimeContext current, in ThirdPersonDisplayFrame display,
            in HudContentHeader header, long workEpoch, long contentRevision, in HudContentPolicy policy)
        {
            var data = EvaluateData(current, header, workEpoch, contentRevision, policy);
            return !data.Allowed ? data : Shared(HudEligibility.EvaluateDisplay(current, display,
                HudDisplayUsage.ScreenComposition, new(HudValidityPolicy.Default.MaximumDisplayAgeSeconds, policy.MaximumSnapshotAgeSeconds)));
        }

        // Commit of a new result. Late, repeated or backwards results must not replace newer content.
        public static HudContentDecision EvaluateResult(in HudRuntimeContext current, in ThirdPersonDisplayFrame display,
            in HudContentHeader header, long workEpoch, long contentRevision, long lastAppliedSequence, in HudContentPolicy policy)
        {
            var data = EvaluateData(current, header, workEpoch, contentRevision, policy);
            return !data.Allowed ? data : Shared(HudEligibility.EvaluateResult(current, display,
                HudDisplayUsage.ScreenComposition, header.Origin, lastAppliedSequence,
                new(HudValidityPolicy.Default.MaximumDisplayAgeSeconds, policy.MaximumSnapshotAgeSeconds)));
        }

        public static HudValueState GetState<T>(in HudRuntimeContext current, in HudContentHeader header,
            in HudValue<T> field, ObjectIdentity expectedSource, HudFreshness freshness,
            long workEpoch, long contentRevision, in HudContentPolicy policy) where T : struct, IHudContentValue
        {
            var data = EvaluateData(current, header, workEpoch, contentRevision, policy);
            if (!data.Allowed) return data.SharedReason == HudDenyReason.ResultExpired ? HudValueState.Expired : HudValueState.Invalid;
            if (field.State == HudValueState.Unknown) return HudValueState.Unknown;
            double maximumAge = policy.MaximumAge(freshness);
            var stamp = field.Stamp; var input = header.Origin.Input;
            if (!double.IsFinite(maximumAge) || !stamp.IsValid || !expectedSource.IsValid || stamp.Source != expectedSource
                || stamp.Generation != HudContentGeneration.From(header)
                || stamp.SampleFrame > input.SampleFrame || stamp.CapturedAtSeconds > input.CapturedAtSeconds
                || stamp.SampleFrame > current.FrameNumber || stamp.CapturedAtSeconds > current.NowSeconds)
                return HudValueState.Invalid;
            if (current.NowSeconds - stamp.CapturedAtSeconds > maximumAge) return HudValueState.Expired;
            if (field.State == HudValueState.Available && !field.RawValue.IsValid) return HudValueState.Invalid;
            return field.State;
        }

        public static bool TryRead<T>(in HudRuntimeContext current, in HudContentHeader header,
            in HudValue<T> field, ObjectIdentity expectedSource, HudFreshness freshness,
            long workEpoch, long contentRevision, in HudContentPolicy policy, out T value, out HudValueState state)
            where T : struct, IHudContentValue
        {
            value = default;
            state = GetState(current, header, field, expectedSource, freshness, workEpoch, contentRevision, policy);
            if (state != HudValueState.Available) return false;
            value = field.RawValue; return true;
        }

        public static bool TryReadQuantity(in HudRuntimeContext current, in HudContentHeader header,
            in HudValue<HudQuantity> field, ObjectIdentity expectedSource, HudUnit expectedUnit, HudFreshness freshness,
            long workEpoch, long contentRevision, in HudContentPolicy policy, out HudQuantity value, out HudValueState state)
        {
            if (!TryRead(current, header, field, expectedSource, freshness, workEpoch, contentRevision, policy, out value, out state)) return false;
            if (expectedUnit != HudUnit.Unknown && value.Unit == expectedUnit) return true;
            value = default; state = HudValueState.Invalid; return false;
        }

        // Semantic field reads additionally enforce the field's unit and bounded progress/infection.
        public static bool TryReadQuantity(in HudRuntimeContext current, in HudContentHeader header,
            in HudValue<HudQuantity> field, ObjectIdentity expectedSource, HudInformationId information,
            long workEpoch, long contentRevision, in HudContentPolicy policy, out HudQuantity value, out HudValueState state)
        {
            var rule = HudInformationCatalog.GetDefault(information);
            value = default; state = HudValueState.Invalid;
            if (!rule.IsValid || rule.Unit == HudUnit.Unknown) return false;
            if (!TryReadQuantity(current, header, field, expectedSource, rule.Unit, rule.Freshness,
                workEpoch, contentRevision, policy, out value, out state)) return false;
            if (HudNumberRules.IsValidFor(value, information)) return true;
            value = default; state = HudValueState.Invalid; return false;
        }
    }

    public static class HudNumberRules
    {
        // Fractions stay unrounded; over-cap values remain visible to later formatting/bar logic.
        public static bool TryHealth(double current, double maximum, out HudQuantity value)
        {
            value = default;
            if (!double.IsFinite(current) || current < 0 || !double.IsFinite(maximum) || maximum <= 0) return false;
            return TryQuantity(current / maximum, HudUnit.Fraction, out value);
        }
        public static bool TryFraction(double fraction, out HudQuantity value) => TryQuantity(fraction, HudUnit.Fraction, out value);
        public static bool TryProgress(double fraction, out HudQuantity value)
        { value = default; return fraction <= 1 && TryFraction(fraction, out value); }
        public static bool TryInfection(double fraction, out HudQuantity value) => TryProgress(fraction, out value);
        public static bool IsValidFor(in HudQuantity value, HudInformationId information)
        {
            var rule = HudInformationCatalog.GetDefault(information);
            return rule.IsValid && rule.Unit != HudUnit.Unknown && value.IsValid && value.Unit == rule.Unit
                && (information is not (HudInformationId.Infection or HudInformationId.ActionProgress
                    or HudInformationId.InteractionProgress or HudInformationId.ScanProgress) || value.Amount <= 1);
        }
        public static bool TryCount(int count, HudUnit unit, out HudQuantity value)
        {
            value = default;
            return (unit is HudUnit.Rounds or HudUnit.Uses or HudUnit.Items) && TryQuantity(count, unit, out value);
        }
        // pack/capacity are resource points, clip is a bullet count. Never add them directly.
        // Actual reserve rounds come from the game's integer getter, not a guessed conversion.
        public static bool TrySupplyRatio(double pack, int clip, double costPerRound, double capacity, out HudQuantity value)
        {
            value = default;
            if (!double.IsFinite(pack) || pack < 0 || clip < 0 || !double.IsFinite(costPerRound) || costPerRound <= 0
                || !double.IsFinite(capacity) || capacity <= 0) return false;
            return TryFraction((pack + clip * costPerRound) / capacity, out value);
        }
        static bool TryQuantity(double amount, HudUnit unit, out HudQuantity value)
        {
            var candidate = new HudQuantity(amount, unit);
            value = candidate.IsValid ? candidate : default; return candidate.IsValid;
        }
    }
}
