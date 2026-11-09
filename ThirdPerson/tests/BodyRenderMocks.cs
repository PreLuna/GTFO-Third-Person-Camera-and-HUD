// Controlled Unity/GTFO stand-ins. These exercise production display adapters,
// not Unity shaders, native animation, or actual camera rendering.
using System;
using System.Collections.Generic;
using System.Linq;
using Num=System.Numerics;

namespace Il2CppInterop.Runtime
{public static class Cast{public static T TryCast<T>(this object value) where T:class=>value as T;}}
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode{Off,On,TwoSided,ShadowsOnly}
    public enum BlendMode{SrcAlpha,OneMinusSrcAlpha}
    public enum RenderQueue{Transparent=3000}
}
namespace UnityEngine
{
    public enum HideFlags{HideAndDontSave}
    public enum AnimatorCullingMode{AlwaysAnimate,CullUpdateTransforms,CullCompletely}
    public static class Time{public static float unscaledTime,time;public static int frameCount;}
    public class Object
    {
        static long next;
        public IntPtr Pointer {get;}=new(++next);
        public string name;
        public HideFlags hideFlags;
        public bool Destroyed;
        public static readonly List<GameObject> Objects=new();
        public static void Destroy(Object o){o.Destroyed=true;}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        internal Num.Vector3 N=>new(x,y,z);
        internal static Vector3 U(Num.Vector3 v)=>new(v.X,v.Y,v.Z);
        public static Vector3 zero=>new();
        public static Vector3 up=>new(0,1,0);
        public float sqrMagnitude=>N.LengthSquared();
        public float magnitude=>N.Length();
        public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>U(a.N+b.N);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>U(a.N-b.N);
        public static Vector3 operator *(Vector3 a,float b)=>U(a.N*b);
    }
    public struct Quaternion
    {
        internal Num.Quaternion N;
        public float x=>N.X;
        public float y=>N.Y;
        public float z=>N.Z;
        public float w=>N.W;
        internal static Quaternion U(Num.Quaternion q)=>new(){N=q};
        public static Quaternion identity=>U(Num.Quaternion.Identity);
        public static Quaternion Inverse(Quaternion q)=>U(Num.Quaternion.Inverse(q.N));
        public static Quaternion Euler(float x,float y,float z)=>U(Num.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180));
        public static Quaternion FromToRotation(Vector3 from,Vector3 to)
        {
            var a=Num.Vector3.Normalize(from.N);var b=Num.Vector3.Normalize(to.N);var dot=Num.Vector3.Dot(a,b);
            if(dot>.999999f)return identity;
            if(dot<-.999999f){var axis=Num.Vector3.Normalize(Num.Vector3.Cross(a,Math.Abs(a.X)>.9f?Num.Vector3.UnitY:Num.Vector3.UnitX));return U(Num.Quaternion.CreateFromAxisAngle(axis,MathF.PI));}
            return U(Num.Quaternion.Normalize(new Num.Quaternion(Num.Vector3.Cross(a,b),1+dot)));
        }
        public static Quaternion operator *(Quaternion a,Quaternion b)=>U(a.N*b.N);
        public static Vector3 operator *(Quaternion q,Vector3 p)=>Vector3.U(Num.Vector3.Transform(p.N,q.N));
        public static float Angle(Quaternion a,Quaternion b)
        {
            float dot=Math.Abs(Num.Quaternion.Dot(Num.Quaternion.Normalize(a.N),Num.Quaternion.Normalize(b.N)));
            return dot>.999999f?0:2*MathF.Acos(Math.Clamp(dot,0,1))*180/MathF.PI;
        }
        public static Quaternion LookRotation(Vector3 forward,Vector3 up)
        {
            var z=Num.Vector3.Normalize(forward.N);var x=Num.Vector3.Normalize(Num.Vector3.Cross(up.N,z));var y=Num.Vector3.Cross(z,x);
            return U(Num.Quaternion.CreateFromRotationMatrix(new(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1)));
        }
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
        public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class=>gameObject.GetComponentsInChildren<T>(inactive);
    }
    public class Transform:Object
    {
        public GameObject gameObject;
        public Transform parent;
        public readonly List<Transform> children=new();
        public Vector3 localPosition;
        public Quaternion localRotation=Quaternion.identity;
        public Vector3 localScale=new(1,1,1);
        public bool ThrowOnRotationWrite;
        public Vector3 position {get=>parent==null?localPosition:parent.TransformPoint(localPosition);set=>localPosition=parent==null?value:parent.InverseTransformPoint(value);}
        public Quaternion rotation {get=>parent==null?localRotation:parent.rotation*localRotation;set{if(ThrowOnRotationWrite)throw new InvalidOperationException("injected bone failure");localRotation=parent==null?value:Quaternion.Inverse(parent.rotation)*value;}}
        public Vector3 lossyScale=>parent==null?localScale:Vector3.Scale(parent.lossyScale,localScale);
        public Vector3 forward=>rotation*new Vector3(0,0,1);
        public bool IsChildOf(Transform other)=>this==other||parent!=null&&parent.IsChildOf(other);
        public void SetParent(Transform next,bool worldPositionStays=true)
        {
            var p=position;var q=rotation;parent?.children.Remove(this);parent=next;parent?.children.Add(this);
            if(worldPositionStays)SetPositionAndRotation(p,q);
        }
        public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;}
        public Vector3 TransformPoint(Vector3 p)
        {var own=localPosition+localRotation*Vector3.Scale(localScale,p);return parent==null?own:parent.TransformPoint(own);}
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            var own=parent==null?p:parent.InverseTransformPoint(p);var v=Quaternion.Inverse(localRotation)*(own-localPosition);
            return new(v.x/localScale.x,v.y/localScale.y,v.z/localScale.z);
        }
    }
    public class GameObject:Object
    {
        readonly List<Component> components=new();
        public Transform transform;
        public bool activeSelf=true;
        public int layer;
        public Action OnEnable;
        public int ActiveTransitions;
        public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
        public GameObject(string n=""){name=n;transform=new(){gameObject=this};Objects.Add(this);}
        public void SetActive(bool active){bool was=activeSelf;activeSelf=active;if(active!=was)ActiveTransitions++;if(active&&!was)OnEnable?.Invoke();}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
        public T GetComponent<T>() where T:class=>components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class
        {
            var list=new List<T>();
            if(inactive||activeInHierarchy)list.AddRange(components.OfType<T>().Where(c=>c is not Object o||!o.Destroyed));
            foreach(var t in transform.children)list.AddRange(t.gameObject.GetComponentsInChildren<T>(inactive));
            return list.ToArray();
        }
    }
    public struct Color{public float r,g,b,a;public Color(float alpha){r=g=b=1;a=alpha;}}
    public class Shader:Object{public Shader(string n){name=n;}}
    public class Material:Object
    {
        public Shader shader;
        public Color Color=new(1);
        public int renderQueue;
        public Material(Shader s){shader=s;}
        public Material(Material m){shader=m.shader;Color=m.Color;}
        public bool HasProperty(string n)=>shader.name=="Standard";
        public Color GetColor(string n)=>Color;
        public void SetColor(string n,Color c){Color=c;}
        public void SetFloat(string n,float v){}
        public void SetInt(string n,int v){}
        public void DisableKeyword(string n){}
        public void EnableKeyword(string n){}
    }
    public struct Bounds
    {
        public Vector3 center,size;
        public Vector3 ClosestPoint(Vector3 p)=>new(Math.Clamp(p.x,center.x-size.x/2,center.x+size.x/2),Math.Clamp(p.y,center.y-size.y/2,center.y+size.y/2),Math.Clamp(p.z,center.z-size.z/2,center.z+size.z/2));
    }
    public class Renderer:Component
    {
        bool enabledValue=true;
        public int EnableTransitions;
        public bool enabled{get=>enabledValue;set{if(enabledValue!=value)EnableTransitions++;enabledValue=value;}}
        public bool forceRenderingOff;
        public Rendering.ShadowCastingMode shadowCastingMode=Rendering.ShadowCastingMode.On;
        public Material[] sharedMaterials={new(new Shader("GTFO/Player"))};
        public Bounds bounds=new(){center=Vector3.zero,size=new(2,2,2)};
    }
    public class MeshRenderer:Renderer{}
    public class SkinnedMeshRenderer:Renderer
    {public Mesh sharedMesh=new();bool offscreen;public int OffscreenTransitions;public bool updateWhenOffscreen{get=>offscreen;set{if(offscreen!=value)OffscreenTransitions++;offscreen=value;}}public int Bakes;public void BakeMesh(Mesh mesh){Bakes++;}}
    public class MeshFilter:Component{public Mesh sharedMesh;}
    public class Mesh:Object{public void MarkDynamic(){}}
    public class Collider:Component{public bool enabled;}
    public enum HumanBodyBones{RightUpperArm,RightLowerArm,RightHand,LeftUpperArm,LeftLowerArm,LeftHand}
    public enum AvatarIKGoal{LeftHand,RightHand}
    public class RuntimeAnimatorController:Object{}
    public struct AnimatorStateInfo{public int fullPathHash,shortNameHash;public float normalizedTime,length,speed,speedMultiplier;}
    public class Animator:Component
    {
        public AnimatorCullingMode cullingMode=AnimatorCullingMode.CullCompletely;
        public bool isHuman;
        public readonly Dictionary<HumanBodyBones,Transform> Bones=new();
        public int layerCount=9;
        public bool AllowPlayback,ThrowOnPlay;
        public int ThrowOnLayer=-1;
        public RuntimeAnimatorController runtimeAnimatorController=new();
        public int PlayCalls,WeightCalls;
        public readonly Dictionary<int,float> Weights=new();
        public readonly Dictionary<int,AnimatorStateInfo> States=new();
        public readonly HashSet<(int Layer,int Hash)> AvailableStates=new();
        public readonly HashSet<int> Transitions=new();
        public static int StringToHash(string name){unchecked{int hash=17;foreach(char c in name)hash=hash*31+c;return hash;}}
        public bool HasState(int layer,int hash)=>AvailableStates.Contains((layer,hash));
        public bool IsInTransition(int layer)=>Transitions.Contains(layer);
        public float GetLayerWeight(int layer)=>Weights.TryGetValue(layer,out float w)?w:0;
        public void SetLayerWeight(int layer,float w){if(!AllowPlayback)throw new Exception("FPS/native animation weight modified");Weights[layer]=w;WeightCalls++;}
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)=>States.TryGetValue(layer,out var s)?s:default;
        public void Play(int state,int layer,float time){if(!AllowPlayback)throw new Exception("FPS/native animation state modified");if(ThrowOnPlay||layer==ThrowOnLayer)throw new Exception("injected animator failure");States[layer]=new(){fullPathHash=state,shortNameHash=state,normalizedTime=time,length=1,speed=1,speedMultiplier=1};PlayCalls++;}
        public Transform GetBoneTransform(HumanBodyBones b)=>Bones.TryGetValue(b,out var t)?t:null;
        public void Update(float dt)=>throw new Exception("Native animation must not be re-evaluated for display");
        public void Play(string state)=>throw new Exception("Native animation state must not be replaced for display");
    }
    public class Camera:Component{public bool enabled=true;public float nearClipPlane=.1f,fieldOfView=65;public int cullingMask=-1;}
}
public class FPSCamera:UnityEngine.Component
{public UnityEngine.Camera m_camera,m_itemCamera;public CullingSystem.C_Camera m_cullingCamera;public UnityEngine.Component AkAudioListener;}
public class PlayerSyncModelData:UnityEngine.Component
{public UnityEngine.GameObject[] m_gfxTorso,m_gfxLegs,m_gfxArms,m_gfxHead;public UnityEngine.Renderer[] m_ghostRenderers;public UnityEngine.Transform ItemAlign,m_neckBone;}
public class PlayerFPSBody
{public UnityEngine.GameObject m_modelRoot;public UnityEngine.GameObject[] m_gfxArms;}
public class FirstPersonItemHolder
{public UnityEngine.Camera m_ItemLayerCamera;public PlayerFPSBody FPSArms;public UnityEngine.Animator m_fpsItemAnim;}
public class ItemEquippable:UnityEngine.Component
{public UnityEngine.Transform RightHandGripTrans,LeftHandGripTrans;public string LeftHandGripAnim="LeftGrip",RightHandGripAnim="RightGrip";public GameData.ItemDataBlock ItemDataBlock=new();public Player.pItemData pItemData=new(){slot=Player.InventorySlot.GearSpecial};public bool IsReloading;}
namespace Gear
{public class BulletWeapon:ItemEquippable{public float m_lastFireTime;public int m_clip=10;}public class Shotgun:BulletWeapon{}public class RifleWeapon:BulletWeapon{}}
namespace GameData
{public class ItemDataBlock{public Player.InventorySlot inventorySlot=Player.InventorySlot.GearSpecial;}}
namespace Player
{
    public enum InventorySlot{None,GearStandard,GearSpecial,GearClass,ResourcePack,GearMelee=10}
    public struct pItemData{public InventorySlot slot;}
    public class PlayerAgent:UnityEngine.Component
    {public const int ANIM_LAYER_FULL_BODY=1,ANIM_LAYER_3RD_AIM_OFFSET=4,ANIM_LAYER_3RD_UPPERBODY=5,ANIM_LAYER_3RD_ADDITIVE=6,ANIM_LAYER_LEFT_HAND=7,ANIM_LAYER_RIGHT_HAND=8;}
    public class PlayerLocomotion
    {public enum ePlayerAnimation{Movement}public string Movement="Rifle_Movement";public string GetAnimationForWielded(ePlayerAnimation animation)=>Movement;}
    public class LocalPlayerAgent:PlayerAgent
    {public PlayerSyncModelData PlayerSyncModel;public UnityEngine.Animator AnimatorBody,AnimatorArms;public PlayerSyncIK SyncIK;public PlayerLocomotion Locomotion=new();public FPSCamera FPSCamera;public FirstPersonItemHolder FPItemHolder;}
    public class PlayerSyncIK
    {public RootMotion.FinalIK.BipedIK m_bipedIK,m_fullbodyBipedIK;public RootMotion.FinalIK.IKSolverLimb m_rightArmSolver,m_leftArmSolver;public RootMotion.FinalIK.IKSolverAim m_aimSolver;}
}
namespace RootMotion
{public class BipedReferences{public UnityEngine.Transform rightUpperArm,rightForearm,rightHand,leftUpperArm,leftForearm,leftHand;}}
namespace RootMotion.FinalIK
{
    public class BipedIK{public RootMotion.BipedReferences references;}
    public class IKSolver
    {
        public static readonly List<IKSolver> Instances=new();
        public static bool FailNewLimb;
        public IKSolver(){Instances.Add(this);}
        public UnityEngine.Vector3 IKPosition;
        public float IKPositionWeight;
        public int Updates;
        public bool ThrowOnUpdate;
        public virtual void Update(){if(ThrowOnUpdate)throw new Exception("injected native IK failure");Updates++;}
    }
    public class IKSolverLimb:IKSolver
    {
        public enum BendModifier{Animation,Goal}
        public Bone bone1,bone2,bone3;
        public UnityEngine.AvatarIKGoal goal;
        public BendModifier bendModifier;
        public float bendModifierWeight,maintainRotationWeight,IKRotationWeight;
        public UnityEngine.Transform bendGoal;
        public UnityEngine.Vector3 bendNormal;
        public UnityEngine.Quaternion IKRotation;
        public bool SetChain(UnityEngine.Transform a,UnityEngine.Transform b,UnityEngine.Transform c,UnityEngine.Transform root)
        {bone1=new(){transform=a};bone2=new(){transform=b};bone3=new(){transform=c};ThrowOnUpdate=FailNewLimb;return true;}
        public override void Update(){base.Update();bone3.transform.rotation=IKRotation;}
    }
    public class IKSolverAim:IKSolver
    {
        public UnityEngine.Transform transform,poleTarget;
        public UnityEngine.Vector3 axis=new(0,0,1),poleAxis,polePosition;
        public float clampWeight,tolerance,poleWeight;
        public int clampSmoothing,maxIterations=2;
        public bool useRotationLimits;
        public Bone[] bones;
        public bool SetChain(UnityEngine.Transform[] chain,UnityEngine.Transform root){bones=chain.Select(t=>new Bone{transform=t}).ToArray();return true;}
        public override void Update(){base.Update();if(bones.Length>0)bones[0].transform.localRotation=UnityEngine.Quaternion.Euler(0,17,0);}
    }
    public class Bone{public UnityEngine.Transform transform;public float weight=1;}
}
namespace CullingSystem
{
    public class C_Camera:UnityEngine.Component
    {public static UnityEngine.Vector3 Position,Forward,Right,Up;public bool Throw;public void RunVisibility(){if(Throw)throw new Exception("native visibility failure");}}
    public static class C_CullingManager{public static void C_CameraUpdate(){}public static void SortAndUpdateLights(){}}
}
public static class CameraManager{public static UnityEngine.Vector3 CullingPosition,CullingDirection;public static float CullingFov;}
public static class LayerManager{public const int LAYER_THIRD_PERSON_ITEM=9,LAYER_FIRST_PERSON_ITEM=10;}
namespace PreLuna.ThirdPerson.Runtime
{internal static class ConvertView{internal static UnityEngine.Vector3 U(Num.Vector3 v)=>new(v.X,v.Y,v.Z);}}
