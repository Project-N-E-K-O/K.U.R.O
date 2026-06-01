using System.Threading.Tasks;
using Godot;
using Kuros.Managers;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场动画统一管理器，挂到 Stage_2（BattleScene 根节点下）。
    /// 负责：统一跳过逻辑 / 禁用玩家输入 / 镜头接管 / 黑幕淡变。
    ///
    /// Inspector 必填项：
    /// PlayerPath       → 相对路径指向玩家节点（如 "World/MainCharacter"）
    /// CameraPath       → 相对路径指向 Camera2D（如 "World/MainCharacter/Camera2D"）
    /// DialoguePanelPath→ 可选，指向 CutsceneDialoguePanel 节点
    /// FadeOverlayPath  → 可选，指向全屏黑幕 CanvasItem
    /// TopBlackBarPath → 可选，指向电影式黑幕上方 ColorRect
    /// BottomBlackBarPath → 可选，指向电影式黑幕下方 ColorRect
    /// HideNodePaths    → 可选，过场期间隐藏并禁用 ProcessMode 的节点路径列表（如 P2、UI 根节点）
    /// BattleSceneManagerPath → 可选，过场开始时自动调用 HideAllUI()，结束时调用 ShowAllUI()，指向 BattleSceneManager 节点
    /// </summary>
    [GlobalClass]
    public partial class CutsceneManager : Node
    {
        // ── 信号 ──────────────────────────────────────────────────────────
        [Signal] public delegate void CutsceneStartedEventHandler(string sequenceId);
        [Signal] public delegate void CutsceneFinishedEventHandler(string sequenceId);

        // ── 导出属性 ──────────────────────────────────────────────────────
        /// <summary>跳过按键动作名称（Project Settings → Input Map 中定义）。</summary>
        [Export] public string SkipActionName { get; set; } = "ui_cancel";

        [Export] public NodePath PlayerPath        { get; set; } = new NodePath();
        [Export] public NodePath CameraPath        { get; set; } = new NodePath();
        [Export] public NodePath DialoguePanelPath { get; set; } = new NodePath();
        [Export] public NodePath FadeOverlayPath   { get; set; } = new NodePath();

        /// <summary>
        /// 电影式黑幕：上方黑色 ColorRect 的节点路径（用于 FadeStep 电影模式）
        /// </summary>
        [Export] public NodePath TopBlackBarPath { get; set; } = new NodePath();

        /// <summary>
        /// 电影式黑幕：下方黑色 ColorRect 的节点路径（用于 FadeStep 电影模式）
        /// </summary>
        [Export] public NodePath BottomBlackBarPath { get; set; } = new NodePath();

        /// <summary>
        /// 过场期间隐藏并禁用 ProcessMode 的节点（如 P2、UI 根节点）。
        /// 结束后自动恢复显示和 ProcessMode。
        /// </summary>
        [Export] public Godot.Collections.Array<NodePath> HideNodePaths { get; set; } = new();

        /// <summary>
        /// 指向 BattleSceneManager 节点的路径。
        /// 设置后，过场开始时自动调用 HideAllUI()，结束时调用 ShowAllUI()。
        /// </summary>
        [Export] public NodePath BattleSceneManagerPath { get; set; } = new NodePath();

        // ── 公开状态 ──────────────────────────────────────────────────────
        public bool IsPlaying { get; private set; } = false;

        // ── 内部访问（供 Step 使用）──────────────────────────────────────
        internal bool                  IsSkipRequested { get; private set; } = false;
        internal Node2D?               Player          { get; private set; }
        internal Camera2D?             Camera          { get; private set; }
        internal CutsceneDialoguePanel? DialoguePanel  { get; private set; }
        internal CanvasItem?           FadeOverlay     { get; private set; }
        internal Control?              TopBlackBar     { get; private set; }
        internal Control?              BottomBlackBar  { get; private set; }

        // ── 私有字段 ──────────────────────────────────────────────────────
        private bool    _cameraWasTopLevel = false;
        private bool    _playerWasVisible  = false;
        private readonly System.Collections.Generic.List<CanvasItem> _hiddenNodes = new();
        private Kuros.Scenes.BattleSceneManager? _battleSceneManager;
        private CameraZoneManager? _cameraZoneManager;
        // ── 生命周期 ──────────────────────────────────────────────────────
        public override void _Ready()
        {
            AddToGroup("cutscene_manager");

            if (!PlayerPath.IsEmpty)
                Player = GetNodeOrNull<Node2D>(PlayerPath);

            if (!CameraPath.IsEmpty)
                Camera = GetNodeOrNull<Camera2D>(CameraPath);

            if (!DialoguePanelPath.IsEmpty)
                DialoguePanel = GetNodeOrNull<CutsceneDialoguePanel>(DialoguePanelPath);

            if (!FadeOverlayPath.IsEmpty)
                FadeOverlay = GetNodeOrNull<CanvasItem>(FadeOverlayPath);

            if (!TopBlackBarPath.IsEmpty)
                TopBlackBar = GetNodeOrNull<Control>(TopBlackBarPath);

            if (!BottomBlackBarPath.IsEmpty)
                BottomBlackBar = GetNodeOrNull<Control>(BottomBlackBarPath);

            if (!BattleSceneManagerPath.IsEmpty)
                _battleSceneManager = GetNodeOrNull<Kuros.Scenes.BattleSceneManager>(BattleSceneManagerPath);

            // 若 NodePath 查找失败（节点顺序问题），延迟一帧再试
            if (Player == null || Camera == null)
                CallDeferred(MethodName.LateInit);
        }

        private void LateInit()
        {
            if (Player == null && !PlayerPath.IsEmpty)
                Player = GetNodeOrNull<Node2D>(PlayerPath);
            if (Camera == null && !CameraPath.IsEmpty)
                Camera = GetNodeOrNull<Camera2D>(CameraPath);
            if (DialoguePanel == null && !DialoguePanelPath.IsEmpty)
                DialoguePanel = GetNodeOrNull<CutsceneDialoguePanel>(DialoguePanelPath);
            if (FadeOverlay == null && !FadeOverlayPath.IsEmpty)
                FadeOverlay = GetNodeOrNull<CanvasItem>(FadeOverlayPath);

            // 若仍为 null，尝试通过 group 查找玩家（兜底）
            if (Player == null)
                Player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (Player != null && Camera == null)
                Camera = Player.GetNodeOrNull<Camera2D>("Camera2D");

            if (_battleSceneManager == null && !BattleSceneManagerPath.IsEmpty)
                _battleSceneManager = GetNodeOrNull<Kuros.Scenes.BattleSceneManager>(BattleSceneManagerPath);

            GD.Print($"[Cutscene] LateInit — Player: {(Player != null ? Player.Name : "null")}, Camera: {(Camera != null ? Camera.Name : "null")}, BattleSceneManager: {(_battleSceneManager != null ? _battleSceneManager.Name : "null")}");
        }

        public override void _Input(InputEvent @event)
        {
            if (!IsPlaying) return;
            if (@event.IsActionPressed(SkipActionName))
                IsSkipRequested = true;
        }

        // ── 公开 API ──────────────────────────────────────────────────────
        /// <summary>播放一段过场动画序列。已在播放中则忽略。</summary>
        public async Task PlayCutscene(CutsceneSequence sequence)
        {
            if (IsPlaying) return;

            GD.Print($"[Cutscene] === PlayCutscene 开始: {sequence.SequenceId}, Steps数量: {sequence.Steps?.Count ?? 0} ===");
            GD.Print($"[Cutscene] DisablePlayerInput={sequence.DisablePlayerInput}, TakeOverCamera={sequence.TakeOverCamera}");
            GD.Print($"[Cutscene] Player节点: {(Player != null ? Player.Name : "null")}");
            GD.Print($"[Cutscene] Camera节点: {(Camera != null ? Camera.Name : "null")}");
            GD.Print($"[Cutscene] DialoguePanel: {(DialoguePanel != null ? DialoguePanel.Name : "null")}");
            GD.Print($"[Cutscene] FadeOverlay: {(FadeOverlay != null ? FadeOverlay.Name : "null")}");

            IsPlaying       = true;
            IsSkipRequested = false;

            EmitSignal(SignalName.CutsceneStarted, sequence.SequenceId);

            // 隐藏 BattleSceneManager 管理的 UI
            _battleSceneManager?.HideAllUI();

            // 禁用玩家输入，并同时隐藏玩家（Shadow 等子节点随父节点一起消失）
            _playerWasVisible = false;
            if (sequence.DisablePlayerInput)
            {
                if (Player != null)
                {
                    Player.ProcessMode = ProcessModeEnum.Disabled;
                    if (Player.Visible)
                    {
                        Player.Hide();
                        _playerWasVisible = true;
                    }
                    GD.Print($"[Cutscene] 禁用+隐藏: {Player.Name}");
                }
                else
                {
                    GD.PrintErr("[Cutscene] DisablePlayerInput=true 但 Player 节点为 null，请检查 PlayerPath");
                }
            }

            // 隐藏节点并同时禁用其 ProcessMode（防止角色仍在移动/运算）
            _hiddenNodes.Clear();
            foreach (var path in HideNodePaths)
            {
                var node = GetNodeOrNull<CanvasItem>(path);
                if (node != null)
                {
                    if (node.Visible)
                    {
                        node.Hide();
                        _hiddenNodes.Add(node);
                    }
                    node.ProcessMode = ProcessModeEnum.Disabled;
                    GD.Print($"[Cutscene] 隐藏+禁用: {node.Name}");
                }
                else
                {
                    GD.PrintErr($"[Cutscene] HideNodePaths 中路径 '{path}' 未找到节点");
                }
            }

            // 接管摄像机
            if (sequence.TakeOverCamera)
                BeginCameraOverride();

            // 依次执行步骤
            var ctx = new CutsceneContext(this, sequence);
            int stepIndex = 0;
            foreach (var step in sequence.Steps ?? new Godot.Collections.Array<CutsceneStep>())
            {
                if (step == null)
                {
                    GD.PrintErr($"[Cutscene] 第 {stepIndex} 步为 null，跳过");
                    stepIndex++;
                    continue;
                }
                if (IsSkipRequested && !step.ExecuteOnSkip)
                {
                    GD.Print($"[Cutscene] 跳过请求，跳过第 {stepIndex} 步: {step.GetType().Name}");
                    stepIndex++;
                    continue;
                }
                GD.Print($"[Cutscene] 执行第 {stepIndex} 步: {step.GetType().Name}");
                await step.Execute(ctx);
                GD.Print($"[Cutscene] 第 {stepIndex} 步完成: {step.GetType().Name}");
                stepIndex++;
            }

            // 还原
            if (sequence.TakeOverCamera)
                EndCameraOverride();

            // 恢复玩家输入与可见性
            if (sequence.DisablePlayerInput && Player != null && GodotObject.IsInstanceValid(Player))
            {
                Player.ProcessMode = ProcessModeEnum.Inherit;
                if (_playerWasVisible)
                    Player.Show();
                GD.Print($"[Cutscene] 恢复输入+显示: {Player.Name}");
            }

            // 恢复隐藏节点的显示和 ProcessMode
            foreach (var node in _hiddenNodes)
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    node.Show();
                    node.ProcessMode = ProcessModeEnum.Inherit;
                    GD.Print($"[Cutscene] 恢复显示+启用: {node.Name}");
                }
            }
            _hiddenNodes.Clear();

            DialoguePanel?.HidePanel();

            // 恢复 BattleSceneManager 管理的 UI
            _battleSceneManager?.ShowAllUI();

            IsPlaying = false;
            GD.Print($"[Cutscene] === PlayCutscene 结束: {sequence.SequenceId} ===");
            EmitSignal(SignalName.CutsceneFinished, sequence.SequenceId);
        }

        /// <summary>手动请求跳过当前过场（例如由 UI 按钮调用）。</summary>
        public void RequestSkip() => IsSkipRequested = true;

        // ── 摄像机接管 ────────────────────────────────────────────────────
        private void BeginCameraOverride()
        {
            if (Camera == null) return;

            // 禁用 CameraFollow 脚本的 _Process / _PhysicsProcess
            Camera.SetProcess(false);
            Camera.SetPhysicsProcess(false);

            // 解除与玩家节点的父级变换绑定，使摄像机可自由移动至任意世界坐标
            _cameraWasTopLevel = Camera.TopLevel;
            var gpos = Camera.GlobalPosition;
            Camera.TopLevel        = true;
            Camera.GlobalPosition  = gpos;

            // 锁定 CameraZoneManager，防止玩家 ProcessMode.Disabled 导致区域误退出
            _cameraZoneManager ??= GetTree().Root.GetNodeOrNull<CameraZoneManager>("BattleScene/CameraZoneManager");
            _cameraZoneManager?.LockZone();
        }

        private void EndCameraOverride()
        {
            if (Camera == null) return;

            Camera.TopLevel = _cameraWasTopLevel;
            Camera.SetProcess(true);
            Camera.SetPhysicsProcess(true);
            // CameraFollow 恢复后会通过 position_smoothing 平滑归位

            // 解锁 CameraZoneManager
            _cameraZoneManager?.UnlockZone();
        }
    }
}
