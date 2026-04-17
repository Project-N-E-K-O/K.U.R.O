using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using System;

namespace Kuros.Builds
{
    /// <summary>
    /// 安保 1 级效果：减少玩家受到敌人的攻击时的伤害。
    /// 每次受伤时减少1点伤害，但玩家最少受到1点伤害。
    /// </summary>
    [GlobalClass]
    public partial class BuildGuardLevel1Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,10,1")] public int DamageReduction { get; set; } = 1;

        private bool _subscribed;

        public BuildGuardLevel1Effect()
        {
            EffectId = "build_guard_level1";
            DisplayName = "安保I";
            Description = "玩家受到敌人伤害时减少1点伤害";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || _subscribed)
            {
                GD.Print("[BuildGuardLevel1Effect] OnApply skipped: Actor is null or already subscribed");
                return;
            }

            // 订阅伤害拦截事件（伤害应用前）
            Actor.DamageIntercepted += OnDamageIntercepted;
            _subscribed = true;
            //GD.Print("[BuildGuardLevel1Effect] OnApply called, damage reduction effect activated");
        }

        public override void OnRemoved()
        {
            if (_subscribed && Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }

            _subscribed = false;
            //GD.Print("[BuildGuardLevel1Effect] OnRemoved called, damage reduction effect deactivated");
            base.OnRemoved();
        }

        /// <summary>
        /// 伤害拦截回调：在伤害应用前修改伤害值。
        /// （注：AnyDamageTaken 是在伤害已应用后的回调，无法修改伤害值）
        /// </summary>
        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            // 确保只处理针对自身的伤害
            if (Actor == null || args.Target != Actor)
            {
                return false;
            }

            int originalDamage = args.Damage;
            int reducedDamage;
            
            // 伤害减少逻辑：
            // 当敌人伤害 <= 减免值时，玩家最终受到伤害 = 1
            // 否则，玩家受到伤害 = 敌人伤害 - 减免值
            if (originalDamage <= DamageReduction)
            {
                // 敌人伤害较低，玩家最少受到1点伤害
                reducedDamage = 1;
            }
            else
            {
                // 敌人伤害较高，减去减免值
                reducedDamage = originalDamage - DamageReduction;
            }
            
            args.Damage = reducedDamage;

            //GD.Print($"[BuildGuardLevel1Effect] OnDamageIntercepted: original={originalDamage}, reduction={DamageReduction}, final={reducedDamage}");
            
            // 返回 false 表示伤害继续应用（不被完全阻挡）
            // 返回 true 可以完全阻挡伤害
            return false;
        }
    }
}
