using Godot;
using Kuros.Items.Effects;

namespace Kuros.Items.Weapons
{
    /// <summary>
    /// 武器技能定义，可配置主动/被动效果、动画、数值等。
    /// </summary>
    public partial class WeaponSkillDefinition : Resource
    {
        [Export] public string SkillId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Weapon Skill";
        [Export] public WeaponSkillType SkillType { get; set; } = WeaponSkillType.Active;
        [Export] public string AnimationName { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "0,5,0.1")] public float DamageMultiplier { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,30,0.1")] public float CooldownSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;
        [Export] public Godot.Collections.Array<ItemEffectEntry> Effects { get; set; } = new();
        [Export] public Godot.Collections.Array<string> StateWhitelist { get; set; } = new();
        [Export] public bool UseDefaultAttackAnimationFallback { get; set; } = true;

        public bool IsUsableInState(string stateName)
        {
            if (StateWhitelist.Count == 0) return true;
            return StateWhitelist.Contains(stateName);
        }
    }
}

