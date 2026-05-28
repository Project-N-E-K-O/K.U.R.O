using System;
using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// 特效自动销毁脚本。
	/// - 挂载在 AnimatedSprite2D 上
	/// - 生成时由外部赋值 FacingRight，_Ready 中一次性设定翻转
	/// - 动画完成后（或经过 DestroyDelay 秒后）可选生成额外场景，然后销毁自身
	/// </summary>
	public partial class EffectAutoDestroy : AnimatedSprite2D
	{
		/// <summary>
		/// 由生成方在 AddChild 之前或之后赋值（同 LaserBeam 模式）。
		/// true = 朝右（不翻转），false = 朝左（翻转 X）。
		/// </summary>
		public bool FacingRight { get; set; } = true;

		/// <summary>
		/// 销毁时依次生成的场景列表（Node2D 根节点），生成位置与自身 GlobalPosition 相同。
		/// 留空则不生成任何附加场景。
		/// </summary>
		[Export] public PackedScene[] SpawnOnDestroyScenes { get; set; } = Array.Empty<PackedScene>();

		/// <summary>
		/// 若 > 0，经过此秒数后触发生成并销毁，忽略动画完成事件。
		/// 若 = 0（默认），保持原有行为：动画播放完毕时触发。
		/// </summary>
		[Export] public float DestroyDelay { get; set; } = 0f;

		/// <summary>
		/// 为 true 时，销毁整个场景根节点（Owner），并将生成的特效挂在根节点的父节点下。
		/// 适用于本脚本挂载在子节点（如 AnimatedSprite2D）上、需要销毁整个 PackedScene 的情况。
		/// </summary>
		[Export] public bool QueueFreeOwner { get; set; } = false;

		public override void _Ready()
		{
			// 根据朝向一次性翻转
			if (!FacingRight)
				Scale = new Vector2(-Scale.X, Scale.Y);

			// 确保动画正在播放
			if (!IsPlaying())
				Play();

			if (DestroyDelay > 0f)
				GetTree().CreateTimer(DestroyDelay).Timeout += SpawnAndDestroy;
			else
				AnimationFinished += OnAnimationFinished;
		}

		private void OnAnimationFinished()
		{
			AnimationFinished -= OnAnimationFinished;
			SpawnAndDestroy();
		}

		private void SpawnAndDestroy()
		{
			// 决定特效的父节点：QueueFreeOwner 模式下挂到 Owner 的父节点（世界层），否则挂到自身父节点
			Node? spawnParent = QueueFreeOwner
				? Owner?.GetParent() ?? GetParent()
				: GetParent();

			Vector2 spawnPos = GlobalPosition;

			foreach (var scene in SpawnOnDestroyScenes)
			{
				if (scene == null) continue;
				var fx = scene.Instantiate<Node2D>();
				spawnParent?.AddChild(fx);
				fx.GlobalPosition = spawnPos;
			}

			// QueueFreeOwner 时销毁整个场景根节点，否则只销毁自身
			Node nodeToFree = QueueFreeOwner ? (Owner ?? (Node)this) : this;
			nodeToFree.QueueFree();
		}
	}
}
