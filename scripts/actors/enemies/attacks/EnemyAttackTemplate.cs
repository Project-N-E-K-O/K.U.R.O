using System.Collections.Generic;
using Godot;
using Kuros.Actors.Enemies.States;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 攻击特效生成时机
    /// </summary>
    public enum EffectSpawnTiming
    {
        /// <summary>继承模板级 SpawnTiming（AttackEffectEntry 未显式配置时）。注意：-1 不影响已序列化的 0/1/2 旧值。</summary>
        Inherit = -1,
        /// <summary>进入 Warmup（预热开始）时生成。追加在末尾：不改变已有 0/1/2 的序列化值。</summary>
        OnWarmup,
        OnActive,
        OnAnimationHit,
        OnRecovery
    }

    /// <summary>
    /// 特效朝向翻转模式（AttackEffectEntry 配置用）：Inherit = 继承模板 FlipEffectWithFacing（bool 无法表达"未配置"）。
    /// </summary>
    public enum FacingFlipMode
    {
        Inherit = 0,
        Flip = 1,
        NoFlip = 2
    }

    /// <summary>
    /// 特效生命周期绑定（AttackEffectEntry 配置用）：由攻击模板在对应阶段结束时销毁特效，
    /// 替代固定 Duration——适配阶段时长不固定（Hold 挂起等）的场景。None = 特效自行管理（默认）。
    /// 攻击被打断（Cancel）时所有绑定特效统一销毁。
    /// </summary>
    public enum EffectLifecycleBinding
    {
        None,
        /// <summary>Warmup 结束（进 Active）时销毁——如蓄力预警在出手瞬间消失。</summary>
        OnWarmupEnd,
        /// <summary>Active 结束（进 Recovery）时销毁——如挥击光效在收招时消失。</summary>
        OnActiveEnd,
        /// <summary>Recovery 结束（回 Idle）时销毁——如收招残留特效在攻击结束时消失。</summary>
        OnRecoveryEnd,
    }

    /// <summary>
    /// 基础敌人攻击模板。封装预热-生效-恢复的攻击流程，并提供可重写的钩子。
    /// 继承此类即可快速实现不同的攻击类型（近战、投射、范围等）。
    /// </summary>
    public partial class EnemyAttackTemplate : Node
    {
        internal enum AttackPhase
        {
            Idle,
            Warmup,
            Active,
            Recovery
        }

        [ExportCategory("Meta")]
        [Export] public string AttackName = "DefaultAttack";

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.2f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.35f;
        [Export(PropertyHint.Range, "0,30,0.1")] public float CooldownDurationMultiplier = 1.0f;

        [ExportCategory("Combat")]
        [Export(PropertyHint.Range, "0,10,0.1")] public float DamageMultiplier = 1.0f;
        [Export(PropertyHint.Range, "0,180,1")] public float MaxAllowedAngleToPlayer = 135.0f;
        [Export] public string AnimationName = "animations/attack";
        [Export] public NodePath AttackAreaPath = new NodePath();
        /// <summary>伤害结算区（可选）：本招伤害落在这块 Area2D 上。
        /// 未配置 = 沿用 <see cref="AttackArea"/>（= AttackAreaPath → 未配置再回退敌人根节点 Sprite2D/AttackArea）。</summary>
        [Export] public NodePath DamageAreaPath = new NodePath();
        /// <summary>触发判定区（可选）：配了就要求玩家在该区内本招才可起手（子类用 <see cref="IsPlayerInTriggerArea"/> 判）。
        /// 未配置 = 不做额外位置限制（现状：基类 CanStart 只要求玩家在敌人根节点 DetectionArea 内）。</summary>
        [Export] public NodePath TriggerAreaPath = new NodePath();
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        /// <summary>是否允许攻击者的攻击命中自身。独立于阵营筛选，默认关闭。</summary>
        [Export] public bool AllowSelfDamage { get; set; } = false;

        [ExportCategory("Knockback")]
        /// <summary>击退位移距离（像素）——位移在 KnockbackDuration 内匀减速滑完。</summary>
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
        /// <summary>击退位移时长（秒）——由攻击方决定，不受受击方动画时序影响。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export] public bool KnockbackFromHitPosition = false;

        [ExportCategory("Animation Sync")]
        [Export] public bool RequireAnimationHitTrigger = false;
        [Export] public bool AllowMultipleAnimationHits = false;

        [ExportCategory("Effect Resistance")]
        /// <summary>
        /// 攻击期间赋予敌人的免疫集合。<br/>
        /// 新增免疫类型只需在 <see cref="ImmunityFlags"/> 枚举追加值，无需修改此模板。
        /// </summary>
        [Export(PropertyHint.Flags, "Stun,ForcedMovement,SpeedSlow,WarmupSuperArmor,ActiveSuperArmor,RecoverySuperArmor,ThrowableDamage,NonThrowableDamage")]
        public ImmunityFlags GrantedImmunities = ImmunityFlags.None;

        [ExportCategory("Damage Interrupt")]
        /// <summary>
        /// 受伤眩晕打断（霸体反制机制）：攻击期间自身受伤时，中断当前攻击并切入 Frozen（眩晕）。
        /// 注意是"自己受伤 → 自己眩晕"，不是命中眩晕目标。
        /// 总开关；各阶段是否触发由下方三个阶段开关细分。
        /// </summary>
        [Export] public bool StunInterruptOnDamage = false;
        /// <summary>Warmup 阶段受伤进入眩晕（StunInterruptOnDamage=true 时生效）。</summary>
        [Export] public bool StunDuringWarmup = true;
        /// <summary>Active 阶段受伤进入眩晕。</summary>
        [Export] public bool StunDuringActive = true;
        /// <summary>Recovery 阶段受伤进入眩晕。</summary>
        [Export] public bool StunDuringRecovery = true;
        /// <summary>受伤眩晕时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float DamageTakenFrozenDuration = 1.5f;
        /// <summary>
        /// 触发眩晕打断的伤害类型过滤（Flags）：默认 All（任何伤害都触发，原行为）；
        /// 选中特定类型后，仅该伤害类型命中才进入眩晕（如只吃投掷伤害的敌人）。
        /// </summary>
        [Export(PropertyHint.Flags, "直接攻击,投掷类,区域效果,暴击追加,效果追加")]
        public DamageSourceFilter InterruptDamageSources { get; set; } = DamageSourceFilter.All;

        [ExportCategory("Damage Interrupt Slow-Mo")]
        /// <summary>受伤眩晕打断触发时开启全场时间减缓（Engine.TimeScale）并把镜头聚焦到被眩晕的敌人。仅 StunInterruptOnDamage=true 时生效。</summary>
        [Export] public bool EnableInterruptSlowMo = false;
        /// <summary>时间减缓倍率（1 = 正常；0.3 = 30%）。</summary>
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float SlowMoTimeScale = 0.3f;
        /// <summary>减缓持续时长（真实秒，眩晕计时随 TimeScale 同步减缓不会跑完）。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float SlowMoDuration = 1.0f;
        /// <summary>聚焦期间的镜头缩放（轻微拉近；0 = 只平移不拉近）。结束瞬时恢复触发瞬间的区域 Zoom（如 0.43）。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float InterruptFocusZoom = 0.6f;

        [ExportCategory("Collision Override")]
        [Export] public bool IgnoreEnemyCollisionDuringAttack = false;
        [Export(PropertyHint.Range, "1,32,1")] public int EnemyCollisionLayerIndex = 2;

        [ExportCategory("Faction Collision Layers")]
        [Export(PropertyHint.Range, "1,32,1")] public int PlayerCollisionLayer = 3;
        [Export(PropertyHint.Range, "1,32,1")] public int EnemyCollisionLayer = 2;
        [Export(PropertyHint.Range, "1,32,1")] public int WorldItemCollisionLayer = 1;

        [ExportCategory("Effect")]
        [Export] public Godot.Collections.Array<AttackEffectEntry> Effects { get; set; } = new();

        protected SampleEnemy Enemy { get; private set; } = null!;
        protected SamplePlayer? Player => Enemy.PlayerTarget;
        protected Area2D? AttackArea { get; private set; }
        /// <summary>伤害结算区：DamageAreaPath 配了用它，未配置 = <see cref="AttackArea"/>（现状：最终落到敌人根节点 AttackArea）。</summary>
        protected Area2D? DamageArea { get; private set; }
        /// <summary>触发判定区：**仅在 TriggerAreaPath 配置时非空**（未配置 = 不额外限制，见 <see cref="IsPlayerInTriggerArea"/>）。</summary>
        protected Area2D? TriggerArea { get; private set; }

        private AttackPhase _phase = AttackPhase.Idle;
        /// <summary>当前所处阶段，仅供控制器在打断时判断子攻击所处阶段。</summary>
        internal AttackPhase CurrentPhase => _phase;
        private float _phaseTimer = 0.0f;
        private float _cooldownTimer = 0.0f;
        protected bool _animationHitReady = false;
        private bool _pendingAnimationHitFromWarmup;
        private bool? _previousIgnoreHitStateOnDamage;
        private ImmunityFlags? _previousImmunities;
        private uint _cachedCollisionMask;
        private bool _hasCollisionMaskOverride;
        private uint _cachedAttackAreaMask;
        private bool _hasAttackAreaMaskOverride;
        private uint _cachedDamageAreaMask;
        private bool _hasDamageAreaMaskOverride;
        private int _spawnMarkerIndex;
        private readonly System.Collections.Generic.HashSet<Area2D> _customAreaOverrides = new();
        // 生命周期绑定特效（entry.LifecycleBinding != None）：阶段结束时由模板销毁
        private readonly List<(Node2D fx, EffectLifecycleBinding binding)> _boundEffects = new();

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0.0f;
        public float CooldownRemaining => Mathf.Max(_cooldownTimer, 0.0f);

        public int GetDamage()
        {
            if (Enemy == null) return 0;
            return Mathf.RoundToInt(Enemy.AttackDamage * DamageMultiplier);
        }

        public float GetCooldown()
        {
            if (Enemy == null) return 0f;
            return Enemy.AttackCooldown * CooldownDurationMultiplier;
        }

        public virtual void Initialize(SampleEnemy enemy)
        {
            Enemy = enemy;

            // 与 DamageAreaPath / TriggerAreaPath 同一套解析：先按模板节点、再按敌人根节点。
            // （历史实现只从根节点解析，于是按模板写的 ../../../../Sprite2D/Xxx 全部静默失效、回退根区）
            AttackArea = ResolveArea(AttackAreaPath, Enemy.AttackArea);

            DamageArea = ResolveArea(DamageAreaPath) ?? AttackArea;
            TriggerArea = ResolveArea(TriggerAreaPath);
            WarnIfAreaMismatch();

            OnInitialized();
        }

        /// <summary>按路径取 Area2D：先在模板自身下找，再在敌人根节点下找（兼容两种配置习惯）。
        /// 路径为空或找不到 → 返回 <paramref name="fallback"/>（默认 null）。</summary>
        protected Area2D? ResolveArea(NodePath? path, Area2D? fallback = null)
        {
            if (path == null || path.IsEmpty) return fallback;
            var area = GetNodeOrNull<Area2D>(path) ?? Enemy?.GetNodeOrNull<Area2D>(path);
            return area ?? fallback;
        }

        /// <summary>玩家是否在"触发判定区"内：未配置 TriggerAreaPath → 恒 true（不额外限制）。
        /// 两种区域写法都认：<br/>
        /// ①**检测区**（CDet/ADet：collision_mask 指向玩家**身体**层）→ <see cref="Area2D.OverlapsBody"/>；<br/>
        /// ②**受击/攻击区**（mask 指向玩家 **HitArea** 层）→ <see cref="GameActor.IsHitByArea"/>。<br/>
        /// 只判前者会漏掉后者的配置，只判后者会漏掉前者（GameActor 有 HitArea 时 IsHitByArea 只看 HitArea 重叠，
        /// 检测区的 body 层掩码对它无效）——两边都试才不会出现"配置正确却永远不触发"。
        /// 只提供能力，**不接进 <see cref="CanStart"/>**：那会让远程/召唤类攻击被迫贴脸；
        /// 选招用的 <see cref="IsPlayerInDetectionRange"/> 与保底大招是另外两套语义，别混。</summary>
        protected bool IsPlayerInTriggerArea()
        {
            if (TriggerArea == null) return true;
            if (Player == null) return false;
            return TriggerArea.OverlapsBody(Player) || Player.IsHitByArea(TriggerArea);
        }

        /// <summary>诊断（一次性提示，不影响运行）：区域路径解析不到、或解析结果与敌人根节点 AttackArea 不同时各打一行，
        /// 便于对照"哪一招的区域没生效/不是根区"（伤害落在 DamageArea；触发/掩码按各自解析结果）。</summary>
        private void WarnIfAreaMismatch()
        {
            WarnIfPathUnresolved(AttackAreaPath, nameof(AttackAreaPath));
            WarnIfPathUnresolved(DamageAreaPath, nameof(DamageAreaPath));
            WarnIfPathUnresolved(TriggerAreaPath, nameof(TriggerAreaPath));

            var rootArea = Enemy?.AttackArea;
            if (rootArea == null) return;
            if (AttackArea == rootArea && DamageArea == rootArea && TriggerArea == null) return;
            GD.PushWarning($"[{Name}] 区域与根节点 AttackArea 不同：触发区={TriggerArea?.Name ?? "未配置"} "
                + $"攻击区={AttackArea?.Name ?? "无"} 伤害区={DamageArea?.Name ?? "无"} 根区={rootArea.Name}（伤害落在伤害区）");
        }

        /// <summary>路径配了却解析不到 Area2D → 已静默回退（看着像"配置没生效"）。
        /// 三个区域字段统一走 <see cref="ResolveArea"/>：先按模板节点、再按敌人根节点，所以
        /// `../../../../Sprite2D/Xxx`（相对模板）与 `Sprite2D/Xxx`（相对敌人根）两种写法都吃。</summary>
        private void WarnIfPathUnresolved(NodePath? path, string label)
        {
            if (path == null || path.IsEmpty || Enemy == null) return;
            if (ResolveArea(path) != null) return;

            GD.PushWarning($"[{Name}] {label}=\"{path}\" 解析不到 Area2D → 已回退"
                + "（该字段先按模板节点、再按敌人根节点解析；确认路径真的指向某个区域节点）");
        }

        protected virtual void OnInitialized() { }

        public virtual bool IsPlayerInDetectionRange() => true;

        public virtual bool CanStart()
        {
            if (Enemy == null || Player == null) return false;
            if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return false;
            if (IsRunning || IsOnCooldown) return false;

            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                return false;
            }

            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer == Vector2.Zero) return false;

            Vector2 facing = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            float angle = Mathf.RadToDeg(facing.AngleTo(toPlayer));
            return angle <= MaxAllowedAngleToPlayer;
        }

        public bool TryStart()
        {
            if (!CanStart()) return false;

            _animationHitReady = false;
            _pendingAnimationHitFromWarmup = false;
            _spawnMarkerIndex = 0;

            OnAttackStarted();
            SetPhase(AttackPhase.Warmup);
            return true;
        }

        public void Tick(double delta)
        {
            if (_cooldownTimer > 0.0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_phase == AttackPhase.Idle) return;

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        public void Cancel(bool clearCooldown = false)
        {
            if (_phase != AttackPhase.Idle)
            {
                SetPhase(AttackPhase.Idle); // OnAttackFinished 会设置 _cooldownTimer = CooldownDuration
            }

            if (clearCooldown)
            {
                _cooldownTimer = 0.0f;
                Enemy.AttackTimer = 0.0f;
            }
        }

        public override void _ExitTree()
        {
            UnsubscribeDamageInterrupt();
            RestoreEnemyCollisionMask();
            RestoreAttackAreaMask();
            // 兜底：模板销毁时清理绑定特效（如敌人死亡未走正常攻击结束路径）
            DestroyAllBoundEffects();
            base._ExitTree();
        }

        // ── 受伤眩晕打断（StunInterruptOnDamage）─────────────────────────────

        private bool _damageInterruptSubscribed;

        private void SubscribeDamageInterrupt()
        {
            if (!StunInterruptOnDamage || Enemy == null || _damageInterruptSubscribed) return;
            DamageEventBus.SubscribeWithSource(OnDamageTakenDuringAttack);
            _damageInterruptSubscribed = true;
        }

        private void UnsubscribeDamageInterrupt()
        {
            if (Enemy == null || !_damageInterruptSubscribed) return;
            DamageEventBus.UnsubscribeWithSource(OnDamageTakenDuringAttack);
            _damageInterruptSubscribed = false;
        }

        private void OnDamageTakenDuringAttack(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (target != Enemy) return;
            if (!IsRunning || Enemy == null) return;

            // 伤害类型过滤（InterruptDamageSources）：默认 All 全放行（原行为）
            if (!InterruptDamageSources.Matches(source)) return;

            // 阶段细分开关：当前阶段不允许眩晕则忽略本次受伤
            if (!IsStunEnabledForCurrentPhase()) return;

            // 中断当前攻击（进入冷却）并切入眩晕
            Cancel();
            var frozenState = Enemy.StateMachine?.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozenState != null)
                frozenState.FrozenDuration = Mathf.Max(DamageTakenFrozenDuration, 0.1f);
            Enemy.StateMachine?.ChangeState("Frozen");

            // 全场时间减缓 + 镜头聚焦被眩晕的敌人（真实秒窗口，结束自动恢复 TimeScale/Zoom/聚焦目标）
            if (EnableInterruptSlowMo)
            {
                Kuros.Effects.DamageInterruptSlowMo.Trigger(SlowMoTimeScale, SlowMoDuration);
                if (Enemy.GetViewport().GetCamera2D() is Kuros.Managers.CameraFollow cam)
                    cam.FocusOn(Enemy, SlowMoDuration, InterruptFocusZoom);
            }

            // 子类追加行为（如恢复攻击期间移动的节点位置）
            OnDamageTakenInterrupt();
        }

        private bool IsStunEnabledForCurrentPhase()
        {
            return _phase switch
            {
                AttackPhase.Warmup => StunDuringWarmup,
                AttackPhase.Active => StunDuringActive,
                AttackPhase.Recovery => StunDuringRecovery,
                _ => false
            };
        }

        /// <summary>受伤眩晕触发后调用，供子类覆写追加行为（默认无操作）。</summary>
        protected virtual void OnDamageTakenInterrupt() { }

        protected virtual void OnAttackStarted()
        {
            ApplyEnemyCollisionMaskOverride();
            ApplyAttackAreaMaskOverride();
            SubscribeDamageInterrupt();

            // 将非霸体类免疫写入 ActiveImmunities（霸体按阶段独立管理）
            var nonSuperFlags = GrantedImmunities
                & ~(ImmunityFlags.WarmupSuperArmor | ImmunityFlags.ActiveSuperArmor | ImmunityFlags.RecoverySuperArmor);
            if (nonSuperFlags != ImmunityFlags.None && Enemy != null)
            {
                _previousImmunities = Enemy.ActiveImmunities;
                Enemy.ActiveImmunities |= nonSuperFlags;
            }

            if (Enemy != null && !string.IsNullOrEmpty(AnimationName))
            {
                Enemy.AnimPlayer?.Play(AnimationName);
            }
        }

        protected virtual void OnWarmupStarted()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnWarmup);
            Enemy.Velocity = Vector2.Zero;
        }

        protected virtual void OnActivePhase()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnActive);
            // Warmup 结束（进 Active）：销毁绑定 OnWarmupEnd 的特效（如蓄力预警出手即消失）
            DestroyBoundEffects(EffectLifecycleBinding.OnWarmupEnd);

            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
                return;
            }

            PerformAttackNow();
        }

        protected virtual void OnRecoveryStarted()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnRecovery);
            // Active 结束（进 Recovery）：销毁绑定 OnActiveEnd 的特效（如挥击光效收招即消失）
            DestroyBoundEffects(EffectLifecycleBinding.OnActiveEnd);

            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed);
            _animationHitReady = false;
        }

        protected virtual void OnAttackFinished()
        {
            // 攻击结束（正常回 Idle 或被打断）：销毁全部剩余绑定特效（含 OnRecoveryEnd）
            DestroyAllBoundEffects();
            _cooldownTimer = GetCooldown();
            UnsubscribeDamageInterrupt();

            RestoreEnemyCollisionMask();
            RestoreAttackAreaMask();

            if (Enemy != null && _previousIgnoreHitStateOnDamage.HasValue)
            {
                Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
            }

            if (Enemy != null && _previousImmunities.HasValue)
            {
                Enemy.ActiveImmunities = _previousImmunities.Value;
            }

            _previousIgnoreHitStateOnDamage = null;
            _previousImmunities = null;
        }

        private void ApplyEnemyCollisionMaskOverride()
        {
            if (!IgnoreEnemyCollisionDuringAttack || Enemy == null || _hasCollisionMaskOverride)
            {
                return;
            }

            int clampedLayer = Mathf.Clamp(EnemyCollisionLayerIndex, 1, 32);
            uint enemyLayerBit = 1u << (clampedLayer - 1);
            _cachedCollisionMask = Enemy.CollisionMask;
            Enemy.CollisionMask = _cachedCollisionMask & ~enemyLayerBit;
            _hasCollisionMaskOverride = true;
        }

        private void RestoreEnemyCollisionMask()
        {
            if (Enemy == null || !_hasCollisionMaskOverride)
            {
                return;
            }

            Enemy.CollisionMask = _cachedCollisionMask;
            _hasCollisionMaskOverride = false;
        }

        private uint BuildFactionMask()
        {
            uint mask = 0;
            if (TargetableFactions.HasFlag(TargetableFactions.Player))
                mask |= 1u << (PlayerCollisionLayer - 1);
            if (TargetableFactions.HasFlag(TargetableFactions.Enemy))
                mask |= 1u << (EnemyCollisionLayer - 1);
            if (TargetableFactions.HasFlag(TargetableFactions.WorldItem))
                mask |= 1u << (WorldItemCollisionLayer - 1);
            return mask;
        }

        private void ApplyAttackAreaMaskOverride()
        {
            uint factionMask = BuildFactionMask();
            if (factionMask == 0) return;

            if (AttackArea != null)
            {
                _cachedAttackAreaMask = AttackArea.CollisionMask;
                AttackArea.CollisionMask |= factionMask;
                _hasAttackAreaMaskOverride = true;
            }

            // 伤害区可能是另一个节点（DamageAreaPath）：伤害查询按区自身的 mask 过滤，必须一并覆盖
            if (DamageArea != null && DamageArea != AttackArea)
            {
                _cachedDamageAreaMask = DamageArea.CollisionMask;
                DamageArea.CollisionMask |= factionMask;
                _hasDamageAreaMaskOverride = true;
            }
        }

        private void RestoreAttackAreaMask()
        {
            foreach (var area in _customAreaOverrides)
            {
                if (!GodotObject.IsInstanceValid(area)) continue;
                area.CollisionMask = _cachedAttackAreaMask;
            }
            _customAreaOverrides.Clear();

            if (_hasAttackAreaMaskOverride && AttackArea != null)
            {
                AttackArea.CollisionMask = _cachedAttackAreaMask;
                _hasAttackAreaMaskOverride = false;
            }

            if (_hasDamageAreaMaskOverride && DamageArea != null)
            {
                DamageArea.CollisionMask = _cachedDamageAreaMask;
                _hasDamageAreaMaskOverride = false;
            }
        }

        /// <summary>
        /// 对自定义 Area2D 也根据 TargetableFactions 自动覆写 CollisionMask。
        /// 在子类 OnAnimationHit 中调用 DealDamageFromArea 前使用。
        /// </summary>
        protected void ApplyAttackAreaMaskOverride(Area2D? area)
        {
            if (area == null) return;
            uint factionMask = BuildFactionMask();
            if (factionMask == 0) return;

            if (!_hasAttackAreaMaskOverride && _customAreaOverrides.Count == 0)
                _cachedAttackAreaMask = AttackArea?.CollisionMask ?? area.CollisionMask;

            area.CollisionMask |= factionMask;
            _customAreaOverrides.Add(area);
        }

        /// <summary>
        /// 对指定区域造成伤害（统一入口）。
        /// 子类调用此方法而非直接调 DamageDispatcher，确保阵营筛选和自伤开关始终生效。
        /// </summary>
        protected void DealDamage(Area2D area)
        {
            DamageDispatcher.DealDamageFromArea(area, GetDamage(), Enemy, TargetableFactions, AllowSelfDamage);
        }

        /// <summary>
        /// 对指定区域造成指定伤害（覆盖默认伤害值）。
        /// </summary>
        protected void DealDamage(Area2D area, int damageOverride)
        {
            DamageDispatcher.DealDamageFromArea(area, damageOverride, Enemy, TargetableFactions, AllowSelfDamage);
        }

        protected virtual bool ShouldHoldWarmupPhase()
        {
            return false;
        }

        protected virtual bool ShouldHoldActivePhase()
        {
            return false;
        }

        protected virtual bool ShouldHoldRecoveryPhase()
        {
            return false;
        }

        protected void ForceEnterRecoveryPhase()
        {
            if (_phase == AttackPhase.Active)
            {
                SetPhase(AttackPhase.Recovery);
            }
        }

        private void SetPhase(AttackPhase phase)
        {
            _phase = phase;
            UpdatePhaseSuperArmor();
            switch (phase)
            {
                case AttackPhase.Warmup:
                    _phaseTimer = WarmupDuration;
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    OnActivePhase();
                    TryConsumePendingAnimationHit();
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = RecoveryDuration;
                    OnRecoveryStarted();
                    break;
                case AttackPhase.Idle:
                    _phaseTimer = 0.0f;
                    OnAttackFinished();
                    break;
            }

            if (_phase != AttackPhase.Idle && _phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        /// <summary>
        /// 根据当前阶段更新霸体状态。Warmup/Active/Recovery 三个阶段可独立启用。
        /// 进入对应阶段时开启 IgnoreHitStateOnDamage，离开时还原。
        /// </summary>
        private void UpdatePhaseSuperArmor()
        {
            if (Enemy == null) return;

            ImmunityFlags requiredFlag = _phase switch
            {
                AttackPhase.Warmup   => ImmunityFlags.WarmupSuperArmor,
                AttackPhase.Active   => ImmunityFlags.ActiveSuperArmor,
                AttackPhase.Recovery  => ImmunityFlags.RecoverySuperArmor,
                _ => ImmunityFlags.None
            };

            if (_phase == AttackPhase.Idle)
            {
                if (_previousIgnoreHitStateOnDamage.HasValue)
                {
                    Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
                    _previousIgnoreHitStateOnDamage = null;
                }
                return;
            }

            if (GrantedImmunities.HasFlag(requiredFlag))
            {
                if (!_previousIgnoreHitStateOnDamage.HasValue)
                {
                    _previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
                    Enemy.IgnoreHitStateOnDamage = true;
                }
            }
            else
            {
                if (_previousIgnoreHitStateOnDamage.HasValue)
                {
                    Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
                    _previousIgnoreHitStateOnDamage = null;
                }
            }
        }

        private void AdvancePhase()
        {
            switch (_phase)
            {
                case AttackPhase.Warmup:
                    if (ShouldHoldWarmupPhase())
                    {
                        _phaseTimer = 0.05f;
                        return;
                    }
                    SetPhase(AttackPhase.Active);
                    break;
                case AttackPhase.Active:
                    if (ShouldHoldActivePhase())
                    {
                        _phaseTimer = 0.05f;
                        return;
                    }
                    _animationHitReady = false;
                    _pendingAnimationHitFromWarmup = false;
                    SetPhase(AttackPhase.Recovery);
                    break;
                case AttackPhase.Recovery:
                    if (ShouldHoldRecoveryPhase())
                    {
                        _phaseTimer = 0.05f;
                        return;
                    }

                    SetPhase(AttackPhase.Idle);
                    break;
            }
        }

        protected void PerformAttackNow()
        {
            float originalDamage = Enemy.AttackDamage;
            Enemy.AttackDamage = GetDamage();
            // 伤害落在本模板解析出的伤害区（DamageAreaPath → 回退 AttackArea → 回退根节点 AttackArea）。
            // 旧行为是写死敌人根节点的 AttackArea：配了 AttackAreaPath 指向别的区就会出现"触发换区了、伤害还在根区"。
            Enemy.PerformAttack(DamageArea ?? Enemy.AttackArea, TargetableFactions);
            Enemy.AttackDamage = originalDamage;
        }

        /// <summary>
        /// Spine 帧事件 hit 到达时执行的逻辑。
        /// 默认调用 PerformAttackNow()，子类可覆写以追加击退等额外效果。
        /// 仅在 RequireAnimationHitTrigger = true 时才会被 TriggerAnimationHit 调用。
        /// </summary>
        protected virtual void OnAnimationHit()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnAnimationHit);

            PerformAttackNow();
        }

        public void TriggerAnimationHit()
        {
            if (!RequireAnimationHitTrigger)
            {
                return;
            }

            if (!_animationHitReady)
            {
                if (_phase == AttackPhase.Warmup)
                {
                    _pendingAnimationHitFromWarmup = true;
                    return;
                }

                return;
            }

            OnAnimationHit();

            if (!AllowMultipleAnimationHits)
            {
                _animationHitReady = false;
            }
        }

        private void TryConsumePendingAnimationHit()
        {
            if (!RequireAnimationHitTrigger)
            {
                _pendingAnimationHitFromWarmup = false;
                return;
            }

            if (!_pendingAnimationHitFromWarmup || !_animationHitReady)
            {
                return;
            }

            OnAnimationHit();
            _pendingAnimationHitFromWarmup = false;

            if (!AllowMultipleAnimationHits)
            {
                _animationHitReady = false;
            }
        }

        protected bool TryApplyPlayerKnockback(SamplePlayer player, float distance, float duration, Vector2 fallbackDirection, Area2D? attackArea = null)
        {
            if (Enemy == null || player == null)
            {
                return false;
            }

            // 无论是无敌帧还是护盾完全格挡（此时血量未减少，_pendingHitKnockback=false），
            // 只要没有待处理的击退标记，就跳过击退，避免护盾格挡后仍被大力弹飞。
            if (player is Kuros.Actors.Heroes.MainCharacter mainCharacter)
            {
                if (!mainCharacter.ConsumePendingHitKnockback())
                {
                    return false;
                }
            }

            float clampedDistance = Mathf.Max(0f, distance);
            float clampedDuration = Mathf.Max(duration, 0.01f);
            if (clampedDistance <= 0f)
            {
                return false;
            }

            Vector2 direction;
            if (KnockbackFromHitPosition)
            {
                var area = attackArea ?? AttackArea;
                var hitArea = player.HitArea;
                Vector2 attackPos = GetShapeCenter(area) ?? area?.GlobalPosition ?? Enemy.GlobalPosition;
                Vector2 hitPos = GetShapeCenter(hitArea) ?? hitArea?.GlobalPosition ?? player.GlobalPosition;
                direction = hitPos - attackPos;
            }
            else
            {
                direction = player.GlobalPosition - Enemy.GlobalPosition;
            }

            if (direction == Vector2.Zero)
            {
                direction = fallbackDirection != Vector2.Zero
                    ? fallbackDirection
                    : (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
            }

            Vector2 dirNormalized = direction.Normalized();
            // 位移驱动击退：受击方 Hit 状态在 clampedDuration 内匀减速滑完 clampedDistance
            player.ApplyKnockbackDisplacement(dirNormalized, clampedDistance, clampedDuration);
            // Frozen 外部位移保留原语义（平均速度 = distance/duration）
            ApplyFrozenExternalDisplacement(player, dirNormalized * (clampedDistance / clampedDuration), clampedDuration);
            return true;
        }

        private static Vector2? GetShapeCenter(Area2D? area)
        {
            if (area == null) return null;
            var shape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return shape?.GlobalPosition;
        }

        protected static void ApplyFrozenExternalDisplacement(SamplePlayer player, Vector2 velocity, float duration)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<Kuros.Actors.Heroes.States.PlayerFrozenState>("Frozen");
            if (frozenState == null)
            {
                return;
            }

            if (player.StateMachine?.CurrentState != frozenState)
            {
                return;
            }

            if (!frozenState.AllowExternalDisplacementWhileFrozen)
            {
                return;
            }

            frozenState.ApplyExternalDisplacement(velocity, duration);
        }

        /// <summary>
        /// 生成攻击特效。timing 非空时只生成解析时机匹配的 entry（per-entry SpawnTiming 生效）；
        /// timing 为 null 时全量生成（子类旧调用兼容：它们在模板 timing 匹配时才调用）。
        /// </summary>
        /// <summary>销毁指定绑定类型的特效（阶段切换时）。</summary>
        private void DestroyBoundEffects(EffectLifecycleBinding binding)
        {
            for (int i = _boundEffects.Count - 1; i >= 0; i--)
            {
                if (_boundEffects[i].binding != binding) continue;
                var fx = _boundEffects[i].fx;
                if (GodotObject.IsInstanceValid(fx)) fx.QueueFree();
                _boundEffects.RemoveAt(i);
            }
        }

        /// <summary>销毁全部绑定特效（攻击结束/被打断/模板销毁兜底）。</summary>
        private void DestroyAllBoundEffects()
        {
            foreach (var (fx, _) in _boundEffects)
            {
                if (GodotObject.IsInstanceValid(fx))
                    fx.QueueFree();
            }
            _boundEffects.Clear();
        }

        protected virtual void SpawnEffectAtEnemy(EffectSpawnTiming? timing = null)
        {
            if (Enemy == null) return;

            foreach (var entry in Effects)
            {
                if (entry == null) continue;
                // 模板级 SpawnTiming 已随 entry 迁移移除——entry 未显式配置时回退 OnActive
                if (timing.HasValue && entry.ResolveTiming(EffectSpawnTiming.OnActive) != timing.Value) continue;
                SpawnSingleEffect(entry.Scene, entry);
            }
        }

        private void SpawnSingleEffect(PackedScene? scene, AttackEffectEntry? entry = null)
        {
            if (scene == null) return;

            // 生成时检测（防同类特效叠加）：场上（全局，含其他同类敌人）已存在该组存活特效 → 跳过本次生成。
            // 攻击本身不受影响（动作/伤害照常）；检查与生成同帧连续完成，天然消除复数敌人同时生成的竞态。
            if (entry != null && !string.IsNullOrEmpty(entry.UniqueGroup) && IsFxGroupActive(entry.UniqueGroup))
            {
                GD.Print($"[{AttackName}] 场上已存在特效组 {entry.UniqueGroup}，跳过生成");
                return;
            }

            try
            {
                var effect = scene.Instantiate();

                if (entry != null)
                {
                    entry.ApplyOverrides(effect);
                    // 唯一性组标记：供"场上是否已有该特效"检测（全局，跨敌人共享）
                    if (!string.IsNullOrEmpty(entry.UniqueGroup))
                        effect.AddToGroup(entry.UniqueGroup);
                }

                // per-entry 配置（模板级字段已随 entry 迁移移除——未配置时用默认常量）。
                // NodePath 先按模板自身解析，失败按敌人根兜底（兼容两种配置习惯）
                Vector2 entryOffset = entry?.ResolveOffset(Vector2.Zero) ?? Vector2.Zero;
                Marker2D[] entryMarkers = entry?.ResolveMarkers(this, Enemy, System.Array.Empty<Marker2D>())
                    ?? System.Array.Empty<Marker2D>();
                bool entryFlip = entry?.ResolveFlip(false) ?? false;

                // 跟随类特效（IFollowAnchor）：后续跟随 marker 的原始位置/未镜像 offset——
                // 生成位置必须与跟随位置一致，否则首帧跳变（marker X≠0 且敌人朝左时）
                bool isFollowFx = effect is Kuros.Fx.IFollowAnchor;

                Vector2 adjustedOffset = entryOffset;
                if (!Enemy!.FacingRight && !isFollowFx && entryOffset.X != 0)
                    adjustedOffset.X = -entryOffset.X;

                Node2D? usedMarker = null;
                Vector2 basePos;
                if (entryMarkers.Length > 0)
                {
                    int idx = _spawnMarkerIndex % entryMarkers.Length;
                    var marker = entryMarkers[idx];
                    if (marker != null && GodotObject.IsInstanceValid(marker))
                    {
                        // 锚点偏移按朝向翻 X：常规锚点（按未镜像姿态手工摆放）要翻；
                        // 自镜像锚点（如挂在 SpineBoneNode 下、由 SpineBoneAnchor 跟骨骼矩阵的炮口 marker）由 entry 关掉（OffsetFlip = NoFlip），
                        // 否则会翻两次（特效落到镜像的相反侧）
                        bool offsetFlip = entry?.ResolveOffsetFlip(true) ?? true;
                        Vector2 rel = marker.GlobalPosition - Enemy.GlobalPosition;
                        if (!Enemy.FacingRight && !isFollowFx && offsetFlip) rel.X = -rel.X;
                        basePos = Enemy.GlobalPosition + rel;
                        usedMarker = marker;
                    }
                    else
                    {
                        basePos = Enemy.GlobalPosition;
                    }
                    _spawnMarkerIndex++;
                }
                else
                {
                    basePos = Enemy.GlobalPosition;
                }

                Vector2 spawnPos = basePos + adjustedOffset;

                if (effect is Node2D node2D)
                {
                    if (node2D is Kuros.Fx.IFacingDirectional facing)
                        facing.FacingRight = Enemy.FacingRight;

                    if (entryFlip && !Enemy.FacingRight)
                        node2D.Scale = new Vector2(-node2D.Scale.X, node2D.Scale.Y);

                    if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
                        attackerProvider.Attacker = Enemy;

                    // 通用：跟随类特效注入"产生它的锚点节点"（实际使用的 marker，无则敌人根）——
                    // 特效跟随锚点（marker 是敌人子节点，转向/移动自然跟随），生成方不感知具体类型
                    if (node2D is Kuros.Fx.IFollowAnchor followAnchor)
                        followAnchor.FollowAnchor = usedMarker ?? Enemy;

                    Enemy.GetParent()?.AddChild(node2D);
                    node2D.GlobalPosition = spawnPos;

                    // 生命周期绑定：模板在对应阶段结束时销毁（替代固定 Duration——适配 Hold 挂起等时长不固定阶段）
                    if (entry != null && entry.LifecycleBinding != EffectLifecycleBinding.None)
                        _boundEffects.Add((node2D, entry.LifecycleBinding));
                }
                else if (effect is Kuros.Core.Effects.ActorEffect actorEffect)
                {
                    if (Enemy.EffectController != null)
                        Enemy.ApplyEffect(actorEffect);
                    else
                        actorEffect.QueueFree();
                }
                else
                {
                    effect?.QueueFree();
                }
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[{AttackName}] 无法生成特效: {ex.Message}");
            }
        }

        /// <summary>场上（全局，含其他同类敌人）是否存在指定组的存活特效实例。</summary>
        private bool IsFxGroupActive(string group)
        {
            foreach (Node node in GetTree().GetNodesInGroup(group))
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
