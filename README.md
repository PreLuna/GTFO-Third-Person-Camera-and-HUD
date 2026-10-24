# GTFO-Third-Person-Camera-and-HUD

第三人称镜头与HUD。本仓包含以下MOD／共享组件，各自维护文件放在自己的目录。

[定位入口](#modules) · [依赖与联动](#relations) · [开发与排查](#development) · [来源说明](#sources)

<a id="modules"></a>
## 定位入口

| 功能／组件 | 版本 | 修改位置与输入 | 维护方式 | 测试材料 |
| --- | --- | --- | --- | --- |
| 第三人称共享接口（非独立MOD） `ThirdPerson.Contracts` | None | [Shared/ThirdPerson.Contracts/src](Shared/ThirdPerson.Contracts/src) | 共享接口源码（非独立MOD） | [Shared/ThirdPerson.Contracts/tests](Shared/ThirdPerson.Contracts/tests) |
| 第三人称镜头 `ThirdPerson` | 0.5.0 | [ThirdPerson/src](ThirdPerson/src) | 源码 | 由仓级入口／配方核对 |
| 第三人称HUD `ThirdPersonHUD` | 0.11.0 | [ThirdPersonHUD/src](ThirdPersonHUD/src) | 源码 | [ThirdPersonHUD/tests](ThirdPersonHUD/tests) |

具体路径、版本、GUID、资源和依赖的机器登记见[repo.json](repo.json)。src是实现，tests是测试；IntegrationTests用于跨MOD场景或调度配方，Shared仅保存真正共享的接口。

仓级测试配方与共享场景见[IntegrationTests](IntegrationTests)。测试文件是否实际执行，以根Test.ps1及配方为准。

普通玩家从[Releases](https://github.com/PreLuna/GTFO-Third-Person-Camera-and-HUD/releases)下载已经构建的安装ZIP，按对应版本说明安装并准备前置；不需要构建工具仓。退出游戏后将ZIP中的BepInEx合并到已配置前置的游戏目录，替换同功能旧版以免重复加载；个人CFG不包含在安装包中。GitHub的Source code／Code ZIP用于开发，不能把baseline原件直接当安装DLL。

<a id="relations"></a>
## 依赖与联动

HUD运行依赖ThirdPerson；二者编译及运行共用ThirdPerson.Contracts，运行时只安装一份契约DLL；接口修改时必须共同核验。ThirdPerson还通过可选桥与机器人仓HeadCamera联动，该桥不构成加载器硬前置。

| 模块 | 插件硬前置／声明版本 | 可选联动／软依赖 |
| --- | --- | --- |
| ThirdPerson | 无插件硬前置声明 | 无插件软依赖声明 |
| ThirdPersonHUD | ThirdPerson | 无插件软依赖声明 |

所有插件仍需GTFO及相应BepInEx IL2CPP环境。版本保留加载器原始声明表达式；编译引用、运行硬前置和可选桥分别判断。

当前编译引用：`ThirdPerson` → `ThirdPerson.Contracts`；`ThirdPersonHUD` → `ThirdPerson.Contracts`, `ThirdPerson`。

跨仓关联：[机器人／摄像头与第三人称另一侧仓库](https://github.com/PreLuna/GTFO-Bot-Commands-and-Control)。修改视角身份和契约时两侧联测；仅凭同仓分组不能推导安装依赖。

<a id="development"></a>
## 开发与排查

| 要修改／排查的功能 | 先读哪里 | 联动范围 |
| --- | --- | --- |
| 镜头、碰撞、瞄准 | [ThirdPerson/src](ThirdPerson/src) | Camera、Collision、Core与Runtime分别负责求解和游戏接线。 |
| HUD显示与收集 | [ThirdPersonHUD/src](ThirdPersonHUD/src) | 同时核对ThirdPersonHUD/tests、IntegrationTests及共享契约。 |
| 镜头与HUD接口 | [Shared/ThirdPerson.Contracts/src](Shared/ThirdPerson.Contracts/src) | 接口变更需重建镜头和HUD；HeadCamera联动属于跨仓可选桥。 |

开发环境：支持C#12编译的PowerShell 7、.NET 6 runtime、GTFO/BepInEx的core与生成的interop。将[共用工具仓](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools)放在本仓旁边，可通过BuildToolsRoot覆盖位置。

先审查并登记输入／维护锁，再在本仓目录执行（路径换成本机实际值，重复构建前显式清理本仓忽略的out）：

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
```

根Test.ps1按IntegrationTests/recipes.json调度18组，覆盖镜头、HUD、共享契约及受控运行场景；不替代真实游戏渲染和联机。

构建与离线测试不安装到游戏。`.cs`是功能／测试／辅助源码，`.ps1`是维护脚本，编译所得`.dll`才供游戏加载；运行CFG不加入源码仓。

维护锁与提交步骤见[共用维护流程](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#maintenance)，打包要求见[发布与打包](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#packaging)。仅使用Releases的玩家不需要这些开发步骤。

<a id="sources"></a>
## 来源说明

本仓库维护 PreLuna 的构建或整合资料。

保留源文件中的作者与版权声明。部分模组包含第三方派生实现或冻结基线，本说明不替代上游许可证，也不新增再分发授权；本仓库没有统一重授权声明。
