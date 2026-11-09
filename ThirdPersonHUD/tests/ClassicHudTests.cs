using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;
using PreLuna.ThirdPersonHUD.Panel;

static class ClassicHudTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool value,string message="assertion failed"){if(!value)throw new Exception(message);}
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static readonly HudRuntimeContext Current=PanelTestData.Context();
    static HudPanelOptions Options(HudLayoutStyle style)=>HudPanelOptions.Default with{
        Layout=HudPanelLayoutOptions.Default with{Style=style,Expanded=true,Team=true,Classic=HudClassicLayout.Default},
        Theme=HudPanelTheme.DivisionV3,
        Typography=HudPanelTypography.Default with{LabelSize=11,BodySize=14,NumberSize=38}};
    static HudPanelPlacement Place(HudPanelOptions? options=null,ThirdPersonDisplayFrame? display=null)
    {var o=options??Options(HudLayoutStyle.FourCorner);Check(HudPanelProjection.TryPlace(display??PanelTestData.Display(Current),o.Layout,1,out var p), "placement rejected");return p.Panel;}
    static HudPanelView View(HudPanelOptions? options=null,HudEquipmentSlot selected=HudEquipmentSlot.Primary,double hp=.82,double infection=.06,ThirdPersonDisplayFrame? display=null)
    {
        var o=options??Options(HudLayoutStyle.FourCorner);
        var frame=display??PanelTestData.Display(Current);
        var snapshot=SceneTestData.Snapshot(Current);
        snapshot=snapshot with{Personal=PersonalTestData.Snapshot(PersonalTestData.Request(Current),selected,hp,infection).Personal};
        var personal=HudPersonalMapper.Map(Current,snapshot,1,1,HudContentPolicy.Default,HudPersonalOptions.Default,true);
        var team=HudTeamMapper.Map(Current,snapshot,1,1,HudContentPolicy.Default,HudTeamOptions.Default,true);
        var scene=HudSceneMapper.Map(Current,frame,snapshot,1,1,HudContentPolicy.Default,HudSceneOptions.Default,true);
        var prompts=new HudPromptSet(new(HudPromptKind.Hit,"受击 · 左侧",90),new(HudPromptKind.Tool,"工具资源不足",45),new(HudPromptKind.Team,"队员4 · 需要救援",92),3);
        return personal with{Team=team,Scene=scene,Prompts=prompts};
    }
    static HudDrawCommand[] Draw(HudLayoutStyle style,HudPanelView? view=null,HudPanelOptions? options=null,ThirdPersonDisplayFrame? display=null)
    {
        var o=options??Options(style);var list=new HudDrawList();
        HudPanelDrawing.Build(Place(o,display),o,new(HudVisualState.Exploration,0,true,false),view??View(o,display:display),list);
        return Enumerable.Range(0,list.Count).Select(i=>list[i]).ToArray();
    }
    static bool Finite(HudDrawCommand command)
    {
        var r=command.Rect;
        return float.IsFinite(r.X)&&float.IsFinite(r.Y)&&float.IsFinite(r.Width)&&float.IsFinite(r.Height)&&r.Width>=0&&r.Height>=0;
    }
    static int Main(string[] args)
    {
        Case("four-corner-panels-present",()=>{
            var c=Draw(HudLayoutStyle.FourCorner);
            Check(c.Any(x=>x.Region.StartsWith("classic-mission")),"mission missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-team")),"team missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-personal")),"personal missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-equipment")),"equipment missing");
            Check(c.Any(x=>x.Region=="classic-interaction-text"),"interaction missing");
        });
        Case("four-corner-uses-full-viewport-without-dark-backdrop",()=>{
            var p=Place(Options(HudLayoutStyle.FourCorner));var c=Draw(HudLayoutStyle.FourCorner);
            Check(p.Bounds.X==0&&p.Bounds.Y==0&&p.Bounds.Width==1920&&p.Bounds.Height==1080);
            Check(!c.Any(x=>x.Region=="background"&&x.Rect.Width>=1919&&x.Rect.Height>=1079));
        });
        Case("radial-center-follows-body-projection",()=>{
            var a=Place(Options(HudLayoutStyle.Radial));
            var d=PanelTestData.Display(Current);var m=d.Camera.WorldToView;m.M41=0;d=d with{Camera=d.Camera with{WorldToView=m}};
            var b=Place(Options(HudLayoutStyle.Radial),d);
            Check(float.IsFinite(a.StyleOrigin.X)&&float.IsFinite(a.StyleOrigin.Y));
            Check(Math.Abs(a.StyleOrigin.X-b.StyleOrigin.X)>.1f);
            Check(a.Bounds.Width==1920&&a.Bounds.Height==1080);
        });
        Case("radial-arcs-use-real-health-and-infection",()=>{
            var c=Draw(HudLayoutStyle.Radial,View(Options(HudLayoutStyle.Radial),hp:.82,infection:.06));
            Check(c.Any(x=>x.Region=="classic-radial-health-fill"));
            Check(c.Any(x=>x.Region=="classic-radial-infection-fill"));
            Check(c.Any(x=>x.Region=="classic-radial-health-value"&&x.Text=="82%"));
        });
        Case("radial-zero-infection-is-silent",()=>{
            var c=Draw(HudLayoutStyle.Radial,View(Options(HudLayoutStyle.Radial),infection:0));
            Check(!c.Any(x=>x.Region.StartsWith("classic-radial-infection")));
        });
        Case("radial-zero-health-keeps-text-without-fill",()=>{
            var c=Draw(HudLayoutStyle.Radial,View(Options(HudLayoutStyle.Radial),hp:0));
            Check(c.Any(x=>x.Region=="classic-radial-health-value"&&x.Text=="0%"));
            Check(!c.Any(x=>x.Region=="classic-radial-health-fill"));
        });
        Case("radial-character-elements-present",()=>{
            var c=Draw(HudLayoutStyle.Radial);
            Check(c.Any(x=>x.Region.StartsWith("classic-radial-mission")),"mission tag missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-radial-team")),"team tag missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-radial-equipment")),"equipment strip missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-radial-health")),"health arc missing");
            Check(c.Any(x=>x.Region.StartsWith("classic-radial-interaction-ring")),"interaction ring missing");
        });
        Case("classic-command-count-and-bounds",()=>{
            foreach(var style in new[]{HudLayoutStyle.FourCorner,HudLayoutStyle.Radial})
            {
                var p=Place(Options(style));var c=Draw(style);
                Check(c.Length>40&&c.Length<512,style+" command count "+c.Length);
                foreach(var command in c)
                {
                    Check(Finite(command),style+" non-finite command "+command.Region);
                    float x=p.Bounds.X+command.Rect.X;
                    float y=1080-p.Bounds.Y-command.Rect.Y-command.Rect.Height;
                    Check(x>=-1&&y>=-1&&x+command.Rect.Width<=1921&&y+command.Rect.Height<=1081,style+" off-screen "+command.Region+" @"+x+","+y);
                }
            }
        });
        Case("classic-layouts-fit-720p-and-ultrawide",()=>{
            foreach(var (w,h) in new[]{(1280f,720f),(2560f,1080f),(3440f,1440f)})
            {
                var frame=PanelTestData.Display(Current) with{Camera=PanelTestData.Display(Current).Camera with{Viewport=new PixelViewport(0,0,w,h)}};
                foreach(var style in new[]{HudLayoutStyle.FourCorner,HudLayoutStyle.Radial})
                {
                    var o=Options(style);var p=Place(o,frame);var c=Draw(style,View(o,display:frame),o,frame);
                    Check(p.Bounds.Width==w&&p.Bounds.Height==h,style+" viewport "+w+"x"+h);
                    Check(p.Scale>=.55f,style+" scale too small "+p.Scale);
                    foreach(var command in c)
                    {
                        float left=p.Bounds.X+command.Rect.X;
                        float top=p.Bounds.Y+p.Bounds.Height-command.Rect.Y-command.Rect.Height;
                        Check(left>=-1&&top>=-1&&left+command.Rect.Width<=w+1&&top+command.Rect.Height<=h+1,
                            style+" "+w+"x"+h+" off-screen "+command.Region+" @"+left+","+top);
                    }
                }
            }
        });
        Case("unknown-data-does-not-invent-percentages",()=>{
            var unavailable=HudPersonalMapper.Unavailable(HudPersonalOptions.Default,HudValueState.Unreadable);
            var c=Draw(HudLayoutStyle.FourCorner,unavailable with{Team=default,Scene=default});
            Check(!c.Any(x=>x.Kind==HudDrawKind.Text&&x.Text.Contains('%')));
            Check(!c.Any(x=>x.Kind==HudDrawKind.Text&&x.Text=="82%"));
        });
        Case("classic-styles-do-not-use-division-regions",()=>{
            foreach(var style in new[]{HudLayoutStyle.FourCorner,HudLayoutStyle.Radial})
                Check(!Draw(style).Any(x=>x.Region.StartsWith("v3-")),style+" leaked v3 region");
        });
        Case("style-options-are-sanitized-and-independent",()=>{
            var classic=(HudClassicLayout.Default with{Scale=float.NaN,Margin=-20,RadialScale=9,LabelSize=1,NumberSize=99}).Sanitized();
            Check(classic.Scale==1&&classic.Margin==0&&classic.RadialScale==1.6f&&classic.LabelSize==9&&classic.NumberSize==48);
            var options=Options(HudLayoutStyle.Radial) with{Layout=HudPanelLayoutOptions.Default with{Style=HudLayoutStyle.Radial,Classic=classic}};
            Check(options.Sanitized().Layout.Style==HudLayoutStyle.Radial&&options.Sanitized().Layout.Classic.NumberSize==48);
        });

        // Offline previews consume exactly the production commands; no separate design mock.
        var previews=new List<object>();
        foreach(var style in new[]{HudLayoutStyle.FourCorner,HudLayoutStyle.Radial})
        {
            var options=Options(style);var panel=Place(options);var commands=Draw(style,View(options),options);
            previews.Add(new{Style=style.ToString(),ScreenHeight=1080,Scale=panel.Scale,
                Bounds=new{X=panel.Bounds.X,Y=panel.Bounds.Y,Width=panel.Bounds.Width,Height=panel.Bounds.Height},
                OriginX=panel.StyleOrigin.X,OriginY=panel.StyleOrigin.Y,Commands=commands});
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]),"ClassicHudPreview.json"),JsonSerializer.Serialize(previews));
        bool passed=results.All(r=>r.Passed);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,GameplayTested=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Classic HUD layouts: {results.Count}; passed={passed}");return passed?0:1;
    }
}
