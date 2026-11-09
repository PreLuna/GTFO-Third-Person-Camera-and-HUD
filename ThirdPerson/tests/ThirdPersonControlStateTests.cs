using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using PreLuna.ThirdPerson.Control;

static class ThirdPersonControlStateTests
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
        readonly Dictionary<string, ThirdPersonControlValue> values = new(StringComparer.Ordinal);
        internal bool AutoSave = true;
        internal ThirdPersonControlState State;

        internal Harness(string content = "[镜头]\n距离 = 2.25\n右肩偏移 = 0.48\n支点高度偏移 = 0.1\n[瞄准]\n肩射距离 = 1.35\n[风格]\n左肩 = false\n")
        {
            path = TempFile();
            File.WriteAllText(path, content);
            values["distance"] = ThirdPersonControlValue.Number(2.25);
            values["shoulder"] = ThirdPersonControlValue.Number(.48);
            values["height"] = ThirdPersonControlValue.Number(.1);
            values["shoulderDistance"] = ThirdPersonControlValue.Number(1.35);
            values["leftShoulder"] = ThirdPersonControlValue.Boolean(false);
            State = new ThirdPersonControlState(path, Read, Write, Serialize, () => AutoSave, value => AutoSave = value);
        }

        internal string Path => path;
        internal ThirdPersonControlState Current => State;
        internal ThirdPersonControlValue Get(string key) => values[key];
        internal void SetValue(string key, ThirdPersonControlValue value) => values[key] = value;

        ThirdPersonControlValue Read(string key) => values.TryGetValue(key, out var value) ? value : null;

        void Write(string key, ThirdPersonControlValue value) => values[key] = value;

        string Serialize(string key)
        {
            var value = values[key];
            if (value.TryGetBoolean(out var boolean)) return boolean ? "true" : "false";
            if (value.TryGetNumber(out var number)) return number.ToString("0.###", CultureInfo.InvariantCulture);
            return null;
        }
    }

    static string TempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "preluna-thirdperson-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "PreLuna.ThirdPerson.cfg");
    }

    static int Main(string[] args)
    {
        Case("rejects-out-of-range-before-writing", () =>
        {
            var h = new Harness();
            var result = h.State.SetPersistent(new ThirdPersonControlChange(h.State.Revision, new Dictionary<string, ThirdPersonControlValue>
            {
                ["distance"] = ThirdPersonControlValue.Number(9)
            }));
            Check(result.Outcome == ThirdPersonControlOutcome.ValidationRejected, "outcome");
            Check(!File.ReadAllText(h.Path).Contains("距离 = 9"), "file");
            Check(h.AutoSave, "auto save restored");
        });

        Case("saves-five-fields-and-restores-auto-save", () =>
        {
            var h = new Harness();
            var revision = h.State.Revision;
            var result = h.State.SetPersistent(new ThirdPersonControlChange(revision, new Dictionary<string, ThirdPersonControlValue>
            {
                ["distance"] = ThirdPersonControlValue.Number(3.1),
                ["shoulder"] = ThirdPersonControlValue.Number(.3),
                ["height"] = ThirdPersonControlValue.Number(.2),
                ["shoulderDistance"] = ThirdPersonControlValue.Number(1.6),
                ["leftShoulder"] = ThirdPersonControlValue.Boolean(true)
            }));
            Check(result.Outcome == ThirdPersonControlOutcome.Success, "outcome:" + result.Message);
            Check(h.State.Revision != revision, "revision");
            Check(h.AutoSave, "auto save restored");
            var text = File.ReadAllText(h.Path);
            Check(text.Contains("距离 = 3.1"), "distance");
            Check(text.Contains("右肩偏移 = 0.3"), "shoulder");
            Check(text.Contains("支点高度偏移 = 0.2"), "height");
            Check(text.Contains("肩射距离 = 1.6"), "aim");
            Check(text.Contains("左肩 = true"), "left");
        });

        Case("external-file-change-is-conflict", () =>
        {
            var h = new Harness();
            File.AppendAllText(h.Path, "外部 = 1\n");
            var result = h.State.SetPersistent(new ThirdPersonControlChange(h.State.Revision, new Dictionary<string, ThirdPersonControlValue>
            {
                ["distance"] = ThirdPersonControlValue.Number(3)
            }));
            Check(result.Outcome == ThirdPersonControlOutcome.StateConflict, "outcome");
            Check(h.AutoSave, "auto save restored");
        });

        Case("internal-hotkey-file-change-is-adopted", () =>
        {
            var h = new Harness();
            File.AppendAllText(h.Path, "# hotkey save\n");
            h.SetValue("distance", ThirdPersonControlValue.Number(3));
            h.Current.ReadSnapshot();
            var result = h.State.SetPersistent(new ThirdPersonControlChange(h.State.Revision, new Dictionary<string, ThirdPersonControlValue>
            {
                ["distance"] = ThirdPersonControlValue.Number(3)
            }));
            Check(result.Outcome == ThirdPersonControlOutcome.Success, "outcome:" + result.Message);
        });

        var output = args.Length > 0 ? args[0] : "ThirdPersonControlStateTests.json";
        File.WriteAllText(output, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Exists(result => !result.Passed) ? 1 : 0;
    }
}
