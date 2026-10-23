using System;
using System.Collections.Generic;
using CullingSystem;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    internal sealed class RenderScope : IDisposable
    {
        readonly RestoreJournal journal=new();
        readonly C_Camera culler;
        readonly Camera camera;
        readonly float originalNear;
        readonly float originalFov;
        readonly int originalMask;
        readonly List<(Transform transform,Vector3 position,Quaternion rotation)> poses=new();
        bool disposed,suspended;
        bool counted;
        readonly List<(Vector3 position,Quaternion rotation)> displayed=new();
        float displayNear,displayFov;
        int displayMask;
        internal bool Suspended=>suspended;
        internal bool WasRestored {get;private set;}
        internal readonly Vector3 OriginalPosition;
        internal readonly Vector3 OriginalForward;
        internal readonly string AudioHandling;
        internal RenderScope(LocalPlayerAgent player,DisplayView view,CameraSettings settings,float? displayFov=null)
        {
            var fps=player.FPSCamera;camera=fps.m_camera;culler=fps.m_cullingCamera;
            if(camera==null||culler==null)throw new InvalidOperationException("本机世界相机或裁剪组件失效");
            if(camera.transform.Pointer==player.transform.Pointer||culler.transform.Pointer==player.transform.Pointer)
                throw new InvalidOperationException("相机/裁剪与人物根物体重合，需要另行适配");
            OriginalPosition=camera.transform.position;OriginalForward=camera.transform.forward;
            originalNear=camera.nearClipPlane;originalMask=camera.cullingMask;originalFov=camera.fieldOfView;
            try
            {
                var cp=CameraManager.CullingPosition;var cd=CameraManager.CullingDirection;float cf=CameraManager.CullingFov;
                var p=C_Camera.Position;var f=C_Camera.Forward;var r=C_Camera.Right;var u=C_Camera.Up;
                journal.Add(()=>{CameraManager.CullingPosition=cp;CameraManager.CullingDirection=cd;CameraManager.CullingFov=cf;C_Camera.Position=p;C_Camera.Forward=f;C_Camera.Right=r;C_Camera.Up=u;});
                SavePose(camera.transform);
                if(culler.transform.Pointer!=camera.transform.Pointer)SavePose(culler.transform);
                float near=camera.nearClipPlane;int mask=camera.cullingMask;float fov=camera.fieldOfView;
                journal.Add(()=>{if(camera!=null){camera.nearClipPlane=near;camera.cullingMask=mask;camera.fieldOfView=fov;}});
                var listener=fps.AkAudioListener;var audio=listener==null?null:listener.transform;
                Vector3 audioPos=audio==null?Vector3.zero:audio.position;Quaternion audioRot=audio==null?Quaternion.identity:audio.rotation;
                bool sharedAudio=audio!=null&&(audio.Pointer==camera.transform.Pointer||audio.Pointer==culler.transform.Pointer);
                bool separateAudio=audio!=null&&!sharedAudio&&(audio.IsChildOf(camera.transform)||audio.IsChildOf(culler.transform));
                AudioHandling=sharedAudio?"shared-transform-render-scope":separateAudio?"child-listener-pinned":"independent-listener";
                if(separateAudio)SavePose(audio);
                var rotation=Quaternion.LookRotation(ConvertView.U(view.Forward),Vector3.up);
                camera.transform.SetPositionAndRotation(ConvertView.U(view.Position),rotation);
                if(culler.transform.Pointer!=camera.transform.Pointer)culler.transform.SetPositionAndRotation(camera.transform.position,rotation);
                camera.nearClipPlane=settings.NearClip;
                if(displayFov.HasValue)camera.fieldOfView=displayFov.Value;
                camera.cullingMask=(mask&~(1<<LayerManager.LAYER_FIRST_PERSON_ITEM))|(1<<LayerManager.LAYER_THIRD_PERSON_ITEM);
                if(separateAudio)audio.SetPositionAndRotation(audioPos,audioRot);
                CameraManager.CullingPosition=camera.transform.position;CameraManager.CullingDirection=camera.transform.forward;CameraManager.CullingFov=camera.fieldOfView;
                foreach(var pose in poses)displayed.Add((pose.transform.localPosition,pose.transform.localRotation));
                displayNear=camera.nearClipPlane;this.displayFov=camera.fieldOfView;displayMask=camera.cullingMask;
                counted=true;ResourceLedger.Create(OwnedResource.RenderScope);
            }
            catch{try{journal.Dispose();}catch{}throw;}
        }
        void SavePose(Transform t)
        { var position=t.localPosition;var rotation=t.localRotation;poses.Add((t,position,rotation));journal.Add(()=>{if(t!=null){t.localPosition=position;t.localRotation=rotation;}}); }
        internal void Suspend()
        {
            if(disposed||suspended)return;suspended=true;
            for(int i=0;i<poses.Count;i++){var p=poses[i];if(p.transform!=null){p.transform.localPosition=p.position;p.transform.localRotation=p.rotation;}}
            camera.nearClipPlane=originalNear;camera.fieldOfView=originalFov;camera.cullingMask=originalMask;
            // The auxiliary renderer may already have configured its globals before Camera.Render.
            // Restore only our main-camera state here; do not overwrite its Current/culling/shader globals.
        }
        internal void Resume()
        {
            if(disposed||!suspended)return;
            for(int i=0;i<poses.Count;i++){var t=poses[i].transform;if(t!=null){t.localPosition=displayed[i].position;t.localRotation=displayed[i].rotation;}}
            camera.nearClipPlane=displayNear;camera.fieldOfView=displayFov;camera.cullingMask=displayMask;suspended=false;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            Exception error=null;
            try{journal.Dispose();}catch(Exception e){error=e;}
            // Rebuild native visibility at the original camera without moving its agent/node.
            try{if(culler!=null){culler.RunVisibility();C_CullingManager.C_CameraUpdate();C_CullingManager.SortAndUpdateLights();}}catch(Exception e){error=error==null?e:new AggregateException(error,e);}
            try
            {
                WasRestored=camera!=null&&camera.cullingMask==originalMask&&Math.Abs(camera.nearClipPlane-originalNear)<.00001f&&Math.Abs(camera.fieldOfView-originalFov)<.00001f;
                foreach(var pose in poses)WasRestored&=pose.transform!=null&&(pose.transform.localPosition-pose.position).sqrMagnitude<.000001f&&Quaternion.Angle(pose.transform.localRotation,pose.rotation)<.02f;
                if(!WasRestored)throw new InvalidOperationException("相机/裁剪/子监听器姿态或近裁剪面/可见层未恢复");
            }
            catch(Exception e){error=error==null?e:new AggregateException(error,e);}
            if(counted){ResourceLedger.Release(OwnedResource.RenderScope);counted=false;}
            if(error!=null)throw error;
        }
    }
}
