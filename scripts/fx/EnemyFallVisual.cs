using Godot;
namespace Kuros.Fx;

/// <summary>单槽位敌人坠落替身：按 key 切换 Spine 骨架（入树前），再循环播放 stun；
/// 通用 fall 动画只驱动父节点 Position，SpineSprite 的 Y 抬升直接写它的 Position。</summary>
public partial class EnemyFallVisual : DirectionalSceneEffect
{
    [Export] public EnemyFallVisualCatalog? Catalog { get; set; }
    [Export] public EnemyFallVisualProfile? VisualProfile { get; set; }

    private bool _configured;
    private EnemyFallVisualProfile? _profile;

    /// <summary>生成方在 AddChild 之前调用：切换骨架与基础变换，并把动画统一成 fall。
    /// 兼容原 FallAnimationName=fall_guard1 的调用链。</summary>
    public void ConfigureForKey(string key, EnemyFallVisualProfile? profile = null)
    {
        var selected = profile ?? VisualProfile ?? Catalog?.Resolve(key);
        if (selected == null)
        {
            GD.PushWarning($"[EnemyFallVisual] 未找到外观键 {key}");
        }
        else
        {
            _profile = selected;

            // SpineSprite 没有 offset 属性：Y 抬升直接写 Position（旋转/缩放由父节点动画负责）。
            var spine = GetNodeOrNull<Node>("Position/SpineSprite");
            if (spine != null)
            {
                if (selected.SkeletonDataResource != null)
                    spine.Set("skeleton_data_res", selected.SkeletonDataResource);
                spine.Set("position", selected.SpinePosition);
                spine.Set("scale", selected.SpineScale);
            }

            // 描边层：四个深色骨架副本，需与主视觉用同一套骨架/同一位置缩放。
            // stun 由 SpineWrapper.play_animation 统一下发，会自动包含 OutlineSpineSprite*。
            var outline = GetNodeOrNull<Node2D>("Position/OutlineLayer");
            if (outline != null)
            {
                outline.Position = selected.SpinePosition;
                outline.Scale = selected.SpineScale;
                if (selected.SkeletonDataResource != null)
                    foreach (Node child in outline.FindChildren("OutlineSpineSprite*", "SpineSprite", true, false))
                        child.Set("skeleton_data_res", selected.SkeletonDataResource);
            }

            // 星光 shader 的 alpha 由动画改变，必须实例独立，避免多个坠落替身串扰。
            if (GetNodeOrNull<Node>("effect") is Node effect)
                foreach (Node child in effect.GetChildren())
                    if (child is CanvasItem item && item.Material != null)
                        item.Material = (Material)item.Material.Duplicate();
        }

        AnimationName = "fall";
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured) ConfigureForKey(AnimationName);
        base._Ready();
        // 子节点（SpineSprite）已 Ready，state 可用后再播循环；延后一帧更稳。
        Callable.From(PlaySpineLoop).CallDeferred();
    }

    private void PlaySpineLoop()
    {
        if (_profile == null || !IsInsideTree()) return;
        var gd = GD.Load<GDScript>("res://scripts/utils/SpineWrapper.gd");
        if (gd == null) return;
        var helper = (Node)gd.New();
        AddChild(helper);
        helper.Call("play_animation", this, _profile.SpineAnimationName, true);
    }
}
