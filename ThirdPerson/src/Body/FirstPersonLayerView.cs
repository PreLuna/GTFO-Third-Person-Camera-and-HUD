using System;
using System.Collections.Generic;
using Player;
using UnityEngine;

namespace PreLuna.ThirdPerson.Body
{
    // Hide graphics only. The original arms/item animators and gameplay stay active.
    internal sealed class FirstPersonLayerView : IDisposable
    {
        sealed class Graphic { internal Renderer Renderer; internal bool ForceOff; }
        readonly List<Graphic> graphics=new();
        readonly List<(Camera camera,bool enabled)> cameras=new();
        readonly List<(Animator animator,AnimatorCullingMode mode)> animators=new();
        readonly LocalPlayerAgent player;
        long itemId;
        float nextRefresh;
        internal int SkippedBodyGraphics {get;private set;}
        bool hidden,camerasHidden;
        internal int RendererCount=>graphics.Count;
        internal bool CamerasHidden=>camerasHidden;
        internal FirstPersonLayerView(LocalPlayerAgent local)
        {
            player=local;
            try
            {
            foreach(var camera in new[]{local.FPSCamera.m_itemCamera,local.FPItemHolder?.m_ItemLayerCamera})
            {
                if(camera==null)continue;
                if(camera.Pointer==local.FPSCamera.m_camera.Pointer)throw new InvalidOperationException("第一人称物品相机与世界相机重合");
                if(!cameras.Exists(c=>c.camera.Pointer==camera.Pointer))cameras.Add((camera,camera.enabled));
            }
            KeepAnimating();
            }
            catch{Dispose();throw;}
        }
        internal void Bind(ItemEquippable item,LocalBodyView body)
        {
            KeepAnimating();
            if(itemId==item.Pointer.ToInt64()&&Time.unscaledTime<nextRefresh)return;
            RestoreGraphics();graphics.Clear();itemId=item.Pointer.ToInt64();SkippedBodyGraphics=0;nextRefresh=Time.unscaledTime+.5f;
            var seen=new HashSet<long>();
            void Add(GameObject root)
            {
                if(root==null)return;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if(renderer!=null&&seen.Add(renderer.Pointer.ToInt64()))
                    {
                        if(body.Contains(renderer)){SkippedBodyGraphics++;continue;}
                        graphics.Add(new Graphic{Renderer=renderer});
                    }
            }
            var arms=player.FPItemHolder?.FPSArms;
            Add(arms?.m_modelRoot);
            if(arms?.m_gfxArms!=null)foreach(var part in arms.m_gfxArms)Add(part);
            Add(item.gameObject);
        }
        void KeepAnimating()
        {
            foreach(var animator in new[]{player.AnimatorArms,player.FPItemHolder?.m_fpsItemAnim})
            {
                if(animator==null||animators.Exists(a=>a.animator!=null&&a.animator.Pointer==animator.Pointer))continue;
                animators.Add((animator,animator.cullingMode));animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            }
        }
        internal void HideGraphics()
        {
            if(hidden)return;
            hidden=true;
            foreach(var graphic in graphics)if(graphic.Renderer!=null)
            {graphic.ForceOff=graphic.Renderer.forceRenderingOff;graphic.Renderer.forceRenderingOff=true;}
        }
        internal void RestoreGraphics()
        {
            if(!hidden)return;
            hidden=false;
            foreach(var graphic in graphics)if(graphic.Renderer!=null)graphic.Renderer.forceRenderingOff=graphic.ForceOff;
        }
        internal void SetCamerasHidden(bool hide)
        {
            if(hide==camerasHidden)return;
            if(hide)for(int i=0;i<cameras.Count;i++)
            {var camera=cameras[i].camera;if(camera!=null){cameras[i]=(camera,camera.enabled);camera.enabled=false;}}
            else foreach(var entry in cameras)if(entry.camera!=null)entry.camera.enabled=entry.enabled;
            camerasHidden=hide;
        }
        internal object Diagnostics()=>new{renderers=graphics.Count,graphicsHidden=hidden,camerasHidden,skippedBodyGraphics=SkippedBodyGraphics,
            cameras=cameras.ConvertAll(c=>new{id=c.camera==null?0:c.camera.Pointer.ToInt64(),enabled=c.camera!=null&&c.camera.enabled,originalEnabled=c.enabled})};
        public void Dispose()
        {
            RestoreGraphics();SetCamerasHidden(false);
            foreach(var a in animators)if(a.animator!=null)a.animator.cullingMode=a.mode;
            graphics.Clear();cameras.Clear();animators.Clear();
        }
    }
}
