using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 瞬移到前方固定距离并沿路径对敌人造成伤害。
    /// </summary>
    [GlobalClass]
    public partial class TeleportStrikeEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "10,1000,10")]
        public float TeleportDistance { get; set; } = 250f;

        [Export(PropertyHint.Range, "1,500,1")]
        public int Damage { get; set; } = 40;

        [Export(PropertyHint.Range, "5,200,1")]
        public float DamagePathRadius { get; set; } = 40f;

        protected override void OnApply()
        {
            base.OnApply();
            if (Actor == null)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            var start = Actor.GlobalPosition;
            var direction = Actor.FacingRight ? Vector2.Right : Vector2.Left;
            var target = start + direction * TeleportDistance;

            var originalLayer = Actor.CollisionLayer;
            var originalMask = Actor.CollisionMask;

            Actor.CollisionLayer = 0;
            Actor.CollisionMask = 0;
            Actor.GlobalPosition = target;
            DamageEnemiesAlongPath(start, target);
            Actor.CollisionLayer = originalLayer;
            Actor.CollisionMask = originalMask;

            Controller?.RemoveEffect(this);
        }

        private void DamageEnemiesAlongPath(Vector2 start, Vector2 end)
        {
            if (Actor == null) return;
            var tree = Actor.GetTree();
            if (tree == null) return;

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || enemy == Actor)
                {
                    continue;
                }

                if (DistanceToSegment(enemy.GlobalPosition, start, end) <= DamagePathRadius)
                {
                    enemy.TakeDamage(Damage, Actor.GlobalPosition, Actor);
                }
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lengthSquared = ab.LengthSquared();
            if (lengthSquared <= Mathf.Epsilon)
            {
                return point.DistanceTo(a);
            }

            float t = Mathf.Clamp((point - a).Dot(ab) / lengthSquared, 0f, 1f);
            var projection = a + ab * t;
            return point.DistanceTo(projection);
        }
    }
}

