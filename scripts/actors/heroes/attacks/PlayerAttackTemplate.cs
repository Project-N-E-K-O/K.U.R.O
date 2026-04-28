using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
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
        [Export] public bool BufferInputUntilReady = true;

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.1f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.25f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float CooldownDuration = 0.6f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public float DamageOverride = 25.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public float AttackRange = 120.0f;
        [Export] public NodePath AttackAreaPath = new NodePath();

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

        private AttackPhase _phase = AttackPhase.Idle;
        private float _phaseTimer = 0f;
        private float _cooldownTimer = 0f;
        private bool _bufferedInput = false;
        private bool _hitEffectSubscribed = false;
        private bool _hitWindowActive = false;
        private Node? _hitEffectParent;
        private Node2D? _customAnchor;
        private Node? _spineControllerNode;
        private Callable _spineHitCallable;
        private bool _spineHitSubscribed = false;
        private bool _spineHitWindowActive = false;
        private string _spineAttackAnimationName = string.Empty;
        private string _resolvedAnimationName = string.Empty;
        private WeaponSkillDefinition? _activeWeaponSkill;
        private AttackHitboxDebugDrawer? _hitboxDebugDrawer;
        private int _currentHitStep = 1;  // 记录当前 Spine 动画段数（1-based）
        private float _weaponBaseDamage = 0f;  // 记录武器基础伤害（不含玩家基础伤害）
        private PlayerInventoryComponent? _inventoryComponent;
        private List<ActorEffect> _appliedEquipEffects = new();  // 已应用的装备效果
        private bool _equipEffectsSubscribed = false;  // 是否已订阅装备事件

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0f;

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

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0f)
            {
                AdvancePhase();
            }

            OnTick(delta);
            RefreshCurrentHitboxDebug();
        }

        protected virtual void OnTick(double delta) { }

        public bool TryStart(bool checkInput = true)
        {
            if (!CanStart(checkInput)) return false;

            _cooldownTimer = CooldownDuration;
            Player.AttackTimer = Mathf.Max(Player.AttackTimer, CooldownDuration);

            // 先进入 Warmup 阶段，再启动攻击
            // 这样可以确保当动画 hit 事件触发时，IsRunning 已经是 true
            SetPhase(AttackPhase.Warmup);
            OnAttackStarted();

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

        protected virtual bool CanStart(bool checkInput)
        {
            if (Player == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Player.AttackTimer > 0f) return false;

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

        protected virtual void ConsumeResources() { }

        protected virtual bool IsInputTriggered()
        {
            if (TriggerActions.Count == 0)
            {
                return true;
            }

            foreach (var action in TriggerActions)
            {
                if (Input.IsActionJustPressed(action))
                {
                    return true;
                }

                if (AllowHoldInput && Input.IsActionPressed(action))
                {
                    return true;
                }
            }

            if (BufferInputUntilReady && _bufferedInput)
            {
                _bufferedInput = false;
                return true;
            }

            return false;
        }

        public void BufferInput()
        {
            if (BufferInputUntilReady)
            {
                _bufferedInput = true;
            }
        }

        protected virtual void OnAttackStarted()
        {
            _activeWeaponSkill = Player.WeaponSkillController?.GetPrimarySkillDefinition();
            ShowCurrentHitboxDebug(_activeWeaponSkill);
            _resolvedAnimationName = ResolveAnimationName(_activeWeaponSkill);
            EnsureSpineHitSupport();
            _spineAttackAnimationName = _resolvedAnimationName;
            // 在播放动画前就启用 Spine 事件窗口，防止动画的第一个 hit 事件被错过
            _spineHitWindowActive = ShouldUseSpineHitEvents();
            _currentHitStep = 1;  // 重置段数计数器
            CurrentAttackHitStep = 1;  // 重置静态段数
            
            // 计算武器基础伤害：DamageOverride - Player.AttackDamage
            // 只有在第一段才应用玩家基础伤害和增伤效果
            _weaponBaseDamage = DamageOverride - Player.AttackDamage;
            if (_weaponBaseDamage < 0) _weaponBaseDamage = 0;

            // 如果是 MainCharacter，使用 Spine 动画
            if (Player is MainCharacter mainChar)
            {
                if (!string.IsNullOrEmpty(_resolvedAnimationName))
                {
                    mainChar.PlaySpineAnimation(_resolvedAnimationName, false);
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
            Player.Velocity = Vector2.Zero;
        }

        protected virtual void OnActivePhase()
        {
            if (ShouldUseSpineHitEvents())
            {
                return;
            }

            PerformDefaultHitDetection();
        }

        protected virtual void OnRecoveryStarted()
        {
            Player.Velocity = Player.Velocity.MoveToward(Vector2.Zero, Player.Speed);
        }

        protected virtual void OnAttackFinished() { }

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
                    OnAttackFinished();
                    break;
                case AttackPhase.Warmup:
                    _phaseTimer = WarmupDuration;
                    // 在 Warmup 阶段也启用 _hitWindowActive，因为第一段 hit 可能在 Warmup 期间触发
                    _hitWindowActive = HitEffectScene != null;
                    _spineHitWindowActive = ShouldUseSpineHitEvents();
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    // 只要有特效场景，就保持 _hitWindowActive=true（不管是否使用 Spine 事件）
                    _hitWindowActive = HitEffectScene != null;
                    _spineHitWindowActive = ShouldUseSpineHitEvents();
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
                    _phaseTimer = RecoveryDuration;
                    _hitWindowActive = false;
                    _spineHitWindowActive = false;
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

            float originalDamage = Player.AttackDamage;
            
            // 区分有武器和徒手的情况：
            // - 徒手（_weaponBaseDamage == 0）：所有段都应用完整伤害 DamageOverride
            // - 有武器（_weaponBaseDamage > 0）：第一段应用 DamageOverride，后续段仅应用武器伤害
            if (_weaponBaseDamage <= 0)
            {
                // 徒手攻击：所有段都应用完整伤害
                Player.AttackDamage = DamageOverride;
            }
            else if (_currentHitStep == 1)
            {
                // 有武器的第一段：应用完整伤害（基础伤害 + 武器伤害 + 增伤效果）
                Player.AttackDamage = DamageOverride;
            }
            else
            {
                // 有武器的后续段：仅应用武器伤害，避免基础伤害和增伤效果被多段武器放大
                Player.AttackDamage = _weaponBaseDamage;
            }

            Player.PerformAttackCheck();

            Player.AttackDamage = originalDamage;
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

            if (attacker != Player || target is not Node2D targetNode)
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

