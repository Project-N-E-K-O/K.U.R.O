using Godot;
using Godot.Collections;
using Kuros.Systems.Stage;

namespace Kuros.Environments;

/// <summary>
/// 一组深坑的规则与兜底点。本身**不是判定区**：子节点里的 FallActivationArea（黄色覆盖范围）、
/// FallHazardArea（红色坑面）通过 <see cref="FindRegion"/> 向上查找所属区域，
/// 由玩家侧 PlayerFallController 使用这里的参数。绿色 FallSupportArea 独立提供承托资格，不查找所属区域。
/// 黄色范围内的非坑面必须是已知固定可行走地面，并留出安全记录所需的坑边缓冲；
/// 黄色外围完整覆盖红区以激活附近检测；生成/传送补查也识别直接落在红区的对象。
/// </summary>
[GlobalClass]
public partial class FallRegion : Node2D
{
    /// <summary>
    /// 坠落持续时间（秒，运行时至少 0.1 秒）：期间隐藏玩家，玩家控制器配置的替身动画按此时长播放。
    /// 到期后验证回退点并结算伤害；若所有回退点失效，则保持保护并每 0.1 秒重试，实际恢复可能延后。
    /// </summary>
    [Export(PropertyHint.Range, "0.1,20,0.1,or_greater")] public float FallDuration { get; set; } = 3f;
    /// <summary>单次坠落伤害占结算时当前最大生命值的比例，运行时限制为 0~1；正数伤害向上取整，0 不扣血。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float DamageRatio { get; set; } = 0.2f;
    /// <summary>
    /// 安全点与坑边的间距（世界单位，查询半径至少 1）：以候选脚底为圆心，查询圆与任意红色危险区域相交时，
    /// 禁止保存或使用该位置；历史安全点和兜底点均执行此验证。此参数不改变坠落触发边界，也不提供踩空容错。
    /// </summary>
    [Export(PropertyHint.Range, "1,1000,1,or_greater")] public float SafeDistance { get; set; } = 80f;
    /// <summary>
    /// 兜底节点路径列表，相对于本 FallRegion 解析，支持 Node2D（建议 Marker2D）；节点世界位置作为玩家根节点回退坐标。
    /// 无有效历史安全点时，从通过安全验证的节点中选择最接近旧记录的位置；从未记录时按踩空位置选最近点。
    /// 兜底点也必须在本黄区内、无平台承托、满足 SafeDistance 且身体空间无阻挡。
    /// 开始前全部无效则拒绝启动坠落；坠落到期时全部失效则保持保护并重试。
    /// </summary>
    [Export] public Array<NodePath> FallbackMarkerPaths { get; set; } = new();

    /// <summary>玩家完成坠落后可前往的隐藏楼层。为空时执行普通坠落。</summary>
    [Export] public Array<StageConfig> HiddenFloorDestinations { get; set; } = new();

    /// <summary>从指定节点的父节点开始，返回最近的 FallRegion 祖先；黄区与红区通过此方法归属区域，绿色承托区不依赖此方法。</summary>
    public static FallRegion? FindRegion(Node node)
    {
        for (Node? current = node.GetParent(); current != null; current = current.GetParent())
            if (current is FallRegion region) return region;
        return null;
    }
}
