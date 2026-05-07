shader_type canvas_item;

// ── 波纹参数 ──────────────────────────────────────────────
/// 每秒扩散速度（值越大波纹跑越快）
uniform float speed : hint_range(0.1, 5.0) = 1.2;
/// 同屏显示的波纹圈数
uniform int   ring_count : hint_range(1, 8) = 3;
/// 单圈线宽（UV 空间，0~1）
uniform float ring_width : hint_range(0.005, 0.15) = 0.04;
/// 最大扩散半径（UV 空间，0.5 = 铺满整个 quad）
uniform float max_radius : hint_range(0.05, 0.5) = 0.46;
/// 中心死区半径（中心留白，模拟气流从引擎向外喷出）
uniform float inner_dead : hint_range(0.0, 0.3) = 0.06;
/// 波纹颜色（支持透明）
uniform vec4  ring_color  : source_color = vec4(0.75, 0.88, 1.0, 0.55);
/// 整体强度倍率
uniform float intensity   : hint_range(0.0, 3.0) = 1.0;
/// 椭圆压扁比（1.0=圆形, 0.5=压扁为俯视透视感）
uniform float aspect_y    : hint_range(0.1, 1.0) = 0.55;
// ─────────────────────────────────────────────────────────

void fragment() {
    // 将 UV 映射到 [-1, 1]，再按椭圆比例缩放
    vec2 uv = UV * 2.0 - 1.0;
    uv.y /= aspect_y;
    float dist = length(uv) * 0.5; // 归一到 [0, ~0.5]

    float alpha = 0.0;

    for (int i = 0; i < ring_count; i++) {
        // 每个圈有均匀相位偏移，形成连续向外扩散
        float phase  = fract(TIME * speed + float(i) / float(ring_count));
        float radius = phase * max_radius;

        // 高斯形状的圈（比 step 更柔和）
        float delta  = abs(dist - radius);
        float ring   = exp(-delta * delta / (ring_width * ring_width));

        // 越外越淡（相位 1.0 时完全透明）
        float fade_out  = 1.0 - phase;
        // 中心死区
        float fade_in   = smoothstep(inner_dead, inner_dead + 0.04, dist);

        alpha += ring * fade_out * fade_in;
    }

    // 边缘软裁剪（超出 max_radius 消隐）
    float edge_clip = 1.0 - smoothstep(max_radius - ring_width, max_radius, dist);

    alpha = clamp(alpha * intensity * edge_clip, 0.0, 1.0);

    COLOR = vec4(ring_color.rgb, COLOR.a * ring_color.a * alpha);
}
