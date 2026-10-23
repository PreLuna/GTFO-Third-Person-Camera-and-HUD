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
        { internal Renderer Source;internal MeshRenderer Target;internal SkinnedMeshRenderer Skin;internal Mesh Baked; }
        readonly List<Part> parts=new();
        readonly List<GameObject> ownedObjects=new();
        GameObject root;
        ItemEquippable item;
        internal int PartCount=>parts.Count;
        internal long ItemIdentity=>item==null?0:item.Pointer.ToInt64();
        internal void Bind(ItemEquippable current)
        {
            if(current==null)throw new InvalidOperationException("本机装备尚未就绪");
            if(current.Pointer.ToInt64()==ItemIdentity)return;
            Dispose();item=current;
            try
            {
                root=new GameObject("PreLuna.ThirdPerson.WorldWeapon"){hideFlags=HideFlags.HideAndDontSave};
                ownedObjects.Add(root);ResourceLedger.Create(OwnedResource.VisualObject);
                root.SetActive(false);
                foreach(var source in item.GetComponentsInChildren<Renderer>(true))
                {
                    var skin=source.TryCast<SkinnedMeshRenderer>();var meshRenderer=source.TryCast<MeshRenderer>();
                    if(skin==null&&meshRenderer==null)continue;
                    var mesh=skin!=null?skin.sharedMesh:source.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null)continue;
                    var node=new GameObject("ThirdPerson.VisualPart"){hideFlags=HideFlags.HideAndDontSave,layer=LayerManager.LAYER_THIRD_PERSON_ITEM};
                    ownedObjects.Add(node);ResourceLedger.Create(OwnedResource.VisualObject);
                    node.transform.SetParent(root.transform,false);
                    var filter=node.AddComponent<MeshFilter>();var target=node.AddComponent<MeshRenderer>();
                    target.sharedMaterials=source.sharedMaterials;target.shadowCastingMode=ShadowCastingMode.On;
                    var part=new Part{Source=source,Target=target,Skin=skin};parts.Add(part);
                    if(skin!=null){part.Baked=new Mesh{name="ThirdPerson.BakedWeapon"};ResourceLedger.Create(OwnedResource.Mesh);part.Baked.MarkDynamic();filter.sharedMesh=part.Baked;}
                    else filter.sharedMesh=mesh;
                }
                if(parts.Count==0)throw new InvalidOperationException("当前装备没有可复用的武器图形");
            }
            catch{Dispose();throw;}
        }
        internal void Show(LocalPlayerAgent player,bool visible)
        {
            if(root==null)return;
            if(!visible){if(root.activeSelf)root.SetActive(false);return;}
            var align=player.PlayerSyncModel?.ItemAlign;
            if(align==null||item==null)throw new InvalidOperationException("本机世界武器连接点失效");
            var origin=item.transform;
            root.transform.localScale=origin.lossyScale;
            var grip=item.RightHandGripTrans;
            // ItemAlign is a hand attachment. The weapon root is not the grip:
            // match the relative grip pose, without calling CalcRightHandGripOffset
            // on the live item (that would move the real first-person weapon).
            if(grip!=null)
            {
                var relativeRotation=Quaternion.Inverse(origin.rotation)*grip.rotation;
                var rotation=align.rotation*Quaternion.Inverse(relativeRotation);
                var offset=Vector3.Scale(origin.InverseTransformPoint(grip.position),root.transform.localScale);
                root.transform.SetPositionAndRotation(align.position-rotation*offset,rotation);
            }
            else root.transform.SetPositionAndRotation(align.position,align.rotation);
            foreach(var p in parts)
            {
                if(p.Source==null){p.Target.enabled=false;continue;}
                var source=p.Source.transform;var target=p.Target.transform;
                target.localPosition=origin.InverseTransformPoint(source.position);
                target.localRotation=Quaternion.Inverse(origin.rotation)*source.rotation;
                var a=source.lossyScale;var b=origin.lossyScale;
                target.localScale=new Vector3(SafeRatio(a.x,b.x),SafeRatio(a.y,b.y),SafeRatio(a.z,b.z));
                p.Target.enabled=p.Source.enabled&&p.Source.gameObject.activeInHierarchy;
                if(p.Skin!=null&&p.Target.enabled)p.Skin.BakeMesh(p.Baked);
            }
            if(!root.activeSelf)root.SetActive(true);
        }
        static float SafeRatio(float a,float b)=>Math.Abs(b)>.00001f?a/b:1;
        public void Dispose()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>{parts.Clear();ownedObjects.Clear();root=null;item=null;});
            foreach(var node in ownedObjects)cleanup.Add(()=>{if(node!=null)UnityEngine.Object.Destroy(node);ResourceLedger.Release(OwnedResource.VisualObject);});
            foreach(var p in parts)if(p.Baked!=null)cleanup.Add(()=>{UnityEngine.Object.Destroy(p.Baked);ResourceLedger.Release(OwnedResource.Mesh);});
            cleanup.Add(()=>{if(root!=null)root.SetActive(false);});
        }
    }
}
