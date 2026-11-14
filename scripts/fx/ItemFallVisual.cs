using Godot;
namespace Kuros.Fx;

/// <summary>七节点通用物品替身；先注入外观再入树，兼容原 AnimationName=fall_xxx 的生成链。</summary>
public partial class ItemFallVisual : DirectionalSceneEffect
{
    [Export] public ItemFallVisualCatalog? Catalog { get; set; }
    [Export] public ItemFallVisualProfile? VisualProfile { get; set; }
    private bool _configured;

    public void ConfigureForKey(string key, ItemFallVisualProfile? profile = null)
    {
        var selected = profile ?? VisualProfile ?? Catalog?.Resolve(key);
        if (selected == null)
        {
            selected = Catalog?.Resolve("fall_small");
            if (key != "fall" && key != "" && selected != null) GD.PushWarning($"[ItemFallVisual] 未找到外观键 {key}，使用 fall_small。");
        }
        selected?.Apply(GetNode<Sprite2D>("ItemPosition/Sprite2D"));
        // 星光 shader 的 alpha 由动画改变，必须实例独立，不能串扰其他同时播放的替身。
        foreach (Node child in GetNode("effect").GetChildren())
            if (child is CanvasItem item && item.Material != null) item.Material = (Material)item.Material.Duplicate();
        AnimationName = "fall";
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured) ConfigureForKey(AnimationName);
        base._Ready();
    }
}
