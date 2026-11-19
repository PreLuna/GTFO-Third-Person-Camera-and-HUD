using System;
using System.Collections.Generic;
using Gear;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    // Experimental Plan A mirror for non-firearm local equipment. The game
    // assembles the ThirdPerson item; this view never registers it in inventory.
    internal sealed class NativeThirdPersonEquipmentView : IDisposable
    {
        LocalPlayerAgent player;
        ItemEquippable source;
        ItemEquippable assembled;
        Transform anchor;
        GearManager.delGearAssembled callback;
        long sourceId, playerId;
        int generation, rendererCount, colliderCount, animatorCount;
        bool resourceTracked, copySource;
        float copyRequestedAt;
        string status = "idle", lastStatus = "idle", anchorRule = "none", assembledType = "none";
        string[] prefabs = Array.Empty<string>();
        internal bool Using => sourceId != 0;
        internal bool Ready => assembled != null;
        internal bool Failed => status.StartsWith("failed:",StringComparison.Ordinal);
        internal ItemEquippable CopySource => copySource ? assembled : null;
        internal long ItemIdentity => sourceId;
        internal int PartCount => rendererCount;
        internal int VisibleParts => Ready && assembled.gameObject.activeInHierarchy ? rendererCount : 0;
        internal string Status => status;

        internal void RejectCopy()
        {
            generation++;
            DestroyAssembled();
            Fail("copy-no-drawable-parts");
        }

        internal bool Bind(LocalPlayerAgent local, ItemEquippable item, bool forCopy=false)
        {
            if (local == null || item == null || item.ItemDataBlock == null)
                return Fail("missing-local-item-data");
            var data = item.ItemDataBlock;
            var itemSlot = item.pItemData.slot;
            if (itemSlot != data.inventorySlot || !Supported(itemSlot))
                return Fail("unsupported-or-mismatched-slot");

            if (sourceId == item.Pointer.ToInt64() && playerId == local.Pointer.ToInt64() && copySource == forCopy)
            {
                if(forCopy&&!Ready&&!Failed&&Time.unscaledTime-copyRequestedAt>=3f)
                {
                    generation++;
                    Fail("factory-callback-timeout");
                }
                return Ready;
            }

            Release();
            copySource = forCopy;
            source = item;
            player = local;
            sourceId = item.Pointer.ToInt64();
            playerId = local.Pointer.ToInt64();
            var model = local.PlayerSyncModel;
            (anchor, anchorRule) = forCopy ? (model?.ItemAlign, "copy-right-hand") : SelectAnchor(model, itemSlot);
            if (anchor == null) return Fail("category-anchor-missing");
            var names = new List<string>();
            var dataPrefabs = data.ThirdPersonPrefabs;
            if (dataPrefabs != null)
                for (int i = 0; i < dataPrefabs.Count && i < 8; i++) names.Add(dataPrefabs[i]);
            prefabs = names.ToArray();
            if (prefabs.Length == 0) return Fail("third-person-prefab-list-empty");

            status = lastStatus = "assembling-scripted-third-person-local-mirror";
            copyRequestedAt = Time.unscaledTime;
            int request = ++generation;
            long liveItemId = sourceId;
            callback = new Action<ItemEquippable>(value => Complete(request, liveItemId, value));
            try
            {
                // setupGearScript=true is required to test the native material,
                // activation and category setup path that the prefab clone lacked.
                // No owner, inventory, sync, or network registration is performed here.
                var immediate = GearBuilder.AssembleGearAsync(item.GearIDRange, ItemMode.ThirdPerson,
                    callback, false, true);
                // The return value may precede completion of native setup.
                if (immediate != null && !forCopy) Complete(request, liveItemId, immediate);
                return Ready;
            }
            catch (Exception error)
            {
                return Fail("factory-threw:" + error.GetType().Name);
            }
        }

        static bool Supported(InventorySlot slot) => slot is InventorySlot.GearMelee
            or InventorySlot.GearClass or InventorySlot.HackingTool or InventorySlot.ResourcePack
            or InventorySlot.Consumable or InventorySlot.ConsumableHeavy or InventorySlot.InLevelCarry;

        static (Transform Anchor, string Rule) SelectAnchor(PlayerSyncModelData model, InventorySlot slot)
        {
            if (model == null) return (null, "no-player-model");
            return slot switch
            {
                InventorySlot.GearMelee => (model.m_alignGearMelee, "native-melee-slot"),
                InventorySlot.GearClass => (First(model.m_alignGearLight, model.m_alignGearMedium, model.m_alignGearHeavy), "candidate-first-gear-anchor"),
                InventorySlot.HackingTool => (First(model.m_alignGearLight, model.m_alignGearMedium, model.m_alignGearHeavy), "candidate-first-gear-anchor"),
                InventorySlot.ResourcePack => (model.m_alignResourcePack, "native-resource-pack-slot"),
                InventorySlot.Consumable => (First(model.m_alignConsumableLight, model.m_alignConsumableMedium, model.m_alignConsumableHeavy), "candidate-first-consumable-anchor"),
                InventorySlot.ConsumableHeavy => (First(model.m_alignConsumableHeavy, model.m_alignConsumableMedium, model.m_alignConsumableLight), "candidate-heavy-consumable-anchor"),
                InventorySlot.InLevelCarry => (model.m_alignCarryItem, "native-carry-item-slot"),
                _ => (null, "unsupported-slot")
            };
        }

        static Transform First(params Transform[] values)
        {
            foreach (var value in values) if (value != null) return value;
            return null;
        }

        void Complete(int request, long liveItemId, ItemEquippable value)
        {
            if (value == null) { if (request == generation) Fail("factory-returned-null"); return; }
            if (value.Pointer.ToInt64() == liveItemId)
            {
                if (request == generation) Fail("factory-returned-live-item");
                return;
            }
            if (request != generation || source == null || anchor == null)
            {
                value.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(value.gameObject);
                return;
            }
            if (assembled != null)
            {
                if (assembled.Pointer != value.Pointer) UnityEngine.Object.Destroy(value.gameObject);
                return;
            }
            try
            {
                assembled = value;
                assembledType = value.GetType().FullName;
                if (!copySource)
                {
                    value.transform.SetParent(anchor, false);
                    SetLayer(value.transform, LayerManager.LAYER_THIRD_PERSON_ITEM);
                }
                foreach (var collider in value.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null) continue;
                    colliderCount++;
                    collider.enabled = false;
                }
                var renderers = value.GetComponentsInChildren<Renderer>(true);
                rendererCount = renderers.Length;
                animatorCount = value.GetComponentsInChildren<Animator>(true).Length;
                if (copySource)
                {
                    foreach (var script in value.GetComponentsInChildren<MonoBehaviour>(true))
                        if (script != null) script.enabled = false;
                    foreach (var renderer in renderers)
                        if (renderer != null) renderer.forceRenderingOff = true;
                    foreach (var light in value.GetComponentsInChildren<Light>(true))
                        if (light != null) light.enabled = false;
                    foreach (var audio in value.GetComponentsInChildren<AudioSource>(true))
                        if (audio != null) audio.enabled = false;
                    value.gameObject.SetActive(true);
                }
                else value.gameObject.SetActive(false);
                if (rendererCount == 0)
                {
                    DestroyAssembled();
                    Fail("assembled-no-renderers");
                    return;
                }
                ResourceLedger.Create(OwnedResource.VisualObject);
                resourceTracked = true;
                status = lastStatus = "assembled-local-mirror-no-owner-no-sync";
            }
            catch (Exception error)
            {
                DestroyAssembled();
                Fail("setup-threw:" + error.GetType().Name);
            }
        }

        static void SetLayer(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null) SetLayer(child, layer);
            }
        }

        internal void Show(bool visible)
        {
            if (assembled == null || copySource) return;
            if (assembled.gameObject.activeSelf != visible) assembled.gameObject.SetActive(visible);
        }

        bool Fail(string reason)
        {
            status = "failed";
            if (reason != null) status += ":" + reason;
            lastStatus = status;
            return false;
        }

        void DestroyAssembled()
        {
            if (assembled == null) return;
            var old = assembled;
            assembled = null;
            if (old != null)
            {
                old.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(old.gameObject);
            }
            if (resourceTracked)
            {
                resourceTracked = false;
                ResourceLedger.Release(OwnedResource.VisualObject);
            }
        }

        internal object Diagnostics() => new
        {
            status, lastStatus, item = sourceId, player = playerId, gearId = source == null ? null : source.GearIDRange.ToString(),
            slot = source?.ItemDataBlock == null ? null : source.ItemDataBlock.inventorySlot.ToString(),
            requestedMode = "ThirdPerson", setupGearScript = true, ownerAssigned = false, copySource,
            inventoryRegistered = false, networkSyncRequested = false,
            anchor = anchor == null ? null : anchor.name, anchorRule, prefabs, assembledType,
            renderers = rendererCount, animators = animatorCount, disabledColliders = colliderCount,
            active = assembled != null && assembled.gameObject.activeInHierarchy
        };

        public void Release()
        {
            generation++;
            DestroyAssembled();
            source = null;
            player = null;
            anchor = null;
            callback = null;
            copySource = false;
            sourceId = playerId = 0;
            rendererCount = colliderCount = animatorCount = 0;
            prefabs = Array.Empty<string>();
            assembledType = "none";
            anchorRule = "none";
            status = "idle";
        }

        public void Dispose() => Release();
    }
}
