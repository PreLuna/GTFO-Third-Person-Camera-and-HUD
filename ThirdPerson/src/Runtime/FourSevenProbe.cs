using System;
using System.Collections.Generic;
using GameData;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Body;

namespace PreLuna.ThirdPerson.Runtime
{
    // M1 read-only diagnostic surface. Called only from the explicit F8 snapshot.
    // It must not bind, show, hide, enable, warm up or otherwise change the item/body.
    internal static class FourSevenProbe
    {
        internal static object Capture(
            LocalPlayerAgent local,
            ItemEquippable item,
            FirstPersonLayerView firstPerson,
            WorldWeaponView weapon,
            LocalBodyView body,
            NativeBodyHolding nativeHolding)
        {
            try
            {
                if (local == null)
                    return new {available = false, reason = "not-in-level-or-no-local-player"};

                var data = item?.ItemDataBlock;
                var model = local.PlayerSyncModel;
                var gearCategory = item?.GearCategoryData;
                var slot = item?.pItemData.slot;
                var dataSlot = data?.inventorySlot;
                var effectiveSlot = slot.HasValue && slot.Value != InventorySlot.None ? slot : dataSlot;
                var choice = SelectAnchor(model, effectiveSlot ?? InventorySlot.None);

                var melee = item == null ? null : item.TryCast<Gear.MeleeWeaponFirstPerson>();
                object meleeState = null;
                if (melee != null)
                {
                    var sync = melee.m_lastSyncData;
                    meleeState = new
                    {
                        state = melee.CurrentStateName.ToString(),
                        inAimMode = sync.inAimMode,
                        inFireMode = sync.inFireMode,
                        inCooldown = sync.inCooldown,
                        syncTime = melee.m_lastSyncTime
                    };
                }

                return new
                {
                    available = true,
                    probeSchema = 1,
                    readOnly = true,
                    frame = Time.frameCount,
                    category = Category(effectiveSlot),
                    player = new
                    {
                        ptr = Id(local),
                        owner = Id(local.Owner),
                        model = Id(model),
                        locomotion = local.Locomotion == null ? null : local.Locomotion.m_currentStateEnum.ToString()
                    },
                    equipment = new
                    {
                        present = item != null,
                        ptr = Id(item),
                        name = item == null ? null : item.name,
                        nativeType = item == null ? null : item.GetType().FullName,
                        isBulletWeapon = item != null && item.TryCast<Gear.BulletWeapon>() != null,
                        isMeleeWeapon = melee != null,
                        isReloading = item != null && item.IsReloading,
                        isWielded = item != null && item.m_isWielded,
                        isFirstPerson = item != null && item.m_isFirstPerson,
                        slot = slot?.ToString(),
                        slotValue = slot == null ? (int?)null : (int)slot.Value,
                        dataSlot = dataSlot?.ToString(),
                        dataSlotValue = dataSlot == null ? (int?)null : (int)dataSlot.Value
                    },
                    itemData = data == null ? null : new
                    {
                        id = data.persistentID,
                        name = data.name,
                        publicName = data.publicName,
                        slot = data.inventorySlot.ToString(),
                        slotValue = (int)data.inventorySlot,
                        fpsSettings = data.FPSSettings,
                        thirdPersonPrefabCount = Count(data.ThirdPersonPrefabs),
                        thirdPersonPrefabs = Prefabs(data.ThirdPersonPrefabs),
                        firstPersonPrefabCount = Count(data.FirstPersonPrefabs),
                        movementSet = data.ThirdPersonFullbodyMovementSet.ToString(),
                        leftGripAlign = data.LeftHandGripAlign,
                        leftGripAnim = data.LeftHandGripAnim,
                        rightGripAlign = data.RightHandGripAlign,
                        rightGripAnim = data.RightHandGripAnim,
                        backpackAlign = data.BackpackAlign,
                        muzzleAlign = data.MuzzleAlign,
                        sightLookAlign = data.SightLookAlign
                    },
                    runtimeGrips = item == null ? null : new
                    {
                        leftAlign = Anchor(item.m_leftHandGripAlign),
                        rightAlign = Anchor(item.m_rightHandGripAlign),
                        leftAnim = item.m_leftHandGripAnim,
                        rightAnim = item.m_rightHandGripAnim
                    },
                    anchors = model == null ? null : new
                    {
                        item = Anchor(model.ItemAlign),
                        gearLight = Anchor(model.m_alignGearLight),
                        gearMedium = Anchor(model.m_alignGearMedium),
                        gearHeavy = Anchor(model.m_alignGearHeavy),
                        gearMelee = Anchor(model.m_alignGearMelee),
                        resourcePack = Anchor(model.m_alignResourcePack),
                        consumableLight = Anchor(model.m_alignConsumableLight),
                        consumableMedium = Anchor(model.m_alignConsumableMedium),
                        consumableHeavy = Anchor(model.m_alignConsumableHeavy),
                        carryItem = Anchor(model.m_alignCarryItem)
                    },
                    anchorSelection = new
                    {
                        category = Category(effectiveSlot),
                        chosen = Anchor(choice.Anchor),
                        chosenName = choice.Anchor == null ? null : choice.Anchor.name,
                        chosenBy = choice.By
                    },
                    gearCategory = gearCategory == null ? null : new
                    {
                        id = gearCategory.persistentID,
                        name = gearCategory.name,
                        baseItem = gearCategory.BaseItem,
                        movementSet = gearCategory.ThirdPersonFullbodyMovement.ToString(),
                        fpsArmPose = gearCategory.FPSArmPoseName,
                        partAlignPriority = PartAlign(gearCategory.PartAlignPriority)
                    },
                    renderers = new
                    {
                        item = CountRenderers(item == null ? null : item.gameObject),
                        fpsArmsModel = CountRenderers(local.FPItemHolder?.FPSArms?.m_modelRoot),
                        fpsArmsGfx = CountGfxArms(local.FPItemHolder?.FPSArms?.m_gfxArms),
                        uniqueVisible = CountUnique(item == null ? null : item.gameObject, local.FPItemHolder?.FPSArms?.m_modelRoot),
                        firstPersonView = firstPerson?.RendererCount ?? 0,
                        worldWeaponParts = weapon?.PartCount ?? 0,
                        body = body?.RendererCount ?? 0
                    },
                    triggers = local.FPItemHolder == null ? null : new
                    {
                        melee = local.FPItemHolder.ItemMeleeTrigger,
                        aim = local.FPItemHolder.ItemAimTrigger,
                        busy = local.FPItemHolder.ItemIsBusy
                    },
                    melee = meleeState,
                    nativeHolding = nativeHolding?.Diagnostics(),
                    surface = "M1-read-only: equipment/slot/itemData/anchors/renderer-counts; no bind/show/action changes"
                };
            }
            catch (Exception error)
            {
                return new
                {
                    available = false,
                    probeSchema = 1,
                    readOnly = true,
                    error = error.GetType().Name + ": " + error.Message,
                    surface = "M1-read-only probe failed; no state was intentionally changed"
                };
            }
        }

        static string Category(InventorySlot? slot)
        {
            if (slot == null) return "none";
            return slot.Value switch
            {
                InventorySlot.GearStandard => "firearm-primary-frozen-2/3",
                InventorySlot.GearSpecial => "firearm-secondary-frozen-2/3",
                InventorySlot.GearMelee => "melee",
                InventorySlot.GearClass => "gear-tool",
                InventorySlot.ResourcePack => "resource-pack",
                InventorySlot.Consumable => "consumable",
                InventorySlot.ConsumableHeavy => "consumable-heavy",
                InventorySlot.InLevelCarry => "carry-item",
                InventorySlot.HackingTool => "hacking-tool",
                _ => slot.Value.ToString()
            };
        }

        static (Transform Anchor, string By) SelectAnchor(PlayerSyncModelData model, InventorySlot slot)
        {
            if (model == null) return (null, "no-player-model");
            return slot switch
            {
                InventorySlot.GearStandard => (model.ItemAlign, "native-item-align"),
                InventorySlot.GearSpecial => (model.ItemAlign, "native-item-align"),
                InventorySlot.GearMelee => (model.m_alignGearMelee, "melee-slot"),
                InventorySlot.GearClass => (First(model.m_alignGearLight, model.m_alignGearMedium, model.m_alignGearHeavy), "fallback-gear-slot-order"),
                InventorySlot.ResourcePack => (model.m_alignResourcePack, "resource-pack-slot"),
                InventorySlot.Consumable => (First(model.m_alignConsumableLight, model.m_alignConsumableMedium, model.m_alignConsumableHeavy), "fallback-consumable-slot-order"),
                InventorySlot.ConsumableHeavy => (First(model.m_alignConsumableHeavy, model.m_alignConsumableMedium, model.m_alignConsumableLight), "fallback-consumable-slot-order"),
                InventorySlot.HackingTool => (First(model.m_alignGearLight, model.m_alignGearMedium, model.m_alignGearHeavy), "fallback-hacking-tool"),
                InventorySlot.InLevelCarry => (model.m_alignCarryItem, "carry-item-slot"),
                _ => (null, "no-candidate")
            };
        }

        static string[] PartAlign(Il2CppSystem.Collections.Generic.List<GearPartAlignPriority> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<string>();
            int count = Math.Min(list.Count, 16);
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                var entry = list[i];
                var priorities = entry.PartPrio;
                var parts = new List<string>();
                if (priorities != null)
                    for (int j = 0; j < priorities.Count; j++) parts.Add(priorities[j].ToString());
                result[i] = entry.AlignType + ":" + string.Join(",", parts);
            }
            return result;
        }

        static Transform First(params Transform[] candidates)
        {
            foreach (var candidate in candidates)
                if (candidate != null)
                    return candidate;
            return null;
        }

        static object Anchor(Transform transform)
        {
            if (transform == null) return null;
            return new
            {
                name = transform.name,
                active = transform.gameObject.activeInHierarchy,
                childCount = transform.childCount,
                ptr = transform.Pointer.ToInt64()
            };
        }

        static int Count(Il2CppSystem.Collections.Generic.List<string> list)
        {
            if (list == null) return 0;
            return list.Count;
        }

        static string[] Prefabs(Il2CppSystem.Collections.Generic.List<string> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<string>();
            int count = Math.Min(list.Count, 8);
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = list[i];
            return result;
        }

        static int CountRenderers(GameObject root)
        {
            if (root == null) return 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers == null ? 0 : renderers.Length;
        }

        static int CountGfxArms(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> gfxArms)
        {
            if (gfxArms == null) return 0;
            int count = 0;
            for (int i = 0; i < gfxArms.Count; i++) count += CountRenderers(gfxArms[i]);
            return count;
        }

        static int CountUnique(GameObject item, GameObject armsModel)
        {
            var seen = new HashSet<long>();
            AddUnique(seen, item);
            AddUnique(seen, armsModel);
            return seen.Count;
        }

        static void AddUnique(HashSet<long> seen, GameObject root)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;
            foreach (var renderer in renderers)
                if (renderer != null)
                    seen.Add(renderer.Pointer.ToInt64());
        }

        static long Id(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
            => value == null ? 0 : value.Pointer.ToInt64();

        static long Id(PlayerAgent value)
            => value == null ? 0 : value.Pointer.ToInt64();

        static long Id(PlayerSyncModelData value)
            => value == null ? 0 : value.Pointer.ToInt64();
    }
}

