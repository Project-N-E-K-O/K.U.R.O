using Godot;
using System.Collections.Generic;

namespace Kuros.Systems.InputTracking
{
    /// <summary>
    /// 按键时长追踪器 + 同键长短按仲裁。
    /// 普通动作：按 action 独立追踪（即时按下/短按/长按）。
    /// 仲裁键（同一键绑定 ≥2 个动作且其一标记 isLongPress）：严格分流——
    ///   按下 → 短按动作不立即触发（延迟到松开确认）；
    ///   按住 ≥ 阈值 → 触发长按动作，短按动作不再触发；
    ///   松开 &lt; 阈值 → 触发短按动作。
    /// 实现"攻击=短按空格、拾取=长按空格"这类同键分流。
    /// </summary>
    public class InputHoldTracker
    {
        private readonly Dictionary<string, float> _thresholds = new();
        private readonly Dictionary<string, bool> _longPressFlags = new();
        private readonly Dictionary<string, float> _pressTimes = new();
        private readonly HashSet<string> _justPressedThisFrame = new();
        private readonly HashSet<string> _longPressTriggeredThisFrame = new();
        private readonly HashSet<string> _shortPressDetectedThisFrame = new();
        // 仲裁键：松开 < 阈值确认的短按动作（本帧 true，读后即焚）
        private readonly HashSet<string> _arbitratedShortPressThisFrame = new();

        // 键 → 动作列表（正数 = 键盘 physical_keycode，负数 = 鼠标 -buttonIndex）
        private readonly Dictionary<int, List<string>> _keyToActions = new();
        // 仲裁键按下状态
        private readonly Dictionary<int, float> _arbitratedPressTime = new();
        private readonly Dictionary<int, bool> _arbitratedLongFired = new();

        /// <summary>注册动作。isLongPress = true 表示该动作通过长按触发（与同键其他动作形成长短按分流）。</summary>
        public void Register(string actionName, float longPressThreshold = 0.4f, bool isLongPress = false)
        {
            _thresholds[actionName] = longPressThreshold;
            _longPressFlags[actionName] = isLongPress;
            RebuildKeyMap();
        }

        /// <summary>取消注册。</summary>
        public void Unregister(string actionName)
        {
            _thresholds.Remove(actionName);
            _longPressFlags.Remove(actionName);
            _pressTimes.Remove(actionName);
            _justPressedThisFrame.Remove(actionName);
            _longPressTriggeredThisFrame.Remove(actionName);
            _shortPressDetectedThisFrame.Remove(actionName);
            _arbitratedShortPressThisFrame.Remove(actionName);
            RebuildKeyMap();
        }

        /// <summary>从 InputMap 重建"键 → 动作"映射（改键后调用 Register 自动刷新）。</summary>
        private void RebuildKeyMap()
        {
            _keyToActions.Clear();
            foreach (var action in _thresholds.Keys)
            {
                foreach (var e in Godot.InputMap.ActionGetEvents(action))
                {
                    int key = e switch
                    {
                        InputEventKey k => (int)k.PhysicalKeycode,
                        InputEventMouseButton m => -(int)m.ButtonIndex,
                        _ => 0
                    };
                    if (key == 0) continue;
                    if (!_keyToActions.TryGetValue(key, out var list))
                    {
                        _keyToActions[key] = list = new List<string>();
                    }
                    if (!list.Contains(action)) list.Add(action);
                }
            }
        }

        /// <summary>该键是否进入仲裁（≥2 动作且其一为长按）。</summary>
        private bool IsArbitratedKey(int key)
        {
            if (!_keyToActions.TryGetValue(key, out var list)) return false;
            if (list.Count < 2) return false;
            foreach (var action in list)
            {
                if (_longPressFlags.TryGetValue(action, out bool lp) && lp) return true;
            }
            return false;
        }

        /// <summary>动作当前绑定的键（首个事件；正数键盘/负数鼠标；无返回 0）。</summary>
        public int GetActionKey(string actionName)
        {
            if (_keyToActions == null) return 0;
            foreach (var (key, list) in _keyToActions)
            {
                if (list.Contains(actionName)) return key;
            }
            return 0;
        }

        /// <summary>清空临时按键状态，保留注册绑定。</summary>
        public void ResetTransientState()
        {
            _pressTimes.Clear(); _justPressedThisFrame.Clear();
            _longPressTriggeredThisFrame.Clear(); _shortPressDetectedThisFrame.Clear();
            _arbitratedShortPressThisFrame.Clear(); _arbitratedPressTime.Clear(); _arbitratedLongFired.Clear();
        }

        /// <summary>每帧调用一次。</summary>
        public void Process(float delta)
        {
            _justPressedThisFrame.Clear();
            _longPressTriggeredThisFrame.Clear();
            _shortPressDetectedThisFrame.Clear();
            _arbitratedShortPressThisFrame.Clear();

            // ── 仲裁键处理（同键长短按分流）──
            foreach (var key in new List<int>(_keyToActions.Keys))
            {
                if (!IsArbitratedKey(key)) continue;
                ProcessArbitratedKey(key, delta);
            }

            // ── 普通动作独立追踪（非仲裁键的动作）──
            foreach (var (action, threshold) in _thresholds)
            {
                if (IsActionOnArbitratedKey(action)) continue;

                if (!_pressTimes.TryGetValue(action, out float elapsed))
                    elapsed = 0f;

                if (Godot.Input.IsActionJustPressed(action))
                {
                    _pressTimes[action] = 0f;
                    _justPressedThisFrame.Add(action);
                }
                else if (Godot.Input.IsActionPressed(action))
                {
                    float newElapsed = elapsed + delta;
                    _pressTimes[action] = newElapsed;

                    if (newElapsed >= threshold && elapsed < threshold)
                    {
                        _longPressTriggeredThisFrame.Add(action);
                    }
                }
                else if (elapsed > 0f)
                {
                    if (elapsed < threshold)
                        _shortPressDetectedThisFrame.Add(action);
                    _pressTimes[action] = 0f;
                }
            }
        }

        private bool IsActionOnArbitratedKey(string action)
        {
            foreach (var (key, list) in _keyToActions)
            {
                if (list.Contains(action) && IsArbitratedKey(key)) return true;
            }
            return false;
        }

        /// <summary>仲裁键严格分流：按下延迟确认、阈值触发长按、松开确认短按。</summary>
        private void ProcessArbitratedKey(int key, float delta)
        {
            var actions = _keyToActions[key];
            bool anyJustPressed = false;
            bool anyPressed = false;
            foreach (var action in actions)
            {
                if (Godot.Input.IsActionJustPressed(action)) anyJustPressed = true;
                if (Godot.Input.IsActionPressed(action)) anyPressed = true;
            }

            if (anyJustPressed && !_arbitratedPressTime.ContainsKey(key))
            {
                // 按下：开始计时，短按动作本帧不触发
                _arbitratedPressTime[key] = 0f;
                _arbitratedLongFired[key] = false;
            }

            if (anyPressed && _arbitratedPressTime.TryGetValue(key, out float elapsed))
            {
                float newElapsed = elapsed + delta;
                _arbitratedPressTime[key] = newElapsed;

                // 达阈值：触发长按动作（只触发一次），短按动作不再有机会
                float threshold = GetArbitratedThreshold(actions);
                if (!_arbitratedLongFired[key] && newElapsed >= threshold)
                {
                    _arbitratedLongFired[key] = true;
                    foreach (var action in actions)
                    {
                        if (_longPressFlags.TryGetValue(action, out bool lp) && lp)
                        {
                            _longPressTriggeredThisFrame.Add(action);
                        }
                    }
                }
            }

            if (!anyPressed && _arbitratedPressTime.ContainsKey(key))
            {
                // 松开：< 阈值且未触发长按 → 确认短按动作
                float held = _arbitratedPressTime[key];
                if (held < GetArbitratedThreshold(actions) && !_arbitratedLongFired[key])
                {
                    foreach (var action in actions)
                    {
                        if (!_longPressFlags.TryGetValue(action, out bool lp) || !lp)
                        {
                            _arbitratedShortPressThisFrame.Add(action);
                        }
                    }
                }
                _arbitratedPressTime.Remove(key);
                _arbitratedLongFired.Remove(key);
            }
        }

        private float GetArbitratedThreshold(List<string> actions)
        {
            foreach (var action in actions)
            {
                if (_thresholds.TryGetValue(action, out float t)) return t;
            }
            return 0.4f;
        }

        // ── 查询 API ──

        /// <summary>即时按下（Edge）：所有动作按下帧即时返回 Input 值（闪避/攻击零延迟，无粘滞感）。
        /// 长短按分流的"长按接管"由 IsActionHeld/WasLongPressTriggered 实现：
        /// 长按激活后短按动作的持续按住被屏蔽（防连击），已触发的即时动作不可撤销。</summary>
        public bool WasActionJustPressed(string actionName)
        {
            return Godot.Input.IsActionJustPressed(actionName);
        }

        /// <summary>按住中（Level）：普通动作 = Input 值；
        /// 仲裁键严格分流——长按动作达阈值后才视为按住（奔跑），短按动作长按激活后抑制按住（防连击）。</summary>
        public bool IsActionHeld(string actionName)
        {
            if (IsActionOnArbitratedKey(actionName))
            {
                int key = GetActionKey(actionName);
                bool isLong = _longPressFlags.TryGetValue(actionName, out bool lp) && lp;
                bool longFired = _arbitratedLongFired.TryGetValue(key, out bool fired) && fired;
                bool pressed = Godot.Input.IsActionPressed(actionName);
                if (isLong)
                {
                    return longFired && pressed;   // 长按动作：达阈值后才生效
                }
                return !longFired && pressed;      // 短按动作：长按激活后抑制
            }
            return Godot.Input.IsActionPressed(actionName);
        }

        public bool IsActionPressed(string actionName) => Godot.Input.IsActionPressed(actionName);

        /// <summary>按下帧触发（Edge，读后即焚）。</summary>
        public bool WasActionJustPressedLegacy(string actionName)
        {
            return _justPressedThisFrame.Contains(actionName);
        }

        /// <summary>长按持续中（Level）。</summary>
        public bool IsLongPressHeld(string actionName)
        {
            if (!_thresholds.TryGetValue(actionName, out float threshold)) return false;
            if (IsActionOnArbitratedKey(actionName)) return false; // 仲裁键长按由仲裁管理
            if (!_pressTimes.TryGetValue(actionName, out float elapsed)) return false;
            return elapsed >= threshold;
        }

        /// <summary>长按刚触发（Edge，读后即焚）。仲裁键长按动作在阈值帧触发。</summary>
        public bool WasLongPressTriggered(string actionName)
        {
            return _longPressTriggeredThisFrame.Contains(actionName);
        }

        /// <summary>短按（松开帧，Edge，读后即焚）。仲裁键短按动作在松开确认时触发。</summary>
        public bool WasShortPressed(string actionName)
        {
            if (_shortPressDetectedThisFrame.Contains(actionName)) return true;
            return _arbitratedShortPressThisFrame.Contains(actionName);
        }

        /// <summary>当前按住时长（秒）。未按住返回 0。</summary>
        public float GetHoldDuration(string actionName)
        {
            _pressTimes.TryGetValue(actionName, out float elapsed);
            return elapsed;
        }
    }
}
