shader_type canvas_item;

// ============================================================
// 粘稠能量液团：一坨会"鼓起 → 摊开 → 回缩"的不规则液体。
//
// 与 energy_orb 的分工：orb 是**规整的能量球**（径向椭圆），本 shader 是**不规则的液体/液滴**——
// 用低频凸起 + 边缘细扰做出粘稠感，靠外层/内层/核心三层 + 一小块高光塑形。
//
// 两个用途通用：
//   · 落点液团（挂在 Sprite2D 上，progress 由 AnimationPlayer 驱动）；
//   · 溅射粒子（挂在 GPUParticles2D 的 material 上，每颗粒子都是一滴不规则液滴）。
// ============================================================


// ============================================================
// 颜色
// ============================================================

uniform vec4 outer_color : source_color =
	vec4(0.086, 0.541, 0.145, 1.0);

uniform vec4 inner_color : source_color =
	vec4(0.4, 0.902, 0.227, 1.0);

uniform vec4 core_color : source_color =
	vec4(0.722, 1.0, 0.533, 1.0);

uniform vec4 highlight_color : source_color =
	vec4(0.949, 1.0, 0.941, 1.0);


// ============================================================
// 形状
// ============================================================

// 横向半径占画布半宽的比例（超上限自动收口，避免被画布边缘切出直边）
uniform float fill : hint_range(0.1, 1.0) = 0.86;

// 横/纵比：> 1 = 横向拉长（摊开的液团），< 1 = 竖向拉长（飞行的液滴）
uniform float aspect = 1.55;

// 时间线：0 = 还没成形，1 = 完全摊开（粒子用不到，保持 0 附近即可）
uniform float progress : hint_range(0.0, 1.0) = 0.0;

// 初始/最终大小（相对最终宽度）
uniform float grow_size : hint_range(0.05, 1.0) = 0.5;
uniform float settle_size : hint_range(0.05, 1.0) = 0.78;

// 低频凸起（粘稠感的主要来源：几团液体并在一起，而不是一个圆）
uniform float lump_strength : hint_range(0.0, 0.6) = 0.18;
uniform float lump_speed = 1.6;

// 边缘细扰（表面张力的小起伏）
uniform float edge_ripple : hint_range(0.0, 0.2) = 0.05;

// 边缘柔化 / 高光强度
uniform float edge_softness : hint_range(0.0, 1.0) = 0.1;
uniform float highlight_strength : hint_range(0.0, 1.0) = 0.35;


void fragment()
{
	// --------------------------------------------------------
	// UV → 中心坐标（半宽 = 0.5）
	// --------------------------------------------------------

	vec2 p = UV - vec2(0.5);
	p.x /= max(aspect, 0.05);

	float r = length(p);
	float angle = atan(p.y, p.x);


	// --------------------------------------------------------
	// 时间线：鼓起(0→0.18) → 摊平回缩(0.25→0.9)
	// --------------------------------------------------------

	float grow = smoothstep(0.0, 0.18, progress);
	float base_scale = mix(grow_size, 1.0, grow) * mix(1.0, settle_size, smoothstep(0.25, 0.9, progress));


	// --------------------------------------------------------
	// 低频凸起：整数倍频率（atan 在 ±π 有接缝，非整数倍会在一侧留下切口）
	// --------------------------------------------------------

	float lump =
		sin(angle * 3.0 + TIME * lump_speed) * 0.5 +
		sin(angle * 5.0 - TIME * lump_speed * 1.31) * 0.3 +
		sin(angle * 7.0 + TIME * lump_speed * 0.63) * 0.2;

	float ripple = sin(angle * 11.0 - TIME * 3.0);


	// --------------------------------------------------------
	// 半径（相对画布半宽）
	// --------------------------------------------------------

	float aspect_now = max(aspect, 0.05);
	float fit_fill = min(fill, max(0.2, 1.0 - 2.0 * (lump_strength + edge_ripple) * aspect_now));
	float radius = fit_fill * 0.5 / aspect_now * base_scale;
	float outer_radius = radius * (1.0 + lump_strength * lump + edge_ripple * ripple);

	float inner_radius = outer_radius * 0.74;
	float core_radius = outer_radius * 0.36;
	float soft = max(0.004, outer_radius * edge_softness);


	// --------------------------------------------------------
	// 三层遮罩
	// --------------------------------------------------------

	float outer_mask = 1.0 - smoothstep(outer_radius - soft, outer_radius, r);

	float inner_mask = 1.0 - smoothstep(inner_radius - soft, inner_radius, r);
	inner_mask *= outer_mask;

	float core_mask = 1.0 - smoothstep(core_radius - soft, core_radius, r);
	core_mask *= inner_mask;


	// --------------------------------------------------------
	// 颜色 + 高光（偏左上的一小块，限制在核心区内）
	// --------------------------------------------------------

	vec3 color = outer_color.rgb;
	color = mix(color, inner_color.rgb, inner_mask);
	color = mix(color, core_color.rgb, core_mask);

	vec2 highlight_center = vec2(-0.35, -0.55) * outer_radius;
	float highlight = 1.0 - smoothstep(0.0, max(0.001, outer_radius * 0.55), distance(p, highlight_center));
	color = mix(color, highlight_color.rgb, highlight * core_mask * highlight_strength);


	// --------------------------------------------------------
	// Alpha：乘上传入 alpha（节点 Modulate.a / 粒子的 color_ramp），淡出才生效
	// --------------------------------------------------------

	COLOR = vec4(color, outer_mask * COLOR.a);
}
