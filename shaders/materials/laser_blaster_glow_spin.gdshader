shader_type canvas_item;

// 激光（伪体积：透视锥化 + 转动高光 + 近亮远暗；FanBeam 专用，共用那份 laser_blaster_glow 不动）。
//
// **透视锥化（fov）**：宽度沿长度**线性**展开成直边梯形（同 pseudo_3d_card 的投影轮廓——投影保直线，
// 加宽平摊在整条束上；双曲律 1/(1−k·x) 会把一半加宽挤进末端几个百分点，呈"末端喇叭"）——
//   W(x) = W0 · (1 + (r − 1)·x)，  r = 1 / (1 − k)
//   k = 0.95 · raw/(raw + 1)，  raw = 2·tan(fov/2)·sin(tilt)
// 其中 x ∈ [0,1] 是沿光束（0 = 发射点、1 = 末端），r = 末端倍率（枪口恒 1×、r < 20）：
//   · raw 对 fov 与 tilt_deg 都单调；**软饱和**（raw/(raw+1)）代替旧的硬钳位——
//     旧写法 clamp(raw, 0, 0.95) 在大 fov 下 tilt 一过零点几度就顶满，tilt 退化成开关（0/非0 两种效果）；
//     软饱和后两者都是渐进的"越大越猛"（收益递减）；
//   · fov / tilt_deg 决定**满强度**的形状；整条效果的过渡走 **volume**（0 = 平面 → 1 = 满强度，线性混合，
//     由脚本按伪体积权重下发——权重不乘进 tilt：大 fov 下乘进去会前段猛、后段钝，起点还会跳变）。
// 关键：**长度方向的 UV 取样完全不动**（不做投影、不做 x 压缩），所以流动速度、两端渐隐位置、
// 芯的粗细都不受挤压——只把**宽度轴**按梯形重映射（顶点按最大倍率 r 预留高度、片元按 W(x) 缩放回去）。
//
// 另外两样零几何形变的线索：转动高光（沿宽度循环扫过的亮带）、近亮远暗（+ 可选远端染色）。

uniform float glow_intensity : hint_range(0.0, 50.0) = 6.0;
uniform float core_width : hint_range(0.0, 1.0) = 0.5;
// 沿光束（UV.x）两端的渐隐，可分别设置：
//   core_length       = **尾端**渐隐起点（0.5 = 后半段渐隐；沿用原语义，原材质都靠它）
//   core_start_length = **首端**渐隐长度（0 = 首端不淡出 = 原效果；0.3 = 前 30% 渐显）
uniform float core_length : hint_range(0.0, 1.0) = 0.5;
uniform float core_start_length : hint_range(0.0, 1.0) = 0.0;
uniform float fade : hint_range(0.0, 1.0) = 1.0;

// ── 流动（与 laser_blaster_glow 同款，默认不生效）──
uniform sampler2D flow_noise : hint_default_white, repeat_enable, filter_linear;
uniform vec2 flow_speed = vec2(0.0, 0.0);
uniform float flow_scale : hint_range(0.1, 100.0) = 8.0;
uniform float flow_strength : hint_range(0.0, 1.0) = 0.0;

// ── 透视锥化 ──
// 视场角（度）：透视强度；1 ≈ 正交（平面），30~60 是常用档
uniform float fov : hint_range(1.0, 179.0) = 45.0;
// 光束朝镜头的倾斜角（度）：0 = 平面；fov 与 tilt_deg 共同决定"满强度"的锥化形状
uniform float tilt_deg : hint_range(0.0, 89.0) = 0.0;
// 伪体积整体强度（0 = 平面、1 = 满强度）：由脚本按权重下发，在两者之间**线性**过渡
uniform float volume : hint_range(0.0, 1.0) = 1.0;

// ── 转动高光 / 纵深 ──
// 亮带位置 = fract(TIME × spin_speed)，沿宽度 0→1 循环（1.0 = 每秒扫一遍）
uniform float spin_speed : hint_range(0.0, 10.0) = 0.6;
// 亮带宽度（占光束宽度比例）
uniform float spin_width : hint_range(0.01, 1.0) = 0.5;
// 亮带亮度加成（0 = 关）
uniform float spin_strength : hint_range(0.0, 3.0) = 0.7;
// 近亮远暗：远端亮度衰减比例（0 = 关，0.3 = 远端 70% 亮度）
uniform float depth_falloff : hint_range(0.0, 1.0) = 0.3;
// 远端染色（默认白 = 只暗不偏色）
uniform vec4 far_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

varying float tip_ratio;      // 远端相对枪口的宽度倍率（顶点算好，片元复用）

void vertex() {
	// 满强度形状：raw = 2·tan(fov/2)·sin(tilt)，软饱和到 k（上界 0.95 → r < 20）。
	// 用 raw/(raw+1) 而非硬钳位：避免大 fov 下 tilt 稍有值就顶到封顶（那样 tilt 等于开关）
	float ps = tan(radians(fov) * 0.5);
	float raw = 2.0 * ps * sin(radians(tilt_deg));
	float k = 0.95 * raw / (raw + 1.0);
	float tip_full = 1.0 / (1.0 - k);
	// 整体过渡：volume 在"平面(1×)"与"满强度 tip_full"之间线性混合，整条效果均匀展开
	tip_ratio = mix(1.0, tip_full, volume);

	// 为锥化预留宽度：绘制面在 Y 上对称加高到 tip_ratio 倍（片元里再按曲线缩放回去）
	if (tip_ratio > 1.0001) {
		float tex_h = 1.0 / TEXTURE_PIXEL_SIZE.y;
		VERTEX.y += (UV.y - 0.5) * tex_h * (tip_ratio - 1.0);
	}
}

void fragment() {
	// 按梯形重映射**宽度轴**（长度轴 UV.x 原样不动）
	// 片元抽样：uvm.y = 0.5 + (UV.y − 0.5) · stretch，stretch = tip_ratio / (1 + (tip_ratio − 1)·x)。
	// 与顶点加高（× tip_ratio）相乘后，屏幕宽度从枪口端 **1×**（= 配置宽度）**线性**长到末端 tip_ratio 倍。
	// 端点与旧的双曲律一致（1× → r×），只把中间分布从"挤在末端"改成直线摊开。
	float stretch = tip_ratio / (1.0 + (tip_ratio - 1.0) * UV.x);
	vec2 uvm = vec2(UV.x, 0.5 + (UV.y - 0.5) * stretch);

	float y_dist = abs(uvm.y - 0.5);

	float core_y_mask = 1.0 - smoothstep(0.0, core_width, y_dist);
	// 尾端：从 core_length 起渐隐到末端
	float core_tail_mask = 1.0 - smoothstep(core_length, 1.0, uvm.x);
	// 首端：从起点渐显到 core_start_length；max(...) 避免 core_start_length = 0 时 smoothstep 边界重合（未定义值），
	// 此时除数取极小值 → 结果恒为 1 = 首端不淡出（原效果）
	float core_head_mask = smoothstep(0.0, max(core_start_length, 0.0001), uvm.x);
	float core_mask = core_y_mask * core_tail_mask * core_head_mask;
	vec4 core_color = COLOR * glow_intensity;
	core_color.a = core_mask * fade;

	if (flow_strength > 0.0) {
		// 噪波随 TIME 沿 X/Y 滚动；*2 让 0.5 灰 = 原亮度（明暗两侧都保留，不整体提亮）
		vec2 flow_uv = uvm * flow_scale + TIME * flow_speed;
		float noise = texture(flow_noise, flow_uv).r * 2.0;
		core_color *= mix(1.0, noise, flow_strength);
	}

	// ── 伪体积线索（只写 rgb，不动 alpha 的形状）──
	// 近亮远暗 + 远端染色
	core_color.rgb *= mix(1.0, 1.0 - depth_falloff, UV.x);
	core_color.rgb *= mix(vec3(1.0), far_tint.rgb, UV.x);

	// 转动高光：亮带沿宽度循环扫过（smoothstep 的两侧软边）
	float highlight_pos = fract(TIME * spin_speed);
	float highlight = 1.0 - smoothstep(0.0, max(spin_width, 0.0001), abs(uvm.y - highlight_pos));
	core_color.rgb *= 1.0 + spin_strength * highlight;

	COLOR = core_color;
}
