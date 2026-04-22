shader_type canvas_item;

// 高光条宽度
uniform float highlight_width : hint_range(0.01, 0.3) = 0.08;
// 高光移动速度
uniform float highlight_speed : hint_range(0.5, 5.0) = 2.0;
// 高光强度
uniform float highlight_intensity : hint_range(0.0, 2.0) = 1.2;
// 光线生成间隔（秒）
uniform float highlight_interval : hint_range(0.5, 10.0) = 2.0;

void fragment() {
	// 采样原始纹理
	vec4 base_color = texture(TEXTURE, UV);
	
	if (base_color.a < 0.1) {
		discard;
	}
	
	// 光线扫过一次的时间
	float sweep_duration = 1.0 / highlight_speed;
	// 完整周期 = 扫过时间 + 间隔时间
	float total_cycle = sweep_duration + highlight_interval;
	// 当前时间在周期内的位置
	float time_in_cycle = mod(TIME, total_cycle);
	
	// 控制可见性：只在扫过时间内显示光线
	float visibility = step(time_in_cycle, sweep_duration);
	
	// 创建对角线坐标（从左下到右上）
	float diag_coord = (UV.x - UV.y + 1.0) * 0.5;
	// 高光沿对角线从左下往右上移动
	float highlight_pos = fract(TIME * highlight_speed);
	
	// 创建高光条：在对角线指定位置产生亮光，两边逐渐淡出
	float distance_to_light = abs(diag_coord - highlight_pos);
	float highlight = exp(-distance_to_light * distance_to_light / (highlight_width * highlight_width)) * highlight_intensity * visibility;
	
	// 组合：基础颜色 + 高光条
	vec3 final_color = base_color.rgb + vec3(highlight);
	
	COLOR = vec4(final_color, base_color.a);
}
