using Godot;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 示例效果：调整角色移动速度。
    /// </summary>
    public partial class SimpleSpeedEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,5,0.1")]
        public float SpeedMultiplier { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "-100,100,0.1")]
        public float SpeedOffset { get; set; } = 0f;

        private float _originalSpeed;

        protected override void OnApply()
        {
            _originalSpeed = Actor.Speed;
            ApplyModifier();
        }

        protected override void OnTick(double delta)
        {
            // 处理动态变化或保证与动画同步
            ApplyModifier();
        }

        public override void OnRemoved()
        {
            Actor.Speed = _originalSpeed;
            base.OnRemoved();
        }

        private void ApplyModifier()
        {
            Actor.Speed = _originalSpeed * SpeedMultiplier + SpeedOffset;
        }
    }
}

