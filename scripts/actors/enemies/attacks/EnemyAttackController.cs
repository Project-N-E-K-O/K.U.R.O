using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 根据权重随机选择子攻击模板并触发。
    /// </summary>
    public partial class EnemyAttackController : EnemyAttackTemplate
    {
        [Export] public NodePath PlayerDetectionAreaPath = new NodePath();
        [Export] public bool EnableDebugLogs = false;
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
            else
            {
                DebugLog("PlayerDetectionAreaPath did not resolve to a valid Area2D.");
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

                    var entry = new Entry
                    {
                        Template = template,
                        Weight = Mathf.Max(weight, 0f)
                    };

                    _entries.Add(entry);
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
				DebugLog("No attack queued; cancelling controller run.");
				Cancel(clearCooldown: true);
				return;
			}

			if (!_currentAttack.CanStart())
			{
				DebugLog($"Attack {_currentAttack.Name} cannot start (likely cooldown/range).");
				FinishControllerAttack("AwaitingStart");
				return;
			}

            if (!_currentAttack.TryStart())
            {
                DebugLog($"Attack {_currentAttack.Name} failed to start.");
                FinishControllerAttack("ChildFailedToStart");
                return;
            }

            OnChildAttackStarted(_currentAttack);
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
				DebugLog($"({reason}) queued attack {_queuedAttack.Name}.");
				DebugLogPendingAttackIfPlayerInside();
			}
			else
			{
				DebugLog($"({reason}) no attack available to queue.");
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
			DebugLog($"Force queue requested ({reason}).");
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
			DebugLog($"Controller finishing because '{reason}'.");

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

		private void DebugLogPendingAttackIfPlayerInside()
		{
			if (_playerDetectionArea == null) return;
			var player = Enemy?.PlayerTarget;
			if (player == null) return;
			if (!_playerInside || !_playerDetectionArea.OverlapsBody(player)) return;
			string attackName = _queuedAttack?.Name ?? "(none queued)";
			DebugLog($"Player already inside detection area. Next attack: {attackName}");
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
			DebugLog("Player entered detection area.");
			if (_queuedAttack == null && _currentAttack == null)
			{
				QueueNextAttack("PlayerEntered");
			}

            if (ShouldForceAttackState())
            {
                Enemy?.StateMachine?.ChangeState("Attack");
            }

        }

        private void OnDetectionAreaBodyExited(Node body)
        {
            if (Enemy?.PlayerTarget == null || body != Enemy.PlayerTarget)
            {
                return;
            }

			_playerInside = false;
			DebugLog("Player left detection area.");

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

        private void DebugLog(string message)
        {
            if (!EnableDebugLogs) return;
            string enemyName = Enemy?.Name ?? "UnknownEnemy";
            GD.Print($"[EnemyAttackController] {enemyName}: {message}");
        }

        protected virtual void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
        }

        protected bool TrySetAttackWeight(string attackName, float weight)
        {
            foreach (var entry in _entries)
            {
                if (entry.Template?.Name == attackName)
                {
                    entry.Weight = Mathf.Max(weight, 0f);
                    return true;
                }
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

