using System;
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
            foreach(string candidate in new[]{options.Typography.FontName,"Microsoft YaHei UI","Microsoft YaHei","SimHei"})
            {
                var next=Font.CreateDynamicFontFromOSFont(candidate,16);
                if(next==null)continue;
                next.hideFlags=HideFlags.DontSave;
                bool accepted=false;
                try
                {
                    next.RequestCharactersInTexture(HudPanelDrawing.RequiredGlyphs,16,FontStyle.Normal);
                    bool covered=true;
                    foreach(char glyph in HudPanelDrawing.RequiredGlyphs)if(!next.HasCharacter(glyph)){covered=false;break;}
                    if(covered){font=next;FontUsed=candidate;accepted=true;return;}
                }
                catch { }
                finally {if(!accepted){UnityEngine.Object.Destroy(next);destroyRequests++;}}
            }
            throw new InvalidOperationException("未找到覆盖 HUD 中文标签的系统字体；可调整 Panel.Text.FontName。");
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
            HudPanelDrawing.Build(panel,options,visual,view,commands);
            var previous=GUI.color;
            float x=panel.Bounds.X,y=Screen.height-panel.Bounds.Y-panel.Bounds.Height;
            try
            {
                for(int i=0;i<commands.Count;i++)
                {
                    var command=commands[i];var r=command.Rect;var c=command.Color;
                    var rect=new Rect(x+r.X,y+r.Y,r.Width,r.Height);
                    if(command.Kind==HudDrawKind.Rectangle)
                    {GUI.color=new Color(c.R,c.G,c.B,c.A);GUI.DrawTexture(rect,pixel);}
                    else
                    {
                        style.fontSize=Math.Max(1,(int)MathF.Round(command.FontSize));label.text=command.Text;
                        if(command.Numeric)while(style.fontSize>8&&style.CalcSize(label).x>rect.width-2)style.fontSize--;
                        if(options.Typography.Shadow)
                        {GUI.color=new Color(0,0,0,c.A*.85f);GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),label,style);}
                        GUI.color=new Color(c.R,c.G,c.B,c.A);GUI.Label(rect,label,style);
                    }
                }
                lastDraw=placement;draws++;
            }
            finally{GUI.color=previous;}
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
