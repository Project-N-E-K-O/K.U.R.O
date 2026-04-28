using Godot;
using System.Collections.Generic;
using Kuros.Actors;
using Kuros.Core;

namespace Kuros.Effects
{
	/// <summary>
	/// 重力牵引场效果（Node2D）
	/// 落地后持续吸附周围敌人并造成伤害，到 FadeOutDuration 前开始淡出。
	/// </summary>
	[GlobalClass]
	public partial class GravityPullFieldEffect : Node2D
	{
		[Export(PropertyHint.Range, "1,30,0.5")] public float Duration { get; set; } = 6f;
		[Export(PropertyHint.Range, "0,5,0.1")] public float FadeOutDuration { get; set; } = 0.8f;
		[Export(PropertyHint.Range, "50,800,10")] public float PullRadius { get; set; } = 300f;
		[Export(PropertyHint.Range, "1,100,1")] public float DamagePerSecond { get; set; } = 10f;
		[Export(PropertyHint.Range, "100,3000,50")] public float PullForce { get; set; } = 600f;

		private double _lifeTimer = 0.0;
		private double _damageTimer = 0.0;
		private const float DamageInterval = 0.25f;
		private const uint EnemyLayerMask = 1u << 1;

		private GameActor? _sourceActor;

		public void SetSourceActor(GameActor? actor) => _sourceActor = actor;

		public override void _PhysicsProcess(double delta)
		{
			_lifeTimer += delta;
			_damageTimer += delta;

			if (_lifeTimer >= Duration)
			{
				QueueFree();
				return;
			}

			PullNearbyEnemies((float)delta);

			if (_damageTimer >= DamageInterval)
			{
				_damageTimer = 0.0;
				DamageNearbyEnemies();
			}

			UpdateFadeOut();
		}

		private void PullNearbyEnemies(float delta)
		{
			foreach (var actor in CollectTargets())
			{
				Vector2 dir = (GlobalPosition - actor.GlobalPosition).Normalized();
				float dist = GlobalPosition.DistanceTo(actor.GlobalPosition);
				float t = 1f - Mathf.Clamp(dist / Mathf.Max(PullRadius, 1f), 0f, 1f);
				float force = Mathf.Lerp(PullForce * 0.4f, PullForce, t);

				actor.GlobalPosition = actor.GlobalPosition.MoveToward(GlobalPosition, force * delta);

				if (actor is CharacterBody2D cb)
				{
					cb.Velocity += dir * force * delta;
				}
			}
		}

		private void DamageNearbyEnemies()
		{
			int dmg = Mathf.Max(1, Mathf.RoundToInt(DamagePerSecond * DamageInterval));
			foreach (var actor in CollectTargets())
			{
				actor.TakeDamage(dmg, GlobalPosition, _sourceActor);
			}
		}

		private void UpdateFadeOut()
		{
			float outroStart = Mathf.Max(0f, Duration - FadeOutDuration);
			if (_lifeTimer < outroStart)
			{
				return;
			}

			float t = Mathf.Clamp((float)((_lifeTimer - outroStart) / Mathf.Max(0.01f, FadeOutDuration)), 0f, 1f);
			Modulate = new Color(1f, 1f, 1f, 1f - t);
		}

		private List<GameActor> CollectTargets()
		{
			var result = new List<GameActor>();
			if (GetTree() == null)
			{
				return result;
			}

			foreach (Node node in GetTree().GetNodesInGroup("enemies"))
			{
				if (node is GameActor actor
					&& IsInstanceValid(actor)
					&& actor != _sourceActor
					&& GlobalPosition.DistanceTo(actor.GlobalPosition) <= PullRadius)
				{
					result.Add(actor);
				}
			}

			return result;
		}
	}
}
