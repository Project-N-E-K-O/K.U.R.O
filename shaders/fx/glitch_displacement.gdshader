
shader_type canvas_item;

uniform sampler2D screen_texture :
    hint_screen_texture, repeat_disable, filter_nearest;

// 总体错位强度：0 表示原画面，1 表示完整效果
uniform float intensity : hint_range(0.0, 1.0) = 0.0;

// 由 ScreenInterferenceEffect 驱动：与 InterferenceLayer 共用同一条时间轴与淡入强度。
// 不能使用内置 TIME（不随 time_scale 缩放、暂停时仍走）。
uniform float strength : hint_range(0.0, 1.0) = 1.0;
uniform float effect_time = 0.0;

// 每个时间片重新生成错位分布
uniform float update_rate : hint_range(1.0, 60.0) = 12.0;

// 水平最大偏移，占屏幕宽度的比例
uniform float max_shift : hint_range(0.0, 0.1) = 0.025;

// 条带数量
uniform float band_count : hint_range(1.0, 100.0) = 28.0;

// 发生错位的条带比例
uniform float glitch_density : hint_range(0.0, 1.0) = 0.22;

uniform float seed = 1.0;

float hash(float n) {
    return fract(sin(n * 127.1 + seed * 311.7) * 43758.5453);
}

void fragment() {
    vec2 uv = SCREEN_UV;

    // 将时间切成离散片段，让错位位置短暂保持稳定（effect_time 与 InterferenceLayer 同步）
    float tick = floor(effect_time * update_rate);

    // 当前像素所属的水平条带
    float band_id = floor(SCREEN_UV.y * band_count);

    // 每条带拥有固定的随机数
    float random_value = hash(band_id + tick * 71.0);

    // 只有部分条带会发生错位
    float active = step(1.0 - glitch_density, random_value);

    // 只绘制真正发生错位的条带：整块 quad 不透明输出会把下面的 Visuals 层覆盖掉。
    // strength≈0 时也整层透明，避免淡出/预警阶段仍然显示原画面方块。
    if (active < 0.5 || intensity * strength < 0.001)
        discard;

    // 为每条带生成左右偏移
    float direction = hash(band_id + tick * 19.0 + 5.0) * 2.0 - 1.0;
    float shift = direction * max_shift * active * intensity * strength;

    // 修改采样坐标，只改变画面显示，不改变游戏世界坐标
    uv.x = clamp(uv.x + shift, 0.001, 0.999);

    COLOR = textureLod(screen_texture, uv, 0.0);
}
