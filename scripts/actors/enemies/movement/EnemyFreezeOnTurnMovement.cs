using Godot;
using Kuros.Core.Effects;

/// <summary>
/// 在基础寻敌功能上，转身时会给敌人施加冷却冻结。
/// </summary>
public partial class EnemyFreezeOnTurnMovement : EnemyChaseMovement
{
    [Export(PropertyHint.Range, "0.1,5,0.1")] public float FreezeDuration = 0.5f;
    [Export] public bool ApplyFreezeOnlyWhenFacingPlayer = true;

    private bool _wasFacingRight = true;
    private bool _initializedFacing = false;

    public override void _Ready()
    {
        base._Ready();
        if (Enemy != null)
        {
            _wasFacingRight = Enemy.FacingRight;
            _initializedFacing = true;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Enemy == null) return;

        bool facingBefore = Enemy.FacingRight;
        base._PhysicsProcess(delta);

        if (!_initializedFacing)
        {
            _wasFacingRight = Enemy.FacingRight;
            _initializedFacing = true;
            return;
        }

        if (Enemy.FacingRight != _wasFacingRight)
        {
            if (ShouldApplyFreeze(Enemy))
            {
                ApplyFreezeEffect(Enemy);
            }
            _wasFacingRight = Enemy.FacingRight;
        }
    }

    private bool ShouldApplyFreeze(SampleEnemy enemy)
    {
        if (!ApplyFreezeOnlyWhenFacingPlayer) return true;
        Vector2 dirToPlayer = enemy.GetDirectionToPlayer();
        return dirToPlayer != Vector2.Zero;
    }

    private void ApplyFreezeEffect(SampleEnemy enemy)
    {
        if (enemy.EffectController == null) return;

        var freezeEffect = new FreezeEffect
        {
            FrozenStateName = "CooldownFrozen",
            FallbackStateName = "Walk",
            Duration = FreezeDuration,
            EffectId = $"freeze_on_turn_{GetInstanceId()}",
            ResumePreviousState = false
        };

        enemy.ApplyEffect(freezeEffect);
    }
}

