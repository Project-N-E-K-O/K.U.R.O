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

        // 控制状态（眩晕/转身冷却/受击/死亡）下不跑寻敌转身逻辑：
        // 眩晕中不应随玩家位置翻转朝向（此前转身冻结会顺手顶掉眩晕，把这个现象掩盖了；
        // 冻结被豁免后必须在源头停掉转向）。状态结束恢复时朝向未变，照常触发一次转身冻结。
        string st = Enemy.StateMachine?.CurrentState?.Name ?? string.Empty;
        if (st == "Frozen" || st == "CooldownFrozen" || st == "Hit" || st == "Dying" || st == "Dead")
        {
            return;
        }

        base._PhysicsProcess(delta);

        if (!_initializedFacing)
        {
            _wasFacingRight = Enemy.FacingRight;
            _initializedFacing = true;
            return;
        }

        if (Enemy.FacingRight != _wasFacingRight)
        {
            // KeepDistance 状态自行控制朝向（后撤），不应触发转身冻结
            if (!Enemy.HasMeta("__keep_distance_active") && ShouldApplyFreeze(Enemy))
            {
                ApplyFreezeEffect(Enemy);
            }
            _wasFacingRight = Enemy.FacingRight;
        }
    }

    private bool ShouldApplyFreeze(SampleEnemy enemy)
    {
        // 攻击中、以及被控制的状态（眩晕/转身冷却/受击/死亡）都不应再叠加"转身冻结"——
        // 否则转身的 FreezeEffect（落地状态 CooldownFrozen）会把刚进入的眩晕/受击状态顶掉
        // （guard4 破招眩晕被切走、直接接招复现过）。
        string state = enemy.StateMachine?.CurrentState?.Name ?? string.Empty;
        if (state == "Attack" || state == "Frozen" || state == "CooldownFrozen"
            || state == "Hit" || state == "Dying" || state == "Dead")
        {
            return false;
        }

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

