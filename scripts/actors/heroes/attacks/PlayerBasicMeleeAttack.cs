using Kuros.Actors.Heroes;
using Kuros.Items.Attributes;

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
        private PlayerInventoryComponent? _inventory;
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
            _inventory = Player.InventoryComponent ?? Player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
        }

        protected override void OnAttackStarted()
        {
            // 如果是 MainCharacter，使用攻击动画名称
            if (Player is MainCharacter mainChar)
            {
                AnimationName = mainChar.AttackAnimationName;
            }
            else
            {
                AnimationName = _defaultAnimation;
            }
            
            DamageOverride = Player.AttackDamage;

            if (_inventory != null)
            {
                DamageOverride += _inventory.GetSelectedAttributeValue(ItemAttributeIds.AttackPower, 0f);
            }

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

            // 调用基类方法，基类会自动检测 MainCharacter 并播放 Spine 动画
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

