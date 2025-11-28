using Godot;
using Kuros.Core;
using Kuros.Core.Interactions;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Npc
{
    /// <summary>
    /// 示例：简单宝箱互动，打开后将配置物品加入玩家背包。
    /// </summary>
    public partial class ChestInteractable : BaseInteractable
    {
        [ExportGroup("Loot")]
        [Export] public ItemDefinition? RewardItem { get; set; }
        [Export(PropertyHint.Range, "1,9999,1")] public int RewardAmount { get; set; } = 1;

        [ExportGroup("Presentation")]
        [Export] public Texture2D? ClosedTexture { get; set; }
        [Export] public Texture2D? OpenedTexture { get; set; }

        private Sprite2D? _sprite;
        private bool _opened;

        public override void _Ready()
        {
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
            UpdateVisual();
        }

        protected override bool OnCanInteract(GameActor actor)
        {
            return !_opened && RewardItem != null;
        }

        protected override void OnInteract(GameActor actor)
        {
            var inventory = actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (inventory == null || inventory.Backpack == null || RewardItem == null)
            {
                return;
            }

            int inserted = inventory.Backpack.AddItem(RewardItem, RewardAmount);
            if (inserted > 0)
            {
                _opened = true;
                UpdateVisual();
            }
        }

        private void UpdateVisual()
        {
            if (_sprite == null) return;
            _sprite.Texture = _opened ? OpenedTexture ?? _sprite.Texture : ClosedTexture ?? _sprite.Texture;
        }

        protected override void OnInteractionLimitReached(GameActor actor)
        {
            base.OnInteractionLimitReached(actor);
            _opened = true;
            UpdateVisual();
        }
    }
}

