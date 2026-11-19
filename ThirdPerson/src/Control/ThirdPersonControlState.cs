using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Control
{
    internal sealed class ThirdPersonControlState
    {
        internal const string TargetId = "PreLuna.ThirdPerson";
        internal const string ControlVersion = "0.6.32";

        internal static readonly IReadOnlyList<ThirdPersonControlSetting> Definitions = new[]
        {
            new ThirdPersonControlSetting("distance", "镜头距离", ThirdPersonControlKind.Number, ThirdPersonControlValue.Number(2.25), "镜头", "距离", .6, 4),
            new ThirdPersonControlSetting("shoulder", "右肩偏移", ThirdPersonControlKind.Number, ThirdPersonControlValue.Number(.48), "镜头", "右肩偏移", -1, 1),
            new ThirdPersonControlSetting("height", "支点高度偏移", ThirdPersonControlKind.Number, ThirdPersonControlValue.Number(.1), "镜头", "支点高度偏移", -.35, .6),
            new ThirdPersonControlSetting("shoulderDistance", "肩射距离", ThirdPersonControlKind.Number, ThirdPersonControlValue.Number(1.35), "瞄准", "肩射距离", .6, 3),
            new ThirdPersonControlSetting("leftShoulder", "左肩", ThirdPersonControlKind.Boolean, ThirdPersonControlValue.Boolean(false), "风格", "左肩"),
            new ThirdPersonControlSetting("key.toggle", "切换第三人称", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(287), "按键", "开关", 0, 509),
            new ThirdPersonControlSetting("key.reset", "恢复镜头默认", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(288), "按键", "恢复默认", 0, 509),
            new ThirdPersonControlSetting("key.snapshot", "诊断快照", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(289), "按键", "诊断快照", 0, 509),
            new ThirdPersonControlSetting("key.swapShoulder", "左右换肩", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(290), "按键", "左右换肩", 0, 509),
            new ThirdPersonControlSetting("key.cyclePreset", "切换镜头风格", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(291), "按键", "切换风格", 0, 509),
            new ThirdPersonControlSetting("key.capture", "性能采集", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(292), "按键", "性能采集", 0, 509),
            new ThirdPersonControlSetting("key.pointer", "指向调试覆盖层", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(293), "按键", "指向覆盖层", 0, 509),
            new ThirdPersonControlSetting("key.observe", "按住站定观察", ThirdPersonControlKind.Integer, ThirdPersonControlValue.Integer(307), "镜头·C2", "观察按键", 0, 509)
        };

        readonly Func<int, bool> validKey;
        readonly string path;
        readonly Func<string, ThirdPersonControlValue> readSaved;
        readonly Action<string, ThirdPersonControlValue> writeSaved;
        readonly Func<string, string> serialize;
        readonly Func<bool> getAutoSave;
        readonly Action<bool> setAutoSave;
        readonly Dictionary<string, ThirdPersonControlValue> saved = new(StringComparer.Ordinal);
        readonly Dictionary<string, ThirdPersonControlValue> effective = new(StringComparer.Ordinal);
        string revision = "1";
        string fileHash;

        internal ThirdPersonControlState(
            string path,
            Func<string, ThirdPersonControlValue> readSaved,
            Action<string, ThirdPersonControlValue> writeSaved,
            Func<string, string> serialize,
            Func<bool> getAutoSave = null,
            Action<bool> setAutoSave = null, Func<int, bool> validKey = null)
        {
            this.validKey = validKey;
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.readSaved = readSaved ?? throw new ArgumentNullException(nameof(readSaved));
            this.writeSaved = writeSaved ?? throw new ArgumentNullException(nameof(writeSaved));
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.getAutoSave = getAutoSave;
            this.setAutoSave = setAutoSave;
            fileHash = ThirdPersonControlFileStore.Capture(path);

            foreach (var definition in Definitions)
            {
                var value = this.readSaved(definition.Key) ?? definition.DefaultValue;
                saved[definition.Key] = value;
                effective[definition.Key] = value;
            }
        }

        internal string Revision => revision;

        // Call only after a known successful BepInEx automatic save. Normal camera
        // hotkeys also write fields outside Definitions (Enabled, diagnostics, etc.).
        // Those writes must advance the file revision even when no managed value changed.
        internal void ObserveAutomaticSave()
        {
            var currentHash = ThirdPersonControlFileStore.Capture(path);
            if (!string.Equals(fileHash, currentHash, StringComparison.Ordinal))
            {
                fileHash = currentHash;
                BumpRevision();
            }
            RefreshSaved(false);
        }

        internal ThirdPersonControlSnapshot ReadSnapshot()
        {
            RefreshSaved();
            return Snapshot();
        }

        internal ThirdPersonControlResult Validate(ThirdPersonControlChange change)
        {
            var check = Check(change);
            if (check != null) return check;
            return new ThirdPersonControlResult(ThirdPersonControlOutcome.NoChanges, "没有改动", null, Snapshot());
        }

        internal ThirdPersonControlResult SetPersistent(ThirdPersonControlChange change)
        {
            var check = Check(change, requireChanges: true);
            if (check != null) return check;

            var previous = SnapshotValues(change.Values.Keys);
            var previousAutoSave = getAutoSave?.Invoke();
            var restoreAutoSave = setAutoSave != null && previousAutoSave.HasValue;
            if (restoreAutoSave) setAutoSave(false);

            try
            {
                try
                {
                    foreach (var pair in change.Values) writeSaved(pair.Key, pair.Value);
                    var writes = new List<ThirdPersonControlWrite>();
                    foreach (var pair in change.Values)
                    {
                        var definition = FindDefinition(pair.Key);
                        var text = serialize(pair.Key);
                        if (definition == null || text == null)
                            throw new InvalidOperationException("缺少配置映射：" + pair.Key);
                        writes.Add(new ThirdPersonControlWrite(definition.Section, definition.ConfigKey, text));
                    }

                    fileHash = ThirdPersonControlFileStore.Save(path, fileHash, writes);
                }
                catch (ThirdPersonControlConflictException error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new ThirdPersonControlResult(ThirdPersonControlOutcome.StateConflict, "配置文件已被外部修改：" + error.Message, null, Snapshot());
                }
                catch (Exception error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new ThirdPersonControlResult(ThirdPersonControlOutcome.SaveFailed, "保存失败：" + error.Message, null, Snapshot());
                }

                foreach (var pair in change.Values)
                {
                    saved[pair.Key] = pair.Value;
                    effective[pair.Key] = pair.Value;
                }

                BumpRevision();
                return new ThirdPersonControlResult(ThirdPersonControlOutcome.Success, "已保存并生效", null, Snapshot());
            }
            finally
            {
                if (restoreAutoSave) setAutoSave(previousAutoSave.Value);
            }
        }

        ThirdPersonControlResult Check(ThirdPersonControlChange change, bool requireChanges = false)
        {
            if (change == null)
                return new ThirdPersonControlResult(ThirdPersonControlOutcome.ValidationRejected, "缺少变更集", new[] { new ThirdPersonControlIssue(string.Empty, "null change") }, Snapshot());
            if (!string.Equals(change.ExpectedRevision, revision, StringComparison.Ordinal))
                return new ThirdPersonControlResult(ThirdPersonControlOutcome.StateConflict, "目标状态已变化，请刷新后重试", null, Snapshot());
            if (requireChanges && change.Values.Count == 0)
                return new ThirdPersonControlResult(ThirdPersonControlOutcome.NoChanges, "没有改动", null, Snapshot());

            var issues = new List<ThirdPersonControlIssue>();
            foreach (var pair in change.Values)
            {
                var definition = FindDefinition(pair.Key);
                if (definition == null)
                {
                    issues.Add(new ThirdPersonControlIssue(pair.Key, "未注册的设置"));
                    continue;
                }

                var issue = definition.Validate(pair.Value);
                if (issue == null && pair.Key.StartsWith("key.", StringComparison.Ordinal) &&
                    pair.Value.TryGetInteger(out var code) && validKey != null && !validKey(code))
                    issue = new ThirdPersonControlIssue(pair.Key, "不是有效的按键");
                if (issue != null) issues.Add(issue);
            }

            if (issues.Count == 0) return null;
            return new ThirdPersonControlResult(ThirdPersonControlOutcome.ValidationRejected, "变更未通过校验", issues, Snapshot());
        }

        Dictionary<string, ThirdPersonControlValue> SnapshotValues(IEnumerable<string> keys)
        {
            var values = new Dictionary<string, ThirdPersonControlValue>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (saved.TryGetValue(key, out var value)) values[key] = value;
            return values;
        }

        void RestoreValues(Dictionary<string, ThirdPersonControlValue> values)
        {
            foreach (var pair in values)
            {
                try { writeSaved(pair.Key, pair.Value); }
                catch { }
            }
        }

        void RefreshSaved(bool adoptFile = true)
        {
            var changed = false;
            foreach (var definition in Definitions)
            {
                var value = readSaved(definition.Key) ?? definition.DefaultValue;
                if (!saved.TryGetValue(definition.Key, out var previous) || !previous.ValueEquals(value))
                {
                    saved[definition.Key] = value;
                    effective[definition.Key] = value;
                    changed = true;
                }
            }

            if (changed)
            {
                // Hotkeys (F9/F10 etc.) update ConfigEntry values and let BepInEx save the file.
                // Adopt that internal file state so a subsequent ModControl write is not a false conflict.
                if (adoptFile)
                {
                    var current = ThirdPersonControlFileStore.Capture(path);
                    if (!string.IsNullOrEmpty(current)) fileHash = current;
                }

                BumpRevision();
            }
        }

        void BumpRevision()
        {
            var number = 1;
            if (int.TryParse(revision, out var parsed)) number = parsed + 1;
            revision = number.ToString();
        }

        ThirdPersonControlSnapshot Snapshot()
            => new ThirdPersonControlSnapshot(
                TargetId,
                "PreLuna 第三人称镜头",
                ControlVersion,
                ThirdPersonControlStatus.Available,
                revision,
                ThirdPersonControlCapabilities.Validate | ThirdPersonControlCapabilities.SavePersistent,
                Definitions,
                saved,
                effective);

        internal static ThirdPersonControlSnapshot Unavailable(ThirdPersonControlStatus status)
            => new ThirdPersonControlSnapshot(
                TargetId,
                "PreLuna 第三人称镜头",
                string.Empty,
                status,
                string.Empty,
                ThirdPersonControlCapabilities.None,
                Array.Empty<ThirdPersonControlSetting>(),
                new Dictionary<string, ThirdPersonControlValue>(),
                new Dictionary<string, ThirdPersonControlValue>());

        static ThirdPersonControlSetting FindDefinition(string key)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.Key, key, StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
