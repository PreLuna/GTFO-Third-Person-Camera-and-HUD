using System;
using System.Collections.Generic;
using System.Linq;

namespace PreLuna.ThirdPersonHUD.Control
{
    internal sealed class HudControlState
    {
        internal const string TargetId = "PreLuna.ThirdPersonHUD";
        internal const string ControlVersion = "0.13.1";

        internal static readonly IReadOnlyList<HudControlSetting> Definitions = new[]
        {
            new HudControlSetting("panel", "角色旁面板", HudControlKind.Boolean, HudControlValue.Boolean(true), "Panel", "Enabled"),
            new HudControlSetting("personal", "个人读数", HudControlKind.Boolean, HudControlValue.Boolean(true), "Personal", "Enabled"),
            new HudControlSetting("team", "队伍摘要", HudControlKind.Boolean, HudControlValue.Boolean(true), "Team", "Enabled"),
            new HudControlSetting("scene", "场景提示", HudControlKind.Boolean, HudControlValue.Boolean(true), "Scene", "Enabled"),
            new HudControlSetting("alerts", "提示区", HudControlKind.Boolean, HudControlValue.Boolean(true), "Alerts", "Enabled"),
            new HudControlSetting("panelScale", "面板缩放", HudControlKind.Number, HudControlValue.Number(1), "Panel.Layout", "Scale", .65, 1.6),
            new HudControlSetting("panelWidth", "面板宽度", HudControlKind.Number, HudControlValue.Number(360), "Panel.Layout", "Width", 260, 420)
        };

        readonly string path;
        readonly Func<string, HudControlValue> readSaved;
        readonly Action<string, HudControlValue> writeSaved;
        readonly Func<string, string> serialize;
        readonly Dictionary<string, HudControlValue> saved = new Dictionary<string, HudControlValue>(StringComparer.Ordinal);
        readonly Dictionary<string, HudControlValue> effective = new Dictionary<string, HudControlValue>(StringComparer.Ordinal);
        string revision = "1";
        string fileHash;

        internal HudControlState(
            string path,
            Func<string, HudControlValue> readSaved,
            Action<string, HudControlValue> writeSaved,
            Func<string, string> serialize)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.readSaved = readSaved ?? throw new ArgumentNullException(nameof(readSaved));
            this.writeSaved = writeSaved ?? throw new ArgumentNullException(nameof(writeSaved));
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            fileHash = HudConfigFileStore.Capture(path);

            foreach (var definition in Definitions)
            {
                var value = this.readSaved(definition.Key) ?? definition.DefaultValue;
                saved[definition.Key] = value;
                effective[definition.Key] = value;
            }
        }

        internal string Revision => revision;

        internal HudControlSnapshot ReadSnapshot()
        {
            RefreshSaved();
            return Snapshot();
        }

        internal HudControlResult Validate(HudControlChange change)
        {
            var check = Check(change);
            if (check != null) return check;
            return new HudControlResult(HudControlOutcome.NoChanges, "没有改动", null, Snapshot());
        }

        internal HudControlResult SetPersistent(HudControlChange change)
        {
            var check = Check(change, requireChanges: true);
            if (check != null) return check;

            var previous = SnapshotValues(change.Values.Keys);
            try
            {
                foreach (var pair in change.Values) writeSaved(pair.Key, pair.Value);
                var writes = new List<HudConfigWrite>();
                foreach (var pair in change.Values)
                {
                    var definition = FindDefinition(pair.Key);
                    var text = serialize(pair.Key);
                    if (definition == null || text == null)
                        throw new InvalidOperationException("缺少配置映射：" + pair.Key);
                    writes.Add(new HudConfigWrite(definition.Section, definition.ConfigKey, text));
                }

                fileHash = HudConfigFileStore.Save(path, fileHash, writes);
            }
            catch (HudConfigConflictException error)
            {
                RestoreValues(previous);
                RefreshSaved();
                return new HudControlResult(HudControlOutcome.StateConflict, "配置文件已被外部修改：" + error.Message, null, Snapshot());
            }
            catch (Exception error)
            {
                RestoreValues(previous);
                RefreshSaved();
                return new HudControlResult(HudControlOutcome.SaveFailed, "保存失败：" + error.Message, null, Snapshot());
            }

            foreach (var pair in change.Values)
            {
                saved[pair.Key] = pair.Value;
                effective[pair.Key] = pair.Value;
            }

            BumpRevision();
            return new HudControlResult(HudControlOutcome.Success, "已保存并生效", null, Snapshot());
        }

        HudControlResult Check(HudControlChange change, bool requireChanges = false)
        {
            if (change == null)
                return new HudControlResult(HudControlOutcome.ValidationRejected, "缺少变更集", new[] { new HudControlIssue(string.Empty, "null change") }, Snapshot());
            if (!string.Equals(change.ExpectedRevision, revision, StringComparison.Ordinal))
                return new HudControlResult(HudControlOutcome.StateConflict, "目标状态已变化，请刷新后重试", null, Snapshot());
            if (requireChanges && change.Values.Count == 0)
                return new HudControlResult(HudControlOutcome.NoChanges, "没有改动", null, Snapshot());

            var issues = new List<HudControlIssue>();
            foreach (var pair in change.Values)
            {
                var definition = FindDefinition(pair.Key);
                if (definition == null)
                {
                    issues.Add(new HudControlIssue(pair.Key, "未注册的设置"));
                    continue;
                }

                var issue = definition.Validate(pair.Value);
                if (issue != null) issues.Add(issue);
            }

            if (issues.Count == 0) return null;
            return new HudControlResult(HudControlOutcome.ValidationRejected, "变更未通过校验", issues, Snapshot());
        }

        Dictionary<string, HudControlValue> SnapshotValues(IEnumerable<string> keys)
        {
            var values = new Dictionary<string, HudControlValue>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (saved.TryGetValue(key, out var value)) values[key] = value;
            return values;
        }

        void RestoreValues(Dictionary<string, HudControlValue> values)
        {
            foreach (var pair in values)
            {
                try { writeSaved(pair.Key, pair.Value); }
                catch { }
            }
        }

        void RefreshSaved()
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

            if (changed) BumpRevision();
        }

        void BumpRevision()
        {
            var number = 1;
            if (int.TryParse(revision, out var parsed)) number = parsed + 1;
            revision = number.ToString();
        }

        HudControlSnapshot Snapshot()
            => new HudControlSnapshot(
                TargetId,
                "PreLuna 第三人称 HUD",
                ControlVersion,
                HudControlStatus.Available,
                revision,
                HudControlCapabilities.Validate | HudControlCapabilities.SavePersistent,
                Definitions,
                saved,
                effective);

        internal static HudControlSnapshot Unavailable(HudControlStatus status)
            => new HudControlSnapshot(
                TargetId,
                "PreLuna 第三人称 HUD",
                string.Empty,
                status,
                string.Empty,
                HudControlCapabilities.None,
                Array.Empty<HudControlSetting>(),
                new Dictionary<string, HudControlValue>(),
                new Dictionary<string, HudControlValue>());

        static HudControlSetting FindDefinition(string key)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.Key, key, StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
