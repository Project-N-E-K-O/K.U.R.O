using Godot;
using System.Collections.Generic;

namespace Kuros.Companions
{
    [GlobalClass]
    public partial class P2LoadoutPanel : CanvasLayer
    {
        [ExportCategory("References")]
        [Export] public NodePath SupportExecutorPath { get; set; } = new("../SupportExecutor");

        [ExportCategory("UI")]
        [Export] public NodePath PanelRootPath { get; set; } = new("Panel");
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentPath { get; set; } = new("Panel/VBox/Content");
        [Export] public NodePath SkillOptionPath { get; set; } = new("Panel/VBox/Content/SkillOption");
        [Export] public NodePath EquipmentOptionPath { get; set; } = new("Panel/VBox/Content/EquipmentOption");
        [Export] public NodePath SummaryLabelPath { get; set; } = new("Panel/VBox/Content/SummaryLabel");

        [ExportCategory("Input")]
        [Export] public string TogglePanelAction { get; set; } = "p2_toggle_loadout_panel";
        [Export] public Key TogglePanelKey { get; set; } = Key.J;
        [Export] public bool StartVisible { get; set; } = false;

        private P2SupportExecutor? _executor;
        private Control? _panelRoot;
        private Button? _toggleButton;
        private Control? _content;
        private OptionButton? _skillOption;
        private OptionButton? _equipmentOption;
        private Label? _summaryLabel;
        private bool _panelVisible;
        private bool _contentVisible = true;
        private readonly List<string> _skillIds = new();
        private readonly List<string> _equipmentIds = new();

        public override void _Ready()
        {
            ResolveDependencies();

            _panelRoot = GetNodeOrNull<Control>(PanelRootPath);
            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _content = GetNodeOrNull<Control>(ContentPath);
            _skillOption = GetNodeOrNull<OptionButton>(SkillOptionPath);
            _equipmentOption = GetNodeOrNull<OptionButton>(EquipmentOptionPath);
            _summaryLabel = GetNodeOrNull<Label>(SummaryLabelPath);

            _panelVisible = StartVisible;
            ApplyPanelVisibility();

            // Keep panel interactive by mouse but prevent keyboard focus stealing gameplay inputs.
            if (_toggleButton != null)
            {
                _toggleButton.FocusMode = Control.FocusModeEnum.None;
            }

            if (_skillOption != null)
            {
                _skillOption.FocusMode = Control.FocusModeEnum.None;
            }

            if (_equipmentOption != null)
            {
                _equipmentOption.FocusMode = Control.FocusModeEnum.None;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed += OnTogglePressed;
            }

            if (_skillOption != null)
            {
                _skillOption.ItemSelected += OnSkillSelected;
            }

            if (_equipmentOption != null)
            {
                _equipmentOption.ItemSelected += OnEquipmentSelected;
            }

            if (_executor != null)
            {
                _executor.LoadoutChanged += OnLoadoutChanged;
            }

            UpdateToggleButtonText();
            RefreshOptions();
            RefreshSummary();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            base._UnhandledInput(@event);

            if (!string.IsNullOrWhiteSpace(TogglePanelAction)
                && InputMap.HasAction(TogglePanelAction)
                && @event.IsActionPressed(TogglePanelAction))
            {
                _panelVisible = !_panelVisible;
                ApplyPanelVisibility();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            {
                return;
            }

            if (keyEvent.Keycode != TogglePanelKey)
            {
                return;
            }

            _panelVisible = !_panelVisible;
            ApplyPanelVisibility();
            GetViewport().SetInputAsHandled();
        }

        public override void _ExitTree()
        {
            if (_toggleButton != null)
            {
                _toggleButton.Pressed -= OnTogglePressed;
            }

            if (_skillOption != null)
            {
                _skillOption.ItemSelected -= OnSkillSelected;
            }

            if (_equipmentOption != null)
            {
                _equipmentOption.ItemSelected -= OnEquipmentSelected;
            }

            if (_executor != null)
            {
                _executor.LoadoutChanged -= OnLoadoutChanged;
            }

            base._ExitTree();
        }

        private void ResolveDependencies()
        {
            if (_executor != null && IsInstanceValid(_executor) && _executor.IsInsideTree())
            {
                return;
            }

            _executor = GetNodeOrNull<P2SupportExecutor>(SupportExecutorPath)
                ?? GetNodeOrNull<P2SupportExecutor>(NormalizeRelativePath(SupportExecutorPath));
        }

        private void RefreshOptions()
        {
            ResolveDependencies();
            if (_executor == null)
            {
                return;
            }

            if (_skillOption != null)
            {
                _skillOption.Clear();
                _skillIds.Clear();

                var skills = _executor.GetSupportSkills();
                for (int i = 0; i < skills.Count; i++)
                {
                    var skill = skills[i];
                    if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
                    {
                        continue;
                    }

                    _skillIds.Add(skill.SkillId);
                    _skillOption.AddItem(string.IsNullOrWhiteSpace(skill.DisplayName) ? skill.SkillId : skill.DisplayName);
                }

                SelectCurrentSkillInOption();
            }

            if (_equipmentOption != null)
            {
                _equipmentOption.Clear();
                _equipmentIds.Clear();

                var equipments = _executor.GetSupportEquipments();
                for (int i = 0; i < equipments.Count; i++)
                {
                    var equip = equipments[i];
                    if (equip == null || string.IsNullOrWhiteSpace(equip.EquipmentId))
                    {
                        continue;
                    }

                    _equipmentIds.Add(equip.EquipmentId);
                    _equipmentOption.AddItem(string.IsNullOrWhiteSpace(equip.DisplayName) ? equip.EquipmentId : equip.DisplayName);
                }

                SelectCurrentEquipmentInOption();
            }
        }

        private void SelectCurrentSkillInOption()
        {
            if (_skillOption == null || _executor == null)
            {
                return;
            }

            string current = _executor.GetEquippedSupportSkillId();
            int index = _skillIds.IndexOf(current);
            if (index >= 0)
            {
                _skillOption.Select(index);
            }
        }

        private void SelectCurrentEquipmentInOption()
        {
            if (_equipmentOption == null || _executor == null)
            {
                return;
            }

            string current = _executor.GetEquippedEquipmentId();
            int index = _equipmentIds.IndexOf(current);
            if (index >= 0)
            {
                _equipmentOption.Select(index);
            }
        }

        private void OnSkillSelected(long index)
        {
            if (_executor == null)
            {
                return;
            }

            int i = (int)index;
            if (i < 0 || i >= _skillIds.Count)
            {
                return;
            }

            _executor.EquipSupportSkill(_skillIds[i]);
            ClearInteractiveFocus();
            RefreshSummary();
        }

        private void OnEquipmentSelected(long index)
        {
            if (_executor == null)
            {
                return;
            }

            int i = (int)index;
            if (i < 0 || i >= _equipmentIds.Count)
            {
                return;
            }

            _executor.EquipSupportEquipment(_equipmentIds[i]);
            ClearInteractiveFocus();
            RefreshSummary();
        }

        private void OnLoadoutChanged(string _, string __)
        {
            RefreshOptions();
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (_summaryLabel == null || _executor == null)
            {
                return;
            }

            string keyName = OS.GetKeycodeString(TogglePanelKey);
            string toggleHint = !string.IsNullOrWhiteSpace(TogglePanelAction) ? $"{TogglePanelAction} ({keyName})" : keyName;
            _summaryLabel.Text = $"当前技能: {_executor.GetEquippedSupportSkillId()}\\n当前装备: {_executor.GetEquippedEquipmentId()}\\n治疗倍率: x{_executor.GetCurrentHealPowerMultiplier():0.00}\\n面板开关: {toggleHint}";
        }

        private void OnTogglePressed()
        {
            _contentVisible = !_contentVisible;
            if (_content != null)
            {
                _content.Visible = _contentVisible;
            }

            UpdateToggleButtonText();
        }

        private void UpdateToggleButtonText()
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.Text = _contentVisible ? "Hide" : "Show";
        }

        private void ApplyPanelVisibility()
        {
            if (_panelRoot != null)
            {
                _panelRoot.Visible = _panelVisible;
            }

            if (!_panelVisible)
            {
                ClearInteractiveFocus();
            }
        }

        private void ClearInteractiveFocus()
        {
            _toggleButton?.ReleaseFocus();
            _skillOption?.ReleaseFocus();
            _equipmentOption?.ReleaseFocus();
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }
    }
}
