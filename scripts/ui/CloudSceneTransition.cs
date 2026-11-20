using Godot;
using System.Threading.Tasks;

namespace Kuros.UI;

/// <summary>挂在 SceneTree 根下，覆盖换场空帧；由新楼层就绪信号启动向上退场。</summary>
public partial class CloudSceneTransition : CanvasLayer
{
    private const string NodeName = "CloudSceneTransition";
    private const string ScenePath = "res://scenes/ui/CloudSceneTransition.tscn";
    private bool _revealing;
    private Node? _source;
    private string _destination = "";

    public static CloudSceneTransition? Find(SceneTree tree)
        => tree.Root.GetNodeOrNull<CloudSceneTransition>(NodeName);

    public static async Task<CloudSceneTransition> CoverAsync(SceneTree tree, string destination)
    {
        if (Find(tree) is { } previous)
        {
            tree.Root.RemoveChild(previous);
            previous.QueueFree();
        }
        var cover = GD.Load<PackedScene>(ScenePath).Instantiate<CloudSceneTransition>();
        cover._source = tree.CurrentScene;
        cover._destination = destination;
        tree.Root.AddChild(cover);
        await cover.AnimateAsync("cover");
        return cover;
    }

    public override void _Process(double delta)
    {
        // 用户返回菜单或意外进入其他场景时，不遗留跨场景遮罩。
        var current = GetTree().CurrentScene;
        if (_destination.Length > 0 && current != null && current != _source && current.SceneFilePath != _destination)
            QueueFree();
    }

    public async Task RevealAsync()
    {
        if (_revealing || !IsInsideTree()) return;
        _revealing = true;
        await AnimateAsync("reveal");
        if (IsInstanceValid(this)) QueueFree();
    }

    private async Task AnimateAsync(StringName animation)
    {
        var player = GetNode<AnimationPlayer>("AnimationPlayer");
        player.Play(animation);
        player.Advance(0);

        // 使用真实时间，过场不受暂停或战斗慢动作影响。
        ulong start = Time.GetTicksMsec();
        double duration = player.CurrentAnimationLength;
        while (IsInsideTree())
        {
            double elapsed = (Time.GetTicksMsec() - start) / 1000.0;
            player.Seek(System.Math.Min(elapsed, duration), update: true);
            if (elapsed >= duration) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this)) return;
        }
    }
}
