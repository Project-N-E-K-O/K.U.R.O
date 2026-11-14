using Godot;
namespace Kuros.Fx;

/// <summary>坠落外观配置；基础变换位于 Sprite 上，不被父节点的通用动画覆盖。</summary>
[GlobalClass]
public partial class ItemFallVisualProfile : Resource
{
    [Export] public string Key { get; set; } = "";
    [Export] public Texture2D? Texture { get; set; }
    [Export] public Vector2 Position { get; set; }
    [Export] public Vector2 Scale { get; set; } = Vector2.One;
    [Export] public float Rotation { get; set; }
    [Export] public Vector2 Offset { get; set; }
    [Export] public bool Centered { get; set; } = true;
    [Export] public bool FlipH { get; set; }
    [Export] public bool FlipV { get; set; }
    [Export] public bool RegionEnabled { get; set; }
    [Export] public Rect2 RegionRect { get; set; }
    [Export] public int Hframes { get; set; } = 1;
    [Export] public int Vframes { get; set; } = 1;
    [Export] public int Frame { get; set; }
    [Export] public Color Modulate { get; set; } = Colors.White;
    [Export] public Color SelfModulate { get; set; } = Colors.White;
    [Export] public Material? Material { get; set; }

    public void Apply(Sprite2D sprite)
    {
        sprite.Texture = Texture; sprite.Position = Position; sprite.Scale = Scale; sprite.Rotation = Rotation;
        sprite.Offset = Offset; sprite.Centered = Centered; sprite.FlipH = FlipH; sprite.FlipV = FlipV;
        sprite.RegionEnabled = RegionEnabled; sprite.RegionRect = RegionRect;
        sprite.Hframes = Hframes; sprite.Vframes = Vframes; sprite.Frame = Frame;
        sprite.Modulate = Modulate; sprite.SelfModulate = SelfModulate; sprite.Material = Material;
    }
}
