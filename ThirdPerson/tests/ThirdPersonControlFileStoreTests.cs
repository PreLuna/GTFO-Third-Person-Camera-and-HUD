using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PreLuna.ThirdPerson.Control;

static class ThirdPersonControlFileStoreTests
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
        var directory = Path.Combine(Path.GetTempPath(), "preluna-thirdperson-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "PreLuna.ThirdPerson.cfg");
    }

    static int Main(string[] args)
    {
        Case("writes-only-managed-lines-and-preserves-comments", () =>
        {
            var path = TempFile();
            File.WriteAllText(path, "## header\n[镜头]\n# keep\n距离 = 2.25\n其他 = true\n[维护]\n配置版本 = 9\n");
            var expected = ThirdPersonControlFileStore.Capture(path);
            var saved = ThirdPersonControlFileStore.Save(path, expected, new[]
            {
                new ThirdPersonControlWrite("镜头", "距离", "3.1"),
                new ThirdPersonControlWrite("风格", "左肩", "true")
            });
            var text = File.ReadAllText(path);
            Check(text.Contains("## header"), "comment");
            Check(text.Contains("# keep"), "inline comment");
            Check(text.Contains("距离 = 3.1"), "updated");
            Check(text.Contains("其他 = true"), "unknown key");
            Check(text.Contains("配置版本 = 9"), "other section");
            Check(text.Contains("[风格]"), "new section");
            Check(saved != expected, "hash changed");
        });

        Case("rejects-external-change", () =>
        {
            var path = TempFile();
            File.WriteAllText(path, "[镜头]\n距离 = 2.25\n");
            var expected = ThirdPersonControlFileStore.Capture(path);
            File.AppendAllText(path, "外部 = 1\n");
            var rejected = false;
            try { ThirdPersonControlFileStore.Save(path, expected, new[] { new ThirdPersonControlWrite("镜头", "距离", "3") }); }
            catch (ThirdPersonControlConflictException) { rejected = true; }
            Check(rejected, "conflict");
            Check(File.ReadAllText(path).Contains("距离 = 2.25"), "unchanged");
        });

        Case("creates-missing-file-and-section", () =>
        {
            var path = TempFile();
            ThirdPersonControlFileStore.Save(path, "", new[] { new ThirdPersonControlWrite("镜头", "距离", "2.5") });
            var text = File.ReadAllText(path);
            Check(text.Contains("[镜头]") && text.Contains("距离 = 2.5"), "created");
        });

        var output = args.Length > 0 ? args[0] : "ThirdPersonControlFileStoreTests.json";
        File.WriteAllText(output, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Exists(result => !result.Passed) ? 1 : 0;
    }
}
