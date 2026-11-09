using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Control
{
    public enum ThirdPersonControlStatus
    {
        Available,
        Uninitialized,
        Missing,
        Incompatible
    }

    [Flags]
    public enum ThirdPersonControlCapabilities
    {
        None = 0,
        Validate = 1,
        SavePersistent = 2
    }

    public enum ThirdPersonControlOutcome
    {
        Success,
        NoChanges,
        ValidationRejected,
        StateConflict,
        Unavailable,
        SaveFailed
    }

    public enum ThirdPersonControlKind
    {
        Boolean,
        Number,
        Integer
    }

    public sealed class ThirdPersonControlValue
    {
        ThirdPersonControlValue(ThirdPersonControlKind kind, object raw)
        {
            Kind = kind;
            Raw = raw;
        }

        public ThirdPersonControlKind Kind { get; }
        public object Raw { get; }

        public static ThirdPersonControlValue Boolean(bool value) => new ThirdPersonControlValue(ThirdPersonControlKind.Boolean, value);
        public static ThirdPersonControlValue Number(double value) => new ThirdPersonControlValue(ThirdPersonControlKind.Number, value);
        public static ThirdPersonControlValue Integer(int value) => new ThirdPersonControlValue(ThirdPersonControlKind.Integer, value);

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == ThirdPersonControlKind.Boolean && Raw is bool typed)
            {
                value = typed;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(out double value)
        {
            if (Kind == ThirdPersonControlKind.Number && Raw is double typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInteger(out int value)
        {
            if (Kind == ThirdPersonControlKind.Integer && Raw is int typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool ValueEquals(ThirdPersonControlValue other)
        {
            if (other == null || Kind != other.Kind || Raw == null || other.Raw == null) return false;
            if (Kind == ThirdPersonControlKind.Number)
                return Raw is double left && other.Raw is double right && Math.Abs(left - right) < 1e-9;
            return Raw.Equals(other.Raw);
        }

        public override string ToString() => Raw?.ToString() ?? string.Empty;
    }

    public sealed class ThirdPersonControlSetting
    {
        public ThirdPersonControlSetting(
            string key,
            string displayName,
            ThirdPersonControlKind kind,
            ThirdPersonControlValue defaultValue,
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
        public ThirdPersonControlKind Kind { get; }
        public ThirdPersonControlValue DefaultValue { get; }
        public string Section { get; }
        public string ConfigKey { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        public ThirdPersonControlIssue Validate(ThirdPersonControlValue value)
        {
            if (value == null) return new ThirdPersonControlIssue(Key, "缺少值");
            if (value.Kind != Kind) return new ThirdPersonControlIssue(Key, "值类型不匹配");

            if (Kind == ThirdPersonControlKind.Boolean)
                return value.TryGetBoolean(out _) ? null : new ThirdPersonControlIssue(Key, "不是合法布尔值");

            if (Kind == ThirdPersonControlKind.Integer)
            {
                if (!value.TryGetInteger(out var integer)) return new ThirdPersonControlIssue(Key, "不是合法整数");
                return CheckRange(integer);
            }

            if (!value.TryGetNumber(out var number) || double.IsNaN(number) || double.IsInfinity(number))
                return new ThirdPersonControlIssue(Key, "不是合法数值");
            return CheckRange(number);
        }

        ThirdPersonControlIssue CheckRange(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value) return new ThirdPersonControlIssue(Key, "小于最小值 " + Minimum.Value);
            if (Maximum.HasValue && value > Maximum.Value) return new ThirdPersonControlIssue(Key, "大于最大值 " + Maximum.Value);
            return null;
        }
    }

    public sealed class ThirdPersonControlIssue
    {
        public ThirdPersonControlIssue(string key, string message)
        {
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Key { get; }
        public string Message { get; }
    }

    public sealed class ThirdPersonControlSnapshot
    {
        public ThirdPersonControlSnapshot(
            string targetId,
            string displayName,
            string version,
            ThirdPersonControlStatus status,
            string revision,
            ThirdPersonControlCapabilities capabilities,
            IReadOnlyList<ThirdPersonControlSetting> settings,
            IDictionary<string, ThirdPersonControlValue> saved,
            IDictionary<string, ThirdPersonControlValue> effective)
        {
            TargetId = targetId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Version = version ?? string.Empty;
            Status = status;
            Revision = revision ?? string.Empty;
            Capabilities = capabilities;
            Settings = settings ?? Array.Empty<ThirdPersonControlSetting>();
            Saved = Copy(saved);
            Effective = Copy(effective);
        }

        public string TargetId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public ThirdPersonControlStatus Status { get; }
        public string Revision { get; }
        public ThirdPersonControlCapabilities Capabilities { get; }
        public IReadOnlyList<ThirdPersonControlSetting> Settings { get; }
        public IReadOnlyDictionary<string, ThirdPersonControlValue> Saved { get; }
        public IReadOnlyDictionary<string, ThirdPersonControlValue> Effective { get; }

        static IReadOnlyDictionary<string, ThirdPersonControlValue> Copy(IDictionary<string, ThirdPersonControlValue> source)
        {
            var copy = new Dictionary<string, ThirdPersonControlValue>(StringComparer.Ordinal);
            if (source != null)
                foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public sealed class ThirdPersonControlChange
    {
        public ThirdPersonControlChange(string expectedRevision, IDictionary<string, ThirdPersonControlValue> values)
        {
            ExpectedRevision = expectedRevision ?? string.Empty;
            var copy = new Dictionary<string, ThirdPersonControlValue>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) copy[pair.Key] = pair.Value;
            Values = copy;
        }

        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, ThirdPersonControlValue> Values { get; }
    }

    public sealed class ThirdPersonControlResult
    {
        public ThirdPersonControlResult(
            ThirdPersonControlOutcome outcome,
            string message,
            IReadOnlyList<ThirdPersonControlIssue> issues = null,
            ThirdPersonControlSnapshot snapshot = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            Issues = issues ?? Array.Empty<ThirdPersonControlIssue>();
            Snapshot = snapshot;
        }

        public ThirdPersonControlOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<ThirdPersonControlIssue> Issues { get; }
        public ThirdPersonControlSnapshot Snapshot { get; }
    }
}
