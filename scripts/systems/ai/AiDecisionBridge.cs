using System;
using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// High-level bridge that captures game state and requests a local Ollama model for decisions.
    /// </summary>
    [GlobalClass]
    public partial class AiDecisionBridge : Node
    {
        [Signal] public delegate void DecisionChunkReceivedEventHandler(string chunkText);
        [Signal] public delegate void DecisionPromptBuiltEventHandler(string promptText);
        [Signal] public delegate void DecisionCompletedEventHandler(string responseText);
        [Signal] public delegate void DecisionStructuredEventHandler(string decisionJson);
        [Signal] public delegate void DecisionStructureFailedEventHandler(string errorMessage);
        [Signal] public delegate void DecisionFailedEventHandler(string errorMessage);

        [Export] public NodePath GameStateProviderPath { get; set; } = new("../GameStateProvider");
        [Export] public NodePath OllamaClientPath { get; set; } = new("../OllamaClient");
        [Export(PropertyHint.MultilineText)] public string DefaultInstruction { get; set; } = "You are deciding for a fast-paced action game. Prefer proactive combat decisions. When enemies are present, usually choose attack, use_skill, or switch_weapon. Only choose retreat when the player is in genuine lethal danger.";
        [Export] public string Model { get; set; } = string.Empty;
        [Export] public bool Stream { get; set; } = true;
        [Export(PropertyHint.Range, "0,60,0.1")] public float MinRequestIntervalSeconds { get; set; } = 0.5f;

        public bool RequestInFlight => _requestInFlight;
        public string LastPromptText { get; private set; } = string.Empty;
        public string LastDecisionText { get; private set; } = string.Empty;
        public AiDecision LastStructuredDecision { get; private set; } = AiDecision.FromError(string.Empty, "No decision parsed yet.");
        public string LastStructuredDecisionJson => LastStructuredDecision.ToJson(pretty: true);
        public string LastDecisionParseError { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        private GameStateProvider? _gameStateProvider;
        private OllamaGenerateClient? _ollamaClient;
        private bool _requestInFlight;
        private ulong _lastRequestAtMs;

        public override void _Ready()
        {
            ResolveDependencies();
            SubscribeClientSignals();
        }

        public override void _ExitTree()
        {
            UnsubscribeClientSignals();
            base._ExitTree();
        }

        public async Task<OllamaGenerateResult> RequestDecisionAsync(string? instruction = null)
        {
            ResolveDependencies();
            if (_gameStateProvider == null)
            {
                return Fail("GameStateProvider not found.");
            }

            if (_ollamaClient == null)
            {
                return Fail("OllamaGenerateClient not found.");
            }

            if (_requestInFlight)
            {
                return Fail("AI request already in flight.");
            }

            ulong now = Time.GetTicksMsec();
            ulong minIntervalMs = (ulong)Mathf.RoundToInt(Mathf.Max(0f, MinRequestIntervalSeconds) * 1000f);
            if (_lastRequestAtMs != 0 && now - _lastRequestAtMs < minIntervalMs)
            {
                return Fail("AI request throttled by MinRequestIntervalSeconds.");
            }

            _requestInFlight = true;
            _lastRequestAtMs = now;
            LastPromptText = string.Empty;
            LastDecisionText = string.Empty;
            LastStructuredDecision = AiDecision.FromError(string.Empty, "No decision parsed yet.");
            LastDecisionParseError = string.Empty;
            LastError = string.Empty;

            try
            {
                string effectiveInstruction = string.IsNullOrWhiteSpace(instruction) ? DefaultInstruction : instruction!;
                var state = _gameStateProvider.CaptureGameState();
                LastPromptText = OllamaGenerateClient.BuildGameStatePrompt(state, effectiveInstruction);
                EmitSignal(SignalName.DecisionPromptBuilt, LastPromptText);

                var result = await _ollamaClient.GenerateAsync(
                    LastPromptText,
                    string.IsNullOrWhiteSpace(Model) ? null : Model,
                    Stream);

                if (result.Success)
                {
                    LastDecisionText = result.ResponseText;
                    LastStructuredDecision = AiDecision.Parse(result.ResponseText);
                    LastDecisionParseError = LastStructuredDecision.IsValid ? string.Empty : LastStructuredDecision.ParseError;
                    EmitSignal(SignalName.DecisionCompleted, result.ResponseText);

                    if (LastStructuredDecision.IsValid)
                    {
                        EmitSignal(SignalName.DecisionStructured, LastStructuredDecisionJson);
                    }
                    else
                    {
                        EmitSignal(SignalName.DecisionStructureFailed, LastDecisionParseError);
                    }
                }
                else
                {
                    LastError = result.ErrorMessage;
                    EmitSignal(SignalName.DecisionFailed, result.ErrorMessage);
                }

                return result;
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        private void ResolveDependencies()
        {
            _gameStateProvider ??= GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath));

            var nextClient = _ollamaClient
                ?? GetNodeOrNull<OllamaGenerateClient>(OllamaClientPath)
                ?? GetNodeOrNull<OllamaGenerateClient>(NormalizeRelativePath(OllamaClientPath));

            if (!ReferenceEquals(nextClient, _ollamaClient))
            {
                UnsubscribeClientSignals();
                _ollamaClient = nextClient;
                SubscribeClientSignals();
            }
        }

        private void SubscribeClientSignals()
        {
            if (_ollamaClient == null)
            {
                return;
            }

            var chunkCallable = new Callable(this, MethodName.OnClientChunkReceived);
            if (!_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.StreamChunkReceived, chunkCallable))
            {
                _ollamaClient.StreamChunkReceived += OnClientChunkReceived;
            }

            var failCallable = new Callable(this, MethodName.OnClientRequestFailed);
            if (!_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.RequestFailed, failCallable))
            {
                _ollamaClient.RequestFailed += OnClientRequestFailed;
            }
        }

        private void UnsubscribeClientSignals()
        {
            if (_ollamaClient == null)
            {
                return;
            }

            var chunkCallable = new Callable(this, MethodName.OnClientChunkReceived);
            if (_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.StreamChunkReceived, chunkCallable))
            {
                _ollamaClient.StreamChunkReceived -= OnClientChunkReceived;
            }

            var failCallable = new Callable(this, MethodName.OnClientRequestFailed);
            if (_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.RequestFailed, failCallable))
            {
                _ollamaClient.RequestFailed -= OnClientRequestFailed;
            }
        }

        private void OnClientChunkReceived(string chunkText)
        {
            EmitSignal(SignalName.DecisionChunkReceived, chunkText);
        }

        private void OnClientRequestFailed(string errorMessage)
        {
            LastError = errorMessage;
        }

        private OllamaGenerateResult Fail(string error)
        {
            LastError = error;
            EmitSignal(SignalName.DecisionFailed, error);
            return OllamaGenerateResult.FromError(error);
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            if (path.IsEmpty)
            {
                return path;
            }

            string text = path.ToString();
            return text.StartsWith("../", StringComparison.Ordinal) ? new NodePath(text[3..]) : path;
        }
    }
}
