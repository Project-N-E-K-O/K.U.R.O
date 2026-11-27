shader_type canvas_item;
render_mode unshaded;

// ============================================================
// 世界空间"原地滚动"视差：节点本身不动，只滚动贴图的采样坐标。
// 由 ParallaxTextureScroll2D.cs 每帧写入：
//   · tile_scale   = 节点世界尺寸 / 一个平铺块的世界尺寸（UV 重复次数）
//   · scroll_offset = -累计滚动世界位移 / 平铺块世界尺寸（UV 单位）
// 采样前把 UV 回卷到有效区域，再处理背景首尾的接缝。
// ============================================================

uniform sampler2D pattern_texture : source_color, filter_linear_mipmap;
uniform vec2 tile_scale = vec2(1.0);
uniform vec2 scroll_offset = vec2(0.0);
// 水平自动滚动速度，单位为贴图像素/秒。正数向右，负数向左，0 停用。
uniform float scroll_speed : hint_range(-300.0, 300.0, 1.0) = 0.0;
// Sprite2D 开启 region_enabled 时：图案在整张贴图中的 UV 起点/大小（未开启时 = (0,0)/(1,1)）。
uniform vec2 region_offset = vec2(0.0);
uniform vec2 region_uv_size = vec2(1.0);
// 以贴图像素为单位，避开左、右透明边，并重叠一小段首尾素材。
uniform vec2 loop_horizontal_trim = vec2(0.0);
uniform float loop_blend_pixels : hint_range(0.0, 128.0, 1.0) = 0.0;
uniform float opacity : hint_range(0.0, 1.0, 0.01) = 1.0;
uniform vec4 tint : source_color = vec4(1.0);
uniform float tint_mix : hint_range(0.0, 1.0, 0.01) = 0.0;
varying vec4 item_color;

void vertex() {
    item_color = COLOR;
}

void fragment() {
    vec2 texture_size = max(vec2(textureSize(pattern_texture, 0)), vec2(1.0));
    vec2 region_size = max(region_uv_size * texture_size, vec2(1.0));
    // Sprite2D 的 UV 已在整张贴图坐标中，先换算为区域内的局部坐标。
    vec2 region_uv = (UV - region_offset) * texture_size / region_size;
    vec2 tile_uv = region_uv * tile_scale + scroll_offset;

    float trim_left = clamp(loop_horizontal_trim.x, 0.0, region_size.x - 1.0);
    float trim_right = clamp(loop_horizontal_trim.y, 0.0, region_size.x - trim_left - 1.0);
    float source_width = region_size.x - trim_left - trim_right;
    float blend_width = clamp(loop_blend_pixels, 0.0, (source_width - 1.0) * 0.5);
    float tile_width = source_width - blend_width;
    tile_uv.x -= fract(TIME * scroll_speed / tile_width);

    vec2 sample_position = fract(tile_uv) * vec2(tile_width, region_size.y);
    vec2 source_start = region_offset * texture_size + vec2(trim_left, 0.0);
    vec2 source_end = source_start + vec2(source_width, region_size.y);
    // 采样停在区域内侧半个像素，避免线性过滤读到透明边或相邻素材。
    vec2 texel = clamp(source_start + sample_position, source_start + 0.5, source_end - 0.5);
    vec4 ink = texture(pattern_texture, texel / texture_size);
    if (sample_position.x < blend_width) {
        float tail_x = min(source_start.x + tile_width + sample_position.x, source_end.x - 0.5);
        vec4 tail = texture(pattern_texture, vec2(tail_x, texel.y) / texture_size);
        ink = mix(tail, ink, smoothstep(0.0, blend_width, sample_position.x));
    }
    COLOR = vec4(mix(ink.rgb, tint.rgb, tint_mix), ink.a * opacity) * item_color;
}
