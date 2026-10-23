using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx.Configuration;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Diagnostics;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    [BepInPlugin(Id,"第三人称视角",Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id="PreLuna.ThirdPerson",Version="0.5.0",GameRevision="34873";
        const string NativeHash="C6A5C3CD8CA5FE2A8C1A71A3D107663E8CBF01404820DBBDA2EA10C4BFD7BF55";
        internal static Plugin Instance;
        internal PluginSettings Settings;
        internal ThirdPersonRuntime Runtime;
        internal OptionalControlProbe Control;
        internal PluginInventory Inventory;
        internal string BinaryHash;
        long hudConfigurationRevision=1;
        internal long HudConfigurationRevision=>Interlocked.Read(ref hudConfigurationRevision);
        EventHandler<SettingChangedEventArgs> hudConfigurationChanged;
        AssemblyLoadEventHandler assemblyLoaded;
        bool beforeSubscribed,renderSubscribed,afterSubscribed;
        Harmony harmony;
        Camera.CameraCallback beforeCamera,beforeRender,afterCamera;
        public override void Load()
        {
            Inventory=PluginInventory.Scan(Directory.EnumerateFiles(Paths.PluginPath,"*.dll",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}));
            var duplicate=Inventory.ValidateSingle(Id,GetType().Assembly.Location);
            if(duplicate!=null)throw new InvalidOperationException("第三人称插件身份检查失败："+duplicate+"；请保留一个安装版本，未接入任何相机补丁。");
            Instance=this;
            try
            {
                BinaryHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GetType().Assembly.Location)));
                Settings=new PluginSettings(Config);harmony=new Harmony(Id);
                Control=new OptionalControlProbe(()=>
                {
                    foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="PreLuna.MicroMaster")return a.GetType("PreLuna.MicroMaster.ControlRuntime")??typeof(Plugin);
                    return null;
                });
                assemblyLoaded=(_,__)=>{Control?.Invalidate();Runtime?.InvalidateHeadCameraProbe();};AppDomain.CurrentDomain.AssemblyLoad+=assemblyLoaded;ResourceLedger.Create(OwnedResource.Subscription);
                hudConfigurationChanged=(_,__)=>Interlocked.Increment(ref hudConfigurationRevision);
                Config.SettingChanged+=hudConfigurationChanged;ResourceLedger.Create(OwnedResource.Subscription);
                string native=Path.Combine(Paths.GameRootPath,"GameAssembly.dll");
                using(var stream=File.OpenRead(native))using(var hash=SHA256.Create())if(Convert.ToHexString(hash.ComputeHash(stream))!=NativeHash)
                    throw new InvalidOperationException("当前游戏原生样本与阶段 1 核实版本不符，未启用相机补丁");
                var guard=P1MethodAddressGuard.Load(Path.Combine(Paths.BepInExRootPath,"interop","MethodAddressToToken.db"));
                if(!guard.Available)throw new InvalidOperationException("当前方法地址表不可用："+guard.Error);
                Install(guard,typeof(FPSCamera),"OnPreCull",nameof(BeforeMain),null,nameof(AfterMainPreCull));
                Install(guard,typeof(CullingSystem.C_Camera),"RunVisibilityOnPreCull",nameof(Visibility));
                Install(guard,typeof(FPSCamera),"OnPostRender",null,null,nameof(AfterMainRenderFault));
                Install(guard,typeof(FPSCamera),"FixedUpdate",nameof(Logic));
                Install(guard,typeof(FPSCamera),"UpdateCameraRay",nameof(Logic));
                Install(guard,typeof(Gear.BulletWeapon),"Fire",nameof(Fire),null,nameof(FireFinished));
                Install(guard,typeof(Gear.Shotgun),"Fire",nameof(Fire),null,nameof(FireFinished));
                Install(guard,typeof(FirstPersonItemHolder),"UpdateAimInput",nameof(AimInput));
                Install(guard,typeof(ItemEquippable),"get_AimButtonHeld",nameof(AimHeld));
                Install(guard,typeof(ItemEquippable),"get_AimButtonPressed",nameof(AimPressed));
                Install(guard,typeof(Weapon),"CastWeaponRay",nameof(CastRay),parameters:new[]{typeof(Transform),typeof(Weapon.WeaponHitData).MakeByRefType(),typeof(Vector3),typeof(int)});
                Install(guard,typeof(PlayerManager),"OnLevelCleanup",nameof(Cleanup));
                ClassInjector.RegisterTypeInIl2Cpp<ThirdPersonRuntime>();Runtime=AddComponent<ThirdPersonRuntime>();
                beforeCamera=(Action<Camera>)AnyCamera;beforeRender=(Action<Camera>)AnyPreRender;afterCamera=(Action<Camera>)AnyCameraFinished;
                Camera.onPreCull+=beforeCamera;beforeSubscribed=true;ResourceLedger.Create(OwnedResource.Subscription);
                Camera.onPreRender+=beforeRender;renderSubscribed=true;ResourceLedger.Create(OwnedResource.Subscription);
                Camera.onPostRender+=afterCamera;afterSubscribed=true;ResourceLedger.Create(OwnedResource.Subscription);
                Log.LogInfo("第三人称 0.5.0 测试候选已加载；SHA256="+BinaryHash+"；F6 开关 / F7 默认 / F8 诊断 / F9 换肩 / F10 风格 / F11 性能采样。默认关闭；总验收尚未通过。");
            }
            catch {Unload();throw;}
        }
        void Install(P1MethodAddressGuard guard,Type type,string name,string prefix=null,string postfix=null,string finalizer=null,Type[] parameters=null)
        {
            var target=AccessTools.DeclaredMethod(type,name,parameters)??throw new MissingMethodException(type.FullName,name);
            var assessment=guard.Inspect(target.Module.Assembly.GetName().Name,target.MetadataToken);
            if(!assessment.CanObserve)throw new InvalidOperationException("拒绝未知/共享原生入口："+type.Name+"."+name+" / "+assessment.Status);
            HarmonyMethod Hook(string method)=>method==null?null:new HarmonyMethod(typeof(Plugin),method){priority=Priority.Last};
            harmony.Patch(target,prefix:Hook(prefix),postfix:Hook(postfix),finalizer:Hook(finalizer));
            Log.LogInfo("接入："+type.Name+"."+name+"；当前唯一地址已核对");
        }
        static void BeforeMain(FPSCamera __instance)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.BeginPreCull(__instance));
        static Exception AfterMainPreCull(FPSCamera __instance,Exception __exception)
        {Instance?.Runtime?.Guard(()=>Instance.Runtime.PreCullFinished(__instance,__exception));return __exception;}
        static void Visibility(CullingSystem.C_Camera __instance)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.BeforeVisibility(__instance));
        static Exception AfterMainRenderFault(FPSCamera __instance,Exception __exception)
        {Instance?.Runtime?.Guard(()=>Instance.Runtime.EndFor(__instance,__exception==null));return __exception;}
        static void Logic(FPSCamera __instance)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.BeforeLogic(__instance));
        static void Fire(Gear.BulletWeapon __instance,out IDisposable __state)
        {
            var journal=new Core.RestoreJournal();__state=journal;
            // Suspend any outer shot even when this nested/remote shot is unsupported.
            var barrier=new FireAimScope(0,Vector3.zero,Vector3.zero);journal.Add(barrier.Dispose);
            Instance?.Runtime?.Guard(()=>{var active=Instance.Runtime.StartFire(__instance);if(active!=null)journal.Add(active.Dispose);});
        }
        static Exception FireFinished(IDisposable __state,Exception __exception)
        {try{__state?.Dispose();}catch(Exception error){Instance?.Runtime?.Guard(()=>throw error);}return __exception;}
        static bool AimInput(FirstPersonItemHolder __instance)
        {bool handled=false;Instance?.Runtime?.Guard(()=>handled=Instance.Runtime.AimInput(__instance));return !handled;}
        static bool AimHeld(ItemEquippable __instance,ref bool __result)
        {bool handled=false,value=false;Instance?.Runtime?.Guard(()=>handled=Instance.Runtime.AimButton(__instance,false,out value));if(handled)__result=value;return !handled;}
        static bool AimPressed(ItemEquippable __instance,ref bool __result)
        {bool handled=false,value=false;Instance?.Runtime?.Guard(()=>handled=Instance.Runtime.AimButton(__instance,true,out value));if(handled)__result=value;return !handled;}
        static void CastRay(Transform __0,ref Weapon.WeaponHitData __1,Vector3 __2)
        {var data=__1;Instance?.Runtime?.Guard(()=>FireAimScope.Current?.Adapt(__0,data,__2));}
        static void Cleanup()=>Instance?.Runtime?.Guard(()=>Instance.Runtime.LevelCleanup());
        static void AnyCamera(Camera camera)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.OtherCameraBegin(camera));
        static void AnyPreRender(Camera camera)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.CameraPreRender(camera));
        static void AnyCameraFinished(Camera camera)=>Instance?.Runtime?.Guard(()=>Instance.Runtime.CameraFinished(camera));
        public override bool Unload()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>{Runtime=null;Instance=null;Control=null;});
            cleanup.Add(()=>{if(Runtime!=null)UnityEngine.Object.Destroy(Runtime);});
            cleanup.Add(()=>harmony?.UnpatchSelf());
            cleanup.Add(()=>{if(hudConfigurationChanged!=null){Config.SettingChanged-=hudConfigurationChanged;hudConfigurationChanged=null;ResourceLedger.Release(OwnedResource.Subscription);}});
            cleanup.Add(()=>{if(assemblyLoaded!=null){AppDomain.CurrentDomain.AssemblyLoad-=assemblyLoaded;assemblyLoaded=null;ResourceLedger.Release(OwnedResource.Subscription);}});
            cleanup.Add(()=>{if(afterSubscribed){Camera.onPostRender-=afterCamera;afterSubscribed=false;ResourceLedger.Release(OwnedResource.Subscription);}});
            cleanup.Add(()=>{if(renderSubscribed){Camera.onPreRender-=beforeRender;renderSubscribed=false;ResourceLedger.Release(OwnedResource.Subscription);}});
            cleanup.Add(()=>{if(beforeSubscribed){Camera.onPreCull-=beforeCamera;beforeSubscribed=false;ResourceLedger.Release(OwnedResource.Subscription);}});
            cleanup.Add(()=>Runtime?.Stop());
            return true;
        }
    }
}


