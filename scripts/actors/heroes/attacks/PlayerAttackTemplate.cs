using System;
using Godot;
using Godot.Collections;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Events;
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

            InitializeHitEffectSupport();
            InitializeSpineHitSupport();

            OnInitialized();
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            if (_hitEffectSubscribed)
            {
                DamageEventBus.Unsubscribe(OnDamageResolved);
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
        }

        protected virtual void OnTick(double delta) { }

        public bool TryStart(bool checkInput = true)
        {
            if (!CanStart(checkInput)) return false;

            _cooldownTimer = CooldownDuration;
            Player.AttackTimer = Mathf.Max(Player.AttackTimer, CooldownDuration);

            OnAttackStarted();
            SetPhase(AttackPhase.Warmup);

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
                var quickBarStack = Player.InventoryComponent.GetSelectedQuickBarStack();
                if (quickBarStack != null && !quickBarStack.IsEmpty && quickBarStack.Item.ItemId != "empty_item")
                {
                    return quickBarStack.Item.ItemId;
                }

                var backpackStack = Player.InventoryComponent.GetSelectedBackpackStack();
                if (backpackStack != null && !backpackStack.IsEmpty && backpackStack.Item.ItemId != "empty_item")
                {
                    return backpackStack.Item.ItemId;
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
            _spineHitWindowActive = ShouldUseSpineHitEvents();

            // 如果是 MainCharacter，使用 Spine 动画
            if (Player is MainCharacter mainChar)
            {
                if (!string.IsNullOrEmpty(_resolvedAnimationName))
                {
                    GD.Print($"[{GetType().Name}] 播放攻击动画 (Spine): {_resolvedAnimationName}");
                    mainChar.PlaySpineAnimation(_resolvedAnimationName, false);
                }
                else
                {
                    GD.PushWarning($"[{GetType().Name}] AnimationName 为空，无法播放攻击动画");
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
            else
            {
                GD.PushWarning($"[{GetType().Name}] 无法播放攻击动画: AnimationName={_resolvedAnimationName}, AnimPlayer={Player.AnimPlayer}");
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

            ShowWeaponHitboxDebug(skill, collisionShape);
        }

        private void ShowWeaponHitboxDebug(WeaponSkillDefinition skill, CollisionShape2D collisionShape)
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

            GD.Print($"[{GetType().Name}] Hitbox Debug => Shape={collisionShape.Shape.GetType().Name}, Position={collisionShape.GlobalPosition}, Rotation={collisionShape.GlobalRotationDegrees}");
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
                    _hitWindowActive = false;
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    _hitWindowActive = HitEffectScene != null;
                    if (ShouldUseSpineHitEvents())
                    {
                        OnActivePhase();
                    }
                    else
                    {
                        try
                        {
                            OnActivePhase();
                        }
                        finally
                        {
                            _hitWindowActive = false;
                        }
                    }
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = RecoveryDuration;
                    _hitWindowActive = false;
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
            Player.AttackDamage = DamageOverride;

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
            GD.Print($"[{GetType().Name}] 已连接 Spine hit_received 信号");
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

            GD.Print($"[{GetType().Name}] Spine hit_received 触发伤害判定: 动画={animationName}, 段数={hitStep}");
            PerformDefaultHitDetection();
        }

        private void InitializeHitEffectSupport()
        {
            if (HitEffectScene == null || Player == null)
            {
                if (_hitEffectSubscribed)
                {
                    DamageEventBus.Unsubscribe(OnDamageResolved);
                    _hitEffectSubscribed = false;
                }
                _hitEffectParent = null;
                return;
            }

            _hitEffectParent = ResolveHitEffectParent();
            _customAnchor = ResolveCustomAnchor();

            if (!_hitEffectSubscribed)
            {
                DamageEventBus.Subscribe(OnDamageResolved);
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

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage)
        {
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
                GD.PushWarning($"[{GetType().Name}] 无法生成击打特效，未找到父节点。");
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
                GD.PushWarning($"[{GetType().Name}] HitEffectScene 需要是 Node2D 场景。");
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
                float sign = Player.FacingRight ? 1f : -1f;
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

            float sign = Player.FacingRight ? 1f : -1f;
            Vector2 scale = effectNode.Scale;
            scale.X = Mathf.Abs(scale.X) * sign;
            effectNode.Scale = scale;
        }

        private void TriggerHitEffect(Node2D effectNode)
        {
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

            if (effectNode.HasSignal("finished"))
            {
                var callable = Callable.From(() => effectNode.QueueFree());
                if (!effectNode.IsConnected("finished", callable))
                {
                    effectNode.Connect("finished", callable);
                }
            }
        }
    }
}

