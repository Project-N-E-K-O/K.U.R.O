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
        
        /// <summary>
        /// 获取当前冻结的剩余时长（用于调试显示）
        /// 优先返回 FreezeEffect 的剩余时长，否则返回本状态的计时器
        /// </summary>
        public float GetRemainingTime()
        {
            // 若有活跃的 FreezeEffect，返回其剩余时长
            var freeze = Enemy?.EffectController?.GetEffect<FreezeEffect>();
            if (freeze != null)
            {
                return freeze.GetRemainingDuration();
            }
            
            // 否则返回本状态的计时器
            return Mathf.Max(_timer, 0f);
        }

        public override void Enter()
        {
            // 优先从活跃的 FreezeEffect 中读取剩余时长（由外部效果如StunEnemiesEffect控制）
            var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
            if (freeze != null)
            {
                float remainingDuration = freeze.GetRemainingDuration();
                if (remainingDuration > 0f)
                {
                    _timer = remainingDuration;
                }
                else if (freeze.PendingRemainingTime > 0f)
                {
                    // 备选：使用被Hit打断后保存的剩余时长
                    _timer = freeze.PendingRemainingTime;
                    freeze.PendingRemainingTime = 0f;
                }
                else
                {
                    _timer = FrozenDuration;
                }
            }
            // 若无 FreezeEffect，尝试从 Enemy.FrozenStateRemainingTime 恢复（来自Hit状态恢复）
            else if (Enemy.FrozenStateRemainingTime > 0f)
            {
                _timer = Enemy.FrozenStateRemainingTime;
                Enemy.FrozenStateRemainingTime = 0f;
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
            // 只有在被外部打断（仍有剩余时间）的情况下才保存
            // 正常完成的 Frozen（由 FreezeEffect 或计时器结束）不应保存任何时间
            if (_timer > 0f)
            {
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                {
                    freeze.PendingRemainingTime = _timer;
                }
                else
                {
                    // 若无 FreezeEffect（比如 DashEndSelfFrozenDuration 的情况），
                    // 保存到 Enemy 对象，供 Hit 状态恢复使用
                    Enemy.FrozenStateRemainingTime = _timer;
                }
            }
            else
            {
                // 正常完成：清空所有剩余时间标记，防止被后续 Hit 状态错误恢复
                Enemy.FrozenStateRemainingTime = 0f;
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                {
                    freeze.PendingRemainingTime = 0f;
                }
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

