using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPersonHUD.Control
{
    public enum HudControlStatus
    {
        Available,
        Uninitialized,
        Missing,
        Incompatible
    }

    [Flags]
    public enum HudControlCapabilities
    {
        None = 0,
        Validate = 1,
        SavePersistent = 2
    }

    public enum HudControlOutcome
    {
        Success,
        NoChanges,
        ValidationRejected,
        StateConflict,
        Unavailable,
        SaveFailed
    }

    public enum HudControlKind
    {
        Boolean,
        Number,
        Integer
    }

    public sealed class HudControlValue
    {
        HudControlValue(HudControlKind kind, object raw)
        {
            Kind = kind;
            Raw = raw;
        }

        public HudControlKind Kind { get; }
        public object Raw { get; }

        public static HudControlValue Boolean(bool value) => new HudControlValue(HudControlKind.Boolean, value);
        public static HudControlValue Number(double value) => new HudControlValue(HudControlKind.Number, value);
        public static HudControlValue Integer(int value) => new HudControlValue(HudControlKind.Integer, value);

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == HudControlKind.Boolean && Raw is bool typed)
            {
                value = typed;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(out double value)
        {
            if (Kind == HudControlKind.Number && Raw is double typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInteger(out int value)
        {
            if (Kind == HudControlKind.Integer && Raw is int typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool ValueEquals(HudControlValue other)
        {
            if (other == null || Kind != other.Kind || Raw == null || other.Raw == null) return false;
            if (Kind == HudControlKind.Number)
                return Raw is double left && other.Raw is double right && Math.Abs(left - right) < 1e-9;
            return Raw.Equals(other.Raw);
        }

        public override string ToString() => Raw?.ToString() ?? string.Empty;
    }

    public sealed class HudControlSetting
    {
        public HudControlSetting(
            string key,
            string displayName,
            HudControlKind kind,
            HudControlValue defaultValue,
            string section,
            string configKey,
            double? minimum = null,
            double? maximum = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            DisplayName = displayName ?? key;
            Kind = kind;
            DefaultValue = defaultValue;
            Section = section ?? string.Empty;
            ConfigKey = configKey ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public HudControlKind Kind { get; }
        public HudControlValue DefaultValue { get; }
        public string Section { get; }
        public string ConfigKey { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        public HudControlIssue Validate(HudControlValue value)
        {
            if (value == null) return new HudControlIssue(Key, "缺少值");
            if (value.Kind != Kind) return new HudControlIssue(Key, "值类型不匹配");

            if (Kind == HudControlKind.Boolean)
                return value.TryGetBoolean(out _) ? null : new HudControlIssue(Key, "不是合法布尔值");

            if (Kind == HudControlKind.Integer)
            {
                if (!value.TryGetInteger(out var integer)) return new HudControlIssue(Key, "不是合法整数");
                return CheckRange(integer);
            }

            if (!value.TryGetNumber(out var number) || double.IsNaN(number) || double.IsInfinity(number))
                return new HudControlIssue(Key, "不是合法数值");
            return CheckRange(number);
        }

        HudControlIssue CheckRange(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value) return new HudControlIssue(Key, "小于最小值 " + Minimum.Value);
            if (Maximum.HasValue && value > Maximum.Value) return new HudControlIssue(Key, "大于最大值 " + Maximum.Value);
            return null;
        }
    }

    public sealed class HudControlIssue
    {
        public HudControlIssue(string key, string message)
        {
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Key { get; }
        public string Message { get; }
    }

    public sealed class HudControlSnapshot
    {
        public HudControlSnapshot(
            string targetId,
            string displayName,
            string version,
            HudControlStatus status,
            string revision,
            HudControlCapabilities capabilities,
            IReadOnlyList<HudControlSetting> settings,
            IDictionary<string, HudControlValue> saved,
            IDictionary<string, HudControlValue> effective)
        {
            TargetId = targetId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Version = version ?? string.Empty;
            Status = status;
            Revision = revision ?? string.Empty;
            Capabilities = capabilities;
            Settings = settings ?? Array.Empty<HudControlSetting>();
            Saved = Copy(saved);
            Effective = Copy(effective);
        }

        public string TargetId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public HudControlStatus Status { get; }
        public string Revision { get; }
        public HudControlCapabilities Capabilities { get; }
        public IReadOnlyList<HudControlSetting> Settings { get; }
        public IReadOnlyDictionary<string, HudControlValue> Saved { get; }
        public IReadOnlyDictionary<string, HudControlValue> Effective { get; }

        static IReadOnlyDictionary<string, HudControlValue> Copy(IDictionary<string, HudControlValue> source)
        {
            var copy = new Dictionary<string, HudControlValue>(StringComparer.Ordinal);
            if (source != null)
                foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public sealed class HudControlChange
    {
        public HudControlChange(string expectedRevision, IDictionary<string, HudControlValue> values)
        {
            ExpectedRevision = expectedRevision ?? string.Empty;
            var copy = new Dictionary<string, HudControlValue>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) copy[pair.Key] = pair.Value;
            Values = copy;
        }

        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, HudControlValue> Values { get; }
    }

    public sealed class HudControlResult
    {
        public HudControlResult(
            HudControlOutcome outcome,
            string message,
            IReadOnlyList<HudControlIssue> issues = null,
            HudControlSnapshot snapshot = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            Issues = issues ?? Array.Empty<HudControlIssue>();
            Snapshot = snapshot;
        }

        public HudControlOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<HudControlIssue> Issues { get; }
        public HudControlSnapshot Snapshot { get; }
    }
}
