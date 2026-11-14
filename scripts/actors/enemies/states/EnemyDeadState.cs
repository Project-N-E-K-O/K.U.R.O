using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人死亡终止状态：**进入时**生成收尸/消失特效（<see cref="DeadEffect"/>，可选），
    /// 随后触发实体销毁及资源清理。原 actor 级 DeathEffect 的生成时点语义搬到这里。
    /// </summary>
    public partial class EnemyDeadState : EnemyState
    {
        /// <summary>收尸/消失特效（可选）：Enter（FinalizeDeath 之前）在敌人位置生成——
        /// 特效挂世界层，不随敌人销毁。</summary>
        [Export] public PackedScene? DeadEffect { get; set; }
        /// <summary>收尸特效的生成偏移（按未镜像/朝右姿态摆；朝左自动翻 X）。</summary>
        [Export] public Vector2 EffectOffset { get; set; } = Vector2.Zero;

        public override void Enter()
        {
            // 先生成特效、再终结实体（FinalizeDeath 会销毁敌人，特效已挂在父级下不受影响）
            if (Enemy.GetNodeOrNull<EnemyFallController>("FallController")?.HasCorpseFallen != true)
                SpawnEffectAtSelf(DeadEffect, EffectOffset);
            Enemy.FinalizeDeath();
        }
    }
}
