using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Effects
{
    /// <summary>
    /// 攻击时击退敌人的效果。
    /// 当携带此效果的角色攻击敌人时，敌人会被物理击退，自动与场景碰撞停止。
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
        /// 触发击退的攻击段数（1-based）。0 表示所有段都触发
        /// </summary>
        [Export(PropertyHint.Range, "0,10,1")]
        public int TriggerHitStep { get; set; } = 1;

        private GameActor? _actor;

        protected override void OnApply()
        {
            base.OnApply();
            _actor = Actor;
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (attacker != _actor || target == null) return;

            if (TriggerHitStep > 0 && PlayerAttackTemplate.CurrentAttackHitStep != TriggerHitStep)
            {
                return;
            }

            if (target is not CharacterBody2D targetBody) return;

            Vector2 direction = (target.GlobalPosition - attacker.GlobalPosition);
            if (direction == Vector2.Zero)
            {
                direction = attacker.FacingRight ? Vector2.Right : Vector2.Left;
            }

            // 线性减速的总位移 = v0 * T / 2，故初速度需乘以 2 才能达到目标距离
            float speed = 2f * KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f);
            KnockbackDriver.Attach(targetBody, direction.Normalized(), speed, KnockbackDuration);
        }

        // ─── 内部驱动节点：每物理帧用 MoveAndCollide 推动目标，保证碰撞检测 ───

        /// <summary>
        /// 临时挂载到被击退目标上的物理驱动节点。
        /// 每物理帧通过 MoveAndCollide 施加位移，自动与静态体（墙壁/家具）碰撞停止。
        /// 在 StateMachine._PhysicsProcess 之后执行，避免与 HitState.MoveAndSlide 叠加。
        /// </summary>
        private sealed partial class KnockbackDriver : Node
        {
            private const string NodeName = "__KnockbackDriver__";

            private CharacterBody2D? _target;
            private Vector2 _direction;
            private float _initialSpeed;
            private float _duration;
            private float _elapsed;

            /// <summary>
            /// 将击退驱动节点附加到目标身上。若目标已有驱动节点则先移除。
            /// </summary>
            public static void Attach(CharacterBody2D target, Vector2 direction,
                float initialSpeed, float duration)
            {
                var existing = target.GetNodeOrNull<KnockbackDriver>(NodeName);
                if (existing != null && GodotObject.IsInstanceValid(existing))
                {
                    existing.QueueFree();
                }

                var driver = new KnockbackDriver
                {
                    Name = NodeName,
                    _target = target,
                    _direction = direction,
                    _initialSpeed = initialSpeed,
                    _duration = Mathf.Max(duration, 0.01f),
                    _elapsed = 0f,
                    // 使其在父节点 StateMachine 之后执行，避免 MoveAndSlide 叠加
                    ProcessPhysicsPriority = 10
                };
                target.AddChild(driver);
            }

            public override void _PhysicsProcess(double delta)
            {
                if (_target == null || !GodotObject.IsInstanceValid(_target)
                    || !_target.IsInsideTree())
                {
                    QueueFree();
                    return;
                }

                _elapsed += (float)delta;

                if (_elapsed >= _duration)
                {
                    // 确保 HitState 下一帧从零速度开始
                    _target.Velocity = Vector2.Zero;
                    QueueFree();
                    return;
                }

                // 线性减速：速度从 _initialSpeed 线性降到 0
                float t = _elapsed / _duration;
                float currentSpeed = _initialSpeed * (1f - t);

                Vector2 displacement = _direction * currentSpeed * (float)delta;

                // 用 MoveAndCollide 施加位移，自动与碰撞体停止
                _target.MoveAndCollide(displacement);

                // 将 Velocity 清零，防止 HitState 的 MoveAndSlide 再次叠加位移
                _target.Velocity = Vector2.Zero;
            }
        }
    }
}
