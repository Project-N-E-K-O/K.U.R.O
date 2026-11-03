# 敌人坠落：guard1 首次接入

## 当前接入范围

仅 `scenes/actors/characters/Enemy_Normal_guard1.tscn` 已启用。玩家坠落逻辑不变，其他敌人不会自动获得坠落能力。

guard1 根节点新增 `GroundProbe`、`FallController`，状态机新增 `Fall`。通用脚本分别为 `EnemyFallController.cs` 与 `EnemyFallState.cs`。目前替身暂用玩家的 `Animation_character.tscn`，以后替换控制器导出的场景即可，不需要修改深坑。

## 参数

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

1. 每个物理帧在常规运动之后（优先级 100）按脚底点一次查询红色 FallHazardArea 和绿色 FallSupportArea。绿色有效承托优先；红区向上查找 FallRegion。敌人不使用黄色区域或玩家安全历史。
2. 验证启用、存活、未坠落及免疫条件。结清旧合并伤害；旧伤害已致死时保留原死亡流程。
3. 通过已验证请求进入高优先级 Fall，仍执行原状态 Exit。guard1 的 Attack.Exit 会取消控制器与子攻击；Frozen 等退出守卫不能挡住有效 Fall。
4. 隐藏本体、清零速度、清空整棵子树 CollisionObject2D 的碰撞层与掩码；生成独立替身，然后停用敌人子树。
5. 专用地形终结入口将生命归零并立即调用统一 FinalizeDeath，不走普通 Hit、Dying 或 Dead 的演出，不产生普通伤害事件/飘字，不受伤害免疫或减伤影响。保留生命变化通知、正常分数、掉落、效果清理及 DeathFinalized 事件；击杀进度通过原事件统计。
6. 本体当帧排队释放，替身留在当前世界场景，根动画按 FallRegion.FallDuration（至少 0.1 秒）同步速度。独立绑定于替身的 Tween 到期回收，敌人释放不会取消它；缺少动画也能回收。切场景时替身随场景释放。

## 掉落与击杀归属

坠落死亡使用所属 FallRegion 的 FallbackMarkerPaths 选择距离踩空点最近的可用固定掉落点。候选点周围 SafeDistance 内不能有红区或承托区，且候选点不能被 LootObstacleMask 中的身体占据；无有效点时仍死亡、仍统计击杀，但不在坑内生成掉落物，并在配置有掉落表时警告。

掉落表仍保留原有 SpawnOffset、PositionOffset、ScatterRadius 和抛洒速度，关卡需留足空间，避免掉落物从安全生成点再次被散落或抛进坑。这里只替换掉落生成基准，死亡事件收到的敌人位置仍为实际踩空点。

第一版沿用当前击杀规则：DeathFinalized 中的 enemies 计入进度，SampleEnemy 按 GrantsScore 给现有玩家加分。没有新增“最近击退者归属”或攻击伤害总线事件，依赖攻击来源的 Build 不会因环境死亡凭空获得一次攻击命中。

## 后续接入其他敌人

在根节点挂相同的控制器并配置脚底/替身，在状态机增加 EnemyFallState（节点名 Fall），确认原 Attack.Exit 能取消该敌人所有运行中的攻击，再启用 FallEnabled。若有抓取、异步延迟生成、特殊位移或独立世界攻击，还需逐项检查退出清理；已经生成且独立存在的攻击特效不会被通用坠落逻辑一并删除。

当前不包含敌人复活、飞行判定或导航绕坑改造。未来有原地复活能力时需另行设计终结前拦截，不能仅恢复生命。

## 验证入口

`scenes/tests/EnemyFallRuntimeCheck.tscn` 使用真实 guard1，手动推进敌人控制器以隔离自主寻路，覆盖承托、白名单、免疫、眩晕与攻击中断、死亡事件、分数、替身独立回收和安全掉落。

阶段免疫合并到 GrantedImmunities 后已通过 32 项运行检查，覆盖配置迁移、Warmup/Active/Recovery 切换、取消后无残留、阶段标记及全局 Fall 位未被起手写入，以及免疫结束后留在红区立即坠落。同步修复攻击控制器在退出场景树后仍收到检测区信号的问题：按实际 C# 事件订阅断开连接，并拒绝已死亡或离树对象的选招回调。无窗口测试加载掉落武器时仍有现有资源的着色器 custom_samplers 报错；测试断言通过，未修改该着色器。
