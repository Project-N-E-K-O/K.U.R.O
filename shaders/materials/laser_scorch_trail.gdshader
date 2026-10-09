shader_type canvas_item;

// 激光**线型焦痕**（纯视觉，挂在 LaserScorchTrail 的 Line2D 上）：
//   1) 横截面填充：以 UV.y = 0.5 为中心画"中间实、两边淡"的剖面（不再是一条纯色带）；
//   2) 沿线破口：用**世界坐标**采样噪点（local_pos，线是 top_level 的所以就等于世界坐标）——
//      噪点不随线长拉伸、且固定在世界上（不会因为线加长而"流动"）；只在**边缘**啃，核心保持实心；
//   3) 老化/淡出仍由脚本写进 Line2D 的 gradient（乘在 COLOR.a 上，这里照旧乘出去）。
// noise_texture 之后添加：留空 = 白 → 破口失效（平滑带）；噪点图走 LaserScorchTrail.NoiseTexture（export）。

uniform vec4 scorch_color : source_color = vec4(0.02, 0.02, 0.02, 1.0);
// 横截面软边：0.01 ≈ 硬边带子，1.0 ≈ 整条雾化
uniform float edge_softness : hint_range(0.01, 1.0) = 0.5;
// 噪点图（留空 = 白 → 平滑）
uniform sampler2D noise_texture : hint_default_white, repeat_enable, filter_linear;
// 噪点密度（1/px：0.05 ≈ 20px 一块；越大越碎）
uniform float noise_scale : hint_range(0.001, 1.0) = 0.05;
// 破口强度（0 = 平滑；越大边缘被啃得越厉害）
uniform float noise_break : hint_range(0.0, 1.0) = 0.8;

varying vec2 local_pos;

void vertex() {
	local_pos = VERTEX;   // 线是 top_level 的 → 局部坐标 = 世界坐标
}

void fragment() {
	// 横截面填充（中间实、两边淡）
	float d = abs(UV.y - 0.5) * 2.0;
	float profile = 1.0 - smoothstep(1.0 - edge_softness, 1.0, d);

	// 沿线破口：边缘啃、核心留实心
	float n = texture(noise_texture, local_pos * noise_scale).r;
	float ragged = mix(1.0, smoothstep(0.25, 0.75, n), noise_break);
	float edge_mask = smoothstep(0.35, 0.9, d);
	float alpha = profile * mix(1.0, ragged, edge_mask);

	COLOR = vec4(scorch_color.rgb, alpha * COLOR.a);
}
