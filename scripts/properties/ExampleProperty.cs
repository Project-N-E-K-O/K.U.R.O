using Godot;
using Kuros.Core;

/// <summary>
/// 示例拾取属性 - 拾取时增加分数
/// </summary>
public partial class ExampleProperty : PickupProperty
{
    [Export] public int ScoreBonus { get; set; } = 10;

    public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>
    /// 当被拾取时调用
    /// </summary>
    protected override void OnPicked(GameActor actor)
    {
        base.OnPicked(actor);
        GD.Print($"{Name} picked up by {actor.Name}. Score bonus: {ScoreBonus}");
        // 这里可以添加增加分数的逻辑
        // 例如：GameManager.Instance?.AddScore(ScoreBonus);
    }
}

