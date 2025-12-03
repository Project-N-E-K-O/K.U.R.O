using Godot;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 负重减速效果。默认将移动速度乘以 0.85，叠加时按乘算累乘。
    /// </summary>
    [GlobalClass]
    public partial class HeavyCarrySlowEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0.1,1,0.01")]
        public float SpeedMultiplierPerStack { get; set; } = 0.85f;

        private float _originalSpeed;

        protected override void OnApply()
        {
            base.OnApply();
            Recalculate();
        }

        protected override void OnStackRefreshed()
        {
            base.OnStackRefreshed();
            Recalculate();
        }

        public override void OnRemoved()
        {
            if (Actor != null)
            {
                Actor.Speed = _originalSpeed;
            }
            base.OnRemoved();
        }

        private void Recalculate()
        {
            if (Actor == null) return;

            if (_originalSpeed == 0f)
            {
                _originalSpeed = Actor.Speed;
            }

            float totalMultiplier = Mathf.Pow(SpeedMultiplierPerStack, Mathf.Max(1, CurrentStacks));
            Actor.Speed = _originalSpeed * totalMultiplier;
        }
    }
}

