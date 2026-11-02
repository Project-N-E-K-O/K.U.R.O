shader_type canvas_item;
render_mode blend_mix, unshaded;

// SDF border from dashed_line, clipped by source alpha. Source RGB is hidden.
// Geometry is explicit: alpha alone cannot provide an arbitrary shape's SDF.
// Existing 512px assets: pipe_width=96, center_uv=(0.5,0.5).
// Elbow canonical direction is LEFT -> TOP. quarter_turns rotates the shape.
// Polyline offers arbitrary routes with constant width and round joins.
uniform int shape : hint_enum("Straight", "Elbow90", "Polyline") = 0;
uniform int quarter_turns : hint_range(0, 3, 1) = 0;
uniform vec2 center_uv = vec2(0.5);
uniform float pipe_width : hint_range(1.0, 512.0, 0.5) = 96.0;
uniform float radius : hint_range(0.0, 256.0, 0.5) = 60.0;
uniform float thickness : hint_range(0.0, 50.0, 0.5) = 24.0;
uniform vec4 color : source_color = vec4(0.23, 1.0, 0.08, 1.0);
uniform float dash_count : hint_range(1.0, 100.0, 1.0) = 10.0;
uniform float dash_ratio : hint_range(0.0, 1.0, 0.01) = 0.5;
uniform float speed = 0.8;
uniform float brightness : hint_range(0.0, 4.0, 0.05) = 1.0;
uniform float phase_offset = 0.0;
// false: both sides move in supply direction. true: out on one side, back on
// the other, like dashed_line. This is a longitudinal loop, without end caps.
uniform bool contour_loop = true;
// Zero uses dash_count. Nonzero specifies spacing in source pixels.
uniform float dash_period_px : hint_range(0.0, 512.0, 1.0) = 0.0;
// Polyline points are ORIGINAL texture UV in travel order; rotation is ignored.
uniform int path_point_count : hint_range(2, 8, 1) = 4;
uniform vec2 path_0 = vec2(0.0, 0.75);
uniform vec2 path_1 = vec2(0.25, 0.75);
uniform vec2 path_2 = vec2(0.25, 0.25);
uniform vec2 path_3 = vec2(1.0, 0.25);
uniform vec2 path_4 = vec2(1.0, 0.25);
uniform vec2 path_5 = vec2(1.0, 0.25);
uniform vec2 path_6 = vec2(1.0, 0.25);
uniform vec2 path_7 = vec2(1.0, 0.25);
varying vec4 sprite_tint;
void vertex() { sprite_tint = COLOR; }

// Route data: distance to centerline, along-distance, total length, side.
vec4 elbow_route(vec2 p, vec2 c) {
    float r = clamp(radius, 0.001, max(min(c.x, c.y), 0.001));
    vec2 origin = c - vec2(r);
    vec2 rel = p - origin;
    float left_length = max(origin.x, 0.0);
    float arc_length = PI * 0.5 * r;
    float total = max(left_length + arc_length + origin.y, 1.0);
    // Extend tile ends: no cap stripe across a connection to another sprite.
    vec2 nearest = vec2(min(p.x, origin.x), c.y);
    float best = length(p - nearest);
    float along = nearest.x;
    float side = p.y - nearest.y;
    float angle = 0.0;
    if (dot(rel, rel) > 0.000001) {
        angle = clamp(atan(rel.x, rel.y), 0.0, PI * 0.5);
    }
    vec2 normal = vec2(sin(angle), cos(angle));
    nearest = origin + normal * r;
    float candidate = length(p - nearest);
    if (candidate < best) {
        best = candidate;
        along = left_length + angle * r;
        side = dot(p - nearest, normal);
    }
    nearest = vec2(c.x, min(p.y, origin.y));
    candidate = length(p - nearest);
    if (candidate < best) {
        best = candidate;
        along = left_length + arc_length + origin.y - nearest.y;
        side = p.x - nearest.x;
    }
    return vec4(best, along, total, side);
}

vec4 polyline_route(vec2 p, vec2 size) {
    vec2 points[8] = {path_0,path_1,path_2,path_3,path_4,path_5,path_6,path_7};
    float best = 1e30;
    float along = 0.0;
    float total = 0.0;
    float side = 0.0;
    for (int i = 0; i < 7; i++) {
        if (i < path_point_count - 1) {
            vec2 a = points[i] * size;
            vec2 ab = (points[i+1] - points[i]) * size;
            float len = length(ab);
            if (len > 0.001) {
                float t = clamp(dot(p-a,ab)/(len*len),0.0,1.0);
                vec2 delta = p-a-t*ab;
                float candidate = length(delta);
                if (candidate < best) {
                    best = candidate;
                    along = total+t*len;
                    side = dot(delta,vec2(-ab.y,ab.x))/len;
                }
            }
            total += len;
        }
    }
    return vec4(best,along,max(total,1.0),side);
}

void fragment() {
    float source_alpha = texture(TEXTURE,UV).a;
    vec2 size = 1.0/TEXTURE_PIXEL_SIZE;
    vec2 uv = UV;
    vec2 center = center_uv;
    if (quarter_turns == 1) {
        uv = vec2(UV.y,1.0-UV.x);
        center = vec2(center_uv.y,1.0-center_uv.x);
        size = size.yx;
    } else if (quarter_turns == 2) {
        uv = vec2(1.0)-UV;
        center = vec2(1.0)-center_uv;
    } else if (quarter_turns == 3) {
        uv = vec2(1.0-UV.y,UV.x);
        center = vec2(1.0-center_uv.y,center_uv.x);
        size = size.yx;
    }
    vec2 p = uv*size;
    vec2 c = center*size;
    vec4 route = vec4(abs(p.y-c.y),p.x,max(size.x,1.0),p.y-c.y);
    if (shape == 1) {
        route = elbow_route(p,c);
    } else if (shape == 2) {
        route = polyline_route(UV/TEXTURE_PIXEL_SIZE,1.0/TEXTURE_PIXEL_SIZE);
    }
    // Same inward border SDF as dashed_line; defined smoothstep ordering.
    float width = min(thickness,pipe_width*0.5);
    float d = route.x-pipe_width*0.5;
    float border_distance = abs(d+width*0.5)-width*0.5;
    float aa = max(fwidth(d)*0.5,0.5);
    float border = 1.0-smoothstep(-aa,aa,border_distance);
    float along = route.y;
    float total = route.z;
    if (contour_loop) {
        along = route.w >= 0.0 ? along : 2.0*total-along;
        total *= 2.0;
    }
    float count = max(dash_count,1.0);
    float period = dash_period_px > 0.0 ? max(dash_period_px,1.0) : total/count;
    float cycles = (along+TIME*speed*0.1*total+phase_offset*total)/period;
    float dash_aa = clamp(fwidth(along/period),0.001,0.25);
    float dash_distance = abs(fract(cycles)-0.5)-dash_ratio*0.5;
    float dash = 1.0-smoothstep(-dash_aa,dash_aa,dash_distance);
    if (dash_ratio <= 0.0) { dash = 0.0; }
    else if (dash_ratio >= 1.0) { dash = 1.0; }
    float alpha = source_alpha*border*dash*color.a*sprite_tint.a;
    alpha *= step(0.001,thickness);
    COLOR = vec4(color.rgb*brightness*sprite_tint.rgb,alpha);
}