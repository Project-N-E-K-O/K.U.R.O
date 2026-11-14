using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Items;
using Kuros.Items.Effects;
using Kuros.Items.Weapons;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 玩家攻击通用模板。
    /// 处理输入监听、预热/生效/恢复阶段、冷却、资源校验与默认命中判定。
    /// 后续的玩家攻击类型可继承本类并覆盖关键钩子。
    /// </summary>
    public partial class PlayerAttackTemplate : Node
    {
        private enum AttackPhase
        {
            Idle,
            Warmup,
            Active,
            Recovery
        }

        [ExportCategory("Meta")]
        [Export] public string AttackId = "player_attack_default";
        [Export] public string DisplayName = "Player Attack";
        [Export(PropertyHint.MultilineText)] public string Description = "";

        [ExportCategory("Input")]
        [Export] public Array<StringName> TriggerActions { get; set; } = new();
        [Export] public bool AllowHoldInput = false;
        [Export] public bool AllowRecoveryCancel = true;

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.1f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.25f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float CooldownDuration = 0.6f;

        [ExportCategory("Dash Movement")]
        /// <summary>攻击惯性衰减：Warmup 开始后沿面朝方向移动，速度从攻击前玩家速度在 Warmup 内线性衰减到 0（Active 前归零，连贯不跳变）→ Recovery 按 RecoverySpeed 滑行。
        /// 速度纯粹由攻击前玩家速度决定（站立攻击不移动）。默认关闭（不影响现有攻击）。</summary>
        [Export] public bool EnableDashMovement = false;
        /// <summary>Recovery 阶段滑行速度（像素/秒）。0 = Recovery 立即停止。</summary>
        [Export(PropertyHint.Range, "0,3000,10")] public float RecoverySpeed = 0f;
        /// <summary>允许向后冲刺攻击：true = 后撤（dashback）时冲刺方向沿后撤方向（向后）；false = 反方向（面朝向前 + 移动 Y）。</summary>
        [Export] public bool AllowBackwardDashAttack = false;
        /// <summary>仅从 Dash（冲刺闪避）状态进入攻击才触发冲刺攻击（RunAttack）；其他来源（Run/Walk/Idle）用普通攻击。false = 任何来源都冲刺。</summary>
        [Export] public bool DashAttackOnlyFromDash = true;
        /// <summary>冲刺攻击宽限窗口（秒）：冲刺结束后此时间内按攻击仍判定为冲刺攻击（继承 Burst 峰值动量）。0 = 关闭（必须冲刺中触发）。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float DashAttackGraceSeconds = 0.2f;
        /// <summary>冲刺攻击固定速度模板兜底（技能 Fixed 模式未配速度时生效；-1 = 继承 Burst 峰值）。</summary>
        [Export(PropertyHint.Range, "-1,6000,10")] public float DashAttackFixedSpeed = -1f;
        /// <summary>冲刺攻击起步速度峰值倍率模板兜底（技能未配置时生效）。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.05")] public float DashAttackSpeedMultiplier = 1f;
        /// <summary>冲刺攻击速度衰减窗口模板兜底（技能未配置时生效）：TemplateDefault = 旧行为（ShouldDecayDashSpeed 控制 Warmup 衰减 + RecoverySpeed 滑行）。</summary>
        [Export(PropertyHint.Enum, "TemplateDefault,Warmup,Active,Recovery,WarmupActive,ActiveRecovery,FullAttack,None")] public DashAttackDecayWindow DashAttackDecayWindow = DashAttackDecayWindow.TemplateDefault;

        [ExportGroup("Directional Movement")]
        /// <summary>攻击中方向移动（激光笔同款机制）：Active 期间按移动方向实时切换 fwd/bwd/站立动画 + 接管位移。默认关闭。</summary>
        [Export] public bool EnableDirectionalMovement = false;
        /// <summary>攻击中移动的朝向策略：KeepFacing = 锁定朝向（激光笔）；FlipOnBackward = 反向水平输入时，
        /// Active 结束后（Recovery 起点）翻转朝向（拳套——不在攻击进行中翻转，避免当段打击方向闪变）。
        /// 仅 EnableDirectionalMovement 开启时生效。</summary>
        [Export(PropertyHint.Enum, "KeepFacing,FlipOnBackward")] public DirectionalFacingPolicy FacingPolicy = DirectionalFacingPolicy.FlipOnBackward;
        /// <summary>前进动画名（移动方向与面朝同向；Y 移动时 x ≥ 0）。</summary>
        [Export] public string ForwardAnimationName = string.Empty;
        /// <summary>后退动画名（移动方向与面朝反向；Y 移动时 x &lt; 0）。</summary>
        [Export] public string BackwardAnimationName = string.Empty;
        /// <summary>输入死区：X 在此范围内视为站立（不切换方向动画）。</summary>
        [Export(PropertyHint.Range, "0,0.5,0.01")] public float InputDeadzone = 0.1f;
        /// <summary>Active 期间移动速度倍率（当前实际速度的 N%）。100 = 不变，0 = 不动。</summary>
        [Export(PropertyHint.Range, "0,300,0.01")] public float MoveSpeedPercent = 15f;
        /// <summary>碰敌归零检测形状路径（可选）：配置后 EnableDashMovement 移动期间，该形状碰到敌人 HitArea → 前冲速度立即归零（Brawl 同款效果）。空 = 回退玩家 AttackArea。</summary>
        [Export] public NodePath ContactShapePath = new();
        /// <summary>碰敌归零开关：false = 前冲穿过敌人不停止（检测完全跳过）。默认开启（现有武器行为不变）。</summary>
        [Export] public bool EnableEnemyContactStop = true;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public float DamageOverride = 25.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public float AttackRange = 120.0f;
        [Export] public NodePath AttackAreaPath = new NodePath();
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy | TargetableFactions.WorldItem;

        [ExportCategory("Faction Collision Layers")]
        [Export(PropertyHint.Range, "1,32,1")] public int PlayerCollisionLayer = 3;
        [Export(PropertyHint.Range, "1,32,1")] public int EnemyCollisionLayer = 2;
        [Export(PropertyHint.Range, "1,32,1")] public int WorldItemCollisionLayer = 1;

        [ExportCategory("Animation")]
        [Export] public string AnimationName = "animations/attack";
        [Export] public bool RestartAnimationOnLoop = true;
        [Export] public bool UseEquippedWeaponSkillAnimation = false;

        [ExportCategory("Animation Sync")]
        [Export] public bool UseSpineHitEvents = true;

            public enum HitEffectAnchor
        {
            Target,
            Player,
            AttackArea,
            CustomNode
        }

        /// <summary>攻击中方向移动的朝向策略（仅 EnableDirectionalMovement 开启时生效）。</summary>
        public enum DirectionalFacingPolicy
        {
            /// <summary>锁定朝向（激光笔）：移动只切换 fwd/bwd 动画，全程不翻转角色。</summary>
            KeepFacing,
            /// <summary>反向水平输入时，Active 结束后（Recovery 起点）翻转朝向（拳套）。</summary>
            FlipOnBackward
        }

        [ExportCategory("Effects")]
        [Export] public PackedScene? HitEffectScene;
        [Export] public NodePath HitEffectParentPath = new();
        [Export(PropertyHint.Enum, "Target,Player,AttackArea,CustomNode")] public HitEffectAnchor HitEffectAnchorMode = HitEffectAnchor.Target;
        [Export] public NodePath HitEffectAnchorPath = new();
        [Export(PropertyHint.Range, "-1024,1024,1")] public int HitEffectZIndex = 100;
        [Export] public bool HitEffectForceTopLevel = false; 
        [Export] public Vector2 HitEffectLocalOffset = Vector2.Zero;
        [Export] public bool HitEffectMirrorFacing = true;

        [ExportCategory("Requirements")]
        [Export] public bool RequiresTargetInRange = false;
        [Export] public bool RequiresResource = false;
        [Export] public StringName ResourceId = new StringName();
        [Export] public int ResourceCost = 0;
        [Export] public string RequiredItemId = "";
        [Export] public bool ConsumeResourceOnStart = true;

        protected SamplePlayer Player { get; private set; } = null!;
        protected string TriggerSourceState { get; private set; } = string.Empty;
        protected Area2D? AttackArea { get; private set; }
        
        /// <summary>
        /// 当前攻击的 Spine 动画段数（1-based），用于让其他系统（如效果）判断是否为第一段伤害
        /// </summary>
        public static int CurrentAttackHitStep { get; private set; } = 1;
        /// <summary>当前攻击回合 ID：每次攻击开始递增，供连击/段数效果隔离回合（防止跨回合命中污染判定）。</summary>
        public static int CurrentAttackRoundId { get; private set; } = 1;

        protected float _currentDashSpeed;   // 实际移动速度（运行时状态）
        protected Vector2 _dashDirection;    // 移动方向（面朝）
        protected bool _isDashing;           // Warmup+Active 衰减移动中
        protected bool _isSliding;           // Recovery 滑行中
        protected bool _dashDisabled;        // 移动被禁用（碰敌归零等）
        private string _lastDirectionalAnimation = string.Empty;   // 已播放的方向动画（去重，同名不重播）
        private bool _directionalActive;                            // Active 方向移动接管标志
        private bool _pendingFacingFlip;                            // FlipOnBackward：Active 期间记录的待翻转（Recovery 起点应用）
        private float _dashElapsed;          // 衰减计时（Warmup 内从起步速度衰减到 0）
        private float _dashStartSpeed;       // 本段攻击的起步速度（首段=AttackEntrySpeed，连击=上一段衰减结果）
        private float _dashDecayElapsed;     // 新窗口模型衰减计时（跨所选阶段）
        private DashAttackDecayWindow _resolvedDecayWindow = DashAttackDecayWindow.TemplateDefault; // 本段攻击解析后的衰减窗口
        private bool _resolvedDashAttack;  // 本段攻击的冲刺判定快照（TryStart 求值一次）：
                                            // ShouldUseDashMovement 的宽限窗口分支是时间敏感的，攻击中途会过期翻转，
                                            // 阶段 hook（特效生成/动画速度/衰减分支）必须用快照，与 DashEffects 的 isDashAttack 参数语义一致
        private CollisionShape2D? _contactShape;   // 碰敌检测形状缓存（ContactShapePath）
        private bool _isRestartAttack;       // 本次攻击是否为 Recovery 打断重启（连击：延续上一段衰减结果）

        // ── 技能阶段特效（WeaponSkillDefinition.AttackFxEntries/DashAttackFxEntries，AttackEffectEntry 数据驱动）──
        private readonly List<(Node fx, EffectLifecycleBinding binding)> _boundSkillFx = new();
        private int _skillFxMarkerIndex;   // 锚点轮换计数（多 marker 依次使用）

        private AttackPhase _phase = AttackPhase.Idle;
        private float _phaseTimer = 0f;
        private float _cooldownTimer = 0f;
        private bool _wantsRestart = false;
        private bool _wantsMove = false;
        private float _pendingSkipWarmupStart = -1f;
        private bool _hitEffectSubscribed = false;
        private bool _hitWindowActive = false;
        private Node? _hitEffectParent;
        private Node2D? _customAnchor;
        private Node? _spineControllerNode;
        private Callable _spineHitCallable;
        private bool _spineHitSubscribed = false;
        private bool _spineHitWindowActive = false;
        private string _spineAttackAnimationName = string.Empty;
        protected string _resolvedAnimationName = string.Empty;

        /// <summary>当前是否处于 Active 阶段（供子类做阶段判断，如激光笔的 Active 方向切换）。</summary>
        protected bool IsInActivePhase => _phase == AttackPhase.Active;

        /// <summary>切换 Active 阶段的动画名并同步 Spine hit 事件匹配名（切换后新动画的 hit 事件才能通过匹配）。</summary>
        protected void SetSpineAttackAnimation(string animationName)
        {
            _spineAttackAnimationName = animationName;
        }

        /// <summary>当前攻击动画的 Warmup 段时长（动画时间轴秒数，未缩放），供子类切换动画时跳帧到 Active 段。</summary>
        protected float ResolveWarmupAnimationTime()
            => ResolveDashOrSkillDuration(_activeWeaponSkill?.DashWarmupDuration, _activeWeaponSkill?.WarmupDuration, WarmupDuration);

        /// <summary>当前 Active 阶段的动画播放速度（技能 ActiveAnimationSpeed × 全局攻速倍率，与阶段计时同源），
        /// 供子类切换动画时保持速度一致，避免播放速度被重置为 1× 导致与阶段计时错位。</summary>
        protected float ResolveActiveAnimationSpeed()
            => Mathf.Max(ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashActiveAnimationSpeed ?? -1f, _activeWeaponSkill?.ActiveAnimationSpeed ?? 1f)
                * Mathf.Max(Player.AttackSpeedMultiplier, 0.01f), 0.01f);

        /// <summary>是否处于冲刺攻击（EnableDashMovement 开启，且满足来源状态条件——DashAttackOnlyFromDash 时仅 Dash 来源触发 RunAttack）。</summary>
        protected bool ShouldUseDashMovement()
        {
            if (!EnableDashMovement) return false;
            if (!DashAttackOnlyFromDash) return true;
            if (TriggerSourceState == "Dash") return true;
            // 宽限窗口：冲刺结束后 GraceSeconds 内按攻击仍判定为冲刺攻击（继承 Burst 峰值动量）
            if (DashAttackGraceSeconds > 0f && Player != null && Player.LastDashFrameMs > 0)
                return Time.GetTicksMsec() - Player.LastDashFrameMs <= (ulong)(DashAttackGraceSeconds * 1000f);
            return false;
        }

        /// <summary>阶段时长解析（冲刺攻击优先技能 Dash 配置，-1 = 用普通阶段）：技能覆盖或模板默认。</summary>
        private float ResolveDashOrSkillDuration(float? dashOverride, float? skillOverride, float templateDefault)
            => _resolvedDashAttack && dashOverride >= 0f
                ? dashOverride.Value
                : ResolveSkillTiming(skillOverride, templateDefault);

        /// <summary>动画速度解析：冲刺攻击时优先技能 Dash 动画速度（&gt;=0），否则技能/默认。</summary>
        private float ResolveDashOrSkillSpeed(float dashSpeed, float skillSpeed)
            => _resolvedDashAttack && dashSpeed >= 0f ? dashSpeed : skillSpeed;
        private WeaponSkillDefinition? _activeWeaponSkill;
        private WeaponAttackFallPhases _activeFallImmunities;
        /// <summary>本次攻击起手时锁定的配置，只对当前实际阶段生效。</summary>
        public bool ProvidesFallImmunity => _phase switch
        {
            AttackPhase.Warmup => (_activeFallImmunities & WeaponAttackFallPhases.Warmup) != 0,
            AttackPhase.Active => (_activeFallImmunities & WeaponAttackFallPhases.Active) != 0,
            AttackPhase.Recovery => (_activeFallImmunities & WeaponAttackFallPhases.Recovery) != 0,
            _ => false
        };
        private AttackHitboxDebugDrawer? _hitboxDebugDrawer;
        private int _currentHitStep = 1;  // 记录当前 Spine 动画段数（1-based）
        private readonly HashSet<int> _resolvedHitSteps = new();  // 本次攻击已结算的 hit 段（同段不重复结算——方向切换动画变体不产生额外伤害）
        private PlayerInventoryComponent? _inventoryComponent;
        private uint _cachedAttackAreaMask;
        private bool _hasAttackAreaMaskOverride;
        // 本轮攻击的有效 timing（可能被武器技能定义覆盖）
        private float _effectiveWarmup = 0.15f;
        private float _effectiveActive = 0.1f;
        private float _effectiveRecovery = 0.25f;
        private List<ActorEffect> _appliedEquipEffects = new();  // 已应用的装备效果
        private bool _equipEffectsSubscribed = false;  // 是否已订阅装备事件

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0f;
        public bool IsInRecovery => _phase == AttackPhase.Recovery;
        public bool CanDashCancel => !IsRunning || IsInRecovery;
        public bool WantsRestart => _wantsRestart;
        public bool WantsMove => _wantsMove;

        public virtual void Initialize(SamplePlayer player)
        {
            Player = player;

            if (!string.IsNullOrEmpty(AttackAreaPath.ToString()))
            {
                AttackArea = Player.GetNodeOrNull<Area2D>(AttackAreaPath);
            }

            if (AttackArea == null)
            {
                AttackArea = Player.AttackArea;
            }

            // 获取背包组件用于监听装备事件
            _inventoryComponent = Player.InventoryComponent ?? Player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (_inventoryComponent != null)
            {
                _inventoryComponent.WeaponEquipped += OnWeaponEquipped;
                _inventoryComponent.WeaponUnequipped += OnWeaponUnequipped;
                _equipEffectsSubscribed = true;
                
                // 如果已经装备了武器，立即应用效果
                var currentWeapon = _inventoryComponent.GetCurrentWeaponDefinition();
                if (currentWeapon != null)
                {
                    OnWeaponEquipped(currentWeapon);
                }
            }

            InitializeHitEffectSupport();
            InitializeSpineHitSupport();

            OnInitialized();
        }

        public override void _ExitTree()
        {
            RestoreAttackAreaMask();
            base._ExitTree();

            // 移除已应用的装备效果
            RemoveAllEquipEffects();
            
            if (_equipEffectsSubscribed && _inventoryComponent != null)
            {
                _inventoryComponent.WeaponEquipped -= OnWeaponEquipped;
                _inventoryComponent.WeaponUnequipped -= OnWeaponUnequipped;
                _equipEffectsSubscribed = false;
            }
            
            if (_hitEffectSubscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _hitEffectSubscribed = false;
            }

            UnsubscribeSpineHitSignal();

            if (_hitboxDebugDrawer != null && GodotObject.IsInstanceValid(_hitboxDebugDrawer))
            {
                _hitboxDebugDrawer.QueueFree();
                _hitboxDebugDrawer = null;
            }
        }

        protected virtual void OnInitialized() { }

        public void SetTriggerSourceState(string stateName)
        {
            TriggerSourceState = stateName;
        }

        public bool HasWeaponRequirement => !string.IsNullOrWhiteSpace(RequiredItemId);

        public bool IsWeaponRequirementSatisfied()
        {
            if (!HasWeaponRequirement)
            {
                return true;
            }

            string currentWeaponId = ResolveCurrentWeaponItemId();
            return string.Equals(currentWeaponId, RequiredItemId, StringComparison.OrdinalIgnoreCase);
        }

        public void Tick(double delta)
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_phase == AttackPhase.Idle) return;

            if (_phase == AttackPhase.Recovery && AllowRecoveryCancel)
            {
                if (IsInputTriggered() && CanCancelRecoveryForRestart())
                {
                    _wantsRestart = true;
                    SetPhase(AttackPhase.Idle);
                    return;
                }
                Vector2 moveInput = Player.GetControlledMovementInput();
                if (moveInput.LengthSquared() > 0.01f)
                {
                    _wantsMove = true;
                    SetPhase(AttackPhase.Idle);
                    return;
                }
            }

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0f)
            {
                AdvancePhase();
            }

            OnTick(delta);
            RefreshCurrentHitboxDebug();
        }

        /// <summary>移动起步时机：true = Warmup 开始起步（惯性衰减模式，默认）；false = Active 开始起步（阶段前冲模式，Brawl 原语义：Warmup 停）。</summary>
        protected virtual bool ShouldStartDashInWarmup => true;
        /// <summary>起步后是否在 Warmup+Active 内衰减到 0；false = 保持匀速冲刺（阶段前冲模式）。</summary>
        protected virtual bool ShouldDecayDashSpeed => true;
        /// <summary>起步速度解析（首段攻击）：冲刺来源继承冲刺 Burst 峰值快照（与打断时机无关）；
        /// 跑/走/站立来源继承实时移动速度（攻击惯性）。
        /// 子类可重载（如 BrawlRiotBracer：固定 DashSpeed 冲刺——覆写后快照逻辑被绕过，保持旧语义）。</summary>
        protected virtual float ResolveDashStartSpeed()
        {
            if (ShouldUseDashMovement() && Player.LastDashBurstSpeed > 0f)
                return Player.LastDashBurstSpeed;
            return Player.CurrentMoveSpeed;
        }

        /// <summary>碰敌归零：ContactShapePath 形状碰到敌人 HitArea → 前冲速度归零（返回 true 表示已停止，本帧不再写入速度）。</summary>
        private bool TryStopDashOnEnemyContact()
        {
            if (!EnableEnemyContactStop) return false;

            var shape = ResolveContactShape();
            if (shape?.Shape == null) return false;

            var spaceState = shape.GetWorld2D().DirectSpaceState;
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shape.Shape,
                Transform = shape.GlobalTransform,
                CollideWithAreas = true,
                CollideWithBodies = false
            };
            foreach (var result in spaceState.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                if (collider.As<GodotObject>() is not Area2D area) continue;
                if ((string)area.Name != "HitArea") continue;
                var actor = area.GetParent() as GameActor
                    ?? area.GetParent()?.GetParent() as GameActor;
                if (actor != null
                    && actor.IsInGroup("enemies")
                    && !actor.IsDeathSequenceActive
                    && !actor.IsDead)
                {
                    _dashDisabled = true;
                    _currentDashSpeed = 0f;
                    _isDashing = false;
                    _isSliding = false;
                    Player.Velocity = Vector2.Zero;
                    return true;
                }
            }
            return false;
        }

        private CollisionShape2D? ResolveContactShape()
        {
            if (_contactShape != null && IsInstanceValid(_contactShape) && _contactShape.Shape != null)
                return _contactShape;

            if (!ContactShapePath.IsEmpty)
                _contactShape = GetNodeOrNull<CollisionShape2D>(ContactShapePath);
            // 空路径 → 默认玩家 AttackArea（EnableDashMovement 开启即自动碰敌归零，无需每武器配置）
            _contactShape ??= Player?.GetNodeOrNull<CollisionShape2D>("AttackArea/CollisionShape2D");
            return _contactShape;
        }

        /// <summary>冲刺方向解析：允许向后冲刺时沿攻击前移动方向（后撤时向后）；不允许时——站立回退面朝、纯 Y 移动沿 Y、有 X 分量则面朝 X + 移动 Y（后撤时向前）。</summary>
        protected virtual Vector2 ResolveDashDirection()
        {
            Vector2 input = Player.CurrentMoveDirection;

            if (AllowBackwardDashAttack)
            {
                return input != Vector2.Zero
                    ? input
                    : (Player.FacingRight ? Vector2.Right : Vector2.Left);
            }

            if (Mathf.Abs(input.X) <= 0.01f && Mathf.Abs(input.Y) <= 0.01f)
                return Player.FacingRight ? Vector2.Right : Vector2.Left;   // 站立：面朝
            if (Mathf.Abs(input.X) <= 0.01f)
                return new Vector2(0f, Mathf.Sign(input.Y));               // 纯 Y 移动：沿 Y（不含面朝 X 分量）

            Vector2 dir = new(Player.FacingRight ? 1f : -1f, input.Y);     // 有 X 分量：面朝 X + 移动 Y
            return dir.Normalized();
        }

        /// <summary>启动移动：沿冲刺方向以起步速度移动。
        /// 首段起步 = ResolveDashStartSpeed（攻击前速度/子类兜底）；连击（Recovery 打断重启）起步 = 上一段衰减结果（0）。
        /// 起步速度存入 _dashStartSpeed：衰减公式必须用它（而非 AttackEntrySpeed），否则连击会被初始速度覆盖。</summary>
        private void StartDashMovement()
        {
            _dashDirection = ResolveDashDirection();
            // 连击段直接继承上一段衰减结果；首段走起步速度解析 + 新配置（Fixed/倍率）
            _dashStartSpeed = _isRestartAttack
                ? _currentDashSpeed
                : ApplyDashSpeedConfig(ResolveDashStartSpeed());
            _currentDashSpeed = _dashStartSpeed;
            _dashElapsed = 0f;
            _dashDecayElapsed = 0f;
            _resolvedDecayWindow = ResolveDecayWindow();
            _dashDisabled = false;
            _isDashing = true;
            _isSliding = false;
            Player.Velocity = _dashDirection * _currentDashSpeed;
        }

        /// <summary>新速度配置应用（仅冲刺攻击路径）：技能 Fixed 模式覆盖继承值；峰值倍率乘在最终起步速度上。
        /// 技能字段未配置（-1/负数）时回退模板 export。跑/走惯性路径不经过此处（零影响）。</summary>
        private float ApplyDashSpeedConfig(float inherited)
        {
            if (!ShouldUseDashMovement()) return inherited;

            var skill = _activeWeaponSkill;
            if (skill?.DashAttackSpeedSource == DashAttackSpeedSource.Fixed)
            {
                float fixedSpeed = skill.DashAttackFixedSpeed > 0f
                    ? skill.DashAttackFixedSpeed
                    : (DashAttackFixedSpeed > 0f ? DashAttackFixedSpeed : inherited);
                inherited = fixedSpeed;
            }

            float multiplier = skill != null && skill.DashAttackSpeedMultiplier > 0f
                ? skill.DashAttackSpeedMultiplier
                : DashAttackSpeedMultiplier;
            return inherited * multiplier;
        }

        /// <summary>解析本段冲刺攻击的衰减窗口：技能 → 模板 export → TemplateDefault（旧行为）。</summary>
        private DashAttackDecayWindow ResolveDecayWindow()
        {
            var skill = _activeWeaponSkill;
            if (skill != null && skill.DashAttackDecayWindow != DashAttackDecayWindow.TemplateDefault)
                return skill.DashAttackDecayWindow;
            return DashAttackDecayWindow;
        }

        /// <summary>衰减窗口对应的总时长（各阶段时长本已按武器技能解析）。</summary>
        private float ResolveDecayWindowDuration(DashAttackDecayWindow window)
        {
            return window switch
            {
                DashAttackDecayWindow.Warmup => _effectiveWarmup,
                DashAttackDecayWindow.Active => _effectiveActive,
                DashAttackDecayWindow.Recovery => _effectiveRecovery,
                DashAttackDecayWindow.WarmupActive => _effectiveWarmup + _effectiveActive,
                DashAttackDecayWindow.ActiveRecovery => _effectiveActive + _effectiveRecovery,
                DashAttackDecayWindow.FullAttack => _effectiveWarmup + _effectiveActive + _effectiveRecovery,
                _ => _effectiveWarmup
            };
        }

        protected virtual void OnTick(double delta)
        {
            // 位移对所有来源启用（普通攻击也有攻击惯性——Run/Walk 进入攻击带当前速度衰减）；
            // Dash 动画/阶段/速度仍由 ShouldUseDashMovement（仅 Dash 来源）控制。
            // 冲刺攻击路径与跑/走惯性路径在此硬分叉：前者走新衰减窗口模型，后者旧逻辑原样
            if (EnableDashMovement && !_dashDisabled)
            {
                // 碰敌归零（通用）：检测形状（ContactShapePath 或默认玩家 AttackArea）碰到敌人 HitArea → 前冲速度立即归零
                if (!TryStopDashOnEnemyContact())
                {
                    if (ShouldUseDashMovement())
                        UpdateDashAttackMovement((float)delta);
                    else
                        UpdateInertiaMovement((float)delta);
                }
            }

            // 方向移动（激光笔同款）：Active 期间按移动方向实时切换 fwd/bwd + 接管位移。
            // 可与冲刺攻击顺序组合：冲刺速度衰减到 0（或未冲刺）后方向移动接管——冲刺阶段冲刺位移优先
            if (EnableDirectionalMovement && (!_isDashing || _currentDashSpeed <= 0.01f))
            {
                if (!_directionalActive || !IsInActivePhase)
                {
                    _directionalActive = false; // 退出 Active 停止接管，Warmup/Recovery 走基类原有逻辑
                    return;
                }

                UpdateDirectionalMoveVelocity();

                // 前后移动动画未配置：回退默认攻击动画（OnAttackStarted 已播放），
                // 同一动画仅移动——不切换动画、不改 hit 匹配名（与普通攻击一致）
                if (ShouldSkipDirectionalAnimation())
                    return;

                string target = ResolveDirectionalAnimation();
                if (target == _lastDirectionalAnimation) return; // 动画名未变：不重播
                PlayDirectional(target);
                _lastDirectionalAnimation = target;
            }
        }

        /// <summary>Active 期间直接接管 Velocity（同冲刺位移模式：攻击状态下玩家移动不读 Speed 属性，
        /// 必须由模板每帧设置 Velocity 供状态机 MoveAndSlide）。全方向移动：有输入 → 按「当前实际速度 ×N%」移动；无输入 → 停下。</summary>
        private void UpdateDirectionalMoveVelocity()
        {
            if (Player == null) return;
            Vector2 input = Player.GetControlledMovementInput();
            if (input.LengthSquared() <= 0.01f)
            {
                Player.Velocity = Vector2.Zero;
                return;
            }

            // 拳套策略：Active 期间检测与面朝相反的水平输入 → 记录待翻转标记。
            // 只记录不翻转：OnRecoveryStarted（Active 结束后）统一应用，
            // 避免攻击进行中翻转导致当段打击方向闪变/多段 hit 方向错乱
            if (FacingPolicy == DirectionalFacingPolicy.FlipOnBackward)
            {
                bool backward = Player.FacingRight ? input.X < -InputDeadzone : input.X > InputDeadzone;
                if (backward)
                    _pendingFacingFlip = true;
            }

            float speed = Player.Speed * (MoveSpeedPercent / 100f);
            Player.Velocity = input.Normalized() * speed;
        }

        /// <summary>冲刺攻击路径位移：新衰减窗口模型（技能/模板未配置时回退旧行为——
        /// ShouldDecayDashSpeed 控制 Warmup 衰减 + RecoverySpeed 滑行，Brawl 覆写 false = 匀速）。</summary>
        private void UpdateDashAttackMovement(float dt)
        {
            if (_isDashing)
            {
                if (_resolvedDecayWindow == DashAttackDecayWindow.TemplateDefault)
                {
                    // 旧行为回退：Warmup 内从起步速度线性衰减到 0
                    if (ShouldDecayDashSpeed)
                    {
                        _dashElapsed += dt;
                        float total = _effectiveWarmup;
                        float t = total > 0f ? Mathf.Clamp(_dashElapsed / total, 0f, 1f) : 1f;
                        _currentDashSpeed = _dashStartSpeed * (1f - t);
                    }
                }
                else if (_resolvedDecayWindow != DashAttackDecayWindow.None)
                {
                    // 新窗口模型：跨所选阶段（Warmup/Active/Recovery 组合）线性衰减到 0
                    _dashDecayElapsed += dt;
                    float total = ResolveDecayWindowDuration(_resolvedDecayWindow);
                    float t = total > 0f ? Mathf.Clamp(_dashDecayElapsed / total, 0f, 1f) : 1f;
                    _currentDashSpeed = _dashStartSpeed * (1f - t);
                }
                // None = 匀速（保持起步值）
                Player.Velocity = _dashDirection * _currentDashSpeed;
            }
            else if (IsInRecovery && _resolvedDecayWindow == DashAttackDecayWindow.TemplateDefault && _isSliding)
            {
                // 旧行为：Recovery 滑行
                Player.Velocity = _dashDirection * RecoverySpeed;
            }
            else if (IsInRecovery)
            {
                Player.Velocity = Vector2.Zero;
            }
        }

        /// <summary>跑/走/站立攻击惯性路径（与冲刺攻击硬分叉，旧逻辑原样）：
        /// 实时速度 + ShouldDecayDashSpeed 控制 Warmup 衰减 + RecoverySpeed 滑行。</summary>
        private void UpdateInertiaMovement(float dt)
        {
            if (_isDashing)
            {
                if (ShouldDecayDashSpeed)
                {
                    // Warmup 内从本段起步速度线性衰减到 0（Active 前归零——出手时已无位移惯性）
                    _dashElapsed += dt;
                    float total = _effectiveWarmup;
                    float t = total > 0f ? Mathf.Clamp(_dashElapsed / total, 0f, 1f) : 1f;
                    _currentDashSpeed = _dashStartSpeed * (1f - t);
                }
                Player.Velocity = _dashDirection * _currentDashSpeed;
            }
            else if (_isSliding)
            {
                Player.Velocity = _dashDirection * RecoverySpeed;
            }
            else if (IsInRecovery)
            {
                Player.Velocity = Vector2.Zero;
            }
        }

        /// <summary>播放方向动画：若目标已是当前播放的动画则跳过（防重播）；否则切换动画并**保持当前时间轴位置**。
        /// 三动画共用同一 timing——不重置播放位置，否则快速前后切换时动画永远停在起点，hit 帧（时间轴后段）永远不触发。
        /// timeScale 必须传当前 Active 阶段速度，否则播放速度被重置为 1×，与阶段计时错位。</summary>
        private void PlayDirectional(string target)
        {
            if (Player is not MainCharacter mc) return;
            if (target == mc.CurrentAnimationName) return; // 已在该动画：不重播
            mc.PlaySpineAnimationFrom(target, mc.CurrentAnimationTime, loop: true,
                ResolveActiveAnimationSpeed());
            SetSpineAttackAnimation(target);
        }

        /// <summary>按面朝与移动输入解析目标动画：Y 轴有移动 → 按 X 符号分流（x ≥ 0 → fwd，x < 0 → bwd）；
        /// 仅 X 轴移动 → 同向 → fwd，反向 → bwd，X 在 deadzone 内 → 站立动画。全程不翻转角色。</summary>
        private string ResolveDirectionalAnimation()
        {
            Vector2 input = Player.GetControlledMovementInput();
            float x = input.X;
            float y = input.Y;

            // Y 轴有移动：方向由 X 符号决定（与面朝无关）
            if (y > InputDeadzone || y < -InputDeadzone)
            {
                return x >= 0f ? ForwardAnimationName : BackwardAnimationName;
            }

            // 仅 X 轴移动：保持原面朝规则
            bool forward = Player.FacingRight ? x > InputDeadzone : x < -InputDeadzone;
            bool backward = Player.FacingRight ? x < -InputDeadzone : x > InputDeadzone;
            if (forward) return ForwardAnimationName;
            if (backward) return BackwardAnimationName;
            return _resolvedAnimationName;
        }

        /// <summary>从 Recovery 打断重启时跳过 Warmup，直接进入 Active 阶段（伤害立即判定）。</summary>
        [Export] public bool SkipWarmupOnRecoveryRestart = false;

        public bool TryStart(bool checkInput = true)
        {
            // Recovery 打断重启是玩家主动连击，豁免 AttackTimer 冷却
            bool isRestart = _wantsRestart;
            if (!CanStart(checkInput, allowDuringCooldown: isRestart))
            {
                // 失败也重置：否则 _wantsRestart 残留 true，下次（如 Dash 打断攻击）被误判为连击，
                // 起步速度走 _currentDashSpeed（上次攻击残留）而绕过 ResolveDashStartSpeed（实时速度）
                _wantsRestart = false;
                return false;
            }
            _isRestartAttack = isRestart;

            // 冲刺判定快照：必须早于 SetPhase（OnWarmupStarted 的特效生成先于 OnAttackStarted 执行）
            _resolvedDashAttack = ShouldUseDashMovement();

            bool skipWarmup = isRestart && SkipWarmupOnRecoveryRestart;
            _wantsRestart = false;
            _wantsMove = false;

            // 跳过 Warmup 时动画从 Warmup 结束处（动画内容时间）跳帧播放
            _pendingSkipWarmupStart = skipWarmup
                ? ResolveSkillTiming(_activeWeaponSkill?.WarmupDuration, WarmupDuration)
                : -1f;

            // 提前解析当前武器技能，以便应用 timing 覆盖（冲刺攻击优先技能 Dash 阶段配置）
            _activeWeaponSkill = Player.WeaponSkillController?.GetPrimarySkillDefinition();
            _activeFallImmunities = (_resolvedDashAttack
                ? _activeWeaponSkill?.DashAttackFallImmunities
                : _activeWeaponSkill?.AttackFallImmunities) ?? WeaponAttackFallPhases.None;
            _effectiveWarmup   = ResolveDashOrSkillDuration(_activeWeaponSkill?.DashWarmupDuration, _activeWeaponSkill?.WarmupDuration, WarmupDuration);
            _effectiveActive   = ResolveDashOrSkillDuration(_activeWeaponSkill?.DashActiveDuration, _activeWeaponSkill?.ActiveDuration, ActiveDuration);
            _effectiveRecovery = ResolveDashOrSkillDuration(_activeWeaponSkill?.DashRecoveryDuration, _activeWeaponSkill?.RecoveryDuration, RecoveryDuration);

            // 阶段时长按动画速度调整：速度越慢，阶段越久，确保动画完整播放
            // 全局攻速倍率与武器技能速度相乘，空手（无技能）时 1f × 倍率同样生效；冲刺攻击优先技能 Dash 动画速度
            float globalSpeed = Mathf.Max(Player.AttackSpeedMultiplier, 0.01f);
            float warmupSpeed = ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashWarmupAnimationSpeed ?? -1f, _activeWeaponSkill?.WarmupAnimationSpeed ?? 1f) * globalSpeed;
            float activeSpeed = ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashActiveAnimationSpeed ?? -1f, _activeWeaponSkill?.ActiveAnimationSpeed ?? 1f) * globalSpeed;
            float recoverySpeed = ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashRecoveryAnimationSpeed ?? -1f, _activeWeaponSkill?.RecoveryAnimationSpeed ?? 1f) * globalSpeed;
            _effectiveWarmup   /= Mathf.Max(warmupSpeed, 0.01f);
            _effectiveActive   /= Mathf.Max(activeSpeed, 0.01f);
            _effectiveRecovery /= Mathf.Max(recoverySpeed, 0.01f);

            // 先进入 Warmup 阶段，再启动攻击
            // 这样可以确保当动画 hit 事件触发时，IsRunning 已经是 true
            // Recovery 打断重启且子类要求时，跳过 Warmup 直接进入 Active（伤害立即判定）
            SetPhase(skipWarmup ? AttackPhase.Active : AttackPhase.Warmup);
            OnAttackStarted();
            ApplyPhaseAnimationSpeed(skipWarmup ? AttackPhase.Active : AttackPhase.Warmup);

            if (ConsumeResourceOnStart)
            {
                ConsumeResources();
            }

            return true;
        }

        public void Cancel(bool clearCooldown = false)
        {
            _spineHitWindowActive = false;
            _spineAttackAnimationName = string.Empty;
            _activeWeaponSkill = null;
            _activeFallImmunities = WeaponAttackFallPhases.None;
            _pendingSkipWarmupStart = -1f;

            if (clearCooldown)
            {
                _cooldownTimer = 0f;
                Player.AttackTimer = 0f;
            }

            if (_phase != AttackPhase.Idle)
            {
                SetPhase(AttackPhase.Idle);
            }
        }

        protected virtual bool CanStart(bool checkInput, bool allowDuringCooldown = false)
        {
            if (Player == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (!allowDuringCooldown && Player.AttackTimer > 0f) return false;

            if (!IsWeaponRequirementSatisfied())
            {
                return false;
            }

            if (checkInput && !IsInputTriggered())
            {
                return false;
            }

            if (!HasRequiredResources())
            {
                return false;
            }

            if (RequiresTargetInRange && !HasValidTarget())
            {
                return false;
            }

            return MeetsCustomConditions();
        }

        protected virtual bool HasRequiredResources()
        {
            if (!RequiresResource && string.IsNullOrEmpty(RequiredItemId)) return true;
            return EvaluateCustomRequirement();
        }

        protected virtual bool EvaluateCustomRequirement() => true;

        private string ResolveCurrentWeaponItemId()
        {
            if (Player.InventoryComponent != null)
            {
                var activeWeapon = Player.InventoryComponent.GetActiveCombatWeaponDefinition();
                if (activeWeapon != null && !string.Equals(activeWeapon.ItemId, "empty_item", StringComparison.OrdinalIgnoreCase))
                {
                    return activeWeapon.ItemId;
                }
            }

            if (Player.LeftHandItem != null && !string.Equals(Player.LeftHandItem.ItemId, "empty_item", StringComparison.OrdinalIgnoreCase))
            {
                return Player.LeftHandItem.ItemId;
            }

            return string.Empty;
        }

        protected virtual bool HasValidTarget()
        {
            if (AttackArea != null)
            {
                var bodies = AttackArea.GetOverlappingBodies();
                return bodies.Count > 0;
            }

            return true;
        }

        protected virtual bool MeetsCustomConditions() => true;

        /// <summary>Recovery 期间是否允许输入打断重启连击。默认允许；
        /// 返回 false 时不切断后摇，当前攻击的 Recovery 自然播放完毕（如电量耗尽时）。</summary>
        protected virtual bool CanCancelRecoveryForRestart() => true;

        protected virtual void ConsumeResources() { }

        protected virtual bool IsInputTriggered()
        {
            if (TriggerActions.Count == 0)
            {
                return true;
            }

            var activeSkill = Player.WeaponSkillController?.GetPrimarySkillDefinition();
            bool holdAllowed = AllowHoldInput || activeSkill == null || activeSkill.AllowHoldContinuousAttack;

            foreach (var action in TriggerActions)
            {
                // 走仲裁器：同键长短按分流（攻击为短按动作时延迟到松开确认；
                // 长按激活时 hold 被屏蔽，避免同键长按连击）
                if (Player.IsActionJustPressedArbitrated(action))
                {
                    return true;
                }

                if (holdAllowed && Player.IsActionHeldArbitrated(action))
                {
                    return true;
                }
            }

            return false;
        }


        /// </summary>
        private static float ResolveSkillTiming(float? skillOverride, float templateDefault)
        {
            return (skillOverride.HasValue && skillOverride.Value >= 0f) ? skillOverride.Value : templateDefault;
        }

        private void ApplyPhaseAnimationSpeed(AttackPhase phase)
        {
            float globalSpeed = Mathf.Max(Player.AttackSpeedMultiplier, 0.01f);
            float speed = phase switch
            {
                AttackPhase.Warmup => ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashWarmupAnimationSpeed ?? -1f, _activeWeaponSkill?.WarmupAnimationSpeed ?? 1f) * globalSpeed,
                AttackPhase.Active => ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashActiveAnimationSpeed ?? -1f, _activeWeaponSkill?.ActiveAnimationSpeed ?? 1f) * globalSpeed,
                AttackPhase.Recovery => ResolveDashOrSkillSpeed(_activeWeaponSkill?.DashRecoveryAnimationSpeed ?? -1f, _activeWeaponSkill?.RecoveryAnimationSpeed ?? 1f) * globalSpeed,
                _ => 1f * globalSpeed
            };

            if (Player is MainCharacter mainChar)
            {
                mainChar.SetSpineAnimationSpeed(speed);
            }
            else if (Player.AnimPlayer != null)
            {
                Player.AnimPlayer.SpeedScale = speed;
            }
        }

        private void ResetAnimationSpeed()
        {
            if (Player is MainCharacter mainChar)
            {
                mainChar.SetSpineAnimationSpeed(1f);
            }
            else if (Player.AnimPlayer != null)
            {
                Player.AnimPlayer.SpeedScale = 1f;
            }
        }

        protected virtual void OnAttackStarted()
        {
            if (EnableDashMovement && ShouldStartDashInWarmup)
            {
                StartDashMovement();
            }

            ApplyAttackAreaMaskOverride();
            // _activeWeaponSkill 已在 TryStart() 中解析，此处无需重复赋值
            ShowCurrentHitboxDebug(_activeWeaponSkill);
            _resolvedAnimationName = ResolveAnimationName(_activeWeaponSkill);
            EnsureSpineHitSupport();
            _spineAttackAnimationName = _resolvedAnimationName;
            // 在播放动画前就启用 Spine 事件窗口，防止动画的第一个 hit 事件被错过
            _spineHitWindowActive = ShouldUseSpineHitEvents();
            _currentHitStep = 1;  // 重置段数计数器
            CurrentAttackHitStep = 1;  // 重置静态段数
            CurrentAttackRoundId++;  // 新攻击回合：连击/段数效果按此隔离回合
            _resolvedHitSteps.Clear();  // 新攻击回合：hit 段去重重置（每攻击每段最多一次伤害）

            // 如果是 MainCharacter，使用 Spine 动画
            if (Player is MainCharacter mainChar)
            {
                if (!string.IsNullOrEmpty(_resolvedAnimationName))
                {
                    if (_pendingSkipWarmupStart >= 0f)
                    {
                        // 跳过 Warmup：动画从 Warmup 结束处跳帧播放
                        mainChar.PlaySpineAnimationFrom(_resolvedAnimationName, _pendingSkipWarmupStart, false);
                    }
                    else
                    {
                        mainChar.PlaySpineAnimation(_resolvedAnimationName, false);
                    }
                }
            }
            // 否则使用 AnimationPlayer
            else if (!string.IsNullOrEmpty(_resolvedAnimationName) && Player.AnimPlayer != null)
            {
                if (RestartAnimationOnLoop || !Player.AnimPlayer.IsPlaying())
                {
                    Player.AnimPlayer.Play(_resolvedAnimationName);
                }
            }
        }

        private string ResolveAnimationName(WeaponSkillDefinition? primarySkill)
        {
            // 冲刺攻击动画最高优先（冲刺攻击时使用技能 Dash 专属动画）
            if (ShouldUseDashMovement() && !string.IsNullOrWhiteSpace(_activeWeaponSkill?.DashAnimationName))
            {
                return _activeWeaponSkill.DashAnimationName;
            }

            if (!UseEquippedWeaponSkillAnimation)
            {
                return AnimationName;
            }

            if (primarySkill == null)
            {
                return AnimationName;
            }

            if (!string.IsNullOrWhiteSpace(primarySkill.AnimationName))
            {
                return primarySkill.AnimationName;
            }

            if (primarySkill.UseDefaultAttackAnimationFallback)
            {
                return AnimationName;
            }

            return string.Empty;
        }

        private void ShowCurrentHitboxDebug(WeaponSkillDefinition? skill)
        {
            if (skill == null || !skill.ShowHitboxDebug)
            {
                return;
            }

            Area2D? area = Player.ResolveAttackAreaForHitDetection();
            if (area == null)
            {
                return;
            }

            var collisionShape = ResolveCollisionShape(area);
            if (collisionShape == null || collisionShape.Shape == null)
            {
                return;
            }

            ShowWeaponHitboxDebug(skill, collisionShape, logOnce: true);
        }

        private void RefreshCurrentHitboxDebug()
        {
            if (_phase == AttackPhase.Idle)
            {
                return;
            }

            if (_activeWeaponSkill == null || !_activeWeaponSkill.ShowHitboxDebug)
            {
                return;
            }

            Area2D? area = Player.ResolveAttackAreaForHitDetection();
            if (area == null)
            {
                return;
            }

            var collisionShape = ResolveCollisionShape(area);
            if (collisionShape == null || collisionShape.Shape == null)
            {
                return;
            }

            ShowWeaponHitboxDebug(_activeWeaponSkill, collisionShape, logOnce: false);
        }

        private void ShowWeaponHitboxDebug(WeaponSkillDefinition skill, CollisionShape2D collisionShape, bool logOnce)
        {
            if (!skill.ShowHitboxDebug)
            {
                return;
            }

            EnsureHitboxDebugDrawer();
            _hitboxDebugDrawer?.ShowFromCollisionShape(
                collisionShape,
                skill.HitboxDebugColor,
                skill.HitboxDebugLineWidth,
                skill.HitboxDebugDuration
            );

            
        }

        private void EnsureHitboxDebugDrawer()
        {
            if (_hitboxDebugDrawer != null && GodotObject.IsInstanceValid(_hitboxDebugDrawer))
            {
                return;
            }

            if (Player == null || !Player.IsInsideTree())
            {
                return;
            }

            _hitboxDebugDrawer = new AttackHitboxDebugDrawer
            {
                Name = "AttackHitboxDebugDrawer",
                ZIndex = 9999,
                TopLevel = true,
                Visible = false
            };

            var host = Player.GetTree().CurrentScene ?? Player.GetTree().Root;
            host.AddChild(_hitboxDebugDrawer);
        }

        private static CollisionShape2D? ResolveCollisionShape(Area2D area)
        {
            var direct = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (direct != null)
            {
                return direct;
            }

            foreach (Node child in area.GetChildren())
            {
                if (child is CollisionShape2D shape)
                {
                    return shape;
                }
            }

            return null;
        }

        protected virtual void OnWarmupStarted()
        {
            SpawnSkillFx(EffectSpawnTiming.OnWarmup);
            Player.Velocity = Vector2.Zero;
        }

        /// <summary>前后移动动画未配置时跳过方向动画切换（回退默认攻击动画——OnAttackStarted 已播放，同一动画仅移动）。
        /// 注意：移动接管（UpdateDirectionalMoveVelocity）仍生效，只跳过动画切换。</summary>
        private bool ShouldSkipDirectionalAnimation()
            => string.IsNullOrWhiteSpace(ForwardAnimationName) && string.IsNullOrWhiteSpace(BackwardAnimationName);

        protected virtual void OnActivePhase()
        {
            SpawnSkillFx(EffectSpawnTiming.OnActive);
            // Warmup 结束（进 Active）：销毁绑定 OnWarmupEnd 的特效（如蓄力预警出手即消失）
            DestroyBoundSkillFx(EffectLifecycleBinding.OnWarmupEnd);

            if (EnableDashMovement && !ShouldStartDashInWarmup)
            {
                StartDashMovement();
            }

            if (EnableDirectionalMovement)
            {
                // 方向移动：Active 开始接管方向动画 + 位移
                _directionalActive = true;
                _lastDirectionalAnimation = string.Empty;
                // fwd/bwd 为空时不能调用 PlayDirectional：空动画名 set_animation 会破坏攻击动画 track，
                // 后续 hit 帧（时间轴后段）永久丢失——只保留移动接管
                if (!ShouldSkipDirectionalAnimation())
                    PlayDirectional(ResolveDirectionalAnimation());
            }

            if (ShouldUseSpineHitEvents())
            {
                return;
            }

            PerformDefaultHitDetection();
        }

        protected virtual void OnRecoveryStarted()
        {
            SpawnSkillFx(EffectSpawnTiming.OnRecovery);
            // Active 结束（进 Recovery）：销毁绑定 OnActiveEnd 的特效（如挥击光效收招即消失）
            DestroyBoundSkillFx(EffectLifecycleBinding.OnActiveEnd);

            // 拳套策略：Active 结束后应用记录的朝向翻转——转身叠加在收招上，
            // 不影响当段打击方向；连击重启时下一段自动朝新方向
            if (_pendingFacingFlip && FacingPolicy == DirectionalFacingPolicy.FlipOnBackward && Player != null)
            {
                _pendingFacingFlip = false;
                Player.FlipFacing(!Player.FacingRight);
            }

            // 位移清理/接管用 EnableDashMovement（所有来源都有位移——惯性衰减或 Brawl 固定冲刺）；
            // Dash 动画/阶段/速度才用 ShouldUseDashMovement（仅 Dash 来源）
            if (EnableDashMovement)
            {
                // 新衰减窗口覆盖 Recovery：衰减持续到窗口结束（保持 dashing，滑行关闭）
                if (_resolvedDashAttack
                    && _resolvedDecayWindow is DashAttackDecayWindow.Recovery
                        or DashAttackDecayWindow.ActiveRecovery
                        or DashAttackDecayWindow.FullAttack)
                {
                    _isSliding = false;
                    return;
                }

                // 冲刺模式：Active 前冲 → Recovery 滑行（速度由 OnTick 接管）
                _isDashing = false;
                _isSliding = RecoverySpeed > 0f;
                return;
            }

            Player.Velocity = Player.Velocity.MoveToward(Vector2.Zero, Player.Speed);
        }

        protected virtual void OnAttackFinished()
        {
            // 冲刺攻击专属效果回收：DashEffects 只在冲刺攻击期间生效（攻击结束/被打断时移除），
            // 防止跨攻击泄漏到普通攻击（普通 Effects 的常驻被动语义不变）
            Player.WeaponSkillController?.CleanupDashAttackEffects();

            // 攻击结束（正常回 Idle 或被打断）：销毁全部剩余绑定技能特效（含 OnRecoveryEnd）
            DestroyAllBoundSkillFx();

            RestoreAttackAreaMask();
            // 清理用 EnableDashMovement（所有来源）——否则 Brawl 非 Dash 来源碰敌后 _dashDisabled 残留，
            // 下次攻击冲刺永久失效
            if (EnableDashMovement)
            {
                _isDashing = false;
                _isSliding = false;
                _dashDisabled = false;
                // 保留 _currentDashSpeed：连击时继承上一段衰减结果（自然延续，不回到初始速度）
                Player.Velocity = Vector2.Zero;
            }
            if (EnableDirectionalMovement)
            {
                // 攻击结束/被打断后停止接管位移，Velocity 归零
                _directionalActive = false;
                Player.Velocity = Vector2.Zero;
            }
            // 未应用（攻击被中断、未进入 Recovery）的待翻转标记作废，防止泄漏到下一次攻击
            _pendingFacingFlip = false;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  技能阶段特效生成（与 EnemyAttackTemplate.SpawnEffectAtEnemy 同机制）
        //  数据源 = 当前武器技能的 AttackFxEntries / DashAttackFxEntries（冲刺分流），
        //  由四个阶段 hook 按时机过滤驱动；LifecycleBinding 特效随阶段结束销毁。
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>在攻击阶段生成技能配置的场景特效（子弹、场地破坏等）。按 per-entry SpawnTiming 过滤。</summary>
        private void SpawnSkillFx(EffectSpawnTiming timing)
        {
            var skill = Player?.WeaponSkillController?.GetPrimarySkillDefinition();
            if (skill == null || Player == null) return;

            // 冲刺分流：DashAttackFxEntries 非空时用专属列表，否则回退普通列表（与 DashEffects 同语义）
            var entries = _resolvedDashAttack && skill.DashAttackFxEntries.Count > 0
                ? skill.DashAttackFxEntries
                : skill.AttackFxEntries;

            foreach (var entry in entries)
            {
                if (entry == null) continue;
                if (entry.ResolveTiming(skill.AttackFxSpawnTiming) != timing) continue;
                SpawnSingleSkillFx(entry, skill);
            }
        }

        private void SpawnSingleSkillFx(AttackEffectEntry entry, WeaponSkillDefinition skill)
        {
            var scene = entry.Scene;
            if (scene == null) return;

            // 生成时检测（防同类特效叠加）：场上已存在该组存活特效 → 跳过本次生成
            if (!string.IsNullOrEmpty(entry.UniqueGroup) && IsSkillFxGroupActive(entry.UniqueGroup))
            {
                GD.Print($"[{AttackId}] 场上已存在特效组 {entry.UniqueGroup}，跳过生成");
                return;
            }

            try
            {
                var effect = scene.Instantiate();

                entry.ApplyOverrides(effect);
                // 唯一性组标记：供"场上是否已有该特效"检测（全局共享）
                if (!string.IsNullOrEmpty(entry.UniqueGroup))
                    effect.AddToGroup(entry.UniqueGroup);

                // per-entry 配置优先，未配置回退技能级字段。
                // NodePath 先按攻击模板自身解析，失败按玩家根兜底（兼容两种配置习惯）
                Vector2 entryOffset = entry.ResolveOffset(skill.AttackFxOffset);
                Marker2D[] entryMarkers = entry.ResolveMarkers(this, Player, System.Array.Empty<Marker2D>());
                bool entryFlip = entry.ResolveFlip(skill.AttackFxFlipWithFacing);

                // 跟随类特效（IFollowAnchor）：生成位置必须与跟随位置一致，否则首帧跳变
                bool isFollowFx = effect is Kuros.Fx.IFollowAnchor;

                Vector2 adjustedOffset = entryOffset;
                if (!Player!.FacingRight && !isFollowFx && entryOffset.X != 0)
                    adjustedOffset.X = -entryOffset.X;

                Node2D? usedMarker = null;
                Vector2 basePos;
                if (entryMarkers.Length > 0)
                {
                    int idx = _skillFxMarkerIndex % entryMarkers.Length;
                    var marker = entryMarkers[idx];
                    if (marker != null && GodotObject.IsInstanceValid(marker))
                    {
                        Vector2 rel = marker.GlobalPosition - Player.GlobalPosition;
                        if (!Player.FacingRight && !isFollowFx) rel.X = -rel.X;
                        basePos = Player.GlobalPosition + rel;
                        usedMarker = marker;
                    }
                    else
                    {
                        basePos = Player.GlobalPosition;
                    }
                    _skillFxMarkerIndex++;
                }
                else
                {
                    basePos = Player.GlobalPosition;
                }

                Vector2 spawnPos = basePos + adjustedOffset;

                if (effect is Node2D node2D)
                {
                    if (node2D is Kuros.Fx.IFacingDirectional facing)
                        facing.FacingRight = Player.FacingRight;

                    if (entryFlip && !Player.FacingRight)
                        node2D.Scale = new Vector2(-node2D.Scale.X, node2D.Scale.Y);

                    if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
                        attackerProvider.Attacker = Player;

                    // 通用：跟随类特效注入"产生它的锚点节点"（实际使用的 marker，无则玩家根）
                    if (node2D is Kuros.Fx.IFollowAnchor followAnchor)
                        followAnchor.FollowAnchor = usedMarker ?? Player;

                    Player.GetParent()?.AddChild(node2D);
                    node2D.GlobalPosition = spawnPos;

                    // 生命周期绑定：对应阶段结束时销毁（适配 Hold 挂起等时长不固定阶段）
                    if (entry.LifecycleBinding != EffectLifecycleBinding.None)
                        _boundSkillFx.Add((node2D, entry.LifecycleBinding));
                }
                else if (effect is Kuros.Core.Effects.ActorEffect actorEffect)
                {
                    if (Player.EffectController != null)
                        Player.ApplyEffect(actorEffect);
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
                GD.PushWarning($"[{AttackId}] 无法生成技能特效: {ex.Message}");
            }
        }

        /// <summary>场上（全局）是否存在指定组的存活特效实例。</summary>
        private bool IsSkillFxGroupActive(string group)
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

        /// <summary>销毁指定绑定类型的特效（阶段切换时）。</summary>
        private void DestroyBoundSkillFx(EffectLifecycleBinding binding)
        {
            for (int i = _boundSkillFx.Count - 1; i >= 0; i--)
            {
                if (_boundSkillFx[i].binding != binding) continue;
                var fx = _boundSkillFx[i].fx;
                if (GodotObject.IsInstanceValid(fx)) fx.QueueFree();
                _boundSkillFx.RemoveAt(i);
            }
        }

        /// <summary>销毁全部绑定特效（攻击结束/被打断兜底）。</summary>
        private void DestroyAllBoundSkillFx()
        {
            foreach (var (fx, _) in _boundSkillFx)
            {
                if (GodotObject.IsInstanceValid(fx))
                    fx.QueueFree();
            }
            _boundSkillFx.Clear();
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
            if (AttackArea == null) return;
            uint factionMask = BuildFactionMask();
            if (factionMask == 0) return;

            _cachedAttackAreaMask = AttackArea.CollisionMask;
            AttackArea.CollisionMask |= factionMask;
            _hasAttackAreaMaskOverride = true;
        }

        private void RestoreAttackAreaMask()
        {
            if (!_hasAttackAreaMaskOverride || AttackArea == null) return;

            AttackArea.CollisionMask = _cachedAttackAreaMask;
            _hasAttackAreaMaskOverride = false;
        }

        private void SetPhase(AttackPhase phase)
        {
            _phase = phase;
            switch (phase)
            {
                case AttackPhase.Idle:
                    _phaseTimer = 0f;
                    _hitWindowActive = false;
                    _spineHitWindowActive = false;
                    _spineAttackAnimationName = string.Empty;
                    ResetAnimationSpeed();
                    OnAttackFinished();
                    break;
                case AttackPhase.Warmup:
                    _phaseTimer = _effectiveWarmup;
                    // 在 Warmup 阶段也启用 _hitWindowActive，因为第一段 hit 可能在 Warmup 期间触发
                    _hitWindowActive = HitEffectScene != null;
                    _spineHitWindowActive = ShouldUseSpineHitEvents();
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = _effectiveActive;
                    // 只要有特效场景，就保持 _hitWindowActive=true（不管是否使用 Spine 事件）
                    _hitWindowActive = HitEffectScene != null;
                    _spineHitWindowActive = ShouldUseSpineHitEvents();
                    ApplyPhaseAnimationSpeed(AttackPhase.Active);
                    if (ShouldUseSpineHitEvents())
                    {
                        OnActivePhase();
                    }
                    else
                    {
                        OnActivePhase();
                    }
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = _effectiveRecovery;
                    _hitWindowActive = false;
                    _spineHitWindowActive = false;
                    ApplyPhaseAnimationSpeed(AttackPhase.Recovery);
                    OnRecoveryStarted();
                    break;
            }

            if (_phase != AttackPhase.Idle && _phaseTimer <= 0f)
            {
                AdvancePhase();
            }
        }

        private void AdvancePhase()
        {
            switch (_phase)
            {
                case AttackPhase.Warmup:
                    SetPhase(AttackPhase.Active);
                    break;
                case AttackPhase.Active:
                    SetPhase(AttackPhase.Recovery);
                    break;
                case AttackPhase.Recovery:
                    SetPhase(AttackPhase.Idle);
                    break;
            }
        }

        protected virtual void PerformDefaultHitDetection()
        {
            if (Player == null) return;

            // 命中帧：Spine hit 事件（OnSpineHitReceived）与非 Spine 回退路径都汇聚到此处，
            // 作为 OnAnimationHit 时机的生成点（多段攻击每段命中各生成一次）
            SpawnSkillFx(EffectSpawnTiming.OnAnimationHit);

            float originalDamage = Player.AttackDamage;
            TargetableFactions originalFactions = Player.CurrentAttackTargetableFactions;
            try
            {
                Player.AttackDamage = DamageOverride;
                Player.CurrentAttackTargetableFactions = TargetableFactions;
                Player.PerformAttackCheck();
            }
            finally
            {
                Player.AttackDamage = originalDamage;
                Player.CurrentAttackTargetableFactions = originalFactions;
            }
        }

        private void InitializeSpineHitSupport()
        {
            UnsubscribeSpineHitSignal();

            EnsureSpineHitSupport();
        }

        private void EnsureSpineHitSupport()
        {
            if (_spineHitSubscribed)
            {
                return;
            }

            if (Player is not MainCharacter mainChar)
            {
                return;
            }

            _spineControllerNode = mainChar.GetSpineControllerNode();
            if (_spineControllerNode == null || !_spineControllerNode.HasSignal("hit_received"))
            {
                _spineControllerNode = null;
                return;
            }

            _spineHitCallable = Callable.From<int, string>(OnSpineHitReceived);
            _spineControllerNode.Connect("hit_received", _spineHitCallable);
            _spineHitSubscribed = true;
        }

        private void UnsubscribeSpineHitSignal()
        {
            if (!_spineHitSubscribed || _spineControllerNode == null)
            {
                _spineHitSubscribed = false;
                _spineControllerNode = null;
                return;
            }

            if (_spineControllerNode.IsConnected("hit_received", _spineHitCallable))
            {
                _spineControllerNode.Disconnect("hit_received", _spineHitCallable);
            }

            _spineHitSubscribed = false;
            _spineControllerNode = null;
        }

        private bool ShouldUseSpineHitEvents()
        {
            EnsureSpineHitSupport();
            return UseSpineHitEvents && Player is MainCharacter && _spineControllerNode != null;
        }

        private void OnSpineHitReceived(int hitStep, string animationName)
        {
            if (!_spineHitWindowActive || !IsRunning)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_spineAttackAnimationName) && !string.Equals(animationName, _spineAttackAnimationName, StringComparison.Ordinal))
            {
                return;
            }

            // 同段只结算一次：方向切换（fwd/bwd 互切）是动画变体——来回快速切换时新动画的 hit 帧会反复触发，
            // 去重保证每次攻击每段最多一次伤害
            if (!_resolvedHitSteps.Add(hitStep))
            {
                return;
            }

            _currentHitStep = hitStep;  // 记录当前段数
            CurrentAttackHitStep = hitStep;  // 更新静态属性供其他系统访问
            PerformDefaultHitDetection();
        }

        private void InitializeHitEffectSupport()
        {
            if (HitEffectScene == null || Player == null)
            {
                if (_hitEffectSubscribed)
                {
                    DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                    _hitEffectSubscribed = false;
                }
                _hitEffectParent = null;
                return;
            }

            _hitEffectParent = ResolveHitEffectParent();
            _customAnchor = ResolveCustomAnchor();

            if (!_hitEffectSubscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _hitEffectSubscribed = true;
            }
        }

        private Node? ResolveHitEffectParent()
        {
            if (!HitEffectParentPath.IsEmpty && Player != null)
            {
                return Player.GetNodeOrNull<Node>(HitEffectParentPath);
            }

            return Player?.GetParent();
        }

        private Node2D? ResolveCustomAnchor()
        {
            if (!HitEffectAnchorPath.IsEmpty && Player != null)
            {
                return Player.GetNodeOrNull<Node2D>(HitEffectAnchorPath);
            }

            return null;
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            // 只响应直接攻击来源，过滤 spike 区域伤害等间接伤害，避免错误触发命中特效
            if (source != DamageSource.DirectAttack)
            {
                return;
            }

            if (!_hitWindowActive || HitEffectScene == null)
            {
                return;
            }

            if (attacker != Player)
            {
                return;
            }

            // target 可能为 null（击中非 GameActor 对象如 Gate），此时在 Player 或 AttackArea 位置创建特效
            Node2D? targetNode = target as Node2D;
            if (targetNode == null)
            {
                // target 为 null 或不是 Node2D，使用备用位置
                targetNode = Player;
            }

            if (targetNode == null)
            {
                return;
            }

            var parent = GetValidHitEffectParent();
            if (parent == null)
            {
                return;
            }

            var instance = HitEffectScene.Instantiate();
            if (instance is Node2D effectNode)
            {
                parent.AddChild(effectNode);
                if (HitEffectForceTopLevel)
                {
                    effectNode.TopLevel = true;
                }
                effectNode.ZAsRelative = false;
                effectNode.ZIndex = HitEffectZIndex;
                effectNode.GlobalPosition = GetHitEffectPosition(targetNode);
                ApplyFacingToEffect(effectNode);
                TriggerHitEffect(effectNode);
            }
            else
            {
                instance.QueueFree();
            }
        }

        private Node? GetValidHitEffectParent()
        {
            if (_hitEffectParent != null && _hitEffectParent.IsInsideTree())
            {
                return _hitEffectParent;
            }

            _hitEffectParent = ResolveHitEffectParent();
            return _hitEffectParent;
        }

        private Node2D? GetValidCustomAnchor()
        {
            if (_customAnchor != null && _customAnchor.IsInsideTree())
            {
                return _customAnchor;
            }

            _customAnchor = ResolveCustomAnchor();
            return _customAnchor;
        }

        private Vector2 GetHitEffectPosition(Node2D targetNode)
        {
            Vector2 basePosition = HitEffectAnchorMode switch
            {
                HitEffectAnchor.Player => Player?.GlobalPosition ?? targetNode.GlobalPosition,
                HitEffectAnchor.AttackArea => AttackArea?.GlobalPosition ?? targetNode.GlobalPosition,
                HitEffectAnchor.CustomNode => GetValidCustomAnchor()?.GlobalPosition ?? targetNode.GlobalPosition,
                _ => targetNode.GlobalPosition
            };

            Vector2 offset = HitEffectLocalOffset;
            if (HitEffectMirrorFacing && Player != null)
            {
                float sign = Player.FacingRight ? -1f : 1f;
                offset.X *= sign;
            }

            return basePosition + offset;
        }

        private void ApplyFacingToEffect(Node2D effectNode)
        {
            if (!HitEffectMirrorFacing || Player == null)
            {
                return;
            }

            float sign = Player.FacingRight ? -1f : 1f;
            Vector2 scale = effectNode.Scale;
            scale.X = Mathf.Abs(scale.X) * sign;
            effectNode.Scale = scale;
        }

        private void TriggerHitEffect(Node2D effectNode)
        {
            // 处理 AnimatedSprite2D
            if (effectNode is AnimatedSprite2D animSprite)
            {
                animSprite.Play();
            }
            
            if (effectNode.HasMethod("restart"))
            {
                effectNode.Call("restart");
            }

            if (effectNode.HasMethod("set_emitting"))
            {
                effectNode.Call("set_emitting", true);
            }
            else if (effectNode.HasMethod("set_emission_enabled"))
            {
                effectNode.Call("set_emission_enabled", true);
            }

            if (effectNode.HasSignal("animation_finished"))
            {
                var callable = Callable.From(() => effectNode.QueueFree());
                if (!effectNode.IsConnected("animation_finished", callable))
                {
                    effectNode.Connect("animation_finished", callable);
                }
            }
        }

        /// <summary>
        /// 装备武器时触发
        /// </summary>
        private void OnWeaponEquipped(ItemDefinition weapon)
        {
            // 只在装备了匹配的武器时才应用效果
            if (HasWeaponRequirement && !string.Equals(weapon.ItemId, RequiredItemId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 从武器定义的 EffectEntries 中获取并应用 OnEquip 效果
            ApplyEquipEffectsFromWeapon(weapon);
        }

        /// <summary>
        /// 卸下武器时触发
        /// </summary>
        private void OnWeaponUnequipped()
        {
            RemoveAllEquipEffects();
        }

        /// <summary>
        /// 应用所有装备时的效果
        /// </summary>
        private void ApplyEquipEffectsFromWeapon(ItemDefinition weapon)
        {
            if (weapon?.EffectEntries == null || Player?.EffectController == null)
            {
                return;
            }

            foreach (var effectEntry in weapon.EffectEntries)
            {
                if (effectEntry.Trigger != ItemEffectTrigger.OnEquip)
                {
                    continue;
                }

                var effect = effectEntry.InstantiateEffect();
                if (effect != null)
                {
                    Player.EffectController.AddEffect(effect);
                    _appliedEquipEffects.Add(effect);
                }
            }
        }

        /// <summary>
        /// 移除所有已应用的装备效果
        /// </summary>
        private void RemoveAllEquipEffects()
        {
            if (Player?.EffectController == null)
            {
                return;
            }

            foreach (var effect in _appliedEquipEffects)
            {
                if (GodotObject.IsInstanceValid(effect))
                {
                    Player.EffectController.RemoveEffect(effect);
                }
            }

            _appliedEquipEffects.Clear();
        }
    }
}

