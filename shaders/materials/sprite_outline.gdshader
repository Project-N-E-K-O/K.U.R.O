shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform vec4 outline_color : source_color = vec4(0.02, 0.02, 0.02, 1.0);
uniform float outline_size : hint_range(0.0, 8.0, 0.1) = 2.0;
uniform float alpha_threshold : hint_range(0.0, 1.0, 0.01) = 0.01;
uniform bool preserve_fill = true;

void fragment()
{
	vec4 source_col = texture(TEXTURE, UV) * COLOR;
	vec2 offset = TEXTURE_PIXEL_SIZE * outline_size;

	float neighbor_alpha = 0.0;
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2( offset.x,  0.0)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2(-offset.x,  0.0)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2( 0.0,  offset.y)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2( 0.0, -offset.y)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2( offset.x,  offset.y)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2(-offset.x,  offset.y)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2( offset.x, -offset.y)).a);
	neighbor_alpha = max(neighbor_alpha, texture(TEXTURE, UV + vec2(-offset.x, -offset.y)).a);

	float base_mask = step(alpha_threshold, source_col.a);
	float outline_mask = step(alpha_threshold, neighbor_alpha) * (1.0 - base_mask);

	vec4 result = preserve_fill ? source_col : vec4(0.0);
	if (outline_mask > 0.0)
	{
		result.rgb = mix(result.rgb, outline_color.rgb, outline_mask);
		result.a = max(result.a, outline_color.a * outline_mask);
	}

	COLOR = result;
}
