shader_type canvas_item;
render_mode unshaded;

// 全屏数字干扰（ScreenInterferenceEffect 专用）：采样已完成画面，切成离散时间片的
// 矩形故障块做错位/复制/绿色覆盖/信号丢失，再叠扫描线、RGB 分离与颗粒噪声。
// 纯视觉：强度由 C# 按特效自身时长衰减，strength=0 时原样输出。

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;

uniform float interference_strength : hint_range(0.0, 1.0) = 0.0;
uniform float glitch_frequency : hint_range(0.0, 30.0) = 8.0;
uniform float block_density : hint_range(0.0, 1.0) = 0.35;
uniform float horizontal_shift : hint_range(0.0, 0.1) = 0.025;
uniform float vertical_shift : hint_range(0.0, 0.05) = 0.008;
uniform float rgb_shift : hint_range(0.0, 0.02) = 0.0015;
uniform float scanline_strength : hint_range(0.0, 1.0) = 0.10;
uniform float noise_strength : hint_range(0.0, 1.0) = 0.04;
uniform vec4 green_tint : source_color = vec4(0.05, 1.0, 0.30, 1.0);
uniform vec4 dark_tint : source_color = vec4(0.0, 0.04, 0.01, 1.0);

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

void fragment() {
	float strength = clamp(interference_strength, 0.0, 1.0);
	if (strength <= 0.001) {
		COLOR = texture(screen_texture, SCREEN_UV);
	} else {
	// 像素空间算块：尺寸按屏高比例定义，不同分辨率/宽高比下块形状稳定
	vec2 res = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 px = SCREEN_UV * res;
	float tcell = floor(TIME * glitch_frequency);

	// 三级网格。找到块后不再尝试更小的网格（大块优先，避免互相覆盖成噪点）
	vec2 sample_uv = SCREEN_UV;
	float green_mix = 0.0;
	float dark_mix = 0.0;
	float border = 0.0;
	bool found = false;

	vec2 block_size = vec2(1.0);
	vec2 block_local = vec2(0.0);
	vec2 block_cell = vec2(0.0);
	float block_mode = 0.0;
	float block_seed = 0.0;

	// 大块：明显的画面破碎
	if (!found) {
		vec2 size_px = res.y * vec2(0.35, 0.06);
		vec2 cell = floor(px / size_px);
		float h = hash12(cell + vec2(37.0, tcell));
		if (h > 1.0 - clamp(block_density * 0.35 * strength, 0.0, 1.0)) {
			found = true;
			block_size = size_px;
			block_local = fract(px / size_px);
			block_cell = cell;
			block_mode = floor(hash12(cell.yx + vec2(tcell * 0.37 + 5.0, 19.0)) * 4.0);
			block_seed = 1.0;
		}
	}

	// 中块：局部错位与绿色数据块
	if (!found) {
		vec2 size_px = res.y * vec2(0.14, 0.025);
		vec2 cell = floor(px / size_px);
		float h = hash12(cell + vec2(71.0, tcell));
		if (h > 1.0 - clamp(block_density * 0.55 * strength, 0.0, 1.0)) {
			found = true;
			block_size = size_px;
			block_local = fract(px / size_px);
			block_cell = cell;
			block_mode = floor(hash12(cell.yx + vec2(tcell * 0.61 + 23.0, 47.0)) * 4.0);
			block_seed = 2.0;
		}
	}

	// 小块：细碎数字噪声
	if (!found) {
		vec2 size_px = res.y * vec2(0.05, 0.01);
		vec2 cell = floor(px / size_px);
		float h = hash12(cell + vec2(113.0, tcell));
		if (h > 1.0 - clamp(block_density * 0.75 * strength, 0.0, 1.0)) {
			found = true;
			block_size = size_px;
			block_local = fract(px / size_px);
			block_cell = cell;
			block_mode = floor(hash12(cell.yx + vec2(tcell * 0.83 + 61.0, 89.0)) * 4.0);
			block_seed = 3.0;
		}
	}

	if (found) {
		vec2 rnd = hash22(block_cell + vec2(block_seed * 13.0, tcell));
		vec2 dir = (rnd - 0.5) * 2.0;

		// 块边缘 1~2px 亮绿硬边（数码感，不做整体羽化）
		float edge_px = min(
			min(block_local.x, 1.0 - block_local.x) * block_size.x,
			min(block_local.y, 1.0 - block_local.y) * block_size.y);
		border = (1.0 - smoothstep(1.5, 3.0, edge_px)) * strength;

		if (block_mode < 0.5) {
			// 水平为主、垂直为辅的画面错位
			sample_uv += vec2(dir.x * horizontal_shift, dir.y * vertical_shift);
		} else if (block_mode < 1.5) {
			// 数据块复制：额外横向偏移，呈现相邻区域被重复搬过来
			float dup = (hash12(block_cell + vec2(block_seed * 7.0, tcell)) - 0.5) * 0.08;
			sample_uv += vec2(dir.x * horizontal_shift * 1.6 + dup, dir.y * vertical_shift);
			green_mix = 0.25;
		} else if (block_mode < 2.5) {
			// 荧光绿数据块（仍保留一点底图，不整块死绿）
			sample_uv += vec2(dir.x * horizontal_shift * 0.4, 0.0);
			green_mix = 0.75;
		} else {
			// 信号丢失：深绿/近黑块
			sample_uv += vec2(dir.x * horizontal_shift * 0.25, 0.0);
			dark_mix = 0.85;
		}
	}

	vec4 scene_color = texture(screen_texture, sample_uv);
	vec3 col = scene_color.rgb;

	// RGB 分离：轻微，仅强干扰时明显
	vec2 rgb_off = vec2(rgb_shift * strength, 0.0);
	col.r = texture(screen_texture, sample_uv + rgb_off).r;
	col.b = texture(screen_texture, sample_uv - rgb_off).b;

	// 块内覆盖
	col = mix(col, green_tint.rgb, green_mix * strength * 0.8);
	col = mix(col, dark_tint.rgb, dark_mix * strength);
	col = mix(col, green_tint.rgb * 1.25, border * 0.8);

	// 全屏辅助故障：扫描线 + 颗粒 + 轻微绿色染色（全部随强度）
	float scan = 0.5 + 0.5 * sin(px.y * 1.5707963);   // 4px 周期
	col *= 1.0 - scanline_strength * strength * (0.35 + 0.65 * scan);
	float grain = hash12(px + vec2(floor(TIME * 30.0), floor(TIME * 17.0))) - 0.5;
	col += vec3(grain) * noise_strength * strength;
	col = mix(col, col * green_tint.rgb, 0.12 * strength);

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
	}
}
