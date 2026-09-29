using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 爆炸范围伤害脚本。
    /// 生成后立即对半径内的玩家/敌人造成伤害+径向击退，完成后销毁自身。
    /// 挂载在 BoomDmgEffect.tscn 根节点上。
    /// </summary>
    public partial class BoomDmgEffect : Node2D
    {
        /// <summary>伤害来源，由实例化方在 AddChild 前设置。</summary>
        public GameActor? Attacker { get; set; }

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,9999,1")] public int Damage { get; set; } = 5;
        /// <summary>爆炸范围的**横向**半径（px）。纵向半径 = 本值 × <see cref="VerticalSquash"/>。</summary>
        [Export(PropertyHint.Range, "0,2000,1")] public float Radius { get; set; } = 400f;
        /// <summary>纵向压缩：1 = 正圆（默认，老场景行为不变）；俯视关卡里地面上的爆炸应是"X 长 Y 短"的椭圆，
        /// 0.65 ≈ 45° 俯视的地面透视（与液团 aspect 1.55 同源）。判定范围与视觉因此对得上。</summary>
        [Export(PropertyHint.Range, "0.1,1,0.01")] public float VerticalSquash { get; set; } = 1f;

        [ExportCategory("Knockback")]
        /// <summary>击退位移距离（像素）——目标 Hit 状态在 KnockbackDuration 内匀减速滑完。</summary>
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 300f;
        /// <summary>击退位移时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.18f;

        [ExportCategory("Debug")]
        [Export] public bool ShowDebugRadius { get; set; } = false;
        [Export] public Color DebugRadiusColor { get; set; } = new Color(1f, 0f, 0f, 0.5f);

        [ExportCategory("Targets")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;

        public override void _Ready()
        {
            if (ShowDebugRadius)
                QueueRedraw();
            Callable.From(Execute).CallDeferred();
        }

        public override void _Draw()
        {
            if (!ShowDebugRadius) return;

            // 绘制用非等比缩放是安全的（纯几何，不进物理世界）——一个圆被压成椭圆
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, VerticalSquash));
            DrawCircle(Vector2.Zero, Radius, DebugRadiusColor);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        private async void Execute()
        {
            ApplyExplosion();
            if (ShowDebugRadius)
            {
                var timer = GetTree().CreateTimer(1.0);
                await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }
            QueueFree();
        }

        private void ApplyExplosion()
        {
            Vector2 origin = GlobalPosition;

            if (TargetableFactions.HasFlag(TargetableFactions.Player))
            {
                if (GetTree().GetFirstNodeInGroup("player") is GameActor playerActor
                    && IsWithinRadius(playerActor.GlobalPosition, origin))
                {
                    ApplyDamageAndKnockback(playerActor, origin);
                }
            }

            if (TargetableFactions.HasFlag(TargetableFactions.Enemy))
            {
                foreach (var node in GetTree().GetNodesInGroup("enemies"))
                {
                    if (node is GameActor enemyActor && IsWithinRadius(enemyActor.GlobalPosition, origin))
                        ApplyDamageAndKnockback(enemyActor, origin);
                }
            }

            if (TargetableFactions.HasFlag(TargetableFactions.WorldItem))
            {
                DealDamageToWorldItemsInRadius(origin);
            }
        }

        /// <summary>
        /// WorldItem 伤害：物理查询（半径 Radius 的形状）——碰撞体任意部位进入爆炸范围即命中，
        /// 与视觉接触一致（"中心点距离"判定对大碰撞体（中心到边缘可达数百像素）会在
        /// 爆炸碰到边缘时漏判）。解析接收者（FireWallA/家具）后无视方向限制结算。
        /// <see cref="VerticalSquash"/> &lt; 1 时用凸多边形近似椭圆（Godot 2D 没有椭圆碰撞形状）。
        /// </summary>
        private void DealDamageToWorldItemsInRadius(Vector2 origin)
        {
            var space = GetWorld2D()?.DirectSpaceState;
            if (space == null) return;

            Shape2D areaShape = VerticalSquash >= 0.999f
                ? new CircleShape2D { Radius = Radius }
                : BuildEllipseShape(Radius, Radius * VerticalSquash);

            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = areaShape,
                Transform = new Transform2D(0f, origin),
                CollisionMask = 1u, // layer 1：barrier StaticBody2D / 家具 RigidBody2D 碰撞体
                CollideWithAreas = true,
                CollideWithBodies = true
            };

            var damaged = new HashSet<ulong>();
            foreach (var result in space.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                if (collider.As<GodotObject>() is not Node node) continue;

                var receiver = DamageDispatcher.ResolveDamageReceiver(node, TargetableFactions.WorldItem);
                if (receiver == null || receiver is GameActor) continue; // 敌人/玩家走各自分支
                if (!damaged.Add(receiver.GetInstanceId())) continue;

                // 爆炸是全方位区域效果：无视方向性屏障的方向限制（bypassDirectionCheck）
                DamageDispatcher.DealDamage(receiver, Damage, origin, Attacker, DamageSource.AreaEffect,
                    TargetableFactions.WorldItem, false, null, null, bypassDirectionCheck: true);
            }
        }

        /// <summary>椭圆范围判定：X 半轴 = <see cref="Radius"/>、Y 半轴 = Radius × <see cref="VerticalSquash"/>。
        /// VerticalSquash = 1 时退化成原来的圆形判定（distance ≤ Radius）。</summary>
        private bool IsWithinRadius(Vector2 position, Vector2 origin)
        {
            float xRadius = Mathf.Max(0.001f, Radius);
            float yRadius = Mathf.Max(0.001f, xRadius * VerticalSquash);
            Vector2 delta = position - origin;
            float nx = delta.X / xRadius;
            float ny = delta.Y / yRadius;
            return nx * nx + ny * ny <= 1f;
        }

        /// <summary>用 N 边凸多边形近似椭圆（Godot 2D 没有椭圆碰撞形状；椭圆恒为凸，凸多边形足够）。</summary>
        private static ConvexPolygonShape2D BuildEllipseShape(float xRadius, float yRadius, int segments = 24)
        {
            var points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float t = Mathf.Tau * i / segments;
                points[i] = new Vector2(Mathf.Cos(t) * xRadius, Mathf.Sin(t) * yRadius);
            }
            return new ConvexPolygonShape2D { Points = points };
        }

        private void ApplyDamageAndKnockback(GameActor actor, Vector2 origin)
        {
            if (!GodotObject.IsInstanceValid(actor) || actor.IsDead || actor.IsDeathSequenceActive)
                return;

            // 先造成伤害（对玩家同时设置 _pendingHitKnockback = true）
            actor.TakeDamage(Damage, origin, Attacker);

            if (KnockbackDistance <= 0f) return;

            Vector2 direction = actor.GlobalPosition - origin;
            if (direction == Vector2.Zero) direction = Vector2.Up;
            Vector2 dirNormalized = direction.Normalized();

            // 位移请求：目标 Hit 状态在 KnockbackDuration 内匀减速滑完 KnockbackDistance。
            // 玩家侧先"只读地"看一眼这一下允不允许被击退（不消费）——要决定冰冻位移跟不跟；
            // 真正的放行与消费在 GameActor.ApplyKnockbackDisplacement 内部（EFFECT_STANDARD 第四条）。
            if (actor is Actors.Heroes.MainCharacter mainCharacter)
            {
                if (!mainCharacter.HasPendingHitKnockback) return;
                mainCharacter.ApplyKnockbackDisplacement(dirNormalized, KnockbackDistance, KnockbackDuration);

                // 若玩家处于 Frozen 状态且允许外力位移，同步通知（平均速度 = distance/duration）
                var frozenState = mainCharacter.StateMachine?
                    .GetNodeOrNull<Actors.Heroes.States.PlayerFrozenState>("Frozen");
                if (frozenState != null
                    && mainCharacter.StateMachine?.CurrentState == frozenState
                    && frozenState.AllowExternalDisplacementWhileFrozen)
                {
                    frozenState.ApplyExternalDisplacement(
                        dirNormalized * (KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)),
                        KnockbackDuration);
                }
            }
            else
            {
                actor.ApplyKnockbackDisplacement(dirNormalized, KnockbackDistance, KnockbackDuration);
            }
        }
    }
}
