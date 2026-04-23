shader_type canvas_item;

// 采样屏幕背景以实现扭曲效果
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float strength = 0.3;         // 吸引强度
uniform float radius = 0.45;          // 影响范围（UV 空间 0~1）
uniform float swirl = 3.0;            // 旋转强度
uniform float time_scale = 1.0;

void fragment() {
    vec2 uv = UV;
    vec2 center = vec2(0.5);
    vec2 dir = uv - center;
    float dist = length(dir);
    vec2 norm_dir = dist > 0.0001 ? normalize(dir) : vec2(0.0, 1.0);

    float pull = smoothstep(radius, 0.0, dist);

    // 在 UV 空间计算吸引扭曲
    vec2 distorted = uv - norm_dir * pull * strength;

    // 旋转扭曲（黑洞漩涡）
    float angle = swirl * pull + TIME * time_scale;
    float s = sin(angle);
    float cv = cos(angle);
    mat2 rot = mat2(vec2(cv, -s), vec2(s, cv));
    distorted = center + rot * (distorted - center);

    // 将 UV 偏移映射为 SCREEN_UV 偏移后采样背景
    vec2 screen_uv = SCREEN_UV + (distorted - uv);
    vec4 color = texture(screen_texture, clamp(screen_uv, vec2(0.0), vec2(1.0)));

    // 事件视界：中心变黑
    float horizon = 1.0 - smoothstep(0.0, 0.08, dist);
    color.rgb *= (1.0 - horizon);

    // 边缘淡出
    float alpha = smoothstep(radius + 0.02, radius - 0.02, dist);

    COLOR = vec4(color.rgb, alpha);
}
