using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// F1 炮台族近战招（贴脸惩罚，机枪台 / 炮台共用）：**三区语义**——触发区（TriggerAreaPath）是
    /// 追踪/预备圈（玩家不在圈内本招不进候选池），**攻击盒（AttackAreaPath）才是起手圈**（进盒才劈，
    /// 与常规近战 <see cref="EnemySimpleMeleeAttack"/> 同款判定）；蓄力 Warmup（时长由场景配置）到点后
    /// 对伤害区（DamageAreaPath）内的目标结算伤害，并按 KnockbackDistance/Duration 施加击退。
    /// 伤害有两种配法（场景二选一，脚本两种都支持）：
    ///   · **hit 帧驱动**（RequireAnimationHitTrigger = true + AllowMultipleAnimationHits）：spine `skill` 片的
    ///     hit 事件到达时结算（Cannon 当前配法：Warmup 0.8、事件 @0.85 落在 Active 0.8~0.95 窗口内），
    ///     可一次攻击多段命中；
    ///   · **到点结算**（开关关闭）：进 Active 的瞬间自动 PerformAttackNow()（机枪台当前配法）。
    /// 击退必须**晚于伤害**喂：受击管线的"待处理击退标记"只在伤害真落地时置位，提前喂会被
    /// TryApplyPlayerKnockback 直接拒绝——所以到点结算在 OnActivePhase 喂、hit 帧驱动在 OnAnimationHit 喂。
    /// 基础模板**不自动**击退，这是子类的职责。
    ///
    /// 与另一招（扫射/开火 SimpleMeleeAttack）共存的选招约定：本招的**可选中性**（IsPlayerInDetectionRange）
    /// 要求玩家在触发区（追踪/预备圈）内，**起手**还要求进入攻击盒——玩家不在近处时本招根本不进加权随机池，
    /// 避免"随机选中却永远起不了手"卡队列（控制器还有 QueuedAttackTimeout 兜底）。
    ///
    /// 动画：出手播 spine `skill` 片（骨架尚无 `d_skill`，朝左由动画控制器自动回退基名）。
    /// </summary>
    public partial class EnemyF1RogueAITurretMeleeAttack : EnemyAttackTemplate
    {
        /// <summary>起手条件：基类（检测圈 + 朝向角）之上再要求玩家在**攻击盒**（AttackAreaPath）内——
        /// 与常规近战同款判定；攻击盒未解析到时回退根节点攻击区。</summary>
        protected override bool CanStartCore()
        {
            if (!base.CanStartCore()) return false;
            return IsPlayerInsideAttackArea();
        }

        /// <summary>选招资格绑触发区（追踪/预备圈）：玩家不在圈内时本招不参与加权随机
        /// （防止排队在远处永远起不了手）。</summary>
        protected override bool IsPlayerInDetectionRangeCore()
        {
            if (!base.IsPlayerInDetectionRangeCore()) return false;
            return IsPlayerInTriggerArea();
        }

        /// <summary>玩家是否在攻击盒内；缺攻击盒时回退根攻击区判定。</summary>
        private bool IsPlayerInsideAttackArea()
        {
            if (Player == null) return false;
            if (AttackArea == null) return true;
            return IsPlayerInsideArea(Player, AttackArea);
        }

        /// <summary>玩家是否在某区域内——**两种区域写法都认**（与基类 <see cref="EnemyAttackTemplate.IsPlayerInTriggerArea"/> 同款双检测）：
        /// ①检测区（mask 指向玩家**身体**层）→ <see cref="Area2D.OverlapsBody"/>；
        /// ②受击/攻击区（mask 指向玩家 **HitArea** 层）→ <see cref="GameActor.IsHitByArea"/>。
        /// 只判一种会漏掉另一种的配置（GameActor 有 HitArea 时 IsHitByArea 只看 HitArea 重叠，身体层掩码对它无效）。</summary>
        private static bool IsPlayerInsideArea(SamplePlayer? player, Area2D? area)
        {
            if (area == null || player == null) return false;
            return area.OverlapsBody(player) || player.IsHitByArea(area);
        }

        /// <summary>蓄力到点：**到点结算**配法下基类刚在 OnActivePhase 里结算完伤害 → 此刻喂击退；
        /// **hit 帧驱动**配法下此刻还没有伤害（"待处理击退标记"未置位，喂了也会被拒）→ 击退改由
        /// <see cref="OnAnimationHit"/> 在 hit 帧补喂。</summary>
        protected override void OnActivePhase()
        {
            base.OnActivePhase();

            if (RequireAnimationHitTrigger) return;
            ApplyKnockbackIfHit();
        }

        /// <summary>hit 帧驱动配法：base.OnAnimationHit 刚结算完伤害（放行标记已置位），此刻补喂击退。</summary>
        protected override void OnAnimationHit()
        {
            base.OnAnimationHit();
            ApplyKnockbackIfHit();
        }

        /// <summary>与伤害同门的击退喂送（玩家在伤害区内 + KnockbackDistance>0）；玩家无敌帧/格挡时由
        /// 受击管线自行拒绝（EFFECT_STANDARD 第四条），此处不重复判断。必须在伤害结算之后调用。</summary>
        private void ApplyKnockbackIfHit()
        {
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f) return;
            if (Enemy?.PlayerTarget is not SamplePlayer player) return;

            var hitArea = DamageArea;
            if (!IsPlayerInsideArea(player, hitArea)) return;

            TryApplyPlayerKnockback(player, distance, Mathf.Max(KnockbackDuration, 0.01f),
                Enemy.FacingRight ? Vector2.Right : Vector2.Left);
        }
    }
}
