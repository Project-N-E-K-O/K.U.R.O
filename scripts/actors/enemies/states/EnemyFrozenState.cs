using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人被冻结或被控制时的通用状态。
    /// </summary>
    public partial class EnemyFrozenState : EnemyState
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float FrozenDuration = 0.1f;

        private float _timer;

        public override void Enter()
        {
            // 若是被 Hit 打断后重新进入，从 FreezeEffect 中恢复剩余时间
            var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
            if (freeze != null && freeze.PendingRemainingTime > 0f)
            {
                _timer = freeze.PendingRemainingTime;
                freeze.PendingRemainingTime = 0f;
            }
            else
            {
                _timer = FrozenDuration;
            }

            Enemy.Velocity = Vector2.Zero;

            if (Enemy.AnimPlayer != null)
            {
                if (Enemy.AnimPlayer.HasAnimation("animations/hit"))
                {
                    Enemy.AnimPlayer.Play("animations/hit");
                }
                else
                {
                    Enemy.AnimPlayer.Play("animations/Idle");
                }
            }
        }

        public override void Exit()
        {
            // 若仍有剩余时间（被外部打断），保存到 FreezeEffect 供恢复使用
            if (_timer > 0f)
            {
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                    freeze.PendingRemainingTime = _timer;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.MoveAndSlide();

            // 若当前存在 FreezeEffect 控制此状态，则由 FreezeEffect.OnRemoved 负责退出，
            // 跳过自身计时器，避免 FrozenDuration 抢先结束眩晕。
            if (Enemy.EffectController?.GetEffect<FreezeEffect>() != null)
                return;

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                Enemy.AttackTimer = 0f;
                ChangeState("Idle");
            }
        }
    }
}

