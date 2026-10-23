#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Gear;
using Player;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Runtime;

static class GameCollectionTests
{
    sealed record Result(string Name, bool Passed, string? Error);
    static readonly List<Result> results = new();
    static void Check(bool b, string message = "assertion failed") { if (!b) throw new Exception(message); }
    sealed class Fixture
    {
        public readonly LocalPlayerAgent Local = new() { Owner = new() };
        public readonly PlayerBackpack Bag;
        public readonly BulletWeapon Main, Special;
        public readonly HudGameContentSource Source;
        public HudRuntimeContext Current;
        public double Now = 10;
        public HudContentCaptureRequest Request => new(new(Current.CurrentView, 1, Current.FrameNumber, Now), 1, 1);
        public ObjectIdentity Key(UnityEngine.Object item) => new(item.Pointer.ToInt64(), item.GetInstanceID());
        public Fixture()
        {
            LevelGeneration.Builder.CurrentFloor = new();
            Local.Damage = new() { Owner = Local }; Local.Inventory = new() { Owner = Local, WieldedSlot = InventorySlot.GearStandard };
            Bag = new() { Owner = Local.Owner }; PlayerManager.Local = Local; PlayerBackpackManager.Bag = Bag;
            Main = new() { Owner = Local, AmmoType = AmmoType.Standard, Clip = 10, Name = "主武器" };
            Special = new() { Owner = Local, AmmoType = AmmoType.Special, Clip = 0, Name = "副武器" };
            Put(InventorySlot.GearStandard, Main); Put(InventorySlot.GearSpecial, Special);
            Local.Inventory.WieldedItem = Main;
            Bag.AmmoStorage!.Ammo[AmmoType.Standard] = new() { AmmoInPack = 40, CostOfBullet = 2, AmmoMaxCap = 100, NativeCount = 20 };
            Bag.AmmoStorage.Ammo[AmmoType.Special] = new() { AmmoInPack = 0, CostOfBullet = 4, AmmoMaxCap = 100, NativeCount = 0 };
            var player = new PlayerIdentity(Key(Local), Local.Owner!.Pointer.ToInt64(), 0);
            var binding = new LocalViewBinding(player, Key(Local.PlayerSyncModel!), Key(Local.FPSCamera!.m_camera!), LevelGeneration.Builder.CurrentFloor.Pointer.ToInt64(), 0);
            var view = new ThirdPersonViewState(1, new(Guid.NewGuid(), 1), binding, player, 1, 1, ViewSource.LocalThirdPerson, ThirdPersonMode.Exploration, ViewAvailability.Ready);
            Current = new(true, true, true, view.Context, binding, view, 100, Now, default);
            Source = new(() => Now, () => Current with { NowSeconds = Now });
        }
        public void Put(InventorySlot slot, Item item) => Bag.Items[slot] = new() { Instance = item };
        public HudContentSnapshot Capture() { Check(Source.TryCapture(Request, out var s), "capture failed"); return s; }
        public double Read(HudContentSnapshot snapshot, HudValue<HudQuantity> field, ObjectIdentity owner, HudInformationId information)
        {
            Check(HudContentRules.TryReadQuantity(Current with { NowSeconds = Now }, snapshot.Header, field, owner, information, 1, 1, HudContentPolicy.Default, out var q, out var state), "read failed: " + state);
            return q.Amount;
        }
    }
    static void Case(string name, Action<Fixture> action)
    { try { action(new()); results.Add(new(name, true, null)); } catch (Exception e) { results.Add(new(name, false, e.ToString())); } }
    static void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-6, $"{a} != {b}");
    static int Main(string[] args)
    {
        Case("actual-health-and-infection", f => { var s = f.Capture(); Near(f.Read(s, s.Personal.Health, f.Key(f.Local), HudInformationId.Health), .5); Near(f.Read(s, s.Personal.Infection, f.Key(f.Local), HudInformationId.Infection), .1); });
        Case("damage-owner-mismatch-is-unreadable", f => { f.Local.Damage!.Owner = new(); var s = f.Capture(); Check(s.Personal.Health.State == HudValueState.Unreadable && s.Personal.Primary.Magazine.State == HudValueState.Available); });
        Case("zero-life-is-known-not-missing", f => { f.Local.Damage!.CurrentHealth = 0; var s = f.Capture(); Near(f.Read(s, s.Personal.Health, f.Key(f.Local), HudInformationId.Health), 0); Check(s.Personal.Life.RawValue.Kind == HudLifeKind.Alive); });
        Case("modified-max-and-overcap-are-preserved", f => { f.Local.Damage!.CurrentHealth = 60; var s = f.Capture(); Near(f.Read(s, s.Personal.Health, f.Key(f.Local), HudInformationId.Health), 1.2); });
        Case("invalid-maximum-does-not-become-zero", f => { f.Local.Damage!.HealthMax = 0; Check(f.Capture().Personal.Health.State == HudValueState.Invalid); });
        Case("invalid-infection-does-not-break-weapon", f => { f.Local.Damage!.Infection = float.NaN; var s = f.Capture(); Check(s.Personal.Infection.State == HudValueState.Invalid && s.Personal.Primary.Magazine.State == HudValueState.Available); });
        Case("health-read-failure-preserves-infection", f => { f.Local.Damage!.ThrowHealth = true; var s = f.Capture(); Check(s.Personal.Health.State == HudValueState.Unreadable && s.Personal.Infection.State == HudValueState.Available); });
        Case("main-special-and-resource-units", f => { var s = f.Capture(); Near(f.Read(s, s.Personal.Primary.Magazine, f.Key(f.Main), HudInformationId.Magazine), 10); Near(f.Read(s, s.Personal.Primary.ReserveRounds, f.Key(f.Main), HudInformationId.ReserveRounds), 20); Near(f.Read(s, s.Personal.Primary.SupplyRatio, f.Key(f.Main), HudInformationId.SupplyRatio), .6); Near(f.Read(s, s.Personal.Secondary.ReserveRounds, f.Key(f.Special), HudInformationId.ReserveRounds), 0); });
        Case("reserve-uses-authoritative-native-integer", f => { f.Bag.AmmoStorage!.Ammo[AmmoType.Standard].NativeCount = 7; var s = f.Capture(); Near(f.Read(s, s.Personal.Primary.ReserveRounds, f.Key(f.Main), HudInformationId.ReserveRounds), 7); });
        Case("weapon-switch-updates-selected-slot", f => { f.Local.Inventory!.WieldedSlot = InventorySlot.GearSpecial; f.Local.Inventory.WieldedItem = f.Special; Check(f.Capture().Personal.Selected.RawValue.Slot == HudEquipmentSlot.Secondary); });
        Case("empty-hands-is-explicit", f => { f.Local.Inventory!.WieldedSlot = InventorySlot.None; f.Local.Inventory.WieldedItem = null; Check(f.Capture().Personal.Selected.RawValue.Slot == HudEquipmentSlot.EmptyHands); });
        Case("transitional-missing-wielded-item-is-unreadable", f => { f.Local.Inventory!.WieldedItem = null; Check(f.Capture().Personal.Selected.State == HudValueState.Unreadable); });
        Case("slot-removal-is-not-equipped", f => { f.Bag.Items.Remove(InventorySlot.GearStandard); var s = f.Capture(); Check(s.Personal.Primary.Identity.State == HudValueState.NotEquipped && s.Personal.Primary.Magazine.State == HudValueState.NotEquipped); });
        Case("unloaded-entry-is-unreadable", f => { f.Bag.Items[InventorySlot.GearStandard].Instance = null; Check(f.Capture().Personal.Primary.Identity.State == HudValueState.Unreadable); });
        Case("melee-has-no-fictional-ammo", f => { f.Put(InventorySlot.GearMelee, new ItemEquippable()); var s = f.Capture(); Check(s.Personal.Melee.Identity.State == HudValueState.Available && s.Personal.Melee.Magazine.State == HudValueState.Unsupported); });
        Case("special-nonfirearm-does-not-fake-magazine", f => { f.Put(InventorySlot.GearSpecial, new ItemEquippable()); Check(f.Capture().Personal.Secondary.Magazine.State == HudValueState.Unsupported); });
        Case("clip-read-failure-is-partial", f => { f.Main.ThrowClip = true; var s = f.Capture(); Check(s.Personal.Primary.Magazine.State == HudValueState.Unreadable && s.Personal.Health.State == HudValueState.Available); });
        Case("clip-read-failure-preserves-reserve", f => { f.Main.ThrowClip = true; var s = f.Capture(); Check(s.Personal.Primary.ReserveRounds.State == HudValueState.Available && s.Personal.Primary.Identity.State == HudValueState.Available); });
        Case("wrong-owner-in-owned-backpack-is-unreadable", f => { f.Main.Owner = new(); Check(f.Capture().Personal.Primary.Identity.State == HudValueState.Unreadable); });
        Case("carried-tool-total-resource", f => { var tool = new ItemEquippable { AmmoType = AmmoType.Class }; f.Put(InventorySlot.GearClass, tool); f.Bag.AmmoStorage!.Ammo[AmmoType.Class] = new() { AmmoInPack = 20, CostOfBullet = 2, AmmoMaxCap = 100 }; f.Bag.AmmoStorage.Clips[InventorySlot.GearClass] = 5; var s = f.Capture(); Near(f.Read(s, s.Personal.Tool.SupplyRatio, f.Key(tool), HudInformationId.ToolResource), .3); Check(s.Personal.Tool.Deployment.RawValue.Kind == HudDeploymentKind.Carried); });
        Case("tool-with-no-resource-concept-is-unsupported", f => { f.Put(InventorySlot.GearClass, new ItemEquippable { AmmoType = AmmoType.None }); Check(f.Capture().Personal.Tool.SupplyRatio.State == HudValueState.Unsupported); });
        Case("deployed-sentry-resource-and-identity", f => { var sentry = new SentryGunInstance { Owner = f.Local, Ammo = 15, AmmoMaxCap = 30 }; f.Bag.DeployedFlag = true; f.Bag.Deployed[InventorySlot.GearClass] = sentry; var s = f.Capture(); Check(s.Personal.Tool.Identity.RawValue.Instance == f.Key(sentry) && s.Personal.Tool.Deployment.RawValue.Kind == HudDeploymentKind.Deployed); Near(f.Read(s, s.Personal.Tool.SupplyRatio, f.Key(sentry), HudInformationId.ToolResource), .5); });
        Case("deployed-object-missing-does-not-mean-no-tool", f => { f.Bag.DeployedFlag = true; var s = f.Capture(); Check(s.Personal.Tool.Identity.State == HudValueState.Unreadable && s.Personal.Tool.Deployment.State == HudValueState.Available); });
        Case("deployed-wrong-owner-is-rejected", f => { f.Bag.DeployedFlag = true; f.Bag.Deployed[InventorySlot.GearClass] = new SentryGunInstance { Owner = new() }; Check(f.Capture().Personal.Tool.Identity.State == HudValueState.Unreadable); });
        Case("resource-pack-uses-native-use-count", f => { var pack = new ItemEquippable { AmmoType = AmmoType.ResourcePackRel }; f.Put(InventorySlot.ResourcePack, pack); f.Bag.AmmoStorage!.Ammo[AmmoType.ResourcePackRel] = new() { AmmoInPack = .6f, CostOfBullet = .2f, AmmoMaxCap = 1, NativeCount = 3 }; var s = f.Capture(); Near(f.Read(s, s.Personal.ResourcePack.Uses, f.Key(pack), HudInformationId.ResourcePackUses), 3); });
        Case("consumable-zero-and-heavy-count", f => { var item = new ItemEquippable { AmmoType = AmmoType.CurrentConsumable }; f.Put(InventorySlot.Consumable, item); f.Bag.AmmoStorage!.Ammo[AmmoType.CurrentConsumable] = new() { CostOfBullet = 1, AmmoMaxCap = 4, NativeCount = 0 }; var s = f.Capture(); Near(f.Read(s, s.Personal.Consumable.ItemCount, f.Key(item), HudInformationId.ConsumableCount), 0); f.Put(InventorySlot.ConsumableHeavy, item); Check(f.Capture().Personal.Heavy.ItemCount.State == HudValueState.Available); });
        Case("carried-object-has-name-and-no-fake-count", f => { var carry = new Item { Name = "任务物品" }; f.Put(InventorySlot.InLevelCarry, carry); var s = f.Capture(); Check(s.Personal.Carry.Name.RawValue.Text == "任务物品" && s.Personal.Carry.ItemCount.State == HudValueState.Unsupported); });
        Case("missing-backpack-preserves-life", f => { PlayerBackpackManager.Bag = null; var s = f.Capture(); Check(s.Personal.Health.State == HudValueState.Available && s.Personal.Primary.Identity.State == HudValueState.Unreadable); });
        Case("missing-storage-preserves-equipment", f => { f.Bag.AmmoStorage = null; var s = f.Capture(); Check(s.Personal.Primary.Identity.State == HudValueState.Available && s.Personal.Primary.Magazine.State == HudValueState.Available && s.Personal.Primary.ReserveRounds.State == HudValueState.Unreadable); });
        Case("invalid-resource-cost-is-not-empty", f => { f.Bag.AmmoStorage!.Ammo[AmmoType.Standard].CostOfBullet = 0; Check(f.Capture().Personal.Primary.ReserveRounds.State == HudValueState.Invalid); });
        Case("native-resource-read-failure-preserves-other-slots", f => { f.Bag.AmmoStorage!.ThrowCount = true; var s = f.Capture(); Check(s.Personal.Primary.ReserveRounds.State == HudValueState.Unreadable && s.Personal.Health.State == HudValueState.Available); });
        Case("native-count-failure-preserves-known-resource-ratio", f => { f.Bag.AmmoStorage!.ThrowCount = true; Check(f.Capture().Personal.Primary.SupplyRatio.State == HudValueState.Available); });
        Case("off-main-capture-and-signals-do-nothing", f => { f.Capture(); long version = f.Source.ChangeVersion; int reads = PlayerManager.Reads; Task.Run(() => { Check(!f.Source.TryCapture(f.Request, out _)); f.Source.Notify(f.Local.Damage!.Pointer.ToInt64(), HudContentChange.Health); }).GetAwaiter().GetResult(); Check(PlayerManager.Reads == reads && f.Source.ChangeVersion == version); });
        Case("only-local-native-receivers-mark-dirty", f => { f.Capture(); long v = f.Source.ChangeVersion; f.Source.Notify(123456, HudContentChange.Ammo); Check(f.Source.ChangeVersion == v); f.Source.Notify(f.Main.Pointer.ToInt64(), HudContentChange.Ammo); f.Source.Notify(f.Local.FPItemHolder!.Pointer.ToInt64(), HudContentChange.Equipment); f.Source.Notify(f.Local.Inventory!.Pointer.ToInt64(), HudContentChange.Reload); f.Source.Notify(f.Local.Damage!.Pointer.ToInt64(), HudContentChange.Health); Check(f.Source.ChangeVersion == v + 4); });
        Case("capture-rejects-rebuilt-player-even-in-same-slot", f => { PlayerManager.Local = new() { Owner = f.Local.Owner }; Check(!f.Source.TryCapture(f.Request, out _)); });
        Case("capture-rejects-mid-read-player-replacement", f => { f.Main.OnName = () => PlayerManager.Local = new() { Owner = f.Local.Owner }; Check(!f.Source.TryCapture(f.Request, out _)); });
        Case("capture-rejects-mid-read-backpack-replacement", f => { f.Main.OnName = () => PlayerBackpackManager.Bag = new() { Owner = f.Local.Owner }; Check(!f.Source.TryCapture(f.Request, out _)); });
        Case("capture-rejects-mid-read-wield-switch", f => { f.Main.OnName = () => { f.Local.Inventory!.WieldedItem = f.Special; f.Local.Inventory.WieldedSlot = InventorySlot.GearSpecial; }; Check(!f.Source.TryCapture(f.Request, out _)); });
        Case("downed-state-remains-separate-from-health", f => { f.Local.Alive = false; f.Local.Locomotion!.m_currentStateEnum = PlayerLocomotion.PLOC_State.Downed; Check(f.Capture().Personal.Life.RawValue.Kind == HudLifeKind.Downed); });
        Case("out-of-level-context-rejects-without-player-reads", f => { f.Current = f.Current with { InLevel = false }; int n = PlayerManager.Reads; Check(!f.Source.TryCapture(f.Request, out _) && PlayerManager.Reads == n); });
        Case("first-person-and-control-are-not-collected", f => { f.Current = f.Current with { LocalBodyViewActive = false }; Check(!f.Source.TryCapture(f.Request, out _)); f.Current = f.Current with { LocalBodyViewActive = true, CurrentView = f.Current.CurrentView with { Source = ViewSource.LocalFirstPerson } }; Check(!f.Source.TryCapture(f.Request, out _)); });
        Case("native-reload-state-without-fake-progress", f => { f.Main.IsReloading=true;var a=f.Capture().Personal.Action;Check(a.Action.RawValue.Kind==HudActionKind.Reloading&&a.Progress.State==HudValueState.Unsupported&&a.Equipment==f.Key(f.Main));f.Main.IsReloading=false;Check(f.Capture().Personal.Action.Action.RawValue.Kind==HudActionKind.Idle); });
        Case("melee-charge-is-authoritative-state", f => {var item=new MeleeWeaponFirstPerson{Owner=f.Local,CurrentStateName=eMeleeWeaponState.AttackChargeUpRight};f.Put(InventorySlot.GearMelee,item);f.Local.Inventory!.WieldedSlot=InventorySlot.GearMelee;f.Local.Inventory.WieldedItem=item;Check(f.Capture().Personal.Action.Action.RawValue.Kind==HudActionKind.MeleeCharging);item.CurrentStateName=eMeleeWeaponState.AttackChargeReleaseLeft;Check(f.Capture().Personal.Action.Action.RawValue.Kind==HudActionKind.Idle);});
        Case("pack-native-timer-and-local-interactor", f => {var pack=new ResourcePackFirstPerson{Owner=f.Local,m_interactApplyResource=new(){TimerIsActive=true,InteractionTimerRel=.6f,m_localPlayerInteractInfo=new(){Agent=f.Local}}};f.Put(InventorySlot.ResourcePack,pack);f.Local.Inventory!.WieldedSlot=InventorySlot.ResourcePack;f.Local.Inventory.WieldedItem=pack;var a=f.Capture().Personal.Action;Check(a.Action.RawValue.Kind==HudActionKind.UsingItem);Near(a.Progress.RawValue.Amount,.6f);pack.m_interactApplyResource.TimerIsActive=false;Check(f.Capture().Personal.Action.Action.RawValue.Kind==HudActionKind.Idle);});
        Case("pack-other-interactor-does-not-become-local-use", f => {var pack=new ResourcePackFirstPerson{Owner=f.Local,m_interactApplyResource=new(){TimerIsActive=true,m_localPlayerInteractInfo=new(){Agent=new()}}};f.Local.Inventory!.WieldedItem=pack;Check(f.Capture().Personal.Action.Action.State==HudValueState.Unreadable);});
        Case("action-without-item-owner-is-unreadable", f => {f.Main.Owner=null;Check(f.Capture().Personal.Action.Action.State==HudValueState.Unreadable);});
        Case("unsupported-consumable-action-is-not-assumed-idle", f => {f.Local.Inventory!.WieldedSlot=InventorySlot.Consumable;f.Local.Inventory.WieldedItem=new(){Owner=f.Local};Check(f.Capture().Personal.Action.Action.State==HudValueState.Unsupported);});
        Case("only-local-hit-signal-recorded-and-marked-dirty", f => {f.Capture();long v=f.Source.ChangeVersion;f.Source.NotifyHit(999999,5,System.Numerics.Vector3.UnitX);Check(f.Source.ChangeVersion==v);f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,System.Numerics.Vector3.UnitX);var s=f.Capture();Check(f.Source.ChangeVersion==v+1&&s.Personal.Hit.RawValue.TowardSource.ToVector()==-System.Numerics.Vector3.UnitX);});
        Case("hit-time-is-never-refreshed-by-capture", f => {f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,default);f.Now+=.3;var s=f.Capture();Near(s.Personal.Hit.RawValue.OccurredAtSeconds,10);Check(s.Personal.Hit.Stamp.CapturedAtSeconds>10);});
        Case("new-hit-coalesces-into-single-latest-event", f => {for(int i=0;i<1000;i++)f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,System.Numerics.Vector3.UnitX);Check(f.Capture().Personal.Hit.RawValue.Sequence==1000);});
        Case("session-cancel-clears-latest-native-event", f => {f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,default);f.Source.ClearTransient();Check(f.Capture().Personal.Hit.State==HudValueState.Unknown);});
        Case("old-hit-is-cleared-on-eligibility-change", f => {f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,default);f.Current=f.Current with{CurrentView=f.Current.CurrentView with{EligibilityEpoch=2}};Check(f.Capture().Personal.Hit.State==HudValueState.Unknown);});
        Case("ineligible-and-off-main-hit-signals-cannot-leak", f => {f.Current=f.Current with{LocalBodyViewActive=false};f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,default);f.Current=f.Current with{LocalBodyViewActive=true};int reads=PlayerManager.Reads;Task.Run(()=>f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,default)).GetAwaiter().GetResult();Check(PlayerManager.Reads==reads&&f.Capture().Personal.Hit.State==HudValueState.Unknown);});
        Case("zero-damage-ignored-invalid-direction-remains-unknown", f => {f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),0,default);Check(f.Capture().Personal.Hit.State==HudValueState.Unknown);f.Source.NotifyHit(f.Local.Damage!.Pointer.ToInt64(),5,new(float.NaN,0,0));Check(f.Capture().Personal.Hit.RawValue.TowardSource.ToVector()==default(System.Numerics.Vector3));});
        bool passed = results.All(r => r.Passed);
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = passed, Count = results.Count, Scope = "production collector with offline game doubles", NativeGameExecuted = false, RuntimeRenderingVerified = false, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var r in results.Where(r => !r.Passed)) Console.WriteLine(r.Name + ": " + r.Error);
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {results.Count} game collection cases"); return passed ? 0 : 1;
    }
}
