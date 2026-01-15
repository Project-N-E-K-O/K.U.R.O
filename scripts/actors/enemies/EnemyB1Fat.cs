using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Controllers;
using Kuros.Actors.Enemies.Attacks;

public partial class EnemyB1Fat : SampleEnemy
{
	[Export(PropertyHint.Range, "0.1,10,0.1")] public float HitWindowSeconds = 2f;
	[Export(PropertyHint.Range, "0.1,5,0.1")] public float FreezeOnHitDuration = 0.5f;
	[Export(PropertyHint.Range, "1,10,1")] public int HitsToFreeze = 2;
	[Export(PropertyHint.Range, "0,5,0.1")] public float SimpleAttackWarmupSeconds = 1f;
	[Export] public NodePath SimpleAttackNodePath = new("StateMachine/Attack/AttackController/SimpleMeleeAttack");

	private HitTracker _hitTracker = new();
	private EnemySimpleMeleeAttack? _simpleMeleeAttack;

	public override void _Ready()
	{
		base._Ready();
		_hitTracker = new HitTracker();
		ResolveSimpleAttack();
		ApplySimpleAttackTuning();
	}

	public override void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)
	{
		base.TakeDamage(damage, attackOrigin, attacker);
		if (EffectController == null) return;

		_hitTracker.RegisterHit();
		if (_hitTracker.ShouldFreeze(HitsToFreeze, HitWindowSeconds))
		{
			ApplyFreezeEffect();
			_hitTracker.Reset();
		}
	}

	private void ApplyFreezeEffect()
	{
		if (EffectController == null) return;

		var freezeEffect = new FreezeEffect
		{
			FrozenStateName = "Frozen",
			FallbackStateName = "Walk",
			Duration = FreezeOnHitDuration,
			EffectId = $"b1_fat_hit_freeze_{GetInstanceId()}",
			ResumePreviousState = true
		};

		ApplyEffect(freezeEffect);
	}

	private void ResolveSimpleAttack()
	{
		_simpleMeleeAttack = GetNodeOrNull<EnemySimpleMeleeAttack>(SimpleAttackNodePath);
	}

	private void ApplySimpleAttackTuning()
	{
		if (_simpleMeleeAttack == null) return;
		_simpleMeleeAttack.WarmupDuration = Mathf.Max(SimpleAttackWarmupSeconds, 0f);
	}

	private class HitTracker
	{
		private readonly System.Collections.Generic.Queue<double> _timestamps = new();

		public void RegisterHit()
		{
			_timestamps.Enqueue(Time.GetTicksMsec() / 1000.0);
		}

		public bool ShouldFreeze(int hitCount, float windowSeconds)
		{
			double now = Time.GetTicksMsec() / 1000.0;
			while (_timestamps.Count > 0 && now - _timestamps.Peek() > windowSeconds)
			{
				_timestamps.Dequeue();
			}

			return _timestamps.Count >= hitCount;
		}

		public void Reset()
		{
			_timestamps.Clear();
		}
	}
}
