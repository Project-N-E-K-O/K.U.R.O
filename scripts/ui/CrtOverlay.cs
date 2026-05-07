using Godot;
using Kuros.Managers;

namespace Kuros.UI
{
    /// <summary>
    /// CRT 滤镜覆盖层 — 作为 Autoload 全局存在，通过 GameSettingsManager.CrtEnabled 控制显隐。
    /// 场景结构：CanvasLayer (layer=127) → ColorRect (全屏，ShaderMaterial = crt_effect.gdshader)
    /// 所有 shader uniform 均在此处以 [Export] 暴露，可在 Inspector 中实时调整。
    /// </summary>
    public partial class CrtOverlay : CanvasLayer
    {
        // ── 扫描线 ────────────────────────────────────────────────────────
        [ExportCategory("Scanlines")]
        [Export(PropertyHint.Range, "0,1")] public float ScanlinesOpacity  { get; set; } = 0.15f;       // 扫描线的透明度
        [Export(PropertyHint.Range, "0,0.5")] public float ScanlinesWidth  { get; set; } = 0.25f;       // 扫描线的宽度

        // ── 像素格栅 ──────────────────────────────────────────────────────
        [ExportCategory("Grille")]
        [Export(PropertyHint.Range, "0,1")] public float GrilleOpacity     { get; set; } = 0.05f;       // 格栅的透明度
        [Export] public Vector2 Resolution { get; set; } = new Vector2(1080, 1080);                     // 用于控制格栅的密度和大小，过高会导致格栅过于细密，过低则不明显
        [Export] public bool Pixelate      { get; set; } = false;                                       // 是否启用像素化效果，启用后会将画面分辨率降低到 Resolution 设置的值，模拟 CRT 显示器的像素格栅效果。

        // ── 滚动噪声 ──────────────────────────────────────────────────────
        [ExportCategory("Roll / Noise")]
        [Export] public bool Roll          { get; set; } = false;
        [Export] public float RollSpeed    { get; set; } = 8.0f;
        [Export(PropertyHint.Range, "0,100")] public float RollSize        { get; set; } = 15.0f;       // 过大会导致滚动过快过大，过小则不明显
        [Export(PropertyHint.Range, "0.1,5")] public float RollVariation   { get; set; } = 1.8f;        // 用于调整滚动噪声的频率和幅度，增加随机感，避免过于机械化的滚动效果
        [Export(PropertyHint.Range, "0,0.2")] public float DistortIntensity { get; set; } = 0.0f;       // 用于调整滚动噪声对画面的扭曲强度，过大会导致画面过于混乱，过小则不明显
        [Export(PropertyHint.Range, "0,1")] public float NoiseOpacity      { get; set; } = 0.0f;        // 滚动噪声的透明度，过大会导致画面过于模糊，过小则不明显
        [Export] public float NoiseSpeed   { get; set; } = 5.0f;                                        // 滚动噪声的速度，过快会导致画面过于混乱，过慢则不明显
        [Export(PropertyHint.Range, "0,1")] public float StaticNoiseIntensity { get; set; } = 0.02f;    // 静态噪声的强度，过大会导致画面过于模糊，过小则不明显

        // ── 色差 / 亮度 / 色彩 ───────────────────────────────────────────
        [ExportCategory("Color & Aberration")]
        [Export(PropertyHint.Range, "-1,1")] public float Aberration       { get; set; } = 0.01f;       // 色差强度
        [Export] public float Brightness   { get; set; } = 1.05f;                                       // 用于调整整体亮度，过大会导致画面过曝，过小则过暗
        [Export] public bool Discolor      { get; set; } = false;                                       // 是否启用色彩失真效果，启用后会在 shader 中对颜色进行随机扰动，模拟老式 CRT 显示器的色彩不稳定现象。

        // ── 变形 / 晕影 ───────────────────────────────────────────────────
        [ExportCategory("Warp & Vignette")]
        [Export(PropertyHint.Range, "0,5")] public float WarpAmount        { get; set; } = 0.3f;        // 变形强度。启用后会对画面进行桶形或枕形失真，模拟 CRT 显示器的几何畸变效果。
        [Export] public bool ClipWarp      { get; set; } = false;                                       // 是否裁剪变形效果，启用后会限制变形在屏幕范围内
        [Export] public float VignetteIntensity { get; set; } = 0.3f;                                   // 暗角强度，过大会导致画面过暗，过小则不明显
        [Export(PropertyHint.Range, "0,1")] public float VignetteOpacity   { get; set; } = 0.25f;       // 暗角透明度，过大会导致画面过暗，过小则不明显

        // ── 内部 ──────────────────────────────────────────────────────────
        private ColorRect?      _rect;
        private ShaderMaterial? _mat;

        public override void _Ready()
        {
            Layer       = 127;
            ProcessMode = ProcessModeEnum.Always;

            _rect = GetNodeOrNull<ColorRect>("ColorRect");
            if (_rect == null)
            {
                GD.PrintErr("CrtOverlay: 未找到子节点 ColorRect");
                return;
            }

            var shader = GD.Load<Shader>("res://shaders/ui/crt_effect.gdshader");
            if (shader == null)
            {
                GD.PrintErr("CrtOverlay: 无法加载 res://shaders/ui/crt_effect.gdshader");
                return;
            }

            _mat = new ShaderMaterial { Shader = shader };
            _rect.Material = _mat;
            ApplyShaderParams();

            ApplyState(GameSettingsManager.Instance?.CrtEnabled ?? false);

            if (GameSettingsManager.Instance != null)
                GameSettingsManager.Instance.CrtEnabledChanged += ApplyState;
        }

        public override void _ExitTree()
        {
            if (GameSettingsManager.Instance != null)
                GameSettingsManager.Instance.CrtEnabledChanged -= ApplyState;
        }

        /// <summary>将所有导出属性同步到 ShaderMaterial。编辑器或运行时修改属性后调用。</summary>
        public void ApplyShaderParams()
        {
            if (_mat == null) return;
            _mat.SetShaderParameter("overlay",               true);
            _mat.SetShaderParameter("scanlines_opacity",     ScanlinesOpacity);
            _mat.SetShaderParameter("scanlines_width",       ScanlinesWidth);
            _mat.SetShaderParameter("grille_opacity",        GrilleOpacity);
            _mat.SetShaderParameter("resolution",            Resolution);
            _mat.SetShaderParameter("pixelate",              Pixelate);
            _mat.SetShaderParameter("roll",                  Roll);
            _mat.SetShaderParameter("roll_speed",            RollSpeed);
            _mat.SetShaderParameter("roll_size",             RollSize);
            _mat.SetShaderParameter("roll_variation",        RollVariation);
            _mat.SetShaderParameter("distort_intensity",     DistortIntensity);
            _mat.SetShaderParameter("noise_opacity",         NoiseOpacity);
            _mat.SetShaderParameter("noise_speed",           NoiseSpeed);
            _mat.SetShaderParameter("static_noise_intensity",StaticNoiseIntensity);
            _mat.SetShaderParameter("aberration",            Aberration);
            _mat.SetShaderParameter("brightness",            Brightness);
            _mat.SetShaderParameter("discolor",              Discolor);
            _mat.SetShaderParameter("warp_amount",           WarpAmount);
            _mat.SetShaderParameter("clip_warp",             ClipWarp);
            _mat.SetShaderParameter("vignette_intensity",    VignetteIntensity);
            _mat.SetShaderParameter("vignette_opacity",      VignetteOpacity);
        }

        private void ApplyState(bool enabled)
        {
            if (_rect != null)
                _rect.Visible = enabled;
        }
    }
}
