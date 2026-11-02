shader_type canvas_item;

uniform float radius : hint_range(0.0, 100.0) = 40.0;
uniform float thickness : hint_range(0.0, 50.0) = 10.0;
uniform vec4 color : source_color = vec4(1.0);
uniform float dash_count : hint_range(1.0, 50.0) = 10.0;
uniform float dash_ratio : hint_range(0.0, 1.0) = 0.5;
uniform float speed = 0.8;

float roundedBoxSDF(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    vec2 size = 1.0 / TEXTURE_PIXEL_SIZE;
    vec2 p = (UV - 0.5) * size;
    vec2 b = size * 0.5;
    float r = min(radius, min(b.x, b.y));
    float d = roundedBoxSDF(p, b, r);
    float border_mask = smoothstep(0.5, -0.5, abs(d + thickness * 0.5) - thickness * 0.5); 
    vec2 inner = b - r;
    float total_len = 4.0 * (inner.x + inner.y) + 2.0 * PI * r; 
    vec2 v = p; 
    vec2 q = abs(v) - inner;
    float path_pos = 0.0;
    if (q.x > 0.0 && q.y > 0.0) {
        vec2 dir = sign(v);
        vec2 center_offset = dir * inner;
        vec2 rel = v - center_offset;
        float angle = 0.0;
        if (v.x > 0.0 && v.y < 0.0) {
            angle = atan(rel.x, -rel.y);
            path_pos = inner.x + angle * r;
        } else if (v.x > 0.0 && v.y > 0.0) {
            angle = atan(rel.y, rel.x);
            path_pos = inner.x + (PI * 0.5 * r) + inner.y * 2.0 + angle * r;
        } else if (v.x < 0.0 && v.y > 0.0) {
            angle = atan(-rel.x, rel.y);
            path_pos = inner.x + (PI * 1.0 * r) + inner.y * 2.0 + inner.x * 2.0 + angle * r;
        } else {
            angle = atan(-rel.y, -rel.x);
            path_pos = inner.x + (PI * 1.5 * r) + inner.y * 2.0 + inner.x * 2.0 + inner.y * 2.0 + angle * r;
        }
    } else {
        if (v.y <= -inner.y) {
             if (v.x >= 0.0) path_pos = v.x;
             else path_pos = total_len + v.x;
        } 
        else if (v.x >= inner.x) path_pos = inner.x + (PI * 0.5 * r) + (v.y + inner.y);
        else if (v.y >= inner.y) path_pos = inner.x + (PI * 1.0 * r) + inner.y * 2.0 + (inner.x - v.x);
        else if (v.x <= -inner.x) path_pos = inner.x + (PI * 1.5 * r) + inner.x * 2.0 + inner.y * 2.0 + (inner.y - v.y);
    }
    float normalized_path = path_pos / total_len;
    float segment = 1.0 / max(1.0, dash_count);
    float dash_offset = TIME * (speed * 0.1); 
    float dash_mod = mod(normalized_path + dash_offset, segment) / segment;
    float dash_mask = step(dash_mod, dash_ratio);
    COLOR = vec4(color.rgb, color.a * border_mask * dash_mask);
}