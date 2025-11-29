using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Items.Effects;
using Kuros.Items.Weapons;

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
        private readonly List<ActorEffect> _passiveEffects = new();
        private WeaponSkillDefinition? _defaultActiveSkill;
        private GameActor? _actor;

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
        }

        public override void _ExitTree()
        {
            if (Inventory != null)
            {
                Inventory.WeaponEquipped -= OnWeaponEquipped;
                Inventory.WeaponUnequipped -= OnWeaponUnequipped;
            }

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

        public void TriggerDefaultSkill(GameActor? target = null)
        {
            if (_defaultActiveSkill == null)
            {
                return;
            }

            TriggerSkill(_defaultActiveSkill.SkillId, target);
        }

        public void TriggerSkill(string skillId, GameActor? target = null)
        {
            if (!_skills.TryGetValue(skillId, out var skill) || _actor == null)
            {
                return;
            }

            if (skill.SkillType != WeaponSkillType.Active)
            {
                return;
            }

            ApplySkillEffects(skill, ItemEffectTrigger.OnEquip, target);
        }

        private void OnWeaponEquipped(ItemDefinition weapon)
        {
            LoadSkills(weapon);
        }

        private void OnWeaponUnequipped()
        {
            ClearSkills();
        }

        private void LoadSkills(ItemDefinition weapon)
        {
            ClearSkills();

            foreach (var skill in weapon.GetWeaponSkillDefinitions())
            {
                _skills[skill.SkillId] = skill;
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
            _defaultActiveSkill = null;
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
    }
}

