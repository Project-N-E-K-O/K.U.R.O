shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, repeat_disable, filter_linear;

uniform float cone_angle_deg : hint_range(10.0, 180.0) = 45.0;
uniform float speed : hint_range(0.1, 5.0) = 1.5;
uniform int   ring_count : hint_range(1, 8) = 3;
uniform float ring_width : hint_range(0.01, 0.15) = 0.05;
uniform float max_dist : hint_range(0.1, 1.0) = 0.95;
uniform vec4  ring_color : source_color = vec4(0.75, 0.88, 1.0, 0.55);
uniform float intensity : hint_range(0.0, 3.0) = 1.0;
uniform float edge_fade_deg : hint_range(2.0, 60.0) = 15.0;
uniform float distortion_strength : hint_range(0.0, 0.04) = 0.012;

void fragment() {
    vec2 dir = UV - vec2(0.0, 0.5);
    float dist = length(dir);
    float angle_deg = abs(degrees(atan(dir.y, dir.x)));
    float half_cone = cone_angle_deg * 0.5;

    // 从锥形中心线向两侧边缘渐变淡出
    float cone_gradient = 1.0 - smoothstep(0.0, half_cone - edge_fade_deg * 0.5, angle_deg);
    // 边界处额外柔化裁剪
    float cone_clip = 1.0 - smoothstep(half_cone - edge_fade_deg, half_cone, angle_deg);
    float cone = cone_gradient * cone_clip;

    float range_start = smoothstep(0.0, 0.02, dist);
    float range_end = 1.0 - smoothstep(max_dist - 0.03, max_dist, dist);
    float range = range_start * range_end;
    float mask = cone * range;

    vec2 radial_dir = normalize(dir + vec2(0.00001));

    float alpha = 0.0;
    float distort_signed = 0.0;

    for (int i = 0; i < ring_count; i++) {
        float phase = fract(TIME * speed + float(i) / float(ring_count));
        float radius = phase * max_dist;
        float delta = dist - radius;
        float ring = exp(-delta * delta / (ring_width * ring_width));
        float fade = 1.0 - phase;
        float ring_val = ring * fade;
        alpha += ring_val;
        distort_signed += ring_val * sign(delta);
    }

    alpha = clamp(alpha * intensity * mask, 0.0, 1.0);
    distort_signed *= intensity * mask;

    vec2  screen_uv_warped = SCREEN_UV + radial_dir * distort_signed * distortion_strength;
    vec4  screen_col = texture(SCREEN_TEXTURE, screen_uv_warped);

    float ring_blend = alpha * ring_color.a;
    vec3  out_rgb = mix(screen_col.rgb, ring_color.rgb, ring_blend);
    float visibility = clamp(ring_blend + abs(distort_signed) * 0.7, 0.0, 1.0);

    COLOR = vec4(out_rgb, visibility);
}
