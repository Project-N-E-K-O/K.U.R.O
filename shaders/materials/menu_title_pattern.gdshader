shader_type canvas_item;
render_mode unshaded;

uniform sampler2D pattern_texture : source_color, repeat_enable, filter_linear_mipmap;
uniform float pattern_angle_degrees : hint_range(-180.0, 180.0, 1.0) = -25.0;
uniform float scroll_angle_degrees : hint_range(-180.0, 180.0, 1.0) = -45.0;
uniform float scroll_speed : hint_range(0.0, 300.0, 1.0) = 42.0;
uniform float tile_width : hint_range(128.0, 3840.0, 1.0) = 960.0;
uniform float opacity : hint_range(0.0, 1.0, 0.01) = 0.24;
// 以平铺块为单位，在旋转后的纹理坐标中错开图案。
uniform vec2 pattern_offset = vec2(0.0);

vec2 rotate_point(vec2 point, float angle) {
	float c = cos(angle);
	float s = sin(angle);
	return vec2(c * point.x - s * point.y, s * point.x + c * point.y);
}

void fragment() {
	// 先换算成屏幕像素，旋转时不会因窗口长宽比而压扁字形。
	vec2 screen_position = (SCREEN_UV - vec2(0.5)) / SCREEN_PIXEL_SIZE;
	float scroll_angle = radians(scroll_angle_degrees);
	vec2 scroll = vec2(cos(scroll_angle), sin(scroll_angle)) * scroll_speed * TIME;
	vec2 texture_size = vec2(textureSize(pattern_texture, 0));
	vec2 tile_size = tile_width * texture_size / max(texture_size.x, 1.0);
	vec2 pattern_uv = rotate_point(screen_position - scroll, -radians(pattern_angle_degrees)) / tile_size;
	// 采样器负责重复；保留连续 UV 使纹理过滤能正确处理接缝。
	vec4 ink = texture(pattern_texture, pattern_uv + vec2(0.5) - pattern_offset);
	COLOR = vec4(ink.rgb * COLOR.rgb, ink.a * opacity * COLOR.a);
}
