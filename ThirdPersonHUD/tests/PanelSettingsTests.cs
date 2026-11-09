using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using PreLuna.ThirdPersonHUD.Runtime;
using UnityEngine;
using PreLuna.ThirdPersonHUD.Panel;

// Keep Unity and configuration storage outside the test; execute the production settings constructor.
namespace UnityEngine { public enum KeyCode { LeftAlt, RightAlt, Insert, Home, None, F5 } }
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value { get; set; } public ConfigEntry(T value){Value=value;} public string GetSerializedValue()=>Value?.ToString()??string.Empty; }
    public sealed class ConfigDescription { public ConfigDescription(string text,object range){} }
    public sealed class AcceptableValueRange<T> { public AcceptableValueRange(T min,T max){} }
    public sealed class ConfigFile
    {
        readonly Dictionary<string,object> values=new();
        public ConfigEntry<T> Bind<T>(string group,string key,T value,object description)
        {string id=group+"/"+key;if(!values.TryGetValue(id,out var entry))values[id]=entry=new ConfigEntry<T>(value);return (ConfigEntry<T>)entry;}
        public void Set<T>(string group,string key,T value)=>Bind(group,key,value,"").Value=value;
        public T Get<T>(string group,string key)=>((ConfigEntry<T>)values[group+"/"+key]).Value;
    }
}
static class PanelSettingsTests
{
    sealed record Result(string Name,bool Passed,string Error);
    static readonly List<Result> results=new();
    static void Check(bool ok){if(!ok)throw new Exception("assertion failed");}
    static void Case(string name,Action test){try{test();results.Add(new(name,true,null));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
    static int Main(string[] args)
    {
        Case("fresh-config-uses-insert-whole-hud-toggle",()=>{var c=new ConfigFile();var s=new HudPanelSettings(c);Check(s.ExpandKey==KeyCode.Insert&&s.ToggleHudVisibility&&c.Get<int>("Panel.Input","BindingVersion")==1);});
        Case("existing-hold-config-upgrades-to-whole-hud-toggle",()=>{var c=new ConfigFile();c.Set("Panel.Input","BindingVersion",1);c.Set("Panel.Input","ToggleExpand",false);var s=new HudPanelSettings(c);Check(s.ToggleHudVisibility&&!s.ToggleExpand);});
        Case("explicit-legacy-detail-mode-is-preserved",()=>{var c=new ConfigFile();c.Set("Panel.Input","ToggleHudVisibility",false);c.Set("Panel.Input","ToggleExpand",true);var s=new HudPanelSettings(c);Check(!s.ToggleHudVisibility&&s.ToggleExpand);});
        Case("legacy-left-alt-migrates-without-touching-panel-settings",()=>{var c=new ConfigFile();c.Set("Panel.Input","ExpandKey",KeyCode.LeftAlt);c.Set("Panel.Layout","Width",360f);c.Set("Panel.Text","FontName","Custom font");var s=new HudPanelSettings(c);Check(s.ExpandKey==KeyCode.Insert&&s.Read().Layout.Width==360&&s.Read().Typography.FontName=="Custom font");});
        Case("legacy-toggle-setting-is-preserved",()=>{var c=new ConfigFile();c.Set("Panel.Input","ExpandKey",KeyCode.LeftAlt);c.Set("Panel.Input","ToggleExpand",true);var s=new HudPanelSettings(c);Check(s.ExpandKey==KeyCode.Insert&&s.ToggleExpand);});
        foreach(var key in new[]{KeyCode.Home,KeyCode.RightAlt,KeyCode.None,KeyCode.Insert})
            Case("custom-key-preserved-"+key,()=>{var c=new ConfigFile();c.Set("Panel.Input","ExpandKey",key);Check(new HudPanelSettings(c).ExpandKey==key);});
        Case("migration-is-once-only-and-later-explicit-left-alt-is-preserved",()=>{var c=new ConfigFile();c.Set("Panel.Input","ExpandKey",KeyCode.LeftAlt);Check(new HudPanelSettings(c).ExpandKey==KeyCode.Insert);c.Set("Panel.Input","ExpandKey",KeyCode.LeftAlt);Check(new HudPanelSettings(c).ExpandKey==KeyCode.LeftAlt);});
        Case("future-version-is-not-downgraded",()=>{var c=new ConfigFile();c.Set("Panel.Input","BindingVersion",2);c.Set("Panel.Input","ExpandKey",KeyCode.Home);Check(new HudPanelSettings(c).ExpandKey==KeyCode.Home&&c.Get<int>("Panel.Input","BindingVersion")==2);});
        Case("v3-is-runtime-default-without-resetting-compact",()=>{var c=new ConfigFile();c.Set("Panel.Layout","Width",360f);c.Set("Panel.Theme","BaseColor","#123456");var compact=new HudPanelSettings(c).Read();var v3=new HudDivisionSettings(c);Check(v3.Read(compact).Layout.Style==HudLayoutStyle.DivisionV3);c.Set("Presentation","Style",HudLayoutStyle.Compact);Check(v3.Read(compact)==compact);});
        Case("v3-values-are-independent-and-survive-roundtrip",()=>{var c=new ConfigFile();var compact=new HudPanelSettings(c).Read();var v3=new HudDivisionSettings(c);c.Set("DivisionV3.Layout","Top",720f);c.Set("DivisionV3.Theme","AccentColor","#102030");var before=v3.Read(compact);c.Set("Presentation","Style",HudLayoutStyle.Compact);Check(v3.Read(compact)==compact);c.Set("Presentation","Style",HudLayoutStyle.DivisionV3);Check(v3.Read(compact)==before&&before.Layout.Division.Top==720);});
        Case("v3-invalid-config-falls-back-and-is-sanitized",()=>{var c=new ConfigFile();var v3=new HudDivisionSettings(c);c.Set("Presentation","Style",(HudLayoutStyle)999);c.Set("DivisionV3.Layout","Scale",float.NaN);c.Set("DivisionV3.Theme","AccentColor","invalid");var o=v3.Read(HudPanelOptions.Default);Check(o.Layout.Style==HudLayoutStyle.DivisionV3&&o.Layout.Division.Scale==1&&Math.Abs(o.Theme.Attention.R-224/255f)<.0001);});
        Case("v3-keeps-font-and-module-global-toggle",()=>{var c=new ConfigFile();c.Set("Panel.Text","FontName","Custom font");c.Set("Panel","Enabled",false);var o=new HudDivisionSettings(c).Read(new HudPanelSettings(c).Read());Check(o.Typography.FontName=="Custom font"&&!o.Layout.Enabled&&o.Typography.LabelSize>=11);});
        Case("refinement-defaults-preserve-existing-v3-values",()=>{var c=new ConfigFile();c.Set("DivisionV3.Layout","HealthSegments",10);c.Set("DivisionV3.Layout","Top",720f);var o=new HudDivisionSettings(c).Read(HudPanelOptions.Default);Check(o.Layout.Division.FollowBody&&o.Layout.Division.HealthSegments==10&&o.Layout.Division.Top==720&&o.Layout.Division.NumberWidth==.82f);});
        Case("refinement-disable-and-reduce-motion-are-explicit",()=>{var c=new ConfigFile();c.Set("DivisionV3.Motion","FollowBody",false);c.Set("DivisionV3.Plane","TiltDegrees",0f);var o=new HudDivisionSettings(c).Read(HudPanelOptions.Default with{Behavior=HudPanelBehavior.Default with{ReduceMotion=true}});Check(!o.Layout.Division.FollowBody&&o.Layout.Division.TiltDegrees==0&&o.Layout.Division.FollowSeconds==0);});
        Case("classic-layout-defaults-and-style-selection",()=>{var c=new ConfigFile();var compact=new HudPanelSettings(c).Read();var o=new HudClassicSettings(c).Read(compact,HudLayoutStyle.FourCorner);Check(o.Layout.Style==HudLayoutStyle.FourCorner&&o.Layout.Classic.Scale==1&&o.Layout.Classic.Margin==48&&o.Layout.Classic.NumberSize==26&&Math.Abs(o.Theme.Attention.R-224/255f)<.0001);});
        Case("classic-values-are-independent-and-survive-roundtrip",()=>{var c=new ConfigFile();var compact=new HudPanelSettings(c).Read();var classic=new HudClassicSettings(c);c.Set("Classic.Layout","Margin",72f);c.Set("Classic.Text","NumberSize",30f);c.Set("Classic.Theme","AccentColor","#102030");var before=classic.Read(compact,HudLayoutStyle.Radial);c.Set("Panel.Layout","Width",360f);var after=classic.Read(new HudPanelSettings(c).Read(),HudLayoutStyle.Radial);Check(before.Layout.Classic.Margin==72&&before.Layout.Classic.NumberSize==30&&before.Layout.Style==HudLayoutStyle.Radial);Check(after.Layout.Classic.Margin==72&&after.Layout.Classic.NumberSize==30&&after.Theme.Attention.B<.2f);});
        Case("classic-invalid-values-sanitized",()=>{var c=new ConfigFile();var s=new HudClassicSettings(c);c.Set("Classic.Layout","Scale",float.NaN);c.Set("Classic.Layout","RadialScale",9f);c.Set("Classic.Text","LabelSize",1f);c.Set("Classic.Theme","AccentColor","bad");var o=s.Read(HudPanelOptions.Default,HudLayoutStyle.FourCorner);Check(o.Layout.Classic.Scale==1&&o.Layout.Classic.RadialScale==1.6f&&o.Layout.Classic.LabelSize==9&&Math.Abs(o.Theme.Attention.R-224/255f)<.0001);});
        Case("style-cycle-order-and-temporary-override",()=>{var c=new ConfigFile();var d=new HudDivisionSettings(c);var compact=new HudPanelSettings(c).Read();Check(d.Style==HudLayoutStyle.DivisionV3);Check(d.CycleStyle()==HudLayoutStyle.FourCorner&&d.Read(compact).Layout.Style==HudLayoutStyle.FourCorner);Check(d.CycleStyle()==HudLayoutStyle.Radial&&d.Read(compact).Layout.Style==HudLayoutStyle.Radial);Check(d.CycleStyle()==HudLayoutStyle.Compact&&d.Read(compact).Layout.Style==HudLayoutStyle.Compact);Check(d.CycleStyle()==HudLayoutStyle.DivisionV3&&d.Read(compact).Layout.Style==HudLayoutStyle.DivisionV3);Check(d.CycleStyleKey==KeyCode.F5);});
        Case("classic-style-config-is-preserved-across-cycles",()=>{var c=new ConfigFile();var d=new HudDivisionSettings(c);c.Set("Presentation","Style",HudLayoutStyle.FourCorner);Check(d.Style==HudLayoutStyle.FourCorner);d.CycleStyle();c.Set("Presentation","Style",HudLayoutStyle.Radial);Check(d.Style==HudLayoutStyle.Radial);});
        bool passed=results.All(r=>r.Passed);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=passed,Count=results.Count,Results=results,GameplayTested=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results.Where(r=>!r.Passed))Console.WriteLine("FAIL "+r.Name+": "+r.Error);
        Console.WriteLine($"Panel settings/key migration: {results.Count}; passed={passed}");return passed?0:1;
    }
}
