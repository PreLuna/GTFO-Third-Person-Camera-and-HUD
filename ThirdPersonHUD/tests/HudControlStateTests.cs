using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using PreLuna.ThirdPersonHUD.Control;

static class HudControlStateTests
{
    sealed record Result(string Name, bool Passed, string Error);
    static readonly List<Result> Results = new();

    static void Check(bool value, string message = "assertion failed")
    {
        if (!value) throw new Exception(message);
    }

    static void Case(string name, Action test)
    {
        try { test(); Results.Add(new(name, true, null)); }
        catch (Exception error) { Results.Add(new(name, false, error.ToString())); }
    }

    sealed class Harness
    {
        readonly string path;
        readonly Dictionary<string, HudControlValue> values = new(StringComparer.Ordinal);
        internal HudControlState State;

        internal Harness(string content = "[Panel]\nEnabled = true\n")
        {
            path = TempFile();
            if (content != null) File.WriteAllText(path, content);
            values["panel"] = HudControlValue.Boolean(true);
            values["personal"] = HudControlValue.Boolean(true);
            values["team"] = HudControlValue.Boolean(true);
            values["scene"] = HudControlValue.Boolean(true);
            values["alerts"] = HudControlValue.Boolean(true);
            values["panelScale"] = HudControlValue.Number(1);
            values["panelWidth"] = HudControlValue.Number(360);
            State = new HudControlState(path, Read, Write, Serialize);
        }

        internal string Path => path;
        internal HudControlValue Get(string key) => values[key];

        HudControlValue Read(string key) => values.TryGetValue(key, out var value) ? value : null;

        void Write(string key, HudControlValue value) => values[key] = value;

        string Serialize(string key) => SerializeValue(key);

        internal string SerializeValue(string key)
        {
            var value = values[key];
            if (value.TryGetBoolean(out var boolean)) return boolean ? "true" : "false";
            if (value.TryGetNumber(out var number)) return number.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetInteger(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
            return string.Empty;
        }
    }

    static string TempFile()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "preluna-hud-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, "PreLuna.ThirdPersonHUD.cfg");
    }

    static int Main(string[] args)
    {
        Case("validation-rejects-out-of-range", () =>
        {
            var h = new Harness();
            var change = new HudControlChange(h.State.Revision, new Dictionary<string, HudControlValue>
            {
                ["panelScale"] = HudControlValue.Number(9)
            });
            var result = h.State.SetPersistent(change);
            Check(result.Outcome == HudControlOutcome.ValidationRejected, "outcome");
            Check(!File.ReadAllText(h.Path).Contains("Scale = 9"), "file unchanged");
        });

        Case("revision-conflict-is-rejected", () =>
        {
            var h = new Harness();
            var change = new HudControlChange("999", new Dictionary<string, HudControlValue>
            {
                ["panel"] = HudControlValue.Boolean(false)
            });
            var result = h.State.SetPersistent(change);
            Check(result.Outcome == HudControlOutcome.StateConflict, "outcome");
        });

        Case("successful-save-updates-file-values-and-revision", () =>
        {
            var h = new Harness("[Panel]\nEnabled = true\n[Personal]\nEnabled = true\n");
            var revision = h.State.Revision;
            var change = new HudControlChange(revision, new Dictionary<string, HudControlValue>
            {
                ["panel"] = HudControlValue.Boolean(false)
            });
            var result = h.State.SetPersistent(change);
            Check(result.Outcome == HudControlOutcome.Success, "outcome");
            Check(h.Get("panel").TryGetBoolean(out var stored) && !stored, "memory");
            Check(File.ReadAllText(h.Path).Contains("Enabled = false"), "file");
            Check(h.State.Revision != revision, "revision");
            Check(result.Snapshot != null && result.Snapshot.Revision == h.State.Revision, "snapshot");
        });

        Case("external-file-change-is-a-conflict-and-restores-memory", () =>
        {
            var h = new Harness("[Panel]\nEnabled = true\n");
            File.AppendAllText(h.Path, "External = 1\n");
            var change = new HudControlChange(h.State.Revision, new Dictionary<string, HudControlValue>
            {
                ["panel"] = HudControlValue.Boolean(false)
            });
            var result = h.State.SetPersistent(change);
            Check(result.Outcome == HudControlOutcome.StateConflict, "outcome");
            Check(h.Get("panel").TryGetBoolean(out var stored) && stored, "memory restored");
        });

        Case("save-failure-is-reported", () =>
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "preluna-hud-control-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "PreLuna.ThirdPersonHUD.cfg");
            Directory.CreateDirectory(path); // make the target a directory so replace fails
            var state = new HudControlState(path,
                key => HudControlValue.Boolean(true),
                (key, value) => { },
                key => "true");
            var result = state.SetPersistent(new HudControlChange(state.Revision, new Dictionary<string, HudControlValue>
            {
                ["panel"] = HudControlValue.Boolean(false)
            }));
            Check(result.Outcome == HudControlOutcome.SaveFailed, "outcome");
        });

        var output = args.Length > 0 ? args[0] : "HudControlStateTests.json";
        File.WriteAllText(output, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Exists(result => !result.Passed) ? 1 : 0;
    }
}
