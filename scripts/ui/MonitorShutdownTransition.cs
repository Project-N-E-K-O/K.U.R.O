using Godot;
using System.Threading.Tasks;

namespace Kuros.UI;

public partial class MonitorShutdownTransition : CanvasLayer
{
    private const string OverlayName = "MonitorShutdownTransition";
    public const string HomeScenePath = "res://scenes/Stage_home.tscn";
    private bool _revealing;

    public static MonitorShutdownTransition? Find(SceneTree tree)
        => tree.Root.GetNodeOrNull<MonitorShutdownTransition>(OverlayName);

    public static MonitorShutdownTransition? Create(SceneTree tree)
    {
        const string path = "res://scenes/ui/MonitorShutdownTransition.tscn";
        var overlay = GD.Load<PackedScene>(path)?.Instantiate<MonitorShutdownTransition>();
        if (overlay != null) tree.Root.AddChild(overlay);
        return overlay;
    }

    public Task ShutdownAsync() => PlayAsync("shutdown");

    public async Task RevealAsync()
    {
        if (_revealing) return;
        _revealing = true;
        await PlayAsync("reveal");
        if (IsInstanceValid(this)) QueueFree();
    }

    private async Task PlayAsync(StringName animation)
    {
        var player = GetNode<AnimationPlayer>("AnimationPlayer");
        player.Play(animation);
        player.Advance(0);
        await ToSignal(player, AnimationPlayer.SignalName.AnimationFinished);
    }
}
