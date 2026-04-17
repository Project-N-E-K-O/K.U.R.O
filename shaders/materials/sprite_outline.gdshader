shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform vec4 outline_color : source_color = vec4(0.02, 0.02, 0.02, 1.0);// 描边颜色
uniform float outline_size : hint_range(0.0, 32.0, 0.1) = 16.0;// 描边大小
uniform float alpha_threshold : hint_range(0.0, 1.0, 0.01) = 0.5;// 透明度阈值，低于该值的像素将被视为透明
uniform float edge_softness : hint_range(0.001, 0.25, 0.001) = 0.12;// 边缘软化，降低锯齿
uniform bool preserve_fill = true;

float alpha_mask(float alpha)
{
	return smoothstep(alpha_threshold - edge_softness, alpha_threshold + edge_softness, alpha);
}

float sample_neighbor_alpha(sampler2D tex, vec2 uv, vec2 offset)
{
	float a = 0.0;
	a = max(a, texture(tex, uv + vec2( offset.x,  0.0)).a);
	a = max(a, texture(tex, uv + vec2(-offset.x,  0.0)).a);
	a = max(a, texture(tex, uv + vec2( 0.0,  offset.y)).a);
	a = max(a, texture(tex, uv + vec2( 0.0, -offset.y)).a);
	a = max(a, texture(tex, uv + vec2( offset.x,  offset.y)).a * 0.92);
	a = max(a, texture(tex, uv + vec2(-offset.x,  offset.y)).a * 0.92);
	a = max(a, texture(tex, uv + vec2( offset.x, -offset.y)).a * 0.92);
	a = max(a, texture(tex, uv + vec2(-offset.x, -offset.y)).a * 0.92);
	a = max(a, texture(tex, uv + vec2( offset.x * 0.5,  0.0)).a * 0.9);
	a = max(a, texture(tex, uv + vec2(-offset.x * 0.5,  0.0)).a * 0.9);
	a = max(a, texture(tex, uv + vec2( 0.0,  offset.y * 0.5)).a * 0.9);
	a = max(a, texture(tex, uv + vec2( 0.0, -offset.y * 0.5)).a * 0.9);
	return a;
}

void fragment()
{
	vec4 base = texture(TEXTURE, UV) * COLOR;
	vec2 offset = TEXTURE_PIXEL_SIZE * outline_size;
	float neighbor_alpha = sample_neighbor_alpha(TEXTURE, UV, offset);

	float base_mask = alpha_mask(base.a);
	float outline_mask = alpha_mask(neighbor_alpha) * (1.0 - base_mask);
	outline_mask = clamp(outline_mask, 0.0, 1.0);

	vec4 result = preserve_fill ? base : vec4(base.rgb, base.a);
	result.rgb = mix(result.rgb, outline_color.rgb, outline_mask);
	result.a = max(result.a, outline_color.a * outline_mask);

	COLOR = result;
}
