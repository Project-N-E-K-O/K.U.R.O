using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// guard4（复制人·防御反击型）：与 guard2 的差别 = **正面招架**。
    /// 防御反击攻击（CounterAttack）的 Warmup = skill1 招架窗口，期间**正面 180° 的伤害被拦下**：
    ///   · 免伤：不调 base.TakeDamage（攻击方视为未命中 → 常规攻击也不会施加击退），不进受击硬直；
    ///   · 免击退窗口 <see cref="ParryGraceSeconds"/>（默认 0.3s，从挡下那刻起算）——覆盖进入 skill2 的过渡帧，
    ///     窗口内的正面伤害继续被拦；
    ///   · 同时通知反击攻击立即出手（skill2）。
    /// 背面 180° 被打：不拦（走正常受击 → 进 Hit，招架攻击被模板的受击打断机制接管）。
    /// origin 与 attacker 都拿不到时判不了方向 → 按"不招架"处理。
    /// </summary>
    public partial class EnemyNormalGuard4 : SampleEnemy
    {
        /// <summary>挡下后继续免伤+免击退的余量窗口（秒）。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float ParryGraceSeconds = 0.3f;

        private EnemyGuard4CounterAttack? _counter;
        private float _graceTimer;

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_graceTimer > 0f) _graceTimer -= (float)delta;
        }

        public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack, bool bypassMergeWindow = false)
        {
            if (TryParry(attackOrigin, attacker))
                return false;   // 免伤：不结算伤害、不进受击（攻击方据此视为未命中）

            return base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
        }

        /// <summary>招架判定：正面 180° + （招架窗口内 或 免伤余量窗口内）→ 拦下。
        /// 招架窗口内命中时顺带记录余量窗口并通知反击出手。</summary>
        private bool TryParry(Vector2? attackOrigin, GameActor? attacker)
        {
            if (IsDead || IsDeathSequenceActive) return false;

            Vector2? src = attackOrigin ?? (attacker as Node2D)?.GlobalPosition;
            if (src is not Vector2 p) return false;

            // 正面 180°：按左右朝向取半球（打击来源有横向分量才算正面，正上/正下压线时判不了 → 不拦）
            float dx = p.X - GlobalPosition.X;
            bool frontal = FacingRight ? dx > 0f : dx < 0f;
            if (!frontal) return false;

            var counter = ResolveCounter();
            bool inParryWindow = counter != null && counter.IsRunning
                && counter.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup;
            if (!inParryWindow && _graceTimer <= 0f) return false;

            if (inParryWindow)
            {
                _graceTimer = Mathf.Max(ParryGraceSeconds, 0f);
                counter!.NotifyParried(p);
            }
            return true;
        }

        private EnemyGuard4CounterAttack? ResolveCounter()
        {
            if (_counter != null && GodotObject.IsInstanceValid(_counter)) return _counter;
            _counter = GetNodeOrNull<EnemyGuard4CounterAttack>("StateMachine/Attack/AttackController/CounterAttack");
            return _counter;
        }
    }
}
