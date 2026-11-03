# 敌人寻路与避障配置指南

更新日期：2026-11-03  
适用版本：项目使用的 Godot 4.5.1 Mono。本文依据当前脚本与场景配置整理，不代表所有建议已实施或完成运行验证。

## 1. 当前适合采用的方案

普通地面敌人采用“导航网格规划路线 + NavigationAgent2D 局部避让 + 身体碰撞”的组合；深坑通过只影响烘焙的 NavigationObstacle2D 排除。无需将深坑做成物理墙。

| 层次 | 负责内容 | 不负责内容 |
| --- | --- | --- |
| NavigationRegion2D / NavigationPolygon | 定义可行走区域与绕障路线 | 不直接移动角色，不阻止击退穿越边界 |
| NavigationAgent2D 路径跟随 | 给出下一路径点 | 不会自动让父节点移动 |
| Agent 局部避让 | 调整速度，降低敌人之间及与避让障碍的冲突 | 不替代导航烘焙或真实碰撞 |
| CharacterBody2D 身体形状 + MoveAndSlide | 实际运动和实体阻挡 | 不会替角色规划绕行路线 |
| FallHazardArea / 玩家 Fall | 当前玩家踩空处理 | 目前没有给敌人提供坠落死亡流程 |

不建议让悬挂 Boss、轨道炮台或专门脚本控制的机关统一采用普通追踪方案。

## 2. 当前项目已经具备的代码

| 文件 | 当前作用 |
| --- | --- |
| `scripts/actors/enemies/movement/EnemyChaseMovement.cs` | 普通敌人追踪，读取路径点、提交避让速度、消费 VelocityComputed，并执行 MoveAndSlide |
| `scripts/actors/enemies/EnemyBehaviorConfig.cs` | CloseIn / KeepDistance / Adaptive 战斗站位配置，不是导航烘焙或避障开关 |
| `scripts/actors/enemies/states/EnemyAssistState.cs` | 有独立路径跟随逻辑，修改普通追踪时不能假定它同步生效 |
| `scripts/actors/enemies/states/EnemyKeepDistanceState.cs` | 后撤速度直接驱动身体，不等同于普通追踪的导航路线 |
| `scripts/actors/enemies/attacks/EnemyMoveAttack.cs` | 冲刺攻击，支持 UseNavDuringDash |
| `scripts/actors/enemies/attacks/EnemyDashSlashAttack.cs` | 冲刺斩，支持 UseNavDuringDash，曲线与其他阶段需分别检查 |
| `scripts/actors/enemies/movement/RailChaseMovement.cs` | 轨道移动与限位，不使用普通地面寻路作为运动规则 |
| `scripts/core/NavigationRebakeCoordinator.cs` | 合并家具/屏障触发的导航重烘焙请求，防抖窗口 0.5 秒 |

普通追踪组件从敌人根节点查找名称为 `NavigationAgent2D` 的直接子节点。移动组件自身应是 SampleEnemy 的子节点；脚本使用注册标记防止重复移动组件。

## 3. 给普通敌人配置寻路

### 3.1 节点结构

```text
EnemyRoot (CharacterBody2D，SampleEnemy 或其子类)
├─ CollisionShape2D                  实际身体形状
├─ NavigationAgent2D                 使用这个节点名称
├─ Movement (Node，EnemyChaseMovement)
└─ StateMachine
   ├─ Idle
   ├─ Walk
   ├─ Attack
   └─ ...
```

1. 确认移动组件脚本确实为 EnemyChaseMovement 或相应实现，不能只添加 NavigationAgent2D。
2. 确认 IdleStateName / WalkStateName 对应实际状态节点名称。
3. 检查 BlockedStates：当前默认 Attack、Hit、Frozen、CooldownFrozen、Dying、Dead 阻止常规追踪。
4. 检查敌人检测范围与攻击范围：未检测到玩家或已进入攻击范围，脚本会减速，不应误判为导航失效。
5. 保持身体形状启用，并配置正确的物理 CollisionMask。不要为穿过深坑而关闭普通敌人的全部身体碰撞。

### 3.2 NavigationAgent2D 参数

| 参数 | 设置原则 |
| --- | --- |
| Navigation Layers | 与该敌人允许使用的 NavigationRegion2D 层匹配 |
| Avoidance Enabled | 需要敌人互相让行时开启；仅路径绕障可以不启用 |
| Radius | 按实际世界空间身体占用范围调节，不按贴图尺寸机械填写 |
| Max Speed | 覆盖参与避让时的实际最高速度，包括 CloseIn 加速；不能只看基础速度 |
| Neighbor Distance | 只覆盖合理的邻居距离，避免无必要扩大计算范围 |
| Max Neighbors | 按局部拥挤程度调整，数量过小可能忽略邻居，过大增加成本 |
| Time Horizon Agents / Obstacles | 从较短预测时间开始调节，过大可能过早减速或拥堵 |
| Avoidance Layers / Mask | 配置需要互相避让的对象；与物理层、导航层不是同一套掩码 |
| Path / Target Desired Distance | 与速度和物理更新步长匹配，避免高速越过路径点后折返或终点抖动 |
| Path Max Distance | 偏离路线后重新寻路的距离；当前脚本会在 Ready 强制设为 99999，Inspector 值不会直接生效 |

当前多个普通敌人场景配置了 Radius = 100、Target Desired Distance = 50，并启用避让。例如 Enemy_B1_fat 的 Max Speed = 300，Enemy_C1_waiterA 为 350。这些是现状示例，不是适用于所有敌人的推荐常量。

烘焙 Agent Radius 用于可走空间留白；Agent 的 Radius 用于局部避让占用，两者应根据同一实际体型协调，但不是相互自动同步的参数。

## 4. 给关卡配置导航与固定障碍

1. 给关卡配置 NavigationRegion2D 与 NavigationPolygon，绘制覆盖固定可行走地面的外轮廓。
2. 本项目多个房间采用 `Source Geometry Mode = Groups Explicit`，序列化值为 `2`。
3. 烘焙源组默认是 `navigation_polygon_source_geometry_group`。
4. 在此模式下，必须将实际可解析的障碍节点加入组；只把它的父容器入组，不会递归收集全部子节点。
5. 使用 StaticBody2D 作为真实墙体/家具时，将 StaticBody2D 本身入组，确保形状启用，且其 CollisionLayer 被 Parsed Collision Mask 包含。
6. Parsed Geometry Type 必须包含相应静态碰撞几何；项目未覆写时使用 Godot 默认 Both。
7. 修改轮廓、障碍或其变换后重新烘焙，并开启导航可视化核对结果。

NavigationRegion2D 的导航层、StaticBody2D 的物理碰撞层、Agent 的避让层分别设置，不能用同一个数字推断三者必然互通。

## 5. 深坑：禁止规划经过，但不制造物理墙

建议结构：

```text
FallRegionA
├─ ActivationArea
├─ HazardArea
├─ NavigationObstacle2D
├─ 移动平台
└─ FallbackMarkers
```

NavigationObstacle2D 配置：

- `Vertices`：绘制匹配深坑的闭合轮廓，避免交叉、自交。
- `Affect Navigation Mesh = true`：参与烘焙，剔除坑面导航。
- 加入 `navigation_polygon_source_geometry_group`：适配当前 Groups Explicit 模式。
- 仅需烘焙排除时，建议 `Avoidance Enabled = false`，避免同时引入局部避让边界。
- `Carve Navigation Mesh` 为可选项：开启时按轮廓剔除而不额外受烘焙代理半径偏移影响；希望保留坑边留白时先保持关闭，检查实际烘焙结果。
- 回到拥有 NavigationRegion2D 的关卡重新烘焙，确认坑内多边形已移除。

当前 FallRegionA 已有对应矩形轮廓、Affect Navigation Mesh 和烘焙源分组。F_c1 的导航资源采用 Groups Explicit、Agent Radius = 80；参数以最新场景保存值为准。

不要用覆盖坑面的普通 StaticBody2D 代替导航排除，否则玩家和平台携带的实体也可能被挡住。仅导航排除不会产生物理阻挡，因此敌人仍可能被击退、攻击冲刺或其他脚本推入深坑。

## 6. 当前实现限制：仅配置 Inspector 不能解决的地方

### 6.1 路径结束后的直线回退

EnemyChaseMovement.GetMoveDirection 在导航完成、未获得有效方向或没有 Agent 时，会回退为朝 GetApproachTarget 的直线方向。

因此，“已经从导航中挖掉深坑”并不保证敌人绝不会走进去。玩家在不可达的对岸时，敌人到达可达路径终点后，仍可能继续直线追踪。初始导航尚未同步也需要防护。

建议后续实现：区分正常到达、地图未同步、目标不可达与路径异常；不可达时停止或在有效岸边等待，不再直接朝玩家穿越危险区域。改动需同时检查 Assist、后撤和攻击自己的移动分支。

### 6.2 当前没有完整使用安全速度

EnemyChaseMovement 已提交 desiredVelocity 并接收 VelocityComputed，但会把非零安全速度归一化后恢复为满速；安全速度接近零时还会回退到 desiredVelocity。

这会丢失避让计算中的减速和停车信息，不能认为当前避让已经完全可靠。建议后续保留 safeVelocity 的方向与幅值，并在没有新结果时使用明确的等待/保底规则；不要用满速覆盖避让结果。需要在物理帧顺序下验证，避免同帧读取尚未更新的回调结果。

### 6.3 特殊运动不自动继承追踪避障

- EnemyMoveAttack 的 UseNavDuringDash 当前默认 true；它表示读取导航路径，不代表该攻击同时消费了局部安全速度。
- EnemyDashSlashAttack 的直线、曲线及恢复阶段需要分别确认，不是所有分支都受同一规则控制。
- EnemyKeepDistanceState 的直接后撤、击退和轨道运动不能仅靠 Agent Inspector 保证避坑。
- 攻击范围和朝向判断也不等同于墙体遮挡检测；需要阻止隔墙起手时，应单独检查视线/遮挡规则。

以上为当前实现现状与改进方向，本文没有修改这些脚本。

## 7. 动态家具、火墙与移动平台

### 动态家具与可破坏屏障

当前 RigidBodyWorldItemEntity、DestructibleObject 在相关生命周期中使用 NavigationRebakeCoordinator 合并请求；停顿满 0.5 秒再统一烘焙。因此生成/移除后不会保证同帧更新导航。

仅移动位置不会自动持续重烘焙。平台上的家具与火墙可能已经离开原烘焙位置，不能只依赖旧导航障碍。不要每个平台物理帧全场重烘焙；移动障碍应优先考虑局部避让或单独的导航更新策略，并保留真实碰撞。

### 移动平台

平台承载只负责位移，不会自动创建跨坑路径。若深坑从固定导航中剔除，普通敌人不会自动学会候台、登台、乘坐和离台。

当前推荐先让普通地面敌人不自主规划跨坑。后续需要主动乘平台时，增加明确的登乘状态和可用连接策略；不能将整片坑重新标为可走来假装解决跨坑寻路。

## 8. 推荐落地顺序

1. 先验证一个普通地面敌人：无局部避让时，能沿导航绕过一堵真实墙。
2. 配置深坑 NavigationObstacle2D 与源组，确认烘焙多边形确实排除坑面。
3. 修复不可达目标和路径结束后的直线回退，再验证对岸玩家场景。
4. 保留并正确消费 safeVelocity，验证多敌人让行和零速度停车。
5. 逐项审查后撤、Assist、冲刺和击退，明确哪些动作允许进入深坑。
6. 最后处理动态家具/火墙的导航更新与移动平台登乘，不将全部问题堆到一次全场烘焙中。

## 9. 排查表

| 现象 | 优先检查 |
| --- | --- |
| 烘焙后坑面没有变化 | 实际 Obstacle 节点是否入组、Affect Navigation Mesh、Vertices 与变换是否覆盖导航轮廓 |
| 路径绕开坑，敌人仍走进坑 | 直线回退、独立后撤/冲刺、击退；导航不是物理墙 |
| 敌人绕墙，但互相挤作一团 | Avoidance Enabled、避让层/掩码、邻居参数、实际消费安全速度的代码 |
| 敌人到岸边后继续向玩家直走 | 当前不可达/路径完成后的直线回退 |
| 勾选避让后仍满速碰撞 | 当前安全速度归一化与零速度回退；Max Speed 是否覆盖实际速度 |
| 改 Path Max Distance 没效果 | EnemyChaseMovement.Ready 会覆写为 99999 |
| 初次生成时走错方向 | 导航地图同步时序、出生点是否在有效网格上 |
| 家具搬走后敌人还绕旧位置 | 网格尚未重烘焙，位移本身不等于烘焙请求 |
| 玩家被深坑挡住而无法坠落 | 是否仍有覆盖坑面的实体 StaticBody2D |
| 敌人不上移动平台 | 尚无候台/登乘导航行为，承载不是跨坑规划 |

## 10. 参考

- 项目导航烘焙优化：`res://LOADING_OPTIMIZATION.md` 第 115 行附近。
- 平台承载与特效附着：`res://PLATFORM_CARRY_USAGE.md`。
- [Godot 4.5：NavigationPolygon 烘焙源模式与参数](https://docs.godotengine.org/en/4.5/classes/class_navigationpolygon.html)。
- [Godot 4.5：NavigationObstacle2D 烘焙障碍](https://docs.godotengine.org/en/4.5/classes/class_navigationobstacle2d.html)。
- [Godot 4.5：NavigationAgent 路径跟随与安全速度](https://docs.godotengine.org/en/4.5/tutorials/navigation/navigation_using_navigationagents.html)。

本次只新增文档，未修改敌人运动、导航烘焙或坠落逻辑；未执行构建或运行测试，未生成验证日志。
