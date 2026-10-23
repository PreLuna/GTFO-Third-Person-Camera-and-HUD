using System;
using Il2CppInterop.Runtime.Attributes;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    public sealed class HudRuntime : MonoBehaviour
    {
        IHudRuntimePresentation presentation;
        HolographicHud panel;
        HudPanelOptions panelOptions=HudPanelOptions.Default;
        readonly HudPanelVisualController visuals=new();
        readonly HudPersonalFeedback feedback=new();
        readonly HudAlerts alerts=new();
        HudAlertOptions alertOptions=HudAlertOptions.Default;
        HudPersonalOptions personalOptions=HudPersonalOptions.Default;
        HudTeamOptions teamOptions=HudTeamOptions.Default;
        IHudTeamCaptureControl teamCapture;
        IHudSceneCaptureControl sceneCapture;
        HudSceneOptions sceneOptions=HudSceneOptions.Default;
        HudRuntimeContext drawCurrent;
        ThirdPersonDisplayFrame drawDisplay;
        HudExpandState expand;
        HudLifecycle lifecycle;
        IThirdPersonViewProvider provider;
        bool stopped, reading;
        double lastErrorAt = double.NegativeInfinity;
        double lastPerformanceAt = double.NaN;
        double lastComputationErrorAt = double.NegativeInfinity;
        double lastCollectionErrorAt = double.NegativeInfinity;
        Action drawAction;
        HudOriginalHud original;
        HudReplacementOptions replacementOptions=HudReplacementOptions.Default;
        bool originalRestored;
        ThirdPersonViewState replacementView;
        long replacementEpoch;
        int replacementFrame=-1;
        public HudRuntime(IntPtr pointer) : base(pointer) { }
        [HideFromIl2Cpp] internal void Initialize(Func<double> clock = null, IHudDataComputer computer = null, IHudBackgroundExecutor background = null, IHudContentSource contentSource = null, bool legacyMarker = false, IHudOriginalHudPort originalPort = null)
        { teamCapture=contentSource as IHudTeamCaptureControl; sceneCapture=contentSource as IHudSceneCaptureControl; presentation = legacyMarker ? new PlaceholderHud() : (panel=new HolographicHud()); original=new HudOriginalHud(originalPort); lifecycle = new HudLifecycle(presentation, Report, clock, computer:computer, background:background, computeError:ReportComputation, contentSource:contentSource, collectionError:ReportCollection); drawAction = DrawPresentation; }
        [HideFromIl2Cpp] internal bool TryGetOriginalStatus(out HudOriginalStatus status)
        {status=default;if(lifecycle==null||!lifecycle.OnMainThread)return false;status=original.Status;return true;}
        [HideFromIl2Cpp] internal bool RestoreOriginal(bool resume=false)
        {if(stopped||lifecycle==null||!lifecycle.OnMainThread)return false;originalRestored=!resume;original.ResetConflicts();replacementFrame=-1;return true;}
        [HideFromIl2Cpp] HudOriginalGroup Coverage(in HudRuntimeContext current)
        {
            if(originalRestored||panel==null||!panel.IsReady||!replacementOptions.Enabled
                ||!lifecycle.TryGetContent(current,out var s)||!lifecycle.TryGetStatus(out var life)||!lifecycle.TryGetCollectionStatus(out var collection))return 0;
            var personal=HudReplacementCoverage.Evaluate(current,s,life.WorkEpoch,collection.ContentRevision,collection.Policy,personalOptions,panelOptions.Layout.Expanded)&replacementOptions.Groups;
            var team=teamOptions.Enabled&&teamOptions.ReplaceOriginal&&panelOptions.Layout.Team
                ?HudTeamMapper.Map(current,s,life.WorkEpoch,collection.ContentRevision,collection.Policy,teamOptions,panelOptions.Layout.Expanded).Coverage:0;
            bool scene=HudSceneMapper.CountdownCovered(current,s,life.WorkEpoch,collection.ContentRevision,collection.Policy,sceneOptions)
                &&HudSceneDrawing.Layout(presentation.Diagnostics.LastDraw.Panel,drawDisplay.Camera.Viewport,sceneOptions,out _,out _);
            return personal|team|(scene?HudOriginalGroup.Countdown:0);
        }
        [HideFromIl2Cpp] void CheckOriginal(in HudRuntimeContext current,bool available,bool enabled)
        {
            if(!available||!enabled||!HudEligibility.EvaluateCurrent(current).Allowed||panel==null||!panel.IsReady
                ||!lifecycle.TryGetStatus(out var status)||status.Phase!=HudLifecyclePhase.Ready
                ||replacementFrame<current.FrameNumber-1||replacementFrame>current.FrameNumber
                ||replacementView.Context!=current.ExpectedContext||replacementView.Binding!=current.ExpectedBinding
                ||replacementView.EligibilityEpoch!=current.CurrentView.EligibilityEpoch||replacementView.ConfigurationRevision!=current.CurrentView.ConfigurationRevision
                ||replacementEpoch!=status.WorkEpoch){original.RestoreAll();return;}
            original.RetainOnly(Coverage(current));
        }
        [HideFromIl2Cpp] internal bool TryGetContent(out HudContentSnapshot snapshot)
        {
            snapshot = default;
            return !stopped && lifecycle != null && lifecycle.OnMainThread
                && ThirdPersonApi.TryGetRuntimeContext(out var current) && lifecycle.TryGetContent(current, out snapshot);
        }
        [HideFromIl2Cpp] internal bool TryGetCollectionStatus(out HudCollectionStatus status)
        { status = default; return !stopped && lifecycle != null && lifecycle.TryGetCollectionStatus(out status); }
        [HideFromIl2Cpp] internal bool TryGetStatus(out HudLifecycleStatus status)
        { status = default; return lifecycle != null && lifecycle.TryGetStatus(out status); }
        [HideFromIl2Cpp] internal bool TryGetPerformance(out HudPerformanceSnapshot snapshot)
        { snapshot = default; return lifecycle != null && lifecycle.TryGetPerformance(out snapshot); }
        [HideFromIl2Cpp] internal bool TryGetPresentationDiagnostics(out HudPresentationDiagnostics snapshot)
        { snapshot = default; if (lifecycle == null || !lifecycle.OnMainThread || presentation == null) return false; snapshot = presentation.Diagnostics; return true; }
        public void Update()
        {
            if (stopped || lifecycle == null) return;
            try
            {
                ConnectProvider();
                HudRuntimeContext current = default;
                bool available = provider != null && ThirdPersonApi.TryGetRuntimeContext(out current);
                bool inLevel = GameStateManager.CurrentStateName == eGameStateName.InLevel && (!available || current.InLevel);
                var settings=Plugin.Instance.Settings;
                var options=settings.Read();
                var nextReplacement=settings.ReadReplacement();
                if(nextReplacement!=replacementOptions){original.ResetConflicts();replacementOptions=nextReplacement;}
                if(Input.GetKeyDown(settings.RestoreKey))
                {RestoreOriginal(originalRestored);Plugin.Instance?.Log.LogInfo(originalRestored?"原版 HUD 已恢复，替代已暂停；再次按恢复键可恢复自动校验。":"允许重新校验原版 HUD 替代。");}
                if(panel!=null)
                {
                    panelOptions=settings.ReadPanel();
                    personalOptions=settings.ReadPersonal();
                    var nextTeam=settings.ReadTeam();
                    if(nextTeam!=teamOptions){original.ResetConflicts();teamOptions=nextTeam;}
                    teamCapture?.SetTeamEnabled(teamOptions.Enabled);
                    var nextScene=settings.ReadScene();
                    if(nextScene!=sceneOptions){original.ResetConflicts();sceneOptions=nextScene;}
                    sceneCapture?.ConfigureScene(sceneOptions.Collection);
                    alertOptions=settings.ReadAlerts();
                    bool eligible=available&&inLevel&&options.Enabled&&panelOptions.Layout.Enabled&&HudEligibility.EvaluateCurrent(current).Allowed;
                    expand=expand.Update(eligible,eligible&&Input.GetKey(settings.ExpandKey),settings.ToggleExpand);
                    bool automatic=eligible&&lifecycle.TryGetContent(current,out var content)&&lifecycle.TryGetCollectionStatus(out var collection)
                        &&lifecycle.TryGetStatus(out var life)&&HudPersonalMapper.WantsDetails(current,content,life.WorkEpoch,collection.ContentRevision,collection.Policy,personalOptions);
                    panelOptions=panelOptions with{Layout=panelOptions.Layout with{Expanded=expand.Expanded||automatic,Team=teamOptions.Enabled}};
                    if(!eligible){visuals.Reset();feedback.Reset();alerts.Reset();}
                    panel.Configure(panelOptions);
                    options=options with{Enabled=options.Enabled&&panelOptions.Layout.Enabled,Panel=panelOptions.Layout};
                }
                lifecycle.Tick(new(inLevel, Time.frameCount, ThirdPersonApi.MonotonicSeconds, available, current), options, settings.ReadWork(), settings.ReadExecution(), settings.ReadCollection());
                CheckOriginal(current,available,inLevel&&options.Enabled&&settings.ReadWork().FrameBudgetMilliseconds>0);
                RecordPerformance(false);
            }
            catch (Exception error) { original?.RestoreAll();lifecycle.ReportPresentationFailure(error, ThirdPersonApi.MonotonicSeconds); }
        }
        [HideFromIl2Cpp] void ConnectProvider()
        {
            ThirdPersonApi.TryGetProvider(out var next);
            if (ReferenceEquals(next, provider)) return;
            original.RestoreAll();DisconnectProvider(); lifecycle.HideForRender(); provider = next;
            if (provider == null) return;
            provider.StateChanged += StateChanged; provider.RenderChanged += RenderChanged;
            if (provider.TryGetViewState(out var state)) lifecycle.ObserveState(state);
        }
        [HideFromIl2Cpp] void DisconnectProvider()
        {
            var previous = provider; provider = null;
            if (previous == null) return;
            try { previous.StateChanged -= StateChanged; }
            finally { previous.RenderChanged -= RenderChanged; }
        }
        [HideFromIl2Cpp] void StateChanged(ThirdPersonViewState state)
        { lifecycle?.ObserveState(state); if(state.Availability!=ViewAvailability.Ready||state.Source!=ViewSource.LocalThirdPerson||state.Mode is not (ThirdPersonMode.Exploration or ThirdPersonMode.ShoulderAim)
                ||state.Binding!=replacementView.Binding||state.Context!=replacementView.Context||state.EligibilityEpoch!=replacementView.EligibilityEpoch)
            {original?.RestoreAll();expand=default;visuals.Reset();feedback.Reset();alerts.Reset();} }
        [HideFromIl2Cpp] void RenderChanged(ThirdPersonRenderNotice notice)
        {
            if (stopped || lifecycle == null) return;
            if (notice.Render.Phase != RenderPhase.MainCommitted) { lifecycle.HideForRender(); return; }
            if (reading) return;
            try
            {
                reading = true;
                if (ReadCommitted(out var current, out var display)) lifecycle.ObserveCommitted(current, display);
                else {original.RestoreAll();lifecycle.HideForRender();}
            }
            catch (Exception error) { original.RestoreAll();lifecycle.ReportPresentationFailure(error, ThirdPersonApi.MonotonicSeconds); }
            finally { reading = false; }
        }
        [HideFromIl2Cpp] bool ReadCommitted(out HudRuntimeContext current, out ThirdPersonDisplayFrame display)
        {
            current = default; display = default;
            return provider != null && provider.TryGetDisplayFrame(new(Time.frameCount, HudDisplayUsage.ScreenComposition), out display)
                && ThirdPersonApi.TryGetRuntimeContext(out current);
        }
        [HideFromIl2Cpp] void PreparePanel(in HudRuntimeContext current)
        {
            if(panel==null)return;
            bool has=lifecycle.TryGetContent(current,out var snapshot);
            var life=default(HudLifecycleStatus);
            bool statusAvailable=lifecycle.TryGetCollectionStatus(out var status)&&lifecycle.TryGetStatus(out life);
            var policy=statusAvailable?status.Policy:HudContentPolicy.Default;
            var view=has&&statusAvailable?HudPersonalMapper.Map(current,snapshot,life.WorkEpoch,status.ContentRevision,policy,personalOptions,panelOptions.Layout.Expanded)
                :HudPersonalMapper.Unavailable(personalOptions,status.Enabled?HudValueState.Unreadable:HudValueState.Unknown);
            view=feedback.Apply(current,snapshot.Header,view,personalOptions,policy);
            view=view with{Team=HudTeamMapper.Map(current,snapshot,life.WorkEpoch,status.ContentRevision,policy,teamOptions,panelOptions.Layout.Expanded)};
            view=view with{Scene=HudSceneMapper.Map(current,drawDisplay,snapshot,life.WorkEpoch,status.ContentRevision,policy,sceneOptions,panelOptions.Layout.Expanded)};
            panel.SetView(alerts.Apply(current,drawDisplay,has,snapshot,life.WorkEpoch,status.ContentRevision,policy,view,alertOptions,panelOptions.Behavior));
            panel.SetVisual(visuals.Update(current,has,snapshot,policy,panelOptions));
        }
        [HideFromIl2Cpp] void DrawPresentation()
        {
            if(panel!=null)PreparePanel(drawCurrent with{NowSeconds=ThirdPersonApi.MonotonicSeconds});
            long before=presentation.Diagnostics.ActualDraws;
            presentation.Draw();
            bool rendered=panel!=null&&presentation.Diagnostics.ActualDraws>before&&presentation.Diagnostics.LastDraw.FrameNumber==drawCurrent.FrameNumber;
            var fresh=drawCurrent with{NowSeconds=ThirdPersonApi.MonotonicSeconds};
            original.Apply(fresh,drawDisplay,rendered,rendered?Coverage(fresh):0);
            if(rendered&&lifecycle.TryGetStatus(out var life)){replacementFrame=fresh.FrameNumber;replacementView=fresh.CurrentView;replacementEpoch=life.WorkEpoch;}
        }
        public void OnGUI()
        {
            if (stopped || lifecycle == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            try
            {
                if (ReadCommitted(out var current, out var display)) {drawCurrent=current;drawDisplay=display;if(!lifecycle.TryDraw(current, display, drawAction))original.RestoreAll();}
                else {if(!ThirdPersonApi.TryGetRuntimeContext(out var c)||c.Render.Phase!=RenderPhase.AuxiliaryActive||!HudEligibility.EvaluateCurrent(c).Allowed)original.RestoreAll();lifecycle.HideForRender();}
            }
            catch (Exception error) { original.RestoreAll();lifecycle.ReportPresentationFailure(error, ThirdPersonApi.MonotonicSeconds); }
        }
        [HideFromIl2Cpp] void RecordPerformance(bool final)
        {
            if (!lifecycle.TryGetStatus(out var status)) return;
            double now = ThirdPersonApi.MonotonicSeconds, interval = Plugin.Instance?.Settings.LogIntervalSeconds ?? 0;
            bool active = status.Phase is HudLifecyclePhase.Initializing or HudLifecyclePhase.Ready;
            if (!final && (interval <= 0 || !double.IsFinite(now))) { lastPerformanceAt = double.NaN; return; }
            if (!final && !active) return;
            if (!final && !double.IsFinite(lastPerformanceAt)) { lastPerformanceAt = now; return; }
            if (!final && now - lastPerformanceAt < interval) return;
            lastPerformanceAt = now;
            if (!lifecycle.TryGetPerformance(out var p) || p.Executor.Initialization.Count + p.Captured == 0) return;
            var computation = p.Computation; var worker = computation.Worker;
            Plugin.Instance?.Log.LogInfo($"HUD 计算通路={computation.RequestedMode}/{computation.EffectiveMode}；后台线程启动={worker.ThreadStarts} 运行/排队={worker.Running}/{worker.Pending} 后台计算调用={worker.Executed} 累计/峰值ms={worker.TotalCalculationMilliseconds:F3}/{worker.MaximumCalculationMilliseconds:F3} 接收异常={computation.Errors} 过期完成={computation.ExpiredCompletions}");
            Plugin.Instance?.Log.LogInfo($"HUD 性能(次数/均值/P95/峰值ms)：初始化={FormatTiming(p.Executor.Initialization)} 采集={FormatTiming(p.Executor.Collection)} 计算={FormatTiming(p.Executor.Computation)} 应用={FormatTiming(p.Executor.Application)} 绘制={FormatTiming(p.Executor.Draw)}；预算超时帧={p.Executor.BudgetOverrunFrames} 待处理={p.PendingInputs} 保留结果={p.RetainedResults} 采集={p.Captured} 丢弃过期={p.ExpiredInputs + p.ExpiredResults} 取消输入/结果={p.CancelledInputs}/{p.CancelledResults} 复用={p.ReusedApplications} 资源准备步骤={status.InitializationSteps} 释放={status.Releases}");
        }
        [HideFromIl2Cpp] static string FormatTiming(HudTiming timing) =>
            $"{timing.Count}/{timing.MeanMilliseconds:F3}/{timing.RecentP95Milliseconds:F3}/{timing.MaximumMilliseconds:F3}";
        [HideFromIl2Cpp] void ReportComputation(string error)
        {
            double now = ThirdPersonApi.MonotonicSeconds;
            if (now - lastComputationErrorAt < 5) return;
            lastComputationErrorAt = now; Plugin.Instance?.Log.LogWarning("HUD 纯计算失败，冷却后重试：" + error);
        }
        [HideFromIl2Cpp] void ReportCollection(string error)
        {
            double now = ThirdPersonApi.MonotonicSeconds;
            if (now - lastCollectionErrorAt < 5) return;
            lastCollectionErrorAt = now; Plugin.Instance?.Log.LogWarning("HUD 个人数据采集失败，冷却后重试：" + error);
        }
        [HideFromIl2Cpp] void Report(Exception error)
        {
            double now = ThirdPersonApi.MonotonicSeconds;
            if (now - lastErrorAt < 5) return;
            lastErrorAt = now; Plugin.Instance?.Log.LogWarning("HUD 暂停并清理，稍后重试：" + error.Message);
        }
        public void OnDisable() => original?.RestoreAll();
        public void OnDestroy() => Stop();
        [HideFromIl2Cpp] internal void Stop()
        {
            if (stopped) return; stopped = true;
            try { DisconnectProvider(); }
            finally { original?.RestoreAll();lifecycle?.Dispose(); if (lifecycle != null) RecordPerformance(true); }
        }
    }
}
