using System;
using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 爆炸范围伤害脚本。
    /// 生成后立即对半径内的玩家/敌人造成伤害+径向击退，完成后销毁自身。
    /// 挂载在 BoomDmgEffect.tscn 根节点上。
    /// </summary>
    public partial class BoomDmgEffect : Node2D
    {
        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,9999,1")] public int Damage { get; set; } = 5;
        [Export(PropertyHint.Range, "0,2000,1")] public float Radius { get; set; } = 400f;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 300f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.18f;
        /// <summary>
        /// 直接指定击退速度（像素/秒）。若 > 0 则覆盖 KnockbackDistance/KnockbackDuration 的换算结果。
        /// </summary>
        [Export(PropertyHint.Range, "0,6000,1")] public float KnockbackSpeed { get; set; } = 2000f;

        [ExportCategory("Targets")]
        /// <summary>爆炸是否作用于玩家。</summary>
        [Export] public bool AffectPlayer { get; set; } = true;
        /// <summary>爆炸是否作用于敌人。</summary>
        [Export] public bool AffectEnemies { get; set; } = false;

        public override void _Ready()
        {
            // GlobalPosition 由生成方在 AddChild 之后赋值；
            // 用 CallDeferred 将爆炸逻辑推迟到同帧末尾，确保此时位置已正确设置。
            Callable.From(Execute).CallDeferred();
        }

        private void Execute()
        {
            ApplyExplosion();
            QueueFree();
        }

        private void ApplyExplosion()
        {
            Vector2 origin = GlobalPosition;

            if (AffectPlayer)
            {
                if (GetTree().GetFirstNodeInGroup("player") is GameActor playerActor
                    && IsWithinRadius(playerActor, origin))
                {
                    ApplyDamageAndKnockback(playerActor, origin);
                }
            }

            if (AffectEnemies)
            {
                foreach (var node in GetTree().GetNodesInGroup("enemies"))
                {
                    if (node is GameActor enemyActor && IsWithinRadius(enemyActor, origin))
                        ApplyDamageAndKnockback(enemyActor, origin);
                }
            }
        }

        private bool IsWithinRadius(Node2D target, Vector2 origin)
            => target.GlobalPosition.DistanceTo(origin) <= Radius;

        private void ApplyDamageAndKnockback(GameActor actor, Vector2 origin)
        {
            if (!GodotObject.IsInstanceValid(actor) || actor.IsDead || actor.IsDeathSequenceActive)
                return;

            // 先造成伤害（对玩家同时设置 _pendingHitKnockback = true）
            actor.TakeDamage(Damage, origin, null);

            // 计算击退速度
            float speed = KnockbackSpeed > 0f
                ? KnockbackSpeed
                : KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f);

            if (speed <= 0f) return;

            Vector2 direction = actor.GlobalPosition - origin;
            if (direction == Vector2.Zero) direction = Vector2.Up;

            Vector2 knockbackVelocity = direction.Normalized() * speed;

            // 玩家：通过 ConsumePendingHitKnockback 走标准击退路径
            if (actor is Actors.Heroes.MainCharacter mainCharacter)
            {
                if (mainCharacter.ConsumePendingHitKnockback())
                {
                    mainCharacter.Velocity = knockbackVelocity;

                    // 若玩家处于 Frozen 状态且允许外力位移，同步通知
                    var frozenState = mainCharacter.StateMachine?
                        .GetNodeOrNull<Actors.Heroes.States.PlayerFrozenState>("Frozen");
                    if (frozenState != null
                        && mainCharacter.StateMachine?.CurrentState == frozenState
                        && frozenState.AllowExternalDisplacementWhileFrozen)
                    {
                        frozenState.ApplyExternalDisplacement(knockbackVelocity, KnockbackDuration);
                    }
                }
            }
            else
            {
                // 敌人：直接覆写速度（TakeDamage 已触发 Hit 状态）
                actor.Velocity = knockbackVelocity;
            }
        }
    }
}
