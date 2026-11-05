# 敌人坠落系统

> 2026-11-04 架构更新：地面检测统一至 GroundContactSensor，玩家和敌人继承 FallController；物品、武器、特效统一接入 GroundObjectController。平台统一执行承载位移，ItemFallController 中间实现已移除。当前配置与职责以 [地面系统架构](GROUND_SYSTEM_ARCHITECTURE.md) 为准；下文较早的验证记录及实现描述保留作历史参考。

## 当前接入范围

`scenes/actors/characters/Enemy_Normal_guard1.tscn` 至 `Enemy_Normal_guard5.tscn` 已启用。其他敌人不会自动获得坠落能力。

另已接入以下六类敌人，均将 FallController 挂在敌人根节点，在 StateMachine 下添加 Fall，攻击模板暂不配置阶段坠落免疫：

| 场景 | Animation_enemy.tscn 中的动画 |
| --- | --- |
| Enemy_B1_fat | fall_fat |
| Enemy_B2_fat02 | fall_fat02 |
| Enemy_C1_waiterA | fall_waiterA |
| Enemy_C2_waiterA02 | fall_waiterA02 |
| Enemy_D1_corpDoneA | fall_corpDoneA |
| Enemy_D1_corpDoneB | fall_corpDoneB |

这六类敌人已通过 74 项实际场景检查，覆盖各自动画播放、绿色承托、眩晕中断、死亡只结算一次及替身独立回收；验证后删除临时自动检测场景、脚本和日志。验证中修复 EnemyPinballAttack 在攻击结束与退出场景时重复断开接触信号的问题。

guard1–5 根节点配置 `GroundProbe`、`FallController`，状态机配置 `Fall`。通用脚本分别为 `EnemyFallController.cs` 与 `EnemyFallState.cs`。当前统一使用敌人 `Animation_enemy.tscn` 的 `fall_guard1` 动画，guard2–5 暂时复用该演出，以后替换各控制器的场景和动画名即可。guard2–5 所有攻击模板暂不勾选任何坠落免疫，保留原霸体和其他免疫配置。

## 参数

`FallController` 必须是敌人根节点的直接子节点，不能放进 `StateMachine`；`EnemyFallState` 放在 `StateMachine/Fall`。控制器用直接父节点解析 SampleEnemy，Fall 状态也从敌人根节点查找 FallController。已修正 guard2–5 的错误父节点，并以正常自动物理帧复验全部 11 个已接入敌人，通过承托、入坑死亡及替身回收检查。测试先等待地形完成物理注册，再生成敌人。

| 参数 | 说明 |
| --- | --- |
| FallEnabled | 控制器默认关闭；guard1 场景已开启 |
| GroundProbePath | 相对敌人根节点的脚底路径，默认 GroundProbe；缺失时使用根位置 |
| ImmuneStates | 当前状态名在列表中时免坠落，默认空；每个敌人单独配置 |
| FallEffectScene | 坠落替身，根节点需为 Node2D；缺失时仍正常死亡 |
| FallAnimationName | 默认 fall，支持 DirectionalSceneEffect 朝向注入及根 AnimationPlayer 播放 |
| FallEffectOffset | 世界空间偏移，默认零，不随角色缩放、旋转或朝向变化 |
| LootObstacleMask | 校验固定掉落点的身体障碍层，默认 3（环境与敌人） |

`ImmunityFlags.Fall` 是独立免疫；Stun 免疫不阻止坠落。免疫状态结束后留在红区也会重新检查，不依赖再次进入 Area。悬挂、飞行敌人不要开启 FallEnabled；不使用身体碰撞体临时禁用状态推断坠落免疫。

攻击模组在 `Effect Resistance / GrantedImmunities` 中独立勾选 `WarmupFallImmunity`、`ActiveFallImmunity`、`RecoveryFallImmunity`，与同一列表中的 SuperArmor 各自控制坠落与受击。未勾选时不提供阶段坠落免疫，不提供整招 `Fall` 选项。旧 `ImmuneToFallDuring` 配置及其枚举已删除；guard1 的 OnePunchAttack 已迁移为保留原霸体并仅勾选 ActiveFallImmunity，SimpleMeleeAttack 不提供阶段免疫。不要将整个 Attack 加入 ImmuneStates，否则会使未勾选阶段也免疫。

EnemyFallController 在加载时缓存攻击模板，仅查询当前状态子树内、有效且正在运行的模板的 `ProvidesFallImmunity`；排队模组和取消后的 Idle 不提供免疫。攻击起手不会将阶段坠落标记或 Fall 写入 ActiveImmunities，查询也不修改该集合；角色/Build 已有的独立 Fall 能力仍保留。运行时动态增加攻击模组后需调用控制器的 `RefreshAttackModules()`。

Active 阶段免疫不等于严格的实际冲刺窗口。当前使用通用三阶段配置；若以后需要严格跟随 IsDashing，可在具体攻击模组重写 ProvidesFallImmunity，控制器无需识别具体招式类型。

## 流程与时序

1. FallDetectionGate 自动创建独立 Area2D 脚底探针，通过黄色 FallActivationArea 的进入/离开激活检测，不再依赖身体碰撞，远处只同步探针坐标。附近每个物理帧在常规运动之后（优先级 100）按脚底点一次查询红色 FallHazardArea 和绿色 FallSupportArea。绿色有效承托优先；红区向上查找 FallRegion。敌人使用黄色外围激活，不记录玩家安全历史。首次生成或动态区域加载会补查已有重叠，外部传送可调用 RefreshDetectionAfterTeleport；黄色必须覆盖完整红区并留有缓冲。门控和探针均自动创建，无需手动挂载。
2. 验证启用、存活、未坠落及免疫条件。结清旧合并伤害；旧伤害已致死时保留原死亡流程。
3. 通过已验证请求进入高优先级 Fall，仍执行原状态 Exit。guard1 的 Attack.Exit 会取消控制器与子攻击；Frozen 等退出守卫不能挡住有效 Fall。
4. 隐藏本体、清零速度、清空整棵子树 CollisionObject2D 的碰撞层与掩码；生成独立替身，然后停用敌人子树。
5. 专用地形终结入口将生命归零并立即调用统一 FinalizeDeath，不走普通 Hit、Dying 或 Dead 的演出，不产生普通伤害事件/飘字，不受伤害免疫或减伤影响。保留生命变化通知、正常分数、掉落、效果清理及 DeathFinalized 事件；击杀进度通过原事件统计。
6. 本体当帧排队释放，替身留在当前世界场景，根动画按 FallRegion.FallDuration（至少 0.1 秒）同步速度。独立绑定于替身的 Tween 到期回收，敌人释放不会取消它；缺少动画也能回收。切场景时替身随场景释放。

## 掉落与击杀归属

### Dying 尸体

开启 FallEnabled 且初始化时有有效身体形状的角色，Dying 期间继续随平台移动并检测深坑。死亡停用碰撞时保留传感器探针。落坑后隐藏尸体、生成当前敌人的坠落替身，不再切活体 Fall、不重新结算伤害；既有 Dying 计时结束后统一 FinalizeDeath，DeathDuration<=0 的持续尸体落坑后也完成收尾。停止死亡帧特效和 Dead 收尸特效，岸边掉落校验沿用下述规则。已经完成死亡结算、隐藏或坠落的对象不会重复参与。

坠落死亡使用所属 FallRegion 的 FallbackMarkerPaths 选择距离踩空点最近的可用固定掉落点。候选点周围 SafeDistance 内不能有红区或承托区，且候选点不能被 LootObstacleMask 中的身体占据；无有效点时仍死亡、仍统计击杀，但不在坑内生成掉落物，并在配置有掉落表时警告。

掉落表仍保留原有 SpawnOffset、PositionOffset、ScatterRadius 和抛洒速度，关卡需留足空间，避免掉落物从安全生成点再次被散落或抛进坑。这里只替换掉落生成基准，死亡事件收到的敌人位置仍为实际踩空点。

第一版沿用当前击杀规则：DeathFinalized 中的 enemies 计入进度，SampleEnemy 按 GrantsScore 给现有玩家加分。没有新增“最近击退者归属”或攻击伤害总线事件，依赖攻击来源的 Build 不会因环境死亡凭空获得一次攻击命中。

## 后续接入其他敌人

在根节点挂相同的控制器并配置脚底/替身，在状态机增加 EnemyFallState（节点名 Fall），确认原 Attack.Exit 能取消该敌人所有运行中的攻击，再启用 FallEnabled。若有抓取、异步延迟生成、特殊位移或独立世界攻击，还需逐项检查退出清理；已经生成且独立存在的攻击特效不会被通用坠落逻辑一并删除。

当前不包含敌人复活、飞行判定或导航绕坑改造。未来有原地复活能力时需另行设计终结前拦截，不能仅恢复生命。

## 验证入口

自动运行验证使用真实敌人场景，手动推进敌人控制器以隔离自主寻路，覆盖承托、白名单、免疫、眩晕与攻击中断、死亡事件、分数、替身独立回收和安全掉落。根据项目约定，自动检测场景在验证完成后删除，不保留为长期测试关卡；手动测试关卡保留。guard2–5 接入验证共通过 44 项检查，相关自动检测场景及本次专用脚本已清理。

阶段免疫合并到 GrantedImmunities 后已通过 32 项运行检查，覆盖配置迁移、Warmup/Active/Recovery 切换、取消后无残留、阶段标记及全局 Fall 位未被起手写入，以及免疫结束后留在红区立即坠落。同步修复攻击控制器在退出场景树后仍收到检测区信号的问题：按实际 C# 事件订阅断开连接，并拒绝已死亡或离树对象的选招回调。无窗口测试加载掉落武器时仍有现有资源的着色器 custom_samplers 报错；测试断言通过，未修改该着色器。
