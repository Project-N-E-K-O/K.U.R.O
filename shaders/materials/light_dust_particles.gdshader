shader_type canvas_item;
render_mode blend_add, unshaded;

// ── 光束闪烁 ──
uniform float flicker_speed : hint_range(0.5, 20.0) = 8.0;
uniform float flicker_intensity : hint_range(0.0, 1.0) = 0.3;

// ── 光束微光摇曳 (水平波纹) ──
uniform float shimmer_speed : hint_range(0.1, 5.0) = 1.5;
uniform float shimmer_strength : hint_range(0.0, 0.5) = 0.15;

// ── 浮尘粒子 ──
uniform float density : hint_range(0.1, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 0.5;
uniform float particle_size : hint_range(0.005, 0.08) = 0.025;
uniform float anim_speed : hint_range(0.1, 3.0) = 1.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x),
		f.y
	);
}

void fragment() {
	vec4 base = texture(TEXTURE, UV);

	// 光束闪烁：多层噪声叠加产生不规则闪烁
	float flicker = 1.0
		+ noise(vec2(TIME * flicker_speed, 0.0)) * flicker_intensity
		+ noise(vec2(TIME * flicker_speed * 1.7, 1.0)) * flicker_intensity * 0.6
		- flicker_intensity * 0.8;
	flicker = clamp(flicker, 0.0, 1.5);

	// 水平微光波纹
	float shimmer = 1.0 + sin(UV.x * 6.28318 + TIME * shimmer_speed) * shimmer_strength
		+ cos(UV.x * 12.56637 - TIME * shimmer_speed * 0.7) * shimmer_strength * 0.5;

	vec4 beam_color = base * flicker * shimmer;
	beam_color.a = min(beam_color.a, 1.0);

	// ── 浮尘粒子（叠加在光束之上） ──
	float cell_count = 8.0 + density * 14.0;
	vec2 cell_id = floor(UV * cell_count);
	vec2 local_uv = fract(UV * cell_count);

	float h1 = hash(cell_id);
	float h2 = hash(cell_id + 17.0);
	float h3 = hash(cell_id + 31.0);
	float h4 = hash(cell_id + 53.0);

	vec2 particle_center = vec2(h1, h2) * 0.6 + 0.2;

	float lifetime = 1.0 + h3 * 4.0;
	float cooldown = h4;
	float total_cycle = lifetime + cooldown;
	float phase = hash(cell_id + 7.0);
	float cycle_time = mod(TIME * anim_speed + phase * total_cycle, total_cycle);

	float life = 0.0;
	if (cycle_time < lifetime) {
		float progress = cycle_time / lifetime;
		life = sin(progress * 3.14159265);
	}

	float angle = 0.174533;
	vec2 rel = local_uv - particle_center;
	float ca = cos(angle), sa = sin(angle);
	rel = vec2(rel.x * ca - rel.y * sa, rel.x * sa + rel.y * ca);
	rel.y /= 0.8 + h2 * 0.5;

	float particle = 1.0 - smoothstep(0.0, particle_size, length(rel));
	float alpha = particle * life * brightness;

	COLOR = beam_color + vec4(vec3(1.0, 0.95, 0.75) * alpha, alpha);
}
