using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// Placeholder machine-build effects referenced by PlayerBuildController.
    /// Replace with real design values/behavior when available.
    /// </summary>
    public partial class BuildMachineLevel1Effect : ActorEffect
    {
        protected override void OnApply()
        {
            if (!string.IsNullOrWhiteSpace(EffectId) && string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = nameof(BuildMachineLevel1Effect);
            }

            GD.Print($"[{nameof(BuildMachineLevel1Effect)}] Applied to {Actor.Name}");
        }
    }

    public partial class BuildMachineLevel2Effect : ActorEffect
    {
        protected override void OnApply()
        {
            if (!string.IsNullOrWhiteSpace(EffectId) && string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = nameof(BuildMachineLevel2Effect);
            }

            GD.Print($"[{nameof(BuildMachineLevel2Effect)}] Applied to {Actor.Name}");
        }
    }

    public partial class BuildMachineLevel3Effect : ActorEffect
    {
        protected override void OnApply()
        {
            if (!string.IsNullOrWhiteSpace(EffectId) && string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = nameof(BuildMachineLevel3Effect);
            }

            GD.Print($"[{nameof(BuildMachineLevel3Effect)}] Applied to {Actor.Name}");
        }
    }
}
