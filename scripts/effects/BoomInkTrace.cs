using Godot;

namespace Kuros.Fx;

public partial class BoomInkTrace : FadeInOutDestroy
{
    [Export] public Texture2D? AlternateMask { get; set; }

    public override void _Ready()
    {
        if (AlternateMask != null && GD.Randf() < 0.5f) Texture = AlternateMask;
        FlipH = GD.Randf() < 0.5f;
        base._Ready();
    }
}
