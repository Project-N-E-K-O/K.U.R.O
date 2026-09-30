using Godot;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 持续镜头震动（全局反馈）：在整个存活期内**每帧**把 <see cref="CameraFollow"/> 的抖动强度顶回
    /// <see cref="ShakeStrength"/>，用来配"攻击 Active 期间一直震"。
    ///
    /// 与一次性脉冲的 <see cref="CameraShakeEffect"/> 平级（不动它）：那个首帧 <c>Shake()</c> 一次即
    /// <c>SetProcess(false)</c>，强度随后按 <c>CameraFollow.ShakeRecoverySpeed</c> 自行衰减（默认 16px/s，
    /// 强度 50 约 3 秒衰减完）——所以"把 Duration 调大"换不来长震屏，Duration 只管节点自己活多久。
    ///
    /// 生成路径同 CameraShakeEffect：必须是 Node2D + 自管理（走 AttackEffectEntry 的世界分支），
    /// 配 <c>SpawnTiming = OnActive</c> + <c>LifecycleBinding = OnActiveEnd</c> 由攻击阶段结束负责收掉；
    /// <see cref="Duration"/> 只是没人销毁时的自毁兜底。
    /// </summary>
    [GlobalClass]
    public partial class SustainedCameraShakeEffect : Node2D
    {
        /// <summary>镜头震动强度（像素）：每帧顶到该值，不衰减。</summary>
        [Export(PropertyHint.Range, "1,200,1")] public float ShakeStrength { get; set; } = 12.0f;

        /// <summary>延迟启动时间（秒）：延迟期内不震（0 = 首个可处理帧就开始）。</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float ShakeDelay { get; set; } = 0.0f;

        /// <summary>存活时长（秒，自销毁兜底）——配了 LifecycleBinding 时用不到（阶段结束会先销毁）。
        /// 节点总寿命 = max(Duration, ShakeDelay + 0.1)，保证延迟没到不会被提前销毁。</summary>
        [Export(PropertyHint.Range, "0,10,0.05")] public float Duration { get; set; } = 1.0f;

        private float _elapsed;
        private CameraFollow? _camera;
        private bool _warnedNoCamera;

        public override void _Process(double delta)
        {
            // 全部逻辑走 _Process 而非 _Ready：物品投掷会先预热 OnThrowDestroy 特效场景
            //（隐藏 + ProcessMode.Disabled + 入树，见 RigidBodyWorldItemEntity.WarmUpThrowDestroyEffectShaders），
            // 那时 _Ready 照跑、这里不会——预热实例不会被误当成一次真触发。
            _elapsed += (float)delta;

            if (_elapsed >= Mathf.Max(Duration, ShakeDelay + 0.1f))
            {
                QueueFree();
                return;
            }
            if (_elapsed < ShakeDelay) return;

            if (_camera == null || !GodotObject.IsInstanceValid(_camera))
                _camera = GetViewport()?.GetCamera2D() as CameraFollow;

            if (_camera == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    GD.PushWarning("[SustainedCameraShakeEffect] 找不到 CameraFollow，无法触发镜头震动");
                }
                return;
            }

            _camera.Shake(ShakeStrength);   // 每帧顶满 → 强度不衰减，持续整个存活期
        }
    }
}
