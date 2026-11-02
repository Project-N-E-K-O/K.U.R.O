using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Effects
{
	/// <summary>
	/// 小型干扰区（世界空间危险区）：玩家进入 → 结算一次伤害，区域随后快速淡出。
	/// 视觉由 Visuals 下**多个独立视觉层叠加**（不同 seed/尺度/偏移/自转/伪 3D 倾角），
	/// 每层只绘制随机激活的小方块，方块内采样屏幕纹理做局部错位扭曲；方块外透明。
	/// 碰撞与伤害判定仍是单个圆形 Area2D。
	/// 目标发现用 Area2D 信号（EFFECT_STANDARD 模式 B），伤害走 DamageDispatcher；
	/// 攻击来源由生成方（InterferenceHazardField）通过 <see cref="IAttackerProvider"/> 注入。
	/// </summary>
	[GlobalClass]
	public partial class InterferenceZone : Node2D, IAttackerProvider
	{
		[ExportCategory("Visual")]
		/// <summary>视觉基础尺寸（半宽，像素）。**只影响视觉层**，与伤害判定无关。</summary>
		[Export(PropertyHint.Range, "40,800,5")] public float Radius { get; set; } = 150f;
		/// <summary>视觉宽高比（相对 Radius 的半宽/半高倍率）。**只影响视觉层**。</summary>
		[Export] public Vector2 Aspect { get; set; } = new(1f, 0.72f);

		[ExportCategory("HitBox")]
		/// <summary>独立伤害判定矩形尺寸（像素）：与视觉层 Radius/Aspect/scale 完全解耦。</summary>
		[Export] public Vector2 HitBoxSize { get; set; } = new(300f, 216f);
		/// <summary>独立伤害判定矩形的中心偏移（像素，相对 zone 原点）。</summary>
		[Export] public Vector2 HitBoxOffset { get; set; } = Vector2.Zero;
		/// <summary>目标发现物理层（默认 Player 层 = 4）。</summary>
		[Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask { get; set; } = 4;
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.Player;
		[Export] public bool AllowSelfDamage { get; set; } = false;

		[ExportCategory("Damage")]
		[Export(PropertyHint.Range, "0,200,1")] public int Damage { get; set; } = 20;

		[ExportCategory("Lifecycle")]
		[Export(PropertyHint.Range, "0.2,10,0.1")] public float Lifetime { get; set; } = 4f;
		[Export(PropertyHint.Range, "0,1,0.05")] public float FadeInDuration { get; set; } = 0.15f;
		[Export(PropertyHint.Range, "0,1,0.05")] public float FadeOutDuration { get; set; } = 0.4f;
		/// <summary>被踩中后剩余的淡出时间（小于上面两项时取更短）。</summary>
		[Export(PropertyHint.Range, "0.05,1,0.05")] public float TriggerFadeDuration { get; set; } = 0.35f;

		[ExportCategory("Fullscreen 全屏干扰")]
		/// <summary>命中时生成的全屏干扰场景（ScreenInterferenceEffect）。空 = 只扣血不触发全屏。</summary>
		[Export] public PackedScene? FullScreenEffectScene { get; set; }
		/// <summary>全屏干扰去重组：同一时间只保留一个，多个 zone 同时被踩不会叠层采样。</summary>
		[Export] public string FullScreenGroup { get; set; } = "fx_rogueai_screen_interference";

		[ExportCategory("Warning 预警")]
		/// <summary>生成后先显示落点预警的时长（秒）：预警期间 zone 视觉隐藏、Area2D 关闭，
		/// 结束后才显示视觉并开启伤害判定。0 = 不预警。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float WarningDuration { get; set; } = 1f;
		/// <summary>落点预警指示器场景（复用导弹的 LandingIndicatorA）。空 = 只等待、无视觉预警。</summary>
		[Export] public PackedScene? WarningIndicatorScene { get; set; }
		/// <summary>预警指示器额外偏移（像素，叠加在 HitBoxOffset 之上）：微调落点提示的位置。</summary>
		[Export] public Vector2 WarningOffset { get; set; } = Vector2.Zero;
		/// <summary>预警指示器缩放。Zero = 自动按 HitBoxSize 贴合判定框；非零 = 直接使用该缩放。
		/// （WarmingB 根节点 scale：其 AnimatedSprite2D 自带的 0.33 会继续生效）</summary>
		[Export] public Vector2 WarningScale { get; set; } = Vector2.Zero;
		/// <summary>预警指示器自身生命周期（秒）。0 = 与 WarningDuration 一致（推荐）；
		/// &gt;0 可让指示器提前消失，但落点预警会随之缺失尾部。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float WarningIndicatorDuration { get; set; } = 0f;
		/// <summary>预警指示器 z_index（覆盖 prefab 自带值）。</summary>
		[Export] public int WarningZIndex { get; set; } = 3;
		/// <summary>预警指示器 z_as_relative：false = 绝对层级（默认）；true = 相对其父节点。</summary>
		[Export] public bool WarningZAsRelative { get; set; } = false;

		/// <summary>攻击来源（由生成方在 AddChild 前后注入，随场景模板链路）。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>Visuals 下的单个视觉层：场景里摆好的基准变换，运行时按 Radius 缩放并做相位动画。</summary>
		private sealed class VisualLayer
		{
			public MeshInstance2D Node = null!;
			public ShaderMaterial Material = null!;
			public Vector2 BaseScale;
			public Vector2 BasePosition;
			public float BaseRotation;
			public float Weight;
		}

		private readonly List<VisualLayer> _layers = new();
		private const float VisualScaleFactor = 1.06f;
		private Area2D? _area;
		private CollisionShape2D? _shape;
		private Node2D? _visuals;
		private float _elapsed;
		private float _remaining;
		/// <summary>传给 shader 的自定义时间：用 _Process 的 delta（受 time_scale/暂停影响）累加，
		/// 替代不随 time_scale 缩放的 shader 内置 TIME。</summary>
		private float _shaderTime;
		private bool _triggered;
		/// <summary>预警是否结束（false = 预警中：视觉隐藏、Area2D 关闭、寿命不流逝）。</summary>
		private bool _armed;
		private bool _warningSpawned;
		private float _warningRemaining;
		private Node? _warningIndicator;
		/// <summary>Arm 后的首物理帧补一次重叠检测：预警期间玩家可能已经站在范围内。</summary>
		private bool _needsArmOverlapCheck;

		public override void _Ready()
		{
			_area = GetNodeOrNull<Area2D>("Area2D");
			_shape = _area?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");

			if (_area == null || _shape?.Shape is not RectangleShape2D)
			{
				GD.PushWarning($"[{nameof(InterferenceZone)}] 缺少 Area2D/RectangleShape2D，区域自毁");
				QueueFree();
				return;
			}

			CollectVisualLayers();
			_visuals = GetNodeOrNull<Node2D>("Visuals");

			// 命中判定与视觉完全解耦：尺寸/偏移只由 HitBoxSize / HitBoxOffset 决定，
			// 不读取任何视觉层的 scale / position / rect_margin。
			var rect = (RectangleShape2D)_shape.Shape.Duplicate();
			rect.Size = new Vector2(Mathf.Max(HitBoxSize.X, 1f), Mathf.Max(HitBoxSize.Y, 1f));
			_shape.Shape = rect;
			_shape.Position = HitBoxOffset;

			_area.CollisionLayer = 0;
			_area.CollisionMask = TargetCollisionMask;
			_area.Monitorable = false;

			// 预警期间：Area2D 关闭、视觉隐藏、寿命不流逝
			_armed = WarningDuration <= 0.001f;
			_area.Monitoring = _armed;
			_area.BodyEntered += OnBodyEntered;

			if (!_armed)
			{
				if (_visuals != null) _visuals.Visible = false;
				if (WarningIndicatorScene == null)
					GD.PushWarning($"[{nameof(InterferenceZone)}] WarningDuration>0 但未配置 WarningIndicatorScene：预警期间无视觉提示");
			}

			_remaining = Mathf.Max(Lifetime, 0.1f);
			UpdateLayers(0f);
		}

		public override void _Process(double delta)
		{
			if (!_armed)
			{
				TickWarning((float)delta);
				return;
			}

			_remaining -= (float)delta;
			if (_remaining <= 0f)
			{
				QueueFree();
				return;
			}

			_elapsed += (float)delta;
			_shaderTime += (float)delta;

			float strength = 1f;
			if (FadeInDuration > 0.001f)
				strength = Mathf.Min(strength, _elapsed / FadeInDuration);
			if (FadeOutDuration > 0.001f)
				strength = Mathf.Min(strength, _remaining / FadeOutDuration);
			UpdateLayers(Mathf.Clamp(strength, 0f, 1f));
		}

		/// <summary>预警计时：首帧生成落点指示器；计满后 Arm。</summary>
		private void TickWarning(float delta)
		{
			if (!_warningSpawned)
			{
				_warningSpawned = true;
				_warningRemaining = Mathf.Max(WarningDuration, 0f);
				SpawnWarningIndicator();
			}

			_warningRemaining -= delta;
			if (_warningRemaining <= 0f)
				Arm();
		}

		private void SpawnWarningIndicator()
		{
			if (WarningIndicatorScene == null) return;

			var indicator = WarningIndicatorScene.Instantiate<Node2D>();
			indicator.ZIndex = WarningZIndex;
			indicator.ZAsRelative = WarningZAsRelative;

			Vector2 warningOffset = HitBoxOffset + WarningOffset;
			float indicatorDuration = WarningIndicatorDuration > 0.001f
				? WarningIndicatorDuration
				: Mathf.Max(WarningDuration, 0.05f);

			// WarmingB 这类 FollowAndDestroy 预警：必须在入树（_Ready 读 Duration）之前重载参数
			var follow = indicator as FollowAndDestroy ?? indicator.GetNodeOrNull<FollowAndDestroy>(".");
			if (follow != null)
			{
				follow.Duration = indicatorDuration;
				follow.FollowAnchor = this;          // 跟随 zone，保持贴在落点
				follow.Offset = warningOffset;       // 判定框中心 + WarningOffset
				follow.Attacker = Attacker;          // 生成方死亡时自动清理
			}

			Node parent = GetParent() ?? GetTree().Root;
			parent.AddChild(indicator);
			indicator.GlobalPosition = ToGlobal(warningOffset);

			// 缩放：显式 WarningScale 优先；否则按 HitBoxSize 自动贴合
			//（WarmingB 的 AnimatedSprite2D 自带 0.33 缩放，自动模式下会算进有效贴图尺寸）
			if (WarningScale.X > 0.001f && WarningScale.Y > 0.001f)
			{
				indicator.Scale = WarningScale;
			}
			else
			{
				Vector2 textureSize = GetIndicatorTextureSize(indicator);
				if (textureSize.X > 1f && textureSize.Y > 1f)
					indicator.Scale = new Vector2(HitBoxSize.X / textureSize.X, HitBoxSize.Y / textureSize.Y);
			}

			var landing = indicator as LandingIndicator ?? indicator.GetNodeOrNull<LandingIndicator>(".");
			if (landing != null)
			{
				landing.WarningDuration = Mathf.Max(indicatorDuration, 0.05f);
				landing.Start();
			}

			_warningIndicator = indicator;
		}

		private static Vector2 GetIndicatorTextureSize(Node indicator)
		{
			if (indicator.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") is { SpriteFrames: not null } anim)
			{
				var texture = anim.SpriteFrames.GetFrameTexture(anim.Animation, 0);
				if (texture != null)
					return new Vector2(texture.GetSize().X * Mathf.Abs(anim.Scale.X),
						texture.GetSize().Y * Mathf.Abs(anim.Scale.Y));
			}
			if (indicator.GetNodeOrNull<Sprite2D>("Sprite2D") is { Texture: not null } sprite)
				return new Vector2(sprite.Texture.GetSize().X * Mathf.Abs(sprite.Scale.X),
					sprite.Texture.GetSize().Y * Mathf.Abs(sprite.Scale.Y));
			return Vector2.Zero;
		}

		/// <summary>预警结束：显示视觉、开启判定、开始寿命；玩家若已在范围内需补一次命中。</summary>
		private void Arm()
		{
			_armed = true;
			_warningSpawned = true;
			_warningRemaining = 0f;
			if (_warningIndicator != null && GodotObject.IsInstanceValid(_warningIndicator))
				_warningIndicator.QueueFree();
			_warningIndicator = null;

			if (_visuals != null) _visuals.Visible = true;
			if (_area != null) _area.Monitoring = true;

			_elapsed = 0f;
			_shaderTime = 0f;
			_remaining = Mathf.Max(Lifetime, 0.1f);
			_needsArmOverlapCheck = true;
			UpdateLayers(0f);
		}

		public override void _PhysicsProcess(double delta)
		{
			if (!_needsArmOverlapCheck) return;
			_needsArmOverlapCheck = false;
			if (_triggered || _area == null) return;

			foreach (var body in _area.GetOverlappingBodies())
			{
				OnBodyEntered(body);
				if (_triggered) break;
			}
		}

		/// <summary>收集 Visuals 下的 MeshInstance2D 层：各自独立材质（不同 seed/参数），
		/// 场景里写的 scale/position/rotation 作为基准（position 以 Radius 为单位）。</summary>
		private void CollectVisualLayers()
		{
			var visuals = GetNodeOrNull<Node2D>("Visuals");
			if (visuals == null) return;

			foreach (Node child in visuals.GetChildren())
			{
				if (child is not MeshInstance2D mesh) continue;
				if (mesh.Material is not ShaderMaterial shared) continue;

				var material = (ShaderMaterial)shared.Duplicate();
				mesh.Material = material;

				float weight = Mathf.Clamp(mesh.Scale.X, 0.35f, 1f);
				_layers.Add(new VisualLayer
				{
					Node = mesh,
					Material = material,
					BaseScale = mesh.Scale,
					BasePosition = mesh.Position,
					BaseRotation = mesh.Rotation,
					Weight = weight,
				});
			}

			if (_layers.Count == 0)
				GD.PushWarning($"[{nameof(InterferenceZone)}] Visuals 下没有 MeshInstance2D 视觉层");
		}

		private void UpdateLayers(float fade)
		{
			float radius = Mathf.Max(Radius, 1f);
			Vector2 aspect = new(Mathf.Max(Aspect.X, 0.05f), Mathf.Max(Aspect.Y, 0.05f));
			foreach (var layer in _layers)
			{
				// 矩形区：Position 直接使用场景里摆好的像素偏移（不再乘 Radius）；
				// 只按 Radius × 宽高比缩放，不再漂移/自转/呼吸。
				layer.Node.Position = layer.BasePosition;
				layer.Node.Rotation = layer.BaseRotation;
				layer.Node.Scale = layer.BaseScale * radius * VisualScaleFactor * aspect;
				float layerStrength = Mathf.Clamp(fade * layer.Weight, 0f, 1f);
				layer.Material.SetShaderParameter("strength", layerStrength);
				layer.Material.SetShaderParameter("effect_time", _shaderTime);
				// glitch 层在 strength=0 时输出透明（shader 里 discard），但整块节点隐藏更稳；
				// zone 层同时避免 alpha=0 的无效绘制。
				layer.Node.Visible = layerStrength > 0.001f;
			}
		}

		private void OnBodyEntered(Node2D body)
		{
			if (_triggered || body is not GameActor) return;

			bool accepted = true;
			if (Damage > 0)
			{
				accepted = DamageDispatcher.DealDamage(
					body, Damage, GlobalPosition, Attacker,
					DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage);
			}
			if (!accepted) return;

			_triggered = true;
			if (_area != null) _area.SetDeferred(Area2D.PropertyName.Monitoring, false);
			_remaining = Mathf.Min(_remaining, Mathf.Max(TriggerFadeDuration, 0.05f));
			TriggerFullScreen();
		}

		/// <summary>伤害真正落地后触发一次全屏干扰；同组已有存活实例时跳过，避免多个 zone 同帧叠层。</summary>
		private void TriggerFullScreen()
		{
			if (FullScreenEffectScene == null) return;
			if (!string.IsNullOrEmpty(FullScreenGroup) && GetTree().GetNodesInGroup(FullScreenGroup).Count > 0)
				return;

			var fx = FullScreenEffectScene.Instantiate<Node2D>();
			Node parent = GetParent() ?? GetTree().Root;
			parent.AddChild(fx);
			if (!string.IsNullOrEmpty(FullScreenGroup))
				fx.AddToGroup(FullScreenGroup);
			fx.GlobalPosition = GlobalPosition;
		}
	}
}
