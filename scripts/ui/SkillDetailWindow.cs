using Godot;
using System.Linq;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    /// <summary>
    /// 技能详情窗口 - 显示所有技能的详细信息
    /// </summary>
    public partial class SkillDetailWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        private BuildCardList _cards = null!;

        private bool _isOpen = false;

        public bool IsOpen => _isOpen;

        [Signal] public delegate void SkillDetailClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;

            CacheNodeReferences();
            RefreshSkillData();
            HideWindow();
        }

        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/CloseButton");
            _cards = GetNode<BuildCardList>("MainPanel/Body/BuildCardList");

            ConnectButtonSignal(CloseButton, nameof(HideWindow));
        }

        private void RefreshSkillData()
        {
            var builds = BuildSelectionManager.Instance;
            var cards = builds == null
                ? Enumerable.Empty<RunCard>()
                : builds.PickedEffectIds
                    .Select(pick => (Definition: builds.FindEffectById(pick.Key), Stacks: pick.Value))
                    .Where(pick => pick.Definition != null)
                    .Select(pick => RunCard.FromEffect(pick.Definition!, pick.Stacks, active: true));
            _cards.Display(cards, "当前尚未拥有技能。");
        }

        public void ShowWindow()
        {
            if (_isOpen) return;

            RefreshSkillData();

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;

            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
            }

            var parent = GetParent();
            if (parent != null)
            {
                parent.MoveChild(this, parent.GetChildCount() - 1);
            }
        }

        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            _isOpen = false;

            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }

            EmitSignal(SignalName.SkillDetailClosed);
        }

        public override void _Input(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: true))
            {
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: false))
            {
                return;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: false, useSetInputAsHandled: true))
            {
                return;
            }
        }

        private bool TryHandleCloseInput(InputEvent @event, bool useAcceptEvent, bool useSetInputAsHandled)
        {
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                return false;
            }

            bool isEscKey = @event.IsActionPressed("ui_cancel") ||
                (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape);

            if (isEscKey)
            {
                HideWindow();
                if (useSetInputAsHandled) GetViewport().SetInputAsHandled();
                if (useAcceptEvent) AcceptEvent();
                return true;
            }
            return false;
        }
    }
}
