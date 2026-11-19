using System;
using System.Collections.Generic;
using AssetShards;
using GameData;
using Player;
using UnityEngine;

namespace PreLuna.ThirdPerson.Body
{
    // M2a static melee spike: graphics-only clone of the native third-person prefab.
    // No item script, collider, rigidbody, network identity or Animator is instantiated.
    // It does not drive body animation or IK yet; the native body locomotion continues.
    internal sealed class MeleeSpikeView : IDisposable
    {
        readonly List<string> materials = new();
        GameObject root;
        Transform anchor;
        int partCount;
        string status = "not-bound";
        string lastStatus = "not-bound";
        string sourcePath;
        string lastSourcePath;

        internal bool Active => root != null;
        internal int PartCount => partCount;
        internal string Status => status;
        internal string LastStatus => lastStatus;

        internal bool Bind(LocalPlayerAgent player, ItemEquippable item)
        {
            Release();
            try
            {
                var data = item?.ItemDataBlock;
                var model = player?.PlayerSyncModel;
                if (data == null || model == null || item == null) return Fail("no-item-data");
                if (item.pItemData.slot != InventorySlot.GearMelee || data.inventorySlot != InventorySlot.GearMelee)
                    return Fail("not-melee-slot");
                anchor = model.m_alignGearMelee;
                if (anchor == null) return Fail("melee-anchor-missing");
                var prefabs = data.ThirdPersonPrefabs;
                if (prefabs == null || prefabs.Count == 0) return Fail("native-prefab-list-empty");

                GameObject prefab = null;
                for (int i = 0; i < prefabs.Count && prefab == null; i++)
                {
                    var path = prefabs[i];
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    try { prefab = AssetShardManager.GetLoadedAsset<GameObject>(path.ToUpperInvariant()); }
                    catch { prefab = null; }
                    if (prefab != null) sourcePath = path;
                }
                if (prefab == null) return Fail("native-prefab-not-loaded");

                root = new GameObject("PreLuna.MeleeSpike") { hideFlags = HideFlags.HideAndDontSave, layer = LayerManager.LAYER_THIRD_PERSON_ITEM };
                root.transform.SetParent(anchor, false);
                root.transform.localPosition = prefab.transform.localPosition;
                root.transform.localRotation = prefab.transform.localRotation;
                root.transform.localScale = prefab.transform.localScale;
                partCount = CloneHierarchy(prefab.transform, root.transform);
                if (partCount == 0)
                {
                    Release();
                    return Fail("native-prefab-has-no-mesh-parts");
                }
                if (partCount < 2 || PlaceholderMaterials())
                {
                    // Authored third-person prefabs often keep their renderers disabled
                    // and assign real materials only through their item script. Cloning
                    // that shell shows one white placeholder part, so use the live FPS
                    // item's graphics as the M2a fallback until native setup is proven.
                    var targetAnchor = anchor;
                    Release();
                    anchor = targetAnchor;
                    return BindFpsFallback(item);
                }
                root.SetActive(false);
                status = "native-prefab-graphics-only";
                lastStatus = status;
                lastSourcePath = sourcePath;
                return true;
            }
            catch (Exception error)
            {
                Release();
                return Fail("bind-failed:" + error.GetType().Name + ":" + error.Message);
            }
        }

        bool BindFpsFallback(ItemEquippable item)
        {
            try
            {
                if (anchor == null || item == null) return Fail("fps-fallback-no-anchor-or-item");
                root = new GameObject("PreLuna.MeleeSpike.FpsFallback") { hideFlags = HideFlags.HideAndDontSave, layer = LayerManager.LAYER_THIRD_PERSON_ITEM };
                root.transform.SetParent(anchor, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                partCount = CloneItemHierarchy(item.transform, root.transform);
                if (partCount == 0)
                {
                    Release();
                    return Fail("fps-fallback-has-no-mesh-parts");
                }
                root.SetActive(false);
                sourcePath = "FPS:" + item.name;
                status = "fps-item-graphics-fallback";
                lastStatus = status;
                lastSourcePath = sourcePath;
                return true;
            }
            catch (Exception error)
            {
                Release();
                return Fail("fps-fallback-failed:" + error.GetType().Name + ":" + error.Message);
            }
        }

        int CloneItemHierarchy(Transform source, Transform target)
        {
            int count = CopyAnyRenderer(source.gameObject, target.gameObject) ? 1 : 0;
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                if (child == null) continue;
                var go = new GameObject(child.name) { layer = LayerManager.LAYER_THIRD_PERSON_ITEM };
                go.transform.SetParent(target, false);
                go.transform.localPosition = child.localPosition;
                go.transform.localRotation = child.localRotation;
                go.transform.localScale = child.localScale;
                go.SetActive(child.gameObject.activeSelf);
                count += CloneItemHierarchy(child, go.transform);
            }
            return count;
        }

        bool CopyAnyRenderer(GameObject source, GameObject target)
        {
            if (CopyRenderer(source, target)) return true;
            var skin = source.GetComponent<SkinnedMeshRenderer>();
            if (skin == null || skin.sharedMesh == null) return false;
            var filter = target.AddComponent<MeshFilter>();
            filter.sharedMesh = skin.sharedMesh;
            var renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = skin.sharedMaterials;
            renderer.shadowCastingMode = skin.shadowCastingMode;
            renderer.receiveShadows = skin.receiveShadows;
            renderer.enabled = true;
            var block = new MaterialPropertyBlock();
            skin.GetPropertyBlock(block);
            renderer.SetPropertyBlock(block);
            RecordMaterials(skin.sharedMaterials);
            return true;
        }

        bool PlaceholderMaterials()
        {
            if (materials.Count == 0) return true;
            foreach (var material in materials)
                if (!material.StartsWith("No Name:", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        void RecordMaterials(UnityEngine.Material[] shared)
        {
            if (shared == null) return;
            foreach (var material in shared)
                if (material != null && materials.Count < 8)
                    materials.Add(material.name + ":" + (material.shader == null ? "null" : material.shader.name));
        }

        bool Fail(string reason)
        {
            status = reason;
            lastStatus = reason;
            lastSourcePath = sourcePath;
            return false;
        }

        int CloneHierarchy(Transform source, Transform target)
        {
            int count = CopyRenderer(source.gameObject, target.gameObject) ? 1 : 0;
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                if (child == null) continue;
                var go = new GameObject(child.name) { layer = LayerManager.LAYER_THIRD_PERSON_ITEM };
                go.transform.SetParent(target, false);
                go.transform.localPosition = child.localPosition;
                go.transform.localRotation = child.localRotation;
                go.transform.localScale = child.localScale;
                go.SetActive(child.gameObject.activeSelf);
                count += CloneHierarchy(child, go.transform);
            }
            return count;
        }

        bool CopyRenderer(GameObject source, GameObject target)
        {
            var renderer = source.GetComponent<MeshRenderer>();
            var filter = source.GetComponent<MeshFilter>();
            if (renderer == null || filter == null || filter.sharedMesh == null) return false;
            var targetFilter = target.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = filter.sharedMesh;
            var targetRenderer = target.AddComponent<MeshRenderer>();
            targetRenderer.sharedMaterials = renderer.sharedMaterials;
            targetRenderer.shadowCastingMode = renderer.shadowCastingMode;
            targetRenderer.receiveShadows = renderer.receiveShadows;
            targetRenderer.enabled = true;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            targetRenderer.SetPropertyBlock(block);
            RecordMaterials(renderer.sharedMaterials);
            return true;
        }

        internal void Show(bool visible)
        {
            if (root != null) root.SetActive(visible);
        }

        internal object Diagnostics() => new
        {
            active = Active,
            status,
            lastStatus,
            sourcePath,
            lastSourcePath,
            parts = partCount,
            materials,
            anchor = anchor == null ? null : anchor.name
        };

        internal void Release()
        {
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
            anchor = null;
            partCount = 0;
            sourcePath = null;
            materials.Clear();
            status = "released";
        }

        public void Dispose() => Release();
    }
}
