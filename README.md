# GTFO-Third-Person-Camera-and-HUD

第三人称与HUD。各MOD的维护文件直接放在自己的目录内。

| MOD／目录 | 版本 | 维护方式 | 必要前置／声明版本 | 可选联动／软依赖 |
| --- | --- | --- | --- | --- |
| [第三人称共享接口（非独立MOD）](Shared/ThirdPerson.Contracts/) | 接口v1 | 共享接口，不是独立MOD | 供第三人称与HUD共用 | 无插件软依赖声明 |
| [第三人称镜头](ThirdPerson/) | 0.5.0 | 源码 | 无插件硬前置声明；需共享契约DLL | 无插件软依赖声明 |
| [第三人称HUD](ThirdPersonHUD/) | 0.11.0 | 源码 | 第三人称镜头；需共享契约DLL | 无插件软依赖声明 |

所有插件均需GTFO与相应BepInEx IL2CPP环境。无插件硬依赖声明不表示可脱离游戏运行；软依赖也可能对应某项功能的启用条件。版本列保留加载器声明表达式。

## MOD之间的关系

ThirdPersonHUD硬依赖ThirdPerson；两者编译及运行共用Shared/ThirdPerson.Contracts，运行时只安装一份契约DLL。ThirdPerson通过OptionalHeadCameraProbe适配机器人仓的HeadCamera，属于可选联动。契约或摄像头身份接口变化时，两个仓库共同回归。

## 下载与安装

普通玩家从 [Releases](https://github.com/PreLuna/GTFO-Third-Person-Camera-and-HUD/releases) 下载安装ZIP，退出游戏后将BepInEx合并到已配置前置的游戏目录，替换同功能旧版以免重复加载。个人配置不包含在安装包中。Source code ZIP用于开发。已发布标签固定对应原提交；本地目录整理不覆盖旧安装包。

构建、测试及修改入口见 [DEVELOPMENT.md](DEVELOPMENT.md)，来源见 [NOTICE.md](NOTICE.md)，机器可读路径和依赖见 [repo.json](repo.json)。
