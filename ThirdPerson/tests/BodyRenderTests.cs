using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PreLuna.ThirdPerson.Body;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using V=System.Numerics.Vector3;

static class BodyRenderTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool ok,string why="assertion failed"){if(!ok)throw new Exception(why);}
    static void Same(Vector3 a,Vector3 b){Check((a-b).magnitude<.0001f,"world position drift");}
    static void Same(Quaternion a,Quaternion b){Check(Quaternion.Angle(a,b)<.02f,"rotation drift");}
    static void Case(string name,Action action)
    {
        Time.unscaledTime=0;Time.frameCount=1;
        try{action();foreach(OwnedResource r in Enum.GetValues<OwnedResource>())Check(ResourceLedger.Live(r)==0,"resource leak "+r);results.Add(new(name,true,null));}
        catch(Exception e){results.Add(new(name,false,e.ToString()));}
    }
    static GameObject Node(string name,Transform parent=null){var o=new GameObject(name);o.transform.SetParent(parent,false);return o;}
    sealed class Fixture
    {
        internal readonly Player.LocalPlayerAgent Player;
        internal readonly PlayerSyncModelData Model;
        internal readonly SkinnedMeshRenderer Torso;
        internal readonly Collider Collider;
        internal Fixture(bool linked=false,bool inactive=false)
        {
            Player=Node("player").AddComponent<Player.LocalPlayerAgent>();
            Player.FPSCamera=Node("fps",Player.transform).AddComponent<FPSCamera>();
            var camera=Node("worldCamera",Player.FPSCamera.transform).AddComponent<Camera>();camera.transform.position=new(3,1.8f,5);
            Player.FPSCamera.m_camera=camera;Player.FPSCamera.m_itemCamera=Node("itemCamera").AddComponent<Camera>();
            Player.FPSCamera.m_cullingCamera=camera.gameObject.AddComponent<CullingSystem.C_Camera>();
            var obj=Node("body",linked?camera.transform:Player.transform);obj.transform.position=new(3,0,5);
            Model=obj.AddComponent<PlayerSyncModelData>();Player.PlayerSyncModel=Model;
            Model.m_neckBone=Node("neck",obj.transform).transform;Model.m_neckBone.localPosition=new(0,1.6f,0);
            Model.ItemAlign=Node("hand",obj.transform).transform;Model.ItemAlign.localPosition=new(.4f,1.2f,.2f);
            var torso=Node("torso",obj.transform);Torso=torso.AddComponent<SkinnedMeshRenderer>();Torso.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            Collider=torso.AddComponent<Collider>();torso.OnEnable=()=>Collider.enabled=true;
            torso.SetActive(!inactive);Model.m_gfxTorso=new[]{torso};Model.m_gfxHead=Model.m_gfxArms=Model.m_gfxLegs=Array.Empty<GameObject>();
            Player.AnimatorBody=obj.AddComponent<Animator>();
            Player.FPItemHolder=new(){FPSArms=new(){m_modelRoot=Node("fpsArms"),m_gfxArms=Array.Empty<GameObject>()}};
        }
        internal RenderScope Scope()=>new(Player,new(1,new V(5,2.4f,2),V.Normalize(new V(-1,-.2f,1)),.18f,true,"test"),CameraSettings.Default,80);
        internal ItemEquippable Item(bool mesh=true)
        {
            var item=Node("item",Player.FPSCamera.m_camera.transform).AddComponent<ItemEquippable>();
            item.transform.localPosition=new(.2f,-.3f,.6f);item.transform.localRotation=Quaternion.Euler(12,43,7);item.transform.localScale=new(1.2f,1.2f,1.2f);
            item.RightHandGripTrans=Node("grip",item.transform).transform;item.RightHandGripTrans.localPosition=new(.1f,-.1f,.25f);item.RightHandGripTrans.localRotation=Quaternion.Euler(17,-22,31);
            if(mesh)WeaponMesh(item);
            return item;
        }
    }
    static MeshRenderer WeaponMesh(ItemEquippable item)
    {var o=Node("weaponPart",item.transform);o.AddComponent<MeshFilter>().sharedMesh=new();return o.AddComponent<MeshRenderer>();}
    static void Main(string[] args)
    {
        Case("unknown-shader-inside-bounds-remains-visible",()=>{
            var f=new Fixture();using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true,Vector3.zero,true);
            Check(f.Torso.enabled&&f.Torso.shadowCastingMode==ShadowCastingMode.On);body.Show(false);Check(f.Torso.shadowCastingMode==ShadowCastingMode.ShadowsOnly);
        });
        Case("mixed-standard-gtfo-materials-stay-opaque",()=>{
            var f=new Fixture();f.Torso.sharedMaterials=new[]{new Material(new Shader("Standard")),new Material(new Shader("GTFO/Clothes"))};
            var before=f.Torso.sharedMaterials;using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true,Vector3.zero,true);Check(ReferenceEquals(before,f.Torso.sharedMaterials)&&f.Torso.shadowCastingMode==ShadowCastingMode.On);
        });
        Case("supported-alpha-is-restored-for-auxiliary",()=>{
            var f=new Fixture();var original=new Material(new Shader("Standard"));f.Torso.sharedMaterials=new[]{original};
            using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true,new(1.2f,0,0),true);Check(f.Torso.sharedMaterials[0]!=original&&f.Torso.sharedMaterials[0].Color.a<1);
            body.Show(false);Check(f.Torso.sharedMaterials[0]==original&&original.Color.a==1);
        });
        Case("inactive-body-prepared-once-with-camera-draw-gate",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);Check(!f.Torso.gameObject.activeSelf);
            body.Prepare();body.Show(true);Check(f.Torso.gameObject.activeInHierarchy&&!f.Collider.enabled);body.Show(false);Check(f.Torso.gameObject.activeSelf&&f.Torso.forceRenderingOff&&!f.Collider.enabled);body.EndPresentation();Check(!f.Torso.gameObject.activeSelf&&!f.Collider.enabled);
        });
        Case("skinning-culling-and-animator-restored",()=>{
            var f=new Fixture();using(var body=new LocalBodyView(f.Player)){
                body.Prepare();body.Show(true);Check(f.Torso.updateWhenOffscreen&&f.Player.AnimatorBody.cullingMode==AnimatorCullingMode.AlwaysAnimate);
                body.Show(false);Check(f.Torso.updateWhenOffscreen);body.EndPresentation();Check(!f.Torso.updateWhenOffscreen);
            }Check(f.Player.AnimatorBody.cullingMode==AnimatorCullingMode.CullCompletely);
        });
        Case("late-renderer-under-same-body-part-is-discovered",()=>{
            var f=new Fixture();using var body=new LocalBodyView(f.Player);var extra=Node("lateSleeve",f.Torso.transform).AddComponent<MeshRenderer>();extra.enabled=false;
            Time.unscaledTime=1;body.Refresh();Check(body.RendererCount==2);body.Prepare();body.Show(true);Check(extra.enabled);body.Show(false);Check(extra.enabled&&extra.forceRenderingOff);body.EndPresentation();Check(!extra.enabled);
        });
        Case("empty-body-can-recover-without-new-model-identity",()=>{
            var f=new Fixture();f.Model.m_gfxTorso=Array.Empty<GameObject>();using var body=new LocalBodyView(f.Player);Check(body.RendererCount==0);
            f.Model.m_gfxTorso=new[]{f.Torso.gameObject};Time.unscaledTime=1;body.Refresh();Check(body.RendererCount==1);
        });
        Case("replaced-vanity-root-is-not-reactivated",()=>{
            var f=new Fixture();using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);body.Show(false);
            f.Torso.gameObject.SetActive(false);var replacement=Node("newTorso",f.Model.transform).AddComponent<SkinnedMeshRenderer>();f.Model.m_gfxTorso=new[]{replacement.gameObject};
            Time.unscaledTime=1;body.Refresh();body.Prepare();body.Show(true);Check(body.RendererCount==1&&!f.Torso.gameObject.activeSelf&&replacement.enabled);body.Show(false);
        });
        Case("fps-hider-does-not-disable-shared-body-renderer",()=>{
            var f=new Fixture();f.Player.FPItemHolder.FPSArms.m_modelRoot=f.Model.gameObject;using var body=new LocalBodyView(f.Player);using var fp=new FirstPersonLayerView(f.Player);var item=f.Item();
            fp.Bind(item,body);body.Prepare();body.Show(true);fp.HideGraphics();Check(f.Torso.enabled&&fp.SkippedBodyGraphics==1);fp.RestoreGraphics();body.Show(false);
        });
        Case("camera-linked-model-neck-and-hand-keep-world-pose",()=>{
            var f=new Fixture(linked:true);var body=f.Model.transform.position;var neck=f.Model.m_neckBone.position;var hand=f.Model.ItemAlign.position;var rot=f.Model.transform.rotation;
            using(var scope=f.Scope()) {Check(scope.BodyCameraLinked);Same(f.Model.transform.position,body);Same(f.Model.m_neckBone.position,neck);Same(f.Model.ItemAlign.position,hand);Same(f.Model.transform.rotation,rot);}
            Same(f.Model.transform.position,body);Same(f.Model.m_neckBone.position,neck);
        });
        Case("independent-body-is-not-reparented-or-offset",()=>{
            var f=new Fixture();var parent=f.Model.transform.parent;var p=f.Model.transform.position;using(var scope=f.Scope()){Check(!scope.BodyCameraLinked);Same(p,f.Model.transform.position);}Check(f.Model.transform.parent==parent);
        });
        Case("render-size-diagnostics-preserve-inherited-model-scale",()=>{
            foreach(bool linked in new[]{false,true})
            {
                var f=new Fixture(linked:linked);
                f.Player.transform.localScale=new(1.25f,1.25f,1.25f);
                f.Model.transform.localScale=new(.95f,1.1f,1.05f);
                var local=f.Model.transform.localScale;var world=f.Model.transform.lossyScale;
                BodyScaleSnapshot captured;
                using(var scope=f.Scope())
                {
                    captured=scope.BodySizeBefore;
                    Check(captured.Frame==Time.frameCount&&captured.Model.Identity==f.Model.transform.Pointer.ToInt64());
                    Check(captured.Model.LocalScale==SizeVector.From(local)&&captured.Model.WorldScale==SizeVector.From(world));
                    using var json=JsonDocument.Parse(JsonSerializer.Serialize(captured));
                    Check(Math.Abs(json.RootElement.GetProperty("Model").GetProperty("WorldScale").GetProperty("Y").GetSingle()-world.y)<.0001f,"serialized scale missing");
                    Check(scope.BodySizeDuring.Model.LocalScale==captured.Model.LocalScale&&scope.BodySizeDuring.Model.WorldScale==captured.Model.WorldScale);
                    scope.Suspend();scope.Resume();Same(f.Model.transform.localScale,local);Same(f.Model.transform.lossyScale,world);
                }
                Same(f.Model.transform.localScale,local);Same(f.Model.transform.lossyScale,world);
                f.Model.transform.localScale=new(2,2,2);
                Check(captured.Model.LocalScale==SizeVector.From(local),"historical sample changed with live model");
            }
            Check(BodyScaleSnapshot.Capture(null,null,null).Model.Identity==0);
        });
        Case("known-local-rig-scale-normalizes-before-skinning-and-restores",()=>{
            foreach(bool linked in new[]{false,true})
            {
                var f=new Fixture(linked:linked);f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
                var playerScale=f.Player.transform.localScale;var playerPosition=f.Player.transform.position;
                var cameraPosition=f.Player.FPSCamera.m_camera.transform.position;var modelPosition=f.Model.transform.position;
                using(var body=new LocalBodyView(f.Player))
                {
                    body.Prepare();Same(f.Model.transform.localScale,new(1,1,1));Check(!body.ReadyForMain);
                    Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
                    using(var scope=f.Scope())
                    {Same(f.Model.transform.localScale,new(1,1,1));scope.Suspend();body.Show(false);scope.Resume();body.Show(true);Same(f.Model.transform.localScale,new(1,1,1));}
                    Same(f.Player.transform.localScale,playerScale);Same(f.Player.transform.position,playerPosition);
                    Same(f.Player.FPSCamera.m_camera.transform.position,cameraPosition);Same(f.Model.transform.position,modelPosition);
                    body.EndPresentation();Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
                    Time.frameCount++;body.Prepare();Same(f.Model.transform.localScale,new(1,1,1));
                }
                Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
            }
        });
        Case("normalizing-the-visual-root-preserves-parent-world-scale",()=>{
            var f=new Fixture();f.Player.transform.localScale=new(1.25f,1.25f,1.25f);f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            using(var body=new LocalBodyView(f.Player)){body.Prepare();Same(f.Model.transform.lossyScale,new(1.25f,1.25f,1.25f));}
            Same(f.Model.transform.lossyScale,new(1.5f,1.5f,1.5f));Same(f.Player.transform.localScale,new(1.25f,1.25f,1.25f));
        });
        Case("scale-correction-can-be-disabled-without-resetting-other-settings",()=>{
            var f=new Fixture();f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            using var body=new LocalBodyView(f.Player);body.Prepare(false);Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
            Time.frameCount++;body.Prepare(true);Same(f.Model.transform.localScale,new(1,1,1));Check(!body.ReadyForMain);
            Time.frameCount++;body.Prepare(true);Check(body.ReadyForMain);
            Time.frameCount++;body.Prepare(false);Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));Check(!body.ReadyForMain);
            body.EndPresentation();Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
        });
        Case("scale-correction-does-not-shrink-repeatedly-or-restart-native-skins",()=>{
            var f=new Fixture();f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            using(var body=new LocalBodyView(f.Player))
            {
                body.Prepare();Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
                for(int i=0;i<120;i++)
                {
                    if(i%2==0)f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
                    Time.frameCount++;body.Prepare();Check(body.ReadyForMain);Same(f.Model.transform.localScale,new(1,1,1));body.Show(true);body.Show(false);
                }
            }
            Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
        });
        Case("custom-nonuniform-negative-and-invalid-scales-are-preserved",()=>{
            foreach(var value in new[]{new Vector3(1,1,1),new Vector3(.8f,.8f,.8f),new Vector3(1.3f,1.3f,1.3f),
                new Vector3(1.2f,1,1.2f),new Vector3(-1.2f,1.2f,1.2f),new Vector3(float.NaN,1.2f,1.2f),new Vector3(float.PositiveInfinity,1.2f,1.2f)})
            {
                var root=Node("custom-model").transform;root.localScale=value;var correction=new LocalBodyScale(root);
                Check(!correction.Prepare(true)&&!correction.Applied);correction.Restore();
                Check(root.localScale.x.Equals(value.x)&&root.localScale.y.Equals(value.y)&&root.localScale.z.Equals(value.z));
            }
        });
        Case("newer-external-scale-is-not-overwritten-on-release",()=>{
            var f=new Fixture();f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            using(var body=new LocalBodyView(f.Player))
            {body.Prepare();f.Model.transform.localScale=new(1.1f,1.1f,1.1f);body.EndPresentation();}
            Same(f.Model.transform.localScale,new(1.1f,1.1f,1.1f));
            var correction=new LocalBodyScale(f.Model.transform);f.Model.transform.localScale=new(1.2f,1.2f,1.2f);correction.Prepare(true);
            f.Model.transform.localScale=new(1.4f,1.4f,1.4f);Check(correction.Prepare(true)&&!correction.Applied);correction.Restore();Same(f.Model.transform.localScale,new(1.4f,1.4f,1.4f));
        });
        Case("shared-fps-arm-branches-are-never-rescaled",()=>{
            var f=new Fixture();f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            f.Player.AnimatorArms=Node("shared-fps-arm",f.Model.transform).AddComponent<Animator>();
            using(var body=new LocalBodyView(f.Player)){body.Prepare();Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));}
        });
        Case("normalization-preparation-failure-restores-fps-scale",()=>{
            var f=new Fixture(inactive:true);f.Model.transform.localScale=new(1.2f,1.2f,1.2f);
            f.Torso.gameObject.OnEnable=()=>throw new Exception("native enable failure after normalization");
            using(var body=new LocalBodyView(f.Player)){bool failed=false;try{body.Prepare();}catch{failed=true;}Check(failed);}
            Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));Check(!f.Torso.gameObject.activeSelf);
        });
        Case("normalized-body-retains-native-weapon-size-and-hand-grip",()=>{
            var f=new Fixture();f.Model.transform.localScale=new(1.2f,1.2f,1.2f);var item=f.Item();var sourceScale=item.transform.lossyScale;
            var sourcePosition=item.transform.localPosition;var sourceRotation=item.transform.localRotation;
            using var body=new LocalBodyView(f.Player);using var weapon=new WorldWeaponView();Check(weapon.Bind(item));body.Prepare();
            using(var scope=f.Scope()){weapon.Show(f.Player,true);Check(weapon.GripPositionError<.0001f&&weapon.GripRotationError<.02f);Same(item.transform.lossyScale,sourceScale);weapon.Show(f.Player,false);}
            Same(item.transform.localPosition,sourcePosition);Same(item.transform.localRotation,sourceRotation);body.EndPresentation();Same(f.Model.transform.localScale,new(1.2f,1.2f,1.2f));
        });
        Case("external-neck-and-hand-branches-remain-at-animation-pose",()=>{
            var f=new Fixture();var camera=f.Player.FPSCamera.m_camera.transform;
            f.Model.m_neckBone.SetParent(camera,true);f.Model.ItemAlign.SetParent(camera,true);
            var n=f.Model.m_neckBone.position;var h=f.Model.ItemAlign.position;var parent=f.Model.m_neckBone.parent;
            using(var scope=f.Scope()){Check(scope.PinnedBodyTransforms==2);Same(n,f.Model.m_neckBone.position);Same(h,f.Model.ItemAlign.position);scope.Suspend();scope.Resume();Same(n,f.Model.m_neckBone.position);}
            Same(n,f.Model.m_neckBone.position);Check(f.Model.m_neckBone.parent==parent);
        });
        Case("nested-auxiliary-suspend-resume-does-not-drag-body",()=>{
            var f=new Fixture(linked:true);var p=f.Model.transform.position;var neck=f.Model.m_neckBone.position;var camera=f.Player.FPSCamera.m_camera.transform;var initial=camera.position;
            using var scope=f.Scope();var display=camera.position;
            for(int i=0;i<3;i++){scope.Suspend();Same(camera.position,initial);Same(f.Model.transform.position,p);scope.Resume();Same(camera.position,display);Same(f.Model.m_neckBone.position,neck);}
        });
        Case("linked-child-audio-remains-at-original-world-pose",()=>{
            var f=new Fixture(linked:true);var audio=Node("audio",f.Model.transform).AddComponent<Collider>();f.Player.FPSCamera.AkAudioListener=audio;var p=audio.transform.position;
            using(var scope=f.Scope())Same(p,audio.transform.position);Same(p,audio.transform.position);
        });
        Case("separate-culler-child-hierarchy-restores",()=>{
            var f=new Fixture();var culler=Node("culler",f.Player.FPSCamera.m_camera.transform).AddComponent<CullingSystem.C_Camera>();f.Player.FPSCamera.m_cullingCamera=culler;
            f.Model.transform.SetParent(culler.transform,true);var p=f.Model.transform.position;var local=f.Model.transform.localPosition;
            using(var scope=f.Scope()){Same(f.Model.transform.position,p);scope.Suspend();scope.Resume();Same(f.Model.transform.position,p);}
            Same(f.Model.transform.localPosition,local);
        });
        Case("constructor-failure-restores-parent-poses",()=>{
            var f=new Fixture();var c=f.Player.FPSCamera.m_camera;f.Player.PlayerSyncModel=c.gameObject.AddComponent<PlayerSyncModelData>();var p=c.transform.position;
            bool failed=false;try{using var scope=f.Scope();}catch(InvalidOperationException){failed=true;}Check(failed);Same(c.transform.position,p);
        });
        Case("native-visibility-failure-still-restores-model-and-camera",()=>{
            var f=new Fixture(linked:true);var p=f.Model.transform.position;var c=f.Player.FPSCamera.m_camera;var cp=c.transform.position;var scope=f.Scope();f.Player.FPSCamera.m_cullingCamera.Throw=true;
            bool failed=false;try{scope.Dispose();}catch{failed=true;}Check(failed&&scope.WasRestored);Same(p,f.Model.transform.position);Same(cp,c.transform.position);
        });
        Case("weapon-grip-matches-hand-with-rotated-scaled-source",()=>{
            var f=new Fixture(linked:true);f.Model.ItemAlign.localRotation=Quaternion.Euler(24,67,-14);var item=f.Item();var original=item.transform.localPosition;var rotation=item.transform.localRotation;
            using var weapon=new WorldWeaponView();Check(weapon.Bind(item));using(var scope=f.Scope()){
                weapon.Show(f.Player,true);Check(weapon.LastVisibleParts==1&&weapon.GripPositionError<.0001f&&weapon.GripRotationError<.02f);
                Same(item.transform.localPosition,original);Same(item.transform.localRotation,rotation);
            }
        });
        Case("late-weapon-parts-rebind-with-same-item-identity",()=>{
            var f=new Fixture();var item=f.Item(false);using var weapon=new WorldWeaponView();Check(!weapon.Bind(item));WeaponMesh(item);Time.unscaledTime=1;
            Check(weapon.Bind(item)&&weapon.PartCount==1);var extra=WeaponMesh(item);Time.unscaledTime=2;Check(weapon.Bind(item)&&weapon.PartCount==2);
            extra.enabled=false;weapon.Show(f.Player,true);Check(weapon.LastVisibleParts==1);
        });
        Case("weapon-repeated-main-resume-restores-source-graphics",()=>{
            var f=new Fixture();var item=f.Item();using var body=new LocalBodyView(f.Player);using var fp=new FirstPersonLayerView(f.Player);using var weapon=new WorldWeaponView();
            fp.Bind(item,body);Check(weapon.Bind(item));var source=item.GetComponentsInChildren<MeshRenderer>(true).Single();
            for(int i=0;i<3;i++){fp.RestoreGraphics();weapon.Show(f.Player,true);body.Prepare();body.Show(true);fp.HideGraphics();Check(source.enabled&&source.forceRenderingOff&&weapon.LastVisibleParts==1);}
            fp.RestoreGraphics();body.Show(false);Check(source.enabled&&!source.forceRenderingOff);
        });
        Case("removed-weapon-part-proxy-is-retired",()=>{
            var f=new Fixture();var item=f.Item();using var weapon=new WorldWeaponView();Check(weapon.Bind(item));var source=item.GetComponentsInChildren<MeshRenderer>(true).Single();
            source.transform.SetParent(null,true);Time.unscaledTime=1;Check(!weapon.Bind(item)&&weapon.PartCount==0);Check(ResourceLedger.Live(OwnedResource.VisualObject)==1);
        });
        Case("skinned-weapon-bakes-only-enabled-parts",()=>{
            var f=new Fixture();var item=f.Item(false);var skin=Node("skin",item.transform).AddComponent<SkinnedMeshRenderer>();using var weapon=new WorldWeaponView();Check(weapon.Bind(item));
            weapon.Show(f.Player,true);Check(skin.Bakes==1);skin.enabled=false;weapon.Show(f.Player,true);Check(skin.Bakes==1&&weapon.LastVisibleParts==0);
        });
        Case("reported-24-renderers-only-12-real-parts-display",()=>{
            var f=new Fixture();var roots=new List<GameObject>();var ghosts=new List<Renderer>();
            for(int i=0;i<12;i++){
                var real=Node("clothes"+i,f.Model.transform);real.AddComponent<SkinnedMeshRenderer>();roots.Add(real);
                var ghost=Node("clothes"+i+" (Ghost)",f.Model.transform);var r=ghost.AddComponent<SkinnedMeshRenderer>();r.sharedMaterials=new[]{new Material(new Shader("Cell/Player/PlayerGhost"))};roots.Add(ghost);ghosts.Add(r);
            }
            f.Model.m_gfxTorso=roots.ToArray();f.Model.m_ghostRenderers=ghosts.ToArray();
            using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true,Vector3.zero,true);
            Check(body.RendererCount==12&&body.GhostRendererCount==12&&ghosts.All(r=>r.forceRenderingOff));
            Check(roots.Where(r=>!r.name.EndsWith("(Ghost)")).All(r=>r.GetComponent<Renderer>().enabled));body.Show(false);Check(ghosts.All(r=>r.enabled));
        });
        Case("registered-ghost-excluded-without-name-or-shader-heuristic",()=>{
            var f=new Fixture();var ghost=Node("effect",f.Model.transform).AddComponent<MeshRenderer>();f.Model.m_gfxHead=new[]{ghost.gameObject};f.Model.m_ghostRenderers=new[]{ghost};
            using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);Check(ghost.forceRenderingOff&&body.RendererCount==1&&body.GhostRendererCount==1);body.Show(false);Check(ghost.enabled);
        });
        Case("ghost-shader-fallback-does-not-activate-ghost-only-root",()=>{
            var f=new Fixture();var ghost=Node("unregistered",f.Model.transform).AddComponent<SkinnedMeshRenderer>();ghost.sharedMaterials=new[]{new Material(new Shader("Cell/Player/PlayerGhost"))};ghost.gameObject.SetActive(false);
            bool activated=false;ghost.gameObject.OnEnable=()=>activated=true;f.Model.m_gfxHead=new[]{ghost.gameObject};
            using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);Check(ghost.forceRenderingOff&&!ghost.gameObject.activeSelf&&!activated);body.Show(false);Check(ghost.enabled&&!ghost.gameObject.activeSelf);
        });
        Case("mixed-root-shows-real-graphics-but-suppresses-ghost",()=>{
            var f=new Fixture(inactive:true);var ghost=Node("nestedEffect",f.Torso.transform).AddComponent<MeshRenderer>();ghost.sharedMaterials=new[]{new Material(new Shader("Cell/Player/PlayerGhost"))};
            using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);Check(f.Torso.enabled&&f.Torso.gameObject.activeInHierarchy&&ghost.forceRenderingOff);body.Show(false);Check(f.Torso.gameObject.activeSelf&&f.Torso.forceRenderingOff&&ghost.forceRenderingOff);body.EndPresentation();Check(!f.Torso.gameObject.activeSelf&&ghost.enabled);
        });
        Case("ghost-like-name-with-normal-material-is-kept",()=>{
            var f=new Fixture();f.Torso.gameObject.name="Legitimate Ghost Outfit";using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);Check(f.Torso.enabled&&body.RendererCount==1&&body.GhostRendererCount==0);
        });
        Case("late-ghost-renderer-is-suppressed-and-restored",()=>{
            var f=new Fixture();using var body=new LocalBodyView(f.Player);var ghost=Node("lateGhost",f.Torso.transform).AddComponent<MeshRenderer>();f.Model.m_ghostRenderers=new[]{ghost};
            Time.unscaledTime=1;body.Refresh();body.Prepare();body.Show(true);Check(ghost.forceRenderingOff&&body.GhostRendererCount==1);body.Show(false);Check(ghost.enabled);
        });
        Case("hidden-fps-animation-sources-continue-and-restore-culling",()=>{
            var f=new Fixture();f.Player.AnimatorArms=Node("fpsArmsAnimator").AddComponent<Animator>();f.Player.FPItemHolder.m_fpsItemAnim=Node("itemAnimator").AddComponent<Animator>();
            using(var fp=new FirstPersonLayerView(f.Player)){Check(f.Player.AnimatorArms.cullingMode==AnimatorCullingMode.AlwaysAnimate&&f.Player.FPItemHolder.m_fpsItemAnim.cullingMode==AnimatorCullingMode.AlwaysAnimate);}
            Check(f.Player.AnimatorArms.cullingMode==AnimatorCullingMode.CullCompletely&&f.Player.FPItemHolder.m_fpsItemAnim.cullingMode==AnimatorCullingMode.CullCompletely);
        });
        Case("fps-camera-alias-constructor-failure-does-not-leak-animation-mode",()=>{
            var f=new Fixture();f.Player.AnimatorArms=Node("arms").AddComponent<Animator>();f.Player.FPSCamera.m_itemCamera=f.Player.FPSCamera.m_camera;
            bool failed=false;try{using var fp=new FirstPersonLayerView(f.Player);}catch(InvalidOperationException){failed=true;}
            Check(failed&&f.Player.AnimatorArms.cullingMode==AnimatorCullingMode.CullCompletely);
        });
        Case("new-skins-wait-one-native-frame-before-main",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);
            body.Prepare();Check(!body.ReadyForMain&&f.Torso.updateWhenOffscreen&&f.Torso.forceRenderingOff);
            Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
            Time.frameCount++;Check(!body.ReadyForMain,"a frame without early preparation must not render");
            body.Prepare();Check(body.ReadyForMain);
        });
        Case("120-frames-and-aux-cameras-do-not-restart-native-skins",()=>{
            var f=new Fixture(inactive:true);f.Torso.enabled=false;using var body=new LocalBodyView(f.Player);body.Prepare();
            int active=f.Torso.gameObject.ActiveTransitions,enabled=f.Torso.EnableTransitions,offscreen=f.Torso.OffscreenTransitions;
            for(int i=0;i<120;i++){
                Time.frameCount++;body.Prepare();Check(body.ReadyForMain);body.Show(true);
                Check(f.Torso.enabled&&!f.Torso.forceRenderingOff);
                body.Show(false);Check(f.Torso.forceRenderingOff&&f.Torso.updateWhenOffscreen&&f.Torso.gameObject.activeSelf);
                body.Show(true);body.Show(false);
            }
            Check(f.Torso.gameObject.ActiveTransitions==active&&f.Torso.EnableTransitions==enabled&&f.Torso.OffscreenTransitions==offscreen,"camera callbacks restarted native skin state");
            body.EndPresentation();Check(!f.Torso.gameObject.activeSelf&&!f.Torso.enabled&&!f.Torso.updateWhenOffscreen&&!f.Torso.forceRenderingOff);
        });
        Case("late-equipment-warmup-invalidates-prepared-main-immediately",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);body.Prepare();Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
            body.Warmup();Check(!body.ReadyForMain);body.Prepare();Check(!body.ReadyForMain);
            Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
        });
        Case("main-callback-cannot-activate-an-unprepared-model",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);int changes=f.Torso.gameObject.ActiveTransitions;
            bool failed=false;try{body.Show(true);}catch(InvalidOperationException){failed=true;}
            Check(failed&&!f.Torso.gameObject.activeSelf&&f.Torso.gameObject.ActiveTransitions==changes&&!f.Torso.updateWhenOffscreen);
        });
        Case("native-force-off-is-preserved-outside-main-and-on-release",()=>{
            var f=new Fixture();f.Torso.forceRenderingOff=true;using var body=new LocalBodyView(f.Player);body.Prepare();
            body.Show(true);Check(!f.Torso.forceRenderingOff);body.Show(false);Check(f.Torso.forceRenderingOff);
            body.EndPresentation();Check(f.Torso.forceRenderingOff&&f.Torso.shadowCastingMode==ShadowCastingMode.ShadowsOnly);
        });
        Case("late-skins-have-their-own-warmup-and-native-state",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);body.Prepare();Time.frameCount++;body.Prepare();Check(body.ReadyForMain);
            var extra=Node("lateGlove",f.Torso.transform).AddComponent<SkinnedMeshRenderer>();extra.enabled=false;
            Time.unscaledTime=1;Time.frameCount++;body.Prepare();Check(!body.ReadyForMain&&extra.enabled&&extra.updateWhenOffscreen&&extra.forceRenderingOff);
            Time.frameCount++;body.Prepare();Check(body.ReadyForMain);body.Show(true);Check(!extra.forceRenderingOff);
            body.Show(false);Check(extra.forceRenderingOff);body.EndPresentation();Check(!extra.enabled&&!extra.updateWhenOffscreen&&!f.Torso.gameObject.activeSelf);
        });
        Case("retired-activated-root-does-not-leak-into-world",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);body.Prepare();Check(f.Torso.gameObject.activeSelf);
            f.Model.m_gfxTorso=Array.Empty<GameObject>();Time.unscaledTime=1;body.Refresh();
            Check(body.RendererCount==0&&!f.Torso.gameObject.activeSelf&&!f.Torso.updateWhenOffscreen);
        });
        Case("native-state-is-recaptured-after-first-person-fallback",()=>{
            var f=new Fixture(inactive:true);using var body=new LocalBodyView(f.Player);body.Prepare();body.Show(true);body.EndPresentation();
            f.Torso.enabled=false;f.Torso.forceRenderingOff=true;f.Torso.updateWhenOffscreen=true;
            body.Prepare();body.Show(true);body.EndPresentation();body.EndPresentation();
            Check(!f.Torso.enabled&&f.Torso.forceRenderingOff&&f.Torso.updateWhenOffscreen&&!f.Torso.gameObject.activeSelf);
        });
        Case("fps-camera-draw-gate-does-not-toggle-renderer-enabled",()=>{
            var f=new Fixture();var item=f.Item();var source=item.GetComponentsInChildren<MeshRenderer>(true).Single();source.forceRenderingOff=true;
            using var body=new LocalBodyView(f.Player);using var fp=new FirstPersonLayerView(f.Player);fp.Bind(item,body);int changes=source.EnableTransitions;
            for(int i=0;i<20;i++){fp.HideGraphics();Check(source.forceRenderingOff&&source.enabled);fp.RestoreGraphics();Check(source.forceRenderingOff);}
            Check(source.EnableTransitions==changes);
        });
        Case("preparation-failure-restores-activated-node-and-collider",()=>{
            var f=new Fixture(inactive:true);f.Torso.gameObject.OnEnable=()=>{f.Collider.enabled=true;throw new Exception("native enable failure");};
            using(var body=new LocalBodyView(f.Player)){
                bool failed=false;try{body.Prepare();}catch{failed=true;}Check(failed);
            }
            Check(!f.Torso.gameObject.activeSelf&&!f.Collider.enabled&&!f.Torso.updateWhenOffscreen&&!f.Torso.forceRenderingOff&&f.Player.AnimatorBody.cullingMode==AnimatorCullingMode.CullCompletely);
        });
        var output=args.Length>0?args[0]:"BodyRenderTests.json";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output,JsonSerializer.Serialize(new{Scope="Production adapters with Unity/GTFO stand-ins; not gameplay or GPU validation",Checks=results.Count,Passed=results.All(r=>r.Passed),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine((r.Passed?"PASS ":"FAIL ")+r.Name+(r.Error==null?"":" "+r.Error));
        if(results.Any(r=>!r.Passed))Environment.Exit(1);
    }
}
