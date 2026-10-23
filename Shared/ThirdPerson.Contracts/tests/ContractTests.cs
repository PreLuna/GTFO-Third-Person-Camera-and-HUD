#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;

internal static class ContractTests
{
    sealed class Fixture
    {
        public HudRuntimeContext Current;
        public ThirdPersonDisplayFrame Display;
        public HudResultStamp Result;
        public Fixture()
        {
            var local = new PlayerIdentity(new(100, 11), 200, 0);
            var binding = new LocalViewBinding(local, new(300, 12), new(400, 13), 500, 0);
            var context = new ContextStamp(Guid.NewGuid(), 1);
            var view = new ThirdPersonViewState(1, context, binding, local, 1, 1,
                ViewSource.LocalThirdPerson, ThirdPersonMode.Exploration, ViewAvailability.Ready);
            var render = new RenderIdentity(binding.MainCamera, RenderSurface.MainDisplay, 7);
            Current = new(true, true, true, context, binding, view, 100, 10,
                new(render, RenderPhase.MainActive));
            var camera = new CameraSnapshot(new(new(0, 1, -2), Quaternion.Identity),
                Matrix4x4.CreateLookAt(new(0, 1, -2), new(0, 1, 0), Vector3.UnitY),
                Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, .08f, 250),
                new(0, 0, 1920, 1080));
            Display = new(view, render, 100, 8, 9.98, true, camera, new(Vector3.Zero, Quaternion.Identity));
            Result = new(new(view, 3, 98, 9.75), 9.9);
        }
        public HudDecision DisplayDecision(HudDisplayUsage usage = HudDisplayUsage.WorldRender) =>
            HudEligibility.EvaluateDisplay(Current, Display, usage, HudValidityPolicy.Default);
        public HudDecision DataDecision() => HudEligibility.EvaluateData(Current, Result, HudValidityPolicy.Default);
        public HudDecision ResultDecision(long last = 0) => HudEligibility.EvaluateResult(Current,
            Display, HudDisplayUsage.WorldRender, Result, last, HudValidityPolicy.Default);
    }

    sealed record TestResult(string Name, bool Passed, string? Error);
    static readonly List<TestResult> results = new();
    static void Check(bool condition, string message = "assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    static void Expect(HudDecision decision, HudDenyReason reason) =>
        Check(decision.Reason == reason && decision.Allowed == (reason == HudDenyReason.None),
            $"Expected {reason}, got {decision.Reason}");
    static void Case(string name, Action<Fixture> action)
    {
        try { action(new Fixture()); results.Add(new(name, true, null)); }
        catch (Exception error) { results.Add(new(name, false, error.Message)); }
    }

    static int Main(string[] args)
    {
        Case("local-third-person-main-render-allowed", f => Expect(f.ResultDecision(), HudDenyReason.None));
        Case("default-decision-denies", _ => Check(!default(HudDecision).Allowed));
        Case("default-context-denies", _ => Check(!HudEligibility.EvaluateCurrent(default).Allowed));
        Case("default-display-denies", f => { f.Display = default; Check(!f.DisplayDecision().Allowed); });
        Case("default-result-denies", f => { f.Result = default; Check(!f.ResultDecision().Allowed); });
        Case("provider-api-mismatch", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { ApiVersion = 2 } }; Expect(f.DisplayDecision(), HudDenyReason.ApiMismatch); });
        Case("snapshot-api-mismatch", f => { f.Display = f.Display with { View = f.Display.View with { ApiVersion = 2 } }; Expect(f.DisplayDecision(), HudDenyReason.ApiMismatch); });
        Case("outside-match-denied-immediately", f => { f.Current = f.Current with { InLevel = false }; Expect(f.ResultDecision(), HudDenyReason.OutOfLevel); });
        Case("menu-focus-denied-immediately", f => { f.Current = f.Current with { FocusAllowed = false }; Expect(f.ResultDecision(), HudDenyReason.FocusBlocked); });
        Case("bot-view-replaces-main-view-denied", f => { f.Current = f.Current with { LocalBodyViewActive = false }; Expect(f.ResultDecision(), HudDenyReason.NotLocalBodyView); });
        Case("preview-window-does-not-disable-own-main-view", f =>
        {
            var preview = new RenderIdentity(new(900, 91), RenderSurface.AuxiliaryTexture, 8);
            f.Current = f.Current with { Render = new(preview, RenderPhase.AuxiliaryActive) };
            Expect(f.DisplayDecision(), HudDenyReason.RenderNotMain);
            f.Current = f.Current with { Render = new(f.Display.Render, RenderPhase.MainActive) };
            Expect(f.DisplayDecision(), HudDenyReason.None);
        });
        foreach (var source in Enum.GetValues<ViewSource>().Where(x => x != ViewSource.LocalThirdPerson))
            Case("source-denied-" + source, f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Source = source } }; Expect(f.DisplayDecision(), HudDenyReason.SourceNotLocalThirdPerson); });
        foreach (var availability in Enum.GetValues<ViewAvailability>().Where(x => x != ViewAvailability.Ready))
            Case("availability-denied-" + availability, f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Availability = availability } }; Expect(f.ResultDecision(), HudDenyReason.ViewUnavailable); });
        Case("first-person-aim-denied", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Mode = ThirdPersonMode.FirstPersonAim } }; Expect(f.DisplayDecision(), HudDenyReason.ModeNotThirdPerson); });
        Case("unknown-mode-denied", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Mode = ThirdPersonMode.Unknown } }; Expect(f.DisplayDecision(), HudDenyReason.ModeNotThirdPerson); });
        Case("shoulder-aim-allowed", f =>
        {
            var view = f.Current.CurrentView with { Mode = ThirdPersonMode.ShoulderAim };
            f.Current = f.Current with { CurrentView = view }; f.Display = f.Display with { View = view };
            Expect(f.ResultDecision(), HudDenyReason.None);
        });
        Case("display-mode-must-match-current-view", f => { f.Display = f.Display with { View = f.Display.View with { Mode = ThirdPersonMode.ShoulderAim } }; Expect(f.DisplayDecision(), HudDenyReason.ViewModeMismatch); });
        Case("same-slot-different-player-denied", f =>
        {
            var other = f.Current.ExpectedBinding.LocalPlayer with { Agent = new(901, 92) };
            f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Subject = other } };
            Expect(f.DisplayDecision(), HudDenyReason.SubjectMismatch);
        });
        Case("same-agent-different-owner-denied", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Subject = f.Current.CurrentView.Subject with { OwnerHandle = 901 } } }; Expect(f.DisplayDecision(), HudDenyReason.SubjectMismatch); });
        Case("same-pointer-reused-instance-denied", f => { var p = f.Display.View.Subject; f.Display = f.Display with { View = f.Display.View with { Subject = p with { Agent = p.Agent with { InstanceId = 91 } } } }; Expect(f.DisplayDecision(), HudDenyReason.SubjectMismatch); });
        Case("negative-unity-instance-id-is-valid", _ => Check(new ObjectIdentity(1, -9).IsValid));
        foreach (var part in new[] { "player", "owner", "slot", "model", "camera", "floor", "dimension" })
            Case("old-binding-denied-after-" + part + "-change", f =>
            {
                var b = f.Current.ExpectedBinding;
                b = part switch
                {
                    "player" => b with { LocalPlayer = b.LocalPlayer with { Agent = new(901, 91) } },
                    "owner" => b with { LocalPlayer = b.LocalPlayer with { OwnerHandle = 901 } },
                    "slot" => b with { LocalPlayer = b.LocalPlayer with { Slot = 4 } },
                    "model" => b with { CharacterModel = new(901, 91) },
                    "camera" => b with { MainCamera = new(901, 91) },
                    "floor" => b with { FloorHandle = 901 },
                    _ => b with { DimensionIndex = 1 }
                };
                var context = f.Current.ExpectedContext with { Epoch = 2 };
                var view = f.Current.CurrentView with { Context = context, Binding = b, Subject = b.LocalPlayer };
                var render = f.Display.Render with { Camera = b.MainCamera, PassId = 8 };
                f.Current = f.Current with { ExpectedContext = context, ExpectedBinding = b,
                    CurrentView = view, Render = new(render, RenderPhase.MainActive) };
                Expect(HudEligibility.EvaluateCurrent(f.Current), HudDenyReason.None);
                Expect(f.DisplayDecision(), HudDenyReason.ContextMismatch);
                f.Display = f.Display with { View = view, Render = render };
                Expect(f.DisplayDecision(), HudDenyReason.None);
                Expect(f.ResultDecision(), HudDenyReason.ContextMismatch);
                f.Result = f.Result with { Input = f.Result.Input with { View = view } };
                Expect(f.ResultDecision(), HudDenyReason.None);
            });
        Case("context-epoch-change-denies-old-snapshot", f => { f.Current = f.Current with { ExpectedContext = f.Current.ExpectedContext with { Epoch = 2 } }; Expect(f.DisplayDecision(), HudDenyReason.ContextMismatch); });
        Case("provider-reload-session-denies-old-snapshot", f => { f.Current = f.Current with { ExpectedContext = new(Guid.NewGuid(), 1) }; Expect(f.DisplayDecision(), HudDenyReason.ContextMismatch); });
        Case("missing-model-denied", f => { f.Current = f.Current with { ExpectedBinding = f.Current.ExpectedBinding with { CharacterModel = default } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidIdentity); });
        Case("zero-context-epoch-denied", f => { f.Current = f.Current with { ExpectedContext = f.Current.ExpectedContext with { Epoch = 0 } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidIdentity); });
        Case("zero-eligibility-epoch-denied", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { EligibilityEpoch = 0 } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidIdentity); });
        Case("zero-configuration-revision-denied", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { ConfigurationRevision = 0 } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidIdentity); });
        Case("resume-does-not-revive-old-camera-frame", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { EligibilityEpoch = 2 } }; Expect(f.DisplayDecision(), HudDenyReason.EligibilityEpochMismatch); });
        Case("resume-does-not-revive-old-worker-result", f =>
        {
            var view = f.Current.CurrentView with { EligibilityEpoch = 2 };
            f.Current = f.Current with { CurrentView = view }; f.Display = f.Display with { View = view };
            Expect(f.ResultDecision(), HudDenyReason.EligibilityEpochMismatch);
            f.Result = f.Result with { Input = f.Result.Input with { View = view } };
            Expect(f.ResultDecision(), HudDenyReason.None);
        });
        Case("settings-change-denies-old-result", f =>
        {
            var view = f.Current.CurrentView with { ConfigurationRevision = 2 };
            f.Current = f.Current with { CurrentView = view }; f.Display = f.Display with { View = view };
            Expect(f.ResultDecision(), HudDenyReason.ConfigurationMismatch);
        });
        Case("display-previous-frame-denied-despite-young-age", f => { f.Display = f.Display with { FrameNumber = 99 }; Expect(f.DisplayDecision(), HudDenyReason.DisplayFrameMismatch); });
        Case("display-future-frame-denied", f => { f.Display = f.Display with { FrameNumber = 101 }; Expect(f.DisplayDecision(), HudDenyReason.DisplayFrameMismatch); });
        Case("same-frame-repeated-render-requires-same-pass", f => { f.Current = f.Current with { Render = f.Current.Render with { Identity = f.Display.Render with { PassId = 8 } } }; Expect(f.DisplayDecision(), HudDenyReason.RenderPassMismatch); });
        Case("copy-of-main-settings-is-still-auxiliary-camera", f => { f.Display = f.Display with { Render = f.Display.Render with { Camera = new(900, 91) } }; Expect(f.DisplayDecision(), HudDenyReason.RenderNotMain); });
        Case("main-camera-texture-pass-denied", f => { f.Display = f.Display with { Render = f.Display.Render with { Surface = RenderSurface.AuxiliaryTexture } }; Expect(f.DisplayDecision(), HudDenyReason.RenderNotMain); });
        Case("missing-render-pass-denied", f => { f.Display = f.Display with { Render = f.Display.Render with { PassId = 0 } }; Expect(f.DisplayDecision(), HudDenyReason.RenderNotMain); });
        Case("auxiliary-phase-denied-even-with-main-identity", f => { f.Current = f.Current with { Render = f.Current.Render with { Phase = RenderPhase.AuxiliaryActive } }; Expect(f.DisplayDecision(), HudDenyReason.RenderPhaseMismatch); });
        Case("screen-layer-cannot-consume-uncommitted-frame", f => Expect(f.DisplayDecision(HudDisplayUsage.ScreenComposition), HudDenyReason.RenderPhaseMismatch));
        Case("restoring-camera-after-success-does-not-expire-screen-snapshot", f =>
        {
            f.Current = f.Current with { Render = f.Current.Render with { Phase = RenderPhase.MainCommitted } };
            Expect(f.DisplayDecision(HudDisplayUsage.ScreenComposition), HudDenyReason.None);
            Expect(f.DisplayDecision(), HudDenyReason.RenderPhaseMismatch);
        });
        Case("failed-main-render-has-no-screen-commit", f => { f.Current = f.Current with { Render = f.Current.Render with { Phase = RenderPhase.Unknown } }; Expect(f.DisplayDecision(HudDisplayUsage.ScreenComposition), HudDenyReason.RenderPhaseMismatch); });
        Case("unknown-render-usage-denied", f => Expect(f.DisplayDecision(HudDisplayUsage.Unknown), HudDenyReason.RenderPhaseMismatch));
        Case("third-person-setting-alone-is-insufficient", f => { f.Display = f.Display with { ThirdPersonApplied = false }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("display-expired-during-stalled-frame", f => { f.Display = f.Display with { CapturedAtSeconds = 9.8 }; Expect(f.DisplayDecision(), HudDenyReason.SnapshotExpired); });
        Case("display-timestamp-from-future-denied", f => { f.Display = f.Display with { CapturedAtSeconds = 10.1 }; Expect(f.DisplayDecision(), HudDenyReason.SnapshotFromFuture); });
        Case("invalid-display-sequence-denied", f => { f.Display = f.Display with { Sequence = 0 }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSequence); });
        foreach (double time in new[] { double.NaN, double.PositiveInfinity, -1 })
            Case("invalid-clock-" + time, f => { f.Current = f.Current with { NowSeconds = time }; Expect(f.DisplayDecision(), HudDenyReason.InvalidClock); });
        Case("invalid-clock-frame-denied", f => { f.Current = f.Current with { FrameNumber = -1 }; Expect(f.DisplayDecision(), HudDenyReason.InvalidClock); });
        Case("invalid-snapshot-clock-denied", f => { f.Display = f.Display with { CapturedAtSeconds = double.NaN }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("invalid-policy-denied", f => Expect(HudEligibility.EvaluateDisplay(f.Current, f.Display, HudDisplayUsage.WorldRender, new(double.NaN, .5)), HudDenyReason.InvalidPolicy));
        Case("unbounded-data-age-denied", f => Expect(HudEligibility.EvaluateData(f.Current, f.Result, new(.1, double.PositiveInfinity)), HudDenyReason.InvalidPolicy));
        Case("zero-validity-window-denied", f => Expect(HudEligibility.EvaluateData(f.Current, f.Result, new(.1, 0)), HudDenyReason.InvalidPolicy));
        Case("zero-size-viewport-denied", f => { f.Display = f.Display with { Camera = f.Display.Camera with { Viewport = new(0, 0, 0, 1080) } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("singular-view-matrix-denied", f => { f.Display = f.Display with { Camera = f.Display.Camera with { WorldToView = default } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("nan-projection-denied", f => { var m = f.Display.Camera.Projection; m.M11 = float.NaN; f.Display = f.Display with { Camera = f.Display.Camera with { Projection = m } }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("invalid-body-pose-denied", f => { f.Display = f.Display with { BodyPose = new(Vector3.Zero, default) }; Expect(f.DisplayDecision(), HudDenyReason.InvalidSnapshot); });
        Case("result-can-span-frames-with-current-camera", f => Expect(f.ResultDecision(), HudDenyReason.None));
        Case("data-valid-at-exact-half-second-boundary", f => { f.Result = f.Result with { Input = f.Result.Input with { CapturedAtSeconds = 9.5 } }; Expect(f.DataDecision(), HudDenyReason.None); });
        Case("data-over-half-second-denied", f => { f.Result = f.Result with { Input = f.Result.Input with { CapturedAtSeconds = 9.49 } }; Expect(f.DataDecision(), HudDenyReason.ResultExpired); });
        Case("slow-completion-does-not-renew-sample-age", f => { f.Result = new(f.Result.Input with { CapturedAtSeconds = 9.4 }, 9.99); Expect(f.DataDecision(), HudDenyReason.ResultExpired); });
        Case("future-result-completion-denied", f => { f.Result = f.Result with { CompletedAtSeconds = 10.1 }; Expect(f.DataDecision(), HudDenyReason.ResultFromFuture); });
        Case("future-sample-frame-denied", f => { f.Result = f.Result with { Input = f.Result.Input with { SampleFrame = 101 } }; Expect(f.DataDecision(), HudDenyReason.ResultFromFuture); });
        Case("completion-before-sampling-denied", f => { f.Result = f.Result with { CompletedAtSeconds = 9.5 }; Expect(f.DataDecision(), HudDenyReason.InvalidResult); });
        Case("result-invalid-sequence-denied", f => { f.Result = f.Result with { Input = f.Result.Input with { Sequence = 0 } }; Expect(f.DataDecision(), HudDenyReason.InvalidSequence); });
        Case("out-of-order-result-cannot-overwrite-newer-data", f => Expect(f.ResultDecision(4), HudDenyReason.ResultOutOfOrder));
        Case("duplicate-result-not-applied-twice", f => Expect(f.ResultDecision(3), HudDenyReason.ResultOutOfOrder));
        Case("retained-data-can-be-reused-without-reapplying", f => { Expect(f.ResultDecision(3), HudDenyReason.ResultOutOfOrder); Expect(f.DataDecision(), HudDenyReason.None); });
        Case("negative-last-sequence-denied", f => Expect(f.ResultDecision(-1), HudDenyReason.InvalidSequence));
        Case("later-sequence-can-skip-superseded-work", f => Expect(f.ResultDecision(1), HudDenyReason.None));
        Case("result-from-bot-view-denied", f => { f.Result = f.Result with { Input = f.Result.Input with { View = f.Result.Input.View with { Source = ViewSource.BotControl } } }; Expect(f.DataDecision(), HudDenyReason.SourceNotLocalThirdPerson); });
        Case("data-result-never-authorizes-auxiliary-render", f => { f.Current = f.Current with { Render = new(new(new(900, 91), RenderSurface.AuxiliaryTexture, 8), RenderPhase.AuxiliaryActive) }; Expect(f.DataDecision(), HudDenyReason.None); Expect(f.ResultDecision(), HudDenyReason.RenderNotMain); });
        Case("no-unity-il2cpp-or-plugin-runtime-dependencies", _ =>
        {
            var names = typeof(ThirdPersonContract).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
            Check(!names.Any(n => n.Contains("Unity") || n.Contains("Il2Cpp") || n.Contains("BepInEx")));
        });
        Case("snapshot-fields-are-immutable-value-data", _ =>
        {
            foreach (var type in typeof(ThirdPersonContract).Assembly.GetExportedTypes().Where(t => t.IsValueType && !t.IsEnum))
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    Check(field.IsInitOnly && field.FieldType.IsValueType, type.Name + "." + field.Name);
        });
        Case("unity-column-view-matrix-is-transposed-at-boundary", _ =>
        {
            var unityElements = new Matrix4x4(1, 0, 0, -10, 0, 1, 0, -20, 0, 0, 1, -30, 0, 0, 0, 1);
            var view = Vector4.Transform(new Vector4(11, 22, 33, 1), Matrix4x4.Transpose(unityElements));
            Check(view == new Vector4(1, 2, 3, 1));
        });
        Case("projection-convention-preserves-known-pixel-position", _ =>
        {
            var unityElements = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1.002f, -.2002f, 0, 0, -1, 0);
            var clip = Vector4.Transform(new Vector4(.5f, .5f, -2, 1), Matrix4x4.Transpose(unityElements));
            float x = (clip.X / clip.W + 1) * .5f * 1920;
            float y = (clip.Y / clip.W + 1) * .5f * 1080;
            Check(MathF.Abs(x - 1200) < .001f && MathF.Abs(y - 675) < .001f);
        });
        Case("negative-sample-time-denied", f => { f.Result = f.Result with { Input = f.Result.Input with { CapturedAtSeconds = -1 } }; Expect(f.DataDecision(), HudDenyReason.InvalidResult); });
        Case("nan-result-completion-denied", f => { f.Result = f.Result with { CompletedAtSeconds = double.NaN }; Expect(f.DataDecision(), HudDenyReason.InvalidResult); });
        Case("old-result-cannot-cross-new-provider-session", f =>
        {
            var context = new ContextStamp(Guid.NewGuid(), 1);
            var view = f.Current.CurrentView with { Context = context };
            f.Current = f.Current with { ExpectedContext = context, CurrentView = view };
            f.Display = f.Display with { View = view };
            Expect(f.ResultDecision(), HudDenyReason.ContextMismatch);
        });
        bool passed = results.All(r => r.Passed);
        var report = new { SchemaVersion = 1, ApiVersion = ThirdPersonContract.ApiVersion,
            Passed = passed, Count = results.Count, Results = results,
            RuntimeWired = false, RuntimeVerified = false, InstalledToGame = false };
        File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var result in results.Where(r => !r.Passed)) Console.WriteLine("FAIL " + result.Name + ": " + result.Error);
        Console.WriteLine($"HUD contract checks: {results.Count}; passed={passed}");
        return passed ? 0 : 1;
    }
}
