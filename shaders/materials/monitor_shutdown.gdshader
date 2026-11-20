shader_type canvas_item;
render_mode unshaded;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 glow_color : source_color = vec4(0.59, 0.94, 0.96, 1.0);

void fragment() {
	vec2 p = abs(UV - vec2(0.5));
	vec2 feather = SCREEN_PIXEL_SIZE * 1.5;
	float squeeze = smoothstep(0.03, 0.66, progress);
	float collapse = smoothstep(0.66, 0.91, progress);
	vec2 half_size = vec2(mix(0.53, 0.0, collapse), mix(0.53, 0.002, squeeze));
	float aperture = (1.0 - smoothstep(half_size.x - feather.x, half_size.x + feather.x, p.x))
		* (1.0 - smoothstep(half_size.y - feather.y, half_size.y + feather.y, p.y));
	aperture *= 1.0 - smoothstep(0.63, 0.88, progress);

	float line = (1.0 - smoothstep(0.0, 0.005, p.y))
		* (1.0 - smoothstep(half_size.x, half_size.x + feather.x, p.x))
		* smoothstep(0.45, 0.65, progress) * (1.0 - smoothstep(0.75, 0.96, progress));
	line *= glow_color.a;
	float alpha = max(1.0 - aperture, line);
	float glow_weight = line / max(alpha, 0.001);
	vec3 screen_color = mix(COLOR.rgb, glow_color.rgb, glow_weight);
	COLOR = vec4(screen_color, alpha * COLOR.a);
}
