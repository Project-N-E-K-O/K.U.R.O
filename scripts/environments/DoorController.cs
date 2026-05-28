using Godot;

namespace Kuros.Environments
{
    /// <summary>
    /// 感应门控制器。
    /// - 挂载在门的根节点（RigidBody2D）上
    /// - 监听 Area2D 的 body_entered / body_exited 信号（纯信号驱动，零每帧开销）
    /// - 玩家进入 Area2D → 播放 door_open
    /// - 玩家完全离开 Area2D → 播放 door_close
    /// </summary>
    public partial class DoorController : Node
    {
        [Export] public NodePath AreaPath { get; set; } = new NodePath("Area2D");
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath("AnimationPlayer");

        private AnimationPlayer? _animPlayer;
        private int _playerCount = 0; // 支持多玩家或重叠边界，用计数而非布尔

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);

            var area = GetNodeOrNull<Area2D>(AreaPath);
            if (area == null)
            {
                GD.PushWarning("[DoorController] 找不到 Area2D，路径：" + AreaPath);
                return;
            }

            area.BodyEntered += OnBodyEntered;
            area.BodyExited += OnBodyExited;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;

            _playerCount++;
            if (_playerCount == 1)
                _animPlayer?.Play("door_open");
        }

        private void OnBodyExited(Node2D body)
        {
            if (!body.IsInGroup("player")) return;

            _playerCount = Mathf.Max(0, _playerCount - 1);
            if (_playerCount == 0)
                _animPlayer?.Play("door_close");
        }

        public override void _ExitTree()
        {
            var area = GetNodeOrNull<Area2D>(AreaPath);
            if (area == null) return;

            area.BodyEntered -= OnBodyEntered;
            area.BodyExited  -= OnBodyExited;
        }
    }
}
