using Godot;

namespace Kuros.Items
{
    /// <summary>
    /// 基础物品定义资源，用于描述可被背包、装备栏等系统引用的物品模板。
    /// </summary>
    public partial class ItemDefinition : Resource
    {
        [ExportGroup("Identity")]
        [Export] public string ItemId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Unnamed Item";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("Presentation")]
        [Export] public Texture2D? Icon { get; set; }
        [Export] public string Category { get; set; } = "General";
        [Export] public Godot.Collections.Array<string> Tags { get; set; } = new();

        [ExportGroup("Stacking")]
        [Export(PropertyHint.Range, "1,9999,1")] public int MaxStackSize { get; set; } = 99;

        public bool HasTag(string tag) => Tags.Contains(tag);
    }
}

