using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 投掷/远程攻击模板。
    /// 仅负责范围判断、动画播放和效果生成，具体伤害、击退、轨迹由投掷物自身实现。
    /// 
    /// 工作原理：
    ///   - 检测玩家 HitArea 是否在 AttackArea 范围内
    ///   - 播放攻击动画
    ///   - 在指定时机生成投掷物 effect（通过 EffectScene 配置）
    ///   - 投掷物自身处理飞行、伤害、碰撞等逻辑
    /// </summary>
    public partial class EnemyThrowAttack : EnemyAttackTemplate
    {
        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInThrowRange();
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
        }

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            // 基类 OnActivePhase 已处理：
            // 1. 根据 SpawnTiming 生成效果（投掷物）
            // 2. 若 RequireAnimationHitTrigger 为 true，等待动画事件；否则立即执行
            // 3. RequireAnimationHitTrigger 为 false 时调用 PerformAttackNow（造成伤害）
            //
            // 如需自定义，可在此追加逻辑。否则使用基类默认行为即可。
        }

        private bool IsPlayerInThrowRange()
        {
            if (Player == null) return false;
            if (AttackArea == null) return Enemy.IsPlayerInAttackRange();
            return Player.IsHitByArea(AttackArea);
        }
    }
}
