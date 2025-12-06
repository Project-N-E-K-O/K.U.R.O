using Godot;
using Kuros.Core;
using Kuros.Items.Attributes;

namespace Kuros.Items.World
{
    /// <summary>
    /// 可投掷武器的世界实体版本，被抛出后命中 GameActor 会造成等同于攻击力的伤害。
    /// </summary>
    public partial class ThrowableDamageItemEntity : WorldItemEntity
    {
        [Export(PropertyHint.Range, "0,5,0.1")] public float ImpactDamageMultiplier { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,2000,1")] public float MinImpactSpeed { get; set; } = 200f;
        [Export] public bool DestroyOnImpact { get; set; } = true;

        private bool _impactArmed;
        private bool _hasDealtDamage;

        public override void ApplyThrowImpulse(Vector2 velocity)
        {
            base.ApplyThrowImpulse(velocity);
            if (velocity.LengthSquared() > 0.01f)
            {
                _impactArmed = true;
                _hasDealtDamage = false;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            if (!_impactArmed || _hasDealtDamage)
            {
                return;
            }

            int collisionCount = GetSlideCollisionCount();
            if (collisionCount <= 0)
            {
                return;
            }

            for (int i = 0; i < collisionCount; i++)
            {
                var collision = GetSlideCollision(i);
                if (collision == null)
                {
                    continue;
                }

                if (collision.GetCollider() is GameActor actor)
                {
                    if (TryDealImpactDamage(actor))
                    {
                        break;
                    }
                }
            }
        }

        private bool TryDealImpactDamage(GameActor target)
        {
            if (target == null)
            {
                return false;
            }

            float impactSpeed = Mathf.Max(Velocity.Length(), PendingVelocity.Length());
            if (impactSpeed < MinImpactSpeed)
            {
                return false;
            }

            int damage = ResolveImpactDamage();
            if (damage <= 0)
            {
                return false;
            }

            target.TakeDamage(damage, GlobalPosition, LastDroppedBy);
            _impactArmed = false;
            _hasDealtDamage = true;

            if (DestroyOnImpact)
            {
                QueueFree();
            }
            else
            {
                Velocity = Vector2.Zero;
            }

            return true;
        }

        private int ResolveImpactDamage()
        {
            float attackPower = 0f;

            if (CurrentStack != null)
            {
                attackPower = CurrentStack.GetAttributeValue(ItemAttributeIds.AttackPower);
            }
            else if (ItemDefinition != null &&
                     ItemDefinition.TryResolveAttribute(ItemAttributeIds.AttackPower, Mathf.Max(1, Quantity), out var attribute))
            {
                attackPower = attribute.Value;
            }

            float scaled = attackPower * ImpactDamageMultiplier;
            if (scaled <= 0f)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(scaled));
        }
    }
}

