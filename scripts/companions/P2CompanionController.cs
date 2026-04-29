using Godot;
using Kuros.Actors.Heroes;

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
        [Export] public NodePath HintBubblePath { get; set; } = new("HintBubble");

        [ExportCategory("Debug")]
        [Export] public bool EnableDebugHintHotkey { get; set; } = true;
        [Export] public Key DebugHintKey { get; set; } = Key.F7;

        private MainCharacter? _player;
        private Node2D? _companionAnchor;
        private Sprite2D? _sprite;
        private P2HintBubble? _hintBubble;
        private float _hoverClock;
        private int _layerSign = 1;

        public override void _Ready()
        {
            AddToGroup("companions");
            ResolveReferences();

            if (_player != null)
            {
                GlobalPosition = ComputeTargetPosition(0f);
                UpdateVisualFacing();
                UpdateDynamicLayering();
                PushHint("P2 已就位");
            }
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
                PushHint("注意节奏，先稳住站位");
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
            _hintBubble ??= GetNodeOrNull<P2HintBubble>(HintBubblePath);

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

        public void PushHint(string message)
        {
            ResolveReferences();
            _hintBubble?.EnqueueHint(message);
        }
    }
}