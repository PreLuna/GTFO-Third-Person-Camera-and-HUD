using BepInEx.Configuration;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Runtime
{
    internal sealed class PluginSettings
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<KeyCode> Toggle,Reset,Snapshot;
        readonly ConfigEntry<float> distance,shoulder,height,vertical,release,radius,skin,minimum,near;
        internal readonly ConfigEntry<int> CollisionMask;
        internal readonly ConfigEntry<float> DoubleClickWindow,ShoulderDistance,ShoulderFov;
        internal readonly ConfigEntry<KeyCode> SwapShoulder,CyclePreset;
        internal readonly ConfigEntry<bool> LeftShoulder,MovementFeedback,BodyFade;
        internal readonly ConfigEntry<CameraPreset> Preset;
        readonly ConfigEntry<float> exploreFov,runDistance,runShoulder,runHeight,runFov,crouchDistance,crouchShoulder,crouchHeight,crouchFov,aimShoulder,aimHeight;
        readonly ConfigEntry<float> positionResponse,distanceResponse,shoulderResponse,fovResponse,maxLag,feedbackAmplitude,releaseDelay,releaseDeadband,recoveryDelay,recoveryDistance,fadeNear,fadeFar;
        readonly ConfigEntryBase[] p4Entries;
        internal readonly ConfigEntry<bool> CapturePerformance;
        internal readonly ConfigEntry<KeyCode> CaptureToggle;
        internal readonly ConfigEntry<int> PerformanceFrames;
        internal readonly ConfigEntry<string> PerformanceCondition;
        readonly ConfigEntry<int> schema;
        internal readonly List<string> Corrections=new();
        internal PluginSettings(ConfigFile config)
        {
            bool autoSave=config.SaveOnConfigSet;config.SaveOnConfigSet=false;
            try
            {
            if(File.Exists(config.ConfigFilePath))
            {
                var bytes=File.ReadAllBytes(config.ConfigFilePath);string hash=Convert.ToHexString(SHA256.HashData(bytes));
                string backup=Path.Combine(Path.GetDirectoryName(config.ConfigFilePath),"ThirdPerson-P5-配置备份");Directory.CreateDirectory(backup);
                string target=Path.Combine(backup,hash+".cfg");if(!File.Exists(target))File.WriteAllBytes(target,bytes);
            }
            schema=config.Bind("维护","配置版本",0,"由插件管理；配置升级前保留内容寻址备份，不覆盖用户有效自定义。");
            if(schema.Value>5)throw new InvalidOperationException("配置来自更高版本，未进行降级改写；请使用对应版本插件。");
            Enabled=config.Bind("原型","启动时启用",false,"0.5.0 测试候选默认关闭，实机与联机总验收尚未完成。");
            Toggle=config.Bind("按键","开关",KeyCode.F6,"仅本体可操作时切换第三人称。");
            Reset=config.Bind("按键","恢复默认",KeyCode.F7,"恢复镜头默认参数并关闭第三人称。");
            Snapshot=config.Bind("按键","诊断快照",KeyCode.F8,"保存当前状态及恢复检查，不逐帧写日志。");
            distance=Float(config,"距离",2.25f,.5f,4);shoulder=Float(config,"右肩偏移",.48f,-1,1);
            height=Float(config,"支点高度偏移",.1f,-.35f,.6f);vertical=Float(config,"站蹲跟随响应",12,1,40);
            release=Float(config,"离墙恢复响应",5,.5f,30);radius=Float(config,"最小检测半径",.18f,.08f,.5f);
            skin=Float(config,"碰撞余量",.025f,.005f,.1f);minimum=Float(config,"最短有效距离",.38f,.15f,.8f);
            near=Float(config,"近裁剪面",.08f,.03f,.2f);
            CollisionMask=config.Bind("碰撞","自定义检测掩码",0,"0 使用游戏世界/动态/交互阻挡物组合；其他值按 Unity 层位掩码读取。始终排除自身 Collider 和触发器。");
            DoubleClickWindow=config.Bind("瞄准","双击窗口",.25f,new ConfigDescription("单击立即肩射；再次单击延迟退出，双击第一人称开镜。单位秒。",new AcceptableValueRange<float>(.12f,.5f)));
            ShoulderDistance=config.Bind("瞄准","肩射距离",1.35f,new ConfigDescription("第三阶段基础肩射距离，单位米。",new AcceptableValueRange<float>(.5f,3f)));
            ShoulderFov=config.Bind("瞄准","肩射视野",70f,new ConfigDescription("仅显示相机使用，第一人称倍率仍由原版负责。",new AcceptableValueRange<float>(45f,100f)));
            SwapShoulder=config.Bind("按键","左右换肩",KeyCode.F9,"只改变镜头肩位，记忆选择；不更换持枪手。");
            CyclePreset=config.Bind("按键","切换风格",KeyCode.F10,"依次切换 Balanced / Combat / Traversal / Stable。");
            LeftShoulder=config.Bind("风格","左肩",shoulder.Value<0,"保存手动选择，不自动频繁换肩。");
            Preset=config.Bind("风格","预设",CameraPreset.Balanced,"Balanced 默认；Combat 偏战斗；Traversal 偏探索；Stable 减少跟随与反馈。");
            MovementFeedback=config.Bind("风格","轻微移动反馈",false,"仅位置微动，默认关闭，肩射减弱，无附加翻滚。");
            BodyFade=config.Bind("风格","近身局部淡出",true,"已知 Standard 材质透明；GTFO 未确认透明契约的材质仅局部隐藏并保留影子，只作用主相机。");
            ConfigEntry<float> Value(string group,string name,float value,float min,float max,string unit)=>config.Bind(group,name,value,new ConfigDescription(unit,new AcceptableValueRange<float>(min,max)));
            exploreFov=Value("状态·探索","垂直视野",80,45,110,"垂直角度 °；非水平视野。");
            runDistance=Value("状态·奔跑","距离",2.6f,.5f,4,"米");runShoulder=Value("状态·奔跑","肩位",.42f,0,1,"米，方向由左右肩选择决定");runHeight=Value("状态·奔跑","高度偏移",.08f,-.35f,.6f,"米");runFov=Value("状态·奔跑","垂直视野",84,45,110,"垂直角度 °");
            crouchDistance=Value("状态·蹲伏","距离",1.95f,.5f,4,"米");crouchShoulder=Value("状态·蹲伏","肩位",.42f,0,1,"米");crouchHeight=Value("状态·蹲伏","高度偏移",.04f,-.35f,.6f,"米");crouchFov=Value("状态·蹲伏","垂直视野",78,45,110,"垂直角度 °");
            aimShoulder=Value("瞄准","肩位",.48f,0,1,"米");aimHeight=Value("瞄准","高度偏移",.08f,-.35f,.6f,"米");
            positionResponse=Value("过渡","水平跟随响应",18,1,40,"秒⁻¹；不对鼠标朝向阻尼");distanceResponse=Value("过渡","距离响应",10,1,40,"秒⁻¹");shoulderResponse=Value("过渡","换肩响应",12,1,40,"秒⁻¹");fovResponse=Value("过渡","视野响应",9,1,40,"秒⁻¹");maxLag=Value("过渡","最大水平滞后",.16f,0,.3f,"米");
            feedbackAmplitude=Value("风格","移动反馈幅度",.008f,0,.02f,"米；肩射乘 0.15，Stable 禁用");
            releaseDelay=Value("空间","离障稳定时间",.18f,0,1,"秒");releaseDeadband=Value("空间","离障死区",.04f,.005f,.15f,"米");recoveryDelay=Value("空间","第一人称恢复稳定时间",.28f,.1f,1,"秒");recoveryDistance=Value("空间","第一人称恢复距离",.6f,.4f,1,"米；同时至少超过最短有效距离 0.12 米");
            fadeNear=Value("空间","自身淡出近距",.12f,.05f,.5f,"镜头距 Renderer 包围盒的距离，米");fadeFar=Value("空间","自身淡出远距",.4f,.15f,1,"米，运行时保证大于近距 0.05 米");
            p4Entries=new ConfigEntryBase[]{SwapShoulder,CyclePreset,LeftShoulder,Preset,MovementFeedback,BodyFade,exploreFov,runDistance,runShoulder,runHeight,runFov,crouchDistance,crouchShoulder,crouchHeight,crouchFov,aimShoulder,aimHeight,positionResponse,distanceResponse,shoulderResponse,fovResponse,maxLag,feedbackAmplitude,releaseDelay,releaseDeadband,recoveryDelay,recoveryDistance,fadeNear,fadeFar};
            CapturePerformance=config.Bind("性能","采集",false,"显式开启一次有界采样；容量用尽或离开关卡后结束，不在默认状态逐帧写盘。");
            CaptureToggle=config.Bind("按键","性能采集",KeyCode.F11,"仅有效本体 FPS 焦点下开启/结束采样；第三人称关闭时也可采集基线。");
            PerformanceFrames=config.Bind("性能","采样帧数",3600,new ConfigDescription("60～7200 帧，记录实际帧时间；不能把逻辑测试当作实测 FPS。",new AcceptableValueRange<int>(60,7200)));
            PerformanceCondition=config.Bind("性能","固定条件","未标定","填写关卡/路线/分辨率/画质/装备/敌人负载/插件和小窗条件；未标定数据不能用于性能对照。");
            foreach(var pair in config)
            {
                var entry=pair.Value;
                if(entry.SettingType==typeof(float))
                {
                    var range=entry.Description.AcceptableValues as AcceptableValueRange<float>;
                    float value=(float)entry.BoxedValue,normalized=ConfigurationPolicy.Number(value,(float)entry.DefaultValue,range?.MinValue??float.MinValue,range?.MaxValue??float.MaxValue);
                    if(!value.Equals(normalized)){entry.BoxedValue=normalized;Corrections.Add(entry.Definition.ToString());}
                }
                else if(entry.SettingType.IsEnum&&!Enum.IsDefined(entry.SettingType,entry.BoxedValue)){entry.BoxedValue=entry.DefaultValue;Corrections.Add(entry.Definition.ToString());}
            }
            if(fadeFar.Value<fadeNear.Value+.05f){fadeFar.Value=fadeNear.Value+.05f;Corrections.Add("自身淡出远距");}
            foreach(var d in new[]{distance,runDistance,crouchDistance,ShoulderDistance})if(d.Value<minimum.Value+.15f){d.Value=minimum.Value+.15f;Corrections.Add(d.Definition.ToString());}
            schema.Value=5;config.Save();
            }
            finally{config.SaveOnConfigSet=autoSave;}
        }
        static ConfigEntry<float> Float(ConfigFile cfg,string name,float value,float min,float max)=>cfg.Bind("镜头",name,value,new ConfigDescription("原型参数，距离单位为米；响应按秒计算。",new AcceptableValueRange<float>(min,max)));
        internal CameraSettings Camera=>new CameraSettings(distance.Value,shoulder.Value,height.Value,vertical.Value,release.Value,radius.Value,skin.Value,minimum.Value,near.Value).Sanitized();
        internal StyleSettings Style=>new(new(distance.Value,shoulder.Value,height.Value,exploreFov.Value),new(runDistance.Value,runShoulder.Value,runHeight.Value,runFov.Value),
            new(crouchDistance.Value,crouchShoulder.Value,crouchHeight.Value,crouchFov.Value),new(ShoulderDistance.Value,aimShoulder.Value,aimHeight.Value,ShoulderFov.Value),
            positionResponse.Value,vertical.Value,distanceResponse.Value,shoulderResponse.Value,fovResponse.Value,maxLag.Value,MovementFeedback.Value,feedbackAmplitude.Value);
        internal SpatialSettings Spatial=>new SpatialSettings(releaseDelay.Value,releaseDeadband.Value,recoveryDelay.Value,recoveryDistance.Value).Sanitized();
        internal float FadeNear=>fadeNear.Value;
        internal float FadeFar=>fadeFar.Value;
        internal void Defaults()
        { distance.Value=2.25f;shoulder.Value=.48f;height.Value=.1f;vertical.Value=12;release.Value=5;radius.Value=.18f;skin.Value=.025f;minimum.Value=.38f;near.Value=.08f;CollisionMask.Value=0;DoubleClickWindow.Value=.25f;ShoulderDistance.Value=1.35f;ShoulderFov.Value=70;Enabled.Value=false;foreach(var entry in p4Entries)entry.BoxedValue=entry.DefaultValue;Toggle.Value=KeyCode.F6;Reset.Value=KeyCode.F7;Snapshot.Value=KeyCode.F8;CaptureToggle.Value=KeyCode.F11;CapturePerformance.Value=false; }
    }
}

