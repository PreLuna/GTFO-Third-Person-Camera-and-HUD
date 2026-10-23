using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    [BepInPlugin(Id, "第三人称 HUD 生命周期", Version)]
    [BepInDependency("PreLuna.ThirdPerson", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "PreLuna.ThirdPersonHUD", Version = "0.11.0";
        internal static Plugin Instance;
        internal HudSettings Settings;
        internal HudRuntime Runtime;
        internal HudGameChangeHooks Changes;
        public override void Load()
        {
            // The third-person plugin version alone cannot distinguish old 0.5.0 builds without the bridge.
            var api = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PreLuna.ThirdPerson")
                ?.GetType("PreLuna.ThirdPerson.ThirdPersonApi");
            if (api?.GetField("ApiVersion")?.GetRawConstantValue() is not int version || version != 1
                || api.GetField("HudBridgeVersion")?.GetRawConstantValue() is not int bridge || bridge < 1)
                throw new InvalidOperationException("需要包含 HUD 桥接 v1 的第三人称模组候选。");
            if (Instance != null) throw new InvalidOperationException("第三人称 HUD 已加载，请只保留一个版本。");
            Instance = this;
            try { StartRuntime(); Log.LogInfo("HUD 0.11.0 完整扩展候选已加载；仅有效本机第三人称显示，默认 LeftAlt 展开、Pause 恢复原版。"); }
            catch { Unload(); throw; }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        void StartRuntime()
        {
            Settings = new HudSettings(Config);
            ClassInjector.RegisterTypeInIl2Cpp<HudRuntime>();
            var content = new HudGameContentSource();
            Runtime = AddComponent<HudRuntime>(); Runtime.Initialize(contentSource:content,originalPort:new HudOriginalGamePort());
            Changes = new HudGameChangeHooks(content);
        }
        public override bool Unload()
        {
            var runtime = Runtime; Runtime = null;
            try { try { Changes?.Dispose(); } finally { Changes = null; if (runtime != null) runtime.Stop(); } }
            finally { if (runtime != null) UnityEngine.Object.Destroy(runtime); if (ReferenceEquals(Instance, this)) Instance = null; }
            return true;
        }
    }
}

namespace PreLuna.ThirdPersonHUD
{
    public static class ThirdPersonHudApi
    {
        public const int ApiVersion = 1;
        public static bool TryRestoreOriginalHud()=>Runtime.Plugin.Instance?.Runtime?.RestoreOriginal()==true;
        public static bool TryResumeOriginalHudReplacement()=>Runtime.Plugin.Instance?.Runtime?.RestoreOriginal(resume:true)==true;
        public static bool TryGetOriginalHudStatus(out HudOriginalStatus status)
        {status=default;var runtime=Runtime.Plugin.Instance?.Runtime;return !ReferenceEquals(runtime,null)&&runtime.TryGetOriginalStatus(out status);}
        // Managed data only; data availability does not grant permission to draw into another camera.
        public static bool TryGetPersonalSnapshot(out HudContentSnapshot snapshot)
        {
            snapshot = default;
            var runtime = Runtime.Plugin.Instance?.Runtime;
            return !ReferenceEquals(runtime, null) && runtime.TryGetContent(out snapshot);
        }
        public static bool TryGetCollectionStatus(out HudCollectionStatus status)
        {
            status = default;
            var runtime = Runtime.Plugin.Instance?.Runtime;
            return !ReferenceEquals(runtime, null) && runtime.TryGetCollectionStatus(out status);
        }
        // Main-thread diagnostics only. No Unity references escape through this value snapshot.
        public static bool TryGetStatus(out HudLifecycleStatus status)
        {
            status = default;
            var runtime = Runtime.Plugin.Instance?.Runtime;
            return !ReferenceEquals(runtime, null) && runtime.TryGetStatus(out status);
        }
        public static bool TryGetPerformance(out HudPerformanceSnapshot snapshot)
        {
            snapshot = default;
            var runtime = Runtime.Plugin.Instance?.Runtime;
            return !ReferenceEquals(runtime, null) && runtime.TryGetPerformance(out snapshot);
        }
        public static bool TryGetPresentationDiagnostics(out HudPresentationDiagnostics snapshot)
        {
            snapshot = default;
            var runtime = Runtime.Plugin.Instance?.Runtime;
            return !ReferenceEquals(runtime, null) && runtime.TryGetPresentationDiagnostics(out snapshot);
        }
    }
}
