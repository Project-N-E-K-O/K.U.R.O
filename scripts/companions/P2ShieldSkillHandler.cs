using Godot;

namespace Kuros.Companions
{
    [GlobalClass]
    public partial class P2ShieldSkillHandler : P2SupportSkillHandler
    {
        [Export(PropertyHint.Range, "1,500,1")] public int ShieldAmount { get; set; } = 24;
        [Export(PropertyHint.Range, "0.5,20,0.1")] public float ShieldDurationSeconds { get; set; } = 6f;

        public override string GetHandlerTypeNormalized() => "shield";

        public override bool TryExecute(P2SupportExecutor executor, P2SupportSkillDefinition skill, out string detail, out string rejectReason)
        {
            return executor.ApplyShield(ShieldAmount, ShieldDurationSeconds, skill.SkillId, out detail, out rejectReason);
        }
    }
}
