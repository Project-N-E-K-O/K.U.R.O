using Godot;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
    /// 模式选择菜单 - 选择游戏模式
    /// </summary>
    public partial class ModeSelectionMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button StoryModeButton { get; private set; } = null!;
        [Export] public Button ArcadeModeButton { get; private set; } = null!;
        [Export] public Button EndlessModeButton { get; private set; } = null!;
        [Export] public Button TestLoadingButton { get; private set; } = null!;
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public Label TitleLabel { get; private set; } = null!;

        // 信号
        [Signal] public delegate void ModeSelectedEventHandler(string modeName);
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void TestLoadingRequestedEventHandler();

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接按钮信号
        /// 这种方式在导出版本中比 C# 委托方式更可靠
        /// </summary>
        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        // 模式选择的包装方法（避免使用 lambda）
        private void OnStoryModePressed() => OnModeSelected("Story");
        private void OnArcadeModePressed() => OnModeSelected("Arcade");
        private void OnEndlessModePressed() => OnModeSelected("Endless");

        public override void _Ready()
        {
            // 自动查找节点
            if (TitleLabel == null)
            {
                TitleLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/Title");
            }

            if (StoryModeButton == null)
            {
                StoryModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/StoryModeButton");
            }

            if (ArcadeModeButton == null)
            {
                ArcadeModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/ArcadeModeButton");
            }

            if (EndlessModeButton == null)
            {
                EndlessModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/EndlessModeButton");
            }

            if (TestLoadingButton == null)
            {
                TestLoadingButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/TestLoadingButton");
            }

            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(StoryModeButton, nameof(OnStoryModePressed));
            ConnectButtonSignal(ArcadeModeButton, nameof(OnArcadeModePressed));
            ConnectButtonSignal(EndlessModeButton, nameof(OnEndlessModePressed));
            ConnectButtonSignal(TestLoadingButton, nameof(OnTestLoadingPressed));
            ConnectButtonSignal(BackButton, nameof(OnBackPressed));
        }

        private void OnModeSelected(string modeName)
        {
            EmitSignal(SignalName.ModeSelected, modeName);
            GameLogger.Info(nameof(ModeSelectionMenu), $"选择了模式: {modeName}");
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }
        
        private void OnTestLoadingPressed()
        {
            EmitSignal(SignalName.TestLoadingRequested);
        }
    }
}

