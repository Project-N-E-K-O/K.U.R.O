# 移动平台承载配置

## 对象筛选

`FallMovingPlatform` 按现有组筛选，不需要给每个角色或家具额外挂载组件：

| 组 | 条件与位移方式 |
| --- | --- |
| player | GameActor 存活、可处理、身体形状启用且未 Fall；MoveAndCollide |
| enemies | GameActor 存活、可处理、至少一个实际身体形状启用；MoveAndCollide |
| world_items | RigidBodyWorldItemEntity 的地面静置家具/投掷武器，以及 WorldItemEntity 的静置武器/物品；由各自接口处理位移 |

仅组标签不足以判断飞行状态。玩家出手后，常规家具副本仍在 world_items 组内；ApplyThrowImpulse 让同一实例进入飞行，而非统一替换为独立特效。只有 SpawnEffectOnThrow 特殊分支立即销毁本体并生成效果。

刚体包装物品（家具或投掷武器）承载要求：可见、未拾取、未销毁、不在飞行/回弹/隐藏归还阶段，不在散落未停稳阶段，内部刚体冻结。不要求 IsFurniture，也不要求存在 StaticBody2D。拾取后实例销毁，手持图标不参与承载。

敌人的身体检查只检查该 PhysicsBody2D 的 ShapeOwner，忽略 AttackArea、HitArea 等子区域。因此 rogueAI、cannon、machinegun、netadmin 等对象在实际身体形状全部禁用时自动排除，不按类名写死名单。

## 平台 Inspector

- `CarryPlayers` / `CarryEnemies` / `CarryWorldItems`：默认全部开启，按类别关闭。
- `ExcludedGroup`：默认 `platform_carry_excluded`；加入此组可明确排除，即使碰撞启用也不携带。
- `FurnitureObstacleMask`：家具扫掠检测层，默认 Environment + Enemy + Player（7）。
- `SupportPath`：本平台绿色 FallSupportArea。乘客脚底在其内，且 SupportEnabled 为 true，才参与承载。

原有平台实例不用修改即可使用默认配置。未移动的平台不占用乘客，同一对象每物理帧最多被一个移动平台携带一次；重叠时按平台执行顺序先到先得。

## 家具与投掷武器 Inspector

`RigidBodyWorldItemEntity` 增加两个可选配置：

- `PlatformCarryEnabled`：默认 true，可单件关闭。
- `PlatformGroundProbePath`：相对于物品根节点的可选脚底 Marker。留空时优先使用 StaticBody2D 的启用身体形状原点，其次使用内部刚体的启用身体形状原点；没有身体形状则使用内部刚体世界位置。

沿用已有 RigidBodyPath，无需逐个修改 Furniture 或 Weapon_Throw 场景。物品自己的 ApplyPlatformDisplacement 接口同时移动包装根节点与冻结刚体，保留内部偏移；已有身体、贴图、影子、拾取与攻击区域随其一起移动。

存在实际身体形状时使用该形状扫掠，遇墙截短位移；无身体形状时不把 CollisionArea / AttackArea 当成实体身体，直接同步根与刚体。被挡后可能被平台甩下，当前不额外推开障碍。承载不会让物品或敌人自动获得坠落、回岸功能。

## 普通武器与 WorldItemEntity

`Weapon_Brawl_discoBall.tscn` 等使用 WorldItemEntity 的场景已经统一接入，无需逐个修改武器场景。

- 默认 `PlatformCarryEnabled = true`，不要求实际身体碰撞形状；敌人的身体碰撞要求不应用于这类物品。
- 未拾取、可见、未排队销毁、没有自身滑行速度时承载。散落/投掷滑行中暂停，停稳后自动恢复。
- 可用 `PlatformGroundProbePath` 指定脚底 Marker；默认使用物品根节点世界坐标。
- 没有身体形状时整体移动根节点，使贴图、影子和拾取区域一起移动；有身体形状时使用 MoveAndCollide，沿用物品自己的碰撞遮罩。
- 平台的 CarryWorldItems、ExcludedGroup 和重叠平台去重同样生效。不会额外生成身体碰撞或将区域形状当成身体。

## 验证

运行 `res://scenes/tests/PlatformCarryRuntimeCheck.tscn`，自动验证玩家、普通敌人、禁用身体对象、排除组、真实办公椅家具、内部位置同步、分类开关、重叠平台去重、飞行副本的 world_items 组身份与排除、家具遇墙截断，以及真实迪斯科球的无身体承载、拾取区域同步、滑行暂停与停稳恢复、拾取后释放。

2026-11-02：扩展后 51 项运行检查全部通过，包含现有 7 种 Weapon_Throw 投掷武器的静置承载、根与刚体同步、出手后停止承载；原有玩家、敌人、家具与普通武器检查也通过。

日志保存到 `res://output/platform-carry.stdout.log` 和 `res://output/platform-carry.stderr.log`。实际家具资源在无窗口渲染中可能产生原有材质编译诊断，应与 CarryCheck 断言结果分开查看。
