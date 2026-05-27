shader_type canvas_item;

// 扭曲需要采样屏幕纹理
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, repeat_disable, filter_linear;

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
/// 空间扭曲强度（波纹经过时对背景的折射偏移量，UV 空间；0 = 关闭）
uniform float distortion_strength : hint_range(0.0, 0.04) = 0.012;
// ─────────────────────────────────────────────────────────

void fragment() {
    // 将 UV 映射到 [-1, 1]，再按椭圆比例缩放
    vec2 uv = UV * 2.0 - 1.0;
    uv.y /= aspect_y;
    float dist = length(uv) * 0.5; // 归一到 [0, ~0.5]

    // 径向单位向量（扭曲偏移方向）
    vec2 radial_dir = normalize(UV * 2.0 - 1.0 + vec2(0.00001));

    float alpha          = 0.0;
    float distort_signed = 0.0; // 正=径向向外推, 负=向内拉

    for (int i = 0; i < ring_count; i++) {
        float phase  = fract(TIME * speed + float(i) / float(ring_count));
        float radius = phase * max_radius;

        // 有符号距离：正=在圈外侧, 负=在圈内侧
        float delta = dist - radius;
        float ring  = exp(-delta * delta / (ring_width * ring_width));

        float fade_out = 1.0 - phase;
        float fade_in  = smoothstep(inner_dead, inner_dead + 0.04, dist);
        float ring_val = ring * fade_out * fade_in;

        alpha          += ring_val;
        // 圈外侧向外推（正），内侧向内拉（负），模拟压力波的双向折射
        distort_signed += ring_val * sign(delta);
    }

    // 边缘软裁剪
    float edge_clip   = 1.0 - smoothstep(max_radius - ring_width, max_radius, dist);
    alpha            = clamp(alpha * intensity * edge_clip, 0.0, 1.0);
    distort_signed  *= intensity * edge_clip;

    // ── 背景折射采样 ─────────────────────────────────────
    vec2  screen_uv_warped = SCREEN_UV + radial_dir * distort_signed * distortion_strength;
    vec4  screen_col       = texture(SCREEN_TEXTURE, screen_uv_warped);

    // 波纹颜色叠加在折射背景上
    float ring_blend = alpha * ring_color.a;
    vec3  out_rgb    = mix(screen_col.rgb, ring_color.rgb, ring_blend);

    // 只在波纹带及其折射影响区可见，其余保持透明
    float visibility = clamp(ring_blend + abs(distort_signed) * 0.7, 0.0, 1.0);

    COLOR = vec4(out_rgb, visibility);
}

