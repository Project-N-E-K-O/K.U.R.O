using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Items.Effects;
using Kuros.Items;
using Kuros.Managers;
using Kuros.Systems.Inventory;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Items.World
{
	/// <summary>
	/// 地图场景中的物品实体，与具体 <see cref="ItemDefinition"/> 一一对应，
	/// 负责识别物品、暴露属性并在拾取/丢弃时与背包系统交互。
	/// </summary>
	public partial class WorldItemEntity : CharacterBody2D, IWorldItemEntity
	{
		[Signal] public delegate void ItemTransferredEventHandler(WorldItemEntity entity, GameActor actor, ItemDefinition item, int amount);
		[Signal] public delegate void ItemTransferFailedEventHandler(WorldItemEntity entity, GameActor actor);

		[ExportGroup("Item")]
		[Export] public ItemDefinition? ItemDefinition { get; set; }
		[Export(PropertyHint.File, "*.tres,*.res")] public string ItemDefinitionResourcePath { get; set; } = string.Empty;
		[Export] public string ItemIdOverride { get; set; } = string.Empty;
		[Export(PropertyHint.Range, "1,9999,1")] public int Quantity { get; set; } = 1;

		[ExportGroup("Pickup")]
		[Export] public Area2D TriggerArea { get; private set; } = null!;
		[Export] public bool AutoDisableTriggerOnPickup { get; set; } = true;

		[ExportGroup("World Physics")]
		[Export(PropertyHint.Range, "0,4000,1")] public float ThrowFriction = 600f;
		[Export] public uint BodyCollisionLayer { get; set; } = 0;  // 禁用 body 碰撞
		[Export] public uint BodyCollisionMask { get; set; } = 0;   // 禁用 body 碰撞
		[Export] public uint TriggerCollisionLayer { get; set; } = 1u << 1;  // collision_layer = 2
		[Export] public uint TriggerCollisionMask { get; set; } = 1u;        // collision_mask = 1

		public InventoryItemStack? CurrentStack { get; private set; }
		public string ItemId => !string.IsNullOrWhiteSpace(ItemIdOverride)
			? ItemIdOverride
			: ItemDefinition?.ItemId ?? DeriveItemIdFromScene();

		private ItemDefinition? _lastTransferredItem;
		private int _lastTransferredAmount;
		private Vector2 _pendingVelocity;
		private GameActor? _focusedActor;
		private bool _isPicked;
		private bool _initialMonitoring;
		private bool _initialMonitorable;
		private uint _initialCollisionLayer;
		private uint _initialCollisionMask;

		public GameActor? LastDroppedBy { get; set; }
		protected Vector2 PendingVelocity => _pendingVelocity;

		public override void _Ready()
		{
			base._Ready();
			InitializeStack();
			ResolveTriggerArea();
			ApplyCollisionSettings();
			UpdateSprite(); // 更新精灵贴图
			SetProcess(true);
			SetPhysicsProcess(true);
		}

		public override void _ExitTree()
		{
			base._ExitTree();
			if (TriggerArea != null)
			{
				TriggerArea.BodyEntered -= OnBodyEntered;
				TriggerArea.BodyExited -= OnBodyExited;
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			// 注意：拾取逻辑已移至 PlayerItemInteractionComponent 统一处理
			// 这里不再监听 take_up 输入，避免多个物品同时被拾取的问题
			// WorldItemEntity 只负责追踪哪个 Actor 在范围内（_focusedActor）
			
			if (_isPicked || _focusedActor == null)
			{
				return;
			}

			if (!GodotObject.IsInstanceValid(_focusedActor))
			{
				_focusedActor = null;
				return;
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			if (_pendingVelocity.LengthSquared() > 0.0001f)
			{
				Velocity = _pendingVelocity;
				MoveAndSlide();
				float decel = ThrowFriction * (float)delta;
				_pendingVelocity = _pendingVelocity.MoveToward(Vector2.Zero, decel);
				if (_pendingVelocity.LengthSquared() <= 0.0001f)
				{
					Velocity = Vector2.Zero;
				}
			}
			else if (Velocity.LengthSquared() > 0.0001f)
			{
				Velocity = Vector2.Zero;
			}
		}

		public Dictionary<string, float> GetAttributeSnapshot()
		{
			if (CurrentStack != null)
			{
				return CurrentStack.Item.GetAttributeSnapshot(CurrentStack.Quantity);
			}

			return ItemDefinition != null
				? ItemDefinition.GetAttributeSnapshot(Math.Max(1, Quantity))
				: new Dictionary<string, float>();
		}

		public void InitializeFromStack(InventoryItemStack stack)
		{
			if (stack == null) throw new ArgumentNullException(nameof(stack));

			ItemDefinition = stack.Item;
			Quantity = stack.Quantity;
			CurrentStack = new InventoryItemStack(stack.Item, stack.Quantity);
			
			// 更新精灵贴图
			UpdateSprite();
		}

		public void InitializeFromItem(ItemDefinition definition, int quantity)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			quantity = Math.Max(1, quantity);

			ItemDefinition = definition;
			Quantity = quantity;
			CurrentStack = new InventoryItemStack(definition, quantity);
			
			// 更新精灵贴图
			UpdateSprite();
		}

		/// <summary>
		/// 根据物品定义更新精灵贴图
		/// </summary>
		private void UpdateSprite()
		{
			var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
			if (sprite == null)
			{
				return;
			}

			// 如果物品定义有图标，使用它；否则保持默认贴图
			// 注意：保持场景中设置的缩放，不重置
			if (ItemDefinition?.Icon != null)
			{
				sprite.Texture = ItemDefinition.Icon;
			}
		}

		public virtual void ApplyThrowImpulse(Vector2 velocity)
		{
			_pendingVelocity = velocity;
			Velocity = velocity;
		}

		/// <summary>
		/// 供外部调用的拾取方法（如状态机或其他组件触发）
		/// </summary>
		public bool TryPickupByActor(GameActor actor)
		{
			if (_isPicked)
			{
				return false;
			}

			if (!TryTransferToActor(actor))
			{
				return false;
			}

			ApplyItemEffects(actor, ItemEffectTrigger.OnPickup);
			
			// 同步玩家的左手物品和快捷栏显示
			SyncPlayerHandAndQuickBar(actor);

			// 检查是否为部分转移（地面仍有剩余物品）
			if (Quantity > 0)
			{
				// 部分转移 - 发出信号但保持实体可交互
				if (_lastTransferredItem != null && _lastTransferredAmount > 0)
				{
					EmitSignal(SignalName.ItemTransferred, this, actor, _lastTransferredItem, _lastTransferredAmount);
				}
				return true;
			}

			// 完整转移 - 继续正常的拾取完成流程
			_isPicked = true;
			if (AutoDisableTriggerOnPickup)
			{
				// 使用 CallDeferred 避免在信号回调期间修改监控状态
				CallDeferred(MethodName.DisableTriggerArea);
			}

			OnPicked(actor);
			return true;
		}
		
		/// <summary>
		/// 同步玩家的左手物品和快捷栏显示
		/// </summary>
		private void SyncPlayerHandAndQuickBar(GameActor actor)
		{
			if (actor is SamplePlayer player)
			{
				// 同步左手物品
				player.SyncLeftHandItemFromSlot();
				player.UpdateHandItemVisual();
				
				// 刷新 BattleHUD 快捷栏显示
				var battleHUD = UIManager.Instance?.GetUI<BattleHUD>("BattleHUD");
				if (battleHUD == null)
				{
					battleHUD = GetTree().GetFirstNodeInGroup("ui") as BattleHUD;
				}
				
				if (battleHUD != null)
				{
					battleHUD.CallDeferred("UpdateQuickBarDisplay");
					int leftHandSlot = player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex < 5 ? player.LeftHandSlotIndex : -1;
					battleHUD.CallDeferred("UpdateHandSlotHighlight", leftHandSlot, 0);
				}
			}
		}

		private void ResolveTriggerArea()
		{
			if (TriggerArea == null)
			{
				TriggerArea = GetNodeOrNull<Area2D>("TriggerArea") ??
							  throw new InvalidOperationException($"{Name} 缺少 TriggerArea 节点。");
			}

			_initialMonitoring = TriggerArea.Monitoring;
			_initialMonitorable = TriggerArea.Monitorable;
			TriggerArea.BodyEntered += OnBodyEntered;
			TriggerArea.BodyExited += OnBodyExited;

			TriggerArea.CollisionLayer = TriggerCollisionLayer;
			TriggerArea.CollisionMask = TriggerCollisionMask;
			_initialCollisionLayer = TriggerArea.CollisionLayer;
			_initialCollisionMask = TriggerArea.CollisionMask;
		}

		private void ApplyCollisionSettings()
		{
			CollisionLayer = BodyCollisionLayer;
			CollisionMask = BodyCollisionMask;
		}

		private void OnBodyEntered(Node2D body)
		{
			if (body is GameActor actor)
			{
				// 只有在没有聚焦 actor 时才设置新的，避免多人模式下焦点被抢夺
				if (_focusedActor == null)
				{
					_focusedActor = actor;
				}
			}
		}

		private void OnBodyExited(Node2D body)
		{
			if (_focusedActor == null)
			{
				return;
			}

			if (body == _focusedActor)
			{
				_focusedActor = null;
			}
		}

		private void HandlePickupRequest(GameActor actor)
		{
			if (!TryPickupByActor(actor))
			{
				EmitSignal(SignalName.ItemTransferFailed, this, actor);
			}
		}

		private void DisableTriggerArea()
		{
			if (TriggerArea == null)
			{
				return;
			}

			// 使用 SetDeferred 避免在信号回调期间修改监控状态
			TriggerArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			TriggerArea.SetDeferred(Area2D.PropertyName.Monitorable, false);
			TriggerArea.CollisionLayer = 0;
			TriggerArea.CollisionMask = 0;
		}

		private void RestoreTriggerArea()
		{
			if (TriggerArea == null)
			{
				return;
			}

			// 使用 SetDeferred 避免在信号回调期间修改监控状态
			TriggerArea.SetDeferred(Area2D.PropertyName.Monitoring, _initialMonitoring);
			TriggerArea.SetDeferred(Area2D.PropertyName.Monitorable, _initialMonitorable);
			TriggerArea.CollisionLayer = _initialCollisionLayer;
			TriggerArea.CollisionMask = _initialCollisionMask;
		}

		private void OnPicked(GameActor actor)
		{
			if (_lastTransferredItem != null && _lastTransferredAmount > 0)
			{
				EmitSignal(SignalName.ItemTransferred, this, actor, _lastTransferredItem, _lastTransferredAmount);
			}

			QueueFree();
		}

		private void InitializeStack()
		{
			// 如果 CurrentStack 已经被 InitializeFromStack() 初始化，跳过
			if (CurrentStack != null)
			{
				return;
			}
			
			var definition = ResolveItemDefinition();
			if (definition == null)
			{
				// 如果没有配置 ItemDefinition 或 ItemDefinitionResourcePath，
				// 可能是通过 WorldItemSpawner 动态生成的，等待 InitializeFromStack() 调用
				if (ItemDefinition == null && string.IsNullOrWhiteSpace(ItemDefinitionResourcePath))
				{
					// 静默等待，不报错
					return;
				}
				
				GameLogger.Error(nameof(WorldItemEntity), $"{Name} 无法解析物品定义，路径：{ItemDefinitionResourcePath}, 推断 Id：{ItemId}");
				QueueFree();
				return;
			}

			Quantity = Math.Max(1, Quantity);
			CurrentStack = new InventoryItemStack(definition, Quantity);
		}

		private bool TryTransferToActor(GameActor actor)
		{
			var stack = CurrentStack;
			if (stack == null)
			{
				return false;
			}

			var inventory = ResolveInventoryComponent(actor);
			if (inventory == null)
			{
				GameLogger.Warn(nameof(WorldItemEntity), $"Actor {actor.Name} 缺少 PlayerInventoryComponent，无法拾取 {ItemId}。");
				return false;
			}

			// 使用 AddItemSmart 优先添加到快捷栏，溢出放入背包
			int accepted = inventory.AddItemSmart(stack.Item, stack.Quantity, showPopupIfFirstTime: true);
			if (accepted <= 0)
			{
				GameLogger.Info(nameof(WorldItemEntity), $"Actor {actor.Name} 的物品栏已满，无法拾取 {ItemId}。");
				return false;
			}

			if (accepted < stack.Quantity)
			{
				stack.Remove(accepted);
				Quantity = stack.Quantity;
				_lastTransferredItem = stack.Item;
				_lastTransferredAmount = accepted;
				GameLogger.Info(nameof(WorldItemEntity), $"{actor.Name} 仅拾取了 {accepted} 个 {ItemId}，剩余 {Quantity} 个保留在地面。");
				// 使用 CallDeferred 避免在信号回调期间修改监控状态
				CallDeferred(MethodName.RestoreTriggerArea);
				_isPicked = false;
				return true;
			}

			_lastTransferredItem = stack.Item;
			_lastTransferredAmount = accepted;
			CurrentStack = null;
			Quantity = 0;
			return true;
		}

		private ItemDefinition? ResolveItemDefinition()
		{
			if (ItemDefinition != null)
			{
				return ItemDefinition;
			}

			if (!string.IsNullOrWhiteSpace(ItemDefinitionResourcePath))
			{
				var loaded = ResourceLoader.Load<ItemDefinition>(ItemDefinitionResourcePath);
				if (loaded != null)
				{
					ItemDefinition = loaded;
					return ItemDefinition;
				}
			}

			return ItemDefinition;
		}

		private string DeriveItemIdFromScene()
		{
			if (!string.IsNullOrEmpty(SceneFilePath))
			{
				return Path.GetFileNameWithoutExtension(SceneFilePath);
			}

			return Name;
		}

		private static PlayerInventoryComponent? ResolveInventoryComponent(GameActor actor)
		{
			if (actor == null)
			{
				return null;
			}

			if (actor is SamplePlayer samplePlayer && samplePlayer.InventoryComponent != null)
			{
				return samplePlayer.InventoryComponent;
			}

			var direct = actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
			if (direct != null)
			{
				return direct;
			}

			return FindChildComponent<PlayerInventoryComponent>(actor);
		}

		private void ApplyItemEffects(GameActor actor, ItemEffectTrigger trigger)
		{
			ItemDefinition?.ApplyEffects(actor, trigger);
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
	}
}
