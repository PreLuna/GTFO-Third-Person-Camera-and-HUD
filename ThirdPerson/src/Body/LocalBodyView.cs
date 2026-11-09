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
        sealed class Graphic
        {
            internal Renderer Renderer;
            internal bool Enabled,Offscreen,ForceOff,HierarchyActive,LastMainActive,Ghost;
            internal SkinnedMeshRenderer Skin;
            internal ShadowCastingMode Shadow,LastMainShadow;
        }
        sealed class Part
        {
            internal GameObject Root;
            internal bool Active,HasVisibleGraphics;
            internal readonly List<(Collider collider,bool enabled)> Colliders=new();
        }
        readonly RestoreJournal lifetime=new();
        readonly LocalBodyOcclusion occlusion=new();
        readonly PlayerSyncModelData model;
        readonly LocalBodyScale bodyScale;
        readonly List<Graphic> renderers=new();
        readonly List<Part> parts=new();
        readonly HashSet<long> seenParts=new(),seenRenderers=new(),seenColliders=new();
        float nextRefresh;
        bool prepared,changed;
        int preparedAt=-1,preparedFrame=-1,activations,enabledChanges,offscreenChanges;
        internal bool ReadyForMain=>prepared&&!changed&&preparedFrame==Time.frameCount&&Time.frameCount>preparedAt;
        internal void Warmup()=>changed=true;
        int Count(bool ghost){int count=0;foreach(var g in renderers)if(g.Renderer!=null&&g.Ghost==ghost)count++;return count;}
        internal int RendererCount=>Count(false);
        internal int GhostRendererCount=>Count(true);
        internal bool Contains(Renderer renderer)=>renderer!=null&&seenRenderers.Contains(renderer.Pointer.ToInt64());
        internal object FadeDiagnostics=>new{transparent=occlusion.TransparentRenderers,hidden=occlusion.HiddenRenderers,preservedOpaque=occlusion.PreservedRenderers};
        internal LocalBodyView(LocalPlayerAgent player)
        {
            model=player.PlayerSyncModel;
            if(model==null)throw new InvalidOperationException("本机人物模型尚未生成");
            // Only the distinct body/Animator visual root is eligible. Never
            // normalize a shared player, camera, or FPS-arms transform.
            var visual=model.transform;var animatorRoot=player.AnimatorBody?.transform;var fpsArms=player.AnimatorArms?.transform;
            if(animatorRoot!=null&&visual.Pointer==animatorRoot.Pointer&&visual.Pointer!=player.transform.Pointer
                &&visual.Pointer!=player.FPSCamera?.transform?.Pointer&&visual.Pointer!=player.FPSCamera?.m_camera?.transform?.Pointer
                &&(fpsArms==null||!fpsArms.IsChildOf(visual)))bodyScale=new LocalBodyScale(visual);
            try
            {
                var animator=player.AnimatorBody;
                if(animator!=null)
                {
                    var culling=animator.cullingMode;
                    lifetime.Add(()=>{if(animator!=null)animator.cullingMode=culling;});
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                }
                Refresh();
            }
            catch{try{Dispose();}catch{}throw;}
        }
        // Scan outside the display scope. Vanity can finish loading under the same model/part.
        internal void Refresh()
        {
            if(Time.unscaledTime<nextRefresh)return;
            nextRefresh=Time.unscaledTime+.5f;
            var liveParts=new HashSet<long>();var liveRenderers=new HashSet<long>();var liveColliders=new HashSet<long>();
            var ghosts=new HashSet<long>();
            if(model.m_ghostRenderers!=null)foreach(var g in model.m_ghostRenderers)if(g!=null)ghosts.Add(g.Pointer.ToInt64());
            foreach(var group in new[]{model.m_gfxTorso,model.m_gfxLegs,model.m_gfxArms,model.m_gfxHead})
            {
                if(group==null)continue;
                foreach(var root in group)
                {
                    if(root==null)continue;
                    liveParts.Add(root.Pointer.ToInt64());
                    Part part;
                    if(seenParts.Add(root.Pointer.ToInt64()))
                    {part=new Part{Root=root,Active=root.activeSelf};parts.Add(part);changed=true;}
                    else part=parts.Find(p=>p.Root!=null&&p.Root.Pointer==root.Pointer);
                    part.HasVisibleGraphics=false;
                    foreach(var c in root.GetComponentsInChildren<Collider>(true))if(c!=null)
                    {liveColliders.Add(c.Pointer.ToInt64());if(seenColliders.Add(c.Pointer.ToInt64()))part.Colliders.Add((c,c.enabled));}
                    foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if(r==null)continue;
                        var skin=r.TryCast<SkinnedMeshRenderer>();
                        if(skin==null&&r.TryCast<MeshRenderer>()==null)continue;
                        bool ghost=ghosts.Contains(r.Pointer.ToInt64())||HasGhostMaterial(r);
                        if(!ghost)part.HasVisibleGraphics=true;
                        liveRenderers.Add(r.Pointer.ToInt64());
                        if(seenRenderers.Contains(r.Pointer.ToInt64()))
                        {var existing=renderers.Find(g=>g.Renderer!=null&&g.Renderer.Pointer==r.Pointer);if(existing.Ghost!=ghost){existing.Ghost=ghost;changed=true;}continue;}
                        seenRenderers.Add(r.Pointer.ToInt64());
                        var graphic=new Graphic{Renderer=r,Skin=skin,Ghost=ghost};Capture(graphic);renderers.Add(graphic);changed=true;
                    }
                }
            }
            // Removed vanity roots remain the game's responsibility. Do not
            // reactivate an obsolete outfit after its replacement has arrived.
            foreach(var g in renderers)if(g.Renderer!=null&&!liveRenderers.Contains(g.Renderer.Pointer.ToInt64()))Restore(g);
            foreach(var p in parts)if(p.Root!=null&&!liveParts.Contains(p.Root.Pointer.ToInt64())&&!p.Active&&p.Root.activeSelf)p.Root.SetActive(false);
            parts.RemoveAll(p=>p.Root==null||!liveParts.Contains(p.Root.Pointer.ToInt64()));
            renderers.RemoveAll(g=>g.Renderer==null||!liveRenderers.Contains(g.Renderer.Pointer.ToInt64()));
            foreach(var part in parts)part.Colliders.RemoveAll(c=>c.collider==null||!liveColliders.Contains(c.collider.Pointer.ToInt64()));
            seenParts.IntersectWith(liveParts);seenRenderers.IntersectWith(liveRenderers);seenColliders.IntersectWith(liveColliders);
        }
        static bool HasGhostMaterial(Renderer renderer)
        {
            foreach(var material in renderer.sharedMaterials)if(material?.shader?.name=="Cell/Player/PlayerGhost")return true;
            return false;
        }
        void Capture(Graphic g)
        {
            var r=g.Renderer;g.Enabled=r.enabled;g.ForceOff=r.forceRenderingOff;g.Shadow=r.shadowCastingMode;
            g.Offscreen=g.Skin!=null&&g.Skin.updateWhenOffscreen;
            g.HierarchyActive=r.gameObject.activeInHierarchy&&!parts.Exists(p=>p.Root!=null&&!p.Active&&r.transform.IsChildOf(p.Root.transform));
        }
        static void Restore(Graphic g)
        {
            if(g.Renderer==null)return;
            g.Renderer.forceRenderingOff=g.ForceOff;g.Renderer.enabled=g.Enabled;g.Renderer.shadowCastingMode=g.Shadow;
            if(g.Skin!=null)g.Skin.updateWhenOffscreen=g.Offscreen;
        }
        // Called from LateUpdate, before Unity's skinning/rendering phase. Native
        // nodes and skin updates stay alive between cameras; only the draw gate
        // changes during a render callback. Never activate a skin in OnPreCull.
        internal void Prepare(bool normalizeBodyScale=true)
        {
            Refresh();
            if(!prepared)
            {
                foreach(var p in parts)if(p.Root!=null)p.Active=p.Root.activeSelf;
                foreach(var g in renderers)if(g.Renderer!=null)Capture(g);
                prepared=true;changed=true;
            }
            if(bodyScale?.Prepare(normalizeBodyScale)==true)changed=true;
            foreach(var p in parts)if(p.Root!=null)
            {
                if(p.HasVisibleGraphics&&!p.Root.activeSelf){p.Root.SetActive(true);activations++;changed=true;}
                foreach(var c in p.Colliders)if(c.collider!=null&&c.collider.enabled!=c.enabled)c.collider.enabled=c.enabled;
            }
            foreach(var g in renderers)if(g.Renderer!=null)
            {
                if(g.Ghost){Restore(g);continue;}
                if(!g.Renderer.enabled){g.Renderer.enabled=true;enabledChanges++;changed=true;}
                if(g.Skin!=null&&!g.Skin.updateWhenOffscreen){g.Skin.updateWhenOffscreen=true;offscreenChanges++;changed=true;}
            }
            if(changed){preparedAt=Time.frameCount;changed=false;}
            preparedFrame=Time.frameCount;
            Show(false);
        }
        internal void Show(bool mainCamera,Vector3 camera=default,bool fade=false,float near=.12f,float far=.4f)
        {
            occlusion.Restore();if(mainCamera)occlusion.Begin();
            if(mainCamera&&!prepared)throw new InvalidOperationException("身体尚未在渲染前准备");
            if(!prepared)return;
            foreach(var state in renderers)if(state.Renderer!=null)
            {
                bool off=mainCamera?state.Ghost:state.ForceOff||!state.Enabled||!state.HierarchyActive;
                if(state.Renderer.forceRenderingOff!=off)state.Renderer.forceRenderingOff=off;
                state.Renderer.shadowCastingMode=mainCamera&&!state.Ghost?ShadowCastingMode.On:state.Shadow;
                if(mainCamera)
                {
                    if(!state.Ghost)occlusion.Apply(state.Renderer,camera,fade,near,far);
                    state.LastMainActive=state.Renderer.enabled&&!state.Renderer.forceRenderingOff&&state.Renderer.gameObject.activeInHierarchy;
                    state.LastMainShadow=state.Renderer.shadowCastingMode;
                }
            }
        }
        internal void EndPresentation()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>bodyScale?.Restore());cleanup.Add(occlusion.Restore);
            if(!prepared)return;
            prepared=false;preparedFrame=preparedAt=-1;
            foreach(var p in parts)if(p.Root!=null)
            {
                cleanup.Add(()=>
                {
                    if(p.Root!=null&&p.Root.activeSelf!=p.Active)p.Root.SetActive(p.Active);
                    foreach(var c in p.Colliders)if(c.collider!=null&&c.collider.enabled!=c.enabled)c.collider.enabled=c.enabled;
                });
            }
            foreach(var g in renderers)cleanup.Add(()=>Restore(g));
        }
        internal object Diagnostics()=>new{renderers=RendererCount,excludedGhostRenderers=GhostRendererCount,presentation=new{mode="stable-native-skins",prepared,preparedFrame,preparedAt,activations,enabledChanges,offscreenChanges,ready=ReadyForMain},bodyScale=bodyScale?.Diagnostics(),fade=FadeDiagnostics,parts=parts.ConvertAll(p=>new{name=p.Root==null?"destroyed":p.Root.name,active=p.Root!=null&&p.Root.activeInHierarchy,originalActive=p.Active}),
            graphics=renderers.ConvertAll(g=>new{name=g.Renderer==null?"destroyed":g.Renderer.gameObject.name,lastMainActive=g.LastMainActive,lastMainShadow=g.LastMainShadow.ToString(),skin=g.Skin!=null,excludedGhost=g.Ghost,
                forceRenderingOff=g.Renderer!=null&&g.Renderer.forceRenderingOff,updateWhenOffscreen=g.Skin!=null&&g.Skin.updateWhenOffscreen,shaders=ShaderNames(g.Renderer)})};
        static string[] ShaderNames(Renderer renderer)
        {
            if(renderer==null)return Array.Empty<string>();
            var names=new List<string>();foreach(var m in renderer.sharedMaterials)names.Add(m==null?"null":m.shader?.name??"null");return names.ToArray();
        }
        public void Dispose()
        {
            using var cleanup=new RestoreJournal();
            cleanup.Add(()=>{renderers.Clear();parts.Clear();seenParts.Clear();seenRenderers.Clear();seenColliders.Clear();});
            cleanup.Add(lifetime.Dispose);cleanup.Add(occlusion.Dispose);cleanup.Add(EndPresentation);
        }
    }
}
