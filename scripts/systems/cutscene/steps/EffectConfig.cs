using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 单个特效配置（用于 EffectGroupSpawnStep）。
    /// 注意：本类必须**独占一个与类名同名的文件**——Godot 的 [GlobalClass] 靠"文件名 = 类名"注册，
    /// 之前它和 EffectGroupSpawnStep 挤在同一个 .cs 里，导致注册失败、编辑器里无法创建/填写该数组元素。
    /// </summary>
    [GlobalClass]
    public partial class EffectConfig : Resource
    {
        /// <summary>要生成的特效场景（直接拖场景资源进来）</summary>
        [Export] public PackedScene? EffectScene { get; set; }

        /// <summary>生成方式</summary>
        [Export] public EffectSpawnStep.SpawnTypeEnum SpawnType { get; set; } = EffectSpawnStep.SpawnTypeEnum.PlayerPosition;

        /// <summary>
        /// 位置参数，含义根据 SpawnType 改变：
        /// - PlayerPosition：相对于玩家的偏移
        /// - GlobalPosition：绝对全局坐标
        /// - RelativeToNode：相对于目标节点的偏移
        /// </summary>
        [Export] public Vector2 Position { get; set; } = Vector2.Zero;

        /// <summary>目标节点路径（SpawnType=RelativeToNode 时使用）</summary>
        [Export] public NodePath TargetNodePath { get; set; } = new NodePath();

        /// <summary>生成延迟（秒）。0 = 立即生成</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float SpawnDelay { get; set; } = 0f;

        /// <summary>自动销毁时长（秒）。0 = 不自动销毁</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float DestroyAfterDuration { get; set; } = 0f;

        /// <summary>生成登记标签（供 EffectDespawnStep 按标签销毁；空 = 只参与"清全部"）。
        /// 登记的是实例**根节点**，子树随父节点一起释放。</summary>
        [Export] public string SpawnTag { get; set; } = "";

        /// <summary>跳过过场时是否仍然生成（默认 true）。关键生成（道具/轨道等）保持 true；
        /// 纯视觉（爆炸/烟雾）可设 false，避免按跳过时闪现一下。</summary>
        [Export] public bool GenerateOnSkip { get; set; } = true;

        /// <summary>
        /// 生成时属性覆盖（属性名 → 值），在 AddChild 之前应用——同一个通用场景可借此生成出不同配置的多份实例
        /// （如左右两条 SlideRail：分别覆盖 FlipCarriageEnds / CarriagePrefab / 限位 Marker 路径）。
        /// </summary>
        [Export] public Godot.Collections.Dictionary<string, Variant> PropertyOverrides { get; set; } = new();
    }
}
