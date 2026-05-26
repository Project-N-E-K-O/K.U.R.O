using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 特效自动销毁脚本。
    /// - 挂载在 AnimatedSprite2D 上
    /// - 生成时由外部赋值 FacingRight，_Ready 中一次性设定翻转
    /// - 动画完成后自动销毁
    /// </summary>
    public partial class EffectAutoDestroy : AnimatedSprite2D
    {
        /// <summary>
        /// 由生成方在 AddChild 之前或之后赋值（同 LaserBeam 模式）。
        /// true = 朝右（不翻转），false = 朝左（翻转 X）。
        /// </summary>
        public bool FacingRight { get; set; } = true;

        public override void _Ready()
        {
            // 根据朝向一次性翻转
            if (!FacingRight)
            {
                Scale = new Vector2(-Scale.X, Scale.Y);
            }

            // 确保动画正在播放
            if (!IsPlaying())
            {
                Play();
            }

            // 动画完成后销毁
            AnimationFinished += OnAnimationFinished;
        }

        private void OnAnimationFinished()
        {
            AnimationFinished -= OnAnimationFinished;
            QueueFree();
        }
    }
}


