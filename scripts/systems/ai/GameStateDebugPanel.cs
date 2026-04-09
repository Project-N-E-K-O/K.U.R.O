using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// On-screen debug panel for previewing current GameState snapshot.
    /// </summary>
    [GlobalClass]
    public partial class GameStateDebugPanel : CanvasLayer
    {
        [Export] public NodePath GameStateProviderPath { get; set; } = new("../GameStateProvider");
        [Export] public NodePath RefreshButtonPath { get; set; } = new("Panel/VBox/RefreshButton");
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentNodePath { get; set; } = new("Panel/VBox/StateText");
        [Export] public NodePath OutputLabelPath { get; set; } = new("Panel/VBox/StateText");
        [Export] public bool RefreshOnReady { get; set; } = true;
        [Export] public bool AutoRefresh { get; set; } = true;
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float RefreshIntervalSeconds { get; set; } = 1f;

        private GameStateProvider? _provider;
        private Button? _refreshButton;
        private Button? _toggleButton;
        private Control? _contentNode;
        private RichTextLabel? _outputLabel;
        private bool _contentVisible = true;
        private float _refreshTimer;

        public override void _Ready()
        {
            _provider = GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath));

            _refreshButton = GetNodeOrNull<Button>(RefreshButtonPath);
            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _contentNode = GetNodeOrNull<Control>(ContentNodePath);
            _outputLabel = GetNodeOrNull<RichTextLabel>(OutputLabelPath);

            if (_refreshButton != null)
            {
                _refreshButton.Pressed += OnRefreshPressed;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed += OnTogglePressed;
                UpdateToggleButtonText();
            }

            if (RefreshOnReady)
            {
                RefreshStateView();
            }

            _refreshTimer = 0f;
            SetProcess(AutoRefresh);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (!AutoRefresh)
            {
                return;
            }

            _refreshTimer -= (float)delta;
            if (_refreshTimer > 0f)
            {
                return;
            }

            _refreshTimer = Mathf.Max(0.1f, RefreshIntervalSeconds);
            RefreshStateView();
        }

        public override void _ExitTree()
        {
            if (_refreshButton != null)
            {
                _refreshButton.Pressed -= OnRefreshPressed;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed -= OnTogglePressed;
            }

            base._ExitTree();
        }

        private void OnRefreshPressed()
        {
            RefreshStateView();
        }

        private void OnTogglePressed()
        {
            _contentVisible = !_contentVisible;
            if (_contentNode != null)
            {
                _contentNode.Visible = _contentVisible;
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

        private void RefreshStateView()
        {
            if (_outputLabel == null)
            {
                return;
            }

            _provider ??= GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath));

            if (_provider == null)
            {
                _outputLabel.Text = "GameStateProvider not found.";
                return;
            }

            string json = _provider.GetAiInputJson(pretty: true);
            _outputLabel.Text = json;
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            if (path.IsEmpty)
            {
                return path;
            }

            string text = path.ToString();
            return text.StartsWith("../", System.StringComparison.Ordinal)
                ? new NodePath(text[3..])
                : path;
        }
    }
}
