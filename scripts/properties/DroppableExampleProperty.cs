using Godot;
using Kuros.Core;
using Kuros.Utils;

public partial class DroppableExampleProperty : DroppablePickupProperty
{
    [Export] public int EnergyValue = 25;
    [Export] public Color PickedColor = Colors.LimeGreen;

    private Color _initialColor = Colors.White;
    private Sprite2D? _sprite;
    private bool _energyGranted;

    public override void _Ready()
    {
        base._Ready();

        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _initialColor = _sprite.Modulate;
        }
    }

    protected override void OnPicked(GameActor actor)
    {
        base.OnPicked(actor);
        _energyGranted = true;

        if (_sprite != null)
        {
            _sprite.Modulate = PickedColor;
        }

        GameLogger.Info(nameof(DroppableExampleProperty), $"{Name} granting {EnergyValue} energy to {actor.Name}");
    }

    protected override void OnPutDown(GameActor actor)
    {
        base.OnPutDown(actor);

        if (_sprite != null)
        {
            _sprite.Modulate = _initialColor;
        }

        if (_energyGranted)
        {
            GameLogger.Info(nameof(DroppableExampleProperty), $"{Name} put down by {actor.Name}. Energy effect removed.");
            _energyGranted = false;
        }
    }
}


