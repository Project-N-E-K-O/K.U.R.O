# 地面检测、坠落与移动平台

## 当前结构

```text
黄区 / 红区 / 绿色承托
          ↓
GroundContactSensor → FallPhysics（统一空间查询、替身生成）
    ├─ FallController
    │    ├─ PlayerFallController → 安全记录、Fall 状态、回岸、扣血
    │    └─ EnemyFallController → 状态/攻击免疫、Fall 状态、死亡与掉落
    └─ GroundObjectController → 物品、武器、地面特效

FallMovingPlatform → 唯一承载位移入口
    ├─ 玩家/敌人：MoveAndCollide
    ├─ IPlatformPassenger 物品：扫掠及根/内部刚体同步
    └─ 固定附着特效：GroundObjectController.ApplyPlatformDisplacement
```

GroundContactSensor 是运行时内部子组件，不要求手工挂载。注册表仅用于按对象查找传感器，不遍历全场。相同物理帧、相同地面坐标复用查询结果；移动后重新查询。结果包含黄区、深坑、承托是否有效、选中的承托区域及移动平台。多个平台重叠时确定性选择实例 ID 较小者。

FallDetectionGate 负责黄色区域激活、离开休眠及出生/传送/动态区域加载后的补查。玩家、敌人、物品和特效统一自动创建只监听黄区的 Area2D 脚底探针，碰撞层为 0、Monitorable=false，不影响攻击和身体碰撞，无需手工添加节点。远处探针每物理帧只同步地面坐标，不做精确红绿查询。黄色范围需完整覆盖红区并留出速度缓冲；由身体边缘进入改为脚底进入激活，关卡应保留足够外围距离。缺少黄色区域时，直接出生红区仍有补查兜底。

## 统一组件配置

GroundEffectController 已更名为 GroundObjectController，保留脚本 UID、已有导出字段和 Marker 引用。ItemFallController 中间实现已移除。物品采用显式挂载：两种 WorldItem 实体不再自动添加组件。需要地面功能的场景必须预挂 GroundObjectController，可使用同名或自定义名称。无组件时不参与坠落或平台承载。

| 字段 | 含义 |
| --- | --- |
| TargetPath | 相对于组件的目标 Node2D，默认父节点 |
| GroundAnchor | 显式地面 Marker，优先级最高 |
| GroundOffset | 地面查询的世界偏移 |
| FollowPlatformEnabled | 默认 true；允许承载/附着 |
| AutoBindOnFirstPhysicsFrame | 默认 true；特效只自动尝试一次绑定 |
| DestroyOnPlatformLost | 默认 false；固定附着平台丢失后，开启则直接释放目标，不播放坠落替身 |
| FallEnabled | 默认 true，沿用重构前工作区当前默认值；控制是否参与坠落 |
| Grounded | 默认 true；普通特效飞行期间需设 false，落地后设 true 并刷新；物品还会自动检查自身生命周期 |
| FallEffectScene | 可选独立替身，为空直接释放目标 |
| FallAnimationName | 默认 fall，支持 DirectionalSceneEffect 与根下 AnimationPlayer |
| FallEffectOffset | 替身相对地面检测点的世界偏移 |

无显式 Marker 时：物品读取 IGroundFallReceiver.FallGroundPosition，其他承载对象读取 IPlatformPassenger.PlatformGroundPosition，普通特效退回目标根坐标。两个 WorldItem 类均令 FallGroundPosition 复用 PlatformGroundPosition，所以其 PlatformGroundProbePath 同时影响两种功能。

普通特效落地、传送或从休眠状态启用功能后调用 RefreshGroundDetection()；默认显式尝试平台绑定。传 false 可只请求地面补查。目标应在最终位置设置完成后调用。组件不支持运行时直接更换 TargetPath 后自动迁移传感器；需重新创建组件。

## 承载与附着

玩家、敌人和 world_items 由平台按现有分组筛选，要求实际脚下承托属于当前平台。物品的碰撞、内部刚体同步继续由自身 IPlatformPassenger 实现。GroundObjectController 不再对物品另加一条固定附着路径，避免重复携带。平台的分类开关和排除组仍然有效，物品组件的 FollowPlatformEnabled/Grounded 也会过滤承载。

普通特效通过组件登记到平台本地附着集合。平台统一执行实际位移并按 PlatformTarget 去重；组件只实施整体平移，已继承父平台变换时不再叠加。不再订阅 WorldDisplaced 自行移动。固定地面特效不会被之后经过的平台吸附；可显式重新绑定。固定附着平台失效时解除绑定、刷新地面，在深坑无承托时坠落；不会自动吸附其他平台。

活体角色仍要求至少一个实际身体形状有效；禁用身体的 Boss 不承载。Dying 尸体允许身体碰撞停用，但必须具备启用的 FallController、初始化时有有效身体形状、可见且尚未坠落。尸体直接按平台世界位移平移。无身体物品不受此限制。CanRidePlatform 与 CanFall 独立：滑行物品不携带但仍坠落；家具和投掷武器的飞行、回弹、散落未稳及等待归还阶段均排除地面坠落。已拾取或正在销毁的对象不重复触发。

## 坠落结算

Dying 使用同一脚底探针持续检测，不需要切换检测模式。GameActor.DisableCollisionShape 跳过 GroundContactSensor 子树，避免死亡停用身体时误关探针。尸体失去绿色承托并踩入红区，复用自身 FallEffectScene / FallAnimationName / FallEffectOffset 生成独立替身，隐藏本体；不进入活体 Fall 状态，不额外扣血，也不重复发死亡事件。普通敌人继续既有 Dying 计时，死亡帧特效及 Dead 收尸特效停止生成；DeathDuration<=0 的持续尸体落坑后调用统一 FinalizeDeath。掉落基准改为通过验证的岸边兜底点，无点不在坑内生成掉落。玩家保留 Dying / Dead / GameOver 流程，不回岸、不直接释放玩家；未来复活完成后调用 RestoreVisibilityAfterRevive 恢复隐藏和碰撞。

2026-11-05：统一探针与尸体功能通过 22 项引擎回归检查，覆盖黄区唤醒/休眠、身体停用、平台承载、普通和持续尸体单次结算、玩家复活接口、活体敌人和地面特效旧流程、直接出生红区与动态区域加载/卸载。未进行性能基准测试。

有效绿色承托优先于红区。玩家的安全采样先于免疫判断，保留 Dash 或攻击免疫期间的岸边记录。玩家回岸、致死隐藏及复活可见性接口不迁入通用组件；敌人仍执行自己的死亡和安全掉落逻辑。

物品通过 IGroundFallReceiver 提供 CanFall、FallGroundPosition、PrepareForFall。掉入深坑时立即禁止拾取、关闭物理和伤害，不走普通 Destroy/RequestDestroy，不触发投掷爆炸、打碎广播或背包归还。已处于等待归还阶段的投掷武器被排除，继续原归还链。

地面特效触发一次 FallStarted，可用于取消目标子树之外的定时任务/派生攻击。随后停用本体子树、关闭碰撞并释放。独立替身生成在世界下，不受本体释放影响；按区域 FallDuration 播放和回收。组件不自动清除已生成到外部世界的其他攻击。

## 验证

验证涵盖玩家完整坠落/致死隐藏流程、敌人攻击阶段免疫/死亡/掉落、11 种敌人的真实自动坠落、7 种静置投掷武器、无身体物品的黄区唤醒与休眠、滑行入坑、家具内部刚体偏移、承托失效、特效独立替身，以及原平台承载与特效跟随回归。

已知原有无窗口诊断：部分物品材质出现 custom_samplers 编译诊断；DotBleedEffect 清理时将粒子数设置为 0。它们不属于本次地面逻辑修改，保留现有行为。自动测试临时场景、专用脚本与日志在验证后删除，手动 PlayerFallTest.tscn 保留。

## 2026-11-05：投掷落地的深坑优先处理

抛物线落地、回弹停止和不回弹的飞行停止分支，在正常落地攻击/隐藏流程前调用 TryFallNow。飞行中仍免于地面检测；确定落地后不再因 IsInThrowLifecycle 而排除坠落。绿色承托、FallEnabled 和 Grounded 开关仍有效。

深坑落地取消本次 LandingHideDelay，立即生成坠落替身并隐藏本体，不触发正常 OnThrowDestroy 落地攻击。一次性副本按原通用坠落方式释放；需要归还的投掷武器通过 IGroundFallReceiver.CompleteFall 接管收尾：停用子树，仅根节点继续原归还倒计时，到期调用原 ReturnToInventory 并释放。不提前归还，不改变 LandingHideDelay 资源配置。普通地面及绿色平台上的落地维持原流程。

10 项运行断言通过，覆盖飞行排除、立即坠落、隐藏后停用、归还冷却保留及到期收尾、一次性副本、普通地面、关闭坠落与绿色平台承托。测试中仍出现既有 headless 材质 custom_samplers 诊断。
## 2026-11-05：物品组件显式挂载迁移

64 个家具、通用物品及武器场景均已显式挂载一个 GroundObjectController。新增 44 个家具/通用物品挂载，保留 20 个武器现有组件和对应动画配置。Animation_item 当前无家具专属动画，因此家具及通用物品暂不配置替身，掉坑直接释放；可在各自场景中独立指定动画。

移除两种 WorldItem 基类的 AttachTo 调用及自动添加方法。FallMovingPlatform 对物品要求存在有效 GroundObjectController，且组件允许跟随、Grounded 为 true、未处于坠落。物品自身 PlatformCarryEnabled、平台 CarryWorldItems 和排除组仍有效。只删除组件即可同时退出这两类地面功能；投掷副本继承源场景配置。

92 项断言通过：64 场景组件唯一性、20 武器动画存在性，以及床的有/无组件承载与坠落、无自动补挂、emojiBomb 配置保留、自定义节点名、无组件武器不生成检测器。现有 EmojiBoomEffect 在预热实例退出时尝试向正在初始化的父节点生成子特效，仍会产生 busy add_child 诊断，本次未改动该特效逻辑。

## 2026-11-05：通用物品坠落替身

Animation_item 已精简为 7 节点、fall/RESET 两条动画。旧 FallAnimationName 保留为外观键，生成前解析共享外观表；可通过 GroundObjectController.FallVisualProfile 指定独立外观资源。配置与验证见 [物品坠落外观说明](ITEM_FALL_VISUAL_USAGE.md)。

## JarAssistAttack 导航圆形采样（2026-11-05）

Cannon 和 MachineGun 不再使用固定 Marker 决定协助罐子落点。以 Active 生成时敌人根节点世界位置 + SpawnCenterOffset 为圆心，在 SpawnRadius 内按面积均匀随机采样。使用该敌人 NavigationAgent2D 的导航地图与导航层，要求候选点位于启用的导航面内，且从敌人投影到导航面的位置能够寻路到达。

| 字段 | 默认值与作用 |
| --- | --- |
| SpawnRadius | 1800，世界单位，不随敌人缩放 |
| SpawnCenterOffset | (0,0)，圆心相对敌人根的世界偏移 |
| NavigationAgentPath | NavigationAgent2D，相对敌人根解析 |
| SpawnSampleAttempts | 48，单次最多重试，运行时限制 1~256 |
| SpawnGroundOffset | (0,0)，根节点到地面点的世界偏移；生成原点 = 采样地面点 - 此偏移 |

不再查询 Fall 红区或绿色承托，以烘焙导航为唯一地形依据。绿色平台若未纳入可达导航面，也不会成为落点。导航烘焙必须正确排除深坑，并及时更新动态变化。

罐子的 entry SpawnMarkerPaths、SpawnMarkerSelect、EffectOffset 不再控制位置；其他攻击/特效的生成规则不变。两个炮台场景的罐子 entry 旧 Marker 配置已清理。采样采用拒绝式验证（1 世界单位导航容差），不会将无效候选吸附到导航边缘。

缺少导航代理、导航尚未就绪、导航层为空，或所有重试失败时，跳过生成，不启动协助计时、不误判成功或失败；攻击动作及 CD 仍按原逻辑结束。没有预先采样/缓存位置，避免从排队到 Active 期间敌人位置改变后落点过时。

382 项运行断言通过，覆盖两个实际炮台模板、多次随机采样、圆形边界、导航洞、导航层、不连通孤岛、空地图、世界偏移与旧 Marker 清除，并验证不再叠加 Fall 查询。
# 平台导航与运行时烘焙补充（2026-11-05）

移动平台的独立 NavigationRegion2D 使用 `navigation_rebake_excluded` 组，由 NavigationRebakeCoordinator 跳过家具触发的全场重烘焙。普通地面导航继续重烘焙。平台 NavigationPolygon 使用局部子树作为几何来源，避免全场深坑障碍把平台导航面裁空；移动时由节点变换更新位置，不需重烘焙。

Test_begin 已验证原问题为多边形被清空而非节点被删除；修复后重复全场烘焙、平台移动和手动局部烘焙均通过。该修复处理导航面消失问题，不改变敌人追击的无路径回退逻辑。

