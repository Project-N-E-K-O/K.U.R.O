shader_type canvas_item;
render_mode unshaded;

// ============================================================
// 卡通云朵转场（全屏 canvas_item 着色器）
// 用一排相互交叠的圆弧拼出"扇贝/云朵"边界，随 progress 扫过屏幕：
//   · departing = false（进场）：云从右侧进场、向左铺满全屏，边界是竖线；
//   · departing = true（退场）：云边界自下而上撤离，逐步露出画面，边界是横线。
// 多层（不同 layer_seed / layer_offset）叠起来，就是分层、错速的卡通云。
// UV 按"全屏 0..1"使用；p.x 乘了屏幕宽高比，所以任意分辨率下云弧都是正圆。
// ============================================================

// 转场进度：进场 0 = 未覆盖、1 = 完全覆盖；退场则相反（0 盖住、1 露出）
uniform float progress : hint_range(0.0, 1.0) = 0.0;
// true = 退场（露出画面）；false = 进场（覆盖画面）
uniform bool departing = false;
uniform float cloud_opacity : hint_range(0.0, 1.0) = 1.0;
// 每层的随机种子：决定云弧相位与各格半径差异（同一层内稳定）
uniform float layer_seed = 0.0;
// 层与层的固定错位，占移动方向视口长度的比例，整个动画期间保持不变。
// 有效范围 ±1（超出部分在下面 clamp 掉）；离屏余量已按 ±1 预留。
uniform float layer_offset : hint_range(-1, 1, 0.005) = 0.0;
// 单个云弧的半径（以屏幕高度为单位的 UV 尺度）
uniform float radius = 0.16;
// 云格网格随进度整体漂移的量：各层配不同值，转场时云会"流"起来
uniform float seed_drift : hint_range(-10.0, 10.0, 0.01) = 0.7;
// 运动过程中圆弧半径的"呼吸"幅度（两端为 0，中段最大；上限 0.2，再大云格之间会露缝）
uniform float radius_breath : hint_range(0.0, 0.2, 0.005) = 0.12;
// 云体填充色
uniform vec4 cloud_color : source_color = vec4(0.88, 0.9, 0.91, 1.0);
// 云边缘描边色（卡通勾线）
uniform vec4 outline_color : source_color = vec4(0.24, 0.27, 0.31, 1.0);

float arc_height(float cell, float offset, float base_radius, float seed) {
    float r = base_radius * (1.0 + 0.12 * sin(cell * 2.37 + seed));
    return sqrt(max(0.0, r * r - offset * offset));
}

void fragment() {
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = UV * vec2(aspect, 1.0);

    float tangent = departing ? p.x : p.y;

    float motion_envelope = 4.0 * progress * (1.0 - progress);
    float phase = layer_seed * TAU;

    float moving_seed = layer_seed + seed_drift * (departing ? -1.0 : 1.0)
        * (progress + 0.12 * motion_envelope * sin(progress * TAU + phase));

    float animated_radius = radius * (1.0 + radius_breath * motion_envelope
        * sin(progress * TAU * 1.5 + phase));

    float spacing = radius * 1.55;
    float cells = tangent / spacing + moving_seed;
    float cell = floor(cells);                       
    float local = (fract(cells) - 0.5) * spacing;     

    float scallop = arc_height(cell, local, animated_radius, layer_seed);
    scallop = max(scallop, arc_height(cell - 1.0, local + spacing, animated_radius, layer_seed));
    scallop = max(scallop, arc_height(cell + 1.0, local - spacing, animated_radius, layer_seed));

    float edge;    
    float inside;  

    // 各层共用运动基线，离屏余量容纳偏移和圆弧。
    // padding 里的 1.0 = 为 layer_offset 的 ±1 预留的一个整视口。
    float arc_padding = max(0.35, radius * 1.2);
    float offset = clamp(layer_offset, -1.0, 1.0);

    if (departing) {
        float padding = 1.0 + arc_padding;
        edge = mix(1.0 + padding, -padding, progress) + scallop - offset;
        inside = edge - p.y;
    } else {
        float padding = 1.0 * aspect + arc_padding;
        edge = mix(aspect + padding, -padding, progress) - scallop + offset * aspect;
        inside = p.x - edge;
    }

    float aa = max(fwidth(inside), 0.0005);
    float alpha = smoothstep(-aa, aa, inside);
    float ink = smoothstep(0.004, 0.004 + aa, inside);
    COLOR = vec4(mix(outline_color.rgb, cloud_color.rgb, ink), alpha * cloud_opacity);
}
