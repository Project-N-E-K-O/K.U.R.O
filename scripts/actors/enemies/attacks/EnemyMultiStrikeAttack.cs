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

        // 区域由基类统一提供：TriggerAreaPath（起手检测区；未配置 = 不做额外限制）、
        // DamageAreaPath（伤害落点；未配置 = AttackArea）。旧的 DetectionAreaPath 已并入基类。

        private int _strikesDone = 0;
        private float _strikeTimer = 0f;
        private bool _comboActive = false;

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;

            if (TriggerArea == null) return true;

            var player = Enemy.PlayerTarget;
            return player != null && TriggerArea.OverlapsBody(player);
        }

        protected override void OnAttackStarted()
        {
            WarmupDuration = 0.5f;
            ActiveDuration = float.MaxValue;

            base.OnAttackStarted();

            _strikesDone = 0;
            _strikeTimer = 0f;
            _comboActive = false;
            Enemy.Velocity = Vector2.Zero;
        }

        protected override void OnActivePhase()
        {
            _comboActive = true;
            _strikeTimer = 0f;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_comboActive) return;

            _strikeTimer -= (float)delta;
            while (_comboActive && _strikeTimer <= 0f)
            {
                ExecuteStrike();
                if (_comboActive)
                {
                    _strikeTimer += IntervalBetweenStrikes;
                }
            }
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _comboActive = false;
        }

        private void ExecuteStrike()
        {
            Enemy.PerformAttack(DamageArea, TargetableFactions);
            _strikesDone++;

            if (_strikesDone >= StrikeCount)
            {
                _comboActive = false;
                ForceEnterRecoveryPhase();
            }
        }
    }
}

