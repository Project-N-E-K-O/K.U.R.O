using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	/// <summary>在 MeetingBarrier 阻挡墙的基础上追加接触伤害与击退（FireWallC 的六段墙）。
	/// 独立子类：FireWallA/B 与 MeetingBarrier_* 角墙继续使用无伤害的 MeetingBarrier。</summary>
	public partial class ContactDamageBarrier : MeetingBarrier
	{
		[ExportCategory("Contact Damage 接触伤害")]
		/// <summary>接触墙面时的单次伤害；0 = 不造成接触伤害。</summary>
		[Export(PropertyHint.Range, "0,200,1")] public int ContactDamage { get; set; } = 0;
		[Export(PropertyHint.Range, "0.05,5,0.05")] public float ContactDamageInterval { get; set; } = 0.5f;
		/// <summary>目标发现物理层（默认 Player 层 = 4 与 WorldItem 层 = 8）。家具的可破坏 StaticBody2D
		/// 由 RigidBodyWorldItemEntity 同时挂到 WorldItem 层，因此这里能通过物理查询发现它。</summary>
		[Export(PropertyHint.Layers2DPhysics)] public uint ContactCollisionMask { get; set; } = 12;
		/// <summary>接触伤害的阵营过滤（与 CONTACTMASK 正交：mask 负责找到碰撞体，这里按组过滤）。</summary>
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions ContactTargetableFactions { get; set; } =
			TargetableFactions.Player | TargetableFactions.WorldItem;
		[Export] public bool ContactAllowSelfDamage { get; set; } = false;
		/// <summary>伤害区相对墙面形状的放大倍率。</summary>
		[Export(PropertyHint.Range, "1.0,2.0,0.01")] public float ContactAreaScale { get; set; } = 1.08f;
		/// <summary>接触击退距离（像素）；0 = 不击退。</summary>
		[Export(PropertyHint.Range, "0,2000,10")] public float ContactKnockbackDistance { get; set; } = 150f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float ContactKnockbackDuration { get; set; } = 0.25f;
		/// <summary>攻击来源（由 DirectionalSceneEffect 从 AttackEffectEntry 注入转发）。</summary>
		public GameActor? Attacker { get; set; }

		private Area2D? _contactArea;
		private CollisionShape2D? _contactShape;
		private double _contactClock;
		private bool _contactStopped;
		private bool _selfDestructTriggered;
		private ShaderMaterial? _coreDamageMaterial;
		private ShaderMaterial? _scanDamageMaterial;
		private readonly Dictionary<ulong, double> _contactNextHit = new();
		private readonly HashSet<ulong> _contactSeen = new();
		private readonly List<ulong> _contactStale = new();

		public override void _Ready()
		{
			base._Ready();
			// 基类已把材质复制到各 Sprite2D 上；这里缓存用来直接驱动损伤表现
			_coreDamageMaterial = (GetNodeOrNull<Sprite2D>(CoreWallPath)?.Material) as ShaderMaterial;
			_scanDamageMaterial = (GetNodeOrNull<Sprite2D>(ScanFXPath)?.Material) as ShaderMaterial;
			if (ContactDamage > 0)
				SetupContactArea();
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			// 命中自毁时不改自身 HP，基类每帧会按 CurrentHP 把 damage_level 重算回 0；
			// 这里在基类之后强制满损伤，让 ScanFX/CoreWall 在淡出期间一直播放损伤特效。
			if (!_selfDestructTriggered) return;
			_coreDamageMaterial?.SetShaderParameter("damage_level", 1f);
			_scanDamageMaterial?.SetShaderParameter("damage_level", 1f);
		}

		protected override void Destroy()
		{
			_contactStopped = true;
			base.Destroy();
		}

		/// <summary>命中目标后墙体自毁：直接驱动 ScanFX/CoreWall 的损伤表现并进入
		/// MeetingBarrier 的淡出销毁流程（不走自身 HP，不触发受击闪白/伤害冷却）。</summary>
		private void TriggerSelfDestruct()
		{
			if (_selfDestructTriggered) return;
			_selfDestructTriggered = true;
			_coreDamageMaterial?.SetShaderParameter("damage_level", 1f);
			_scanDamageMaterial?.SetShaderParameter("damage_level", 1f);
			Destroy();
		}

		/// <summary>按 StaticBody2D 的墙面形状生成贴合形状的伤害区（略放大）。
		/// 墙节点被 AnimationPlayer 移动/缩放时，伤害区跟着走。</summary>
		private void SetupContactArea()
		{
			var staticBody = GetNodeOrNull<StaticBody2D>("StaticBody2D");
			CollisionShape2D? shapeNode = null;
			if (staticBody != null)
			{
				foreach (Node child in staticBody.GetChildren())
				{
					if (child is CollisionShape2D shape && shape.Shape != null)
					{
						shapeNode = shape;
						break;
					}
				}
			}

			if (shapeNode == null)
			{
				GD.PushWarning($"[{nameof(ContactDamageBarrier)}] ContactDamage>0 但未找到 StaticBody2D 的碰撞形状：{Name}");
				return;
			}

			// Area2D 只作为跟随墙面变换的形状容器：家具的 StaticBody2D 不会出现在
			// Area2D 的重叠/body_entered 列表里，目标发现统一走每帧 IntersectShape（EFFECT_STANDARD §二）。
			_contactArea = new Area2D
			{
				Name = "ContactDamageArea",
				CollisionLayer = 0,
				CollisionMask = ContactCollisionMask,
				Monitorable = false,
				Monitoring = false,
			};
			_contactShape = new CollisionShape2D
			{
				Name = "CollisionShape2D",
				Shape = (Shape2D)shapeNode.Shape.Duplicate(),
			};
			_contactArea.AddChild(_contactShape);
			AddChild(_contactArea);
			_contactShape.GlobalTransform = shapeNode.GlobalTransform;
			_contactShape.Scale *= ContactAreaScale;
		}

		public override void _PhysicsProcess(double delta)
		{
			if (ContactDamage <= 0 || _contactStopped || _contactShape?.Shape == null) return;
			if (Attacker == null || !GodotObject.IsInstanceValid(Attacker)) return;

			var space = GetWorld2D()?.DirectSpaceState;
			if (space == null) return;

			_contactClock += delta;
			_contactSeen.Clear();

			var query = new PhysicsShapeQueryParameters2D
			{
				Shape = _contactShape.Shape,
				Transform = _contactShape.GlobalTransform,
				CollisionMask = ContactCollisionMask,
				CollideWithAreas = true,
				CollideWithBodies = true,
			};

			Vector2 origin = _contactArea?.GlobalPosition ?? GlobalPosition;
			foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 32))
			{
				if (hit["collider"].As<Node>() is not Node collider) continue;

				// 接收者不限于 GameActor：家具等世界物由 DamageDispatcher 沿父链解析到
				// world_items 组的 TakeDamage 接收者，阵营/方向/自伤过滤统一在 dispatcher 内完成。
				Node? receiver = DamageDispatcher.ResolveDamageReceiver(collider, ContactTargetableFactions);
				if (receiver is not Node2D target) continue;

				ulong id = receiver.GetInstanceId();
				if (!_contactSeen.Add(id)) continue; // 同帧内 body 与其子 Area 解析到同一接收者
				if (_contactNextHit.TryGetValue(id, out double nextHit) && _contactClock < nextHit) continue;

				bool dealt = DamageDispatcher.DealDamage(collider, ContactDamage, origin, Attacker,
					DamageSource.AreaEffect, ContactTargetableFactions, ContactAllowSelfDamage);
				if (!dealt) continue;

				_contactNextHit[id] = _contactClock + Mathf.Max(ContactDamageInterval, 0.05f);

				// 击退只走 GameActor 统一入口（内部处理 ForcedMovement 免疫与玩家侧受击门）
				if (ContactKnockbackDistance > 0f && target is GameActor actor)
				{
					Vector2 direction = actor.GlobalPosition - origin;
					direction = direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector2.Right;
					actor.ApplyKnockbackDisplacement(direction, ContactKnockbackDistance,
						Mathf.Max(ContactKnockbackDuration, 0.01f));
				}

				// 命中即自毁（等同受到自身 MaxHP 伤害的视觉结果）
				TriggerSelfDestruct();
			}

			// 离开接触区的目标不再保留冷却记录（避免长时间存活时字典无界增长）
			if (_contactNextHit.Count > _contactSeen.Count)
			{
				_contactStale.Clear();
				foreach (ulong id in _contactNextHit.Keys)
					if (!_contactSeen.Contains(id)) _contactStale.Add(id);
				foreach (ulong id in _contactStale) _contactNextHit.Remove(id);
			}
		}
	}
}
