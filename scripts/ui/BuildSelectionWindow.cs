using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class BuildSelectionWindow : Control
    {
        [Export] public PanelContainer? Panel { get; set; }
        [Export] public Label? TitleLabel { get; set; }
        [Export] public Label? HintLabel { get; set; }

        [Export] public VBoxContainer? Card0 { get; set; }
        [Export] public Label? NameLabel0 { get; set; }
        [Export] public Label? BuildClassLabel0 { get; set; }
        [Export] public Label? DescLabel0 { get; set; }
        [Export] public Label? ProgressLabel0 { get; set; }

        [Export] public VBoxContainer? Card1 { get; set; }
        [Export] public Label? NameLabel1 { get; set; }
        [Export] public Label? BuildClassLabel1 { get; set; }
        [Export] public Label? DescLabel1 { get; set; }
        [Export] public Label? ProgressLabel1 { get; set; }

        [Export] public VBoxContainer? Card2 { get; set; }
        [Export] public Label? NameLabel2 { get; set; }
        [Export] public Label? BuildClassLabel2 { get; set; }
        [Export] public Label? DescLabel2 { get; set; }
        [Export] public Label? ProgressLabel2 { get; set; }

        private VBoxContainer[] _cards = null!;
        private Label[] _nameLabels = null!;
        private Label[] _buildClassLabels = null!;
        private Label[] _descLabels = null!;
        private Label[] _progressLabels = null!;

        private List<BuildEffectDefinition> _options = new();
        private Action<BuildEffectDefinition>? _onConfirmed;
        private int _selectedIndex;
        private bool _isOpen;

        private readonly Color _highlightColor = new(0.2f, 0.4f, 0.7f, 0.7f);
        private readonly Color _normalColor = new(0.1f, 0.1f, 0.15f, 0.5f);
        private readonly Color _selectedTextColor = Colors.White;
        private readonly Color _normalTextColor = new(0.7f, 0.7f, 0.7f, 1f);

        public override void _Ready()
        {
            ResolveExports();
            Visible = false;
            ProcessMode = ProcessModeEnum.Always;
        }

        private void ResolveExports()
        {
            Panel ??= GetNodeOrNull<PanelContainer>("Panel");

            Card0 ??= GetNodeOrNull<VBoxContainer>("Panel/MainVBox/Cards/Card0");
            NameLabel0 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card0/NameLabel0");
            BuildClassLabel0 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card0/BuildClassLabel0");
            DescLabel0 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card0/DescLabel0");
            ProgressLabel0 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card0/ProgressLabel0");

            Card1 ??= GetNodeOrNull<VBoxContainer>("Panel/MainVBox/Cards/Card1");
            NameLabel1 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card1/NameLabel1");
            BuildClassLabel1 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card1/BuildClassLabel1");
            DescLabel1 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card1/DescLabel1");
            ProgressLabel1 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card1/ProgressLabel1");

            Card2 ??= GetNodeOrNull<VBoxContainer>("Panel/MainVBox/Cards/Card2");
            NameLabel2 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card2/NameLabel2");
            BuildClassLabel2 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card2/BuildClassLabel2");
            DescLabel2 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card2/DescLabel2");
            ProgressLabel2 ??= GetNodeOrNull<Label>("Panel/MainVBox/Cards/Card2/ProgressLabel2");

            _cards = new[] { Card0!, Card1!, Card2! };
            _nameLabels = new[] { NameLabel0!, NameLabel1!, NameLabel2! };
            _buildClassLabels = new[] { BuildClassLabel0!, BuildClassLabel1!, BuildClassLabel2! };
            _descLabels = new[] { DescLabel0!, DescLabel1!, DescLabel2! };
            _progressLabels = new[] { ProgressLabel0!, ProgressLabel1!, ProgressLabel2! };
        }

        public void ShowWindow(List<BuildEffectDefinition> options, Action<BuildEffectDefinition> onConfirmed)
        {
            if (_isOpen) return;

            _options = options;
            _onConfirmed = onConfirmed;
            _selectedIndex = 0;

            PopulateOptions();
            UpdateHighlights();
            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            _isOpen = true;

            PauseManager.Instance.PushPause();
        }

        public void CloseWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;

            GetTree().CreateTimer(0.15f).Timeout += () =>
            {
                if (PauseManager.Instance.IsPaused)
                    PauseManager.Instance.PopPause();
            };

            var parent = GetParent();
            if (parent is CanvasLayer canvasLayer)
                canvasLayer.QueueFree();
            QueueFree();
        }

        public override void _Input(InputEvent @event)
        {
            if (!_isOpen) return;

            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                switch (keyEvent.Keycode)
                {
                    case Key.Key1 when _options.Count > 0:
                        ConfirmSelection(0);
                        return;
                    case Key.Key2 when _options.Count > 1:
                        ConfirmSelection(1);
                        return;
                    case Key.Key3 when _options.Count > 2:
                        ConfirmSelection(2);
                        return;
                }
            }

            if (@event.IsActionPressed("ui_left") || @event.IsActionPressed("move_left"))
            {
                _selectedIndex = (_selectedIndex - 1 + _options.Count) % _options.Count;
                UpdateHighlights();
                return;
            }
            if (@event.IsActionPressed("ui_right") || @event.IsActionPressed("move_right"))
            {
                _selectedIndex = (_selectedIndex + 1) % _options.Count;
                UpdateHighlights();
                return;
            }

            if (@event.IsActionPressed("ui_accept") || @event.IsActionPressed("attack"))
            {
                ConfirmSelection(_selectedIndex);
                return;
            }
        }

        private void PopulateOptions()
        {
            var buildController = ResolveBuildController();

            for (int i = 0; i < 3; i++)
            {
                bool hasOption = i < _options.Count;
                _cards[i].Visible = hasOption;

                if (!hasOption) continue;

                var effect = _options[i];
                _nameLabels[i].Text = effect.DisplayName;
                _descLabels[i].Text = effect.Description;

                string buildClass = effect.BuildClass;
                if (!string.IsNullOrWhiteSpace(buildClass))
                {
                    string className = GetBuildClassName(buildClass);
                    _buildClassLabels[i].Text = $"[{className}]";
                }
                else
                {
                    _buildClassLabels[i].Text = "";
                }

                _progressLabels[i].Text = BuildProgressText(buildController, buildClass, effect.LevelCount);
            }
        }

        private static string BuildProgressText(PlayerBuildController? buildController, string buildClass, int levelCount)
        {
            if (buildController == null || string.IsNullOrWhiteSpace(buildClass))
                return "";

            int currentPoints = 0;
            if (buildController.BuildCountByClass.TryGetValue(buildClass, out int pts))
                currentPoints = pts;

            int afterPts = currentPoints + levelCount;
            var thresholds = new[] { 1, 4, 6 };
            var levelNames = new[] { "Lv1", "Lv2", "Lv3" };

            var parts = new List<string>();
            for (int j = 0; j < thresholds.Length; j++)
            {
                int threshold = thresholds[j];
                bool alreadyReached = currentPoints >= threshold;
                bool willReach = !alreadyReached && afterPts >= threshold;

                char status = alreadyReached ? '●' : (willReach ? '◐' : '○');
                string segment = $"{status} {levelNames[j]}";
                if (willReach)
                    segment += " <-- 选中即解锁";
                else if (alreadyReached)
                    segment += " 已激活";
                else
                    segment += $" ({afterPts}/{threshold})";
                parts.Add(segment);
            }

            return string.Join("  ", parts);
        }

        private static string GetBuildClassName(string buildClass)
        {
            return buildClass switch
            {
                "Guard" => "安保协议",
                "Machine" => "机械协议",
                "Waiter" => "宴会协议",
                _ => buildClass
            };
        }

        private PlayerBuildController? ResolveBuildController()
        {
            var tree = GetTree();
            if (tree == null) return null;
            var player = tree.GetFirstNodeInGroup("player") as SamplePlayer;
            return player?.FindChild("BuildController", recursive: true, owned: false) as PlayerBuildController;
        }

        private void UpdateHighlights()
        {
            for (int i = 0; i < _options.Count; i++)
            {
                bool selected = i == _selectedIndex;
                var panelStyle = new StyleBoxFlat
                {
                    BgColor = selected ? _highlightColor : _normalColor,
                    BorderWidthLeft = selected ? 2 : 0,
                    BorderWidthRight = selected ? 2 : 0,
                    BorderWidthTop = selected ? 2 : 0,
                    BorderWidthBottom = selected ? 2 : 0,
                    BorderColor = selected ? new Color(0.5f, 0.7f, 1f, 0.9f) : Colors.Transparent,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                    ContentMarginLeft = 10, ContentMarginRight = 10,
                    ContentMarginTop = 6, ContentMarginBottom = 6
                };
                _cards[i].AddThemeStyleboxOverride("panel", panelStyle);
                _nameLabels[i].AddThemeColorOverride("font_color", selected ? _selectedTextColor : _normalTextColor);
            }
        }

        private void ConfirmSelection(int index)
        {
            if (index < 0 || index >= _options.Count) return;

            var chosen = _options[index];
            var callback = _onConfirmed;
            CloseWindow();
            callback?.Invoke(chosen);
        }
    }
}
