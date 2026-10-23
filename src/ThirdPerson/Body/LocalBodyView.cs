using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Player;
using UnityEngine;
using UnityEngine.Rendering;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Body
{
    internal sealed class LocalBodyView : IDisposable
    {
        readonly RestoreJournal lifetime=new();
        readonly LocalBodyOcclusion occlusion=new();
        internal object FadeDiagnostics=>new{transparent=occlusion.TransparentRenderers,hidden=occlusion.HiddenRenderers};
        readonly List<(Renderer renderer,bool outside,ShadowCastingMode shadow)> renderers=new();
        internal int RendererCount=>renderers.Count;
        internal LocalBodyView(LocalPlayerAgent player)
        {
            var model=player.PlayerSyncModel;
            if(model==null)throw new InvalidOperationException("本机人物模型尚未生成");
            var seenParts=new HashSet<long>();var seenRenderers=new HashSet<long>();
            try
            {
                var animator=player.AnimatorBody;
                if(animator!=null)
                {
                    var culling=animator.cullingMode;
                    lifetime.Add(()=>{if(animator!=null)animator.cullingMode=culling;});
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                }
                foreach(var parts in new[]{model.m_gfxTorso,model.m_gfxLegs,model.m_gfxArms,model.m_gfxHead})
                {
                    if(parts==null)continue;
                    foreach(var part in parts)
                    {
                        if(part==null||!seenParts.Add(part.Pointer.ToInt64()))continue;
                        bool active=part.activeSelf;
                        var before=new List<(Renderer renderer,bool enabled,bool effective,ShadowCastingMode shadow)>();
                        foreach(var r in part.GetComponentsInChildren<Renderer>(true))
                        {
                            if(r==null||!seenRenderers.Add(r.Pointer.ToInt64()))continue;
                            if(r.TryCast<MeshRenderer>()==null&&r.TryCast<SkinnedMeshRenderer>()==null)continue;
                            before.Add((r,r.enabled,r.enabled&&r.gameObject.activeInHierarchy,r.shadowCastingMode));
                        }
                        // Activation may invoke OnEnable. Preserve every collider's enabled
                        // state immediately; never turn on local damage colliders for display.
                        var colliders=new List<(Collider collider,bool enabled)>();
                        foreach(var c in part.GetComponentsInChildren<Collider>(true))if(c!=null)colliders.Add((c,c.enabled));
                        foreach(var c in colliders)lifetime.Add(()=>{if(c.collider!=null)c.collider.enabled=c.enabled;});
                        foreach(var b in before)lifetime.Add(()=>{if(b.renderer!=null){b.renderer.enabled=b.enabled;b.renderer.shadowCastingMode=b.shadow;}});
                        lifetime.Add(()=>{if(part!=null)part.SetActive(active);});
                        if(!active)part.SetActive(true);
                        foreach(var c in colliders)if(c.collider!=null&&c.collider.enabled!=c.enabled)c.collider.enabled=c.enabled;
                        foreach(var b in before){renderers.Add((b.renderer,b.effective,b.shadow));b.renderer.enabled=b.effective;}
                    }
                }
                if(renderers.Count==0)throw new InvalidOperationException("没有可用的本机身体 Renderer");
            }
            catch {try{lifetime.Dispose();}catch{}throw;}
        }
        internal void Show(bool mainCamera,Vector3 camera=default,bool fade=false,float near=.12f,float far=.4f)
        {
            occlusion.Restore();if(mainCamera)occlusion.Begin();
            foreach(var state in renderers)if(state.renderer!=null)
            {
                state.renderer.enabled=mainCamera||state.outside;
                state.renderer.shadowCastingMode=mainCamera?ShadowCastingMode.On:state.shadow;
                if(mainCamera)occlusion.Apply(state.renderer,camera,fade,near,far);
            }
        }
        public void Dispose(){try{occlusion.Dispose();}finally{lifetime.Dispose();renderers.Clear();}}
    }
}
