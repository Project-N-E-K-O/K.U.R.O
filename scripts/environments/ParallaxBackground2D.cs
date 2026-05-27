using Godot;

namespace Kuros.Environments;

/// <summary>
/// 视差背景层，挂载于 Sprite2D 节点。
/// 根据摄像机 X 轴位置乘以 <see cref="ScrollSpeed"/> 产生深度视差位移。
/// <br/>
/// ScrollSpeed = 0：背景完全静止（无限远景）<br/>
/// ScrollSpeed = 1：随摄像机等速移动（固定于屏幕）<br/>
/// 推荐取值：远景 0.1–0.2，中景 0.3–0.5
/// </summary>
public partial class ParallaxBackground2D : Sprite2D
{
    /// <summary>视差滚动因子 N，范围 [0, 1]</summary>
    [Export] public float ScrollSpeed { get; set; } = 0.3f;

    /// <summary>
    /// 生效区域 Area2D 的节点路径。摄像机在该区域内时视差激活，离开后背景恢复原始世界坐标。
    /// 区域需含一个 CollisionShape2D（RectangleShape2D）子节点作为边界。
    /// 留空则视差始终生效。
    /// </summary>
    [Export] public NodePath AreaPath { get; set; } = new NodePath();

    private Camera2D? _camera;
    private float _originX;       // 视差基准偏移
    private bool _initialized;
    private bool _isActive;       // 摄像机当前是否在生效区域内

    private Rect2 _activeBounds;  // 生效区域的世界坐标矩形
    private bool _hasBounds;

    public override void _Ready()
    {
        // _Ready 时房间尚未被 StageGeneratorManager 定位，延迟到第一帧 _Process 初始化
        _isActive = AreaPath.IsEmpty; // 无区域限制则始终激活
    }

    public override void _Process(double delta)
    {
        if (!_initialized) TryInitialize();
        if (_camera == null) return;

        // 使用实际渲染视口中心（已受 CameraZone Limit 钳制），而非相机节点期望的跟随位置
        var screenCenter = _camera.GetScreenCenterPosition();

        if (_hasBounds)
            _isActive = _activeBounds.HasPoint(screenCenter);

        // 区域外：直接返回，背景停留在离开时的位置
        if (!_isActive) return;

        GlobalPosition = new Vector2(
            _originX + screenCenter.X * ScrollSpeed,
            GlobalPosition.Y
        );
    }

    private void TryInitialize()
    {
        _camera = GetViewport().GetCamera2D();
        if (_camera == null) return;

        // 此时房间已由 StageGeneratorManager 定位，GlobalPosition 为正确的世界坐标
        // 用 GetScreenCenterPosition() 作为基准，与 _Process 中保持一致
        _originX = GlobalPosition.X - _camera.GetScreenCenterPosition().X * ScrollSpeed;

        // 解析区域矩形（世界坐标）
        if (!AreaPath.IsEmpty)
        {
            var area = GetNodeOrNull<Area2D>(AreaPath);
            if (area != null)
            {
                var shape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (shape?.Shape is RectangleShape2D rect)
                {
                    // 用 CollisionShape2D 世界坐标作为矩形中心
                    _activeBounds = new Rect2(shape.GlobalPosition - rect.Size / 2f, rect.Size);
                    _hasBounds = true;
                }
                else
                {
                    GD.PushWarning($"[ParallaxBackground2D] AreaPath 中未找到 RectangleShape2D，视差将始终生效：{AreaPath}");
                    _isActive = true;
                }
            }
            else
            {
                GD.PushWarning($"[ParallaxBackground2D] 未找到 Area2D 节点，视差将始终生效：{AreaPath}");
                _isActive = true;
            }
        }

        _initialized = true;
    }
}
