using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// On-screen panel for displaying AI request output from AiDecisionBridge.
    /// </summary>
    [GlobalClass]
    public partial class AiOutputDebugPanel : CanvasLayer
    {
        [Export] public NodePath AiDecisionBridgePath { get; set; } = new("../AiDecisionBridge");
        [Export] public NodePath OutputLabelPath { get; set; } = new("Panel/VBox/OutputText");
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentNodePath { get; set; } = new("Panel/VBox/OutputText");

        private AiDecisionBridge? _bridge;
        private RichTextLabel? _outputLabel;
        private Button? _toggleButton;
        private Control? _contentNode;
        private bool _contentVisible = true;
        private string _lastPromptText = string.Empty;
        private string _lastResponseText = string.Empty;
        private string _lastErrorText = string.Empty;

        public override void _Ready()
        {
            _bridge = GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));
            _outputLabel = GetNodeOrNull<RichTextLabel>(OutputLabelPath);
            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _contentNode = GetNodeOrNull<Control>(ContentNodePath);

            if (_bridge != null)
            {
                _bridge.DecisionPromptBuilt += OnDecisionPromptBuilt;
                _bridge.DecisionChunkReceived += OnDecisionChunkReceived;
                _bridge.DecisionCompleted += OnDecisionCompleted;
                _bridge.DecisionFailed += OnDecisionFailed;

                _lastPromptText = _bridge.LastPromptText;
                _lastResponseText = _bridge.LastDecisionText;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed += OnTogglePressed;
                UpdateToggleButtonText();
            }

            if (_outputLabel != null && string.IsNullOrWhiteSpace(_outputLabel.Text))
            {
                RenderText();
            }
        }

        public override void _ExitTree()
        {
            if (_bridge != null)
            {
                _bridge.DecisionPromptBuilt -= OnDecisionPromptBuilt;
                _bridge.DecisionChunkReceived -= OnDecisionChunkReceived;
                _bridge.DecisionCompleted -= OnDecisionCompleted;
                _bridge.DecisionFailed -= OnDecisionFailed;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed -= OnTogglePressed;
            }

            base._ExitTree();
        }

        private void OnDecisionPromptBuilt(string promptText)
        {
            _lastPromptText = promptText ?? string.Empty;
            _lastResponseText = string.Empty;
            _lastErrorText = string.Empty;
            RenderText();
        }

        private void OnDecisionChunkReceived(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _lastResponseText += chunk;
            RenderText();
        }

        private void OnDecisionCompleted(string text)
        {
            _lastResponseText = text ?? string.Empty;
            _lastErrorText = string.Empty;
            RenderText();
        }

        private void OnDecisionFailed(string error)
        {
            _lastErrorText = error ?? string.Empty;
            RenderText();
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

        private void RenderText()
        {
            if (_outputLabel == null)
            {
                return;
            }

            string promptText = string.IsNullOrWhiteSpace(_lastPromptText)
                ? "(none)"
                : _lastPromptText;

            string responseText = string.IsNullOrWhiteSpace(_lastResponseText)
                ? "(waiting or empty)"
                : _lastResponseText;

            string errorText = string.IsNullOrWhiteSpace(_lastErrorText)
                ? "(none)"
                : _lastErrorText;

            _outputLabel.Text = string.Join("\n", new[]
            {
                "[AI Prompt]",
                promptText,
                string.Empty,
                "[AI Response]",
                responseText,
                string.Empty,
                "[AI Error]",
                errorText,
                string.Empty,
                "Tip: Press | to request AI."
            });
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
