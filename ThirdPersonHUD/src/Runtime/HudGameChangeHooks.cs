using System;
using System.IO;
using System.Reflection;
using BepInEx;
using Gear;
using HarmonyLib;
using Player;
using PreLuna.ThirdPerson.Diagnostics;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // Observe only verified, unique native entries. No gameplay writes or unbounded event queues.
    internal sealed class HudGameChangeHooks : IDisposable
    {
        readonly Harmony harmony = new(Plugin.Id + ".Content");
        readonly HudGameContentSource source;
        internal int Installed { get; private set; }
        internal HudGameChangeHooks(HudGameContentSource source)
        {
            this.source = source;
            var guard = P1MethodAddressGuard.Load(Path.Combine(Paths.BepInExRootPath, "interop", "MethodAddressToToken.db"));
            Observe(guard, typeof(Dam_PlayerDamageLocal), "UpdateHealthGui", Type.EmptyTypes, nameof(Health));
            Observe(guard, typeof(PlayerAmmoStorage), "UpdateSlotAmmoUI", new[] { typeof(InventorySlotAmmo), typeof(int) }, nameof(Ammo));
            Observe(guard, typeof(BulletWeapon), "UpdateAmmoStatus", Type.EmptyTypes, nameof(Weapon));
            Observe(guard, typeof(FirstPersonItemHolder), "SetWieldedItem", new[] { typeof(ItemEquippable) }, nameof(Equipment));
            Observe(guard, typeof(PlayerInventoryLocal), "TriggerReload", Type.EmptyTypes, nameof(Reload));
            Observe(guard, typeof(Dam_PlayerDamageLocal), "Hitreact", new[] { typeof(float), typeof(UnityEngine.Vector3), typeof(bool), typeof(bool), typeof(bool) }, nameof(Hit));
            Observe(guard, typeof(NavMarker), "Ping", new[] { typeof(float) }, nameof(TeamPing));
            Observe(guard, typeof(ChainedPuzzles.CP_Bioscan_Hud), "Update", Type.EmptyTypes, nameof(Bioscan));
            Plugin.Instance?.Log.LogInfo($"数据、受击、标记与扫描观察点：{Installed}/8；未接入的事件保持缺失，其他读数按工作间隔刷新。");
        }
        void Observe(P1MethodAddressGuard guard, Type type, string name, Type[] parameters, string callback)
        {
            var method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null || !guard.Inspect(method.Module.Assembly.GetName().Name, method.MetadataToken).CanObserve)
            { Plugin.Instance?.Log.LogWarning("个人数据观察点不可用，保留间隔刷新：" + type.Name + "." + name); return; }
            try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(HudGameChangeHooks), callback) { priority = Priority.Last }); Installed++; }
            catch (Exception error) { Plugin.Instance?.Log.LogWarning("个人数据观察点接入失败：" + error.Message); }
        }
        internal void Notify(long receiver, HudContentChange change)
        { try { source.Notify(receiver, change); } catch { } }
        static void Health(Dam_PlayerDamageLocal __instance) => Plugin.Instance?.Changes?.Notify(__instance.Pointer.ToInt64(), HudContentChange.Health);
        static void Ammo(PlayerAmmoStorage __instance) => Plugin.Instance?.Changes?.Notify(__instance.Pointer.ToInt64(), HudContentChange.Ammo);
        static void Weapon(BulletWeapon __instance) => Plugin.Instance?.Changes?.Notify(__instance.Pointer.ToInt64(), HudContentChange.Ammo);
        static void Equipment(FirstPersonItemHolder __instance) => Plugin.Instance?.Changes?.Notify(__instance.Pointer.ToInt64(), HudContentChange.Equipment);
        static void Reload(PlayerInventoryLocal __instance) => Plugin.Instance?.Changes?.Notify(__instance.Pointer.ToInt64(), HudContentChange.Reload);
        static void TeamPing(NavMarker __instance)
        { try { Plugin.Instance?.Changes?.source.NotifyTeamPing(__instance.Pointer.ToInt64()); } catch { } }
        static void Bioscan(ChainedPuzzles.CP_Bioscan_Hud __instance)
        { try { Plugin.Instance?.Changes?.source.ObserveBioscan(__instance); } catch { } }
        static void Hit(Dam_PlayerDamageLocal __instance, float __0, UnityEngine.Vector3 __1)
        { try { Plugin.Instance?.Changes?.source.NotifyHit(__instance.Pointer.ToInt64(), __0, new(__1.x, __1.y, __1.z)); } catch { } }
        public void Dispose() => harmony.UnpatchSelf();
    }
}
