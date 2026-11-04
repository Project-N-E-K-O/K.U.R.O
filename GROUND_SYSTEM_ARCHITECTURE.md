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

FallDetectionGate 负责黄色区域激活、离开休眠及出生/传送/动态区域加载后的补查。玩家和敌人使用身体进入信号；物品与特效创建只监听黄区的轻量 Area 探针，碰撞层为 0，不影响攻击和身体碰撞。远处探针每物理帧只同步地面坐标，不做精确红绿查询。黄色范围需完整覆盖红区并留出速度缓冲；缺少黄色区域时，直接出生红区仍有补查兜底。

## 统一组件配置

GroundEffectController 已更名为 GroundObjectController，保留脚本 UID、已有导出字段和 Marker 引用。ItemFallController 中间实现已移除。两种 WorldItem 实体在 Ready 中自动添加 GroundObjectController；若直接子节点已预挂该组件，则保留其配置，不重复添加。需要在 Inspector 调整物品时，可自行预挂同名或自定义名称的组件。

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

角色仍要求至少一个实际身体形状有效；禁用身体的 Boss 不承载。无身体物品不受此限制。CanRidePlatform 与 CanFall 独立：滑行物品不携带但仍坠落；家具和投掷武器的飞行、回弹、散落未稳及等待归还阶段均排除地面坠落。已拾取或正在销毁的对象不重复触发。

## 坠落结算

有效绿色承托优先于红区。玩家的安全采样先于免疫判断，保留 Dash 或攻击免疫期间的岸边记录。玩家回岸、致死隐藏及复活可见性接口不迁入通用组件；敌人仍执行自己的死亡和安全掉落逻辑。

物品通过 IGroundFallReceiver 提供 CanFall、FallGroundPosition、PrepareForFall。掉入深坑时立即禁止拾取、关闭物理和伤害，不走普通 Destroy/RequestDestroy，不触发投掷爆炸、打碎广播或背包归还。已处于等待归还阶段的投掷武器被排除，继续原归还链。

地面特效触发一次 FallStarted，可用于取消目标子树之外的定时任务/派生攻击。随后停用本体子树、关闭碰撞并释放。独立替身生成在世界下，不受本体释放影响；按区域 FallDuration 播放和回收。组件不自动清除已生成到外部世界的其他攻击。

## 验证

验证涵盖玩家完整坠落/致死隐藏流程、敌人攻击阶段免疫/死亡/掉落、11 种敌人的真实自动坠落、7 种静置投掷武器、无身体物品的黄区唤醒与休眠、滑行入坑、家具内部刚体偏移、承托失效、特效独立替身，以及原平台承载与特效跟随回归。

已知原有无窗口诊断：部分物品材质出现 custom_samplers 编译诊断；DotBleedEffect 清理时将粒子数设置为 0。它们不属于本次地面逻辑修改，保留现有行为。自动测试临时场景、专用脚本与日志在验证后删除，手动 PlayerFallTest.tscn 保留。
