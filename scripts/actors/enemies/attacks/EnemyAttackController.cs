using Godot;
using System;
using System.Collections.Generic;
using Kuros.Utils;

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
        /// <summary>子攻击完成后的攻击间隔（= 子攻击的 CooldownDuration），期间禁止发起任何攻击。</summary>
        private float _interAttackDelay = 0f;

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
                        Weight = Mathf.Max(weight, 0f),
                        GuaranteeInterval = ReadMetaInt(template, "guarantee_interval", 0),
                        GuaranteePriority = ReadMetaInt(template, "guarantee_priority", int.MaxValue),
                        AttackName = template.AttackName
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

            // 攻击间隔（上次子攻击的CD）尚未结束，禁止立即发起下一次攻击
            if (_interAttackDelay > 0f) return false;

            var player = Enemy.PlayerTarget;
            if (player == null) return false;

            if (_playerDetectionArea != null)
            {
                if (!_playerDetectionArea.OverlapsBody(player)) return false;
            }

            // 尚无排队攻击（全部CD中）或排队攻击仍在CD中，均视为不可开始
            if (_queuedAttack == null) return false;
            if (!_queuedAttack.CanStart()) return false;

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

            if (!GodotObject.IsInstanceValid(_currentAttack))
            {
                DebugLog("Queued attack instance became invalid before start.");
                _currentAttack = null;
                FinishControllerAttack("ChildInvalidBeforeStart");
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
            CleanupChildAttack(clearCooldown: true);

            if (_pendingQueueReason != null)
            {
                QueueNextAttack(_pendingQueueReason);
                _pendingQueueReason = null;
            }
            else if (ShouldAutoQueueAfterInterruption())
            {
                QueueNextAttack("Interrupted");
            }

            base.OnAttackFinished();
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            // 递减攻击间隔计时器
            if (_interAttackDelay > 0f) _interAttackDelay -= (float)delta;

            // 对所有非活跃子模板调用 Tick，确保攻击结束后的冷却计时器能正常倒计时。
            // Tick() 在 _phase == Idle 时只递减 _cooldownTimer 后立即返回，开销极低。
            foreach (var entry in _entries)
            {
                if (entry.Template == null || entry.Template == _currentAttack) continue;
                if (!GodotObject.IsInstanceValid(entry.Template)) continue;
                entry.Template.Tick(delta);
            }

            // 控制器空闲时，若无可用攻击（全部CD中）则每帧检查是否有攻击CD已到期，
            // 一旦有攻击可用立即更新 _queuedAttack 以便 CanStart() 重新返回 true。
            if (!IsRunning && (_queuedAttack == null || _queuedAttack.IsOnCooldown))
            {
                var candidate = PickAttack();
                if (candidate != null)
                {
                    _queuedAttack = candidate;
                    DebugLog($"CD expired, re-queued: {_queuedAttack.Name}");
                    if (ShouldForceAttackState())
                    {
                        Enemy?.StateMachine?.ChangeState("Attack");
                    }
                }
            }

            if (_currentAttack == null)
            {
                // OnAttackStarted 内子攻击 CanStart() 失败时，TryStart 仍会调用 SetPhase(Warmup)
                // 导致控制器处于 Running 但无当前攻击的卡死状态，此处主动结束。
                if (IsRunning)
                {
                    FinishControllerAttack("NullCurrentAttack");
                }
                return;
            }
            if (!GodotObject.IsInstanceValid(_currentAttack))
            {
                _currentAttack = null;
                FinishControllerAttack("ChildInvalid");
                return;
            }

            _currentAttack.Tick(delta);
            if (!_currentAttack.IsRunning)
            {
                FinishControllerAttack("ChildFinished");
            }
        }

        private EnemyAttackTemplate? PickAttack()
        {
            // 只在非CD中的攻击里做加权随机选择，避免选中后立即无法启动
            float totalWeight = 0f;
            foreach (var entry in _entries)
            {
                if (entry.Template == null || !GodotObject.IsInstanceValid(entry.Template)) continue;
                if (entry.Template.IsOnCooldown) continue;
                totalWeight += entry.Weight;
            }
            if (totalWeight <= 0f) return null;

            float roll = (float)GD.RandRange(0, totalWeight);
            float cumulative = 0f;

            foreach (var entry in _entries)
            {
                if (entry.Template == null || !GodotObject.IsInstanceValid(entry.Template)) continue;
                if (entry.Template.IsOnCooldown) continue;
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
            string selectionReason = reason;
            var guaranteedAttack = TryGetGuaranteedAttack();
            if (guaranteedAttack != null)
            {
                _queuedAttack = guaranteedAttack;
                selectionReason = $"{reason}|Guarantee";
            }
            else
            {
                _queuedAttack = PickAttack();
            }
            RefreshPlayerDetectionState();
            if (_queuedAttack != null)
            {
				DebugLog($"({selectionReason}) queued attack {_queuedAttack.Name}.");
				DebugLogPendingAttackIfPlayerInside();

                if (reason != "PlayerExit" && ShouldForceAttackState())
                {
                    Enemy?.StateMachine?.ChangeState("Attack");
                }
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

        /// <summary>
        /// 返回所有攻击中剩余CD最短的那个（即最快可用的攻击）的信息。
        /// 若当前无攻击处于冷却中，Remaining 为 0。
        /// </summary>
        public (float Remaining, float Duration, string Name) GetShortestCooldownInfo()
        {
            float minRemaining = float.MaxValue;
            float duration = 0f;
            string name = string.Empty;
            bool anyOnCd = false;

            foreach (var entry in _entries)
            {
                if (entry.Template == null || !GodotObject.IsInstanceValid(entry.Template)) continue;
                if (!entry.Template.IsOnCooldown) continue;
                anyOnCd = true;
                if (entry.Template.CooldownRemaining < minRemaining)
                {
                    minRemaining = entry.Template.CooldownRemaining;
                    duration = entry.Template.CooldownDuration;
                    name = entry.AttackName;
                }
            }

            return anyOnCd ? (minRemaining, duration, name) : (0f, 0f, string.Empty);
        }

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
            // 子攻击正常完成时，将其 CooldownDuration 保存为攻击间隔，
            // 确保结束后不会立即切换到另一种攻击（与旧 Enemy.AttackTimer 语义一致）
            float childInterAttackDelay = 0f;
            if (reason == "ChildFinished" && _currentAttack != null)
                childInterAttackDelay = _currentAttack.CooldownDuration;

            CleanupChildAttack(clearCooldown: false);
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

            // 强制清除时同步清除攻击间隔；否则应用子攻击的 CD 作为间隔
            if (clearControllerCooldown)
                _interAttackDelay = 0f;
            else if (childInterAttackDelay > 0f)
                _interAttackDelay = childInterAttackDelay;
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
                var entered = new Callable(this, MethodName.OnDetectionAreaBodyEntered);
                var exited = new Callable(this, MethodName.OnDetectionAreaBodyExited);
                if (_playerDetectionArea.IsConnected(Area2D.SignalName.BodyEntered, entered))
                {
                    _playerDetectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
                }

                if (_playerDetectionArea.IsConnected(Area2D.SignalName.BodyExited, exited))
                {
                    _playerDetectionArea.BodyExited -= OnDetectionAreaBodyExited;
                }
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
                if (ShouldInterruptOnPlayerExit())
                    FinishControllerAttack("PlayerExit", clearControllerCooldown: true);
            }
            else
            {
                QueueNextAttack("PlayerExit");
            }
        }

        /// <summary>
        /// 玩家离开检测区域时，是否中断当前子攻击。
        /// 子类可重写此方法，对需要持续到底的攻击（如终极技）返回 false。
        /// </summary>
        protected virtual bool ShouldInterruptOnPlayerExit() => true;

        private bool ShouldForceAttackState()
        {
            if (!IsEnemyActionable() || Enemy?.StateMachine == null) return false;
            // 使用完整的 CanStart()，确保 _interAttackDelay 和 _queuedAttack 均已就绪才强制切换
            if (!CanStart()) return false;
            var current = Enemy.StateMachine.CurrentState?.Name;
            return current != "Attack";
        }

        private bool IsEnemyAlive()
        {
            return Enemy != null && !Enemy.IsDeathSequenceActive && !Enemy.IsDead;
        }

        /// <summary>
        /// 敌人是否处于可行动状态（未死亡、未冻结、未处于受击状态）。
        /// 用于判断是否允许发起/强制触发攻击。
        /// </summary>
        private bool IsEnemyActionable()
        {
            if (!IsEnemyAlive()) return false;
            var stateName = Enemy?.StateMachine?.CurrentState?.Name;
            return stateName != "Frozen"
                && stateName != "CooldownFrozen"
                && stateName != "Hit"
                && stateName != "Dying"
                && stateName != "Dead";
        }

        private void DebugLog(string message)
        {
            if (!EnableDebugLogs) return;
            string enemyName = Enemy?.Name ?? "UnknownEnemy";
            GameLogger.Debug(nameof(EnemyAttackController), $"{enemyName}: {message}");
        }

        protected virtual void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            RegisterAttackUsage(attack);
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
            public int GuaranteeInterval;
            public int GuaranteePriority = int.MaxValue;
            public int SinceLastUse;
            public string AttackName = string.Empty;
        }

        private void RegisterAttackUsage(EnemyAttackTemplate attack)
        {
            foreach (var entry in _entries)
            {
                if (entry.Template == null) continue;

                if (entry.Template == attack)
                {
                    entry.SinceLastUse = 0;
                }
                else if (entry.GuaranteeInterval > 0)
                {
                    entry.SinceLastUse = Mathf.Min(entry.SinceLastUse + 1, entry.GuaranteeInterval);
                }
            }
        }

        private EnemyAttackTemplate? TryGetGuaranteedAttack()
        {
            Entry? forcedEntry = null;
            foreach (var entry in _entries)
            {
                if (entry.Template == null) continue;
                if (entry.GuaranteeInterval <= 0) continue;
                if (entry.SinceLastUse < entry.GuaranteeInterval) continue;

                if (forcedEntry == null || entry.GuaranteePriority < forcedEntry.GuaranteePriority)
                {
                    forcedEntry = entry;
                }
            }

            return forcedEntry?.Template;
        }

        private static int ReadMetaInt(Node node, string key, int defaultValue)
        {
            if (!node.HasMeta(key)) return defaultValue;
            Variant meta = node.GetMeta(key);
            return meta.VariantType switch
            {
                Variant.Type.Int => (int)meta,
                Variant.Type.Float => Mathf.RoundToInt((float)meta),
                _ => defaultValue
            };
        }

        private void CleanupChildAttack(bool clearCooldown)
        {
            if (_currentAttack == null) return;
            if (!GodotObject.IsInstanceValid(_currentAttack))
            {
                _currentAttack = null;
                return;
            }

            if (_currentAttack.IsRunning)
            {
                _currentAttack.Cancel(clearCooldown);
            }

            _currentAttack = null;
        }

        private bool ShouldAutoQueueAfterInterruption()
        {
            if (_playerDetectionArea == null || Enemy?.PlayerTarget == null) return false;
            if (!_playerDetectionArea.IsInsideTree()) return false;
            return _playerInside && _playerDetectionArea.OverlapsBody(Enemy.PlayerTarget);
        }

        private void RefreshPlayerDetectionState()
        {
            if (_playerDetectionArea == null || Enemy?.PlayerTarget == null)
            {
                _playerInside = false;
                return;
            }

            if (!_playerDetectionArea.IsInsideTree())
            {
                _playerInside = false;
                return;
            }

            _playerInside = _playerDetectionArea.OverlapsBody(Enemy.PlayerTarget);
        }
    }
}

