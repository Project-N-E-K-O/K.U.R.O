using Godot;

namespace Kuros.Core
{
	/// <summary>
	/// 可丢弃的拾取属性基类 - 可以被拾取和丢弃的物品
	/// </summary>
	public abstract partial class DroppablePickupProperty : PickupProperty
	{
		[Export] public Vector2 DropWorldOffset { get; set; } = Vector2.Zero;

		/// <summary>
		/// 当被放下时调用
		/// </summary>
		protected virtual void OnPutDown(GameActor actor)
		{
			GD.Print($"{Name} put down by {actor.Name}");
		}

		/// <summary>
		/// 获取丢弃物品的父节点（通常是场景根节点或特定的容器）
		/// </summary>
		protected Node2D GetDropParent(GameActor actor)
		{
			// 首先尝试获取当前场景
			var currentScene = GetTree().CurrentScene;
			if (currentScene is Node2D sceneNode2D)
			{
				// 尝试查找 BattleScene 或类似的场景节点
				var battleScene = sceneNode2D.GetNodeOrNull<Node2D>("BattleScene");
				if (battleScene != null)
				{
					return battleScene;
				}
				
				return sceneNode2D;
			}
			
			// 如果当前场景不是 Node2D，尝试从根节点查找
			var root = GetTree().Root;
			if (root != null)
			{
				// 尝试查找 BattleScene
				var battleScene = root.GetNodeOrNull<Node2D>("BattleScene");
				if (battleScene != null)
				{
					return battleScene;
				}
				
				// 尝试获取根节点的第一个子节点（通常是场景）
				if (root.GetChildCount() > 0)
				{
					var firstChild = root.GetChild(0);
					if (firstChild is Node2D firstChildNode2D)
					{
						return firstChildNode2D;
					}
				}
			}
			
			// 如果都找不到，返回当前节点（如果它是 Node2D）
			return this is Node2D ? (Node2D)this : GetTree().CurrentScene as Node2D ?? actor;
		}
	}
}

