using Godot;
namespace Kuros.Fx;

/// <summary>敌人坠落替身外观：每个 key（fall_guard1 等）一份骨架与基础变换。
/// SpineSprite 没有 offset 选项，Y 抬升用整个 SpineSprite 的 Position 实现；
/// 旋转/缩放/下沉由父节点 Position 的通用 fall 动画驱动。</summary>
[GlobalClass]
public partial class EnemyFallVisualProfile : Resource
{
    [Export] public string Key { get; set; } = "";
    /// <summary>SpineSkeletonDataResource（C# 无强类型绑定，按 Resource 存储）。</summary>
    [Export] public Resource? SkeletonDataResource { get; set; }
    /// <summary>要循环播放的 Spine 动画名（默认 stun）。</summary>
    [Export] public string SpineAnimationName { get; set; } = "stun";
    /// <summary>SpineSprite 自身的 Position（代替不存在的 offset）。</summary>
    [Export] public Vector2 SpinePosition { get; set; } = new(0, 180);
    [Export] public Vector2 SpineScale { get; set; } = new(0.33f, 0.33f);
}
