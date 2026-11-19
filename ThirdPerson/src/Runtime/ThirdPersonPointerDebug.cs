using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using LevelGeneration;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPerson.Core;
using NV=System.Numerics.Vector3;
using NV2=System.Numerics.Vector2;
using UV2=UnityEngine.Vector2;

namespace PreLuna.ThirdPerson.Runtime
{
    // Read-only phase-0 diagnostics. It never changes native selection, shot rays,
    // melee hitboxes or the render scope; it only records what the current display
    // camera and the native eye actually hit.
    internal readonly record struct PointerDebug(
        bool Available,bool Stale,int Frame,
        NV Eye,NV Camera,NV EyeForward,NV CameraForward,
        float CameraDistance,float Shoulder,float VerticalFov,
        NV2 Center,
        bool CameraHit,NV CameraHitPoint,float CameraHitDistance,NV2 CameraScreen,bool CameraScreenValid,
        bool EyeHit,NV EyeHitPoint,float EyeHitDistance,NV2 EyeScreen,bool EyeScreenValid,
        bool SelectedValid,NV SelectedPoint,NV2 SelectedScreen,bool SelectedScreenValid,
        string SelectedName,string SelectedAction,string SelectedKey,bool SelectedBlocked,
        bool CurrentValid,NV CurrentPoint,NV2 CurrentScreen,bool CurrentScreenValid,string CurrentName,
        bool LockHit,bool EyeLock,bool CameraLock,bool LockInReach,NV LockPoint,float LockDistance,
        NV2 LockScreen,bool LockScreenValid,string LockName,
        float EyeOffsetPixels,float SelectedOffsetPixels,float CurrentOffsetPixels,
        bool ObservationActive,bool OwnView);

    public sealed partial class ThirdPersonRuntime
    {
        const float PointerRayDistance=250f;
        const float PointerLockReach=2.5f;
        readonly UnityCameraWorld pointerWorld=new();
        long pointerPlayer;
        CameraSnapshot pointerCamera;
        bool pointerCameraValid;
        long pointerTarget;
        string pointerName="",pointerAction="",pointerKey="";

        [HideFromIl2Cpp]
        PointerDebug CapturePointerDebug()
        {
            var fps=player?.FPSCamera;
            if(player==null||fps==null||display.Frame<0||!display.IsThirdPerson)return default;
            NV eye=ConvertView.N(fps.Position),eyeForward=ConvertView.N(fps.Forward);
            NV camera=display.Position,cameraForward=display.Forward;
            if(!Finite(eye)||!Finite(camera)||!Finite(eyeForward)||!Finite(cameraForward))return default;
            if(eyeForward.LengthSquared()<.000001f||cameraForward.LengthSquared()<.000001f)return default;
            eyeForward=NV.Normalize(eyeForward);cameraForward=NV.Normalize(cameraForward);
            float cameraDistance=float.IsFinite(rig.Distance)?rig.Distance:0;
            float shoulder=float.IsFinite(rig.Shoulder)?rig.Shoulder:0;
            float fov=float.IsFinite(rig.VerticalFov)?rig.VerticalFov:0;
            var center=PointerCenter();
            BindPointerWorld();
            bool cameraHit=TracePointer(camera,cameraForward,out var cameraPoint,out var cameraHitDistance,out var cameraCollider);
            bool eyeHit=TracePointer(eye,eyeForward,out var eyePoint,out var eyeHitDistance,out var eyeCollider);
            bool cameraScreenValid=TryScreen(cameraPoint,out var cameraScreen);
            bool eyeScreenValid=TryScreen(eyePoint,out var eyeScreen);

            bool selectedValid=false,selectedBlocked=false;
            NV selectedPoint=default;var selectedName="";var selectedAction="";var selectedKey="";
            var controller=player.Interaction;
            try
            {
                var target=controller?.m_bestSelectedInteract?.TryCast<Interact_Base>();
                selectedValid=controller!=null&&Id(controller.m_owner)==Id(player)&&PlayerInteraction.InteractionEnabled
                    &&target!=null&&target.IsSelected&&target.IsActive;
                if(selectedValid)
                {
                    selectedBlocked=target.IsBlocked;
                    long targetId=Id(target);
                    if(targetId!=pointerTarget){pointerTarget=targetId;pointerName=ReadPointerName(target);pointerAction=ReadPointerAction(target);pointerKey=ReadPointerKey(target);}
                    selectedName=pointerName;selectedAction=pointerAction;selectedKey=pointerKey;
                    var root=target.Root;
                    if(root!=null)selectedPoint=ConvertView.N(root.position);
                    else if(target.m_colliderToOwn!=null)selectedPoint=ConvertView.N(target.m_colliderToOwn.bounds.center);
                    else selectedPoint=ConvertView.N(target.transform.position);
                }
                else {pointerTarget=0;pointerName="";pointerAction="";pointerKey="";}
            }
            catch {selectedValid=false;pointerTarget=0;pointerName="";pointerAction="";pointerKey="";}
            bool selectedScreenValid=false;NV2 selectedScreen=default;
            if(selectedValid)selectedScreenValid=TryScreen(selectedPoint,out selectedScreen);

            bool currentValid=false;NV currentPoint=default;var currentName="";
            try
            {
                var current=controller?.m_currentInteractionObj;
                if(current!=null){currentValid=true;currentPoint=ConvertView.N(current.transform.position);currentName=current.name??"";}
            }
            catch {currentValid=false;currentPoint=default;currentName="";}
            bool currentScreenValid=false;NV2 currentScreen=default;
            if(currentValid)currentScreenValid=TryScreen(currentPoint,out currentScreen);

            LG_WeakLock eyeLockComp=null,cameraLockComp=null;
            try
            {
                if(eyeHit&&eyeCollider!=null)eyeLockComp=FindWeakLock(eyeCollider);
                if(cameraHit&&cameraCollider!=null)cameraLockComp=FindWeakLock(cameraCollider);
            }
            catch {}
            bool eyeLock=eyeLockComp!=null,cameraLock=cameraLockComp!=null,lockHit=eyeLock||cameraLock;
            bool lockInReach=lockHit&&LockDistanceFor(eyeLock,eyeHitDistance,cameraLock,cameraHitDistance)<=PointerLockReach;
            NV lockPoint=default;float lockDistance=0;var lockName="";
            if(eyeLock){lockPoint=eyePoint;lockDistance=eyeHitDistance;lockName=eyeCollider?.name??eyeLockComp.name;lockName=lockName??"";}
            else if(cameraLock){lockPoint=cameraPoint;lockDistance=cameraHitDistance;lockName=cameraCollider?.name??cameraLockComp.name;lockName=lockName??"";}
            bool lockScreenValid=false;NV2 lockScreen=default;
            if(lockHit)lockScreenValid=TryScreen(lockPoint,out lockScreen);

            float eyeOffset=eyeScreenValid?NV2.Distance(eyeScreen,center):-1;
            float selectedOffset=selectedScreenValid?NV2.Distance(selectedScreen,center):-1;
            float currentOffset=currentScreenValid?NV2.Distance(currentScreen,center):-1;
            return new PointerDebug(true,display.Frame!=Time.frameCount,display.Frame,eye,camera,eyeForward,cameraForward,
                cameraDistance,shoulder,fov,center,
                cameraHit,cameraPoint,cameraHitDistance,cameraScreen,cameraScreenValid,
                eyeHit,eyePoint,eyeHitDistance,eyeScreen,eyeScreenValid,
                selectedValid,selectedPoint,selectedScreen,selectedScreenValid,selectedName,selectedAction,selectedKey,selectedBlocked,
                currentValid,currentPoint,currentScreen,currentScreenValid,currentName,
                lockHit,eyeLock,cameraLock,lockInReach,lockPoint,lockDistance,lockScreen,lockScreenValid,lockName,
                eyeOffset,selectedOffset,currentOffset,lookInput.Orientation.Active,OwnObservationView());
        }

        [HideFromIl2Cpp]
        void BindPointerWorld()
        {
            long id=Id(player);
            if(id==pointerPlayer)return;
            pointerWorld.Bind(player.gameObject);pointerPlayer=id;
            pointerWorld.Mask=(LayerManager.MASK_CAMERA_RAY|LayerManager.MASK_MELEE_ATTACK_TARGETS_WITH_STATIC)
                &~((1<<LayerManager.LAYER_FIRST_PERSON_ITEM)|(1<<LayerManager.LAYER_THIRD_PERSON_ITEM));
        }

        [HideFromIl2Cpp]
        bool TracePointer(NV origin,NV direction,out NV point,out float distance,out Collider collider)
        {
            point=origin+direction*PointerRayDistance;distance=PointerRayDistance;collider=null;
            if(!Finite(origin)||!Finite(direction)||direction.LengthSquared()<.000001f)return false;
            try
            {
                if(pointerWorld.RayHit(origin,direction,PointerRayDistance,out var hit))
                {
                    if(hit.collider!=null){point=ConvertView.N(hit.point);distance=hit.distance;collider=hit.collider;}
                    else {point=origin;distance=0;}
                    return true;
                }
            }
            catch {}
            return false;
        }

        [HideFromIl2Cpp]
        bool TryScreen(NV point,out NV2 screen)
        {
            screen=default;
            return pointerCameraValid&&float.IsFinite(Screen.height)&&Screen.height>0
                &&PointerProjection.TryProject(pointerCamera,point,Screen.height,out screen);
        }

        [HideFromIl2Cpp]
        NV2 PointerCenter()
        {
            if(pointerCameraValid)
            {
                var rect=pointerCamera.Viewport;
                if(float.IsFinite(rect.X)&&float.IsFinite(rect.Y)&&float.IsFinite(rect.Width)&&float.IsFinite(rect.Height)
                    &&rect.Width>0&&rect.Height>0)
                    return new(rect.X+rect.Width*.5f,Screen.height-(rect.Y+rect.Height*.5f));
            }
            return new(Screen.width*.5f,Screen.height*.5f);
        }

        [HideFromIl2Cpp]
        static float LockDistanceFor(bool eyeLock,float eyeDistance,bool cameraLock,float cameraDistance)
            =>eyeLock?eyeDistance:cameraLock?cameraDistance:float.PositiveInfinity;

        [HideFromIl2Cpp]
        static LG_WeakLock FindWeakLock(Collider collider)
        {
            if(collider==null)return null;
            try
            {
                var current=collider.transform;
                for(int i=0;i<6&&current!=null;i++)
                {
                    var lockComponent=current.GetComponent<LG_WeakLock>();
                    if(lockComponent!=null)return lockComponent;
                    current=current.parent;
                }
            }
            catch {}
            return null;
        }

        [HideFromIl2Cpp]
        static string ReadPointerName(Interact_Base target)
        {
            try
            {
                var pickup=target.TryCast<Interact_Pickup_PickupItem>();
                if(pickup?.m_nameFunc!=null){var value=pickup.m_nameFunc.Invoke();if(!string.IsNullOrWhiteSpace(value))return value;}
                var generic=target.TryCast<Interact_Generic>();
                if(generic!=null&&!string.IsNullOrWhiteSpace(generic.m_displayName))return generic.m_displayName;
                var revive=target.TryCast<Interact_Revive>();
                if(revive!=null&&!string.IsNullOrWhiteSpace(revive.m_displayName))return revive.m_displayName;
                var timed=target.TryCast<Interact_Timed>();
                if(timed!=null&&!string.IsNullOrWhiteSpace(timed.InteractionMessage))return timed.InteractionMessage;
            }
            catch {}
            return "未读取";
        }

        [HideFromIl2Cpp]
        static string ReadPointerAction(Interact_Base target)
        {
            try
            {
                var timed=target.TryCast<Interact_Timed>();
                if(timed!=null&&!string.IsNullOrWhiteSpace(timed.InteractionMessage))return timed.InteractionMessage;
                if(target.TryCast<Interact_Revive>()!=null)return "救援";
                if(target.TryCast<Interact_Generic>()!=null)return "交互";
            }
            catch {}
            return "";
        }

        [HideFromIl2Cpp]
        static string ReadPointerKey(Interact_Base target)
        {
            try
            {
                var mapper=InputMapper.Current;
                if(mapper?.m_currentKeyMouseMap!=null)
                {
                    var binding=mapper.DoGetBindingName(target.InputAction,mapper.m_currentKeyMouseMap);
                    if(!string.IsNullOrWhiteSpace(binding))return binding;
                }
            }
            catch {}
            return "未读取";
        }

        [HideFromIl2Cpp]
        void DrawPointerDebug()
        {
            var settings=Plugin.Instance?.Settings;
            if(settings==null||!settings.PointerOverlay.Value)return;
            if(Event.current==null||Event.current.type!=EventType.Repaint)return;
            var debug=CapturePointerDebug();
            if(!debug.Available||debug.Stale||!debug.OwnView)return;
            int oldDepth=GUI.depth;var oldColor=GUI.color;
            try
            {
                GUI.depth=-100;
                DrawPointerPoint(debug.Center,Color.white,"中心");
                if(debug.CameraScreenValid)DrawPointerPoint(debug.CameraScreen,Color.yellow,"相机");
                if(debug.EyeScreenValid)DrawPointerPoint(debug.EyeScreen,new Color(1f,.3f,.3f),"眼睛");
                if(debug.SelectedScreenValid)DrawPointerPoint(debug.SelectedScreen,Color.cyan,"选中");
                if(debug.CurrentScreenValid)DrawPointerPoint(debug.CurrentScreen,new Color(1f,.2f,1f),"当前物体");
                if(debug.LockScreenValid)DrawPointerPoint(debug.LockScreen,Color.green,"锁");
                GUI.color=new Color(0,0,0,.72f);
                GUI.DrawTexture(new Rect(12,12,1000,230),Texture2D.whiteTexture);
                GUI.color=Color.white;
                GUI.Label(new Rect(20,18,984,220),BuildPointerText(debug));
            }
            finally {GUI.depth=oldDepth;GUI.color=oldColor;}
        }

        [HideFromIl2Cpp]
        static void DrawPointerPoint(NV2 point,Color color,string label)
        {
            var p=new UV2(point.X,point.Y);
            if(p.x<-20||p.y<-20||p.x>Screen.width+20||p.y>Screen.height+20)return;
            GUI.color=color;
            GUI.DrawTexture(new Rect(p.x-4,p.y-1,8,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x-1,p.y-4,2,8),Texture2D.whiteTexture);
            GUI.Label(new Rect(p.x+7,p.y-10,180,18),label);
        }

        [HideFromIl2Cpp]
        static string BuildPointerText(in PointerDebug debug)
        {
            var text=new StringBuilder();
            text.AppendLine($"P0 指向调试 F12 | 帧 {debug.Frame} | 观察={debug.ObservationActive} | 自有视角={debug.OwnView}");
            text.AppendLine($"相机距离={debug.CameraDistance:0.00} 肩位={debug.Shoulder:0.00} FOV={debug.VerticalFov:0.0} 中心=({debug.Center.X:0},{debug.Center.Y:0})");
            text.AppendLine($"相机命中={HitText(debug.CameraHit,debug.CameraHitDistance)} 屏={ScreenText(debug.CameraScreen,debug.CameraScreenValid)}");
            text.AppendLine($"眼睛命中={HitText(debug.EyeHit,debug.EyeHitDistance)} 屏={ScreenText(debug.EyeScreen,debug.EyeScreenValid)} 离中心={OffsetText(debug.EyeOffsetPixels)}");
            text.AppendLine($"选中={NameText(debug.SelectedName)} / {NameText(debug.SelectedAction)} / 键 {NameText(debug.SelectedKey)} / 受阻={debug.SelectedBlocked} 屏={ScreenText(debug.SelectedScreen,debug.SelectedScreenValid)} 离中心={OffsetText(debug.SelectedOffsetPixels)}");
            text.AppendLine($"当前物体={NameText(debug.CurrentName)} 屏={ScreenText(debug.CurrentScreen,debug.CurrentScreenValid)} 离中心={OffsetText(debug.CurrentOffsetPixels)}");
            text.AppendLine($"锁={NameText(debug.LockName)} 距离={(debug.LockHit?debug.LockDistance.ToString("0.00",CultureInfo.InvariantCulture)+"m":"-")} 触及内={debug.LockInReach} 眼={debug.EyeLock} 相机={debug.CameraLock} 屏={ScreenText(debug.LockScreen,debug.LockScreenValid)}");
            text.Append("黄=相机命中 红=眼睛命中 青=选中 品红=当前物体 绿=锁；射线只读，不改原版选择/命中");
            return text.ToString();
        }

        static string HitText(bool hit,float distance)
            =>hit?(distance<=.001f?"起点受阻":distance.ToString("0.00",CultureInfo.InvariantCulture)+"m"):"无";
        static string ScreenText(NV2 point,bool valid)=>valid?$"({point.X:0},{point.Y:0})":"屏外/无";
        static string OffsetText(float pixels)=>pixels>=0?pixels.ToString("0.0",CultureInfo.InvariantCulture)+"px":"-";
        static string NameText(string value)=>string.IsNullOrWhiteSpace(value)?"无":value;
        static bool Finite(NV value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);

        [HideFromIl2Cpp]
        object PointerSnapshot()
        {
            var debug=CapturePointerDebug();
            return new
            {
                available=debug.Available,stale=debug.Stale,frame=debug.Frame,
                eyePosition=Text(debug.Eye),cameraPosition=Text(debug.Camera),eyeForward=Text(debug.EyeForward),cameraForward=Text(debug.CameraForward),
                cameraDistance=debug.CameraDistance,shoulder=debug.Shoulder,verticalFov=debug.VerticalFov,center=Text(debug.Center),
                cameraRay=new{hit=debug.CameraHit,point=Text(debug.CameraHitPoint),distance=debug.CameraHitDistance,screen=Text(debug.CameraScreen),screenValid=debug.CameraScreenValid},
                eyeRay=new{hit=debug.EyeHit,point=Text(debug.EyeHitPoint),distance=debug.EyeHitDistance,screen=Text(debug.EyeScreen),screenValid=debug.EyeScreenValid,offsetPixels=debug.EyeOffsetPixels},
                selected=new{valid=debug.SelectedValid,point=Text(debug.SelectedPoint),screen=Text(debug.SelectedScreen),screenValid=debug.SelectedScreenValid,offsetPixels=debug.SelectedOffsetPixels,
                    name=debug.SelectedName??"",action=debug.SelectedAction??"",key=debug.SelectedKey??"",blocked=debug.SelectedBlocked},
                current=new{valid=debug.CurrentValid,point=Text(debug.CurrentPoint),screen=Text(debug.CurrentScreen),screenValid=debug.CurrentScreenValid,offsetPixels=debug.CurrentOffsetPixels,name=debug.CurrentName??""},
                lockCandidate=new{hit=debug.LockHit,eye=debug.EyeLock,camera=debug.CameraLock,inReach=debug.LockInReach,point=Text(debug.LockPoint),distance=debug.LockDistance,
                    screen=Text(debug.LockScreen),screenValid=debug.LockScreenValid,name=debug.LockName??"",reachMeters=PointerLockReach},
                observationActive=debug.ObservationActive,ownView=debug.OwnView,queries=pointerWorld.TotalQueries,
                note="Read-only P0 diagnostics; rays do not change native selection or melee hits."
            };
        }

        static string Text(NV value)=>value.X.ToString("0.000",CultureInfo.InvariantCulture)+","+value.Y.ToString("0.000",CultureInfo.InvariantCulture)+","+value.Z.ToString("0.000",CultureInfo.InvariantCulture);
        static string Text(NV2 value)=>value.X.ToString("0.0",CultureInfo.InvariantCulture)+","+value.Y.ToString("0.0",CultureInfo.InvariantCulture);
    }
}
