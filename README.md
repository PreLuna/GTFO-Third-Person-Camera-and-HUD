# GTFO-Third-Person-Camera-and-HUD

GTFO本机第三人称镜头、HUD与共享视角接口。当前源码为 **ThirdPerson0.6.32、ThirdPersonHUD0.15.3、Contracts API1**。镜头保留已验收的身体／装备、相机恢复与阶段0–1b指向提示；HUD支持DivisionV3、FourCorner、Radial、Compact四种布局，ShortPrompt／HideNativePrompt源码默认关闭。

本轮新增镜头8项和HUD4项按键控制字段，供ModControl0.3.7统一管理。0.6.32修复F6等正常自动保存之后改键被误判为外部修改的问题。用户2026-11-19确认“测试通过，保持新键，重启后可以保留”，本轮左右换肩／站定观察保存修复已通过；未追加18项全部逐项、联机或极限条件验收。

阶段2交互射线对齐按已有决策不实施，范围与原因见[阶段2说明](阶段2-实验设计与不实施说明.md)。组件仍按原有目录安装；无新增运行依赖，源码提交不代表创建新的安装Release。

[组件与安装](#modules) · [按键与配置](#usage) · [统一改键与保存](#keybindings) · [HUD视觉目标](#hud-visual-target) · [Contracts用途](#contracts) · [依赖与联动](#relations) · [开发与排查](#development) · [装备实现与验收](第三人称装备实现与验收.md) · [镜头长期方案](#camera-roadmap) · [第三人称指向性设计](第三人称指向性设计方案.md) · [来源说明](#sources)

<a id="modules"></a>
## 组件与安装

| 组件 | 当前版本／接口 | 修改入口 | 职责 |
| --- | --- | --- | --- |
| `ThirdPerson` | 0.6.32（自动保存修订与按键保存修复） | [ThirdPerson/src](ThirdPerson/src)、[tests](ThirdPerson/tests) | 镜头跟随、观察、肩射、碰撞与身体显示，提供 ModControl 控制API |
| `ThirdPersonHUD` | 0.15.3（HUD控制API及4项按键接入） | [ThirdPersonHUD/src](ThirdPersonHUD/src)、[tests](ThirdPersonHUD/tests) | 读取有效视角状态并在四种布局间切换绘制HUD；提供面板/个人/队伍/场景/提示区/1b提示控制API |
| `ThirdPerson.Contracts` | API 1；无独立插件版本 | [Shared/ThirdPerson.Contracts/src](Shared/ThirdPerson.Contracts/src)、[tests](Shared/ThirdPerson.Contracts/tests) | 共享数据、接口和HUD有效性规则，非独立MOD |

精确版本、GUID、编译依赖和测试入口以[repo.json](repo.json)为准。`src`为活动源码，`tests`为测试，跨组件调度见[IntegrationTests/recipes.json](IntegrationTests/recipes.json)。

0.11.3用户截图与日志已确认HUD能绘制，但整份个人数据采集失败、字段全部不可读。0.11.4修正[HudGameContentSource](ThirdPersonHUD/src/Runtime/HudGameContentSource.cs)中的时间比较：同一份资格校验使用一个新鲜时间值，避免“先取得当前时间，再读取更晚的完成时间”使正常采集被判为未来结果。真正未来、过期或身份不符数据仍拒绝；失败日志现在包含具体校验原因或异常。

Compact个人页只显示一次当前装备、其余有效装备与实际持有物品。未携带／不可读／名称不可确认的可选栏目隐藏，边框随实际行数缩短；有效的0弹药或0使用次数保留，不伪装成缺失。队友默认不在个人页显示，按End进入独立队伍页，再按回个人页；隐藏队伍页时停止队伍采集、恢复原版队友读数。Insert仍控制整个HUD，第一人称与非本机画面继续隐藏。

游戏确有不同槽位与字段，分类依据本机实际interop核对：

| HUD名称 | 原生分类／字段 | 含义 |
| --- | --- | --- |
| 工具 | `InventorySlot.GearClass`（3） | 职业工具槽 |
| 消耗品 | `InventorySlot.Consumable`（5） | 普通消耗品槽 |
| 重型消耗品 | `InventorySlot.ConsumableHeavy`（6） | 独立消耗品槽，不能与InLevelCarry混为同类 |
| 任务携带物 | `InventorySlot.InLevelCarry`（8） | 场内携带物槽；以实际物品名称显示 |
| 搬运中 | 当前手持槽为`InLevelCarry`时映射的动作状态 | 不再重复列成额外库存栏目，不虚构数量 |

`GameData.ItemDataBlock.inventorySlot`定义物品槽位，`PlayerInventoryBase.WieldedSlot`表示当前手持槽，`PlayerBackpack.TryGetBackpackItem(InventorySlot,...)`读取对应背包项；具体物品依照实际槽位归类，不按中文“重型”名称或外观猜测。保留0.11.3原生字体创建修复；旧工厂缺陷、回退DLL与前轮源码见维护证据，不将旧离线通过当作游戏验收。

本次增量只替换`PreLuna.ThirdPersonHUD.dll`至下表既有HUD目录；镜头0.6.2、Contracts、MicroMaster沿用，无新增依赖／资源，不要求删CFG或按F7。进入关卡持枪启用F6第三人称，检查当前样式出现；F5循环四种布局，F6回第一人称应隐藏，Insert按一次隐藏／显示整个HUD、Pause恢复原版HUD。助手没有安装或启动游戏，未提交、推送或发布。

已验证的本机布局如下，路径相对于GTFO游戏目录。已有安装继续沿用，不为匹配源码仓层级重排目录。

| 运行文件 | 游戏内位置 |
| --- | --- |
| `PreLuna.ThirdPerson.dll` | `BepInEx/plugins/本地定制/PreLuna.ThirdPerson/` |
| `PreLuna.ThirdPerson.Contracts.dll` | 与镜头DLL同目录，仅保留一份 |
| `PreLuna.ThirdPersonHUD.dll` | `BepInEx/plugins/本地定制/PreLuna.ThirdPersonHUD/` |

现装镜头 0.6.19 已获用户实机确认，包含此前通过的身体比例修复。镜头增量沿用上表布局，只替换镜头 DLL；Contracts、HUD 和微操大师沿用现装匹配版本，无新增依赖或资源。重进关卡残缺身体的关联修复位于 MicroMaster 0.5.17，继续使用已验收的兼容版本。

普通玩家使用[Releases](https://github.com/PreLuna/GTFO-Third-Person-Camera-and-HUD/releases)中实际发布的安装包及其版本说明。当前本地验收不表示对应版本已发布；Source code／Code ZIP不是安装包。增量更新先退出游戏，按当次清单只替换指定文件，避免重复加载；个人CFG不随包分发。

<a id="usage"></a>
## 按键与配置

以下为镜头默认键位；个人CFG有自定义时以个人值为准。只在可操作的本机角色及合适焦点下生效。

| 操作 | 默认按键／方式 |
| --- | --- |
| 开关第三人称 | F6；全新配置默认关闭，已有启动状态保留 |
| 恢复镜头默认参数 | F7；会关闭第三人称并重置个人镜头设置，不用于日常对照 |
| 保存诊断快照 | F8；输出至`BepInEx/ThirdPerson-P5-诊断`，schema6含阶段0`pointer`字段 |
| 指向调试覆盖层 | F12；默认关闭，只读显示中心/相机/眼睛命中、原版选中目标、当前交互物和锁候选；CFG`[调试] 指向覆盖层=true`可默认开启，`[按键] 指向覆盖层`可改键，不改原版选择/命中 |
| 视角点／目标标记 | 1a；CFG`[指向] 视角点`和`[指向] 目标标记`默认开启。视角点在枪械/其他装备用屏幕中心，近战默认用眼睛射线 2.5 m 内首个命中点（`[指向] 近战眼睛基准=true`，可关闭回中心）；目标标记只显示原版当前选中交互目标，替代旧青色圈，不改原版选择/命中；F12调试覆盖层开启时由调试标记接管 |
| 1b短提示 | CFG`[Scene] ShortPrompt`/`HideNativePrompt`默认关闭；开启后用贴近目标位置的短提示替代HUD交互/补给卡片，位置不可用时回退卡片；`HideNativePrompt` 用 `CanvasGroup` 隐藏原版静态提示，不改原版选择/执行。两个开关已暴露为 HUD 控制 API `shortPrompt`/`hideNativePrompt`，可在 ModControl“第三人称HUD”页运行中切换；仅本机客户端视觉，不影响其他玩家 |
| 卡顿自动快照 | CFG `[调试] 卡顿自动快照`默认关闭。关卡内开启第三人称后，帧间隔超过`卡顿阈值毫秒`（默认 80）时后台写入 `BepInEx/ThirdPerson-P5-诊断/卡顿-*.json`，冷却`卡顿冷却秒`（默认 2），每次启动最多 8 份。记录资格、释放原因、绑定代数及耗时。 |
| 手动换肩 | F9；改变镜头侧，不改变持枪手 |
| 切换预设 | F10；Balanced均衡→Combat战斗→Traversal探索→Stable舒适 |
| 开始／结束有界性能采样 | F11；先在CFG填写固定条件，同地图／路线／画质／负载比较 |
| 站定有限绕看 | 持枪站定、非瞄准时按住右Alt并移动鼠标；左Alt保留游戏开麦用途 |
| HUD整体显示／隐藏 | 第三人称下按`[Presentation] Style`常驻DivisionV3、FourCorner、Radial或Compact之一；Insert按一次隐藏、再按显示；第一人称隐藏，返回第三人称保留本次选择，重启默认显示 |
| HUD样式循环 | F5；有效本机第三人称下依次切换DivisionV3→FourCorner→Radial→Compact，配置里的`Style`仍是下次启动值；可在`[Presentation] CycleStyleKey`改绑或设None关闭 |
| HUD队友开关 | End开关队伍信息；DivisionV3默认隐藏右下铭牌，FourCorner/Radial默认显示队伍卡片，Compact进入／退出独立队伍页；可在HUD CFG的`[Team] PageKey`改绑 |
| 肩射／第一人称开镜 | 沿用原有瞄准键；单击进入肩射、再次单击延迟退出，双击交回原版开镜 |

右Alt观察默认相对水平±65°、俯仰±35°并服从原生限位。松键、移动、瞄准或开火开始前交接当前观察方向；动作中断后须松开再按。手柄沿用原生右摇杆曲线，但本阶段激活仍需键盘观察键，没有独立手柄按钮映射。站定判定只使用XZ水平速度与移动输入，原生贴地垂直速度不再被误判为水平移动。

HUD CFG为`BepInEx/config/PreLuna.ThirdPersonHUD.cfg`。`[Panel.Input] ToggleHudVisibility=true`为默认整体开关模式，`ExpandKey=Insert`；全新和已有CFG缺少该字段时都默认启用。显式设false可回旧详情模式，此时`ToggleExpand`决定按住或切换详情。仍只在第三人称显示；已有自定义键及其他个人设置保留，无需删除CFG。

`[Presentation] Style`选择启动样式；`CycleStyleKey`默认F5，只改本次运行内样式，不写回CFG。新增`FourCorner`/`Radial`的几何、字号和颜色在`[Classic.Layout]`、`[Classic.Text]`、`[Classic.Theme]`独立保存，默认值来自v1/v2线框；切换回DivisionV3或Compact不会重置这些值。`Classic.Layout.FollowBody=true`时Radial圆心使用角色根部上方`WaistHeight`（默认0.9米）的主相机投影，`RadialOffsetY`默认100px向下；缩放、屏幕边距和环绕半径可分别调整。

镜头CFG为`BepInEx/config/PreLuna.ThirdPerson.cfg`。修改前退出游戏，保留已有同名分组；无需删除CFG或按F7。当前schema9保留有效显式参数、自定义键和启动状态，升级前自动保留原CFG。回退0.5.9时必须同时恢复匹配的schema8配置。

| 设置 | 默认行为／用途 |
| --- | --- |
| `[镜头·C1] 启用跟随支点` | true；独立水平／根高度跟随与受限前视量 |
| `[镜头·C2] 启用站定观察`、`观察按键` | true、RightAlt；有限站定观察 |
| `[镜头·C3] 启用连续速度过渡` | true；实际速度驱动距离／FOV，分通道过渡 |
| `[空间·C4] 启用空间过渡深化` | true；近位避让、低顶处理与稳定恢复 |
| `[动作·C5] 启用动作反馈求解` | true；选择新起停／步态求解器，仍受总开关控制 |
| `[风格] 轻微移动反馈` | **false**；额外微动总开关，开启才有C5起停／步态效果 |
| `[风格] 移动反馈幅度` | 0.008米；设0或关闭总开关可完全归零，Stable始终禁用额外反馈 |
| `[动作·C5] 步态强度`、`起停强度` | 1、0.5；可分别设0作独立对照 |
| `[模型] 原生同步持枪`、`原生握点姿态` | true、false；以原生身体动画及独立IK为底座，额外手臂重定向默认关闭 |
| `[模型] 第三人称标准身体比例` | true；将已观测的本机视觉根均匀1.2倍校正至1倍，第一人称恢复原值；false对照0.6.1尺寸 |

关闭C3／C4分别回到旧活动Profile／旧空间路径。关闭C5求解器仍可能保留旧步态；完全去除额外微动应关闭总开关。预设切换不代替阶段开关，聊天会提示当前预设、C3／C4及动作反馈状态。

<a id="keybindings"></a>
## 统一改键与自动保存修订

可选安装ModControl0.3.7，从一级“按键绑定”修改下列字段；未安装管理模块时仍读取相同CFG并独立响应原输入。单键使用Unity KeyCode名称，None关闭绑定；页面Esc清除，观察仍为按住。

| CFG | 字段／默认值 |
| --- | --- |
| `PreLuna.ThirdPerson.cfg`／`[按键]` | 开关F6、恢复默认F7、诊断快照F8、左右换肩F9、切换风格F10、性能采集F11、指向覆盖层F12 |
| 镜头CFG／`[镜头·C2]` | 观察按键RightAlt |
| `PreLuna.ThirdPersonHUD.cfg` | `[Panel.Input] ExpandKey=Insert`、`[Team] PageKey=End`、`[Presentation] CycleStyleKey=F5`、`[OriginalHUD] RestoreKey=Pause` |

控制API沿用GetSnapshotJson／ValidateJson／SetPersistentJson，新增 `key.*` 整数键码；运行端校验KeyCode，保存时仍使用F9／RightAlt／None等原CFG文本。未知键、段和注释由原行级存储保留，失败回滚运行值。镜头“恢复默认”沿用原行为，会重置部分镜头按键，管理页会读回更新；HUD恢复键只在FPS焦点有效且非聊天时响应。

**修复原因与边界。** F6修改`[原型] 启动时启用`并触发BepInEx自动Save，该字段不在原控制字段清单中。旧状态仅在受管值变化时吸收文件指纹，于是换肩／观察改键使用过期指纹，合法写入被误拒绝。现在[Plugin](ThirdPerson/src/Runtime/Plugin.cs)在SettingChanged且SaveOnConfigSet=true时通知[ThirdPersonControlState](ThirdPerson/src/Control/ThirdPersonControlState.cs)的ObserveAutomaticSave，同步已成功保存的指纹与修订。已核验实际BepInEx顺序为Save成功后触发事件；控制器写入暂时关闭自动保存，不吸收未完成写入或后续外部改动，原冲突与回滚保护保留。

[Test.ps1](Test.ps1)运行39组原配方及[真实配置回归](IntegrationTests/Keybinds/KeybindAutomaticSaveTests.cs)7项。后者从本仓构建清单定位本机BepInEx.Core，执行实际托管ConfigFile与JSON API，验证F6后两个报告动作、诊断自动保存、外部编辑仍拒绝／回滚、关闭自动保存不放行、旧修订拒绝、None清除／全新配置实例读回和旧行为反例；缺构建清单会明确跳过，不宣称执行。该测试不启动插件Load、Unity或游戏。实机确认限于用户报告的新键保持和游戏重启后保留。既有构建证据与本次文档核验分别记录，不因提交文档重复构建。

<a id="hud-visual-target"></a>
## HUD视觉目标与当前实现：v3实机还原＋四角／环绕

用户指定工作区`研究截图/线框布局/v3_实机还原`为DivisionV3的视觉实现目标，`实机动作HUD/04_全境封锁2`为实机表现参考。DivisionV3仍是默认样式；v1四角版和v2角色环绕作为保留草案，在0.13.0中作为独立可切换样式重新实现，不再决定DivisionV3布局。参考素材是本地设计输入，不是运行依赖；原图、SVG与生成脚本本轮保持不变。本节记录当前约定，具体像素、字号、颜色和显示规则仍可讨论。

### 0.13.0四角版与角色环绕实现

两套新样式直接复用已验证的个人、队伍和场景映射，不新增采集字段：

| 样式 | 主要布局 | 默认行为／边界 |
| --- | --- | --- |
| `FourCorner` | 画布1920×1080、48px边距；左上任务600×84、右上队伍336×276、左下个人420×288、右下装备420×288；中央只绘制真实交互卡 | 生命10段橙色条＋白色大读数；感染4段，零值静默；装备显示主/副武器、工具和可确认物品；缺失可选项隐藏；不绘制假准星或任务距离 |
| `Radial` | 圆心跟随角色腰部投影，默认生命/感染弧半径170；任务标签在角色左上、队伍标签在肩线右侧、装备条在脚下居中；交互只在真实交互时出现环与文字 | 弧线按真实Fraction填充；生命低转危险色；感染零值静默；队伍/任务标签保留角色间距；装备条显示当前枪械或主武器，不显示假的搬运百分比 |

两套布局默认不覆盖原版队伍栏和倒计时，只按现有替换规则覆盖已完整绘制的生命、弹药、工具和物品读数；因此原版队伍/倒计时仍可见，End可隐藏本模组的队伍卡片。Insert继续控制整个HUD，Pause恢复原版。样式切换先恢复原版覆盖并清旧投影，再等待新样式成功绘制。

可调入口为`[Classic.Layout] Scale/Margin/Top/Bottom/RadialScale/RadialOffsetX/RadialOffsetY/FollowBody/WaistHeight/FollowSeconds`、`[Classic.Text] LabelSize/BodySize/NumberSize`和`[Classic.Theme] BackgroundOpacity/AccentColor/TextColor/TeamColor/DangerColor`。`DivisionV3`参数、`Compact`参数和个人CFG独立保留。离线预览由生产绘制命令生成在`研究截图/线框布局/v4_实现预览`，只用于排版核验，不等于游戏截图或Unity原生验收。

### 0.12.1实机反馈后的细化

用户确认0.12.0能正常加载，后续同意按“信息重心→图标／窄列→人物关联→整簇轻透视”的顺序调整。当前取消常驻橙底生命大方块，生命由更厚分段条与小百分比承担；橙色聚焦当前装备／危险状态，弹匣与备用纵向排列，副武器窄列，工具和物品使用自制分类图形并保留真实名称。近战／空手／携带物缩为图标、名称和状态，无假弹药或搬运进度。感染零值静默，真实非零与异常仍区分。

新增`[DivisionV3.Motion] FollowBody=true / WaistHeight=0.9 / FollowSeconds=0.10`：由本次主相机投影角色根上方参考点，跟随纵向与受限横向变化，保留视口边界／准星避让。它是腰部高度参考，尚无姿态骨骼采样，不声称精确绑定蹲姿骨盆。`Top`在跟随模式下表示围绕686基准的纵向微调；`FollowBody=false`回到固定屏幕锚点。失效、身份／配置变化立即重置，不用缓慢隐藏掩盖过期读数。

新增`[DivisionV3.Plane] TiltDegrees=2 / NumberWidth=0.82`，配合已有`TileSlant`对所有核心条、文字、图标施加同一有界仿射变换，近似轻透视；不宣称世界空间3D投影。纯ASCII数字压窄，带中文单位不压窄；不新增字体资源。外围任务／扫描／交互／铭牌保持屏幕正向。每次绘制（含异常）恢复调用方GUI矩阵／颜色；Compact不进入该变换路径。

旧CFG显式参数继续保留，新增键自动补齐；全新配置20段，已有10段不擅自重置。Health强调选项放大生命小读数、收敛弹药字号，不恢复橙底大卡片。右侧雷达、强化剂与其他MOD外围本轮不调整。

### 已确认的方向与修正

- 0.12.0通过`[Presentation] Style=DivisionV3|Compact`切换；0.13.0扩展为`DivisionV3|FourCorner|Radial|Compact`，缺省仍为DivisionV3。Compact保留0.11.4竖向精简面板、旧Panel配置与End独立队伍页。无需删CFG，首次运行新增分组。
- v3采用角色腰胯附近的紧凑信息簇：分段状态条、细感染线、小数值板和少量物品图标；取消整块大外框。任务／通知／队友为独立区域，中心给真实准星和交互。
- 用户明确：拿起物品是短暂交互，随后是持有／移动状态，**没有持续“搬运进度”**。删除线框中的“搬运中62%”及对应动作条；携带时只显示实际物品名／图标或简短状态，不显示模拟百分比、计时或剩余距离。按键文字从游戏实际绑定读取，不把E写死。
- 用户明确：1920×1080越肩参考中角色约在42%宽，核心簇起点x≈860；若实际GTFO角色居中，起点改为x≈960。这里移动HUD锚点，不改变已经验收的镜头、角色根或身体比例；已实现Auto按身体实际主相机投影选择，支持手动Shoulder／Centered及独立偏移，待游戏标定。
- 继续保留“空／未知可选项隐藏、真实零值保留、不重复展示当前装备、只在有效本机第三人称显示”。v3里的“重0／搬—”是示例，不要求常驻四个占位格。

### 实机图可支持的观察

| 观察 | 对应参考 | 对实现的意义 |
| --- | --- | --- |
| 状态条、弹药、技能／物品被压成一小簇，周围没有巨大面板标题和长表格 | 实机04／06／07，4K03／05 | 先改变信息层级与密度，不能只把当前面板换成橙色 |
| 大号数字明显突出弹药，白色分段条与细橙条各自承担不同语义；橙色焦点也随内容变化 | 实机04／06／07，4K05 | v3的橙底“生命80”是GTFO改编选择；当前默认突出弹药，可配置Health突出生命；这是GTFO适配选项 |
| 整簇在不同瞄准、站姿、蹲姿画面中的位置／倾斜不同 | 实机03／04／05，4K03／05 | 不能从一个静态帧推断固定世界坐标或完整跟随算法；按GTFO实际出图标定位置、轻透视和遮挡 |
| 队友在右下以名字、细状态条及少量图标呈现，通知短小，任务在左上 | 实机07／08 | 保留清晰分区；队友默认常驻与否／End如何控制需与现有独立页规则协调 |
| 升级、菜单与人物浏览不属于普通战斗HUD | 4K01为升级，02为技能界面，04为人物浏览，06为设置界面 | 可参考字体／材质／状态，不能用其大面板和背景压暗作为常驻战斗布局 |

部分4K素材带白色编辑锚点／辅助线，这些不属于要制作的HUD。静态截图不能测出150ms、200ms等时长；参考说明中的动画时间是候选设计参数，不作为原作测量结论。

### 字段与显示边界

| 目标元素 | 当前数据依据 | 约束／待调整 |
| --- | --- | --- |
| 分段生命与感染细线 | `Personal.Health`、`Infection`（Fraction） | GTFO没有同义护甲层，不新增虚构护甲。分段仅是表现，数值不按段取整；超100%的真实数值不丢失 |
| 弹匣／备用与工具资源 | 当前装备`Magazine`、`ReserveRounds`、`SupplyRatio` | Rounds和Fraction分开；没有资源概念的工具不画假百分比，切近战／补给／携带物时换为对应真实信息 |
| 补给／消耗品／重型图标 | `Uses`（次）、`ItemCount`（个）及物品身份 | 无物品隐藏；重型消耗品与任务携带物保持独立槽位；Carry没有数量时不补1 |
| 左上任务、目标距离 | 现有任务标题／正文；独立SceneMarker可给距离 | 任务文字不等于任务定位点，不能把任意Ping的距离标成任务距离；GTFO有原版地图，本目标未要求复制原作圆形雷达 |
| 顶部扫描／交互条 | 可靠`Scene.Scan.Progress`或真实限时交互／使用补给进度 | 仅相应动作实际有进度时显示；扫描比例不能推导剩余秒数。移动／携带本身没有进度条 |
| 右上数据球1/3、钥匙2 | 当前没有通用、结构化且已验证的全局任务物品计数采集 | 保持隐藏或作为后续数据扩展；任务正文里可能有计数文字，不等于已有独立计数字段 |
| 右下队友铭牌 | 现有三名远端队友的身份、生命、感染、倒地、同步资源 | v3可参考小铭牌；默认隐藏，End显示／隐藏右下铭牌；核心簇保留。铭牌仅摘要，不替换完整原版队友信息 |
| 左下事件通知 | 已有受击、资源警告、队友状态／标记等事件 | “拾取+12”需要可靠的物品变更来源；当前没有通用拾取流水事件，不能用静态样本或任意数量差冒充拾取 |
| 准星周围交互提示 | 当前实际画面、交互目标与真实按键／进度 | 不为线框的(960,470)移动真实瞄准点，不改变射线或开火方向 |

### 参数与优先顺序

v3的核心宽252px、10段生命、78／106／56px三块数值板、36×32物品格、色板`#E08A2E／#ECE8E2／#59C8E8／#C8452F`作为讨论起点。脚本中的7–9px中文标签、17px弹匣数字不能直接作为成品最小字号；当前字体与可读范围也不应机械限制新样式。需在1080p实际游戏、暗背景／手电亮区、肩射／换肩／近墙／宽屏下校准。

0.12.0初版默认突出弹药、生命10段、核心252px宽、顶部686px、小板背景0.48、标签11px／正文14px／主数值38px；工具资源仍为百分比，补给为次、消耗品为个、携带物只显示名称／持有状态。为可读性，物品格改为58px高并保留文字；不照搬线框极小字号。数值板仅斜底材质，文字正向。生命真实读数和填充即时更新，允许另画短暂掉血残影，不用缓慢数值动画掩盖当前状态。

实现入口为[HudDivisionDrawing](ThirdPersonHUD/src/Panel/HudDivisionDrawing.cs)、[HudDivisionProjection](ThirdPersonHUD/src/Panel/HudDivisionProjection.cs)、[HudDivisionSettings](ThirdPersonHUD/src/Runtime/HudDivisionSettings.cs)。任务左上、扫描顶部细条、真实倒计时单列、通知左下、队友右下。场景标记保留身份／时效／准星安全区与冲突避让；缺失数据隐藏，不增加虚构雷达、钥匙／数据球计数、拾取流水或扫描秒数。原版倒计时和完整队友信息保留。四种样式共用已验证采集、资格、原生HUD恢复和生命周期；现有`Panel.*`个人参数保留，新样式参数分开保存，不通过重置CFG或并行加载多份HUD切换。样式切换时先恢复原版覆盖、清旧投影／待应用结果，再在新样式实际绘制成功后覆盖对应原版信息。

### 四种样式配置与对照

| 配置 | 当前行为 |
| --- | --- |
| `[Presentation] Style` | `DivisionV3`默认；`FourCorner`四角面板；`Radial`角色环绕；`Compact`恢复0.11.4布局 |
| `[Presentation] CycleStyleKey` | 默认F5；切换顺序DivisionV3→FourCorner→Radial→Compact，仅本次运行有效；None关闭 |
| `[DivisionV3.Layout] Anchor` | Auto默认；Shoulder固定越肩、Centered固定居中 |
| `ShoulderX / CenteredX / Top` | 基准860／960／686；开启FollowBody时追加投影偏移，Top作为相对686的微调 |
| `Width / Scale / OffsetX / OffsetY` | 252／1／0／0；偏移右／下为正 |
| `Emphasis / HealthSegments / TileSlant` | Ammunition／全新20段（旧值保留）／3；Health调整字号权重，斜切作用于整个核心平面 |
| `[DivisionV3.Theme]` | AccentColor／TextColor／TeamColor／DangerColor和BackgroundOpacity |
| `[DivisionV3.Text]` | LabelSize／BodySize／NumberSize；字体仍由Panel.Text.FontName选择 |
| `[Classic.Layout]` | 四角/环绕共用的Scale、Margin、Top、Bottom、RadialScale、RadialOffsetX/Y、FollowBody、WaistHeight、FollowSeconds |
| `[Classic.Text]` | 四角/环绕的LabelSize／BodySize／NumberSize；字体仍由Panel.Text.FontName选择 |
| `[Classic.Theme]` | 四角/环绕的BackgroundOpacity／AccentColor／TextColor／TeamColor／DangerColor |
| Insert / End / Pause | 整体开关／队伍信息或Compact队伍页／恢复原版HUD |

保持有效第三人称默认显示；第一人称、旁观、其他玩家与辅助画面不显示。DivisionV3铭牌默认隐藏，FourCorner/Radial队伍卡片默认显示，End可隐藏；Compact的End进入队伍页。关闭队伍采集时停止对应采集。切换样式先恢复原版，再等待本次样式有效绘制；未展示双枪时不隐藏原版弹药信息。离线预览／替身测试不等于游戏截图或原生Unity验收。

<a id="contracts"></a>
## Contracts的用途

`PreLuna.ThirdPerson.Contracts.dll`是镜头与HUD共用的接口库，包含人物／关卡／相机身份、视角模式与可用性、实际出图相机快照、状态通知接口，以及HUD使用数据的有效性校验。镜头按这些定义提供状态，HUD据此决定何时显示、使用哪个画面，并拒绝过期或不属于当前关卡／本机主画面的数据。

它不负责移动镜头、生成身体模型或绘制HUD，没有独立插件入口、功能开关或联机网络服务。镜头和HUD运行都需要可解析到同一份匹配接口DLL；不能因为它没有独立开关就删除，也无需随每次镜头或微操大师修复重复替换。

<a id="relations"></a>
## 依赖与联动

| 组件 | 插件硬前置声明 | 编译／运行接口依赖 |
| --- | --- | --- |
| ThirdPerson | 无额外插件硬前置声明 | ThirdPerson.Contracts；仍需GTFO及BepInEx IL2CPP环境 |
| ThirdPersonHUD | ThirdPerson | ThirdPerson、ThirdPerson.Contracts |

ThirdPerson通过可选桥与[机器人／摄像头仓](https://github.com/PreLuna/GTFO-Bot-Commands-and-Control)的HeadCamera联动，该桥不是加载器硬前置。相机／接管身份变化需两侧回归；Shared契约变更需同时核验镜头与HUD。同仓分组、编译引用、加载硬前置和可选联动分别判断，不由文件名推导。

<a id="development"></a>
## 开发与排查

2026-11-18 **ThirdPerson 0.6.27 回退 0.6.26 软挂起崩溃风险已构建**。0.6.26 的 `NativeDisplay` 软挂起保留 player/body/weapon 原生对象跨越 Gate/菜单/场景，出现 UnityPlayer `0xc0000005` 崩溃（WER 09:44:29，无托管栈）。0.6.27 恢复 Gate/缺模型/相机不可用路径的 `Release()`，回到 0.6.25 生命周期；保留自动快照过滤（跳过 player/body 未就绪、最多8次）和 `lastSuspendReason`。场景/指针测试通过；已构建、未安装，稳定性/卡顿仍需实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPerson 0.6.26 短暂 Gate 软挂起卡顿修复已构建，因崩溃风险回退**。0.6.25 快照显示卡顿对应 `generation` 递增、`player=0`/`bodyRenderers=0`、`detail=body-skinning-warmup`，而玩家/模型指针不变：`Gate` 的 `Release()` 全量销毁身体，下一帧重建并等待原生蒙皮。0.6.26 改为软挂起，但随后崩溃；0.6.27 回退。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPerson 0.6.25 卡顿自动快照诊断已构建**。新增 `[调试] 卡顿自动快照=false`、`卡顿阈值毫秒=80`、`卡顿冷却秒=2`；在有效本机第三人称中检测帧间隔，超阈值自动调用 F8 快照并记录 `stall.lastFrameMs`、场景名、装备、相机、交互目标/指向数据；默认关闭。用于捕捉 0.15.1 后仍偶发的掉帧。场景/指针测试通过；已构建、未安装，需实机复现时启用。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-19 **ThirdPersonHUD 0.15.2 阶段1b 控制 API/ModControl 接入已构建**。HUD 控制 API 新增 `shortPrompt`/`hideNativePrompt`（默认 false，分别写入 `Scene.ShortPrompt`/`Scene.HideNativePrompt`）；ModControl 0.3.5 的“第三人称HUD”页新增两个开关，运行中即时保存。新增 `HudNativePromptVisibility` 小 seam 和 CanvasGroup 隐藏/恢复回归（不切换 GameObject active）。HUD 场景 61 项、控制状态/ModControl 适配测试通过；未安装，需实机确认 ModControl 页和运行中切换。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPersonHUD 0.15.1 修复原版提示抑制的每帧 SetActive 风险**。0.15.0 每帧对原版 `m_interactPrompt.gameObject` 调用 `SetActive(false)`；若游戏每帧重新激活，会反复触发 UI 激活/Canvas 重建，可能造成掉帧卡顿。0.15.1 改为一次性取得/添加 `CanvasGroup`，只写 `alpha/interactable/blocksRaycasts`，不再切换 GameObject active；恢复时还原保存值。原版提示隐藏语义不变，`HideNativePrompt` 仍默认关闭。场景/指针测试通过；已构建、未安装，仍需实机确认掉帧是否消失。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPersonHUD 0.15.0 阶段1b 原版提示抑制实验已构建**。新增 `[Scene] HideNativePrompt`（默认关闭）：短提示激活时读取 `GuiManager.InteractionLayer.m_interactPrompt`，隐藏其 GameObject；提示失效/关闭/HUD 隐藏时恢复。只改提示对象，不改原版交互；找不到对象时保留原版提示。场景规则/绘制60项与既有回归通过；已构建、未安装，仍需实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPersonHUD 0.14.0 阶段1b 短提示已构建**。新增 [HudScenePromptProjection](ThirdPersonHUD/src/Panel/HudScenePromptProjection.cs)、`HudScenePrompt`、`HudScenePrompt` 绘制；HUD 采集当前交互/补给目标世界点，映射成贴着目标的短提示，位置不可用时回退原交互卡；四种布局共用绘制层。`[Scene] ShortPrompt`默认关闭；原版交互提示仍保留，1b 本版不隐藏原版提示。场景规则/绘制60项通过；已构建、未安装，仍需实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-18 **ThirdPerson 0.6.24 阶段1a 近战眼睛基准获用户实机确认**。用户实测近战命中跟随原版眼睛射线（红点），0.6.24 在持近战 `GearMelee` 时把视角点放到 `fps.Position/Forward` 射线 2.5 m 内首个命中点的投影位置；`[指向] 近战眼睛基准=true`默认开启，可关闭回屏幕中心。枪械/其他装备仍用中心；目标标记和黄色 × 不变。武器指向语义和胶枪两次回退修复见[指向性设计](第三人称指向性设计方案.md#1a-实机结果与武器指向语义)。3 程序集构建通过，PointerProjection 11 项与既有回归通过；已安装并由用户实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-17 **ThirdPerson 0.6.23 阶段1a 视角点与选中目标标记已构建**。新增 [ThirdPersonPointerOverlay](ThirdPerson/src/Runtime/ThirdPersonPointerOverlay.cs) 和 `PointerProjection` 的视口 clamp；有效本机第三人称绘制中心白点，选中交互目标绘制轻量括号，替代旧青色圈。`[指向] 视角点`/`[指向] 目标标记`默认开启；F12 阶段0调试覆盖层开启时由调试标记接管。3 程序集构建通过，PointerProjection 11 项与既有回归通过；未启动游戏、未安装，1a 仍需实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-17 **ThirdPerson 0.6.22 阶段0 指向调试覆盖层与胶枪回退修复已构建**。新增 [PointerProjection](ThirdPerson/src/Core/PointerProjection.cs)、[ThirdPersonPointerDebug](ThirdPerson/src/Runtime/ThirdPersonPointerDebug.cs)、[PointerProjectionTests](ThirdPerson/tests/PointerProjectionTests.cs)，并在 [UnityCameraWorld](ThirdPerson/src/Runtime/UnityCameraWorld.cs) 增加只读 `RayHit`；F12 默认关闭，F8 快照 schema6 新增 `pointer`。0.6.22 修正 `SupportsEquipmentHolding` 把 `GearClass` 胶枪归为消耗品而触发的 `equipment-action` 回退，并为 F9 换肩写日志。3 程序集构建通过，PointerProjection 10 项与既有回归通过；未启动游戏、未安装，F12 覆盖层和胶枪回退仍需实机确认。见[维护摘要](../../维护记录/验收摘要.md)。

2026-11-16 **ThirdPerson 0.6.19 装备阶段已收尾**。胶枪持握和泡沫发射、所测近战持握与动作、四类资源包及所测普通道具已有实机反馈。远光手电的持握及右键回退修正通过复测。各项范围、原版复用办法、F8 排查和仍待验证的动作见[装备实现与验收](第三人称装备实现与验收.md)。

| 修改范围 | 实际入口 |
| --- | --- |
| 阶段1a视角点/目标标记 | [ThirdPersonPointerOverlay](ThirdPerson/src/Runtime/ThirdPersonPointerOverlay.cs)、[PointerProjection](ThirdPerson/src/Core/PointerProjection.cs) |
| 阶段0指向调试 | [ThirdPersonPointerDebug](ThirdPerson/src/Runtime/ThirdPersonPointerDebug.cs)、[PointerProjection](ThirdPerson/src/Core/PointerProjection.cs)、[PointerProjectionTests](ThirdPerson/tests/PointerProjectionTests.cs) |
| 支点跟随与运动采样 | [CameraMotionSolver](ThirdPerson/src/Camera/CameraMotionSolver.cs)、[CameraKinematics](ThirdPerson/src/Camera/CameraKinematics.cs) |
| 观察、输入与交接 | [CameraOrientationSolver](ThirdPerson/src/Camera/CameraOrientationSolver.cs)、[CameraInputAdapter](ThirdPerson/src/Runtime/CameraInputAdapter.cs)、[ThirdPersonObservation](ThirdPerson/src/Runtime/ThirdPersonObservation.cs) |
| 连续构图、反馈与风格合成 | [CameraTransitionSolver](ThirdPerson/src/Camera/CameraTransitionSolver.cs)、[CameraActionFeedbackSolver](ThirdPerson/src/Camera/CameraActionFeedbackSolver.cs)、[CameraStyle](ThirdPerson/src/Camera/CameraStyle.cs) |
| 最终空间约束与恢复 | [SpatialCameraSolver](ThirdPerson/src/Collision/SpatialCameraSolver.cs)、[SpatialCameraRefinement](ThirdPerson/src/Collision/SpatialCameraRefinement.cs)、[UnityCameraWorld](ThirdPerson/src/Runtime/UnityCameraWorld.cs) |
| 身体、持枪与恢复 | [LocalBodyView](ThirdPerson/src/Body/LocalBodyView.cs)、[NativeBodyHolding](ThirdPerson/src/Body/NativeBodyHolding.cs)、[NativeBodyIk](ThirdPerson/src/Body/NativeBodyIk.cs)、[RenderScope](ThirdPerson/src/Runtime/RenderScope.cs) |
| 装备模型来源、复制与握点 | [NativeThirdPersonEquipmentView](ThirdPerson/src/Body/NativeThirdPersonEquipmentView.cs)、[WorldWeaponView](ThirdPerson/src/Body/WorldWeaponView.cs) |
| 模式、配置、生命周期与诊断 | [ThirdPersonRuntime](ThirdPerson/src/Runtime/ThirdPersonRuntime.cs)、[PluginSettings](ThirdPerson/src/Runtime/PluginSettings.cs)、[ThirdPersonCameraStages](ThirdPerson/src/Runtime/ThirdPersonCameraStages.cs) |
| 共享接口／HUD | [Shared源码](Shared/ThirdPerson.Contracts/src)、[ThirdPerson桥](ThirdPerson/src/Bridge)、[HUD源码](ThirdPersonHUD/src) |

当前生产顺序：主线程采样与资格检查→观察／模式协调→支点、构图及反馈合成→最终碰撞与环境恢复→主画面渲染及HUD发布。开火可重新检查几何，但不再次推进输入、惯性、步态、碰撞历史或恢复计时。[Core/ViewContracts.cs](ThirdPerson/src/Core/ViewContracts.cs)是镜头内部数学数据，与Shared公共契约分开维护。

本机身体持续蒙皮更新，主画面通过绘制门控显示，Ghost按原生登记和shader精确排除。支持的2/3枪械复用原生身体动画与独立IK，真实FPS物品、原有IK及网络身份保持原版；换弹暂回第一人称。装备扩展共用模型复制和持握流程，按物品在原版装配与本机手持模型之间选来源。独立的「实验性原生装备方案A」通用挂载开关仍默认关闭。

F8诊断重点：`cameraStages`为速度、当前／目标构图、反馈、空间原因和恢复探测；`observation`为按键、进入／拒绝及移动样本；`lastModelRender`为最近主出图的身体、Ghost、动画／IK与恢复状态。最近主出图是历史样本，必须结合帧号与当前模式判断。现场异常先保留快照、聊天原文、操作顺序、版本和预设，再判定输入、空间、身体或关联MOD归属。

0.6.20起F8快照schema6新增`pointer`（阶段0指向调试：中心、相机/眼睛射线、原版选中、当前交互物、锁候选和像素偏移）；0.6.1引入的schema5 `bodySizeComparison`保留：在渲染作用域外按4个玩家槽读取本机／队友的根、模型与Animator本地／世界缩放、最多8层父链、可用的人形骨段世界长度及模型相对玩家根偏移。`lastModelRender.scaleBefore/scaleDuring`保存最近主出图前／中的数值副本，不保留Unity对象；诊断本身不激活模型、推进动画或更改尺度，缺失骨骼写null。比较时请让角色在平地站定、姿态相近，依次在第三人称普通站立、肩射和F6第一人称各按F8；记录第三人称开关状态并同时保留截图。肩射图与普通探索图分别判断，不能把占屏比例当世界尺寸。当前槽位样本与历史主出图帧号不同，`referenceDepthFromLastMain`只供静止对照；骨段长度不等于服装外轮廓或碰撞体大小。实现入口为[身体尺寸诊断](ThirdPerson/src/Runtime/ThirdPersonBodySizeDiagnostics.cs)与[渲染尺寸副本](ThirdPerson/src/Body/BodyScaleSnapshot.cs)。

0.6.2的[LocalBodyScale](ThirdPerson/src/Body/LocalBodyScale.cs)只拥有独立身体／Animator视觉根上的已观测均匀1.2倍修正。在LateUpdate、蒙皮前准备并跨相机保持稳定，先完成一帧身体预热再显示；不在OnPreCull反复缩放。其他尺度、玩家父级尺度与FPS手臂保持原样；外部写入不同尺度时不回写旧值。换弹、第一人称开镜、F6关闭、失去资格、释放或异常清理沿现有EndPresentation恢复捕获的原始值。F8的`lastModelRender.body.bodyScale`记录修正状态、原值和目标值：本次样本正常第三人称应为1，回第一人称应恢复1.2。第三人称只是把约20%的既有放大去掉，并不保证与任何距离的队友占屏大小相同；约0.6米的原有模型后移本轮不改。配置保留schema9，新增开关缺省开启，不要求删除个人CFG。

开发需要PowerShell 7的C#12编译器、.NET 6 runtime、GTFO/BepInEx core与interop，并将[共用工具仓](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools)放在同级目录，或显式指定BuildToolsRoot。先审查／暂存输入并更新维护锁，再执行实际入口；路径换为本机值。

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.36'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.36'
```

0.6.2修复实施时已实际完成3程序集构建、28组离线回归，身体渲染50项含新增9项尺度与恢复检查；2026-11-05用户随后验收第一／第三人称正常。这些构建与实测证据分开登记，本次文档收尾未重跑构建或测试。

0.13.0已完成3程序集构建与31组离线回归：Classic专项12项、Division专项39项、个人运行74项、配置22项；新增720p／1080p／21:9投影边界检查，Classic预览场景FourCorner 146条、Radial 297条绘制命令均在512条上限内且无屏幕外命令。实际字体interop核验通过，未新增Unity原生调用；离线预览由生产绘制命令生成，未运行游戏，实际遮挡、字号、圆心跟随和不同装备状态仍需实机确认。构建DLL为446,976字节，SHA256见当前交付清单。

0.12.1已完成3程序集构建与30组离线回归，Division专项39项、个人运行72项、配置17项。54组Compact投影／绘制命令与0.12.0逐字节一致，该基线此前已对齐0.11.4。实际GUI矩阵getter/setter、40处值字段引用解析通过，合成函数不调用原生矩阵辅助；字体核验和29方法JIT准备通过。四种装备状态预览使用生产命令与同一变换生成；未运行游戏，位置／可读性仍需实测。

0.12.0已完成3程序集构建与30组离线回归；Division专项29项、个人运行69项、配置15项，54组Compact场景的全部投影／绘制命令与捕获的0.11.4源码逐字节相同。实际字体interop核验与23个方法JIT准备通过，未调用Unity原生方法／未运行游戏。三种装备状态的离线预览由实际绘制命令生成，仍需实测标定。精确DLL、哈希与清理证据见工作区维护摘要。前轮：0.11.4的29组全部通过：游戏采集56项、个人映射／绘制58项、HUD运行34项、个人运行64项；持续前进的时钟回归检出旧比较反例，真实未来输入仍被拒绝。实际程序集的字体／初始化／绘制／采集8个方法JIT准备通过，离线布局预览另作排版核验。新增实际interop核验入口为[Verify-FontInterop.ps1](ThirdPersonHUD/tests/Verify-FontInterop.ps1)，需显式传GameRoot、HudAssembly和OutputPath；不修改游戏引用。实际旧工厂的JIT反例失败、新字体／初始化／绘制方法编译准备通过；这些离线检查不执行Unity原生调用，游戏显示仍待用户验收。此前0.6.0构建3程序集、28组离线回归通过；C3／C4／C5专项分别22／26／21项，配置迁移隔离27项。MicroMaster0.5.17修复时再次通过关联镜头28组、机器人34组及338项生命周期检查。2026-11-01用户验收与这些离线证据分别登记；2026-11-02本轮仅整理文档，不重跑构建或游戏。

数值门槛保留：同路线30／60／120FPS距离差≤0.025米、垂直FOV差≤0.3°、反馈向量差≤0.001米；10000次稳态数学求解托管分配0；单次C4最多20次空间查询，恢复探测加首次求解最多40次。关闭新阶段的行为与实际执行旧源码所得0.5.4／0.5.9基线对照。几何替身、投影估算及数学帧率比较不等于Unity物理、GPU或实际游戏FPS保证。

构建不自动安装，`out`仅为临时产物，不整目录交付。个人CFG、框架和游戏引用不入源码Git。维护、钩子与发布流程见[共用维护](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#maintenance)和[打包说明](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#packaging)；提交、推送、发布按当次授权。

<a id="camera-roadmap"></a>
## 镜头长期设计 TP-CAM v1.2（2026-11-02收尾整理）

目标仍为探索时具有《死亡搁浅》式跟随重量与环境可读性，战斗时具有《全境封锁》式稳定越肩构图。借鉴体验，不宣称复刻未公开算法或原版参数。以下原则与阶段编号继续作为后续讨论基准，本轮只更新文档状态，不改变已验收行为。

| 编号 | 已确定的原则 | 对实现的约束 |
| --- | --- | --- |
| D1 | 镜头位置、观察方向、角色身体朝向、实际移动方向分开表达 | 不再用单一Forward同时暗示四种意图；人物朝向与移动方向可以不同，原地观察和战斗横移分别定义 |
| D2 | 鼠标观察及时响应，跟随重量主要来自支点、构图和身体协调 | 不给所有旋转统一加迟滞；自动回正、身体跟转、瞄准交接有各自速率与条件 |
| D3 | 探索宽松，肩射收紧，第一人称开镜交回原版 | 进入瞄准更利落、退出稍柔和；过渡中仍须明确实际准星与射击方向 |
| D4 | 碰撞是最终位置约束 | 惯性、肩位、预瞄偏移、反馈先合成，再避障；避障之后不再叠加可能穿墙的位移 |
| D5 | 用可读的构图形成风格 | 围绕角色占屏比例、前路／脚下留白、肩位和距离协同调节FOV；额外摇晃与翻滚默认关闭 |
| D6 | 当前已验证的生命周期和原生动作继续作为底座 | 保留持续蒙皮更新、Ghost筛选、原生持枪、换弹／工具回退；不通过反复启停Renderer或重播Animator制造运动感 |
| D7 | 变更必须可分阶段比较和回退 | 各阶段保留前一已验收行为，新增效果可独立禁用；不靠恢复所有个人CFG做对照 |

完整掩体、负重玩法、载具／剧情镜头、全局后处理及冻结移动MOD行为不在镜头路线图范围；HUD布局由[HUD视觉目标](#hud-visual-target)独立维护，FourCorner/Radial不改变镜头、角色根或身体比例。身体重量通过现有动画协调表达，不承诺新增转身动画、根运动或完整第三人称控制器。

| 阶段 | 当前状态 | 后续边界 |
| --- | --- | --- |
| C0 基线与观测 | 0.5.4／0.5.9离线对照与诊断已保留 | 未逐项报告的实机性能矩阵不补写为通过 |
| C1 跟随支点 | 已实现；用户此前通过 | 保留独立水平／根高度过滤、前视与有界偏移 |
| C2 观察与转向 | C2a站定有限绕看已交付并纳入本轮功能反馈 | 移动中自由观察、180°绕看、渐进身体跟转及独立手柄激活为后续扩展 |
| C3 连续缩放与过渡 | 已实现；本轮用户通过 | 保留分通道过渡、当前值中断续接及直接观察响应 |
| C4 空间处理 | 已实现；本轮用户通过 | 保留立即压缩、延迟释放、稳定回退／恢复和最终约束 |
| C5 可选动作与预设 | 已实现；本轮用户通过 | 反馈默认关闭，可独立归零，四预设共享安全／射击流程 |
| C6 综合标定与交付 | 当前实现范围已交付并获用户验收，本轮收尾 | 不推定所有地图／装备／联机组合与定量帧时已覆盖；正式发布另行授权 |

<a id="camera-c1"></a>
### C1：跟随支点

根高度与站蹲眼高分开过滤，水平滞后与按实际位移估算的前视量分别限幅。默认水平响应上限12、根高度响应8秒⁻¹，根高度滞后上限0.12米，前视时间0.1秒、上限0.12米、响应6秒⁻¹。肩射收紧，Stable关闭前视并加快跟随；瞬移、身份变化、长帧及过期样本清状态。默认值为本MOD工程参数，不是参考游戏测量结果。

<a id="camera-c2"></a>
### C2a：站定观察与动作交接

观察资格、默认键位与范围见[按键与配置](#usage)。站定要求原生水平速度小于0.05米／秒、移动轴绝对值不超过0.001；垂直重力仅作诊断。输入作用域捕获原生处理后的角度并恢复yaw／pitch，普通动作前用SetLookDirection与UpdateFlatTransform交接；不额外推进原生后坐力。原生钩子仍受既有GameAssembly哈希与唯一地址保护。

完整C2扩展前需确定观察方向与实际移动方向的规则，分别验证键鼠／手柄、180°反向、近墙瞄准与首发交接；不能只在显示层转身体而不适配真实移动。自动回正当前不启用。

<a id="camera-c3-c5"></a>
### C3～C5：连续构图、空间与动作

| 阶段 | 已实现逻辑与默认值 |
| --- | --- |
| C3 | 实际水平速度1.5～5.5米／秒连续插值探索／奔跑Profile，滤波响应5；距离／肩位／高度／FOV各自过渡；肩射进入响应至少20、退出8秒⁻¹，打断从当前值续接 |
| C4 | 逻辑眼到支点路径、肩位和后向球扫；低顶先压低支点，受阻最多尝试3个更近位置，近位默认0.6米；无安全路径则回原版，空间稳定后从当前眼位重建，不续播旧轨迹 |
| C5 | 按实际行程推进步态与受限水平起停反馈，周期1.4米、响应12；合成向量总幅度受预算限制，肩射立即衰减至15%，静止归零；不施加翻滚、不改原生后坐力 |

默认探索／奔跑／蹲伏／肩射距离为2.25／2.60／1.95／1.35米，垂直FOV为80／84／78／70°；实际构图还受速度、预设、过渡及空间约束。16:9／21:9保持相同垂直构图，宽屏拓展水平视野；F8占屏比例为参考高度投影估计，不能当作角色网格测量。步态按行程推进，不声称与每次原生动画落脚精确同步。

后续修改继续使用“TP-CAM／阶段编号／修改对象”定位，先写明触发场景、目标、输入输出、恢复策略、参数和验收，再改实现。保持同一条生产镜头路径，不另建竞争方案；旧候选日志、版本过程与回退文件从维护者本地只读证据及索引恢复，不作为活动开发入口。

<a id="sources"></a>
## 来源说明

本仓库维护 PreLuna 的构建或整合资料。

保留源文件中的作者与版权声明。部分模组包含第三方派生实现或冻结基线，本说明不替代上游许可证，也不新增再分发授权；本仓库没有统一重授权声明。
