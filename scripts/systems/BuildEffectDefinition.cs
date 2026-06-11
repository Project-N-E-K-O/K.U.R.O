using Godot;
using Godot.Collections;

namespace Kuros.Systems
{
    /// <summary>
    /// 构筑效果定义（三选一弹窗中的可选项）。
    /// 携带构筑类型和等级点数，选择后同时施加数值加成并推动 Build Level 进度。
    /// </summary>
    [GlobalClass]
    public partial class BuildEffectDefinition : Resource
    {
        [ExportGroup("基础信息")]
        [Export] public string EffectId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "未命名效果";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("构筑类型")]
        /// <summary>所属构筑类别（Guard / Machine / Banquet ...），对应 PlayerBuildController 的统计维度。</summary>
        [Export] public string BuildClass { get; set; } = string.Empty;

        /// <summary>选择后贡献的构筑等级点数（通常为 1）。</summary>
        [Export(PropertyHint.Range, "1,10,1")] public int LevelCount { get; set; } = 1;

        [ExportGroup("数值加成")]
        /// <summary>
        /// 选择后直接施加的属性修正。
        /// Key 使用 ItemAttributeIds 或 GameActor 的属性名（如 "attack_damage", "speed", "max_health"）。
        /// Value 为固定值增量。
        /// </summary>
        [Export] public Dictionary<string, float> StatBonuses { get; set; } = new();
    }
}
