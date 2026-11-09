# GTFO-Third-Person-Camera-and-HUD

GTFO本机第三人称镜头、配套HUD与共享接口库。**HUD0.13.0在DivisionV3/Compact之外新增四角版`FourCorner`与角色环绕`Radial`：`[Presentation] Style`选择启动样式，F5在有效本机第三人称下循环`DivisionV3→FourCorner→Radial→Compact`。DivisionV3保留0.12.1腰胯信息簇，Compact保留0.11.4竖向布局，现有个人CFG和DivisionV3参数不重置。HUD0.13.1增加供PreLuna.ModControl使用的控制API（角色旁面板/个人/队伍/场景/提示区开关及面板缩放/宽度），CFG改为安全行级写入；现有HUD行为不变。ThirdPerson0.6.3新增5字段控制API（镜头距离/右肩偏移/支点高度偏移/肩射距离/左肩），供ModControl安全行级写入。** 第三人称0.6.2身体比例修复已获用户2026-11-05第一／第三人称均正常反馈；MicroMaster0.5.17沿用既有验收。本地验收与正式发布分别管理。

2026-11-09 **ThirdPerson0.6.3少量可写控制API**：新增 `ThirdPersonControlApi`、`ThirdPersonControlState`、`ThirdPersonControlFileStore`，通过 `GetSnapshotJson`/`ValidateJson`/`SetPersistentJson` 供 ModControl 读取/修改镜头距离、右肩偏移、支点高度偏移、肩射距离和左肩。写入期间临时关闭 `Config.SaveOnConfigSet`，安全行级替换后恢复，保留注释/未知键；F9/F10 等内部热键保存会被状态吸收，外部改动返回冲突。新增目标侧存储 3 项、状态 4 项测试；ModControl 侧新增适配器 3 项测试。已构建并安装，已由用户实机确认正常。

2026-11-08 **HUD0.13.1控制API与安全配置写入**：新增 `ThirdPersonHudControlApi`（`GetSnapshotJson`/`ValidateJson`/`SetPersistentJson` 与类型化入口）、`HudConfigFileStore` 和 `HudControlState`。ModControl 可通过类型名隔离的 JSON 桥读取/修改五个本地视觉开关及面板缩放/宽度，不静态引用 HUD 程序集。写入只更新受管段/键，保留注释、未知键和其他 HUD 设置；文件在打开后被外部修改时拒绝覆盖并返回冲突。HUD 自身的 `Config.SaveOnConfigSet` 在初始化后关闭，避免整文件重写。新增 `HudConfigFileStoreTests` 4 项、`HudControlStateTests` 5 项，HUD 测试共 33 组通过；未运行游戏，页面/控件与真实 CFG 写入已由用户实机确认正常。控制 API 为可选能力，未安装 ModControl 时 HUD 仍按原配置独立运行。

2026-11-05 **ThirdPerson0.6.2身体比例修复已获用户验收**：0.6.1实机快照确认本机视觉骨架为1.2倍、队友为1倍，普通第三人称出图前／中保持1.2倍，F6关闭后本机仍为1.2倍。0.6.2在第三人称身体准备阶段校正已观测的均匀1.2倍视觉根，回第一人称或结束身体显示时恢复原值；保留镜头参数和玩家根。用户随后确认第一人称与第三人称均正常，现装哈希与0.6.2交付一致。身体比例修复沿用当前安装；本轮仅升级HUD，不要求重置CFG。未逐项报告的装备、地图、联机组合和量化性能不推定全部覆盖。

2026-11-06 **HUD0.13.0新增两套可切换布局**：`FourCorner`按v1四角线框重做任务、队伍、个人状态和装备四个屏幕边角面板；`Radial`按v2角色环绕线框绘制腰部生命/感染弧、肩线队伍标签、角色左侧任务标签和脚下装备条，圆心跟随当前主相机的角色腰部投影。两者只复用已验收的个人、队伍、场景数据与有效性规则，不伪造任务距离、拾取流水、扫描秒数或搬运进度。`DivisionV3`与`Compact`绘制路径未改；本版未运行游戏，仍需实机确认字体、遮挡、不同分辨率与手柄场景。

[组件与安装](#modules) · [按键与配置](#usage) · [HUD视觉目标](#hud-visual-target) · [Contracts用途](#contracts) · [依赖与联动](#relations) · [开发与排查](#development) · [镜头长期方案](#camera-roadmap) · [来源说明](#sources)

<a id="modules"></a>
## 组件与安装

| 组件 | 当前版本／接口 | 修改入口 | 职责 |
| --- | --- | --- | --- |
| `ThirdPerson` | 0.6.3（5字段可写控制API；待实测） | [ThirdPerson/src](ThirdPerson/src)、[tests](ThirdPerson/tests) | 镜头跟随、观察、肩射、碰撞与身体显示；提供 ModControl 控制API |
| `ThirdPersonHUD` | 0.13.1（0.13.0四角/环绕＋ModControl控制API；待实测） | [ThirdPersonHUD/src](ThirdPersonHUD/src)、[tests](ThirdPersonHUD/tests) | 读取有效视角状态并在四种布局间切换绘制HUD；提供面板/个人/队伍/场景/提示区控制API |
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

现装镜头0.6.2已通过比例修复验收，SHA256为`75D3C4FF20F1F07D0F3F0473903EB1E82EFF4E389A714A93DEBB39F118203287`，无需再次替换镜头；本次HUD升级按上方0.13.0说明单独替换。Contracts和微操大师沿用现装匹配版本，无新增依赖或资源。重进关卡残缺身体的关联修复位于MicroMaster0.5.17；不要用旧微操大师替换已验收组合。

普通玩家使用[Releases](https://github.com/PreLuna/GTFO-Third-Person-Camera-and-HUD/releases)中实际发布的安装包及其版本说明。当前本地验收不表示对应版本已发布；Source code／Code ZIP不是安装包。增量更新先退出游戏，按当次清单只替换指定文件，避免重复加载；个人CFG不随包分发。

<a id="usage"></a>
## 按键与配置

以下为镜头默认键位；个人CFG有自定义时以个人值为准。只在可操作的本机角色及合适焦点下生效。

| 操作 | 默认按键／方式 |
| --- | --- |
| 开关第三人称 | F6；全新配置默认关闭，已有启动状态保留 |
| 恢复镜头默认参数 | F7；会关闭第三人称并重置个人镜头设置，不用于日常对照 |
| 保存诊断快照 | F8；输出至`BepInEx/ThirdPerson-P5-诊断` |
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

| 修改范围 | 实际入口 |
| --- | --- |
| 支点跟随与运动采样 | [CameraMotionSolver](ThirdPerson/src/Camera/CameraMotionSolver.cs)、[CameraKinematics](ThirdPerson/src/Camera/CameraKinematics.cs) |
| 观察、输入与交接 | [CameraOrientationSolver](ThirdPerson/src/Camera/CameraOrientationSolver.cs)、[CameraInputAdapter](ThirdPerson/src/Runtime/CameraInputAdapter.cs)、[ThirdPersonObservation](ThirdPerson/src/Runtime/ThirdPersonObservation.cs) |
| 连续构图、反馈与风格合成 | [CameraTransitionSolver](ThirdPerson/src/Camera/CameraTransitionSolver.cs)、[CameraActionFeedbackSolver](ThirdPerson/src/Camera/CameraActionFeedbackSolver.cs)、[CameraStyle](ThirdPerson/src/Camera/CameraStyle.cs) |
| 最终空间约束与恢复 | [SpatialCameraSolver](ThirdPerson/src/Collision/SpatialCameraSolver.cs)、[SpatialCameraRefinement](ThirdPerson/src/Collision/SpatialCameraRefinement.cs)、[UnityCameraWorld](ThirdPerson/src/Runtime/UnityCameraWorld.cs) |
| 身体、持枪与恢复 | [LocalBodyView](ThirdPerson/src/Body/LocalBodyView.cs)、[NativeBodyHolding](ThirdPerson/src/Body/NativeBodyHolding.cs)、[NativeBodyIk](ThirdPerson/src/Body/NativeBodyIk.cs)、[RenderScope](ThirdPerson/src/Runtime/RenderScope.cs) |
| 模式、配置、生命周期与诊断 | [ThirdPersonRuntime](ThirdPerson/src/Runtime/ThirdPersonRuntime.cs)、[PluginSettings](ThirdPerson/src/Runtime/PluginSettings.cs)、[ThirdPersonCameraStages](ThirdPerson/src/Runtime/ThirdPersonCameraStages.cs) |
| 共享接口／HUD | [Shared源码](Shared/ThirdPerson.Contracts/src)、[ThirdPerson桥](ThirdPerson/src/Bridge)、[HUD源码](ThirdPersonHUD/src) |

当前生产顺序：主线程采样与资格检查→观察／模式协调→支点、构图及反馈合成→最终碰撞与环境恢复→主画面渲染及HUD发布。开火可重新检查几何，但不再次推进输入、惯性、步态、碰撞历史或恢复计时。[Core/ViewContracts.cs](ThirdPerson/src/Core/ViewContracts.cs)是镜头内部数学数据，与Shared公共契约分开维护。

本机身体持续蒙皮更新，主画面通过绘制门控显示，Ghost按原生登记和shader精确排除。支持的枪械复用原生身体动画与独立IK，真实FPS物品、原有IK及网络身份保持原版；换弹暂回第一人称，工具／近战沿既有回退。异常／失去身份／离关时恢复原状态，不通过反复激活Renderer、重播动画或移动玩家坐标制造镜头重量。

F8诊断重点：`cameraStages`为速度、当前／目标构图、反馈、空间原因和恢复探测；`observation`为按键、进入／拒绝及移动样本；`lastModelRender`为最近主出图的身体、Ghost、动画／IK与恢复状态。最近主出图是历史样本，必须结合帧号与当前模式判断。现场异常先保留快照、聊天原文、操作顺序、版本和预设，再判定输入、空间、身体或关联MOD归属。

0.6.1起F8快照schema5新增`bodySizeComparison`：在渲染作用域外按4个玩家槽读取本机／队友的根、模型与Animator本地／世界缩放、最多8层父链、可用的人形骨段世界长度及模型相对玩家根偏移。`lastModelRender.scaleBefore/scaleDuring`保存最近主出图前／中的数值副本，不保留Unity对象；诊断本身不激活模型、推进动画或更改尺度，缺失骨骼写null。比较时请让角色在平地站定、姿态相近，依次在第三人称普通站立、肩射和F6第一人称各按F8；记录第三人称开关状态并同时保留截图。肩射图与普通探索图分别判断，不能把占屏比例当世界尺寸。当前槽位样本与历史主出图帧号不同，`referenceDepthFromLastMain`只供静止对照；骨段长度不等于服装外轮廓或碰撞体大小。实现入口为[身体尺寸诊断](ThirdPerson/src/Runtime/ThirdPersonBodySizeDiagnostics.cs)与[渲染尺寸副本](ThirdPerson/src/Body/BodyScaleSnapshot.cs)。

0.6.2的[LocalBodyScale](ThirdPerson/src/Body/LocalBodyScale.cs)只拥有独立身体／Animator视觉根上的已观测均匀1.2倍修正。在LateUpdate、蒙皮前准备并跨相机保持稳定，先完成一帧身体预热再显示；不在OnPreCull反复缩放。其他尺度、玩家父级尺度与FPS手臂保持原样；外部写入不同尺度时不回写旧值。换弹、第一人称开镜、F6关闭、失去资格、释放或异常清理沿现有EndPresentation恢复捕获的原始值。F8的`lastModelRender.body.bodyScale`记录修正状态、原值和目标值：本次样本正常第三人称应为1，回第一人称应恢复1.2。第三人称只是把约20%的既有放大去掉，并不保证与任何距离的队友占屏大小相同；约0.6米的原有模型后移本轮不改。配置保留schema9，新增开关缺省开启，不要求删除个人CFG。

开发需要PowerShell 7的C#12编译器、.NET 6 runtime、GTFO/BepInEx core与interop，并将[共用工具仓](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools)放在同级目录，或显式指定BuildToolsRoot。先审查／暂存输入并更新维护锁，再执行实际入口；路径换为本机值。

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
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
