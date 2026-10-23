#nullable disable
using System;
using System.Collections.Generic;
using PreLuna.ThirdPerson.Bridge;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;

namespace Il2CppInterop.Runtime.Attributes { public sealed class HideFromIl2CppAttribute : Attribute { } }
public enum eGameStateName { InLevel, Lobby }
public static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
namespace PreLuna.ThirdPerson
{
    public static class ThirdPersonApi
    {
        public static ThirdPersonViewProvider Provider;
        public static double MonotonicSeconds;
        public static bool TryGetProvider(out IThirdPersonViewProvider provider)
        { provider = Provider; return provider != null; }
        public static bool TryGetRuntimeContext(out HudRuntimeContext current)
        { current = default; return Provider != null && Provider.TryGetRuntimeContext(out current); }
    }
}
namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed class HudSettings
    {
        internal HudLifecycleOptions Options = HudLifecycleOptions.Default;
        internal HudWorkOptions WorkOptions = HudWorkOptions.Default;
        internal HudExecutionOptions ExecutionOptions = HudExecutionOptions.Default;
        internal double LogIntervalSeconds = 10;
        internal HudLifecycleOptions Read() => Options;
        internal HudWorkOptions ReadWork() => WorkOptions;
        internal HudExecutionOptions ReadExecution() => ExecutionOptions;
        internal PreLuna.ThirdPersonHUD.Content.HudCollectionOptions ReadCollection() => PreLuna.ThirdPersonHUD.Content.HudCollectionOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudPanelOptions PanelOptions = PreLuna.ThirdPersonHUD.Panel.HudPanelOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudPanelOptions ReadPanel() => PanelOptions;
        internal PreLuna.ThirdPersonHUD.Panel.HudPersonalOptions PersonalOptions=PreLuna.ThirdPersonHUD.Panel.HudPersonalOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudPersonalOptions ReadPersonal()=>PersonalOptions;
        internal PreLuna.ThirdPersonHUD.Panel.HudAlertOptions AlertOptions=PreLuna.ThirdPersonHUD.Panel.HudAlertOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudAlertOptions ReadAlerts()=>AlertOptions;
        internal PreLuna.ThirdPersonHUD.Panel.HudReplacementOptions ReplacementOptions=PreLuna.ThirdPersonHUD.Panel.HudReplacementOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudReplacementOptions ReadReplacement()=>ReplacementOptions;
        internal PreLuna.ThirdPersonHUD.Panel.HudTeamOptions TeamOptions=PreLuna.ThirdPersonHUD.Panel.HudTeamOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudTeamOptions ReadTeam()=>TeamOptions;
        internal PreLuna.ThirdPersonHUD.Panel.HudSceneOptions SceneOptions=PreLuna.ThirdPersonHUD.Panel.HudSceneOptions.Default;
        internal PreLuna.ThirdPersonHUD.Panel.HudSceneOptions ReadScene()=>SceneOptions;
        internal UnityEngine.KeyCode RestoreKey=>UnityEngine.KeyCode.Pause;
        internal UnityEngine.KeyCode ExpandKey=>UnityEngine.KeyCode.LeftAlt;
        internal bool ToggleExpand=false;
    }
    internal sealed class MockLog
    {
        internal List<string> InformationMessages = new();
        internal List<string> WarningMessages = new();
        internal void LogWarning(object value) { WarningMessages.Add(value.ToString()); }
        internal void LogInfo(object value) { InformationMessages.Add(value.ToString()); }
    }
    internal sealed class Plugin
    { internal static Plugin Instance = new(); internal HudSettings Settings = new(); internal MockLog Log = new(); }
}
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Created = new();
        public static string FailAt;
        public bool Destroyed;
        public HideFlags hideFlags;
        public string name;
        public Object() { Created.Add(this); }
        public static void Fail(string point) { if (FailAt == point) { FailAt = null; throw new InvalidOperationException("mock " + point); } }
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Destroyed = true; }
        public static bool operator ==(Object a, Object b) => (ReferenceEquals(a, null) || a.Destroyed) == (ReferenceEquals(b, null) || b.Destroyed) && (ReferenceEquals(a, b) || ReferenceEquals(a, null) || ReferenceEquals(b, null));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    public class MonoBehaviour { public MonoBehaviour(IntPtr p) { } }
    public enum HideFlags { DontSave }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Vector2 { public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;} }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color white => new(){r=1,g=1,b=1,a=1}; }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject : Object
    {
        public bool active = true; public Transform transform = new();
        public GameObject(string name) { this.name=name; }
        public void SetActive(bool value) { active=value; Fail("SetActive"); }
    }
    public sealed class Texture2D : Object
    {
        public int width,height,Pixels,Uploads; public FilterMode filterMode;
        public Texture2D(int w,int h,TextureFormat format,bool mip){width=w;height=h;Fail("TextureConstructor");}
        public void SetPixel(int x,int y,Color color){Fail("SetPixel");Pixels++;}
        public void Apply(bool mip,bool noRead){Fail("Apply");Uploads++;}
    }
    public sealed class Camera { public static Camera current; public object targetTexture; }
    public enum EventType { Repaint, Layout, KeyDown }
    public sealed class Event { public static Event current = new(){type=EventType.Repaint}; public EventType type; }
    public static class Screen { public static int height=1080; }
    public static class Time { public static int frameCount; }
    public enum KeyCode { LeftAlt,Pause }
    public static class Input { public static bool Held,RestorePressed;public static bool GetKey(KeyCode key)=>Held;public static bool GetKeyDown(KeyCode key){bool v=RestorePressed;RestorePressed=false;return v;} }
    public enum FontStyle { Normal }
    public enum TextClipping { Clip }
    public enum TextAnchor { MiddleLeft }
    public sealed class Font : Object
    {
        public static bool MissingGlyphs;
        public static string RejectedName;
        public static Font CreateDynamicFontFromOSFont(string name,int size){Fail("FontConstructor");return new(){name=name};}
        public void RequestCharactersInTexture(string glyphs,int size,FontStyle style){Fail("FontRequest");}
        public bool HasCharacter(char glyph)=>!MissingGlyphs&&name!=RejectedName;
    }
    public sealed class GUIStyleState { public Color textColor; }
    public sealed class GUIStyle
    { public Font font;public bool richText,wordWrap;public TextClipping clipping;public TextAnchor alignment;public GUIStyleState normal=new();public int fontSize;
      public Vector2 CalcSize(GUIContent label){float units=0;foreach(char c in label.text)units+=c>255?1:.66f;return new(units*fontSize,fontSize*1.2f);} }
    public sealed class GUIContent { public string text; }
    public static class GUI
    {
        public static Action Drawing;
        public static int Draws; public static Color color; public static Rect LastRect;
        public static readonly List<string> Labels=new();
        public static void DrawTexture(Rect rect,Texture2D texture){Object.Fail("DrawTexture");Drawing?.Invoke();LastRect=rect;Draws++;}
        public static void Label(Rect rect,GUIContent label,GUIStyle style){Object.Fail("Label");Labels.Add(label.text);}
    }
}
