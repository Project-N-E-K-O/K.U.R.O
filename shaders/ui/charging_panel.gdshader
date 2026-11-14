shader_type canvas_item;

uniform float sweep_period : hint_range(0.5, 6.0, 0.1) = 2.6;
uniform float sweep_strength : hint_range(0.0, 0.5, 0.01) = 0.18;
uniform float pulse_strength : hint_range(0.0, 0.3, 0.01) = 0.08;

void fragment() {
	float phase = TIME / max(sweep_period, 0.001);
	// 扫光从贴图外进入并离开，循环时不会在边缘跳闪。
	float sweep_x = mix(-0.16, 1.16, fract(phase));
	float sweep = 1.0 - smoothstep(0.0, 0.16, abs(UV.x - sweep_x));
	float pulse = 0.5 - 0.5 * cos(phase * TAU);
	COLOR.rgb *= 1.0 + sweep * sweep_strength + pulse * pulse_strength;
}
