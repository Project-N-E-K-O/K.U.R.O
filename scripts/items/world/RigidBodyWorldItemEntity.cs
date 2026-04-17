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

		[ExportGroup("Outline Highlight")]
		[Export] public bool EnableGrabAreaOutlineHighlight { get; set; } = true;
		[Export] public NodePath HighlightSpritePath { get; set; } = new("Outline");
		[Export] public Color DefaultOutlineColor { get; set; } = new Color(0f, 0f, 0f, 1f);
		[Export] public Color HighlightOutlineColor { get; set; } = new Color(1f, 1f, 1f, 1f);

		[ExportGroup("Combat")]
		[Export] public float ThrowDamage {get; set;} = 4f;
		[Export] public float MinDamageVelocity { get; set; } = 300f; // 造成伤害的最小速度阈值
        [Export] public float KnockbackForce { get; set; } = 200f; // 击退力度
		[Export] public bool StopOnHit { get; set; } = false; // 命中敌人后是否停止（false = 穿过敌人）

		[ExportGroup("Durability")]
		[Export] public bool IsThrowWeapon { get; set; } = false; // 是否为投掷武器（true=不从背包销毁，进入CD；false=投掷后在LandingHideDelay后销毁）
		[Export(PropertyHint.Range, "0.1,60,0.1")] public float ThrowWeaponCooldown { get; set; } = 2.0f; // 投掷武器的使用冷却时间
		[Export] public NodePath DestructionAnimationPlayerPath { get; set; } = new NodePath(""); // 销毁动画播放器路径
		[Export] public string DestructionAnimationName { get; set; } = "destroy"; // 销毁动画名称
		[Export] public float DestructionAnimationDuration { get; set; } = 0.5f; // 销毁动画时长（如果动画播放器不存在，使用固定时长）
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float LandingHideDelay { get; set; } = 2.0f; // 落点处隐藏延迟（秒）：投掷武器落地后隐藏视觉的等待时间；到达 ThrowWeaponCooldown 后才归还背包并销毁节点
		[Export(PropertyHint.File, "*.tscn")] public string DestructionEffectScene { get; set; } = string.Empty; // 销毁时生成的特效Scene（可选）

		[ExportGroup("Physics")]
		[Export] public NodePath RigidBodyPath { get; set; } = new NodePath(".");
		[Export] public NodePath HitboxAreaPath { get; set; } = new NodePath("Rigidbody2D/Hitbox");
		[Export] public uint ThrowCollisionLayer { get; set; } = 1u << 2; // 投掷时的碰撞层（默认第3层：1u<<2=4，占用第3层；墙/地面的Mask需包含第3层才能检测到投掷物品）
		[Export] public uint ThrowCollisionMask { get; set; } = 0; // 投掷时的碰撞遮罩（0=不检测任何层；如果碰撞只依赖layer，则设为0；如果需要双向检测，则设置为包含墙所在层的值）


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
		private float _throwStartY = 0f; // 投掷起始的Y坐标
		private float _throwHorizontalVelocity = 0f; // 投掷的水平速度
		private bool _isDropping = false;
		private float _dropStartY = 0f;
		private bool _initialMonitoring;
		private bool _initialMonitorable;
		private uint _initialCollisionLayer;
		private uint _initialCollisionMask;
		private uint _initialRigidBodyCollisionLayer; // RigidBody2D 的原始碰撞层
		private uint _initialRigidBodyCollisionMask; // RigidBody2D 的原始碰撞遮罩
		private readonly System.Collections.Generic.HashSet<GameActor> _actorsInRange = new();
		private bool _impactArmed = false; // 是否已激活伤害检测
		private bool _hasDealtDamage = false; // 是否已造成伤害
		private readonly System.Collections.Generic.HashSet<GameActor> _hitActors = new(); // 已命中的 Actor，防止重复伤害
		private Area2D? _hitboxArea; // 用于伤害检测的 Area2D
		private bool _isDestroying = false; // 是否正在销毁中
		private AnimationPlayer? _destructionAnimPlayer; // 销毁动画播放器引用
		private bool _isThrown = false; // 是否正在投掷中
		private Sprite2D? _highlightSprite; // Outline highlight 精灵
		private ShaderMaterial? _outlineMaterial; // Outline 着色器材料
		private Area2D? _cachedPlayerGrabArea; // 缓存的玩家 GrabArea
		private bool _isOutlineHighlighted; // 是否正在高亮显示
		private double _throwCooldownTimer = 0.0; // 投掷武器冷却计时器
		private bool _isInCooldown = false; // 是否在冷却中
		private double _landingHideTimer = 0.0; // 落点隐藏计时器（LandingHideDelay：到期后隐藏视觉，不销毁节点）
		private double _inventoryReturnTimer = 0.0; // 落地后归还背包的计时器（由 ThrowWeaponCooldown 决定，到期后真正销毁节点）
		private int _reservedQuickBarSlotIndex = -1; // 投掷武器飞行期间预留的快捷栏槽位索引

		public GameActor? LastDroppedBy { get; set; }
		
		/// <summary>
		/// 检查投掷武器是否在冷却中
		/// </summary>
		public bool IsThrowWeaponInCooldown => _isInCooldown;

		/// <summary>
		/// 投掷武器预留的快捷栏槽位索引（-1 = 未预留）
		/// </summary>
		public int ReservedQuickBarSlotIndex => _reservedQuickBarSlotIndex;

		/// <summary>
		/// 冷却进度 0→1（1 = 冷却刚开始 / 完全覆盖，0 = 冷却结束）
		/// </summary>
		public float ThrowCooldownProgress => ThrowWeaponCooldown > 0f
			? Mathf.Clamp((float)(_throwCooldownTimer / ThrowWeaponCooldown), 0f, 1f)
			: 0f;

		/// <summary>
		/// 从 ItemDefinition 读取投掷参数，ItemDefinition 为 null 时回退到内置默认值。
		/// </summary>
		private double GetEffectiveThrowParabolicDuration()
			=> ItemDefinition?.ThrowParabolicDuration is > 0 ? ItemDefinition.ThrowParabolicDuration : 0.6;

		private float GetEffectiveThrowParabolicPeakHeight()
			=> ItemDefinition?.ThrowParabolicPeakHeight is > 0 ? ItemDefinition.ThrowParabolicPeakHeight : 200f;

		private float GetEffectiveThrowHorizontalDistance()
			=> ItemDefinition?.ThrowHorizontalDistance is > 0 ? ItemDefinition.ThrowHorizontalDistance : 600f;

		private float GetEffectiveThrowParabolicLandingYOffset()
			=> ItemDefinition?.ThrowParabolicLandingYOffset ?? 100f;

		private Vector2 GetEffectiveThrowStartOffset()
			=> (ItemDefinition != null && ItemDefinition.ThrowStartOffset != Vector2.Zero)
				? ItemDefinition.ThrowStartOffset
				: new Vector2(0, -200);
		
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
			ResolveHitboxArea();
			UpdateSprite();
			ResolveOutlineHighlight();
			UpdateOutlineHighlight(force: true);
			SetProcess(true);
		}

		public override void _ExitTree()
		{
			base._ExitTree();
			if (_grabArea != null)
			{
				var entered = new Callable(this, MethodName.OnBodyEntered);
				var exited = new Callable(this, MethodName.OnBodyExited);
				if (_grabArea.IsConnected(Area2D.SignalName.BodyEntered, entered))
				{
					_grabArea.BodyEntered -= OnBodyEntered;
				}

				if (_grabArea.IsConnected(Area2D.SignalName.BodyExited, exited))
				{
					_grabArea.BodyExited -= OnBodyExited;
				}
			}
			if (_rigidBody != null)
			{
				var rigidEntered = new Callable(this, MethodName.OnRigidBodyEntered);
				if (_rigidBody.IsConnected(RigidBody2D.SignalName.BodyEntered, rigidEntered))
				{
					_rigidBody.BodyEntered -= OnRigidBodyEntered;
				}
			}
			if (_hitboxArea != null)
			{
				var hitboxEntered = new Callable(this, MethodName.OnHitboxBodyEntered);
				if (_hitboxArea.IsConnected(Area2D.SignalName.BodyEntered, hitboxEntered))
				{
					_hitboxArea.BodyEntered -= OnHitboxBodyEntered;
				}
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			UpdateOutlineHighlight();
			
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

		private void ResolveOutlineHighlight()
		{
			if (!EnableGrabAreaOutlineHighlight)
			{
				_outlineMaterial = null;
				return;
			}

			if (_highlightSprite == null || !GodotObject.IsInstanceValid(_highlightSprite))
			{
				_highlightSprite = !HighlightSpritePath.IsEmpty
					? GetNodeOrNull<Sprite2D>(HighlightSpritePath)
					: null;

				_highlightSprite ??= _rigidBody?.GetNodeOrNull<Sprite2D>("Sprite2D");
			}

			if (_highlightSprite?.Material is ShaderMaterial spriteMaterial)
			{
				if (!ReferenceEquals(spriteMaterial, _outlineMaterial))
				{
					_outlineMaterial = spriteMaterial.Duplicate() as ShaderMaterial;
					if (_outlineMaterial != null)
					{
						_outlineMaterial.ResourceLocalToScene = true;
						_highlightSprite.Material = _outlineMaterial;
					}
				}
			}
			else
			{
				_outlineMaterial = null;
			}
		}

		private Area2D? ResolvePlayerGrabArea()
		{
			if (_cachedPlayerGrabArea != null && GodotObject.IsInstanceValid(_cachedPlayerGrabArea))
			{
				return _cachedPlayerGrabArea;
			}

			GameActor? actor = _focusedActor;
			if ((actor == null || !GodotObject.IsInstanceValid(actor)) && GetTree() != null)
			{
				actor = GetTree().GetFirstNodeInGroup("player") as GameActor;
			}

			if (actor == null || !GodotObject.IsInstanceValid(actor))
			{
				return null;
			}

			_cachedPlayerGrabArea = actor.GetNodeOrNull<Area2D>("GrabArea")
				?? actor.GetNodeOrNull<Area2D>("SpineCharacter/GrabArea")
				?? actor.FindChild("GrabArea", recursive: true, owned: false) as Area2D;

			return _cachedPlayerGrabArea;
		}

		/// <summary>
		/// 检查该实例是否处于投掷生命周期中（飞行、落地隐藏、等待归还阶段均视为投掷中）
		/// </summary>
		private bool IsInThrowLifecycle =>
			_isThrown || _inFlight || _landingHideTimer > 0.0 || _inventoryReturnTimer > 0.0;

		private void UpdateOutlineHighlight(bool force = false)
		{
			ResolveOutlineHighlight();
			if (_outlineMaterial == null)
			{
				return;
			}

			// 只高亮离玩家最近的那一件
			// 投掷中的武器实例本身不参与高亮（但不影响场上其他同名武器）
			bool shouldHighlight = false;
			if (EnableGrabAreaOutlineHighlight && !_isPicked && !IsInThrowLifecycle && _grabArea != null && GodotObject.IsInstanceValid(_grabArea))
			{
				var grabArea = ResolvePlayerGrabArea();
				if (grabArea != null && GodotObject.IsInstanceValid(grabArea))
				{
					RigidBodyWorldItemEntity? closest = null;
					float minDist = float.MaxValue;
					foreach (var node in GetTree().GetNodesInGroup("world_items"))
					{
						if (node is RigidBodyWorldItemEntity item
							&& item.EnableGrabAreaOutlineHighlight
							&& !item._isPicked
							&& !item.IsInThrowLifecycle  // 排除投掷中的实例，不参与最近候选
							&& item._grabArea != null && GodotObject.IsInstanceValid(item._grabArea))
						{
							if (item._grabArea.OverlapsArea(grabArea))
							{
								float dist = item.GlobalPosition.DistanceSquaredTo(grabArea.GlobalPosition);
								if (dist < minDist)
								{
									minDist = dist;
									closest = item;
								}
							}
						}
					}
					shouldHighlight = ReferenceEquals(this, closest);
				}
			}

			if (!force && _isOutlineHighlighted == shouldHighlight)
			{
				return;
			}

			_outlineMaterial.SetShaderParameter("outline_color", shouldHighlight ? HighlightOutlineColor : DefaultOutlineColor);
			_isOutlineHighlighted = shouldHighlight;
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
				// 应用投掷起点偏移（优先使用 ItemDefinition 参数）
				_rigidBody.GlobalPosition = GlobalPosition + GetEffectiveThrowStartOffset();
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
				
// 根据飞行距离和持续时间计算实际水平速度
			// 水平速度 = 距离 / 时间，方向由 velocity.X 的符号决定
			float horizontalDistance = GetEffectiveThrowHorizontalDistance();
			float duration = (float)GetEffectiveThrowParabolicDuration();
			float horizontalVelocity = duration > 0 ? (horizontalDistance / duration) : 0f;
			_throwHorizontalVelocity = velocity.X > 0 ? horizontalVelocity : -horizontalVelocity;
			// 记录抛物线飞行相关数据
			_throwStartY = _rigidBody.GlobalPosition.Y;
				
				// 激活伤害检测并应用投掷时的碰撞设置
				if (velocity.LengthSquared() > 0.01f)
				{
					_impactArmed = true;
					_hasDealtDamage = false;
					_hitActors.Clear();
					_isThrown = true;
					
					// 立即清除高亮（进入投掷生命周期后本实例不再参与高亮）
					UpdateOutlineHighlight(force: true);

					// 构筑效果已在 PlayerItemInteractionComponent.TryHandleDrop 中预注册，此处无需重复注册
					
					// 投掷武器：进入冷却状态，并预占原快捷栏槽位
					if (IsThrowWeapon)
					{
						_isInCooldown = true;
						_throwCooldownTimer = ThrowWeaponCooldown;
						
						// 记录原槽位、加入ReservedQuickBarSlots并写入 empty_item 占位
						// 防止拾取其他物品时占用该槽
						if (LastDroppedBy is SamplePlayer throwPlayer && throwPlayer.InventoryComponent?.QuickBar != null)
						{
							_reservedQuickBarSlotIndex = throwPlayer.InventoryComponent.SelectedQuickBarSlot;
							if (_reservedQuickBarSlotIndex >= 0)
							{
								throwPlayer.InventoryComponent.ReservedQuickBarSlots.Add(_reservedQuickBarSlotIndex);
								var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
								if (emptyItem != null)
								{
									throwPlayer.InventoryComponent.QuickBar.TryAddItemToSlot(emptyItem, 1, _reservedQuickBarSlotIndex);
								}
							}
						}
					}
					
				// 立即应用投掷时的碰撞设置，避免与敌人/玩家/其他物品碰撞
				// 先立即设置
				ApplyThrowCollisionSettings();
				// 在下一帧物理更新前再次设置（确保生效）
				CallDeferred(MethodName.ApplyThrowCollisionSettingsDeferred);
				// 在物理更新后再次验证（双重保险）
				GetTree().CreateTimer(0.0).Timeout += () =>
				{
					if (IsInstanceValid(this) && _isThrown && _rigidBody != null)
					{
						var checkLayer = _rigidBody.CollisionLayer;
						var checkMask = _rigidBody.CollisionMask;
						if (checkLayer != ThrowCollisionLayer || checkMask != ThrowCollisionMask)
						{
							GD.PushWarning($"[{Name}] ⚠️ 物理更新后碰撞设置被改变！强制修复: layer={checkLayer}->{ThrowCollisionLayer}, mask={checkMask}->{ThrowCollisionMask}");
							_rigidBody.CollisionLayer = ThrowCollisionLayer;
							_rigidBody.CollisionMask = ThrowCollisionMask;
						}
					}
				};
				}
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			if (_rigidBody == null) return;

			// 更新投掷武器冷却计时器
			if (_isInCooldown)
			{
				_throwCooldownTimer -= delta;
				if (_throwCooldownTimer <= 0.0)
				{
					_isInCooldown = false;
					_throwCooldownTimer = 0.0;
				}
			}

			// 更新落点隐藏计时器（LandingHideDelay：到期后隐藏视觉；投掷武器不销毁节点，等 ThrowWeaponCooldown）
			if (_landingHideTimer > 0.0 && !_inFlight && !_isDestroying)
			{
				_landingHideTimer -= delta;
				if (_landingHideTimer <= 0.0)
				{
					if (IsThrowWeapon)
					{
						HideItemAtLanding(); // 投掷武器：仅隐藏，等 _inventoryReturnTimer 到期后销毁
					}
					else
					{
						DestroyItemAtLanding(); // 非投掷武器：正常播放动画并销毁
					}
					return;
				}
			}

			// 更新落地后归还背包计时器（ThrowWeaponCooldown：到期后归还背包并销毁节点）
			// 武器此时已被 HideItemAtLanding 隐藏，直接 QueueFree 无需动画
			if (_inventoryReturnTimer > 0.0 && !_inFlight)
			{
				_inventoryReturnTimer -= delta;
				if (_inventoryReturnTimer <= 0.0)
				{
					_inventoryReturnTimer = 0.0;
					ReturnToInventory();
					QueueFree();
				}
			}

			// 如果正在投掷中，确保碰撞设置正确（防止被其他代码覆盖）
			if (_isThrown)
			{
				var currentLayer = _rigidBody.CollisionLayer;
				var currentMask = _rigidBody.CollisionMask;
				if (currentLayer != ThrowCollisionLayer || currentMask != ThrowCollisionMask)
				{
					GD.Print($"[{Name}] _PhysicsProcess检测到碰撞设置被改变: layer={currentLayer}->{ThrowCollisionLayer}, mask={currentMask}->{ThrowCollisionMask}，正在修复");
					_rigidBody.CollisionLayer = ThrowCollisionLayer;
					_rigidBody.CollisionMask = ThrowCollisionMask;
					_rigidBody.Sleeping = false; // 强制唤醒
				}
			}

			// 抛物线飞行逻辑：平顺的参数化抛物线轨迹
			if (_inFlight)
			{
				_flightTimer += delta;
			double phase = _flightTimer / GetEffectiveThrowParabolicDuration();
			
			// 安全夹叶 phase 到 [0, 1] 范围
			if (phase > 1.0) phase = 1.0;
			
			// 计算目标落点Y坐标（优先使用 ItemDefinition 参数）
			float landingY = _throwStartY + GetEffectiveThrowParabolicLandingYOffset();
			float peakY = _throwStartY - GetEffectiveThrowParabolicPeakHeight();
			
			// 统一的平顺抛物线公式：使用 sin(phase * π) 生成平顺曲线
			// 这确保了速度导数在 0 和 1 处连续
			// verticalHeight = sin(phase * π) * peakHeight + (1 - sin(phase * π)) * landingOffset
			
			float heightAtPhase;
			
			// 使用改进的参数方程，替代分段处理以消除卡顿
			// 设置一个虚拟的"初始上升速度"和"重力加速度"
			// 确保在 phase=0 时：y=startY, v=0
			// 确保在 phase=1 时：y=landingY, v=0
			
			// 使用对称的钟形曲线（bell curve）：使速度在起点、峰值、落点处连续
			// 上升权重：sin(phase*π) 从 0 平顺上升到 1 再下降到 0
			// 注意：Godot中Y向下为正，所以要减去峰值来向上飞
			float upDown = (float)Mathf.Sin(phase * Mathf.Pi);
			
			// 计算相对于起点的高度
			// 当offset=0时，形成对称抛物线
			// 当offset!=0时，自动调整以确保平顺达到目标落点
			heightAtPhase = Mathf.Lerp(_throwStartY, landingY, (float)phase)  // 线性从start到landing
				- upDown * GetEffectiveThrowParabolicPeakHeight();  // 减去钟形峰值（使物体向上飞）
			
			// 应用到实际位置
			float newY = heightAtPhase;
			float newX = _rigidBody.GlobalPosition.X + (float)(_throwHorizontalVelocity * delta);
			
			_rigidBody.GlobalPosition = new Vector2(newX, newY);
			
			// 计算虚拟速度用于碰撞检测（在飞行时维持水平速度）
			Vector2 simulatedVelocity = new Vector2(
				_throwHorizontalVelocity,
				0f  // 不需要Y速度，水平速度足以通过最小伤害阈值检查
			);
			_rigidBody.LinearVelocity = simulatedVelocity;
			
			// 当抛物线完成，且到达配置的落点高度时，停止飞行
			if (phase >= 1.0)
			{
				_inFlight = false;
				_isDropping = false;
					_refreezePending = false;
					_refreezeTimer = 0.0;
					
					// 确保最终位置精确在落点
					_rigidBody.GlobalPosition = new Vector2(newX, landingY);
					
					// 停止伤害检测并恢复原始碰撞设置
					_impactArmed = false;
					_rigidBody.LinearVelocity = Vector2.Zero;
					try { _rigidBody.Set("freeze", true); } catch { }
					RestoreRigidBodyCollision();
					
					// 开始落点隐藏计时（LandingHideDelay：到期后隐藏视觉）
					// 归还背包由 _inventoryReturnTimer（ThrowWeaponCooldown）独立控制
					if (!_isDestroying)
					{
						_landingHideTimer = LandingHideDelay;

						if (IsThrowWeapon)
						{
							// 投掷武器：LandingHideDelay 后隐藏，ThrowWeaponCooldown 后归还背包
							_inventoryReturnTimer = ThrowWeaponCooldown;
						}
						// 非投掷武器：LandingHideDelay 后直接销毁（由 _PhysicsProcess 中 DestroyItemAtLanding 处理）
					}
					return;
				}
			}

			// 传统的物理冻结处理（用于非飞行中的状态）
			if (_refreezePending && !_isPicked && !_inFlight)
			{
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
						// 停止伤害检测并恢复原始碰撞设置
						_impactArmed = false;
						RestoreRigidBodyCollision();
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

			// 禁止在投掷到归还/销毁的全程拾取（飞行中、落地隐藏期间、冷却归还期间均不可拾取）
			if (_isThrown || _inFlight || _landingHideTimer > 0.0 || _inventoryReturnTimer > 0.0)
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
				// 使用 CallDeferred 避免在信号回调期间修改监控状态
				CallDeferred(MethodName.DisableGrabArea);
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
					int leftHandSlot = player.LeftHandSlotIndex >= 0 && player.LeftHandSlotIndex < 5
						? player.LeftHandSlotIndex
						: (player.InventoryComponent?.SelectedQuickBarSlot ?? -1);
					if (leftHandSlot < 0 || leftHandSlot >= 5)
					{
						leftHandSlot = -1;
					}
					battleHUD.CallDeferred("UpdateHandSlotHighlight", leftHandSlot, -1);
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
				try { _initialGravityScale = _rigidBody.GravityScale; } catch { _initialGravityScale = 0.0f; }
				
				// 保存 RigidBody2D 的原始碰撞设置
				try
				{
					_initialRigidBodyCollisionLayer = _rigidBody.CollisionLayer;
					_initialRigidBodyCollisionMask = _rigidBody.CollisionMask;
					GD.Print($"[{Name}] 保存原始碰撞设置: layer={_initialRigidBodyCollisionLayer}, mask={_initialRigidBodyCollisionMask}");
				}
				catch
				{
					_initialRigidBodyCollisionLayer = 0;
					_initialRigidBodyCollisionMask = 0;
					GD.PrintErr($"[{Name}] 无法读取原始碰撞设置，使用默认值0");
				}
				
				// 启用 RigidBody2D 的接触监控以检测碰撞
				try
				{
					_rigidBody.ContactMonitor = true;
					_rigidBody.MaxContactsReported = 10; // 设置最大接触报告数量
				}
				catch (Exception ex)
				{
					GD.PushWarning($"{Name}: 无法设置 RigidBody2D 的 ContactMonitor: {ex.Message}");
				}
				
				// 连接 RigidBody2D 的 body_entered 信号用于伤害检测
				try
				{
					_rigidBody.BodyEntered += OnRigidBodyEntered;
				}
				catch (Exception ex)
				{
					GD.PrintErr($"{Name}: 无法连接 RigidBody2D.BodyEntered 信号: {ex.Message}");
				}
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
			
		}

		private void ResolveHitboxArea()
		{
			if (HitboxAreaPath.IsEmpty)
			{
				// 尝试直接查找 Hitbox
				_hitboxArea = GetNodeOrNull<Area2D>("Rigidbody2D/Hitbox");
				if (_hitboxArea == null && _rigidBody != null)
				{
					_hitboxArea = _rigidBody.GetNodeOrNull<Area2D>("Hitbox");
				}
			}
			else
			{
				_hitboxArea = GetNodeOrNull<Area2D>(HitboxAreaPath);
			}

			if (_hitboxArea == null)
			{
				GD.PushWarning($"{Name}: 未找到 Hitbox Area2D 节点，将仅使用 RigidBody2D 的 body_entered 信号进行伤害检测");
				return;
			}

			// 设置 Hitbox 的碰撞层和遮罩，确保可以检测到敌人（collision_layer = 2）
			_hitboxArea.CollisionLayer = 0; // Hitbox 不占用碰撞层
			_hitboxArea.CollisionMask = 1u << 1; // 检测第2层（敌人层）
			_hitboxArea.Monitoring = true;
			_hitboxArea.Monitorable = false;

			_hitboxArea.BodyEntered += OnHitboxBodyEntered;
		}

		/// <summary>
		/// 当 Hitbox Area2D 检测到碰撞时调用（用于伤害检测，更可靠）
		/// </summary>
		private void OnHitboxBodyEntered(Node2D body)
		{
			// 只在已激活伤害检测时处理
			if (!_impactArmed)
			{
				return;
			}

			// 检查碰撞的是否是 GameActor（敌人或NPC）
			if (body is GameActor actor)
			{
				// 防止对投掷者造成伤害
				if (actor == LastDroppedBy)
				{
					return;
				}

				// 防止重复伤害同一个 Actor
				if (_hitActors.Contains(actor))
				{
					return;
				}

				// 检查速度是否达到造成伤害的阈值
				if (_rigidBody != null)
				{
					var velocity = _rigidBody.LinearVelocity;
					float speed = velocity.Length();

					if (speed >= MinDamageVelocity)
					{
						TryDealImpactDamage(actor, velocity);
					}
				}
			}
		}

		private void OnBodyEntered(Node2D body)
		{
			if (body is GameActor actor)
			{
				_actorsInRange.Add(actor);
				
				// 设置第一个进入的 Actor 为聚焦对象（用于向后兼容）
				if (_focusedActor == null)
				{
					_focusedActor = actor;
				}
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
					_focusedActor = null;
					
					// 如果有其他 Actor 在范围内，选择第一个作为新的聚焦对象
					if (_actorsInRange.Count > 0)
					{
						_focusedActor = _actorsInRange.First();
					}
				}
			}
		}

		/// <summary>
		/// 当 RigidBody2D 与其他物理体碰撞时调用（用于伤害检测）
		/// </summary>
		private void OnRigidBodyEntered(Node body)
		{
			// 只在已激活伤害检测时处理
			if (!_impactArmed)
			{
				return;
			}

			// 检查碰撞的是否是 GameActor（敌人或NPC）
			if (body is GameActor actor)
			{
				// 防止对投掷者造成伤害
				if (actor == LastDroppedBy)
				{
					return;
				}

				// 防止重复伤害同一个 Actor
				if (_hitActors.Contains(actor))
				{
					return;
				}

				// 检查速度是否达到造成伤害的阈值
				if (_rigidBody != null)
				{
					var velocity = _rigidBody.LinearVelocity;
					float speed = velocity.Length();
					
					if (speed >= MinDamageVelocity)
					{
						TryDealImpactDamage(actor, velocity);
					}
				}
			}
		}

		/// <summary>
		/// 尝试对目标造成碰撞伤害
		/// </summary>
		private bool TryDealImpactDamage(GameActor target, Vector2 impactVelocity)
		{
			if (target == null || _rigidBody == null)
			{
				return false;
			}

			// 计算伤害
			int damage = Mathf.Max(1, Mathf.RoundToInt(ThrowDamage));
			
			if (damage <= 0)
			{
				return false;
			}

			// 造成伤害
			target.TakeDamage(damage, GlobalPosition, LastDroppedBy);
			_hitActors.Add(target);

			// 应用击退效果
			if (KnockbackForce > 0)
			{
				var knockbackDirection = (target.GlobalPosition - GlobalPosition).Normalized();
				if (knockbackDirection.LengthSquared() < 0.01f)
				{
					knockbackDirection = impactVelocity.Normalized();
				}
				
				// GameActor 继承自 CharacterBody2D，可以直接应用击退
				var knockbackVelocity = knockbackDirection * KnockbackForce;
				target.Velocity += knockbackVelocity;
			}

			// 飞行中命中敌人（必须在 StopItemMovement 之前判断，否则 _inFlight 会被提前清除）
			if (_inFlight)
			{
				if (StopOnHit)
				{
					// StopOnHit=true：立即停止飞行，走与落地相同的 LandingHideDelay 流程
					_inFlight = false;
					_flightTimer = 0.0;
					_impactArmed = false;
					if (_rigidBody != null)
					{
						_rigidBody.LinearVelocity = Vector2.Zero;
						try { _rigidBody.Set("freeze", true); } catch { }
					}
					RestoreRigidBodyCollision();
					if (!_isDestroying)
					{
						_landingHideTimer = LandingHideDelay;

						if (IsThrowWeapon)
						{
							_inventoryReturnTimer = ThrowWeaponCooldown;
						}
						// 非投掷武器：LandingHideDelay 后直接销毁
					}
				}
				// StopOnHit=false：穿透敌人，飞行轨迹不受影响，继续按抛物线运动
				return true;
			}

			// 非飞行状态（已落地）命中后根据 StopOnHit 决定是否停止移动
			if (StopOnHit)
			{
				StopItemMovement();
			}

			return true;
		}

		/// <summary>
		/// 停止物品的移动（用于命中敌人后停止）
		/// </summary>
		private void StopItemMovement()
		{
			if (_rigidBody == null) return;

			try
			{
				// 停止移动：设置速度为零
				_rigidBody.LinearVelocity = Vector2.Zero;
				
				// 退出飞行状态
				_inFlight = false;
				_flightTimer = 0.0;
				
				// 恢复重力
				try 
				{ 
					_rigidBody.GravityScale = _initialGravityScale; 
				} 
				catch 
				{ 
					try 
					{ 
						_rigidBody.Set("gravity_scale", _initialGravityScale); 
					} 
					catch { } 
				}
				
				// 冻结 RigidBody2D（可选，如果需要完全停止）
				try 
				{ 
					_rigidBody.Set("freeze", true); 
				} 
				catch { }
				
				// 禁用伤害检测并恢复原始碰撞设置
				_impactArmed = false;
				_refreezePending = false;
				_isDropping = false;
				RestoreRigidBodyCollision();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"{Name}: 停止移动时出错: {ex.Message}");
			}
		}

		/// <summary>
		/// 应用投掷时的碰撞设置
		/// </summary>
		private void ApplyThrowCollisionSettings()
		{
			if (_rigidBody == null)
			{
				return;
			}

			try
			{
				// 先读取当前值
				var beforeLayer = _rigidBody.CollisionLayer;
				var beforeMask = _rigidBody.CollisionMask;
				
				// 如果 ThrowCollisionLayer 为 0，确保 mask 也只检测需要的层（避免与静止物件碰撞）
				// 如果用户想要检测第3层（墙/地面），mask 应该设置为 4（1u<<2）
				uint finalLayer = ThrowCollisionLayer;
				uint finalMask = ThrowCollisionMask;
				
				// 如果 layer=0 但 mask 不为0，说明用户想要穿透其他物体但检测特定层（如墙）
				// 这是正确的配置，直接使用
				
				// 强制设置碰撞层和遮罩
				_rigidBody.CollisionLayer = finalLayer;
				_rigidBody.CollisionMask = finalMask;
				
				// 强制唤醒 RigidBody 以确保设置生效
				_rigidBody.Sleeping = false;
				
				// 立即验证设置是否成功
				var actualLayer = _rigidBody.CollisionLayer;
				var actualMask = _rigidBody.CollisionMask;
				
				GD.Print($"[{Name}] 投掷时碰撞设置: 设置前 layer={beforeLayer}, mask={beforeMask} | 设置后 layer={finalLayer}->{actualLayer}, mask={finalMask}->{actualMask} | 原始: layer={_initialRigidBodyCollisionLayer}, mask={_initialRigidBodyCollisionMask}");
				
				// 如果设置失败，输出警告并重试
				if (actualLayer != finalLayer || actualMask != finalMask)
				{
					GD.PushWarning($"[{Name}] ⚠️ 碰撞设置未生效！期望: layer={finalLayer}, mask={finalMask} | 实际: layer={actualLayer}, mask={actualMask}");
					// 强制重试
					_rigidBody.CollisionLayer = finalLayer;
					_rigidBody.CollisionMask = finalMask;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"{Name}: 无法设置投掷时的碰撞设置: {ex.Message}");
			}
		}

		/// <summary>
		/// 延迟应用投掷时的碰撞设置（用于确保在物理更新前设置）
		/// </summary>
		private void ApplyThrowCollisionSettingsDeferred()
		{
			if (_rigidBody == null || !_isThrown)
			{
				return;
			}

			try
			{
				_rigidBody.CollisionLayer = ThrowCollisionLayer;
				_rigidBody.CollisionMask = ThrowCollisionMask;
				_rigidBody.Sleeping = false; // 强制唤醒
				
				var actualLayer = _rigidBody.CollisionLayer;
				var actualMask = _rigidBody.CollisionMask;
				GD.Print($"[{Name}] CallDeferred 应用碰撞设置: layer={ThrowCollisionLayer}->{actualLayer}, mask={ThrowCollisionMask}->{actualMask}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"{Name}: CallDeferred 无法设置投掷时的碰撞设置: {ex.Message}");
			}
		}

		/// <summary>
		/// 恢复 RigidBody2D 的原始碰撞设置
		/// </summary>
		private void RestoreRigidBodyCollision()
		{
			if (_rigidBody == null || !_isThrown)
			{
				return;
			}

			try
			{
				_rigidBody.CollisionLayer = _initialRigidBodyCollisionLayer;
				_rigidBody.CollisionMask = _initialRigidBodyCollisionMask;
				_isThrown = false;
				GD.Print($"[{Name}] 恢复碰撞设置: layer={_initialRigidBodyCollisionLayer}, mask={_initialRigidBodyCollisionMask}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"{Name}: 无法恢复 RigidBody2D 的碰撞设置: {ex.Message}");
			}
		}

		/// <summary>
		/// 将此物品归还背包（投掷武器落地后归还，仍有剩余使用次数时调用）
		/// </summary>
		private void ReturnToInventory()
		{
			if (!(LastDroppedBy is SamplePlayer player) || player.InventoryComponent == null)
				return;

			var item = ItemDefinition ?? CurrentStack?.Item;
			if (item == null) return;

			var buildController = player.FindChild("BuildController", recursive: true, owned: false) as PlayerBuildController;

			// 先把物品放回背包/快捷栏，再注销飞行列表。
			// 顺序关键：若先 UnregisterThrowInFlight，RefreshBuildState 会在物品尚未入背包时移除构筑效果，
			// 随后 InventoryChanged 又重新添加效果，导致冷却计时器归零。
			// 先放回背包后，InventoryChanged 触发时效果仍存在（由飞行列表保障），
			// SyncBuildEffects 检测到 existing != null 直接 return，冷却不重置；
			// 紧接着 UnregisterThrowInFlight 再次 RefreshBuildState，此时物品已在背包，效果继续保留。

			// Step 1：优先归还到投掷前记录的槽位（该槽位已被 empty_item 占位）
			bool returnedToSlot = false;
			if (_reservedQuickBarSlotIndex >= 0 && player.InventoryComponent.QuickBar != null)
			{
				int added = player.InventoryComponent.QuickBar.TryAddItemToSlot(item, 1, _reservedQuickBarSlotIndex);
				returnedToSlot = added > 0;
			}

			// 若原槽已被其他物品占用，则走智能放入逻辑
			if (!returnedToSlot)
			{
				player.InventoryComponent.AddItemSmart(item, 1, showPopupIfFirstTime: false);
			}

			// 释放预占
			if (_reservedQuickBarSlotIndex >= 0)
			{
				player.InventoryComponent.ReservedQuickBarSlots.Remove(_reservedQuickBarSlotIndex);
				_reservedQuickBarSlotIndex = -1;
			}

			// Step 2：物品已回到背包后再注销飞行列表，此时 RefreshBuildState 能正确计入背包中的武器
			buildController?.UnregisterThrowInFlight(item);

			SyncPlayerHandAndQuickBar(player);
		}

		/// <summary>
		/// 飞行中击中敌人时销毁自身并生成特效
		/// </summary>
		private void DestroyItemOnImpact()
		{
			if (_isDestroying)
			{
				return;
			}

			_isDestroying = true;
			_inFlight = false;
			_landingHideTimer = 0.0;
			_inventoryReturnTimer = 0.0; // 飞行中命中：取消归还计划

			// 飞行中命中销毁：释放预占槽位（道具不归还），同时注销构筑效果保留
			if (LastDroppedBy is SamplePlayer impactSPlayer)
			{
				if (_reservedQuickBarSlotIndex >= 0 && impactSPlayer.InventoryComponent != null)
				{
					impactSPlayer.InventoryComponent.ReservedQuickBarSlots.Remove(_reservedQuickBarSlotIndex);
					_reservedQuickBarSlotIndex = -1;
				}
				var impactBuildController = impactSPlayer.FindChild("BuildController", recursive: true, owned: false) as PlayerBuildController;
				var destroyedItem = ItemDefinition ?? CurrentStack?.Item;
				if (impactBuildController != null && destroyedItem != null)
				{
					impactBuildController.UnregisterThrowInFlight(destroyedItem);
				}
			}

			// 禁用碰撞和伤害检测
			_impactArmed = false;
			if (_hitboxArea != null)
			{
				_hitboxArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}
			if (_grabArea != null)
			{
				DisableGrabArea();
			}

			// 生成特效（如果有）
			SpawnDestructionEffect();

			// 播放销毁动画
			PlayDestructionAnimation();
		}

		/// <summary>
		/// 在落点处销毁自身并生成特效
		/// </summary>
		private void DestroyItemAtLanding()
		{
			if (_isDestroying)
			{
				return;
			}

			_isDestroying = true;
			_landingHideTimer = 0.0;

			// 禁用碰撞和伤害检测
			_impactArmed = false;
			if (_hitboxArea != null)
			{
				_hitboxArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}
			if (_grabArea != null)
			{
				DisableGrabArea();
			}

			// 生成特效（如果有）
			SpawnDestructionEffect();

			// 播放销毁动画
			PlayDestructionAnimation();
		}

		/// <summary>
		/// 投掷武器落地隐藏（不销毁节点，等待 ThrowWeaponCooldown 到期后归还背包并 QueueFree）
		/// </summary>
		private void HideItemAtLanding()
		{
			// 隐藏视觉表现
			if (_rigidBody != null)
			{
				_rigidBody.Visible = false;
			}
			Visible = false;

			// 禁用碰撞和伤害检测
			_impactArmed = false;
			if (_hitboxArea != null)
			{
				_hitboxArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}
			if (_grabArea != null)
			{
				DisableGrabArea();
			}

			// 生成落点特效（如果有配置）
			SpawnDestructionEffect();
		}

		/// <summary>
		/// 生成销毁特效
		/// </summary>
		private void SpawnDestructionEffect()
		{
			// 如果配置了销毁特效Scene，则实例化它
			if (!string.IsNullOrWhiteSpace(DestructionEffectScene))
			{
				try
				{
					var scene = GD.Load<PackedScene>(DestructionEffectScene);
					if (scene != null)
					{
						var effect = scene.Instantiate();
						if (effect is Node2D effect2D)
						{
							// 使用 RigidBody2D 的实际坐标：飞行期间只有 _rigidBody 随武器移动，
							// 脚本所在的 Node2D 根节点停留在初始生成位置（玩家附近），直接用 GlobalPosition 会导致特效生成在玩家处
							GetParent()?.AddChild(effect2D);
							effect2D.GlobalPosition = _rigidBody?.GlobalPosition ?? GlobalPosition;
						}
						else if (effect != null)
						{
							GetParent()?.AddChild(effect);
						}
					}
				}
				catch (Exception ex)
				{
					GD.PushWarning($"[{Name}] 无法生成销毁特效: {ex.Message}");
				}
			}
		}

		private void DisableGrabArea()
		{
			if (_grabArea == null) return;

			// 使用 SetDeferred 避免在信号回调期间修改监控状态
			_grabArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			_grabArea.SetDeferred(Area2D.PropertyName.Monitorable, false);
			_grabArea.CollisionLayer = 0;
			_grabArea.CollisionMask = 0;
		}

		private void DestroyItem()
		{
			if (_isDestroying)
			{
				return; // 已经在销毁中
			}

			_isDestroying = true;

			// 禁用伤害检测和碰撞
			_impactArmed = false;
			if (_hitboxArea != null)
			{
				// 使用 SetDeferred 避免在信号回调期间修改监控状态
				_hitboxArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}
			if (_grabArea != null)
			{
				DisableGrabArea();
			}

			// 停止移动并恢复碰撞设置
			if (_rigidBody != null)
			{
				try
				{
					_rigidBody.LinearVelocity = Vector2.Zero;
					_rigidBody.Set("freeze", true);
				}
				catch { }
				RestoreRigidBodyCollision();
			}

			// 播放销毁动画
			PlayDestructionAnimation();
		}

		/// <summary>
		/// 播放销毁动画（预留功能）
		/// </summary>
		private void PlayDestructionAnimation()
		{
			AnimationPlayer? animPlayer = null;

			// 尝试获取动画播放器
			if (!DestructionAnimationPlayerPath.IsEmpty)
			{
				animPlayer = GetNodeOrNull<AnimationPlayer>(DestructionAnimationPlayerPath);
			}
			else
			{
				// 尝试在 RigidBody2D 下查找
				if (_rigidBody != null)
				{
					animPlayer = _rigidBody.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
				}
				// 尝试在当前节点下查找
				if (animPlayer == null)
				{
					animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
				}
			}

			if (animPlayer != null && animPlayer.HasAnimation(DestructionAnimationName))
			{
				// 播放指定的销毁动画
				animPlayer.Play(DestructionAnimationName);
				var animation = animPlayer.GetAnimation(DestructionAnimationName);
				if (animation != null)
				{
					var duration = animation.Length;
					_destructionAnimPlayer = animPlayer;
					
					// 连接动画播放完成的信号
					animPlayer.AnimationFinished += _OnDestructionAnimationFinished;
					
					// 备用：使用 Timer 确保动画播放完成后销毁
					GetTree().CreateTimer(duration).Timeout += () =>
					{
						if (IsInstanceValid(this))
						{
							_OnDestructionAnimationFinished(new StringName(""));
						}
					};
					return;
				}
			}

			// 如果没有找到动画播放器或动画，使用固定时长
			GetTree().CreateTimer(DestructionAnimationDuration).Timeout += () =>
			{
				if (IsInstanceValid(this))
				{
					_OnDestructionAnimationFinished(new StringName(""));
				}
			};
		}

		/// <summary>
		/// 销毁动画播放完成后的回调
		/// </summary>
		private void _OnDestructionAnimationFinished(StringName animName)
		{
			// 只处理销毁动画
			if (!string.IsNullOrEmpty(DestructionAnimationName) && !animName.IsEmpty && animName != DestructionAnimationName)
			{
				return;
			}

			// 断开信号连接
			if (_destructionAnimPlayer != null && IsInstanceValid(_destructionAnimPlayer))
			{
				_destructionAnimPlayer.AnimationFinished -= _OnDestructionAnimationFinished;
				_destructionAnimPlayer = null;
			}

			QueueFree();
		}

		private void RestoreGrabArea()
		{
			if (_grabArea == null) return;

			// 使用 SetDeferred 避免在信号回调期间修改监控状态
			_grabArea.SetDeferred(Area2D.PropertyName.Monitoring, _initialMonitoring);
			_grabArea.SetDeferred(Area2D.PropertyName.Monitorable, _initialMonitorable);
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
				return false;
			}

			if (accepted < stack.Quantity)
			{
				stack.Remove(accepted);
				Quantity = stack.Quantity;
				_lastTransferredItem = stack.Item;
				_lastTransferredAmount = accepted;
				// 使用 CallDeferred 避免在信号回调期间修改监控状态
				CallDeferred(MethodName.RestoreGrabArea);
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
