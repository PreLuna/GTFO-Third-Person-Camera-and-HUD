#nullable enable
using System;
using Gear;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using LevelGeneration;
using Player;
using PreLuna.ThirdPerson;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // This adapter is the only content layer that dereferences game/Unity objects. Main thread only.
    internal sealed partial class HudGameContentSource : IHudContentSource, IHudContentRefreshSource, IHudContentTransientSource, IHudTeamCaptureControl
    {
        readonly int thread = Environment.CurrentManagedThreadId;
        readonly Func<double> clock;
        readonly Func<HudRuntimeContext?> readContext;
        long changeVersion = 1;
        long damage, inventory, holder, storage, primary, secondary;
        ThirdPersonViewState hitView;
        HudHit latestHit;
        long hitSequence;
        public bool OnMainThread => Environment.CurrentManagedThreadId == thread;
        public long ChangeVersion { get { if (!OnMainThread) return 0; ObserveTeam(); ObserveScene(); return changeVersion; } }
        internal HudGameContentSource(Func<double>? clock = null, Func<HudRuntimeContext?>? context = null)
        { this.clock = clock ?? (() => ThirdPersonApi.MonotonicSeconds); readContext = context ?? Context; }
        static HudRuntimeContext? Context() => ThirdPersonApi.TryGetRuntimeContext(out var c) ? c : null;
        public void ClearTransient() { if (OnMainThread) { latestHit = default; hitView = default; ClearTeamTransient(); ClearSceneTransient(); } }
        internal void Notify(long receiver, HudContentChange change)
        {
            if (!OnMainThread || receiver == 0 || change == HudContentChange.None) return;
            bool owns = change == HudContentChange.Health ? receiver == damage
                : change == HudContentChange.Equipment ? receiver == holder || receiver == inventory
                : change == HudContentChange.Reload ? receiver == inventory
                : receiver == storage || receiver == primary || receiver == secondary;
            if (owns) changeVersion = checked(changeVersion + 1);
        }
        static long Id(Il2CppObjectBase? value) => value?.Pointer.ToInt64() ?? 0;
        internal void NotifyHit(long receiver, float amount, System.Numerics.Vector3 incoming)
        {
            if (!OnMainThread || receiver == 0 || !float.IsFinite(amount) || amount <= 0) return;
            try
            {
                var c = readContext();
                if (!c.HasValue || !HudEligibility.EvaluateCurrent(c.Value).Allowed) return;
                var local = PlayerManager.GetLocalPlayerAgent()?.TryCast<LocalPlayerAgent>();
                if (local == null || Binding(local) != c.Value.ExpectedBinding || Id(local.Damage) != receiver
                    || PlayerKey(local.Damage?.Owner) != c.Value.ExpectedBinding.LocalPlayer) return;
                double now = clock();
                if (!double.IsFinite(now) || now < 0) return;
                var toward = -incoming; // Native Hitreact uses incoming travel direction for external push force.
                if (!float.IsFinite(toward.X) || !float.IsFinite(toward.Y) || !float.IsFinite(toward.Z)) toward = default;
                latestHit = new(checked(++hitSequence), now, toward);
                hitView = c.Value.CurrentView;
                changeVersion = checked(changeVersion + 1);
            }
            catch { /* Optional observer failure must not affect the native hit. */ }
        }
        static ObjectIdentity Key(UnityEngine.Object? value) => value == null ? default : new(Id(value), value.GetInstanceID());
        static PlayerIdentity PlayerKey(PlayerAgent? player) => player == null || player.Owner == null
            ? default : new(Key(player), Id(player.Owner), player.PlayerSlotIndex);
        static LocalViewBinding Binding(LocalPlayerAgent local) => new(PlayerKey(local), Key(local.PlayerSyncModel),
            Key(local.FPSCamera?.m_camera), Id(Builder.CurrentFloor), (int)local.DimensionIndex);
        bool Current(in HudContentCaptureRequest request, out LocalPlayerAgent? local)
        {
            local = null;
            var context = readContext();
            if (!context.HasValue || request.WorkEpoch <= 0 || request.ContentRevision <= 0
                || request.Origin.SampleFrame != context.Value.FrameNumber
                || !HudEligibility.EvaluateData(context.Value, new(request.Origin, clock()), HudValidityPolicy.Default).Allowed) return false;
            local = PlayerManager.GetLocalPlayerAgent()?.TryCast<LocalPlayerAgent>();
            return local != null && Binding(local) == request.Origin.View.Binding;
        }
        public bool TryCapture(in HudContentCaptureRequest request, out HudContentSnapshot snapshot)
        {
            snapshot = default;
            if (!OnMainThread) return false;
            try
            {
                if (!Current(request, out var local) || local == null) return false;
                var playerStamp = HudValueStamp.AtCapture(request, request.Origin.View.Binding.LocalPlayer.Agent);
                var health = Missing<HudQuantity>(HudValueState.Unreadable, playerStamp);
                var infection = Missing<HudQuantity>(HudValueState.Unreadable, playerStamp);
                var life = Missing<HudLife>(HudValueState.Unreadable, playerStamp);
                var selected = Missing<HudSelection>(HudValueState.Unreadable, playerStamp);
                damage = Id(local.Damage); inventory = Id(local.Inventory); holder = Id(local.FPItemHolder);
                long wielded = Id(local.Inventory?.WieldedItem);
                var wieldedSlot = local.Inventory?.WieldedSlot ?? InventorySlot.None;
                try
                {
                    var d = local.Damage;
                    if (d != null && PlayerKey(d.Owner) == request.Origin.View.Binding.LocalPlayer)
                    {
                        try { health = HudNumberRules.TryHealth(d.Health, d.HealthMax, out var h) ? Known(h, playerStamp) : Missing<HudQuantity>(HudValueState.Invalid, playerStamp); } catch { }
                        try { infection = HudNumberRules.TryInfection(d.Infection, out var i) ? Known(i, playerStamp) : Missing<HudQuantity>(HudValueState.Invalid, playerStamp); } catch { }
                    }
                }
                catch { }
                try
                {
                    var kind = local.Locomotion?.m_currentStateEnum == PlayerLocomotion.PLOC_State.Downed
                        ? HudLifeKind.Downed : local.Alive ? HudLifeKind.Alive : HudLifeKind.Dead;
                    life = Known(new HudLife(kind), playerStamp);
                }
                catch { }
                try
                {
                    var inv = local.Inventory;
                    if (inv != null && PlayerKey(inv.Owner) == request.Origin.View.Binding.LocalPlayer)
                    {
                        var slot = Map(inv.WieldedSlot);
                        if (slot != HudEquipmentSlot.Unknown && (inv.WieldedItem != null || slot == HudEquipmentSlot.EmptyHands))
                            selected = Known(new HudSelection(slot), playerStamp);
                    }
                }
                catch { }
                PlayerBackpack? bag = null;
                if (local.Owner != null) PlayerBackpackManager.TryGetBackpack(local.Owner, out bag);
                if (bag != null && Id(bag.Owner) != request.Origin.View.Binding.LocalPlayer.OwnerHandle) bag = null;
                var ammo = bag?.AmmoStorage; storage = Id(ammo);
                var melee = Equipment(request, bag, ammo, InventorySlot.GearMelee, playerStamp);
                var main = Equipment(request, bag, ammo, InventorySlot.GearStandard, playerStamp);
                var special = Equipment(request, bag, ammo, InventorySlot.GearSpecial, playerStamp);
                var tool = Equipment(request, bag, ammo, InventorySlot.GearClass, playerStamp);
                var pack = Equipment(request, bag, ammo, InventorySlot.ResourcePack, playerStamp);
                var consumable = Equipment(request, bag, ammo, InventorySlot.Consumable, playerStamp);
                var heavy = Equipment(request, bag, ammo, InventorySlot.ConsumableHeavy, playerStamp);
                var carry = Equipment(request, bag, ammo, InventorySlot.InLevelCarry, playerStamp);
                var action = Action(local, request, playerStamp);
                primary = main.Identity.State == HudValueState.Available ? main.Identity.RawValue.Instance.NativeHandle : 0;
                secondary = special.Identity.State == HudValueState.Available ? special.Identity.RawValue.Instance.NativeHandle : 0;
                var team = CaptureTeam(request);
                var scene = CaptureScene(request, local, bag);
                if (!Current(request, out var after) || after == null || Id(after.Damage) != damage || Id(after.Inventory) != inventory) return false;
                if (Id(after.Inventory?.WieldedItem) != wielded || (after.Inventory?.WieldedSlot ?? InventorySlot.None) != wieldedSlot) return false;
                PlayerBackpack? afterBag = null;
                if (after.Owner != null) PlayerBackpackManager.TryGetBackpack(after.Owner, out afterBag);
                if (Id(afterBag) != Id(bag) || Id(afterBag?.AmmoStorage) != storage) return false;
                var personal = new HudPersonalSnapshot(health, infection, life, selected, melee, main, special, tool, pack,
                    consumable, heavy, carry, action);
                var view = request.Origin.View;
                if (hitView.Context != view.Context || hitView.Binding != view.Binding || hitView.EligibilityEpoch != view.EligibilityEpoch
                    || hitView.ConfigurationRevision != view.ConfigurationRevision) latestHit = default;
                if (latestHit.IsValid && latestHit.OccurredAtSeconds <= playerStamp.CapturedAtSeconds
                    && playerStamp.CapturedAtSeconds - latestHit.OccurredAtSeconds <= 5)
                    personal = personal with { Hit = Known(latestHit, playerStamp) };
                snapshot = new(new(HudContentContract.SchemaVersion, new(request.Origin, clock()), request.WorkEpoch, request.ContentRevision), personal) { Team = team, Scene = scene };
                return true;
            }
            catch { snapshot = default; return false; }
        }
        static HudActionSnapshot Action(LocalPlayerAgent local, in HudContentCaptureRequest request, HudValueStamp stamp)
        {
            var unavailable = new HudActionSnapshot(Missing<HudAction>(HudValueState.Unreadable, stamp), Missing<HudQuantity>(HudValueState.Unsupported, stamp));
            try
            {
                var inv = local.Inventory;
                if (inv == null || PlayerKey(inv.Owner) != request.Origin.View.Binding.LocalPlayer) return unavailable;
                var slot = Map(inv.WieldedSlot); var item = inv.WieldedItem;
                if (slot == HudEquipmentSlot.EmptyHands && item == null)
                    return unavailable with { Action = Known(new HudAction(HudActionKind.Idle), stamp) };
                // An action must belong to the actual local wielded object, not a backpack neighbour.
                if (item == null || PlayerKey(item.Owner) != request.Origin.View.Binding.LocalPlayer) return unavailable;
                unavailable = unavailable with { Equipment = Key(item) };
                var kind = item.IsReloading ? HudActionKind.Reloading : HudActionKind.Idle;
                var melee = item.TryCast<MeleeWeaponFirstPerson>();
                if (melee != null && melee.CurrentStateName is eMeleeWeaponState.AttackChargeUpLeft or eMeleeWeaponState.AttackChargeUpRight)
                    kind = HudActionKind.MeleeCharging;
                var pack = item.TryCast<ResourcePackFirstPerson>();
                if (pack != null)
                {
                    var timer = pack.m_interactApplyResource;
                    if (timer == null) return unavailable;
                    if (timer.TimerIsActive)
                    {
                        if (PlayerKey(timer.m_localPlayerInteractInfo?.Agent) != request.Origin.View.Binding.LocalPlayer) return unavailable;
                        kind = HudActionKind.UsingItem;
                        var progress = HudNumberRules.TryProgress(timer.InteractionTimerRel, out var q)
                            ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp);
                        return unavailable with { Action = Known(new HudAction(kind), stamp), Progress = progress };
                    }
                }
                else if (slot is HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy or HudEquipmentSlot.Tool)
                    return unavailable with { Action = Missing<HudAction>(HudValueState.Unsupported, stamp) };
                if (slot == HudEquipmentSlot.Carry) kind = HudActionKind.Carrying;
                return unavailable with { Action = Known(new HudAction(kind), stamp) };
            }
            catch { return unavailable; }
        }
        static HudEquipmentSnapshot Equipment(in HudContentCaptureRequest request, PlayerBackpack? bag,
            PlayerAmmoStorage? storage, InventorySlot gameSlot, HudValueStamp playerStamp)
        {
            var slot = Map(gameSlot);
            if (bag == null) return Absent(slot, HudValueState.Unreadable, playerStamp);
            try
            {
                bool deployed = slot == HudEquipmentSlot.Tool && bag.IsDeployed(gameSlot);
                Item? item;
                if (deployed)
                {
                    if (!bag.TryGetDeployedItem(gameSlot, out item) || item == null)
                        return Absent(slot, HudValueState.Unreadable, playerStamp) with { Deployment = Known(new HudDeployment(HudDeploymentKind.Deployed), playerStamp) };
                    if (PlayerKey(item.Owner) != request.Origin.View.Binding.LocalPlayer) return Absent(slot, HudValueState.Unreadable, playerStamp);
                }
                else
                {
                    if (!bag.TryGetBackpackItem(gameSlot, out var entry)) return Absent(slot, HudValueState.NotEquipped, playerStamp);
                    item = entry?.Instance;
                    if (item == null) return Absent(slot, HudValueState.Unreadable, playerStamp);
                }
                var stamp = HudValueStamp.AtCapture(request, Key(item));
                if (item.Owner != null && PlayerKey(item.Owner) != request.Origin.View.Binding.LocalPlayer)
                    return Absent(slot, HudValueState.Unreadable, playerStamp);
                var result = Absent(slot, HudValueState.Unsupported, stamp) with
                { Identity = Known(new HudEquipmentIdentity(Key(item), slot), stamp) };
                var equippable = item.TryCast<ItemEquippable>();
                try { result = result with { Name = Known(new HudText(equippable != null ? equippable.PublicName : item.PublicName), stamp) }; }
                catch { result = result with { Name = Missing<HudText>(HudValueState.Unreadable, stamp) }; }
                if (slot == HudEquipmentSlot.Tool)
                    result = result with { Deployment = Known(new HudDeployment(deployed ? HudDeploymentKind.Deployed : HudDeploymentKind.Carried), stamp) };
                if (deployed)
                {
                    var sentry = item.TryCast<SentryGunInstance>();
                    if (sentry != null)
                    {
                        try { result = result with { SupplyRatio = Ratio(sentry.Ammo, sentry.AmmoMaxCap, stamp) }; }
                        catch { result = result with { SupplyRatio = Missing<HudQuantity>(HudValueState.Unreadable, stamp) }; }
                    }
                    return result;
                }
                if (slot is HudEquipmentSlot.Melee or HudEquipmentSlot.Carry || equippable == null) return result;
                var weapon = item.TryCast<BulletWeapon>();
                if (weapon != null)
                {
                    try { result = result with { Magazine = Count(weapon.GetCurrentClip(), HudUnit.Rounds, stamp) }; }
                    catch { result = result with { Magazine = Missing<HudQuantity>(HudValueState.Unreadable, stamp) }; }
                }
                if (equippable.AmmoType == AmmoType.None) return result;
                if (storage == null) return MissingAmmo(result, stamp);
                var a = storage.GetInventorySlotAmmo(equippable.AmmoType);
                if (a == null) return MissingAmmo(result, stamp);
                if (!float.IsFinite(a.CostOfBullet) || a.CostOfBullet <= 0 || !float.IsFinite(a.AmmoInPack) || a.AmmoInPack < 0)
                    return MissingAmmo(result, stamp, HudValueState.Invalid);
                if (slot is HudEquipmentSlot.ResourcePack or HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy)
                {
                    // ResourcePackFirstPerson.ApplyPack consumes one native "bullet" per use and checks this same getter.
                    try
                    {
                        var count = storage.GetBulletsInPack(equippable.AmmoType);
                        return slot == HudEquipmentSlot.ResourcePack
                            ? result with { Uses = Count(count, HudUnit.Uses, stamp) }
                            : result with { ItemCount = Count(count, HudUnit.Items, stamp) };
                    }
                    catch { return MissingAmmo(result, stamp); }
                }
                if (weapon == null && slot != HudEquipmentSlot.Tool) return result;
                if (weapon != null)
                {
                    try { result = result with { ReserveRounds = Count(storage.GetBulletsInPack(equippable.AmmoType), HudUnit.Rounds, stamp) }; }
                    catch { result = result with { ReserveRounds = Missing<HudQuantity>(HudValueState.Unreadable, stamp) }; }
                }
                try
                {
                    float clip = weapon != null ? weapon.GetCurrentClip() : storage.GetClipAmmoFromSlot(gameSlot);
                    var supply = float.IsFinite(clip) && clip >= 0 && clip <= int.MaxValue && clip == MathF.Truncate(clip)
                        && HudNumberRules.TrySupplyRatio(a.AmmoInPack, (int)clip, a.CostOfBullet, a.AmmoMaxCap, out var ratio)
                        ? Known(ratio, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp);
                    result = result with { SupplyRatio = supply };
                }
                catch { result = result with { SupplyRatio = Missing<HudQuantity>(HudValueState.Unreadable, stamp) }; }
                return result;
            }
            catch { return Absent(slot, HudValueState.Unreadable, playerStamp); }
        }
        static HudEquipmentSnapshot MissingAmmo(HudEquipmentSnapshot result, HudValueStamp stamp, HudValueState state = HudValueState.Unreadable)
        {
            if (result.Slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary) return result with { ReserveRounds = Missing<HudQuantity>(state, stamp), SupplyRatio = Missing<HudQuantity>(state, stamp) };
            if (result.Slot == HudEquipmentSlot.ResourcePack) return result with { Uses = Missing<HudQuantity>(state, stamp) };
            if (result.Slot is HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy) return result with { ItemCount = Missing<HudQuantity>(state, stamp) };
            return result with { SupplyRatio = Missing<HudQuantity>(state, stamp) };
        }
        static HudEquipmentSnapshot Absent(HudEquipmentSlot slot, HudValueState state, HudValueStamp stamp) => new(slot,
            Missing<HudEquipmentIdentity>(state, stamp), Missing<HudText>(state, stamp), Missing<HudQuantity>(state, stamp),
            Missing<HudQuantity>(state, stamp), Missing<HudQuantity>(state, stamp), Missing<HudQuantity>(state, stamp),
            Missing<HudQuantity>(state, stamp), Missing<HudDeployment>(state, stamp));
        static HudValue<T> Known<T>(T value, HudValueStamp stamp) where T : struct, IHudContentValue => HudValue<T>.FromValue(value, stamp);
        static HudValue<T> Missing<T>(HudValueState state, HudValueStamp stamp) where T : struct, IHudContentValue => HudValue<T>.Missing(state, stamp);
        static HudValue<HudQuantity> Count(int count, HudUnit unit, HudValueStamp stamp) => HudNumberRules.TryCount(count, unit, out var q)
            ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp);
        static HudValue<HudQuantity> Ratio(double amount, double maximum, HudValueStamp stamp) => HudNumberRules.TryHealth(amount, maximum, out var q)
            ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp);
        internal static HudEquipmentSlot Map(InventorySlot slot) => slot switch
        {
            InventorySlot.GearMelee => HudEquipmentSlot.Melee, InventorySlot.GearStandard => HudEquipmentSlot.Primary,
            InventorySlot.GearSpecial => HudEquipmentSlot.Secondary, InventorySlot.GearClass => HudEquipmentSlot.Tool,
            InventorySlot.ResourcePack => HudEquipmentSlot.ResourcePack, InventorySlot.Consumable => HudEquipmentSlot.Consumable,
            InventorySlot.ConsumableHeavy => HudEquipmentSlot.Heavy, InventorySlot.InLevelCarry => HudEquipmentSlot.Carry,
            InventorySlot.None => HudEquipmentSlot.EmptyHands, _ => HudEquipmentSlot.Unknown
        };
    }
}
