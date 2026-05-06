using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 挂在 GroundWash/WashRect (ColorRect) 上。
    /// 根据出租车离地高度动态调整波纹强度和速度。
    /// </summary>
    public partial class GroundWashRipple : ColorRect
    {
        /// <summary>出租车节点路径（相对于此节点的父节点树）。</summary>
        [Export] public NodePath TaxiPath { get; set; } = new NodePath("../..");

        /// <summary>
        /// 触发波纹的最大离地距离（像素）。
        /// 超过此值波纹完全消失。
        /// </summary>
        [Export] public float MaxHeight { get; set; } = 200f;

        /// <summary>
        /// 贴地时（高度=0）的波纹强度。
        /// </summary>
        [Export] public float MaxIntensity { get; set; } = 1.0f;

        /// <summary>
        /// 贴地时（高度=0）的波纹速度。
        /// </summary>
        [Export] public float MaxSpeed { get; set; } = 1.4f;

        // ── 地面的世界 Y 坐标（用于计算离地高度）──────────
        /// <summary>地面的全局 Y 坐标（像素）。</summary>
        [Export] public float GroundGlobalY { get; set; } = 300f;

        private ShaderMaterial? _mat;
        private Node2D? _taxi;

        public override void _Ready()
        {
            _mat  = (ShaderMaterial?)Material;
            _taxi = GetNodeOrNull<Node2D>(TaxiPath);

            if (_mat == null)
                GD.PushWarning("[GroundWashRipple] 未找到 ShaderMaterial，请在 MeshInstance2D 的 Material 属性上设置 ShaderMaterial。");
            if (_taxi == null)
                GD.PushWarning($"[GroundWashRipple] 未找到出租车节点，路径：{TaxiPath}");
        }

        public override void _Process(double delta)
        {
            if (_mat == null || _taxi == null) return;

            // 离地高度（出租车 Y 越小 = 越高，与地面 Y 的差值为高度）
            float height = Mathf.Max(0f, GroundGlobalY - _taxi.GlobalPosition.Y);
            float t = Mathf.Clamp(1f - height / MaxHeight, 0f, 1f);

            _mat.SetShaderParameter("intensity", MaxIntensity * t);
            _mat.SetShaderParameter("speed",     Mathf.Lerp(0.3f, MaxSpeed, t));

            // 高度为 0 时完全隐藏整个节点性能优化
            Visible = t > 0.01f;
        }
    }
}
