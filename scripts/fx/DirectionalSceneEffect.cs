using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 通用方向性场景效果：根据生成者的朝向自动播放对应动画，支持在动画中生成子场景和自毁。
	///
	/// 方案 A（当前）：子级 AnimationPlayer 的方法调用轨道通过 NodePath("..") 找到根节点，
	/// 调用 SpawnAtMarker / DestroySelf。适用简单场景。
	///
	/// 方案 B（后续优化）：在根节点添加独立 AnimationPlayer，只放方法调用轨道，子级
	/// AnimationPlayer 专注动画。步骤：
	/// 1. 在场景根节点下新建 AnimationPlayer，animation_length 与子级动画一致
	/// 2. 将子级 AnimationPlayer 的方法调用轨道剪切到根 AnimationPlayer 的同名动画中
	/// 3. 轨道路径改为 .:SpawnAtMarker / .:DestroySelf（不再需要 ..）
	/// 4. 根 AnimationPlayer 加入 AnimationPlayers 数组，_Ready 自动同步播放
	/// </summary>
	public partial class DirectionalSceneEffect : Node2D, IFacingDirectional, IAttackerProvider
	{
		/// <summary>攻击来源（AttackEffectEntry 生成链自动注入）：转发给子级 ContactDamageBarrier 墙体的接触伤害。</summary>
		public GameActor? Attacker { get; set; }
		[ExportGroup("Animation")]
		/// <summary>单条指定动画：**非空则无视朝向固定播这一条**，优先于 AnimLeft/AnimRight。
		/// 适合"只有一段表演"的场景（如 rogueAI 表演体的 leave / move_down / wake_up）——
		/// AttackEffectEntry 的 PropertyOverrides 只写这一个字段即可指定；
		/// 留空则维持原双向行为：按 FacingRight 取 AnimRight / AnimLeft。</summary>
		[Export] public string AnimationName { get; set; } = "";
		[Export] public string AnimLeft { get; set; } = "SpawnLeft";
		[Export] public string AnimRight { get; set; } = "SpawnRight";
		/// <summary>
		/// 手动指定需要控制的 AnimationPlayer。留空则自动查找所有子节点中的 AnimationPlayer。
		/// </summary>
		[Export] public Array<AnimationPlayer> AnimationPlayers { get; set; } = new();

		[ExportGroup("Spawn")]
		[Export] public Array<AttackEffectEntry> SpawnEntries { get; set; } = new();

		[Export] public bool FacingRight { get; set; } = true;

		public override void _Ready()
		{
			var players = AnimationPlayers.Count > 0
				? AnimationPlayers
				: FindAnimationPlayers();

			string anim = string.IsNullOrEmpty(AnimationName)
				? (FacingRight ? AnimRight : AnimLeft)
				: AnimationName;
			foreach (var ap in players)
			{
				if (ap != null && IsInstanceValid(ap) && ap.HasAnimation(anim))
					ap.Play(anim);
			}

			// 接触伤害来源：转发给子级 ContactDamageBarrier（FireWallC 的六面墙）
			foreach (Node child in GetChildren())
				if (child is ContactDamageBarrier wall)
					wall.Attacker = Attacker;
		}

		public void DestroySelf()
		{
			QueueFree();
		}

		/// <summary>生成全部 SpawnEntries 到指定 Marker（动画 method track 单次调用生成所有条目，
		/// 避免多个 method 调用在编辑器保存时被覆盖丢失）。</summary>
		public void SpawnAllEntries(string markerName)
		{
			var marker = FindChild(markerName, recursive: true, owned: false) as Marker2D;
			if (marker == null) return;

			for (int i = 0; i < SpawnEntries.Count; i++)
				SpawnEntryAtMarker(i, marker);
		}

		public void SpawnAtMarker(string encoded)
		{
			int entryIndex = 0;
			string markerName = encoded;
			int colon = encoded.LastIndexOf(':');
			if (colon > 0 && int.TryParse(encoded[(colon + 1)..], out int idx))
			{
				markerName = encoded[..colon];
				entryIndex = idx;
			}

			var marker = FindChild(markerName, recursive: true, owned: false) as Marker2D;
			if (marker == null) return;

			SpawnEntryAtMarker(entryIndex, marker);
		}

		private void SpawnEntryAtMarker(int entryIndex, Marker2D marker)
		{
			if (entryIndex < 0 || entryIndex >= SpawnEntries.Count) return;
			var entry = SpawnEntries[entryIndex];
			if (entry?.Scene == null) return;

			var instance = entry.Scene.Instantiate();
			entry.ApplyOverrides(instance);
			// 唯一性组标记（同 EnemyAttackTemplate.SpawnSingleEffect）：生成的子场景（如召唤的敌人）入组，
			// 供"场上已有该组存活成员"检测（BlockedByFxGroup/UniqueGroup 阻塞重复召唤）
			if (!string.IsNullOrEmpty(entry.UniqueGroup))
				instance.AddToGroup(entry.UniqueGroup);
			GetParent()?.AddChild(instance);
			if (instance is Node2D node)
				node.GlobalPosition = marker.GlobalPosition;
		}

		private Array<AnimationPlayer> FindAnimationPlayers()
		{
			var list = new Array<AnimationPlayer>();
			foreach (var child in GetChildren())
			{
				if (child is AnimationPlayer ap)
					list.Add(ap);
				else
					CollectAnimationPlayers(child, list);
			}
			return list;
		}

		private static void CollectAnimationPlayers(Node node, Array<AnimationPlayer> list)
		{
			foreach (var child in node.GetChildren())
			{
				if (child is AnimationPlayer ap)
					list.Add(ap);
				CollectAnimationPlayers(child, list);
			}
		}
	}
}
