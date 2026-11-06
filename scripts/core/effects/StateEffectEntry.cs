using Godot;

namespace Kuros.Core.Effects
{
    public enum StateEffectTiming
    {
        /// <summary>由状态监听的动画 effect 事件触发；默认兼容原死亡特效。</summary>
        OnAnimationEffect = 0,
        /// <summary>每次进入状态立即生成一次，不消耗动画事件次数。</summary>
        OnStateEnter = 1
    }

    /// <summary>状态专用特效条目，不包含攻击阶段或攻击生命周期字段。</summary>
    [GlobalClass]
    public partial class StateEffectEntry : EffectSpawnConfig
    {
        [Export] public StateEffectTiming SpawnTiming { get; set; } = StateEffectTiming.OnAnimationEffect;
    }
}
