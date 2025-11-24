using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 对话UI - 显示对话内容和选项
    /// </summary>
    public partial class DialogueUI : Control
    {
        [ExportCategory("UI References")]
        [Export] public Control DialoguePanel { get; private set; } = null!;
        [Export] public Label DialogueTextLabel { get; private set; } = null!;
        [Export] public Label SpeakerNameLabel { get; private set; } = null!;
        [Export] public VBoxContainer OptionsContainer { get; private set; } = null!;
        [Export] public Button ExitButton { get; private set; } = null!;
        [Export] public Button TalkButton { get; private set; } = null!;

        [ExportCategory("Option Buttons")]
        [Export] public Button Option1Button { get; private set; } = null!;
        [Export] public Button Option2Button { get; private set; } = null!;
        [Export] public Button Option3Button { get; private set; } = null!;
        [Export] public Button Option4Button { get; private set; } = null!;
        [Export] public Button Option5Button { get; private set; } = null!;

        // 信号
        [Signal] public delegate void OptionSelectedEventHandler(int optionIndex);
        [Signal] public delegate void ExitRequestedEventHandler();
        [Signal] public delegate void TalkRequestedEventHandler();

        private Button[] _optionButtons = new Button[5];

        public override void _Ready()
        {
            // 自动查找节点
            if (DialoguePanel == null)
            {
                DialoguePanel = GetNodeOrNull<Control>("DialoguePanel");
            }

            if (DialogueTextLabel == null)
            {
                DialogueTextLabel = GetNodeOrNull<Label>("DialoguePanel/DialogueText");
            }

            if (SpeakerNameLabel == null)
            {
                SpeakerNameLabel = GetNodeOrNull<Label>("DialoguePanel/SpeakerName");
            }

            if (OptionsContainer == null)
            {
                OptionsContainer = GetNodeOrNull<VBoxContainer>("DialoguePanel/OptionsContainer");
            }

            if (ExitButton == null)
            {
                ExitButton = GetNodeOrNull<Button>("DialoguePanel/ExitButton");
            }

            if (TalkButton == null)
            {
                TalkButton = GetNodeOrNull<Button>("DialoguePanel/TalkButton");
            }

            // 查找选项按钮
            if (Option1Button == null)
            {
                Option1Button = GetNodeOrNull<Button>("DialoguePanel/OptionsContainer/Option1");
            }
            if (Option2Button == null)
            {
                Option2Button = GetNodeOrNull<Button>("DialoguePanel/OptionsContainer/Option2");
            }
            if (Option3Button == null)
            {
                Option3Button = GetNodeOrNull<Button>("DialoguePanel/OptionsContainer/Option3");
            }
            if (Option4Button == null)
            {
                Option4Button = GetNodeOrNull<Button>("DialoguePanel/OptionsContainer/Option4");
            }
            if (Option5Button == null)
            {
                Option5Button = GetNodeOrNull<Button>("DialoguePanel/OptionsContainer/Option5");
            }

            // 存储选项按钮
            _optionButtons[0] = Option1Button;
            _optionButtons[1] = Option2Button;
            _optionButtons[2] = Option3Button;
            _optionButtons[3] = Option4Button;
            _optionButtons[4] = Option5Button;

            // 连接按钮信号
            if (ExitButton != null)
            {
                ExitButton.Pressed += OnExitPressed;
            }

            if (TalkButton != null)
            {
                TalkButton.Pressed += OnTalkPressed;
            }

            // 连接选项按钮
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                if (_optionButtons[i] != null)
                {
                    int index = i; // 捕获变量
                    _optionButtons[i].Pressed += () => OnOptionSelected(index);
                    _optionButtons[i].Visible = false;
                }
            }

            // 初始状态：隐藏
            SetDialogueVisible(false);
        }

        /// <summary>
        /// 显示对话
        /// </summary>
        public void ShowDialogue(string speakerName, string dialogueText, string[]? options = null)
        {
            SetDialogueVisible(true);

            if (SpeakerNameLabel != null)
            {
                SpeakerNameLabel.Text = speakerName;
            }

            if (DialogueTextLabel != null)
            {
                DialogueTextLabel.Text = dialogueText;
            }

            // 显示选项
            if (options != null && options.Length > 0)
            {
                ShowOptions(options);
            }
            else
            {
                HideAllOptions();
            }
        }

        /// <summary>
        /// 显示选项
        /// </summary>
        public void ShowOptions(string[] options)
        {
            HideAllOptions();

            for (int i = 0; i < options.Length && i < _optionButtons.Length; i++)
            {
                if (_optionButtons[i] != null)
                {
                    _optionButtons[i].Text = options[i];
                    _optionButtons[i].Visible = true;
                }
            }
        }

        /// <summary>
        /// 隐藏所有选项
        /// </summary>
        public void HideAllOptions()
        {
            foreach (var button in _optionButtons)
            {
                if (button != null)
                {
                    button.Visible = false;
                }
            }
        }

        /// <summary>
        /// 设置对话可见性
        /// </summary>
        public void SetDialogueVisible(bool visible)
        {
            Visible = visible;
            if (DialoguePanel != null)
            {
                DialoguePanel.Visible = visible;
            }
        }

        private void OnOptionSelected(int optionIndex)
        {
            EmitSignal(SignalName.OptionSelected, optionIndex);
        }

        private void OnExitPressed()
        {
            EmitSignal(SignalName.ExitRequested);
            SetDialogueVisible(false);
        }

        private void OnTalkPressed()
        {
            EmitSignal(SignalName.TalkRequested);
        }
    }
}

