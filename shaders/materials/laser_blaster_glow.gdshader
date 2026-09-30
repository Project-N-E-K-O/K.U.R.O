shader_type canvas_item;

uniform float glow_intensity : hint_range(0.0, 50.0) = 6.0;
uniform float core_width : hint_range(0.0, 1.0) = 0.5;
// 沿光束（UV.x）两端的渐隐，可分别设置：
//   core_length       = **尾端**渐隐起点（0.5 = 后半段渐隐；沿用原语义，原材质都靠它）
//   core_start_length = **首端**渐隐长度（0 = 首端不淡出 = 原效果；0.3 = 前 30% 渐显）
uniform float core_length : hint_range(0.0, 1.0) = 0.5;
uniform float core_start_length : hint_range(0.0, 1.0) = 0.0;
uniform float fade : hint_range(0.0, 1.0) = 1.0;

// ── 流动（默认完全不生效：flow_strength = 0 时与原版输出逐像素一致）──
// 噪波贴图：建议无缝噪波（NoiseTexture2D seamless=true）；未设置时 hint 给白色 → 不影响画面
uniform sampler2D flow_noise : hint_default_white, repeat_enable, filter_linear;
// 流动速度（UV/秒）：x = 沿光束长度方向、y = 横跨宽度方向——两个轴可分别设置（填 0 = 该轴不流动）
uniform vec2 flow_speed = vec2(0.0, 0.0);
// 噪波平铺密度：1 = 整条光束一张图（很疏）；越大越碎（超长光束要调大）
uniform float flow_scale : hint_range(0.1, 100.0) = 8.0;
// 流动强度：0 = 关闭（原版），1 = 明暗对比最强
uniform float flow_strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    float y_dist = abs(UV.y - 0.5);

    float core_y_mask = 1.0 - smoothstep(0.0, core_width, y_dist);
    // 尾端：从 core_length 起渐隐到末端
    float core_tail_mask = 1.0 - smoothstep(core_length, 1.0, UV.x);
    // 首端：从起点渐显到 core_start_length；max(...) 避免 core_start_length = 0 时 smoothstep 边界重合（未定义值），
    // 此时除数取极小值 → 结果恒为 1 = 首端不淡出（原效果）
    float core_head_mask = smoothstep(0.0, max(core_start_length, 0.0001), UV.x);
    float core_mask = core_y_mask * core_tail_mask * core_head_mask;
    vec4 core_color = COLOR * glow_intensity;
    core_color.a = core_mask * fade;

    if (flow_strength > 0.0) {
        // 噪波随 TIME 沿 X/Y 滚动；*2 让 0.5 灰 = 原亮度（明暗两侧都保留，不整体提亮）
        vec2 flow_uv = UV * flow_scale + TIME * flow_speed;
        float noise = texture(flow_noise, flow_uv).r * 2.0;
        core_color *= mix(1.0, noise, flow_strength);
    }

    COLOR = core_color;
}
