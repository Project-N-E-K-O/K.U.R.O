using Godot;

namespace Kuros.Companions
{
    [GlobalClass]
    public partial class P2HealSkillHandler : P2SupportSkillHandler
    {
        [Export(PropertyHint.Range, "1,500,1")] public int HealAmount { get; set; } = 18;

        public override string GetHandlerTypeNormalized() => "heal";

        public override bool TryExecute(P2SupportExecutor executor, P2SupportSkillDefinition skill, out string detail, out string rejectReason)
        {
            return executor.ApplyHeal(HealAmount, skill.SkillId, out detail, out rejectReason);
        }
    }
}
