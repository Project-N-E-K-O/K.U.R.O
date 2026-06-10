using Godot;
using System.Collections.Generic;
using Kuros.Managers;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 检测范围内的友方血量，当某个友方血量低于阈值时进入该状态追踪并治疗，结束后回到 Walk。
    /// 优先级：血量比例最低者优先；比例相同时选最近的。
    ///
    /// 性能策略：
    ///   · 用 GetTree().GetNodesInGroup("enemies") 扫描，绕过 DetectionArea 碰撞层限制
    ///     （ControllerDetectionArea.collision_mask=4 只检测玩家，无法感知其他敌人）
    ///   · 每 ScanInterval 秒刷新"范围内友方"集合，同时订阅其 HealthChanged 做即时重算
    ///   · ShouldTriggerAssist() 只读缓存 bool，Idle/Walk 每帧调用无开销
    /// </summary>
    public partial class EnemyAssistState : EnemyState
    {
        [ExportCategory("Detection")]
        [Export(PropertyHint.Range, "100,5000,100")]
        public float ScanRadius { get; set; } = 1500f;  //

        [Export(PropertyHint.Range, "0.2,5,0.1")]
        public float ScanInterval { get; set; } = 1f;       

        [Export(PropertyHint.Range, "1,100,1")]
        public float HealthThresholdPercent { get; set; } = 50f;

        [ExportCategory("Healing")]
        [Export(PropertyHint.Range, "1,100,1")]
        public int HealPercent { get; set; } = 30;

        [Export(PropertyHint.Range, "50,2000,50")]
        public float ContactDistance { get; set; } = 200f;

        [Export(PropertyHint.Range, "0,10,0.1")]
        public float HealDelay { get; set; } = 0f;

        [Export(PropertyHint.Range, "50,2000,50")]
        public float AssistMoveSpeed { get; set; } = 300f;

        [ExportCategory("Use Cooldown")]
        [Export(PropertyHint.Range, "1,20,1")]
        public int UsesBeforeCooldown = 1; // 连续触发多少次后进入冷却

        [Export(PropertyHint.Range, "0.5,60,0.5")]
        public float CooldownAfterUses = 10.0f;

        // 当前在扫描范围内的友方集合（不含自身）
        private readonly HashSet<SampleEnemy> _nearbyAllies = new();

        private SampleEnemy? _cachedTarget;
        private bool _hasValidTarget;
        private float _scanTimer = 0f;
        private float _healDelayTimer;
        private bool _waitingToHeal;
        private int _useCount;
        private float _cooldownTimer;
        private NavigationAgent2D? _navAgent;

        // ─── 生命周期 ────────────────────────────────────────────────────────────

        public override void _Ready()
        {
            base._Ready();
            if (Engine.IsEditorHint()) return;
            CallDeferred(MethodName.RegisterAsBlockedState);
        }

        public override void _ExitTree()
        {
            UnsubscribeAll();
            base._ExitTree();
        }

        /// <summary>
        /// 找到 EnemyChaseMovement 组件，将 "Assist" 加入 BlockedStates，
        /// 防止追踪组件在 Assist 状态下把速度覆盖为朝玩家方向。
        /// </summary>
        private void RegisterAsBlockedState()
        {
            if (Enemy == null) return;
            foreach (var child in Enemy.GetChildren())
            {
                if (child is EnemyChaseMovement movement)
                {
                    var assistName = new StringName("Assist");
                    if (!movement.BlockedStates.Contains(assistName))
                        movement.BlockedStates.Add(assistName);
                    break;
                }
            }
        }

        // ─── 主循环：定期刷新范围内友方集合 ──────────────────────────────────────

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint() || Enemy == null) return;

            // 冷却倒计时（无论激活状态始终运行）
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
                if (_cooldownTimer <= 0f)
                {
                    _cooldownTimer = 0f;
                    _useCount = 0;
                }
            }

            _scanTimer -= (float)delta;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                RefreshNearbyAllies();
            }
        }

        /// <summary>
        /// 用 Group 扫描替代 DetectionArea 信号，与碰撞层完全解耦。
        /// 对新进入范围的敌人订阅 HealthChanged，对离开的取消订阅。
        /// </summary>
        private void RefreshNearbyAllies()
        {
            float radiusSq = ScanRadius * ScanRadius;
            var allEnemies = GetTree().GetNodesInGroup("enemies");

            // 找出本次在范围内的集合
            var currentSet = new HashSet<SampleEnemy>();
            foreach (var node in allEnemies)
            {
                if (node is not SampleEnemy other) continue;
                if (other == Enemy) continue;
                if (other.IsDead || other.IsDeathSequenceActive) continue;
                if (Enemy.GlobalPosition.DistanceSquaredTo(other.GlobalPosition) <= radiusSq)
                    currentSet.Add(other);
            }

            // 新进入范围 → 订阅 HealthChanged
            foreach (var e in currentSet)
            {
                if (_nearbyAllies.Add(e))
                    e.HealthChanged += OnAllyHealthChanged;
            }

            // 离开范围 → 取消订阅
            var toRemove = new List<SampleEnemy>();
            foreach (var e in _nearbyAllies)
            {
                if (!currentSet.Contains(e))
                    toRemove.Add(e);
            }
            foreach (var e in toRemove)
            {
                _nearbyAllies.Remove(e);
                if (GodotObject.IsInstanceValid(e))
                    e.HealthChanged -= OnAllyHealthChanged;
            }

            ReevaluateTarget();
        }

        private void UnsubscribeAll()
        {
            foreach (var e in _nearbyAllies)
            {
                if (GodotObject.IsInstanceValid(e))
                    e.HealthChanged -= OnAllyHealthChanged;
            }
            _nearbyAllies.Clear();
        }

        // ─── 事件：友方受伤时即时重算 ────────────────────────────────────────────

        private void OnAllyHealthChanged(int current, int max) => ReevaluateTarget();

        // ─── 目标评估 ────────────────────────────────────────────────────────────

        private void ReevaluateTarget()
        {
            if (Enemy == null) return;

            float threshold = HealthThresholdPercent / 100f;
            SampleEnemy? best = null;
            float bestRatio = float.MaxValue;
            float bestDist  = float.MaxValue;

            foreach (var other in _nearbyAllies)
            {
                if (!GodotObject.IsInstanceValid(other)) continue;
                if (other.IsDead || other.IsDeathSequenceActive) continue;
                if (other.MaxHealth <= 0) continue;

                float ratio = (float)other.CurrentHealth / other.MaxHealth;
                if (ratio >= threshold) continue;

                float dist = Enemy.GlobalPosition.DistanceTo(other.GlobalPosition);

                bool betterRatio     = ratio < bestRatio - 0.01f;
                bool sameRatioCloser = Mathf.Abs(ratio - bestRatio) <= 0.01f && dist < bestDist;
                if (betterRatio || sameRatioCloser)
                {
                    best      = other;
                    bestRatio = ratio;
                    bestDist  = dist;
                }
            }

            _cachedTarget   = best;
            _hasValidTarget = best != null;
        }

        // ─── 状态接口 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 供外部（Idle / Walk）查询是否应进入援助状态。只读缓存，零开销。
        /// </summary>
        public bool ShouldTriggerAssist() => _cooldownTimer <= 0f && _hasValidTarget && _cachedTarget != null;

        public override void Enter()
        {
            // 立刻做一次完整扫描，不等下一个 ScanInterval
            RefreshNearbyAllies();
            if (_cachedTarget == null)
            {
                ChangeState("Walk");
                return;
            }

            // 累积使用次数，达到上限触发冷却
            _useCount++;
            if (UsesBeforeCooldown > 0 && _useCount >= UsesBeforeCooldown)
                _cooldownTimer = Mathf.Max(CooldownAfterUses, 0f);

            _healDelayTimer = 0f;
            _waitingToHeal = false;
            _navAgent = Enemy.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");

            Enemy.AnimPlayer?.Play("animations/Walk");
        }

        public override void PhysicsUpdate(double delta)
        {
            if (_cachedTarget == null
                || !GodotObject.IsInstanceValid(_cachedTarget)
                || _cachedTarget.IsDead
                || _cachedTarget.IsDeathSequenceActive)
            {
                _cachedTarget   = null;
                _hasValidTarget = false;
                ChangeState("Walk");
                return;
            }

            float dist = Enemy.GlobalPosition.DistanceTo(_cachedTarget.GlobalPosition);
            if (dist <= ContactDistance)
            {
                if (HealDelay <= 0f)
                {
                    ApplyHeal();
                    return;
                }

                if (!_waitingToHeal)
                {
                    _waitingToHeal = true;
                    _healDelayTimer = HealDelay;
                }

                _healDelayTimer -= (float)delta;
                if (_healDelayTimer <= 0f)
                {
                    ApplyHeal();
                    return;
                }

                // 等待期间继续追踪，但不重置计时器
                MoveTowardTarget();
                return;
            }

            _waitingToHeal = false;
            _healDelayTimer = 0f;

            MoveTowardTarget();
        }

        private void MoveTowardTarget()
        {
            if (_cachedTarget == null) return;

            Vector2 dir;
            float speed = AssistMoveSpeed > 0f ? AssistMoveSpeed : Enemy.Speed;

            if (_navAgent != null)
            {
                if (_navAgent.TargetPosition.DistanceSquaredTo(_cachedTarget.GlobalPosition) > 100f)
                    _navAgent.TargetPosition = _cachedTarget.GlobalPosition;

                if (!_navAgent.IsNavigationFinished())
                {
                    Vector2 nextPoint = _navAgent.GetNextPathPosition();
                    dir = (nextPoint - Enemy.GlobalPosition).Normalized();
                }
                else
                {
                    dir = Enemy.GlobalPosition.DirectionTo(_cachedTarget.GlobalPosition);
                }
            }
            else
            {
                dir = Enemy.GlobalPosition.DirectionTo(_cachedTarget.GlobalPosition);
            }

            if (dir.IsZeroApprox()) return;

            Enemy.Velocity = dir * speed;
            if (dir.X != 0)
                Enemy.FlipFacing(dir.X > 0);
            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();
        }

        private void ApplyHeal()
        {
            if (_cachedTarget == null) return;

            int amount = Mathf.Max(1, Mathf.RoundToInt(_cachedTarget.MaxHealth * HealPercent / 100f));
            Vector2 healPosition = _cachedTarget.GlobalPosition;
            _cachedTarget.RestoreHealth(_cachedTarget.CurrentHealth + amount);
            FloatingDamageTextManager.Instance.ShowFloatingHealing(amount, healPosition, 0f);
            _cachedTarget = null;
            _hasValidTarget = false;
            _waitingToHeal = false;
            ChangeState("Walk");
        }
    }
}

