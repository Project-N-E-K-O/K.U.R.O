using Godot;
namespace Kuros.Fx;

/// <summary>fall_guard1 等旧名称 → 敌人坠落外观。动画本身始终是通用 fall，配置资源实例间共享。</summary>
[GlobalClass]
public partial class EnemyFallVisualCatalog : Resource
{
    [Export] public Godot.Collections.Array<EnemyFallVisualProfile> Profiles { get; set; } = new();

    public EnemyFallVisualProfile? Resolve(string key)
    {
        foreach (var profile in Profiles)
            if (profile != null && profile.Key == key) return profile;
        return null;
    }
}
