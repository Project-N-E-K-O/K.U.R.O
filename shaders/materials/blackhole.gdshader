shader_type canvas_item;

// 采样屏幕背景以实现扭曲效果
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float strength = 0.16;         // 吸引强度（减弱）
uniform float radius = 0.45;           // 影响范围（UV 空间 0~1）
uniform float swirl = 0.0;             // 旋转强度（保留参数，但默认关闭）
uniform float time_scale = 1.0;

// 波动效果参数
uniform float wave_amplitude = 0.06;       // 波动振幅
uniform float wave_frequency = 4.5;        // 波动频率
uniform float wave_speed = 2.6;            // 波动速度
uniform float chromatic_intensity = 0.01;  // 色散强度
uniform float distortion_intensity = 0.85; // 扭曲强度倍增（减弱）

// 事件视界与外环
uniform float horizon_radius = 0.155;
uniform float horizon_thickness = 0.022;
uniform float horizon_brightness = 1.0;
uniform float ring_intensity = 0.32;
uniform float ring_density = 34.0;
uniform float ring_speed = 1.8;
uniform float edge_softness = 0.07;
uniform float horizon_softness = 0.012;
uniform float edge_fog_strength = 0.22;
uniform float distortion_falloff = 0.1;

float ring_band(float d, float r, float w, float soft) {
    float outer = smoothstep(r + w + soft, r + w - soft, d);
    float inner = smoothstep(r - w - soft, r - w + soft, d);
    return max(0.0, outer - inner);
}

void fragment() {
    vec2 uv = UV;
    vec2 center = vec2(0.5);
    vec2 dir = uv - center;
    float dist = length(dir);
    vec2 norm_dir = dist > 0.0001 ? normalize(dir) : vec2(0.0, 1.0);
    float pull = smoothstep(radius, 0.0, dist);
    float edge_blend = smoothstep(radius + edge_softness, radius - distortion_falloff, dist);
    
    // 轻微波动：维持“活着”的感觉，但不破坏稳定环形。
    float wave = sin((dist * wave_frequency - TIME * wave_speed) * 6.28318) * wave_amplitude;
    float ripple_pull = pull * (1.0 + wave * 0.22);

    // 环境扭曲：仅保留吸引扭曲，不再旋转。
    vec2 distorted = uv - norm_dir * ripple_pull * strength * distortion_intensity * edge_blend;

    // 色散只留在外层环附近，减少“花边”。
    float outer_zone = smoothstep(horizon_radius + 0.03, radius, dist) * (1.0 - smoothstep(radius - 0.01, radius + 0.01, dist));
    float chroma_strength = chromatic_intensity * outer_zone;
    
    vec2 offset = (distorted - uv);
    vec2 screen_uv_r = SCREEN_UV + offset + norm_dir * chroma_strength * 0.5;
    vec2 screen_uv_g = SCREEN_UV + offset;
    vec2 screen_uv_b = SCREEN_UV + offset - norm_dir * chroma_strength * 0.5;
    
    vec4 color_r = texture(screen_texture, clamp(screen_uv_r, vec2(0.0), vec2(1.0)));
    vec4 color_g = texture(screen_texture, clamp(screen_uv_g, vec2(0.0), vec2(1.0)));
    vec4 color_b = texture(screen_texture, clamp(screen_uv_b, vec2(0.0), vec2(1.0)));
    
    vec4 color = vec4(
        mix(color_r.r, color_g.r, 0.7),
        color_g.g,
        mix(color_b.b, color_g.b, 0.7),
        color_g.a
    );
    vec4 base_color = texture(screen_texture, clamp(SCREEN_UV, vec2(0.0), vec2(1.0)));

    // 外层半透明同心环：从外向内收缩。
    float rings_mask = smoothstep(horizon_radius + 0.04, radius, dist) * (1.0 - smoothstep(radius - 0.015, radius + edge_softness, dist));
    float inward_phase = (radius - dist) * ring_density - TIME * ring_speed * 6.28318;
    float rings = 0.5 + 0.5 * sin(inward_phase);
    float ring_dark = mix(0.78, 1.0, rings);
    color.rgb *= mix(1.0, ring_dark, rings_mask * ring_intensity);

    // 事件视界：保留白边，但用可控软边避免“刀切感”。
    float horizon_ring = ring_band(dist, horizon_radius, horizon_thickness, horizon_softness);
    color.rgb = mix(color.rgb, vec3(1.0), horizon_ring * horizon_brightness);

    float core = 1.0 - smoothstep(horizon_radius - horizon_thickness * 1.18, horizon_radius - horizon_thickness * 0.2, dist);
    color.rgb = mix(color.rgb, vec3(0.0), core);

    // 外缘雾化过渡：让黑洞和环境更自然融合。
    float edge_fog = smoothstep(radius + edge_softness, radius - 0.01, dist);
    color.rgb = mix(base_color.rgb, color.rgb, edge_fog_strength + (1.0 - edge_fog_strength) * edge_fog);

    // 透明度：增加过渡带，降低边界硬切。
    float outer_alpha = smoothstep(radius + edge_softness, radius - 0.012, dist);
    outer_alpha = pow(clamp(outer_alpha, 0.0, 1.0), 1.45);
    float alpha = clamp(outer_alpha * 0.78 + rings_mask * 0.12 + horizon_ring * 0.38, 0.0, 1.0);

    COLOR = vec4(color.rgb, alpha);
}
