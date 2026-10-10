using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard5 攻击控制器：疲劳权重法（继承基类）。
    /// Melee = 盾击（节点 "SimpleMeleeAttack"，复用 <see cref="EnemySimpleMeleeAttack"/>：
    /// 无位移的普通近战——动画事件结算伤害+击退）；
    /// Skill = 跪地持盾→冲撞（节点 "ShieldStance"，<see cref="EnemyGuard5ShieldStanceAttack"/>，skill1+skill2 一体）。
    /// </summary>
    public partial class EnemyNormalGuard5AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard5AttackController()
        {
            MeleeAttackName = "SimpleMeleeAttack";
            SkillAttackName = "ShieldStance";
        }

        /// <summary>子攻击被打断（受击/眩晕导致的状态切换）：控制器默认会清掉子攻击 CD（允许尽快重试），
        /// 但盾反不同——跪地被打断就是"起手失败"：按普通 CD 结算（与 guard4 招架失败的语义一致）。
        /// （Recovery 阶段打断时控制器本就保留 CD、也不触发本回调，不受影响。）</summary>
        protected override void OnAttackInterrupted(EnemyAttackTemplate attack)
        {
            base.OnAttackInterrupted(attack);
            if (attack is EnemyGuard5ShieldStanceAttack stance)
            {
                stance.ApplyInterruptedCooldown();
            }
        }
    }
}
