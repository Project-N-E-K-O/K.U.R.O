using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.UI
{
	/// <summary>
	/// 简单的物品快捷栏，展示背包前几格物品。
	/// </summary>
	public partial class QuickSlotBar : HBoxContainer
	{
		[Export] public NodePath InventoryPath { get; set; } = new("../Player/Inventory");
		[Export(PropertyHint.Range, "1,8,1")] public int SlotDisplayCount { get; set; } = 4;

		private PlayerInventoryComponent? _inventory;
		private InventoryContainer? _backpack;
		private readonly List<(TextureRect icon, Label quantity)> _slots = new();

		public override void _Ready()
		{
			_inventory = ResolveInventoryComponent();
			if (_inventory == null)
			{
				GD.PushWarning($"{Name}: QuickSlotBar 未能找到 PlayerInventoryComponent。");
				return;
			}

			_backpack = _inventory.Backpack;
			if (_backpack == null)
			{
				GD.PushWarning($"{Name}: PlayerInventoryComponent.Backpack 尚未初始化。");
				return;
			}

			_backpack.InventoryChanged += OnInventoryChanged;
			BuildSlotVisuals();
			RefreshSlots();
		}

		public override void _ExitTree()
		{
			if (_backpack != null)
			{
				_backpack.InventoryChanged -= OnInventoryChanged;
			}
			base._ExitTree();
		}

		private void OnInventoryChanged()
		{
			RefreshSlots();
		}

		private void BuildSlotVisuals()
		{
			ClearSlotNodes();
			_slots.Clear();

			for (int i = 0; i < SlotDisplayCount; i++)
			{
				var panel = new PanelContainer
				{
					CustomMinimumSize = new Vector2(64, 72),
					ThemeTypeVariation = "QuickSlotPanel"
				};

				var vbox = new VBoxContainer
				{
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
					Alignment = BoxContainer.AlignmentMode.Center
				};

				var icon = new TextureRect
				{
					ExpandMode = TextureRect.ExpandModeEnum.KeepSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					CustomMinimumSize = new Vector2(48, 48)
				};

				var quantity = new Label
				{
					HorizontalAlignment = HorizontalAlignment.Center,
					ThemeTypeVariation = "QuickSlotLabel"
				};

				vbox.AddChild(icon);
				vbox.AddChild(quantity);
				panel.AddChild(vbox);
				AddChild(panel);

				_slots.Add((icon, quantity));
			}
		}

		private void ClearSlotNodes()
		{
			foreach (Node child in GetChildren())
			{
				RemoveChild(child);
				child.QueueFree();
			}
		}

		private PlayerInventoryComponent? ResolveInventoryComponent()
		{
			if (InventoryPath.GetNameCount() > 0)
			{
				var node = GetNodeOrNull<Node>(InventoryPath);
				if (node is PlayerInventoryComponent inventoryComponent)
				{
					return inventoryComponent;
				}
			}

			var scene = GetTree().CurrentScene ?? GetTree().Root;
			return FindChildComponent<PlayerInventoryComponent>(scene);
		}

		private static T? FindChildComponent<T>(Node root) where T : Node
		{
			foreach (Node child in root.GetChildren())
			{
				if (child is T typed)
				{
					return typed;
				}

				if (child.GetChildCount() > 0)
				{
					var nested = FindChildComponent<T>(child);
					if (nested != null)
					{
						return nested;
					}
				}
			}

			return null;
		}

		private void RefreshSlots()
		{
			if (_backpack == null)
			{
				return;
			}

			for (int i = 0; i < _slots.Count; i++)
			{
				var (icon, quantity) = _slots[i];
				var stack = i < _backpack.Slots.Count ? _backpack.Slots[i] : null;
				if (stack == null)
				{
					icon.Texture = null;
					quantity.Text = string.Empty;
					icon.Modulate = new Color(1, 1, 1, 0.15f);
				}
				else
				{
					icon.Texture = stack.Item.Icon;
					icon.Modulate = Colors.White;
					quantity.Text = stack.Quantity > 1 ? $"x{stack.Quantity}" : string.Empty;
				}
			}
		}
	}
}
