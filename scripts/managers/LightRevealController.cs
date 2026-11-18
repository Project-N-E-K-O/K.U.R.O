using Godot;

namespace Kuros.Managers
{
    /// <summary>
    /// 逐盏亮灯控制器（暗层 + 光圈坐标窗口版）：
    /// - 暗层：单个 Sprite2D 黑色贴图覆盖整个房间，挂 dark_mask shader 模拟关灯状态。
    /// - 光圈：N 个节点（任意 Node2D/Sprite2D）只提供世界坐标——暗层 shader 把光圈位置处的
    ///   圆形区域 alpha 归零（完全露出场景），其余保持暗影。
    ///   玩家进入触发区后，光圈半径依次从 0 扩散到目标值 → 房间被一盏一盏真正照亮。
    /// </summary>
    [GlobalClass]
    public partial class LightRevealController : Node2D
    {
        [Export] public NodePath DarkLayerPath { get; set; } = new();
        /// <summary>
        /// 光圈分组（嵌套数组）：每组一个路径数组——同组所有灯**同时**亮起（共享同一延迟），
        /// 组与组之间按预热/全亮各自序列的间隔错峰。节点只取坐标，不渲染自身。
        /// </summary>
        [Export] public Godot.Collections.Array<Godot.Collections.Array<NodePath>> SpotGroups { get; set; } = new();
        [Export] public NodePath TriggerAreaPath { get; set; } = new();
        /// <summary>暗层遮罩 shader（空 = 自动加载 dark_mask.gdshader）。</summary>
        [Export] public Shader? DarkMaskShader { get; set; }
        /// <summary>阴影色（圆外暗影；圆内 alpha 归零露出场景）。</summary>
        [Export] public Color DarkColor { get; set; } = new(0f, 0f, 0f, 0.85f);
        /// <summary>第一段（预热）灯光半径：触发后先扩散到此半径，预热结束后进入第二段全亮。</summary>
        [Export(PropertyHint.Range, "0,3000,5")] public float PreheatRadius { get; set; } = 200f;
        /// <summary>第一段（预热）圆边缘柔化宽度（px）。预热结束后过渡到 Softness。</summary>
        [Export(PropertyHint.Range, "0,5000,5")] public float PreheatSoftness { get; set; } = 300f;
        /// <summary>灯光照亮半径（px，圆内完全透明露出场景）。</summary>
        [Export(PropertyHint.Range, "0,3000,5")] public float SpotRadius { get; set; } = 500f;
        /// <summary>圆边缘柔化宽度（px）。</summary>
        [Export(PropertyHint.Range, "0,5000,5")] public float Softness { get; set; } = 80f;
        /// <summary>半径淡入时长（秒）：每个阶段里 radius 先变化，默认 0.1s。</summary>
        [Export(PropertyHint.Range, "0.01,3,0.01")] public float RadiusFadeSeconds { get; set; } = 0.1f;
        /// <summary>软度淡入时长（秒）：radius 变化完成后，softness 再花这段时间过渡，默认 0.4s。</summary>
        [Export(PropertyHint.Range, "0.01,3,0.01")] public float SoftnessFadeSeconds { get; set; } = 0.4f;
        /// <summary>预热序列：第一组的开始延迟（秒，从触发算起）。</summary>
        [Export(PropertyHint.Range, "0,20,0.05")] public float PreheatStartDelaySeconds { get; set; }
        /// <summary>预热序列：相邻两组的开始间隔（秒）；小于单组时长时自动顺延，保证不重叠。</summary>
        [Export(PropertyHint.Range, "0,20,0.05")] public float PreheatIntervalSeconds { get; set; } = 1f;
        /// <summary>全亮序列：第一组的开始延迟（秒，从触发算起）；与预热序列相互独立。</summary>
        [Export(PropertyHint.Range, "0,30,0.05")] public float SpotStartDelaySeconds { get; set; } = 2f;
        /// <summary>全亮序列：相邻两组的开始间隔（秒）；同样自动顺延避免重叠。</summary>
        [Export(PropertyHint.Range, "0,20,0.05")] public float SpotIntervalSeconds { get; set; } = 1f;

        private const string DefaultMaskShaderPath = "res://shaders/materials/dark_mask.gdshader";
        private const int MaxLights = 16;
        private bool _started;
        private Sprite2D? _darkLayer;
        private ShaderMaterial? _maskMaterial;
        private readonly Godot.Collections.Array<Node2D> _spots = new();
        private readonly Godot.Collections.Array<int> _spotGroups = new();
        private readonly float[] _radii = new float[MaxLights];
        private readonly float[] _softnesses = new float[MaxLights];

        public override void _Ready()
        {
            if (DarkLayerPath != null && !DarkLayerPath.IsEmpty)
            {
                _darkLayer = GetNodeOrNull<CanvasItem>(DarkLayerPath) as Sprite2D;
            }
            if (_darkLayer == null)
            {
                GD.PrintErr("[LightReveal] 暗层 Sprite2D 未找到（需设置黑色贴图纹理）");
                return;
            }

            _darkLayer.Modulate = new Color(1f, 1f, 1f, 1f); // 颜色由 shader 统一控制

            var shader = DarkMaskShader ?? GD.Load<Shader>(DefaultMaskShaderPath);
            if (shader == null)
            {
                GD.PrintErr($"[LightReveal] 遮罩 shader 加载失败: {DefaultMaskShaderPath}");
                return;
            }

            _maskMaterial = new ShaderMaterial { Shader = shader };
            _darkLayer.Material = _maskMaterial;

            int index = 0;
            for (int groupIndex = 0; groupIndex < SpotGroups.Count; groupIndex++)
            {
                var group = SpotGroups[groupIndex];
                if (group == null) continue;
                foreach (var path in group)
                {
                    if (path == null || path.IsEmpty || index >= MaxLights) continue;
                    if (GetNodeOrNull<CanvasItem>(path) is Node2D spot)
                    {
                        _spots.Add(spot);
                        _spotGroups.Add(groupIndex);
                        _radii[index] = 0f;
                        _softnesses[index] = Softness;
                        index++;
                    }
                }
            }

            BindTrigger();
        }

        private void BindTrigger()
        {
            Area2D? trigger = null;
            if (TriggerAreaPath != null && !TriggerAreaPath.IsEmpty)
            {
                trigger = GetNodeOrNull<Area2D>(TriggerAreaPath);
            }
            if (trigger == null)
            {
                trigger = GetParent()?.GetNodeOrNull<Area2D>("LightTrigger");
            }
            if (trigger != null)
            {
                trigger.BodyEntered += OnTriggerBodyEntered;
            }
        }

        public override void _Process(double delta)
        {
            if (_maskMaterial == null || _darkLayer == null || _spots.Count == 0)
            {
                return;
            }

            // 每帧同步光圈世界坐标（转暗层局部空间）、当前半径与软度到 shader
            var positions = new Godot.Collections.Array<Vector2>();
            var radii = new Godot.Collections.Array<float>();
            var softnesses = new Godot.Collections.Array<float>();
            for (int i = 0; i < _spots.Count; i++)
            {
                positions.Add(_darkLayer.ToLocal(_spots[i].GlobalPosition));
                radii.Add(_radii[i]);
                softnesses.Add(_softnesses[i]);
            }
            _maskMaterial.SetShaderParameter("light_count", _spots.Count);
            _maskMaterial.SetShaderParameter("light_positions", positions);
            _maskMaterial.SetShaderParameter("light_radii", radii);
            _maskMaterial.SetShaderParameter("light_softness", softnesses);
            _maskMaterial.SetShaderParameter("dark_color", DarkColor);
        }

        private void OnTriggerBodyEntered(Node2D body)
        {
            if (_started) return;
            if (!body.IsInGroup("player")) return;
            StartReveal();
        }

        /// <summary>
        /// 开始逐排亮灯：预热与全亮是**两条独立时间线**，可以全局重叠（如预热 1~5s、全亮 3~7s）。
        /// 每个阶段内部先 radius（RadiusFadeSeconds，默认 0.1s），radius 完成后 softness
        /// （SoftnessFadeSeconds，默认 0.4s）。两条序列各自保证"上一组完全结束才轮到下一组"：
        /// 当设定间隔小于单组时长时，后一组自动顺延，不会与本组前一阶段或其他组重叠。
        /// </summary>
        public void StartReveal()
        {
            if (_started) return;
            _started = true;

            float stageSeconds = RadiusFadeSeconds + SoftnessFadeSeconds;

            int groupCount = 0;
            foreach (int groupIndex in _spotGroups)
                groupCount = Mathf.Max(groupCount, groupIndex + 1);
            if (groupCount <= 0) return;

            // 预热序列：组 g 起点 = max(设定节奏, 上一组结束)；组内固定占 stageSeconds。
            var preheatStart = new float[groupCount];
            var preheatEnd = new float[groupCount];
            float cursor = 0f;
            for (int g = 0; g < groupCount; g++)
            {
                preheatStart[g] = Mathf.Max(PreheatStartDelaySeconds + g * PreheatIntervalSeconds, cursor);
                preheatEnd[g] = preheatStart[g] + stageSeconds;
                cursor = preheatEnd[g];
            }

            // 全亮序列：独立节奏，且不早于本组预热结束、不早于上一组全亮结束。
            var spotStart = new float[groupCount];
            cursor = 0f;
            for (int g = 0; g < groupCount; g++)
            {
                spotStart[g] = Mathf.Max(
                    Mathf.Max(SpotStartDelaySeconds + g * SpotIntervalSeconds, cursor),
                    preheatEnd[g]);
                cursor = spotStart[g] + stageSeconds;
            }

            for (int i = 0; i < _spots.Count; i++)
            {
                int index = i; // 捕获局部副本
                int group = _spotGroups[i];
                var tween = CreateTween();
                tween.TweenInterval(preheatStart[group]);
                // 预热：radius 先变（短）
                tween.TweenMethod(Callable.From<float>(value => _radii[index] = value), 0f, PreheatRadius, RadiusFadeSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                // radius 完成后再过渡 softness（长）
                tween.TweenMethod(Callable.From<float>(value => _softnesses[index] = value), Softness, PreheatSoftness, SoftnessFadeSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                // 等待全亮序列轮到本组；期间保持预热值
                tween.TweenInterval(Mathf.Max(0f, spotStart[group] - preheatEnd[group]));
                // 全亮：同样 radius 先变（短）
                tween.TweenMethod(Callable.From<float>(value => _radii[index] = value), PreheatRadius, SpotRadius, RadiusFadeSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                // radius 完成后再过渡 softness（长）
                tween.TweenMethod(Callable.From<float>(value => _softnesses[index] = value), PreheatSoftness, Softness, SoftnessFadeSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
            }
        }
    }
}
