using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Systems.Inventory;
using Kuros.Actors.Heroes;

namespace Kuros.Effects
{
    /// <summary>
    /// 如果玩家同时持有两把妖梦武器，则降低攻击与技能冷却。
    /// </summary>
    [GlobalClass]
    public partial class YaomengDualSynergyEffect : ActorEffect
    {
        private const string SourceKey = "yaomeng_dual_synergy";

        [Export] public string WeaponIdA { get; set; } = "weapon_a0_yaomeng1";
        [Export] public string WeaponIdB { get; set; } = "weapon_a0_yaomeng2";
        [Export(PropertyHint.Range, "0.1,1,0.05")] public float CooldownMultiplier { get; set; } = 0.5f;

        private PlayerInventoryComponent? _inventory;
        private InventoryContainer? _quickBar;
        private PlayerWeaponSkillController? _skillController;
        private bool _synergyActive;
        private float? _cachedAttackCooldown;

        protected override void OnApply()
        {
            base.OnApply();
            if (Actor is not SamplePlayer player)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            _inventory = player.InventoryComponent ?? player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            _quickBar = _inventory?.QuickBar;
            _skillController = player.WeaponSkillController ?? player.GetNodeOrNull<PlayerWeaponSkillController>("WeaponSkillController");

            Subscribe();
            RefreshSynergy();
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            Unsubscribe();
            DeactivateSynergy();
        }

        private void Subscribe()
        {
            if (_inventory?.Backpack != null)
            {
                _inventory.Backpack.InventoryChanged += OnInventoryChanged;
            }
            if (_quickBar != null)
            {
                _quickBar.InventoryChanged += OnInventoryChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_inventory?.Backpack != null)
            {
                _inventory.Backpack.InventoryChanged -= OnInventoryChanged;
            }
            if (_quickBar != null)
            {
                _quickBar.InventoryChanged -= OnInventoryChanged;
            }
        }

        private void OnInventoryChanged()
        {
            RefreshSynergy();
        }

        private void RefreshSynergy()
        {
            bool hasA = OwnsWeapon(WeaponIdA);
            bool hasB = OwnsWeapon(WeaponIdB);
            bool shouldActivate = hasA && hasB;

            if (shouldActivate == _synergyActive)
            {
                return;
            }

            if (shouldActivate)
            {
                ActivateSynergy();
            }
            else
            {
                DeactivateSynergy();
            }
        }

        private void ActivateSynergy()
        {
            if (Actor == null) return;

            _synergyActive = true;
            if (!_cachedAttackCooldown.HasValue)
            {
                _cachedAttackCooldown = Mathf.Max(0.01f, Actor.AttackCooldown);
            }

            Actor.AttackCooldown = Mathf.Max(0.01f, _cachedAttackCooldown.Value * CooldownMultiplier);
            _skillController?.SetCooldownScale(SourceKey, CooldownMultiplier);
        }

        private void DeactivateSynergy()
        {
            if (!_synergyActive)
            {
                return;
            }

            _synergyActive = false;
            if (Actor != null && _cachedAttackCooldown.HasValue)
            {
                Actor.AttackCooldown = _cachedAttackCooldown.Value;
            }

            _skillController?.ClearCooldownScale(SourceKey);
            _cachedAttackCooldown = null;
        }

        private bool OwnsWeapon(string weaponId)
        {
            if (string.IsNullOrWhiteSpace(weaponId))
            {
                return false;
            }

            return ContainsItem(_inventory?.Backpack, weaponId) || ContainsItem(_quickBar, weaponId);
        }

        private static bool ContainsItem(InventoryContainer? container, string itemId)
        {
            if (container == null) return false;
            foreach (var stack in container.Slots)
            {
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == itemId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

