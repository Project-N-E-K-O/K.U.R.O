using Godot;
using Kuros.Systems.Cutscene;

namespace Kuros.UI
{
    /// <summary>
    /// 过场"长按跳过"HUD（屏幕右下角环形按钮）。
    /// 只负责**显示**与鼠标命中判定：
    ///   · 环形进度来自 <see cref="CutsceneManager.SkipHoldProgress"/>（长按累加器在管理器里，
    ///     因为过场期间玩家节点是 ProcessMode.Disabled，复用不了 SamplePlayer 的 InputHoldTracker）；
    ///   · 喂给 circular_bar.gdshader —— 其 `removed_segments` 的实际含义是**显示弧长比例**（满 = 满环）；
    ///   · 鼠标按住环区域时 <see cref="IsButtonHeld"/> 为 true，让"长按按钮"与"长按按键"等效。
    /// 由 CutsceneManager 实例化到自建的 CanvasLayer（层 129 > 黑条的 128）。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneSkipHUD : Control
    {
        /// <summary>环形进度节点（挂 circular_bar.gdshader）。</summary>
        [Export] public TextureRect? Ring { get; set; }

        /// <summary>键位提示标签（显示当前绑定键，如"长按 Q"）；改键后自动刷新。</summary>
        [Export] public Label? KeyHintLabel { get; set; }

        /// <summary>鼠标是否正按住按钮区域（供 CutsceneManager 的长按累加读取）。</summary>
        public bool IsButtonHeld { get; private set; }

        private CutsceneManager? _manager;
        private ShaderMaterial? _ringMaterial;
        private Callable _bindingsChangedCallable;
        private bool _bindingsSubscribed;

        public override void _Ready()
        {
            Ring ??= GetNodeOrNull<TextureRect>("BottomRight/Ring");
            KeyHintLabel ??= GetNodeOrNull<Label>("BottomRight/KeyHint");
            _ringMaterial = Ring?.Material as ShaderMaterial;
            _manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
            if (_manager == null)
                GD.PushWarning("[CutsceneSkipHUD] 未找到 CutsceneManager（组 cutscene_manager），跳过按钮不会显示");

            SubscribeBindingsChanged();
            RefreshKeyHint();

            Visible = false;
        }

        public override void _ExitTree()
        {
            var settings = Kuros.Managers.GameSettingsManager.Instance;
            if (_bindingsSubscribed && settings != null && GodotObject.IsInstanceValid(settings)
                && settings.IsConnected("InputBindingsChanged", _bindingsChangedCallable))
            {
                settings.Disconnect("InputBindingsChanged", _bindingsChangedCallable);
            }
            _bindingsSubscribed = false;
        }

        private void SubscribeBindingsChanged()
        {
            var settings = Kuros.Managers.GameSettingsManager.Instance;
            if (settings == null || !GodotObject.IsInstanceValid(settings)) return;
            if (!settings.HasSignal("InputBindingsChanged")) return;

            _bindingsChangedCallable = Callable.From(RefreshKeyHint);
            if (!settings.IsConnected("InputBindingsChanged", _bindingsChangedCallable))
                settings.Connect("InputBindingsChanged", _bindingsChangedCallable);
            _bindingsSubscribed = true;
        }

        /// <summary>刷新键位提示：读取"跳过动作当前绑定的键"（含玩家改键），无绑定显示 "?"。</summary>
        private void RefreshKeyHint()
        {
            if (KeyHintLabel == null || _manager == null) return;

            string action = _manager.SkipActionName;
            var settings = Kuros.Managers.GameSettingsManager.Instance;
            string keyName = settings != null && GodotObject.IsInstanceValid(settings)
                ? settings.GetActionKeyDisplayName(action)
                : "?";
            KeyHintLabel.Text = $"{keyName}";
        }

        public override void _Process(double delta)
        {
            if (_manager == null || !GodotObject.IsInstanceValid(_manager))
            {
                Visible = false;
                IsButtonHeld = false;
                return;
            }

            Visible = _manager.CanSkip;
            IsButtonHeld = Visible
                && Ring != null
                && GodotObject.IsInstanceValid(Ring)
                && Ring.GetGlobalRect().HasPoint(GetGlobalMousePosition())
                && Input.IsMouseButtonPressed(MouseButton.Left);

            if (_ringMaterial != null)
                _ringMaterial.SetShaderParameter("removed_segments", _manager.SkipHoldProgress);
        }
    }
}
