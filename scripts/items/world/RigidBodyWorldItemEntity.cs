using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Actors.Heroes.Attacks;
using Kuros.Core;
using Kuros.Effects;
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
	public partial class RigidBodyWorldItemEntity : Node2D, IWorldItemEntity, Kuros.Environments.IPlatformPassenger, Kuros.Environments.IGroundFallReceiver
	{
		/// <summary>世界物走 Destroy 链销毁完成(被打碎/外部 RequestDestroy;OnThrowDestroy 特效已生成)。
		/// 供 BuildThrow_A_004 等订阅并按来源组过滤;静态事件,订阅方 OnRemoved 需退订。</summary>
		/// <summary>投掷核心"件"的组/来源标记:生成时入组;拾取→放置恢复在场身份用同一值。</summary>
		public const string ThrowCorePieceTag = "throwcore_generated_furniture";
		/// <summary>A_007 复制件身份组:生成时入组;拾取→重新生成时据此恢复乱码滤镜。</summary>
		public const string ThrowCoreCopyTag = "throwcore_copy_furniture";
		/// <summary>件"身份"组(摧毁爆炸依据):直接生成/放置/投掷的件都入组,
		/// 与脉冲组分离——投掷件不恢复脉冲,但销毁时仍算"件"触发 A_004。</summary>
		public const string ThrowCorePieceIdentityTag = "throwcore_piece_identity";

		public static event Action<RigidBodyWorldItemEntity>? Destroyed;

		[Signal] public delegate void ItemTransferredEventHandler(RigidBodyWorldItemEntity entity, GameActor actor, ItemDefinition item, int amount);
		[Signal] public delegate void ItemTransferFailedEventHandler(RigidBodyWorldItemEntity entity, GameActor actor);

		[ExportGroup("Item")]
		[Export] public ItemDefinition? ItemDefinition { get; set; }
		[Export(PropertyHint.File, "*.tres,*.res")] public string ItemDefinitionResourcePath { get; set; } = string.Empty;
		[Export] public string ItemIdOverride { get; set; } = string.Empty;
		[Export(PropertyHint.Range, "1,9999,1")] public int Quantity { get; set; } = 1;

		/// <summary>构筑修饰(投掷者/放置者在生成后设置;_Ready 之后设置会按修饰档重解析血量——满血)。</summary>
		public ThrowableModifiers Modifiers
		{
			get => _modifiers;
			set
			{
				_modifiers = value;
				if (_readyDone)
					CurrentHP = ResolveMaxHP();
			}
		}

		private ThrowableModifiers _modifiers;
		private bool _readyDone;

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
			/// <summary>击退位移距离（像素）：0=未配置 → 家具档位值；攻击方控制——受击方 Hit 在 KnockbackDuration 内匀减速滑完（双时间轴 K 轴）。</summary>
		[Export(PropertyHint.Range, "0,1000,5")] public float KnockbackDistance { get; set; } = 0f;
		/// <summary>击退位移时长（秒）：0=未配置 → 家具档位值；攻击方控制，与受击方 HitImpactDuration 解耦。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float KnockbackDuration { get; set; } = 0f;
		[Export] public bool StopOnHit { get; set; } = true; // 命中敌人后是否停止（false = 穿过敌人）

		[ExportGroup("Physics Mode")]
		/// <summary>停止后是否回弹（与 StopOnHit 解耦：false = 停止后直接走 LandingHideDelay 销毁流程）。</summary>
		[Export] public bool BounceAfterStop { get; set; } = true;
		/// <summary>停止后回弹的向上初速度（像素/秒）。0=未配置 → 家具档位值（越重越不弹）；无档回退 650。</summary>
		[Export] public float BounceSpeed { get; set; } = 0f;
		/// <summary>回弹水平分量 = 飞行水平速度 × 此比例（大小相同，方向分两种情况）：
		/// 撞击停止（撞墙/撞人）→ 投掷**反方向**弹开；B_006 干净落点 → 顺势**向前**滚。
		/// 0=未配置 → 家具档位值（越重弹得越少）；无档回退 0.2。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float BounceHorizontalRatio { get; set; } = 0f;
		/// <summary>落地反弹弹性（判定层边界反弹衰减系数，0 = 落地即停，1 = 完全弹性）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float BounceElasticity { get; set; } = 0.0f;
		/// <summary>回弹下落的重力倍率（脚本模拟，仅用于回弹上升/回落）。</summary>
		[Export] public float PhysicsModeGravityScale { get; set; } = 8.0f;
		/// <summary>回弹水平速度的衰减系数（指数衰减 ≈ 物理阻尼，越大弹开后越快停住）。</summary>
		[Export] public float PhysicsModeLinearDamp { get; set; } = 8.0f;

		[ExportGroup("Durability")]
		/// <summary>
		/// 投掷后是否回收（冷却归还背包）。false=投掷后落地销毁。
		/// 从 ItemDefinition.IsThrowWeapon 读取。
		/// </summary>
		private bool IsThrowWeapon => ItemDefinition?.IsThrowWeapon ?? false;
		/// <summary>
		/// 投掷武器冷却时间（秒）。仅在 IsThrowWeapon=true 时生效。
		/// 从 ItemDefinition.ThrowWeaponCooldown 读取，null 时回退默认 2.0s。
		/// </summary>
		private float ThrowWeaponCooldown => ItemDefinition?.ThrowWeaponCooldown ?? 2.0f;
		[Export] public NodePath DestructionAnimationPlayerPath { get; set; } = new NodePath(""); // 销毁动画播放器路径
		[Export] public string DestructionAnimationName { get; set; } = "destroy"; // 销毁动画名称
		[Export] public float DestructionAnimationDuration { get; set; } = 0.5f; // 销毁动画时长（如果动画播放器不存在，使用固定时长）
		[Export(PropertyHint.Range, "0.01,10,0.01")] public float LandingHideDelay { get; set; } = 2.0f; // 落点处隐藏延迟（秒）：投掷武器落地后隐藏视觉的等待时间；到达 ThrowWeaponCooldown 后才归还背包并销毁节点
		public int ThrowHoldFrame { get; set; } = -1;

		/// <summary>落地不销毁（BuildThrow_B_006 对象撤回）：由卡在出手瞬间置入——一次性道具到达落点后
		/// 不进入销毁流程，而是收尾成普通世界物（同步根位置、恢复碰撞），玩家可走过去正常拾取。
		/// 投掷武器不适用（本就 2s 自动归还）；A_008 分裂件不置入——只认本次投掷的原件，
		/// 否则一次投掷裂 3 枚、捡回 3 件 = 白给道具。</summary>
		public bool KeepAfterLanding { get; set; } = false;

		/// <summary>投掷耐久上限（B_006 置入；0 = 不限）：与 <see cref="ThrowCountUsed"/> 比较，
		/// 达到上限的这一次投掷落地即正常销毁（即"同一件最多投出 N 次"）。</summary>
		public int KeepThrowLimit { get; set; } = 0;

		/// <summary>本件已投掷次数（跨"拾取→放置→投掷"由栈携带：出手 +1 带到实体、拾取/保留时带回栈）。</summary>
		public int ThrowCountUsed { get; set; } = 0;

		[ExportCategory("Health")]
		[Export] public bool Destructible { get; set; } = false;
		/// <summary>最大血量：0=未配置 → 家具档位值（无档回退 60）。</summary>
		[Export(PropertyHint.Range, "0,9999,1")] public float MaxHP = 0f;
		[Export(PropertyHint.Range, "0.1,5,0.05")] public float DamageCooldown = 0.2f;
		public float CurrentHP { get; private set; }

		[ExportCategory("Hit Flash")]
		[Export] public NodePath HitFlashSpritePath { get; set; } = new();
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float HitFlashDuration = 0.15f;
		[Export(PropertyHint.Range, "1,120,1")] public float HitFlashSpeed = 30f;
		[Export(PropertyHint.Range, "0,500,0.1")] public float HitShakeIntensity = 100f;
		[Export] public Color HitFlashColor = new Color(1f, 1f, 1f, 1f);

		[ExportGroup("Physics")]
		[Export] public NodePath RigidBodyPath { get; set; } = new NodePath(".");
		[Export] public NodePath HitboxAreaPath { get; set; } = new NodePath("Rigidbody2D/Hitbox");
		/// <summary>阴影节点路径（投掷飞行期间影子做地面投影:随视觉 X 水平移动、按离地高度等比缩放）。</summary>
		[Export] public NodePath ShadowPath { get; set; } = new NodePath("Shadow");
		/// <summary>飞行中影子最小缩放比例（相对场景原尺寸;1=不缩放）。起步/最高点约取该值，落地还原为 1。</summary>
		[Export(PropertyHint.Range, "0.1,1,0.05")] public float ShadowMinScaleFactor { get; set; } = 0.5f;
		[Export] public uint ThrowCollisionLayer { get; set; } = 1u << 2;
		[Export] public uint ThrowCollisionMask { get; set; } = 0;

		[ExportGroup("Debug")]
		/// <summary>调试：砸中敌人时在屏幕上绘制贯穿扫描范围（ZIndex 10，临时可视化）。</summary>
		[Export] public bool ShowThrowPenetrationDebug { get; set; } = false;


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
		/// <summary>WorldItem 物理层（layer 4）：伤害效果把 TargetCollisionMask 配成该层即可
		/// 通过物理查询发现家具的可破坏 StaticBody2D；Environment 位保留，移动碰撞不变。</summary>
		private const uint WorldItemDamageLayer = 1u << 3;
		private float _initialGravityScale = 0.0f;
		private bool _inFlight = false;
		private double _flightTimer = 0.0;
		private float _throwStartY = 0f; // 投掷起始的Y坐标
		private float _throwJudgmentY = 0f; // 判定层的Y坐标（投掷者地面层——玩家/敌人站立的世界Y，非场景原点 0）
		private float _bounceGroundY = 0f;  // 回弹模拟的地面行 = 本件自己的落点行（_throwJudgmentY + LandingOffsetYDelta，A_008 分裂件各行不同）
		private float _throwHorizontalVelocity = 0f; // 投掷的水平速度
		private bool _bouncing = false; // 停止后脚本回弹中（重力积分 + 判定层反弹）
		private float _bounceVelX = 0f; // 回弹水平速度（指数衰减）
		private float _bounceVelY = 0f; // 回弹垂直速度（重力积分）
		private const float BounceGravity = 980f; // 模拟重力加速度（Godot 默认值）
		private bool _isDropping = false;
		private float _dropStartY = 0f;
		private bool _initialMonitoring;
		private bool _initialMonitorable;
		private uint _initialCollisionLayer;
		/// <summary>场景原始 z_index：投掷期会被覆写为 ItemDefinition.ThrowZIndex（默认 3，飞行途中在最上层），
		/// 落地保留（B_006）时须还原——否则保留件会永远盖在所有家具（场景值 2）之上，图层与放置件不一致。</summary>
		private int _initialZIndex;
		private uint _initialCollisionMask;
		private uint _initialRigidBodyCollisionLayer; // RigidBody2D 的原始碰撞层
		private uint _initialRigidBodyCollisionMask; // RigidBody2D 的原始碰撞遮罩
		private readonly System.Collections.Generic.HashSet<GameActor> _actorsInRange = new();
		private bool _impactArmed = false; // 是否已激活伤害检测
		private bool _hasDealtDamage = false; // 是否已造成伤害
		private readonly System.Collections.Generic.HashSet<Node> _hitActors = new(); // 已命中的目标（GameActor/非 GameActor），防止重复伤害
		private Area2D? _hitboxArea; // 用于伤害检测的 Area2D
		private bool _isDestroying = false; // 是否正在销毁中
		private AnimationPlayer? _destructionAnimPlayer; // 销毁动画播放器引用
		private bool _isThrown = false; // 是否正在投掷中
		public bool IsDisposableCopy { get; set; }
		public float ThrowCooldownRemaining { get; set; }
		/// <summary>投掷落点纵向偏移(px,发射后可改;抛物线每帧重算落点 → A_008 分裂散射把各枚落点沿 Y 错开)。</summary>
		public float LandingOffsetYDelta { get; set; }
		private Sprite2D? _highlightSprite; // Outline highlight 精灵
		private ShaderMaterial? _outlineMaterial; // Outline 着色器材料
		private Area2D? _cachedPlayerGrabArea; // 缓存的玩家 GrabArea
		private bool _isOutlineHighlighted; // 是否正在高亮显示
		private Node2D? _shadowNode; // 阴影节点缓存
		private Vector2 _shadowBasePosition;    // 阴影本地变换(飞行结束还原;飞行中影子被覆写为地面投影)
		private Vector2 _shadowBaseScale = Vector2.One;
		private float _shadowHeightRef = 1f;    // 飞行起步时的视觉离地高度(影子缩放归一参考)
		private bool _flightProjectionActive;   // 影子/判定盒处于飞行地面投影中(结束帧统一归位)
		private Vector2 _hitboxBasePosition;    // 判定盒本地位置(飞行结束还原)
		private double _throwCooldownTimer = 0.0; // 投掷武器冷却计时器
		private bool _isInCooldown = false; // 是否在冷却中
		private double _landingHideTimer = 0.0; // 落点隐藏计时器（LandingHideDelay：到期后隐藏视觉，不销毁节点）
		private double _inventoryReturnTimer = 0.0; // 落地后归还背包的计时器（由 ThrowWeaponCooldown 决定，到期后真正销毁节点）
		private int _reservedQuickBarSlotIndex = -1; // 投掷武器飞行期间预留的快捷栏槽位索引

		// Hit flash
		private Sprite2D? _hitFlashSprite;
		private ShaderMaterial? _hitFlashMaterial;
		private Material? _hitFlashOriginalMaterial;
		private float _hitFlashTimer;
		private bool _hitFlashActive;

		private float _damageCooldownRemaining;
		private AttackHitboxDebugDrawer? _penetrationDebugDrawer;

		public GameActor? LastDroppedBy { get; set; }

		/// <summary>
		/// 当前被高亮的实体（由 PlayerItemInteractionComponent 每帧设置，避免 O(N²) 遍历）
		/// </summary>
		internal static RigidBodyWorldItemEntity? CurrentHighlightedEntity { get; set; }
		
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

		/// <summary>单次飞行参数重载（&gt;0 时替代物品定义的投掷距离/时长）——效果卡等单次投掷用,普通投掷为 0。</summary>
		public float OverrideThrowDistance { get; set; }
		public float OverrideThrowDuration { get; set; }

		/// <summary>
		/// 从 ItemDefinition 读取投掷参数（档位解析见 ItemDefinition.GetEffectiveThrow*，随 Modifiers 修饰），
		/// ItemDefinition 为 null 时回退到内置默认值。OverrideThrow* &gt;0 时优先（单次重载）。
		/// </summary>
		private double GetEffectiveThrowParabolicDuration()
			=> OverrideThrowDuration > 0f ? OverrideThrowDuration
				: (ItemDefinition != null ? ItemDefinition.GetEffectiveThrowDuration(Modifiers) : 0.6);

		private float GetEffectiveThrowParabolicPeakHeight()
			=> ItemDefinition?.ThrowParabolicPeakHeight is > 0 ? ItemDefinition.ThrowParabolicPeakHeight : 200f;

		private float GetEffectiveThrowHorizontalDistance()
			=> OverrideThrowDistance > 0f ? OverrideThrowDistance
				: (ItemDefinition != null ? ItemDefinition.GetEffectiveThrowDistance(Modifiers) : 600f);

		private float GetEffectiveThrowParabolicLandingYOffset()
			=> ItemDefinition?.ThrowParabolicLandingYOffset ?? 100f;

		private Vector2 GetEffectiveThrowStartOffset()
			=> (ItemDefinition != null && ItemDefinition.ThrowStartOffset != Vector2.Zero)
				? ItemDefinition.ThrowStartOffset
				: new Vector2(0, -200);

		/// <summary>回弹初速度:场景 export &gt;0 逐件特化覆盖;0 = 家具档位值(越重越不弹);无档(武器)回退 650。</summary>
		private float GetEffectiveBounceSpeed()
			=> BounceSpeed > 0f
				? BounceSpeed
				: ItemDefinition?.GetResolvedTierSpec(Modifiers, 0)?.BounceSpeed ?? 650f;

		/// <summary>回弹水平分量比例:场景 export &gt;0 逐件特化覆盖;0 = 家具档位值(越重向后弹得越少);无档(武器)回退 0.2。</summary>
		private float GetEffectiveBounceHorizontalRatio()
			=> BounceHorizontalRatio > 0f
				? BounceHorizontalRatio
				: ItemDefinition?.GetResolvedTierSpec(Modifiers, 0)?.BounceHorizontalRatio ?? 0.2f;
		
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

		/// <summary>未拾取存活时长（秒，0=禁用）：静止后开始计时，到期前闪烁预警并消失（方案 B）。</summary>
		[Export(PropertyHint.Range, "0,300,1")] public float UnpickedLifetime { get; set; } = 0f;

		/// <summary>过期组件是否允许计时：静止（非投掷/回弹/隐藏中且可见）。</summary>
		public bool IsSettledForExpiry =>
			!_isThrown && !_inFlight && !_bouncing && !_isDestroying
			&& _landingHideTimer <= 0.0 && _inventoryReturnTimer <= 0.0 && Visible;

		private Kuros.Fx.WorldItemExpiry? _expiry;

		public override void _Ready()
		{
			base._Ready();

			_initialZIndex = ZIndex; // 投掷期会被 ThrowZIndex 覆写,落地保留时还原

			// 添加到组，方便通过场景树查找
			if (!IsInGroup("world_items"))
			{
				AddToGroup("world_items");
			}
			if (!IsInGroup("pickables"))
			{
				AddToGroup("pickables");
			}

			// 未拾取过期（方案 B）：本体在世界中（敌人掉落/玩家放置 place）时生效——
			// 场景 export 优先，否则读 ItemDefinition 定义级配置（0 = 禁用）。
			// 投掷（throw）生成的副本（IsDisposableCopy）排除——走自身特效自毁路径。
			if (!IsDisposableCopy)
			{
				float lifetime = UnpickedLifetime > 0f
					? UnpickedLifetime
					: ItemDefinition?.UnpickedLifetime ?? 0f;
				if (lifetime > 0f)
				{
					_expiry = new Kuros.Fx.WorldItemExpiry { UnpickedLifetime = lifetime };
					_expiry.Name = "WorldItemExpiry";
					AddChild(_expiry);
				}
			}
			
			InitializeStack();
			ResolveRigidBody();
			ResolveGrabArea();
			ResolveHitboxArea();
			UpdateSprite();
			ResolveOutlineHighlight();
			ResolveShadowNode();
			UpdateOutlineHighlight(force: true);

			CurrentHP = ResolveMaxHP();
			_readyDone = true;
			SetupHitFlash();

			SetProcess(true);

			// 如果该物品包含导航源几何子节点，进入场景树后延迟烘焙，使障碍区域生效
			if (NavigationRebakeCoordinator.HasNavigationSourceGeometry(this))
			{
				NavigationRebakeCoordinator.RequestRebake(this);
			}

			// 预热 OnThrowDestroy 效果场景的 Shader：
			// Godot 4 首次实例化含 ParticleProcessMaterial 的场景时会同步编译 Shader（卡帧）。
			// 在 _Ready 阶段提前实例化一次（invisible，立刻销毁），使 Shader 在此时完成编译。
			WarmUpThrowDestroyEffectShaders();
		}

		public override void _ExitTree()
		{
			base._ExitTree();

			// 如果该物品有子节点属于导航源几何组，移除后触发延迟重新烘焙导航网格
			if (NavigationRebakeCoordinator.HasNavigationSourceGeometry(this))
			{
				NavigationRebakeCoordinator.RequestRebake(this);
			}

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
				var hitboxEntered = new Callable(this, MethodName.OnHitboxAreaEntered);
				if (_hitboxArea.IsConnected(Area2D.SignalName.AreaEntered, hitboxEntered))
				{
					_hitboxArea.AreaEntered -= OnHitboxAreaEntered;
				}
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			UpdateOutlineHighlight();

			if (_hitFlashActive)
			{
				_hitFlashTimer -= (float)delta;
				if (_hitFlashTimer <= 0f)
					EndHitFlash();
				else
					_hitFlashMaterial?.SetShaderParameter("hit_effect", _hitFlashTimer / HitFlashDuration);
			}

			if (_damageCooldownRemaining > 0f)
				_damageCooldownRemaining -= (float)delta;

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

		private void ResolveShadowNode()
		{
			if (!ShadowPath.IsEmpty)
			{
				_shadowNode = GetNodeOrNull<Node2D>(ShadowPath);
			}
			// 若路径未找到，尝试常见名称
			if (_shadowNode == null)
			{
				_shadowNode = GetNodeOrNull<Node2D>("Shadow")
					?? _rigidBody?.GetNodeOrNull<Node2D>("Shadow");
			}

			// 缓存本地变换:飞行中影子被覆写为地面投影,结束帧按此还原
			if (_shadowNode != null)
			{
				_shadowBasePosition = _shadowNode.Position;
				_shadowBaseScale = _shadowNode.Scale;
			}
		}

		private void SetShadowVisible(bool visible)
		{
			if (_shadowNode != null && GodotObject.IsInstanceValid(_shadowNode))
				_shadowNode.Visible = visible;
		}

		/// <summary>飞行中影子:地面投影(与判定盒同一手法——X 随视觉、Y 取投掷地面行),
		/// 并按"视觉离地高度"等比缩放:越高越小、落地还原场景原尺寸(伪 3D)。</summary>
		private void UpdateFlightShadow(float visualX, float groundY)
		{
			if (_shadowNode == null || !GodotObject.IsInstanceValid(_shadowNode)) return;

			_shadowNode.GlobalPosition = new Vector2(visualX, groundY);

			// 高度归一:参考 = 起步瞬间离地高度;落地 dist→0 → 1(原尺寸),起步/峰值 → 最小缩放
			float dist = Mathf.Abs(_rigidBody.GlobalPosition.Y - groundY);
			float t = Mathf.Clamp(dist / Mathf.Max(_shadowHeightRef, 1f), 0f, 1f);
			_shadowNode.Scale = _shadowBaseScale * Mathf.Lerp(ShadowMinScaleFactor, 1f, 1f - t);
		}

		/// <summary>飞行结束统一归位(覆盖落地/撞墙/命中停止/被接住等所有退出路径,
		/// 由 _PhysicsProcess 的"投影中且不在飞行"检查触发一次):影子与判定盒从地面投影还原为实体本地变换。</summary>
		private void RestoreFlightProjection()
		{
			_flightProjectionActive = false;

			if (_shadowNode != null && GodotObject.IsInstanceValid(_shadowNode))
			{
				_shadowNode.Position = _shadowBasePosition;
				_shadowNode.Scale = _shadowBaseScale;
			}
			if (_hitboxArea != null && GodotObject.IsInstanceValid(_hitboxArea))
				_hitboxArea.Position = _hitboxBasePosition;
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
		/// 检查该实例是否处于投掷生命周期中（飞行、回弹、落地隐藏、等待归还阶段均视为投掷中）。
		/// 同时是"对象撤回"（BuildThrow_B_006）的可撤回窗口 = 投出后、销毁/归还之前。
		/// </summary>
		internal bool IsInThrowLifecycle =>
			_isThrown || _inFlight || _bouncing || _landingHideTimer > 0.0 || _inventoryReturnTimer > 0.0;

		/// <summary>
		/// 当前是否可被拾取：投掷生命周期（飞行/回弹/落点停留/等待归还）中、已拾取、已进销毁流程 → false。
		/// 供拾取候选筛选（PlayerItemInteractionComponent 两条选取路径）——不过滤的话，
		/// 飞行/落点停留中的道具会抢占"最近可拾取"位置，TryPickupByActor 再拒绝 → 拾取静默失败（按了没反应）。
		/// 与 TryPickupByActor 的门控同源：候选筛选用这个，真正拾取仍由 TryPickupByActor 兜底。
		/// </summary>
		internal bool IsPickupAvailable => !_isPicked && !_isDestroying && !IsInThrowLifecycle;

		[ExportGroup("Platform Carry")]
		/// <summary>允许地面静置物品（家具或投掷武器）随平台移动；投掷、回弹、散落未停稳及拾取期间不承载。</summary>
		[Export] public bool PlatformCarryEnabled { get; set; } = true;
		/// <summary>可选脚底节点，相对于物品根节点；空时优先使用实际身体形状原点，无身体形状时使用内部刚体位置。</summary>
		[Export] public NodePath PlatformGroundProbePath { get; set; } = new();
		private PhysicsBody2D? PlatformCollisionBody
		{
			get
			{
				var staticBody = _rigidBody?.GetNodeOrNull<StaticBody2D>("StaticBody2D");
				if (staticBody != null && Kuros.Environments.PlatformCarryPhysics.HasEnabledBodyShape(staticBody)) return staticBody;
				return _rigidBody != null && Kuros.Environments.PlatformCarryPhysics.HasEnabledBodyShape(_rigidBody) ? _rigidBody : null;
			}
		}
		public Node2D PlatformTarget => this;
		public bool CanRidePlatform => PlatformCarryEnabled && IsPickupAvailable && !_isDropping && !_refreezePending
			&& IsVisibleInTree() && CanProcess() && !IsQueuedForDeletion()
			&& _rigidBody != null && _rigidBody.Freeze;

		public Vector2 PlatformGroundPosition => Kuros.Environments.PlatformCarryPhysics.GroundPosition(
			this, PlatformGroundProbePath, PlatformCollisionBody, _rigidBody);
		private bool _throwLanded;
		private bool _fallWaitingForReturn;
		/// <summary>排除飞行、回弹及空中散落；落地等待隐藏期间仍可坠落，不要求静止或开启平台承载。</summary>
		public bool CanFall => !_isPicked && !_isDestroying && !_inFlight && !_bouncing
			&& (IsPickupAvailable || _throwLanded) && !_isDropping && !_refreezePending
			&& _rigidBody != null && IsVisibleInTree() && CanProcess() && !IsQueuedForDeletion();
		public Vector2 FallGroundPosition => PlatformGroundPosition;
		public void PrepareForFall()
		{
			_fallWaitingForReturn = _throwLanded && !IsDisposableCopy && IsThrowWeapon;
			_landingHideTimer = 0;
			_isThrown = false;
			_isDestroying = true;
			_isPicked = true;
			_impactArmed = false;
			if (_rigidBody != null)
			{
				_rigidBody.LinearVelocity = Vector2.Zero;
				_rigidBody.AngularVelocity = 0;
				_rigidBody.Freeze = true;
			}
			// 不触发正常打碎/落地特效；已落地的归还型投掷武器独立保留归还倒计时。
		}
		public bool CompleteFall()
		{
			if (!_fallWaitingForReturn) return false;
			Visible = false;
			foreach (Node child in GetChildren()) Kuros.Environments.FallPhysics.DisableCollisions(child, stopProcessing: true);
			SetProcess(false);
			SetPhysicsProcess(true); // 根仅运行下面的归还专用分支，子树全部停用。
			return true;
		}

		private bool TryHandleLandingFall()
		{
			_throwLanded = true;
			if (_flightProjectionActive) RestoreFlightProjection();
			if (!IsDisposableCopy && IsThrowWeapon) _inventoryReturnTimer = ThrowWeaponCooldown;
			foreach (Node child in GetChildren())
				if (child is Kuros.Environments.GroundObjectController controller) return controller.TryFallNow();
			return false;
		}

		public void ApplyPlatformDisplacement(Vector2 displacement, uint obstacleMask)
		{
			if (!CanRidePlatform || displacement.IsZeroApprox()) return;
			Vector2 motion = displacement;
			if (PlatformCollisionBody is { } body)
			{
				var exclude = new Godot.Collections.Array<Rid>();
				Kuros.Environments.PlatformCarryPhysics.CollectBodyRids(this, exclude);
				motion = Kuros.Environments.PlatformCarryPhysics.ClipMotion(body, displacement, obstacleMask, exclude);
			}
			Vector2 rigidTarget = _rigidBody!.GlobalPosition + motion;
			GlobalPosition += motion;
			// 显式同步冻结的刚体，保留它与包装根节点原有偏移，防止物理同步覆盖父级移动。
			_rigidBody.GlobalPosition = rigidTarget;
			// 均为地面静置状态；不改线速度、冻结状态、投掷计时及局部影子/Hitbox 偏移。
		}

		/// <summary>
		/// 判断此实例是否有资格参与高亮候选（供 PlayerItemInteractionComponent 调用）
		/// </summary>
		internal bool IsHighlightCandidate =>
			EnableGrabAreaOutlineHighlight && !_isPicked && !IsInThrowLifecycle
			&& _grabArea != null && GodotObject.IsInstanceValid(_grabArea);

		/// <summary>
		/// 获取此实例的 GrabArea（供 PlayerItemInteractionComponent 进行距离判断）
		/// </summary>
		internal Area2D? GrabArea => _grabArea;

		private void UpdateOutlineHighlight(bool force = false)
		{
			ResolveOutlineHighlight();
			if (_outlineMaterial == null)
			{
				return;
			}

			// 由 PlayerItemInteractionComponent 每帧设置 CurrentHighlightedEntity，
			// 此处只需检查自己是否为当前高亮实体（O(1)）
			bool shouldHighlight = ReferenceEquals(this, CurrentHighlightedEntity);

			if (!force && _isOutlineHighlighted == shouldHighlight)
			{
				return;
			}

			_outlineMaterial.SetShaderParameter("outline_color", shouldHighlight ? HighlightOutlineColor : DefaultOutlineColor);
			_isOutlineHighlighted = shouldHighlight;
		}

		/// <inheritdoc/>
		public virtual void ApplyScatterImpulse(Vector2 velocity)
		{
			// 仅做物理弹出，不进入投掷状态机，不触发 OnThrowDestroy 效果
			if (_rigidBody == null) ResolveRigidBody();
			if (_rigidBody == null) return;
			try { _rigidBody.Set("freeze", false); } catch { }
			_rigidBody.Sleeping = false;
			// 添加线性阻尼确保物体能自然减速停止（gravity_scale=0 时无自然减速）
			_rigidBody.LinearDamp = 4.0f;
			_rigidBody.LinearVelocity = velocity;
			// 速度归零后自动重新冻结（由 _refreezePending 逻辑处理）
			_refreezePending = true;
			_refreezeTimer = 0.0;
		}

		public virtual void ApplyThrowImpulse(Vector2 velocity)
		{
			_throwLanded = false;
			// 投掷即效果：不进入飞行，投掷瞬间销毁武器本体并生成 OnThrowDestroy 效果（回旋镖等）
			if (ItemDefinition?.SpawnEffectOnThrow == true)
			{
				ImmediateEffectThrow();
				return;
			}

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
				// 投掷起点归到玩家原点（根节点可能带 ThrowOffset 偏移），视觉从玩家原点+视觉偏移抛出
				Vector2 origin = LastDroppedBy?.GlobalPosition ?? GlobalPosition;
				_rigidBody.GlobalPosition = origin + GetEffectiveThrowStartOffset();
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
			// 判定层：投掷者站立的地面世界 Y（敌人 HitArea 同层）——不是场景原点 0
			_throwJudgmentY = origin.Y;
			// 影子缩放归一参考 = 起步瞬间的视觉离地高度(峰值段超出 → 钳到最小缩放)
			_shadowHeightRef = Mathf.Max(Mathf.Abs(_throwStartY - _throwJudgmentY), 1f);

				// 激活伤害检测并应用投掷时的碰撞设置
				if (velocity.LengthSquared() > 0.01f)
				{
					_impactArmed = true;
					_hasDealtDamage = false;
					_hitActors.Clear();
					_isThrown = true;
					
						// 家具投掷时关闭 StaticBody2D 碰撞体，避免大碰撞体推开敌人导致 AttackArea 无法命中
						SetFurnitureStaticBodyCollision(false);
						// (影子不再于飞行期间隐藏:改为地面投影,见 UpdateFlightShadow)
				

					// 构筑效果已在 PlayerItemInteractionComponent.TryHandleDrop 中预注册，此处无需重复注册
					
					// 投掷武器：进入冷却状态，并预占原快捷栏槽位
					if (!IsDisposableCopy && IsThrowWeapon)
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
			if (_fallWaitingForReturn)
			{
				_inventoryReturnTimer = Math.Max(0, _inventoryReturnTimer - delta);
				if (_inventoryReturnTimer <= 0)
				{ _fallWaitingForReturn = false; ReturnToInventory(); QueueFree(); }
				return;
			}

			if (_rigidBody == null) return;

			if (ThrowCooldownRemaining > 0f)
				ThrowCooldownRemaining = Mathf.Max(0f, ThrowCooldownRemaining - (float)delta);

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
					if (!IsDisposableCopy && IsThrowWeapon)
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

			// 飞行中手动空间查询（与玩家攻击打可破坏物同机制）：Hitbox 是 RigidBody2D 子节点，
			// Area↔Body 重叠检测在快速手动位移下不可靠（bodies 恒 0）——直接查空间，必然命中。
			// 非 GameActor 接收者（FireWallA 等 StaticBody2D 可破坏物）→ 拦截/放行判定；
			// 敌人（GameActor）由 AreaEntered 信号路径处理（正常工作），此处跳过
			if (_impactArmed && _hitboxArea != null)
			{
				QueryNonActorTargets();
			}

			// 飞行结束统一归位(覆盖落地/撞墙/命中停止/被接住等所有退出路径):
			// 影子与判定盒从地面投影还原为实体本地变换;回弹段(_bouncing)继续投影,静止后再归位
			if (_flightProjectionActive && !_inFlight && !_bouncing)
				RestoreFlightProjection();

			// 抛物线飞行逻辑：平顺的参数化抛物线轨迹
			if (_inFlight)
			{
				_flightTimer += delta;
			double phase = _flightTimer / GetEffectiveThrowParabolicDuration();
			
			// 安全夹叶 phase 到 [0, 1] 范围
			if (phase > 1.0) phase = 1.0;
			
			// 计算目标落点Y坐标（优先使用 ItemDefinition 参数）
			float landingY = _throwStartY + GetEffectiveThrowParabolicLandingYOffset()
				+ LandingOffsetYDelta; // A_008 散射:每枚落点纵向错开(发射后改亦生效,平滑重定向)
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

			// 撞墙检测：手动物理射线（freeze 刚体子节点的 Area2D 信号不可靠），
			// 沿水平飞行方向检测前方墙体，命中非 GameActor 物理体则停止并销毁
			if (_impactArmed && StopOnHit && CheckWallHit(_rigidBody.GlobalPosition, _throwHorizontalVelocity))
			{
				_inFlight = false;
				_flightTimer = 0.0;
				_impactArmed = false;
				_rigidBody.LinearVelocity = Vector2.Zero;
				try { _rigidBody.Set("freeze", true); } catch { }
				RestoreRigidBodyCollision();
				HandleStopAfterFlight(); // 回弹或直接销毁（由 BounceAfterStop 决定）
				return;
			}

			_rigidBody.GlobalPosition = new Vector2(newX, newY);

			// 视觉/判定分离：判定 Hitbox 投影在投掷者地面层（_throwJudgmentY），X 每帧跟随视觉——
			// 判定与视觉抛物线水平完全同步（同起点、同速度、同距离），垂直分离到敌人 HitArea 所在层。
			// A_008 散射:判定行从玩家行(0)随 phase 渐变到该枚终点行(LandingOffsetYDelta),与视觉落点同进
			float groundRowY = _throwJudgmentY + LandingOffsetYDelta * (float)phase;
			_flightProjectionActive = true; // 标记投影中(含判定盒归位),飞行结束帧统一还原
			if (_hitboxArea != null)
			{
				_hitboxArea.GlobalPosition = new Vector2(_rigidBody.GlobalPosition.X, groundRowY);
			}

			// 影子地面投影(与判定盒同一手法):X 随视觉、Y 在投掷地面行;按离地高度等比缩放(伪 3D)
			UpdateFlightShadow(_rigidBody.GlobalPosition.X, groundRowY);

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
					// [临时诊断] A_008 分裂落点对质:实体实际落地 Y 与判定行关系
					if (LandingOffsetYDelta != 0f)
						GD.Print($"[A_008实体] {Name}: 落地Y={landingY:F0}, 判定行={_throwJudgmentY:F0}, delta={LandingOffsetYDelta}");
					if (TryHandleLandingFall()) return;

					// 落点砸地 AoE（仅一次性投掷物）：飞尽未中敌时按碰撞体积结算一次范围伤害，
					// 必须在关闭判定臂之前执行（命中过的敌人已在 _hitActors,不会重复结算）
					if (!IsThrowWeapon)
						DealDamageOnLanding();

					// 停止伤害检测并恢复原始碰撞设置
					_impactArmed = false;
					_rigidBody.LinearVelocity = Vector2.Zero;
					try { _rigidBody.Set("freeze", true); } catch { }
					RestoreRigidBodyCollision();
					
					// 落地时恢复阴影显示
					SetShadowVisible(true);
					
					// 落点收尾：B_006 对象撤回 → 不销毁、留在场上可拾取；否则走隐藏/销毁计时
					// 归还背包由 _inventoryReturnTimer（ThrowWeaponCooldown）独立控制
					if (!_isDestroying)
					{
						if (ShouldKeepAfterLanding)
						{
							// 完整抛物线到落点（途中没碰到任何东西）也补一次回弹物理模拟：
							// 原本只有"撞到敌人/墙停住"才回弹，干净落点是直接定格。
							// 向后上方弹开→滑行衰减到停→由回弹停止分支统一收尾（FinalizeKeptAfterLanding）。
							if (BounceAfterStop)
							{
								StartBounce(forwardHorizontal: true); // 干净落点：顺势向前滚/弹（撞击停止才是反方向弹开）
								return;
							}

							FinalizeKeptAfterLanding();
							return;
						}

						_landingHideTimer = LandingHideDelay;

						if (!IsDisposableCopy && IsThrowWeapon)
						{
							// 投掷武器：LandingHideDelay 后隐藏，ThrowWeaponCooldown 后归还背包
							_inventoryReturnTimer = ThrowWeaponCooldown;
						}
						// 非投掷武器：LandingHideDelay 后直接销毁（由 _PhysicsProcess 中 DestroyItemAtLanding 处理）
					}
					return;
				}
			}

			// 脚本回弹模拟（俯视角"地面"= 判定层）：重力积分下落，接触判定层反弹衰减，水平指数衰减
			if (_bouncing)
			{
				float dt = (float)delta;
				_bounceVelY += BounceGravity * PhysicsModeGravityScale * dt;
				_bounceVelX *= Mathf.Exp(-PhysicsModeLinearDamp * dt);

				// 撞 AirWall：水平弹回（复用飞行阶段射线检测）
				if (_bounceVelX != 0f && CheckWallHit(_rigidBody.GlobalPosition, _bounceVelX))
					_bounceVelX *= -0.5f;

				Vector2 pos = _rigidBody.GlobalPosition + new Vector2(_bounceVelX, _bounceVelY) * dt;
				bool grounded = false;
				if (pos.Y >= _bounceGroundY)
				{
					pos.Y = _bounceGroundY;
					grounded = true;
					if (_bounceVelY > 0f)
					{
						float bounce = -_bounceVelY * BounceElasticity;
						_bounceVelY = Mathf.Abs(bounce) < 60f ? 0f : bounce;
					}
				}
				_rigidBody.GlobalPosition = pos;

				// 影子同投影(StopOnHit 后的回弹段;地面 = 判定层,与反弹地面一致):
				// X 随视觉、Y 在地面行;越接近地面越大,静止归位由顶部统一检查触发
				UpdateFlightShadow(_rigidBody.GlobalPosition.X, _bounceGroundY);

				// 静止：已接触判定层、反弹已完全吸收（无上升速度）、水平速度衰减到位
				if (grounded && _bounceVelY >= 0f && Mathf.Abs(_bounceVelX) < 40f)
				{
					_bouncing = false;
					_bounceVelX = 0f;
					_bounceVelY = 0f;
					_rigidBody.LinearVelocity = Vector2.Zero;
					if (TryHandleLandingFall()) return;
					if (ShouldKeepAfterLanding)
					{
						FinalizeKeptAfterLanding();
						return;
					}
					_landingHideTimer = LandingHideDelay;
					if (!IsDisposableCopy && IsThrowWeapon)
						_inventoryReturnTimer = ThrowWeaponCooldown;
				}
				return;
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
						// 还原 LinearDamp（由 ApplyScatterImpulse 临时设置）
						_rigidBody.LinearDamp = 0.0f;
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

			// 禁止在投掷到归还/销毁的全程拾取（飞行中、回弹、落地隐藏期间、冷却归还期间均不可拾取）
			if (_isThrown || _inFlight || _bouncing || _landingHideTimer > 0.0 || _inventoryReturnTimer > 0.0)
			{
				return false;
			}

			if (!TryTransferToActor(actor))
			{
				return false;
			}

			// 跨拾取携带(实体即将销毁,状态写到家具槽栈实例上,下次放置/投掷由出口消费):
			// · "件"身份 → 恢复为在场脉冲件。组判据含"件身份"组:投掷中的件只有身份组(脉冲组仅放置时入),
			//   对象撤回(B_006)保留的件从此处恢复,否则捡回后再放置会丢掉件身份。
			// · 投掷耐久已用次数(B_006) → 条件与件身份无关:任何被投出的家具都计数(天然家具扔出后也算)。
			//   注意目的地是**家具槽**而不是快捷栏/背包——AddItemSmart 对 IsFurniture 一律走 AddFurnitureItem,
			//   拾取到的家具只可能落在 FurnitureSlotStack 这个新栈上(见 AddFurnitureItem)。
			if (ResolveInventoryComponent(actor) is PlayerInventoryComponent pieceInv
				&& pieceInv.FurnitureSlotStack != null
				&& pieceInv.FurnitureSlotStack.Item == ItemDefinition)
			{
				pieceInv.FurnitureSlotStack.RuntimeThrowCountUsed = ThrowCountUsed;

				if (IsInGroup(ThrowCorePieceTag) || IsInGroup(ThrowCorePieceIdentityTag))
				{
					pieceInv.FurnitureSlotStack.RuntimeSourceTag = ThrowCorePieceTag;
					pieceInv.FurnitureSlotStack.RuntimeIsThrowCoreCopy = IsInGroup(ThrowCoreCopyTag);
				}
			}

			if (ThrowCooldownRemaining > 0f && _lastTransferredItem != null)
				TryApplyCooldownToPickedStack(actor);

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


		private void TryApplyCooldownToPickedStack(GameActor actor)
		{
			if (_lastTransferredItem == null || !(actor is SamplePlayer player) || player.InventoryComponent == null)
				return;

			float cd = ThrowCooldownRemaining;
			InventoryContainer? quickbar = player.InventoryComponent.QuickBar;
			if (quickbar != null)
			{
				for (int i = 0; i < quickbar.SlotCount; i++)
				{
					var stack = quickbar.GetStack(i);
					if (stack != null && stack.Item == _lastTransferredItem)
					{
						stack.ThrowCooldownRemaining = Mathf.Max(stack.ThrowCooldownRemaining, cd);
						return;
					}
				}
			}
			InventoryContainer? backpack = player.InventoryComponent.Backpack;
			if (backpack != null)
			{
				for (int i = 0; i < backpack.SlotCount; i++)
				{
					var stack = backpack.GetStack(i);
					if (stack != null && stack.Item == _lastTransferredItem)
					{
						stack.ThrowCooldownRemaining = Mathf.Max(stack.ThrowCooldownRemaining, cd);
						return;
					}
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

				if (_rigidBody.GetNodeOrNull<StaticBody2D>("StaticBody2D") is StaticBody2D staticBody)
					staticBody.CollisionLayer |= WorldItemDamageLayer;

				// 保存 RigidBody2D 的原始碰撞设置
				try
				{
					_initialRigidBodyCollisionLayer = _rigidBody.CollisionLayer;
					_initialRigidBodyCollisionMask = _rigidBody.CollisionMask;
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

			// 设置 Hitbox 检测敌人的 HitArea（Area2D，默认 layer 1）
			_hitboxArea.CollisionLayer = 0;
			_hitboxArea.CollisionMask = 1u << 0;
			_hitboxArea.Monitoring = true;
			_hitboxArea.Monitorable = false;

			_hitboxBasePosition = _hitboxArea.Position; // 飞行结束还原(飞行中覆写为地面投影)

			_hitboxArea.AreaEntered += OnHitboxAreaEntered;
		}

		/// <summary>
		/// 当 Hitbox Area2D 检测到碰撞时调用（用于伤害检测，更可靠）
		/// </summary>
		private void OnHitboxAreaEntered(Area2D area)
		{
			if (!_impactArmed)
				return;

			if (!string.Equals(area.Name, "HitArea", StringComparison.OrdinalIgnoreCase))
				return;

			var actor = ResolveActorFromArea(area);
			if (actor != null)
			{
				if (actor == LastDroppedBy)
					return;

				// 免疫目标不构成遮挡（EFFECT_STANDARD 第八节）：跳过 CanBeAffected(null)==false 的目标——
				// 不停留/不回弹/不记录，投掷物视为未接触、照常飞向落点（伤害侧本就被目标自己的守门拒绝）。
				// 例：常态免伤的机枪台（悬挂）对投掷物完全透明；进入 Attack 弱点窗口后恢复可命中。
				if (!actor.CanBeAffected(null))
					return;

				if (_hitActors.Contains(actor))
					return;

				if (_rigidBody != null)
				{
					var velocity = _rigidBody.LinearVelocity;
					if (velocity.Length() >= MinDamageVelocity)
						TryDealImpactDamage(actor, velocity);
				}
				return;
			}

			// 非 GameActor 目标（Gate/FireWallA 等 IBarrier 屏障）
			var receiver = ResolveBarrier(area);
			if (receiver == null || receiver == LastDroppedBy)
				return;

			if (_hitActors.Contains(receiver))
				return;

			if (_rigidBody != null)
			{
				var velocity = _rigidBody.LinearVelocity;
				if (velocity.Length() >= MinDamageVelocity)
					TryDealDamageToNonActor(receiver);
			}
		}

		/// <summary>投掷攻击方向（水平飞行方向，供方向性屏障 FireWallA 的拦截/放行判定）。</summary>
		private Vector2 ResolveThrowDirection()
			=> _throwHorizontalVelocity >= 0f ? Vector2.Right : Vector2.Left;

		/// <summary>
		/// 飞行中手动空间查询：用 Hitbox 形状扫描（mask 与 Hitbox 一致，Areas+Bodies），
		/// 对 IBarrier 屏障（FireWallA/Gate 等）做拦截/放行结算。
		/// 物理查询不依赖 Area 重叠状态——Hitbox 是 RigidBody2D 子节点时 Area↔Body 检测不可靠。
		/// </summary>
		private void QueryNonActorTargets()
		{
			var shapeNode = _hitboxArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (shapeNode?.Shape == null) return;

			var space = _hitboxArea!.GetWorld2D()?.DirectSpaceState;
			if (space == null) return;

			var query = new PhysicsShapeQueryParameters2D
			{
				Shape = shapeNode.Shape,
				Transform = shapeNode.GlobalTransform,
				CollisionMask = _hitboxArea.CollisionMask,
				CollideWithAreas = true,
				CollideWithBodies = true
			};

			foreach (var result in space.IntersectShape(query))
			{
				if (!result.TryGetValue("collider", out var collider)) continue;
				if (collider.As<GodotObject>() is not Node node) continue;

				var receiver = ResolveBarrier(node);
				if (receiver == null || receiver is GameActor) continue; // 敌人走 AreaEntered 信号路径
				if (receiver == LastDroppedBy) continue;
				if (_hitActors.Contains(receiver)) continue;

				TryDealDamageToNonActor(receiver);
			}
		}

		/// <summary>沿父链向上查找 IBarrier 屏障（可被投掷物拦截的目标：Gate/FireWallA 等）。
		/// 显式接口替代 HasMethod("TakeDamage") 鸭子类型——其他投掷物/P2 同伴/普通静态体自然排除。</summary>
		private static Node? ResolveBarrier(Node node)
		{
			Node? current = node;
			while (current != null)
			{
				if (current is IBarrier) return current;
				current = current.GetParent();
			}
			return null;
		}

		/// <summary>对非 GameActor 目标造成伤害（数值复用 CalculateImpactDamage，与 GameActor 命中一致），
		/// 并按 StopOnHit 决定停止飞行（砸到 Gate 等障碍物即停）。
		/// 走 DamageDispatcher 统一管线：方向性屏障（FireWallA）的拦截/放行判定自动生效——
		/// 放行（非接收方向）→ 返回 false 不伤害不停止，投掷物继续飞；拦截 → 伤害 + 停止。</summary>
		private bool TryDealDamageToNonActor(Node receiver)
		{
			int damage = Mathf.Max(1, Mathf.RoundToInt(CalculateImpactDamage()));
			if (damage <= 0) return false;

			bool dealt = DamageDispatcher.DealDamage(receiver, damage, GlobalPosition, LastDroppedBy,
				Kuros.Core.Events.DamageSource.ThrowImpact, TargetableFactions.All, false, null,
				ResolveThrowDirection());
			if (!dealt) return false;

			_hitActors.Add(receiver);

			if (_inFlight)
			{
				if (StopOnHit)
				{
					// 与 GameActor 命中相同的停止流程（落地隐藏/归还由 _PhysicsProcess 处理）
					_inFlight = false;
					_flightTimer = 0.0;
					_impactArmed = false;
					if (_rigidBody != null)
					{
						_rigidBody.LinearVelocity = Vector2.Zero;
						try { _rigidBody.Set("freeze", true); } catch { }
					}
					RestoreRigidBodyCollision();
					HandleStopAfterFlight();
				}
				return true;
			}

			if (StopOnHit)
			{
				StopItemMovement();
			}
			return true;
		}

		private static GameActor? ResolveActorFromArea(Area2D area)
		{
			Node? current = area;
			while (current != null)
			{
				if (current is GameActor actor)
					return actor;
				current = current.GetParent();
			}
			return null;
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
			if (!_impactArmed)
				return;

			if (body is not GameActor actor)
				return;

			if (actor == LastDroppedBy)
				return;

			if (_hitActors.Contains(actor))
				return;

			// 家具投掷路径：仍需确认投掷物与敌人 HitArea 重叠
			var enemyHitArea = actor.GetNodeOrNull<Area2D>("Sprite2D/HitAreaMover/HitArea")
			                    ?? actor.GetNodeOrNull<Area2D>("Sprite2D/HitArea");
			if (enemyHitArea != null && _hitboxArea != null && !_hitboxArea.OverlapsArea(enemyHitArea))
				return;

			if (_rigidBody != null)
			{
				var velocity = _rigidBody.LinearVelocity;
				if (velocity.Length() >= MinDamageVelocity)
					TryDealImpactDamage(actor, velocity);
			}
		}

		/// <summary>
		/// 尝试对目标造成碰撞伤害：attack_power 属性(逐项特化) → 家具档位 → ThrowDamage 兜底。
		/// </summary>
		private float CalculateImpactDamage()
		{
			float attributeDamage = 0f;
			var snapshot = GetAttributeSnapshot();
			if (snapshot.TryGetValue("attack_power", out float ap) && ap > 0f)
				attributeDamage = ap;
			return ItemDefinition != null
				? ItemDefinition.ResolveThrowImpactDamage(attributeDamage, ThrowDamage, Modifiers)
				: (attributeDamage > 0f ? attributeDamage : ThrowDamage);
		}

		private bool TryDealImpactDamage(GameActor target, Vector2 impactVelocity)
		{
			if (target == null || _rigidBody == null)
			{
				return false;
			}

			int damage = Mathf.Max(1, Mathf.RoundToInt(CalculateImpactDamage()));

			if (damage <= 0)
			{
				return false;
			}

			DealImpactToActor(target, damage, impactVelocity);

			// 飞行中命中敌人（必须在 StopItemMovement 之前判断，否则 _inFlight 会被提前清除）
			if (_inFlight)
			{
				// B_008 延迟销毁:一次性道具携带穿透修饰时不停留,穿透敌人继续飞向落点(武器不受该卡影响)
				if (StopOnHit && !(Modifiers.PassThroughEnemies && !IsThrowWeapon))
				{
					// StopOnHit=true：立即停止飞行，走与落地相同的 LandingHideDelay 流程。
					// 停止前沿飞行方向结算贯穿段内的所有敌人——视觉停在第一个敌人，伤害覆盖路径后方敌群
					DealDamageAlongThrowPath(impactVelocity);
					_inFlight = false;
					_flightTimer = 0.0;
					_impactArmed = false;
					if (_rigidBody != null)
					{
						_rigidBody.LinearVelocity = Vector2.Zero;
						try { _rigidBody.Set("freeze", true); } catch { }
					}
					RestoreRigidBodyCollision();
					HandleStopAfterFlight(); // 回弹或直接销毁（由 BounceAfterStop 决定）
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
		/// 命中即停（StopOnHit=true）时的贯穿结算：沿飞行方向延伸"贯穿段"，
		/// 对段内所有敌人统一造成伤害——视觉仍停在第一个敌人处，伤害覆盖路径后方敌群。
		/// 贯穿深度从 CollisionArea（道具实际水平体积）派生，参考对象不是攻击判定区 Hitbox
		/// （竖直长条 10×200，水平宽度不代表撞击体积）。
		/// 伤害数值复用 CalculateImpactDamage（attack_power / 档位 / ThrowDamage），与 CSV 一致。
		/// </summary>
		private void DealDamageAlongThrowPath(Vector2 impactVelocity)
		{
			if (_hitboxArea == null) return;
			float penetration = ResolveThrowPenetrationRange();
			if (penetration <= 0f) return;

			var scanShape = BuildPenetrationScanShape(penetration);
			if (scanShape == null) return;

			// 贯穿段：命中点 → 命中点 + 贯穿距离（X 方向），Y 固定在判定层（与现有 Hitbox 重叠判定同规则，不额外扩展）
			Vector2 dir = _throwHorizontalVelocity >= 0f ? Vector2.Right : Vector2.Left;
			Vector2 center = new Vector2(
				_hitboxArea.GlobalPosition.X + dir.X * penetration * 0.5f,
				_hitboxArea.GlobalPosition.Y);

			DrawThrowPenetrationDebug(scanShape, center);
			DealDamageInScanShape(scanShape, center, impactVelocity);
		}

		/// <summary>共用范围扫描：遍历与 shape 相交的敌人 HitArea,逐个结算(过滤投掷者/已命中/死亡)。</summary>
		private void DealDamageInScanShape(Shape2D shape, Vector2 center, Vector2 impactVelocity)
		{
			if (_hitboxArea == null) return;
			int damage = Mathf.Max(1, Mathf.RoundToInt(CalculateImpactDamage()));
			if (damage <= 0) return;

			var space = _hitboxArea.GetWorld2D().DirectSpaceState;
			var query = new PhysicsShapeQueryParameters2D
			{
				Shape = shape,
				Transform = new Transform2D(0f, center),
				CollisionMask = 1u << 0, // 与现有 Hitbox 检测一致（敌人 HitArea 所在层）
				CollideWithAreas = true,
				CollideWithBodies = false
			};

			foreach (var result in space.IntersectShape(query))
			{
				if (!result.TryGetValue("collider", out var collider)) continue;
				if (collider.As<GodotObject>() is not Area2D area) continue;
				if (!string.Equals(area.Name, "HitArea", StringComparison.OrdinalIgnoreCase)) continue;
				var actor = ResolveActorFromArea(area);
				if (actor == null || actor == LastDroppedBy) continue;
				if (_hitActors.Contains(actor)) continue;
				if (actor.IsDeadOrDying) continue;

				DealImpactToActor(actor, damage, impactVelocity);
			}
		}

		/// <summary>共用目标结算：伤害分发 + 记录 + 击退（方向水平化在 ApplyKnockback 内）。</summary>
		private void DealImpactToActor(GameActor actor, int damage, Vector2 impactVelocity)
		{
			DamageDispatcher.DealDamage(actor, damage, GlobalPosition, LastDroppedBy, Kuros.Core.Events.DamageSource.ThrowImpact);
			_hitActors.Add(actor);
			ApplyKnockback(actor, impactVelocity);
		}

		/// <summary>
		/// 落点砸地 AoE（仅一次性投掷物,投掷武器走自身特效）：飞尽（未命中敌人走到最远落点）时
		/// 按道具碰撞体积生成一次范围伤害——补"终点与敌人重叠却因判定臂已关而无伤"的判定洞,
		/// 并把占地宽度纳入命中范围。范围 = CollisionArea 实际占地 × Hitbox 判定高,中心 = 落点;
		/// 击退离心（自落点向外水平推）。
		/// </summary>
		private void DealDamageOnLanding()
		{
			if (_hitboxArea == null) return;
			float footprint = ResolveThrowPenetrationRange(); // CollisionArea 实际水平占地（矩形取宽/圆取直径）
			if (footprint <= 0f) return;

			var scanShape = BuildPenetrationScanShape(footprint); // (Hitbox 水平宽 + 占地) × Hitbox 高
			if (scanShape == null) return;

			DealDamageInScanShape(scanShape, _hitboxArea.GlobalPosition,
				new Vector2(_throwHorizontalVelocity, 0f));
		}

		/// <summary>临时调试绘制：砸中时在屏幕上显示贯穿扫描范围（ZIndex 10，TopLevel，自动消失）。</summary>
		private void DrawThrowPenetrationDebug(Shape2D scanShape, Vector2 center)
		{
			if (!ShowThrowPenetrationDebug || scanShape == null) return;

			if (_penetrationDebugDrawer == null || !GodotObject.IsInstanceValid(_penetrationDebugDrawer))
			{
				var host = GetTree().CurrentScene ?? GetTree().Root;
				_penetrationDebugDrawer = new AttackHitboxDebugDrawer
				{
					Name = "ThrowPenetrationDebugDrawer",
					ZIndex = 10,
					TopLevel = true,
					Visible = false
				};
				host.AddChild(_penetrationDebugDrawer);
			}

			_penetrationDebugDrawer.ShowFromShape(scanShape, center, new Color(10f, 3f, 10f, 10f), 10f, 2f);
		}

		/// <summary>
		/// 贯穿深度：从投掷物自身 CollisionArea（捡拾/互动区，反映道具实际水平体积）的碰撞形状水平尺寸派生。
		/// 矩形取宽、圆形取直径；无形状回退 0（退化为仅重叠敌人）。
		/// </summary>
		private float ResolveThrowPenetrationRange()
		{
			var shape = ResolveCollisionAreaShape()?.Shape;
			if (shape is RectangleShape2D rect) return rect.Size.X;
			if (shape is CircleShape2D circle) return circle.Radius * 2f;
			return 0f;
		}

		private CollisionShape2D? ResolveCollisionAreaShape()
			=> ResolveAreaCollisionShape(_grabArea);

		/// <summary>通用：查找 Area2D 下的碰撞形状（直接子节点优先，嵌套兜底）。</summary>
		private static CollisionShape2D? ResolveAreaCollisionShape(Area2D? area)
		{
			if (area == null) return null;

			var direct = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (direct != null) return direct;

			foreach (Node child in area.GetChildren())
			{
				if (child is CollisionShape2D shape) return shape;
			}
			return null;
		}

		/// <summary>
		/// 贯穿扫描矩形：宽 = 贯穿距离 + AttackArea 水平尺寸，高 = AttackArea 垂直尺寸——
		/// 等价于"AttackArea 沿飞行方向平移扫过贯穿段"：Y 规则与首段命中判定完全一致，
		/// CollisionArea 只决定 X 延伸多远（道具实际水平体积）。
		/// </summary>
		private RectangleShape2D? BuildPenetrationScanShape(float penetration)
		{
			var attackShape = ResolveAreaCollisionShape(_hitboxArea)?.Shape;
			float attackW = 0f;
			float attackH = 0f;

			if (attackShape is RectangleShape2D rect)
			{
				attackW = rect.Size.X;
				attackH = rect.Size.Y;
			}
			else if (attackShape is CircleShape2D circle)
			{
				attackW = circle.Radius * 2f;
				attackH = circle.Radius * 2f;
			}

			if (attackH <= 0f) return null;

			return new RectangleShape2D { Size = new Vector2(attackW + penetration, attackH) };
		}

		/// <summary>统一击退入口（与命中结算共用）：方向仅取水平分量——击退位移锁定 X 轴，
		/// 目标高度不受影响（不会被砸压或挑飞）。退化链：目标相对水平 → 撞击速度水平分量 → 正 X。
		/// 走 GameActor 位移驱动 API（新三参——攻击方控制距离+时长，与伤害特效击退统一），禁止直接改 Velocity。
		/// 距离/时长解析：场景 export &gt;0 优先 → 家具档位 → 无击退。</summary>
		private void ApplyKnockback(GameActor target, Vector2 impactVelocity)
		{
			float distance = ResolveKnockbackDistance();
			if (distance <= 0f) return;

			float dirX = target.GlobalPosition.X - GlobalPosition.X;
			if (Mathf.Abs(dirX) < 1f)
				dirX = impactVelocity.X;
			if (Mathf.Abs(dirX) < 1f)
				dirX = 1f;
			var knockbackDirection = new Vector2(Mathf.Sign(dirX), 0f);

			target.ApplyKnockbackDisplacement(knockbackDirection, distance, ResolveKnockbackDuration(distance));
		}

		// 击退与血量解析：场景 export >0 优先；否则按修饰档(整体+击退专属 shift / 整体 shift)取档位值；
		// 最后乘击退倍率(B_004 蓄力——距离与投掷距离同比例、初速与飞行时长同处理)。
		// 倍率按 >0?:1 守卫(聚合链起点标量可为 0,直接乘会把数值乘穿)
		private float ResolveKnockbackDistance()
		{
			float baseDistance = KnockbackDistance > 0f ? KnockbackDistance
				: ItemDefinition?.GetResolvedTierSpec(Modifiers, Modifiers.KnockbackTierShift)?.KnockbackDistance ?? 0f;
			float scale = Modifiers.KnockbackDistanceScale > 0f ? Modifiers.KnockbackDistanceScale : 1f;
			return baseDistance * scale;
		}

		/// <summary>击退初速(px/s)：档位初速(高档更快——"砸得更凶") × 初速倍率(B_004 蓄力)。</summary>
		private float ResolveKnockbackSpeed()
		{
			float baseSpeed = ItemDefinition?.GetResolvedTierSpec(Modifiers, Modifiers.KnockbackTierShift)?.KnockbackSpeed ?? 0f;
			float scale = Modifiers.KnockbackSpeedScale > 0f ? Modifiers.KnockbackSpeedScale : 1f;
			return baseSpeed * scale;
		}

		/// <summary>击退时长由"距离 + 初速"派生(受击方匀减速滑完 d = v0×t/2 → t = 2d/v0);
		/// 场景 export KnockbackDuration >0 仍优先(逐件特化,不参与倍率);初速缺失回退 0.2s。</summary>
		private float ResolveKnockbackDuration(float distance)
		{
			if (KnockbackDuration > 0f) return KnockbackDuration;
			float speed = ResolveKnockbackSpeed();
			return speed > 0f ? 2f * distance / speed : 0.2f;
		}

		private float ResolveMaxHP()
			=> MaxHP > 0f ? MaxHP
				: ItemDefinition?.GetResolvedTierSpec(Modifiers, Modifiers.HpTierShift)?.MaxHp ?? 60f;

		/// <summary>
		/// 飞行中沿水平方向发射射线，检测前方是否为 AirWall（空气墙）。
		/// 仅 AirWall 触发销毁，其他物理体（地面/障碍/投掷物）不拦截。
		/// </summary>
		private bool CheckWallHit(Vector2 from, float horizontalVelocity)
		{
			if (_rigidBody == null || horizontalVelocity == 0f) return false;

			float step = Mathf.Abs(horizontalVelocity) * 0.05f + 20f; // 覆盖本帧移动距离
			var space = _rigidBody.GetWorld2D().DirectSpaceState;
			var query = new PhysicsRayQueryParameters2D
			{
				From = from,
				To = from + new Vector2(Mathf.Sign(horizontalVelocity) * step, 0f),
				CollideWithBodies = true,
				CollideWithAreas = false
			};

			var result = space.IntersectRay(query);
			if (result.Count == 0 || !result.TryGetValue("collider", out var collider))
				return false;

			var body = collider.As<GodotObject>();
			if (body is GameActor) return false;      // 敌人/玩家交给伤害流程
			if (body == LastDroppedBy) return false;
			if (body is Node node && !string.Equals((string)node.Name, "AirWall", System.StringComparison.OrdinalIgnoreCase))
				return false;
			return true;
		}

		/// <summary>
		/// 投掷即效果（SpawnEffectOnThrow）：投掷瞬间销毁武器本体并触发 OnThrowDestroy 效果。
		/// 不进入飞行、不做槽位预占（投掷武器为背包副本，原 stack 保留带 CD），伤害由效果自带的判定负责。
		/// </summary>
		private void ImmediateEffectThrow()
		{
			if (_isDestroying) return;
			_isDestroying = true;
			Visible = false; // 不闪一帧

			if (_grabArea != null)
			{
				DisableGrabArea();
			}
			if (_hitboxArea != null)
			{
				_hitboxArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}

			// 生成 OnThrowDestroy 效果（BoomerangAttackEffect，Attacker 由 SpawnThrowDestroyEffects 设置）
			SpawnThrowDestroyEffects();
			QueueFree();
		}

		/// <summary>
		/// 飞行停止后的收尾：由 BounceAfterStop 决定进入脚本回弹模拟（静止后销毁）还是直接走 LandingHideDelay 销毁流程。
		/// </summary>
		private void HandleStopAfterFlight()
		{
			if (BounceAfterStop)
			{
				StartBounce();
				return;
			}

			// 直接销毁：走 LandingHideDelay 计时（隐藏/归还由 _PhysicsProcess 处理）
			if (!_isDestroying)
			{
				if (TryHandleLandingFall()) return;
				if (ShouldKeepAfterLanding)
				{
					FinalizeKeptAfterLanding();
					return;
				}

				_landingHideTimer = LandingHideDelay;
				if (!IsDisposableCopy && IsThrowWeapon)
					_inventoryReturnTimer = ThrowWeaponCooldown;
			}
		}

		/// <summary>
		/// 进入脚本回弹模拟：向后上方初速度 + 重力积分回落 + 判定层反弹衰减。
		/// 与真实物理相比完全可控（无物理引擎状态/碰撞副作用），俯视角"地面"= 判定层。
		/// </summary>
		/// <param name="forwardHorizontal">水平分量方向：
		/// false(默认,撞击停止) = 向投掷反方向弹开(撞墙/撞人原语义)；
		/// true(完整抛物线干净落点) = 顺势向前滚——没有撞击对象可弹回。</param>
		private void StartBounce(bool forwardHorizontal = false)
		{
			if (_rigidBody == null || _bouncing) return;
			_bouncing = true;
			float hSpeed = Mathf.Abs(_throwHorizontalVelocity) * GetEffectiveBounceHorizontalRatio();
			_bounceVelX = (forwardHorizontal ? Mathf.Sign(_throwHorizontalVelocity) : -Mathf.Sign(_throwHorizontalVelocity)) * hSpeed;
			_bounceVelY = -GetEffectiveBounceSpeed();

			// 回弹地面 = 本件自己的落点行（A_008 分裂件的落点行是偏移过的）；
			// 写死 _throwJudgmentY(基准行) 会把偏移件在回弹时拽回基准行
			_bounceGroundY = _throwJudgmentY + LandingOffsetYDelta;

			// 回弹阶段视觉在地面附近，恢复阴影
			SetShadowVisible(true);

			// 回弹期间不隐藏/不归还；静止后再设计时
			_landingHideTimer = 0.0;
			_inventoryReturnTimer = 0.0;
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
				SetFurnitureStaticBodyCollision(true);
				_isThrown = false;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"{Name}: 无法恢复 RigidBody2D 的碰撞设置: {ex.Message}");
			}
		}

		private void SetFurnitureStaticBodyCollision(bool enabled)
		{
			if (_rigidBody == null) return;
			var staticBody = _rigidBody.GetNodeOrNull<StaticBody2D>("StaticBody2D");
			if (staticBody == null) return;
			var shape = staticBody.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (shape != null)
				shape.Disabled = !enabled;
		}
		/// 将此物品归还背包（投掷武器落地后归还，仍有剩余使用次数时调用）
		/// </summary>
		private void ReturnToInventory()
		{
			if (!(LastDroppedBy is SamplePlayer player) || player.InventoryComponent == null)
				return;

			var item = ItemDefinition ?? CurrentStack?.Item;
			if (item == null) return;

			// 先把物品放回背包/快捷栏。

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

			SyncPlayerHandAndQuickBar(player);
		}

		/// <summary>
		/// 飞行中击中敌人时销毁自身并生成特效
		/// </summary>
		public void TriggerDestruction()
		{
			if (_isDestroying || !_impactArmed) return;
			_impactArmed = false;
			if (_hitboxArea != null) _hitboxArea.Monitoring = false;
			if (_inFlight)
				DestroyItemOnImpact();
			else
				DestroyItem();
		}
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

			// 飞行中命中销毁：释放预占槽位（道具不归还）
			if (LastDroppedBy is SamplePlayer impactSPlayer)
			{
				if (_reservedQuickBarSlotIndex >= 0 && impactSPlayer.InventoryComponent != null)
				{
					impactSPlayer.InventoryComponent.ReservedQuickBarSlots.Remove(_reservedQuickBarSlotIndex);
					_reservedQuickBarSlotIndex = -1;
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

			// 生成 OnThrowDestroy 效果（Node2D 在世界坐标生成，ActorEffect 应用到投掷者）
			SpawnThrowDestroyEffects();

			// 飞行命中销毁同样广播(供 A_004 响应"投掷命中销毁")
			NotifyDestroyedOnce();

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

			// 生成 OnThrowDestroy 效果（Node2D 在世界坐标生成，ActorEffect 应用到投掷者）
			SpawnThrowDestroyEffects();

			// 投掷件销毁广播(节点销毁前;供 A_004 冲击投放等订阅者响应"投掷销毁")
			NotifyDestroyedOnce();

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

			// 生成 OnThrowDestroy 效果（Node2D 在世界坐标生成，ActorEffect 应用到投掷者）
			SpawnThrowDestroyEffects();
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

			// 落地(非飞行)命中销毁同样广播(供 A_004 响应)
			NotifyDestroyedOnce();

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

			int accepted = inventory.AddItemSmart(stack.Item, stack.Quantity, actor, showPopupIfFirstTime: true);
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

		/// <summary>
		/// <summary>
		/// 预热 OnThrowDestroy 效果场景的 Shader。
		/// 将每个 Node2D 效果场景实例化一次并立刻销毁，触发 Godot 在 _Ready 阶段编译所有相关 Shader，
		/// 避免投掷落地时因首次 Shader 编译导致的主线程卡顿。
		/// ActorEffect 类型不含渲染资源，跳过处理。
		/// </summary>
		private void WarmUpThrowDestroyEffectShaders()
		{
			if (ItemDefinition == null) return;

			foreach (var entry in ItemDefinition.GetEffectEntries(ItemEffectTrigger.OnThrowDestroy))
			{
				if (entry?.EffectScene == null) continue;

				try
				{
					var node = entry.EffectScene.Instantiate();
					if (node is Node2D node2D)
					{
						// 隐藏后加入场景树，触发 Shader 编译，然后立刻销毁
						node2D.Visible = false;
						node2D.ProcessMode = ProcessModeEnum.Disabled;
						AddChild(node2D);
						node2D.QueueFree();
					}
					else
					{
						node?.QueueFree();
					}
				}
				catch
				{
					// 预热失败不影响正常流程，静默忽略
				}
			}
		}

		/// <summary>
		/// 在落点/命中点处理 OnThrowDestroy 效果：
		/// - Node2D 类型场景：在世界坐标实例化（烟雾、爆炸等视觉效果）
		/// - ActorEffect 类型：应用到投掷者身上
		/// </summary>
		private void SpawnThrowDestroyEffects()
		{
			if (ItemDefinition == null) return;

			Vector2 spawnPos = _rigidBody?.GlobalPosition ?? GlobalPosition;

			foreach (var entry in ItemDefinition.GetEffectEntries(ItemEffectTrigger.OnThrowDestroy))
			{
				if (entry?.EffectScene == null) continue;

				if (ThrowHoldFrame >= 0)
				{
					if (entry.RequiredFrameMin >= 0 && ThrowHoldFrame < entry.RequiredFrameMin)
						continue;
					if (entry.RequiredFrameMax >= 0 && ThrowHoldFrame > entry.RequiredFrameMax)
						continue;
				}

				try
				{
					// 通用方式实例化，ActorEffect 优先（如 EmojiBoomEffect）
					var node = entry.EffectScene.Instantiate();

					if (node is Kuros.Core.Effects.ActorEffect actorEffect)
					{
						entry.ApplyOverrides(actorEffect);

						if (actorEffect is Kuros.Core.Effects.IWorldSpawnable worldSpawnable)
							worldSpawnable.WorldSpawnPosition = spawnPos;

						if (LastDroppedBy?.EffectController != null)
							LastDroppedBy.ApplyEffect(actorEffect);
						else
							actorEffect.QueueFree();
					}
					else if (node is Node2D node2D)
					{
						entry.ApplyOverrides(node2D);
						// Attacker 必须在 AddChild（触发 _Ready）之前注入——否则效果的 _Ready
						// 读到的 Attacker 为 null（如 BriefcaseOpenEffect 的朝向翻转）
						if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
							attackerProvider.Attacker = LastDroppedBy;
						// 生成朝向 = 投掷方向（非投掷者当前朝向——投掷后转身不应影响生成朝向，
						// 如 BriefcaseOpenEffect 延迟生成 FireWall 的翻转）
						if (node2D is Kuros.Fx.IFacingDirectional facing)
							facing.FacingRight = _throwHorizontalVelocity >= 0f;
						// 投掷即效果(回旋镖等):注入本次投掷飞行距离——与 ThrowTrajectoryPreview 同一解析真源
						// (ItemDefinition.GetEffectiveThrowDistance + 出手快照 Modifiers/单次 Override);
						// 仅在定义提供了显式距离来源时注入,否则特效使用自有参数
						if (node2D is Kuros.Fx.IThrowFlightDistance flightDistance
							&& (OverrideThrowDistance > 0f || ItemDefinition?.ThrowHorizontalDistance > 0f))
							flightDistance.SetThrowFlightDistance(GetEffectiveThrowHorizontalDistance());
						GetParent()?.AddChild(node2D);
						node2D.GlobalPosition = spawnPos;
						node2D.SetMeta("source_weapon_item_id", ItemDefinition?.ItemId ?? "");
					}
					else
					{
						node?.QueueFree();
					}
				}
				catch (Exception ex)
				{
					GD.PushWarning($"[{Name}] 无法生成 OnThrowDestroy 效果: {ex.Message}");
				}
			}
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
		public void TakeDamage(float damage)
		{
			if (!Destructible) return;
			if (CurrentHP <= 0f) return;
			if (_damageCooldownRemaining > 0f) return;

			CurrentHP = Mathf.Max(0f, CurrentHP - damage);
			_damageCooldownRemaining = DamageCooldown;
			TriggerHitFlash();

			if (CurrentHP <= 0f)
				Destroy();
		}

		/// <summary>
		/// 外部主动销毁入口(带完整销毁表现:OnThrowDestroy 特效/掉落链 + QueueFree)。
		/// 供长按核心技能等"清场式"销毁调用;飞行/回弹/已在销毁中的实体忽略(防中途误清)。
		/// </summary>
		public void RequestDestroy()
		{
			if (_isDestroying || _inFlight || _isThrown || _bouncing) return;
			_isDestroying = true;
			Destroy();
		}

		/// <summary>本件落点是否不销毁（B_006 对象撤回）：一次性道具 + 卡已在出手瞬间置位 + 未耗尽投掷耐久。
		/// 耐久判定用"已用次数 ≥ 上限"——第 N 次投掷照常飞行/结算伤害，落地后正常销毁（即"最多投 N 次"）。</summary>
		private bool ShouldKeepAfterLanding =>
			KeepAfterLanding && !IsThrowWeapon
			&& (KeepThrowLimit <= 0 || ThrowCountUsed < KeepThrowLimit);

		/// <summary>落点收尾（B_006 不销毁）：把投掷态收干净,变回普通可拾取世界物。
		/// 关键点——同步根位置到本体：飞行/落点期间视觉由内部 RigidBody2D 承载,根节点还停在投掷起点,
		/// 而高亮、拾取距离、遮挡判定都用根位置,不同步的话会锚在起点（站在道具旁没高亮、在起点却提示可拾取）。
		/// 碰撞还原幂等（RestoreRigidBodyCollision 以投掷中为门控,已还原则空转）。</summary>
		private void FinalizeKeptAfterLanding()
		{
			if (_flightProjectionActive) RestoreFlightProjection();
			RestoreRigidBodyCollision();
			SetShadowVisible(true);
			SyncRootToBody();
			// 还原场景图层：投掷期 ZIndex 被覆写为 ThrowZIndex(3),不还原会永远盖住所有家具(场景值 2)
			ZIndex = _initialZIndex;

			// 视为在场件（与"放置件"同等待遇）：长按核心键清场、A_002 脉冲扩散、A_003 节点串联
			// 都按脉冲组枚举——投出后保留的件现在也是"在场的核心件"。
			// 仅核心件（有件身份组）入组：天然家具被扔出后不该获得脉冲、也不该被清场扫掉。
			if (IsInGroup(ThrowCorePieceIdentityTag) && !IsInGroup(ThrowCorePieceTag))
				AddToGroup(ThrowCorePieceTag);

			_refreezePending = false;
			_refreezeTimer = 0.0;
			_flightTimer = 0.0;
			_isDropping = false;
			_bouncing = false;
			_inFlight = false;
			_impactArmed = false;
			if (_isThrown) _isThrown = false;
		}

		/// <summary>把根节点位置同步到本体实际位置（本体作为子节点会被一起移动,再钉回原处）。</summary>
		private void SyncRootToBody()
		{
			if (_rigidBody == null || !GodotObject.IsInstanceValid(_rigidBody)) return;
			Vector2 pos = _rigidBody.GlobalPosition;
			GlobalPosition = pos;
			_rigidBody.GlobalPosition = pos;
		}

		private bool _destroyedBroadcast;

		/// 销毁广播只发一次:同一件可能经多个销毁入口(Destroy/落地/飞行命中/墙壁),防重复爆炸。
		private void NotifyDestroyedOnce()
		{
			if (_destroyedBroadcast) return;
			_destroyedBroadcast = true;
			Destroyed?.Invoke(this);
		}

		private void Destroy()
		{
			var rigidBody = _rigidBody;
			Vector2 spawnPos = (rigidBody?.GlobalPosition ?? GlobalPosition);

			if (ItemDefinition != null)
			{
				foreach (var entry in ItemDefinition.GetEffectEntries(ItemEffectTrigger.OnThrowDestroy))
				{
					if (entry?.EffectScene == null) continue;
					try
					{
						var node = entry.EffectScene.Instantiate();
						if (node is Node2D node2D)
						{
							entry.ApplyOverrides(node2D);
								// Attacker 在 AddChild（触发 _Ready）之前注入
								if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
									attackerProvider.Attacker = LastDroppedBy;
								var worldNode = GetTree().CurrentScene?.GetNodeOrNull<Node>("World")
								?? GetTree().CurrentScene;
							worldNode?.AddChild(node2D);
							node2D.GlobalPosition = spawnPos;
								node2D.SetMeta("source_weapon_item_id", ItemDefinition?.ItemId ?? "");
						}
						else if (node is Kuros.Core.Effects.ActorEffect actorEffect)
						{
							entry.ApplyOverrides(actorEffect);
							if (actorEffect is Kuros.Core.Effects.IWorldSpawnable worldSpawnable)
								worldSpawnable.WorldSpawnPosition = spawnPos;
							LastDroppedBy?.ApplyEffect(actorEffect);
						}
						else
						{
							node?.QueueFree();
						}
					}
					catch (Exception ex)
					{
						GD.PushWarning($"[RigidBodyWorldItemEntity] Cannot spawn destroy effect: {ex.Message}");
					}
				}
			}

			NotifyDestroyedOnce();
			QueueFree();
		}

		private void SetupHitFlash()
		{
			if (HitFlashSpritePath.IsEmpty) return;

			_hitFlashSprite = GetNodeOrNull<Sprite2D>(HitFlashSpritePath);
			if (_hitFlashSprite == null) return;

			var shader = GD.Load<Shader>("res://shaders/materials/trigger_hit.gdshader");
			if (shader == null) return;

			_hitFlashMaterial = new ShaderMaterial();
			_hitFlashMaterial.Shader = shader;
			_hitFlashMaterial.SetShaderParameter("get_hit", false);
			_hitFlashMaterial.SetShaderParameter("hit_effect", 0f);
			_hitFlashMaterial.SetShaderParameter("flash_color", HitFlashColor);
			_hitFlashMaterial.SetShaderParameter("flash_speed", HitFlashSpeed);
			_hitFlashMaterial.SetShaderParameter("shake_intensity", HitShakeIntensity);
		}

		private void TriggerHitFlash()
		{
			if (_hitFlashSprite == null || _hitFlashMaterial == null) return;

			if (!_hitFlashActive)
				_hitFlashOriginalMaterial = _hitFlashSprite.Material;

			_hitFlashSprite.Material = _hitFlashMaterial;
			_hitFlashMaterial.SetShaderParameter("get_hit", true);
			_hitFlashMaterial.SetShaderParameter("hit_effect", 1f);
			_hitFlashTimer = HitFlashDuration;
			_hitFlashActive = true;
		}

		private void EndHitFlash()
		{
			_hitFlashActive = false;
			_hitFlashMaterial?.SetShaderParameter("get_hit", false);
			_hitFlashMaterial?.SetShaderParameter("hit_effect", 0f);

			if (_hitFlashSprite != null)
				_hitFlashSprite.Material = _hitFlashOriginalMaterial;
		}

	}
}
