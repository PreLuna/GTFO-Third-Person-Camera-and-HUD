#nullable enable
using System;
using System.Collections.Generic;
using PreLuna.ThirdPerson.Contracts;

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        static long next = 10;
        public IntPtr Pointer { get; set; } = new(++next);
    }
}
namespace Il2CppInterop.Runtime
{
    public static class CastMock { public static T? TryCast<T>(this Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value) where T : class => value as T; }
}
namespace UnityEngine
{
    public class GameObject : Object { public bool activeInHierarchy=true; }
    public class Renderer : Object { public bool forceRenderingOff; public bool enabled=true; }
    public class SpriteRenderer : Renderer { }
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public int InstanceId = 1;
        public int GetInstanceID() => InstanceId;
        GameObject? ownObject;Transform? ownTransform;
        public GameObject gameObject=>this as GameObject??(ownObject??=new());
        public Transform transform=>ownTransform??=new();
    }
    public class Transform {public Vector3 position;}
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
}
namespace TMPro
{ public class TextMeshPro : UnityEngine.Object { public string text=""; public UnityEngine.Renderer renderer=new();public new UnityEngine.GameObject gameObject=new(); } }
public class PlayerGuiLayer
{public PUI_Inventory? Inventory;public PUI_LocalPlayerStatus? m_playerStatus;public Player.PlayerAgent? m_player;public PUI_GameObjectives? m_wardenObjective;public PUI_ObjectiveTimer? m_objectiveTimer;public bool TimerOnMainScreenVisible;}
public class GuiManager {static PlayerGuiLayer? player;public static bool ThrowPlayerLayer;public static PlayerGuiLayer? PlayerLayer{get=>ThrowPlayerLayer?throw new Exception("GUI unavailable"):player;set=>player=value;}public static GuiManager? Current;public SyncedNavMarkerWrapper?[] m_playerPings=new SyncedNavMarkerWrapper?[4];}
public class PUI_GameObjectives : UnityEngine.Object
{public const int MAIN_OBJECTIVE_ID=1;public bool m_isCurrentLayer=true,ShowPreviousObjectives;public Dictionary<int,PUI_ProgressionObjective> m_progressionObjectiveMap=new();}
public class PUI_ProgressionObjective : UnityEngine.Object {public TMPro.TextMeshPro m_header=new(),m_text=new();}
public class PUI_ObjectiveTimer : UnityEngine.Object {public bool Visible=true;public TMPro.TextMeshPro m_timerText=new(),m_titleText=new();}
public class SyncedNavMarkerWrapper : UnityEngine.Object {public int m_playerIndex;public NavMarker? m_marker;public bool Visible=true;public bool NavMarkerIsVisible()=>Visible;}
public class PUI_LocalPlayerStatus
{public UnityEngine.SpriteRenderer m_health1=new(),m_health2=new(),m_infection1=new(),m_infection2=new();public TMPro.TextMeshPro m_healthText=new(),m_infectionText=new();public UnityEngine.SpriteRenderer m_air=new(),m_warning=new(),m_shield1=new();}
public class PUI_Inventory
{public SNetwork.SNet_Player? m_owner;public readonly Dictionary<Player.InventorySlot,PUI_InventoryItem> m_inventorySlots=new();}
public class PUI_InventoryItem
{
    public bool ShowAmmoClip=true,ShowAmmoPack=true,ShowAmmoTotalRel,ShowAmmoInfinite,IsDeployed;
    public TMPro.TextMeshPro m_selected_ammoClip=new(),m_selected_ammoTotal=new(),m_slim_ammoTotal=new(),m_selected_ammoCap=new(),m_selected_ammoPack=new();
}
namespace SNetwork
{
    public enum ConnectionStatus { NotConnected, Connecting, ConnectedDirect, ConnectedTroughRelay }
    public sealed class SNet_Player : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    { public ulong Lookup; public string NickName="队员"; public bool IsBot, HasTimedOut, IsOutOfSync, IsInGame=true; public ConnectionStatus ConnectionStatus=ConnectionStatus.ConnectedDirect; }
    public class SNet_PlayerSlotManager { public readonly SNet_Player?[] Players=new SNet_Player?[4]; public SNet_Player? GetPlayerInSlot(int slot)=>Players[slot]; }
    public static class SNet { public static SNet_PlayerSlotManager? Slots; }
}
public class PlaceNavMarkerOnGO : UnityEngine.Object
{public Player.PlayerAgent? Player;public NavMarker? m_marker;public bool ExtraInfoVisible,m_beingDestroyed;}
public class NavMarker : UnityEngine.Object {public TMPro.TextMeshPro m_playerName=new();public UnityEngine.Transform? TrackingTrans=new();public bool IsVisible=true,m_isInPlayerDimension=true;}
namespace LevelGeneration { public static class Builder { public static UnityEngine.Object? CurrentFloor; } }
namespace PreLuna.ThirdPerson
{
    public static class ThirdPersonApi
    {
        public static double MonotonicSeconds;
        public static bool TryGetRuntimeContext(out HudRuntimeContext current) { current = default; return false; }
    }
}
public sealed class FPSCamera { public UnityEngine.Object? m_camera; }
public sealed class FirstPersonItemHolder : UnityEngine.Object { }
public class Item : UnityEngine.Object
{
    public Player.PlayerAgent? Owner;
    public string Name = "装备";
    public Action? OnName;
    public virtual string PublicName { get { OnName?.Invoke(); return Name; } }
}
public class ItemEquippable : Item { public Player.AmmoType AmmoType = Player.AmmoType.None; public bool IsReloading; }
public enum InputAction { Interact,Fire,AltFire }
public class InputMapper
{public static InputMapper? Current;public object? m_currentKeyMouseMap=new();public string Binding="E";public Action? OnBinding;public string DoGetBindingName(InputAction a,object map){OnBinding?.Invoke();return Binding;}}
public class PlayerInteraction : UnityEngine.Object
{public Player.LocalPlayerAgent? m_owner;public Interact_Base? m_bestSelectedInteract;public static bool InteractionEnabled=true;}
public class Interact_Base : UnityEngine.Object {public bool IsSelected=true,IsActive=true,IsBlocked;public InputAction InputAction;}
public class Interact_Generic : Interact_Base {public string m_displayName="门";}
public class Interact_Pickup_PickupItem : Interact_Timed {public Func<string>? m_nameFunc;}
public class Interact_Revive : Interact_Timed {public string m_displayName="队员";}
public class Interact_Timed : Interact_Base
{
    public sealed class InteractorInfo { public Player.PlayerAgent? Agent; }
    public bool TimerIsActive;
    public float InteractionTimerRel;
    public string InteractionMessage="长按操作";
    public InteractorInfo? m_localPlayerInteractInfo;
}
public class Interact_ManualTimedWithCallback : Interact_Timed { }
public class SentryGunInstance : ItemEquippable { public float Ammo, AmmoMaxCap; }
namespace Gear
{
    public enum eMeleeWeaponState { Idle, AttackChargeUpLeft, AttackChargeUpRight, AttackChargeReleaseLeft }
    public class MeleeWeaponFirstPerson : ItemEquippable { public eMeleeWeaponState CurrentStateName; }
    public class ResourcePackFirstPerson : ItemEquippable { public Interact_ManualTimedWithCallback? m_interactApplyResource;public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? m_actionReceiver; }
    public class EnemyScanner : ItemEquippable {public List<Enemies.EnemyAgent> m_enemiesDetected=new();}
    public class BulletWeapon : ItemEquippable
    {
        public int Clip;
        public bool ThrowClip;
        public int GetCurrentClip() => ThrowClip ? throw new Exception("clip unavailable") : Clip;
    }
}
public class Dam_PlayerDamageBase : UnityEngine.Object
{
    public Player.PlayerAgent? Owner;
    public float CurrentHealth = 25, HealthMax = 50, Infection = .1f;
    public bool ThrowHealth;
    public float Health => ThrowHealth ? throw new Exception("health unavailable") : CurrentHealth;
}
public class PlayerInventoryBase : UnityEngine.Object
{
    public Player.PlayerAgent? Owner;
    public Player.InventorySlot WieldedSlot;
    public ItemEquippable? WieldedItem;
}
namespace Player
{
    public enum InventorySlot { None, GearStandard, GearSpecial, GearClass, ResourcePack, Consumable, ConsumableHeavy, InPocket, InLevelCarry, Pickup, GearMelee, HackingTool }
    public enum AmmoType { Standard, Special, Class, ResourcePackRel, None, CurrentConsumable }
    public class PlayerLocomotion { public enum PLOC_State { Stand, Downed }; public PLOC_State m_currentStateEnum; }
    public class PlayerAgent : UnityEngine.Object
    {
        public bool IsBeingDestroyed,IsBeingDespawned,IsLocallyOwned;
        public PlayerSync? Sync=new();public PlaceNavMarkerOnGO? NavMarker;
        public SNetwork.SNet_Player? Owner;
        public int PlayerSlotIndex, DimensionIndex;
        public UnityEngine.Vector3 Position;public PlayerInteraction? Interaction;
        public bool Alive = true;
        public PlayerLocomotion? Locomotion = new();
        public Dam_PlayerDamageBase? Damage;
        public PlayerInventoryBase? Inventory;
        public FirstPersonItemHolder? FPItemHolder = new();
        public UnityEngine.Object? PlayerSyncModel = new();
        public FPSCamera? FPSCamera = new() { m_camera = new() };
    }
    public sealed class LocalPlayerAgent : PlayerAgent { }
    public class PlayerSync : UnityEngine.Object { public PlayerAgent? m_agent;public pPlayerLocomotion m_locomotionData; }
    public struct pPlayerLocomotion { public PlayerLocomotion.PLOC_State State; }
    public sealed class BackpackItem { public Item? Instance; }
    public sealed class InventorySlotAmmo : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public float AmmoInPack, AmmoMaxCap, CostOfBullet;
        public int NativeCount;
        public float RelInPack;
    }
    public sealed class PlayerAmmoStorage : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly Dictionary<AmmoType, InventorySlotAmmo> Ammo = new();
        public readonly Dictionary<InventorySlot, float> Clips = new();
        public bool ThrowCount;
        public int CountReads;
        public InventorySlotAmmo? GetInventorySlotAmmo(AmmoType type) => Ammo.GetValueOrDefault(type);
        public int GetBulletsInPack(AmmoType type) { CountReads++; return ThrowCount ? throw new Exception("count unavailable") : Ammo[type].NativeCount; }
        public float GetClipAmmoFromSlot(InventorySlot slot) => Clips.GetValueOrDefault(slot);
    }
    public sealed class PlayerBackpack : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public SNetwork.SNet_Player? Owner;
        public PlayerAmmoStorage? AmmoStorage = new();
        public readonly Dictionary<InventorySlot, BackpackItem> Items = new();
        public readonly Dictionary<InventorySlot, Item> Deployed = new();
        public bool DeployedFlag;
        public bool IsDeployed(InventorySlot slot) => DeployedFlag;
        public bool TryGetDeployedItem(InventorySlot slot, out Item? item) => Deployed.TryGetValue(slot, out item);
        public bool TryGetBackpackItem(InventorySlot slot, out BackpackItem? item) => Items.TryGetValue(slot, out item);
    }
}
public class PlayerManager
{
    public static PlayerManager? Current;
    public readonly Player.PlayerAgent?[] Slots=new Player.PlayerAgent?[4];
    public Player.PlayerAgent? GetPlayerAgentInSlot(int slot)=>Slots[slot];
    public static Player.LocalPlayerAgent? Local;
    public static int Reads;
    public static Player.PlayerAgent? GetLocalPlayerAgent() { Reads++; return Local; }
}
public static class PlayerBackpackManager
{
    public static readonly Dictionary<long,Player.PlayerBackpack> Bags=new();
    public static Player.PlayerBackpack? Bag;
    public static bool TryGetBackpack(SNetwork.SNet_Player owner, out Player.PlayerBackpack? bag) { bag = Bags.GetValueOrDefault(owner.Pointer.ToInt64())??Bag; return bag != null; }
}
