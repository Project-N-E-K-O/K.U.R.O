shader_type canvas_item;
render_mode unshaded;

uniform vec4 bar_color : source_color = vec4(0.20, 0.31, 0.57, 1.0);
uniform vec4 edge_color : source_color = vec4(0.06, 0.09, 0.19, 1.0);
uniform vec4 highlight_color : source_color = vec4(0.55, 0.72, 0.96, 1.0);
uniform float edge_width : hint_range(0.0, 8.0, 0.1) = 2.0;
uniform float stroke_wobble : hint_range(0.0, 4.0, 0.1) = 1.35;

float rounded_box(vec2 point, vec2 half_size, float radius) {
	vec2 delta = abs(point) - half_size + vec2(radius);
	return length(max(delta, vec2(0.0))) + min(max(delta.x, delta.y), 0.0) - radius;
}

void fragment() {
	// 使用屏幕像素，底栏拉宽后描边和笔触仍保持原来的粗细。
	vec2 size = 1.0 / max(fwidth(UV), vec2(0.00001));
	float scale = size.y / 68.0;
	vec2 pixel = UV * size;
	vec2 point = pixel - size * 0.5;
	float x = pixel.x / max(scale, 0.001);
	point.y += (sin(x * 0.026) + 0.4 * sin(x * 0.071)) * stroke_wobble * scale;
	float distance_to_edge = rounded_box(point, size * 0.5 - vec2(4.0 * scale), 23.0 * scale);
	float feather = max(fwidth(distance_to_edge) * 0.5, 0.5);
	float coverage = 1.0 - smoothstep(-feather, feather, distance_to_edge);
	float fill = 1.0 - smoothstep(-feather, feather, distance_to_edge + edge_width * scale);

	float top = 1.0 - smoothstep(0.10, 0.39, UV.y);
	float bottom = smoothstep(0.57, 0.96, UV.y);
	float brush = smoothstep(0.22, 0.44, sin(x * 0.019 + 0.5 * sin(x * 0.043)));
	vec3 ink = mix(bar_color.rgb, highlight_color.rgb, top * (0.22 + 0.26 * brush));
	ink = mix(ink, edge_color.rgb, bottom * 0.55);
	COLOR = vec4(mix(edge_color.rgb, ink, fill), coverage * mix(edge_color.a, bar_color.a, fill)) * COLOR;
}
