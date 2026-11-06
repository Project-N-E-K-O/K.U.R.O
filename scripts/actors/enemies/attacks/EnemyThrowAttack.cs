using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 投掷/远程攻击模板。
    /// 仅负责范围判断、动画播放和效果生成，具体伤害、击退、轨迹由投掷物自身实现。
    /// 
    /// 工作原理：
    ///   - TriggerAreaPath 由父类统一检查，无需重复订阅身体信号
    ///   - CanStartCore() 仅补充玩家引用和攻击间隔条件
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

        protected override bool CanStartCore()
        {
            if (Enemy == null || Enemy.PlayerTarget == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Enemy.AttackTimer > 0) return false;
            // TriggerArea 的选招和起手限制由非虚公共入口统一检查。
            return true;
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


    }
}
