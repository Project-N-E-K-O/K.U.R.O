using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors;
using Kuros.Core;
using Kuros.Managers;
using Kuros.Utils;
 
namespace Kuros.Items.World
{
	/// <summary>
	/// 重力手雷生成的黑洞实体
	/// 负责：吸引周围敌人，持续造成伤害，管理黑洞的生命周期
	/// </summary>
	public partial class GravityGrenadeBlackHole : Node2D
	{
		[ExportGroup("Detection")]
		[Export] public NodePath AttractAreaPath { get; set; } = new NodePath("AttractArea");
		[Export] public NodePath AttractCollisionShapePath { get; set; } = new NodePath("AttractArea/CollisionShape2D");

		[ExportGroup("BlackHole Properties")]
		[Export(PropertyHint.Range, "50,500,10")] public float AttractRadius { get; set; } = 200f; // 吸引范围
		[Export(PropertyHint.Range, "100,2000,50")] public float AttractForce { get; set; } = 400f; // 吸引力度
		[Export(PropertyHint.Range, "1,100,5")] public float DamagePerSecond { get; set; } = 15f; // 每秒伤害值
		[Export(PropertyHint.Range, "0.1,5,0.1")] public float TickInterval { get; set; } = 0.2f; // 伤害检测间隔
		[Export(PropertyHint.Range, "1,30,0.5")] public float Duration { get; set; } = 8f; // 黑洞持续时间
		[Export(PropertyHint.Range, "0.5,3,0.1")] public float PullVelocityDamping { get; set; } = 0.95f; // 被吸引时的速度衰减
		[Export] public bool AffectEnemiesGroupOnly { get; set; } = true;
		[Export] public bool UseBroadActorFallback { get; set; } = true; // 组筛选失败时，是否回退为全场Actor扫描
		[Export(PropertyHint.Range, "0,3,0.05")] public float PullOnlyDuration { get; set; } = 0.65f; // 仅吸附不伤害阶段
		[Export(PropertyHint.Range, "1,6,0.1")] public float PullBurstMultiplier { get; set; } = 3.6f; // 起手强吸倍率
		[Export(PropertyHint.Range, "0,200,1")] public float CenterSnapRadius { get; set; } = 72f; // 进入中心区后快速收束
		[Export(PropertyHint.Range, "0.5,0.99,0.01")] public float CenterSnapDamping { get; set; } = 0.72f;
		[Export(PropertyHint.Range, "10,2000,10")] public float PullMinPixelsPerSecond { get; set; } = 420f; // 最小位置牵引速度，保证可见吸附
		[Export(PropertyHint.Range, "1,30,0.1")] public float PullLerpPerSecond { get; set; } = 10f; // 保留参数兼容旧配置
		
		[ExportGroup("Visual")]
		[Export] public NodePath SpriteBlackHolePath { get; set; } = new NodePath("VisualSprite");
		[Export] public float FadeOutStartTime { get; set; } = 6.5f; // 开始淡出的时间
		[Export(PropertyHint.Range, "0.05,2,0.01")] public float IntroDuration { get; set; } = 0.16f; // 出现：原地放大
		[Export(PropertyHint.Range, "0.05,2,0.01")] public float OutroDuration { get; set; } = 0.3f; // 消失：坍缩缩小
		[Export(PropertyHint.Range, "1,4,0.1")] public float CollapsePower { get; set; } = 1.9f;
		[Export(PropertyHint.Range, "0,0.5,0.01")] public float SquashAmount { get; set; } = 0.12f;
		[Export(PropertyHint.Range, "0,20,0.1")] public float SquashFrequency { get; set; } = 5.0f;
		[Export(PropertyHint.Range, "0,30,0.1")] public float JitterFrequency { get; set; } = 9.0f;
		[Export(PropertyHint.Range, "0,8,0.1")] public float JitterRotationDegrees { get; set; } = 2.2f;
		[Export(PropertyHint.Range, "0,20,0.1")] public float PulsingScalePercent { get; set; } = 6.0f;
		
		private Area2D? _attractArea;
		private CollisionShape2D? _attractCollisionShape;
		private Shape2D? _queryShape;
		private Sprite2D? _spriteBlackHole;
		private ShaderMaterial? _shaderMaterial;
		private double _lifeTimer = 0.0;
		private double _damageTickTimer = 0.0;
		private GameActor? _sourceActor; // 发射方，用于伤害计算属性
		private Vector2 _baseScale = Vector2.One;
		private float _resolvedOutroStart;
		
		public override void _Ready()
		{
			// 提高处理优先级，让黑洞在多数角色位移逻辑之后执行，避免被同帧覆盖。
			ProcessPriority = 100;
			ProcessPhysicsPriority = 100;

			// 优先使用场景中手动配置的吸附区域。
			_attractArea = GetNodeOrNull<Area2D>(AttractAreaPath);
			if (_attractArea == null)
			{
				_attractArea = new Area2D
				{
					Name = "AttractArea"
				};
				AddChild(_attractArea);
			}

			_attractCollisionShape = GetNodeOrNull<CollisionShape2D>(AttractCollisionShapePath)
				?? _attractArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");

			if (_attractCollisionShape == null)
			{
				_attractCollisionShape = new CollisionShape2D
				{
					Name = "CollisionShape2D",
					Shape = new CircleShape2D { Radius = AttractRadius }
				};
				_attractArea.AddChild(_attractCollisionShape);
			}
			
			// 设置碰撞层配置
			_attractArea.CollisionLayer = 0;
			_attractArea.CollisionMask = 1u << 1; // 检测敌人层
			_attractArea.Monitoring = true;
			_attractArea.Monitorable = false;
			SyncAttractShapeCache();
			
			// 获取黑洞精灵和Shader
			_spriteBlackHole = GetNode<Sprite2D>(SpriteBlackHolePath);
			if (_spriteBlackHole != null)
			{
				_shaderMaterial = _spriteBlackHole.Material as ShaderMaterial;
				_baseScale = _spriteBlackHole.Scale;
				_spriteBlackHole.Scale = Vector2.Zero;
				_spriteBlackHole.Modulate = new Color(1, 1, 1, 0f);
			}

			float maxOutroStart = Mathf.Max(0f, Duration - Mathf.Max(0.01f, OutroDuration));
			_resolvedOutroStart = Mathf.Clamp(FadeOutStartTime, 0f, maxOutroStart);
			
			GameLogger.Info(nameof(GravityGrenadeBlackHole), $"[{Name}] 黑洞已生成，吸引范围: {AttractRadius}，持续时间: {Duration}s");
		}
		
		public override void _PhysicsProcess(double delta)
		{
			_lifeTimer += delta;
			_damageTickTimer += delta;
			
			// 检查是否超时
			if (_lifeTimer >= Duration)
			{
				QueueFree();
				return;
			}
			
			// 在物理帧中执行吸附，确保对 CharacterBody2D 的位移生效并减少被覆盖。
			AttrackNearbyActors(delta);

			// 先强拉到中心，再开始持续伤害。
			if (_lifeTimer >= PullOnlyDuration && _damageTickTimer >= TickInterval)
			{
				_damageTickTimer = 0.0;
				DealDamageToAttractedActors();
			}
		}

		public override void _Process(double delta)
		{
			if (_lifeTimer >= Duration)
			{
				return;
			}

			// 普通帧只负责视觉形变。
			UpdateVisualDeformation();
		}
		
		/// <summary>
		/// 吸引范围内的敌人
		/// </summary>
		private void AttrackNearbyActors(double delta)
		{
			var attractedList = CollectActorsInRadius();
			bool inPullOnlyPhase = _lifeTimer < PullOnlyDuration;
			float dt = (float)delta;
			
			foreach (var actor in attractedList)
			{
				// 计算吸引方向和力度
				Vector2 direction = (GlobalPosition - actor.GlobalPosition).Normalized();
				float distance = GlobalPosition.DistanceTo(actor.GlobalPosition);

				float effectiveRadius = Mathf.Max(GetEffectiveAttractRadius(), 1f);
				float t = 1.0f - Mathf.Clamp(distance / effectiveRadius, 0, 1);
				float attractForce = Mathf.Lerp(AttractForce * 0.38f, AttractForce, t);
				if (inPullOnlyPhase)
				{
					attractForce *= PullBurstMultiplier;
				}

				// 关键修复：增加确定性的位置牵引，避免敌人移动逻辑覆盖速度后“看起来吸不动”。
				float pullPixelsPerSecond = Mathf.Lerp(PullMinPixelsPerSecond, Mathf.Max(PullMinPixelsPerSecond, AttractForce), t);
				if (inPullOnlyPhase)
				{
					pullPixelsPerSecond *= PullBurstMultiplier;
				}

				float step = pullPixelsPerSecond * dt;
				// 直接位移牵引：避免二次平滑后位移过小，导致“看起来完全不吸”。
				actor.GlobalPosition = actor.GlobalPosition.MoveToward(GlobalPosition, step);
				
				// 应用速度拉扯（作用于CharacterBody2D或RigidBody2D）
				if (actor is CharacterBody2D characterBody)
				{
					characterBody.Velocity += direction * attractForce * dt;
					characterBody.Velocity *= PullVelocityDamping;

					if (distance <= CenterSnapRadius * 1.5f)
					{
						characterBody.GlobalPosition = characterBody.GlobalPosition.Lerp(GlobalPosition, 0.5f);
						characterBody.Velocity *= CenterSnapDamping;
					}
				}
				else if (actor.GetParent() is RigidBody2D rigidBody)
				{
					rigidBody.ApplyCentralForce(direction * attractForce);
					rigidBody.LinearVelocity *= PullVelocityDamping;

					if (distance <= CenterSnapRadius * 1.5f)
					{
						rigidBody.GlobalPosition = rigidBody.GlobalPosition.Lerp(GlobalPosition, 0.45f);
						rigidBody.LinearVelocity *= CenterSnapDamping;
					}
				}
			}
		}
		
		/// <summary>
		/// 对被吸引的敌人造成伤害
		/// </summary>
		private void DealDamageToAttractedActors()
		{
			var attractedList = CollectActorsInRadius();
			
			foreach (var actor in attractedList)
			{
				// 计算本次伤害量
				int damageThisTick = Mathf.Max(1, Mathf.RoundToInt(DamagePerSecond * (float)TickInterval));
				
				actor.TakeDamage(damageThisTick, GlobalPosition, _sourceActor);
				GameLogger.Debug(nameof(GravityGrenadeBlackHole),
					$"黑洞对 {actor.Name} 造成 {damageThisTick} 伤害");
			}
		}

		private void UpdateVisualDeformation()
		{
			if (_spriteBlackHole == null)
			{
				return;
			}

			float t = (float)_lifeTimer;
			float introFactor = Mathf.Clamp(IntroDuration > 0f ? t / IntroDuration : 1f, 0f, 1f);
			introFactor = introFactor * introFactor * (3f - 2f * introFactor); // smoothstep

			float outroFactor = 1f;
			if (_lifeTimer >= _resolvedOutroStart)
			{
				float outroT = Mathf.Clamp((float)((_lifeTimer - _resolvedOutroStart) / Mathf.Max(0.01f, OutroDuration)), 0f, 1f);
				outroFactor = Mathf.Pow(1f - outroT, CollapsePower);
			}

			float lifeScale = introFactor * outroFactor;
			float squash = Mathf.Sin(t * SquashFrequency) * SquashAmount;
			float pulse = Mathf.Sin(t * 2.1f) * (PulsingScalePercent / 100f);

			float sx = (1f + pulse) * (1f + squash);
			float sy = (1f + pulse) * (1f - squash * 0.7f);
			_spriteBlackHole.Scale = new Vector2(_baseScale.X * sx, _baseScale.Y * sy) * lifeScale;

			float rotA = Mathf.Sin(t * JitterFrequency);
			float rotB = Mathf.Sin(t * (JitterFrequency * 0.61f) + 1.7f);
			_spriteBlackHole.RotationDegrees = (rotA + rotB) * 0.5f * JitterRotationDegrees;

			_spriteBlackHole.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(lifeScale, 0f, 1f));
		}
		
		/// <summary>
		/// 设置发射方Actor
		/// </summary>
		public void SetSourceActor(GameActor? actor)
		{
			_sourceActor = actor;
		}

		private List<GameActor> CollectActorsInRadius()
		{
			var actors = new List<GameActor>();
			if (GetTree() == null)
			{
				return actors;
			}

			var seen = new HashSet<ulong>();

			var nodes = AffectEnemiesGroupOnly
				? GetTree().GetNodesInGroup("enemies")
				: GetTree().GetNodesInGroup("actors");

			foreach (Node node in nodes)
			{
				if (node is not GameActor actor)
				{
					continue;
				}

				if (!IsAttractableTarget(actor))
				{
					continue;
				}

				if (!IsPointInsideAttractZone(actor.GlobalPosition))
				{
					continue;
				}

				if (seen.Add(actor.GetInstanceId()))
				{
					actors.Add(actor);
				}
			}

			// 兜底：有些敌人未正确进入 enemies 组，或组绑定时序异常。
			if (UseBroadActorFallback && (actors.Count == 0 || AffectEnemiesGroupOnly))
			{
				Node? root = GetTree().CurrentScene;
				if (root != null)
				{
					CollectActorsRecursive(root, actors, seen);
				}
			}

			// 最终兜底：物理圆形查询，确保“从中心走过”的目标不会漏检。
			CollectActorsByPhysicsQuery(actors, seen);

			return actors;
		}

		private void CollectActorsByPhysicsQuery(List<GameActor> actors, HashSet<ulong> seen)
		{
			var world = GetWorld2D();
			if (world == null)
			{
				return;
			}

			if (_queryShape == null)
			{
				SyncAttractShapeCache();
			}

			Shape2D shape = _queryShape ?? new CircleShape2D { Radius = Mathf.Max(AttractRadius, 1f) };
			Transform2D transform = _attractCollisionShape?.GlobalTransform ?? new Transform2D(0f, GlobalPosition);
			var query = new PhysicsShapeQueryParameters2D
			{
				Shape = shape,
				Transform = transform,
				CollisionMask = uint.MaxValue,
				CollideWithBodies = true,
				CollideWithAreas = false
			};

			foreach (var hit in world.DirectSpaceState.IntersectShape(query, 64))
			{
				if (!hit.TryGetValue("collider", out Variant colliderVariant) || colliderVariant.VariantType != Variant.Type.Object)
				{
					continue;
				}

				var colliderObj = colliderVariant.As<GodotObject>();
				GameActor? actor = colliderObj as GameActor;
				if (actor == null && colliderObj is Node node)
				{
					Node? current = node;
					while (current != null && actor == null)
					{
						actor = current as GameActor;
						current = current.GetParent();
					}
				}

				if (actor == null || !IsAttractableTarget(actor))
				{
					continue;
				}

				if (seen.Add(actor.GetInstanceId()))
				{
					actors.Add(actor);
				}
			}
		}

		private void CollectActorsRecursive(Node node, List<GameActor> actors, HashSet<ulong> seen)
		{
			if (node is GameActor actor && IsAttractableTarget(actor))
			{
				if (IsPointInsideAttractZone(actor.GlobalPosition) && seen.Add(actor.GetInstanceId()))
				{
					actors.Add(actor);
				}
			}

			foreach (Node child in node.GetChildren())
			{
				CollectActorsRecursive(child, actors, seen);
			}
		}

		private bool IsAttractableTarget(GameActor actor)
		{
			if (!IsInstanceValid(actor) || actor == _sourceActor)
			{
				return false;
			}

			if (!AffectEnemiesGroupOnly)
			{
				return !actor.IsInGroup("player");
			}

			if (actor.IsInGroup("enemies"))
			{
				return true;
			}

			// 额外兼容：敌人常用碰撞层2（bit=1）。
			const uint enemyLayerMask = 1u << 1;
			return (actor.CollisionLayer & enemyLayerMask) != 0;
		}

		private void SyncAttractShapeCache()
		{
			if (_attractCollisionShape?.Shape == null)
			{
				_queryShape = new CircleShape2D { Radius = Mathf.Max(AttractRadius, 1f) };
				return;
			}

			_queryShape = _attractCollisionShape.Shape.Duplicate() as Shape2D;
			if (_queryShape == null)
			{
				_queryShape = _attractCollisionShape.Shape;
			}

			AttractRadius = Mathf.Max(1f, GetEffectiveAttractRadius());
		}

		private float GetEffectiveAttractRadius()
		{
			if (_attractCollisionShape?.Shape == null)
			{
				return AttractRadius;
			}

			Vector2 scale = _attractCollisionShape.GlobalScale;
			float sx = Mathf.Abs(scale.X);
			float sy = Mathf.Abs(scale.Y);

			return _attractCollisionShape.Shape switch
			{
				CircleShape2D circle => circle.Radius * Mathf.Max(sx, sy),
				RectangleShape2D rect => Mathf.Max(rect.Size.X * 0.5f * sx, rect.Size.Y * 0.5f * sy),
				CapsuleShape2D capsule => Mathf.Max(capsule.Radius * sx, (capsule.Height * 0.5f + capsule.Radius) * sy),
				_ => AttractRadius
			};
		}

		private bool IsPointInsideAttractZone(Vector2 globalPoint)
		{
			if (_attractCollisionShape?.Shape == null)
			{
				return GlobalPosition.DistanceTo(globalPoint) <= AttractRadius;
			}

			Vector2 local = _attractCollisionShape.GlobalTransform.AffineInverse() * globalPoint;

			switch (_attractCollisionShape.Shape)
			{
				case CircleShape2D circle:
					return local.LengthSquared() <= circle.Radius * circle.Radius;
				case RectangleShape2D rect:
					return Mathf.Abs(local.X) <= rect.Size.X * 0.5f && Mathf.Abs(local.Y) <= rect.Size.Y * 0.5f;
				case CapsuleShape2D capsule:
					float halfHeight = capsule.Height * 0.5f;
					float bodyHalf = Mathf.Max(0f, halfHeight - capsule.Radius);
					if (Mathf.Abs(local.X) <= capsule.Radius && Mathf.Abs(local.Y) <= bodyHalf)
					{
						return true;
					}
					Vector2 top = new Vector2(0f, -bodyHalf);
					Vector2 bottom = new Vector2(0f, bodyHalf);
					return local.DistanceSquaredTo(top) <= capsule.Radius * capsule.Radius
						|| local.DistanceSquaredTo(bottom) <= capsule.Radius * capsule.Radius;
				default:
					return GlobalPosition.DistanceTo(globalPoint) <= AttractRadius;
			}
		}
		
		public override void _ExitTree()
		{
			GameLogger.Info(nameof(GravityGrenadeBlackHole), $"[{Name}] 黑洞已消失");
		}
	}
}
