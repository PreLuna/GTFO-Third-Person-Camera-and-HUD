#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Runtime;

static class CollectionPipelineTests
{
    sealed record Result(string Name, bool Passed, string? Error);
    static readonly List<Result> results = new();
    static void Check(bool b, string message = "assertion failed") { if (!b) throw new Exception(message); }
    sealed class Clock { public double Now = 10; }
    sealed class Source : IHudContentSource, IHudContentRefreshSource, IHudContentTransientSource
    {
        readonly Clock clock;
        public long ChangeVersion { get; private set; } = 1;
        public int Calls, Thread;
        public int TransientClears;
        public void ClearTransient()=>TransientClears++;
        public bool Fail, Throw, Corrupt;
        public double CostMilliseconds;
        public Action? DuringCapture;
        public Source(Clock clock) { this.clock = clock; }
        public void Dirty(int count = 1) => ChangeVersion += count;
        public bool TryCapture(in HudContentCaptureRequest request, out HudContentSnapshot snapshot)
        {
            Calls++; Thread = Environment.CurrentManagedThreadId;
            clock.Now += CostMilliseconds / 1000;
            snapshot = default;
            if (Throw) throw new Exception("source failure");
            if (Fail) return false;
            var stamp = HudValueStamp.AtCapture(request, request.Origin.View.Binding.LocalPlayer.Agent);
            var personal = default(HudPersonalSnapshot) with { Health = HudValue<HudQuantity>.FromValue(new(.8, HudUnit.Fraction), stamp) };
            snapshot = new(new(1, new(request.Origin, clock.Now), Corrupt ? request.WorkEpoch + 1 : request.WorkEpoch, request.ContentRevision), personal);
            DuringCapture?.Invoke(); return true;
        }
    }
    sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly Source Source;
        public readonly HudMainThreadExecutor Executor;
        public readonly HudContentPipeline Pipeline;
        public HudRuntimeContext Current;
        public HudWorkOptions Work = new(5, 3, .05, .25);
        public HudCollectionOptions Options = HudCollectionOptions.Default;
        public Fixture()
        {
            var p = new PlayerIdentity(new(100, 1), 200, 0);
            var b = new LocalViewBinding(p, new(300, 2), new(400, 3), 500, 0);
            var view = new ThirdPersonViewState(1, new(Guid.NewGuid(), 1), b, p, 1, 1, ViewSource.LocalThirdPerson, ThirdPersonMode.Exploration, ViewAvailability.Ready);
            Current = new(true, true, true, view.Context, b, view, 100, 10, default);
            Source = new(Clock); Executor = new(() => Clock.Now); Pipeline = new(Executor, Source);
        }
        public void Prepare(long epoch = 1, long revision = 1)
        { Current = Current with { NowSeconds = Clock.Now }; Executor.BeginFrame(Current.FrameNumber, Work); Pipeline.Configure(Options, Work); Pipeline.Prepare(Current, epoch, revision); }
        public void Next(double seconds = .01) { Clock.Now += seconds; Current = Current with { FrameNumber = Current.FrameNumber + 1, NowSeconds = Clock.Now }; }
        public bool Read(long epoch = 1) => Pipeline.TryGet(Current, epoch, out _);
        public HudCollectionStatus Status { get { Pipeline.TryGetStatus(out var s); return s; } }
    }
    static void Case(string name, Action<Fixture> action)
    { try { action(new()); results.Add(new(name, true, null)); } catch (Exception error) { results.Add(new(name, false, error.ToString())); } }

    static int Main(string[] args)
    {
        Case("initial-capture-on-main-thread", f => { f.Prepare(); Check(f.Read() && f.Source.Thread == Environment.CurrentManagedThreadId && f.Source.Calls == 1); });
        Case("cancel-work-version-and-disable-clear-source-events", f => {f.Prepare();int n=f.Source.TransientClears;f.Pipeline.Cancel();Check(f.Source.TransientClears>n);f.Next();f.Prepare();n=f.Source.TransientClears;f.Next();f.Prepare(2);Check(f.Source.TransientClears>n);n=f.Source.TransientClears;f.Options=f.Options with{Enabled=false};f.Next();f.Prepare(2);Check(f.Source.TransientClears>n);});
        Case("interval-refresh-does-not-accumulate-tasks", f => { f.Prepare(); for (int i = 0; i < 4; i++) { f.Next(); f.Prepare(); } Check(f.Source.Calls == 1); f.Next(); f.Prepare(); Check(f.Source.Calls == 2); });
        Case("slow-config-still-refreshes-short-lived-interactions", f => {f.Work=f.Work with{RefreshIntervalSeconds=.5,MaximumDataAgeSeconds=.5};f.Prepare();f.Next(.11);f.Prepare();Check(f.Source.Calls==2&&f.Read());});
        Case("large-time-gap-produces-one-current-sample", f => { f.Prepare(); f.Next(100); f.Prepare(); Check(f.Source.Calls == 2); });
        Case("dirty-event-invalidates-cache-immediately", f => { f.Prepare(); f.Source.Dirty(); Check(!f.Read()); f.Next(); f.Prepare(); Check(f.Read() && f.Source.Calls == 2); });
        Case("five-hundred-events-merge-into-one-capture", f => { f.Prepare(); f.Next(); f.Source.Dirty(500); f.Prepare(); f.Prepare(); Check(f.Source.Calls == 2 && !f.Status.RefreshPending); });
        Case("same-frame-second-event-waits-for-next-frame", f => { f.Prepare(); f.Source.Dirty(); f.Prepare(); Check(f.Source.Calls == 1 && !f.Read() && f.Status.RefreshPending); f.Next(); f.Prepare(); Check(f.Source.Calls == 2 && f.Read()); });
        Case("event-during-capture-is-not-lost", f => { f.Source.DuringCapture = () => f.Source.Dirty(); f.Prepare(); Check(!f.Read() && f.Status.RefreshPending); f.Source.DuringCapture = null; f.Next(); f.Prepare(); Check(f.Read()); });
        Case("zero-budget-defers-collection-without-old-data", f => { f.Prepare(); f.Source.Dirty(); f.Next(); f.Work = f.Work with { FrameBudgetMilliseconds = 0 }; f.Prepare(); Check(f.Source.Calls == 1 && !f.Read()); f.Next(); f.Work = f.Work with { FrameBudgetMilliseconds = 5 }; f.Prepare(); Check(f.Read()); });
        Case("atomic-collection-overrun-is-accounted", f => { f.Work = f.Work with { FrameBudgetMilliseconds = .5 }; f.Source.CostMilliseconds = 2; f.Prepare(); f.Executor.TryGetSnapshot(out var s); Check(s.BudgetOverrunFrames == 1 && s.Collection.Count == 1 && s.FrameSpentMilliseconds > 1.9); });
        Case("failure-clears-old-cache-and-retries-after-cooldown", f => { f.Prepare(); f.Source.Fail = true; f.Source.Dirty(); f.Next(); f.Prepare(); Check(!f.Read() && f.Status.Failed == 1); f.Source.Fail = false; f.Next(.1); f.Source.Dirty(); f.Prepare(); Check(f.Source.Calls == 2); f.Next(.2); f.Prepare(); Check(f.Read()); });
        Case("source-exception-is-contained", f => { f.Source.Throw = true; f.Prepare(); Check(f.Status.Failed == 1 && !f.Read()); });
        Case("wrong-provenance-is-rejected", f => { f.Source.Corrupt = true; f.Prepare(); Check(f.Status.Rejected == 1 && !f.Read()); });
        Case("stale-envelope-is-never-returned", f => { f.Prepare(); f.Clock.Now += .6; Check(!f.Read()); });
        Case("field-expiration-is-independent-of-envelope", f => { f.Prepare(); f.Clock.Now += .3; Check(f.Pipeline.TryGet(f.Current, 1, out var s)); Check(HudContentRules.GetState(f.Current with { NowSeconds = f.Clock.Now }, s.Header, s.Personal.Health, f.Current.ExpectedBinding.LocalPlayer.Agent, HudFreshness.Live, 1, 1, f.Status.Policy) == HudValueState.Expired); });
        Case("cancel-discards-data-without-resetting-sequence", f => { f.Prepare(); long n = f.Status.LastSequence; f.Pipeline.Cancel(); Check(!f.Read()); f.Next(); f.Prepare(); Check(f.Status.LastSequence > n && f.Read()); });
        Case("context-rebuild-recovers-with-new-generation", f => { f.Prepare(); var context = f.Current.ExpectedContext with { Epoch = 2 }; f.Current = f.Current with { ExpectedContext = context, CurrentView = f.Current.CurrentView with { Context = context } }; Check(!f.Read()); f.Next(); f.Prepare(2); Check(f.Read(2)); });
        Case("content-revision-discards-old-cache", f => { f.Prepare(); f.Next(); f.Prepare(1, 2); Check(f.Pipeline.TryGet(f.Current, 1, out var s) && s.Header.ContentRevision == 2); });
        Case("disable-clears-cache-and-does-not-read-source", f => { f.Prepare(); f.Options = f.Options with { Enabled = false }; f.Next(); f.Prepare(); Check(!f.Read() && f.Source.Calls == 1 && !f.Status.Enabled); });
        Case("age-configuration-is-applied-without-hardcoding", f => { f.Prepare(); f.Next(); f.Work = f.Work with { MaximumDataAgeSeconds = .1 }; f.Prepare(); Check(f.Status.Policy.LiveAgeSeconds == .1); });
        Case("off-main-prepare-and-read-are-rejected", f => { f.Prepare(); Task.Run(() => { f.Pipeline.Prepare(f.Current, 1, 1); Check(!f.Pipeline.TryGet(f.Current, 1, out _)); }).GetAwaiter().GetResult(); Check(f.Source.Calls == 1); });
        Case("outside-level-and-control-clear-cache", f => { f.Prepare(); f.Current = f.Current with { LocalBodyViewActive = false }; Check(!f.Read()); f.Next(); f.Prepare(); Check(!f.Status.HasContent); });
        RuntimeCase("runtime-content-is-wired-and-pauses-with-view", (runtime, source, provider, next, live) =>
        {
            Check(runtime.TryGetContent(out var s) && s.Personal.Health.State == HudValueState.Available);
            source.Dirty(100); Check(!runtime.TryGetContent(out _)); next(); Check(runtime.TryGetContent(out _));
            provider.AuxiliaryBegin(new(900, 9)); Check(runtime.TryGetContent(out _)); provider.AuxiliaryEnd(new(900, 9));
            live(ViewSource.BotControl); next(); Check(!runtime.TryGetContent(out _));
            live(ViewSource.LocalThirdPerson); for (int i = 0; i < 5; i++) next(); Check(runtime.TryGetContent(out _));
            runtime.Stop(); Check(!runtime.TryGetContent(out _));
        });
        RuntimeCase("one-preparation-step-with-continuous-interval-does-not-starve-anchor", (runtime, source, provider, next, live) =>
        {
            runtime.TryGetStatus(out var status); Check(status.Presentations > 0 && runtime.TryGetContent(out _));
        }, 1, 0);
        bool passed = results.All(r => r.Passed);
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = passed, Count = results.Count, NativeGameExecuted = false, RuntimeRenderingVerified = false, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var r in results.Where(r => !r.Passed)) Console.WriteLine(r.Name + ": " + r.Error);
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {results.Count} collection pipeline cases"); return passed ? 0 : 1;
    }
    static void RuntimeCase(string name, Action<HudRuntime, Source, ThirdPersonViewProvider, Action, Action<ViewSource>> action, int maxSteps = 4, double refreshInterval = .05)
    {
        try
        {
            var clock = new Clock(); var source = new Source(clock);
            var p = new PlayerIdentity(new(100, 1), 200, 0); var binding = new LocalViewBinding(p, new(300, 2), new(400, 3), 500, 0);
            var live = new BridgeObservation(binding, p, true, true, true, ViewSource.LocalThirdPerson, ThirdPersonMode.Exploration, ViewAvailability.Ready, 1, 100, clock.Now);
            using var provider = new ThirdPersonViewProvider(() => live);
            ThirdPersonApi.Provider = provider; Plugin.Instance = new();
            Plugin.Instance.Settings.Options = HudLifecycleOptions.Default with { ActivationDelaySeconds = 0 };
            Plugin.Instance.Settings.WorkOptions = new(5, maxSteps, refreshInterval, .25);
            Plugin.Instance.Settings.ExecutionOptions = HudExecutionOptions.Default;
            var runtime = new HudRuntime(IntPtr.Zero); runtime.Initialize(() => clock.Now, contentSource: source, legacyMarker:true);
            var camera = new CameraSnapshot(new(new(0, 1, 3), Quaternion.Identity), Matrix4x4.CreateTranslation(0, -1, -3),
                Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9, .08f, 250), new(0, 0, 1920, 1080));
            void Next()
            {
                clock.Now += .01; live = live with { NowSeconds = clock.Now, FrameNumber = live.FrameNumber + 1 };
                UnityEngine.Time.frameCount = live.FrameNumber; ThirdPersonApi.MonotonicSeconds = clock.Now;
                runtime.Update(); provider.BeginMainAttempt(); provider.PublishMain(camera, new(Vector3.Zero, Quaternion.Identity));
                provider.MainRestored(true); provider.NativeMainCompleted(true);
            }
            try { for (int i = 0; i < 8; i++) Next(); action(runtime, source, provider, Next, s => live = live with { Source = s }); }
            finally { runtime.Stop(); ThirdPersonApi.Provider = null!; }
            results.Add(new(name, true, null));
        }
        catch (Exception error) { results.Add(new(name, false, error.ToString())); }
    }
}
