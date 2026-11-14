using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>攻击专用特效条目：通用生成参数继承自 EffectSpawnConfig。</summary>
    [GlobalClass]
    public partial class AttackEffectEntry : EffectSpawnConfig
    {
        /// <summary>场上唯一性组；非空时阻止同组特效重复生成。</summary>
        [Export] public string UniqueGroup { get; set; } = string.Empty;
        /// <summary>攻击生成时机；Inherit 回退 OnActive，保留已有序列化值。</summary>
        [Export] public EffectSpawnTiming SpawnTiming = EffectSpawnTiming.Inherit;
        /// <summary>攻击选择时检查的阻塞特效组；空表示不限制。</summary>
        [Export] public string BlockedByFxGroup { get; set; } = string.Empty;
        /// <summary>绑定攻击阶段结束时销毁；None 表示特效自行管理。</summary>
        [Export] public EffectLifecycleBinding LifecycleBinding { get; set; } = EffectLifecycleBinding.None;

        public EffectSpawnTiming ResolveTiming(EffectSpawnTiming fallback)
            => SpawnTiming == EffectSpawnTiming.Inherit ? fallback : SpawnTiming;

        public string ResolveBlockedGroup(string fallback)
            => string.IsNullOrEmpty(BlockedByFxGroup) ? fallback : BlockedByFxGroup;
    }
}
