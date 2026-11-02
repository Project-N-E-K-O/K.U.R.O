shader_type canvas_item;
render_mode blend_mix, unshaded;

// ── 伪 3D 投影（pseudo_3d_card 同款模型）────────────────────────
uniform bool cull_backface = true;
uniform float fov : hint_range(1.0, 179.0) = 60.0;
uniform float rot_y_deg : hint_range(-360.0, 360.0) = 0.0;
uniform float rot_x_deg : hint_range(-360.0, 360.0) = 25.0;
uniform float inset : hint_range(0.0, 1.0) = 0.0;
// MeshInstance2D 的 QuadMesh 本地尺寸（当前 2×2）
uniform float quad_size = 2.0;

// ── 环形波（平面空间，缺口/半径都与 C# 的世界圆一致）──────────
uniform float wave_radius : hint_range(0.0, 1.2) = 0.0;
uniform float fade_alpha : hint_range(0.0, 1.0) = 1.0;
uniform float ring_thickness : hint_range(0.001, 0.3) = 0.03;
uniform float edge_softness : hint_range(0.0, 0.15) = 0.012;
uniform float wave_irregularity : hint_range(0.0, 0.08) = 0.01;
uniform float wave_frequency : hint_range(1.0, 40.0) = 7.0;
uniform float wave_wobble_speed : hint_range(0.0, 20.0) = 7.0;

uniform float gap_feather : hint_range(0.005, 0.2) = 0.1;
uniform int gap_count : hint_range(0, 6) = 0;
uniform float gap0_center = 0.0;
uniform float gap0_half = 0.0;
uniform float gap1_center = 0.0;
uniform float gap1_half = 0.0;
uniform float gap2_center = 0.0;
uniform float gap2_half = 0.0;
uniform float gap3_center = 0.0;
uniform float gap3_half = 0.0;
uniform float gap4_center = 0.0;
uniform float gap4_half = 0.0;
uniform float gap5_center = 0.0;
uniform float gap5_half = 0.0;

uniform vec4 edge_color : source_color = vec4(0.02, 0.32, 0.08, 1.0);
uniform vec4 body_color : source_color = vec4(0.22, 1.0, 0.35, 0.95);
uniform vec4 highlight_color : source_color = vec4(0.78, 1.0, 0.84, 1.0);
uniform vec4 glow_color : source_color = vec4(0.04, 0.85, 0.25, 0.42);

varying vec2 offset;
varying vec3 world_pos_3d;

float wrap_angle(float a) {
	return atan(sin(a), cos(a));
}

float gap_factor(float angle, float center, float half_width) {
	float delta = wrap_angle(angle - center);
	// 只做角度方向的平滑羽化；不做径向随机抖动——视觉羽化区域必须能被 C# 的
	// "缺口半宽 + 同样的羽化角" 覆盖，否则视觉断口和实际伤害边界会错位。
	return smoothstep(0.0, gap_feather, abs(delta) - half_width);
}

void vertex() {
	float rot_y_rad = radians(rot_y_deg);
	float rot_x_rad = radians(rot_x_deg);

	float sin_y = sin(rot_y_rad), cos_y = cos(rot_y_rad);
	float sin_x = sin(rot_x_rad), cos_x = cos(rot_x_rad);

	mat3 rotation_matrix;
	rotation_matrix[0] = vec3(cos_y, 0.0, -sin_y);
	rotation_matrix[1] = vec3(sin_y * sin_x, cos_x, cos_y * sin_x);
	rotation_matrix[2] = vec3(sin_y * cos_x, -sin_x, cos_y * cos_x);

	float perspective_scale = tan(radians(fov) * 0.5);
	world_pos_3d = rotation_matrix * vec3(UV - 0.5, 0.5 / perspective_scale);

	float depth_scale = (0.5 / perspective_scale) + 0.5;
	world_pos_3d.xy *= depth_scale * rotation_matrix[2].z;
	offset = depth_scale * rotation_matrix[2].xy;

	// 环是程序化绘制，不采样贴图；用 QuadMesh 的本地尺寸替代 card 的 TEXTURE_PIXEL_SIZE 展开
	VERTEX += (UV - 0.5) * quad_size * perspective_scale * (1.0 - inset);
}

void fragment() {
	if (cull_backface && world_pos_3d.z <= 0.0) discard;

	// 屏幕像素 → 环所在平面的 UV（pseudo_3d_card 的逆投影）
	vec2 projected_uv = (world_pos_3d.xy / world_pos_3d.z) - offset + 0.5;
	if (projected_uv.x < 0.0 || projected_uv.x > 1.0
		|| projected_uv.y < 0.0 || projected_uv.y > 1.0)
		discard;

	// QuadMesh 的 UV V 轴与世界 Y 轴相反（顶部 V=1、底部 V=0；Sprite2D 则相反）。
	// 把 V 翻回来，缺口角度/半径才与 C# 用世界坐标算出的缺口一致，否则上下镜像。
	vec2 plane_uv = vec2(projected_uv.x, 1.0 - projected_uv.y);
	vec2 p = plane_uv - vec2(0.5);
	float dist = length(p) * 2.0;
	float angle = atan(p.y, p.x);

	// 动态不规则波前（三层正弦 + 时间；整数分段避免 ±π 接缝）
	float segments = max(1.0, round(wave_frequency));
	float wobble = sin(angle * segments + TIME * wave_wobble_speed) * 0.55
		+ sin(angle * segments * 2.0 - TIME * wave_wobble_speed * 1.31) * 0.30
		+ sin(angle * segments * 3.0 + TIME * wave_wobble_speed * 0.63) * 0.15;
	wobble *= wave_irregularity;
	float r = wave_radius + wobble;
	float half_t = max(ring_thickness * 0.5, 0.0001);

	// 波前 = 环的“外沿”，不是带子的中心：环整体向内绘制，外沿正好落在 wave_radius。
	// 否则视觉的亮带会领先逻辑圆 half_t（再叠 edge_softness），玩家看到环已经压到身上，
	// 伤害却要等带子中心到达才结算。
	float ring_center = r - half_t;
	float d = abs(dist - ring_center);
	float core = 1.0 - smoothstep(half_t, half_t + edge_softness, d);
	float glow = 1.0 - smoothstep(0.0, half_t * 3.5 + edge_softness, d);

	float mask = fade_alpha;
	if (gap_count > 0) mask *= gap_factor(angle, gap0_center, gap0_half);
	if (gap_count > 1) mask *= gap_factor(angle, gap1_center, gap1_half);
	if (gap_count > 2) mask *= gap_factor(angle, gap2_center, gap2_half);
	if (gap_count > 3) mask *= gap_factor(angle, gap3_center, gap3_half);
	if (gap_count > 4) mask *= gap_factor(angle, gap4_center, gap4_half);
	if (gap_count > 5) mask *= gap_factor(angle, gap5_center, gap5_half);

	float edge_t = smoothstep(half_t * 0.25, half_t, d);
	vec3 col = mix(body_color.rgb, edge_color.rgb, edge_t);
	float highlight = 1.0 - smoothstep(0.0, half_t * 0.55, d);
	col = mix(col, highlight_color.rgb, highlight * 0.75);

	float ring_a = core * body_color.a;
	float glow_a = glow * glow_color.a;
	float total_a = clamp(ring_a + glow_a, 0.0, 1.0);

	// 普通 alpha 混合：颜色先反预乘回非预乘值，再由 alpha 与背景混合；
	// 不再把环的颜色直接加到背景上（避免 HDR/tonemap 偏色）。
	vec3 rgb = total_a > 0.0001
		? (col * ring_a + glow_color.rgb * glow_a) / total_a
		: col;
	float alpha = total_a * mask;
	if (alpha <= 0.001) discard;

	COLOR = vec4(rgb, alpha);
}
