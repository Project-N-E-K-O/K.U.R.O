using Godot;
using Kuros.Managers;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// 核心拾取区域。玩家接触后自动弹出构筑核心 N选1。
    /// 直接挂载到场景中的 Area2D 节点即可。
    /// </summary>
    [GlobalClass]
    public partial class CorePickupZone : Area2D
    {
        [Export] public bool TriggerOnce { get; set; } = true;

        private bool _triggered;

        [Export] public string PlayerGroup { get; set; } = "player";

        public override void _Ready()
        {
            AreaEntered += OnAreaEntered;
        }

        public override void _ExitTree()
        {
            AreaEntered -= OnAreaEntered;
        }

        private void OnAreaEntered(Area2D area)
        {
            if (_triggered && TriggerOnce) return;

            var owner = area.IsInGroup(PlayerGroup) ? (Node)area : area.GetParent();
            if (owner == null || !owner.IsInGroup(PlayerGroup)) return;

            if (BuildSelectionManager.Instance?.TryTriggerCoreSelection() == true)
                _triggered = true;
        }
    }
}
