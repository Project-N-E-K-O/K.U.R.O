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
        [Export] public NodePath AiDecisionExecutorPath { get; set; } = new("../AiDecisionExecutor");
        [Export] public NodePath OutputLabelPath { get; set; } = new("Panel/VBox/OutputText");
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentNodePath { get; set; } = new("Panel/VBox/OutputText");

        private AiDecisionBridge? _bridge;
        private AiDecisionExecutor? _executor;
        private RichTextLabel? _outputLabel;
        private Button? _toggleButton;
        private Control? _contentNode;
        private bool _contentVisible = true;
        private string _lastPromptText = string.Empty;
        private string _lastResponseText = string.Empty;
        private string _lastDecisionJsonText = string.Empty;
        private string _lastDecisionParseError = string.Empty;
        private string _lastExecutionJsonText = string.Empty;
        private string _lastExecutionErrorText = string.Empty;
        private string _lastErrorText = string.Empty;
        private bool _autopilotEnabled;
        private bool _clearBeforeNextResult;

        public override void _Ready()
        {
            _bridge = GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));
            _executor = GetNodeOrNull<AiDecisionExecutor>(AiDecisionExecutorPath)
                ?? GetNodeOrNull<AiDecisionExecutor>(NormalizeRelativePath(AiDecisionExecutorPath));
            _outputLabel = GetNodeOrNull<RichTextLabel>(OutputLabelPath);
            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _contentNode = GetNodeOrNull<Control>(ContentNodePath);

            if (_bridge != null)
            {
                _bridge.DecisionPromptBuilt += OnDecisionPromptBuilt;
                _bridge.DecisionChunkReceived += OnDecisionChunkReceived;
                _bridge.DecisionCompleted += OnDecisionCompleted;
                _bridge.DecisionStructured += OnDecisionStructured;
                _bridge.DecisionStructureFailed += OnDecisionStructureFailed;
                _bridge.DecisionFailed += OnDecisionFailed;

                _lastPromptText = _bridge.LastPromptText;
                _lastResponseText = _bridge.LastDecisionText;
                _lastDecisionJsonText = _bridge.LastStructuredDecisionJson;
                _lastDecisionParseError = _bridge.LastDecisionParseError;
            }

            if (_executor != null)
            {
                _executor.AutopilotChanged += OnAutopilotChanged;
                _executor.ExecutionCompleted += OnExecutionCompleted;
                _executor.ExecutionRejected += OnExecutionRejected;
                _lastExecutionJsonText = _executor.LastExecutionJson;
                _lastExecutionErrorText = _executor.LastExecutionError;
                _autopilotEnabled = _executor.AutoPilotEnabled;
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
                _bridge.DecisionStructured -= OnDecisionStructured;
                _bridge.DecisionStructureFailed -= OnDecisionStructureFailed;
                _bridge.DecisionFailed -= OnDecisionFailed;
            }

            if (_executor != null)
            {
                _executor.AutopilotChanged -= OnAutopilotChanged;
                _executor.ExecutionCompleted -= OnExecutionCompleted;
                _executor.ExecutionRejected -= OnExecutionRejected;
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
            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = string.Empty;
            _lastExecutionJsonText = string.Empty;
            _lastExecutionErrorText = string.Empty;
            _lastErrorText = string.Empty;
            _clearBeforeNextResult = true;
            RenderText();
        }

        private void OnDecisionChunkReceived(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            ClearWindowIfNeededForNewResult();

            _lastResponseText += chunk;
            RenderText();
        }

        private void OnDecisionCompleted(string text)
        {
            ClearWindowIfNeededForNewResult();

            _lastResponseText = text ?? string.Empty;
            _lastErrorText = string.Empty;
            RenderText();
        }

        private void OnDecisionStructured(string decisionJson)
        {
            ClearWindowIfNeededForNewResult();

            _lastDecisionJsonText = decisionJson ?? string.Empty;
            _lastDecisionParseError = string.Empty;
            RenderText();
        }

        private void OnDecisionStructureFailed(string error)
        {
            ClearWindowIfNeededForNewResult();

            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = error ?? string.Empty;
            RenderText();
        }

        private void OnDecisionFailed(string error)
        {
            ClearWindowIfNeededForNewResult();

            _lastErrorText = error ?? string.Empty;
            RenderText();
        }

        private void OnExecutionCompleted(string executionJson)
        {
            _lastExecutionJsonText = executionJson ?? string.Empty;
            _lastExecutionErrorText = string.Empty;
            RenderText();
        }

        private void OnAutopilotChanged(bool enabled)
        {
            _autopilotEnabled = enabled;
            RenderText();
        }

        private void OnExecutionRejected(string reason)
        {
            _lastExecutionJsonText = string.Empty;
            _lastExecutionErrorText = reason ?? string.Empty;
            RenderText();
        }

        private void ClearWindowIfNeededForNewResult()
        {
            if (!_clearBeforeNextResult)
            {
                return;
            }

            _lastResponseText = string.Empty;
            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = string.Empty;
            _lastErrorText = string.Empty;
            _lastExecutionJsonText = string.Empty;
            _lastExecutionErrorText = string.Empty;

            if (_outputLabel != null)
            {
                _outputLabel.Text = string.Empty;
            }

            _clearBeforeNextResult = false;
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

            string decisionJsonText = string.IsNullOrWhiteSpace(_lastDecisionJsonText)
                ? "(not parsed yet)"
                : _lastDecisionJsonText;

            string decisionParseText = string.IsNullOrWhiteSpace(_lastDecisionParseError)
                ? "(none)"
                : _lastDecisionParseError;

            string executionJsonText = string.IsNullOrWhiteSpace(_lastExecutionJsonText)
                ? "(none)"
                : _lastExecutionJsonText;

            string executionErrorText = string.IsNullOrWhiteSpace(_lastExecutionErrorText)
                ? "(none)"
                : _lastExecutionErrorText;

            string autopilotText = _autopilotEnabled ? "ON" : "OFF";

            _outputLabel.Text = string.Join("\n", new[]
            {
                $"[Autopilot] {autopilotText}",
                string.Empty,
                "[AI Prompt]",
                promptText,
                string.Empty,
                "[AI Response]",
                responseText,
                string.Empty,
                "[Structured Decision]",
                decisionJsonText,
                string.Empty,
                "[Decision Parse Error]",
                decisionParseText,
                string.Empty,
                "[Execution Result]",
                executionJsonText,
                string.Empty,
                "[Execution Error]",
                executionErrorText,
                string.Empty,
                "[AI Error]",
                errorText,
                string.Empty,
                "Tip: Press | to request AI.",
                "Tip: Press F6 to toggle AI autopilot."
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
