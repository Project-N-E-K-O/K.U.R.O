using Godot;

namespace Kuros.Companions
{
    [GlobalClass]
    public abstract partial class P2SupportSkillHandler : Resource
    {
        [Export] public string HandlerId { get; set; } = "p2_handler_base";

        public virtual string GetHandlerTypeNormalized() => "unknown";

        public abstract bool TryExecute(P2SupportExecutor executor, P2SupportSkillDefinition skill, out string detail, out string rejectReason);
    }
}
