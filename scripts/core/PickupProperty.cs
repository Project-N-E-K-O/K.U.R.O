using Godot;

namespace Kuros.Core
{
	/// <summary>
	/// 拾取属性基类 - 可以被角色拾取的物品
	/// </summary>
	public abstract partial class PickupProperty : Node2D
	{
		/// <summary>
		/// 当被拾取时调用
		/// </summary>
		protected virtual void OnPicked(GameActor actor)
		{
			GD.Print($"{Name} picked up by {actor.Name}");
		}
	}
}

