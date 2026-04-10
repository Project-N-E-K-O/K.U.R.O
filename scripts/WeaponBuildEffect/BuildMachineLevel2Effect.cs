using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Systems.FSM;

namespace Kuros.Builds
{
    /// <summary>
    /// 机械构筑 2 级效果：玩家攻击时不会因受击进入 Hit 状态。
    /// </summary>
    [GlobalClass]
    public partial class BuildMachineLevel2Effect : ActorEffect
    {
        [Export] public bool OnlyIgnoreDuringAttackState { get; set; } = true;
        [Export(PropertyHint.Range, "0,1,0.01,or_greater")] public float PostAttackGraceSeconds { get; set; } = 0.5f;

        private bool _cachedIgnoreHitStateOnDamage;
        private bool _hasCachedValue;
        private StateMachine? _stateMachine;
        private float _postAttackGraceRemaining;

        public BuildMachineLevel2Effect()
        {
            EffectId = "build_machine_level2";
            DisplayName = "机械II";
            Description = "玩家攻击中以及攻击结束后短时间内不会被非控制技能打断";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null)
            {
                return;
            }

            _stateMachine = Actor.StateMachine;
            _cachedIgnoreHitStateOnDamage = Actor.IgnoreHitStateOnDamage;
            _hasCachedValue = true;
            _postAttackGraceRemaining = 0f;
        }

        protected override void OnTick(double delta)
        {
            if (Actor == null)
            {
                return;
            }

            if (!OnlyIgnoreDuringAttackState)
            {
                Actor.IgnoreHitStateOnDamage = true;
                return;
            }

            bool isInAttackState = IsInAttackState();
            if (isInAttackState)
            {
                _postAttackGraceRemaining = Mathf.Max(0f, PostAttackGraceSeconds);
            }
            else if (_postAttackGraceRemaining > 0f)
            {
                _postAttackGraceRemaining = Mathf.Max(0f, _postAttackGraceRemaining - (float)delta);
            }

            bool shouldIgnoreHitState = isInAttackState || _postAttackGraceRemaining > 0f;
            Actor.IgnoreHitStateOnDamage = shouldIgnoreHitState;
        }

        public override void OnRemoved()
        {
            if (_hasCachedValue && Actor != null)
            {
                Actor.IgnoreHitStateOnDamage = _cachedIgnoreHitStateOnDamage;
            }

            _postAttackGraceRemaining = 0f;
            _hasCachedValue = false;
            base.OnRemoved();
        }

        private bool IsInAttackState()
        {
            string? stateName = _stateMachine?.CurrentState?.Name;
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            return stateName.Contains("Attack", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
