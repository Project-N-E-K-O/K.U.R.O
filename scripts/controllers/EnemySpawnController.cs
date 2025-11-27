using Godot;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Controllers
{
	/// <summary>
	/// 管理作为子节点放置的敌人实例，负责运行时初始化与可选复活。
	/// </summary>
	[Tool]
	public partial class EnemySpawnController : Node2D
	{
		[Export] public bool AutoRespawn = false;
		[Export(PropertyHint.Range, "0.5,60,0.5")] public float RespawnDelay = 5f;

		private readonly List<GameActor> _managedActors = new();

		public override void _Ready()
		{
			if (Engine.IsEditorHint()) return;

			foreach (var child in GetChildren())
			{
				if (child is GameActor actor)
				{
					InitializeActor(actor);
				}
			}

			ChildEnteredTree += OnChildEnteredTree;
		}

		public override void _ExitTree()
		{
			if (!Engine.IsEditorHint())
			{
				ChildEnteredTree -= OnChildEnteredTree;
			}

			base._ExitTree();
		}

		private void InitializeActor(GameActor actor)
		{
			_managedActors.Add(actor);
			actor.TreeExited += () => HandleActorExited(actor);
		}

		private void OnChildEnteredTree(Node node)
		{
			if (Engine.IsEditorHint()) return;
			if (node is GameActor actor && !_managedActors.Contains(actor))
			{
				InitializeActor(actor);
			}
		}

		private void HandleActorExited(GameActor actor)
		{
			_managedActors.Remove(actor);

			if (!AutoRespawn || string.IsNullOrEmpty(actor.SceneFilePath)) return;

			var timer = GetTree().CreateTimer(RespawnDelay);
			timer.Timeout += () =>
			{
				if (!IsInstanceValid(this)) return;

				var scene = GD.Load<PackedScene>(actor.SceneFilePath);
				if (scene == null) return;

				if (scene.Instantiate() is GameActor newActor)
				{
					AddChild(newActor);
					newActor.GlobalPosition = actor.GlobalPosition;
					InitializeActor(newActor);
				}
			};
		}
	}
}
