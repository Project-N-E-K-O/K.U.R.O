using Godot;
using Kuros.Core.Effects;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 镜头震动效果，支持延迟触发。
    /// </summary>
    [GlobalClass]
    public partial class CameraShakeEffect : ActorEffect
    {
        /// <summary>
        /// 镜头震动强度（像素）
        /// </summary>
        [Export(PropertyHint.Range, "1,200,1")]
        public float ShakeStrength { get; set; } = 12.0f;

        /// <summary>
        /// 延迟触发时间（秒），0 表示立即触发
        /// </summary>
        [Export(PropertyHint.Range, "0,10,0.1")]
        public float ShakeDelay { get; set; } = 0.0f;

        private Timer? _delayTimer;

        protected override void OnApply()
        {
            base.OnApply();

            if (ShakeDelay <= 0f)
            {
                DoShake();
                return;
            }

            if (Duration < ShakeDelay + 0.1f)
            {
                Duration = ShakeDelay + 0.1f;
            }

            _delayTimer = new Timer { OneShot = true, WaitTime = ShakeDelay };
            _delayTimer.Timeout += OnDelayTimeout;
            AddChild(_delayTimer);
            _delayTimer.Start();
        }

        public override void OnRemoved()
        {
            CleanupTimer();
            base.OnRemoved();
        }

        private void OnDelayTimeout()
        {
            CleanupTimer();
            DoShake();
        }

        private void CleanupTimer()
        {
            if (_delayTimer != null)
            {
                _delayTimer.Stop();
                _delayTimer.Timeout -= OnDelayTimeout;
                _delayTimer.QueueFree();
                _delayTimer = null;
            }
        }

        private void DoShake()
        {
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
