using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    // Applied only inside the main camera scope; auxiliary cameras see original materials.
    internal sealed class LocalBodyOcclusion : IDisposable
    {
        sealed class Entry
        {
            internal Renderer Renderer;
            internal Material[] Original,Transparent;
            internal Color[] Colors;
            internal bool Applied;
        }
        readonly Dictionary<long,Entry> entries=new();
        internal int TransparentRenderers {get;private set;}
        internal int HiddenRenderers {get;private set;}
        internal int PreservedRenderers {get;private set;}
        Entry Get(Renderer renderer)
        {
            long id=renderer.Pointer.ToInt64();
            if(entries.TryGetValue(id,out var entry))return entry;
            var materials=renderer.sharedMaterials;
            entry=new Entry{Renderer=renderer};entries.Add(id,entry);
            bool standard=materials.Length>0;
            foreach(var m in materials)standard&=m!=null&&m.shader?.name=="Standard"&&m.HasProperty("_Color")&&m.HasProperty("_Mode");
            if(!standard)return entry;
            entry.Original=new Material[materials.Length];entry.Transparent=new Material[materials.Length];entry.Colors=new Color[materials.Length];
            for(int i=0;i<materials.Length;i++)
            {
                var source=materials[i];entry.Original[i]=source;entry.Colors[i]=source.GetColor("_Color");
                var copy=new Material(source){name="PreLuna.ViewFade",hideFlags=HideFlags.HideAndDontSave};entry.Transparent[i]=copy;
                ResourceLedger.Create(OwnedResource.Material);
                copy.SetFloat("_Mode",2);copy.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);copy.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);copy.SetInt("_ZWrite",0);
                copy.DisableKeyword("_ALPHATEST_ON");copy.EnableKeyword("_ALPHABLEND_ON");copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");copy.renderQueue=(int)RenderQueue.Transparent;
            }
            return entry;
        }
        internal void Begin(){TransparentRenderers=0;HiddenRenderers=0;PreservedRenderers=0;}
        internal void Apply(Renderer renderer,Vector3 camera,bool enabled,float near,float far)
        {
            if(!enabled||renderer==null)return;
            float clearance=(renderer.bounds.ClosestPoint(camera)-camera).magnitude;
            var entry=Get(renderer);
            // A bounds test cannot identify the obstructing triangles. On GTFO shaders
            // hiding the entire renderer removes the torso/legs, not a local patch.
            // Keep unsupported materials opaque, even with an existing fade=true CFG.
            if(entry.Transparent==null){PreservedRenderers++;return;}
            var decision=BodyOcclusionPolicy.Evaluate(clearance,near,far,false);
            float opacity=decision.Opacity;
            if(entry.Transparent!=null&&opacity<.999f)
            {
                for(int i=0;i<entry.Transparent.Length;i++){var color=entry.Colors[i];color.a*=opacity;entry.Transparent[i].SetColor("_Color",color);}
                renderer.sharedMaterials=entry.Transparent;entry.Applied=true;TransparentRenderers++;
            }
        }
        internal void Restore()
        {
            using var recovery=new RestoreJournal();
            foreach(var entry in entries.Values)
            {
                if(!entry.Applied)continue;
                recovery.Add(()=>{if(entry.Renderer!=null)entry.Renderer.sharedMaterials=entry.Original;entry.Applied=false;});
            }
        }
        public void Dispose()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>entries.Clear());
            foreach(var entry in entries.Values)if(entry.Transparent!=null)foreach(var m in entry.Transparent)if(m!=null)cleanup.Add(()=>{UnityEngine.Object.Destroy(m);ResourceLedger.Release(OwnedResource.Material);});
            cleanup.Add(Restore);
        }
    }
}
