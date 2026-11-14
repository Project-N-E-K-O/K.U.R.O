using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 受伤眩晕打断的时间减缓载体：全局 Engine.TimeScale 减缓（无视觉层）。
    /// 静态触发、单实例宿主：重叠触发时新触发覆盖旧的（重置剩余时长与倍率），
    /// 只有最后一次触发到期才恢复首次触发前的 TimeScale。
    /// 持续计时用真实时间（delta / TimeScale 还原）——"持续 N 秒"按玩家体感时间。
    /// 已知共存：NPC 对话暂停也使用 Engine.TimeScale（0→1），战斗中不会重叠，可接受。
    /// </summary>
    public static partial class DamageInterruptSlowMo
    {
        private static SlowMoHost? _host;

        /// <summary>触发全局时间减缓。durationSeconds 为保持时长，recoverySeconds 为额外恢复时长，均为真实秒。
        /// 恢复时间默认 0，保持现有受击打断调用的行为。</summary>
        public static void Trigger(float timeScale, float durationSeconds, float recoverySeconds = 0f)
        {
            if (durationSeconds <= 0f || timeScale <= 0f) return;
            EnsureHost();
            if (_host == null || !GodotObject.IsInstanceValid(_host)) return;

            _host.Begin(Mathf.Clamp(timeScale, 0.05f, 1f), durationSeconds, Mathf.Max(0f, recoverySeconds));
        }

        private static void EnsureHost()
        {
            if (_host != null && GodotObject.IsInstanceValid(_host) && !_host.IsQueuedForDeletion()) return;
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            _host = new SlowMoHost { Name = "DamageInterruptSlowMo" };
            tree.Root.AddChild(_host);
        }

        private sealed partial class SlowMoHost : Node
        {
            private float _remaining;
            private double _originalTimeScale;
            private float _slowTimeScale;
            private float _recoveryDuration;
            private float _recoveryElapsed;
            private bool _active;

            public void Begin(float timeScale, float durationSeconds, float recoverySeconds)
            {
                // 恢复途中重触发仍保留整轮演出开始前的倍率，避免恢复到中间值。
                if (!_active) _originalTimeScale = Engine.TimeScale;
                _active = true;
                _remaining = durationSeconds;
                _slowTimeScale = timeScale;
                _recoveryDuration = recoverySeconds;
                _recoveryElapsed = 0f;
                Engine.TimeScale = timeScale;
            }

            public override void _Process(double delta)
            {
                if (!_active) return;

                // 真实时间倒计时（TimeScale < 1 时 delta 已被缩放，除以 TimeScale 还原真实秒）
                float realDelta = (float)delta / Mathf.Max((float)Engine.TimeScale, 0.05f);
                if (_remaining > 0f)
                {
                    _remaining -= realDelta;
                    if (_remaining > 0f) return;
                    realDelta = -_remaining; // 跨越保持段终点的余量计入恢复段。
                    _remaining = 0f;
                }
                _recoveryElapsed += realDelta;
                if (_recoveryDuration <= 0f || _recoveryElapsed >= _recoveryDuration)
                {
                    Finish();
                    return;
                }
                float progress = Mathf.SmoothStep(0f, 1f, _recoveryElapsed / _recoveryDuration);
                Engine.TimeScale = Mathf.Lerp(_slowTimeScale, (float)_originalTimeScale, progress);
            }

            private void Finish()
            {
                _active = false;
                Engine.TimeScale = _originalTimeScale;
                QueueFree();
            }

            public override void _ExitTree()
            {
                // 兜底：场景切换/强制销毁时若减缓仍在进行，恢复时间
                if (_active)
                    Engine.TimeScale = _originalTimeScale;
                base._ExitTree();
            }
        }
    }
}
