using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 投掷/远程攻击模板。
    /// 仅负责范围判断、动画播放和效果生成，具体伤害、击退、轨迹由投掷物自身实现。
    /// 
    /// 工作原理：
    ///   - 订阅 DetectionAreaPath 的 BodyEntered/Exited，记录玩家是否在范围内
    ///   - CanStart() 使用自身的检测状态（与 EnemyChargeGrabAttack 一致）
    ///   - 生效时生成投掷物 effect（通过 EffectScene 配置）
    ///   - 投掷物自身处理飞行、伤害、碰撞等逻辑
    ///   - 不使用 AttackArea hitbox，不直接调用 PerformAttackNow
    /// </summary>
    public partial class EnemyThrowAttack : EnemyAttackTemplate
    {
        // 区域由基类统一提供：TriggerAreaPath（起手检测区；未配置 = 不做额外限制）。
        // 旧的 DetectionAreaPath 已并入基类。本招不结算伤害（只生成投掷物），所以不涉及 AttackArea/DamageArea。

        [ExportCategory("Behaviour")]
        [Export] public bool FacePlayerOnAttack { get; set; } = true;

        private Area2D? _detectionArea;
        private bool _playerInsideDetection;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            _detectionArea = TriggerArea;
            if (_detectionArea != null)
            {
                _detectionArea.Monitoring = true;
                _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
                _detectionArea.BodyExited  += OnDetectionAreaBodyExited;
            }

            SetPhysicsProcess(true);
        }

        public override void _ExitTree()
        {
            if (_detectionArea != null)
            {
                var entered = new Callable(this, MethodName.OnDetectionAreaBodyEntered);
                var exited  = new Callable(this, MethodName.OnDetectionAreaBodyExited);
                if (_detectionArea.IsConnected(Area2D.SignalName.BodyEntered, entered))
                    _detectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
                if (_detectionArea.IsConnected(Area2D.SignalName.BodyExited, exited))
                    _detectionArea.BodyExited -= OnDetectionAreaBodyExited;
            }

            base._ExitTree();
        }

        public override bool CanStart()
        {
            if (Enemy == null || Enemy.PlayerTarget == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Enemy.AttackTimer > 0) return false;

            // 使用自身 DetectionArea 或回退到敌人默认检测范围
            bool detectionSatisfied = _detectionArea != null
                ? _playerInsideDetection || _detectionArea.OverlapsBody(Enemy.PlayerTarget)
                : Enemy.IsPlayerInAttackRange();

            return detectionSatisfied;
        }

        protected override void OnAttackStarted()
        {
            if (FacePlayerOnAttack && Enemy != null && Enemy.PlayerTarget != null)
            {
                bool playerIsRight = Enemy.PlayerTarget.GlobalPosition.X >= Enemy.GlobalPosition.X;
                Enemy.FlipFacing(playerIsRight);
            }

            base.OnAttackStarted();
        }

        protected override void OnActivePhase()
        {
            // 如果存在动画帧时机（OnAnimationHit）的特效，交由 OnAnimationHit 逐帧触发
            if (RequireAnimationHitTrigger && HasOnAnimationHitEffect())
            {
                _animationHitReady = true;
                return;
            }

            // 生成 OnActive 时机的特效（entry 独立时机生效；未配置回退 OnActive）
            SpawnEffectAtEnemy(EffectSpawnTiming.OnActive);
        }

        /// <summary>Effects 中是否存在显式 OnAnimationHit 时机的特效（模板级 SpawnTiming 已随 entry 迁移移除）。</summary>
        private bool HasOnAnimationHitEffect()
        {
            foreach (var entry in Effects)
            {
                if (entry != null
                    && entry.ResolveTiming(EffectSpawnTiming.OnActive) == EffectSpawnTiming.OnAnimationHit)
                    return true;
            }
            return false;
        }

        protected override void OnAnimationHit()
        {
            // 仅生成投掷物，不调用 PerformAttackNow（伤害由投掷物自身实现）
            SpawnEffectAtEnemy(EffectSpawnTiming.OnAnimationHit);
        }

        private void OnDetectionAreaBodyEntered(Node body)
        {
            if (body == Enemy?.PlayerTarget)
                _playerInsideDetection = true;
        }

        private void OnDetectionAreaBodyExited(Node body)
        {
            if (body == Enemy?.PlayerTarget)
                _playerInsideDetection = false;
        }

    }
}
