using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 交互式过场触发器 —— 玩家进入检测范围后，按下指定按键触发过场动画序列。
    ///
    /// 使用方法：
    ///   1. 将此脚本挂载到一个 Area2D 节点，添加 CollisionShape2D 子节点定义检测范围。
    ///   2. 设置 collision_mask = 4（检测玩家 HitArea 所在的 Layer 3）。
    ///   3. 在 Inspector 中设置 Sequence、InteractAction、HintNodePath、HintOffset。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneInteractTrigger : Area2D
    {
        /// <summary>要播放的过场动画序列。</summary>
        [Export] public CutsceneSequence? Sequence { get; set; }

        /// <summary>触发用的输入动作名称（Project Settings → Input Map 中定义）。</summary>
        [Export] public string InteractAction { get; set; } = "interact";

        /// <summary>只触发一次。</summary>
        [Export] public bool TriggerOnce { get; set; } = true;

        /// <summary>玩家所在的组名。</summary>
        [Export] public string PlayerGroup { get; set; } = "player";

        /// <summary>
        /// 提示节点路径（如"按E"图标或标签），进入范围时显示，离开或触发后隐藏。
        /// 留空则不显示任何提示。
        /// </summary>
        [Export] public NodePath HintNodePath { get; set; } = new NodePath();

        /// <summary>
        /// 提示节点相对于本节点的位置偏移，用于调整场景中提示出现的位置。
        /// </summary>
        [Export] public Vector2 HintOffset { get; set; } = new Vector2(0f, -200f);

        private bool _playerInRange = false;
        private bool _triggered     = false;
        private CanvasItem? _hintNode;

        public override void _Ready()
        {
            AreaEntered += OnAreaEntered;
            AreaExited  += OnAreaExited;

            if (!HintNodePath.IsEmpty)
            {
                _hintNode = GetNodeOrNull<CanvasItem>(HintNodePath);
                if (_hintNode != null)
                {
                    SetHintPosition();
                    _hintNode.Hide();
                }
            }
        }

        public override void _ExitTree()
        {
            AreaEntered -= OnAreaEntered;
            AreaExited  -= OnAreaExited;
        }

        public override void _Process(double delta)
        {
            // 同步提示节点位置（以防父节点被动画驱动移动）
            if (_hintNode != null && _hintNode.Visible)
                SetHintPosition();

            if (!_playerInRange) return;
            if (TriggerOnce && _triggered) return;

            if (Input.IsActionJustPressed(InteractAction))
                TryTrigger();
        }

        private void TryTrigger()
        {
            if (Sequence == null)
            {
                GD.PrintErr($"[CutsceneInteractTrigger] Sequence 未设置！节点: {Name}");
                return;
            }

            var manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
            if (manager == null)
            {
                GD.PrintErr("[CutsceneInteractTrigger] 未找到 CutsceneManager！");
                return;
            }
            if (manager.IsPlaying) return;

            _triggered = true;
            _hintNode?.Hide();
            GD.Print($"[CutsceneInteractTrigger] 触发过场: {Sequence.SequenceId}");
            _ = manager.PlayCutscene(Sequence);
        }

        private void OnAreaEntered(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            _playerInRange = true;

            if (_hintNode != null && !(TriggerOnce && _triggered))
            {
                SetHintPosition();
                _hintNode.Show();
            }
            GD.Print($"[CutsceneInteractTrigger] 玩家进入交互范围 [{Name}]，按 [{InteractAction}] 触发过场");
        }

        private void OnAreaExited(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            _playerInRange = false;
            _hintNode?.Hide();
        }

        private void SetHintPosition()
        {
            var worldPos = GlobalPosition + HintOffset;
            if (_hintNode is Node2D node2D)
                node2D.GlobalPosition = worldPos;
            else if (_hintNode is Control ctrl)
                ctrl.GlobalPosition = worldPos;
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
