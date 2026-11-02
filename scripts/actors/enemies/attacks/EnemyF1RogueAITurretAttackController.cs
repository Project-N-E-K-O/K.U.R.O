using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// F1 炮台族攻击控制器（机枪台 / 炮台共用）：疲劳权重法（继承基类，与 guard4/guard5 同款）。
    /// Melee = 贴脸近战（节点 "MeleeAttack"，<see cref="EnemyF1RogueAITurretMeleeAttack"/>）；
    /// 另一招（扫射/开火 SimpleMeleeAttack）不占"技能"槽，SkillAttackName 留空。
    ///
    /// 换用疲劳基类带来的收益：连续同招递减权重（防"连招重复"）、
    /// 排队超时几何降权（选中的招够不着时自动让位——近战在远处被选中也能优雅退场）、
    /// 被打断还原权重（不长期停留在降权值）。
    /// </summary>
    public partial class EnemyF1RogueAITurretAttackController : EnemyFatigueAttackControllerBase
    {
        [ExportCategory("Empower Attack Selection")]
        /// <summary>仅普通状态允许的攻击名。留空保持未接入强化的炮位原行为。</summary>
        [Export] public string[] NormalOnlyAttackNames { get; set; } = System.Array.Empty<string>();
        /// <summary>仅强化状态允许的攻击名；表外攻击不受强化状态限制。</summary>
        [Export] public string[] EmpoweredOnlyAttackNames { get; set; } = System.Array.Empty<string>();
        /// <summary>近战须已满足起手条件才参与排队，避免远距离选中近战后空等超时。</summary>
        [Export] public bool RequireMeleeReadyBeforeQueue { get; set; } = false;

        protected override bool IsAttackAllowed(EnemyAttackTemplate attack)
        {
            if (!base.IsAttackAllowed(attack)) return false;
            if (RequireMeleeReadyBeforeQueue && attack is EnemyF1RogueAITurretMeleeAttack
                && !attack.CanStart()) return false;
            bool normalOnly = System.Array.IndexOf(NormalOnlyAttackNames, attack.AttackName) >= 0;
            bool empoweredOnly = System.Array.IndexOf(EmpoweredOnlyAttackNames, attack.AttackName) >= 0;
            if (!normalOnly && !empoweredOnly) return true;
            bool empowered = Enemy?.GetNodeOrNull<EnemyEmpowerController>("EmpowerController")?.IsEmpowered == true;
            // 配置冲突时不允许该攻击，避免普通/Pro 同时进入选招池。
            if (normalOnly && empoweredOnly) return false;
            return empoweredOnly ? empowered : !empowered;
        }

        public EnemyF1RogueAITurretAttackController()
        {
            MeleeAttackName = "MeleeAttack";
        }
    }
}
