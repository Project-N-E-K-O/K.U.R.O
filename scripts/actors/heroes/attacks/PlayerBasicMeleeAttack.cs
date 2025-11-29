using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 示例：基础近战攻击
    /// - 监听 attack 输入
    /// - 仅在玩家处于 Idle/Walk/Run 状态时可触发
    /// - 生效期调用默认命中逻辑，对范围内敌人造成伤害
    /// </summary>
    public partial class PlayerBasicMeleeAttack : PlayerAttackTemplate
    {
        private PlayerWeaponSkillController? _weaponSkillController;
        private string _defaultAnimation = "animations/attack";

        protected override void OnInitialized()
        {
            base.OnInitialized();

            TriggerActions.Clear();
            TriggerActions.Add("attack");
            RequiresTargetInRange = false;
            WarmupDuration = 0.15f;
            ActiveDuration = 0.2f;
            RecoveryDuration = 0.35f;
            CooldownDuration = 0.5f;
            AnimationName = _defaultAnimation;
            _weaponSkillController = Player.GetNodeOrNull<PlayerWeaponSkillController>("WeaponSkillController");
        }

        protected override void OnAttackStarted()
        {
            AnimationName = _defaultAnimation;
            DamageOverride = Player.AttackDamage;

            if (_weaponSkillController != null)
            {
                var overrideAnim = _weaponSkillController.GetPrimarySkillAnimation();
                if (!string.IsNullOrEmpty(overrideAnim))
                {
                    AnimationName = overrideAnim;
                }

                DamageOverride = _weaponSkillController.ModifyAttackDamage(DamageOverride);
                _weaponSkillController.TriggerDefaultSkill();
            }

            base.OnAttackStarted();
        }

        protected override bool MeetsCustomConditions()
        {
            string source = string.IsNullOrEmpty(TriggerSourceState)
                ? Player.LastMovementStateName
                : TriggerSourceState;

            return source == "Idle" ||
                   source == "Walk" ||
                   source == "Run";
        }
    }
}

