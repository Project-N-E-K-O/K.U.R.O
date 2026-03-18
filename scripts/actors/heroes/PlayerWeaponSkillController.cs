using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Items.Effects;
using Kuros.Items.Weapons;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责加载当前武器的技能定义，并与攻击/效果系统联动。
    /// </summary>
    public partial class PlayerWeaponSkillController : Node
    {
        [Export] public PlayerInventoryComponent? Inventory { get; set; }
        [Export(PropertyHint.MultilineText)] public string DefaultSkillId { get; set; } = string.Empty;

        private readonly Dictionary<string, WeaponSkillDefinition> _skills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _skillCooldowns = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _actionSkillMap = new(StringComparer.Ordinal);
        private readonly List<ActorEffect> _passiveEffects = new();
        private WeaponSkillDefinition? _defaultActiveSkill;
        private WeaponSkillDefinition? _fallbackUnarmedSkill;
        private ItemDefinition? _fallbackWeaponDefinition;
        private GameActor? _actor;
        private InventoryContainer? _currentQuickBar;
        private readonly Dictionary<string, float> _cooldownScaleSources = new(StringComparer.Ordinal);

        public override void _Ready()
        {
            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            Inventory ??= _actor?.GetNodeOrNull<PlayerInventoryComponent>("Inventory");

            if (Inventory == null)
            {
                GD.PushWarning($"{Name}: 未找到 PlayerInventoryComponent，无法加载武器技能。");
                return;
            }

            Inventory.WeaponEquipped += OnWeaponEquipped;
            Inventory.WeaponUnequipped += OnWeaponUnequipped;
            Inventory.ActiveBackpackSlotChanged += OnActiveSlotChanged;
            Inventory.QuickBarAssigned += OnQuickBarAssigned;
            Inventory.QuickBarSlotChanged += OnQuickBarSelectedSlotChanged;
            if (Inventory.Backpack != null)
            {
                Inventory.Backpack.InventoryChanged += OnBackpackInventoryChanged;
            }
            SubscribeToQuickBarSignals();
            InitializeFallbackSkill();
            CallDeferred(nameof(ApplyFallbackIfNoWeapon));
        }

        public override void _ExitTree()
        {
            if (Inventory != null)
            {
                Inventory.WeaponEquipped -= OnWeaponEquipped;
                Inventory.WeaponUnequipped -= OnWeaponUnequipped;
                Inventory.ActiveBackpackSlotChanged -= OnActiveSlotChanged;
                Inventory.QuickBarAssigned -= OnQuickBarAssigned;
                Inventory.QuickBarSlotChanged -= OnQuickBarSelectedSlotChanged;
                if (Inventory.Backpack != null)
                {
                    Inventory.Backpack.InventoryChanged -= OnBackpackInventoryChanged;
                }
            }

            UnsubscribeFromQuickBarSignals();
            ClearSkills();
            base._ExitTree();
        }

        public float ModifyAttackDamage(float baseDamage)
        {
            if (_defaultActiveSkill == null)
            {
                return baseDamage;
            }

            return baseDamage * MathF.Max(0f, _defaultActiveSkill.DamageMultiplier <= 0 ? 1f : _defaultActiveSkill.DamageMultiplier);
        }

        public string? GetPrimarySkillAnimation()
        {
            return _defaultActiveSkill?.AnimationName;
        }

        public bool TriggerDefaultSkill(GameActor? target = null)
        {
            if (_defaultActiveSkill == null)
            {
                return false;
            }

            return TriggerSkill(_defaultActiveSkill.SkillId, target);
        }

        public bool TriggerSkill(string skillId, GameActor? target = null)
        {
            if (!_skills.TryGetValue(skillId, out var skill) || _actor == null)
            {
                return false;
            }

            if (skill.SkillType != WeaponSkillType.Active)
            {
                return false;
            }

            if (!IsSkillOffCooldown(skill))
            {
                return false;
            }

            ApplySkillEffects(skill, ItemEffectTrigger.OnEquip, target);
            ArmCooldown(skill);
            return true;
        }

        public bool TryTriggerActionSkill(string actionName, GameActor? target = null)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return false;
            if (_actionSkillMap.TryGetValue(actionName, out var skillId))
            {
                return TriggerSkill(skillId, target);
            }
            return false;
        }

        private void OnWeaponEquipped(ItemDefinition weapon)
        {
            LoadSkills(weapon);
        }

        private void OnBackpackInventoryChanged()
        {
            ApplyFallbackIfNoWeapon();
        }

        private void OnWeaponUnequipped()
        {
            ClearSkills();
            ApplyFallbackIfNoWeapon();
        }

        private void OnActiveSlotChanged(int slotIndex)
        {
            ApplyFallbackIfNoWeapon();
        }

        private void InitializeFallbackSkill()
        {
            _fallbackWeaponDefinition = Inventory?.UnarmedWeaponDefinition;
            if (_fallbackWeaponDefinition == null) return;
            foreach (var skill in _fallbackWeaponDefinition.GetWeaponSkillDefinitions())
            {
                if (skill.SkillType == WeaponSkillType.Passive)
                {
                    _fallbackUnarmedSkill = skill;
                    break;
                }
            }
        }

        private void ApplyFallbackIfNoWeapon()
        {
            if (TryApplyQuickBarWeapon())
            {
                return;
            }

            if (TryApplyBackpackWeapon())
            {
                return;
            }

            ApplyUnarmedFallback();
        }

        private void LoadSkills(ItemDefinition weapon)
        {
            ClearSkills();

            foreach (var skill in weapon.GetWeaponSkillDefinitions())
            {
                _skills[skill.SkillId] = skill;
                if (!string.IsNullOrWhiteSpace(skill.ActivationAction))
                {
                    _actionSkillMap[skill.ActivationAction] = skill.SkillId;
                }

                if (skill.SkillType == WeaponSkillType.Passive)
                {
                    ApplySkillEffects(skill, ItemEffectTrigger.OnEquip);
                    continue;
                }

                if (_defaultActiveSkill == null || skill.SkillId == DefaultSkillId)
                {
                    _defaultActiveSkill = skill;
                }
            }

            if (_defaultActiveSkill == null)
            {
                GD.Print($"{Name}: 武器 {weapon.DisplayName} 未定义主动技能，使用基础攻击。");
            }
        }

        private void ClearSkills()
        {
            foreach (var effect in _passiveEffects)
            {
                _actor?.EffectController?.RemoveEffect(effect);
            }

            _passiveEffects.Clear();
            _skills.Clear();
            _skillCooldowns.Clear();
            _actionSkillMap.Clear();
            _defaultActiveSkill = null;
        }
        public void ApplyUnarmedFallback()
        {
            if (_fallbackUnarmedSkill == null || _actor == null) return;
            if (_defaultActiveSkill == _fallbackUnarmedSkill) return;

            ClearSkills();
            _skills[_fallbackUnarmedSkill.SkillId] = _fallbackUnarmedSkill;
            _defaultActiveSkill = _fallbackUnarmedSkill;
            ApplySkillEffects(_fallbackUnarmedSkill, ItemEffectTrigger.OnEquip);
        }

        private void ApplySkillEffects(WeaponSkillDefinition skill, ItemEffectTrigger trigger, GameActor? target = null)
        {
            if (_actor?.EffectController == null)
            {
                return;
            }

            foreach (var entry in skill.Effects)
            {
                if (entry == null) continue;
                var effect = entry.InstantiateEffect();
                if (effect == null) continue;
                if (trigger != ItemEffectTrigger.OnPickup)
                {
                    _actor.ApplyEffect(effect);
                    if (skill.SkillType == WeaponSkillType.Passive)
                    {
                        _passiveEffects.Add(effect);
                    }
                }
            }
        }

        private bool IsSkillOffCooldown(WeaponSkillDefinition skill)
        {
            if (skill.CooldownSeconds <= 0) return true;

            if (_skillCooldowns.TryGetValue(skill.SkillId, out var readyTime))
            {
                double now = Time.GetTicksMsec() / 1000.0;
                return now >= readyTime;
            }

            return true;
        }

        private void ArmCooldown(WeaponSkillDefinition skill)
        {
            if (skill.CooldownSeconds <= 0) return;
            double now = Time.GetTicksMsec() / 1000.0;
            _skillCooldowns[skill.SkillId] = now + skill.CooldownSeconds * ResolveCooldownScale();
        }

        private float ResolveCooldownScale()
        {
            if (_cooldownScaleSources.Count == 0) return 1f;
            float scale = 1f;
            foreach (var value in _cooldownScaleSources.Values)
            {
                scale *= MathF.Max(0.01f, value);
            }
            return Mathf.Clamp(scale, 0.05f, 100f);
        }

        private bool TryApplyQuickBarWeapon()
        {
            if (Inventory == null) return false;
            var stack = Inventory.GetSelectedQuickBarStack();
            if (!IsUsableWeaponStack(stack)) return false;

            LoadSkills(stack!.Item);
            return true;
        }

        private bool TryApplyBackpackWeapon()
        {
            if (Inventory == null) return false;
            var stack = Inventory.GetSelectedBackpackStack();
            if (!IsUsableWeaponStack(stack)) return false;

            LoadSkills(stack!.Item);
            return true;
        }

        private static bool IsUsableWeaponStack(InventoryItemStack? stack)
        {
            return stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
        }

        private void SubscribeToQuickBarSignals()
        {
            if (Inventory?.QuickBar == null)
            {
                return;
            }

            if (_currentQuickBar == Inventory.QuickBar)
            {
                return;
            }

            UnsubscribeFromQuickBarSignals();
            _currentQuickBar = Inventory.QuickBar;
            _currentQuickBar.SlotChanged += OnQuickBarSlotChanged;
            _currentQuickBar.InventoryChanged += OnQuickBarInventoryChanged;
        }

        private void UnsubscribeFromQuickBarSignals()
        {
            if (_currentQuickBar == null)
            {
                return;
            }

            _currentQuickBar.SlotChanged -= OnQuickBarSlotChanged;
            _currentQuickBar.InventoryChanged -= OnQuickBarInventoryChanged;
            _currentQuickBar = null;
        }

        private void OnQuickBarAssigned()
        {
            SubscribeToQuickBarSignals();
            ApplyFallbackIfNoWeapon();
        }

        private void OnQuickBarSelectedSlotChanged(int slotIndex)
        {
            ApplyFallbackIfNoWeapon();
        }

        private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
        {
            if (Inventory != null && slotIndex == Inventory.SelectedQuickBarSlot)
            {
                ApplyFallbackIfNoWeapon();
            }
        }

        private void OnQuickBarInventoryChanged()
        {
            ApplyFallbackIfNoWeapon();
        }

        public void SetCooldownScale(string sourceId, float scale)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;
            if (scale <= 0f)
            {
                _cooldownScaleSources.Remove(sourceId);
                return;
            }

            _cooldownScaleSources[sourceId] = scale;
        }

        public void ClearCooldownScale(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;
            _cooldownScaleSources.Remove(sourceId);
        }
    }
}

