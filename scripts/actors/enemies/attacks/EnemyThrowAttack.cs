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
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();

        [ExportCategory("Behaviour")]
        [Export] public bool FacePlayerOnAttack { get; set; } = true;

        private Area2D? _detectionArea;
        private bool _playerInsideDetection;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            _detectionArea = ResolveArea(DetectionAreaPath);
            if (_detectionArea != null)
            {
                _detectionArea.Monitoring = true;
                _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
                _detectionArea.BodyExited  += OnDetectionAreaBodyExited;
            }
            else
            {
                GD.PushWarning($"[EnemyThrowAttack] DetectionArea not found for {Enemy?.Name ?? Name}.");
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
            // 只生成投掷物 effect，不调用 PerformAttackNow（基类默认行为）
            // 设置 SpawnTiming = OnActive，基类会自动调用 SpawnEffectAtEnemy()
            // 伤害判定完全由投掷物自身实现
            SpawnEffectAtEnemy();
            // 不调用 base.OnActivePhase()，避免触发 PerformAttackNow
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

        private Area2D? ResolveArea(NodePath path)
        {
            if (path.IsEmpty) return null;
            return GetNodeOrNull<Area2D>(path) ?? Enemy?.GetNodeOrNull<Area2D>(path);
        }
    }
}
