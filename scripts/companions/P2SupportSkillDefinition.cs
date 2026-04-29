using Godot;

namespace Kuros.Companions
{
    [GlobalClass]
    public partial class P2SupportSkillDefinition : Resource
    {
        [Export] public string SkillId { get; set; } = "p2_skill_base";
        [Export] public string DisplayName { get; set; } = "P2 支援技能";
        [Export] public string Description { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "0,60,0.1")] public float CooldownSeconds { get; set; } = 3.0f;
        [Export] public P2SupportSkillHandler? Handler { get; set; }
            = null;

        public string GetSkillTypeNormalized()
        {
            return Handler?.GetHandlerTypeNormalized() ?? "unknown";
        }
    }
}
