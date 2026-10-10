using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// guard5（大盾保镖）：**常驻盾牌**——正面 180° 的伤害永久按比例减免
    /// （<see cref="FrontalDamageReduction"/>，默认 99%；减免后至少保留 1 点，
    /// 保证每次格挡都有受击反馈与击退——守卫被挡**不免疫击退**）。
    /// **跪地持盾期间（ShieldStance 的 Warmup = skill1）升级为正面 180° 完全免疫**：
    /// 伤害不结算、不进受击（攻击方视为未命中，击退也不施加）。
    /// 背面 / 侧面 / 判不了方向（正上正下、拿不到来源）→ 全额伤害。
    /// 被减免的伤害仍走正常受击管线（Hit 状态 + 击退），动画控制器按
    /// <see cref="LastHitFromFront"/> 选 hit1（正面格挡表现）/ hit2（背面受击表现）。
    /// </summary>
    public partial class EnemyNormalGuard5 : SampleEnemy
    {
        /// <summary>正面伤害减免比例（0.99 = 只吃 1%；上限 0.999，减免后最低 1 点）。</summary>
        [Export(PropertyHint.Range, "0,0.999,0.001")] public float FrontalDamageReduction = 0.99f;

        /// <summary>最近一次受击是否来自正面（动画控制器据此选 hit1/hit2）。</summary>
        public bool LastHitFromFront { get; private set; }

        private EnemyGuard5ShieldStanceAttack? _shieldStance;

        public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack, bool bypassMergeWindow = false)
        {
            LastHitFromFront = IsFrontal(attackOrigin, attacker);
            if (LastHitFromFront)
            {
                // skill1 跪地持盾期间：正面全免——不结算伤害、不进受击（攻击方据此不施加击退）
                if (IsShieldStanceWarmupActive())
                    return false;

                float keep = 1f - Mathf.Clamp(FrontalDamageReduction, 0f, 0.999f);
                damage = Mathf.Max(Mathf.RoundToInt(damage * keep), 1);
            }
            return base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
        }

        /// <summary>跪地持盾（ShieldStance 的 Warmup）是否正在进行。</summary>
        private bool IsShieldStanceWarmupActive()
        {
            var stance = ResolveShieldStance();
            return stance != null && stance.IsRunning
                && stance.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup;
        }

        private EnemyGuard5ShieldStanceAttack? ResolveShieldStance()
        {
            if (_shieldStance != null && GodotObject.IsInstanceValid(_shieldStance)) return _shieldStance;
            _shieldStance = GetNodeOrNull<EnemyGuard5ShieldStanceAttack>("StateMachine/Attack/AttackController/ShieldStance");
            return _shieldStance;
        }

        /// <summary>正面 180°：来源相对朝向的横向半球；正上/正下（dx≈0）或拿不到来源 → 判不了 → 按非正面（全额）。</summary>
        private bool IsFrontal(Vector2? attackOrigin, GameActor? attacker)
        {
            Vector2? src = attackOrigin ?? (attacker as Node2D)?.GlobalPosition;
            if (src is not Vector2 p) return false;
            float dx = p.X - GlobalPosition.X;
            return FacingRight ? dx > 0f : dx < 0f;
        }
    }
}
