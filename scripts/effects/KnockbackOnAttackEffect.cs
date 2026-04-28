using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Effects
{
    /// <summary>
    /// 攻击时击退敌人的效果。
    /// 当携带此效果的角色攻击敌人时，敌人会在指定时间内被击退指定距离。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class KnockbackOnAttackEffect : ActorEffect
    {
        /// <summary>
        /// 击退持续时间（秒）
        /// </summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float KnockbackDuration { get; set; } = 0.3f;

        /// <summary>
        /// 击退距离（像素）
        /// </summary>
        [Export(PropertyHint.Range, "50,500,10")]
        public float KnockbackDistance { get; set; } = 150f;

        /// <summary>
        /// 击退缓动类型
        /// </summary>
        [Export]
        public Tween.EaseType KnockbackEaseType { get; set; } = Tween.EaseType.InOut;

        /// <summary>
        /// 击退缓动曲线
        /// </summary>
        [Export]
        public Tween.TransitionType KnockbackTransitionType { get; set; } = Tween.TransitionType.Quart;

        /// <summary>
        /// 触发击退的攻击段数（1-based）。0 表示所有段都触发
        /// </summary>
        [Export(PropertyHint.Range, "0,10,1")]
        public int TriggerHitStep { get; set; } = 1;

        private GameActor? _actor;
        private Dictionary<GameActor, Tween> _knockbackTweens = new();

        protected override void OnApply()
        {
            base.OnApply();
            _actor = Actor;  // 使用 Actor 属性而不是 Owner

            // 订阅伤害事件，只响应直接攻击（不响应区域效果等间接伤害）
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            
            // 移除所有正在进行的击退 Tween
            foreach (var tween in _knockbackTweens.Values)
            {
                if (tween != null)
                {
                    tween.Kill();
                }
            }
            _knockbackTweens.Clear();

            // 取消订阅伤害事件
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            // 只响应直接攻击，过滤掌刺/持续区域伤害等间接伤害
            if (source != DamageSource.DirectAttack)
            {
                return;
            }
            if (attacker != _actor || target == null)
            {
                return;
            }

            // 检查击退是否应该在此段触发
            // TriggerHitStep == 0 表示所有段都触发，否则只在匹配段触发
            if (TriggerHitStep > 0 && PlayerAttackTemplate.CurrentAttackHitStep != TriggerHitStep)
            {
                GD.Print($"击退: 击退触发段 {PlayerAttackTemplate.CurrentAttackHitStep} 所有触发段 {TriggerHitStep}");
                return;
            }

            // 目标必须是 CharacterBody2D（敌人）
            if (target is not CharacterBody2D targetBody)
            {
                return;
            }

            ApplyKnockback(targetBody, attacker);
        }

        private void ApplyKnockback(CharacterBody2D target, GameActor? attacker)
        {
            if (target == null || attacker == null)
            {
                return;
            }

            // 计算从攻击者指向目标的方向
            Vector2 direction = (target.GlobalPosition - attacker.GlobalPosition).Normalized();

            // 杀死已存在的击退 Tween（避免重复）
            GameActor? targetActor = target as GameActor;
            if (targetActor != null && _knockbackTweens.TryGetValue(targetActor, out var existingTween))
            {
                if (existingTween != null)
                {
                    existingTween.Kill();
                }
            }

            // 保存初始速度以便恢复
            Vector2 originalVelocity = Vector2.Zero;
            if (target != null)
            {
                originalVelocity = target.Velocity;
            }

            // 计算目标位置
            Vector2 startPos = target!.GlobalPosition;
            Vector2 endPos = startPos + direction * KnockbackDistance;

            // 创建击退 Tween
            var tween = target.CreateTween();
            tween.TweenProperty(target, "global_position", endPos, KnockbackDuration)
                .SetEase(KnockbackEaseType)
                .SetTrans(KnockbackTransitionType);

            // 在 Tween 完成时恢复原始速度
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(target))
                {
                    target.Velocity = originalVelocity;
                }

                if (targetActor != null)
                {
                    _knockbackTweens.Remove(targetActor);
                }
            }));

            // 存储 Tween 引用
            if (targetActor != null)
            {
                _knockbackTweens[targetActor] = tween;
            }
        }
    }
}
