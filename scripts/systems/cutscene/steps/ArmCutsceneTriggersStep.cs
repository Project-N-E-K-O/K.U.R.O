using Godot;
using Godot.Collections;
using System.Threading.Tasks;

namespace Kuros.Systems.Cutscene
{
    [GlobalClass]
    public partial class ArmCutsceneTriggersStep : CutsceneStep
    {
        [Export] public Array<NodePath> TriggerPaths { get; set; } = new();
        public override bool ExecuteOnSkip => true;

        public override Task Execute(CutsceneContext ctx)
        {
            foreach (var path in TriggerPaths)
            {
                var trigger = ctx.Manager.GetNodeOrNull<CutsceneTrigger>(path);
                if (trigger != null) trigger.Arm();
                else GD.PushWarning($"[Cutscene] Missing trigger: {path}");
            }
            return Task.CompletedTask;
        }
    }
}
