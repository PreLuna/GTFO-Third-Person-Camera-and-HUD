using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Player;
using UnityEngine;
using UnityEngine.Rendering;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    // Graphics-only proxy. No item/player components, physics, lights or network identity.
    internal sealed class WorldWeaponView : IDisposable
    {
        sealed class Part
        { internal Renderer Source;internal MeshRenderer Target;internal MeshFilter Filter;internal SkinnedMeshRenderer Skin;internal Mesh Baked; }
        readonly List<Part> parts=new();
        readonly List<GameObject> ownedObjects=new();
        GameObject root;
        ItemEquippable item;
        Transform gripAnchor;
        float nextRefresh;
        internal int LastVisibleParts {get;private set;}
        internal float GripPositionError {get;private set;}
        internal float GripRotationError {get;private set;}
        internal int PartCount=>parts.Count;
        internal bool HasDrawableParts=>parts.Exists(p=>p.Source!=null&&p.Source.enabled&&p.Source.gameObject.activeInHierarchy);
        internal bool HasRightGrip=>item!=null&&item.RightHandGripTrans!=null;
        internal long ItemIdentity=>item==null?0:item.Pointer.ToInt64();
        internal Transform AimReference=>root==null?null:root.transform;
        internal ItemEquippable VisualSource=>item;
        internal bool Bind(ItemEquippable current,Transform anchor=null)
        {
            if(current==null)throw new InvalidOperationException("本机装备尚未就绪");
            bool sameAnchor=anchor==null?gripAnchor==null:gripAnchor!=null&&gripAnchor.Pointer==anchor.Pointer;
            if(current.Pointer.ToInt64()!=ItemIdentity||!sameAnchor){Dispose();item=current;gripAnchor=anchor;}
            else if(Time.unscaledTime<nextRefresh)return parts.Count>0;
            nextRefresh=Time.unscaledTime+.5f;
            try
            {
                if(root==null)
                {
                    root=new GameObject("PreLuna.ThirdPerson.WorldWeapon"){hideFlags=HideFlags.HideAndDontSave};
                    ownedObjects.Add(root);ResourceLedger.Create(OwnedResource.VisualObject);
                }
                var liveSources=new HashSet<long>();
                foreach(var source in item.GetComponentsInChildren<Renderer>(true))
                {
                    var skin=source.TryCast<SkinnedMeshRenderer>();var meshRenderer=source.TryCast<MeshRenderer>();
                    if(skin==null&&meshRenderer==null)continue;
                    var mesh=skin!=null?skin.sharedMesh:source.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null)continue;
                    liveSources.Add(source.Pointer.ToInt64());
                    var existing=parts.Find(p=>p.Source!=null&&p.Source.Pointer==source.Pointer);
                    if(existing!=null)
                    {
                        existing.Target.sharedMaterials=source.sharedMaterials;
                        if(skin==null)existing.Filter.sharedMesh=mesh;
                        continue;
                    }
                    var node=new GameObject("ThirdPerson.VisualPart"){hideFlags=HideFlags.HideAndDontSave,layer=LayerManager.LAYER_THIRD_PERSON_ITEM};
                    ownedObjects.Add(node);ResourceLedger.Create(OwnedResource.VisualObject);
                    node.transform.SetParent(root.transform,false);
                    var filter=node.AddComponent<MeshFilter>();var target=node.AddComponent<MeshRenderer>();
                    target.forceRenderingOff=true;
                    target.sharedMaterials=source.sharedMaterials;target.shadowCastingMode=ShadowCastingMode.On;
                    var part=new Part{Source=source,Target=target,Filter=filter,Skin=skin};parts.Add(part);
                    if(skin!=null){part.Baked=new Mesh{name="ThirdPerson.BakedWeapon"};ResourceLedger.Create(OwnedResource.Mesh);part.Baked.MarkDynamic();filter.sharedMesh=part.Baked;}
                    else filter.sharedMesh=mesh;
                }
                foreach(var stale in parts.FindAll(p=>p.Source==null||!liveSources.Contains(p.Source.Pointer.ToInt64())))
                {
                    if(stale.Target!=null)
                    {
                        SetVisible(stale.Target,false);ownedObjects.Remove(stale.Target.gameObject);
                        UnityEngine.Object.Destroy(stale.Target.gameObject);ResourceLedger.Release(OwnedResource.VisualObject);
                    }
                    if(stale.Baked!=null){UnityEngine.Object.Destroy(stale.Baked);ResourceLedger.Release(OwnedResource.Mesh);}
                    parts.Remove(stale);
                }
                // Gear generation is asynchronous. Keep retrying the same item,
                // rather than permanently caching an incomplete first enumeration.
                return parts.Count>0;
            }
            catch{Dispose();throw;}
        }
        internal void Show(LocalPlayerAgent player,bool visible)
        {
            if(root==null)return;
            // Camera passes only change drawing. Keep the owned mesh renderers
            // registered while the binding is live.
            if(!visible){HideParts();return;}
            UpdatePose(player);
            var origin=item.transform;
            LastVisibleParts=0;
            foreach(var p in parts)
            {
                if(p.Target==null)continue;
                if(p.Source==null){SetVisible(p.Target,false);continue;}
                var source=p.Source.transform;var target=p.Target.transform;
                target.localPosition=origin.InverseTransformPoint(source.position);
                target.localRotation=Quaternion.Inverse(origin.rotation)*source.rotation;
                var a=source.lossyScale;var b=origin.lossyScale;
                target.localScale=new Vector3(SafeRatio(a.x,b.x),SafeRatio(a.y,b.y),SafeRatio(a.z,b.z));
                bool draw=p.Source.enabled&&p.Source.gameObject.activeInHierarchy;
                if(draw)
                {
                    if(p.Skin!=null)p.Skin.BakeMesh(p.Baked);
                    LastVisibleParts++;
                }
                SetVisible(p.Target,draw);
            }
        }
        void HideParts()
        {
            foreach(var part in parts)if(part.Target!=null)SetVisible(part.Target,false);
        }
        static void SetVisible(Renderer renderer,bool visible)
        {
            bool off=!visible;
            if(renderer.forceRenderingOff!=off)renderer.forceRenderingOff=off;
        }
        internal void UpdatePose(LocalPlayerAgent player)
        {
            if(root==null)return;
            var align=gripAnchor??player.PlayerSyncModel?.ItemAlign;
            if(align==null||item==null)throw new InvalidOperationException("本机世界武器连接点失效");
            var origin=item.transform;
            // Match the native synced equipment parent. The owned graphics root
            // now follows hand/spine rotations during the native aim solver too.
            if(root.transform.parent==null||root.transform.parent.Pointer!=align.Pointer)root.transform.SetParent(align,false);
            var scale=origin.lossyScale;var parentScale=align.lossyScale;
            root.transform.localScale=new Vector3(SafeRatio(scale.x,parentScale.x),SafeRatio(scale.y,parentScale.y),SafeRatio(scale.z,parentScale.z));
            var grip=item.RightHandGripTrans;
            // ItemAlign is a hand attachment. The weapon root is not the grip:
            // match the relative grip pose, without calling CalcRightHandGripOffset
            // on the live item (that would move the real first-person weapon).
            if(grip!=null)
            {
                var relativeRotation=Quaternion.Inverse(origin.rotation)*grip.rotation;
                var rotation=align.rotation*Quaternion.Inverse(relativeRotation);
                var offset=Vector3.Scale(origin.InverseTransformPoint(grip.position),scale);
                root.transform.SetPositionAndRotation(align.position-rotation*offset,rotation);
            }
            else root.transform.SetPositionAndRotation(align.position,align.rotation);
            GripPositionError=grip==null?0:(root.transform.TransformPoint(origin.InverseTransformPoint(grip.position))-align.position).magnitude;
            GripRotationError=grip==null?0:Quaternion.Angle(root.transform.rotation*Quaternion.Inverse(origin.rotation)*grip.rotation,align.rotation);
        }
        internal bool LeftGrip(out Vector3 position,out Quaternion rotation)
        {
            position=default;rotation=Quaternion.identity;
            var grip=item?.LeftHandGripTrans;
            if(root==null||grip==null||!grip.IsChildOf(item.transform))return false;
            position=root.transform.TransformPoint(item.transform.InverseTransformPoint(grip.position));
            rotation=root.transform.rotation*Quaternion.Inverse(item.transform.rotation)*grip.rotation;
            return float.IsFinite(position.x)&&float.IsFinite(position.y)&&float.IsFinite(position.z)
                &&float.IsFinite(rotation.x)&&float.IsFinite(rotation.y)&&float.IsFinite(rotation.z)&&float.IsFinite(rotation.w);
        }
        static float SafeRatio(float a,float b)=>Math.Abs(b)>.00001f?a/b:1;
        public void Dispose()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>{parts.Clear();ownedObjects.Clear();root=null;item=null;gripAnchor=null;nextRefresh=0;LastVisibleParts=0;GripPositionError=GripRotationError=0;});
            foreach(var node in ownedObjects)cleanup.Add(()=>{if(node!=null)UnityEngine.Object.Destroy(node);ResourceLedger.Release(OwnedResource.VisualObject);});
            foreach(var p in parts)if(p.Baked!=null)cleanup.Add(()=>{UnityEngine.Object.Destroy(p.Baked);ResourceLedger.Release(OwnedResource.Mesh);});
            cleanup.Add(HideParts);
        }
    }
}
