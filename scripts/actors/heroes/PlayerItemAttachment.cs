using System;
using Godot;
using Kuros.Core;
using Kuros.Items;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责把最近拾取的物品视觉挂在角色骨骼上，用于反馈当前持有物品。
    /// </summary>
    public partial class PlayerItemAttachment : Node
    {
        [Export] public PlayerInventoryComponent? Inventory { get; set; }
        [Export] public NodePath AttachmentParentPath { get; set; } = new("SpineCharacter/Skeleton2D");
        [Export] public NodePath SpineSlotNodePath { get; set; } = new();
        [Export(PropertyHint.Range, "-512,512,1")] public Vector2 IconOffset { get; set; } = new Vector2(32, -32);
        [Export] public bool FlipWithFacing { get; set; } = true;
        [Export] public int ZIndex { get; set; } = 100;
        [Export] public string SpineSlotDefaultSelection { get; set; } = string.Empty;

        private Node2D? _attachmentParent;
        private Sprite2D? _iconSprite;
        private GameActor? _actor;
        private Node? _spineSlotNode;
        private Node? _SpineSlotIconContainer;
        private string? _previousSlotSelection;
        private const string SlotSelectProperty = "切换名";
        private const string SlotIconName = "HeldItemSlotIcon";

        public override void _Ready()
        {
            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            Inventory ??= _actor?.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (Inventory == null)
            {
                GD.PushWarning($"{Name}: 未找到 PlayerInventoryComponent，无法显示物品附件。");
                return;
            }

            _attachmentParent = ResolveAttachmentParent();
            _spineSlotNode = ResolveSpineSlotNode();
            Inventory.ItemPicked += OnItemPicked;
            Inventory.ItemRemoved += OnItemRemoved;
            Inventory.ActiveBackpackSlotChanged += OnActiveSlotChanged;
            if (Inventory.Backpack != null)
            {
                Inventory.Backpack.InventoryChanged += OnInventoryChanged;
            }
            else
            {
                GD.PushWarning($"{Name}: PlayerInventoryComponent.Backpack 尚未初始化，无法订阅背包事件。");
            }

            // 订阅 QuickBar 事件（如果存在）
            SubscribeToQuickBar();

            UpdateAttachmentIcon();
        }

        /// <summary>
        /// 订阅 QuickBar 的事件
        /// </summary>
        public void SubscribeToQuickBar()
        {
            if (Inventory?.QuickBar != null)
            {
                // 先取消订阅，避免重复
                Inventory.QuickBar.InventoryChanged -= OnQuickBarChanged;
                Inventory.QuickBar.SlotChanged -= OnQuickBarSlotChanged;
                
                // 订阅
                Inventory.QuickBar.InventoryChanged += OnQuickBarChanged;
                Inventory.QuickBar.SlotChanged += OnQuickBarSlotChanged;
            }
        }

        private void OnQuickBarChanged()
        {
            UpdateAttachmentIcon();
        }

        private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
        {
            // 如果变化的是当前选中的快捷栏槽位，更新图标
            if (Inventory != null && slotIndex == Inventory.SelectedQuickBarSlot)
            {
                UpdateAttachmentIcon();
            }
        }

        public override void _ExitTree()
        {
            if (Inventory != null)
            {
                Inventory.ItemPicked -= OnItemPicked;
                Inventory.ItemRemoved -= OnItemRemoved;
                Inventory.ActiveBackpackSlotChanged -= OnActiveSlotChanged;
                if (Inventory.Backpack != null)
                {
                    Inventory.Backpack.InventoryChanged -= OnInventoryChanged;
                }
                // 取消订阅 QuickBar 事件
                if (Inventory.QuickBar != null)
                {
                    Inventory.QuickBar.InventoryChanged -= OnQuickBarChanged;
                    Inventory.QuickBar.SlotChanged -= OnQuickBarSlotChanged;
                }
            }
            base._ExitTree();
        }

        private void OnItemPicked(ItemDefinition item)
        {
            UpdateAttachmentIcon();
        }

        private void ShowItemIcon(Texture2D? texture)
        {
            if (_spineSlotNode != null)
            {
                ShowOnSpineSlot(texture);
                return;
            }

            if (_attachmentParent == null)
            {
                return;
            }

            if (texture == null)
            {
                _iconSprite?.QueueFree();
                _iconSprite = null;
                return;
            }

            _iconSprite ??= CreateIconSprite("HeldItemIcon");
            AttachIconTo(_attachmentParent, texture);
        }

        private void OnItemRemoved(string itemId)
        {
            UpdateAttachmentIcon();
        }

        private void OnActiveSlotChanged(int _)
        {
            UpdateAttachmentIcon();
        }

        private void OnInventoryChanged()
        {
            UpdateAttachmentIcon();
        }

        private void UpdateAttachmentIcon()
        {
            // 优先从 QuickBar 获取选中的物品（左手物品）
            var stack = Inventory?.GetSelectedQuickBarStack();
            
            // 检查是否为空白道具
            if (stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item")
            {
                ShowItemIcon(stack.Item.Icon);
            }
            else
            {
                // 如果 QuickBar 没有有效物品，尝试从 Backpack 获取
                var backpackStack = Inventory?.GetSelectedBackpackStack();
                ShowItemIcon(backpackStack?.Item.Icon);
            }
        }

        private void ShowOnSpineSlot(Texture2D? texture)
        {
            if (_spineSlotNode == null)
            {
                return;
            }

            if (texture == null)
            {
                ClearSpineSlot();
                return;
            }

            _iconSprite ??= CreateIconSprite(SlotIconName);
            AttachIconTo(_SpineSlotIconContainer ?? _spineSlotNode, texture);

            if (_spineSlotNode.HasMethod("set_slot") && texture is AtlasTexture atlasTexture)
            {
                _spineSlotNode.Call("set_slot", atlasTexture.Region, atlasTexture.Atlas);
                return;
            }

            if (_spineSlotNode.HasMethod("set_slot"))
            {
                _spineSlotNode.Call("set_slot");
                return;
            }

            if (_spineSlotNode.HasMeta(SlotSelectProperty))
            {
                // fallback if property stored as metadata
                _previousSlotSelection ??= _spineSlotNode.GetMeta(SlotSelectProperty).AsString();
            }

            if (_previousSlotSelection == null)
            {
                var value = _spineSlotNode.Get(SlotSelectProperty);
                _previousSlotSelection = value.VariantType == Variant.Type.String ? (string)value : SpineSlotDefaultSelection;
            }

            _spineSlotNode.Set(SlotSelectProperty, _iconSprite.Name);
        }

        private void ClearSpineSlot()
        {
            if (_spineSlotNode == null)
            {
                return;
            }

            if (_previousSlotSelection != null)
            {
                _spineSlotNode.Set(SlotSelectProperty, _previousSlotSelection);
            }

            if (_iconSprite != null)
            {
                _iconSprite.Visible = false;
            }
        }

        private Sprite2D CreateIconSprite(string name)
        {
            var sprite = new Sprite2D
            {
                Name = name
            };
            return sprite;
        }

        private void AttachIconTo(Node parent, Texture2D texture)
        {
            if (_iconSprite == null)
            {
                return;
            }

            if (_iconSprite.GetParent() != parent)
            {
                parent.AddChild(_iconSprite);
            }

            _iconSprite.Visible = true;
            _iconSprite.Texture = texture;
            _iconSprite.Position = IconOffset;
            _iconSprite.ZIndex = ZIndex;
            _iconSprite.ZAsRelative = false;

            if (_actor != null && FlipWithFacing)
            {
                var scale = _iconSprite.Scale;
                scale.X = MathF.Abs(scale.X) * (_actor.FacingRight ? 1f : -1f);
                _iconSprite.Scale = scale;
            }
        }

        private Node2D? ResolveAttachmentParent()
        {
            if (_actor == null)
            {
                return null;
            }

            if (AttachmentParentPath.GetNameCount() == 0)
            {
                return _actor;
            }

            return _actor.GetNodeOrNull<Node2D>(AttachmentParentPath);
        }

        private Node? ResolveSpineSlotNode()
        {
            if (SpineSlotNodePath.IsEmpty)
            {
                return null;
            }

            return GetNodeOrNull(SpineSlotNodePath) ?? _actor?.GetNodeOrNull(SpineSlotNodePath);
        }
    }
}

