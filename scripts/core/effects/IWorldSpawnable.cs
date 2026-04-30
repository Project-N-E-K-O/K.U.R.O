using Godot;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 支持"世界坐标落点"的效果接口。
    /// 实现此接口的 ActorEffect 在 OnApply 之前可接收落点坐标，
    /// 从而将效果的作用区域定位到投掷物的着地点，而非施法者自身位置。
    /// </summary>
    public interface IWorldSpawnable
    {
        /// <summary>
        /// 世界坐标落点。由 SpawnThrowDestroyEffects 在 ApplyEffect 之前赋值。
        /// 若为 Vector2.Zero 则表示未设置，效果应回退到施法者位置。
        /// </summary>
        Vector2? WorldSpawnPosition { get; set; }
    }
}
