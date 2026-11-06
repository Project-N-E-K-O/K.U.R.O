using Godot;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>独立世界特效：聚焦生成位置与全局慢动作可分别启用，持续时间均为真实秒。</summary>
    [GlobalClass]
    public partial class CombatFocusEffect : Node2D
    {
        [ExportGroup("Timing")]
        /// <summary>聚焦和慢动作共同的触发延迟（真实秒）；0 = 不延迟。</summary>
        [Export(PropertyHint.Range, "0,10,0.05")] public float TriggerDelay { get; set; } = 0f;

        [ExportGroup("Camera Focus")]
        [Export] public bool FocusEnabled { get; set; } = true;
        [Export(PropertyHint.Range, "0,10,0.05")] public float FocusDuration { get; set; } = 1.2f;
        /// <summary>0 保持当前缩放；正值为聚焦期间的绝对 Zoom。</summary>
        [Export(PropertyHint.Range, "0,3,0.01")] public float FocusZoom { get; set; } = 0.6f;

        [ExportGroup("Time Slow")]
        [Export] public bool SlowMoEnabled { get; set; } = true;
        [Export(PropertyHint.Range, "0,10,0.05")] public float SlowMoDuration { get; set; } = 1.2f;
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float SlowMoTimeScale { get; set; } = 0.25f;
        /// <summary>保持阶段结束后平滑恢复到原时间倍率的真实秒数；0 = 立即恢复。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float SlowMoRecoveryDuration { get; set; } = 0.35f;

        private bool _started;
        private float _remaining;
        private float _delayRemaining;

        public override void _Ready()
        {
            _delayRemaining = Mathf.Max(0f, TriggerDelay);
            // 生成方在 AddChild 后设置 GlobalPosition；同帧延迟启动才能取得最终坐标。
            // Disabled 的预热实例不触发，启用后由首个 Process 启动。
            Callable.From(() => { if (_delayRemaining <= 0f && IsInsideTree() && CanProcess()) Begin(); }).CallDeferred();
        }

        private void Begin()
        {
            if (_started || IsQueuedForDeletion()) return;
            _started = true;
            _remaining = 0.05f;

            if (FocusEnabled && FocusDuration > 0f && GetViewport().GetCamera2D() is CameraFollow camera)
            {
                camera.FocusOn(this, FocusDuration, FocusZoom);
                _remaining = Mathf.Max(_remaining, FocusDuration);
            }
            if (SlowMoEnabled && SlowMoDuration > 0f)
                DamageInterruptSlowMo.Trigger(SlowMoTimeScale, SlowMoDuration, SlowMoRecoveryDuration);
        }

        public override void _Process(double delta)
        {
            float realDelta = (float)delta / Mathf.Max((float)Engine.TimeScale, 0.05f);
            if (!_started)
            {
                _delayRemaining -= realDelta;
                if (_delayRemaining <= 0f) Begin();
                return;
            }
            _remaining -= realDelta;
            if (_remaining <= 0f) QueueFree();
        }
    }
}
