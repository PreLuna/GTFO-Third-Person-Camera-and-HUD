using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

static class HudConfigFileStoreTests
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

    static string TempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "preluna-hud-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "PreLuna.ThirdPersonHUD.cfg");
    }

    static int Main(string[] args)
    {
        Case("update-preserves-comments-and-unknown-sections", () =>
        {
            var path = TempFile();
            File.WriteAllText(path,
                "# header\n[Panel]\n\n## comment\nEnabled = true\nUnknown = 42\n\n[Future]\nValue = keep\n");
            var hash = PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Capture(path);
            PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Save(path, hash, new[]
            {
                new PreLuna.ThirdPersonHUD.Control.HudConfigWrite("Panel", "Enabled", "false")
            });
            var text = File.ReadAllText(path);
            Check(text.Contains("# header"), "header");
            Check(text.Contains("## comment"), "comment");
            Check(text.Contains("Enabled = false"), "updated");
            Check(text.Contains("Unknown = 42"), "unknown key");
            Check(text.Contains("[Future]") && text.Contains("Value = keep"), "unknown section");
        });

        Case("inserts-missing-key-and-section", () =>
        {
            var path = TempFile();
            File.WriteAllText(path, "[Panel]\nEnabled = true\n");
            var hash = PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Capture(path);
            PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Save(path, hash, new[]
            {
                new PreLuna.ThirdPersonHUD.Control.HudConfigWrite("Panel.Layout", "Scale", "1.1"),
                new PreLuna.ThirdPersonHUD.Control.HudConfigWrite("Team", "Enabled", "false")
            });
            var text = File.ReadAllText(path);
            Check(text.Contains("[Panel.Layout]") && text.Contains("Scale = 1.1"), "new section");
            Check(text.Contains("[Team]") && text.Contains("Enabled = false"), "new key+section");
        });

        Case("external-change-is-a-conflict-and-file-is-unchanged", () =>
        {
            var path = TempFile();
            File.WriteAllText(path, "[Panel]\nEnabled = true\n");
            var hash = PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Capture(path);
            File.AppendAllText(path, "ChangedElsewhere = 1\n");
            var before = File.ReadAllText(path);
            var threw = false;
            try
            {
                PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Save(path, hash, new[]
                {
                    new PreLuna.ThirdPersonHUD.Control.HudConfigWrite("Panel", "Enabled", "false")
                });
            }
            catch (PreLuna.ThirdPersonHUD.Control.HudConfigConflictException) { threw = true; }
            Check(threw, "expected conflict");
            Check(File.ReadAllText(path) == before, "file unchanged");
        });

        Case("missing-file-is-created", () =>
        {
            var path = TempFile();
            var hash = PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Capture(path);
            Check(hash.Length == 0, "missing hash");
            PreLuna.ThirdPersonHUD.Control.HudConfigFileStore.Save(path, hash, new[]
            {
                new PreLuna.ThirdPersonHUD.Control.HudConfigWrite("Panel", "Enabled", "true")
            });
            Check(File.Exists(path), "created");
            Check(File.ReadAllText(path).Contains("[Panel]") && File.ReadAllText(path).Contains("Enabled = true"), "content");
        });

        var output = args.Length > 0 ? args[0] : "HudConfigFileStoreTests.json";
        File.WriteAllText(output, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Exists(result => !result.Passed) ? 1 : 0;
    }
}
