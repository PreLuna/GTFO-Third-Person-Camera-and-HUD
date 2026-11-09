using BepInEx.Configuration;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using PreLuna.ThirdPerson.CameraSystem;
using PreLuna.ThirdPerson.Collision;
using System;
using System.IO;
using System.Globalization;
using PreLuna.ThirdPerson.Control;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Runtime
{
    internal sealed class PluginSettings
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<bool> IndependentLook;
        internal readonly ConfigEntry<KeyCode> Observe;
        internal readonly ConfigEntry<float> ObserveYaw;
        internal readonly ConfigEntry<KeyCode> Toggle,Reset,Snapshot;
        readonly ConfigEntry<float> distance,shoulder,height,vertical,release,radius,skin,minimum,near;
        internal readonly ConfigEntry<int> CollisionMask;
        internal readonly ConfigEntry<float> DoubleClickWindow,ShoulderDistance,ShoulderFov;
        internal readonly ConfigEntry<KeyCode> SwapShoulder,CyclePreset;
        internal readonly ConfigEntry<bool> LeftShoulder,MovementFeedback,BodyFade,NativeGripPose,NativeSyncedHolding,NormalizeBodyScale;
        internal readonly ConfigEntry<CameraPreset> Preset;
        readonly ConfigEntry<float> exploreFov,runDistance,runShoulder,runHeight,runFov,crouchDistance,crouchShoulder,crouchHeight,crouchFov,aimShoulder,aimHeight;
        readonly ConfigEntry<float> positionResponse,distanceResponse,shoulderResponse,fovResponse,maxLag,feedbackAmplitude,releaseDelay,releaseDeadband,recoveryDelay,recoveryDistance,fadeNear,fadeFar;
        readonly ConfigEntry<bool> motionEnabled;
        readonly ConfigEntry<float> motionHorizontal,motionRoot,motionRootLag,motionLeadSeconds,motionLeadLimit,motionLeadResponse;
        readonly ConfigEntry<bool> transitionEnabled,spatialRefinementEnabled,actionFeedbackEnabled;
        readonly ConfigEntry<float> speedStart,speedFull,speedResponse,aimEnterResponse,aimExitResponse,retractionDistance,
            gaitStrength,startStopStrength,strideLength,actionResponse;
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
            if(schema.Value>9)throw new InvalidOperationException("配置来自更高版本，未进行降级改写；请使用对应版本插件。");
            Enabled=config.Bind("原型","启动时启用",false,"0.6.0 C3～C5联测候选默认关闭，按F6启用；C3连续缩放、C4空间避让默认开启，额外动作反馈由轻微移动反馈开关控制。");
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
            MovementFeedback=config.Bind("风格","轻微移动反馈",false,"C5位置反馈总开关，默认关闭；开启后才有起停／步态微动，肩射减弱，Stable关闭，无附加翻滚。false完全归零。");
            BodyFade=config.Bind("风格","近身局部淡出",true,"仅已知 Standard 材质允许透明淡出；其他材质保持完整可见，不再整块隐藏身体。只作用主相机。");
            NativeGripPose=config.Bind("模型","原生握点姿态",false,"实验性握点重定向，默认关闭以保留原生身体姿态。开启会额外调整主画面手臂骨骼；不重复开火或更新原版Animator，Ghost筛选始终生效。");
            NativeSyncedHolding=config.Bind("模型","原生同步持枪",true,"标准／特殊槽枪械复用原版全身动画、手型与独立原生IK。仅驱动本机身体，真实第一人称物品、AI和网络身份保持原版；换弹暂回第一人称。优先于实验性握点重定向。");
            NormalizeBodyScale=config.Bind("模型","第三人称标准身体比例",true,"第三人称身体准备时将已观测的本机视觉根1.2倍缩放还原为1倍；切回第一人称、换弹回退或停用时恢复原值。不改玩家根、FPS手臂或镜头参数；其他尺度保留。false对照0.6.1尺寸。");
            ConfigEntry<float> Value(string group,string name,float value,float min,float max,string unit)=>config.Bind(group,name,value,new ConfigDescription(unit,new AcceptableValueRange<float>(min,max)));
            exploreFov=Value("状态·探索","垂直视野",80,45,110,"垂直角度 °；非水平视野。");
            runDistance=Value("状态·奔跑","距离",2.6f,.5f,4,"米");runShoulder=Value("状态·奔跑","肩位",.42f,0,1,"米，方向由左右肩选择决定");runHeight=Value("状态·奔跑","高度偏移",.08f,-.35f,.6f,"米");runFov=Value("状态·奔跑","垂直视野",84,45,110,"垂直角度 °");
            crouchDistance=Value("状态·蹲伏","距离",1.95f,.5f,4,"米");crouchShoulder=Value("状态·蹲伏","肩位",.42f,0,1,"米");crouchHeight=Value("状态·蹲伏","高度偏移",.04f,-.35f,.6f,"米");crouchFov=Value("状态·蹲伏","垂直视野",78,45,110,"垂直角度 °");
            aimShoulder=Value("瞄准","肩位",.48f,0,1,"米");aimHeight=Value("瞄准","高度偏移",.08f,-.35f,.6f,"米");
            positionResponse=Value("过渡","水平跟随响应",18,1,40,"秒⁻¹；不对鼠标朝向阻尼");distanceResponse=Value("过渡","距离响应",10,1,40,"秒⁻¹");shoulderResponse=Value("过渡","换肩响应",12,1,40,"秒⁻¹");fovResponse=Value("过渡","视野响应",9,1,40,"秒⁻¹");maxLag=Value("过渡","最大水平滞后",.16f,0,.3f,"米");
            feedbackAmplitude=Value("风格","移动反馈幅度",.008f,0,.02f,"米；肩射乘 0.15，Stable 禁用");
            releaseDelay=Value("空间","离障稳定时间",.18f,0,1,"秒");releaseDeadband=Value("空间","离障死区",.04f,.005f,.15f,"米");recoveryDelay=Value("空间","第一人称恢复稳定时间",.28f,.1f,1,"秒");recoveryDistance=Value("空间","第一人称恢复距离",.6f,.4f,1,"米；同时至少超过最短有效距离 0.12 米");
            fadeNear=Value("空间","自身淡出近距",.12f,.05f,.5f,"镜头距 Renderer 包围盒的距离，米");fadeFar=Value("空间","自身淡出远距",.4f,.15f,1,"米，运行时保证大于近距 0.05 米");
            motionEnabled=config.Bind("镜头·C1","启用跟随支点",true,"启用起停惯性、根高度过滤和受限前视量；false对照054跟随。观察、射击方向及距离/FOV配置保持原语义。");
            motionHorizontal=Value("镜头·C1","水平响应上限",12,1,40,"秒⁻¹；与原有水平响应取较小值，肩射至少30，Stable使用40。");
            motionRoot=Value("镜头·C1","根高度响应",8,1,40,"秒⁻¹；独立于站蹲眼高过滤，肩射至少24，Stable使用40。");
            motionRootLag=Value("镜头·C1","最大根高度滞后",.12f,0,.25f,"米；肩射乘0.5，Stable乘0.25；支点路径受碰撞约束。");
            motionLeadSeconds=Value("镜头·C1","移动前视时间",.1f,0,.2f,"秒；按有效相邻帧实际水平速度估计，不改变朝向。");
            motionLeadLimit=Value("镜头·C1","最大移动前视量",.12f,0,.2f,"米；肩射乘0.15，Stable关闭；总水平支点偏移不超过0.35米。");
            motionLeadResponse=Value("镜头·C1","前视量响应",6,1,30,"秒⁻¹；起停和反向受限收敛，无随机晃动。");
            IndependentLook=config.Bind("镜头·C2","启用站定观察",true,"C2a候选：持枪站定、非瞄准且可正常第三人称出图时按住观察键绕看。松开、移动、瞄准或开火时立即交接原生朝向；移动中不开放绕看。false对照C1。");
            Observe=config.Bind("镜头·C2","观察按键",KeyCode.RightAlt,"默认右Alt，避开左Alt开麦键。按住生效；被动作中断后须松开再按。手柄沿用原生右摇杆曲线，但本阶段激活仍需此键，无独立手柄按钮映射。");
            // Only the old conflicting binding migrates once; preserve other custom keys.
            if(schema.Value<8&&Observe.Value==KeyCode.LeftAlt)
            {Observe.Value=KeyCode.RightAlt;Corrections.Add("镜头·C2.观察按键：旧版LeftAlt迁移为RightAlt，避开开麦键");}
            ObserveYaw=Value("镜头·C2","最大水平绕看角",65,10,75,"相对原生朝向的左右角度°；俯仰相对范围固定±35°且服从原生限位。180°绕看留待完整C2。");
            transitionEnabled=config.Bind("镜头·C3","启用连续速度过渡",true,"距离／肩位／高度／垂直FOV分别过渡，奔跑缩放依据实际水平位移。false对照059固定活动Profile；不改变观察或射击方向。");
            speedStart=Value("镜头·C3","速度缩放起点",1.5f,.25f,5,"米/秒，实际根位移；低于此速度使用探索构图。");
            speedFull=Value("镜头·C3","速度缩放终点",5.5f,1,12,"米/秒；至少高于起点0.5，达到后使用奔跑构图。速度曲线连续，非奔跑状态跳变。");
            speedResponse=Value("镜头·C3","速度滤波响应",5,1,20,"秒⁻¹；短按抑制过度拉远。");
            aimEnterResponse=Value("镜头·C3","进入肩射响应",20,8,40,"秒⁻¹；进入瞄准时各通道至少此响应，观察和射击意图立即生效。");
            aimExitResponse=Value("镜头·C3","退出肩射响应",8,2,20,"秒⁻¹；退出距离／肩位／视野柔和恢复，从当前值继续。");
            spatialRefinementEnabled=config.Bind("空间·C4","启用空间过渡深化",true,"新的压缩／释放、至多3次近位避让及独立空间恢复探测。false对照059空间路径；碰撞保护始终存在。");
            retractionDistance=Value("空间·C4","近位避让距离",.6f,.45f,1,"米；快速转向路径受阻时尝试更近的安全位置，所有移动段仍须扫掠，无法避让则回原生。");
            actionFeedbackEnabled=config.Bind("动作·C5","启用动作反馈求解",true,"使用起停与步态位置反馈；仍需风格／轻微移动反馈总开关为true。false对照059单轴步态，Stable始终禁用。");
            gaitStrength=Value("动作·C5","步态强度",1,0,1,"无单位；依据实际行进距离推进步态，不靠时间在静止时持续晃动。");
            startStopStrength=Value("动作·C5","起停强度",.5f,0,1,"无单位；水平加减速产生受限位置反馈，与步态共享移动反馈幅度预算。");
            strideLength=Value("动作·C5","步态周期距离",1.4f,.8f,2.5f,"米；一次完整步态相位的实际行进距离，不驱动人物动画。");
            actionResponse=Value("动作·C5","反馈响应",12,4,30,"秒⁻¹；起停收敛，无随机漂移、额外翻滚或重复原生后坐力。");
            p4Entries=new ConfigEntryBase[]{SwapShoulder,CyclePreset,LeftShoulder,Preset,MovementFeedback,BodyFade,NativeGripPose,NativeSyncedHolding,NormalizeBodyScale,exploreFov,runDistance,runShoulder,runHeight,runFov,crouchDistance,crouchShoulder,crouchHeight,crouchFov,aimShoulder,aimHeight,positionResponse,distanceResponse,shoulderResponse,fovResponse,maxLag,feedbackAmplitude,releaseDelay,releaseDeadband,recoveryDelay,recoveryDistance,fadeNear,fadeFar,motionEnabled,motionHorizontal,motionRoot,motionRootLag,motionLeadSeconds,motionLeadLimit,motionLeadResponse,IndependentLook,Observe,ObserveYaw,transitionEnabled,speedStart,speedFull,speedResponse,aimEnterResponse,aimExitResponse,spatialRefinementEnabled,retractionDistance,actionFeedbackEnabled,gaitStrength,startStopStrength,strideLength,actionResponse};
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
            if(speedFull.Value<speedStart.Value+.5f){speedFull.Value=speedStart.Value+.5f;Corrections.Add("速度缩放终点");}
            schema.Value=9;config.Save();
            }
            finally{config.SaveOnConfigSet=autoSave;}
        }
        static ConfigEntry<float> Float(ConfigFile cfg,string name,float value,float min,float max)=>cfg.Bind("镜头",name,value,new ConfigDescription("原型参数，距离单位为米；响应按秒计算。",new AcceptableValueRange<float>(min,max)));
        internal CameraSettings Camera=>new CameraSettings(distance.Value,shoulder.Value,height.Value,vertical.Value,release.Value,radius.Value,skin.Value,minimum.Value,near.Value).Sanitized();
        internal StyleSettings Style=>new(new(distance.Value,shoulder.Value,height.Value,exploreFov.Value),new(runDistance.Value,runShoulder.Value,runHeight.Value,runFov.Value),
            new(crouchDistance.Value,crouchShoulder.Value,crouchHeight.Value,crouchFov.Value),new(ShoulderDistance.Value,aimShoulder.Value,aimHeight.Value,ShoulderFov.Value),
            positionResponse.Value,vertical.Value,distanceResponse.Value,shoulderResponse.Value,fovResponse.Value,maxLag.Value,MovementFeedback.Value,feedbackAmplitude.Value);
        internal SpatialSettings Spatial=>new SpatialSettings(releaseDelay.Value,releaseDeadband.Value,recoveryDelay.Value,recoveryDistance.Value).Sanitized();
        internal MotionSettings Motion=>new MotionSettings(motionEnabled.Value,motionHorizontal.Value,motionRoot.Value,motionRootLag.Value,motionLeadSeconds.Value,motionLeadLimit.Value,motionLeadResponse.Value).Sanitized();
        internal TransitionSettings Transition=>new TransitionSettings(transitionEnabled.Value,speedStart.Value,speedFull.Value,speedResponse.Value,aimEnterResponse.Value,aimExitResponse.Value).Sanitized();
        internal SpatialRefinementSettings Refinement=>new SpatialRefinementSettings(spatialRefinementEnabled.Value,retractionDistance.Value).Sanitized();
        internal ActionFeedbackSettings ActionFeedback=>new ActionFeedbackSettings(actionFeedbackEnabled.Value,gaitStrength.Value,startStopStrength.Value,strideLength.Value,actionResponse.Value).Sanitized();
        internal float FadeNear=>fadeNear.Value;
        internal float FadeFar=>fadeFar.Value;
        internal ThirdPersonControlValue ReadControl(string key)
        {
            switch (key)
            {
                case "distance": return ThirdPersonControlValue.Number(distance.Value);
                case "shoulder": return ThirdPersonControlValue.Number(shoulder.Value);
                case "height": return ThirdPersonControlValue.Number(height.Value);
                case "shoulderDistance": return ThirdPersonControlValue.Number(ShoulderDistance.Value);
                case "leftShoulder": return ThirdPersonControlValue.Boolean(LeftShoulder.Value);
                default: return null;
            }
        }

        internal void WriteControl(string key, ThirdPersonControlValue value)
        {
            if (value == null) return;
            if (key == "leftShoulder")
            {
                if (value.TryGetBoolean(out var left)) LeftShoulder.Value = left;
                return;
            }

            if (!value.TryGetNumber(out var number)) return;
            var single = (float)number;
            switch (key)
            {
                case "distance": distance.Value = single; break;
                case "shoulder": shoulder.Value = single; break;
                case "height": height.Value = single; break;
                case "shoulderDistance": ShoulderDistance.Value = single; break;
            }
        }

        internal string SerializeControl(string key)
        {
            var value = ReadControl(key);
            if (value == null) return null;
            if (value.TryGetBoolean(out var boolean)) return boolean ? "true" : "false";
            if (value.TryGetNumber(out var number)) return number.ToString("0.###", CultureInfo.InvariantCulture);
            return null;
        }

        internal void Defaults()
        { distance.Value=2.25f;shoulder.Value=.48f;height.Value=.1f;vertical.Value=12;release.Value=5;radius.Value=.18f;skin.Value=.025f;minimum.Value=.38f;near.Value=.08f;CollisionMask.Value=0;DoubleClickWindow.Value=.25f;ShoulderDistance.Value=1.35f;ShoulderFov.Value=70;Enabled.Value=false;foreach(var entry in p4Entries)entry.BoxedValue=entry.DefaultValue;Toggle.Value=KeyCode.F6;Reset.Value=KeyCode.F7;Snapshot.Value=KeyCode.F8;CaptureToggle.Value=KeyCode.F11;CapturePerformance.Value=false; }
    }
}

