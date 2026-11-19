shader_type canvas_item;
render_mode unshaded;

uniform sampler2D desktop_snapshot : source_color, filter_linear, repeat_disable;
uniform vec2 snapshot_area = vec2(1.0);
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 glow_color : source_color = vec4(0.59, 0.94, 0.96, 1.0);

void fragment() {
	vec2 center = UV - vec2(0.5);
	float squeeze = smoothstep(0.03, 0.66, progress);
	float collapse = smoothstep(0.66, 0.93, progress);
	vec2 half_size = vec2(mix(0.5, 0.0, collapse), mix(0.5, 0.002, squeeze));
	vec2 feather = min(fwidth(UV) * 0.6, vec2(0.025));
	vec2 inside = vec2(1.0) - smoothstep(half_size - feather, half_size + feather, abs(center));

	// 截图中心对应交接时实际取景的内屏范围。拉远后，其余部分显示黑色。
	vec2 image_uv = center / max(half_size * 2.0 * snapshot_area, vec2(0.001)) + vec2(0.5);
	vec4 snapshot = texture(desktop_snapshot, clamp(image_uv, vec2(0.0), vec2(1.0)));
	vec2 image_inside = step(vec2(0.0), image_uv) * step(image_uv, vec2(1.0));
	vec3 display_color = snapshot.rgb * snapshot.a * image_inside.x * image_inside.y;
	float glow = smoothstep(0.42, 0.65, progress)
		* (1.0 - smoothstep(0.72, 0.97, progress)) * glow_color.a;
	float coverage = inside.x * inside.y * (1.0 - smoothstep(0.93, 1.0, progress));
	COLOR = vec4(mix(display_color, glow_color.rgb, glow), coverage);
}
