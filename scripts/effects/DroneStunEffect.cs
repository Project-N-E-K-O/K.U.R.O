using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 无人机眩晕效果（**世界型一次性效果**）：实例化后立刻在 <see cref="WorldSpawnPosition"/>（无则根节点位置）
    /// 做一次空间扫描，命中带 `StunArea` 的敌人则施加 <see cref="FreezeEffect"/>（眩晕 <see cref="Duration"/> 秒），随后自毁。
    ///
    /// 用 Node2D 而非 ActorEffect：本效果的位置由世界坐标决定，**不需要宿主 actor**。
    /// 旧实现是 ActorEffect，破坏时由物品管线挂到 `LastDroppedBy`（最后投掷者）身上——drone 死亡掉落、
    /// 从未被拾取投掷的家具没有投掷者，于是该效果被直接丢弃（"死亡掉落的无人机炸了不眩晕 netAdmin"）。
    /// 世界型与投掷者无关，两种来源都能生效。
    /// </summary>
    [GlobalClass]
    public partial class DroneStunEffect : Node2D, IWorldSpawnable
    {
        /// <summary>由生成方（投掷/破坏管线）注入的世界落点。</summary>
        public Vector2? WorldSpawnPosition { get; set; }

        /// <summary>眩晕持续时长（秒）。</summary>
        [Export(PropertyHint.Range, "0,60,0.1")] public float Duration { get; set; } = 6.0f;

        private bool _scanned;

        public override void _Process(double delta)
        {
            // 扫描放在首个"可处理帧"而不是 _Ready：物品的投掷破坏特效**预热**会以
            // ProcessMode.Disabled + 隐藏 的方式实例化本场景一次（RigidBodyWorldItemEntity.WarmUpThrowDestroyEffectShaders），
            // _Ready 照跑，而 Disabled 的实例永远走不到这里——不会被预热误当成一次真触发。
            if (_scanned) return;
            _scanned = true;
            InstantScan();
        }

        private void InstantScan()
        {
            if (WorldSpawnPosition.HasValue)
                GlobalPosition = WorldSpawnPosition.Value;

            var area = GetNodeOrNull<Area2D>("Area2D");
            var shapeNode = area?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            var spaceState = area?.GetWorld2D()?.DirectSpaceState;
            if (shapeNode?.Shape == null || spaceState == null)
            {
                QueueFree();
                return;
            }

            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = new Transform2D(0f, GlobalPosition),
                CollisionMask = 1u,
                CollideWithAreas = true,
                CollideWithBodies = false,
            };

            foreach (var result in spaceState.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var colliderVar)) continue;
                if (colliderVar.As<GodotObject>() is not Area2D other) continue;
                if (other.Name != "StunArea") continue;

                var enemy = other.GetParent()?.GetParent() as GameActor;
                if (enemy == null || !IsInstanceValid(enemy)) continue;
                if (enemy.ActiveImmunities.HasFlag(ImmunityFlags.Stun)) continue;

                // 同一敌人身上的旧无人机眩晕先移除，再压新的（避免叠成两份 FreezeEffect）
                var existing = enemy.EffectController?.GetEffect<FreezeEffect>();
                if (existing?.EffectId?.StartsWith("drone_stun_") == true)
                    enemy.RemoveEffect(existing.EffectId);
                enemy.FrozenStateRemainingTime = 0f;

                enemy.ApplyEffect(new FreezeEffect
                {
                    Duration = Duration,
                    EffectId = $"drone_stun_{GetInstanceId()}"
                });
                break;
            }

            QueueFree();
        }
    }
}
