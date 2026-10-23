#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

static class ContentTests
{
    sealed record Result(string Name, bool Passed, string? Error);
    static readonly List<Result> results = new();
    static void Check(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
    static void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-10, $"{a} != {b}");
    sealed class Fixture
    {
        public HudRuntimeContext Current;
        public ThirdPersonDisplayFrame Display;
        public HudContentHeader Header;
        public HudContentPolicy Policy = HudContentPolicy.Default;
        public ObjectIdentity Player => Current.ExpectedBinding.LocalPlayer.Agent;
        public HudValueStamp ValueStamp => new(Player, 98, 9.95, HudContentGeneration.From(Header));
        public Fixture()
        {
            var local = new PlayerIdentity(new(100, 11), 200, 0);
            var binding = new LocalViewBinding(local, new(300, 12), new(400, 13), 500, 0);
            var context = new ContextStamp(Guid.NewGuid(), 1);
            var view = new ThirdPersonViewState(1, context, binding, local, 1, 1,
                ViewSource.LocalThirdPerson, ThirdPersonMode.Exploration, ViewAvailability.Ready);
            var render = new RenderIdentity(binding.MainCamera, RenderSurface.MainDisplay, 7);
            Current = new(true, true, true, context, binding, view, 100, 10, new(render, RenderPhase.MainCommitted));
            var camera = new CameraSnapshot(new(new(0, 1, -2), Quaternion.Identity),
                Matrix4x4.CreateLookAt(new(0, 1, -2), Vector3.Zero, Vector3.UnitY),
                Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, .08f, 250), new(0, 0, 1920, 1080));
            Display = new(view, render, 100, 8, 9.98, true, camera, new(Vector3.Zero, Quaternion.Identity));
            Header = new(1, new(new(view, 3, 98, 9.95), 9.96), 1, 1);
        }
        public HudContentDecision Data() => HudContentRules.EvaluateData(Current, Header, 1, 1, Policy);
        public HudContentDecision Draw() => HudContentRules.EvaluateDisplay(Current, Display, Header, 1, 1, Policy);
        public bool Read(HudValue<HudQuantity> value, out HudValueState state) => HudContentRules.TryReadQuantity(
            Current, Header, value, Player, HudUnit.Fraction, HudFreshness.Live, 1, 1, Policy, out _, out state);
    }
    static void Case(string name, Action<Fixture> action)
    { try { action(new()); results.Add(new(name, true, null)); } catch (Exception error) { results.Add(new(name, false, error.ToString())); } }
    static HudValue<HudQuantity> Fraction(Fixture f, double amount) => HudValue<HudQuantity>.FromValue(new(amount, HudUnit.Fraction), f.ValueStamp);

    static int Main(string[] args)
    {
        Case("valid-zero-is-readable", f => { var q = Fraction(f, 0); Check(f.Read(q, out var s) && s == HudValueState.Available); });
        Case("default-field-is-unknown-not-zero", f => Check(!f.Read(default, out var s) && s == HudValueState.Unknown));
        foreach (var state in new[] { HudValueState.NotEquipped, HudValueState.Unreadable, HudValueState.Unsupported, HudValueState.Invalid })
            Case("unavailable-keeps-distinct-state-" + state, f => Check(!f.Read(HudValue<HudQuantity>.Missing(state, f.ValueStamp), out var s) && s == state));
        Case("missing-factory-cannot-manufacture-available-zero", f => Check(!f.Read(HudValue<HudQuantity>.Missing(HudValueState.Available, f.ValueStamp), out var s) && s == HudValueState.Unknown));
        foreach (double amount in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            Case("invalid-quantity-" + amount, f => Check(!f.Read(Fraction(f, amount), out var s) && s == HudValueState.Invalid));
        Case("fractional-count-is-invalid", f => Check(HudValue<HudQuantity>.FromValue(new(1.5, HudUnit.Items), f.ValueStamp).State == HudValueState.Invalid));
        Case("rounds-cannot-be-read-as-health", f => Check(!f.Read(HudValue<HudQuantity>.FromValue(new(30, HudUnit.Rounds), f.ValueStamp), out var s) && s == HudValueState.Invalid));
        Case("health-uses-actual-modified-maximum", unused => { Check(HudNumberRules.TryHealth(25, 50, out var h)); Near(h.Amount, .5); });
        Case("health-keeps-over-cap-value", unused => { Check(HudNumberRules.TryHealth(60, 50, out var h)); Near(h.Amount, 1.2); });
        Case("health-zero-maximum-is-unreadable", unused => Check(!HudNumberRules.TryHealth(0, 0, out _)));
        Case("health-infinite-maximum-is-unreadable", unused => Check(!HudNumberRules.TryHealth(1, double.PositiveInfinity, out _)));
        Case("supply-adds-clip-in-resource-units", unused => { Check(HudNumberRules.TrySupplyRatio(40, 10, 2, 100, out var r)); Near(r.Amount, .6); });
        Case("supply-over-cap-is-preserved", unused => { Check(HudNumberRules.TrySupplyRatio(110, 10, 2, 100, out var r)); Near(r.Amount, 1.3); });
        Case("supply-invalid-cost-is-not-zero", unused => Check(!HudNumberRules.TrySupplyRatio(10, 0, 0, 100, out _)));
        Case("supply-invalid-capacity-is-not-zero", unused => Check(!HudNumberRules.TrySupplyRatio(10, 0, 1, 0, out _)));
        Case("supply-overflow-is-rejected", unused => Check(!HudNumberRules.TrySupplyRatio(double.MaxValue, 2, double.MaxValue, 1, out _)));
        Case("native-integer-rounds-stay-distinct-from-resource-ratio", unused => { Check(HudNumberRules.TryCount(12, HudUnit.Rounds, out var r)); Check(r.Unit == HudUnit.Rounds && r.Amount == 12); });
        Case("count-rejects-negative-or-fraction-unit", unused => { Check(!HudNumberRules.TryCount(-1, HudUnit.Uses, out _)); Check(!HudNumberRules.TryCount(1, HudUnit.Fraction, out _)); });
        Case("partial-failure-does-not-invalidate-independent-field", f => { Check(f.Read(Fraction(f, .8), out _)); Check(!f.Read(HudValue<HudQuantity>.Missing(HudValueState.Unreadable, f.ValueStamp), out _)); Check(f.Draw().Allowed); });
        Case("old-field-in-fresh-snapshot-stays-expired", f => { var old = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 90, CapturedAtSeconds = 9.7 }); Check(f.Data().Allowed && !f.Read(old, out var s) && s == HudValueState.Expired); Check(f.Read(Fraction(f, .7), out _)); });
        Case("field-age-boundary-is-inclusive", f => { var field = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 90, CapturedAtSeconds = 9.75 }); Check(f.Read(field, out _)); });
        Case("per-class-age-is-adjustable", f => { var old = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 90, CapturedAtSeconds = 9.7 }); f.Policy = f.Policy with { LiveAgeSeconds = .4 }; Check(f.Read(old, out _)); });
        Case("identity-can-live-longer-than-dynamic-value", f => { var text = HudValue<HudText>.FromValue(new("主武器"), f.ValueStamp with { SampleFrame = 90, CapturedAtSeconds = 9.6 }); Check(HudContentRules.TryRead(f.Current, f.Header, text, f.Player, HudFreshness.Identity, 1, 1, f.Policy, out _, out _)); });
        Case("action-progress-expires-faster", f => { var q = HudValue<HudQuantity>.FromValue(new(.4, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 90, CapturedAtSeconds = 9.8 }); Check(HudContentRules.GetState(f.Current, f.Header, q, f.Player, HudFreshness.Action, 1, 1, f.Policy) == HudValueState.Expired); });
        Case("field-cannot-be-sampled-after-envelope", f => { var q = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 99, CapturedAtSeconds = 9.97 }); Check(!f.Read(q, out var s) && s == HudValueState.Invalid); });
        Case("field-cannot-use-future-frame", f => { var q = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), f.ValueStamp with { SampleFrame = 101, CapturedAtSeconds = 9.94 }); Check(!f.Read(q, out var s) && s == HudValueState.Invalid); });
        Case("equipment-instance-mismatch-is-rejected", f => { var q = HudValue<HudQuantity>.FromValue(new(12, HudUnit.Rounds), f.ValueStamp with { Source = new(800, 81) }); Check(!HudContentRules.TryReadQuantity(f.Current, f.Header, q, new(800, 82), HudUnit.Rounds, HudFreshness.Live, 1, 1, f.Policy, out _, out var s) && s == HudValueState.Invalid); });
        Case("unbound-source-is-not-trusted", f => Check(!HudContentRules.TryRead(f.Current, f.Header, Fraction(f, .8), default, HudFreshness.Live, 1, 1, f.Policy, out _, out _)));
        Case("empty-name-is-unavailable", f => Check(HudValue<HudText>.FromValue(new(" "), f.ValueStamp).State == HudValueState.Invalid));
        Case("empty-hands-is-explicit-not-unknown", unused => { Check(new HudSelection(HudEquipmentSlot.EmptyHands).IsValid); Check(!default(HudSelection).IsValid); Check(!new HudEquipmentIdentity(new(1, 1), HudEquipmentSlot.EmptyHands).IsValid); });
        Case("idle-action-does-not-imply-progress", f => { var a = new HudActionSnapshot(HudValue<HudAction>.FromValue(new(HudActionKind.Idle), f.ValueStamp), default); Check(a.Action.State == HudValueState.Available && a.Progress.State == HudValueState.Unknown); });
        Case("progress-and-infection-reject-overflow-but-resource-keeps-it", f =>
        {
            Check(!HudNumberRules.TryProgress(1.1, out _) && !HudNumberRules.TryInfection(1.1, out _));
            var q = Fraction(f, 1.1);
            Check(!HudContentRules.TryReadQuantity(f.Current, f.Header, q, f.Player, HudInformationId.Infection, 1, 1, f.Policy, out _, out var state) && state == HudValueState.Invalid);
            Check(HudContentRules.TryReadQuantity(f.Current, f.Header, q, f.Player, HudInformationId.SupplyRatio, 1, 1, f.Policy, out _, out _));
        });
        Case("non-quantity-field-cannot-be-read-as-quantity", f => Check(!HudContentRules.TryReadQuantity(f.Current, f.Header, Fraction(f, .8), f.Player, HudInformationId.Action, 1, 1, f.Policy, out _, out _)));
        Case("field-cannot-be-rewrapped-after-work-cancellation", f =>
        {
            var old = Fraction(f, .8); f.Header = f.Header with { WorkEpoch = 2 };
            Check(HudContentRules.EvaluateData(f.Current, f.Header, 2, 1, f.Policy).Allowed);
            Check(!HudContentRules.TryRead(f.Current, f.Header, old, f.Player, HudFreshness.Live, 2, 1, f.Policy, out _, out var state) && state == HudValueState.Invalid);
        });
        Case("field-cannot-be-rewrapped-after-content-revision", f =>
        {
            var old = Fraction(f, .8); f.Header = f.Header with { ContentRevision = 2 };
            Check(!HudContentRules.TryRead(f.Current, f.Header, old, f.Player, HudFreshness.Live, 1, 2, f.Policy, out _, out _));
        });
        Case("field-cannot-be-rewrapped-in-a-new-provider-session", f =>
        {
            var old = Fraction(f, .8); var context = new ContextStamp(Guid.NewGuid(), 1);
            var view = f.Current.CurrentView with { Context = context };
            f.Current = f.Current with { CurrentView = view, ExpectedContext = context };
            f.Header = f.Header with { Origin = f.Header.Origin with { Input = f.Header.Origin.Input with { View = view } } };
            Check(f.Data().Allowed && !f.Read(old, out var state) && state == HudValueState.Invalid);
        });
        Case("capture-stamp-copies-provenance-without-renewing-time", f =>
        {
            var stamp = HudValueStamp.AtCapture(new(f.Header.Origin.Input, 1, 1), f.Player);
            Check(stamp == f.ValueStamp);
        });
        Case("default-header-denies", f => { f.Header = default; Check(!f.Data().Allowed); });
        Case("default-decision-denies", unused => Check(!default(HudContentDecision).Allowed));
        Case("schema-version-mismatch", f => { f.Header = f.Header with { SchemaVersion = 2 }; Check(f.Data().Reason == HudContentDenyReason.SchemaMismatch); });
        Case("work-epoch-cancels-old-content", f => Check(HudContentRules.EvaluateData(f.Current, f.Header, 2, 1, f.Policy).Reason == HudContentDenyReason.WorkEpochMismatch));
        Case("content-revision-cancels-old-content", f => Check(HudContentRules.EvaluateData(f.Current, f.Header, 1, 2, f.Policy).Reason == HudContentDenyReason.ContentRevisionMismatch));
        Case("invalid-policy-never-enables-data", f => { f.Policy = default; Check(f.Data().Reason == HudContentDenyReason.InvalidPolicy); });
        Case("field-policy-cannot-exceed-envelope-policy", f => { f.Policy = f.Policy with { LiveAgeSeconds = .6 }; Check(!f.Data().Allowed); });
        Case("valid-third-person-main-display", f => Check(f.Draw().Allowed));
        Case("outside-level-denies-even-with-retained-data", f => { f.Current = f.Current with { InLevel = false }; Check(!f.Draw().Allowed && !f.Read(Fraction(f, .8), out _)); });
        Case("menu-focus-denies", f => { f.Current = f.Current with { FocusAllowed = false }; Check(!f.Draw().Allowed); });
        foreach (var source in Enum.GetValues<ViewSource>().Where(s => s != ViewSource.LocalThirdPerson))
            Case("non-local-third-person-source-denies-" + source, f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Source = source } }; Check(!f.Draw().Allowed); });
        Case("first-person-aim-denies", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Mode = ThirdPersonMode.FirstPersonAim } }; Check(!f.Draw().Allowed); });
        Case("temporary-fallback-denies", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { Availability = ViewAvailability.TemporaryFallback } }; Check(!f.Draw().Allowed); });
        Case("bot-control-denies", f => { f.Current = f.Current with { LocalBodyViewActive = false }; Check(!f.Draw().Allowed); });
        Case("auxiliary-render-preserves-data-but-denies-draw", f => { f.Current = f.Current with { Render = new(new(new(900, 91), RenderSurface.AuxiliaryTexture, 9), RenderPhase.AuxiliaryActive) }; Check(f.Data().Allowed && !f.Draw().Allowed); f.Current = f.Current with { Render = new(f.Display.Render, RenderPhase.MainCommitted) }; Check(f.Draw().Allowed); });
        Case("opening-switch-without-applied-frame-denies", f => { f.Display = f.Display with { ThirdPersonApplied = false }; Check(!f.Draw().Allowed); });
        Case("last-frame-camera-cannot-draw", f => { f.Display = f.Display with { FrameNumber = 99 }; Check(!f.Draw().Allowed); });
        Case("same-frame-wrong-pass-cannot-draw", f => { f.Display = f.Display with { Render = f.Display.Render with { PassId = 8 } }; Check(!f.Draw().Allowed); });
        Case("player-rebuild-invalidates-snapshot", f => { var b = f.Current.ExpectedBinding with { CharacterModel = new(301, 14) }; f.Current = f.Current with { ExpectedBinding = b, CurrentView = f.Current.CurrentView with { Binding = b } }; Check(!f.Data().Allowed); });
        Case("provider-session-reload-invalidates-snapshot", f => { var c = new ContextStamp(Guid.NewGuid(), 1); f.Current = f.Current with { ExpectedContext = c, CurrentView = f.Current.CurrentView with { Context = c } }; Check(!f.Data().Allowed); });
        Case("eligibility-revocation-invalidates-snapshot", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { EligibilityEpoch = 2 } }; Check(!f.Data().Allowed); });
        Case("provider-configuration-invalidates-snapshot", f => { f.Current = f.Current with { CurrentView = f.Current.CurrentView with { ConfigurationRevision = 2 } }; Check(!f.Data().Allowed); });
        Case("snapshot-age-is-not-renewed-by-completion", f => { f.Header = f.Header with { Origin = new(f.Header.Origin.Input with { CapturedAtSeconds = 9.4 }, 9.99) }; Check(!f.Data().Allowed); });
        Case("completion-before-sampling-is-invalid", f => { f.Header = f.Header with { Origin = f.Header.Origin with { CompletedAtSeconds = 9.9 } }; Check(!f.Data().Allowed); });
        Case("future-completion-is-invalid", f => { f.Header = f.Header with { Origin = f.Header.Origin with { CompletedAtSeconds = 10.1 } }; Check(!f.Data().Allowed); });
        Case("new-result-commits-and-old-result-cannot-overwrite", f => { Check(HudContentRules.EvaluateResult(f.Current, f.Display, f.Header, 1, 1, 2, f.Policy).Allowed); Check(!HudContentRules.EvaluateResult(f.Current, f.Display, f.Header, 1, 1, 3, f.Policy).Allowed); Check(f.Draw().Allowed); });
        Case("catalog-is-complete-readonly-and-adjustable-by-copy", unused =>
        {
            var rules = HudInformationCatalog.Defaults;
            Check(rules.Count == Enum.GetValues<HudInformationId>().Length - 1 && rules.All(r => r.IsValid));
            Check(rules.Select(r => r.Id).Distinct().Count() == rules.Count);
            Check(((IList)rules).IsReadOnly);
            var original = HudInformationCatalog.GetDefault(HudInformationId.Health);
            var changed = original with { EnabledByDefault = false, Visibility = HudVisibility.Expanded };
            Check(changed.IsValid && changed != original && HudInformationCatalog.GetDefault(HudInformationId.Health) == original);
            Check(!HudInformationCatalog.GetDefault(HudInformationId.Unknown).IsValid);
        });
        Case("content-is-value-only-and-has-no-native-framework-dependency", unused =>
        {
            var visited = new HashSet<Type>();
            void Visit(Type type)
            {
                if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid)) return;
                Check(type.IsValueType && !type.IsByRefLike && !type.IsPointer, "reference payload: " + type);
                Check(type.IsDefined(typeof(IsReadOnlyAttribute)), "mutable payload: " + type);
                Check(type != typeof(CameraSnapshot) && type != typeof(WorldPose), "old pose in content");
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                { Check(field.IsInitOnly); Visit(field.FieldType); }
            }
            Visit(typeof(HudContentSnapshot));
            Check(typeof(HudContentSnapshot).Assembly.GetReferencedAssemblies().All(a =>
                a.Name!.StartsWith("System.") || a.Name == "PreLuna.ThirdPerson.Contracts"));
        });
        bool passed = results.All(r => r.Passed);
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = passed, Count = results.Count,
            ContentSchemaVersion = HudContentContract.SchemaVersion, SharedApiVersion = ThirdPersonContract.ApiVersion,
            RuntimeCollectorImplemented = false, RuntimeRenderingVerified = false, InstalledToGame = false,
            Rules = HudInformationCatalog.Defaults, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var r in results.Where(r => !r.Passed)) Console.WriteLine(r.Name + ": " + r.Error);
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {results.Count} content cases"); return passed ? 0 : 1;
    }
}
