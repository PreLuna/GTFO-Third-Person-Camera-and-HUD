using System;
using System.IO;
using System.Text.Json;
using BepInEx;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Body;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;
using PreLuna.ThirdPerson.Core;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime : MonoBehaviour
    {
        public ThirdPersonRuntime(IntPtr pointer):base(pointer){}
        readonly PrototypeState state=new();
        readonly CameraStyleSolver cameraStyle=new();
        readonly SpatialCameraSolver collisionSolver=new();
        readonly EnvironmentRecovery environment=new();
        RigTarget rig;
        bool nativeDisplay=true;
        readonly UnityCameraWorld world=new();
        readonly CameraRenderStack cameraStack=new();
        readonly AimController aim=new();
        readonly ContextTracker context=new();
        readonly string sessionId=Guid.NewGuid().ToString("N");
        readonly PerformanceSession performance=new();
        object performanceMetadata;
        bool performanceRequested;
        readonly UnityAimWorld aimWorld=new();
        ShotAim shotAim;
        int adaptedShots;
        LocalPlayerAgent player;
        LocalBodyView body;
        readonly WorldWeaponView weapon=new();
        RenderScope scope;
        FirstPersonLayerView firstPerson;
        bool insidePreCull,stopped;
        long ownerId,modelId;
        int scopeFrame=-1,solverFrame=-1;
        float attackUntil;
        string detail="disabled";
        string audioHandling="not-observed";
        LogicalView logical;
        DisplayView display;
        Vector3 expectedBody,expectedEye;
        int renders,restores,collisionFallbacks,levelCleanups;
        bool? lastRenderRestored;
        FallbackReason previousReason=FallbackReason.Disabled;
        public void Awake(){state.Request(Plugin.Instance.Settings.Enabled.Value);InitializeHudBridge();}
        public void Update()
        {
            if(stopped||Plugin.Instance==null)return;
            Guard(()=>
            {
                if(scope!=null){EndRender();Fault("相机渲染未在当前帧结束，已恢复并停用");}
                var local=Local();
                if(context.Observe(local==null?default:new WorldContext(Id(local),Id(local.Owner),Id(LevelGeneration.Builder.CurrentFloor),(int)local.DimensionIndex,Id(local.PlayerSyncModel)),local==null?default:ConvertView.N(local.transform.position)))
                {aim.Cancel();Release();}
                if(player!=null&&(local==null||local.Pointer!=player.Pointer||Id(local.Owner)!=ownerId))Release();
                var gate=Gate(local);
                var settings=Plugin.Instance.Settings;
                var key=local!=null&&FocusAllowed()?ConfigurationPolicy.Key(Input.GetKeyDown(settings.Reset.Value),Input.GetKeyDown(settings.Toggle.Value),Input.GetKeyDown(settings.Snapshot.Value),Input.GetKeyDown(settings.SwapShoulder.Value),Input.GetKeyDown(settings.CyclePreset.Value),Input.GetKeyDown(settings.CaptureToggle.Value)):HotkeyAction.None;
                if(gate==FallbackReason.None)
                {
                    if(key==HotkeyAction.Toggle)
                    {state.Request(!state.Requested);Plugin.Instance.Settings.Enabled.Value=state.Requested;if(!state.Requested)Release();}
                    if(key==HotkeyAction.Reset)
                    {Release();Plugin.Instance.Settings.Defaults();state.ResetFault();state.Request(false);}
                    if(key==HotkeyAction.Performance)settings.CapturePerformance.Value=!settings.CapturePerformance.Value;
                    if(state.Requested&&!state.Faulted&&key==HotkeyAction.SwapShoulder)
                        Plugin.Instance.Settings.LeftShoulder.Value=!Plugin.Instance.Settings.LeftShoulder.Value;
                    if(state.Requested&&!state.Faulted&&key==HotkeyAction.CyclePreset)
                        Plugin.Instance.Settings.Preset.Value=(CameraPreset)(((int)Plugin.Instance.Settings.Preset.Value+1)%4);
                }
                if(key==HotkeyAction.Snapshot)TrySnapshot();
                state.Request(Plugin.Instance.Settings.Enabled.Value);
                state.Evaluate(gate);
                aim.Poll(local,gate==FallbackReason.None&&state.Requested&&!state.Faulted,Plugin.Instance.Settings.DoubleClickWindow.Value);
                if(gate!=FallbackReason.None||!state.Requested||state.Faulted)Release();
                TickPerformance(gate);
                ReportReason();
                RefreshHudBridge();
            });
        }
        [HideFromIl2Cpp] static long Id(object obj)=>obj is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value?value.Pointer.ToInt64():0;
        [HideFromIl2Cpp] static LocalPlayerAgent Local()=>GameStateManager.CurrentStateName==eGameStateName.InLevel?PlayerManager.GetLocalPlayerAgent()?.TryCast<LocalPlayerAgent>():null;
        [HideFromIl2Cpp] bool FocusAllowed()=>Application.isFocused&&InputMapper.PlayerHasFocus&&InputMapper.Current?.FocusSwitchPause!=true&&FocusStateManager.CurrentState==eFocusState.FPS&&!PlayerChatManager.InChatMode;
        [HideFromIl2Cpp] bool ControlTaken()
        {
            return Plugin.Instance.Control?.Read()??false;
        }
        [HideFromIl2Cpp] FallbackReason Gate(LocalPlayerAgent local)
        {
            if(GameStateManager.CurrentStateName!=eGameStateName.InLevel||local==null||!local.IsLocallyOwned||local.FPSCamera==null||local.Owner==null)return FallbackReason.NoPlayer;
            if(!FocusAllowed())return FallbackReason.Focus;
            if(ControlTaken())return FallbackReason.ControlTaken;
            if(!local.Alive)return FallbackReason.UnsupportedState;
            if(local.FPSCamera.m_hasPositionOverride||local.FPSCamera.m_hasRotationOverride)return FallbackReason.UnsupportedState;
            var locomotion=local.Locomotion;
            if(locomotion==null||locomotion.m_currentStateEnum is not (PlayerLocomotion.PLOC_State.Stand or PlayerLocomotion.PLOC_State.StandStill or PlayerLocomotion.PLOC_State.Crouch or PlayerLocomotion.PLOC_State.Run))return FallbackReason.UnsupportedState;
            return FallbackReason.None;
        }
        [HideFromIl2Cpp] internal void BeginPreCull(FPSCamera fps)
        {
            var local=Local();
            if(local?.FPSCamera==null||fps.Pointer!=local.FPSCamera.Pointer)return;
            hudProvider?.BeginMainAttempt();
            if(scope!=null){EndRender();Fault("检测到本机相机嵌套渲染");return;}
            insidePreCull=true;
        }
        [HideFromIl2Cpp] internal void PreCullFinished(FPSCamera fps,Exception error)
        {
            if(Local()?.FPSCamera?.Pointer!=fps.Pointer)return;
            insidePreCull=false;
            if(error!=null){EndRender();Fault("原版相机渲染前回调异常："+error.GetBaseException().Message);}
        }
        [HideFromIl2Cpp] internal void BeforeVisibility(CullingSystem.C_Camera culler)
        {
            if(!insidePreCull||state.Faulted)return;
            var local=Local();
            if(local?.FPSCamera?.m_cullingCamera?.Pointer!=culler.Pointer)return;
            var gate=Gate(local);state.Request(Plugin.Instance.Settings.Enabled.Value);state.Evaluate(gate);
            if(!state.CanRender){Release();return;}
            var item=local.Inventory?.WieldedItem;
            if(item==null||local.PlayerSyncModel?.ItemAlign==null){state.Evaluate(FallbackReason.MissingModel);Release();return;}
            PollAim(local);
            if(aim.Active&&aim.Gesture.Mode==ViewMode.FirstPersonAim){NativeDisplay("first-person-ads");return;}
            // Non-firearm right buttons keep their native scan/charge/deploy/throw meaning.
            bool attack=local.FPItemHolder?.ItemMeleeTrigger==true||!aim.Active&&(Input.GetMouseButton(0)||Input.GetMouseButton(1)
                ||InputMapper.GetButton.Invoke(InputAction.Fire,local.InputFilter)||InputMapper.GetButton.Invoke(InputAction.Aim,local.InputFilter)
                ||local.FPItemHolder?.ItemAimTrigger==true||local.FPItemHolder?.ItemIsBusy==true);
            if(attack)attackUntil=Time.unscaledTime+.25f;
            if(Time.unscaledTime<attackUntil){state.Evaluate(FallbackReason.Attack);Release();return;}
            Bind(local,item);
            var fps=local.FPSCamera;var camera=fps.m_camera;
            if(camera==null||!camera.enabled||fps.m_cullingCamera.m_cullAgent?.CurrentNode==null){state.Evaluate(FallbackReason.NoPlayer);Release();return;}
            var settings=Plugin.Instance.Settings.Camera;
            ComputeView(local);
            if(!display.IsThirdPerson)
            {collisionFallbacks++;state.Evaluate(FallbackReason.Collision);detail=display.Reason;HideItemCamera(false);hudProvider?.AbortMain();return;}
            expectedBody=local.transform.position;expectedEye=fps.Position;
            scopeFrame=Time.frameCount;
            lastRenderRestored=false;
            UpdateAimSolution(local);
            if(aim.Active&&!shotAim.Valid){HideItemCamera(false);display=display with{IsThirdPerson=false,Reason=shotAim.Reason};detail=shotAim.Reason;hudProvider?.AbortMain();return;}
            scope=new RenderScope(local,display,settings,rig.VerticalFov);
            cameraStack.Open(camera.Pointer.ToInt64(),scopeFrame);
            audioHandling=scope.AudioHandling;
            HideItemCamera(true);ShowMainGraphics();renders++;performance.Render(false);detail="rendering";
            PublishHudFrame(local);
        }
        [HideFromIl2Cpp] void Bind(LocalPlayerAgent local,ItemEquippable item)
        {
            if(player==null||player.Pointer!=local.Pointer||ownerId!=Id(local.Owner)||modelId!=Id(local.PlayerSyncModel))
            {
                Release();player=local;ownerId=Id(local.Owner);modelId=Id(local.PlayerSyncModel);state.Bind();
                body=new LocalBodyView(local);world.Bind(local.gameObject);firstPerson=new FirstPersonLayerView(local);
                cameraStyle.Reset(true,Plugin.Instance.Settings.Camera.MinimumDistance+.15f);collisionSolver.Reset();environment.Reset();nativeDisplay=true;
            }
            weapon.Bind(item);firstPerson.Bind(item);
        }
        [HideFromIl2Cpp] void HideItemCamera(bool hide)
        {
            firstPerson?.SetCamerasHidden(hide);
        }
        [HideFromIl2Cpp] void ShowMainGraphics()
        {firstPerson.RestoreGraphics();weapon.Show(player,true);body.Show(true,ConvertView.U(display.Position),Plugin.Instance.Settings.BodyFade.Value,Plugin.Instance.Settings.FadeNear,Plugin.Instance.Settings.FadeFar);firstPerson.HideGraphics();}
        [HideFromIl2Cpp] internal void OtherCameraBegin(Camera camera)
        {
            if(performance.Active&&camera!=null&&camera.Pointer!=Local()?.FPSCamera?.m_camera?.Pointer)performance.Render(true);
            if(camera!=null)hudProvider?.AuxiliaryBegin(ObjectKey(camera));
            if(scope==null||camera==null||camera.Pointer==player?.FPSCamera?.m_camera?.Pointer)return;
            if(cameraStack.BeginAuxiliary(camera.Pointer.ToInt64())){scope.Suspend();firstPerson.RestoreGraphics();body.Show(false);weapon.Show(player,false);}
        }
        [HideFromIl2Cpp] internal void CameraPreRender(Camera camera)
        {
            if(scope!=null&&camera?.Pointer==player?.FPSCamera?.m_camera?.Pointer)
            {cameraStack.ValidateMain();scope.Resume();ShowMainGraphics();}
        }
        [HideFromIl2Cpp] internal void CameraFinished(Camera camera)
        {
            if(camera==null)return;
            if(camera.Pointer==Local()?.FPSCamera?.m_camera?.Pointer)
            {if(scope!=null){cameraStack.ValidateMain();RestoreRender();}return;}
            if(scope!=null&&cameraStack.EndAuxiliary(camera.Pointer.ToInt64())){scope.Resume();ShowMainGraphics();}
            hudProvider?.AuxiliaryEnd(ObjectKey(camera));
        }
        [HideFromIl2Cpp] internal void EndFor(FPSCamera fps,bool succeeded)
        {
            if(fps?.Pointer!=Local()?.FPSCamera?.Pointer)return;
            if(scope!=null){cameraStack.ValidateMain();RestoreRender();}
            hudProvider?.NativeMainCompleted(succeeded);
        }
        [HideFromIl2Cpp] internal void BeforeLogic(FPSCamera fps)
        {if(scope!=null&&fps?.Pointer==player?.FPSCamera?.Pointer){EndRender();Fault("玩法更新前检测到未恢复的显示姿态");}}
        [HideFromIl2Cpp] internal void BeforeFire(Gear.BulletWeapon item)
        {if(scope!=null&&item?.Owner?.Pointer==player?.Pointer){EndRender();Fault("开火前检测到显示区间，已交还原版");}}
        [HideFromIl2Cpp] void PollAim(LocalPlayerAgent local)
        {aim.Poll(local,Gate(local)==FallbackReason.None&&Plugin.Instance.Settings.Enabled.Value&&state.Requested&&!state.Faulted,Plugin.Instance.Settings.DoubleClickWindow.Value);}
        [HideFromIl2Cpp] internal bool AimInput(FirstPersonItemHolder holder)
        {var local=Local();PollAim(local);return aim.Apply(holder);}
        [HideFromIl2Cpp] internal bool AimButton(ItemEquippable item,bool pressed,out bool value)
        {PollAim(Local());return aim.GetAim(item,pressed,out value);}
        [HideFromIl2Cpp] void NativeDisplay(string why)
        {
            if(!nativeDisplay){cameraStyle.Reset(true,Plugin.Instance.Settings.Camera.MinimumDistance+.15f);collisionSolver.Reset();environment.Reset();nativeDisplay=true;solverFrame=-1;}
            HideItemCamera(false);display=display with{IsThirdPerson=false,Reason=why};detail=why;
            hudProvider?.AbortMain();RefreshHudBridge();
        }
        [HideFromIl2Cpp] void ComputeView(LocalPlayerAgent local)
        {
            using var measured=performance.Measure();
            var fps=local.FPSCamera;var camera=fps.m_camera;var config=Plugin.Instance.Settings;var settings=config.Camera;
            logical=new(Time.frameCount,Time.fixedTime,state.Generation,ConvertView.N(local.transform.position),ConvertView.N(fps.Position),ConvertView.N(fps.Forward));
            float dt=solverFrame==Time.frameCount?0:Time.unscaledDeltaTime;solverFrame=Time.frameCount;
            if(nativeDisplay){cameraStyle.Reset(true,Plugin.Instance.Settings.Camera.MinimumDistance+.15f);collisionSolver.Reset();nativeDisplay=false;}
            var activity=aim.Active&&aim.Gesture.WantsAim?CameraActivity.ShoulderAim:local.Locomotion.m_currentStateEnum switch
            {PlayerLocomotion.PLOC_State.Run=>CameraActivity.Running,PlayerLocomotion.PLOC_State.Crouch=>CameraActivity.Crouching,_=>CameraActivity.Exploration};
            rig=cameraStyle.Step(logical,dt,activity,config.Preset.Value,config.LeftShoulder.Value,config.Style,camera.fieldOfView);
            world.Mask=Plugin.Instance.Settings.CollisionMask.Value;
            if(world.Mask==0)world.Mask=LayerManager.MASK_WORLD|LayerManager.MASK_PLAYER_INTERACT_BLOCKERS|(1<<LayerManager.LAYER_DYNAMIC);
            display=collisionSolver.Solve(Time.frameCount,rig,dt,Time.unscaledTime,camera.aspect,settings,config.Spatial,world);
            bool recovering=environment.Blocked;
            bool safe=environment.Evaluate(display.IsThirdPerson,display.IsThirdPerson?(display.Position-rig.Pivot).Length():0,settings.MinimumDistance,Time.unscaledTime,config.Spatial);
            if(!safe){display=display with{IsThirdPerson=false,Reason=display.IsThirdPerson?"environment-stabilizing":display.Reason};return;}
            if(recovering)
            {
                // Resume using current input intent and start near the original eye; never replay an old aim mode.
                cameraStyle.Reset(true,Plugin.Instance.Settings.Camera.MinimumDistance+.15f);collisionSolver.Reset();
                rig=cameraStyle.Step(logical,0,activity,config.Preset.Value,config.LeftShoulder.Value,config.Style,camera.fieldOfView);
                display=collisionSolver.Solve(Time.frameCount,rig,0,Time.unscaledTime,camera.aspect,settings,config.Spatial,world);
                if(!display.IsThirdPerson)environment.Evaluate(false,0,settings.MinimumDistance,Time.unscaledTime,config.Spatial);
            }
        }
        [HideFromIl2Cpp] void UpdateAimSolution(LocalPlayerAgent local)
        {
            var item=local.Inventory.WieldedItem;var fps=local.FPSCamera;
            // Shotgun native origin is the original camera Transform, regular rifles use the cached holder.
            var origin=item.TryCast<Gear.Shotgun>()!=null?fps.transform.position:fps.Position;
            aimWorld.Bind(local);
            shotAim=AimSolver.Solve(new(Time.frameCount,state.Generation,item.Pointer.ToInt64(),ConvertView.N(origin),display.Position,display.Forward,display.IsThirdPerson),
                Time.frameCount,state.Generation,item.Pointer.ToInt64(),ConvertView.N(origin),ConvertView.N(fps.Forward),250,aimWorld);
        }
        [HideFromIl2Cpp] internal FireAimScope StartFire(Gear.BulletWeapon item)
        {
            BeforeFire(item);
            var local=Local();
            if(local==null||item?.Owner?.Pointer!=local.Pointer||local.Inventory?.WieldedItem?.Pointer!=item.Pointer)return null;
            PollAim(local);
            if(!aim.Active||state.Faulted)return null;
            if(aim.Gesture.Mode==ViewMode.FirstPersonAim){NativeDisplay("first-person-ads");return null;}
            Bind(local,item);aim.Apply(local.FPItemHolder);ComputeView(local);
            if(!display.IsThirdPerson)return null;
            UpdateAimSolution(local);
            if(!shotAim.Valid)return null;
            adaptedShots++;
            return new FireAimScope(local.FPSCamera.transform.Pointer.ToInt64(),ConvertView.U(shotAim.Origin),ConvertView.U(shotAim.Target));
        }
        public void OnGUI()
        {
            if(performance.Active&&FocusAllowed())GUI.Label(new Rect(20,20,300,28),"性能采样："+performance.Count+" / "+performance.Capacity+"（再次按采样键结束）");
            if(stopped||!state.CanRender||!FocusAllowed()||!display.IsThirdPerson||display.Frame!=Time.frameCount||aim.Gesture.Mode==ViewMode.FirstPersonAim)return;
            var interaction=player?.Interaction;
            if(interaction?.HasWorldInteraction==true&&interaction.m_currentInteractionObj!=null)
                DrawWorldMarker(interaction.m_currentInteractionObj.transform.position,"○",Color.cyan);
            // Keep the native crosshair/spread UI; show the physical obstruction separately.
            if(aim.Active&&shotAim.Valid&&shotAim.Blocked)
            {
                DrawWorldMarker(ConvertView.U(shotAim.Impact),"×",Color.yellow);
                var color=GUI.color;
                try{GUI.color=Color.yellow;GUI.Label(new Rect(Screen.width/2+24,Screen.height/2+16,240,28),"射线路径受阻");}
                finally{GUI.color=color;}
            }
        }
        [HideFromIl2Cpp] void DrawWorldMarker(Vector3 point,string text,Color tint)
        {
            var camera=player?.FPSCamera?.m_camera;if(camera==null)return;
            // OnGUI runs outside the render scope, so use the displayed pose explicitly.
            var relative=Quaternion.Inverse(Quaternion.LookRotation(ConvertView.U(display.Forward),Vector3.up))*(point-ConvertView.U(display.Position));
            if(relative.z<=.001f)return;
            float fov=rig.VerticalFov;
            float scale=Screen.height/(2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f));
            var color=GUI.color;
            try{GUI.color=tint;GUI.Label(new Rect(Screen.width*.5f+relative.x*scale/relative.z-10,Screen.height*.5f-relative.y*scale/relative.z-12,40,30),text);}
            finally{GUI.color=color;}
        }
        [HideFromIl2Cpp] internal void EndRender()
        {hudProvider?.AbortMain();RestoreRender();}
        [HideFromIl2Cpp] void RestoreRender()
        {
            using var measured=performance.Measure(true);
            var active=scope;scope=null;insidePreCull=false;cameraStack.Clear();
            var recovery=new RestoreJournal();
            recovery.Add(()=>{if(active!=null)active.Dispose();});
            recovery.Add(()=>weapon.Show(player,false));recovery.Add(()=>body?.Show(false));recovery.Add(()=>firstPerson?.RestoreGraphics());
            try{recovery.Dispose();}
            catch{hudProvider?.MainRestored(false);throw;}
            if(active!=null)
            {
                lastRenderRestored=active.WasRestored;
                restores++;
                if(player!=null&&((player.transform.position-expectedBody).sqrMagnitude>.000001f||(player.FPSCamera.Position-expectedEye).sqrMagnitude>.000001f))
                    Fault("恢复检查发现人物位置或逻辑支点变化");
                detail="restored";
                hudProvider?.MainRestored(active.WasRestored&&!state.Faulted);
            }
        }
        [HideFromIl2Cpp] internal void Release()
        {
            hudProvider?.AbortMain();
            if(scope==null&&player==null&&body==null&&firstPerson==null&&weapon.ItemIdentity==0){insidePreCull=false;return;}
            var cleanup=new RestoreJournal();
            cleanup.Add(()=>{player=null;ownerId=0;modelId=0;firstPerson=null;body=null;cameraStyle.Reset(true,Plugin.Instance.Settings.Camera.MinimumDistance+.15f);collisionSolver.Reset();environment.Reset();nativeDisplay=true;solverFrame=-1;});
            cleanup.Add(()=>body?.Dispose());cleanup.Add(()=>weapon.Dispose());cleanup.Add(()=>firstPerson?.Dispose());cleanup.Add(EndRender);
            cleanup.Dispose();
        }
        [HideFromIl2Cpp] internal void LevelCleanup()
        {
            ResetHudContext();
            Exception failure=null;
            try{using var cleanup=new RestoreJournal();cleanup.Add(Release);cleanup.Add(aim.Cancel);}
            catch(Exception error){state.Fail();detail="level-cleanup-failed";failure=error;}
            finally{context.Reset();levelCleanups++;EndPerformance("level-cleanup");TrySnapshot("level-cleanup");Plugin.Instance?.Log.LogInfo("关卡清理结束："+levelCleanups+"；渲染/恢复="+renders+"/"+restores+"；成功="+(failure==null));}
            if(failure!=null)throw failure;
        }
        [HideFromIl2Cpp] internal void Guard(Action action)
        {try{action();}catch(Exception error){try{Release();}catch(Exception restore){Plugin.Instance?.Log.LogError("恢复失败："+restore);}Fault(error.GetBaseException().Message);}}
        [HideFromIl2Cpp] void Fault(string why)
        {try{aim.Cancel();}finally{state.Fail();detail=why;RefreshHudBridge();Plugin.Instance?.Log.LogError("第三人称已停用："+why);}}
        [HideFromIl2Cpp] void ReportReason()
        {if(previousReason!=state.Reason){previousReason=state.Reason;Plugin.Instance.Log.LogInfo("第三人称状态："+state.Reason);}}
        [HideFromIl2Cpp] internal void Snapshot(string captureReason="manual")
        {
            string root=Path.Combine(Paths.BepInExRootPath,"ThirdPerson-P5-诊断");Directory.CreateDirectory(root);
            var data=new{schemaVersion=3,utc=DateTime.UtcNow.ToString("o"),pluginVersion=Plugin.Version,gameRevision=Plugin.GameRevision,sessionId,processId=Environment.ProcessId,captureReason,
                frame=Time.frameCount,fixedTime=Time.fixedTime,requested=state.Requested,faulted=state.Faulted,reason=state.Reason.ToString(),detail,
                gameState=GameStateManager.CurrentStateName.ToString(),levelCleanups,generation=state.Generation,player=Id(player),owner=ownerId,item=weapon.ItemIdentity,bodyRenderers=body?.RendererCount??0,weaponParts=weapon.PartCount,bodyMode="original-local-model",gripPoseAdapted=false,
                scopeActive=scope!=null,scopeSuspended=scope?.Suspended??false,scopeFrame,lastRenderRestored,auxiliaryCameras=cameraStack.AuxiliaryCount,itemCameraHidden=firstPerson?.CamerasHidden??false,firstPerson=firstPerson?.Diagnostics(),renders,restores,collisionFallbacks,audioHandling,collisionBufferSaturated=world.BufferSaturated,
                hudBridge=HudBridgeDiagnostics(),
                logical=new{logical.Frame,logical.FixedTime,body=logical.Body.ToString(),eye=logical.Eye.ToString(),forward=logical.Forward.ToString()},
                display=new{display.Frame,position=display.Position.ToString(),display.Radius,display.IsThirdPerson,display.Reason},
                aim=new{active=aim.Active,mode=aim.Gesture.Mode.ToString(),pendingExit=aim.Gesture.PendingExit,equipment=aim.Equipment,adaptedShots,valid=shotAim.Valid,blocked=shotAim.Blocked,reason=shotAim.Reason,origin=shotAim.Origin.ToString(),target=shotAim.Target.ToString()},
                style=new{preset=Plugin.Instance.Settings.Preset.Value.ToString(),leftShoulder=Plugin.Instance.Settings.LeftShoulder.Value,activity=rig.Activity.ToString(),rig.Distance,rig.Shoulder,rig.VerticalFov,rig.Feedback,compressedShoulder=collisionSolver.CompressedShoulder,environmentBlocked=environment.Blocked,nativeDisplay,bodyFade=body?.FadeDiagnostics},
                parameters=new{style=Plugin.Instance.Settings.Style,spatial=Plugin.Instance.Settings.Spatial,camera=Plugin.Instance.Settings.Camera},
                candidateSHA256=Plugin.Instance.BinaryHash,resources=ResourceLedger.Snapshot(),physicsQueries=TotalQueries(),controlBridge=Plugin.Instance.Control?.Status,
                installedPlugins=Plugin.Instance.Inventory.Plugins,configurationCorrections=Plugin.Instance.Settings.Corrections,
                performance=new{performance.Active,performance.Count,performance.Capacity},
                note="P5 test candidate. Ownership counters mean release requested, not GPU destruction confirmed. Runtime acceptance/performance comparison remains required."};
            File.WriteAllText(Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
            if(GameStateManager.CurrentStateName==eGameStateName.InLevel)
            {
                try{File.WriteAllText(Path.Combine(root,"装备与发射路径清单.json"),JsonSerializer.Serialize(EquipmentCatalog.Capture(),new JsonSerializerOptions{WriteIndented=true}));}
                catch(Exception error){Plugin.Instance.Log.LogWarning("装备清单采集未完成："+error.GetBaseException().Message);}
            }
        }
        [HideFromIl2Cpp] void TrySnapshot(string reason="manual"){try{Snapshot(reason);}catch(Exception e){Plugin.Instance?.Log.LogWarning("诊断未写出："+e.GetBaseException().Message);}}
        [HideFromIl2Cpp] long TotalQueries()=>world.TotalQueries+aimWorld.TotalQueries;
        [HideFromIl2Cpp] void TickPerformance(FallbackReason gate)
        {
            var config=Plugin.Instance.Settings;
            bool playing=GameStateManager.CurrentStateName==eGameStateName.InLevel;
            if(performanceRequested&&(!config.CapturePerformance.Value||!playing)){EndPerformance("stopped-or-left-level");return;}
            if(!performanceRequested&&config.CapturePerformance.Value&&playing)
            {
                performance.Start(config.PerformanceFrames.Value);performanceRequested=true;
                Plugin.Instance.Log.LogInfo("性能采样开始，最多 "+config.PerformanceFrames.Value+" 帧；采样结束后写盘。");
                string condition=config.PerformanceCondition.Value?.Trim()??"";
                string configJson=JsonSerializer.Serialize(new{config.Style,config.Spatial,config.Camera,preset=config.Preset.Value.ToString(),left=config.LeftShoulder.Value,fade=config.BodyFade.Value,config.FadeNear,config.FadeFar,mask=config.CollisionMask.Value,doubleClickWindow=config.DoubleClickWindow.Value});
                performanceMetadata=new{version=Plugin.Version,candidateSHA256=Plugin.Instance.BinaryHash,revision=Plugin.GameRevision,condition,
                    conditionsConfirmed=condition.Length>=8&&condition!="未标定",resolution=Screen.width+"x"+Screen.height,quality=QualitySettings.GetQualityLevel(),floor=Id(LevelGeneration.Builder.CurrentFloor),
                    configurationSHA256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configJson))),startedAtUtc=DateTime.UtcNow.ToString("o")};
            }
            if(!performanceRequested)return;
            performance.BeginFrame(Time.frameCount,Time.unscaledDeltaTime*1000,config.Enabled.Value,gate==FallbackReason.None,TotalQueries());
            if(!performance.Active)EndPerformance("capacity-reached");
        }
        [HideFromIl2Cpp] void EndPerformance(string reason)
        {
            if(!performanceRequested)return;performance.Stop(TotalQueries());performanceRequested=false;
            Plugin.Instance.Settings.CapturePerformance.Value=false;
            try
            {
                string root=Path.Combine(Paths.BepInExRootPath,"ThirdPerson-P5-诊断");Directory.CreateDirectory(root);
                var data=new{sourceKind="runtime",metadata=performanceMetadata,reason,samples=performance.Export(),resources=ResourceLedger.Snapshot(),
                    note="FrameMs is whole-game frame interval including waits. SolveMs/RestoreMs are wall-clock measured sections; allocated bytes cover those sections only. No GPU timing or performance pass is inferred."};
                File.WriteAllText(Path.Combine(root,"性能-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
                Plugin.Instance.Log.LogInfo("性能记录已写出："+performance.Count+" 帧；"+reason);
            }
            catch(Exception e){Plugin.Instance.Log.LogWarning("性能记录未写出："+e.GetBaseException().Message);}
        }
        [HideFromIl2Cpp] internal void Stop()
        {
            if(stopped)return;stopped=true;
            StopHudBridge();
            using var cleanup=new RestoreJournal();cleanup.Add(()=>{EndPerformance("plugin-stop");TrySnapshot("plugin-stop");});cleanup.Add(Release);cleanup.Add(aim.Cancel);
        }
        public void OnDestroy(){Guard(Stop);}
    }
}


