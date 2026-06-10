shader_type canvas_item;
render_mode blend_mix, unshaded;

uniform float scanline_pos : hint_range(-1.0, 1.0, 0.01) = -1.0;
uniform float scanline_width : hint_range(0.01, 0.3, 0.01) = 0.04;
uniform vec4 scanline_color : source_color = vec4(0.3, 0.8, 1.0, 1.0);
uniform bool reverse_scan = false;

void fragment() {
	vec4 tex = texture(TEXTURE, UV) * COLOR;

	if (scanline_pos >= 0.0) {
		float reveal;
		if (reverse_scan) {
			reveal = step(scanline_pos, UV.y);
		} else {
			reveal = step(UV.y, scanline_pos);
		}

		float dist = abs(UV.y - scanline_pos);
		float line = 1.0 - smoothstep(0.0, scanline_width, dist);
		vec4 glow = scanline_color * line;

		COLOR = tex * reveal + glow;
	} else {
		COLOR = tex;
	}
}
