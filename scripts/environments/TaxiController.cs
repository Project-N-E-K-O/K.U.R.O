using Godot;
using Kuros.Actors.Heroes;
using Kuros.Managers;
using Kuros.Systems.Cutscene;

namespace Kuros.Environments
{
    public partial class TaxiController : Area2D
    {
        [ExportCategory("Scene")]
        [Export(PropertyHint.File, "*.tscn")] public string NextStagePath { get; set; } = "";
        [Export] public CutsceneSequence? Sequence { get; set; }

        [ExportCategory("Paths")]
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath();
        [Export] public NodePath HintLabelPath { get; set; } = new NodePath();
        [Export] public NodePath InteractionAreaPath { get; set; } = new NodePath();

        [ExportCategory("Animations")]
        [Export] public string EnterAnimName { get; set; } = "taxi_enter";
        [Export] public string IdleAnimName  { get; set; } = "taxi_idle";

        [ExportCategory("Hints")]
        [Export] public string CallHintText    { get; set; } = "[{KEY}] 呼叫";   // {KEY} 由当前 interact 绑定键替换
        [Export] public string BoardHintText   { get; set; } = "[{KEY}] 前往";
        [Export] public string LoadingHintText { get; set; } = "加载中...";

        [ExportCategory("Detection")]
        [Export] public string PlayerGroup { get; set; } = "player";

        private enum TaxiState { Idle, Calling, Called, Triggered }

        private TaxiState        _state = TaxiState.Idle;
        private AnimationPlayer? _animPlayer;
        private Label?           _hintLabel;
        private Area2D?          _interactionArea;
        private bool             _playerInRange;
        private bool             _sceneReady;
        private PackedScene?     _loadedScene;

        public static PackedScene? PreloadedScene { get; private set; }

        public static PackedScene? ConsumePreloadedScene()
        {
            var scene = PreloadedScene;
            PreloadedScene = null;
            return scene;
        }

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
            _hintLabel  = GetNodeOrNull<Label>(HintLabelPath);

            if (_animPlayer == null)
                GD.PushWarning($"[TaxiController] AnimationPlayer not found: {AnimationPlayerPath}");

            _interactionArea = InteractionAreaPath.IsEmpty
                ? this
                : GetNodeOrNull<Area2D>(InteractionAreaPath);
            if (_interactionArea != null)
            {
                _interactionArea.AreaEntered += OnAreaEntered;
                _interactionArea.AreaExited  += OnAreaExited;
            }
            else
            {
                GD.PushWarning($"[TaxiController] 交互区域未找到：{InteractionAreaPath}");
            }

            if (_animPlayer != null)
                _animPlayer.AnimationFinished += OnAnimationFinished;

            _hintLabel?.Hide();

            // 改键后实时刷新提示文本
            GameSettingsManager.Instance.InputBindingsChanged += OnInputBindingsChanged;
        }

        public override void _ExitTree()
        {
            if (IsInstanceValid(_interactionArea))
            {
                _interactionArea!.AreaEntered -= OnAreaEntered;
                _interactionArea.AreaExited  -= OnAreaExited;
            }

            if (_animPlayer != null)
                _animPlayer.AnimationFinished -= OnAnimationFinished;

            if (GameSettingsManager.Instance != null)
                GameSettingsManager.Instance.InputBindingsChanged -= OnInputBindingsChanged;
        }

        public override void _Process(double delta)
        {
            switch (_state)
            {
                case TaxiState.Idle:
                    if (_playerInRange && SamplePlayer.IsActionJustPressedGlobal("interact"))
                        StartCalling();
                    break;

                case TaxiState.Calling:
                    PollSceneLoad();
                    break;

                case TaxiState.Called:
                    PollSceneLoad();
                    if (_playerInRange && SamplePlayer.IsActionJustPressedGlobal("interact"))
                        StartTriggering();
                    break;

                case TaxiState.Triggered:
                    PollSceneLoad();
                    break;
            }
        }

        private void StartCalling()
        {
            if (string.IsNullOrEmpty(NextStagePath))
            {
                GD.PushError("[TaxiController] NextStagePath not set!");
                return;
            }

            _state      = TaxiState.Calling;
            _sceneReady = false;
            _loadedScene = null;
            _hintLabel?.Hide();

            GD.Print("[TaxiController] Player called taxi, playing enter animation");

            if (_animPlayer != null && _animPlayer.HasAnimation(EnterAnimName))
            {
                _animPlayer.Stop();
                _animPlayer.Play(EnterAnimName);
            }
            else
            {
                GD.PushWarning($"[TaxiController] Animation '{EnterAnimName}' not found, skipping to Called");
                EnterCalledState();
            }

            var err = ResourceLoader.LoadThreadedRequest(NextStagePath);
            if (err != Error.Ok)
            {
                GD.PushError($"[TaxiController] LoadThreadedRequest failed: {err}");
                _sceneReady = true;
            }
        }

        private void EnterCalledState()
        {
            _state = TaxiState.Called;

            if (_animPlayer != null && _animPlayer.HasAnimation(IdleAnimName))
            {
                _animPlayer.Stop();
                _animPlayer.Play(IdleAnimName);
            }

            if (_playerInRange)
            {
                SetHintText(BoardHintText);
                _hintLabel?.Show();
            }

            GD.Print("[TaxiController] Taxi ready, waiting for player second E press");
        }

        private void StartTriggering()
        {
            _state = TaxiState.Triggered;

            GD.Print("[TaxiController] Player confirmed departure");

            if (_sceneReady)
            {
                _hintLabel?.Hide();
                _ = TriggerCutsceneAsync();
            }
            else
            {
                SetHintText(LoadingHintText);
                // 保持显示，等 PollSceneLoad 完成后再隐藏并触发
            }
        }

        private async System.Threading.Tasks.Task TriggerCutsceneAsync()
        {
            if (Sequence == null)
            {
                GD.PushError("[TaxiController] Sequence not set!");
                EnterCalledState();
                return;
            }

            if (_loadedScene != null)
            {
                PreloadedScene = _loadedScene;
                GD.Print("[TaxiController] Preloaded scene cached for ChangeSceneStep");
            }

            var manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
            if (manager == null)
            {
                GD.PushError("[TaxiController] CutsceneManager not found in group 'cutscene_manager'!");
                EnterCalledState();
                return;
            }
            if (manager.IsPlaying)
            {
                EnterCalledState();
                return;
            }

            GD.Print($"[TaxiController] Playing cutscene: {Sequence.SequenceId}");
            await manager.PlayCutscene(Sequence);
            // 写盘或切场失败时，旧房间仍在，允许再次确认离场。
            if (IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion()) EnterCalledState();
        }

        private void PollSceneLoad()
        {
            if (_sceneReady || string.IsNullOrEmpty(NextStagePath)) return;

            var status = ResourceLoader.LoadThreadedGetStatus(NextStagePath);
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    _loadedScene = ResourceLoader.LoadThreadedGet(NextStagePath) as PackedScene;
                    _sceneReady  = true;
                    GD.Print($"[TaxiController] Scene loaded: {NextStagePath}");
                    if (_state == TaxiState.Triggered)
                    {
                        _hintLabel?.Hide();
                        _ = TriggerCutsceneAsync();
                    }
                    break;

                case ResourceLoader.ThreadLoadStatus.Failed:
                    GD.PushError($"[TaxiController] Scene load failed: {NextStagePath}");
                    _sceneReady = true;
                    if (_state == TaxiState.Triggered)
                    {
                        _hintLabel?.Hide();
                        _ = TriggerCutsceneAsync();
                    }
                    break;
            }
        }

        /// <summary>当前提示模板（未替换 {KEY}），改键后据此重新格式化。</summary>
        private string _currentHintTemplate = "";

        private void SetHintText(string text)
        {
            _currentHintTemplate = text;
            ApplyHintText();
        }

        private void ApplyHintText()
        {
            if (_hintLabel != null)
                _hintLabel.Text = GameSettingsManager.Instance?.FormatActionPrompt(_currentHintTemplate, "interact") ?? _currentHintTemplate;
        }

        private void OnInputBindingsChanged()
        {
            ApplyHintText();
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (_state != TaxiState.Calling) return;
            if (animName != EnterAnimName) return;

            GD.Print($"[TaxiController] {EnterAnimName} finished, entering Called state");
            EnterCalledState();
        }

        private void OnAreaEntered(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            _playerInRange = true;

            switch (_state)
            {
                case TaxiState.Idle:
                    SetHintText(CallHintText);
                    _hintLabel?.Show();
                    break;
                case TaxiState.Called:
                    SetHintText(BoardHintText);
                    _hintLabel?.Show();
                    break;
            }
        }

        private void OnAreaExited(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            _playerInRange = false;

            if (_state == TaxiState.Idle || _state == TaxiState.Called)
                _hintLabel?.Hide();
        }

        private bool IsPlayerArea(Area2D area)
        {
            var player = GetTree().GetFirstNodeInGroup(PlayerGroup) as Node2D;
            if (player == null) return false;
            var hitArea = player.GetNodeOrNull<Area2D>("HitArea");
            if (hitArea != null && area == hitArea) return true;
            return player.IsAncestorOf(area);
        }
    }
}
