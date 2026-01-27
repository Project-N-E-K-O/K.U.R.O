using System;
using System.Collections.Generic;
using System.Linq;
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
	/// 适配 RigidBody2D 物品场景的包装类
	/// 将 RigidBody2D 作为子节点，提供 WorldItemEntity 的功能
	/// </summary>
	public partial class RigidBodyWorldItemEntity : Node2D, IWorldItemEntity
	{
		[Signal] public delegate void ItemTransferredEventHandler(RigidBodyWorldItemEntity entity, GameActor actor, ItemDefinition item, int amount);
		[Signal] public delegate void ItemTransferFailedEventHandler(RigidBodyWorldItemEntity entity, GameActor actor);

		[ExportGroup("Item")]
		[Export] public ItemDefinition? ItemDefinition { get; set; }
		[Export(PropertyHint.File, "*.tres,*.res")] public string ItemDefinitionResourcePath { get; set; } = string.Empty;
		[Export] public string ItemIdOverride { get; set; } = string.Empty;
		[Export(PropertyHint.Range, "1,9999,1")] public int Quantity { get; set; } = 1;

		[ExportGroup("Pickup")]
		[Export] public NodePath GrabAreaPath { get; set; } = new NodePath("GrabArea");
		[Export] public bool AutoDisableTriggerOnPickup { get; set; } = true;
		[Export] public uint GrabAreaCollisionLayer { get; set; } = 1u << 1;  // collision_layer = 2
		[Export] public uint GrabAreaCollisionMask { get; set; } = 1u;        // collision_mask = 1

		[ExportGroup("Physics")]
		[Export] public NodePath RigidBodyPath { get; set; } = new NodePath(".");
		[Export] public double FlightDurationSeconds = 0.4; // how long the item keeps flying before dropping
		[Export] public float DropLimitDistance { get; set; } = 64f;


		public InventoryItemStack? CurrentStack { get; private set; }
		public string ItemId => !string.IsNullOrWhiteSpace(ItemIdOverride)
			? ItemIdOverride
			: ItemDefinition?.ItemId ?? DeriveItemIdFromScene();

		private ItemDefinition? _lastTransferredItem;
		private int _lastTransferredAmount;
		private GameActor? _focusedActor;
		private bool _isPicked;
		private Area2D? _grabArea;
		private RigidBody2D? _rigidBody;
		private bool _refreezePending = false;
		private double _refreezeTimer = 0.0;
		private const double RefreezeTimeThreshold = 0.25; // seconds below speed threshold to refreeze
		private const float RefreezeSpeedThreshold = 8.0f; // speed below which we consider the body at rest
		private float _initialGravityScale = 0.0f;
		private bool _inFlight = false;
		private double _flightTimer = 0.0;
		private const float DropVerticalSpeed = 240f; // downward speed when starting drop
		private const float DropHorizontalDamping = 0.6f; // horizontal speed multiplier when dropping
		private bool _isDropping = false;
		private float _dropStartY = 0f;
		private bool _initialMonitoring;
		private bool _initialMonitorable;
		private uint _initialCollisionLayer;
		private uint _initialCollisionMask;
		private readonly System.Collections.Generic.HashSet<GameActor> _actorsInRange = new();

		public GameActor? LastDroppedBy { get; set; }
		
		/// <summary>
		/// 检查指定 Actor 是否在 GrabArea 范围内
		/// </summary>
		public bool IsActorInRange(GameActor actor)
		{
			return _actorsInRange.Contains(actor);
		}
		
		/// <summary>
		/// 获取在范围内的所有 Actor
		/// </summary>
		public System.Collections.Generic.IReadOnlyCollection<GameActor> ActorsInRange => _actorsInRange;

		public override void _Ready()
		{
			base._Ready();
			
			// 添加到组，方便通过场景树查找
			if (!IsInGroup("world_items"))
			{
				AddToGroup("world_items");
			}
			if (!IsInGroup("pickables"))
			{
				AddToGroup("pickables");
			}
			
			InitializeStack();
			ResolveRigidBody();
			ResolveGrabArea();
			UpdateSprite();
			SetProcess(true);
		}

		public override void _ExitTree()
		{
			base._ExitTree();
			if (_grabArea != null)
			{
				_grabArea.BodyEntered -= OnBodyEntered;
				_grabArea.BodyExited -= OnBodyExited;
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

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
			UpdateSprite();
		}

		public void InitializeFromItem(ItemDefinition definition, int quantity)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			quantity = Math.Max(1, quantity);

			ItemDefinition = definition;
			Quantity = quantity;
			CurrentStack = new InventoryItemStack(definition, quantity);
			UpdateSprite();
		}

		private void UpdateSprite()
		{
			// 查找 RigidBody2D 下的 Sprite2D
			if (_rigidBody != null)
			{
				var sprite = _rigidBody.GetNodeOrNull<Sprite2D>("Sprite2D");
				if (sprite != null && ItemDefinition?.Icon != null)
				{
					sprite.Texture = ItemDefinition.Icon;
				}
			}
		}

		public virtual void ApplyThrowImpulse(Vector2 velocity)
		{
			if (_rigidBody == null)
			{
				// Try to resolve rigidbody if it wasn't found earlier
				ResolveRigidBody();
			}
			if (_rigidBody != null)
			{
				// Ensure the rigid body is active (avoid referencing Mode/ModeEnum)
				try
				{
					_rigidBody.Sleeping = false;
					// Unset 'freeze' flag on the RigidBody so it can move when thrown
					try
					{
						_rigidBody.Set("freeze", false);
					}
					catch { /* ignore if property not available */ }
					// enter flight state: disable gravity while flying and start flight timer
					try { _rigidBody.GravityScale = 0.0f; } catch { try { _rigidBody.Set("gravity_scale", 0.0f); } catch { } }
					_inFlight = true;
					_flightTimer = 0.0;
				}
				catch
				{
					// ignore if property not available on this build
				}
				// Ensure the physics body position lines up with the Node2D root
				_rigidBody.GlobalPosition = GlobalPosition;
				// Set linear velocity and apply impulse to simulate a throw
				_rigidBody.LinearVelocity = velocity;
				// ApplyImpulse may be available; call defensively
				try
				{
					_rigidBody.ApplyImpulse(velocity);
				}
				catch
				{
					// fallback: no-op
				}
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			if (_rigidBody == null) return;

			// Flight handling: keep flying for a short duration, then start drop
			if (_inFlight)
			{
				_flightTimer += delta;
				var vel = _rigidBody.LinearVelocity;
				// keep vertical velocity minimal during flight so it travels horizontally
				try { _rigidBody.LinearVelocity = new Vector2(vel.X, 0); } catch { }
				if (_flightTimer >= FlightDurationSeconds)
				{
					_inFlight = false;
					// restore gravity so the item drops
					try { _rigidBody.GravityScale = _initialGravityScale; } catch { try { _rigidBody.Set("gravity_scale", _initialGravityScale); } catch { } }
					// apply downward velocity while damping horizontal speed
					try { _rigidBody.LinearVelocity = new Vector2(vel.X * DropHorizontalDamping, DropVerticalSpeed); } catch { }
					// start drop tracking and refreeze detection
					_isDropping = true;
					try { _dropStartY = _rigidBody.GlobalPosition.Y; } catch { _dropStartY = GlobalPosition.Y; }
					_refreezePending = true;
					_refreezeTimer = 0.0;
				}
				return;
			}

			if (_refreezePending && !_isPicked)
			{
				// If we're in a dropping phase, check vertical drop limit first
				if (_isDropping)
				{
					try
					{
						var currentY = _rigidBody.GlobalPosition.Y;
						if (currentY - _dropStartY >= DropLimitDistance)
						{
							// hit drop limit — consider touching ground
							try { _rigidBody.Set("freeze", true); } catch { }
							try { _rigidBody.LinearVelocity = Vector2.Zero; } catch { }
							_isDropping = false;
							_refreezePending = false;
							_refreezeTimer = 0.0;
							return;
						}
					}
					catch { }
				}
				var speed = _rigidBody.LinearVelocity.Length();
				if (speed <= RefreezeSpeedThreshold)
				{
					_refreezeTimer += delta;
					if (_refreezeTimer >= RefreezeTimeThreshold)
					{
						// re-freeze the body and clear velocity
						try { _rigidBody.Set("freeze", true); } catch { }
						try { _rigidBody.LinearVelocity = Vector2.Zero; } catch { }
						_refreezePending = false;
						_refreezeTimer = 0.0;
					}
				}
				else
				{
					_refreezeTimer = 0.0;
				}
			}
		}

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
			SyncPlayerHandAndQuickBar(actor);

			if (Quantity > 0)
			{
				if (_lastTransferredItem != null && _lastTransferredAmount > 0)
				{
					EmitSignal(SignalName.ItemTransferred, this, actor, _lastTransferredItem, _lastTransferredAmount);
				}
				return true;
			}

			_isPicked = true;
			if (AutoDisableTriggerOnPickup)
			{
				DisableGrabArea();
			}

			OnPicked(actor);
			return true;
		}

		private void SyncPlayerHandAndQuickBar(GameActor actor)
		{
			if (actor is SamplePlayer player)
			{
				player.SyncLeftHandItemFromSlot();
				player.UpdateHandItemVisual();

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

		private void ResolveRigidBody()
		{
			if (RigidBodyPath.IsEmpty)
			{
				// 尝试查找子节点中的 RigidBody2D
				_rigidBody = GetNodeOrNull<RigidBody2D>(".");
				if (_rigidBody == null)
				{
					_rigidBody = FindChild("RigidBody2D", recursive: true) as RigidBody2D;
				}
			}
			else
			{
				_rigidBody = GetNodeOrNull<RigidBody2D>(RigidBodyPath);
			}
			
			if (_rigidBody == null)
			{
				GD.PrintErr($"{Name}: 未找到 RigidBody2D 节点。请检查 RigidBodyPath 设置或确保场景中有 RigidBody2D 子节点。");
			}
			else
			{
				GD.Print($"{Name}: RigidBody2D resolved at {_rigidBody.GetPath()}");
				try { _initialGravityScale = _rigidBody.GravityScale; } catch { _initialGravityScale = 0.0f; }
			}
		}

		private void ResolveGrabArea()
		{
			if (GrabAreaPath.IsEmpty)
			{
				// 尝试直接查找 GrabArea
				_grabArea = GetNodeOrNull<Area2D>("GrabArea");
				// 如果找不到，尝试在 RigidBody2D 下查找
				if (_grabArea == null && _rigidBody != null)
				{
					_grabArea = _rigidBody.GetNodeOrNull<Area2D>("GrabArea");
				}
			}
			else
			{
				_grabArea = GetNodeOrNull<Area2D>(GrabAreaPath);
			}

			if (_grabArea == null)
			{
				GD.PrintErr($"{Name} 缺少 GrabArea 节点，无法进行拾取检测。请检查 GrabAreaPath 设置或确保场景中有名为 'GrabArea' 的 Area2D 节点。");
				return;
			}

			_initialMonitoring = _grabArea.Monitoring;
			_initialMonitorable = _grabArea.Monitorable;
			_initialCollisionLayer = _grabArea.CollisionLayer;
			_initialCollisionMask = _grabArea.CollisionMask;
			
			// 设置碰撞层和遮罩，确保可以被玩家检测到
			// 注意：collision_mask 应该检测玩家的 collision_layer（通常是第1层）
			// collision_layer 是物品所在的层（第2层），用于被玩家的 AttackArea 检测
			_grabArea.CollisionLayer = GrabAreaCollisionLayer;
			_grabArea.CollisionMask = GrabAreaCollisionMask; // 应该包含玩家的 collision_layer（通常是 1）
			_grabArea.Monitoring = true;  // 检测进入的 Body（玩家）
			_grabArea.Monitorable = true; // 可以被其他 Area 检测到
			
			_grabArea.BodyEntered += OnBodyEntered;
			_grabArea.BodyExited += OnBodyExited;
			
			GD.Print($"{Name}: GrabArea resolved at {_grabArea.GetPath()}, collision_layer={GrabAreaCollisionLayer}, collision_mask={GrabAreaCollisionMask}");
		}

		private void OnBodyEntered(Node2D body)
		{
			GD.Print($"[RigidBodyWorldItemEntity] {Name}: OnBodyEntered 被调用，body: {body.Name}, 类型: {body.GetType().Name}");
			
			if (body is GameActor actor)
			{
				_actorsInRange.Add(actor);
				
				// 设置第一个进入的 Actor 为聚焦对象（用于向后兼容）
				if (_focusedActor == null)
				{
					_focusedActor = actor;
					GD.Print($"[RigidBodyWorldItemEntity] {Name}: {actor.Name} 进入了可拾取区域 (GrabArea)。物品: {ItemId}, 数量: {Quantity}");
				}
				else
				{
					GD.Print($"[RigidBodyWorldItemEntity] {Name}: {actor.Name} 进入了可拾取区域，当前范围内有 {_actorsInRange.Count} 个 Actor。");
				}
			}
			else
			{
				GD.Print($"[RigidBodyWorldItemEntity] {Name}: 进入的 body 不是 GameActor，类型: {body.GetType().Name}");
			}
		}

		private void OnBodyExited(Node2D body)
		{
			if (body is GameActor actor)
			{
				_actorsInRange.Remove(actor);
				
				// 如果离开的是聚焦对象，清除聚焦
				if (_focusedActor == actor)
				{
					GD.Print($"[RigidBodyWorldItemEntity] {Name}: {actor.Name} 离开了可拾取区域 (GrabArea)。");
					_focusedActor = null;
					
					// 如果有其他 Actor 在范围内，选择第一个作为新的聚焦对象
					if (_actorsInRange.Count > 0)
					{
						_focusedActor = _actorsInRange.First();
						GD.Print($"[RigidBodyWorldItemEntity] {Name}: 切换聚焦对象为 {_focusedActor.Name}。");
					}
				}
			}
		}

		private void DisableGrabArea()
		{
			if (_grabArea == null) return;

			_grabArea.Monitoring = false;
			_grabArea.Monitorable = false;
			_grabArea.CollisionLayer = 0;
			_grabArea.CollisionMask = 0;
		}

		private void RestoreGrabArea()
		{
			if (_grabArea == null) return;

			_grabArea.Monitoring = _initialMonitoring;
			_grabArea.Monitorable = _initialMonitorable;
			_grabArea.CollisionLayer = _initialCollisionLayer;
			_grabArea.CollisionMask = _initialCollisionMask;
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
			if (CurrentStack != null) return;

			var definition = ResolveItemDefinition();
			if (definition == null)
			{
				if (ItemDefinition == null && string.IsNullOrWhiteSpace(ItemDefinitionResourcePath))
				{
					return;
				}

				GameLogger.Error(nameof(RigidBodyWorldItemEntity), $"{Name} 无法解析物品定义，路径：{ItemDefinitionResourcePath}, 推断 Id：{ItemId}");
				QueueFree();
				return;
			}

			Quantity = Math.Max(1, Quantity);
			CurrentStack = new InventoryItemStack(definition, Quantity);
		}

		private bool TryTransferToActor(GameActor actor)
		{
			var stack = CurrentStack;
			if (stack == null) return false;

			var inventory = ResolveInventoryComponent(actor);
			if (inventory == null)
			{
				GameLogger.Warn(nameof(RigidBodyWorldItemEntity), $"Actor {actor.Name} 缺少 PlayerInventoryComponent，无法拾取 {ItemId}。");
				return false;
			}

			int accepted = inventory.AddItemSmart(stack.Item, stack.Quantity, showPopupIfFirstTime: true);
			if (accepted <= 0)
			{
				GameLogger.Info(nameof(RigidBodyWorldItemEntity), $"Actor {actor.Name} 的物品栏已满，无法拾取 {ItemId}。");
				return false;
			}

			if (accepted < stack.Quantity)
			{
				stack.Remove(accepted);
				Quantity = stack.Quantity;
				_lastTransferredItem = stack.Item;
				_lastTransferredAmount = accepted;
				GameLogger.Info(nameof(RigidBodyWorldItemEntity), $"{actor.Name} 仅拾取了 {accepted} 个 {ItemId}，剩余 {Quantity} 个保留在地面。");
				RestoreGrabArea();
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
			if (ItemDefinition != null) return ItemDefinition;

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
				return System.IO.Path.GetFileNameWithoutExtension(SceneFilePath);
			}

			return Name;
		}

		private static PlayerInventoryComponent? ResolveInventoryComponent(GameActor actor)
		{
			if (actor == null) return null;

			if (actor is SamplePlayer samplePlayer && samplePlayer.InventoryComponent != null)
			{
				return samplePlayer.InventoryComponent;
			}

			var direct = actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
			if (direct != null) return direct;

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
				if (child is T typed) return typed;

				if (child.GetChildCount() > 0)
				{
					var nested = FindChildComponent<T>(child);
					if (nested != null) return nested;
				}
			}

			return null;
		}
	}
}
