using Godot;
using Kuros.Core;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 接触治疗：挂在 Area2D 下，任意进入碰撞体的玩家恢复生命后自毁父节点。
    /// 可复用于血包、飞行治疗道具、掉落物等任意场景。
    /// </summary>
    [GlobalClass]
    public partial class HealOnContact : Node
    {
        [Export(PropertyHint.Range, "1,9999,1")]
        public int HealAmount { get; set; } = 10;

        [Export]
        public string TargetGroup { get; set; } = "player";

        private Area2D? _area;
        private Node2D? _owner;

        public override void _Ready()
        {
            _area = GetParentOrNull<Area2D>();
            if (_area == null)
            {
                GD.PushWarning("[HealOnContact] 必须挂在 Area2D 子节点下");
                return;
            }

            _area.BodyEntered += OnBodyEntered;
            _owner = _area.GetParentOrNull<Node2D>();
        }

        public override void _ExitTree()
        {
            if (_area != null)
                _area.BodyEntered -= OnBodyEntered;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup(TargetGroup)) return;
            if (body is not GameActor actor) return;

            actor.RestoreHealth(actor.CurrentHealth + HealAmount);
            FloatingDamageTextManager.Instance.ShowFloatingHealing(HealAmount, _owner?.GlobalPosition ?? actor.GlobalPosition);
            _owner?.QueueFree();
        }
    }
}
