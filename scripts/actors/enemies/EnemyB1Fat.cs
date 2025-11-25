using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Controllers;

public partial class EnemyB1Fat : SampleEnemy
{
    [Export(PropertyHint.Range, "0.1,10,0.1")] public float HitWindowSeconds = 2f;
    [Export(PropertyHint.Range, "0.1,5,0.1")] public float FreezeOnHitDuration = 0.5f;
    [Export(PropertyHint.Range, "1,10,1")] public int HitsToFreeze = 2;

    private HitTracker _hitTracker = new();

    public override void _Ready()
    {
        base._Ready();
        _hitTracker = new HitTracker();
    }

    public override void TakeDamage(int damage)
    {
        base.TakeDamage(damage);
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

