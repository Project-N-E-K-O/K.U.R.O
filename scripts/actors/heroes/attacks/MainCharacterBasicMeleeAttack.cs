using Kuros.Actors.Heroes;
using Kuros.Items.Attributes;

namespace Kuros.Actors.Heroes.Attacks
{
	/// <summary>
	/// MainCharacter 专用的基础近战攻击
	/// 使用 Spine 动画，集成 WeaponSkillController
	/// </summary>
	public partial class MainCharacterBasicMeleeAttack : PlayerAttackTemplate
	{
		private PlayerWeaponSkillController? _weaponSkillController;
		private PlayerInventoryComponent? _inventory;
		private string _defaultAnimation = "attack";

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
			
			// 获取 MainCharacter 的组件
			if (Player is MainCharacter mainChar)
			{
				_weaponSkillController = mainChar.WeaponSkillController;
				_inventory = mainChar.InventoryComponent;
				_defaultAnimation = mainChar.AttackAnimationName;
			}
			else
			{
				_weaponSkillController = Player.GetNodeOrNull<PlayerWeaponSkillController>("WeaponSkillController");
				_inventory = Player.InventoryComponent ?? Player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
			}
		}

		protected override void OnAttackStarted()
		{
			AnimationName = _defaultAnimation;
			DamageOverride = Player.AttackDamage;

			// 应用 InventoryComponent 的攻击力加成
			if (_inventory != null)
			{
				DamageOverride += _inventory.GetSelectedAttributeValue(ItemAttributeIds.AttackPower, 0f);
			}

			// 应用 WeaponSkillController 的伤害倍率和动画
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

		protected override void OnActivePhase()
		{
			// 如果是 MainCharacter，使用集成了 WeaponSkillController 的 PerformAttackCheck
			// 否则使用基类的默认实现
			if (Player is MainCharacter mainChar)
			{
				// 不调用 base.OnActivePhase()，避免重复调用 PerformAttackCheck
				mainChar.PerformAttackCheck();
			}
			else
			{
				// 回退到基类的默认实现
				base.OnActivePhase();
			}
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
