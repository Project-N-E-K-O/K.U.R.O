using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 持续对攻击区域进行多段打击。
    /// </summary>
    public partial class EnemyMultiStrikeAttack : EnemyAttackTemplate
    {
        [Export(PropertyHint.Range, "1,20,1")]
        public int StrikeCount = 3;

        [Export(PropertyHint.Range, "0.05,5,0.05")]
        public float IntervalBetweenStrikes = 0.4f;

        [Export] public NodePath DetectionAreaPath = new NodePath();

        private Area2D? _detectionArea;
        private int _strikesDone = 0;
        private float _intervalTimer = 0f;
        private bool _isAttacking = false;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            if (!DetectionAreaPath.IsEmpty)
            {
                _detectionArea = Enemy.GetNodeOrNull<Area2D>(DetectionAreaPath);
            }
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;

            if (_detectionArea == null)
            {
                return true;
            }

            var player = Enemy.PlayerTarget;
            return player != null && _detectionArea.OverlapsBody(player);
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _strikesDone = 0;
            _intervalTimer = 0f;
            _isAttacking = true;
            Enemy.Velocity = Vector2.Zero;
        }

        protected override void OnActivePhase()
        {
            ExecuteStrike();
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_isAttacking) return;

            _intervalTimer -= (float)delta;
            if (_intervalTimer <= 0f)
            {
                ExecuteStrike();
            }
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _isAttacking = false;
        }

        private void ExecuteStrike()
        {
            if (_strikesDone >= StrikeCount)
            {
                _isAttacking = false;
                SetPhaseToRecovery();
                return;
            }

            Enemy.PerformAttack();
            _strikesDone++;
            _intervalTimer = IntervalBetweenStrikes;
        }

        private void SetPhaseToRecovery()
        {
            // 强制进入恢复阶段
            if (Enemy.AttackTimer <= 0)
            {
                Enemy.AttackTimer = IntervalBetweenStrikes;
            }

            _isAttacking = false;
            _strikesDone = StrikeCount;
            Enemy.Velocity = Vector2.Zero;
        }
    }
}

