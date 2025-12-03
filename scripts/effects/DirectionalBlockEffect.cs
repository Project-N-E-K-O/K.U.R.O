using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 在持续时间内阻挡来自前方一定角度的伤害。
    /// </summary>
    [GlobalClass]
    public partial class DirectionalBlockEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "30,360,5")] public float BlockArcDegrees = 120f;
        [Export(PropertyHint.Range, "0,1,0.05")] public float ResidualDamageRatio = 0f;
        [Export] public bool EndsOnSuccessfulBlock = true;

        private bool _isActive;

        protected override void OnApply()
        {
            base.OnApply();
            if (Actor == null) return;
            Actor.DamageIntercepted += OnDamageIntercepted;
            _isActive = true;
        }

        public override void OnRemoved()
        {
            if (Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }
            _isActive = false;
            base.OnRemoved();
        }

        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (!_isActive || args.Target != Actor) return false;

            if (!IsWithinArc(args))
            {
                return false;
            }

            int reducedDamage = Mathf.RoundToInt(args.Damage * Mathf.Clamp(ResidualDamageRatio, 0f, 1f));
            if (reducedDamage <= 0)
            {
                args.Damage = 0;
                args.IsBlocked = true;
            }
            else
            {
                args.Damage = reducedDamage;
            }

            if (EndsOnSuccessfulBlock && Controller != null)
            {
                Controller.RemoveEffect(this);
            }

            return true;
        }

        private bool IsWithinArc(GameActor.DamageEventArgs args)
        {
            var forward = args.Forward;
            var attackDir = args.AttackDirection;
            if (attackDir == Vector2.Zero)
            {
                // fallback to forward direction (assume frontal attack)
                attackDir = -forward;
            }

            attackDir = attackDir.Normalized();
            forward = forward.Normalized();

            // attack direction points from attacker to actor, so invert for direction from actor to attacker
            var toAttacker = -attackDir;
            float dot = Mathf.Clamp(forward.Dot(toAttacker), -1f, 1f);
            float angle = Mathf.RadToDeg(Mathf.Acos(dot));
            return angle <= BlockArcDegrees * 0.5f;
        }
    }
}


