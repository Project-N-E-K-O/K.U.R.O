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
        /// <summary>两次攻击之间的全局呼吸窗（= MinInterAttackDelay）。各攻击独立 CD 由子模板的 CooldownDurationMultiplier 控制（只锁自己）。</summary>
        private float _interAttackDelay = 0f;

	    /// <summary>两次攻击之间的全局最小间隔（呼吸窗）：攻击结束后必须经过此间隔才能选下一招。
        /// 各技能仍由自身的 CooldownDurationMultiplier 独立 CD（只锁自己，不锁其他技能）。
        /// 0 = 无间隔（旧行为：纯独立 CD，可能出现 A 结束 B 零间隔连发）。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float MinInterAttackDelay = 0f;

        /// <summary>
        /// 排队攻击等待超时（秒）：选中的攻击因距离/角度一直无法启动（CanStart 不满足）时，
        /// 超时后放弃当前排队并重新加权选择——防止"选中近战但玩家保持远距离，突刺等可达攻击永远轮不到"。
        /// 0 = 不限（默认）。
        /// </summary>
        [Export(PropertyHint.Range, "0,10,0.5")] public float QueuedAttackTimeout = 0f;
        private float _queuedElapsed;

        /// <summary>调试：当前排队攻击名（空 = 未排队）。</summary>
        public string QueuedAttackName => _queuedAttack?.Name ?? "";
        /// <summary>调试：排队等待已计时间（超时判定用）。</summary>
        public float QueuedElapsed => _queuedElapsed;
        /// <summary>调试：排队攻击当前是否可启动（CanStart 检查）。</summary>
        public bool QueuedCanStart => _queuedAttack?.CanStart() == true;

        /// <summary>调试：所有攻击的当前权重（攻击名 → 权重，疲劳降权后）。</summary>
        public Dictionary<string, float> GetAttackWeights()
        {
            var result = new Dictionary<string, float>();
            foreach (var entry in _entries)
            {
                if (entry.Template != null)
                    result[entry.Template.Name] = entry.Weight;
            }
            return result;
        }

        public EnemyAttackController()
        {
            WarmupDuration = 0f;
            ActiveDuration = ControllerActiveDuration;
            RecoveryDuration = 0f;
            CooldownDurationMultiplier = 0f;
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

            // 全局呼吸窗（MinInterAttackDelay）尚未结束，禁止立即发起下一次攻击
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
            // 打断时根据子攻击所处阶段决定是否保留 CD：
            // Recovery 阶段打断 → 攻击已基本完成，保留 CD 防止立即复用
            // Warmup/Active 阶段打断 → 攻击未完成，清除 CD 允许重新尝试
            bool childInRecovery = _currentAttack?.CurrentPhase == EnemyAttackTemplate.AttackPhase.Recovery;
            var interruptedAttack = !childInRecovery ? _currentAttack : null; // 未完成被中断（如受伤眩晕）
            CleanupChildAttack(clearCooldown: !childInRecovery);

            // 打断 = 视为未使用：恢复该攻击权重（子类覆写；与切换攻击恢复上一个攻击的语义一致）
            if (interruptedAttack != null)
                OnAttackInterrupted(interruptedAttack);

            // 不在此处立即排队下一次攻击，而是清空 _queuedAttack，
            // 让 _PhysicsProcess 空闲循环在所有攻击 CD 结束后再做加权随机选择
            if (_pendingQueueReason != null)
            {
                _pendingQueueReason = null;
                _queuedAttack = null;
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

            // 排队攻击等待超时：间隔已过但选中的攻击仍无法启动（玩家距离/角度不符，CanStart 不满足）——
            // 超时视为"使用一次"对该攻击降权（子类覆写 OnQueuedAttackTimeout），
            // 其概率降低后，突刺等可达攻击有更高机会被选中。
            // 敌人不可行动（Frozen/Hit 眩晕）期间不计时——避免长时间眩晕把攻击权重反复降到地板
            if (!IsRunning && _queuedAttack != null && QueuedAttackTimeout > 0f && _interAttackDelay <= 0f
                && !IsEnemyDisabled())
            {
                _queuedElapsed += (float)delta;
                if (_queuedElapsed >= QueuedAttackTimeout && !_queuedAttack.CanStart())
                {
                    DebugLog($"Queued attack {_queuedAttack.Name} timed out waiting to start, applying fatigue.");
                    OnQueuedAttackTimeout(_queuedAttack);
                    _queuedAttack = null;
                    _queuedElapsed = 0f;
                    // 下一帧空闲循环按降权后的权重重新加权选择（本帧不再重复进入）
                }
            }

            // 控制器空闲时，等待呼吸窗到期后再做加权随机选择；
            // 各攻击的独立 CD 由 IsAttackEligible 过滤，权重真正生效
            if (!IsRunning && _interAttackDelay <= 0f
                && (_queuedAttack == null || _queuedAttack.IsOnCooldown))
            {
                var candidate = PickAttack();
                if (candidate != null)
                {
                    _queuedAttack = candidate;
                    _queuedElapsed = 0f; // 新排队重新计时
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

        /// <summary>
        /// 排队攻击超时回调（选中后 QueuedAttackTimeout 秒内未能启动，CanStart 不满足）。
        /// 子类可覆写：将该攻击视为"使用一次"降权（疲劳），降低其后续被选中的概率，
        /// 让突刺等可达攻击有机会被选中。默认空。
        /// </summary>
        protected virtual void OnQueuedAttackTimeout(EnemyAttackTemplate attack) { }

        /// <summary>
        /// 子攻击被打断回调（Warmup/Active 阶段强制中断——如受伤眩晕进入 Frozen）：
        /// 子类可覆写恢复该攻击权重（视为未使用，不累计疲劳）。默认空。
        /// </summary>
        protected virtual void OnAttackInterrupted(EnemyAttackTemplate attack) { }

        /// <summary>敌人是否处于不可行动状态（受伤眩晕/硬直）——期间排队超时不计时。</summary>
        private bool IsEnemyDisabled()
        {
            string state = Enemy?.StateMachine?.CurrentState?.Name ?? "";
            return state == "Frozen" || state == "CooldownFrozen" || state == "Hit";
        }

        private EnemyAttackTemplate? PickAttack()
        {
            float totalWeight = 0f;
            foreach (var entry in _entries)
            {
                if (!IsAttackEligible(entry)) continue;
                totalWeight += entry.Weight;
            }
            if (totalWeight <= 0f) return null;

            float roll = (float)GD.RandRange(0, totalWeight);
            float cumulative = 0f;

            foreach (var entry in _entries)
            {
                if (!IsAttackEligible(entry)) continue;
                cumulative += entry.Weight;
                if (roll <= cumulative)
                {
                    return entry.Template;
                }
            }

            return null;
        }

        /// <summary>攻击可选中性：模板有效 + 非冷却 + 玩家在范围 + 无其他敌人执行同攻击 + 场上无同攻击特效。</summary>
        private bool IsAttackEligible(Entry entry)
        {
            if (entry.Template == null || !GodotObject.IsInstanceValid(entry.Template)) return false;
            if (entry.Template.IsOnCooldown) return false;
            if (!entry.Template.IsPlayerInDetectionRange()) return false;

            // 条件1：有其他敌人正在执行本攻击 → 排除（防动作重叠）
            if (IsOtherEnemyAttacking(entry.Template.AttackName)) return false;

            // 条件2：场上（全局）存在本攻击的存活特效 → 排除（防特效叠加）
            return !IsFxBlockedByOwnEffects(entry.Template);
        }

        /// <summary>特效阻塞判定（对所有攻击自动生效）：每个 entry 的显式 BlockedByFxGroup 生效
        /// （模板级字段已随 entry 迁移移除）；另自动收集 entry 的 UniqueGroup——任何一组有存活实例即阻塞。</summary>
        private bool IsFxBlockedByOwnEffects(EnemyAttackTemplate template)
        {
            foreach (var entry in template.Effects)
            {
                if (entry == null) continue;

                // entry 显式阻塞组
                string group = entry.ResolveBlockedGroup(string.Empty);
                if (!string.IsNullOrEmpty(group) && IsFxGroupActive(group))
                {
                    return true;
                }

                // 自动收集：entry 的 UniqueGroup（防同特效叠加）
                if (!string.IsNullOrEmpty(entry.UniqueGroup) && IsFxGroupActive(entry.UniqueGroup))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>场上是否存在"其他"敌人（排除自己）正在执行指定攻击。</summary>
        private bool IsOtherEnemyAttacking(string attackName)
        {
            foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node == Enemy) continue;
                if (node is SampleEnemy other && other.IsAttackRunning(attackName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>场上（全局）是否存在指定组的存活特效实例。</summary>
        private bool IsFxGroupActive(string group)
        {
            if (string.IsNullOrEmpty(group)) return false;

            foreach (Node node in GetTree().GetNodesInGroup(group))
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>该敌人当前是否正在执行指定攻击（基类查询：当前子攻击名匹配）。</summary>
        public bool IsAttackRunning(string attackName)
        {
            return _currentAttack != null
                && GodotObject.IsInstanceValid(_currentAttack)
                && _currentAttack.AttackName == attackName;
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
            _queuedElapsed = 0f; // 排队计时起点
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
                    duration = entry.Template.GetCooldown();
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
            // 攻击结束后统一进入 MinInterAttackDelay 全局呼吸窗（默认 0 = 无间隔）。
            // 各技能由自身 CooldownDurationMultiplier 独立 CD（只锁自己），
            // 呼吸窗仅填补"A 刚结束 B 零间隔连发"这个独立 CD 覆盖不到的缺口。
            CleanupChildAttack(clearCooldown: false);
            _pendingQueueReason = reason;
			DebugLog($"Controller finishing because '{reason}'.");

            if (IsRunning)
            {
                Cancel(clearControllerCooldown);
            }
            else if (_pendingQueueReason != null)
            {
                // 清空排队攻击，由 _PhysicsProcess 空闲循环在 CD 到期后重新加权选择
                _pendingQueueReason = null;
                _queuedAttack = null;
            }

            // 强制清除时同步清除呼吸窗；否则固定用 MinInterAttackDelay 作为全局最小间隔
            _interAttackDelay = clearControllerCooldown ? 0f : MinInterAttackDelay;
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

