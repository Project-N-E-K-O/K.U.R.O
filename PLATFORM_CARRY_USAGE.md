# 移动平台承载配置

当前完整架构见 [地面系统架构](GROUND_SYSTEM_ARCHITECTURE.md)。玩家、敌人共用 FallController；物品、武器、地面特效统一使用 GroundObjectController。

## 平台职责

FallMovingPlatform 在起点和 TravelOffset 之间平移。所有承载位移由平台统一分发，按实际目标 PlatformTarget 每物理帧去重。平台既处理脚下站立者，也处理已登记的固定附着特效。

| 参数 | 含义 |
| --- | --- |
| TravelOffset / Speed | 往返终点偏移与速度 |
| SupportPath | 平台的绿色 FallSupportArea |
| CarryPlayers / CarryEnemies / CarryWorldItems | 分类承载开关，默认全部开启 |
| ExcludedGroup | 默认 platform_carry_excluded，明确排除指定对象 |
| FurnitureObstacleMask | 家具身体扫掠阻挡层，默认 7 |

player / enemies 需要存活、可处理且实际身体形状有效，使用 MoveAndCollide。不把 AttackArea / HitArea 的形状当作身体，禁用身体的 Boss 不会被平台携带。

world_items 通过 IPlatformPassenger 提供 CanRidePlatform、PlatformGroundPosition、PlatformTarget 和 ApplyPlatformDisplacement。分组本身不足以判断飞行状态。

## 物品和武器

WorldItemEntity 与 RigidBodyWorldItemEntity 自动添加 GroundObjectController；预先挂载组件可以覆盖配置，不会重复添加。无需逐个修改家具或武器场景。

- PlatformCarryEnabled：默认 true，控制物品自身承载资格。
- PlatformGroundProbePath：可选地面节点，同时作为默认坠落地面位置。
- WorldItemEntity：没有自身滑行速度、未拾取、可见且有效时允许携带。无身体时整体平移，有身体时沿用身体碰撞移动。
- RigidBodyWorldItemEntity：非投掷、回弹、隐藏归还、散落未稳阶段，且内部刚体冻结时允许携带。实际 StaticBody 形状优先作为地面点，其次内部刚体；位移扫掠后同步包装根与内部刚体。
- GroundObjectController 的 FollowPlatformEnabled、Grounded 也会过滤物品承载，且不受组件节点名称影响。
- 物品不会额外登记为固定附着对象，避免与平台按 world_items 携带的路径叠加。
- CanFall 与 CanRidePlatform 独立：滑行物品仍会坠落；关闭平台承载不等于免坠落。

## 地面特效

将 GroundObjectController 作为 Node 子节点挂在需要处理的特效下。原 GroundEffectController / PlatformEffectFollower 场景挂载已迁移。

| 字段 | 含义 |
| --- | --- |
| TargetPath | 组件相对路径，默认 ..，指定实际控制的 Node2D |
| GroundAnchor / GroundOffset | 地面 Marker 与世界偏移；平台跟随、坠落使用同一点 |
| FollowPlatformEnabled | 默认 true |
| AutoBindOnFirstPhysicsFrame | 默认 true；只自动尝试一次固定附着 |
| DestroyOnPlatformLost | 默认 false；开启时平台丢失直接销毁，不播放替身 |
| FallEnabled | 默认 true，沿用重构前工作区当前值；可单独关闭 |
| Grounded | 默认 true，普通特效飞行阶段需关闭 |
| FallEffectScene / FallAnimationName / FallEffectOffset | 可选独立坠落替身、动画名与相对地面点的世界偏移 |

FireWallA/B 保留对应 Marker 引用。GroundAnchor 不改变 TargetPath，只改变地面检测位置。平台位移仍作用于完整 Target，包括视觉和伤害区域。

特效首次绑定后保持附着，不因锚点越过平台边缘反复脱离，也不会被后来路过的平台自动吸走。通过 TryBindToPlatformBelow() 显式重绑，通过 Detach() 解除绑定。平台已是目标祖先且目标继承其变换时，不额外叠加位移。多个组件共享实际目标去重。

Grounded=false 时暂停承载与坠落；落地设置 true 并调用 RefreshGroundDetection()。运行时从休眠状态开启功能或外部传送后同样主动刷新。该方法默认尝试绑定；传 false 只刷新地面检测。TargetPath 运行时更换需重建组件。

## 检测与坠落

GroundContactSensor / FallDetectionGate 通过黄色范围激活精确检测；物品与特效使用仅监听黄色层的无身体探针，远处只同步坐标。出生、传送和动态黄区加载会补查。相同帧、相同脚底坐标复用查询结果。

无有效绿色承托且脚底在红区时，组件请求坠落。物品通过 IGroundFallReceiver 拒绝飞行、回弹、等待归还等阶段；普通特效由 Grounded 控制。坠落停用本体处理与碰撞并释放，配置替身时在世界独立播放并按区域 FallDuration 回收。

FallStarted 信号可用于取消外部任务。物品掉坑不触发普通爆炸或归还链；特效已生成到外部世界的攻击不自动清理。平台失效后默认解除附着并重新检查地面，不自动绑定其他平台。

仅支持平台平移。目标若另有绝对位置 Tween、TopLevel 子节点或世界空间粒子，需要协调自身位移方式。

## 验证

2026-11-04：平台承载 51 项、特效跟随 20 项、玩家 Fall 44 项、敌人 Fall 32 项、统一对象接入 45 项断言通过，共 192 项。覆盖全部 11 种已接入敌人和 7 种投掷武器。

部分既有材质在 headless 下有 custom_samplers 诊断，既有 DotBleedEffect 清理存在粒子数量设为 0 的报错；与此次逻辑无关，未修改这些效果。临时自动测试场景、专用脚本与日志在验证后清理，保留手动 PlayerFallTest.tscn。