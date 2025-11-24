using Godot;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 根据权重随机选择子攻击模板并触发。
    /// </summary>
    public partial class EnemyAttackController : EnemyAttackTemplate
    {
        [Export] public NodePath PlayerDetectionAreaPath = new NodePath();

        private const float ControllerActiveDuration = 9999f;
        private readonly List<Entry> _entries = new();
        private EnemyAttackTemplate? _currentAttack;
        private EnemyAttackTemplate? _queuedAttack;
        private Area2D? _playerDetectionArea;
        private string? _pendingQueueReason;
        private bool _playerInside;

        public EnemyAttackController()
        {
            WarmupDuration = 0f;
            ActiveDuration = ControllerActiveDuration;
            RecoveryDuration = 0f;
            CooldownDuration = 0f;
        }

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _entries.Clear();
            _playerDetectionArea = ResolveArea(PlayerDetectionAreaPath, AttackArea);
            if (_playerDetectionArea != null)
            {
                _playerDetectionArea.BodyEntered += OnDetectionAreaBodyEntered;
                _playerDetectionArea.BodyExited += OnDetectionAreaBodyExited;
            }

            foreach (Node child in GetChildren())
            {
                if (child is EnemyAttackTemplate template)
                {
                    template.Initialize(enemy);
                    float weight = 1f;
                    if (template.HasMeta("attack_weight"))
                    {
                        Variant meta = template.GetMeta("attack_weight");
                        if (meta.VariantType == Variant.Type.Float || meta.VariantType == Variant.Type.Int)
                        {
                            weight = (float)meta;
                        }
                    }

                    _entries.Add(new Entry { Template = template, Weight = Mathf.Max(weight, 0f) });
                }
            }

            QueueNextAttack();
        }

        public override bool CanStart()
        {
            if (_entries.Count == 0) return false;
            if (!base.CanStart()) return false;

            var player = Enemy.PlayerTarget;
            if (player == null) return false;

            if (_playerDetectionArea != null)
            {
                return _playerDetectionArea.OverlapsBody(player);
            }

            return true;
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            if (_queuedAttack == null)
            {
                QueueNextAttack();
            }

            _currentAttack = _queuedAttack;
            _queuedAttack = null;

            if (_currentAttack == null)
            {
                GD.Print("[EnemyAttackController] No attack selected, cancelling.");
                Cancel(clearCooldown: true);
                return;
            }

            GD.Print($"[EnemyAttackController] Starting {_currentAttack.Name} for enemy {Enemy.Name}.");
            if (!_currentAttack.TryStart())
            {
                FinishControllerAttack("ChildFailedToStart");
            }
        }

        protected override void OnRecoveryStarted()
        {
            // 控制器的恢复阶段由子攻击流程驱动，因此此处不执行逻辑。
        }

        protected override void OnAttackFinished()
        {
            if (_pendingQueueReason != null)
            {
                QueueNextAttack(_pendingQueueReason);
                _pendingQueueReason = null;
            }

            base.OnAttackFinished();
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            if (_currentAttack == null) return;

            _currentAttack.Tick(delta);
            if (!_currentAttack.IsRunning)
            {
                FinishControllerAttack("ChildFinished");
            }
        }

        private EnemyAttackTemplate? PickAttack()
        {
            float totalWeight = 0f;
            foreach (var entry in _entries)
            {
                totalWeight += entry.Weight;
            }
            if (totalWeight <= 0f) return null;

            float roll = (float)GD.RandRange(0, totalWeight);
            float cumulative = 0f;

            foreach (var entry in _entries)
            {
                cumulative += entry.Weight;
                if (roll <= cumulative)
                {
                    return entry.Template;
                }
            }

            return null;
        }

        private void QueueNextAttack(string reason = "Auto")
        {
            _queuedAttack = PickAttack();
            if (_queuedAttack != null)
            {
                GD.Print($"[EnemyAttackController] ({reason}) Queued {_queuedAttack.Name} for enemy {Enemy.Name}.");
                DebugLogPendingAttackIfPlayerInside(reason);
            }
            else
            {
                GD.Print($"[EnemyAttackController] ({reason}) No attack available to queue for {Enemy.Name}.");
            }
        }

        private Area2D? ResolveArea(NodePath path, Area2D? fallback = null)
        {
            if (path.IsEmpty)
            {
                return fallback;
            }

            var area = GetNodeOrNull<Area2D>(path);
            if (area != null)
            {
                return area;
            }

            return Enemy?.GetNodeOrNull<Area2D>(path) ?? fallback;
        }

        public EnemyAttackTemplate? PeekQueuedAttack() => _queuedAttack;

        public void ForceQueueNextAttack(string reason = "Forced")
        {
            GD.Print($"[EnemyAttackController] Force queue requested ({reason}) for {Enemy.Name}.");
            if (_currentAttack != null)
            {
                _currentAttack.Cancel(clearCooldown: true);
                _currentAttack = null;
            }

            _queuedAttack = null;
            FinishControllerAttack(reason, clearControllerCooldown: true);
        }

        protected override void OnActivePhase()
        {
            // 控制器本身不执行攻击判定，具体逻辑由子攻击管理。
        }

        private void FinishControllerAttack(string reason, bool clearControllerCooldown = false)
        {
            if (_currentAttack != null && _currentAttack.IsRunning)
            {
                _currentAttack.Cancel();
            }

            _currentAttack = null;
            _pendingQueueReason = reason;

            if (IsRunning)
            {
                Cancel(clearControllerCooldown);
            }
            else if (_pendingQueueReason != null)
            {
                QueueNextAttack(_pendingQueueReason);
                _pendingQueueReason = null;
            }
        }

        private void DebugLogPendingAttackIfPlayerInside(string reason)
        {
            if (_playerDetectionArea == null) return;
            var player = Enemy?.PlayerTarget;
            if (player == null) return;

            if (_playerDetectionArea.OverlapsBody(player))
            {
                string attackName = _queuedAttack?.Name ?? "(none queued)";
                GD.Print($"[EnemyAttackController] ({reason}) Player already inside detection area. Next attack: {attackName}");
            }
        }

        public override void _ExitTree()
        {
            if (_playerDetectionArea != null)
            {
                _playerDetectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
                _playerDetectionArea.BodyExited -= OnDetectionAreaBodyExited;
            }
            base._ExitTree();
        }

        private void OnDetectionAreaBodyEntered(Node body)
        {
            if (Enemy?.PlayerTarget == null || body != Enemy.PlayerTarget)
            {
                return;
            }

            _playerInside = true;
            if (_queuedAttack == null && _currentAttack == null)
            {
                QueueNextAttack("PlayerEntered");
            }

            if (ShouldForceAttackState())
            {
                Enemy?.StateMachine?.ChangeState("Attack");
            }

            string attackName = _queuedAttack?.Name ?? "(none queued)";
            GD.Print($"[EnemyAttackController] Player entered detection area. Next attack: {attackName}");
        }

        private void OnDetectionAreaBodyExited(Node body)
        {
            if (Enemy?.PlayerTarget == null || body != Enemy.PlayerTarget)
            {
                return;
            }

            _playerInside = false;
            GD.Print("[EnemyAttackController] Player left detection area. Resetting attack controller.");

            if (_currentAttack != null)
            {
                FinishControllerAttack("PlayerExit", clearControllerCooldown: true);
            }
            else
            {
                QueueNextAttack("PlayerExit");
            }
        }

        private bool ShouldForceAttackState()
        {
            if (Enemy?.StateMachine == null) return false;
            if (_queuedAttack == null) return false;
            if (_queuedAttack.CanStart())
            {
                var current = Enemy.StateMachine.CurrentState?.Name;
                return current != "Attack";
            }

            return false;
        }

        private class Entry
        {
            public EnemyAttackTemplate Template = null!;
            public float Weight;
        }
    }
}

