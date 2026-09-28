using System;
using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 自动销毁特效：播放完动画（或延迟 DestroyDelay 秒）后自动销毁自身。
	/// 衍生特效生成时机由 SpawnDelay 控制：0 = 销毁时生成（默认），N&gt;0 = 动画开始 N 秒后生成。
	/// 通用动画类特效的挂载器：负责翻转、播放、延迟/动画结束销毁、衍生特效。
	/// </summary>
	public partial class EffectAutoDestroy : AnimatedSprite2D
	{
		/// <summary>是否面朝右。false 时水平镜像自身（配合角色朝向左）。</summary>
		public bool FacingRight { get; set; } = true;
		/// <summary>伤害来源（玩家），传递给衍生的伤害特效（BoomDmgEffect/LaserBeamPlayerWeapon）。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>在自身位置生成的特效场景列表（可多个）。生成时机由 SpawnDelay 决定。</summary>
		[Export] public PackedScene[] SpawnOnDestroyScenes { get; set; } = Array.Empty<PackedScene>();
		/// <summary>生成衍生特效时按自身缩放等比缩放衍生特效（fx.Scale ×= 自身 Scale）。默认关闭。</summary>
		[Export] public bool ScaleSpawnWithSelf { get; set; } = false;
		/// <summary>衍生特效生成延迟（秒）：0 = 销毁时生成（与销毁同帧）；N&gt;0 = 动画开始 N 秒后生成（不随销毁）。</summary>
		[Export(PropertyHint.Range, "0,10,0.01")] public float SpawnDelay { get; set; } = 0f;
		/// <summary>延迟销毁秒数。&gt;0 时用定时器销毁（不等动画播完）；0 时等动画播完销毁。</summary>
		[Export] public float DestroyDelay { get; set; } = 0f;
		/// <summary>为 true 时销毁 Owner（如挂载的特效根节点）而非自身，生成的子特效挂在 Owner 的父节点下。</summary>
		[Export] public bool QueueFreeOwner { get; set; } = false;

		public override void _Ready()
		{
			// 面朝左：水平镜像（翻转 X 缩放）
			if (!FacingRight)
				Scale = new Vector2(-Scale.X, Scale.Y);

			// 未在播放则手动播放
			if (!IsPlaying())
				Play();

			// 延迟销毁（定时器）或动画播放完成销毁
			if (DestroyDelay > 0f)
				GetTree().CreateTimer(DestroyDelay).Timeout += SpawnAndDestroy;
			else
				AnimationFinished += OnAnimationFinished;

			// SpawnDelay > 0：动画开始 N 秒后提前生成特效（不等销毁，生成后不再重复生成）
			if (SpawnDelay > 0f)
				GetTree().CreateTimer(SpawnDelay).Timeout += SpawnEffects;
		}

		private void OnAnimationFinished()
		{
			AnimationFinished -= OnAnimationFinished;
			SpawnAndDestroy();
		}

		/// <summary>生成衍生特效并销毁本体。SpawnDelay &gt; 0 时特效已提前生成，此处只销毁。</summary>
		private void SpawnAndDestroy()
		{
			if (SpawnDelay <= 0f)
				SpawnEffects();
			DestroySelf();
		}

		/// <summary>在自身当前位置生成所有衍生特效。</summary>
		private void SpawnEffects()
		{
			// 预热（ParticleEffectWarmer 全透明）时不生成衍生特效：
			// 衍生脚本（如 FadeInOutDestroy）会每帧驱动 modulate:a 覆盖继承的透明，导致预热时左上角闪现
			if (Modulate.A <= 0f) return;

			// 生成父节点：QueueFreeOwner 时挂在 Owner 的父级（衍生特效随场景层级走），否则挂自身父级
			Node? spawnParent = QueueFreeOwner
				? Owner?.GetParent() ?? GetParent()
				: GetParent();

			Vector2 spawnPos = GlobalPosition;

			foreach (var scene in SpawnOnDestroyScenes)
			{
				if (scene == null) continue;
				var fx = scene.Instantiate<Node2D>();
				spawnParent?.AddChild(fx);

				// 自身是"世界锚定"的（TopLevel，如落在滑槽机械旁的爆炸）→ 衍生特效同样锚定：
				// 否则它们会挂在 Mount 这类会移动的父节点下，跟着滑槽一起漂走
				if (TopLevel)
					fx.TopLevel = true;

				fx.GlobalPosition = spawnPos;

				// 继承自身调制：预热器（ParticleEffectWarmer）全透明预热时，衍生特效同样透明，
				// 避免预热结束后衍生特效在屏幕左上角闪现；正常游戏（自身 Modulate=1,1,1,1）无变化
				fx.Modulate *= Modulate;

				// 按自身缩放等比缩放衍生特效（保留衍生特效自身的原始比例，整体放大/缩小）
				if (ScaleSpawnWithSelf)
					fx.Scale *= Scale;

				// 把伤害来源传给衍生的伤害特效（使其能正确归属伤害）
				if (fx is BoomDmgEffect boom)
					boom.Attacker = Attacker;
				else if (fx is LaserBeamPlayerWeapon laser)
					laser.Attacker = Attacker;
			}
		}

		/// <summary>销毁本体：QueueFreeOwner 时销毁 Owner（自身可能只是挂载的动画），否则销毁自身。</summary>
		private void DestroySelf()
		{
			Node nodeToFree = QueueFreeOwner ? (Owner ?? (Node)this) : this;
			nodeToFree.QueueFree();
		}
	}
}
