using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items.World;
using Kuros.Managers;
using Kuros.Systems.Stage;
using Kuros.Utils;

namespace Kuros.Environments
{
    /// <summary>
    /// 电梯加载控制器 — 将 Stage_loading 场景中的电梯作为可交互过渡界面。
    ///
    /// 状态流程：
    ///   Idle    → 玩家进入交互区域，显示"[{KEY}] 进入下一楼层"提示（{KEY}=当前 interact 绑定键）
    ///   Loading → 玩家按交互键后：播放 loading 动画 + 后台异步加载目标场景
    ///             满足【动画已播放 MinRideDuration 秒 AND 场景已加载】才进入 Arrived
    ///   Arrived → 停止动画，显示"[{KEY}] 离开电梯"提示
    ///   （玩家进入指定区域后）→ ChangeSceneToPacked 切换到目标场景
    ///
    /// 使用方式：
    ///   切换到 Stage_loading 前，写入：SaveManager.Instance.PendingNextStagePath = "res://scenes/Stage_hotel.tscn";
    /// </summary>
    public partial class ElevatorController : Node
    {
        [ExportCategory("Timing")]
        /// <summary>最短骑乘时间（秒）。即使场景加载完毕，动画也至少播放此时长，避免瞬间停止。</summary>
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float MinRideDuration { get; set; } = 3.0f;

        [ExportCategory("Paths")]
        /// <summary>
        /// 目标场景路径。可直接在 Inspector 设置（嵌入关卡时使用）。
        /// 若为空则回退到 SaveManager.PendingNextStagePath（中间加载场景模式）。
        /// </summary>
        [Export(PropertyHint.File, "*.tscn,*.scn")] public string NextStagePath { get; set; } = "";
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath("AnimationPlayer");
        [Export] public NodePath InteractAreaPath   { get; set; } = new NodePath("InteractArea");
        [Export] public NodePath HintLabelPath      { get; set; } = new NodePath("HintLabel");
        [Export] public NodePath ExitAreaPath       { get; set; } = new NodePath("ExitArea");

        // ── 内部状态 ──────────────────────────────────────────────
        private enum ElevatorState { Idle, Selecting, Closing, Loading, Arrived }

        private ElevatorState _state = ElevatorState.Idle;
        private AnimationPlayer? _animPlayer;
        private Area2D?          _interactArea;
        private Area2D?          _exitArea;
        private Label?           _hintLabel;

        private bool        _playerInRange;
        private bool        _sceneReady;      // ResourceLoader 已完成
        private PackedScene? _loadedScene;
        private string      _nextStagePath = "";
        private double      _rideTimer;

        /// <summary>会话模式：场景内找到 StageSession 时，出舱 = 应用选中的关卡配置（world 内换关），不切场景。</summary>
        private StageSession? _session;

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _animPlayer   = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
            _interactArea = GetNodeOrNull<Area2D>(InteractAreaPath);
            _hintLabel    = GetNodeOrNull<Label>(HintLabelPath);
            _exitArea     = GetNodeOrNull<Area2D>(ExitAreaPath);

            if (_animPlayer == null)
                GD.PushWarning("[ElevatorController] 未找到 AnimationPlayer，路径：" + AnimationPlayerPath);
            if (_interactArea == null)
                GD.PushWarning("[ElevatorController] 未找到 InteractArea，路径：" + InteractAreaPath);
            if (_exitArea == null)
                GD.PushWarning("[ElevatorController] 未找到 ExitArea，路径：" + ExitAreaPath);

            if (_interactArea != null)
            {
                _interactArea.BodyEntered += OnBodyEntered;
                _interactArea.BodyExited  += OnBodyExited;
            }
            if (_exitArea != null)
                _exitArea.BodyEntered += OnExitAreaBodyEntered;
            if (_animPlayer != null)
                _animPlayer.AnimationFinished += OnAnimationFinished;

            // 确定目标场景路径：优先使用 Inspector 里设置的，否则读 SaveManager
            if (string.IsNullOrEmpty(_nextStagePath))
                _nextStagePath = NextStagePath;
            if (string.IsNullOrEmpty(_nextStagePath))
                _nextStagePath = SaveManager.Instance?.PendingNextStagePath ?? "";
            if (string.IsNullOrEmpty(_nextStagePath) && GetTree().GetFirstNodeInGroup("stage_session") == null)
                GD.PushWarning("[ElevatorController] PendingNextStagePath 为空，请在切换到 Stage_loading 前设置目标路径或在 Inspector 中设置 NextStagePath。");

            // 会话模式：壳场景内有 StageSession（group 注册）→ 出舱改关由会话决定
            _session = GetTree().GetFirstNodeInGroup("stage_session") as StageSession;

            UpdateHintLabel();

            // 改键后实时刷新提示文本
            GameSettingsManager.Instance.InputBindingsChanged += OnInputBindingsChanged;
        }

        public override void _ExitTree()
        {
            if (_interactArea != null)
            {
                _interactArea.BodyEntered -= OnBodyEntered;
                _interactArea.BodyExited  -= OnBodyExited;
            }
            if (_exitArea != null)
                _exitArea.BodyEntered -= OnExitAreaBodyEntered;
            if (_animPlayer != null)
                _animPlayer.AnimationFinished -= OnAnimationFinished;

            if (GameSettingsManager.Instance != null)
                GameSettingsManager.Instance.InputBindingsChanged -= OnInputBindingsChanged;
        }

        private Kuros.UI.ElevatorSelectionPanel? _selectionPanel;

        private void ConfirmSelection(int index)
        {
            if (_state == ElevatorState.Selecting && _session?.SelectStage(index) == true)
                StartClosing();
        }

        private void CancelSelection()
        {
            if (_state != ElevatorState.Selecting) return;
            _state = ElevatorState.Idle;
            UpdateHintLabel();
        }

        // ── 每帧逻辑 ──────────────────────────────────────────────

        public override void _Process(double delta)
        {
            switch (_state)
            {
                case ElevatorState.Idle:
                    if (_playerInRange && SamplePlayer.IsActionJustPressedGlobal("interact"))
                    {
                        // 会话模式：先选关（数字键），选完才关门骑行；无会话则维持原流程
                        if (_session == null)
                            StartClosing();
                        else if (_session.PrepareElevatorOptions() > 0)
                            EnterSelecting();
                        else if (_hintLabel != null)
                            _hintLabel.Text = _session.DescribeOptions();
                    }
                    break;

                case ElevatorState.Selecting:
                    break;

                case ElevatorState.Closing:
                    // 等待 close 动画完成（由 OnAnimationFinished 驱动）
                    break;

                case ElevatorState.Loading:
                    _rideTimer += delta;
                    if (_session == null) PollSceneLoad();

                    if (_session != null && (_session.PendingFailed || (_rideTimer > 45 && !_session.PendingReady)))
                    {
                        if (_animPlayer?.HasAnimation("open") == true) _animPlayer.Play("open");
                        _state = ElevatorState.Idle;
                        UpdateHintLabel();
                        if (_hintLabel != null) _hintLabel.Text = "楼层加载失败，请重新选择";
                        break;
                    }
                    bool ready = _session?.PendingReady ?? _sceneReady;
                    if (ready && _rideTimer >= MinRideDuration)
                        EnterArrived();
                    break;

                case ElevatorState.Arrived:
                    // ExitArea 进入后由 OnExitAreaBodyEntered 处理跳转
                    break;
            }
        }

        // ── 状态转换 ──────────────────────────────────────────────

        /// <summary>进入选关状态：显示可用路线，确认后关门骑行。</summary>
        private void EnterSelecting()
        {
            if (_session == null) return;
            if (_selectionPanel == null)
            {
                _selectionPanel = GD.Load<PackedScene>("res://scenes/ui/ElevatorSelectionPanel.tscn")
                    .Instantiate<Kuros.UI.ElevatorSelectionPanel>();
                _selectionPanel.Confirmed += ConfirmSelection;
                _selectionPanel.Cancelled += CancelSelection;
                AddChild(_selectionPanel);
            }
            _state = ElevatorState.Selecting;
            _selectionPanel.Open(_session.DisplayedPlans);
            UpdateHintLabel();
        }

        /// <summary>玩家按交互键（interact）后先播放 close 动画，动画结束后再进入 Loading。</summary>
        private void StartClosing()
        {
            if (_session == null && string.IsNullOrEmpty(_nextStagePath))
            {
                GD.PushError("[ElevatorController] 无法开始加载：目标场景路径未设置。");
                return;
            }

            _state       = ElevatorState.Closing;
            _sceneReady  = false;
            _loadedScene = null;

            if (_animPlayer != null && _animPlayer.HasAnimation("close"))
            {
                _animPlayer.Stop();
                _animPlayer.Play("close");
            }
            else
            {
                BeginLoading(); // 没有 close 动画则直接开始加载
            }

            UpdateHintLabel();
        }

        /// <summary>close 动画结束后调用：启动异步加载 + 播放 loading 动画。</summary>
        private void BeginLoading()
        {
            // 每次电梯 loading 时自动保存元数据（进度/时间）
            SaveManager.Instance?.AutosaveCurrentSlot();

            _state     = ElevatorState.Loading;
            _rideTimer = 0;

            // 会话模式不切场景：骑行仅为动画，换关在出舱时由 StageSession 执行
            if (_session == null)
            {
                var err = ResourceLoader.LoadThreadedRequest(_nextStagePath);
                if (err != Error.Ok)
                {
                    GD.PushError($"[ElevatorController] ResourceLoader.LoadThreadedRequest 失败: {err}，路径: {_nextStagePath}");
                    _sceneReady = true;
                }
            }

            if (_animPlayer != null && _animPlayer.HasAnimation("loading"))
            {
                _animPlayer.Stop();
                _animPlayer.Play("loading");
            }

            UpdateHintLabel();
        }

        private void EnterArrived()
        {
            _state = ElevatorState.Arrived;

            // 播放 open 动画（门开，玩家走出）
            if (_animPlayer != null && _animPlayer.HasAnimation("open"))
            {
                _animPlayer.Stop();
                _animPlayer.Play("open");
            }

            UpdateHintLabel();
        }

        private void LeaveElevator()
        {
            // 会话模式：场景壳常驻，出舱 = 应用选中的关卡配置（world 内换关，不切场景）
            if (_session != null)
            {
                _session.CommitPending();
                return;
            }

            // 离开前捕获背包+HP快照，传递到目标场景
            if (SaveManager.Instance != null)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
                if (player != null)
                    SaveManager.Instance.CaptureInventoryTransit(player);
            }

            // 清除 SaveManager 中的路径，避免下次误用
            if (SaveManager.Instance != null)
                SaveManager.Instance.PendingNextStagePath = "";

            var tree = GetTree();
            if (tree == null) return;

            if (PauseManager.Instance != null)
                PauseManager.Instance.ClearAllPauses();

            // 清理跨场景缓存，防止 PackedScene 在新场景中继续占用内存
            WorldItemSpawner.ClearCache();
            DialogicUtils.CleanupPersistentState(this);

            // 持有 _loadedScene 引用会阻止 Godot resource cache 释放纹理；
            // 先取出再置 null，使旧场景卸载后 GC 可以回收该 PackedScene。
            var sceneToLoad = _loadedScene;
            _loadedScene = null;

            if (sceneToLoad != null)
            {
                tree.ChangeSceneToPacked(sceneToLoad);
            }
            else if (!string.IsNullOrEmpty(_nextStagePath))
            {
                GD.PushWarning("[ElevatorController] 异步场景未缓存，降级为同步切换。");
                tree.ChangeSceneToFile(_nextStagePath);
            }
        }

        // ── 工具方法 ──────────────────────────────────────────────

        private void PollSceneLoad()
        {
            if (_sceneReady || string.IsNullOrEmpty(_nextStagePath)) return;

            var status = ResourceLoader.LoadThreadedGetStatus(_nextStagePath);
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    _loadedScene = ResourceLoader.LoadThreadedGet(_nextStagePath) as PackedScene;
                    _sceneReady  = true;
                    break;
                case ResourceLoader.ThreadLoadStatus.Failed:
                    GD.PushError($"[ElevatorController] 场景加载失败: {_nextStagePath}");
                    _sceneReady = true; // 允许流程继续，LeaveElevator 会降级处理
                    break;
            }
        }

        private void OnInputBindingsChanged()
        {
            UpdateHintLabel();
        }

        private void UpdateHintLabel()
        {
            if (_state != ElevatorState.Selecting) _selectionPanel?.Close();
            if (_hintLabel == null) return;
            switch (_state)
            {
                case ElevatorState.Idle:
                    _hintLabel.Text    = GameSettingsManager.Instance?.FormatActionPrompt("[{KEY}] 进入电梯", "interact") ?? "[{KEY}] 进入电梯";
                    _hintLabel.Visible = _playerInRange;
                    break;
                case ElevatorState.Selecting:
                    _hintLabel.Hide();
                    break;
                case ElevatorState.Closing:
                    _hintLabel.Visible = false;
                    break;
                case ElevatorState.Loading:
                    _hintLabel.Text    = "电梯上升中...";
                    _hintLabel.Visible = true;
                    break;
                case ElevatorState.Arrived:
                    _hintLabel.Visible = false;
                    break;
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _playerInRange = true;
            UpdateHintLabel();
        }

        private void OnBodyExited(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _playerInRange = false;
            // 未选关就离开交互区 → 放弃选关会话
            if (_state == ElevatorState.Selecting)
                _state = ElevatorState.Idle;
            UpdateHintLabel();
        }

        private void OnExitAreaBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            if (_state != ElevatorState.Arrived) return;
            LeaveElevator();
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (animName == "close" && _state == ElevatorState.Closing)
                BeginLoading();
        }
    }
}
