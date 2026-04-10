shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform vec4 outline_color : source_color = vec4(0.02, 0.02, 0.02, 1.0);
uniform float outline_size : hint_range(0.0, 8.0, 0.1) = 2.0;
uniform float alpha_threshold : hint_range(0.0, 1.0, 0.01) = 0.01;
uniform bool preserve_fill = true;

float sample_neighbor_alpha(vec2 uv, vec2 offset)
{
	float a = 0.0;
	a = max(a, texture(TEXTURE, uv + vec2( offset.x,  0.0)).a);
	a = max(a, texture(TEXTURE, uv + vec2(-offset.x,  0.0)).a);
	a = max(a, texture(TEXTURE, uv + vec2( 0.0,  offset.y)).a);
	a = max(a, texture(TEXTURE, uv + vec2( 0.0, -offset.y)).a);
	a = max(a, texture(TEXTURE, uv + vec2( offset.x,  offset.y)).a);
	a = max(a, texture(TEXTURE, uv + vec2(-offset.x,  offset.y)).a);
	a = max(a, texture(TEXTURE, uv + vec2( offset.x, -offset.y)).a);
	a = max(a, texture(TEXTURE, uv + vec2(-offset.x, -offset.y)).a);
	return a;
}

void fragment()
{
	vec4 base = texture(TEXTURE, UV) * COLOR;
	vec2 offset = TEXTURE_PIXEL_SIZE * outline_size;
	float neighbor_alpha = sample_neighbor_alpha(UV, offset);

	float base_mask = step(alpha_threshold, base.a);
	float outline_mask = step(alpha_threshold, neighbor_alpha) * (1.0 - base_mask);

	vec4 result = preserve_fill ? base : vec4(base.rgb, base.a);
	if (outline_mask > 0.0)
	{
		result.rgb = mix(result.rgb, outline_color.rgb, outline_mask);
		result.a = max(result.a, outline_color.a * outline_mask);
	}

	COLOR = result;
}
