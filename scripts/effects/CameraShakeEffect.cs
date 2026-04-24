using Godot;
using Kuros.Core.Effects;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 即时镜头震动效果。
    /// 应用时找到当前活跃的 CameraFollow 并触发一次震动，随后立即过期。
    /// </summary>
    [GlobalClass]
    public partial class CameraShakeEffect : ActorEffect
    {
        /// <summary>
        /// 镜头震动强度（像素）
        /// </summary>
        [Export(PropertyHint.Range, "1,200,1")]
        public float ShakeStrength { get; set; } = 12.0f;

        protected override void OnApply()
        {
            base.OnApply();

            var camera = Actor?.GetViewport()?.GetCamera2D() as CameraFollow;
            if (camera != null)
            {
                camera.Shake(ShakeStrength);
                GD.Print($"CameraShakeEffect: 触发镜头震动，强度 {ShakeStrength}");
            }
            else
            {
                GD.PrintErr("CameraShakeEffect: 找不到 CameraFollow 节点，无法触发镜头震动");
            }
        }
    }
}
