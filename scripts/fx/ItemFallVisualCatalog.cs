using Godot;
namespace Kuros.Fx;

/// <summary>旧 fall_xxx 名称映射至外观，动画本身始终是 fall。配置资源在实例间共享。</summary>
[GlobalClass]
public partial class ItemFallVisualCatalog : Resource
{
    [Export] public Godot.Collections.Array<ItemFallVisualProfile> Profiles { get; set; } = new();
    public ItemFallVisualProfile? Resolve(string key)
    {
        foreach (var profile in Profiles) if (profile.Key == key) return profile;
        return null;
    }
}
