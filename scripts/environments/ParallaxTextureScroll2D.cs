using Godot;

namespace Kuros.Environments;

/// <summary>
/// 世界空间"原地滚动"视差背景（Sprite2D + parallax_texture_scroll.gdshader）：
/// 节点本身不动，按相机屏幕中心的位移滚动贴图 UV，视觉上图案相对相机缓慢移动。
/// <br/>
/// ScrollSpeed 语义与 <see cref="ParallaxBackground2D"/> 一致：1 = 图案随相机等速移动（屏幕上看不动），
/// 0 = 完全静止；推荐远景 0.1–0.2、中景 0.3–0.5。默认只滚 X（ScrollY 可开启 Y 轴）。
/// <br/>
/// 需要 ShaderMaterial；_Ready 会复制材质保证每个实例的 offset 独立，
/// 若材质的 pattern_texture 未设置则自动取 Sprite2D 的 Texture。
/// 配置 AreaPath 后，玩家进入时启用滚动，玩家离开时保留偏移。每次启用重新记录相机起点。
/// </summary>
public partial class ParallaxTextureScroll2D : Sprite2D
{
    /// <summary>视差滚动因子 N，范围 [0, 1]（语义同 ParallaxBackground2D）。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ScrollSpeed { get; set; } = 0.3f;

    /// <summary>是否随相机水平位移滚动。</summary>
    [Export] public bool ScrollX { get; set; } = true;
    /// <summary>是否随相机垂直位移滚动。</summary>
    [Export] public bool ScrollY { get; set; }

    /// <summary>一个平铺块的世界宽度（像素）；高度按绘制区域宽高比推导。
    /// 0 = 自动：一个平铺块 = Sprite 绘制区域的世界尺寸（不额外平铺，整张素材原地滚动）。</summary>
    [Export(PropertyHint.Range, "0,8192,1")] public float TileWorldWidth { get; set; }

    /// <summary>
    /// 生效区域 Area2D 的节点路径。监测到 player 组的物理体时视差激活，
    /// 离开后停止滚动（保留当前偏移）。留空则始终生效。
    /// </summary>
    [Export] public NodePath AreaPath { get; set; } = new NodePath();

    private Area2D? _area;
    private Camera2D? _lastCamera;
    private ShaderMaterial? _material;
    private Vector2 _lastScreenCenter;
    private Vector2 _scrollWorld;
    private int _playersInArea;

    public override void _Ready()
    {
        if (Material is not ShaderMaterial source)
        {
            GD.PushWarning($"[{nameof(ParallaxTextureScroll2D)}] {Name}: 需要 ShaderMaterial（parallax_texture_scroll.gdshader）");
            SetProcess(false);
            return;
        }

        // 每面墙独立保存自己的滚动偏移。
        _material = (ShaderMaterial)source.Duplicate();
        Material = _material;
        if (_material.GetShaderParameter("pattern_texture").VariantType == Variant.Type.Nil && Texture != null)
            _material.SetShaderParameter("pattern_texture", Texture);
        UpdateShaderParameters();

        if (AreaPath.IsEmpty) return;
        _area = GetNodeOrNull<Area2D>(AreaPath);
        if (_area == null)
        {
            GD.PushWarning($"[{nameof(ParallaxTextureScroll2D)}] 未找到 Area2D 节点，视差将始终生效：{AreaPath}");
            return;
        }

        _area.BodyEntered += OnBodyEntered;
        _area.BodyExited += OnBodyExited;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (_area == null || !IsInstanceValid(_area)) return;
        _area.BodyEntered -= OnBodyEntered;
        _area.BodyExited -= OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!body.IsInGroup("player")) return;
        _playersInArea++;
        if (_playersInArea == 1) SetProcess(true);
    }

    private void OnBodyExited(Node2D body)
    {
        if (!body.IsInGroup("player")) return;
        _playersInArea = Mathf.Max(0, _playersInArea - 1);
        if (_playersInArea != 0) return;

        _lastCamera = null;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera2D();
        if (camera == null)
        {
            _lastCamera = null;
            return;
        }

        // 用实际渲染视口中心（受 CameraZone Limit 钳制后的位置）作为相机位移
        Vector2 screenCenter = camera.GetScreenCenterPosition();
        if (camera == _lastCamera)
        {
            Vector2 cameraDelta = screenCenter - _lastScreenCenter;
            _scrollWorld += new Vector2(
                ScrollX ? cameraDelta.X : 0f,
                ScrollY ? cameraDelta.Y : 0f) * ScrollSpeed;
        }

        // 玩家进区或视口更换相机时，从当前画面中心开始累计。
        _lastCamera = camera;
        _lastScreenCenter = screenCenter;
        UpdateShaderParameters();
    }

    /// <summary>按当前绘制区域/缩放/平铺尺寸换算 shader 参数。</summary>
    private void UpdateShaderParameters()
    {
        if (_material == null || Texture == null) return;

        Vector2 textureSize = Texture.GetSize();
        if (textureSize.X <= 0f || textureSize.Y <= 0f) return;

        // 绘制区域像素尺寸：region_enabled 时是裁剪区域，否则是整张贴图
        Vector2 drawnSize = GetRect().Size;
        if (drawnSize.X <= 0f || drawnSize.Y <= 0f) return;

        // 平铺块世界尺寸：0 = 自动（一个平铺块 = 整个绘制区域）；否则宽度取导出值、高度按绘制区域宽高比推导
        Vector2 worldSize = new(
            Mathf.Abs(drawnSize.X * GlobalScale.X),
            Mathf.Abs(drawnSize.Y * GlobalScale.Y));
        Vector2 tileWorld;
        if (TileWorldWidth > 0f)
        {
            float tileHeight = Mathf.Max(TileWorldWidth * drawnSize.Y / drawnSize.X, 0.001f);
            tileWorld = new(Mathf.Max(TileWorldWidth, 0.001f), tileHeight);
        }
        else
        {
            tileWorld = new(Mathf.Max(worldSize.X, 0.001f), Mathf.Max(worldSize.Y, 0.001f));
        }

        // 节点世界尺寸 / 平铺块 = UV 重复次数
        _material.SetShaderParameter("tile_scale", new Vector2(
            worldSize.X / tileWorld.X,
            worldSize.Y / tileWorld.Y));

        // 内容要随相机位移同向滑动 → 采样坐标反向偏移
        _material.SetShaderParameter("scroll_offset", -_scrollWorld / tileWorld);

        // 传递区域在整张贴图中的起点和大小，供 shader 换算局部 UV。
        if (RegionEnabled)
        {
            _material.SetShaderParameter("region_offset", RegionRect.Position / textureSize);
            _material.SetShaderParameter("region_uv_size", RegionRect.Size / textureSize);
        }
        else
        {
            _material.SetShaderParameter("region_offset", Vector2.Zero);
            _material.SetShaderParameter("region_uv_size", Vector2.One);
        }
    }
}
