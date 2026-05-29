using Godot;
using Kuros.Actors.Heroes;
using System.Collections.Generic;

namespace Kuros.Companions
{
    /// <summary>
    /// Lightweight companion follow controller for P2.
    /// Keeps a floating offset relative to player and updates front/back render order dynamically.
    /// </summary>
    public partial class P2CompanionController : CharacterBody2D, ICompanionStateSource
    {
        [ExportCategory("Companion State")]
        [Export] public string CompanionRoleName { get; set; } = "support";
        [Export(PropertyHint.Range, "1,9999,1")] public int ReportedMaxHp { get; set; } = 100;
        [Export(PropertyHint.Range, "0,9999,1")] public int ReportedCurrentHp { get; set; } = 100;

        public string CompanionName => Name;
        public int CurrentHp => Mathf.Clamp(ReportedCurrentHp, 0, Mathf.Max(1, ReportedMaxHp));
        public int MaxHp => Mathf.Max(1, ReportedMaxHp);
        public bool IsCompanionAvailable => IsInsideTree() && Visible;
        public string CompanionRole => CompanionRoleName;

        [ExportCategory("Follow")]
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        [Export] public NodePath CompanionAnchorPath { get; set; } = new("CompanionAnchor");
        [Export] public Vector2 FollowOffset { get; set; } = new(320f, -80f);
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;
        [Export(PropertyHint.Range, "10,5000,1")] public float MaxCatchUpSpeed { get; set; } = 1400f;
        [Export] public bool AlwaysFollowBehindPlayer { get; set; } = true;
        [Export] public bool KeepCompanionOnFacingSide { get; set; } = false; 

        [ExportCategory("Floating")]
        [Export(PropertyHint.Range, "0,200,0.1")] public float FloatAmplitude { get; set; } = 22f;
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float FloatFrequency { get; set; } = 1.8f;

        [ExportCategory("Render Layer")]
        [Export] public bool EnableDynamicLayering { get; set; } = true;
        [Export(PropertyHint.Range, "0,200,1")] public int FrontLayerDelta { get; set; } = 0;
        [Export(PropertyHint.Range, "-200,0,1")] public int BackLayerDelta { get; set; } = -1;
        [Export(PropertyHint.Range, "0,100,0.1")] public float LayerSwitchDeadZone { get; set; } = 8f;
        [Export] public bool LayerByFacingDirection { get; set; } = false;

        [ExportCategory("Visual")]
        [Export] public NodePath SpritePath { get; set; } = new("Sprite2D");
        [Export] public bool SyncFacingWithPlayer { get; set; } = true;

        [ExportCategory("Dialogic Hint")]
        /// <summary>
        /// P2 的 Dialogic 角色资源路径（.dch）。气泡会跟随 BubbleAnchorPath 节点位置显示。
        /// 需要在 Dialogic Variables 设置中定义 'p2_hint_text' 变量（默认值为空字符串）。
        /// </summary>
        [Export(PropertyHint.File, "*.dch")] public string P2CharacterPath { get; set; } = "res://dialogic/character/P2.dch";
        /// <summary>气泡锚点节点（相对于 P2CompanionController 自身）。留空则以自身位置为锚点。</summary>
        [Export] public NodePath BubbleAnchorPath { get; set; } = new(".");
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float HintDisplaySeconds { get; set; } = 2.2f;
        [Export(PropertyHint.Range, "1,20,1")] public int MaxHintQueueSize { get; set; } = 6;

        [ExportCategory("Debug")]
        [Export] public bool EnableDebugHintHotkey { get; set; } = true;
        [Export] public Key DebugHintKey { get; set; } = Key.F7;

        private MainCharacter? _player;
        private Node2D? _companionAnchor;
        private Sprite2D? _sprite;
        private float _hoverClock;
        private int _layerSign = 1;

        // Dialogic hint queue
        private readonly Queue<string> _hintQueue = new();
        private bool _dialogicBusy;
        private bool _waitingForHintEnd;
        private GodotObject? _dialogic;
        private Callable _timelineEndedCallable;

        public override void _Ready()
        {
            AddToGroup("companions");

            _dialogic = GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null)
            {
                _timelineEndedCallable = Callable.From(OnDialogicTimelineEnded);
                _dialogic.Connect("timeline_ended", _timelineEndedCallable);
            }

            ResolveReferences();

            if (_player != null)
            {
                GlobalPosition = ComputeTargetPosition(0f);
                UpdateVisualFacing();
                UpdateDynamicLayering();
                PushHint("ready");
            }

            // P2CompanionController.cs _Ready() 末尾
            var animHsm = GetNodeOrNull("AnimHSM");
            animHsm?.Call("initialize", this);   // 触发 _setup()，建立状态和迁移
            animHsm?.Call("set_active", true);   // 进入 StateIdle
        }

        public override void _ExitTree()
        {
            if (_dialogic != null && IsInstanceValid(_dialogic)
                && _dialogic.IsConnected("timeline_ended", _timelineEndedCallable))
            {
                _dialogic.Disconnect("timeline_ended", _timelineEndedCallable);
            }
        }

        public override void _Notification(int what)
        {
            // P2 被隐藏时（过场 HideNodePaths 触发），立即取消正在显示的 hint 气泡
            if (what == NotificationVisibilityChanged && !Visible)
                CancelActiveHint();
        }

        private void CancelActiveHint()
        {
            _hintQueue.Clear();
            if (!_waitingForHintEnd) return;
            _waitingForHintEnd = false;
            _dialogicBusy = false;
            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null && IsInstanceValid(_dialogic) && _dialogic.HasMethod("end_timeline"))
                _dialogic.Call("end_timeline");
        }

        public override void _PhysicsProcess(double delta)
        {
            ResolveReferences();
            if (_player == null)
            {
                return;
            }

            _hoverClock += (float)delta;

            Vector2 target = ComputeTargetPosition(_hoverClock);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * (float)delta);
            Vector2 next = GlobalPosition.Lerp(target, blend);

            float maxStep = Mathf.Max(10f, MaxCatchUpSpeed) * (float)delta;
            Vector2 step = next - GlobalPosition;
            if (step.Length() > maxStep)
            {
                next = GlobalPosition + step.Normalized() * maxStep;
            }

            GlobalPosition = next;
            UpdateVisualFacing();
            UpdateDynamicLayering();

            if (EnableDebugHintHotkey && Input.IsKeyPressed(DebugHintKey))
            {
                PushHint("combat");
            }
        }

        private void ResolveReferences()
        {
            if (_player == null || !IsInstanceValid(_player) || !_player.IsInsideTree())
            {
                _player = GetNodeOrNull<MainCharacter>(PlayerPath)
                    ?? GetNodeOrNull<MainCharacter>(NormalizeRelativePath(PlayerPath))
                    ?? GetTree().GetFirstNodeInGroup("player") as MainCharacter;
            }

            if (_player == null)
            {
                _companionAnchor = null;
                return;
            }

            _sprite ??= GetNodeOrNull<Sprite2D>(SpritePath);

            if (_companionAnchor == null || !IsInstanceValid(_companionAnchor) || !_companionAnchor.IsInsideTree())
            {
                _companionAnchor = _player.GetNodeOrNull<Node2D>(CompanionAnchorPath)
                    ?? _player.FindChild(CompanionAnchorPath.ToString(), recursive: true, owned: false) as Node2D;
            }
        }

        private Vector2 ComputeTargetPosition(float hoverClock)
        {
            if (_player == null)
            {
                return GlobalPosition;
            }

            Vector2 anchorPosition = _companionAnchor?.GlobalPosition ?? _player.GlobalPosition;
            float sideSign;
            if (AlwaysFollowBehindPlayer)
            {
                // Keep P2 on the opposite side of player's forward direction.
                sideSign = _player.FacingRight ? -1f : 1f;
            }
            else
            {
                sideSign = _player.FacingRight ? 1f : -1f;
                if (!KeepCompanionOnFacingSide)
                {
                    // Keep a stable side in world-space to avoid crossing through player when turning.
                    sideSign = GlobalPosition.X >= anchorPosition.X ? 1f : -1f;
                }
            }
            float hover = Mathf.Sin(hoverClock * Mathf.Tau * FloatFrequency) * FloatAmplitude;

            return anchorPosition + new Vector2(FollowOffset.X * sideSign, FollowOffset.Y + hover);
        }

        private void UpdateDynamicLayering()
        {
            if (!EnableDynamicLayering || _player == null)
            {
                return;
            }

            if (AlwaysFollowBehindPlayer)
            {
                ZIndex = _player.ZIndex + BackLayerDelta;
                return;
            }

            if (LayerByFacingDirection)
            {
                float xDiff = GlobalPosition.X - _player.GlobalPosition.X;
                if (Mathf.Abs(xDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    bool sameSideAsFacing = _player.FacingRight ? xDiff >= 0f : xDiff <= 0f;
                    _layerSign = sameSideAsFacing ? 1 : -1;
                }
            }
            else
            {
                float yDiff = GlobalPosition.Y - _player.GlobalPosition.Y;
                if (Mathf.Abs(yDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    _layerSign = yDiff >= 0f ? 1 : -1;
                }
            }

            int delta = _layerSign >= 0 ? FrontLayerDelta : BackLayerDelta;
            ZIndex = _player.ZIndex + delta;
        }

        private void UpdateVisualFacing()
        {
            if (!SyncFacingWithPlayer || _player == null || _sprite == null)
            {
                return;
            }

            // P2 texture is authored facing right by default.
            _sprite.FlipH = !_player.FacingRight;
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

        /// <summary>
        /// 播放 p2_hint timeline 中对应 label 的对话气泡。
        /// hintKey 对应 p2_hint.dtl 中的 label 名称（如 "ready"、"combat"）。
        /// 在 Dialogic 编辑器中编辑 dialogic/timeline/p2_hint.dtl 来维护文本。
        /// </summary>
        public void PushHint(string hintKey)
        {
            if (string.IsNullOrWhiteSpace(hintKey))
                return;

            // 过场播放期间禁止触发 hint
            var cutsceneManager = GetTree().GetFirstNodeInGroup("cutscene_manager");
            if (cutsceneManager is Kuros.Systems.Cutscene.CutsceneManager cm && cm.IsPlaying)
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 如果 Dialogic 正在播放非本 hint 的 Timeline（例如剧情对话），则放弃
            var currentTimeline = _dialogic.Get("current_timeline");
            if (currentTimeline.VariantType != Variant.Type.Nil && !_waitingForHintEnd)
                return;

            if (_dialogicBusy)
            {
                if (_hintQueue.Count < Mathf.Max(1, MaxHintQueueSize))
                    _hintQueue.Enqueue(hintKey);
                return;
            }

            StartDialogicHint(hintKey);
        }

        /// <summary>
        /// 显示运行时动态生成的文本（如 AI 个性台词），文本不在 DTL 中预定义。
        /// 通过 Dialogic 变量 "p2_hint_text" 注入后播放 p2_hint.dtl 的 label:direct。
        /// 需在 Dialogic 编辑器 Variables 中预先定义 "p2_hint_text" 变量（默认值留空即可）。
        /// </summary>
        public void PushHintDirect(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 在启动 timeline 前注入变量，label:direct 中的 {p2_hint_text} 会读取该值
            _dialogic.Get("VAR").AsGodotObject()?.Call("set_variable", "p2_hint_text", rawText);
            PushHint("direct");
        }

        private void StartDialogicHint(string hintKey)
        {
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            _dialogicBusy = true;
            _waitingForHintEnd = true;

            // 若还没有激活的 Layout，先加载 textbubble_A 样式
            var styles = _dialogic.Get("Styles").AsGodotObject();
            if (styles != null && !(bool)styles.Call("has_active_layout_node"))
                styles.Call("load_style", "textbubble_A");

            // 以 label 为入口启动 p2_hint timeline（文本全部定义在 dtl 文件中）
            var layoutNode = _dialogic.Call("start", "p2_hint", hintKey).AsGodotObject() as Node;

            // 将气泡定位到 BubbleAnchorPath 指定节点
            if (!string.IsNullOrEmpty(P2CharacterPath) && !BubbleAnchorPath.IsEmpty && layoutNode != null)
            {
                var anchor = GetNodeOrNull<Node2D>(BubbleAnchorPath);
                if (anchor != null)
                    layoutNode.CallDeferred("register_character", P2CharacterPath, anchor);
            }

            // 到时后自动结束（若玩家未手动推进）
            float delay = Mathf.Max(0.5f, HintDisplaySeconds);
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (_waitingForHintEnd && _dialogic != null && IsInstanceValid(_dialogic))
                    _dialogic.Call("end_timeline");
            };
        }

        private void OnDialogicTimelineEnded()
        {
            if (!_waitingForHintEnd)
                return;

            _waitingForHintEnd = false;
            _dialogicBusy = false;

            if (_hintQueue.Count > 0)
                StartDialogicHint(_hintQueue.Dequeue());
        }
    }
}