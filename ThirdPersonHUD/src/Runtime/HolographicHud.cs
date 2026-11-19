using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal interface IHudRuntimePresentation : IHudPresentation
    { void Draw(); HudPresentationDiagnostics Diagnostics { get; } }

    internal sealed class HolographicHud : IHudRuntimePresentation
    {
        Texture2D pixel;
        Font font;
        GUIStyle style;
        GUIContent label;
        HudPanelOptions options=HudPanelOptions.Default;
        HudPanelVisual visual;
        HudPanelView view=HudPanelView.Empty;
        readonly HudDrawList commands=new();
        readonly Guid instance=Guid.NewGuid();
        HudPlacement placement,lastDraw;
        ContextStamp context;
        LocalViewBinding binding;
        int step;
        bool queued;
        long draws,textureRejections,texturesCreated,destroyRequests;
        public int InitializationStep=>step;
        public bool HasResources=>step!=0||!ReferenceEquals(pixel,null)||!ReferenceEquals(font,null);
        public bool IsHealthy=>(step<1||pixel!=null)&&(step<2||font!=null&&style!=null&&label!=null);
        public bool IsReady=>step==3&&IsHealthy;
        internal bool PromptActive=>queued&&IsReady&&view.Scene.Active&&view.Scene.Prompt.Active;
        public HudPresentationDiagnostics Diagnostics=>new(instance,draws,textureRejections,0,texturesCreated,destroyRequests,
            (pixel!=null?1:0)+(font!=null?1:0),lastDraw);
        internal string FontUsed { get; private set; }
        internal void Configure(in HudPanelOptions raw)
        {
            var next=raw.Sanitized();
            if(options.Typography.FontName!=next.Typography.FontName&&HasResources)
            {var c=context;var b=binding;Release();context=c;binding=b;}
            options=next;
        }
        internal void SetVisual(in HudPanelVisual next)=>visual=next;
        internal void SetView(in HudPanelView next)=>view=next;
        public void InitializeNextStep()
        {
            switch(step)
            {
                case 0:
                    pixel=new Texture2D(1,1,TextureFormat.RGBA32,false);texturesCreated++;
                    pixel.name="PreLuna.ThirdPersonHUD.PanelPixel";pixel.hideFlags=HideFlags.DontSave;pixel.filterMode=FilterMode.Point;
                    pixel.SetPixel(0,0,Color.white);pixel.Apply(false,true);break;
                case 1:
                    PrepareFont();
                    style=new GUIStyle {font=font,richText=false,wordWrap=false,clipping=TextClipping.Clip,alignment=TextAnchor.MiddleLeft};
                    style.normal.textColor=Color.white;label=new GUIContent();break;
                case 2: break;
                default:return;
            }
            step++;
        }
        void PrepareFont()
        {
            Exception lastError=null;
            foreach(string candidate in new[]{options.Typography.FontName,"Microsoft YaHei UI","Microsoft YaHei","SimHei"})
            {
                Font next=null;
                bool accepted=false;
                try
                {
                    // Both restored factory overloads are broken in the installed interop:
                    // one creates ReferenceArray<string>, the other calls a stripped constructor.
                    // Allocate the IL2CPP wrapper without creating a spare native font asset,
                    // then use the generated native icall to create exactly one dynamic font.
                    next=new Font(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Font>.NativeClassPtr));
                    Font.Internal_CreateDynamicFont(next,new Il2CppStringArray(new[]{candidate}),16);
                    if(next==null)continue;
                    next.hideFlags=HideFlags.DontSave;
                    next.RequestCharactersInTexture(HudPanelDrawing.RequiredGlyphs,16,FontStyle.Normal);
                    bool covered=true;
                    foreach(char glyph in HudPanelDrawing.RequiredGlyphs)if(!next.HasCharacter(glyph)){covered=false;break;}
                    if(covered){font=next;FontUsed=candidate;accepted=true;return;}
                }
                catch(Exception error) {lastError=error;}
                finally {if(!accepted&&next!=null){UnityEngine.Object.Destroy(next);destroyRequests++;}}
            }
            throw new InvalidOperationException("HUD字体创建或中文字形验证失败；可调整 Panel.Text.FontName。",lastError);
        }
        public void Bind(ContextStamp c,LocalViewBinding b){Hide();context=c;binding=b;}
        public void Show(in HudPlacement next)
        {
            if(!IsReady||context!=next.View.Context||binding!=next.View.Binding||!next.Panel.IsValid){Hide();return;}
            placement=next;queued=true;
        }
        public void Hide(){queued=false;placement=default;view=HudPanelView.Empty;}
        public void Draw()
        {
            if(!queued||!IsReady||Event.current==null||Event.current.type!=EventType.Repaint)return;
            if(Camera.current!=null&&Camera.current.targetTexture!=null){textureRejections++;return;}
            var panel=placement.Panel;
            if(options.Layout.Style==HudLayoutStyle.Compact&&options.Layout.Streamlined&&!options.Layout.TeamPage&&view.Personal.Active)
            {
                var bounds=panel.Bounds;
                float height=Math.Min(bounds.Height,HudPersonalDrawing.MeasureHeight(view)*panel.Scale);
                panel=panel with{Bounds=new(bounds.X,bounds.Y+bounds.Height-height,bounds.Width,height)};
                placement=placement with{Panel=panel};
            }
            HudPanelDrawing.Build(panel,options,visual,view,commands);
            var previous=GUI.color;
            float x=panel.Bounds.X,y=Screen.height-panel.Bounds.Y-panel.Bounds.Height;
            bool division=options.Layout.Style==HudLayoutStyle.DivisionV3;
            var previousMatrix=division?GUI.matrix:default;
            var plane=division?PlaneMatrix(previousMatrix,panel.Plane,x,y):default;
            try
            {
                for(int i=0;i<commands.Count;i++)
                {
                    var command=commands[i];var r=command.Rect;var c=command.Color;
                    var rect=new Rect(x+r.X,y+r.Y,r.Width,r.Height);
                    if(division)GUI.matrix=command.CorePlane?plane:previousMatrix;
                    if(command.Kind==HudDrawKind.Rectangle)
                    {GUI.color=new Color(c.R,c.G,c.B,c.A);GUI.DrawTexture(rect,pixel);}
                    else
                    {
                        if(division&&command.NumberWidth>0&&command.NumberWidth<1)
                        {
                            float width=command.NumberWidth;
                            GUI.matrix=Compose(command.CorePlane?plane:previousMatrix,width,0,0,1,rect.x*(1-width),0);
                            rect.width/=width;
                        }
                        style.fontSize=Math.Max(1,(int)MathF.Round(command.FontSize));label.text=command.Text;
                        style.alignment=command.Align==HudTextAlign.Center?TextAnchor.MiddleCenter:command.Align==HudTextAlign.Right?TextAnchor.MiddleRight:TextAnchor.MiddleLeft;
                        if(command.Numeric)while(style.fontSize>8&&(style.CalcSize(label).x>rect.width-2
                            ||(options.Layout.Style is HudLayoutStyle.DivisionV3 or HudLayoutStyle.FourCorner or HudLayoutStyle.Radial)&&style.CalcSize(label).y>rect.height))style.fontSize--;
                        if(options.Typography.Shadow)
                        {GUI.color=new Color(0,0,0,c.A*.85f);GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),label,style);}
                        GUI.color=new Color(c.R,c.G,c.B,c.A);GUI.Label(rect,label,style);
                    }
                }
                lastDraw=placement;draws++;
            }
            finally{try{GUI.color=previous;}finally{if(division)GUI.matrix=previousMatrix;}}
        }
        public void DrawPrompt(in ThirdPersonDisplayFrame display)
        {
            if(!queued||!IsReady||Event.current==null||Event.current.type!=EventType.Repaint)return;
            if(Camera.current!=null&&Camera.current.targetTexture!=null)return;
            var scene=view.Scene;var prompt=scene.Prompt;
            if(!scene.Active||!prompt.Active||pixel==null||font==null||style==null||label==null)return;
            float scale=Math.Clamp(display.Camera.Viewport.Height/1080f,.75f,2f)*Math.Max(.65f,scene.Options.CardScale);
            if(!HudPromptProjection.TryPlace(display,prompt,scale,placement.Panel.Bounds,out var bounds))return;
            var t=options.Theme;var accent=t.Base.Brightness(t.Brightness).Opacity(t.TextOpacity);
            var ink=t.Text.Brightness(t.Brightness).Opacity(t.TextOpacity);var background=t.Background.Opacity(t.BackgroundOpacity*.94f);
            float x=bounds.X,y=Screen.height-bounds.Y-bounds.Height;
            var old=GUI.color;
            try
            {
                GUI.color=new Color(background.R,background.G,background.B,background.A);
                GUI.DrawTexture(new Rect(x,y,bounds.Width,bounds.Height),pixel);
                GUI.color=new Color(accent.R,accent.G,accent.B,accent.A*.85f);
                GUI.DrawTexture(new Rect(x,y,Math.Max(1f,2f*scale),bounds.Height),pixel);
                if(prompt.Progress is float progress)
                {
                    float bar=Math.Clamp(progress,0f,1f),barY=y+bounds.Height-Math.Max(2f,3f*scale);
                    GUI.color=new Color(accent.R,accent.G,accent.B,accent.A*.2f);
                    GUI.DrawTexture(new Rect(x+8f,barY,bounds.Width-16f,Math.Max(1f,2f*scale)),pixel);
                    GUI.color=new Color(accent.R,accent.G,accent.B,accent.A);
                    GUI.DrawTexture(new Rect(x+8f,barY,(bounds.Width-16f)*bar,Math.Max(1f,2f*scale)),pixel);
                }
                style.fontSize=Math.Max(1,(int)MathF.Round(options.Typography.BodySize*scale));
                style.alignment=TextAnchor.MiddleCenter;
                label.text=HudPanelDrawing.Ellipsize(prompt.Text,Math.Max(40f,bounds.Width/scale-16f),options.Typography.BodySize);
                GUI.color=new Color(ink.R,ink.G,ink.B,ink.A);
                if(options.Typography.Shadow){GUI.color=new Color(0,0,0,ink.A*.85f);GUI.Label(new Rect(x+9f,y+3f,bounds.Width-18f,bounds.Height-8f),label,style);}
                GUI.color=new Color(ink.R,ink.G,ink.B,ink.A);
                GUI.Label(new Rect(x+8f,y+2f,bounds.Width-16f,bounds.Height-8f),label,style);
            }
            finally {GUI.color=old;}
        }
        static Matrix4x4 PlaneMatrix(Matrix4x4 previous,HudPlaneTransform p,float x,float y)
            =>Compose(previous,p.A,p.B,p.C,p.D,x+p.X-p.A*x-p.B*y,y+p.Y-p.C*x-p.D*y);
        // Multiply the 2D plane into the caller's matrix using value fields only, avoiding native matrix helper calls.
        static Matrix4x4 Compose(Matrix4x4 p,float a,float b,float c,float d,float x,float y)
        {
            var n=p;
            n.m00=p.m00*a+p.m01*c;n.m01=p.m00*b+p.m01*d;n.m03=p.m03+p.m00*x+p.m01*y;
            n.m10=p.m10*a+p.m11*c;n.m11=p.m10*b+p.m11*d;n.m13=p.m13+p.m10*x+p.m11*y;
            n.m20=p.m20*a+p.m21*c;n.m21=p.m20*b+p.m21*d;n.m23=p.m23+p.m20*x+p.m21*y;
            n.m30=p.m30*a+p.m31*c;n.m31=p.m30*b+p.m31*d;n.m33=p.m33+p.m30*x+p.m31*y;
            return n;
        }
        public void Release()
        {
            Hide();var oldPixel=pixel;var oldFont=font;
            pixel=null;font=null;style=null;label=null;step=0;context=default;binding=default;FontUsed=null;commands.Clear();
            try{if(oldFont!=null){UnityEngine.Object.Destroy(oldFont);destroyRequests++;}}
            finally{if(oldPixel!=null){UnityEngine.Object.Destroy(oldPixel);destroyRequests++;}}
        }
    }
}
