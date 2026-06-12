using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items.World;
using Kuros.Managers;
using Kuros.Utils;

namespace Kuros.Environments
{
    /// <summary>
    /// 电梯加载控制器 — 将 Stage_loading 场景中的电梯作为可交互过渡界面。
    ///
    /// 状态流程：
    ///   Idle    → 玩家进入交互区域，显示"[E] 进入下一楼层"提示
    ///   Loading → 玩家按 E 后：播放 loading 动画 + 后台异步加载目标场景
    ///             满足【动画已播放 MinRideDuration 秒 AND 场景已加载】才进入 Arrived
    ///   Arrived → 停止动画，显示"[E] 离开电梯"提示
    ///   （玩家进入指定区域后）→ ChangeSceneToPacked 切换到目标场景
    ///
    /// 使用方式：
    ///   切换到 Stage_loading 前，写入：SaveManager.Instance.PendingNextStagePath = "res://scenes/Stage_3.tscn";
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
        private enum ElevatorState { Idle, Closing, Loading, Arrived }

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
            if (string.IsNullOrEmpty(_nextStagePath))
                GD.PushWarning("[ElevatorController] PendingNextStagePath 为空，请在切换到 Stage_loading 前设置目标路径或在 Inspector 中设置 NextStagePath。");

            UpdateHintLabel();
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
        }

        // ── 每帧逻辑 ──────────────────────────────────────────────

        public override void _Process(double delta)
        {
            switch (_state)
            {
                case ElevatorState.Idle:
                    if (_playerInRange && Input.IsActionJustPressed("interact"))
                        StartClosing();
                    break;

                case ElevatorState.Closing:
                    // 等待 close 动画完成（由 OnAnimationFinished 驱动）
                    break;

                case ElevatorState.Loading:
                    _rideTimer += delta;
                    PollSceneLoad();

                    // 两个条件都满足才停止
                    if (_sceneReady && _rideTimer >= MinRideDuration)
                        EnterArrived();
                    break;

                case ElevatorState.Arrived:
                    // ExitArea 进入后由 OnExitAreaBodyEntered 处理跳转
                    break;
            }
        }

        // ── 状态转换 ──────────────────────────────────────────────

        /// <summary>玩家按 [E] 后先播放 close 动画，动画结束后再进入 Loading。</summary>
        private void StartClosing()
        {
            if (string.IsNullOrEmpty(_nextStagePath))
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
            _state     = ElevatorState.Loading;
            _rideTimer = 0;

            var err = ResourceLoader.LoadThreadedRequest(_nextStagePath);
            if (err != Error.Ok)
            {
                GD.PushError($"[ElevatorController] ResourceLoader.LoadThreadedRequest 失败: {err}，路径: {_nextStagePath}");
                _sceneReady = true;
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

        private void UpdateHintLabel()
        {
            if (_hintLabel == null) return;
            switch (_state)
            {
                case ElevatorState.Idle:
                    _hintLabel.Text    = "[E] 进入下一楼层";
                    _hintLabel.Visible = _playerInRange;
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
