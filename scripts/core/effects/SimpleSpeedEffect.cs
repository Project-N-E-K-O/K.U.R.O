using Godot;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 示例效果：调整角色移动速度。
    /// </summary>
    public partial class SimpleSpeedEffect : ActorEffect
    {
        [Export] public float SpeedMultiplier = 0.5f;

        private float _originalSpeed;

        protected override void OnApply()
        {
            _originalSpeed = Actor.Speed;
            Actor.Speed *= SpeedMultiplier;
        }

        public override void OnRemoved()
        {
            Actor.Speed = _originalSpeed;
            base.OnRemoved();
        }
    }
}

