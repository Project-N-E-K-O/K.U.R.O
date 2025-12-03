using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.UI
{
	/// <summary>
	/// [已廢棄] 简单的物品快捷栏，展示背包前几格物品。
	/// 此組件已被 BattleHUD 中的快捷欄取代，請使用 BattleHUD 來顯示快捷欄。
	/// </summary>
	[Obsolete("QuickSlotBar 已廢棄，請使用 BattleHUD 中的快捷欄功能。")]
	public partial class QuickSlotBar : HBoxContainer
	{
		[Export] public NodePath InventoryPath { get; set; } = new("../Player/Inventory");
		[Export(PropertyHint.Range, "1,8,1")] public int SlotDisplayCount { get; set; } = 4;
		[Export] public bool ShowHeldSlot { get; set; } = true;
		[Export] public string HeldSlotTitle { get; set; } = "Held";
		[Export] public Color SelectedSlotColor { get; set; } = new Color(1.2f, 1.2f, 1.2f, 1);
		[Export] public Color SelectedBorderColor { get; set; } = new Color(1, 0, 0, 1);
		[Export] public Color UnselectedSlotColor { get; set; } = new Color(0.85f, 0.85f, 0.85f, 1);
		[Export] public Color UnselectedBorderColor { get; set; } = new Color(0, 0, 0, 0.2f);

		private PlayerInventoryComponent? _inventory;
		private InventoryContainer? _backpack;
		private readonly List<SlotVisual> _slots = new();
		private StyleBoxFlat? _selectedStyle;
		private StyleBoxFlat? _unselectedStyle;

		private sealed class SlotVisual
		{
			public PanelContainer Panel { get; }
			public TextureRect Icon { get; }
			public Label Quantity { get; }
			public Label? Caption { get; }

			public SlotVisual(PanelContainer panel, TextureRect icon, Label quantity, Label? caption)
			{
				Panel = panel;
				Icon = icon;
				Quantity = quantity;
				Caption = caption;
			}
		}

		public override void _Ready()
		{
			// [已廢棄] 此組件已停用，所有初始化邏輯已禁用
			// 請使用 BattleHUD 中的快捷欄功能
			GD.PushWarning($"{Name}: QuickSlotBar 已廢棄，請從場景中移除此節點並使用 BattleHUD 中的快捷欄。");
			
			// 隱藏自己，避免視覺干擾
			Visible = false;
			return;
			
			// 以下代碼已停用
			#pragma warning disable CS0162 // Unreachable code detected
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
			_inventory.ItemPicked += OnHeldItemChanged;
			_inventory.ItemRemoved += OnHeldItemRemoved;
			_inventory.ActiveBackpackSlotChanged += OnSelectionChanged;
			_selectedStyle = BuildStyleBox(SelectedSlotColor, SelectedBorderColor);
			_unselectedStyle = BuildStyleBox(UnselectedSlotColor, UnselectedBorderColor);
			BuildSlotVisuals();
			RefreshSlots();
			#pragma warning restore CS0162
		}

		public override void _ExitTree()
		{
			if (_backpack != null)
			{
				_backpack.InventoryChanged -= OnInventoryChanged;
			}
			if (_inventory != null)
			{
				_inventory.ItemPicked -= OnHeldItemChanged;
				_inventory.ItemRemoved -= OnHeldItemRemoved;
				_inventory.ActiveBackpackSlotChanged -= OnSelectionChanged;
			}
			base._ExitTree();
		}

		private void OnInventoryChanged()
		{
			RefreshSlots();
		}

		private void OnHeldItemChanged(ItemDefinition _) => RefreshSlots();
		private void OnHeldItemRemoved(string _) => RefreshSlots();
		private void OnSelectionChanged(int _) => RefreshSlots();

		private void BuildSlotVisuals()
		{
			ClearSlotNodes();
			_slots.Clear();

			for (int i = 0; i < SlotDisplayCount; i++)
			{
				_slots.Add(CreateSlotPanel($"Slot {i + 1}"));
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

			int selectedIndex = _inventory?.SelectedBackpackSlot ?? -1;
			for (int i = 0; i < _slots.Count; i++)
			{
				var stack = i < _backpack.Slots.Count ? _backpack.Slots[i] : null;
				var slot = _slots[i];
				UpdateSlotVisual(slot, stack);
				bool isHeld = i == selectedIndex;
				ApplySelectionStyle(slot.Panel, isHeld);

				if (slot.Caption != null)
				{
					if (isHeld && ShowHeldSlot)
					{
						slot.Caption.Text = $"{HeldSlotTitle} ({i + 1})";
					}
					else
					{
						slot.Caption.Text = $"Slot {i + 1}";
					}
				}
			}
		}

		private SlotVisual CreateSlotPanel(string title)
		{
			var panel = new PanelContainer
			{
				CustomMinimumSize = new Vector2(64, 80),
				ThemeTypeVariation = "QuickSlotPanel"
			};

			var vbox = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ShrinkCenter,
				Alignment = BoxContainer.AlignmentMode.Center
			};

			var caption = new Label
			{
				Text = title,
				HorizontalAlignment = HorizontalAlignment.Center,
				ThemeTypeVariation = "QuickSlotCaption"
			};
			vbox.AddChild(caption);

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

			return new SlotVisual(panel, icon, quantity, caption);
		}

		private static void UpdateSlotVisual(SlotVisual? slot, InventoryItemStack? stack)
		{
			if (slot == null)
			{
				return;
			}

			if (stack == null || stack.IsEmpty)
			{
				slot.Icon.Texture = null;
				slot.Quantity.Text = string.Empty;
				slot.Icon.Modulate = new Color(1, 1, 1, 0.15f);
			}
			else
			{
				slot.Icon.Texture = stack.Item.Icon;
				slot.Icon.Modulate = Colors.White;
				slot.Quantity.Text = stack.Quantity > 1 ? $"x{stack.Quantity}" : string.Empty;
			}
		}

		private void ApplySelectionStyle(PanelContainer panel, bool selected)
		{
			if (selected && _selectedStyle != null)
			{
				panel.Modulate = SelectedSlotColor;
				panel.AddThemeStyleboxOverride("panel", _selectedStyle);
			}
			else if (!selected && _unselectedStyle != null)
			{
				panel.Modulate = UnselectedSlotColor;
				panel.AddThemeStyleboxOverride("panel", _unselectedStyle);
			}
		}

		private StyleBoxFlat BuildStyleBox(Color fillColor, Color borderColor)
		{
			var style = new StyleBoxFlat
			{
				BgColor = fillColor
			};
			style.BorderWidthLeft = 3;
			style.BorderWidthRight = 3;
			style.BorderWidthTop = 3;
			style.BorderWidthBottom = 3;
			style.BorderColor = borderColor;
			style.CornerRadiusTopLeft = 6;
			style.CornerRadiusTopRight = 6;
			style.CornerRadiusBottomLeft = 6;
			style.CornerRadiusBottomRight = 6;
			return style;
		}
	}
}
